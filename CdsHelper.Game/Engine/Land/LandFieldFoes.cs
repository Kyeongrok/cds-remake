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
    /// 구역 여덟의 네모(<c>0x00569EC0</c>, 한 줄 32바이트) — 원본 눈금(칸) 그대로다.
    /// </summary>
    /// <remarks>
    /// 게임은 경도(<c>0x005B63B0</c>)와 위도(<c>0x005B63B4</c>)를 <b>16 으로 나눈</b> 눈금으로
    /// 견준다(<c>0x0048BEE3</c>: 서 ≤ x &lt; 동 · 북 ≤ y &lt; 남). 경도 눈금 하나가 0.144도, 위도도 같다.
    /// 괄호 안 도는 어림이다 — 도로 바꿔 소수 한 자리에서 자르면 경계가 0.03~0.13도 어긋나 칸 눈금을 그대로 쓴다.
    /// <code>
    ///   0  칸 x 1389~1701 · y 347~521  (경도   20.02~  64.94 · 위도  40.03~ 14.98)   예니체리 · 맘루크
    ///   1  칸 x 1146~1563 · y 486~833  (경도  -14.98~  45.07 · 위도  20.02~-29.95)   마차이족 · 자가족
    ///   2  칸 x 1701~1875 · y 347~556  (경도   64.94~  90.00 · 위도  40.03~  9.94)   무슬림 · 마라타
    ///   3  칸 x 1944~2153 · y 312~451  (경도   99.94~ 130.03 · 위도  45.07~ 25.06)   경비병 · 도적단
    ///   4  칸 x 2153~2222 · y 347~417  (경도  130.03~ 139.97 · 위도  40.03~ 29.95)   전국 무사단 · 산적
    ///   5  칸 x 1563~1944 · y 208~347  (경도   45.07~  99.94 · 위도  60.05~ 40.03)   타타르족 · 몽골족
    ///   6  칸 x 1944~2188 · y 521~694  (경도   99.94~ 135.07 · 위도  14.98~ -9.94)   네그리트 · 도둑족
    ///   7  칸 x  417~ 625 · y 278~451  (경도 -119.95~ -90.00 · 위도  49.97~ 25.06)   수우족 · 나체즈족
    /// </code>
    /// 어느 네모에도 안 들면 <b>아무 일도 안 난다</b>(<c>0x0048BF04</c>).
    /// </remarks>
    private static readonly (int W, int E, int N, int S)[] Zones =
    [
        (1389, 1701, 347, 521),
        (1146, 1563, 486, 833),
        (1701, 1875, 347, 556),
        (1944, 2153, 312, 451),
        (2153, 2222, 347, 417),
        (1563, 1944, 208, 347),
        (1944, 2188, 521, 694),
        ( 417,  625, 278, 451),
    ];

    /// <summary>경도·위도 한 바퀴의 칸 수(원본 좌표 / 16) — 가로 2500 · 세로 1250.</summary>
    private const double CellsX = 2500, CellsY = 1250;

    /// <summary>그 자리가 드는 구역. 어디에도 안 들면 −1.</summary>
    /// <remarks>
    /// 게임은 <b>앞에서부터 훑되 마지막으로 맞은 것</b>을 쓴다(<c>0x0048BEF6</c> 이 덮어쓴다) —
    /// 네모가 겹치면 뒤쪽 구역이 이긴다. 중국(3)과 동남아(6)가 겹치는 데가 그렇다.
    /// </remarks>
    public static int ZoneAt(double lat, double lon)
    {
        // 도를 원본 칸 눈금으로 되돌린다(x / 16 · y / 16). 부동소수 찌꺼기에 칸이 하나 밀리지 않게 조금 민다.
        int x = (int)Math.Floor((lon + 180.0) * CellsX / 360.0 + 1e-9);
        int y = (int)Math.Floor((90.0 - lat) * CellsY / 180.0 + 1e-9);
        int found = -1;
        for (int i = 0; i < Zones.Length; i++)
        {
            var (w, e, n, s) = Zones[i];
            if (x >= w && x < e && y >= n && y < s) found = i;
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
