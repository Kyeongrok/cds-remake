using CdsHelper.Support.Local.Models;

namespace CdsHelper.Game.Engine.Land;

/// <summary>
/// 육상전 한 판 — 열두 부대와 턴.
/// </summary>
/// <remarks>
/// 게임의 <c>CLandWar</c>(<c>0x005A47E8</c>)에 맞먹는다. 부대 열둘은 앞 여섯이 아군,
/// 뒤 여섯이 적이고 레코드가 40바이트씩이다(<c>+0xAC</c>부터).
///
/// 판을 세우고 값을 치르는 것이 이 클래스고, 한 턴을 굴리는 것은
/// <see cref="LandFight"/> 다.
///
/// 묘책 넷(<c>0x004490D0</c>) · 일기토(<c>0x004478A0</c>) · 증원 2차전(<c>0x00449930</c>) ·
/// 다 빈치의 작렬탄(<c>0x00448DD0</c>) · 적 AI 의 명령 고르기(<c>0x00447A60</c>)를 다 옮겼다.
/// </remarks>
public sealed class LandBattle
{
    /// <summary>한 쪽 자리 수와 온 자리 수.</summary>
    public const int PerSide = LandRoster.SlotCount, Slots = PerSide * 2;

    /// <summary>적의 첫 자리.</summary>
    public const int FirstFoe = PerSide;

    /// <summary>턴은 열까지다(<c>0x00449C80</c> 의 <c>cmp 턴, 11</c>).</summary>
    public const int LastTurn = 10;

    /// <summary>부대 하나에 드는 최소 인원 — <c>0x004A1200</c> 의 15 다.</summary>
    private const int PerUnit = 15;

    /// <summary>부대 하나.</summary>
    /// <param name="Kind">병종 0~23. −1 이면 빈 자리다.</param>
    /// <param name="Men">병사수.</param>
    public readonly record struct Unit(int Kind, int Men)
    {
        /// <summary>선 부대인지.</summary>
        public bool Standing => Kind >= 0 && Men > 0;

        /// <summary>총대장 부대인지(<c>0x00446E90</c> 의 <c>+0xC4</c>).</summary>
        public bool IsLeader => Kind >= 0 && LandUnits.IsLeader(Kind);

        public string Name =>
            Kind >= 0 && Kind < LandUnits.Names.Length ? LandUnits.Names[Kind] : "";
    }

    private readonly Unit[] _units = new Unit[Slots];

    /// <summary>부대 열둘. 앞 여섯이 아군이다.</summary>
    public IReadOnlyList<Unit> Units => _units;

    /// <summary>지금 턴. 첫 턴이 1 이다.</summary>
    public int Turn { get; private set; } = 1;

    /// <summary>
    /// 그 자리의 <b>지형 부류</b>를 싸움터 그림 번호로 바꾼다(<c>0x0044A624</c>).
    /// </summary>
    /// <remarks>
    /// <code>
    ///   7  → 도시(0, 부르는 쪽이 넘긴 값)
    ///   2  → 초지(1)
    ///   4  → 황무지(3)
    ///   그 밖 → 숲(2)
    /// </code>
    /// </remarks>
    public static int FieldFor(int terrainClass) =>
        terrainClass == 2 ? 1 : terrainClass == 4 ? 3 : 2;

    /// <summary>싸움터 그림 번호 — 0 도시 · 1 초지 · 2 숲 · 3 황무지.</summary>
    public int Terrain { get; }

    /// <summary>상대 도시의 문화권. 적 진형과 그림이 이것으로 갈린다.</summary>
    public int Culture { get; }

    /// <summary>
    /// <b>아군</b> 쪽 문화권 — 제독 나라 수도의 것이다(<c>0x00447070(0)</c>).
    /// </summary>
    /// <remarks>
    /// 쓰러지며 하는 말(<c>0x00446C00</c>)이 <b>쓰러진 쪽</b>의 문화권으로 갈린다.
    /// 부르는 데가 <c>0x00447F7E</c> 인데 <c>0x00446200(자리)</c> 이 <c>자리 &gt;= 6</c> 으로
    /// 편을 내고 그것을 <c>0x00447070</c> 에 넘긴다 — 아군이 쓰러지면 <b>내</b> 문화권이다.
    /// 안 넣어 주면 서유럽(0)으로 둔다.
    /// </remarks>
    public int MyCulture { get; init; }

    /// <summary>그 편의 문화권(<c>0x00447070(편)</c>).</summary>
    public int CultureOfSide(bool foe) => foe ? Culture : MyCulture;

    /// <summary>적 대장의 능력 — 무력 · 지력 · 운 · 체력(<c>0x00449E50</c>).</summary>
    public int FoeMight { get; }
    public int FoeMind { get; }
    public int FoeLuck { get; }
    public int FoeBody { get; }

    /// <summary>
    /// 판을 세운다.
    /// </summary>
    /// <param name="mine">아군 여섯 자리의 병종. −1 이면 빈 자리다.</param>
    /// <param name="scale">도시 규모(<c>도시 +0x08</c>). 적의 크기가 여기서 나온다.</param>
    /// <param name="myMen">
    /// 아군 병력. 0 이하면 <b>제독의 선원 + 1</b> 이다(<c>0x0044A7CB</c>).
    /// 모의전에서만 따로 준다.
    /// </param>
    /// <param name="mock">모의전이면 참 — 값을 안 치른다.</param>
    public LandBattle(IReadOnlyList<int> mine, Player player, Player.MateInfo? aide,
                      int scale, int nation, int culture, int terrain, GameRandom dice,
                      int myMen = 0, bool mock = false, int city = -1)
    {
        City = city;
        Nation = nation;
        Culture = culture;
        Terrain = Math.Clamp(terrain, 0, 3);
        Scale = Math.Max(0, scale);
        _me = player;
        _aide = aide;
        _mock = mock;

        int men = myMen > 0 ? myMen : player.Crew + 1;
        MyFirst = men;
        Split(mine, men);

        // 적 대장의 능력. 규모 0 / 1~2 / 3~4 / 5+ 마다 밑값이 다르다.
        int band = scale <= 0 ? 0 : scale <= 2 ? 1 : scale <= 4 ? 2 : 3;
        FoeMight = dice.Next(10) + new[] { 40, 60, 75, 90 }[band] - 1;
        FoeMind = dice.Next(10) + new[] { 30, 50, 70, 80 }[band] - 1;
        FoeLuck = dice.Next(10) + new[] { 20, 40, 65, 70 }[band] - 1;
        FoeBody = dice.Next(10) + new[] { 60, 75, 85, 90 }[band] - 1;

        Muster(scale, dice);
        for (int i = FirstFoe; i < Slots; i++) FoeFirst += _units[i].Men;
        FoeRoom = FoeUnits > 0 ? FoeFirst / FoeUnits : FoeFirst;
        MyRoom = MyUnits > 0 ? MyFirst / MyUnits : MyFirst;
    }

    /// <summary>
    /// <b>모의전</b> 판을 세운다 — 양쪽 병종과 병력수를 그대로 받는다.
    /// </summary>
    /// <remarks>
    /// 도시도 나라도 없이 싸움만 돌려 보는 자리다. 그래서 적을 문화권으로 지어내지
    /// 않고(<c>Muster</c>) 받은 대로 세우고, 작렬탄도 안 준다. 적 대장의 능력만
    /// <paramref name="scale"/> 로 굴린다 — 싸움 셈이 그것을 본다.
    /// </remarks>
    /// <param name="mine">아군 여섯 자리의 병종. −1 이면 빈 자리다.</param>
    /// <param name="theirs">적 여섯 자리의 병종.</param>
    /// <param name="myMen">아군 병력 합.</param>
    /// <param name="foeMen">적 병력 합.</param>
    public LandBattle(IReadOnlyList<int> mine, IReadOnlyList<int> theirs,
                      int myMen, int foeMen, Player player, Player.MateInfo? aide,
                      int culture, int terrain, GameRandom dice, int sort = Town)
    {
        Sort = sort == Field ? Field : Town;
        Nation = -1;
        Culture = culture;
        Terrain = Math.Clamp(terrain, 0, 3);
        Scale = 3;
        _me = player;
        _aide = aide;
        _mock = true;

        MyFirst = Math.Max(1, myMen);
        Split(mine, MyFirst);
        Fill(theirs, Math.Max(1, foeMen));

        FoeMight = dice.Next(10) + 75 - 1;
        FoeMind = dice.Next(10) + 70 - 1;
        FoeLuck = dice.Next(10) + 65 - 1;
        FoeBody = dice.Next(10) + 85 - 1;

        for (int i = FirstFoe; i < Slots; i++) FoeFirst += _units[i].Men;
        FoeRoom = FoeUnits > 0 ? FoeFirst / FoeUnits : FoeFirst;
        MyRoom = MyUnits > 0 ? MyFirst / MyUnits : MyFirst;
    }

    /// <summary>
    /// <b>발견 대본</b>의 육상전 판을 세운다 — <c>2F 0D [인물]</c> 이다(갈래 3).
    /// </summary>
    /// <remarks>
    /// 해석기가 <c>0x0044AA30(3, 아군, 적 대장, 0, 지형)</c> 을 부른다. 도시가 없으므로
    /// 적을 규모로 짓지 않는다(<c>0x00449E50</c> 은 갈래 2·4 만). 대신
    /// <code>
    ///   0x004A04F0  적 총원 = 적 대장 묶음 +0x0C + 1      ; 대본의 26 1C 10 이 채운다
    ///   0x004A1200  부대 수를 굴림으로 깎고
    ///   0x004A1320  적 대장 나라 수도의 문화권(0x00447070)으로 진형을 고른다
    /// </code>
    /// 적 대장 능력은 그 인물의 것이다(<c>0x00446FBC</c>). 도시 규모는 없으니 0 이다.
    /// </remarks>
    /// <param name="mine">아군 여섯 자리의 병종.</param>
    /// <param name="myMen">아군 총원(묶음 +0x0C + 1, <c>0x0044A7C8</c>).</param>
    /// <param name="foeMen">적 총원.</param>
    /// <param name="foe">적 대장의 무력 · 지력 · 운 · 체력.</param>
    /// <param name="foeSkills">
    /// 적 대장의 실제 기능(검술 · 포술 · 사격술). 편성을 <b>이 생성자 안에서</b> 짓기 때문에(<c>Deal</c>)
    /// 초기화 식으로 나중에 넣으면 늦는다 — 여기서 받는다.
    /// </param>
    /// <param name="sort">
    /// 판의 갈래. 대본은 <see cref="Script"/>, <b>뭍에서 마주친 무리</b>는 <see cref="Field"/> 다 —
    /// 들싸움은 판이 끝날 때까지 버티면 이긴 것으로 친다(<see cref="TimeUpWon"/>).
    /// </param>
    public LandBattle(IReadOnlyList<int> mine, int myMen, int foeMen, Player player,
                      Player.MateInfo? aide, int culture, int terrain,
                      (int Might, int Mind, int Luck, int Body) foe, GameRandom dice,
                      (int Sword, int Gunnery, int Shooting)? foeSkills = null, int sort = Script,
                      int scale = 0, int nation = -1)
    {
        FoeSkills = foeSkills;
        Sort = sort == Field ? Field : Script;
        Nation = nation;
        Culture = culture;
        Terrain = Math.Clamp(terrain, 0, 3);
        // 들싸움·대본 판의 규모는 <b>적 대장 나라의 수도</b> 규모다(0x004494B3 — 갈래 2·4 만 그 도시를 쓴다).
        Scale = Math.Max(0, scale);
        _me = player;
        _aide = aide;

        MyFirst = Math.Max(1, myMen);
        Split(mine, MyFirst);

        FoeMight = foe.Might;
        FoeMind = foe.Mind;
        FoeLuck = foe.Luck;
        FoeBody = foe.Body;

        Deal(Math.Max(1, foeMen), dice);
        for (int i = FirstFoe; i < Slots; i++) FoeFirst += _units[i].Men;
        FoeRoom = FoeUnits > 0 ? FoeFirst / FoeUnits : FoeFirst;
        MyRoom = MyUnits > 0 ? MyFirst / MyUnits : MyFirst;
    }

    /// <summary>
    /// 아군이 <b>빌린 병력</b>인지 — 참이면 끝나고 제독의 선원 수를 안 고친다.
    /// </summary>
    /// <remarks>
    /// 대본이 <c>26 1C 02 [n]</c> 으로 임시 묶음(<c>[ebp-0xB0]</c>)을 세우면 싸움은 그 묶음으로
    /// 한다. 부상병 복귀(<c>0x004495D5</c>)도 그 묶음의 <c>+0x0C</c> 에만 적히므로(<c>0x0045FF40</c>)
    /// 함대의 선원은 그대로다.
    /// </remarks>
    public bool KeepsCrew { get; init; }

    /// <summary>
    /// 아군이 <b>다 쓰러져</b> 끝났는지. 물러난 것과 다르다.
    /// </summary>
    /// <remarks>
    /// 게임은 이때 <c>0x00449908</c> 에서 바로 게임 오버(<c>0x0044AF40(3)</c>)를 건다 — 들싸움만
    /// 「적이 봐 준」 문이 있다. 판이 이것을 적어 두면 발견 대본이 보고 멈춘다.
    /// </remarks>
    public bool Wiped { get; internal set; }

    /// <summary>적 여섯 자리를 받은 대로 세우고 병력을 고르게 나눈다.</summary>
    private void Fill(IReadOnlyList<int> theirs, int men)
    {
        int units = 0;
        for (int i = 0; i < PerSide && i < theirs.Count; i++) if (theirs[i] >= 0) units++;
        if (units == 0) return;

        int each = Math.Max(1, men / units), over = men % units;
        bool first = true;
        for (int i = 0; i < PerSide && i < theirs.Count; i++)
        {
            if (theirs[i] < 0) continue;
            _units[FirstFoe + i] = new Unit(theirs[i], each + (first ? over : 0));
            first = false;
        }
    }

    /// <summary>모의전인지 — 이기고 져도 값을 안 치른다.</summary>
    public bool IsMock => _mock;

    private readonly bool _mock;

    private readonly Player _me;
    private readonly Player.MateInfo? _aide;

    /// <summary>도시 규모. 전리품 셈이 이것을 본다.</summary>
    public int Scale { get; }

    /// <summary>도시가 딸린 나라. 증원이 붙을지를 이것도 본다.</summary>
    public int Nation { get; }

    // ── 증원 2차전 — 0x00449930 ────────────────────────────────────────────────

    /// <summary>증원이 붙는 나라(<c>0x00449956</c> 의 <c>cmp eax, 7</c>).</summary>
    private const int ReinforcingNation = 7;

    /// <summary>증원이 붙는 도시 규모.</summary>
    private const int ReinforcingScale = 3;

    /// <summary>이미 한 번 붙었는지(<c>+0x88</c>). 한 판에 딱 한 번이다.</summary>
    private bool _reinforced;

    /// <summary>치는 도시 번호 — 증원이 붙는지를 이것으로 가른다(<c>0x0044993C</c>의 <c>0x00429D20</c>). 모르면 −1.</summary>
    public int City { get; } = -1;

    /// <summary>
    /// 증원이 왔을 때 나오는 말 — 리스본·세빌리아면 첫 줄(<c>0x0056D130</c>), 그 밖이면 셋 가운데 굴린다
    /// (<c>0x00446DF0</c> 의 <c>0x00549CC8</c>). 모두 부관(없으면 뱃사람) 얼굴로 나온다.
    /// </summary>
    public string ReinforceWordFor(GameRandom dice) =>
        City is ReinforcingCityA or ReinforcingCityB
            ? "제독, 원군입니다!\n아니! 보십시오! 후방에도 증원부대가 대기하고\n있습니다! 이래서는 승산이 없습니다! 퇴각합시다···"
            : dice.Next(3) switch
            {
                0 => "제독, 적의 새 병력입니다!",
                1 => "안심할 때가 아닙니다!\n적의 증원부대입니다!",
                _ => "제독, 조심하십시오!\n적의 원군입니다!",
            };

    /// <summary>
    /// 이겼을 때 적의 새 병력이 붙는지 — <b>마을 공략에서 딱 한 번</b>이다.
    /// </summary>
    /// <remarks>
    /// 규모가 셋 위면 늘 붙고, 작으면 <b>리스본(0)·세빌리아(7)</b>에서만 붙는다(<c>0x0044993C</c> 가 도시 번호를 본다).
    /// 붙어도 <b>턴은 그대로</b> 간다 — <c>0x00449930</c> 은 깃발만 세우고 돌아가며 턴 칸을 안 건드린다.
    /// </remarks>
    public bool Reinforce(GameRandom dice)
    {
        if (_reinforced) return false;
        if (Sort != Town) return false;     // 0x00449930 이 갈래 2 부터 본다
        if (Scale < ReinforcingScale && City is not (ReinforcingCityA or ReinforcingCityB)) return false;

        _reinforced = true;
        for (int i = FirstFoe; i < Slots; i++) _units[i] = default;

        Muster(Scale, dice);
        // 적 처음 인원은 새 편성으로 <b>다시</b> 센다(0x004A1320 — 더하지 않는다).
        FoeFirst = MenOn(foe: true);
        FoeRoom = FoeUnits > 0 ? MenOn(foe: true) / FoeUnits : FoeFirst;
        return true;
    }

    /// <summary>규모가 작아도 증원이 붙는 도시 — 리스본과 세빌리아(<c>0x0044993C</c>).</summary>
    private const int ReinforcingCityA = 0, ReinforcingCityB = 7;

    /// <summary>판을 열 때의 인원.</summary>
    public int MyFirst { get; }
    public int FoeFirst { get; private set; }

    /// <summary>부대 하나의 정원 — 고승의 회복이 이것을 본다(<c>0x00448280</c>).</summary>
    public int MyRoom { get; private set; }
    public int FoeRoom { get; private set; }

    private int MyUnits => Standing(0);
    private int FoeUnits => Standing(FirstFoe);

    private int Standing(int side)
    {
        int n = 0;
        for (int i = side; i < side + PerSide; i++) if (_units[i].Standing) n++;
        return n;
    }

    /// <summary>지금 그 편에 남은 병사수.</summary>
    public int MenOn(bool foe)
    {
        int side = foe ? FirstFoe : 0, n = 0;
        for (int i = side; i < side + PerSide; i++) n += Math.Max(0, _units[i].Men);
        return n;
    }

    /// <summary>부대 하나의 정원.</summary>
    public int RoomPerUnit(int side) => side >= FirstFoe ? FoeRoom : MyRoom;

    /// <summary>그 부대의 병사수를 고쳐 넣는다. 0 이 되면 쓰러진 것이다.</summary>
    public void SetMen(int slot, int men)
    {
        if (slot < 0 || slot >= Slots) return;
        _units[slot] = _units[slot] with { Men = Math.Max(0, men) };
    }

    /// <summary>
    /// 그 자리 부대가 쓰는 <b>기능</b> 자리(0~3).
    /// </summary>
    /// <remarks>
    /// 아군은 제독과 부관 중 큰 쪽이고(<c>0x00446F70</c>), 적은 대장 인물을 아직 안
    /// 들고 있어 능력에서 어림한다.
    /// </remarks>
    public int SkillAt(int slot, int skill)
    {
        if (slot >= FirstFoe) return FoeSkill(skill);

        int mine = _me.LevelOf(Skill.Names[skill]);
        int mate = _aide is not { } who ? 0 : skill switch
        {
            Skill.Sword => who.Sword,
            Skill.Shooting => who.Shooting,
            Skill.Gunnery => who.Gunnery,
            _ => 0,
        };
        return Math.Max(mine, mate);
    }

    /// <summary>
    /// 그 자리 부대의 무력 — 아군은 <b>제독과 부관 가운데 큰 쪽 + 1</b> 이다
    /// (<c>0x00446FF0</c>, 기능과 같은 규칙이다).
    /// </summary>
    public int MightAt(int slot) =>
        slot >= FirstFoe ? FoeMight
        : Math.Max(_me.AbilityOf(Ability.Might), _aide?.Might ?? 0) + 1;

    /// <summary>그 자리 부대의 지력 — 아군은 제독과 부관 가운데 큰 쪽 + 1 이다(<c>0x00446FF0</c>).</summary>
    public int MindAt(int slot) =>
        slot >= FirstFoe ? FoeMind
        : Math.Max(_me.AbilityOf(Ability.Mind), _aide?.Mind ?? 0) + 1;

    // ── 아군 ───────────────────────────────────────────────────────────────────

    /// <summary>
    /// 아군 인원을 나눈다 — 고르게 나누고 <b>나머지는 대장 부대</b>가 갖는다.
    /// </summary>
    /// <remarks><c>0x0049F640</c> 이 배치 화면에서 하던 그 셈 그대로다.</remarks>
    private void Split(IReadOnlyList<int> mine, int men)
    {
        int units = 0;
        for (int i = 0; i < PerSide && i < mine.Count; i++) if (mine[i] >= 0) units++;
        if (units == 0) return;

        int each = Math.Max(1, men / units), over = men % units;
        for (int i = 0; i < PerSide && i < mine.Count; i++)
        {
            if (mine[i] < 0) continue;
            _units[i] = new Unit(mine[i], each + (LandUnits.IsLeader(mine[i]) ? over : 0));
        }
    }

    // ── 적 ─────────────────────────────────────────────────────────────────────

    /// <summary>
    /// 적을 그 자리에서 지어낸다.
    /// </summary>
    /// <remarks>
    /// <code>
    ///   0x004A11D0  총 병사수 = 50 x 규모² + 100 + rand(50), 규모가 2 이하면 곱하기 2
    ///   0x004A1200  부대 수  = clamp(총인원 / 15, 1, 6) 에서 굴림으로 깎는다
    ///   0x004A1320  문화권으로 진형 여덟 중 하나를 고른다
    ///   0x004A04B0  인원을 고르게 나누고 나머지는 <b>첫 부대(대장)</b>가 갖는다
    /// </code>
    /// </remarks>
    private void Muster(int scale, GameRandom dice)
    {
        int men = 50 * scale * scale + 100 + dice.Next(50);
        if (scale <= 2) men *= 2;
        Deal(men, dice);
    }

    /// <summary>총원으로 부대 수를 정하고 진형대로 나눠 세운다(<c>0x004A0530</c>).</summary>
    private void Deal(int men, GameRandom dice)
    {
        int units = Shrink(Math.Clamp(men / PerUnit, 1, PerSide), dice);
        var kinds = Formation(units, dice);

        int each = Math.Max(1, men / units), over = men % units;
        for (int i = 0; i < units && i < PerSide; i++)
            _units[FirstFoe + i] = new Unit(kinds[i], each + (i == 0 ? over : 0));
    }

    /// <summary>
    /// 부대 수를 굴림으로 깎는다(<c>0x004A1200</c>).
    /// </summary>
    /// <remarks>
    /// 그대로 둘 확률이 70%, 하나 깎을 확률이 60%… 스무 번째까지 다 빗나가면 처음부터
    /// 다시 굴린다. 그래서 여섯 부대가 다 나오는 판은 드물다.
    /// </remarks>
    private static int Shrink(int units, GameRandom dice)
    {
        int[] odds = [70, 60, 50, 40, 30, 20];
        for (int guard = 0; guard < 100; guard++)
            for (int cut = 0; cut < odds.Length; cut++)
            {
                if (units - cut <= 0) break;
                if (dice.Next(100) <= odds[cut]) return units - cut;
            }
        return Math.Max(1, units);
    }

    /// <summary>
    /// 그 문화권의 진형이 낼 병종들. 세트 자체는 <see cref="LandFormations"/> 에 있다.
    /// </summary>
    /// <remarks>
    /// 놀이도 고치는 창(<c>LandFormationDialog</c>)도 같은 표를 본다 — 사람이 편성을
    /// 갈아 두면 여기에도 그대로 먹는다.
    /// </remarks>
    private int[] Formation(int units, GameRandom dice) =>
        LandFormations.Muster(LandFormations.ShapeOf(Culture), units,
                              FoeSkill(Skill.Sword), FoeSkill(Skill.Gunnery),
                              FoeSkill(Skill.Shooting), dice);

    /// <summary>
    /// 적 대장의 기능 자리 — 검술·포술·사격술이 <b>셋 다 같은 값</b>이다.
    /// </summary>
    /// <remarks>
    /// 판을 세울 때 <b>도시 규모</b>로 매긴다(<c>0x00449FBB</c>): 0 → 0 · 1~2 → 1 ·
    /// 3~4 → 2 · 5 이상 → 3 을 <c>+0x48</c>(검술) · <c>+0x4C</c> · <c>+0x50</c> 에 똑같이 적고,
    /// 싸움은 <c>0x00446F70</c> 으로 그 값을 그대로 읽는다. 능력치에서 어림하던 것은 틀렸다.
    /// </remarks>
    private int FoeSkill(int slot)
    {
        // 적 대장 인물을 알면(발견 대본의 2F 0D [인물]) 그 사람의 기능 자리를 그대로 본다.
        // 파르테논 신전의 206번에게 사격술 3 을 주면 화승총병이 아니라 머스켓총병이 선다.
        if (FoeSkills is { } known)
            return Math.Clamp(slot switch
            {
                Skill.Sword => known.Sword,
                Skill.Gunnery => known.Gunnery,
                Skill.Shooting => known.Shooting,
                _ => 0,
            }, 0, Skill.MaxLevel);

        return Scale <= 0 ? 0 : Scale <= 2 ? 1 : Scale <= 4 ? 2 : 3;
    }

    /// <summary>
    /// 적 대장의 실제 기능(검술 · 포술 · 사격술). 인물을 아는 판만 준다 — 없으면 능력에서 어림한다.
    /// </summary>
    /// <remarks>게임은 <c>0x00446F70(기능, 6)</c> 으로 적 대장 인물 레코드를 본다.</remarks>
    public (int Sword, int Gunnery, int Shooting)? FoeSkills { get; init; }

    /// <summary>적장 얼굴 — 몰살한 뒤 봐 줄 때 적장이 한마디 한다(<c>0x00446DD9</c> 가 <c>[+0x9C]</c> 의 얼굴을 쓴다).</summary>
    public uint[]? FoeFace { get; init; }

    // ── 끝맺음 — 0x00449870 ────────────────────────────────────────────────────

    /// <summary>싸움이 끝나고 치르는 값.</summary>
    /// <param name="Loot">전리품 금화(<c>0x00449490</c>).</param>
    /// <param name="Back">복귀한 부상병(<c>0x00449570</c>).</param>
    /// <param name="Fame">오른 명성 · <paramref name="Infamy"/> 오른 악명(<c>0x00449600</c>).</param>
    /// <param name="Might">오른 무력(<c>0x00449730</c>).</param>
    public readonly record struct Spoils(int Loot, int Back, int Fame, int Infamy, int Might);

    /// <summary>
    /// 이기거나 물러난 뒤를 치른다.
    /// </summary>
    /// <remarks>
    /// <code>
    ///   전리품  ((적 처음 - 적 남은) x (규모+1)) / 10 + rand(50)
    ///           열 턴을 버텨 이긴 판이면 x (6 - 살아남은 적 부대 수) x 2 / 10       0x0044950A
    ///   복귀    율 = (제독·부관 가운데 높은 운 + 1)*5/100 + 높은 의학*2             0x00449585
    ///           min(처음 - 1, 생존 + 율*(처음 - 생존 - 1)/10)
    ///   명성·악명 갈래마다 밑값이 다르다(뜀표 0x0044971C)
    ///           0 명성 50 · 악명 150 / 1 들 60 · 0 / 2 마을 100 · 200 / 3 대본 50 · 없음 / 4 100 · 없음
    ///           그 뒤 적 나라 == 내 나라면 악명 +100, 아니면 명성 +10. 갈래 3·4 는 악명을 아예 안 센다.
    ///   무력    <b>적을 몰살했을 때만</b>(상태 0) rand(20) == 0 이면 rand(2)+1, 마을 공략이면 +1  0x00449730
    /// </code>
    /// </remarks>
    /// <param name="heldTenTurns">열 턴을 버텨 이긴 판인지(상태 1) — 전리품이 깎이고 무력은 안 오른다.</param>
    public Spoils Finish(bool won, GameRandom dice, bool heldTenTurns = false)
    {
        int loot = won ? (FoeFirst - MenOn(foe: true)) * (Scale + 1) / 10 + dice.Next(50) : 0;
        if (won && heldTenTurns) loot = (6 - UnitsAlive(foe: true)) * loot * 2 / 10;

        int alive = Math.Max(0, MenOn(foe: false) - 1);
        int luck = Math.Max(_me.AbilityOf(Ability.Luck), _aide?.Luck ?? 0) + 1;
        int medicine = Math.Max(_me.LevelOf(Skill.Names[Skill.Medicine]), _aide?.Medicine ?? 0);
        int rate = luck * 5 / 100 + medicine * 2;
        int back = Math.Min(MyFirst - 1, alive + rate * (MyFirst - alive - 1) / 10) - alive;
        back = Math.Max(0, back);

        var (fameBase, infamyBase) = Sort switch
        {
            Field => (60, 0),
            Script => (50, 0),
            _ => (100, 200),
        };
        bool countsInfamy = Sort != Script;         // 갈래 3·4 는 악명을 건너뛴다(0x004496DA)
        bool same = Nation >= 0 && Nation == _me.Nation;

        int fame = won ? fameBase + (same ? 0 : 10) + dice.Next(11) : dice.Next(11);
        int infamy = !countsInfamy ? 0
                   : won ? infamyBase + (same ? 100 : 0)
                   : 300;

        int might = won && !heldTenTurns && dice.Next(20) == 0
                    ? dice.Next(2) + 1 + (Sort == Town ? 1 : 0) : 0;

        return new Spoils(loot, back, fame, infamy, might);
    }

    /// <summary>그 편에 아직 선 부대 수(<c>0x00447530</c>).</summary>
    public int UnitsAlive(bool foe)
    {
        int side = foe ? FirstFoe : 0, n = 0;
        for (int i = side; i < side + PerSide; i++) if (_units[i].Men > 0) n++;
        return n;
    }

    // ── 턴 ─────────────────────────────────────────────────────────────────────

    /// <summary>턴 알림 글 — <c>0x0056D7B0</c> "제%2d턴" 이다.</summary>
    public string TurnWord => $"제{Turn,2}턴";

    /// <summary>다음 턴으로. 열 턴이 지나면 거짓.</summary>
    public bool NextTurn() => ++Turn <= LastTurn;

    /// <summary>
    /// 이번 턴에 <b>공격명령을 묻는가</b> — <b>들싸움 첫 턴</b>에는 안 묻는다(<c>0x00449C00</c>).
    /// </summary>
    /// <remarks>
    /// 차림표를 여는 <c>0x00449BA0</c> 이 마지막에 턴과 갈래를 본다.
    /// <code>
    ///   00449c00  cmp dword ptr [ebp + 8], 1      ; 턴(+0x00)이 1 이고
    ///   00449c06  cmp dword ptr [esi + 0x34], 1   ; 갈래가 1(들에서 마주친 부대)이면
    ///   00449c0c  mov dword ptr [esi + 0x90], 0   ; 명령을 통상공격으로 두고
    ///   00449c24  ret 0xc                         ; 차림표를 안 열고 나간다
    /// </code>
    /// 「제N턴」 알림은 그 앞(<c>0x00449D29</c>)이라 그대로 뜬다. 마을 공략(2)·대본(3)은
    /// 첫 턴에도 묻는다.
    /// </remarks>
    public bool AsksOrder => !(Turn == 1 && Sort == Field);

    /// <summary>
    /// 열 턴을 넘겼을 때 <b>이긴 것으로 치는가</b>(<c>0x00449420</c>).
    /// </summary>
    /// <remarks>
    /// <c>0x00449432</c> 가 갈래(<c>+0x34</c>)를 1 과 견주어, 들에서 마주친 부대면
    /// <c>+0x3C</c> 에 1(이김)을, 그 밖이면 2(물러남)를 적는다. 곧 <b>마을 공략은 열 턴
    /// 안에 못 끝내면 이길 길이 없고</b>, 들싸움은 열 턴을 버티면 적이 물러간다.
    /// </remarks>
    public bool TimeUpWon => Sort == Field;

    /// <summary><b>열 턴을 넘겼을 때</b> 부관이 하는 말(<c>0x00449420</c>).</summary>
    public string TimeUpWord(GameRandom dice)
    {
        var words = TimeUpWon ? HeldOutWords : TimeUpWords;
        return words[dice.Next(words.Length)];
    }

    /// <summary>열 턴을 넘겨 물러날 때 나오는 셋(<c>0x00549D08</c>).</summary>
    private static readonly string[] TimeUpWords =
    [
        "사기가 떨어지고 있습니다. 일단 퇴각합시다.",
        "제독, 더 이상 싸워도 소용없습니다. 퇴각합시다.",
        "싸움을 너무 오래 끈 것 같습니다. 포기하고 퇴각합시다.",
    ];

    /// <summary>들싸움에서 열 턴을 버텨 냈을 때 나오는 셋(<c>0x00549CF8</c>).</summary>
    private static readonly string[] HeldOutWords =
    [
        "적이 도망가고 있습니다. 분투한 결과입니다.",
        "저희들의 실력에 겁먹었는지 퇴각해 버렸습니다.",
        "적은 포기한 것 같습니다. 퇴각해 버렸습니다.",
    ];

    /// <summary>
    /// 다 빈치의 작렬탄(<c>0x00448DD0</c>) — 판이 열릴 때 한 번 굴린다.
    /// </summary>
    /// <remarks>
    /// <b>판을 열 때 딱 한 번</b> 굴린다(<c>0x00449C9E</c> — 턴 되돌이 <b>앞</b>이다). 살아 있는
    /// <b>포 부대</b>가 아군에 있고 아이템 2 를 지녔을 때, 40%로 <c>0x0056D340</c>
    /// 「다 빈치 선생의 작렬탄을 받아라!」가 뜨고 그 아이템이 없어진다.
    /// 받아도 <b>첫 턴만</b> 간다(<see cref="ShellSpent"/>).
    /// </remarks>
    public const int ShellItem = 2, ShellOdds = 40;

    /// <summary>
    /// 전투 갈래(<c>+0x34</c>) — <b>2 마을 공략 · 1 들에서 마주친 부대</b>다.
    /// </summary>
    /// <remarks>
    /// <c>0x0044AA30</c> 을 부르는 데가 넷이고 그 첫 인자가 이것이다. 갈래마다 갈리는
    /// 것이 여럿인데, 우리 판이 보는 것은 <b>열 턴을 넘겼을 때</b>다
    /// (<see cref="TimeUpWon"/>). 그 밖에 게임은 일기토 문(<c>0x004479B0</c>)과 증원
    /// (<c>0x00449930</c>)도 갈래로 가른다.
    /// </remarks>
    public int Sort { get; } = Town;

    /// <summary>전투 갈래 — 들에서 마주친 부대 · 마을 공략 · 발견 대본의 인물전(<c>2F 0D</c>)이다.</summary>
    public const int Field = 1, Town = 2, Script = 3;

    /// <summary>작렬탄을 받았는지. 서 있으면 포가 비를 안 타고 두 번 쏜다.</summary>
    public bool Shells { get; private set; }

    /// <summary>
    /// 한 턴이 굴렀으니 작렬탄을 내린다(<c>0x00449DE3</c>).
    /// </summary>
    /// <remarks>
    /// <c>+0x3C</c> 의 <c>0x40</c> 이 작렬탄이고 <c>0x08</c> 이 「판이 이어진다」다. 턴이
    /// 굴러간 뒤 둘이 다 서 있으면 <c>0x48</c> 을 뒤집고 <c>0x08</c> 만 도로 세운다 — 곧
    /// <b>작렬탄만 지운다</b>. 판을 열 때 한 번 굴리므로 사실상 <b>첫 턴에만</b> 듣는다.
    /// </remarks>
    public void ShellSpent() => Shells = false;

    /// <summary>작렬탄을 받았을 때 나오는 말(<c>0x0056D340</c>). 안 받았으면 빈 글.</summary>
    public string ShellWord { get; private set; } = "";

    /// <summary>
    /// 작렬탄을 굴린다(<c>0x00448DD0</c>) — <b>판을 열 때 한 번</b>이다.
    /// </summary>
    /// <remarks>
    /// 살아 있는 <b>포 부대</b>(총대장 부대는 안 센다, <c>0x00447580(2, 0)</c>)가 있어야 하고, 아이템을
    /// 지녀야 하며, <c>rand(100) &lt; 40</c> 이라야 받는다. 받으면 그 아이템이 없어진다(<c>0x0047CDB0</c>).
    /// </remarks>
    /// <returns>받았으면 참 — 그때만 말이 나온다.</returns>
    public bool TryShell(Player player, GameRandom dice)
    {
        if (Shells || !player.HasItem(ShellItem)) return false;
        if (!HasCannon(foe: false)) return false;
        if (dice.Next(100) >= ShellOdds) return false;

        player.Drop(ShellItem);
        Shells = true;
        ShellWord = "다 빈치 선생의 작렬탄을 받아라!";
        return true;
    }

    /// <summary>그 편에 살아 있는 포 부대가 있는지(<c>0x00447580(2, 편)</c>) — 총대장 부대는 안 센다.</summary>
    public bool HasCannon(bool foe)
    {
        int side = foe ? FirstFoe : 0;
        for (int i = side; i < side + PerSide; i++)
            if (_units[i].Standing && !_units[i].IsLeader
                && LandUnits.KindOf(_units[i].Kind) == LandUnits.Kind.Cannon) return true;
        return false;
    }

    // ── 적 AI — 0x00447A60 ─────────────────────────────────────────────────────

    /// <summary>
    /// 전투 갈래(<c>+0x34</c>). 마을 공략이 <b>2</b> 다.
    /// </summary>
    public const int VillageRaid = 2;

    /// <summary>
    /// 적이 고르는 공격명령(<c>0x00447A60</c>).
    /// </summary>
    /// <remarks>
    /// 양쪽 병사수를 견주고 턴이 여덟을 넘었는지로 갈린다. <b>턴이 여덟보다 이르면 갈래를
    /// 안 본다</b> — 들싸움이냐 아니냐만 가른다.
    /// <code>
    ///   턴 &lt;  8   들싸움 첫 턴이면 늘 통상                       0x00447B69
    ///             들싸움  이기면 rand(9) &gt; 3 ? 돌격 : 통상       지면 rand(5) &gt; 3 ? 돌격 : 통상
    ///             그 밖   이기면 rand(7) &gt; 3 ? 돌격 : 통상       지면 rand(7) &gt; 2 ? 방어중시 : 통상
    ///   턴 &gt;= 8   이기면  갈래 0·2 rand(4) != 0 ? 방어중시 : 통상
    ///                     갈래 1   rand(5) != 0 ? 돌격     : 통상
    ///                     갈래 3·4 <b>명령을 안 바꾼다</b>(0x00447C1C 로 바로 나간다)
    ///             지면    갈래 1   rand(4) == 0 ? 퇴각     : 통상
    ///                     갈래 2·4 rand(9) != 0 ? 방어중시 : 통상
    ///                     갈래 3   명령을 안 바꾼다
    /// </code>
    /// <b>함대전 갈래(4)는 따로 옮길 것이 없다.</b> 지고 있을 때는 마을 공략(2)과 셈이 같고,
    /// 이기고 있을 때는 명령을 아예 안 바꾼다. 우리 쪽에는 그 판이 없기도 하다.
    ///
    /// <b>죽은 가지 하나</b> — <c>0x00447B14</c> 가 <c>갈래 != 0</c> 이면 뛰고 나서 <c>갈래 == 3</c>
    /// 을 보는데, 거기 닿았을 때 갈래는 반드시 0 이라 절대 안 걸린다.
    ///
    /// <b>적은 마을 공략에서 일기토를 안 건다</b>(<c>0x004479D7</c> 이 갈래 2·4 를
    /// 먼저 걸러 낸다). 퇴각도 갈래 1 에서만 고른다.
    /// </remarks>
    public int FoeOrder(GameRandom dice)
    {
        bool ahead = MenOn(foe: true) >= MenOn(foe: false);
        bool field = Sort == Field;                    // 들에서 마주친 부대는 셈이 다르다

        if (Turn >= LateTurn)
        {
            if (field)
                return ahead ? (dice.Next(5) != 0 ? Charge : Normal)
                             : (dice.Next(4) == 0 ? Retreat : Normal);
            // 마을 공략은 앞서면 지키고, 밀리면 아홉에 여덟으로 지킨다.
            return dice.Next(ahead ? 4 : 9) != 0 ? Guarded : Normal;
        }

        // 들싸움 첫 턴은 그냥 친다(0x00447AA5).
        if (Turn == 1 && field) return Normal;

        if (field)
            return ahead ? (dice.Next(9) > 3 ? Charge : Normal)
                         : (dice.Next(5) > 3 ? Charge : Normal);

        return ahead ? (dice.Next(7) > 3 ? Charge : Normal)
                     : (dice.Next(7) > 2 ? Guarded : Normal);
    }

    /// <summary>적이 셈을 바꾸는 턴(<c>0x00447A7C</c> 의 <c>cmp 턴, 8</c>).</summary>
    private const int LateTurn = 8;

    // ── 묘책 — 0x004490D0 ──────────────────────────────────────────────────────

    /// <summary>묘책 넷의 이름(<c>0x0056D438</c>). 제목은 「기습명령」이다.</summary>
    public static readonly string[] Ruses = ["기습", "함정", "암살자", "심판"];

    /// <summary>묘책 번호.</summary>
    public const int Ambush = 0, Trap = 1, Assassin = 2, Judgement = 3;

    /// <summary>묘책 차림표의 제목(<c>0x0056D458</c>).</summary>
    public const string RuseTitle = "기습명령";

    /// <summary>
    /// 묘책이 먹힐 확률 — <b>문화권 x 세 묘책</b> 표(<c>0x00549B80</c>)다.
    /// </summary>
    /// <remarks>
    /// <c>0x00449080</c> 이 <c>표[문화권*3 + 묘책] &gt;= rand(100)</c> 으로 가른다.
    /// 값은 20 · 40 · 60 · 80 넷뿐이다. <b>심판은 굴리지 않는다</b> — 늘 떨어진다.
    /// </remarks>
    private static readonly int[] RuseOdds =
    [
        40, 80, 60,   40, 80, 60,   40, 80, 60,   60, 20, 40,
        60, 40, 80,   40, 80, 20,   80, 60, 20,   20, 60, 40,
        60, 20, 40,   80, 20, 60,   40, 20, 60,
    ];

    /// <summary>표의 칸 수(<c>0x00549B80</c> 의 서른셋).</summary>
    public static int RuseOddsCount => RuseOdds.Length;

    /// <summary>표 칸을 읽는 자리 — <b>문화권 * 3 + 묘책</b> 이다(<c>0x00449092</c>).</summary>
    public static int RuseOddsAt(int culture, int ruse) => Math.Clamp(culture, 0, 10) * 3 + ruse;

    /// <summary>게임 표의 값 — 손으로 고친 것을 안 얹은 것이다.</summary>
    public static int BuiltinRuseOdds(int index) =>
        index >= 0 && index < RuseOdds.Length ? RuseOdds[index] : -1;

    /// <summary>그 칸의 확률 — 손으로 고쳐 두었으면 그것이 먼저다(<see cref="Local.Helpers.RuseEdits"/>).</summary>
    public static int RuseOddsOf(int index) =>
        Local.Helpers.RuseEdits.Of(index) ?? BuiltinRuseOdds(index);

    /// <summary>한 판에 한 번씩만 쓴다(<c>+0x50</c> 의 비트).</summary>
    private int _usedRuses;

    /// <summary>그 묘책을 아직 안 썼는지.</summary>
    public bool RuseLeft(int ruse) => (_usedRuses & (1 << ruse)) == 0;

    /// <summary>
    /// 「심판」을 여는 아이템 — <b>사해사본</b>(아이템 184, <c>0x004490F9</c> 가 소지품 열여섯 칸을 뒤진다).
    /// </summary>
    public const int JudgementItem = 184;

    /// <summary>묘책 차림표의 줄들. 쓴 것은 꺼지고, 심판은 사해사본을 지녀야 열린다.</summary>
    public IReadOnlyList<(string Text, bool On)> RuseRows(bool hasJudgementItem) =>
    [
        (Ruses[Ambush], RuseLeft(Ambush)),
        (Ruses[Trap], RuseLeft(Trap)),
        (Ruses[Assassin], RuseLeft(Assassin)),
        (Ruses[Judgement], hasJudgementItem && RuseLeft(Judgement)),
    ];

    /// <summary>
    /// 그 묘책을 <b>골랐다</b>고 적어 둔다 — 한 판에 한 번뿐이라 이때 없어진다.
    /// </summary>
    /// <remarks>
    /// 차림표를 여는 <c>0x004490D0</c> 이 고른 자리에서 바로 <c>+0x50</c> 에 비트를
    /// 세운다(<c>0x00449153</c>·<c>0x0044915D</c>·<c>0x00449167</c>·<c>0x00449171</c>).
    /// 성사 굴림은 <b>그때가 아니라 턴이 굴러갈 때</b>다 — 고르고 나서 퇴각이나 일기토를
    /// 고르면 그 묘책은 <b>쓰지도 못하고 없어진다</b>.
    /// </remarks>
    public void UseRuse(int ruse) => _usedRuses |= 1 << ruse;

    /// <summary>
    /// 그 묘책을 걸어 본다. 먹혔으면 참이다.
    /// <b>심판은 안 굴린다</b> — 늘 떨어지고 양쪽을 다 친다(<c>0x00448F80</c>).
    /// </summary>
    public bool TryRuse(int ruse, GameRandom dice)
    {
        if (ruse == Judgement) return true;

        int at = RuseOddsAt(Culture, ruse);
        return at < RuseOdds.Length && RuseOddsOf(at) >= dice.Next(100);
    }

    /// <summary>공격명령 차림표의 제목(<c>0x0056D7A0</c>).</summary>
    public const string OrderTitle = "공격명령";

    /// <summary>공격명령 일곱(<c>0x00549D18</c>).</summary>
    public static readonly string[] Orders =
    [
        "통상공격", "방어중시공격", "돌격", "일기토", "퇴각", "묘책", "애니메이션",
    ];

    /// <summary>명령 번호.</summary>
    public const int Normal = 0, Guarded = 1, Charge = 2, Duel = 3, Retreat = 4,
                     Ruse = 5, Animate = 6;

    /// <summary>
    /// 일기토를 걸 수 있는지 — <b>차림표를 열 때마다 굴린다</b>(<c>0x00447930</c>).
    /// </summary>
    /// <remarks>
    /// 차림표를 짓는 <c>0x00449BC8</c> 이 일기토 비트(<c>0x08</c>)를 <b>먼저 막아 두고</b>,
    /// <c>0x00447930</c> 이 참을 내면 푼다. 그 셈이 이렇다.
    /// <code>
    ///   0044793A  몫 = 내 운 * 3 / 10          ; 0x00446FF0(4, 0) — 능력 4 가 운
    ///   00447951  몫 -= 내 무력                ; 0x00446FF0(2, 0)
    ///   0044795C  몫 += 적 대장 무력           ; 0x00446FF0(2, 6)
    ///   00447965  몫이 0 이하면 0                ; 닫는 것이 아니라 <b>0 으로 눌러 둔다</b>
    ///   0044799D  rand(100) &lt;= 몫 이면 열린다   ; 곧 몫이 0 이라도 <b>1%</b> 는 열린다
    ///   004479B0  갈래(+0x34)가 2 나 4 면 그래도 닫는다
    /// </code>
    /// 곧 <b>적 대장이 나보다 셀수록</b> 열린다 — 내가 훨씬 세면 굳이 일대일로 겨룰
    /// 까닭이 없다는 셈이다. 운이 조금 거든다.
    ///
    /// <b>마을 공략에서는 아예 안 열린다</b>(<c>0x004479B0</c>) — 성을 치는 판에
    /// 일대일이 낄 자리가 없다는 셈이다. 들에서 마주친 부대(갈래 1)에서만 열린다.
    /// </remarks>
    public bool DuelOffered(GameRandom dice)
    {
        if (Sort == Town) return false;     // 0x004479B0 은 갈래 2·4 만 닫는다

        // 몫이 0 이하여도 닫지 않는다 — 0 으로 눌러 두고 굴리므로 늘 1% 는 열린다(0x0044799B).
        int odds = Math.Max(0, _me.AbilityOf(Ability.Luck) * 3 / 10
                             - _me.AbilityOf(Ability.Might) + FoeMight);
        return dice.Next(100) <= odds;
    }

    /// <summary>적장이 하는 말(<c>0x0056D228</c>).</summary>
    public const string FoeDuelWord = "남자라면 일대일로 싸워라! 어떠냐?";

    /// <summary>
    /// <b>적이</b> 일기토를 걸어오는지(<c>0x004479D0</c>) — 걸어오면 물어야 한다.
    /// </summary>
    /// <remarks>
    /// 내가 거는 쪽(<see cref="DuelOffered"/>)과 잣대가 아주 다르다. 이쪽은
    /// <b>병사수</b>만 보고 능력은 안 본다 — <b>적이 밀릴 때</b> 판을 뒤집으려 든다.
    /// <code>
    ///   004479d7  갈래가 2 나 4 면 안 건다
    ///   004479e1  적 정원 x 4/10 &gt; 적 첫 칸 병사수 면 아래 문을 건너뛴다
    ///   004479f7      아니면 적 부대 수가 넷 이상이면 안 건다
    ///   00447a09  적 병사수 합 &gt;= 아군 병사수 합 이면 안 건다      ★
    ///   00447a1d  rand(10) &lt;= 3 이면 안 건다                       ; 열에 여섯
    ///   00447a2c  "남자라면 일대일로 싸워라! 어떠냐?" 로 묻는다
    /// </code>
    /// 곧 <b>적 병사수가 적고</b>, 그 위에 <b>적 부대가 셋 이하로 줄었거나 첫 칸이
    /// 정원의 4할 밑으로 깎였어야</b> 한다.
    /// </remarks>
    public bool FoeDuelOffered(GameRandom dice)
    {
        if (Sort == Town) return false;     // 0x004479D7 도 갈래 2·4 만 거른다

        // 적 첫 칸이 아직 성하면 부대 수까지 본다.
        if (_units[FirstFoe].Men >= RoomPerUnit(FirstFoe) * 4 / 10
            && Standing(FirstFoe) > 3) return false;

        if (MenOn(foe: true) >= MenOn(foe: false)) return false;
        return dice.Next(10) > 3;
    }

    /// <summary>
    /// 지금 고를 수 있는 명령들. 꺼진 줄도 자리를 지킨다.
    /// </summary>
    public IReadOnlyList<(string Text, bool On)> OrderRows(bool canDuel, bool canRuse) =>
    [
        (Orders[Normal], true),
        (Orders[Guarded], true),
        (Orders[Charge], true),
        (Orders[Duel], canDuel),
        (Orders[Retreat], true),
        (Orders[Ruse], canRuse),
        (Orders[Animate], true),
    ];
}
