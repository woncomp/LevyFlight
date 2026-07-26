using System.Collections.Generic;
using System.Threading.Tasks;

namespace LevyFlight
{
    public sealed class RecentFileScanner : Scanner
    {
        public override string Id => "RecentFile";
        public override string DisplayName => "Recent File";

        internal override Task<IEnumerable<JumpItem>> ScanAsync(ScannerContext context)
        {
            return Task.FromResult(EnumerateItems(context));
        }

        private IEnumerable<JumpItem> EnumerateItems(ScannerContext context)
        {
            for (; context.TransitionIndex < context.TransitionEnd; context.TransitionIndex++)
            {
                TransitionRecord transition = context.Transitions[context.TransitionIndex];
                JumpItem jumpItem = context.CreateClaimedFileItem(this, CommonMixin.ToAbsolutePath(transition.Path));
                if (jumpItem != null)
                {
                    yield return jumpItem;
                }
            }

            for (; context.RecentIndex < context.RecentEnd; context.RecentIndex++)
            {
                JumpItem jumpItem = context.CreateClaimedFileItem(this, context.RecentFiles[context.RecentIndex]);
                if (jumpItem != null)
                {
                    yield return jumpItem;
                }
            }
        }
    }
}
