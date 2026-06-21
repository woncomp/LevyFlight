using LevyFlight.TreeSitter;
using Microsoft.VisualStudio.Imaging;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;

namespace LevyFlight
{
    /// <summary>
    /// Parses C++ source files using tree-sitter and extracts function definitions.
    /// </summary>
    /// <remarks>
    /// ═══════════════════════════════════════════════════════════════════════
    ///  KNOWN TREE-SITTER C++ PITFALLS  (collected during initial development)
    /// ═══════════════════════════════════════════════════════════════════════
    ///
    /// 1. REFERENCE / POINTER DECLARATORS — child_by_field_name("declarator") may return null
    ///    For nodes like reference_declarator or pointer_declarator, the inner declarator
    ///    is sometimes NOT exposed as a named "declarator" field.  You must fall back to
    ///    scanning children by node type (function_declarator, identifier, qualified_identifier, …).
    ///    Example: void foo(int &amp;writeIndent) — the &amp; lived inside a reference_declarator
    ///    whose field lookup returned null.
    ///
    /// 2. MACRO INVOCATIONS LOOK LIKE FUNCTIONS
    ///    A macro call such as INDENT_SCOPE() { … } is parsed by tree-sitter as a
    ///    function_definition.  The key difference is that a real function has a "type" field
    ///    (its return type), while a macro-generated false positive does not.
    ///    Always check: node.ChildByFieldName("type").is_null() to filter these out.
    ///
    /// 3. NESTED "FUNCTION DEFINITIONS" INSIDE FUNCTION BODIES
    ///    C++ does not allow nested function definitions, but tree-sitter can misparse
    ///    certain constructs (e.g. local struct literals, statement-expression macros) as
    ///    function_definition nodes inside an existing function body.
    ///    Use an "insideFunction" flag to suppress these spurious matches.
    ///
    /// 4. insideFunction FLAG SUPPRESSES CLASS/STRUCT MEMBER FUNCTIONS
    ///    When a class or struct is defined inside a function body (or at any nesting level),
    ///    its member functions are legitimate — but the insideFunction flag would wrongly
    ///    suppress them.  Reset insideFunction = false when entering a class_specifier or
    ///    struct_specifier scope.
    ///
    /// 5. CONSTRUCTORS / DESTRUCTORS HAVE NO RETURN TYPE
    ///    The "type" field check (pitfall #2) also rejects constructors and destructors,
    ///    because they genuinely lack a return type.  You must carve out an exception via
    ///    IsSpecialMemberFunction(), which validates:
    ///      • In-class constructors:  identifier matching the enclosing class name
    ///      • In-class destructors:   destructor_name node
    ///      • In-class operators:     operator_name or operator_cast node
    ///
    /// 6. OUT-OF-CLASS CONSTRUCTORS / DESTRUCTORS
    ///    Definitions like  ClassName::ClassName(…) { … }  appear as a qualified_identifier
    ///    inside a function_declarator.  You must split on "::" and check whether the last
    ///    component matches the preceding scope component (constructor) or is ~ScopeName
    ///    (destructor).  Example: CodeGen_Text::CodeGen_Text  or  CodeGen_Text::~CodeGen_Text.
    ///
    /// 7. OUT-OF-CLASS OPERATOR DEFINITIONS — MULTIPLE TREE SHAPES
    ///    Operators defined outside their class can appear in at least three different
    ///    tree-sitter shapes:
    ///      a) declarator = function_declarator whose inner declarator is a qualified_identifier
    ///         containing "operator"  (e.g. ClassName::operator==)
    ///      b) declarator = qualified_identifier directly  (e.g. internal::IndentScope::operator bool)
    ///      c) NO declarator field at all — the qualified_identifier (or operator_cast) is a
    ///         direct child of the function_definition node.
    ///    All three must be handled in both IsSpecialMemberFunction() and GetFunctionName().
    ///
    /// 8. GetFunctionName FALLBACK WHEN declarator FIELD IS MISSING
    ///    Some function_definition nodes have no "declarator" named field (see pitfall #7c).
    ///    In that case, scan the direct children of the function_definition for a
    ///    qualified_identifier or operator_cast node and use its text.
    /// ═══════════════════════════════════════════════════════════════════════
    /// </remarks>
    internal static class TreeSitterCodeParser
    {
        public static async Task<List<JumpItem>> ParseAndListFunctionsAsync(string filePath)
        {
            return await Task.Run(() =>
            {
                var results = new List<JumpItem>();
                try
                {
                    string sourceText = File.ReadAllText(filePath);
                    var tree = TreeSitterParser.Parse(sourceText);
                    var root = tree.Root;
                    var scopeStack = new List<string>();
                    CollectFunctions(root, filePath, scopeStack, results);

                    TreeSitterDiagnostics.SaveParse(filePath, sourceText, tree, "QuickOpen", TreeSitterParser.CurrentEngineName);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine("[TreeSitter] Error parsing document: " + ex.Message);
                }
                return results;
            });
        }

        private static void CollectFunctions(SyntaxNode node, string filePath, List<string> scopeStack, List<JumpItem> results, bool insideFunction = false, string currentClassName = null)
        {
            string nodeType = node.Type;

            // Track enclosing scopes: namespace, class, struct
            bool pushedScope = false;
            if (nodeType == "namespace_definition" || nodeType == "class_specifier" || nodeType == "struct_specifier")
            {
                var nameNode = node.ChildByFieldName("name");
                string scopeName = !nameNode.IsNull ? nameNode.Text : "<anonymous>";
                scopeStack.Add(scopeName);
                pushedScope = true;

                // Member functions inside a class/struct are valid, so reset the function flag
                // and track the class/struct name for constructor/destructor validation
                if (nodeType == "class_specifier" || nodeType == "struct_specifier")
                {
                    insideFunction = false;
                    currentClassName = scopeName;
                }
            }

            if (nodeType == "function_definition")
            {
                // C++ doesn't allow nested function definitions — skip anything
                // tree-sitter misparses inside an existing function body.
                if (insideFunction)
                    goto recurse;

                // Check if this function has a return type
                var typeNode = node.ChildByFieldName("type");

                // If no type field, check if it's a special member function (constructor/destructor/operator)
                if (typeNode.IsNull)
                {
                    if (!IsSpecialMemberFunction(node, currentClassName))
                        goto recurse;
                }

                string funcName = GetFunctionName(node);

                // If the declarator already has a qualified name (contains "::"), use it as-is.
                // Otherwise, prepend the enclosing scope stack.
                if (!funcName.Contains("::") && scopeStack.Count > 0)
                {
                    funcName = string.Join("::", scopeStack) + "::" + funcName;
                }

                // Build display name: "returnType funcName(param1, param2)"
                string returnType = GetReturnType(node);
                string paramList = GetParameterList(node);
                string displayName = (!string.IsNullOrEmpty(returnType) ? returnType + " " : "") + funcName + "(" + paramList + ")";

                var start = node.Start;
                var jumpItem = new JumpItem(Category.TreeSitter, filePath);
                jumpItem.Name = displayName;
                jumpItem.SetPosition((int)start.Row + 1, (int)start.Column);
                jumpItem.IconMoniker = KnownMonikers.MethodPublic;
                results.Add(jumpItem);
                insideFunction = true;
            }

            // Function declarations (prototypes) — e.g. "void foo(int);" at namespace/class scope
            if (nodeType == "declaration" && !insideFunction)
            {
                var declarator = node.ChildByFieldName("declarator");
                if (!declarator.IsNull && declarator.Type == "function_declarator")
                {
                    string returnType = GetReturnType(node);
                    string funcName = ExtractDeclaratorName(declarator);

                    if (!funcName.Contains("::") && scopeStack.Count > 0)
                    {
                        funcName = string.Join("::", scopeStack) + "::" + funcName;
                    }

                    string paramList = GetParameterList(node);
                    string displayName = (!string.IsNullOrEmpty(returnType) ? returnType + " " : "") + funcName + "(" + paramList + ")";

                    var start = node.Start;
                    var jumpItem = new JumpItem(Category.TreeSitter, filePath);
                    jumpItem.Name = displayName;
                    jumpItem.SetPosition((int)start.Row + 1, (int)start.Column);
                    jumpItem.IconMoniker = KnownMonikers.Procedure;
                    jumpItem.IsDeclaration = true;
                    results.Add(jumpItem);
                }
            }

            recurse:
            int childCount = node.Children.Count;
            for (int i = 0; i < childCount; i++)
            {
                CollectFunctions(node.Children[i], filePath, scopeStack, results, insideFunction, currentClassName);
            }

            if (pushedScope)
            {
                scopeStack.RemoveAt(scopeStack.Count - 1);
            }
        }

        internal static bool IsSpecialMemberFunction(SyntaxNode funcDefNode, string className)
        {
            // Check the declarator field first
            var declarator = funcDefNode.ChildByFieldName("declarator");

            // If no declarator field, scan direct children for qualified_identifier or operator_cast
            // This handles out-of-class operator definitions like:
            //   internal::IndentScope::operator bool() const { ... }
            // where tree-sitter puts the whole thing into a qualified_identifier with no declarator.
            if (declarator.IsNull)
            {
                for (int i = 0; i < funcDefNode.Children.Count; i++)
                {
                    var child = funcDefNode.Children[i];
                    string childType = child.Type;

                    if (childType == "operator_cast")
                        return true;

                    if (childType == "qualified_identifier")
                    {
                        string text = child.Text;
                        if (text.Contains("operator"))
                            return true;

                        // Check for constructor/destructor pattern
                        int lastSep = text.LastIndexOf("::");
                        if (lastSep > 0)
                        {
                            string scope = text.Substring(0, lastSep);
                            string funcName = text.Substring(lastSep + 2);
                            int prevSep = scope.LastIndexOf("::");
                            string scopeName = prevSep >= 0 ? scope.Substring(prevSep + 2) : scope;
                            if (funcName == scopeName)
                                return true;
                            if (funcName.StartsWith("~") && funcName.Substring(1) == scopeName)
                                return true;
                        }
                    }
                }
                return false;
            }

            string declType = declarator.Type;

            // Operator overloads: operator_cast
            if (declType == "operator_cast")
                return true;

            // qualified_identifier as declarator (e.g., internal::IndentScope::operator bool)
            if (declType == "qualified_identifier")
            {
                string text = declarator.Text;
                if (text.Contains("operator"))
                    return true;

                int lastSep = text.LastIndexOf("::");
                if (lastSep > 0)
                {
                    string scope = text.Substring(0, lastSep);
                    string funcName = text.Substring(lastSep + 2);
                    int prevSep = scope.LastIndexOf("::");
                    string scopeName = prevSep >= 0 ? scope.Substring(prevSep + 2) : scope;
                    if (funcName == scopeName)
                        return true;
                    if (funcName.StartsWith("~") && funcName.Substring(1) == scopeName)
                        return true;
                }
            }

            // For function_declarator, check the inner declarator
            if (declType == "function_declarator")
            {
                var inner = declarator.ChildByFieldName("declarator");
                if (!inner.IsNull)
                {
                    string innerType = inner.Type;

                    // Destructor: destructor_name
                    if (innerType == "destructor_name")
                        return true;

                    // Operator: operator_name, template_function with operator, etc.
                    if (innerType == "operator_name")
                        return true;

                    // Constructor: identifier matching class name (in-class definition)
                    if (innerType == "identifier" && className != null)
                    {
                        string name = inner.Text;
                        if (name == className)
                            return true;
                    }

                    // Constructor/Destructor: qualified_identifier (out-of-class definition)
                    // e.g., ClassName::ClassName or ClassName::~ClassName
                    if (innerType == "qualified_identifier")
                    {
                        string qualifiedName = inner.Text;

                        // Operator overload: contains "operator"
                        // e.g., "ClassName::operator==" or "ClassName::operator bool"
                        if (qualifiedName.Contains("operator"))
                            return true;

                        // Check if it's a constructor: last two parts are the same
                        // e.g., "CodeGen_Text::CodeGen_Text"
                        int lastSep = qualifiedName.LastIndexOf("::");
                        if (lastSep > 0)
                        {
                            string scope = qualifiedName.Substring(0, lastSep);
                            string funcName = qualifiedName.Substring(lastSep + 2);

                            // Constructor: ClassName::ClassName
                            int prevSep = scope.LastIndexOf("::");
                            string className2 = prevSep >= 0 ? scope.Substring(prevSep + 2) : scope;
                            if (funcName == className2)
                                return true;

                            // Destructor: ClassName::~ClassName
                            if (funcName.StartsWith("~") && funcName.Substring(1) == className2)
                                return true;
                        }
                    }
                }
            }

            return false;
        }

        /// <summary>
        /// Extracts the return type text from a function_definition node.
        /// Returns empty string for constructors/destructors (no return type).
        /// </summary>
        internal static string GetReturnType(SyntaxNode funcDefNode)
        {
            var typeNode = funcDefNode.ChildByFieldName("type");
            if (!typeNode.IsNull)
                return typeNode.Text.Trim();
            return "";
        }

        /// <summary>
        /// Extracts a comma-separated list of parameter type names from a function_definition.
        /// Drills into the declarator to find the parameter_list.
        /// </summary>
        internal static string GetParameterList(SyntaxNode funcDefNode)
        {
            // Find the function_declarator (may be wrapped in pointer/reference declarators)
            var declarator = funcDefNode.ChildByFieldName("declarator");
            var funcDecl = FindFunctionDeclarator(declarator);
            if (funcDecl.IsNull)
            {
                // Some out-of-class definitions have no declarator field;
                // scan direct children for a function_declarator
                for (int i = 0; i < funcDefNode.Children.Count; i++)
                {
                    var child = funcDefNode.Children[i];
                    funcDecl = FindFunctionDeclarator(child);
                    if (!funcDecl.IsNull)
                        break;
                }
            }
            if (funcDecl.IsNull)
                return "";

            var parameters = funcDecl.ChildByFieldName("parameters");
            if (parameters.IsNull)
                return "";

            var paramNames = new List<string>();
            for (int i = 0; i < parameters.Children.Count; i++)
            {
                var param = parameters.Children[i];
                string paramType = param.Type;

                if (paramType == "parameter_declaration" || paramType == "optional_parameter_declaration")
                {
                    var paramTypeNode = param.ChildByFieldName("type");
                    if (!paramTypeNode.IsNull)
                        paramNames.Add(paramTypeNode.Text.Trim());
                    else
                        paramNames.Add(param.Text.Trim());
                }
                else if (paramType == "variadic_parameter_declaration")
                {
                    paramNames.Add("...");
                }
            }
            return string.Join(", ", paramNames);
        }

        /// <summary>
        /// Recursively unwraps pointer/reference declarators to find the function_declarator node.
        /// </summary>
        private static SyntaxNode FindFunctionDeclarator(SyntaxNode node)
        {
            if (node.IsNull)
                return node;
            string type = node.Type;
            if (type == "function_declarator")
                return node;
            if (type == "pointer_declarator" || type == "reference_declarator")
            {
                var inner = node.ChildByFieldName("declarator");
                if (!inner.IsNull)
                    return FindFunctionDeclarator(inner);
                // Field lookup failed — scan children
                for (int i = 0; i < node.Children.Count; i++)
                {
                    var result = FindFunctionDeclarator(node.Children[i]);
                    if (!result.IsNull)
                        return result;
                }
            }
            return SyntaxNode.Null;
        }

        internal static string GetFunctionName(SyntaxNode funcDefNode)
        {
            // The "declarator" field of a function_definition holds the function declarator
            var declarator = funcDefNode.ChildByFieldName("declarator");
            if (!declarator.IsNull)
            {
                // Drill into nested declarators (e.g. pointer_declarator, reference_declarator)
                // until we find the actual name or qualified_identifier
                return ExtractDeclaratorName(declarator);
            }

            // No declarator field — scan children for qualified_identifier or operator_cast
            // This handles out-of-class operators where tree-sitter puts everything into
            // a qualified_identifier as a direct child of function_definition
                for (int i = 0; i < funcDefNode.Children.Count; i++)
                {
                    var child = funcDefNode.Children[i];
                string childType = child.Type;
                if (childType == "qualified_identifier" || childType == "operator_cast")
                {
                    return child.Text;
                }
            }

            return "<unknown>";
        }

        internal static string ExtractDeclaratorName(SyntaxNode declarator)
        {
            string type = declarator.Type;

            // function_declarator -> look at the "declarator" field for the name
            if (type == "function_declarator")
            {
                var inner = declarator.ChildByFieldName("declarator");
                if (!inner.IsNull)
                    return ExtractDeclaratorName(inner);
            }

            // pointer_declarator, reference_declarator -> unwrap
            // The inner declarator may not be a named field, so fall back to
            // searching children for a recognizable declarator node type.
            if (type == "pointer_declarator" || type == "reference_declarator")
            {
                var inner = declarator.ChildByFieldName("declarator");
                if (!inner.IsNull)
                    return ExtractDeclaratorName(inner);

                // Field lookup failed — scan children for the nested declarator
                for (int i = 0; i < declarator.Children.Count; i++)
                {
                    var child = declarator.Children[i];
                    string childType = child.Type;
                    if (childType == "function_declarator" || childType == "pointer_declarator"
                        || childType == "reference_declarator" || childType == "identifier"
                        || childType == "qualified_identifier" || childType == "destructor_name"
                        || childType == "template_function" || childType == "operator_name"
                        || childType == "parenthesized_declarator")
                    {
                        return ExtractDeclaratorName(child);
                    }
                }
            }

            // For identifiers, qualified_identifier, destructor_name, template_function, operator_name etc.
            // just return the source text of the node
            return declarator.Text;
        }

        public static void PrintTree(SyntaxNode node, int depth)
        {
            string indent = new string(' ', depth * 2);
            string nodeType = node.Type;

            // For small nodes, show the text content
            string content = "";
            if (node.Text.Length < 40)
            {
                content = node.Text.Replace("\n", "\\n").Replace("\r", "");
                if (content.Length > 40) content = content.Substring(0, 37) + "...";
                content = $" \"{content}\"";
            }

            var start = node.Start;
            Debug.WriteLine($"{indent}{nodeType}  (line {start.Row + 1}){content}");

            int childCount = node.Children.Count;
            for (int i = 0; i < childCount; i++)
            {
                PrintTree(node.Children[i], depth + 1);
            }
        }
    }
}






