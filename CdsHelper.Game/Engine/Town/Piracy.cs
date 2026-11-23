using CdsHelper.Game.Local.Helpers;
using CdsHelper.Support.Local.Models;

namespace CdsHelper.Game.Engine.Town;

/// <summary>
/// 해적 공화국 — 원본에 없는 나라와 그 근거지 나소(요청, 버그 리포트 #72).
/// </summary>
/// <remarks>
/// <list type="bullet">
///   <item>나소는 누구나 드나든다. 총독부에서 「해적 가담」을 고르면 국적이 해적 공화국이 되고 자택이 나소로 옮겨지며,
///         <b>항구를 가진 모든 나라와 적대</b>(출입여부 2)가 되고 모든 나라의 모든 후원자가 수배를 내린다 —
///         후원자를 배신했을 때의 추격(<see cref="Player.Betray"/>)을 그대로 쓴다(요청).</item>
///   <item>돌아가려면 망명한다(<see cref="Exile"/>) — 망명하면 수배가 풀리고 적대도 나라 표의 처음 값으로 돌아간다.</item>
/// </list>
/// </remarks>
public static class Piracy
{
    /// <summary>해적 공화국 나라 번호 — 시나리오 표의 더한 나라(<c>scenarios.json</c> Nations).</summary>
    public const int Nation = 79;

    /// <summary>근거지 나소의 도시 번호(<c>cities-added.json</c>).</summary>
    public const int Haven = 226;

    /// <summary>총독부 「해적 가담」 줄 이름.</summary>
    public const string JoinLine = "해적 가담";

    /// <summary>해적 공화국 사람인지.</summary>
    public static bool IsPirate(Player player) => player.Nation == Nation;

    /// <summary>항구를 가진 나라들(해적 공화국은 뺀다) — 도시 표가 그때 가리키는 주인 나라로 센다.</summary>
    public static List<int> PortNations(CityExeTable? cities)
    {
        var got = new SortedSet<int>();
        if (cities == null) return [];
        for (int city = 0; city < CityExeTable.Count; city++)
        {
            int nation = cities.NationOf(city);
            if (nation >= 0 && nation != Nation && cities.HasBuilding(city, 0)) got.Add(nation);
        }
        return [.. got];
    }

    /// <summary>총독부 「해적 가담」 줄을 세울지 — 나소이고 아직 해적이 아닐 때.</summary>
    public static bool Offered(Player player, int city) => city == Haven && !IsPirate(player);

    /// <summary>
    /// 해적 공화국에 가담한다 — 국적 · 모항을 옮기고, 항구를 가진 모든 나라와 적대가 되며, 모든 나라의 모든 후원자가 수배를 내린다(오늘부터 추격).
    /// </summary>
    /// <returns>수배를 내린 후원자 수.</returns>
    public static int Join(Player player, SponsorTable? sponsors, CityExeTable? cities)
    {
        player.Nation = Nation;
        player.SetHomePort(Haven);
        foreach (int n in PortNations(cities)) player.SetHostility(n, Hostile);
        int wanted = 0;
        foreach (var s in sponsors?.Sponsors ?? [])
        {
            if (s.Nation == Nation || player.IsBetrayed(s.Name)) continue;
            player.Betray(s.Name, player.CityName, player.Date);
            wanted++;
        }
        return wanted;
    }

    /// <summary>해적 가담으로 둔 적대 — 항구까지 막는다.</summary>
    private const int Hostile = 2;

    /// <summary>해적이 망명해 돌아간다 — 수배를 모두 거두고, 항구를 가진 나라와의 적대를 나라 표의 처음 출입여부로 되돌린다.</summary>
    public static void Pardon(Player player, CityExeTable? cities, NationTable? nations)
    {
        foreach (var b in player.Betrayals.ToList()) player.SettleBetrayal(b.Sponsor);
        foreach (int n in PortNations(cities)) player.SetHostility(n, nations?.Find(n)?.Entry ?? 0);
    }
}
