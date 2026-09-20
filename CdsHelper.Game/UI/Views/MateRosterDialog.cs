using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using CdsHelper.Game.Local.Helpers;
using CdsHelper.Support.Local.Models;

namespace CdsHelper.Game.UI.Views;

/// <summary>
/// 부하편성 창 — 부관·항해사·측량사·통역 네 자리를 보여 주고 서로 바꾸게 한다.
/// </summary>
/// <remarks>
/// 여관과 술집의 "부하편성" 으로 연다. 게임 화면을 그대로 옮겼다.
/// <code>
///   ┌ 부하편성 ────────────┐
///   │ 부관  : 후안·데·에스칸데 │
///   │ 항해사 : 나코다·이스마엘  │
///   │ 측량사 : 샤비에르·데·야소 │   <- 한 줄을 누르면 남색으로 잡힌다
///   │ 통역  : 안토니오·피가페따 │
///   └──────────────────┘
///        [결정]   [중단]
/// </code>
/// <b>두 줄을 눌러 자리를 맞바꾼다.</b> 먼저 누른 줄이 잡히고, 다음 줄을 누르면 둘이
/// 바뀌면서 잡힘이 풀린다. 같은 줄을 다시 누르면 그냥 놓는다.
///
/// 자리 이름은 게임 EXE 의 표(<c>0x00571038</c>) 차례 그대로다.
///
/// 바꾼 것은 <b>결정을 눌러야</b> 들어간다. 중단하면 들어올 때 그대로 되돌린다 —
/// 게임도 두 단추를 그렇게 가른다.
/// </remarks>
public sealed class MateRosterDialog : GameWindow
{
    /// <summary>줄 속 칸 — 자리 이름 · 콜론 · 사람. 자리 이름은 폭을 맞춰 콜론을 세로로 세운다.</summary>
    private static readonly GameListColumn[] Columns =
    [
        new(GameListDock.Left, new Thickness(6, 0, 6, 0), 64, HorizontalAlignment.Right),
        new(GameListDock.Left, new Thickness(8, 0, 8, 0)),
        new(GameListDock.Fill, new Thickness(6, 0, 6, 0)),
    ];

    private readonly Player _player;

    /// <summary>들어올 때의 자리. 중단하면 이대로 되돌린다.</summary>
    private readonly string[] _before;

    private readonly GameList _list;

    private MateRosterDialog(Player player)
    {
        _player = player;
        _before = [.. player.Mates];

        Title = "부하편성";
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.WidthAndHeight;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        Background = GameUi.Back;

        // 두 줄을 눌러 자리를 맞바꾼다. 자료를 바꾸는 것은 여기 몫이라 바뀐 뒤에 다시 그린다.
        _list = new GameList(Columns, Cells, Player.MaxMates) { Pick = GameListPick.Swap };
        _list.Swapped += (a, b) => { _player.SwapMates(a, b); _list.Refresh(); };

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 4, 0, 12),
        };
        buttons.Children.Add(new GameButton("결정", Decide, width: 110));
        buttons.Children.Add(new GameButton("중단", Cancel, width: 110));

        var title = GameUi.TitleBar("부하편성", Cancel);
        GameUi.EnableDrag(this, title);

        var stack = new StackPanel { MinWidth = 330 };
        stack.Children.Add(title);
        stack.Children.Add(_list);
        stack.Children.Add(buttons);

        Content = new Border
        {
            Background = GameUi.Back,
            BorderBrush = GameUi.Edge,
            BorderThickness = new Thickness(2),
            Margin = new Thickness(4),
            Child = stack,
        };

        KeyDown += (_, e) => { if (e.Key is Key.Escape) Cancel(); };
    }

    /// <summary>줄 하나의 칸 글자 — 자리 · 콜론 · 사람. 빈 자리는 이름을 비운다.</summary>
    private IReadOnlyList<string> Cells(int slot) =>
        [Player.MateRoles[slot], ":", _player.MateAt(slot)];

    private void Decide() => Close();

    /// <summary>들어올 때 자리로 되돌리고 닫는다.</summary>
    private void Cancel()
    {
        for (int i = 0; i < _before.Length; i++) _player.SetMate(i, _before[i]);
        Close();
    }

    /// <summary>부하편성 창을 연다.</summary>
    public static void Show(Window owner, Player player) =>
        new MateRosterDialog(player) { Owner = owner }.ShowDialog();
}
