using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using CdsHelper.Game.Local.Settings;

namespace CdsHelper.Game.UI.Views;

/// <summary>
/// 모드 창 — <b>원본에 없는 편의 기능</b>을 켜고 끈다.
/// </summary>
/// <remarks>
/// 개발 창에 섞여 있던 것 가운데 <b>놀 때 쓰는 것</b>만 따로 뽑아 왔다. 개발 창은 값을
/// 손으로 밀어 넣어 시험하는 데고, 여기는 판을 그대로 두고 보기를 거드는 데다.
///
/// 줄은 <b>왼쪽</b>에 늘어놓고, 커서를 올리거나 고른 줄의 설명이 <b>오른쪽</b>에 뜬다 —
/// 처음 보면 이름만으로는 무엇인지 알 수 없어 풍선말 대신 붙박이 설명 칸을 두었다.
/// </remarks>
public sealed class ModDialog : GameWindow
{
    /// <summary>모드 창이 만지는 것들.</summary>
    public sealed class Options
    {
        /// <summary>제독 컨디션(HP) 상자.</summary>

        /// <summary>미니맵 — 발견물 지도를 작게 잘라 배를 따라간다.</summary>
        public Func<bool> DiscoveryCountOn { get; init; } = () => false;
        public Action<bool> SetDiscoveryCount { get; init; } = _ => { };
        public Func<bool> MiniMapOn { get; init; } = () => false;
        public Action<bool> SetMiniMap { get; init; } = _ => { };
        public Func<double> MiniMapOpacity { get; init; } = () => 0.75;
        public Action<double> SetMiniMapOpacity { get; init; } = _ => { };

        /// <summary>바람·해류 화살표 — 원본에 없는 덧그림이다.</summary>
        public Func<bool> ArrowsOn { get; init; } = () => false;
        public Action<bool> SetArrows { get; init; } = _ => { };

        /// <summary>바다 입체 효과 — 원본에 없는 덧그림이다.</summary>
        public Func<bool> SeaOn { get; init; } = () => false;
        public Action<bool> SetSea { get; init; } = _ => { };
        public Func<double> SeaBrightness { get; init; } = () => 1.0;
        public Action<double> SetSeaBrightness { get; init; } = _ => { };

        /// <summary>고해상도 바다.</summary>
        public Func<bool> HiResSeaOn { get; init; } = () => false;
        public Action<bool> SetHiResSea { get; init; } = _ => { };
        public Func<double> SeaFlowAmount { get; init; } = () => 0.5;
        public Action<double> SetSeaFlowAmount { get; init; } = _ => { };

        /// <summary>뭍 세부 질감.</summary>
        public Func<bool> LandDetailOn { get; init; } = () => false;
        public Action<bool> SetLandDetail { get; init; } = _ => { };

        /// <summary>도트 확대 필터.</summary>
        public Func<bool> PixelFilterOn { get; init; } = () => false;
        public Action<bool> SetPixelFilter { get; init; } = _ => { };

        /// <summary>배 항적 — 항적·그림자·출렁임.</summary>
        public Func<bool> ShipWakeOn { get; init; } = () => false;
        public Action<bool> SetShipWake { get; init; } = _ => { };

        /// <summary>부드러운 구름.</summary>
        public Func<bool> SmoothCloudsOn { get; init; } = () => true;
        public Action<bool> SetSmoothClouds { get; init; } = _ => { };
    }

    /// <summary>줄 목록과 설명 칸의 너비.</summary>
    private const double ListWidth = 250, TipWidth = 330;

    /// <summary>아무 줄에도 커서가 없을 때 설명 칸에 적는 글.</summary>
    private const string Greeting =
        "원본에 없는 기능을 켜고 끄는 창입니다.\n\n「편의성」은 손을 덜어 주는 것, 「정보」는 화면에 무언가를 더 보여 주는 것, 「일반」은 놀이 규칙·소리·진행을 바꾸는 것, 「실험」은 아직 다듬는 중인 것입니다.\n\n왼쪽 줄에 커서를 올리면 여기에 설명이 뜹니다.";

    /// <summary>오른쪽 설명 칸의 이름 줄.</summary>
    private readonly TextBlock _tipName = new()
    {
        Foreground = GameUi.Text,
        FontWeight = FontWeights.Bold,
        FontSize = 15,
        Margin = new Thickness(0, 0, 0, 6),
        TextWrapping = TextWrapping.Wrap,
    };

    /// <summary>오른쪽 설명 칸의 본문.</summary>
    private readonly TextBlock _tipText = new()
    {
        Text = Greeting,
        Foreground = GameUi.Text,
        FontSize = 13,
        LineHeight = 20,
        TextWrapping = TextWrapping.Wrap,
    };

    private ModDialog(Options options)
    {
        Title = "모드";
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.WidthAndHeight;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        Background = GameUi.Back;

        // 줄이 길어 두 탭으로 가른다 — 「편의성」은 손을 덜어 주는 것, 「정보」는 화면에 무언가를 더 보여 주는 것, 「일반」은 놀이 규칙·소리·진행을 바꾸는 것.
        var rows = new StackPanel { Width = ListWidth, Margin = new Thickness(12, 10, 8, 4) };
        var general = new StackPanel { Width = ListWidth, Margin = new Thickness(12, 10, 8, 4) };
        // 「정보」 — 화면에 쪽지·덧그림·창으로 무언가를 더 보여 주는 것만 모은다.
        var info = new StackPanel { Width = ListWidth, Margin = new Thickness(12, 10, 8, 4) };
        // 「UI」 — 창 모양 · 글꼴 · 차림표 줄처럼 화면 꾸밈을 바꾸는 것.
        var ui = new StackPanel { Width = ListWidth, Margin = new Thickness(12, 10, 8, 4) };
        // 「실험」 — 아직 다듬는 중인 덧그림. 모양이 바뀔 수 있다.
        var lab = new StackPanel { Width = ListWidth, Margin = new Thickness(12, 10, 8, 4) };


        // 미니맵 — D 로 여는 발견물 지도를 항해·뭍 이동 중에 오른쪽 아래에 작게 띄운다.
        // 배 속도 — 바다에서 걸음마다 잰 함대 속도를 쪽지로 띄운다.
        info.Children.Add(Toggle("배 속도", GameSettings.ShowShipSpeed, on => GameSettings.ShowShipSpeed = on,
            "원본에 없는 것입니다 — 바다에 있을 때 함대 속도(바람·돛·선원으로 걸음마다 잰 값)를 지도 위 쪽지로 띄웁니다. 뭍·도시에서는 안 뜹니다. 끌어 옮길 수 있습니다."));

        // 접근 함대 정보 — 다가간 함대의 초상화 · 국적 · 직업 · 함대 규모.
        info.Children.Add(Toggle("접근 함대 정보", GameSettings.FleetCard, on => GameSettings.FleetCard = on,
            "원본에 없는 것입니다 — 바다에서 다른 함대에 다가가 「우호적으로 접근한다 · 습격한다 · 떠난다」를 고를 때,"
            + " 그 위에 상대의 초상화와 국적 · 직업 · 함대 규모(척수) · 무력을 띄웁니다."));

        // 항해 일수 — 출항한 지 며칠인지 지도 왼쪽 위 동그라미에.
        info.Children.Add(Toggle("항해 일수", GameSettings.ShowSeaDays, on => GameSettings.ShowSeaDays = on,
            "원본에 없는 것입니다 — 항해 중 지도 왼쪽 위에 동그라미를 띄우고 그 안에 출항한 지 며칠이 됐는지 적습니다."
            + " 항구에 들면 0 으로 돌아갑니다. 뭍·도시에서는 안 뜹니다."));

        // 발견물 수 — 찾은 발견물이 전체 몇 개 가운데 몇 개인지 지도 왼쪽 아래에 띄운다.
        info.Children.Add(Toggle("발견물 수", options.DiscoveryCountOn(), options.SetDiscoveryCount,
            "원본에 없는 것입니다 — 도시에 들어가면 지금까지 찾은 발견물이 전체 몇 개 가운데 몇 개인지 도시 창 곁에 띄웁니다. 끌어 옮길 수 있습니다."));

        info.Children.Add(MiniMapControls(options));

        // 바람·해류 화살표 — 원본은 물결로만 흐름을 보인다. 개발 창에 있던 것을 여기로 옮겼다.
        info.Children.Add(Toggle("바람·해류 화살표", options.ArrowsOn(), options.SetArrows,
            "원본에 없는 덧그림입니다 — 바람과 해류의 방위를 지도 위에 화살표로 얹습니다."));

        // 발견물 지도 — 햄버거 줄과 단축키를 함께 여닫는다. 원본 항해지도는 표식을 안 찍는다.
        ui.Children.Add(Toggle("발견물 지도", GameSettings.ShowDiscoveryMapMenu,
            on => GameSettings.ShowDiscoveryMapMenu = on,
            "햄버거에 「발견물 지도」 줄을 냅니다. 어디에 무엇이 있는지 표식으로 찍어 보여 줍니다."
            + " 끄면 줄도 단축키도 안 먹습니다."));

        // 여급 수첩 — 낯을 튼 여급과 궁합을 모아 본다. 원본에는 없는 창이다.
        ui.Children.Add(Toggle("여급 수첩", GameSettings.ShowBarmaidBookMenu,
            on => GameSettings.ShowBarmaidBookMenu = on,
            "제목 줄 왼쪽 위 도시락 단추(점 아홉, 햄버거 왼쪽)에 「여급 수첩」을 냅니다. 낯을 튼 여급의 친밀도와 궁합을 모아 봅니다."
            + " 여급 수첩 · 인물 이동을 다 끄면 도시락 단추도 사라집니다."));

        // 인물 이동 — 누가 어느 도시로 가고 있는지 늘어놓는 창.
        ui.Children.Add(Toggle("인물 이동", GameSettings.ShowPersonMoveMenu,
            on => GameSettings.ShowPersonMoveMenu = on,
            "제목 줄 왼쪽 위 도시락 단추(점 아홉, 햄버거 왼쪽)에 「인물 이동」을 냅니다. 인물이 어느 도시로 가고 있는지 늘어놓습니다."));

        // Ctrl+클릭 배 놓기 — 지도를 찍은 자리로 배가 뛴다. 켠 채로 시작한다.
        rows.Children.Add(Toggle("Ctrl+클릭 배 놓기", GameSettings.PlaceShipByCtrlClick,
            on => GameSettings.PlaceShipByCtrlClick = on,
            "Ctrl 을 짚고 지도를 찍으면 배를 그 자리에 놓습니다. 끄면 여느 클릭처럼 닻만 오르내립니다."));

        // 계약 힌트 — 기능·언어 쪽지 위에 현재 계약의 힌트 이름을 띄운다.
        info.Children.Add(Toggle("현재 계약 힌트", GameSettings.ShowContractHintOverlay,
            on => GameSettings.ShowContractHintOverlay = on,
            "도시에 들어가면 현재 계약을 맺은 힌트 이름을 기능·언어 쪽지 위에 띄웁니다."));

        info.Children.Add(Toggle("현재 함대 선박 이름", GameSettings.ShowFleetOverlay,
            on => GameSettings.ShowFleetOverlay = on,
            "도시에 들어가면 현재 함대의 선박 이름과 선체를 도시 창 옆에 띄웁니다."));

        info.Children.Add(Toggle("현재 힌트 목록", GameSettings.ShowHintOverlay,
            on => GameSettings.ShowHintOverlay = on,
            "현재 남아 있는 힌트를 최대 10개까지 함대 선박 이름 아래에 띄웁니다."));

        // 기능·언어 — 켜 두면 도시에 들어갈 때 도시 그림 왼쪽에 쪽지로 뜬다.
        info.Children.Add(Toggle("기능·언어", GameSettings.ShowSkillOverlay,
            on => GameSettings.ShowSkillOverlay = on,
            "도시에 들어가면 제독과 부하 넷의 기능·언어를 도시 그림 왼쪽에 띄웁니다. 끌어 옮기면 그 자리를 기억합니다."));

        // 자동 도망 — 조우의 고르기 창을 건너뛰고 「도망」을 고른다.
        rows.Children.Add(Toggle("자동 도망", GameSettings.AutoFlee,
            on => GameSettings.AutoFlee = on,
            "원본에 없는 것입니다 — 바다에서 해적·이슬람 함대·추격대와 부딪치거나 뭍에서 적 무리를 만나면"
            + " 고르기 창 없이 「도망」을 고릅니다. 짐승·독충은 그대로 묻습니다."
            + " 도망 성공 여부는 원본 그대로 굴리므로 실패하면 싸움이 이어집니다."));

        // 자동 보급 — 출항할 때 물·식량을 10일분까지, 또는 실을 수 있는 데까지.
        rows.Children.Add(AutoSupplyControls());

        // 선원 자동 모집 — 출항할 때 최저 승원 수까지.
        rows.Children.Add(Toggle("선원 자동 모집", GameSettings.AutoCrew,
            on => GameSettings.AutoCrew = on,
            "원본에 없는 것입니다 — 항구에서 「출항」을 누를 때 선원이 함대의 최저 승원 수보다 적으면 그만큼 저절로 모집합니다."
            + " 값은 선원 모집과 같고(명성이 높을수록 쌉니다), 소지금이 모자라면 낼 수 있는 만큼만 모집합니다."
            + " 정원은 넘기지 않습니다. 모집한 것은 아래 띠로 알립니다."));

        // 배 빌림 묻기 — 원본은 배가 있으면 계약 자리에서 늘 묻는다(0x00410724).
        rows.Children.Add(Toggle("배 빌림 묻기", GameSettings.AskLendShips,
            on => GameSettings.AskLendShips = on,
            "계약을 맺을 때 내 배가 한 척이라도 있으면 후원자가 「배를 빌리겠습니까?」를 묻습니다(원본 그대로)."
            + " 끄면 묻지 않고 안 빌린 것으로 넘어갑니다 — 배를 이미 갖춘 판에서 물음이 성가실 때 씁니다."));

        // 생명력 — 원본 탐험정보에 없는 줄이다.
        info.Children.Add(Toggle("생명력 정보", GameSettings.ShowVitalityInfo,
            on => GameSettings.ShowVitalityInfo = on,
            "원본에 없는 것입니다 — 양상·탐험·도시정보 창에 「생명력」(제독 HP) 줄을 내고, 켜면 상단 띠에도 세울 수 있습니다."
            + " 끄면 정보 창에서 빠지고 띠에서도 걷힙니다."));

        // 리디바탕 글꼴 — 윈도 글꼴로 찍는 창 글씨를 리디바탕으로.
        ui.Children.Add(Toggle("리디바탕 글꼴", GameSettings.RidiFont,
            on => { GameSettings.RidiFont = on; UiFont.Refresh(); },
            "원본에 없는 것입니다 — 게임 비트맵 글꼴이 아니라 윈도 글꼴로 찍는 글씨(이 모드 창, 향상된 힌트 보기, 교역소 숫자 따위)를"
            + " 리디바탕(명조)으로 바꿉니다. 게임 비트맵 글씨는 그대로입니다. 켜고 끄면 떠 있는 창에도 곧바로 듭니다."
            + " 리디바탕은 리디주식회사가 SIL OFL 1.1 로 낸 글꼴입니다."));

        // 향상된 힌트 보기 — 취득 힌트 일람을 모드 창처럼 목록 · 설명 두 칸으로.
        ui.Children.Add(Toggle("향상된 힌트 보기", GameSettings.HintBrowser,
            on => GameSettings.HintBrowser = on,
            "원본과 다릅니다 — 취득 힌트 일람을 왼쪽에 목록, 오른쪽에 설명으로 나란히 띄웁니다."
            + " 줄을 누르면(↑↓ 글쇠도) 곧바로 그 힌트의 이야기가 오른쪽에 나옵니다(정보 등급 「일반」이면 등급 · 자금 · 기한도)."
            + " 끄면 원본처럼 고르고 결정을 눌러야 파란 판이 뜹니다."));

        // 정보 제공 등급 — 기본(원본) · 일반 · 상세.
        info.Children.Add(Select("정보 등급", ["기본 (원본)", "일반", "상세"],
            GameSettings.InfoLevel,
            i => GameSettings.InfoLevel = i,
            "창이 원본보다 얼마나 더 알려 주는지 고릅니다. 「기본」은 원본만큼만 보입니다."
            + " 「일반」은 게임 안에서 알 수 있는 값을 한 단계 더 보입니다 — 향상된 힌트 보기의 등급 · 자금 · 기한이 이것입니다."
            + " 「상세」는 원본이 감춰 둔 값까지 보입니다 — 도서관 책등 이름표에 읽는 데 필요한 언어 · 기능이 붙습니다."));

        // 향상된 아이템 이미지 — 덧붙인 그림으로 보인다.
        ui.Children.Add(Toggle("향상된 아이템 이미지", GameSettings.EnhancedItemArt,
            on => GameSettings.EnhancedItemArt = on,
            "원본과 다릅니다 — 몇몇 아이템을 새로 그린 그림으로 보입니다(지금은 사자의 서). 끄면 원본 그림입니다."
            + " 원본은 사자의 서가 지중해의 유혹어와 같은 책 그림을 나눠 씁니다."));

        // 인물정보 목록 — 누구를 볼지 고르는 창을 스폰서 일람처럼(게임 글꼴).
        ui.Children.Add(Toggle("인물정보 목록", GameSettings.PersonInfoList,
            on => GameSettings.PersonInfoList = on,
            "원본과 다릅니다 — 「인물정보」에서 누구를 볼지 고르는 창을 스폰서 일람처럼 띄웁니다(게임 글꼴)."
            + " 줄마다 초상화, 이름 밑에 지닌 기능 · 언어(그 자리에서 안 쓰이는 것은 흐리게), 오른쪽에 자리가 나오고,"
            + " 줄을 고르면 오른쪽에 맡은 기능의 효과가 펼쳐집니다. 끄면 원본처럼 자리 이름만 늘어놓습니다."));

        // 향상된 인물정보 목록 — 같은 목록을 리디바탕 글씨의 전용 창으로.
        ui.Children.Add(Toggle("향상된 인물정보 목록", GameSettings.PersonInfoEnhanced,
            on => GameSettings.PersonInfoEnhanced = on,
            "원본과 다릅니다 — 「인물정보」 목록을 리디바탕 글씨의 전용 창으로 띄웁니다. 내용은 「인물정보 목록」과 같고,"
            + " 기능 · 언어가 길어도 폭에 맞춰 접혀 읽기 쉽습니다. 결정(또는 두 번 누르기)으로 인물정보 판을 엽니다."
            + " 켜면 「인물정보 목록」을 켜지 않아도 이 창이 뜹니다."));

        // 아이템 창 — 소지품일람 줄마다 그림과 효과.
        ui.Children.Add(Toggle("아이템 창 개선", GameSettings.ItemListPictures,
            on => GameSettings.ItemListPictures = on,
            "원본과 다릅니다 — 소지품일람의 줄마다 스폰서 일람처럼 왼쪽에 아이템 그림을 내고, 이름 밑에"
            + " 갈래와 효과(무기 · 방어구), 「장비중」을 적습니다. 끄면 원본처럼 이름만 늘어놓습니다."));

        // 자금 증가 기본 — 스폰서 제안에서 고르기 없이 자금 증가.
        rows.Children.Add(Toggle("자금 증가 기본", GameSettings.AutoFundRaise, on => GameSettings.AutoFundRaise = on,
            "원본에 없는 것입니다 — 스폰서가 자금과 기간을 내놓고 「어떤가」 하면 「승낙한다 / 교섭한다」를 묻지 않고"
            + " 곧바로 자금 증가(자금 x1.3, 기간 절반)를 고릅니다. 스폰서 재력이 모자라거나 기간이 1년이라 원본대로면"
            + " 「탐욕스러운 놈!」 하고 쫓겨날 판이면, 대신 제안 그대로 승낙합니다."));

        // 휠 확대 — 지도를 마우스 휠로 키우고 줄인다. 원본에 없어 꺼 둔다.
        rows.Children.Add(Toggle("휠 확대", GameSettings.WheelZoom, on => GameSettings.WheelZoom = on,
            "원본에 없는 것입니다 — 항해 · 뭍 지도에서 마우스 휠을 굴리면 커서 자리를 두고 지도를 키우고 줄입니다."
            + " 끄면(기본) 휠을 굴려도 지도가 그대로입니다. 발견물 지도 창의 휠 확대는 이와 상관없이 늘 됩니다."));

        // 중량 없음 — 보급품 · 교역품 무게로 막지 않는다.
        rows.Children.Add(Toggle("중량 없음", GameSettings.NoWeight, on => GameSettings.NoWeight = on,
            "원본과 다릅니다 — 보급품(물 · 식량 · 자재 · 탄약)과 교역품의 무게를 따지지 않습니다."
            + " 교역소 · 보급 · 전리품 · 약탈에서 「중량을 초과하고 있습니다」로 막히지 않고, 짐 덜기 창도 무게 때문에는 안 뜹니다."
            + " 용량(통 수)과 대포 수 한도는 그대로입니다."));

        // 편리한 인벤토리 — 소지품 창에서 바로 보관 · 판매.
        rows.Children.Add(Toggle("편리한 인벤토리", GameSettings.HandyInventory, on => GameSettings.HandyInventory = on,
            "원본에 없는 것입니다 — 소지품 정보 창에 「보관함」 · 「판매」 단추가 붙습니다."
            + " 고른 아이템을 자택에 가지 않고 바로 자택 보관함으로 보내거나, 시장에 가지 않고 바로 팝니다."
            + " 판 값은 시장 매각과 같습니다(아이템 매각가 x 지금 도시 시세, 바다 위면 시세 100)."));

        // 스핑크스 퀴즈 도우미 — 개발도구에 있던 계산기를 놀이 안으로 옮겼다.
        rows.Children.Add(Toggle("스핑크스 퀴즈 도우미", GameSettings.SphinxHelper,
            on => GameSettings.SphinxHelper = on,
            "원본에 없는 것입니다 — 스핑크스 퀴즈에서 고르는 창의 정답 줄에 「← 답」을 붙입니다."
            + " 셈 문제의 답은 늘 다리 넷 달린 괴물의 수입니다."));

        // 자동저장 — 원본에 없다. 손으로 적는 자리(SAVEDATA.CDS)는 안 건드리고 따로 적는다.
        rows.Children.Add(Toggle("도시 자동저장", GameSettings.AutoSaveOnPort,
            on => GameSettings.AutoSaveOnPort = on,
            "원본에 없는 것입니다 — 도시에 들어설 때마다 자동저장 파일(AUTOSAVE.CDS)에 적습니다."
            + " 배로 입항하든 뭍으로 성문을 지나든 마찬가지라, 항구가 없는 내륙 마을에서도 적힙니다."
            + " 손으로 적어 둔 세이브(SAVEDATA.CDS)는 건드리지 않습니다."
            + " 첫 화면의 「CONTINUE」가 이 파일을 엽니다."));

        // 커스텀 BGM — 등록한 곡으로 갈아 끼운다. 곡 등록 창은 햄버거에 있던 것을 이 줄 밑 단추로 옮겼다.
        general.Children.Add(CustomBgmControls());

        // 바다 입체 효과 — 지도 셰이더가 바다 칸에 물결 굴곡·햇빛·깊이·물보라를 얹는다.
        // 배 중심 — 가장자리에서 화면을 넘기지 않고 배를 늘 한가운데에 둔다. 아직 다듬는 중이라 실험에 둔다.
        // 뭍 자동이동 — 발견물 지도에서 발견물 점을 오른쪽 단추로 누르면 그 자리까지 걸어간다.
        lab.Children.Add(Toggle("뭍 자동이동", GameSettings.LandAutoWalk, on => GameSettings.LandAutoWalk = on,
            "원본에 없는 것입니다 — 뭍에 올라 있을 때 발견물 지도를 열고 발견물 점에 마우스를 올리면 점에 흰 테두리가 생깁니다."
            + " 그 점을 오른쪽 단추로 누르면 지도가 닫히고 그 자리까지 뭍길을 찾아 저절로 걸어갑니다(바다 자동항해와 같은 마디 따라가기)."
            + " 길을 못 찾거나 바다에 있으면 아래 줄에 알립니다. 사건이 나면 멈춥니다."));

        lab.Children.Add(Toggle("배 중심", GameSettings.ShipCentered, on => GameSettings.ShipCentered = on,
            "원본에 없는 것입니다 — 원본은 배가 화면 가장자리에 닿으면 화면을 한 번에 넘깁니다."
            + " 켜면 배를 늘 화면 한가운데에 두고 지도가 배를 따라 실시간으로 흐릅니다. 뭍에서 말로 다닐 때도 같습니다."));
        lab.Children.Add(SeaControls(options));
        lab.Children.Add(HiResSeaControls(options));
        lab.Children.Add(Toggle("뭍 세부 질감", options.LandDetailOn(), options.SetLandDetail,
            "지도를 키웠을 때 원본 도트는 그대로 두고, 지형마다 화면 해상도의 잔무늬를 얇게 얹습니다 —"
            + " 사막은 모래 결, 산은 바위 결, 숲은 잎 덩이, 평지는 풀 결. 도시·발견물 그림과 물은 건드리지 않습니다."
            + " 키울수록 짙어집니다."));
        lab.Children.Add(Toggle("도트 확대 필터", options.PixelFilterOn(), options.SetPixelFilter,
            "지도를 키웠을 때(칸이 화면 네 점보다 클 때) 원본 도트의 대각선 계단을 사선으로 깎아 매끈하게 그립니다."
            + " 바다·뭍·해안·도시 그림과 내 배 그림 모두에 듭니다. 바둑판 잔무늬는 그대로 둡니다."));
        lab.Children.Add(Toggle("배 항적", options.ShipWakeOn(), options.SetShipWake,
            "원본에 없는 덧그림입니다 — 바다에 뜬 내 배 뒤로 지나온 길을 따라 물거품 항적이 벌어지며 스러지고,"
            + " 배 밑에 옅은 그림자가 깔리고 배가 살짝 출렁입니다. 빠를수록 항적이 길고, 서면 사라집니다."
            + " 켜 둔 동안은 화면을 계속 다시 그립니다. 남의 배와 뭍의 말에는 안 듭니다."));
        lab.Children.Add(Toggle("부드러운 구름", options.SmoothCloudsOn(), options.SetSmoothClouds,
            "원본 구름은 한 점 걸러 찍은 바둑판 무늬로 반투명을 흉내 내서, 지도를 키우면 격자가 그대로 커집니다."
            + " 켜면 그 무늬를 참 반투명으로 풀어 매끈하게 늘려 그립니다. 비치는 정도는 원본과 같습니다."));

        // 작위 — 공적 · 작위 · 혜택.
        lab.Children.Add(Toggle("작위", GameSettings.Nobility, on => GameSettings.Nobility = on,
            "원본에 없는 것입니다 — 발견물을 보고할 때마다 공적(힌트 등급 x 10)이 쌓이고, 교역소 「투자」로도 1000닢마다 공적 1 이 쌓입니다. 공적이 차면 술집에서"
            + " 「본국 왕궁에서 찾는다」는 말을 듣습니다. 본국 수도 왕궁에 들면 국왕이 작위를 내립니다(기사 · 남작 · 자작 · 백작 · 후작 · 공작 · 대공)."
            + " 작위마다 패시브 혜택이 붙고 쌓입니다 — "
            + string.Join(", ", Engine.Town.Passives.All.Where(p => p.Rank > 0).Select(p => $"{Engine.Town.Nobility.Names[p.Rank]}: {p.Name}")) + "."
            + " 패시브는 도구 앱 「요소 → 패시브」에서 만들고 고칩니다."
            + " 인물정보에 작위와 공적, 「작위」 단추가 나옵니다."));

        // 특별주문 — 조선소에서 가진 배와 옵션까지 똑같은 배를 산다.
        // 부하 해고 — 부하편성 오른쪽 단추에 「해고한다」.
        var dismiss = Toggle("부하 해고", GameSettings.MateDismiss, on => GameSettings.MateDismiss = on,
            "원본에 없는 것입니다 — 여관 · 술집 「부하편성」의 결정 오른쪽에 「해고」 단추가 생깁니다. 부하 줄을 눌러 잡고 「해고」를 누릅니다."
            + " 해고하면 계약이 끝나 재계약을 안 맺었을 때처럼 「또 일이 있으면 불러 주십시오!」 하고 떠나고,"
            + " 그 뒤로는 여느 인물처럼 제 갈 길을 갑니다(다시 술집에서 고용할 수 있습니다).");
        general.Children.Add(dismiss);
        // 그 밑 — 보고 시 재계약 끄기. 부하 해고를 켰을 때만 살아 있다.
        var skip = Toggle("보고 시 재계약 끄기", GameSettings.SkipRecontract, on => GameSettings.SkipRecontract = on,
            "부하 해고를 켰을 때만 듭니다(기본 켬) — 후원자에게 보고해 계약이 끝나도 부하마다 「한번 더 제독의 배를 탈 수 있습니다」를"
            + " 묻지 않습니다. 부하는 선금 없이 그대로 남고, 내보내고 싶으면 부하편성에서 해고합니다. 끄면 원본처럼 물어봅니다.");
        skip.Margin = new Thickness(18, skip.Margin.Top, skip.Margin.Right, skip.Margin.Bottom);
        skip.IsEnabled = dismiss.IsChecked == true;
        dismiss.Checked += (_, _) => skip.IsEnabled = true;
        dismiss.Unchecked += (_, _) => skip.IsEnabled = false;
        general.Children.Add(skip);

        general.Children.Add(Toggle("특별주문", GameSettings.SpecialOrder, on => GameSettings.SpecialOrder = on,
            "원본에 없는 것입니다 — 조선소 「구입」 아래에 「특별주문」 줄이 생깁니다. 가진 배 가운데 그 조선소가 파는 선체를 고르면"
            + " 포탑 · 대포 · 돛 · 개조까지 똑같은 새 배를 짓습니다. 값은 새 배값에 개조비 장부 · 포탑 · 대포 · 선수상 값을 더한 것입니다."
            + " 그 마을에서 안 파는 선수상은 빼고, 안 파는 대포는 바꿔 달지 묻습니다."));

        // 오프닝 동영상 — 원본은 켤 때마다 로고와 오프닝을 튼다(0x00410AE3 · 0x00410B22).
        general.Children.Add(Toggle("오프닝 동영상", GameSettings.PlayOpeningMovie,
            on => GameSettings.PlayOpeningMovie = on,
            "켤 때 로고(LOGO.AVI)와 오프닝(OPEN.AVI) 동영상을 틉니다(원본 그대로). 끄면 둘 다 건너뛰고 곧장 메인메뉴로 갑니다."));

        // 직업 누르면 다시 굴림 — 원본은 직업을 바꿔도 안 굴린다(0x0045D8DA).
        general.Children.Add(Toggle("직업 누르면 다시 굴림", GameSettings.RerollOnJob,
            on => GameSettings.RerollOnJob = on,
            "원본에 없는 것입니다 — 새 주인공(NORMAL) 능력치 창에서 직업 단추를 누를 때마다 능력치와 보너스를 새로 굴립니다."
            + " 넣어 둔 보너스는 도로 걷힙니다. 끄면 원본처럼 직업은 기본 기술만 정합니다."));

        // 해적 조우 확률 — 바다 주사위 폭(유럽 700 · 동쪽 400)을 배수로 나눈다(0x0048CABA).
        general.Children.Add(Select("해적 조우 확률",
            [.. GameSettings.SeaRaidScales.Select((s, i) =>
                s == 0 ? "안 만남" : i == GameSettings.DefaultSeaRaidScale ? $"x{s} (원본)" : $"x{s}")],
            GameSettings.SeaRaidScale,
            i => GameSettings.SeaRaidScale = i,
            "바다에서 해적(유럽 바다)·이슬람 함대(동지중해~아라비아해)가 붙는 확률의 배수입니다."
            + " 원본은 걸음마다 유럽 700분의 1, 동쪽 400분의 1입니다. 「안 만남」이면 아예 붙지 않습니다."
            + " 지도에 보이는 적 함대와 마주치는 것은 따로라 바뀌지 않습니다."));

        // 마을·항구에 들고 날 때 보내는 날수. 원본은 열흘씩이라 오가는 시험이 더디다.
        general.Children.Add(Select("출입 일수",
            [.. Enumerable.Range(GameSettings.MinPortDays,
                                 GameSettings.MaxPortDays - GameSettings.MinPortDays + 1)
                          .Select(n => n == GameSettings.DefaultPortDays ? $"{n}일 (원본)" : $"{n}일")],
            GameSettings.PortDays - GameSettings.MinPortDays,
            i => GameSettings.PortDays = i + GameSettings.MinPortDays,
            $"항구·마을에 들어가고 나올 때 각각 지나는 날수. 원본 기본값 {GameSettings.DefaultPortDays}일입니다."
            + " 바꾼 값은 다음 출입부터 곧바로 듭니다."));

        // 인물 이동 — 떠날지 굴리는 때와 확률. 원본은 매월 1일 5분의 1이다.
        // 첫 줄(0)이 원본 「매월 1일」이고, 그 뒤 줄 번호가 곧 날수다.
        general.Children.Add(Select("이동 주기",
            ["매월 1일 (원본)", .. Enumerable.Range(1, GameSettings.MaxPersonRollDays).Select(n => $"{n}일마다")],
            GameSettings.PersonRollDays,
            i => GameSettings.PersonRollDays = i,
            "인물(14~200번)이 떠날지 굴리는 때. 원본은 매월 1일입니다. N일마다는 1480년 1월 1일부터 셉니다."
            + " 역사 항해자 대본은 늘 매월 1일입니다."));
        general.Children.Add(Select("떠날 확률",
            [.. Enumerable.Range(GameSettings.MinPersonMoveOdds,
                                 GameSettings.MaxPersonMoveOdds - GameSettings.MinPersonMoveOdds + 1)
                          .Select(n => n == 1 ? "1분의 1 (반드시)" : $"{n}분의 1")],
            GameSettings.PersonMoveOdds - GameSettings.MinPersonMoveOdds,
            i => GameSettings.PersonMoveOdds = i + GameSettings.MinPersonMoveOdds,
            $"굴릴 때마다 떠날 확률. 원본은 {GameSettings.DefaultPersonMoveOdds}분의 1입니다."));

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 6, 0, 12),
        };
        // 닫기는 게임 띠 단추다 — 설정·단축키 창과 같이 원본 단추 결로 맞춘다.
        buttons.Children.Add(new GameButton("닫기", Close, width: 110));

        var title = GameUi.TitleBar("모드", Close);
        GameUi.EnableDrag(this, title);

        // 오른쪽 설명 칸 — 줄 이름과 설명을 한 판에 담는다.
        var tip = new StackPanel();
        tip.Children.Add(_tipName);
        tip.Children.Add(_tipText);

        var side = new Border
        {
            Width = TipWidth,
            Margin = new Thickness(0, 10, 12, 4),
            Padding = new Thickness(10, 8, 10, 8),
            Background = new SolidColorBrush(Color.FromArgb(0x30, 0, 0, 0)),
            BorderBrush = GameUi.Edge,
            BorderThickness = new Thickness(1),
            Child = tip,
        };

        // 두 판을 한 칸에 겹쳐 두고 안 보이는 쪽은 Hidden 으로 — 자리를 지켜 탭을 넘겨도 창 크기가 안 바뀐다.
        var pages = new Grid();
        pages.Children.Add(rows);
        pages.Children.Add(info);
        pages.Children.Add(ui);
        pages.Children.Add(general);
        pages.Children.Add(lab);
        info.Visibility = Visibility.Hidden;
        ui.Visibility = Visibility.Hidden;
        lab.Visibility = Visibility.Hidden;
        general.Visibility = Visibility.Hidden;

        var body = new StackPanel { Orientation = Orientation.Horizontal };
        body.Children.Add(pages);
        body.Children.Add(side);

        var stack = new StackPanel();
        stack.Children.Add(title);
        stack.Children.Add(Tabs(rows, info, ui, general, lab));
        stack.Children.Add(body);
        stack.Children.Add(buttons);

        Content = new Border
        {
            Background = GameUi.Back,
            BorderBrush = GameUi.Edge,
            BorderThickness = new Thickness(2),
            Margin = new Thickness(4),
            Child = stack,
        };

        KeyDown += (_, e) => { if (e.Key is Key.Escape) Close(); };
    }

    /// <summary>탭 머리 — 「편의성」·「일반」. 누른 쪽 판만 보이고 머리는 밝게 선다.</summary>
    private FrameworkElement Tabs(FrameworkElement convenience, FrameworkElement info, FrameworkElement ui,
                                  FrameworkElement general, FrameworkElement lab)
    {
        var bar = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(12, 8, 12, 0) };
        var heads = new List<(Border Head, FrameworkElement Page)>();

        void Select(FrameworkElement page)
        {
            foreach (var (head, p) in heads)
            {
                bool on = ReferenceEquals(p, page);
                p.Visibility = on ? Visibility.Visible : Visibility.Hidden;
                head.Background = on ? new SolidColorBrush(Color.FromArgb(0x60, 0xFF, 0xFF, 0xFF)) : Brushes.Transparent;
                ((TextBlock)head.Child).Opacity = on ? 1 : 0.6;
            }
            _tipName.Text = "";
            _tipText.Text = Greeting;
        }

        void Add(string text, FrameworkElement page)
        {
            var head = new Border
            {
                BorderBrush = GameUi.Edge,
                BorderThickness = new Thickness(1, 1, 1, 0),
                Padding = new Thickness(16, 4, 16, 4),
                Margin = new Thickness(0, 0, 4, 0),
                Cursor = Cursors.Hand,
                Child = new TextBlock { Text = text, Foreground = GameUi.Text, FontWeight = FontWeights.Bold, FontSize = 15 },
            };
            head.MouseLeftButtonDown += (_, _) => Select(page);
            heads.Add((head, page));
            bar.Children.Add(head);
        }

        Add("편의성", convenience);
        Add("정보", info);
        Add("UI", ui);
        Add("일반", general);
        Add("실험", lab);
        Select(convenience);
        return bar;
    }

    /// <summary>커스텀 BGM 켜고 끄기와 그 밑의 「곡 등록」 단추. 끄면 단추도 흐려진다.</summary>
    private UIElement CustomBgmControls()
    {
        var box = Toggle("커스텀 BGM", GameSettings.CustomBgmEnabled,
            on => GameSettings.CustomBgmEnabled = on,
            "원본에 없는 기능입니다 — 곡 번호마다 등록해 둔 파일이 있으면 그걸로 갈아 낍니다."
            + " 등록은 아래 「곡 등록」 단추로 합니다. 꺼도 등록은 그대로 남고, 다시 켜면 그대로 씁니다.");

        var open = GameUi.PushButton("곡 등록…", () => CustomBgmDialog.Show(this), 120);
        open.HorizontalAlignment = HorizontalAlignment.Left;
        open.Margin = new Thickness(18, 2, 0, 2);
        open.IsEnabled = box.IsChecked == true;
        open.Opacity = open.IsEnabled ? 1 : 0.4;
        box.Checked += (_, _) => { open.IsEnabled = true; open.Opacity = 1; };
        box.Unchecked += (_, _) => { open.IsEnabled = false; open.Opacity = 0.4; };
        Watch(open, "곡 등록", "곡 번호마다 틀 파일을 고릅니다. 커스텀 BGM 을 켜야 누를 수 있습니다.");

        var group = new StackPanel();
        group.Children.Add(box);
        group.Children.Add(open);
        return group;
    }

    /// <summary>그 줄의 설명을 오른쪽 칸에 건다. 커서가 떠나도 마지막 것을 남긴다.</summary>
    private void Watch(FrameworkElement row, string label, string tip)
    {
        void Show()
        {
            _tipName.Text = label;
            _tipText.Text = tip;
        }

        row.MouseEnter += (_, _) => Show();
        row.GotKeyboardFocus += (_, _) => Show();
        row.PreviewMouseLeftButtonDown += (_, _) => Show();
    }

    /// <summary>켜고 끄는 줄 하나.</summary>
    private CheckBox Toggle(string label, bool on, Action<bool> set, string tip)
    {
        var box = new CheckBox
        {
            Content = label,
            IsChecked = on,
            Foreground = GameUi.Text,
            FontWeight = FontWeights.Bold,
            FontSize = 15,
            Margin = new Thickness(0, 8, 0, 2),
            VerticalContentAlignment = VerticalAlignment.Center,
        };
        box.Checked += (_, _) => set(true);
        box.Unchecked += (_, _) => set(false);
        Watch(box, label, tip);
        return box;
    }

    /// <summary>바다 입체 효과 켜기와 그 밑의 밝기 막대. 끄면 막대도 흐려진다.</summary>
    private UIElement SeaControls(Options options)
    {
        var box = Toggle("바다 입체 효과", options.SeaOn(), options.SetSea,
            "원본에 없는 덧그림입니다 — 바다에 움직이는 물결 굴곡과 햇빛 반짝임을 얹고, 해안에서 멀수록 깊은 색으로,"
            + " 해안선에는 흰 물보라를 칩니다. 지도를 키울수록 물결이 또렷합니다. 켜 둔 동안은 화면을 계속 다시 그립니다.");

        var value = new TextBlock { Width = 48, Foreground = GameUi.Text, VerticalAlignment = VerticalAlignment.Center };
        var slider = new Slider
        {
            Minimum = 0.6, Maximum = 1.6, TickFrequency = 0.05, IsSnapToTickEnabled = true,
            Width = 150, Margin = new Thickness(18, 0, 0, 0),
            IsEnabled = box.IsChecked == true,
            Value = Math.Clamp(options.SeaBrightness(), 0.6, 1.6),
        };
        void ShowValue() => value.Text = $"{slider.Value:P0}";
        slider.ValueChanged += (_, _) => { ShowValue(); options.SetSeaBrightness(slider.Value); };
        box.Checked += (_, _) => slider.IsEnabled = true;
        box.Unchecked += (_, _) => slider.IsEnabled = false;
        ShowValue();

        var line = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 2) };
        line.Children.Add(new TextBlock
        {
            Text = "밝기", Width = 64, Foreground = GameUi.Text, Margin = new Thickness(18, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
        });
        line.Children.Add(slider);
        line.Children.Add(value);
        Watch(line, "바다 밝기", "바다 입체 효과의 밝기입니다. 100% 가 기본이고 60~160% 사이로 고릅니다. 효과를 켜야 조절할 수 있습니다.");

        var group = new StackPanel();
        group.Children.Add(box);
        group.Children.Add(line);
        return group;
    }

    /// <summary>자동 보급 켜기와 그 밑의 양 고르기(10일분 · 최대). 끄면 고르기도 흐려진다.</summary>
    private UIElement AutoSupplyControls()
    {
        var box = Toggle("자동 보급", GameSettings.AutoSupply,
            on => GameSettings.AutoSupply = on,
            "원본에 없는 것입니다 — 항구에서 「출항」을 누를 때 물과 식량을 저절로 사 싣습니다. 양은 아래에서 고릅니다."
            + " 값은 보급 창과 같은 그 항구 시세이고, 용량·중량·소지금이 모자라면 들어가는 데까지만 싣습니다."
            + " 짐이 차서 못 실으면 그냥 넘어가니, 출항 물음에 뜨는 항해 일수를 보고 손으로 보급하면 됩니다. 산 것은 아래 띠로 알립니다.");

        RadioButton Pick(string label, bool max, string tip)
        {
            var radio = new RadioButton
            {
                Content = label, GroupName = "AutoSupplyAmount",
                IsChecked = GameSettings.AutoSupplyMax == max,
                IsEnabled = box.IsChecked == true,
                Foreground = GameUi.Text, FontSize = 14,
                Margin = new Thickness(0, 0, 16, 0),
                VerticalContentAlignment = VerticalAlignment.Center,
            };
            radio.Checked += (_, _) => GameSettings.AutoSupplyMax = max;
            box.Checked += (_, _) => radio.IsEnabled = true;
            box.Unchecked += (_, _) => radio.IsEnabled = false;
            Watch(radio, "자동 보급 — " + label, tip);
            return radio;
        }

        var line = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(18, 0, 0, 2) };
        line.Children.Add(Pick("10일분", false,
            "물이나 식량이 10일분이 안 될 때 10일분까지 채웁니다. 보급 창의 「10일분」과 같은 셈(선원 수만큼의 통)입니다."));
        line.Children.Add(Pick("최대", true,
            "보급 창의 「최대」처럼 물과 식량을 같은 통 수로, 용량·중량·소지금이 닿는 데까지 채웁니다. 이미 실린 것을 덜어 내지는 않습니다."));

        var group = new StackPanel();
        group.Children.Add(box);
        group.Children.Add(line);
        return group;
    }

    /// <summary>고해상도 바다 켜기와 그 밑의 해류 결 막대. 끄면 막대도 흐려진다.</summary>
    private UIElement HiResSeaControls(Options options)
    {
        var box = Toggle("고해상도 바다", options.HiResSeaOn(), options.SetHiResSea,
            "원본에 없는 덧그림입니다 — 지도를 키웠을 때(칸이 화면 네 점보다 클 때) 바다를 원본 16x16 타일 대신 화면 해상도로"
            + " 새로 그리고, 해안선을 계단 대신 곡선으로 다듬습니다. 바다 색은 원본 타일의 물 색을 따르고, 뭍은 원본 그대로입니다."
            + " 「바다 입체 효과」와 함께 켜면 그 위에 물결 빛이 얹힙니다.");

        var value = new TextBlock { Width = 48, Foreground = GameUi.Text, VerticalAlignment = VerticalAlignment.Center };
        var slider = new Slider
        {
            Minimum = 0, Maximum = 1, TickFrequency = 0.05, IsSnapToTickEnabled = true,
            Width = 150, Margin = new Thickness(18, 0, 0, 0),
            IsEnabled = box.IsChecked == true,
            Value = Math.Clamp(options.SeaFlowAmount(), 0, 1),
        };
        void ShowValue() => value.Text = $"{slider.Value:P0}";
        slider.ValueChanged += (_, _) => { ShowValue(); options.SetSeaFlowAmount(slider.Value); };
        box.Checked += (_, _) => slider.IsEnabled = true;
        box.Unchecked += (_, _) => slider.IsEnabled = false;
        ShowValue();

        var line = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 2) };
        line.Children.Add(new TextBlock
        {
            Text = "해류 결", Width = 64, Foreground = GameUi.Text, Margin = new Thickness(18, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
        });
        line.Children.Add(slider);
        line.Children.Add(value);
        Watch(line, "해류 결", "고해상도 바다에서 해류를 보이는 결과 띠의 짙기입니다. 해류가 흐르는 쪽으로 결이 늘어지고 셀수록 또렷합니다."
            + " 0% 면 잔물결만 해류를 따라 흐르고 결은 안 섭니다. 고해상도 바다를 켜야 조절할 수 있습니다.");

        var group = new StackPanel();
        group.Children.Add(box);
        group.Children.Add(line);
        return group;
    }

    private UIElement MiniMapControls(Options options)
    {
        var box = Toggle("미니맵", options.MiniMapOn(), options.SetMiniMap,
            "항해·뭍 이동 중에 발견물 지도를 지도 오른쪽 아래에 작게 띄웁니다. 배를 가운데 두고 따라갑니다"
            + " (빨강 찾음 · 회색 아직 · 파랑 내 자리).");

        var value = new TextBlock
        {
            Width = 48,
            Foreground = GameUi.Text,
            VerticalAlignment = VerticalAlignment.Center,
        };
        var slider = new Slider
        {
            Minimum = 0.1,
            Maximum = 1.0,
            TickFrequency = 0.1,
            IsSnapToTickEnabled = true,
            Width = 150,
            Margin = new Thickness(18, 0, 0, 0),
            IsEnabled = box.IsChecked == true,
            Value = Math.Clamp(options.MiniMapOpacity(), 0.1, 1.0),
        };
        void ShowValue() => value.Text = $"{slider.Value:P0}";
        slider.ValueChanged += (_, _) =>
        {
            ShowValue();
            options.SetMiniMapOpacity(slider.Value);
        };
        box.Checked += (_, _) => slider.IsEnabled = true;
        box.Unchecked += (_, _) => slider.IsEnabled = false;
        ShowValue();

        var line = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 2) };
        line.Children.Add(new TextBlock
        {
            Text = "투명도",
            Width = 64,
            Foreground = GameUi.Text,
            Margin = new Thickness(18, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
        });
        line.Children.Add(slider);
        line.Children.Add(value);
        Watch(line, "미니맵 투명도", "미니맵을 켠 상태에서 투명도를 조절합니다. 체크를 끄면 조절할 수 없습니다.");

        var group = new StackPanel();
        group.Children.Add(box);
        group.Children.Add(line);
        return group;
    }

    /// <summary>고르는 줄 하나 — 이름과 펼침 상자. 고르면 곧바로 설정에 남긴다.</summary>
    private UIElement Select(string label, IReadOnlyList<string> items, int selected,
                             Action<int> set, string tip)
    {
        var line = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 8, 0, 2),
        };
        line.Children.Add(new TextBlock
        {
            Text = label,
            Width = 64,
            Foreground = GameUi.Text,
            FontWeight = FontWeights.Bold,
            FontSize = 15,
            VerticalAlignment = VerticalAlignment.Center,
        });

        var box = new ComboBox
        {
            Width = 160,
            Margin = new Thickness(6, 0, 6, 0),
            Padding = new Thickness(6, 3, 6, 3),
            FontWeight = FontWeights.Bold,
            FontSize = 14,
            VerticalContentAlignment = VerticalAlignment.Center,
        };
        foreach (string item in items) box.Items.Add(item);
        box.SelectedIndex = Math.Clamp(selected, 0, items.Count - 1);
        box.SelectionChanged += (_, _) => { if (box.SelectedIndex >= 0) set(box.SelectedIndex); };

        line.Children.Add(box);
        Watch(line, label, tip);
        Watch(box, label, tip);
        return line;
    }

    public static void Show(Window owner, Options options) =>
        new ModDialog(options) { Owner = owner }.ShowDialog();
}
