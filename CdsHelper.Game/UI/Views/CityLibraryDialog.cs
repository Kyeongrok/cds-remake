using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using CdsHelper.Game.Local.Helpers;
using CdsHelper.Support.Local.Models;

namespace CdsHelper.Game.UI.Views;

/// <summary>
/// 세계지도 도시 오른쪽 단추 메뉴의 「도서관」 — 그 도시 도서관에 놓인 책과 책마다 실린 힌트를 늘어놓는다(요청, 모드 「도서관 책 보기」).
/// </summary>
/// <remarks>
/// 원본은 도서관에 들어가 서가를 열어야 책을 보고, 책을 펼쳐야 힌트를 안다 — 원본보다 많이 보여 주는 창이다.
/// 아직 서가에 안 나온 책(나오는 해가 지금보다 뒤)도 늘어놓되 그 해를 붙이고 흐리게 낸다. 책을 읽는 조건은
/// <see cref="BookTable"/> 그대로다(책의 언어 3 이상, 힌트마다 기능 · 먼저 찾을 발견물).
/// 모양은 모드 창 · 향상된 힌트 보기(<see cref="HintBrowserDialog"/>)와 같은 왼쪽 목록 · 오른쪽 설명 두 칸이다(요청).
/// </remarks>
public sealed class CityLibraryDialog : GameWindow
{
    /// <summary>목록 칸과 설명 칸의 너비, 두 칸의 높이.</summary>
    private const double ListWidth = 300, DetailWidth = 440, PaneHeight = 440;

    /// <summary>고른 줄 바탕.</summary>
    private static readonly Brush Picked = Frozen(Color.FromArgb(0x60, 0xFF, 0xFF, 0xFF));

    /// <summary>커서가 올라간 줄 바탕.</summary>
    private static readonly Brush Hover = Frozen(Color.FromArgb(0x28, 0xFF, 0xFF, 0xFF));

    /// <summary>목록 저자 글빛 — 제목보다 흐리게.</summary>
    private static readonly Brush AuthorInk = Frozen(Color.FromArgb(0xA0, 0xE8, 0xDC, 0xC8));

    private static Brush Frozen(Color c)
    {
        var b = new SolidColorBrush(c);
        b.Freeze();
        return b;
    }

    private readonly Engine.Game _game;
    private readonly int _year;
    private List<BookTable.Book> _books = [];
    private readonly List<BookTable.Book> _all;
    private readonly List<Border> _rows = [];
    private readonly StackPanel _list = new();

    /// <summary>
    /// 목록 거르기(요청) — 0 모든 책 · 1 힌트가 실린 책 · 2 아직 못 찾은 발견물의 힌트가 실린 책. 창을 다시 열어도 이어 간다.
    /// </summary>
    private static int _filter;

    private static readonly string[] FilterNames = ["모든 책", "힌트 있는 책", "못 찾은 힌트만"];
    private readonly StackPanel _detail = new();
    private int _at = -1;

    private CityLibraryDialog(Engine.Game game, int city)
    {
        _game = game;
        _year = game.Player.Date.Year;
        string cityName = game.CityName(city) is { Length: > 0 } n ? n : $"도시 {city}";
        string title = $"{Lang.T(cityName)} {Lang.T("도서관")}";
        Title = title;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.WidthAndHeight;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        Background = GameUi.Back;

        // 서가에 나온 책이 먼저, 그다음 나올 책 — 같은 무리 안에서는 나오는 해 · 번호 차례.
        _all = [.. (game.Books?.Books ?? []).Where(b => b.Title.Length > 0 && b.Cities.Contains(city))
            .OrderBy(b => b.Year > _year).ThenBy(b => b.Year).ThenBy(b => b.Index)];

        // 굴림대는 게임 것(GameUi.Scroller)이다 — 윈도 기본 막대가 판 위에서 튄다.
        var scroll = GameUi.Scroller(_list, PaneHeight);
        scroll.Width = ListWidth;
        scroll.Height = PaneHeight;
        scroll.Margin = new Thickness(12, 10, 8, 4);

        var side = new Border
        {
            Width = DetailWidth,
            Height = PaneHeight,
            Margin = new Thickness(0, 10, 12, 4),
            Padding = new Thickness(12, 10, 12, 10),
            Background = new SolidColorBrush(Color.FromArgb(0x30, 0, 0, 0)),
            BorderBrush = GameUi.Edge,
            BorderThickness = new Thickness(1),
            Child = GameUi.Scroller(_detail, PaneHeight - 22),
        };

        var body = new StackPanel { Orientation = Orientation.Horizontal };
        body.Children.Add(scroll);
        body.Children.Add(side);

        var head = GameUi.TitleBar(title, Close);
        GameUi.EnableDrag(this, head);

        var stack = new StackPanel();
        stack.Children.Add(head);
        stack.Children.Add(body);
        stack.Children.Add(new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 6, 0, 12),
            Children = { FilterButton(), new GameButton("닫기", Close, width: 110) },
        });

        Content = new Border
        {
            Background = GameUi.Back,
            BorderBrush = GameUi.Edge,
            BorderThickness = new Thickness(2),
            Margin = new Thickness(4),
            Child = stack,
        };

        KeyDown += OnKey;
        MouseRightButtonUp += (_, _) => Close();
        Fill();
    }

    /// <summary>거르기 단추 — 누를 때마다 모든 책 → 힌트 있는 책 → 못 찾은 힌트만 차례로 돈다.</summary>
    private GameButton FilterButton()
    {
        GameButton? button = null;
        button = new GameButton($"{Lang.T("보기")}: {Lang.T(FilterNames[_filter])}", () =>
        {
            _filter = (_filter + 1) % FilterNames.Length;
            button!.Text = $"{Lang.T("보기")}: {Lang.T(FilterNames[_filter])}";
            Fill();
        }, width: 190) { Margin = new Thickness(0, 0, 10, 0) };
        return button;
    }

    /// <summary>책이 거르기에 걸리는지 — 힌트가 실렸는지, 실린 힌트 가운데 발견물을 아직 못 찾은 것이 있는지.</summary>
    private bool Passes(BookTable.Book book) => _filter switch
    {
        1 => book.Hints.Any(id => _game.Hints?.Find(id) != null),
        2 => book.Hints.Any(id => _game.Hints?.Find(id) != null
                                  && !(_game.Discoveries?.IsHintFound(_game.Player, id) ?? false)),
        _ => true,
    };

    /// <summary>거른 책으로 목록을 다시 짓고 첫 책을 고른다.</summary>
    private void Fill()
    {
        _books = [.. _all.Where(Passes)];
        _rows.Clear();
        _list.Children.Clear();
        _at = -1;
        for (int i = 0; i < _books.Count; i++) _list.Children.Add(Row(i));
        if (_books.Count == 0)
        {
            _list.Children.Add(Text(_game.Books == null ? "책 표를 읽지 못했습니다."
                                    : _all.Count == 0 ? "이 도시 도서관에는 책이 없습니다."
                                    : "고른 보기에 맞는 책이 없습니다.", 14));
            _detail.Children.Clear();
            return;
        }
        Select(0);
    }

    private static string LanguageName(int language) =>
        language >= 0 && language < Skill.Languages.Length ? Skill.Languages[language] : "언어 없음";

    private static TextBlock Text(string text, double size, bool bold = false, double opacity = 1, Thickness? margin = null) => new()
    {
        Text = text,
        Foreground = GameUi.Text,
        FontSize = size,
        FontWeight = bold ? FontWeights.Bold : FontWeights.Normal,
        Opacity = opacity,
        TextWrapping = TextWrapping.Wrap,
        Margin = margin ?? new Thickness(0),
    };

    /// <summary>목록 한 줄 — 왼쪽 번호 · 제목, 오른쪽 언어(아직 안 나온 책은 나오는 해).</summary>
    private Border Row(int index)
    {
        var book = _books[index];
        bool later = book.Year > _year;

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var name = new TextBlock
        {
            Foreground = GameUi.Text,
            FontSize = 14,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center,
        };
        name.Inlines.Add(new System.Windows.Documents.Run($"{book.Index,3}  {Lang.T(book.Title)}"));
        // 저자는 제목 뒤에 흐리게 붙인다(요청) — 모르는 책은 안 붙인다.
        if (book.Author.Length > 0)
            name.Inlines.Add(new System.Windows.Documents.Run($"  {Lang.T(book.Author)}") { FontSize = 12, Foreground = AuthorInk });
        var right = new TextBlock
        {
            Text = later ? $"{book.Year}년부터" : Lang.T(LanguageName(book.Language)),
            Foreground = GameUi.Text,
            FontSize = 12.5,
            Opacity = 0.7,
            Margin = new Thickness(8, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
        };
        Grid.SetColumn(right, 1);
        grid.Children.Add(name);
        grid.Children.Add(right);

        var row = new Border
        {
            Padding = new Thickness(8, 4, 8, 4),
            Margin = new Thickness(0, 0, 0, 1),
            Background = Brushes.Transparent,
            Cursor = Cursors.Hand,
            Opacity = later ? 0.55 : 1,
            Child = grid,
        };
        row.MouseEnter += (_, _) => { if (index != _at) row.Background = Hover; };
        row.MouseLeave += (_, _) => { if (index != _at) row.Background = Brushes.Transparent; };
        row.MouseLeftButtonDown += (_, e) => { Select(index); e.Handled = true; };
        _rows.Add(row);
        return row;
    }

    /// <summary>그 책을 고르고 오른쪽에 조건과 실린 힌트(이름 · 갈래 · 등급 · 알아듣는 조건 · 글)를 편다.</summary>
    private void Select(int index)
    {
        if (index < 0 || index >= _books.Count) return;
        if (_at >= 0 && _at < _rows.Count) _rows[_at].Background = Brushes.Transparent;
        _at = index;
        _rows[index].Background = Picked;
        _rows[index].BringIntoView();

        var book = _books[index];
        var table = _game.Books;
        _detail.Children.Clear();
        _detail.Children.Add(Text(book.Author.Length > 0 ? $"『{Lang.T(book.Title)}』 {Lang.T(book.Author)}" : $"『{Lang.T(book.Title)}』",
            16, bold: true, margin: new Thickness(0, 0, 0, 4)));
        string when = book.Year > _year ? $"{book.Year}년부터 서가에 나옵니다(지금 {_year}년)" : $"{book.Year}년부터 서가에 있습니다";
        _detail.Children.Add(Text($"{Lang.T(LanguageName(book.Language))} 3 이상 · {when}", 12.5, opacity: 0.75,
            margin: new Thickness(0, 0, 0, 10)));

        var hints = book.Hints.Distinct().ToList();
        if (hints.Count == 0) _detail.Children.Add(Text("실린 힌트가 없습니다.", 14));
        foreach (int id in hints)
        {
            var hint = _game.Hints?.Find(id);
            string category = hint is { } c ? _game.Hints!.CategoryOf(c.Category) : "";
            string name = hint?.Name ?? $"힌트 {id}";
            string have = _game.Player.HasHint(id) ? "  ✔" : "";
            _detail.Children.Add(Text(category.Length > 0 ? $"{name}({category}){have}" : $"{name}{have}", 14.5, bold: true,
                margin: new Thickness(0, 6, 0, 2)));

            var facts = new List<string>();
            if (hint is { } h) facts.Add($"등급 {h.Grade}");
            var need = table?.NeedFor(id) ?? new BookTable.HintNeed(-1, 0);
            if (need.Skill >= 0 && need.Skill < Skill.Names.Length) facts.Add($"{Skill.Names[need.Skill]} {need.Level} 이상");
            if (need.Parents is { Count: > 0 } parents)
                facts.Add("먼저 찾을 발견물: " + string.Join(", ", parents.Select(p => _game.Discoveries?.Table?.NameOf(p) ?? $"발견물 {p}")));
            if (need.Skill < 0 && need.Parents is not { Count: > 0 }) facts.Add("조건 없음");
            if (_game.Player.HasHint(id)) facts.Add("힌트");
            _detail.Children.Add(Text(string.Join(" · ", facts), 12.5, opacity: 0.75, margin: new Thickness(0, 0, 0, 4)));

            if (hint is { Text.Length: > 0 } withText)
                _detail.Children.Add(new TextBlock
                {
                    Text = withText.Text,
                    Foreground = GameUi.Text,
                    FontSize = 14,
                    LineHeight = 22,
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 0, 0, 8),
                });
        }
    }

    private void OnKey(object sender, KeyEventArgs e)
    {
        switch (GameUi.Arrow(e.Key))
        {
            case Key.Escape: Close(); break;
            case Key.Up: Select(Math.Max(0, _at - 1)); break;
            case Key.Down: Select(Math.Min(_books.Count - 1, _at + 1)); break;
            default: return;
        }
        e.Handled = true;
    }

    /// <summary>그 도시 도서관의 책 창을 띄운다(모달이 아니다 — 지도를 그대로 쓸 수 있다).</summary>
    public static void Show(Window owner, Engine.Game game, int city)
    {
        var window = new CityLibraryDialog(game, city) { Owner = owner };
        window.Show();
    }
}
