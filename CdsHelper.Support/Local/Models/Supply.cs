namespace CdsHelper.Support.Local.Models;

/// <summary>배에 싣는 보급품 넷.</summary>
/// <remarks>차례는 게임 이름표(<c>0x0055F248</c>~) 그대로다 — 식량·물·자재·탄약.</remarks>
public enum SupplyKind
{
    Food,       // 식량
    Water,      // 물
    Material,   // 자재
    Ammo,       // 탄약
}

/// <summary>
/// 보급품 한 가지 — 이름과 한 통의 무게, 그리고 값.
/// </summary>
/// <param name="Kind">갈래.</param>
/// <param name="Name">이름. 게임 화면에 뜨는 그대로다.</param>
/// <param name="UnitWeight">한 통의 무게(단중량).</param>
/// <param name="BasePrice">한 통의 값(단가). 도시 시세를 곱하기 전 값이다.</param>
public sealed record Supply(SupplyKind Kind, string Name, int UnitWeight, int BasePrice)
{
    /// <summary>
    /// 넷. 단중량은 게임 보급 화면에서 읽은 값이고, 밑값은 EXE 에 박힌 값이다.
    /// </summary>
    /// <remarks>
    /// 게임은 이름을 <c>0x004208A0</c> 의 갈래별 분기로 낸다(0 식량 · 1 물 · 2 자재 · 3 탄약).
    /// 그 뒤 칸(4번부터)은 실어 둔 교역품이라 보급 화면에는 안 나온다.
    ///
    /// <b>단가는 밑값 x 도시 시세 / 100</b> 이다(<c>0x00429DC0</c>). 밑값은 갈래표 <c>0x0040EB80</c> 이
    /// 부르는 넷이 그대로 들고 있다.
    /// <code>
    ///   0 식량  0x0040E8E0 → 0x00493F50   15
    ///   1 물    0x0040E8C0 → 0x00493F10   10   도시 형편 비트 0x40 이면 0 닢
    ///   2 자재  0x0040E900 → 0x00493F80   25
    ///   3 탄약  0x0040E920 → 0x00493FB0   25   도시 형편 비트 8 이 없으면 안 판다(−1)
    /// </code>
    /// 예전에는 시세 127 쯤인 도시 화면에서 잰 19·12·31·31 을 밑값으로 두고 시세를 또 곱해
    /// 어디서나 두 할 반쯤 비쌌다.
    /// </remarks>
    public static readonly Supply[] All =
    [
        new(SupplyKind.Food, "식량", 5, 15),
        new(SupplyKind.Water, "물", 10, 10),
        new(SupplyKind.Material, "자재", 5, 25),
        new(SupplyKind.Ammo, "탄약", 20, 25),
    ];

    /// <summary>
    /// 물이 공짜인 도시의 형편 비트(<c>0x00493F27</c> 의 <c>test [도시+4], 0x40</c>) — 동남아 · 동아시아 ·
    /// 신대륙 42곳이다.
    /// </summary>
    public const int FreeWaterBit = 0x40;

    /// <summary>갈래 수.</summary>
    public static int Count => All.Length;

    /// <summary>갈래로 찾는다.</summary>
    public static Supply Of(SupplyKind kind) => All[(int)kind];

    /// <summary>한 통을 사는 값. 시세는 100 이 제값이다(<c>0x00429DC0</c> — 밑값이 있으면 최소 1).</summary>
    /// <param name="rate">도시 시세.</param>
    /// <param name="cityFlags">도시 형편 낱말 — 비트 <see cref="FreeWaterBit"/> 가 서면 물이 0 닢이다.</param>
    public int PriceAt(int rate, int cityFlags = 0) =>
        Kind == SupplyKind.Water && (cityFlags & FreeWaterBit) != 0 ? 0 : Math.Max(1, BasePrice * rate / 100);

    /// <summary>한 통에 든 단위 수. 식량·물은 속으로 이만큼씩 들고 있다.</summary>
    /// <remarks>
    /// 게임은 식량·물을 <b>열 배로</b> 들고 있다가 화면에 낼 때 <c>(값 + 9) / 10</c> 으로
    /// 올림해 통 수를 낸다(<c>0x0040EA15</c>). 자재·탄약은 그런 변환이 없다.
    /// </remarks>
    public const int UnitsPerBarrel = 10;

    /// <summary>
    /// 식량·물이 며칠 갈지. 적은 쪽이 정한다.
    /// </summary>
    /// <remarks>
    /// 게임 <c>0x00494010</c> 을 그대로 옮겼다.
    /// <code>
    ///   if (선원수 == 0) return 0
    ///   날수 = min(식량통, 물통) * 10 / 선원수
    /// </code>
    /// 나누는 값이 <b>함대 총 선원수</b>다(<c>0x004745F0</c> 이 배 여덟 칸의 <c>+0x34</c> 를
    /// 더한다 — 배 기록의 "선원수" 칸이다). 곧 <b>한 사람이 하루에 한 단위</b>를 쓰고
    /// 한 통이 열 단위다.
    /// </remarks>
    public static int DaysLeft(int foodBarrels, int waterBarrels, int crew) =>
        crew <= 0 ? 0 : Math.Min(foodBarrels, waterBarrels) * UnitsPerBarrel / crew;

    /// <summary>
    /// 그 날수를 버티려면 몇 통이 있어야 하는지. 게임 "10일분" 단추가 이 셈이다.
    /// </summary>
    /// <remarks>
    /// <see cref="DaysLeft"/> 를 되돌린 것이다 — <c>통 = 올림(선원수 * 날수 / 10)</c>.
    /// 그래서 <b>10일분은 선원수만큼의 통</b>이 된다.
    /// </remarks>
    public static int BarrelsForDays(int days, int crew) =>
        crew <= 0 || days <= 0
            ? 0
            : (crew * days + UnitsPerBarrel - 1) / UnitsPerBarrel;

    /// <summary>날수로 세는 품목인지. 식량과 물만 날마다 닳는다.</summary>
    public bool IsDaily => Kind is SupplyKind.Food or SupplyKind.Water;

    /// <summary>단위 수를 화면에 낼 통 수로. 게임의 <c>(값 + 9) / 10</c> 이다.</summary>
    public static int BarrelsOf(int units) => (Math.Max(0, units) + UnitsPerBarrel - 1) / UnitsPerBarrel;

    /// <summary>통 수를 단위 수로.</summary>
    public static int UnitsOf(int barrels) => Math.Max(0, barrels) * UnitsPerBarrel;

    /// <summary>
    /// 하루에 닳는 단위 수 — 식량도 물도 이만큼씩이다.
    /// </summary>
    /// <remarks>
    /// 게임의 <c>0x004755E6</c> 이다.
    /// <code>
    ///   4755e6  eax = 0x004745F0(함대)     ; 함대 총 선원수
    ///   4755ed  eax = eax * 5 * 2 / 12     ; = 선원수 x 5 / 6
    ///   4755fa  적어도 1
    ///   475616  0x004740E0(함대, -소모)     ; 물   -= 소모
    ///   47561d  0x00474180(함대, -소모)     ; 식량 -= 소모
    /// </code>
    /// <b><see cref="DaysLeft"/> 와 어긋난다.</b> 화면에 내는 남은일수는 한 사람이 하루에
    /// <b>한 단위</b>를 쓴다고 세는데, 실제로 닳는 것은 <b>다섯/여섯 단위</b>다 —
    /// 곧 보급은 화면이 이르는 것보다 <b>이 할쯤 더 간다</b>. 게임이 그렇게 되어 있어
    /// 양쪽 다 그대로 옮긴다.
    /// </remarks>
    public static int DailyUse(int crew) => Math.Max(1, crew * 5 / 6);

    /// <summary>이 날수보다 적게 남은 날 "얼마 남지 않았습니다!" 가 뜬다(<c>0x00475624</c>).</summary>
    public const int LowDays = 3;
}
