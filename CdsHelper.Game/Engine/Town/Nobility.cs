using CdsHelper.Game.Local.Settings;
using CdsHelper.Support.Local.Models;

namespace CdsHelper.Game.Engine.Town;

/// <summary>
/// 모드 「작위」 — 발견물을 보고해 <b>공적</b>을 쌓고, 본국 왕궁에서 작위를 받는다. 작위마다 혜택이 붙는다.
/// </summary>
/// <remarks>
/// 원본에 없는 제도다. 나뉜 몫은 이렇다.
/// <code>
///   공적        보고한 발견물마다 그 힌트 등급 x 10(힌트가 없으면 5) — 보고 기록에서 그때그때 센다(따로 안 적는다)
///   작위 받기    대본 「작위」(exe-tables/작위.json) — 개인 이야기처럼 단계 카운터로 돈다. 파트 k 가 작위 k → k+1 이다.
///                술집에 들면 「본국 왕궁에서 부른다」, 본국 수도 왕궁에 들면 국왕이 작위를 준다(승리 효과음)
///   작위 값     Player.NobleRank(0 없음 ~ 7 대공) — 대본이 SetStat 41 로 적는다
///   혜택        여기 표(<see cref="Perks"/>) — 엔진 곳곳이 <see cref="Has"/> · <see cref="Effective"/> 로 묻는다
/// </code>
/// 대본이 읽는 값은 능력치 칸 40 공적 · 41 작위 · 42 본국 수도에 있는지(1/0)다(DisevRunner.ValueOf).
/// </remarks>
public static class Nobility
{
    /// <summary>모드를 켰는지.</summary>
    public static bool Enabled => GameSettings.Nobility;

    /// <summary>작위 이름. 0 은 없음이다.</summary>
    public static readonly string[] Names = ["", "기사", "남작", "자작", "백작", "후작", "공작", "대공"];

    /// <summary>가장 높은 작위(대공).</summary>
    public const int MaxRank = 7;

    /// <summary>작위마다 넘어야 할 누적 공적. 대본 「작위」의 조건과 같은 값이다.</summary>
    public static readonly int[] Thresholds = [0, 25, 75, 150, 275, 450, 700, 1000];

    /// <summary>대본이 읽는 능력치 칸.</summary>
    public const int MeritStat = 40, RankStat = 41, HomeCapitalStat = 42;

    /// <summary>혜택 갈래.</summary>
    public enum Perk { SponsorView, AutoCrew, HintView, AutoSupply, AutoFlee, Inventory, Speed, Cannon }

    /// <summary>
    /// 혜택 표 — 효과가 작은 것을 낮은 작위에, 큰 것을 높은 작위에 둔다. 대공은 앞의 혜택을 다 가진 자리다(덧붙일 혜택은 나중에).
    /// </summary>
    public static readonly (Perk Perk, int Rank, string Name, string Text)[] Perks =
    [
        (Perk.SponsorView, 1, "향상된 스폰서보기", "스폰서 일람에 권력 · 친밀도가 보인다"),
        (Perk.AutoCrew,    1, "선원 자동 모집",   "입항하면 함대 필요 선원만큼 자동으로 모은다"),
        (Perk.HintView,    2, "향상된 힌트보기",   "취득 힌트 일람을 목록 · 설명 두 칸으로 본다"),
        (Perk.AutoSupply,  2, "자동 보급",        "입항하면 물 · 식량을 자동으로 채운다"),
        (Perk.AutoFlee,    3, "자동 도망",        "바다에서 해적을 만나면 자동으로 달아난다"),
        (Perk.Inventory,   4, "소지품 +10칸",     $"소지품을 {Player.MaxItems + ExtraItemSlots}칸까지 지닌다"),
        (Perk.Speed,       5, "항해 속도 +1노트", "바다에서 함대가 1노트 빨라진다"),
        (Perk.Cannon,      6, "대포 +1회",        "해전에서 한 번 쏠 때 한 발 더 쏜다"),
    ];

    /// <summary>자작 혜택으로 느는 소지품 칸.</summary>
    public const int ExtraItemSlots = 10;

    /// <summary>그 혜택이 붙는 작위.</summary>
    public static int RankOf(Perk perk) => Perks.First(p => p.Perk == perk).Rank;

    /// <summary>그 혜택을 가졌는지 — 모드를 켰고 작위가 닿았을 때.</summary>
    public static bool Has(Player player, Perk perk) => Enabled && player.NobleRank >= RankOf(perk);

    /// <summary>
    /// 따로 켜고 끄던 모드(자동 보급 따위)와 겹치는 혜택 — <b>작위 모드를 켜면 작위가 정하고</b>, 끄면 그 모드 토글대로다.
    /// </summary>
    public static bool Effective(Player player, bool toggle, Perk perk) => Enabled ? Has(player, perk) : toggle;

    /// <summary>작위 이름. 없으면 「없음」.</summary>
    public static string NameOf(int rank) => rank > 0 && rank < Names.Length ? Names[rank] : "없음";

    /// <summary>
    /// 지금까지 쌓은 공적 — 보고한 발견물마다 그 힌트 등급 x 10, 힌트가 없는 발견물은 5. 모조품이 들통나 도장만 찍힌 것은 안 친다.
    /// </summary>
    public static int MeritOf(Game game)
    {
        var player = game.Player;
        var table = game.Discoveries?.Table;
        var hints = game.Hints;
        int merit = 0;
        foreach (int id in player.Announced)
        {
            if (player.Unresolved.Contains(id)) continue;
            int serial = table?.Find(id)?.Hint ?? -1;
            int grade = serial >= 0 && hints != null
                ? hints.Hints.FirstOrDefault(h => h.Discovery == serial).Grade
                : 0;
            merit += grade > 0 ? grade * 10 : 5;
        }
        return merit;
    }

    /// <summary>그 공적이면 받을 수 있는 가장 높은 작위.</summary>
    public static int Earned(int merit)
    {
        int rank = 0;
        for (int r = 1; r <= MaxRank; r++) if (merit >= Thresholds[r]) rank = r;
        return rank;
    }

    /// <summary>다음 작위까지 필요한 공적. 대공이면 0.</summary>
    public static int ToNext(int rank, int merit) =>
        rank >= MaxRank ? 0 : Math.Max(0, Thresholds[rank + 1] - merit);

    /// <summary>지금 본국 수도에 있는지 — 대본의 「본국 왕궁」 조건(능력치 칸 42)이 읽는다.</summary>
    public static bool InHomeCapital(Game game) =>
        game.Nations?.Find(game.Player.Nation)?.Capital is { } capital && capital >= 0 && capital == game.Player.CityId;

    /// <summary>모드를 켰을 때 돌리는 전역 대본 — 직업과 상관없이 늘 돈다.</summary>
    public const string BookName = "작위";
}
