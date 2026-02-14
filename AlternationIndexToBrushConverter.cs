using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace LevyFlight
{
    public class AlternationIndexToBrushConverter : IValueConverter
    {
        // Subtle semi-transparent overlay that works on both light and dark backgrounds.
        // On light themes the rows tint slightly darker; on dark themes slightly lighter.
        private static readonly Brush AlternateRowBrush = CreateAlternateRowBrush();

        private static Brush CreateAlternateRowBrush()
        {
            var brush = new SolidColorBrush(Color.FromArgb(0x0C, 0x80, 0x80, 0x80));
            brush.Freeze();
            return brush;
        }

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is int idx)
            {
                // Apply subtle tint to odd alternation indices (i.e., every other visual row)
                return (idx % 2 == 1) ? AlternateRowBrush : Brushes.Transparent;
            }
            return Brushes.Transparent;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
    }
}
