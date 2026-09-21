using System.IO;
using System.Windows.Documents;
using System.Windows.Controls.Primitives;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using System.Windows.Media;
using System.Windows;
using CdsHelper.Game.Engine;
using CdsHelper.Game.Local.Helpers;
using CdsHelper.Support.Local.Helpers;
using CdsHelper.Support.Local.Models;
using CdsHelper.Support.Local.Settings;
using Prism.Ioc;
using CdsHelper.Game.Engine.Discovery;
using CdsHelper.Game.Engine.Disev;
using CdsHelper.Game.Engine.Land;
using CdsHelper.Game.Engine.Market;
using CdsHelper.Game.Engine.Menu;
using CdsHelper.Game.Engine.Sea;
using CdsHelper.Game.Engine.Town;
using CdsHelper.Game.Local.Settings;

namespace CdsHelper.Game.UI.Views;

/// <summary>
/// 게임 화면처럼 지도 위에 함대만 띄우는 창. Direct3D 로 그린다.
/// </summary>
/// <remarks>
/// 세계지도 탭과 별개다. 그쪽은 도시·발견물 마커와 라벨이 WPF 요소로 얹혀 있어 손대지 않았고,
/// 이 창은 마커가 없는 대신 자식 창에 스왑체인을 곧바로 걸어 짧은 길로 그린다.
/// airspace 규칙상 D3D 화면 위에는 WPF 를 얹을 수 없으므로, 조작 줄은 화면 위가 아니라
/// 위아래로 나눠 놓았다.
/// </remarks>
public sealed class ShipMapWindow : Window
{
    private readonly ShipMapHost _host = new();
    private readonly TextBlock _status = new()
    {
        Foreground = Brushes.Gainsboro,
        VerticalAlignment = VerticalAlignment.Center,
        Margin = new Thickness(8, 0, 0, 0),
        FontFamily = new FontFamily("Consolas"),
    };
    private readonly DispatcherTimerLite _statusTimer;

    /// <summary>
    /// 지도 아래 띠에 적는 글. 게임은 이 자리에 짧은 알림을 낸다 —
    /// "명성치가 모자랍니다." 처럼 창을 띄울 것도 없는 한마디다.
    /// </summary>
    /// <remarks>
    /// 글은 <b>띠 위에 바로</b> 찍는다 — 베이지 단추를 깔고 그 위에 얹지 않는다.
    /// 게임 갈무리를 보면 위쪽 정보 띠와 달리 이 자리에는 칸이 없고 액자 바탕에 글자만
    /// 놓여 있다. 단추로 두면 짧은 한마디마다 띠 위에 밝은 조각이 서서 어색하다.
    ///
    /// 글자는 <b>검정</b>이다 — 밝은 베이지 띠 위라 흰 글씨는 읽히지 않는다.
    /// </remarks>
    private readonly GameUi.GameLabel _note = new(GameFont.BlackColor)
    {
        Margin = new Thickness(12, 0, 0, 0),
        HorizontalAlignment = HorizontalAlignment.Left,
    };

    /// <summary>현재 타이틀 화면용 알림 글자. 지도 하단 띠와 컨트롤을 공유하지 않는다.</summary>
    private GameUi.GameLabel? _titleNote;

    /// <summary>한 판 — 게임 폴더 · 주인공 · 표들 · 소리. 화면들이 이것을 받아 쓴다.</summary>
    private readonly Engine.Game _game = new();

    /// <summary>지도 쪽 화면. 타이틀에서 고르면 이것으로 갈아 끼운다.</summary>
    private FrameworkElement _mapRoot = null!;

    /// <summary>
    /// 타이틀과 지도를 갈아 끼우는 자리. 제목 줄을 우리가 그리게 되면서
    /// <see cref="ContentControl.Content"/> 는 제목 줄까지 담게 되었으므로,
    /// 화면만 바꿔 끼울 칸을 따로 두었다.
    /// </summary>
    private readonly ContentControl _screen = new()
    {
        HorizontalContentAlignment = HorizontalAlignment.Stretch,
        VerticalContentAlignment = VerticalAlignment.Stretch,
    };

    /// <summary>타이틀 쪽 화면. 키를 이 화면에서만 받으려고 들고 있는다.</summary>
    private FrameworkElement? _titleRoot;

    /// <summary>게임 상단 정보 띠. 플레이어 값을 한 번 채운 뒤에만 보인다.</summary>
    private FrameworkElement? _gameBar;

    /// <summary>상단 정보 띠의 첫 표시를 끝냈는지.</summary>
    private bool _barReady;

    /// <summary>지도를 한 번 띄웠는지. <see cref="ShipMapHost.Start"/> 는 한 번만 부른다.</summary>
    private bool _started;

    /// <summary>방금 물어본 도시. 떠났다 다시 와야 다시 묻는다.</summary>
    private int _askedCity = -1;

    /// <summary>다이얼로그가 떠 있는 동안 또 묻지 않게.</summary>
    private bool _asking;

    /// <summary>초점 진단이 마지막으로 찍은 줄. 상태줄 뒤에 붙는다.</summary>
    private string _focusNote = "";

    /// <summary>지도 위에 겹쳐 둔 투명한 입력 판. 커서 자리를 이것 기준으로 잰다.</summary>
    private Border _input = null!;

    // 상단 띠의 칸들. 게임 것은 <b>베이지 버튼 띠</b>다 — MISC.CDS 파트 4 의 왼끝(16) ·
    // 가운데(8, 되풀이) · 오른끝(16) 을 이어 붙이고 그 위에 비트맵 글꼴을 짙은 갈색(색인 17)
    // 으로 찍는다(<see cref="GameButton"/>). 칸을 따로 그리던 것을 이것으로 바꿨다 —
    // 확대해 보면 칸 사이 이음매가 마구리 둘이 맞닿은 모양이라 버튼임이 드러난다.
    // 모드 쪽 ButtonMakerKR 이 같은 길로 짓는다(진홍=타이틀 · 베이지=버튼 · 회녹색=다른 상태).

    /// <summary>게임 상단 바의 날짜 칸. 조합에서 기술을 배우면 달이 넘어간다.</summary>
    private readonly GameButton _date = new("") { Lit = true, Margin = default };

    /// <summary>게임 상단 바의 소지금·함선 칸.</summary>
    private readonly GameButton _purse = new("") { Lit = true, Margin = default };

    /// <summary>게임 상단 바의 명성 칸. 후원자를 만날 수 있는지가 이 값으로 갈린다.</summary>
    private readonly GameButton _fame = new("") { Lit = true, Margin = default };

    /// <summary>선원들이 지친 만큼. 폭풍을 맞으면 오르고 자택 휴양이 푼다.</summary>
    private readonly GameButton _tired = new("") { Lit = true, Margin = default };
    private readonly GameButton _morale = new("") { Lit = true, Margin = default };

    /// <summary>태우고 있는 선원 수.</summary>
    private readonly GameButton _crew = new("") { Lit = true, Margin = default };

    /// <summary>바람과 배 속도. 게임 띠에는 없는 칸이라 꺼 둔 채로 낸다.</summary>
    private readonly GameButton _windText = new("") { Lit = true, Margin = default };
    private readonly GameButton _currentText = new("") { Lit = true, Margin = default };
    private readonly GameButton _hpCell = new("") { Lit = true, Margin = default };

    /// <summary>실어 둔 물과 식량(통).</summary>
    private readonly GameButton _stores = new("") { Lit = true, Margin = default };

    /// <summary>보급이 며칠 갈지. 게임 셈(<c>0x00494010</c>)을 그대로 낸다.</summary>
    private readonly GameButton _left = new("") { Lit = true, Margin = default };

    /// <summary>게임 상단 바의 위경도 칸.</summary>
    private readonly GameButton _coord = new("") { Lit = true, Margin = default };

    /// <summary>게임 상단 바의 도시명 칸. 바다에서는 빈 채로 둔다.</summary>
    private readonly GameButton _cityLabel = new("") { Lit = true, Margin = default };

    /// <summary>들어와 있는 도시의 말. 바다에서는 줄표만 나온다.</summary>
    private readonly GameButton _language = new("") { Lit = true, Margin = default };

    /// <summary>들어와 있는 도시의 시세(백분율). 바다에서는 줄표만 나온다.</summary>
    private readonly GameButton _rate = new("") { Lit = true, Margin = default };

    /// <summary>지도 위에 겹쳐 띄우는 좌표 상자의 글.</summary>
    private readonly TextBlock _overlayText = new()
    {
        Foreground = Brushes.White,
        FontFamily = new FontFamily("Consolas"),
        FontSize = 12,
        LineHeight = 16,
        LineStackingStrategy = LineStackingStrategy.BlockLineHeight,
    };

    /// <summary>
    /// 좌표 상자. <see cref="Popup"/> 은 제 창(HWND)을 쓰므로 D3D 자식 창 위에 제대로 뜬다 —
    /// 커맨드 메뉴와 같은 수를 쓴 것이다. 보통 WPF 요소로는 airspace 에 막혀 얹을 수 없다.
    /// </summary>
    private Popup _overlay = null!;

    /// <summary>
    /// 만난 사람 상자 — 말을 걸어 본 여급(친밀도·궁합)과 만난 인물을 지도 위에 겹쳐 낸다.
    /// </summary>
    /// <remarks>
    /// 놀이에는 없는 것이라 개발 창의 "정보" 로만 켠다(<see cref="GameSettings.ShowPeopleOverlay"/>).
    /// 좌표 상자와 같은 꼴이고, 자리만 지도 오른쪽 위다.
    /// </remarks>
    private Popup _people = null!;

    /// <summary>만난 사람 상자의 글.</summary>
    private readonly TextBlock _peopleText = new()
    {
        Foreground = Brushes.White,
        FontFamily = new FontFamily("Consolas"),
        FontSize = 12,
        LineHeight = 16,
        LineStackingStrategy = LineStackingStrategy.BlockLineHeight,
    };

    /// <summary>만난 사람 상자를 켜 두었는지.</summary>
    private bool _peopleWanted = GameSettings.ShowPeopleOverlay;

    /// <summary>제독 컨디션(HP) 상자 — 지도 왼쪽 아래. 개발 창의 「컨디션」으로 켠다.</summary>
    private Popup _vital = null!;

    /// <summary>컨디션 상자를 켜 두었는지.</summary>
    private bool _vitalWanted = GameSettings.ShowConditionOverlay;

    /// <summary>미니맵 — 지도 오른쪽 아래. 개발 창의 「미니맵」으로 켠다.</summary>
    private Popup _miniPopup = null!;

    /// <summary>바다의 비·눈 — 날마다 굴린다(<c>0x0044AFD0</c>). 그림은 <see cref="_weatherView"/> 가 그린다.</summary>
    private readonly SeaWeather _seaWeather = new();
    private readonly WeatherView _weatherView = new();
    private Popup _weatherPopup = null!;
    private bool _weatherLoaded;
    private WindTable? _weatherWind;

    /// <summary>빗소리 — 소리 0x3F(WAVE 파트 35).</summary>
    private const int RainSoundPart = 0x3F - WaveBank.FirstSoundId;
    private readonly MiniMapView _mini = new();
    private bool _miniWanted = GameSettings.ShowMiniMap;

    /// <summary>이벤트가 끝나고 이만큼 조용해야 미니맵이 다시 뜬다 — 연달아 뜨는 창 사이에 깜빡이지 않게.</summary>
    private static readonly TimeSpan MiniCalm = TimeSpan.FromSeconds(1);

    /// <summary>이벤트 없이 조용해진 때. 이벤트 중이면 null.</summary>
    private DateTime? _miniCalmSince;

    /// <summary>컨디션 글 한 줄.</summary>
    private readonly TextBlock _vitalText = new()
    {
        Foreground = Brushes.White,
        FontFamily = new FontFamily("Consolas"),
        FontSize = 12,
    };

    /// <summary>컨디션 막대 — 판 폭이 2000 이다.</summary>
    private readonly Canvas _vitalBar = new() { Width = VitalBarWidth, Height = 10, Margin = new Thickness(0, 4, 0, 0) };

    private const double VitalBarWidth = 220;

    /// <summary>좌표 상자를 켜 두었는지. 실제로 뜨는지는 <see cref="SyncOverlay"/> 가 정한다.</summary>
    private bool _overlayWanted = GameSettings.ShowCoordOverlay;

    // 게임 화면 위쪽 띠에서 뽑은 색. 누런 양피지 바탕에 어두운 테두리다.
    private static readonly Brush BarFill = new SolidColorBrush(Color.FromRgb(0xC8, 0xBF, 0xA0));
    private static readonly Brush BarEdge = new SolidColorBrush(Color.FromRgb(0x4A, 0x40, 0x30));

    // 게임 커맨드 창에서 뽑은 색. 짙은 밤색 바탕에 밝은 테를 두르고, 항목만 양피지다.
    private static readonly Brush MenuBack = new SolidColorBrush(Color.FromRgb(0x4A, 0x2A, 0x22));
    // 창 테는 공용 것을 그대로 쓴다 — 같은 색을 두 군데 적어 두면 한쪽만 바뀐다.
    private static readonly Brush MenuEdge = GameUi.Edge;

    /// <summary>
    /// 메인메뉴 상자의 테. 여느 창의 검은 테(<see cref="GameUi.Edge"/>)가 아니라
    /// 바탕보다 조금 밝은 <c>C2AE95</c> 한 점이다 — 갈무리에서 집은 색이다.
    /// </summary>
    private static readonly Brush TitleBoxEdge = new SolidColorBrush(Color.FromRgb(0xC2, 0xAE, 0x95));
    private static readonly Brush MenuTitleFg = new SolidColorBrush(Color.FromRgb(0xEC, 0xDF, 0xC0));

    /// <summary>
    /// 도시정보 창에서 켜고 끄는 칸. 켠 상태를 따로 들고 있지 않고 칸의
    /// <see cref="UIElement.Visibility"/> 를 그대로 본다 — 둘로 나누면 어긋난다.
    /// </summary>
    private readonly Dictionary<string, FrameworkElement> _infoCells = [];

    /// <summary>도시정보 창의 줄 이름을 달아 띠에 놓는 칸.</summary>
    /// <summary>
    /// 상단 띠 칸마다의 서식 — 게임 것을 <b>자리 수까지</b> 그대로 옮겼다.
    /// </summary>
    /// <remarks>
    /// <code>
    ///   0x0056BE98  "%4d년%2d월%2d일"
    ///   0x0056BEA8  "%s%4d명"              %s 는 "선원"(0x56BEB8) 또는 "대원"(0x56BEB0)
    ///   0x0056BEC0  "물%4d통 식량%4d통"
    ///   0x0056BED8  "%s위 %3d  %s경 %3d  " 북·남 / 동·서 (0x56BEF0~)
    ///   0x0056BF18  "소지금%6d닢"
    ///   0x0056BF28  "피로도%4d"
    ///   0x0056BF38  "명성%6d"
    ///   0x0056BF90  "남은일수%4d"          계약이 없으면 0x0056BFA0 "남은일수----"
    ///   0x0056BF70  "시세%4d%"
    /// </code>
    /// 칸 너비는 글자 수를 따라가므로 <b>서식이 맞으면 너비도 맞는다</b> — 예전에는
    /// "1499년 5월8일" · "1770닢" 처럼 자리를 안 맞춰 칸마다 폭이 어긋났다.
    /// </remarks>
    private FrameworkElement InfoCell(string name, GameButton cell, bool on)
    {
        // 지난번에 켜고 끈 것이 있으면 그것이 먼저다. 한 번도 안 건드렸으면(null)
        // 여기 적힌 기본값으로 선다.
        var saved = GameSettings.BarCells;
        cell.Visibility = (saved?.Contains(name) ?? on) ? Visibility.Visible : Visibility.Collapsed;
        _infoCells[name] = cell;
        return cell;
    }

    /// <summary>지금 띠에 켜져 있는 칸을 적어 둔다. 다음에 켤 때 이대로 선다.</summary>
    private void SaveBarCells() =>
        GameSettings.BarCells =
            [.. _infoCells.Where(p => p.Value.Visibility == Visibility.Visible).Select(p => p.Key)];

    public ShipMapWindow()
    {
        Title = "대항해시대3";
        // 크기는 설정에 적어 둔 것으로 선다(기본은 예전 그대로 1200x800).
        Width = 1200;
        Height = 800;
        Loaded += (_, _) => SettingsDialog.Apply(this);
        Background = Brushes.Black;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        // HwndHost 자체는 WPF 에 아무것도 그리지 않아 히트테스트에 안 걸린다.
        // 같은 자리에 투명 Border 를 겹쳐 두고 마우스는 그쪽에서 받는다.
        // (자식 창이 D3D 로 덮으므로 이 Border 는 보이지 않는다 — 입력만 받는다.)
        // 지도 위에서도 보통 화살표를 쓴다 — 십자는 조준하는 것처럼 보여 게임 화면과 안 맞는다.
        // 눌렀을 때 초점을 받게 둔다. 지도는 WPF 가 모르는 자식 창이라, 이것이 없으면
        // 지도를 눌러도 창 안에 초점 가진 것이 없는 상태가 된다.
        // 자식 창이 덮어 보이지 않으니 초점 테두리는 끈다.
        var input = new Border
        {
            Background = Brushes.Transparent,
            Cursor = Cursors.Arrow,
            Focusable = true,
            FocusVisualStyle = null,
        };
        _input = input;
        var surface = new Grid();
        surface.Children.Add(_host);
        surface.Children.Add(input);

        // 좌표 상자는 지도 왼쪽 위에 겹쳐 둔다. 히트테스트를 꺼서 그 밑으로 배를 몰 수 있게 한다.
        _overlay = new Popup
        {
            PlacementTarget = input,
            Placement = PlacementMode.Relative,
            HorizontalOffset = 10,
            VerticalOffset = 10,
            AllowsTransparency = true,
            StaysOpen = true,
            IsHitTestVisible = false,
            Child = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(0xB4, 0x10, 0x10, 0x10)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(0xC8, 0x0B, 0x05, 0x05)),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(10, 6, 10, 6),
                IsHitTestVisible = false,
                Child = _overlayText,
            },
        };
        surface.Children.Add(_overlay);   // 자리만 잡아 둔다 — 실제로는 제 창에 뜬다

        // 만난 사람 상자는 지도 오른쪽 위에 겹쳐 둔다. 좌표 상자와 같은 꼴이되 자리만 반대다.
        _people = new Popup
        {
            PlacementTarget = input,
            Placement = PlacementMode.Right,
            AllowsTransparency = true,
            StaysOpen = true,
            IsHitTestVisible = false,
            Child = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(0xB4, 0x10, 0x10, 0x10)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(0xC8, 0x0B, 0x05, 0x05)),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(10, 6, 10, 6),
                IsHitTestVisible = false,
                Child = _peopleText,
            },
        };
        surface.Children.Add(_people);

        // 컨디션 상자는 지도 왼쪽 아래다. 높이가 늘 같으므로 아래 끝에서 그만큼 끌어올린다.
        _vital = new Popup
        {
            PlacementTarget = input,
            Placement = PlacementMode.Bottom,
            HorizontalOffset = 10,
            VerticalOffset = -56,
            AllowsTransparency = true,
            StaysOpen = true,
            IsHitTestVisible = false,
            Child = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(0xB4, 0x10, 0x10, 0x10)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(0xC8, 0x0B, 0x05, 0x05)),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(10, 5, 10, 6),
                IsHitTestVisible = false,
                Child = new StackPanel { Children = { _vitalText, _vitalBar } },
            },
        };
        surface.Children.Add(_vital);

        // 비·눈은 지도 전체를 덮는 층이다 — 미니맵보다 먼저 연다(아래에 깔린다).
        _weatherPopup = new Popup
        {
            PlacementTarget = input,
            Placement = PlacementMode.Relative,
            AllowsTransparency = true,
            StaysOpen = true,
            IsHitTestVisible = false,
            Child = _weatherView,
        };

        // 미니맵은 지도 오른쪽 아래다. 자리는 띄울 때 지도 크기로 다시 잡는다(SyncOverlay).
        _miniPopup = new Popup
        {
            PlacementTarget = input,
            Placement = PlacementMode.Relative,
            AllowsTransparency = true,
            StaysOpen = true,
            IsHitTestVisible = false,
            Child = _mini,
        };
        surface.Children.Add(_miniPopup);

        // 게임 상단 띠. 어느 칸을 띄울지는 도시정보 창에서 켜고 끈다(띠를 오른쪽 단추로 누른다).
        // 이동 모드(정박·해상 이동) 칸은 뺐다 — 게임 띠에 없는 칸이다.
        var gameCells = new StackPanel { Orientation = Orientation.Horizontal };
        gameCells.Children.Add(InfoCell(CityInfoMenu.Date, _date, on: true));
        // 선원 칸은 처음부터 켜 둔다 — 게임 띠도 날짜·선원·소지금 셋으로 선다.
        gameCells.Children.Add(InfoCell(CityInfoMenu.Crew, _crew, on: true));
        gameCells.Children.Add(InfoCell(CityInfoMenu.Stores, _stores, on: false));
        gameCells.Children.Add(InfoCell(CityInfoMenu.DaysLeft, _left, on: false));
        gameCells.Children.Add(InfoCell(CityInfoMenu.Wind, _windText, on: false));
        gameCells.Children.Add(InfoCell(CityInfoMenu.Coord, _coord, on: true));
        gameCells.Children.Add(InfoCell(CityInfoMenu.Gold, _purse, on: true));
        gameCells.Children.Add(InfoCell(CityInfoMenu.Fame, _fame, on: true));
        gameCells.Children.Add(InfoCell(CityInfoMenu.Fatigue, _tired, on: false));
        gameCells.Children.Add(InfoCell(CityInfoMenu.Morale, _morale, on: true));
        gameCells.Children.Add(InfoCell(CityInfoMenu.City, _cityLabel, on: false));
        gameCells.Children.Add(InfoCell(CityInfoMenu.Language, _language, on: false));
        gameCells.Children.Add(InfoCell(CityInfoMenu.Rate, _rate, on: false));
        gameCells.Children.Add(InfoCell(CityInfoMenu.Current, _currentText, on: false));
        gameCells.Children.Add(InfoCell(CityInfoMenu.Vitality, _hpCell, on: false));

        // 게임처럼 액자를 깔고 그 위에 칸들을 얹는다(asset/ui/misc-00.png).
        // 그림이 없으면 예전처럼 민색 띠로 물러선다.
        FrameworkElement gameBar = (FrameworkElement?)GameUi.BarFrame(gameCells)
            ?? new Border
            {
                Background = BarFill,
                BorderBrush = BarEdge,
                BorderThickness = new Thickness(0, 0, 0, 2),
                Child = gameCells,
            };
        _gameBar = gameBar;
        gameBar.Visibility = Visibility.Collapsed;

        // 띠를 오른쪽 단추로 누르면 도시정보 창이 뜬다 — 게임처럼 도시 안에서만 낸다.
        gameBar.MouseRightButtonUp += (_, e) =>
        {
            e.Handled = true;
            ShowCityInfoMenu(gameBar, e.GetPosition(gameBar));
        };

        // 왼쪽 단추로 누르면 커맨드 창이 뜬다 — 게임도 상단 띠를 누르면 이것이 나온다.
        // 지도에서는 오른쪽 단추가 같은 창을 내는데, 띠에서는 오른쪽이 도시정보 몫이라
        // 왼쪽을 준다. 창은 띠 <b>바로 밑</b>에 붙는다.
        gameBar.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            ShowCommandMenu(gameBar, new Point(e.GetPosition(gameBar).X, gameBar.ActualHeight));
        };

        var root = new DockPanel();
        DockPanel.SetDock(gameBar, Dock.Top);
        root.Children.Add(gameBar);
        // 게임은 지도 아래에도 같은 띠를 하나 둔다 — 짧은 알림이 이 자리에 뜬다.
        var footer = TitleBarStrip(null, _note);
        // 띠를 누르면 마지막 알림을 상자로 다시 편다(0x0040DE30).
        footer.Cursor = System.Windows.Input.Cursors.Hand;
        footer.MouseLeftButtonUp += (_, e) => { e.Handled = true; ReadNote(); };
        DockPanel.SetDock(footer, Dock.Bottom);
        root.Children.Add(footer);
        root.Children.Add(surface);
        _mapRoot = root;

        // 타이틀을 지도 위에 겹쳐 둘 수는 없다 — airspace 규칙상 D3D 자식 창이 WPF 를 덮는다.
        // 그래서 겹치지 않고 통째로 갈아 끼운다. 타이틀이 떠 있는 동안은 자식 창 자체가 없다.
        _titleRoot = BuildTitleScreen();
        _screen.Content = _titleRoot;

        // 윈도 제목 줄 대신 크롬처럼 우리가 그린 줄을 얹는다.
        // 왼쪽 위 햄버거에는 앱이 적어 둔 것을 들여다보는 줄을 단다.
        var shell = new DockPanel { LastChildFill = true };
        var titleBar = ChromeTitleBar.Attach(this, out var hamburger,
            // 원본에 없는 줄은 모드 창에서 켜야 뜬다.
            label => label switch
            {
                DiscoveryMapRow => GameSettings.ShowDiscoveryMapMenu,
                BarmaidBookRow => GameSettings.ShowBarmaidBookMenu,
                PersonMoveRow => GameSettings.ShowPersonMoveMenu,
                _ => true,
            },
            // 설정은 게임 띠에 두었다가 햄버거로 옮겼다 — 게임 띠에 없는 칸이라
            // 섞여 있으면 원본과 달라 보인다(개발 창을 옮긴 것과 같은 까닭이다).
            // 지도 배율은 고르는 그 자리에서 지도에 먹인다.
            ("설정", () => SettingsDialog.Show(this, _game.Bgm, s => _host.ApplyMapScale(s))),
            // 걷은 줄 둘 — 「게임데이터」는 도구 앱 「개발」 차림표로 옮겼고,
            // 「제독 정보」는 자택 차림표에서 여는 길이 있어 창만 남겼다.
            // 낯을 튼 여급과 그 궁합. 궁합은 초상화 번호 하나로 갈리는데 화면에서는
            // 볼 길이 없어 여기에 둔다.
            (BarmaidBookRow, () => BarmaidBookDialog.Show(this, _game)),
            // 누가 어느 도시로 가고 있는지는 지도에 배만 떠 있어 알 길이 없다.
            (PersonMoveRow, () => PersonMoveDialog.Show(this, _game)),
            // 어디에 무엇이 있는지 한눈에 — 게임 항해지도는 표식을 안 찍는다(볼트 91).
            (DiscoveryMapRow, ShowDiscoveryMap),
            // 「도구 앱」은 개발 창으로 옮겼다 — 표를 손보는 길이라 개발 쪽이 맞다.
            // 원본에 없는 편의 기능(컨디션·미니맵·바람 화살표·기능·언어·출입 일수)은 모드 창에 모아 두었다.
            ("모드", ShowModDialog),
            ("개발", ShowDevDialog));
        DockPanel.SetDock(titleBar, Dock.Top);
        shell.Children.Add(titleBar);
        shell.Children.Add(_screen);
        Content = shell;

        // 대화 상자가 떠 있는 동안은 <b>게임 화면만</b> 손을 안 받게 덮는다 —
        // 제목 줄의 최소화·최대화·닫기는 살아 있어야 오른쪽 위 단추로 게임을 끝낼 수
        // 있다(원본이 그렇다).
        // <b>시험하는 동안은 햄버거도 열어 둔다</b> — 미니게임·대사 창 위에서도 설정·개발 창을 연다.
        // 대본이 도는 중에 판을 바꾸면(개발·발견물 지도) 발견·보상이 엉뚱한 상태에 적힐 수 있고, 상자 위에
        // 상자를 열면 기다리는 고리가 겹친다. 내놓을 때는 다시 덮는다: [_screen, hamburger].
        GameWindow.Cover(this, [_screen]);

        PreviewKeyDown += OnTitleKey;   // 타이틀에서만 먹는다(그 안에서 화면을 본다)
        KeyDown += OnMapKey;            // 지도에서 V 저장

        // V 글쇠가 어느 창에서든 이 창을 찾을 수 있게 해 둔다.
        Current = this;
        Closed += (_, _) => { if (ReferenceEquals(Current, this)) Current = null; };
        input.MouseWheel += (_, e) => _host.Zoom(e.Delta > 0 ? 1 : -1, e.GetPosition(input));
        // 오른쪽 단추는 커맨드 창만 낸다. 예전에는 끌면 지도가 밀렸는데, 게임에 없는
        // 조작인 데다 커맨드를 내려다 손이 조금만 흔들려도 지도가 밀려 걷어냈다.
        input.MouseRightButtonUp += (_, e) =>
        {
            e.Handled = true;
            // 도시 안에서는 함대 커맨드 창을 안 낸다 — 도시 화면이 제 커맨드 창을 따로 낸다.
            // 물음창으로 멎어 있을 때도 안 낸다.
            if (_host.SeaBlocked || _host.Paused) return;
            // Shift 를 누른 채면 커맨드 창 대신 그 자리로 자동항해를 건다.
            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) && !_host.IsOnLand)
            {
                if (_host.MouseCell is { } at) Say(AutoSail(at.X, at.Y));
                return;
            }
            ShowCommandMenu(input, e.GetPosition(input));
        };
        input.MouseLeftButtonDown += (_, e) =>
        {
            // 도시 화면이 떠 있으면 지도는 남색 막 아래다 — 닻도 배 놓기도 받지 않는다.
            // 커맨드 창·물음창으로 멎어 있을 때도 마찬가지다. 게임이 서 있는데 손이
            // 먹으면 창 뒤에서 말이 서고 가고, 닻 소리까지 난다.
            if (_host.SeaBlocked || _host.Paused) return;
            // Ctrl 을 누른 채 찍으면 그 자리에 배를 놓는다. 시작 자리를 손으로 잡는 길인데,
            // 놀이에는 없는 것이라 모드 창에서 끌 수 있다.
            if (GameSettings.PlaceShipByCtrlClick && Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
            {
                _host.PlaceShipAt(e.GetPosition(input));
                return;
            }
            // 그냥 찍으면 닻을 내리고 그 자리에 선다. 한 번 더 찍으면 올리고 다시 간다.
            // 뭍에서도 같은 스위치로 말이 서고 다시 간다.
            // 이 클릭이 커서 조타도 깨운다(0x0048B080 이 +0x104 에 1).
            _host.SteerArmed = true;
            _host.ToggleAnchor();
            // 내릴 때도 올릴 때도 같은 소리가 난다.
            _game.Sfx?.Play(SoundBank.AnchorPart);
        };
        input.MouseMove += (_, e) => _host.SetMouse(e.GetPosition(input), true);
        // 지도를 벗어나도 "밖" 으로 두지 않는다 — 가장자리로 잘라 계속 그쪽으로 간다(SyncMouse).
        input.MouseLeave += (_, _) => SyncMouse();

        // 초점이 어디로 가는지 보려고 둔 진단(FocusWatch). 다 잡고 나면 지운다.
        FocusWatch.Sink = note => _focusNote = note;

        _statusTimer = new DispatcherTimerLite(TimeSpan.FromMilliseconds(100), () =>
        {
            SyncMouse();
            _host.ShowShip = _game.Player.Ships.Count > 0;
            _status.Text = _focusNote.Length > 0 ? $"{_host.Status}    {_focusNote}"
                                                 : _host.Status;
            // 한 틱의 차례는 원본 고리 그대로다(0x0048EF18~0x0048EF7D) —
            // 조우 → 극지 → 발견 → 도시 발견·입항 물음 → 이동·날 눈금.
            // <b>조우가 걸린 틱은 나머지를 통째로 건너뛴다</b>(0x0048EF1D).
            _folkEntered = MeetFolk();
            if (_folkEntered) return;

            // 극지방은 틱마다 본다(0x0048EF29) — 게임오버면 그 틱의 나머지는 건너뛴다.
            if (!CheckPolar()) return;
            CheckDiscovery();
            SpotCities();
            CheckPort();
            PassTime();
            MarkSeen();
            var (lat, lon) = _host.ShipLatLon;
            // 칸마다의 서식은 게임 것을 자리 수까지 그대로 옮겼다(BarFormats 참고).
            // 좌표는 <b>육분의</b>를 지녀야 보인다 — 없으면 「위도 ---′ 경도 ---′」다
            // (0x0047DCE2 가 0x0047CE20(0x22) 를 보고 0x0056BF00 을 찍는다).
            _coord.Text = _game.Player.Items.Contains(SextantItem)
                ? $"{(lat >= 0 ? "북" : "남")}위 {Math.Abs(lat),3:F0}  " +
                  $"{(lon >= 0 ? "동" : "서")}경 {Math.Abs(lon),3:F0}  "
                : "위도 ---′ 경도 ---′";
            _purse.Text = $"소지금{_game.Player.Gold,6}닢";
            _fame.Text = $"명성{_game.Player.Fame,6}";
            _tired.Text = $"피로도{_game.Player.Fatigue,4}";
            // 게임 서식 그대로다 — 「규칙%4d」(0x0056BFB0) · 「풍향: %s/풍속:%d」(0x0056BFF8) ·
            // 「해류: %s/속도:%d」(0x0056C010) · 「HP:%4d」(0x005692AC).
            _morale.Text = $"규칙{_game.Player.Morale,4}";
            _windText.Text = WindLine();
            _currentText.Text = CurrentLine();
            // 커서 쪽 길찾기 보정은 항해사(자리 1)와 나침반이 있을 때만 든다(0x0048ECEF).
            _host.PathAssist = _game.Player.MateAt(NavigatorSlot).Length > 0
                               && _game.Player.Items.Contains(CompassItem);
            _hpCell.Text = $"HP:{_game.Player.Condition,4}";
            // 게임은 뭍이면 「대원」, 바다면 「선원」이다(0x0056BEA8 의 %s).
            _crew.Text = $"{(_host.IsOnLand ? "대원" : "선원")}{_game.Player.Crew,4}명";
            _stores.Text = $"물{_game.Player.SupplyOf(SupplyKind.Water),4}통" +
                           $" 식량{_game.Player.SupplyOf(SupplyKind.Food),4}통";
            // 「남은일수」는 <b>계약 기한</b>이다 — 보급이 아니다(0x0047DEF8).
            // 계약이 없으면 게임처럼 줄을 긋는다.
            _left.Text = _game.Player.Contract is { } deal
                ? $"남은일수{deal.DaysLeftOn(_game.Player.Date),4}"
                : "남은일수----";
            // 가진 배 중 가장 큰 것이 기함이다 — 그 벌의 그림으로 그린다(게임이 안 떠 있을 때).
            // 그림은 기함 것으로 그린다 — 항구 함대편성에서 기함을 바꾸면 배 모양도 바뀐다.
            ShipSprites.Use(_game.Player.FlagshipHull?.Hull);
            _date.Text = $"{_game.Player.Date.Year,4}년{_game.Player.Date.Month,2}월{_game.Player.Date.Day,2}일";
            _cityLabel.Text = _game.Player.CityName.Length > 0 ? _game.Player.CityName : NoCity;
            _language.Text = CityLanguage();
            _rate.Text = CityRate();
            if (!_barReady && _started)
            {
                _barReady = true;
                if (_gameBar != null) _gameBar.Visibility = Visibility.Visible;
            }
            if (_overlay.IsOpen) FillOverlay(lat, lon);
            if (_vital.IsOpen) FillVital();
            if (_miniWanted) SyncMiniMap();
            SyncWeather();
            SyncSeaMusic();
        });
        Loaded += OnLoaded;

        // 창을 옮기면 그 위에 얹힌 도시 그림·커맨드 창도 같이 옮긴다 — 게임에서는 지도 안에
        // 그려진 것이라 따로 남을 수가 없다.
        // 안 선 도시도, 아직 모르는 도시도 다가가도 안 물어보고 지도에서도 지운다.
        _host.CityOpen = _game.CityVisible;

        // 지도에 남의 배를 낸다 — 누가 어디 있는지는 인물 세상이 안다.
        _host.FolkAt = FolkAfloat;

        // 자동항해가 도착하거나 막혀서 스스로 멎으면 아래 띠로 알린다.
        _host.AutoSailEnded += Say;
        Closed += (_, _) => _host.AutoSailEnded -= Say;

        GameUi.CarryOwnedWindows(this);

        // 창이 물러나거나 접히면 좌표 상자도 같이 감춘다 — 제 창이라 그냥 두면 남의 앱 위에 뜬다.
        Activated += (_, _) => SyncOverlay();
        Deactivated += (_, _) =>
        {
            SyncOverlay();
            FocusWatch.After("지도창 초점 잃음");
        };
        StateChanged += (_, _) => SyncOverlay();

        // 육상전·일기토가 열려도 비·눈이 그친다(0x0044AA31 · 0x004AA87F) — 발견 대본의 육상전이나
        // 바다 조우의 일기토는 이 창 밖에서 열리므로 판 쪽이 알린다.
        Action endWeather = () => Dispatcher.Invoke(EndWeather);
        LandBattleScene.Opening += endWeather;
        DuelDialog.Opening += endWeather;
        Closed += (_, _) =>
        {
            LandBattleScene.Opening -= endWeather;
            DuelDialog.Opening -= endWeather;
            _overlay.IsOpen = false;
            _statusTimer.Stop();
            _game.Close();
            FocusWatch.Sink = null;   // 진단 — 다 잡고 나면 지운다
        };
    }

    /// <summary>
    /// 지도가 화면에서 차지한 자리(WPF 단위). 도시 화면이 이 자리를 통째로 덮는다 —
    /// 게임도 도시에 들어가면 지도 영역이 남색으로 덮인다.
    /// </summary>
    /// <summary>
    /// 발견물 지도를 연다 — 온 지도를 밝힌 양피지 위에 발견물 자리와 내 자리를 찍는다.
    /// </summary>
    /// <remarks>
    /// 바탕은 항해지도를 짓는 손을 그대로 쓰되 <b>다 밝힌 지도</b>로 부른다. 놀이에는 없는
    /// 창이라 햄버거 차림표에 둔다 — 원본 항해지도는 표식을 하나도 안 찍는다.
    /// </remarks>
    /// <summary>햄버거의 발견물 지도 줄 이름. 모드 창이 이 줄을 켜고 끈다.</summary>
    internal const string DiscoveryMapRow = "발견물 지도";

    /// <summary>햄버거의 여급 수첩 줄 이름. 모드 창이 이 줄을 켜고 끈다.</summary>
    internal const string BarmaidBookRow = "여급 수첩";

    /// <summary>햄버거의 인물 이동 줄 이름. 모드 창이 이 줄을 켜고 끈다.</summary>
    internal const string PersonMoveRow = "인물 이동";

    private void ShowDiscoveryMap()
    {
        var all = new ExploredMap();
        all.RevealAll();

        var chart = _host.Chart(all, out int w, out int h);
        if (chart == null) { NoticeDialog.Show(this, "지도를 아직 못 읽었습니다"); return; }

        var at = _host.ShipCell is { } cell ? ((double, double)?)(cell.CellX, cell.CellY) : null;
        DiscoveryMapDialog.Show(this, chart, w, h, _game.Discoveries?.Table, _game.Player, at,
                                WindTable.Open(_game.Directory), WarpTo, AutoSail);
    }

    /// <summary>
    /// 발견물 지도에서 오른쪽 단추로 짚은 칸으로 함대를 옮긴다. 옮긴 칸을 돌려준다.
    /// </summary>
    /// <remarks>
    /// 도시 안이거나 뭍에 올라 있으면 안 옮긴다 — 그 자리에서 배만 바다로 빼면 상륙·입항
    /// 상태가 어긋난다. 바다에서는 가까운 물칸에 <b>닻을 내린 채</b> 선다(PlaceAtSea).
    /// </remarks>
    private (double X, double Y)? WarpTo(double cellX, double cellY)
    {
        if (_host.SeaBlocked || _host.IsOnLand) return null;
        if (!_host.PlaceAtSea(cellX, cellY)) return null;
        return _host.ShipCell is { } cell ? (cell.X, cell.Y) : null;
    }

    /// <summary>
    /// 그 칸까지 자동항해를 건다 — 지도 클릭(주 지도 Shift+오른쪽 클릭 · 발견물지도
    /// Shift+오른쪽 클릭)과 도시 이름 고르기(<see cref="AutoSailDialog"/>)가 같이 쓴다.
    /// </summary>
    private string AutoSail(double cellX, double cellY)
    {
        var (ok, message) = _host.StartAutoSail(cellX, cellY);
        return message;
    }

    /// <summary>도시 이름으로 자동항해 목적지를 고르는 창을 연다 — 지금 아는 도시만 나온다.</summary>
    private void ShowAutoSailDialog()
    {
        var cities = Enumerable.Range(0, GameMapCoords.CityCount)
            .Where(id => _game.CityVisible(id))
            .Select(id => (Id: id, Name: _game.CityName(id)));

        AutoSailDialog.Show(this, cities, id =>
        {
            if (!GameMapCoords.TryCityCell(id, out double cx, out double cy))
                return (false, "그 도시의 좌표를 모릅니다");
            var (ok, message) = _host.StartAutoSail(cx, cy);
            if (ok) Say(message);
            return (ok, message);
        });
    }

    /// <summary>
    /// 도구 앱(Editor.exe)을 띄운다. 이미 떠 있으면 그 창을 앞으로 부른다.
    /// </summary>
    /// <remarks>
    /// 게임은 그대로 돈다 — 딴 프로세스라 여기서 멎게 할 까닭이 없다. 다만 도구에서 표를
    /// 고쳐도 이미 읽어 둔 표는 그대로다. 게임을 껐다 켜야 새 값이 먹는다.
    /// </remarks>
    private void RunHelperApp()
    {
        if (!HelperApp.Run())
            NoticeDialog.Show(this, $"도구 앱을 띄우지 못했습니다.\n{HelperApp.LastError}");
    }

    /// <summary>
    /// 놀이 끝 화면을 연다. 게임 화면만 덮고(제목 줄·위아래 띠는 남는다) 곡을 8번으로 바꾼다.
    /// </summary>
    private bool GameOver(int picture = GameOverDialog.MutinyLost) =>
        GameOverDialog.Show(this, _game.EventStills, picture, MapAreaOnScreen(), _game.Bgm);

    private Rect MapAreaOnScreen()
    {
        var source = PresentationSource.FromVisual(this);
        if (source == null) return default;

        // 지도를 막 갈아 끼운 참이면 아직 자리를 안 잡았을 수 있다. 그때 빈 자리를 내면
        // 도시 창이 제 크기를 못 잡는다 — 한 번 재워 두고 다시 본다.
        //
        // <b>크기만 봐서는 모자란다.</b> WPF 는 <c>Content</c> 를 갈아 끼워도 그 아래
        // 것들을 <b>다음 자리잡기 때</b> 트리에 붙인다. 앞서 한 번 떠 있었던 지도는
        // 크기가 남아 있어서 이 검사를 그냥 지나치는데, 그 참에 <c>PointToScreen</c> 을
        // 부르면 "이 Visual이 PresentationSource에 연결되지 않았습니다" 로 터진다 —
        // 타이틀로 돌아갔다가 NEW GAME 을 다시 고르면 늘 이 자리였다.
        if (!Ready(_input)) UpdateLayout();
        if (!Ready(_input)) return default;

        // PointToScreen 은 실픽셀을 내므로 WPF 단위로 되돌린다(고해상도 화면에서 어긋난다).
        var device = _input.PointToScreen(new Point(0, 0));
        var topLeft = source.CompositionTarget.TransformFromDevice.Transform(device);
        return new Rect(topLeft.X, topLeft.Y, _input.ActualWidth, _input.ActualHeight);
    }

    /// <summary>
    /// 지도 위에 사건 애니메이션 한 장면을 돌린다 — 게임의 <c>0x0048E820(장면)</c>.
    /// </summary>
    /// <remarks>
    /// 게임 한 점은 지도가 구름을 그리는 배율(<see cref="ShipMapHost.GamePixelScale"/>)대로 잡는다 —
    /// 그래야 덤불이 배 그림(48점) 곁에 제 크기로 선다. 너무 멀리 보거나 가까이 보면 장면이
    /// 티끌만 하거나 지도를 넘치므로, 지도 폭이 게임 점 320~1280 사이가 되게 묶는다(원본 640).
    /// </remarks>
    internal void PlayEventScene(int scene)
    {
        var area = MapAreaOnScreen();
        var (pixelW, _) = _host.SurfaceSize;
        if (area.Width <= 0 || pixelW <= 0) return;

        double perPixel = area.Width / pixelW;                  // 실픽셀 → WPF 단위
        double scale = Math.Clamp(_host.GamePixelScale * perPixel, area.Width / 1280, area.Width / 320);
        Point? ship = _host.ShipOnSurface is { } p ? new Point(p.X * perPixel, p.Y * perPixel) : null;
        EventAnimationPopup.Play(this, _game, scene, area, scale, ship);
    }

    /// <summary>트리에 붙었고 자리도 잡았는가 — <c>PointToScreen</c> 을 부르기 전에 본다.</summary>
    private static bool Ready(FrameworkElement element) =>
        PresentationSource.FromVisual(element) != null
        && element.ActualWidth > 0 && element.ActualHeight > 0;

    /// <summary>요소 안의 한 자리를 화면 좌표(WPF 단위)로 옮긴다.</summary>
    private Point ToScreen(FrameworkElement element, Point at)
    {
        var device = element.PointToScreen(at);
        var source = PresentationSource.FromVisual(this);
        return source == null
            ? device
            : source.CompositionTarget.TransformFromDevice.Transform(device);
    }

    /// <summary>
    /// 도시 화면을 여닫는다. 들어가 있는 동안은 바다 명령이 전부 막힌다 —
    /// 막는 일 자체는 <see cref="ShipMapHost.SeaBlocked"/> 가 하고, 여기서는 그 김에
    /// 조작 줄 단추도 흐려 둔다. 눌러도 안 먹는 단추가 멀쩡해 보이면 헷갈린다.
    /// </summary>
    private void SetInCity(bool on)
    {
        _host.InCity = on;
    }

    /// <summary>
    /// 상단 띠의 <b>언어</b> 칸 — 들어와 있는 도시가 쓰는 말이다.
    /// </summary>
    /// <remarks>
    /// 게임 자리는 <c>0x0047DE39</c> 다. 도시 번호가 0~225 밖이면(바다에 있으면) 이름 대신
    /// 줄표(<c>0x0056BF58</c>)를 낸다. 말 번호는 도시가 딸린 나라의 것이고
    /// (<see cref="CityExeTable.NationOf"/>), 이름표는 <c>0x00560A48</c> 이다.
    /// </remarks>
    private string CityLanguage()
    {
        int city = _game.Player.CityId;
        if (city < 0 || _game.CityRows == null || _game.Nations == null) return NoValue;

        int nation = _game.CityRows.NationOf(city);
        var names = _game.Buildings?.LanguageNames;
        if (names == null || _game.Nations.Find(nation) is not { } row) return NoValue;

        return row.Language >= 0 && row.Language < names.Count ? names[row.Language] : NoValue;
    }

    /// <summary>
    /// 상단 띠의 <b>시세</b> 칸. 게임 서식은 <c>"시세%4d%"</c> 고, 도시 밖에서는
    /// <c>"시세 ---%"</c> 다(<c>0x0047DE94</c>).
    /// </summary>
    private string CityRate()
    {
        int city = _game.Player.CityId;
        return city < 0 ? "시세 ---%" : $"시세{_game.Rates.Of(city),4}%";
    }

    /// <summary>도시 밖일 때 언어 칸에 나오는 줄표(<c>0x0056BF58</c>, 열여덟 개).</summary>
    private const string NoValue = "------------------";

    /// <summary>도시 밖일 때 도시명 칸에 나오는 줄표(<c>0x0056BF40</c>, 열아홉 개).</summary>
    private const string NoCity = "-------------------";

    /// <summary>
    /// 상단 띠 좌표 칸에 드는 <b>육분의</b>(아이템 <c>0x22</c> = 34) · 자동 경로에 드는
    /// <b>나침반</b>(<c>0x21</c> = 33) · 도시 발견 반지름을 넓히는 아이템(<c>0x23</c> = 35).
    /// </summary>
    private const int CompassItem = 0x21, SextantItem = 0x22, SpyglassItem = 0x23;

    /// <summary>망원경을 지녔을 때 도시 발견 반지름에 얹는 칸 수(<c>0x0048D84A</c>).</summary>
    private const int SpotWithGlass = 2;

    /// <summary>도시정보 창. 상단 띠 밑에 붙여 띄운다.</summary>
    private GameMenuHost? _infoMenuHost;

    private GameMenuHost InfoMenu
    {
        get
        {
            if (_infoMenuHost != null) return _infoMenuHost;
            _infoMenuHost = new GameMenuHost(this);
            // 바다에서 여는 편집 창 때문에 멈춤을 잡아 둔다 — 도시·뭍에서는 이미 서 있어 뜻이 없다.
            _infoMenuHost.Closed += () =>
            {
                if (_asking || _host.SeaBlocked) return;
                _host.Paused = false;
            };
            return _infoMenuHost;
        }
    }

    /// <summary>
    /// 상단 띠를 오른쪽 단추로 눌렀을 때 — 띠에 <b>무엇을 띄울지</b> 켜고 끄는 창이다.
    /// </summary>
    /// <remarks>
    /// 게임은 도시 안에서만 이 창을 내지만 우리는 <b>바다에서도</b> 낸다 — 띠는 어디서나
    /// 서 있는데 바다에서만 못 고치면 칸을 켜려고 도시에 들어가야 한다. 바다에서는 창이
    /// 떠 있는 동안 배를 세운다.
    /// </remarks>
    private void ShowCityInfoMenu(FrameworkElement bar, Point at)
    {
        if (InfoMenu.IsOpen) { InfoMenu.Focus(); return; }

        InfoMenu.Open(BuildCityInfo, ToScreen(bar, new Point(at.X, bar.ActualHeight)));
        if (!_host.InCity && !_host.IsOnLand) _host.Paused = true;
    }

    /// <summary>
    /// 도시정보 창의 지금 모습. 줄을 하나 뒤집을 때마다 다시 지어 갈아 끼운다 —
    /// <c>:ON</c>·<c>:OFF</c> 글자는 게임 글꼴로 찍은 그림이라 고쳐 쓸 수가 없다.
    /// </summary>
    private GameMenu BuildCityInfo() => CityInfoMenu.Build(
        name => _infoCells.TryGetValue(name, out var cell)
            ? cell.Visibility == Visibility.Visible
            : null,
        name =>
        {
            var cell = _infoCells[name];
            cell.Visibility = cell.Visibility == Visibility.Visible
                ? Visibility.Collapsed
                : Visibility.Visible;
            SaveBarCells();
            InfoMenu.Refresh();
        },
        InfoMenu.Close);

    /// <summary>
    /// 커서가 지금 지도 위 어디에 있는지 다시 잰다.
    /// </summary>
    /// <remarks>
    /// 예전에는 <c>MouseMove</c> 로만 알려 줬다. 그러다 보니 창(입항 물음·도시 그림)이 떴다
    /// 닫히거나 커서가 잠깐 지도를 벗어나면 <c>MouseLeave</c> 가 "밖" 으로 표시해 놓고,
    /// 커서를 <b>움직이기 전까지</b> 그대로였다 — 배가 뱃머리를 못 잡고 그 자리에 서 있었다.
    /// 입항 직후에는 뱃머리가 0 이라 특히 티가 났다. 그래서 틱마다 직접 재 둔다.
    ///
    /// <b>커서가 창 밖이어도 그쪽으로 간다</b> — 원본이 그렇다. 원본은 마우스를 붙들지 않고
    /// 틱마다 <c>GetCursorPos</c> → <c>ScreenToClient</c> 로 읽어 창 가장자리로 자른다
    /// (<c>0x004BA427</c>, 항해 고리 <c>0x0048BA84</c>). 그래서 창 밖 커서는 가장 가까운 가장자리
    /// 점을 가리킨 셈이 된다. 우리도 화면 좌표를 읽어 지도 크기로 자른다. 다른 창이 앞에
    /// 있을 때만은 끈다 — 딴 프로그램을 쓰는 동안 배가 돌면 곤란하다.
    /// </remarks>
    private void SyncMouse()
    {
        if (!_started || !ReferenceEquals(_screen.Content, _mapRoot) || _input.ActualWidth <= 0) return;

        if (!IsActive || !GetCursorPos(out var screen))
        {
            _host.SetMouse(default, false);
            return;
        }

        var p = _input.PointFromScreen(new Point(screen.X, screen.Y));
        p = new Point(Math.Clamp(p.X, 0, _input.ActualWidth - 1),
                      Math.Clamp(p.Y, 0, _input.ActualHeight - 1));
        _host.SetMouse(p, true);
    }

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct NativePoint { public int X, Y; }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out NativePoint point);

    /// <summary>
    /// 해상에서 자리에 맞는 곡으로 갈아탄다. <b>지금 곡이 끝난 뒤에</b> 바뀐다 —
    /// 게임도 이 갈래에서만 그렇게 한다(<see cref="BgmPlayer.PlayWhenDone"/>).
    /// </summary>
    /// <remarks>
    /// 도시에 들어가 있거나 뭍에 올라 있으면 손대지 않는다 — 그때는 도시 곡·말 곡이 돈다.
    /// 멈춰 있을 때(커맨드 창)도 그대로 둔다.
    /// </remarks>
    private void SyncSeaMusic()
    {
        if (!_started || _host.SeaBlocked || _host.IsOnLand || _host.Paused) return;
        if (_host.ShipCell is not { } cell) return;

        _game.Bgm.PlayWhenDone(BgmPlayer.SeaTrackAt(cell.X, cell.Y));
    }

    /// <summary>좌표 상자를 띄울 때인지 다시 따진다 — 켜 두었고, 지도가 떠 있고, 이 창이 앞일 때만.</summary>
    /// <remarks>
    /// <b>컨디션만은 도시에 들어가도 그대로 둔다.</b> 다른 상자는 지도를 읽는 것이라 지도가
    /// 안 보이면 뜻이 없지만, 컨디션은 도시에서 쉬거나 다치는 동안에도 봐야 하는 값이다.
    /// </remarks>
    private void SyncOverlay()
    {
        // 지도가 화면에 걸려 있는가. 도시 창은 딴 창이라 이 값은 그대로 참이다.
        bool up = _started && WindowState != WindowState.Minimized
                  && ReferenceEquals(_screen.Content, _mapRoot);
        bool room = up && IsActive;
        _overlay.IsOpen = _overlayWanted && room;

        bool people = _peopleWanted && room;
        if (people) FillPeople();
        _people.IsOpen = people;

        bool vital = _vitalWanted && up;
        if (vital) FillVital();
        _vital.IsOpen = vital;

        SyncMiniMap();
    }

    /// <summary>
    /// 만난 사람 상자를 채운다 — 왼쪽에 여급(친밀도·궁합), 오른쪽에 만난 인물이다.
    /// </summary>
    /// <remarks>
    /// 궁합은 여급의 운명 얼굴 코드와 내 것을 견주어 가른다(<see cref="BarmaidTable.Destined"/>) —
    /// 같거나 하나 차이면 맞는 것이다. 친밀도는 말을 걸어야 생기므로, 여기 뜨는 여급은
    /// <b>한 번이라도 말을 걸어 본</b> 이들이다.
    /// </remarks>
    private void FillPeople()
    {
        var player = _game.Player;
        var left = new List<string> { "여급 (친밀도 · 궁합)" };

        int mine = Engine.Town.Barmaids.FortuneOf(player);
        var table = _game.Barmaids;
        foreach (var (id, liking) in player.Liking.OrderByDescending(p => p.Value))
        {
            string name = table?.Find(id)?.Name ?? $"{id}번";
            string fit = table?.Find(id) is { } her
                ? BarmaidTable.Destined(mine, her.Fortune) ? "궁합 ○" : "궁합 ×"
                : "";
            left.Add($"  {PadCells(name, 16)}{liking,4}  {fit}");
        }
        if (left.Count == 1) left.Add("  아직 말을 걸어 본 여급이 없다");

        var right = new List<string> { "만난 인물" };
        foreach (string name in player.Met.OrderBy(n => n, StringComparer.Ordinal))
            right.Add($"  {name}");
        if (right.Count == 1) right.Add("  아직 만난 인물이 없다");

        var lines = new List<string>();
        for (int i = 0; i < Math.Max(left.Count, right.Count); i++)
            lines.Add(PadCells(i < left.Count ? left[i] : "", PeopleColumn)
                      + (i < right.Count ? right[i] : ""));

        _peopleText.Text = string.Join(Environment.NewLine, lines);
    }

    /// <summary>바다에서 하루 — 비·눈을 굴린다(<see cref="SeaWeather"/>). 비가 오면 빗소리를 되풀이한다.</summary>
    private void RollWeather()
    {
        var (lat, lon) = _host.ShipLatLon;
        int latRaw = (int)((90 - lat) / 180 * 20000), lonRaw = (int)((lon + 180) / 360 * 40000);
        _weatherWind ??= WindTable.Open(_game.Directory);
        int zone = _weatherWind?.ZoneAt(WindTable.CellOf(lonRaw, latRaw)) ?? -1;
        if (_seaWeather.Roll(zone, _game.Player.Date.Month, latRaw, _game.Random) is not { } now) return;

        if (now == SeaWeather.Kind.None)
        {
            _weatherView.Stop();
            _game.Sfx?.StopLoop();          // 0x00422A40(0x3F, 3)
            return;
        }
        _weatherView.Start(now);
        if (now == SeaWeather.Kind.Rain) _game.Sfx?.PlayLoop(RainSoundPart);
    }

    /// <summary>비·눈을 곧바로 거둔다 — 입항·해전처럼 판이 바뀌는 자리(<c>0x0048EACA</c> · <c>0x00443822</c>).</summary>
    private void EndWeather()
    {
        _seaWeather.Stop();
        _weatherView.Clear();
        _game.Sfx?.StopLoop();
    }

    /// <summary>
    /// 비·눈 층을 한 걸음 옮기고 띄울지 따진다 — 0.1초마다. 지도가 앞이고 오는 것이 있을 때만 뜬다.
    /// 사건 애니메이션이 도는 동안에는 원본도 안 그린다(<c>0x0048A9F0</c>) — 그 창이 앞이면 여기도 숨는다.
    /// </summary>
    private void SyncWeather()
    {
        bool show = _weatherView.Busy && _started && IsActive
                    && WindowState != WindowState.Minimized
                    && ReferenceEquals(_screen.Content, _mapRoot)
                    && _input.ActualWidth > 0 && _input.ActualHeight > 0;
        if (show)
        {
            if (!_weatherLoaded) { _weatherView.Load(_game.EventAnims); _weatherLoaded = true; }
            var (pixelW, _) = _host.SurfaceSize;
            double w = _input.ActualWidth, h = _input.ActualHeight;
            double perPixel = pixelW > 0 ? w / pixelW : 1;
            double scale = Math.Clamp(_host.GamePixelScale * perPixel, w / 1280, w / 320);
            _weatherView.Resize(w, h, scale);
            _weatherView.Step();
        }
        _weatherPopup.IsOpen = show;
    }

    /// <summary>
    /// 미니맵을 띄울 때인지 따지고 배 자리로 옮긴다 — 켜 두었고, 지도가 앞이고, <b>도시 밖</b>(항해·뭍 이동)일 때만.
    /// </summary>
    /// <remarks>
    /// <b>이벤트 중에는 안 뜬다</b> — 발견물·바다 사건·도시 물음처럼 배를 세우거나(<c>Paused</c>) 물음을
    /// 걸었거나(<c>_asking</c>) 지도 창 위에 딴 창이 떠 있으면 숨긴다. 이벤트 창이 연달아 뜨면 그 사이에
    /// 지도 창이 잠깐 앞으로 와 켜졌다 꺼졌다 깜빡였으므로, 다시 띄우는 것은 <b>조용한 채로
    /// <see cref="MiniCalm"/> 가 지난 뒤</b>다. 숨기는 것은 곧바로 한다.
    /// </remarks>
    private void SyncMiniMap()
    {
        bool calm = !_asking && !_host.Paused
                    && !OwnedWindows.Cast<Window>().Any(w => w.IsVisible);
        var now = DateTime.UtcNow;
        if (!calm) _miniCalmSince = null;
        else _miniCalmSince ??= now;
        bool settled = _miniCalmSince is { } since && now - since >= MiniCalm;

        bool room = _miniWanted && _started && IsActive && settled
                    && WindowState != WindowState.Minimized
                    && ReferenceEquals(_screen.Content, _mapRoot)
                    && !_host.SeaBlocked
                    && _host.ShipCell is not null;
        if (room && !_mini.HasChart)
        {
            var all = new ExploredMap();
            all.RevealAll();
            if (_host.Chart(all, out int w, out int h) is { } chart) _mini.SetChart(chart, w, h);
            else room = false;
        }

        if (room && _host.ShipCell is { } cell)
        {
            _mini.Update(_game.Discoveries?.Table, _game.Player, cell.CellX, cell.CellY);
            _miniPopup.HorizontalOffset = Math.Max(0, _input.ActualWidth - MiniMapView.ViewW - 10);
            _miniPopup.VerticalOffset = Math.Max(0, _input.ActualHeight - MiniMapView.ViewH - 10);
        }
        _miniPopup.IsOpen = room;
    }

    /// <summary>
    /// 컨디션 상자를 채운다 — 「컨디션 1520 / 2000 · 괜찮음」과 막대. 막대에는 300·100 문턱을 금으로 긋는다
    /// (<see cref="Vitality.Pale"/> · <see cref="Vitality.Faint"/>).
    /// </summary>
    private void FillVital()
    {
        int hp = _game.Player.Condition;
        string state = hp <= 0 ? "쓰러짐" : hp < Vitality.Faint ? "위험" : hp < Vitality.Pale ? "창백" : "괜찮음";
        _vitalText.Text = $"컨디션 {hp,4} / {Player.ConditionMax} · {state}";

        var fill = hp < Vitality.Faint ? Color.FromRgb(0xD0, 0x40, 0x30)
                 : hp < Vitality.Pale ? Color.FromRgb(0xE0, 0xA0, 0x30)
                 : Color.FromRgb(0x50, 0xB0, 0x60);
        double Scale(int v) => VitalBarWidth * Math.Clamp(v, 0, Player.ConditionMax) / Player.ConditionMax;

        _vitalBar.Children.Clear();
        _vitalBar.Children.Add(new System.Windows.Shapes.Rectangle { Width = VitalBarWidth, Height = 10, Fill = new SolidColorBrush(Color.FromArgb(0x60, 0xFF, 0xFF, 0xFF)) });
        _vitalBar.Children.Add(new System.Windows.Shapes.Rectangle { Width = Scale(hp), Height = 10, Fill = new SolidColorBrush(fill) });
        foreach (int mark in new[] { Vitality.Faint, Vitality.Pale })
        {
            var tick = new System.Windows.Shapes.Rectangle { Width = 1, Height = 10, Fill = Brushes.Gold };
            Canvas.SetLeft(tick, Scale(mark));
            _vitalBar.Children.Add(tick);
        }
    }

    /// <summary>여급 칸의 너비(글자 칸). 한글 한 자를 두 칸으로 센다.</summary>
    private const int PeopleColumn = 34;

    /// <summary>한글을 두 칸으로 세어 그 칸 수만큼 빈칸을 채운다.</summary>
    private static string PadCells(string text, int cells)
    {
        int used = 0;
        foreach (char c in text) used += c < 0x80 ? 1 : 2;
        return used >= cells ? text : text + new string(' ', cells - used);
    }

    /// <summary>
    /// 좌표 상자를 채운다. 배가 선 칸을 WORLD.CDS 파일 안의 자리까지 풀어서 보여 주고,
    /// <b>타일 번호 뒤에는 그 타일 그림</b>을 한 장 끼워 넣는다 — 번호만 봐서는 어떤 칸인지
    /// 알 수가 없어서다.
    /// </summary>
    private void FillOverlay(double lat, double lon)
    {
        _overlayText.Inlines.Clear();

        var c = _host.ShipCell;
        if (c == null) { _overlayText.Inlines.Add(new Run("배가 아직 지도에 없습니다")); return; }

        var v = c.Value;
        _overlayText.Inlines.Add(new Run(
            $"칸        {v.X,7:F1}, {v.Y,6:F1}   (칸 {v.CellX}, {v.CellY})\n" +
            $"WORLD.CDS 행 {v.Row,4} · 열 {v.Col,4} · 0x{v.Offset:X5}\n" +
            $"칸 값     지형 {v.Terrain,3} · 속성 {v.Attr,3} · 타일 {v.Tile,5} "));

        if (TileImage(v.Tile) is { } tile)
            _overlayText.Inlines.Add(new InlineUIContainer(tile)
            {
                BaselineAlignment = BaselineAlignment.Center,
            });

        var lines = new List<string>
        {
            $" · 육지 {v.LandRatio * 100,3:F0}%",
            $"위경도    {(lat >= 0 ? "북위" : "남위")} {Math.Abs(lat):F2} · {(lon >= 0 ? "동경" : "서경")} {Math.Abs(lon):F2}",
        };

        lines.AddRange(SpeedLines());

        // 40칸까지만 본다. 더 넓히면 도시마다 항구 칸을 찾느라 100ms 틱이 무거워진다.
        var (city, cells) = _host.NearestDock(40);
        if (city >= 0) lines.Add($"가까운 항구 [{_game.CityName(city)}] {cells:F1}칸");

        var m = _host.MouseCell;
        if (m != null)
            lines.Add($"커서      {m.Value.X,7:F1}, {m.Value.Y,6:F1}   0x{m.Value.Offset:X5}");

        _overlayText.Inlines.Add(new Run(string.Join("\n", lines)));
    }

    /// <summary>
    /// 좌표 상자의 속도 줄 — 바람 · 돛 · 함대 속도 · 해류를 한 줄씩 낸다.
    /// </summary>
    /// <remarks>
    /// 게임과 속도가 어긋날 때 <b>어디서 어긋나는지</b>를 보려고 둔 것이다. 셈은
    /// <see cref="Sailing.SpeedOf"/> 가 하고(게임 <c>0x0048BCF0</c>), 여기서는 그 셈에
    /// 들어간 값들을 그대로 늘어놓는다.
    /// <code>
    ///   속도   = 추진력 x (풍속 + 1) x 돛효율 / 100      (배마다)
    ///   함대   = (기함 + 배들 평균) / 2
    ///   한 틱  = 빠른 칸이면 9 x 속도 / 10, 아니면 (3 x 속도 + 54) / 10, 둘 다 / 64
    /// </code>
    /// 상대각은 <b>0 이 정순풍, 8 이 정면 역풍</b>이다. 돛효율은 기함 것이다 — 배마다
    /// 다르지만 한 줄에 다 적을 수는 없다.
    /// </remarks>
    private IEnumerable<string> SpeedLines()
    {
        var (dir, speed, relative) = _host.LastWind;
        string where = ShipMapHost.Compass[(dir & 0xF) >> 1];

        var ships = _game.Player.Ships;
        var flag = ships.Count > 0
            ? ships[Math.Clamp(_game.Player.Flagship, 0, ships.Count - 1)] : null;
        int sail = flag != null && _game.Sails is { } table
            ? table.Efficiency(flag.Sails, relative) : 0;

        yield return $"바람      {where} {speed}  · 상대각 {relative,2} (0 순풍 · 8 역풍)"
                   + $" · 돛효율 {sail,3}%";

        string ground = _host.LastFast ? "빠른 칸" : "느린 칸";
        yield return $"속도      함대 {_host.LastSpeed,3} · 한 틱 {_host.LastStep:F3}칸 · {ground}"
                   + (flag != null ? $" · 기함 추진력 {flag.Speed}" : "");

        var flow = _host.LastFlow;
        yield return flow.Speed > 0
            ? $"해류      {ShipMapHost.Compass[(flow.Dir & 0xF) >> 1]} {flow.Speed}"
            : "해류      없음(느린 칸에서는 안 받는다)";
    }

    /// <summary>좌표 상자에 끼우는 타일 그림의 배율. 글줄 높이에 맞춰 두 배로 키운다.</summary>
    private const int OverlayTileScale = 2;

    private int _overlayTile = -1;
    private BitmapSource? _overlayTileBitmap;

    /// <summary>
    /// 타일 번호의 그림. <b>그림판만 담아 두고 <see cref="Image"/> 는 부를 때마다 새로 짓는다</b> —
    /// 한 번 만든 것을 계속 끼우면 앞서 끼운 <see cref="InlineUIContainer"/> 에 아직 매여 있어
    /// "이미 다른 요소의 논리 자식입니다" 로 터진다. 그림판은 얼려 두었으니 나눠 써도 된다.
    /// </summary>
    private Image? TileImage(int tile)
    {
        if (tile < 0 || tile >= OceanTiles.TileCount) return null;

        if (tile != _overlayTile)
        {
            var ocean = OceanTiles.LoadFromDirectory(_game.Directory);
            if (ocean == null) return null;

            int w = OceanTiles.TileW;
            var pixels = new uint[w * w];
            for (int i = 0; i < pixels.Length; i++)
                pixels[i] = 0xFF000000u
                            | (uint)ocean.PaletteRgb[ocean.TileData[tile * OceanTiles.TilePixels + i]];

            var made = BitmapSource.Create(w, w, 96, 96, PixelFormats.Bgra32, null, pixels, w * 4);
            made.Freeze();
            _overlayTileBitmap = made;
            _overlayTile = tile;
        }
        if (_overlayTileBitmap == null) return null;

        int side = OceanTiles.TileW * OverlayTileScale;
        var image = new Image
        {
            Source = _overlayTileBitmap,
            Width = side,
            Height = side,
            Margin = new Thickness(1, 0, 1, 0),
            IsHitTestVisible = false,
        };
        RenderOptions.SetBitmapScalingMode(image, GameUi.SpriteScaling);
        RenderOptions.SetEdgeMode(image, EdgeMode.Aliased);
        return image;
    }

    /// <summary>
    /// 게임 첫 화면을 흉내낸 타이틀. 무늬를 깐 바탕 한가운데에 커맨드 창처럼 생긴 메뉴 상자를 둔다.
    /// </summary>
    private FrameworkElement BuildTitleScreen()
    {
        var items = new StackPanel();

        // 제목 상자는 게임 원본 조각(MISC.CDS)으로 짓는다. 못 읽으면 민색 상자로 물러선다.
        // 한 번 넣어 두면 제목 줄이 있는 창들이 다 같이 쓴다(GameUi.TitleBar).
        LoadSprites();

        FrameworkElement? handle = GameUi.TitleFrame(GameUi.Sprites, "메인메뉴");
        handle ??= new Border
        {
            Background = MenuBack,
            BorderBrush = MenuEdge,
            BorderThickness = new Thickness(2),
            Padding = new Thickness(18, 2, 18, 2),
            Child = new TextBlock
            {
                Text = "메인메뉴",
                Foreground = MenuTitleFg,
                FontWeight = FontWeights.Bold,
                FontSize = 13,
                HorizontalAlignment = HorizontalAlignment.Center,
            },
        };
        // 게임 메인메뉴는 제목 띠와 첫 줄이 맞붙어 있다 — 사이를 띄우지 않는다.
        items.Children.Add(handle);
        _titleFocus = new GameUi.FocusGroup();
        items.Children.Add(TitleMenuItem("NEW GAME", NewGame));

        // 적어 둔 판이 있을 때만 고를 수 있다. 빈 새 설치에서는 게임처럼 흐리게 낸다.
        items.Children.Add(TitleMenuItem("LOAD GAME",
            System.IO.File.Exists(Engine.GameSave.Path) ? () =>
        {
            // 게임도 제목 띠를 얹는다 — 0x00571A78 "게임 로드" · 0x00571A88 본문.
            if (!ConfirmDialog.Ask(this, "마지막에 저장한 데이터를 로드합니다", "게임 로드")) return;
            if (!System.IO.File.Exists(Engine.GameSave.Path))
            {
                // 0x005723F8 「저장 데이터 · 파일 이름 · 발견되지 않습니다」 — 제목은 「에러」다.
                NoticeDialog.Show(this, $"저장 데이터{Environment.NewLine}{Engine.GameSave.Path}"
                                        + $"{Environment.NewLine}가 발견되지 않습니다", "에러");
                return;
            }
            StartMap(fresh: false);
        } : null));
        // CONTINUE — 원본에 없는 줄이다. 입항 자동저장(모드 창)이 적어 둔 파일을 연다.
        // 적어 둔 것이 없으면 <b>줄이 흐리다</b>(눌러도 안 먹는다) — LOAD GAME 과 달리
        // 원본에 없는 줄이라 「없습니다」를 띄울 자리가 아니다.
        items.Children.Add(TitleMenuItem("CONTINUE",
            System.IO.File.Exists(Engine.GameSave.AutoPath)
                ? () =>
                {
                    if (!ConfirmDialog.Ask(this, "자동저장한 데이터를 로드합니다", "게임 로드")) return;
                    StartMap(fresh: false, auto: true);
                }
                : null));
        items.Children.Add(TitleMenuItem("MINI GAME", MiniGames));
        items.Children.Add(TitleMenuItem("END GAME", Close));
        // 사운드테스트 줄은 <b>헬퍼의 「도구」로 옮겼다</b>(0x00571BE8 — 원본 타이틀에는 있다).
        // 놀이를 시작하는 자리에 시험 줄이 섞여 있는 것이 거슬린다는 뜻이었다.

        var box = new Border
        {
            Background = MenuBack,
            BorderBrush = TitleBoxEdge,
            BorderThickness = new Thickness(1),
            Padding = new Thickness(6),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Child = items,
        };
        _titleMenuBox = box;
        // 게임 메뉴처럼 제목 띠를 잡아 옮길 수 있게 한다. 가운데 놓는 것은 그대로 두고
        // 옮긴 만큼만 얹으므로, 창 크기가 바뀌어도 가운데가 기준이 된다.
        var move = new TranslateTransform(_titleMenuOffset.X, _titleMenuOffset.Y);
        box.RenderTransform = move;

        var middle = new Grid { Background = TitleBackground(), Children = { box } };

        // 게임 메인메뉴는 화면 한가운데보다 조금 위에 앉는다. 아래에 빈칸을 두면 가운데맞춤이
        // 그만큼 올라가는데, 올라가는 것은 빈칸의 <b>절반</b>이라 두 배로 잡는다.
        middle.SizeChanged += (_, e) =>
            box.Margin = new Thickness(0, 0, 0, e.NewSize.Height * TitleMenuRise * 2);

        EnableMenuDrag(handle, box, middle, move);

        // 게임 타이틀에도 위아래로 액자 띠가 있다. 위 띠에는 날짜 칸 하나만 있고 나머지는 비었다.
        var screen = new DockPanel();
        var top = TitleBarStrip($"{_game.Player.Date.Year}년 {_game.Player.Date.Month}월 {_game.Player.Date.Day}일");
        DockPanel.SetDock(top, Dock.Top);
        screen.Children.Add(top);

        // 다운로드·오류 같은 시작 알림도 타이틀 화면 아래 띠에 보인다.
        _titleNote = new GameUi.GameLabel(GameFont.BlackColor)
        {
            Margin = new Thickness(12, 0, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Left,
            Text = _note.Text,
            Visibility = _note.Visibility,
        };
        var bottom = TitleBarStrip(null, _titleNote);
        bottom.Cursor = System.Windows.Input.Cursors.Hand;
        bottom.MouseLeftButtonUp += (_, e) => { e.Handled = true; ReadNote(); };
        DockPanel.SetDock(bottom, Dock.Bottom);
        screen.Children.Add(bottom);

        screen.Children.Add(middle);

        return screen;
    }

    /// <summary>
    /// 메인메뉴를 가운데에서 얼마나 옮겼는지. 타이틀 화면을 다시 지어도 그 자리에 남는다 —
    /// 게임 폴더를 알게 되면 화면을 새로 짓기 때문이다.
    /// </summary>
    private Point _titleMenuOffset;

    /// <summary>
    /// 제목 띠를 잡아 메뉴 상자를 옮길 수 있게 한다.
    /// </summary>
    /// <remarks>
    /// 창을 옮기는 <see cref="GameUi.EnableDrag"/> 와 달리 이것은 <b>화면 안에서</b> 상자만
    /// 옮긴다 — 타이틀 메뉴는 제 창이 아니라 타이틀 화면 위에 얹힌 칸이기 때문이다.
    ///
    /// 손잡이를 제목 띠로 좁힌 까닭은 상자 속이 죄다 누르는 줄이어서다. 아무 데나 잡게 두면
    /// NEW GAME 을 누르려다 조금만 흔들려도 끌기로 새어 눌리지 않는다.
    ///
    /// 상자가 화면 밖으로 아주 나가지 않게 가장자리에서 막는다. 제목 띠가 남아 있어야
    /// 다시 잡아 끌 수 있다.
    /// </remarks>
    private void EnableMenuDrag(FrameworkElement handle, FrameworkElement box,
                                FrameworkElement area, TranslateTransform move)
    {
        Point grabbed = default;
        Point start = default;
        // 커서는 그대로 둔다 — 게임은 끌 수 있는 자리라고 십자로 바꿔 알리지 않는다.

        handle.MouseLeftButtonDown += (_, e) =>
        {
            grabbed = e.GetPosition(area);
            start = new Point(move.X, move.Y);
            handle.CaptureMouse();
            e.Handled = true;
        };

        handle.MouseMove += (_, e) =>
        {
            if (!handle.IsMouseCaptured) return;
            var now = e.GetPosition(area);

            // 가운데 놓인 상자가 얼마나 갈 수 있는지 — 좌우·위아래로 각각 절반씩이다.
            double roomX = Math.Max(0, (area.ActualWidth - box.ActualWidth) / 2);
            double roomY = Math.Max(0, (area.ActualHeight - box.ActualHeight) / 2);

            move.X = Math.Clamp(start.X + (now.X - grabbed.X), -roomX, roomX);
            move.Y = Math.Clamp(start.Y + (now.Y - grabbed.Y), -roomY, roomY);
            _titleMenuOffset = new Point(move.X, move.Y);
        };

        handle.MouseLeftButtonUp += (_, e) =>
        {
            handle.ReleaseMouseCapture();
            e.Handled = true;
        };
    }

    /// <summary>
    /// 타이틀 화면 위아래에 두는 액자 띠. <paramref name="text"/> 를 주면 왼쪽에 칸 하나를 둔다.
    /// </summary>
    private static FrameworkElement TitleBarStrip(string? text, FrameworkElement? slot = null)
    {
        var inside = new StackPanel { Orientation = Orientation.Horizontal };
        if (slot != null)
        {
            inside.Children.Add(slot);
        }
        else if (text != null)
        {
            inside.Children.Add(new GameButton(text) { Lit = true, Margin = default });
        }
        else
        {
            // 빈 띠도 높이는 있어야 한다 — 글자 한 줄만큼 자리를 잡아 둔다.
            inside.Children.Add(new Border { Height = 24 });
        }

        FrameworkElement? framed = GameUi.BarFrame(inside);
        return framed ?? new Border
        {
            Background = BarFill,
            BorderBrush = BarEdge,
            BorderThickness = new Thickness(0, 0, 0, 2),
            Child = inside,
        };
    }

    /// <summary>
    /// 타이틀 바탕. <c>asset/title/title-tile.png</c> 가 있으면 바둑판처럼 깔고,
    /// 없으면 무늬 없이 양피지색만 채운다.
    /// </summary>
    /// <summary>
    /// 바탕 무늬를 얼마로 줄여 깔지. 1 이면 원본 크기다.
    /// </summary>
    /// <remarks>
    /// 원본 <c>140x112</c> 은 <b>1.75배로 늘어난 화면</b>에서 뜬 것이다 — 게임 무늬는
    /// <c>80x64</c> 다(갈무리에서 잰 마디 가로 114 · 세로 91 을 그 갈무리 배율 1.425 로
    /// 나누면 딱 떨어진다). 그 배로 도로 줄여야 창들과 결이 맞는다.
    /// </remarks>
    private const double TilePack = 1.0 / 1.75;

    /// <remarks>놀이 끝 화면(<see cref="GameOverDialog"/>)도 같은 무늬를 깐다.</remarks>
    internal static Brush TitleBackground()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "asset", "title", "title-tile.png");
        if (!File.Exists(path)) return BarFill;
        try
        {
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.UriSource = new Uri(path);
            bmp.CacheOption = BitmapCacheOption.OnLoad;   // 파일을 잡고 있지 않게 다 읽고 놓는다
            bmp.EndInit();
            bmp.Freeze();
            var wall = new ImageBrush(bmp)
            {
                TileMode = TileMode.Tile,
                ViewportUnits = BrushMappingMode.Absolute,
                // 무늬 원본은 1.75배 화면에서 찍은 것이라 그대로 깔면 성기다.
                Viewport = new Rect(0, 0, bmp.PixelWidth * TilePack, bmp.PixelHeight * TilePack),
                Stretch = Stretch.Fill,
            };
            RenderOptions.SetBitmapScalingMode(wall, GameUi.SpriteScaling);
            wall.Freeze();
            return wall;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ShipMap] 타이틀 무늬 로드 실패: {ex.Message}");
            return BarFill;
        }
    }

    /// <summary>타이틀 메뉴에서 초점이 오가는 줄 묶음. 화면을 다시 지을 때 새로 잡는다.</summary>
    private GameUi.FocusGroup _titleFocus = new();

    /// <summary>메인메뉴를 화면 높이의 몇 만큼 위로 올릴지.</summary>
    private const double TitleMenuRise = 0.05;

    /// <summary>타이틀 메뉴 줄의 최소 폭. 글자 좌우 여백까지 넣은 게임 비율이다.</summary>
    private const double TitleItemMinWidth = 124;

    /// <summary>
    /// 타이틀 메뉴 한 줄. <paramref name="run"/> 이 null 이면 흐리게 두고 못 고른다.
    /// </summary>
    /// <remarks>
    /// 예전에는 여기서 띠를 짓고 초점 테를 얹고 깜빡임까지 손수 굴렸다 — <see cref="GameUi"/>
    /// 에 같은 것이 이미 있는데도 <c>FocusLight</c>·<c>FocusDark</c>·<c>FocusBlink</c> 를
    /// 다시 선언해 두었다. 이제 <see cref="GameButton"/> 과 <see cref="GameUi.FocusGroup"/>
    /// 이 맡는다.
    ///
    /// 못 고르는 줄은 묶음에 안 넣는다 — 초점이 그 줄을 건너뛴다.
    /// </remarks>
    /// <summary>
    /// 미궁 64 퍼즐을 여는 자리. <b>그 놀이는 CdsHelper.Maze 에 따로 있다</b> —
    /// 그쪽이 여기를 물고 있어서 반대로는 못 부른다. 띄우는 쪽(CdsHelper.Form)이
    /// 이 자리에 걸어 준다.
    /// </summary>
    /// <remarks>돌파했으면 true 를 낸다 — 발견 대본(<c>0E 04 02</c>)이 그 결과로 갈라진다.</remarks>
    public static Func<Window, Random, Player?, Local.Helpers.SoundBank?, bool>? MazeGame { get; set; }

    // 일기토를 밖에서 걸어 주던 자리(DuelGame)는 걷었다 — 이제 PlayDuel 이 반란·해전이
    // 쓰는 그 판을 곧장 부른다. CdsHelper.Duel 의 옛 판은 아무도 안 부른다.

    /// <summary>
    /// MINI GAME — 일곱 줄을 늘어놓는다(<c>0x0045F957</c> 벌).
    /// </summary>
    /// <remarks>
    /// 이름은 <c>0x00571E00</c> 부터 열여섯 바이트씩이고, 고르면 <c>0x0045FCCC</c> 의
    /// 뜀표로 갈린다.
    /// <code>
    ///   MG00 성배 퍼즐          0x004684D0
    ///   MG01 스핑크스 퀴즈      0x0047BFE0
    ///   MG02 미궁 64 퍼즐       0x0042C8A0
    ///   MG03 낚시 게임          0x0047BDD0
    ///   MG04 코인 게임          0x004531F0
    ///   MG05 발라몬의 탑 퍼즐   0x0045FB60
    ///   MG06 화살표 입방체 퍼즐 0x0045FBBD
    /// </code>
    /// 게임은 줄마다 레지스트리를 읽어 <b>풀어 놓은 것만</b> 켠다 —
    /// <c>Software\KOEI\CostaDelSol\1.0</c> 의 <c>MG00</c>~<c>MG06</c> 이 1 이어야 한다
    /// (<c>0x0045FA54</c> 벌). 우리는 설정의 <see cref="Local.Settings.GameSettings.IsMinigameUnlocked"/> 로 본다.
    ///
    /// <b>여덟째 「일기토」는 원본 차림표에 없다.</b> 게임에서는 해전에서 기함끼리
    /// 붙었을 때만 열리는데(<c>0x0043A347</c>) 아직 해전이 없어서 여기에 붙여 둔다.
    /// </remarks>
    private void MiniGames()
    {
        // 원본 차림표 그대로 일곱 줄이다 — 일기토·육상전 모의전·모의해전은 게임에 없는 줄이라 개발 창으로 옮겼다.
        string[] names =
        [
            "성배 퍼즐", "스핑크스 퀴즈", "미궁 64 퍼즐", "낚시 게임",
            "코인 게임", "발라몬의 탑 퍼즐", "화살표 입방체 퍼즐",
        ];

        // 발견 이벤트에서 그 놀이를 이겨 풀린 것만 켜진다(0x0045FA54 벌).
        bool[] open = [.. names.Select((_, i) => Local.Settings.GameSettings.IsMinigameUnlocked(i))];
        int pick = MapPointDialog.Ask(this, names, "미니 게임", MapPointDialog.MenuWidth, open);
        if (pick < 0) return;

        switch (pick)
        {
            case 0: GrailPuzzleDialog.Play(this, _game.Player, _game.Random, _game.Sfx); break;
            case 1: SphinxQuizDialog.Play(this, _game.Random); break;
            case 2:
                if (MazeGame == null) NoticeDialog.Show(this, "아직 만들지 않았습니다");
                else MazeGame(this, _game.Random, null, _game.Sfx);   // 미니 게임은 상금 갈래가 아니다(0x0042C8A0(0))
                break;
            case 3: FishingGameDialog.Play(this, _game.Random); break;
            case 4: CoinPuzzleDialog.Play(this, _game.Random); break;
            case 5: TowerPuzzleDialog.Play(this, _game.Random); break;
            case 6: CubePuzzleDialog.Play(this, _game.Player, _game.Random, _game.Sfx); break;
            default: NoticeDialog.Show(this, "아직 만들지 않았습니다"); break;
        }
    }

    /// <summary>
    /// 모의해전 — 바다에서 무리를 만나 <b>「응전한다」를 누른 것과 똑같이</b> 흘린다.
    /// </summary>
    /// <remarks>
    /// <see cref="CheckEncounter"/> 의 응전 갈래를 그대로 밟는다 — 무리 굴림 → 들어설 때의 말 →
    /// 응전 말(부관, 없으면 뱃사람 얼굴) → 해전 판. 판의 바람은 함대 자리의 바다 바람이다
    /// (<c>0x00441F1C</c>). 배가 없으면 해전 창이 연습용 카라벨 한 척을 띄운다.
    /// </remarks>
    private void MockSeaBattle()
    {
        var rng = _game.Random;
        var foe = Encounter.Roll(rng, CaptainOf);
        var face = MateFace();

        ConfirmDialog.Tell(this, Encounter.GreetOf(foe, rng), Encounter.TitleOf(foe.Kind), face);
        ConfirmDialog.Tell(this, Encounter.FightOnWord(rng), "응전", face);
        int leaderId = foe.Leader?.Id ?? Encounter.PirateLeader;
        var foeFace = PersonFace(leaderId);
        EndWeather();   // 해전이 열리면 비가 그친다(0x00443822)

        SeaCombatDialog.Fight(this, _game.Player, foe, rng, face,
                              (_host.LastWind.Dir, _host.LastWind.Speed), _game.Sfx,
                              foeFace, SeaDuel(leaderId, foe.Name, foeFace), _game.Bgm, game: _game);
    }

    /// <summary>그 인물의 얼굴. 인물표를 못 읽었으면 null.</summary>
    private uint[]? PersonFace(int id) =>
        _game.World?.Table.Find(id) is { } row ? _game.Faces?.TryGetBgra(row.Face, female: false) : null;

    /// <summary>
    /// 해전 일기토(<c>0x0043A200</c> 6.4 → <c>0x004AA700(적장, 0, 0, −1)</c>) — 판 창 위에 결투 판을 연다.
    /// </summary>
    /// <remarks>
    /// 싸우는 값은 해전 값이 아니라 <b>인물 레코드</b>(체력·무력·검술·운)다. 갈래 0 이라 부관이 있으면
    /// 「　부관을 싸우게 하겠습니까?」를 묻는다(<c>0x004A8611</c>). 이기면 처형·놓아 준다·모두 뺏는다(<c>0x004A9E50</c>).
    /// 지면 용서받아도 기함이 가라앉은 것으로 쳐 GAME OVER 라 도망·용서 말은 안 낸다. 상대 무기·갑옷은 인물표에 없어 0 이다.
    /// </remarks>
    private Func<Window, bool?> SeaDuel(int leaderId, string name, uint[]? foeFace) => board =>
    {
        var player = _game.Player;
        var row = _game.World?.Table.Find(leaderId);
        var builtin = Encounter.CaptainOf(leaderId);
        var foe = new Engine.Town.Duel.Fighter(
            row?.Name is { Length: > 0 } rowName ? rowName : name,
            Body: row?.Stats is { Length: > 0 } s0 ? s0[Ability.Body] : 50,
            Might: row?.Stats is { Length: > Ability.Might } s2 ? s2[Ability.Might] : builtin.Might,
            Sword: row?.Skills is { Length: > Skill.Sword } k ? k[Skill.Sword] : builtin.Sword,
            Luck: row?.Stats is { Length: > Ability.Luck } s4 ? s4[Ability.Luck] : builtin.Luck,
            Weapon: 0, Armor: 0);

        var dice = new GameRandom(Environment.TickCount);
        var mate = SeaSendMate(board, dice);
        var me = mate is { } m
            ? new Engine.Town.Duel.Fighter(m.Name, m.Body, m.Might, m.Sword, m.Luck,
                                           BestItem(Engine.Town.Duel.WeaponCategory),
                                           BestItem(Engine.Town.Duel.ArmorCategory))
            : MyFighter();
        var duel = new Engine.Town.Duel(me, foe, player.Items.Contains(Engine.Town.Duel.EdithShieldId),
                                        Environment.TickCount);

        DuelDialog.Show(board, duel, dice, foeFace, _game.Fighters, foeSet: 1,
                        myFace: _game.Faces?.TryGetBgra(
                            PortraitAges.At(player.Face, player.Age, false, _game.Faces), female: false),
                        arena: DuelArt.Deck,
                        bgm: _game.Bgm);

        // 대신 나간 사람이 다친다(0x004AA5F8).
        if (mate is { } hurt) player.HurtMate(hurt.Name, duel.BodyLost);
        else player.Hurt(duel.BodyLost);

        if (duel.Won != true) return false;
        SeaTriumph(board, leaderId, foeFace, dice);
        // 승리 차림표가 뜬 판이라 무력 성장 굴림이 붙는다(0x004AA592).
        TavernMenu.GrowMight(board, player, mate is { }, dice);
        return true;
    };

    /// <summary>부관을 대신 내보낼지 묻는다(<c>0x004A8611</c>) — 술집 일기토와 같은 셈·말이다.</summary>
    private Player.MateInfo? SeaSendMate(Window owner, GameRandom dice)
    {
        var player = _game.Player;
        string first = player.Mates.FirstOrDefault(n => n.Length > 0) ?? "";
        if (first.Length == 0 || player.MateInfoOf(first) is not { } mate) return null;
        if (!ConfirmDialog.Ask(owner, "　부관을 싸우게 하겠습니까?", "일기토")) return null;

        int mine = (player.AbilityOf(Ability.Might) + 1) / TavernMenu.MateEdge
                 + player.LevelOf(Skill.Names[Skill.Sword]) * TavernMenu.MateSwordWeight;
        int theirs = (mate.Might + 1) / TavernMenu.MateEdge + mate.Sword * TavernMenu.MateSwordWeight;

        var face = _game.Faces?.TryGetBgra(mate.Face, female: false);
        if (mine <= theirs)
        {
            TalkDialog.Say(owner, face, "", TavernMenu.MateEager[dice.Next(TavernMenu.MateEager.Length)]);
            return mate;
        }
        TalkDialog.Say(owner, face, "", TavernMenu.MateShy[dice.Next(TavernMenu.MateShy.Length)]);
        return ConfirmDialog.Ask(owner, "　부관을 싸우게 하겠습니까?", "일기토") ? mate : null;
    }

    /// <summary>해전 일기토에서 이긴 뒤 — 처형한다 · 놓아 준다 · 모두 뺏는다(<c>0x004AA2D2</c>~).</summary>
    private void SeaTriumph(Window owner, int leaderId, uint[]? face, GameRandom dice)
    {
        var player = _game.Player;
        // 해전 일기토는 <b>무대 0(갑판)</b> 이라 줄이 「처형한다」 하나뿐이다 — 「놓아 준다」와
        // 「모두 뺏는다」는 무대 4 이상(술집·모스크·사원)이고 종류가 6 이 아닐 때만 붙는다
        // (0x004A8470~0x004A84AC).
        switch (ChoiceDialog.Pick(owner, "", ["처형한다"]))
        {
            case 0:
                TalkDialog.Say(owner, face, "", TavernMenu.Executed[dice.Next(TavernMenu.Executed.Length)]);
                if (_game.World?.People.FirstOrDefault(r => r.Id == leaderId) is { } person)
                    person.Appear = 0;                                            // 0x00432180(0)
                break;

            case 2:
                TalkDialog.Say(owner, face, "", TavernMenu.Robbed[dice.Next(TavernMenu.Robbed.Length)]);
                player.Infamy += TavernMenu.RobInfamy;
                NoticeDialog.Show(owner, $"악명이 {TavernMenu.RobInfamy} 올라갔다", "일기토");
                int gold = dice.Next(TavernMenu.RobGoldRoll) + TavernMenu.RobGoldBase;
                player.Earn(gold);
                NoticeDialog.Show(owner, $"금화 {gold}닢을 손에 넣었다", "일기토");
                break;

            default:
                TalkDialog.Say(owner, face, "", TavernMenu.Beaten[dice.Next(TavernMenu.Beaten.Length)]);
                player.Fame += TavernMenu.SpareFame;
                NoticeDialog.Show(owner, $"명성이 {TavernMenu.SpareFame} 올라갔다", "일기토");
                break;
        }
    }

    /// <remarks>
    /// <paramref name="run"/> 이 null 이면 <b>죽은 줄</b>이다 — 띠는 그대로고 글씨만
    /// 회색(색인 21)으로 찍힌다. 게임도 그렇게 낸다.
    /// </remarks>
    private Border TitleMenuItem(string text, Action? run)
    {
        var item = run != null
            ? _titleFocus.Add(text, run, 0)
            : new GameButton(text, null);

        // 게임은 글자 좌우로 넉넉히 비운다 — 글자에 딱 붙이면 띠가 쪼그라들어 보인다.
        // 가장 긴 "LOAD GAME"(72점)의 1.7배쯤이 게임 비율이다.
        item.MinWidth = TitleItemMinWidth;
        // 줄과 줄 사이는 붙인다 — 게임 메뉴는 띠가 맞닿아 있고 빈 자리가 없다.
        item.Margin = default;
        return item;
    }

    /// <summary>타이틀에서 위아래로 옮기고 엔터로 고른다. 지도가 뜨면 아무것도 안 한다.</summary>
    private void OnTitleKey(object sender, KeyEventArgs e)
    {
        if (!ReferenceEquals(_screen.Content, _titleRoot)) return;
        if (_titleFocus.HandleKey(e.Key)) e.Handled = true;
    }

    /// <summary>
    /// 타이틀을 걷고 지도를 띄운다. <paramref name="fresh"/> 면 배를 리스본 앞바다에 새로 놓고,
    /// 아니면 적어 둔 기록(<see cref="GameSave"/>)을 되돌린다.
    /// </summary>
    /// <summary>
    /// NEW GAME — 초심자로 할지 새로 지을지 묻고, 새로 지으면 신상부터 받는다.
    /// </summary>
    /// <remarks>
    /// 게임의 <c>0x0045EBE0</c> 이다.
    /// <code>
    ///   0x00552728  표 두 줄
    ///     0x00571A18  초심자용 주인공으로 시작한다(EASY)
    ///     0x00571A40  새로운 주인공으로 시작한다(NORMAL)
    ///   45ec61  EASY   → 0x0045E670  "시작할 주인공을 선택해 주십시오"
    ///   45ec6e            [0x5A4D1A] |= 8      ; 이 비트 때문에 나중에 은퇴를 못 한다
    ///   45ec7e  NORMAL → 0x0045BF80 신상 → 0x0045D6C0 능력치 → 0x0045DE20 지식·언어 → 0x0045E260
    /// </code>
    /// EASY 는 미리 만든 주인공 둘(라몬·데·마르시아스, 에밀리오·알발레스)을 게임이 박는
    /// 값 그대로 앉히고(<see cref="Beginner"/>), 그 개인 이야기(STORY0/1.CDS)는
    /// <see cref="CdsHelper.Support.Local.Models.Player.ActiveStoryBook"/> 로 묶어
    /// <see cref="CityPicView"/> 가 건물을 드나들 때마다 <see cref="Engine.Discovery.StoryLog"/>
    /// 로 찾아 튼다.
    ///
    /// 자세한 것은 볼트 <c>39.분석-NEW GAME(주인공 만들기와 은퇴)</c>.
    /// </remarks>
    /// <summary>
    /// 누적 캐릭터를 이번 판에 내보낼지 묻는다(<c>0x0041AC60</c> 의 <c>mode 0</c>).
    /// </summary>
    /// <remarks>
    /// <code>
    ///   0x0055DFB0  「누적캐릭터를 등장시킨다」
    ///   0x0055DFC8  「누적캐릭터를 등장시키지 않는다」
    ///   0x0055DFE8  다섯이 다 찼을 때의 본문 — 「…등장시키면 지금부터 시작하는 캐릭터로는 은퇴할 수 없게 됩니다.」
    ///   0x0055E0B0  그 밖의 본문
    /// </code>
    /// 「않는다」를 고르면 깃발(<c>0x005A4D1A</c> 비트 0x10)만 서고, 그 판에서 <b>은퇴할 때</b>
    /// 올라 있던 다섯을 다 지운다(<c>0x0041AD55</c>) — 은퇴하지 않으면 다섯은 그대로 남는다.
    ///
    /// 「등장시킨다」를 고르면 인물 276~280 자리에 앉히고(<see cref="Engine.AccData.Place"/>)
    /// 옛 발자취를 날마다 되짚게 건다(<see cref="Engine.AccReplay"/>).
    /// </remarks>
    /// <returns>이어서 제독을 지어도 되면 true.</returns>
    private bool AskCumulative()
    {
        var all = Engine.AccData.Load();
        if (all.Count == 0) return true;

        string names = string.Join("\n", all.Select(c => c.Name));
        string body = $"{names}\n{all.Count}명의 누적캐릭터가 등록되어 있습니다.\n"
                    + "이 캐릭터들을 게임 속에 등장시킬 수 있습니다만, 어떻게 하시겠습니까?"
                    + (all.Count >= Engine.AccData.Slots
                        ? "\n또, 이 캐릭터들을 등장시키면 지금부터 시작하는 캐릭터로는 은퇴할 수 없게 됩니다."
                        : "");

        int at = ChoiceDialog.Ask(this, body,
            ["누적캐릭터를 등장시킨다", "누적캐릭터를 등장시키지 않는다"]);
        if (at < 0) return false;

        // 「등장시키지 않는다」면 깃발만 세운다 — 비우는 것은 은퇴할 때다(0x0041AD55).
        if (at == 1) { _game.Player.SkipsCumulative = true; return true; }

        // 등장시키면 인물 276~280 자리에 앉고(0x0041AF00), 옛 발자취를 날마다 되짚는다.
        if (_game.World is { } world)
        {
            Engine.AccData.Place(world.People);
            var replay = new Engine.AccReplay(_game.Player.Date);
            // 누적 캐릭터가 옛 공략을 되짚으면 그 도시가 그 나라로 넘어가고 알림이 뜬다(0x00409A7E).
            replay.Captured = (person, city, nation) =>
            {
                Engine.Market.CityHistory.ChangeNation(_game.Player, _game.CityRows, _game.Nations, city, nation);
                string who = world.People.FirstOrDefault(r => r.Id == person)?.Name ?? "";
                string town = _game.CityName(city);
                if (who.Length > 0)
                    NoticeDialog.Show(this, $"{who}{GameUi.Josa(who, "이", "가")} [{town}]{GameUi.Josa(town, "을", "를")} 공략했습니다");
            };
            // 옛 발견 보고를 되짚으면 그 발견물이 그 사람 이름으로 세상에 알려진다
            // (0x0040B916 — 아무도 발표하지 않은 것에만 이름이 올라간다).
            replay.Announced = (person, discovery) =>
            {
                string who = world.People.FirstOrDefault(r => r.Id == person)?.Name ?? "";
                if (!_game.Player.Scoop(discovery, who)) return;
                string what = _game.Discoveries?.Table.Find(discovery)?.Name ?? "";
                if (what.Length == 0) return;
                NoticeDialog.Show(this, $"{who}{GameUi.Josa(who, "이", "가")} [{what}]{GameUi.Josa(what, "을", "를")} 보고했습니다");
            };
            replay.Load();
            world.Replay = replay.Any ? replay : null;
        }
        return true;
    }

    private void NewGame()
    {
        // 게임도 여기부터는 메인메뉴를 걷는다 — 고르는 창이 그 자리에 뜬다.
        HideTitleMenu(true);

        // 적어 둔 판이 있으면 그것부터 어떻게 할지 묻는다.
        if (!BreakOff()) { HideTitleMenu(false); return; }

        // 앞 판이 묻어 오지 않게 주인공을 새로 앉힌다 — 새 놀이는 1480년 1월 1일부터다.
        // 짓다 말고 물러나면 하던 판을 도로 앉혀야 한다.
        var before = _game.NewPlayer();
        bool made = false;
        try
        {
            // 주인공 고르기나 신상에서 물리면 <b>NEW GAME 차림표로</b> 되돌아간다 — 원본은 0x0045EC6C · 0x0045EC85 에서
            // 0x0045EBF5 로 뛰어 주인공을 다시 비우고(0x00478550) 차림표를 다시 낸다. 첫 화면으로는 차림표에서 물릴 때만 간다.
            while (!made)
            {
                int at = ChoiceDialog.Ask(this, "NEW GAME",
                    ["초심자용 주인공으로 시작한다(EASY)", "새로운 주인공으로 시작한다(NORMAL)"]);
                if (at < 0) return;

                if (at == 0)
                {
                    // 미리 만든 주인공 둘(0x0045E670). 표는 0x00571998 두 줄이다.
                    int who = ChoiceDialog.Ask(this, "시작할 주인공을 선택해 주십시오",
                                               ["라몬(포르투갈)", "에밀리오(에스파니아)"]);
                    if (who < 0) { _game.NewPlayer(); continue; }
                    Beginner.Apply(_game.Player, Beginner.All[who]);
                }
                else
                {
                    // 누적 캐릭터가 올라 있으면 <b>제독을 짓기 앞서</b> 내보낼지 묻는다(0x0041AF00).
                    if (!AskCumulative() || !MakeCharacter()) { _game.NewPlayer(); continue; }
                    // 새 주인공도 국적·직업에 따른 개인 이야기를 든다(0x0045ECA8).
                    if (Engine.Disev.DisevBook.PersonalStory(_game.Player.Nation, _game.Player.JobIndex) is { } story)
                        _game.Player.SetActiveStoryBook(story);
                }
                made = true;
            }
        }
        finally
        {
            if (!made) _game.UsePlayer(before);
            // 물러났으면 메뉴가 도로 나와야 한다. 놀이로 들어갔으면 타이틀째로 사라진다.
            HideTitleMenu(false);
        }

        // 새 주인공은 배가 없다 — 조선소에서 첫 배를 사야 바다에 나간다.
        _game.Player.ClearShips();

        StartMap(fresh: true);
        OpenHome();
    }

    /// <summary>
    /// 주인공을 짓는 네 걸음. 어느 걸음에서 물러도 앞 걸음으로 되돌아간다.
    /// </summary>
    /// <remarks>
    /// 게임도 걸음마다 0 을 내면 한 걸음 되돌아간다(<c>0x0045EC7E</c> 벌).
    /// <code>
    ///   0x0045BF80  신상        → CharacterMakeDialog
    ///   0x0045D6C0  능력치·직업 → AbilityMakeDialog
    ///   0x0045DE20  기술·언어   → SkillMakeDialog
    ///   0x0045E260  마무리      → CharacterSheetDialog
    /// </code>
    /// </remarks>
    private bool MakeCharacter()
    {
        var rng = new Random();
        int step = 0;

        // 기술 화면에서 되돌아오면 능력치를 <b>그대로 잇는다</b>. −1 이면 새로 굴린다 —
        // 첫 걸음(이름·초상)으로 돌아갔다 오는 것은 사람을 새로 짓는 것이라 굴린다.
        int spare = -1;
        bool back = false;

        while (true)
            switch (step)
            {
                case 0:
                    if (!CharacterMakeDialog.Show(this, _game.Player, _game.Directory)) return false;
                    step = 1;
                    spare = -1;
                    break;

                case 1:
                    spare = AbilityMakeDialog.Show(this, _game.Player, rng, spare);
                    step = spare < 0 ? 0 : 2;
                    break;

                case 2:
                    // 보너스는 기술 화면이 제 손으로 센다 — 앞 걸음의 잔량이 아니다.
                    step = SkillMakeDialog.Show(this, _game.Player, AbilityMakeDialog.RolledMind, back) ? 3 : 1;
                    back = false;
                    break;

                default:
                    if (CharacterSheetDialog.Show(this, _game.Player)) return true;
                    step = 2;
                    back = true;   // 확인 화면에서 물러서면 기술 화면이 고르던 것을 잇는다
                    break;
            }
    }
    /// <summary>
    /// 새 놀이는 <b>고른 국적의 자택</b>에서 시작한다 — 포르투갈이면 리스본,
    /// 에스파니아면 세빌리아다.
    /// </summary>
    /// <summary>새 판이 여는 도시 — 나라가 1 이면 세빌리아, 아니면 리스본. 없으면 번호 -1.</summary>
    private (int Id, string Name) StartCity()
    {
        string want = _game.Player.Nation == 1 ? "세빌리아" : "리스본";
        var found = _game.CityTable.Cities.FirstOrDefault(c => c.Name == want);
        return found.Name == want ? (found.Id, found.Name) : (-1, "");
    }

    private void OpenHome()
    {
        var found = StartCity();
        if (found.Id < 0) return;

        // 새 판을 연 도시가 모항이다 — 발표는 여기서만 된다(0x0045E449).
        _game.Player.SetHomePort(found.Id);

        if (!_host.PlaceAtCity(found.Id)) return;
        _askedCity = found.Id;                    // 곧바로 다시 묻지 않게
        _host.EnterPort(found.Name);
        if (ShowCityPicture(found.Id, found.Name, enterHome: true)) _host.Paused = true;
    }

    /// <summary>
    /// <b>모험 중단</b> — 적어 둔 판이 있을 때 NEW GAME 이 먼저 묻는 것.
    /// </summary>
    /// <remarks>
    /// 게임은 <c>0x0045F60E</c> 에서 지금 놀고 있는 캐릭터가 있는지 보고, 있으면
    /// 이 창을 낸다(<c>0x0045F65B</c>).
    /// <code>
    ///   0045F65B  "현재 게임중의 캐릭터인 %s%s 있습니다만 어떻게 하겠습니까?"  제목 "모험 중단"
    ///   0045F66C  은퇴시킨다 · 삭제한다 · 신규작성을 중지한다
    ///   0045F700  은퇴 줄은 [0x005A4D1A] &amp; 0x40 — <b>누적 캐릭터 자리가 비어야</b> 켜진다
    ///   0045F8CE  삭제한다 → "[%s]%s 삭제합니다. 좋습니까?"
    ///   0045F8F2  YES 면 C:SAVEDATA.CDS · C:SAVEDATA.TMP · C:ACCDATA.CDS 를 지우고 만들기로
    /// </code>
    ///
    /// <b>지우는 것은 우리 세이브뿐이다</b>(<c>%APPDATA%\CdsHelper\SAVEDATA.CDS</c>).
    /// 게임 폴더의 SAVEDATA.CDS 는 사람이 진짜로 놀던 것이라 우리는 읽기만 한다 —
    /// 그것을 지우면 되돌릴 길이 없다.
    ///
    /// 「은퇴시킨다」는 그 제독을 누적 캐릭터 다섯 자리에 올린다(<see cref="Engine.AccData"/>) —
    /// 초심자용 캐릭터만 물린다(<c>0x0045F886</c>).
    /// </remarks>
    /// <returns>새로 만들어도 되면 true, 물러났으면 false.</returns>
    private bool BreakOff()
    {
        var saved = GameSave.Load();
        if (saved == null) return true;

        string name = !string.IsNullOrEmpty(saved.Name) ? saved.Name : "이름 없는 제독";

        while (true)
        {
            ConfirmDialog.Tell(this,
                $"현재 게임중의 캐릭터인 {name}{GameUi.Josa(name, "이", "가")} 있습니다만 " +
                "어떻게 하겠습니까?", "모험 중단");

            // 누적 캐릭터 자리가 다 찼으면 「은퇴시킨다」 줄이 <b>흐리게 남는다</b> — 목록에서 빠지지는 않는다
            // (0x0045F700 이 [0x005A4D1A] 의 0x40 비트로 그 줄의 켜짐 칸을 0 으로 둔다).
            bool room = Engine.AccData.Load().Count < Engine.AccData.Slots;
            int at = ChoiceDialog.Ask(this, "", ["은퇴시킨다", "삭제한다"], "신규작성을 중지한다",
                                      dim: room ? -1 : 0);

            if (at == 0)
            {
                // 초심자용 캐릭터는 못 올린다(0x0045F886). 자리가 다 찼어도 마찬가지고, 둘 다 알린 뒤
                // <b>타이틀로</b> 나간다(0x0045F89B · 0x0045F853 이 차림표를 부순다).
                if (Beginner.IsBeginnerBook(saved.ActiveStoryBook))
                {
                    ConfirmDialog.Tell(this,
                        $"[{name}]{GameUi.Josa(name, "은", "는")} 초심자용 캐릭터입니다. 은퇴할 수 없습니다.",
                        "모험 중단");
                    return false;
                }

                if (!room)
                {
                    ConfirmDialog.Tell(this,
                        $"[{name}]에서는 {Engine.AccData.Slots}명의 캐릭터가 사용되고 있기 때문에 "
                        + "이 캐릭터를 은퇴시킬 수 없습니다.", "모험 중단");
                    return false;
                }

                // 「누적캐릭터를 등장시키지 않는다」로 시작한 판만 되묻는다(0x0045F77F 의 비트 0x10, 0x00571CC8).
                // 여느 판은 <b>묻지 않고</b> 그대로 올린다.
                if (saved.SkipsCumulative == true
                    && !ConfirmDialog.Ask(this,
                        $"[{name}]{GameUi.Josa(name, "은", "는")} 누적 캐릭터를 사용하고 있지 않습니다. "
                        + "이 캐릭터를 은퇴시키기 위해서는 현재 등록되어 있는 누적 캐릭터를 삭제할 필요가 있습니다."
                        + Environment.NewLine
                        + $"[{name}]{GameUi.Josa(name, "을", "를")} 은퇴시키겠습니까?"))
                    return false;

                // 그 깃발이 선 판은 올리기 앞서 다섯 자리를 비운다(0x0041AD55).
                if (saved.SkipsCumulative == true) Engine.AccData.Clear();

                // 적어 둔 것 그대로 누적 캐릭터로 올린다(0x0041AB90 → 0x0041A270).
                // <b>자리가 다 찼으면 못 올린다</b>(0x0045F83E) — 그때는 적어 둔 것을
                // 지우지 않고 되돌아간다. 예전에는 올리지 못한 채로 지워 버렸다.
                if (!Engine.AccData.Register(saved))
                {
                    ConfirmDialog.Tell(this,
                        $"[{name}]에서는 {Engine.AccData.Slots}명의 캐릭터가 사용되고 있기 때문에 "
                        + "이 캐릭터를 은퇴시킬 수 없습니다.", "모험 중단");
                    return false;
                }

                if (GameSave.Delete()) return true;
                NoticeDialog.Show(this, "적어 둔 것을 지우지 못했습니다.");
                return false;
            }

            if (at != 1) return false;      // 신규작성을 중지한다 · ESC

            if (!ConfirmDialog.Ask(this, $"[{name}]{GameUi.Josa(name, "을", "를")} 삭제합니다. 좋습니까?"))
                return false;

            if (GameSave.Delete()) return true;

            NoticeDialog.Show(this, "적어 둔 것을 지우지 못했습니다.");
            return false;
        }
    }

    /// <summary>
    /// <b>사운드테스트</b>(<c>0x0045FBCD</c>) — 번호를 적으면 그 소리를 틀고, 중단하면 소리를 끄고 나간다.
    /// 원본은 타이틀 차림표에 두는데 우리는 <b>헬퍼의 「도구」</b>에서 부른다.
    /// </summary>
    /// <remarks>
    /// 게임은 수 적기 창(<c>0x00481FE0(지금값, 0, 0x4D, 1, 1)</c>)을 되풀이해 띄우고 고른 번호를
    /// <c>0x004225A0</c> 에 넘긴다. 번호 0~27 은 CD 트랙, 28~77 은 WAVE 파트다
    /// (<see cref="Support.Local.Helpers.WaveBank.FirstSoundId"/>).
    /// </remarks>
    public void SoundTest()
    {
        int at = _soundTestAt;
        while (CountDialog.Set(this, "사운드테스트", "번호", "", at, SoundTestMax) is { } pick)
        {
            at = _soundTestAt = pick;
            if (pick < Support.Local.Helpers.WaveBank.FirstSoundId) _game.Bgm.Play(pick);
            else _game.Sfx?.Play(pick - Support.Local.Helpers.WaveBank.FirstSoundId);
        }

        _game.Bgm.Stop();   // 나갈 때 소리를 끈다(0x0045FC24 의 0x00422A40(-1, 3))
    }

    /// <summary>사운드테스트가 마지막으로 튼 번호(<c>0x005527B8</c>).</summary>
    private int _soundTestAt;

    /// <summary>사운드테스트에서 고를 수 있는 가장 큰 번호(<c>0x0045FBDC</c> 의 <c>0x4D</c>).</summary>
    private const int SoundTestMax = 0x4D;

    /// <summary>
    /// 타이틀의 메인메뉴 상자를 걷거나 도로 낸다.
    /// </summary>
    /// <remarks>
    /// 게임은 NEW GAME 을 고르면 메인메뉴를 지우고 그 자리에 고르는 창을 낸다. 우리 타이틀은
    /// 무늬 바탕 위에 상자를 얹은 것이라 <b>상자만 감춘다</b> — 바탕은 그대로 남는다.
    /// </remarks>
    private void HideTitleMenu(bool hide)
    {
        if (_titleMenuBox != null)
            _titleMenuBox.Visibility = hide ? Visibility.Hidden : Visibility.Visible;
    }

    /// <summary>타이틀의 메인메뉴 상자. NEW GAME 으로 들어갈 때 잠깐 걷는다.</summary>
    private FrameworkElement? _titleMenuBox;

    /// <summary>
    /// 도시 커맨드의 「지도를 본다 → 항해지도」 — 바다와 <b>같은</b> 모달 창을 띄운다.
    /// </summary>
    /// <remarks>
    /// 게임의 <c>0x0049333E</c> 가 <c>0x00416A00</c> 을 부른다. 본 지도의 배율은 안 건드린다.
    /// </remarks>
    /// <param name="owner">창의 주인. 도시 그림이다.</param>
    /// <param name="menu">잠깐 감출 커맨드 창. 닫으면 도로 낸다.</param>
    internal void ShowSeaChart(Window owner, Window? menu) =>
        WithMenuHidden(menu, () => SeaChartDialog.ShowWorld(owner, _host, _game.Player.Explored));

    /// <summary>
    /// 커맨드 창을 감춘 채 지도 창을 띄우고, 닫히면 그 창을 도로 낸다 — 게임은 지도를 닫으면
    /// 「지도를 본다」 메뉴를 다시 띄운다(<c>0x0042617F</c>).
    /// </summary>
    private static void WithMenuHidden(Window? menu, Action show)
    {
        if (menu != null) menu.Visibility = Visibility.Hidden;
        try { show(); }
        finally
        {
            if (menu != null)
            {
                menu.Visibility = Visibility.Visible;
                menu.Activate();
            }
        }
    }

    /// <summary>
    /// 놀이를 그만두고 첫 화면으로 돌아간다. 자택의 "게임 종료" 가 부른다.
    /// </summary>
    /// <remarks>
    /// 창을 닫지는 않는다 — 게임도 첫 화면으로 되돌아갈 뿐이다. 그래서 D3D 자식 창도
    /// 멈추지 않고 그대로 둔다(<c>Content</c> 에서 빠지면 안 보인다). 다시 시작할 때
    /// <see cref="StartMap"/> 이 <c>_started</c> 를 보고 켜는 일을 건너뛴다.
    ///
    /// 도시 그림·명령 창은 이 창이 거느린 것들이라 모두 닫는다. 닫는 동안 목록이 바뀌므로
    /// 먼저 베껴 놓고 돈다.
    /// </remarks>
    public void ReturnToTitle()
    {
        if (_titleRoot == null || ReferenceEquals(_screen.Content, _titleRoot)) return;

        foreach (var child in OwnedWindows.OfType<Window>().ToList()) child.Close();

        _overlay.IsOpen = false;
        _statusTimer.Stop();
        _askedCity = -1;                 // 다시 들어가면 도시를 새로 묻게

        // 놀다 온 사이에 자동저장이 생겼을 수 있다 — CONTINUE 줄이 살아나게 다시 짓는다.
        _titleRoot = BuildTitleScreen();
        _screen.Content = _titleRoot;
        _game.Bgm.Play(BgmPlayer.TitleTrack);
        _status.Text = "";
    }

    /// <summary>
    /// 적어 둔 판을 도로 불러온다 — 자택·여관의 <b>기능 · 로드</b> 가 부른다.
    /// </summary>
    /// <remarks>
    /// 게임도 그 자리에서 곧바로 불러온다(<c>0x004A2830</c>). 도시 창이며 명령 창이
    /// 떠 있으므로 먼저 걷는다 — 불러온 판은 세이브에 적힌 자리에서 다시 시작한다.
    /// </remarks>
    /// <summary>
    /// 지금 떠 있는 함대 창. <b>V 글쇠</b>가 어느 창에서 눌리든 이것을 찾아 저장한다.
    /// </summary>
    /// <remarks>
    /// 시설 창·상자들은 저마다 딴 창이라 글쇠가 지도까지 올라오지 않는다. 그래서 창 쪽에서
    /// 이 자리를 찾아 부른다. 헬퍼 앱에서는 놀이가 안 도니 늘 <c>null</c> 이다.
    /// </remarks>
    internal static ShipMapWindow? Current { get; private set; }

    /// <summary>
    /// V 글쇠로 저장한다 — 자택 「기능 → 저장」과 같은 차례다.
    /// </summary>
    /// <param name="owner">물음창을 얹을 창. 지금 손이 가 있는 창이다.</param>
    /// <remarks>
    /// 타이틀 화면에서는 적을 판이 없으므로 아무것도 안 한다.
    /// </remarks>
    internal void SaveByKey(Window owner)
    {
        if (!_started || !ReferenceEquals(_screen.Content, _mapRoot)) return;
        _game.Player.SetSeaCell(_host.SeaSpot);
        GameSystemMenu.Save(owner, _game);
    }

    /// <summary>
    /// 지도 창 자체에서 누른 V — 이 창은 <see cref="GameWindow"/> 가 아니라 글쇠를 따로 받는다.
    /// 해상에서도 저장이 된다.
    /// </summary>
    /// <summary>
    /// 단축키로 <b>발견물 지도</b>를 연다 — 어느 창에서 눌러도 여기로 온다.
    /// </summary>
    /// <remarks>지도가 떠 있을 때만 연다. 이미 딴 창이 떠 있으면 그 위에 얹힌다.</remarks>
    internal void MapByKey()
    {
        if (!ReferenceEquals(_screen.Content, _mapRoot)) return;
        if (!GameSettings.ShowDiscoveryMapMenu) return;   // 모드에서 꺼 두면 글쇠도 안 먹는다
        ShowDiscoveryMap();
    }

    private void OnMapKey(object sender, KeyEventArgs e)
    {
        // ESC — 떠 있는 커맨드·도시정보 창을 접는다. 창이 제 글쇠를 받는 것은 그 창에
        // <b>초점이 있을 때뿐</b>인데, 상자를 닫고 나면 초점이 지도 창으로 돌아와 있어
        // 그때부터 ESC 가 안 먹었다(피드백 fb-ui-15).
        if (e.Key == Key.Escape && !e.Handled)
        {
            if (CommandMenu.IsOpen) { CommandMenu.Close(); e.Handled = true; return; }
            if (InfoMenu.IsOpen) { InfoMenu.Close(); e.Handled = true; return; }
        }

        if (e.Handled || Keyboard.Modifiers != ModifierKeys.None) return;
        if (e.OriginalSource is System.Windows.Controls.Primitives.TextBoxBase) return;
        if (!ReferenceEquals(_screen.Content, _mapRoot)) return;

        // <b>이 창은 GameWindow 가 아니다</b> — 공용 글쇠 손(GameWindow 의 클래스 손)이
        // 여기까지 오지 않는다. 그래서 지도 창에서는 이 자리에서 같은 글쇠를 받는다.
        // 예전에는 지도 글쇠가 <b>창이 하나 떠 있을 때만</b> 먹어, 커맨드를 열고 눌러야 했다.
        // 숫자판 조타(0x0048B02C) — 1~9 가 뱃머리를 곧장 세우고, 5 는 멈춤을 토글하며
        // 0 은 커맨드 창을 연다. 뭍에서도 같은 글쇠다.
        if (NumpadWay(e.Key) is { } pad)
        {
            e.Handled = true;
            if (pad < 0) _host.ToggleAnchor();          // 「5」
            else _host.SteerTo(pad);
            return;
        }
        if (e.Key is Key.D0 or Key.NumPad0)
        {
            e.Handled = true;
            ShowCommandMenu(this, new Point(ActualWidth / 2, ActualHeight / 2));
            return;
        }

        if (e.Key == KeyOf(GameSettings.MapKey, Key.D))
        {
            e.Handled = true;
            if (GameSettings.ShowDiscoveryMapMenu) Hold(ShowDiscoveryMap);
            return;
        }

        if (e.Key != KeyOf(GameSettings.SaveKey, Key.V)) return;

        e.Handled = true;
        Hold(() => SaveByKey(this));
    }

    /// <summary>
    /// 숫자판 조타 표(<c>0x005696EC</c> 의 글쇠 <c>'1'</c>~<c>'9'</c> 칸) — 16방위 값이다.
    /// <c>'5'</c> 는 −1 로 내어 멈춤 토글을 뜻한다. 숫자 글쇠가 아니면 null.
    /// </summary>
    private static int? NumpadWay(Key key) => key switch
    {
        Key.D1 or Key.NumPad1 => 6,
        Key.D2 or Key.NumPad2 => 8,
        Key.D3 or Key.NumPad3 => 10,
        Key.D4 or Key.NumPad4 => 4,
        Key.D5 or Key.NumPad5 => -1,
        Key.D6 or Key.NumPad6 => 12,
        Key.D7 or Key.NumPad7 => 2,
        Key.D8 or Key.NumPad8 => 0,
        Key.D9 or Key.NumPad9 => 14,
        _ => null,
    };

    /// <summary>글쇠 이름을 글쇠로. 비었거나 모르는 이름이면 기본값이다.</summary>
    private static Key KeyOf(string name, Key fallback) =>
        !string.IsNullOrWhiteSpace(name) && Enum.TryParse(name, ignoreCase: true, out Key key)
            ? key
            : fallback;

    /// <summary>창이 떠 있는 동안 배를 세운다 — 묻는 사이에 흘러가지 않게.</summary>
    private void Hold(Action show)
    {
        bool paused = _host.Paused;
        _host.Paused = true;
        try { show(); }
        finally { _host.Paused = paused; }
    }

    public void LoadGame()
    {
        foreach (var child in OwnedWindows.OfType<Window>().ToList()) child.Close();
        _overlay.IsOpen = false;
        _askedCity = -1;
        StartMap(fresh: false);
    }

    /// <param name="auto">
    /// 자동저장 파일(<see cref="GameSave.AutoPath"/>)을 열지 — 첫 화면의 <b>CONTINUE</b> 다.
    /// </param>
    private void StartMap(bool fresh, bool auto = false)
    {
        _barReady = false;
        if (_gameBar != null) _gameBar.Visibility = Visibility.Collapsed;

        // 불러올 것이 없으면 타이틀에 그대로 머문다 — 화면부터 갈아 끼우면 되돌리기 번거롭다.
        GameSave.Data? saved = null;
        if (!fresh)
        {
            saved = GameSave.Load(auto ? GameSave.AutoPath : null);
            if (saved == null)
            {
                NoticeDialog.Show(this, "적어 둔 기록이 없다.");
                return;
            }
        }

        _screen.Content = _mapRoot;

        if (string.IsNullOrEmpty(_game.Directory))
        {
            _status.Text = "세이브 파일 경로가 없습니다 — 먼저 세이브를 열어 주세요";
            return;
        }

        // 타이틀을 지을 때 게임 폴더를 몰랐을 수 있다. 여기서 한 번 더 챙긴다.
        LoadSprites();

        if (!_started)
        {
            // 바람 표는 달에 따라 갈린다. 지도가 날짜를 들고 있지 않으니 물어보게 해 둔다.
            _host.MonthOf = () => _game.Player.Date.Month;
            // 배가 얼마나 빨리 가는지는 함대와 돛 효율표가 정한다 — 지도는 그 둘을 모른다.
            _host.FleetSpeed = (dir, speed, heading, onLand) =>
                Sailing.SpeedOf(_game.Player, _game.Sails, dir, speed, heading, onLand);
            // 뱃머리가 도는 빠르기도 기함 종류가 정한다(0x00569FC0) — 큰 배일수록 굼뜨다.
            _host.TurnRateOf = () => Sailing.TurnRateOf(_game.Player.FlagshipHull?.Hull);
            // 날짜변경선을 넘을 때마다 바퀴 수를 센다(0x0047D11B) — 세계일주 장면이 쓴다.
            _host.Lapped = laps => _game.Player.Laps += laps;
            if (!_host.Start(_game.Directory)) { _status.Text = _host.Status; return; }
            _host.ShowFlowArrows = GameSettings.ShowFlowArrows;
            _started = true;
        }

        // 상태 시계는 <b>들어올 때마다</b> 켠다. 타이틀로 돌아가면(ReturnToTitle) 멈추는데, 첫 판에서만
        // 켜 두었더니 NEW GAME·불러오기로 다시 들어오면 위 띠가 앞 판 값(날짜·선원·물·식량)에 멈춰
        // 있었고, 입항·날짜 흐름·발견 판정도 함께 섰다. 이미 돌고 있으면 다시 켜도 그대로다.
        _statusTimer.Start();

        // 발견물 이름 덧씌우기는 판을 열 때마다 비운다 — 안 그러면 앞 판에서 지은 이름이 남는다.
        Local.Helpers.DiscoveryTable.ResetNames(null);

        if (fresh)
        {
            _host.ShowShip = false;
            _host.ResetToLisbon();
        }
        else if (saved != null)
        {
            _game.Player.Restore(saved.Gold, saved.Date, saved.CityId, saved.CityName,
                            saved.Skills, saved.Hints, saved.Mates, saved.Met, saved.Items,
                            saved.Supplies, saved.Discoveries, saved.Crew, saved.Announced,
                            saved.Stored, saved.Savings,
                            // 판 16 앞에는 식량·물도 통으로 적혔다.
                            supplyInBarrels: saved.Version < GameSave.SupplyUnitsFrom);
            _game.Player.RestoreFleet(saved.Ships, saved.Flagship, saved.Docked,
                                 saved.ShipHp, saved.DockedHp,
                                 saved.ShipStats, saved.DockedStats,
                                 saved.ShipNames, saved.DockedNames,
                                 gunsInStats: saved.Version >= GameSave.GunsInStatsFrom,
                                 sailsInStats: saved.Version >= GameSave.SailsInStatsFrom);
            _game.Player.RestoreMateBook(saved.MateBook);
            // 실은 교역품과 교역소 재고. 이 판 앞의 세이브에는 없어 빈 짐 · 처음 재고로 연다.
            _game.Player.RestoreCargo(saved.Cargo);
            _game.Player.RestoreTradeStock(saved.TradeStock);
            // 도시 시세·상태와 역사 대본 진행. 옛 세이브면 시세는 100, 상태는 1480년부터 되짚어 채운다.
            _game.Player.RestoreCityRates(saved.CityRates, saved.RatesMonth);
            _game.Player.RestoreCityStates(saved.CityStates);
            _game.Player.RestoreCityScales(saved.CityScales);
            _game.Player.RestoreCityBuildings(saved.CityBuildings);
            _game.Player.SetLastSupply(saved.LastSupply);
            _game.Player.RestoreFleetCity(saved.FleetCity);
            _game.Player.SkipsCumulative = saved.SkipsCumulative ?? false;
            // 중단저장으로 적힌 판을 열었으면 「아직 저장 안 됨」이 선다(0x00478E2B).
            _game.Unsaved = saved.Suspended == true;
            // 앞 판은 발견물 아이템을 발견할 때 소지품에 넣었다 — 아직 안 알린 것은 한 벌씩 걷어 낸다.
            if (saved.Version < GameSave.VirtualItemsFrom)
                foreach (int item in GameInfo.VirtualItems(_game)) _game.Player.Drop(item);
            _game.Player.RestoreScriptedCities(saved.ScriptedCities);
            _game.Player.RestoreNationStatus(saved.NationStatus);
            _game.Player.RestoreBarmaidFlags(saved.GiftedBarmaids, saved.RefusedBarmaids);
            _game.Player.Laps = saved.Laps ?? 0;
            _game.Player.RestorePurses(saved.Purses);
            _game.Player.RestoreHidden(saved.Hidden);
            _game.Player.RestoreTraces(saved.Traces);
            // 대본으로 지어 준 발견물 이름을 표에 도로 덧씌운다 — 게임은 레코드에 직접 쓴다.
            _game.Player.RestoreNamedDiscoveries(saved.NamedDiscoveries);
            // 남이 먼저 발표한 발견물. 이 칸 앞의 세이브는 아무도 안 앞지른 판으로 연다.
            _game.Player.RestoreScooped(saved.Scooped);
            _game.Player.Drinking = saved.Drinking ?? 0;
            Local.Helpers.DiscoveryTable.ResetNames(_game.Player.NamedDiscoveries);
            _game.Player.RestoreRumors(saved.Rumors, saved.PersonLines);
            _game.Player.RestoreHistory(saved.HistoryMonth, saved.HistoryNations, saved.HistoryDone);
            _game.Player.RestoreAnnouncedDates(saved.AnnouncedOn, saved.AnnouncedYears);
            _game.Player.RestoreFoundDates(saved.FoundOn);
            if (saved.Fatigue is { } tired) _game.Player.SetFatigue(tired);
            if (saved.DaysAtSea is { } atSea) _game.Player.SetDaysAtSea(atSea);
            // 컨디션. 이 판 앞의 세이브에는 없어 성한 채로 연다.
            if (saved.Condition is { } fit) _game.Player.SetCondition(fit);
            // 서 있던 해상재해. 판 27 앞의 세이브에는 없어 없는 채로 연다.
            if (saved.Ailments is { } ail) _game.Player.SetAilments(ail);
            // 대열과 배마다 승원(편성). 판 28 앞의 세이브에는 없어 대열 0 · 고르게 나눈 채로 연다.
            if (saved.Formation is { } formation) _game.Player.SetFormation(formation);
            _game.Player.SetCrewShares(saved.CrewShares);
            // 밝힌 바다. 판 21 앞의 세이브에는 없어 빈 채로 시작한다.
            _game.Player.Explored.Restore(saved.Explored);
            // 아내와 후손. 판 22 앞의 세이브에는 없어 홀로 시작한다.
            _game.Player.RestoreFamily(saved.Spouse, saved.Heirs,
                                       saved.SpouseId ?? -1, saved.Liking);

            // 능력치·직업·신상. 이 판 앞의 세이브에는 없어 기본값(여섯 다 50 · 탐험가 · 스물다섯)
            // 으로 열린다 — 적어 두기 전에는 새로 지은 주인공도 불러오면 죄다 50 이었다.
            if (saved.Abilities is { Count: > 0 } stats) _game.Player.SetAbilities(stats);
            if (saved.JobIndex is { } job) _game.Player.JobIndex = job;
            // 나이는 생년월일로 세는 값이라 생일을 먼저 넣고 나이로 태어난 해를 맞춘다.
            if (saved.BirthMonth is { } birthMonth) _game.Player.BirthMonth = birthMonth;
            if (saved.BirthDay is { } birthDay) _game.Player.BirthDay = birthDay;
            if (saved.Age is { } age) _game.Player.Age = age;
            if (saved.Blood is { } blood) _game.Player.Blood = blood;
            if (saved.Nation is { } nation) _game.Player.Nation = nation;

            // 모항. 판 29 앞의 세이브에는 없어 새 판이 여는 도시(리스본·세빌리아)로 둔다.
            // <b>국적을 넣은 뒤에</b> 고른다 — 앞에서 고르면 국적이 아직 밑값 0 이라 에스파니아도 리스본이 모항이 됐다.
            _game.Player.SetHomePort(saved.HomePort ?? StartCity().Id);

            // 이름은 판 24 부터 적힌다 — 그 앞 세이브에서는 빈 채로 둔다.
            if (!string.IsNullOrEmpty(saved.Name)) _game.Player.Name = saved.Name;
            if (saved.Family != null) _game.Player.Family = saved.Family;
            if (saved.Given != null) _game.Player.Given = saved.Given;
            _game.Player.RestoreTongues(saved.Tongues);

            // 나라 적대도와 열린 적대 도시. 판 26 앞의 세이브에는 없어 죄다 0 으로 시작한다 —
            // 게임도 켤 때는 0 이다(형편 판 0x005859C0 은 .bss 다).
            _game.Player.RestoreStandings(saved.Hostility, saved.OpenedGates, saved.TalksLost);

            // 항해하다 알게 된 도시들. 판 27 앞의 세이브에는 없어 유럽 101곳만 아는 채로
            // 시작한다 — 원본을 처음 켠 것과 같다.
            _game.Player.RestoreKnownCities(saved.KnownCities);

            // 후원자 친밀도. 판 26 앞의 세이브에는 없어 다들 0 에서 시작한다 — 게임도 그렇다.
            _game.Player.RestoreCloseness(saved.Closeness);

            // 얼굴과 운명 코드는 판 25 부터 적힌다. 운명 코드가 없으면 얼굴 번호로
            // 물러선다 — 그때까지는 새 놀이가 앞의 열여섯만 고르게 해 둘이 같았다.
            if (saved.Face is { } face) _game.Player.Face = face;
            _game.Player.SetFortune(saved.Fortune ?? _game.Player.Face);
            if (saved.Morale is { } morale) _game.Player.SetMorale(morale);
            _game.Player.RestoreContract(GameSave.ContractOf(saved));
            // 계약 맺어 본 힌트. 이 칸이 없던 옛 세이브라도 지금 맺고 있는 계약만큼은
            // 열어 둔 채로 이어야 한다 — 안 그러면 불러오자마자 그 발견물이 다시 잠긴다.
            _game.Player.RestoreOpenedHints(
                saved.OpenedHints ?? (saved.Contract is { } deal ? [deal.Hint] : null));

            // 발견으로 판매가 켜진 교역품. 이 칸이 없던 세이브는 <b>발견한 발견물의 대본</b>에서 교역품 활성화
            // (01 15)를 찾아 켠다 — 상아(코끼리의 무덤)·후추 따위를 찾아 놓고도 교역소에 안 나오면 안 된다.
            // 대본 안의 갈래는 가리지 않고 그 파트에 적힌 것을 다 켠다.
            // 아이. 이 칸이 없던 세이브는 이름만 있어 RestoreFamily 가 빈 아이를 앉혀 두었다 — 아버지 값으로 채운다.
            if (saved.Children != null) _game.Player.RestoreChildren(saved.Children);
            else if (_game.Player.Children.Count > 0)
                _game.Player.RestoreChildren([.. _game.Player.Children.Select(c => Engine.Town.Home.Bless(_game.Player, _game.Random, c))]);
            _game.Player.RestoreBetrayals(saved.Betrayals);
            _game.Player.RestoreSulks(saved.Sulks);
            // 초심자 개인 퀘스트라인(이야기0/1). 이 칸이 없던 세이브는 새로운 주인공(NORMAL)이거나
            // 이 기능 앞에 지은 판이라 묶인 책이 없는 채로 연다.
            _game.Player.RestoreStory(saved.ActiveStoryBook, saved.StoryQuestDeadline,
                                      saved.StoryProgress, saved.ClosedStoryArcs);
            _game.Player.RestoreActiveGoods(saved.ActiveGoods ??
                _game.Player.Discoveries.SelectMany(id => Engine.Disev.DisevRunner.GoodsActivatedBy(_game, id)));
            if (saved.Fame is { } fame) _game.Player.Fame = fame;
            // 적어 둔 도시 앞바다에 배를 놓는다. 그 도시는 이미 들렀으니 곧바로 다시 묻지 않는다.
            if (saved.CityId >= 0 && _host.PlaceAtCity(saved.CityId)) _askedCity = saved.CityId;
            // 바다에서 적은 판은 적어 둔 칸에 닻을 내린 채로 연다.
            else if (saved.CityId < 0 && saved.SeaX is { } sx && saved.SeaY is { } sy)
                _host.PlaceAtSea(sx, sy);
            _status.Text = saved.CityId >= 0
                ? $"[{saved.CityName}] 에서 이어 간다 — {saved.Date:yyyy년 M월 d일}"
                : $"바다에서 이어 간다 — {saved.Date:yyyy년 M월 d일}";
        }

        _game.Bgm.Play(BgmPlayer.SeaTrack);
        SyncOverlay();

        // 날짜가 다 자리잡은 뒤라야 어느 도시가 섰는지 셀 수 있다. 처음 한 번은
        // 조용히 세고 지도만 갈아 끼운다 — 이어 가는 판에서 스무 줄이 쏟아지면 안 된다.
        _founded = null;
        TellFounded();

        // 적어 둔 자리가 도시면 도시 화면부터 연다. 바다에서 적었으면(CityId 가 -1) 그대로 둔다 —
        // 어디에서 적었는지는 그 값 하나로 갈린다.
        //
        // 지도가 자리를 잡은 뒤에 열어야 도시 그림이 지도 한가운데에 놓인다
        // (MapAreaOnScreen 이 아직 0 이면 엉뚱한 데 뜬다).
        if (!fresh && saved is { CityId: >= 0 })
        {
            int city = saved.CityId;
            string name = saved.CityName.Length > 0 ? saved.CityName : _game.CityName(city);
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (ShowCityPicture(city, name, resumed: true)) _host.Paused = true;
            }), System.Windows.Threading.DispatcherPriority.Loaded);
        }
    }

    /// <summary>
    /// 모드 창 — 원본에 없는 <b>편의 기능</b>만 모아 켜고 끈다.
    /// </summary>
    /// <remarks>
    /// 개발 창에 섞여 있던 컨디션·미니맵·기능·언어·출입 일수를 여기로 옮겼다. 개발 창은
    /// 값을 밀어 넣어 시험하는 데고, 이쪽은 판을 그대로 두고 보기를 거드는 데다.
    /// </remarks>
    private void ShowModDialog() => ModDialog.Show(this, new ModDialog.Options
    {
        MiniMapOn = () => _miniWanted,
        SetMiniMap = on =>
        {
            _miniWanted = on;
            GameSettings.ShowMiniMap = on;   // 다음에 켤 때도 그대로
            SyncOverlay();
        },
        ConditionOn = () => _vitalWanted,
        SetCondition = on =>
        {
            _vitalWanted = on;
            GameSettings.ShowConditionOverlay = on;   // 다음에 켤 때도 그대로
            SyncOverlay();
        },
        ArrowsOn = () => _host.ShowFlowArrows,
        SetArrows = on =>
        {
            _host.ShowFlowArrows = on;
            GameSettings.ShowFlowArrows = on;   // 다음에 켤 때도 그대로
        },
    });

    /// <summary>
    /// 개발 창 — 소지금과 명성을 손으로 넣고, 놀이에 없는 것들을 켜고 끈다.
    /// </summary>
    /// <remarks>
    /// 게임 상단 띠에 칸으로 두었던 것을 제목 줄 햄버거로 옮겼다. 놀이에는 없는 자리라
    /// 게임 띠에 섞여 있으면 원본과 달라 보인다 — 앱이 얹은 것은 앱 쪽 차림표에 둔다.
    /// </remarks>
    private void ShowDevDialog() => DevDialog.Show(this, _game.Player, new DevDialog.Options
    {
        CoordsOn = () => _overlayWanted,
        SetCoords = on =>
        {
            _overlayWanted = on;
            GameSettings.ShowCoordOverlay = on;   // 다음에 켤 때도 그대로
            SyncOverlay();
        },
        // 싸움 셈을 도시 없이 돌려 보는 세 가지 — 미니 게임 차림표에 붙여 두었던 것을 옮겼다.
        HelperApp = RunHelperApp,
        // 묘책 확률 표(0x00549B80) — 보고 고치면 놀이에도 바로 든다.
        RuseTable = () => RuseEditDialog.Show(this),
        Duel = PlayDuel,
        LandSpar = () => LandSparDialog.Play(this, _game),
        SeaSpar = MockSeaBattle,
        // 게임에는 없는 것이라 해상 커맨드에서 개발 창으로 옮겼다(fb-ui-21). 지도를 Shift+오른쪽 클릭해
        // 바로 찍는 길은 그대로다.
        AutoSail = () =>
        {
            if (_host.IsOnLand) Say("바다에 있을 때만 자동항해를 쓸 수 있습니다");
            else ShowAutoSailDialog();
        },
    });

    /// <summary>
    /// 게임 커맨드 창을 흉내낸 우클릭 메뉴. 떠 있는 동안 <b>게임이 멈춘다</b> —
    /// 배도 시간도 그 자리에 선다(닻을 내리는 것과는 다르다. 닻은 그대로 두고 멈추기만 한다).
    /// </summary>
    /// <remarks>
    /// 제 창(HWND)으로 띄운다 — D3D 자식 창 위에 제대로 뜨고(airspace 를 안 탄다),
    /// 제목 줄을 잡아 <b>끌어 옮길 수 있다</b>. 도시정보 창과 같은
    /// <see cref="MenuWindow"/> 를 쓴다.
    ///
    /// 예전에는 <c>Popup</c> 이었는데 두 가지가 걸렸다. 옮길 수가 없었고, 닫힐 때 초점이
    /// 갈 데를 잃어 다른 앱으로 넘어갔다(팝업이 활성창을 가져가는데 지도는 WPF 가 모르는
    /// 자식 창이라 돌려줄 데가 없다). 주인을 둔 창은 닫히면 주인이 되살아나므로 둘 다 없다.
    /// </remarks>
    private void ShowCommandMenu(FrameworkElement anchor, Point at)
    {
        if (_host.SeaBlocked) return;
        if (CommandMenu.IsOpen) { CommandMenu.Focus(); return; }

        CommandMenu.Open(CommandMenuBox, ToScreen(anchor, at));
        _host.Paused = true;
    }

    /// <summary>
    /// 해상 커맨드 창의 줄들. <b>지을 때마다 새로 본다</b> — 바다냐 뭍이냐에 따라 줄이 갈리므로,
    /// 상륙한 자리에서 <see cref="GameMenuHost.Refresh"/> 하면 그대로 승선 줄이 된다.
    /// </summary>
    /// <summary>도시에 드는 문 — 바다에서는 항구(건물 0), 뭍에서는 성문(건물 10).</summary>
    private const int HarborCode = 0, GateCode = 10;

    private GameMenu CommandMenuBox()
    {
        void Close() => CommandMenu.Close();

        // 바다에 있으면 상륙, 뭍에 있으면 출항. <b>갈 데가 없으면 줄 자체를 안 낸다</b> —
        // 흐린 줄로 남겨 두면 창 높이만 잡아먹고 게임에도 없는 모습이다.
        // 게임 커맨드 창에는 없는 줄이지만 이 창에서는 이것으로 뭍을 오간다.
        var items = new List<(string Text, Action? Run)>();

        // 도시에 닿아 있으면 <b>맨 위</b>가 그 도시로 들어가는 줄이다 — 바다든 뭍이든, 창 안에 드는 도시마다
        // 한 줄씩이다(0x0048B1E2). 아는 도시(+4 비트 0)이고 선 도시여야 하며, 바다면 항구(건물 0), 뭍이면
        // 성문(건물 10)이 있어야 한다(0x0048B2B7 ~ 0x0048B2DB). 다가갈 때 한 번 물어보는 창(CheckPort)에서
        // 아니오를 눌렀어도 이 줄로 다시 들어간다. 줄 글은 0x0056F990 "[%s]에 들어간다" 다.
        int door = _host.IsOnLand ? GateCode : HarborCode;
        foreach (int town in _host.TownsAt())
        {
            if (!_game.CityKnown(town) || !(_game.CityRows?.HasBuilding(town, door) ?? true)) continue;
            items.Add(($"[{_game.CityName(town)}]에 들어간다", () => { Close(); AskEnterCity(town); }));
        }

        if (_host.IsOnLand)
        {
            // 뭍에 올라 있는 동안은 보급·수리 줄이 <b>없다</b> — 그 둘은 배에 탄 채로 여는
            // 「상륙」 차림표에 있다(0x0048B1E2~0x0048B4C2 에는 도시·승선·정보·도시좌표·
            //  항해일지·기능뿐이다).
            // 대 둔 배 곁(두 칸 안)이어야 선다(0x0048B397) — 아무 물가에서나 타지는 못한다.
            // 함대가 도시 항구에 들어가 있으면(0x005B6388 — 바다로 들어와 성문으로 탐험 나선 길) 줄이 없다 —
            // 그 도시로 걸어 돌아가야 배에 오른다.
            if (_host.IsNearMoor() && _game.Player.FleetCity < 0)
                // 뭍에서 배로 옮겨 타는 줄은 「승선」이다(0x0056F9A8, 0x0048B3ED) — 「출항」은 항구 것이다.
                items.Add(("승선", () => { if (_host.Embark()) _game.Bgm.Play(BgmPlayer.SeaTrack); Close(); }));
        }
        else if (_host.IsNearLand())
        {
            // 「상륙」은 곧바로 뭍에 올리지 않는다 — 네 줄짜리 차림표가 한 겹 더 있다
            // (0x0048E5E0). 「탐색」만 뭍에 올리고, 보급·수리는 <b>배에 탄 채로</b> 한다.
            items.Add(("상륙", () => CommandMenu.Push(AshoreMenuBox)));
        }

        items.Add(("정보", () => CommandMenu.Push(InfoMenuBox)));
        // 편성·대열은 바다에서만 있다(0x0048B3xx) — 뭍에 올라 있으면 줄이 없다.
        // 대열은 배가 두 척 이상이어야 켜진다.
        if (!_host.IsOnLand)
        {
            items.Add(("편성", () => { Close(); CrewShareDialog.Show(this, _game.Player); }));

            // 「대열」은 <b>배가 둘 이상일 때만 줄이 선다</b> — 원본은 흐린 줄로 두지도 않고
            // 아예 안 넣는다(0x0048B442: 0x00473E00() 이 1 이하면 줄을 건너뛴다).
            // 호위함을 어떻게 세울지 고르는 창이라 한 척뿐이면 고를 것이 없다.
            if (_game.Player.Ships.Count > 1)
                items.Add(("대열", () => { Close(); FormationDialog.Show(this, _game.Player); }));
        }
        // 「도시좌표」는 측량을 아는 사람이 있어야 뜬다(0x0048B469) — 제독이거나 측량사 자리 부하다.
        if (SurveyLevel() >= 1)
            items.Add(("도시좌표", () => { Close(); ShowCityCoordinates(); }));

        items.Add(("항해일지를 본다", () => { Close(); ShowLogbook(); }));
        // 줄은 「기능」에서 끝난다(0x0048B4B5) — 「취소」는 기능 아래에만 있고 커맨드는 오른쪽 단추로 닫는다.
        items.Add(("기능", () => CommandMenu.Push(SeaSystemMenuBox)));

        // 넓히는 것은 GameUi 가 창을 지으며 한다 — 커맨드 창만이 아니라 도시 창·시설 창도
        // 같이 넓어야 모양이 맞는다.
        return new GameMenu("커맨드", null, [.. items]);
    }

    /// <summary>
    /// 커맨드의 "정보" 아래 일곱 줄. 게임 <c>0x00425E40</c> 것 그대로다.
    /// </summary>
    /// <remarks>
    /// <code>
    ///   0x005331A8  함대정보 → 0x0046F340      0x005331D8  힌트정보
    ///   0x005331B8  인물정보 → 0x0046DF70      0x005331E8  계약정보
    ///   0x005331C8  소지품정보 → 0x0044CB20    0x005331F8  지도를 본다
    ///   0x00533210  돌아간다
    /// </code>
    /// 소지품·힌트·계약은 도시 커맨드에서 쓰던 창을 그대로 쓴다 — 게임도 한 창이다.
    /// "지도를 본다" 는 창이 한 겹 더 뜬다(<see cref="MapMenuBox"/>).
    /// </remarks>
    /// <summary>
    /// 바다 커맨드의 "기능" 에서 뻗는 창 — 게임 중단 · 게임 종료 · 취소.
    /// </summary>
    /// <remarks>
    /// <b>도시 안의 기능 창과 다르다.</b> 도시에서는 저장·로드·게임 종료·게임 재개가
    /// 나오는데(<see cref="GameSystemMenu"/>), 바다에서는 이 셋뿐이다 — 바다에서는
    /// 그냥 저장할 수 없고 <b>중단</b>으로만 적는다.
    /// <code>
    ///   0048b703  "게임 중단"                       0x0056FA38
    ///   0048b715  "게임 종료"                       0x0056FA48
    ///   0048b724  "취소"                            0x0056FA58
    ///   0048b731  창 제목 "기능"                     0x0056FA60
    ///   0048b75c  "지금 플레이하고 있는 게임을 중단하겠습니까?"   0x0056FA68
    ///   0048b779  "게임을 종료합니까?"                0x0056FA98
    /// </code>
    /// 예전에는 "기능" 이 곧바로 적고 마는 줄이었다.
    /// </remarks>
    private GameMenu SeaSystemMenuBox() => new("기능", null,
    [
        ("게임 중단", Suspend),
        ("게임 종료", () =>
        {
            if (!ConfirmDialog.Ask(CommandMenu.Window ?? this, "게임을 종료합니까?")) return;
            CommandMenu.Close();
            ReturnToTitle();
        }),
        ("취소", CommandMenu.Close),
    ]);

    /// <summary>
    /// "게임 중단" — 이 자리를 적고 첫 화면으로 돌아간다.
    /// </summary>
    /// <remarks>
    /// 게임도 중단은 <b>적고 나가는</b> 한 몸이다("이 시점에서 데이터를 저장하고 게임을
    /// 중단하겠습니다." <c>0x00568C80</c>). 적지 못했으면 나가지 않는다 — 나가 버리면
    /// 그 판이 그대로 사라진다.
    ///
    /// 적는 자리는 도시에서 적는 것과 같다. 도시에 들어가 있지 않으므로
    /// <see cref="Player.CityId"/> 가 -1 로 남는데, 그 값이 곧 "바다에서 적었다" 는 표시다.
    /// </remarks>
    private void Suspend()
    {
        var owner = CommandMenu.Window ?? this;
        // 바다에서는 <b>한 번만</b> 묻는다(0x0048B75B) — 「이 시점에서 데이터를 저장하고…」(0x00568C80)는
        // 도시 기능 창의 「중단」 줄(0x004A27D0) 말이다.
        if (!ConfirmDialog.Ask(owner, "지금 플레이하고 있는 게임을 중단하겠습니까?")) return;

        _game.Player.SetSeaCell(_host.SeaSpot);
        string error = _game.Save(suspended: true);   // 중단저장(0x004791D0)
        if (error.Length > 0)
        {
            NoticeDialog.Show(owner, $"기록하지 못했다 — {error}");
            return;
        }

        CommandMenu.Close();
        ReturnToTitle();
    }

    /// <summary>
    /// 「상륙」 차림표(<c>0x0048E5E0</c>) — 탐색(<c>0x00570D30</c>) · 보급(<c>0x00570D38</c>) ·
    /// 수리(<c>0x00570D40</c>) · 승선한다(<c>0x00570D48</c>) 넉 줄이고 제목은 「상륙」
    /// (<c>0x00570D58</c>)이다.
    /// </summary>
    /// <remarks>
    /// <b>「탐색」만 뭍에 올린다</b>(<c>0x0048E734</c>). 보급(<c>0x0048DC60</c>)과
    /// 수리(<c>0x0048E140</c>)는 배에 탄 채로 하고 차림표로 되돌아오며, 「승선한다」는 닫는다.
    /// </remarks>
    private GameMenu AshoreMenuBox()
    {
        // <b>창을 닫는 것은 차림표다</b> — 이름만 Close 라고 쓰면 게임 창이 닫혀 놀이가 끝난다.
        void Shut() => CommandMenu.Close();

        return new GameMenu("상륙", null,
        [
            ("탐색", () =>
            {
                if (!_host.Land()) { Shut(); return; }
                _game.Bgm.Play(BgmPlayer.LandTrack);

                // 재해가 풀려 <b>부관이 한 마디 할 때만</b> 창을 남긴다 — 닫으면 그 자리에서
                // 멈춤이 풀려 말이 뜨는 동안 말(馬)이 벌써 달려 나가고, 읽고 나면 바로 승선할
                // 수도 있기 때문이다. 아무 말 없이 상륙했으면 <b>곧바로 닫아</b> 그 자리에서
                // 움직이게 둔다.
                if (EndVoyage()) CommandMenu.Refresh();
                else Shut();
            }),
            ("보급", () => { Forage(); CommandMenu.Refresh(); }),
            ("수리", () => { RepairAshore(); CommandMenu.Refresh(); }),
            ("승선한다", Shut),
        ]);
    }

    private GameMenu InfoMenuBox() => new("정보", null,
    [
        // 바다에서는 함대좌표 칸에 지금 자리를 적는다. 도시 안이라면 게임처럼 "---" 다.
        ("함대정보", () => Info(() => FleetInfoDialog.Show(this, _game.Player, CoordLine(), _game.Items,
                                                        c => GameInfo.CargoLabel(_game, c)))),
        // 부하가 있으면 게임처럼 누구를 볼지 먼저 묻는다 — 도시 창과 한 벌이다.
        ("인물정보", PersonInfo),
        // 설명문과 그림을 <b>같이 넘긴다</b> — null 로 두어 바다에서 연 소지품 창만
        // 그림도 설명도 없이 떴다(도시 창은 넘기고 있었다).
        ("소지품정보", () => Info(() => BelongingsDialog.Show(
            this, _game.Player, _game.Items, _game.ItemText, _game.ItemPictures,
            GameInfo.DiscoveryNames(_game), _game))),
        // 도시 커맨드(CityPicView.ShowHints)와 같은 창이다 — 보고까지 마친 힌트만 빼고, 고르면 설명을 편다.
        // 예전에는 발견만 한 힌트까지 빼는 딴 목록(GameInfo.HintNames)을 써서 바다에서는 비어 보였다(fb-ui-20).
        ("힌트정보", () => Info(ShowHints)),
        ("계약정보", () => Info(ShowContract)),
        ("지도를 본다", () => CommandMenu.Push(MapMenuBox)),
        // 「돌아간다」는 커맨드로 되짚지 않고 <b>커맨드 창을 통째로 닫는다</b> — 0x00425E40 이 돌아가면
        // 0x0048B636 → 0x0048B798 로 창이 끝난다.
        ("돌아간다", CommandMenu.Close),
    ]);

    /// <summary>
    /// 「지도를 본다」에서 뻗는 창 — 항해지도 · 주변지도 · 돌아간다.
    /// </summary>
    /// <remarks>
    /// 게임의 <c>0x0042602E</c> 자리다(<c>0x00533240</c> · <c>0x00533250</c> ·
    /// <c>0x00533260</c>, 창 제목 <c>0x00533270</c>).
    ///
    /// 둘은 아주 다른 그림이다. <b>항해지도</b>(<c>0x00416A00</c>)는 밝힌 자리만
    /// 드러나는 양피지 지도고, <b>주변지도</b>(<c>0x00416B60</c>)는 배 둘레를
    /// <c>(측량 + 2) / 16</c> 칸 걸음으로 크게 보며 알고 서 있는 도시와 이미 찾았거나 발표된
    /// 발견물의 그림 칸을 밝힌다. 둘 다 <see cref="SeaChartDialog"/> 가 낸다.
    ///
    /// 지도 창을 닫으면 <b>이 메뉴로 되돌아온다</b>(<c>0x0042617F</c>) — 창이 떠 있는 동안
    /// 커맨드 창은 감춰 둘 뿐 닫지 않는다. 멈춤은 커맨드 창이 떠 있는 동안 그대로 걸려 있다.
    /// </remarks>
    private GameMenu MapMenuBox() => new("지도를 본다", null,
    [
        ("항해지도", () => WithMenuHidden(CommandMenu.Window, () =>
            SeaChartDialog.ShowWorld(this, _host, _game.Player.Explored))),
        ("주변지도", () => WithMenuHidden(CommandMenu.Window, () =>
            SeaChartDialog.ShowAround(this, _host, _game.Discoveries, _game.Player,
                                      _game.CityVisible, SurveyLevel()))),
        ("돌아간다", CommandMenu.Pop),
    ]);

    /// <summary>
    /// 지도에 쓰는 측량술 — <c>0x0047CCA0(제독, 기능 7, 2, -1, -1, -1)</c> 의 <c>+0x5C</c>.
    /// </summary>
    /// <remarks>
    /// 게임은 제독과 <b>측량사 자리 부하</b>(부하 자리 2, <c>0x0047CCA0(7, 2, …)</c>) 가운데 높은 쪽을 쓴다.
    /// 부하 기능은 인물 표(<see cref="PersonTable"/>)에서 본다 — 부하 자료 쪽에는 기능이 없다.
    /// </remarks>
    private int SurveyLevel()
    {
        int best = _game.Player.LevelOf(Skill.Names[CityCoordinates.SurveySkill]);
        string mate = _game.Player.MateAt(CityCoordinates.SurveyorSlot);
        if (mate.Length > 0 && _game.World?.People.FirstOrDefault(r => r.Name == mate) is { } row
            && CityCoordinates.SurveySkill < row.Skills.Length)
            best = Math.Max(best, row.Skills[CityCoordinates.SurveySkill]);
        return best;
    }

    /// <summary>함대정보 판의 함대좌표 줄. 게임 말투 그대로 "북위 38도 서경 9도" 다.</summary>
    private string CoordLine()
    {
        if (_host.SeaBlocked) return "";
        var (lat, lon) = _host.ShipLatLon;
        return $"{(lat >= 0 ? "북위" : "남위")} {Math.Abs(lat),3:F0}도" +
               $"  {(lon >= 0 ? "동경" : "서경")} {Math.Abs(lon),3:F0}도";
    }

    /// <summary>
    /// 계약 정보 판. 계약이 없으면 게임처럼 한 줄로 물린다.
    /// </summary>
    /// <remarks>판에 채울 것은 도시 창과 한 벌이다 — <see cref="GameInfo.ContractSheetOf"/>.</remarks>
    private void ShowContract()
    {
        var sheet = GameInfo.ContractSheetOf(_game);
        if (sheet.Contract == null)
        {
            NoticeDialog.Show(this, "계약을 맺지 않았습니다");
            return;
        }
        ContractDialog.Show(this, sheet.Contract, _game.Player.Date,
                            sheet.HintName, sheet.Found, sheet.Evidence,
                            _game.Sponsors?.FindByName(sheet.Contract?.Sponsor ?? "")?.Name);
    }

    /// <summary>
    /// 지도 아래 띠에 한마디 적는다. 게임이 창을 띄우지 않고 알리는 자리다.
    /// </summary>
    /// <remarks>도시 창처럼 이 창이 거느린 쪽에서도 부른다.</remarks>
    /// <summary>
    /// 하단 띠에 한 줄 적는다 — 게임처럼 <b>네 번 깜빡이고</b> 한참 뒤에 지워진다.
    /// </summary>
    /// <remarks>
    /// 띠 알림 객체(<c>0x00580C48</c>)의 한 틱이 <c>0x0040DE80</c> 이다.
    /// <code>
    ///   c = [+0xC0]
    ///   if (c &lt; 0x28)            ; 마흔 틱 동안
    ///       c++ ; if (c % 10 == 0) 다시 그린다      ; 열 틱마다 — 이것이 깜빡임이다
    ///   else
    ///       n = [+0xBC]           ; 적을 때 넘긴 값 x 20 (0x0040E15A, 5 를 넘기므로 100)
    ///       if (n) { n-- ; if (n == 0) 다시 그린다 } ; 다 세면 지운다
    /// </code>
    /// 곧 <b>마흔 틱 동안 열 틱마다 깜빡이고, 그 뒤 백 틱을 버티다 사라진다.</b>
    /// 적는 손(<c>0x0040E0A0</c>)은 적은 뒤 늘 소리 <c>0x1D</c> 를 낸다.
    ///
    /// 빈 글을 주면 그 자리에서 지운다.
    /// </remarks>
    public void Say(string text)
    {
        // 게임은 글을 넣기 앞서 통을 비운다(0x0040E0D7) — 그래서 통에는 <b>마지막 하나</b>만 남는다.
        if (text.Length > 0) _lastNote = text;
        SetNoteText(text);
        _note.Visibility = Visibility.Visible;
        if (_titleNote != null) _titleNote.Visibility = Visibility.Visible;
        _noteTick = 0;

        _noteTimer ??= new DispatcherTimerLite(TimeSpan.FromMilliseconds(100), NoteTick);
        if (text.Length == 0) _noteTimer.Stop();
        else _noteTimer.Start();
    }

    /// <summary>띠 알림이 깜빡이는 동안의 틱 수(<c>0x28</c>)와 한 번 깜빡이는 사이(10틱).</summary>
    private const int NoteBlinkTicks = 0x28, NoteBlinkEvery = 10;

    /// <summary>깜빡임이 끝난 뒤 버티는 틱 수 — 게임이 넘기는 5 에 20 을 곱한 값이다.</summary>
    private const int NoteHoldTicks = 100;

    private DispatcherTimerLite? _noteTimer;
    private int _noteTick;

    /// <summary>띠에 마지막으로 적은 글 — 띠를 눌러 다시 펴 볼 때 쓴다.</summary>
    /// <remarks>
    /// 게임은 글통(<c>+0xC4</c>)에 <b>마지막 하나만</b> 담는다 — <c>0x0040E0C0</c> 이 새 글을
    /// 넣기 앞서 통을 비우기 때문이다(<c>0x0040E0D7</c>). 띠가 흐려진 뒤에도 통은 그대로라
    /// 눌러 보면 나온다. 비우는 것은 띠를 감출 때뿐이다(<c>0x0040E060</c>).
    /// </remarks>
    private string _lastNote = "";

    private void SetNoteText(string text)
    {
        _note.Text = text;
        if (_titleNote != null) _titleNote.Text = text;
    }

    /// <summary>
    /// 띠를 눌러 마지막 알림을 다시 편다(<c>0x0040DE30</c>).
    /// </summary>
    /// <remarks>
    /// <code>
    ///   0040de38  누름이면
    ///   0040de3d  글통에 글이 있으면(+0xB8 비트 0)
    ///   0040de64  0x0049E3E0(0, "Information"(0x00535C84), "%s"(0x0052F858), 글)
    /// </code>
    /// 제목이 한글이 아니라 <b>Information</b> 이다 — 원본 그대로 둔다.
    /// </remarks>
    private void ReadNote()
    {
        if (_lastNote.Length == 0) return;
        NoticeDialog.Show(this, _lastNote, "Information");
    }

    private void NoteTick()
    {
        _noteTick++;

        if (_noteTick <= NoteBlinkTicks)
        {
            // 열 틱마다 한 틱을 비운다 — 게임이 그 자리에서 띠를 다시 그리며 깜빡이는 것이다.
            var visibility = _noteTick % NoteBlinkEvery == 0
                ? Visibility.Hidden
                : Visibility.Visible;
            _note.Visibility = visibility;
            if (_titleNote != null) _titleNote.Visibility = visibility;
            return;
        }

        _note.Visibility = Visibility.Visible;
        if (_titleNote != null) _titleNote.Visibility = Visibility.Visible;
        if (_noteTick < NoteBlinkTicks + NoteHoldTicks) return;

        SetNoteText("");
        _noteTimer?.Stop();
    }

    /// <summary>정보 판 하나를 띄운다 — 커맨드 창은 접고, 배는 세워 둔 채다.</summary>
    /// <summary>얻은 힌트를 늘어놓고, 한 줄을 고르면 그 이야기를 편다 — 도시 쪽과 한 벌이다.</summary>
    private void ShowHints()
    {
        var player = _game.Player;
        var ids = _game.Discoveries?.LiveHints(player) ?? [.. player.Hints.Order()];

        while (true)
        {
            int at = HintListDialog.Pick(this, [.. ids.Select(id => GameInfo.HintLabel(_game, id))]);
            if (at < 0 || at >= ids.Count) return;
            if (_game.Hints?.Find(ids[at]) is not { } hint) return;

            HintDetailDialog.Show(this, hint, _game.Hints.CategoryOf(hint.Category),
                                  player.Fame, _game.MateSpeaks, player.Contract?.Hint == hint.Id);
        }
    }

    /// <summary>
    /// 정보 판 하나를 띄운다. 판을 닫으면 <b>정보 목록으로 되돌아온다</b> — 원본 0x00425E40 은 「돌아간다」(6)를
    /// 고를 때까지 목록을 되풀이한다(0x004261E6). 판이 떠 있는 동안 커맨드 창은 감춰 둘 뿐이라 멈춤도 그대로다.
    /// </summary>
    private void Info(Action show) => WithMenuHidden(CommandMenu.Window, show);

    /// <summary>
    /// 인물정보 — 부하가 있으면 <b>이 창 위에 한 겹</b>을 쌓아 누구를 볼지 묻는다.
    /// </summary>
    /// <remarks>
    /// <b>먼저 닫으면 안 된다.</b> 커맨드 창은 점으로 오므라든 뒤에 닫히는데
    /// (<c>MenuWindow.CloseZoomed</c>), 그 사이에 겹을 쌓으면 쌓은 겹이 닫히는 창에 실려
    /// <b>그대로 같이 닫힌다</b> — 「인물정보를 눌러도 아무것도 안 열리던」 것이 이것이다.
    /// 부하가 없을 때는 물을 것이 없으니 예전처럼 곧장 판을 낸다.
    /// </remarks>
    private void PersonInfo() =>
        // 판이 떠 있는 동안 커맨드 창은 감춰 둘 뿐이라 멈춤도 그대로 걸려 있다.
        PersonInfoMenu.Show(this, _game, CommandMenu);

    /// <summary>해상 커맨드 창. 하나만 띄운다.</summary>
    private GameMenuHost? _commandMenuHost;

    private GameMenuHost CommandMenu
    {
        get
        {
            if (_commandMenuHost != null) return _commandMenuHost;
            _commandMenuHost = new GameMenuHost(this);
            // 메뉴가 떠 있는 동안은 게임을 멈춘다. 닫히면 다시 흐른다 — 다만
            // <b>남의 멈춤을 밟지 않는다</b>.
            //
            // 커맨드 창은 점으로 오므라든 뒤에 닫히므로(GameMenuHost.Close 의
            // CloseZoomed) 이 알림이 <b>한 박자 늦게</b> 온다. 그 사이에
            // 「…에 들어간다」가 이미 EnterCity 로 들어가 멈춤을 새로 잡았는데
            // 여기서 무턱대고 풀어 버리면, 성문 창이 떠 있는 채로 말이 계속 달려
            // 대륙 끝까지 가 있었다.
            _commandMenuHost.Closed += () =>
            {
                if (_asking || _host.SeaBlocked) return;
                _host.Paused = false;
            };
            return _commandMenuHost;
        }
    }

    /// <summary>도시에 다가가면 한 번 물어본다. 떠났다 다시 와야 또 묻는다.</summary>
    private void CheckPort()
    {
        // 멈춰 있으면(커맨드 창) 아무것도 묻지 않는다 — 멈춘 동안 창이 겹쳐 뜨면 안 된다.
        if (_asking || _host.Paused) return;

        // 배는 항구 칸으로, 말은 도시 칸으로 잰다. 게임도 그렇게 갈라 본다.
        bool byLand = _host.IsOnLand;
        int city = byLand ? _host.NearestTown() : _host.NearestCity();
        if (city < 0) { _askedCity = -1; return; }      // 도시를 벗어났다
        if (city == _askedCity) return;                 // 이미 물어본 도시다
        _askedCity = city;

        var name = _game.CityName(city);
        // 물음창이 떠 있는 동안 배가 계속 가면 대답할 새가 없다.
        _asking = true;
        _host.Paused = true;
        bool inCity = false;
        try
        {
            // 게임도 그냥 물음창이다 — 짙은 밤색 판에 흰 글씨, 아래에 YES/NO 둘.
            // 배면 "항구로", 말이면 "도시로" 로 갈아 낸다(문구는 하나다).
            //
            // 묻는 것이 먼저다. 게임도 이 자리(0x004687EC)에서 대원 대사까지 낸 다음에야
            // 출입여부를 본다(0x004687FD) — 들어가겠다고 해야 적대 차림표가 뜬다.
            // (「제독, 도시가 보입니다!」는 여기가 아니라 <b>도시를 처음 알아볼 때</b> 나온다 —
            //  SpotCities 로 옮겼다.)
            string where = byLand ? "도시" : "항구";

            // <b>피로도가 60 이상이면 말이 다르다</b>(0x0048DBCA) — 물음인 것은 같다.
            if (!ConfirmDialog.Ask(this, _game.Player.Fatigue >= TiredToRest
                    ? $"[{name}]의 {where}입니다. 모두 지쳐 있으니 {where}로 들어갑시다."
                    : $"[{name}]의 {where}로 들어가겠습니까?")) return;

            // 막힌 도시면 여기서 공격·잠입·교섭·떠난다가 뜬다(0x00468804).
            if (!PassGate(city, name, byLand)) return;

            _host.EnterPort(name);
            inCity = ShowCityPicture(city, name);
        }
        finally
        {
            // 도시 창이 열렸으면 그 창이 닫힐 때 푼다(그동안 배는 서 있는다).
            if (!inCity)
            {
                _host.Paused = false;
                _asking = false;
            }
        }
    }

    /// <summary>
    /// 커맨드로 고른 도시에 들어갈지 한 번 묻는다(<c>0x0048B4F8</c>).
    /// </summary>
    /// <remarks>
    /// 다가갈 때 뜨는 물음(<see cref="CheckPort"/>)과는 <b>글이 다르다</b> —
    /// 이쪽은 <c>0x0056FA20</c> 「[%s]에 들어갑니다」다.
    /// </remarks>
    private void AskEnterCity(int city)
    {
        _asking = true;
        _host.Paused = true;
        bool ok;
        try { ok = ConfirmDialog.Ask(this, $"[{_game.CityName(city)}]에 들어갑니다"); }
        finally { _host.Paused = false; _asking = false; }

        if (ok) EnterCity(city);
    }

    /// <summary>
    /// 묻지 않고 그 도시로 들어간다 — 커맨드의 "…에 들어간다" 가 부른다.
    /// </summary>
    /// <remarks>
    /// 다가갈 때 한 번 묻는 <see cref="CheckPort"/> 와 들어가는 대목은 같다. 다만 이쪽은
    /// 이미 고른 뒤라 다시 묻지 않고, 그 도시를 물어본 것으로 적어 둔다 — 안 그러면 도시
    /// 창을 닫자마자 물음창이 또 뜬다.
    /// </remarks>
    private void EnterCity(int city)
    {
        // 전염병 걸린 함대가 통상인 마을에 들면 마을에 옮는다(0x00477124) — 병이 풀리기 전에 본다.
        if (_game.Player.Has(SeaAilment.Plague)) SpreadPlague(city);
        // 그 다음, 후원자의 나라가 멸망했으면 이 항구에서 소문을 듣고 계약이 깨진다(0x00476F50).
        CheckSponsorFallen(city);

        // 마을에 닿으면 항해가 끝난다 — 쥐·병이 풀리고 부관이 알린다.
        EndVoyage();
        if (_asking) return;

        _askedCity = city;
        var name = _game.CityName(city);
        _asking = true;
        _host.Paused = true;
        bool inCity = false;
        try
        {
            // 커맨드로 곧장 들어가도 문은 같다 — 적대 도시면 차림표부터다.
            if (!PassGate(city, name, byLand: _host.IsOnLand)) return;

            _host.EnterPort(name);

            // 행적에 적는다(원본 갈래 0, 0x0049270F — 도시 화면을 펼 때) — 은퇴하면 이 줄들이 누적 캐릭터의 발자취가 된다.
            _game.Player.Note(Player.TraceArrival, city, _game.Player.Nation);

            inCity = ShowCityPicture(city, name);
        }
        finally
        {
            if (!inCity)
            {
                _host.Paused = false;
                _asking = false;
            }
        }
    }

    /// <summary>
    /// 도시에 들어선 그 자리에서 <b>자동저장</b> 한다 — 모드 창에서 켰을 때만이다.
    /// </summary>
    /// <remarks>
    /// 원본에 없는 것이다. 배로 입항하든 뭍으로 성문을 지나든 이 자리를 거치므로 <b>항구가
    /// 없는 내륙 마을</b>에서도 적힌다. 적는 자리는 <see cref="Engine.GameSave.AutoPath"/> 라
    /// 손으로 적어 둔 <c>SAVEDATA.CDS</c> 는 안 건드린다. 적다 넘어져도 놀이는 그대로
    /// 굴러가야 하므로 띠에 한 줄만 남기고 지나간다.
    /// </remarks>
    private void AutoSaveHere()
    {
        if (!GameSettings.AutoSaveOnPort) return;

        string error = _game.AutoSave();
        Say(error.Length == 0 ? "자동저장했습니다" : $"자동저장하지 못했습니다 — {error}");
    }

    /// <summary>
    /// 함대의 전염병이 마을에 옮는다 — 상태가 통상일 때만 전염병이 되고 영영 안 풀린다(<c>0x00477124</c>).
    /// </summary>
    /// <remarks>
    /// 게임은 입항의 열흘을 흘린 뒤, 병을 풀기(<c>0x00476F50</c>) 전에 이것을 본다. 말은 얼굴 없는 창이다.
    /// </remarks>
    private void SpreadPlague(int city)
    {
        if (_game.Rates.StateOf(city) != CityState.Normal) return;
        _game.Player.SetCityState(city, CityState.Plague);
        NoticeDialog.Show(this, CityState.SpreadWord);
    }

    /// <summary>
    /// 후원자의 나라가 망했다는 소문을 입항한 항구에서 듣고 계약이 깨진다(<c>0x00476F50</c>).
    /// </summary>
    /// <remarks>
    /// <code>
    ///   계약 중 · 항구 사람과 말이 3 으로 통함(0x00468F70, 제독·부관 자리 0·3) · 이 도시 나라 출입여부가 2 가 아님
    ///   · 후원자 나라 형편(0x005859C0+나라*16 +4) 이 2(멸망, HIST_EV 22 00)
    ///   → 항구 사람 · 감찰관 · 항구 사람 · 부관 네 마디, 계약 파기(0x00477050), 「%s와의 계약은 파기되었습니다!」
    /// </code>
    /// 원본 한국어판은 이 다섯 줄이 일본어(Shift-JIS) 그대로라 깨져 나온다. 뜻을 옮겨 한국어로 넣었다.
    /// </remarks>
    private void CheckSponsorFallen(int city)
    {
        var player = _game.Player;
        if (player.Contract is not { } deal) return;
        if (_game.Sponsors?.FindByName(deal.Sponsor) is not { Nation: >= 0 } sponsor) return;
        _game.CatchUpMonths();
        if (!player.IsNationFallen(sponsor.Nation)) return;

        int nation = _game.CityRows?.NationOf(city) ?? -1;
        if (Standoff.EntryOf(player, _game.Nations, nation) == 2) return;

        // 항구 사람 말(그 나라 말)을 제독·부관 가운데 누군가 막힘없이(3) 해야 소문을 알아듣는다.
        int language = _game.Nations?.Find(nation)?.Language ?? -1;
        if (language is >= 0 and < 14)
        {
            int best = player.TongueOf(Skill.Languages[language]);
            foreach (int slot in (int[])[0, 3])
            {
                string mate = player.MateAt(slot);
                if (mate.Length > 0 && _game.World?.People.FirstOrDefault(r => r.Name == mate) is { } row)
                    best = Math.Max(best, row.Languages[language]);
            }
            if (best < 3) return;
        }

        int culture = _game.CityRows?.CultureOf(city) ?? 0;
        var port = _game.SpeakerFace(0, culture);
        var inspector = _game.Faces?.TryGetBgra(Inspector.Face, female: false);
        string fallen = _game.Nations?.Find(sponsor.Nation)?.Name ?? "";

        _asking = true;
        _host.Paused = true;
        TalkDialog.Say(this, port, "", $"어이, 자네 들었나? {fallen}{GameUi.Josa(fallen, "이", "가")} 멸망했다는 소문이야.");
        TalkDialog.Say(this, inspector, "", "그, 그럴 수가, 말도 안 돼! 네놈 그 이야기를 누구한테서 들었나!?");
        TalkDialog.Say(this, port, "", "자네들과 같은 뱃사람이지. 거짓말이라고 생각하면 직접 확인해 보게!");
        TalkDialog.Say(this, MateFace(), "", "이럴 수가. 이래서는 계약은 없던 일이 되겠군요.");
        player.EndContract();
        NoticeDialog.Show(this, $"{deal.Sponsor}{GameUi.Josa(deal.Sponsor, "과", "와")}의 계약은 파기되었습니다!");
        _host.Paused = false;
        _asking = false;
    }

    /// <summary>
    /// 그 도시의 문을 지난다 — 적대 도시면 차림표를 내고, 들어가도 되면 참.
    /// </summary>
    /// <remarks>
    /// 게임은 입항을 시도할 때 문을 <b>두 번</b> 본다(<c>0x00468790</c>).
    /// <code>
    ///   468804  그 나라 적대도 &gt; 0        →  적대 차림표 0x004A56F0
    ///   46882C  트루데시야스 조약에 막힘   →  같은 차림표 0x0046ABB0
    /// </code>
    /// 교섭·잠입으로 뚫어도 <b>그때 한 번</b>이다 — 성공 갈래(<c>0x004A5669</c> · <c>0x004A53DC</c>)는 남는 것을
    /// 아무것도 적지 않아 다음에 오면 차림표가 또 뜬다. 공략에 이기면 도시가 제 나라로 넘어와 그 뒤로는 안 막힌다.
    ///
    /// <b>적대도는 아직 오를 일이 없다</b> — 게임에서도 켤 때는 죄다 0 이고
    /// (<c>0x005859C0</c> 은 <c>.bss</c> 다) 무엇이 처음 올리는지를 못 짚었다. 그래서
    /// 지금 이 문이 실제로 닫히는 것은 <b>조약</b> 쪽뿐이다.
    /// </remarks>
    private bool PassGate(int city, string name, bool byLand)
    {
        int nation = _game.CityRows?.NationOf(city) ?? -1;
        int entry = Standoff.EntryOf(_game.Player, _game.Nations, nation);
        bool angry = nation >= 0 && Standoff.Barred(entry, byLand);
        bool treaty = Standoff.TreatyBars(_game.Player.Date.Year, _game.Player.Nation, nation);
        if (!angry && !treaty) return true;

        // 조약 쪽은 무엇에 막혔는지를 먼저 이른다(0x0046A6C0) — <b>문지기가 얼굴을 걸고</b>
        // 말하고, 마을(0x00552208)과 항구(0x00552240)의 글이 다르다.
        if (treaty && !angry)
        {
            string theirs = _game.Nations?.Find(nation)?.Name ?? "";
            string mine = _game.Player.NationName;
            int culture = _game.CityRows?.CultureOf(city) ?? 0;
            var gate = _game.SpeakerFace(Standoff.GateSpeaker(byLand), culture);
            TalkDialog.Say(this, gate, "", byLand
                ? string.Format(Standoff.TreatyWord, Standoff.TreatyName, mine)
                : string.Format(Standoff.TreatyPortWord, theirs, Standoff.TreatyName, mine));
        }

        // 도시 그림이 펴지는 동안은 이미 도시에 닿은 것이다 — 지도에 남색 막을 씌운다.
        // 그림을 못 여는 판이면 차림표만 뜨므로 막도 씌우지 않는다.
        bool veiled = _game.CityPics != null;
        if (veiled) SetInCity(true);

        var end = HostileCityMenu.Run(this, _game, city, name, byLand, MapAreaOnScreen(),
                                      byTreaty: treaty && !angry);

        // 성문 앞에서는 막이 씌워지고 그 도시 곡이 돌았다. 못 들어가고 물러서면 되돌린다 —
        // 들어갔으면 ShowCityPicture 가 막도 곡도 제 것으로 다시 건다.
        if (!end.Entered)
        {
            if (veiled) SetInCity(false);
            _game.Bgm.Play(_host.IsOnLand ? BgmPlayer.LandTrack : BgmPlayer.SeaTrack);
        }

        if (end.GameOver)
        {
            GameOver(end.Picture);
            Dispatcher.BeginInvoke(ReturnToTitle);
            return false;
        }

        return end.Entered;
    }

    /// <summary>
    /// 배(또는 말)가 선 칸에 발견물이 있으면 발견한다. 게임의 <c>0x0048D3F0</c> 자리다 —
    /// 그쪽도 항해 루프를 한 번 돌 때마다 이것을 한다.
    /// </summary>
    /// <remarks>
    /// 판정은 <see cref="DiscoveryLog.At"/> 가 하고, 여기서는 <b>언제 묻는지</b>만 맡는다.
    /// 창이 떠 있거나 멈춰 있으면 건너뛴다 — 도시 물음창과 겹쳐 뜨면 안 된다.
    ///
    /// 원본은 발견물마다 DISEV.CDS 의 사건을 틀지만 여기서는 알림 한 줄로 갈음한다.
    /// 문구는 게임의 <c>0x00538490</c> ("%s%s [%s]%s 발견했습니다") 그대로다.
    /// </remarks>
    /// <summary>
    /// 바다에서 날이 가게 한다. 하루가 넘으면 사건을 굴린다.
    /// </summary>
    /// <remarks>
    /// 게임은 <b>고리를 한 바퀴 돌 때마다</b> 발밑 칸의 눈금을 하나 붙이고, 마흔여덟
    /// 눈금이 하루다(<c>0x0048EF7D</c> → <c>0x0044AF90</c> 의 <c>cmp eax, 0x30</c>).
    /// 붙이는 자리가 고리 안이라 조건이 없다 — <b>칸을 넘든 안 넘든</b> 한 바퀴에 하나다.
    ///
    /// <b>칸마다 붙이던 것이 틀렸다.</b> 한 걸음은 한 칸이 아니다. 뭍은
    /// <see cref="Sailing.LandSpeed"/> 가 2 라 걸음이 <c>(3x2+54)/10/16 = 0.375</c>칸이고,
    /// 그래서 한 칸에 걸음이 <b>2.67</b> 번 든다. 육지(눈금 2)라면 칸마다 5.3눈금이라
    /// 하루에 아홉 칸 남짓인데, 칸마다 2눈금만 붙이면 스물넉 칸이 되어 <b>날이 세 배 가까이
    /// 빨리 갔다</b> — 세빌리아에서 톨레도(스물한 칸)까지가 사흘이 아니라 하루였던 것이
    /// 이것이다.
    ///
    /// 바다도 같이 바로잡힌다. 걸음이 배의 이동값에 딸리므로 <b>느린 배는 같은 거리에
    /// 날이 더 든다</b> — 칸마다 세던 동안에는 배가 아무리 느려도 하루에 마흔여덟 칸이었다.
    ///
    /// 마을에 들어가 있거나 멈춰 있는 동안에는 날이 안 간다 — 그때는 걸음도 안 는다.
    /// </remarks>
    private void PassTime()
    {
        if (_asking || _host.Paused || _host.SeaBlocked) return;

        // <b>닻을 내려도 날은 간다</b> — 게임은 자리만 안 옮기고(0x0048D14F) 칸 눈금은
        // 조건 없이 쌓는다(0x0048EF64 → 0x0044AF90). 그래서 서 있어도 식량·물·피로가 흐른다.
        // 지난번에 세고 나서 고리를 몇 바퀴 돌았는지. 게임 고리 한 바퀴가 한 눈금이다.
        long walked = _host.Ticks;
        int since = (int)Math.Min(walked - _steps, MaxCatchUp);
        if (since <= 0) { _steps = walked; return; }
        _steps = walked;

        // 무리가 붙는 주사위는 날이 아니라 걸음마다 굴린다 — 게임도 고리 한 바퀴에 한 번이다.
        // 이번 틱에 보이는 배가 새로 붙었으면 굴리지 않는다(0x0048C126).
        if (!_host.IsOnLand && !_folkEntered) CheckEncounter(since);

        _ticks += since * TerrainTable.TicksOfClass(_host.TerrainClass);
        if (_ticks < TerrainTable.TicksPerDay) return;

        // 한 번에 여러 날이 넘어갈 수 있다 — 사막처럼 눈금이 굵은 데를 지날 때다.
        int days = _ticks / TerrainTable.TicksPerDay;
        _ticks -= days * TerrainTable.TicksPerDay;

        // 날을 다 보낸 뒤 이레째 날이거나 이레 넘게 흘렀으면 바람을 다시 흔든다(0x0044B25F → 0x00424E50).
        // 앞에서 걸어 두면 이 고리가 끝나고 처음 읽을 때 새로 굴린다.
        int serialAfter = Engine.Town.Vitality.DaySerial(_game.Player.Date.AddDays(days));
        if (days >= 7 || serialAfter % 7 == 0) _host.ShiftWind();

        for (int i = 0; i < days; i++)
        {
            // 새 도시가 섰으면 알린다 — 날이 간 뒤라야 그 달로 넘어간 것이 보인다.
            TellFounded();

            // 뭍은 따로 센다 — 보급도 항해일도 없고 여행비와 규율만 움직인다.
            if (_host.IsOnLand)
            {
                PassLandDay();
                PassVitalityDay();
                // 선원 0 검사는 뭍의 하루 뒤에도 돈다(0x00475A2C 는 뭍 갈래 뒤에 있다).
                if (CrewGone()) return;
                continue;
            }

            // 차례는 원본 하루(0x0044B18A ~ 0x0044B23E) 그대로다 — 제독 컨디션(0x0047CE80)이 먼저,
            // 그 다음 0x00475470 안에서 보급 · 피로 · 규율 · 바다 사건 · 재해 피해 · 선원 0 검사, 하루 사건(0x00426E80)이 끝이다.
            _game.Player.PassDayAtSea();
            RollWeather();

            // 제독 HP 도 닳는다 — 이레마다·병마다(0x0047CEE0).
            PassVitalityDay();

            var (lat, _) = _host.ShipLatLon;
            Tell(SeaEvents.PassDay(_game.Player, lat, _game.Random, FleetLevel(Skill.Sailing)));
            PassSeaMorale();
            CheckSeaEvent();

            // 서 있는 재해가 날마다 해를 끼친다 — 쥐는 식량을, 병은 선원을(0x00474DA0).
            SeaEvents.Ail(_game.Player, _game.Random, MateSheetAt);
            TellCrewShort();

            if (CrewGone()) return;
            CheckSeaDailyEvent();
        }
    }

    /// <summary>
    /// 선원이 다 죽었으면 놀이가 끝난다 — 하루 셈 끝에 도시 밖이고 선원 합이 0 이면
    /// <c>0x0044AF40(0x5A4D18, 1)</c> 로 GAME OVER 다(<c>0x00475A2C</c>). 바다든 뭍이든 본다.
    /// </summary>
    private bool CrewGone()
    {
        if (_game.Player.Ships.Count == 0 || _game.Player.Crew > 0) return false;
        _host.Paused = true;
        _asking = true;                      // 창이 떠 있는 동안 하루 셈이 다시 안 돌게
        GameOver(GameOverDialog.FleetLost);   // 까닭 1
        _asking = false;
        Dispatcher.BeginInvoke(ReturnToTitle);
        return true;
    }

    /// <summary>
    /// 항해가 끝났다 — 상륙하거나 마을에 들었다. 서 있던 재해가 풀리고 항해일수가 0 이 된다.
    /// </summary>
    /// <remarks>
    /// 게임은 항해일수(<c>0x005A4D40</c>)와 재해 비트(<c>0x005B39FC</c>)를 늘 함께 0 으로 둔다.
    /// 그중 <c>0x0048E5E0</c> 은 풀기 전에 서 있던 재해마다 부관이 한 줄씩 말한다.
    /// 세 자리 가운데 어느 것이 상륙·입항인지는 아직 이름표를 안 붙여, 둘 다 말하게 둔다.
    /// </remarks>
    /// <returns>부관이 한 마디라도 했으면 참 — 서 있던 재해가 있었다는 뜻이다.</returns>
    private bool EndVoyage()
    {
        var player = _game.Player;
        var was = player.CureAilments();
        player.SetDaysAtSea(0);

        bool said = false;
        foreach (string line in SeaEvents.CureWords(was))
        {
            ConfirmDialog.Tell(this, line, face: MateFace());
            said = true;
        }
        return said;
    }

    /// <summary>
    /// 새로 선 도시를 지도에 올린다 — 신대륙 식민 도시가 해마다 하나씩 늘어난다.
    /// </summary>
    /// <remarks>
    /// 언제 무엇이 서는지는 <see cref="CityFounding"/> 에 있다. 1531년에는 파나마가 선
    /// 다음 달에 레온·코로·투르히요가 <b>한꺼번에</b> 선다 — 게임의 제작 오류를 그대로
    /// 옮긴 것이라 그 무더기도 그대로 나온다.
    /// </remarks>
    private void TellFounded()
    {
        var now = CityFounding.FoundedBy(_game.Player.Date);

        // 처음 셀 때는 알리지 않는다 — 이어 가는 판이면 이미 다 선 뒤다.
        // 발견 대본이 도시를 세우고 없앤 것은 알리지 않고 지도만 다시 짓는다(0x0040A038 은 말이 없다).
        int scripted = _game.Player.ScriptedCities.Count;
        if (_founded == null) { _founded = now; _scriptedSeen = scripted; HideCities(now); return; }
        if (now.Count == _founded.Count)
        {
            if (scripted != _scriptedSeen) { _scriptedSeen = scripted; HideCities(now); }
            return;
        }
        _scriptedSeen = scripted;

        // 창으로 알리지는 않는다 — 원본은 역사 대본이 그 도시 소문 가게에 「…항이 생겼다는군」 같은 글을
        // 적어 둘 뿐이고(HIST_EV 20 0A), 그것은 술집·여관 무명 손님이 말한다(CityHistory.Run).
        _founded = now;
        HideCities(now);
    }

    /// <summary>
    /// 지도에서 지울 도시들 — <b>아직 안 섰거나, 아직 모르는</b> 도시다.
    /// </summary>
    private void HideCities(IReadOnlySet<int>? founded = null)
    {
        var up = founded ?? CityFounding.FoundedBy(_game.Player.Date);
        var gone = new List<int>();

        for (int city = 0; city < CityExeTable.Count; city++)
        {
            bool standing = _game.Player.ScriptedCities.TryGetValue(city, out bool set) ? set
                            : !CityFounding.Hidden.Contains(city) || up.Contains(city);
            if (!standing || !_game.CityKnown(city)) gone.Add(city);
        }
        _host.HideCities(gone, HiddenPlaces());
    }

    /// <summary>
    /// 아직 못 찾은 발견물의 자리와 그 바탕 타일 — 지도에서 지울 것들이다.
    /// </summary>
    /// <remarks>
    /// 게임도 도시와 똑같은 손으로 가린다(<c>0x00425720</c> → <c>0x004AADD0</c> →
    /// <c>0x004AAE90</c>). 사각형이 없는 발견물(유적 속 물건 따위)은 지도에 자리가 없어
    /// 건너뛴다.
    ///
    /// <c>0x004AADD0</c> 이 안 덮는 것은 셋이다 — 내가 찾은 것(사람 칸 0), 역사 항해자가
    /// 먼저 찾은 것(칸 1), 그리고 <b>계약 목표</b>다. 계약 힌트가 가리키는 일련번호와
    /// 발견물 <c>+0x08</c> 을 맞대므로, 계약을 맺으면 아직 못 찾은 유적 그림이 지도에 드러난다.
    /// 같은 번호를 쓰는 것(기제의 피라미드·스핑크스)은 함께 드러난다.
    /// </remarks>
    private IEnumerable<(int X, int Y, ushort[] Block)> HiddenPlaces()
    {
        if (_game.Discoveries is not { } log) yield break;

        var player = _game.Player;
        int target = player.Contract is { } contract && _game.Hints?.Find(contract.Hint) is { } hint
                   ? hint.Discovery : -1;

        foreach (var row in log.Table.Discoveries)
        {
            if (!row.HasPlace || row.Erase is not { Length: > 0 } block) continue;
            if (player.HasFound(row.Id)) continue;
            if (log.TakenBy(row, player.Date) >= 0) continue;
            if (target >= 0 && row.Hint == target) continue;

            yield return (row.X1, row.Y1, block);
        }
    }

    /// <summary>
    /// 가까이 지나가면 도시를 알아본다(<c>0x0048D983</c>).
    /// </summary>
    /// <remarks>
    /// 게임은 배를 가운데 두고 <b>반지름 안의 원</b>(<c>dx² + dy² &lt;= r²</c>)을 훑는다.
    /// 반지름은 <b>측량술 + 2</b> 이고(<c>0x0048D82F</c> 이 부하 표에서 기능 7 을 떠 온다),
    /// 망원경을 지녔으면 두 칸 더 본다(<c>0x0048D84A</c>).
    ///
    /// <b>이미 선 도시만 걸린다</b> — <c>0x0048D94A</c> 의 <c>test al, 5</c> 가 비트 0(안다)과
    /// 비트 2(아직 안 세워짐)를 함께 보아 <b>둘 다 없어야</b> 발견으로 친다.
    /// </remarks>
    private void SpotCities()
    {
        if (_asking || _host.SeaBlocked) return;
        if (_game.CityRows is not { } rows) return;
        if (_host.ShipCell is not { } here) return;

        var spotted = new List<int>();
        // 측량술은 <b>제독과 측량사 부하 가운데 큰 쪽</b>이고(0x0048D82F), 망원경을 지녔으면
        // 두 칸 더 본다(0x0048D84A).
        int reach = SurveyLevel() + SpotBase
                    + (_game.Player.Items.Contains(SpyglassItem) ? SpotWithGlass : 0);
        int far = reach * reach;
        int sx = (int)here.X, sy = (int)here.Y;

        for (int city = 0; city < CityExeTable.Count; city++)
        {
            if (_game.CityKnown(city)) continue;
            if (!_game.CityStanding(city)) continue;         // 아직 안 선 도시는 못 본다
            if (!rows.TryCell(city, out int cx, out int cy, out _)) continue;

            int dx = cx - sx, dy = cy - sy;
            if (dx * dx + dy * dy > far) continue;

            if (_game.Player.Know(city)) spotted.Add(city);
        }
        if (spotted.Count == 0) return;

        // 지도는 한 번만 다시 짓는다 — 한 틱에 둘을 봐도 한 장이면 된다.
        HideCities();

        // 도시 이름은 <b>안 나온다</b> — 부관이 있으면 부관이 말하고(0x0048D9E6 의 0x00478280),
        // 없으면 얼굴 없는 알림이다(0x0048DA0A 의 0x0049E3E0). 알아본 도시가 여럿이어도
        // 원본은 한 번만 낸다.
        _asking = true;
        _host.Paused = true;
        try
        {
            if (_game.Player.MateAt(0).Length > 0)
                ConfirmDialog.Tell(this, "제독, 도시가 보입니다!", face: MateFace());
            else
                NoticeDialog.Show(this, "도시를 발견했습니다!");
        }
        finally
        {
            _host.Paused = false;
            _asking = false;
        }
    }

    /// <summary>발견 반지름의 밑값 — 게임도 측량술에 둘을 더한다(<c>0x0048D834</c>).</summary>
    private const int SpotBase = 2;

    // ── 바다에서 사람을 만난다 ─────────────────────────────────────────────────

    /// <summary>
    /// 지금 바다에 떠 있는 사람들 — 지도가 이 목록을 받아 그린다.
    /// </summary>
    /// <remarks>
    /// 게임도 지도를 그릴 때마다 인물 배열을 통째로 훑는다(<c>0x00426790</c>).
    ///
    /// 다만 이쪽은 <b>화면 새로 고침마다</b> 불린다(<c>CompositionTarget.Rendering</c>).
    /// 사람 자리는 <b>눈금이 하나 넘을 때</b>만 바뀌므로 날짜·눈금·세상 판이 그대로면 지난
    /// 목록을 그대로 낸다 — 안 그러면 초당 예순 번 이백여든 줄을 훑고 목록을 새로 짓는다.
    ///
    /// 하루 안의 눈금(<see cref="_ticks"/>)을 함께 넘긴다. 예전에는 날짜만 넘겨 배가 하루에
    /// 스물네 칸씩 <b>순간이동</b>하듯 뛰었다 — 게임은 눈금마다 조금씩 옮긴다.
    /// </remarks>
    private IReadOnlyList<(double X, double Y, int Heading, int Person)> FolkAfloat()
    {
        if (_game.World is not { } world) return [];

        world.Advance(_game.Player.Date);

        var now = (_game.Player.Date, world.Revision, _ticks);
        if (_folkStamp == now) return _folkList;
        _folkStamp = now;

        double dayPart = (double)_ticks / TerrainTable.TicksPerDay;
        _folkList.Clear();
        foreach (var (who, x, y, heading) in world.Afloat(dayPart))
            _folkList.Add((x, y, heading, who.Id));
        return _folkList;
    }

    private readonly List<(double X, double Y, int Heading, int Person)> _folkList = [];
    private (DateTime Day, int Revision, int Tick) _folkStamp = (default, -1, -1);

    /// <summary>
    /// 화면에 뜬 사람의 배에 <b>두 칸 안</b>으로 붙으면 「배가 보입니다」 — 우호 · 습격 · 떠난다.
    /// </summary>
    /// <remarks>
    /// 게임의 <c>0x0048C049</c> ~ <c>0x0048CA90</c> 이다(볼트 <c>59.분석-해적 조우</c> 5·6절).
    /// <code>
    ///   거리² ≤ 0x400  (1/16 칸이라 두 칸)   칸마다 걸쇠 +4 — 들어올 때 한 번, 벗어나면 풀린다
    ///   하나면 묻고, 여럿이면 고르게 한다(0x0056FBA8)
    ///   상대 갈래  0 같은 나라 · 1 포르투갈·에스파니아 · 2 이교도(나라 갈래 3)
    ///              3 해적(직업 4) · 4 그 밖                              (0x0048C265)
    /// </code>
    /// 새로 붙은 배가 하나라도 있으면 그 틱에는 바다 주사위(<see cref="CheckEncounter"/>)를
    /// 굴리지 않는다 — 게임도 <c>0x0048C126</c> 에서 목록이 비었을 때만 주사위로 간다.
    ///
    /// 그 뒤의 흐름·해전·값 치르기는 볼트 <c>전투/93.분석-보이는 함대 습격</c> 그대로다
    /// (<see cref="FleetRaid"/>). 해전에서 내 기함이 가라앉으면 놀이가 끝난다.
    /// </remarks>
    /// <returns>이번에 새로 두 칸 안에 든 배가 있었는지.</returns>
    private bool MeetFolk()
    {
        if (_asking || _host.SeaBlocked || _host.IsOnLand) return false;
        if (_game.World is not { } world) return false;

        // 걸쇠 — 두 칸 밖으로 벗어난 사람은 풀어 주고, 새로 든 사람만 고른다.
        var inside = _host.FolkWithin(MeetCells);
        _near.IntersectWith(inside);

        var fresh = new List<PersonTable.Row>();
        foreach (int id in inside)
        {
            if (!_near.Add(id)) continue;
            if (world.People.FirstOrDefault(r => r.Id == id) is { } row) fresh.Add(row);
        }
        if (fresh.Count == 0) return false;

        _asking = true;
        _host.Paused = true;
        bool over = false;
        try
        {
            if (PickFolk(fresh) is { } who) over = Approach(world, who);
        }
        finally
        {
            _host.Paused = false;
            _asking = false;
        }

        // 창을 되돌리는 것은 try 밖에서 한다 — 안에서 하면 닫히는 창에 잠금을 풀게 된다.
        if (over) ReturnToTitle();
        return true;
    }

    /// <summary>다가갈 배를 정한다. 안 다가가면 null.</summary>
    private PersonTable.Row? PickFolk(List<PersonTable.Row> rows)
    {
        const string Seen = "제독! 배가 보입니다. 가까이 가 보겠습니까?";     // 0x0056FB28

        if (rows.Count == 1)
            return ConfirmDialog.Ask(this, Seen, face: _game.AideFace) ? rows[0] : null;

        // 여럿이면 물음은 건네기만 하고 목록에서 고르게 한다(0x0056FB58 · 0x0056FBA8).
        ConfirmDialog.Tell(this, Seen, face: _game.AideFace);
        int pick = ChoiceDialog.Ask(this, "접근할 함대를 선택해 주십시오",
                                    [.. rows.Select(r => $"{r.Name}에 접근한다")]);
        return pick >= 0 && pick < rows.Count ? rows[pick] : null;
    }

    /// <summary>붙은 배의 갈래(<c>0x0048C265</c>).</summary>
    private enum FolkSide { Own, Crown, Heathen, Pirate, Other }

    private FolkSide SideOf(int nation, int job)
    {
        if (nation == _game.Player.Nation) return FolkSide.Own;
        if (nation is 0 or 1) return FolkSide.Crown;
        if (_game.Nations?.Find(nation) is { Sect: 3 }) return FolkSide.Heathen;
        if (job == PersonTemplate.PirateJob) return FolkSide.Pirate;
        return FolkSide.Other;
    }

    /// <summary>다가간 뒤 — 우호적으로 접근한다 · 습격한다 · 떠난다(<c>0x0056FBE0</c> 벌).</summary>
    /// <remarks>
    /// 세 줄 모두 켜져 있다(<c>0x00487820</c> 의 켜짐 깃발 1). 「떠난다」는 아무 값도 안 바꾼다 —
    /// 걸쇠만 남아 두 칸 밖으로 벗어났다 다시 들어오면 또 묻는다.
    /// </remarks>
    /// <returns>해전에서 내 기함이 가라앉아 놀이가 끝났는지.</returns>
    private bool Approach(PersonWorld world, PersonTable.Row who)
    {
        var template = _game.PersonTemplates?.Find(who.Id);
        int nation = template?.Nation ?? -1;
        var side = SideOf(nation, template?.Job ?? 0);
        string nationName = _game.Nations?.Find(nation)?.Name ?? "";
        var face = _game.Faces?.TryGetBgra(who.Face, female: false);
        var aide = _game.AideFace;
        // 운세 칸(0x00477FE0) — 별자리·혈액형이 밑표(얼굴·혈액형·나라)에서 나온다.
        var fortune = FleetRaid.FortuneOf(template?.Face ?? 0, template?.Blood ?? 0, nation);

        int pick = ChoiceDialog.Ask(this, who.Name, ["우호적으로 접근한다", "습격한다"], cancel: "떠난다");
        if (pick < 0) return false;

        bool fight = pick == 0
            ? Befriend(who, side, nationName, fortune, face, aide)
            : Raid(side, nationName, face, aide, _game.Random);

        return fight && FightFolk(world, who, nation, face);
    }

    /// <summary>우호적으로 접근했을 때(<c>0x0048C3B4</c>). 싸움이 붙으면 true.</summary>
    /// <remarks>여기서 이어진 해전도 플래그 0 이다 — 해적이 덮쳐 와도 이기면 악명이 오른다.</remarks>
    private bool Befriend(PersonTable.Row who, FolkSide side, string nationName, int[] fortune,
                          uint[]? face, uint[]? aide)
    {
        switch (side)
        {
            // 포르투갈·에스파니아 배는 1492년까지는 여느 배다(0x0048C530 의 cmp 해, 0x5D4).
            case FolkSide.Crown when _game.Player.Date.Year > 1492:
                return Treaty(nationName, fortune, face, aide);

            case FolkSide.Heathen:
                return MeetHeathen(face);

            // 해적은 우호로 가도 덮친다(0x0056FEA0 · 0x0056FED8).
            case FolkSide.Pirate:
                ConfirmDialog.Tell(this, "제독, 뭔가 분위기가 이상한데요···해, 해적입니다!", face: aide);
                ConfirmDialog.Tell(this, "하하하! 좋은 봉이 걸려 들었군! 놈들을 남김없이 해치워라!", face: face);
                return true;

            default:
                Trade(who, nationName, fortune, face, aide);
                return false;
        }
    }

    /// <summary>
    /// 같은 나라·그 밖의 배와의 거래(<c>0x0048CCF0</c>) — 「보급물자를 산다」와 「헤어진다」 두 줄뿐이다.
    /// </summary>
    /// <remarks>
    /// <code>
    ///   k = 운세[4]==0 ? 2 : 1 ;  물 (rand(3)+k)*10 · 식량 (rand(3)+k)*15   한 통
    ///   성사 = rand(100) ≤ 매력+1  (하트 0x004A6360)
    ///   실패 && 운세[4] != 2 → 「미안하지만 나누어 줄 만큼…」 끝
    ///   고리 — 처음이고 운세[4]==0 이면 비싼 값 말 + 부관 「바가지」, 아니면 여느 값 말
    ///          「물을 산다」/「식량을 산다」/「교섭을 안한다」
    ///          최대 = min(소지금/값, 100, 빈 적재, 남는 무게/단중량)
    /// </code>
    /// 「정보를 산다」(<c>0x005702A0</c>)와 그 대답 두 줄은 EXE 에 글만 있고 <b>아무도 안 읽는다</b>.
    /// 인사말의 셋째 %s 는 조사 갈래를 못 짚어 받침이 있으면 「이」를 붙인다.
    /// </remarks>
    private void Trade(PersonTable.Row who, string nationName, int[] fortune, uint[]? face, uint[]? aide)
    {
        var player = _game.Player;
        var rng = _game.Random;
        var (waterPrice, foodPrice) = FleetRaid.PricesOf(fortune, rng);

        ConfirmDialog.Tell(this, $"여어, 자네! 나는 {nationName}의 {who.Name}{GameUi.Josa(who.Name, "이", "")}네. " +
                                 "항해는 순조롭나?", face: face);                               // 0x00570270
        if (ChoiceDialog.Ask(this, who.Name, ["보급물자를 산다"], cancel: "헤어진다") != 0) return;

        bool shares = FleetRaid.Shares(player.AbilityOf(Ability.Charm), rng);
        EffectPopup.Play(this, _game, EffectAnim.Heart, shares, MapAreaOnScreen());
        if (!shares && fortune[4] != 2)
        {
            ConfirmDialog.Tell(this, "미안하지만 나누어 줄 만큼 여유가 없네. 미안하네, 주님의 가호가 있기를!",
                               face: face);                                                    // 0x00570330
            return;
        }

        for (bool first = true; ; first = false)
        {
            if (first && fortune[4] == 0)
            {
                ConfirmDialog.Tell(this, $"물은 금화 {waterPrice}닢, 식량은 금화 {foodPrice}닢 지불한다면 " +
                                         "팔 수도 있네.", face: face);                          // 0x005703E8
                ConfirmDialog.Tell(this, "제독. 이놈, 우리가 난처한 입장에 있는걸 알고 바가지 씌울 생각인 것 " +
                                         "같습니다.", face: aide);                              // 0x00570428
            }
            else
            {
                ConfirmDialog.Tell(this, $"물은 금화 {waterPrice}닢으로, 식량은 금화 {foodPrice}닢 지불한다면 " +
                                         "팔 수도 있지.", face: face);                          // 0x005704B8
            }

            int pick = ChoiceDialog.Ask(this, who.Name, ["물을 산다", "식량을 산다"], cancel: "교섭을 안한다");
            if (pick < 0) return;

            var kind = pick == 0 ? SupplyKind.Water : SupplyKind.Food;
            int price = pick == 0 ? waterPrice : foodPrice;
            int room = Math.Max(0, player.Capacity - player.LoadedBarrels);                   // 0x00474490
            int weight = Math.Max(0, player.Tonnage - player.LoadedWeight)
                         / Supply.Of(kind).UnitWeight;                                        // 0x004743D0
            int most = Math.Min(Math.Min(player.Gold / price, FleetRaid.MaxBarrels), Math.Min(room, weight));
            if (most <= 0) continue;

            if (NumberPadDialog.Ask(this, most, 0, most,
                    $"몇 통 사겠습니까?\n(1통=금화 {price}닢/ 최대 {most}통)") is not { } barrels   // 0x005704F8
                || barrels <= 0)
                continue;

            if (player.Pay(barrels * price)) player.AddSupply(kind, barrels);                 // 0x004740C0 · 0x00474160
        }
    }

    /// <summary>
    /// 이교도 함대에 우호로 다가갔을 때(<c>0x0048C3DB</c>). 싸움이 붙으면 true.
    /// </summary>
    /// <remarks>
    /// 아랍어(<c>vtbl+0x20(5)</c>)가 3 이면 교섭 줄이 뜬다 — 교섭하면 그 말로 끝난다. 아랍어를 못
    /// 하거나 「전투를 한다」를 골라도 <b>신앙심 + 1 ≥ 75</b> 일 때만 저쪽이 덤빈다. 아니면 그냥 지나간다.
    /// </remarks>
    private bool MeetHeathen(uint[]? face)
    {
        var player = _game.Player;
        if (player.TongueOf("아랍어") == 3)
        {
            int pick = ChoiceDialog.Ask(this, "교섭", ["교섭하여 전투를 피한다", "전투를 한다"]);
            if (pick == 0)
                ConfirmDialog.Tell(this, "좋지. 자비를 청하는 자를 죽이진 않겠다. " +
                                         "그러나, 우리 선원들을 다치게 하면 용서치 않겠다.", face: face);
            if (pick != 1) return false;
        }

        if (player.AbilityOf(Ability.Faith) + 1 < 75) return false;

        ConfirmDialog.Tell(this, "이교도들, 우리들이 상대해 주겠다!", face: face);
        return true;
    }

    /// <summary>
    /// 1493년부터 포르투갈·에스파니아 배와 만나면 토르데시야스선을 따진다(<c>0x0048C551</c>).
    /// 싸움이 붙으면 true.
    /// </summary>
    /// <remarks>
    /// 선은 x = 15000(1/16 칸, 서경 45°)이고 1493년만 16223(서경 34°)이다. <b>포르투갈은 동쪽,
    /// 에스파니아는 서쪽이 제 바다</b>다.
    /// <list type="bullet">
    ///   <item>제 바다면 이쪽이 경고한다(<c>0x0056FC10</c>). 저쪽 운세 칸(<c>vtbl+0x24</c> = <c>0x00477FE0</c>)
    ///         넷째(<c>[3]</c>)가 2 면 「조약따윈 모른다!」(<c>0x0056FC70</c>) + 부관 「당치도 않는 소리를!」
    ///         (<c>0x0056FCA0</c>)로 해전, 아니면 저쪽이 물러간다(<c>0x0056FCD8</c>).</item>
    ///   <item>남의 바다면 저쪽이 경고하고(<c>0x0056FD18</c>) 따를지 칠지 고른다.</item>
    /// </list>
    /// </remarks>
    private bool Treaty(string theirNation, int[] fortune, uint[]? face, uint[]? aide)
    {
        var player = _game.Player;
        var (_, lon) = _host.ShipLatLon;
        int x = (int)((lon + 180) / 360 * 40000);
        int line = FleetRaid.TreatyLine(player.Date.Year);
        bool west = x <= line;

        if ((player.Nation == 0) != west)
        {
            string mine = _game.Nations?.Find(player.Nation)?.Name ?? player.NationName;
            ConfirmDialog.Tell(this, $"경고한다. 여기는 {mine}의 영해다. " +
                                     "타국의 배는 기항도 항해도 허용되지 않는다. 신속히 떠나도록.", face: aide);
            // 여기서 보는 성미는 <b>제독 제 것</b>이다 — 0x0048C5E5 가 ecx 에 0x005B60A0
            // (제독 함대)을 넣고 vtbl+0x24 를 부른다. 상대 것을 쓰려던 흠으로 보이지만
            // 원본 그대로 옮긴다.
            if (FleetRaid.AdmiralFortuneOf(player)[3] == 2)
            {
                ConfirmDialog.Tell(this, "조약따윈 모른다! 물고기 밥이 되게 해 주마!", face: face);
                ConfirmDialog.Tell(this, "당치도 않는 소리를! 제독, 할 수 없습니다. 싸웁시다.", face: aide);
                return true;
            }
            ConfirmDialog.Tell(this, "알았다. 우리는 조약을 위반할 뜻은 없다. 이 해역에서 떠나겠다.", face: face);
            return false;
        }

        ConfirmDialog.Tell(this, $"경고한다. 여기는 {theirNation}의 영해다. " +
                                 "타국의 배는 기항도 항해도 허용하지 않는다. 신속히 떠나도록.", face: face);
        if (ChoiceDialog.Ask(this, "경고", ["경고를 따라 떠난다", "경고를 무시하고 공격한다"]) == 1)
            return true;

        ConfirmDialog.Tell(this, "알았다. 우리는 조약을 위반할 뜻은 없다. 이 해역을 곧 떠나겠다.", face: aide);
        return false;
    }

    /// <summary>습격한다를 골랐을 때(<c>0x0048C76D</c>). 싸움이 붙으면 true.</summary>
    private bool Raid(FolkSide side, string nationName, uint[]? face, uint[]? aide, Random rng)
    {
        switch (side)
        {
            case FolkSide.Heathen:
                ConfirmDialog.Tell(this, "제독, 전방의 함대는 이교도의 것입니다!", face: aide);
                ConfirmDialog.Tell(this, rng.Next(2) == 0 ? "적의 습격이다! 서둘러 응전하라!"
                                                          : "전원 전투 준비! 반격하라!", face: face);
                return true;

            case FolkSide.Pirate:
                ConfirmDialog.Tell(this, "제독, 전방의 함대는 해적입니다! 해치워 버립시다!", face: aide);
                ConfirmDialog.Tell(this, rng.Next(2) == 0
                    ? "적의 습격이다! 서둘러 응전하라!"
                    : "흐흐, 우리 해적들을 우습게 보지 마라! 모두 물고기 밥이 되게 해 주마!", face: face);
                return true;
        }

        // 같은 나라(0x0056FF18)와 그 밖(0x0056FF98)은 한 번 더 묻는다.
        ConfirmDialog.Tell(this, side == FolkSide.Own
            ? $"제독, 잠깐! 저것은 {nationName}의 함대입니다!"
            : $"제독, 저것은 {nationName}의 함대입니다. 괜찮습니까?", face: aide);
        if (ChoiceDialog.Ask(this, "습격", ["무시하고 공격한다", "공격을 중지한다"]) != 0) return false;

        ConfirmDialog.Tell(this, "···공격하실 겁니까!?", face: aide);
        ConfirmDialog.Tell(this, side == FolkSide.Own ? "도대체 어쩔 셈인냐!?"
                                                      : "갑자기 공격하다니, 비겁한 놈들! 응전하라!", face: face);
        return true;
    }

    /// <summary>
    /// 보이는 함대와 해전(<c>0x0048CC20(인물, 0)</c>) — 교섭·도망·응전 창 없이 곧장 판이다.
    /// </summary>
    /// <remarks>
    /// <code>
    ///   0x004435B0(id, 목록, 플래그 0)   ; 플래그 0 이라 0x004555B0 을 건너뛴다
    ///   0x00441A00                        ; 바람은 함대 자리 바다 바람, 대열은 함대 +0xDC, 선공 덤 없음
    ///   0x004350F0                        ; 끝 — 값 치르기는 <see cref="SettleRaid"/>
    ///   0x00432400                        ; 결과와 상관없이 제 나라 수도로 · -60일
    /// </code>
    /// 적 배는 그 사람을 적장으로 짓는다(<see cref="EnemyFleet"/>). 대본 함대 276~280 의 선체 목록
    /// (<c>0x00589C70</c>)은 아직 없어 그들도 적장 능력으로 짓는다.
    /// </remarks>
    /// <returns>내 기함이 가라앉아 놀이가 끝났는지.</returns>
    private bool FightFolk(PersonWorld world, PersonTable.Row who, int nation, uint[]? foeFace)
    {
        var player = _game.Player;
        var rng = _game.Random;
        var leader = CaptainOf(who.Id) ?? Encounter.CaptainOf(who.Id);
        var foe = Encounter.OfPerson(leader, who.Name);
        int capital = _game.Nations?.Find(nation)?.Capital ?? -1;

        EndWeather();   // 해전이 열리면 비가 그친다(0x00443822)

        var report = SeaCombatDialog.Engage(this, player, foe, rng, MateFace(),
                                            (_host.LastWind.Dir, _host.LastWind.Speed), _game.Sfx, foeFace,
                                            (board, end) => SettleRaid(board, end, nation, capital, rng, raid: true),
                                            SeaDuel(who.Id, who.Name, foeFace), _game.Bgm, game: _game,
                                            // 누적 캐릭터면 제 옛 함대의 선체로 싸운다(0x0048CC3D).
                                            hulls: world.Replay?.FleetOf(who.Id));

        // 판이 어떻게 끝났든 상대는 제 나라 수도로 돌아가 예순 날 쉰다 — 곧바로 다시 못 만난다.
        world.SendHome(who, capital);

        if (report.Outcome != SeaCombatDialog.Outcome.Defeated) return false;

        // 패배 — 0x0044AF40(0x5A4D18, 2) → 그림 0x0C.
        GameOver(GameOverDialog.FleetLost);
        return true;
    }

    /// <summary>
    /// 해전이 끝난 뒤의 값 치르기(<c>0x004350F0</c>) — 판 창 위에 알린다.
    /// </summary>
    /// <remarks>
    /// <code>
    ///   밑값        적장 나라 == 내 나라 ? 악명 100 : 명성 100
    ///   적 기함 격침 플래그 0(보이는 함대)이면 명성 +120 · 악명 +180, 아니면(바다 주사위 조우) 명성 +200 만
    ///                (0x004355F7) · 전리품 (100(규모+1)+rand100) x 꺾음 · 무력 오름 1/20
    ///   적 기함 퇴각 명성·악명 같음 · 전리품 없음 · 무력 오름 1/20        (알림 끝에 느낌표 없음)
    ///   내 기함 퇴각 악명 +200 만
    ///   내 기함 격침 없음(GAME OVER)
    /// </code>
    /// 곧 명성 220/120 · 악명 180/280(남의 나라/같은 나라), 도망 악명 200/300 이다. 항복은 게임에 없는
    /// 앱 차림표라 도망처럼 친다. 나포선 들임(<c>0x00434D30</c>)과 되찾은 배 알림은 나포가 없어 안 낸다.
    /// </remarks>
    /// <param name="raid">보이는 함대를 친 판인지(플래그 0). 바다에서 마주친 판은 거짓이다.</param>
    private void SettleRaid(Window board, SeaCombatDialog.Report end, int nation, int capital, Random rng,
                            bool raid)
    {
        const string Title = "해전";
        var player = _game.Player;
        var (fame, infamy) = FleetRaid.BaseOf(nation == player.Nation);

        switch (end.Outcome)
        {
            case SeaCombatDialog.Outcome.Won:
            case SeaCombatDialog.Outcome.EnemyRetreated:
            {
                bool won = end.Outcome == SeaCombatDialog.Outcome.Won;
                string bang = won ? "!" : "";                      // 0x0056A7E8 · 0x0056ADF8
                if (raid) { fame += FleetRaid.WinFame; infamy += FleetRaid.WinInfamy; }
                else fame += FleetRaid.MetFame;

                player.Fame = Math.Min(FleetRaid.MaxRenown, player.Fame + fame);
                ConfirmDialog.Tell(board, $"명성이 {fame} 올라갔다{bang}", Title);
                player.Infamy = Math.Min(FleetRaid.MaxRenown, player.Infamy + infamy);
                ConfirmDialog.Tell(board, $"악명이 {infamy} 올라갔다{bang}", Title);

                if (won && end.EnemyDowned + end.EnemyCaptured > 0)
                {
                    // 규모는 EXE 의 처음 규모로 갈음한다(도시가 자라는 셈은 아직 없다).
                    int scale = _game.CityRows?.ScaleOf(capital) ?? 0;
                    int gold = FleetRaid.Loot(scale, end.EnemyDowned + end.EnemyCaptured, rng);
                    ConfirmDialog.Tell(board, $"전리품으로서 금화 {gold} 닢을 손에 넣었다!", Title);   // 0x0056A828
                    player.Earn(gold);
                }

                RaiseMight(board, rng);
                break;
            }

            case SeaCombatDialog.Outcome.Escaped:
            case SeaCombatDialog.Outcome.Surrendered:
                infamy += FleetRaid.FleeInfamy;
                player.Infamy = Math.Min(FleetRaid.MaxRenown, player.Infamy + infamy);
                ConfirmDialog.Tell(board, $"악명이 {infamy} 올라갔다", Title);                      // 0x0056AD28
                break;
        }
    }

    /// <summary>해전 뒤 무력 오름(<c>0x00455CA0(0)</c>) — 스물에 하나, 제독·부관이 1~2 오른다.</summary>
    private void RaiseMight(Window board, Random rng)
    {
        var player = _game.Player;
        string mateName = player.MateAt(0);
        var mate = mateName.Length > 0 ? _game.MateInfo(mateName) : null;
        var (admiral, raiseMate, amount) = FleetRaid.MightUp(player.AbilityOf(Ability.Might), mate?.Might, rng);
        if (amount == 0) return;

        if (admiral)
            player.Abilities[Ability.Might] = Math.Min(FleetRaid.MaxMight, player.Abilities[Ability.Might] + amount);
        if (raiseMate && mate is { } m)
            player.RememberMate(m with { Might = Math.Min(FleetRaid.MaxMight, m.Might + amount) });

        string text = (admiral, raiseMate) switch
        {
            (true, true) => $"{player.Name}, 부관의 무력이 {amount} 상승했다!",   // 0x005602A8
            (true, false) => $"{player.Name}의 무력이 {amount} 상승했다!",        // 0x00560258
            _ => $"부관의 무력이 {amount} 상승했다!",                              // 0x00560280
        };
        ConfirmDialog.Tell(board, text, "성장");
    }

    /// <summary>두 칸. 게임은 1/16 칸 거리² 가 <c>0x400</c> 이하일 때 붙인다(<c>0x0048C0E5</c>).</summary>
    private const double MeetCells = 2;

    /// <summary>두 칸 안에 들어와 이미 한 번 물은 사람들 — 게임의 걸쇠(<c>칸+4</c>)다.</summary>
    private readonly HashSet<int> _near = [];

    /// <summary>이번 틱에 새 배가 붙었는지. 그러면 바다 주사위를 건너뛴다.</summary>
    private bool _folkEntered;

    /// <summary>지난번에 세어 둔, 선 도시들.</summary>
    private HashSet<int>? _founded;

    /// <summary>지도를 지을 때 본 「발견 대본이 세우고 없앤 도시」 수.</summary>
    private int _scriptedSeen;

    /// <summary>
    /// 한 번에 몰아 셀 걸음의 윗값. 창이 오래 멎었다 살아나도 날이 왕창 넘어가지 않게 한다.
    /// </summary>
    private const int MaxCatchUp = TerrainTable.TicksPerDay * 2;

    /// <summary>지난번에 날을 셀 때까지 걸은 걸음 수.</summary>
    private long _steps;

    /// <summary>
    /// 뭍에서 하루가 간다 — 여행비가 나가고 규율이 깎인다.
    /// </summary>
    /// <remarks>
    /// 게임의 <c>0x00475470</c> 이다.
    /// <code>
    ///   4754BE  eax = 5 - 운용술
    ///   4754CA  eax *= 대원수                    ; [0x005AA2C4]
    ///   4754D2  eax = -(eax / 2)
    ///   4754DA  0x0047CBC0(제독, eax)            ; 소지금에 더한다(음수라 빠진다)
    ///   4754DF  소지금 &gt; 0 ? edi -= 4 : edi -= 10   ; edi 는 운용술
    ///   4754F6  새 규율 = 0x00474060(여행물건, edi)
    /// </code>
    /// 곧 <b>하루 여행비 = (5 − 운용술) × 대원수 ÷ 2</b> 이고
    /// <b>하루 규율 = 운용술 − (소지금 &gt; 0 ? 4 : 10)</b> 이다. 운용술 최대가 3이라
    /// 규율은 늘 깎이고, 가장 느릴 때가 −1(운용술 3 · 돈 있음), 가장 빠를 때가 −10 이다.
    /// <b>돈을 내고 난 뒤</b>의 소지금을 보므로 여행비를 못 대면 그날부터 두 배 넘게 빠진다.
    ///
    /// 소지금은 못 대도 물리지 않고 <b>0 까지만</b> 깎인다(<see cref="Player.Spend"/>).
    /// </remarks>
    private void PassLandDay()
    {
        var player = _game.Player;
        int handling = FleetLevel(Skill.Handling);

        player.PassDayAtSea();                       // 날짜는 뭍에서도 간다
        player.Spend((5 - handling) * player.Crew / 2);

        int before = player.Morale;
        player.Cheer(handling - (player.Gold > 0 ? 4 : 10));

        // 빈 글로 덮어쓰면 어제 적힌 한 줄이 하루 만에 지워진다. 할 말이 있을 때만 적는다.
        // 띠에 적는 손(0x0040E0A0)은 적은 뒤 늘 소리 0x1D(WAVE 파트 1)를 낸다.
        if (MoraleLine(before, player.Morale) is { Length: > 0 } line)
        {
            Say(line);
            _game.Sfx?.Play(SoundBank.BandNoticePart);
        }

        // 뭍에서는 짐승과 독충을 마주친다.
        CheckLandEvent();

        // 그리고 오백에 한 번 <b>그 구역의 무리</b>와 마주쳐 들싸움이 붙는다(0x0048BE9B).
        CheckLandParty();

        // 규율이 바닥나면 반란이다 — 뭍에서는 <b>이전 규율 &gt; 0 이고 새 값이 0</b> 일 때만
        // 0x004751E0 을 부른다(0x00475569). 바다는 새 값만 본다(PassSeaMorale).
        // 대표와의 일기토까지는 이미 옮겨 두었다(바다 사건 쪽 Mutiny).
        if (before > 0 && player.Morale == 0) Mutiny();
    }

    /// <summary>
    /// 바다에서 하루가 가면 규율이 깎인다 — <b>항해술 − 6</b>.
    /// </summary>
    /// <remarks>
    /// 게임의 <c>0x00475810</c> 부터다. 뭍과 달리 여행비는 안 나가고 규율만 움직인다.
    /// <code>
    ///   47574a  edi = 그 기능을 제일 잘 아는 사람의 <b>항해술</b>  ; 뭍은 운용술이다
    ///   475810  test [0x005A4D18], 8       ; 항해 루프가 도는 날에만
    ///   475822  eax = 0x00426740()         ; 지금 칸의 부류
    ///   475827  dec eax
    ///   47582b  cmp eax, 1 ; mov eax, 2 ; adc eax, -1
    ///             부류 1 이면 (0 &lt; 1, 부호 없이) → eax = 2
    ///             그 밖(부류 0 은 −1 이 부호 없이 커서) → eax = 1
    ///   475838  eax *= 3                   ; 부류 1 은 6, 그 밖은 3
    ///   47583b  edi -= eax
    ///   47583d  edi -= [esp+0x14]          ; 추위(0~3) — 피로 셈과 같은 값이다
    /// </code>
    /// 바다 칸은 부류 0·1 이 번갈아 깔려 있어 하루 −3 과 −6 이 섞인다.
    /// </remarks>
    private void PassSeaMorale()
    {
        var player = _game.Player;
        int sailing = FleetLevel(Skill.Sailing);
        int drain = SeaMoraleStep * (_host.TerrainClass == 1 ? 2 : 1)
                    + SeaEvents.ColdAt(_host.ShipLatLon.Lat);

        int before = player.Morale;
        player.Cheer(sailing - drain);

        // 바다 쪽 문구는 「선원」이다 — 뭍의 「대원」과 갈린다(0x0047585E).
        if (MoraleLine(before, player.Morale) is { Length: > 0 } line)
        {
            Say(line.Replace("대원", "선원"));
            _game.Sfx?.Play(SoundBank.BandNoticePart);   // 띠 알림 소리(0x0040E0B6)
        }

        // 바다에서는 새 규율이 0 이면 <b>늘</b> 반란이다 — 전날 이미 0 이었어도 그렇다(0x004758AC).
        // 이전 값을 보는 것은 뭍 쪽(0x00475569)뿐이다.
        if (player.Morale == 0) Mutiny();
    }

    /// <summary>바다에서 하루에 빠지는 규율의 밑값(<c>0x00475838</c> 의 3, 부류 1 이면 두 배).</summary>
    private const int SeaMoraleStep = 3;

    /// <summary>
    /// 그 기능의 수준 — <b>제독과 부하 자리 1</b> 가운데 높은 값.
    /// </summary>
    /// <remarks>
    /// 게임의 <c>0x0047CCA0(기능, 1, -1, -1, -1)</c> 이다. 뭍의 하루(<c>0x004754B6</c>, 운용술)와
    /// 바다의 하루(<c>0x0047574A</c>, 항해술) 둘 다 이것을 쓴다. 예전에는 <b>제독 것만</b> 봐서
    /// 운용술 좋은 부하를 태워도 여행비가 줄지 않았다 — 여행비가 <c>(5 − 운용술) × 대원수 ÷ 2</c>
    /// 라 운용술 0 이면 대원 백 명에 하루 250닢이 나가 돈이 금방 바닥나고, 그러면 규율이 −10 으로 뛴다.
    ///
    /// 부하 자료(<see cref="Player.MateInfo"/>)에는 운용술·항해술 칸이 없어 인물 표에서 이름으로 찾는다.
    /// </remarks>
    private int FleetLevel(int skill)
    {
        // 견주는 것은 <b>부하 자리 1</b> 하나뿐이다 — 0x0047CCA0(기능, 1, −1, −1, −1) 이 넘긴 자리만 제독과 대 본다.
        var player = _game.Player;
        int best = player.LevelOf(Skill.Names[skill]);

        string mate = player.MateAt(NavigatorSlot);
        if (mate.Length == 0 || _game.World?.People is not { } people) return best;
        if (people.FirstOrDefault(r => r.Name == mate) is { } row && skill < row.Skills.Length)
            best = Math.Max(best, row.Skills[skill]);
        return best;
    }

    /// <summary>항해·운용을 대 보는 부하 자리(<c>0x004754B4</c> · <c>0x0047574A</c> 의 인자 1).</summary>
    private const int NavigatorSlot = 1;

    /// <summary>
    /// 상륙 차림표의 「수리」(<c>0x0048E140</c>) — 자재로 배를 고친다.
    /// </summary>
    /// <remarks>
    /// 규칙은 <see cref="ShoreRepair"/> 에 있다. 되돌이라 자재가 남는 한 배 고르기부터
    /// 되풀이한다. 추진력은 우리 배 모델에 「지금 추진력」 칸이 없어 안 고친다.
    /// </remarks>
    private void RepairAshore()
    {
        var player = _game.Player;
        var mate = MateFace();

        _asking = true;
        _host.Paused = true;
        try
        {
            if (player.SupplyOf(SupplyKind.Material) <= 0)
            {
                TalkDialog.Say(this, mate, "", "수리하는데 필요한 자재가 없습니다!");
                return;
            }

            // 조선기술은 제독과 부관(자리 0) 가운데 높은 쪽이다(0x0047CCA0).
            int mine = player.LevelOf(ShoreRepair.Skill);
            string aide = player.MateAt(0);
            int his = aide.Length > 0 && _game.World?.People.FirstOrDefault(r => r.Name == aide) is { } row
                      && row.Skills.Length > Skill.Shipwright ? row.Skills[Skill.Shipwright] : 0;
            int skill = Math.Max(mine, his);

            if (skill <= 0)
            {
                TalkDialog.Say(this, mate, "", "조선기술을 가진 사람이 없습니다!");
                return;
            }

            // 부관이 더 잘하면 부관이 나선다(0x0048E408).
            if (his > mine) TalkDialog.Say(this, mate, "", "제가 수리하겠습니다.");

            while (true)
            {
                if (player.SupplyOf(SupplyKind.Material) <= 0)
                {
                    TalkDialog.Say(this, mate, "", "수리하는데 필요한 자재가 없습니다!");
                    return;
                }

                var hurt = player.Ships.Where(ShoreRepair.Damaged).Take(ShoreRepair.MaxListed).ToList();
                if (hurt.Count == 0)
                {
                    TalkDialog.Say(this, mate, "", "어느 배도 다 완전합니다. 수리할 필요는 없습니다.");
                    return;
                }

                TalkDialog.Say(this, mate, "", "어느 배를 수리하겠습니까?");
                int at = ChoiceDialog.Pick(this, "선박 일람",
                    [.. hurt.Select(sh => $"{sh.Name} ({sh.Hp}/{sh.MaxHp})")]);
                if (at < 0 || at >= hurt.Count) return;

                var ship = hurt[at];
                if (!ConfirmDialog.Ask(this, $"{ship.Name}호로 좋습니까?")) continue;

                TalkDialog.Say(this, mate, "", "자재를 몇 통 쓰겠습니까?");
                int have = player.SupplyOf(SupplyKind.Material);
                // 통 수는 계산기 판으로 받는다(0x0048E4C5 → 0x00481FE0, 1~실은 자재).
                if (have <= 0 || NumberPadDialog.Ask(this, 1, 1, have) is not { } barrels || barrels <= 0) continue;

                player.AddSupply(SupplyKind.Material, -barrels);
                int was = ship.Hp;
                int wasSpeed = ship.Speed;
                int gain = ShoreRepair.PerBarrel(skill) * barrels;
                // 자재 수리는 <b>추진력과 내구를 같은 만큼</b> 올린다(0x0048E4FE · 0x0048E537).
                ship.SpeedUp(gain);
                ship.SetHp(ship.Hp + gain);

                NoticeDialog.Show(this, ShoreRepair.RepairWord(ship.Hp - was, ship.Speed - wasSpeed));
            }
        }
        finally
        {
            _host.Paused = false;
            _asking = false;
        }
    }

    /// <summary>
    /// 상륙 차림표의 「보급」(<c>0x0048DC60</c>) — 둘레를 뒤져 물과 먹을 것을 찾는다.
    /// </summary>
    /// <remarks>
    /// 맨 먼저 <b>백에 하나</b> 동굴이 나오고(<see cref="Cave"/>), 그러고 나면 <b>언제나</b>
    /// 물과 식량을 찾는다(<see cref="Gather"/>). 동굴에서 보물을 얻었어도 찾기는 그대로 돈다.
    /// </remarks>
    private void Forage()
    {
        var dice = new GameRandom(Environment.TickCount);
        var player = _game.Player;
        _asking = true;
        _host.Paused = true;
        try
        {
            Cave(dice);
            Gather(dice);
        }
        finally
        {
            _host.Paused = false;
            _asking = false;
        }
    }

    /// <summary>
    /// 보급 앞머리의 동굴(<c>0x0048DC8A</c>) — 백에 하나다.
    /// </summary>
    private void Cave(GameRandom dice)
    {
        var player = _game.Player;
        if (dice.Next(LandEvents.CaveOdds) != 0) return;

        var mate = MateFace();
        TalkDialog.Say(this, mate, "", "제독, 동굴을 발견했습니다!");
        if (!ConfirmDialog.Ask(this, "제독, 동굴속을 탐색하겠습니까?", face: mate)) return;

        if (LandEvents.CaveTreasure(player.AbilityOf(Ability.Faith),
                                    player.AbilityOf(Ability.Luck), dice))
        {
            TalkDialog.Say(this, mate, "", "제독, 원주민의 보물을 발견했습니다!");
            int gold = LandEvents.CaveGold(player.AbilityOf(Ability.Luck), dice);
            player.Earn(gold);
            NoticeDialog.Show(this, $"제독, 금화 {gold}닢에 해당하는 보물을 발견했습니다!");
            return;
        }

        TalkDialog.Say(this, mate, "", "아뿔싸! 짐승의 소굴이다!");

        // 뭍 사건과 같은 완화 식을 탄다(0x0048DDA8 · 0x00426DA0) — 당한 수를 이르고 돌아온 수를 뺀다.
        int hurt = Math.Min(LandEvents.DenLoss(dice), player.Crew);
        if (hurt <= 0) return;
        int back = LandEvents.Returned(MateMedicine(), hurt, dice);
        player.SetCrew(player.Crew - (hurt - back));

        NoticeDialog.Show(this, $"{hurt}명이 당했습니다!");
        if (back > 0) NoticeDialog.Show(this, $"{back}명의 선원이 되돌아왔습니다");
    }

    /// <summary>제독과 부관 가운데 높은 의학(<c>0x0047CCA0(5,0,-1,-1,-1)</c>).</summary>
    private int MateMedicine()
    {
        var player = _game.Player;
        string mate = player.MateAt(0);
        int his = mate.Length > 0 && _game.World?.People.FirstOrDefault(r => r.Name == mate) is { } row
                  && row.Skills.Length > Skill.Medicine ? row.Skills[Skill.Medicine] : 0;
        return Math.Max(player.LevelOf(Skill.Names[Skill.Medicine]), his);
    }

    /// <summary>
    /// 물과 식량을 찾는다(<c>0x0048DEC6</c>) — 동굴이 나오든 말든 보급은 여기까지 간다.
    /// </summary>
    /// <remarks>
    /// 찾은 만큼 다 싣지는 못한다. 남은 짐 칸과 적재 중량이 허락하는 데까지만 담고,
    /// 담을 때는 <b>지금 적은 쪽</b>을 먼저 채워 물과 식량을 맞춘다. 규칙은
    /// <see cref="Foraging"/> 에 있다.
    /// </remarks>
    private void Gather(GameRandom dice)
    {
        var player = _game.Player;
        int ground = _host.TerrainClass;
        int bonus = Foraging.CrewBonus(player.Crew);

        int waterLevel = Foraging.LevelOf(Foraging.WaterLevels, ground);
        int foodLevel = Foraging.LevelOf(Foraging.FoodLevels, ground);

        int water = Foraging.Found(waterLevel, bonus, dice) ? Foraging.Amount(waterLevel, dice) : 0;
        int food = Foraging.Found(foodLevel, bonus, dice) ? Foraging.Amount(foodLevel, dice) : 0;

        var (gotWater, gotFood) = Foraging.Stow(
            water, food,
            player.SupplyOf(SupplyKind.Water), player.SupplyOf(SupplyKind.Food),
            Math.Max(0, player.Capacity - player.LoadedBarrels),        // 0x00474490
            Math.Max(0, player.Tonnage - player.LoadedWeight));         // 0x004743D0

        player.AddSupply(SupplyKind.Food, gotFood);                     // 0x00474160
        player.AddSupply(SupplyKind.Water, gotWater);                   // 0x004740C0

        // 식량 줄과 물 줄을 한 말로 잇는다(0x0048E018 · 0x0048E054).
        string said = (gotFood > 0 ? $"식량을 {gotFood}통 발견했습니다!" : "식량을 발견할 수 없었습니다!")
                      + "\n"
                      + (gotWater > 0 ? $"물을 {gotWater}통 발견했습니다!" : "물을 발견할 수 없었습니다!");

        bool empty = gotFood == 0 && gotWater == 0;
        player.Tire(empty ? Foraging.EmptyFatigue : Foraging.TiredFatigue);
        player.Cheer(empty ? -Foraging.EmptyMorale : -Foraging.TiredMorale);

        var mate = MateFace();
        TalkDialog.Say(this, mate, "", said);
        TalkDialog.Say(this, mate, "",
                       empty ? "선원들이 불평을 하고 있습니다!" : "다들 조금씩 지친 것 같습니다!");
    }

    /// <summary>
    /// 뭍을 걷다가 무리와 마주친다(<c>0x0048BE80</c>) — <c>rand(500) == 0</c> 이고 그 자리가
    /// 구역 여덟 가운데 하나에 들 때다.
    /// </summary>
    /// <remarks>
    /// 무리는 그 구역의 둘 가운데 <c>rand(2)</c>, 병력은 그 무리의 <c>밑 + rand(폭)</c> 이다.
    /// 판은 <b>들싸움</b>이라 버티기만 해도 이긴 것으로 친다(<see cref="LandBattle.Field"/>).
    /// 지면 놀이가 끝난다 — 마을 공략과 같다.
    /// </remarks>
    private void CheckLandParty()
    {
        var dice = _game.Random;
        if (dice.Next(LandFieldFoes.Roll) != 0) return;

        var (lat, lon) = _host.ShipLatLon;
        int zone = LandFieldFoes.ZoneAt(lat, lon);
        if (zone < 0) return;

        int at = LandFieldFoes.PartyAt(zone, new GameRandom(Environment.TickCount));
        var party = LandFieldFoes.All[at];
        int foeMen = LandFieldFoes.MenOf(at, new GameRandom(Environment.TickCount));

        _asking = true;
        _host.Paused = true;
        try
        {
            // 0x0048BFD6 — 제목이 <b>"Encounter!"</b>(0x0056FB00) 다. 원본 글 그대로 둔다.
            ConfirmDialog.Tell(this,
                $"{party.Name}{GameUi.Josa(party.Name, "과", "와")} 부딪쳤습니다!", "Encounter!");

            var player = _game.Player;
            var aide = player.MateAt(0) is { Length: > 0 } name ? player.MateInfoOf(name) : null;

            // 싸우기 전에 교섭·도망·응전을 고른다(0x0044AA30 → 0x0044A830 → 0x004555B0, 갈래 0) —
            // 교섭이 되거나 달아나면 판을 안 연다. 요구액 셈에는 무리 인원이 든다.
            int bandLeader = LandFieldFoes.FirstLeader + at;
            var band = Encounter.OfPerson(CaptainOf(bandLeader) ?? Encounter.CaptainOf(bandLeader), party.Name)
                       with { Kind = EnemyKind.Raider };
            var talkFace = MateFace();
            switch (ChoiceDialog.Ask(this, Encounter.TitleOf(EnemyKind.Raider), Encounter.Choices))
            {
                case 0 when Talked(band, dice, talkFace, weight: foeMen): return;
                case 1:
                    bool fled = Encounter.Escapes(player, band, dice,
                                                  MateRow(0) is { } who && who.Stats.Length > Ability.Luck
                                                      ? who.Stats[Ability.Luck] : 0);
                    EffectPopup.PlayCoin(this, _game, fled, MapAreaOnScreen());
                    if (fled)
                    {
                        ConfirmDialog.Tell(this, Encounter.FledWord(dice), "도망성공", talkFace);
                        return;
                    }
                    ConfirmDialog.Tell(this, Encounter.CaughtWord(dice), "도망실패", talkFace);
                    break;
                case 2:
                    ConfirmDialog.Tell(this, Encounter.FightOnWord(dice), "응전", talkFace);
                    break;
            }

            if (LandDeployDialog.Show(this, _game, "", -1) is not { } line) return;

            var roll = new GameRandom(Environment.TickCount);
            var (foe, foeSkills, culture) = LeaderOf(at, party.Culture);
            // 규모는 적 대장 나라의 수도에서 온다(0x004494B3).
            int bandNation = _game.PersonTemplates?.Find(bandLeader)?.Nation ?? -1;
            int bandScale = _game.CityRows?.ScaleOf(_game.Nations?.Find(bandNation)?.Capital ?? -1) ?? 0;
            var field = new LandBattle(line, 0, foeMen, player, aide, culture,
                                       LandBattle.FieldFor(_host.TerrainClass),
                                       foe, roll, foeSkills, sort: LandBattle.Field,
                                       scale: bandScale, nation: bandNation)
            {
                MyCulture = _game.MyCulture,
                FoeFace = _game.PersonTemplates?.Find(LandFieldFoes.FirstLeader + at) is { } chief
                    ? _game.Faces?.TryGetBgra(chief.Face, female: false) : null,
            };
            if (LandBattleScene.Run(this, _game, field, roll)) return;
            if (!field.Wiped) return;

            GameOver(GameOverDialog.LandLost);   // 육상전 전멸은 까닭 3(0x00449920)
            Dispatcher.BeginInvoke(ReturnToTitle);
        }
        finally { _host.Paused = false; _asking = false; }
    }

    /// <summary>
    /// 들에서 마주친 무리의 <b>대장</b>을 인물 표에서 집는다(<c>0x00447070</c>).
    /// </summary>
    /// <remarks>
    /// 대장은 인물 246 부터 차례대로다(<see cref="LandFieldFoes.FirstLeader"/>). 능력 여섯과
    /// 기능 자리를 그대로 쓰고, 문화권은 <b>그 나라 수도</b>의 것이다 — 표를 못 읽으면
    /// 예전처럼 굴림 값과 구역에 맞춰 박아 둔 문화권으로 물러선다.
    /// </remarks>
    private ((int Might, int Mind, int Luck, int Body) Foe,
             (int Sword, int Gunnery, int Shooting)? Skills, int Culture)
        LeaderOf(int at, int fallbackCulture)
    {
        var roll = new GameRandom(Environment.TickCount);
        var foe = (Might: roll.Next(10) + 74, Mind: roll.Next(10) + 69,
                   Luck: roll.Next(10) + 64, Body: roll.Next(10) + 84);
        (int Sword, int Gunnery, int Shooting)? skills = null;
        int culture = fallbackCulture;

        int person = LandFieldFoes.FirstLeader + at;
        try
        {
            if (PersonTable.Open().Find(person) is { } row && row.Stats.Length >= 5)
            {
                foe = (row.Stats[2], row.Stats[1], row.Stats[4], row.Stats[0]);
                if (row.Skills.Length > Skill.Shooting)
                    skills = (row.Skills[Skill.Sword], row.Skills[Skill.Gunnery],
                              row.Skills[Skill.Shooting]);
            }
        }
        catch (Exception)
        {
            // 인물 표를 못 읽으면 굴림 값 그대로 간다.
        }

        if (_game.PersonTemplates?.Find(person) is { } who
            && _game.Nations?.Find(who.Nation) is { } nation
            && _game.CityRows?.CultureOf(nation.Capital) is { } seat and >= 0)
            culture = seat;

        return (foe, skills, culture);
    }

    private void CheckLandEvent()
    {
        var dice = new GameRandom(Environment.TickCount);
        int ground = _host.TerrainClass;      // 짐승·회오리는 2, 독충은 6 에서만 난다

        var (lat, lon) = _host.ShipLatLon;

        // 차례가 있다(0x00427311~) — 하루에 하나만 터진다.
        if (LandEvents.Heat(dice, ground))
        {
            Refresh(dice, EventAnimation.Oasis,
                    ["제독, 더, 덥다... 더 이상 못참겠다.", "아이구.", "제독! 아니!"],
                    ["물이다-.", "맛있다! 최곱니다!"]);
            return;
        }
        if (LandEvents.Cold(dice, ground, lat, lon))
        {
            Refresh(dice, EventAnimation.Oasis,
                    ["추, 추워..., 몸이 얼 것 같습니다!", "비, 빛입니다! 가 봅시다.", "제독, 저 집에 들어갑시다."],
                    ["으샤-, 기분좋다!"]);
            return;
        }
        if (LandEvents.HotSpring(dice, ground, lat))
        {
            Refresh(dice, EventAnimation.Oasis,
                    ["제독, 웬지 무더워졌군요.", "앗, 온천이다. 갑시다."],
                    ["기분좋군요, 제독.", "이 후에 술이라도 마실까요?"]);
            return;
        }
        if (LandEvents.Rockfall(dice, ground)) { Mishap(dice, EventAnimation.Landslide, "앗! 제독, 위에!"); return; }
        if (LandEvents.Swamp(dice, ground)) { Mishap(dice, EventAnimation.Swamp, "제독, 큰일입니다. 늪입니다!"); return; }
        if (LandEvents.Quicksand(dice, ground)) { Mishap(dice, EventAnimation.Quicksand, "제독, 큰일입니다. 유사입니다!"); return; }

        if (LandEvents.Meet(dice, ground, lat, lon) is { } met) { MeetBeast(dice, met); return; }

        // 마지막이 공통 꼬리다 — 유성, 그리고 삼 년에 두 해는 회오리다(0x00427D05 · 0x00427DA3).
        if (LandEvents.Meteor(dice, _game.Player.Date.Month)) { Meteor(); return; }
        if (LandEvents.Tornado(dice, ground, _game.Player.Date.Year)) Tornado(dice);
    }

    /// <summary>
    /// 쉬어 가는 뭍 사건(더위 · 추위 · 온천) — 그림 한 번에 말 몇 마디, 그리고 <b>피로가 풀린다</b>.
    /// </summary>
    /// <remarks>피로도 <c>-(rand(10)+10)</c> · 규율 <c>+10</c>(<c>0x004273B0</c>).</remarks>
    private void Refresh(GameRandom dice, int scene, string[] before, string[] after)
    {
        _asking = true;
        _host.Paused = true;
        try
        {
            var face = MateFace();
            foreach (string word in before) ConfirmDialog.Tell(this, word, face: face);
            PlayEventScene(scene);
            foreach (string word in after) ConfirmDialog.Tell(this, word, face: face);

            _game.Player.Tire(-LandEvents.RestGain(dice));
            _game.Player.Cheer(LandEvents.RestMorale);
            NoticeDialog.Show(this, "피로가 회복됐다");
        }
        finally { _host.Paused = false; _asking = false; }
    }

    /// <summary>
    /// 다치는 뭍 사건(낙석 · 늪 · 유사) — 그림 한 번에 대원을 잃는다(<c>0x00426DA0</c>).
    /// </summary>
    private void Mishap(GameRandom dice, int scene, string first)
    {
        _asking = true;
        _host.Paused = true;
        try
        {
            var face = MateFace();
            ConfirmDialog.Tell(this, first, face: face);
            PlayEventScene(scene);
            ConfirmDialog.Tell(this, "제독, 다친데는 없습니까?", face: face);
            Casualties(dice, LandEvents.HurtCount(dice));
        }
        finally { _host.Paused = false; _asking = false; }
    }

    /// <summary>
    /// 다친 사람을 셈해 알린다(<c>0x00426DA0</c>) — <b>의학</b>이 높으면 더러 돌아온다.
    /// </summary>
    private void Casualties(GameRandom dice, int hurt)
    {
        var player = _game.Player;
        hurt = Math.Min(hurt, player.Crew);
        if (hurt <= 0) return;

        int back = LandEvents.Returned(MateMedicine(), hurt, dice);
        player.SetCrew(player.Crew - (hurt - back));

        NoticeDialog.Show(this, $"대원 {hurt}명이 사망했습니다.");
        if (back > 0) NoticeDialog.Show(this, $"{back}명의 대원이 돌아왔습니다.");
    }

    /// <summary>
    /// 뭍을 걷다 짐승이나 독충을 마주친다 — 「싸운다 · 도망친다」.
    /// </summary>
    /// <remarks>
    /// 셈은 <see cref="LandEvents"/> 가 다 하고 여기서는 말만 낸다. 문구는 게임
    /// <c>0x005338E0</c> 덩이에서 그대로 옮겼다 — 짐승은 조사가 하나 더 붙는 서식이라
    /// (「큰일이다! %s%s다!」) 이름 뒤에 은/는을 넣는다.
    ///
    /// 말보다 먼저 덤불 장면(<c>0x0048E820(8)</c>)이 함대 자리에서 돈다 — 독충
    /// (<c>0x00427866</c>)과 짐승(<c>0x00427B4C</c>)이 같은 8 이다.
    /// </remarks>
    private void MeetBeast(GameRandom dice, LandEvents.Meeting met)
    {

        _asking = true;
        _host.Paused = true;
        try
        {
            var face = MateFace();     // 말을 거는 것은 부관이다
            PlayEventScene(EventAnimation.Bush);   // 게임도 말보다 먼저 튼다
            string what = met.Venomous
                ? $"큰일이다! {met.Name}다!"
                : $"큰일이다! {met.Name}{GameUi.Josa(met.Name, "이", "가")}다!";

            ConfirmDialog.Tell(this, what, face: face);
            ConfirmDialog.Tell(this, "제독, 어떻게 하시겠습니까?", face: face);

            bool fight = ChoiceDialog.Pick(this, "", ["싸운다", "도망친다"]) == 0;
            var end = fight ? LandEvents.Fight(_game.Player, met, dice)
                            : LandEvents.Flee(_game.Player, met, dice);

            // 끝말도 부관(아니면 뱃사람) 얼굴로 한다(0x00478280). 독충(0x00533958~)과 짐승(0x00533A98~)은
            // 글이 조금씩 다르다 — 짐승 쪽은 「후우, 」·「큰일입니다! 」·끝 마침표다.
            bool venom = met.Venomous;
            if (end.Won)
            {
                ConfirmDialog.Tell(this, fight ? "제독, 퇴치했습니다."
                                   : venom ? "후우..., 간신히 도망쳐 나왔습니다." : "후우, 간신히 도망쳐 나왔습니다.",
                                   face: face);
                return;
            }

            ConfirmDialog.Tell(this, end.Cornered
                ? venom ? "위험하다! 제독, 도망칠 수 없습니다!" : "큰일입니다! 제독, 도망칠 수 없습니다!"
                : venom ? "우와앗, 안되겠다, 제독" : "우와앗, 안되겠다, 제독.", face: face);
            // 다친 사람도 의학으로 더러 돌아온다(0x00426DA0).
            Casualties(dice, end.Dead);
        }
        finally
        {
            _host.Paused = false;
            _asking = false;
        }
    }

    /// <summary>
    /// 유성이 흐른다(<c>0x00427D05</c>) — 부관이 하늘을 가리키고, 그림이 한 번 돈 뒤 두 마디를 더 한다.
    /// 잃는 것도 얻는 것도 없다.
    /// </summary>
    private void Meteor()
    {
        _asking = true;
        _host.Paused = true;
        try
        {
            var face = MateFace();
            var lines = LandEvents.MeteorLines;
            ConfirmDialog.Tell(this, lines[0], face: face);
            PlayEventScene(EventAnimation.Meteor);     // 0x00427D59 — EVANIME 파트 19
            ConfirmDialog.Tell(this, lines[1], face: face);
            ConfirmDialog.Tell(this, lines[2], face: face);
        }
        finally
        {
            _host.Paused = false;
            _asking = false;
        }
    }

    /// <summary>
    /// 회오리에 휩쓸린다 — 말 다섯이 잇달아 나고 대원이 서른 넘게 죽는다.
    /// </summary>
    /// <remarks>
    /// 게임의 <c>0x00427DB8</c> 이다. 짐승과 달리 <b>가릴 것도 고를 것도 없다</b> —
    /// 말 셋이 나고, 그림이 한 번 돌고(<c>0x0048E820(13)</c>), 말 둘이 더 난 뒤에
    /// 죽은 수를 알린다. 그림은 EVANIME 파트 17 — 회오리 둘이 좌우에서 굽이치며 지나간다
    /// (<see cref="EventAnimationPopup"/>).
    /// </remarks>
    private void Tornado(GameRandom dice)
    {
        _asking = true;
        _host.Paused = true;
        try
        {
            var face = MateFace();
            var lines = LandEvents.TornadoLines;

            for (int i = 0; i < 3; i++) ConfirmDialog.Tell(this, lines[i], face: face);
            PlayEventScene(EventAnimation.Tornado);          // 0x00427E4A — 말 셋 뒤, 말 둘 앞

            int dead = LandEvents.Strike(_game.Player, dice);

            for (int i = 3; i < lines.Length; i++) ConfirmDialog.Tell(this, lines[i], face: face);
            NoticeDialog.Show(this, $"대원 {dead}명이 사망했습니다.");
        }
        finally
        {
            _host.Paused = false;
            _asking = false;
        }
    }

    /// <summary>
    /// 규율이 문턱을 넘어 내려갔을 때만 한 줄 낸다. 아니면 빈 글이다.
    /// </summary>
    /// <remarks>
    /// 게임은 <b>넘어갈 때 한 번</b>만 알린다(<c>0x004754FB</c> 아래) — 그 아래에
    /// 머무는 동안은 잠잠하다.
    /// <code>
    ///   50 넘던 것이 30~50 으로  "대원들이 불만을 품기 시작했습니다!"        0x005354B8
    ///   30 넘던 것이 10~30 으로  "대원들의 불만이 심해지고 있습니다!"         0x005354E0
    ///   10 넘던 것이  1~10 으로  "대원들의 불만이 한계에 달했습니다! …"       0x00535508
    ///   0 이 되면                반란(0x004751E0)
    /// </code>
    /// 바다 쪽은 같은 손의 <c>0x00475840</c> 갈래고 문구가 「선원」이다
    /// (<c>0x005356B8</c> · <c>0x005356E0</c> · <c>0x00535708</c>).
    /// 어느 갈래로 가는지는 <c>0x005B61B4</c>(뭍이냐 바다냐)가 가른다.
    /// </remarks>
    private static string MoraleLine(int before, int after) =>
        before > 50 && after is > 30 and <= 50 ? "대원들이 불만을 품기 시작했습니다!"
      : before > 30 && after is > 10 and <= 30 ? "대원들의 불만이 심해지고 있습니다!"
      : before > 10 && after is > 0 and <= 10
            ? "대원들의 불만이 한계에 달했습니다! 일단 아무 마을로나 철수합시다."
      : "";

    /// <summary>
    /// 오늘 바다에서 있었던 일을 알린다 — 보급이 줄어든 것과 지친 것.
    /// </summary>
    /// <remarks>
    /// 문구는 게임 것 그대로다.
    /// <code>
    ///   0x00535550  "제독, %s%s얼마 남지 않았습니다!"   (물이/물도 · 식량이/식량도)
    ///   0x00535590  "제독, 물도 식량도 바닥을 드러내고 있습니다. 빨리 상륙하지 않으면 전멸입니다!"
    ///   0x005355E0  "제독, %s%s 바닥을 드러내고 있습니다, 빨리 상륙합시다!"
    ///   0x00535628  "선원들이 지쳐있습니다"                        (피로 50)
    ///   0x00535640  "선원들이 지쳐있습니다. 이제 상륙합시다!"        (피로 70)
    ///   0x00535668  "선원들의 피로가 한계에 달하고 있습니다. …"      (피로 90)
    /// </code>
    /// 바닥 두 줄만 대원이 말한다 — <c>0x004756FF</c>·<c>0x0047573C</c> 가 <c>0x0047CC60(0, 1)</c> 로
    /// 화자를 집어 말 창 <c>0x00478280</c> 에 넘기므로 <b>부관 아니면 뱃사람 얼굴</b>이 선다.
    /// 나머지(얼마 안 남음·피로)는 <c>0x0040E0A0</c> 알림으로 얼굴이 없다.
    /// </remarks>
    private void Tell(SeaEvents.Day day)
    {
        var lines = new List<(string Text, Where To)>();

        if (day.WaterLow || day.FoodLow)
        {
            // 둘 다 모자라면 "도", 하나뿐이면 "이" 다. 게임도 그렇게 갈라 넣는다.
            bool both = day.WaterLow && day.FoodLow;
            string water = day.WaterLow ? (both ? "물도 " : "물이 ") : "";
            string food = day.FoodLow ? (both ? "식량도 " : "식량이 ") : "";
            lines.Add(($"제독, {water}{food}얼마 남지 않았습니다!", Where.Strip));
        }

        // 오늘 하나라도 바닥났고 <b>지금 둘 다 0</b> 이면 둘 다의 말이다(0x004756E1) — 같은 날 함께 떨어질 필요는 없다.
        bool bothEmpty = _game.Player.SupplyUnitsOf(SupplyKind.Water) <= 0
                         && _game.Player.SupplyUnitsOf(SupplyKind.Food) <= 0;
        if ((day.WaterOut || day.FoodOut) && bothEmpty)
            lines.Add(("제독, 물도 식량도 바닥을 드러내고 있습니다. 빨리 상륙하지 않으면 전멸입니다!",
                       Where.Mate));
        else if (day.WaterOut || day.FoodOut)
        {
            string what = day.WaterOut ? "물" : "식량";
            lines.Add(($"제독, {what}{GameUi.Josa(what, "이", "가")} 바닥을 드러내고 있습니다, " +
                       "빨리 상륙합시다!", Where.Mate));
        }

        if (day.Weary > 0)
            lines.Add((day.Weary switch
            {
                50 => "선원들이 지쳐있습니다",
                70 => "선원들이 지쳐있습니다. 이제 상륙합시다!",
                _ => "선원들의 피로가 한계에 달하고 있습니다. 이대로라면 죽는 사람이 나오고 맙니다!",
            }, Where.Strip));

        // 지쳐 죽어 승원이 모자라졌으면 부관이 한 줄 한다(0x004759A5) — 부관이 없으면 알림 상자다
        // (0x004759D0). 죽은 수는 따로 안 알린다 — 게임도 선원 칸이 줄어드는 것으로만 보인다.
        if (day.Short.Length > 0)
            lines.Add((day.Short, _game.Player.MateAt(0).Length > 0 ? Where.Mate : Where.Notice));

        if (lines.Count == 0) return;

        // 띠에 적는 줄은 창을 안 띄우므로 멈출 것도 없다 — 창이 있을 때만 멈춘다.
        bool boxes = lines.Any(one => one.To != Where.Strip);
        if (boxes) { _asking = true; _host.Paused = true; }
        try
        {
            foreach (var (text, to) in lines)
                switch (to)
                {
                    // 문턱 알림은 <b>하단 띠</b>다 — 게임의 0x0040E0A0 이고, 적은 뒤 늘
                    // 소리 0x1D 를 낸다. 창으로 내면 확인을 눌러야 해 항해가 뚝뚝 끊긴다.
                    case Where.Strip:
                        Say(text);
                        _game.Sfx?.Play(SoundBank.BandNoticePart);
                        break;
                    case Where.Mate:
                        ConfirmDialog.Tell(this, text, face: MateFace());
                        break;
                    default:
                        NoticeDialog.Show(this, text);
                        break;
                }
        }
        finally
        {
            if (boxes) { _host.Paused = false; _asking = false; }
        }
    }

    /// <summary>오늘 있었던 일을 <b>어디에</b> 적는지.</summary>
    /// <remarks>
    /// 게임이 두 손을 나눠 쓴다 — 문턱 알림은 띠(<c>0x0040E0A0</c>)에 적고, 바닥났다는 말은
    /// 화자를 세워 말 창(<c>0x00478280</c>)에 낸다.
    /// </remarks>
    private enum Where
    {
        /// <summary>하단 띠에 한 줄.</summary>
        Strip,

        /// <summary>얼굴 없는 알림 창.</summary>
        Notice,

        /// <summary>부관(아니면 뱃사람) 얼굴을 세운 말 창.</summary>
        Mate,
    }

    /// <summary>한 걸음(<c>0.1초</c>) 몇 번을 하루로 세는지.</summary>
    /// <summary>하루가 차기까지 쌓인 눈금. 마흔여덟이면 날이 넘어간다.</summary>
    private int _ticks;

    /// <summary>지난번에 세어 둔 칸. 같은 칸에 머무는 동안은 날이 안 간다.</summary>

    /// <summary>
    /// 오늘 바다에서 무슨 일이 있었는지 보고, 있으면 겪게 한다.
    /// </summary>
    /// <remarks>
    /// 판정은 <see cref="SeaEvents"/> 가 하고 여기서는 <b>보여 주는 것</b>만 맡는다.
    /// 문구는 게임 것 그대로다 — <c>0x00535178</c> "제, 제독, 큰일입니다! %s%s 오고
    /// 있습니다!!" · <c>0x005351B8</c> "빨리 돛을 접어라!…" · <c>0x005351F0</c> "제독 %s%s
    /// 눈에 띄지 않습니다…" · <c>0x00535260</c> "간신히 빠져 나왔습니다만…".
    ///
    /// 「빨리 돛을 접어라」 뒤에 폭풍 장면(<c>0x0048E820(2)</c>, 눈보라는 3)을 돌린다 — 게임도
    /// 피로·사기를 깎은 다음 그 자리에서 튼다(<c>0x00474D4D</c>).
    /// </remarks>
    /// <summary>
    /// 남의 함대와 붙는다 — 교섭 · 도망 · 응전.
    /// </summary>
    /// <remarks>
    /// 게임에는 길이 둘이다(<c>0x0048BE80</c>, 볼트 <c>59.분석-해적 조우</c>).
    /// <list type="number">
    ///   <item>화면에 보이는 인물 함대와 <b>두 칸 안</b>으로 가까워지면 「배가 보입니다」 —
    ///         우호 · 습격 · 떠난다. <see cref="MeetFolk"/> 가 맡는다.</item>
    ///   <item>그런 함대가 없으면 <b>구역 주사위</b> — 유럽 바다 1/700 해적, 동지중해~아라비아해
    ///         1/400 이슬람 함대. 걸음마다 굴리고 구역 밖에서는 안 붙는다. 여기가 이것이다
    ///         (<see cref="Encounter.AtSea"/>).</item>
    /// </list>
    /// 붙고 난 뒤의 교섭 · 도망 · 응전 차례와 셈은 게임 것 그대로다(<see cref="Encounter"/>).
    ///
    /// </remarks>
    /// <param name="steps">지난번 뒤로 걸은 걸음 수.</param>
    private void CheckEncounter(int steps)
    {
        // 무엇이 떠 있거나 멈춰 있으면 안 붙인다 — 입항 물음과 겹치면 둘 다 어그러진다.
        if (_asking || _host.Paused) return;
        if (_game.Player.Ships.Count == 0) return;          // 배가 없으면 붙을 일이 없다

        var (lat, lon) = _host.ShipLatLon;
        if (Encounter.AtSea(lat, lon, steps, _game.Random, CaptainOf,
                            chased: _game.Player.Pursuers.Any()) is not { } foe) return;
        var rng = _game.Random;

        bool over = false;
        _asking = true;
        _host.Paused = true;
        try
        {
            // 추격대면 적장이 먼저 이름을 댄다(0x00455690) — 뒤쫓는 후원자 하나를 골라 그 명령이라 한다.
            if (foe.Kind == EnemyKind.Chaser && _game.Player.Pursuers.ToList() is { Count: > 0 } chasers)
            {
                var who = chasers[rng.Next(chasers.Count)];
                var boss = _game.Sponsors?.FindByName(who.Sponsor);
                string me = _game.Player.Name, lord = $"{boss?.Name ?? who.Sponsor} {boss?.Honorific ?? "각하"}";
                ConfirmDialog.Tell(this,
                    $"네가 {me}{GameUi.Josa(me, "이", "")}군. 찾고 있었다! {lord}께서 너를 토벌하라는 명령이다. 각오해라.",
                    Encounter.TitleOf(foe.Kind), PersonFace(Encounter.ChaserLeader));
            }

            // 조우의 말은 모두 한 사람이 한다 — 부관, 없으면 뱃사람(0x004555BC 가 처음에 집는다).
            var face = MateFace();
            ConfirmDialog.Tell(this, Encounter.GreetOf(foe, rng), Encounter.TitleOf(foe.Kind), face);

            int pick = ChoiceDialog.Ask(this, Encounter.TitleOf(foe.Kind), Encounter.Choices);
            switch (pick)
            {
                case 0 when Talked(foe, rng, face): return;  // 교섭이 되면 그대로 끝난다
                case 1:
                    // 게임도 굴리고 나서 동전을 돌린다(0x00455B8D → 0x00455B98) — 멎은 쪽이 곧 결과다.
                    bool fled = Encounter.Escapes(_game.Player, foe, rng,
                                                  MateRow(0) is { } who && who.Stats.Length > Ability.Luck
                                                      ? who.Stats[Ability.Luck] : 0);
                    EffectPopup.PlayCoin(this, _game, fled, MapAreaOnScreen());
                    if (fled)
                    {
                        ConfirmDialog.Tell(this, Encounter.FledWord(rng), "도망성공", face);
                        return;
                    }
                    ConfirmDialog.Tell(this, Encounter.CaughtWord(rng), "도망실패", face);
                    break;
                case 2:
                    ConfirmDialog.Tell(this, Encounter.FightOnWord(rng), "응전", face);
                    break;
            }

            // 여기까지 오면 해전이다 — 조우 함수가 0 을 돌려주면 부른 쪽이 판을 연다(볼트 47).
            // 판의 풍향·세기는 함대 자리의 바다 바람에서 온다(0x00441F1C).
            int leaderId = foe.Leader?.Id ?? Encounter.PirateLeader;
            var foeFace = PersonFace(leaderId);
            EndWeather();   // 해전이 열리면 비가 그친다(0x00443822)

            // 바다에서 마주친 판도 값을 치른다(0x004350F0 은 플래그와 상관없이 돈다) — 플래그가 0 이 아니라
            // 명성이 200 붙고 악명 밑값만 간다.
            int foeNation = _game.PersonTemplates?.Find(leaderId)?.Nation ?? -1;
            int foeCapital = _game.Nations?.Find(foeNation)?.Capital ?? -1;
            var outcome = SeaCombatDialog.Engage(this, _game.Player, foe, rng, face,
                                                (_host.LastWind.Dir, _host.LastWind.Speed), _game.Sfx,
                                                foeFace,
                                                (board, end) => SettleRaid(board, end, foeNation, foeCapital, rng,
                                                                           raid: false),
                                                SeaDuel(leaderId, foe.Name, foeFace), _game.Bgm, game: _game).Outcome;

            // 기함을 잃으면(격침·나포·일기토 패배) 놀이가 끝난다 — 보이는 함대 해전(FightFolk)과 같다.
            if (outcome == SeaCombatDialog.Outcome.Defeated)
            {
                GameOver(GameOverDialog.FleetLost);   // 해전 패배는 까닭 2(0x0044386D)
                over = true;
            }
        }
        finally
        {
            _asking = false;
            _host.Paused = false;
        }

        // 창을 되돌리는 것은 try 밖에서 한다 — 안에서 하면 닫히는 창에 잠금을 풀게 된다.
        if (over) ReturnToTitle();
    }

    /// <summary>
    /// 적장 — 인물표(능력·기능 날값)와 인물 밑표(나라·직업)에서 짓는다. 못 읽은 칸은 붙박이 값이다.
    /// </summary>
    /// <summary>
    /// 발견 대본이 거는 해전(<c>0D 0D [인물]</c>) — 바다 괴물 넷이 이것으로 덤빈다.
    /// </summary>
    /// <remarks>
    /// 조우 해전과 같은 길이다 — 적장을 인물 표에서 떠 오고, 판의 바람은 함대 자리의
    /// 바다 바람이다(<c>0x00441F1C</c>). 기함을 잃으면 놀이가 끝난다.
    /// </remarks>
    /// <returns>이겼는지와, 져서 놀이가 끝났는지.</returns>
    internal (bool Won, bool Over) SeaFight(int person)
    {
        var rng = _game.Random;
        var leader = CaptainOf(person) ?? Encounter.CaptainOf(person);
        string name = _game.World?.Table.Find(person)?.Name ?? "괴물";
        var foe = Encounter.OfPerson(leader, name);
        var foeFace = PersonFace(person);

        EndWeather();   // 해전이 열리면 비가 그친다(0x00443822)

        // 괴물 판도 같은 값 치르기를 거친다 — 나라가 없으므로 명성 쪽이다(플래그는 대본 것이라 0 이 아니라고 본다).
        int beastNation = _game.PersonTemplates?.Find(person)?.Nation ?? -1;
        var outcome = SeaCombatDialog.Engage(this, _game.Player, foe, rng, MateFace(),
                                            (_host.LastWind.Dir, _host.LastWind.Speed), _game.Sfx,
                                            foeFace,
                                            (board, end) => SettleRaid(board, end, beastNation,
                                                                       _game.Nations?.Find(beastNation)?.Capital ?? -1,
                                                                       rng, raid: false),
                                            SeaDuel(person, name, foeFace), _game.Bgm,
                                            monster: true).Outcome;

        if (outcome != SeaCombatDialog.Outcome.Defeated)
        {
            // 괴물을 잡으면 그 자리에서 능력치가 오른다(0x0043553C).
            if (outcome == SeaCombatDialog.Outcome.Won) RewardMonster(person);
            return (outcome == SeaCombatDialog.Outcome.Won, false);
        }

        // 괴물에게 지면 여느 패배와 딴 말이다(0x004351F9).
        NoticeDialog.Show(this, "  괴물이 먹어 버렸습니다", "해전");   // 앞 빈칸 둘도 원본 그대로다(0x0056A3F8)

        GameOver(GameOverDialog.FleetLost);
        return (false, true);
    }

    /// <summary>괴물을 퇴치한 삯 — 능력치를 올리고 그 말을 낸다(<c>0x0043553C</c>).</summary>
    private void RewardMonster(int person)
    {
        var (words, gains) = EnemyFleet.MonsterPrize(person);
        if (words.Length == 0) return;

        var stats = _game.Player.Abilities.ToArray();
        foreach (var (ability, by) in gains)
            stats[ability] = Math.Clamp(stats[ability] + by, Ability.Min, Ability.Max);
        _game.Player.SetAbilities(stats);
        NoticeDialog.Show(this, words);
    }

    private Captain? CaptainOf(int id)
    {
        var row = _game.World?.Table.Find(id);
        var template = _game.PersonTemplates?.Find(id);
        return Encounter.CaptainOf(id, row?.Stats, row?.Skills, template?.Nation, template?.Job,
                                   template?.Face, template?.Blood);
    }

    /// <summary>그 자리에 앉은 부하의 인물 표 줄. 비었으면 null.</summary>
    private PersonTable.Row? MateRow(int slot)
    {
        string mate = _game.Player.MateAt(slot);
        return mate.Length == 0 ? null : _game.World?.People.FirstOrDefault(p => p.Name == mate);
    }

    /// <summary>
    /// 적장과 말이 통하는지(<c>0x004558B1</c>) — 그 나라 말을 제독 · 말하는 이(부하 자리 0) · 부하 자리 3
    /// 가운데 하나라도 알면 통한다.
    /// </summary>
    private bool SpeaksWithFoe(in Enemy foe)
    {
        int nation = foe.Leader?.Nation ?? -1;
        int tongue = _game.Nations?.Find(nation)?.Language ?? -1;
        if (tongue < 0 || tongue >= Skill.Languages.Length) return true;   // 나라를 모르면 막지 않는다

        if (_game.Player.TongueOf(Skill.Languages[tongue]) > 0) return true;
        foreach (int slot in (int[])[0, 3])
            if (MateRow(slot) is { } row && tongue < row.Languages.Length && row.Languages[tongue] > 0)
                return true;
        return false;
    }

    /// <summary>교섭 한 판. 돈을 물어 물러가면 true.</summary>
    /// <param name="weight">요구액 셈의 덩치 — 바다는 안 준다(척수 x 30), 뭍 무리는 그 인원이다.</param>
    private bool Talked(in Enemy foe, Random rng, uint[]? face, int? weight = null)
    {
        // 추격대·토벌대는 말이 안 통한다(0x0045585C) — 굴림 없이 진 동전이 돈다(0x00455860).
        // 통하는 적이면 굴리고 나서 동전을 돌린다(0x004559C2 → 0x004559CD).
        // 말은 부관이 대신한다 — 부관 웅변과 내 웅변 가운데 높은 쪽이 먹힌다(0x0045597D).
        var speaker = MateRow(0);
        int mateRhetoric = speaker is { } who && who.Skills.Length > Skill.Rhetoric ? who.Skills[Skill.Rhetoric] : 0;
        int mateLuck = speaker is { } row && row.Stats.Length > Ability.Luck ? row.Stats[Ability.Luck] : 0;

        // <b>말이 통해야 굴린다</b>(0x004558B1) — 적장 나라 말을 제독 · 말하는 이 · 부하 자리 3 가운데
        // 아무도 모르면 굴림 없이 「말이 통하지 않습니다…」다. 추격대는 그 앞에서 막힌다(0x0045585C).
        if (!Encounter.CanTalk(foe.Kind) || !SpeaksWithFoe(foe))
        {
            EffectPopup.PlayCoin(this, _game, false, MapAreaOnScreen());
            ConfirmDialog.Tell(this,
                Encounter.CanTalk(foe.Kind) ? Encounter.NoWordsWord(rng) : Encounter.TalkFailedWord(rng),
                "교섭", face: face);
            return false;
        }

        bool heard = Encounter.Roll(Encounter.TalkOdds(_game.Player, foe.Kind, mateRhetoric, rng, mateLuck), rng);
        EffectPopup.PlayCoin(this, _game, heard, MapAreaOnScreen());
        if (!heard)
        {
            // 굴림에 지면 「교섭이 되지 않는…」 벌이다(0x00455868) — 말이 안 통할 때의 말과 다르다.
            ConfirmDialog.Tell(this, Encounter.TalkFailedWord(rng), "교섭", face: face);
            return false;
        }

        int want = Encounter.Demand(foe, weight);
        // 액수는 부관이 이르고(0x00455A7B), 낼지는 얼굴 없는 상자가 따로 묻는다(0x00455A8F).
        ConfirmDialog.Tell(this, Encounter.DemandWord(want, rng), "교섭", face: face);
        if (!ConfirmDialog.Ask(this, Encounter.PayDemandAsk, "교섭"))
        {
            ConfirmDialog.Tell(this, Encounter.TalkFailedWord(rng), "교섭", face: face);
            return false;
        }

        if (_game.Player.Gold < want)
        {
            // 돈이 모자라면 한 줄을 더 듣는다 — 0x00455AA0 뒤에 0x00455868 로 떨어진다.
            ConfirmDialog.Tell(this, Encounter.TooPoorWord(rng), "교섭", face: face);
            ConfirmDialog.Tell(this, Encounter.TalkFailedWord(rng), "교섭", face: face);
            return false;
        }

        _game.Player.Pay(want);
        ConfirmDialog.Tell(this, Encounter.PaidWord(rng), "교섭", face: face);
        return true;
    }

    /// <summary>
    /// 바다에서 하루가 갈 때 도는 일상 사건(<c>0x00426E80</c> 의 바다 갈래).
    /// </summary>
    /// <remarks>
    /// 세이렌 → 크리스마스 → 생일 → 빙산 차례로 굴려 <b>하루에 하나만</b> 터진다.
    /// 마지막으로 유성이 흐르는데 그 가지는 뭍과 함께 쓴다(<c>0x00427D05</c>).
    /// </remarks>
    private void CheckSeaDailyEvent()
    {
        var player = _game.Player;
        var rng = _game.Random;
        var (lat, _) = _host.ShipLatLon;

        if (SeaEvents.Siren(rng, lat)) { Siren(rng); return; }
        if (SeaEvents.Christmas(player.Date)) { Feast(rng, "메리크리스마스, 제독.", "오늘밤은 마음껏 마시자, 건배!", gift: false); return; }
        if (SeaEvents.Birthday(player)) { Feast(rng, "제독의 생일에 건배다.", "제독, 오늘밤은 마셔도 괜찮겠지요?", gift: true); return; }

        if (SeaEvents.Iceberg(rng, player.Date, lat))
        {
            _asking = true;
            _host.Paused = true;
            try
            {
                ConfirmDialog.Tell(this, "큰일입니다! 빙산이 흘러오고 있습니다!", face: MateFace());
                PlayEventScene(EventAnimation.Iceberg);      // 잃는 것은 없다
            }
            finally { _host.Paused = false; _asking = false; }
            return;
        }

        if (LandEvents.Meteor(new GameRandom(Environment.TickCount), player.Date.Month)) Meteor();
    }

    /// <summary>
    /// 인어의 노래에 홀린다(<c>0x00426ECA</c>) — 자는 사이에 며칠이 흐르고 몸이 상한다.
    /// </summary>
    private void Siren(Random rng)
    {
        var player = _game.Player;
        _asking = true;
        _host.Paused = true;
        try
        {
            var face = MateFace();
            ConfirmDialog.Tell(this, "제독, 뭔가 기분 좋은 노래 소리가 들리는군요.", face: face);
            ConfirmDialog.Tell(this, "갑자기 졸음이...", face: face);

            // 자는 동안은 <b>항해 날</b>이 그대로 흐른다 — 0x00426FB1 이 깃발 4 를 세우고 0x0044AFD0 을 rand(5)+3 번
            // 돌려 보급·피로·규율·컨디션·항해일수가 날마다 움직이되 사건만 안 난다. 도시의 쉬는 날(AdvanceDays)이 아니다.
            int sleep = SeaEvents.SirenDays(rng);
            var (lat, _) = _host.ShipLatLon;
            for (int d = 0; d < sleep; d++)
            {
                player.PassDayAtSea();
                Engine.Town.Vitality.PassDay(player);
                SeaEvents.PassDay(player, lat, _game.Random, FleetLevel(Skill.Sailing));
                player.Cheer(FleetLevel(Skill.Sailing)
                             - (SeaMoraleStep * (_host.TerrainClass == 1 ? 2 : 1) + SeaEvents.ColdAt(lat)));
            }

            ConfirmDialog.Tell(this, "으, 으...머리가 아프다... 자고 있었나...", face: face);
            // 제독이 혼잣말한다 — 0x00478280(제독)이라 뱃사람 얼굴 #299 가 선다.
            ConfirmDialog.Tell(this, "하아하아, 기분이 안좋다...", face: _game.Faces?.TryGetBgra(SailorFace, female: false));
            ConfirmDialog.Tell(this, "선원들이 불안해 하고 있습니다.", face: face);

            // 0 이 되면 1 로, 100 이 되면 99 로 되돌린다 — 반란과 전멸을 여기서는 안 낸다.
            player.Cheer(-SeaEvents.SirenMoraleDrop(rng));
            if (player.Morale <= 0) player.Cheer(1 - player.Morale);
            player.Tire(SeaEvents.SirenFatigue(rng));
            if (player.Fatigue >= Player.MaxFatigue) player.SetFatigue(Player.MaxFatigue - 1);

            NoticeDialog.Show(this, "피로도가 상승했다");
        }
        finally { _host.Paused = false; _asking = false; }
    }

    /// <summary>크리스마스와 생일(<c>0x0042709D</c> · <c>0x00427115</c>) — 마시고 쉰다.</summary>
    private void Feast(Random rng, string first, string second, bool gift)
    {
        var player = _game.Player;
        _asking = true;
        _host.Paused = true;
        try
        {
            var face = MateFace();
            ConfirmDialog.Tell(this, first, face: face);
            ConfirmDialog.Tell(this, second, face: face);

            player.Tire(-SeaEvents.FeastRest);
            player.Cheer(SeaEvents.FeastMorale);
            if (!gift || player.IsBagFull) return;

            if (!SeaEvents.BirthdayGift(player.AbilityOf(Ability.Charm), player.Morale, player.Fatigue)) return;

            ConfirmDialog.Tell(this, "저희들이 드리는 선물입니다. 받아 주십시오.", face: face);
            int item = SeaEvents.BirthdayItem(rng);
            if (!player.Take(item)) return;

            string got = _game.Items?.Find(item)?.Name ?? $"아이템 {item}";
            NoticeDialog.Show(this, $"[{got}]{GameUi.Josa(got, "을", "를")} 받았다");
        }
        finally { _host.Paused = false; _asking = false; }
    }

    /// <summary>이 피로도부터는 입항 물음이 「모두 지쳐 있으니…」로 바뀐다(<c>0x0048DBCA</c>).</summary>
    private const int TiredToRest = 60;

    /// <summary>바로 앞서 잰 |위도| — 게임의 <c>[함대+0x128]</c> 자리다.</summary>
    private double _polarWas;

    /// <summary>
    /// 극지방 경고와 전멸(<c>0x0048D690</c>). 놀이가 이어지면 true.
    /// </summary>
    private bool CheckPolar()
    {
        var (lat, _) = _host.ShipLatLon;
        var step = SeaEvents.PolarAt(lat, _polarWas);
        _polarWas = Math.Abs(lat);
        if (step == SeaEvents.PolarStep.None) return true;

        _asking = true;
        _host.Paused = true;
        try
        {
            if (step == SeaEvents.PolarStep.Warn)
            {
                string way = SeaEvents.PolarWay(lat);
                ConfirmDialog.Tell(this,
                    $"제독, 너무 춥습니다! 더 이상 {way}{GameUi.Josa(way, "으로", "로")} 가는 것은 위험합니다!",
                    face: MateFace());
                return true;
            }

            if (step == SeaEvents.PolarStep.Alarm)
            {
                ConfirmDialog.Tell(this, SeaEvents.PolarAlarmWord(_host.IsOnLand), face: MateFace());
                return true;
            }

            ConfirmDialog.Tell(this, SeaEvents.PolarDoomWord(_host.IsOnLand, _game.Random),
                               face: MateFace());
        }
        finally { _host.Paused = false; _asking = false; }

        GameOver(GameOverDialog.FleetLost);   // 극지방 전멸은 까닭 1(0x0048D7FD)
        Dispatcher.BeginInvoke(ReturnToTitle);
        return false;
    }

    private void CheckSeaEvent()
    {
        var (lat, _) = _host.ShipLatLon;
        if (SeaEvents.Roll(_game.Player, lat, _game.Random, MateSheetAt) is not { } kind) return;

        if (kind == SeaEventKind.Mutiny) { Mutiny(); return; }
        if (Plagued(kind)) return;

        var storm = SeaEvents.Resolve(_game.Player, kind, _game.Random);

        _asking = true;
        _host.Paused = true;
        try
        {
            // 폭풍 말은 모두 부관(없으면 뱃사람 #299) 얼굴이다 — 0x00478280(0x0047CC60(0, 1)).
            string word = storm.Word;
            var face = MateFace();
            ConfirmDialog.Tell(this,
                $"제, 제독, 큰일입니다! {word}{GameUi.Josa(word, "이", "가")} 오고 있습니다!!", face: face);
            ConfirmDialog.Tell(this, "빨리 돛을 접어라! 어떻게 해서든지 버텨라!!", face: face);
            PlayEventScene(kind == SeaEventKind.Storm ? EventAnimation.Storm : EventAnimation.Blizzard);

            if (storm.Lost.Count > 0)
            {
                // 배 이름은 「,  %s호」(0x005351E8, 쉼표 뒤 두 칸)로 잇는다.
                string names = string.Join(",  ", storm.Lost.Select(n => $"{n}호"));
                ConfirmDialog.Tell(this,
                    $"제독 {names}{GameUi.Josa(names, "이", "가")} 눈에 띄지 않습니다. " +
                    $"{word}에서 놓친 것 같습니다.", face: face);
            }
            else if (_game.Player.CrewShares.Any(c => c <= 0))
            {
                // 배를 놓치지는 않았어도 <b>승원이 0 인 배</b>가 생겼으면 「간신히…」 대신
                // 인원 부족 말이다(0x004750C5 — 0x00474FD1 이 승원 0 인 배를 보고 세운 깃발).
                string names = string.Concat(_game.Player.Ships.Select(s => $", {s.Name}호"));
                ConfirmDialog.Tell(this,
                    $"제독{names}{GameUi.Josa(names, "이", "가")} 인원 부족입니다!", face: face);
            }
            else
            {
                ConfirmDialog.Tell(this, kind == SeaEventKind.Storm
                    ? "간신히 빠져 나왔습니다만, 선원들이 지쳐 있습니다. 어디서 휴양하는 것이 좋겠습니다."
                    : "간신히 빠져 나왔습니다만, 선원들이 얼어있습니다. 어딘가 상륙해서 몸을 녹이는 것이 좋을 것 같습니다.",
                    face: face);
            }
        }
        finally
        {
            _host.Paused = false;
            _asking = false;
        }
    }

    /// <summary>
    /// 반란 — 선원 대표가 나서서 승부를 걸어 온다.
    /// </summary>
    /// <remarks>
    /// 문구는 게임 것 그대로다 — <c>0x00535330</c> "제독, 큰일입니다. %s%s 반란을
    /// 일으켰습니다!…" · <c>0x00535390</c> "제독, 이대로 %s%s 계속할 작정이라면…" ·
    /// <c>0x00535400</c> "그러니, 모두가 보는 앞에서 나와 승부하자!…" ·
    /// <c>0x005354A0</c> "반란을 진압했습니다".
    ///
    /// 배를 탔으면 "선원"(<c>0x00535320</c>)과 "항해"(<c>0x005353F0</c>)와
    /// "물고기"(<c>0x00535478</c>), 뭍이면 "대원"·"탐험"·"새" 로 갈린다.
    /// </remarks>
    /// <summary>
    /// 쥐 · 괴혈병 · 전염병과 그 귀띔. 맡았으면 true.
    /// </summary>
    /// <remarks>
    /// 문구는 게임 것 그대로다. 말하는 이는 부관이라 <b>부관 얼굴</b>이 함께 선다 —
    /// 게임도 <c>0x0047CC60(0, 0)</c> 으로 부하 첫 자리를 집어 넘긴다.
    /// <code>
    ///   0x00534D88  쥐        제독 큰일입니다! 쥐가 대량으로 발생했습니다…
    ///   0x00534F68  괴혈병    제독 큰일입니다! 선원들이 픽픽 쓰러지기 시작했습니다…
    ///   0x005350C8  전염병    제독, 큰일입니다! 유행병이 퍼지고 있습니다…
    ///   0x00534DE0  귀띔      제독! 모두 약해져 있습니다. 슬슬 상륙하는 것이 좋겠습니다.
    ///   0x00534FC8  귀띔      제독! 이상한 병이 돌고 있습니다. 상륙하는 것이 좋겠습니다.
    ///   0x00534E20  보리      제독, 선원들이 약해져 있습니다. 제독의 지시대로 보리를 먹이겠습니다.
    ///   0x00534E68  보리      제독, 선원들이 약해져 있으니 보리를 먹이겠습니다.
    ///   0x00534D60  퇴치      제독, 쥐들이 늘었으므로 퇴치하겠습니다.
    /// </code>
    /// 게임은 병이 돌면 선원을 하나씩 골라 이름을 부르며 죽이는데(<c>0x00534F30</c>
    /// "%s%s 괴혈병에 걸려…") 우리는 함대가 선원을 통째로 태우므로 머릿수만 던다.
    /// </remarks>
    /// <summary>쥐가 들끓을 때 깎이는 규율(<c>0x00474804</c>).</summary>
    private const int RatsMoraleLoss = 10;

    private bool Plagued(SeaEventKind kind)
    {
        string word = kind switch
        {
            SeaEventKind.Rats =>
                "제독 큰일입니다! 쥐가 대량으로 발생했습니다. 어디 상륙해서 퇴치하는 것이 좋겠습니다!",
            SeaEventKind.Scurvy =>
                "제독 큰일입니다! 선원들이 픽픽 쓰러지기 시작했습니다. 어디 상륙해서 휴양하는 것이 좋겠습니다!",
            SeaEventKind.Plague =>
                "제독, 큰일입니다! 유행병이 퍼지고 있습니다. 어딘가 상륙하지 않으면 전멸입니다!",
            SeaEventKind.Weakening =>
                "제독! 모두 약해져 있습니다. 슬슬 상륙하는 것이 좋겠습니다.",
            SeaEventKind.StrangeIllness =>
                "제독! 이상한 병이 돌고 있습니다. 상륙하는 것이 좋겠습니다.",
            SeaEventKind.BarleyByMe =>
                "제독, 선원들이 약해져 있습니다. 제독의 지시대로 보리를 먹이겠습니다.",
            SeaEventKind.BarleyByMate =>
                "제독, 선원들이 약해져 있으니 보리를 먹이겠습니다.",
            SeaEventKind.RatsKilled =>
                "제독, 쥐들이 늘었으므로 퇴치하겠습니다.",
            _ => "",
        };
        if (word.Length == 0) return false;

        // 쥐는 실은 식량이 있어야 인다(0x004746F9 — (식량+9)/10 &gt; 0). 없으면 그날은 아무 일도 없다.
        if (kind == SeaEventKind.Rats && _game.Player.SupplyOf(SupplyKind.Food) <= 0) return true;

        // 병이 터지는 자리에서 HP 가 0 이면 거기서 끝난다(0x004748F6 · 0x00474AA2).
        if (kind is SeaEventKind.Scurvy or SeaEventKind.Plague && DiedOfDisease()) return true;

        // 터진 재해는 함대에 남는다 — 항해가 끝날 때까지 날마다 해를 끼친다(0x004747FF 벌).
        // 터지는 그 자리에서 죽는 사람은 없다 — 사람이 죽고 식량이 주는 것은 날마다의 0x00474DA0 뿐이다.
        _game.Player.Afflict(SeaEvents.AilmentOf(kind));

        // 쥐가 들끓으면 규율이 10 준다(0x00474804 → 0x00474060(-10)).
        if (kind == SeaEventKind.Rats) _game.Player.Cheer(-RatsMoraleLoss);

        // 터진 재해만 사건 스틸을 먼저 세운다(0x004747C4 쥐 #2 · 0x004748EA/0x00474A96 병 #1).
        // 귀띔(약해짐·이상한 병)에는 그림이 없다.
        int picture = kind switch
        {
            SeaEventKind.Rats => EventStillPopup.Rats,
            SeaEventKind.Scurvy or SeaEventKind.Plague => EventStillPopup.Sickness,
            _ => -1,
        };

        _asking = true;
        _host.Paused = true;
        var still = picture >= 0 ? EventStillPopup.Open(this, _game, picture, MapAreaOnScreen()) : null;
        try
        {
            // 반란과 같은 꼴이다 — 대사 창은 스틸 아래에 겹치지 않게 선다.
            ConfirmDialog.Tell(this, word, face: MateFace(), under: still);
        }
        finally
        {
            still?.Close();                          // 대사 창이 닫히면 스틸도 걷는다(0x00473160)
            _host.Paused = false;
            _asking = false;
        }
        return true;
    }

    /// <summary>
    /// 대원이 말하는 얼굴 — <b>부관이 있으면 부관, 없으면 뱃사람(MALE #299)</b>이다.
    /// </summary>
    /// <remarks>
    /// 게임의 말 창 <c>0x00478280</c> 은 얼굴 <c>0x12B</c>(299)에서 시작해, 넘겨받은 사람이 제독 객체
    /// (<c>0x005B60A0</c>)가 아니면 그 사람 얼굴로 바꾼다. 부르는 쪽은 <c>0x0047CC60(0, 1)</c> 로
    /// 부관을 집고 없으면 제독 객체를 넘기므로, 부관이 없을 때 뱃사람 얼굴이 선다(볼트 81).
    /// </remarks>
    /// <summary>그 후원자의 얼굴. 표나 그림을 못 읽으면 null 이고, 그러면 대사만 나온다.</summary>
    private uint[]? SponsorFaceOf(string? name)
    {
        if (name == null || _game.Sponsors?.FindByName(name) is not { } sponsor) return null;
        return _game.Faces?.TryGetBgra(sponsor.Face, sponsor.IsFemale);
    }

    /// <summary>
    /// 그 자리의 부하 신상. 빈 자리면 null 이다.
    /// </summary>
    /// <remarks>
    /// 바다 사건이 기능마다 <b>제독과 어느 한 자리</b>를 견주는 데 쓴다(<c>0x0047CCA0</c>).
    /// </remarks>
    private Player.MateInfo? MateSheetAt(int slot)
    {
        string name = _game.Player.MateAt(slot);
        return name.Length > 0 ? _game.MateInfo(name) : null;
    }

    /// <summary>
    /// 선원이 한 명도 없는 배가 있으면 날마다 한마디 한다 — 「제독, …호, …호가 인원 부족입니다!」.
    /// </summary>
    /// <remarks>
    /// 게임의 바다 하루 뒷정리(<c>0x00474DA0</c>) 끝이다. 배 여덟 칸 가운데 승원(<c>0x0044C7C0</c>)이 0 인
    /// 배가 있으면(<c>0x00474FB4</c>) <b>함대 배 이름을 모두</b> 「, %s호」(<c>0x00535238</c>)로 잇고
    /// 「제독%s%s 인원 부족입니다!」(<c>0x00535240</c>, 조사 이/가)를 부관 아니면 뱃사람이 말한다
    /// (<c>0x00478280</c>). 편성으로 선원을 나눠 태울 때까지 날마다 되풀이된다.
    /// </remarks>
    private void TellCrewShort()
    {
        var player = _game.Player;
        var shares = player.CrewShares;
        if (player.Ships.Count == 0 || !shares.Any(c => c <= 0)) return;

        string names = string.Concat(player.Ships.Select(s => $", {s.Name}호"));
        _asking = true;
        _host.Paused = true;
        try
        {
            ConfirmDialog.Tell(this, $"제독{names}{GameUi.Josa(names, "이", "가")} 인원 부족입니다!", face: MateFace());
        }
        finally
        {
            _host.Paused = false;
            _asking = false;
        }
    }

    private uint[]? MateFace()
    {
        string mate = _game.Player.MateAt(0);
        if (mate.Length > 0 && _game.MateInfo(mate) is { Face: >= 0 and < 0xFFFF } who
            && _game.Faces?.TryGetBgra(who.Face, female: false) is { } face)
            return face;
        return _game.Faces?.TryGetBgra(SailorFace, female: false);
    }

    /// <summary>부관이 없을 때 말하는 뱃사람 얼굴 번호(<c>0x00478280</c> 의 <c>0x12B</c>).</summary>
    private const int SailorFace = 299;

    private void Mutiny()
    {
        bool land = _host.IsOnLand;
        string who = land ? "대원" : "선원";
        string what = land ? "탐험" : "항해";
        string beast = land ? "새" : "물고기";

        _asking = true;
        _host.Paused = true;
        try
        {
            // 첫 마디는 <b>부관</b>이(0x0047534A — 0x0047CC60 으로 부하 첫 자리), 둘째·셋째는
            // <b>반란 대표</b>(#212)가 얼굴을 걸고 한다(0x00475383 · 0x004753AA 가 대표 객체를 넘긴다).
            // 게임은 이 동안 사건 스틸 #0 을 뒤에 깔아 둔다(0x00475317 → 0x00472FA0(0)).
            var leaderFace = _game.Faces?.TryGetBgra(MutinyFace, female: false);
            var still = EventStillPopup.Open(this, _game, EventStillPopup.Mutiny, MapAreaOnScreen());
            try
            {
                // 대사 창은 스틸 <b>아래</b>에 겹치지 않게 선다 — 게임 화면이 그렇다.
                ConfirmDialog.Tell(this,
                    $"제독, 큰일입니다. {who}{GameUi.Josa(who, "이", "가")} 반란을 일으켰습니다!  " +
                    $"{who}의 대표가 제독께 할 이야기가 있다고 합니다!", face: MateFace(), under: still);
                ConfirmDialog.Tell(this,
                    $"제독, 이대로 {what}{GameUi.Josa(what, "을", "를")} 계속할 작정이라면 우리들은 " +
                    "전멸이다. 우리들은 당신과 함께 죽을 마음이 없다.", face: leaderFace, under: still);
                ConfirmDialog.Tell(this,
                    "그러니, 모두가 보는 앞에서 나와 승부하자! 당신이 이기면 얌전히 따르겠다. " +
                    $"그러나, 내가 이기면 {beast}의 먹이가 될 줄 알아라.", face: leaderFace, under: still);
            }
            finally
            {
                still?.Close();                      // 일기토 판이 뜨기 전에 걷는다
            }

            // 굴림 하나로 갈음하던 것을 <b>진짜 일기토 판</b>으로 바꿨다. 술집이 쓰는
            // 그 판(DuelDialog)이고, 상대만 그 자리에서 지어 세운다.
            var dice = new GameRandom(Environment.TickCount);
            // 반란은 판 종류 7 이라 <b>부관을 대신 내보낼지 묻는다</b>(0x004A8680 의
            // 종류 <= 2 또는 >= 7).
            var stand = SeaSendMate(this, dice);
            var duel = new Engine.Town.Duel(
                stand is { } fighter
                    ? new Engine.Town.Duel.Fighter(fighter.Name, fighter.Body, fighter.Might,
                                                   fighter.Sword, fighter.Luck,
                                                   BestItem(Engine.Town.Duel.WeaponCategory),
                                                   BestItem(Engine.Town.Duel.ArmorCategory))
                    : MyFighter(),
                MutinyLeader(dice),
                _game.Player.Items.Contains(Engine.Town.Duel.EdithShieldId),
                Environment.TickCount);
            // 배경은 뭍이면 초원, 바다면 배 갑판이다.
            // 대표 얼굴은 #212 다 — 게임이 [대표+8] 에 0xD4 를 박는다(0x00475279).
            DuelDialog.Show(this, duel, dice,
                            _game.Faces?.TryGetBgra(MutinyFace, female: false),
                            _game.Fighters, foeSet: 1,
                            myFace: _game.Faces?.TryGetBgra(
                                PortraitAges.At(_game.Player.Face, _game.Player.Age,
                                                false, _game.Faces),
                                female: false),
                            arena: land ? DuelArt.Field : DuelArt.Deck,
                            // 반란도 일기토 판이라 트랙 11 이 돈다(0x004AA8A0).
                            bgm: _game.Bgm);

            // 대신 나간 사람이 다친다(0x004AA5CA).
            if (stand is { } hurtStand) _game.Player.HurtMate(hurtStand.Name, duel.BodyLost);
            else _game.Player.Hurt(duel.BodyLost);

            if (duel.Won == true)
            {
                _game.Player.Cheer(SeaEvents.MutinyCheer);
                // 반란 판(종류 7)은 처형·놓아 준다·모두 뺏는다가 없다(0x004AA2C9). 게임은 둘 중
                // <b>하나만</b> 낸다(0x004753F8): 부하 첫 자리가 있으면 그 사람이 「이것으로 불만
                // 없겠지!」, 없으면 알림 「반란을 진압했습니다」. 예전에는 둘을 잇달아 냈다.
                if (_game.Player.MateAt(0).Length > 0)
                    ConfirmDialog.Tell(this, "이것으로 불만 없겠지!", face: MateFace());
                else
                    NoticeDialog.Show(this, "반란을 진압했습니다");
                // 규율이 도로 올랐으니 띠에 남아 있던 불만 글도 걷는다.
                Say("");
                return;
            }

            // 지면 대표가 마지막 한마디를 한다 — 반란 판(종류 7)만의 두 마디다
            // (0x004AA0BF 의 rand(2), 0x005344E8 · 0x00534500).
            ConfirmDialog.Tell(this, dice.Next(2) == 0
                ? "자 제독, 죽어라!"
                : $"자네, 제독감이 아니로군. {beast}의 먹이가 더 어울리는군.",
                face: _game.Faces?.TryGetBgra(MutinyFace, female: false));

            // 그리고 <b>놀이가 끝난다</b> — 게임도 여기서 끝낸다(0x0044AF40 상태 4).
            NoticeDialog.Show(this,
                $"제독은 {beast}의 먹이가 되었다. 항해는 여기서 끝났다.");

            // 그러고 나서 사건 스틸 한 장과 CONTINUE? 물음이다(0x00410CC2).
            GameOver();
        }
        finally
        {
            _host.Paused = false;
            _asking = false;
        }

        // 창을 되돌리는 것은 try 밖에서 한다 — 안에서 하면 닫히는 창에 잠금을 풀게 된다.
        ReturnToTitle();
    }

    /// <summary>반란 대표의 얼굴 번호 — MALE.CDS #212(<c>0x00475279</c> 의 <c>[대표+8] = 0xD4</c>).</summary>
    /// <remarks>
    /// 인물 <c>+0x08</c> 이 얼굴 칸이다. 예전에 얼굴로 본 <c>0x004752E3</c> 의 <c>0x31</c> 은
    /// <c>+0x34</c> — 능력 여섯의 끝인 <b>신앙심</b>이었다. 게임 화면의 대표(희끗한 머리에
    /// 수염 난 사람)와 #212 가 같다.
    /// </remarks>
    private const int MutinyFace = 0xD4;


    /// <summary>일기토에 선 내 몫. 술집 것과 같다.</summary>
    /// <summary>
    /// 미니 게임의 일기토 — <b>인물표에서 상대를 골라</b> 붙는다.
    /// </summary>
    /// <remarks>
    /// 예전에는 <c>CdsHelper.Duel</c> 의 옛 판을 걸어 두고 손으로 지은 넷 가운데 골랐다.
    /// 그쪽은 몸짓 그림을 아직 안 옮긴 판이라 <b>화면이 딴판</b>이었다 — 배경도 384x136 로
    /// 좁고 사람이 안 움직인다. 여기서는 반란·해전이 쓰는 그 판(<see cref="DuelDialog"/>)을
    /// 그대로 쓴다.
    ///
    /// 상대의 무기·갑옷은 <b>0</b> 이다. 인물표에 지닌 것이 안 적혀 있어 지어낼 수가 없다 —
    /// 대원 대표(<see cref="MutinyLeader"/>)와 같은 결로 둔다. 값은 그 사람의 체력·무력·
    /// 검술·운이 그대로 판을 가른다.
    /// </remarks>
    private void PlayDuel()
    {
        if (DuelFoeDialog.Ask(this, _game.Faces) is not var (who, arena))
        {
            if (Local.Helpers.PersonTable.Open() == null)
                NoticeDialog.Show(this, "인물 표를 읽지 못했습니다", "일기토");
            return;
        }

        var dice = new GameRandom(Environment.TickCount);
        var foe = new Engine.Town.Duel.Fighter(
            who.Name,
            Body: who.Stats.Length > 0 ? who.Stats[0] : 50,
            Might: who.Stats.Length > 2 ? who.Stats[2] : 50,
            Sword: who.Skills.Length > Skill.Sword ? who.Skills[Skill.Sword] : 0,
            Luck: who.Stats.Length > 4 ? who.Stats[4] : 50,
            Weapon: 0, Armor: 0);

        var duel = new Engine.Town.Duel(MyFighter(), foe,
                                        _game.Player.Items.Contains(Engine.Town.Duel.EdithShieldId),
                                        Environment.TickCount);

        DuelDialog.Show(this, duel, dice,
                        _game.Faces?.TryGetBgra(who.Face, female: false),
                        _game.Fighters, foeSet: 1,
                        myFace: _game.Faces?.TryGetBgra(
                            PortraitAges.At(_game.Player.Face, _game.Player.Age,
                                            false, _game.Faces),
                            female: false),
                        arena: arena,
                        bgm: _game.Bgm);
    }

    private Engine.Town.Duel.Fighter MyFighter()
    {
        var me = _game.Player;
        return new(me.Name.Length > 0 ? me.Name : "제독",
                   me.AbilityOf(Ability.Body), me.AbilityOf(Ability.Might),
                   me.LevelOf(Skill.Names[Skill.Sword]), me.AbilityOf(Ability.Luck),
                   BestItem(Engine.Town.Duel.WeaponCategory),
                   BestItem(Engine.Town.Duel.ArmorCategory));
    }

    /// <summary>지닌 것 가운데 그 갈래에서 가장 센 효과. 표를 못 읽었으면 0.</summary>
    private int BestItem(int category)
    {
        if (_game.Items is not { } table) return 0;

        int best = 0;
        foreach (int id in _game.Player.Items)
            if (table.Find(id) is { } item && item.Category == category && item.Effect > best)
                best = item.Effect;
        return best;
    }

    /// <summary>
    /// 반란 대표를 그 자리에서 지어 세운다.
    /// </summary>
    /// <remarks>
    /// 인물 표에 없는 사람이라 게임도 <c>0x00475240</c> 어름에서 <c>0x004B7C0F</c>(난수)로
    /// 능력을 굴려 채운다. 굴림을 그대로 옮겼다.
    /// <code>
    ///   4752B0  +0x28 무력  = rand(15) + 0x3B   ; 59 ~ 73
    ///   475280  +0x20 체력  = rand(16) + 0x45   ; 69 ~ 84
    ///   475290  +0x24 지력  = rand(16) + 0x27   ; 39 ~ 54
    ///   4752C0  +0x2C 매력  = rand(16) + 0x27   ; 39 ~ 54
    ///   4752D0  +0x30 운    = rand(16) + 0x27   ; 39 ~ 54
    ///   4752E3  +0x34 신앙심 = 0x31              ; 49 못박음
    ///   4752F5  +0x48 검술  = 1                 ; 기능은 +0x40 부터 넷씩이다
    /// </code>
    /// <b>무력이 세다.</b> 59~73 이라 여느 술집 손님보다 한참 위다 — 검술이 1 뿐인 것이
    /// 그나마 숨통이다. 이름은 게임이 이름표에서 굴리는데 그 표를 안 짚어 「대원 대표」로
    /// 갈음한다.
    /// </remarks>
    private static Engine.Town.Duel.Fighter MutinyLeader(GameRandom dice) =>
        new("대원 대표",
            Body: dice.Next(16) + 0x45,
            Might: dice.Next(15) + 0x3B,
            Sword: 1,
            Luck: dice.Next(16) + 0x27,
            Weapon: 0, Armor: 0);

    /// <summary>
    /// 바람 칸의 글 — 풍향·풍속과 상대각, 그리고 그 바람이 내는 함대 속도.
    /// </summary>
    /// <remarks>
    /// 게임 띠에는 없는 칸이다. 돛 효율표가 상대각으로 갈리는 것이 눈에 보여야 삼각돛·
    /// 사각돛을 고르는 뜻이 생겨서 뒀다 — <b>0 이 정순풍, 8 이 정면 역풍</b>이다.
    /// </remarks>
    private string WindLine()
    {
        var (dir, speed, _) = _host.LastWind;
        string where = ShipMapHost.Compass[(dir & 0xF) >> 1];
        return $"풍향: {where}/풍속:{speed}";
    }

    /// <summary>상단 띠의 해류 칸 — 「해류: %s/속도:%d」(<c>0x0056C010</c>, <c>0x0047DF8F</c>).</summary>
    private string CurrentLine()
    {
        var (dir, speed) = _host.LastFlow;
        string where = ShipMapHost.Compass[(dir & 0xF) >> 1];
        return $"해류: {where}/속도:{speed}";
    }

    /// <summary>
    /// 배가 선 자리 둘레를 항해지도에 밝힌다. 게임의 <c>0x00468D90</c> 자리다 —
    /// 지금 칸을 가운데로 반지름 <c>측량 * 8 + 56</c> 칸의 원을 칠한다.
    /// </summary>
    /// <remarks>
    /// 게임은 함대 칸이 <b>바뀌었을 때만</b> 부른다(<c>0x0047D3FE</c>·<c>0x0047D41E</c> 비교 뒤
    /// <c>0x0047D438</c>). 같은 칸에서 되풀이하면 결과는 같고 셈만 는다.
    /// </remarks>
    private void MarkSeen()
    {
        if (_host.ShipCell is not { } cell) return;
        if (_lastMarked == (cell.CellX, cell.CellY)) return;
        _lastMarked = (cell.CellX, cell.CellY);
        _game.Player.Explored.Mark(cell.CellX, cell.CellY, ExploredMap.RadiusFor(SurveyLevel()));
    }

    /// <summary>마지막으로 밝힌 칸. 칸이 바뀔 때만 다시 칠한다.</summary>
    private (int X, int Y)? _lastMarked;

    /// <summary>발견을 마지막으로 재 본 칸. 그 사이를 지나온 칸도 함께 훑는다.</summary>
    private (int X, int Y)? _lastChecked;

    /// <summary>
    /// 한 번에 훑는 칸 수의 위쪽 끝. 이보다 멀리 뛰었으면 지금 칸만 본다 —
    /// 입항·출항처럼 자리가 통째로 옮겨간 것이지 지나온 것이 아니다.
    /// </summary>
    private const int MaxTrail = 64;

    /// <summary>지난번 잰 칸에서 지금 칸까지, 지나온 칸을 차례로 낸다.</summary>
    /// <remarks>
    /// <b>이것이 없으면 좁은 발견물을 밟고도 지나친다.</b> 발견 판정은 100ms 짜리 상태
    /// 시계에 걸려 있는데, 걸음은 화면 프레임마다 나아간다. 뭍의 한 프레임은
    /// <c>(3 x 이동값 + 54) / 10 / 16</c> 칸이라(<see cref="Engine.Sea.Sailing.CellsPerTick"/>)
    /// 이동값이 10 이면 0.5칸, 60프레임이면 초당 서른 칸이다 — 100ms 사이에 서너 칸을
    /// 건너뛴다. 카르낙 거석군의 자리가 <b>X 1226~1227 · Y 291~292 두 칸</b>이라
    /// 지금 칸만 보면 열에 아홉은 못 잡는다.
    /// </remarks>
    private static IEnumerable<(int X, int Y)> Trail((int X, int Y) from, (int X, int Y) to)
    {
        int dx = to.X - from.X, dy = to.Y - from.Y;
        int steps = Math.Max(Math.Abs(dx), Math.Abs(dy));

        if (steps == 0 || steps > MaxTrail) { yield return to; yield break; }

        // 지난번에 이미 본 칸(from)은 건너뛰고 그 다음 칸부터 낸다.
        for (int i = 1; i <= steps; i++)
            yield return (from.X + dx * i / steps, from.Y + dy * i / steps);
    }

    /// <summary>
    /// 사건이 도는 동안 지도를 파랗게 덮거나 걷는다.
    /// </summary>
    /// <remarks>
    /// 덮는 일은 <b>그리는 쪽</b>이 한다(<see cref="Rendering.ShipMapHost.Shaded"/>).
    /// 지도 칸은 <c>HwndHost</c> 라 그 위에 WPF 사각형을 얹어 봐야 밑에 깔린다 —
    /// 처음에 그렇게 넣었다가 화면에서 아무 일도 안 일어나 옮겼다.
    /// </remarks>
    private void Tint(bool on) => _host.Shaded = on;

    /// <summary>
    /// 초심자(EASY) 캐릭터의 개인 퀘스트라인(이야기0·이야기1) 한 장면을 체크한다 — 건물
    /// 조건이 없는(도시·연도·명성만 보는) 장면용이다. 건물 안에서 여는 것은
    /// <see cref="CityPicView.CheckStory"/> 가 따로 본다(같은 <see cref="StoryLog"/> 를 쓴다).
    /// </summary>
    private void CheckStory(int building)
    {
        if (_game.Player.ActiveStoryBook is not { } book) return;
        if (StoryLog.NextPart(_game.Player, _game, building) is not { } part) return;

        DisevRunner.Run(this, _game, book, part, building);
        StoryLog.Advance(_game.Player, _game, book, part);

        if (DisevRunner.LastEndedInGameOver)
        {
            GameOver(DisevRunner.LastGameOverPicture);
            ReturnToTitle();
        }
    }

    private void CheckDiscovery()
    {
        if (_asking || _host.Paused || _host.SeaBlocked) return;
        if (_game.Discoveries is not { } log) return;
        if (_host.ShipCell is not { } cell) return;

        var now = (cell.CellX, cell.CellY);
        var from = _lastChecked ?? now;
        _lastChecked = now;

        int id = -1;
        foreach (var (x, y) in Trail(from, now))
        {
            id = log.At(_game.Player, x, y, _host.IsOnLand);
            if (id >= 0) break;
        }

        if (id < 0) return;
        if (log.Table.Find(id) is not { } row) return;

        // 알리는 동안 배가 계속 가면 다음 칸에서 또 뜬다.
        _asking = true;
        _host.Paused = true;
        bool over = false;
        try
        {
            // 사건이 도는 동안 지도가 파래진다.
            Tint(true);

            // DISEV.CDS 에 대본이 있으면 <b>그것이 다 한다</b> — 부하 대사 · 동영상 · 음원 ·
            // 아이템 · 발견까지. 카르낙 거석군(19번)은 열세 줄짜리다.
            bool scripted = DisevRunner.Run(this, _game, id);
            if (!scripted) PlainNotice(row);

            // 대본이 게임 오버(명령 4A)로 끝났으면 — 피라미드에서 성배 퍼즐에 지면 그렇다 —
            // 발견을 적지 않고 놀이를 끝낸다. 반란에 졌을 때와 같은 차례다.
            if (DisevRunner.LastEndedInGameOver)
            {
                over = true;
                GameOver(DisevRunner.LastGameOverPicture);
            }

            // 대본이 돌았으면 발견은 <b>대본의 01 0B 만</b> 적는다 — 게임의 발견 판정(0x0048D3F0)은
            // 대본 뒤에 결과 코드만 볼 뿐 발견을 따로 안 적는다. 예전에는 여기서 늘 적어서 존왕의
            // 술잔(104)을 낚시에 지고도(대본은 4E 로 끝남) 손에 넣었다. 대본이 없을 때만 여기서 적는다.
            // 발견물 아이템은 여기서 안 든다 — 발표할 때 들어온다(GameInfo.VirtualItems).
            if (!over && !scripted) log.Discover(_game.Player, id);

            // 게임은 대본 결과가 0 이나 1 이면 그 발견물 줄의 +0x17 에 비트 0 을 세운다
            // (0x0048D569 · 0x0049294A · 0x00492B49). 그런데 <b>그 비트를 읽는 데가 EXE
            // 어디에도 없다</b> — 세우기만 하는 죽은 깃발이라 옮길 것이 없다. 「한 번 본
            // 사건은 다시 안 뜬다」로 쓰려던 자리로 보인다.
        }
        finally
        {
            // 찾았으면 지도에 돋는다 — 못 찾은 동안 바탕 타일로 덮여 있었다.
            HideCities();

            Tint(false);
            _host.Paused = false;
            _asking = false;
        }

        // 창을 되돌리는 것은 try 밖에서 한다 — 안에서 하면 닫히는 창에 잠금을 풀게 된다.
        if (over) ReturnToTitle();
    }

    /// <summary>
    /// 대본이 없을 때의 발견 알림 — 동영상 한 편과 한 줄이다.
    /// </summary>
    /// <remarks>
    /// 자리에서 발견했을 때 게임이 쓰는 것은 <b>짧은 꼴</b>이다 —
    /// <c>0x00544720</c> "%s%s 발견했다!"(<c>0x004B3801</c> 이 쓴다). 긴 꼴
    /// "%s%s [%s]%s 발견했습니다"(<c>0x00538490</c>) 는 항구에서 <b>보고</b>할 때다.
    /// </remarks>
    private void PlainNotice(in DiscoveryTable.Record row)
    {
        MoviePlayer.Play(this, DiscoveryDialog.MovieOf(_game.Directory, row.Movie));

        string found = $"{row.Name}{GameUi.Josa(row.Name, "을", "를")} 발견했다!";

        // 동영상을 튼 뒤에는 그림을 또 세우지 않는다. 그림만 있는 것이면 그림을 낸다.
        if (row.Movie >= 0) NoticeDialog.Show(this, found);
        else DiscoveryDialog.Show(this, _game.Stills, row.Picture, found);
    }

    /// <summary>
    /// 커맨드의 「항해일지를 본다」 — 연표와 같은 창을 쪽 갈래만 달리해 띄운다(<c>0x004246C0</c>).
    /// </summary>
    private void ShowLogbook() =>
        ChronicleDialog.ShowLogbook(this, _game.Player, _game.Discoveries?.Table);

    /// <summary>
    /// 「도시좌표」(<c>0x004269F0</c>) — 가 본 도시를 골라 위도·경도를 듣는다.
    /// </summary>
    /// <remarks>
    /// 가 본 곳이 열여섯 곳이 넘으면 문화권부터 고르고(맨 끝에 「전도시 일람」), 그 밑이면 바로 도시 목록이다.
    /// 측량사 자리에 사람이 있으면 <b>그 사람이 말하고</b>, 없으면 얼굴 없는 알림이다(<c>0x00426C6E</c>).
    /// </remarks>
    private void ShowCityCoordinates()
    {
        var player = _game.Player;
        var rows = _game.CityRows;
        var seen = new List<int>();
        for (int city = 0; city < CityExeTable.Count; city++)
            if (player.Knows(city) && CityCoordinates.Of(rows, city) != null) seen.Add(city);

        // 고를 도시가 없으면 말없이 물린다 — 원본(0x004269F0)에는 빈 목록을 알리는 말이 없다.
        if (seen.Count == 0) return;

        _asking = true;
        _host.Paused = true;
        try
        {
            bool guided = false;
            while (true)
            {
                var list = seen;
                if (seen.Count >= CityCoordinates.AskRegionFrom)
                {
                    if (!guided) { guided = true; NoticeDialog.Show(this, CityCoordinates.Guide); }

                    var regions = seen.Select(c => rows?.CultureOf(c) ?? -1)
                                      .Where(r => r >= 0 && r < CityCoordinates.Regions.Length)
                                      .Distinct().Order().ToList();
                    var names = regions.Select(r => CityCoordinates.Regions[r]).ToList();
                    names.Add(CityCoordinates.AllCities);

                    int at = ChoiceDialog.Ask(this, "", names);
                    if (at < 0) return;
                    if (at < regions.Count)
                        list = [.. seen.Where(c => rows?.CultureOf(c) == regions[at])];
                }

                int pick = ChoiceDialog.Ask(this, CityCoordinates.CityListTitle,
                                            [.. list.Select(_game.CityName)]);
                if (pick < 0)
                {
                    if (seen.Count >= CityCoordinates.AskRegionFrom) continue;    // 도시 목록만 낼 때는 그대로 끝난다
                    return;
                }

                int city = list[pick];
                if (CityCoordinates.Of(rows, city) is not { } spot) continue;

                string name = _game.CityName(city);
                string mate = player.MateAt(CityCoordinates.SurveyorSlot);
                if (mate.Length > 0 && player.MateInfoOf(mate) is { } who)
                    TalkDialog.Say(this, _game.Faces?.TryGetBgra(who.Face, female: false), "",
                                   $"{name}{NameToken.Of(name, 9)} {spot.Words}");
                else NoticeDialog.Show(this, $"{name}  {spot.Words}");

                if (seen.Count < CityCoordinates.AskRegionFrom) return;
            }
        }
        finally
        {
            _host.Paused = false;
            _asking = false;
        }
    }

    /// <summary>
    /// 도시 밖에서 하루 — 제독 HP 를 닳리고, 문턱을 막 넘었으면 부관이 말한다(<see cref="Vitality.PassDay"/>).
    /// </summary>
    /// <remarks>
    /// <b>여기서는 안 죽는다.</b> 게임이 HP 0 을 보는 자리는 둘뿐이다 — 도시에 들어설 때(<c>0x00492717</c>)와
    /// 병이 새로 터질 때(<c>0x004748F6</c> · <c>0x00474AA2</c>)다. 바다에서는 0 인 채로 계속 떠 있는다.
    /// </remarks>
    private void PassVitalityDay()
    {
        var player = _game.Player;
        if (Vitality.PassDay(player) is { } warn)
        {
            _host.Paused = true;
            _asking = true;
            TalkDialog.Say(this, MateFace(), "", warn);
            _asking = false;
            _host.Paused = false;
        }
    }

    /// <summary>
    /// 병이 새로 터지는 자리에서 HP 가 0 이면 그대로 끝난다(<c>0x004748F6</c> · <c>0x00474AA2</c> →
    /// <c>0x0044AF40(0)</c>). 끝났으면 true.
    /// </summary>
    private bool DiedOfDisease()
    {
        if (Vitality.DiseaseDeath(_game.Player) is not { } death) return false;

        _host.Paused = true;
        _asking = true;
        TalkDialog.Say(this, MateFace(), "", "제독, 정신차리십시오! 제독, 제독!");   // 0x00534ED0
        NoticeDialog.Show(this, death);                                              // 0x00534F30 · 0x00535090
        GameOver();
        _asking = false;
        Dispatcher.BeginInvoke(ReturnToTitle);
        return true;
    }

    /// <summary>
    /// 입항한 도시의 그림을 지도 한가운데에 띄운다. CITYCG.CDS 가 없거나 그림을 못 풀면
    /// 조용히 넘어간다 — 그림은 덤이고, 입항은 이미 끝났다.
    /// </summary>
    /// <remarks>
    /// 도시에 들어가 있는 동안에는 곡이 바뀌고 지도에 남색 막이 씌워진다. 창은 모달이 아니다 —
    /// 모달이면 함대 창 제목 줄이 죽는다. 그래서 창이 닫힐 때 곡·막·멈춤을 함께 푼다.
    /// </remarks>
    /// <returns>도시 창을 띄웠으면 true.</returns>
    private bool ShowCityPicture(int city, string name, bool enterHome = false, bool resumed = false)
    {
        // 그림도 건물 표도 Game 이 처음 쓸 때 연다. 둘 중 하나라도 없으면 도시 화면을 안 연다.
        if (_game.CityPics == null || _game.Buildings == null) return false;

        // 도는 곡은 문화권마다 다르다 — 세우타 같은 중근동 도시는 딴 곡이다.
        // 문화권은 건물에 들어갈 때 뜨는 타원 사진을 고르는 데도 쓴다(BuildingPhoto).
        string culture = _game.CultureOf(city);
        int track = BgmPlayer.CityTrackFor(culture, _game.CityRows?.CultureOf(city) ?? -1);

        // 도시에 들어서면 비·눈이 그친다(0x0048EACA — 빗소리도 끊는다, 0x0048EA69).
        EndWeather();

        // 바다로 들어서면 함대가 이 도시에 닻을 내린다(0x0048B54E). 말로 걸어 들어오면 안 바뀐다.
        bool bySea = !_host.IsOnLand;
        if (enterHome || bySea) _game.Player.MoorAt(city);
        // 함대가 기다리는 도시로 <b>걸어 돌아왔으면 배에 다시 오른다</b> — 성문 건물이 들어설 때
        // 뭍 표시를 끄고 대 둔 바다 자리를 되돌린다(0x0046871F → 0x004745B0).
        else if (_game.Player.FleetCity == city) _host.Embark();

        var dialog = CityPicView.Open(this, _game, city, name, MapAreaOnScreen(), track, culture);
        if (dialog == null) return false;

        _game.Bgm.Play(track);
        SetInCity(true);          // 지도에 남색 막을 씌운다(그림 창과는 따로 논다)
        _game.Player.EnterCity(city, name);
        // 도시 상태를 반영한 뒤 저장해야 CONTINUE가 실제 입항 도시에서 시작한다.
        AutoSaveHere();
        // 건물 조건 없이 도시·연도·명성만으로 여는 이야기 장면(장의 첫머리)은 여기서 잡는다 —
        // 건물 안에서 여는 것은 CityPicView.CheckStory 가 따로 본다.
        CheckStory(-1);

        // 도시에 들어서면 HP 를 본다(0x00492717) — 0 이면 쓰러져 끝나고, 모자라면 부관이 쉬라고 한다.
        if (_game.Player.Condition <= 0)
        {
            var words = Vitality.CollapseWords(_game.Player);
            for (int i = 0; i < words.Length; i++)
            {
                if (i % 2 == 0) TalkDialog.Say(dialog, MateFace(), "", words[i]);
                else NoticeDialog.Show(dialog, words[i]);
            }
            GameOver();
            Dispatcher.BeginInvoke(ReturnToTitle);
            return true;
        }
        if (Vitality.EntryWarning(_game.Player) is { } warn)
            TalkDialog.Say(dialog, MateFace(), "", warn);


        // 지구를 돌고 계약을 맺은 도시로 돌아왔으면 그 자리에서 세계일주 장면이 돈다
        // (0x00492040) — 항구 명령 창보다 먼저다.
        if (WorldRouteScene.Due(_game, city, _game.Player.MateAt(0).Length > 0))
            WorldRouteScene.Play(dialog, _game, SponsorFaceOf(_game.Player.Contract?.Sponsor), MateFace());
        // 들어가는 데 열흘 — 다만 새 판은 이미 자택 안에서 시작하므로 날을 안 보낸다.
        // 게임도 새 판은 1월 1일에 자택 명령 창이 떠 있다. 여기서 열흘을 보내 1월 11일이 되었었다.
        // 세이브를 열어 이어 가는 도시도 이미 들어와 있던 것이라 날을 안 보낸다 — 예전에는 불러올
        // 때마다 열흘씩 흘렀다.
        if (!enterHome && !resumed) PassPortDays();
        // 새 판은 자택 안에서 시작한다 — 게임도 판을 열면 자택 명령 창이 이미 떠 있다.
        if (enterHome) dialog.EnterHome();
        // 닿았으면 바다는 항구 차림표부터, 뭍은 성문을 지나며 부관이 인사한다(0x004A2530).
        // 세이브를 열어 이어 가는 도시는 이미 들어와 있던 것이라 이 대목이 없다.
        else if (!resumed) dialog.Arrive(bySea);
        dialog.Closed += (_, _) =>
        {
            SetInCity(false);

            // 항구에서 출항했는데 아직 뭍이면(뭍으로 걸어 들어온 마을이다) 그 마을 앞바다에 배를
            // 띄운다. 예전에는 출항을 따로 안 받아, 말을 탄 채 뭍에 그대로 남았다.
            if (dialog.Sailed && _host.IsOnLand) _host.PlaceAtCity(city);
            // 출항하면 닻을 걷는다(0x0048EB84). 성문으로 나섰으면 함대는 이 도시에 그대로 있다.
            if (dialog.Sailed) _game.Player.MoorAt(-1);

            // 성문으로 나섰으면 뭍에 올라 말로 걷는다 — 곡도 뭍 것으로 바뀐다.
            // 이미 뭍에 서 있으면(말로 걸어 들어온 마을이면) Land() 는 거짓을 낸다 — 그때도 걷는
            // 것이다. 예전에는 그 거짓을 그대로 받아 성문으로 나섰는데 출항 곡이 돌았다.
            bool walking = dialog.Explored && (_host.IsOnLand || _host.Land());
            _game.Bgm.Play(walking ? BgmPlayer.LandTrack : BgmPlayer.SeaTrack);
            _host.Paused = false;
            _asking = false;
            _game.Player.EnterCity(-1);
            // 나오는 데도 열흘 — 다만 닿자마자 뜬 항구 차림표에서 곧장 출항하면 없다(0x00477319).
            if (!dialog.SailedOnArrival) PassPortDays();
            InfoMenu.Close();        // 도시를 나오면 도시정보 창도 같이 걷는다

            // 도시에서 계약을 맺거나 깨거나 보고했으면 목표 유적 그림이 드러나거나 다시 덮인다.
            HideCities();
        };
        return true;
    }

    /// <summary>마을에 들고 나는 데 드는 날수.</summary>
    /// <remarks>
    /// 게임은 마을에 들 때와 날 때 <b>열흘씩</b> 보낸다 — <c>0x004A2AD0(10, 2)</c> 을
    /// 부르는 자리가 도시 쪽 <c>0x0046875A</c>·<c>0x00468786</c> 과 항구 쪽
    /// <c>0x00477326</c> 에 나란히 있다.
    ///
    /// <b>이 스무 날이 빠져 있어 우리 놀이가 훨씬 빨랐다.</b> 세비야에서 카르낙 거석군을
    /// 왕복하면 원본이 스무아흐레인데, 그 가운데 스무 날이 들고 나는 값이고 실제로 걷는
    /// 것은 아흐레쯤이다. 우리는 걷는 날만 세고 있었으니 절반이 될 수밖에 없었다.
    /// </remarks>
    /// <remarks>기본은 원본대로 열흘이고, 개발 창 「출입 일수」로 1~10 사이에서 줄일 수 있다.</remarks>
    private static int PortDays => Local.Settings.GameSettings.PortDays;

    /// <summary>마을에 들거나 날 때 설정한 날수(기본 열흘)를 보낸다.</summary>
    private void PassPortDays() => _game.Player.AdvanceDays(PortDays);

    /// <summary>
    /// 게임 원본 화면 조각과 비트맵 글꼴을 한 번만 읽어 <see cref="GameUi"/> 에 넣는다.
    /// 게임 폴더를 아직 모르면 그냥 넘어간다 — 세이브를 열면 다시 부른다.
    /// </summary>
    private void LoadSprites()
    {
        // 게임 폴더를 몰라도 연다 — 조각과 글꼴은 asset 을 먼저 보므로 폴더 없이도 원본 그대로 선다.
        // 예전에는 폴더가 비면 여기서 돌아가 버려 세이브를 안 연 첫 실행에서 메인메뉴가 민색 상자였다.
        // 폴더를 나중에 알게 되면 한 번 더 연다(asset 에 빠진 조각을 CDS 에서 마저 찾는다).
        var dir = _game.Directory;
        if (_spritesTriedFor == dir) return;
        _spritesTriedFor = dir;

        GameUi.Sprites = UiSprites.Open(dir);
        if (GameUi.Sprites == null)
            System.Diagnostics.Debug.WriteLine($"[ShipMap] 화면 조각 없음: {UiSprites.LastError}");

        GameUi.Font = GameFont.Open(dir);
        if (GameUi.Font == null)
            System.Diagnostics.Debug.WriteLine($"[ShipMap] 게임 글꼴 없음: {GameFont.LastError}");
    }

    /// <summary>조각·글꼴을 마지막으로 연 게임 폴더(모르면 빈 문자열). 아직 안 열었으면 null.</summary>
    private string? _spritesTriedFor;

    /// <summary>
    /// 게임 폴더를 잡고 타이틀 곡을 튼다. 지도는 아직 띄우지 않는다 —
    /// 메뉴에서 NEW/LOAD 를 골라야 <see cref="StartMap"/> 로 넘어간다.
    /// </summary>
    private async void OnLoaded(object? sender, RoutedEventArgs e)
    {
        // <b>작업표시줄 단추를 하나로 둔다.</b> 뷰어에서 띄운 것이면 이 창까지
        // 단추를 갖는데, 대화 창을 여닫을 때마다 활성 창이 두 단추 사이를 오가는 것이
        // 눈에 보인다 — WPF 의 ShowDialog 가 앱의 창을 다 잠갔다 푸는 통에 활성 창이
        // 잠깐 주인 쪽으로 넘어가기 때문이다. 주인이 없으면(놀이 전용 exe) 제 단추를
        // 갖는다.
        ShowInTaskbar = Owner == null;

        var dir = Path.GetDirectoryName(AppSettings.LastSaveFilePath);
        if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir))
            dir = AppContext.BaseDirectory;

        _game.SetDirectory(dir);

        // 타이틀 화면은 생성자에서 지었는데, 그때는 게임 폴더를 몰라 원본 조각도 글꼴도
        // 없었다(민색 상자로 물러선 채였다). 이제 알았으니 다시 짓는다.
        LoadSprites();
        if (_titleRoot != null && ReferenceEquals(_screen.Content, _titleRoot))
        {
            // 묶음은 BuildTitleScreen 이 새로 잡는다 — 새 줄에 초점이 다시 간다.
            _titleRoot = BuildTitleScreen();
            _screen.Content = _titleRoot;
        }

        if (!BgmPlayer.IsAvailable(dir))
        {
            var result = MessageBox.Show(
                "배경음악 파일이 없습니다.\n릴리즈에서 BGM을 다운로드할까요?",
                "BGM 다운로드",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);
            if (result == MessageBoxResult.Yes)
            {
                _status.Text = "BGM 다운로드 중...";
                Say("BGM 다운로드 중...");
                var download = await BgmAssetDownloader.DownloadAsync();
                if (!download.Success)
                {
                    Say($"BGM 다운로드 실패: {download.Error}");
                    MessageBox.Show($"BGM 다운로드 실패:\n{download.Error}", "오류",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                }
                else
                {
                    Say("BGM 다운로드 완료");
                }
            }
        }

        _game.Bgm.Enabled = GameSettings.BgmEnabled;   // 설정 창에서 꺼 뒀으면 조용히 시작한다
        _game.Bgm.Play(BgmPlayer.TitleTrack);   // 메뉴 화면에서는 bgm/Track23.mp3
        if (_game.Bgm.LastError.Length > 0)
        {
            _status.Text = _game.Bgm.LastError;
            System.Diagnostics.Debug.WriteLine($"[ShipMap] BGM — {_game.Bgm.LastError}");
        }
    }
}

/// <summary>상태 줄만 이따금 갱신하려고 쓰는 간단한 타이머.</summary>
internal sealed class DispatcherTimerLite
{
    private readonly System.Windows.Threading.DispatcherTimer _t;

    public DispatcherTimerLite(TimeSpan interval, Action tick)
    {
        _t = new System.Windows.Threading.DispatcherTimer { Interval = interval };
        _t.Tick += (_, _) => tick();
    }

    public void Start() => _t.Start();
    public void Stop() => _t.Stop();
}
