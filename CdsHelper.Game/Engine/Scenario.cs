using System.IO;
using CdsHelper.Game.Engine.Disev;
using CdsHelper.Game.Local.Helpers;
using CdsHelper.Game.Local.Settings;

namespace CdsHelper.Game.Engine;

/// <summary>
/// 손으로 지은 시나리오 한 편 — 미리 만든 주인공 한 사람과 그 사람이 드는 이야기 책.
/// </summary>
/// <remarks>
/// 원본에 없는 것이다. 초심자용 주인공 둘(<see cref="Beginner"/>)과 같은 길로 앉히되, 사람과 책을 표
/// (<c>exe-tables/시나리오목록.json</c>)에서 읽는다. 책은 <see cref="DisevBook"/> 이 다루는 대본 책 그대로라
/// 대본 편집기로 장면을 짓는다. 모드 「시나리오 모드」를 켜야 NEW GAME 차림표에 나온다.
/// </remarks>
/// <param name="Book">이야기 책 이름 — <c>exe-tables/{Book}.json</c> 에 적힌다.</param>
/// <param name="Title">차림표에 뜨는 제목.</param>
/// <param name="Abilities">능력 여섯(체력 · 지력 · 무력 · 매력 · 운 · 신앙심).</param>
/// <param name="Skills">기능 열셋(<see cref="Support.Local.Models.Skill.Names"/> 차례).</param>
/// <param name="Tongues">언어 여섯(<see cref="Support.Local.Models.Skill.Languages"/> 차례).</param>
/// <param name="Nation">나라 표의 번호 — 고르는 둘(0 · 1)만이 아니라 어느 나라든 된다.</param>
/// <param name="HomeCity">시작 도시이자 모항. -1 이면 그 나라의 수도다.</param>
/// <param name="HomeX">자택 건물 상자의 왼쪽 위(도시 그림 좌표) — 그 도시에 자택이 없을 때 그림 위에 올린다.</param>
public sealed record Scenario(
    string Book, string Title, string Given, string Family,
    int BirthYear, int Month, int Day, int Face, int Nation, int Blood, int Job,
    int[] Abilities, int[] Skills, int[] Tongues, int Gold, int Fame,
    int HomeCity = -1, int HomeX = Scenarios.DefaultHomeX, int HomeY = Scenarios.DefaultHomeY)
{
    /// <summary>앉힐 주인공 — 초심자용 주인공과 같은 모양이다.</summary>
    public Beginner.Hero Hero => new(Given, Family, BirthYear, Month, Day, Face, Nation, Blood, Job,
                                     Abilities, Skills, Tongues, Gold, Book);
}

/// <summary>시나리오 표(<c>exe-tables/시나리오목록.json</c>).</summary>
public static class Scenarios
{
    /// <summary>적어 둘 파일 이름.</summary>
    private const string CacheName = "시나리오목록";

    /// <summary>책 이름의 머리 — 뒤에 번호가 붙는다(<c>시나리오책1</c>).</summary>
    private const string BookPrefix = "시나리오책";

    /// <summary>JSON 으로 적어 두는 알맹이. <c>Nations</c> 는 시나리오가 들고 오는 나라(원본 나라 표 뒤에 붙는다)다.</summary>
    internal sealed record Snapshot(List<Scenario> Scenarios, List<NationTable.Nation>? Nations = null);

    private static List<Scenario>? _all;
    private static List<NationTable.Nation>? _nations;

    /// <summary>
    /// 시나리오가 들고 오는 나라 — 원본 나라 표(0~77) 뒤에 붙는다(<see cref="Table.NationTable.Find"/>).
    /// 루터파처럼 원본에 없는 나라를 대본이 주인공의 나라로 삼을 때 쓴다.
    /// </summary>
    public static IReadOnlyList<NationTable.Nation> ExtraNations =>
        _nations ??= TableCache.Read<Snapshot>(CacheName)?.Data.Nations ?? [];

    /// <summary>지어 둔 시나리오 전부. 하나도 없으면 빈 목록.</summary>
    public static IReadOnlyList<Scenario> All =>
        _all ??= TableCache.Read<Snapshot>(CacheName)?.Data.Scenarios.Where(Sound).ToList() ?? [];

    /// <summary>자택 자리를 안 골랐을 때의 밑값 — 그림 가운데쯤이다.</summary>
    public const int DefaultHomeX = 152, DefaultHomeY = 120;

    /// <summary>
    /// 그 도시에 시나리오가 올린 자택 자리(건물 상자의 왼쪽 위). 그 도시를 본거지로 삼은 시나리오가 없으면 null.
    /// </summary>
    /// <remarks>
    /// 원본 건물 표에는 자택이 리스본 · 세비야에만 있다. 다른 도시를 본거지로 삼으면 건물 줄을 하나 지어 얹고
    /// (<see cref="Local.Helpers.CityBuildingTable.InCity"/>), 그림은 도시 그림 위에 따로 올린다(<see cref="Local.Helpers.BuildingSprites.Home"/>).
    /// 자택은 모항에서만 서므로(CityPicView.Standing) 다른 주인공에게는 안 보인다.
    /// </remarks>
    public static (int X, int Y)? HomeSpot(int cityId)
    {
        if (cityId < 0) return null;
        foreach (var s in All)
            if (s.HomeCity == cityId) return (s.HomeX, s.HomeY);
        return null;
    }

    /// <summary>모드 「시나리오 모드」를 켰는지.</summary>
    public static bool Enabled => GameSettings.ScenarioMode;

    /// <summary>그 책을 든 시나리오. 없으면 null.</summary>
    public static Scenario? Find(string? book) => book == null ? null : All.FirstOrDefault(s => s.Book == book);

    /// <summary>시나리오의 이야기 책인지.</summary>
    public static bool IsBook(string? book) => Find(book) != null;

    /// <summary>표를 통째로 갈아 적는다.</summary>
    public static void Save(IEnumerable<Scenario> scenarios)
    {
        _all = [.. scenarios.Where(Sound)];
        // 나라는 이 창에서 안 고친다 — 적어 둔 것을 그대로 다시 싣는다.
        TableCache.Write(CacheName, new TableCache.Cached<Snapshot>("", new Snapshot(_all, [.. ExtraNations]), "손으로 지음"));
    }

    /// <summary>아직 안 쓴 책 이름.</summary>
    public static string NextBook(IEnumerable<Scenario> taken)
    {
        var used = taken.Select(s => s.Book).ToHashSet();
        for (int n = 1; ; n++)
            if (!used.Contains(BookPrefix + n) && !File.Exists(TableCache.PathFor(BookPrefix + n)))
                return BookPrefix + n;
    }

    /// <summary>빈 시나리오 — 능력치는 가운데 값, 기능 · 언어는 0 이다.</summary>
    public static Scenario Blank(string book) =>
        new(book, "새 시나리오", "이름", "성", 1456, 1, 1, Face: 0, Nation: 0, Blood: 0, Job: 0,
            Abilities: [60, 60, 60, 60, 60, 60],
            Skills: new int[Support.Local.Models.Skill.Names.Length],
            Tongues: new int[Support.Local.Models.Skill.Languages.Length],
            Gold: 5000, Fame: Beginner.Fame);

    /// <summary>앉혀도 터지지 않을 줄인지 — 손으로 고친 파일이 어긋났을 때를 거른다.</summary>
    private static bool Sound(Scenario s) =>
        !string.IsNullOrWhiteSpace(s.Book) && s.Abilities is { Length: 6 } && s.Skills != null && s.Tongues != null
        && s.Nation >= 0 && s.Job is >= 0 and < 4 && s.Month is >= 1 and <= 12 && s.Day is >= 1 and <= 31;
}
