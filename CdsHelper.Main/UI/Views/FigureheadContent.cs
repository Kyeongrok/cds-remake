using System.Windows;
using System.Windows.Controls;
using CdsHelper.Main.Local.ViewModels;
using CdsHelper.Support.Local.Helpers;
using Prism.Ioc;

namespace CdsHelper.Main.UI.Views;

public class FigureheadContent : ContentControl
{
    static FigureheadContent()
    {
        DefaultStyleKeyProperty.OverrideMetadata(
            typeof(FigureheadContent),
            new FrameworkPropertyMetadata(typeof(FigureheadContent)));
    }

    public FigureheadContent()
    {
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is FigureheadContentViewModel)
            return;

        var figureheadService = ContainerLocator.Container.Resolve<FigureheadService>();
        DataContext = new FigureheadContentViewModel(figureheadService);
    }
}
