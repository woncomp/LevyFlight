using System.Collections.Generic;
using System.Threading.Tasks;

namespace LevyFlight
{
    public sealed class OpenFileScanner : Scanner
    {
        public override string Id => "OpenFile";
        public override string DisplayName => "Open File";

        internal override Task<IEnumerable<JumpItem>> ScanAsync(ScannerContext context, ScannerDumpSection dump)
        {
            return Task.FromResult(EnumerateItems(context, dump));
        }

        private IEnumerable<JumpItem> EnumerateItems(ScannerContext context, ScannerDumpSection dump)
        {
            dump.Detail("active files: " + context.ActiveFiles.Length);
            foreach (string filePath in context.ActiveFiles)
            {
                dump.Input(filePath);
                JumpItem jumpItem = context.CreateClaimedFileItem(this, filePath, dump);
                if (jumpItem != null)
                {
                    dump.Produced(filePath);
                    yield return jumpItem;
                }
            }
        }
    }
}
