using System.Collections.Generic;
using System.Threading.Tasks;

namespace LevyFlight
{
    public sealed class TransitionScanner : Scanner
    {
        public override string Id => "Transition";
        public override string DisplayName => "Transition";

        internal override Task<IEnumerable<JumpItem>> ScanAsync(ScannerContext context, ScannerDumpSection dump)
        {
            return Task.FromResult(EnumerateItems(context, dump));
        }

        private IEnumerable<JumpItem> EnumerateItems(ScannerContext context, ScannerDumpSection dump)
        {
            dump.Detail("quota: 11 files, transition range [index " + context.TransitionIndex + ", " + context.TransitionEnd + ")");
            for (int transitionCount = 11; transitionCount > 0 && context.TransitionIndex < context.TransitionEnd; context.TransitionIndex++)
            {
                TransitionRecord transition = context.Transitions[context.TransitionIndex];
                string filePath = CommonMixin.ToAbsolutePath(transition.Path);
                dump.Input(filePath + "  [transition #" + context.TransitionIndex + "]");
                JumpItem jumpItem = context.CreateClaimedFileItem(this, filePath, dump);
                if (jumpItem != null)
                {
                    dump.Produced(filePath);
                    yield return jumpItem;
                    transitionCount--;
                }
            }
        }
    }
}
