using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using CdsHelper.Game.Local.Settings;

namespace CdsHelper.Game.UI.Views;

/// <summary>
/// 모드 「향상된 힌트 보기」 — 취득 힌트를 <b>왼쪽 목록</b>에 늘어놓고, 한 줄을 누르면 그 이야기를 <b>오른쪽</b>에 곧바로 편다.
/// </summary>
/// <remarks>
/// 원본은 목록에서 한 줄을 고르고 결정을 눌러야 파란 판(<see cref="HintDetailDialog"/>)이 뜨고, 닫으면 목록으로 되돌아온다.
/// 여러 힌트를 훑어보기 번거로워 모드 창처럼 목록과 설명을 한 창에 나란히 둔다. 고르는 것만 바뀌고 목록(살아 있는 힌트)과
/// 글(힌트 표의 글 · 부관의 평)은 원본 창과 같다. 모드를 끄면 원본 창으로 돌아간다.
/// </remarks>
public sealed class HintBrowserDialog : GameWindow
{
    /// <summary>목록 칸과 설명 칸의 너비, 두 칸의 높이.</summary>
    private const double ListWidth = 250, DetailWidth = 340, PaneHeight = 340;

    /// <summary>고른 줄 바탕.</summary>
    private static readonly Brush Picked = Frozen(Color.FromArgb(0x60, 0xFF, 0xFF, 0xFF));

    /// <summary>커서가 올라간 줄 바탕.</summary>
    private static readonly Brush Hover = Frozen(Color.FromArgb(0x28, 0xFF, 0xFF, 0xFF));

    private static Brush Frozen(Color c)
    {
        var b = new SolidColorBrush(c);
        b.Freeze();
        return b;
    }

    private readonly Engine.Game _game;
    private readonly IReadOnlyList<int> _ids;
    private readonly List<Border> _rows = [];
    private readonly ScrollViewer _scroll;
    private int _at = -1;

    private readonly TextBlock _head = new()
    {
        Foreground = GameUi.Text,
        FontWeight = FontWeights.Bold,
        FontSize = 16,
        TextWrapping = TextWrapping.Wrap,
        Margin = new Thickness(0, 0, 0, 4),
    };

    private readonly TextBlock _facts = new()
    {
        Foreground = GameUi.Text,
        FontSize = 12.5,
        Opacity = 0.75,
        TextWrapping = TextWrapping.Wrap,
        Margin = new Thickness(0, 0, 0, 10),
    };

    private readonly TextBlock _body = new()
    {
        Foreground = GameUi.Text,
        FontSize = 14,
        LineHeight = 22,
        TextWrapping = TextWrapping.Wrap,
    };

    private HintBrowserDialog(Engine.Game game, IReadOnlyList<int> ids)
    {
        _game = game;
        _ids = ids;
        Title = "취득 힌트 일람";
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.WidthAndHeight;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        Background = GameUi.Back;

        var list = new StackPanel();
        for (int i = 0; i < ids.Count; i++) list.Children.Add(Row(i));

        _scroll = new ScrollViewer
        {
            Width = ListWidth,
            Height = PaneHeight,
            Margin = new Thickness(12, 10, 8, 4),
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content = list,
        };

        var detail = new StackPanel();
        detail.Children.Add(_head);
        detail.Children.Add(_facts);
        detail.Children.Add(_body);

        var side = new Border
        {
            Width = DetailWidth,
            Height = PaneHeight,
            Margin = new Thickness(0, 10, 12, 4),
            Padding = new Thickness(12, 10, 12, 10),
            Background = new SolidColorBrush(Color.FromArgb(0x30, 0, 0, 0)),
            BorderBrush = GameUi.Edge,
            BorderThickness = new Thickness(1),
            Child = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Content = detail,
            },
        };

        var body = new StackPanel { Orientation = Orientation.Horizontal };
        body.Children.Add(_scroll);
        body.Children.Add(side);

        var title = GameUi.TitleBar("취득 힌트 일람", Close);
        GameUi.EnableDrag(this, title);

        var stack = new StackPanel();
        stack.Children.Add(title);
        stack.Children.Add(body);
        stack.Children.Add(new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 6, 0, 12),
            Children = { new GameButton("닫기", Close, width: 110) },
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
        Select(0);
    }

    /// <summary>목록 한 줄 — 왼쪽 이름, 오른쪽 갈래.</summary>
    private Border Row(int index)
    {
        int id = _ids[index];
        string category = _game.Hints?.Find(id) is { } hint ? _game.Hints.CategoryOf(hint.Category) : "";

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var name = new TextBlock
        {
            Text = _game.HintName(id),
            Foreground = GameUi.Text,
            FontSize = 14,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center,
        };
        var right = new TextBlock
        {
            Text = category,
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
            Child = grid,
        };
        row.MouseEnter += (_, _) => { if (index != _at) row.Background = Hover; };
        row.MouseLeave += (_, _) => { if (index != _at) row.Background = Brushes.Transparent; };
        row.MouseLeftButtonDown += (_, e) => { Select(index); e.Handled = true; };
        _rows.Add(row);
        return row;
    }

    /// <summary>그 줄을 고르고 오른쪽에 편다.</summary>
    private void Select(int index)
    {
        if (index < 0 || index >= _ids.Count) return;
        if (_at >= 0 && _at < _rows.Count) _rows[_at].Background = Brushes.Transparent;
        _at = index;
        _rows[index].Background = Picked;
        _rows[index].BringIntoView();

        var player = _game.Player;
        int id = _ids[index];
        if (_game.Hints?.Find(id) is not { } hint)
        {
            _head.Text = _game.HintName(id);
            _facts.Text = "";
            _body.Text = "";
            return;
        }

        string category = _game.Hints.CategoryOf(hint.Category);
        _head.Text = category.Length > 0 ? $"{hint.Name}({category})" : hint.Name;
        // 등급 · 자금 · 기한은 정보 제공 등급 「일반」부터 — 「기본」은 원본 판처럼 이름과 이야기뿐이다.
        var facts = new List<string>();
        if (Local.Settings.GameSettings.Shows(Local.Settings.GameSettings.InfoNormal))
            facts.AddRange([$"등급 {hint.Grade}", $"자금 {hint.Funds:N0}", $"기한 {hint.Deadline}년"]);
        // 원본 판 아래 말 창에 뜨던 부관의 평(「발견할 수 있을 것 같습니다」 따위)은 안 붙인다 — 훑어보는 데 거슬린다는 요청.
        // 지금 좇는 계약만은 알 만하니 기한 줄 끝에 단다.
        if (player.Contract?.Hint == hint.Id) facts.Add("현재 계약중");
        _facts.Text = string.Join(" · ", facts);
        _facts.Visibility = facts.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        _body.Text = hint.Text;
    }

    private void OnKey(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Escape: Close(); break;
            case Key.Up: Select(Math.Max(0, _at - 1)); break;
            case Key.Down: Select(Math.Min(_ids.Count - 1, _at + 1)); break;
            default: return;
        }
        e.Handled = true;
    }

    /// <summary>
    /// 살아 있는 힌트를 띄운다. 없으면 원본처럼 「설득 가능한 힌트가 없습니다」.
    /// </summary>
    public static void Show(Window owner, Engine.Game game, IReadOnlyList<int> ids)
    {
        if (ids.Count == 0)
        {
            NoticeDialog.Show(owner, "설득 가능한 힌트가 없습니다");
            return;
        }
        new HintBrowserDialog(game, ids) { Owner = owner }.ShowDialog();
    }

    /// <summary>모드 「향상된 힌트 보기」가 켜졌는지.</summary>
    /// <remarks>작위 모드를 켰으면 남작 혜택이 정한다(<see cref="Engine.Town.Nobility.Effective"/>).</remarks>
    public static bool IsOn(Support.Local.Models.Player player) =>
        Engine.Town.Nobility.Effective(player, GameSettings.HintBrowser, Engine.Town.Nobility.Perk.HintView);
}
