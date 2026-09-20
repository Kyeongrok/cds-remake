using CdsHelper.Game.Local.Helpers;
using CdsHelper.Support.Local.Models;

namespace CdsHelper.Game.Engine.Town;

/// <summary>술집에서 치르는 값.</summary>
public static class Tavern
{
    /// <summary>
    /// 한잔 사는 값. 게임은 도시마다 파는 술이 달라 값도 다른데(술 표 <c>0x4FF978</c>)
    /// 아직 그 표를 안 읽어 한 값으로 둔다.
    /// </summary>
    public const int DrinkPrice = 10;

    /// <summary>게임 세이브의 인물 한 줄을 우리 부하 신상으로 옮긴다.</summary>
    public static Player.MateInfo MateInfoOf(TavernRoster.Person who) =>
        new(who.Name, who.FaceCode, who.Fame, who.Age,
            who.Body, who.Mind, who.Might, who.Charm, who.Luck,
            who.Sword, who.Shooting, who.Gunnery, Player.ConditionFull,
            who.Sailing, who.Handling, who.Medicine, who.Science);
}
