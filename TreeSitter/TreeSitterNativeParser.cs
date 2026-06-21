using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using TreeSitterNative;

namespace LevyFlight.TreeSitter
{
    internal static class TreeSitterNativeParser
    {
        public static SyntaxTree Parse(string sourceText)
        {
            using (var parser = new TSParser())
            using (var lang = TSParser.CppLanguage())
            {
                parser.set_language(lang);
                using (var tree = parser.parse_string(null, sourceText))
                {
                    var root = Convert(tree.root_node(), sourceText, null);
                    return new SyntaxTree(root);
                }
            }
        }

        private static SyntaxNode Convert(TreeSitterNative.TSNode node, string sourceText, string fieldName)
        {
            if (node.is_null())
                return SyntaxNode.Null;

            uint count = node.child_count();
            var children = new List<SyntaxNode>((int)count);
            for (uint i = 0; i < count; i++)
            {
                string childFieldName = GetFieldName(node, i);
                children.Add(Convert(node.child(i), sourceText, childFieldName));
            }

            return BuildNode(node, sourceText, fieldName, children);
        }

        private static SyntaxNode BuildNode(TreeSitterNative.TSNode node, string sourceText,
            string fieldName, List<SyntaxNode> children)
        {
            var start = node.start_point();
            var end = node.end_point();

            return new SyntaxNode(
                node.type(),
                node.text(sourceText),
                node.is_named(),
                fieldName,
                new SyntaxPoint(start.row, start.column),
                new SyntaxPoint(end.row, end.column),
                children);
        }

        private static string GetFieldName(TreeSitterNative.TSNode node, uint childIndex)
        {
            try
            {
                IntPtr ptr = node.field_name_for_child(childIndex);
                return ptr != IntPtr.Zero ? Marshal.PtrToStringAnsi(ptr) : null;
            }
            catch
            {
                return null;
            }
        }
    }
}
