using CdsHelper.Game.Local.Helpers;
using CdsHelper.Support.Local.Models;

namespace CdsHelper.Game.Engine.Town;

/// <summary>
/// 모드 「도시 투자 성장」 — 교역소에 투자한 돈이 쌓이면 그 도시의 규모(0~7)가 오른다. 원본에 없는 것이다.
/// </summary>
/// <remarks>
/// 다음 규모로 오르는 데 「10만 닢 x 올라갈 규모」가 든다 — 0→1 은 10만, 6→7 은 70만, 0 에서 7 까지 280만 닢이다.
/// 모드를 켠 뒤 넣은 돈만 센다(<see cref="Player.GrowthFunds"/>) — 그 전의 투자는 작위 공적으로만 남는다.
/// 오른 규모는 역사 대본이 바꾼 규모와 같은 자리(<see cref="Player.CityScales"/>)에 적혀, 교역소 재고 · 시세 · 조선소 · 후원자 함대 ·
/// 그 도시 수비 병력이 그 규모로 돈다. 규모가 <see cref="BigArtScale"/> 이상이면 지도의 도시 그림이 큰 도시 그림으로 바뀐다
/// (<see cref="UI.Rendering.WorldMapHost"/>).
/// </remarks>
public static class CityGrowth
{
    /// <summary>규모 한 칸의 값 — 올라갈 규모에 곱한다.</summary>
    public const int CostPerScale = 100_000;

    /// <summary>규모의 끝.</summary>
    public const int MaxScale = 7;

    /// <summary>이 규모부터 지도에 큰 도시 그림을 쓴다.</summary>
    public const int BigArtScale = 5;

    /// <summary>모드를 켰는지.</summary>
    public static bool Enabled => Local.Settings.GameSettings.InvestGrowth;

    /// <summary>규모 <paramref name="next"/> 로 오르는 데 드는 돈.</summary>
    public static int CostToReach(int next) => CostPerScale * next;

    /// <summary>지금 규모에서 다음 규모까지 남은 돈. 끝까지 올랐으면 0.</summary>
    public static long LeftToNext(Player player, int scale, int city) =>
        scale >= MaxScale ? 0 : Math.Max(0, CostToReach(scale + 1) - player.GrowthFundOf(city));

    /// <summary>
    /// 투자한 돈을 그 도시 적립금에 넣고, 찬 만큼 규모를 올린다. 오른 규모들(차례대로), 모드를 껐거나 안 올랐으면 빈 목록.
    /// </summary>
    public static List<int> Feed(Player player, CityExeTable? cities, int city, int amount)
    {
        var grown = new List<int>();
        if (!Enabled || cities == null || city < 0 || amount <= 0) return grown;
        player.AddGrowthFund(city, amount);
        int scale = cities.ScaleOf(city);
        while (scale < MaxScale && player.GrowthFundOf(city) >= CostToReach(scale + 1))
        {
            player.AddGrowthFund(city, -CostToReach(scale + 1));
            scale++;
            player.SetCityScale(city, scale);
            grown.Add(scale);
        }
        return grown;
    }
}
