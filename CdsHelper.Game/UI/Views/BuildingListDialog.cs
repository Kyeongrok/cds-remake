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
/// 건물 보기 — <b>왼쪽에 도시, 오른쪽에 그 도시의 건물</b>.
/// </summary>
/// <remarks>
/// <see cref="CityBuildingTable"/>(<c>건물표.json</c>, 1504줄)를 편다. 적어 둔 JSON 을 그냥
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

    private CityBuildingTable? _table;
    private CityTable? _cities;

    /// <summary>도시 그림 꾸러미(CITYCG.CDS). 게임 폴더가 없으면 null 이다.</summary>
    private CityPictures? _pictures;

    private BuildingListDialog()
    {
        Title = "건물 보기";
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
        _picFrame = new Border
        {
            Child = _pic,
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
        _tabs.SelectionChanged += (_, e) =>
        {
            if (e.OriginalSource == _tabs && _tabs.SelectedIndex == 1) ShowJson();
        };

        // 오른쪽 위: 도시 그림 + 설명.
        var top = new DockPanel { Margin = new Thickness(6, 10, 0, 0) };
        DockPanel.SetDock(_picFrame, Dock.Left);
        top.Children.Add(_picFrame);
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
        if (_pictures == null) _picFrame.Visibility = Visibility.Collapsed;

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
        if (_pictures is not { } pictures) return;

        var bgra = cityId < 0 ? null : pictures.TryGetBgra(cityId);
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
