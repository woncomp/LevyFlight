using System.Collections.Generic;
using TreeSitterSharp;

namespace LevyFlight.TreeSitter
{
    internal static class TreeSitterSharpParser
    {
        public static SyntaxTree Parse(string sourceText)
        {
            using (var parser = new TSParser())
            using (var lang = TSParser.CppLanguage())
            {
                parser.set_language(lang);
                using (var tree = parser.parse_string(null, sourceText))
                {
                    var root = Convert(tree.root_node(), sourceText);
                    return new SyntaxTree(root);
                }
            }
        }

        private static SyntaxNode Convert(TreeSitterSharp.TSNode node, string sourceText)
        {
            if (node.is_null())
                return SyntaxNode.Null;

            uint count = node.child_count();
            var children = new List<SyntaxNode>((int)count);
            for (uint i = 0; i < count; i++)
                children.Add(Convert(node.child(i), sourceText));

            var start = node.start_point();
            var end = node.end_point();

            return new SyntaxNode(
                node.type(),
                node.text(sourceText),
                node.is_named(),
                node.Node?.FieldName,
                new SyntaxPoint(start.row, start.column),
                new SyntaxPoint(end.row, end.column),
                children);
        }
    }
}
