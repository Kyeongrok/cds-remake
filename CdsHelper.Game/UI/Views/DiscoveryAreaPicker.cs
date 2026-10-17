using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using CdsHelper.Support.Local.Models;

namespace CdsHelper.Game.UI.Views;

/// <summary>
/// 세계 지도를 띄워 발견물 자리(칸 범위)를 드래그로 고르는 창 — 원본에 없는 것이다(<see cref="UserDiscoveryDialog"/>).
/// </summary>
/// <remarks>
/// 지도는 네비게이션과 같은 항해지도 그림이다(점 하나가 칸 4x4, 625x313 점 = 2500x1250 칸). 휠로 키우고 줄이며,
/// 왼쪽 단추로 끌어 범위를 잡는다. 다른 발견물 자리는 옅게 깔아 겹치지 않게 본다.
/// 육지인지는 고른 범위 칸 가운데 육지가 바다보다 많은지로 정하고, 창에서 바꿀 수 있다.
/// </remarks>
public sealed class DiscoveryAreaPicker : Window
{
    /// <summary>창이 쓰는 지도 — 그림과 칸이 육지인지.</summary>
    /// <param name="Others">깔아 둘 다른 발견물 자리(X1, Y1, X2, Y2, 이름).</param>
    public sealed record Map(BitmapSource Chart, Func<int, int, bool?> IsLand,
                             IReadOnlyList<(int X1, int Y1, int X2, int Y2, string Name)> Others);

    /// <summary>고른 범위와 육지인지.</summary>
    public readonly record struct Area(int X1, int Y1, int X2, int Y2, bool Land);

    /// <summary>게임 폴더에서 지어 둔 지도 그림과 칸 — 한 번 지으면 들고 있는다.</summary>
    private static (string Dir, BitmapSource Chart, Func<int, int, bool?> IsLand)? _fromGame;

    /// <summary>
    /// 지도 창 없이 게임 폴더의 <c>WORLD.CDS</c> 로 지도를 짓는다 — 항해지도와 같은 셈(점 하나가 칸 4x4, 뭍이 많으면 뭍색)이다.
    /// 지도 창이 지도를 아직 안 읽었거나(도시 · 첫 화면) 지도 창 밖에서 열었을 때 쓴다. 못 읽으면 null.
    /// </summary>
    public static Map? FromGame(Engine.Game? game)
    {
        if (game is not { Directory.Length: > 0 }) return null;
        if (_fromGame is not { } made || made.Dir != game.Directory)
        {
            if (Support.Local.Helpers.WorldMapRenderer.LoadWorldData(System.IO.Path.Combine(game.Directory, "WORLD.CDS")) is not { } world
                || Local.Helpers.TerrainTable.Open(game.Directory) is not { } terrain)
                return null;

            int w = ExploredMap.Width, h = ExploredMap.Height;
            var dots = new uint[w * h];
            uint Color(byte index)
            {
                int k = index * 3;
                return 0xFF000000u | ((uint)Local.Helpers.GamePalette.Rgb[k] << 16)
                     | ((uint)Local.Helpers.GamePalette.Rgb[k + 1] << 8) | Local.Helpers.GamePalette.Rgb[k + 2];
            }
            for (int by = 0; by < h; by++)
                for (int bx = 0; bx < w; bx++)
                {
                    int sea = 0;
                    for (int y = 0; y < Cells; y++)
                        for (int x = 0; x < Cells; x++)
                            if (Engine.Sea.SeaPathfinder.IsSeaCell(world, terrain, bx * Cells + x, by * Cells + y)) sea++;
                    // 항해지도 색(0x00416AED) — 바다 0x2E · 뭍 0x18, 같으면 바다다.
                    dots[by * w + bx] = Color(Cells * Cells - sea > sea ? (byte)0x18 : (byte)0x2E);
                }
            var chart = BitmapSource.Create(w, h, 96, 96, PixelFormats.Bgra32, null, dots, w * 4);
            chart.Freeze();
            made = (game.Directory, chart, (x, y) => !Engine.Sea.SeaPathfinder.IsSeaCell(world, terrain, x, y));
            _fromGame = made;
        }
        var others = (game.Discoveries?.Table?.Discoveries ?? [])
            .Where(r => r.HasPlace).Select(r => (r.X1, r.Y1, r.X2, r.Y2, r.Name)).ToList();
        return new Map(made.Chart, made.IsLand, others);
    }

    private const int Cells = ExploredMap.CellsPerBlock;
    private const int MapCellsW = ExploredMap.Width * Cells, MapCellsH = ExploredMap.Height * Cells;

    private readonly Map _map;
    private readonly Canvas _canvas = new();
    private readonly ScaleTransform _zoom = new(2, 2);
    private readonly Rectangle _pick = new() { Stroke = Brushes.Red, StrokeThickness = 0.6, Fill = new SolidColorBrush(Color.FromArgb(0x50, 0xFF, 0x30, 0x30)) };
    private readonly TextBlock _note = new() { Margin = new Thickness(8, 4, 8, 4) };
    private readonly CheckBox _land = new() { Content = "육지", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 8, 0) };
    private readonly ScrollViewer _scroll;

    private Point? _from;
    private Area? _area;

    private DiscoveryAreaPicker(Map map, Area? start)
    {
        _map = map;
        Title = "발견물 자리 고르기 — 끌어서 범위를 잡습니다";
        Width = 1300;
        Height = 820;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        // 캔버스는 칸 좌표 그대로 둔다(지도 그림을 칸 4배로 늘린다) — 확대는 LayoutTransform 하나로.
        var sheet = new Image { Source = map.Chart, Width = MapCellsW, Height = MapCellsH, Stretch = Stretch.Fill };
        RenderOptions.SetBitmapScalingMode(sheet, BitmapScalingMode.NearestNeighbor);
        _canvas.Width = MapCellsW;
        _canvas.Height = MapCellsH;
        _canvas.Background = Brushes.Transparent;
        _canvas.Children.Add(sheet);
        foreach (var (x1, y1, x2, y2, name) in map.Others)
        {
            var mark = new Rectangle
            {
                Width = Math.Max(1, x2 - x1 + 1), Height = Math.Max(1, y2 - y1 + 1),
                Stroke = Brushes.Navy, StrokeThickness = 0.4, Fill = new SolidColorBrush(Color.FromArgb(0x30, 0x20, 0x40, 0xFF)),
                ToolTip = name,
            };
            Canvas.SetLeft(mark, x1);
            Canvas.SetTop(mark, y1);
            _canvas.Children.Add(mark);
        }
        _pick.IsHitTestVisible = false;
        _pick.Visibility = Visibility.Collapsed;
        _canvas.Children.Add(_pick);

        _scroll = new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = new Border { Child = _canvas, LayoutTransform = _zoom },
        };

        _canvas.MouseLeftButtonDown += (_, e) =>
        {
            _from = Snap(e.GetPosition(_canvas));
            _canvas.CaptureMouse();
            Draw(_from.Value, _from.Value);
            e.Handled = true;
        };
        _canvas.MouseMove += (_, e) =>
        {
            var at = Snap(e.GetPosition(_canvas));
            if (_from is { } from && e.LeftButton == MouseButtonState.Pressed) Draw(from, at);
            else _note.Text = $"칸 ({at.X}, {at.Y})" + (_area is { } a ? $"   ·   고른 범위 ({a.X1}, {a.Y1}) ~ ({a.X2}, {a.Y2})" : "");
        };
        _canvas.MouseLeftButtonUp += (_, e) =>
        {
            if (_from is not { } from) return;
            _canvas.ReleaseMouseCapture();
            _from = null;
            Draw(from, Snap(e.GetPosition(_canvas)));
            if (_area is { } a)
            {
                bool land = MostlyLand(a);
                _land.IsChecked = land;
                _area = a with { Land = land };
                _note.Text = $"고른 범위 ({a.X1}, {a.Y1}) ~ ({a.X2}, {a.Y2}) — {a.X2 - a.X1 + 1}x{a.Y2 - a.Y1 + 1}칸, {(land ? "육지" : "바다")}";
            }
        };
        // 휠은 확대 · 축소 — 마우스가 있던 자리가 그대로 남게 스크롤을 맞춘다.
        _scroll.PreviewMouseWheel += (_, e) =>
        {
            var at = e.GetPosition(_canvas);
            double scale = Math.Clamp(_zoom.ScaleX * (e.Delta > 0 ? 1.25 : 0.8), 0.5, 12);
            _zoom.ScaleX = _zoom.ScaleY = scale;
            _scroll.UpdateLayout();
            var view = e.GetPosition(_scroll);
            _scroll.ScrollToHorizontalOffset(at.X * scale - view.X);
            _scroll.ScrollToVerticalOffset(at.Y * scale - view.Y);
            e.Handled = true;
        };
        _land.Checked += (_, _) => { if (_area is { } a) _area = a with { Land = true }; };
        _land.Unchecked += (_, _) => { if (_area is { } a) _area = a with { Land = false }; };

        var ok = new Button { Content = "이 자리로", Padding = new Thickness(14, 2, 14, 2), Margin = new Thickness(4) };
        ok.Click += (_, _) =>
        {
            if (_area == null) { _note.Text = "먼저 지도 위를 끌어 범위를 잡아 주세요."; return; }
            DialogResult = true;
        };
        var cancel = new Button { Content = "그만두기", Padding = new Thickness(14, 2, 14, 2), Margin = new Thickness(4) };
        cancel.Click += (_, _) => DialogResult = false;
        var bar = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Children = { _land, ok, cancel, new TextBlock { Text = "휠: 확대 · 축소   왼쪽 단추로 끌기: 범위 잡기   파란 칸: 다른 발견물", VerticalAlignment = VerticalAlignment.Center, Foreground = Brushes.DimGray, Margin = new Thickness(12, 0, 0, 0) } },
        };

        var page = new DockPanel();
        DockPanel.SetDock(bar, Dock.Bottom);
        DockPanel.SetDock(_note, Dock.Bottom);
        page.Children.Add(bar);
        page.Children.Add(_note);
        page.Children.Add(_scroll);
        Content = page;

        if (start is { X1: >= 0, Y1: >= 0 } s)
        {
            _area = s;
            _land.IsChecked = s.Land;
            Draw(new Point(s.X1, s.Y1), new Point(s.X2, s.Y2));
            // 고른 자리가 보이게 크게 해서 가운데로.
            Loaded += (_, _) =>
            {
                _zoom.ScaleX = _zoom.ScaleY = 6;
                _scroll.UpdateLayout();
                _scroll.ScrollToHorizontalOffset((s.X1 + s.X2) / 2.0 * 6 - _scroll.ViewportWidth / 2);
                _scroll.ScrollToVerticalOffset((s.Y1 + s.Y2) / 2.0 * 6 - _scroll.ViewportHeight / 2);
            };
        }
    }

    private static Point Snap(Point p) =>
        new(Math.Clamp((int)p.X, 0, MapCellsW - 1), Math.Clamp((int)p.Y, 0, MapCellsH - 1));

    private void Draw(Point a, Point b)
    {
        int x1 = (int)Math.Min(a.X, b.X), y1 = (int)Math.Min(a.Y, b.Y), x2 = (int)Math.Max(a.X, b.X), y2 = (int)Math.Max(a.Y, b.Y);
        Canvas.SetLeft(_pick, x1);
        Canvas.SetTop(_pick, y1);
        _pick.Width = x2 - x1 + 1;
        _pick.Height = y2 - y1 + 1;
        _pick.Visibility = Visibility.Visible;
        _area = new Area(x1, y1, x2, y2, _land.IsChecked == true);
        _note.Text = $"범위 ({x1}, {y1}) ~ ({x2}, {y2}) — {x2 - x1 + 1}x{y2 - y1 + 1}칸";
    }

    /// <summary>범위 칸 가운데 육지가 바다보다 많은지 — 넓으면 듬성듬성 센다.</summary>
    private bool MostlyLand(Area a)
    {
        int step = Math.Max(1, Math.Max(a.X2 - a.X1, a.Y2 - a.Y1) / 32);
        int land = 0, sea = 0;
        for (int y = a.Y1; y <= a.Y2; y += step)
            for (int x = a.X1; x <= a.X2; x += step)
                if (_map.IsLand(x, y) is { } l) { if (l) land++; else sea++; }
        return land > sea;
    }

    /// <summary>창을 띄워 범위를 고르게 한다. 그만두면 null.</summary>
    public static Area? Pick(Window owner, Map map, Area? start)
    {
        var window = new DiscoveryAreaPicker(map, start) { Owner = owner };
        return window.ShowDialog() == true ? window._area : null;
    }
}
