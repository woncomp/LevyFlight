using Microsoft.VisualStudio.Imaging;
using Microsoft.VisualStudio.Imaging.Interop;
using Microsoft.VisualStudio.Shell;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace LevyFlight
{
    public sealed class RecentEditScanner : Scanner
    {
        public override string Id => "RecentEdit";
        public override string DisplayName => "Recent Edit";

        internal override ImageMoniker GetIconMoniker(string filePath)
        {
            return KnownMonikers.Edit;
        }

        internal override async Task<IEnumerable<JumpItem>> ScanAsync(ScannerContext context, ScannerDumpSection dump)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            return EnumerateItems(context, dump);
        }

        private IEnumerable<JumpItem> EnumerateItems(ScannerContext context, ScannerDumpSection dump)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            int editRank = 0;
            foreach (RecentEditCollector.EditRegion region in RecentEditCollector.Collect())
            {
                dump.Input(region.FilePath + " line " + (region.JumpLine + 1));
                JumpItem jumpItem = CreateJumpItem(region.FilePath);
                jumpItem.SetPosition(region.JumpLine, 0);
                jumpItem.ExtraScore = (uint)Math.Max(1, 99 - editRank);
                context.ClaimFile(region.FilePath, Id);
                dump.Produced(region.FilePath, "line " + (region.JumpLine + 1) + ", extra score " + jumpItem.ExtraScore);
                yield return jumpItem;
                editRank++;
            }
        }
    }
}
