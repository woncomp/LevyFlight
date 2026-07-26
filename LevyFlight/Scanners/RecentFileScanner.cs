using System.Collections.Generic;
using System.Threading.Tasks;

namespace LevyFlight
{
    public sealed class RecentFileScanner : Scanner
    {
        public override string Id => "RecentFile";
        public override string DisplayName => "Recent File";

        internal override Task<IEnumerable<JumpItem>> ScanAsync(ScannerContext context, ScannerDumpSection dump)
        {
            return Task.FromResult(EnumerateItems(context, dump));
        }

        private IEnumerable<JumpItem> EnumerateItems(ScannerContext context, ScannerDumpSection dump)
        {
            dump.Detail("transition range [index " + context.TransitionIndex + ", " + context.TransitionEnd + "), recent range [index " + context.RecentIndex + ", " + context.RecentEnd + ")");
            for (; context.TransitionIndex < context.TransitionEnd; context.TransitionIndex++)
            {
                TransitionRecord transition = context.Transitions[context.TransitionIndex];
                string transitionPath = CommonMixin.ToAbsolutePath(transition.Path);
                dump.Input(transitionPath + "  [transition #" + context.TransitionIndex + "]");
                JumpItem jumpItem = context.CreateClaimedFileItem(this, transitionPath, dump);
                if (jumpItem != null)
                {
                    dump.Produced(transitionPath);
                    yield return jumpItem;
                }
            }

            for (; context.RecentIndex < context.RecentEnd; context.RecentIndex++)
            {
                string recentPath = context.RecentFiles[context.RecentIndex];
                dump.Input(recentPath + "  [recent #" + context.RecentIndex + "]");
                JumpItem jumpItem = context.CreateClaimedFileItem(this, recentPath, dump);
                if (jumpItem != null)
                {
                    dump.Produced(recentPath);
                    yield return jumpItem;
                }
            }
        }
    }
}
