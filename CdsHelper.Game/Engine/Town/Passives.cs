using CdsHelper.Game.Local.Helpers;
using CdsHelper.Support.Local.Models;

namespace CdsHelper.Game.Engine.Town;

/// <summary>패시브가 무엇을 바꾸는지.</summary>
public enum PassiveEffect
{
    /// <summary>능력치 하나(<see cref="Passive.Stat"/>)를 늘 그만큼 높인다.</summary>
    Ability,

    /// <summary>바다에서 해적 · 이슬람 함대와 마주칠 확률을 그 %만큼 줄인다.</summary>
    EncounterRate,

    /// <summary>바다 재해(쥐 · 병 · 반란 · 폭풍 · 눈보라)가 일어날 확률을 그 %만큼 줄인다.</summary>
    DisasterRate,

    /// <summary>소지품 칸을 그만큼 늘린다.</summary>
    ItemSlots,

    /// <summary>뭍에서 걷는 빠르기를 그 %만큼 올린다.</summary>
    LandSpeed,

    /// <summary>바다에서 함대 빠르기를 그 노트만큼 올린다.</summary>
    SeaSpeed,

    /// <summary>해전에서 내 배가 한 번 쏠 때 그 발만큼 더 쏜다.</summary>
    ExtraShots,

    /// <summary>육상전에서 제독 부대가 그 % 확률로 적 총대장 부대를 먼저 친다.</summary>
    LeaderStrike,
}

/// <summary>
/// 패시브 하나 — <see cref="Rank"/> 작위부터 늘 붙는 혜택이다. 작위가 오르면 아래 것도 다 가진다(쌓인다).
/// </summary>
/// <param name="Name">보이는 이름(「매력 +10」 따위).</param>
/// <param name="Effect">무엇을 바꾸는지.</param>
/// <param name="Amount">얼마나 — 갈래마다 단위가 다르다(<see cref="Passives.UnitOf"/>).</param>
/// <param name="Rank">붙는 작위(1 기사 ~ 7 대공). 0 이면 미배치 — 표에만 있고 어느 작위에도 안 붙는다.</param>
/// <param name="Stat">능력치 갈래일 때 어느 능력치인지(<see cref="Support.Local.Models.Ability"/> 0~5).</param>
public sealed record Passive(string Name, PassiveEffect Effect, int Amount, int Rank, int Stat = 0);

/// <summary>
/// 패시브 표 — 모드 「작위」의 혜택이다. 도구 앱의 「패시브」 창에서 만들고 고친다.
/// </summary>
/// <remarks>
/// 적어 둔 표(<c>exe-tables/패시브.json</c>)가 없으면 <see cref="Defaults"/> 를 쓴다.
/// 엔진은 <see cref="Nobility.Sum"/> 으로 「지금 작위에서 그 갈래의 합」을 묻는다 — 같은 갈래를 여럿 두면 더해진다.
/// </remarks>
public static class Passives
{
    /// <summary>적어 둘 파일 이름.</summary>
    private const string CacheName = "패시브";

    /// <summary>처음 깔린 패시브 — 작위 하나에 하나씩.</summary>
    public static readonly IReadOnlyList<Passive> Defaults =
    [
        new("해적 조우 -20%", PassiveEffect.EncounterRate, 20, 1),
        new("재해 -20%", PassiveEffect.DisasterRate, 20, 2),
        new("소지품 +10칸", PassiveEffect.ItemSlots, 10, 3),
        new("육상 이동 속도 +10%", PassiveEffect.LandSpeed, 10, 4),
        new("적 대장 우선 타격 50%", PassiveEffect.LeaderStrike, 50, 5),
        new("대포 +1회", PassiveEffect.ExtraShots, 1, 6),
        new("항해 속도 +1노트", PassiveEffect.SeaSpeed, 1, 7),
        // 미배치(작위 0) — 표에는 두되 어느 작위에도 안 붙는다. 편집 창에서 작위를 골라 쓴다.
        new("매력 +10", PassiveEffect.Ability, 10, 0, Ability.Charm),
    ];

    /// <summary>갈래 이름 — 편집 창의 고르는 칸에 쓴다.</summary>
    public static string NameOf(PassiveEffect effect) => effect switch
    {
        PassiveEffect.Ability => "능력치 증가",
        PassiveEffect.EncounterRate => "해적 조우 감소",
        PassiveEffect.DisasterRate => "재해 감소",
        PassiveEffect.ItemSlots => "소지품 칸 증가",
        PassiveEffect.LandSpeed => "육상 이동 속도 증가",
        PassiveEffect.SeaSpeed => "항해 속도 증가",
        PassiveEffect.ExtraShots => "대포 발수 증가",
        PassiveEffect.LeaderStrike => "적 대장 우선 타격",
        _ => effect.ToString(),
    };

    /// <summary>갈래마다 양의 단위.</summary>
    public static string UnitOf(PassiveEffect effect) => effect switch
    {
        PassiveEffect.EncounterRate or PassiveEffect.DisasterRate or PassiveEffect.LandSpeed or PassiveEffect.LeaderStrike => "%",
        PassiveEffect.ItemSlots => "칸",
        PassiveEffect.SeaSpeed => "노트",
        PassiveEffect.ExtraShots => "발",
        _ => "",
    };

    /// <summary>한 줄 풀이 — 「매력 +10」 · 「해적 조우 -20%」 따위.</summary>
    public static string Describe(Passive p) => p.Effect switch
    {
        PassiveEffect.Ability => $"{Ability.Names[Math.Clamp(p.Stat, 0, Ability.Names.Length - 1)]} {Signed(p.Amount)}",
        PassiveEffect.EncounterRate => $"해적 조우 {Signed(-p.Amount)}%",
        PassiveEffect.DisasterRate => $"재해 {Signed(-p.Amount)}%",
        PassiveEffect.ItemSlots => $"소지품 {Signed(p.Amount)}칸",
        PassiveEffect.ExtraShots => $"대포 {Signed(p.Amount)}발",
        PassiveEffect.LeaderStrike => $"적 대장 우선 타격 {p.Amount}%",
        _ => $"{NameOf(p.Effect).Replace(" 증가", "")} {Signed(p.Amount)}{UnitOf(p.Effect)}",
    };

    private static string Signed(int n) => n >= 0 ? $"+{n}" : $"{n}";

    /// <summary>JSON 으로 적어 두는 알맹이.</summary>
    internal sealed record Snapshot(List<Passive> Passives);

    private static List<Passive>? _all;

    /// <summary>지금 패시브 전부.</summary>
    public static IReadOnlyList<Passive> All => _all ??= Load();

    /// <summary>표가 바뀌었을 때 알린다.</summary>
    public static event Action? Changed;

    /// <summary>표를 통째로 바꿔 적는다.</summary>
    public static void Save(IEnumerable<Passive> passives)
    {
        _all = [.. passives.Select(Clean).OrderBy(p => p.Rank == 0 ? int.MaxValue : p.Rank)];
        TableCache.Write(CacheName, new TableCache.Cached<Snapshot>(
            $"{_all.Count}개", new Snapshot(_all), "패시브 편집"));
        Changed?.Invoke();
    }

    /// <summary>처음 깔린 것으로 되돌린다.</summary>
    public static void Reset() => Save(Defaults);

    /// <summary>다시 읽는다 — 다른 앱(도구 앱)이 고쳤을 때.</summary>
    public static void Reload() => _all = null;

    private static List<Passive> Load()
    {
        var saved = TableCache.Read<Snapshot>(CacheName)?.Data.Passives;
        return saved is { Count: > 0 } ? [.. saved.Select(Clean)] : [.. Defaults];
    }

    /// <summary>값을 들 수 있는 자리로 자른다.</summary>
    private static Passive Clean(Passive p) => p with
    {
        Name = p.Name ?? "",
        Rank = Math.Clamp(p.Rank, 0, Nobility.MaxRank),
        Stat = Math.Clamp(p.Stat, 0, Ability.Names.Length - 1),
    };
}
