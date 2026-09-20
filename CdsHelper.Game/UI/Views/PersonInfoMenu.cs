using System.Windows;
using CdsHelper.Game.Engine.Menu;
using CdsHelper.Support.Local.Models;

namespace CdsHelper.Game.UI.Views;

/// <summary>
/// 인물정보 — <b>부하가 하나라도 있으면</b> 게임처럼 누구를 볼지 먼저 묻고,
/// 아무도 없으면 곧바로 제독의 판을 낸다.
/// </summary>
/// <remarks>
/// 바다에서도 도시에서도 같은 판이라 한 벌만 둔다. 예전에는 도시 창만 묻고 지도 창은
/// 곧바로 제독 판을 냈다.
/// </remarks>
internal static class PersonInfoMenu
{
    /// <param name="owner">판을 띄울 창.</param>
    /// <param name="game">이 판.</param>
    /// <param name="menu">이 줄을 낸 커맨드 창. 판을 띄우는 동안 감춰 두고, 물을 때는 그 위에 한 겹 쌓는다.</param>
    /// <param name="hold">
    /// 판이 떠 있는 동안 <b>붙잡아 달라</b>고 부르는 쪽에 알리는 손. 지도 창은 이것으로 멈춤을
    /// 쥐고 있는다 — 창을 접으면 그 알림이 멈춤을 풀어 버려, 인물정보를 보는 사이에 배가
    /// 계속 나아갔다.
    /// </param>
    public static void Show(Window owner, Engine.Game game, GameMenuHost menu,
                            Action<bool>? hold = null)
    {
        if (game.Player.MateCount == 0)
        {
            Held(hold, menu, () => PersonInfoDialog.Show(owner, game.Player, game.Directory));
            return;
        }
        menu.Push(() => Build(owner, game, menu, hold));
    }

    /// <summary>
    /// 붙잡아 달라고 알리고 판을 띄운다. 판이 떠 있는 동안 커맨드 창은 <b>감춰 둘 뿐</b>이다 — 판을 닫으면
    /// 고르던 창으로 되돌아온다(<c>0x0046E0BA</c> 가 고르기로 되뛴다).
    /// </summary>
    private static void Held(Action<bool>? hold, GameMenuHost menu, Action show)
    {
        hold?.Invoke(true);
        var window = menu.Window;
        if (window != null) window.Visibility = Visibility.Hidden;
        try { show(); }
        finally
        {
            if (window != null && window.IsLoaded)
            {
                window.Visibility = Visibility.Visible;
                window.Activate();
            }
            hold?.Invoke(false);
        }
    }

    /// <summary>
    /// 누구의 인물정보를 볼지 고르는 창 — 제독과 부하 네 자리다.
    /// </summary>
    /// <remarks>
    /// <b>사람이 앉은 자리만 낸다.</b> 예전에는 넷을 다 내고 빈 자리를 흐려 두었는데,
    /// 없는 자리를 굳이 보일 까닭이 없다.
    /// </remarks>
    private static GameMenu Build(Window owner, Engine.Game game, GameMenuHost menu,
                                  Action<bool>? hold = null)
    {
        var rows = new List<(string, Action?)>
        {
            ("플레이어", () => Held(hold, menu, () => PersonInfoDialog.Show(owner, game.Player, game.Directory))),
        };

        for (int i = 0; i < Player.MaxMates; i++)
        {
            int slot = i;
            string name = game.Player.MateAt(slot);
            if (name.Length == 0) continue;
            rows.Add((Player.MateRoles[slot], () => ShowMate(owner, game, menu, slot, hold)));
        }

        // 「취소」면 정보 차림표로 되돌아간다 — 고르기는 취소할 때까지 되풀이한다(0x0046E0BA).
        rows.Add(("취소", menu.Pop));
        return new GameMenu("", null, [.. rows]);
    }

    /// <summary>
    /// 그 자리에 앉은 부하의 인물정보 판.
    /// </summary>
    /// <remarks>
    /// 신상은 판이 찾아 준다(<see cref="Engine.Game.MateInfo"/>) — 우리 세이브를 먼저 보고,
    /// 없으면 게임 세이브의 인물표에서 채운다. 채울 데가 없으면 못 찾았다고 알린다.
    /// </remarks>
    private static void ShowMate(Window owner, Engine.Game game, GameMenuHost menu, int slot,
                                 Action<bool>? hold = null)
    {
        string name = game.Player.MateAt(slot);
        var who = game.MateInfo(name);

        Held(hold, menu, () =>
        {
            if (who is { } mate)
                PersonInfoDialog.ShowMate(owner, mate, Engine.GameInfo.SheetOf(game, mate), game.Directory);
            else
                NoticeDialog.Show(owner, $"{name}의 자료를 찾지 못했다");
        });
    }
}
