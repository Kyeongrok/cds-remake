using CdsHelper.Game.Local.Helpers;
using CdsHelper.Support.Local.Models;

namespace CdsHelper.Game.Engine.Market;

/// <summary>
/// 발표한 뒤 해가 갈수록 값이 움직이는 교역품 — 향신료(<c>0x004805D0</c>)와
/// 커피·담배·카카오·차(<c>0x004807E0</c>).
/// </summary>
/// <remarks>
/// 둘 다 기준가를 비튼 뒤에 시세를 곱한다(<c>0x00429DC0</c>). 햇수는 그 발견물을 <b>보고·발표한 해</b>
/// (발견물 인스턴스 칸 2 의 <c>+0x28</c>, <c>0x00480780</c>)부터 지금 해까지다 — 안 했으면 「모른다」.
/// 발견물은 이름으로 찾는다(<c>0x00480160</c>: 인도 · 향료제도 · 커피 · 담배 · 카카오 · 차).
/// </remarks>
public sealed class GoodsTrend
{
    /// <summary>문화권 → 향신료 갈래(<c>0x0048076C</c>). 9 넘는 문화권은 안 비튼다.</summary>
    private static readonly int[] SpiceGroups = [0, 0, 0, 3, 1, 1, 3, 3, 2];

    /// <summary>신대륙 기호품 — 교역품 이름과 같은 이름의 발견물이 짝이다.</summary>
    private static readonly string[] NewWorldGoods = ["커피", "담배", "카카오", "차"];

    /// <summary>향신료 분류(교역품 표 <c>+0x08</c>).</summary>
    public const int SpiceCategory = 5;

    private readonly Dictionary<string, int> _ids = [];

    public GoodsTrend(DiscoveryTable? discoveries)
    {
        if (discoveries == null) return;
        foreach (var row in discoveries.Discoveries)
            _ids.TryAdd(row.Name, row.Id);
    }

    /// <summary>이 교역품이 햇수로 움직이는 것인지.</summary>
    public bool Moves(GoodsTable.Goods goods) =>
        goods.Category == SpiceCategory || NewWorldGoods.Contains(goods.Name);

    /// <summary>
    /// 기준가를 비튼다(시세를 곱하기 전).
    /// </summary>
    /// <remarks>
    /// <code>
    ///   향신료   y = 향료제도 햇수, x = 인도 햇수 (모르면 -1)
    ///     유럽(문화권 0~2)   y&gt;=0: max(70-y, 40)%  · x&gt;=0: max(95-x, 75)%  · 둘 다 모르면 그대로
    ///     이슬람·인도(4·5)   y&gt;=0: min(130+y,150)% · x&gt;=0: min(105+x,125)% · 둘 다 모르면 그대로
    ///     동남아(8)          y&gt;=0: min(5y+15, 200)% · 모르면 1/10
    ///   커피·담배·카카오·차  유럽(0~2)에서만 — 알면 min(3y+30, 200)%, 모르면 30%
    /// </code>
    /// </remarks>
    public int Adjust(Player player, int culture, GoodsTable.Goods goods, int basis)
    {
        if (goods.Category == SpiceCategory)
        {
            int islands = YearsSince(player, "향료제도"), india = YearsSince(player, "인도");
            if (culture is < 0 or > 8) return basis;
            return SpiceGroups[culture] switch
            {
                0 when islands >= 0 => Math.Max(70 - islands, 40) * basis / 100,
                0 when india >= 0 => Math.Max(95 - india, 75) * basis / 100,
                1 when islands >= 0 => Math.Min(islands + 130, 150) * basis / 100,
                1 when india >= 0 => Math.Min(india + 105, 125) * basis / 100,
                2 when islands >= 0 => Math.Min(5 * islands + 15, 200) * basis / 100,
                2 => basis / 10,
                _ => basis,
            };
        }

        if (!NewWorldGoods.Contains(goods.Name) || culture is < 0 or > 2) return basis;
        int years = YearsSince(player, goods.Name);
        return years < 0 ? basis * 30 / 100 : Math.Min(3 * years + 30, 200) * basis / 100;
    }

    /// <summary>그 발견물을 보고·발표한 뒤 지난 햇수(0 밑은 0). 안 했으면 -1.</summary>
    private int YearsSince(Player player, string discovery)
    {
        if (!_ids.TryGetValue(discovery, out int id) || player.AnnouncedYearOf(id) is not { } year) return -1;
        return Math.Max(0, player.Date.Year - year);
    }
}
