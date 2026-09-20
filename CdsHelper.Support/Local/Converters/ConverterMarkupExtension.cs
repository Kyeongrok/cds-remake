using System.Globalization;
using System.Windows.Data;
using System.Windows.Markup;

namespace CdsHelper.Support.Local.Converters;

/// IValueConverter를 MarkupExtension으로 감싸는 공용 베이스.
/// 상속 클래스는 Lazy 싱글톤으로 자동 공유되어 리소스 선언 없이 XAML에서 바로 사용 가능.
public abstract class ConverterMarkupExtension<T> : MarkupExtension, IValueConverter
    where T : class, new()
{
    private static readonly Lazy<T> _converter = new(() => new T());

    public override object ProvideValue(IServiceProvider serviceProvider) => _converter.Value;

    public abstract object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture);

    public virtual object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
