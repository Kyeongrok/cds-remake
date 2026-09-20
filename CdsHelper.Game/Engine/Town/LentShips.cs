using CdsHelper.Support.Local.Models;

namespace CdsHelper.Game.Engine.Town;

/// <summary>
/// 후원자를 <b>처벌하고 나설 때</b> 그가 빌려준 배들이 나를 따를지 가른다(<c>0x004101B0</c>).
/// </summary>
/// <remarks>
/// 계약이 끝나 후원자 건물을 나서면 게임은 빌린 배를 두 갈래로 나눈다(<c>0x004105A0</c>).
/// <code>
///   0040fe40  여느 후원자의 배 — 그냥 돌려준다
///   004101b0  나를 <b>처벌한</b> 후원자(인물 비트 13)의 배 — 돌려줄 상대가 없으니 따를지 굴린다
/// </code>
/// 뒤쪽이 이 클래스다. 차례는 이렇다.
/// <code>
///   0040e1f0  함대에 타고 있는 그 후원자 배가 하나라도 있어야 한다
///   004103c0  부하들이 순순히 따르는가                       ; Obeys
///   0040ffc0  안 따르면 「선장」이 나서서 일기토를 건다        ; 지면 게임오버
///   004104b0  함대가 <b>전부</b> 그 배들이면 한 척은 남는다   ; KeepsOne
///   00410400  배마다 운으로 굴려 남을지 정한다                ; Stays
///   00410380  남으면 대출 표시를 지우고 <b>내 배</b>가 된다
///   0040ff60  아니면 함대에서 빠지며 선원·짐을 몫만큼 가져간다
/// </code>
/// <b>탈주를 알리는 두 줄은 원본에서 안 나온다</b> — 「큰일입니다. %s호가 탈주했습니다!」
/// (<c>0x0055C3B0</c>)와 「%s호가 탈주했습니다!」(<c>0x0055C3D8</c>)를 내기 직전에
/// <c>0x0040E1F0</c>(아직 함대에 있는가)을 다시 묻는데, 바로 앞줄에서 이미 함대에서 뺐으므로
/// 언제나 거짓이다(<c>0x0041030F</c>). 검사를 뒤집었어야 할 자리로 보인다 — 그대로 둔다.
/// </remarks>
public static class LentShips
{
    /// <summary>
    /// 부하들이 순순히 따르는가(<c>0x004103C0</c>).
    /// </summary>
    /// <remarks>
    /// <code>
    ///   f = max(명성 − 악명, 0)
    ///   v = 매력 + f / 100 + 1
    ///   따른다 = v > rand(150)
    /// </code>
    /// 능력치는 0 부터 세는 값이라 <c>+1</c> 이 붙는다 — 화면에 보이는 매력 그대로다.
    /// </remarks>
    public static bool Obeys(int charm, int fame, int infamy, Random dice) =>
        charm + Math.Max(fame - infamy, 0) / 100 + 1 > dice.Next(150);

    /// <summary>
    /// 그 배가 남는가 — <c>rand(100) &lt;= 운 + 1</c>(<c>0x00410400</c>).
    /// </summary>
    public static bool Stays(int luck, Random dice) => dice.Next(100) <= luck + 1;

    /// <summary>
    /// <b>그래도 한 척은 남는다</b>(<c>0x004104B0</c>) — 함대가 전부 그 배들일 때만이다.
    /// </summary>
    /// <remarks>
    /// 내 배가 한 척이라도 섞여 있으면 이 자리가 −1 이 되어, 남는 배는 오로지
    /// <see cref="Stays"/> 굴림에만 달린다. 남길 배는 기함이 후보면 기함, 아니면 굴려 고른다.
    /// </remarks>
    public static bool KeepsOne(int lentInFleet, int shipsInFleet) =>
        shipsInFleet > 0 && lentInFleet >= shipsInFleet;

    // ── 도전하는 「선장」(0x0040FFC0) ────────────────────────────────────────

    /// <summary>선장의 이름(<c>0x0055C2D0</c>)과 얼굴(MALE.CDS <c>212</c>).</summary>
    public const string CaptainName = "선장";
    public const int CaptainFace = 212;

    /// <summary>
    /// 도전하는 선장의 몸값 — 인물 예비 칸(275번)을 그때그때 덮어써서 짓는다.
    /// </summary>
    /// <remarks>
    /// <code>
    ///   +0x20 체력  rand(29) + 69      +0x24 지력 64      +0x28 무력  rand(34) + 64
    ///   +0x2C 매력  49                 +0x30 운   rand(20) + 49       +0x34 신앙심 49
    ///   +0x74 항해술 3   +0x78 운용술 3   ; 나머지 기능은 0 — <b>검술도 0</b> 이다
    /// </code>
    /// 검술이 0 이라 능력치만 보면 만만치 않은데도 칼솜씨는 초보다.
    /// </remarks>
    public static Duel.Fighter CaptainOf(Random dice) =>
        new(CaptainName, dice.Next(29) + 69, dice.Next(34) + 64, 0, dice.Next(20) + 49, 0, 0);

    /// <summary>선장이 거는 말(<c>0x0055C2E0</c>). <paramref name="sponsor"/> 는 옛 후원자 이름이다.</summary>
    public static string Challenge(string sponsor) =>
        $"우리들은 {sponsor}님에게 충성을 맹세하고 있다. 당신이 일기토로 나를 이긴다면, "
        + "당신을 제독으로 인정해 명령에 따르겠다. 내가 이기면 죽음을 각오해라. "
        + "승부다. 싫다고는 못하겠지.";

    /// <summary>일기토 화면에 뜨는 이름 — 「○○호의」(<c>0x0055C3A0</c>).</summary>
    public static string DuelName(string ship) => $"{ship}호의";
}
