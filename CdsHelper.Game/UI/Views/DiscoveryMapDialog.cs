using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CdsHelper.Game.Local.Helpers;
using CdsHelper.Game.Local.Settings;
using CdsHelper.Support.Local.Models;

namespace CdsHelper.Game.UI.Views;

/// <summary>
/// 발견물 지도 — 양피지 세계지도 위에 <b>발견물 자리</b>와 <b>내 자리</b>를 찍어 보인다.
/// 휠로 키우고 줄이며, 끌어서 옮긴다.
/// </summary>
/// <remarks>
/// <b>게임에는 없는 창이다.</b> 원본 항해지도(<c>0x00416A00</c>)는 밝힌 바다만 드러내고
/// 표식을 하나도 안 찍는다(볼트 <c>91.분석-지도를 본다</c>). 이쪽은 <c>cds95-mod</c> 의
/// <c>WorldMapKR</c> 처럼 <b>어디에 무엇이 있는지</b> 보려고 둔 것이라 햄버거 차림표에서 연다.
///
/// 모드의 손놀림을 그대로 옮겼다(<c>mapwin.c</c>).
/// <code>
///   휠      커서 밑에 있던 자리가 <b>제자리에 남도록</b> 배율만 바꾼다(ZoomAt)
///   끌기    누른 자리를 붙잡고 지도를 민다(g_drag)
///   오른쪽  짚은 자리로 함대를 옮긴다 — 가까운 물칸에 닻을 내린 채 선다
///   열 때   함대 자리를 가운데 두고 중간 배율로 연다(ZOOM_START)
///   이름표  배율이 어느 구간일 때만 단다 — 너무 키우면 이름이 그림을 덮는다
/// </code>
/// 바탕은 항해지도를 짓는 손(<see cref="Rendering.ShipMapHost.Chart"/>)을 <b>온 지도를 밝힌
/// 채</b> 부른 것이라 점 하나가 칸 <c>4x4</c> 다 — 모드처럼 타일을 다시 그리지는 않으므로
/// 아주 키우면 네모가 커질 뿐이다.
///
/// <b>풍향 · 해류</b>는 바람표(<see cref="WindTable"/>)의 50x25 칸마다 화살표 하나씩이다. 한 칸이 지도
/// 점 12.5 라 칸 윗쪽에 풍향, 아랫쪽에 해류를 둔다. 화살표는 <b>불어가는 쪽</b>을 가리키고 세기만큼
/// 길다. 풍향은 지금 달의 표(1~6월 · 7~12월)를 쓴다. 둘 다 아래 단추(글쇠 W · C)로 켜고 끄며,
/// 켠 것은 설정에 남는다.
/// </remarks>
public sealed class DiscoveryMapDialog : GameWindow
{
    /// <summary>보이는 자리의 크기(점).</summary>
    private const double ViewW = 940, ViewH = 470;

    /// <summary>휠로 오갈 배율 — 지도 점 하나가 화면 몇 점인지. 첫 칸이 <b>온 지도</b>다.</summary>
    private static readonly double[] Zooms = [1.5, 2, 3, 4, 6, 8, 12, 16];

    /// <summary>열 때의 배율 자리.</summary>
    private const int ZoomStart = 2;

    /// <summary>이름표를 다는 배율 구간 — 너무 키우면 이름이 그림을 덮는다.</summary>
    private const double LabelFrom = 4, LabelTo = 12;

    /// <summary>
    /// 표식 크기(지도 점) — <see cref="ZoomBase"/> 배율에서 잰 값이다.
    /// </summary>
    /// <remarks>
    /// 표식은 <b>화면에서 늘 같은 크기</b>로 보인다. 지도를 키우면 점도 같이 커져서 커질수록
    /// 그림을 덮었다 — 이제 배율만큼 <b>지도 점 크기를 줄여</b> 화면 크기를 붙박아 둔다.
    /// </remarks>
    private const double MarkSize = 3, ShipSize = 5;

    /// <summary>표식 크기를 잰 배율. 이 배율에서 <see cref="MarkSize"/> 그대로가 된다.</summary>
    private static double ZoomBase => Zooms[ZoomStart];

    /// <summary>이름표 글자 크기(<see cref="ZoomBase"/> 배율의 지도 점).</summary>
    private const double LabelSize = 4;

    private static readonly Brush Found = Frozen(Color.FromRgb(0xC0, 0x30, 0x20));
    private static readonly Brush Yet = Frozen(Color.FromRgb(0x50, 0x50, 0x50));
    private static readonly Brush Mine = Frozen(Color.FromRgb(0x20, 0x40, 0xC0));

    /// <summary>풍향 · 해류 화살표 색. 표식(빨강·회색·파랑)과 안 겹치게 보라와 청록이다.</summary>
    private static readonly Brush WindInk = Frozen(Color.FromRgb(0x70, 0x40, 0xC0));
    private static readonly Brush CurrentInk = Frozen(Color.FromRgb(0x10, 0x8A, 0x70));

    /// <summary>화살표 선 굵기와 머리 크기(지도 점). 배율을 따라 함께 커진다.</summary>
    private const double ArrowLine = 0.5, ArrowHead = 1.6;

    private static SolidColorBrush Frozen(Color c)
    {
        var b = new SolidColorBrush(c);
        b.Freeze();
        return b;
    }

    /// <summary>
    /// 지도에 찍은 표식 하나 — 점과 이름표, 그리고 찍힌 지도 점.
    /// </summary>
    /// <remarks>배율이 바뀔 때마다 <see cref="Place"/> 가 크기와 자리를 다시 잡는다.</remarks>
    private sealed class Pin(FrameworkElement dot, TextBlock? tag, double x, double y, double size)
    {
        public FrameworkElement Dot { get; } = dot;
        public TextBlock? Tag { get; } = tag;
        public double X { get; set; } = x;
        public double Y { get; set; } = y;
        public double Size { get; } = size;
    }

    private readonly Canvas _world;
    private readonly List<Pin> _pins = [];
    private readonly List<FrameworkElement> _labels = [];
    private readonly ScaleTransform _scale = new(1, 1);
    private readonly TranslateTransform _shift = new(0, 0);
    private readonly TextBlock _note;

    /// <summary>풍향 · 해류 화살표가 앉는 켜. 표식보다 아래다.</summary>
    private readonly Canvas _windLayer = new() { IsHitTestVisible = false };
    private readonly Canvas _currentLayer = new() { IsHitTestVisible = false };

    /// <summary>바람표를 읽었는지 — 못 읽었으면 단추도 설명도 안 낸다.</summary>
    private readonly bool _hasFlows;

    /// <summary>풍향에 쓴 달.</summary>
    private readonly int _month;

    /// <summary>풍향 · 해류 켜고 끄기(단추와 글쇠가 같이 쓴다).</summary>
    private Action? _toggleWind, _toggleCurrent;

    /// <summary>
    /// 오른쪽 단추로 짚은 자리로 함대를 옮기는 손. 칸 자리를 받아 <b>실제로 선 칸</b>을
    /// 돌려준다(뭍이면 가까운 물칸으로 밀리므로 짚은 자리와 다를 수 있다). 옮길 수 없는
    /// 형편(도시 안·뭍 위)이면 null 이다. 안 주면 옮기기가 아예 없다.
    /// </summary>
    private readonly Func<double, double, (double X, double Y)?>? _warp;

    /// <summary>
    /// Shift + 오른쪽 단추로 짚은 자리로 자동항해를 거는 손. 칸 자리를 받아 알림 말을
    /// 돌려준다. 안 주면 자동항해 걸기가 아예 없다.
    /// </summary>
    private readonly Func<double, double, string>? _autoSail;

    /// <summary>내 자리 점. 옮기면 이 점을 따라 옮긴다.</summary>
    private System.Windows.Shapes.Ellipse? _shipDot;

    /// <summary>아래 줄에 잠깐 붙는 말(옮겼다 · 못 옮긴다).</summary>
    private string _said = "";

    private readonly int _chartW, _chartH, _found, _done;
    private int _zoom = ZoomStart;
    private double _vx, _vy;              // 보이는 자리의 왼쪽 위(지도 점)
    private bool _dragging;
    private Point _grab;
    private double _grabVx, _grabVy;

    private double Z => Zooms[_zoom];

    private DiscoveryMapDialog(uint[] chart, int width, int height,
                               DiscoveryTable table, Player player, (double X, double Y)? ship, WindTable? wind,
                               Func<double, double, (double X, double Y)?>? warp,
                               Func<double, double, string>? autoSail)
    {
        _warp = warp;
        _autoSail = autoSail;
        _chartW = width;
        _chartH = height;
        _month = player.Date.Month;
        _hasFlows = wind != null;

        Title = "발견물 지도";
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.WidthAndHeight;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        Background = GameUi.Back;

        var bmp = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null,
                                      chart, width * 4);
        bmp.Freeze();

        _world = new Canvas { Width = width, Height = height };
        _world.Children.Add(new Image
        {
            Source = bmp,
            Width = width,
            Height = height,
            SnapsToDevicePixels = true,
        });

        // 화살표 켜는 표식보다 먼저 얹는다 — 점과 이름표가 화살표에 안 가린다.
        _world.Children.Add(_windLayer);
        _world.Children.Add(_currentLayer);
        if (wind != null) DrawFlows(wind, width, height);
        _windLayer.Visibility = GameSettings.DiscoveryMapWind ? Visibility.Visible : Visibility.Collapsed;
        _currentLayer.Visibility = GameSettings.DiscoveryMapCurrent ? Visibility.Visible : Visibility.Collapsed;

        // 옮기고 키우는 것은 한 덩이로 — 먼저 밀고 나서 키운다.
        var moves = new TransformGroup();
        moves.Children.Add(_shift);
        moves.Children.Add(_scale);
        _world.RenderTransform = moves;

        int shown = 0, done = 0;
        // 0~Count-1 로만 돌면 DiscoveryEdits 로 더한 줄(274 이상)이 안 뜬다 — table.Discoveries 를 돈다.
        foreach (var row in table.Discoveries)
        {
            if (!row.HasPlace) continue;

            bool found = player.HasFound(row.Id);
            // 자리는 네모라 한가운데를 찍는다.
            Mark((row.X1 + row.X2) / 2.0 / ExploredMap.CellsPerBlock,
                 (row.Y1 + row.Y2) / 2.0 / ExploredMap.CellsPerBlock,
                 MarkSize, found ? Found : Yet, row.Name, label: true);
            shown++;
            if (found) done++;
        }

        _found = shown;
        _done = done;

        if (ship is { } at)
            _shipDot = Mark(at.X / ExploredMap.CellsPerBlock, at.Y / ExploredMap.CellsPerBlock,
                            ShipSize, Mine, "지금 자리", label: false);

        var viewport = new Border
        {
            Width = ViewW,
            Height = ViewH,
            BorderBrush = GameUi.Edge,
            BorderThickness = new Thickness(1),
            Background = Brushes.Black,
            ClipToBounds = true,
            Child = new Canvas { Children = { _world } },
            Cursor = Cursors.SizeAll,
        };

        viewport.MouseWheel += (_, e) => ZoomAt(e.Delta > 0 ? 1 : -1, e.GetPosition(viewport));
        viewport.MouseLeftButtonDown += (_, e) =>
        {
            _dragging = true;
            _grab = e.GetPosition(viewport);
            _grabVx = _vx;
            _grabVy = _vy;
            viewport.CaptureMouse();
        };
        viewport.MouseMove += (_, e) =>
        {
            if (!_dragging) return;
            var now = e.GetPosition(viewport);
            _vx = _grabVx - (now.X - _grab.X) / Z;
            _vy = _grabVy - (now.Y - _grab.Y) / Z;
            Apply();
        };
        viewport.MouseLeftButtonUp += (_, _) =>
        {
            _dragging = false;
            viewport.ReleaseMouseCapture();
        };
        // 오른쪽 단추 — 짚은 자리로 함대를 옮긴다. Shift 를 누른 채면 그 자리로 자동항해를 건다.
        viewport.MouseRightButtonUp += (_, e) =>
        {
            e.Handled = true;
            var at = e.GetPosition(viewport);
            double px = _vx + at.X / Z, py = _vy + at.Y / Z;
            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)) AutoSailTo(px, py);
            else Warp(px, py);
        };

        _note = new TextBlock
        {
            Foreground = GameUi.Text,
            FontSize = 14,
            Margin = new Thickness(6, 4, 6, 6),
            HorizontalAlignment = HorizontalAlignment.Center,
        };

        var ok = GameUi.PushButton("확인", Close, 88);
        ok.HorizontalAlignment = HorizontalAlignment.Center;
        ok.Margin = new Thickness(0, 0, 0, 8);

        var stack = new StackPanel { Margin = new Thickness(8) };
        stack.Children.Add(viewport);
        if (_hasFlows)
        {
            var toggles = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 6, 0, 0),
            };
            toggles.Children.Add(Toggle("풍향", () => GameSettings.DiscoveryMapWind,
                                        v => GameSettings.DiscoveryMapWind = v, _windLayer, out _toggleWind));
            toggles.Children.Add(Toggle("해류", () => GameSettings.DiscoveryMapCurrent,
                                        v => GameSettings.DiscoveryMapCurrent = v, _currentLayer, out _toggleCurrent));
            stack.Children.Add(toggles);
        }
        stack.Children.Add(_note);
        stack.Children.Add(ok);
        Content = stack;

        KeyDown += OnKey;

        // 함대 자리를 가운데 두고 연다 — 모드도 그렇게 연다.
        if (ship is { } spot)
            CenterOn(spot.X / ExploredMap.CellsPerBlock, spot.Y / ExploredMap.CellsPerBlock);
        else
            CenterOn(width / 2.0, height / 2.0);
    }

    /// <summary>글쇠 — 화살표로 밀고 <c>+ -</c> 로 키우고 줄인다.</summary>
    private void OnKey(object sender, KeyEventArgs e)
    {
        const double Step = 40;
        switch (e.Key)
        {
            case Key.Escape: Close(); return;
            case Key.Left: _vx -= Step / Z; break;
            case Key.Right: _vx += Step / Z; break;
            case Key.Up: _vy -= Step / Z; break;
            case Key.Down: _vy += Step / Z; break;
            case Key.OemPlus or Key.Add: ZoomAt(1, new Point(ViewW / 2, ViewH / 2)); return;
            case Key.OemMinus or Key.Subtract: ZoomAt(-1, new Point(ViewW / 2, ViewH / 2)); return;
            case Key.W: _toggleWind?.Invoke(); e.Handled = true; return;
            case Key.C: _toggleCurrent?.Invoke(); e.Handled = true; return;
            default: return;
        }
        e.Handled = true;
        Apply();
    }

    /// <summary>그 자리가 화면 한가운데 오게 민다(지도 점).</summary>
    private void CenterOn(double x, double y)
    {
        _vx = x - ViewW / Z / 2;
        _vy = y - ViewH / Z / 2;
        Apply();
    }

    /// <summary>
    /// 커서 밑에 있던 자리가 <b>제자리에 남도록</b> 배율만 바꾼다(모드 <c>ZoomAt</c>).
    /// </summary>
    private void ZoomAt(int by, Point at)
    {
        int want = Math.Clamp(_zoom + by, 0, Zooms.Length - 1);
        if (want == _zoom) return;

        double x = _vx + at.X / Z;          // 커서가 짚고 있던 지도 점
        double y = _vy + at.Y / Z;
        _zoom = want;
        _vx = x - at.X / Z;
        _vy = y - at.Y / Z;
        Apply();
    }

    /// <summary>보이는 자리를 지도 밖으로 못 나가게 자르고, 화면에 먹인다.</summary>
    private void Apply()
    {
        double seeW = ViewW / Z, seeH = ViewH / Z;
        _vx = seeW >= _chartW ? (_chartW - seeW) / 2 : Math.Clamp(_vx, 0, _chartW - seeW);
        _vy = seeH >= _chartH ? (_chartH - seeH) / 2 : Math.Clamp(_vy, 0, _chartH - seeH);

        _scale.ScaleX = _scale.ScaleY = Z;
        _shift.X = -_vx;
        _shift.Y = -_vy;

        // 표식은 화면에서 같은 크기로 보이게 지도 점 크기를 배율만큼 줄인다.
        foreach (var pin in _pins) Place(pin);

        // 이름표는 어느 구간에서만 단다 — 아주 키우면 이름이 그림을 덮는다.
        var show = Z >= LabelFrom && Z <= LabelTo ? Visibility.Visible : Visibility.Collapsed;
        foreach (var tag in _labels) tag.Visibility = show;

        _note.Text = $"발견물 {_found}곳 · 찾은 것 {_done}곳 · 배율 x{Z:0.#}"
                   + "   (휠 키우기·줄이기 · 끌어서 옮기기"
                   + (_warp != null ? " · 오른쪽 단추 그 자리로 옮기기" : "")
                   + (_autoSail != null ? " · Shift+오른쪽 단추 그 자리로 자동항해" : "")
                   + " · 빨강 찾음 · 회색 아직 · 파랑 내 자리"
                   + (_hasFlows
                       ? $" · 보라 풍향 {(WindTable.IsFirstHalf(_month) ? "1~6월" : "7~12월")} · 청록 해류)"
                       : ")")
                   + _said;
    }

    /// <summary>
    /// 켜고 끄는 단추 하나 — 누르면 설정을 뒤집고 그 켜를 보이거나 감춘다.
    /// </summary>
    /// <param name="flip">글쇠가 같은 일을 하도록 내주는 손.</param>
    private static Border Toggle(string name, Func<bool> get, Action<bool> set, Canvas layer, out Action flip)
    {
        Border? button = null;
        void Refresh()
        {
            bool on = get();
            layer.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
            if (button?.Child is TextBlock label) label.Text = $"{name} {(on ? "끄기" : "켜기")}";
        }

        flip = () => { set(!get()); Refresh(); };
        button = GameUi.PushButton(name, flip, 110);
        Refresh();
        return button;
    }

    /// <summary>
    /// 바람표 칸마다 풍향과 해류 화살표를 긋는다 — 켜 하나에 <b>도형 하나</b>로 묶어 가볍게 둔다.
    /// </summary>
    private void DrawFlows(WindTable table, int width, int height)
    {
        double cellW = width / (double)WindTable.Cols, cellH = height / (double)WindTable.Rows;
        var wind = new StreamGeometry();
        var current = new StreamGeometry();

        using (var w = wind.Open())
        using (var c = current.Open())
        {
            for (int cell = 0; cell < WindTable.Count; cell++)
            {
                double cx = (cell % WindTable.Cols + 0.5) * cellW;
                double cy = (cell / WindTable.Cols + 0.5) * cellH;

                // 한 칸 윗쪽에 풍향, 아랫쪽에 해류 — 가운데 겹치면 둘 다 안 읽힌다.
                var blow = table.WindAt(cell, _month);
                if (!blow.IsStill) Arrow(w, table, blow, cx, cy - cellH / 4, cellW, maxSpeed: 6);
                var flow = table.CurrentAt(cell);
                if (!flow.IsStill) Arrow(c, table, flow, cx, cy + cellH / 4, cellW, maxSpeed: 7);
            }
        }

        wind.Freeze();
        current.Freeze();
        _windLayer.Children.Add(new System.Windows.Shapes.Path
        {
            Data = wind, Stroke = WindInk, Fill = WindInk, StrokeThickness = ArrowLine,
        });
        _currentLayer.Children.Add(new System.Windows.Shapes.Path
        {
            Data = current, Stroke = CurrentInk, Fill = CurrentInk, StrokeThickness = ArrowLine,
        });
    }

    /// <summary>
    /// 화살표 하나 — <paramref name="cx"/>·<paramref name="cy"/> 를 가운데로, 불어가는 쪽에 머리를 둔다.
    /// 길이는 세기에 따라 칸 폭의 1/5 ~ 1/2 이다.
    /// </summary>
    private static void Arrow(StreamGeometryContext g, WindTable table, WindTable.Flow flow,
                              double cx, double cy, double cellW, int maxSpeed)
    {
        var (dx, dy) = table.Vector(flow.Dir);
        double ux = dx / (double)WindTable.VectorLength, uy = dy / (double)WindTable.VectorLength;
        double len = cellW * (0.2 + 0.3 * Math.Min(flow.Speed, maxSpeed) / maxSpeed);

        var tail = new Point(cx - ux * len / 2, cy - uy * len / 2);
        var head = new Point(cx + ux * len / 2, cy + uy * len / 2);
        g.BeginFigure(tail, isFilled: false, isClosed: false);
        g.LineTo(head, isStroked: true, isSmoothJoin: false);

        // 머리 — 뒤로 물러난 자리에서 좌우로 벌린 세모.
        var back = new Point(head.X - ux * ArrowHead, head.Y - uy * ArrowHead);
        double px = -uy * ArrowHead / 2, py = ux * ArrowHead / 2;
        g.BeginFigure(head, isFilled: true, isClosed: true);
        g.LineTo(new Point(back.X + px, back.Y + py), isStroked: false, isSmoothJoin: false);
        g.LineTo(new Point(back.X - px, back.Y - py), isStroked: false, isSmoothJoin: false);
    }

    /// <summary>점 하나와 이름표를 찍는다. 자리는 <b>지도 점</b>(칸/4)이다.</summary>
    private System.Windows.Shapes.Ellipse Mark(double x, double y, double size, Brush fill,
                                              string name, bool label)
    {
        var dot = new System.Windows.Shapes.Ellipse
        {
            Width = size,
            Height = size,
            Fill = fill,
            ToolTip = name,
        };
        _world.Children.Add(dot);

        TextBlock? tag = null;
        if (label)
        {
            tag = new TextBlock
            {
                Text = name,
                Foreground = fill,
                FontWeight = FontWeights.Bold,
                Visibility = Visibility.Collapsed,
                IsHitTestVisible = false,
            };
            _world.Children.Add(tag);
            _labels.Add(tag);
        }

        var pin = new Pin(dot, tag, x, y, size);
        _pins.Add(pin);
        if (!label) _shipPin = pin;
        Place(pin);
        return dot;
    }

    /// <summary>내 자리 점. 함대를 옮기면 이 표식만 자리를 고쳐 잡는다.</summary>
    private Pin? _shipPin;

    /// <summary>
    /// 표식 하나를 지금 배율에 맞춰 놓는다 — 화면에서 늘 같은 크기로 보이게 줄인다.
    /// </summary>
    private void Place(Pin pin)
    {
        double size = pin.Size * ZoomBase / Z;
        pin.Dot.Width = pin.Dot.Height = size;
        Canvas.SetLeft(pin.Dot, pin.X - size / 2);
        Canvas.SetTop(pin.Dot, pin.Y - size / 2);

        if (pin.Tag is not { } tag) return;
        tag.FontSize = LabelSize * ZoomBase / Z;
        Canvas.SetLeft(tag, pin.X + size / 2 + 1 / Z);
        Canvas.SetTop(tag, pin.Y - tag.FontSize * 0.7);
    }

    /// <summary>
    /// 짚은 자리(지도 점)로 함대를 옮긴다. 옮겨진 자리에 내 자리 점을 다시 찍는다.
    /// </summary>
    /// <remarks>
    /// 게임에 없는 길이라 묻지 않고 바로 옮긴다 — 지도를 띄워 놓고 여러 곳을 짚어 볼 수
    /// 있어야 쓸모가 있다. 뭍을 짚으면 부르는 쪽이 가까운 물칸으로 밀어 준다.
    /// </remarks>
    private void Warp(double px, double py)
    {
        if (_warp == null) return;

        var landed = _warp(px * ExploredMap.CellsPerBlock, py * ExploredMap.CellsPerBlock);
        if (landed is not { } spot)
        {
            _said = "   ·   지금은 함대를 옮길 수 없습니다(도시 안이거나 뭍 위)";
            Apply();
            return;
        }

        double x = spot.X / ExploredMap.CellsPerBlock, y = spot.Y / ExploredMap.CellsPerBlock;
        _shipDot ??= Mark(x, y, ShipSize, Mine, "지금 자리", label: false);
        if (_shipPin is { } pin) { pin.X = x; pin.Y = y; Place(pin); }

        _said = "   ·   그 자리로 옮겼습니다(닻을 내린 채)";
        Apply();
    }

    /// <summary>짚은 자리(지도 점)로 자동항해를 건다.</summary>
    private void AutoSailTo(double px, double py)
    {
        if (_autoSail == null) return;
        _said = "   ·   " + _autoSail(px * ExploredMap.CellsPerBlock, py * ExploredMap.CellsPerBlock);
        Apply();
    }

    /// <summary>창을 연다. 지도를 못 지으면 아무 일도 안 한다.</summary>
    /// <param name="wind">바람표. 없으면 풍향 · 해류 단추가 안 나온다.</param>
    /// <param name="warp">
    /// 오른쪽 단추로 짚은 자리로 함대를 옮기는 손. 안 주면 옮기기가 없다.
    /// </param>
    /// <param name="autoSail">
    /// Shift + 오른쪽 단추로 짚은 자리로 자동항해를 거는 손. 안 주면 자동항해 걸기가 없다.
    /// </param>
    public static void Show(Window owner, uint[]? chart, int width, int height,
                            DiscoveryTable? table, Player player, (double X, double Y)? ship,
                            WindTable? wind = null,
                            Func<double, double, (double X, double Y)?>? warp = null,
                            Func<double, double, string>? autoSail = null)
    {
        if (chart == null || table == null || width <= 0 || height <= 0) return;
        new DiscoveryMapDialog(chart, width, height, table, player, ship, wind, warp, autoSail)
        { Owner = owner }.ShowDialog();
    }
}
