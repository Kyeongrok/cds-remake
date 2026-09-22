using CdsHelper.Support.Local.Models;

namespace CdsHelper.Game.Engine.Sea;

/// <summary>붙는 무리의 갈래. 게임의 <c>0x004555B0(kind)</c> 인자 그대로다.</summary>
public enum EnemyKind
{
    /// <summary>갑자기 쳐들어온 무리 — 창 제목이 "적의 공격" 이다.</summary>
    Raider = 0,

    /// <summary>추격대·토벌대. <b>교섭이 통하지 않는다.</b></summary>
    Chaser = 1,

    /// <summary>해적.</summary>
    Pirate = 2,

    /// <summary>이슬람 함대.</summary>
    Islam = 3,
}

/// <summary>
/// 적장 한 사람 — 해전 들머리(<c>0x00440D90</c>)가 적 능력 벌과 적 배를 이 값으로 짓는다.
/// </summary>
/// <remarks>
/// 능력은 모두 <b>세이브 날값(게임 값 + 1)</b>이다 — 게임도 <c>[인물+0x28]+1</c> 로 옮겨 적
/// 벌 <c>+0x924</c>~ 에 넣는다(볼트 <c>92.분석-적 함대 배 짜기</c> 3절). 기능은 그대로다.
/// </remarks>
/// <param name="Id">인물 번호(262~270 조우 인물).</param>
/// <param name="Nation">나라(인물 밑표 <c>+0x14</c>).</param>
/// <param name="Job">직업(밑표 <c>+0x20</c> — 0 탐험가 3 정복자 4 해적 5 전도사 6 상인 7 군인).</param>
/// <param name="Might">무력 <c>+0x924</c>.</param>
/// <param name="Mind">지력 <c>+0x928</c>.</param>
/// <param name="Charm">매력 <c>+0x92C</c>.</param>
/// <param name="Luck">운 <c>+0x930</c>.</param>
/// <param name="Faith">신앙심(전도사·상인 승원 셈에만 쓴다).</param>
/// <param name="Gunnery">포술 <c>+0x934</c>.</param>
/// <param name="Sword">검술 <c>+0x938</c>.</param>
/// <param name="Shooting">사격술 <c>+0x93C</c>.</param>
/// <param name="Fortune">
/// 운세 여덟 칸(<c>vtbl+0x24</c> = <c>0x00477FE0</c>). 칸[0] 이 해전 <c>+0x940</c>, 칸[3] 이 일기토 걸기(<c>0x0043A347</c>)
/// 에 든다. 없으면 얼굴·혈액형 0 으로 나라만 넣어 셈한다.
/// </param>
public readonly record struct Captain(
    int Id, int Nation, int Job, int Might, int Mind, int Charm, int Luck, int Faith,
    int Gunnery, int Sword, int Shooting, int[]? Fortune = null, int Theology = 0)
{
    /// <summary>운세 칸 하나(0~2).</summary>
    public int FortuneAt(int slot) =>
        (Fortune ?? FleetRaid.FortuneOf(0, 0, Nation)).ElementAtOrDefault(slot);
}

/// <summary>붙은 무리 하나.</summary>
/// <param name="Ships">
/// 교섭 창의 척수(<c>0x004435B0</c> — <c>무력/14</c> + 해적 2 · 군인·정복자 1). 요구액과 도망 셈이
/// 여기 걸린다. <b>해전에 실제로 나오는 척수와 다르다</b>(<see cref="EnemyFleet.CountOf"/>).
/// </param>
/// <param name="Sum">적장 능력 넷의 합에 1 을 더한 값(<c>0x00455A36</c>).</param>
/// <param name="Leader">적장. 없으면(옛 호출) 해전이 조우 인물 262 로 갈음한다.</param>
public readonly record struct Enemy(EnemyKind Kind, string Name, int Ships, int Sum, Captain? Leader = null);

/// <summary>
/// 바다에서 남의 함대를 만났을 때의 셈과 말 — <b>교섭 · 도망 · 응전</b>.
/// </summary>
/// <remarks>
/// 게임의 <c>0x004555B0(kind)</c> 이다. 돌려주는 값이 1 이면 도망 성공(부른 쪽이 그냥
/// 넘어간다), 5 면 교섭 성공, 0 이면 해전이다.
/// <code>
///   0x004878A0  고르기 — 교섭한다(0) · 도망간다(1) · 응전한다(2)
///   0x00455859  교섭   0x00455B5F  도망   0x00455C38  응전
/// </code>
///
/// <b>적 함대는 우리가 지어낸다.</b> 게임은 지도 위를 돌아다니는 함대 객체를 들고 있다가
/// 두 칸 안으로 붙으면 그것을 넘기는데([[59.분석-해적 조우]]), 우리 쪽에는 그 객체가
/// 없다. 그래서 척수와 적장 능력을 여기서 굴린다 — 셈식만은 게임 것 그대로다.
/// </remarks>
public static class Encounter
{
    // ── 말 ──────────────────────────────────────────────────────────────────

    /// <summary>
    /// 갈래마다 다섯 줄씩. 게임도 <c>0x004B7C0F(5)</c> 로 하나를 뽑는다.
    /// </summary>
    private static readonly string[][] Greetings =
    [
        // 0 갑자기 쳐들어온 무리 (0x0055F5E0 벌) — 이쪽도 다섯 줄이다(0x00455600).
        // 「%s%s」는 이름과 조사 이/가 다(0x0045565B 의 0x004281B0(이름, 0)) — {1} 자리에 끼운다.
        [
            "제독, {0}입니다!",
            "{0}{1} 이쪽으로 오고 있습니다. 제독, 어떻게 할까요?",
            "제독, {0}입니다. 어떻게 할까요?",
            "{0}{1} 왔습니다. 살기 등등합니다. 어떻게 할까요?",
            "{0}{1} 쳐들어 왔습니다!",
        ],

        // 1 추격대·토벌대 (0x0055F6F0 벌)
        [
            "제독, 추격대가 왔습니다!",
            "추격대인 것 같습니다! 어떻게 할까요?",
            "큰일이군! 토벌대입니다!",
            "제독, 끈질긴 추격대의 함대입니다. 어떻게 할까요?",
            "귀찮은 녀석들이군요. 추격대의 함대입니다.",
        ],

        // 2 해적 (0x0055F7C0 벌)
        [
            "제독, 해적입니다. 어떻게 할까요?",
            "이 주변을 흐리고 있는 해적들인 것 같습니다. 어떻게 할까요?",
            "해적이다! 제독, 녀석들이 이쪽으로 오고 있습니다.",
            "해적입니다. 성가신 녀석들이 왔군요.",
            "제독, 해적입니다! 어떻게 처리할까요?",
        ],

        // 3 이슬람 함대 (0x0055F8B8 벌)
        [
            "이교도의 녀석들입니다. 제독, 지시를!",
            "이슬람 함대가 이쪽으로 오고 있습니다. 어떻게 할까요?",
            "제독, 저쪽에서 이슬람 녀석들이 오고 있습니다. 어떻게 할까요?",
            "이슬람교의 녀석들이 오고 있습니다. 우리들을 공격할 것 같습니다.",
            "큰일입니다. 이슬람 함대가 돌격해 오고 있습니다.",
        ],
    ];

    /// <summary>말이 안 통해 교섭이 엎어질 때(<c>0x0055FA00</c> 벌).</summary>
    private static readonly string[] NoWords =
    [
        "제독, 말이 통하지 않습니다! 교섭은 실패입니다.",
        "말이 통하지 않아서 교섭은 불가능합니다. 싸웁시다!",
        "녀석들, 습격해 왔습니다! 말이 통하지 않아서 무리였던 것 같습니다.",
        "말이 통하지 않아서 교섭할 수 없었습니다. 제독, 응전합시다!",
        "안되겠습니다. 대화를 할 수 없습니다. 제독, 응전합시다!",
    ];

    /// <summary>돈을 요구하는 말(<c>0x0055FB30</c> 벌). <c>{0}</c> 이 요구액이다.</summary>
    private static readonly string[] Demands =
    [
        "제독, 적은 금화 {0}닢을 요구하고 있습니다. 어떻게 할까요?",
        "녀석들, 금화 {0}닢을 요구하고 있습니다. 제독. 어떻게 할까요?",
        "금화 {0}닢을 달라고 하고 있습니다. 어떻게 대답할까요?",
        "{0}닢의 금화를 요구하고 있습니다만, 어떻게 할까요?",
        "{0}닢의 금화를 주면 봐 주겠다고 하고 있습니다만, 어떻게 할까요?",
    ];

    /// <summary>돈이 모자랄 때(<c>0x0055FCA0</c> 벌).</summary>
    private static readonly string[] TooPoor =
    [
        "제독, 그런 돈은 없습니다!",
        "제독, 금화가 모자랍니다!",
        "그렇게까지 지불할 수 없습니다.",
        "제독, 그렇게까지 돈이 없을 텐데요?",
        "제독, 그렇게 금화를 가지고 있지 않습니다!",
    ];

    /// <summary>돈을 내고 물러갈 때(<c>0x0055FD60</c> 벌).</summary>
    private static readonly string[] Paid =
    [
        "적은 만족해 하며 사라졌습니다.",
        "적이 납득한 것 같습니다. 전투는 피한 것 같습니다.",
        "적은 사라졌습니다. 이것으로 싸우지 않고 끝난 것이라면, 괜찮군요.",
        "적은 허락한 것 같습니다.",
        "후우, 싸움은 간신히 피한 것 같군요.",
        "잘 되었습니다. 쓸데없는 싸움은 피하는 것이 좋습니다.",
    ];

    /// <summary>교섭이 깨질 때(<c>0x0055FE88</c> 벌).</summary>
    private static readonly string[] TalkFailed =
    [
        "제독, 응해주지 않는군요. 싸웁시다!",
        "실패입니다. 적이 공격해 왔습니다!",
        "안되겠습니다. 제독, 싸움 준비를!",
        "제독, 안되겠습니다. 녀석들 화가 나 있습니다. 싸움은 피할 수 없습니다!",
        "교섭은 실패입니다. 제독, 싸울 수 밖에 없군요.",
        "제독, 최악입니다. 교섭에 실패했습니다.",
    ];

    /// <summary>도망에 성공할 때(<c>0x0055FFA8</c> 벌).</summary>
    private static readonly string[] Fled =
    [
        "간신히 추격을 따돌린 것 같습니다.",
        "잘 되었습니다. 아둔한 녀석들이어서 살았습니다.",
        "따라오지 않는군요. 습격할 생각이 없었나···",
        "도망쳐 나왔습니다. 추격하지 않는 듯하군요.",
        "잘 도망쳐 나온 것 같습니다. 도망치는 것도 전법의 하나이군요.",
    ];

    /// <summary>도망에 실패할 때(<c>0x005600B0</c> 벌).</summary>
    private static readonly string[] Caught =
    [
        "큰일이다! 둘러 싸였다.",
        "안됩니다! 도망칠 수 없습니다!",
        "제독, 실패입니다. 응전합시다.",
        "추격을 따돌릴 수 없었습니다. 제독, 싸웁시다.",
        "안되겠다. 제독, 포기하고 싸웁시다.",
    ];

    /// <summary>응전을 고를 때(<c>0x00560170</c> 벌).</summary>
    private static readonly string[] FightOn =
    [
        "맞받아 공격할 것이죠. 역시 제독은 이래야지.",
        "제독, 저희들의 실력을 보여 줍시다.",
        "때마침 몸이 근질거리던 차입니다. 해치워 버립시다.",
        "저런 녀석들, 적도 아닙니다. 해치워 버립시다.",
        "모두들! 준비되었나!",
    ];

    /// <summary>고르는 세 줄(<c>0x0055F9D0</c>·<c>0x0055F9E0</c>·<c>0x0055F9F0</c>).</summary>
    public static readonly string[] Choices = ["교섭한다", "도망간다", "응전한다"];

    /// <summary>창 제목 — 이름 있는 적만 "적의 공격" 이고 나머지는 "해전" 이다.</summary>
    public static string TitleOf(EnemyKind kind) =>
        kind == EnemyKind.Raider ? "적의 공격" : "해전";

    private static string One(string[] lines, Random rng) => lines[rng.Next(lines.Length)];

    /// <summary>
    /// 추격대장이 이름을 대며 거는 말은 <c>0x0055F6A8</c> 이고 ShipMapWindow 가 짓는다.
    /// </summary>
    /// <remarks>
    /// <b>추격대(갈래 1)에만</b> 있다 — <c>0x004556E7</c> 이 그 사람을 찾아(<c>0x0044FD60</c>)
    /// 얼굴을 걸고 말하게 한 뒤에 다섯 줄 가운데 하나를 잇는다. 예전에는 이 줄을 갈래 0 의
    /// 하나뿐인 말로 잘못 두어 갈래 0 의 다섯 줄이 통째로 빠졌었다.
    /// </remarks>
    /// <summary>들어설 때 건네는 말 — 갈래마다 다섯 줄 가운데 하나다.</summary>
    public static string GreetOf(in Enemy foe, Random rng) =>
        string.Format(One(Greetings[(int)foe.Kind], rng), foe.Name, Local.Helpers.NameToken.Of(foe.Name, 0));

    public static string NoWordsWord(Random rng) => One(NoWords, rng);
    public static string DemandWord(int gold, Random rng) => string.Format(One(Demands, rng), gold);

    /// <summary>
    /// 요구액을 알린 <b>다음에</b> 따로 뜨는 예·아니오 물음(<c>0x0055FC70</c>).
    /// </summary>
    /// <remarks>
    /// 게임은 창을 둘 낸다 — 부관이 <see cref="DemandWord"/> 로 액수를 이르고
    /// (<c>0x00455A7B</c>), 그다음 <b>얼굴 없는 상자</b>가 이 말로 묻는다
    /// (<c>0x00455A8F</c> 의 <c>0x0049E3E0(2, …)</c>).
    /// </remarks>
    public const string PayDemandAsk = "계속해 오는 그들의 요구액을 지불하겠습니까?";
    public static string TooPoorWord(Random rng) => One(TooPoor, rng);
    public static string PaidWord(Random rng) => One(Paid, rng);
    public static string TalkFailedWord(Random rng) => One(TalkFailed, rng);
    public static string FledWord(Random rng) => One(Fled, rng);
    public static string CaughtWord(Random rng) => One(Caught, rng);
    public static string FightOnWord(Random rng) => One(FightOn, rng);

    // ── 셈 ──────────────────────────────────────────────────────────────────

    /// <summary>
    /// 적이 부르는 돈(<c>0x00455A36</c>).
    /// </summary>
    /// <remarks>
    /// <code>
    ///   둘째 인자 = 적 함대수 x 30                                   ; 0x0044364F
    ///   요구액    = ((검술 + 포술 + 사격술 + 신학 + 1) x 그것 x 20 / 100) x 100
    /// </code>
    /// 곧 <b>기능 넷의 합 x 척수 x 600</b> 이다. 백 닢 단위로 내림한다. 예전에는 능력 넷의 합에 x20 만 곱해
    /// 서른 곱이 빠져 있었다.
    /// </remarks>
    /// <param name="weight">셈에 드는 덩치 — 바다는 척수 x 30(<c>0x0044364F</c>), 뭍에서 마주친 무리는 그 인원이다.</param>
    public static int Demand(in Enemy foe, int? weight = null) =>
        (SkillSum(foe) + 1) * (weight ?? foe.Ships * 30) * 20 / 100 * 100;

    /// <summary>요구액에 드는 적장 기능 넷 — 검술·포술·사격술·신학(<c>0x00455A36</c>).</summary>
    private static int SkillSum(in Enemy foe) =>
        foe.Leader is { } who ? who.Sword + who.Gunnery + who.Shooting + who.Theology : foe.Sum;

    /// <summary>
    /// 교섭이 통할 확률(%). <c>0x004559AE</c> 그대로다.
    /// </summary>
    /// <remarks>
    /// <code>
    ///   0045597d  ebp = 말하는 이의 웅변([esi+0x58])
    ///   00455980  [0x005B60F8](내 웅변 = 기능[6])이 더 크면 그것을 쓴다   ; max
    ///   0045598e  갈래가 3(이슬람 함대)이면  밑값 = rand(5)
    ///   004559a1  아니면                     밑값 = rand(10) + 30
    ///   004559ae  확률 = 내 함대수 / 2 + 웅변 * 10 + 밑값
    /// </code>
    /// <b>매력이 아니라 웅변</b>이다(기능 여섯째, <c>0x005B60F8</c> = 기능표 <c>+4*6</c>).
    /// 말은 부관이 대신하므로 부관 웅변과 내 웅변 가운데 <b>높은 쪽</b>을 쓴다.
    /// 이슬람 함대에게는 밑값 서른이 안 붙어 <b>거의 안 통한다</b>.
    /// </remarks>
    /// <param name="mateLuck">말하는 이(부관, 없으면 뱃사람)의 운 — 제독 운과 견준다.</param>
    public static int TalkOdds(Player player, EnemyKind kind, int mateRhetoric, Random rng, int mateLuck = 0)
    {
        int rhetoric = Math.Max(mateRhetoric, player.LevelOf(Skill.Names[Skill.Rhetoric]));
        int floor = kind == EnemyKind.Islam ? rng.Next(5) : rng.Next(10) + 30;
        // 첫 항은 <b>말하는 이와 제독의 운 가운데 높은 쪽 + 1 의 절반</b>이다(0x004555D0 이 미리 담아 둔 값).
        int luck = Math.Max(mateLuck, player.AbilityOf(Ability.Luck));
        return (luck + 1) / 2 + rhetoric * 10 + floor;
    }

    /// <summary>
    /// 교섭이 통하는지. <b>추격대·토벌대에게는 통하지 않는다</b>(<c>0x0045585C</c>).
    /// </summary>
    public static bool CanTalk(EnemyKind kind) => kind != EnemyKind.Chaser;

    /// <summary>백 가운데 몇이면 되는지를 굴린다 — 게임의 <c>0x004B7C62</c> 다.</summary>
    public static bool Roll(int percent, Random rng) => rng.Next(100) < percent;

    /// <summary>
    /// 도망칠 수 있는지(<c>0x00455B5F</c>).
    /// </summary>
    /// <remarks>
    /// <code>
    ///   0x00455B77  ecx = [0x005B3954] - 적 값       ; 내 <b>규율</b>
    ///   0x00455B83  eax = ecx + 인자 + 100
    ///   0x00455B88  eax /= 나눔수
    ///   0x00455B8D  rand(eax) 가 0 이 아니면 성공
    /// </code>
    /// 두 전역은 <b>내 함대 객체</b>(<c>0x005B3928</c>)의 칸이다 — <c>+0x28</c> 피로도가
    /// <c>0x005B3950</c>, <c>+0x2C</c> 규율이 <c>0x005B3954</c> 다. 보고가 끝나면 게임이
    /// 이 둘을 0 · 100 으로 되돌린다(<c>0x0041156A</c> · <c>0x00411576</c>)는 것으로 짚었다.
    ///
    /// 그래서 <b>내 값은 규율</b>이고 빼는 값은 <b>내 피로도</b>다 — 깃발(<c>edi</c>, 조우 갈래)이 0 이 아니면
    /// <c>0x005B3950</c> 을 바로 읽고(<c>0x00455B63</c>), 0 이면 함대 객체의 vt+0x0C(<c>0x00450220</c>)를 부르는데
    /// 그것도 <c>[0x005B3950]</c> 을 돌려주는 같은 값이다. 예전에는 그 깃발을 못 짚어 「적 배 수」로 적어 두었었다.
    /// 셈의 얼개(차 + 100 을 나누어 굴린다)는 게임 것 그대로다.
    /// </remarks>
    /// <param name="mateLuck">말하는 이의 운 — 제독 운과 견준다.</param>
    public static bool Escapes(Player player, in Enemy foe, Random rng, int mateLuck = 0)
    {
        // 확률 = (규율 − 피로 + (높은 쪽 운 + 1) + 100) / 나눔수, 나눔수는 갑작스런 무리·해적이 3, 추격대·이슬람이 5다.
        int luck = Math.Max(mateLuck, player.AbilityOf(Ability.Luck));
        int div = foe.Kind is EnemyKind.Raider or EnemyKind.Pirate ? 3 : 5;
        int odds = (player.Morale - player.Fatigue + luck + 1 + 100) / div;
        return Roll(odds, rng);
    }

    // ── 누구를 만나는가 ─────────────────────────────────────────────────────

    /// <summary>
    /// 적 함대 이름표(<c>0x00548900</c>). 앞의 여섯이 바다 것이고 그 뒤는 뭍 도적이다.
    /// </summary>
    public static readonly string[] Names =
    [
        "터키 해군", "이슬람 함대", "아랍 해적", "콜세르", "해적", "사략 함대",
    ];

    /// <summary>유럽 바다 구역의 주사위 폭 — <c>rand(700) == 0</c> 이면 해적이 붙는다.</summary>
    public const int EuropeRoll = 700;

    /// <summary>동지중해~아라비아해 구역의 주사위 폭 — <c>rand(400) == 0</c> 이면 이슬람 함대.</summary>
    public const int LevantRoll = 400;

    /// <summary>
    /// 그 자리가 어느 주사위 구역인지. 구역 밖이면 null — 바다 주사위가 아예 없다.
    /// </summary>
    /// <remarks>
    /// 게임 <c>0x0048CAC5</c> 의 경계를 위도·경도로 옮긴 것이다. 게임 좌표는 1/16 칸이라
    /// 경도 = x/40000·360−180, 위도 = 90 − y/20000·180 이다.
    /// <code>
    ///   A  18334 ≤ x &lt; 22222 · 3334 ≤ y &lt;  6667   경도 -15~20 · 위도 30~60   rand(700)
    ///   B  22222 ≤ x &lt; 27777 · 4445 ≤ y &lt; 10000   경도  20~70 · 위도  0~50   rand(400)
    /// </code>
    /// 대서양 한가운데·아메리카·동아시아는 두 구역 밖이다.
    /// </remarks>
    public static int? RollOf(double lat, double lon)
    {
        if (lon >= -15 && lon < 20 && lat > 30 && lat <= 60) return EuropeRoll;
        if (lon >= 20 && lon < 70 && lat > 0 && lat <= 50) return LevantRoll;
        return null;
    }

    /// <summary>
    /// 바다에서 걸음을 옮기는 동안 무리가 붙는지 굴린다(<c>0x0048CABA</c>). 안 붙으면 null.
    /// </summary>
    /// <param name="steps">그동안 걸은 걸음 수. 게임은 고리 한 바퀴(한 걸음)마다 한 번 굴린다.</param>
    /// <remarks>
    /// 게임은 이 주사위를 <b>화면에 보이는 함대에 두 칸 안으로 붙지 않았을 때</b>만 굴리고
    /// (<c>0x0048C049</c>, 볼트 <c>59.분석-해적 조우</c> 5·6절), 걸리면
    /// <c>0x004435B0(인물, 0, 1)</c> 로 교섭·도망·응전 창을 연다. 유럽 구역은 인물 262(해적),
    /// 81칸 표(후원자)에 뒤쫓는 이가 있으면 268(추격대) 이다(<c>0x0048CB00</c> → <c>0x0044FD60</c>) —
    /// 감찰관을 처벌해 배신한 후원자의 원래 기한이 지난 때다(<paramref name="chased"/>).
    /// 동쪽 구역은 인물 265(이슬람)다.
    /// </remarks>
    /// <param name="chased">뒤쫓는 후원자가 있는지(<c>Player.Pursuers</c>).</param>
    /// <param name="lookup">인물 번호로 적장을 찾는다. null 이거나 못 찾으면 붙박이 값(<see cref="CaptainOf"/>)이다.</param>
    public static Enemy? AtSea(double lat, double lon, int steps, Random rng,
                               Func<int, Captain?>? lookup = null, bool chased = false)
    {
        if (RollOf(lat, lon) is not { } roll) return null;

        for (int i = 0; i < steps; i++)
        {
            if (rng.Next(roll) != 0) continue;
            return roll == LevantRoll
                ? Make(EnemyKind.Islam, IslamLeader, rng, lookup)
                : chased ? Make(EnemyKind.Chaser, ChaserLeader, rng, lookup)
                : Make(EnemyKind.Pirate, PirateLeader, rng, lookup);
        }
        return null;
    }

    /// <summary>추격대를 이끄는 인물 — 인물 밑표의 「상금 벌기」(<c>0x0048CB00</c> 이 늘 이 번호를 넘긴다).</summary>
    public const int ChaserLeader = 268;

    /// <summary>바다 주사위가 부르는 인물 — 유럽 해적 262 · 이슬람 265(<c>0x0048CABA</c>).</summary>
    public const int PirateLeader = 262, IslamLeader = 265;

    /// <summary>
    /// 그 적장의 무리 하나를 짓는다. 척수(교섭 창 몫)와 능력 합은 적장에서 낸다(<c>0x004435B0</c>).
    /// </summary>
    /// <remarks>
    /// <code>
    ///   척수 = 무력/14 + (해적 2 · 군인·정복자 1)   1~8, 굴림 없음
    /// </code>
    /// 능력 합(<see cref="Enemy.Sum"/>)은 <b>적장을 모를 때의 갈음값</b>일 뿐이다 — 요구액에 드는 것은 적장의
    /// 검술·포술·사격술·신학(<see cref="SkillSum"/>, <c>0x00455A36</c>)이고, 적장은 늘 있어(<c>0x004435DE</c> 의
    /// <c>0x004319D0(번호)</c>) 이 값은 실제로 안 쓰인다. 무력·지력·매력·운의 게임 값(날값−1) 합에 1 을 더해 둔다.
    /// 이름은 인물 이름(262 「사략 함대」 따위)이다.
    /// </remarks>
    private static Enemy Make(EnemyKind kind, int leaderId, Random rng, Func<int, Captain?>? lookup)
    {
        var leader = lookup?.Invoke(leaderId) ?? CaptainOf(leaderId);
        string name = NameOf(leaderId)
                      ?? (kind == EnemyKind.Islam ? Names[1] : Names[rng.Next(2, Names.Length)]);
        int bonus = leader.Job switch { EnemyFleet.PirateJob => 2, EnemyFleet.SoldierJob or EnemyFleet.ConquerorJob => 1, _ => 0 };
        int ships = Math.Clamp(leader.Might / 14 + bonus, 1, SeaBattle.PerSide);
        int sum = (leader.Might - 1) + (leader.Mind - 1) + (leader.Charm - 1) + (leader.Luck - 1) + 1;
        return new Enemy(kind, name, ships, sum, leader);
    }

    /// <summary>
    /// 지도에 보이는 인물 함대를 친다 — 그 사람을 적장으로 한 무리(<c>0x0048CC20</c> → <c>0x004435B0(id, 목록, 0)</c>).
    /// </summary>
    /// <remarks>
    /// 척수·능력 합은 <see cref="Make"/> 와 같은 셈이다(교섭 창 몫이라 플래그 0 에서는 안 쓰인다).
    /// 갈래는 창 제목에만 쓰이므로 해적 직업이면 해적, 아니면 이름 있는 적으로 둔다.
    /// </remarks>
    public static Enemy OfPerson(in Captain leader, string name)
    {
        int bonus = leader.Job switch { EnemyFleet.PirateJob => 2, EnemyFleet.SoldierJob or EnemyFleet.ConquerorJob => 1, _ => 0 };
        int ships = Math.Clamp(leader.Might / 14 + bonus, 1, SeaBattle.PerSide);
        int sum = (leader.Might - 1) + (leader.Mind - 1) + (leader.Charm - 1) + (leader.Luck - 1) + 1;
        var kind = leader.Job == EnemyFleet.PirateJob ? EnemyKind.Pirate : EnemyKind.Raider;
        return new Enemy(kind, name, ships, sum, leader);
    }

    /// <summary>
    /// 조우 인물 262~270 의 붙박이 값 — 인물표·인물 밑표를 못 읽었을 때 쓴다.
    /// </summary>
    /// <remarks>
    /// 나라·직업은 EXE 밑표(<c>0x004DF3F0</c> <c>+0x14</c>·<c>+0x20</c>), 능력(날값)·기능은
    /// 같이 깔린 <c>인물표.json</c> 에서 옮겼다(볼트 92 의 8절).
    /// <code>
    ///   id  이름         나라          직업  무력 지력 매력 운 신앙 포술 검술 사격
    /// </code>
    /// </remarks>
    private static readonly (string Name, Captain Who)[] Builtins =
    [
        ("사략 함대",   new(262, 11, 4, 56, 46, 31, 51, 21, 1, 1, 0)),
        ("해적",        new(263,  3, 4, 61, 51, 31, 51, 21, 1, 2, 1)),
        ("콜세르",      new(264, 16, 4, 66, 51, 31, 51, 21, 2, 2, 2)),
        ("아랍 해적",   new(265, 27, 4, 56, 46, 31, 51, 81, 0, 1, 1)),
        ("이슬람 함대", new(266, 26, 7, 66, 51, 36, 51, 81, 1, 2, 2)),
        ("터키 해군",   new(267, 37, 7, 76, 56, 46, 51, 81, 2, 3, 2)),
        ("상금벌기",    new(268,  1, 4, 61, 46, 31, 51, 31, 1, 1, 1)),
        ("사설 함대",   new(269,  0, 7, 71, 51, 41, 51, 51, 2, 2, 2)),
        ("추격 함대",   new(270,  1, 7, 81, 56, 51, 51, 61, 2, 3, 3)),
    ];

    /// <summary>붙박이 적장. 표에 없는 번호면 262(해적)로 갈음한다.</summary>
    public static Captain CaptainOf(int id) =>
        (Builtins.FirstOrDefault(b => b.Who.Id == id) is { Name: not null } hit ? hit : Builtins[0]).Who;

    private static string? NameOf(int id) => Builtins.FirstOrDefault(b => b.Who.Id == id).Name;

    /// <summary>
    /// 인물표 한 줄과 밑표 한 줄로 적장을 짓는다. 나라·직업을 모르면 붙박이 값을 쓴다.
    /// </summary>
    /// <param name="stats">능력 여섯(날값) — 체력·지력·무력·매력·운·신앙심.</param>
    /// <param name="skills">기능 열셋 — 차례는 <see cref="Skill.Names"/>.</param>
    /// <param name="face">밑표 얼굴 — 운세 칸의 별자리에 든다.</param>
    /// <param name="blood">밑표 혈액형.</param>
    public static Captain CaptainOf(int id, IReadOnlyList<int>? stats, IReadOnlyList<int>? skills,
                                    int? nation, int? job, int? face = null, int? blood = null)
    {
        var fallback = CaptainOf(id);
        int Stat(int k, int dflt) => stats != null && k < stats.Count ? stats[k] : dflt;
        int Skl(int k, int dflt) => skills != null && k < skills.Count ? skills[k] : dflt;
        int home = nation ?? fallback.Nation;
        return new Captain(
            id, nation ?? fallback.Nation, job ?? fallback.Job,
            Might: Stat(Ability.Might, fallback.Might),
            Mind: Stat(Ability.Mind, fallback.Mind),
            Charm: Stat(Ability.Charm, fallback.Charm),
            Luck: Stat(Ability.Luck, fallback.Luck),
            Faith: Stat(Ability.Faith, fallback.Faith),
            Gunnery: Skl(Skill.Gunnery, fallback.Gunnery),
            Sword: Skl(Skill.Sword, fallback.Sword),
            Shooting: Skl(Skill.Shooting, fallback.Shooting),
            Fortune: FleetRaid.FortuneOf(face ?? 0, blood ?? 0, home),
            Theology: Skl(Skill.Theology, fallback.Theology));
    }

    /// <summary>
    /// 만난 무리 하나를 굴린다. 적장은 갈래 안에서 고른다 — 해적 262~264 · 이슬람 265~267.
    /// </summary>
    /// <remarks>자리를 따지지 않는다 — 모의해전이 쓴다. 적장을 고르는 굴림은 우리 것이다.</remarks>
    public static Enemy Roll(Random rng, Func<int, Captain?>? lookup = null)
    {
        bool islam = rng.Next(4) == 0;
        int leader = (islam ? IslamLeader : PirateLeader) + rng.Next(3);
        return Make(islam ? EnemyKind.Islam : EnemyKind.Pirate, leader, rng, lookup);
    }
}
