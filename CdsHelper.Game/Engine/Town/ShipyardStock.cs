using CdsHelper.Game.Local.Helpers;

namespace CdsHelper.Game.Engine.Town;

/// <summary>
/// 조선소가 <b>지금 파는 선체</b> — 도시마다 다르고, 해가 가면 늘어난다.
/// </summary>
/// <remarks>
/// 게임은 도시 레코드 <c>+0x1E</c> 낱말에 비트로 든다(비트 n = 선체 n). 세이브에도 들어간다
/// (<c>0x00429B9A</c>). 켜는 곳은 둘이다.
/// <code>
///   판을 열 때  0x00429A2F   도시 표 +0x18 비트 가운데  규모 x 5 + 5  &gt;  선체 문턱  인 것 모두
///   달마다      0x0042A340   조선소가 있는 도시(+0x1C 비트 6)만,
///                            도시 표 +0x18 비트 가운데  규모 x 5 − 1475 + 해  &gt;  선체 문턱  인 것 중
///                            <b>번호가 가장 큰 하나</b>를 더한다 (0x0044B42C 가 226곳을 돈다)
/// </code>
/// 선체 문턱은 선체 표 <c>+0x08</c>(<c>0x004FC1E8</c>)이다 — 코그 10 · 카라벨 15 · 대형카라벨 25 ·
/// 카락 35 · 대형카락 45 · 중카락 55 · 갤리온 65 · 다우 10.
///
/// 달마다 <b>하나씩만</b> 더하므로 한 해에 둘이 한꺼번에 문턱을 넘으면 작은 것은 영영 안 켜진다 —
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

    /// <summary>놀이 첫 해 — 해 값에서 빼는 수(<c>0x0042A383</c> 의 <c>−0x5C3</c>)와 짝이다.</summary>
    private const int FirstYear = 1480, YearBase = 1475;

    /// <summary>파는 선체가 없을 때 조선소 주인의 말(<c>0x00531118</c>).</summary>
    public const string NoneWord = "미안하지만, 우리집은 새로 만든 배는 취급하지 않네.";

    /// <summary>
    /// 그 도시 조선소가 <paramref name="date"/> 에 파는 게임 선체 번호(0~7).
    /// </summary>
    public static IReadOnlySet<int> HullsAt(CityExeTable cities, int city, DateTime date)
    {
        int mask = cities.HullMaskOf(city), scale = cities.ScaleOf(city);
        var got = new HashSet<int>();

        // 판을 열 때의 셈.
        for (int h = 0; h < Thresholds.Length; h++)
            if ((mask & 1 << h) != 0 && scale * 5 + 5 > Thresholds[h]) got.Add(h);

        // 달마다의 셈 — 첫 달(1480/1)은 안 돈다. 값은 해로만 바뀌므로 해마다 한 번 본다.
        int lastYear = date.Year == FirstYear && date.Month == 1 ? FirstYear - 1 : date.Year;
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
