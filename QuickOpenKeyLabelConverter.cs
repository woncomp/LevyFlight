using System;
using System.Globalization;
using System.Linq;
using System.Windows.Data;
using System.Windows.Input;

namespace LevyFlight
{
    public class QuickOpenKeyLabelConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is int altIndex && altIndex >= 1) // start from second item
            {
                int idx = altIndex - 1; // map 1 -> 0
                if (idx < LevyFlightWindow.QuickOpenKeys.Length)
                {
                    var key = LevyFlightWindow.QuickOpenKeys[idx];
                    if (key >= Key.D0 && key <= Key.D9)
                    {
                        return ((char)('0' + (key - Key.D0))).ToString();
                    }
                    return key.ToString();
                }
            }
            return " ";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
    }
}
