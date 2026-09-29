using CdsHelper.Game.Local.Helpers;

namespace CdsHelper.Game.Engine.Town;

/// <summary>
/// 조선소가 <b>지금 파는 선체</b> — 도시마다 다르고, 해가 가면 늘어난다.
/// </summary>
/// <remarks>
/// 게임은 도시 레코드 <c>+0x1E</c> 낱말에 비트로 든다(비트 n = 선체 n). 세이브에도 들어간다
/// (<c>0x00429B9A</c>). 켜는 곳은 둘이다.
/// <code>
///   판을 열 때  0x00429A2F   조선소가 있는 도시(표 +0x60 비트 6, 0x00429A25)만,
///                            도시 표 +0x18 비트 가운데  규모 x 5 + 5  &gt;  선체 문턱  인 것 모두
///   해마다      0x0042A340   조선소가 있는 도시(+0x1C 비트 6)만,
///                            도시 표 +0x18 비트 가운데  규모 x 5 − 1475 + 해  &gt;  선체 문턱  인 것 중
///                            <b>번호가 가장 큰 하나</b>를 더한다 (해 넘김 0x0044B3D0 의 0x0044B42C 가 226곳을 돈다)
/// </code>
/// 선체 문턱은 선체 표 <c>+0x08</c>(<c>0x004FC1E8</c>)이다 — 코그 10 · 카라벨 15 · 대형카라벨 25 ·
/// 카락 35 · 대형카락 45 · 중카락 55 · 갤리온 65 · 다우 10.
///
/// 해마다 <b>하나씩만</b> 더하므로 한 해에 둘이 한꺼번에 문턱을 넘으면 작은 것은 영영 안 켜진다 —
/// 판을 열 때 이미 넘은 것은 첫 셈이 다 켜 준다. 값은 해가 바뀔 때만 오르므로 여기서는 해마다 한 번
/// 되짚는다. 규모는 <b>지금 규모</b>로 센다 — 역사 대본이 규모를 바꾸면 원본은 그 달부터 셈이 달라지지만
/// 여기서는 처음부터 그 규모였던 것처럼 센다.
///
/// 하나도 없으면 「미안하지만, 우리집은 새로 만든 배는 취급하지 않네.」로 끝난다(<c>0x0044B76B</c>).
/// </remarks>
public static class ShipyardStock
{
    /// <summary>선체 문턱(선체 표 <c>+0x08</c>). 색인이 선체 번호다.</summary>
    private static readonly int[] Thresholds = [10, 15, 25, 35, 45, 55, 65, 10];

    /// <summary>선체 문턱(선체 표 <c>+0x08</c>). 표 밖이면 int.MaxValue.</summary>
    public static int ThresholdOf(int hull) =>
        hull >= 0 && hull < Thresholds.Length ? Thresholds[hull] : int.MaxValue;

    /// <summary>건물 낱말에서 조선소 비트(<c>0x40</c>).</summary>
    private const int ShipyardBit = 6;

    /// <summary>놀이 첫 해 — 해 값에서 빼는 수(<c>0x0042A383</c> 의 <c>−0x5C3</c>)와 짝이다.</summary>
    private const int FirstYear = 1480, YearBase = 1475;

    /// <summary>파는 선체가 없을 때 조선소 주인의 말(<c>0x00531118</c>).</summary>
    public const string NoneWord = "미안하지만, 우리집은 새로 만든 배는 취급하지 않네.";

    /// <summary>
    /// 그 도시 조선소가 <paramref name="date"/> 에 파는 게임 선체 번호(0~7).
    /// </summary>
    public static IReadOnlySet<int> HullsAt(CityExeTable cities, int city, DateTime date) =>
        HullsAt(cities, city, date, yearly: true);

    /// <summary>
    /// 도시 레코드 <c>+0x1E</c> 그대로 — <paramref name="yearly"/> 가 거짓이면 판을 열 때의 셈만 한다.
    /// </summary>
    /// <remarks>
    /// 해마다의 셈(<c>0x0042A340</c>)은 조선소가 있는 도시(<c>+0x1C</c> 비트 6)만 돈다 — 후원자가 앉은
    /// 도시처럼 조선소가 없는 데서 이 낱말을 읽을 때는 거짓을 넘긴다. 판을 열 때의 셈도 처음 조선소 비트가
    /// 없으면 안 돈다(<c>0x00429A25</c>).
    /// </remarks>
    public static IReadOnlySet<int> HullsAt(CityExeTable cities, int city, DateTime date, bool yearly)
    {
        int mask = cities.HullMaskOf(city), scale = cities.ScaleOf(city);
        var got = new HashSet<int>();

        // 판을 열 때의 셈 — 처음 건물 낱말에 조선소(비트 6)가 있는 도시만 돈다(0x00429A25 test al, 0x40).
        // 없으면 +0x1E 가 0 으로 남는다 — 후원자가 앉은 톨레도 · 파리 · 로마 같은 데가 그렇다.
        // 모르는 표(-1)면 있다고 본다.
        if ((cities.StartBuildingsOf(city) & 1 << ShipyardBit) != 0)
            for (int h = 0; h < Thresholds.Length; h++)
                if ((mask & 1 << h) != 0 && scale * 5 + 5 > Thresholds[h]) got.Add(h);

        // 해마다의 셈(1월 1일, 0x0044B3D0) — 1481년부터 돈다. 1480년 셈은 판을 열 때의 셈과 같은 값이라 넣어도 달라지지 않는다.
        int lastYear = !yearly ? FirstYear - 1
                     : date.Year == FirstYear && date.Month == 1 ? FirstYear - 1 : date.Year;
        for (int year = FirstYear; year <= lastYear; year++)
        {
            int value = scale * 5 - YearBase + year, best = -1;
            for (int h = 0; h < Thresholds.Length; h++)
                if ((mask & 1 << h) != 0 && Thresholds[h] < value) best = h;
            if (best >= 0) got.Add(best);
        }
        return got;
    }
}
