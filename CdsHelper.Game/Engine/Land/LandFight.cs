using CdsHelper.Support.Local.Models;

namespace CdsHelper.Game.Engine.Land;

/// <summary>
/// 육상전 한 턴을 굴린다 — 차례를 매기고, 부대를 하나씩 움직이고, 피해를 먹인다.
/// </summary>
/// <remarks>
/// 게임의 <c>0x00449320</c>(한 턴) · <c>0x00448050</c>(부대 하나 움직이기) ·
/// <c>0x00449250</c>(한 대 때리기) · <c>0x00448360</c>(피해 매기기)를 옮긴 것이다.
/// 셈은 <see cref="LandUnits"/> 에 있고 여기서는 <b>차례</b>만 맡는다.
///
/// 한 턴이 남기는 것은 <see cref="Line"/> 목록이다 — 화면은 그것을 한 줄씩 읽어
/// 보여 준다. 그래야 셈과 그리기가 안 엉킨다.
/// </remarks>
public sealed class LandFight(LandBattle battle, GameRandom dice)
{
    /// <summary>한 턴에 일어난 일 한 줄.</summary>
    /// <param name="Text">화면에 낼 말. 비어 있으면 안 낸다.</param>
    /// <param name="Actor">움직인 부대. 없으면 −1.</param>
    /// <param name="Target">맞은 부대. 없으면 −1.</param>
    /// <param name="Damage">깎인 병사수.</param>
    /// <param name="Sound">낼 효과음 파트. −1 이면 없다.</param>
    /// <summary>
    /// 한 턴에 일어난 일 하나.
    /// </summary>
    /// <param name="Men">
    /// <b>그 일이 일어난 바로 뒤</b>의 병사수 열둘. 창은 이것으로 판을 그린다 —
    /// 한 턴을 통째로 굴려 놓고 나서 그림을 돌리므로, 이것이 없으면 그 턴에 죽을 부대가
    /// 그림이 시작될 때 이미 사라져 있다.
    /// </param>
    /// <param name="Felled">이 일로 <b>쓰러진</b> 부대. 없으면 −1 이다.</param>
    /// <param name="Fell">쓰러지며 남기는 말(<c>0x00446C00</c>). 없으면 빈 글이다.</param>
    public readonly record struct Line(string Text, int Actor = -1, int Target = -1,
                                       int Damage = 0, int Sound = -1,
                                       IReadOnlyList<int>? Men = null,
                                       int Felled = -1, string Fell = "");

    private readonly List<Line> _log = [];

    /// <summary>
    /// 심판(벼락)이 <b>판을 끝냈는지</b> — 그러면 그 턴은 통째로 건너뛴다.
    /// </summary>
    /// <remarks>
    /// <c>0x00448F80</c> 이 한 부대를 칠 때마다 <c>+0x3C</c> 를 보고, 8(판이 이어짐)이
    /// 아니면 <b>1 을 내고 곧바로 나간다</b>. 받은 <c>0x00449369</c> 는 <c>0x00449410</c> 으로
    /// 뛰어 행동 차례 짜기·적 명령·싸움 되돌이를 다 건너뛴다.
    /// </remarks>
    public bool Struck { get; private set; }

    /// <summary>
    /// 자리 열둘을 섞어 낸다 — 게임의 피셔–예이츠 그대로다(<c>0x00448F9A</c>).
    /// </summary>
    /// <remarks>
    /// <code>
    ///   00448f9c  arr[i] = i                       ; 0..11
    ///   00448fa8  for i in 0..11:
    ///   00448fb0      r = rand(12 - i) + i
    ///   00448fc2      swap(arr[i], arr[r])         ; 0x00444540
    /// </code>
    /// </remarks>
    private static int[] Shuffled(GameRandom dice)
    {
        var order = new int[LandBattle.Slots];
        for (int i = 0; i < order.Length; i++) order[i] = i;
        for (int i = 0; i < order.Length; i++)
        {
            int r = dice.Next(order.Length - i) + i;
            (order[i], order[r]) = (order[r], order[i]);
        }
        return order;
    }

    /// <summary>턴이 열릴 때의 병사수 열둘 — 그림은 여기서 시작한다.</summary>
    public IReadOnlyList<int> Opening { get; private set; } = [];

    /// <summary>지금 병사수 열둘을 떠 둔다.</summary>
    private int[] Snapshot()
    {
        var men = new int[LandBattle.Slots];
        for (int i = 0; i < men.Length; i++) men[i] = battle.Units[i].Men;
        return men;
    }

    /// <summary>줄 하나를 적는다 — 적는 그 시점의 병사수를 같이 담는다.</summary>
    private void Log(Line line) => _log.Add(line with { Men = Snapshot() });

    /// <summary>
    /// 쓰러지며 남기는 말을 고른다(<c>0x00446C00</c>).
    /// </summary>
    /// <remarks>
    /// 문화권마다 제 말이 있고, 안 걸리면 <b>짧은 비명 열하나</b>(표 <c>0x00549C18</c>)에서
    /// 하나를 굴린다. 말이 아예 안 나오는 낯도 있다.
    ///
    /// 문화권은 <b>쓰러진 쪽</b>의 것이다 — <c>0x00447F7E</c> 가 <c>0x00446200(자리)</c> 으로
    /// 편(<c>자리 &gt;= 6</c>)을 내고 그것을 <c>0x00447070</c> 에 넘긴다. 예전에는 늘
    /// 적 문화권을 써서, 아군이 쓰러져도 「이놈 남만인!」 같은 말이 나왔다.
    /// </remarks>
    private string FellWord(int slot)
    {
        string? said = battle.CultureOfSide(slot >= LandBattle.FirstFoe) switch
        {
            0 or 1 or 2 => dice.Next(10) < 4 ? "오오, 신이여···"
                         : dice.Next(10) < 4 ? "아니, 이럴 수가!!" : null,
            6 => dice.Next(10) < 2 ? "으아~!" : null,
            9 => dice.Next(9) < 3 ? "분하다!"
               : dice.Next(9) < 3 ? "이놈 남만인!"
               : dice.Next(9) < 3 ? "이것이 무인의 죽음이다." : null,
            10 => dice.Next(10) < 3 ? "침략자놈···" : null,
            _ => null,
        };
        if (said != null) return said;
        if (dice.Next(100) < 5) return "엄마아!";
        return Cries[dice.Next(Cries.Length)];
    }

    /// <summary>
    /// 몸짓마다의 말 — 게임은 무리마다 <b>셋(총·포는 다섯) 가운데 하나</b>를 집는다
    /// (<c>0x00446D00(무리)</c> 이 <c>rand(3)</c>, <c>0x00446D90</c> 이 <c>rand(5)</c>).
    /// </summary>
    /// <remarks>
    /// 표는 <c>0x00549C48</c> 부터 널로 끊어 늘어서 있다 — 되받아치기 · 닌자 · 주술사 ·
    /// 고승 · 표범 · 젖은 화약 차례다. 예전에는 무리마다 한 줄씩만 냈다.
    /// </remarks>
    private static readonly string[] Countered =
    [
        "남만 검술 따위 무섭지도 않다.",
        "네 약점을 알았다! 받아라!",
        "바보같으니. 빈틈투성이로군.",
    ];

    private static readonly string[] Vanished =
    [
        "둔갑술의 하나, 변신술!",
        "둔갑술이 어떤 건지 잘 봐라!",
        "싸워봤자다.",
    ];

    private static readonly string[] RainCalls =
    [
        "정령님, 비를 내려 주소서.",
        "비여, 우리를 지켜다오.",
        "비여, 우리가 이기게 해다오.",
    ];

    private static readonly string[] Prayers =
    [
        "신이여, 상처입은 자에게 힘을!",
        "지금 구해 주겠다..",
        "포기하지 마라. 상처는 가볍다.",
    ];

    private static readonly string[] Dancing =
    [
        "다들 힘을 내라!",
        "격려의 춤을 봐라.",
        "태양신이여, 우리들에게 힘을!",
    ];

    /// <summary>비에 젖어 못 쏠 때의 다섯(<c>0x00549C98</c>).</summary>
    private static readonly string[] Damped =
    [
        "제기랄! 화약이 눅눅해졌어!",
        "비 때문에 화약이···",
        "비 속에서는 총도 대포도 소용없군.",
        "체! 불발인가!",
        "어, 총알이 안 나온다.",
    ];

    /// <summary>짧은 비명 열하나(<c>0x00549C18</c>).</summary>
    private static readonly string[] Cries =
    [
        "윽!", "우악!", "꺅!", "아···", "죽고 싶지 않아···", "으윽!", "끄윽!",
        "이럴 수가···", "끝장인가···", "오늘은 이 정도로 용서해 주마.", "두고 보자.",
    ];

    /// <summary>비가 오는지 — 주술사가 부르면 총·포가 죽는다(<c>+0x3C</c> 의 <c>0x20</c>).</summary>
    public bool Raining { get; private set; }

    /// <summary>
    /// 표범이 춘 춤의 겹수(<c>+0x44</c>). 적 공격력이 겹마다 1.5배가 된다.
    /// </summary>
    /// <remarks>
    /// <b>턴마다 0 으로 지운다</b>(<c>0x00449D5E</c>) — 그 턴에 춘 춤만 센다. 예전에는 판이
    /// 끝날 때까지 쌓여, 표범이 여럿인 판에서 열 턴째 공격력이 수십 배가 되었다.
    /// </remarks>
    public int Dances { get; private set; }

    /// <summary>다 빈치의 작렬탄 — 서 있으면 포가 비를 무시한다(<c>+0x3C</c> 의 <c>0x48</c>).</summary>
    private bool Shells => battle.Shells;

    /// <summary>
    /// 한 턴을 굴린다. 돌려주는 것은 그 턴에 일어난 일들이다.
    /// </summary>
    /// <param name="mine">아군이 고른 공격명령(<see cref="LandBattle.Normal"/> 따위).</param>
    /// <param name="theirs">적이 고른 공격명령.</param>
    public IReadOnlyList<Line> Turn(int mine, int theirs)
    {
        _log.Clear();
        Opening = Snapshot();
        (_myOrder, _foeOrder) = (mine, theirs);
        Dances = 0;                      // 춤 겹수는 턴마다 지운다(0x00449D5E)

        // 적이 「퇴각」을 골랐으면 <b>아무도 안 움직이고</b> 그 자리에서 이긴다
        // (0x004493A5 가 [+0x94] == 4 이면 [+0x3C] = 1 을 적고 돌아간다).
        if (theirs == LandBattle.Retreat)
        {
            Over = true;
            return _log;
        }

        // 차례는 행동속도가 빠른 쪽부터다(0x00447C20 → 0x004493BE 의 순서표).
        // 기습이 먹히면 아군이, 어그러지면 적이 앞선다(0x00447E10).
        var order = Order();
        if (_ambush != 0)
        {
            bool mineFirst = _ambush > 0;
            order = [.. order.OrderByDescending(s => (s < LandBattle.FirstFoe) == mineFirst)];
            _ambush = 0;
        }

        foreach (int slot in order)
        {
            if (!battle.Units[slot].Standing) continue;
            if (_frozen.Remove(slot)) continue;          // 함정에 걸린 부대는 못 움직인다
            Act(slot);
            if (Over != null) break;
        }
        _frozen.Clear();

        // 비는 한 턴만 온다(0x00449403 이 그 비트를 끈다).
        Raining = false;
        return _log;
    }

    /// <summary>싸움이 끝났으면 이긴 쪽 — 참이면 아군, 거짓이면 적. 아직이면 null.</summary>
    public bool? Over { get; private set; }

    /// <summary>이번 턴에 못 움직이는 부대들 — 함정에 걸린 자리다(<c>+0x1C</c> 의 0x20).</summary>
    private readonly HashSet<int> _frozen = [];

    /// <summary>이번 턴에 앞서는 쪽 — 1 아군 · −1 적 · 0 없음.</summary>
    private int _ambush;

    /// <summary>싸움을 그대로 끝낸다 — 일기토가 판을 가른 자리다.</summary>
    public void End(bool won) => Over = won;

    // ── 묘책 — 0x004490D0 ──────────────────────────────────────────────────────

    /// <summary>
    /// 묘책을 건다. 돌려주는 것은 화면에 낼 줄들이다.
    /// </summary>
    /// <remarks>
    /// 성사는 <see cref="LandBattle.TryRuse"/> 가 굴린다. 어그러지면 <b>그대로 제 발등을
    /// 찍는다</b> — 함정은 아군 한 부대가 묶이고, 암살자는 아군 한 부대가 없어진다.
    /// </remarks>
    public IReadOnlyList<Line> Ruse(int ruse, GameRandom dice, out bool asked)
    {
        var said = new List<Line>();
        bool won = battle.TryRuse(ruse, dice);
        asked = false;

        switch (ruse)
        {
            case LandBattle.Ambush:
                // 성사면 아군이, 어그러지면 적이 먼저 친다(0x00447E10). 둘 다 <b>알림</b>이지 물음이 아니다.
                said.Add(new Line(won ? "기습성공! 선제 공격을 가하겠습니까?"
                                      : "기습실패! 복병을 만났다!"));
                _ambush = won ? 1 : -1;
                break;

            case LandBattle.Trap:
                // 문구는 부대 이름이 안 들어간 붙박이다(0x0056D360 · 0x0056D378).
                int caught = Any(!won, dice, leader: true);
                if (caught >= 0)
                {
                    _frozen.Add(caught);
                    said.Add(new Line(won ? "적부대는 함정에 빠졌다!" : "아군부대가 함정에 빠졌다!"));
                }
                break;

            case LandBattle.Assassin:
                // 상대 부대가 하나뿐이면 아무 일도 안 일어난다(0x00447530).
                int side = won ? LandBattle.FirstFoe : 0;
                if (Count(side) <= 1)
                {
                    said.Add(new Line(won ? "암살자는 실패했다!" : "암살자는 실패했다!"));
                    break;
                }

                // 총대장 부대는 안 노린다(0x004476C0 의 둘째 인자 0).
                int killed = Any(!won, dice, leader: false);
                if (killed >= 0)
                {
                    battle.SetMen(killed, 0);
                    said.Add(new Line(won ? "암살자는 적의 한 부대를 전멸시켰다!"
                                          : "암살자는 배반하여 아군의 한 부대를 전멸시켰다!"));
                    Done();
                }
                break;

            case LandBattle.Judgement:
                // 하늘에서 벼락이 — 굴림 없이 <b>양쪽</b> 선 부대를 다 친다(0x00448F80).
                // 차례는 <b>섞는다</b>(0x00448F9A 의 피셔–예이츠). 치는 사이에 판이 끝나면
                // 거기서 멈추고 참을 내며, 그러면 <b>그 턴은 통째로 건너뛴다</b>(0x0044904B).
                said.Add(new Line("하늘에서 벼락이···!"));
                foreach (int slot in Shuffled(dice))
                {
                    if (!Alive(slot)) continue;
                    int hurt = (dice.Next(100) == 0 ? dice.Next(1000) : dice.Next(100)) + 1;
                    int men = battle.Units[slot].Men;
                    hurt = Math.Min(hurt, men);
                    battle.SetMen(slot, men - hurt);
                    said.Add(new Line("", Actor: slot, Target: slot, Damage: hurt, Men: Snapshot()));
                    Done();
                    if (Over != null) { Struck = true; break; }
                }
                Done();
                break;
        }

        // 성사·어그러짐 소리는 굴리는 그 자리에서 한 번 난다(0x00449080).
        if (said.Count > 0)
            said[0] = said[0] with
            {
                Sound = won ? LandUnits.Sound.RuseWon : LandUnits.Sound.RuseLost,
            };

        _log.Clear();
        _log.AddRange(said);
        return said;
    }

    /// <summary>
    /// 그 편에서 아무 부대 하나. 없으면 −1.
    /// </summary>
    /// <param name="leader">총대장 부대도 고를지(<c>0x004476C0</c> 의 둘째 인자) — 암살자는 안 고른다.</param>
    private int Any(bool mine, GameRandom dice, bool leader)
    {
        int side = mine ? 0 : LandBattle.FirstFoe;
        var live = new List<int>();
        for (int i = side; i < side + LandBattle.PerSide; i++)
            if (Alive(i) && (leader || !battle.Units[i].IsLeader)) live.Add(i);
        return live.Count == 0 ? -1 : live[dice.Next(live.Count)];
    }

    /// <summary>그 편에 선 부대 수.</summary>
    private int Count(int side)
    {
        int n = 0;
        for (int i = side; i < side + LandBattle.PerSide; i++) if (Alive(i)) n++;
        return n;
    }

    // ── 차례 ───────────────────────────────────────────────────────────────────

    /// <summary>
    /// 행동속도(<c>0x00447C20</c>)로 매긴 차례. 빠른 쪽이 먼저다.
    /// </summary>
    /// <remarks>
    /// 갈래마다 보는 기능이 다르다.
    /// <code>
    ///   0 근접  rand(14 - 검술)   + 검술*2
    ///   1 사격  rand(14 - 사격술) + 사격술*2
    ///   2 포    rand(14 - 포술)   + 포술*2
    ///   3 지원  rand(2*(7 - 신학)) + 신학*3
    /// </code>
    /// <b>속도가 같으면</b> 갈래로 한 번 더 가른다(<c>0x00447D35</c>). 같은 속도가 이어지는
    /// 동안만 끼워넣기 정렬을 도는데,
    /// <code>
    ///   00447d8e  왼쪽이 갈래 3(지원)이면 건드리지 않는다
    ///   00447da8  오른쪽이 갈래 3 이면 <b>무조건</b> 앞으로 당긴다
    ///   00447db3  아니면 왼쪽 갈래 &gt; 오른쪽 갈래 일 때만 당긴다
    ///   00447ddb  당기다가 갈래 3 을 만나면 멈춘다
    /// </code>
    /// 곧 <b>지원이 맨 앞</b>이고 나머지는 <b>갈래 오름차순</b>(근접 → 사격 → 포)이며,
    /// 같은 갈래끼리는 자리 차례 그대로다. 예전에는 속도만 보고 같은 속도는 아무렇게나
    /// 두었다.
    /// </remarks>
    private int[] Order()
    {
        var speed = new int[LandBattle.Slots];
        for (int i = 0; i < LandBattle.Slots; i++)
        {
            var unit = battle.Units[i];
            if (!unit.Standing) { speed[i] = int.MinValue; continue; }

            int level = SkillFor(i, LandUnits.KindOf(unit.Kind));
            speed[i] = LandUnits.KindOf(unit.Kind) == LandUnits.Kind.Support
                ? dice.Next(Math.Max(1, 2 * (7 - level))) + level * 3
                : dice.Next(Math.Max(1, 14 - level)) + level * 2;
        }

        // OrderBy 는 제자리 차례를 지킨다 — 게임의 끼워넣기 정렬과 같은 결이다.
        return [.. Enumerable.Range(0, LandBattle.Slots)
                             .OrderByDescending(i => speed[i])
                             .ThenBy(TieKind)];
    }

    /// <summary>같은 속도끼리 가르는 잣대 — 지원(3)은 −1 로 쳐서 맨 앞이다(<c>0x00447DA8</c>).</summary>
    private int TieKind(int slot)
    {
        var kind = LandUnits.KindOf(battle.Units[slot].Kind);
        return kind == LandUnits.Kind.Support ? -1 : (int)kind;
    }

    /// <summary>그 갈래가 보는 기능 자리.</summary>
    private int SkillFor(int slot, LandUnits.Kind kind) => kind switch
    {
        LandUnits.Kind.Shot => battle.SkillAt(slot, Skill.Shooting),
        LandUnits.Kind.Cannon => battle.SkillAt(slot, Skill.Gunnery),
        LandUnits.Kind.Support => battle.SkillAt(slot, Skill.Theology),
        _ => battle.SkillAt(slot, Skill.Sword),
    };

    // ── 부대 하나 움직이기 — 0x00448050 ────────────────────────────────────────

    /// <summary>이번 턴에 두 편이 고른 공격명령.</summary>
    private int _myOrder, _foeOrder;

    /// <summary>
    /// 그 자리가 <b>제 편의</b> 명령. 공격은 치는 쪽 것이고 <b>방어는 맞는 쪽 것</b>이다.
    /// </summary>
    /// <remarks>
    /// 게임의 <c>0x00448120</c>(공격)·<c>0x00448180</c>(방어)이 둘 다 <b>자리를 받아</b>
    /// 여섯보다 작으면 아군 명령(<c>+0x90</c>)을, 아니면 적 명령(<c>+0x94</c>)을 본다.
    ///
    /// 예전에는 <b>치는 쪽 명령 하나로</b> 공격도 방어도 굽혔다. 그래서 내가 돌격을
    /// 고르면 적 방어까지 0.7 로 깎이고, 적이 돌격을 고르면 <b>내 방어</b>가 0.7 로
    /// 깎였다 — 한 턴에 아군이 통째로 쓰러져 판에서 사라지곤 했다.
    /// </remarks>
    private int OrderAt(int slot) => slot < LandBattle.FirstFoe ? _myOrder : _foeOrder;

    private void Act(int slot)
    {
        var unit = battle.Units[slot];
        var kind = LandUnits.KindOf(unit.Kind);
        bool mine = slot < LandBattle.FirstFoe;

        switch (kind)
        {
            case LandUnits.Kind.Melee:
                // 창병은 <b>앞열 자리를 굴려</b> 잡고 그 뒤까지 둘을 친다(0x004487C0).
                // 그 밖의 근접은 총대장·최소·자리굴림 네 갈래다(0x00448900).
                int front = unit.Kind == LandUnits.Spear ? FrontByRoll(foe: mine) : MeleeTarget(mine);
                if (front < 0) { Done(); return; }
                Hit(slot, front);
                if (unit.Kind == LandUnits.Spear && Behind(front) is { } back && Alive(back))
                    Hit(slot, back);
                break;

            case LandUnits.Kind.Shot:
                if (Damp(unit.Kind)) { Say(slot, Damped[dice.Next(Damped.Length)]); break; }
                // 궁병만 아무나 하나를 노린다(0x00448880). 나머지는 앞열 전부대다.
                if (unit.Kind == LandUnits.Bow)
                {
                    int one = BowTarget(mine);
                    if (one >= 0) Hit(slot, one);
                }
                else foreach (int at in Facing(mine, frontOnly: true)) Hit(slot, at);
                break;

            case LandUnits.Kind.Cannon:
                if (Damp(unit.Kind)) { Say(slot, Damped[dice.Next(Damped.Length)]); break; }
                // 작렬탄을 받으면 <b>아군 포만</b> 한 차례에 두 번 쏜다(0x00448BD3).
                int volleys = Shells && slot < LandBattle.FirstFoe ? 2 : 1;
                for (int v = 0; v < volleys; v++)
                    foreach (int at in Facing(mine, frontOnly: false)) Hit(slot, at);
                break;

            default:
                Support(slot, unit.Kind);
                break;
        }
        Done();
    }

    /// <summary>
    /// 비에 죽는 병종인지 — <b>궁병만 빠져나간다</b>(<c>0x00448A30</c> 의 <c>cmp eax,0x11</c>).
    /// </summary>
    /// <remarks>다 빈치의 작렬탄이 서 있으면 포는 비를 무시한다(<c>0x00448DD0</c>).</remarks>
    private bool Damp(int unitKind)
    {
        if (!Raining || unitKind == LandUnits.Bow) return false;
        return !(Shells && LandUnits.KindOf(unitKind) == LandUnits.Kind.Cannon);
    }

    /// <summary>지원 병종 셋(<c>0x00448C80</c>).</summary>
    private void Support(int slot, int unitKind)
    {
        switch (unitKind)
        {
            case LandUnits.Shaman:
                if (Raining) return;                 // 이미 오면 아무것도 안 한다
                Raining = true;
                Say(slot, RainCalls[dice.Next(RainCalls.Length)], LandUnits.Sound.Rain);
                break;

            case LandUnits.Monk:
                // <b>한 부대만</b> 고친다(0x00448CFE) — 총대장 부대의 병사수가 정원의 4할
                // 이상이면 그 대장을, 아니면 제 편에서 병사수가 가장 적은 부대를 고른다.
                // 되살리는 만큼은 min(정원, 병사수 + 정원*2/10) 이다(0x00448280).
                bool monkFoe = slot >= LandBattle.FirstFoe;
                int side = monkFoe ? LandBattle.FirstFoe : 0;
                int room = battle.RoomPerUnit(side);

                int who = LeaderOf(monkFoe);
                if (who < 0 || battle.Units[who].Men < room * 4 / 10)
                    who = Pick(foe: monkFoe, frontOnly: false);
                if (who < 0) break;

                int was = battle.Units[who].Men;
                int now = Math.Min(room, was + room * 2 / 10);
                battle.SetMen(who, now);
                if (now > was)
                    Say(slot, Prayers[dice.Next(Prayers.Length)], LandUnits.Sound.Heal);
                break;

            case LandUnits.Leopard:
                Dances++;
                Say(slot, Dancing[dice.Next(Dancing.Length)], LandUnits.Sound.Dance);
                break;
        }
    }

    // ── 한 대 때리기 — 0x00449250 ──────────────────────────────────────────────

    private void Hit(int from, int to)
    {
        if (!Alive(from) || !Alive(to)) return;

        // 치는 소리는 <b>치는 쪽 병종</b>의 것이다 — 되받아쳐 편이 뒤바뀌어도 그대로다.
        int sound = LandUnits.SoundOf(battle.Units[from].Kind, Raining);
        int hurt = Worth(from, to);

        // 되받아치기 — 사무라이 15 · 하타모토 20 · 영주 25 (0x004481E0).
        int kick = battle.Units[to].Kind switch
        {
            LandUnits.Samurai => 15,
            LandUnits.Hatamoto => 20,
            LandUnits.Lord => 25,
            _ => 0,
        };
        bool kicked = kick > 0 && LandUnits.KindOf(battle.Units[from].Kind) == LandUnits.Kind.Melee
                   && dice.Next(100) <= kick;
        if (kicked)
        {
            Say(to, Countered[dice.Next(Countered.Length)]);
            (from, to) = (to, from);
            hurt = Worth(from, to);
        }

        // 닌자의 변신술 — <b>작렬탄이 없을 때</b> 40%로 피해가 없다(0x004492AA 는 비가 아니라 작렬탄을 본다).
        // <b>되받아쳤으면 아예 안 굴린다</b> — 게임은 되받아친 가지에서 0x004492FB 로 뛰어
        // 닌자 칸을 통째로 건너뛴다(0x00449299).
        if (!kicked && battle.Units[to].Kind == LandUnits.Ninja && !Shells && dice.Next(100) < 40)
        {
            Log(new Line(Vanished[dice.Next(Vanished.Length)], from, to, 0, LandUnits.Sound.Ninja));
            return;
        }

        hurt = Math.Min(hurt, battle.Units[to].Men);
        battle.SetMen(to, battle.Units[to].Men - hurt);

        // 쓰러졌으면 그 자리에서 말을 고른다 — 게임도 피해 숫자를 보인 <b>다음에</b>
        // 말풍선을 띄우고 그러고 나서 부대를 지운다(0x00447F50).
        bool felled = battle.Units[to].Men <= 0;
        Log(new Line($"{Name(from)}의 공격 — {Name(to)} {hurt}명", from, to, hurt, sound,
                     Felled: felled ? to : -1, Fell: felled ? FellWord(to) : ""));
        Done();
    }

    /// <summary>
    /// 피해를 매긴다(<c>0x00448360</c>).
    /// </summary>
    private int Worth(int from, int to)
    {
        var a = battle.Units[from];
        var d = battle.Units[to];

        int atk = LandUnits.Attack(a.Kind, battle.MightAt(from) + 1,
                                   battle.SkillAt(from, Skill.Sword),
                                   battle.SkillAt(from, Skill.Gunnery),
                                   battle.SkillAt(from, Skill.Shooting));
        int def = LandUnits.Defence(d.Kind, battle.MindAt(to) + 1,
                                    battle.SkillAt(to, Skill.Sword),
                                    battle.SkillAt(to, Skill.Gunnery),
                                    battle.SkillAt(to, Skill.Shooting),
                                    battle.SkillAt(to, Skill.Theology));

        // 공격은 치는 쪽 명령으로, <b>방어는 맞는 쪽 명령</b>으로 굽힌다.
        atk = Bent(atk, OrderAt(from), attacking: true);
        def = Bent(def, OrderAt(to), attacking: false);

        // 춤 겹수는 <b>적 쪽 공격</b>에만 붙는다(0x00448360 이 슬롯 6 이상을 본다).
        if (from >= LandBattle.FirstFoe)
            for (int i = 0; i < Dances; i++) atk = atk * 3 / 2;

        int hurt = def + 1 >= atk ? 1 : (atk - def) / 3;

        switch (LandUnits.Match(LandUnits.KindOf(a.Kind), LandUnits.KindOf(d.Kind)))
        {
            case 0: hurt += dice.Next(4) + 3; break;
            case 2: hurt -= dice.Next(2) + 2; break;
        }
        if (hurt <= 0) hurt = 1;
        return hurt + dice.Next(3);
    }

    /// <summary>
    /// 공격명령이 셈을 굽히는 만큼(<c>0x00448120</c> · <c>0x00448180</c>).
    /// </summary>
    /// <remarks>통상 1.0 · 방어중시 공 0.7 방 1.5 · 돌격 공 1.5 방 0.7 이다.</remarks>
    private static int Bent(int value, int order, bool attacking) => order switch
    {
        LandBattle.Guarded => attacking ? value * 7 / 10 : value * 15 / 10,
        LandBattle.Charge => attacking ? value * 15 / 10 : value * 7 / 10,
        _ => value,
    };

    // ── 목표 고르기 ────────────────────────────────────────────────────────────

    /// <summary>
    /// 그 편에서 노릴 부대 하나 — <b>병사수가 가장 적은</b> 부대다(<c>0x004475E0</c>).
    /// </summary>
    /// <remarks>
    /// 앞열이 살아 있으면 앞열에서만 고른다. 앞열이 다 쓰러지면 뒷열이 앞열이 된다 —
    /// 게임도 고를 것이 없으면 자리를 넓혀 잡는다.
    /// </remarks>
    private int Pick(bool foe, bool frontOnly)
    {
        int best = -1, fewest = int.MaxValue;
        foreach (int at in All(foe, frontOnly))
            if (battle.Units[at].Men < fewest) { fewest = battle.Units[at].Men; best = at; }
        return best;
    }

    /// <summary>그 편에서 노릴 수 있는 부대들.</summary>
    /// <summary>
    /// 그 부대가 노릴 <b>맞은편</b> 자리들. 아군이 치면 적 쪽, 적이 치면 아군 쪽이다.
    /// </summary>
    /// <remarks>
    /// <see cref="All"/> 는 <b>어느 편인지</b>를 받는데(<c>foe</c> 가 참이면 적 쪽),
    /// 부르는 쪽은 <b>치는 쪽이 누구인지</b>를 들고 있다. 그래서 아군이 칠 때
    /// (<paramref name="mine"/> 이 참) 적 쪽을 보려면 <c>foe: true</c> 여야 한다 — 곧
    /// 그대로 넘기면 된다.
    ///
    /// 예전에는 <c>!mine</c> 을 넘겨 <b>제 편을 쳤다</b>. 그래서 아군이 공격해도 피해가
    /// 아군 머리 위에 떴다.
    /// </remarks>
    private IEnumerable<int> Facing(bool mine, bool frontOnly) => All(foe: mine, frontOnly);

    /// <summary>맞은편에서 노릴 부대 하나 — <see cref="Facing"/> 와 같은 셈이다.</summary>
    private int Across(bool mine, bool frontOnly) => Pick(foe: mine, frontOnly);

    /// <summary>그 편의 총대장 부대 자리. 없거나 쓰러졌으면 −1.</summary>
    private int LeaderOf(bool foe)
    {
        int side = foe ? LandBattle.FirstFoe : 0;
        for (int i = side; i < side + LandBattle.PerSide; i++)
            if (Alive(i) && battle.Units[i].IsLeader) return i;
        return -1;
    }

    /// <summary>
    /// 앞열 <b>자리를 굴려</b> 잡는다(<c>0x00447470(편, rand(3))</c>) — 빈 자리면 다시 굴린다.
    /// 앞열이 다 비었으면 −1.
    /// </summary>
    private int FrontByRoll(bool foe)
    {
        int side = foe ? LandBattle.FirstFoe : 0;
        bool any = false;
        for (int i = side; i < side + 3; i++) if (Alive(i)) any = true;
        if (!any) return -1;

        while (true)
        {
            int at = side + dice.Next(3);
            if (Alive(at)) return at;
        }
    }

    /// <summary>
    /// 창병 아닌 근접 부대가 노릴 자리(<c>0x00448900</c>).
    /// </summary>
    /// <remarks>
    /// <code>
    ///   00448971  맞은편 총대장이 앞열이면 rand(5) &lt; 2 (40%) 로 그 대장
    ///   00448998  아니면 rand(5) == 2 (20%) 로 병사수 최소 부대
    ///   004489a7  아니면 rand(5) &lt;= 2 (60%) 로 처음부터 다시
    ///   004489b6  거기서도 떨어지면 앞열 자리를 굴려 잡는다
    /// </code>
    /// </remarks>
    private int MeleeTarget(bool mine)
    {
        bool foe = mine;                       // 치는 쪽이 아군이면 맞은편은 적 쪽이다
        for (int round = 0; round < MeleeRounds; round++)
        {
            int leader = LeaderOf(foe);
            if (leader >= 0 && LandUnits.IsFront(leader) && dice.Next(5) < 2) return leader;
            if (dice.Next(5) == 2) return Pick(foe, frontOnly: true);
            if (dice.Next(5) > 2) break;
        }
        return FrontByRoll(foe);
    }

    /// <summary>되돌이가 끝없이 돌지 않게 두는 끝 — 원본은 확률로 저절로 빠진다.</summary>
    private const int MeleeRounds = 32;

    /// <summary>
    /// 궁병이 노릴 자리(<c>0x00448880</c>) — <c>rand(6)</c> 으로 가른다.
    /// 0 총대장 · 1~4 병사수 최소(앞뒤 다) · 5 아무 부대나(총대장 뺀다).
    /// </summary>
    private int BowTarget(bool mine)
    {
        bool foe = mine;
        return dice.Next(6) switch
        {
            0 => LeaderOf(foe) is var l && l >= 0 ? l : Pick(foe, frontOnly: false),
            5 => Any(mine: !mine, dice, leader: false),
            _ => Pick(foe, frontOnly: false),
        };
    }

    private IEnumerable<int> All(bool foe, bool frontOnly)
    {
        int side = foe ? LandBattle.FirstFoe : 0;
        var front = new List<int>();
        var back = new List<int>();
        for (int i = side; i < side + LandBattle.PerSide; i++)
        {
            if (!Alive(i)) continue;
            (LandUnits.IsFront(i) ? front : back).Add(i);
        }
        if (!frontOnly) return [.. front, .. back];
        return front.Count > 0 ? front : back;
    }

    /// <summary>그 앞열 자리의 뒤에 선 부대 — 창병이 꿰뚫는 자리다.</summary>
    private static int? Behind(int place)
    {
        int side = place < LandBattle.FirstFoe ? 0 : LandBattle.FirstFoe;
        int at = place - side;
        return at < 3 ? side + at + 3 : null;
    }

    private bool Alive(int slot) => battle.Units[slot].Standing;

    private string Name(int slot) =>
        $"{(slot < LandBattle.FirstFoe ? "아군" : "적")} {battle.Units[slot].Name}";

    private void Say(int slot, string text, int sound = -1) =>
        Log(new Line(text, slot, Sound: sound));

    /// <summary>
    /// 싸움이 끝났는지 본다 — <b>대장 부대가 쓰러졌거나</b> 한 쪽이 다 쓰러졌을 때다.
    /// </summary>
    /// <remarks>
    /// 부대 하나를 전멸시키는 <c>0x00447F50</c> 이 첫머리에서 이렇게 한다.
    /// <code>
    ///   00447f62  cmp [부대+0x18], 1        ; 총대장 부대인가
    ///   00447f70  eax = (칸 &lt; 6) ? 4 : 0   ; 아군 대장이면 4(짐) · 적 대장이면 0(이김)
    ///   00447f7b  [+0x3C] = eax             ; 그 자리에서 판이 끝난다
    /// </code>
    /// 곧 <b>제독 부대가 쓰러지면 남은 부대가 성해도 그대로 진다.</b> 돌격은 방어가
    /// 0.7 배라(<c>0x00448180</c>) 대장 부대로 지르면 이 문에 잘 걸린다.
    ///
    /// <b>이 자리에서는 아무 말도 안 낸다</b> — 위 세 줄이 하는 일의 전부이고, 게임은
    /// 「대장이 쓰러졌다」 따위를 따로 알리지 않는다. 쓰러지며 남기는 말은 부대를
    /// 전멸시키는 쪽(<c>0x00446C00</c>)에서 이미 나온 뒤다.
    /// </remarks>
    private void Done()
    {
        if (Over != null) return;

        for (int i = 0; i < LandBattle.Slots; i++)
        {
            var leader = battle.Units[i];
            if (!leader.IsLeader || leader.Standing) continue;
            Over = i >= LandBattle.FirstFoe;
            return;
        }

        bool mine = false, theirs = false;
        for (int i = 0; i < LandBattle.Slots; i++)
        {
            if (!battle.Units[i].Standing) continue;
            if (i < LandBattle.FirstFoe) mine = true; else theirs = true;
        }
        if (!theirs) Over = true;
        else if (!mine) Over = false;
    }
}
