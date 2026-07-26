using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace LevyFlight
{
    public sealed class HotFileScanner : Scanner
    {
        public override string Id => "HotFile";
        public override string DisplayName => "Hot File";

        internal override Task<IEnumerable<JumpItem>> ScanAsync(ScannerContext context, ScannerDumpSection dump)
        {
            return Task.FromResult(EnumerateItems(context, dump));
        }

        private IEnumerable<JumpItem> EnumerateItems(ScannerContext context, ScannerDumpSection dump)
        {
            dump.Detail("quota: 6 files, recent range [index " + context.RecentIndex + ", " + context.RecentEnd + ")");
            for (int recentCount = 6; recentCount > 0 && context.RecentIndex < context.RecentEnd; context.RecentIndex++)
            {
                string filePath = Path.GetFullPath(context.RecentFiles[context.RecentIndex]);
                dump.Input(filePath + "  [recent #" + context.RecentIndex + "]");
                JumpItem jumpItem = context.CreateClaimedFileItem(this, filePath, dump);
                if (jumpItem != null)
                {
                    dump.Produced(filePath);
                    yield return jumpItem;
                    recentCount--;
                }
            }
        }
    }
}
