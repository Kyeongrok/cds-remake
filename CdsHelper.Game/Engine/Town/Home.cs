using CdsHelper.Support.Local.Models;

namespace CdsHelper.Game.Engine.Town;

/// <summary>
/// 자택에서 쉬는 규칙. 값은 안 든다 — 내 집이다.
/// </summary>
/// <remarks>
/// 게임은 <c>0x004A2AD0(개월 x 30, 1)</c> 로 <b>날수</b>를 넘긴다 — 달력 달이 아니라
/// 서른 날이다. 쉬면 하루에 피로 -1 · 사기 +3 씩 돌아오는데, 그것은 날을 넘기는 자리가
/// 함께 하므로 <see cref="Support.Local.Models.Player.AdvanceDays"/> 가 맡는다.
/// 그래서 한 달만 쉬어도 폭풍 몇 번 분이 한꺼번에 풀린다.
/// </remarks>
public static class Home
{
    /// <summary>장기 휴양으로 고를 수 있는 가장 긴 달수(<c>0x00460782</c> 의 <c>push 0xC</c>).</summary>
    public const int MaxRestMonths = 12;

    /// <summary>휴양 한 달을 며칠로 세는지. 게임도 서른 날이다.</summary>
    public const int DaysPerMonth = 30;

    /// <summary>그만큼 쉬면 며칠이 가는지.</summary>
    public static int RestDays(int months) => DaysPerMonth * months;

    /// <summary>쉬고 나서 나오는 지문 셋. 게임 것 그대로다(<c>0x00539840</c> 벌).</summary>
    /// <remarks>
    /// 게임은 아내가 있으면 <b>아내가</b>, 없으면 이 셋 가운데 하나를 낸다
    /// (<c>0x004607FE</c> 의 <c>rand(3)</c>).
    /// </remarks>
    public static readonly string[] RestWords =
        ["피로가 풀렸다!", "체력이 회복되었다!", "기분이 상쾌하다!"];

    /// <summary>쉬고 나서 건네는 한마디.</summary>
    public static string RestWord(Random random) => RestWords[random.Next(RestWords.Length)];

    /// <summary>아내가 있으면 아내가 하는 말 셋(<c>0x005397E0</c>~).</summary>
    public static readonly string[] RestWifeWords =
    [
        "피로 풀렸어요? 너무 무리하지 마세요.",
        "집에서는 편히 쉬세요.",
        "이렇게 쉬기는 오랫만이죠.",
    ];

    /// <summary>쉬고 나서 아내가 건네는 한마디.</summary>
    public static string RestWifeWord(Random random) => RestWifeWords[random.Next(RestWifeWords.Length)];

    // ── 후손을 남긴다 ────────────────────────────────────────────────────────

    /// <summary>
    /// 후손 하나를 얻는 데 드는 날.
    /// </summary>
    /// <remarks>게임도 끝에 <c>0x00469850(5)</c> 로 닷새를 넘긴다(<c>0x00461401</c>).</remarks>
    public const int HeirDays = 5;

    /// <summary>주사위 폭과 되는 눈(<c>0x004613CC</c> 의 <c>rand(8) &lt; 2</c>).</summary>
    public const int HeirRoll = 8, HeirWin = 2;

    /// <summary>후손을 보려면 컨디션이 이만큼은 있어야 한다(<c>0x0046139E</c> 의 <c>cmp 0x64</c>).</summary>
    public const int HeirCondition = 100;

    /// <summary>
    /// 자택에 들어서면 <b>아내</b>가 맞는 말(<c>0x004144C0</c>) — 여덟 가운데 하나를 굴린다.
    /// </summary>
    /// <remarks><c>0x0055E5F8</c> ~ <c>0x0055E720</c>. 아내가 없으면 아무 말도 없다.</remarks>
    public static readonly string[] WifeWelcome =
    [
        "수고했어요. 다친 데는 없는 것 같아 안심했어요.",
        "어디까지 갔었어요. 걱정했었어요.",
        "모험은 어땠어요? 이번에는 오래 있을 수 있지요.",
        "어서 오세요. 오늘 돌아오는 날이었어요?",
        "끼얏, 놀랬어요. 도둑이 들어왔나 했어요.",
        "어서 오세요, 당신!",
        "어서 오세요. 슬슬 돌아올 때라고 생각하고 있었어요.",
        "어서 오세요. 목욕, 식사? 아니면 저요?",
    ];

    // ── 아내에게 말을 건다 — 0x004149F0 ──────────────────────────────────────

    /// <summary>
    /// 아내에게 말을 걸었을 때, <b>아이가 하나도 없으면</b> 아내가 혼자 하는 말
    /// (<c>0x00414A93</c> 의 <c>rand(7)</c>).
    /// </summary>
    public static readonly string[] WifeAlone =
    [
        "집 많이 비우지 말아요. 혼자 있으면 외로워요.",
        "아니, 벌써 배가 고파요? 지금 만들테니 기다려요.",
        "있죠, 가끔은 둘이서 외출해요.",
        "벌써 자려구요? 아직 해도 지지 않았는데.",
        "나에게는 선물 안 줘요?",
        "다른 여자에게 눈 돌리면 안돼요!",
        "가끔은 저금도 하세요. 장래 무슨 일이 일어날지 모르니까요.",
    ];

    /// <summary>아내와 아이가 주고받는 두 줄 — 아내가 먼저, 아이가 받는다.</summary>
    /// <param name="Wife">아내가 하는 말. <c>{0}</c> 이 있으면 아이 이름이 든다.</param>
    /// <param name="Child">아이가 받는 말.</param>
    /// <param name="Wife">아내 말. <c>{0}</c> 은 아이 이름, <c>{1}</c> 은 그 은/는 조사다.</param>
    public readonly record struct FamilyTalk(string Wife, string Child);

    /// <summary>
    /// 아내에게 말을 걸면 <b>아이 하나를 골라</b> 두 줄을 주고받는다(<c>0x004146D0</c>).
    /// </summary>
    /// <remarks>
    /// 아이 나이로 셋(<c>0~4</c> · <c>5~9</c> · <c>10 이상</c>), 갈래는 <c>rand(3)</c> 다.
    /// <b>0~4세만 성별을 안 가린다</b>(<c>0x00414730</c>). 고르는 아이는 아들·딸 각각의
    /// <b>맏이</b> 가운데 <c>rand(2)</c> 로 하나다(<c>0x004AB790</c>) — 나이 상한이 없어
    /// 열다섯이 넘어도 「10세 이상」 대본을 쓴다.
    ///
    /// <b>값은 하나도 안 바뀐다</b> — 순전히 연출이다.
    /// </remarks>
    public static FamilyTalk TalkWith(bool daughter, int age, Random random)
    {
        int pick = random.Next(3);
        if (age < BabyTo)
            return pick switch
            {
                0 => new("까꿍!", "아~, 아~."),
                1 => new("{0}, 아버지 오셨어요.", "아빠!"),
                _ => new("아버지에게 인사해야지.", "아~부."),
            };

        if (age < GrownFrom)
            return pick switch
            {
                0 => new("{0}{1} 크면, 뭐가 되고 싶어?",
                         daughter ? "신부가 되고 싶어!" : "나는..... 어른이 되고 싶어!"),
                1 => new("아버지가 돌아오시면, 맛있는 걸 먹을 수 있어요!",
                         daughter ? "나도 요리 할거야~." : "와~, 와~!"),
                _ => new("아버지에게 부탁할 일 있으면 지금 말해요.",
                         daughter ? "으~응. 한번만이라도 좋으니, 배에 태워 주세요." : "같이 놀아요!"),
            };

        return pick switch
        {
            0 => new("장래일 신중히 생각하고 있니?",
                     daughter ? "아버지 같은 사람이랑 결혼할거야!" : "나도 모험할거야."),
            1 => new("가끔은 가족끼리 놀러 가고 싶어요.",
                     daughter ? "간다면, 푹 쉴 수 있는 곳이 좋겠군." : "그래요, 그래요, 한번도 없어요!"),
            _ => new("놀지만 말고 공부도 좀 해요.",
                     daughter ? "예~." : "나는 바다의 사나이가 될테니까 필요 없어."),
        };
    }

    /// <summary>대본이 갈리는 나이(<c>0x004146EB</c> 의 <c>cmp 5</c> · <c>cmp 0xA</c>).</summary>
    public const int BabyTo = 5, GrownFrom = 10;

    /// <summary>
    /// 아내와 이야기할 때 끼는 아이 — 아들·딸 각각의 맏이 가운데 하나다(<c>0x00414A05</c>).
    /// </summary>
    /// <remarks>
    /// 귀가 인사(<see cref="WelcomerOf"/>)와 달리 <b>나이 테두리가 없다</b> — 갓난아이도
    /// 열다섯 넘은 아이도 낀다.
    /// </remarks>
    public static Player.Child? TalkerOf(Player player, Random random)
    {
        var born = player.Children.Where(c => c.IsBornBy(player.Date)).ToList();
        var son = born.Where(c => !c.Daughter).MaxBy(c => c.AgeOn(player.Date));
        var girl = born.Where(c => c.Daughter).MaxBy(c => c.AgeOn(player.Date));
        if (son is null) return girl;
        if (girl is null) return son;
        return random.Next(2) == 0 ? girl : son;
    }

    /// <summary>
    /// 자택에 들어서면 <b>아이</b>가 맞는 말(<c>0x00414550</c>) — 아들·딸과 <b>열 살</b>로 넷이 갈린다.
    /// </summary>
    /// <remarks>
    /// <code>
    ///   아들 ~9세  0x0055E748 (다섯)     아들 10세~ 0x0055E830 (다섯)
    ///   딸   ~9세  0x0055E958 (다섯)     딸   10세~ 0x0055EA60 (넷)
    /// </code>
    /// 태어나기 전이면 아무 말도 없다.
    /// </remarks>
    public static string WelcomeOf(bool daughter, int age, Random random)
    {
        var set = daughter
            ? age < GrownChildAge ? YoungGirl : GrownGirl
            : age < GrownChildAge ? YoungBoy : GrownBoy;
        return set[random.Next(set.Length)];
    }

    /// <summary>아이 말이 갈리는 나이(<c>0x00414571</c> 의 <c>cmp 0xA</c>).</summary>
    public const int GrownChildAge = 10;

    /// <summary>귀가 인사를 하는 나이(<c>0x00414670</c> 의 <c>0x004AB800(성별, 5, 14)</c>).</summary>
    public const int WelcomeFrom = 5, WelcomeTo = 14;

    /// <summary>
    /// 그 방문에 인사할 아이 <b>하나</b>를 고른다(<c>0x00414670</c>).
    /// </summary>
    /// <remarks>
    /// 아들과 딸에서 각각 하나씩 뽑고 둘 다 있으면 <c>rand(2)</c> 로 가른다 — <b>둘이 같이
    /// 말하지 않는다</b>. 이름을 아직 안 지은 아이가 있으면 인사가 통째로 건너뛰어진다.
    ///
    /// <b>배 속의 아이도 막는다</b>(원본의 흠) — <c>0x00414675</c> 는 「아직 아버지가 못 본」
    /// 깃발(<c>아이 +0x338</c>)만 보는데, 그 깃발은 <b>임신이 확정될 때</b> 서고
    /// (<c>0x00460F7E</c>) 이름짓기(<c>0x0045FFC0</c>)는 태어나기 전이면 아무것도 안 하고
    /// 깃발을 그대로 둔다. 그래서 <b>임신부터 출산까지 열 달 동안 다른 아이들의 귀가 인사가
    /// 한 번도 안 나온다</b>. 아내 인사와 아내와의 대화는 그대로 나온다.
    /// </remarks>
    public static Player.Child? WelcomerOf(Player player, Random random)
    {
        if (player.Children.Any(c => !c.Introduced)) return null;

        var able = player.Children
            .Where(c => c.IsBornBy(player.Date)
                        && c.AgeOn(player.Date) >= WelcomeFrom && c.AgeOn(player.Date) <= WelcomeTo)
            .ToList();
        if (able.Count == 0) return null;

        var son = able.FirstOrDefault(c => !c.Daughter);
        var girl = able.FirstOrDefault(c => c.Daughter);
        if (son is null) return girl;
        if (girl is null) return son;
        return random.Next(2) == 0 ? girl : son;
    }

    private static readonly string[] YoungBoy =
    [
        "앗, 아버지 돌아오셨어요! 나, 많이 컸죠.",
        "가끔은 나와 놀아 주세요~. 항상 집에 없어.",
        "아버지 도와 주세요. 오줌 싸서 엄마가 화내고 있어요.",
        "어서 오세요. 아버지! 나도 크면 아버지 배에 탈거야.",
        "어서 오세요. 응, 선물은?",
    ];

    private static readonly string[] GrownBoy =
    [
        "어서 오세요. 모험 그렇게 재미있어요? 가끔은 집에 돌아 오세요.",
        "돌아오셨어요? 빨리 모험 이야기 해 주세요.",
        "아버지 기다렸어요? 얼마 전 싸움에 지고 말았어요. 저에게도 검술을 가르쳐 주세요!",
        "돌아오셨어요? 아버지. 팔씨름 한판 할까요. 이번에는 자신 있어요.",
        "돌아오셨어요? 그런데 용돈이 필요해요.",
    ];

    private static readonly string[] YoungGirl =
    [
        "돌아오셨어요? 아버지. 함께 목욕해요.",
        "요리 배웠어요! 먹어 주실거죠.",
        "이번에 놀러 데리고 가 주세요. 가끔은 괜찮죠.",
        "돌아오셨어요? 이번에 아버지 배에 태워 주세요!",
        "돌아오셨어요? 있잖아요, 아버지는 어머니 어떻게 알게 되셨어요? 어머니는 전혀 가르쳐 주지 않아요.",
    ];

    private static readonly string[] GrownGirl =
    [
        "아버지, 돌아오셨어요? 이번에는 오래 머무르실 거죠.",
        "돌아오셨어요? 가끔은 집에 있어 주세요. 집안일도 생각해 주셔야죠.",
        "돌아오셨어요? 오늘은 제가 요리했어요! 먹어 주실거죠? 둘이 먹다 하나가 죽어도 모른다니까요.",
        "돌아오셨어요? 그런데 저 예뻐졌어요?",
    ];

    /// <summary>컨디션이 모자랄 때 아내가 하는 말(<c>0x00539A70</c>).</summary>
    public const string HeirTired = "안색이 안 좋은데요. 너무 무리하지 마세요.";

    /// <summary>
    /// 후손을 남길 수 있는지 — <b>아내가 있어야 한다</b>.
    /// </summary>
    /// <remarks>
    /// 게임은 줄의 켜짐을 <c>0x00460650</c> 하나로 정한다 — <c>[0x005B61B0] != -1</c>,
    /// 곧 아내가 있느냐다. 안 되면 줄이 흐릴 뿐 사라지지는 않는다.
    ///
    /// 게임은 그 뒤로 관문을 둘 더 둔다. 체력(<c>[0x005B60D8] &gt;= 100</c>)은
    /// <see cref="HeirCondition"/> 으로 옮겼고, <c>[아내+4] == 2</c> 는 <b>사람 갈래</b>가
    /// 여급(2)인지를 보는 것이라 아내가 있으면 언제나 참이다 — 옮길 것이 없다.
    /// </remarks>
    public static bool CanLeaveHeir(Player player) => player.Spouse.Length > 0;

    /// <summary>
    /// 이번에 후손을 얻었는지. <b>여덟에 둘</b>이라 네 번에 한 번 꼴이다.
    /// </summary>
    /// <remarks>
    /// 게임은 굴리기 전에 빈 아이 칸이 있고(<c>0x004AB9F0</c>) 막내가 이미 태어났는지(아내 <c>+0x38 == -1</c>)를 본다 —
    /// 둘 다 차 있거나 배 속에 아이가 있으면 굴림과 상관없이 안 된다.
    /// </remarks>
    public static bool HeirBorn(Player player, Random random) =>
        player.Children.Count < Player.MaxChildren
        && player.Children.All(c => c.IsBornBy(player.Date))
        && random.Next(HeirRoll) < HeirWin;

    /// <summary>
    /// 아이를 잉태한다(<c>0x00460C50</c>).
    /// </summary>
    /// <remarks>
    /// <code>
    ///   성별      rand(2) == 0 이면 딸. 이미 아이가 있으면 그 반대 성별
    ///   태어나는 날  지금 달 + 10 (넘치면 이듬해), 날은 그 달 끝날(게임 굴림 고리가 늘 끝날 쪽으로 멎는다)
    ///   능력치    아버지 값 + rand(20) − 10 + 1, 1~100 (0x004610C0)
    ///   기능      아버지가 3 인 것은 3, 아닌 것 가운데 하나를 골라 2 (0x00460F1C)
    ///   언어      아버지가 3 인 것은 3 (0x00460EB8)
    /// </code>
    /// 능력치 폭은 칸마다 다르고(운·신앙심만 <c>rand(30) − 15</c>), 딸이면 체력·무력이 −10, 나머지 넷이 +5 다.
    /// 거기에 <b>아내 운명 코드 줄</b>(<c>0x0051B0A0</c>, <see cref="Oracle.WifeSlope"/>)이 얹힌다.
    /// </remarks>
    /// <param name="wifeFortune">아내 운명 코드. 모르면 −1 이라 보정이 없다.</param>
    /// <param name="wifeBlood">아내 혈액형. 모르면 −1 이라 아버지 것만 본다.</param>
    /// <param name="daughter">딸인지. 안 주면 여기서 굴린다(이미 아이가 있으면 그 반대 성별이다).</param>
    public static Player.Child Conceive(Player father, Random random, string name,
                                        int wifeFortune = -1, int wifeBlood = -1, bool? daughter = null,
                                        int nationLanguage = -1, int wifeTongues = 0)
    {
        daughter ??= father.Children.Count > 0 ? !father.Children[^1].Daughter : random.Next(2) == 0;

        var due = DueDate(father.Date, random);

        var abilities = new int[6];
        for (int i = 0; i < abilities.Length; i++)
            abilities[i] = AbilityOfChild(father.AbilityOf(i), i, daughter.Value, wifeFortune, random);

        var child = new Player.Child(name, daughter.Value, due, abilities,
                                     new int[Skill.Names.Length], new int[Skill.Languages.Length],
                                     Blood: BloodOf(father.Blood, wifeBlood, random),
                                     Face: ChildFaces[random.Next(2) + (daughter.Value ? 2 : 0)][0],
                                     GrownFace: father.Face,
                                     // 주량은 아버지 것에 rand(3) − 1 을 얹어 0~3 으로 자른다(0x00460E99).
                                     Drinking: Math.Clamp(father.Drinking + random.Next(3) - 1,
                                                          0, Player.MaxDrinking));
        return Bless(father, random, child, nationLanguage, wifeTongues);
    }

    /// <summary>
    /// 아이 얼굴 표(<c>0x00560D10</c>) — 넉 줄에 얼굴 셋씩이다.
    /// </summary>
    /// <remarks>
    /// <code>
    ///   아들  393 · 396 · 395        아들  394 · 397 · 395
    ///   딸    139 · 141 · 143        딸    140 · 142 · 143
    /// </code>
    /// 태어날 때 줄을 굴려 고르고(<c>0x00460CD8</c> — <c>rand(2) + 딸*2</c>), 그 뒤로는 나이로
    /// 칸이 바뀐다(<c>0x0047D710</c>). 아들은 <b>열다섯 살부터</b> 표를 떠나 제 얼굴을 쓰는데
    /// (<c>0x0047D726</c>), 그 얼굴은 후손으로 물려받을 때 정해지므로 여기서는 표만 든다.
    /// 딸은 끝까지 표를 쓴다 — 그래서 열 살 넘은 두 줄이 같은 얼굴(143)이다.
    /// </remarks>
    public static readonly int[][] ChildFaces =
    [
        [393, 396, 395], [394, 397, 395],
        [139, 141, 143], [140, 142, 143],
    ];

    /// <summary>나이 한 칸이 다섯 해고 칸은 셋뿐이다(<c>0x0047D73F</c> 의 <c>idiv 5</c> → <c>0x0049E540(0,2)</c>).</summary>
    public const int FaceYearsPerStep = 5, FaceSteps = 3;

    /// <summary>아들이 표를 떠나 제 얼굴을 쓰는 나이(<c>0x0047D726</c> 의 <c>cmp 0xF</c>).</summary>
    /// <remarks>그 얼굴은 태어날 때 받아 둔 <b>아버지 얼굴</b>이다(<c>0x00460F88</c>).</remarks>
    public const int GrownSonAge = 15;

    /// <summary>
    /// 그 나이의 아이 얼굴(<c>0x0047D710</c>). 얼굴을 안 적던 옛 세이브면 −1 이다.
    /// </summary>
    public static int FaceOf(Player.Child child, int age)
    {
        // 열다섯 넘은 아들은 표를 떠나 아버지 얼굴을 쓴다(0x0047D726).
        if (!child.Daughter && age >= GrownSonAge && child.GrownFace >= 0) return child.GrownFace;

        if (child.Face < 0) return -1;
        int row = Array.FindIndex(ChildFaces, r => r[0] == child.Face);
        if (row < 0) return child.Face;

        int step = Math.Clamp(age / FaceYearsPerStep, 0, FaceSteps - 1);
        return ChildFaces[row][step];
    }

    /// <summary>능력치 폭과 밑값(<c>0x004610C0</c> 의 칸별 값) — 운·신앙심만 넓다.</summary>
    private static readonly int[] Spread = [20, 20, 20, 20, 30, 30];
    private static readonly int[] Floor = [-10, -10, -10, -10, -15, -15];

    /// <summary>딸이면 얹히는 값(<c>0x00461123</c>) — 체력·무력은 오히려 깎인다.</summary>
    private static readonly int[] DaughterBonus = [-10, 5, -10, 5, 5, 5];

    /// <summary>아이 능력치 한 칸(<c>0x004610C0</c>). 1~100 으로 자른다.</summary>
    /// <remarks>
    /// 능력치 칸은 <b>보이는 값에서 1 을 뺀 것</b>을 담는다([[reference_cds95_player_globals]]) —
    /// 우리도 원본과 같은 담는 값을 쓴다. 그래서 식이 <c>+1</c> 한 뒤 1~100 으로 자르고
    /// (<c>0x004611A6</c>) 다시 1 을 빼는 것까지 그대로다(<c>0x00460E26</c>의 <c>dec eax</c>).
    /// </remarks>
    public static int AbilityOfChild(int fathers, int ability, bool daughter, int wifeFortune, Random random)
    {
        int value = fathers + random.Next(Spread[ability]) + Floor[ability]
                    + (daughter ? DaughterBonus[ability] : 0)
                    + Oracle.WifeSlope(wifeFortune, ability) + 1;
        return Math.Clamp(value, 1, 100) - 1;
    }

    /// <summary>한 해의 달마다의 날 수(<c>0x004FF940</c>) — 윤년을 안 본다.</summary>
    private static readonly int[] MonthDays = [0, 31, 28, 31, 30, 31, 30, 31, 31, 30, 31, 30, 31];

    /// <summary>
    /// 태어나는 날(<c>0x00460CDE</c>) — 열 달 뒤, 날은 <c>rand(31)+1</c> 이 <b>달 길이 이상</b> 나올 때까지 굴린다.
    /// </summary>
    /// <remarks>
    /// 견줌이 뒤집혀 있어(원본의 흠) 실제로는 <b>달 끝 무렵</b>만 나온다 — 2월이면 28~31 이고, 서른 날짜리 달에도
    /// 31 이 나온다. 그래서 그 달에 없는 날이면 달 끝날로 앉힌다.
    /// </remarks>
    public static DateTime DueDate(DateTime today, Random random)
    {
        int year = today.Year, month = today.Month + 10;
        if (month > 12) { month -= 12; year++; }

        int day;
        do { day = random.Next(31) + 1; } while (day < MonthDays[month]);
        return new DateTime(year, month, Math.Min(day, DateTime.DaysInMonth(year, month)));
    }

    /// <summary>
    /// 아이 혈액형(<c>0x00460FA0</c>) — 0 A · 1 B · 2 O · 3 AB.
    /// </summary>
    /// <remarks>
    /// 부모가 AB 면 한쪽 인자는 A, 다른 쪽은 B 로 보고, 아니면 반반으로 O 를 섞는다. 어머니 쪽 인자를
    /// <b>반만 굴리는</b> 원본의 흠(굴림이 빗나가면 아버지 값이 그대로 남는다)까지 그대로 옮겼다.
    /// </remarks>
    public static int BloodOf(int fathers, int mothers, Random random)
    {
        if (fathers is < 0 or > 3) return 0;
        if (mothers is < 0 or > 3) mothers = fathers;

        int f = fathers == 3 ? 0 : fathers;
        int altF = fathers == 3 ? 1 : random.Next(2) == 0 ? 2 : fathers;
        int m = mothers == 3 ? 0 : mothers;
        int altM = mothers == 3 ? 1 : random.Next(2) == 0 ? 2 : altF;   // 어머니 쪽 굴림이 빗나가면 아버지 값이 남는다

        if (random.Next(2) == 0) f = altF;
        if (random.Next(2) == 0) m = altM;

        if ((f == 0 && m == 1) || (f == 1 && m == 0)) return 3;
        if (f == 0 || m == 0) return 0;
        return f == 1 || m == 1 ? 1 : 2;
    }

    /// <summary>
    /// 아이를 가지려 한 뒤 아내가 하는 말 스물하나(<c>0x00414B30</c>). 쉰 살 밑이면 앞 셋은 안 나온다.
    /// </summary>
    public static string WifeWord(int age, Random random)
    {
        int at = age >= 50 ? random.Next(WifeWords.Length) : random.Next(WifeWords.Length - 3) + 3;
        return WifeWords[at];
    }

    /// <summary>그 말들(<c>0x00414B30</c> 표 차례 그대로).</summary>
    public static readonly string[] WifeWords =
    [
        "당신 아직 젊군요.",
        "괜찮아요. 무리하지 않아도.",
        "오래간만이에요.",
        "사랑해요, 당신.",
        "당신이 없으면 외로우니까, 아이들이 많이 있었으면 해요.",
        "너무 안 돌아오면 생각 달리 할테니까···농담이에요.",
        "남자애나 여자애나 어느 쪽이라도 좋으니, 건강한 아이면 좋겠어요.",
        "언제까지나 함께 있고 싶어요.",
        "···(화끈)",
        "후후후.",
        "싫어요.",
        "응, 당신은.",
        "그렇게 쳐다보지 말아요.",
        "좀더 이쪽으로 와요.",
        "서두르지 말아요. 밤은 기니까.",
        "둘이 있을 때가 가장 행복해요.",
        "나는 여자 아이가 좋아.",
        "당신은 남자 아이가 좋죠?",
        "이대로 밤이 새지 않았으면···",
        "사랑해요.",
        "당신도 좋아하죠.",
    ];

    /// <summary>
    /// 기능·언어를 아버지에게서 받는다 — 잉태할 때와, 이름만 있는 옛 세이브 아이를 채울 때 쓴다.
    /// </summary>
    /// <remarks>
    /// 언어는 <b>셋 가운데 하나만 맞아도</b> 3 으로 준다(<c>0x00460EB8</c>~<c>0x00460F1A</c>).
    /// <code>
    ///   0x00460ECE  제독 나라의 언어인가        ; 나라표 +0x04
    ///   0x00460EE2  아버지가 3 인 언어인가
    ///   0x00460EF8  아내가 가르치는 언어인가    ; 여급 표 +0x20 비트
    /// </code>
    /// </remarks>
    /// <param name="nationLanguage">제독 나라의 언어 번호. 모르면 −1.</param>
    /// <param name="wifeTongues">아내가 가르치는 언어 비트. 아내가 없으면 0.</param>
    public static Player.Child Bless(Player father, Random random, Player.Child child,
                                     int nationLanguage = -1, int wifeTongues = 0)
    {
        var skills = Skill.Names.Select(n => father.LevelOf(n) >= Skill.MaxLevel ? Skill.MaxLevel : 0).ToArray();
        var empty = Enumerable.Range(0, skills.Length).Where(i => skills[i] == 0).ToList();
        if (empty.Count > 0) skills[empty[random.Next(empty.Count)]] = 2;

        var tongues = new int[Skill.Languages.Length];
        for (int i = 0; i < tongues.Length; i++)
        {
            bool knows = i == nationLanguage
                         || father.TongueOf(Skill.Languages[i]) >= Skill.MaxLevel
                         || (wifeTongues & (1 << i)) != 0;
            tongues[i] = knows ? Skill.MaxLevel : 0;
        }

        var abilities = child.Abilities.All(a => a == 0)
            ? Enumerable.Range(0, 6).Select(i => Math.Clamp(father.AbilityOf(i) + random.Next(20) - 10 + 1, 1, 100)).ToArray()
            : child.Abilities;
        return child with { Abilities = abilities, Skills = skills, Tongues = tongues };
    }

    // ── 자택에 가면 — 아이 소개 ──────────────────────────────────────────────

    /// <summary>이름을 지을 때 글자 수(원본 인물 이름 칸을 따라 넉넉히 잡았다).</summary>
    public const int ChildNameMaxLength = 8;

    /// <summary>
    /// 아직 소개 안 한, 이미 태어난 아이들 — 태어난 차례대로(<c>0x0045FFC0</c>).
    /// </summary>
    public static List<Player.Child> NotIntroduced(Player player) =>
        [.. player.Children.Where(c => !c.Introduced && c.IsBornBy(player.Date)).OrderBy(c => c.Born)];

    /// <summary>
    /// 아이를 소개하는 말(<c>0x00460070</c>, <c>0x00539398</c>·<c>0x005393E8</c>).
    /// </summary>
    public static string IntroductionOf(Player.Child child, DateTime today)
    {
        int age = child.AgeOn(today);
        return age > 0
            ? $"보세요, 당신의 아이에요. 올해 {age}세가 되지요. 이름은 {child.Name}. 신부님이 지어 주셨어요."
            : $"보세요, 당신의 아이에요. 이름은 {child.Name}. 신부님이 지어 주셨어요.";
    }

    /// <summary>이 나이까지는 소개할 때 사건 그림을 함께 낸다(<c>0x0045FFE6</c>).</summary>
    public const int BabyAge = 5;

    /// <summary>그때 세우는 사건 그림(<c>0x00472FA0(8)</c>).</summary>
    public const int BabyStill = 8;

    // ── 딸의 결혼 ────────────────────────────────────────────────────────────

    /// <summary>이야기가 열리는 주사위(<c>0x00460180</c> 의 <c>rand(5) == 0</c>) — 다섯에 하나.</summary>
    public const int MarriageRoll = 5;

    /// <summary>딸이 결혼 이야기를 꺼낼 수 있는 나이(열다섯).</summary>
    public const int MarriageAge = 15;

    /// <summary>이야기가 열리는 데 있어야 하는 저금(만 닢).</summary>
    public const int MarriageSavings = 10000;

    /// <summary>결혼 준비금(<c>0x0047CC00(-10000)</c>).</summary>
    public const int MarriageDowry = 10000;

    /// <summary>
    /// 지금 결혼 이야기를 꺼낼 수 있는 맏딸 — 아내가 있고 저금이 <see cref="MarriageSavings"/>
    /// 이상이며, 그 딸이 <see cref="MarriageAge"/> 이상이라야 한다. 없으면 null.
    /// </summary>
    public static Player.Child? MarriageableDaughter(Player player) =>
        player.Spouse.Length == 0 || player.Savings < MarriageSavings ? null
        : player.Children.Where(c => c.Daughter && c.AgeOn(player.Date) >= MarriageAge)
                         .OrderBy(c => c.Born).FirstOrDefault();

    // ── 집에 돌아왔을 때 나는 사건(0x00460530) ──────────────────────────────

    /// <summary>
    /// 집에 사건이 나는가 — 아내가 있고, <c>rand(100) &lt;= 2</c> 이고, 저금이 만 닢을 넘어야 한다
    /// (<c>0x00460530</c>).
    /// </summary>
    public static bool IncidentDue(Player player, Random random) =>
        player.Spouse.Length > 0 && random.Next(100) <= 2 && player.Savings > MarriageSavings;

    /// <summary>
    /// 사진 쪽인가 동물 쪽인가 — <b>제독의 직업</b>이 가른다(<c>0x00460583</c> 의 점프표 <c>0x004605A8</c>).
    /// </summary>
    /// <remarks>
    /// 탐험가(0)·발굴자(1)는 「서적·유물」(분류 7)을 가져오니 아내가 사진을 팔고, 사냥꾼(2)·
    /// 정복자(3)는 「동물」(분류 8)을 가져오니 구경거리가 된다. 직업이 넷을 넘으면 아무 일도 없다.
    /// </remarks>
    public static bool SellsPhotos(int jobIndex) => jobIndex is 0 or 1;

    /// <summary>사진 사건이 뒤지는 아이템 분류 — 서적·유물(<c>0x0046044A</c>).</summary>
    public const int RelicCategory = 7;

    /// <summary>동물 사건이 뒤지는 아이템 분류 — 동물(<c>0x004602AC</c>).</summary>
    public const int AnimalCategory = 8;

    /// <summary>
    /// 아내의 성미 칸 — 성미 여덟 가운데 <b>일곱째</b>(칸 번호 6, 무신경 0 · 1 · 신경질 2).
    /// </summary>
    /// <remarks>
    /// 동물 사건(<c>0x00460306</c>)과 사진 사건(<c>0x00460471</c>) 둘 다 성미 버퍼 첫머리에서
    /// <c>+0x18</c>, 곧 <b>칸 6</b> 을 본다. 값이 2 이상이면 그냥 벌이가 되고, 1 이면 운을
    /// 굴리고(<c>rand(100) &gt; 운+1</c> 이면 놓친다), 0 이면 굴림 없이 놓친다.
    /// </remarks>
    public const int GreedSlot = 6;

    /// <summary>
    /// 동물을 구경시켜 돈을 벌었는가, 아니면 놓쳤는가(<c>0x00460306</c>).
    /// </summary>
    /// <remarks>
    /// 욕심 칸이 2 면 언제나 구경거리로 삼고, 0 이면 언제나 놓친다. 1 이면
    /// <c>rand(100) &lt;= 운 + 1</c> 일 때만 벌어 온다.
    /// </remarks>
    public static bool ShowsAnimals(int greed, int luck, Random random) =>
        greed >= 2 || (greed > 0 && random.Next(100) <= luck + 1);

    /// <summary>사진을 팔려면 욕심 칸이 <b>1 이상</b>이라야 한다(<c>0x00460471</c>).</summary>
    public static bool SellsRelicPhotos(int greed) => greed >= 1;

    /// <summary>구경삯 — 가진 동물 <b>모두</b>의 매각가를 백으로 나눠 더한다(<c>0x004603CD</c>).</summary>
    public static int ShowFee(int sellPrice) => sellPrice / 100;

    /// <summary>사진 값 — <c>rand(운 + 1) + 매각가 / 300</c>(<c>0x004604E2</c>).</summary>
    public static int PhotoFee(int sellPrice, int luck, Random random) =>
        random.Next(luck + 1) + sellPrice / 300;

    /// <summary>놓친 동물이 물어 오는 악명 — <c>매각가 / 300</c>(<c>0x00460395</c>).</summary>
    public static int RunawayInfamy(int sellPrice) => sellPrice / 300;

    /// <summary>교육 나이 — 열 살부터(<c>0x0046181E</c>).</summary>
    public const int EducateAge = 10;

    /// <summary>
    /// 아버지가 아이보다 높은 것 — 가르칠 수 있는 기능(<c>0x004AC040</c>, 열셋)과 언어(<c>0x004AC090</c>, 열넷).
    /// </summary>
    /// <returns>(기능인가, 칸 번호) 차례 — 기능이 먼저다.</returns>
    public static List<(bool Skill, int Index)> Teachable(Player father, Player.Child child)
    {
        var list = new List<(bool, int)>();
        for (int i = 0; i < Skill.Names.Length && i < child.Skills.Length; i++)
            if (father.LevelOf(Skill.Names[i]) > child.Skills[i]) list.Add((true, i));
        for (int i = 0; i < Skill.Languages.Length && i < child.Tongues.Length; i++)
            if (father.TongueOf(Skill.Languages[i]) > child.Tongues[i]) list.Add((false, i));
        return list;
    }

    /// <summary>
    /// 더 배울 수 있는지(<c>0x004696E0</c> 기능 · <c>0x00469750</c> 언어).
    /// </summary>
    /// <remarks>
    /// 단계마다 무게를 매겨 더한 값이 지력으로 정한 한도 밑이라야 한다.
    /// <code>
    ///   점수 = (3단계 수 × 2 + 2단계 수) × 3 + 1단계 수
    ///   한도 = (지력 × 3 + 3) / 5            ; 지력은 아이 칸 +0x24
    ///   점수 &lt; 한도 라야 배운다
    /// </code>
    /// 언어 쪽(<c>0x00469750</c>)은 같은 꼴로 보고 옮겼다 — 따로 확인하지는 않았다.
    /// </remarks>
    public static bool CanLearnMore(Player.Child child, bool skill)
    {
        var levels = skill ? child.Skills : child.Tongues;
        int ones = levels.Count(l => l == 1), twos = levels.Count(l => l == 2), threes = levels.Count(l => l == 3);
        int score = (threes * 2 + twos) * 3 + ones;
        int mind = child.Abilities.Length > 1 ? child.Abilities[1] : 0;
        return score < (mind * 3 + 3) / 5;
    }

    /// <summary>
    /// 한 단계 가르치는 데 드는 날(<c>0x00461440</c>) — (120 − (지력 + 1)) / 10 × 다음 단계 × 30.
    /// </summary>
    public static int EducateDays(Player.Child child, int nextLevel)
    {
        int mind = child.Abilities.Length > 1 ? child.Abilities[1] : 0;
        return (120 - (mind + 1)) / 10 * nextLevel * 30;
    }

    /// <summary>
    /// 기능을 익힌 아이가 하는 말(<c>0x00461640</c> 의 뜀표) — 기능마다 (2단계, 3단계) 한 쌍이다.
    /// </summary>
    public static readonly (string Two, string Three)[] SkillRemarks =
    [
        ("이제 항해술은 완벽해! 빨리 바다로 나가고 싶군.", "항해술은 이제 됐으니 빨리 아버지 배에 태워줘요."),
        ("탐험 수칙인건 알겠지만, 걷는건 싫군.", "괜찮아! 숲도 사막도 위험하니까, 주위를 주의하면 되는 거죠?"),
        ("상대방이 상단공격을 하면 웅크리면 되죠?", "솜씨가 많이 늘었다! 워낙 칼싸움을 좋아하거든."),
        ("흠-, 대포도 여러 종류가 있군요.", "대포는 화약을 조심해야 하죠? 괜찮아요."),
        ("잘 보세요. 저 돌을 맞출테니…! 아, 빗나갔다.", "이것이 화승총이고, 이쪽이 머스켓총. 다 알았어요."),
        ("항해중엔 영양부족이 되니, 보리를 먹어야 되는거군···", "흠, 응급처지는 이렇게 하는 거구나. 이러면 다쳐도 걱정 없네요."),
        ("과연 상대방의 마음을 꿰뚫어 보는군. 그럼 이것으로 교섭이라면 걱정없어요.", "조리있게 말하는 건 어렵구나···"),
        ("멀리 있는 것과의 거리를 잴 때는···인지를 세워서···", "육분의나 나침반을 쓰는 방법은 다 이해했어요. 다음은 해보는 일만 남았군요."),
        ("역사란 재미있군. 옛날 세계를 한번 보고 싶네.", "나도 역사에 이름을 남길 수 있는 위대한 인물이 되고 싶어."),
        ("돈을 많이 모아서 어머니에게 큰 집을 지어 드릴께요.", "무역의 비결은···시세와 특산품에 있지요!"),
        ("아무때나 배가 고장나도 걱정없어요!", "완벽하게 수리했어요. 조선소 아저씨 못지 않아요."),
        ("성경책을 다 외웠어요. 옛? 암송해 보라고요? 내, 내일 할께요.", "신학은 심오하군요. 터득하려면 열심히 공부해야겠어요."),
        ("실험은 재미있군요! 더 가르쳐 줘요.", "좀 알것 같아요. 앞으로는 과학의 시대가 되겠지요!"),
    ];

    /// <summary>세대교체 나이 — 열여덟부터(<c>0x00461AF4</c>).</summary>
    public const int SucceedAge = 18;

    /// <summary>
    /// 뒤를 이을 아들 — 성별 0 가운데 가장 나이 많은 것(<c>0x004AB790(0, 0)</c>). 없으면 null.
    /// </summary>
    /// <remarks>
    /// <b>이미 태어난</b> 아이만 센다 — 원본은 나이 칸이 0 이상인 아이만 집으므로(<c>0x004AB7B7</c>),
    /// 배 속에 있는 열 달 동안은 없는 것과 같다.
    /// </remarks>
    public static Player.Child? EldestSon(Player player) =>
        player.Children.Where(c => !c.Daughter && c.IsBornBy(player.Date))
                       .OrderBy(c => c.Born).FirstOrDefault();

    /// <summary>이미 태어난 아이가 하나라도 있는지 — 「교육」 줄의 조건이다(<c>0x004AB8C0(0)</c>).</summary>
    public static bool HasBornChild(Player player) =>
        player.Children.Any(c => c.IsBornBy(player.Date));

    /// <summary>물려받는 명성(<c>0x00461B66</c>) — 3000 밑 0 · 6000 밑 1/5 · 그 위 1/5 + 1000.</summary>
    public static int InheritedFame(int fame) => fame < 3000 ? 0 : fame < 6000 ? fame / 5 : fame / 5 + 1000;

    /// <summary>물려받는 악명(<c>0x00461B9E</c>) — 2000 밑 0 · 5000 밑 1/8 · 그 위 1/8 + 1000.</summary>
    public static int InheritedInfamy(int infamy) => infamy < 2000 ? 0 : infamy < 5000 ? infamy / 8 : infamy / 8 + 1000;
}
