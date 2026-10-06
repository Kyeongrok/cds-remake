using System.ComponentModel;
using System.IO;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Rectangle = System.Windows.Shapes.Rectangle;
using CdsHelper.Game.Local.Helpers;
using CdsHelper.Support.Local.Settings;

namespace CdsHelper.Game.UI.Views;

/// <summary>
/// 도시·건물 보기 — <b>왼쪽에 도시, 오른쪽에 그 도시의 그림과 건물</b>.
/// </summary>
/// <remarks>
/// <see cref="CityBuildingTable"/>(<c>건물표.json</c>, 1508줄)를 편다. 적어 둔 JSON 을 그냥
/// 열면 도시 번호와 비트마스크뿐이라 눈으로 읽기 힘들다 — 여기서는 <b>도시 이름</b>을 붙이고
/// <b>가르치는 기능·언어</b>를 풀어 낸다.
///
/// <b>천오백 줄을 한 판에 늘어놓으면 읽히지 않는다.</b> 도시 하나에 건물이 열 남짓이라
/// 도시를 고르면 그 몫만 오른쪽에 펴는 쪽이 눈에 들어온다.
///
/// 오른쪽 위에는 <b>도시 그림</b>(CITYCG.CDS, 400x320)을 그대로 띄우고, 표에서 고른 건물
/// 자리에 96x80 상자를 씌운다 — 표의 X·Y 가 그림 어디를 가리키는지 눈으로 바로 맞춰 볼 수
/// 있다. 게임 폴더를 모르면 그림 자리는 아예 안 뜬다.
///
/// 오른쪽은 두 쪽이다. <b>표</b> 는 사람이 읽는 꼴이고, <b>JSON</b> 은 적어 둔 파일에 든
/// 날 값 그대로다 — 어느 칸이 어떤 이름으로 적히는지 보거나, 복사해 다른 데 붙일 때 쓴다.
///
/// 읽기만 한다. 건물 자리는 도시 그림에 딸린 것이라 여기서 고칠 것이 못 된다.
///
/// 그림 밑 「바깥 틀(CITYFRM)」을 켜면 CITYFRM.CDS 파트 0(416x336, 여덟 점 두께)을 씌우고, 「장식 테두리」를 끄면 그림 안의
/// 장식 틀을 잘라 낸 안쪽만 보인다. 
/// 「이미지 저장」은 지금 보이는 대로(테두리를 켰으면 테두리째, 건물 상자는 빼고) PNG 로 적는다.
/// </remarks>
public sealed class BuildingListDialog : Window
{
    /// <summary>날 값을 적는 법 — 들여쓰고, 한글을 <c>\uXXXX</c> 로 바꾸지 않는다.</summary>
    private static readonly JsonSerializerOptions Raw = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private readonly ListBox _cityList = new() { Margin = new Thickness(0, 4, 0, 0) };

    private readonly DataGrid _grid = new()
    {
        AutoGenerateColumns = false,
        CanUserAddRows = false,
        CanUserDeleteRows = false,
        CanUserSortColumns = true,
        IsReadOnly = true,
        HeadersVisibility = DataGridHeadersVisibility.Column,
        SelectionMode = DataGridSelectionMode.Single,
    };

    private readonly TextBox _json = new()
    {
        IsReadOnly = true,
        BorderThickness = new Thickness(0),
        FontFamily = new FontFamily("Consolas, D2Coding, 맑은 고딕"),
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
        Padding = new Thickness(8, 6, 8, 6),
    };

    /// <summary>JSON 쪽에서 고른 도시만 낼지, 천오백 줄을 다 낼지.</summary>
    private readonly CheckBox _whole = new()
    {
        Content = "도시 모두",
        VerticalAlignment = VerticalAlignment.Center,
        Margin = new Thickness(0, 0, 10, 0),
    };

    private readonly TextBox _find = new() { Margin = new Thickness(0, 0, 0, 0) };

    private readonly TabControl _tabs = new() { Margin = new Thickness(6, 4, 0, 0) };

    private readonly TextBlock _status = new()
    {
        Margin = new Thickness(10, 6, 10, 8),
        TextWrapping = TextWrapping.Wrap,
    };

    /// <summary>도시 그림 400x320 과 그 위에 씌우는 건물 상자를 담는 자리.</summary>
    private readonly Canvas _pic = new()
    {
        Width = CityPictures.Width,
        Height = CityPictures.Height,
        Background = Brushes.Black,
        ClipToBounds = true,
    };

    private readonly Image _picImage = new()
    {
        Width = CityPictures.Width,
        Height = CityPictures.Height,
    };

    /// <summary>표에서 고른 건물 자리. 아무것도 안 골랐으면 안 보인다.</summary>
    private readonly Rectangle _box = new()
    {
        Width = CityBuildingTable.BoxWidth,
        Height = CityBuildingTable.BoxHeight,
        Stroke = Brushes.Yellow,
        StrokeThickness = 2,
        Fill = new SolidColorBrush(Color.FromArgb(0x30, 0xFF, 0xFF, 0x00)),
        Visibility = Visibility.Collapsed,
    };

    /// <summary>그림 옆 설명 — 도시 이름과 고른 건물 자리.</summary>
    private readonly TextBlock _picNote = new()
    {
        Margin = new Thickness(10, 0, 0, 0),
        TextWrapping = TextWrapping.Wrap,
        VerticalAlignment = VerticalAlignment.Top,
    };

    private readonly Border _picFrame;

    /// <summary>금테 그림(416x336, 안쪽은 비친다). 테두리를 켜면 도시 그림 뒤가 아니라 위에 얹는다.</summary>
    private readonly Image _frameImage = new()
    {
        Width = CityFrame.Width,
        Height = CityFrame.Height,
        IsHitTestVisible = false,
        Visibility = Visibility.Collapsed,
    };

    private readonly CheckBox _frameBox = new()
    {
        Content = "바깥 틀(CITYFRM)",
        VerticalAlignment = VerticalAlignment.Center,
        Margin = new Thickness(0, 0, 10, 0),
        ToolTip = "게임 도시 화면의 금테(CITYFRM.CDS)를 씌운다",
    };

    /// <summary>
    /// 장식 테두리 — 도시 그림(CITYCG) 안에 그려진 나무 틀·깃발 띠·문장. 끄면 가장자리를 <see cref="_inset"/> 점만큼 검게 덮어
    /// 안쪽 풍경만 보인다. 틀 밑 풍경은 자료에 없고 두께가 문화권마다 달라(나가사키 6점 · 리스본 16점 · 구석 소용돌이는 60점 넘게)
    /// 두께를 막대로 고른다. 자리는 그대로라 건물 상자 좌표가 어긋나지 않는다.
    /// </summary>
    private readonly CheckBox _ornateBox = new()
    {
        Content = "장식 테두리",
        IsChecked = true,
        VerticalAlignment = VerticalAlignment.Center,
        Margin = new Thickness(0, 0, 6, 0),
        ToolTip = "도시 그림 안에 그려진 장식 틀 — 끄면 가장자리를 잘라 안쪽 풍경만 본다",
    };

    private readonly Slider _inset = new()
    {
        Minimum = 0, Maximum = 48, Value = 16, Width = 90, TickFrequency = 2, IsSnapToTickEnabled = true,
        VerticalAlignment = VerticalAlignment.Center, IsEnabled = false,
        ToolTip = "잘라 낼 두께(점)",
    };

    private readonly TextBlock _insetText = new() { Width = 34, VerticalAlignment = VerticalAlignment.Center };

    private string _gameDir = "";
    private uint[]? _frameBgra;
    private uint[]? _cityBgra;
    private int _shownCity = -1;

    private CityBuildingTable? _table;
    private CityTable? _cities;

    /// <summary>도시 그림 꾸러미(CITYCG.CDS). 게임 폴더가 없으면 null 이다.</summary>
    private CityPictures? _pictures;

    private BuildingListDialog()
    {
        Title = "도시·건물 보기";
        Width = 1060;
        Height = 800;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        Col("번호", nameof(Row.Code), 50);
        Col("갈래", nameof(Row.Kind), 90);
        Col("이름", nameof(Row.Name), 180);
        Col("X", nameof(Row.X), 48);
        Col("Y", nameof(Row.Y), 48);
        Col("가르치는 것", nameof(Row.Teaches), 200);
        Col("발견물", nameof(Row.Discovery), 60);
        Col("그림", nameof(Row.Picture), 50);
        Col("해설", nameof(Row.Comment), 260);

        // 400x320 도트 그림이라 흐려지지 않게 이웃 점을 그대로 늘린다.
        RenderOptions.SetBitmapScalingMode(_picImage, BitmapScalingMode.NearestNeighbor);
        _pic.Children.Add(_picImage);
        _pic.Children.Add(_box);
        RenderOptions.SetBitmapScalingMode(_frameImage, BitmapScalingMode.NearestNeighbor);
        // 그림과 금테를 한 칸에 겹친다 — 테두리를 켜면 그림이 여덟 점 안으로 들어가고 금테가 위에 덮인다.
        var framed = new Grid();
        framed.Children.Add(_pic);
        framed.Children.Add(_frameImage);
        _frameBox.Checked += (_, _) => SyncFrame();
        _frameBox.Unchecked += (_, _) => SyncFrame();
        _picFrame = new Border
        {
            Child = framed,
            BorderBrush = Brushes.Gray,
            BorderThickness = new Thickness(1),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
        };

        _cityList.SelectionChanged += (_, _) => ShowCity();
        _grid.SelectionChanged += (_, _) => MarkBuilding();
        _find.TextChanged += (_, _) => FillCities();
        _whole.Checked += (_, _) => ShowJson();
        _whole.Unchecked += (_, _) => ShowJson();

        // 왼쪽: 찾기 + 도시 목록.
        var left = new DockPanel { Width = 200, Margin = new Thickness(10, 10, 0, 0) };
        DockPanel.SetDock(_find, Dock.Top);
        left.Children.Add(_find);
        left.Children.Add(_cityList);

        // 오른쪽: 표 쪽과 JSON 쪽.
        _tabs.Items.Add(new TabItem { Header = "표", Content = _grid });
        _tabs.Items.Add(new TabItem { Header = "JSON", Content = JsonPage() });
        _tabs.Items.Add(new TabItem { Header = "건물 그림", Content = SpritePage() });
        _tabs.SelectionChanged += (_, e) =>
        {
            if (e.OriginalSource == _tabs && _tabs.SelectedIndex == 1) ShowJson();
        };

        // 오른쪽 위: 도시 그림(밑에 테두리·저장) + 설명.
        var save = new Button { Content = "이미지 저장…", Padding = new Thickness(10, 3, 10, 3), ToolTip = "지금 보이는 도시 그림을 PNG 로 저장한다" };
        save.Click += (_, _) => SavePicture();
        var picBar = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0) };
        _ornateBox.Checked += (_, _) => { _inset.IsEnabled = false; ShowPicture(_shownCity); };
        _ornateBox.Unchecked += (_, _) => { _inset.IsEnabled = true; ShowPicture(_shownCity); };
        _inset.ValueChanged += (_, _) => { _insetText.Text = $"{(int)_inset.Value}점"; if (_ornateBox.IsChecked != true) ShowPicture(_shownCity); };
        _insetText.Text = $"{(int)_inset.Value}점";
        picBar.Children.Add(_ornateBox);
        picBar.Children.Add(_inset);
        picBar.Children.Add(_insetText);
        picBar.Children.Add(_frameBox);
        picBar.Children.Add(save);
        var picColumn = new StackPanel();
        picColumn.Children.Add(_picFrame);
        picColumn.Children.Add(picBar);

        var top = new DockPanel { Margin = new Thickness(6, 10, 0, 0) };
        DockPanel.SetDock(picColumn, Dock.Left);
        top.Children.Add(picColumn);
        top.Children.Add(_picNote);

        var right = new DockPanel();
        DockPanel.SetDock(top, Dock.Top);
        right.Children.Add(top);
        right.Children.Add(_tabs);

        var body = new DockPanel { Margin = new Thickness(0, 0, 10, 0) };
        DockPanel.SetDock(left, Dock.Left);
        body.Children.Add(left);
        body.Children.Add(right);

        var page = new DockPanel();
        DockPanel.SetDock(_status, Dock.Bottom);
        page.Children.Add(_status);
        page.Children.Add(body);
        Content = page;

        Loaded += (_, _) => Load();
        KeyDown += (_, e) => { if (e.Key is System.Windows.Input.Key.Escape) Close(); };
    }

    /// <summary>표 한 줄 — 사람이 읽는 꼴.</summary>
    private sealed class Row
    {
        public int Code { get; init; }
        public string Kind { get; init; } = "";
        public string Name { get; init; } = "";
        public int X { get; init; }
        public int Y { get; init; }
        public string Teaches { get; init; } = "";
        public int Discovery { get; init; }
        public int Picture { get; init; }
        public string Comment { get; init; } = "";
    }

    /// <summary>왼쪽 도시 한 줄 — 목록에 글로 뜨고, 고르면 번호로 찾는다.</summary>
    private sealed class CityRow
    {
        public int Id { get; init; }
        public string Text { get; init; } = "";
        public override string ToString() => Text;
    }

    private void Col(string header, string path, double width) =>
        _grid.Columns.Add(new DataGridTextColumn
        {
            Header = header,
            Binding = new System.Windows.Data.Binding(path),
            Width = new DataGridLength(width),
            SortMemberPath = path,
        });

    // ── 건물 그림 ─────────────────────────────────────────────────────────

    private readonly ListBox _spriteList = new() { Width = 300 };
    private readonly Image _spriteImage = new() { Width = BuildingSprites.BoxW * 3, Height = BuildingSprites.BoxH * 3 };
    private readonly TextBlock _spriteNote = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(8, 6, 0, 0) };
    private readonly TextBlock _spriteStatus = new() { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0) };
    private List<BuildingSprites.Sprite> _sprites = [];

    /// <summary>
    /// 건물 그림 쪽 — 도시 그림에 박힌 건물을 문화권·갈래·변형마다 뽑아 보여 주고 PNG 로 적는다(<see cref="BuildingSprites"/>).
    /// 뽑는 데 몇 초 걸려 「뽑기」를 눌러야 시작한다.
    /// </summary>
    private UIElement SpritePage()
    {
        var run = new Button { Content = "뽑기", Padding = new Thickness(10, 3, 10, 3) };
        var save = new Button { Content = "PNG 로 모두 저장", Padding = new Thickness(10, 3, 10, 3), Margin = new Thickness(6, 0, 0, 0), IsEnabled = false };
        run.Click += async (_, _) =>
        {
            if (_pictures == null || _table == null) { _spriteStatus.Text = "게임 폴더의 도시 그림을 못 읽었습니다"; return; }
            run.IsEnabled = false;
            _spriteStatus.Text = "도시 그림 226장을 견주는 중…";
            // 그림 읽개는 마지막 한 장을 들고 있어 화면 쪽과 같이 쓰면 부딪친다 — 뽑기용으로 따로 연다.
            var pictures = CityPictures.Open(Path.GetDirectoryName(AppSettings.LastSaveFilePath) ?? "") ?? _pictures;
            var table = _table; var cities = _cities;
            string Culture(int id) => cities?.CultureOf(id) is { Length: > 0 } c ? c : "기타";
            _sprites = await Task.Run(() => BuildingSprites.Extract(pictures, table, Culture));
            _spriteList.ItemsSource = _sprites;
            _spriteList.SelectedIndex = 0;
            _spriteStatus.Text = $"건물 그림 {_sprites.Count}가지 — 문화권·갈래마다 모양이 다른 것은 변형 번호로 갈랐습니다";
            run.IsEnabled = true;
            save.IsEnabled = _sprites.Count > 0;
        };
        save.Click += (_, _) =>
        {
            try
            {
                string folder = BuildingSprites.SaveDirectory();
                BuildingSprites.Export(folder, _sprites, NameOf);
                _spriteStatus.Text = $"{_sprites.Count}장을 적었습니다: {folder}";
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                MessageBox.Show(this, $"저장하지 못했습니다:\n{ex.Message}", "건물 그림");
            }
        };
        _spriteList.SelectionChanged += (_, _) => ShowSprite();
        RenderOptions.SetBitmapScalingMode(_spriteImage, BitmapScalingMode.NearestNeighbor);

        var bar = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 6) };
        bar.Children.Add(run);
        bar.Children.Add(save);
        bar.Children.Add(_spriteStatus);

        // 뽑은 그림은 비침이 보이게 바둑판 위에 얹는다.
        var frame = new Grid
        {
            Background = new DrawingBrush
            {
                TileMode = TileMode.Tile, Viewport = new Rect(0, 0, 16, 16), ViewportUnits = BrushMappingMode.Absolute,
                Drawing = new GeometryDrawing(Brushes.LightGray, null, Geometry.Parse("M0,0 H8 V8 H0Z M8,8 H16 V16 H8Z")),
            },
            HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top,
        };
        frame.Children.Add(_spriteImage);
        var right = new StackPanel { Margin = new Thickness(10, 0, 0, 0) };
        right.Children.Add(new Border { BorderBrush = Brushes.Gray, BorderThickness = new Thickness(1), Child = frame,
                                        HorizontalAlignment = HorizontalAlignment.Left });
        right.Children.Add(_spriteNote);

        var body = new DockPanel();
        DockPanel.SetDock(_spriteList, Dock.Left);
        body.Children.Add(_spriteList);
        body.Children.Add(new ScrollViewer { Content = right, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });

        var page = new DockPanel { Margin = new Thickness(6) };
        DockPanel.SetDock(bar, Dock.Top);
        page.Children.Add(bar);
        page.Children.Add(body);
        return page;
    }

    private void ShowSprite()
    {
        if (_spriteList.SelectedItem is not BuildingSprites.Sprite s) { _spriteImage.Source = null; _spriteNote.Text = ""; return; }
        var bmp = BitmapSource.Create(BuildingSprites.BoxW, BuildingSprites.BoxH, 96, 96, PixelFormats.Bgra32, null,
                                      s.Bgra, BuildingSprites.BoxW * 4);
        bmp.Freeze();
        _spriteImage.Source = bmp;
        _spriteNote.Text = $"{s.Culture} · {s.Kind} 변형 {s.Variant + 1} — 건물 점 {s.Points}개\n"
                         + $"쓰는 도시 {s.Members.Count}곳: " + string.Join(", ", s.Members.Select(m => $"{NameOf(m.City)}({m.X},{m.Y})"))
                         + "\n색은 기준 도시(첫 도시)의 것입니다. 도시가 두세 곳뿐인 변형은 바탕 땅이 비슷해 땅 무늬가 섞여 남을 수 있습니다.";
    }

    /// <summary>JSON 쪽 — 위에 단추 줄, 아래에 날 값.</summary>
    private UIElement JsonPage()
    {
        var copy = new Button { Content = "글로 복사", Padding = new Thickness(10, 3, 10, 3) };
        copy.Click += (_, _) =>
        {
            try { Clipboard.SetText(_json.Text); }
            catch (System.Runtime.InteropServices.COMException) { }
        };

        var bar = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 0, 0, 6),
        };
        bar.Children.Add(_whole);
        bar.Children.Add(copy);

        var page = new DockPanel { Margin = new Thickness(6) };
        DockPanel.SetDock(bar, Dock.Top);
        page.Children.Add(bar);
        page.Children.Add(_json);
        return page;
    }

    private void Load()
    {
        string dir = Path.GetDirectoryName(AppSettings.LastSaveFilePath) ?? "";
        _table = CityBuildingTable.Open(dir);
        _cities = CityTable.Open();
        // 건물 표와 달리 그림은 적어 둘 수 없다(20MB) — 게임 폴더가 있어야만 뜬다.
        _pictures = dir.Length > 0 ? CityPictures.Open(dir) : null;
        _gameDir = dir;
        if (_pictures == null) _picFrame.Visibility = Visibility.Collapsed;
        _frameBgra = dir.Length > 0 ? CityFrame.TryGetBgra(dir) : null;
        if (_frameBgra != null)
        {
            var frame = BitmapSource.Create(CityFrame.Width, CityFrame.Height, 96, 96, PixelFormats.Bgra32, null,
                                            _frameBgra, CityFrame.Width * 4);
            frame.Freeze();
            _frameImage.Source = frame;
        }
        else _frameBox.IsEnabled = false;

        if (_table == null)
        {
            _status.Text = $"건물표를 열지 못했습니다 — {CityBuildingTable.LastError}";
            return;
        }

        FillCities();
    }

    /// <summary>도시 이름. 표를 못 읽었으면 번호로 낸다.</summary>
    private string NameOf(int id) => _cities?.NameOf(id) ?? $"도시 {id}";

    /// <summary>왼쪽 목록을 짓는다. 찾는 글이 있으면 이름·번호로 거른다.</summary>
    private void FillCities()
    {
        if (_table is not { } table) return;

        string find = _find.Text.Trim();
        int keep = (_cityList.SelectedItem as CityRow)?.Id ?? -1;

        var rows = new List<CityRow>();
        foreach (int id in table.Buildings.Select(b => b.City).Distinct().Order())
        {
            string name = NameOf(id);
            if (find.Length > 0
                && !name.Contains(find, StringComparison.OrdinalIgnoreCase)
                && !id.ToString().Contains(find)) continue;

            rows.Add(new CityRow
            {
                Id = id,
                Text = $"{id,3}. {name}  ({table.InCity(id).Count})",
            });
        }

        _cityList.ItemsSource = rows;
        _cityList.SelectedItem = rows.FirstOrDefault(r => r.Id == keep) ?? rows.FirstOrDefault();
    }

    /// <summary>고른 도시의 건물을 오른쪽에 편다.</summary>
    private void ShowCity()
    {
        if (_table is not { } table) return;
        if (_cityList.SelectedItem is not CityRow city)
        {
            _grid.ItemsSource = null;
            ShowPicture(-1);
            ShowJson();
            _status.Text = Tail(table);
            return;
        }

        var rows = table.InCity(city.Id).Select(one => new Row
        {
            Code = one.Code,
            Kind = one.Kind,
            Name = one.Name,
            X = one.X,
            Y = one.Y,
            Teaches = string.Join(" ", table.Teaches(one.TeachMask)),
            Discovery = one.Discovery,
            Picture = one.Picture,
            Comment = one.Comment,
        }).ToList();

        _grid.ItemsSource = rows;
        ShowPicture(city.Id);
        _grid.Items.SortDescriptions.Clear();
        _grid.Items.SortDescriptions.Add(
            new SortDescription(nameof(Row.Code), ListSortDirection.Ascending));
        _grid.Items.Refresh();
        ShowJson();

        int teach = rows.Count(r => r.Teaches.Length > 0);
        _status.Text = $"{NameOf(city.Id)} — 건물 {rows.Count}개"
                     + (teach == 0 ? "" : $" (가르치는 곳 {teach}개)")
                     + "   ·   " + Tail(table);
    }

    /// <summary>
    /// 고른 도시의 그림을 오른쪽 위에 건다. 그림이 없는 도시(원주민 마을 위쪽 번호)나
    /// 게임 폴더를 모르는 자리에서는 까만 판만 남는다.
    /// </summary>
    private void ShowPicture(int cityId)
    {
        _box.Visibility = Visibility.Collapsed;
        _cityBgra = null;
        _shownCity = cityId;
        if (_pictures is not { } pictures) return;

        var bgra = cityId < 0 ? null : pictures.TryGetBgra(cityId);
        if (bgra != null && _ornateBox.IsChecked != true) bgra = Crop(bgra, (int)_inset.Value);
        _cityBgra = bgra;
        if (bgra == null)
        {
            _picImage.Source = null;
            _picNote.Text = cityId < 0 ? "" : $"{NameOf(cityId)} — 그림이 없습니다";
            return;
        }

        var picture = BitmapSource.Create(CityPictures.Width, CityPictures.Height, 96, 96,
                                          PixelFormats.Bgra32, null, bgra, CityPictures.Width * 4);
        picture.Freeze();
        _picImage.Source = picture;
        _picNote.Text = Note(
            NameOf(cityId),
            $"CITYCG.CDS {CityPictures.Width}x{CityPictures.Height}",
            "",
            "표에서 건물을 고르면 그 자리에",
            $"{CityBuildingTable.BoxWidth}x{CityBuildingTable.BoxHeight} 상자를 씌웁니다.");
    }

    /// <summary>표에서 고른 건물 자리를 그림 위에 씌운다.</summary>
    private void MarkBuilding()
    {
        if (_picImage.Source == null || _grid.SelectedItem is not Row row)
        {
            _box.Visibility = Visibility.Collapsed;
            return;
        }

        Canvas.SetLeft(_box, row.X);
        Canvas.SetTop(_box, row.Y);
        _box.Visibility = Visibility.Visible;

        if (_cityList.SelectedItem is CityRow city)
            _picNote.Text = Note(
                NameOf(city.Id),
                $"CITYCG.CDS {CityPictures.Width}x{CityPictures.Height}",
                "",
                $"{row.Kind} · {row.Name}",
                $"왼쪽 위 ({row.X}, {row.Y})",
                $"가운데 ({row.X + CityBuildingTable.BoxWidth / 2}, "
                    + $"{row.Y + CityBuildingTable.BoxHeight / 2})");
    }

    /// <summary>가장자리 <paramref name="inset"/> 점을 검게 덮은 사본 — 장식 테두리를 껐을 때 안쪽만 보이게.</summary>
    private static uint[] Crop(uint[] bgra, int inset)
    {
        var copy = (uint[])bgra.Clone();
        for (int y = 0; y < CityPictures.Height; y++)
            for (int x = 0; x < CityPictures.Width; x++)
                if (x < inset || y < inset || x >= CityPictures.Width - inset || y >= CityPictures.Height - inset)
                    copy[y * CityPictures.Width + x] = 0xFF000000u;
        return copy;
    }

    /// <summary>바깥 틀(CITYFRM)을 켜고 끈다 — 켜면 그림을 여덟 점 안으로 밀고 틀을 덮는다.</summary>
    private void SyncFrame()
    {
        bool on = _frameBox.IsChecked == true && _frameImage.Source != null;
        _frameImage.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
        _pic.Margin = on ? new Thickness(CityFrame.Border) : new Thickness(0);
    }

    /// <summary>
    /// 지금 보이는 도시 그림을 PNG 로 적는다 — 테두리를 켰으면 금테째(416x336), 아니면 그림만(400x320).
    /// 건물 상자는 고르는 표시라 넣지 않는다.
    /// </summary>
    private void SavePicture()
    {
        if (_cityBgra is not { } city)
        {
            MessageBox.Show(this, "저장할 도시 그림이 없습니다.", "이미지 저장");
            return;
        }

        bool framed = _frameBox.IsChecked == true && _frameBgra != null;
        int w = framed ? CityFrame.Width : CityPictures.Width;
        int h = framed ? CityFrame.Height : CityPictures.Height;
        var pixels = new uint[w * h];
        int off = framed ? CityFrame.Border : 0;
        for (int y = 0; y < CityPictures.Height; y++)
            Array.Copy(city, y * CityPictures.Width, pixels, (y + off) * w + off, CityPictures.Width);
        if (framed)
            for (int i = 0; i < pixels.Length; i++)
                if ((_frameBgra![i] >> 24) != 0) pixels[i] = _frameBgra[i];   // 금테는 비치지 않는 점만 덮는다

        var name = NameOf(_shownCity);
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = "도시 그림 저장",
            Filter = "PNG 그림|*.png",
            FileName = $"{_shownCity:000}_{string.Concat(name.Split(Path.GetInvalidFileNameChars()))}{(framed ? "_테두리" : "")}.png",
        };
        if (dialog.ShowDialog(this) != true) return;

        try
        {
            var bmp = BitmapSource.Create(w, h, 96, 96, PixelFormats.Bgra32, null, pixels, w * 4);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bmp));
            using var file = File.Create(dialog.FileName);
            encoder.Save(file);
            _status.Text = $"저장했습니다: {dialog.FileName}";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(this, $"저장하지 못했습니다:\n{ex.Message}", "이미지 저장");
        }
    }

    /// <summary>그림 옆 설명 한 덩이 — 줄마다 한 도막.</summary>
    private static string Note(params string[] lines) => string.Join(Environment.NewLine, lines);

    /// <summary>아래 줄 뒷부분 — 어느 도시를 보든 그대로인 몫.</summary>
    private string Tail(CityBuildingTable table) =>
        $"도시 {_cityList.Items.Count}곳 · 건물 모두 {table.Buildings.Count}개"
        + "   ·   읽기만 합니다(건물 자리는 도시 그림에 딸린 값입니다)";

    /// <summary>
    /// JSON 쪽을 짓는다. 표 쪽이 보일 때는 굳이 적지 않는다 — 천오백 줄을 다 내면
    /// 글자가 백만 자를 넘어 도시를 넘길 때마다 멈칫한다.
    /// </summary>
    private void ShowJson()
    {
        if (_tabs.SelectedIndex != 1) { _json.Text = ""; return; }
        if (_table is not { } table) return;

        var got = _whole.IsChecked == true || _cityList.SelectedItem is not CityRow city
            ? table.Buildings
            : table.InCity(city.Id);

        _json.Text = JsonSerializer.Serialize(got, Raw);
        _json.ScrollToHome();
    }

    /// <summary>창을 연다.</summary>
    public static void Show(Window owner) =>
        new BuildingListDialog { Owner = owner }.ShowDialog();
}
