using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using CdsHelper.Game.Local.Helpers;
using CdsHelper.Support.Local.Models;

namespace CdsHelper.Game.UI.Views;

/// <summary>
/// 커스텀 발견물의 힌트를 실을 책을 고르는 창 — 책마다 놓인 도서관 · 읽는 데 드는 언어 · 실린 힌트와 그 힌트를
/// 알아듣는 조건을 늘어놓아, 어느 책에 실어야 누가 언제 읽게 될지 보고 고르게 한다.
/// </summary>
/// <remarks>
/// 도서관은 <b>책의 언어가 3</b> 이라야 책을 펼친다(<see cref="BookTable"/>). 힌트마다는 기능 자리와 먼저 찾아 둘
/// 발견물이 더 붙는다(<see cref="BookTable.HintNeed"/>). 커스텀 발견물의 힌트는 기능 조건이 없다.
/// </remarks>
public sealed class BookPickerDialog : GameWindow
{
    private readonly ListBox _list = new() { Width = 260, Margin = new Thickness(10, 10, 6, 6) };
    private readonly TextBox _detail = new()
    {
        IsReadOnly = true, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(6, 10, 10, 6),
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto, BorderThickness = new Thickness(0), FontSize = 13,
    };

    private readonly Engine.Game? _game;
    private readonly List<BookTable.Book> _books;

    /// <summary>고른 책 제목. 고르지 않고 닫았으면 null.</summary>
    public string? Picked { get; private set; }

    private BookPickerDialog(Engine.Game? game, string current)
    {
        _game = game;
        Title = "책 고르기";
        Width = 860;
        Height = 620;
        Background = Brushes.White;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        // 같은 제목이 언어만 달리 여러 권이면 한 줄로 — 커스텀 힌트는 제목으로 실린다.
        _books = [.. (game?.Books?.Books ?? []).Where(b => b.Title.Length > 0).GroupBy(b => b.Title).Select(g => g.First())
                  .OrderBy(b => b.Title, StringComparer.Ordinal)];
        foreach (var book in _books)
            _list.Items.Add(new ListBoxItem { Content = $"{book.Title}  ({LanguageName(book.Language)})", Tag = book.Title });

        _list.SelectionChanged += (_, _) => { if (_list.SelectedItem is ListBoxItem { Tag: string title }) Describe(title); };
        _list.MouseDoubleClick += (_, _) => Choose();

        var choose = new Button { Content = "이 책에 싣기", Padding = new Thickness(12, 2, 12, 2), Margin = new Thickness(0, 0, 6, 0) };
        choose.Click += (_, _) => Choose();
        var cancel = new Button { Content = "취소", Padding = new Thickness(12, 2, 12, 2) };
        cancel.Click += (_, _) => Close();
        var bar = new StackPanel
        {
            Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(10, 0, 10, 10), Children = { choose, cancel },
        };

        var body = new DockPanel();
        DockPanel.SetDock(_list, Dock.Left);
        body.Children.Add(_list);
        body.Children.Add(_detail);

        var page = new DockPanel();
        DockPanel.SetDock(bar, Dock.Bottom);
        page.Children.Add(bar);
        page.Children.Add(body);
        Content = page;

        if (_books.Count == 0) _detail.Text = "책 표를 읽지 못했습니다 — 게임 안에서 개발 창을 열어 주세요.";
        _list.SelectedItem = _list.Items.OfType<ListBoxItem>().FirstOrDefault(i => (string)i.Tag == current)
                             ?? _list.Items.OfType<ListBoxItem>().FirstOrDefault();
        if (_list.SelectedItem != null) _list.ScrollIntoView(_list.SelectedItem);
    }

    private static string LanguageName(int language) =>
        language >= 0 && language < Skill.Languages.Length ? Skill.Languages[language] : "언어 없음";

    private void Choose()
    {
        if (_list.SelectedItem is not ListBoxItem { Tag: string title }) return;
        Picked = title;
        Close();
    }

    /// <summary>그 책의 자리 · 조건 · 실린 힌트를 적는다.</summary>
    private void Describe(string title)
    {
        var table = _game?.Books;
        var all = (table?.Books ?? []).Where(b => b.Title == title).ToList();
        if (all.Count == 0) { _detail.Text = ""; return; }
        var first = all[0];

        var text = new System.Text.StringBuilder();
        text.AppendLine($"『{first.Title}』  {first.Author}");
        text.AppendLine();
        text.AppendLine("■ 책을 읽는 조건");
        foreach (var book in all)
            text.AppendLine($"  {LanguageName(book.Language)} 3 이상 · {book.Year}년부터 서가에 나옴");
        text.AppendLine();
        text.AppendLine("■ 놓인 도서관");
        var cities = all.SelectMany(b => b.Cities).Where(c => c >= 0).Distinct().ToList();
        text.AppendLine(cities.Count == 0 ? "  (없음)" : "  " + string.Join(" · ", cities.Select(c => _game?.CityName(c) ?? $"도시 {c}")));
        text.AppendLine();
        text.AppendLine("■ 실린 힌트와 알아듣는 조건");
        var hints = all.SelectMany(b => b.Hints).Distinct().ToList();
        if (hints.Count == 0) text.AppendLine("  (없음)");
        foreach (int hint in hints)
        {
            string name = _game?.Hints?.NameOf(hint) ?? $"힌트 {hint}";
            var need = table?.NeedFor(hint) ?? new BookTable.HintNeed(-1, 0);
            var terms = new List<string>();
            if (need.Skill >= 0 && need.Skill < Skill.Names.Length) terms.Add($"{Skill.Names[need.Skill]} {need.Level} 이상");
            if (need.Parents is { Count: > 0 } parents)
                terms.Add("먼저 찾을 발견물: " + string.Join(", ", parents.Select(p => _game?.Discoveries?.Table?.NameOf(p) ?? $"발견물 {p}")));
            text.AppendLine($"  · {name}");
            text.AppendLine($"      {(terms.Count == 0 ? "조건 없음 — 책만 읽으면 들어옵니다" : string.Join(" · ", terms))}");
        }
        text.AppendLine();
        text.AppendLine("커스텀 발견물의 힌트는 기능 조건 없이, 책을 읽을 수 있으면 들어옵니다.");
        _detail.Text = text.ToString();
    }

    /// <summary>창을 띄워 책을 고르게 한다. 고른 제목, 안 골랐으면 null.</summary>
    public static string? Pick(Window owner, Engine.Game? game, string current)
    {
        var window = new BookPickerDialog(game, current) { Owner = owner };
        window.ShowDialog();
        return window.Picked;
    }
}
