namespace LevyFlight.TreeSitter
{
    public sealed class SyntaxTree
    {
        public SyntaxNode Root { get; }

        public SyntaxTree(SyntaxNode root)
        {
            Root = root ?? SyntaxNode.Null;
        }
    }
}
