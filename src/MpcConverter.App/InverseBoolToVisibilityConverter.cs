using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace MpcConverter.App;

/// <summary>true → Collapsed, false → Visible. Used to show the drop hint until a project loads.</summary>
public sealed class InverseBoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is true ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => value is Visibility.Visible;
}
