using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace CdsHelper.Main.UI.Views;

/// <summary>
/// 남길 구간을 끌어서 정하는 띠 — 양 끝 손잡이를 끌면 시작·끝이 바뀐다.
/// </summary>
/// <remarks>
/// 동영상 줄이기 창의 재생 막대 바로 밑에 놓인다. 값은 초 단위이고(0 ~ <see cref="Maximum"/>),
/// 손잡이를 끄는 동안 <see cref="Dragging"/> 으로 그 자리를 알려 미리 보기가 따라가게 한다.
/// </remarks>
public sealed class RangeBar : Canvas
{
    private const double HandleW = 10, BarH = 18, TrackH = 6;

    private readonly Rectangle _track = new() { Height = TrackH, Fill = new SolidColorBrush(Color.FromRgb(0xDD, 0xDD, 0xDD)) };
    private readonly Rectangle _band = new() { Height = TrackH, Fill = new SolidColorBrush(Color.FromRgb(0x2E, 0x8B, 0x57)) };
    private readonly Thumb _fromHandle = Handle("시작점 — 끌어서 옮긴다");
    private readonly Thumb _toHandle = Handle("끝점 — 끌어서 옮긴다");

    private double _maximum = 1, _from, _to = 1;

    /// <summary>손잡이를 놓아 구간이 바뀌었다 — (시작, 끝) 초.</summary>
    public event Action<double, double>? RangeChanged;

    /// <summary>손잡이를 끄는 동안 그 자리(초) — 미리 보기를 옮기라고 알린다.</summary>
    public event Action<double>? Dragging;

    public RangeBar()
    {
        Height = BarH;
        ClipToBounds = false;
        Children.Add(_track);
        Children.Add(_band);
        Children.Add(_fromHandle);
        Children.Add(_toHandle);

        _fromHandle.DragDelta += (_, e) => Move(fromSide: true, e.HorizontalChange);
        _toHandle.DragDelta += (_, e) => Move(fromSide: false, e.HorizontalChange);
        _fromHandle.DragCompleted += (_, _) => RangeChanged?.Invoke(_from, _to);
        _toHandle.DragCompleted += (_, _) => RangeChanged?.Invoke(_from, _to);
        SizeChanged += (_, _) => Layout();
    }

    /// <summary>전체 길이(초).</summary>
    public double Maximum
    {
        get => _maximum;
        set { _maximum = Math.Max(0.01, value); Layout(); }
    }

    /// <summary>구간을 밖에서 정한다(단추·새 동영상). null 이면 처음·끝이다.</summary>
    public void SetRange(double? from, double? to)
    {
        _from = Math.Clamp(from ?? 0, 0, _maximum);
        _to = Math.Clamp(to ?? _maximum, 0, _maximum);
        if (_to < _from) (_from, _to) = (_to, _from);
        Layout();
    }

    private static Thumb Handle(string tip)
    {
        var thumb = new Thumb { Width = HandleW, Height = BarH, Cursor = Cursors.SizeWE, ToolTip = tip };
        // 손잡이 모양 — 진한 초록 막대.
        var face = new FrameworkElementFactory(typeof(Border));
        face.SetValue(Border.BackgroundProperty, new SolidColorBrush(Color.FromRgb(0x1F, 0x6B, 0x41)));
        face.SetValue(Border.BorderBrushProperty, Brushes.White);
        face.SetValue(Border.BorderThicknessProperty, new Thickness(1));
        face.SetValue(Border.CornerRadiusProperty, new CornerRadius(2));
        thumb.Template = new ControlTemplate(typeof(Thumb)) { VisualTree = face };
        return thumb;
    }

    /// <summary>손잡이 쪽 길이(픽셀)를 초로 바꿔 옮긴다. 시작은 끝을, 끝은 시작을 못 넘는다.</summary>
    private void Move(bool fromSide, double dx)
    {
        double usable = Math.Max(1, ActualWidth - HandleW);
        double delta = dx / usable * _maximum;
        if (fromSide) _from = Math.Clamp(_from + delta, 0, _to);
        else _to = Math.Clamp(_to + delta, _from, _maximum);
        Layout();
        Dragging?.Invoke(fromSide ? _from : _to);
    }

    private void Layout()
    {
        double usable = Math.Max(1, ActualWidth - HandleW);
        double x1 = _from / _maximum * usable, x2 = _to / _maximum * usable;
        double trackTop = (BarH - TrackH) / 2;

        _track.Width = usable;
        SetLeft(_track, HandleW / 2);
        SetTop(_track, trackTop);

        _band.Width = Math.Max(0, x2 - x1);
        SetLeft(_band, HandleW / 2 + x1);
        SetTop(_band, trackTop);

        SetLeft(_fromHandle, x1);
        SetLeft(_toHandle, x2);
    }
}
