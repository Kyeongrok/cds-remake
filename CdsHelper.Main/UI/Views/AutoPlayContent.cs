using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using CdsHelper.Main.Local.ViewModels;

namespace CdsHelper.Main.UI.Views;

public class AutoPlayContent : ContentControl
{
    /// <summary>true↔0, false↔1 변환 (북위/동경=true=0, 남위/서경=false=1)</summary>
    public static readonly IValueConverter BoolToIndexConverter = new BoolToIndexValueConverter();

    static AutoPlayContent()
    {
        DefaultStyleKeyProperty.OverrideMetadata(
            typeof(AutoPlayContent),
            new FrameworkPropertyMetadata(typeof(AutoPlayContent)));
    }

    public AutoPlayContent()
    {
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is AutoPlayContentViewModel)
            return;

        DataContext = new AutoPlayContentViewModel();
    }

    private class BoolToIndexValueConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            => value is true ? 0 : 1;

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => value is 0;
    }
}
