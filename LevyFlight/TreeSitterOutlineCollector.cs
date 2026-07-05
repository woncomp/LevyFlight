using LevyFlight.TreeSitter;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;

namespace LevyFlight
{
    /// <summary>
    /// Walks a tree-sitter C++ AST and builds a hierarchical list of
    /// <see cref="OutlineSymbolItem"/> nodes suitable for a Document Outline view.
    /// </summary>
    internal static class TreeSitterOutlineCollector
    {
        /// <summary>
        /// Parses <paramref name="sourceText"/> with tree-sitter and returns the
        /// outline symbol tree.  Runs on a background thread.
        /// </summary>
        public static async Task<List<OutlineSymbolItem>> CollectAsync(string sourceText)
        {
            return await Task.Run(() =>
            {
                var results = new List<OutlineSymbolItem>();
                try
                {
                    var tree = TreeSitterParser.Parse(sourceText);
                    TreeSitterDiagnostics.SaveParse(null, sourceText, tree, "OutlineCollector", TreeSitterParser.CurrentEngineName);
                    CollectSymbols(tree.Root, results, AccessLevel.Public, false);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine("[OutlineCollector] Error: " + ex.Message);
                }
                return results;
            });
        }

        /// <summary>
        /// Synchronous overload that accepts an already-converted root node.
        /// </summary>
        public static List<OutlineSymbolItem> Collect(SyntaxNode root)
        {
            var results = new List<OutlineSymbolItem>();
            try
            {
                CollectSymbols(root, results, AccessLevel.Public, false);
            }
            catch (Exception ex)
            {
                Debug.WriteLine("[OutlineCollector] Error: " + ex.Message);
            }
            return results;
        }

        // ────────────────────────────────────────────────────────────────────
        //  Core recursive walker
        // ────────────────────────────────────────────────────────────────────

        private static void CollectSymbols(
            SyntaxNode node,
            IList<OutlineSymbolItem> siblings,
            AccessLevel currentAccess,
            bool insideFunctionBody)
        {
            int childCount = node.Children.Count;

            for (int i = 0; i < childCount; i++)
            {
                var child = node.Children[(int)i];
                if (child.IsNull) continue;

                string type = child.Type;

                switch (type)
                {
                    // ── Scoped containers ──────────────────────────────────
                    case "namespace_definition":
                        HandleNamespace(child, siblings, insideFunctionBody);
                        break;

                    case "class_specifier":
                        HandleClassOrStruct(child, siblings, OutlineSymbolKind.Class, currentAccess, insideFunctionBody);
                        break;

                    case "struct_specifier":
                        HandleClassOrStruct(child, siblings, OutlineSymbolKind.Struct, currentAccess, insideFunctionBody);
                        break;

                    case "union_specifier":
                        HandleClassOrStruct(child, siblings, OutlineSymbolKind.Union, currentAccess, insideFunctionBody);
                        break;

                    case "enum_specifier":
                        HandleEnum(child, siblings, currentAccess);
                        break;

                    // ── Functions ───────────────────────────────────────────
                    case "function_definition":
                        if (!insideFunctionBody)
                            HandleFunction(child, siblings, currentAccess);
                        break;

                    // ── Declarations (variables, prototypes, fields) ───────
                    case "declaration":
                        if (!insideFunctionBody)
                            HandleDeclaration(child, siblings, currentAccess);
                        break;

                    case "field_declaration":
                        HandleFieldDeclaration(child, siblings, currentAccess);
                        break;

                    // ── Preprocessor ────────────────────────────────────────
                    case "preproc_def":
                        HandlePreprocDef(child, siblings);
                        break;

                    case "preproc_function_def":
                        HandlePreprocFunctionDef(child, siblings);
                        break;

                    // ── Type aliases ────────────────────────────────────────
                    case "type_definition":
                        HandleTypeDefinition(child, siblings, currentAccess);
                        break;

                    case "alias_declaration":
                        HandleAliasDeclaration(child, siblings, currentAccess);
                        break;

                    // ── Template wrapper ────────────────────────────────────
                    case "template_declaration":
                        HandleTemplateDeclaration(child, siblings, currentAccess, insideFunctionBody);
                        break;

                    // ── Linkage specification (extern "C" { … }) ───────────
                    case "linkage_specification":
                        HandleLinkageSpecification(child, siblings, currentAccess, insideFunctionBody);
                        break;

                    // ── Access specifier (public:/private:/protected:) ──────
                    case "access_specifier":
                        // Handled at the parent level in HandleClassOrStruct
                        break;

                    // ── Preprocessor conditionals — recurse through ─────────
                    case "preproc_ifdef":
                    case "preproc_ifndef":
                    case "preproc_if":
                    case "preproc_else":
                    case "preproc_elif":
                        CollectSymbols(child, siblings, currentAccess, insideFunctionBody);
                        break;

                    default:
                        // Skip everything else (comments, includes, expressions, etc.)
                        break;
                }
            }
        }

        // ────────────────────────────────────────────────────────────────────
        //  Individual node-type handlers
        // ────────────────────────────────────────────────────────────────────

        private static void HandleNamespace(SyntaxNode node, IList<OutlineSymbolItem> siblings, bool insideFunctionBody)
        {
            var nameNode = node.ChildByFieldName("name");
            string name = !nameNode.IsNull ? nameNode.Text : "<anonymous>";

            var item = MakeItem(name, OutlineSymbolKind.Namespace, AccessLevel.Public, node);

            // Recurse into the namespace body
            var body = node.ChildByFieldName("body");
            if (!body.IsNull)
                CollectSymbols(body, item.Children, AccessLevel.Public, insideFunctionBody);

            siblings.Add(item);
        }

        private static void HandleClassOrStruct(SyntaxNode node, IList<OutlineSymbolItem> siblings,
            OutlineSymbolKind kind, AccessLevel parentAccess, bool insideFunctionBody)
        {
            var nameNode = node.ChildByFieldName("name");
            // Forward declarations (no body) — skip or could be included
            string name = !nameNode.IsNull ? nameNode.Text : "<anonymous>";

            var item = MakeItem(name, kind, parentAccess, node);

            // Find the body (field_declaration_list)
            var body = node.ChildByFieldName("body");
            if (!body.IsNull)
            {
                // Default access: private for class, public for struct/union
                AccessLevel defaultAccess = (kind == OutlineSymbolKind.Class) ? AccessLevel.Private : AccessLevel.Public;
                AccessLevel access = defaultAccess;

                int bodyChildCount = body.Children.Count;
                for (int j = 0; j < bodyChildCount; j++)
                {
                    var member = body.Children[(int)j];
                    if (member.IsNull) continue;

                    string memberType = member.Type;

                    if (memberType == "access_specifier")
                    {
                        access = ParseAccessSpecifier(member);
                        continue;
                    }

                    // Process members with the current access level
                    switch (memberType)
                    {
                        case "function_definition":
                            HandleFunction(member, item.Children, access);
                            break;

                        case "field_declaration":
                            HandleFieldDeclaration(member, item.Children, access);
                            break;

                        case "declaration":
                            HandleDeclaration(member, item.Children, access);
                            break;

                        case "class_specifier":
                            HandleClassOrStruct(member, item.Children, OutlineSymbolKind.Class, access, false);
                            break;

                        case "struct_specifier":
                            HandleClassOrStruct(member, item.Children, OutlineSymbolKind.Struct, access, false);
                            break;

                        case "union_specifier":
                            HandleClassOrStruct(member, item.Children, OutlineSymbolKind.Union, access, false);
                            break;

                        case "enum_specifier":
                            HandleEnum(member, item.Children, access);
                            break;

                        case "template_declaration":
                            HandleTemplateDeclaration(member, item.Children, access, false);
                            break;

                        case "type_definition":
                            HandleTypeDefinition(member, item.Children, access);
                            break;

                        case "alias_declaration":
                            HandleAliasDeclaration(member, item.Children, access);
                            break;

                        case "friend_declaration":
                            // Usually not shown in outline — skip
                            break;

                        case "preproc_def":
                            HandlePreprocDef(member, item.Children);
                            break;

                        case "preproc_function_def":
                            HandlePreprocFunctionDef(member, item.Children);
                            break;

                        case "preproc_ifdef":
                        case "preproc_ifndef":
                        case "preproc_if":
                        case "preproc_else":
                        case "preproc_elif":
                            // Recurse through preprocessor conditionals inside the class body
                            CollectClassBody(member, item.Children, ref access);
                            break;

                        default:
                            break;
                    }
                }
            }

            siblings.Add(item);
        }

        /// <summary>
        /// Helper to recurse through preprocessor conditionals inside a class body,
        /// preserving access specifier state across conditional branches.
        /// </summary>
        private static void CollectClassBody(SyntaxNode node, IList<OutlineSymbolItem> children, ref AccessLevel access)
        {
            int count = node.Children.Count;
            for (int i = 0; i < count; i++)
            {
                var child = node.Children[(int)i];
                if (child.IsNull) continue;
                string childType = child.Type;

                if (childType == "access_specifier")
                {
                    access = ParseAccessSpecifier(child);
                    continue;
                }

                switch (childType)
                {
                    case "function_definition":
                        HandleFunction(child, children, access);
                        break;
                    case "field_declaration":
                        HandleFieldDeclaration(child, children, access);
                        break;
                    case "declaration":
                        HandleDeclaration(child, children, access);
                        break;
                    case "class_specifier":
                        HandleClassOrStruct(child, children, OutlineSymbolKind.Class, access, false);
                        break;
                    case "struct_specifier":
                        HandleClassOrStruct(child, children, OutlineSymbolKind.Struct, access, false);
                        break;
                    case "union_specifier":
                        HandleClassOrStruct(child, children, OutlineSymbolKind.Union, access, false);
                        break;
                    case "enum_specifier":
                        HandleEnum(child, children, access);
                        break;
                    case "template_declaration":
                        HandleTemplateDeclaration(child, children, access, false);
                        break;
                    case "type_definition":
                        HandleTypeDefinition(child, children, access);
                        break;
                    case "alias_declaration":
                        HandleAliasDeclaration(child, children, access);
                        break;
                    case "preproc_def":
                        HandlePreprocDef(child, children);
                        break;
                    case "preproc_function_def":
                        HandlePreprocFunctionDef(child, children);
                        break;
                    case "preproc_ifdef":
                    case "preproc_ifndef":
                    case "preproc_if":
                    case "preproc_else":
                    case "preproc_elif":
                        CollectClassBody(child, children, ref access);
                        break;
                    default:
                        break;
                }
            }
        }

        private static void HandleEnum(SyntaxNode node, IList<OutlineSymbolItem> siblings, AccessLevel access)
        {
            var nameNode = node.ChildByFieldName("name");
            string name = !nameNode.IsNull ? nameNode.Text : "<anonymous enum>";

            var item = MakeItem(name, OutlineSymbolKind.Enum, access, node);

            // Collect enumerators
            var body = node.ChildByFieldName("body");
            if (!body.IsNull)
            {
                int count = body.Children.Count;
                for (int j = 0; j < count; j++)
                {
                    var enumerator = body.Children[j];
                    if (enumerator.IsNull) continue;
                    if (enumerator.Type == "enumerator")
                    {
                        var eName = enumerator.ChildByFieldName("name");
                        if (!eName.IsNull)
                        {
                            var eItem = MakeItem(eName.Text, OutlineSymbolKind.EnumMember, AccessLevel.Public, enumerator);
                            item.Children.Add(eItem);
                        }
                    }
                }
            }

            siblings.Add(item);
        }

        private static void HandleFunction(SyntaxNode node, IList<OutlineSymbolItem> siblings, AccessLevel access)
        {
            var typeNode = node.ChildByFieldName("type");

            // Filter out macro-like false positives (no return type and not a constructor/destructor/operator)
            if (typeNode.IsNull)
            {
                if (!TreeSitterCodeParser.IsSpecialMemberFunction(node, null))
                    return;
            }

            string returnType = !typeNode.IsNull ? typeNode.Text.Trim() : "";
            var (funcName, paramList) = ExtractNameAndParams(node);

            string displayName = (!string.IsNullOrEmpty(returnType) ? returnType + " " : "")
                                 + funcName + "(" + paramList + ")";

            var item = MakeItem(displayName, OutlineSymbolKind.Function, access, node);
            siblings.Add(item);
        }

        /// <summary>
        /// Extracts function name and parameter list by drilling through the
        /// declarator once, avoiding redundant child_by_field_name lookups.
        /// </summary>
        private static (string name, string paramList) ExtractNameAndParams(SyntaxNode funcDefNode)
        {
            var declarator = funcDefNode.ChildByFieldName("declarator");
            if (declarator.IsNull)
            {
                // No declarator field — scan children for qualified_identifier or operator_cast
                for (int i = 0; i < funcDefNode.Children.Count; i++)
                {
                    var child = funcDefNode.Children[i];
                    string childType = child.Type;
                    if (childType == "qualified_identifier" || childType == "operator_cast")
                        return (child.Text, "");
                }
                return ("<unknown>", "");
            }

            // Find the function_declarator (may be wrapped in pointer/reference declarators)
            var funcDecl = FindFunctionDeclaratorLocal(declarator);
            if (funcDecl.IsNull)
            {
                // Not a function_declarator — just a name (e.g. function pointer variable)
                return (TreeSitterCodeParser.ExtractDeclaratorName(declarator), "");
            }

            // Extract name from the function_declarator's inner declarator
            var innerDecl = funcDecl.ChildByFieldName("declarator");
            string name = !innerDecl.IsNull
                ? TreeSitterCodeParser.ExtractDeclaratorName(innerDecl)
                : "<unknown>";

            // Extract params from the function_declarator's parameters
            var parameters = funcDecl.ChildByFieldName("parameters");
            string paramList = "";
            if (!parameters.IsNull)
            {
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
                paramList = string.Join(", ", paramNames);
            }

            return (name, paramList);
        }

        private static SyntaxNode FindFunctionDeclaratorLocal(SyntaxNode node)
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
                    return FindFunctionDeclaratorLocal(inner);
                for (int i = 0; i < node.Children.Count; i++)
                {
                    var result = FindFunctionDeclaratorLocal(node.Children[i]);
                    if (!result.IsNull)
                        return result;
                }
            }
            return SyntaxNode.Null;
        }

        private static void HandleDeclaration(SyntaxNode node, IList<OutlineSymbolItem> siblings, AccessLevel access)
        {
            // A "declaration" can be a variable, a function prototype, or a forward decl.
            // We show only variable declarations and simple prototypes.
            var declarator = node.ChildByFieldName("declarator");
            if (declarator.IsNull) return;

            string declType = declarator.Type;

            // Function prototype — show it
            if (declType == "function_declarator")
            {
                string returnType = "";
                var typeNode = node.ChildByFieldName("type");
                if (!typeNode.IsNull)
                    returnType = typeNode.Text.Trim();

                string name = ExtractSimpleName(declarator);
                string paramList = TreeSitterCodeParser.GetParameterList(node);
                string displayName = (!string.IsNullOrEmpty(returnType) ? returnType + " " : "")
                                     + name + "(" + paramList + ")";
                var item = MakeItem(displayName, OutlineSymbolKind.Function, access, node);
                siblings.Add(item);
                return;
            }

            // Variable / constant declaration
            if (declType == "identifier" || declType == "init_declarator" || declType == "pointer_declarator"
                || declType == "reference_declarator")
            {
                string returnType = "";
                var typeNode = node.ChildByFieldName("type");
                if (!typeNode.IsNull)
                    returnType = typeNode.Text.Trim();

                string name = ExtractSimpleName(declarator);
                string displayName = (!string.IsNullOrEmpty(returnType) ? returnType + " " : "") + name;
                var item = MakeItem(displayName, OutlineSymbolKind.Variable, access, node);
                siblings.Add(item);
                return;
            }
        }

        private static void HandleFieldDeclaration(SyntaxNode node, IList<OutlineSymbolItem> siblings, AccessLevel access)
        {
            // A field_declaration has a type and one or more declarators.
            string returnType = "";
            var typeNode = node.ChildByFieldName("type");
            if (!typeNode.IsNull)
                returnType = typeNode.Text.Trim();

            var declarator = node.ChildByFieldName("declarator");

            // Some field_declaration nodes contain nested class/struct/union/enum definitions
            // For example: struct { int x; } myField;
            // In that case the type itself may be a class_specifier etc.
            if (!typeNode.IsNull)
            {
                string typeType = typeNode.Type;
                if (typeType == "class_specifier" || typeType == "struct_specifier" || typeType == "union_specifier")
                {
                    var kind = typeType == "class_specifier" ? OutlineSymbolKind.Class
                             : typeType == "struct_specifier" ? OutlineSymbolKind.Struct
                             : OutlineSymbolKind.Union;
                    HandleClassOrStruct(typeNode, siblings, kind, access, false);
                    // If there's also a declarator (named field), still add it
                    if (declarator.IsNull) return;
                }
                else if (typeType == "enum_specifier")
                {
                    HandleEnum(typeNode, siblings, access);
                    if (declarator.IsNull) return;
                }
            }

            if (declarator.IsNull) return;

            // Check if this is a function pointer or function declarator inside a field
            string declType = declarator.Type;
            if (declType == "function_declarator")
            {
                string name = ExtractSimpleName(declarator);
                string paramList = TreeSitterCodeParser.GetParameterList(node);
                string displayName = (!string.IsNullOrEmpty(returnType) ? returnType + " " : "")
                                     + name + "(" + paramList + ")";
                var item = MakeItem(displayName, OutlineSymbolKind.Function, access, node);
                siblings.Add(item);
                return;
            }

            string fieldName = ExtractSimpleName(declarator);
            string display = (!string.IsNullOrEmpty(returnType) ? returnType + " " : "") + fieldName;
            var fieldItem = MakeItem(display, OutlineSymbolKind.Field, access, node);
            siblings.Add(fieldItem);
        }

        private static void HandlePreprocDef(SyntaxNode node, IList<OutlineSymbolItem> siblings)
        {
            var nameNode = node.ChildByFieldName("name");
            if (nameNode.IsNull) return;

            string name = nameNode.Text;
            var item = MakeItem(name, OutlineSymbolKind.Macro, AccessLevel.Public, node);
            siblings.Add(item);
        }

        private static void HandlePreprocFunctionDef(SyntaxNode node, IList<OutlineSymbolItem> siblings)
        {
            var nameNode = node.ChildByFieldName("name");
            if (nameNode.IsNull) return;

            // Build parameter list from the preproc parameters
            string name = nameNode.Text;
            var parameters = node.ChildByFieldName("parameters");
            string paramText = !parameters.IsNull ? parameters.Text : "()";
            var item = MakeItem(name + paramText, OutlineSymbolKind.Macro, AccessLevel.Public, node);
            siblings.Add(item);
        }

        private static void HandleTypeDefinition(SyntaxNode node, IList<OutlineSymbolItem> siblings, AccessLevel access)
        {
            var declarator = node.ChildByFieldName("declarator");
            if (declarator.IsNull) return;

            string name = ExtractSimpleName(declarator);
            var item = MakeItem(name, OutlineSymbolKind.TypeDef, access, node);
            siblings.Add(item);
        }

        private static void HandleAliasDeclaration(SyntaxNode node, IList<OutlineSymbolItem> siblings, AccessLevel access)
        {
            var nameNode = node.ChildByFieldName("name");
            if (nameNode.IsNull) return;

            string name = nameNode.Text;
            var item = MakeItem(name, OutlineSymbolKind.UsingAlias, access, node);
            siblings.Add(item);
        }

        private static void HandleTemplateDeclaration(SyntaxNode node, IList<OutlineSymbolItem> siblings, AccessLevel access, bool insideFunctionBody)
        {
            // A template_declaration wraps another declaration — unwrap and process the inner node
            int count = node.Children.Count;
            for (int i = 0; i < count; i++)
            {
                var child = node.Children[(int)i];
                if (child.IsNull) continue;
                string childType = child.Type;

                switch (childType)
                {
                    case "function_definition":
                        if (!insideFunctionBody)
                            HandleFunction(child, siblings, access);
                        break;
                    case "declaration":
                        if (!insideFunctionBody)
                            HandleDeclaration(child, siblings, access);
                        break;
                    case "class_specifier":
                        HandleClassOrStruct(child, siblings, OutlineSymbolKind.Class, access, insideFunctionBody);
                        break;
                    case "struct_specifier":
                        HandleClassOrStruct(child, siblings, OutlineSymbolKind.Struct, access, insideFunctionBody);
                        break;
                    case "union_specifier":
                        HandleClassOrStruct(child, siblings, OutlineSymbolKind.Union, access, insideFunctionBody);
                        break;
                    case "alias_declaration":
                        HandleAliasDeclaration(child, siblings, access);
                        break;
                    // template_parameter_list — skip
                    default:
                        break;
                }
            }
        }

        private static void HandleLinkageSpecification(SyntaxNode node, IList<OutlineSymbolItem> siblings, AccessLevel access, bool insideFunctionBody)
        {
            // extern "C" { ... } — recurse into the body
            var body = node.ChildByFieldName("body");
            if (!body.IsNull)
                CollectSymbols(body, siblings, access, insideFunctionBody);

            // extern "C" single-declaration  (no body, the declaration is a direct child)
            var value = node.ChildByFieldName("value");
            if (!value.IsNull)
            {
                string valType = value.Type;
                if (valType == "function_definition" && !insideFunctionBody)
                    HandleFunction(value, siblings, access);
                else if (valType == "declaration" && !insideFunctionBody)
                    HandleDeclaration(value, siblings, access);
            }
        }

        // ────────────────────────────────────────────────────────────────────
        //  Helpers
        // ────────────────────────────────────────────────────────────────────

        private static AccessLevel ParseAccessSpecifier(SyntaxNode node)
        {
            // The node text is something like "public" or "private" or "protected"
            // (the colon is a separate child in some grammars)
            string text = node.Text.Trim().TrimEnd(':').Trim();
            if (text.StartsWith("private")) return AccessLevel.Private;
            if (text.StartsWith("protected")) return AccessLevel.Protected;
            return AccessLevel.Public;
        }

        private static OutlineSymbolItem MakeItem(string name, OutlineSymbolKind kind, AccessLevel access, SyntaxNode node)
        {
            var start = node.Start;
            var end = node.End;
            return new OutlineSymbolItem(name, kind, access)
            {
                StartLine = (int)start.Row + 1,
                StartColumn = (int)start.Column,
                EndLine = (int)end.Row + 1,
            };
        }

        /// <summary>
        /// Extracts a simple, readable name from a declarator node.
        /// Handles init_declarator, pointer_declarator, reference_declarator, identifier, etc.
        /// </summary>
        private static string ExtractSimpleName(SyntaxNode declarator)
        {
            if (declarator.IsNull) return "<unknown>";

            string type = declarator.Type;

            if (type == "identifier" || type == "qualified_identifier"
                || type == "destructor_name" || type == "operator_name")
            {
                return declarator.Text;
            }

            if (type == "init_declarator")
            {
                var inner = declarator.ChildByFieldName("declarator");
                if (!inner.IsNull)
                    return ExtractSimpleName(inner);
            }

            if (type == "pointer_declarator" || type == "reference_declarator")
            {
                var inner = declarator.ChildByFieldName("declarator");
                if (!inner.IsNull)
                    return ExtractSimpleName(inner);
                // Fall back to scanning children
                for (int i = 0; i < declarator.Children.Count; i++)
                {
                    var child = declarator.Children[(int)i];
                    string ct = child.Type;
                    if (ct == "identifier" || ct == "qualified_identifier"
                        || ct == "function_declarator" || ct == "init_declarator")
                        return ExtractSimpleName(child);
                }
            }

            if (type == "function_declarator")
            {
                var inner = declarator.ChildByFieldName("declarator");
                if (!inner.IsNull)
                    return ExtractSimpleName(inner);
            }

            if (type == "array_declarator")
            {
                var inner = declarator.ChildByFieldName("declarator");
                if (!inner.IsNull)
                    return ExtractSimpleName(inner);
            }

            if (type == "parenthesized_declarator")
            {
                // (identifier) — unwrap
                for (int i = 0; i < declarator.Children.Count; i++)
                {
                    var child = declarator.Children[(int)i];
                    if (child.Type != "(" && child.Type != ")")
                        return ExtractSimpleName(child);
                }
            }

            // Last resort
            return declarator.Text;
        }
    }
}












