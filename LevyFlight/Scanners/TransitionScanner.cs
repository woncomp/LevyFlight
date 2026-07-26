using System.Collections.Generic;
using System.Threading.Tasks;

namespace LevyFlight
{
    public sealed class TransitionScanner : Scanner
    {
        public override string Id => "Transition";
        public override string DisplayName => "Transition";

        internal override Task<IEnumerable<JumpItem>> ScanAsync(ScannerContext context)
        {
            return Task.FromResult(EnumerateItems(context));
        }

        private IEnumerable<JumpItem> EnumerateItems(ScannerContext context)
        {
            for (int transitionCount = 11; transitionCount > 0 && context.TransitionIndex < context.TransitionEnd; context.TransitionIndex++)
            {
                TransitionRecord transition = context.Transitions[context.TransitionIndex];
                JumpItem jumpItem = context.CreateClaimedFileItem(this, CommonMixin.ToAbsolutePath(transition.Path));
                if (jumpItem != null)
                {
                    yield return jumpItem;
                    transitionCount--;
                }
            }
        }
    }
}
