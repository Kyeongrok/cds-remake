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
///
/// 결정을 누르면 <b>부관·통역 자리의 사람이 제독과 말이 3 이상 통하는지</b> 본다
/// (<c>0x00453F6D</c> ~ <c>0x00453FCD</c>, <c>0x00478050(제독, 그 사람)</c>). 안 통하면
/// 「말이 통하지 않는 자는 부관(통역)이 될 수 없습니다!」를 내고 창으로 되돌아간다 — 창을 안 닫는다.
///
/// 사람이 앉은 줄을 <b>오른쪽 단추</b>로 누르면 그 이름을 제목으로 「정보를 본다 / 그만둔다」
/// (<c>0x0055AB10</c> · <c>0x0055AB20</c>) 두 줄 창이 뜨고, 「정보를 본다」면 그 사람의 인물정보 판
/// (<c>0x0046DBC0</c>)이 열린다(<c>0x00453970</c> — 목록 틀 <c>0x004B52D8</c> 이 사건 2 에 부른다).
/// 빈 자리는 아무 일도 없다.
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

    /// <summary>말을 잴 인물 표. 없으면 막지 않는다.</summary>
    private readonly IReadOnlyList<PersonTable.Row>? _people;

    /// <summary>부관 · 통역 자리와, 거기 앉는 데 드는 말 수준(<c>0x00453F8B</c> 의 <c>cmp eax, 3</c>).</summary>
    private const int FirstMateSlot = 0, InterpreterSlot = 3, FluentTongue = 3;

    /// <summary>들어올 때의 자리. 중단하면 이대로 되돌린다.</summary>
    private readonly string[] _before;

    private readonly GameList _list;

    /// <summary>인물정보 판을 찾을 게임. 없으면 오른쪽 단추가 아무 일도 안 한다.</summary>
    private readonly Engine.Game? _game;

    private MateRosterDialog(Player player, IReadOnlyList<PersonTable.Row>? people, Engine.Game? game)
    {
        _player = player;
        _game = game;
        _people = people;
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
        if (_game != null) _list.RowRightClicked += AskInfo;

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

    /// <summary>부관 · 통역 차례로 말을 재고(<c>0x00453F6D</c> · <c>0x00453FA1</c>), 다 되면 닫는다.</summary>
    private void Decide()
    {
        foreach (int slot in (int[])[FirstMateSlot, InterpreterSlot])
        {
            if (Tongue(_player.MateAt(slot)) >= FluentTongue) continue;
            GameDialog.Show(this, slot == FirstMateSlot
                ? "말이 통하지 않는 자는 부관이 될 수 없습니다!"      // 0x0055AB40
                : "말이 통하지 않는 자는 통역이 될 수 없습니다!");    // 0x0055AB70
            return;
        }
        Close();
    }

    /// <summary>
    /// 제독과 그 사람이 함께 잘하는 말의 수준 — 언어 열넷마다 낮은 쪽의 가장 큰 값(<c>0x00478050</c>).
    /// 빈 자리거나 표에 없으면 막지 않는다.
    /// </summary>
    private int Tongue(string name)
    {
        if (name.Length == 0 || _people?.FirstOrDefault(r => r.Name == name) is not { } row) return FluentTongue;
        int best = 0;
        for (int i = 0; i < Math.Min(row.Languages.Length, Skill.Languages.Length); i++)
            best = Math.Max(best, Math.Min(row.Languages[i], _player.TongueOf(Skill.Languages[i])));
        return best;
    }

    /// <summary>
    /// 오른쪽 단추 — 「정보를 본다 / 그만둔다」를 묻고 인물정보 판을 연다(<c>0x00453970</c>).
    /// 제목은 그 사람 이름이다(<c>0x004539ED</c> 가 이름을 <c>0x00469C40</c> 에 넘긴다).
    /// </summary>
    private void AskInfo(int slot)
    {
        if (_game == null) return;
        string name = _player.MateAt(slot);
        if (name.Length == 0) return;                                   // 0x004539B3 — 빈 자리
        if (ChoiceDialog.Pick(this, name, ["정보를 본다", "그만둔다"]) != 0) return;

        if (_game.MateInfo(name) is { } mate)
            PersonInfoDialog.ShowMate(this, mate, Engine.GameInfo.SheetOf(_game, mate), _game.Directory);
        else
            NoticeDialog.Show(this, $"{name}의 자료를 찾지 못했다");
    }

    /// <summary>들어올 때 자리로 되돌리고 닫는다.</summary>
    private void Cancel()
    {
        for (int i = 0; i < _before.Length; i++) _player.SetMate(i, _before[i]);
        Close();
    }

    /// <summary>부하편성 창을 연다.</summary>
    /// <param name="people">부관·통역의 말을 잴 인물 표(<c>Game.World.People</c>). 없으면 막지 않는다.</param>
    /// <param name="game">오른쪽 단추의 인물정보 판을 찾을 게임. 없으면 그 단추가 쉰다.</param>
    public static void Show(Window owner, Player player, IReadOnlyList<PersonTable.Row>? people = null,
                            Engine.Game? game = null) =>
        new MateRosterDialog(player, people, game) { Owner = owner }.ShowDialog();
}
