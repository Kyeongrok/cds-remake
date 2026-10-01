using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CdsHelper.Game.Local.Helpers;
using CdsHelper.Support.Local.Helpers;
using CdsHelper.Support.Local.Settings;

namespace CdsHelper.Main.UI.Views;

/// <summary>
/// WORLD.CDS 편집 — 세계지도 칸을 붓으로 칠한다.
/// </summary>
/// <remarks>
/// 칸 하나는 2바이트다. 아래 14비트가 OCEAN.CDS 타일 번호, <c>0x4000</c> 이 뭍 비트다(<c>WorldCells</c> 참고).
/// 파일은 2500바이트 행이 2500줄이고 짝수 행이 지도 왼쪽 절반, 홀수 행이 오른쪽 절반이다.
///
/// <b>원본 게임 폴더의 WORLD.CDS 는 건드리지 않는다.</b> 고친 것은 <c>asset/cds/WORLD.CDS</c>
/// (<see cref="CdsAssetPath.EditedSaveDirectory"/>)에 적고, 놀이·편집기는 그것이 있으면 먼저 읽는다
/// (<see cref="CdsAssetPath.Resolve"/>). 저장소에서 고치면 릴리즈에도 같이 실린다.
///
/// 왼쪽 단추로 칠하고, 오른쪽 단추로 그 칸 값을 집는다(스포이드). 가운데 단추로 끌면 화면을 옮기고
/// 휠로 키운다. Ctrl+Z · Ctrl+Y 로 한 번 칠한 것씩 되돌린다.
/// </remarks>
public class WorldEditContent : ContentControl
{
    private const int W = 2500, H = 1250, Half = W / 2, Stride = 2500;
    private const int FileSize = Stride * H * 2;
    private static readonly int[] Zooms = [1, 2, 3, 4, 6, 8, 12, 16, 24, 32, 48, 64];
    private static readonly int[] Brushes_ = [1, 3, 5, 9, 15];

    private byte[]? _world;
    private int[]? _avg;
    private OceanTiles? _ocean;
    private WriteableBitmap? _bitmap;

    private readonly Image _image = new() { Stretch = Stretch.None, Cursor = Cursors.Pen };

    /// <summary>
    /// 확대했을 때 보이는 자리만 <b>타일 그림 그대로</b> 덮는 판 — 바탕 그림은 칸마다 평균색 한 점이라
    /// 키우면 뭉개져 해안·도시 모양이 안 보인다. 배율 z 면 칸마다 z x z 로 줄인 타일을 찍어 화면 점과 1:1 이 된다.
    /// </summary>
    private readonly Image _detail = new() { Stretch = Stretch.Fill, IsHitTestVisible = false };
    private readonly Canvas _overlay = new() { IsHitTestVisible = false, Width = 2500, Height = 1250 };
    private readonly Grid _host = new();
    private readonly ScaleTransform _scale = new(1, 1);
    private readonly ScrollViewer _scroll = new()
    {
        HorizontalScrollBarVisibility = ScrollBarVisibility.Visible,
        VerticalScrollBarVisibility = ScrollBarVisibility.Visible,
        Background = Brushes.Black,
    };
    private readonly TextBlock _status = new() { Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _hover = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 8) };
    private readonly TextBlock _zoomText = new() { Width = 40, TextAlignment = TextAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
    private readonly ComboBox _brush = new() { Width = 70, Margin = new Thickness(4, 0, 0, 0) };
    private readonly TextBox _tileBox = new() { Width = 70 };
    private readonly CheckBox _landBox = new() { Content = "뭍 (0x4000)", Margin = new Thickness(0, 6, 0, 0) };
    private readonly TextBlock _valueText = new() { Margin = new Thickness(0, 6, 0, 0), FontFamily = new FontFamily("Consolas") };
    private readonly Image _tilePreview = new() { Width = 96, Height = 96, Stretch = Stretch.Fill };
    private readonly Image _hoverPreview = new() { Width = 96, Height = 96, Stretch = Stretch.Fill };

    private int _zoom;
    private ushort _paint = 0x0880;   // 대서양 한가운데 바다 칸
    private bool _syncing;

    // 칠하기 — 한 번 누르고 뗄 때까지가 한 획이다. 칸 자리(파일 오프셋)마다 처음 값과 새 값.
    private Dictionary<int, (ushort Old, ushort New)>? _stroke;
    private readonly Stack<Dictionary<int, (ushort Old, ushort New)>> _undo = new();
    private readonly Stack<Dictionary<int, (ushort Old, ushort New)>> _redo = new();
    private int _unsaved;

    private Point? _panFrom;
    private double _panH, _panV;

    /// <summary>손바닥 — 켜 두면 왼쪽 단추로 끌어 화면을 옮기고 칠하지 않는다.</summary>
    private bool _hand;

    /// <summary>
    /// 도시 숨김 — 도시(3x3)·발견물(2x2) 칸을 그 바탕 타일(도시 표 <c>+0x74</c> · 발견물 표 <c>+0x54</c>)로 보이고 칠하지 않는다.
    /// 그 칸의 파일 값은 도시 그림 타일이고 바탕은 표 쪽 자료라, 칠하면 도시 그림이 깨진다. 게임의 「도시 분리」와 같은 바탕이다.
    /// </summary>
    private bool _hideCities = true;
    private readonly System.Windows.Controls.Primitives.ToggleButton _hideButton = new()
    {
        Content = "도시 숨김", IsChecked = true, Padding = new Thickness(6, 0, 6, 0), Margin = new Thickness(2, 0, 2, 0),
        ToolTip = "도시·발견물 칸을 바탕 지형으로 보이고 칠하지 않게 막는다 (C)",
    };

    /// <summary>도시·발견물 칸 → 그 바탕 타일과 누구 칸인지. 칸 번호는 y*2500+x.</summary>
    private readonly Dictionary<int, (ushort Under, string Owner)> _covered = [];

    /// <summary>칸 격자 — 키웠을 때(x4 위) 칸 경계를 어둡게 긋는다.</summary>
    private bool _grid = true;
    private readonly System.Windows.Controls.Primitives.ToggleButton _gridButton = new()
    {
        Content = "격자", IsChecked = true, Padding = new Thickness(6, 0, 6, 0), Margin = new Thickness(2, 0, 2, 0),
        ToolTip = "칸 경계선 (G) — x4 위로 키웠을 때 보인다",
    };
    private readonly System.Windows.Controls.Primitives.ToggleButton _handButton = new()
    {
        Content = "✋", FontSize = 16, Width = 34, Margin = new Thickness(0, 0, 2, 0),
        ToolTip = "손바닥 (H) — 켜면 왼쪽 단추로 끌어 지도를 옮긴다. 끄면 칠하기 (B)",
    };

    public WorldEditContent()
    {
        Focusable = true;
        RenderOptions.SetBitmapScalingMode(_image, BitmapScalingMode.NearestNeighbor);
        RenderOptions.SetBitmapScalingMode(_tilePreview, BitmapScalingMode.NearestNeighbor);
        RenderOptions.SetBitmapScalingMode(_hoverPreview, BitmapScalingMode.NearestNeighbor);
        RenderOptions.SetBitmapScalingMode(_detail, BitmapScalingMode.NearestNeighbor);
        _overlay.Children.Add(_detail);
        _host.Children.Add(_image);
        _host.Children.Add(_overlay);
        _host.LayoutTransform = _scale;
        _scroll.Content = _host;
        _scroll.ScrollChanged += (_, _) => RefreshDetail();

        foreach (int b in Brushes_) _brush.Items.Add($"{b}x{b}");
        _brush.SelectedIndex = 0;

        var root = new DockPanel();
        var bar = Toolbar();
        DockPanel.SetDock(bar, Dock.Top);
        root.Children.Add(bar);
        var side = SidePanel();
        DockPanel.SetDock(side, Dock.Right);
        root.Children.Add(side);
        root.Children.Add(_scroll);
        Content = root;

        _image.MouseLeftButtonDown += (_, e) =>
        {
            if (_hand) { StartPan(e.GetPosition(_scroll)); e.Handled = true; return; }
            _image.CaptureMouse(); BeginStroke(); PaintAt(e); e.Handled = true;
        };
        _image.MouseLeftButtonUp += (_, _) => { EndStroke(); _image.ReleaseMouseCapture(); };
        _image.MouseRightButtonDown += (_, e) => { if (CellAt(e) is { } c) Pick(Read(c.X, c.Y)); e.Handled = true; };
        _image.MouseMove += (_, e) =>
        {
            ShowHover(e);
            if (e.LeftButton == MouseButtonState.Pressed && _stroke != null) PaintAt(e);
        };
        _scroll.PreviewMouseDown += (_, e) =>
        {
            if (e.ChangedButton != MouseButton.Middle) return;
            StartPan(e.GetPosition(_scroll)); e.Handled = true;
        };
        _scroll.PreviewMouseMove += (_, e) =>
        {
            if (_panFrom is not { } from) return;
            var now = e.GetPosition(_scroll);
            _scroll.ScrollToHorizontalOffset(_panH - (now.X - from.X));
            _scroll.ScrollToVerticalOffset(_panV - (now.Y - from.Y));
        };
        _scroll.PreviewMouseUp += (_, e) =>
        {
            if (_panFrom == null || (e.ChangedButton != MouseButton.Middle && e.ChangedButton != MouseButton.Left)) return;
            _panFrom = null; _scroll.ReleaseMouseCapture();
            _image.Cursor = _hand ? Cursors.Hand : Cursors.Pen;
        };
        _scroll.PreviewMouseWheel += (_, e) => { ZoomAt(e.Delta > 0 ? 1 : -1, e.GetPosition(_image)); e.Handled = true; };

        KeyDown += (_, e) =>
        {
            if (Keyboard.Modifiers == ModifierKeys.None && Keyboard.FocusedElement is not TextBox)
            {
                if (e.Key == Key.H) { _handButton.IsChecked = true; e.Handled = true; return; }
                if (e.Key == Key.B) { _handButton.IsChecked = false; e.Handled = true; return; }
                if (e.Key == Key.G) { _gridButton.IsChecked = !_gridButton.IsChecked; e.Handled = true; return; }
                if (e.Key == Key.C) { _hideButton.IsChecked = !_hideButton.IsChecked; e.Handled = true; return; }
            }
            if (Keyboard.Modifiers != ModifierKeys.Control) return;
            if (e.Key == Key.Z) { Undo(); e.Handled = true; }
            else if (e.Key == Key.Y) { Redo(); e.Handled = true; }
            else if (e.Key == Key.S) { Save(); e.Handled = true; }
        };
        _tileBox.LostFocus += (_, _) => FromBoxes();
        _tileBox.KeyDown += (_, e) => { if (e.Key == Key.Enter) FromBoxes(); };
        _landBox.Click += (_, _) => FromBoxes();

        Loaded += (_, _) => { if (_world == null) Load(); Focus(); };
    }

    // ── 화면 짜기 ──────────────────────────────────────────────────────────

    private FrameworkElement Toolbar()
    {
        var bar = new WrapPanel { Margin = new Thickness(6) };
        _handButton.Checked += (_, _) => SetHand(true);
        _handButton.Unchecked += (_, _) => SetHand(false);
        bar.Children.Add(_handButton);
        _gridButton.Checked += (_, _) => { _grid = true; RefreshDetail(); };
        _gridButton.Unchecked += (_, _) => { _grid = false; RefreshDetail(); };
        bar.Children.Add(Btn("다시 읽기", Load, "저장한 것(없으면 원본)을 다시 읽는다 — 저장 안 한 칠은 버린다"));
        bar.Children.Add(Btn("저장 (Ctrl+S)", Save, "asset/cds/WORLD.CDS 에 적는다. 원본 게임 폴더는 안 건드린다"));
        bar.Children.Add(Btn("원본으로", Restore, "고친 WORLD.CDS 를 지우고 원본으로 돌아간다"));
        bar.Children.Add(Gap());
        bar.Children.Add(new TextBlock { Text = "붓", VerticalAlignment = VerticalAlignment.Center });
        bar.Children.Add(_brush);
        bar.Children.Add(Gap());
        bar.Children.Add(Btn("−", () => ZoomAt(-1, null), "줄이기 (휠)"));
        bar.Children.Add(_zoomText);
        bar.Children.Add(Btn("+", () => ZoomAt(1, null), "키우기 (휠)"));
        bar.Children.Add(_gridButton);
        bar.Children.Add(_hideButton);
        _hideButton.Checked += (_, _) => { _hideCities = true; Redraw(); };
        _hideButton.Unchecked += (_, _) => { _hideCities = false; Redraw(); };
        bar.Children.Add(Gap());
        bar.Children.Add(Btn("되돌리기", Undo, "Ctrl+Z"));
        bar.Children.Add(Btn("다시하기", Redo, "Ctrl+Y"));
        bar.Children.Add(_status);
        return bar;

        static FrameworkElement Gap() => new Border { Width = 12 };
    }

    private static Button Btn(string text, Action run, string tip)
    {
        var b = new Button { Content = text, Padding = new Thickness(8, 2, 8, 2), Margin = new Thickness(2, 0, 2, 0), ToolTip = tip };
        b.Click += (_, _) => run();
        return b;
    }

    private FrameworkElement SidePanel()
    {
        var panel = new StackPanel { Width = 210, Margin = new Thickness(8) };
        panel.Children.Add(new TextBlock { Text = "칠할 칸", FontWeight = FontWeights.Bold });
        var pickFrame = new Border
        {
            BorderBrush = Brushes.Gray, BorderThickness = new Thickness(1), HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 4, 0, 4), Child = _tilePreview, Cursor = Cursors.Hand,
            ToolTip = "눌러서 타일 목록에서 고르기",
        };
        pickFrame.MouseLeftButtonUp += (_, _) => OpenTilePicker();
        panel.Children.Add(pickFrame);
        panel.Children.Add(Btn("타일 목록에서 고르기…", OpenTilePicker, "지도에 쓰인 타일을 4열로 늘어놓고 골라 칠할 칸으로 삼는다"));
        var tileRow = new StackPanel { Orientation = Orientation.Horizontal };
        tileRow.Children.Add(new TextBlock { Text = "타일 번호 ", VerticalAlignment = VerticalAlignment.Center });
        tileRow.Children.Add(_tileBox);
        panel.Children.Add(tileRow);
        panel.Children.Add(_landBox);
        panel.Children.Add(_valueText);

        panel.Children.Add(new TextBlock { Text = "마우스 아래 칸", FontWeight = FontWeights.Bold, Margin = new Thickness(0, 16, 0, 0) });
        panel.Children.Add(new Border { BorderBrush = Brushes.Gray, BorderThickness = new Thickness(1), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 4, 0, 0), Child = _hoverPreview });
        panel.Children.Add(_hover);

        panel.Children.Add(new TextBlock
        {
            Text = "왼쪽 위 ✋ (H): 끌어서 옮기기, 끄면(B) 칠하기\n왼쪽 단추: 칠하기\n오른쪽 단추: 그 칸 집기(스포이드)\n가운데 단추 끌기: 화면 옮기기\n휠: 키우기·줄이기\nCtrl+Z / Ctrl+Y: 되돌리기 / 다시하기\n\n" +
                   "저장하면 asset/cds/WORLD.CDS 에 적고, 놀이와 편집기가 그것을 먼저 읽는다. 놀이 창을 다시 열어야 먹는다.",
            TextWrapping = TextWrapping.Wrap,
            Foreground = Brushes.Gray,
            Margin = new Thickness(0, 16, 0, 0),
        });
        return new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    }

    // ── 읽기 · 쓰기 ────────────────────────────────────────────────────────

    private static string GameDirectory =>
        Path.GetDirectoryName(AppSettings.LastSaveFilePath) is { Length: > 0 } dir ? dir : "";

    private void Load()
    {
        string path = CdsAssetPath.Resolve(GameDirectory, "WORLD.CDS");
        if (!File.Exists(path) || new FileInfo(path).Length != FileSize)
        {
            _status.Text = "WORLD.CDS 를 찾지 못했습니다 — 세이브를 한 번 열어 게임 폴더를 알려 주세요";
            return;
        }
        _world = File.ReadAllBytes(path);
        _ocean ??= OceanTiles.LoadFromDirectory(GameDirectory);
        _avg = _ocean?.GetAverages(1);
        _undo.Clear(); _redo.Clear(); _unsaved = 0;
        LoadCovered();

        _bitmap = new WriteableBitmap(W, H, 96, 96, PixelFormats.Bgr32, null);
        _image.Source = _bitmap;
        _image.Width = W; _image.Height = H;
        Redraw();
        Pick(_paint);

        bool edited = CdsAssetPath.Edited("WORLD.CDS") != null;
        _status.Text = (edited ? "고친 판: " : "원본: ") + path;
    }

    /// <summary>도시·발견물 블록이 덮는 칸을 모은다(<see cref="_covered"/>).</summary>
    private void LoadCovered()
    {
        _covered.Clear();
        void Lay(int x0, int y0, ushort[] block, int side, string owner)
        {
            for (int k = 0; k < block.Length; k++)
            {
                if (block[k] == CityExeTable.Keep) continue;
                int x = ((x0 + k % side) % W + W) % W, y = y0 + k / side;
                if (y >= 0 && y < H) _covered[y * W + x] = ((ushort)(block[k] & OceanTiles.TileMask), owner);
            }
        }
        if (CityExeTable.Open(GameDirectory) is { } cities)
        {
            var names = CityTable.Open();
            for (int id = 0; id < GameMapCoords.CityCount; id++)
                if (cities.TryCell(id, out int cx, out int cy, out _))
                    Lay(cx, cy, cities.EraseOf(id), CityExeTable.EraseWidth, $"도시 {names.NameOf(id)}");
        }
        if (DiscoveryTable.Open(GameDirectory) is { } finds)
            foreach (var row in finds.Discoveries)
                if (row.HasPlace && row.Erase is { Length: DiscoveryTable.EraseWidth * DiscoveryTable.EraseWidth } erase)
                    Lay(row.X1, row.Y1, erase, DiscoveryTable.EraseWidth, $"발견물 {row.Name}");
    }

    /// <summary>그 칸을 화면에 무엇으로 보일지 — 도시 숨김이면 도시·발견물 칸은 바탕 타일이다.</summary>
    private ushort Shown(int x, int y) =>
        _hideCities && _covered.TryGetValue(y * W + x, out var c) ? c.Under : Read(x, y);

    /// <summary>바탕 그림을 통째로 다시 칠한다 — 도시 숨김을 켜고 끌 때.</summary>
    private void Redraw()
    {
        if (_world == null || _bitmap == null) return;
        var pixels = new int[W * H];
        for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
                pixels[y * W + x] = ColorOf(Shown(x, y));
        _bitmap.WritePixels(new Int32Rect(0, 0, W, H), pixels, W * 4, 0);
        ApplyZoom();
    }

    private void Save()
    {
        if (_world == null) return;
        try
        {
            string dir = CdsAssetPath.EditedSaveDirectory();
            string target = Path.Combine(dir, "WORLD.CDS");
            string temp = target + ".part";
            File.WriteAllBytes(temp, _world);
            File.Move(temp, target, overwrite: true);
            _unsaved = 0;
            _status.Text = $"저장했습니다: {target}";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            MessageBox.Show($"저장하지 못했습니다:\n{ex.Message}", "WORLD.CDS 편집", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void Restore()
    {
        if (CdsAssetPath.Edited("WORLD.CDS") == null && _unsaved == 0) { _status.Text = "이미 원본입니다"; return; }
        if (MessageBox.Show("고친 WORLD.CDS 를 지우고 원본으로 돌아갈까요?\n저장 안 한 칠도 버립니다.", "WORLD.CDS 편집",
                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        // 고친 판은 소스 옆 · %APPDATA% · 실행 폴더 복사본에 있을 수 있다 — 모두 지운다.
        while (CdsAssetPath.Edited("WORLD.CDS") is { } edited)
        {
            try { File.Delete(edited); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                MessageBox.Show($"지우지 못했습니다:\n{ex.Message}", "WORLD.CDS 편집", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }
        }
        Load();
    }

    // ── 칸 ─────────────────────────────────────────────────────────────────

    /// <summary>칸 (x, y)가 파일 안에서 놓인 자리 — 왼쪽 절반은 짝수 행, 오른쪽 절반은 홀수 행이다.</summary>
    private static int Offset(int x, int y) =>
        x < Half ? (2 * y) * Stride + x * 2 : (2 * y + 1) * Stride + (x - Half) * 2;

    private ushort Read(int x, int y)
    {
        int at = Offset(x, y);
        return (ushort)(_world![at] | (_world[at + 1] << 8));
    }

    private int ColorOf(ushort value)
    {
        int tile = value & OceanTiles.TileMask;
        if (_avg != null) return _avg[tile];
        return (value & 0x4000) != 0 ? 0x8B6B3A : 0x2850A0;
    }

    private (int X, int Y)? CellAt(MouseEventArgs e)
    {
        var p = e.GetPosition(_image);
        int x = (int)p.X, y = (int)p.Y;
        return x >= 0 && x < W && y >= 0 && y < H ? (x, y) : null;
    }

    // ── 칠하기 ─────────────────────────────────────────────────────────────

    private void BeginStroke()
    {
        if (_world == null) return;
        _stroke = [];
        Focus();
    }

    private void PaintAt(MouseEventArgs e)
    {
        if (_world == null || _bitmap == null || _stroke == null || CellAt(e) is not { } c) return;
        int size = Brushes_[Math.Max(0, _brush.SelectedIndex)];
        int r = size / 2;
        _bitmap.Lock();
        try
        {
            for (int y = c.Y - r; y <= c.Y + r; y++)
                for (int x = c.X - r; x <= c.X + r; x++)
                {
                    if (x < 0 || x >= W || y < 0 || y >= H) continue;
                    if (_hideCities && _covered.ContainsKey(y * W + x)) { _blocked = true; continue; }   // 도시 그림 칸은 칠하지 않는다
                    Write(x, y, _paint, _stroke);
                }
            int left = Math.Max(0, c.X - r), top = Math.Max(0, c.Y - r);
            _bitmap.AddDirtyRect(new Int32Rect(left, top, Math.Min(W, c.X + r + 1) - left, Math.Min(H, c.Y + r + 1) - top));
        }
        finally { _bitmap.Unlock(); }
        RefreshDetail();
    }

    /// <summary>한 칸을 적고 그림도 고친다. 비트맵은 잠긴 채여야 한다.</summary>
    private void Write(int x, int y, ushort value, Dictionary<int, (ushort Old, ushort New)>? log)
    {
        int at = Offset(x, y);
        ushort old = (ushort)(_world![at] | (_world[at + 1] << 8));
        if (old == value) return;
        _world[at] = (byte)value;
        _world[at + 1] = (byte)(value >> 8);
        if (log != null)
            log[at] = log.TryGetValue(at, out var was) ? (was.Old, value) : (old, value);
        System.Runtime.InteropServices.Marshal.WriteInt32(
            _bitmap!.BackBuffer, y * _bitmap.BackBufferStride + x * 4, ColorOf(Shown(x, y)));
    }

    /// <summary>이번 획에서 도시·발견물 칸을 건너뛰었는지 — 끝나면 한 번 알린다.</summary>
    private bool _blocked;

    private void EndStroke()
    {
        if (_blocked) _status.Text = "도시·발견물 칸은 칠하지 않습니다 — 「도시 숨김」을 끄면 원본 칸을 직접 고칠 수 있습니다";
        _blocked = false;
        if (_stroke is { Count: > 0 })
        {
            _undo.Push(_stroke);
            _redo.Clear();
            _unsaved += _stroke.Count;
            _status.Text = $"저장 안 한 칸 {_unsaved}개";
        }
        _stroke = null;
    }

    private void Undo() => Replay(_undo, _redo, useOld: true);
    private void Redo() => Replay(_redo, _undo, useOld: false);

    private void Replay(Stack<Dictionary<int, (ushort Old, ushort New)>> from,
                        Stack<Dictionary<int, (ushort Old, ushort New)>> to, bool useOld)
    {
        if (_world == null || _bitmap == null || from.Count == 0) return;
        var stroke = from.Pop();
        _bitmap.Lock();
        try
        {
            foreach (var (at, v) in stroke)
            {
                int row = at / Stride, col = at % Stride / 2;
                int y = row / 2, x = row % 2 == 0 ? col : col + Half;
                Write(x, y, useOld ? v.Old : v.New, null);
                _bitmap.AddDirtyRect(new Int32Rect(x, y, 1, 1));
            }
        }
        finally { _bitmap.Unlock(); }
        RefreshDetail();
        to.Push(stroke);
        _unsaved += useOld ? -stroke.Count : stroke.Count;
        _status.Text = $"저장 안 한 칸 {Math.Max(0, _unsaved)}개";
    }

    // ── 고른 칸 ────────────────────────────────────────────────────────────

    private void Pick(ushort value)
    {
        _paint = value;
        _syncing = true;
        _tileBox.Text = (value & OceanTiles.TileMask).ToString();
        _landBox.IsChecked = (value & 0x4000) != 0;
        _syncing = false;
        _valueText.Text = $"값 0x{value:X4}";
        _tilePreview.Source = TileImage(value & OceanTiles.TileMask);
    }

    private void FromBoxes()
    {
        if (_syncing) return;
        if (!int.TryParse(_tileBox.Text, out int tile) || tile < 0 || tile > OceanTiles.TileMask)
        {
            _tileBox.Text = (_paint & OceanTiles.TileMask).ToString();
            return;
        }
        // 0x8000 비트는 그대로 둔다 — 뜻을 모르니 집은 값의 것을 잇는다.
        ushort value = (ushort)((_paint & 0x8000) | tile | (_landBox.IsChecked == true ? 0x4000 : 0));
        Pick(value);
    }

    private ImageSource? TileImage(int tile)
    {
        if (_ocean == null) return null;
        var data = _ocean.TileData;
        var rgb = _ocean.PaletteRgb;
        var pixels = new int[OceanTiles.TilePixels];
        int at = tile * OceanTiles.TilePixels;
        for (int i = 0; i < pixels.Length; i++) pixels[i] = rgb[data[at + i]];
        var bmp = BitmapSource.Create(OceanTiles.TileW, OceanTiles.TileW, 96, 96, PixelFormats.Bgr32, null,
                                      pixels, OceanTiles.TileW * 4);
        bmp.Freeze();
        return bmp;
    }

    /// <summary>타일 고르기 창의 갈래 — 지형표 부류(0x004CD048)와 그림 비트(0x8000)로 가른다.</summary>
    private static readonly (string Name, string Tip)[] Kinds =
    [
        ("바다", "부류 1 — 먼바다"),
        ("해안", "부류 0 — 뭍에 붙은 얕은 물. 배가 지난다"),
        ("평지", "부류 2 — 초지·평원. 극지 얼음도 여기 든다"),
        ("산", "부류 3 — 말이 가장 더디게 지난다"),
        ("강", "부류 5 — 뭍 위에 흩어진 물길 모양 타일(추정). 바다처럼 빨리 지난다"),
        ("사막", "부류 4"),
        ("숲", "부류 6 — 숲·정글"),
        ("도시·그림", "그림 비트(0x8000)가 선 칸 — 도시·발견물 그림 조각. 이름은 가장 가까운 도시다"),
        ("기타", "위에 안 드는 부류"),
    ];

    private static int KindOf(int terrainClass, bool picture) =>
        picture ? 7 : terrainClass switch { 1 => 0, 0 => 1, 2 => 2, 3 => 3, 5 => 4, 4 => 5, 6 => 6, _ => 8 };

    private TerrainTable? _terrain;
    private (string Name, double X, double Y)[]? _cities;

    /// <summary>도시 이름과 칸 자리 — 타일 이름을 「리스본 부근」처럼 달 때 쓴다.</summary>
    private (string Name, double X, double Y)[] Cities()
    {
        if (_cities != null) return _cities;
        var names = new Dictionary<int, string>();
        try
        {
            for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
            {
                string path = Path.Combine(dir.FullName, "exe-tables", "도시표.json");
                if (!File.Exists(path)) continue;
                using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(path));
                foreach (var c in doc.RootElement.GetProperty("Data").GetProperty("Cities").EnumerateArray())
                    names[c.GetProperty("Id").GetInt32()] = c.GetProperty("Name").GetString() ?? "";
                break;
            }
        }
        catch (Exception ex) when (ex is IOException or System.Text.Json.JsonException or KeyNotFoundException) { }

        var list = new List<(string, double, double)>();
        for (int id = 0; id < GameMapCoords.CityCount; id++)
            if (GameMapCoords.TryCityCell(id, out double x, out double y))
                list.Add((names.TryGetValue(id, out var n) && n.Length > 0 ? n : $"도시 {id}", x, y));
        return _cities = [.. list];
    }

    private string NearestCity(double x, double y)
    {
        string best = "";
        double bestD = double.MaxValue;
        foreach (var (name, cx, cy) in Cities())
        {
            double dx = Math.Abs(cx - x); dx = Math.Min(dx, W - dx);
            double d = dx * dx + (cy - y) * (cy - y);
            if (d < bestD) { bestD = d; best = name; }
        }
        return best;
    }

    /// <summary>고르기 창에 늘어놓을 타일 하나.</summary>
    private sealed record TileInfo(int Tile, ushort Common, int Uses, int Kind, string Label);

    /// <summary>
    /// 지도에 쓰인 타일을 훑어 갈래·이름을 단다. 이름은 갈래 안 차례 번호에, 쓰인 자리가 한 곳에 몰려 있으면
    /// 가장 가까운 도시를 붙인다(「산 120 · 마드리드 부근」). 도시·그림 타일은 도시 이름에 번호다(「리스본 3」).
    /// </summary>
    private List<TileInfo> SurveyTiles()
    {
        _terrain ??= TerrainTable.Open(GameDirectory);
        var values = new Dictionary<int, Dictionary<ushort, int>>();
        var sums = new Dictionary<int, (double X, double Y, double XX, double YY, int N, bool Pic)>();
        for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
            {
                ushort v = Shown(x, y);   // 도시 숨김이면 도시·발견물 칸은 바탕 타일로 센다 — 그림 타일이 목록에서 빠진다
                int tile = v & OceanTiles.TileMask;
                if (!values.TryGetValue(tile, out var byValue)) values[tile] = byValue = [];
                byValue[v] = byValue.GetValueOrDefault(v) + 1;
                var s = sums.GetValueOrDefault(tile);
                sums[tile] = (s.X + x, s.Y + y, s.XX + (double)x * x, s.YY + (double)y * y, s.N + 1, s.Pic || (v & 0x8000) != 0);
            }

        var result = new List<TileInfo>();
        var perKind = new int[Kinds.Length];
        var perCity = new Dictionary<string, int>();
        foreach (int tile in values.Keys.Order())
        {
            var s = sums[tile];
            double mx = s.X / s.N, my = s.Y / s.N;
            double spread = Math.Sqrt(Math.Max(0, s.XX / s.N - mx * mx + s.YY / s.N - my * my));
            int cls = _terrain?.ClassOfCell(tile) ?? ((values[tile].Keys.First() & 0x4000) != 0 ? 2 : 1);
            int kind = KindOf(cls, s.Pic);
            string label;
            if (kind == 7)
            {
                string city = NearestCity(mx, my);
                int n = perCity[city] = perCity.GetValueOrDefault(city) + 1;
                label = $"{city} {n}";
            }
            else
            {
                int n = ++perKind[kind];
                label = spread < 40 ? $"{Kinds[kind].Name} {n} · {NearestCity(mx, my)} 부근" : $"{Kinds[kind].Name} {n}";
            }
            result.Add(new TileInfo(tile, values[tile].MaxBy(kv => kv.Value).Key, s.N, kind, label));
        }
        return result;
    }

    /// <summary>
    /// 타일 고르기 창 — 지도에 <b>쓰인 타일만</b> 4열로 늘어놓고, 위 탭으로 바다·해안·평지·산·강·사막·숲·도시를 가른다.
    /// 고르면 그 타일이 지도에서 가장 많이 쓰인 값(뭍 비트까지)을 칠할 칸으로 삼는다.
    /// </summary>
    private void OpenTilePicker()
    {
        if (_world == null || _ocean == null) return;
        Cursor = Cursors.Wait;
        List<TileInfo> tiles;
        try { tiles = SurveyTiles(); }
        finally { Cursor = null; }

        const int Columns = 4, Cell = 64;
        var dialog = new Window
        {
            Title = $"타일 고르기 — 지도에 쓰인 타일 {tiles.Count}개",
            Owner = Window.GetWindow(this),
            Width = Columns * (Cell + 50) + 60,
            Height = 700,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false,
        };

        var find = new TextBox { Margin = new Thickness(0, 6, 0, 6), ToolTip = "번호나 이름(도시 이름 따위)으로 찾기" };
        var list = new ListBox { HorizontalContentAlignment = HorizontalAlignment.Left };
        VirtualizingPanel.SetIsVirtualizing(list, true);
        ScrollViewer.SetCanContentScroll(list, true);

        // 처음 탭은 지금 고른 타일의 갈래다.
        int mine = tiles.FindIndex(t => t.Tile == (_paint & OceanTiles.TileMask));
        int current = mine >= 0 ? tiles[mine].Kind : -1;

        var tabs = new WrapPanel();
        var tabButtons = new List<(System.Windows.Controls.Primitives.ToggleButton Button, int Kind)>();

        void Fill()
        {
            foreach (var (b, k) in tabButtons) b.IsChecked = k == current;
            string filter = find.Text.Trim();
            var shown = tiles.Where(t => (current < 0 || t.Kind == current)
                                         && (filter.Length == 0 || t.Tile.ToString().Contains(filter) || t.Label.Contains(filter)))
                             .ToList();
            list.Items.Clear();
            for (int i = 0; i < shown.Count; i += Columns)
            {
                var row = new StackPanel { Orientation = Orientation.Horizontal };
                foreach (var info in shown.Skip(i).Take(Columns))
                {
                    var img = new Image { Width = Cell, Height = Cell, Source = TileImage(info.Tile) };
                    RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.NearestNeighbor);
                    var cellPanel = new StackPanel { Margin = new Thickness(3), Width = Cell + 40 };
                    cellPanel.Children.Add(new Border
                    {
                        BorderThickness = new Thickness(2),
                        BorderBrush = (info.Common & 0x4000) != 0 ? Brushes.SaddleBrown : Brushes.SteelBlue,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        Child = img,
                    });
                    cellPanel.Children.Add(new TextBlock
                    {
                        Text = info.Label, FontSize = 11, TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap,
                    });
                    cellPanel.Children.Add(new TextBlock
                    {
                        Text = $"#{info.Tile}", FontSize = 10, Foreground = Brushes.Gray, HorizontalAlignment = HorizontalAlignment.Center,
                    });
                    var button = new Border
                    {
                        Child = cellPanel, Cursor = Cursors.Hand, Background = Brushes.Transparent,
                        ToolTip = $"{info.Label} · 타일 {info.Tile} · {((info.Common & 0x4000) != 0 ? "뭍" : "바다")} · 지도에 {info.Uses}칸",
                    };
                    button.MouseLeftButtonUp += (_, _) => { Pick(info.Common); dialog.Close(); };
                    row.Children.Add(button);
                }
                list.Items.Add(row);
            }
            int at = shown.FindIndex(t => t.Tile == (_paint & OceanTiles.TileMask));
            if (at >= 0 && list.Items.Count > 0) list.ScrollIntoView(list.Items[at / Columns]);
        }

        void AddTab(string text, int kind, string tip)
        {
            int count = kind < 0 ? tiles.Count : tiles.Count(t => t.Kind == kind);
            if (count == 0) return;
            var b = new System.Windows.Controls.Primitives.ToggleButton
            {
                Content = $"{text} {count}", Padding = new Thickness(8, 2, 8, 2), Margin = new Thickness(0, 0, 3, 3), ToolTip = tip,
            };
            b.Click += (_, _) => { current = kind; Fill(); };
            tabButtons.Add((b, kind));
            tabs.Children.Add(b);
        }
        AddTab("전체", -1, "지도에 쓰인 타일 전부");
        for (int k = 0; k < Kinds.Length; k++) AddTab(Kinds[k].Name, k, Kinds[k].Tip);

        find.TextChanged += (_, _) => Fill();

        var note = new TextBlock
        {
            Text = "테두리 갈색은 뭍, 파랑은 바다로 많이 쓰인 타일입니다. 이름의 번호는 갈래 안 차례이고, 한곳에 몰려 쓰인 타일에는 가까운 도시를 붙였습니다."
                   + " 고르면 그 타일이 지도에서 가장 많이 쓰인 값을 칠합니다.",
            TextWrapping = TextWrapping.Wrap, Foreground = Brushes.Gray, Margin = new Thickness(0, 6, 0, 0),
        };
        var head = new StackPanel();
        head.Children.Add(tabs);
        head.Children.Add(find);
        var root = new DockPanel { Margin = new Thickness(8) };
        DockPanel.SetDock(head, Dock.Top);
        DockPanel.SetDock(note, Dock.Bottom);
        root.Children.Add(head);
        root.Children.Add(note);
        root.Children.Add(list);
        dialog.Content = root;
        dialog.KeyDown += (_, e) => { if (e.Key == Key.Escape) dialog.Close(); };
        dialog.Loaded += (_, _) => Fill();
        dialog.ShowDialog();
    }

    private void ShowHover(MouseEventArgs e)
    {
        if (_world == null || CellAt(e) is not { } c) return;
        ushort v = Read(c.X, c.Y);
        var (lat, lon) = WorldMapRenderer.PixelToLatLon(c.X + 0.5, c.Y + 0.5);
        string owner = _covered.TryGetValue(c.Y * W + c.X, out var cov)
            ? $"\n{cov.Owner} 칸" + (_hideCities ? $" (바탕 타일 {cov.Under} 로 보임 · 칠 안 됨)" : "") : "";
        _hover.Text = $"칸 ({c.X}, {c.Y})\n" +
                      $"{Math.Abs(lat):0.0}°{(lat >= 0 ? "N" : "S")} {Math.Abs(lon):0.0}°{(lon >= 0 ? "E" : "W")}\n" +
                      $"값 0x{v:X4} · 타일 {v & OceanTiles.TileMask}\n" +
                      ((v & 0x4000) != 0 ? "뭍" : "바다") +
                      $" · 파일 0x{Offset(c.X, c.Y):X6}" + owner;
        _hoverPreview.Source = TileImage(v & OceanTiles.TileMask);
    }

    // ── 키우기 ─────────────────────────────────────────────────────────────

    private void ZoomAt(int by, Point? at)
    {
        int want = Math.Clamp(_zoom + by, 0, Zooms.Length - 1);
        if (want == _zoom) return;
        // 마우스 아래 자리가 그대로 남게 — 없으면 화면 가운데.
        var focus = at ?? new Point((_scroll.HorizontalOffset + _scroll.ViewportWidth / 2) / Zooms[_zoom],
                                    (_scroll.VerticalOffset + _scroll.ViewportHeight / 2) / Zooms[_zoom]);
        var screen = at is { } p
            ? new Point(p.X * Zooms[_zoom] - _scroll.HorizontalOffset, p.Y * Zooms[_zoom] - _scroll.VerticalOffset)
            : new Point(_scroll.ViewportWidth / 2, _scroll.ViewportHeight / 2);
        _zoom = want;
        ApplyZoom();
        _scroll.UpdateLayout();
        _scroll.ScrollToHorizontalOffset(focus.X * Zooms[_zoom] - screen.X);
        _scroll.ScrollToVerticalOffset(focus.Y * Zooms[_zoom] - screen.Y);
    }

    private void SetHand(bool on)
    {
        _hand = on;
        _image.Cursor = on ? Cursors.Hand : Cursors.Pen;
    }

    private void StartPan(Point at)
    {
        _panFrom = at; _panH = _scroll.HorizontalOffset; _panV = _scroll.VerticalOffset;
        _scroll.CaptureMouse();
        _image.Cursor = Cursors.ScrollAll;
    }

    private void ApplyZoom()
    {
        _scale.ScaleX = _scale.ScaleY = Zooms[_zoom];
        _zoomText.Text = $"x{Zooms[_zoom]}";
        RefreshDetail();
    }

    /// <summary>
    /// 보이는 자리의 칸을 타일 그림으로 다시 찍는다. 배율 1 이면 바탕 그림 그대로라 걷는다.
    /// </summary>
    private void RefreshDetail()
    {
        int zoom = Zooms[_zoom];
        // 타일 원본이 16x16 이라 그보다 키우면 16 으로 찍고 화면 배율로 늘린다(최근접이라 또렷하다).
        int z = Math.Min(zoom, OceanTiles.TileW);
        if (_world == null || _ocean == null || zoom <= 1 || _scroll.ViewportWidth <= 0)
        {
            _detail.Visibility = Visibility.Collapsed;
            return;
        }
        // 배율 z 로 줄인 타일 표 — 칸 하나가 z x z 점이다(16 이면 원본 타일 그대로).
        var tiles = _ocean.GetAverages(z);
        int per = z * z;

        int x0 = Math.Max(0, (int)(_scroll.HorizontalOffset / zoom) - 1);
        int y0 = Math.Max(0, (int)(_scroll.VerticalOffset / zoom) - 1);
        int cols = Math.Min(W - x0, (int)Math.Ceiling(_scroll.ViewportWidth / zoom) + 3);
        int rows = Math.Min(H - y0, (int)Math.Ceiling(_scroll.ViewportHeight / zoom) + 3);
        if (cols <= 0 || rows <= 0) { _detail.Visibility = Visibility.Collapsed; return; }

        int pw = cols * z, ph = rows * z;
        var pixels = new int[pw * ph];
        for (int cy = 0; cy < rows; cy++)
            for (int cx = 0; cx < cols; cx++)
            {
                int tile = Shown(x0 + cx, y0 + cy) & OceanTiles.TileMask;
                int src = tile * per;
                for (int qy = 0; qy < z; qy++)
                {
                    int dst = (cy * z + qy) * pw + cx * z;
                    Array.Copy(tiles, src + qy * z, pixels, dst, z);
                }
            }

        // 격자 — 칸마다 오른쪽·아래 끝 점을 반쯤 어둡게. 칸이 너무 작으면(x4 아래) 그림을 가려 긋지 않는다.
        if (_grid && zoom >= 4)
            for (int py = 0; py < ph; py++)
                for (int px = 0; px < pw; px++)
                    if (px % z == z - 1 || py % z == z - 1)
                    {
                        int c = pixels[py * pw + px];
                        pixels[py * pw + px] = (c >> 1) & 0x7F7F7F;
                    }

        var bmp = BitmapSource.Create(pw, ph, 96, 96, PixelFormats.Bgr32, null, pixels, pw * 4);
        bmp.Freeze();
        _detail.Source = bmp;
        _detail.Width = cols;
        _detail.Height = rows;
        Canvas.SetLeft(_detail, x0);
        Canvas.SetTop(_detail, y0);
        _detail.Visibility = Visibility.Visible;
    }
}
