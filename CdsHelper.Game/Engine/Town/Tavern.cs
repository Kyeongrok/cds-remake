using CdsHelper.Game.Local.Helpers;
using CdsHelper.Support.Local.Models;

namespace CdsHelper.Game.Engine.Town;

/// <summary>술집에서 치르는 값.</summary>
public static class Tavern
{
    /// <summary>게임 세이브의 인물 한 줄을 우리 부하 신상으로 옮긴다.</summary>
    public static Player.MateInfo MateInfoOf(TavernRoster.Person who) =>
        new(who.Name, who.FaceCode, who.Fame, who.Age,
            who.Body, who.Mind, who.Might, who.Charm, who.Luck,
            who.Sword, who.Shooting, who.Gunnery, Player.ConditionFull,
            who.Sailing, who.Handling, who.Medicine, who.Science);
}
