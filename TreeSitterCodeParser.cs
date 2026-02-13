using GitHub.TreeSitter;
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
    ///    Always check: node.child_by_field_name("type").is_null() to filter these out.
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
                    using (var parser = new TSParser())
                    using (var lang = TSParser.CppLanguage())
                    {
                        parser.set_language(lang);
                        using (var tree = parser.parse_string(null, sourceText))
                        {
                            if (tree == null)
                                return results;

                            var root = tree.root_node();
                            var scopeStack = new List<string>();
                            CollectFunctions(root, sourceText, filePath, scopeStack, results);
                        }
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine("[TreeSitter] Error parsing document: " + ex.Message);
                }
                return results;
            });
        }

        private static void CollectFunctions(TSNode node, string sourceText, string filePath, List<string> scopeStack, List<JumpItem> results, bool insideFunction = false, string currentClassName = null)
        {
            string nodeType = node.type();

            // Track enclosing scopes: namespace, class, struct
            bool pushedScope = false;
            if (nodeType == "namespace_definition" || nodeType == "class_specifier" || nodeType == "struct_specifier")
            {
                var nameNode = node.child_by_field_name("name");
                string scopeName = !nameNode.is_null() ? nameNode.text(sourceText) : "<anonymous>";
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
                var typeNode = node.child_by_field_name("type");

                // If no type field, check if it's a special member function (constructor/destructor/operator)
                if (typeNode.is_null())
                {
                    if (!IsSpecialMemberFunction(node, sourceText, currentClassName))
                        goto recurse;
                }

                string funcName = GetFunctionName(node, sourceText);

                // If the declarator already has a qualified name (contains "::"), use it as-is.
                // Otherwise, prepend the enclosing scope stack.
                if (!funcName.Contains("::") && scopeStack.Count > 0)
                {
                    funcName = string.Join("::", scopeStack) + "::" + funcName;
                }

                // Build display name: "returnType funcName(param1, param2)"
                string returnType = GetReturnType(node, sourceText);
                string paramList = GetParameterList(node, sourceText);
                string displayName = (!string.IsNullOrEmpty(returnType) ? returnType + " " : "") + funcName + "(" + paramList + ")";

                var start = node.start_point();
                var jumpItem = new JumpItem(Category.TreeSitter, filePath);
                jumpItem.Name = displayName;
                jumpItem.SetPosition((int)start.row + 1, (int)start.column);
                jumpItem.IconMoniker = KnownMonikers.MethodPublic;
                results.Add(jumpItem);
                insideFunction = true;
            }

            // Function declarations (prototypes) — e.g. "void foo(int);" at namespace/class scope
            if (nodeType == "declaration" && !insideFunction)
            {
                var declarator = node.child_by_field_name("declarator");
                if (!declarator.is_null() && declarator.type() == "function_declarator")
                {
                    string returnType = GetReturnType(node, sourceText);
                    string funcName = ExtractDeclaratorName(declarator, sourceText);

                    if (!funcName.Contains("::") && scopeStack.Count > 0)
                    {
                        funcName = string.Join("::", scopeStack) + "::" + funcName;
                    }

                    string paramList = GetParameterList(node, sourceText);
                    string displayName = (!string.IsNullOrEmpty(returnType) ? returnType + " " : "") + funcName + "(" + paramList + ")";

                    var start = node.start_point();
                    var jumpItem = new JumpItem(Category.TreeSitter, filePath);
                    jumpItem.Name = displayName;
                    jumpItem.SetPosition((int)start.row + 1, (int)start.column);
                    jumpItem.IconMoniker = KnownMonikers.Procedure;
                    jumpItem.IsDeclaration = true;
                    results.Add(jumpItem);
                }
            }

            recurse:
            uint childCount = node.child_count();
            for (uint i = 0; i < childCount; i++)
            {
                CollectFunctions(node.child(i), sourceText, filePath, scopeStack, results, insideFunction, currentClassName);
            }

            if (pushedScope)
            {
                scopeStack.RemoveAt(scopeStack.Count - 1);
            }
        }

        internal static bool IsSpecialMemberFunction(TSNode funcDefNode, string sourceText, string className)
        {
            // Check the declarator field first
            var declarator = funcDefNode.child_by_field_name("declarator");

            // If no declarator field, scan direct children for qualified_identifier or operator_cast
            // This handles out-of-class operator definitions like:
            //   internal::IndentScope::operator bool() const { ... }
            // where tree-sitter puts the whole thing into a qualified_identifier with no declarator.
            if (declarator.is_null())
            {
                for (uint i = 0; i < funcDefNode.child_count(); i++)
                {
                    var child = funcDefNode.child(i);
                    string childType = child.type();

                    if (childType == "operator_cast")
                        return true;

                    if (childType == "qualified_identifier")
                    {
                        string text = child.text(sourceText);
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

            string declType = declarator.type();

            // Operator overloads: operator_cast
            if (declType == "operator_cast")
                return true;

            // qualified_identifier as declarator (e.g., internal::IndentScope::operator bool)
            if (declType == "qualified_identifier")
            {
                string text = declarator.text(sourceText);
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
                var inner = declarator.child_by_field_name("declarator");
                if (!inner.is_null())
                {
                    string innerType = inner.type();

                    // Destructor: destructor_name
                    if (innerType == "destructor_name")
                        return true;

                    // Operator: operator_name, template_function with operator, etc.
                    if (innerType == "operator_name")
                        return true;

                    // Constructor: identifier matching class name (in-class definition)
                    if (innerType == "identifier" && className != null)
                    {
                        string name = inner.text(sourceText);
                        if (name == className)
                            return true;
                    }

                    // Constructor/Destructor: qualified_identifier (out-of-class definition)
                    // e.g., ClassName::ClassName or ClassName::~ClassName
                    if (innerType == "qualified_identifier")
                    {
                        string qualifiedName = inner.text(sourceText);

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
        internal static string GetReturnType(TSNode funcDefNode, string sourceText)
        {
            var typeNode = funcDefNode.child_by_field_name("type");
            if (!typeNode.is_null())
                return typeNode.text(sourceText).Trim();
            return "";
        }

        /// <summary>
        /// Extracts a comma-separated list of parameter type names from a function_definition.
        /// Drills into the declarator to find the parameter_list.
        /// </summary>
        internal static string GetParameterList(TSNode funcDefNode, string sourceText)
        {
            // Find the function_declarator (may be wrapped in pointer/reference declarators)
            var declarator = funcDefNode.child_by_field_name("declarator");
            var funcDecl = FindFunctionDeclarator(declarator);
            if (funcDecl.is_null())
            {
                // Some out-of-class definitions have no declarator field;
                // scan direct children for a function_declarator
                for (uint i = 0; i < funcDefNode.child_count(); i++)
                {
                    var child = funcDefNode.child(i);
                    funcDecl = FindFunctionDeclarator(child);
                    if (!funcDecl.is_null())
                        break;
                }
            }
            if (funcDecl.is_null())
                return "";

            var parameters = funcDecl.child_by_field_name("parameters");
            if (parameters.is_null())
                return "";

            var paramNames = new List<string>();
            for (uint i = 0; i < parameters.child_count(); i++)
            {
                var param = parameters.child(i);
                string paramType = param.type();

                if (paramType == "parameter_declaration" || paramType == "optional_parameter_declaration")
                {
                    var paramTypeNode = param.child_by_field_name("type");
                    if (!paramTypeNode.is_null())
                        paramNames.Add(paramTypeNode.text(sourceText).Trim());
                    else
                        paramNames.Add(param.text(sourceText).Trim());
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
        private static TSNode FindFunctionDeclarator(TSNode node)
        {
            if (node.is_null())
                return node;
            string type = node.type();
            if (type == "function_declarator")
                return node;
            if (type == "pointer_declarator" || type == "reference_declarator")
            {
                var inner = node.child_by_field_name("declarator");
                if (!inner.is_null())
                    return FindFunctionDeclarator(inner);
                // Field lookup failed — scan children
                for (uint i = 0; i < node.child_count(); i++)
                {
                    var result = FindFunctionDeclarator(node.child(i));
                    if (!result.is_null())
                        return result;
                }
            }
            return new TSNode(); // null node
        }

        internal static string GetFunctionName(TSNode funcDefNode, string sourceText)
        {
            // The "declarator" field of a function_definition holds the function declarator
            var declarator = funcDefNode.child_by_field_name("declarator");
            if (!declarator.is_null())
            {
                // Drill into nested declarators (e.g. pointer_declarator, reference_declarator)
                // until we find the actual name or qualified_identifier
                return ExtractDeclaratorName(declarator, sourceText);
            }

            // No declarator field — scan children for qualified_identifier or operator_cast
            // This handles out-of-class operators where tree-sitter puts everything into
            // a qualified_identifier as a direct child of function_definition
            for (uint i = 0; i < funcDefNode.child_count(); i++)
            {
                var child = funcDefNode.child(i);
                string childType = child.type();
                if (childType == "qualified_identifier" || childType == "operator_cast")
                {
                    return child.text(sourceText);
                }
            }

            return "<unknown>";
        }

        internal static string ExtractDeclaratorName(TSNode declarator, string sourceText)
        {
            string type = declarator.type();

            // function_declarator -> look at the "declarator" field for the name
            if (type == "function_declarator")
            {
                var inner = declarator.child_by_field_name("declarator");
                if (!inner.is_null())
                    return ExtractDeclaratorName(inner, sourceText);
            }

            // pointer_declarator, reference_declarator -> unwrap
            // The inner declarator may not be a named field, so fall back to
            // searching children for a recognizable declarator node type.
            if (type == "pointer_declarator" || type == "reference_declarator")
            {
                var inner = declarator.child_by_field_name("declarator");
                if (!inner.is_null())
                    return ExtractDeclaratorName(inner, sourceText);

                // Field lookup failed — scan children for the nested declarator
                for (uint i = 0; i < declarator.child_count(); i++)
                {
                    var child = declarator.child(i);
                    string childType = child.type();
                    if (childType == "function_declarator" || childType == "pointer_declarator"
                        || childType == "reference_declarator" || childType == "identifier"
                        || childType == "qualified_identifier" || childType == "destructor_name"
                        || childType == "template_function" || childType == "operator_name"
                        || childType == "parenthesized_declarator")
                    {
                        return ExtractDeclaratorName(child, sourceText);
                    }
                }
            }

            // For identifiers, qualified_identifier, destructor_name, template_function, operator_name etc.
            // just return the source text of the node
            return declarator.text(sourceText);
        }

        public static void PrintTree(TSNode node, string sourceText, int depth)
        {
            string indent = new string(' ', depth * 2);
            string nodeType = node.type();

            // For small nodes, show the text content
            string content = "";
            if (node.end_offset() - node.start_offset() < 40)
            {
                content = node.text(sourceText).Replace("\n", "\\n").Replace("\r", "");
                if (content.Length > 40) content = content.Substring(0, 37) + "...";
                content = $" \"{content}\"";
            }

            var start = node.start_point();
            Debug.WriteLine($"{indent}{nodeType}  (line {start.row + 1}){content}");

            uint childCount = node.child_count();
            for (uint i = 0; i < childCount; i++)
            {
                PrintTree(node.child(i), sourceText, depth + 1);
            }
        }
    }
}
