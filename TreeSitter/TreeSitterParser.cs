using System.Collections.Generic;

namespace LevyFlight.TreeSitter
{
    internal static class TreeSitterParser
    {
        public static SyntaxTree Parse(string sourceText)
        {
            switch (LevyFlightOptions.TreeSitterEngine)
            {
                case TreeSitterEngine.Managed:
                    return TreeSitterSharpParser.Parse(sourceText);
                default:
                    return TreeSitterNativeParser.Parse(sourceText);
            }
        }

        public static string CurrentEngineName
        {
            get
            {
                return LevyFlightOptions.TreeSitterEngine == TreeSitterEngine.Managed
                    ? "managed"
                    : "native";
            }
        }
    }
}
