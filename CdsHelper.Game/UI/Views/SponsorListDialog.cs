using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using CdsHelper.Game.Engine.Town;
using CdsHelper.Game.Local.Helpers;
using CdsHelper.Support.Local.Helpers;
using CdsHelper.Support.Local.Models;
using CdsHelper.Support.Local.Settings;

namespace CdsHelper.Game.UI.Views;

/// <summary>
/// 도구 앱 「요소 → 후원자」 — 후원자 전부(원본 81명과 더해 넣은 사람)를 표로 보고, 고른 사람의 수치를 고친다.
/// </summary>
/// <remarks>
/// 줄을 고르면 오른쪽에 얼굴과 메모(<c>patrons.json</c> 의 <c>note</c>)가 뜬다. 앉는 자리는 후원자 표의 도시 · 건물
/// (<see cref="SponsorTable.SeatedAt"/>)이고, 명성 관문은 안목 x 배수(왕궁 100 · 그 밖 70, <c>0x0044E740</c>)다.
///
/// 아래 칸에서 안목 · 인심 · 재력 · 자리 · 등장/은퇴 연도 · 취향 · 언어를 고친다 — 고친 칸만 <see cref="SponsorEdits"/>
/// (<c>sponsors-edits.json</c>)에 적고 게임이 후원자 표를 읽을 때 얹는다. 개발도구 메뉴 「커스텀 팩」을 골라 두면 그 팩에 적힌다.
/// <b>이름은 못 바꾼다</b> — 후원자 친밀도 · 계약은 세이브가 이름으로 찾는다.
/// </remarks>
public sealed class SponsorListDialog : GameWindow
{
    private readonly DataGrid _grid = new()
    {
        AutoGenerateColumns = false,
        CanUserAddRows = false,
        CanUserDeleteRows = false,
        IsReadOnly = true,
        HeadersVisibility = DataGridHeadersVisibility.Column,
        SelectionMode = DataGridSelectionMode.Single,
        CanUserSortColumns = true,
        Margin = new Thickness(10, 10, 6, 4),
    };

    private readonly TextBox _search = new() { Width = 200, Margin = new Thickness(0, 0, 8, 0) };

    /// <summary>나라 · 도시 고르기 — 맨 앞 「전체」면 안 거른다. 나라를 고르면 도시 목록도 그 나라 것으로 좁힌다(요청).</summary>
    private readonly ComboBox _nation = new() { Width = 160, Margin = new Thickness(0, 0, 8, 0) };
    private readonly ComboBox _city = new() { Width = 120, Margin = new Thickness(0, 0, 8, 0) };

    private const string All = "전체";
    private readonly Image _face = new() { Width = 120, Height = 144, Stretch = Stretch.Uniform, HorizontalAlignment = HorizontalAlignment.Left };
    private readonly TextBlock _detail = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0), Width = 250 };
    private readonly TextBlock _status = new() { Margin = new Thickness(10, 4, 10, 8), TextWrapping = TextWrapping.Wrap };

    // ── 고치는 칸 ──
    private readonly TextBox _eye = Num(), _closeness = Num(), _wealth = Num(80), _seatCity = Num(), _seatBuilding = Num(),
                             _appear = Num(), _retire = Num();
    private readonly CheckBox[] _tastes = new CheckBox[Persuasion.Categories];
    private readonly CheckBox[] _languages = new CheckBox[Skill.Languages.Length];
    private readonly Button _apply = new() { Content = "적용", Padding = new Thickness(12, 2, 12, 2), Margin = new Thickness(0, 0, 6, 0) };
    private readonly Button _reset = new() { Content = "원래대로", Padding = new Thickness(10, 2, 10, 2) };
    private readonly StackPanel _editor = new() { Margin = new Thickness(0, 8, 0, 0), IsEnabled = false };

    private List<Row> _rows = [];
    private Portraits? _faces;
    private SponsorTable? _table;

    public SponsorListDialog()
    {
        Title = "후원자";
        Width = 1280;
        Height = 760;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        Col("번호", nameof(Row.Index), 44);
        Col("이름", nameof(Row.Name), 170);
        Col("직업", nameof(Row.Job), 50);
        Col("나라", nameof(Row.Nation), 130);
        Col("도시", nameof(Row.City), 80);
        Col("건물", nameof(Row.Building), 130);
        Col("등장", nameof(Row.Appear), 48);
        Col("물러남", nameof(Row.Retire), 52);
        Col("안목", nameof(Row.Eye), 44);
        Col("명성 관문", nameof(Row.FameGate), 66);
        Col("인심", nameof(Row.Closeness), 44);
        Col("재력", nameof(Row.Wealth), 80);
        Col("취향", nameof(Row.Tastes), 160);
        Col("언어", nameof(Row.Languages), 160);
        Col("더함", nameof(Row.Added), 40);
        Col("고침", nameof(Row.Edited), 40);

        _grid.SelectionChanged += (_, _) => ShowPicked();
        _search.TextChanged += (_, _) => Filter();
        _nation.SelectionChanged += (_, _) => { FillCities(); Filter(); };
        _city.SelectionChanged += (_, _) => Filter();
        _apply.Click += (_, _) => Apply();
        _reset.Click += (_, _) => ResetPicked();

        BuildEditor();

        var bar = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(10, 10, 10, 0),
            Children =
            {
                new TextBlock { Text = "찾기", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) }, _search,
                new TextBlock { Text = "나라", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 6, 0) }, _nation,
                new TextBlock { Text = "도시", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) }, _city,
            },
        };
        var side = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Margin = new Thickness(4, 10, 10, 4),
            Width = 280,
            Content = new StackPanel { Children = { _face, _detail, _editor } },
        };

        var page = new DockPanel();
        DockPanel.SetDock(bar, Dock.Top);
        DockPanel.SetDock(_status, Dock.Bottom);
        DockPanel.SetDock(side, Dock.Right);
        page.Children.Add(bar);
        page.Children.Add(_status);
        page.Children.Add(side);
        page.Children.Add(_grid);
        Content = page;

        Loaded += (_, _) => Load();
    }

    /// <summary>표 한 줄.</summary>
    private sealed record Row(int Index, string Name, string Job, string Nation, string City, string Building,
                              int Appear, string Retire, int Eye, int FameGate, int Closeness, int Wealth,
                              string Tastes, string Languages, string Added, int Face, bool Female, string Note,
                              string Edited, SponsorTable.Sponsor Sponsor);

    private void Col(string header, string path, double width) =>
        _grid.Columns.Add(new DataGridTextColumn
        {
            Header = header,
            Binding = new System.Windows.Data.Binding(path),
            Width = width,
        });

    private static TextBox Num(double width = 56) =>
        new() { Width = width, Margin = new Thickness(4, 1, 10, 1), TextAlignment = TextAlignment.Right };

    /// <summary>오른쪽 아래 고치는 칸을 짠다.</summary>
    private void BuildEditor()
    {
        _editor.Children.Add(new TextBlock { Text = "수치 고치기 (이름은 바꿀 수 없다)", FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 0, 4) });
        _editor.Children.Add(Line(("안목", _eye), ("인심", _closeness)));
        _editor.Children.Add(Line(("재력(닢)", _wealth)));
        _editor.Children.Add(Line(("도시 번호", _seatCity), ("건물 코드", _seatBuilding)));
        _editor.Children.Add(Line(("등장 연도", _appear), ("은퇴 연도", _retire)));
        _editor.Children.Add(new TextBlock { Text = "은퇴 연도 0 은 안 물러난다.", Foreground = Brushes.Gray, Margin = new Thickness(0, 0, 0, 4) });

        _editor.Children.Add(new TextBlock { Text = "취향", Margin = new Thickness(0, 4, 0, 2) });
        var tastes = new WrapPanel();
        for (int c = 0; c < _tastes.Length; c++)
            tastes.Children.Add(_tastes[c] = new CheckBox { Content = CategoryName(c), Margin = new Thickness(0, 1, 8, 1) });
        _editor.Children.Add(tastes);

        _editor.Children.Add(new TextBlock { Text = "언어", Margin = new Thickness(0, 4, 0, 2) });
        var langs = new WrapPanel();
        for (int i = 0; i < _languages.Length; i++)
            langs.Children.Add(_languages[i] = new CheckBox { Content = Skill.Languages[i], Margin = new Thickness(0, 1, 8, 1) });
        _editor.Children.Add(langs);

        _editor.Children.Add(new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 8, 0, 0),
            Children = { _apply, _reset },
        });
    }

    private static StackPanel Line(params (string Label, TextBox Box)[] cells)
    {
        var line = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 1, 0, 1) };
        foreach (var (label, box) in cells)
        {
            line.Children.Add(new TextBlock { Text = label, Width = 58, VerticalAlignment = VerticalAlignment.Center });
            line.Children.Add(box);
        }
        return line;
    }

    private void Load()
    {
        string dir = Path.GetDirectoryName(AppSettings.LastSaveFilePath) ?? "";
        var table = _table = SponsorTable.Open(dir);
        if (table == null)
        {
            _status.Text = $"후원자 표를 못 읽었습니다 ({SponsorTable.LastError})";
            return;
        }

        var nations = NationTable.Open(dir);
        var buildings = CityBuildingTable.Open(dir);
        var cities = CityTable.Open();
        _faces = Portraits.Open(dir);

        // 메모와 물러나는 해는 patrons.json 에 있다 — 이름으로 짝짓는다.
        var patrons = new Dictionary<string, Patron>();
        try
        {
            foreach (var p in new PatronService().LoadPatrons(Path.Combine(AppContext.BaseDirectory, "patrons.json")))
                if (p.Name is { Length: > 0 } name) patrons[Key(name)] = p;
        }
        catch (Exception ex) when (ex is IOException or System.Text.Json.JsonException) { }

        const int Original = 81;
        _rows = [.. table.Sponsors.Select(s =>
        {
            patrons.TryGetValue(Key(s.Name), out var patron);
            string building = buildings?.InCity(s.City).FirstOrDefault(b => b.Code == s.Building) is { Name.Length: > 0 } b
                ? $"{s.Building} {b.Name}" : $"{s.Building}";
            int retire = s.Retire > 0 ? s.Retire : patron?.RetireYear ?? 0;
            return new Row(
                s.Index, s.Name, s.Job, nations?.Find(s.Nation)?.Name ?? $"{s.Nation}",
                s.City >= 0 ? cities.NameOf(s.City) : "", building,
                SponsorTable.FirstYear + s.Appear, retire > 0 ? $"{retire}" : "",
                s.Eye, s.HasMeetFame ? s.MeetFame : s.Eye * (s.Building == PalaceCode ? 100 : 70), s.Closeness, s.Wealth,
                string.Join(" · ", Enumerable.Range(0, Persuasion.Categories).Where(c => Persuasion.Likes(s.Tastes, c)).Select(CategoryName)),
                string.Join(" · ", Skill.Languages.Where((_, i) => (s.Languages & (1 << i)) != 0)),
                s.Index >= Original ? "●" : "", s.Face, s.IsFemale, patron?.Note ?? "",
                SponsorEdits.Of(s.Index) != null ? "●" : "", s);
        })];

        string? keepNation = _nation.SelectedItem as string;
        _nation.ItemsSource = new[] { All }.Concat(_rows.Select(r => r.Nation).Distinct().Order()).ToList();
        _nation.SelectedItem = keepNation != null && _nation.Items.Contains(keepNation) ? keepNation : All;   // FillCities · Filter 를 부른다
        if (_nation.SelectedIndex < 0) _nation.SelectedIndex = 0;
        _status.Text = (CustomPacks.Active is { } pack ? $"[커스텀 팩 「{pack}」에 고친 칸만 적는다]   " : "")
                     + $"후원자 {_rows.Count}명 — 원본 {Math.Min(Original, _rows.Count)}명 · 더한 사람 {_rows.Count(r => r.Added.Length > 0)}명 · "
                     + $"고친 사람 {_rows.Count(r => r.Edited.Length > 0)}명. "
                     + "명성 관문은 처음 만날 때 집사가 보는 명성(안목 x 왕궁 100 · 그 밖 70, 표에 따로 적은 사람은 그 값)이다. "
                     + "인심은 낼 자금 · 보고 평가 · 모조품 판별을 가르는 고정값이고, 플레이어와의 친밀도는 따로 0 에서 시작한다.";
    }

    /// <summary>왕궁 건물 코드.</summary>
    private const int PalaceCode = 2;

    /// <summary>취향 갈래 이름(힌트 갈래와 같은 차례).</summary>
    private static string CategoryName(int category) =>
        category switch { 0 => "지리", 1 => "역사", 2 => "보물", 3 => "종교", 4 => "교역품", 5 => "미신", 6 => "생물", 7 => "민족", _ => $"{category}" };

    /// <summary>도시 목록을 고른 나라 것으로 다시 채운다. 고르던 도시가 그대로 있으면 둔다.</summary>
    private void FillCities()
    {
        string? was = _city.SelectedItem as string;
        var cities = _rows.Where(r => Picked(_nation) is not { } n || r.Nation == n)
                          .Select(r => r.City).Where(c => c.Length > 0).Distinct().Order().ToList();
        _city.ItemsSource = new[] { All }.Concat(cities).ToList();
        _city.SelectedItem = was != null && cities.Contains(was) ? was : All;
    }

    /// <summary>고른 값. 「전체」거나 안 골랐으면 null.</summary>
    private static string? Picked(ComboBox box) => box.SelectedItem is string s && s != All ? s : null;

    private void Filter()
    {
        string want = _search.Text.Trim();
        string? nation = Picked(_nation), city = Picked(_city);
        _grid.ItemsSource = _rows.Where(r =>
            (nation == null || r.Nation == nation) && (city == null || r.City == city)
            && (want.Length == 0 || $"{r.Name} {r.Job} {r.Nation} {r.City} {r.Building} {r.Tastes} {r.Languages}"
                                        .Contains(want, StringComparison.OrdinalIgnoreCase))).ToList();
    }

    private void ShowPicked()
    {
        if (_grid.SelectedItem is not Row row)
        {
            _face.Source = null;
            _detail.Text = "";
            _editor.IsEnabled = false;
            return;
        }
        _face.Source = _faces?.TryGetBgra(row.Face, row.Female) is { } px ? Portraits.Bitmap(px) : null;
        RenderOptions.SetBitmapScalingMode(_face, Portraits.Scaling(_face.Source));
        _detail.Text = $"{row.Name}\n{row.Job} · {row.Nation}\n{row.City} {row.Building}\n"
                     + $"{row.Appear}년 등장{(row.Retire.Length > 0 ? $" · {row.Retire}년 물러남" : "")}\n얼굴 {row.Face}"
                     + (row.Note.Length > 0 ? $"\n\n{row.Note}" : "");

        var s = row.Sponsor;
        _eye.Text = $"{s.Eye}";
        _closeness.Text = $"{s.Closeness}";
        _wealth.Text = $"{s.Wealth}";
        _seatCity.Text = $"{s.City}";
        _seatBuilding.Text = $"{s.Building}";
        _appear.Text = $"{SponsorTable.FirstYear + s.Appear}";
        _retire.Text = $"{s.Retire}";
        for (int c = 0; c < _tastes.Length; c++) _tastes[c].IsChecked = Persuasion.Likes(s.Tastes, c);
        for (int i = 0; i < _languages.Length; i++) _languages[i].IsChecked = (s.Languages & (1 << i)) != 0;
        _reset.IsEnabled = SponsorEdits.Of(s.Index) != null;
        _editor.IsEnabled = true;
    }

    /// <summary>칸에 적은 값을 고른 후원자에 씌운다 — 표 값과 다른 칸만 적힌다.</summary>
    private void Apply()
    {
        if (_grid.SelectedItem is not Row row || _table?.OriginalOf(row.Index) is not { } original) return;
        if (!Read(_eye, 0, 1000, out int eye) || !Read(_closeness, 0, 1000, out int closeness)
            || !Read(_wealth, 0, int.MaxValue, out int wealth) || !Read(_seatCity, -1, 9999, out int city)
            || !Read(_seatBuilding, -1, 99, out int building)
            || !Read(_appear, SponsorTable.FirstYear, 1700, out int appear) || !Read(_retire, 0, 1700, out int retire))
        {
            _status.Text = "숫자가 아니거나 범위를 벗어난 칸이 있습니다 — 고치지 않았습니다.";
            return;
        }
        int tastes = 0, languages = 0;
        for (int c = 0; c < _tastes.Length; c++) if (_tastes[c].IsChecked == true) tastes |= 1 << c;
        for (int i = 0; i < _languages.Length; i++) if (_languages[i].IsChecked == true) languages |= 1 << i;
        // 취향 칸의 여덟 비트 밖(원본 +0x38 의 다른 비트)은 그대로 둔다.
        tastes |= original.Tastes & ~0xFF;

        SponsorEdits.Set(original, original with
        {
            Eye = eye, Closeness = closeness, Wealth = wealth, City = city, Building = building,
            Appear = appear - SponsorTable.FirstYear, Retire = retire, Tastes = tastes, Languages = languages,
        });
        Reload(row.Index);
    }

    private void ResetPicked()
    {
        if (_grid.SelectedItem is not Row row) return;
        SponsorEdits.Reset(row.Index);
        Reload(row.Index);
    }

    /// <summary>표를 다시 읽고 그 사람을 다시 고른다.</summary>
    private void Reload(int index)
    {
        Load();
        if (_grid.ItemsSource is List<Row> rows && rows.FirstOrDefault(r => r.Index == index) is { } again)
        {
            _grid.SelectedItem = again;
            _grid.ScrollIntoView(again);
        }
    }

    private static bool Read(TextBox box, int min, int max, out int value) =>
        int.TryParse(box.Text.Trim(), out value) && value >= min && value <= max;

    /// <summary>이름 맞추기 열쇠 — 가운뎃점과 빈칸을 뗀다.</summary>
    private static string Key(string name) => name.Replace("·", "").Replace(" ", "");

    public static void Show(Window? owner)
    {
        var window = new SponsorListDialog();
        if (owner != null) window.Owner = owner;
        window.ShowDialog();
    }
}
