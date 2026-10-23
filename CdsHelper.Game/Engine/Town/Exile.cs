using CdsHelper.Game.Local.Settings;
using CdsHelper.Support.Local.Helpers;
using CdsHelper.Support.Local.Models;

namespace CdsHelper.Game.Engine.Town;

/// <summary>
/// 왕궁 「망명」 — 리스본 · 세비야 · 런던 왕궁과 제노바 총독부(코드 2 라 왕궁 갈래다)에서 그 나라로 국적을 옮기고 자택도 그 도시로 옮긴다. 원본에 없는 줄이다(모드 「망명」).
/// </summary>
/// <remarks>
/// 명성 <see cref="Fame"/> 이상과 그 왕궁 국왕과의 친밀도 100(<see cref="Player.MaxCloseness"/>)이 있어야 하고,
/// 망명하면 금화 <see cref="Fee"/> 닢과 명성 <see cref="FameCost"/> 이 깎인다.
/// 후원자와 계약 중이거나 개인 이야기 의뢰(<see cref="Player.StoryQuestDeadline"/>)를 하는 중에는 안 된다.
/// </remarks>
public static class Exile
{
    /// <summary>망명에 드는 명성.</summary>
    public const int Fame = 60000;

    /// <summary>망명하면 깎이는 명성.</summary>
    public const int FameCost = 50000;

    /// <summary>망명에 바치는 금화.</summary>
    public const int Fee = 1_000_000;

    /// <summary>제노바 · 런던의 도시 번호.</summary>
    private const int GenoaCity = 28, LondonCity = 38;

    /// <summary>모드 「망명」을 켰는지.</summary>
    public static bool Enabled => GameSettings.Exile;

    /// <summary>그 도시 왕궁이 받아 주는 나라. 망명을 받는 왕궁이 아니면 null.</summary>
    public static int? NationAt(int cityId) => cityId switch
    {
        CityNames.LisbonId => Skill.Portugal,
        CityNames.SevilleId => Skill.Spain,
        GenoaCity => Skill.Genoa,
        LondonCity => Skill.England,
        _ => null,
    };

    /// <summary>
    /// 그 도시 왕궁에 「망명」 줄이 서는지 — 모드를 켰고 망명을 받는 왕궁이며 모국이 아니면, 조건이 안 돼도 선다(흐리게, <see cref="Ready"/>).
    /// </summary>
    public static bool Offered(Player player, int cityId) =>
        Enabled && NationAt(cityId) is { } nation && nation != player.Nation;

    /// <summary>지금 망명할 수 있는지 — 아니면 줄을 흐리게 낸다.</summary>
    /// <param name="closeness">그 왕궁 국왕과의 친밀도. 국왕이 없으면 -1.</param>
    public static bool Ready(Player player, int cityId, int closeness) =>
        NationAt(cityId) is { } nation && nation != player.Nation
        && Busy(player) == null && Trusted(closeness) && Affords(player);

    /// <summary>
    /// 설명 창을 띄우기도 전에 막는 까닭(아래 띠에 적는 글) — 후원자와 계약 중이거나 의뢰를 수행하는 중이다. 없으면 null.
    /// </summary>
    public static string? Busy(Player player)
    {
        if (player.Contract != null) return "후원자와 계약 중에는 망명할 수 없습니다.";
        // 기한 칸은 의뢰를 마쳐도 안 지워진다 — 기한이 아직 남았고 이야기 책이 안 닫혔을 때만 수행 중으로 본다(제보: 의뢰가 없는데 막혔다).
        if (player.StoryQuestDaysLeft > 0 && !(player.ActiveStoryBook is { } book && player.IsStoryArcClosed(book)))
            return "의뢰를 수행하는 중에는 망명할 수 없습니다.";
        return null;
    }

    /// <summary>그 왕궁 국왕과의 친밀도가 다 찼는지. 국왕이 없으면 -1 을 넘긴다.</summary>
    public static bool Trusted(int closeness) => closeness >= Player.MaxCloseness;

    /// <summary>명성 · 금화가 다 갖춰졌는지.</summary>
    public static bool Affords(Player player) => player.Fame >= Fame && player.Gold >= Fee;
}
