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

            var fillBrush = new SolidColorBrush(Color.FromRgb(80, 200, 120));   // Emerald green fill
            fillBrush.Freeze();
            var strokeBrush = new SolidColorBrush(Color.FromRgb(20, 120, 60));  // Darker emerald stroke
            strokeBrush.Freeze();
            var pen = new Pen(strokeBrush, 1.0);
            pen.Freeze();

            var drawing = new GeometryDrawing(fillBrush, pen, geometry);
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
