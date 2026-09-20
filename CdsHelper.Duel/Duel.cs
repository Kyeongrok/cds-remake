namespace CdsHelper.Duel;

/// <summary>
/// 「일기토」 — 해전에서 기함끼리 붙었을 때 벌이는 일대일 결투.
/// </summary>
/// <remarks>
/// 껍데기는 게임의 <c>0x004AA700</c>, 판을 까는 데는 <c>0x004A8500</c>, 한 수를
/// 가리는 데는 <c>0x004A98E0</c> 다. 몸을 <b>상·중·하 세 자리</b>로 나누고 자리마다
/// 체력 막대를 따로 두는데, 어느 하나가 0 이 되면 그 사람이 진다
/// (<c>0x004A9BD4</c> 가 <c>[0xE4] = 2</c> 로 판을 닫는다).
///
/// 자세한 것은 분석 글 「48.분석-일기토」에 모아 두었다.
/// </remarks>
public sealed class Duel
{
    /// <summary>몸의 자리 셋 — 상·중·하.</summary>
    public const int Zones = 3;

    /// <summary>막대 눈금의 끝(<c>0x004A8D65</c> 의 <c>mov $0x64</c>).</summary>
    public const int Full = 100;

    /// <summary>자리 이름. 차림표 글은 <c>0x00533DA8</c> 벌이다.</summary>
    public static readonly string[] ZoneNames = ["상단", "중단", "하단"];

    /// <summary>막는 몸짓 이름(<c>0x00533E08</c> 벌).</summary>
    public static readonly string[] GuardNames = ["뛴다", "피한다", "웅크린다"];

    /// <summary>몸짓 벌 수 — <c>FIGHTER.CDS</c> 의 파트 0·2·4…16 아홉 벌이다.</summary>
    public const int Kits = 9;

    /// <summary>싸우는 이 하나.</summary>
    /// <remarks>
    /// 값을 담는 자리는 <c>0x004A87AA</c> 벌이다 — 내 것이 <c>[0x160]</c> 부터,
    /// 적 것이 <c>[0x164]</c> 부터 4바이트씩 벌어져 나란히 놓인다.
    /// </remarks>
    public sealed class Fighter
    {
        public Fighter(string name, int body, int might, int sword, int luck,
                       int weapon, int armour, int face = -1, int kit = 0)
        {
            Name = name;
            Face = face;
            Kit = Math.Clamp(kit, 0, Kits - 1);
            Might = might;
            Sword = sword;
            Luck = luck;
            Weapon = weapon;
            Armour = armour;

            int start = Math.Clamp(body, 1, Full);
            for (int zone = 0; zone < Zones; zone++) Health[zone] = start;
        }

        public string Name { get; }

        /// <summary>얼굴 번호(<see cref="Local.Helpers.Portraits"/>). 모르면 -1 이다.</summary>
        public int Face { get; }

        /// <summary>
        /// 몸짓 벌(0~8) — <c>FIGHTER.CDS</c> 의 아홉 벌 가운데 어느 것으로 그릴지다.
        /// </summary>
        /// <remarks>
        /// <b>게임이 어느 칸을 보고 고르는지는 아직 못 짚었다.</b> 그리는 자리
        /// (<c>0x004A7104</c>)가 인물 객체를 꺼내 쓰는 것까지는 봤으니 그 안 어딘가다.
        /// 그때까지는 부르는 쪽이 넣어 준다.
        /// </remarks>
        public int Kit { get; }

        /// <summary>무력(<c>[0x170]</c>·<c>[0x174]</c>). 인물 <c>+0x28</c> 에 1 을 더한 것.</summary>
        public int Might { get; }

        /// <summary>검술(<c>[0x178]</c>·<c>[0x17C]</c>). 인물 <c>+0x48</c>.</summary>
        public int Sword { get; }

        /// <summary>넷째 능력(<c>[0x180]</c>·<c>[0x184]</c>). 회심의 일격에 쓴다.</summary>
        public int Luck { get; }

        /// <summary>가진 무기의 위력(<c>[0x188]</c>·<c>[0x18C]</c>).</summary>
        public int Weapon { get; }

        /// <summary>가진 방어구의 위력(<c>[0x190]</c>·<c>[0x194]</c>).</summary>
        public int Armour { get; }

        /// <summary>상·중·하 세 자리의 체력.</summary>
        public int[] Health { get; } = new int[Zones];

        /// <summary>필살을 이미 썼나(<c>[0xAC]</c>·<c>[0xB0]</c>). 한 판에 한 번뿐이다.</summary>
        public bool SpentBlow { get; internal set; }

        /// <summary>어느 자리든 0 이 되면 진다.</summary>
        public bool Down => Health.Any(h => h <= 0);

        /// <summary>가장 얇은 자리. 적이 노리고 또 지키는 곳이다.</summary>
        public int Thinnest
        {
            get
            {
                int at = 0;
                for (int zone = 1; zone < Zones; zone++)
                    if (Health[zone] < Health[at]) at = zone;
                return at;
            }
        }
    }

    /// <summary>지금 무엇을 고를 차례인지(<c>[0xCC]</c>).</summary>
    public enum Step
    {
        /// <summary>선제 겨루기 — 서로 자리를 골라 가위바위보를 한다.</summary>
        First = 0,

        /// <summary>내가 친다 — 자리를 고르고, 필살을 쓸 수 있다.</summary>
        Strike = 1,

        /// <summary>적이 친다 — 막는 몸짓을 고른다.</summary>
        Guard = 2,
    }

    private readonly Random _rng;

    public Duel(Fighter mine, Fighter theirs, Random rng)
    {
        Mine = mine;
        Theirs = theirs;
        _rng = rng;
    }

    public Fighter Mine { get; }

    public Fighter Theirs { get; }

    /// <summary>지금 차례(<c>[0xCC]</c>).</summary>
    public Step Now { get; private set; } = Step.First;

    /// <summary>몇 수 주고받았는지.</summary>
    public int Moves { get; private set; }

    /// <summary>끝났으면 이겼는지. 아직이면 null.</summary>
    public bool? Over { get; private set; }

    /// <summary>바로 앞 수의 이야기. 화면이 그대로 적는다.</summary>
    public string Line { get; private set; } = "";

    /// <summary>바로 앞 수가 회심의 일격이었나(<c>[0x13C]</c>).</summary>
    public bool Telling { get; private set; }

    /// <summary>
    /// 바로 앞 수에 <b>지은 몸짓</b> — 화면이 그림을 고를 때 쓴다.
    /// </summary>
    /// <remarks>
    /// <c>0~2</c> 가 치는 자리(상·중·하), <c>3~5</c> 가 막는 몸짓(뛴다·피한다·웅크린다)이다.
    /// 아직 한 수도 안 두었으면 −1 이다.
    /// </remarks>
    public int MyPose { get; private set; } = -1;

    public int TheirPose { get; private set; } = -1;

    /// <summary>막는 몸짓을 몸짓 번호로 — 치는 자리 셋 뒤에 붙는다.</summary>
    private const int GuardPose = Zones;

    /// <summary>필살을 고를 수 있나 — 내가 치는 차례이고 아직 안 썼을 때.</summary>
    public bool CanBlow => Over == null && Now == Step.Strike && !Mine.SpentBlow;

    /// <summary>
    /// 한 수 둔다.
    /// </summary>
    /// <remarks>
    /// 게임의 <c>0x004A9610</c>(수 고르기)과 <c>0x004A98E0</c>(가리기),
    /// <c>0x004A6E9E</c>(차례 넘기기)를 한 자리에 모은 것이다.
    /// </remarks>
    /// <param name="pick">
    /// <see cref="Step.First"/>·<see cref="Step.Strike"/> 면 칠 자리(0~2),
    /// <see cref="Step.Guard"/> 면 막는 몸짓(0~2).
    /// </param>
    /// <param name="blow">필살로 치나. <see cref="CanBlow"/> 일 때만 먹는다.</param>
    public void Play(int pick, bool blow = false)
    {
        if (Over != null) return;

        pick = Math.Clamp(pick, 0, Zones - 1);
        Telling = false;
        Moves++;

        if (blow && CanBlow) Mine.SpentBlow = true;
        else blow = false;

        switch (Now)
        {
            case Step.First: FirstBlood(pick); break;
            case Step.Strike: IStrike(pick, blow); break;
            default: TheyStrike(pick); break;
        }

        if (Mine.Down) Over = false;
        else if (Theirs.Down) Over = true;
    }

    /// <summary>
    /// 선제 겨루기 — 상 &gt; 중 &gt; 하 &gt; 상 의 가위바위보.
    /// </summary>
    /// <remarks>
    /// 뜀표는 <c>0x004A9E30</c> 이고 <c>내 - 적 + 2</c> 로 뛴다. 같은 자리면
    /// <c>0x004A9953</c> 이 <c>무력 + 검술*30 + rand(11)</c> 을 굴려 가르는데,
    /// <b>비기면 다시 굴린다</b>.
    /// </remarks>
    private void FirstBlood(int mine)
    {
        int theirs = _rng.Next(Zones);                 // 0x004A9650 rand(3)
        bool won;

        if (mine == theirs)
        {
            int me, you;
            do
            {
                me = Mine.Might + Mine.Sword * 30 + _rng.Next(11);
                you = Theirs.Might + Theirs.Sword * 30 + _rng.Next(11);
            }
            while (me == you);
            won = me > you;
        }
        else
        {
            // 뜀표에서 이기는 것은 차가 -1 과 +2 인 자리다.
            won = (mine + 1) % Zones == theirs;
        }

        MyPose = mine;
        TheirPose = theirs;
        Line = $"내 {ZoneNames[mine]}, 상대 {ZoneNames[theirs]} : " +
               (won ? "내가 앞섰다!" : "상대에게 앞을 내주었다.");
        Now = won ? Step.Strike : Step.Guard;
    }

    /// <summary>내가 친다 — 적은 제 가장 얇은 자리를 막으려 든다.</summary>
    private void IStrike(int zone, bool blow)
    {
        int guard = GuardFor(Theirs.Thinnest);          // 0x004A9665
        int grade = Grade(zone, guard);                 // 4 정통 · 3 스침 · 2 막힘

        int hurt = grade == 2 ? 0 : Hurt(Mine, Theirs, blow, grade == 3);
        if (hurt > 0) Wound(Theirs, zone, hurt);

        MyPose = zone;
        TheirPose = GuardPose + guard;
        Line = $"{ZoneNames[zone]}{(blow ? "필살" : "공격")} vs {GuardNames[guard]} : " +
               Tale(grade, hurt, "상대");
        Pass(grade);
    }

    /// <summary>적이 친다 — 내 가장 얇은 자리를 노린다.</summary>
    /// <remarks>
    /// <c>0x004A97A5</c> 가 <b>내</b> 눈금 <c>[0xB4]·[0xB8]·[0xBC]</c> 를 견주어
    /// 얇은 자리를 짚고, <c>0x004A9868</c> 이 <c>rand(10)</c> 으로 60% 는 그대로
    /// 노리고 40% 는 아무 데나 친다.
    /// </remarks>
    private void TheyStrike(int guard)
    {
        int zone = Mine.Thinnest;                       // 0x004A97A5
        bool blow = false;

        if (_rng.Next(10) < 6)
        {
            if (!Theirs.SpentBlow && _rng.Next(2) == 1)
            {
                Theirs.SpentBlow = true;
                blow = true;
            }
        }
        else
        {
            zone = _rng.Next(Zones);
        }

        int grade = Grade(zone, guard);
        int hurt = grade == 2 ? 0 : Hurt(Theirs, Mine, blow, grade == 3);
        if (hurt > 0) Wound(Mine, zone, hurt);

        MyPose = GuardPose + guard;
        TheirPose = zone;
        Line = $"상대 {ZoneNames[zone]}{(blow ? "필살" : "공격")} vs {GuardNames[guard]} : " +
               Tale(grade, hurt, "나");
        Pass(grade);
    }

    /// <summary>자리 <paramref name="zone"/> 을 가장 잘 막는 몸짓은 <c>2 - zone</c> 이다.</summary>
    /// <remarks>
    /// <c>0x004A9719</c> 은 늘 그것을 고르지는 않는다 — <c>rand(10)</c> 이
    /// 0~4(50%)면 가장 잘 막는 몸짓, 5~8(40%)이면 그 다음, 9(10%)면 가장 못 막는
    /// 몸짓이다.
    /// </remarks>
    private int GuardFor(int zone)
    {
        int roll = _rng.Next(10);
        int off = roll < 5 ? 0 : roll < 9 ? 1 : 2;

        // 등급이 2·3·4 로 오르는 차례대로 늘어놓고 off 번째를 집는다.
        var order = Enumerable.Range(0, Zones).OrderBy(guard => Grade(zone, guard)).ToArray();
        return order[off];
    }

    /// <summary>
    /// 자리와 몸짓의 짝 — 4 정통, 3 스침, 2 막힘.
    /// </summary>
    /// <remarks>
    /// <c>0x004A99BA</c> 가 낸 표다. 상단은 뛰면 정통·웅크리면 막히고, 중단은
    /// 웅크리면 정통·피하면 막히고, 하단은 피하면 정통·뛰면 막힌다.
    /// </remarks>
    public static int Grade(int zone, int guard) => zone switch
    {
        0 => 4 - guard,
        1 => guard == 0 ? 3 : guard * 2,
        _ => guard == 0 ? 2 : 5 - guard,
    };

    /// <summary>
    /// 피해를 셈한다(<c>0x004A9AC2</c> 와 그 짝 <c>0x004A9C74</c>).
    /// </summary>
    private int Hurt(Fighter hits, Fighter takes, bool blow, bool graze)
    {
        int hurt = (hits.Sword - takes.Sword) * 10
                   - takes.Might / 5
                   + hits.Might / 5
                   - takes.Armour
                   + hits.Weapon;

        hurt = hurt < 0
            ? _rng.Next(4) + _rng.Next(6) + 10
            : hurt + _rng.Next(6) + 10;

        if (blow)
        {
            hurt *= 2;                                  // 0x004A9B4E
        }
        else if (hits.Sword + hits.Luck / 10 >= _rng.Next(1000))
        {
            Telling = true;                             // 0x004A9B84 회심의 일격
            hurt = hurt * 3 / 2;
        }

        if (graze) hurt /= 2;                           // 0x004A9B9D 스침은 절반
        return hurt;
    }

    private static void Wound(Fighter who, int zone, int hurt) =>
        who.Health[zone] = Math.Max(0, who.Health[zone] - hurt);

    /// <summary>
    /// 차례를 넘긴다(<c>0x004A6E9E</c>) — <b>정통으로 맞히면 같은 쪽이 이어 친다</b>.
    /// </summary>
    /// <remarks>
    /// 게임은 «<c>[0xD0]</c> 이 0 이거나 4 면 그대로»라고 적혀 있는데, 눈금이
    /// 내가 칠 때는 2·3·4 이고 적이 칠 때는 2·1·0 으로 뒤집혀 있다. 두 쪽 다
    /// <b>정통</b>일 때만 안 넘긴다는 뜻이다.
    /// </remarks>
    private void Pass(int grade)
    {
        if (grade == 4) return;
        Now = Now == Step.Strike ? Step.Guard : Step.Strike;
    }

    private string Tale(int grade, int hurt, string who) => grade switch
    {
        2 => "막혔다.",
        3 => $"스쳤다. {who} -{hurt}",
        _ => (Telling ? "회심의 일격!" : "정통으로 맞혔다!") + $" {who} -{hurt}",
    };
}
