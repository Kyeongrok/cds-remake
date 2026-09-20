using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using CdsHelper.Game.Engine.Menu;
using CdsHelper.Game.Local.Helpers;

namespace CdsHelper.Game.UI.Views;

/// <summary>
/// 줄 몇 개를 늘어놓고 하나를 고르게 하는 <b>모달</b> 창 — 게임 커맨드 창과 같은 모습이다.
/// </summary>
/// <remarks>
/// <see cref="GameMenu"/> 는 눌렀을 때 할 일을 줄마다 들고 있는 <b>안 멈추는</b> 창이라,
/// "골라 올 때까지 기다리는" 자리에는 못 쓴다. 첫 화면의 NEW GAME 처럼 <b>대답을 받아
/// 와야</b> 하는 곳에 이것을 쓴다.
/// <code>
///   NEW GAME
///     초심자용 주인공으로 시작한다(EASY)
///     새로운 주인공으로 시작한다(NORMAL)
///     취소
/// </code>
/// 마지막 줄은 <see cref="GameMenu"/> 가 알아서 회녹색 띠로 낸다 — 게임도 나가기 줄을
/// 그렇게 갈라 놓는다.
/// </remarks>
internal sealed class ChoiceDialog : GameWindow
{
    private int _picked = -1;

    private ChoiceDialog(string title, IReadOnlyList<(string Text, bool On)> rows, bool exitRow)
    {
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.WidthAndHeight;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        Background = GameUi.Back;

        var items = new List<GameMenuRow>();
        for (int i = 0; i < rows.Count; i++)
        {
            int pick = i;
            // 꺼진 줄은 <b>자리를 지킨 채</b> 죽는다 — 할 일을 안 주면 GameMenu 가 흐린
            // 단추로 낸다. 게임도 넉 줄을 먼저 깔고 그 뒤에 켜고 끈다(0x004A5726).
            // 나가기 줄이 없는 창은 <b>마지막 줄도 단추</b>다 — 안 그러면 GameMenu 가 끝 줄을
            // 회녹색 나가기 띠로 낸다. 싸움의 공격명령(끝이 「애니메이션」)과 묘책(끝이
            // 「심판」)이 그런 창이라, 그 줄들은 나가기가 아니라 여느 명령이다.
            items.Add(new GameMenuRow(rows[i].Text,
                                      rows[i].On ? () => { _picked = pick; Close(); } : null,
                                      exitRow ? null : BandStyle.Button));
        }

        var box = new GameMenu(title, items);
        var frame = new Border { Background = GameUi.Back, Child = box };
        GameUi.EnableDrag(this, frame);
        Content = frame;

        KeyDown += (_, e) => { if (e.Key is Key.Escape) Close(); };
        MouseRightButtonUp += (_, _) => Close();
    }

    private ChoiceDialog(string title, IReadOnlyList<string> rows, string? cancel, int dim)
    {
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.WidthAndHeight;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        // 네모진 창이라 비침이 필요 없다 — 레이어드 창은 겹칠 때마다 깜빡인다.
        Background = GameUi.Back;

        var items = new List<GameMenuRow>();
        for (int i = 0; i < rows.Count; i++)
        {
            int pick = i;
            // 나가기 줄이 없는 창은 <b>마지막 줄도 단추</b>다 — 안 그러면 GameMenu 가
            // 끝 줄을 회녹색 나가기 띠로 낸다. 스폰서의 승낙/교섭이 그런 창이다.
            items.Add(new GameMenuRow(rows[i], () => { _picked = pick; Close(); },
                                      cancel == null ? BandStyle.Button : null,
                                      Dim: i == dim));
        }
        if (cancel != null) items.Add(new GameMenuRow(cancel, Close));

        var menu = new GameMenu(title, items);
        var root = new Border { Background = GameUi.Back, Child = menu };
        GameUi.EnableDrag(this, root);
        Content = root;

        KeyDown += (_, e) => { if (e.Key is Key.Escape) Close(); };
        MouseRightButtonUp += (_, _) => Close();
    }

    /// <summary>
    /// 창을 띄우고 고른 줄 번호를 낸다. 물렀으면 -1.
    /// </summary>
    /// <param name="owner">주인 창.</param>
    /// <param name="title">제목 줄.</param>
    /// <param name="rows">고를 줄들.</param>
    /// <param name="cancel">마지막 나가기 줄의 글.</param>
    /// <param name="dim">
    /// 그 자리의 줄을 <b>죽은 줄</b>로 낸다 — 흐리게 깔고 못 고르게 한다. −1 이면 없다.
    /// </param>
    public static int Ask(Window owner, string title, IReadOnlyList<string> rows,
                          string cancel = "취소", int dim = -1)
    {
        var dialog = new ChoiceDialog(title, rows, cancel, dim) { Owner = owner };
        dialog.ShowDialog();
        return dialog._picked;
    }

    /// <summary>
    /// 나가기 줄 없이 <b>줄이 모두 같은 단추</b>인 고르기 창. 물렀으면 -1.
    /// </summary>
    /// <remarks>
    /// 스폰서의 계약 제안이 이 모양이다 — 제목 띠에 「기간N년 금화 N닢」이 서고 그 아래
    /// 승낙한다·교섭한다 두 줄이 같은 무늬로 놓인다. 게임은 이 창을
    /// <c>0x00469A70</c> 으로 낸다(<c>0x004AF22A</c> 가 줄 수 2 를 넘긴다).
    /// </remarks>
    public static int Pick(Window owner, string title, IReadOnlyList<string> rows)
    {
        var dialog = new ChoiceDialog(title, rows, null, dim: -1) { Owner = owner };
        dialog.ShowDialog();
        return dialog._picked;
    }

    /// <summary>
    /// 줄마다 <b>켜고 끌 수 있는</b> 고르기 창. 물렀으면 -1.
    /// </summary>
    /// <remarks>
    /// 적대 도시의 공격·잠입·교섭·떠난다가 이 모양이다(<c>0x004A56F0</c>) — 꺼진 줄도
    /// 자리를 안 비우므로 <b>고른 값이 곧 붙박이 번호</b>다. 마지막 줄은 <see cref="GameMenu"/>
    /// 가 알아서 회녹색 나가기 띠로 낸다(적대 차림표의 「떠난다」가 그 자리다).
    /// </remarks>
    /// <param name="place">
    /// 자리를 손으로 잡을 때 부른다 — 안 주면 여느 때처럼 주인 창 가운데다. 싸움터가
    /// 가리면 안 되는 자리(<see cref="GameUi.PlaceAtCorner"/>)에 쓴다.
    /// </param>
    /// <param name="exitRow">
    /// 마지막 줄을 <b>나가기 띠</b>로 낼지. 싸움 차림표처럼 끝 줄도 여느 명령인 창은
    /// 거짓으로 준다.
    /// </param>
    public static int Pick(Window owner, string title, IReadOnlyList<(string Text, bool On)> rows,
                           Action<Window>? place = null, bool exitRow = true)
    {
        var dialog = new ChoiceDialog(title, rows, exitRow) { Owner = owner };
        place?.Invoke(dialog);
        dialog.ShowDialog();
        return dialog._picked;
    }
}
