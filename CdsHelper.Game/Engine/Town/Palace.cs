using CdsHelper.Game.Local.Helpers;
using CdsHelper.Support.Local.Models;

namespace CdsHelper.Game.Engine.Town;

/// <summary>
/// 후원자와의 계약 규칙 — 보고할 것 고르기 · 사례 · 계약을 깰 때의 눈감아 주기.
/// </summary>
/// <remarks>
/// 후원자는 왕궁에만 앉는 것이 아니다 — 총독부·상관·학자 저택 어디든 앉고, 앉은 자리에
/// 설득·보고·계약중단 줄이 붙는다(<see cref="TownWorks"/>). 그래서 이름은 왕궁이지만
/// 자리가 아니라 <b>후원자와의 일</b>을 든다.
/// </remarks>
public static class Palace
{
    /// <summary>
    /// 그 후원자에게 보고할 발견물. 계약의 유적 번호를 가진 것 중 발견했고 아직 안 알린 것이다.
    /// </summary>
    /// <remarks>
    /// 게임의 <c>0x0044EA00</c> 이다 — 계약을 맺은 그 <b>자리</b>인지(<c>0x0044E550</c> 가 도시와
    /// 시설 종류를 본다) 보고, 계약의 유적 번호로 모은다(<c>0x00493E60</c>).
    ///
    /// <b>사람이 아니라 자리를 본다.</b> 항해하는 사이 후원자가 은퇴하고 뒷사람이 그 자리에
    /// 앉았어도 보고할 수 있고, 그때는 「선대의 계약」 인사가 붙는다(<c>0x00411620</c>).
    /// 사람까지 견주는 곳은 따로 있다(<c>0x0044E590</c> — 계약중단 줄이 그것을 쓴다).
    /// </remarks>
    /// <param name="atContractSeat">계약을 맺은 그 자리에 서 있는가.</param>
    public static List<DiscoveryTable.Record> ReportTargets(Player player, bool atContractSeat,
                                                            DiscoveryTable? table,
                                                            HintTable? hints)
    {
        if (player.Contract is not { } contract) return [];
        if (!atContractSeat) return [];
        if (table == null) return [];
        if (hints?.Find(contract.Hint) is not { } hint) return [];

        var rows = new List<DiscoveryTable.Record>();
        foreach (int id in player.Discoveries.Order())
        {
            if (player.HasAnnounced(id)) continue;
            // 감찰관을 매수해 숨긴 것은 목록에서 빠진다(0x0046B0A0 의 비트 0x20).
            if (player.IsHidden(id)) continue;
            if (table.Find(id) is not { } row || row.Hint != hint.Discovery) continue;
            rows.Add(row);
        }
        return rows;
    }

    /// <summary>
    /// <b>보고</b>가 올리는 명성 — 항구 <b>발표</b>와 셈이 다르다
    /// (<see cref="Harbor.FameFor"/> 는 보수/70).
    /// </summary>
    /// <remarks>
    /// 게임의 <c>0x004111D0</c> 이다. 발견물 하나마다 이 셈을 하고 <c>0x004697C0(0, 명성)</c>
    /// 으로 올린다.
    /// <code>
    ///   411477  ebx = (발견물 번호 == 7)          ; 세계일주항로만 따로 센다
    ///   41146c  이미 알려진 것이면 명성 칸을 통째로 건너뛴다   ★
    ///   411483  세계일주 · 기한 안 : max(10, 보수/50)
    ///   4114b4  세계일주 · 늦음    : max(10, 보수/60)
    ///   4114e6  그 밖  · 기한 안   : max(10, 보수/50)
    ///   411510  그 밖  · 늦음      : max(10, 보수/50) / 2
    /// </code>
    /// 명성은 <b>깎이지 않은 표의 보수</b>로 센다 — <see cref="CreditFor"/> 가 깎는 것과
    /// 다른 값이다(<c>0x00411495</c> 가 표를 다시 읽는다).
    /// </remarks>
    /// <param name="known">
    /// 이미 세상에 알려진 발견물인가(<c>0x004AADB0</c>). 남이 먼저 보고해 사람 칸 2 에
    /// 이름이 올라가 있으면 참이고, <b>그러면 명성이 한 톨도 안 오른다</b>. 한 번 남의
    /// 이름이 올라가면 <c>0x004AACA0</c> 첫 줄이 되돌리지 않으므로 영영 그렇다.
    /// </param>
    public static int FameFor(DiscoveryTable.Record row, bool inTime, bool known)
    {
        if (known) return 0;

        if (row.Id == WorldRoute)
            return Math.Max(FameFloor, row.Reward / (inTime ? FameDivisor : LateFameDivisor));

        int fame = Math.Max(FameFloor, row.Reward / FameDivisor);
        return inTime ? fame : fame / 2;
    }

    /// <summary>보고가 올리는 명성의 나눗수(<c>0x004114E6</c>).</summary>
    public const int FameDivisor = 50;

    /// <summary>세계일주항로를 늦게 보고했을 때만 쓰는 나눗수(<c>0x004114B4</c>).</summary>
    public const int LateFameDivisor = 60;

    /// <summary>아무리 하찮아도 이만큼은 오른다(<c>0x004114D0</c>).</summary>
    public const int FameFloor = Harbor.FameFloor;

    /// <summary>
    /// 세계일주항로의 발견물 번호. 이것만 셈이 따로다(<c>0x004111ED</c> 의 <c>sub eax, 7</c>).
    /// </summary>
    /// <remarks>
    /// 보수가 300000 닢으로 표에서 홀로 크다 — 늦어도 반토막이 나지 않고 나눗수만
    /// 50 에서 60 으로 바뀐다. 친밀도도 기한을 따지지 않고 오른다.
    /// </remarks>
    public const int WorldRoute = 7;

    /// <summary>
    /// 보고가 움직이는 <b>친밀도</b>(<c>0x00478530</c> 이 0~100 으로 자른다).
    /// </summary>
    /// <remarks>
    /// <code>
    ///   411220  세계일주        : max(1, 보수/10000) + 덤
    ///   4112a2  기한 안         : max(1, 보수/10000) + 덤
    ///   4112fc  늦음            : (max(2, 보수/10000) + 덤) / 2
    ///   411362  이미 알려진 것  : 기한 안이면 그대로, 늦었으면 -(rand(5)+5)
    /// </code>
    /// <b>덤</b>은 후원자가 그 갈래를 좋아할 때만 굴리는 <c>rand(10)</c> 이다
    /// (<c>0x004ADAE0</c> — 후원자 표 <c>+0x38</c> 의 비트, <see cref="Persuasion.Likes"/>).
    /// </remarks>
    public static int ClosenessFor(DiscoveryTable.Record row, bool inTime, bool known,
                                   bool likes, Random random)
    {
        if (known && row.Id != WorldRoute)
            return inTime ? 0 : -(random.Next(ClosenessDrop) + ClosenessDrop);

        int bonus = likes ? random.Next(ClosenessBonus) : 0;
        if (row.Id == WorldRoute || inTime)
            return Math.Max(1, row.Reward / ClosenessPerReward) + bonus;

        return (Math.Max(2, row.Reward / ClosenessPerReward) + bonus) / 2;
    }

    /// <summary>친밀도 한 칸을 만드는 보수(<c>0x0041125E</c> 의 <c>0x2710</c>).</summary>
    public const int ClosenessPerReward = 10000;

    /// <summary>좋아하는 갈래일 때 굴리는 덤의 폭(<c>0x0041124D</c>).</summary>
    public const int ClosenessBonus = 10;

    /// <summary>늦게 온 데다 알려진 것이면 깎이는 폭 — <c>-(rand(5)+5)</c>(<c>0x00411366</c>).</summary>
    public const int ClosenessDrop = 5;

    /// <summary>
    /// 보고한 발견물이 <b>후원자에게 쌓는 값</b>(<c>0x004113B4</c>, 후원자 <c>+0x24</c>).
    /// </summary>
    /// <remarks>
    /// 표의 보수를 그대로 쌓되 <b>이미 알려진 것이면 깎는다</b> — 기한 안이면 1/4,
    /// 늦었으면 1/5 다(<c>0x0041139B</c>). 상한은 후원자 표 <c>+0x2C</c> 의 만 배다.
    ///
    /// 이 값은 후원자의 <b>지갑</b>이다(<see cref="Support.Local.Models.Player.Purses"/>) —
    /// 새 판에 <b>재력</b>만큼 차고(<c>0x004AD88F</c> — 원본은 등급 x 10000 이고 우리
    /// <c>patrons.json</c> 의 <c>wealth</c> 가 이미 그 값이다), 계약을 맺으면 계약금의
    /// 절반이 빠지고(<c>0x004ADF4A</c>), 보고하면 여기로 도로 쌓인다. 상한도 재력이다.
    /// 내 소지금과는 다른 자리다 — 내가 받는 돈은 <see cref="RewardFor"/> 뿐이다.
    /// </remarks>
    public static int CreditFor(int reward, bool inTime, bool known) =>
        known ? reward / (inTime ? 4 : 5) : reward;

    /// <summary>
    /// 보고할 때 후원자가 <b>돌려주는</b> 아이템인가 — 분류 7(서적·유물)만 그렇다.
    /// </summary>
    /// <remarks>
    /// <c>0x004113F5</c> 가 아이템 표(<c>0x004FD558</c>) <c>+0x14</c> 를 7 과 견주고,
    /// 맞으면 "이것은 자네가 가지고 가게" 하고 <c>0x004B1710</c> 으로 소지품에 넣는다
    /// — 문구도 <b>"…을 손에 넣었다!"</b> 다. <b>빼앗기는 것이 아니다.</b>
    /// </remarks>
    public const int KeepsakeCategory = 7;

    /// <summary>후원자가 성과를 어떻게 보았는가(<c>0x00411AA0</c>) — 사례와 대사가 여기서 갈린다.</summary>
    public enum ReportGrade
    {
        /// <summary>「굉장하다! 잘 해냈네!!」 — 듬뿍 준다.</summary>
        Good,

        /// <summary>그저 그렇다 — 기한 안이면 <b>말없이</b> 미불 그대로, 늦었으면 반만.</summary>
        Mid,

        /// <summary>「이건가...」 — 깎아서 주거나 늦었으면 한 푼도 없다.</summary>
        Poor,
    }

    /// <summary>
    /// 후원자가 성과를 보는 눈(<c>0x00411AA0</c>) — 안목 굴림 두 번으로 셋을 가른다.
    /// </summary>
    /// <remarks>
    /// <code>
    ///   411ab9  hit = 후원자표.+0x30(친밀도 밑값) >= rand(100)
    ///   411ad4  hit  이면  surpass ? 굉장 : 보통
    ///   411b1e  아니면  T = 40*성미[7] + (surpass ? 20 : 0)      ; 0x00411CC0
    ///   411b2e          T <= rand(100) ? 굉장 : 시시
    /// </code>
    /// <b>surpass</b> 는 보고한 발견물들의 보수를 다 더한 값이 <b>힌트 표의 자금</b>
    /// (<see cref="HintTable.Hint.Funds"/>, <c>0x004D8E80+0x14</c>)보다 큰가다
    /// (<c>0x00412289</c>) — 흥정으로 고친 계약금이 아니라 밑값과 견준다.
    /// 성미[7] 은 후원자 성미 여덟 칸의 마지막(0~2)이라 <c>T</c> 는 0·20·40·60·80·100 여섯 가지다.
    ///
    /// 굴림은 화면이 제 씨앗으로 굴리는 것이라(<c>0x004A2780</c>) 같은 보고를 다시 하면
    /// 같은 갈래가 나오는데, 우리는 그냥 화면의 난수를 쓴다.
    /// </remarks>
    public static ReportGrade GradeOf(int closeness, int fortune7, bool surpass, Random random)
    {
        if (closeness >= random.Next(100))
            return surpass ? ReportGrade.Good : ReportGrade.Mid;

        int need = GradeStep * Math.Clamp(fortune7, 0, 2) + (surpass ? GradeSurpass : 0);
        return need <= random.Next(100) ? ReportGrade.Good : ReportGrade.Poor;
    }

    /// <summary>성미 한 칸이 올리는 문턱(<c>0x00411CD7</c> 의 <c>imul 0x28</c>).</summary>
    public const int GradeStep = 40;

    /// <summary>자금보다 많이 물어 왔을 때 더 붙는 문턱(<c>0x00411CE6</c>).</summary>
    public const int GradeSurpass = 20;

    /// <summary>
    /// 보고 사례. 미불(계약금의 반)에 갈래마다의 비율을 먹이고 100닢 단위로 내린다.
    /// </summary>
    /// <remarks>
    /// 게임의 <c>0x00411D10</c> · <c>0x004117D0</c> 이다. <b>기한을 먼저 보고 갈래를 나중에 본다</b> —
    /// 비율이 걸리는 곳은 기한 안일 때뿐이다.
    /// <code>
    ///   굉장 · 기한 안   미불 x (120 + rand(30)) / 100
    ///   굉장 · 늦음      미불                                ; 「늦은 것은 없었던 일로 하지」
    ///   보통 · 기한 안   미불                                ; 대사가 아예 없다
    ///   보통 · 늦음      미불 / 2                            ; 「늦은 것은 공제하겠네」
    ///   시시 · 기한 안   미불 x (90 - rand(20)) / 100
    ///   시시 · 늦음      0                                   ; 한 푼도 없다
    /// </code>
    /// 100 을 넘으면 100닢 단위로 내린다(<see cref="To100"/>).
    /// </remarks>
    /// <summary>
    /// <b>세계일주</b>를 보고했을 때의 사례(<c>0x00411D90</c>) — 안목 굴림도 시시한 갈래도 없다.
    /// </summary>
    /// <remarks>
    /// <code>
    ///   기한 안   미불 x (120 + rand(30)) / 100    「굉장하다! 잘 해주었다!!」
    ///   늦음      미불 x (100 + rand(20)) / 100    「훌륭하다! 잘 해내었다!! 늦은 것은 공제하겠다.」
    /// </code>
    /// 늦어도 깎이지 않는다 — 명성 셈(<see cref="FameFor"/>)이 세계일주만 반토막을 면하는 것과 같다.
    /// </remarks>
    public static int WorldRouteRewardFor(int unpaid, bool inTime, Random random) =>
        To100((int)((long)unpaid * (inTime ? 120 + random.Next(30) : 100 + random.Next(20)) / 100));

    public static int RewardFor(int unpaid, ReportGrade grade, bool inTime, Random random) => grade switch
    {
        ReportGrade.Good => To100(inTime ? (int)((long)unpaid * (120 + random.Next(30)) / 100) : unpaid),
        ReportGrade.Mid => To100(inTime ? unpaid : unpaid / 2),
        _ => inTime ? To100((int)((long)unpaid * (90 - random.Next(20)) / 100)) : 0,
    };

    /// <summary>
    /// 계약이 끝나 <b>빌린 배를 거둬 갈 때</b>(<c>0x0040FE40</c>) 낼 말.
    /// </summary>
    /// <remarks>
    /// <code>
    ///   0040fe80  계약을 파기했고 짐이 있으면  0x0055C158 「제독, 짐을 전부 가지고 간 것 같습니다!」
    ///                                         0x0055C180 「짐을 스폰서가 몰수해 버렸습니다!」
    ///   0040fed9  배가 한 척이라도 남으면      0x0055C1A8 · 0x0055C1D0
    ///   0040ff07  안 남고 짐이 있으면          0x0055C1F0 · 0x0055C230
    ///                                         0x0055C268 「금화 %ld닢을 손에 넣었다!」
    ///   0040ff3d  안 남고 짐도 없으면          0x0055C288 · 0x0055C2B0
    /// </code>
    /// 넷이 다 <b>부관 있음·없음 두 벌</b>이다(<c>0x00469680</c>) — 부관이 없으면
    /// 「돌려 주었습니다」가 「반환했습니다」로 바뀐다.
    ///
    /// 짐 값은 그 도시 <b>매각가의 절반</b>이다(<c>0x0044D95C</c> 의 <c>sar 1</c>) — 급히 넘기는 값이다.
    /// </remarks>
    public const string ShipsReturned = "스폰서에게 배를 돌려 주었습니다.";
    public const string ShipsReturnedAlone = "스폰서에게 배를 반환했습니다.";
    public const string ShipsReturnedCargoSold = "스폰서에게 배를 돌려 주었습니다. 남은 짐은 전부 팔았습니다.";
    public const string ShipsReturnedCargoSoldAlone = "스폰서에게 배를 반환했습니다. 짐은 전부 매각하겠습니다.";

    /// <summary>
    /// 빌린 배가 있는 채로 <b>계약을 파기하면</b> 스폰서가 짐까지 가져간다(<c>0x0040FE5C</c>).
    /// </summary>
    /// <remarks>
    /// 계약이 끝난 갈래 값(<c>+0xBC</c>)이 <b>1(파기)</b> 일 때만이다 — 보고로 끝났으면(0)
    /// 짐은 그대로다. 감찰관을 처벌해 갈아탄 것(2)도 몰수가 아니다.
    /// 배가 한 척도 안 빌렸으면 이 갈래 자체가 없다.
    /// </remarks>
    public const string CargoSeized = "제독, 짐을 전부 가지고 간 것 같습니다!";
    public const string CargoSeizedAlone = "짐을 스폰서가 몰수해 버렸습니다!";

    /// <summary>
    /// 지갑이 이만큼도 안 남으면 아예 물린다(<c>0x004AF183</c> 의 <c>cmp 0x14</c>).
    /// </summary>
    /// <remarks>
    /// 그 위라면 후원자는 <b>있는 만큼으로 깎아</b> 내준다(<c>0x004AF18C</c>) — 물리지 않는다.
    /// </remarks>
    public const int PurseFloor = 20;

    /// <summary>
    /// 감찰관에게 줄 뇌물(<c>0x0041CBA0</c>).
    /// </summary>
    /// <remarks>
    /// <code>
    ///   합계 = 숨길 발견물들의 <b>증거품 되팔값</b>(아이템 표 +0x0C) 을 다 더한 값
    ///   값   = (200 − 후원자표 +0x30) x 합계 / 50        ; 0x0041CC12
    ///   1 닢 밑으로는 안 내려간다                          ; 0x0041CC1F
    /// </code>
    /// 견주는 <c>+0x30</c> 은 <b>그 자리에 앉은 후원자</b>의 값이다
    /// (<see cref="SponsorTable.Sponsor.Closeness"/>) — 클수록 싸게 먹힌다(2.2~3.0배).
    /// 명성·악명·성미·소지금은 값에 안 든다.
    /// </remarks>
    public static int BribePrice(int closeness, int evidenceValue) =>
        Math.Max(1, (200 - closeness) * evidenceValue / 50);

    /// <summary>돈이 모자란다고 이만큼 물리면 쫓겨난다(<c>0x0041C696</c>).</summary>
    public const int BribeTries = 3;

    /// <summary>
    /// <b>불가침 조약</b>(토르데시야스) 경고가 뜨는지(<c>0x004698C0</c>).
    /// </summary>
    /// <remarks>
    /// <code>
    ///   004698c0  해 &lt; 1494 면 안 뜬다(cmp 0x5D6)
    ///   004698de  나라 0(포르투갈)의 후원자에게 에스파니아 제독이 갔을 때
    ///   004698ea  나라 1(에스파니아)의 후원자에게 포르투갈 제독이 갔을 때
    /// </code>
    /// 막지는 않는다 — <b>알려만 주고</b> 그대로 설득으로 넘어간다.
    /// </remarks>
    public static bool TreatyWarning(int playerNation, int patronNation, int year) =>
        year >= TreatyYear && playerNation is 0 or 1 && patronNation is 0 or 1
        && playerNation != patronNation;

    /// <summary>불가침 조약이 맺어진 해(<c>0x004698C0</c> 의 <c>cmp 0x5D6</c>).</summary>
    public const int TreatyYear = 1494;

    // ── 문간의 명성 관문과 집사 뇌물(0x004AE260) ───────────────────────────

    /// <summary>문간이 얹어 주는 명성(<c>0x004AE26F</c> 의 <c>add 0x5DC</c>).</summary>
    public const int DoorBonus = 1500;

    /// <summary>뇌물로 더 얹을 수 있는 명성(<c>0x004AE2D3</c> 의 <c>add 0x1F4</c>).</summary>
    public const int BribeBonus = 500;

    /// <summary>그냥 들여보내 주는지(<c>0x004AE290</c>) — 안목 x 100 이 명성 + 1500 이하라야 한다.</summary>
    public static bool Admitted(int eye, int fame) => eye * 100 <= fame + DoorBonus;

    /// <summary>
    /// 집사를 매수해서라도 들어갈 수 있는지(<c>0x004AE2E1</c>).
    /// </summary>
    /// <remarks>명성이 오백이 더 있어야 그 자리가 열린다 — 아니면 「매수한다」 줄도 안 뜬다.</remarks>
    public static bool BribeAdmits(int eye, int fame) =>
        eye * 100 <= fame + DoorBonus + BribeBonus;

    /// <summary>집사에게 줄 돈(<c>0x004AE35B</c>) — 후원자 표 <c>+0x20</c> 그대로다.</summary>
    public static int StewardFee(int eye) => eye;

    /// <summary>
    /// 집사가 말을 들어 주는지(<c>0x004AE36C</c>) — <c>33 x 웅변 + 매력 + 1 &gt; rand(200)</c>.
    /// </summary>
    public static bool StewardTalks(int rhetoric, int charm, Random random) =>
        rhetoric * 33 + charm + 1 > random.Next(200);

    /// <summary>급히 넘기는 짐 값 — 매각가의 절반(<c>0x0044D95C</c>).</summary>
    public static int DistressPrice(int sellPrice) => sellPrice / 2;

    /// <summary>100닢 단위로 내린다(<c>0x004117D0</c>). 100 이하면 그대로 둔다.</summary>
    public static int To100(int coins) => coins > 100 ? coins / 100 * 100 : coins;

    /// <summary>
    /// <b>남이 먼저 발표해 버렸을 때</b>의 사례(<c>0x004117F0</c>).
    /// </summary>
    /// <remarks>
    /// 위약금을 무는 것이 아니라 <b>받을 사례가 깎이는 것</b>이다.
    /// <code>
    ///   411805  eax = [0x005B619C]      ; 계약금
    ///   41180a  eax /= 4                ; 사분의 일
    ///   411814  0x004117D0(eax)         ; 100닢 단위 내림
    ///   4119e8  기한까지 넘겼으면 이 셈에 들지도 못하고 한 푼도 없다
    /// </code>
    /// 말도 따로 있다 — 기한 안이면 <b>"안됐지만, %ld닢 밖에 지불할 수 없습니다."</b>
    /// (<c>0x00530100</c>), 넘겼으면 <b>"계약기한이 지나 버렸으니, 사례는 지불할 수
    /// 없습니다."</b>(<c>0x00530238</c>)다.
    ///
    /// 세 갈래를 가르는 곳은 <c>0x00411FC0</c> 이고, 어느 쪽이든 <c>0x0041200E</c> 가
    /// <b>더하기만</b> 한다(<c>if (eax &gt; 0)</c>) — 소지금이 줄어드는 길은 여기에 없다.
    /// 위약금은 딴 자리다: 계약중단이 계약금/2(<c>0x0044F827</c>), 감찰관 사고가
    /// 후원자 표 <c>+0x20</c> x (n+1) x 1000(<c>0x0044FA76</c>)이다.
    /// </remarks>
    /// <param name="amount">계약금(<see cref="Support.Local.Models.Contract.Unpaid"/> 의 두 배).</param>
    public static int ScoopedRewardFor(int amount, bool inTime) =>
        inTime ? To100(amount / 4) : 0;

    /// <summary>
    /// <b>모조품</b>(<see cref="DiscoveryTable.Record.IsCounterfeit"/>)을 보고했을 때 후원자가
    /// 그 자리에서 알아보는지(<c>0x00412460</c>, <c>0x00412020</c> 안쪽).
    /// </summary>
    /// <remarks>
    /// <code>
    ///   412464  0x004AD830(후원자)                  ; 후원자 표 그 줄
    ///   412469  eax = 줄.+0x30                       ; 정적 친밀도 밑값(SponsorTable.Closeness)
    ///   412472  eax = eax*2 - [0x5B60D0]              ; [0x5B60D0] = 능력치[4] = 운
    ///   412479  eax = max(10, eax - 1)
    ///   412483  들킴 = (eax &gt; roll(100))
    /// </code>
    /// 정적 친밀도 밑값(놀이 내내 안 바뀌는 표 값)이 높을수록, 내 운이 낮을수록 잘 들킨다.
    /// <b>안 들키면 진짜를 가져온 것처럼 그대로 통과된다</b> — 사례·명성·친밀도가 다 오른다.
    /// </remarks>
    public static bool CounterfeitCaught(int sponsorTableCloseness, int luck, Random random) =>
        random.Next(100) < Math.Max(CounterfeitCatchFloor, sponsorTableCloseness * 2 - luck - 1);

    /// <summary>
    /// 계약을 그르친 죄를 <b>얼마나 무겁게 보는가</b>(<c>0x0044F170</c>).
    /// </summary>
    /// <remarks>
    /// <code>
    ///   문턱 = −(악명/100) − 계약금/100 + 명성/100 + 신앙심 + 1      ; 0 밑은 0 으로 자른다
    ///   문턱 == 0        → 그냥 봐 준다
    ///   문턱 &gt;= 친밀도  → 감옥
    ///   그 밖            → 위약금
    /// </code>
    /// <b>부호가 뒤집혀 보인다.</b> 명성과 신앙심이 높을수록 문턱이 커져 감옥에 가기 쉽고,
    /// 악명이 높거나 계약금이 클수록 문턱이 작아져 용서받기 쉽다 — <c>idiv</c> 의 제수가
    /// <c>-100</c> 이고 계약금은 <c>sub</c> 다. 「믿었던 만큼 실망도 크다」는 결로 읽힌다.
    /// </remarks>
    public static int Reckoning(int fame, int infamy, int funds, int faith) =>
        Math.Max(0, -(infamy / 100) - funds / 100 + fame / 100 + faith + 1);

    /// <summary>
    /// 그때 무는 위약금(<c>0x0044F1B7</c>) — <b>계약금의 4분의 1</b>을 내림한 것이다.
    /// </summary>
    /// <remarks>100 을 넘으면 100 닢 단위로, 10 을 넘으면 10 닢 단위로 자른다.</remarks>
    public static int FineFor(int funds)
    {
        int fine = funds / 4;
        if (fine > 100) return fine / 100 * 100;
        return fine > 10 ? fine / 10 * 10 : fine;
    }

    /// <summary>
    /// 후원자 성미 여덟 칸 가운데 <b>관용</b>에 쓰는 칸(<c>0x00412490</c> · <c>0x0044F145</c>).
    /// </summary>
    /// <remarks>
    /// 모조품을 봐 줄지(<c>0x00412490</c>)는 이 칸이 <b>0 보다 커야</b> 굴리고, 계약 실패를
    /// 봐 줄지(<c>0x0044F155</c>)는 <b>2 라야</b> 굴린다.
    /// </remarks>
    public const int MercyFortune = 4;

    /// <summary>모조품 판정 문턱의 바닥값(<c>cmp eax,0xa; mov eax,0xa</c>).</summary>
    public const int CounterfeitCatchFloor = 10;

    /// <summary>
    /// 들킨 모조품을 후원자가 <b>한 번 더 봐 주는지</b>(<c>0x004124C0</c>).
    /// </summary>
    /// <remarks>
    /// <code>
    ///   4124c4  ecx = 후원자.+0x20                    ; 동적 친밀도(놀이 중에 쌓인 것, 0~100)
    ///   4124cb  ecx += [0x5B60D0]                      ; + 운
    ///   4124d1  ecx = min(97, ecx + 1)
    ///   4124dc  봐줌 = (ecx &gt; roll(100))
    /// </code>
    /// 봐주면 "이것은 모조품이네, 기회를 한번 더 주겠다" 하고 물건은 안 주지만 크게
    /// 나무라지도 않는다(<see cref="CounterfeitInfamyRoll"/> 만큼 악명이 오른다). 못 봐주면
    /// "이런 모조품으로 나를 속이려 했나!" 하고 사이가 상한다(<see cref="Support.Local.Models.Player.Sulk"/>).
    /// </remarks>
    public static bool CounterfeitForgiven(int playerCloseness, int luck, Random random) =>
        random.Next(100) < Math.Min(ForgiveCap, playerCloseness + luck + 1);

    /// <summary>
    /// 봐준 모조품이 올리는 악명 — <c>rand(10)+1</c>(<c>0x00412427</c>).
    /// </summary>
    public static int CounterfeitInfamyRoll(Random random) => random.Next(10) + 1;

    /// <summary>기한 안에 깰 때 굴리는 주사위 폭(<c>add $0x64,%eax</c>).</summary>
    public const int OnTimeRoll = 100;

    /// <summary>기한을 넘겨 깰 때의 폭 — 반쯤 넓어져 통과하기 어렵다(<c>and $0x32</c>).</summary>
    public const int LateRoll = 150;

    /// <summary>문턱을 자르는 값(<c>cmp $0x61,%ecx</c>).</summary>
    public const int ForgiveCap = 97;

    /// <summary>
    /// 계약을 깨는 것을 후원자가 눈감아 주는지(<c>0x0044F8B0</c>).
    /// </summary>
    /// <remarks>
    /// <code>
    ///   0044f8ba  주사위 = rand(기한을 넘겼으면 150, 아니면 100)
    ///   0044f8ca  문턱  = min(97, [후원자+0x20] + [0x005B60D0] + 1)
    ///   0044f8de  용서받는다 = 주사위 &lt; 문턱
    /// </code>
    /// <c>[후원자+0x20]</c> 은 <b>친밀도</b>(<see cref="Support.Local.Models.Player.Closeness"/>)이고
    /// <c>[0x005B60D0]</c> 은 능력치 넷째인 <b>운</b>이다 — 모조품을 봐주는 판정
    /// (<see cref="CounterfeitForgiven"/>)과 <b>똑같은 꼴</b>이다.
    /// </remarks>
    public static bool Forgiven(int closeness, int luck, bool overdue, Random random) =>
        random.Next(overdue ? LateRoll : OnTimeRoll)
            < Math.Min(ForgiveCap, closeness + luck + 1);
}
