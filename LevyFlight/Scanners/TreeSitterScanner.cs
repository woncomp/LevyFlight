using Microsoft.VisualStudio.Imaging;
using Microsoft.VisualStudio.Imaging.Interop;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace LevyFlight
{
    public sealed class TreeSitterScanner : Scanner
    {
        public override string Id => "TreeSitter";
        public override string DisplayName => "Tree-sitter";

        internal override ImageMoniker GetIconMoniker(string filePath)
        {
            return KnownMonikers.MethodPublic;
        }

        internal override async Task<IEnumerable<JumpItem>> ScanAsync(ScannerContext context)
        {
            if (string.IsNullOrEmpty(context.CurrentFile))
            {
                return new List<JumpItem>();
            }

            return await TreeSitterCodeParser.ParseAndListFunctionsAsync(context.CurrentFile, this);
        }
    }
}
