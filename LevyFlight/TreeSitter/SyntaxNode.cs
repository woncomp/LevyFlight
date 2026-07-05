using System;
using System.Collections.Generic;

namespace LevyFlight.TreeSitter
{
    public sealed class SyntaxNode
    {
        public static SyntaxNode Null { get; } = new SyntaxNode();

        public string Type { get; }
        public string Text { get; }
        public bool IsNamed { get; }
        public string FieldName { get; }
        public SyntaxPoint Start { get; }
        public SyntaxPoint End { get; }
        public IReadOnlyList<SyntaxNode> Children { get; }
        public bool IsNull { get; }

        private SyntaxNode()
        {
            Type = string.Empty;
            Text = string.Empty;
            IsNamed = false;
            FieldName = null;
            Start = new SyntaxPoint(0, 0);
            End = new SyntaxPoint(0, 0);
            Children = Array.Empty<SyntaxNode>();
            IsNull = true;
        }

        internal SyntaxNode(string type, string text, bool isNamed, string fieldName,
                            SyntaxPoint start, SyntaxPoint end,
                            IReadOnlyList<SyntaxNode> children)
        {
            Type = type ?? string.Empty;
            Text = text ?? string.Empty;
            IsNamed = isNamed;
            FieldName = fieldName;
            Start = start;
            End = end;
            Children = children ?? Array.Empty<SyntaxNode>();
            IsNull = false;
        }

        public SyntaxNode ChildByFieldName(string fieldName)
        {
            if (string.IsNullOrEmpty(fieldName))
                return Null;

            for (int i = 0; i < Children.Count; i++)
            {
                var child = Children[i];
                if (!child.IsNull && string.Equals(child.FieldName, fieldName, StringComparison.Ordinal))
                    return child;
            }

            return Null;
        }
    }
}
