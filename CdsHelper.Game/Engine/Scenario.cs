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
public sealed record Scenario(
    string Book, string Title, string Given, string Family,
    int BirthYear, int Month, int Day, int Face, int Nation, int Blood, int Job,
    int[] Abilities, int[] Skills, int[] Tongues, int Gold, int Fame)
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

    /// <summary>JSON 으로 적어 두는 알맹이.</summary>
    internal sealed record Snapshot(List<Scenario> Scenarios);

    private static List<Scenario>? _all;

    /// <summary>지어 둔 시나리오 전부. 하나도 없으면 빈 목록.</summary>
    public static IReadOnlyList<Scenario> All =>
        _all ??= TableCache.Read<Snapshot>(CacheName)?.Data.Scenarios.Where(Sound).ToList() ?? [];

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
        TableCache.Write(CacheName, new TableCache.Cached<Snapshot>("", new Snapshot(_all), "손으로 지음"));
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
        && s.Nation is 0 or 1 && s.Job is >= 0 and < 4 && s.Month is >= 1 and <= 12 && s.Day is >= 1 and <= 31;
}
