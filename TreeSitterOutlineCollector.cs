using GitHub.TreeSitter;
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
        public static async Task<List<OutlineSymbolItem>> CollectAsync(string sourceText, TSTree existingTree, TSParser parser)
        {
            // Capture values for the task
            var text = sourceText;
            var tree = existingTree;
            var p = parser;

            return await Task.Run(() =>
            {
                var results = new List<OutlineSymbolItem>();
                try
                {
                    TSTree parseTree = tree;
                    bool ownTree = false;

                    if (parseTree == null)
                    {
                        // Full parse
                        using (var lang = TSParser.CppLanguage())
                        {
                            p.set_language(lang);
                        }
                        parseTree = p.parse_string(null, text);
                        ownTree = true;
                    }

                    if (parseTree == null)
                        return results;

                    var root = parseTree.root_node();
                    CollectSymbols(root, text, results, AccessLevel.Public, false);

                    if (ownTree)
                        parseTree.Dispose();
                }
                catch (Exception ex)
                {
                    Debug.WriteLine("[OutlineCollector] Error: " + ex.Message);
                }
                return results;
            });
        }

        /// <summary>
        /// Synchronous overload that accepts an already-parsed tree.
        /// </summary>
        public static List<OutlineSymbolItem> Collect(TSNode root, string sourceText)
        {
            var results = new List<OutlineSymbolItem>();
            try
            {
                CollectSymbols(root, sourceText, results, AccessLevel.Public, false);
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
            TSNode node,
            string src,
            IList<OutlineSymbolItem> siblings,
            AccessLevel currentAccess,
            bool insideFunctionBody)
        {
            uint childCount = node.child_count();

            for (uint i = 0; i < childCount; i++)
            {
                var child = node.child(i);
                if (child.is_null()) continue;

                string type = child.type();

                switch (type)
                {
                    // ── Scoped containers ──────────────────────────────────
                    case "namespace_definition":
                        HandleNamespace(child, src, siblings, insideFunctionBody);
                        break;

                    case "class_specifier":
                        HandleClassOrStruct(child, src, siblings, OutlineSymbolKind.Class, currentAccess, insideFunctionBody);
                        break;

                    case "struct_specifier":
                        HandleClassOrStruct(child, src, siblings, OutlineSymbolKind.Struct, currentAccess, insideFunctionBody);
                        break;

                    case "union_specifier":
                        HandleClassOrStruct(child, src, siblings, OutlineSymbolKind.Union, currentAccess, insideFunctionBody);
                        break;

                    case "enum_specifier":
                        HandleEnum(child, src, siblings, currentAccess);
                        break;

                    // ── Functions ───────────────────────────────────────────
                    case "function_definition":
                        if (!insideFunctionBody)
                            HandleFunction(child, src, siblings, currentAccess);
                        break;

                    // ── Declarations (variables, prototypes, fields) ───────
                    case "declaration":
                        if (!insideFunctionBody)
                            HandleDeclaration(child, src, siblings, currentAccess);
                        break;

                    case "field_declaration":
                        HandleFieldDeclaration(child, src, siblings, currentAccess);
                        break;

                    // ── Preprocessor ────────────────────────────────────────
                    case "preproc_def":
                        HandlePreprocDef(child, src, siblings);
                        break;

                    case "preproc_function_def":
                        HandlePreprocFunctionDef(child, src, siblings);
                        break;

                    // ── Type aliases ────────────────────────────────────────
                    case "type_definition":
                        HandleTypeDefinition(child, src, siblings, currentAccess);
                        break;

                    case "alias_declaration":
                        HandleAliasDeclaration(child, src, siblings, currentAccess);
                        break;

                    // ── Template wrapper ────────────────────────────────────
                    case "template_declaration":
                        HandleTemplateDeclaration(child, src, siblings, currentAccess, insideFunctionBody);
                        break;

                    // ── Linkage specification (extern "C" { … }) ───────────
                    case "linkage_specification":
                        HandleLinkageSpecification(child, src, siblings, currentAccess, insideFunctionBody);
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
                        CollectSymbols(child, src, siblings, currentAccess, insideFunctionBody);
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

        private static void HandleNamespace(TSNode node, string src, IList<OutlineSymbolItem> siblings, bool insideFunctionBody)
        {
            var nameNode = node.child_by_field_name("name");
            string name = !nameNode.is_null() ? nameNode.text(src) : "<anonymous>";

            var item = MakeItem(name, OutlineSymbolKind.Namespace, AccessLevel.Public, node);

            // Recurse into the namespace body
            var body = node.child_by_field_name("body");
            if (!body.is_null())
                CollectSymbols(body, src, item.Children, AccessLevel.Public, insideFunctionBody);

            siblings.Add(item);
        }

        private static void HandleClassOrStruct(TSNode node, string src, IList<OutlineSymbolItem> siblings,
            OutlineSymbolKind kind, AccessLevel parentAccess, bool insideFunctionBody)
        {
            var nameNode = node.child_by_field_name("name");
            // Forward declarations (no body) — skip or could be included
            string name = !nameNode.is_null() ? nameNode.text(src) : "<anonymous>";

            var item = MakeItem(name, kind, parentAccess, node);

            // Find the body (field_declaration_list)
            var body = node.child_by_field_name("body");
            if (!body.is_null())
            {
                // Default access: private for class, public for struct/union
                AccessLevel defaultAccess = (kind == OutlineSymbolKind.Class) ? AccessLevel.Private : AccessLevel.Public;
                AccessLevel access = defaultAccess;

                uint bodyChildCount = body.child_count();
                for (uint j = 0; j < bodyChildCount; j++)
                {
                    var member = body.child(j);
                    if (member.is_null()) continue;

                    string memberType = member.type();

                    if (memberType == "access_specifier")
                    {
                        access = ParseAccessSpecifier(member, src);
                        continue;
                    }

                    // Process members with the current access level
                    switch (memberType)
                    {
                        case "function_definition":
                            HandleFunction(member, src, item.Children, access);
                            break;

                        case "field_declaration":
                            HandleFieldDeclaration(member, src, item.Children, access);
                            break;

                        case "declaration":
                            HandleDeclaration(member, src, item.Children, access);
                            break;

                        case "class_specifier":
                            HandleClassOrStruct(member, src, item.Children, OutlineSymbolKind.Class, access, false);
                            break;

                        case "struct_specifier":
                            HandleClassOrStruct(member, src, item.Children, OutlineSymbolKind.Struct, access, false);
                            break;

                        case "union_specifier":
                            HandleClassOrStruct(member, src, item.Children, OutlineSymbolKind.Union, access, false);
                            break;

                        case "enum_specifier":
                            HandleEnum(member, src, item.Children, access);
                            break;

                        case "template_declaration":
                            HandleTemplateDeclaration(member, src, item.Children, access, false);
                            break;

                        case "type_definition":
                            HandleTypeDefinition(member, src, item.Children, access);
                            break;

                        case "alias_declaration":
                            HandleAliasDeclaration(member, src, item.Children, access);
                            break;

                        case "friend_declaration":
                            // Usually not shown in outline — skip
                            break;

                        case "preproc_def":
                            HandlePreprocDef(member, src, item.Children);
                            break;

                        case "preproc_function_def":
                            HandlePreprocFunctionDef(member, src, item.Children);
                            break;

                        case "preproc_ifdef":
                        case "preproc_ifndef":
                        case "preproc_if":
                        case "preproc_else":
                        case "preproc_elif":
                            // Recurse through preprocessor conditionals inside the class body
                            CollectClassBody(member, src, item.Children, ref access);
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
        private static void CollectClassBody(TSNode node, string src, IList<OutlineSymbolItem> children, ref AccessLevel access)
        {
            uint count = node.child_count();
            for (uint i = 0; i < count; i++)
            {
                var child = node.child(i);
                if (child.is_null()) continue;
                string childType = child.type();

                if (childType == "access_specifier")
                {
                    access = ParseAccessSpecifier(child, src);
                    continue;
                }

                switch (childType)
                {
                    case "function_definition":
                        HandleFunction(child, src, children, access);
                        break;
                    case "field_declaration":
                        HandleFieldDeclaration(child, src, children, access);
                        break;
                    case "declaration":
                        HandleDeclaration(child, src, children, access);
                        break;
                    case "class_specifier":
                        HandleClassOrStruct(child, src, children, OutlineSymbolKind.Class, access, false);
                        break;
                    case "struct_specifier":
                        HandleClassOrStruct(child, src, children, OutlineSymbolKind.Struct, access, false);
                        break;
                    case "union_specifier":
                        HandleClassOrStruct(child, src, children, OutlineSymbolKind.Union, access, false);
                        break;
                    case "enum_specifier":
                        HandleEnum(child, src, children, access);
                        break;
                    case "template_declaration":
                        HandleTemplateDeclaration(child, src, children, access, false);
                        break;
                    case "type_definition":
                        HandleTypeDefinition(child, src, children, access);
                        break;
                    case "alias_declaration":
                        HandleAliasDeclaration(child, src, children, access);
                        break;
                    case "preproc_def":
                        HandlePreprocDef(child, src, children);
                        break;
                    case "preproc_function_def":
                        HandlePreprocFunctionDef(child, src, children);
                        break;
                    case "preproc_ifdef":
                    case "preproc_ifndef":
                    case "preproc_if":
                    case "preproc_else":
                    case "preproc_elif":
                        CollectClassBody(child, src, children, ref access);
                        break;
                    default:
                        break;
                }
            }
        }

        private static void HandleEnum(TSNode node, string src, IList<OutlineSymbolItem> siblings, AccessLevel access)
        {
            var nameNode = node.child_by_field_name("name");
            string name = !nameNode.is_null() ? nameNode.text(src) : "<anonymous enum>";

            var item = MakeItem(name, OutlineSymbolKind.Enum, access, node);

            // Collect enumerators
            var body = node.child_by_field_name("body");
            if (!body.is_null())
            {
                uint count = body.child_count();
                for (uint j = 0; j < count; j++)
                {
                    var enumerator = body.child(j);
                    if (enumerator.is_null()) continue;
                    if (enumerator.type() == "enumerator")
                    {
                        var eName = enumerator.child_by_field_name("name");
                        if (!eName.is_null())
                        {
                            var eItem = MakeItem(eName.text(src), OutlineSymbolKind.EnumMember, AccessLevel.Public, enumerator);
                            item.Children.Add(eItem);
                        }
                    }
                }
            }

            siblings.Add(item);
        }

        private static void HandleFunction(TSNode node, string src, IList<OutlineSymbolItem> siblings, AccessLevel access)
        {
            // Re-use the same pitfall-aware logic from TreeSitterCodeParser
            var typeNode = node.child_by_field_name("type");

            // Filter out macro-like false positives (no return type and not a constructor/destructor/operator)
            if (typeNode.is_null())
            {
                if (!TreeSitterCodeParser.IsSpecialMemberFunction(node, src, null))
                    return;
            }

            string funcName = TreeSitterCodeParser.GetFunctionName(node, src);
            string returnType = TreeSitterCodeParser.GetReturnType(node, src);
            string paramList = TreeSitterCodeParser.GetParameterList(node, src);

            string displayName = (!string.IsNullOrEmpty(returnType) ? returnType + " " : "")
                                 + funcName + "(" + paramList + ")";

            var item = MakeItem(displayName, OutlineSymbolKind.Function, access, node);
            siblings.Add(item);
        }

        private static void HandleDeclaration(TSNode node, string src, IList<OutlineSymbolItem> siblings, AccessLevel access)
        {
            // A "declaration" can be a variable, a function prototype, or a forward decl.
            // We show only variable declarations and simple prototypes.
            var declarator = node.child_by_field_name("declarator");
            if (declarator.is_null()) return;

            string declType = declarator.type();

            // Function prototype — show it
            if (declType == "function_declarator")
            {
                string returnType = "";
                var typeNode = node.child_by_field_name("type");
                if (!typeNode.is_null())
                    returnType = typeNode.text(src).Trim();

                string name = ExtractSimpleName(declarator, src);
                string paramList = TreeSitterCodeParser.GetParameterList(node, src);
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
                var typeNode = node.child_by_field_name("type");
                if (!typeNode.is_null())
                    returnType = typeNode.text(src).Trim();

                string name = ExtractSimpleName(declarator, src);
                string displayName = (!string.IsNullOrEmpty(returnType) ? returnType + " " : "") + name;
                var item = MakeItem(displayName, OutlineSymbolKind.Variable, access, node);
                siblings.Add(item);
                return;
            }
        }

        private static void HandleFieldDeclaration(TSNode node, string src, IList<OutlineSymbolItem> siblings, AccessLevel access)
        {
            // A field_declaration has a type and one or more declarators.
            string returnType = "";
            var typeNode = node.child_by_field_name("type");
            if (!typeNode.is_null())
                returnType = typeNode.text(src).Trim();

            var declarator = node.child_by_field_name("declarator");

            // Some field_declaration nodes contain nested class/struct/union/enum definitions
            // For example: struct { int x; } myField;
            // In that case the type itself may be a class_specifier etc.
            if (!typeNode.is_null())
            {
                string typeType = typeNode.type();
                if (typeType == "class_specifier" || typeType == "struct_specifier" || typeType == "union_specifier")
                {
                    var kind = typeType == "class_specifier" ? OutlineSymbolKind.Class
                             : typeType == "struct_specifier" ? OutlineSymbolKind.Struct
                             : OutlineSymbolKind.Union;
                    HandleClassOrStruct(typeNode, src, siblings, kind, access, false);
                    // If there's also a declarator (named field), still add it
                    if (declarator.is_null()) return;
                }
                else if (typeType == "enum_specifier")
                {
                    HandleEnum(typeNode, src, siblings, access);
                    if (declarator.is_null()) return;
                }
            }

            if (declarator.is_null()) return;

            // Check if this is a function pointer or function declarator inside a field
            string declType = declarator.type();
            if (declType == "function_declarator")
            {
                string name = ExtractSimpleName(declarator, src);
                string paramList = TreeSitterCodeParser.GetParameterList(node, src);
                string displayName = (!string.IsNullOrEmpty(returnType) ? returnType + " " : "")
                                     + name + "(" + paramList + ")";
                var item = MakeItem(displayName, OutlineSymbolKind.Function, access, node);
                siblings.Add(item);
                return;
            }

            string fieldName = ExtractSimpleName(declarator, src);
            string display = (!string.IsNullOrEmpty(returnType) ? returnType + " " : "") + fieldName;
            var fieldItem = MakeItem(display, OutlineSymbolKind.Field, access, node);
            siblings.Add(fieldItem);
        }

        private static void HandlePreprocDef(TSNode node, string src, IList<OutlineSymbolItem> siblings)
        {
            var nameNode = node.child_by_field_name("name");
            if (nameNode.is_null()) return;

            string name = nameNode.text(src);
            var item = MakeItem(name, OutlineSymbolKind.Macro, AccessLevel.Public, node);
            siblings.Add(item);
        }

        private static void HandlePreprocFunctionDef(TSNode node, string src, IList<OutlineSymbolItem> siblings)
        {
            var nameNode = node.child_by_field_name("name");
            if (nameNode.is_null()) return;

            // Build parameter list from the preproc parameters
            string name = nameNode.text(src);
            var parameters = node.child_by_field_name("parameters");
            string paramText = !parameters.is_null() ? parameters.text(src) : "()";
            var item = MakeItem(name + paramText, OutlineSymbolKind.Macro, AccessLevel.Public, node);
            siblings.Add(item);
        }

        private static void HandleTypeDefinition(TSNode node, string src, IList<OutlineSymbolItem> siblings, AccessLevel access)
        {
            var declarator = node.child_by_field_name("declarator");
            if (declarator.is_null()) return;

            string name = ExtractSimpleName(declarator, src);
            var item = MakeItem(name, OutlineSymbolKind.TypeDef, access, node);
            siblings.Add(item);
        }

        private static void HandleAliasDeclaration(TSNode node, string src, IList<OutlineSymbolItem> siblings, AccessLevel access)
        {
            var nameNode = node.child_by_field_name("name");
            if (nameNode.is_null()) return;

            string name = nameNode.text(src);
            var item = MakeItem(name, OutlineSymbolKind.UsingAlias, access, node);
            siblings.Add(item);
        }

        private static void HandleTemplateDeclaration(TSNode node, string src, IList<OutlineSymbolItem> siblings, AccessLevel access, bool insideFunctionBody)
        {
            // A template_declaration wraps another declaration — unwrap and process the inner node
            uint count = node.child_count();
            for (uint i = 0; i < count; i++)
            {
                var child = node.child(i);
                if (child.is_null()) continue;
                string childType = child.type();

                switch (childType)
                {
                    case "function_definition":
                        if (!insideFunctionBody)
                            HandleFunction(child, src, siblings, access);
                        break;
                    case "declaration":
                        if (!insideFunctionBody)
                            HandleDeclaration(child, src, siblings, access);
                        break;
                    case "class_specifier":
                        HandleClassOrStruct(child, src, siblings, OutlineSymbolKind.Class, access, insideFunctionBody);
                        break;
                    case "struct_specifier":
                        HandleClassOrStruct(child, src, siblings, OutlineSymbolKind.Struct, access, insideFunctionBody);
                        break;
                    case "union_specifier":
                        HandleClassOrStruct(child, src, siblings, OutlineSymbolKind.Union, access, insideFunctionBody);
                        break;
                    case "alias_declaration":
                        HandleAliasDeclaration(child, src, siblings, access);
                        break;
                    // template_parameter_list — skip
                    default:
                        break;
                }
            }
        }

        private static void HandleLinkageSpecification(TSNode node, string src, IList<OutlineSymbolItem> siblings, AccessLevel access, bool insideFunctionBody)
        {
            // extern "C" { ... } — recurse into the body
            var body = node.child_by_field_name("body");
            if (!body.is_null())
                CollectSymbols(body, src, siblings, access, insideFunctionBody);

            // extern "C" single-declaration  (no body, the declaration is a direct child)
            var value = node.child_by_field_name("value");
            if (!value.is_null())
            {
                string valType = value.type();
                if (valType == "function_definition" && !insideFunctionBody)
                    HandleFunction(value, src, siblings, access);
                else if (valType == "declaration" && !insideFunctionBody)
                    HandleDeclaration(value, src, siblings, access);
            }
        }

        // ────────────────────────────────────────────────────────────────────
        //  Helpers
        // ────────────────────────────────────────────────────────────────────

        private static AccessLevel ParseAccessSpecifier(TSNode node, string src)
        {
            // The node text is something like "public" or "private" or "protected"
            // (the colon is a separate child in some grammars)
            string text = node.text(src).Trim().TrimEnd(':').Trim();
            if (text.StartsWith("private")) return AccessLevel.Private;
            if (text.StartsWith("protected")) return AccessLevel.Protected;
            return AccessLevel.Public;
        }

        private static OutlineSymbolItem MakeItem(string name, OutlineSymbolKind kind, AccessLevel access, TSNode node)
        {
            var start = node.start_point();
            var end = node.end_point();
            return new OutlineSymbolItem(name, kind, access)
            {
                StartLine = (int)start.row + 1,
                StartColumn = (int)start.column,
                EndLine = (int)end.row + 1,
            };
        }

        /// <summary>
        /// Extracts a simple, readable name from a declarator node.
        /// Handles init_declarator, pointer_declarator, reference_declarator, identifier, etc.
        /// </summary>
        private static string ExtractSimpleName(TSNode declarator, string src)
        {
            if (declarator.is_null()) return "<unknown>";

            string type = declarator.type();

            if (type == "identifier" || type == "qualified_identifier"
                || type == "destructor_name" || type == "operator_name")
            {
                return declarator.text(src);
            }

            if (type == "init_declarator")
            {
                var inner = declarator.child_by_field_name("declarator");
                if (!inner.is_null())
                    return ExtractSimpleName(inner, src);
            }

            if (type == "pointer_declarator" || type == "reference_declarator")
            {
                var inner = declarator.child_by_field_name("declarator");
                if (!inner.is_null())
                    return ExtractSimpleName(inner, src);
                // Fall back to scanning children
                for (uint i = 0; i < declarator.child_count(); i++)
                {
                    var child = declarator.child(i);
                    string ct = child.type();
                    if (ct == "identifier" || ct == "qualified_identifier"
                        || ct == "function_declarator" || ct == "init_declarator")
                        return ExtractSimpleName(child, src);
                }
            }

            if (type == "function_declarator")
            {
                var inner = declarator.child_by_field_name("declarator");
                if (!inner.is_null())
                    return ExtractSimpleName(inner, src);
            }

            if (type == "array_declarator")
            {
                var inner = declarator.child_by_field_name("declarator");
                if (!inner.is_null())
                    return ExtractSimpleName(inner, src);
            }

            if (type == "parenthesized_declarator")
            {
                // (identifier) — unwrap
                for (uint i = 0; i < declarator.child_count(); i++)
                {
                    var child = declarator.child(i);
                    if (child.type() != "(" && child.type() != ")")
                        return ExtractSimpleName(child, src);
                }
            }

            // Last resort
            return declarator.text(src);
        }
    }
}
