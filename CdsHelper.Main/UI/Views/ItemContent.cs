using System.Windows;
using System.Windows.Controls;
using CdsHelper.Main.Local.ViewModels;
using CdsHelper.Support.Local.Helpers;
using Prism.Ioc;

namespace CdsHelper.Main.UI.Views;

public class ItemContent : ContentControl
{
    static ItemContent()
    {
        DefaultStyleKeyProperty.OverrideMetadata(
            typeof(ItemContent),
            new FrameworkPropertyMetadata(typeof(ItemContent)));
    }

    public ItemContent()
    {
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is ItemContentViewModel)
            return;

        var itemService = ContainerLocator.Container.Resolve<ItemService>();
        DataContext = new ItemContentViewModel(itemService);
    }
}
