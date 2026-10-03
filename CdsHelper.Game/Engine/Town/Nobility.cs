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
///               + 교역소 투자 1000 닢마다 1(<see cref="InvestPerMerit"/>, Player.Investments)
///   작위 받기    대본 「작위」(exe-tables/작위.json) — 개인 이야기처럼 단계 카운터로 돈다. 파트 k 가 작위 k → k+1 이다.
///                술집에 들면 「본국 왕궁에서 부른다」, 본국 수도 왕궁에 들면 국왕이 작위를 준다(승리 효과음)
///   작위 값     Player.NobleRank(0 없음 ~ 7 대공) — 대본이 SetStat 41 로 적는다
///   혜택        패시브 표(<see cref="Passives"/>, 도구 앱 「패시브」 창) — 엔진 곳곳이 <see cref="Sum"/> 으로 묻는다. 혜택은 쌓인다
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
    public static readonly int[] Thresholds = [0, 125, 375, 750, 1375, 2250, 3500, 5000];

    /// <summary>대본이 읽는 능력치 칸.</summary>
    public const int MeritStat = 40, RankStat = 41, HomeCapitalStat = 42;

    /// <summary>
    /// 지금 작위에서 그 갈래 패시브의 합 — 모드를 켰을 때만, 작위가 닿은 것만 더한다(쌓인다).
    /// </summary>
    /// <param name="stat">능력치 갈래일 때 어느 능력치인지. 다른 갈래면 안 본다.</param>
    public static int Sum(Player player, PassiveEffect effect, int stat = -1)
    {
        if (!Enabled || player.NobleRank <= 0) return 0;
        int sum = 0;
        foreach (var p in Passives.All)
            if (p.Effect == effect && p.Rank <= player.NobleRank && (effect != PassiveEffect.Ability || p.Stat == stat))
                sum += p.Amount;
        return sum;
    }

    /// <summary>작위 이름. 없으면 「없음」.</summary>
    public static string NameOf(int rank) => rank > 0 && rank < Names.Length ? Names[rank] : "없음";

    /// <summary>
    /// 지금까지 쌓은 공적 — 보고한 발견물마다 그 힌트 등급 x 10, 힌트가 없는 발견물은 5. 모조품이 들통나 도장만 찍힌 것은 안 친다.
    /// </summary>
    public static int MeritOf(Game game) => DiscoveryMeritOf(game) + InvestMeritOf(game.Player.TotalInvested);

    /// <summary>교역소 투자로 쌓이는 공적 — <see cref="InvestPerMerit"/> 닢마다 1.</summary>
    public static int InvestMeritOf(long invested) => (int)Math.Min(int.MaxValue, Math.Max(0, invested) / InvestPerMerit);

    /// <summary>공적 1 이 되는 투자 금액.</summary>
    public const int InvestPerMerit = 1000;

    /// <summary>발견물 보고로 쌓인 공적.</summary>
    public static int DiscoveryMeritOf(Game game)
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
