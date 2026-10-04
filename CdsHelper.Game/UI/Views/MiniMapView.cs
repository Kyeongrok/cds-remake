using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using CdsHelper.Game.Local.Helpers;
using CdsHelper.Game.Local.Settings;
using CdsHelper.Support.Local.Models;

namespace CdsHelper.Game.UI.Views;

/// <summary>
/// 지도 오른쪽 아래에 붙는 <b>미니맵</b> — 발견물 지도(<see cref="DiscoveryMapDialog"/>)를 작게 잘라 배를 가운데 두고 따라간다.
/// </summary>
/// <remarks>
/// 놀이에는 없는 것이라 개발 창의 「미니맵」으로 켠다. 바탕은 발견물 지도와 같은 온 세계 양피지 지도이고(점 하나가 칸 4x4),
/// 빨강은 찾은 발견물 · 회색은 아직 · 파랑은 내 자리다. 도시 안에서는 안 뜬다(부르는 쪽이 가린다).
/// </remarks>
internal sealed class MiniMapView : Border
{
    /// <summary>보이는 창 크기(화면 점) — 모드 창 「미니맵」 탭의 크기 배율을 곱한다.</summary>
    public static double ViewW => 260 * GameSettings.MiniMapScale;
    public static double ViewH => 160 * GameSettings.MiniMapScale;

    /// <summary>지도 점 하나를 몇 배로 키워 보일지.</summary>
    private const double Zoom = 2;

    private static readonly Brush Found = Frozen(Color.FromRgb(0xC0, 0x30, 0x20));
    private static readonly Brush Yet = Frozen(Color.FromRgb(0x50, 0x50, 0x50));
    private static readonly Brush Mine = Frozen(Color.FromRgb(0x20, 0x40, 0xC0));
    private static readonly Brush CityInk = Frozen(Color.FromRgb(0xD0, 0x78, 0x00));

    private static SolidColorBrush Frozen(Color c)
    {
        var b = new SolidColorBrush(c);
        b.Freeze();
        return b;
    }

    private readonly Canvas _world = new();
    private readonly Canvas _foundLayer = new();
    private readonly Canvas _yetLayer = new();
    private readonly Canvas _cityLayer = new();
    private readonly Canvas _windLayer = new() { IsHitTestVisible = false };
    private readonly Canvas _currentLayer = new() { IsHitTestVisible = false };
    private readonly Border _windButton, _currentButton;

    /// <summary>화살표를 마지막으로 그린 반년(1~6월이면 참). 바뀌면 다시 그린다.</summary>
    private bool? _flowsFirstHalf;

    /// <summary>마우스가 미니맵 위에 있는지 — 부르는 쪽이 이때 불투명도를 반으로 낮춘다.</summary>
    public bool Hovered => IsMouseOver;

    /// <summary>마우스가 올라오거나 나갔다.</summary>
    public event Action? HoverChanged;
    private readonly Ellipse _ship = new() { Width = 5, Height = 5, Fill = Mine, Stroke = Brushes.White, StrokeThickness = 0.8 };
    private readonly TranslateTransform _shift = new();

    /// <summary>점을 마지막으로 찍었을 때의 찾은 발견물 수 — 달라지면 다시 찍는다.</summary>
    private int _markedFound = -1;

    public MiniMapView()
    {
        Width = ViewW;
        Height = ViewH;
        BorderBrush = GameUi.Edge;
        BorderThickness = new Thickness(2);
        Background = Brushes.Black;
        ClipToBounds = true;
        // 마우스를 받는다 — 올리면 반투명해지고, 오른쪽 위 단추로 풍향·해류를 켠다.
        IsHitTestVisible = true;
        // 깃발을 따로 안 둔다 - 마우스가 올라간 채 팝업이 닫히면 MouseLeave 가 안 와 흐린 채로 굳었다.
        MouseEnter += (_, _) => HoverChanged?.Invoke();
        MouseLeave += (_, _) => HoverChanged?.Invoke();

        var moves = new TransformGroup();
        moves.Children.Add(new ScaleTransform(Zoom, Zoom));
        moves.Children.Add(_shift);
        _world.RenderTransform = moves;
        _world.Children.Add(_windLayer);
        _world.Children.Add(_currentLayer);
        _world.Children.Add(_cityLayer);
        _world.Children.Add(_yetLayer);
        _world.Children.Add(_foundLayer);
        _world.Children.Add(_ship);

        // 오른쪽 위 작은 단추 둘 — 「풍」 풍향 · 「류」 해류. 켜져 있으면 그 화살표 색으로 밝다.
        _windButton = FlowButton("풍", "풍향 화살표 켜기/끄기",
            () => GameSettings.MiniMapWind, v => GameSettings.MiniMapWind = v);
        _currentButton = FlowButton("류", "해류 화살표 켜기/끄기",
            () => GameSettings.MiniMapCurrent, v => GameSettings.MiniMapCurrent = v);
        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 2, 2, 0),
            Children = { _windButton, _currentButton },
        };
        Child = new Grid { Children = { new Canvas { Children = { _world } }, buttons } };
        SyncFlows();
    }

    /// <summary>작은 켜기 단추 하나.</summary>
    private Border FlowButton(string text, string tip, Func<bool> get, Action<bool> set)
    {
        var button = new Border
        {
            Width = 18,
            Height = 16,
            Margin = new Thickness(2, 0, 0, 0),
            CornerRadius = new CornerRadius(2),
            BorderThickness = new Thickness(1),
            Cursor = System.Windows.Input.Cursors.Hand,
            ToolTip = tip,
            Child = new TextBlock
            {
                Text = text,
                FontSize = 10,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            },
        };
        button.MouseLeftButtonDown += (_, e) => { e.Handled = true; set(!get()); SyncFlows(); };
        return button;
    }

    private static readonly Brush OffFill = Frozen(Color.FromArgb(0xB0, 0x20, 0x20, 0x20));
    private static readonly Brush OffEdge = Frozen(Color.FromArgb(0xA0, 0x80, 0x80, 0x80));

    /// <summary>모드 창 「미니맵」 탭에서 고른 표식만 보이게 한다 — 화살표 단추도 같이 맞춘다.</summary>
    public void SyncMarks()
    {
        _foundLayer.Visibility = GameSettings.MiniMapFound ? Visibility.Visible : Visibility.Collapsed;
        _yetLayer.Visibility = GameSettings.MiniMapYet ? Visibility.Visible : Visibility.Collapsed;
        _cityLayer.Visibility = GameSettings.MiniMapCities ? Visibility.Visible : Visibility.Collapsed;
        SyncFlows();
        if (Math.Abs(Width - ViewW) > 0.01) { Width = ViewW; Height = ViewH; }

        // 표식 크기가 바뀌었으면 찍어 둔 점을 가운데를 지킨 채 다시 잰다.
        double size = GameSettings.MiniMapMarkSize;
        if (Math.Abs(size - _markSize) < 0.01) return;
        foreach (var layer in new[] { _foundLayer, _yetLayer, _cityLayer })
            foreach (var child in layer.Children)
                if (child is Ellipse dot)
                {
                    double cx = Canvas.GetLeft(dot) + dot.Width / 2, cy = Canvas.GetTop(dot) + dot.Height / 2;
                    dot.Width = dot.Height = size;
                    Canvas.SetLeft(dot, cx - size / 2);
                    Canvas.SetTop(dot, cy - size / 2);
                }
        _markSize = size;
    }

    /// <summary>지금 찍힌 점의 크기 — 설정과 달라지면 <see cref="SyncMarks"/> 가 다시 잰다.</summary>
    private double _markSize = GameSettings.MiniMapMarkSize;

    /// <summary>점 하나 — 가운데(지도 점)와 색.</summary>
    private Ellipse Dot(double x, double y, Brush ink)
    {
        var dot = new Ellipse { Width = _markSize, Height = _markSize, Fill = ink };
        Canvas.SetLeft(dot, x - _markSize / 2);
        Canvas.SetTop(dot, y - _markSize / 2);
        return dot;
    }

    /// <summary>도시 점을 다시 찍는다 — 자리는 칸. 아는 도시가 늘면 부르는 쪽이 다시 준다.</summary>
    public void SetCities(IReadOnlyList<(double X, double Y)> cities)
    {
        _cityLayer.Children.Clear();
        foreach (var (cx, cy) in cities)
        {
            _cityLayer.Children.Add(Dot(cx / ExploredMap.CellsPerBlock, cy / ExploredMap.CellsPerBlock, CityInk));
        }
    }

    /// <summary>켠 켜만 보이고, 단추 모양을 켜짐/꺼짐에 맞춘다.</summary>
    private void SyncFlows()
    {
        bool wind = GameSettings.MiniMapWind, current = GameSettings.MiniMapCurrent;
        _windLayer.Visibility = wind ? Visibility.Visible : Visibility.Collapsed;
        _currentLayer.Visibility = current ? Visibility.Visible : Visibility.Collapsed;
        Paint(_windButton, wind, DiscoveryMapDialog.WindInk);
        Paint(_currentButton, current, DiscoveryMapDialog.CurrentInk);
    }

    private static void Paint(Border b, bool on, Brush ink)
    {
        b.Background = on ? ink : OffFill;
        b.BorderBrush = on ? Brushes.White : OffEdge;
        if (b.Child is TextBlock t) t.Foreground = on ? Brushes.White : Brushes.Gray;
    }

    /// <summary>
    /// 풍향·해류 화살표를 깐다 — 바람표가 반년마다 갈리므로 반년이 바뀌면 다시 그린다. 발견물 지도와 같은 도형이다.
    /// </summary>
    public void SetFlows(WindTable? table, int month)
    {
        if (table == null || !HasChart) return;
        bool first = WindTable.IsFirstHalf(month);
        if (_flowsFirstHalf == first) return;
        _flowsFirstHalf = first;

        var (wind, current) = DiscoveryMapDialog.FlowGeometry(table, month, (int)_world.Width, (int)_world.Height);
        _windLayer.Children.Clear();
        _currentLayer.Children.Clear();
        _windLayer.Children.Add(new Path
        {
            Data = wind, Stroke = DiscoveryMapDialog.WindInk, Fill = DiscoveryMapDialog.WindInk,
            StrokeThickness = DiscoveryMapDialog.ArrowLine,
        });
        _currentLayer.Children.Add(new Path
        {
            Data = current, Stroke = DiscoveryMapDialog.CurrentInk, Fill = DiscoveryMapDialog.CurrentInk,
            StrokeThickness = DiscoveryMapDialog.ArrowLine,
        });
    }

    /// <summary>미니맵의 불투명도를 바꾼다.</summary>
    public void SetOpacity(double opacity) => Opacity = Math.Clamp(opacity, 0.05, 1.0);

    /// <summary>바탕 지도가 섰는지. 안 섰으면 <see cref="SetChart"/> 부터.</summary>
    public bool HasChart { get; private set; }

    /// <summary>바탕 지도를 깐다 — 한 번이면 된다.</summary>
    public void SetChart(uint[] chart, int width, int height)
    {
        var bmp = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, chart, width * 4);
        bmp.Freeze();
        var image = new Image { Source = bmp, Width = width, Height = height };
        RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.NearestNeighbor);
        _world.Width = width;
        _world.Height = height;
        _world.Children.Insert(0, image);
        HasChart = true;
    }

    /// <summary>배 자리(칸)로 가운데를 옮기고, 찾은 발견물이 늘었으면 점을 다시 찍는다.</summary>
    public void Update(DiscoveryTable? table, Player player, double cellX, double cellY)
    {
        if (table != null && player.Discoveries.Count != _markedFound)
        {
            _foundLayer.Children.Clear();
            _yetLayer.Children.Clear();
            foreach (var row in table.Discoveries)
            {
                if (!row.HasPlace) continue;
                bool found = player.HasFound(row.Id);
                double x = (row.X1 + row.X2) / 2.0 / ExploredMap.CellsPerBlock;
                double y = (row.Y1 + row.Y2) / 2.0 / ExploredMap.CellsPerBlock;
                (found ? _foundLayer : _yetLayer).Children.Add(Dot(x, y, found ? Found : Yet));
            }
            _markedFound = player.Discoveries.Count;
        }

        double px = cellX / ExploredMap.CellsPerBlock, py = cellY / ExploredMap.CellsPerBlock;
        Canvas.SetLeft(_ship, px - _ship.Width / 2);
        Canvas.SetTop(_ship, py - _ship.Height / 2);
        _shift.X = ViewW / 2 - px * Zoom;
        _shift.Y = ViewH / 2 - py * Zoom;
    }
}
