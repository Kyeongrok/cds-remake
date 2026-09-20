namespace CdsHelper.Support.Local.Models;

/// <summary>배를 산 결과.</summary>
/// <summary>
/// 함대에 남아 있는 해상재해. 게임의 함대 <c>+0xD4</c> 비트 0·1·2 다(볼트 80).
/// </summary>
[Flags]
public enum SeaAilment
{
    None = 0,

    /// <summary>쥐 — 날마다 식량을 먹는다.</summary>
    Rats = 1,

    /// <summary>괴혈병 — 날마다 선원이 죽는다.</summary>
    Scurvy = 2,

    /// <summary>전염병 — 날마다 선원이 죽는다.</summary>
    Plague = 4,
}

public enum PurchaseResult
{
    /// <summary>샀다.</summary>
    Ok,

    /// <summary>소지금이 모자란다.</summary>
    NotEnoughGold,

    /// <summary>배가 이미 <see cref="Player.MaxShips"/> 척이다.</summary>
    FleetFull,

    /// <summary>소지품 칸이 꽉 찼다("이 이상 가질 수 없습니다!").</summary>
    BagFull,
}

/// <summary>기술을 배운 결과.</summary>
public enum LearnResult
{
    /// <summary>배웠다.</summary>
    Ok,

    /// <summary>소지금이 모자란다.</summary>
    NotEnoughGold,

    /// <summary>이미 <see cref="Skill.MaxLevel"/> 자리다.</summary>
    Mastered,
}

/// <summary>
/// 함대 창의 주인공. 소지금과 가진 배를 들고 있는다 — 조선소에서 배를 사면 여기서 돈이 빠진다.
/// </summary>
/// <remarks>
/// 세이브 파일에서 읽는 <see cref="PlayerData"/> 와는 다르다. 그쪽은 게임이 적어 둔 값을
/// 보여 주는 것이고, 이쪽은 함대 창에서 우리가 굴리는 값이다.
/// </remarks>
public sealed class Player
{
    /// <summary>
    /// 함대에 둘 수 있는 배의 수. 넘으면 더 못 산다.
    /// </summary>
    /// <remarks>
    /// 게임도 여덟이다 — 항구 함대편성의 "선박 편입" 이 <c>0x0046A24A</c> 에서
    /// <c>cmp eax, 8</c> 으로 막고, 함대 객체도 여덟 칸이다.
    /// </remarks>
    public const int MaxShips = 8;

    /// <summary>시작 소지금(닢).</summary>
    public const int StartingGold = 1000;

    /// <summary>시작 명성. 이만큼이면 만나 주는 후원자가 제법 있다.</summary>
    public const int StartingFame = 1700;

    /// <summary>
    /// 놀이가 시작하는 날 — <b>1480년 1월 1일</b>이다.
    /// </summary>
    /// <remarks>
    /// 새 놀이는 늘 이 날이다. 여급 표의 등장년도도 이 해를 바닥으로 삼고
    /// (<c>max(1480, 1495 - 표값)</c>), 도서관 책도 이 해부터 하나씩 나온다.
    ///
    /// 예전에는 <c>1499년 4월 15일</c> 로 두었는데, 그것은 <b>이어서 하던 판</b>의 갈무리에서
    /// 본 날짜였다.
    /// </remarks>
    public static readonly DateTime StartDate = new(1480, 1, 1);

    private readonly List<Ship> _ships = [];
    private readonly Dictionary<string, int> _skills = [];
    private readonly HashSet<int> _hints = [];
    private readonly HashSet<int> _openedHints = [];

    /// <summary>카라벨 한 척과 시작 소지금으로 시작한다.</summary>
    public Player()
    {
        Gold = StartingGold;
        Fame = StartingFame;
        Date = StartDate;
        _ships.Add(new Ship(Hull.Cheapest, name: ShipNames.All[0]));
        Crew = MinCrew;   // 배는 최저 승원을 채우고 시작한다
    }

    /// <summary>
    /// 주인공 이름. 사람들이 이 이름으로 부른다("각하, 에르네스토를 데리고 왔습니다").
    /// </summary>
    /// <remarks>
    /// 세이브에서 읽어 오지 않고 여기서 들고 있는다 — 함대 창은 세이브와 따로 굴러가기
    /// 때문이다(<see cref="PlayerData"/> 참고). <b>처음에는 비어 있다</b> — 게임도 신상
    /// 창을 빈 칸으로 열고 사람이 적어 넣는다.
    /// </remarks>
    public string Name { get; set; } = "";

    // ── 신상 (NEW GAME 첫 걸음) ────────────────────────────────────────────────

    /// <summary>성. 게임 화면의 첫 칸이다.</summary>
    public string Family { get; set; } = "";

    /// <summary>명. 화면에는 <c>"%s·%s"</c>(<c>0x00571B08</c>) 로 성과 붙여 낸다.</summary>
    public string Given { get; set; } = "";

    /// <summary>
    /// 나이. 게임은 25로 시작한다. <b>생년월일과 오늘 날짜로 센다</b>(<c>0x0047CB20</c>).
    /// </summary>
    /// <remarks>
    /// 게임은 나이 칸을 들고 있지 않다 — 올해 − 태어난 해이고, 올해 생일이 아직이면 하나 뺀다.
    /// 예전에는 박아 둔 값이라 해가 바뀌어도 안 늘어서, 서른여섯부터 달라지는 얼굴·여급 궁합·
    /// 성미가 영영 안 바뀌었다. 넣을 때는 그 나이가 되게 태어난 해를 거꾸로 맞춘다.
    /// </remarks>
    public int Age
    {
        get => Date.Year - BirthYear - (BirthdayAhead ? 1 : 0);
        set => BirthYear = Date.Year - value - (BirthdayAhead ? 1 : 0);
    }

    /// <summary>올해 생일이 아직 안 왔는지.</summary>
    private bool BirthdayAhead =>
        BirthMonth > Date.Month || (BirthMonth == Date.Month && BirthDay > Date.Day);

    /// <summary>생일(달·날).</summary>
    public int BirthMonth { get; set; } = 1;

    /// <summary>생일의 날.</summary>
    public int BirthDay { get; set; } = 1;

    /// <summary>혈액형(<see cref="BloodTypes"/> 의 번호).</summary>
    public int Blood { get; set; }

    /// <summary>고를 수 있는 혈액형. 게임 화면 차례 그대로다.</summary>
    public static readonly string[] BloodTypes = ["A", "B", "O", "AB"];

    /// <summary>국적(<see cref="Nations"/> 의 번호).</summary>
    public int Nation { get; set; }

    /// <summary>고를 수 있는 국적. 게임 화면에 둘만 뜬다.</summary>
    public static readonly string[] Nations = ["포르투갈 왕국", "에스파니아 왕국"];

    /// <summary>얼굴 번호(MALE.CDS 의 파트). 화면 왼쪽 초상화다.</summary>
    public int Face { get; set; }

    /// <summary>
    /// <b>운명 코드</b> — 여급과의 궁합이 이 값 하나로 갈린다(0~15).
    /// </summary>
    /// <remarks>
    /// 게임은 이것을 주인공 객체의 <c>+0x08</c> 에 따로 들고 있다(<c>0x0047CB10</c>). 새 놀이가
    /// 앞의 열여섯 초상화만 고르게 해서 값이 <see cref="Face"/> 와 늘 같았을 뿐,
    /// <b>초상화 번호와 같은 것이 아니다</b>.
    ///
    /// 그래서 칸을 갈라 두었다. 붙여 두면 초상화를 더 넣거나 차례를 바꾸는 순간, 또는
    /// 열여섯 밖의 얼굴을 가진 세이브를 읽는 순간 궁합이 조용히 어긋난다.
    /// 새로 지을 때는 고른 초상화 자리를 그대로 넣고(<see cref="SetFortune"/>),
    /// 그 값을 안 적어 둔 옛 세이브만 얼굴 번호로 물러선다.
    /// </remarks>
    public int Fortune { get; private set; }

    /// <summary>운명 자리를 넣는다. 0~15 를 벗어나면 잘라 넣는다.</summary>
    public void SetFortune(int fortune) => Fortune = Math.Clamp(fortune, 0, MaxFortune);

    /// <summary>운명 자리의 위. 게임도 젊은 얼굴 열여섯 벌만 쓴다.</summary>
    public const int MaxFortune = 15;

    /// <summary>국적 이름. 번호가 표 밖이면 첫째다.</summary>
    public string NationName => Nations[Math.Clamp(Nation, 0, Nations.Length - 1)];

    /// <summary>혈액형 이름.</summary>
    public string BloodName => BloodTypes[Math.Clamp(Blood, 0, BloodTypes.Length - 1)];

    /// <summary>
    /// 생일이 드는 별자리. 게임 표(<c>0x005609D8</c>, 목양좌부터 열둘)와 같은 이름이다.
    /// </summary>
    public string Zodiac => ZodiacOf(BirthMonth, BirthDay);

    /// <summary>별자리 이름 열둘. 목양좌(양자리)부터 돈다.</summary>
    public static readonly string[] Zodiacs =
    [
        "목양좌", "목우좌", "쌍둥이좌", "게좌", "사자좌", "처녀좌",
        "천칭좌", "전갈좌", "궁수좌", "산양좌", "물병좌", "물고기좌",
    ];

    /// <summary>그 날짜가 드는 별자리. 경계 날은 흔히 쓰는 자리를 따른다.</summary>
    public static string ZodiacOf(int month, int day)
    {
        // 자리마다 "그 달 며칟날부터"다. 목양좌(양자리)는 3월 21일부터.
        int[] from = [21, 20, 21, 22, 23, 23, 23, 23, 22, 22, 20, 19];
        int at = (month + 9) % 12;                 // 3월이 0(목양좌)이 되게 민다
        if (day < from[at]) at = (at + 11) % 12;    // 아직 안 넘었으면 앞자리
        return Zodiacs[at];
    }

    /// <summary>신상을 한꺼번에 박는다. NEW GAME 의 첫 걸음이 부른다.</summary>
    /// <param name="fortune">
    /// 그 얼굴이 지고 나올 <b>운명 자리</b>. 안 넘기면 얼굴 번호를 그대로 쓴다 —
    /// 게임이 앞의 열여섯만 고르게 하던 때의 셈이다.
    /// </param>
    public void SetProfile(string family, string given, int age, int month, int day,
                           int blood, int nation, int face, int? fortune = null)
    {
        Family = family.Trim();
        Given = given.Trim();
        Name = Family.Length > 0 ? $"{Given}·{Family}" : Given;
        BirthMonth = Math.Clamp(month, 1, 12);
        BirthDay = Math.Clamp(day, 1, 31);
        // 생일을 먼저 넣어야 나이에서 태어난 해가 바로 나온다.
        Age = Math.Clamp(age, MinAge, MaxAge);
        Blood = Math.Clamp(blood, 0, BloodTypes.Length - 1);
        Nation = Math.Clamp(nation, 0, Nations.Length - 1);
        Face = Math.Max(0, face);
        // 운명 자리는 부르는 쪽이 정해 넘긴다 — 게임은 앞의 열여섯만 고르게 해서
        // 얼굴 번호가 곧 자리였지만, 얼굴을 더 넣으면 그 셈이 안 통한다.
        SetFortune(fortune ?? Face);
    }

    /// <summary>고를 수 있는 나이 — 숫자판이 18 ~ 40 이다(<c>0x0045C311</c>).</summary>
    public const int MinAge = 18;

    /// <summary>고를 수 있는 가장 많은 나이.</summary>
    public const int MaxAge = 40;

    /// <summary>악명치 — 인물정보 판의 명성 맞은편 칸이다.</summary>
    /// <remarks>
    /// 게임은 나쁜 짓(해적질·약탈)으로 올린다. 우리 쪽에는 아직 올릴 길이 없어 늘 0 이다.
    /// </remarks>
    public int Infamy { get; set; }

    /// <summary>
    /// 지구를 몇 바퀴 돌았는가(<c>0x005B63D0</c> = 제독 <c>+0x330</c>) — 동으로 돌면 +1, 서로 돌면 −1 이다.
    /// </summary>
    /// <remarks>
    /// 경도가 <b>날짜변경선을 넘을 때</b>만 움직인다(<c>0x0047D11B</c>) — 경도 칸이 0 밑으로
    /// 내려가면 40000 을 더하며 하나 줄고, 40000 을 넘으면 빼며 하나 는다. 세계일주 장면이
    /// 이 값으로 「하루 어긋났다」를 센다.
    /// </remarks>
    public int Laps { get; set; }

    /// <summary>
    /// NEW GAME 에서 「누적캐릭터를 등장시키지 않는다」를 골랐다(<c>0x005A4D1A</c> 비트 <c>0x10</c>).
    /// 이 판에서 은퇴하면 올라 있던 누적 캐릭터를 모두 지우고 이 제독을 올린다(<c>0x0041AD55</c>).
    /// </summary>
    public bool SkipsCumulative { get; set; }

    /// <summary>빚(닢). 아직 빌려 주는 데가 없어 늘 0 이다.</summary>
    public int Debt { get; set; }

    /// <summary>태어난 해. 인물정보 판이 생년월일로 적는다.</summary>
    public int BirthYear { get; set; } = StartDate.Year - 25;

    /// <summary>직업 번호(<see cref="Job.All"/>).</summary>
    public int JobIndex { get; set; }

    /// <summary>직업.</summary>
    public Job Work => Job.Of(JobIndex);

    /// <summary>능력치 여섯(체력·지력·무력·매력·운·신앙심).</summary>
    public int[] Abilities { get; private set; } = [50, 50, 50, 50, 50, 50];

    /// <summary>그 능력치.</summary>
    public int AbilityOf(int which) =>
        which >= 0 && which < Abilities.Length ? Abilities[which] : 0;

    /// <summary>능력치를 통째로 박는다.</summary>
    public void SetAbilities(IReadOnlyList<int> values)
    {
        var next = new int[Ability.Names.Length];
        for (int i = 0; i < next.Length; i++)
            next[i] = i < values.Count ? values[i] : Ability.Base;
        Abilities = next;
    }

    /// <summary>능력치 한 칸을 그만큼 움직인다. 모르는 칸이면 아무 일도 없다.</summary>
    public void AdjustAbility(int which, int by)
    {
        if (which < 0 || which >= Abilities.Length) return;
        // 게임은 <b>보이는 값</b> 1~100 으로 자른 뒤 도로 1 을 뺀다(0x00432C50 → 0x0049E560).
        Abilities[which] = Math.Clamp(Ability.Display(Abilities[which]) + by, 1, Ability.Max)
                           - 1;
    }

    /// <summary>
    /// 주량(<c>0x005B60DC</c>, 0~3) — 술집에서 <c>(주량 + 1) x 50</c> 을 넘게 마시면 취한다
    /// (<c>0x0042F021</c>).
    /// </summary>
    /// <remarks>
    /// <b>놀이 안에서 올라가는 길이 없다.</b> NEW GAME 은 0 으로 시작하고, 아이가 물려받을 때만
    /// <c>rand(3) − 1</c> 이 얹히며(<c>0x00460E99</c>), 세대교체가 아이 칸을 그대로 베껴 온다
    /// (<c>0x0047D4F5</c> 의 <c>rep movsd</c> 가 <c>+0x20</c>~<c>+0xAB</c> 를 옮긴다).
    /// </remarks>
    public int Drinking { get; set; }

    /// <summary>주량이 들 수 있는 끝(<c>0x00460EA3</c> 의 <c>clamp(값, 0, 3)</c>).</summary>
    public const int MaxDrinking = 3;

    /// <summary>제독의 컨디션(<c>0x005B60D8</c>). 처음 값은 <see cref="ConditionFull"/> 이다.</summary>
    public int Condition { get; private set; } = ConditionFull;

    /// <summary>컨디션의 성한 값과 위 끝(<c>0x0049E560</c> 이 <c>0 ~ 0x7D0</c> 으로 자른다).</summary>
    public const int ConditionFull = 100, ConditionMax = 2000;

    /// <summary>컨디션을 그대로 박는다. 세이브를 되돌릴 때 쓴다.</summary>
    public void SetCondition(int value) => Condition = Math.Clamp(value, 0, ConditionMax);

    /// <summary>
    /// 몸이 상한다 — 일기토를 치르고 나면 <b>컨디션</b>이 그만큼 준다.
    /// </summary>
    /// <remarks>
    /// <code>
    /// 4aa600  잃은 = 체력+1 − (남은 부위 셋의 합 / 3)
    /// 4aa61a  if (컨디션[0x5B60D8] &lt;= 잃은) 컨디션 = 100
    /// 4aa62c  0x00432C80(0x5B60C0, −잃은)   ; [객체+0x18] = clamp(컨디션 − 잃은, 0, 0x7D0)
    /// </code>
    /// <b>체력(능력치)은 안 건드린다.</b> 능력 여섯은 <c>0x5B60C0</c> 부터고 <c>+0x18</c> 은
    /// 그 뒤의 컨디션 칸이다. 예전에는 여기서 체력을 깎아 <b>일기토를 치를수록 제독의 체력이
    /// 영영 줄었다</b> — 일기토 막대는 <c>체력+1</c> 을 100 눈금으로 재니까, 몇 판 치르고 나면
    /// 막대가 손톱만 해진다. 「플레이어 체력이 너무 낮다」가 이것이다.
    /// </remarks>
    public void Hurt(int amount)
    {
        if (amount <= 0) return;
        if (Condition <= amount) Condition = ConditionFull;
        SetCondition(Condition - amount);
    }

    /// <summary>언어마다의 자리(0~<see cref="Skill.MaxLevel"/>).</summary>
    private readonly Dictionary<string, int> _tongues = [];

    /// <summary>배운 언어.</summary>
    public IReadOnlyDictionary<string, int> Tongues => _tongues;

    /// <summary>그 언어의 자리.</summary>
    public int TongueOf(string language) => _tongues.GetValueOrDefault(language);

    /// <summary>언어 자리를 박는다.</summary>
    public void SetTongue(string language, int level) =>
        _tongues[language] = Math.Clamp(level, 0, Skill.MaxLevel);

    /// <summary>
    /// 세이브에서 언어 자리를 되돌린다.
    /// </summary>
    /// <remarks>
    /// 언어는 기술과 <b>딴 칸</b>에 있어서 <see cref="Restore"/> 의 기술 사전에 안 실린다.
    /// 판 24 앞 세이브에는 언어가 아예 안 적혀 있어 그때는 아무 일도 안 한다 —
    /// 그 판까지는 갈무리를 불러오면 <b>배운 언어가 다 0 이 되었다</b>.
    /// </remarks>
    public void RestoreTongues(IEnumerable<KeyValuePair<string, int>>? tongues)
    {
        if (tongues == null) return;
        _tongues.Clear();
        foreach (var (name, level) in tongues)
            _tongues[name] = Math.Clamp(level, 0, Skill.MaxLevel);
    }

    /// <summary>기술 자리를 박는다(새 놀이에서 찍어 줄 때).</summary>
    public void SetSkill(string skill, int level) =>
        _skills[skill] = Math.Clamp(level, 0, Skill.MaxLevel);

    /// <summary>
    /// 명성. 후원자를 만나려면 그 사람이 요구하는 만큼 있어야 한다.
    /// </summary>
    /// <remarks>
    /// 게임은 알현의 첫 관문에서 이것을 본다(<c>0x004AE1F0</c> → <c>0x0044E740</c>) —
    /// 모자라면 집사가 "…님은 바쁘셔서 만나실 수 없습니다" 로 돌려보낸다.
    /// 요구치는 후원자마다 다르다(<c>patrons.json</c> 의 fame, 0 부터 9900 까지).
    ///
    /// 항구에서 발견물을 <b>발표</b>하면 오른다 — 그 발견물의 보수를 70 으로 나눈 만큼이고
    /// 적어도 10 이다(<c>0x0047E849</c>). <see cref="StartingFame"/> 이면 여든한 명 가운데
    /// 열몇이 만나 주고, 알릴수록 문이 열린다.
    /// </remarks>
    public int Fame { get; set; }

    /// <summary>지금 날짜. 기술을 배우면 그만큼 달이 넘어간다.</summary>
    public DateTime Date { get; private set; }

    /// <summary>지금 들어와 있는 도시. 바다에 있으면 -1.</summary>
    public int CityId { get; private set; } = -1;

    /// <summary>
    /// 함대가 <b>닻을 내린 도시</b> — 배 레코드 <c>+0x60</c>(세터 <c>0x0044CA70</c>). 바다면 -1.
    /// </summary>
    /// <remarks>
    /// 바다로 들어설 때 함대 배 모두에 그 도시를 적고(<c>0x0048B54E</c> · <c>0x0048DC33</c>), 출항하면
    /// -1 로 돌린다(<c>0x0048EB84</c>). <b>성문으로 나서 걸어가도 그대로</b>라, 걸어 들어간 딴 마을에서는
    /// 함대가 없는 것으로 친다 — <see cref="FleetHere"/> 가 그 관문(<c>0x0040E1C0</c>)이다.
    /// 옛 세이브는 이 값을 몰라 <see cref="FleetUnknown"/> 으로 열고, 그때는 어디서나 통과시킨다.
    /// </remarks>
    public int FleetCity { get; private set; } = FleetUnknown;

    /// <summary>함대 도시를 모른다 — 옛 세이브. 다음 입항까지 어디서나 함대가 있는 것으로 친다.</summary>
    public const int FleetUnknown = -2;

    /// <summary>함대가 그 도시에 닻을 내렸다.</summary>
    public void MoorAt(int city) => FleetCity = city;

    /// <summary>세이브에서 함대 도시를 되돌린다. 없으면 모른다.</summary>
    public void RestoreFleetCity(int? city) => FleetCity = city ?? FleetUnknown;

    /// <summary>
    /// 함대가 그 도시에 있고 배가 <paramref name="ships"/> 척 이상인지(<c>0x0040E1C0(도시, n)</c>).
    /// </summary>
    public bool FleetHere(int city, int ships = 0) =>
        (FleetCity == FleetUnknown || FleetCity == city) && _ships.Count >= ships;

    /// <summary>지금 들어와 있는 도시 이름. 바다에 있으면 빈 문자열.</summary>
    public string CityName { get; private set; } = "";

    /// <summary>모항 — 새 판을 연 도시. 모르면 -1.</summary>
    /// <remarks>
    /// 게임은 도시 레코드 <c>+0x1D</c> 의 비트 8 로 든다. NEW GAME 이 시작 도시에만 세우고
    /// (<c>0x0045E449</c> · <c>0x0045E7BE</c> · <c>0x0045EA28</c>) 그 뒤로 옮기는 곳은 없다.
    /// 항구 「발표」가 이 비트를 본다(<c>0x00476DE0</c>) — <b>발표는 모항에서만</b> 된다.
    /// 도착 대사 「제독, 역시 모항이 좋군요.」(<c>0x004687BE</c>)도 같은 비트다.
    /// </remarks>
    public int HomePort { get; private set; } = -1;

    /// <summary>모항을 박는다(새 판 · 불러오기).</summary>
    public void SetHomePort(int cityId) => HomePort = cityId;

    /// <summary>
    /// 바다에서 적을 때 배가 있던 칸. 도시에 있으면 쓰지 않는다 — 적기 앞에 지도가 채운다.
    /// </summary>
    public (double X, double Y)? SeaCell { get; private set; }

    /// <summary>바다의 배 자리를 적어 둔다.</summary>
    public void SetSeaCell((double X, double Y)? cell) => SeaCell = cell;

    /// <summary>배운 기술과 그 자리.</summary>
    public IReadOnlyDictionary<string, int> Skills => _skills;

    /// <summary>얻은 힌트 번호. 책을 읽으면 는다.</summary>
    public IReadOnlyCollection<int> Hints => _hints;

    /// <summary>
    /// 부하 자리 이름. 게임 EXE 의 표(<c>0x00571038</c>) 차례 그대로다.
    /// </summary>
    public static readonly string[] MateRoles = ["부관", "항해사", "측량사", "통역"];

    /// <summary>부하로 삼을 수 있는 사람 수. 자리마다 하나씩이다.</summary>
    public static readonly int MaxMates = MateRoles.Length;

    /// <summary>자리별 부하. 빈 자리는 빈 문자열이다.</summary>
    private readonly string[] _mates = ["", "", "", ""];
    private readonly HashSet<string> _met = [];

    /// <summary>낯을 튼 사람. 이 사람들만 이름이 보이고 말을 걸 수 있다.</summary>
    /// <remarks>
    /// 게임은 인물 객체의 <c>vtbl[0x34]</c> 로 이것을 가른다 — 참이면 이름 대신 "남자"·"여"
    /// 로 부르고 한잔 사는 것만 되고, 거짓이면 "[이름]이 있다" 로 부르고 말을 걸 수 있다
    /// (볼트 <c>14.분석-술집 화면과 대사</c>).
    /// </remarks>
    public IReadOnlyCollection<string> Met => _met;

    /// <summary>그 사람과 낯을 텄는지.</summary>
    public bool HasMet(string name) => _met.Contains(name);

    /// <summary>낯을 튼다. 처음이면 true.</summary>
    public bool Meet(string name) => !string.IsNullOrEmpty(name) && _met.Add(name);

    private readonly Dictionary<string, int> _closeness = [];

    /// <summary>친밀도의 위(<c>0x00478530</c> 이 0~100 으로 자른다).</summary>
    public const int MaxCloseness = 100;

    /// <summary>
    /// 후원자마다의 친밀도(0~<see cref="MaxCloseness"/>). 움직인 적 있는 사람만 들어 있다.
    /// </summary>
    /// <remarks>
    /// 게임은 후원자 객체 <c>+0x20</c> 에 들고 <b>0 에서 시작한다</b> — 후원자 정보 창의
    /// 「친밀도」가 이 값이다. 후원자 표의 <c>+0x30</c> 은 이름은 같아도 딴 값이다:
    /// 그쪽은 낼 자금을 가르는 밑값이고(<c>0x004AF086</c> 이 표를 읽는다) 놀이 내내 안 바뀐다.
    ///
    /// 여급의 친밀도(<see cref="Liking"/>)와 같은 함수로 오르내리지만 자리는 따로다.
    /// </remarks>
    public IReadOnlyDictionary<string, int> Closeness => _closeness;

    /// <summary>그 후원자와의 친밀도. 아직 움직인 적이 없으면 0 이다.</summary>
    public int ClosenessOf(string name) =>
        !string.IsNullOrEmpty(name) && _closeness.TryGetValue(name, out int now) ? now : 0;

    /// <summary>
    /// 친밀도를 움직인다(<c>0x00478530</c>). 돌려주는 것은 움직인 뒤의 값이다.
    /// </summary>
    public int Endear(string name, int by)
    {
        if (string.IsNullOrEmpty(name)) return 0;
        int now = Math.Clamp(ClosenessOf(name) + by, 0, MaxCloseness);
        _closeness[name] = now;
        return now;
    }

    /// <summary>
    /// 후원자마다의 <b>지갑</b>(<c>후원자 +0x24</c>). 적어 둔 적 없는 사람은 여기 없다.
    /// </summary>
    /// <remarks>
    /// 새 판을 열 때 <b>재력 x 10000</b> 으로 채워진다(<c>0x004AD88F</c> — 표 <c>+0x2C</c>).
    /// 계약을 맺으면 계약금의 <b>절반</b>이 여기서 빠지고(<c>0x004ADF4A</c>), 발견물을 보고하면
    /// 그 보수가 도로 차되 <b>재력 x 10000 을 못 넘는다</b>(<c>0x004113E4</c>).
    /// <b>저절로 차지 않는다</b> — 달이 바뀌어도, 역사 대본으로도 안 바뀐다.
    /// 위약금(계약중단 · 감찰관 사고)은 내 소지금만 건드리고 이 값은 안 건드린다.
    /// </remarks>
    public IReadOnlyDictionary<string, int> Purses => _purses;

    private readonly Dictionary<string, int> _purses = [];

    /// <summary>
    /// 그 후원자의 지갑. 아직 건드린 적이 없으면 <paramref name="wealth"/> 그대로다.
    /// </summary>
    /// <remarks>
    /// <paramref name="wealth"/> 는 <b>이미 닢으로 적힌 재력</b>이다 — 원본 표의 등급(9~99)에
    /// 만을 곱한 값이 <c>patrons.json</c> 의 <c>wealth</c> 다(조안 2세 = 90 → 900000).
    /// </remarks>
    public int PurseOf(string name, int wealth) =>
        !string.IsNullOrEmpty(name) && _purses.TryGetValue(name, out int now) ? now : wealth;

    /// <summary>지갑을 움직인다 — 0 밑으로도, 재력 위로도 안 간다.</summary>
    public int SpendPurse(string name, int by, int wealth)
    {
        if (string.IsNullOrEmpty(name)) return 0;
        int now = Math.Clamp(PurseOf(name, wealth) + by, 0, Math.Max(0, wealth));
        _purses[name] = now;
        return now;
    }

    /// <summary>적어 둔 지갑을 되돌린다.</summary>
    public void RestorePurses(Dictionary<string, int>? purses)
    {
        _purses.Clear();
        if (purses == null) return;
        foreach (var (name, value) in purses)
            if (!string.IsNullOrEmpty(name)) _purses[name] = Math.Max(0, value);
    }

    /// <summary>적어 둔 친밀도를 되돌린다.</summary>
    public void RestoreCloseness(Dictionary<string, int>? closeness)
    {
        _closeness.Clear();
        if (closeness == null) return;
        foreach (var (name, value) in closeness)
            if (!string.IsNullOrEmpty(name))
                _closeness[name] = Math.Clamp(value, 0, MaxCloseness);
    }

    /// <summary>
    /// 자리별 부하. 색인이 <see cref="MateRoles"/> 의 자리고, 빈 자리는 빈 문자열이다.
    /// </summary>
    /// <remarks>
    /// 게임은 부하를 든 차례가 아니라 <b>자리</b>로 든다 — 부관·항해사·측량사·통역이다.
    /// 여관·술집의 "부하편성" 에서 두 줄을 눌러 자리를 맞바꾼다.
    /// </remarks>
    public IReadOnlyList<string> Mates => _mates;

    /// <summary>그 자리에 앉은 사람. 비었으면 빈 문자열.</summary>
    public string MateAt(int slot) =>
        slot >= 0 && slot < _mates.Length ? _mates[slot] : "";

    /// <summary>든 부하 수(빈 자리는 빼고).</summary>
    public int MateCount => _mates.Count(m => m.Length > 0);

    /// <summary>이미 부하로 든 사람인지.</summary>
    public bool HasMate(string name) => _mates.Contains(name);

    /// <summary>
    /// 부하로 삼는다. 앞에서부터 빈 자리에 앉힌다. 처음 드는 사람이고 자리가 남았으면 true.
    /// </summary>
    public bool Hire(string name)
    {
        if (string.IsNullOrEmpty(name) || HasMate(name)) return false;

        for (int i = 0; i < _mates.Length; i++)
            if (_mates[i].Length == 0)
            {
                _mates[i] = name;
                return true;
            }
        return false;
    }

    /// <summary>그 자리에 그 사람을 앉힌다. 자리를 되돌릴 때 쓴다.</summary>
    public void SetMate(int slot, string name)
    {
        if (slot >= 0 && slot < _mates.Length) _mates[slot] = name ?? "";
    }

    /// <summary>
    /// 부하 하나의 됨됨이. 자리에는 이름만 앉히고 자료는 여기에 따로 적어 둔다.
    /// </summary>
    /// <remarks>
    /// 술집에서 사람을 들일 때 <b>그 자리에서 베껴 둔다</b>. 이름만 들고 있으면 나중에
    /// 인물정보를 낼 때마다 게임 세이브(SAVEDATA.CDS)를 다시 뒤져야 하는데, 그 파일은
    /// 우리 것이 아니라 이름이 바뀌거나 없어질 수 있다 — 그러면 제 부하를 두고도
    /// "자료를 찾지 못했다" 가 뜬다.
    /// </remarks>
    /// <param name="Sword">검술(0~3). 일기토에서 부관을 내보낼 때 이것을 본다.
    /// 예전 갈무리에는 없던 칸이라 없으면 0 이다.</param>
    /// <param name="Shooting">사격술(0~3) · <paramref name="Gunnery"/> 포술(0~3).
    /// 육상전 부대배치가 제독 것과 견주어 <b>높은 쪽</b>을 쓴다(<c>0x00446F70</c>).
    /// 검술처럼 예전 갈무리에는 없던 칸이라 없으면 0 이다.</param>
    /// <param name="Condition">컨디션(인물 <c>+0x38</c>). 일기토에 대신 나가면 이것이 깎인다.</param>
    /// <param name="Sailing">항해술(0~3) · <paramref name="Handling"/> 운용술 ·
    /// <paramref name="Medicine"/> 의학 · <paramref name="Science"/> 과학.
    /// 바다 사건이 제독 것과 견주어 <b>높은 쪽</b>을 쓴다(<c>0x0047CCA0</c>).
    /// 예전 갈무리에는 없던 칸이라 없으면 0 이다.</param>
    public readonly record struct MateInfo(string Name, int Face, int Fame, int Age,
                                           int Body, int Mind, int Might, int Charm, int Luck,
                                           int Sword = 0, int Shooting = 0, int Gunnery = 0,
                                           int Condition = ConditionFull,
                                           int Sailing = 0, int Handling = 0,
                                           int Medicine = 0, int Science = 0);

    private readonly Dictionary<string, MateInfo> _mateBook = [];

    /// <summary>적어 둔 부하 자료. 세이브에 그대로 적힌다.</summary>
    public IReadOnlyCollection<MateInfo> MateBook => _mateBook.Values;

    /// <summary>부하 자료를 적어 둔다. 같은 이름이면 새것으로 갈아 낸다.</summary>
    public void RememberMate(MateInfo who)
    {
        if (!string.IsNullOrEmpty(who.Name)) _mateBook[who.Name] = who;
    }

    /// <summary>그 이름으로 적어 둔 자료. 없으면 null.</summary>
    public MateInfo? MateInfoOf(string name) =>
        _mateBook.TryGetValue(name ?? "", out var who) ? who : null;

    /// <summary>세이브에서 부하 자료를 되돌린다.</summary>
    public void RestoreMateBook(IEnumerable<MateInfo>? book)
    {
        _mateBook.Clear();
        if (book == null) return;
        foreach (var who in book) RememberMate(who);
    }

    /// <summary>
    /// 부관이 다친다 — 일기토를 대신 치른 뒤에 부른다.
    /// </summary>
    /// <remarks>
    /// 게임도 부관을 내보내면 그 사람의 컨디션(인물 <c>+0x38</c>)에서 깎는데, 제독 쪽과
    /// <b>길이 다르다</b> — 컨디션이 잃은 값 <b>이하면 100 으로 세우고 거기서 끝난다</b>
    /// (<c>0x004AA5EF</c> 의 <c>jmp 0x4AA639</c>). 제독만 100 을 세운 뒤 이어서 뺀다
    /// (<c>0x004AA622</c>).
    /// </remarks>
    public void HurtMate(string name, int amount)
    {
        if (amount <= 0 || !_mateBook.TryGetValue(name ?? "", out var who)) return;

        if (who.Condition <= amount)
        {
            _mateBook[who.Name] = who with { Condition = ConditionFull };
            return;
        }
        _mateBook[who.Name] = who with { Condition = Math.Clamp(who.Condition - amount, 0, ConditionMax) };
    }

    /// <summary>
    /// 부하의 무력을 올린다 — 일기토에 이긴 뒤 굴리는 성장이다(<c>0x00455D6B</c>).
    /// </summary>
    /// <remarks>담는 값이라 <see cref="Ability.Max"/> − 1 을 넘기지 않는다.</remarks>
    public void GrowMate(string name, int by)
    {
        if (by <= 0 || !_mateBook.TryGetValue(name ?? "", out var who)) return;
        _mateBook[who.Name] = who with { Might = Math.Min(who.Might + by, Ability.Max - 1) };
    }

    /// <summary>두 자리를 맞바꾼다. 빈 자리와도 바꿀 수 있다.</summary>
    public void SwapMates(int a, int b)
    {
        if (a == b || a < 0 || b < 0 || a >= _mates.Length || b >= _mates.Length) return;
        (_mates[a], _mates[b]) = (_mates[b], _mates[a]);
    }

    private readonly List<int> _items = [];

    /// <summary>
    /// 소지품 — 산 것·주운 것이 든 차례대로다. 값은 아이템 번호(<c>item.json</c> 의 id)다.
    /// </summary>
    /// <remarks>
    /// 게임은 플레이어 객체 <c>+0x118</c> 에 <b>열여섯 칸</b>을 두고 빈 칸은 -1 로 둔다
    /// (읽기 <c>0x0047CDD0</c> · 쓰기 <c>0x0047CDB0</c>). 꽉 차면 "이 이상 가질 수
    /// 없습니다!"(<c>0x00544830</c>) 로 물린다.
    ///
    /// 같은 것을 여럿 들 수 있게 두었다. 게임 소지품 일람에도 같은 이름이 두 줄 나온다.
    /// </remarks>
    public IReadOnlyList<int> Items => _items;

    /// <summary>소지품 칸 수. 게임도 열여섯이다.</summary>
    public const int MaxItems = 16;

    /// <summary>소지품이 꽉 찼는지.</summary>
    public bool IsBagFull => _items.Count >= MaxItems;

    /// <summary>그 아이템을 지녔는지.</summary>
    public bool HasItem(int itemId) => _items.Contains(itemId);

    /// <summary>소지품에 넣는다. 칸이 꽉 찼으면 아무것도 하지 않고 false.</summary>
    public bool Take(int itemId)
    {
        if (itemId < 0 || IsBagFull) return false;
        _items.Add(itemId);
        return true;
    }

    /// <summary>소지품에서 하나 뺀다. 없었으면 false.</summary>
    public bool Drop(int itemId) => _items.Remove(itemId);

    private readonly List<int> _stored = [];

    /// <summary>
    /// 자택에 보관해 둔 것. 소지품과 같은 아이템 번호다.
    /// </summary>
    /// <remarks>
    /// 게임은 소지품 열여섯 칸 바로 뒤(<c>+0x158</c>)에 <b>아흔아홉 칸</b>을 둔다
    /// (읽기 <c>0x0047CE70</c> · 쓰기 <c>0x0047CE50</c>). 자택의 "보관" 이 두 칸을 주고받는다.
    /// </remarks>
    public IReadOnlyList<int> Stored => _stored;

    /// <summary>보관 칸 수. 게임도 아흔아홉이다.</summary>
    public const int MaxStored = 99;

    /// <summary>보관 칸이 꽉 찼는지.</summary>
    public bool IsStoreFull => _stored.Count >= MaxStored;

    /// <summary>소지품 한 칸을 자택에 맡긴다. 자리가 없거나 칸이 없으면 false.</summary>
    public bool Store(int index)
    {
        if (index < 0 || index >= _items.Count || IsStoreFull) return false;
        _stored.Add(_items[index]);
        _items.RemoveAt(index);
        return true;
    }

    /// <summary>보관해 둔 한 칸을 도로 든다. 소지품이 꽉 찼으면 false.</summary>
    public bool Fetch(int index)
    {
        if (index < 0 || index >= _stored.Count || IsBagFull) return false;
        _items.Add(_stored[index]);
        _stored.RemoveAt(index);
        return true;
    }

    /// <summary>
    /// 소지품과 보관 칸을 통째로 갈아 끼운다. 자택 <b>아이템 교환</b> 창이 다 마치고
    /// 한 번 쓴다.
    /// </summary>
    /// <remarks>
    /// 게임은 두 칸을 열여섯·아흔아홉 자리 배열로 들고 빈 자리를 -1 로 남긴다. 교환 창도
    /// 그 배열을 그대로 주무르다가 끝날 때 되쓴다(<c>0x0047CDB0</c> · <c>0x0047CE50</c>).
    /// 우리 쪽은 빈 자리를 안 들고 다니므로 <b>여기서 추려</b> 넣는다.
    /// </remarks>
    public void ReplaceBelongings(IEnumerable<int> items, IEnumerable<int> stored)
    {
        _items.Clear();
        foreach (int id in items)
            if (id >= 0 && _items.Count < MaxItems) _items.Add(id);

        _stored.Clear();
        foreach (int id in stored)
            if (id >= 0 && _stored.Count < MaxStored) _stored.Add(id);
    }

    /// <summary>그 값을 치를 수 있는지.</summary>
    public bool CanAfford(int price) => Gold >= price;

    /// <summary>
    /// 소지금과 저금의 위쪽 끝. 게임도 백만 닢에서 자른다.
    /// </summary>
    /// <remarks>
    /// 돈을 더하는 자리(<c>0x0047CBC0</c> 소지금 · <c>0x0047CC00</c> 저금)가 둘 다
    /// <c>0x0049E5A0(지금, 더할값, 0, 0xF4240)</c> 으로 0~1000000 사이에 가둔다.
    /// 넘치면 "금화는 더 이상 늘릴 수 없습니다"(<c>0x0055A488</c>) 가 뜬다.
    /// </remarks>
    public const int MaxGold = 1_000_000;

    /// <summary>
    /// 돈을 받는다(매각·사례). 소지금은 <see cref="MaxGold"/> 에서 잘린다.
    /// </summary>
    public void Earn(int amount)
    {
        if (amount <= 0) return;
        Gold = (int)Math.Min(MaxGold, (long)Gold + amount);
    }

    /// <summary>
    /// 자택에 맡겨 둔 돈. 소지금과 따로 두며 백만 닢까지 담긴다.
    /// </summary>
    /// <remarks>게임은 플레이어 객체의 소지금(<c>+0xF4</c>) 옆에 나란히 둔다.</remarks>
    public int Savings { get; private set; }

    /// <summary>예금을 그 값으로 둔다(세대교체 — 4/5 만 물려준다).</summary>
    public void SetSavings(int savings) => Savings = Math.Clamp(savings, 0, MaxGold);

    /// <summary>낯튼 사람을 다 잊는다(세대교체 — 새 제독은 아무도 모른다).</summary>
    public void ForgetEveryone() => _met.Clear();

    /// <summary>여급 친밀도를 다 지운다(세대교체 — <c>0x00461E6C</c>).</summary>
    public void ClearLiking() => _liking.Clear();

    /// <summary>
    /// 그만큼 저금한다. 소지금이 모자라거나 저금 칸이 다 찼으면 할 수 있는 만큼만 한다.
    /// </summary>
    /// <returns>실제로 맡긴 돈.</returns>
    public int Deposit(int amount)
    {
        int moved = Math.Min(Math.Max(0, amount), Math.Min(Gold, MaxGold - Savings));
        Gold -= moved;
        Savings += moved;
        return moved;
    }

    /// <summary>
    /// 저금에서 그만큼 꺼낸다. 저금이 모자라거나 소지금이 다 찼으면 되는 만큼만 꺼낸다.
    /// </summary>
    /// <returns>실제로 꺼낸 돈.</returns>
    public int Withdraw(int amount)
    {
        int moved = Math.Min(Math.Max(0, amount), Math.Min(Savings, MaxGold - Gold));
        Savings -= moved;
        Gold += moved;
        return moved;
    }

    /// <summary>
    /// 있는 만큼만 치른다 — 모자라도 물리지 않고 0 까지만 깎인다.
    /// </summary>
    /// <remarks>
    /// 게임의 <c>0x0047CBC0</c> 이 이렇게 한다 — 소지금을 <c>0x0049E5A0</c> 으로
    /// <c>0 ~ 1,000,000</c> 사이에 가둔다. 뭍을 걷는 하루 여행비가 이 길로 나간다.
    /// 물건 값처럼 <b>못 사면 안 사야 하는</b> 자리는 <see cref="Pay"/> 를 쓴다.
    /// </remarks>
    /// <returns>실제로 나간 돈.</returns>
    public int Spend(int amount)
    {
        int paid = Math.Clamp(amount, 0, Gold);
        Gold -= paid;
        return paid;
    }

    /// <summary>값을 치른다. 모자라면 아무것도 하지 않고 false.</summary>
    public bool Pay(int amount)
    {
        if (amount < 0 || !CanAfford(amount)) return false;
        Gold -= amount;
        return true;
    }

    /// <summary>
    /// 달을 넘긴다(여관 숙박·수련). 날짜는 놀이 안에서만 흐르므로 밖에서 박지 않는다.
    /// </summary>
    public void AdvanceMonths(int months)
    {
        if (months <= 0) return;
        Date = Date.AddMonths(months);
        Recover(months * DaysPerMonth);
        AgeCargo(months * DaysPerMonth);
    }

    /// <summary>게임이 달을 날로 셀 때 쓰는 날수. 달력 달이 아니라 서른 날이다.</summary>
    public const int DaysPerMonth = 30;

    /// <summary>
    /// 날을 넘긴다(자택 휴양). 게임은 달을 셀 때도 <b>서른 날</b>로 세므로 이쪽을 쓴다.
    /// </summary>
    /// <remarks>
    /// 휴양은 <c>0x004A2AD0(개월 x 30, 1)</c> 로 날수를 넘긴다 — 달력 달이 아니라 30일이다.
    /// </remarks>
    public void AdvanceDays(int days)
    {
        if (days <= 0) return;
        Date = Date.AddDays(days);
        Recover(days);
        AgeCargo(days);
    }

    /// <summary>
    /// 마을에서 날을 넘긴 값을 몸에 먹인다 — <b>하루에 피로 -1, 사기 +3</b>.
    /// </summary>
    /// <remarks>
    /// 게임의 <c>0x004A2AD0(날수, 모드)</c> 가 하는 일이다. 휴양만이 아니라 숙박·수련처럼
    /// <b>마을에서 날이 가는 모든 자리</b>가 이 하나를 거친다.
    /// <code>
    /// 4a2b06  0x474030(함대, -날수)      ; 피로도 -= 날수     (+0x28)
    /// 4a2b14  0x474060(함대, 날수 * 3)   ; 사기   += 날수 x 3 (+0x2C)
    /// 4a2b1f  0x0044AFD0(달력, 날수)     ; 날짜를 넘긴다
    /// </code>
    /// 그래서 한 달 휴양이면 피로가 서른, 사기가 아흔 움직인다 — 한 번에 다 푼다.
    /// <b>바다에서는 이 길을 안 거친다</b>(<see cref="PassDayAtSea"/>) — 항해 중에는
    /// 지치기만 하고 안 풀린다.
    /// </remarks>
    private void Recover(int days)
    {
        Tire(-days);
        Cheer(days * MoralePerRestDay);
    }

    /// <summary>마을에서 하루를 나면 오르는 사기(<c>lea (%esi,%esi,2),%ecx</c>).</summary>
    public const int MoralePerRestDay = 3;

    // ── 피로도와 항해일 ───────────────────────────────────────────────────────

    /// <summary>피로도가 더 못 올라가는 자리.</summary>
    /// <remarks>
    /// 게임은 함대 객체 <c>+0x28</c> 에 0~100 으로 든다(<c>0x00474030</c> 이 그 폭으로 자른다).
    /// </remarks>
    public const int MaxFatigue = 100;

    /// <summary>선원들이 지친 만큼(0~<see cref="MaxFatigue"/>).</summary>
    /// <remarks>
    /// 폭풍을 맞으면 20~30 오르고 자택에서 휴양하면 도로 0 이 된다. 게임은 이 값이
    /// 80 을 넘으면 반란을 굴린다(<c>0x00474B5B</c> 의 <c>cmpl $0x50</c>).
    /// </remarks>
    public int Fatigue { get; private set; }

    /// <summary>그만큼 지친다.</summary>
    public void Tire(int amount) => Fatigue = Math.Clamp(Fatigue + amount, 0, MaxFatigue);

    /// <summary>피로도를 그대로 박는다. 세이브를 되돌릴 때와 개발용 창에서 쓴다.</summary>
    public void SetFatigue(int fatigue) => Fatigue = Math.Clamp(fatigue, 0, MaxFatigue);

    /// <summary>사기가 더 못 올라가는 자리.</summary>
    public const int MaxMorale = 100;

    /// <summary>선원들의 사기(0~<see cref="MaxMorale"/>). 꽉 찬 채로 시작한다.</summary>
    /// <remarks>
    /// 게임은 함대 객체 <c>+0x2C</c> 에 든다(<c>0x00474060</c> 이 0~100 으로 자른다).
    /// 폭풍이 10~20 깎고(<c>0x00474D2D</c>) 반란을 눌러 앉히면 30 오른다
    /// (<c>0x004753EA</c>). 값이 50·30·10 을 <b>아래로 지날 때</b> 게임이 한 마디씩 적지만
    /// (<c>0x004754FB</c> 아래 셋), 그건 안 보이는 힌트 판으로 가는 글이라 옮길 것이 아니다
    /// — 「대원들이 불만을 품기 시작했습니다!」·「대원들의 불만이 심해지고 있습니다!」 들이다.
    /// 0 이 되면 그 자리에서 반란이다(<c>0x00475579</c>).
    /// </remarks>
    public int Morale { get; private set; } = MaxMorale;

    /// <summary>사기를 그만큼 올린다(음수면 깎는다).</summary>
    public void Cheer(int amount) => Morale = Math.Clamp(Morale + amount, 0, MaxMorale);

    /// <summary>사기를 그대로 박는다. 세이브를 되돌릴 때와 개발용 창에서 쓴다.</summary>
    public void SetMorale(int morale) => Morale = Math.Clamp(morale, 0, MaxMorale);

    /// <summary>마을을 떠난 뒤로 바다에서 지낸 날수.</summary>
    /// <remarks>
    /// 게임의 <c>0x005A4D40</c> 자리다 — 사건은 이 값이 <b>열을 넘어야</b> 굴러가고
    /// (<c>0x00474680</c>), 반란은 이레마다 본다(<c>0x00474B4F</c> 의 <c>idiv 7</c>).
    /// 마을에 들어가면 0 으로 돌아간다.
    /// </remarks>
    public int DaysAtSea { get; private set; }

    /// <summary>
    /// 밝힌 바다. 항해지도가 이것으로 그려진다 — 안 밝힌 곳은 양피지로 남는다.
    /// </summary>
    /// <remarks>배가 지나며 저절로 칠해진다(<see cref="ExploredMap.Mark"/>).</remarks>
    public ExploredMap Explored { get; } = new();

    /// <summary>
    /// 아내 이름. 없으면 빈 문자열이다.
    /// </summary>
    /// <remarks>
    /// 게임은 <c>0x005B61B0</c> 에 아내 번호를 들고 <b>-1 이면 없는 것</b>으로 본다
    /// (<c>0x00460650</c> 이 그 값 하나로 "후손을 남긴다" 줄의 켜짐을 정한다).
    /// 우리는 아직 사람 표를 안 들고 있어 이름만 든다.
    /// </remarks>
    public string Spouse { get; private set; } = "";

    /// <summary>
    /// 아이 하나 — 게임의 아이 배열 한 칸(<c>0x005B5A28</c>, 828바이트 x 둘).
    /// </summary>
    /// <param name="Name">이름.</param>
    /// <param name="Daughter">딸인지(<c>+0x10</c> 이 1).</param>
    /// <param name="Born">태어나는 날(<c>+0xE8</c>·<c>+0xEC</c>·<c>+0xF0</c>). 잉태 열 달 뒤라 그 전에는 아직 배 속이다.</param>
    /// <param name="Abilities">능력치 여섯(<c>+0x20</c>). 세대교체하면 이것이 제독 능력치가 된다.</param>
    /// <param name="Skills">기능 열셋(<c>+0x40</c>, <see cref="Skill.Names"/> 차례).</param>
    /// <param name="Tongues">언어 열넷(<c>+0x74</c>, <see cref="Skill.Languages"/> 차례).</param>
    /// <param name="Introduced">
    /// 자택에서 이미 소개했는지. 원본에는 없는 칸이다 — 자택에 갈 때마다 <b>누구를 내보일지</b>
    /// 고르는 규칙(<c>0x004AB980</c>)을 못 짚어서, 여기서는 한 번 알린 아이는 다시 안 낸다.
    /// </param>
    /// <param name="Blood">혈액형(0 A · 1 B · 2 O · 3 AB) — 아버지와 어머니에게서 받는다(<c>0x00460FA0</c>).</param>
    /// <param name="Face">
    /// 태어날 때 고른 얼굴 줄의 <b>첫 얼굴</b>(<c>0x00460CE7</c>) — 아들 393·394, 딸 139·140 이다.
    /// 나이가 들면 같은 줄의 다음 얼굴로 바뀐다. 이 칸이 없던 옛 세이브는 −1 이다.
    /// </param>
    /// <param name="GrownFace">
    /// 자란 아들이 쓰는 얼굴(<c>아이 +0x334</c>) — 태어날 때 <b>아버지 얼굴</b>을 그대로 받는다
    /// (<c>0x00460F88</c> 이 <c>0x0047CB10</c> 의 제독 얼굴을 넣는다). 딸은 안 쓴다.
    /// </param>
    /// <param name="Drinking">
    /// 주량(0~3) — 아버지 것에 <c>rand(3) − 1</c> 을 얹어 0~3 으로 자른다(<c>0x00460E99</c>).
    /// </param>
    public sealed record Child(string Name, bool Daughter, DateTime Born, int[] Abilities, int[] Skills,
                               int[] Tongues, bool Introduced = false, int Blood = 0, int Face = -1,
                               int GrownFace = -1, int Drinking = 0)
    {
        /// <summary>그 날의 나이. 아직 안 태어났으면 음수다.</summary>
        public int AgeOn(DateTime now) =>
            now.Year - Born.Year - (now.Month < Born.Month || (now.Month == Born.Month && now.Day < Born.Day) ? 1 : 0);

        /// <summary>그 날 태어나 있는지.</summary>
        public bool IsBornBy(DateTime now) => Born <= now;
    }

    /// <summary>게임이 드는 아이 칸 수 — 둘이다.</summary>
    public const int MaxChildren = 2;

    private readonly List<Child> _children = [];

    /// <summary>아이들. 차례가 곧 잉태한 차례다.</summary>
    public IReadOnlyList<Child> Children => _children;

    /// <summary>얻은 후손 이름들.</summary>
    public IReadOnlyList<string> Heirs => [.. _children.Select(c => c.Name)];

    /// <summary>아이를 하나 더한다. 칸이 없으면 false.</summary>
    public bool AddChild(Child child)
    {
        if (_children.Count >= MaxChildren) return false;
        _children.Add(child);
        return true;
    }

    /// <summary>아이 기록을 갈아 끼운다(교육으로 기능이 올랐을 때).</summary>
    public void ReplaceChild(Child before, Child after)
    {
        int at = _children.IndexOf(before);
        if (at >= 0) _children[at] = after;
    }

    /// <summary>아이 칸을 비운다(세대교체 — <c>0x0047D640</c>).</summary>
    public void ClearChildren() => _children.Clear();

    /// <summary>아이 하나를 칸에서 뺀다(딸이 시집갈 때 — <c>0x00460180</c>). 없었으면 false.</summary>
    public bool RemoveChild(Child child) => _children.Remove(child);

    /// <summary>세이브를 되돌릴 때.</summary>
    public void RestoreChildren(IEnumerable<Child>? children)
    {
        _children.Clear();
        if (children != null) foreach (var c in children) AddChild(c);
    }

    /// <summary>
    /// 맺어진 여급의 번호. 없으면 -1.
    /// </summary>
    /// <remarks>
    /// 게임 세이브는 배우자 자리(오프셋 173, 2바이트)에 <c>0x2000 | 여급번호</c> 를 적고
    /// 빈 자리는 <c>0xFFFF</c> 다. 우리는 번호만 든다.
    /// </remarks>
    public int SpouseId { get; private set; } = -1;

    /// <summary>여급마다의 친밀도(0~100). 말을 걸어야 생긴다.</summary>
    public IReadOnlyDictionary<int, int> Liking => _liking;

    private readonly Dictionary<int, int> _liking = [];

    /// <summary>그 여급과의 친밀도. 아직 말을 안 걸었으면 0.</summary>
    public int LikingOf(int barmaid) => _liking.GetValueOrDefault(barmaid);

    /// <summary>친밀도를 올리고 내린다. 0~100 을 벗어나지 않는다.</summary>
    /// <returns>더한 뒤의 값.</returns>
    public int AddLiking(int barmaid, int amount)
    {
        int now = Math.Clamp(LikingOf(barmaid) + amount, 0, MaxLiking);
        _liking[barmaid] = now;
        return now;
    }

    /// <summary>친밀도의 위. 게임도 0~100 으로 자른다.</summary>
    public const int MaxLiking = 100;

    private readonly HashSet<int> _giftedBarmaids = [];
    private readonly HashSet<int> _refusedBarmaids = [];

    /// <summary>선물을 받아 본 여급(여급 칸 <c>+0x34</c>). 설득이 90 문턱을 넘으려면 있어야 한다.</summary>
    public IReadOnlyCollection<int> GiftedBarmaids => _giftedBarmaids;

    /// <summary>퇴짜를 놓은 여급(여급 칸 <c>+0x30</c> 이 0 이 된 것). 그 여급에게는 다시 프로포즈를 못 한다.</summary>
    public IReadOnlyCollection<int> RefusedBarmaids => _refusedBarmaids;

    /// <summary>그 여급이 선물을 받아 봤는지.</summary>
    public bool HasGifted(int barmaid) => _giftedBarmaids.Contains(barmaid);

    /// <summary>선물을 받은 것으로 적는다(<c>0x00466B45</c>).</summary>
    public void MarkGifted(int barmaid) => _giftedBarmaids.Add(barmaid);

    /// <summary>그 여급에게 퇴짜를 맞았는지.</summary>
    public bool WasRefusedBy(int barmaid) => _refusedBarmaids.Contains(barmaid);

    /// <summary>퇴짜를 맞은 것으로 적고 친밀도를 0 으로 되돌린다(<c>0x00465B9E</c> · <c>0x00466214</c>).</summary>
    public void MarkRefused(int barmaid)
    {
        _refusedBarmaids.Add(barmaid);
        _liking[barmaid] = 0;
    }

    /// <summary>세이브에서 여급 형편을 되돌린다.</summary>
    public void RestoreBarmaidFlags(IEnumerable<int>? gifted, IEnumerable<int>? refused)
    {
        _giftedBarmaids.Clear();
        foreach (int id in gifted ?? []) _giftedBarmaids.Add(id);
        _refusedBarmaids.Clear();
        foreach (int id in refused ?? []) _refusedBarmaids.Add(id);
    }

    /// <summary>아내를 맞는다. 빈 이름을 주면 홀로 돌아간다.</summary>
    public void Marry(string? name, int barmaid = -1)
    {
        Spouse = (name ?? "").Trim();
        SpouseId = Spouse.Length == 0 ? -1 : barmaid;
    }

    /// <summary>
    /// 이름만 있는 후손을 하나 얻는다 — 아이 칸이 생기기 전 세이브를 되돌릴 때 쓴다. 오늘 태어난 것으로,
    /// 첫째는 아들 · 둘째는 딸로, 능력은 비워 둔다(불러오는 쪽이 아버지 값으로 다시 채운다).
    /// </summary>
    public void AddHeir(string name)
    {
        string given = (name ?? "").Trim();
        if (given.Length == 0) return;
        AddChild(new Child(given, _children.Count > 0, Date, new int[6], new int[Skill.Names.Length],
                           new int[Skill.Languages.Length]));
    }

    /// <summary>적어 둔 것을 되돌린다.</summary>
    public void RestoreFamily(string? spouse, IEnumerable<string>? heirs, int spouseId = -1,
                              IReadOnlyDictionary<int, int>? liking = null)
    {
        Spouse = (spouse ?? "").Trim();
        SpouseId = Spouse.Length == 0 ? -1 : spouseId;
        _children.Clear();
        foreach (string h in heirs ?? []) AddHeir(h);

        _liking.Clear();
        foreach (var (id, value) in liking ?? new Dictionary<int, int>())
            _liking[id] = Math.Clamp(value, 0, MaxLiking);
    }

    /// <summary>바다에서 하루를 넘긴다.</summary>
    public void PassDayAtSea()
    {
        DaysAtSea++;
        Date = Date.AddDays(1);
        AgeCargo(1);
    }

    /// <summary>항해일을 그대로 박는다. 세이브를 되돌릴 때 쓴다.</summary>
    public void SetDaysAtSea(int days) => DaysAtSea = Math.Max(0, days);

    /// <summary>
    /// 함대에 남아 있는 해상재해. 터지면 서고, 항해가 끝나야(상륙·입항) 풀린다.
    /// </summary>
    /// <remarks>
    /// 게임은 함대 <c>+0xD4</c> 비트로 든다(세터 <c>0x00474630</c>). 항해일수를 0 으로 돌리는
    /// 세 자리(<c>0x0048B56A</c> · <c>0x0048DC4F</c> · <c>0x0048E6AD</c>)가 이 값도 함께 0 으로 둔다.
    /// </remarks>
    public SeaAilment Ailments { get; private set; }

    /// <summary>그 재해가 서 있는지.</summary>
    public bool Has(SeaAilment ailment) => (Ailments & ailment) != 0;

    /// <summary>재해를 세운다.</summary>
    public void Afflict(SeaAilment ailment) => Ailments |= ailment;

    /// <summary>재해를 모두 풀고, 풀기 전에 서 있던 것을 낸다.</summary>
    public SeaAilment CureAilments()
    {
        var was = Ailments;
        Ailments = SeaAilment.None;
        return was;
    }

    /// <summary>재해를 그대로 박는다. 세이브를 되돌릴 때 쓴다.</summary>
    public void SetAilments(int bits) =>
        Ailments = (SeaAilment)bits & (SeaAilment.Rats | SeaAilment.Scurvy | SeaAilment.Plague);

    /// <summary>
    /// 아이템을 산다. 값을 치르고 소지품에 넣는다. 돈이 모자라면 아무것도 하지 않는다.
    /// </summary>
    /// <remarks>
    /// 게임도 돈 검사를 <b>YES 를 고른 뒤</b>에 한다(구입 본체 0x004B3AAD 에서 소지금과
    /// 값을 견준다). 그래서 여기서도 물어보는 것과 치르는 것을 갈라 두었다 —
    /// 부르는 쪽이 물음창을 먼저 띄우고 이것을 마지막에 부른다.
    /// </remarks>
    public PurchaseResult BuyItem(int itemId, int price)
    {
        if (itemId < 0) return PurchaseResult.NotEnoughGold;
        if (IsBagFull) return PurchaseResult.BagFull;
        if (!CanAfford(price)) return PurchaseResult.NotEnoughGold;

        Gold -= price;
        Take(itemId);
        return PurchaseResult.Ok;
    }

    private readonly Dictionary<int, int> _hostility = [];
    private readonly HashSet<int> _openedGates = [];
    private readonly HashSet<int> _talksLost = [];

    /// <summary>
    /// 나라마다의 <b>적대도</b>. 0 이면 여느 나라다.
    /// </summary>
    /// <remarks>
    /// 게임은 나라마다 열여섯 바이트짜리 형편 레코드를 <c>0x005859C0</c> 에 두고 적대도를
    /// <c>+0x0C</c> 에 적는다(<c>0x00429D90</c>). 그 판은 <c>.bss</c> 라 켤 때는 죄다 0 이고
    /// 세이브에 통째로 실린다(<c>0x0047858E</c>, 일흔여덟 칸).
    ///
    /// <b>처음 값은 나라 표에서 온다.</b> 판을 열 때 <c>0x0041B320</c> 이 나라 표
    /// <c>0x004CA370</c> 의 <c>+0x14</c>(출입여부)를 이 칸으로 떠 준다. 그래서 그라나다와
    /// 오스만·투르크는 첫날부터 2 다. 여기서는 <b>이 딕셔너리에 아무것도 없으면 표 값이
    /// 그대로 쓰인다</b>(<c>Standoff.EntryOf</c>) — 표를 고치면 놀이에 곧장 먹는다.
    /// 1 은 마을만 막고 항구는 열며, 2 라야 항구까지 막힌다(<c>Standoff.Barred</c>).
    ///
    /// <b>아직 이 값을 올리는 데는 없다.</b> <see cref="Anger"/> 와 <see cref="Calm"/> 는
    /// 갖춰만 두었다 — 마을을 치거나 숨어들다 잡혔을 때 올리는 것은 앞으로 할 일이다.
    /// 지금은 문을 하나씩 여는 쪽으로 갈음한다(<see cref="IsGateOpen"/>).
    /// </remarks>
    public IReadOnlyDictionary<int, int> Hostility => _hostility;

    /// <summary>그 나라의 적대도. 모르는 나라면 0.</summary>
    public int HostilityOf(int nation) => _hostility.GetValueOrDefault(nation);

    /// <summary>그 나라를 성나게 한다.</summary>
    public void Anger(int nation, int by = 1)
    {
        if (nation < 0 || by <= 0) return;
        _hostility[nation] = HostilityOf(nation) + by;
    }

    /// <summary>그 나라를 달랜다.</summary>
    /// <remarks>
    /// <b>칸을 지우지 않고 0 을 적는다.</b> 지우면 나라 표의 처음 값(출입여부)이 되살아나
    /// 그라나다를 달래 놓고도 다시 막히는 꼴이 된다.
    /// </remarks>
    public void Calm(int nation)
    {
        if (nation < 0) return;
        _hostility[nation] = 0;
    }

    /// <summary>
    /// 적대 도시 가운데 <b>문이 열린</b> 곳. 공략·잠입·교섭에 성공하면 는다.
    /// </summary>
    /// <remarks>
    /// 게임의 「제독, 이것으로 마을에 들어갈 수 있습니다」(<c>0x00551C28</c>) 다. 한 번
    /// 열리면 그 도시는 적대도와 상관없이 그냥 들어간다.
    /// </remarks>
    public IReadOnlyCollection<int> OpenedGates => _openedGates;

    private readonly HashSet<int> _knownCities = [];

    /// <summary>
    /// <b>아는 도시</b> — 지도에 뜨는 도시들.
    /// </summary>
    /// <remarks>
    /// 게임은 도시 레코드 <c>+0x04</c> 의 비트 0 으로 들고 있고, 처음 켜져 있는 101곳이
    /// 죄다 유럽·지중해다. 나머지는 항해하다 가까이 가야 켜진다(<c>0x0048D983</c>).
    /// 켤 때 어디가 켜져 있는지는 도시 표가 알고 있으므로(<c>CityExeTable.KnownAtStart</c>)
    /// 여기에는 <b>놀이 중에 새로 안 것</b>만 담는다.
    /// </remarks>
    public IReadOnlyCollection<int> KnownCities => _knownCities;

    /// <summary>그 도시를 알게 되었는지 적어 둔다. 처음 아는 것이면 참.</summary>
    public bool Know(int city) => city >= 0 && _knownCities.Add(city);

    /// <summary>놀이 중에 알게 된 도시인지.</summary>
    public bool Knows(int city) => _knownCities.Contains(city);

    /// <summary>세이브에서 아는 도시를 되돌린다.</summary>
    public void RestoreKnownCities(IEnumerable<int>? cities)
    {
        _knownCities.Clear();
        foreach (int city in cities ?? []) _knownCities.Add(city);
    }

    /// <summary>그 적대 도시의 문이 이미 열렸는지.</summary>
    public bool IsGateOpen(int city) => _openedGates.Contains(city);

    /// <summary>적대 도시의 문을 연다. 처음 여는 것이면 true.</summary>
    public bool OpenGate(int city) => city >= 0 && _openedGates.Add(city);

    /// <summary>
    /// 교섭이 어그러진 자리. <b>마을 쪽과 항구 쪽을 따로</b> 센다.
    /// </summary>
    /// <remarks>
    /// 게임도 도시 레코드에 <c>+0xB0</c>(항구)와 <c>+0xB4</c>(마을) 두 자리를 둔다
    /// (<c>0x004A56A7</c>). 한 번 어그러지면 그 자리에서는 <b>「떠난다」가 꺼져</b>
    /// 물러설 수 없다.
    /// </remarks>
    public bool TalkLostAt(int city, bool byLand) => _talksLost.Contains(GateKey(city, byLand));

    /// <summary>교섭이 어그러졌다고 적어 둔다.</summary>
    public void MarkTalkLost(int city, bool byLand) => _talksLost.Add(GateKey(city, byLand));

    /// <summary>적어 둔 자리 — 마을 쪽과 항구 쪽이 다른 칸이다.</summary>
    private static int GateKey(int city, bool byLand) => city * 2 + (byLand ? 0 : 1);

    /// <summary>적어 둔 형편을 통째로 되돌린다(세이브를 읽을 때).</summary>
    public void RestoreStandings(IEnumerable<KeyValuePair<int, int>>? hostility,
                                 IEnumerable<int>? openedGates,
                                 IEnumerable<int>? talksLost)
    {
        _hostility.Clear();
        foreach (var (nation, level) in hostility ?? []) _hostility[nation] = level;

        _openedGates.Clear();
        foreach (int city in openedGates ?? []) _openedGates.Add(city);

        _talksLost.Clear();
        foreach (int key in talksLost ?? []) _talksLost.Add(key);
    }

    /// <summary>세이브에 적을 교섭 실패 자리(칸 번호 그대로).</summary>
    public IReadOnlyCollection<int> TalksLost => _talksLost;

    /// <summary>그 힌트를 이미 얻었는지.</summary>
    public bool HasHint(int hint) => _hints.Contains(hint);

    /// <summary>힌트를 얻는다. 처음 얻는 것이면 true.</summary>
    public bool GainHint(int hint) => hint >= 0 && _hints.Add(hint);

    private readonly HashSet<int> _found = [];

    /// <summary>
    /// 발견한 발견물 번호. 게임 발견물 표(274줄)의 줄 번호다.
    /// </summary>
    /// <remarks>
    /// 게임은 발견물마다 164바이트 칸을 두고 발견자·발표자 이름과 그 연월까지 적는다.
    /// 여기서는 주인공이 하나뿐이라 번호만 든다 — 발표(왕궁 보고)는 아직 흉내내지 않는다.
    /// </remarks>
    public IReadOnlyCollection<int> Discoveries => _found;

    /// <summary>그것을 이미 발견했는지.</summary>
    public bool HasFound(int discovery) => _found.Contains(discovery);

    /// <summary>
    /// 감찰관을 매수해 <b>숨겨 둔</b> 발견물(발견물 칸 <c>+0x16</c> 의 비트 <c>0x20</c>).
    /// </summary>
    /// <remarks>
    /// 보고 목록에서 빠지고(<c>0x0046B0A0</c>), 보고를 마치고 건물을 나설 때 그 증거품이
    /// 소지품으로 들어오면서 비트가 지워진다(<c>0x0044E6C0</c> → <c>0x0041C480</c>).
    /// </remarks>
    public IReadOnlyCollection<int> HiddenDiscoveries => _hidden;

    private readonly HashSet<int> _hidden = [];

    /// <summary>그것을 숨겼는지.</summary>
    public bool IsHidden(int discovery) => _hidden.Contains(discovery);

    /// <summary>숨긴 것으로 적는다.</summary>
    public void Hide(int discovery) => _hidden.Add(discovery);

    /// <summary>숨긴 표를 모두 지운다 — 증거품을 돌려받은 뒤다.</summary>
    public void ClearHidden() => _hidden.Clear();

    /// <summary>적어 둔 숨긴 발견물을 되돌린다.</summary>
    public void RestoreHidden(IEnumerable<int>? hidden)
    {
        _hidden.Clear();
        foreach (int id in hidden ?? []) _hidden.Add(id);
    }

    // ── 행적(ACCDATA) ────────────────────────────────────────────────────────

    /// <summary>
    /// 행적 한 줄 — <b>언제 무슨 일이 있었는가</b>(<c>0x0041A070</c> 이 쌓는 레코드).
    /// </summary>
    /// <param name="Kind">갈래 0~22. 낱말 수는 표 <c>0x005375C8</c> 이 정한다.</param>
    /// <param name="A">첫째 낱말. 갈래 1(입항)이면 <b>도시 번호</b>다.</param>
    /// <param name="B">둘째 낱말. 갈래 1 이면 <b>나라</b>다.</param>
    public readonly record struct Trace(DateTime On, int Kind, int A, int B);

    /// <summary>
    /// 지금까지의 행적. 은퇴하면 이것이 누적 캐릭터의 <c>ACCDATA%d.ACC</c> 가 된다.
    /// </summary>
    /// <remarks>
    /// 게임은 <c>ACCDATA.CDS</c> 에 <b>스물다섯 자리</b>에서 스물세 갈래를 쌓는다. 우리는 그 가운데
    /// 되돌려 틀 때 쓰는 <b>갈래 1(도시 입항)</b>과 <b>0x15 · 0x16(배가 들고 남)</b>만 적는다 —
    /// 나머지를 적어 봐야 읽는 데가 없다.
    /// </remarks>
    public IReadOnlyList<Trace> Traces => _traces;

    private readonly Dictionary<int, string> _namedDiscoveries = [];

    /// <summary>
    /// 대본으로 <b>이름을 지어 준</b> 발견물(<c>1F 0B</c>, <c>0x004AAB00</c>).
    /// </summary>
    /// <remarks>
    /// 게임은 이름을 발견물 레코드에 그대로 써 넣어서 그 뒤로는 어디서든 그 이름이 보인다.
    /// 우리는 표를 안 건드리고 이 칸에 적어 두었다가 판을 열 때 표에 덧씌운다.
    /// </remarks>
    public IReadOnlyDictionary<int, string> NamedDiscoveries => _namedDiscoveries;

    /// <summary>그 발견물의 이름을 적어 둔다.</summary>
    public void NameDiscovery(int id, string name)
    {
        if (id < 0 || string.IsNullOrWhiteSpace(name)) return;
        _namedDiscoveries[id] = name.Trim();
    }

    /// <summary>세이브에서 되돌린다.</summary>
    public void RestoreNamedDiscoveries(IEnumerable<KeyValuePair<int, string>>? named)
    {
        _namedDiscoveries.Clear();
        if (named == null) return;
        foreach (var (id, name) in named) NameDiscovery(id, name);
    }

    private readonly List<Trace> _traces = [];

    /// <summary>행적에 한 줄 더한다(<c>0x0041A070</c>).</summary>
    /// <remarks>같은 날 같은 곳이 잇달아 적히지는 않는다 — 도시를 드나들 때마다 불리기 때문이다.</remarks>
    public void Note(int kind, int a = 0, int b = 0)
    {
        if (kind == TraceArrival && _traces.Count > 0 && _traces[^1] is { } last
            && last.Kind == kind && last.A == a && last.B == b && last.On == Date) return;
        _traces.Add(new Trace(Date, kind, a, b));
    }

    /// <summary>적어 둔 판에서 되돌린다.</summary>
    public void RestoreTraces(IEnumerable<Trace>? traces)
    {
        _traces.Clear();
        foreach (var one in traces ?? []) _traces.Add(one);
    }

    /// <summary>행적 갈래 — 도시 입항(낱말: 도시·나라).</summary>
    /// <remarks>
    /// 원본 번호로는 <b>갈래 0</b>이다(<c>0x0049270F</c> 가 도시 화면을 펼 때마다 적는다 — 되돌려 틀면 <c>26 08 [도시]</c>
    /// 로 그 사람을 도시에 옮긴다). 앱은 옛 세이브의 행적이 이 값으로 적혀 있어 1 로 둔다.
    /// </remarks>
    public const int TraceArrival = 1;

    /// <summary>
    /// 행적 갈래 — 마을을 공략했다(낱말: 도시·나라). 원본 번호로는 <b>갈래 1</b>이다(<c>0x00468A01</c>).
    /// </summary>
    /// <remarks>
    /// 되돌려 틀면 <c>21 08 [도시] 00 [나라]</c> 로 그 도시를 그 나라로 넘기고(수도면 그 나라 도시 전부)
    /// 「%s%s [%s]%s 공략했습니다」(<c>0x005387C0</c>)를 알린다(<c>0x00409A7E</c>). 앱 번호는 입항과 겹치지 않게 따로 둔다.
    /// </remarks>
    public const int TraceCapture = 101;

    /// <summary>
    /// 행적 갈래 — 배가 함대에 들어왔다(<c>0x00473D97</c>) · 나갔다(<c>0x00473E86</c>). 낱말은 선체 번호 하나다.
    /// </summary>
    /// <remarks>
    /// 은퇴하면 명령 <c>69</c> · <c>6A</c> 로 바뀌어(<c>0x0041AAAE</c> · <c>0x0041AAB8</c>) 누적 캐릭터의
    /// 함대 목록을 채우고 지운다 — 그 사람을 습격하면 이 선체들이 나온다.
    /// </remarks>
    public const int TraceShipIn = 0x15, TraceShipOut = 0x16;

    /// <summary>발견한 것으로 적는다. 처음 발견하는 것이면 true.</summary>
    /// <remarks>
    /// 계약 중이면 그 계약에도 얹는다 — 계약 정보 창의 "발견물" 칸이 그것이다.
    /// </remarks>
    public bool Discover(int discovery)
    {
        if (discovery < 0 || !_found.Add(discovery)) return false;
        _foundOn[discovery] = Date;
        Contract?.Add(discovery);
        return true;
    }

    private readonly Dictionary<int, DateTime> _foundOn = [];

    /// <summary>
    /// 발견한 날 — 발견물 인스턴스 칸 0 의 <c>+0x28</c>(해) · <c>+0x2C</c>(달)이다. 연표가 이 날로 줄을 세운다.
    /// </summary>
    public IReadOnlyDictionary<int, DateTime> FoundOn => _foundOn;

    /// <summary>그것을 발견한 날. 안 찾았거나 날을 안 적던 옛 세이브면 null.</summary>
    public DateTime? FoundDateOf(int discovery) =>
        _foundOn.TryGetValue(discovery, out var when) ? when : null;

    /// <summary>세이브에서 발견한 날을 되돌린다.</summary>
    public void RestoreFoundDates(IReadOnlyDictionary<int, DateTime>? dates)
    {
        _foundOn.Clear();
        foreach (var (id, when) in dates ?? new Dictionary<int, DateTime>())
            if (_found.Contains(id)) _foundOn[id] = when;
    }

    /// <summary>
    /// 지금 맺고 있는 계약. 없으면 null — 도시 커맨드의 "계약 정보" 가 이것을 낸다.
    /// </summary>
    /// <remarks>게임도 계약을 하나만 든다(<c>0x0061D1D0</c>).</remarks>
    public Contract? Contract { get; private set; }

    /// <summary>
    /// 계약을 맺어 본 적 있는 힌트 — 힌트만 쥐고 있어서는 안 열리고, <b>계약까지 맺어야</b>
    /// 그 힌트가 가리키는 발견물이 자리 판정에 걸린다(<see cref="Discovery.DiscoveryLog.IsOpen"/>).
    /// </summary>
    /// <remarks>
    /// EXE 를 뜯어 보면 발견물 인스턴스의 열림 깃발(<c>+0x16</c> 비트 <c>0x08</c>)을 새 판을 열 때
    /// 말고 나중에 세우는 자리는 <b>딱 하나</b>, 계약 맺기(<c>0x004ADEE0</c>)뿐이다 —
    /// 힌트를 얻는 자리(도서관·술집)에는 그런 코드가 없다. 계약이 기한을 넘기거나 깨져도
    /// 이 깃발을 지우는 코드는 없어(<c>AND</c> 명령이 아예 없다) 한 번 열리면 계속 열려 있다.
    /// 그래서 지금 맺은 계약뿐 아니라 <b>맺어 본 적 있는 계약을 전부</b> 든다.
    /// </remarks>
    public IReadOnlyCollection<int> OpenedHints => _openedHints;

    /// <summary>
    /// 계약을 맺는다. 선금은 그 자리에서 받는다 — 게임도 그렇다(<c>0x004ADF3E</c>).
    /// 이미 맺은 것이 있으면 갈아 끼운다(게임도 계약을 하나만 든다).
    /// </summary>
    public void Sign(Contract contract)
    {
        Contract = contract;
        _openedHints.Add(contract.Hint);
        Earn(contract.Advance);
    }

    /// <summary>계약을 지운다(기한 넘김·파기). 돈은 건드리지 않는다.</summary>
    /// <remarks>
    /// <see cref="OpenedHints"/> 는 건드리지 않는다 — 원본도 계약이 끝났다고 열어 둔 발견물을
    /// 다시 잠그지는 않는다.
    /// </remarks>
    public void EndContract() => Contract = null;

    /// <summary>세이브를 되돌릴 때 계약을 맺어 본 힌트를 그대로 채운다.</summary>
    public void RestoreOpenedHints(IEnumerable<int>? openedHints)
    {
        _openedHints.Clear();
        if (openedHints != null) foreach (int hint in openedHints) _openedHints.Add(hint);
    }

    /// <summary>
    /// 감찰관을 처벌해 <b>배신한 후원자</b> 한 사람 — 게임의 후원자 플래그 비트 13(<c>0x0044FC6D</c>).
    /// </summary>
    /// <param name="Sponsor">후원자 이름.</param>
    /// <param name="City">계약을 맺었던 도시.</param>
    /// <param name="DueOn">원래 계약의 기한. 이 날이 지나야 추격이 시작된다(후원자 <c>+0x30</c> 남은 날수 ≤ 0).</param>
    public readonly record struct Betrayal(string Sponsor, string City, DateTime DueOn);

    private readonly Dictionary<string, Betrayal> _betrayals = [];

    /// <summary>배신한 후원자들. 그 후원자를 찾아가 결판을 내야(<c>0x0044FBE7</c>) 지워진다.</summary>
    public IReadOnlyCollection<Betrayal> Betrayals => _betrayals.Values;

    /// <summary>그 후원자를 배신했는지.</summary>
    public bool IsBetrayed(string sponsor) => _betrayals.ContainsKey(sponsor);

    /// <summary>후원자를 배신했다고 적는다.</summary>
    public void Betray(string sponsor, string city, DateTime dueOn) =>
        _betrayals[sponsor] = new Betrayal(sponsor, city, dueOn);

    /// <summary>결판이 났다 — 배신 표시를 지운다.</summary>
    public void SettleBetrayal(string sponsor) => _betrayals.Remove(sponsor);

    /// <summary>지금 뒤쫓고 있는 후원자들 — 원래 계약의 기한이 지난 배신이다.</summary>
    public IEnumerable<Betrayal> Pursuers => _betrayals.Values.Where(b => b.DueOn <= Date);

    /// <summary>세이브를 되돌릴 때.</summary>
    public void RestoreBetrayals(IEnumerable<Betrayal>? betrayals)
    {
        _betrayals.Clear();
        if (betrayals != null)
            foreach (var b in betrayals)
                if (!string.IsNullOrEmpty(b.Sponsor)) _betrayals[b.Sponsor] = b;
    }

    private readonly Dictionary<string, DateTime> _sulks = [];

    /// <summary>후원자가 기분이 상해 있는 날수 — 이만큼 지나면 풀린다(<c>0x004A2AD0</c>).</summary>
    public const int SulkDays = 30;

    /// <summary>
    /// 기분이 상한 후원자와 그 날 — 게임의 후원자 플래그 비트 14. 켜져 있으면 설득을 문간에서 돌려보낸다.
    /// </summary>
    public IReadOnlyDictionary<string, DateTime> Sulks => _sulks;

    /// <summary>후원자 기분을 상하게 한다(설득 거절 · 계약 결판 뒤).</summary>
    public void Sulk(string sponsor)
    {
        if (!string.IsNullOrEmpty(sponsor)) _sulks[sponsor] = Date;
    }

    /// <summary>그 후원자가 아직 기분이 상해 있는지.</summary>
    public bool IsSulking(string sponsor) =>
        _sulks.TryGetValue(sponsor, out var since) && (Date - since).TotalDays < SulkDays;

    /// <summary>세이브를 되돌릴 때.</summary>
    public void RestoreSulks(Dictionary<string, DateTime>? sulks)
    {
        _sulks.Clear();
        if (sulks != null) foreach (var (name, since) in sulks) _sulks[name] = since;
    }

    /// <summary>예금을 그 몫만 남기고 잃는다(도둑 — <c>0x00450060</c> 은 30% 만 남긴다).</summary>
    public void LoseSavings(int keepPercent) =>
        Savings = Math.Clamp(Savings * Math.Clamp(keepPercent, 0, 100) / 100, 0, MaxGold);

    /// <summary>자택 보관 칸에서 한 칸을 잃는다.</summary>
    public void LoseStored(int index)
    {
        if (index >= 0 && index < _stored.Count) _stored.RemoveAt(index);
    }

    /// <summary>소지품과 보관 칸을 다 잃는다(감옥).</summary>
    public void LoseBelongings()
    {
        _items.Clear();
        _stored.Clear();
    }

    /// <summary>계류해 둔 배까지 모든 배를 잃는다(감옥).</summary>
    public void LoseAllShips()
    {
        ClearShips();
        _docked.Clear();
    }

    private readonly HashSet<int> _activeGoods = [];

    /// <summary>
    /// 발견으로 판매가 켜진 교역품 번호 — 게임의 판매 게이트 <c>0x0058BAB0[교역품]</c> 를 1 로 세운 것들이다.
    /// </summary>
    /// <remarks>
    /// 새 판은 교역품 표 <c>+0x84</c> 로 게이트를 채우는데(<c>0x0042E2A0</c>) 상아·후추·커피 따위 27종이 꺼진 채
    /// 시작한다. 발견 대본의 <c>01 15 [교역품]</c>(<c>0x004088D8</c>)만 켜고, 끄는 코드는 없다.
    /// 여기에는 <b>대본이 켠 것만</b> 든다 — 처음부터 켜진 것은 교역소 표(<c>TradeTable.OnSale</c>)가 안다.
    /// </remarks>
    public IReadOnlyCollection<int> ActiveGoods => _activeGoods;

    /// <summary>교역품 판매를 켠다. 새로 켰으면 true.</summary>
    public bool ActivateGoods(int kind) => kind >= 0 && _activeGoods.Add(kind);

    /// <summary>발견으로 판매가 켜진 교역품인지.</summary>
    public bool IsGoodsActive(int kind) => _activeGoods.Contains(kind);

    /// <summary>세이브를 되돌릴 때 켜 둔 교역품을 그대로 채운다.</summary>
    public void RestoreActiveGoods(IEnumerable<int>? kinds)
    {
        _activeGoods.Clear();
        if (kinds != null) foreach (int kind in kinds) ActivateGoods(kind);
    }

    private readonly HashSet<int> _announced = [];

    /// <summary>발표한 발견물 번호.</summary>
    /// <remarks>
    /// 게임은 발견물 인스턴스의 깃발 <c>0x80</c> 으로 든다. 발표하면 명성이 오르고, 한 번
    /// 발표한 것은 다시 못 한다.
    /// </remarks>
    public IReadOnlyCollection<int> Announced => _announced;

    /// <summary>그것을 이미 발표했는지.</summary>
    public bool HasAnnounced(int discovery) => _announced.Contains(discovery);

    /// <summary>발표한 것으로 적는다. 발견한 적 없거나 이미 발표했으면 false.</summary>
    public bool Announce(int discovery)
    {
        if (!HasFound(discovery) || !_announced.Add(discovery)) return false;
        _announcedOn[discovery] = Date;
        // 행적에도 한 줄 남는다(0x0047E630 끝의 0x0041A070(…, 9, 발견물번호)) — 은퇴하면 누적 캐릭터의 발자취가 된다.
        Note(TraceDiscovery, discovery);
        return true;
    }

    /// <summary>행적 갈래 — 발견물을 보고·발표했다(낱말: 발견물 번호). 원본도 갈래 <b>9</b> 다.</summary>
    public const int TraceDiscovery = 9;

    private readonly Dictionary<int, string> _scooped = [];

    /// <summary>
    /// <b>남이 먼저 발표해 버린</b> 발견물 — 번호 → 그 사람 이름.
    /// </summary>
    /// <remarks>
    /// 게임은 발견물 인스턴스의 칸 2(발표자)에 그 사람 이름과 연월을 적는다
    /// (<c>0x004AACA0</c>). 칸 2 가 비어 있지 않으면 그 발견물은 이미 세상에 알려진 것이라,
    /// 내가 보고해도 명성이 안 오르고 사례가 계약금/4 로 깎인다(<c>0x004117F0</c>).
    ///
    /// 그 칸에 <b>남의</b> 이름이 올라가는 길은 누적 캐릭터뿐이다 — 은퇴한 제독의 행적 갈래
    /// 9 가 되살아날 때 <c>68 0B [발견물]</c> 로 바뀌어(<c>0x0041A7CF</c>) 그 명령
    /// (<c>0x0040B916</c>)이 이 칸을 채운다.
    /// </remarks>
    public IReadOnlyDictionary<int, string> Scooped => _scooped;

    /// <summary>그 발견물을 남이 먼저 발표했으면 그 사람 이름, 아니면 null.</summary>
    public string? ScoopedBy(int discovery) =>
        _scooped.TryGetValue(discovery, out string? who) ? who : null;

    /// <summary>
    /// 남이 발표한 것으로 적는다. 이미 아무나 발표한 것이면 false 다
    /// (<c>0x0040B983</c> 이 칸 2 가 비었을 때만 채운다).
    /// </summary>
    public bool Scoop(int discovery, string who)
    {
        if (who.Length == 0 || HasAnnounced(discovery) || _scooped.ContainsKey(discovery)) return false;
        _scooped[discovery] = who;
        return true;
    }

    /// <summary>세이브를 되돌릴 때 남이 발표한 것을 그대로 채운다.</summary>
    public void RestoreScooped(IReadOnlyDictionary<int, string>? taken)
    {
        _scooped.Clear();
        if (taken == null) return;
        foreach (var (discovery, who) in taken) _scooped[discovery] = who;
    }

    private readonly Dictionary<int, DateTime> _announcedOn = [];

    /// <summary>
    /// 보고·발표한 날 — 발견물 인스턴스 칸 2 의 <c>+0x28</c>(해) · <c>+0x2C</c>(달). 향신료·신대륙 기호품 값이
    /// 이 해부터 지난 햇수로 갈리고, 연표가 이 날로 줄을 세운다.
    /// </summary>
    public IReadOnlyDictionary<int, DateTime> AnnouncedOn => _announcedOn;

    /// <summary>그것을 발표한 해. 발표 안 했으면 null.</summary>
    public int? AnnouncedYearOf(int discovery) => AnnouncedDateOf(discovery)?.Year;

    /// <summary>그것을 발표한 날. 발표 안 했으면 null.</summary>
    public DateTime? AnnouncedDateOf(int discovery) =>
        _announced.Contains(discovery) && _announcedOn.TryGetValue(discovery, out var when) ? when : null;

    /// <summary>세이브에서 발표한 날을 되돌린다. 해만 적힌 옛 세이브는 그 해 1월로, 그것도 없으면 지금 날로 본다.</summary>
    public void RestoreAnnouncedDates(IReadOnlyDictionary<int, DateTime>? dates,
                                      IReadOnlyDictionary<int, int>? years = null)
    {
        _announcedOn.Clear();
        foreach (int id in _announced)
            _announcedOn[id] = dates != null && dates.TryGetValue(id, out var when) ? when
                : years != null && years.TryGetValue(id, out int y) ? new DateTime(y, 1, 1)
                : Date;
    }

    /// <summary>세이브를 되돌릴 때 계약을 그대로 박는다. 선금을 다시 주지 않는다.</summary>
    public void RestoreContract(Contract? contract) => Contract = contract;

    // ── 이야기(초심자 개인 퀘스트라인) ──────────────────────────────────────────

    /// <summary>
    /// 초심자(EASY)로 시작했으면 그 캐릭터의 이야기 책("이야기0"·"이야기1"). 아니면 null —
    /// 새로운 주인공(NORMAL)은 이 이야기라인을 겪지 않는다.
    /// </summary>
    public string? ActiveStoryBook { get; private set; }

    /// <summary>초심자 캐릭터를 그 이야기 책에 묶는다. NEW GAME 의 EASY 걸음이 부른다.</summary>
    public void SetActiveStoryBook(string book) => ActiveStoryBook = book;

    /// <summary>STORY 의뢰(이야기 속 심부름)의 기한. 아직 안 받았으면 null.</summary>
    public DateTime? StoryQuestDeadline { get; private set; }

    /// <summary>STORY 의뢰 기한을 오늘부터 그만큼 뒤로 잡는다.</summary>
    public void SetStoryQuestDeadline(int daysFromNow) => StoryQuestDeadline = Date.AddDays(daysFromNow);

    /// <summary>STORY 의뢰 남은 날수. 기한이 없거나 이미 지났으면 0.</summary>
    public int StoryQuestDaysLeft =>
        StoryQuestDeadline is { } due ? (int)Math.Max(0, (due - Date).TotalDays) : 0;

    private readonly Dictionary<string, int> _storyProgress = [];
    private readonly HashSet<string> _closedStoryArcs = [];

    /// <summary>이야기 장(章)마다의 진행도. 열쇠는 "{책}:{장 첫 파트}".</summary>
    public IReadOnlyDictionary<string, int> StoryProgress => _storyProgress;

    /// <summary>그 장의 진행도. 아직 없으면 0.</summary>
    public int StoryStepOf(string arcKey) => _storyProgress.GetValueOrDefault(arcKey);

    /// <summary>그 장의 진행도를 그 값으로 올린다 — 내려가지는 않는다.</summary>
    public void SetStoryStep(string arcKey, int step)
    {
        if (step > StoryStepOf(arcKey)) _storyProgress[arcKey] = step;
    }

    /// <summary>끝난 이야기 장(EndEventCompletely 를 겪은 것들). 열쇠는 <see cref="StoryProgress"/> 와 같다.</summary>
    public IReadOnlyCollection<string> ClosedStoryArcs => _closedStoryArcs;

    /// <summary>그 장이 끝났는지 — 끝난 장은 다시 트리거되지 않는다.</summary>
    public bool IsStoryArcClosed(string arcKey) => _closedStoryArcs.Contains(arcKey);

    /// <summary>그 장을 닫는다.</summary>
    public void CloseStoryArc(string arcKey) => _closedStoryArcs.Add(arcKey);

    /// <summary>세이브를 되돌릴 때.</summary>
    public void RestoreStory(string? activeBook, DateTime? questDeadline,
                             IEnumerable<KeyValuePair<string, int>>? progress,
                             IEnumerable<string>? closedArcs)
    {
        ActiveStoryBook = activeBook;
        StoryQuestDeadline = questDeadline;
        _storyProgress.Clear();
        foreach (var (key, step) in progress ?? []) _storyProgress[key] = step;
        _closedStoryArcs.Clear();
        foreach (string key in closedArcs ?? []) _closedStoryArcs.Add(key);
    }

    /// <summary>
    /// 적어 둔 것을 되돌린다(불러오기). 배는 부르는 쪽이 그 도시 앞바다에 갖다 놓는다.
    /// </summary>
    public void Restore(int gold, DateTime date, int cityId, string cityName,
                        IEnumerable<KeyValuePair<string, int>> skills,
                        IEnumerable<int>? hints = null,
                        IEnumerable<string>? mates = null,
                        IEnumerable<string>? met = null,
                        IEnumerable<int>? items = null,
                        IEnumerable<int>? supplies = null,
                        IEnumerable<int>? discoveries = null,
                        int? crew = null,
                        IEnumerable<int>? announced = null,
                        IEnumerable<int>? stored = null,
                        int? savings = null,
                        bool supplyInBarrels = false)
    {
        Gold = gold;
        Date = date;
        EnterCity(cityId, cityName);
        _skills.Clear();
        foreach (var (name, level) in skills) _skills[name] = Math.Clamp(level, 0, Skill.MaxLevel);
        _hints.Clear();
        if (hints != null) foreach (int hint in hints) _hints.Add(hint);
        // 보급은 자리째로 되돌린다. 옛 세이브에는 없으므로 그때는 빈 채로 둔다.
        // 판 16 앞의 세이브는 식량·물도 <b>통</b>으로 적혀 있어 열 배로 펴 준다.
        Array.Clear(_supplies);
        if (supplies != null)
        {
            int slot = 0;
            foreach (int value in supplies)
            {
                if (slot >= _supplies.Length) break;
                var kind = (SupplyKind)slot;
                _supplies[slot++] = Math.Max(0,
                    supplyInBarrels && Supply.Of(kind).IsDaily
                        ? Supply.UnitsOf(value) : value);
            }
        }
        // 자리째로 되돌린다 — 빈 자리가 섞여 있어도 차례가 어긋나지 않게.
        for (int i = 0; i < _mates.Length; i++) _mates[i] = "";
        if (mates != null)
        {
            int at = 0;
            foreach (var name in mates)
            {
                if (at >= _mates.Length) break;
                _mates[at++] = name ?? "";
            }
        }
        _met.Clear();
        if (met != null) foreach (var name in met) _met.Add(name);
        _items.Clear();
        if (items != null) foreach (int id in items) Take(id);
        _found.Clear();
        if (discoveries != null) foreach (int id in discoveries) _found.Add(id);
        _announced.Clear();
        if (announced != null) foreach (int id in announced) _announced.Add(id);
        _stored.Clear();
        if (stored != null) foreach (int id in stored) _stored.Add(id);
        Savings = Math.Clamp(savings ?? 0, 0, MaxGold);
        // 선원을 안 적어 둔 옛 세이브는 최저 승원으로 채운다 — 그 전까지 쓰던 값이 그것이다.
        SetCrew(crew ?? MinCrew);
    }

    /// <summary>도시에 들어가거나(이름과 함께) 바다로 나온다(-1).</summary>
    public void EnterCity(int cityId, string cityName = "")
    {
        CityId = cityId;
        CityName = cityId >= 0 ? cityName : "";
        // 마을에 들면 항해일이 끊긴다 — 게임도 입항하면 0x5A4D40 을 도로 0 으로 둔다.
        if (cityId >= 0) DaysAtSea = 0;
    }

    /// <summary>소지금(닢).</summary>
    public int Gold { get; private set; }

    /// <summary>
    /// 소지금을 그대로 박는다. 놀이 안에서 쓰는 길은 아니고 개발용 창에서만 부른다 —
    /// 돈이 도는 길(교역)을 아직 흉내내지 않아 시험하려면 넣어 줄 데가 있어야 한다.
    /// </summary>
    public void SetGold(int gold) => Gold = Math.Clamp(gold, 0, MaxGold);

    /// <summary>
    /// 아직 안 쓴 배 이름 하나. 함대에 있는 것도 맡겨 둔 것도 다 피한다.
    /// </summary>
    public string SuggestShipName() =>
        ShipNames.Suggest(_ships.Select(s => s.Name)
                                .Concat(_docked.Values.SelectMany(l => l).Select(s => s.Name)));

    /// <summary>가지고 있는 배. 산 차례대로다.</summary>
    public IReadOnlyList<Ship> Ships => _ships;

    /// <summary>
    /// 기함이 함대에서 몇째 자리인지. 배가 없으면 -1 이다.
    /// </summary>
    /// <remarks>
    /// 항구 함대편성의 "기함 변경" 으로 바꾼다. 게임은 배가 <b>두 척 이상</b>일 때만 그 줄을
    /// 켠다(<c>0x0046A220</c>) — 한 척뿐이면 바꿀 것이 없다.
    /// </remarks>
    public int Flagship { get; private set; }

    /// <summary>기함. 배가 없으면 null.</summary>
    public Ship? FlagshipHull =>
        Flagship >= 0 && Flagship < _ships.Count ? _ships[Flagship] : _ships.FirstOrDefault();

    /// <summary>
    /// 배와 선원을 다 걷는다 — <b>새 주인공은 배도 선원도 없이 시작한다</b>.
    /// </summary>
    /// <remarks>
    /// 게임도 새 놀이를 열면 함대가 비어 있고, 조선소에서 첫 배를 사야 바다에 나간다.
    /// 생성자가 카라벨 한 척을 얹어 두는 것은 세이브를 안 읽고 함대 창만 여는 길
    /// (도구 쪽) 때문이라 그대로 두고, 새 놀이만 여기서 걷는다.
    /// </remarks>
    public void ClearShips()
    {
        _ships.Clear();
        Flagship = 0;
        // 배가 없으면 정원이 0 이라 선원도 0 이다. 배를 사도 선원은 안 붙는다 —
        // 게임도 항구에서 고용해야 는다(<see cref="Buy"/> 가 선원을 안 건드린다).
        Crew = 0;
    }

    /// <summary>기함을 그 자리의 배로 바꾼다. 자리가 이상하면 false.</summary>
    public bool SetFlagship(int index)
    {
        if (index < 0 || index >= _ships.Count) return false;
        Flagship = index;
        return true;
    }

    private readonly Dictionary<int, List<Ship>> _docked = [];

    /// <summary>
    /// 그 마을에 맡겨 둔 배. 함대에서 <b>삭제</b>하면 여기로 오고, <b>편입</b>하면 도로 나간다.
    /// </summary>
    /// <remarks>
    /// 게임도 마을마다 배를 맡아 둔다 — 편입·삭제가 그 마을의 수를 세어 줄을 켠다
    /// (<c>0x0040E280(도시, 0)</c>).
    /// </remarks>
    public IReadOnlyList<Ship> DockedAt(int cityId) =>
        _docked.TryGetValue(cityId, out var list) ? list : [];

    /// <summary>한 마을에 맡길 수 있는 배의 수. 게임도 여덟이다.</summary>
    public const int MaxDocked = 8;

    /// <summary>
    /// 함대의 배를 그 마을에 맡긴다(선박 삭제). 기함만 남는 상태로는 못 만든다.
    /// </summary>
    public bool Dock(int index, int cityId)
    {
        if (_ships.Count <= 1 || index < 0 || index >= _ships.Count) return false;
        if (DockedAt(cityId).Count >= MaxDocked) return false;

        if (!_docked.TryGetValue(cityId, out var list)) _docked[cityId] = list = [];
        list.Add(_ships[index]);
        RemoveShip(index);
        return true;
    }

    /// <summary>맡겨 둔 배를 함대에 넣는다(선박 편입).</summary>
    public bool Undock(int cityId, int index)
    {
        if (IsFleetFull) return false;
        if (!_docked.TryGetValue(cityId, out var list)) return false;
        if (index < 0 || index >= list.Count) return false;

        _ships.Add(list[index]);
        Note(TraceShipIn, list[index].Hull.GameId);
        list.RemoveAt(index);
        return true;
    }

    /// <summary>배를 없앤다(선박 파기). 마지막 한 척은 못 없앤다.</summary>
    public bool Scrap(int index)
    {
        if (_ships.Count <= 1 || index < 0 || index >= _ships.Count) return false;
        RemoveShip(index);
        return true;
    }

    /// <summary>
    /// 적어 둔 함대를 되돌린다(불러오기). 이름으로 <see cref="Hull.All"/> 에서 찾는다.
    /// </summary>
    /// <remarks>
    /// 배는 선체 다섯 가지 중 하나라 이름만 적어 두면 된다. 모르는 이름은 버린다 —
    /// 선체 표가 갈려도 세이브가 통째로 깨지지는 않게.
    /// </remarks>
    public void RestoreFleet(IEnumerable<string>? ships, int flagship,
                             IEnumerable<KeyValuePair<int, List<string>>>? docked,
                             IReadOnlyList<int>? shipHp = null,
                             IReadOnlyDictionary<int, List<int>>? dockedHp = null,
                             IReadOnlyList<Ship.Stats>? shipStats = null,
                             IReadOnlyDictionary<int, List<Ship.Stats>>? dockedStats = null,
                             IReadOnlyList<string>? shipNames = null,
                             IReadOnlyDictionary<int, List<string>>? dockedNames = null,
                             bool gunsInStats = true,
                             bool sailsInStats = true)
    {
        // 해전에서 빼앗은 코구·다우 따위는 조선소 선체에 없어 선체표에서 살린다.
        static Hull? Find(string name) => Hull.All.FirstOrDefault(h => h.Name == name) ?? Hull.FromTableName(name);

        List<Ship> Build(IEnumerable<string> hulls, IReadOnlyList<int>? hps,
                         IReadOnlyList<Ship.Stats>? stats, IReadOnlyList<string>? names)
        {
            var list = new List<Ship>();
            int at = 0;
            foreach (var hull in hulls)
            {
                int? hp = hps != null && at < hps.Count ? hps[at] : null;
                var st = stats != null && at < stats.Count ? stats[at] : null;
                var nm = names != null && at < names.Count ? names[at] : null;
                at++;
                // 판 18·19 앞 세이브에는 포탑·대포·돛 칸이 없다 — 선체 기본값으로 되살린다.
                if (st != null && !gunsInStats)
                    st = st with { Turrets = Find(hull)?.Guns ?? 0, Gun = -1, Guns = 0 };
                if (st != null && !sailsInStats)
                    st = st with { Sails = [Ship.Lateen, Ship.NoSail, Ship.NoSail] };
                if (Find(hull) is { } found) list.Add(new Ship(found, hp, st, nm));
            }
            return list;
        }

        if (ships != null)
        {
            _ships.Clear();
            _ships.AddRange(Build(ships, shipHp, shipStats, shipNames));
            if (_ships.Count == 0) _ships.Add(new Ship(Hull.Cheapest, name: ShipNames.All[0]));
        }
        Flagship = Math.Clamp(flagship, 0, Math.Max(0, _ships.Count - 1));

        _docked.Clear();
        if (docked == null) return;
        foreach (var (city, names) in docked)
        {
            var list = Build(names,
                dockedHp != null && dockedHp.TryGetValue(city, out var h) ? h : null,
                dockedStats != null && dockedStats.TryGetValue(city, out var t) ? t : null,
                dockedNames != null && dockedNames.TryGetValue(city, out var n) ? n : null);
            if (list.Count > 0) _docked[city] = list;
        }
    }

    /// <summary>맡겨 둔 배를 마을별로. 세이브에 적을 때 쓴다.</summary>
    public IReadOnlyDictionary<int, List<Ship>> Docked => _docked;

    /// <summary>배를 폭풍에 놓친다. 마지막 한 척과 기함은 안 없어진다.</summary>
    /// <remarks>게임의 <c>0x00473E60</c> 자리다 — 함대에서 빼기만 하고 마을에 안 맡긴다.</remarks>
    public bool LoseShip(int index)
    {
        if (_ships.Count <= 1 || index < 0 || index >= _ships.Count) return false;
        if (index == Flagship) return false;
        RemoveShip(index);
        SetCrew(Crew);   // 배가 줄면 정원도 줄어 선원이 넘칠 수 있다
        return true;
    }

    /// <summary>
    /// 배 한 척을 함대에 들인다 — 해전 끝 「선박 편입」(<c>0x00473D50</c>). 꽉 찼으면 false.
    /// </summary>
    public bool Enlist(Ship ship)
    {
        if (IsFleetFull || _ships.Contains(ship)) return false;
        _ships.Add(ship);
        Note(TraceShipIn, ship.Hull.GameId);
        return true;
    }

    /// <summary>
    /// 그 배를 함대에서 뺀다 — 해전에서 잃은 배·「선박 삭제」(<c>0x00473E30</c>). 마지막 한 척은 못 뺀다.
    /// </summary>
    /// <remarks>선원은 안 건드린다 — 부르는 쪽이 배마다의 몫과 함께 맞춘다.</remarks>
    public bool Release(Ship ship)
    {
        int index = _ships.IndexOf(ship);
        if (_ships.Count <= 1 || index < 0) return false;
        RemoveShip(index);
        return true;
    }

    /// <summary>
    /// 함대에 편입해 둔 빌린 배의 척수(<c>0x0040FB80</c>) — <b>항구에 대 놓은 것은 안 친다</b>.
    /// </summary>
    /// <remarks>
    /// 게임은 함대 자리 여덟을 돌며 배마다 <c>0x0040FBE0</c> 으로 거른다 — 빌려준 사람이
    /// 지금 그 자리에 앉은 후원자일 때만 센다. 우리는 계약이 하나뿐이라 <c>Lent</c> 만 본다.
    /// </remarks>
    public int LentInFleet => _ships.Count(s => s.Lent);

    /// <summary>
    /// 빌린 배를 모두 거둬 간다(<c>0x0040FE40</c>) — 계약이 끝나는 자리마다 돈다.
    /// </summary>
    /// <remarks>
    /// 함대에 편입해 둔 것도, 항구에 대 놓은 것도 다 가져간다. <b>남는 배가 없어도 거둬 간다</b> —
    /// 게임은 그때 짐을 대신 팔아 준다(부르는 쪽이 한다).
    /// </remarks>
    /// <returns>거둬 간 척수.</returns>
    public int TakeBackLentShips()
    {
        int taken = 0;
        for (int i = _ships.Count - 1; i >= 0; i--)
            if (_ships[i].Lent) { RemoveShip(i); taken++; }

        foreach (var (city, list) in _docked)
            taken += list.RemoveAll(s => s.Lent);
        foreach (int city in _docked.Where(e => e.Value.Count == 0).Select(e => e.Key).ToList())
            _docked.Remove(city);

        return taken;
    }

    /// <summary>실어 둔 짐을 몽땅 내린다 — 빌린 배를 다 돌려주고 배가 없을 때다.</summary>
    public void DropAllCargo() => _cargo.Clear();

    /// <summary>함대에서 한 척을 뺀다. 기함 자리가 밀리지 않게 같이 손본다.</summary>
    private void RemoveShip(int index)
    {
        Note(TraceShipOut, _ships[index].Hull.GameId);
        _ships.RemoveAt(index);
        if (Flagship > index) Flagship--;
        else if (Flagship == index) Flagship = 0;
        Flagship = Math.Clamp(Flagship, 0, Math.Max(0, _ships.Count - 1));
    }

    // ── 보급 ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// 실어 둔 보급품. 색인은 <see cref="SupplyKind"/> 고, <b>값은 게임 원값</b>이다 —
    /// 식량·물은 <b>단위</b>, 자재·탄약은 통이다.
    /// </summary>
    /// <remarks>
    /// 게임도 식량·물만 열 배로 들고 화면에 낼 때 <c>(값 + 9) / 10</c> 으로 통을 낸다
    /// (<c>0x0040EA15</c>). 하루 소모가 통보다 잘아서 통으로만 들면 셀 수가 없다.
    /// </remarks>
    private readonly int[] _supplies = new int[Supply.Count];

    /// <summary>그 보급품을 몇 통 실었는지 — <b>화면에 내는 값</b>이다.</summary>
    public int SupplyOf(SupplyKind kind) =>
        Supply.Of(kind).IsDaily ? Supply.BarrelsOf(_supplies[(int)kind]) : _supplies[(int)kind];

    /// <summary>속으로 든 값 그대로. 하루 소모와 세이브가 이것을 쓴다.</summary>
    public int SupplyUnitsOf(SupplyKind kind) => _supplies[(int)kind];

    /// <summary>보급품을 그만큼 싣는다(통, 음수면 던다). 0 밑으로는 안 내려간다.</summary>
    public void AddSupply(SupplyKind kind, int barrels) =>
        AddSupplyUnits(kind, Supply.Of(kind).IsDaily ? barrels * Supply.UnitsPerBarrel : barrels);

    /// <summary>보급품을 원값으로 그만큼 더한다(음수면 던다).</summary>
    public void AddSupplyUnits(SupplyKind kind, int units) =>
        _supplies[(int)kind] = Math.Max(0, _supplies[(int)kind] + units);

    /// <summary>실어 둔 것을 통 수로 박는다.</summary>
    public void SetSupply(SupplyKind kind, int barrels) =>
        SetSupplyUnits(kind, Supply.Of(kind).IsDaily ? Supply.UnitsOf(barrels) : barrels);

    /// <summary>실어 둔 것을 원값으로 박는다. 세이브를 되돌릴 때 쓴다.</summary>
    public void SetSupplyUnits(SupplyKind kind, int units) =>
        _supplies[(int)kind] = Math.Max(0, units);

    /// <summary>실어 둔 보급품을 원값으로 통째로. 세이브에 적을 때 쓴다.</summary>
    public IReadOnlyList<int> Supplies => _supplies;

    private readonly int[] _lastSupply = new int[Supply.Count];

    /// <summary>
    /// 지난번 항구 보급에서 <b>맞춘 통 수</b> — 보급 창 「전회분」이 이것으로 되돌린다.
    /// </summary>
    /// <remarks>
    /// 게임은 함대 전역에 넷을 든다 — 식량 <c>0x005B3970</c> · 물 <c>0x005B396C</c> · 자재 <c>0x005B3974</c> ·
    /// 탄약 <c>0x005B3978</c>(읽기 <c>0x0040E960</c> · 쓰기 <c>0x0040E9B0</c>). 보급을 결정할 때
    /// <c>0x0040F541</c> 이 그때 맞춘 총량을 적는다. 새 판은 0 이다.
    /// </remarks>
    public IReadOnlyList<int> LastSupply => _lastSupply;

    /// <summary>이번 보급에서 맞춘 통 수를 적는다(색인은 <see cref="SupplyKind"/>).</summary>
    public void SetLastSupply(IReadOnlyList<int>? barrels)
    {
        for (int i = 0; i < _lastSupply.Length; i++)
            _lastSupply[i] = barrels != null && i < barrels.Count ? Math.Max(0, barrels[i]) : 0;
    }

    // ── 교역품 짐 ────────────────────────────────────────────────────────────

    /// <summary>짐 칸 하나 — 교역품 종류 · 갯수 · 원산지 도시 · 한 개 무게 · 유통 기한.</summary>
    /// <remarks>
    /// 게임은 함대 전역 객체(<c>0x005B3928</c>)의 <c>+0x54</c> 에 <c>{종류, 갯수, 원산지, 기한}</c>
    /// 16바이트 x 8칸을 둔다(읽기 <c>0x004742B0</c> · 쓰기 <c>0x004742F0</c>). 무게는 교역품 표에 있지만
    /// 이 모델은 그 표를 모르므로 실을 때 함께 적어 둔다.
    ///
    /// <b>기한</b>은 남은 날수다 — 살 때 교역품 표 수명(달) x 30 으로 차고(<c>0x004B5910</c>), 날마다 하나씩
    /// 준다(<c>0x004759ED</c>). <see cref="NeverSpoils"/>(210)는 「안 썩음」 표시라 안 준다 — 수명이 7달이
    /// 아닌 어육·쇠고기·포도주·말·노예만 썩는다. 0 이 되어도 짐은 그대로 남고 <b>팔 값만 0</b> 이다.
    /// 옛 세이브의 짐은 기한이 없어 안 썩는 것으로 연다.
    /// </remarks>
    public readonly record struct Cargo(int Kind, int Count, int Origin, int UnitWeight,
                                        int Shelf = NeverSpoils)
    {
        /// <summary>썩어서 팔 값이 없는지.</summary>
        public bool Spoiled => Shelf <= 0;

        /// <summary>화면에 내는 달수 — <c>(기한 + 29) / 30</c>, 0 이하면 0(<c>0x004B58F0</c>).</summary>
        public int Months => Shelf <= 0 ? 0 : (Shelf + 29) / 30;
    }

    /// <summary>「안 썩음」 기한 — 수명 7달 x 30(<c>0x004759F4</c> 의 <c>cmp 0xD2</c>).</summary>
    public const int NeverSpoils = 210;

    /// <summary>짐 기한을 날수만큼 줄인다 — 0 에서 멈추고 안 썩는 것은 그대로다(<c>0x004759ED</c>).</summary>
    public void AgeCargo(int days)
    {
        if (days <= 0) return;
        for (int i = 0; i < _cargo.Count; i++)
        {
            var c = _cargo[i];
            if (c.Shelf == NeverSpoils) continue;
            _cargo[i] = c with { Shelf = Math.Max(0, c.Shelf - days) };
        }
    }

    /// <summary>짐 칸 수. <b>함대 통틀어</b> 여덟이다 — 배마다가 아니다(<c>0x004B5780</c>).</summary>
    public const int CargoSlots = 8;

    private readonly List<Cargo> _cargo = [];

    /// <summary>실은 교역품. 앞에서부터 차고, 덜어 내어 빈 칸은 뒤가 당겨 붙는다.</summary>
    public IReadOnlyList<Cargo> CargoHold => _cargo;

    /// <summary>실은 교역품 갯수 — 용량을 이만큼 먹는다(<c>0x00474430</c>).</summary>
    public int CargoCount => _cargo.Sum(c => c.Count);

    /// <summary>실은 교역품 무게 — 갯수 x 한 개 무게(<c>0x00474330</c>).</summary>
    public int CargoWeight => _cargo.Sum(c => c.Count * c.UnitWeight);

    /// <summary>
    /// 교역품을 싣는다. 같은 종류 · 같은 원산지 · <b>같은 기한</b> 칸이 있으면 합치고, 없으면 빈 칸에 넣는다
    /// (게임 <c>0x004B5830</c> AddCargo — 짝 찾기 <c>0x004B5750</c> 이 기한까지 본다). 칸이 없으면 false.
    /// </summary>
    public bool LoadCargo(int kind, int count, int origin, int unitWeight, int shelf = NeverSpoils)
    {
        if (kind < 0 || count <= 0) return false;
        int at = _cargo.FindIndex(c => c.Kind == kind && c.Origin == origin && c.Shelf == shelf);
        if (at >= 0)
        {
            _cargo[at] = _cargo[at] with { Count = _cargo[at].Count + count };
            return true;
        }
        if (_cargo.Count >= CargoSlots) return false;
        _cargo.Add(new Cargo(kind, count, origin, Math.Max(0, unitWeight), shelf));
        return true;
    }

    /// <summary>그 칸에서 그만큼 덜어 낸다. 다 덜면 칸을 빼고 뒤를 당긴다.</summary>
    public bool UnloadCargo(int slot, int count)
    {
        if (slot < 0 || slot >= _cargo.Count || count <= 0 || count > _cargo[slot].Count) return false;
        int left = _cargo[slot].Count - count;
        if (left == 0) _cargo.RemoveAt(slot);
        else _cargo[slot] = _cargo[slot] with { Count = left };
        return true;
    }

    private readonly Dictionary<int, int[]> _tradeStock = [];

    /// <summary>
    /// 교역소 재고 — 도시 번호마다 <c>[공통품 다섯 칸, 특산품, 적은 달]</c> 일곱 칸.
    /// </summary>
    /// <remarks>
    /// 게임 도시 레코드(<c>0x005863A8</c> + 도시 x 92)의 <c>+0x44~+0x54</c>(공통품)와
    /// <c>+0x18</c>(특산품)이다. 게임은 매달 1일에 한도까지 통째로 다시 채우므로(<c>0x0042A280</c>)
    /// 끝 칸에 그 재고를 적은 달을 둔다 — 달이 바뀌었거나 여기 없는 도시는 규칙 쪽이 한도로 본다.
    /// </remarks>
    public IReadOnlyDictionary<int, int[]> TradeStock => _tradeStock;

    /// <summary>한 벌 칸 수. 공통품 다섯 · 특산품 하나 · 적은 달 하나.</summary>
    public const int TradeStockCells = 7;

    /// <summary>그 도시 재고를 박는다. 칸 수가 모자라면 받지 않는다.</summary>
    public void SetTradeStock(int cityId, IReadOnlyList<int> cells)
    {
        if (cityId < 0 || cells.Count < TradeStockCells) return;
        _tradeStock[cityId] = [.. cells.Take(TradeStockCells).Select(v => Math.Max(0, v))];
    }

    /// <summary>세이브에서 재고를 되돌린다. 옛 세이브(null)면 다들 처음 재고다.</summary>
    public void RestoreTradeStock(IReadOnlyDictionary<int, List<int>>? stock)
    {
        _tradeStock.Clear();
        foreach (var (city, cells) in stock ?? new Dictionary<int, List<int>>())
            SetTradeStock(city, cells);
    }

    // ── 도시 시세 · 도시 상태 ───────────────────────────────────────────────

    /// <summary>시세 기준값. 이 값인 도시는 표에 안 둔다.</summary>
    public const int ParRate = 100;

    private readonly Dictionary<int, int> _cityRates = [];

    /// <summary>
    /// 100 에서 벗어난 도시 시세 — 도시 레코드(<c>0x005863A8</c> + 도시 x 92)의 <c>+0x0C</c>.
    /// </summary>
    /// <remarks>
    /// 첫값은 도시 표 <c>+0x2C</c> 로 어디나 100 이다. 거래가 밀고(<c>0x00481430</c>) 매달 1일에
    /// 흔들린다(<c>0x0042A280</c>). 없는 도시는 100 이다.
    /// </remarks>
    public IReadOnlyDictionary<int, int> CityRates => _cityRates;

    /// <summary>그 도시 시세. 모르는 도시는 100.</summary>
    public int CityRateOf(int cityId) => _cityRates.TryGetValue(cityId, out int rate) ? rate : ParRate;

    /// <summary>그 도시 시세를 박는다. 100 이면 표에서 지운다.</summary>
    public void SetCityRate(int cityId, int rate)
    {
        if (cityId < 0) return;
        if (rate == ParRate) _cityRates.Remove(cityId);
        else _cityRates[cityId] = rate;
    }

    /// <summary>시세를 마지막으로 흔든 달(해 x 12 + 달). 0 이면 아직 안 셌다 — 지금 달부터 센다.</summary>
    public int RatesMonth { get; private set; }

    /// <summary>시세를 흔든 달을 적는다.</summary>
    public void SetRatesMonth(int monthKey) => RatesMonth = Math.Max(0, monthKey);

    /// <summary>세이브에서 시세를 되돌린다. 옛 세이브(null)면 다들 100 이다.</summary>
    public void RestoreCityRates(IReadOnlyDictionary<int, int>? rates, int? month)
    {
        _cityRates.Clear();
        foreach (var (city, rate) in rates ?? new Dictionary<int, int>()) SetCityRate(city, rate);
        RatesMonth = Math.Max(0, month ?? 0);
    }

    private readonly Dictionary<int, int> _cityStates = [];

    /// <summary>
    /// 통상(0)이 아닌 도시 상태 — 도시 레코드 <c>+0x40</c>. 기한이 없어 대본이 바꿀 때까지 그대로다.
    /// </summary>
    public IReadOnlyDictionary<int, int> CityStates => _cityStates;

    /// <summary>그 도시 상태(0 통상 ~ 13 대조선).</summary>
    public int CityStateOf(int cityId) => _cityStates.TryGetValue(cityId, out int state) ? state : 0;

    /// <summary>그 도시 상태를 박는다. 0(통상)이면 표에서 지운다.</summary>
    public void SetCityState(int cityId, int state)
    {
        if (cityId < 0) return;
        if (state == 0) _cityStates.Remove(cityId);
        else _cityStates[cityId] = state;
    }

    /// <summary>세이브에서 도시 상태를 되돌린다.</summary>
    public void RestoreCityStates(IReadOnlyDictionary<int, int>? states)
    {
        _cityStates.Clear();
        foreach (var (city, state) in states ?? new Dictionary<int, int>()) SetCityState(city, state);
    }

    /// <summary>
    /// 역사 대본(<c>HIST_EV.CDS</c> · <c>HISTCHR.CDS</c>)을 마지막으로 돌린 달(해 x 12 + 달).
    /// 0 이면 아직 안 돌렸다 — 놀이 첫 달부터 되짚는다.
    /// </summary>
    public int HistoryMonth { get; private set; }

    /// <summary>역사 대본을 돌린 달을 적는다.</summary>
    public void SetHistoryMonth(int monthKey) => HistoryMonth = Math.Max(0, monthKey);

    private readonly Dictionary<int, int> _historyNations = [];

    /// <summary>역사 대본(<c>21 08 [도시] 00 [나라]</c>)이 바꾼 도시의 나라. 대본의 나라 조건이 본다.</summary>
    public IReadOnlyDictionary<int, int> HistoryNations => _historyNations;

    /// <summary>역사 대본이 바꾼 나라를 적는다.</summary>
    public void SetHistoryNation(int cityId, int nation)
    {
        if (cityId >= 0) _historyNations[cityId] = nation;
    }

    private readonly Dictionary<int, int> _cityScales = [];

    /// <summary>
    /// 역사 대본(<c>19/1A 08 [도시] 1A [값]</c>)이 바꾼 도시 규모(0~7) — 도시 레코드 <c>+0x08</c>. 없는 도시는 표 첫값이다.
    /// </summary>
    public IReadOnlyDictionary<int, int> CityScales => _cityScales;

    /// <summary>바뀐 도시 규모를 적는다.</summary>
    public void SetCityScale(int cityId, int scale)
    {
        if (cityId >= 0) _cityScales[cityId] = Math.Clamp(scale, 0, 7);
    }

    /// <summary>세이브에서 바뀐 도시 규모를 되돌린다.</summary>
    public void RestoreCityScales(IReadOnlyDictionary<int, int>? scales)
    {
        _cityScales.Clear();
        foreach (var (city, scale) in scales ?? new Dictionary<int, int>()) SetCityScale(city, scale);
    }

    /// <summary>도시 소문 한 줄 — 역사 대본(<c>20 0A</c>)이 그 도시에 적어 둔 말.</summary>
    public readonly record struct Rumor(int City, DateTime Added, string Text);

    private readonly List<Rumor> _rumors = [];

    /// <summary>
    /// 도시 소문 가게(<c>0x005AA298</c>) — 0x10000 바이트 고리 버퍼라 넘치면 오래된 것부터 지워지고,
    /// 적은 날로부터 <see cref="RumorDays"/> 일이 지나면 안 보인다(<c>0x0044E3E0</c>).
    /// </summary>
    public IReadOnlyList<Rumor> Rumors => _rumors;

    /// <summary>소문이 살아 있는 날 수.</summary>
    public const int RumorDays = 180;

    /// <summary>소문 가게 크기(바이트). 한 줄은 글(CP949) 길이 + 9 바이트다.</summary>
    private const int RumorBytes = 0x10000;

    /// <summary>소문을 적는다(<c>0x0044E1D0</c>). 넘치면 오래된 줄부터 밀어낸다.</summary>
    /// <param name="added">적는 날 — 밀린 달을 되짚을 때는 그 달이다.</param>
    public void AddRumor(int city, string text, DateTime added)
    {
        if (city < 0 || text.Length == 0) return;
        _rumors.Add(new Rumor(city, added, text));
        static int Size(Rumor r) => r.Text.Length * 2 + 9;
        int total = _rumors.Sum(Size);
        while (total > RumorBytes && _rumors.Count > 1)
        {
            total -= Size(_rumors[0]);
            _rumors.RemoveAt(0);
        }
    }

    /// <summary>그 도시의 살아 있는 소문(적은 차례).</summary>
    public List<string> RumorsOf(int city) =>
        [.. _rumors.Where(r => r.City == city && (Date - r.Added).TotalDays < RumorDays).Select(r => r.Text)];

    /// <summary>세이브에서 소문을 되돌린다.</summary>
    public void RestoreRumors(IEnumerable<Rumor>? rumors, IEnumerable<Rumor>? personLines = null)
    {
        _rumors.Clear();
        if (rumors != null) _rumors.AddRange(rumors);
        _personLines.Clear();
        foreach (var line in personLines ?? []) _personLines[line.City] = line;
    }

    private readonly Dictionary<int, Rumor> _personLines = [];

    /// <summary>
    /// 역사 항해자가 제 입으로 하는 말(<c>0x005AA278</c>) — 사람마다 <b>마지막 한 줄</b>만 남고(<c>0x0040A0C4</c> 가
    /// 지우고 적는다) 180일이 지나면 안 보인다. <see cref="Rumor.City"/> 칸에 인물 번호를 담는다.
    /// </summary>
    public IReadOnlyCollection<Rumor> PersonLines => _personLines.Values;

    /// <summary>그 사람의 말을 적는다(<c>26 0A [글]</c>, <c>0x0040A082</c>).</summary>
    public void SetPersonLine(int person, string text, DateTime added)
    {
        if (person >= 0 && text.Length > 0) _personLines[person] = new Rumor(person, added, text);
    }

    /// <summary>그 사람의 살아 있는 말. 없으면 null.</summary>
    public string? PersonLineOf(int person) =>
        _personLines.TryGetValue(person, out var line) && (Date - line.Added).TotalDays < RumorDays
            ? line.Text : null;

    private readonly Dictionary<int, int> _nationStatus = [];

    /// <summary>
    /// 역사 대본이 바꾼 나라 형편(나라 레코드 <c>+0x04</c>) — 1 등장(<c>26 00</c>) · 2 멸망(<c>22 00</c>). 없는 나라는 표 첫값.
    /// </summary>
    public IReadOnlyDictionary<int, int> NationStatus => _nationStatus;

    /// <summary>나라 형편을 적는다.</summary>
    public void SetNationStatus(int nation, int status)
    {
        if (nation >= 0) _nationStatus[nation] = status;
    }

    /// <summary>그 나라가 역사 대본으로 멸망했는지.</summary>
    public bool IsNationFallen(int nation) => _nationStatus.TryGetValue(nation, out int s) && s == 2;

    /// <summary>세이브에서 나라 형편을 되돌린다.</summary>
    public void RestoreNationStatus(IReadOnlyDictionary<int, int>? status)
    {
        _nationStatus.Clear();
        foreach (var (nation, s) in status ?? new Dictionary<int, int>()) SetNationStatus(nation, s);
    }

    private readonly Dictionary<int, bool> _scriptedCities = [];

    /// <summary>
    /// 발견 대본이 <b>세우거나(<c>26 08</c>) 없앤(<c>22 08</c>)</b> 도시 — 참이면 세움.
    /// </summary>
    /// <remarks>
    /// 게임은 도시 레코드 <c>+0x04</c> 의 비트 4(「안 서 있다」)를 지우고(<c>0x0040A038</c>)
    /// 세운다(<c>0x00409DC8</c>). 날짜로 되짚는 <c>CityFounding</c> 보다 이 값이 먼저다.
    /// 아스텍을 무너뜨리면(발견 이벤트 263) 테노치티틀란이 없어지고 멕시코가 선다.
    /// </remarks>
    public IReadOnlyDictionary<int, bool> ScriptedCities => _scriptedCities;

    /// <summary>발견 대본이 세운(참)·없앤(거짓) 도시를 적는다.</summary>
    public void SetScriptedCity(int cityId, bool standing)
    {
        if (cityId >= 0) _scriptedCities[cityId] = standing;
    }

    /// <summary>세이브에서 발견 대본이 세우고 없앤 도시를 되돌린다.</summary>
    public void RestoreScriptedCities(IReadOnlyDictionary<int, bool>? cities)
    {
        _scriptedCities.Clear();
        foreach (var (city, standing) in cities ?? new Dictionary<int, bool>()) SetScriptedCity(city, standing);
    }

    private readonly Dictionary<int, int> _cityBuildings = [];

    /// <summary>역사 대본(<c>22/26 10 [비트] 08 [도시]</c>)이 바꾼 건물 낱말 — 도시 레코드 <c>+0x1C</c>.</summary>
    public IReadOnlyDictionary<int, int> CityBuildings => _cityBuildings;

    /// <summary>바뀐 건물 낱말을 적는다.</summary>
    public void SetCityBuildings(int cityId, int word)
    {
        if (cityId >= 0) _cityBuildings[cityId] = word & 0xFFFF;
    }

    /// <summary>세이브에서 바뀐 건물 낱말을 되돌린다.</summary>
    public void RestoreCityBuildings(IReadOnlyDictionary<int, int>? words)
    {
        _cityBuildings.Clear();
        foreach (var (city, word) in words ?? new Dictionary<int, int>()) SetCityBuildings(city, word);
    }

    private readonly HashSet<int> _historyDone = [];

    /// <summary>
    /// 「그 달부터」(<c>1B</c>) 조건으로 한 번 돈 역사 대본 칸(파트 x 100 + 칸). 게임은 몸통이 조건을 깨서
    /// 다시 안 돌게 하는데, 우리는 도시 세우기를 따로 셈하므로 한 번 돈 것을 적어 둔다.
    /// </summary>
    public IReadOnlyCollection<int> HistoryDone => _historyDone;

    /// <summary>그 칸을 돈 것으로 적는다.</summary>
    public void MarkHistoryDone(int slotKey) => _historyDone.Add(slotKey);

    /// <summary>세이브에서 역사 대본 진행을 되돌린다. 옛 세이브(null)면 첫 달부터 다시 되짚는다.</summary>
    public void RestoreHistory(int? month, IReadOnlyDictionary<int, int>? nations, IEnumerable<int>? done)
    {
        HistoryMonth = Math.Max(0, month ?? 0);
        // 역사를 처음부터 되짚는 옛 세이브면 규모·나라도 첫값부터 다시 쌓는다.
        if (HistoryMonth == 0)
        {
            _cityScales.Clear();
            _cityBuildings.Clear();
            _nationStatus.Clear();
            _rumors.Clear();
            _personLines.Clear();
        }
        _historyNations.Clear();
        foreach (var (city, nation) in nations ?? new Dictionary<int, int>()) SetHistoryNation(city, nation);
        _historyDone.Clear();
        foreach (int key in done ?? []) _historyDone.Add(key);
    }

    /// <summary>세이브에서 짐을 되돌린다. 옛 세이브(null)면 빈 채로 둔다.</summary>
    public void RestoreCargo(IEnumerable<Cargo>? cargo)
    {
        _cargo.Clear();
        foreach (var c in cargo ?? [])
            if (c.Kind >= 0 && c.Count > 0 && _cargo.Count < CargoSlots) _cargo.Add(c);
    }

    /// <summary>
    /// 함대가 실을 수 있는 통 수(용량) — <b>포탑이 먹고 남은 것</b>이다.
    /// </summary>
    /// <remarks>
    /// 배 게터(<c>0x0044C910</c>)가 적재용량에서 포탑 수를 빼서 준다. 예전에는 안 빼서
    /// <b>포탑 수만큼 더 실을 수 있었다</b>.
    /// </remarks>
    public int Capacity => _ships.Sum(s => s.UsableCapacity);

    /// <summary>함대가 견디는 무게(중량 한도).</summary>
    public int Tonnage => _ships.Sum(s => s.Tonnage);

    /// <summary>
    /// 지금 태우고 있는 선원 수. 식량·물이 며칠 가는지가 이것으로 갈린다.
    /// </summary>
    /// <remarks>
    /// 게임도 배 여덟 칸의 선원수를 더한다(<c>0x004745F0</c>). 항구의 "선원편성" 에서
    /// 모집하고 해고한다 — 배를 사도 선원이 따라오지는 않는다.
    /// </remarks>
    public int Crew { get; private set; }

    /// <summary>
    /// 최저 승원 수 — 배마다의 필요승인을 더한 것. 이보다 적으면 배가 제대로 안 간다.
    /// </summary>
    /// <remarks>
    /// 게임은 배 레코드 <c>+0x30</c> 에 <b>필요승인에서 10을 뺀</b> 값을 담고 읽을 때 도로
    /// 더한다(<c>0x0044C780</c>). 선체 표(<c>0x004FC1E0</c>, 64바이트 x 8)의 <c>+0x34</c> 가
    /// 그 값이다.
    /// </remarks>
    public int MinCrew => _ships.Sum(s => s.Crew);

    /// <summary>
    /// 태울 수 있는 선원 수(정원). 필요승인의 <b>다섯 배</b>다.
    /// </summary>
    /// <remarks>
    /// 게임은 선체 표 <c>+0x34</c> 에 5 를 곱하고 50 을 더한다(<c>0x0044C790</c>).
    /// <c>+0x34</c> 가 필요승인 - 10 이므로 <c>(필요승인-10)*5 + 50 = 필요승인*5</c> 로 같다 —
    /// 카라벨 15명이면 75명, 갤리온 40명이면 200명이다.
    /// </remarks>
    public int MaxCrew => _ships.Sum(s => s.Crew) * 5;

    /// <summary>
    /// 선원을 그만큼 태운다(음수면 내린다). 0 과 정원 사이로 잘린다.
    /// </summary>
    /// <returns>실제로 늘거나 준 수.</returns>
    /// <remarks>게임의 <c>0x0040E3F0</c> 과 같다 — 그쪽도 0~정원으로 자른 뒤 싣는다.</remarks>
    public int AddCrew(int count)
    {
        int before = Crew;
        Crew = Math.Clamp(Crew + count, 0, MaxCrew);
        return Crew - before;
    }

    /// <summary>선원 수를 그대로 박는다. 세이브를 되돌릴 때 쓴다.</summary>
    public void SetCrew(int crew) => Crew = Math.Clamp(crew, 0, MaxCrew);

    // ── 편성(배마다 선원)과 대열 ─────────────────────────────────────────────

    private readonly List<int> _crewShares = [];

    /// <summary>
    /// 배마다 태운 선원 — <see cref="Ships"/> 차례대로다. 합은 늘 <see cref="Crew"/> 다.
    /// </summary>
    /// <remarks>
    /// 게임은 배 레코드 <c>+0x34</c> 마다 승원을 들고, 함대 선원은 그 합이다(<c>0x004745F0</c>).
    /// 우리는 모집·해고가 함대 총수를 움직이므로, 합이 어긋나거나 배 수가 바뀌면 바다 커맨드
    /// 「편성」의 <b>최적화</b>(<c>0x004744F0</c>)와 같은 규칙으로 다시 나눈다.
    /// </remarks>
    public IReadOnlyList<int> CrewShares
    {
        get
        {
            if (_crewShares.Count != _ships.Count || _crewShares.Sum() != Crew
                || _crewShares.Where((c, i) => c > MaxCrewOf(_ships[i])).Any())
            {
                var fresh = OptimizeCrew(_ships, Crew);
                _crewShares.Clear();
                _crewShares.AddRange(fresh);
            }
            return _crewShares;
        }
    }

    /// <summary>그 배의 필요 승원(<c>0x0044C780</c> — 레코드 <c>+0x30</c> + 10).</summary>
    public static int NeedCrewOf(Ship ship) => ship.Crew;

    /// <summary>그 배에 태울 수 있는 끝(<c>0x0044C790</c> — 선체표 <c>+0x34</c> x 5 + 50 = 필요 x 5).</summary>
    public static int MaxCrewOf(Ship ship) => ship.Crew * 5;

    /// <summary>
    /// 선원을 배마다 고르게 나눈다(<c>0x004744F0</c>) — 한 명씩, 최대에 안 찬 배 가운데
    /// <c>승원*100/필요승원</c> 이 가장 작은 배에 태운다.
    /// </summary>
    public static List<int> OptimizeCrew(IReadOnlyList<Ship> ships, int total)
    {
        var shares = new List<int>(new int[ships.Count]);
        for (int n = 0; n < total; n++)
        {
            int best = -1, bestRatio = int.MaxValue;
            for (int i = 0; i < ships.Count; i++)
            {
                if (shares[i] >= MaxCrewOf(ships[i])) continue;
                int ratio = shares[i] * 100 / Math.Max(1, NeedCrewOf(ships[i]));
                if (ratio < bestRatio) { bestRatio = ratio; best = i; }
            }
            if (best < 0) break;
            shares[best]++;
        }
        return shares;
    }

    /// <summary>
    /// 편성 창에서 결정한 몫을 적는다. 배 수·합·최대가 안 맞으면 받지 않는다.
    /// </summary>
    public bool SetCrewShares(IEnumerable<int>? shares)
    {
        if (shares == null) return false;
        var list = shares.ToList();
        if (list.Count != _ships.Count || list.Sum() != Crew || list.Any(c => c < 0)) return false;
        if (list.Where((c, i) => c > MaxCrewOf(_ships[i])).Any()) return false;
        _crewShares.Clear();
        _crewShares.AddRange(list);
        return true;
    }

    /// <summary>해전에 들어설 때의 대열(0~7). 게임의 함대 <c>+0xDC</c> 다 — 새 판은 0.</summary>
    public int Formation { get; private set; }

    /// <summary>대열을 고른다(<c>0x00475A50</c>).</summary>
    public void SetFormation(int formation) => Formation = Math.Clamp(formation, 0, 7);

    /// <summary>식량과 물이 며칠 갈지. 적은 쪽이 정한다.</summary>
    public int SupplyDaysLeft =>
        Supply.DaysLeft(SupplyOf(SupplyKind.Food), SupplyOf(SupplyKind.Water), Crew);

    /// <summary>지금 실은 통 수.</summary>
    public int LoadedBarrels => Supply.All.Sum(s => SupplyOf(s.Kind)) + CargoCount;

    /// <summary>
    /// 지금 실은 무게 — 보급품과 <b>대포</b>를 센다. 소지품 무게는 아직 안 센다.
    /// </summary>
    public int LoadedWeight =>
        Supply.All.Sum(s => SupplyOf(s.Kind) * s.UnitWeight) + GunWeight + CargoWeight;

    /// <summary>함대가 실은 대포의 무게.</summary>
    public int GunWeight => _ships.Sum(s => s.GunWeight);

    /// <summary>함대의 대포 문수.</summary>
    public int Guns => _ships.Sum(s => s.Guns);

    /// <summary>함대의 포탑 수.</summary>
    public int Turrets => _ships.Sum(s => s.Turrets);

    /// <summary>
    /// 바다에서 하루치 식량과 물을 축낸다.
    /// </summary>
    /// <returns>축내기 <b>앞</b>의 (물, 식량) 단위 수. 알림을 가리는 데 쓴다.</returns>
    public (int Water, int Food) UseDailySupply()
    {
        var before = (SupplyUnitsOf(SupplyKind.Water), SupplyUnitsOf(SupplyKind.Food));
        int use = Supply.DailyUse(Crew);
        AddSupplyUnits(SupplyKind.Water, -use);
        AddSupplyUnits(SupplyKind.Food, -use);
        return before;
    }

    /// <summary>배가 꽉 찼는지.</summary>
    public bool IsFleetFull => _ships.Count >= MaxShips;

    /// <summary>그 배를 살 돈이 있는지.</summary>
    public bool CanAfford(Hull hull) => Gold >= hull.Price;

    /// <summary>그 배를 지금 살 수 있는지 — 살 수 없으면 까닭을 낸다.</summary>
    public PurchaseResult CanBuy(Hull hull) =>
        IsFleetFull ? PurchaseResult.FleetFull
      : !CanAfford(hull) ? PurchaseResult.NotEnoughGold
      : PurchaseResult.Ok;

    /// <summary>배를 산다. 살 수 없으면 아무것도 하지 않고 까닭을 낸다.</summary>
    /// <param name="name">
    /// 새 배에 붙일 이름. 안 주면 <see cref="SuggestShipName"/> 이 골라 준다 —
    /// 조선소 창은 「선명입력」 에서 받은 것을 넘긴다.
    /// </param>
    /// <param name="price">치를 값. 안 주면 <see cref="Hull.Price"/> — 조선소는 시세를 먹인 값을 넘긴다.</param>
    public PurchaseResult Buy(Hull hull, string? name = null, int? price = null)
    {
        int cost = price ?? hull.Price;
        if (IsFleetFull) return PurchaseResult.FleetFull;
        if (!CanAfford(cost)) return PurchaseResult.NotEnoughGold;

        Gold -= cost;
        _ships.Add(new Ship(hull, name: string.IsNullOrWhiteSpace(name) ? SuggestShipName() : name.Trim()));
        Note(TraceShipIn, hull.GameId);
        return PurchaseResult.Ok;
    }

    /// <summary>
    /// 값 없이 배를 한 척 받는다 — 스폰서가 계약을 맺으며 대 주는 배다.
    /// </summary>
    /// <remarks>
    /// 게임은 세상 배 표(<c>0x005A4E18</c>)에 이미 있는 배를 골라 <c>+0x64</c> 에 1 을
    /// 박아 「대출」로 표시할 뿐 새 배를 짓지 않는다(<c>0x0040FA00</c>). 우리 쪽에는 그런
    /// 배 무리가 없어 새로 세운다.
    ///
    /// <b>함대에 곧장 들어가지 않는다.</b> 항구에 「대출 · 계류」로 대 놓일 뿐이라, 쓰려면
    /// 함대편성 → 선박 편입을 해야 한다. 그래서 맡긴 배와 같은 자리에 넣는다.
    /// 계약이 끝나면 <see cref="TakeBackLentShips"/> 가 거둬 간다.
    /// </remarks>
    /// <returns>그 마을이 더 못 맡으면 false.</returns>
    public bool Give(Hull hull, int cityId, string? name = null)
    {
        if (DockedAt(cityId).Count >= MaxDocked) return false;

        if (!_docked.TryGetValue(cityId, out var list)) _docked[cityId] = list = [];
        list.Add(new Ship(hull, stats: Ship.Stats.Of(hull) with { Lent = true },
                          name: string.IsNullOrWhiteSpace(name) ? SuggestShipName() : name.Trim()));
        return true;
    }

    /// <summary>그 기술의 지금 자리(0~<see cref="Skill.MaxLevel"/>).</summary>
    public int LevelOf(string skill) => _skills.GetValueOrDefault(skill);

    /// <summary>그 기술을 지금 배울 수 있는지 — 없으면 까닭을 낸다.</summary>
    public LearnResult CanLearn(string skill) =>
        LevelOf(skill) >= Skill.MaxLevel ? LearnResult.Mastered
      : Gold < Skill.Price ? LearnResult.NotEnoughGold
      : LearnResult.Ok;

    /// <summary>
    /// 조합에서 한 자리 배운다. 값을 치르고 자리를 올린 뒤, 그 자리에 걸리는 만큼 달이 간다
    /// (0→1 석 달 · →2 여섯 달 · →3 열두 달).
    /// </summary>
    public LearnResult Learn(string skill)
    {
        var can = CanLearn(skill);
        if (can != LearnResult.Ok) return can;

        int next = LevelOf(skill) + 1;
        Gold -= Skill.Price;
        _skills[skill] = next;
        Date = Date.AddMonths(Skill.MonthsFor(next));
        AgeCargo(Skill.MonthsFor(next) * DaysPerMonth);
        return LearnResult.Ok;
    }
}
