using System.Globalization;
using System.Windows;
using CdsHelper.Support.Local.Converters;

namespace CdsHelper.Main.Local.Converters;

public sealed class NullToVisibilityConverter : ConverterMarkupExtension<NullToVisibilityConverter>
{
    public override object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is null ? Visibility.Collapsed : Visibility.Visible;
}
