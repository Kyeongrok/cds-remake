using CdsHelper.Game.Engine.Models;
using CdsHelper.Support.Local.Models;
using CdsHelper.Game.Engine.Town;

namespace CdsHelper.Game.Engine.Menu;

/// <summary>
/// 시설 명령 창 — 줄을 세우고 <b>일마다 무엇을 하는지</b> 짝지어 준다.
/// </summary>
/// <remarks>
/// 세 켜다.
/// <list type="number">
///   <item><see cref="TownWorks"/> — 어느 자리의 어느 줄이 무슨 일인지, 그리고 언제 붙고 떨어지는지.</item>
///   <item>여기 — 그 일에 손을 달아 준다. 조건이 어긋나면 <b>손을 안 달아</b> 줄이 흐려진다.</item>
///   <item><see cref="ITownScreen"/> — 실제로 창을 띄우고 돈을 세는 도시 화면.</item>
/// </list>
/// 제목은 건물 이름이다(게임도 그렇다).
///
/// <b>흐린 줄은 지운 줄이 아니다.</b> 게임도 눌리지 않는 줄을 자리에 그대로 둔다 —
/// 이를테면 배를 팔 수 없어도 「선박 매각」은 눌리고, 그때 "기함을 처분하는 일은
/// 불가능합니다!"(<c>0x0044B96F</c>) 가 뜬다. 그래서 그 줄은 여기서 막지 않는다.
/// </remarks>
internal static class TownMenu
{
    /// <summary>
    /// 그 시설의 명령 창 한 벌.
    /// </summary>
    /// <param name="facility">들어와 있는 시설.</param>
    /// <param name="title">제목 — 건물 이름이다.</param>
    /// <param name="code">건물 코드. 수련·해설·성문이 이 값을 쓴다.</param>
    /// <param name="teachMask">건물 표의 가르침 비트.</param>
    /// <param name="patron">이 건물에 앉은 후원자. 없으면 null.</param>
    /// <param name="state">줄이 붙고 떨어지는 조건.</param>
    /// <param name="screen">일을 실제로 하는 도시 화면.</param>
    public static GameMenu Build(Facility facility, string title, int code, uint teachMask,
                                 Patron? patron, in TownWorks.TownState state,
                                 ITownScreen screen)
    {
        var items = TownWorks.LinesOf(facility, state);

        return new GameMenu(title, null,
            [.. items.Select(item => (item, ActionFor(facility, item, code, teachMask, patron, screen)))]);
    }

    /// <summary>
    /// 그 줄이 하는 일에 손을 달아 준다. 흉내낼 수 있는 것만 달리고 나머지는 null 이다.
    /// </summary>
    public static Action? ActionFor(Facility facility, string item, int code, uint teachMask,
                                    Patron? patron, ITownScreen screen)
    {
        // 술집의 술 줄은 일 표에 없다 — 고장마다 이름이 달라 그때그때 붙이기 때문이다.
        if (facility.Kind == FacilityKind.Tavern && screen.IsDrink(item))
            return () => screen.Drink(item);

        return TownWorks.WorkOf(facility, item, TownWorks.Teaches(teachMask), patron != null) switch
        {
            TownWork.Exit when facility.Kind == FacilityKind.Tavern => screen.LeaveTavern,
            TownWork.Exit => screen.CloseMenu,

            // 가르치는 사람은 <b>수련을 눌러야</b> 말을 건다 — 들어서자마자가 아니다.
            TownWork.Train => () => screen.Train(code, teachMask),
            TownWork.System => screen.OpenSystemMenu,

            TownWork.Persuade => () => screen.Persuade(patron!),
            TownWork.Report => () => screen.Report(patron!),
            TownWork.BreakContract => () => screen.BreakContract(patron!),
            TownWork.BribeInspector => () => screen.BribeInspector(patron!),
            TownWork.BorrowShips => () => screen.BorrowShips(patron!),

            // 배가 한 척도 없으면 줄 자체가 흐리다(출항·보급·선원편성 셋이 그렇다).
            TownWork.Sail when screen.HasShips => screen.Sail,
            // 보급은 선원도 있어야 켜진다 — 항구 차림표 표 0x00567CD8 의 보급 줄이 0x00476CE0
            // (출항 조건 && 선원 수 0x0040E360 > 0)을 받는다. 선원이 없으면 실을 양도 셈이 안 선다.
            TownWork.Supply when screen.HasShips && screen.HasCrew => screen.Supply,
            TownWork.CrewForm when screen.HasShips => screen.OpenCrewForm,

            // 성문 — 마을을 나서 뭍을 걷는다. 배는 항구에 그대로 둔다.
            TownWork.Explore => () => screen.Explore(code),

            // 발견한 건물의 해설 — 그림 한 장과 그 이야기다.
            TownWork.Comment => () => screen.ShowComment(code),

            // 술집 주인이 계약한 것의 실마리를 준다 — 술을 한잔 사야 입을 연다.
            TownWork.Info => screen.HearInfo,

            // 포카는 유럽 세 문화권 술집에서만 줄이 산다 — 나머지는 회색 줄이다(0x0042FB00).
            TownWork.Treat when screen.CanPlayPoker => screen.PlayPoker,

            // 함대편성은 그 안의 네 줄이 다 막히면 저도 흐려진다.
            TownWork.FleetForm when screen.CanFormFleet => screen.OpenFleetForm,
            TownWork.CityInfo => screen.ShowPortCityInfo,
            TownWork.Announce => screen.Announce,

            TownWork.BuyShip => screen.BuyShip,
            // 함대가 닿아 있으면 켜진다(0x0044BD60). 기함뿐이면 눌러야 막힌다(0x0044B863 의 cmp esi,1 / jle).
            TownWork.SellShip when screen.CanSellShip => screen.SellShip,
            // 게임도 고칠 배가 없으면 이 줄을 흐리게 둔다(0x0044BD40).
            TownWork.RepairShip when screen.CanRepairShip => screen.RepairShip,
            TownWork.RefitShip when screen.CanRefitShip => screen.RefitShip,

            TownWork.BuyGoods when screen.CanBuyGoods => screen.BuyGoods,
            TownWork.SellGoods when screen.CanSellGoods => screen.SellGoods,
            TownWork.Trade when screen.CanTrade => screen.Trade,
            TownWork.Talk when screen.CanTrade => screen.TradeTalk,

            TownWork.Stay => screen.Stay,
            TownWork.OddJob => screen.OddJob,
            // 부하편성은 부하가 있어야 눌린다 — 여관(0x0047FE3D)도 술집(0x0042FE87)도 0x00453CA0() > 0 을 켜짐 칸에.
            TownWork.MateForm when screen.HasMates => screen.ShowMates,

            TownWork.Heir when screen.CanLeaveHeir => screen.LeaveHeir,
            TownWork.Succeed when screen.CanSucceed => screen.Succeed,
            TownWork.Educate when screen.CanEducate => screen.Educate,
            TownWork.Rest => screen.OpenRestMenu,
            TownWork.Savings => screen.OpenSavingsMenu,
            // 게임도 지닌 것이 없으면 이 줄을 흐리게 둔다 — 맡길 것이 없으면 열 일도 없다.
            TownWork.Store when screen.HasItems => screen.OpenStorage,

            // 백과사전 — 갈래마다 한 권씩, 발견한 것이 한 쪽씩 쌓인다.
            TownWork.Encyclopedia => screen.ShowEncyclopedia,
            TownWork.Chronicle => screen.ShowChronicle,
            TownWork.Retire => screen.Retire,
            TownWork.Read when screen.CanRead => screen.ReadBooks,

            _ => null,
        };
    }
}
