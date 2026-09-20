using System.Diagnostics;
using CdsHelper.Game.Engine.Discovery;
using CdsHelper.Game.Engine.Market;
using CdsHelper.Game.Local.Helpers;
using CdsHelper.Support.Local.Helpers;
using CdsHelper.Support.Local.Models;
using CdsHelper.Support.Local.Settings;
using Prism.Ioc;

namespace CdsHelper.Game.Engine;

/// <summary>
/// 한 판. 게임 폴더와 거기서 읽어 온 표들, 제독, 주사위, 소리를 한자리에 든다.
/// </summary>
/// <remarks>
/// 화면(<see cref="UI.Views.ShipMapWindow"/> · <see cref="UI.Views.CityPicView"/>)들은 이것을
/// 받아 쓴다. 예전에는 화면마다 제 표를 열어, 이를테면 힌트 표가 네 군데서 따로 열렸다.
///
/// 표는 <b>처음 쓸 때</b> 연다 — 도시 그림만 20MB 라 미리 읽으면 첫 화면이 늦다.
/// 한 번 못 열면 다시 찾지 않는다(파일이 없는 폴더에서 틱마다 뒤지지 않게).
/// </remarks>
public sealed class Game
{
    /// <summary>게임 폴더. 아직 모르면 빈 문자열이다.</summary>
    public string Directory { get; private set; } = "";

    /// <summary>주인공 — 소지금과 가진 배. 조선소에서 배를 사면 여기서 돈이 빠진다.</summary>
    public Player Player { get; private set; } = new();

    /// <summary>
    /// 주인공을 새로 앉힌다 — <b>NEW GAME</b> 이 부른다.
    /// </summary>
    /// <remarks>
    /// 한 판을 하고 첫 화면으로 돌아온 뒤 다시 시작하면 <b>앞 판이 그대로 묻어 온다</b> —
    /// 소지금·날짜·배·소지품·발견물이 다 남는다. 판 하나를 통째로 갈아 끼우는 것이
    /// 칸을 하나씩 되돌리는 것보다 확실하다.
    ///
    /// 창들은 <c>_game.Player</c> 를 그때그때 물어보므로 갈아 끼워도 따라온다.
    /// </remarks>
    /// <returns>앉아 있던 주인공. 물러나면 <see cref="UsePlayer"/> 로 도로 앉힌다.</returns>
    public Player NewPlayer()
    {
        var before = Player;
        Player = new Player();
        return before;
    }

    /// <summary>주인공을 도로 앉힌다 — 새 놀이를 짓다 말고 물러났을 때다.</summary>
    public void UsePlayer(Player player) => Player = player;

    /// <summary>바다 사건 주사위.</summary>
    public Random Random { get; } = new();

    /// <summary>배경음악. 폴더를 잡을 때 그 폴더를 함께 일러 준다.</summary>
    public BgmPlayer Bgm { get; } = new();

    /// <summary>
    /// 게임 폴더를 잡는다. 폴더가 갈리면 열어 둔 표를 잊는다 — 다음에 쓸 때 새 폴더에서 연다.
    /// </summary>
    public void SetDirectory(string directory)
    {
        if (Directory == directory) return;

        Directory = directory;
        _cityPics = null; _cityPicsTried = false;
        _buildings = null; _buildingsTried = false;
        _books = null; _booksTried = false;
        _hints = null; _hintsTried = false;
        _sponsors = null; _sponsorsTried = false;
        _items = null; _itemsTried = false;
        _sails = null; _sailsTried = false;
        _speakers = null; _speakersTried = false;
        _nations = null; _nationsTried = false;
        _goods = null; _goodsTried = false;
        _trade = null; _tradeTried = false;
        _cityRows = null; _cityRowsTried = false;
        _faces = null; _facesTried = false;
        _effects = null; _effectsTried = false;
        _eventAnims = null; _eventAnimsTried = false;
        _guests = null; _guestsTried = false;
        _templates = null; _templatesTried = false;
        _cells = null; _cellsTried = false;
        _photos = null; _photosTried = false;
        _itemText = null; _itemTextTried = false;
        _discoveryText = null; _discoveryTextTried = false;
        _itemArt = null;
        _discoveries = null; _discoveriesTried = false;
        _voyagers = null; _voyagersTried = false;
        _townFolk = null; _townFolkTried = false;
        _history = null; _historyTried = false;
        _stills = null; _stillsTried = false;
        _fighters = null; _fightersTried = false;
        _book = null; _bookTried = false;
        _barmaids = null; _barmaidsTried = false;

        Bgm.SetGameDirectory(directory);
    }

    /// <summary>효과음. 한 벌만 두고 나눠 쓴다(<see cref="SoundBank.Shared"/> 가 들고 있다).</summary>
    public SoundBank? Sfx => Directory.Length == 0 ? null : SoundBank.Shared(Directory);

    /// <summary>도시 표. 이름·문화권이 여기서 온다 — 게임 폴더가 아니라 우리 DB 것이다.</summary>
    public CityTable CityTable => _cities ??= Local.Helpers.CityTable.Open();

    /// <summary>도시 그림(CITYCG.CDS). 20MB 라 입항을 처음 할 때에야 연다.</summary>
    public CityPictures? CityPics =>
        Once(ref _cityPics, ref _cityPicsTried, CityPictures.Open,
             () => CityPictures.LastError, "도시 그림");

    /// <summary>건물 표(CDS_95.EXE). 건물 자리·이름·가르치는 기능이 여기서 온다.</summary>
    public CityBuildingTable? Buildings =>
        Once(ref _buildings, ref _buildingsTried, CityBuildingTable.Open,
             () => CityBuildingTable.LastError, "건물 표");

    /// <summary>책 표(CDS_95.EXE). 도서관 서가를 채운다.</summary>
    public BookTable? Books =>
        Once(ref _books, ref _booksTried, BookTable.Open, () => BookTable.LastError, "책 표");

    /// <summary>힌트 표(CDS_95.EXE). 이름·등급·자금·기한이 여기서 온다.</summary>
    public HintTable? Hints =>
        Once(ref _hints, ref _hintsTried, HintTable.Open, () => HintTable.LastError, "힌트 표");

    /// <summary>후원자 표(CDS_95.EXE). 어느 자리에 누가 앉았는지와 얼굴 번호가 여기서 온다.</summary>
    public SponsorTable? Sponsors =>
        Once(ref _sponsors, ref _sponsorsTried, SponsorTable.Open,
             () => SponsorTable.LastError, "후원자 표");

    /// <summary>아이템 표(CDS_95.EXE). 발견물이 주는 물건 이름을 여기서 얻는다.</summary>
    public ItemTable? Items =>
        Once(ref _items, ref _itemsTried, ItemTable.Open, () => ItemTable.LastError, "아이템 표");

    /// <summary>나라 표(CDS_95.EXE). 도시 정보 창의 나라 칸에 쓴다.</summary>
    public NationTable? Nations =>
        Once(ref _nations, ref _nationsTried, NationTable.Open,
             () => NationTable.LastError, "나라 표");

    /// <summary>
    /// <b>제독 나라 수도의 문화권</b> — 육상전에서 아군 쪽 낯을 가릴 때 쓴다(<c>0x00447070(0)</c>).
    /// </summary>
    /// <remarks>
    /// 게임은 아군 대장 묶음(<c>+0x98</c>)의 인물 → 나라 → 수도 → <c>+0x58</c> 로 거슬러
    /// 문화권을 얻는다. 못 찾으면 서유럽(0)으로 둔다.
    /// </remarks>
    public int MyCulture =>
        CityRows?.CultureOf(Nations?.Find(Player.Nation)?.Capital ?? -1) ?? 0;

    /// <summary>교역품 표(CDS_95.EXE). 도시 특산품을 낼 때 쓴다.</summary>
    public GoodsTable? Goods =>
        Once(ref _goods, ref _goodsTried, GoodsTable.Open, () => GoodsTable.LastError, "교역품 표");

    /// <summary>
    /// 도시 시세. 시장·여관·상단 띠가 <b>한 벌</b>을 나눠 쓴다.
    /// </summary>
    /// <remarks>
    /// 값 자체는 주인공이 들고(세이브에 함께 적힌다), 이것은 그 값을 읽고 쓰는 창구다 —
    /// 새 판으로 주인공을 갈아 끼워도 창구는 그대로 따라간다.
    /// </remarks>
    public MarketRates Rates => _rates ??= new MarketRates(this);

    private MarketRates? _rates;

    /// <summary>역사 대본(<c>HIST_EV.CDS</c> · <c>HISTCHR.CDS</c>) — 도시 상태를 바꾼다.</summary>
    public CityHistory? History =>
        Once(ref _history, ref _historyTried, CityHistory.Open, () => "HIST_EV.CDS 를 못 읽었습니다", "역사 대본");

    private CityHistory? _history;
    private bool _historyTried;

    /// <summary>밀린 달을 세는 중인지 — 세는 동안 시세를 물어도 다시 들어오지 않게.</summary>
    private bool _catchingUp;

    /// <summary>한꺼번에 흔들 시세 달 수. 오래 건너뛰어도 목표 가까이 모이고 나면 더 셀 까닭이 없다.</summary>
    private const int MaxDriftMonths = 36;

    /// <summary>
    /// 달이 바뀐 만큼 게임의 달 셈(<c>0x0044B2A0</c>)을 따라잡는다 — 시세 흔들림, 그 다음 역사 대본.
    /// </summary>
    /// <remarks>
    /// 날짜는 항해·숙박·수련 따위 여러 곳에서 넘어가므로, 달마다 부르는 대신 <b>값을 물을 때</b> 밀린 달을 센다.
    /// 시세를 한 번도 안 센 판(새 판·옛 세이브)은 지금 달부터 세고, 역사 대본을 안 돌린 판은
    /// 1480년 1월부터 되짚는다 — 날짜로 정해지는 것이라 옛 세이브도 그 해의 도시 상태로 열린다.
    /// </remarks>
    public void CatchUpMonths()
    {
        var player = Player;
        if (_catchingUp || player.Date.Year < CityHistory.FirstYear) return;

        int now = CityHistory.MonthKey(player.Date.Year, player.Date.Month);
        if (player.RatesMonth == 0 || player.RatesMonth > now) player.SetRatesMonth(now);
        if (player.HistoryMonth == 0 || player.HistoryMonth > now) player.SetHistoryMonth(CityHistory.StartKey);
        if (player.RatesMonth == now && player.HistoryMonth == now) return;

        _catchingUp = true;
        try
        {
            var voyagers = Voyagers;
            bool Found(int id, DateTime when) =>
                player.HasFound(id) || (voyagers?.TakenBy(id, when) ?? -1) >= 0;

            int first = Math.Min(player.RatesMonth, player.HistoryMonth) + 1;
            for (int key = first; key <= now; key++)
            {
                int year = (key - 1) / 12, month = (key - 1) % 12 + 1;
                var when = new DateTime(year, month, 1);

                if (key > player.RatesMonth && now - key < MaxDriftMonths)
                    MarketRates.Drift(player, Random, CityExeTable.Count,
                                      Found(MarketRates.Aztec, when), Found(MarketRates.Inca, when));

                if (key > player.HistoryMonth)
                    History?.RunMonth(player, year, month, CityRows, Nations, Found);
            }
            player.SetRatesMonth(now);
            player.SetHistoryMonth(now);
        }
        finally
        {
            _catchingUp = false;
        }
    }

    /// <summary>교역소 표(CDS_95.EXE). 지역 공통품 · 기준가 · 도시 특산가다.</summary>
    public TradeTable? Trade =>
        Once(ref _trade, ref _tradeTried, TradeTable.Open, () => TradeTable.LastError, "교역소 표");

    private TradeTable? _trade;
    private bool _tradeTried;

    /// <summary>술집 소문 표(CDS_95.EXE <c>0x00525078</c>). 「정보를 듣는다」가 쓴다.</summary>
    public RumorTable? Rumors =>
        Once(ref _rumors, ref _rumorsTried, RumorTable.Open, () => RumorTable.LastError, "소문 표");

    private RumorTable? _rumors;
    private bool _rumorsTried;

    /// <summary>술 표(CDS_95.EXE). 술집이 그 고장에서 파는 술과 값이다.</summary>
    public DrinkTable? Drinks =>
        Once(ref _drinks, ref _drinksTried, DrinkTable.Open, () => DrinkTable.LastError, "술 표");

    private DrinkTable? _drinks;
    private bool _drinksTried;

    /// <summary>EXE 도시 표(문화권·시장 물건). 시장과 여관이 같이 쓴다.</summary>
    /// <remarks>
    /// 놀이 중 바뀐 나라·규모(역사 대본)를 덮어 읽도록 주인공을 이어 두고, 꺼낼 때마다 밀린 달을 먼저 센다
    /// (<see cref="CatchUpMonths"/>) — 성문·도시정보가 그 달의 나라를 본다.
    /// </remarks>
    public CityExeTable? CityRows
    {
        get
        {
            var rows = Once(ref _cityRows, ref _cityRowsTried, CityExeTable.Open,
                            () => CityExeTable.LastError, "EXE 도시 표");
            if (rows == null) return null;
            rows.LivePlayer ??= () => Player;
            CatchUpMonths();
            return rows;
        }
    }

    /// <summary>
    /// 시설 화자표(CDS_95.EXE). 어느 건물에서 누가 말을 거는지 — 문화권마다 다르다.
    /// </summary>
    public SpeakerFaceTable? Speakers =>
        Once(ref _speakers, ref _speakersTried, SpeakerFaceTable.Open,
             () => SpeakerFaceTable.LastError, "화자표");

    /// <summary>돛 효율표(CDS_95.EXE). 배 속도를 잴 때 쓴다.</summary>
    public SailTable? Sails =>
        Once(ref _sails, ref _sailsTried, SailTable.Open, () => SailTable.LastError, "돛 효율표");

    /// <summary>
    /// 발견물. 배가 어디에 서면 무엇이 발견되는지가 여기서 온다.
    /// </summary>
    /// <remarks>힌트 표는 없어도 연다 — 그때는 힌트로 열리는 것만 안 뜬다.</remarks>
    public DiscoveryLog? Discoveries
    {
        get
        {
            if (_discoveries != null || _discoveriesTried) return _discoveries;
            _discoveriesTried = true;
            if (Directory.Length == 0) return null;

            var table = DiscoveryTable.Open(Directory);
            if (table == null)
            {
                Debug.WriteLine($"[Game] 발견물 표 없음: {DiscoveryTable.LastError}");
                return null;
            }
            return _discoveries = new DiscoveryLog(table, Hints, Voyagers);
        }
    }

    /// <summary>
    /// 역사 항해자 열넷(<c>HISTCHR.CDS</c>) — 이 놀이의 <b>유일한 경쟁자</b>다.
    /// </summary>
    /// <remarks>
    /// 못 읽어도 놀이는 돈다 — 그때는 아무도 선수를 치지 않아 발견물이 늘 열려 있다.
    /// </remarks>
    public HistoryVoyages? Voyagers =>
        Once(ref _voyagers, ref _voyagersTried, HistoryVoyages.Open,
             () => HistoryVoyages.LastError, "역사 항해자");

    /// <summary>도시 그림에 서 있는 마을 사람 표 — 누르면 그 고장 이야기를 한다.</summary>
    public TownFolkTable? TownFolk =>
        Once(ref _townFolk, ref _townFolkTried, TownFolkTable.Open,
             () => TownFolkTable.LastError, "마을 사람 표");

    private TownFolkTable? _townFolk;
    private bool _townFolkTried;

    /// <summary>여급 표 — 술집에 서는 127명. 궁합이 여기서 나온다.</summary>
    public BarmaidTable? Barmaids =>
        Once(ref _barmaids, ref _barmaidsTried, BarmaidTable.Open,
             () => BarmaidTable.LastError, "여급 표");

    /// <summary>발견했을 때 뜨는 그림(DSTILL.CDS). 못 읽으면 글만 낸다.</summary>
    /// <summary>펼친 책 그림(OPENBOOK.CDS) — 도서관에서 힌트를 읽을 때 뜬다.</summary>
    public OpenBookArt? Book =>
        Once(ref _book, ref _bookTried, OpenBookArt.Open,
             () => OpenBookArt.LastError, "펼친 책 그림");

    /// <summary>일기토 그림(FIGHTER.CDS) — 제독 한 벌과 상대 여덟 벌.</summary>
    public FighterSprites? Fighters =>
        Once(ref _fighters, ref _fightersTried, FighterSprites.Open,
             () => FighterSprites.LastError, "일기토 그림");

    public DiscoveryStills? Stills =>
        Once(ref _stills, ref _stillsTried, DiscoveryStills.Open,
             () => DiscoveryStills.LastError, "발견물 그림");

    /// <summary>발견 대본이 트는 움직이는 그림(DISCOVER.CDS) — 존왕의 술잔 따위.</summary>
    public DiscoveryClips? Clips =>
        Once(ref _clips, ref _clipsTried, DiscoveryClips.Open,
             () => DiscoveryClips.LastError, "발견 애니메이션");

    /// <summary>화면에 겹쳐 도는 동그란 애니메이션(MPEFFECT.CDS).</summary>
    public EffectAnim? Effects =>
        Once(ref _effects, ref _effectsTried, EffectAnim.Open,
             () => EffectAnim.LastError, "애니메이션");

    /// <summary>
    /// 지도 위에 통째로 겹쳐 도는 사건 애니메이션(EVANIME.CDS) — 회오리·폭풍·눈보라·덤불.
    /// </summary>
    public EventAnimation? EventAnims =>
        Once(ref _eventAnims, ref _eventAnimsTried, EventAnimation.Open,
             () => EventAnimation.LastError, "사건 애니메이션");

    /// <summary>술집 손님 그림. 못 읽으면 손님만 안 선다.</summary>
    public TavernGuests? Guests =>
        Once(ref _guests, ref _guestsTried, TavernGuests.Open,
             () => TavernGuests.LastError, "손님 그림");

    /// <summary>인물 밑표(CDS_95.EXE). 세이브에 없는 인물의 나라·직업이 여기서 온다.</summary>
    public PersonTemplate? PersonTemplates =>
        Once(ref _templates, ref _templatesTried, PersonTemplate.Open,
             () => PersonTemplate.LastError, "인물 밑표");

    /// <summary>대본 화자 이름(일본어) → 후원자·인물 번호(CDS_95.EXE).</summary>
    public Table.SpeakerNameTable? SpeakerNames =>
        Once(ref _speakerNames, ref _speakerNamesTried, Table.SpeakerNameTable.Open, () => "", "화자 이름표");

    /// <summary>WORLD.CDS 칸. 지도 창 없이 뭍인지 볼 때 쓴다 — 인물 이동의 끝점을 잡는다.</summary>
    public WorldCells? Cells =>
        Once(ref _cells, ref _cellsTried, WorldCells.Open, () => WorldCells.LastError, "WORLD.CDS 칸");

    /// <summary>건물 사진(MPCG.CDS). 건물에 들어갈 때 뜨는 타원 사진이다.</summary>
    public BuildingPhoto? Photos =>
        Once(ref _photos, ref _photosTried, BuildingPhoto.Open,
             () => BuildingPhoto.LastError, "건물 사진");

    /// <summary>아이템 설명문. 없으면 설명 자리가 빈 채로 뜬다.</summary>
    public ItemDescriptions? ItemText =>
        Once(ref _itemText, ref _itemTextTried, ItemDescriptions.Open,
             () => ItemDescriptions.LastError, "아이템 설명문");

    /// <summary>발견물 설명문(<c>0x0057AA78</c>). 백과사전 오른쪽 면에 뜬다.</summary>
    public DiscoveryDescriptions? DiscoveryText =>
        Once(ref _discoveryText, ref _discoveryTextTried, DiscoveryDescriptions.Open,
             () => DiscoveryDescriptions.LastError, "발견물 설명문");

    /// <summary>
    /// 사건 스틸(<c>EVSTILL.CDS</c>) 열여섯 장 — 난파 · 폭풍 · 반란 · 놀이 끝 따위다.
    /// </summary>
    /// <remarks>
    /// 발견물 스틸(<see cref="Stills"/>, <c>DSTILL.CDS</c>)과 <b>짜임이 같고 파일만 다르다</b>.
    /// 이벤트 대본의 「EVSTILL 이미지 표시」와 반란 · 놀이 끝 화면이 이것을 쓴다.
    /// </remarks>
    public DiscoveryStills? EventStills =>
        Directory.Length == 0 ? null
        : _eventStills ??= DiscoveryStills.Open(Directory, "EVSTILL.CDS");

    private DiscoveryStills? _eventStills;

    /// <summary>아이템 그림. asset/item 만 있으면 게임 폴더가 없어도 나온다.</summary>
    public ItemArt? ItemPictures => _itemArt ??= ItemArt.Open(Directory);

    /// <summary>
    /// 초상화(MALE.CDS · FEMALE.CDS). 게임 폴더를 몰라도 연다 — 우리 asset 폴더에도 있다.
    /// </summary>
    public Portraits? Faces
    {
        get
        {
            if (_faces != null || _facesTried) return _faces;
            _facesTried = true;

            _faces = Portraits.Open(Directory);
            if (_faces == null) Debug.WriteLine($"[Game] 초상화 없음: {Portraits.LastError}");
            return _faces;
        }
    }

    /// <summary>
    /// 인물표 — 술집에 앉은 사람과 부하의 신상이 여기서 온다.
    /// </summary>
    /// <remarks>
    /// <b>세이브를 보지 않는다.</b> 같이 깔린 <c>인물표.json</c>(<see cref="PersonTable"/>)이
    /// 원본이라 <c>SAVEDATA.CDS</c> 가 없어도 술집에 사람이 선다. 개발 창에서 표를 고치면
    /// <see cref="PersonTable.Revision"/> 이 올라가 다음에 물을 때 새로 읽는다.
    /// </remarks>
    public TavernRoster? Roster
    {
        get
        {
            if (World is not { } world) return null;

            world.Advance(Player.Date);

            // 해가 바뀌면 나이 문에 드나드는 사람이 생기므로 해도 열쇠에 넣는다 —
            // 일곱 살이던 후안·데·에스칸데가 1491년에 술집에 앉는 것이 이 때문이다.
            int year = Player.Date.Year;
            if (_roster != null && _rosterWalk == (world.Revision, year)) return _roster;

            _rosterWalk = (world.Revision, year);
            _roster = TavernRoster.From(world.People,
                                        r => world.Table.ActiveOn(r, year),
                                        r => world.Table.AgeOn(r, year));
            return _roster;
        }
    }

    /// <summary>
    /// 인물이 옮겨 다니는 세상. 표가 고쳐지면 새로 연다.
    /// </summary>
    /// <remarks>
    /// 날짜를 따라잡는 것은 <see cref="Roster"/> 가 물을 때다 — 굴림이 씨를 뿌린 주사위라
    /// 언제 따라잡아도 같은 세상이 되므로 시계에 손을 걸어 둘 까닭이 없다.
    /// </remarks>
    /// <summary>
    /// 그 도시가 <b>지금</b> 지도에 서 있는지.
    /// </summary>
    /// <remarks>
    /// 신대륙 식민 도시 스물셋은 켤 때 없고 해가 가야 하나씩 선다
    /// (<see cref="CityFounding"/>). 산타마르타·리마는 걷어 줄 이벤트가 아예 없어 끝까지
    /// 안 서고, 멕시코는 아스텍을 무너뜨리는 발견 이벤트(263)로만 선다(<c>Player.ScriptedCities</c>).
    /// </remarks>
    public bool CityStanding(int city) => CityFounding.Standing(city, Player.Date, Player.ScriptedCities);

    /// <summary>
    /// 그 도시를 <b>아는지</b> — 지도에 뜨는지.
    /// </summary>
    /// <remarks>
    /// 유럽·지중해 101곳은 켤 때부터 알고(<see cref="CityExeTable.KnownAtStart"/>),
    /// 나머지는 가까이 가야 안다(<see cref="Player.Know"/>). 게임의 도시 레코드
    /// <c>+0x04</c> 비트 0 이다.
    /// </remarks>
    public bool CityKnown(int city) =>
        (CityRows?.KnownAtStart(city) ?? true) || Player.Knows(city);

    /// <summary>그 도시가 <b>지금 지도에 보이는가</b> — 서 있고, 알고 있어야 한다.</summary>
    public bool CityVisible(int city) => CityStanding(city) && CityKnown(city);

    public PersonWorld? World
    {
        get
        {
            if (_world != null && _worldRevision == PersonTable.Revision) return _world;

            var table = PersonTable.Open();
            _worldRevision = PersonTable.Revision;
            _roster = null;
            _rosterWalk = (-1, -1);

            if (table.IsEmpty)
            {
                Debug.WriteLine($"[Game] 인물 표가 비었습니다: {PersonTable.LastError}");
                return _world = null;
            }
            // 역사 항해자 열넷은 주사위가 아니라 제 대본대로 움직인다 — 대본을 물려준다.
            return _world = new PersonWorld(table, CityRows, Buildings,
                                            Support.Local.Models.Player.StartDate,
                                            Voyagers, Discoveries?.Table, Cells);
        }
    }

    /// <summary>
    /// 그 건물에서 말을 거는 사람의 얼굴. 없으면 null 이다.
    /// </summary>
    /// <remarks>
    /// 화자표(<see cref="Speakers"/>)에서 번호를 집어 초상화를 푼다. 게임도 시설에
    /// 들어설 때 <c>[건물코드][문화권]</c> 으로 한 번 집어 시설 객체에 넣어 둔다
    /// (<c>0x004A2500</c>).
    /// </remarks>
    /// <param name="buildingCode">건물 코드(항구 0 · 조선소 6 · 도서관 8 …).</param>
    /// <param name="culture">그 마을 문화권 번호.</param>
    /// <summary>
    /// <b>부관</b>(부하 첫 자리)의 얼굴. 부관이 없거나 신상을 못 찾으면 <b>뱃사람(MALE #299)</b>이다.
    /// </summary>
    /// <remarks>
    /// 대원이 대신 말하는 자리는 죄다 이 얼굴을 쓴다 — 성문에서 문지기 말을 못 알아들어
    /// 한 마디 덧붙일 때, 쳐들어가기 전에 되물을 때가 그렇다.
    ///
    /// 게임의 말 창 <c>0x00478280</c> 은 얼굴 <c>0x12B</c>(299)에서 시작해, 넘겨받은 사람이 제독
    /// 객체가 아니면 그 사람 얼굴로 바꾼다. <c>0x0047CC60(0, 1)</c> 이 부관이 없으면 제독 객체를
    /// 넘기므로 그때는 뱃사람 얼굴이 선다(볼트 81).
    /// </remarks>
    public uint[]? AideFace
    {
        get
        {
            string mate = Player.MateAt(0);
            if (mate.Length > 0 && MateInfo(mate) is { Face: >= 0 and < 0xFFFF } who
                && Faces?.TryGetBgra(who.Face, female: false) is { } face)
                return face;
            return Faces?.TryGetBgra(SailorFace, female: false);
        }
    }

    /// <summary>부하 첫 자리(부관)가 있으면 그 얼굴, 없으면 null — 부관이 있을 때만 얼굴을 걸고 말하는 창이 쓴다.</summary>
    public uint[]? MateSpeaks => Player.MateAt(0).Length > 0 ? AideFace : null;

    /// <summary>부관이 없을 때 말하는 뱃사람 얼굴 번호(<c>0x00478280</c> 의 <c>0x12B</c>).</summary>
    public const int SailorFace = 299;

    public uint[]? SpeakerFace(int buildingCode, int culture)
    {
        if (Speakers is not { } speakers) return null;

        int face = speakers.FaceOf(buildingCode, culture);
        // 여관 주인은 여자다 — 표의 성별 칸이 어느 CDS 에서 꺼낼지 일러 준다.
        return face < 0 ? null : Faces?.TryGetBgra(face, speakers.IsFemale(buildingCode));
    }

    /// <summary>
    /// 그 부하의 신상. 우리 세이브에 적어 둔 것을 먼저 보고, 없으면(판 20 앞에 들인 부하)
    /// 게임 세이브의 인물표에서 채워 <b>그 자리에서 적어 둔다</b> — 한 번 채우면 다음부터는
    /// 우리 것만으로 뜬다.
    /// </summary>
    public Player.MateInfo? MateInfo(string name)
    {
        if (Player.MateInfoOf(name) is { } mine) return mine;
        if (Roster?.Find(name) is not { } person) return null;

        var filled = Town.Tavern.MateInfoOf(person);
        Player.RememberMate(filled);
        return filled;
    }

    /// <summary>
    /// 힌트 이름. 게임 표를 읽었으면 그것으로, 아니면 우리 DB 것으로, 그것도 없으면 번호로 낸다.
    /// </summary>
    public string HintName(int id)
    {
        if (Hints?.Find(id)?.Name is { Length: > 0 } name) return name;

        if (_hintNames == null)
        {
            _hintNames = [];
            try
            {
                var service = ContainerLocator.Container.Resolve<HintService>();
                service.InitializeAsync(System.IO.Path.Combine(AppContext.BaseDirectory,
                                                               "cdshelper.db")).Wait();
                _hintNames = service.GetAllHintNames();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Game] 힌트 이름 로드 실패: {ex.Message}");
            }
        }
        return _hintNames.TryGetValue(id, out var found) && found.Length > 0 ? found : $"힌트 {id}";
    }

    /// <summary>그 도시의 문화권("이슬람" · "북유럽" …). 모르면 빈 문자열.</summary>
    /// <remarks>
    /// 이름은 앱 DB 에서 나온다. 도구 창에서 손으로 갈아 둔 것이 있으면 그 번호의
    /// 이름으로 낸다(<see cref="CityCultureEdits"/>) — 건물 사진과 술집 손님이 번호가
    /// 아니라 <b>이름</b>으로 갈리기 때문에, 여기까지 따라오지 않으면 얼굴만 바뀌고
    /// 사진은 그대로인 어정쩡한 마을이 된다.
    /// </remarks>
    public string CultureOf(int city)
    {
        int changed = CityCultureEdits.Of(city);
        return changed == CityCultureEdits.None
            ? CityTable.CultureOf(city)
            : CityCultureEdits.NameOf(changed);
    }

    /// <summary>그 도시의 이름. 표에 없으면 번호로 물러선다.</summary>
    public string CityName(int city) => CityTable.NameOf(city);

    /// <summary>
    /// 화면을 닫을 때 부른다 — 소리를 끄고, 20MB 짜리 도시 그림을 놓는다.
    /// </summary>
    public void Close()
    {
        Bgm.Dispose();
        _cityPics = null;
        _cityPicsTried = false;
    }

    /// <summary>지금 판을 적는다. 적히는 자리는 <see cref="GameSave"/> 참고.</summary>
    public string Save(bool suspended = false)
    {
        string error = GameSave.Save(Player, suspended);
        // 적고 나면 「아직 저장 안 됨」이 풀린다(0x00479174 · 0x004794A2).
        if (error.Length == 0) Unsaved = false;
        return error;
    }

    /// <summary>
    /// 지금 판을 <b>자동저장</b> 자리에 적는다 — 손으로 적은 것은 안 건드린다.
    /// </summary>
    /// <remarks>
    /// 원본에 없는 것이라 「아직 저장 안 됨」도 안 푼다 — 그 비트는 손으로 적었을 때만
    /// 풀리는 것이 맞다.
    /// </remarks>
    public string AutoSave() => GameSave.Save(Player, suspended: false, path: GameSave.AutoPath);

    /// <summary>
    /// 아직 저장하지 않은 판인지(<c>0x005A4D18</c> 비트 <c>0x80</c>) — 중단저장을 불러오면 서고, 저장하면 풀린다.
    /// 도시 「게임 종료」가 이 값을 보고 한 번 더 묻는다(<c>0x00568D38</c>).
    /// </summary>
    public bool Unsaved { get; set; }

    /// <summary>
    /// 표를 처음 쓸 때 한 번만 연다. 폴더를 모르거나 못 열면 <c>null</c> 인 채로 둔다.
    /// </summary>
    private T? Once<T>(ref T? slot, ref bool tried, Func<string, T?> open,
                       Func<string> lastError, string what) where T : class
    {
        if (slot != null || tried) return slot;
        tried = true;
        if (Directory.Length == 0) return null;

        slot = open(Directory);
        if (slot == null) Debug.WriteLine($"[Game] {what} 없음: {lastError()}");
        return slot;
    }

    private CityTable? _cities;
    private CityPictures? _cityPics;
    private CityBuildingTable? _buildings;
    private BookTable? _books;
    private HintTable? _hints;
    private SponsorTable? _sponsors;
    private ItemTable? _items;
    private SailTable? _sails;
    private SpeakerFaceTable? _speakers;
    private NationTable? _nations;
    private GoodsTable? _goods;
    private CityExeTable? _cityRows;
    private DiscoveryLog? _discoveries;
    private OpenBookArt? _book;
    private bool _bookTried;
    private FighterSprites? _fighters;
    private bool _fightersTried;
    private DiscoveryStills? _stills;
    private bool _stillsTried;
    private DiscoveryClips? _clips;
    private bool _clipsTried;
    private BarmaidTable? _barmaids;
    private bool _barmaidsTried;
    private TavernRoster? _roster;
    private PersonWorld? _world;

    /// <summary>인물 표를 읽었을 때의 판. 표가 고쳐지면 달라져 세상을 새로 연다.</summary>
    private int _worldRevision = -1;

    /// <summary>술집 목록을 짤 때 사람들이 서 있던 자리. 누가 움직이면 달라진다.</summary>
    private (int Walk, int Year) _rosterWalk = (-1, -1);
    private Portraits? _faces;
    private EffectAnim? _effects;
    private EventAnimation? _eventAnims;
    private bool _eventAnimsTried;
    private TavernGuests? _guests;
    private PersonTemplate? _templates;
    private Table.SpeakerNameTable? _speakerNames;
    private bool _speakerNamesTried;
    private bool _templatesTried;
    private WorldCells? _cells;
    private bool _cellsTried;
    private BuildingPhoto? _photos;
    private ItemDescriptions? _itemText;
    private DiscoveryDescriptions? _discoveryText;
    private bool _discoveryTextTried;
    private ItemArt? _itemArt;
    private Dictionary<int, string>? _hintNames;

    private bool _cityPicsTried, _buildingsTried, _booksTried, _hintsTried;
    private HistoryVoyages? _voyagers;
    private bool _voyagersTried;
    private bool _sponsorsTried, _itemsTried, _sailsTried, _discoveriesTried;
    private bool _nationsTried, _goodsTried, _cityRowsTried, _facesTried, _speakersTried;
    private bool _effectsTried, _guestsTried, _photosTried, _itemTextTried;
}
