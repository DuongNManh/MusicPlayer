using System;
using System.Windows;
using System.Windows.Data;

namespace MusicPlayer.Converters
{
    public class VisibilityToWidthConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            if (value is Visibility visibility)
            {
                return visibility == Visibility.Visible ? new GridLength(0) : new GridLength(250);
            }
            return new GridLength(250);
        }

        public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}