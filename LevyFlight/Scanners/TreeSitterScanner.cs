using LevyFlight.TreeSitter;
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

        internal override async Task<IEnumerable<JumpItem>> ScanAsync(ScannerContext context, ScannerDumpSection dump)
        {
            if (string.IsNullOrEmpty(context.CurrentFile))
            {
                dump.Detail("no current file, scan skipped");
                return new List<JumpItem>();
            }

            dump.Detail("parse " + context.CurrentFile + " with engine " + TreeSitterParser.CurrentEngineName);
            List<JumpItem> items = await TreeSitterCodeParser.ParseAndListFunctionsAsync(context.CurrentFile, this);
            foreach (JumpItem item in items)
            {
                dump.Input(item.Name);
                dump.Produced(context.CurrentFile, item.Name + " at line " + (item.LineNumber + 1) + (item.IsDeclaration ? ", declaration" : string.Empty));
            }

            return items;
        }
    }
}
