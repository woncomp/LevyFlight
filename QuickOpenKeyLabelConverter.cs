using System;
using System.Globalization;
using System.Linq;
using System.Windows.Data;
using System.Windows.Input;

namespace LevyFlight
{
    /// <summary>
    /// Converts a <see cref="JumpItem.QuickOpenIndex"/> value to a hotkey label string.
    /// Index  0 = first item (no hotkey, user presses Ctrl+J).
    /// Index 1–15 = maps to 1,2,…,9,Q,W,E,R,T,Y.
    /// Index -1 or out-of-range = blank.
    /// </summary>
    public class QuickOpenKeyLabelConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is int qIdx && qIdx >= 1) // index 0 = first item, no hotkey
            {
                int keyIdx = qIdx - 1; // map 1 -> QuickOpenKeys[0]
                if (keyIdx < LevyFlightWindow.QuickOpenKeys.Length)
                {
                    var key = LevyFlightWindow.QuickOpenKeys[keyIdx];
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
