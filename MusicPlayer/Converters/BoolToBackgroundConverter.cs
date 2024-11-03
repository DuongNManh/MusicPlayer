using System;
using System.Windows.Data;
using System.Windows.Media;

namespace MusicPlayer.Converters
{
    public class BoolToBackgroundConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            bool isEnabled = (bool)value;
            return isEnabled ?
                new SolidColorBrush(System.Windows.Media.Color.FromRgb(30, 215, 96)) :
                new SolidColorBrush(Colors.Transparent);
        }

        public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}