namespace CdsHelper.Support.Local.Models;

/// <summary>
/// 주인공의 직업 — 능력치가 어느 쪽으로 기우는지와, 처음에 든 기술을 정한다.
/// </summary>
/// <param name="Name">이름.</param>
/// <param name="Bias">능력치 보정(체력·지력·무력·매력·운·신앙심 차례).</param>
/// <param name="Skills">처음부터 든 기술 — (기술 번호, 자리).</param>
/// <param name="Tongues">처음부터 든 언어 — (언어 번호, 자리). 국적이 주는 것과는 딴 몫이다.</param>
/// <remarks>
/// 이름은 게임 표(<c>0x00560AA8</c>) 여덟 그대로다. 능력치 보정은
/// <c>0x0051ACA0</c>(직업마다 32바이트 = int 여덟)에서 읽은 값이다 — 탐험가가 온통 0 인
/// <b>밑자리</b>고 나머지가 거기서 기운다.
/// <code>
///   탐험가  0  0  0  0  0  0  0  0
///   발굴자  2 -3 -2  5 -2  0  1  1
///   사냥꾼  2 -3  2 -3  2  0  7 -1
///   정복자 -2  3 -2  3 -2  0  3 -1
///   해적   -2 -4  5 -1  2  0  0  1
///   전도사 -2  4 -3 -1  2  0  1 -1
///   상인   -3  2  4 -2 -1  0  3  1
///   군인    2  2  2 -5  2  0  2 -1
/// </code>
/// <b>처음 든 기술과 언어는 화면에서 옮겼다.</b> 고를 수 있는 넷을 한 번씩 만들어
/// 기능 창을 맞대어 보고 얻은 것이다 — EXE 에서는 이 표를 아직 못 짚었다.
/// <code>
///   탐험가  항해술2 측량2 회계2            + 로망스어3
///   발굴자  웅변2 측량2 역사학2            + 슬라브·그리스어3
///   사냥꾼  운용술3 사격술2 의학2 과학2
///   정복자  검술3 포술2 조선기술2 신학2
/// </code>
/// <b>넷 다 합이 9점</b>이다 — 기술만 아홉을 받거나, 여섯에 언어 셋을 얹는다. 언어를
/// 받는 둘은 그만큼 기술이 적다.
///
/// 국적이 주는 <b>모국어 3 · 이웃 2</b> 는 여기 없다(<see cref="Skill.TongueOf"/>).
///
/// 뒤의 넷(해적·전도사·상인·군인)은 새 놀이에서 못 고르므로 아직 <b>지어 둔 값</b>이다.
/// </remarks>
public sealed record Job(string Name, int[] Bias, (int Skill, int Level)[] Skills,
                         (int Language, int Level)[] Tongues)
{
    /// <summary>새 놀이에서 고를 수 있는 직업 넷. 게임 화면에도 이 넷만 뜬다.</summary>
    public const int Choosable = 4;

    /// <summary>여덟. 앞의 넷만 새 놀이에서 고른다.</summary>
    public static readonly Job[] All =
    [
        new("탐험가", [0, 0, 0, 0, 0, 0],
            [(Skill.Sailing, 2), (Skill.Survey, 2), (Skill.Accounting, 2)],
            [(Skill.Romance, 3)]),
        new("발굴자", [2, -3, -2, 5, -2, 0],
            [(Skill.Rhetoric, 2), (Skill.Survey, 2), (Skill.History, 2)],
            [(Skill.SlavGreek, 3)]),
        new("사냥꾼", [2, -3, 2, -3, 2, 0],
            [(Skill.Handling, 3), (Skill.Shooting, 2), (Skill.Medicine, 2), (Skill.Science, 2)],
            []),
        new("정복자", [-2, 3, -2, 3, -2, 0],
            [(Skill.Sword, 3), (Skill.Gunnery, 2), (Skill.Shipwright, 2), (Skill.Theology, 2)],
            []),

        // 아래 넷은 새 놀이에서 못 고른다 — 값은 아직 지어 둔 것이다.
        new("해적", [-2, -4, 5, -1, 2, 0],
            [(Skill.Sword, 3), (Skill.Gunnery, 2), (Skill.Handling, 1)], []),
        new("전도사", [-2, 4, -3, -1, 2, 0],
            [(Skill.Theology, 3), (Skill.Medicine, 2), (Skill.Rhetoric, 1)], []),
        new("상인", [-3, 2, 4, -2, -1, 0],
            [(Skill.Accounting, 3), (Skill.Rhetoric, 2), (Skill.Sailing, 1)], []),
        new("군인", [2, 2, 2, -5, 2, 0],
            [(Skill.Gunnery, 3), (Skill.Sword, 2), (Skill.Handling, 1)], []),
    ];

    /// <summary>번호로 찾는다. 표 밖이면 탐험가.</summary>
    public static Job Of(int index) =>
        index >= 0 && index < All.Length ? All[index] : All[0];
}

/// <summary>
/// 능력치 여섯 — 게임 표(<c>0x00560A88</c>) 차례 그대로다.
/// </summary>
/// <remarks>
/// 새 놀이 화면에는 앞의 다섯만 뜬다. <b>신앙심</b>은 안 보이는 자리에서 따로 굴린다
/// (<c>0x0045D590</c> 의 <c>rand(80) + 21</c>).
/// </remarks>
public static class Ability
{
    /// <summary>이름 여섯.</summary>
    public static readonly string[] Names = ["체력", "지력", "무력", "매력", "운", "신앙심"];

    /// <summary>번호.</summary>
    public const int Body = 0, Mind = 1, Might = 2, Charm = 3, Luck = 4, Faith = 5;

    /// <summary>화면에 뜨는 수(신앙심은 안 뜬다).</summary>
    public const int Shown = 5;

    /// <summary>능력치가 들 수 있는 폭(<c>0x0045D576</c> 의 <c>clamp(값, 20, 100)</c>).</summary>
    public const int Min = 20, Max = 100;

    /// <summary>
    /// <b>담는 값은 보이는 값에서 1 을 뺀 것</b>이다(<c>0x005B60C0</c>).
    /// </summary>
    /// <remarks>
    /// NEW GAME 이 굴린 값을 넣을 때 <c>dec</c> 하고(<c>0x0045E47A</c>), 보여 줄 때 도로
    /// 1 을 더한다(<c>0x0046D76C</c> 의 「체  력/%4d」). 능력치를 올리고 내리는 자리도
    /// 보이는 값 1~100 으로 자른 뒤 다시 1 을 뺀다(<c>0x00432C50</c>).
    /// </remarks>
    public static int Display(int stored) => stored + 1;

    /// <summary>보이는 값을 담는 값으로 되돌린다.</summary>
    public static int Store(int shown) => shown - 1;

    /// <summary>능력치를 굴릴 때 얹는 밑값(<c>add $0x32</c>).</summary>
    public const int Base = 50;

    /// <summary>신앙심을 굴리는 폭(<c>rand(80) + 21</c>).</summary>
    public const int FaithRoll = 80, FaithBase = 21;

    /// <summary>
    /// 나이대 보정. 게임은 스택에 세 벌(여섯씩 열여덟 칸)을 깔고 나이로 고른다.
    /// </summary>
    /// <remarks>
    /// <code>
    /// 45d49e  [S+0x1c]=20  [S+0x20]=-10 [S+0x24]=20  [S+0x28]=-20 [S+0x2c]=0 [S+0x30]=0
    /// 45d4aa  [S+0x34]=0   …                                                 [S+0x48]=0
    /// 45d4c2  [S+0x4c]=-20 [S+0x50]=20  [S+0x54]=-20 [S+0x58]=20  [S+0x5c]=0 [S+0x60]=0
    /// 45d4e5  ecx = (나이 >= 36) + (나이 >= 26)          ; 벌 번호
    /// 45d50f  ebx = S+0x1c + 벌*24                       ; 그 벌의 첫 칸
    /// </code>
    /// <b>젊으면 몸이 세고 늙으면 머리가 선다</b> — 스물다섯까지는 체력·무력이 +20,
    /// 지력 -10 에 매력 -20 이고, 서른여섯부터는 그게 그대로 뒤집힌다. 스물여섯부터
    /// 서른다섯까지는 보정이 없다. 운은 어느 벌에서도 안 건드린다.
    /// </remarks>
    public static readonly int[][] AgeBias =
    [
        [20, -10, 20, -20, 0, 0],   // 26살 밑
        [0, 0, 0, 0, 0, 0],         // 26~35
        [-20, 20, -20, 20, 0, 0],   // 36살 위
    ];

    /// <summary>나이가 어느 벌에 드는지(<c>cmp $0x1A</c> · <c>cmp $0x24</c>).</summary>
    public static int TierOf(int age) => age >= 36 ? 2 : age >= 26 ? 1 : 0;

    /// <summary>
    /// 생일 보정 — <b>이레만</b> 값이 있고 나머지 날은 다 0 이다.
    /// </summary>
    /// <remarks>
    /// 표는 <c>0x005472C0</c> 이고 32바이트 여덟 칸짜리 줄이다. 앞 둘이 <b>월·일</b>,
    /// 뒤 다섯이 체력·지력·무력·매력·운 보정이다. 찾는 것은 <c>0x0042E670</c> 인데
    /// 월과 일이 둘 다 맞는 줄을 찾을 때까지 훑고, 못 찾으면 <c>-1, -1</c> 로 끝나는
    /// 마감 줄을 그대로 낸다 — 그 줄의 보정이 다 0 이라 안 걸린 날은 0 이 된다.
    /// <code>
    /// 42e670  cmpl $-1,0x5472c0        ; 표 첫 줄
    /// 42e686  cmp %ecx,(%eax)          ; 월
    /// 42e68a  cmp %edx,0x4(%eax)       ; 일
    /// 42e68f  add $0x20,%eax           ; 다음 줄
    /// 45d568  mov (%ecx),%ecx          ; 줄+8 부터 다섯을 차례로 읽는다
    /// </code>
    /// 밸런타인·만우절·칠석·크리스마스 이브가 들어 있는 것을 보면 <b>말장난</b>이다.
    /// </remarks>
    public static readonly (int Month, int Day, int[] Bias)[] Birthdays =
    [
        (2, 14, [0, 0, -5, 10, 0]),     // 밸런타인 — 매력이 는다
        (4, 1, [0, 10, 0, -5, 0]),      // 만우절 — 머리가 는다
        (7, 7, [0, -10, 0, 5, 10]),     // 칠석 — 운이 는다
        (10, 10, [10, -10, 5, 0, 0]),
        (12, 24, [0, 0, 0, 5, 5]),      // 크리스마스 이브
        (1, 3, [-5, 5, 0, 5, 0]),
        (8, 11, [0, 10, -5, 0, 0]),
    ];

    /// <summary>그 생일의 보정. 이레에 안 들면 다 0 이다.</summary>
    public static int[] BirthBias(int month, int day)
    {
        foreach (var (m, d, bias) in Birthdays)
            if (m == month && d == day) return bias;
        return [0, 0, 0, 0, 0];
    }

    /// <summary>
    /// 시작 컨디션 — <b>굴린 체력</b>에서 나온다(<c>0x0045D5A3</c>, 보너스를 얹기 전 값).
    /// </summary>
    /// <remarks>
    /// <code>
    /// 45d5a3  eax = 체력
    /// 45d5b3  eax *= 20
    /// 45d5bd  0x0049E540(eax, 10, 2000)   → [+0x13C] → 마무리가 0x005B60D8 로 옮긴다
    /// </code>
    /// 예전에는 이것을 소지금으로 읽었다 — 소지금은 <see cref="GoldRoll"/> 이다.
    /// </remarks>
    public static int ConditionFor(int body) => Math.Clamp(body * 20, 10, 2000);

    /// <summary>나이가 이보다 많으면 돈·명성이 더 붙는다(<c>0x0045D640</c> 의 <c>cmp 0x23</c>).</summary>
    public const int SeniorAge = 35;

    /// <summary>시작 소지금 — <c>rand(1000) + 1000</c>, 나이 &gt; 35 면 <c>rand(500) + 1500</c> 더(<c>0x0045D628</c>).</summary>
    public static int GoldRoll(int age, Random rng) =>
        rng.Next(1000) + 1000 + (age > SeniorAge ? rng.Next(500) + 1500 : 0);

    /// <summary>시작 명성 — <c>rand(500) + 500</c>, 나이 &gt; 35 면 1000 더(<c>0x0045D667</c>).</summary>
    public static int FameRoll(int age, Random rng) =>
        rng.Next(500) + 500 + (age > SeniorAge ? 1000 : 0);

    /// <summary>시작 악명 — 나이 &gt; 35 면 <c>rand(500)</c>, 아니면 0(<c>0x0045D69C</c> → 0x005B6150).</summary>
    public static int InfamyRoll(int age, Random rng) => age > SeniorAge ? rng.Next(500) : 0;

    /// <summary>
    /// 능력치 다섯과 신앙심을 굴린다.
    /// </summary>
    /// <remarks>
    /// <code>
    /// 45d55c  rand(나이)                                    ; 0 ~ 나이-1
    /// 45d568  값 = 생일보정[i] + rand(나이) + 직업보정[i] + 나이보정[i] + 50
    /// 45d576  0x0049E540(값, 20, 100)                       ; 잘라 넣는다
    /// 45d590  신앙심 = rand(80) + 21
    /// </code>
    /// <b>나이가 두 번 먹힌다</b> — 굴리는 폭이 곧 나이라(<c>rand(나이)</c>) 늙을수록
    /// 평균이 통째로 올라가고, 그 위에 나이대 보정이 또 얹힌다. 열다섯이면 평균 +7,
    /// 마흔이면 평균 +19.5 다. 그래서 <b>늙게 잡을수록 능력치가 세다</b> — 대신 잘
    /// 굴린 만큼 보너스 포인트를 덜 준다(<see cref="BonusFor"/>).
    /// </remarks>
    /// <summary>
    /// 굴림 보정 열여섯 줄(<c>0x0051ACA0</c>, 한 줄 32바이트의 앞 여섯) — <b>얼굴 자리</b>로 고른다(<c>0x0045D473</c> 가
    /// <c>[+0xBC]</c> = 1단계에서 고른 초상화 번호를 줄로 쓴다). 직업이 아니다.
    /// </summary>
    public static readonly int[][] FaceBias =
    [
        [0, 0, 0, 0, 0, 0], [2, -3, -2, 5, -2, 0], [2, -3, 2, -3, 2, 0], [-2, 3, -2, 3, -2, 0],
        [-2, -4, 5, -1, 2, 0], [-2, 4, -3, -1, 2, 0], [-3, 2, 4, -2, -1, 0], [2, 2, 2, -5, 2, 0],
        [-5, -1, -1, 4, 3, 0], [5, -4, 4, 1, -5, 0], [3, 5, -3, -3, -1, 0], [2, 0, -2, -3, 3, 0],
        [4, -2, 3, -4, 0, 0], [-3, -1, 4, -3, 3, 0], [1, -3, -3, 0, 5, 0], [2, 2, 2, -3, -3, 0],
    ];

    /// <summary>그 얼굴 자리의 보정 줄. 표 밖이면 0 줄이다.</summary>
    public static int[] BiasOf(int face) => face >= 0 && face < FaceBias.Length ? FaceBias[face] : FaceBias[0];

    public static int[] Roll(int[] bias, int age, int month, int day, Random rng)
    {
        var tier = AgeBias[TierOf(age)];
        var born = BirthBias(month, day);
        var stats = new int[Names.Length];

        for (int i = 0; i < Shown; i++)
            stats[i] = Math.Clamp(Base + born[i] + rng.Next(Math.Max(1, age))
                                  + bias[i] + tier[i], Min, Max);

        stats[Faith] = rng.Next(FaithRoll) + FaithBase;
        return stats;
    }

    /// <summary>
    /// 능력치를 굴린 뒤 남는 보너스 포인트.
    /// </summary>
    /// <remarks>
    /// 다섯을 더한 값으로 갈린다(<c>0x0045D5D5</c>). <b>잘 굴렸을수록 덜 준다.</b>
    /// <code>
    ///   합 &gt;= 451  rand(6)
    ///   합 &gt;= 351  rand(11) + 5
    ///   합 &gt;= 251  rand(11) + 10
    ///   그 밖      rand(11) + 20
    /// </code>
    /// </remarks>
    public static int BonusFor(int[] stats, Random rng)
    {
        int sum = 0;
        for (int i = 0; i < Shown; i++) sum += stats[i];

        return sum >= 451 ? rng.Next(6)
             : sum >= 351 ? rng.Next(11) + 5
             : sum >= 251 ? rng.Next(11) + 10
             : rng.Next(11) + 20;
    }
}
