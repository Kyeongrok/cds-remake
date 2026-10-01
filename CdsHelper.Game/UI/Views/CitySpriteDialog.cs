using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CdsHelper.Game.Local.Helpers;
using CdsHelper.Support.Local.Helpers;
using CdsHelper.Support.Local.Settings;

namespace CdsHelper.Game.UI.Views;

/// <summary>
/// 도시 그림 뽑기 — 지도 타일에 박힌 도시 그림을 <b>바탕과 갈라</b> 도시마다 한 장으로 뽑는다.
/// </summary>
/// <remarks>
/// 원본은 도시 그림을 지형 타일에 그려 넣어, 같은 평지라도 도시가 얹힌 칸마다 따로 타일이 있다(그림 타일 495개).
/// 도시 표 <c>+0x74</c> 에는 도시를 지울 때 깔 <b>바탕 타일 3×3</b> 이 있다(<see cref="CityExeTable.EraseOf"/>) —
/// 도시 칸 타일과 그 바탕 타일을 점마다 견주어 색 번호가 다른 점만 남기면 도시 그림이 된다.
///
/// 네 장을 나란히 본다 — <b>원본</b> · <b>바탕만</b> · <b>뽑은 그림</b>(비침은 바둑판) · <b>바탕+그림</b>(원본과 같아야 한다).
/// 둘레 한 칸까지 같이 보여 바탕이 이웃 칸과 이어지는지(해안선 따위) 눈으로 맞춘다. 바탕 블록 밖 칸에 그림 비트(0x8000)가
/// 선 도시는 「블록 밖 그림」으로 표시한다 — 그 칸까지 그림이 번졌을 수 있다.
///
/// 발견물 마커도 같은 손으로 뽑는다 — 발견물 표 <c>+0x54</c> 의 바탕 타일 2×2 다.
///
/// 「PNG 로 모두 저장」은 도시 48×48 을 <c>{번호:000}.png</c>, 발견물 32×32 를 <c>find_{번호:000}.png</c> 로 적는다(비침은 알파 0).
/// 지도에는 아무것도 적지 않는다 — 1단계 미리보기다.
/// </remarks>
public sealed class CitySpriteDialog : Window
{
    private const int Tile = OceanTiles.TileW;          // 16
    private const int Margin = 1;                        // 둘레 한 칸
    private const int Zoom = 4;

    private readonly ListBox _list = new() { Width = 260, Margin = new Thickness(8) };
    private readonly WrapPanel _images = new() { Margin = new Thickness(8) };
    private readonly TextBlock _status = new() { Margin = new Thickness(8, 4, 8, 8), TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _detail = new() { Margin = new Thickness(8), TextWrapping = TextWrapping.Wrap };

    /// <summary>도시 이름(또는 번호)으로 거르는 칸.</summary>
    private readonly TextBox _find = new() { Width = 180, Margin = new Thickness(8, 0, 0, 0), VerticalContentAlignment = VerticalAlignment.Center };
    private bool _onlyWarn;

    private byte[]? _world;
    private OceanTiles? _ocean;
    private CityExeTable? _cities;
    private CityTable? _names;
    private readonly List<Entry> _entries = [];

    /// <summary>모든 그림 블록(도시 · 발견물)이 바탕으로 덮는 칸 — 둘레 칸의 그림 비트가 이웃 블록 것인지 가린다.</summary>
    private readonly HashSet<(int X, int Y)> _covered = [];

    /// <summary>도시 하나, 또는 발견물 마커 하나를 뽑은 것.</summary>
    private sealed record Entry(bool IsCity, int City, string Name, int X, int Y, int Side, uint[] Original, uint[] Background,
                                uint[] Sprite, uint[] Rebuilt, int Erased, int Points, int Outside)
    {
        public override string ToString() =>
            (IsCity ? $"{City,3}. {Name}" : $"발견 {City,3}. {Name}")
            + (Outside > 0 ? $"  ⚠블록 밖 그림 {Outside}칸" : "") + (Points == 0 ? "  (그림 없음)" : "");
    }

    private CitySpriteDialog()
    {
        Title = "도시·발견물 그림 뽑기";
        Width = 1100;
        Height = 760;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        _list.SelectionChanged += (_, _) => ShowEntry();

        var save = new Button { Content = "PNG 로 모두 저장…", Padding = new Thickness(10, 3, 10, 3), Margin = new Thickness(8, 0, 0, 0) };
        save.Click += (_, _) => SaveAll();
        var onlyWarn = new CheckBox { Content = "블록 밖 그림만", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) };
        onlyWarn.Checked += (_, _) => { _onlyWarn = true; Fill(); };
        onlyWarn.Unchecked += (_, _) => { _onlyWarn = false; Fill(); };
        _find.TextChanged += (_, _) => Fill();

        var bar = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(8, 8, 8, 0) };
        bar.Children.Add(new TextBlock { Text = "찾기", VerticalAlignment = VerticalAlignment.Center });
        bar.Children.Add(_find);
        bar.Children.Add(save);
        var export = new Button { Content = "도시 디자인 내보내기", Padding = new Thickness(10, 3, 10, 3), Margin = new Thickness(8, 0, 0, 0),
                                  ToolTip = "도시 그림 디자인(문화권별 아홉 가지)을 asset/citysprite 에 밑그림·목록·기본 4배 그림으로 내보낸다" };
        export.Click += (_, _) => ExportDesigns();
        bar.Children.Add(export);
        bar.Children.Add(onlyWarn);

        var right = new DockPanel();
        DockPanel.SetDock(_images, Dock.Top);
        right.Children.Add(_images);
        right.Children.Add(_detail);

        var body = new DockPanel();
        DockPanel.SetDock(bar, Dock.Top);
        DockPanel.SetDock(_status, Dock.Bottom);
        DockPanel.SetDock(_list, Dock.Left);
        body.Children.Add(bar);
        body.Children.Add(_status);
        body.Children.Add(_list);
        body.Children.Add(new ScrollViewer { Content = right, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        Content = body;

        Loaded += (_, _) => Load();
        KeyDown += (_, e) => { if (e.Key == System.Windows.Input.Key.Escape) Close(); };
    }

    public static void Show(Window owner) => new CitySpriteDialog { Owner = owner }.ShowDialog();

    // ── 읽기 · 뽑기 ─────────────────────────────────────────────────────

    private void Load()
    {
        string dir = Path.GetDirectoryName(AppSettings.LastSaveFilePath) ?? "";
        string worldPath = CdsAssetPath.Resolve(dir, "WORLD.CDS");
        _world = File.Exists(worldPath) ? File.ReadAllBytes(worldPath) : null;
        _ocean = OceanTiles.LoadFromDirectory(dir);
        _cities = CityExeTable.Open(dir);
        _names = CityTable.Open();
        if (_world == null || _ocean == null || _cities == null)
        {
            _status.Text = "WORLD.CDS · OCEAN.CDS · 도시 표를 다 읽지 못했습니다 — 세이브를 한 번 열어 게임 폴더를 알려 주세요.";
            return;
        }

        // 도시 3x3 과 발견물 2x2 블록을 다 모은다 — 둘레 칸 검사가 이웃 블록을 빼고 셀 수 있게 먼저 덮는 칸을 적는다.
        var blocks = new List<(bool IsCity, int Id, string Name, int X, int Y, ushort[] Block, int Side)>();
        for (int id = 0; id < GameMapCoords.CityCount; id++)
            if (_cities.TryCell(id, out int cx, out int cy, out _))
                blocks.Add((true, id, _names?.NameOf(id) ?? $"도시 {id}", cx, cy, _cities.EraseOf(id), CityExeTable.EraseWidth));
        if (DiscoveryTable.Open(dir) is { } finds)
            foreach (var row in finds.Discoveries)
                if (row.HasPlace && row.Erase is { Length: DiscoveryTable.EraseWidth * DiscoveryTable.EraseWidth } erase)
                    blocks.Add((false, row.Id, row.Name, row.X1, row.Y1, erase, DiscoveryTable.EraseWidth));
        foreach (var b in blocks)
            for (int k = 0; k < b.Block.Length; k++)
                if (b.Block[k] != CityExeTable.Keep)
                    _covered.Add((Wrap(b.X + k % b.Side), b.Y + k / b.Side));

        foreach (var b in blocks)
            if (Extract(b.IsCity, b.Id, b.Name, b.X, b.Y, b.Block, b.Side) is { } e) _entries.Add(e);
        Fill();

        int cities = _entries.Count(e => e.IsCity), marks = _entries.Count - cities;
        int warn = _entries.Count(e => e.Outside > 0);
        int empty = _entries.Count(e => e.Points == 0);
        _status.Text = $"도시 {cities}곳 · 발견물 마커 {marks}곳을 뽑았습니다 · 블록 밖 그림 {warn}곳 · 그림 없음 {empty}곳"
                     + "   ·   바탕+그림은 만드는 법이 그대로라 늘 원본과 같습니다 — 볼 것은 「바탕만」이 이웃과 이어지는지입니다.";
    }

    private static int Wrap(int x) => ((x % WorldMapRenderer.UnfoldedW) + WorldMapRenderer.UnfoldedW) % WorldMapRenderer.UnfoldedW;

    private int Word(int x, int y)
    {
        x = Wrap(x);
        y = Math.Clamp(y, 0, WorldMapRenderer.CellH - 1);
        int half = WorldMapRenderer.CellW;
        int at = x < half ? (2 * y) * WorldMapRenderer.RawStride + x * 2
                          : (2 * y + 1) * WorldMapRenderer.RawStride + (x - half) * 2;
        return _world![at] | (_world[at + 1] << 8);
    }

    private Entry? Extract(bool isCity, int city, string name, int cx, int cy, ushort[] erase, int side)
    {
        if (erase.Length != side * side) return null;

        int View = (side + Margin * 2) * Tile;
        int n = View * View;
        var original = new uint[n];
        var background = new uint[n];
        var sprite = new uint[side * Tile * side * Tile];
        var rebuilt = new uint[n];
        var tiles = _ocean!.TileData;
        var pal = _ocean.PaletteRgb;
        int erased = 0, points = 0, outside = 0;

        for (int gy = -Margin; gy < side + Margin; gy++)
            for (int gx = -Margin; gx < side + Margin; gx++)
            {
                int word = Word(cx + gx, cy + gy);
                int tile = word & OceanTiles.TileMask;
                bool inBlock = gx >= 0 && gx < side && gy >= 0 && gy < side;
                ushort under = inBlock ? erase[gy * side + gx] : CityExeTable.Keep;
                bool erasedCell = under != CityExeTable.Keep;
                int baseTile = erasedCell ? under & OceanTiles.TileMask : tile;
                if (erasedCell) erased++;
                else if ((word & 0x8000) != 0 && !_covered.Contains((Wrap(cx + gx), cy + gy))) outside++;   // 이웃 블록 것은 빼고 센다

                for (int py = 0; py < Tile; py++)
                    for (int px = 0; px < Tile; px++)
                    {
                        int k = py * Tile + px;
                        byte a = tiles[tile * OceanTiles.TilePixels + k];
                        byte b = tiles[baseTile * OceanTiles.TilePixels + k];
                        int vx = (gx + Margin) * Tile + px, vy = (gy + Margin) * Tile + py;
                        int vi = vy * View + vx;
                        original[vi] = Opaque(pal[a]);
                        background[vi] = Opaque(pal[b]);
                        rebuilt[vi] = Opaque(pal[b]);
                        if (erasedCell && a != b)
                        {
                            sprite[(gy * Tile + py) * side * Tile + gx * Tile + px] = Opaque(pal[a]);
                            rebuilt[vi] = Opaque(pal[a]);
                            points++;
                        }
                    }
            }

        return new Entry(isCity, city, name, cx, cy, side, original, background, sprite, rebuilt, erased, points, outside);
    }

    private static uint Opaque(int rgb) => 0xFF000000u | (uint)rgb;

    // ── 보이기 ──────────────────────────────────────────────────────────

    /// <summary>목록을 다시 짓는다 — 찾는 글이 있으면 도시 이름·번호로, 「블록 밖 그림만」이면 그것만 거른다.</summary>
    private void Fill()
    {
        string find = _find.Text.Trim();
        var keep = (_list.SelectedItem as Entry)?.City;
        var shown = _entries.Where(e => (!_onlyWarn || e.Outside > 0)
                                        && (find.Length == 0
                                            || e.Name.Contains(find, StringComparison.OrdinalIgnoreCase)
                                            || e.City.ToString() == find))
                            .ToList();
        _list.ItemsSource = shown;
        _list.SelectedItem = shown.FirstOrDefault(e => e.City == keep) ?? shown.FirstOrDefault();
    }

    private void ShowEntry()
    {
        _images.Children.Clear();
        if (_list.SelectedItem is not Entry e) return;

        int view = (e.Side + Margin * 2) * Tile;
        _images.Children.Add(Card("원본", e.Original, view, view, false));
        _images.Children.Add(Card("바탕만", e.Background, view, view, false));
        _images.Children.Add(Card("뽑은 그림", e.Sprite, e.Side * Tile, e.Side * Tile, true));
        _images.Children.Add(Card("바탕+그림", e.Rebuilt, view, view, false));

        _detail.Text = $"{e.Name} — 칸 ({e.X}, {e.Y}) 에서 {e.Side}×{e.Side} · 바탕으로 덮는 칸 {e.Erased}개 · 그림 점 {e.Points}개"
                     + (e.Outside > 0 ? $"\n⚠ 바탕 블록 밖 둘레 칸 {e.Outside}개에 그림 비트가 섰습니다 — 이웃 도시·발견물 블록에도 안 드는 칸이라 그 그림은 바탕과 못 갈라 지도에 남습니다." : "")
                     + $"\n원본·바탕만·바탕+그림은 둘레 한 칸까지 보입니다(가운데 {e.Side}×{e.Side} 이 그림 블록).";
    }

    private static FrameworkElement Card(string title, uint[] bgra, int w, int h, bool checker)
    {
        var bmp = BitmapSource.Create(w, h, 96, 96, PixelFormats.Bgra32, null, bgra, w * 4);
        bmp.Freeze();
        var img = new Image { Source = bmp, Width = w * Zoom, Height = h * Zoom, Stretch = Stretch.Fill };
        RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.NearestNeighbor);

        // 뽑은 그림은 비침이 보이게 바둑판 위에 얹는다.
        var frame = new Grid { Background = checker ? Brushes.White : Brushes.Black };
        if (checker)
            frame.Children.Add(new Border
            {
                Background = new DrawingBrush
                {
                    TileMode = TileMode.Tile, Viewport = new Rect(0, 0, 16, 16), ViewportUnits = BrushMappingMode.Absolute,
                    Drawing = new GeometryDrawing(Brushes.LightGray, null, Geometry.Parse("M0,0 H8 V8 H0Z M8,8 H16 V16 H8Z")),
                },
            });
        frame.Children.Add(img);

        var panel = new StackPanel { Margin = new Thickness(6) };
        panel.Children.Add(new TextBlock { Text = title, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 0, 4) });
        panel.Children.Add(new Border { BorderBrush = Brushes.Gray, BorderThickness = new Thickness(1), Child = frame,
                                        HorizontalAlignment = HorizontalAlignment.Left });
        return panel;
    }

    // ── 디자인 ──────────────────────────────────────────────────────────

    /// <summary>
    /// 도시 디자인을 <c>asset/citysprite</c> 에 내보낸다(<see cref="CityDesigns.Export"/>) — 밑그림 1배·4배, 디자인별 도시 목록,
    /// 그리고 아직 없는 <c>design_N.png</c> 는 Scale4x 로 키운 기본 그림. 이미 있는 고해상도 그림은 건드리지 않는다.
    /// </summary>
    private void ExportDesigns()
    {
        if (_world == null || _ocean == null || _cities == null) return;
        try
        {
            var designs = CityDesigns.Find(Word, _ocean, _cities);
            string folder = CityDesigns.SaveDirectory();
            int made = CityDesigns.Export(folder, designs, _ocean, id => _names?.NameOf(id) ?? $"도시 {id}");
            _status.Text = $"도시 디자인 {designs.Count}가지를 내보냈습니다 — 새 기본 그림 {made}장 · {folder}"
                         + "  (design_N.png 를 정수 배 크기로 그려 넣으면 게임의 「도시 분리」가 그 그림을 씁니다)";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(this, $"내보내지 못했습니다:\n{ex.Message}", "도시 디자인 내보내기");
        }
    }

    // ── 저장 ────────────────────────────────────────────────────────────

    private void SaveAll()
    {
        if (_entries.Count == 0) return;
        var dialog = new Microsoft.Win32.OpenFolderDialog { Title = "도시 그림을 적을 폴더" };
        if (dialog.ShowDialog(this) != true) return;
        int saved = 0;
        try
        {
            foreach (var e in _entries.Where(e => e.Points > 0))
            {
                int s = e.Side * Tile;
                var bmp = BitmapSource.Create(s, s, 96, 96, PixelFormats.Bgra32, null, e.Sprite, s * 4);
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(bmp));
                using var file = File.Create(Path.Combine(dialog.FolderName,
                    e.IsCity ? $"{e.City:000}.png" : $"find_{e.City:000}.png"));
                encoder.Save(file);
                saved++;
            }
            _status.Text = $"도시 그림 {saved}장을 적었습니다: {dialog.FolderName}";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(this, $"저장하지 못했습니다:\n{ex.Message}", "도시 그림 뽑기");
        }
    }
}
