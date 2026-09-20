using System.Windows;
using System.Windows.Controls;
using CdsHelper.Main.Local.ViewModels;

namespace CdsHelper.Main.UI.Views;

/// <summary>발견물 그림 뷰어 — DSTILL.CDS 의 그림을 골라 본다.</summary>
public class DiscoveryStillContent : Control
{
    static DiscoveryStillContent()
    {
        DefaultStyleKeyProperty.OverrideMetadata(
            typeof(DiscoveryStillContent),
            new FrameworkPropertyMetadata(typeof(DiscoveryStillContent)));
    }

    public DiscoveryStillContent(DiscoveryStillContentViewModel viewModel)
    {
        DataContext = viewModel;
    }
}
