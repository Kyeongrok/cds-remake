using CdsHelper.Support.Local.Models;

namespace CdsHelper.Game.Engine.Sea;

/// <summary>바다에서 하루를 넘길 때 일어날 수 있는 일.</summary>
/// <remarks>
/// 게임은 일곱 갈래를 굴리고(<c>0x004746CD</c> 의 <c>rand(7)</c>) 갈래마다 딴 처리로
/// 뛴다(점프표 <c>0x00474D7C</c>).
/// <code>
///   0  0x004746F9   쥐          3  0x00474B4A  반란
///   1  0x00474812   괴혈병      4  0x00474BD5  폭풍
///   2  0x004749FA   전염병      5  0x00474BD5  눈보라
///                               6  0x004746F9  쥐(0 과 같은 자리)
/// </code>
/// 예전에는 0·1·2 를 암초·무풍·병으로 적어 두었는데 <b>틀렸다</b>. 갈래마다 부르는
/// 문구를 보면 갈린다 — <c>0x00534D38</c> "쥐가 발생하기…" · <c>0x00534F30</c>
/// "…괴혈병에 걸려…" · <c>0x00535090</c> "…전염병으로 인해…" 다.
/// </remarks>
public enum SeaEventKind
{
    /// <summary>폭풍. 갈래 넷.</summary>
    Storm,

    /// <summary>눈보라. 갈래 다섯.</summary>
    Blizzard,

    /// <summary>쥐. 갈래 0 과 6.</summary>
    Rats,

    /// <summary>괴혈병. 갈래 하나.</summary>
    Scurvy,

    /// <summary>전염병. 갈래 둘.</summary>
    Plague,

    /// <summary>괴혈병이 돌기 전의 귀띔 — 터지지는 않는다.</summary>
    Weakening,

    /// <summary>전염병이 돌기 전의 귀띔 — 터지지는 않는다.</summary>
    StrangeIllness,

    /// <summary>괴혈병을 보리로 눌러 앉힌다 — 의학이나 과학이 <b>제독의 것</b>일 때.</summary>
    BarleyByMe,

    /// <summary>괴혈병을 보리로 눌러 앉힌다 — 그 기능이 <b>부관의 것</b>일 때.</summary>
    BarleyByMate,

    /// <summary>부관이 쥐를 미리 퇴치한다 — 재해로는 안 남는다.</summary>
    RatsKilled,

    /// <summary>반란. 갈래 셋.</summary>
    Mutiny,
}

/// <summary>폭풍이 지나간 뒤에 남은 것.</summary>
/// <param name="Kind">폭풍인지 눈보라인지.</param>
/// <param name="Hurt">배마다 깎인 값. 추진력과 내구를 <b>같은 만큼</b> 깎는다. 함대 차례대로다.</param>
/// <param name="Lost">놓친 배의 이름.</param>
public sealed record SeaEventResult(SeaEventKind Kind, IReadOnlyList<int> Hurt,
                                    IReadOnlyList<string> Lost)
{
    /// <summary>어느 배든 상했는지.</summary>
    public bool AnyHurt => Hurt.Any(h => h > 0);

    /// <summary>폭풍이면 "폭풍", 눈보라면 "눈보라". 게임 문구에 그대로 끼운다.</summary>
    public string Word => Kind == SeaEventKind.Storm ? "폭풍" : "눈보라";
}

/// <summary>
/// 바다 사건 판정. 게임의 <c>0x00474680</c>(일어나는가)과 <c>0x00474DA0</c>(뒷정리)을
/// 옮긴 것이다.
/// </summary>
/// <remarks>
/// <code>
/// ; 일어나는가  0x00474680
/// 474680  if ([0x5A4D40] &lt;= 9) return           ; 열흘 넘게 항해했을 때만
/// 4746a6  edi = [항해사+0x40] * 25
/// 4746b4  edi += [0x5B60D4] + 0x1A              ; 항해 능력 + 26
/// 4746c5  if (edi &gt;= rand(200)) return          ; 안 일어난다
/// 4746cd  edi = rand(7)                          ; 갈래
/// 4746db  if (그 갈래 비트가 이미 서 있으면) return
/// 4746f2  jmp *0x474D7C[edi*4]
/// </code>
/// 사건 갈래는 함대 객체 <c>+0xD4</c> 의 비트 하나씩으로 든다
/// (<c>0x00474630</c> 세우기 · <c>0x00474660</c> 보기). 뒷정리가 그 비트를 도로 끈다.
/// </remarks>
public static class SeaEvents
{
    /// <summary>항해 날수가 이 값을 넘겨야 사건이 일어난다(<c>0x00474680</c> 의 <c>cmp [0x005A4D40], 9; jle</c>) — 열흘째부터다.</summary>
    public const int MinDaysAtSea = 9;

    /// <summary>사건 갈래 수(<c>rand(7)</c>).</summary>
    public const int KindCount = 7;

    /// <summary>폭풍과 눈보라의 갈래 번호. 점프표에서 둘 다 <c>0x00474BD5</c> 로 간다.</summary>
    public const int StormKind = 4, BlizzardKind = 5;

    /// <summary>반란의 갈래 번호(<c>0x00474B4A</c>).</summary>
    public const int MutinyKind = 3;

    /// <summary>쥐의 갈래 번호 둘. 점프표가 0 과 6 을 같은 자리로 보낸다.</summary>
    public const int RatsKind = 0, RatsAgainKind = 6;

    /// <summary>
    /// 부관이 쥐를 퇴치하는 판정(<c>0x0047478D</c>~<c>0x004747AB</c>).
    /// </summary>
    /// <remarks>
    /// <c>운용술 x 25 + 지력 + 1 &gt; rand(200)</c> 이면 쥐가 안 퍼진다. <b>부관이 없으면
    /// 굴리지도 않는다</b>(<c>0x0047477A</c> 가 자리 0 을 먼저 본다).
    /// </remarks>
    public const int RatsPerLevel = 25, RatsRoll = 200;

    /// <summary>괴혈병(<c>0x00474812</c>)과 전염병(<c>0x004749FA</c>)의 갈래 번호.</summary>
    public const int ScurvyKind = 1, PlagueKind = 2;

    /// <summary>
    /// 병이 터지기 전에 <b>귀띔만 하고 끝나는</b> 주사위 폭.
    /// </summary>
    /// <remarks>
    /// <code>
    ///   474812  괴혈병  if (rand(120) &lt; 신앙심 + 1)  "제독! 모두 약해져 있습니다…"  로 끝
    ///   4749fa  전염병  if (rand( 60) &lt; 신앙심 + 1)  "제독! 이상한 병이 돌고 있습니다…"
    /// </code>
    /// 폭이 좁을수록 귀띔이 잦다 — 그러니 <b>전염병 쪽이 더 자주 미리 잡힌다</b>.
    /// 걸리는 것은 기능이 아니라 <b>신앙심</b>이다(<c>0x005B60D4</c>) — 기도가 통한 셈이다.
    /// </remarks>
    public const int ScurvyNotice = 120, PlagueNotice = 60;

    /// <summary>괴혈병을 막는 기능의 자리(<c>0x0047484C</c> · <c>0x0047486A</c> 의 <c>cmp 3</c>).</summary>
    /// <remarks>
    /// 게임은 <b>과학</b>(기능 12)을 먼저 보고 그 다음 <b>의학</b>(기능 5)을 본다. 어느 쪽이든
    /// 자리가 3 이면 괴혈병이 안 터지고 <b>보리를 먹여</b> 넘긴다. 전염병 쪽에는 이 관문이 없다.
    ///
    /// 누구 것을 보는가는 <c>0x0047CCA0(기능, 0, -1, -1, -1)</c> — <b>제독과 부하 자리 0</b>
    /// 가운데 높은 쪽이다.
    /// </remarks>
    public const int MedicineNeeded = 3;

    /// <summary>그 기능들의 이름 — 보는 차례 그대로다.</summary>
    public const string ScienceSkill = "과학", MedicineSkill = "의학";

    /// <summary>반란을 그냥 보는 주기(<c>mov $0x7,%ecx ; idiv</c>).</summary>
    public const int MutinyPeriod = 7;

    /// <summary>이 피로도를 넘으면 주기와 상관없이 본다(<c>cmpl $0x50, 0x28(%esi)</c>).</summary>
    public const int MutinyFatigue = 80;

    /// <summary>안 일어나게 하는 밑값(<c>add edi, 0x1A</c>).</summary>
    /// <remarks>
    /// 게임의 안전 값은 <c>항해술 x 25 + 신앙심 + 26</c> 이다(<c>0x004746A6</c>~<c>0x004746BF</c>).
    /// 신앙심(<c>0x005B60D4</c>)이 그대로 얹히므로 <b>100 이면 자리 넷과 맞먹는다</b>.
    ///
    /// 그리고 항해술은 제독 것만 보지 않는다 — <c>0x0047CCA0(0, 1, -1, -1, -1)</c> 이
    /// <b>제독과 부하 자리 1</b> 가운데 높은 쪽을 집는다. 괴혈병 쪽은 자리 0 을 보는데
    /// 여기만 자리 1 이다 — <b>원본이 그렇다</b>.
    /// </remarks>
    public const int SafeBase = 26;

    /// <summary>항해술 한 자리가 더해 주는 안전(<c>edi * 25</c>).</summary>
    public const int SafePerLevel = 25;

    /// <summary>판정에 굴리는 주사위 폭(<c>push 0xC8</c>).</summary>
    public const int SafeRoll = 200;

    /// <summary>사건이 걸리는 항해 기술 이름.</summary>
    public const string SkillName = "항해술";

    /// <summary>폭풍이 부는 위도 띠(도). 무역풍 자리다.</summary>
    public const double StormLatMin = 10, StormLatMax = 25;

    /// <summary>눈보라가 치는 위도 띠(도).</summary>
    public const double BlizzardLatMin = 60, BlizzardLatMax = 75;

    /// <summary>
    /// 오늘 무슨 일이 있는지. 없으면 <c>null</c>.
    /// </summary>
    /// <param name="player">함대.</param>
    /// <param name="lat">지금 위도(북이 양수).</param>
    /// <param name="rng">주사위.</param>
    /// <param name="mateAt">
    /// 그 자리의 부하 신상. 게임은 기능마다 <b>제독과 어느 한 자리</b>를 견주므로(<c>0x0047CCA0</c>)
    /// 부르는 쪽이 자리 번호로 찾아 준다. 안 주면 제독 것만 본다.
    /// </param>
    public static SeaEventKind? Roll(Player player, double lat, Random rng,
                                     Func<int, Player.MateInfo?>? mateAt = null)
    {
        if (player.DaysAtSea <= MinDaysAtSea) return null;

        // 항해술은 <b>자리 1</b> 의 부하와 견준다 — 자리 0 이 아니다(0x00474692 의 인자).
        var second = mateAt?.Invoke(1);
        int sail = Math.Max(player.LevelOf(SkillName), second?.Sailing ?? 0);
        int faith = player.AbilityOf(Ability.Faith);

        int safe = sail * SafePerLevel + faith + SafeBase;
        if (safe >= rng.Next(SafeRoll)) return null;

        // 과학·의학은 <b>자리 0</b> 과 견준다(0x0047484C · 0x0047486A 의 인자).
        var aide = mateAt?.Invoke(0);
        int carved = FigureheadOf(player);

        // 굴린 갈래의 재해가 이미 서 있으면 그날은 그냥 넘긴다(0x004746DC) — 겹쳐 뜨지 않는다.
        int kind = rng.Next(KindCount);
        if (AilmentOf(kind) is { } standing && player.Has(standing)) return null;

        return kind switch
        {
            RatsKind or RatsAgainKind when
                Figureheads.Blocks(carved, Figureheads.GuardsRats, rng) => null,

            // 선수상이 안 막으면 <b>부관이 나선다</b>(0x00474770) — 자리 0 의 부하가 있어야 한다.
            RatsKind or RatsAgainKind when
                aide is { } who && who.Handling * RatsPerLevel + who.Mind + 1 > rng.Next(RatsRoll)
                => SeaEventKind.RatsKilled,

            RatsKind or RatsAgainKind => SeaEventKind.Rats,

            // 병 둘은 먼저 귀띔 주사위를 굴린다. 걸리면 그것으로 끝이다.
            ScurvyKind when rng.Next(ScurvyNotice) < faith + 1 => SeaEventKind.Weakening,
            // 과학이나 의학이 3 이면 보리를 먹여 넘긴다(0x0047484C · 0x0047486A).
            // 말은 그 기능을 제독이 가졌는지 부관이 가졌는지로 갈린다(0x0047499E).
            ScurvyKind when player.LevelOf(ScienceSkill) >= MedicineNeeded
                         || player.LevelOf(MedicineSkill) >= MedicineNeeded
                => SeaEventKind.BarleyByMe,
            ScurvyKind when aide is { } who
                         && (who.Science >= MedicineNeeded || who.Medicine >= MedicineNeeded)
                => SeaEventKind.BarleyByMate,
            ScurvyKind =>
                Figureheads.Blocks(carved, Figureheads.GuardsSickness, rng)
                    ? null : SeaEventKind.Scurvy,

            PlagueKind when rng.Next(PlagueNotice) < faith + 1 => SeaEventKind.StrangeIllness,
            PlagueKind =>
                Figureheads.Blocks(carved, Figureheads.GuardsSickness, rng)
                    ? null : SeaEventKind.Plague,

            MutinyKind when !Mutinous(player) => null,
            MutinyKind =>
                Figureheads.Blocks(carved, Figureheads.GuardsMutiny, rng)
                    ? null : SeaEventKind.Mutiny,

            StormKind or BlizzardKind when
                Figureheads.Blocks(carved, Figureheads.GuardsStorm, rng) => null,
            StormKind or BlizzardKind => BandOf(lat),

            _ => null,
        };
    }

    /// <summary>
    /// 기함에 단 선수상 번호. 안 달았으면 -1.
    /// </summary>
    /// <remarks>
    /// 게임도 <b>기함</b> 것만 본다 — 다섯 갈래가 하나같이 <c>0x00473CD0</c>(기함 번호)로
    /// 배를 집어 <c>[배+0x5C]</c> 를 읽는다. 함대에 몇 척이 있든 뱃머리는 하나다.
    /// </remarks>
    public static int FigureheadOf(Player player)
    {
        var ships = player.Ships;
        if (ships.Count == 0) return -1;
        return ships[Math.Clamp(player.Flagship, 0, ships.Count - 1)].Figurehead;
    }

    /// <summary>그 갈래가 남기는 재해. 쥐·괴혈병·전염병만 남는다.</summary>
    public static SeaAilment? AilmentOf(int kind) => kind switch
    {
        RatsKind or RatsAgainKind => SeaAilment.Rats,
        ScurvyKind => SeaAilment.Scurvy,
        PlagueKind => SeaAilment.Plague,
        _ => null,
    };

    /// <summary>터진 재해가 남기는 표시. 귀띔이나 폭풍은 안 남긴다.</summary>
    public static SeaAilment AilmentOf(SeaEventKind kind) => kind switch
    {
        SeaEventKind.Rats => SeaAilment.Rats,
        SeaEventKind.Scurvy => SeaAilment.Scurvy,
        SeaEventKind.Plague => SeaAilment.Plague,
        _ => SeaAilment.None,
    };

    /// <summary>쥐가 서 있는 동안 날마다 먹는 식량 원값 — <c>0x00474160(-10)</c> 이 열 배로 민다(10통).</summary>
    public const int RatsDailyUnits = 100;

    /// <summary>
    /// 재해가 서 있는 동안 바다에서 하루를 난다 — 쥐는 식량을 먹고 병은 선원을 죽인다.
    /// </summary>
    /// <remarks>
    /// 게임의 바다 하루 뒷정리 <c>0x00474DA0</c> 앞머리다.
    /// <code>
    ///   474db0  쥐    → 식량 원값 -100
    ///   474dd2  괴혈병 → 죽는 수  = rand(2) + 3 - 부하 의학
    ///   474e0c  전염병 → 죽는 수 += 6 - 부하 의학
    ///   474e3c  죽는 수만큼 선원 비율이 높은 배에서 한 명씩
    /// </code>
    /// 의학은 제독과 <b>부하 자리 0</b> 가운데 높은 쪽이다(<c>0x00474DEA</c> · <c>0x00474E24</c> 의
    /// <c>0x0047CCA0(5, 0, …)</c>). 우리 선원은 함대가 통째로 태우므로 머릿수만 던다.
    /// </remarks>
    /// <returns>오늘 죽은 선원 수.</returns>
    public static int Ail(Player player, Random rng, Func<int, Player.MateInfo?>? mateAt = null)
    {
        if (player.Has(SeaAilment.Rats))
            player.AddSupplyUnits(SupplyKind.Food, -RatsDailyUnits);

        int medicine = Math.Max(player.LevelOf(MedicineSkill), mateAt?.Invoke(0)?.Medicine ?? 0);
        int dead = 0;
        if (player.Has(SeaAilment.Scurvy)) dead = rng.Next(2) + 3 - medicine;
        if (player.Has(SeaAilment.Plague)) dead += 6 - medicine;

        dead = Math.Clamp(dead, 0, player.Crew);
        if (dead > 0) player.SetCrew(player.Crew - dead);
        return dead;
    }

    /// <summary>
    /// 항해가 끝나 재해가 풀릴 때 부관이 하는 말(<c>0x0048E5E0</c>) — 서 있던 것마다 한 줄씩이다.
    /// </summary>
    public static IEnumerable<string> CureWords(SeaAilment was)
    {
        if ((was & SeaAilment.Rats) != 0) yield return "쥐를 퇴치했습니다. 이제 괜찮습니다.";
        if ((was & SeaAilment.Scurvy) != 0) yield return "환자가 회복되었습니다. 어떻게 되는 줄 알았습니다.";
        if ((was & SeaAilment.Plague) != 0) yield return "병이 가라앉은 것 같습니다. 정말 위험할 뻔했습니다.";
    }

    /// <summary>
    /// 반란이 일 만한지 — <b>이레마다</b>, 또는 피로도가 80 을 넘었으면 언제든.
    /// </summary>
    /// <remarks>
    /// <code>
    /// 474b4a  ecx = 7 ; eax = [0x5A4D40] ; idiv ecx
    /// 474b57  if (나머지 == 0) 그냥 본다
    /// 474b5b  cmpl $0x50, 0x28(%esi) ; jle 끝     ; 아니면 피로도 &gt; 80 이라야
    /// </code>
    /// 피로도가 여기서만 쓰인다 — 폭풍이 왜 피로도를 올리는지가 이 줄에 있다.
    /// </remarks>
    public static bool Mutinous(Player player) =>
        player.DaysAtSea % MutinyPeriod == 0 || player.Fatigue > MutinyFatigue;

    /// <summary>
    /// 그 위도에서 부는 것. 띠 밖이면 <c>null</c>.
    /// </summary>
    /// <remarks>
    /// 게임은 위도를 <c>0x005B63B4</c> 에 0~20000 으로 들고(10000 이 적도) 띠를 이렇게 나눈다.
    /// <code>
    ///   폭풍     0x1C37~0x22B9 · 0x2B67~0x31E9   = 적도에서 10~25도
    ///   눈보라   0x0683~0x0D06 · 0x411A~0x479D   = 적도에서 60~75도
    /// </code>
    /// 여덟 값이 10000 을 가운데 두고 짝을 이룬다 — 남북이 같다.
    /// </remarks>
    public static SeaEventKind? BandOf(double lat)
    {
        double a = Math.Abs(lat);
        if (a is >= StormLatMin and <= StormLatMax) return SeaEventKind.Storm;
        if (a is >= BlizzardLatMin and <= BlizzardLatMax) return SeaEventKind.Blizzard;
        return null;
    }

    /// <summary>
    /// 손상을 재는 기준 내구(<c>0x00474EF9</c> 의 <c>mov $0x64,%esi</c>).
    /// </summary>
    public const int HurtBase = 100;

    /// <summary>폭풍이 올리는 피로도(<c>0x00474D18</c> 의 <c>rand(11) + 0x14</c>).</summary>
    public static int TireOf(Random rng) => rng.Next(11) + 20;

    /// <summary>폭풍이 깎는 사기(<c>0x00474D2D</c> 의 <c>rand(11) + 0x0A</c>, 부호 뒤집음).</summary>
    public static int Dishearten(Random rng) => rng.Next(11) + 10;

    /// <summary>바다에서 하루를 난 끝.</summary>
    /// <param name="WaterLow">오늘 물이 사흘치 밑으로 떨어졌는지.</param>
    /// <param name="FoodLow">오늘 식량이 사흘치 밑으로 떨어졌는지.</param>
    /// <param name="WaterOut">오늘 물이 바닥났는지.</param>
    /// <param name="FoodOut">오늘 식량이 바닥났는지.</param>
    /// <param name="Tired">오늘 오른 피로도.</param>
    /// <param name="Cold">추위 값(0~3).</param>
    /// <param name="Weary">넘어선 피로 문턱(50·70·90). 안 넘었으면 0.</param>
    /// <param name="Dead">지쳐 죽은 선원 수.</param>
    /// <param name="Short">그 바람에 승원이 모자라졌으면 부관이 할 말. 아니면 빈 글.</param>
    public sealed record Day(bool WaterLow, bool FoodLow, bool WaterOut, bool FoodOut,
                             int Tired, int Cold, int Weary, int Dead = 0, string Short = "");

    /// <summary>추위가 한 단씩 오르는 위도(도). 게임 값 <c>0x1C36·0x1E61·0x208D</c> 다.</summary>
    public static readonly double[] ColdLats = [65, 70, 75];

    // ── 극지방 ───────────────────────────────────────────────────────────────

    /// <summary>
    /// 너무 깊이 들어가면 얼어 죽는다(<c>0x0048D690</c>).
    /// </summary>
    /// <remarks>
    /// 적도에서 떨어진 만큼(<c>|10000 − 위도|</c>)을 문턱과 견주고, <b>바로 앞서 잰 값</b>
    /// (<c>[함대+0x128]</c>)보다 커졌을 때만 — 곧 <b>넘어서는 그 걸음에만</b> 한 번 낸다.
    /// <code>
    ///   48d6d6  0x1C36 = 7222 = 65도   부관 「제독, 너무 춥습니다! …」
    ///   48d72e  0x208D = 8333 = 75도   부관 「제독, 추위의 한계입니다! …」
    ///   48d77d  남 0x22B8 = 8888 = 80도 · 북 0x216B = 8555 = 77도   전멸
    /// </code>
    /// <b>남쪽이 3도 더 깊이 갈 수 있다.</b> 셋은 차례로 보아 <b>하나만</b> 난다.
    /// 우리는 하루가 갈 때마다 재므로 걸음마다 재는 원본보다 성기다.
    /// </remarks>
    public const double PolarWarnLat = 65, PolarAlarmLat = 75;

    /// <summary>얼어 죽는 위도 — 북이 77도, 남이 80도다.</summary>
    public const double PolarDoomNorth = 77, PolarDoomSouth = 80;

    /// <summary>극지방에서 이번 걸음에 난 일.</summary>
    public enum PolarStep
    {
        /// <summary>아무 일도 없다.</summary>
        None,

        /// <summary>65도를 넘어섰다.</summary>
        Warn,

        /// <summary>75도를 넘어섰다.</summary>
        Alarm,

        /// <summary>죽는 위도를 넘어섰다.</summary>
        Doom,
    }

    /// <summary>지금 위도와 <paramref name="was"/>(바로 앞서 잰 |위도|)로 가른다.</summary>
    public static PolarStep PolarAt(double lat, double was)
    {
        double now = Math.Abs(lat);
        double doom = lat < 0 ? PolarDoomSouth : PolarDoomNorth;

        if (now >= PolarWarnLat && now > was && was < PolarWarnLat) return PolarStep.Warn;
        if (now >= PolarAlarmLat && now > was && was < PolarAlarmLat) return PolarStep.Alarm;
        if (now >= doom && now > was && doom > was) return PolarStep.Doom;
        return PolarStep.None;
    }

    /// <summary>「북」이냐 「남」이냐(<c>0x00570598</c> · <c>0x0057059C</c>).</summary>
    public static string PolarWay(double lat) => lat < 0 ? "남" : "북";

    /// <summary>75도 알림(<c>0x00570620</c>) — 바다면 「유빙에 갇혀서」, 뭍이면 「추위 때문에」다.</summary>
    public static string PolarAlarmWord(bool onLand) =>
        $"제독, 추위의 한계입니다! 이러다가는 {(onLand ? "추위 때문에" : "유빙에 갇혀서")} 전멸합니다!";

    /// <summary>얼어 죽는 말 — 바다 셋·뭍 셋 가운데 하나다(<c>0x0048D78F</c> 벌).</summary>
    public static string PolarDoomWord(bool onLand, Random rng) =>
        (onLand ? PolarDoomOnLand : PolarDoomAtSea)[rng.Next(3)];

    private static readonly string[] PolarDoomAtSea =
    [
        "우악! 눈앞에 유빙이! 피할 수 없습니다! 우아아아아···",
        "제독, 추위로 돛이 얼어 버렸습니다! 끝장입니다. 움직일 수가 없습니다.",
        "제독! 유빙으로 둘러싸였습니다! 전혀 움직일 수가 없습니다. 이대로 전멸입니다···",
    ];

    private static readonly string[] PolarDoomOnLand =
    [
        "우악! 눈사태다! ! 우아아아···",
        "제독, 추위 때문에···아무도 걸을 수 없습니다. 이제 끝장입니다···",
        "제독, 눈보라 때문에 아무것도 보이지 않습니다! 움직일 수도 없습니다. 이대로 전멸입니다.",
    ];

    /// <summary>피로 알림이 뜨는 문턱(<c>0x004757C5</c> 벌).</summary>
    public static readonly int[] WearySteps = [50, 70, 90];

    /// <summary>이 피로도부터는 <b>날마다 사람이 죽는다</b>(<c>0x004758DD</c> 의 <c>cmp 0x5A</c>).</summary>
    /// <remarks>
    /// 문턱 알림의 마지막 줄이 이르는 그대로다 — 「선원들의 피로가 한계에 달하고 있습니다.
    /// 이대로라면 죽는 사람이 나오고 맙니다!」 가 90 에서 뜨고, 그 뒤로는 <b>정말 죽는다</b>.
    /// </remarks>
    public const int DeathFatigue = 90;

    /// <summary>
    /// 오늘 지쳐 죽는 선원 수. 피로도가 <see cref="DeathFatigue"/> 아래면 0 이다.
    /// </summary>
    /// <remarks>
    /// <code>
    /// 4758dd  cmp 피로도(+0x28), 0x5A ; jl  건너뛴다
    /// 4758e7  죽을수 = 0x004745F0(함대 총선원) / 10 + 1
    /// 4758fd  척수   = 0x00473E00(배 수) ; 0 이면 1
    /// 475916  배 여덟 칸마다   n = max(1, 죽을수 / 척수)
    ///                          0x0044C800(배, n)          ; 배+0x34 선원 -= n
    ///                          모자람 |= 필요승원(배+0x30 + 10) > 선원(배+0x34)
    /// 475968  모자라면 부관(아니면 뱃사람)이 한 줄 한다
    /// </code>
    /// <b>배마다 적어도 한 사람</b>이라, 배가 많으면 그만큼 더 죽는다 — 선원 스물에 배 넉 척이면
    /// 죽을수가 3 이라 척당 0 으로 떨어지지만 바닥이 1 이라 <b>넷</b>이 죽는다.
    /// </remarks>
    public static int WearyDeaths(Player player)
    {
        if (player.Fatigue < DeathFatigue || player.Crew <= 0) return 0;

        int ships = Math.Max(1, player.Ships.Count);
        int each = Math.Max(1, (player.Crew / 10 + 1) / ships);
        return Math.Min(player.Crew, each * ships);
    }

    /// <summary>
    /// 승원이 모자라졌을 때의 말. 넉넉하면 빈 글이다.
    /// </summary>
    /// <remarks>
    /// 게임은 배마다 견주지만 우리는 함대가 통째로 태우므로 <b>합</b>으로 본다. 부관이 있으면
    /// 부관이(<c>0x005357D8</c> · <c>0x00535820</c>), 없으면 알림 상자로(<c>0x00535860</c> · <c>0x005358A0</c>)
    /// 이른다 — <c>0x0047CC50(0) == −1</c> 이 가른다(<c>0x0047598C</c>). 배가 두 척 이상이면 앞의 말이다.
    /// </remarks>
    public static string ShortCrewWord(Player player)
    {
        if (player.Crew >= player.MinCrew) return "";
        bool many = player.Ships.Count > 1;

        if (player.MateAt(0).Length == 0)
            return many
                ? "선원이 부족한 선박이 존재합니다. 선원수를 조정해 주십시오."
                : "선원이 부족합니다! 마을에서 선원을 고용해 주십시오";
        return many
            ? "제독, 선원이 부족한 배는 따라 올 수 없습니다. 선원수를 조정해 주십시오."
            : "제독, 선원수가 모자랍니다! 아무 항구에서든 선원을 고용합시다.";
    }

    /// <summary>
    /// 그 위도의 추위 — 65도에서 한 단, 70도에서 두 단, 75도를 넘으면 세 단이다.
    /// </summary>
    /// <remarks>
    /// <code>
    /// 475587  eax = |10000 - 0x5B63B4|            ; 적도에서 떨어진 만큼
    /// 47559e  edx  = (eax &gt;= 0x1C36) ? 1 : 0     ; 7222 = 65도
    /// 4755ac  edx += (eax &gt;= 0x1E61) ? 1 : 0     ; 7777 = 70도
    /// 4755bc  edx += (eax &gt;= 0x208D) ? 1 : 0     ; 8333 = 75도
    /// </code>
    /// 이 값은 그날 오르는 피로도에 그대로 더해진다 — <b>추운 데를 지나면 더 지친다</b>.
    /// </remarks>
    public static int ColdAt(double lat)
    {
        double a = Math.Abs(lat);
        int cold = 0;
        foreach (double step in ColdLats) if (a >= step) cold++;
        return cold;
    }

    /// <summary>
    /// 바다에서 하루를 난다 — 식량과 물을 축내고, 그만큼 지친다.
    /// </summary>
    /// <remarks>
    /// 게임의 <c>0x00475470</c>(항해 하루치)이다. 부르는 곳은 <c>0x0044B1A2</c> 하나다.
    /// <code>
    /// 4755e6  소모 = max(1, 선원수 * 5 / 6)      ; 식량·물 각각
    /// 475624  사흘치 밑으로 <b>떨어진 그 날</b> "얼마 남지 않았습니다!"
    /// 4756b4  0 이 <b>된 그 날</b>              "바닥을 드러내고 있습니다"
    /// 47578e  피로 += rand(2) + 4 - 항해사등급 + 추위      ; 둘 다 있을 때
    /// 4757a4  피로 += rand(3) + 6 - …                     ; 한쪽이 바닥
    /// 47576e  피로 += rand(3) + 8 - …                     ; 둘 다 바닥
    /// </code>
    /// <b>바다에서는 날마다 지친다</b> — 폭풍이 없어도 오래 나가 있으면 반란이 온다.
    /// 항해사 등급은 우리에게 없어 그 자리에 <b>항해술 자리</b>를 넣는다.
    /// </remarks>
    /// <param name="sailing">
    /// 항해술 — 제독과 부하 자리 1 가운데 높은 쪽(<c>0x0047574A</c> 의 <c>0x0047CCA0(0, 1, …)</c>). 안 주면 제독 것이다.
    /// </param>
    public static Day PassDay(Player player, double lat, Random rng, int? sailing = null)
    {
        int use = Supply.DailyUse(player.Crew);
        int warn = use * Supply.LowDays;
        var (water0, food0) = player.UseDailySupply();
        int water = player.SupplyUnitsOf(SupplyKind.Water);
        int food = player.SupplyUnitsOf(SupplyKind.Food);

        static bool Crossed(int before, int after, int mark) =>
            before >= mark && after < mark && after > 0;

        int cold = ColdAt(lat);
        int tired = water == 0 && food == 0 ? rng.Next(3) + 8
                  : water == 0 || food == 0 ? rng.Next(3) + 6
                  : rng.Next(2) + 4;
        tired = Math.Max(0, tired - (sailing ?? player.LevelOf(SkillName)) + cold);

        int was = player.Fatigue;
        player.Tire(tired);

        int weary = 0;
        foreach (int mark in WearySteps)
            if (was < mark && player.Fatigue >= mark) weary = mark;

        // 피로가 한계를 넘으면 그날부터 사람이 죽는다(0x004758DD) — 지친 뒤에 센다.
        int dead = WearyDeaths(player);
        if (dead > 0) player.SetCrew(player.Crew - dead);

        return new Day(
            Crossed(water0, water, warn), Crossed(food0, food, warn),
            water0 > 0 && water == 0, food0 > 0 && food == 0,
            tired, cold, weary, dead, dead > 0 ? ShortCrewWord(player) : "");
    }

    /// <summary>반란을 눌러 앉히면 오르는 사기(<c>0x004753EA</c> 의 <c>push 0x1E</c>).</summary>
    public const int MutinyCheer = 30;

    /// <summary>
    /// 폭풍을 맞는다. 배마다 내구를 깎고, 0 이 된 배는 놓친다.
    /// </summary>
    /// <remarks>
    /// <code>
    /// ; 뒷정리  0x00474DA0 — 폭풍/눈보라 자리(0x00474EB1~)
    /// 474ef9  esi = 100 - 지금내구(0x44C860)
    /// 474f0c  esi = esi / 10 + rand(3)                     ; 손상
    /// 474f28  추진력 = clamp(추진력 - 손상, 0, 150)          ; 0x44C810 세터
    /// 474f5a  내구   = clamp(내구   - 손상, 기함?1:0, 250)   ; 0x44C850 세터
    /// 474f74  if (내구 == 0) 그 배를 함대에서 뺀다(0x473E60) — "눈에 띄지 않습니다"
    /// </code>
    /// <b>손상은 내구가 높을수록 작다.</b> 100 이 기준이라 갤리온(70)은 한 번에 3~5,
    /// 카라벨(20)은 8~10 씩 깎인다 — <b>작은 배가 훨씬 아프다</b>. 그리고 한 번 상하기
    /// 시작하면 다음 폭풍이 더 아프다.
    ///
    /// 우리 선체 값이 게임 화면에서 옮긴 그 값이라 <b>기준 100 을 그대로 쓴다</b>.
    /// 추진력도 같은 만큼 깎는다 — 수리로만 되돌아온다(<see cref="Support.Local.Models.Ship.SlowDown"/>).
    ///
    /// 기함은 안 잃는다. 게임도 기함 자리의 내구를 1 밑으로 안 내린다(<c>ebp</c>).
    /// </remarks>
    public static SeaEventResult Resolve(Player player, SeaEventKind kind, Random rng)
    {
        player.Tire(TireOf(rng));
        player.Cheer(-Dishearten(rng));

        var hurt = new int[player.Ships.Count];
        for (int i = 0; i < hurt.Length; i++)
            // 바닥이 없다 — 내구가 100 을 넘으면 셈이 <b>음수</b>가 되어 오히려 낫는다
            // (0x00474EF9: esi = (100 − 내구)/10 + rand(3) 을 그대로 neg 해서 더한다).
            hurt[i] = (HurtBase - player.Ships[i].Hp) / 10 + rng.Next(3);

        // 뒤에서부터 깎아야 배를 잃어도 앞 칸의 짝이 안 어긋난다.
        var lost = new List<string>();
        for (int i = hurt.Length - 1; i >= 0; i--)
        {
            var ship = player.Ships[i];
            bool flag = i == player.Flagship;
            // 폭풍은 <b>추진력과 내구를 같은 만큼</b> 깎는다(0x00474F28 · 0x00474F5A).
            ship.Drive(-hurt[i]);
            ship.Batter(-hurt[i], floor: flag ? 1 : 0);
            if (ship.Hp == 0 && !flag && player.Ships.Count > 1)
            {
                lost.Add(ship.Name);
                player.LoseShip(i);
            }
        }
        lost.Reverse();

        return new SeaEventResult(kind, hurt, lost);
    }
    // ── 하루가 갈 때 도는 바다 사건들(0x00426E80 의 바다 갈래) ────────────────
    //
    // 세이렌 → 크리스마스 → 생일 → 빙산 차례로 굴려 <b>하루에 하나만</b> 터진다.
    // 폭풍·역병과는 다른 갈래다(그쪽은 0x00474680).

    /// <summary>
    /// 인어의 노래(<c>0x00426ECA</c>) — 이백에 하나, <b>북위 30도대 한 줄</b>에서만 난다.
    /// </summary>
    public static bool Siren(Random rng, double lat) =>
        lat >= 30 && lat < 31 && rng.Next(200) == 0;

    /// <summary>노래에 홀려 흘려보내는 날수 — <c>rand(5)+3</c>(<c>0x00426F5B</c>).</summary>
    public static int SirenDays(Random rng) => rng.Next(5) + 3;

    /// <summary>깨고 나서 깎이는 규율 — <c>rand(5)+1</c>. 0 이 되면 <b>1 로 되돌린다</b>.</summary>
    public static int SirenMoraleDrop(Random rng) => rng.Next(5) + 1;

    /// <summary>오르는 피로도 — <c>rand(5)+5</c>. 100 이 되면 <b>99 로 되돌린다</b>.</summary>
    public static int SirenFatigue(Random rng) => rng.Next(5) + 5;

    /// <summary>크리스마스(<c>0x0042709D</c>) — 12월 24일이면 굴림 없이 난다.</summary>
    public static bool Christmas(DateTime date) => date.Month == 12 && date.Day == 24;

    /// <summary>제독 생일(<c>0x00427115</c>).</summary>
    public static bool Birthday(Player player) =>
        player.Date.Month == player.BirthMonth && player.Date.Day == player.BirthDay;

    /// <summary>잔치가 풀어 주는 피로도와 올려 주는 규율(<c>0x004270DD</c>).</summary>
    public const int FeastRest = 10, FeastMorale = 20;

    /// <summary>
    /// 생일 선물을 받는지(<c>0x004271C9</c>) — <c>매력 + 규율 − 피로도 &gt; 99</c> 라야 한다.
    /// </summary>
    /// <remarks>잔치로 피로가 풀리고 규율이 오른 <b>뒤</b>의 값으로 잰다.</remarks>
    public static bool BirthdayGift(int charm, int morale, int fatigue) =>
        charm + morale - fatigue > 99;

    /// <summary>받는 물건 — 각각 넷에 하나다(<c>0x004271FE</c>).</summary>
    public static int BirthdayItem(Random rng) =>
        rng.Next(4) == 0 ? 0x34
      : rng.Next(3) == 0 ? 0x25
      : rng.Next(2) == 0 ? 0x26 : 0x42;

    /// <summary>
    /// 빙산(<c>0x0042727F</c>) — 스물에 하나, <b>1~6월</b>에 <b>북위 70도 위</b>에서만 흘러온다.
    /// </summary>
    /// <remarks>그림만 돌고 <b>잃는 것이 없다</b> — 배도 사람도 안 다친다.</remarks>
    public static bool Iceberg(Random rng, DateTime date, double lat) =>
        date.Month >= 1 && date.Month <= 6 && lat >= 70 && rng.Next(20) == 0;
}
