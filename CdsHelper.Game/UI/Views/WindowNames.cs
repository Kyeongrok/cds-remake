using System.Windows;

namespace CdsHelper.Game.UI.Views;

/// <summary>
/// 창 이름 표 — <b>창 키</b>(클래스 이름, 쪽지는 갈래까지)로 사람이 읽을 이름을 찾는다.
/// </summary>
/// <remarks>
/// 창 제목(<see cref="Window.Title"/>)은 언어를 바꾸면 번역되고, 같은 클래스라도 띄울 때마다 다르다(메뉴 창의
/// 「여관」 · 「도서관」 따위). 그래서 창을 가리킬 때는 제목이 아니라 키를 쓴다 — 키는 코드에서 오므로 번역되지 않는다.
/// 창 주인 보기(개발, <see cref="WindowTreeWindow"/>)가 쓴다. 표에 없는 창은 키를 그대로 보인다.
/// </remarks>
public static class WindowNames
{
    /// <summary>그 창의 키 — 클래스 이름이고, 쪽지 창은 갈래를 덧붙인다(「FleetLabelWindow.Hint」).</summary>
    public static string KeyOf(Window window) => window switch
    {
        FleetLabelWindow note => $"{nameof(FleetLabelWindow)}.{note.Kind}",
        _ => window.GetType().Name,
    };

    /// <summary>그 창의 이름. 표에 없으면 null.</summary>
    public static string? NameOf(Window window) => Names.GetValueOrDefault(KeyOf(window));

    private static readonly Dictionary<string, string> Names = new()
    {
        // 큰 창 · 화면
        ["WorldMapWindow"] = "세계지도",
        ["CityPicView"] = "도시 화면",
        ["MenuWindow"] = "명령 창",
        ["BuildingPhotoWindow"] = "건물 사진",
        ["GateScene"] = "성문 장면",
        ["LandBattleScene"] = "육상전",
        ["SeaCombatDialog"] = "해전",
        ["DuelDialog"] = "일기토",
        ["WindowTreeWindow"] = "창 주인 보기(개발)",

        // 쪽지 · 띠
        ["FleetLabelWindow.Fleet"] = "함대 쪽지",
        ["FleetLabelWindow.Hint"] = "힌트 쪽지",
        ["FleetLabelWindow.Found"] = "발견물 수 쪽지",
        ["FleetLabelWindow.Speed"] = "배 속도 쪽지",
        ["SkillOverlayWindow"] = "기능 · 언어 쪽지",
        ["SeaDaysBadge"] = "항해 일수",

        // 알림 · 물음 · 고르기
        ["ConfirmDialog"] = "알림 · 물음",
        ["ChoiceDialog"] = "고르기",
        ["ListPickDialog"] = "목록 고르기",
        ["MapPointDialog"] = "맵 포인트",
        ["NumberPadDialog"] = "숫자판",
        ["CountDialog"] = "수 고르기",
        ["TextInputDialog"] = "글 넣기",
        ["NameListDialog"] = "이름 고르기",
        ["ShipNameDialog"] = "배 이름",
        ["ShipPickDialog"] = "배 고르기",
        ["HullSelectDialog"] = "선체 고르기",
        ["PersonListDialog"] = "사람 고르기",
        ["BuildingListDialog"] = "건물 고르기",
        ["SaveListDialog"] = "저장 목록",

        // 정보 판
        ["PlayerInfoDialog"] = "제독 정보",
        ["PersonInfoDialog"] = "인물 정보",
        ["CharacterSheetDialog"] = "인물 판",
        ["FleetInfoDialog"] = "함대 정보",
        ["FleetCardDialog"] = "함대 카드",
        ["ShipInfoDialog"] = "배 정보",
        ["SeaShipInfoDialog"] = "해상 배 정보",
        ["BelongingsDialog"] = "소지품 정보",
        ["ItemInfoDialog"] = "아이템 정보",
        ["GoodsInfoDialog"] = "교역품 정보",
        ["CityInfoDialog"] = "도시 정보",
        ["ContractDialog"] = "계약 정보",
        ["PatronInfoDialog"] = "후원자 정보",
        ["HintDetailDialog"] = "힌트 설명",
        ["HintBrowserDialog"] = "힌트 보기(향상)",
        ["DiscoveryDialog"] = "발견",
        ["DiscoveryMapDialog"] = "발견물 지도",
        ["ChronicleDialog"] = "연대기",
        ["EncyclopediaDialog"] = "백과사전",
        ["EncyclopediaPageDialog"] = "백과사전 쪽",
        ["SkillSheetDialog"] = "기능 판",
        ["SkillBoardDialog"] = "기능 일람",
        ["MateRosterDialog"] = "부하 명단",
        ["CargoView"] = "짐 보기",
        ["CargoRankDialog"] = "짐 순위",
        ["SeaBattleInfoDialog"] = "해전 정보",
        ["LandBattleInfoDialog"] = "육상전 정보",
        ["LandUnitInfoDialog"] = "육상 부대 정보",
        ["InfoDialog"] = "정보 판",

        // 시설 일
        ["TradePostDialog"] = "교역소",
        ["MarketBuyDialog"] = "시장 사기",
        ["MarketSellDialog"] = "시장 팔기",
        ["SupplyDialog"] = "보급",
        ["StorageDialog"] = "창고",
        ["CargoDropDialog"] = "짐 버리기",
        ["CrewShareDialog"] = "선원 편성",
        ["FormationDialog"] = "진형",
        ["LibraryDialog"] = "도서관",
        ["OpenBookDialog"] = "펼친 책",
        ["SkillLearnDialog"] = "기능 배우기",
        ["FortuneDialog"] = "점",
        ["PokerDialog"] = "포커",
        ["BarmaidBookDialog"] = "여급 수첩",
        ["LootDialog"] = "전리품",
        ["SeaChartDialog"] = "항해지도",
        ["AutoSailDialog"] = "자동항해",
        ["LandDeployDialog"] = "부대 배치",
        ["LandFormationDialog"] = "육상 진형",
        ["LandSparDialog"] = "육상 겨루기",
        ["DuelFoeDialog"] = "일기토 상대",
        ["QuestDialog"] = "이야기",
        ["CityCultureDialog"] = "도시 문화",

        // 퍼즐 · 미니게임
        ["CubePuzzleDialog"] = "입방체 퍼즐",
        ["CoinPuzzleDialog"] = "동전 퍼즐",
        ["GrailPuzzleDialog"] = "성배 퍼즐",
        ["TowerPuzzleDialog"] = "탑 퍼즐",
        ["MazePuzzleDialog"] = "미궁 퍼즐",
        ["FishingGameDialog"] = "낚시",

        // 연출
        ["EffectPopup"] = "효과 연출",
        ["EventAnimationPopup"] = "사건 연출",
        ["EventStillPopup"] = "사건 그림",
        ["Still"] = "그림",
        ["ShipStillWindow"] = "배 그림",
        ["GameOverDialog"] = "게임 오버",

        // 새 게임 · 설정
        ["CharacterMakeDialog"] = "주인공 만들기",
        ["AbilityMakeDialog"] = "능력 정하기",
        ["SkillMakeDialog"] = "기능 정하기",
        ["SettingsDialog"] = "설정",
        ["ModDialog"] = "모드",
        ["ShortcutDialog"] = "단축키",
        ["CustomBgmDialog"] = "BGM 갈아 끼우기",
        ["PortraitBookDialog"] = "초상화 책",
        ["PortraitAddDialog"] = "초상화 더하기",

        // 개발 · 편집
        ["DevDialog"] = "개발",
        ["GameDataDialog"] = "게임 데이터",
        ["MenuDesignerDialog"] = "메뉴 디자이너",
        ["MotionMakerDialog"] = "동작 만들기",
        ["MonsterMotionDialog"] = "괴물 관리",
        ["DisevEditorDialog"] = "이벤트 편집",
        ["DiscoveryEditDialog"] = "발견물 편집",
        ["BookEditDialog"] = "책 편집",
        ["NationEditDialog"] = "나라 편집",
        ["PersonEditDialog"] = "인물 편집",
        ["PersonMoveDialog"] = "인물 옮기기",
        ["PassiveEditDialog"] = "패시브 편집",
        ["RuseEditDialog"] = "묘책 편집",
        ["ScenarioEditDialog"] = "시나리오 편집",
        ["TavernHintEditDialog"] = "술집 힌트 편집",
        ["TavernRumorEditDialog"] = "술집 소문 편집",
        ["VoyagerEditDialog"] = "역사 항해자 편집",
        ["FigureheadEditDialog"] = "선수상 편집",
        ["CityCultureEditDialog"] = "도시 문화 편집",
        ["CitySpriteDialog"] = "도시 그림 조각",
    };
}
