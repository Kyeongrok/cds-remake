using System.Windows;
using System.Windows.Controls;
using CdsHelper.Main.Local.ViewModels;
using CdsHelper.Support.Local.Helpers;
using Prism.Events;
using Prism.Ioc;

namespace CdsHelper.Main.UI.Views;

public class CharacterContent : ContentControl
{
    static CharacterContent()
    {
        DefaultStyleKeyProperty.OverrideMetadata(
            typeof(CharacterContent),
            new FrameworkPropertyMetadata(typeof(CharacterContent)));
    }

    public CharacterContent()
    {
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is CharacterContentViewModel)
            return;

        var characterService = ContainerLocator.Container.Resolve<CharacterService>();
        var saveDataService = ContainerLocator.Container.Resolve<SaveDataService>();
        var eventAggregator = ContainerLocator.Container.Resolve<IEventAggregator>();
        DataContext = new CharacterContentViewModel(characterService, saveDataService, eventAggregator);
    }
}
