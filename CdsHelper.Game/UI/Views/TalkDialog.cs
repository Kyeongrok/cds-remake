using System.Windows;
using CdsHelper.Game.Local.Helpers;

namespace CdsHelper.Game.UI.Views;

/// <summary>
/// 인물이 말하는 창 — 왼쪽에 얼굴, 오른쪽에 대사. 게임의 인물 대사 창(<c>0x004692E0</c>)이다.
/// </summary>
/// <remarks>
/// 게임은 대사 창을 따로 두지 않는다 — <c>0x004692E0</c> · <c>0x00469540</c> 이 다 <c>0x004691F0</c> →
/// <c>0x00478280</c> 을 거쳐 물음창과 같은 <c>0x0049D7B0</c> 을 얼굴을 붙여 짓는다. 폭은
/// <c>max(30, 가장 긴 줄) x 8 + 32 + 96</c>(<c>0x0049D9F5</c> ~ <c>0x0049DA21</c>)이고, 글은 얼굴 옆 고정 자리에서
/// 왼쪽맞춤으로 시작한다(<c>0x0049DFE8</c> — 얼굴이 있으면 가운데로 밀지 않는다). 그래서 여기서는
/// <see cref="ConfirmDialog"/> 를 얼굴과 함께 부르기만 한다.
///
/// 얼굴은 MALE.CDS · FEMALE.CDS 에서 온다(<see cref="Portraits"/>, 80x96 도트 그림).
/// 얼굴을 못 구하면 대사만 낸다 — 그림이 없다고 말까지 막을 일은 아니다.
/// </remarks>
public static class TalkDialog
{
    /// <summary>얼굴을 띄우고 한마디 한다. 확인만 받는다.</summary>
    /// <remarks>
    /// <b>게임 알림창을 그대로 쓴다</b>(<see cref="ConfirmDialog"/>) — 게임은 <b>글 길이에 맞춰 창을 늘인다</b>
    /// (칸수 = max(30, 가장 긴 줄), 너비 = 칸수 x 8 + 32, 얼굴이 서면 + 96).
    /// </remarks>
    public static void Say(Window owner, uint[]? face, string speaker, string text) =>
        ConfirmDialog.Tell(owner, text, speaker.Length > 0 ? speaker : null, face);

    /// <summary>
    /// 얼굴을 띄우고 물어본다. 고른 자리를 내고, 그냥 닫으면 -1 이다.
    /// </summary>
    /// <remarks>
    /// <b>대사와 고를 줄은 딴 창이다.</b> 게임은 먼저 대사 창을 내고(확인 하나), 확인을
    /// 누르면 그제야 세로로 선 메뉴를 낸다 — 술집에서 여성을 누르면 "아름다운 여성이 있다"
    /// 가 뜨고, 확인하면 "한잔 산다 · 무시한다" 가 뜨는 그 차례다.
    ///
    /// 예전에는 둘을 한 상자에 담아 글 밑에 단추를 가로로 늘어놓았다.
    ///
    /// 메뉴에서 물러나면(ESC · 오른쪽 단추) <b>마지막 줄</b>을 고른 것으로 친다 — 마지막
    /// 줄이 늘 "무시한다"·"떠난다" 같은 나가기 줄이기 때문이다.
    /// </remarks>
    public static int Ask(Window owner, uint[]? face, string speaker, string text,
                          params string[] choices)
    {
        if (text.Length > 0) Say(owner, face, speaker, text);
        if (choices.Length == 0) return -1;

        int picked = ChoiceDialog.Ask(owner, "", choices[..^1], choices[^1]);
        return picked >= 0 ? picked : choices.Length - 1;
    }
}
