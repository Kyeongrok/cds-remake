using CdsHelper.Game.Local.Settings;
using CdsHelper.Support.Local.Helpers;
using CdsHelper.Support.Local.Models;

namespace CdsHelper.Game.Engine.Town;

/// <summary>
/// 왕궁 「망명」 — 리스본 · 세비야 · 런던 왕궁에서 그 나라로 국적을 옮기고 자택도 그 도시로 옮긴다. 원본에 없는 줄이다(모드 「망명」).
/// </summary>
/// <remarks>
/// 명성 <see cref="Fame"/> 이상, 그 왕궁 국왕과의 친밀도 100(<see cref="Player.MaxCloseness"/>), 금화 <see cref="Fee"/> 닢이 든다.
/// 후원자와 계약 중이거나 개인 이야기 의뢰(<see cref="Player.StoryQuestDeadline"/>)를 하는 중에는 안 된다.
/// </remarks>
public static class Exile
{
    /// <summary>망명에 드는 명성.</summary>
    public const int Fame = 50000;

    /// <summary>망명에 바치는 금화.</summary>
    public const int Fee = 1_000_000;

    /// <summary>런던의 도시 번호.</summary>
    private const int LondonCity = 38;

    /// <summary>모드 「망명」을 켰는지.</summary>
    public static bool Enabled => GameSettings.Exile;

    /// <summary>그 도시 왕궁이 받아 주는 나라. 망명을 받는 왕궁이 아니면 null.</summary>
    public static int? NationAt(int cityId) => cityId switch
    {
        CityNames.LisbonId => Skill.Portugal,
        CityNames.SevilleId => Skill.Spain,
        LondonCity => Skill.England,
        _ => null,
    };

    /// <summary>그 도시 왕궁에 「망명」 줄이 서는지 — 모드를 켰고, 받아 주는 왕궁이며, 이미 그 나라 사람이 아닐 때.</summary>
    public static bool Offered(Player player, int cityId) =>
        Enabled && NationAt(cityId) is { } nation && nation != player.Nation;

    /// <summary>망명을 못 하는 까닭(아래 띠에 적는 글). 할 수 있으면 null.</summary>
    /// <param name="closeness">그 왕궁 국왕과의 친밀도. 국왕이 없으면 -1.</param>
    public static string? Blocked(Player player, int closeness)
    {
        if (player.Contract != null) return "후원자와 계약 중에는 망명할 수 없습니다.";
        if (player.StoryQuestDeadline != null) return "의뢰를 수행하는 중에는 망명할 수 없습니다.";
        if (player.Fame < Fame) return "명성치가 모자랍니다! 망명하려면 명성 50000 이 필요합니다.";
        if (closeness < Player.MaxCloseness) return "국왕과의 친밀도가 모자랍니다. 친밀도를 더 쌓아 주십시오.";
        if (player.Gold < Fee) return "돈이 모자랍니다. 망명하려면 금화 1000000닢이 필요합니다.";
        return null;
    }
}
