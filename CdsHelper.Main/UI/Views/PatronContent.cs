using System.Windows;
using System.Windows.Controls;
using CdsHelper.Main.Local.ViewModels;
using CdsHelper.Support.Local.Helpers;
using Prism.Ioc;

namespace CdsHelper.Main.UI.Views;

public class PatronContent : ContentControl
{
    static PatronContent()
    {
        DefaultStyleKeyProperty.OverrideMetadata(
            typeof(PatronContent),
            new FrameworkPropertyMetadata(typeof(PatronContent)));
    }

    public PatronContent()
    {
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is PatronContentViewModel)
            return;

        var patronService = ContainerLocator.Container.Resolve<PatronService>();
        var saveDataService = ContainerLocator.Container.Resolve<SaveDataService>();
        DataContext = new PatronContentViewModel(patronService, saveDataService);
    }
}
