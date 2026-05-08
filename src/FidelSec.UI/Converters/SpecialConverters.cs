using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace FidelSec.UI.Converters
{
    /// <summary>
    /// Returns Red brush if bad sector count > 0, otherwise green.
    /// </summary>
    public class BadSectorColorConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is long count && count > 0)
                return new SolidColorBrush(Color.FromRgb(0xEF, 0x44, 0x44)); // Red
            return new SolidColorBrush(Color.FromRgb(0x22, 0xC5, 0x5E)); // Green
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }

    /// <summary>
    /// Inverts a boolean value for binding.
    /// </summary>
    public class InverseBoolConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            => value is bool b ? !b : value;

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => value is bool b ? !b : value;
    }
}
