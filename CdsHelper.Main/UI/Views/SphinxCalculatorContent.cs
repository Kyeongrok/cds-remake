using System.Windows;
using System.Windows.Controls;
using CdsHelper.Main.Local.ViewModels;

namespace CdsHelper.Main.UI.Views;

public class SphinxCalculatorContent : Control
{
    static SphinxCalculatorContent()
    {
        DefaultStyleKeyProperty.OverrideMetadata(
            typeof(SphinxCalculatorContent),
            new FrameworkPropertyMetadata(typeof(SphinxCalculatorContent)));
    }

    public SphinxCalculatorContent(SphinxCalculatorContentViewModel viewModel)
    {
        DataContext = viewModel;
    }
}
