using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using CdsHelper.Game.Local.Helpers;

namespace CdsHelper.Game.UI.Views;

/// <summary>
/// 신규 캐릭터 창의 <b>「이름 일람」</b> — 갖춰 둔 이름을 늘어놓고 하나를 고른다.
/// </summary>
/// <remarks>
/// 게임 갈무리 그대로다.
/// <code>
///   ┌ 이름 일람 ────────────┐
///   │ [ 베니토           ]▣ │  ← 고른 것이 위 칸에 적힌다(계산기로 직접 칠 수도)
///   │ 아르바레스           ↑ │
///   │ 안드레스             █ │
///   │ 베니토(고른 줄 남색)  │ │
///   │ …                  ↓ │
///   ├────────────────────┤
///   │    결정      중단     │
///   └────────────────────┘
/// </code>
/// 목록은 EXE 에 박힌 것이다(<see cref="PlayerNameTable"/>) — 성 마흔여덟,
/// 명 서른일곱이고 명은 국적에 따라 표기가 갈린다.
///
/// 예전에는 도시 건물 고르는 창(<see cref="MapPointDialog"/>)을 빌려 썼는데, 그쪽은
/// 줄마다 띠 단추가 서는 딴 모양이라 게임 것과 달랐다.
/// </remarks>
public sealed class NameListDialog : GameWindow
{
    /// <summary>줄 속 칸 — 이름 하나. 게임은 왼쪽맞춤이다.</summary>
    private static readonly GameListColumn[] Columns =
    [
        new(GameListDock.Fill, new Thickness(6, 0, 6, 0)),
    ];

    /// <summary>목록 바닥 폭과, 넘치면 굴리기 시작하는 키. 게임 갈무리를 재어 맞췄다.</summary>
    private const double ListWidth = 300, ListMaxHeight = 224;

    /// <summary>위 칸의 키와 아래 단추 폭.</summary>
    private const double FieldHeight = 18, ButtonWidth = 96;

    private readonly IReadOnlyList<string> _names;
    private readonly GameList _list;
    private readonly GameUi.GameLabel _typed;

    private string _picked = "";

    private NameListDialog(IReadOnlyList<string> names, string start)
    {
        _names = names;

        Title = "이름 일람";
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.WidthAndHeight;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        Background = GameUi.Back;

        // 위 칸 — 고른 이름이 여기 적힌다. 옆의 계산기로 손수 칠 수도 있다.
        _typed = new GameUi.GameLabel(GameFont.ButtonColor)
        {
            Text = start,
            FallbackBrush = System.Windows.Media.Brushes.Black,
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(4, 0, 4, 0),
        };

        var field = new DockPanel { LastChildFill = true, Margin = new Thickness(0, 0, 0, 3) };
        var pad = GameUi.CalcButton(TypeIt, FieldHeight);
        DockPanel.SetDock(pad, Dock.Right);
        field.Children.Add(pad);
        field.Children.Add(new Border
        {
            Background = GameUi.PageFill,
            BorderBrush = GameUi.ItemEdge,
            BorderThickness = new Thickness(1),
            Height = FieldHeight,
            Child = _typed,
        });

        _list = new GameList(Columns, i => [_names[i]], _names.Count, maxHeight: ListMaxHeight)
        {
            Margin = new Thickness(0),
            BorderBrush = GameUi.Edge,
        };
        _list.SelectionChanged += () =>
        {
            if (_list.Selected >= 0 && _list.Selected < _names.Count)
                _typed.Text = _names[_list.Selected];
        };

        // 들어올 때 적혀 있던 이름이 목록에 있으면 그 줄에 손을 올려 둔다.
        for (int i = 0; i < _names.Count; i++)
            if (_names[i] == start) { _list.Select(i); break; }

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 4, 0, 0),
        };
        buttons.Children.Add(new GameButton("결정", Decide, width: ButtonWidth));
        buttons.Children.Add(new GameButton("중단", Close, width: ButtonWidth));

        var title = GameUi.TitleBar("이름 일람", Close);
        GameUi.EnableDrag(this, title);

        var stack = new StackPanel { MinWidth = ListWidth };
        stack.Children.Add(title);
        stack.Children.Add(field);
        stack.Children.Add(_list);
        stack.Children.Add(buttons);

        Content = GameUi.DialogEdge(stack);
        KeyDown += OnKey;
    }

    /// <summary>계산기 단추 — 목록에 없는 이름을 손수 친다.</summary>
    private void TypeIt()
    {
        if (TextInputDialog.Ask(this, _typed.Text, NameLimit) is { } got) _typed.Text = got;
    }

    /// <summary>이름 한 칸에 들어갈 수 있는 길이. 신상 창과 같다.</summary>
    private const int NameLimit = 16;

    private void Decide()
    {
        if (_typed.Text.Trim().Length == 0) return;
        _picked = _typed.Text.Trim();
        DialogResult = true;
    }

    private void OnKey(object sender, KeyEventArgs e)
    {
        if (_list.HandleKey(e.Key)) { e.Handled = true; return; }

        switch (e.Key)
        {
            case Key.Escape:
                Close();
                break;
            case Key.Enter:
                Decide();
                e.Handled = true;
                break;
        }
    }

    /// <summary>
    /// 이름 일람을 연다. 고르면 그 이름, 그만두면 null.
    /// </summary>
    /// <param name="start">지금 칸에 적혀 있는 이름. 목록에 있으면 그 줄에 손이 간다.</param>
    public static string? Ask(Window owner, IReadOnlyList<string> names, string start)
    {
        if (names.Count == 0) return null;

        var dialog = new NameListDialog(names, start) { Owner = owner };
        return dialog.ShowDialog() == true ? dialog._picked : null;
    }
}
