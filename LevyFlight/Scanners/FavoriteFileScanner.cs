using Microsoft.VisualStudio.Imaging;
using Microsoft.VisualStudio.Imaging.Interop;

namespace LevyFlight
{
    public sealed class FavoriteFileScanner : Scanner
    {
        public override string Id => "FavoriteFile";
        public override string DisplayName => "Favorite File";

        internal override ImageMoniker GetIconMoniker(string filePath)
        {
            return KnownMonikers.Favorite;
        }
    }
}
