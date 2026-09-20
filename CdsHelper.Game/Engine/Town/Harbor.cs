using CdsHelper.Game.Local.Helpers;
using CdsHelper.Support.Local.Models;

namespace CdsHelper.Game.Engine.Town;

/// <summary>
/// 항구에서 발견물을 알리는 규칙 — 무엇을 알릴 수 있고 명성이 얼마나 오르는지.
/// </summary>
public static class Harbor
{
    /// <summary>알려서 오르는 명성 — 보수를 이만큼으로 나눈다(<c>0x0047E851</c>).</summary>
    public const int FamePerReward = 70;

    /// <summary>아무리 하찮아도 이만큼은 오른다(<c>0x0047E853</c>).</summary>
    public const int FameFloor = 10;

    /// <summary>그것을 알려서 오르는 명성.</summary>
    /// <remarks>
    /// 후원자에게 하는 <b>보고</b>는 셈이 다르다 — <see cref="Palace.FameFor"/> 는 보수/50
    /// 이고 늦으면 반이다. 같은 발견물이라도 계약으로 맡아 보고하는 편이 후하다.
    /// </remarks>
    public static int FameFor(DiscoveryTable.Record row) =>
        Math.Max(FameFloor, row.Reward / FamePerReward);

    /// <summary>
    /// 알리고 나면 <b>피로도가 풀리고 규율이 꽉 찬다</b>(<c>0x0047E885</c> · <c>0x0047E88A</c>).
    /// </summary>
    /// <remarks>
    /// 후원자에게 보고할 때도 똑같이 한다(<c>0x0041156A</c> · <c>0x00411576</c>) — 그쪽은
    /// <b>보고한 발견물마다 늘</b> 돈다. 명성을 건너뛰는 가지(이미 알려진 것)도 이 줄 앞으로
    /// 합쳐지므로 명성이 한 톨도 안 올라도 피로도는 풀린다.
    /// </remarks>
    public static void Celebrate(Player player)
    {
        player.SetFatigue(0);
        player.SetMorale(Player.MaxMorale);
    }

    /// <summary>
    /// 이미 세상에 알려진 것을 알리려 들었을 때 듣는 말(<c>0x0055A298</c>).
    /// </summary>
    /// <remarks>
    /// 판정은 <c>0x004AADB0</c> 이다.
    /// <code>
    ///   [인스턴스+0x16] &amp; 0x80   내가 이미 발표함 → 그냥 거절
    ///   사람 칸 2 에 이름이 있음   <b>남이 먼저 알림</b> → 이 말, 명성은 한 톨도 안 오른다
    /// </code>
    /// 칸 2 는 「세상에 처음 알린 사람」이고, 채우는 것은 <b>역사 항해자 대본(HISTCHR)</b>이다 —
    /// 디아스·다 가마가 제 각본대로 정해진 해에 발표하면 그때부터 값이 없어진다.
    ///
    /// <b>그런데 원본에서도 이 말은 안 나온다.</b> 칸 2 에 남의 이름을 올리는 길은 대본 명령
    /// 둘뿐인데(<c>0x3F</c> · <c>0x68 0B</c> — <c>0x0040AFC6</c> 이 <c>0x004AACA0</c> 으로 적는다),
    /// 딸려 오는 대본 어디에도 그 명령이 없다. 새 판은 세 칸을 다 비우고 시작한다
    /// (<c>0x004AA9B3</c>). <see cref="Palace.FameFor"/> 의 <c>known</c> 과 같은 사정이다.
    ///
    /// 역사 항해자(<see cref="Discovery.HistoryVoyages"/>)가 채가는 것은 칸 <b>0·1</b>(발견)이라
    /// 이 자리가 아니다 — 그쪽은 <b>한 번짜리를 아예 못 찾게</b> 막는 길로 이미 옮겨 두었다
    /// (<c>0x004AAC10</c>).
    /// </remarks>
    public const string AlreadyKnown = "자네, 그런 건 벌써 모두 알고 있네.";

    /// <summary>
    /// <b>자리로는 못 찾는 것</b>(유적 속 물건·인물·비보)을 알리려 들었을 때(<c>0x0055A318</c>).
    /// </summary>
    /// <remarks>
    /// <c>0x0047E820</c> 이 발견물 인스턴스 <c>+0x16</c> 의 깃발 <c>0x04</c> 를 본다 —
    /// 새 판을 열 때 <see cref="DiscoveryTable.Record.Indirect"/> 인 줄만 그 깃발을
    /// 지우므로(<c>0x004AA97B</c>), 그런 것은 항구에서 알려도 <b>명성이 한 톨도 안 오르고</b>
    /// 피로·규율도 안 풀린다. 그래도 <b>알린 것으로는 찍혀</b> 다시 못 낸다(<c>0x0047E680</c>).
    /// </remarks>
    public const string NobodyCares = "아무도 상대해 주지 않았습니다!";

    /// <summary>
    /// 지금 항구에서 알릴 수 있는 발견물. 찾은 차례대로다.
    /// </summary>
    /// <remarks>
    /// 게임의 <c>0x00476D20</c> · <c>0x00476DA0</c> 그대로다.
    /// <code>
    ///   발견했고(깃발 0x40) · 아직 발표 안 했고(0x80 없음)
    ///   계약이 있으면 그 계약의 유적 번호와 <b>다른</b> 것만
    /// </code>
    /// 계약으로 맡은 것은 항구에서 못 알린다 — 그쪽은 후원자에게 보고해야 한다.
    /// 그래서 <b>계약 없이 발견한 것</b>이 여기 뜬다.
    /// </remarks>
    public static List<DiscoveryTable.Record> Announceable(Player player,
                                                          DiscoveryTable? table,
                                                          HintTable? hints)
    {
        if (table == null) return [];

        int target = player.Contract is { } contract && hints?.Find(contract.Hint) is { } hint
                   ? hint.Discovery : -1;

        var rows = new List<DiscoveryTable.Record>();
        foreach (int id in player.Discoveries.Order())
        {
            if (player.HasAnnounced(id)) continue;
            if (table.Find(id) is not { } row) continue;
            if (target >= 0 && row.Hint == target) continue;   // 계약의 목표는 뺀다
            rows.Add(row);
        }
        return rows;
    }
}
