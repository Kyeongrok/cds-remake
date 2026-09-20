using CdsHelper.Game.Local.Helpers;
using CdsHelper.Support.Local.Models;

namespace CdsHelper.Game.Engine.Sea;

/// <summary>
/// 해전에서 이기고 배를 잡았을 때 <b>빼앗는 짐</b> — 보급품 넷과 교역품 한 가지.
/// </summary>
/// <remarks>
/// 게임의 <c>0x00434D30</c> 이 판이 끝날 때 센다(판을 열 때 미리 실어 두는 것이 아니다).
/// <code>
///   V = 잡은 배들의 빈 용량 합(판을 열 때 배 +0x44 − 포탑, 0x0044C910)
///   W = 잡은 배들의 선체 중량 한도 합(선체 표 +0x1C)
///   N = ⌊⌊V x 7 / 10⌋ / 4⌋, W x 7 / 10 &lt; 40 x N 이면 N 을 줄인다   — 식량·물·자재·탄약 풀이 저마다 N 통
///   G = V − 4N, (W − 40N) &lt; G x 단중량 이면 G 를 줄인다              — 교역품 갯수
/// </code>
/// 40 은 네 보급품의 단중량 합(5 + 10 + 5 + 20)이다. 교역품 종류는 <b>적장 나라에 딸린 첫 도시</b>
/// (도시 번호 차례, 도시 레코드 <c>+0x00</c>)의 교역소 공통품(<c>0x0042A160</c> — 그 도시 특산품과 같은
/// 것은 뺀다) 가운데 하나를 굴린다. 원산지는 그 도시다. 그런 도시가 없으면 교역품은 없다.
/// </remarks>
public static class BattleLoot
{
    /// <summary>네 보급품의 단중량 합.</summary>
    private const int SupplyWeight = 40;

    /// <summary>빼앗은 교역품 한 무더기.</summary>
    /// <param name="Shelf">유통 기한(날). 빼앗은 것은 가득이다(<c>0x00435045</c>).</param>
    public readonly record struct Goods(int Kind, int Count, int Origin, int UnitWeight,
                                        int Shelf = Player.NeverSpoils);

    /// <summary>보급품마다의 풀 N(통).</summary>
    public static int PoolOf(int volume, int weight)
    {
        int n = volume * 7 / 10 / 4;
        while (n > 0 && weight * 7 / 10 < SupplyWeight * n) n--;
        return Math.Max(0, n);
    }

    /// <summary>교역품 갯수 G.</summary>
    public static int GoodsCountOf(int volume, int weight, int pool, int unitWeight)
    {
        int g = volume - 4 * pool;
        while (g > 0 && weight - SupplyWeight * pool < g * unitWeight) g--;
        return Math.Max(0, g);
    }

    /// <summary>
    /// 빼앗을 교역품. 적장 나라를 모르거나 그 나라 도시에 교역품이 없으면 null.
    /// </summary>
    public static Goods? GoodsOf(int nation, int volume, int weight, int pool,
                                 CityExeTable cities, TradeTable trade, GoodsTable goods, Random random)
    {
        if (nation < 0) return null;
        for (int city = 0; city < CityExeTable.Count; city++)
        {
            if (cities.NationOf(city) != nation) continue;

            int region = trade.RegionOf(city), special = trade.SpecialOf(city);
            if (region < 0) continue;
            var kinds = trade.CommonOf(region).Where(k => k >= 0 && k != special).ToList();
            if (kinds.Count == 0) continue;

            int kind = kinds[random.Next(kinds.Count)];
            int unit = goods.Find(kind)?.Weight ?? 1;
            int count = GoodsCountOf(volume, weight, pool, unit);
            return new Goods(kind, count, city, unit, goods.Find(kind)?.FreshShelf ?? Player.NeverSpoils);
        }
        return null;
    }
}
