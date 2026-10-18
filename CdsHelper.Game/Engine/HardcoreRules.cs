using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using CdsHelper.Game.Local.Settings;
using CdsHelper.Support.Local.Models;

namespace CdsHelper.Game.Engine;

/// <summary>
/// 하드코어 판이 <b>한 번이라도</b> 켠 · 줄인 모드와 쓴 개발 기능을 적어 두고, 고치지 못하게 봉인한다 — 원본에 없는 것이다.
/// </summary>
/// <remarks>
/// 랭킹(<c>tools/stats-worker</c> 의 <c>/ranking</c>)에 발견물 수와 함께 이 표가 올라간다.
/// <code>
///   적는 때    하드코어로 시작할 때 · 설정이 바뀔 때마다(GameSettings.Changed) · 적을 때 · 게임 오버 때
///   적는 것    아래 Switches 가 켜져 있으면 그 이름, Levels 가 원본보다 쉬운 쪽이면 「이름=값」, 개발 기능을 쓰면 dev:…
///   봉인       세이브에 HMAC-SHA256(열쇠, 판 표지 | 열쇠들) 을 함께 적는다. 불러올 때 안 맞으면 「tampered」를 더한다
///   서명       랭킹에 보낼 때 같은 열쇠로 기록 전부에 서명하고, 받는 곳이 같은 열쇠로 대 본다
/// </code>
/// 열쇠는 릴리즈 빌드가 GitHub 비밀(<c>HARDCORE_KEY</c>)로 박는다 — 저장소에는 없다. 손으로 빌드한 판은 열쇠가 없어
/// 봉인은 「dev」로 하고 랭킹에는 안 보낸다. exe 를 뜯으면 열쇠를 꺼낼 수 있으니 막는 것은 세이브를 손으로 고치는 정도다.
/// </remarks>
public static class HardcoreRules
{
    /// <summary>켜 두면 적는 모드 — 플레이에 크게 손을 대는 것들이다.</summary>
    private static readonly (string Name, Func<bool> On)[] Switches =
    [
        (nameof(GameSettings.SuezCanal), () => GameSettings.SuezCanal),
        (nameof(GameSettings.NoWeight), () => GameSettings.NoWeight),
        (nameof(GameSettings.RichCap), () => GameSettings.RichCap),
        (nameof(GameSettings.LongRest), () => GameSettings.LongRest),
        (nameof(GameSettings.SkipMinigames), () => GameSettings.SkipMinigames),
        (nameof(GameSettings.SphinxHelper), () => GameSettings.SphinxHelper),
        (nameof(GameSettings.DuelImmortal), () => GameSettings.DuelImmortal),
        (nameof(GameSettings.AutoFundRaise), () => GameSettings.AutoFundRaise),
        (nameof(GameSettings.Nobility), () => GameSettings.Nobility),
        (nameof(GameSettings.LandAutoWalk), () => GameSettings.LandAutoWalk),
        (nameof(GameSettings.SpecialOrder), () => GameSettings.SpecialOrder),
        (nameof(GameSettings.MiniMapYet), () => GameSettings.ShowMiniMap && GameSettings.MiniMapYet),
        (nameof(GameSettings.ShowDiscoveryMapMenu), () => GameSettings.ShowDiscoveryMapMenu),
        (nameof(GameSettings.RerollOnJob), () => GameSettings.RerollOnJob),
        (nameof(GameSettings.MonsterRetry), () => GameSettings.MonsterRetry),
    ];

    /// <summary>원본 값에서 쉬운 쪽으로 옮기면 「이름=값」으로 적는 단계 모드.</summary>
    private static readonly (string Name, Func<int> Value, Func<int, bool> Eased)[] Levels =
    [
        // 해적 · 이슬람 함대가 붙는 배수 — 원본 x1(차례 3)보다 낮으면.
        (nameof(GameSettings.SeaRaidScale), () => GameSettings.SeaRaidScale, v => v < GameSettings.DefaultSeaRaidScale),
        // 바다 반란 몫 — 원본 100% 보다 낮으면.
        (nameof(GameSettings.MutinyRate), () => GameSettings.MutinyRate, v => v < 100),
        // 마을 · 항구에 들고 날 때의 날수 — 원본 10일보다 짧으면.
        (nameof(GameSettings.PortDays), () => GameSettings.PortDays, v => v < GameSettings.DefaultPortDays),
        // 인물이 떠날지 굴리는 간격 · 떠날 확률 · 쉬는 날수 — 원본과 다르면.
        (nameof(GameSettings.PersonRollDays), () => GameSettings.PersonRollDays, v => v != 0),
        (nameof(GameSettings.PersonMoveOdds), () => GameSettings.PersonMoveOdds, v => v != GameSettings.DefaultPersonMoveOdds),
        (nameof(GameSettings.PersonRestDays), () => GameSettings.PersonRestDays, v => v != GameSettings.DefaultPersonRestDays),
        // 정보 제공 등급 — 원본(기본)보다 더 보이면.
        (nameof(GameSettings.InfoLevel), () => GameSettings.InfoLevel, v => v > 0),
    ];

    /// <summary>세이브 봉인이 안 맞을 때 붙는 열쇠.</summary>
    public const string Tampered = "tampered";

    /// <summary>지금 판의 주인공 — 게임 창이 건다. 게임이 없으면 null.</summary>
    public static Func<Player?>? Current { get; set; }

    /// <summary>지금 켜 둔 · 줄인 모드의 열쇠들.</summary>
    public static IEnumerable<string> Active()
    {
        foreach (var (name, on) in Switches)
            if (on()) yield return name;
        foreach (var (name, value, eased) in Levels)
            if (value() is var v && eased(v)) yield return $"{name}={v}";
        // 도구 앱으로 고친 표(인물 · 경쟁자 · 도시 따위의 수치) — 「이름-고친것.json」이 있으면 그 표를 고친 것이다.
        foreach (var name in EditedTables()) yield return $"edited:{name}";
    }

    /// <summary>도구 앱이 고쳐 둔 표 이름들(<c>exe-tables\이름-고친것.json</c>).</summary>
    private static IEnumerable<string> EditedTables()
    {
        // 파일 이름은 ASCII 라(「rumors-added.json」 따위) 표 이름으로 되짚어 본다(TableCache.NamesEndingWith).
        const string suffix = "-고친것";
        return Local.Helpers.TableCache.NamesEndingWith(suffix).Select(name => name[..^suffix.Length]);
    }

    /// <summary>하드코어 판이면 지금 켜 둔 것을 더한다. 끈 것은 안 지운다.</summary>
    public static void Note(Player? player)
    {
        if (player is not { Hardcore: true }) return;
        player.HardcoreMarks.UnionWith(Active());
    }

    /// <summary>하드코어 판이면 그 열쇠를 더한다 — 개발 기능 · Ctrl+클릭 배 놓기처럼 <b>쓴 것</b>을 적는다.</summary>
    public static void Mark(string key, Player? player = null)
    {
        player ??= Current?.Invoke();
        if (player is { Hardcore: true }) player.HardcoreMarks.Add(key);
    }

    /// <summary>릴리즈 빌드가 박은 열쇠. 없으면 빈 글.</summary>
    public static string Key { get; } =
        typeof(HardcoreRules).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(a => a.Key == "HardcoreKey")?.Value ?? "";

    /// <summary>세이브에 적을 봉인.</summary>
    public static string Seal(Player player) => Hmac($"{player.GameId}|{string.Join(",", Sorted(player.HardcoreMarks))}");

    /// <summary>불러온 열쇠들이 봉인과 맞는지.</summary>
    public static bool Verify(string gameId, IEnumerable<string> marks, string? seal) =>
        seal != null && CryptographicOperations.FixedTimeEquals(
            Encoding.ASCII.GetBytes(seal), Encoding.ASCII.GetBytes(Hmac($"{gameId}|{string.Join(",", Sorted(marks))}")));

    /// <summary>랭킹 기록의 서명 — 받는 곳(<c>postHardcore</c>)과 같은 차례로 이어 붙인다.</summary>
    public static string Sign(string gameId, string name, string nation, int discoveries, string died, int cause, IEnumerable<string> marks) =>
        Hmac($"{gameId}|{name}|{nation}|{discoveries}|{died}|{cause}|{string.Join(",", Sorted(marks))}");

    /// <summary>열쇠들을 늘 같은 차례로 — 서명이 차례에 매이지 않게.</summary>
    public static List<string> Sorted(IEnumerable<string> marks) => [.. marks.Distinct().OrderBy(m => m, StringComparer.Ordinal)];

    private static string Hmac(string text)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(Key.Length > 0 ? Key : "dev"));
        return Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();
    }
}
