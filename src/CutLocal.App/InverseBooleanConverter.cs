using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace CutLocal.App;

/// <summary>Inverts boolean values for XAML bindings.</summary>
public sealed class InverseBooleanConverter : IValueConverter
{
    /// <inheritdoc />
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        bool inverted = value is bool flag && !flag;
        return targetType == typeof(Visibility)
            ? inverted ? Visibility.Visible : Visibility.Collapsed
            : inverted;
    }

    /// <inheritdoc />
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        bool visible = value is Visibility visibility
            ? visibility == Visibility.Visible
            : value is bool flag && flag;
        return !visible;
    }
}
