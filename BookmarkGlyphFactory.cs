using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.Text.Formatting;
using Microsoft.VisualStudio.Text.Tagging;
using Microsoft.VisualStudio.Utilities;
using System.ComponentModel.Composition;
using System.Windows;
using System.Windows.Media;

namespace LevyFlight
{
    /// <summary>
    /// Generates a bookmark-shaped flag glyph displayed in the editor indicator margin
    /// for bookmarked lines. Styled as a small ribbon/flag to distinguish from the native bookmark.
    /// </summary>
    internal class BookmarkGlyphFactory : IGlyphFactory
    {
        private const double GlyphWidth = 12.0;
        private const double GlyphHeight = 14.0;

        public UIElement GenerateGlyph(IWpfTextViewLine line, IGlyphTag tag)
        {
            if (tag == null || !(tag is BookmarkTag))
                return null;

            // Draw a bookmark ribbon/flag shape:
            //   ┌───────┐
            //   │       │
            //   │       │
            //   │       │
            //   └──╲ ╱──┘
            //      ╲╱
            var geometry = new StreamGeometry();
            using (var ctx = geometry.Open())
            {
                ctx.BeginFigure(new Point(0, 0), isFilled: true, isClosed: true);
                ctx.LineTo(new Point(GlyphWidth, 0), isStroked: true, isSmoothJoin: false);
                ctx.LineTo(new Point(GlyphWidth, GlyphHeight), isStroked: true, isSmoothJoin: false);
                ctx.LineTo(new Point(GlyphWidth / 2, GlyphHeight * 0.7), isStroked: true, isSmoothJoin: false);
                ctx.LineTo(new Point(0, GlyphHeight), isStroked: true, isSmoothJoin: false);
            }
            geometry.Freeze();

            var drawing = new GeometryDrawing(
                new SolidColorBrush(Color.FromRgb(100, 180, 255)),  // Light steel blue fill
                new Pen(new SolidColorBrush(Color.FromRgb(40, 100, 180)), 1.0), // Darker blue stroke
                geometry);
            drawing.Freeze();

            var image = new System.Windows.Media.DrawingImage(drawing);
            image.Freeze();

            var element = new System.Windows.Controls.Image
            {
                Source = image,
                Width = GlyphWidth,
                Height = GlyphHeight,
            };

            return element;
        }
    }

    /// <summary>
    /// MEF-exported provider that creates BookmarkGlyphFactory instances.
    /// Ordered before VsTextMarker so breakpoints render on top of bookmark glyphs.
    /// </summary>
    [Export(typeof(IGlyphFactoryProvider))]
    [Name("LevyFlightBookmarkGlyph")]
    [Order(Before = "VsTextMarker")]
    [ContentType("text")]
    [TagType(typeof(BookmarkTag))]
    internal class BookmarkGlyphFactoryProvider : IGlyphFactoryProvider
    {
        public IGlyphFactory GetGlyphFactory(IWpfTextView view, IWpfTextViewMargin margin)
        {
            return new BookmarkGlyphFactory();
        }
    }
}
