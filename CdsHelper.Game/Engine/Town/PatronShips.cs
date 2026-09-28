using CdsHelper.Game.Local.Helpers;
using CdsHelper.Support.Local.Models;

namespace CdsHelper.Game.Engine.Town;

/// <summary>
/// 후원자가 <b>빌려줄 배</b> — 후원자마다 배 칸 다섯(런타임 객체 <c>+0x34</c>, <c>0x004ADBB0</c>)을 든다.
/// </summary>
/// <remarks>
/// <code>
///   칸 수        0x004ADB50  1 + (해 ≥ 1500) + (해 ≥ 1515) + (권력 &gt; 50) + (권력 &gt; 80), 다섯까지
///                            권력은 정적 표 +0x20(SponsorTable.Sponsor.Eye)이다
///   새 판        0x004AD850  칸을 모두 −1 로 두고, 그 도시 조선소 낱말(+0x1E)의 선체 가운데
///                            5 x 규모 − 1475 − 문턱 + 해 가 0 이상이고 <b>짝수</b>인 <b>첫</b> 선체로 칸 수만큼 채운다
///   해 넘김      0x004ADE50  (0x0044B3FE 가 81명을 돈다) 같은 조건의 <b>마지막</b> 선체 하나를
///                0x004ADCC0  빈 칸에 넣고, 빈 칸이 없으면 번호가 가장 작은 칸을 그것으로 갈아 끼운다
///   빌려줄 때    0x0040FC60  칸의 배를 모두 꺼내 짓고 칸을 비운다 — 척수 상한이 곧 찬 칸 수다
///                0x004105C0  빌려주는 척수 = min(자금 / 60000 + 1, 찬 칸 수, 8 − 내 배 척수)
///                0x0040FA00  선체 번호가 큰 것부터 그 척수만큼 대출 표시(+0x64 = 1)
///                0x0040FDC0  안 빌려준 배는 0x0040FE00 → 0x004ADBF0 으로 칸에 되돌린다
///   돌려받을 때  0x0040FE00  계약이 끝나 거둔 배도 0x004ADBF0 으로 칸에 되돌린다 — 빈 칸이 없으면 버린다
/// </code>
/// 그래서 조선소 낱말이 비어 있는 도시의 후원자는 한 척도 못 빌려주고, 한 번 다 빌려주면 이듬해 1월에
/// 한 척씩 찬다. 해 넘김 때 도시 조선소 셈(<c>0x0044B420</c>)은 후원자 셈 <b>뒤</b>에 돌므로 지난해 낱말로 본다.
///
/// 상태는 <see cref="Player.PatronDocks"/> 에 둔다 — 손댄 적 없는 후원자는 날짜만으로 셈해 낸다.
/// </remarks>
public static class PatronShips
{
    /// <summary>배 칸 수(<c>0x004ADB50</c>).</summary>
    public static int SlotCount(int power, int year)
    {
        int n = 1 + (year >= 1515 ? 1 : 0) + (year >= 1500 ? 1 : 0) + (power > 80 ? 1 : 0) + (power > 50 ? 1 : 0);
        return Math.Min(n, Slots);
    }

    /// <summary>배 칸 자리 수 — 다섯이다(<c>0x004AD902</c> 가 다섯을 −1 로 둔다).</summary>
    public const int Slots = 5;

    /// <summary>조선소 건물 코드 — 도시 건물 낱말(<c>+0x1C</c>)의 비트 6(<c>0x0042A345</c> 의 <c>test 0x40</c>).</summary>
    private const int ShipyardCode = 6;

    /// <summary>판이 열리는 해.</summary>
    private const int FirstYear = 1480;

    /// <summary>계약금이 이만큼 오를 때마다 한 척씩 는다(<c>0x004105F4</c> 의 <c>0xEA60</c>).</summary>
    public const int GoldPerShip = 60000;

    /// <summary>
    /// 그 후원자가 <paramref name="year"/> 에 들고 있는 배 칸(선체 번호, 빈 칸 −1) 다섯.
    /// </summary>
    public static int[] SlotsOf(Player player, SponsorTable.Sponsor sponsor, CityExeTable cities, int year)
    {
        int[] slots;
        int from;
        if (player.PatronDocks.TryGetValue(sponsor.Name, out var dock) && dock.Slots.Count == Slots)
        {
            slots = [.. dock.Slots];
            from = dock.Year;
        }
        else
        {
            slots = Initial(sponsor, cities);
            from = FirstYear;
        }
        for (int y = from + 1; y <= year; y++) AddYearly(slots, sponsor, cities, y);
        return slots;
    }

    /// <summary>
    /// 빌려줄 수 있는 척수(<c>0x004105C0</c>). 0 이하면 「배가 전부 나가고 없네」다.
    /// </summary>
    public static int CountFor(Player player, SponsorTable.Sponsor sponsor, CityExeTable cities, int funds)
    {
        int year = player.Date.Year;
        int filled = SlotsOf(player, sponsor, cities, year).Count(h => h >= 0);
        return Math.Min(Math.Min(funds / GoldPerShip + 1, filled), Player.MaxShips - player.Ships.Count);
    }

    /// <summary>
    /// 칸에서 <paramref name="count"/> 척을 꺼낸다 — 선체 번호가 큰 것부터다(<c>0x0040FA00</c>).
    /// 꺼낸 선체 번호들을 돌려주고 칸을 적어 둔다.
    /// </summary>
    public static List<int> Take(Player player, SponsorTable.Sponsor sponsor, CityExeTable cities, int count)
    {
        int year = player.Date.Year;
        var slots = SlotsOf(player, sponsor, cities, year);
        var taken = new List<int>();
        for (int k = 0; k < count; k++)
        {
            int best = -1;
            for (int i = 0; i < slots.Length; i++)
                if (slots[i] >= 0 && (best < 0 || slots[i] > slots[best])) best = i;
            if (best < 0) break;
            taken.Add(slots[best]);
            slots[best] = -1;
        }
        player.SetPatronDock(sponsor.Name, new Player.PatronDock(year, [.. slots]));
        return taken;
    }

    /// <summary>
    /// 거둔 배를 칸에 되돌린다(<c>0x004ADBF0</c>) — 칸 수 안의 빈 칸에 넣고, 없으면 버린다.
    /// </summary>
    public static void Return(Player player, SponsorTable.Sponsor sponsor, CityExeTable cities,
                              IEnumerable<int> hulls)
    {
        int year = player.Date.Year;
        var slots = SlotsOf(player, sponsor, cities, year);
        int count = SlotCount(sponsor.Eye, year);
        foreach (int hull in hulls)
        {
            if (hull is < 0 or >= 8) continue;
            for (int i = 0; i < count; i++)
                if (slots[i] < 0) { slots[i] = hull; break; }
        }
        player.SetPatronDock(sponsor.Name, new Player.PatronDock(year, [.. slots]));
    }

    /// <summary>새 판의 칸(<c>0x004AD850</c>).</summary>
    private static int[] Initial(SponsorTable.Sponsor sponsor, CityExeTable cities)
    {
        var slots = Enumerable.Repeat(-1, Slots).ToArray();
        var sold = ShipyardStock.HullsAt(cities, sponsor.City, new DateTime(FirstYear, 1, 1), yearly: false);
        int scale = cities.ScaleOf(sponsor.City);

        int pick = -1;
        for (int h = 0; h < 8 && pick < 0; h++)
            if (sold.Contains(h) && Fits(scale, h, FirstYear)) pick = h;
        if (pick < 0) return slots;

        int n = SlotCount(sponsor.Eye, FirstYear);
        for (int i = 0; i < n; i++) slots[i] = pick;
        return slots;
    }

    /// <summary>해 넘김 하나(<c>0x004ADE50</c> → <c>0x004ADCC0</c>).</summary>
    private static void AddYearly(int[] slots, SponsorTable.Sponsor sponsor, CityExeTable cities, int year)
    {
        var sold = ShipyardStock.HullsAt(cities, sponsor.City, new DateTime(year - 1, 12, 31),
                                         yearly: cities.HasBuilding(sponsor.City, ShipyardCode));
        int scale = cities.ScaleOf(sponsor.City);

        int pick = -1;
        for (int h = 0; h < 8; h++)
            if (sold.Contains(h) && Fits(scale, h, year)) pick = h;
        if (pick < 0) return;

        int n = SlotCount(sponsor.Eye, year), weakest = -1, least = 8;
        for (int i = 0; i < n; i++)
        {
            if (slots[i] < 0) { slots[i] = pick; return; }
            if (slots[i] < least) { least = slots[i]; weakest = i; }
        }
        if (weakest >= 0) slots[weakest] = pick;
    }

    /// <summary>
    /// 그 해에 그 선체를 칸에 넣을 수 있는지 — <c>5 x 규모 − 0x5C3 − 문턱 + 해</c> 가 0 이상이고 짝수다
    /// (<c>0x004AD8CD</c> · <c>0x004ADE97</c>). 짝홀로 조선소마다 취급 선체가 갈린다.
    /// </summary>
    private static bool Fits(int scale, int hull, int year)
    {
        long v = 5L * scale - 1475 - ShipyardStock.ThresholdOf(hull) + year;
        return v >= 0 && v % 2 == 0;
    }
}
