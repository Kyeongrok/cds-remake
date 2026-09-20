namespace CdsHelper.Game.UI.Views;

/// <summary>
/// 한 마디 알리고 확인만 받는 창 — <see cref="ConfirmDialog"/> 를 부르는 <b>이름 하나</b>다.
/// </summary>
/// <remarks>
/// 본디 <b>물려받아 늘려 쓰는 밑바탕</b>으로 지었다(테 두 겹 · 제목 띠 · 끌기 · 단추 초점).
/// 그런데 물려받은 창이 한 벌도 없었고, 쓰이는 것은 <c>Show(글, 제목)</c> 하나뿐이었다 —
/// <see cref="NoticeDialog"/> 와 하는 일이 똑같은데 폭 셈만 달랐다(이쪽은 <c>620</c> 에서
/// 접고, 물음창은 게임 셈으로 늘인다). 그래서 같은 말이 자리마다 다른 폭으로 떴다.
///
/// 늘려 쓸 밑바탕이 필요하면 <see cref="InfoDialog"/> 가 있다 — 그쪽은 열둘이 물려받아
/// 쓰고 있다.
/// </remarks>
public static class GameDialog
{
    /// <param name="owner">알림을 얹을 창.</param>
    /// <param name="text">할 말.</param>
    /// <param name="title">제목 띠에 얹을 글. 비우면 띠를 안 단다.</param>
    public static void Show(System.Windows.Window owner, string text, string? title = null) =>
        ConfirmDialog.Tell(owner, text, title);
}
