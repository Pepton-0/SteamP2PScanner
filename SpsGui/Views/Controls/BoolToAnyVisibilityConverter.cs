using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace SpsGui.Views.Controls
{
    /// <summary>
    /// Converts a boolean value to configurable visibility values.
    /// </summary>
    public sealed class BoolToAnyVisibilityConverter : IValueConverter
    {
        public Visibility? TrueTo { get; set; }

        public Visibility? FalseTo { get; set; }

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            Visibility trueTo = TrueTo ?? Visibility.Visible;
            Visibility falseTo = FalseTo ?? Visibility.Collapsed;

            if (!(value is bool enabled))
            {
                return DependencyProperty.UnsetValue;
            }

            return enabled ? trueTo : falseTo;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }
}
