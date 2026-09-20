using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;

namespace CdsHelper.Main.UI.Views;

/// 단일 레이어 DrawingVisual 호스트. 이벤트가 필요 없는 대량 오버레이 요소 전용.
public sealed class CanvasVisualHost : FrameworkElement
{
    private readonly List<Visual> _visuals = new();

    public CanvasVisualHost()
    {
        IsHitTestVisible = false;
    }

    protected override int VisualChildrenCount => _visuals.Count;

    protected override Visual GetVisualChild(int index) => _visuals[index];

    public void AddVisual(Visual visual)
    {
        _visuals.Add(visual);
        AddVisualChild(visual);
    }

    public void Clear()
    {
        foreach (var v in _visuals)
            RemoveVisualChild(v);
        _visuals.Clear();
    }

    public int Count => _visuals.Count;
}
