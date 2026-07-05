using System;
using System.Collections.Generic;
using System.Linq;

namespace TreeSitterSharp
{
    public enum TSInputEncoding
    {
        TSInputEncodingUTF8,
        TSInputEncodingUTF16
    }

    public enum TSSymbolType
    {
        TSSymbolTypeRegular,
        TSSymbolTypeAnonymous,
        TSSymbolTypeAuxiliary,
    }

    public enum TSLogType
    {
        TSLogTypeParse,
        TSLogTypeLex,
    }

    public enum TSQuantifier
    {
        TSQuantifierZero = 0,
        TSQuantifierZeroOrOne,
        TSQuantifierZeroOrMore,
        TSQuantifierOne,
        TSQuantifierOneOrMore,
    }

    public enum TSQueryPredicateStepType
    {
        TSQueryPredicateStepTypeDone,
        TSQueryPredicateStepTypeCapture,
        TSQueryPredicateStepTypeString,
    }

    public enum TSQueryError
    {
        TSQueryErrorNone = 0,
        TSQueryErrorSyntax,
        TSQueryErrorNodeType,
        TSQueryErrorField,
        TSQueryErrorCapture,
        TSQueryErrorStructure,
        TSQueryErrorLanguage,
    }

    public struct TSPoint
    {
        public uint row;
        public uint column;

        public TSPoint(uint row, uint column)
        {
            this.row = row;
            this.column = column;
        }
    }

    public struct TSRange
    {
        public TSPoint start_point;
        public TSPoint end_point;
        public uint start_byte;
        public uint end_byte;
    }

    public struct TSInputEdit
    {
        public uint start_byte;
        public uint old_end_byte;
        public uint new_end_byte;
        public TSPoint start_point;
        public TSPoint old_end_point;
        public TSPoint new_end_point;
    }

    public struct TSQueryCapture
    {
        public TSNode node;
        public uint index;
    }

    public struct TSQueryMatch
    {
        public uint id;
        public ushort pattern_index;
        public ushort capture_count;
        public IntPtr captures;
    }

    public struct TSQueryPredicateStep
    {
        public TSQueryPredicateStepType type;
        public uint value_id;
    }

    public delegate void TSLogger(TSLogType logType, string message);

    public sealed class TSParser : IDisposable
    {
        private TSLanguage currentLanguage;
        private ulong timeoutMicros;

        public void Dispose()
        {
        }

        public bool set_language(TSLanguage language)
        {
            currentLanguage = language;
            return language != null;
        }

        public TSLanguage language()
        {
            return currentLanguage;
        }

        public bool set_included_ranges(TSRange[] ranges)
        {
            return true;
        }

        public TSRange[] included_ranges()
        {
            return new TSRange[0];
        }

        public TSTree parse_string(TSTree oldTree, string input)
        {
            if (input == null)
                input = string.Empty;

            var lang = currentLanguage ?? CppLanguage();
            return ManagedCppParser.Parse(input, lang);
        }

        public void reset()
        {
        }

        public void set_timeout_micros(ulong timeout)
        {
            timeoutMicros = timeout;
        }

        public ulong timeout_micros()
        {
            return timeoutMicros;
        }

        public void set_logger(TSLogger logger)
        {
        }

        public static TSLanguage CppLanguage()
        {
            return TSLanguage.Cpp;
        }
    }

    public sealed class TSTree : IDisposable
    {
        internal ManagedNode Root { get; private set; }
        internal TSLanguage Language { get; private set; }

        internal TSTree(ManagedNode root, TSLanguage language)
        {
            Root = root;
            Language = language;
        }

        public void Dispose()
        {
            Root = null;
        }

        public TSTree copy()
        {
            return Root != null ? new TSTree(Root, Language) : null;
        }

        public TSNode root_node()
        {
            return new TSNode(Root, Language);
        }

        public TSNode root_node_with_offset(uint offsetBytes, TSPoint offsetPoint)
        {
            return root_node();
        }

        public TSLanguage language()
        {
            return Language;
        }

        public void edit(TSInputEdit edit)
        {
        }

        public TSRange[] get_changed_ranges(TSTree newTree)
        {
            return new TSRange[0];
        }
    }

    public struct TSNode
    {
        internal ManagedNode Node;
        internal TSLanguage Language;

        internal TSNode(ManagedNode node, TSLanguage language)
        {
            Node = node;
            Language = language;
        }

        public void clear()
        {
            Node = null;
            Language = null;
        }

        public string type()
        {
            return Node != null ? Node.Type : null;
        }

        public string type(TSLanguage lang)
        {
            return type();
        }

        public ushort symbol()
        {
            return Language != null ? Language.SymbolForType(type()) : ushort.MaxValue;
        }

        public uint start_offset()
        {
            return Node != null ? (uint)Node.Start : 0;
        }

        public uint end_offset()
        {
            return Node != null ? (uint)Node.End : 0;
        }

        public TSPoint start_point()
        {
            return Node != null ? Node.Source.PointForOffset(Node.Start) : new TSPoint();
        }

        public TSPoint end_point()
        {
            return Node != null ? Node.Source.PointForOffset(Node.End) : new TSPoint();
        }

        public string text(string sourceText)
        {
            if (Node == null)
                return string.Empty;

            int start = Math.Max(0, Math.Min(Node.Start, sourceText.Length));
            int end = Math.Max(start, Math.Min(Node.End, sourceText.Length));
            return sourceText.Substring(start, end - start);
        }

        public string text(TSLanguage lang, string sourceText)
        {
            return text(sourceText);
        }

        public string str(TSLanguage lang)
        {
            return type();
        }

        public bool is_null()
        {
            return Node == null;
        }

        public bool is_named()
        {
            return Node != null && Node.IsNamed;
        }

        public bool is_missing()
        {
            return false;
        }

        public bool is_extra()
        {
            return false;
        }

        public bool has_changes()
        {
            return false;
        }

        public bool has_error()
        {
            return false;
        }

        public TSNode parent()
        {
            return Node != null ? new TSNode(Node.Parent, Language) : new TSNode();
        }

        public TSNode child(uint index)
        {
            if (Node == null || index >= Node.Children.Count)
                return new TSNode();
            return new TSNode(Node.Children[(int)index], Language);
        }

        public string field_name_for_child(uint index)
        {
            if (Node == null || index >= Node.Children.Count)
                return null;
            return Node.Children[(int)index].FieldName;
        }

        public uint child_count()
        {
            return Node != null ? (uint)Node.Children.Count : 0;
        }

        public TSNode named_child(uint index)
        {
            if (Node == null)
                return new TSNode();

            uint seen = 0;
            foreach (var child in Node.Children)
            {
                if (!child.IsNamed)
                    continue;
                if (seen == index)
                    return new TSNode(child, Language);
                seen++;
            }
            return new TSNode();
        }

        public uint named_child_count()
        {
            if (Node == null)
                return 0;
            return (uint)Node.Children.Count(c => c.IsNamed);
        }

        public TSNode child_by_field_name(string fieldName)
        {
            if (Node == null)
                return new TSNode();

            var child = Node.Children.FirstOrDefault(c => c.FieldName == fieldName);
            return child != null ? new TSNode(child, Language) : new TSNode();
        }

        public TSNode child_by_field_id(ushort fieldId)
        {
            if (Language == null)
                return new TSNode();

            return child_by_field_name(Language.field_name_for_id(fieldId));
        }

        public TSNode next_sibling()
        {
            return sibling(1, false);
        }

        public TSNode prev_sibling()
        {
            return sibling(-1, false);
        }

        public TSNode next_named_sibling()
        {
            return sibling(1, true);
        }

        public TSNode prev_named_sibling()
        {
            return sibling(-1, true);
        }

        public TSNode first_child_for_offset(uint offset)
        {
            return firstChildCovering((int)offset, false);
        }

        public TSNode first_named_child_for_offset(uint offset)
        {
            return firstChildCovering((int)offset, true);
        }

        public TSNode descendant_for_offset_range(uint start, uint end)
        {
            return descendantForRange((int)start, (int)end, false);
        }

        public TSNode descendant_for_point_range(TSPoint start, TSPoint end)
        {
            if (Node == null)
                return new TSNode();
            return descendant_for_offset_range((uint)Node.Source.OffsetForPoint(start), (uint)Node.Source.OffsetForPoint(end));
        }

        public TSNode named_descendant_for_offset_range(uint start, uint end)
        {
            return descendantForRange((int)start, (int)end, true);
        }

        public TSNode named_descendant_for_point_range(TSPoint start, TSPoint end)
        {
            if (Node == null)
                return new TSNode();
            return named_descendant_for_offset_range((uint)Node.Source.OffsetForPoint(start), (uint)Node.Source.OffsetForPoint(end));
        }

        public bool eq(TSNode other)
        {
            return ReferenceEquals(Node, other.Node);
        }

        private TSNode sibling(int delta, bool namedOnly)
        {
            if (Node == null || Node.Parent == null)
                return new TSNode();

            var siblings = Node.Parent.Children;
            int index = siblings.IndexOf(Node) + delta;
            while (index >= 0 && index < siblings.Count)
            {
                if (!namedOnly || siblings[index].IsNamed)
                    return new TSNode(siblings[index], Language);
                index += delta;
            }
            return new TSNode();
        }

        private TSNode firstChildCovering(int offset, bool namedOnly)
        {
            if (Node == null)
                return new TSNode();

            foreach (var child in Node.Children)
            {
                if (namedOnly && !child.IsNamed)
                    continue;
                if (child.Start <= offset && child.End >= offset)
                    return new TSNode(child, Language);
            }
            return new TSNode();
        }

        private TSNode descendantForRange(int start, int end, bool namedOnly)
        {
            if (Node == null)
                return new TSNode();

            ManagedNode best = Node;
            bool changed;
            do
            {
                changed = false;
                foreach (var child in best.Children)
                {
                    if (namedOnly && !child.IsNamed)
                        continue;
                    if (child.Start <= start && child.End >= end)
                    {
                        best = child;
                        changed = true;
                        break;
                    }
                }
            } while (changed);

            return new TSNode(best, Language);
        }
    }

    public sealed class TSCursor : IDisposable
    {
        private TSNode node;
        public TSLanguage lang { get; private set; }

        public TSCursor(TSNode node, TSLanguage lang)
        {
            this.node = node;
            this.lang = lang;
        }

        public void Dispose()
        {
        }

        public void reset(TSNode node)
        {
            this.node = node;
        }

        public TSNode current_node()
        {
            return node;
        }

        public string current_field_name()
        {
            if (node.Node == null)
                return null;
            return node.Node.FieldName;
        }

        public ushort current_field_id()
        {
            return lang != null ? lang.field_id_for_name(current_field_name()) : (ushort)0;
        }

        public bool goto_parent()
        {
            var parent = node.parent();
            if (parent.is_null())
                return false;
            node = parent;
            return true;
        }

        public bool goto_next_sibling()
        {
            var sibling = node.next_sibling();
            if (sibling.is_null())
                return false;
            node = sibling;
            return true;
        }

        public bool goto_first_child()
        {
            var child = node.child(0);
            if (child.is_null())
                return false;
            node = child;
            return true;
        }

        public long goto_first_child_for_offset(uint offset)
        {
            var child = node.first_child_for_offset(offset);
            if (child.is_null())
                return -1;
            node = child;
            return 0;
        }

        public long goto_first_child_for_point(TSPoint point)
        {
            if (node.Node == null)
                return -1;
            return goto_first_child_for_offset((uint)node.Node.Source.OffsetForPoint(point));
        }

        public TSCursor copy()
        {
            return new TSCursor(node, lang);
        }
    }

    public sealed class TSQuery : IDisposable
    {
        public void Dispose()
        {
        }

        public uint pattern_count() { return 0; }
        public uint capture_count() { return 0; }
        public uint string_count() { return 0; }
        public uint start_offset_for_pattern(uint patternIndex) { return 0; }
        public IntPtr predicates_for_pattern(uint patternIndex, out uint length) { length = 0; return IntPtr.Zero; }
        public bool is_pattern_rooted(uint patternIndex) { return false; }
        public bool is_pattern_non_local(uint patternIndex) { return false; }
        public bool is_pattern_guaranteed_at_offset(uint offset) { return false; }
        public string capture_name_for_id(uint id, out uint length) { length = 0; return null; }
        public TSQuantifier capture_quantifier_for_id(uint patternId, uint captureId) { return TSQuantifier.TSQuantifierZero; }
        public string string_value_for_id(uint id, out uint length) { length = 0; return null; }
        public void disable_capture(string captureName) { }
        public void disable_pattern(uint patternIndex) { }
    }

    public sealed class TSQueryCursor : IDisposable
    {
        public void Dispose() { }
        public void exec(TSQuery query, TSNode node) { }
        public bool did_exceed_match_limit() { return false; }
        public uint match_limit() { return 0; }
        public void set_match_limit(uint limit) { }
        public void set_range(uint start, uint end) { }
        public void set_point_range(TSPoint start, TSPoint end) { }
        public bool next_match(out TSQueryMatch match, out TSQueryCapture[] captures) { match = new TSQueryMatch(); captures = null; return false; }
        public void remove_match(uint id) { }
        public bool next_capture(out TSQueryMatch match, out uint index) { match = new TSQueryMatch(); index = 0; return false; }
    }

    public sealed class TSLanguage : IDisposable
    {
        private readonly Dictionary<string, ushort> symbolIds = new Dictionary<string, ushort>();

        internal static readonly TSLanguage Cpp = new TSLanguage();

        public string[] symbols;
        public string[] fields;
        public Dictionary<string, ushort> fieldIds;

        private TSLanguage()
        {
            fields = new[] { null, "name", "body", "type", "declarator", "parameters", "value" };
            fieldIds = new Dictionary<string, ushort>();
            for (ushort i = 1; i < fields.Length; i++)
                fieldIds[fields[i]] = i;

            symbols = new string[0];
        }

        public void Dispose()
        {
        }

        public TSQuery query_new(string source, out uint error_offset, out TSQueryError error_type)
        {
            error_offset = 0;
            error_type = TSQueryError.TSQueryErrorNone;
            return new TSQuery();
        }

        public uint symbol_count()
        {
            return (uint)symbolIds.Count;
        }

        public string symbol_name(ushort symbol)
        {
            foreach (var pair in symbolIds)
            {
                if (pair.Value == symbol)
                    return pair.Key;
            }
            return symbol == ushort.MaxValue ? "ERROR" : null;
        }

        public ushort symbol_for_name(string str, bool is_named)
        {
            return SymbolForType(str);
        }

        public uint field_count()
        {
            return (uint)(fields.Length - 1);
        }

        public string field_name_for_id(ushort fieldId)
        {
            return fieldId < fields.Length ? fields[fieldId] : null;
        }

        public ushort field_id_for_name(string str)
        {
            if (str == null)
                return 0;

            ushort id;
            return fieldIds.TryGetValue(str, out id) ? id : (ushort)0;
        }

        public TSSymbolType symbol_type(ushort symbol)
        {
            return TSSymbolType.TSSymbolTypeRegular;
        }

        internal ushort SymbolForType(string type)
        {
            if (type == null)
                return ushort.MaxValue;

            ushort id;
            if (!symbolIds.TryGetValue(type, out id))
            {
                id = (ushort)symbolIds.Count;
                symbolIds[type] = id;
            }
            return id;
        }
    }

    internal sealed class ManagedNode
    {
        internal readonly SourceMap Source;
        internal readonly string Type;
        internal readonly int Start;
        internal readonly int End;
        internal readonly bool IsNamed;
        internal string FieldName;
        internal ManagedNode Parent;
        internal readonly List<ManagedNode> Children = new List<ManagedNode>();

        internal ManagedNode(SourceMap source, string type, int start, int end, string fieldName = null, bool isNamed = true)
        {
            Source = source;
            Type = type;
            Start = Math.Max(0, start);
            End = Math.Max(Start, end);
            FieldName = fieldName;
            IsNamed = isNamed;
        }

        internal void Add(ManagedNode child)
        {
            if (child == null)
                return;

            child.Parent = this;
            Children.Add(child);
        }
    }

    internal sealed class SourceMap
    {
        private readonly string text;
        private readonly int[] lineStarts;

        internal SourceMap(string text)
        {
            this.text = text ?? string.Empty;

            var starts = new List<int> { 0 };
            for (int i = 0; i < this.text.Length; i++)
            {
                if (this.text[i] == '\n')
                    starts.Add(i + 1);
            }
            lineStarts = starts.ToArray();
        }

        internal TSPoint PointForOffset(int offset)
        {
            offset = Math.Max(0, Math.Min(offset, text.Length));
            int row = Array.BinarySearch(lineStarts, offset);
            if (row < 0)
                row = ~row - 1;

            return new TSPoint((uint)Math.Max(0, row), (uint)(offset - lineStarts[Math.Max(0, row)]));
        }

        internal int OffsetForPoint(TSPoint point)
        {
            int row = (int)Math.Min(point.row, (uint)(lineStarts.Length - 1));
            return Math.Max(0, Math.Min(text.Length, lineStarts[row] + (int)point.column));
        }
    }

    internal static class ManagedCppParser
    {
        private static readonly HashSet<string> ControlKeywords = new HashSet<string>
        {
            "if", "for", "while", "switch", "catch", "sizeof", "alignof"
        };

        internal static TSTree Parse(string text, TSLanguage language)
        {
            var source = new SourceMap(text);
            var root = new ManagedNode(source, "translation_unit", 0, text.Length);
            ParseRange(text, source, root, 0, text.Length, false);
            return new TSTree(root, language);
        }

        private static void ParseRange(string text, SourceMap source, ManagedNode parent, int start, int end, bool classBody)
        {
            int segmentStart = start;
            int i = start;

            while (i < end)
            {
                int beforeTrivia = i;
                i = SkipTrivia(text, i, end);
                if (segmentStart == beforeTrivia)
                    segmentStart = i;
                if (i >= end)
                    break;

                if (text[i] == '#')
                {
                    int lineEnd = LineEnd(text, i, end);
                    AddPreprocessorNode(text, source, parent, i, lineEnd);
                    i = lineEnd + 1;
                    segmentStart = i;
                    continue;
                }

                if (classBody && TryReadAccessSpecifier(text, source, parent, i, end, out i))
                {
                    segmentStart = i;
                    continue;
                }

                if (IsAtSegmentStart(text, segmentStart, i) && TrySkipStandaloneMacroInvocation(text, i, end, out i))
                {
                    segmentStart = i;
                    continue;
                }

                if (MatchesKeyword(text, i, "template"))
                {
                    int templateStart = i;
                    int afterTemplate = FindTemplateEnd(text, i, end);
                    var templateNode = new ManagedNode(source, "template_declaration", templateStart, afterTemplate);
                    ParseRange(text, source, templateNode, afterTemplate, end, classBody);
                    if (templateNode.Children.Count > 0)
                    {
                        var child = templateNode.Children[0];
                        parent.Add(ClampNodeEnd(templateNode, child.End));
                        i = child.End;
                        segmentStart = i;
                        continue;
                    }
                }

                if (IsAtSegmentStart(text, segmentStart, i))
                {
                    var compound = TryReadCompound(text, source, i, end);
                    if (compound != null)
                    {
                        parent.Add(compound);
                        i = compound.End;
                        segmentStart = i;
                        continue;
                    }
                }

                if (text[i] == '{')
                {
                    int close = FindMatching(text, i, end, '{', '}');
                    if (close >= 0)
                    {
                        var function = TryBuildFunction(text, source, segmentStart, close + 1, i);
                        if (function != null)
                        {
                            parent.Add(function);
                            i = close + 1;
                            segmentStart = i;
                            continue;
                        }

                        i = close + 1;
                        segmentStart = i;
                        continue;
                    }
                }

                if (text[i] == ';')
                {
                    AddDeclaration(text, source, parent, segmentStart, i + 1, classBody);
                    i++;
                    segmentStart = i;
                    continue;
                }

                i = SkipToken(text, i, end);
            }
        }

        private static ManagedNode ClampNodeEnd(ManagedNode node, int end)
        {
            var replacement = new ManagedNode(node.Source, node.Type, node.Start, end, node.FieldName, node.IsNamed);
            foreach (var child in node.Children)
                replacement.Add(child);
            return replacement;
        }

        private static bool IsAtSegmentStart(string text, int segmentStart, int position)
        {
            int firstToken = SkipTrivia(text, segmentStart, position);
            return firstToken == position;
        }

        private static bool TrySkipStandaloneMacroInvocation(string text, int start, int end, out int next)
        {
            next = start;
            int nameEnd = ReadIdentifier(text, start, end);
            if (nameEnd <= start)
                return false;

            string name = Slice(text, start, nameEnd);
            if (!LooksLikeMacroName(name))
                return false;

            int openParen = SkipWhitespace(text, nameEnd, end);
            if (openParen >= end || text[openParen] != '(')
                return false;

            int lineEnd = LineEnd(text, openParen, end);
            int closeParen = FindMatching(text, openParen, lineEnd, '(', ')');
            if (closeParen < 0)
                return false;

            int rest = SkipWhitespace(text, closeParen + 1, lineEnd);
            if (rest != lineEnd)
                return false;

            next = lineEnd < end ? lineEnd + 1 : lineEnd;
            return true;
        }

        private static bool LooksLikeMacroName(string name)
        {
            bool hasUpper = false;
            for (int i = 0; i < name.Length; i++)
            {
                char c = name[i];
                if (char.IsLower(c))
                    return false;
                if (char.IsUpper(c))
                    hasUpper = true;
            }
            return hasUpper;
        }

        private static ManagedNode TryReadCompound(string text, SourceMap source, int start, int end)
        {
            string keyword = null;
            string nodeType = null;

            if (MatchesKeyword(text, start, "namespace"))
            {
                keyword = "namespace";
                nodeType = "namespace_definition";
            }
            else if (MatchesKeyword(text, start, "class"))
            {
                keyword = "class";
                nodeType = "class_specifier";
            }
            else if (MatchesKeyword(text, start, "struct"))
            {
                keyword = "struct";
                nodeType = "struct_specifier";
            }
            else if (MatchesKeyword(text, start, "union"))
            {
                keyword = "union";
                nodeType = "union_specifier";
            }
            else if (MatchesKeyword(text, start, "enum"))
            {
                keyword = "enum";
                nodeType = "enum_specifier";
            }

            if (keyword == null)
                return null;

            int brace = FindNextSignificant(text, start + keyword.Length, end, '{');
            if (brace < 0)
                return null;

            int close = FindMatching(text, brace, end, '{', '}');
            if (close < 0)
                return null;

            int nodeEnd = close + 1;
            while (nodeEnd < end && char.IsWhiteSpace(text[nodeEnd]))
                nodeEnd++;
            if (nodeEnd < end && text[nodeEnd] == ';')
                nodeEnd++;

            var node = new ManagedNode(source, nodeType, start, nodeEnd);
            AddNameField(text, source, node, start + keyword.Length, brace);

            var body = new ManagedNode(source, keyword == "enum" ? "enumerator_list" : "field_declaration_list", brace, close + 1, "body");
            node.Add(body);

            if (keyword == "enum")
                ParseEnumBody(text, source, body, brace + 1, close);
            else
                ParseRange(text, source, body, brace + 1, close, keyword != "namespace");

            return node;
        }

        private static bool TryReadAccessSpecifier(string text, SourceMap source, ManagedNode parent, int start, int end, out int next)
        {
            next = start;
            string keyword = null;
            if (MatchesKeyword(text, start, "public"))
                keyword = "public";
            else if (MatchesKeyword(text, start, "private"))
                keyword = "private";
            else if (MatchesKeyword(text, start, "protected"))
                keyword = "protected";

            if (keyword == null)
                return false;

            int colon = SkipWhitespace(text, start + keyword.Length, end);
            if (colon >= end || text[colon] != ':')
                return false;

            parent.Add(new ManagedNode(source, "access_specifier", start, colon + 1));
            next = colon + 1;
            return true;
        }

        private static void ParseEnumBody(string text, SourceMap source, ManagedNode parent, int start, int end)
        {
            int i = start;
            while (i < end)
            {
                i = SkipTrivia(text, i, end);
                int nameStart = i;
                if (!IsIdentifierStart(CharAt(text, i)))
                {
                    i++;
                    continue;
                }

                i = ReadQualifiedIdentifier(text, i, end);
                var enumerator = new ManagedNode(source, "enumerator", nameStart, i);
                enumerator.Add(new ManagedNode(source, "identifier", nameStart, i, "name"));
                parent.Add(enumerator);

                while (i < end && text[i] != ',')
                    i = SkipToken(text, i, end);
                if (i < end && text[i] == ',')
                    i++;
            }
        }

        private static void AddPreprocessorNode(string text, SourceMap source, ManagedNode parent, int start, int end)
        {
            int i = start + 1;
            i = SkipWhitespace(text, i, end);
            int directiveStart = i;
            i = ReadIdentifier(text, i, end);
            string directive = Slice(text, directiveStart, i);
            if (directive != "define")
                return;

            i = SkipWhitespace(text, i, end);
            int nameStart = i;
            i = ReadIdentifier(text, i, end);
            if (i <= nameStart)
                return;

            bool functionLike = i < end && text[i] == '(';
            var node = new ManagedNode(source, functionLike ? "preproc_function_def" : "preproc_def", start, end);
            node.Add(new ManagedNode(source, "identifier", nameStart, i, "name"));
            if (functionLike)
            {
                int paramsEnd = FindMatching(text, i, end, '(', ')');
                if (paramsEnd >= 0)
                    node.Add(new ManagedNode(source, "preproc_params", i, paramsEnd + 1, "parameters"));
            }
            parent.Add(node);
        }

        private static void AddDeclaration(string text, SourceMap source, ManagedNode parent, int start, int end, bool classBody)
        {
            start = SkipTrivia(text, start, end);
            int trimmedEnd = TrimEnd(text, start, end);
            if (trimmedEnd <= start)
                return;

            string declarationText = Slice(text, start, trimmedEnd);
            if (declarationText.StartsWith("using ", StringComparison.Ordinal))
            {
                int equals = declarationText.IndexOf('=');
                if (equals > 0)
                {
                    int nameStart = start + "using ".Length;
                    int nameEnd = start + equals;
                    nameEnd = TrimEnd(text, nameStart, nameEnd);
                    var alias = new ManagedNode(source, "alias_declaration", start, end);
                    alias.Add(new ManagedNode(source, "type_identifier", nameStart, nameEnd, "name"));
                    parent.Add(alias);
                }
                return;
            }

            if (declarationText.StartsWith("typedef ", StringComparison.Ordinal))
            {
                int nameStart = FindLastIdentifierStart(text, start, trimmedEnd);
                if (nameStart >= 0)
                {
                    int nameEnd = ReadIdentifier(text, nameStart, trimmedEnd);
                    var typedef = new ManagedNode(source, "type_definition", start, end);
                    typedef.Add(new ManagedNode(source, IdentifierNodeType(Slice(text, nameStart, nameEnd)), nameStart, nameEnd, "declarator"));
                    parent.Add(typedef);
                }
                return;
            }

            if (LooksLikeControlStatement(declarationText))
                return;

            var declaration = new ManagedNode(source, classBody ? "field_declaration" : "declaration", start, end);
            AddTypeAndDeclaratorFields(text, source, declaration, start, trimmedEnd);
            if (declaration.Children.Count > 0)
                parent.Add(declaration);
        }

        private static ManagedNode TryBuildFunction(string text, SourceMap source, int segmentStart, int end, int bodyStart)
        {
            int start = TrimStartAfterBoundary(text, segmentStart, bodyStart);
            int signatureEnd = TrimEnd(text, start, bodyStart);
            if (signatureEnd <= start)
                return null;

            int closeParen = LastIndexOfChar(text, start, signatureEnd, ')');
            int openParen = -1;
            int nameStart = -1;
            int nameEnd = -1;
            string name = null;

            while (closeParen >= start)
            {
                openParen = FindMatchingReverse(text, closeParen, start, '(', ')');
                if (openParen < 0)
                    return null;

                nameEnd = TrimEnd(text, start, openParen);
                nameStart = FindNameStartBefore(text, start, nameEnd);
                if (nameStart >= start)
                {
                    name = Slice(text, nameStart, nameEnd).Trim();
                    if (IsFunctionNameCandidate(name) && !IsInitializerListEntry(text, start, nameStart))
                        break;
                }

                closeParen = LastIndexOfChar(text, start, openParen, ')');
            }

            if (!IsFunctionNameCandidate(name))
                return null;

            var node = new ManagedNode(source, "function_definition", start, end);

            int typeEnd = TrimEnd(text, start, nameStart);
            if (typeEnd > start)
                node.Add(new ManagedNode(source, "primitive_type", start, typeEnd, "type"));

            node.Add(BuildFunctionDeclarator(text, source, nameStart, closeParen + 1, nameStart, nameEnd, openParen, closeParen, "declarator"));
            node.Add(new ManagedNode(source, "compound_statement", bodyStart, end, "body"));
            return node;
        }

        private static void AddTypeAndDeclaratorFields(string text, SourceMap source, ManagedNode node, int start, int end)
        {
            int equals = FindTopLevelChar(text, start, end, '=');
            int declaratorEnd = equals >= 0 ? equals : end;
            if (declaratorEnd > start && text[declaratorEnd - 1] == ';')
                declaratorEnd--;
            declaratorEnd = TrimEnd(text, start, declaratorEnd);

            int closeParen = LastIndexOfChar(text, start, declaratorEnd, ')');
            if (closeParen >= 0)
            {
                int openParen = FindMatchingReverse(text, closeParen, start, '(', ')');
                if (openParen >= 0)
                {
                    int nameEnd = TrimEnd(text, start, openParen);
                    int nameStart = FindNameStartBefore(text, start, nameEnd);
                    if (nameStart >= start)
                    {
                        int typeEnd = TrimEnd(text, start, nameStart);
                        if (typeEnd > start)
                            node.Add(new ManagedNode(source, "primitive_type", start, typeEnd, "type"));
                        node.Add(BuildFunctionDeclarator(text, source, nameStart, closeParen + 1, nameStart, nameEnd, openParen, closeParen, "declarator"));
                        return;
                    }
                }
            }

            int simpleNameStart = FindLastIdentifierStart(text, start, declaratorEnd);
            if (simpleNameStart < start)
                return;

            int simpleNameEnd = ReadIdentifier(text, simpleNameStart, declaratorEnd);
            int simpleTypeEnd = TrimEnd(text, start, simpleNameStart);
            if (simpleTypeEnd > start)
                node.Add(new ManagedNode(source, "primitive_type", start, simpleTypeEnd, "type"));

            string name = Slice(text, simpleNameStart, simpleNameEnd);
            node.Add(new ManagedNode(source, IdentifierNodeType(name), simpleNameStart, simpleNameEnd, "declarator"));
        }

        private static ManagedNode BuildFunctionDeclarator(string text, SourceMap source, int start, int end, int nameStart, int nameEnd, int openParen, int closeParen, string fieldName)
        {
            var function = new ManagedNode(source, "function_declarator", start, end, fieldName);
            string name = Slice(text, nameStart, nameEnd).Trim();
            int adjustedNameStart = nameStart + (Slice(text, nameStart, nameEnd).Length - Slice(text, nameStart, nameEnd).TrimStart().Length);
            int adjustedNameEnd = adjustedNameStart + name.Length;

            function.Add(new ManagedNode(source, IdentifierNodeType(name), adjustedNameStart, adjustedNameEnd, "declarator"));
            function.Add(new ManagedNode(source, "parameter_list", openParen, closeParen + 1, "parameters"));
            ParseParameters(text, source, function.Children[1], openParen + 1, closeParen);
            return function;
        }

        private static void ParseParameters(string text, SourceMap source, ManagedNode parent, int start, int end)
        {
            int itemStart = start;
            int depth = 0;
            for (int i = start; i <= end; i++)
            {
                char c = i < end ? text[i] : ',';
                if (c == '<' || c == '(' || c == '[')
                    depth++;
                else if (c == '>' || c == ')' || c == ']')
                    depth = Math.Max(0, depth - 1);

                if (c == ',' && depth == 0)
                {
                    int pStart = TrimStart(text, itemStart, i);
                    int pEnd = TrimEnd(text, pStart, i);
                    if (pEnd > pStart)
                    {
                        var param = new ManagedNode(source, Slice(text, pStart, pEnd) == "..." ? "variadic_parameter_declaration" : "parameter_declaration", pStart, pEnd);
                        int typeEnd = GuessParameterTypeEnd(text, pStart, pEnd);
                        if (typeEnd > pStart)
                            param.Add(new ManagedNode(source, "primitive_type", pStart, typeEnd, "type"));
                        parent.Add(param);
                    }
                    itemStart = i + 1;
                }
            }
        }

        private static int GuessParameterTypeEnd(string text, int start, int end)
        {
            int nameStart = FindLastIdentifierStart(text, start, end);
            if (nameStart <= start)
                return end;

            string last = Slice(text, nameStart, ReadIdentifier(text, nameStart, end));
            if (IsCppKeyword(last))
                return end;
            return TrimEnd(text, start, nameStart);
        }

        private static void AddNameField(string text, SourceMap source, ManagedNode node, int start, int end)
        {
            int i = SkipWhitespace(text, start, end);
            if (MatchesKeyword(text, i, "class") || MatchesKeyword(text, i, "struct"))
                i = SkipWhitespace(text, i + 5, end);

            int nameStart = i;
            i = ReadQualifiedIdentifier(text, i, end);
            if (i > nameStart)
                node.Add(new ManagedNode(source, IdentifierNodeType(Slice(text, nameStart, i)), nameStart, i, "name"));
        }

        private static string IdentifierNodeType(string name)
        {
            if (name == null)
                return "identifier";
            if (name.Contains("::"))
                return "qualified_identifier";
            if (name.StartsWith("~", StringComparison.Ordinal))
                return "destructor_name";
            if (name.StartsWith("operator", StringComparison.Ordinal))
                return "operator_name";
            return "identifier";
        }

        private static bool IsCppKeyword(string value)
        {
            switch (value)
            {
                case "const":
                case "volatile":
                case "unsigned":
                case "signed":
                case "long":
                case "short":
                case "int":
                case "char":
                case "float":
                case "double":
                case "void":
                case "bool":
                case "auto":
                    return true;
                default:
                    return false;
            }
        }

        private static bool LooksLikeControlStatement(string value)
        {
            value = value.TrimStart();
            return value.StartsWith("if", StringComparison.Ordinal)
                || value.StartsWith("for", StringComparison.Ordinal)
                || value.StartsWith("while", StringComparison.Ordinal)
                || value.StartsWith("switch", StringComparison.Ordinal)
                || value.StartsWith("return", StringComparison.Ordinal);
        }

        private static bool IsFunctionNameCandidate(string name)
        {
            if (string.IsNullOrEmpty(name))
                return false;

            if (ControlKeywords.Contains(name))
                return false;

            // noexcept(false) and similar trailing specifiers can look like the
            // last parameter list if we scan backward from the opening brace.
            return name != "noexcept";
        }

        private static bool IsInitializerListEntry(string text, int start, int nameStart)
        {
            int i = nameStart - 1;
            while (i >= start && char.IsWhiteSpace(text[i]))
                i--;

            return i >= start && (text[i] == ':' || text[i] == ',');
        }

        private static int FindTemplateEnd(string text, int start, int end)
        {
            int lt = FindNextSignificant(text, start, end, '<');
            if (lt < 0)
                return start + "template".Length;
            int gt = FindMatching(text, lt, end, '<', '>');
            return gt >= 0 ? gt + 1 : start + "template".Length;
        }

        private static int TrimStartAfterBoundary(string text, int start, int end)
        {
            int best = start;
            for (int i = end - 1; i >= start; i--)
            {
                char c = text[i];
                if (c == ';' || c == '}' || c == '{' || c == '#')
                {
                    best = i + 1;
                    break;
                }
            }
            return SkipTrivia(text, best, end);
        }

        private static int FindNameStartBefore(string text, int start, int end)
        {
            end = TrimEnd(text, start, end);
            if (end <= start)
                return -1;

            int i = end - 1;
            while (i >= start && (IsIdentifierPart(text[i]) || text[i] == ':' || text[i] == '~'))
                i--;

            int candidate = i + 1;
            if (candidate < end)
            {
                int opIndex = LastIndexOfWord(text, "operator", start, end);
                if (opIndex >= 0)
                    return opIndex;
                return candidate;
            }

            string op = "operator";
            int fallbackOpIndex = LastIndexOfWord(text, op, start, end);
            return fallbackOpIndex >= 0 ? fallbackOpIndex : -1;
        }

        private static int FindLastIdentifierStart(string text, int start, int end)
        {
            int i = end - 1;
            while (i >= start)
            {
                if (IsIdentifierPart(text[i]))
                {
                    int finish = i + 1;
                    while (i >= start && IsIdentifierPart(text[i]))
                        i--;
                    return i + 1 < finish ? i + 1 : -1;
                }
                i--;
            }
            return -1;
        }

        private static int LastIndexOfWord(string text, string word, int start, int end)
        {
            for (int i = end - word.Length; i >= start; i--)
            {
                if (MatchesKeyword(text, i, word))
                    return i;
            }
            return -1;
        }

        private static int FindTopLevelChar(string text, int start, int end, char target)
        {
            int depth = 0;
            for (int i = start; i < end; i++)
            {
                char c = text[i];
                if (c == '"' || c == '\'')
                    i = SkipString(text, i, end);
                else if (c == '(' || c == '[' || c == '{' || c == '<')
                    depth++;
                else if (c == ')' || c == ']' || c == '}' || c == '>')
                    depth = Math.Max(0, depth - 1);
                else if (c == target && depth == 0)
                    return i;
            }
            return -1;
        }

        private static int FindNextSignificant(string text, int start, int end, char target)
        {
            for (int i = start; i < end; i = SkipToken(text, i, end))
            {
                if (text[i] == target)
                    return i;
            }
            return -1;
        }

        private static int FindPreviousSignificant(string text, int start, int end, char target)
        {
            for (int i = start; i >= end; i--)
            {
                if (char.IsWhiteSpace(text[i]))
                    continue;
                return text[i] == target ? i : -1;
            }
            return -1;
        }

        private static int LastIndexOfChar(string text, int start, int end, char target)
        {
            for (int i = end - 1; i >= start; i--)
            {
                if (text[i] == target)
                    return i;
            }
            return -1;
        }

        private static int FindMatching(string text, int open, int end, char openChar, char closeChar)
        {
            int depth = 0;
            for (int i = open; i < end; i++)
            {
                char c = text[i];
                if (c == '"' || c == '\'')
                    i = SkipString(text, i, end);
                else if (c == '/' && i + 1 < end && text[i + 1] == '/')
                    i = LineEnd(text, i, end);
                else if (c == '/' && i + 1 < end && text[i + 1] == '*')
                    i = SkipBlockComment(text, i, end);
                else if (c == openChar)
                    depth++;
                else if (c == closeChar)
                {
                    depth--;
                    if (depth == 0)
                        return i;
                }
            }
            return -1;
        }

        private static int FindMatchingReverse(string text, int close, int start, char openChar, char closeChar)
        {
            int depth = 0;
            for (int i = close; i >= start; i--)
            {
                char c = text[i];
                if (c == closeChar)
                    depth++;
                else if (c == openChar)
                {
                    depth--;
                    if (depth == 0)
                        return i;
                }
            }
            return -1;
        }

        private static bool MatchesKeyword(string text, int index, string keyword)
        {
            if (index < 0 || index + keyword.Length > text.Length)
                return false;
            if (string.Compare(text, index, keyword, 0, keyword.Length, StringComparison.Ordinal) != 0)
                return false;

            char before = index > 0 ? text[index - 1] : '\0';
            char after = index + keyword.Length < text.Length ? text[index + keyword.Length] : '\0';
            return !IsIdentifierPart(before) && !IsIdentifierPart(after);
        }

        private static int SkipTrivia(string text, int start, int end)
        {
            int i = start;
            while (i < end)
            {
                if (char.IsWhiteSpace(text[i]))
                {
                    i++;
                    continue;
                }
                if (text[i] == '/' && i + 1 < end && text[i + 1] == '/')
                {
                    i = LineEnd(text, i, end);
                    continue;
                }
                if (text[i] == '/' && i + 1 < end && text[i + 1] == '*')
                {
                    i = SkipBlockComment(text, i, end) + 1;
                    continue;
                }
                break;
            }
            return i;
        }

        private static int SkipToken(string text, int i, int end)
        {
            if (i >= end)
                return end;
            if (text[i] == '"' || text[i] == '\'')
                return SkipString(text, i, end) + 1;
            if (text[i] == '/' && i + 1 < end && text[i + 1] == '/')
                return LineEnd(text, i, end) + 1;
            if (text[i] == '/' && i + 1 < end && text[i + 1] == '*')
                return SkipBlockComment(text, i, end) + 1;
            return i + 1;
        }

        private static int SkipString(string text, int i, int end)
        {
            char quote = text[i];
            i++;
            while (i < end)
            {
                if (text[i] == '\\')
                {
                    i += 2;
                    continue;
                }
                if (text[i] == quote)
                    return i;
                i++;
            }
            return end - 1;
        }

        private static int SkipBlockComment(string text, int i, int end)
        {
            i += 2;
            while (i + 1 < end)
            {
                if (text[i] == '*' && text[i + 1] == '/')
                    return i + 1;
                i++;
            }
            return end - 1;
        }

        private static int ReadQualifiedIdentifier(string text, int start, int end)
        {
            int i = start;
            if (i < end && text[i] == '~')
                i++;
            i = ReadIdentifier(text, i, end);
            while (i + 1 < end && text[i] == ':' && text[i + 1] == ':')
            {
                i += 2;
                if (i < end && text[i] == '~')
                    i++;
                i = ReadIdentifier(text, i, end);
            }
            return i;
        }

        private static int ReadIdentifier(string text, int start, int end)
        {
            int i = start;
            if (i >= end || !IsIdentifierStart(text[i]))
                return start;
            i++;
            while (i < end && IsIdentifierPart(text[i]))
                i++;
            return i;
        }

        private static bool IsIdentifierStart(char c)
        {
            return char.IsLetter(c) || c == '_';
        }

        private static bool IsIdentifierPart(char c)
        {
            return char.IsLetterOrDigit(c) || c == '_';
        }

        private static char CharAt(string text, int index)
        {
            return index >= 0 && index < text.Length ? text[index] : '\0';
        }

        private static int SkipWhitespace(string text, int start, int end)
        {
            while (start < end && char.IsWhiteSpace(text[start]))
                start++;
            return start;
        }

        private static int TrimStart(string text, int start, int end)
        {
            while (start < end && char.IsWhiteSpace(text[start]))
                start++;
            return start;
        }

        private static int TrimEnd(string text, int start, int end)
        {
            while (end > start && char.IsWhiteSpace(text[end - 1]))
                end--;
            return end;
        }

        private static int LineEnd(string text, int start, int end)
        {
            int i = start;
            while (i < end && text[i] != '\n')
                i++;
            return i;
        }

        private static string Slice(string text, int start, int end)
        {
            start = Math.Max(0, Math.Min(start, text.Length));
            end = Math.Max(start, Math.Min(end, text.Length));
            return text.Substring(start, end - start);
        }
    }
}
