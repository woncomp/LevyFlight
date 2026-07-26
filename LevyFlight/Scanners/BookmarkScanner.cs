using Microsoft.VisualStudio.Imaging;
using Microsoft.VisualStudio.Imaging.Interop;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace LevyFlight
{
    public sealed class BookmarkScanner : Scanner
    {
        public override string Id => "Bookmark";
        public override string DisplayName => "Bookmark";

        internal override ImageMoniker GetIconMoniker(string filePath)
        {
            return KnownMonikers.Bookmark;
        }

        internal override Task<IEnumerable<JumpItem>> ScanAsync(ScannerContext context)
        {
            return Task.FromResult<IEnumerable<JumpItem>>(LevyFlightWindowCommand.Instance.Bookmarks);
        }
    }
}
