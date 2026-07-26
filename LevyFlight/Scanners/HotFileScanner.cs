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

        internal override Task<IEnumerable<JumpItem>> ScanAsync(ScannerContext context)
        {
            return Task.FromResult(EnumerateItems(context));
        }

        private IEnumerable<JumpItem> EnumerateItems(ScannerContext context)
        {
            for (int recentCount = 6; recentCount > 0 && context.RecentIndex < context.RecentEnd; context.RecentIndex++)
            {
                string filePath = Path.GetFullPath(context.RecentFiles[context.RecentIndex]);
                JumpItem jumpItem = context.CreateClaimedFileItem(this, filePath);
                if (jumpItem != null)
                {
                    yield return jumpItem;
                    recentCount--;
                }
            }
        }
    }
}
