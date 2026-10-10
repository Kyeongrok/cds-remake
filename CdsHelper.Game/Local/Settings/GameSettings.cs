using System.IO;
using System.Text.Json;

namespace CdsHelper.Game.Local.Settings;

/// <summary>
/// 새 주인공을 짓다 「다음」을 누를 때 적어 두는 것 — 다음에 새로 지을 때 그대로 채운다.
/// <see cref="GameSettings.CharacterDraftKeep"/> 가 지나면 버린다.
/// </summary>
public sealed class CharacterDraft
{
    public string Family { get; set; } = "";
    public string Given { get; set; } = "";
    public int Age { get; set; }
    public int BirthMonth { get; set; }
    public int BirthDay { get; set; }
    public int Blood { get; set; }
    public int Nation { get; set; }
    public int Face { get; set; }

    /// <summary>능력치 창에서 계산기로 적은 바라는 값(모드 「직업 누르면 다시 굴림」). 안 적은 칸은 null.</summary>
    public int?[]? Targets { get; set; }

    /// <summary>마지막으로 적은 때(UTC).</summary>
    public DateTime SavedAt { get; set; }
}

/// <summary>이대로 <c>game-settings.json</c> 이 된다.</summary>
public sealed class GameSettingsData
{
    /// <summary>새 주인공을 짓다 적어 둔 것. 없으면 null.</summary>
    public CharacterDraft? CharacterDraft { get; set; }

    /// <summary>앱을 켤 때 함대 보기(Direct3D) 창을 바로 띄울지. 기본은 켬.</summary>
    public bool AutoOpenShipMap { get; set; } = true;

    /// <summary>함대 창에서 배경음악을 틀지. 기본은 켬.</summary>
    public bool BgmEnabled { get; set; } = true;

    /// <summary>효과음(닻·거절 따위)을 낼지. 기본은 켬.</summary>
    public bool SfxEnabled { get; set; } = true;

    /// <summary>배경음악·효과음의 크기(0~100). 기본은 다 크게.</summary>
    public int BgmVolume { get; set; } = GameSettings.MaxVolume;

    /// <summary>해상 지도 배율(0.5~1.5, 0.25 칸). 기본 0.75 — 원본 크기에 맞춘 값이다.</summary>
    public double MapScale { get; set; } = GameSettings.DefaultMapScale;
    public int SfxVolume { get; set; } = GameSettings.MaxVolume;

    /// <summary>게임 창 단추의 좌우 여백(점).</summary>
    public int BandPad { get; set; } = GameSettings.DefaultBandPad;

    /// <summary>마을·항구에 들고 날 때 보내는 날수(1~10). 기본 10 — 원본 값이다.</summary>
    public int PortDays { get; set; } = GameSettings.DefaultPortDays;

    /// <summary>인물이 떠날지 굴리는 간격 — 0 이면 매월 1일(원본), 1~30 이면 그 날수마다.</summary>
    public int PersonRollDays { get; set; }

    /// <summary>인물이 떠날 확률의 분모(1~5) — N분의 1. 기본 5(원본).</summary>
    public int PersonMoveOdds { get; set; } = GameSettings.DefaultPersonMoveOdds;

    /// <summary>일기토에서 최근에 싸운 상대 이름 — 앞이 가장 최근이다.</summary>
    public List<string> RecentDuelFoes { get; set; } = [];

    /// <summary>도시 창이 열릴 때 줄 효과. <see cref="Settings.CityOpenEffect"/> 의 이름이다.</summary>
    public string CityOpenEffect { get; set; } = "Expand";

    /// <summary>게임 창 크기 — <see cref="GameSettings.Resolutions"/> 의 몇째인지.</summary>
    public int Resolution { get; set; } = GameSettings.DefaultResolution;

    /// <summary>지도 위에 좌표 상자를 겹쳐 보일지. 개발용이라 꺼 두고 시작한다(내놓은 판을 처음 깔면 꺼져 있다).</summary>
    public bool ShowCoordOverlay { get; set; }

    /// <summary>바다에 있을 때 배 속도 쪽지를 띄울지. 놀이에는 없는 것이라 꺼 두고 시작한다.</summary>
    public bool ShowShipSpeed { get; set; }

    /// <summary>항해 중 지도 왼쪽 위에 출항한 지 며칠인지 동그라미로 띄울지. 원본에 없어 꺼 두고 시작한다.</summary>
    public bool ShowSeaDays { get; set; }

    /// <summary>지도가 배를 늘 한가운데에 두고 따라 흐를지. 원본은 가장자리에서 넘기므로 꺼 두고 시작한다.</summary>
    public bool ShipCentered { get; set; }

    /// <summary>발견물 지도에 위도·경도 25도 격자를 깔지. 꺼 두고 시작한다.</summary>
    public bool DiscoveryMapGrid { get; set; }

    /// <summary>발견물 지도에 도시를 찍을지. 꺼 두고 시작한다.</summary>
    public bool DiscoveryMapCities { get; set; }

    /// <summary>발견물 지도에서 발견물 표식을 감출지. 기본은 보인다.</summary>
    public bool DiscoveryMapHideSpots { get; set; }

    /// <summary>바다 입체 효과(물결 굴곡·햇빛·깊이·해안 물보라). 켜 두고 시작한다.</summary>
    public bool SeaEffect { get; set; } = true;

    /// <summary>조우하면 저절로 「도망」을 고를지. 꺼 두고 시작한다.</summary>
    public bool AutoFlee { get; set; }

    /// <summary>릴리즈 노트를 마지막으로 보여 준 판(「1.0.51」 꼴). 아직 없으면 빈 글이다.</summary>
    public string NotesSeenVersion { get; set; } = "";

    /// <summary>놀이 통계를 보내도 되는지. 아직 안 물었으면 null 이다.</summary>
    public bool? SendStats { get; set; }

    /// <summary>항구에서 출항할 때 물·식량이 10일분 밑이면 10일분까지 저절로 사 싣는다. 꺼 두고 시작한다.</summary>
    public bool AutoSupply { get; set; }

    /// <summary>자동 보급을 「최대」로 할지 — 거짓이면 10일분까지다.</summary>
    public bool AutoSupplyMax { get; set; }

    /// <summary>항구에서 출항할 때 선원이 최저 승원 밑이면 그만큼 저절로 모집한다. 꺼 두고 시작한다.</summary>
    public bool AutoCrew { get; set; }

    /// <summary>바다 입체 효과의 밝기 배수(0.6~1.6). 0.85 에서 시작한다.</summary>
    public double SeaBrightness { get; set; } = 0.85;

    /// <summary>고해상도 바다에서 해류 결·띠의 짙기(0~1). 0.1 에서 시작한다.</summary>
    public double SeaFlowAmount { get; set; } = 0.1;

    /// <summary>구름을 부드럽게(바둑판 반투명을 참 반투명으로 풀어 매끈하게 늘려) 그릴지. 켜 두고 시작한다.</summary>
    public bool SmoothClouds { get; set; } = true;

    /// <summary>고해상도 바다(물 점을 화면 해상도로 새로 그리고 해안선을 곡선으로). 켜 두고 시작한다.</summary>
    public bool HiResSea { get; set; } = true;

    /// <summary>도트 확대 필터(대각선 계단을 사선으로). 꺼 두고 시작한다.</summary>
    public bool PixelFilter { get; set; }

    /// <summary>배 항적(항적·그림자·출렁임). 꺼 두고 시작한다.</summary>
    public bool ShipWake { get; set; }

    /// <summary>뭍 세부 질감(지형마다 화면 해상도 잔무늬). 꺼 두고 시작한다.</summary>
    public bool LandDetail { get; set; }

    /// <summary>미니맵 풍향 화살표. 꺼 두고 시작한다.</summary>
    public bool MiniMapWind { get; set; }

    /// <summary>미니맵 해류 화살표. 꺼 두고 시작한다.</summary>
    public bool MiniMapCurrent { get; set; }

    /// <summary>미니맵에 찾은 발견물 점(빨강)을 찍을지. 꺼 두고 시작한다.</summary>
    public bool MiniMapFound { get; set; }

    /// <summary>미니맵에 아직 못 찾은 발견물 점(회색)을 찍을지. 꺼 두고 시작한다.</summary>
    public bool MiniMapYet { get; set; }

    /// <summary>미니맵에 도시 점(주황)을 찍을지. 꺼 두고 시작한다.</summary>
    public bool MiniMapCities { get; set; }

    /// <summary>미니맵 창 크기 배율(0.5~2.5). 1 이 260x160 이다.</summary>
    public double MiniMapScale { get; set; } = 1.0;

    /// <summary>마우스를 올렸거나 배가 밑에 들어갔을 때의 미니맵 불투명도(0.05~1.0).</summary>
    public double MiniMapHoverOpacity { get; set; } = 0.35;

    /// <summary>미니맵 표식(발견물 · 도시 점) 크기(지도 점, 1~5).</summary>
    public double MiniMapMarkSize { get; set; } = 2.5;

    /// <summary>미니맵의 내 자리 점(파란 점) 크기(지도 점, 2~12).</summary>
    public double MiniMapShipSize { get; set; } = 5;

    /// <summary>지도 왼쪽 아래에 「발견물 N / 전체」 상자를 띄울지. 놀이에는 없는 것이라 꺼 두고 시작한다.</summary>
    public bool ShowDiscoveryCount { get; set; }

    /// <summary>저장·발견물 지도·모드 단축키(글쇠 이름). 비면 기본값을 쓴다.</summary>
    public string SaveKey { get; set; } = "V";
    public string MapKey { get; set; } = "D";
    public string ModKey { get; set; } = "M";
    public string ItemsKey { get; set; } = "I";
    public string NavKey { get; set; } = "R";

    /// <summary>자동항해를 「최소 조타」로 몰지. 끄면 「속도 중시」다.</summary>
    public bool SteadyHelm { get; set; }
    public string HintsKey { get; set; } = "H";
    public string PersonKey { get; set; } = "X";
    public string PatronKey { get; set; } = "P";

    /// <summary>항해 글쇠(<see cref="GameSettings.SailActions"/> 의 Id → 글쇠 이름). 안 적힌 것은 기본값이고, 빈 글이면 안 쓴다.</summary>
    public Dictionary<string, string>? SailKeys { get; set; }

    /// <summary>보관 창에서 줄을 누르면 곧바로 옮길지. 원본에 없어 꺼 두고 시작한다.</summary>
    public bool QuickStorage { get; set; }

    /// <summary>함대정보 「짐」 판의 교역품을 누르면 비싸게 팔리는 도시 순위를 낼지. 원본에 없어 꺼 두고 시작한다.</summary>
    public bool CargoPriceRank { get; set; }

    /// <summary>지도 위에 만난 사람 상자를 겹쳐 보일지. 놀이에는 없는 것이라 꺼 두고 시작한다.</summary>
    public bool ShowPeopleOverlay { get; set; }


    /// <summary>항해·뭍 이동 중에 지도 오른쪽 아래에 미니맵을 띄울지. 놀이에는 없는 것이라 꺼 두고 시작한다.</summary>
    public bool ShowMiniMap { get; set; }

    /// <summary>미니맵의 불투명도(0.1~1.0). 모드에서 미니맵을 켰을 때만 조절한다.</summary>
    public double MiniMapOpacity { get; set; } = 0.75;

    /// <summary>
    /// 풀린 미니게임 번호(0~6). 원본은 레지스트리 <c>MG00</c>~<c>MG06</c> 이라 세이브가 아니라 설치에 딸린다.
    /// </summary>
    public List<int> UnlockedMinigames { get; set; } = [];

    /// <summary>도시에 들어가면 도시 그림 왼쪽에 기능·언어 쪽지를 띄울지. 놀이에는 없는 것이라 꺼 두고 시작한다.</summary>
    public bool ShowSkillOverlay { get; set; }

    /// <summary>도시에 들어가면 현재 계약 힌트를 기능·언어 쪽지 위에 띄울지. 놀이에는 없는 것이라 꺼 두고 시작한다.</summary>
    public bool ShowContractHintOverlay { get; set; }

    /// <summary>도시에 들어가면 현재 함대의 배 이름 쪽지를 띄울지. 기존 기능을 유지하도록 켜 두고 시작한다.</summary>
    public bool ShowFleetOverlay { get; set; } = true;

    /// <summary>도시에 들어가면 아직 남은 힌트 열 개를 함대 쪽지 아래에 띄울지.</summary>
    public bool ShowHintOverlay { get; set; }

    /// <summary>햄버거에 「발견물 지도」 줄을 낼지. 놀이에는 없는 것이라 꺼 두고 시작한다.</summary>
    public bool ShowDiscoveryMapMenu { get; set; }

    /// <summary>
    /// 계약을 맺을 때 배가 있으면 후원자가 「배를 빌리겠습니까?」를 묻는다 — 끄면 안 묻고
    /// 안 빌린다. 원본에는 늘 묻는 자리라 켜 두고 시작한다.
    /// </summary>
    public bool AskLendShips { get; set; } = true;

    /// <summary>바다에서 해적·이슬람 함대가 붙는 확률 배수의 차례(<see cref="GameSettings.SeaRaidScales"/>). 원본은 x1.</summary>
    public int SeaRaidScale { get; set; } = GameSettings.DefaultSeaRaidScale;

    /// <summary>정보 창·상단 띠에 「생명력」 줄을 낼지. 원본 탐험정보에는 없어 꺼 두고 시작한다.</summary>
    public bool ShowVitalityInfo { get; set; }

    /// <summary>소지품일람 줄마다 아이템 그림과 효과를 낼지(스폰서 일람처럼). 원본은 이름만이라 꺼 두고 시작한다.</summary>
    public bool ItemListPictures { get; set; }

    /// <summary>취득 힌트 일람을 목록·설명 두 칸으로 볼지(모드 창처럼). 원본은 고르고 결정해야 펴지므로 꺼 두고 시작한다.</summary>
    public bool HintBrowser { get; set; }

    /// <summary>보급품 · 교역품 무게를 안 따질지. 원본은 따지므로 꺼 두고 시작한다.</summary>
    public bool NoWeight { get; set; }

    /// <summary>윈도 글꼴로 찍는 창 글씨를 리디바탕으로 쓸지. 켜 두고 시작한다.</summary>
    public bool RidiFont { get; set; } = true;

    /// <summary>
    /// 모드 창 「권장」에서 마지막으로 고른 단계(0 오리지널 · 1 초보 · 2 중수 · 3 고수). 고른 적이 없으면 0(오리지널)이다.
    /// 놀이 통계가 이 값을 그대로 보낸다.
    /// </summary>
    public int ModPreset { get; set; }

    /// <summary>스폰서 일람 줄에 얼굴 · 발견물 취향 · 권력 · 친밀도를 낼지. 원본은 이름만이다. 켜 두고 시작한다.</summary>
    public bool PatronListEnhanced { get; set; } = true;

    /// <summary>조선소 「특별주문」 줄을 낼지. 원본에 없어 꺼 두고 시작한다.</summary>
    public bool SpecialOrder { get; set; }

    /// <summary>덧붙인 아이템 그림(asset/item 206~)을 쓸지. 끄면 원본 그림이다.</summary>
    public bool EnhancedItemArt { get; set; }

    /// <summary>정보 제공 등급(0 기본 · 1 일반 · 2 상세). 기본은 원본만큼만 보인다.</summary>
    public int InfoLevel { get; set; }

    /// <summary>소지품 창에서 바로 보관 · 판매할지. 원본에 없어 꺼 두고 시작한다.</summary>
    public bool HandyInventory { get; set; }

    /// <summary>바다에서 다가간 함대의 신상 쪽지를 띄울지. 원본에 없어 꺼 두고 시작한다.</summary>
    public bool FleetCard { get; set; }

    /// <summary>부하편성에서 부하를 해고할 수 있게 할지. 원본에 없어 꺼 두고 시작한다.</summary>
    public bool MateDismiss { get; set; }

    /// <summary>보고를 마치고 나설 때 부하 재계약을 안 물을지(부하 해고를 켰을 때만). 켜 두고 시작한다.</summary>
    public bool SkipRecontract { get; set; } = true;

    /// <summary>인물정보 고르기를 초상화 · 기능 목록으로 띄울지. 원본에 없어 꺼 두고 시작한다.</summary>
    public bool PersonInfoList { get; set; }

    /// <summary>인물정보 고르기를 리디바탕 글씨의 전용 목록으로 띄울지. 원본에 없어 꺼 두고 시작한다.</summary>
    public bool PersonInfoEnhanced { get; set; }

    /// <summary>지도를 마우스 휠로 키우고 줄일지. 원본에 없어 꺼 두고 시작한다.</summary>
    public bool WheelZoom { get; set; }

    /// <summary>녹화용 — 도시 화면을 지도 창 안에 비춰 그릴지(CaptureMirror). 실험이라 꺼 두고 시작한다.</summary>
    public bool CaptureMirror { get; set; }

    /// <summary>새 주인공을 지을 때 지난번에 적은 것을 채워 줄지. 원본에 없어 꺼 두고 시작한다.</summary>
    public bool KeepCharacterDraft { get; set; }

    /// <summary>개발 — 일기토에서 내 체력이 0 이 안 된다. 꺼 두고 시작한다.</summary>
    public bool DuelImmortal { get; set; }

    /// <summary>개발 — 바다 반란이 일어날 몫(%). 100 이 원본 그대로, 0 이면 안 일어난다.</summary>
    public int MutinyRate { get; set; } = 100;

    /// <summary>게임 로드 · 이어하기 창을 리디바탕 표로 띄울지. 원본에 없어 꺼 두고 시작한다.</summary>
    public bool RidiLoadList { get; set; }

    /// <summary>수에즈 지협에 바닷길을 뚫을지. 원본에 없어 꺼 두고 시작한다.</summary>
    public bool SuezCanal { get; set; }

    /// <summary>스폰서 제안에서 고르기 없이 자금 증가를 고를지. 원본에 없어 꺼 두고 시작한다.</summary>
    public bool AutoFundRaise { get; set; }

    /// <summary>뭍에서 발견물 지도로 자동이동할지. 실험이라 꺼 두고 시작한다.</summary>
    public bool LandAutoWalk { get; set; }

    /// <summary>작위 제도(공적 · 작위 · 혜택)를 쓸지. 원본에 없어 꺼 두고 시작한다.</summary>
    public bool Nobility { get; set; }

    /// <summary>스핑크스 퀴즈에서 정답 줄에 표를 달지. 원본에 없어 꺼 두고 시작한다.</summary>
    public bool SphinxHelper { get; set; }

    /// <summary>켤 때 로고·오프닝 동영상을 틀지. 원본은 늘 트므로 켜 두고 시작한다.</summary>
    public bool PlayOpeningMovie { get; set; } = true;

    /// <summary>새 주인공 능력치 창에서 직업 단추를 누르면 능력치를 다시 굴릴지. 원본에는 없어 꺼 두고 시작한다.</summary>
    public bool RerollOnJob { get; set; }

    /// <summary>
    /// <b>도시에 들어설 때마다</b> 자동저장 파일에 적을지. 놀이에는 없는 것이라 꺼 두고 시작한다.
    /// </summary>
    public bool AutoSaveOnPort { get; set; }

    /// <summary>햄버거에 「여급 수첩」 줄을 낼지. 놀이에는 없는 것이라 꺼 두고 시작한다.</summary>
    public bool ShowBarmaidBookMenu { get; set; }

    /// <summary>지도를 Ctrl+클릭해 배를 그 자리에 놓을지. 켠 채로 시작한다.</summary>
    public bool PlaceShipByCtrlClick { get; set; } = true;

    /// <summary>햄버거에 「인물 이동」 줄을 낼지. 놀이에는 없는 것이라 꺼 두고 시작한다.</summary>
    public bool ShowPersonMoveMenu { get; set; }

    /// <summary>
    /// 게임 상단 띠에 켜 둔 칸 이름들("날짜"·"소지금" …). 한 번도 안 건드렸으면 null 이라
    /// 부르는 쪽 기본값이 선다.
    /// </summary>
    public List<string>? BarCells { get; set; }

    /// <summary>
    /// 상단 띠에 켜 둔 칸 이름들을 <b>자리마다</b>(0 바다 · 1 뭍 · 2 도시) 따로 든다. 한 번도 안
    /// 건드린 자리는 빠져 있어 게임 기본값이 선다.
    /// </summary>
    /// <remarks>
    /// 게임도 띠 갈래마다 제 마스크를 든다(<c>[+0xF8]</c> 워드 셋, 레지스트리에 여섯 바이트로 적는다 —
    /// <c>0x0047E2BD</c>). 예전 <see cref="BarCells"/> 는 한 벌뿐이라 쓰지 않는다.
    /// </remarks>
    public Dictionary<int, List<string>>? BarCellsByPlace { get; set; }

    /// <summary>지도 위에 바람·해류 화살표를 얹을지.</summary>
    public bool ShowFlowArrows { get; set; }

    /// <summary>발견물 지도에 풍향 화살표를 얹을지.</summary>
    public bool DiscoveryMapWind { get; set; }

    /// <summary>발견물 지도에 해류 화살표를 얹을지.</summary>
    public bool DiscoveryMapCurrent { get; set; }

    /// <summary>육상전 모의전 창이 지난번에 차렸던 짜임. 한 번도 안 차렸으면 null.</summary>
    public LandSparData? LandSpar { get; set; }

    /// <summary>부대배치 「전회」 — 지난번 여섯 칸의 고른 번호. 한 번도 안 정했으면 null.</summary>
    public int[]? LandDeployLast { get; set; }

    /// <summary>
    /// 곡 번호별로 갈아 끼운 파일(전체 경로). 비어 있으면 아무것도 안 바꾼 것이다.
    /// 원본에 없는 것이라 <see cref="CustomBgmEnabled"/> 가 꺼져 있으면 등록해 두어도 안 쓴다.
    /// </summary>
    public Dictionary<int, string> CustomBgmTracks { get; set; } = new();

    /// <summary>등록한 곡 갈아 끼우기를 쓸지 — 모드 창에서 켜고 끈다.</summary>
    public bool CustomBgmEnabled { get; set; }
}

/// <summary>
/// 육상전 모의전 창(<c>LandSparDialog</c>)이 지난번에 차렸던 짜임.
/// </summary>
/// <remarks>
/// 같은 짜임으로 여러 판을 굴려 보는 자리라 <b>앱을 껐다 켜도</b> 남긴다. 손으로 고친
/// 파일이 들어와도 놀이는 굴러가야 하므로, 읽는 쪽이 칸 수와 범위를 다시 본다.
/// </remarks>
public sealed class LandSparData
{
    /// <summary>아군 여섯 자리의 병종. −1 이면 빈 자리다.</summary>
    public int[]? Mine { get; set; }

    /// <summary>적 여섯 자리.</summary>
    public int[]? Theirs { get; set; }

    public int MyMen { get; set; }
    public int FoeMen { get; set; }
    public int Culture { get; set; }
    public int Terrain { get; set; }
}

/// <summary>
/// 이 앱이 품고 있는 놀이에만 쓰는 설정. <c>%APPDATA%\CdsHelper\game-settings.json</c> 에 적는다.
/// </summary>
/// <remarks>
/// 앱 설정(<c>CdsHelper.Support</c> 의 <c>AppSettings</c>)과 갈라 두었다. 그쪽은 지도·발견물처럼
/// 이 앱이 도구로서 하는 일이고, 여기는 놀이 쪽이라 섞일 까닭이 없다. 갈라 두면 놀이를 통째로
/// 들어내도 앱 설정은 그대로다.
///
/// <b>실제 CDS_95 를 자동으로 조작하는 값(<c>AutoConfirmDialog</c> 따위)은 여기 없다</b> —
/// 그건 놀이가 아니라 도구 쪽 일이라 <c>AppSettings</c> 에 남아 있다.
///
/// 예전에는 이 값들이 <c>settings.json</c> 에 같이 들어 있었다. 새 파일이 없으면 그 파일에서
/// 한 번 옮겨 온다(<see cref="MigrateFromLegacy"/>) — 쓰던 사람이 맞춰 둔 값을 잃지 않는다.
/// </remarks>
public static class GameSettings
{
    /// <summary>
    /// 게임 창 단추의 좌우 여백 기본값(점).
    /// </summary>
    /// <remarks>
    /// 띠 마구리는 실제로 16점이라, 16 이면 마구리가 통째로 글자 밖에 선다. 그만큼 다 비우면
    /// 조금 헐거워 보여 눈으로 맞춰 12 로 잡았다 — 마구리 무늬가 글자에 살짝 걸치는 자리다.
    ///
    /// 이 값은 <b>적어 둔 것이 없을 때만</b> 선다. 한 번이라도 개발 창에서 만졌으면
    /// <c>game-settings.json</c> 에 적힌 값이 이긴다.
    /// </remarks>
    public const int DefaultBandPad = 12;

    /// <summary>단추 여백을 이 사이로만 잡는다.</summary>
    public const int MinBandPad = 0, MaxBandPad = 32;

    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "CdsHelper", "game-settings.json");

    /// <summary>옛 자리 — 갈라 놓기 전에는 여기 같이 들어 있었다.</summary>
    private static readonly string LegacyPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "CdsHelper", "settings.json");

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    private static GameSettingsData _data = new();
    private static bool _loaded;

    static GameSettings() => Load();

    /// <summary>
    /// 적어 둔 것을 읽는다. 두 번 불러도 한 번만 읽는다.
    /// </summary>
    /// <remarks>
    /// 앱을 켤 때 한 번 불러 둔다(<c>App.OnStartup</c>). 옛 <c>settings.json</c> 에서 옮겨 오는
    /// 일이 여기서 벌어지는데, 그 전에 앱 설정이 먼저 저장되면 옛 값이 지워진 뒤라 놓치게 된다.
    /// </remarks>
    public static void Load()
    {
        if (_loaded) return;
        _loaded = true;

        try
        {
            if (File.Exists(FilePath))
            {
                _data = JsonSerializer.Deserialize<GameSettingsData>(File.ReadAllText(FilePath)) ?? new();
                return;
            }

            if (MigrateFromLegacy()) Save();
        }
        catch
        {
            // 읽다 넘어져도 놀이는 기본값으로 굴러가야 한다.
            _data = new GameSettingsData();
        }
    }

    /// <summary>옛 <c>settings.json</c> 에 있던 값을 한 번 옮겨 온다. 옮길 게 있었으면 참.</summary>
    private static bool MigrateFromLegacy()
    {
        if (!File.Exists(LegacyPath)) return false;

        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(LegacyPath));
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return false;

            var moved = new GameSettingsData();
            bool any = false;

            any |= Bool("AutoOpenShipMap", v => moved.AutoOpenShipMap = v);
            any |= Bool("BgmEnabled", v => moved.BgmEnabled = v);
            any |= Bool("SfxEnabled", v => moved.SfxEnabled = v);
            any |= Bool("ShowCoordOverlay", v => moved.ShowCoordOverlay = v);
            any |= Bool("ShowPeopleOverlay", v => moved.ShowPeopleOverlay = v);
            any |= Bool("ShowFlowArrows", v => moved.ShowFlowArrows = v);

            if (root.TryGetProperty("BandPad", out var pad) && pad.TryGetInt32(out int padValue))
            {
                moved.BandPad = Math.Clamp(padValue, MinBandPad, MaxBandPad);
                any = true;
            }

            if (root.TryGetProperty("CityOpenEffect", out var effect) && effect.ValueKind == JsonValueKind.String)
            {
                moved.CityOpenEffect = effect.GetString() ?? "Expand";
                any = true;
            }

            if (root.TryGetProperty("BarCells", out var cells) && cells.ValueKind == JsonValueKind.Array)
            {
                moved.BarCells = [.. cells.EnumerateArray()
                    .Where(c => c.ValueKind == JsonValueKind.String)
                    .Select(c => c.GetString()!)];
                any = true;
            }

            if (any) _data = moved;
            return any;

            bool Bool(string name, Action<bool> set)
            {
                if (!root.TryGetProperty(name, out var value)) return false;
                if (value.ValueKind is not (JsonValueKind.True or JsonValueKind.False)) return false;

                set(value.GetBoolean());
                return true;
            }
        }
        catch
        {
            return false;
        }
    }

    private static void Save()
    {
        try
        {
            string? dir = Path.GetDirectoryName(FilePath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

            File.WriteAllText(FilePath, JsonSerializer.Serialize(_data, Json));
        }
        catch
        {
            // 못 적어도 이번 판은 굴러간다.
        }
    }

    /// <summary>새 주인공 적어 둔 것을 들고 있는 동안 — 세 시간이다.</summary>
    public static readonly TimeSpan CharacterDraftKeep = TimeSpan.FromHours(3);

    /// <summary>
    /// 적어 둔 새 주인공. 없거나 <see cref="CharacterDraftKeep"/> 가 지났으면 null.
    /// </summary>
    public static CharacterDraft? CharacterDraft =>
        Get(d => d.CharacterDraft is { } draft && DateTime.UtcNow - draft.SavedAt <= CharacterDraftKeep ? draft : null);

    /// <summary>
    /// 새 주인공 적어 둔 것을 고친다 — 지난 것이면 빈 것에서 시작한다. 적은 때는 지금으로 갈린다.
    /// </summary>
    public static void EditCharacterDraft(Action<CharacterDraft> edit) => Set(d =>
    {
        var draft = d.CharacterDraft is { } old && DateTime.UtcNow - old.SavedAt <= CharacterDraftKeep ? old : new();
        edit(draft);
        draft.SavedAt = DateTime.UtcNow;
        d.CharacterDraft = draft;
    });

    private static T Get<T>(Func<GameSettingsData, T> read)
    {
        Load();
        return read(_data);
    }

    private static void Set(Action<GameSettingsData> write)
    {
        Load();
        write(_data);
        Save();
    }

    // ── 값들 ────────────────────────────────────────────────────────────────

    /// <summary>
    /// 그 미니게임이 MINI GAME 차림표에 풀렸는지 — 발견 이벤트에서 그 놀이를 이기면 풀린다
    /// (<c>0x00406B60</c> 이 레지스트리 <c>MG%02d</c> 를 켜고 <c>0x0045FA54</c> 벌이 읽는다).
    /// </summary>
    public static bool IsMinigameUnlocked(int game) => Get(d => d.UnlockedMinigames.Contains(game));

    /// <summary>미니게임 하나를 푼다.</summary>
    public static void UnlockMinigame(int game)
    {
        if (IsMinigameUnlocked(game)) return;
        Set(d => d.UnlockedMinigames.Add(game));
    }

    /// <summary>
    /// 앱을 켤 때 함대 보기(Direct3D) 창을 바로 띄울지. 기본은 <b>켬</b>이다 — 이 앱이
    /// 하는 일이 곧 그 창이라, 켤 때마다 메뉴에서 한 번 더 누르게 할 까닭이 없다.
    /// </summary>
    public static bool AutoOpenShipMap
    {
        get => Get(d => d.AutoOpenShipMap);
        set => Set(d => d.AutoOpenShipMap = value);
    }

    /// <summary>함대 창의 배경음악을 틀지. 설정 창에서 켜고 끈다.</summary>
    /// <summary>소리 크기의 위와 한 번에 오르내리는 폭.</summary>
    public const int MaxVolume = 100, VolumeStep = 10;

    /// <summary>배경음악 크기(0~100).</summary>
    public static int BgmVolume
    {
        get => Math.Clamp(Get(d => d.BgmVolume), 0, MaxVolume);
        set => Set(d => d.BgmVolume = Math.Clamp(value, 0, MaxVolume));
    }

    /// <summary>효과음 크기(0~100).</summary>
    public static int SfxVolume
    {
        get => Math.Clamp(Get(d => d.SfxVolume), 0, MaxVolume);
        set => Set(d => d.SfxVolume = Math.Clamp(value, 0, MaxVolume));
    }

    public static bool BgmEnabled
    {
        get => Get(d => d.BgmEnabled);
        set => Set(d => d.BgmEnabled = value);
    }

    /// <summary>효과음을 낼지. 배경음악과 따로 켜고 끈다.</summary>
    public static bool SfxEnabled
    {
        get => Get(d => d.SfxEnabled);
        set => Set(d => d.SfxEnabled = value);
    }

    /// <summary>해상 지도 배율의 아래·위·기본과 한 칸.</summary>
    public const double MinMapScale = 0.5, MaxMapScale = 1.5, DefaultMapScale = 0.75, MapScaleStep = 0.25;

    /// <summary>
    /// 해상 지도를 얼마나 크게 그릴지(0.5~1.5, 0.25 칸). 1 이 예전 크기(한 점에 1/32 칸)이고,
    /// 원본 화면에 맞춘 기본은 0.75(1/24 칸)다.
    /// </summary>
    public static double MapScale
    {
        get => Snap(Get(d => d.MapScale));
        set => Set(d => d.MapScale = Snap(value));
    }

    /// <summary>배율을 0.25 칸에 맞추고 범위 안으로 자른다.</summary>
    private static double Snap(double scale) =>
        Math.Clamp(Math.Round(scale / MapScaleStep) * MapScaleStep, MinMapScale, MaxMapScale);

    /// <summary>인물 떠남 확률 분모의 아래·위·기본.</summary>
    public const int MinPersonMoveOdds = 1, MaxPersonMoveOdds = 5, DefaultPersonMoveOdds = 5;

    /// <summary>인물 굴림 간격의 위 끝. 0 은 「매월 1일」(원본)이다.</summary>
    public const int MaxPersonRollDays = 30;

    /// <summary>
    /// 인물이 떠날지 굴리는 간격 — 0 이면 매월 1일(원본), 1~30 이면 1480년 1월 1일부터 그 날수마다.
    /// 역사 항해자의 대본은 이것과 상관없이 매월 1일에만 든다.
    /// </summary>
    public static int PersonRollDays
    {
        get => Math.Clamp(Get(d => d.PersonRollDays), 0, MaxPersonRollDays);
        set => Set(d => d.PersonRollDays = Math.Clamp(value, 0, MaxPersonRollDays));
    }

    /// <summary>인물이 떠날 확률의 분모(1~5) — 굴릴 때마다 N분의 1. 원본은 5다.</summary>
    public static int PersonMoveOdds
    {
        get => Math.Clamp(Get(d => d.PersonMoveOdds), MinPersonMoveOdds, MaxPersonMoveOdds);
        set => Set(d => d.PersonMoveOdds = Math.Clamp(value, MinPersonMoveOdds, MaxPersonMoveOdds));
    }

    /// <summary>들고 나는 날수의 아래·위·기본.</summary>
    public const int MinPortDays = 1, MaxPortDays = 10, DefaultPortDays = 10;

    /// <summary>
    /// 마을·항구에 들고 날 때 보내는 날수(1~10). 원본은 열흘이고, 개발 창에서 줄여 시험할 수 있다.
    /// </summary>
    public static int PortDays
    {
        get => Math.Clamp(Get(d => d.PortDays), MinPortDays, MaxPortDays);
        set => Set(d => d.PortDays = Math.Clamp(value, MinPortDays, MaxPortDays));
    }

    /// <summary>
    /// 게임 창 단추의 좌우 여백(점).
    /// </summary>
    /// <remarks>
    /// 띠는 왼끝·가운데·오른끝 셋으로 짓고 양 끝(마구리)이 16점씩이다. 이 값만큼을 글자
    /// 바깥에 비워 두므로, 16 이면 마구리가 통째로 글자 밖에 서고 그보다 작으면 글자가
    /// 마구리 위로 조금씩 올라앉는다. 바꾼 값은 <b>다음에 여는 창</b>부터 든다.
    /// </remarks>
    /// <summary>
    /// <b>저장</b> 단축키. 글쇠 이름(<see cref="System.Windows.Input.Key"/>)이고 기본은 <c>V</c> 다.
    /// </summary>
    public static string SaveKey
    {
        get => Get(d => d.SaveKey);
        set => Set(d => d.SaveKey = value);
    }

    /// <summary><b>발견물 지도</b> 단축키. 기본은 <c>D</c> 다.</summary>
    public static string MapKey
    {
        get => Get(d => d.MapKey);
        set => Set(d => d.MapKey = value);
    }

    /// <summary><b>모드</b> 창 단축키. 기본은 <c>M</c> 이다.</summary>
    public static string ModKey
    {
        get => Get(d => d.ModKey);
        set => Set(d => d.ModKey = value);
    }

    /// <summary><b>소지품 정보</b> 단축키. 기본은 <c>I</c> 다.</summary>
    /// <remarks>
    /// 예전 기본은 <c>R</c> 이었는데 네비게이션이 그 글쇠를 가져갔다. 예전 값이 적힌 설정 파일은 둘이 같은
    /// 글쇠가 되므로, 겹치면 이쪽이 <c>I</c> 로 비켜 선다.
    /// </remarks>
    public static string ItemsKey
    {
        get => Get(d => string.Equals(d.ItemsKey, d.NavKey, StringComparison.OrdinalIgnoreCase) ? "I" : d.ItemsKey);
        set => Set(d => d.ItemsKey = value);
    }

    /// <summary>
    /// 자동항해의 조타 — 참이면 「최소 조타」, 거짓(기본)이면 「속도 중시」다. 네비게이션 창에서 고른다.
    /// </summary>
    /// <remarks>
    /// 속도 중시는 그은 선에 바짝 붙어 가느라 뱃머리를 자주 튼다. 최소 조타는 길을 8방위로 곧게 뻗는 토막으로
    /// 바꿔 토막마다 뱃머리를 한 번만 세운다 — 해안 가까이처럼 그렇게 못 바꾸는 데서는 조금 더 걸린다.
    /// </remarks>
    public static bool SteadyHelm
    {
        get => Get(d => d.SteadyHelm);
        set => Set(d => d.SteadyHelm = value);
    }

    /// <summary><b>네비게이션</b>(목적지 도시를 골라 자동항해) 단축키. 기본은 <c>R</c> 이다.</summary>
    public static string NavKey
    {
        get => Get(d => d.NavKey);
        set => Set(d => d.NavKey = value);
    }

    /// <summary><b>힌트 정보</b>(취득 힌트 일람) 단축키. 기본은 <c>H</c> 다.</summary>
    public static string HintsKey
    {
        get => Get(d => d.HintsKey);
        set => Set(d => d.HintsKey = value);
    }

    /// <summary><b>후원자 정보</b> 단축키. 기본은 <c>P</c> 다.</summary>
    public static string PatronKey
    {
        get => Get(d => d.PatronKey);
        set => Set(d => d.PatronKey = value);
    }

    /// <summary><b>인물정보</b> 단축키. 기본은 <c>X</c> 다.</summary>
    public static string PersonKey
    {
        get => Get(d => d.PersonKey);
        set => Set(d => d.PersonKey = value);
    }

    /// <summary>
    /// 항해 글쇠로 하는 일들 — 단축키 창 「항해」 탭이 이 차례로 늘어놓는다. 숫자판 조타(1~9 · 5 · 0)는 이것과 따로 늘 먹는다.
    /// </summary>
    /// <remarks>
    /// 숫자판이 없는 자판(텐키리스)에서는 숫자 글쇠가 한 줄로 늘어서 있어 방위를 잡기 어렵다 — 그래서 방향키를 기본으로 둔다.
    /// 대각선은 비워 두고 시작한다(방향키 둘을 함께 누르면 대각선이다).
    /// </remarks>
    public static readonly (string Id, string Label, string Default)[] SailActions =
    [
        ("Up", "북 (위)", "Up"), ("Down", "남 (아래)", "Down"),
        ("Left", "서 (왼쪽)", "Left"), ("Right", "동 (오른쪽)", "Right"),
        ("UpLeft", "북서", ""), ("UpRight", "북동", ""),
        ("DownLeft", "남서", ""), ("DownRight", "남동", ""),
        ("Anchor", "정지 · 출발", "Space"), ("Command", "커맨드 창", ""),
    ];

    /// <summary>그 항해 글쇠의 이름. 안 쓰면 빈 글이다.</summary>
    public static string SailKey(string id) => Get(d =>
        d.SailKeys != null && d.SailKeys.TryGetValue(id, out string? key)
            ? key
            : SailActions.FirstOrDefault(a => a.Id == id).Default ?? "");

    /// <summary>항해 글쇠를 바꾼다. 빈 글이면 그 일에는 글쇠를 안 쓴다.</summary>
    public static void SetSailKey(string id, string key) => Set(d => (d.SailKeys ??= [])[id] = key);

    /// <summary>편리한 보관함 — 자택 「보관」 창에서 줄을 누르면 곧바로 반대쪽으로 옮긴다. 모드 창에서 켜고 끈다.</summary>
    public static bool QuickStorage
    {
        get => Get(d => d.QuickStorage);
        set => Set(d => d.QuickStorage = value);
    }

    /// <summary>적하 시세 순위 — 함대정보 「짐」 판에서 교역품을 누르면 비싸게 팔리는 도시 순위를 낸다. 모드 창에서 켜고 끈다.</summary>
    public static bool CargoPriceRank
    {
        get => Get(d => d.CargoPriceRank);
        set => Set(d => d.CargoPriceRank = value);
    }

    /// <summary>몇 사람까지 적어 둘지.</summary>
    public const int MaxRecentFoes = 5;

    /// <summary>
    /// 일기토에서 최근에 싸운 상대 — 앞이 가장 최근이다.
    /// </summary>
    public static IReadOnlyList<string> RecentDuelFoes => Get(d => (IReadOnlyList<string>)[.. d.RecentDuelFoes]);

    /// <summary>
    /// 그 사람을 맨 앞에 적어 둔다. 이미 있으면 앞으로 끌어 올린다.
    /// </summary>
    public static void RememberDuelFoe(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return;

        Set(d =>
        {
            d.RecentDuelFoes.RemoveAll(one => one == name);
            d.RecentDuelFoes.Insert(0, name);
            if (d.RecentDuelFoes.Count > MaxRecentFoes)
                d.RecentDuelFoes.RemoveRange(MaxRecentFoes, d.RecentDuelFoes.Count - MaxRecentFoes);
        });
    }

    public static int BandPad
    {
        get => Get(d => Math.Clamp(d.BandPad, MinBandPad, MaxBandPad));
        set => Set(d => d.BandPad = Math.Clamp(value, MinBandPad, MaxBandPad));
    }

    /// <summary>도시 창이 열릴 때 줄 효과. 개발 창에서 고른다.</summary>
    public static CityOpenEffect CityOpenEffect
    {
        get => Get(d => Enum.TryParse<CityOpenEffect>(d.CityOpenEffect, out var effect)
            ? effect
            : Settings.CityOpenEffect.Expand);
        set => Set(d => d.CityOpenEffect = value.ToString());
    }

    /// <summary>지도 위에 좌표 상자를 겹쳐 보일지. 개발 창에서 켜고 끈다.</summary>
    /// <summary>배 속도 쪽지 — 모드 창에서 켜고 끈다. 바다에 있을 때만 뜬다.</summary>
    public static bool ShowShipSpeed
    {
        get => Get(d => d.ShowShipSpeed);
        set => Set(d => d.ShowShipSpeed = value);
    }

    /// <summary>항해 일수 — 항해 중 지도 왼쪽 위 동그라미에 출항한 지 며칠인지 띄운다. 모드 창에서 켜고 끈다.</summary>
    public static bool ShowSeaDays
    {
        get => Get(d => d.ShowSeaDays);
        set => Set(d => d.ShowSeaDays = value);
    }

    /// <summary>배 중심 — 지도가 배를 늘 한가운데에 두고 실시간으로 흐른다. 모드 창에서 켜고 끈다.</summary>
    public static bool ShipCentered
    {
        get => Get(d => d.ShipCentered);
        set => Set(d => d.ShipCentered = value);
    }

    /// <summary>발견물 수 상자 — 모드 창에서 켜고 끈다.</summary>
    public static bool ShowDiscoveryCount
    {
        get => Get(d => d.ShowDiscoveryCount);
        set
        {
            Set(d => d.ShowDiscoveryCount = value);
            ShowDiscoveryCountChanged?.Invoke();
        }
    }

    /// <summary><see cref="ShowDiscoveryCount"/> 가 바뀌었다 — 떠 있는 도시 창이 쪽지를 곧바로 내거나 걷는다.</summary>
    public static event Action? ShowDiscoveryCountChanged;

    public static bool ShowCoordOverlay
    {
        get => Get(d => d.ShowCoordOverlay);
        set => Set(d => d.ShowCoordOverlay = value);
    }

    /// <summary>
    /// 지도 위에 <b>만난 사람</b> 상자를 겹쳐 보일지. 개발 창의 "정보" 가 켜고 끈다.
    /// </summary>
    public static bool ShowPeopleOverlay
    {
        get => Get(d => d.ShowPeopleOverlay);
        set => Set(d => d.ShowPeopleOverlay = value);
    }


    /// <summary>
    /// 햄버거에 <b>발견물 지도</b> 줄을 낼지. 모드 창의 「발견물 지도」가 켜고 끈다.
    /// </summary>
    /// <remarks>
    /// 꺼 두면 줄도 안 뜨고 단축키(<see cref="MapKey"/>)도 안 먹는다 — 원본 항해지도는
    /// 표식을 안 찍으므로 이 지도는 앱이 얹은 것이다.
    /// </remarks>
    public static bool ShowDiscoveryMapMenu
    {
        get => Get(d => d.ShowDiscoveryMapMenu);
        set => Set(d => d.ShowDiscoveryMapMenu = value);
    }

    /// <summary>
    /// 지도를 <b>Ctrl+클릭</b>해 배를 그 자리에 놓을지. 모드 창의 「Ctrl+클릭 배 놓기」가 켜고 끈다.
    /// </summary>
    /// <remarks>
    /// 놀이에는 없는 길이다 — 끄면 Ctrl 을 짚고 찍어도 여느 클릭처럼 닻만 오르내린다.
    /// 잘못 눌러 배가 엉뚱한 데로 뛰는 것을 막고 싶을 때 끈다.
    /// </remarks>
    public static bool PlaceShipByCtrlClick
    {
        get => Get(d => d.PlaceShipByCtrlClick);
        set => Set(d => d.PlaceShipByCtrlClick = value);
    }

    /// <summary>
    /// 햄버거에 <b>인물 이동</b> 줄을 낼지. 모드 창의 「인물 이동」이 켜고 끈다.
    /// </summary>
    public static bool ShowPersonMoveMenu
    {
        get => Get(d => d.ShowPersonMoveMenu);
        set => Set(d => d.ShowPersonMoveMenu = value);
    }

    /// <summary>
    /// 햄버거에 <b>여급 수첩</b> 줄을 낼지. 모드 창의 「여급 수첩」이 켜고 끈다.
    /// </summary>
    public static bool ShowBarmaidBookMenu
    {
        get => Get(d => d.ShowBarmaidBookMenu);
        set => Set(d => d.ShowBarmaidBookMenu = value);
    }

    /// <summary>항해·뭍 이동 중 <b>미니맵</b>을 지도 오른쪽 아래에 띄울지. 개발 창의 「미니맵」이 켜고 끈다.</summary>
    public static bool ShowMiniMap
    {
        get => Get(d => d.ShowMiniMap);
        set => Set(d => d.ShowMiniMap = value);
    }

    public static double MiniMapOpacity
    {
        get => Math.Clamp(Get(d => d.MiniMapOpacity), 0.1, 1.0);
        set => Set(d => d.MiniMapOpacity = Math.Clamp(value, 0.1, 1.0));
    }

    /// <summary>
    /// 도시에 들어가면 <b>기능·언어</b> 쪽지를 도시 그림 왼쪽에 띄울지. 개발 창의 「기능·언어」가 켜고 끈다.
    /// 바꾸면 <see cref="ShowSkillOverlayChanged"/> 로 알려 떠 있는 도시 창이 곧바로 붙이거나 걷는다.
    /// </summary>
    public static bool ShowSkillOverlay
    {
        get => Get(d => d.ShowSkillOverlay);
        set
        {
            Set(d => d.ShowSkillOverlay = value);
            ShowSkillOverlayChanged?.Invoke();
        }
    }

    /// <summary><see cref="ShowSkillOverlay"/> 가 바뀌었다.</summary>
    public static event Action? ShowSkillOverlayChanged;

    /// <summary><see cref="ShowContractHintOverlay"/> 가 바뀌었다.</summary>
    public static event Action? ShowContractHintOverlayChanged;

    public static event Action? ShowFleetOverlayChanged;
    public static event Action? ShowHintOverlayChanged;

    public static bool ShowFleetOverlay
    {
        get => Get(d => d.ShowFleetOverlay);
        set
        {
            Set(d => d.ShowFleetOverlay = value);
            ShowFleetOverlayChanged?.Invoke();
        }
    }

    public static bool ShowHintOverlay
    {
        get => Get(d => d.ShowHintOverlay);
        set
        {
            Set(d => d.ShowHintOverlay = value);
            ShowHintOverlayChanged?.Invoke();
        }
    }

    /// <summary>
    /// 도시에 들어가면 현재 계약 힌트를 기능·언어 쪽지 위에 띄울지 — 모드 창에서 켜고 끈다.
    /// </summary>
    public static bool ShowContractHintOverlay
    {
        get => Get(d => d.ShowContractHintOverlay);
        set
        {
            Set(d => d.ShowContractHintOverlay = value);
            ShowContractHintOverlayChanged?.Invoke();
        }
    }

    /// <summary>
    /// 계약을 맺을 때 배가 있으면 후원자가 「배를 빌리겠습니까?」를 묻는지(<c>0x00410724</c>).
    /// 끄면 묻지 않고 안 빌린 것으로 한다 — 모드 창에서 켜고 끈다.
    /// </summary>
    public static bool AskLendShips
    {
        get => Get(d => d.AskLendShips);
        set => Set(d => d.AskLendShips = value);
    }

    /// <summary>해적 조우 확률 배수 — 모드 창의 고르기 칸 차례다. 0 이면 아예 안 붙는다.</summary>
    public static readonly double[] SeaRaidScales = [0, 0.25, 0.5, 1, 2, 4, 8];

    /// <summary>원본(x1)의 차례.</summary>
    public const int DefaultSeaRaidScale = 3;

    /// <summary>
    /// 바다 주사위(유럽 해적 rand(700) · 동쪽 이슬람 rand(400), <c>0x0048CABA</c>)에 거는 배수의 차례 — 모드 창에서 고른다.
    /// </summary>
    public static int SeaRaidScale
    {
        get => Math.Clamp(Get(d => d.SeaRaidScale), 0, SeaRaidScales.Length - 1);
        set => Set(d => d.SeaRaidScale = Math.Clamp(value, 0, SeaRaidScales.Length - 1));
    }

    /// <summary>
    /// 정보 창(양상·탐험·도시정보)과 상단 띠에 「생명력」(제독 HP) 줄을 낼지 — 모드 창에서 켜고 끈다.
    /// </summary>
    public static bool ShowVitalityInfo
    {
        get => Get(d => d.ShowVitalityInfo);
        set => Set(d => d.ShowVitalityInfo = value);
    }

    /// <summary>
    /// 아이템 창 개선 — 소지품일람 줄마다 <b>왼쪽에 아이템 그림</b>, 이름 밑에 갈래 · 효과 · 「장비중」을 낸다.
    /// 모드 창에서 켜고 끈다.
    /// </summary>
    public static bool ItemListPictures
    {
        get => Get(d => d.ItemListPictures);
        set => Set(d => d.ItemListPictures = value);
    }

    /// <summary>향상된 힌트 보기 — 왼쪽 목록에서 누르면 오른쪽에 곧바로 설명이 펴진다. 모드 창에서 켜고 끈다.</summary>
    public static bool HintBrowser
    {
        get => Get(d => d.HintBrowser);
        set => Set(d => d.HintBrowser = value);
    }

    /// <summary>중량 없음 — 보급품 · 교역품 무게로 막지 않는다(용량 · 통 수는 그대로). 모드 창에서 켜고 끈다.</summary>
    public static bool NoWeight
    {
        get => Get(d => d.NoWeight);
        set => Set(d => d.NoWeight = value);
    }

    /// <summary>리디바탕 글꼴 — 게임 비트맵 글꼴이 아닌 창 글씨(모드 창 · 향상된 힌트 보기 따위)를 리디바탕으로. 모드 창에서 켜고 끈다.</summary>
    public static bool RidiFont
    {
        get => Get(d => d.RidiFont);
        set => Set(d => d.RidiFont = value);
    }

    /// <summary>특별주문 — 조선소에서 가진 배와 옵션까지 똑같은 배를 산다. 모드 창에서 켜고 끈다.</summary>
    public static bool SpecialOrder
    {
        get => Get(d => d.SpecialOrder);
        set => Set(d => d.SpecialOrder = value);
    }

    /// <summary>향상된 아이템 이미지 — 몇몇 아이템을 덧붙인 그림으로 보인다(사자의 서 따위). 모드 창에서 켜고 끈다.</summary>
    public static bool EnhancedItemArt
    {
        get => Get(d => d.EnhancedItemArt);
        set => Set(d => d.EnhancedItemArt = value);
    }

    /// <summary>정보 제공 등급 — 창이 원본보다 얼마나 더 알려 주는지.</summary>
    /// <remarks>
    /// <code>
    ///   0 기본  원본만큼만
    ///   1 일반  게임 안에서 알 수 있는 값을 한 단계 더 보인다(힌트의 등급 · 자금 · 기한 따위)
    ///   2 상세  더 많이 — 아직 쓰는 곳이 없어 모드 창에서는 숨겨 둔다
    /// </code>
    /// 무엇을 어느 등급에 둘지는 차차 정한다. 보이는 쪽은 <see cref="Shows"/> 로 묻는다.
    /// </remarks>
    public static int InfoLevel
    {
        get => Math.Clamp(Get(d => d.InfoLevel), InfoBasic, InfoDetailed);
        set => Set(d => d.InfoLevel = Math.Clamp(value, InfoBasic, InfoDetailed));
    }

    /// <summary>편리한 인벤토리 — 소지품 정보 창에 「보관함」 · 「판매」 단추가 붙는다. 모드 창에서 켜고 끈다.</summary>
    public static bool HandyInventory
    {
        get => Get(d => d.HandyInventory);
        set => Set(d => d.HandyInventory = value);
    }

    /// <summary>
    /// 보고 시 재계약 끄기 — 모드 「부하 해고」를 켰을 때, 보고로 계약이 끝나도 부하마다 재계약을 묻지 않는다.
    /// 부하는 선금 없이 그대로 남는다(내보내려면 부하편성에서 해고한다). 기본은 켬.
    /// </summary>
    public static bool SkipRecontract
    {
        get => Get(d => d.SkipRecontract);
        set => Set(d => d.SkipRecontract = value);
    }

    /// <summary>부하 해고 — 부하편성 결정 오른쪽의 「해고」 단추. 재계약을 안 맺은 것처럼 떠난다.</summary>
    public static bool MateDismiss
    {
        get => Get(d => d.MateDismiss);
        set => Set(d => d.MateDismiss = value);
    }

    /// <summary>
    /// 뭍 자동이동(실험) — 뭍에 있을 때 발견물 지도에서 발견물 점을 오른쪽 단추로 누르면 그 자리까지 뭍길을 찾아 걸어간다.
    /// </summary>
    public static bool LandAutoWalk
    {
        get => Get(d => d.LandAutoWalk);
        set => Set(d => d.LandAutoWalk = value);
    }

    /// <summary>
    /// 자금 증가 기본 — 스폰서가 「이것으로 어떤가」 하면 승낙/교섭 고르기 없이 자금 증가(x1.3, 기간 절반)를 고른다.
    /// 스폰서 재력이 모자라거나 기간이 1년이면 쫓겨나지 않게 제안 그대로 승낙한다.
    /// </summary>
    public static bool AutoFundRaise
    {
        get => Get(d => d.AutoFundRaise);
        set => Set(d => d.AutoFundRaise = value);
    }

    /// <summary>수에즈 운하 — 지중해와 수에즈만 사이에 배가 지나갈 바닷길을 뚫는다(Engine.Sea.SuezCanal). 판을 다시 열 때 든다.</summary>
    public static bool SuezCanal
    {
        get => Get(d => d.SuezCanal);
        set => Set(d => d.SuezCanal = value);
    }

    /// <summary>게임 로드 창 글꼴 — 세이브 고르기 창(게임 로드 · CONTINUE)을 리디바탕 표로 띄운다(SaveListDialog).</summary>
    public static bool RidiLoadList
    {
        get => Get(d => d.RidiLoadList);
        set => Set(d => d.RidiLoadList = value);
    }

    /// <summary>개발 「일기토 불사」 — 일기토에서 내 부위 체력이 1 밑으로 안 내려간다. 판은 오른쪽 단추로 끝낸다.</summary>
    public static bool DuelImmortal
    {
        get => Get(d => d.DuelImmortal);
        set => Set(d => d.DuelImmortal = value);
    }

    /// <summary>개발 「반란 빈도」 — 바다 반란이 일어날 몫(%). 100 이 원본 그대로다.</summary>
    public static int MutinyRate
    {
        get => Math.Clamp(Get(d => d.MutinyRate), 0, 100);
        set => Set(d => d.MutinyRate = Math.Clamp(value, 0, 100));
    }

    /// <summary>모드 「캐릭터 작성 기억」 — 새 주인공을 지을 때 지난번에 적은 것(CharacterDraft)을 채워 준다.</summary>
    public static bool KeepCharacterDraft
    {
        get => Get(d => d.KeepCharacterDraft);
        set => Set(d => d.KeepCharacterDraft = value);
    }

    /// <summary>모드 실험 「녹화용 창 합치기」 — 도시 화면을 지도 창 안에 비춰 그려 「창 지정」 녹화에 들게 한다.</summary>
    public static bool CaptureMirror
    {
        get => Get(d => d.CaptureMirror);
        set => Set(d => d.CaptureMirror = value);
    }

    /// <summary>휠 확대 — 항해 · 뭍 지도를 마우스 휠로 키우고 줄인다. 끄면(기본) 휠이 아무 일도 안 한다.</summary>
    public static bool WheelZoom
    {
        get => Get(d => d.WheelZoom);
        set => Set(d => d.WheelZoom = value);
    }

    /// <summary>향상된 인물정보 목록 — 인물정보 목록을 리디바탕 글씨의 전용 창으로(초상화 · 기능 · 언어 · 설명).</summary>
    public static bool PersonInfoEnhanced
    {
        get => Get(d => d.PersonInfoEnhanced);
        set => Set(d => d.PersonInfoEnhanced = value);
    }

    /// <summary>모드 창 「권장」에서 마지막으로 고른 단계(0 오리지널 · 1 초보 · 2 중수 · 3 고수).</summary>
    public static int ModPreset
    {
        get => Get(d => d.ModPreset);
        set => Set(d => d.ModPreset = value);
    }

    /// <summary>향상된 후원자 정보 — 스폰서 일람 줄에 얼굴 · 발견물 취향 · 권력 · 친밀도.</summary>
    public static bool PatronListEnhanced
    {
        get => Get(d => d.PatronListEnhanced);
        set => Set(d => d.PatronListEnhanced = value);
    }

    /// <summary>인물정보 목록 — 누구를 볼지 고르는 창을 스폰서 일람처럼 초상화와 기능으로.</summary>
    public static bool PersonInfoList
    {
        get => Get(d => d.PersonInfoList);
        set => Set(d => d.PersonInfoList = value);
    }

    /// <summary>접근 함대 정보 — 「우호적으로 접근한다 · 습격한다」 고르기 위에 상대의 초상화 · 국적 · 직업 · 함대 규모.</summary>
    public static bool FleetCard
    {
        get => Get(d => d.FleetCard);
        set => Set(d => d.FleetCard = value);
    }

    /// <summary>작위 — 발견물을 보고해 공적을 쌓고 본국 왕궁에서 작위를 받는다(Engine.Town.Nobility). 모드 창에서 켜고 끈다.</summary>
    public static bool Nobility
    {
        get => Get(d => d.Nobility);
        set => Set(d => d.Nobility = value);
    }

    /// <summary>정보 제공 등급 값.</summary>
    public const int InfoBasic = 0, InfoNormal = 1, InfoDetailed = 2;

    /// <summary>그 등급 이상이면 참 — 「일반부터 보인다」는 Shows(InfoNormal) 이다.</summary>
    public static bool Shows(int level) => InfoLevel >= level;

    /// <summary>스핑크스 퀴즈 도우미 — 켜면 고르는 창의 정답 줄에 「← 답」을 단다. 모드 창에서 켜고 끈다.</summary>
    public static bool SphinxHelper
    {
        get => Get(d => d.SphinxHelper);
        set => Set(d => d.SphinxHelper = value);
    }

    /// <summary>
    /// 켤 때 로고(<c>LOGO.AVI</c>)와 오프닝(<c>OPEN.AVI</c>)을 틀지 — 모드 창에서 켜고 끈다.
    /// 원본은 늘 튼다(<c>0x00410AE3</c> · <c>0x00410B22</c>).
    /// </summary>
    public static bool PlayOpeningMovie
    {
        get => Get(d => d.PlayOpeningMovie);
        set => Set(d => d.PlayOpeningMovie = value);
    }

    /// <summary>
    /// 새 주인공 능력치 창에서 직업 단추를 누를 때마다 능력치를 다시 굴릴지 — 모드 창에서 켜고 끈다.
    /// 원본은 직업을 바꿔도 안 굴린다(<c>0x0045D8DA</c>).
    /// </summary>
    public static bool RerollOnJob
    {
        get => Get(d => d.RerollOnJob);
        set => Set(d => d.RerollOnJob = value);
    }

    /// <summary>
    /// 도시에 들어설 때마다 자동저장할지 — 모드 창에서 켜고 끈다.
    /// </summary>
    /// <remarks>
    /// 배로 입항하든 뭍으로 성문을 지나든 같은 자리를 거치므로(<c>EnterCity</c>) <b>항구가
    /// 없는 내륙 마을</b>에서도 적힌다. 적는 자리는 <see cref="Engine.GameSave.AutoDirectory"/> 의 칸들이라
    /// 손으로 적어 둔 것과 따로다. 첫 화면의 <b>CONTINUE</b> 가 그 파일을 연다.
    /// </remarks>
    public static bool AutoSaveOnPort
    {
        get => Get(d => d.AutoSaveOnPort);
        set => Set(d => d.AutoSaveOnPort = value);
    }

    /// <summary>
    /// 등록한 곡 갈아 끼우기를 쓸지 — 모드 창에서 켜고 끈다. 꺼도 등록은 그대로 남는다.
    /// </summary>
    public static bool CustomBgmEnabled
    {
        get => Get(d => d.CustomBgmEnabled);
        set => Set(d => d.CustomBgmEnabled = value);
    }

    /// <summary>지금 갈아 끼워 둔 곡 번호와 파일 — BGM 창이 목록을 그릴 때 쓴다.</summary>
    public static IReadOnlyDictionary<int, string> CustomBgmTracks => Get(d => d.CustomBgmTracks);

    /// <summary>그 곡 번호에 갈아 끼운 파일. 없으면 null.</summary>
    public static string? CustomBgmTrackPath(int track) =>
        Get(d => d.CustomBgmTracks.TryGetValue(track, out var path) ? path : null);

    /// <summary>그 곡 번호에 파일을 갈아 끼운다. <paramref name="path"/> 가 null 이면 등록을 지운다.</summary>
    public static void SetCustomBgmTrack(int track, string? path) => Set(d =>
    {
        if (string.IsNullOrEmpty(path)) d.CustomBgmTracks.Remove(track);
        else d.CustomBgmTracks[track] = path;
    });

    /// <summary>게임 상단 띠에 켜 둔 칸 이름들. 도시정보 창에서 켜고 끈다.</summary>
    public static IReadOnlyList<string>? BarCells
    {
        get => Get(d => d.BarCells);
        set => Set(d => d.BarCells = value == null ? null : [.. value]);
    }

    /// <summary>그 자리(0 바다 · 1 뭍 · 2 도시)의 상단 띠에 켜 둔 칸 이름들. 안 건드렸으면 null.</summary>
    public static IReadOnlyList<string>? BarCellsAt(int place) =>
        Get<IReadOnlyList<string>?>(d =>
            d.BarCellsByPlace is { } map && map.TryGetValue(place, out var cells) ? [.. cells] : null);

    /// <summary>그 자리의 상단 띠 칸을 적어 둔다.</summary>
    public static void SetBarCellsAt(int place, IEnumerable<string> cells)
    {
        List<string> copy = [.. cells];
        Set(d => (d.BarCellsByPlace ??= new())[place] = copy);
    }

    /// <summary>
    /// 고를 수 있는 게임 창 크기.
    /// </summary>
    /// <remarks>
    /// 원본은 <b>640x480 한 가지</b>다. 우리 지도는 훑어 보는 창이라 넓으면 넓은 만큼 더
    /// 보이므로 몇 가지를 열어 둔다. 원본 그림이 4:3 이라 4:3 을 위주로 두고, 지금까지
    /// 쓰던 1200x800 을 그대로 기본으로 남긴다. 폭이 0 이면 <b>전체 화면</b>이다.
    /// </remarks>
    public static readonly (string Name, int Width, int Height)[] Resolutions =
    [
        ("800 x 600", 800, 600),
        ("1024 x 768", 1024, 768),
        ("1200 x 800", 1200, 800),
        ("1280 x 960", 1280, 960),
        ("1600 x 1200", 1600, 1200),
        ("전체 화면", 0, 0),
    ];

    /// <summary>기본 크기 — 지금까지 쓰던 1200x800 이다.</summary>
    public const int DefaultResolution = 2;

    /// <summary>지금 고른 창 크기가 <see cref="Resolutions"/> 의 몇째인지.</summary>
    public static int Resolution
    {
        get => Get(d => Math.Clamp(d.Resolution, 0, Resolutions.Length - 1));
        set => Set(d => d.Resolution = Math.Clamp(value, 0, Resolutions.Length - 1));
    }

    /// <summary>지금 고른 창 크기. 폭이 0 이면 전체 화면이다.</summary>
    public static (string Name, int Width, int Height) WindowSize => Resolutions[Resolution];

    /// <summary>지도 위에 바람·해류 화살표를 얹을지. 함대 창 커맨드에서 켜고 끈다.</summary>
    public static bool ShowFlowArrows
    {
        get => Get(d => d.ShowFlowArrows);
        set => Set(d => d.ShowFlowArrows = value);
    }

    /// <summary>발견물 지도에 풍향 화살표를 얹을지. 그 창의 「풍향」 단추로 켜고 끈다.</summary>
    /// <summary>발견물 지도에 위도·경도 25도 격자를 깔지 — 지도 아래 「격자」 단추로 켜고 끈다.</summary>
    public static bool DiscoveryMapGrid
    {
        get => Get(d => d.DiscoveryMapGrid);
        set => Set(d => d.DiscoveryMapGrid = value);
    }

    /// <summary>발견물 지도에 아는 도시를 찍을지 — 지도 아래 「도시」 단추로 켜고 끈다. 기본은 끔.</summary>
    public static bool DiscoveryMapCities
    {
        get => Get(d => d.DiscoveryMapCities);
        set => Set(d => d.DiscoveryMapCities = value);
    }

    /// <summary>발견물 지도에 발견물 표식을 찍을지 — 지도 아래 「발견물」 단추로 켜고 끈다. 기본은 켬.</summary>
    public static bool DiscoveryMapSpots
    {
        get => !Get(d => d.DiscoveryMapHideSpots);
        set => Set(d => d.DiscoveryMapHideSpots = !value);
    }

    /// <summary>
    /// 자동 보급 — 배로 항구에 들면 물·식량 가운데 10일분이 안 되는 것을 10일분까지 산다(보급 창과 같은 값 · 용량·중량·소지금 안에서).
    /// 모드 창 「편의성」에서 켠다.
    /// </summary>
    public static bool AutoSupply
    {
        get => Get(d => d.AutoSupply);
        set => Set(d => d.AutoSupply = value);
    }

    /// <summary>자동 보급의 양 — 참이면 보급 창 「최대」처럼 실을 수 있는 데까지, 거짓이면 10일분까지.</summary>
    public static bool AutoSupplyMax
    {
        get => Get(d => d.AutoSupplyMax);
        set => Set(d => d.AutoSupplyMax = value);
    }

    /// <summary>선원 자동 모집 — 출항할 때 함대의 최저 승원 수까지 저절로 모집한다. 모드 창에서 켜고 끈다.</summary>
    public static bool AutoCrew
    {
        get => Get(d => d.AutoCrew);
        set => Set(d => d.AutoCrew = value);
    }

    /// <summary>
    /// 자동 도망 — 해적·이슬람 함대·추격대, 뭍의 무리와 마주치면 고르기 창 없이 「도망」을 고른다(짐승·독충은 그대로 묻는다).
    /// 도망 굴림은 원본 그대로라 실패하면 싸움이 이어진다. 모드 창 「편의성」에서 켠다.
    /// </summary>
    public static bool AutoFlee
    {
        get => Get(d => d.AutoFlee);
        set => Set(d => d.AutoFlee = value);
    }

    /// <summary>
    /// 릴리즈 노트를 마지막으로 보여 준 판. 켠 판이 이것과 다르면 업데이트된 것이라 노트를 띄운다
    /// (<see cref="Helpers.ReleaseNotes"/>).
    /// </summary>
    public static string NotesSeenVersion
    {
        get => Get(d => d.NotesSeenVersion);
        set => Set(d => d.NotesSeenVersion = value);
    }

    /// <summary>
    /// 놀이 통계(<see cref="Helpers.PlayStats"/>)를 보내도 되는지 — 처음 켤 때 한 번 묻는다. 아직 안 물었으면 null.
    /// </summary>
    public static bool? SendStats
    {
        get => Get(d => d.SendStats);
        set => Set(d => d.SendStats = value);
    }

    /// <summary>바다 입체 효과의 밝기 배수 — 모드 창 「실험」의 막대로 고른다.</summary>
    public static double SeaBrightness
    {
        get => Math.Clamp(Get(d => d.SeaBrightness), 0.6, 1.6);
        set => Set(d => d.SeaBrightness = Math.Clamp(value, 0.6, 1.6));
    }

    /// <summary>고해상도 바다의 해류 결·띠 짙기 — 모드 창 「실험」의 막대로 고른다.</summary>
    public static double SeaFlowAmount
    {
        get => Math.Clamp(Get(d => d.SeaFlowAmount), 0, 1);
        set => Set(d => d.SeaFlowAmount = Math.Clamp(value, 0, 1));
    }

    /// <summary>뭍 세부 질감 — 모드 창 「실험」에서 켜고 끈다.</summary>
    public static bool LandDetail
    {
        get => Get(d => d.LandDetail);
        set => Set(d => d.LandDetail = value);
    }

    /// <summary>도트 확대 필터 — 모드 창 「실험」에서 켜고 끈다.</summary>
    public static bool PixelFilter
    {
        get => Get(d => d.PixelFilter);
        set => Set(d => d.PixelFilter = value);
    }

    /// <summary>배 항적 — 모드 창 「실험」에서 켜고 끈다.</summary>
    public static bool ShipWake
    {
        get => Get(d => d.ShipWake);
        set => Set(d => d.ShipWake = value);
    }

    /// <summary>고해상도 바다 — 모드 창 「실험」에서 켜고 끈다.</summary>
    public static bool HiResSea
    {
        get => Get(d => d.HiResSea);
        set => Set(d => d.HiResSea = value);
    }

    /// <summary>부드러운 구름 — 모드 창 「실험」에서 켜고 끈다.</summary>
    public static bool SmoothClouds
    {
        get => Get(d => d.SmoothClouds);
        set => Set(d => d.SmoothClouds = value);
    }

    /// <summary>바다 입체 효과를 켤지 — 모드 창 「일반」에서 켜고 끈다. 원본에 없는 덧그림이다.</summary>
    public static bool SeaEffect
    {
        get => Get(d => d.SeaEffect);
        set => Set(d => d.SeaEffect = value);
    }

    /// <summary>미니맵에 풍향 화살표를 깔지 — 미니맵 오른쪽 위 단추로 켜고 끈다.</summary>
    public static bool MiniMapWind
    {
        get => Get(d => d.MiniMapWind);
        set => Set(d => d.MiniMapWind = value);
    }

    /// <summary>미니맵에 해류 화살표를 깔지 — 미니맵 오른쪽 위 단추로 켜고 끈다.</summary>
    public static bool MiniMapCurrent
    {
        get => Get(d => d.MiniMapCurrent);
        set => Set(d => d.MiniMapCurrent = value);
    }

    /// <summary>미니맵에 찾은 발견물 점을 찍을지 — 모드 창 「미니맵」 탭.</summary>
    public static bool MiniMapFound
    {
        get => Get(d => d.MiniMapFound);
        set => Set(d => d.MiniMapFound = value);
    }

    /// <summary>미니맵에 아직 못 찾은 발견물 점을 찍을지 — 모드 창 「미니맵」 탭.</summary>
    public static bool MiniMapYet
    {
        get => Get(d => d.MiniMapYet);
        set => Set(d => d.MiniMapYet = value);
    }

    /// <summary>미니맵 창 크기 배율 — 모드 창 「미니맵」 탭의 굴림대.</summary>
    public static double MiniMapScale
    {
        get => Math.Clamp(Get(d => d.MiniMapScale), 0.5, 2.5);
        set => Set(d => d.MiniMapScale = Math.Clamp(value, 0.5, 2.5));
    }

    /// <summary>마우스를 올렸거나 배가 밑에 들어갔을 때의 미니맵 불투명도 — 모드 창 「미니맵」 탭의 굴림대.</summary>
    public static double MiniMapHoverOpacity
    {
        get => Math.Clamp(Get(d => d.MiniMapHoverOpacity), 0.05, 1.0);
        set => Set(d => d.MiniMapHoverOpacity = Math.Clamp(value, 0.05, 1.0));
    }

    /// <summary>미니맵의 내 자리 점(파란 점) 크기 — 모드 창 「미니맵」 탭의 굴림대.</summary>
    public static double MiniMapShipSize
    {
        get => Math.Clamp(Get(d => d.MiniMapShipSize), 2.0, 12.0);
        set => Set(d => d.MiniMapShipSize = Math.Clamp(value, 2.0, 12.0));
    }

    /// <summary>미니맵 표식(발견물 · 도시 점) 크기 — 모드 창 「미니맵」 탭의 굴림대.</summary>
    public static double MiniMapMarkSize
    {
        get => Math.Clamp(Get(d => d.MiniMapMarkSize), 1.0, 5.0);
        set => Set(d => d.MiniMapMarkSize = Math.Clamp(value, 1.0, 5.0));
    }

    /// <summary>미니맵에 도시 점을 찍을지 — 모드 창 「미니맵」 탭.</summary>
    public static bool MiniMapCities
    {
        get => Get(d => d.MiniMapCities);
        set => Set(d => d.MiniMapCities = value);
    }

    public static bool DiscoveryMapWind
    {
        get => Get(d => d.DiscoveryMapWind);
        set => Set(d => d.DiscoveryMapWind = value);
    }

    /// <summary>발견물 지도에 해류 화살표를 얹을지. 그 창의 「해류」 단추로 켜고 끈다.</summary>
    public static bool DiscoveryMapCurrent
    {
        get => Get(d => d.DiscoveryMapCurrent);
        set => Set(d => d.DiscoveryMapCurrent = value);
    }

    /// <summary>
    /// 육상전 모의전 창이 지난번에 차렸던 짜임. 한 번도 안 차렸으면 null.
    /// </summary>
    /// <remarks>
    /// 「싸운다」를 누를 때 적히고, 창을 열 때 되편다. 그 창은 놀이가 아니라 시험 삼아
    /// 굴려 보는 자리라 앱을 껐다 켜도 남긴다.
    /// </remarks>
    public static LandSparData? LandSpar
    {
        get => Get(d => d.LandSpar);
        set => Set(d => d.LandSpar = value);
    }

    /// <summary>
    /// 부대배치 「전회」가 되펼 지난번 배치 — 여섯 칸, 고르는 넉 칸의 번호(−1 빈 자리).
    /// </summary>
    /// <remarks>
    /// 게임은 정적 자리(<c>0x0056EAB8</c>)에만 들고 있어 끄면 사라지는데, 다시 켤 때마다 새로
    /// 놓기가 번거로워 여기 적어 둔다.
    /// </remarks>
    public static int[]? LandDeployLast
    {
        get => Get(d => d.LandDeployLast is { } last ? (int[])last.Clone() : null);
        set => Set(d => d.LandDeployLast = value is null ? null : (int[])value.Clone());
    }
}
