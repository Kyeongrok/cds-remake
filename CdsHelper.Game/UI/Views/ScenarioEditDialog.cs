using System.IO;
using System.Windows;
using System.Windows.Controls;
using CdsHelper.Game.Engine;
using CdsHelper.Game.Engine.Disev;
using CdsHelper.Support.Local.Models;

namespace CdsHelper.Game.UI.Views;

/// <summary>
/// 시나리오(미리 만든 주인공 + 이야기 책)를 짓고 고치는 창.
/// </summary>
/// <remarks>
/// 원본에 없는 것이다 — 초심자용 주인공 둘(<see cref="Beginner"/>)은 EXE 에 박혀 있지만, 여기서 지은 사람은
/// 표(<c>exe-tables/시나리오목록.json</c>)에 적힌다. 줄을 새로 지으면 그 사람의 이야기 책(<c>시나리오책N.json</c>)이
/// 본보기 파트 하나를 든 채로 함께 생기고, 「대본 열기」로 대본 편집기에서 장면을 적는다.
/// 모드 「시나리오 모드」를 켜야 NEW GAME 에 나온다.
/// </remarks>
public sealed class ScenarioEditDialog : GameWindow
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

    private readonly Button _add = Push("새 시나리오");
    private readonly Button _remove = Push("이 줄 지우기");
    private readonly Button _script = Push("대본 열기");
    private readonly Button _save = Push("저장");
    private readonly TextBlock _status = new() { Margin = new Thickness(10, 4, 10, 8), TextWrapping = TextWrapping.Wrap };

    private List<Row> _rows = [];

    public ScenarioEditDialog()
    {
        Title = "시나리오 짓기";
        Width = 1240;
        Height = 560;
        Background = System.Windows.Media.Brushes.White;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        Col("책", nameof(Row.Book), 92, readOnly: true);
        Col("제목", nameof(Row.Title), 150);
        Col("이름", nameof(Row.Given), 80);
        Col("성", nameof(Row.Family), 110);
        Col("태어난 해", nameof(Row.BirthYear), 70);
        Col("달", nameof(Row.Month), 36);
        Col("날", nameof(Row.Day), 36);
        Col("얼굴", nameof(Row.Face), 44);
        Col("나라", nameof(Row.Nation), 40);
        Col("혈액형", nameof(Row.Blood), 50);
        Col("직업", nameof(Row.Job), 40);
        Col("능력 여섯", nameof(Row.Abilities), 130);
        Col("기능 열셋", nameof(Row.Skills), 190);
        Col("언어 여섯", nameof(Row.Tongues), 96);
        Col("돈", nameof(Row.Gold), 64);
        Col("명성", nameof(Row.Fame), 56);

        _add.Click += (_, _) => Add();
        _remove.Click += (_, _) => Remove();
        _script.Click += (_, _) => OpenScript();
        _save.Click += (_, _) => Save();

        var bar = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(10, 0, 10, 0),
            Children = { _add, _remove, _script, _save },
        };

        var page = new DockPanel();
        DockPanel.SetDock(_status, Dock.Bottom);
        DockPanel.SetDock(bar, Dock.Bottom);
        page.Children.Add(_status);
        page.Children.Add(bar);
        page.Children.Add(_grid);
        Content = page;

        Loaded += (_, _) => { Rebuild(Scenarios.All); Guide(""); };
    }

    private static Button Push(string text) => new()
    {
        Content = text,
        Padding = new Thickness(10, 2, 10, 2),
        Margin = new Thickness(0, 0, 6, 0),
    };

    /// <summary>목록 한 줄 — 수 여럿인 칸은 쉼표로 이은 글로 고친다.</summary>
    private sealed class Row
    {
        public string Book { get; init; } = "";
        public string Title { get; set; } = "";
        public string Given { get; set; } = "";
        public string Family { get; set; } = "";
        public int BirthYear { get; set; }
        public int Month { get; set; }
        public int Day { get; set; }
        public int Face { get; set; }
        public int Nation { get; set; }
        public int Blood { get; set; }
        public int Job { get; set; }
        public string Abilities { get; set; } = "";
        public string Skills { get; set; } = "";
        public string Tongues { get; set; } = "";
        public int Gold { get; set; }
        public int Fame { get; set; }
    }

    private void Col(string header, string path, double width, bool readOnly = false) =>
        _grid.Columns.Add(new DataGridTextColumn
        {
            Header = header,
            Binding = new System.Windows.Data.Binding(path),
            Width = new DataGridLength(width),
            IsReadOnly = readOnly,
        });

    private void Rebuild(IEnumerable<Scenario> scenarios, string? pick = null)
    {
        _rows = [.. scenarios.Select(s => new Row
        {
            Book = s.Book, Title = s.Title, Given = s.Given, Family = s.Family,
            BirthYear = s.BirthYear, Month = s.Month, Day = s.Day,
            Face = s.Face, Nation = s.Nation, Blood = s.Blood, Job = s.Job,
            Abilities = string.Join(",", s.Abilities),
            Skills = string.Join(",", s.Skills),
            Tongues = string.Join(",", s.Tongues),
            Gold = s.Gold, Fame = s.Fame,
        })];
        _grid.ItemsSource = _rows;
        _grid.SelectedItem = _rows.FirstOrDefault(r => r.Book == pick) ?? _rows.FirstOrDefault();
    }

    /// <summary>칸의 뜻을 아래에 적어 둔다 — 번호로 적는 칸이 많다.</summary>
    private void Guide(string head)
    {
        _status.Text = (head.Length > 0 ? head + "\n" : "")
            + "나라 0 포르투갈 · 1 에스파니아   ·   직업 " + string.Join(" · ", Enumerable.Range(0, 4).Select(i => $"{i} {Job.Of(i).Name}"))
            + "   ·   혈액형 0~3   ·   얼굴은 운명 자리 번호(라몬 5 · 에밀리오 14)\n"
            + "능력 여섯: " + string.Join(",", Ability.Names) + $" ({Ability.Min}~{Ability.Max})\n"
            + "기능 열셋: " + string.Join(",", Skill.Names) + "\n"
            + "언어 여섯: " + string.Join(",", Skill.Languages) + "\n"
            + "게임은 1480년 1월 1일에 시작한다 — 나이는 태어난 해로 셈한다. 고친 뒤 「저장」을 눌러야 적힌다. 모드 「시나리오 모드」를 켜면 NEW GAME 에 나온다.";
    }

    /// <summary>줄들을 시나리오로 거둔다. 어긋난 칸이 있으면 null 이고 까닭을 아래에 적는다.</summary>
    private List<Scenario>? Collect()
    {
        _grid.CommitEdit(DataGridEditingUnit.Row, true);

        var made = new List<Scenario>();
        foreach (var row in _rows)
        {
            string at = $"「{row.Title}」";
            if (Numbers(row.Abilities, 6) is not { } abilities) { Guide($"{at} 능력은 수 여섯을 쉼표로 적습니다."); return null; }
            if (Numbers(row.Skills, Skill.Names.Length) is not { } skills) { Guide($"{at} 기능은 수 {Skill.Names.Length}개를 쉼표로 적습니다."); return null; }
            if (Numbers(row.Tongues, Skill.Languages.Length) is not { } tongues) { Guide($"{at} 언어는 수 {Skill.Languages.Length}개를 쉼표로 적습니다."); return null; }
            if (row.Nation is not (0 or 1)) { Guide($"{at} 나라는 0(포르투갈) 이나 1(에스파니아)입니다."); return null; }
            if (row.Job is < 0 or > 3) { Guide($"{at} 직업은 0~3 입니다."); return null; }
            if (row.Month is < 1 or > 12 || row.Day < 1 || row.Day > DateTime.DaysInMonth(2001, row.Month)) { Guide($"{at} 생일이 달력에 없습니다."); return null; }
            if (string.IsNullOrWhiteSpace(row.Given)) { Guide($"{at} 이름이 비었습니다."); return null; }

            made.Add(new Scenario(row.Book, row.Title.Trim(), row.Given.Trim(), row.Family.Trim(),
                row.BirthYear, row.Month, row.Day, Math.Max(0, row.Face), row.Nation, Math.Clamp(row.Blood, 0, 3), row.Job,
                [.. abilities.Select(a => Math.Clamp(a, Ability.Min, Ability.Max))],
                [.. skills.Select(s => Math.Max(0, s))], [.. tongues.Select(t => Math.Max(0, t))],
                Math.Max(0, row.Gold), Math.Max(0, row.Fame)));
        }
        return made;
    }

    /// <summary>쉼표로 이은 수 <paramref name="count"/> 개. 모자라거나 수가 아니면 null.</summary>
    private static int[]? Numbers(string text, int count)
    {
        var cells = text.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (cells.Length != count) return null;
        var values = new int[count];
        for (int i = 0; i < count; i++)
            if (!int.TryParse(cells[i], out values[i])) return null;
        return values;
    }

    /// <summary>줄을 새로 짓는다 — 이야기 책도 본보기 파트 하나를 든 채로 함께 생긴다.</summary>
    private void Add()
    {
        if (Collect() is not { } now) return;

        string book = Scenarios.NextBook(now);
        if (DisevBook.CreateBlank(book) == null)
        {
            Guide($"이야기 책을 못 지었습니다 — {DisevBook.LastError}");
            return;
        }
        now.Add(Scenarios.Blank(book));
        Scenarios.Save(now);
        Rebuild(now, book);
        Guide($"시나리오를 지었습니다 — 이야기 책 {DisevBook.PathOf(book)}");
    }

    /// <summary>고른 줄을 지운다. 이야기 책 파일도 함께 지운다.</summary>
    private void Remove()
    {
        if (_grid.SelectedItem is not Row row || Collect() is not { } now) return;
        if (!ConfirmDialog.Ask(this,
                $"「{row.Title}」 시나리오를 지웁니다. 이야기 책({row.Book}.json)에 적어 둔 대본도 함께 사라집니다. 좋습니까?",
                "시나리오 지우기"))
            return;

        now.RemoveAll(s => s.Book == row.Book);
        Scenarios.Save(now);
        try { File.Delete(DisevBook.PathOf(row.Book)); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        Rebuild(now);
        Guide($"「{row.Title}」 을 지웠습니다.");
    }

    private void Save()
    {
        if (Collect() is not { } now) return;
        string? keep = (_grid.SelectedItem as Row)?.Book;
        Scenarios.Save(now);
        Rebuild(now, keep);
        Guide($"적어 두었습니다 — 시나리오 {now.Count}편.");
    }

    /// <summary>고른 시나리오의 이야기 책을 대본 편집기로 연다. 고친 칸은 먼저 적어 둔다 — 편집기의 책 목록이 제목을 쓴다.</summary>
    private void OpenScript()
    {
        if (_grid.SelectedItem is not Row row || Collect() is not { } now) return;
        Scenarios.Save(now);
        DisevEditorDialog.Show(this, row.Book);
    }

    /// <summary>창을 띄운다.</summary>
    public static void Show(Window? owner)
    {
        var window = new ScenarioEditDialog();
        if (owner != null) window.Owner = owner;
        window.ShowDialog();
    }
}
