using System.Globalization;
using CdsHelper.Support.Local.Converters;

namespace CdsHelper.Main.Local.Converters;

public sealed class BoolToOXConverter : ConverterMarkupExtension<BoolToOXConverter>
{
    public override object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is bool b && b ? "O" : "X";

    public override object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is string s && s == "O";
}
