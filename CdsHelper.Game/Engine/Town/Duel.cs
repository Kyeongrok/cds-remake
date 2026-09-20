namespace CdsHelper.Game.Engine.Town;

/// <summary>
/// 일기토 — 술집에서 이름난 항해자에게 칼을 겨루는 판.
/// </summary>
/// <remarks>
/// 게임 자리는 이렇다.
/// <code>
///   0x004A4AA0  말을 건 뒤의 세 줄 — 정보를 듣는다 · 일기토를 신청한다 · 떠난다
///   0x004A48A0  신청을 받은 뒤 — 도망 잡기, 그리고 판 열기
///   0x004A2D80  판 열기(상대 번호, 종류, -1) — 술집이면 무대가 문화권에 따라 4·5·6
///   0x004A8500  판 짓기 — 부위 체력·능력치·무기·방어구를 여기서 담는다
///   0x004A9610  이번 판의 명령을 고른다(내 것과 상대 것)
///   0x004A98E0  맞았는지 막았는지 가리고 아픈 만큼 깎는다
///   0x004A6E9E  다음 판이 공격인지 방어인지 가른다
///   0x004A9E50  판이 끝난 뒤 — 도망·용서·죽음, 그리고 체력 깎기
/// </code>
///
/// <b>부위가 셋이다.</b> 상·중·하 각각이 <b>체력+1</b> 을 따로 가지고, <b>한 군데만
/// 뚫려도 진다</b>. 그래서 같은 곳만 노리는 것이 빠르고, 상대도 제 약한 곳을 감싼다.
///
/// 명령은 공격 셋(상·중·하)과 막기 셋(뛴다·피한다·웅크린다)이다. <b>공격 a 는 막기
/// 2-a 가 막는다</b> — 머리를 노리면 웅크려 피하고, 발을 노리면 뛰어 피한다.
/// </remarks>
public sealed class Duel
{
    /// <summary>공격 줄 셋. 막기도 셋이라 같은 수를 쓴다.</summary>
    public const int Lines = 3;

    /// <summary>공격 <paramref name="line"/> 을 막는 막기.</summary>
    public static int GuardFor(int line) => Lines - 1 - line;

    /// <summary>공격 이름과 막기 이름(<c>0x00533DA8</c> 벌).</summary>
    public static readonly string[] Attacks = ["상단공격", "중단공격", "하단공격"];
    public static readonly string[] Finishers = ["상단필살", "중단필살", "하단필살"];
    public static readonly string[] Guards = ["뛴다", "피한다", "웅크린다"];

    /// <summary>부위 이름. 화면에 부위 체력을 세 줄로 낼 때 쓴다.</summary>
    public static readonly string[] Parts = ["상", "중", "하"];

    /// <summary>맞부딪힘에서 검술 한 자리가 갖는 무게(<c>0x004A995D</c>).</summary>
    private const int ContestSword = 30;

    /// <summary>맞부딪힘에 섞는 주사위(<c>rand(11)</c>).</summary>
    private const int ContestDice = 11;

    /// <summary>아픈 값의 밑동과 주사위(<c>0x004A9B10</c>).</summary>
    private const int HurtBase = 10, HurtDice = 6, WeakDice = 4;

    /// <summary>검술 한 자리가 아픈 값에 얹는 무게.</summary>
    private const int SwordWeight = 10;

    /// <summary>무력을 나누는 수 — 5 다.</summary>
    private const int MightDivider = 5;

    /// <summary>회심의 한 수 — <c>검술 + 운/10 &gt;= rand(1000)</c> 이면 한 배 반이다.</summary>
    private const int CriticalDice = 1000, CriticalLuckDivider = 10;

    /// <summary>필살은 판마다 한 번, 아픈 값이 곱이 된다.</summary>
    private const int FinisherMultiplier = 2;

    /// <summary>상대가 막기를 고를 때 굴리는 주사위와 그 갈림 자리(<c>0x004A9719</c>).</summary>
    private const int GuardDice = 10, GuardRight = 5, GuardNear = 9;

    /// <summary>상대가 딴 데를 치는 몫과 필살을 꺼내는 몫(<c>0x004A986A</c>).</summary>
    private const int FoeStrayDice = 10, FoeStrayFrom = 6, FoeFinisherDice = 2;

    /// <summary>가진 것 가운데 이것이 있으면 스친 것이 막은 것이 된다 — <b>이디스의 방패</b>.</summary>
    public const int EdithShieldId = 4;

    /// <summary>아이템 분류. 표 <c>+0x14</c> 의 번호다.</summary>
    public const int WeaponCategory = 3, ArmorCategory = 4;

    /// <summary>
    /// 맞붙는 상대가 <b>지니고 나오는 무기와 방어구</b>(<c>0x004A89D4</c>~<c>0x004A8D16</c>).
    /// </summary>
    /// <remarks>
    /// 인물 표에 적혀 있는 것이 아니라 <b>그 자리에서 짓는다</b> — 복장 갈래(나라 갈래를
    /// <c>Local.Helpers.FighterSprites.SetForCulture</c> 로 옮긴 값)와 <b>무력</b>으로 가른다.
    /// <code>
    ///   갈래 6(중국)  무기 무력&gt;90 ? 0x3A : 0x39     방어구 무력&gt;70 ? 0x4B : 0x4A
    ///   갈래 7(일본)  무기 무력&gt;90 ? 0x3C : 0x3B     방어구 무력&gt;70 ? 0x4D : 0x4C
    ///   갈래 1·3      아래 표대로 굴린다
    ///   그 밖         무기 0x40 · 방어구 0x42          ; 한 벌로 못 박혀 있다
    /// </code>
    /// 갈래 1(유럽)·3(아랍)은 방어구를 먼저 굴리고 무기를 굴린다.
    /// <code>
    ///   방어구  무력&gt;90  rand(2) ? 0x49 : 0x45      무력&gt;80  rand(2) ? 0x44 : 0x48
    ///           무력&gt;70  rand(2) ? 0x47 : 0x46      그 밖    rand(2) ? 0x42 : 0x43
    ///   무기 1  무력&gt;90  rand(2) ? 0x2E : 0x2D      무력&gt;80  rand(2) ? 0x2C : 0x2B
    ///           무력&gt;70  rand(2) ? 0x2A : 0x29      그 밖    rand(3) 0x28 · 0x26 · 0x25
    ///   무기 3  무력&gt;90  rand(2) ? 0x37 : 0x38      무력&gt;80  rand(2) ? 0x36 : 0x41
    ///           무력&gt;70  rand(2) ? 0x32 : 0x35      그 밖    0x34         ; 굴림 없음
    /// </code>
    /// 이긴 뒤 「모두 뺏는다」를 고르면 이 둘을 그대로 얻는다(<c>0x004AA4B3</c>).
    /// </remarks>
    /// <param name="set">복장 갈래(1~8).</param>
    /// <param name="might">상대의 무력.</param>
    public static (int Weapon, int Armor) GearOf(int set, int might, GameRandom dice)
    {
        if (set == 6) return (might > 90 ? 0x3A : 0x39, might > 70 ? 0x4B : 0x4A);
        if (set == 7) return (might > 90 ? 0x3C : 0x3B, might > 70 ? 0x4D : 0x4C);
        if (set is not (1 or 3)) return (0x40, 0x42);

        int armor = might > 90 ? (dice.Next(2) == 0 ? 0x45 : 0x49)
                  : might > 80 ? (dice.Next(2) == 0 ? 0x48 : 0x44)
                  : might > 70 ? (dice.Next(2) == 0 ? 0x46 : 0x47)
                  : dice.Next(2) == 0 ? 0x43 : 0x42;

        int weapon = set == 1
            ? might > 90 ? (dice.Next(2) == 0 ? 0x2D : 0x2E)
            : might > 80 ? (dice.Next(2) == 0 ? 0x2B : 0x2C)
            : might > 70 ? (dice.Next(2) == 0 ? 0x29 : 0x2A)
            : dice.Next(3) switch { 0 => 0x28, 1 => 0x26, _ => 0x25 }
            : might > 90 ? (dice.Next(2) == 0 ? 0x38 : 0x37)
            : might > 80 ? (dice.Next(2) == 0 ? 0x41 : 0x36)
            : might > 70 ? (dice.Next(2) == 0 ? 0x35 : 0x32)
            : 0x34;

        return (weapon, armor);
    }

    /// <summary>
    /// 굴린 무기·방어구를 <b>아이템 표의 효과</b>로 바꾼다 — 판에 들어가는 값은 아이템 번호가
    /// 아니라 표 <c>0x004FD558</c> 의 <c>+0x10</c> 효과다(<c>0x004A89D4</c>~<c>0x004A8D65</c>).
    /// </summary>
    /// <param name="effectOf">아이템 번호를 효과로 바꾸는 손. 표를 못 읽으면 0 을 내면 된다.</param>
    public static (int Weapon, int Armor) GearFor(int set, int might, GameRandom dice,
                                                  Func<int, int> effectOf)
    {
        var (weapon, armor) = GearOf(set, might, dice);
        return (effectOf(weapon), effectOf(armor));
    }

    /// <summary>도망 판정(<c>0x004A9EED</c>) — <c>운*5 + 10 + rand(60) &gt;= rand(1000)</c>.</summary>
    private const int FleeLuck = 5, FleeBase = 10, FleeDice = 60, FleeRoll = 1000;

    /// <summary>
    /// 용서 판정(<c>0x004A9FC8</c>) — 명성이 2000 아래고 <c>rand(100) &lt; 99 − 대원수</c> 면 살려 준다.
    /// </summary>
    /// <remarks>
    /// 문턱에서 <b>대원 수</b>(<c>0x005AA2C4</c>)를 뺀다(<c>0x004A9FE2</c>) — 사람이 많을수록
    /// 안 봐준다. 아흔아홉이 넘으면 아예 안 봐준다.
    /// </remarks>
    private const int SpareFame = 2000, SpareDice = 100, SpareEdge = 99;

    /// <summary>이번 판이 무엇인가.</summary>
    public enum Phase
    {
        /// <summary>맞부딪힘 — 둘이 한꺼번에 친다. 판은 여기서 열린다.</summary>
        Clash,

        /// <summary>내가 친다.</summary>
        Attack,

        /// <summary>내가 막는다.</summary>
        Guard,
    }

    /// <summary>한 판의 끝.</summary>
    /// <remarks>
    /// 게임 값 그대로다 — <b>0·1 은 내가 맞음, 2 는 막힘, 3·4 는 상대가 맞음</b>이다.
    /// 1 과 3 은 스친 것이라 아픈 값이 반이다.
    /// </remarks>
    public enum Blow
    {
        MeHit = 0, MeGrazed = 1, Blocked = 2, FoeGrazed = 3, FoeHit = 4,
    }

    /// <summary>싸우는 한 사람.</summary>
    /// <param name="Body">체력. 부위마다 <c>체력+1</c> 로 시작한다.</param>
    /// <param name="Might">무력.</param>
    /// <param name="Sword">검술(0~3).</param>
    /// <param name="Luck">운. 회심과 도망에 쓴다.</param>
    /// <param name="Weapon">가진 무기 가운데 가장 센 것의 효과.</param>
    /// <param name="Armor">가진 방어구 가운데 가장 센 것의 효과.</param>
    public readonly record struct Fighter(string Name, int Body, int Might, int Sword,
                                          int Luck, int Weapon, int Armor);

    /// <summary>한 판을 치른 자취. 화면이 이것을 읽어 말과 그림을 고른다.</summary>
    /// <param name="Line">이번에 오간 줄(공격 쪽 기준).</param>
    /// <param name="MyMove">내가 고른 명령. 공격 판이면 공격 줄, 방어 판이면 막기.</param>
    /// <param name="FoeMove">상대의 명령.</param>
    /// <param name="Finisher">필살이 나왔는지.</param>
    /// <param name="Critical">회심의 한 수였는지.</param>
    public readonly record struct Turn(Phase Was, Blow Blow, int Line, int MyMove, int FoeMove,
                                       int Hurt, bool Finisher, bool Critical);

    private readonly GameRandom _dice;

    /// <summary>나와 상대.</summary>
    public Fighter Me { get; }
    public Fighter Foe { get; }

    /// <summary>내가 이디스의 방패를 들었는가 — 스친 것이 막은 것이 된다.</summary>
    public bool HasShield { get; }

    /// <summary>부위 체력 셋. 한 군데라도 0 이 되면 끝이다.</summary>
    public int[] MyParts { get; }
    public int[] FoeParts { get; }

    /// <summary>부위 체력의 처음 값 — 막대를 그릴 때 쓴다.</summary>
    public int MyFull { get; }
    public int FoeFull { get; }

    /// <summary>이번 판이 무엇인가.</summary>
    public Phase Now { get; private set; } = Phase.Clash;

    /// <summary>필살을 이미 썼는가. 판마다 한 번씩이다.</summary>
    public bool MyFinisherSpent { get; private set; }
    public bool FoeFinisherSpent { get; private set; }

    /// <summary>이겼으면 true, 졌으면 false, 아직이면 null.</summary>
    public bool? Won { get; private set; }

    public Duel(Fighter me, Fighter foe, bool shield, int seed)
    {
        // 무력과 운은 <b>담은 값에 1 을 더해</b> 쓴다(0x004A87F1 · 0x004A8809, 상대도 같다).
        // 체력도 그렇고(MyFull), 검술만 그대로다(0x004A87FE).
        Me = me with { Might = me.Might + 1, Luck = me.Luck + 1 };
        Foe = foe with { Might = foe.Might + 1, Luck = foe.Luck + 1 };
        HasShield = shield;
        _dice = new GameRandom(seed);
        MyFull = me.Body + 1;
        FoeFull = foe.Body + 1;
        MyParts = [MyFull, MyFull, MyFull];
        FoeParts = [FoeFull, FoeFull, FoeFull];
    }

    /// <summary>이번 판에 고를 수 있는 명령. 공격 판에서는 필살이 뒤에 셋 더 붙는다.</summary>
    public string[] Choices() => Now switch
    {
        Phase.Guard => Guards,
        Phase.Attack when !MyFinisherSpent => [.. Attacks, .. Finishers],
        _ => Attacks,
    };

    /// <summary>
    /// 한 판을 치른다. <paramref name="pick"/> 은 <see cref="Choices"/> 의 자리다.
    /// </summary>
    public Turn Play(int pick)
    {
        var was = Now;
        bool finisher = false;

        if (was != Phase.Guard && pick >= Lines)
        {
            // 필살은 상·중·하 뒤에 같은 차례로 붙어 있다(0x004A9634).
            finisher = true;
            MyFinisherSpent = true;
            pick -= Lines;
        }

        var turn = was switch
        {
            Phase.Clash => Clash(pick),
            Phase.Attack => Attack(pick, finisher),
            _ => Guard(pick),
        };

        Advance(turn.Blow, was);
        return turn;
    }

    /// <summary>맞부딪힘 — 둘이 한꺼번에 친다(<c>0x004A98EC</c>).</summary>
    /// <remarks>
    /// 같은 줄이면 <b>힘겨루기</b>다. 아니면 상·중·하가 맞물려 돈다 —
    /// 상이 중을, 중이 하를, 하가 상을 이긴다.
    /// </remarks>
    private Turn Clash(int line)
    {
        int foe = _dice.Next(Lines);
        int gap = ((line - foe) % Lines + Lines) % Lines;

        bool win;
        if (gap == 0)
        {
            // 검술 서른 배에 무력을 얹고 주사위를 섞는다. 같으면 다시 굴린다.
            int mine, yours;
            do
            {
                mine = _dice.Next(ContestDice) + Me.Sword * ContestSword + Me.Might;
                yours = _dice.Next(ContestDice) + Foe.Sword * ContestSword + Foe.Might;
            }
            while (mine == yours);
            win = mine > yours;
        }
        else
        {
            win = gap == Lines - 1;
        }

        return win
            ? Strike(Blow.FoeHit, line, line, foe, mine: true, finisher: false)
            : Strike(Blow.MeHit, foe, line, foe, mine: false, finisher: false);
    }

    /// <summary>내가 친다(<c>0x004A99BA</c>).</summary>
    private Turn Attack(int line, bool finisher)
    {
        int guard = FoeGuard();
        var blow = (Blow)(4 - (line + guard) % Lines);
        return blow == Blow.Blocked
            ? new Turn(Phase.Attack, blow, line, line, guard, 0, finisher, false)
            : Strike(blow, line, line, guard, mine: true, finisher);
    }

    /// <summary>내가 막는다(<c>0x004A9A35</c>).</summary>
    private Turn Guard(int guard)
    {
        int line = FoeAttack(out bool finisher);
        var blow = (Blow)((guard + line) % Lines);

        // 이디스의 방패는 스친 것을 막은 것으로 바꾼다(0x004A9A87).
        if (HasShield && blow == Blow.MeGrazed) blow = Blow.Blocked;

        return blow == Blow.Blocked
            ? new Turn(Phase.Guard, blow, line, guard, line, 0, finisher, false)
            : Strike(blow, line, guard, line, mine: false, finisher);
    }

    /// <summary>
    /// 아픈 값을 셈해 그 부위에서 깎는다(<c>0x004A9AC2</c> · <c>0x004A9C7A</c>).
    /// </summary>
    private Turn Strike(Blow blow, int line, int myMove, int foeMove, bool mine, bool finisher)
    {
        var hitter = mine ? Me : Foe;
        var taker = mine ? Foe : Me;

        int worth = (hitter.Sword - taker.Sword) * SwordWeight
                  - taker.Might / MightDivider
                  + hitter.Might / MightDivider
                  - taker.Armor
                  + hitter.Weapon;

        // 밑동이 마이너스면 주사위 둘로만 친다 — 아무리 밀려도 조금은 아프다.
        int hurt = worth < 0
            ? _dice.Next(WeakDice) + _dice.Next(HurtDice) + HurtBase
            : worth + _dice.Next(HurtDice) + HurtBase;

        bool critical = false;
        if (finisher)
        {
            hurt *= FinisherMultiplier;
        }
        else if (hitter.Sword + hitter.Luck / CriticalLuckDivider >= _dice.Next(CriticalDice))
        {
            critical = true;
            hurt = hurt * 3 / 2;
        }

        // 스친 것은 반만 아프다.
        if (blow is Blow.MeGrazed or Blow.FoeGrazed) hurt /= 2;

        var parts = mine ? FoeParts : MyParts;
        parts[line] -= hurt;
        if (parts[line] <= 0)
        {
            parts[line] = 0;
            Won = mine;
        }

        var was = Now;
        return new Turn(was, blow, line, myMove, foeMove, hurt, finisher, critical);
    }

    /// <summary>
    /// 상대가 고르는 막기(<c>0x004A9665</c>). <b>제 부위 가운데 가장 얇은 데</b>를
    /// 감싸는데, 열에 다섯만 제대로 감싸고 넷은 한 칸, 하나는 두 칸 어긋난다.
    /// </summary>
    private int FoeGuard()
    {
        int weak = Weakest(FoeParts);
        int off = Off();
        // off 0 이면 그 부위를 제대로 막는 명령이고, 1·2 는 옆으로 밀린다.
        return (GuardFor(weak) + off * 2) % Lines;
    }

    /// <summary>
    /// 상대가 고르는 공격(<c>0x004A97AE</c>). <b>내 부위 가운데 가장 얇은 데</b>를
    /// 노리는데, 열에 넷은 딴 데로 샌다. 필살은 판마다 한 번, 나머지 여섯 가운데
    /// 절반쯤에서 나온다.
    /// </summary>
    private int FoeAttack(out bool finisher)
    {
        int line = Weakest(MyParts);
        finisher = false;

        if (_dice.Next(FoeStrayDice) < FoeStrayFrom)
        {
            if (!FoeFinisherSpent && _dice.Next(FoeFinisherDice) == 1)
            {
                FoeFinisherSpent = true;
                finisher = true;
            }
            return line;
        }

        int stray;
        do { stray = _dice.Next(Lines); } while (stray == line);
        return stray;
    }

    /// <summary>명령이 얼마나 어긋나는가 — 0 이 절반, 1 이 열에 넷, 2 가 열에 하나다.</summary>
    private int Off()
    {
        int roll = _dice.Next(GuardDice);
        return roll < GuardRight ? 0 : roll < GuardNear ? 1 : 2;
    }

    /// <summary>
    /// 가장 얇은 부위(<c>0x004A9665</c>). 같으면 같은 것끼리 주사위로 가른다.
    /// </summary>
    private int Weakest(int[] parts)
    {
        int a = parts[0] - parts[1];
        if (a > 0)
        {
            int b = parts[1] - parts[2];
            return b > 0 ? 2 : b < 0 ? 1 : _dice.Next(2) + 1;
        }
        if (a < 0)
        {
            int b = parts[0] - parts[2];
            return b > 0 ? 2 : b < 0 ? 0 : _dice.Next(2) * 2;
        }
        int c = parts[1] - parts[2];
        return c > 0 ? 2 : c < 0 ? _dice.Next(2) : _dice.Next(Lines);
    }

    /// <summary>
    /// 다음 판이 무엇인가(<c>0x004A6E9E</c>) — <b>제대로 맞히면 그대로, 스치거나
    /// 막히면 공수가 바뀐다</b>.
    /// </summary>
    private void Advance(Blow blow, Phase was)
    {
        if (was == Phase.Clash)
        {
            Now = blow == Blow.FoeHit ? Phase.Attack : Phase.Guard;
            return;
        }
        if (blow is Blow.MeHit or Blow.FoeHit) return;      // 그대로
        Now = was == Phase.Attack ? Phase.Guard : Phase.Attack;
    }

    /// <summary>부위 하나가 뚫려 판이 끝났는가.</summary>
    public bool Over => Won != null;

    /// <summary>
    /// 판을 치르며 <b>잃은 만큼</b>(<c>0x004AA600</c>) — <c>체력+1 − 남은 부위 셋의 평균</c>이다.
    /// 이 값이 <b>컨디션</b>에서 깎인다(<see cref="Support.Local.Models.Player.Hurt"/>).
    /// </summary>
    public int BodyLost => MyFull - (MyParts[0] + MyParts[1] + MyParts[2]) / Lines;

    /// <summary>진 뒤에 어떻게 되는가.</summary>
    public enum Fate
    {
        /// <summary>틈을 봐서 도망쳤다.</summary>
        Fled,

        /// <summary>상대가 봐 주었다.</summary>
        Spared,

        /// <summary>베였다. 게임은 여기서 놀이가 끝난다.</summary>
        Slain,
    }

    /// <summary>
    /// 졌을 때의 끝을 가린다(<c>0x004A9EED</c> · <c>0x004A9FC8</c>).
    /// </summary>
    /// <param name="fame">내 명성. 2000 을 넘으면 봐 주지 않는다.</param>
    /// <param name="canFlee">
    /// 도망을 굴릴 수 있는 판인지 — 무대가 <b>4 이상</b>(술집·모스크·사원)이라야 한다
    /// (<c>0x004A9EE4</c> 의 <c>cmp eax, 4 / jl</c>).
    /// </param>
    /// <param name="canSpare">
    /// 용서를 굴릴 수 있는 판인지 — 무대가 0(갑판)이 아니고 종류가 <b>0·3·8</b> 가운데
    /// 하나라야 한다(<c>0x004A9EBE</c> · <c>0x004A9ED6</c> · <c>0x004A9EDE</c>).
    /// 아니면 도망도 용서도 없이 곧바로 죽는다.
    /// </param>
    /// <param name="crew">대원 수(<c>0x005AA2C4</c>) — 용서 문턱에서 이만큼 뺀다.</param>
    public Fate FateOf(int fame, bool canFlee = true, bool canSpare = true, int crew = 0)
    {
        if (canFlee && Me.Luck * FleeLuck + FleeBase + _dice.Next(FleeDice) >= _dice.Next(FleeRoll))
            return Fate.Fled;
        if (canSpare && fame <= SpareFame && _dice.Next(SpareDice) < SpareEdge - crew)
            return Fate.Spared;
        return Fate.Slain;
    }

    /// <summary>
    /// 일기토를 걸었을 때 상대가 내뱉는 말(<c>0x004A48A0</c>).
    /// </summary>
    /// <remarks>
    /// <code>
    ///   004a48be  성미 여덟 칸을 뜬다(vtbl+0x24)
    ///   004a48c1  ebx = 성미[3]  ; [esp+0x30] 은 버퍼(S1+0x18) 에서 0x0C, 곧 칸 3 이다
    ///   004a48d5  묶음 크기 = {5, 6, 5}   (0x00572940)
    ///   004a48ec  표 = 0x00572998[ebx]    → 0x00572950 · 0x00572968 · 0x00572980
    /// </code>
    /// 무신경할수록 살려 달라 빌고, 신경질일수록 먼저 덤벼든다.
    /// </remarks>
    public static string Taunt(IReadOnlyList<int> fortune, GameRandom dice)
    {
        var set = Taunts[Math.Clamp(fortune.Count > TauntSlot ? fortune[TauntSlot] : 1, 0, 2)];
        return set[dice.Next(set.Length)];
    }

    /// <summary>
    /// 대사 묶음을 가르는 성미 칸 — <b>넷째</b>(칸 3)다. 영해 경고에 불복하는지를 가르는
    /// 칸과 같다(<c>0x0048C5E8</c>).
    /// </summary>
    public const int TauntSlot = 3;

    /// <summary>성미마다의 대사 묶음(<c>0x00551228</c> ~ <c>0x00551500</c>).</summary>
    private static readonly string[][] Taunts =
    [
        [
            "뭐, 뭐라고. 나, 나와 해보자는 건가~.",
            "아, 아파. 잠깐 배가... 기, 기다려. 아직 용건이...",
            "잠깐 기다리게! 자네에게 원한 산 일이 없다. 이야기해야 알 것 아닌가.",
            "사람 잘못 본 것 아닌가. 왜 싸우지 않으면 안되지.",
            "진정하게. 뭔가 오해다. 우, 우와.",
        ],
        [
            "뭐, 뭐야, 도대체 어떻게 할 작정이냐.",
            "좋아. 상대해 주지.",
            "네 놈이냐? 최근, 여관이나 술집에서 소동을 일으킨 녀석이?",
            "쓸데없는 싸움은 하고 싶지 않지만, 할 수 없군.",
            "나를 방해할 작정이냐! 누구의 조종이냐?",
            "이런 곳에서 죽을 내가 아니지. 질 수 없다.",
        ],
        [
            "되려 당하게 해 주마, 자, 덤벼라!",
            "나에게 칼을 내민 걸 후회하게 해 주마!",
            "조금 전부터 살기를 느끼고 있었지. 덤벼라, 상대해 주마.",
            "자신의 미숙함을 모르는 어리석은 녀석..., 따끔한 맛을 보여 주지.",
            "그 배짱은 칭찬할 만하지만, 무모하군. 덤벼라.",
        ],
    ];

    /// <summary>
    /// 신청을 한 뒤 상대가 <b>달아나려 드는지</b>(<c>0x004A4901</c>) — 성미 칸
    /// <see cref="TauntSlot"/> 이 0 이면 늘, 1 이면 <c>rand(2) == 0</c> 일 때, 2 면 아예 안 든다.
    /// </summary>
    public static bool TriesToFlee(int temper, GameRandom dice) => Math.Clamp(temper, 0, 2) switch
    {
        0 => true,
        1 => dice.Next(2) == 0,
        _ => false,
    };

    /// <summary>
    /// 달아나려 드는 상대를 쫓아 잡는지(<c>0x004A494B</c>) — 내 체력과 상대 체력에
    /// 주사위 오십씩을 얹어 견준다. 못 미치면 놓친다.
    /// </summary>
    /// <param name="myBody">
    /// 제독 체력(담은 값). <b>부관이 있고 그 값이 더 크면 부관 것을 쓴다</b>
    /// (<c>0x004A4964</c> 의 <c>0x00468F10(부관, 0)</c>).
    /// </param>
    public static bool Caught(int myBody, int foeBody, GameRandom dice) =>
        myBody + 1 + dice.Next(ChaseDice) >= foeBody + dice.Next(ChaseDice) + 1;

    /// <summary>쫓을 때 섞는 주사위.</summary>
    private const int ChaseDice = 50;

    /// <summary>
    /// 이긴 뒤 <b>1/100</b> 으로 무력이 1~2 오른다(<c>0x004AA592</c> → <c>0x00455CA0</c>).
    /// </summary>
    /// <remarks>
    /// <code>
    ///   00455cb5  rand(100) != 0 이면 아무 일 없다
    ///   00455ccc  부관이 안 싸웠고 제독 무력 &lt;= 99 면 제독이 오를 자리에 든다
    ///   00455cf2  부관이 있고 그 무력 &lt;= 99 면 부관도 든다
    ///   00455d08  오르는 값 = rand(2) + 1
    /// </code>
    /// 승리 차림표가 <b>뜬 판에서만</b> 굴린다 — 종류 2·5·7 이상은 차림표째 건너뛰므로 없다.
    /// </remarks>
    /// <param name="mateFought">부관이 대신 싸웠는지 — 그러면 제독은 안 오른다.</param>
    /// <param name="myMight">제독 무력(담는 값). 99 를 넘으면 안 오른다.</param>
    /// <param name="mateMight">부하 첫 자리의 무력(담는 값). 부하가 없으면 null.</param>
    public static (int By, bool Mine, bool Mate) MightGrowth(bool mateFought, int myMight,
                                                             int? mateMight, GameRandom dice)
    {
        if (dice.Next(100) != 0) return (0, false, false);

        bool mine = !mateFought && myMight <= GrowthCeiling;
        bool mate = mateMight is { } m && m <= GrowthCeiling;
        if (!mine && !mate) return (0, false, false);

        return (dice.Next(2) + 1, mine, mate);
    }

    /// <summary>이 값을 넘으면 더 안 오른다(<c>0x00455CD1</c> 의 <c>sub eax, 0x63</c>).</summary>
    private const int GrowthCeiling = 99;
}
