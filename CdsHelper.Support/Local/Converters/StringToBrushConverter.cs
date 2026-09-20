using System.Globalization;
using System.Windows.Media;

namespace CdsHelper.Support.Local.Converters;

public sealed class StringToBrushConverter : ConverterMarkupExtension<StringToBrushConverter>
{
    public override object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is string colorString)
        {
            try
            {
                var color = (Color)ColorConverter.ConvertFromString(colorString);
                return new SolidColorBrush(color);
            }
            catch
            {
                return Brushes.Gray;
            }
        }
        return Brushes.Gray;
    }
}
