using CdsHelper.Game.Local.Helpers;
using CdsHelper.Support.Local.Models;

namespace CdsHelper.Game.Engine.Market;

/// <summary>사려고 했을 때 벌어진 일.</summary>
public enum BuyResult
{
    /// <summary>샀다.</summary>
    Ok,

    /// <summary>돈이 모자란다 — "가난한 사람에게는 볼일 없네! 안 살 거면 돌아가게!"</summary>
    NotEnoughGold,

    /// <summary>그 도시가 안 파는 물건이다.</summary>
    NotSold,

    /// <summary>소지품 칸이 꽉 찼다 — "이 이상 가질 수 없습니다!"</summary>
    BagFull,
}

/// <summary>팔려고 했을 때 벌어진 일.</summary>
public enum SellResult
{
    /// <summary>팔았다.</summary>
    Ok,

    /// <summary>지니고 있지 않은 물건이다.</summary>
    NotOwned,
}

/// <summary>
/// 시장에서 사고파는 규칙. 무엇을 파는지, 얼마인지, 살 수 있는지를 여기서만 정한다.
/// </summary>
/// <remarks>
/// 창(<c>MarketBuyDialog</c>)은 이 규칙을 부르기만 한다. 갈라 둔 까닭은 값 셈과 소지금
/// 검사가 창보다 오래 갈 것이기 때문이다 — 매각 창도, 나중에 붙일 흥정도 같은 규칙을 쓴다.
///
/// 게임의 차례를 그대로 따른다. 돈 검사는 <b>물어보고 YES 를 고른 뒤</b>다 —
/// 목록에서 고를 때도, 값을 알릴 때도 막지 않는다(구입 본체 <c>0x004B3AAD</c>).
/// </remarks>
public sealed class Market
{
    /// <summary>도시 하나가 내놓는 물건 칸 수. 게임 도시 구조체의 <c>+20~+48</c> 이 여덟 칸이다.</summary>
    public const int MaxSlots = 8;

    private readonly ItemTable _items;
    private readonly MarketRates _rates;
    private readonly CityExeTable? _stock;
    private readonly Dictionary<int, List<ItemTable.Record>> _cache = [];

    /// <param name="stock">
    /// 도시마다 무엇을 내놓는지. 없으면 어느 도시도 아무것도 안 판다 —
    /// 있지도 않은 물건을 지어내는 것보다 낫다.
    /// </param>
    public Market(ItemTable items, MarketRates rates, CityExeTable? stock)
    {
        _items = items;
        _rates = rates;
        _stock = stock;
    }

    /// <summary>시세 표. 값이 도시마다 갈리는 것은 이것 하나 때문이다.</summary>
    public MarketRates Rates => _rates;

    /// <summary>
    /// 그 도시가 파는 물건. 없으면 빈 목록.
    /// </summary>
    /// <remarks>
    /// 게임 EXE 의 도시 표에 박혀 있는 그대로다(<see cref="CityExeTable"/>). 켜 놓은
    /// 게임의 메모리를 226곳 다 읽어 대 보니 한 칸도 다르지 않았다.
    /// </remarks>
    /// <summary>아이템 번호로 그 줄을 찾는다 — 소지품을 이름으로 늘어놓을 때 쓴다.</summary>
    public ItemTable.Record? Find(int itemId) => _items.Find(itemId);

    public IReadOnlyList<ItemTable.Record> StockOf(int cityId)
    {
        if (_cache.TryGetValue(cityId, out var got)) return got;

        var picked = new List<ItemTable.Record>();
        if (_stock != null)
            foreach (int id in _stock.Of(cityId))
                if (_items.Find(id) is { } record) picked.Add(record);

        _cache[cityId] = picked;
        return picked;
    }

    /// <summary>그 도시가 그 물건을 파는지.</summary>
    public bool Sells(int cityId, int itemId) =>
        StockOf(cityId).Any(r => r.Id == itemId);

    /// <summary>살 때 내는 값 — 정가에 그 도시 시세를 먹인 것이다.</summary>
    public int PriceOf(ItemTable.Record item, int cityId) =>
        _rates.BuyPrice(item.BuyList, cityId);

    /// <summary>팔 때 받는 값.</summary>
    public int PaidFor(ItemTable.Record item, int cityId) =>
        _rates.SellPrice(item.SellList, cityId);

    /// <summary>
    /// 판다. 소지품에서 빼고 값을 받는다. 안 지닌 것이면 아무것도 하지 않는다.
    /// </summary>
    /// <remarks>
    /// 사는 쪽과 달리 <b>도시가 그 물건을 파는지는 안 따진다</b> — 게임도 무엇이든 받아 준다.
    /// 값은 그 도시 시세를 먹인 것이다.
    /// </remarks>
    public SellResult Sell(Player player, ItemTable.Record item, int cityId)
    {
        if (!player.Drop(item.Id)) return SellResult.NotOwned;

        player.Earn(PaidFor(item, cityId));
        return SellResult.Ok;
    }

    /// <summary>
    /// 여러 개를 한꺼번에 산다. 게임도 목록에서 여럿을 골라 <b>값을 합쳐</b> 묻는다.
    /// </summary>
    /// <remarks>
    /// <b>되면 다 되고 안 되면 하나도 안 된다.</b> 값을 합친 것이 소지금을 넘거나 소지품
    /// 칸이 모자라면 아무것도 사지 않는다 — 반쯤 사고 마는 것보다 게임 말("가난한
    /// 사람에게는 볼일 없네!")과 맞는다.
    /// </remarks>
    public BuyResult Buy(Player player, IReadOnlyList<ItemTable.Record> items, int cityId)
    {
        if (items.Count == 0) return BuyResult.NotSold;
        foreach (var item in items)
            if (!Sells(cityId, item.Id)) return BuyResult.NotSold;

        if (player.Items.Count + items.Count > Player.MaxItems) return BuyResult.BagFull;

        long total = 0;
        foreach (var item in items) total += PriceOf(item, cityId);
        if (!player.CanAfford((int)Math.Min(int.MaxValue, total))) return BuyResult.NotEnoughGold;

        foreach (var item in items)
            if (player.BuyItem(item.Id, PriceOf(item, cityId)) != PurchaseResult.Ok)
                return BuyResult.NotEnoughGold;

        return BuyResult.Ok;
    }

    /// <summary>
    /// 산다. 값을 치르고 소지품에 넣는다. 돈이 모자라면 아무것도 하지 않는다.
    /// </summary>
    public BuyResult Buy(Player player, ItemTable.Record item, int cityId)
    {
        if (!Sells(cityId, item.Id)) return BuyResult.NotSold;

        int price = PriceOf(item, cityId);
        return player.BuyItem(item.Id, price) switch
        {
            PurchaseResult.Ok => BuyResult.Ok,
            PurchaseResult.BagFull => BuyResult.BagFull,
            _ => BuyResult.NotEnoughGold,
        };
    }
}
