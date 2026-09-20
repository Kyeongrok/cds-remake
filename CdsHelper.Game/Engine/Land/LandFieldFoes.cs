namespace CdsHelper.Game.Engine.Land;

/// <summary>
/// 뭍을 걷다 마주치는 부대 열여섯 — 구역 여덟에 두 벌씩이다(<c>0x00569EC0</c>).
/// </summary>
/// <remarks>
/// <c>0x0048BE80</c> 이 걸음마다 <c>rand(500)</c> 을 굴려 하나를 뽑는다. 구역표 한 칸이
/// 32바이트고 <c>+0x00</c> 경도 아래·위 <c>+0x08</c> 위도 아래·위(모두 16으로 나눈 값),
/// 그 뒤에 벌 둘이 <c>(병력 밑, 굴림 폭)</c> 짝으로 붙는다.
/// <code>
///   0048bf16  벌 = rand(2)
///   0048bf1d  대장 인물 = 0xF6 + 구역*2 + 벌        ; 246 ~ 261
///   0048bf8b  병력 = 밑 + rand(폭)
/// </code>
/// 대장 이름은 EXE 가 아니라 <b>세이브의 인물 표</b>에 있다(<c>0x924A</c>, 한 칸
/// <c>0x90</c>, 이름 <c>+0x32</c>). 자세한 것은 볼트 <c>65.분석-육상전</c> 10.2 절이다.
/// </remarks>
public static class LandFieldFoes
{
    /// <param name="Name">대장 이름 — 인물 246~261 이다.</param>
    /// <param name="Least">병력 밑값.</param>
    /// <param name="Spread">병력 굴림 폭 — <c>밑 + rand(폭)</c> 이다.</param>
    /// <param name="Culture">
    /// 적 그림과 진형을 가르는 문화권.
    /// </param>
    /// <param name="Where">구역 이름 — 네모 안에 드는 도시로 붙였다.</param>
    public readonly record struct Party(string Name, int Least, int Spread, int Culture,
                                        string Where);

    /// <summary>
    /// 구역 여덟 x 두 벌. 차례가 곧 <c>대장 인물 − 246</c> 이다.
    /// </summary>
    /// <remarks>
    /// 여기 적은 문화권은 <b>물러설 자리</b>다. 싸움이 붙을 때는 게임처럼 적 대장 국적의
    /// <b>수도 도시</b> 문화권을 집고(<c>0x00447070</c>), 능력·기능도 인물 표에서 그대로
    /// 읽는다 — 표를 못 읽을 때만 이 값이 쓰인다.
    /// </remarks>
    public static readonly Party[] All =
    [
        new("예니체리",     200, 100,  3, "동지중해·근동"),
        new("맘루크",       150, 100,  3, "동지중해·근동"),
        new("마차이족",      50,  50,  8, "아프리카"),
        new("자가족",        30,  50,  8, "아프리카"),
        new("무슬림",       100,  50,  4, "인도·서아시아"),
        new("마라타",       100,  50,  4, "인도·서아시아"),
        new("경비병",       100,  50,  6, "중국"),
        new("도적단",        50, 100,  6, "중국"),
        new("전국 무사단",   100,  50,  7, "일본"),
        new("산적",          50, 100,  7, "일본"),
        new("타타르족",     100, 100,  3, "중앙아시아"),
        new("몽골족",       150, 100,  3, "중앙아시아"),
        new("네그리트",      30,  50,  5, "동남아시아"),
        new("도둑족",        30,  50,  5, "동남아시아"),
        new("수우족",        50,  50,  9, "북아메리카"),
        new("나체즈족",      50,  50,  9, "북아메리카"),
    ];

    /// <summary>
    /// 뭍을 걷다가 무리와 마주칠 주사위 폭(<c>0x0048BE9B</c> 의 <c>push 0x1F4</c>).
    /// </summary>
    /// <remarks>
    /// 바다에서는 굴리지 않는다(<c>0x0048BE86</c> 이 먼저 막는다) — 걸을 때만이다.
    /// </remarks>
    public const int Roll = 500;

    /// <summary>
    /// 구역 여덟의 네모(<c>0x00569EC0</c>, 한 줄 32바이트) — 도로 바꾼 값이다.
    /// </summary>
    /// <remarks>
    /// 게임은 경도(<c>0x005B63B0</c>)와 위도(<c>0x005B63B4</c>)를 <b>16 으로 나눈</b> 눈금으로
    /// 견준다(<c>0x0048BEE3</c>). 경도 눈금 하나가 0.144도, 위도도 같다.
    /// <code>
    ///   0  경도  20.0~ 64.9 · 위도  40.0~ 15.0   예니체리 · 맘루크
    ///   1  경도 -15.0~ 45.1 · 위도  20.0~-30.0   마차이족 · 자가족
    ///   2  경도  64.9~ 90.0 · 위도  40.0~  9.9   무슬림 · 마라타
    ///   3  경도  99.9~129.9 · 위도  45.1~ 25.1   경비병 · 도적단
    ///   4  경도 129.9~139.9 · 위도  40.0~ 30.0   전국 무사단 · 산적
    ///   5  경도  45.1~ 99.9 · 위도  60.0~ 40.0   타타르족 · 몽골족
    ///   6  경도  99.9~135.1 · 위도  15.0~-10.0   네그리트 · 도둑족
    ///   7  경도-120.0~-90.0 · 위도  50.0~ 25.1   수우족 · 나체즈족
    /// </code>
    /// 어느 네모에도 안 들면 <b>아무 일도 안 난다</b>(<c>0x0048BF04</c>).
    /// </remarks>
    private static readonly (double W, double E, double N, double S)[] Zones =
    [
        ( 20.0,  64.9,  40.0,  15.0),
        (-15.0,  45.1,  20.0, -30.0),
        ( 64.9,  90.0,  40.0,   9.9),
        ( 99.9, 129.9,  45.1,  25.1),
        (129.9, 139.9,  40.0,  30.0),
        ( 45.1,  99.9,  60.0,  40.0),
        ( 99.9, 135.1,  15.0, -10.0),
        (-120.0, -90.0, 50.0,  25.1),
    ];

    /// <summary>그 자리가 드는 구역. 어디에도 안 들면 −1.</summary>
    /// <remarks>
    /// 게임은 <b>앞에서부터 훑되 마지막으로 맞은 것</b>을 쓴다(<c>0x0048BEF6</c> 이 덮어쓴다) —
    /// 네모가 겹치면 뒤쪽 구역이 이긴다. 중국(3)과 동남아(6)가 겹치는 데가 그렇다.
    /// </remarks>
    public static int ZoneAt(double lat, double lon)
    {
        int found = -1;
        for (int i = 0; i < Zones.Length; i++)
        {
            var (w, e, n, s) = Zones[i];
            if (lon >= w && lon < e && lat <= n && lat > s) found = i;
        }
        return found;
    }

    /// <summary>그 구역에서 마주치는 무리 — 둘 가운데 <c>rand(2)</c> 다(<c>0x0048BF16</c>).</summary>
    public static int PartyAt(int zone, GameRandom dice) => zone * 2 + dice.Next(2);

    /// <summary>그 무리 대장의 인물 번호(<c>0x0048BF1D</c> 의 <c>+0xF6</c>).</summary>
    public const int FirstLeader = 246;

    /// <summary>그 벌의 병력을 굴린다 — <c>밑 + rand(폭)</c> 이다.</summary>
    public static int MenOf(int at, GameRandom dice)
    {
        var party = All[Math.Clamp(at, 0, All.Length - 1)];
        return party.Least + dice.Next(Math.Max(1, party.Spread));
    }
}
