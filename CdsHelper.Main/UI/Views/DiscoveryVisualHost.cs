using System.Windows;
using System.Windows.Media;

namespace CdsHelper.Main.UI.Views;

/// 발견물 점/영역 마커를 DrawingVisual로 호스팅. found/unfound 레이어를 분리하여
/// HideFound 토글을 O(1)로 처리한다.
public sealed class DiscoveryVisualHost : FrameworkElement
{
    private readonly ContainerVisual _root = new();
    private readonly ContainerVisual _unfoundLayer = new();
    private readonly ContainerVisual _foundLayer = new();

    public DiscoveryVisualHost()
    {
        _root.Children.Add(_unfoundLayer);
        _root.Children.Add(_foundLayer);
        AddVisualChild(_root);
        AddLogicalChild(_root);
        IsHitTestVisible = false;
    }

    protected override int VisualChildrenCount => 1;

    protected override Visual GetVisualChild(int index) => _root;

    public void AddVisual(DrawingVisual visual, bool isFound)
    {
        (isFound ? _foundLayer : _unfoundLayer).Children.Add(visual);
    }

    public void Clear()
    {
        _unfoundLayer.Children.Clear();
        _foundLayer.Children.Clear();
    }

    public bool FoundVisible
    {
        get => _foundLayer.Opacity > 0.5;
        set => _foundLayer.Opacity = value ? 1.0 : 0.0;
    }
}
