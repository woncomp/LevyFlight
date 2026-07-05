using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace LevyFlight
{
    /// <summary>
    /// A WPF adorner that paints a small Windows-style keytip badge below the adorned element.
    /// The badge does not participate in layout, so showing/hiding it does not change the size
    /// of the decorated button.
    /// </summary>
    internal sealed class KeyTipAdorner : Adorner
    {
        private const double KeyTipFontSize = 11;
        private const double KeyTipVerticalGap = 2;

        private readonly Border keyTipBorder;
        private readonly FrameworkElement adornedElement;

        public KeyTipAdorner(FrameworkElement adornedElement, string keyTip)
            : base(adornedElement)
        {
            this.adornedElement = adornedElement;

            // Windows Notepad/Paint style: dark background with light text and a thin border.
            keyTipBorder = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(230, 30, 30, 30)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(255, 220, 220, 220)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(2),
                Padding = new Thickness(5, 1, 5, 1),
                SnapsToDevicePixels = true,
                Child = new TextBlock
                {
                    Text = keyTip,
                    Foreground = Brushes.White,
                    FontSize = KeyTipFontSize,
                    FontWeight = FontWeights.Bold,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                },
            };

            // Do not intercept mouse input so the user can still click the button.
            IsHitTestVisible = false;
            SnapsToDevicePixels = true;

            AddVisualChild(keyTipBorder);
            AddLogicalChild(keyTipBorder);
        }

        public FrameworkElement Target => adornedElement;

        protected override int VisualChildrenCount => 1;

        protected override Visual GetVisualChild(int index) => keyTipBorder;

        protected override Size MeasureOverride(Size constraint)
        {
            keyTipBorder.Measure(constraint);
            return keyTipBorder.DesiredSize;
        }

        protected override Size ArrangeOverride(Size finalSize)
        {
            // Center horizontally below the adorned button, leaving a small gap.
            double x = (adornedElement.ActualWidth - finalSize.Width) / 2.0;
            double y = adornedElement.ActualHeight + KeyTipVerticalGap;
            keyTipBorder.Arrange(new Rect(x, y, finalSize.Width, finalSize.Height));
            return finalSize;
        }
    }
}
