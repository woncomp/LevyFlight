using System.Collections.Generic;
using System.Threading.Tasks;

namespace LevyFlight
{
    public sealed class OpenFileScanner : Scanner
    {
        public override string Id => "OpenFile";
        public override string DisplayName => "Open File";

        internal override Task<IEnumerable<JumpItem>> ScanAsync(ScannerContext context)
        {
            return Task.FromResult(EnumerateItems(context));
        }

        private IEnumerable<JumpItem> EnumerateItems(ScannerContext context)
        {
            foreach (string filePath in context.ActiveFiles)
            {
                JumpItem jumpItem = context.CreateClaimedFileItem(this, filePath);
                if (jumpItem != null)
                {
                    yield return jumpItem;
                }
            }
        }
    }
}
