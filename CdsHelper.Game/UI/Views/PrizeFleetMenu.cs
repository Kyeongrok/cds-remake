using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using CdsHelper.Game.Local.Helpers;
using CdsHelper.Support.Local.Models;

namespace CdsHelper.Game.UI.Views;

/// <summary>
/// 해전 끝 편입 차림표(<c>0x00488A20</c>) — 빼앗은 배를 함대에 들이고, 빼고, 기함을 바꾼다.
/// </summary>
/// <remarks>
/// 볼트 <c>95.분석-해전 충돌·백병전·나포·일기토</c> 10.5 절이다. 차림표 <c>0x00469A70</c> 네 줄(<c>0x005694E8</c>).
/// <code>
///   기함 변경  함대 &gt; 1        배 고르기 「기함변경」 → 「기함을 %s호로 변경하겠습니다. 좋습니까?」
///   선박 편입  함대 &lt; 8 · 후보  여러 줄 고르기 「편입 선박의 선택」 (8척 한도 말)
///   선박 삭제  함대 &gt; 1        배 고르기 「선박 삭제」 → 후보로 되돌리고 그 배 승원을 웅덩이에
///   편성 종료  늘               남은 후보는 버리고 웅덩이를 태운다(넘치면 사라진다)
///   (한 번 할 때마다) 여유 = 최대승원합 − 승원합 만큼 웅덩이에서 태운다
/// </code>
/// 필요 승원 조건은 없다 — 승원 0 배도 들어온다. 차림표는 「편성 종료」로만 닫힌다(물리면 다시 뜬다).
/// 선원을 배마다 나누는 <c>0x004745B0</c> 은 <see cref="Player.CrewShares"/> 의 최적화 나눔으로 갈음했다.
/// </remarks>
internal static class PrizeFleetMenu
{
    public static void Run(Window owner, Player player, List<Ship> prizes, ItemTable? items = null)
    {
        int pool = 0;
        while (true)
        {
            int count = player.Ships.Count;
            (string Text, bool On)[] rows =
            [
                ("기함 변경", count > 1),
                ("선박 편입", count < Player.MaxShips && prizes.Count > 0),
                ("선박 삭제", count > 1),
                ("편성 종료", true),
            ];

            switch (ChoiceDialog.Pick(owner, "", rows))
            {
                case 0:
                    ChangeFlagship(owner, player, items);
                    break;
                case 1:
                    Enlist(owner, player, prizes, items);
                    break;
                case 2:
                    pool += Delete(owner, player, prizes, items);
                    break;
                case 3:
                    player.AddCrew(pool);                   // 최대승원에서 잘린다
                    return;
                default:
                    continue;
            }
            pool = Board(player, pool);
        }
    }

    // 셋 다 배 목록 표다 — 기함변경·선박 삭제는 0x0049D3F0(묶음, 제목, 방식 0), 편입은 후보를 모아
    // 0x0046C3E0 여럿 고르기로 연다. 묶음은 모두 0(선명·승원수·내구력·중량·용적·함대)에서 시작한다.

    private static void ChangeFlagship(Window owner, Player player, ItemTable? items)
    {
        // 0x00488ADA → 0x0049D3F0(묶음 0, 「기함변경」 0x0055F4D8, 0) — 이름만 늘어놓은 차림 창이 아니다.
        int at = ShipPickDialog.Pick(owner, player, items, "기함변경", startSet: 0);
        if (at < 0) return;
        if (!ConfirmDialog.Ask(owner, $"기함을 {player.Ships[at].Name}호로 변경하겠습니다. 좋습니까?")) return;
        player.SetFlagship(at);
    }

    private static void Enlist(Window owner, Player player, List<Ship> prizes, ItemTable? items)
    {
        if (player.IsFleetFull)
        {
            ConfirmDialog.Tell(owner, "더 이상 편입 할 수 없습니다!");
            return;
        }

        // 0x00488B9C → 0x0046C3E0(후보 수, 후보, 묶음 0x00569518, 「편입 선박의 선택」 0x0055F518, 0, 고름표) —
        // 여섯째 인자가 있으니 여럿 고르기다. 후보는 아직 승원이 없다.
        var rows = prizes.Select(s => new ShipPickDialog.Entry(s, 0, false)).ToList();
        var picked = ShipPickDialog.PickMany(owner, rows, items, "편입 선박의 선택", startSet: 0)
            .Select(i => prizes[i]).ToList();
        if (picked.Count == 0) return;

        int room = Player.MaxShips - player.Ships.Count;
        if (picked.Count > room)
        {
            ConfirmDialog.Tell(owner, $"편입 가능한 것은 {room}척까지입니다. 편입 할 배의 수를 줄여 주십시오.");
            return;
        }

        foreach (var ship in picked)
        {
            var shares = player.CrewShares.ToList();
            if (!player.Enlist(ship)) break;
            shares.Add(0);                                  // 승원 0 으로 들어온다
            player.SetCrewShares(shares);
            prizes.Remove(ship);
        }
    }

    /// <returns>웅덩이로 옮긴 승원.</returns>
    private static int Delete(Window owner, Player player, List<Ship> prizes, ItemTable? items)
    {
        if (player.Ships.Count <= 1)
        {
            ConfirmDialog.Tell(owner, "더 이상 삭제 할 수 없습니다!");
            return 0;
        }

        // 0x00488CAF → 0x0049D3F0(묶음 0x0056951C, 「선박 삭제」 0x0055F5A8, 0).
        int at = ShipPickDialog.Pick(owner, player, items, "선박 삭제", startSet: 0);
        if (at < 0) return 0;

        var shares = player.CrewShares.ToList();
        var ship = player.Ships[at];
        if (!player.Release(ship)) return 0;

        int crew = shares.ElementAtOrDefault(at);
        shares.RemoveAt(at);
        player.SetCrew(player.Crew - crew);
        player.SetCrewShares(shares);
        prizes.Add(ship);
        return crew;
    }

    /// <summary>여유만큼 웅덩이에서 태운다(<c>0x0040E3F0</c>). 남은 웅덩이를 낸다.</summary>
    private static int Board(Player player, int pool)
    {
        if (pool <= 0) return 0;
        int room = Math.Max(0, player.MaxCrew - player.Crew);
        if (pool <= room)
        {
            player.AddCrew(pool);
            return 0;
        }
        player.AddCrew(room);
        return pool - room;
    }
}
