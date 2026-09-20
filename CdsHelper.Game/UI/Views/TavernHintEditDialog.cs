using System.IO;
using System.Windows;
using System.Windows.Controls;
using CdsHelper.Game.Local.Helpers;
using CdsHelper.Support.Local.Settings;

namespace CdsHelper.Game.UI.Views;

/// <summary>
/// 힌트 186줄을 보고 고치거나, 원본에 없던 새 힌트를 더하는 창.
/// </summary>
/// <remarks>
/// <b>힌트 자체</b>(이름·등급·갈래·자금·기한·가리키는 발견물·설명)가 이 창의 본디 임자다.
/// 「목적지」·「술집이 이르는 곳」 두 칸은 그 힌트 자체의 값이 아니라, <b>술집 주인이
/// 그 힌트를 놓고 어느 쪽으로 가라고 일러 주는지</b>를 셈해서 곁다리로 보여 주는 것뿐이다
/// (계약한 힌트 하나에 곁들는 한 줄짜리 안내다) — 셈은 이렇다.
/// <code>
///   힌트 줄의 +0x08 ─ 일련번호 ─→ 발견물 줄의 +0x08 로 짝을 맺고
///   그 발견물이 앉은 사각형 ─→ 안에 든 도시(없으면 가장 가까운 도시)
///   그 도시의 문화권 ─→ "이베리아" "중근동" 같은 방향 이름
/// </code>
/// 그래서 <b>일련번호 한 칸만 틀려도</b> 엉뚱한 데로 가라고 한다 — 「카르낙 거석군」
/// (일련번호 107)을 줄 번호로 잘못 알고 표의 107째 줄을 짚으면 「로제타석」이 나와
/// 브르타뉴 대신 중근동을 일러 주었다. 고치기 전에 눈으로 대 볼 수 있게 함께 낸다.
///
/// <b>적어 둔 힌트표.json 을 직접 고치지 않는다</b> — 고치거나 더한 것만 따로 적어 두고
/// (<see cref="HintEdits"/>) 표가 읽힐 때 얹는다. 그래서 여기서 고치면 놀이 안의 술집·도서관도
/// 그대로 따라온다.
/// </remarks>
public sealed class TavernHintEditDialog : GameWindow
{
    private readonly DataGrid _grid = new()
    {
        AutoGenerateColumns = false,
        CanUserAddRows = false,
        CanUserDeleteRows = false,
        HeadersVisibility = DataGridHeadersVisibility.Column,
        SelectionMode = DataGridSelectionMode.Single,
        Margin = new Thickness(10, 10, 10, 4),
    };

    private readonly TextBox _search = new()
    {
        Width = 180,
        Padding = new Thickness(4, 2, 4, 2),
        VerticalContentAlignment = VerticalAlignment.Center,
    };

    private readonly CheckBox _oddOnly = new()
    {
        Content = "짝이 안 맞는 줄만",
        VerticalAlignment = VerticalAlignment.Center,
        Margin = new Thickness(12, 0, 0, 0),
        ToolTip = "일련번호로 발견물을 못 찾거나, 그 발견물에 자리가 없거나, "
                + "어느 책에도 안 물려 있어 도서관에서 절대 못 얻는 줄만 낸다",
    };

    private readonly Button _add = new()
    {
        Content = "새 힌트 추가",
        Padding = new Thickness(10, 2, 10, 2),
        Margin = new Thickness(12, 0, 0, 0),
        ToolTip = "원본에 없던 새 힌트 번호를 하나 더한다 — 커스텀 발견물의 짝을 지을 때 쓴다",
    };

    private readonly Button _reset = new()
    {
        Content = "이 줄 되돌리기",
        Padding = new Thickness(10, 2, 10, 2),
        Margin = new Thickness(6, 0, 0, 0),
        ToolTip = "원본에 있던 번호면 게임 값으로 돌아가고, 여기서 새로 더한 번호면 통째로 없어진다",
    };

    private readonly Button _resetAll = new()
    {
        Content = "전부 되돌리기",
        Padding = new Thickness(10, 2, 10, 2),
        Margin = new Thickness(6, 0, 0, 0),
    };

    private readonly TextBlock _status = new()
    {
        Margin = new Thickness(10, 4, 10, 8),
        TextWrapping = TextWrapping.Wrap,
    };

    private HintTable? _hints;
    private DiscoveryTable? _discoveries;
    private CityExeTable? _cities;
    private CityTable? _cityNames;
    private BookTable? _books;

    public TavernHintEditDialog()
    {
        Title = "힌트 고치기 · 더하기";
        Width = 1180;
        Height = 700;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        Col("번호", nameof(Row.Id), 52, readOnly: true);
        Col("힌트 이름", nameof(Row.Name), 150);
        Col("등급", nameof(Row.Grade), 48);
        Col("갈래", nameof(Row.Category), 48);
        Col("갈래 이름", nameof(Row.CategoryName), 84, readOnly: true);
        Col("자금", nameof(Row.Funds), 72);
        Col("기한", nameof(Row.Deadline), 48);
        Col("일련번호", nameof(Row.Discovery), 66);
        Col("가리키는 발견물", nameof(Row.DiscoveryName), 150, readOnly: true);
        Col("수록된 책", nameof(Row.Books), 160, readOnly: true);
        Col("설명", nameof(Row.Text), 260);
        // 여기부터 둘은 힌트 자체의 칸이 아니라, 술집 주인이 이 힌트를 놓고 어느 쪽으로
        // 가라 이르는지 셈해서 곁다리로 보여 주는 것뿐이다.
        Col("(곁다리)목적지", nameof(Row.TargetCity), 110, readOnly: true);
        Col("(곁다리)술집 안내", nameof(Row.Bearing), 110, readOnly: true);
        Col("고침", nameof(Row.Mark), 44, readOnly: true);

        _grid.CellEditEnding += (_, e) =>
        {
            // 칸을 다 쓰고 나서야 값이 들어온다 — 한 박자 뒤에 거둔다.
            if (e.EditAction == DataGridEditAction.Commit)
                Dispatcher.BeginInvoke(new Action(Collect));
        };

        _search.TextChanged += (_, _) => Rebuild();
        _oddOnly.Checked += (_, _) => Rebuild();
        _oddOnly.Unchecked += (_, _) => Rebuild();

        _add.Click += (_, _) => AddNew();
        _reset.Click += (_, _) =>
        {
            if (_grid.SelectedItem is Row row) HintEdits.Reset(row.Id);
            Rebuild();
        };
        _resetAll.Click += (_, _) => { HintEdits.ResetAll(); Rebuild(); };

        var label = new TextBlock
        {
            Text = "찾기",
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 6, 0),
        };
        var bar = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(10, 10, 10, 0),
            Children = { label, _search, _oddOnly, _add, _reset, _resetAll },
        };

        var page = new DockPanel();
        DockPanel.SetDock(bar, Dock.Top);
        DockPanel.SetDock(_status, Dock.Bottom);
        page.Children.Add(bar);
        page.Children.Add(_status);
        page.Children.Add(_grid);
        Content = page;

        Loaded += (_, _) => Load();
    }

    /// <summary>목록 한 줄. 오른쪽 세 칸은 셈해서 낸 것이라 못 고친다.</summary>
    private sealed class Row
    {
        public int Id { get; init; }
        public string Name { get; set; } = "";
        public int Grade { get; set; }
        public int Category { get; set; }
        public int Funds { get; set; }
        public int Deadline { get; set; }

        /// <summary>가리키는 발견물의 <b>일련번호</b>. 줄 번호가 아니다.</summary>
        public int Discovery { get; set; }

        public string Text { get; set; } = "";

        public string CategoryName { get; init; } = "";
        public string DiscoveryName { get; init; } = "";
        public string TargetCity { get; init; } = "";
        public string Bearing { get; init; } = "";

        /// <summary>이 힌트를 물고 있는 책 이름들. 하나도 없으면 「(없음)」 — 그러면 도서관에서
        /// 아무리 책을 읽어도 이 힌트는 절대 안 나온다(<c>LibraryDialog.Shown</c> 참고).</summary>
        public string Books { get; init; } = "";

        /// <summary>손으로 고친 줄에만 <c>●</c> 가 선다.</summary>
        public string Mark { get; init; } = "";

        /// <summary>발견물을 못 찾았거나, 그 발견물에 자리가 없거나, 어느 책에도 안 물린 줄.</summary>
        public bool Odd { get; init; }
    }

    private void Col(string header, string path, double width, bool readOnly = false) =>
        _grid.Columns.Add(new DataGridTextColumn
        {
            Header = header,
            Binding = new System.Windows.Data.Binding(path),
            Width = new DataGridLength(width),
            IsReadOnly = readOnly,
        });

    private void Load()
    {
        string dir = Path.GetDirectoryName(AppSettings.LastSaveFilePath) ?? "";
        _hints = HintTable.Open(dir);
        _discoveries = DiscoveryTable.Open(dir);
        _cities = CityExeTable.Open(dir);
        _cityNames = CityTable.Open();
        _books = BookTable.Open(dir);

        if (_hints == null)
        {
            _status.Text = "힌트 표를 못 읽었습니다 — 세이브를 한 번 열어 게임 폴더를 알려 주세요"
                         + $" ({HintTable.LastError})".TrimEnd();
            _grid.IsEnabled = false;
            return;
        }
        Rebuild();
    }

    private void Rebuild()
    {
        if (_hints is not { } hints) return;

        int keep = _grid.SelectedItem is Row picked ? picked.Id : -1;
        string find = _search.Text.Trim();

        var rows = new List<Row>();
        int odd = 0, noBook = 0;
        foreach (var hint in hints.Hints)
        {
            var (name, city, bearing, aimBad) = Aim(hint.Discovery);
            string books = BooksOf(hint.Id);
            // 책 표를 못 읽었으면(_books == null) 모르는 것이지 없는 것이 아니다 — 나쁨으로 안 친다.
            bool bookless = _books != null && books.Length == 0;
            if (bookless) noBook++;
            bool bad = aimBad || bookless;
            if (bad) odd++;

            var row = new Row
            {
                Id = hint.Id,
                Name = hint.Name,
                Grade = hint.Grade,
                Category = hint.Category,
                Funds = hint.Funds,
                Deadline = hint.Deadline,
                Discovery = hint.Discovery,
                Text = hint.Text,
                CategoryName = hints.CategoryOf(hint.Category),
                DiscoveryName = name,
                TargetCity = city,
                Bearing = bearing,
                Books = _books == null ? "(모름)" : books.Length == 0 ? "(없음)" : books,
                Odd = bad,
                Mark = HintEdits.Of(hint.Id) == null ? "" : "●",
            };

            if (_oddOnly.IsChecked == true && !bad) continue;
            if (find.Length > 0 && !Matches(row, find)) continue;

            rows.Add(row);
        }

        _grid.ItemsSource = rows;
        if (keep >= 0) _grid.SelectedItem = rows.FirstOrDefault(r => r.Id == keep);

        int edits = HintEdits.All.Count;
        _status.Text = $"힌트 {hints.Hints.Count}줄 가운데 {rows.Count}줄 — 게임 표 0x004D8E80"
                     + (odd == 0 ? "" : $" · 짝이 안 맞는 줄 {odd}")
                     + (noBook == 0 ? "" : $"(그중 어느 책에도 안 물린 줄 {noBook})")
                     + (edits == 0 ? "" : $" · 손으로 고친 줄 {edits}")
                     + "   ·   「일련번호」는 발견물 표의 몇째 줄인지가 아니라 발견물 줄의"
                     + " +0x08 과 맞대어 보는 번호다   ·   힌트는 어느 책에도 안 물려 있으면"
                     + " 도서관에서 절대 안 나온다(「수록된 책」 참고)   ·   고친 것은 놀이 안에서도 그대로 쓰인다";
    }

    /// <summary>이 힌트 번호를 물고 있는 책 이름들. 하나도 없으면 빈 글.</summary>
    private string BooksOf(int hintId) =>
        _books == null ? ""
        : string.Join(", ", _books.Books.Where(b => b.Hints.Contains(hintId)).Select(b => b.Title));

    private static bool Matches(Row row, string find) =>
        row.Name.Contains(find, StringComparison.OrdinalIgnoreCase)
        || row.DiscoveryName.Contains(find, StringComparison.OrdinalIgnoreCase)
        || row.TargetCity.Contains(find, StringComparison.OrdinalIgnoreCase)
        || row.Books.Contains(find, StringComparison.OrdinalIgnoreCase)
        || row.Text.Contains(find, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// 그 일련번호가 가리키는 발견물과, 술집이 이를 곳.
    /// </summary>
    /// <remarks>셈은 술집이 하는 것과 똑같다 — 눈으로 대 보라고 함께 낸다.</remarks>
    private (string Name, string City, string Bearing, bool Odd) Aim(int serial)
    {
        if (_discoveries?.FindBySerial(serial) is not { } row)
            return ("(못 찾음)", "", "", true);
        if (!row.HasPlace)
            return (row.Name, "(자리 없음)", "", true);
        if (_cities is not { } cities)
            return (row.Name, "", "", false);

        int cx = (row.X1 + row.X2) / 2, cy = (row.Y1 + row.Y2) / 2;
        int best = -1;
        long near = long.MaxValue;
        for (int city = 0; city < CityExeTable.Count; city++)
        {
            if (!cities.TryCell(city, out int x, out int y, out _)) continue;
            if (row.Covers(x, y)) { best = city; break; }

            long dx = x - cx, dy = y - cy, far = dx * dx + dy * dy;
            if (far < near) { near = far; best = city; }
        }
        if (best < 0) return (row.Name, "", "", true);

        return (row.Name,
                _cityNames?.NameOf(best) ?? $"도시 {best}",
                TavernMenu.BearingName(cities.CultureOf(best)),
                false);
    }

    /// <summary>다음 빈 번호로 새 힌트 하나를 더한다.</summary>
    private void AddNew()
    {
        if (_hints is not { } hints) return;

        int nextId = hints.Hints.Select(h => h.Id).DefaultIfEmpty(-1).Max() + 1;
        HintEdits.Set(nextId, "새 힌트", 1, 0, 10000, 3, -1, "");

        Rebuild();
        if (_grid.ItemsSource is List<Row> rows)
            _grid.SelectedItem = rows.FirstOrDefault(r => r.Id == nextId);
    }

    /// <summary>고친 칸만 골라 적어 둔다 — 게임 값과 같으면 씌우지 않는다.</summary>
    private void Collect()
    {
        if (_hints is not { } hints || _grid.ItemsSource is not List<Row> rows) return;

        foreach (var row in rows)
        {
            // 원본에 있던 번호면 게임 값과 다른 칸만 씌운다. 원본에 없던(여기서 더한) 번호면
            // 견줄 게임 값이 없으니 칸 그대로 통째로 적는다.
            if (hints.Original(row.Id) is { } game)
                HintEdits.Set(row.Id,
                              row.Name == game.Name ? null : row.Name,
                              row.Grade == game.Grade ? null : row.Grade,
                              row.Category == game.Category ? null : row.Category,
                              row.Funds == game.Funds ? null : row.Funds,
                              row.Deadline == game.Deadline ? null : row.Deadline,
                              row.Discovery == game.Discovery ? null : row.Discovery,
                              row.Text == game.Text ? null : row.Text);
            else
                HintEdits.Set(row.Id, row.Name, row.Grade, row.Category, row.Funds,
                              row.Deadline, row.Discovery, row.Text);
        }
        Rebuild();
    }

    /// <summary>창을 띄운다.</summary>
    public static void Show(Window? owner)
    {
        var window = new TavernHintEditDialog();
        if (owner != null) window.Owner = owner;
        window.ShowDialog();
    }
}
