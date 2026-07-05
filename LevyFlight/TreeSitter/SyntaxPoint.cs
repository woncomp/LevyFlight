namespace LevyFlight.TreeSitter
{
    public readonly struct SyntaxPoint
    {
        public uint Row { get; }
        public uint Column { get; }

        public SyntaxPoint(uint row, uint column)
        {
            Row = row;
            Column = column;
        }
    }
}
