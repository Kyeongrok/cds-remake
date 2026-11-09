using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using CdsHelper.Game.Local.Helpers;
using CdsHelper.Game.Local.Settings;
using CdsHelper.Support.Local.Models;

namespace CdsHelper.Game.UI.Views;

/// <summary>
/// 지나온 뱃길 — 이번 게임 실행 동안 내 배가 지난 자리(칸). 세계지도 창이 선으로 긋는다.
/// </summary>
/// <remarks>
/// 창을 닫아도 남도록 지도 창의 상태 시계(<see cref="WorldMapWindow"/>)가 적는다. 크게 뛴 자리(배 놓기 · 로드 · 날짜변경선)에서는
/// 선을 끊어 새 토막을 연다 — 이어 그으면 지도를 가로지르는 줄이 생긴다.
/// </remarks>
internal static class ShipTrail
{
    /// <summary>한 칸 넘게 옮겨야 새 점을 적는다.</summary>
    private const double MinStep = 1;

    /// <summary>이보다 멀리 뛰면 선을 끊는다(칸). 배는 한 번에 한두 칸밖에 못 간다.</summary>
    private const double JumpCells = 30;

    /// <summary>토막이 너무 길어지면 앞을 걷어 낸다 — 몇 시간을 돌려도 가볍게.</summary>
    private const int MaxPoints = 200_000;

    private static readonly List<List<Point>> _segments = [];
    private static int _count;

    /// <summary>바뀔 때마다 오른다 — 창이 선을 다시 그을 때를 안다.</summary>
    public static int Version { get; private set; }

    public static IReadOnlyList<List<Point>> Segments => _segments;

    public static void Note(double x, double y)
    {
        var at = new Point(x, y);
        if (_segments.Count > 0 && _segments[^1] is { Count: > 0 } last)
        {
            var prev = last[^1];
            double dx = at.X - prev.X, dy = at.Y - prev.Y;
            double d = Math.Sqrt(dx * dx + dy * dy);
            if (d < MinStep) return;
            if (d > JumpCells) _segments.Add([at]);
            else last.Add(at);
        }
        else _segments.Add([at]);

        if (++_count > MaxPoints && _segments.Count > 0)
        {
            _count -= _segments[0].Count;
            _segments.RemoveAt(0);
        }
        Version++;
    }

    public static void Clear()
    {
        _segments.Clear();
        _count = 0;
        Version++;
    }
}

/// <summary>
/// 세계지도 — 게임 옆에 띄워 두는 <b>모달이 아닌</b> 보조 지도. 내 배를 실시간으로 따라가고, 지나온 뱃길과 마우스를 올린 칸을 보인다.
/// </summary>
/// <remarks>
/// <b>원본에는 없는 창이다.</b> 도구 앱의 세계지도(「좌표 추적」)는 원본 CDS_95 만 따라가 리메이크와 맞지 않아(버그 리포트 #30)
/// 그 기능만 게임 안 도시락으로 옮겼다. 바탕은 둘 가운데 고른다.
/// <code>
///   게임 지도  화면 지도와 같은 타일 그림(<see cref="Rendering.WorldMapHost.GameTiles"/>) — 키우면 타일을 더 곱게 다시 짓는다
///   양피지     발견물 지도와 같은 그림(<see cref="Rendering.WorldMapHost.Chart"/>, 다 밝힌 것)
/// </code>
/// 자리는 모두 <b>칸</b>(가로 2500 · 세로 1250)이다. 떠 있는 동안 게임은 안 멈춘다 — <see cref="Tick"/> 이 0.5초마다 배 자리를 읽는다.
/// 발견물은 양피지에서는 발견물 지도 설정을, 게임 지도에서는 이 창의 「발견물」 단추(기본 끔)를 따른다.
/// </remarks>
public sealed class LiveMapWindow : Window
{
    /// <summary>창이 지도에서 읽어 오는 것들 — 지도 창이 채운다.</summary>
    internal sealed record Source(
        Func<(double X, double Y)?> Ship,
        Func<int> MapVersion,
        Func<int, int, int, int, int, uint[]?> Tiles,
        Func<(uint[] Pixels, int Width, int Height)?> Parchment,
        Func<int, int, bool?> IsLand,
        Func<IReadOnlyList<(string Name, double X, double Y, bool Library)>> Cities,
        Func<IReadOnlyList<(string Name, double X, double Y, bool Found)>> Spots);

    /// <summary>휠로 오갈 배율 — 칸 하나가 화면 몇 점인지.</summary>
    private static readonly double[] Zooms = [0.25, 0.35, 0.5, 0.75, 1, 1.5, 2, 3, 4, 6, 8, 12, 16];

    private const int ZoomStart = 6;

    /// <summary>이 배율부터 화면에 보이는 자리만 타일을 곱게 다시 짓는다(게임 지도).</summary>
    private const double DetailFrom = 1.5;

    /// <summary>이 배율부터 이름표를 단다.</summary>
    private const double LabelFrom = 2;

    private const int WorldW = 2500, WorldH = 1250;

    private static readonly Brush Found = Frozen(Color.FromRgb(0xE0, 0x30, 0x20));
    private static readonly Brush Yet = Frozen(Color.FromRgb(0x60, 0x60, 0x60));
    private static readonly Brush Mine = Frozen(Color.FromRgb(0x20, 0x60, 0xFF));
    private static readonly Brush CityInk = Frozen(Color.FromRgb(0xF0, 0x90, 0x10));
    private static readonly Brush TrailInk = Frozen(Color.FromArgb(0xE0, 0xFF, 0xE0, 0x40));
    private static readonly Brush Shade = Frozen(Color.FromArgb(0xA0, 0, 0, 0));

    private static SolidColorBrush Frozen(Color c)
    {
        var b = new SolidColorBrush(c);
        b.Freeze();
        return b;
    }

    /// <summary>떠 있는 창 — 하나만 띄운다.</summary>
    private static LiveMapWindow? _open;

    private readonly Source _src;
    private readonly Canvas _world = new() { Width = WorldW, Height = WorldH };
    private readonly Image _base = new() { Width = WorldW, Height = WorldH };
    private readonly Image _detail = new() { Visibility = Visibility.Collapsed };
    private readonly System.Windows.Shapes.Path _trail = new() { Stroke = TrailInk, IsHitTestVisible = false };
    private readonly Canvas _cityLayer = new();
    private readonly Canvas _spotLayer = new();
    private readonly Canvas _yetLayer = new();
    private readonly System.Windows.Shapes.Ellipse _ship = new()
    {
        Fill = Mine, Stroke = Brushes.White, IsHitTestVisible = false, Visibility = Visibility.Collapsed,
    };
    private readonly ScaleTransform _scale = new(1, 1);
    private readonly TranslateTransform _shift = new(0, 0);
    private readonly Border _viewport;
    private readonly TextBlock _hover, _status;
    private readonly DispatcherTimer _timer;

    /// <summary>표식 — 화면에서 늘 같은 크기로 보이게 배율마다 다시 놓는다.</summary>
    private readonly List<(System.Windows.Shapes.Ellipse Dot, TextBlock? Tag, double X, double Y, double Size)> _pins = [];

    private Border _baseButton = null!, _spotsButton = null!, _yetButton = null!, _cityButton = null!, _followButton = null!;

    private int _zoom = ZoomStart;
    private double _vx, _vy;               // 보이는 자리의 왼쪽 위(칸)
    private bool _dragging;
    private Point _grab;
    private double _grabVx, _grabVy;
    private (double X, double Y)? _shipAt;
    private int _trailVersion = -1, _mapVersion = -1;
    private string _marksKey = "";
    private bool _parchmentShown;
    private (int X0, int Y0, int Cols, int Rows, int Scale)? _detailFor;
    private bool _detailQueued;

    private double Z => Zooms[_zoom];
    private double ViewW => Math.Max(1, _viewport.ActualWidth - 2);
    private double ViewH => Math.Max(1, _viewport.ActualHeight - 2);

    private LiveMapWindow(Source src)
    {
        _src = src;
        Title = "세계지도";
        Width = 900;
        Height = 620;
        MinWidth = 420;
        MinHeight = 300;
        ResizeMode = ResizeMode.CanResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        Background = GameUi.Back;

        RenderOptions.SetBitmapScalingMode(_detail, BitmapScalingMode.NearestNeighbor);
        _world.Children.Add(_base);
        _world.Children.Add(_detail);
        _world.Children.Add(_trail);
        _world.Children.Add(_cityLayer);
        _world.Children.Add(_spotLayer);
        _spotLayer.Children.Add(_yetLayer);
        _world.Children.Add(_ship);

        var moves = new TransformGroup();
        moves.Children.Add(_shift);
        moves.Children.Add(_scale);
        _world.RenderTransform = moves;

        _viewport = new Border
        {
            BorderBrush = GameUi.Edge,
            BorderThickness = new Thickness(1),
            Background = Brushes.Black,
            ClipToBounds = true,
            Child = new Canvas { Children = { _world } },
            Cursor = Cursors.SizeAll,
            Focusable = true,
            FocusVisualStyle = null,
        };
        _viewport.SizeChanged += (_, _) => Apply();
        _viewport.MouseWheel += (_, e) => ZoomAt(e.Delta > 0 ? 1 : -1, e.GetPosition(_viewport));
        _viewport.MouseLeftButtonDown += (_, e) =>
        {
            Keyboard.Focus(_viewport);
            _dragging = true;
            _grab = e.GetPosition(_viewport);
            _grabVx = _vx;
            _grabVy = _vy;
            _viewport.CaptureMouse();
        };
        _viewport.MouseMove += (_, e) =>
        {
            var now = e.GetPosition(_viewport);
            if (_dragging)
            {
                // 손으로 옮기면 따라가기를 끈다 — 다음 0.5초에 배 쪽으로 도로 끌려가면 둘러볼 수가 없다.
                if (GameSettings.LiveMapFollow && (Math.Abs(now.X - _grab.X) > 3 || Math.Abs(now.Y - _grab.Y) > 3))
                    SetFollow(false);
                _vx = _grabVx - (now.X - _grab.X) / Z;
                _vy = _grabVy - (now.Y - _grab.Y) / Z;
                Apply();
            }
            ShowHover(_vx + now.X / Z, _vy + now.Y / Z);
        };
        _viewport.MouseLeave += (_, _) => _hover.Text = Lang.T("지도 위에 마우스를 올리면 그 칸을 알려 줍니다");
        _viewport.MouseLeftButtonUp += (_, _) =>
        {
            _dragging = false;
            _viewport.ReleaseMouseCapture();
        };

        // 위 단추 줄 — 바탕 · 표식 켜고 끄기 · 따라가기 · 내 위치로 · 경로 지우기.
        var bar = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 4, 0, 4) };
        _baseButton = Button("", () =>
        {
            GameSettings.LiveMapParchment = !GameSettings.LiveMapParchment;
            _mapVersion = -1;
            _marksKey = "";
            Tick();
        }, 150);
        bar.Children.Add(_baseButton);
        _spotsButton = Button("", () =>
        {
            if (GameSettings.LiveMapParchment) GameSettings.DiscoveryMapSpots = !GameSettings.DiscoveryMapSpots;
            else GameSettings.LiveMapSpots = !GameSettings.LiveMapSpots;
            Refresh();
        }, 120);
        bar.Children.Add(_spotsButton);
        _yetButton = Button("", () => { GameSettings.DiscoveryMapYet = !GameSettings.DiscoveryMapYet; Refresh(); }, 140);
        bar.Children.Add(_yetButton);
        _cityButton = Button("", () => { GameSettings.DiscoveryMapCities = !GameSettings.DiscoveryMapCities; Refresh(); }, 110);
        bar.Children.Add(_cityButton);
        _followButton = Button("", () => SetFollow(!GameSettings.LiveMapFollow), 130);
        bar.Children.Add(_followButton);
        bar.Children.Add(Button("내 위치로", ToShip, 100));
        bar.Children.Add(Button("경로 지우기", () => { ShipTrail.Clear(); Tick(); }, 110));

        _hover = new TextBlock
        {
            Foreground = GameUi.Text, FontSize = 14, Margin = new Thickness(6, 3, 6, 0),
            TextTrimming = TextTrimming.CharacterEllipsis,
            Text = Lang.T("지도 위에 마우스를 올리면 그 칸을 알려 줍니다"),
        };
        _status = new TextBlock
        {
            Foreground = GameUi.Text, FontSize = 12, Margin = new Thickness(6, 1, 6, 4),
            TextTrimming = TextTrimming.CharacterEllipsis,
        };

        var root = new DockPanel { LastChildFill = true, Margin = new Thickness(2) };
        DockPanel.SetDock(bar, Dock.Top);
        root.Children.Add(bar);
        var foot = new StackPanel();
        foot.Children.Add(_hover);
        foot.Children.Add(_status);
        DockPanel.SetDock(foot, Dock.Bottom);
        root.Children.Add(foot);
        root.Children.Add(_viewport);
        Content = root;

        KeyDown += OnKey;

        _timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(500) };
        _timer.Tick += (_, _) => Tick();
        Loaded += (_, _) =>
        {
            Tick();
            if (_shipAt is { } at) CenterOn(at.X, at.Y); else CenterOn(WorldW / 2.0, WorldH / 2.0);
            _timer.Start();
        };
        Closed += (_, _) =>
        {
            _timer.Stop();
            if (ReferenceEquals(_open, this)) _open = null;
        };
        RefreshButtons();
    }

    /// <summary>창을 띄운다. 이미 떠 있으면 앞으로 낸다. <b>기다리지 않고 곧바로 돌아온다.</b></summary>
    internal static void Toggle(Window owner, Source src)
    {
        if (_open is { } shown)
        {
            if (shown.WindowState == WindowState.Minimized) shown.WindowState = WindowState.Normal;
            shown.Activate();
            return;
        }
        var win = new LiveMapWindow(src);
        if (GameWindow.Alive(owner) is { } alive) win.Owner = alive;
        _open = win;
        win.Show();
    }

    /// <summary>떠 있으면 닫는다 — 게임을 새로 불러올 때 따위.</summary>
    internal static void CloseOpen() => _open?.Close();

    private static Border Button(string text, Action run, double width)
    {
        var b = GameUi.PushButton(Lang.T(text), run, width);
        b.Margin = new Thickness(3, 2, 3, 2);
        return b;
    }

    private static void SetText(Border button, string text)
    {
        if (button.Child is TextBlock t) t.Text = text;
    }

    private void RefreshButtons()
    {
        bool parchment = GameSettings.LiveMapParchment;
        SetText(_baseButton, Lang.T("바탕") + ": " + Lang.T(parchment ? "양피지" : "게임 지도"));
        bool spots = parchment ? GameSettings.DiscoveryMapSpots : GameSettings.LiveMapSpots;
        SetText(_spotsButton, Lang.T("발견물") + " " + Lang.T(spots ? "끄기" : "켜기"));
        SetText(_yetButton, Lang.T("못 찾은 것") + " " + Lang.T(GameSettings.DiscoveryMapYet ? "끄기" : "켜기"));
        SetText(_cityButton, Lang.T("도시") + " " + Lang.T(GameSettings.DiscoveryMapCities ? "끄기" : "켜기"));
        SetText(_followButton, Lang.T("따라가기") + " " + Lang.T(GameSettings.LiveMapFollow ? "끄기" : "켜기"));
    }

    private void SetFollow(bool on)
    {
        GameSettings.LiveMapFollow = on;
        RefreshButtons();
        if (on) ToShip();
    }

    private void ToShip()
    {
        if (_shipAt is { } at) CenterOn(at.X, at.Y);
    }

    /// <summary>켜고 끈 것을 켜에 먹이고 단추 글을 고친다.</summary>
    private void Refresh()
    {
        bool spots = GameSettings.LiveMapParchment ? GameSettings.DiscoveryMapSpots : GameSettings.LiveMapSpots;
        _spotLayer.Visibility = spots ? Visibility.Visible : Visibility.Collapsed;
        _yetLayer.Visibility = GameSettings.DiscoveryMapYet ? Visibility.Visible : Visibility.Collapsed;
        _cityLayer.Visibility = GameSettings.DiscoveryMapCities ? Visibility.Visible : Visibility.Collapsed;
        RefreshButtons();
        Apply();
    }

    /// <summary>0.5초마다 — 배 자리 · 뱃길 · 바탕 · 표식을 지도에 맞춘다.</summary>
    private void Tick()
    {
        if (!IsLoaded && !IsVisible && _mapVersion >= 0) return;

        // 바탕 — 고른 것이 바뀌었거나 지도의 덮개(안 선 도시 · 못 찾은 발견물)가 바뀌었을 때만 새로 짓는다.
        bool parchment = GameSettings.LiveMapParchment;
        int version = _src.MapVersion();
        if (version != _mapVersion || parchment != _parchmentShown)
        {
            _mapVersion = version;
            _parchmentShown = parchment;
            BuildBase(parchment);
        }

        _shipAt = _src.Ship();
        if (_shipAt is { } at)
        {
            _ship.Visibility = Visibility.Visible;
            if (GameSettings.LiveMapFollow && !_dragging && IsLoaded) CenterOn(at.X, at.Y, apply: false);
        }
        else _ship.Visibility = Visibility.Collapsed;

        if (ShipTrail.Version != _trailVersion)
        {
            _trailVersion = ShipTrail.Version;
            DrawTrail();
        }

        // 표식 — 찾은 것이 바뀌거나 도시가 서면 다시 찍는다.
        var cities = _src.Cities();
        var spots = _src.Spots();
        string key = $"{cities.Count}|{spots.Count}|{spots.Count(s => s.Found)}|{parchment}";
        if (key != _marksKey)
        {
            _marksKey = key;
            BuildMarks(cities, spots);
        }

        Refresh();
    }

    private void BuildBase(bool parchment)
    {
        _detailFor = null;
        _detail.Visibility = Visibility.Collapsed;
        if (parchment && _src.Parchment() is { } chart)
        {
            var bmp = BitmapSource.Create(chart.Width, chart.Height, 96, 96, PixelFormats.Bgra32, null,
                                          chart.Pixels, chart.Width * 4);
            bmp.Freeze();
            _base.Source = bmp;
            // 양피지 한 점이 칸 4x4 다.
            _base.Width = chart.Width * ExploredMap.CellsPerBlock;
            _base.Height = chart.Height * ExploredMap.CellsPerBlock;
            RenderOptions.SetBitmapScalingMode(_base, BitmapScalingMode.NearestNeighbor);
            return;
        }

        var px = _src.Tiles(0, 0, WorldW, WorldH, 1);
        if (px == null) { _base.Source = null; return; }
        var tiles = BitmapSource.Create(WorldW, WorldH, 96, 96, PixelFormats.Bgra32, null, px, WorldW * 4);
        tiles.Freeze();
        _base.Source = tiles;
        _base.Width = WorldW;
        _base.Height = WorldH;
        RenderOptions.SetBitmapScalingMode(_base, BitmapScalingMode.Linear);
    }

    /// <summary>
    /// 게임 지도 바탕을 키웠을 때 — 보이는 자리(둘레를 조금 더)만 타일을 곱게 다시 짓는다. 온 지도를 칸당 16점으로 지으면 너무 크다.
    /// </summary>
    private void QueueDetail()
    {
        if (_detailQueued) return;
        _detailQueued = true;
        Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => { _detailQueued = false; BuildDetail(); }));
    }

    private void BuildDetail()
    {
        if (GameSettings.LiveMapParchment || Z < DetailFrom)
        {
            _detail.Visibility = Visibility.Collapsed;
            return;
        }

        int scale = 1;
        while (scale < Z && scale < 16) scale *= 2;
        double seeW = ViewW / Z, seeH = ViewH / Z;
        // 지금 그림이 보이는 자리를 다 덮고 곱기가 같으면 그대로 둔다.
        if (_detailFor is { } d && d.Scale == scale
            && _vx >= d.X0 && _vx + seeW <= d.X0 + d.Cols
            && (_vy >= d.Y0 || d.Y0 <= 0) && (_vy + seeH <= d.Y0 + d.Rows || d.Y0 + d.Rows >= WorldH))
        {
            _detail.Visibility = Visibility.Visible;
            return;
        }

        int x0 = (int)Math.Floor(_vx - seeW / 4), y0 = Math.Max(0, (int)Math.Floor(_vy - seeH / 4));
        int cols = Math.Min(WorldW, (int)Math.Ceiling(seeW * 1.5) + 2);
        int rows = Math.Min(WorldH - y0, (int)Math.Ceiling(seeH * 1.5) + 2);
        if (rows <= 0 || cols <= 0) return;
        var px = _src.Tiles(x0, y0, cols, rows, scale);
        if (px == null) return;
        var bmp = BitmapSource.Create(cols * scale, rows * scale, 96, 96, PixelFormats.Bgra32, null, px, cols * scale * 4);
        bmp.Freeze();
        _detail.Source = bmp;
        _detail.Width = cols;
        _detail.Height = rows;
        Canvas.SetLeft(_detail, x0);
        Canvas.SetTop(_detail, y0);
        _detail.Visibility = Visibility.Visible;
        _detailFor = (x0, y0, cols, rows, scale);
    }

    private void DrawTrail()
    {
        var geo = new StreamGeometry();
        using (var g = geo.Open())
            foreach (var seg in ShipTrail.Segments)
            {
                if (seg.Count < 2) continue;
                g.BeginFigure(seg[0], false, false);
                g.PolyLineTo(seg.Skip(1).ToList(), true, true);
            }
        geo.Freeze();
        _trail.Data = geo;
    }

    private void BuildMarks(IReadOnlyList<(string Name, double X, double Y, bool Library)> cities,
                            IReadOnlyList<(string Name, double X, double Y, bool Found)> spots)
    {
        _pins.Clear();
        _cityLayer.Children.Clear();
        _yetLayer.Children.Clear();
        _spotLayer.Children.Clear();
        _spotLayer.Children.Add(_yetLayer);

        foreach (var c in cities)
            Mark(_cityLayer, c.X, c.Y, c.Library ? 9 : 7, CityInk, c.Library ? $"{Lang.T(c.Name)} ({Lang.T("도서관")})" : Lang.T(c.Name), Lang.T(c.Name));
        foreach (var s in spots)
            Mark(s.Found ? _spotLayer : _yetLayer, s.X, s.Y, 7, s.Found ? Found : Yet, Lang.T(s.Name), Lang.T(s.Name));
    }

    private void Mark(Canvas layer, double x, double y, double size, Brush fill, string tip, string name)
    {
        var dot = new System.Windows.Shapes.Ellipse { Fill = fill, Stroke = Brushes.Black, ToolTip = tip };
        layer.Children.Add(dot);
        var tag = new TextBlock
        {
            Text = name, Foreground = fill, FontWeight = FontWeights.Bold, IsHitTestVisible = false,
            Background = Shade, Visibility = Visibility.Collapsed,
        };
        layer.Children.Add(tag);
        _pins.Add((dot, tag, x, y, size));
    }

    private void OnKey(object sender, KeyEventArgs e)
    {
        const double Step = 60;
        switch (e.Key)
        {
            case Key.Escape: Close(); return;
            case Key.Left: _vx -= Step / Z; SetFollow(false); break;
            case Key.Right: _vx += Step / Z; SetFollow(false); break;
            case Key.Up: _vy -= Step / Z; SetFollow(false); break;
            case Key.Down: _vy += Step / Z; SetFollow(false); break;
            case Key.OemPlus or Key.Add: ZoomAt(1, new Point(ViewW / 2, ViewH / 2)); e.Handled = true; return;
            case Key.OemMinus or Key.Subtract: ZoomAt(-1, new Point(ViewW / 2, ViewH / 2)); e.Handled = true; return;
            default: return;
        }
        e.Handled = true;
        Apply();
    }

    private void CenterOn(double x, double y, bool apply = true)
    {
        _vx = x - ViewW / Z / 2;
        _vy = y - ViewH / Z / 2;
        if (apply) Apply();
    }

    /// <summary>커서 밑의 자리가 제자리에 남도록 배율만 바꾼다. 따라가기 중이면 배를 가운데 둔 채 키운다.</summary>
    private void ZoomAt(int by, Point at)
    {
        int want = Math.Clamp(_zoom + by, 0, Zooms.Length - 1);
        if (want == _zoom) return;
        if (GameSettings.LiveMapFollow && _shipAt is { } ship)
        {
            _zoom = want;
            CenterOn(ship.X, ship.Y);
            return;
        }
        double x = _vx + at.X / Z, y = _vy + at.Y / Z;
        _zoom = want;
        _vx = x - at.X / Z;
        _vy = y - at.Y / Z;
        Apply();
    }

    /// <summary>보이는 자리를 지도 안으로 자르고 화면에 먹인다. 표식은 화면에서 늘 같은 크기다.</summary>
    private void Apply()
    {
        if (!IsLoaded) return;
        double seeW = ViewW / Z, seeH = ViewH / Z;
        _vx = seeW >= WorldW ? (WorldW - seeW) / 2 : Math.Clamp(_vx, 0, WorldW - seeW);
        _vy = seeH >= WorldH ? (WorldH - seeH) / 2 : Math.Clamp(_vy, 0, WorldH - seeH);
        _scale.ScaleX = _scale.ScaleY = Z;
        _shift.X = -_vx;
        _shift.Y = -_vy;

        bool labels = Z >= LabelFrom;
        foreach (var (dot, tag, x, y, size) in _pins)
        {
            double s = size / Z;
            dot.Width = dot.Height = s;
            dot.StrokeThickness = 1 / Z;
            Canvas.SetLeft(dot, x - s / 2);
            Canvas.SetTop(dot, y - s / 2);
            if (tag == null) continue;
            tag.Visibility = labels ? Visibility.Visible : Visibility.Collapsed;
            if (!labels) continue;
            tag.FontSize = 12 / Z;
            Canvas.SetLeft(tag, x + s / 2 + 2 / Z);
            Canvas.SetTop(tag, y - 8 / Z);
        }

        if (_shipAt is { } at)
        {
            double s = 12 / Z;
            _ship.Width = _ship.Height = s;
            _ship.StrokeThickness = 2 / Z;
            Canvas.SetLeft(_ship, at.X - s / 2);
            Canvas.SetTop(_ship, at.Y - s / 2);
        }
        _trail.StrokeThickness = 2.5 / Z;

        if (GameSettings.LiveMapParchment || Z < DetailFrom) _detail.Visibility = Visibility.Collapsed;
        else QueueDetail();

        string where = _shipAt is { } me ? $"   ·   {Lang.T("내 위치")} {LatLon(me.X, me.Y)}" : "";
        _status.Text = $"{Lang.T("배율")} x{Z:0.##}{where}   ·   " + Lang.T("휠 키우기·줄이기 · 끌어서 옮기기 · 노랑 지나온 뱃길 · 파랑 내 위치");
    }

    /// <summary>마우스를 올린 칸 — 좌표 · 바다/육지 · 가까운 도시.</summary>
    private void ShowHover(double x, double y)
    {
        if (x < 0 || y < 0 || x >= WorldW || y >= WorldH) { _hover.Text = ""; return; }
        int cx = (int)x, cy = (int)y;
        string kind = _src.IsLand(cx, cy) switch { true => Lang.T("육지"), false => Lang.T("바다"), _ => "" };
        string near = "";
        double best = double.MaxValue;
        foreach (var c in _src.Cities())
        {
            double dx = Math.Abs(c.X - x);
            dx = Math.Min(dx, WorldW - dx);   // 가로는 이어져 있다
            double d = Math.Sqrt(dx * dx + (c.Y - y) * (c.Y - y));
            if (d < best) { best = d; near = c.Name; }
        }
        string city = near.Length > 0 ? "   ·   " + Lang.T($"가까운 도시 {near} ({best:0}칸)") : "";
        _hover.Text = $"{LatLon(x, y)}   ·   ({cx}, {cy})   ·   {kind}{city}";
    }

    /// <summary>칸 자리를 위도 · 경도 글로 — 가로 2500칸이 경도 -180~180, 세로 1250칸이 위도 90~-90 이다.</summary>
    private static string LatLon(double x, double y)
    {
        double lat = 90.0 - y * 180.0 / WorldH, lon = x * 360.0 / WorldW - 180.0;
        string ns = lat >= 0 ? "N" : "S", ew = lon >= 0 ? "E" : "W";
        return $"{Math.Abs(lat):0.0}°{ns} {Math.Abs(lon):0.0}°{ew}";
    }
}
