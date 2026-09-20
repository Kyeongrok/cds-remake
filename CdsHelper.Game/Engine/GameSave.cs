using System.IO;
using System.Text.Json;
using CdsHelper.Support.Local.Models;

namespace CdsHelper.Game.Engine;

/// <summary>
/// 함대 창 놀이의 세이브. 소지금·날짜·있는 도시·배운 기술을 적어 둔다.
/// </summary>
/// <remarks>
/// 파일 이름은 게임과 같은 <c>SAVEDATA.CDS</c> 지만 <b>게임 폴더에는 쓰지 않는다</b> —
/// 거기 있는 것은 진짜 게임 세이브라 덮어쓰면 그 판이 날아간다. 그래서 설정 파일과 같은
/// 자리(<c>%APPDATA%\CdsHelper</c>)에 둔다. 속은 게임 형식이 아니라 우리 것(JSON)이다.
/// </remarks>
public static class GameSave
{
    // 한글이 \uXXXX 로 깨져 보이지 않게 그대로 적는다(사람이 열어 볼 파일이다).
    private static readonly JsonSerializerOptions Pretty = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>
    /// 이 판부터 <c>Supplies</c> 의 식량·물이 <b>통이 아니라 단위</b>로 적힌다(한 통이 열).
    /// </summary>
    public const int SupplyUnitsFrom = 16;

    /// <summary>이 판부터 <c>ShipStats</c> 에 포탑수·대포가 함께 적힌다.</summary>
    public const int GunsInStatsFrom = 18;

    /// <summary>이 판부터 <b>주인공 이름</b>도 적는다 — 모험 중단 창이 이름을 부른다.</summary>
    public const int NameFrom = 24;

    /// <summary>
    /// 이 판부터 <b>얼굴과 운명 코드</b>도 적는다 — 초상화와 여급 궁합이 여기서 온다.
    /// </summary>
    /// <remarks>
    /// 그 앞 세이브에는 둘 다 없다. 얼굴은 0 으로 두고, 운명 코드는 얼굴 번호로 물러선다 —
    /// 새 놀이가 앞의 열여섯만 고르게 해서 그때까지는 둘이 같은 값이었다.
    /// </remarks>
    public const int FaceFrom = 25;

    /// <summary>나라 적대도와 열린 문이 적히기 시작한 판.</summary>
    public const int StandingFrom = 26;

    /// <summary>이 판부터 함대에 남은 해상재해(쥐·괴혈병·전염병)도 적는다.</summary>
    public const int AilmentsFrom = 27;

    /// <summary>이 판부터 대열(함대 <c>+0xDC</c>)과 배마다 승원(편성)도 적는다.</summary>
    public const int FormationFrom = 28;

    /// <summary>이 판부터 모항(새 판을 연 도시)도 적는다 — 항구 발표가 모항에서만 된다.</summary>
    public const int HomePortFrom = 29;

    /// <summary>
    /// 이 판부터 발견물 아이템은 <b>발표할 때</b> 소지품에 든다(<see cref="GameInfo.VirtualItems"/>).
    /// 앞 판은 발견할 때 넣어 두었으므로, 아직 안 알린 발견물의 아이템을 한 벌씩 걷어 낸다.
    /// </summary>
    public const int VirtualItemsFrom = 30;

    /// <summary>이 판부터 <c>ShipStats</c> 에 마스트의 돛도 함께 적힌다.</summary>
    public const int SailsInStatsFrom = 19;

    /// <summary>
    /// 적어 둔 것을 지운다 — 새 놀이에서 <b>삭제한다</b> 를 고를 때다.
    /// </summary>
    /// <remarks>
    /// 게임은 <c>0x0045F8F2</c> 에서 <c>C:SAVEDATA.CDS</c> · <c>C:SAVEDATA.TMP</c> ·
    /// <c>C:ACCDATA.CDS</c> 셋을 지운다. 우리 것은 한 파일뿐이고, <b>게임 폴더의
    /// SAVEDATA.CDS 는 건드리지 않는다</b> — 그쪽은 우리 것이 아니라 읽기만 한다.
    /// </remarks>
    public static bool Delete()
    {
        try
        {
            if (File.Exists(Path)) File.Delete(Path);
            return true;
        }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
    }

    /// <summary>세이브 파일 자리.</summary>
    public static string Path => System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "CdsHelper", "SAVEDATA.CDS");

    /// <summary>
    /// <b>자동저장</b> 파일 자리 — 손으로 적은 것과 <b>따로</b> 둔다.
    /// </summary>
    /// <remarks>
    /// 원본에 없는 것이다. 입항할 때마다 덮어쓰므로 손으로 적어 둔 <see cref="Path"/> 를
    /// 건드리지 않게 이름을 달리한다. 첫 화면의 <b>CONTINUE</b> 가 이 파일을 연다.
    /// </remarks>
    public static string AutoPath => System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "CdsHelper", "AUTOSAVE.CDS");

    /// <summary>적어 두는 것.</summary>
    /// <param name="Version">형식 판. 나중에 늘릴 때 본다.</param>
    /// <param name="SavedAt">적은 때(현실 시각).</param>
    /// <param name="Mates">술집에서 부하로 삼은 사람. 판 2 부터 있어 옛 세이브에서는 null 이다.</param>
    /// <param name="Met">낯을 튼 사람. 이 사람들만 술집에서 이름이 보인다.</param>
    /// <param name="Items">소지품(아이템 번호). 판 3 부터 있어 그 전 세이브에서는 null 이다.</param>
    /// <param name="Supplies">
    /// 실어 둔 보급품(식량·물·자재·탄약). 판 4 부터 있어 그 전 세이브에서는 null 이다.
    /// <b>판 16 부터 식량·물은 통이 아니라 단위</b>다 — 한 통이 열이다.
    /// </param>
    /// <param name="Discoveries">
    /// 발견한 발견물 번호(게임 표의 줄 번호). 판 5 부터 있어 그 전 세이브에서는 null 이다.
    /// </param>
    /// <param name="Contract">
    /// 맺고 있는 계약. 판 6 부터 있어 그 전 세이브에서는 null 이다(= 계약 없음).
    /// </param>
    /// <param name="Crew">
    /// 태우고 있는 선원 수. 판 7 부터 있어 그 전 세이브에서는 null 이다 — 그때는 최저 승원
    /// 수로 채운다(그 전까지 쓰던 값이 그것이다).
    /// </param>
    /// <param name="Announced">
    /// 발표한 발견물 번호. 판 8 부터 있어 그 전 세이브에서는 null 이다(= 아직 아무것도 안 알림).
    /// </param>
    /// <param name="Fame">명성. 판 8 부터 있다 — 발표로 오르기 시작해서 적어 둬야 한다.</param>
    /// <param name="Stored">자택에 맡겨 둔 것(아이템 번호). 판 9 부터 있다.</param>
    /// <param name="Savings">자택에 맡겨 둔 돈(닢). 판 10 부터 있다.</param>
    /// <param name="Ships">가진 배(선체 이름). 판 11 부터 있다 — 그 전에는 아예 안 적었다.</param>
    /// <param name="Flagship">기함이 <paramref name="Ships"/> 에서 몇째인지.</param>
    /// <param name="Docked">마을에 맡겨 둔 배 — 도시 번호마다 선체 이름들.</param>
    /// <param name="ShipHp">배마다의 지금 내구. 판 12 부터 있다 — 없으면 성한 채로 연다.</param>
    /// <param name="DockedHp">맡겨 둔 배의 지금 내구.</param>
    /// <param name="Fatigue">
    /// 선원들이 지친 만큼(0~100). 판 13 부터 있다 — 폭풍이 올리고 자택 휴양이 푼다.
    /// </param>
    /// <param name="DaysAtSea">바다에서 지낸 날수. 판 13 부터 있다.</param>
    /// <param name="ShipStats">
    /// 개조로 갈린 배마다의 값(내구·추진력·용량·중량·승원). 판 14 부터 있다 — 없으면
    /// 선체 기본값으로 연다.
    /// </param>
    /// <param name="DockedStats">맡겨 둔 배의 개조 값.</param>
    /// <param name="Morale">선원들의 사기(0~100). 판 15 부터 있다 — 없으면 꽉 찬 채로 연다.</param>
    /// <param name="ShipNames">
    /// 배마다의 이름. 판 17 부터 있다 — 없으면 선체 이름을 쓴다.
    /// </param>
    /// <param name="DockedNames">맡겨 둔 배의 이름.</param>
    /// <param name="MateBook">
    /// 부하로 삼은 사람의 됨됨이(얼굴·능력치·명성·연령). 판 20 부터 있다 — 그 전
    /// 세이브에서는 null 이라 게임 세이브를 뒤져 채운다.
    /// </param>
    /// <param name="Closeness">
    /// 후원자마다의 친밀도(0~100). 판 26 부터 있다 — 없으면 다들 0 에서 시작한다.
    /// </param>
    /// <remarks>
    /// 판 18 부터 <c>ShipStats</c> 에 포탑수·대포갈래·대포수가 함께 적힌다. 그 앞 세이브는
    /// 그 칸이 비어(0·0·0) 들어오므로 <b>선체 기본값으로 되살린다</b> —
    /// <see cref="Support.Local.Models.Player.RestoreFleet"/> 의 <c>gunsInStats</c>.
    /// </remarks>
    public sealed record Data(
        int Version, DateTime SavedAt, int Gold, DateTime Date,
        int CityId, string CityName, Dictionary<string, int> Skills, List<int> Hints,
        List<string>? Mates = null, List<string>? Met = null,
        List<int>? Items = null, List<int>? Supplies = null,
        List<int>? Discoveries = null, Deal? Contract = null, int? Crew = null,
        List<int>? Announced = null, int? Fame = null, List<int>? Stored = null,
        int? Savings = null, List<string>? Ships = null, int Flagship = 0,
        Dictionary<int, List<string>>? Docked = null,
        List<int>? ShipHp = null, Dictionary<int, List<int>>? DockedHp = null,
        int? Fatigue = null, int? DaysAtSea = null,
        List<Ship.Stats>? ShipStats = null,
        Dictionary<int, List<Ship.Stats>>? DockedStats = null, int? Morale = null,
        List<string>? ShipNames = null, Dictionary<int, List<string>>? DockedNames = null,
        List<Support.Local.Models.Player.MateInfo>? MateBook = null,
        string? Explored = null, string? Spouse = null, List<string>? Heirs = null,
        int? SpouseId = null, Dictionary<int, int>? Liking = null,
        string? Name = null, string? Family = null, string? Given = null,
        Dictionary<string, int>? Tongues = null,
        int? Face = null, int? Fortune = null,
        Dictionary<int, int>? Hostility = null, List<int>? OpenedGates = null,
        List<int>? TalksLost = null, Dictionary<string, int>? Closeness = null,
        List<int>? KnownCities = null, int? Ailments = null,
        int? Formation = null, List<int>? CrewShares = null, int? HomePort = null,
        double? SeaX = null, double? SeaY = null, int? Condition = null,
        List<int>? Abilities = null, int? JobIndex = null, int? Age = null,
        int? BirthMonth = null, int? BirthDay = null, int? Blood = null, int? Nation = null,
        List<Support.Local.Models.Player.Cargo>? Cargo = null,
        Dictionary<int, List<int>>? TradeStock = null,
        List<int>? OpenedHints = null,
        List<int>? ActiveGoods = null,
        List<Support.Local.Models.Player.Betrayal>? Betrayals = null,
        Dictionary<string, DateTime>? Sulks = null,
        List<Support.Local.Models.Player.Child>? Children = null,
        string? ActiveStoryBook = null, DateTime? StoryQuestDeadline = null,
        Dictionary<string, int>? StoryProgress = null, List<string>? ClosedStoryArcs = null,
        Dictionary<int, int>? CityRates = null, int? RatesMonth = null,
        Dictionary<int, int>? CityStates = null, int? HistoryMonth = null,
        Dictionary<int, int>? HistoryNations = null, List<int>? HistoryDone = null,
        Dictionary<int, int>? AnnouncedYears = null,
        Dictionary<int, DateTime>? AnnouncedOn = null, Dictionary<int, DateTime>? FoundOn = null, Dictionary<int, int>? CityScales = null,
        List<Support.Local.Models.Player.Rumor>? Rumors = null,
        List<Support.Local.Models.Player.Rumor>? PersonLines = null,
        Dictionary<int, int>? CityBuildings = null, Dictionary<int, int>? NationStatus = null,
        List<int>? GiftedBarmaids = null, List<int>? RefusedBarmaids = null,
        int? Laps = null, Dictionary<string, int>? Purses = null,
        List<int>? Hidden = null,
        List<Player.Trace>? Traces = null,
        Dictionary<int, string>? NamedDiscoveries = null,
        Dictionary<int, bool>? ScriptedCities = null,
        List<int>? LastSupply = null,
        int? FleetCity = null, bool? SkipsCumulative = null, bool? Suspended = null,
        Dictionary<int, string>? Scooped = null, int? Drinking = null);

    /// <summary>
    /// 세이브에 적는 계약. <see cref="Support.Local.Models.Contract"/> 를 그대로 적을 수도
    /// 있지만, 세이브 형식은 놀이 쪽 모델이 바뀌어도 그대로여야 하므로 따로 둔다.
    /// </summary>
    /// <param name="Found">이 계약을 맺은 뒤 발견한 것(발견물 번호).</param>
    /// <param name="Inspector">딸려 온 감찰관 이름. 판 21 앞의 세이브에는 없다.</param>
    public sealed record Deal(
        int Hint, string Sponsor, string City, int Amount, DateTime SignedOn, int Years,
        List<int>? Found = null, string Inspector = "", bool ShipsLent = false,
        bool LoanAnnounced = false, bool BribeOpen = true);

    /// <summary>지금 상태를 적는다. 실패하면 까닭을 돌려준다(성공이면 빈 문자열).</summary>
    /// <param name="suspended">
    /// 중단저장인지 — 게임은 중단이면 <c>SAVEDATA.TMP</c> 로 적고, 그것을 불러온 판에 「아직 저장 안 됨」
    /// 비트(<c>0x005A4D18 &amp; 0x80</c>)를 세운다(<c>0x00478E2B</c>). 우리는 파일이 하나라 이 칸으로 든다.
    /// </param>
    /// <param name="path">
    /// 적을 자리. 안 주면 <see cref="Path"/> 다 — <b>자동저장</b>만 <see cref="AutoPath"/> 를 준다.
    /// </param>
    public static string Save(Player player, bool suspended = false, string? path = null)
    {
        var data = new Data(VirtualItemsFrom, DateTime.Now, player.Gold, player.Date,
                            player.CityId, player.CityName,
                            new Dictionary<string, int>(player.Skills), [.. player.Hints],
                            [.. player.Mates], [.. player.Met], [.. player.Items],
                            [.. player.Supplies], [.. player.Discoveries], DealOf(player),
                            player.Crew, [.. player.Announced], player.Fame,
                            [.. player.Stored], player.Savings,
                            [.. player.Ships.Select(s => s.Name)], player.Flagship,
                            player.Docked.ToDictionary(
                                e => e.Key, e => e.Value.Select(s => s.Name).ToList()),
                            [.. player.Ships.Select(s => s.Hp)],
                            player.Docked.ToDictionary(
                                e => e.Key, e => e.Value.Select(s => s.Hp).ToList()),
                            player.Fatigue, player.DaysAtSea,
                            [.. player.Ships.Select(s => s.Snapshot())],
                            player.Docked.ToDictionary(
                                e => e.Key, e => e.Value.Select(s => s.Snapshot()).ToList()),
                            player.Morale,
                            [.. player.Ships.Select(s => s.Name)],
                            player.Docked.ToDictionary(
                                e => e.Key, e => e.Value.Select(s => s.Name).ToList()),
                            [.. player.MateBook],
                            player.Explored.ToText(),
                            player.Spouse, [.. player.Heirs],
                            player.SpouseId,
                            player.Liking.ToDictionary(p => p.Key, p => p.Value),
                            player.Name, player.Family, player.Given,
                            new Dictionary<string, int>(player.Tongues),
                            player.Face, player.Fortune,
                            player.Hostility.ToDictionary(e => e.Key, e => e.Value),
                            [.. player.OpenedGates], [.. player.TalksLost],
                            player.Closeness.ToDictionary(e => e.Key, e => e.Value),
                            [.. player.KnownCities], (int)player.Ailments,
                            player.Formation, [.. player.CrewShares], player.HomePort,
                            // 바다에서 적을 때만 배 자리를 적는다 — 도시면 도시 앞바다로 연다.
                            player.CityId < 0 ? player.SeaCell?.X : null,
                            player.CityId < 0 ? player.SeaCell?.Y : null,
                            player.Condition,
                            // 능력치와 신상. 이 칸들이 없어 <b>불러오면 능력이 죄다 50</b> 이었다.
                            [.. player.Abilities], player.JobIndex, player.Age,
                            player.BirthMonth, player.BirthDay, player.Blood, player.Nation,
                            Cargo: [.. player.CargoHold],
                            TradeStock: player.TradeStock.ToDictionary(e => e.Key, e => e.Value.ToList()),
                            OpenedHints: [.. player.OpenedHints],
                            // 발견으로 판매가 켜진 교역품. 이 칸 앞의 세이브는 불러올 때 발견물 대본에서 다시 찾는다.
                            ActiveGoods: [.. player.ActiveGoods],
                            // 감찰관을 처벌해 배신한 후원자와, 기분이 상한 후원자.
                            Betrayals: [.. player.Betrayals],
                            Sulks: player.Sulks.ToDictionary(e => e.Key, e => e.Value),
                            // 아이 — 성별·태어나는 날·능력치·기능·언어. 이 칸 앞의 세이브는 이름(Heirs)만 있다.
                            Children: [.. player.Children],
                            // 초심자 개인 퀘스트라인(이야기0/1) — 묶인 책과 STORY 의뢰 기한, 장마다의 진행도·닫힘.
                            ActiveStoryBook: player.ActiveStoryBook,
                            StoryQuestDeadline: player.StoryQuestDeadline,
                            StoryProgress: player.StoryProgress.ToDictionary(e => e.Key, e => e.Value),
                            ClosedStoryArcs: [.. player.ClosedStoryArcs],
                            // 도시 시세·상태와 역사 대본 진행. 이 칸 앞의 세이브는 시세 100 · 역사를 1480년부터 되짚는다.
                            CityRates: player.CityRates.ToDictionary(e => e.Key, e => e.Value),
                            RatesMonth: player.RatesMonth,
                            CityStates: player.CityStates.ToDictionary(e => e.Key, e => e.Value),
                            HistoryMonth: player.HistoryMonth,
                            HistoryNations: player.HistoryNations.ToDictionary(e => e.Key, e => e.Value),
                            HistoryDone: [.. player.HistoryDone],
                            // 발표한 해 — 향신료·신대륙 기호품 값이 여기서부터 햇수를 센다.
                            AnnouncedYears: player.AnnouncedOn.ToDictionary(e => e.Key, e => e.Value.Year),
                            // 연표가 쓰는 날짜. 이 칸 앞의 세이브는 해만 있어 1월로 연다.
                            AnnouncedOn: player.AnnouncedOn.ToDictionary(e => e.Key, e => e.Value),
                            FoundOn: player.FoundOn.ToDictionary(e => e.Key, e => e.Value),
                            CityScales: player.CityScales.ToDictionary(e => e.Key, e => e.Value),
                            Rumors: [.. player.Rumors],
                            PersonLines: [.. player.PersonLines],
                            CityBuildings: player.CityBuildings.ToDictionary(e => e.Key, e => e.Value),
                            NationStatus: player.NationStatus.ToDictionary(e => e.Key, e => e.Value),
                            // 여급 형편 — 선물을 받아 봤는지, 퇴짜를 놓았는지.
                            GiftedBarmaids: [.. player.GiftedBarmaids],
                            RefusedBarmaids: [.. player.RefusedBarmaids],
                            // 지구를 몇 바퀴 돌았는지. 이 칸 앞의 세이브는 0 바퀴로 연다.
                            Laps: player.Laps,
                            // 후원자 지갑. 이 칸 앞의 세이브는 재력 가득으로 연다.
                            Purses: player.Purses.ToDictionary(e => e.Key, e => e.Value),
                            // 감찰관을 매수해 숨겨 둔 발견물.
                            Hidden: [.. player.HiddenDiscoveries],
                            // 누적 캐릭터가 먼저 세상에 알려 버린 발견물(발견물 칸 2).
                            Scooped: player.Scooped.ToDictionary(e => e.Key, e => e.Value),
                            // 주량 — 세대교체로만 바뀐다. 이 칸 앞의 세이브는 0 으로 연다.
                            Drinking: player.Drinking,
                            Traces: [.. player.Traces],
                            NamedDiscoveries: player.NamedDiscoveries.ToDictionary(e => e.Key, e => e.Value),
                            // 발견 대본이 세우고 없앤 도시. 이 칸 앞의 세이브는 날짜로만 센다.
                            ScriptedCities: player.ScriptedCities.ToDictionary(e => e.Key, e => e.Value),
                            // 보급 창 「전회분」. 이 칸 앞의 세이브는 0 으로 연다.
                            LastSupply: [.. player.LastSupply],
                            // 함대가 닻을 내린 도시. 이 칸 앞의 세이브는 모름으로 연다.
                            FleetCity: player.FleetCity == Player.FleetUnknown ? null : player.FleetCity,
                            // 「누적캐릭터를 등장시키지 않는다」 깃발. 이 칸 앞의 세이브는 서지 않은 것으로 연다.
                            SkipsCumulative: player.SkipsCumulative ? true : null,
                            Suspended: suspended ? true : null);
        try
        {
            string file = string.IsNullOrEmpty(path) ? Path : path;
            var dir = System.IO.Path.GetDirectoryName(file);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            File.WriteAllText(file, JsonSerializer.Serialize(data, Pretty));
            return "";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return ex.Message;
        }
    }

    /// <summary>맺고 있는 계약을 적을 꼴로. 계약이 없으면 null.</summary>
    private static Deal? DealOf(Player player) =>
        player.Contract is not { } c ? null
        : new Deal(c.Hint, c.Sponsor, c.City, c.Amount, c.SignedOn, c.Years, [.. c.Found],
                   c.Inspector, c.ShipsLent, c.LoanAnnounced, c.BribeOpen);

    /// <summary>적어 둔 계약을 놀이 쪽 모델로. 없으면 null.</summary>
    public static Contract? ContractOf(Data saved)
    {
        if (saved.Contract is not { } d) return null;

        var contract = new Contract(d.Hint, d.Sponsor, d.City, d.Amount, d.SignedOn, d.Years,
                                    d.Inspector);
        contract.Restore(d.Found);
        contract.ShipsLent = d.ShipsLent;
        contract.BribeOpen = d.BribeOpen;
        contract.LoanAnnounced = d.LoanAnnounced;
        return contract;
    }

    /// <summary>적어 둔 것을 읽는다. 없거나 깨졌으면 null.</summary>
    /// <param name="path">읽을 자리. 안 주면 <see cref="Path"/> 다.</param>
    public static Data? Load(string? path = null)
    {
        try
        {
            string file = string.IsNullOrEmpty(path) ? Path : path;
            if (!File.Exists(file)) return null;
            return JsonSerializer.Deserialize<Data>(File.ReadAllText(file));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }
}
