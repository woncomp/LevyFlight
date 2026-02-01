using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace LevyFlight
{
    public class AlternationIndexToBrushConverter : IValueConverter
    {
        private static readonly Brush LightGray = new SolidColorBrush(Color.FromRgb(0xF0, 0xF0, 0xF0));
        private static readonly Brush Transparent = Brushes.Transparent;
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is int idx)
            {
                // Apply light gray to odd alternation indices (i.e., every other visual row)
                return (idx % 2 == 1) ? LightGray : Transparent;
            }
            return Transparent;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
    }
}
