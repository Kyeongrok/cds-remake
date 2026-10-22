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
/// <b>원본에는 없는 창이다.</b> 원본 항해지도(<c>0x00416A00</c>)는 밝힌 바다만 드러내고
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
/// 바탕은 항해지도를 짓는 손(<see cref="Rendering.WorldMapHost.Chart"/>)을 <b>온 지도를 밝힌
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

    /// <summary>
    /// 이름표를 다는 가장 작은 배율 — 온 지도에 가까우면 이름끼리 겹친다. 이름표도 화면에서 늘 같은 크기라
    /// 키우는 쪽으로는 끝을 두지 않는다(예전에는 x12 까지만 달아 x16 에서 이름이 사라졌다).
    /// </summary>
    private const double LabelFrom = 3;

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

    /// <summary>도시 표식 색 — 발견물(빨강 · 회색) · 내 자리(파랑) · 화살표(보라 · 청록)와 안 겹치는 주황.</summary>
    private static readonly Brush CityInk = Frozen(Color.FromRgb(0xD0, 0x78, 0x00));

    /// <summary>도시 표식 크기(지도 점, <see cref="ZoomBase"/> 배율).</summary>
    private const double CitySize = 3;

    /// <summary>도서관 있는 도시 표식 크기 — 동그라미 안에 「도」가 읽히게 조금 크다.</summary>
    private const double LibrarySize = 5;

    /// <summary>
    /// 내 자리 점이 키울수록 줄어드는 하한 — 화면 크기의 몇 배까지 줄지. 아주 키우면 점 하나가 바다를 덮었다.
    /// </summary>
    private const double ShipShrinkFloor = 0.45;

    /// <summary>풍향 · 해류 화살표 색. 표식(빨강·회색·파랑)과 안 겹치게 보라와 청록이다.</summary>
    internal static readonly Brush WindInk = Frozen(Color.FromRgb(0x70, 0x40, 0xC0));
    internal static readonly Brush CurrentInk = Frozen(Color.FromRgb(0x10, 0x8A, 0x70));

    /// <summary>화살표 선 굵기와 머리 크기(지도 점). 배율을 따라 함께 커진다.</summary>
    internal const double ArrowLine = 0.5, ArrowHead = 1.6;

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

        /// <summary>
        /// 이름표 왼쪽 위가 점 한가운데서 떨어진 거리 — <b>화면 점</b>이라 배율로 나눠 쓴다(표식처럼 화면에서 늘 같은 크기다).
        /// 겹치는 이름표를 비킬 때 <see cref="Declutter"/> 가 정한다. 없으면 점 오른쪽이다.
        /// </summary>
        public Point? Offset { get; set; }

        /// <summary>둘레가 붐벼 이 배율에서는 이름표를 안 단다 — 키우면 다시 센다.</summary>
        public bool Crowded { get; set; }
    }

    /// <summary>이름표 비키기를 마지막으로 한 배율과 켜 모양 — 바뀔 때만 다시 센다(끌어 옮길 때는 그대로다).</summary>
    private string _declutteredFor = "";

    private readonly Canvas _world;
    private readonly List<Pin> _pins = [];
    private readonly ScaleTransform _scale = new(1, 1);
    private readonly TranslateTransform _shift = new(0, 0);
    private readonly TextBlock _note;

    /// <summary>풍향 · 해류 화살표가 앉는 켜. 표식보다 아래다.</summary>
    private readonly Canvas _windLayer = new() { IsHitTestVisible = false };
    private readonly Canvas _currentLayer = new() { IsHitTestVisible = false };

    /// <summary>도시 표식 켜 — 「도시」 단추로 켜고 끈다. 발견물 표식보다 아래다.</summary>
    private readonly Canvas _cityLayer = new();

    /// <summary>발견물 표식 켜 — 「발견물」 단추로 켜고 끈다. 도시 켜 위, 내 자리 점 아래다.</summary>
    private readonly Canvas _spotLayer = new();

    /// <summary>아직 못 찾은 발견물(회색) 켜 — 「못 찾은 것」 단추로 켜고 끈다. 발견물 켜 <b>안</b>에 있어 「발견물」을 끄면 같이 꺼진다.</summary>
    private readonly Canvas _yetLayer = new();

    /// <summary>위도·경도 격자 켜 — 25도마다 어두운 흰 선. 도수 글씨는 <see cref="_gridTags"/> 에 따로 띄운다.</summary>
    private readonly Canvas _gridLayer = new() { IsHitTestVisible = false };

    /// <summary>격자 간격(도).</summary>
    private const int GridStep = 25;

    /// <summary>
    /// 격자 도수 글씨 켜 — 지도와 함께 움직이지 않고 <b>보이는 화면의 위쪽·왼쪽 가장자리</b>에 붙는다.
    /// 지도 가장자리에 달아 두면 키웠을 때 화면 밖으로 밀려나 안 보였다.
    /// </summary>
    private readonly Canvas _gridTags = new() { IsHitTestVisible = false, Width = ViewW, Height = ViewH };

    /// <summary>축 제목이 차지하는 왼쪽 위 모서리 — 도수 글씨가 여기와 겹치면 안 단다.</summary>
    private const double AxisW = 30, AxisH = 26;

    private static readonly Brush GridInk = Frozen(Color.FromArgb(0xB0, 0xC8, 0xC8, 0xC8));

    /// <summary>바람표를 읽었는지 — 못 읽었으면 단추도 설명도 안 낸다.</summary>
    private readonly bool _hasFlows;

    /// <summary>풍향에 쓴 달.</summary>
    private readonly int _month;

    /// <summary>풍향 · 해류 켜고 끄기(단추와 글쇠가 같이 쓴다).</summary>
    private Action? _toggleWind, _toggleCurrent;

    /// <summary>발견물 · 도시 켜고 끄기 — 찾은 것이 꺼진 켜에 있으면 켠다.</summary>
    private Action? _toggleSpots, _toggleCities;

    /// <summary>찾기 칸이 훑는 표식 — 이름과 그 표식, 앉은 켜.</summary>
    private readonly List<(string Name, Pin Pin, Canvas Layer)> _searchable = [];

    /// <summary>지금 찾은 것들과 그 가운데 보고 있는 차례.</summary>
    private List<(string Name, Pin Pin, Canvas Layer)> _hits = [];
    private int _hit;

    private readonly TextBox _query;
    private readonly TextBlock _hitNote;

    /// <summary>찾은 표식을 두르는 고리 — 모든 켜 위에 얹는다.</summary>
    private readonly System.Windows.Shapes.Ellipse _ring = new()
    {
        Stroke = Frozen(Color.FromRgb(0xFF, 0xE0, 0x40)),
        IsHitTestVisible = false,
        Visibility = Visibility.Collapsed,
    };

    /// <summary>고리 지름 · 선 굵기(화면 점).</summary>
    private const double RingSize = 16, RingLine = 2.5;

    /// <summary>찾은 것으로 옮겨 갈 때 적어도 이만큼 키운다 — 이름표가 서는 배율이다.</summary>
    private const double SearchZoom = 6;

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
    private FrameworkElement? _shipDot;

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
                               Func<double, double, string>? autoSail,
                               IReadOnlyList<(string Name, double X, double Y, bool Library)>? cities,
                               IReadOnlyDictionary<int, (double X, double Y)>? inTown,
                               Func<double, double, (bool Ok, string Message)>? walk,
                               IReadOnlySet<int>? eventsDone)
    {
        _walk = walk;
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

        // 격자는 화살표보다도 밑이다 — 바탕 바로 위.
        _world.Children.Add(_gridLayer);
        DrawGrid(width, height);
        _gridLayer.Visibility = GameSettings.DiscoveryMapGrid ? Visibility.Visible : Visibility.Collapsed;

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

        // 도시는 발견물 표식보다 아래 켜다 — 같은 자리(건물 속 발견물)에서 발견물 점이 위에 온다.
        _world.Children.Add(_cityLayer);
        _cityLayer.Visibility = GameSettings.DiscoveryMapCities ? Visibility.Visible : Visibility.Collapsed;
        foreach (var city in cities ?? [])
        {
            Mark(city.X / ExploredMap.CellsPerBlock, city.Y / ExploredMap.CellsPerBlock,
                 city.Library ? LibrarySize : CitySize, CityInk, city.Name, label: true, layer: _cityLayer,
                 glyph: city.Library ? "도" : null);
            _searchable.Add((city.Name, _pins[^1], _cityLayer));
        }

        _world.Children.Add(_spotLayer);
        _spotLayer.Children.Add(_yetLayer);
        // 켜를 끄고 켜면 이름표 비키기를 다시 센다 — 꺼진 켜의 이름표가 자리를 차지하지 않게.
        _cityLayer.IsVisibleChanged += (_, _) => { if (IsLoaded) Apply(); };
        _spotLayer.IsVisibleChanged += (_, _) => { if (IsLoaded) Apply(); };
        _yetLayer.IsVisibleChanged += (_, _) => { if (IsLoaded) Apply(); };
        // 이름표 비키기는 창이 뜬 뒤에 센다 — 그 앞에 재면 글꼴을 물려받기 전이라 크기가 틀려, 처음 열 때
        // 이름표가 엉뚱한 데 몰려 겹쳐 보였다(키우면 다시 세어 제자리로 왔다, 제보).
        Loaded += (_, _) => { _declutteredFor = ""; Apply(); };
        _spotLayer.Visibility = GameSettings.DiscoveryMapSpots ? Visibility.Visible : Visibility.Collapsed;
        _yetLayer.Visibility = GameSettings.DiscoveryMapYet ? Visibility.Visible : Visibility.Collapsed;

        int shown = 0, done = 0;
        // 0~Count-1 로만 돌면 DiscoveryEdits 로 더한 줄(274 이상)이 안 뜬다 — table.Discoveries 를 돈다.
        foreach (var row in table.Discoveries)
        {
            // 바다 · 뭍 자리가 없는 발견물은 도시 건물 속에서 찾는 것(진흙 모스크 따위) — 그 도시 자리에 찍는다.
            double x, y;
            if (row.HasPlace)
            {
                // 자리는 네모라 한가운데를 찍는다.
                x = (row.X1 + row.X2) / 2.0;
                y = (row.Y1 + row.Y2) / 2.0;
            }
            else if (inTown != null && inTown.TryGetValue(row.Id, out var town))
                (x, y) = town;
            else continue;

            bool found = player.HasFound(row.Id) || eventsDone?.Contains(row.Id) == true;
            var spotDot = Mark(x / ExploredMap.CellsPerBlock, y / ExploredMap.CellsPerBlock,
                            MarkSize, found ? Found : Yet, row.Name, label: true, layer: found ? _spotLayer : _yetLayer);
            _searchable.Add((row.Name, _pins[^1], found ? _spotLayer : _yetLayer));
            if (walk != null && spotDot is System.Windows.Shapes.Ellipse dot) WalkTarget(dot, x, y);
            shown++;
            if (found) done++;
        }

        _found = shown;
        _done = done;

        if (ship is { } at)
            _shipDot = Mark(at.X / ExploredMap.CellsPerBlock, at.Y / ExploredMap.CellsPerBlock,
                            ShipSize, Mine, "지금 자리", label: false);

        // 찾은 것을 두르는 고리는 모든 켜 위다.
        _world.Children.Add(_ring);

        var viewport = new Border
        {
            Width = ViewW,
            Height = ViewH,
            BorderBrush = GameUi.Edge,
            BorderThickness = new Thickness(1),
            Background = Brushes.Black,
            ClipToBounds = true,
            Child = new Canvas { Children = { _world, _gridTags } },
            Cursor = Cursors.SizeAll,
            Focusable = true,
            FocusVisualStyle = null,
        };

        viewport.MouseWheel += (_, e) => ZoomAt(e.Delta > 0 ? 1 : -1, e.GetPosition(viewport));
        viewport.MouseLeftButtonDown += (_, e) =>
        {
            // 지도를 누르면 글쇠가 다시 지도로 온다 — 찾기 칸에 남아 있으면 화살표 · W · C 가 칸에 먹힌다.
            Keyboard.Focus(viewport);
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

        // 안내 글은 <b>지도 폭에서 접는다</b> — 한 줄로 두면 글이 창 폭을 정해 지도 양옆이 쓸데없이 넓어졌다.
        _note = new TextBlock
        {
            Foreground = GameUi.Text,
            FontSize = 14,
            Margin = new Thickness(4, 2, 4, 4),
            HorizontalAlignment = HorizontalAlignment.Center,
            MaxWidth = ViewW,
            TextWrapping = TextWrapping.Wrap,
            TextAlignment = TextAlignment.Center,
        };

        var ok = GameUi.PushButton("확인", Close, 88);
        ok.HorizontalAlignment = HorizontalAlignment.Center;
        ok.Margin = new Thickness(0, 0, 0, 4);

        var stack = new StackPanel { Margin = new Thickness(2) };
        stack.Children.Add(viewport);
        var toggles = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 2, 0, 0),
        };
        if (_hasFlows)
        {
            toggles.Children.Add(Toggle("풍향", () => GameSettings.DiscoveryMapWind,
                                        v => GameSettings.DiscoveryMapWind = v, _windLayer, out _toggleWind));
            toggles.Children.Add(Toggle("해류", () => GameSettings.DiscoveryMapCurrent,
                                        v => GameSettings.DiscoveryMapCurrent = v, _currentLayer, out _toggleCurrent));
        }
        // 위도·경도 격자 — 바람표가 없어도 늘 선다.
        toggles.Children.Add(Toggle("격자", () => GameSettings.DiscoveryMapGrid,
                                    v => GameSettings.DiscoveryMapGrid = v, _gridLayer, out _));
        toggles.Children.Add(Toggle("발견물", () => GameSettings.DiscoveryMapSpots,
                                    v => GameSettings.DiscoveryMapSpots = v, _spotLayer, out _toggleSpots));
        // 못 찾은 것(회색)은 따로 끈다 — 미니맵처럼 감춰 두고 싶은데 여기서만 늘 보여 스포일러가 됐다(제보).
        toggles.Children.Add(Toggle("못 찾은 것", () => GameSettings.DiscoveryMapYet,
                                    v => GameSettings.DiscoveryMapYet = v, _yetLayer, out _, 136));
        if (cities is { Count: > 0 })
            toggles.Children.Add(Toggle("도시", () => GameSettings.DiscoveryMapCities,
                                        v => GameSettings.DiscoveryMapCities = v, _cityLayer, out _toggleCities));
        stack.Children.Add(toggles);

        // 찾기 — 발견물 · 도시 이름에 든 말로 찾는다. 치는 대로 첫 것으로 옮겨 가고, Enter · 「다음」으로 다음 것.
        _query = new TextBox
        {
            Width = 220,
            FontSize = 14,
            VerticalContentAlignment = VerticalAlignment.Center,
            Padding = new Thickness(2, 0, 2, 0),
            BorderThickness = new Thickness(2),
            BorderBrush = GameUi.ItemEdge,
        };
        _query.TextChanged += (_, _) => Search();
        _query.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter) { Step(Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? -1 : 1); e.Handled = true; }
            else if (e.Key == Key.Escape && _query.Text.Length > 0) { _query.Text = ""; e.Handled = true; }
        };
        _hitNote = new TextBlock
        {
            Foreground = GameUi.Text,
            FontSize = 14,
            MinWidth = 200,
            Margin = new Thickness(8, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
        };
        var finder = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 4, 0, 0),
        };
        finder.Children.Add(new TextBlock
        {
            Text = Lang.T("찾기"), Foreground = GameUi.Text, FontSize = 14, FontWeight = FontWeights.Bold,
            Margin = new Thickness(0, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center,
        });
        finder.Children.Add(_query);
        finder.Children.Add(GameUi.ImeBadge(_query));
        finder.Children.Add(GameUi.PushButton(Lang.T("이전"), () => Step(-1), 70));
        finder.Children.Add(GameUi.PushButton(Lang.T("다음"), () => Step(1), 70));
        finder.Children.Add(_hitNote);
        stack.Children.Add(finder);
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
        // 찾기 칸에 치는 글자는 글쇠로 안 먹는다(W · C · 화살표 · + - 따위). Esc 는 칸이 비었을 때만 여기로 온다.
        if (e.OriginalSource is TextBox && e.Key != Key.Escape) return;
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

    /// <summary>찾기 칸의 말로 다시 찾고, 첫 것으로 옮겨 간다. 띄어쓰기와 대소문자는 안 가린다.</summary>
    private void Search()
    {
        static string Bare(string s) => s.Replace(" ", "").Replace("·", "").ToLowerInvariant();
        string want = Bare(_query.Text);
        _hits = want.Length == 0
            ? []
            : [.. _searchable.Where(s => s.Layer != _yetLayer || _yetLayer.Visibility == Visibility.Visible)
                             .Where(s => Bare(s.Name).Contains(want) || Bare(Lang.T(s.Name)).Contains(want))
                             .OrderBy(s => Bare(s.Name).StartsWith(want) ? 0 : 1)];
        _hit = 0;
        ShowHit();
    }

    /// <summary>찾은 것 가운데 앞 · 뒤 것으로 옮겨 간다(끝에서는 처음으로 돈다).</summary>
    private void Step(int by)
    {
        if (_hits.Count == 0) return;
        _hit = ((_hit + by) % _hits.Count + _hits.Count) % _hits.Count;
        ShowHit();
    }

    /// <summary>지금 고른 찾은 것을 가운데 두고 고리를 두른다. 그 켜가 꺼져 있으면 켠다.</summary>
    private void ShowHit()
    {
        if (_hits.Count == 0)
        {
            _ring.Visibility = Visibility.Collapsed;
            _hitNote.Text = _query.Text.Trim().Length == 0 ? "" : Lang.T("찾는 것이 없습니다");
            return;
        }

        var (name, pin, layer) = _hits[_hit];
        if (layer == _cityLayer ? _cityLayer.Visibility != Visibility.Visible : _spotLayer.Visibility != Visibility.Visible)
            (layer == _cityLayer ? _toggleCities : _toggleSpots)?.Invoke();
        _hitNote.Text = $"{_hit + 1}/{_hits.Count}  {Lang.T(name)}";
        _ring.Visibility = Visibility.Visible;
        while (Z < SearchZoom && _zoom < Zooms.Length - 1) _zoom++;
        CenterOn(pin.X, pin.Y);
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
        PlaceGridTags();

        // 표식은 화면에서 같은 크기로 보이게 지도 점 크기를 배율만큼 줄인다.
        foreach (var pin in _pins) Place(pin);
        if (_hits.Count > 0)
        {
            var at = _hits[_hit].Pin;
            _ring.Width = _ring.Height = RingSize / Z;
            _ring.StrokeThickness = RingLine / Z;
            Canvas.SetLeft(_ring, at.X - RingSize / Z / 2);
            Canvas.SetTop(_ring, at.Y - RingSize / Z / 2);
        }

        // 이름표는 어느 배율부터 단다 — 온 지도에 가까우면 이름끼리 겹친다.
        bool labels = Z >= LabelFrom;
        string state = $"{_zoom}|{_cityLayer.Visibility}|{_spotLayer.Visibility}|{_yetLayer.Visibility}";
        if (labels && state != _declutteredFor && IsLoaded) { _declutteredFor = state; Declutter(); }
        foreach (var pin in _pins)
            if (pin.Tag is { } tag) tag.Visibility = labels && !pin.Crowded ? Visibility.Visible : Visibility.Collapsed;

        // 조각마다 따로 번역한다 — 붙는 조각이 그때그때 달라 이은 글 통째로는 번역표에 없다.
        _note.Text = Lang.T($"발견물 {_found}곳 · 찾은 것 {_done}곳 · 배율 x{Z:0.#}")
                   + Environment.NewLine + Lang.T("(휠 키우기·줄이기 · 끌어서 옮기기")
                   + (_warp != null ? Lang.T(" · 오른쪽 단추 그 자리로 옮기기") : "")
                   + (_autoSail != null ? Lang.T(" · Shift+오른쪽 단추 그 자리로 자동항해") : "")
                   + Lang.T(" · 빨강 찾음 · 회색 아직 · 파랑 내 자리")
                   + Lang.T(" · 주황 도시(「도」 도서관)")
                   + (_hasFlows
                       ? Lang.T($" · 보라 풍향 {(WindTable.IsFirstHalf(_month) ? "1~6월" : "7~12월")} · 청록 해류)")
                       : ")")
                   + _said;
    }

    /// <summary>
    /// 켜고 끄는 단추 하나 — 누르면 설정을 뒤집고 그 켜를 보이거나 감춘다.
    /// </summary>
    /// <param name="flip">글쇠가 같은 일을 하도록 내주는 손.</param>
    private static Border Toggle(string name, Func<bool> get, Action<bool> set, Canvas layer, out Action flip, double width = 110)
    {
        Border? button = null;
        void Refresh()
        {
            bool on = get();
            layer.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
            if (button?.Child is TextBlock label) label.Text = $"{Lang.T(name)} {Lang.T(on ? "끄기" : "켜기")}";
        }

        flip = () => { set(!get()); Refresh(); };
        button = GameUi.PushButton(name, flip, width);
        Refresh();
        return button;
    }

    /// <summary>
    /// 바람표 칸마다 풍향과 해류 화살표를 긋는다 — 켜 하나에 <b>도형 하나</b>로 묶어 가볍게 둔다.
    /// </summary>
    private void DrawFlows(WindTable table, int width, int height)
    {
        var (wind, current) = FlowGeometry(table, _month, width, height);
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
    /// 위도·경도 격자 선을 긋는다 — 25도마다 하나. 지도 점(칸/4)으로 가로가 경도 360도,
    /// 세로가 위도 180도다(왼쪽 끝 서경 180 · 위 끝 북위 90).
    /// </summary>
    private void DrawGrid(int width, int height)
    {
        var lines = new StreamGeometry();
        using (var g = lines.Open())
        {
            for (int lon = -180 + GridStep; lon < 180; lon += GridStep)
            {
                double x = LonX(lon);
                g.BeginFigure(new Point(x, 0), false, false);
                g.LineTo(new Point(x, height), true, false);
            }
            for (int lat = -90 + GridStep; lat < 90; lat += GridStep)
            {
                double y = LatY(lat);
                g.BeginFigure(new Point(0, y), false, false);
                g.LineTo(new Point(width, y), true, false);
            }
        }
        lines.Freeze();
        _gridLayer.Children.Insert(0, new System.Windows.Shapes.Path
        {
            Data = lines, Stroke = GridInk, StrokeThickness = 0.5,
        });

        // 도수 글씨 켜는 격자 켜를 따라 켜지고 꺼진다.
        _gridTags.SetBinding(VisibilityProperty,
            new System.Windows.Data.Binding(nameof(Visibility)) { Source = _gridLayer });
    }

    private double LonX(int lon) => (lon + 180) / 360.0 * _chartW;
    private double LatY(int lat) => (90 - lat) / 180.0 * _chartH;

    /// <summary>
    /// 도수 글씨를 지금 보이는 자리에 맞춰 다시 단다 — 경도는 위쪽, 위도는 왼쪽 가장자리. 왼쪽 위 모서리에는
    /// 축 제목 「경도」·「위도」를 둔다.
    /// </summary>
    private void PlaceGridTags()
    {
        _gridTags.Children.Clear();
        for (int lon = -180 + GridStep; lon < 180; lon += GridStep)
        {
            double x = (LonX(lon) - _vx) * Z;
            if (x < AxisW || x > ViewW - 24) continue;
            Tag(lon == 0 ? "0°" : lon < 0 ? $"{-lon}°W" : $"{lon}°E", x + 2, 2);
        }
        for (int lat = -90 + GridStep; lat < 90; lat += GridStep)
        {
            double y = (LatY(lat) - _vy) * Z;
            if (y < AxisH || y > ViewH - 12) continue;
            Tag(lat == 0 ? "0°" : lat < 0 ? $"{-lat}°S" : $"{lat}°N", 2, y + 1);
        }
        Tag("경도 →", 2, 2, bold: true);
        Tag("위도 ↓", 2, 13, bold: true);

        void Tag(string text, double x, double y, bool bold = false)
        {
            var tag = new TextBlock
            {
                Text = text, Foreground = GridInk, FontSize = 10,
                FontWeight = bold ? FontWeights.Bold : FontWeights.Normal,
            };
            Canvas.SetLeft(tag, x);
            Canvas.SetTop(tag, y);
            _gridTags.Children.Add(tag);
        }
    }

    /// <summary>
    /// 풍향·해류 화살표 도형 둘 — 발견물 지도와 미니맵이 같이 쓴다. 한 칸 윗쪽에 풍향, 아랫쪽에 해류다.
    /// </summary>
    internal static (StreamGeometry Wind, StreamGeometry Current) FlowGeometry(WindTable table, int month,
                                                                             int width, int height)
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
                var blow = table.WindAt(cell, month);
                if (!blow.IsStill) Arrow(w, table, blow, cx, cy - cellH / 4, cellW, maxSpeed: 6);
                var flow = table.CurrentAt(cell);
                if (!flow.IsStill) Arrow(c, table, flow, cx, cy + cellH / 4, cellW, maxSpeed: 7);
            }
        }

        wind.Freeze();
        current.Freeze();
        return (wind, current);
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
    /// <param name="glyph">동그라미 안에 넣을 글자(도서관 「도」). 없으면 그냥 점이다.</param>
    private FrameworkElement Mark(double x, double y, double size, Brush fill,
                                  string name, bool label, Canvas? layer = null, string? glyph = null)
    {
        layer ??= _world;
        FrameworkElement dot = new System.Windows.Shapes.Ellipse
        {
            Width = size,
            Height = size,
            Fill = fill,
            ToolTip = name,
        };
        if (glyph != null)
        {
            // 글자는 동그라미를 따라 줄고 는다 — 크기는 Place 가 점째 바꾼다.
            dot = new Grid
            {
                Width = size,
                Height = size,
                ToolTip = name,
                Children =
                {
                    new System.Windows.Shapes.Ellipse { Fill = fill },
                    new Viewbox
                    {
                        // 여백은 글자 안쪽에 둔다 — 상자 밖에 두면 점을 줄일 때 여백만 그대로라 글자가 찌그러진다.
                        Child = new TextBlock
                        {
                            Text = glyph, Foreground = Brushes.White, FontWeight = FontWeights.Bold,
                            Margin = new Thickness(3, 1, 3, 2),
                        },
                    },
                },
            };
        }
        layer.Children.Add(dot);

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
            layer.Children.Add(tag);
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
        // 내 자리 점은 모드 「발견물 지도 내 자리 크기」만큼 키우고 줄인다.
        if (pin == _shipPin) size *= GameSettings.DiscoveryMapShipScales[GameSettings.DiscoveryMapShipScale];
        // 내 자리 점은 키울수록 화면에서도 조금씩 준다 — 붙박아 두면 크게 키웠을 때 둘레를 덮었다.
        if (pin == _shipPin && Z > ZoomBase) size *= Math.Max(ShipShrinkFloor, Math.Sqrt(ZoomBase / Z));
        pin.Dot.Width = pin.Dot.Height = size;
        Canvas.SetLeft(pin.Dot, pin.X - size / 2);
        Canvas.SetTop(pin.Dot, pin.Y - size / 2);

        if (pin.Tag is not { } tag) return;
        tag.FontSize = LabelSize * ZoomBase / Z;
        var off = pin.Offset ?? new Point(size * Z / 2 + 1, -tag.FontSize * Z * 0.7);
        Canvas.SetLeft(tag, pin.X + off.X / Z);
        Canvas.SetTop(tag, pin.Y + off.Y / Z);
    }

    /// <summary>
    /// 겹치는 이름표를 비킨다. 보이는 켜(발견물 · 못 찾은 것 · 도시)의 이름표만 센다. 배율이나 켜가 바뀔 때만 다시 센다.
    /// </summary>
    /// <remarks>
    /// <b>같은 자리</b>에 찍힌 것(도시와 그 건물 속 발견물 — 모스크바와 우스펜스키 대성당 따위)은 이름표 한 덩이로 묶어
    /// 도시 이름 밑에 발견물 이름을 줄줄이 붙인다. 덩이마다 점 오른쪽 · 왼쪽 · 위 · 아래 차례로 앞서 놓은 이름표와
    /// 다른 점을 안 덮는 자리를 찾고, 자리가 없으면 이 배율에서는 이름을 안 단다 — 키우면 자리가 나서 붙는다.
    /// 예전에는 부딪히면 줄 높이만큼 열두 번까지 아래로 밀어, 도시와 발견물을 같이 켜면 이름이 엉뚱한 데 쌓였다(제보).
    /// 발견물 덩이를 도시만 있는 덩이보다 먼저 놓는다 — 발견물 지도라 발견물 이름이 앞선다.
    /// </remarks>
    private void Declutter()
    {
        var live = _pins.Where(p => p.Tag != null && LayerOn(p.Dot)).ToList();
        var dots = live.Select(p => (Pin: p, Box: new Rect(p.X - p.Dot.Width / 2, p.Y - p.Dot.Height / 2,
                                                         p.Dot.Width, p.Dot.Height))).ToList();
        var groups = live.GroupBy(p => (Math.Round(p.X, 1), Math.Round(p.Y, 1)))
                         .Select(g => g.OrderBy(p => p.Tag!.Parent == _cityLayer ? 0 : 1).ToList())
                         .OrderBy(g => g.All(p => p.Tag!.Parent == _cityLayer) ? 1 : 0)
                         .ThenBy(g => g[0].Y).ThenBy(g => g[0].X);

        var placed = new List<Rect>();
        foreach (var group in groups)
        {
            foreach (var pin in group)
            {
                pin.Offset = null;
                pin.Crowded = false;
                Place(pin);
                pin.Tag!.Visibility = Visibility.Visible;   // 숨긴(Collapsed) 글은 크기가 0 으로 재진다
                pin.Tag.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            }

            double line = group.Max(p => p.Tag!.DesiredSize.Height) * 0.9;
            double w = group.Max(p => p.Tag!.DesiredSize.Width);
            double h = line * (group.Count - 1) + group[^1].Tag!.DesiredSize.Height * 0.85;
            double r = group.Max(p => p.Dot.Width) / 2, gap = 1 / Z, x = group[0].X, y = group[0].Y;
            double lift = group[0].Tag!.FontSize * 0.7;
            Point[] tries =
            [
                new(x + r + gap, y - lift),         // 오른쪽
                new(x - r - gap - w, y - lift),     // 왼쪽
                new(x - w / 2, y - r - gap - h),    // 위
                new(x - w / 2, y + r + gap),        // 아래
            ];

            Rect? spot = null;
            foreach (var at in tries)
            {
                var box = new Rect(at.X, at.Y, w, h);
                if (placed.Any(b => b.IntersectsWith(box))) continue;
                if (dots.Any(d => !group.Contains(d.Pin) && d.Box.IntersectsWith(box))) continue;
                spot = box;
                break;
            }

            if (spot is not { } found)
            {
                foreach (var pin in group) pin.Crowded = true;
                continue;
            }
            placed.Add(found);
            for (int i = 0; i < group.Count; i++)
            {
                var pin = group[i];
                pin.Offset = new Point((found.X - pin.X) * Z, (found.Y + line * i - pin.Y) * Z);
                Place(pin);
            }
        }
    }

    /// <summary>표식이 앉은 켜가 (그 위 켜까지) 다 보이는지.</summary>
    private bool LayerOn(FrameworkElement element)
    {
        for (var up = element.Parent as FrameworkElement; up != null && up != _world; up = up.Parent as FrameworkElement)
            if (up.Visibility != Visibility.Visible) return false;
        return true;
    }

    /// <summary>
    /// 짚은 자리(지도 점)로 함대를 옮긴다. 옮겨진 자리에 내 자리 점을 다시 찍는다.
    /// </summary>
    /// <remarks>
    /// 원본에 없는 길이라 묻지 않고 바로 옮긴다 — 지도를 띄워 놓고 여러 곳을 짚어 볼 수
    /// 있어야 쓸모가 있다. 뭍을 짚으면 부르는 쪽이 가까운 물칸으로 밀어 준다.
    /// </remarks>
    private void Warp(double px, double py)
    {
        if (_warp == null) return;

        var landed = _warp(px * ExploredMap.CellsPerBlock, py * ExploredMap.CellsPerBlock);
        if (landed is not { } spot)
        {
            _said = "   ·   함대를 옮길 수 없습니다(도시 안 · 육지 위이거나 가까이에 바다가 없음)";
            Apply();
            return;
        }

        double x = spot.X / ExploredMap.CellsPerBlock, y = spot.Y / ExploredMap.CellsPerBlock;
        _shipDot ??= Mark(x, y, ShipSize, Mine, "지금 자리", label: false);
        if (_shipPin is { } pin) { pin.X = x; pin.Y = y; Place(pin); }

        _said = "   ·   그 자리로 옮겼습니다(닻을 내린 채)";
        Apply();
    }

    /// <summary>뭍 자동이동을 거는 손(모드 「뭍 자동이동」). 칸 자리를 받아 시작했는지와 알림 말을 돌려준다. 없으면 그 기능이 없다.</summary>
    private readonly Func<double, double, (bool Ok, string Message)>? _walk;

    /// <summary>
    /// 발견물 점 하나를 자동이동 과녁으로 — 마우스를 올리면 흰 테두리가 서고, 오른쪽 단추로 누르면 그 자리로 걸어간다.
    /// 걷기 시작하면 지도를 닫는다(창이 떠 있는 동안은 게임이 멈춰 있다).
    /// </summary>
    private void WalkTarget(System.Windows.Shapes.Ellipse dot, double cellX, double cellY)
    {
        dot.Cursor = Cursors.Hand;
        dot.MouseEnter += (_, _) =>
        {
            dot.Stroke = Brushes.White;
            dot.StrokeThickness = Math.Max(0.1, dot.Width * 0.22);
        };
        dot.MouseLeave += (_, _) => dot.Stroke = null;
        dot.MouseRightButtonUp += (_, e) =>
        {
            if (_walk == null) return;
            e.Handled = true;
            var (ok, message) = _walk(cellX, cellY);
            if (ok) { Close(); return; }
            _said = "   ·   " + message;
            Apply();
        };
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
    /// <param name="cities">찍을 도시(이름 · 칸 자리). 없으면 「도시」 단추가 안 나온다.</param>
    /// <param name="inTown">건물 속에서 찾는 발견물의 자리 — 발견물 번호 → 그 도시 칸 자리.</param>
    /// <param name="eventsDone">대본이 발견을 안 적지만 사건을 마친 발견물 — 찾은 것으로 찍는다.</param>
    public static void Show(Window owner, uint[]? chart, int width, int height,
                            DiscoveryTable? table, Player player, (double X, double Y)? ship,
                            WindTable? wind = null,
                            Func<double, double, (double X, double Y)?>? warp = null,
                            Func<double, double, string>? autoSail = null,
                            IReadOnlyList<(string Name, double X, double Y, bool Library)>? cities = null,
                            IReadOnlyDictionary<int, (double X, double Y)>? inTown = null,
                            Func<double, double, (bool Ok, string Message)>? walk = null,
                            IReadOnlySet<int>? eventsDone = null)
    {
        if (chart == null || table == null || width <= 0 || height <= 0) return;
        new DiscoveryMapDialog(chart, width, height, table, player, ship, wind, warp, autoSail, cities, inTown, walk, eventsDone)
        { Owner = owner }.ShowDialog();
    }
}
