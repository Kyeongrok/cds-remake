using System.IO;
using System.Windows;
using System.Windows.Controls;
using CdsHelper.Game.Local.Helpers;
using CdsHelper.Support.Local.Settings;

namespace CdsHelper.Game.UI.Views;

/// <summary>
/// 책 표를 보고, 고치고, <b>새 책을 더하는</b> 창.
/// </summary>
/// <remarks>
/// 힌트는 <see cref="HintEdits"/> 로 새로 더해도 <b>그 자체로는 손에 안 들어온다</b> —
/// <c>LibraryDialog.Shown</c> 이 힌트를 주는 자리는 <c>book.Hints[i]</c> 뿐이라, 그 힌트를
/// 물고 있는 책이 있어야 도서관에서 읽어 얻을 수 있다. 이 창은 그 책 쪽을 손본다 —
/// 기존 책의 힌트 목록에 끼워 넣거나, 아예 새 책을 지어 도시·언어·연도까지 정한다.
///
/// <b>적어 둔 책표.json 을 직접 고치지 않는다</b> — 고치거나 더한 것만 <see cref="BookEdits"/>
/// 로 따로 적어 두고 표가 읽힐 때 얹는다.
/// </remarks>
public sealed class BookEditDialog : GameWindow
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

    private readonly CheckBox _editedOnly = new()
    {
        Content = "고치거나 더한 책만",
        VerticalAlignment = VerticalAlignment.Center,
        Margin = new Thickness(12, 0, 0, 0),
    };

    private readonly Button _add = new()
    {
        Content = "새 책 추가",
        Padding = new Thickness(10, 2, 10, 2),
        Margin = new Thickness(12, 0, 0, 0),
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

    private BookTable? _books;
    private HintTable? _hints;
    private CityBuildingTable? _langs;
    private CityTable? _cityNames;

    public BookEditDialog()
    {
        Title = "책 고치기 · 더하기";
        Width = 1200;
        Height = 700;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        Col("번호", nameof(Row.Id), 52, readOnly: true);
        Col("제목", nameof(Row.Title), 160);
        Col("저자", nameof(Row.Author), 120);
        Col("언어", nameof(Row.Language), 44);
        Col("언어 이름", nameof(Row.LanguageName), 72, readOnly: true);
        Col("나오는 해", nameof(Row.Year), 64);
        Col("놓인 도시(번호, 쉼표)", nameof(Row.CitiesText), 140);
        Col("도시 이름", nameof(Row.CityNames), 160, readOnly: true);
        Col("주는 힌트(번호, 쉼표)", nameof(Row.HintsText), 140);
        Col("힌트 이름", nameof(Row.HintNames), 200, readOnly: true);
        Col("고침", nameof(Row.Mark), 40, readOnly: true);

        _grid.CellEditEnding += (_, e) =>
        {
            if (e.EditAction == DataGridEditAction.Commit)
                Dispatcher.BeginInvoke(new Action(Collect));
        };

        _search.TextChanged += (_, _) => Rebuild();
        _editedOnly.Checked += (_, _) => Rebuild();
        _editedOnly.Unchecked += (_, _) => Rebuild();

        _add.Click += (_, _) => AddNew();
        _reset.Click += (_, _) =>
        {
            if (_grid.SelectedItem is Row row) BookEdits.Reset(row.Id);
            Rebuild();
        };
        _resetAll.Click += (_, _) => { BookEdits.ResetAll(); Rebuild(); };

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
            Children = { label, _search, _editedOnly, _add, _reset, _resetAll },
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

    /// <summary>목록 한 줄. 「이름」 칸들은 셈해서 낸 것이라 못 고친다.</summary>
    private sealed class Row
    {
        public int Id { get; init; }
        public string Title { get; set; } = "";
        public string Author { get; set; } = "";
        public int Language { get; set; }
        public int Year { get; set; } = 1480;
        public string CitiesText { get; set; } = "";
        public string HintsText { get; set; } = "";

        public string LanguageName { get; init; } = "";
        public string CityNames { get; init; } = "";
        public string HintNames { get; init; } = "";

        /// <summary>손으로 고치거나 더한 줄에만 <c>●</c> 가 선다.</summary>
        public string Mark { get; init; } = "";
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
        _books = BookTable.Open(dir);
        _hints = HintTable.Open(dir);
        _langs = CityBuildingTable.Open(dir);
        _cityNames = CityTable.Open();

        if (_books == null)
        {
            _status.Text = "책 표를 못 읽었습니다 — 세이브를 한 번 열어 게임 폴더를 알려 주세요"
                         + $" ({BookTable.LastError})".TrimEnd();
            _grid.IsEnabled = false;
            return;
        }
        Rebuild();
    }

    private void Rebuild()
    {
        if (_books is not { } books) return;

        int keep = _grid.SelectedItem is Row picked ? picked.Id : -1;
        string find = _search.Text.Trim();

        var rows = new List<Row>();
        foreach (var b in books.Books.OrderBy(b => b.Index))
        {
            var row = ToRow(b);
            if (_editedOnly.IsChecked == true && row.Mark.Length == 0) continue;
            if (find.Length > 0 && !Matches(row, find)) continue;
            rows.Add(row);
        }

        _grid.ItemsSource = rows;
        if (keep >= 0) _grid.SelectedItem = rows.FirstOrDefault(r => r.Id == keep);

        int edits = BookEdits.All.Count;
        _status.Text = $"책 {books.Books.Count}권 가운데 {rows.Count}권 — 게임 표 0x004C4748"
                     + (edits == 0 ? "" : $" · 손으로 고치거나 더한 것 {edits}")
                     + "   ·   힌트는 여기 목록에 들어 있어야만 도서관에서 읽어 얻어진다";
    }

    private Row ToRow(in BookTable.Book b) => new()
    {
        Id = b.Index,
        Title = b.Title,
        Author = b.Author,
        Language = b.Language,
        Year = b.Year,
        CitiesText = string.Join(",", b.Cities),
        HintsText = string.Join(",", b.Hints),
        LanguageName = LanguageNameOf(b.Language),
        CityNames = string.Join(", ", b.Cities.Select(c => _cityNames?.NameOf(c) ?? $"도시 {c}")),
        HintNames = string.Join(", ", b.Hints.Select(h => _hints?.Find(h)?.Name ?? $"힌트 {h}")),
        Mark = BookEdits.Of(b.Index) == null ? "" : "●",
    };

    private string LanguageNameOf(int language) =>
        _langs is { } langs && language >= 0 && language < langs.LanguageNames.Count
            ? langs.LanguageNames[language] : $"언어 {language}";

    private static bool Matches(Row row, string find) =>
        row.Title.Contains(find, StringComparison.OrdinalIgnoreCase)
        || row.Author.Contains(find, StringComparison.OrdinalIgnoreCase)
        || row.HintNames.Contains(find, StringComparison.OrdinalIgnoreCase)
        || row.CityNames.Contains(find, StringComparison.OrdinalIgnoreCase);

    private static List<int> ParseInts(string text) =>
        [.. text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(s => int.TryParse(s, out int v) ? v : (int?)null)
                .Where(v => v.HasValue)
                .Select(v => v!.Value)];

    /// <summary>다음 빈 번호로 빈 책 하나를 얹는다.</summary>
    private void AddNew()
    {
        if (_books is not { } books) return;

        int nextId = books.Books.Select(b => b.Index).DefaultIfEmpty(-1).Max() + 1;
        BookEdits.Set(new BookTable.Book(
            nextId, "새 책", "", Language: 0, Year: 1480,
            Cities: [], Hints: []));

        Rebuild();
        if (_grid.ItemsSource is List<Row> rows)
            _grid.SelectedItem = rows.FirstOrDefault(r => r.Id == nextId);
    }

    /// <summary>고친 칸만 골라 적어 둔다 — 게임 값과 같으면 씌우지 않는다.</summary>
    private void Collect()
    {
        if (_books is not { } books || _grid.ItemsSource is not List<Row> rows) return;

        foreach (var row in rows)
        {
            var edited = new BookTable.Book(
                row.Id, row.Title, row.Author, row.Language, row.Year,
                ParseInts(row.CitiesText), ParseInts(row.HintsText));

            if (books.Original(row.Id) is { } game && SameCore(edited, game))
                BookEdits.Reset(row.Id);
            else
                BookEdits.Set(edited);
        }
        Rebuild();
    }

    private static bool SameCore(in BookTable.Book a, in BookTable.Book b) =>
        a.Title == b.Title && a.Author == b.Author && a.Language == b.Language
        && a.Year == b.Year && a.Cities.SequenceEqual(b.Cities) && a.Hints.SequenceEqual(b.Hints);

    /// <summary>창을 띄운다.</summary>
    public static void Show(Window? owner)
    {
        var window = new BookEditDialog();
        if (owner != null) window.Owner = owner;
        window.ShowDialog();
    }
}
