using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using Line = System.Windows.Shapes.Line;
using System.Windows.Threading;
using CdsHelper.Game.Local.Helpers;
using CdsHelper.Support.Local.Settings;
using CdsHelper.Support.UI.Units;

namespace CdsHelper.Game.UI.Views;

/// <summary>
/// 모션 메이커 — 일기토 그림 서른셋을 늘어놓고, 번호를 적어 이어 돌려 보는 창.
/// </summary>
/// <remarks>
/// 일기토 몸짓은 <see cref="DuelStage"/> 가 눈금마다 어느 장을 낼지 정해 짓는데, 그
/// 차례를 코드에 적어 넣고 앱을 다시 구워 보는 것 말고는 눈으로 맞댈 길이 없었다.
/// 이 창은 그 앞자리다 — <b>번호를 적으면 그대로 돌려 준다</b>.
/// <code>
///   그림   FIGHTER.CDS 의 한 스프라이트셋이 144x136 짜리 서른세 장(<see cref="FighterSprites"/>)
///   아군   스프라이트셋 0(제독)        오른쪽에 서서 왼쪽을 본다
///   적군   스프라이트셋 1~8(문화권)    왼쪽에 서서 오른쪽을 본다
///   자리   상대 60 · 나 173  <see cref="DuelStage"/> 가 갈무리로 재어 둔 그 자리다
/// </code>
/// 한 장은 <b>0.1초</b>가 밑값이고 장마다 따로 늘리고 줄인다. 게임의 눈금 하나는 실은
/// 0.067초(1/15초)지만 여기서는 손으로 맞추기 좋게 0.1초씩 끊는다 — 0.1 단위로 굴러가는
/// 스피너가 장마다 하나씩 붙는다.
/// </remarks>
public sealed class MotionMakerDialog : GameWindow
{
    /// <summary>그림 목록에 거는 배율의 밑값과 한계, 그리고 한 번에 굴리는 폭.</summary>
    private const double DefaultBookScale = 1.0, MinBookScale = 0.25, MaxBookScale = 3.0, BookScaleStep = 0.25;

    /// <summary>차례 줄에 거는 배율.</summary>
    private const double StripScale = 0.4;

    /// <summary>
    /// 한 장이 머무는 시간 — 밑값과 그 한계.
    /// </summary>
    /// <remarks>
    /// 굴리는 폭은 0.1 이지만 <b>적어 두는 값은 소수 두 자리</b>다. 게임 눈금이 0.067초라
    /// 적어 둔 모션이 0.07 · 0.13 · 0.27 같은 값을 쓰는데, 한 자리로 끊으면 죄다 0.1 로
    /// 뭉개져 판이 느려진다.
    /// </remarks>
    private const double DefaultSeconds = 0.1, MinSeconds = 0.05, MaxSeconds = 9.9;

    /// <summary>
    /// 앞으로 나가는 거리의 한계와 한 번에 굴리는 폭.
    /// </summary>
    /// <remarks>
    /// 게임이 쓰는 값이 죄다 열의 배수라(내지름 30 · 다가섬 40 · 한 걸음 10) 다섯씩
    /// 굴린다. 그 사이 값은 칸에 곧바로 적어 넣으면 된다.
    /// </remarks>
    private const double MaxPush = 150, PushStep = 5;

    /// <summary>두 사람이 서는 자리. <see cref="DuelStage"/> 의 <c>FoeStand</c> · <c>MyStand</c> 다.</summary>
    private const double FoeStand = 80, MyStand = 152;

    /// <summary>
    /// 조각 안에서 <b>발 가운데</b>가 앉은 자리 — 여느 첫 장(30)을 재어 잡았다.
    /// </summary>
    /// <remarks>
    /// 조각이 144점 폭이고 사람은 그 안쪽에 그려져 있어, 조각 왼끝(<see cref="FoeStand"/> ·
    /// <see cref="MyStand"/>)에 줄을 그으면 사람과 한참 어긋난다. 발 자리에 그어야 눈에 맞는다.
    /// </remarks>
    private const double FoeFeet = 76.0, MyFeet = 86.5;

    /// <summary>몸짓 이름 — <see cref="FighterSprites.Move"/> 차례 그대로다.</summary>
    private static readonly string[] MoveNames =
        ["상단", "중단", "하단", "도약", "피함", "웅크림", "승리", "쓰러짐", "여느"];

    /// <summary>적군 스프라이트셋 여덟. 이름은 <c>0x00534420</c> 에 적힌 그대로다.</summary>
    private static readonly string[] FoeSetNames =
        ["유럽", "아프리카", "아랍", "아시아A", "아시아Ｂ", "중국", "일본", "아즈텍"];

    /// <summary>고를 수 있는 배경 — <c>asset/duel</c> 에 뽑아 둔 것 그대로다.</summary>
    private static readonly (string Key, string Name)[] Arenas =
    [
        ("duel-field", "초원"), ("duel-tavern", "술집"), ("duel-deck", "갑판"),
        ("duel-sand", "모래벌"), ("duel-wood", "숲"),
        ("duel-temple", "사원"), ("duel-mosque", "모스크"),
    ];

    /// <summary>
    /// 차례 한 자리 — 어느 장을 몇 초 동안, <b>얼마나 앞으로 나가서</b> 보일지.
    /// </summary>
    private sealed class Step
    {
        public int Frame;
        public double Seconds = DefaultSeconds;

        /// <summary>
        /// 그 장 동안 <b>제자리에서 앞으로</b> 나가는 거리(점). 뒤로 물러나면 음수다.
        /// </summary>
        /// <remarks>
        /// 게임은 내지를 때 치는 쪽을 <b>서른 점</b> 앞으로 보낸다
        /// (<c>0x004A794A</c> 의 <c>sub eax,0x1E</c>). 갈무리 두 장(상단1 · 상단2)의
        /// 배경을 맞춰 재어 보아도 조각이 <b>30.9점</b> 옮겨 가 그 값과 맞는다.
        ///
        /// 앞이란 <b>상대 쪽</b>이다 — 아군은 왼쪽으로, 적군은 오른쪽으로 간다.
        /// </remarks>
        public double Push;
    }

    /// <summary>한쪽 — 아군이든 적군이든 제 차례와 제 시계를 따로 가진다.</summary>
    private sealed class Lane(string name, int set, bool forward)
    {
        /// <summary>「아군」이나 「적군」.</summary>
        public string Name { get; } = name;

        /// <summary>스프라이트셋. 적군은 문화권을 바꾸면 따라 바뀐다.</summary>
        public int Set { get; set; } = set;

        /// <summary>오른쪽을 보고 왼편에 서는 쪽인지 — 적군이 그렇다.</summary>
        public bool Forward { get; } = forward;

        public List<Step> Steps { get; } = [];
        public Image View { get; } = new();
        public TextBox Numbers { get; } = new();
        public WrapPanel Strip { get; } = new();
        public WrapPanel Book { get; } = new();
        public TextBlock Head { get; } = new();
        public DispatcherTimer Clock { get; } = new();

        /// <summary>지금 보이고 있는 자리.</summary>
        public int At { get; set; }

        /// <summary>차례 줄에 선 칸들 — 고른 자리를 다시 칠할 때 쓴다.</summary>
        public List<Border> Cells { get; } = [];

        /// <summary>고른 자리. 아무것도 안 골랐으면 −1.</summary>
        public int Picked { get; set; } = -1;
    }

    private readonly Lane _mine = new("아군", 0, forward: false);
    private readonly Lane _foe = new("적군", 1, forward: true);

    private readonly ComboBox _foeSet = new() { Width = 110 };
    private readonly ComboBox _arena = new() { Width = 110 };
    /// <summary>미리보기 판 배율의 밑값과 한계, 한 번에 굴리는 폭.</summary>
    private const double DefaultStageScale = 1.5, MinStageScale = 0.5,
                         MaxStageScale = 4.0, StageScaleStep = 0.25;

    /// <summary>판 배율 알림.</summary>
    private readonly TextBlock _stageShown = new()
    {
        Width = 46,
        TextAlignment = TextAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center,
        Foreground = Brushes.Gray,
    };

    /// <summary>쪽마다의 모션 고르기 — 고르면 그 차례가 곧바로 든다.</summary>
    private readonly ComboBox _foeMotion = new() { Width = 150 };
    private readonly ComboBox _myMotion = new() { Width = 150 };

    /// <summary>짝을 지을 때 보는 이름들 — 앞이 찌르기 셋, 뒤가 막기 셋이다.</summary>
    private static readonly string[] Lines =
    [
        DuelMotions.ThrustKey(0, +1), DuelMotions.ThrustKey(1, +1), DuelMotions.ThrustKey(2, +1),
        DuelMotions.ThrustKey(0, 0), DuelMotions.ThrustKey(1, 0), DuelMotions.ThrustKey(2, 0),
    ];

    private static readonly string[] Blocks =
    [
        DuelMotions.GuardKey(0, -1), DuelMotions.GuardKey(1, -1), DuelMotions.GuardKey(2, -1),
    ];

    /// <summary>마지막으로 만진 쪽 — 「저장」이 이 쪽 차례를 적는다.</summary>
    private Lane? _touched;

    /// <summary>재생·일시정지 토글 단추.</summary>
    private Button? _playPause;

    /// <summary>지금 도는 중인지.</summary>
    private bool _playing;

    private const string PlayText = "▶ 재생", PauseText = "⏸ 일시정지";

    private readonly CheckBox _loop = new()
    {
        Content = "되돌이",
        IsChecked = true,
        VerticalAlignment = VerticalAlignment.Center,
        Margin = new Thickness(8, 0, 8, 0),
    };

    private readonly TextBlock _status = new()
    {
        Margin = new Thickness(12, 6, 12, 8),
        TextWrapping = TextWrapping.Wrap,
    };

    /// <summary>새로 넣는 장이 받는 시간. 0.1 씩 굴러간다.</summary>
    private readonly NumericSpinner _every = new()
    {
        Minimum = MinSeconds,
        Maximum = MaxSeconds,
        Step = 0.1,
        DecimalPlaces = 2,
        Value = DefaultSeconds,
        Width = 84,
        VerticalAlignment = VerticalAlignment.Center,
    };

    private readonly Canvas _stage = new()
    {
        Width = DuelArt.ArenaWidth,
        Height = DuelArt.ArenaHeight,
        Background = Brushes.Black,
        ClipToBounds = true,
    };

    private readonly Image _ground = new();

    /// <summary>
    /// 판 위에 얹는 격자 — <b>열 점마다</b> 가는 줄, <b>쉰 점마다</b> 굵은 줄이다.
    /// </summary>
    /// <remarks>
    /// 게임 자리를 그대로 재는 눈금이라 판 좌표(384x136)로 그린다 — 판을 키우고 줄여도
    /// 같이 늘고 준다. 두 사람이 서는 자리(60 · 173)에는 빛깔 줄을 따로 세워 둔다.
    /// </remarks>
    private readonly Canvas _grid = new()
    {
        Width = DuelArt.ArenaWidth,
        Height = DuelArt.ArenaHeight,
        IsHitTestVisible = false,
    };

    /// <summary>
    /// 늘이는 결 — 켜면 점을 뭉개어 고르게 늘인다.
    /// </summary>
    /// <remarks>
    /// 밑값은 <see cref="BitmapScalingMode.NearestNeighbor"/> 다. 점 하나가 네모로
    /// 커져 원본 그대로 보이지만 계단이 굵게 진다. 켜면 <see cref="BitmapScalingMode.Fant"/>
    /// 로 이웃 점을 섞어 부드럽게 늘인다 — 대신 획이 흐려진다.
    /// </remarks>
    private readonly ComboBox _smoothScale = new() { Width = 128, VerticalAlignment = VerticalAlignment.Center };

    /// <summary>
    /// 늘이는 결 — 목록 차례 그대로다.
    /// </summary>
    /// <remarks>
    /// <code>
    ///   점 그대로   NearestNeighbor  점 하나가 네모로 커진다 (밑값)
    ///   이웃 섞기   Linear           바로 옆 넷을 섞는다 — 가볍고 조금 부드럽다
    ///   곱게        Fant             넓게 섞는다 — 줄일 때 특히 곱다
    ///   맡기기      Unspecified      WPF 가 알아서 고른다
    /// </code>
    /// </remarks>
    private static readonly (string Name, BitmapScalingMode Mode)[] Scalings =
    [
        ("점 그대로", BitmapScalingMode.NearestNeighbor),
        ("이웃 섞기", BitmapScalingMode.Linear),
        ("곱게", BitmapScalingMode.Fant),
        ("맡기기", BitmapScalingMode.Unspecified),
    ];

    /// <summary>
    /// 가장자리 — 켜면 테두리 계단을 갈아 낸다.
    /// </summary>
    /// <remarks>
    /// 밑값은 <see cref="EdgeMode.Aliased"/> 라 테두리가 칼같이 진다. 켜면 WPF 가
    /// 알아서 갈아 내(<see cref="EdgeMode.Unspecified"/>) 비침 테두리가 매끈해진다.
    /// </remarks>
    private readonly CheckBox _smoothEdge = new()
    {
        Content = "가장자리 갈기",
        VerticalAlignment = VerticalAlignment.Center,
        Margin = new Thickness(0, 0, 8, 0),
        ToolTip = "테두리 계단을 갈아 낸다 — 끄면 칼같이 진다",
    };

    /// <summary>흐림 — 판 전체에 얹는다. 0 이면 안 얹는다.</summary>
    private readonly NumericSpinner _blur = new()
    {
        Minimum = 0,
        Maximum = 5,
        Step = 0.2,
        DecimalPlaces = 1,
        Value = 0,
        Width = 74,
        VerticalAlignment = VerticalAlignment.Center,
    };

    /// <summary>
    /// 겹쳐 그리기 — 판을 그만큼 크게 그렸다가 줄여 앉힌다.
    /// </summary>
    /// <remarks>
    /// <see cref="BitmapCache.RenderAtScale"/> 다. 2 로 두면 두 배 크기로 그린 뒤 절반으로
    /// 줄이므로, 늘이는 결이 「곱게」일 때 계단이 한 번 더 갈린다. 1 이면 안 쓴다.
    /// </remarks>
    private readonly NumericSpinner _over = new()
    {
        Minimum = 1,
        Maximum = 4,
        Step = 0.5,
        DecimalPlaces = 1,
        Value = 1,
        Width = 74,
        VerticalAlignment = VerticalAlignment.Center,
    };

    /// <summary>주사선 — 옛 브라운관처럼 한 줄 걸러 어둡게 깐다.</summary>
    private readonly CheckBox _scan = new()
    {
        Content = "주사선",
        VerticalAlignment = VerticalAlignment.Center,
        Margin = new Thickness(8, 0, 4, 0),
        ToolTip = "한 줄 걸러 어둡게 깔아 옛 화면처럼 보이게 한다",
    };

    /// <summary>주사선 진하기.</summary>
    private readonly NumericSpinner _scanDeep = new()
    {
        Minimum = 0.1,
        Maximum = 0.9,
        Step = 0.1,
        DecimalPlaces = 1,
        Value = 0.3,
        Width = 68,
        VerticalAlignment = VerticalAlignment.Center,
    };

    /// <summary>주사선을 깔아 두는 곳 — 판 위에 겹친다.</summary>
    private readonly Canvas _scanLines = new()
    {
        Width = DuelArt.ArenaWidth,
        Height = DuelArt.ArenaHeight,
        IsHitTestVisible = false,
        Visibility = Visibility.Collapsed,
    };

    /// <summary>격자를 켜고 끄는 상자.</summary>
    private readonly CheckBox _gridOn = new()
    {
        Content = "격자",
        IsChecked = true,
        VerticalAlignment = VerticalAlignment.Center,
        Margin = new Thickness(8, 0, 8, 0),
        ToolTip = "열 점마다 눈금을 얹는다 — 쉰 점마다 굵고, 선 자리 둘은 빛깔 줄이다",
    };
    private readonly ScaleTransform _stageScale = new(DefaultStageScale, DefaultStageScale);

    /// <summary>지금 그림 목록에 거는 배율. 제목 옆 ＋ － 로 굴린다.</summary>
    private double _bookScale = DefaultBookScale;

    /// <summary>제목마다의 배율 알림 — 굴릴 때 같이 고쳐 준다.</summary>
    private readonly List<TextBlock> _scaleShown = [];

    private FighterSprites? _art;
    private DuelArt? _board;

    /// <summary>푼 장을 담아 둔다 — 돌릴 때마다 다시 짜면 눈에 띄게 끊긴다.</summary>
    private readonly Dictionary<(int Set, int Frame), BitmapSource> _kept = [];

    /// <summary>번호 칸을 우리가 고쳐 넣는 동안인지 — 되울림을 막는다.</summary>
    private bool _syncing;

    public MotionMakerDialog()
    {
        Title = "모션 메이커 — 일기토";
        Width = 1180;
        Height = 900;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        Content = Page();

        foreach (var lane in Lanes())
        {
            var it = lane;
            it.Clock.Tick += (_, _) => Advance(it);
            it.Numbers.KeyDown += (_, e) => { if (e.Key == Key.Enter) Parse(it); };
            it.Numbers.LostFocus += (_, _) => Parse(it);
        }

        Loaded += (_, _) => Load();
        Closed += (_, _) => Stop();
    }

    private Lane[] Lanes() => [_mine, _foe];

    // ── 창 짜기 ─────────────────────────────────────────────────────────────

    private UIElement Page()
    {
        var page = new DockPanel();

        var bar = Bar();
        DockPanel.SetDock(bar, Dock.Top);
        page.Children.Add(bar);

        DockPanel.SetDock(_status, Dock.Bottom);
        page.Children.Add(_status);

        var body = new Grid();
        body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        body.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var preview = Preview();
        Grid.SetRow(preview, 0);
        body.Children.Add(preview);

        var lanes = Split(Side(_foe), Side(_mine));
        Grid.SetRow(lanes, 1);
        body.Children.Add(lanes);

        page.Children.Add(body);
        return page;
    }

    /// <summary>
    /// 한쪽 — 차례 줄을 위에, 그림 목록을 그 아래에 둔다.
    /// </summary>
    /// <remarks>
    /// 두 쪽을 <b>줄이 아니라 칸으로</b> 나눈 까닭이 여기 있다. 차례 줄과 그림 목록을
    /// 각각 좌우로 갈라 두면 칸막이가 둘이 되어 따로 놀지만, 한쪽을 통째로 한 칸에
    /// 담으면 칸막이 하나가 둘을 함께 늘이고 줄인다.
    /// </remarks>
    private UIElement Side(Lane lane)
    {
        var box = new DockPanel();
        var strip = Strip(lane);
        DockPanel.SetDock(strip, Dock.Top);
        box.Children.Add(strip);
        box.Children.Add(Book(lane));
        return box;
    }

    /// <summary>한 칸이 이보다 좁아지지는 않는다.</summary>
    private const double MinSideWidth = 180;

    /// <summary>
    /// 둘을 나란히 놓고 <b>가운데에 칸막이</b>를 세운다 — 왼쪽이 적군, 오른쪽이 아군이다.
    /// </summary>
    /// <remarks>
    /// 판에 선 자리와 같은 차례다. 적군이 왼편(60)에, 아군이 오른편(173)에 서므로
    /// 편집 칸도 그대로 놓아야 어느 쪽을 만지고 있는지 눈이 안 헷갈린다.
    ///
    /// 칸막이는 <b>끌어서</b> 좌우 몫을 바꾼다. 그림을 크게 키워 놓으면 한 줄에 몇 장
    /// 안 들어가므로, 지금 만지는 쪽에 자리를 몰아 줄 데가 있어야 한다. 어느 쪽도
    /// <see cref="MinSideWidth"/> 밑으로는 안 줄어든다 — 아주 접히면 되돌리기 어렵다.
    /// </remarks>
    private static UIElement Split(UIElement left, UIElement right)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition
        {
            Width = new GridLength(1, GridUnitType.Star),
            MinWidth = MinSideWidth,
        });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition
        {
            Width = new GridLength(1, GridUnitType.Star),
            MinWidth = MinSideWidth,
        });

        var bar = new GridSplitter
        {
            Width = 6,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            Background = Brushes.LightGray,
            ResizeBehavior = GridResizeBehavior.PreviousAndNext,
            ResizeDirection = GridResizeDirection.Columns,
            ShowsPreview = false,
            Cursor = Cursors.SizeWE,
            ToolTip = "끌어서 좌우 몫을 바꾼다",
        };

        Grid.SetColumn(left, 0);
        Grid.SetColumn(bar, 1);
        Grid.SetColumn(right, 2);
        grid.Children.Add(left);
        grid.Children.Add(bar);
        grid.Children.Add(right);
        return grid;
    }

    /// <summary>위 줄 — 스프라이트셋 · 배경 · 배율과 재생 단추들.</summary>
    private UIElement Bar()
    {
        foreach (string name in FoeSetNames) _foeSet.Items.Add(name);
        _foeSet.SelectedIndex = 0;
        _foeSet.SelectionChanged += (_, _) =>
        {
            _foe.Set = _foeSet.SelectedIndex + 1;
            FillBook(_foe);
            FillStrip(_foe);
            Draw(_foe);
            Tell();
        };

        foreach (var (_, name) in Arenas) _arena.Items.Add(name);
        _arena.SelectedIndex = 0;
        _arena.SelectionChanged += (_, _) => Ground();

        var smaller = Push("－", () => StageZoom(-StageScaleStep), 8);
        smaller.ToolTip = "판을 줄인다";
        var bigger = Push("＋", () => StageZoom(+StageScaleStep), 8);
        bigger.ToolTip = "판을 키운다 — 판 위에서 휠을 굴려도 된다";
        foreach (var button in new[] { smaller, bigger })
        {
            button.Width = 26;
            button.FontSize = 13;
        }
        _stageShown.Text = Percent(DefaultStageScale);

        var back = Push("◀", () => StepBy(-1), 12);
        back.ToolTip = "한 장 뒤로";
        var next = Push("▶", () => StepBy(+1), 12);
        next.ToolTip = "한 장 앞으로";

        _playPause = Push(PlayText, Toggle, 14);
        _playPause.MinWidth = 96;
        _playPause.ToolTip = "돌리다 멈추면 그 자리에서 다시 이어 돈다";

        // 장마다 따로 맞춰 둔 것을 죄다 지금 간격으로 되돌린다.
        var all = Push("모두 이 간격으로", () =>
        {
            foreach (var lane in Lanes())
            {
                foreach (var step in lane.Steps) step.Seconds = _every.Value;
                FillStrip(lane);
            }
            Tell();
        }, 10);

        var bar = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(12, 10, 12, 6),
        };
        bar.Children.Add(Label("적군 스프라이트셋", 108));
        bar.Children.Add(_foeSet);
        bar.Children.Add(Label("배경", 44));
        bar.Children.Add(_arena);
        bar.Children.Add(Label("판", 32));
        bar.Children.Add(smaller);
        bar.Children.Add(_stageShown);
        bar.Children.Add(bigger);
        bar.Children.Add(_gridOn);
        bar.Children.Add(Label("한 장", 48));
        bar.Children.Add(_every);
        bar.Children.Add(new TextBlock
        {
            Text = "초",
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(4, 0, 14, 0),
        });
        bar.Children.Add(back);
        bar.Children.Add(_playPause);
        bar.Children.Add(next);
        bar.Children.Add(_loop);
        bar.Children.Add(all);

        var rows = new StackPanel();
        rows.Children.Add(bar);
        rows.Children.Add(MotionRow());
        rows.Children.Add(SmoothRow());
        return rows;
    }

    /// <summary>
    /// 판을 부드럽게 보는 줄 — 그림 자체는 안 건드리고 <b>보이는 결만</b> 바꾼다.
    /// </summary>
    /// <remarks>
    /// 원본은 점그림이라 밑값은 죄다 꺼 둔다. 켜고 끄며 견주어 보라고 낸 손잡이다.
    /// 저장하는 값(장 · 초 · 점)과는 아무 상관이 없다.
    /// </remarks>
    private UIElement SmoothRow()
    {
        foreach (var (name, _) in Scalings) _smoothScale.Items.Add(name);
        _smoothScale.SelectedIndex = 0;
        _smoothScale.SelectionChanged += (_, _) => Smooth();

        _over.ValueChanged += (_, _) => Smooth();
        _scan.Checked += (_, _) => Smooth();
        _scan.Unchecked += (_, _) => Smooth();
        _scanDeep.ValueChanged += (_, _) => Smooth();

        _smoothEdge.Checked += (_, _) => Smooth();
        _smoothEdge.Unchecked += (_, _) => Smooth();
        _blur.ValueChanged += (_, _) => Smooth();

        var plain = Push("원래대로", () =>
        {
            _smoothScale.SelectedIndex = 0;
            _smoothEdge.IsChecked = false;
            _blur.Value = 0;
            _over.Value = 1;
            _scan.IsChecked = false;
            Smooth();
        }, 10);
        plain.ToolTip = "셋을 다 끄고 점그림 그대로 본다";

        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(12, 0, 12, 8),
        };
        row.Children.Add(Label("늘이기", 62));
        row.Children.Add(_smoothScale);
        row.Children.Add(_smoothEdge);
        row.Children.Add(Label("흐림", 40));
        row.Children.Add(_blur);
        row.Children.Add(Label("겹쳐", 40));
        row.Children.Add(_over);
        row.Children.Add(_scan);
        row.Children.Add(_scanDeep);
        row.Children.Add(new TextBlock { Width = 8 });
        row.Children.Add(plain);
        return row;
    }

    /// <summary>고른 결을 판에 얹는다.</summary>
    private void Smooth()
    {
        var mode = Scalings[Math.Clamp(_smoothScale.SelectedIndex, 0, Scalings.Length - 1)].Mode;
        var edge = _smoothEdge.IsChecked == true ? EdgeMode.Unspecified : EdgeMode.Aliased;

        foreach (var image in new[] { _ground, _mine.View, _foe.View })
        {
            RenderOptions.SetBitmapScalingMode(image, mode);
            RenderOptions.SetEdgeMode(image, edge);
        }

        // 흐림은 판 전체에 한 번만 얹는다 — 사람과 배경이 따로 놀면 어색하다.
        _stage.Effect = _blur.Value > 0.05
            ? new BlurEffect { Radius = _blur.Value, KernelType = KernelType.Gaussian }
            : null;

        // 겹쳐 그리기 — 크게 그렸다 줄여 앉힌다. 1 이면 안 쓴다.
        _stage.CacheMode = _over.Value > 1.05
            ? new BitmapCache { RenderAtScale = _over.Value, SnapsToDevicePixels = false }
            : null;

        _scanLines.Visibility = _scan.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        _scanLines.Opacity = _scanDeep.Value;
    }

    /// <summary>주사선을 한 번 깔아 둔다 — 켜고 끄는 것은 보임만 바꾼다.</summary>
    private void BuildScanLines()
    {
        for (int y = 0; y < DuelArt.ArenaHeight; y += 2)
            _scanLines.Children.Add(new Line
            {
                X1 = 0, X2 = DuelArt.ArenaWidth,
                Y1 = y + 0.5, Y2 = y + 0.5,
                Stroke = Brushes.Black,
                StrokeThickness = 1,
            });
    }

    /// <summary>적어 둔 모션을 골라 넣고, 고친 것을 되적는 줄.</summary>
    private UIElement MotionRow()
    {
        foreach (var box in new[] { _foeMotion, _myMotion })
        {
            foreach (var motion in DuelMotions.All) box.Items.Add(motion.Name);
            box.SelectedIndex = -1;                     // 그림을 읽고 나서 밑값을 넣는다
        }

        // 고르는 그 자리에서 든다 — 「넣기」 단추가 따로 없다.
        _foeMotion.SelectionChanged += (_, _) => Chose(_foe, _foeMotion);
        _myMotion.SelectionChanged += (_, _) => Chose(_mine, _myMotion);

        // 공격과 막기는 짝이라 한꺼번에 세워 보는 일이 잦다. 아군 것을 고른 뒤 누르면
        // 적군에 그것을 받는 몸짓이 선다.
        var pair = Push("맞세우기", Face, 10);
        pair.ToolTip = "아군 것에 맞춰 적군을 세운다"
                     + " — 아군이 상단 공격이면 적군은 웅크린다, 뛴다면 적군은 하단 공격이다";

        // 고친 차례를 그 모션으로 되적는다 — 놀이가 이 파일을 읽는다.
        var keep = Push("저장", Keep, 14);
        keep.ToolTip = "마지막으로 만진 쪽의 차례를 고른 모션으로 적어 둔다."
                     + " 놀이의 일기토가 이 파일을 읽으므로 다시 굽지 않아도 든다";

        var back = Push("되돌리기", Undo, 10);
        back.ToolTip = "적어 둔 파일을 지우고 게임 표에서 짚은 밑값으로 돌린다";

        // 차례 전체의 점과 초를 한꺼번에 민다 — 장마다 스피너를 굴리지 않아도 된다.
        var less = Push("－", () => ShiftPush(-PushStep), 8);
        less.ToolTip = "이 차례의 점을 죄다 5 줄인다 (뒤로 민다)";
        var more = Push("＋", () => ShiftPush(+PushStep), 8);
        more.ToolTip = "이 차례의 점을 죄다 5 늘린다 (앞으로 민다)";

        var slower = Push("－", () => ShiftSeconds(-SecondsStep), 8);
        slower.ToolTip = "이 차례의 초를 죄다 0.01 줄인다 (빨라진다)";
        var faster = Push("＋", () => ShiftSeconds(+SecondsStep), 8);
        faster.ToolTip = "이 차례의 초를 죄다 0.01 늘린다 (느려진다)";

        foreach (var one in new[] { less, more, slower, faster })
        {
            one.Width = 26;
            one.FontSize = 13;
        }

        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(12, 0, 12, 8),
        };
        row.Children.Add(Label("적군 모션", 74));
        row.Children.Add(_foeMotion);
        row.Children.Add(Label("아군 모션", 74));
        row.Children.Add(_myMotion);
        row.Children.Add(new TextBlock { Width = 8 });
        row.Children.Add(pair);
        row.Children.Add(new TextBlock { Width = 8 });
        row.Children.Add(keep);
        row.Children.Add(back);
        row.Children.Add(Label("점 모두", 62));
        row.Children.Add(less);
        row.Children.Add(more);
        row.Children.Add(Label("초 모두", 62));
        row.Children.Add(slower);
        row.Children.Add(faster);
        return row;
    }

    /// <summary>그 상자에서 고른 모션을 그쪽 차례로 옮긴다.</summary>
    private void Chose(Lane lane, ComboBox box)
    {
        if (box.SelectedIndex < 0 || box.SelectedIndex >= DuelMotions.All.Count) return;

        var motion = DuelMotions.All[box.SelectedIndex];
        Fill(lane, motion);
        _status.Text = $"{lane.Name} 에 「{motion.Name}」 {motion.Steps.Length}장을 넣었습니다"
                     + $" — 모두 {motion.Length:0.00}초.";
    }

    /// <summary>차례를 통째로 갈아 끼운다.</summary>
    private void Fill(Lane lane, DuelMotions.Motion motion)
    {
        Pause();
        lane.Steps.Clear();
        foreach (var step in motion.Steps)
            lane.Steps.Add(new Step { Frame = step.Frame, Seconds = step.Seconds, Push = step.Push });

        lane.Picked = 0;
        _touched = lane;
        Sync(lane);
    }

    /// <summary>초를 한꺼번에 굴리는 폭 — 눈금이 0.067초라 잘게 잡는다.</summary>
    private const double SecondsStep = 0.01;

    /// <summary>
    /// 마지막으로 만진 쪽 차례의 초를 <b>죄다</b> 그만큼 민다.
    /// </summary>
    /// <remarks>
    /// 한 장씩 굴리는 스피너는 0.1 폭이라 눈금 값(0.07 · 0.13 …)을 손보기 뻑뻑하다.
    /// 여기서는 0.01 씩 밀어 판 전체를 빠르게·느리게 한다.
    /// </remarks>
    private void ShiftSeconds(double way)
    {
        var lane = _touched ?? _mine;
        if (lane.Steps.Count == 0)
        {
            _status.Text = $"{lane.Name} 차례가 비어 있습니다.";
            return;
        }

        foreach (var step in lane.Steps)
            step.Seconds = Math.Clamp(step.Seconds + way, MinSeconds, MaxSeconds);

        FillStrip(lane);
        Tell();
        _status.Text = $"{lane.Name} 차례의 초를 죄다 {way:+0.00;-0.00} 했습니다"
                     + $" — 모두 {lane.Steps.Sum(s => s.Seconds):0.00}초입니다.";
    }

    /// <summary>
    /// 마지막으로 만진 쪽 차례의 점을 <b>죄다</b> 그만큼 민다.
    /// </summary>
    /// <remarks>
    /// 다가오기처럼 열일곱 장이 죽 이어진 것은 장마다 스피너를 굴리기가 성가시다.
    /// 통째로 밀면 걸음 폭은 그대로 두고 자리만 옮길 수 있다. 한계(±150)에 닿은 장은
    /// 거기서 멎으므로, 끝까지 민 뒤에는 걸음 폭이 달라질 수 있다.
    /// </remarks>
    private void ShiftPush(double way)
    {
        var lane = _touched ?? _mine;
        if (lane.Steps.Count == 0)
        {
            _status.Text = $"{lane.Name} 차례가 비어 있습니다.";
            return;
        }

        foreach (var step in lane.Steps)
            step.Push = Math.Clamp(step.Push + way, -MaxPush, MaxPush);

        FillStrip(lane);
        Draw(lane);
        Tell();
        _status.Text = $"{lane.Name} 차례의 점을 죄다 {way:+0;-0} 했습니다"
                     + $" — 지금 {lane.Steps[0].Push:0} 에서 {lane.Steps[^1].Push:0} 까지입니다.";
    }

    /// <summary>
    /// 마지막으로 만진 쪽의 차례를 고른 모션으로 적어 둔다.
    /// </summary>
    /// <remarks>
    /// 적히는 곳은 <c>asset/duel/motion.json</c> 이고 <b>놀이가 그 파일을 읽는다</b>
    /// (<see cref="DuelMotions"/>). 곧 여기서 고쳐 저장하면 일기토가 그대로 돈다 —
    /// 다시 굽지 않아도 된다.
    /// </remarks>
    private void Keep()
    {
        var lane = _touched ?? _mine;
        if (lane.Steps.Count == 0)
        {
            _status.Text = $"{lane.Name} 차례가 비어 있어 적을 것이 없습니다.";
            return;
        }

        var box = lane == _foe ? _foeMotion : _myMotion;
        if (box.SelectedIndex < 0)
        {
            _status.Text = $"{lane.Name} 쪽에 고른 모션이 없습니다.";
            return;
        }
        var motion = DuelMotions.All[box.SelectedIndex];
        var made = new DuelMotions.Motion(motion.Key, motion.Name,
            [.. lane.Steps.Select(s => new DuelMotions.Step(s.Frame, s.Seconds, s.Push))]);

        var table = DuelMotions.All.Select(m => m.Key == made.Key ? made : m);
        if (!DuelMotions.Save(table))
        {
            _status.Text = $"적지 못했습니다 — {DuelMotions.LastError}";
            return;
        }

        // <b>꼬리를 잘못 두면 판마다 사이가 달라진다.</b> 마지막 장의 점이 곧 판이 끝날 때
        // 담기는 거리라, 두 쪽이 짝을 이루지 않으면 판을 거듭할수록 붙거나 벌어진다.
        double tail = made.Steps[^1].Push;
        string warn = Math.Abs(tail) < 0.5 || Math.Abs(Math.Abs(tail) - DuelMotions.Drift) < 0.5
            ? ""
            : $"   ※ 마지막 장의 점이 {tail:0} 입니다 — 0 이나 ±{DuelMotions.Drift:0} 이라야"
              + " 판마다 두 사람 사이가 그대로입니다.";

        _status.Text = $"{lane.Name} 차례를 「{made.Name}」 으로 적었습니다"
                     + $" ({made.Steps.Length}장 · {made.Length:0.00}초) — {DuelMotions.Path_()}."
                     + "  놀이의 일기토가 이것을 읽습니다." + warn;
    }

    /// <summary>적어 둔 파일을 걷고 밑값으로 돌린다.</summary>
    private void Undo()
    {
        string path = DuelMotions.Path_();
        if (!File.Exists(path))
        {
            _status.Text = "적어 둔 파일이 없습니다 — 이미 밑값입니다.";
            return;
        }
        if (MessageBox.Show(this, $"{path} 를 지우고 게임 표에서 짚은 밑값으로 돌립니다.",
                            "되돌리기", MessageBoxButton.OKCancel, MessageBoxImage.Warning)
            != MessageBoxResult.OK) return;

        try
        {
            File.Delete(path);
            DuelMotions.Forget();
            _status.Text = "밑값으로 돌렸습니다. 창을 다시 열면 목록도 밑값이 됩니다.";
        }
        catch (Exception e)
        {
            _status.Text = $"지우지 못했습니다 — {e.Message}";
        }
    }

    /// <summary>
    /// 고른 것이 공격이면 아군에 넣고 적군에는 <b>그 부위를 막는 몸짓</b>을 세운다.
    /// </summary>
    /// <remarks>
    /// 상단을 막는 것이 웅크림, 하단을 막는 것이 도약이다 — 노리는 자리와 피하는 쪽이
    /// 반대라 <c>상 ↔ 하</c> 로 엇갈린다. 중단은 피함이다.
    /// </remarks>
    private void Face()
    {
        if (_myMotion.SelectedIndex < 0 || _myMotion.SelectedIndex >= DuelMotions.All.Count) return;
        var motion = DuelMotions.All[_myMotion.SelectedIndex];

        // 찌르기 셋과 막기 셋이 짝이다 — 공격 a 는 막기 2−a 가 막는다(Duel.cs).
        int line = Array.FindIndex(Lines, k => k == motion.Key);
        int guard = Array.FindIndex(Blocks, k => k == motion.Key);

        DuelMotions.Motion? foe = null;
        if (line >= 0) foe = DuelMotions.Find(DuelMotions.GuardKey(2 - line % 3, -1));
        else if (guard >= 0) foe = DuelMotions.Find(DuelMotions.ThrustKey(2 - guard, +1));

        if (foe == null)
        {
            _status.Text = $"「{motion.Name}」 은 맞세울 짝이 없습니다 — 아군에 공격이나 막기를 고르십시오.";
            return;
        }

        // 상자를 옮기면 고르기 되울림으로 차례가 든다.
        int at = 0;
        for (int i = 0; i < DuelMotions.All.Count; i++)
            if (DuelMotions.All[i].Key == foe.Key) { at = i; break; }
        _foeMotion.SelectedIndex = at;

        _status.Text = $"아군 「{motion.Name}」 · 적군 「{foe.Name}」 을 맞세웠습니다."
                     + "  재생을 누르면 두 쪽이 함께 돕니다.";
    }

    private static Button Push(string text, Action click, double pad)
    {
        var button = new Button
        {
            Content = text,
            Padding = new Thickness(pad, 3, pad, 3),
            Margin = new Thickness(0, 0, 6, 0),
        };
        button.Click += (_, _) => click();
        return button;
    }

    private static TextBlock Label(string text, double width = 60) => new()
    {
        Text = text,
        Width = width,
        VerticalAlignment = VerticalAlignment.Center,
        Margin = new Thickness(10, 0, 4, 0),
    };

    /// <summary>미리보기 — 배경 그림 위에 두 사람을 얹는다.</summary>
    private UIElement Preview()
    {
        Crisp(_ground);
        _ground.Width = DuelArt.ArenaWidth;
        _ground.Height = DuelArt.ArenaHeight;
        Canvas.SetLeft(_ground, 0);
        Canvas.SetTop(_ground, 0);
        _stage.Children.Add(_ground);

        foreach (var lane in Lanes())
        {
            var view = lane.View;
            view.Width = FighterSprites.Width;
            view.Height = FighterSprites.Height;
            Crisp(view);
            Canvas.SetTop(view, DuelArt.ArenaHeight - FighterSprites.Height);
            Canvas.SetLeft(view, lane.Forward ? FoeStand : MyStand);
            _stage.Children.Add(view);
        }

        BuildGrid();
        BuildScanLines();
        _stage.Children.Add(_scanLines);
        _stage.Children.Add(_grid);

        _gridOn.Checked += (_, _) => _grid.Visibility = Visibility.Visible;
        _gridOn.Unchecked += (_, _) => _grid.Visibility = Visibility.Collapsed;

        _stage.LayoutTransform = _stageScale;

        var box = new Border
        {
            Background = Brushes.Black,
            BorderBrush = Brushes.Gray,
            BorderThickness = new Thickness(1),
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 4, 0, 8),
            Child = _stage,
        };

        // 판 위에서 휠을 굴리면 키우고 줄인다 — 단추까지 손을 옮기지 않아도 된다.
        box.MouseWheel += (_, e) =>
        {
            StageZoom(e.Delta > 0 ? StageScaleStep : -StageScaleStep);
            e.Handled = true;
        };
        return box;
    }

    /// <summary>
    /// 격자를 한 번 짜 둔다 — 켜고 끄는 것은 보임만 바꾼다.
    /// </summary>
    private void BuildGrid()
    {
        for (int x = 0; x <= DuelArt.ArenaWidth; x += 10)
        {
            bool big = x % 50 == 0;
            _grid.Children.Add(new Line
            {
                X1 = x, X2 = x, Y1 = 0, Y2 = DuelArt.ArenaHeight,
                Stroke = Brushes.White,
                StrokeThickness = big ? 0.7 : 0.3,
                Opacity = big ? 0.55 : 0.25,
            });

            if (!big) continue;

            var mark = new TextBlock
            {
                Text = x.ToString(),
                FontSize = 7,
                Foreground = Brushes.White,
                Opacity = 0.7,
            };
            Canvas.SetLeft(mark, x + 1);
            Canvas.SetTop(mark, 1);
            _grid.Children.Add(mark);
        }

        // 판 한가운데.
        _grid.Children.Add(new Line
        {
            X1 = DuelArt.ArenaWidth / 2.0, X2 = DuelArt.ArenaWidth / 2.0,
            Y1 = 0, Y2 = DuelArt.ArenaHeight,
            Stroke = Brushes.Lime,
            StrokeThickness = 0.9,
            Opacity = 0.85,
        });

        var middle = new TextBlock
        {
            Text = "192",
            FontSize = 8,
            FontWeight = FontWeights.Bold,
            Foreground = Brushes.Lime,
        };
        Canvas.SetLeft(middle, DuelArt.ArenaWidth / 2.0 + 1);
        Canvas.SetTop(middle, 10);
        _grid.Children.Add(middle);

        // 두 사람이 다 모여 섰을 때 <b>발이 닿는 자리</b>다 — 조각 왼끝이 아니라 발이라야
        // 눈에 맞는다. 조각 안에서 발 가운데가 제독은 86.5, 상대는 76.0 이다(여느 첫 장을 재었다).
        Stand(FoeStand + FoeFeet, Brushes.Aqua, "적");
        Stand(MyStand + MyFeet, Brushes.Orange, "아");

        void Stand(double at, Brush color, string name)
        {
            _grid.Children.Add(new Line
            {
                X1 = at, X2 = at, Y1 = 0, Y2 = DuelArt.ArenaHeight,
                Stroke = color,
                StrokeThickness = 0.8,
                Opacity = 0.8,
            });

            var mark = new TextBlock
            {
                Text = name,
                FontSize = 8,
                FontWeight = FontWeights.Bold,
                Foreground = color,
            };
            Canvas.SetLeft(mark, at + 1);
            Canvas.SetTop(mark, DuelArt.ArenaHeight - 12);
            _grid.Children.Add(mark);
        }
    }

    /// <summary>차례 줄 — 적은 번호와 장마다의 시간이 여기 선다.</summary>
    private UIElement Strip(Lane lane)
    {
        lane.Head.FontWeight = FontWeights.Bold;
        lane.Head.Margin = new Thickness(0, 0, 0, 4);

        lane.Numbers.Height = 24;
        lane.Numbers.VerticalContentAlignment = VerticalAlignment.Center;
        lane.Numbers.ToolTip = "장 번호를 쉼표나 빈칸으로 나눠 적는다 — 보기: 24 25 26 27";

        var put = new Button { Content = "넣기", Padding = new Thickness(10, 2, 10, 2), Margin = new Thickness(6, 0, 0, 0) };
        put.Click += (_, _) => Parse(lane);

        var clear = new Button { Content = "비우기", Padding = new Thickness(10, 2, 10, 2), Margin = new Thickness(4, 0, 0, 0) };
        clear.Click += (_, _) => { lane.Steps.Clear(); lane.Picked = -1; Sync(lane); };

        var line = new DockPanel { Margin = new Thickness(0, 0, 0, 4) };
        DockPanel.SetDock(clear, Dock.Right);
        DockPanel.SetDock(put, Dock.Right);
        line.Children.Add(clear);
        line.Children.Add(put);
        line.Children.Add(lane.Numbers);

        lane.Strip.Orientation = Orientation.Horizontal;

        // 높이를 <b>박지 않는다</b>. 칸에 스피너가 둘로 늘면서 136점으로는 아래가 잘리고,
        // 가로 스크롤바까지 그 위에 얹혀 「점」 칸이 가려졌다. 칸이 자란 만큼 줄도 자라게 둔다.
        var scroll = new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content = lane.Strip,
        };

        var box = new StackPanel { Margin = new Thickness(12, 0, 12, 6) };
        box.Children.Add(lane.Head);
        box.Children.Add(line);
        box.Children.Add(scroll);
        return box;
    }

    /// <summary>
    /// 그림 목록 — 서른세 장을 번호와 함께 늘어놓는다. 누르면 차례 끝에 붙는다.
    /// </summary>
    /// <remarks>
    /// 제목 오른쪽의 <b>＋ －</b> 가 그림 크기를 굴린다. 반으로 줄여 놓으면 서른세 장이
    /// 한눈에 들어오지만 칼끝이 어디로 가는지가 안 보여, 크게 볼 데가 있어야 한다.
    /// 배율은 <b>두 쪽이 같이 쓴다</b> — 아군과 적군을 같은 크기로 맞대야 겨룸이 보인다.
    /// </remarks>
    private UIElement Book(Lane lane)
    {
        lane.Book.Margin = new Thickness(2);

        var head = new TextBlock
        {
            Text = lane.Name + " 그림 — 누르면 차례 끝에 붙는다",
            FontWeight = FontWeights.Bold,
            VerticalAlignment = VerticalAlignment.Center,
        };

        var shown = new TextBlock
        {
            Text = Percent(_bookScale),
            Width = 46,
            TextAlignment = TextAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = Brushes.Gray,
        };
        _scaleShown.Add(shown);

        var less = Tiny("－", () => Zoom(-BookScaleStep), "그림을 줄인다");
        var more = Tiny("＋", () => Zoom(+BookScaleStep), "그림을 키운다");
        foreach (var button in new[] { less, more })
        {
            button.Width = 26;
            button.FontSize = 13;
            button.Margin = new Thickness(2, 0, 0, 0);
        }

        var line = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 0, 0, 4),
            Children = { head, less, shown, more },
        };

        var scroll = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = lane.Book,
        };

        var box = new DockPanel { Margin = new Thickness(12, 0, 12, 6) };
        DockPanel.SetDock(line, Dock.Top);
        box.Children.Add(line);
        box.Children.Add(scroll);
        return box;
    }

    /// <summary>미리보기 판을 키우고 줄인다.</summary>
    private void StageZoom(double way)
    {
        double want = Math.Clamp(_stageScale.ScaleX + way, MinStageScale, MaxStageScale);
        if (Math.Abs(want - _stageScale.ScaleX) < 0.001) return;

        _stageScale.ScaleX = _stageScale.ScaleY = want;
        _stageShown.Text = Percent(want);
    }

    /// <summary>「100%」 꼴로.</summary>
    private static string Percent(double scale) => $"{(int)Math.Round(scale * 100)}%";

    /// <summary>그림 목록 배율을 굴린다 — 두 쪽을 함께 다시 편다.</summary>
    private void Zoom(double way)
    {
        double want = Math.Clamp(_bookScale + way, MinBookScale, MaxBookScale);
        if (Math.Abs(want - _bookScale) < 0.001) return;

        _bookScale = want;
        foreach (var shown in _scaleShown) shown.Text = Percent(_bookScale);
        foreach (var lane in Lanes()) FillBook(lane);
    }

    // ── 그림 읽기 ───────────────────────────────────────────────────────────

    /// <summary>세이브를 연 자리에서 게임 폴더를 찾아 <c>FIGHTER.CDS</c> 를 연다.</summary>
    private void Load()
    {
        string dir = Path.GetDirectoryName(AppSettings.LastSaveFilePath) ?? "";
        _art = FighterSprites.Open(dir);
        _board = DuelArt.Open();

        if (_art == null)
        {
            _status.Text = $"일기토 그림을 못 읽었습니다 — {FighterSprites.LastError}";
            return;
        }

        Ground();
        foreach (var lane in Lanes())
        {
            FillBook(lane);
            Draw(lane);
        }
        Tell();

        // 밑값은 <b>다가오기</b>다 — 창을 열면 두 사람이 걸어 나오는 것부터 보인다.
        int walk = 0;
        for (int i = 0; i < DuelMotions.All.Count; i++)
            if (DuelMotions.All[i].Key == DuelMotions.Walk) { walk = i; break; }

        _foeMotion.SelectedIndex = walk;
        _myMotion.SelectedIndex = walk;
    }

    /// <summary>고른 배경 그림을 판 밑에 깐다. 못 찾으면 까만 판이다.</summary>
    private void Ground()
    {
        string key = Arenas[Math.Max(0, _arena.SelectedIndex)].Key;
        string? path = _board?.Path_(key);
        _ground.Source = path == null ? null : new BitmapImage(new Uri(path, UriKind.RelativeOrAbsolute));
    }

    /// <summary>그 스프라이트셋의 그 장. 한 번 푼 것은 담아 둔다.</summary>
    private BitmapSource? Bitmap(int set, int frame)
    {
        if (_art is not { } art) return null;
        if (_kept.TryGetValue((set, frame), out var kept)) return kept;

        var px = art.TryGetBgra(set, frame);
        if (px == null) return null;

        var bmp = BitmapSource.Create(FighterSprites.Width, FighterSprites.Height, 96, 96,
                                      PixelFormats.Bgra32, null, px, FighterSprites.Width * 4);
        bmp.Freeze();
        _kept[(set, frame)] = bmp;
        return bmp;
    }

    /// <summary>그 장이 어느 몸짓의 몇째 장인지 — 「12 도약0」 꼴로 적는다.</summary>
    private static string NameOf(int frame)
    {
        for (int at = FighterSprites.Starts.Length - 1; at >= 0; at--)
            if (frame >= FighterSprites.Starts[at])
                return $"{frame} {MoveNames[at]}{frame - FighterSprites.Starts[at]}";
        return frame.ToString();
    }

    private static void Crisp(Image image)
    {
        RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.NearestNeighbor);
        RenderOptions.SetEdgeMode(image, EdgeMode.Aliased);
    }

    // ── 목록과 차례 ─────────────────────────────────────────────────────────

    /// <summary>그림 목록을 다시 편다 — 적군 스프라이트셋을 바꾸면 다시 부른다.</summary>
    private void FillBook(Lane lane)
    {
        lane.Book.Children.Clear();
        for (int frame = 0; frame < FighterSprites.Frames; frame++)
        {
            var bmp = Bitmap(lane.Set, frame);
            if (bmp == null) continue;

            var image = new Image
            {
                Source = bmp,
                Width = FighterSprites.Width * _bookScale,
                Height = FighterSprites.Height * _bookScale,
            };
            Crisp(image);

            var cell = new StackPanel { Margin = new Thickness(3) };
            cell.Children.Add(image);
            cell.Children.Add(new TextBlock
            {
                Text = NameOf(frame),
                HorizontalAlignment = HorizontalAlignment.Center,
                FontSize = 11,
            });

            int at = frame;
            var button = new Button
            {
                Content = cell,
                Padding = new Thickness(0),
                Background = Brushes.Transparent,
                ToolTip = $"{lane.Name} 차례 끝에 {frame}번을 붙인다",
            };
            button.Click += (_, _) =>
            {
                lane.Steps.Add(new Step { Frame = at, Seconds = _every.Value });
                lane.Picked = lane.Steps.Count - 1;      // 방금 붙인 장을 골라 둔다
                Sync(lane);
            };
            lane.Book.Children.Add(button);
        }
    }

    /// <summary>적어 넣은 번호를 차례로 옮긴다. 없는 번호는 버리고 알린다.</summary>
    private void Parse(Lane lane)
    {
        if (_syncing) return;

        var made = new List<Step>();
        int dropped = 0;
        foreach (string piece in lane.Numbers.Text.Split(
                     [',', ' ', '\t', '\n', '\r', ';'], StringSplitOptions.RemoveEmptyEntries))
        {
            if (!int.TryParse(piece, out int frame) || frame < 0 || frame >= FighterSprites.Frames)
            {
                dropped++;
                continue;
            }

            // 그 자리에 있던 장이 그대로면 시간도 그대로 둔다 — 번호를 고칠 때마다 맞춰
            // 둔 간격이 날아가면 손으로 다듬은 것이 헛일이 된다.
            int seat = made.Count;
            bool same = seat < lane.Steps.Count && lane.Steps[seat].Frame == frame;
            made.Add(new Step
            {
                Frame = frame,
                Seconds = same ? lane.Steps[seat].Seconds : _every.Value,
                Push = same ? lane.Steps[seat].Push : 0,
            });
        }

        lane.Steps.Clear();
        lane.Steps.AddRange(made);
        lane.Picked = -1;
        _touched = lane;
        Sync(lane);

        if (dropped > 0)
            _status.Text = $"{lane.Name} — 0~{FighterSprites.Frames - 1} 밖의 수 {dropped}개는 버렸습니다";
    }

    /// <summary>차례가 바뀐 뒤에 번호 칸 · 차례 줄 · 미리보기를 한꺼번에 맞춘다.</summary>
    private void Sync(Lane lane)
    {
        _syncing = true;
        lane.Numbers.Text = string.Join(" ", lane.Steps.Select(s => s.Frame));
        _syncing = false;

        // 고른 자리가 차례 밖으로 밀렸으면 끝자리로 당긴다.
        if (lane.Picked >= lane.Steps.Count) lane.Picked = lane.Steps.Count - 1;

        FillStrip(lane);
        lane.At = Math.Max(0, lane.Picked);
        Draw(lane);
        Tell();
        Follow(lane);
    }

    /// <summary>고른 칸이 줄 밖으로 나갔으면 굴려서 보이게 한다.</summary>
    private void Follow(Lane lane)
    {
        if (lane.Picked < 0 || lane.Picked >= lane.Cells.Count) return;

        var cell = lane.Cells[lane.Picked];
        // 칸을 방금 새로 짰으므로 자리가 잡힌 뒤라야 굴릴 데를 안다. 손끝도 새 칸으로
        // 옮겨 준다 — 옛 칸은 이미 헐렸으므로 그냥 두면 좌우 글쇠가 갈 데를 잃는다.
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =>
        {
            cell.BringIntoView();
            cell.Focus();
        });
    }

    /// <summary>그 자리를 고른다 — 판에도 그 장을 걸어 눈으로 맞대게 한다.</summary>
    private void Pick(Lane lane, int seat)
    {
        if (seat < 0 || seat >= lane.Steps.Count) return;

        _touched = lane;

        lane.Picked = seat;
        lane.At = seat;
        Paint(lane);
        Draw(lane);

        // 고른 칸에 손끝을 준다 — 그래야 이어서 좌우 글쇠가 듣는다.
        if (seat < lane.Cells.Count)
        {
            lane.Cells[seat].Focus();
            lane.Cells[seat].BringIntoView();
        }
    }

    /// <summary>
    /// 차례 줄에서 <b>← →</b> 로 앞뒤 장을 훑는다. 훑는 대로 위 판에 그 장이 걸린다.
    /// </summary>
    /// <remarks>
    /// <b>Ctrl</b> 을 짚고 누르면 훑는 대신 <b>자리를 바꾼다</b> — ◀ ▶ 단추와 같은 일이다.
    /// 숫자 칸 안에서는 글쇠를 가로채지 않는다. 그 안의 좌우는 글자 사이를 오가는 것이라
    /// 뺏으면 초를 고쳐 적을 수 없다.
    /// </remarks>
    private void Arrow(Lane lane, KeyEventArgs e)
    {
        if (e.OriginalSource is TextBox) return;

        int way = e.Key switch { Key.Left => -1, Key.Right => +1, _ => 0 };
        if (way == 0) return;

        e.Handled = true;
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) Move(lane, lane.Picked, way);
        else Pick(lane, lane.Picked + way);
    }

    /// <summary>
    /// 고른 자리만 파랗게 칠한다.
    /// </summary>
    /// <remarks>
    /// 칸을 <b>다시 짓지 않는다</b>. 누르는 김에 줄을 새로 짜면 눌리던 단추가 그 자리에서
    /// 사라져 누름이 끝내 안 닿는다.
    /// </remarks>
    private static void Paint(Lane lane)
    {
        for (int seat = 0; seat < lane.Cells.Count; seat++)
        {
            bool on = seat == lane.Picked;
            lane.Cells[seat].BorderBrush = on ? PickedEdge : Brushes.LightGray;
            lane.Cells[seat].Background = on ? PickedBack : Brushes.Transparent;
        }
    }

    /// <summary>
    /// 고른 장을 앞·뒤 자리와 맞바꾼다.
    /// </summary>
    /// <remarks>
    /// 시간은 <b>장을 따라간다</b> — 자리에 남지 않는다. 0.3초를 준 장을 옮겼는데 그
    /// 0.3초가 자리에 남으면 옮긴 뜻이 없다.
    /// </remarks>
    private void Move(Lane lane, int seat, int way)
    {
        int to = seat + way;
        if (seat < 0 || seat >= lane.Steps.Count || to < 0 || to >= lane.Steps.Count) return;

        (lane.Steps[seat], lane.Steps[to]) = (lane.Steps[to], lane.Steps[seat]);
        lane.Picked = to;
        Sync(lane);
    }

    /// <summary>고른 칸의 테두리와 바탕.</summary>
    private static readonly Brush PickedEdge = new SolidColorBrush(Color.FromRgb(0x1A, 0x6B, 0xB5));
    private static readonly Brush PickedBack = new SolidColorBrush(Color.FromRgb(0xE3, 0xF2, 0xFD));

    /// <summary>
    /// 차례 줄을 다시 편다 — 장마다 그림 · 번호 · 시간 스피너 · 옮김과 뺌 단추다.
    /// </summary>
    /// <remarks>
    /// 칸을 누르면 <b>골라지고</b>(파란 테두리) 그 장이 판에 걸린다. ◀ ▶ 가 고른 장을
    /// 앞뒤 자리와 맞바꾼다 — 번호를 다시 적지 않아도 차례를 바꿀 수 있다.
    /// </remarks>
    private void FillStrip(Lane lane)
    {
        lane.Strip.Children.Clear();
        lane.Cells.Clear();
        for (int seat = 0; seat < lane.Steps.Count; seat++)
        {
            var step = lane.Steps[seat];
            var cell = new StackPanel { Margin = new Thickness(3, 0, 3, 0) };

            if (Bitmap(lane.Set, step.Frame) is { } bmp)
            {
                var image = new Image
                {
                    Source = bmp,
                    Width = FighterSprites.Width * StripScale,
                    Height = FighterSprites.Height * StripScale,
                };
                Crisp(image);
                cell.Children.Add(image);
            }

            cell.Children.Add(new TextBlock
            {
                Text = $"{seat + 1}. {step.Frame}",
                HorizontalAlignment = HorizontalAlignment.Center,
                FontSize = 11,
            });

            int here = seat;

            // 장마다의 시간. 0.1 씩 굴러가고 밑값이 0.1 이다.
            var seconds = new NumericSpinner
            {
                Minimum = MinSeconds,
                Maximum = MaxSeconds,
                Step = 0.1,
                DecimalPlaces = 2,
                Value = step.Seconds,
                Width = 62,
            };
            seconds.ValueChanged += (_, e) => { step.Seconds = e.NewValue; Tell(); };
            cell.Children.Add(Titled("초", seconds, "이 장이 머무는 시간(초)"));

            // 그 장 동안 앞으로 나가는 거리. 게임의 내지름이 30점이다.
            var push = new NumericSpinner
            {
                Minimum = -MaxPush,
                Maximum = MaxPush,
                Step = PushStep,
                DecimalPlaces = 0,
                Value = step.Push,
                Width = 62,
            };
            push.ValueChanged += (_, e) => { step.Push = e.NewValue; if (lane.At == here) Draw(lane); };
            cell.Children.Add(Titled("점", push, "이 장에서 앞으로 나가는 거리(점). 게임의 내지름은 30점이다"));

            // 왼쪽으로 · 빼기 · 오른쪽으로. 끝자리에서는 갈 데 없는 화살표가 죽는다.
            var back = Tiny("◀", () => Move(lane, here, -1), "앞자리와 맞바꾼다");
            back.IsEnabled = seat > 0;
            var next = Tiny("▶", () => Move(lane, here, +1), "뒷자리와 맞바꾼다");
            next.IsEnabled = seat < lane.Steps.Count - 1;

            var drop = Tiny("빼기", () =>
            {
                if (here < lane.Steps.Count) lane.Steps.RemoveAt(here);
                Sync(lane);
            }, "이 장을 차례에서 뺀다");

            var hands = new Grid { Margin = new Thickness(0, 2, 0, 0) };
            hands.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            hands.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            hands.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            Grid.SetColumn(back, 0);
            Grid.SetColumn(drop, 1);
            Grid.SetColumn(next, 2);
            hands.Children.Add(back);
            hands.Children.Add(drop);
            hands.Children.Add(next);
            cell.Children.Add(hands);

            bool on = seat == lane.Picked;
            var box = new Border
            {
                BorderBrush = on ? PickedEdge : Brushes.LightGray,
                Background = on ? PickedBack : Brushes.Transparent,
                BorderThickness = new Thickness(2),
                Padding = new Thickness(2),
                Margin = new Thickness(2),
                Child = cell,
                Cursor = Cursors.Hand,
                // 글쇠를 들으려면 칸이 손끝을 받아야 한다.
                Focusable = true,
            };
            // 칸을 누르면 골라진다. 단추는 제 누름을 이미 삼키므로 여기까지 안 온다.
            box.MouseLeftButtonDown += (_, _) => Pick(lane, here);
            box.KeyDown += (_, e) => Arrow(lane, e);

            lane.Cells.Add(box);
            lane.Strip.Children.Add(box);
        }
    }

    /// <summary>스피너 앞에 작은 이름표를 붙여 한 줄로 만든다.</summary>
    private static UIElement Titled(string name, UIElement what, string tip)
    {
        var line = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 2, 0, 0),
            ToolTip = tip,
        };
        line.Children.Add(new TextBlock
        {
            Text = name,
            FontSize = 10,
            Foreground = Brushes.Gray,
            Width = 10,
            VerticalAlignment = VerticalAlignment.Center,
        });
        line.Children.Add(what);
        return line;
    }

    /// <summary>차례 칸에 붙는 작은 단추.</summary>
    private static Button Tiny(string text, Action click, string tip)
    {
        var button = new Button
        {
            Content = text,
            FontSize = 10,
            Padding = new Thickness(2, 0, 2, 0),
            ToolTip = tip,
        };
        button.Click += (_, _) => click();
        return button;
    }

    // ── 돌리기 ──────────────────────────────────────────────────────────────

    /// <summary>
    /// 지금 자리의 장을 판에 건다. 차례가 비었으면 자리를 비운다.
    /// </summary>
    /// <remarks>
    /// 장마다 적어 둔 <see cref="Step.Push"/> 만큼 <b>선 자리에서 앞으로</b> 민다.
    /// 앞은 상대 쪽이라 아군은 왼쪽으로, 적군은 오른쪽으로 간다.
    /// </remarks>
    private void Draw(Lane lane)
    {
        if (lane.Steps.Count == 0) { lane.View.Source = null; return; }

        var step = lane.Steps[Math.Clamp(lane.At, 0, lane.Steps.Count - 1)];
        lane.View.Source = Bitmap(lane.Set, step.Frame);

        double stand = lane.Forward ? FoeStand : MyStand;
        Canvas.SetLeft(lane.View, lane.Forward ? stand + step.Push : stand - step.Push);
    }

    /// <summary>돌리다 멈추고, 멈췄으면 그 자리에서 이어 돈다.</summary>
    private void Toggle()
    {
        if (_playing) Pause();
        else Play();
    }

    /// <summary>
    /// 두 쪽을 함께 돌린다. 쪽마다 시계가 따로라 길이가 달라도 된다.
    /// </summary>
    /// <remarks>
    /// <b>멈춘 자리에서 이어 돈다.</b> 다만 끝에 닿은 채 멈췄으면 처음으로 돌린다 —
    /// 안 그러면 눌러도 그 자리에 선 채 아무 일도 안 난다.
    /// </remarks>
    private void Play()
    {
        foreach (var lane in Lanes())
        {
            if (lane.Steps.Count == 0) continue;
            if (lane.At >= lane.Steps.Count - 1) lane.At = 0;

            Draw(lane);
            lane.Clock.Interval = TimeSpan.FromSeconds(lane.Steps[lane.At].Seconds);
            lane.Clock.Start();
        }
        Playing(true);
    }

    /// <summary>그 자리에 세운다. 자리는 그대로라 다시 누르면 이어 돈다.</summary>
    private void Pause()
    {
        Stop();
        Playing(false);
    }

    private void Stop()
    {
        foreach (var lane in Lanes()) lane.Clock.Stop();
    }

    /// <summary>토글 단추의 글자를 지금 꼴에 맞춘다.</summary>
    private void Playing(bool on)
    {
        _playing = on;
        if (_playPause != null) _playPause.Content = on ? PauseText : PlayText;
    }

    /// <summary>한 장 앞뒤로 옮긴다 — 돌던 것은 세운다.</summary>
    private void StepBy(int way)
    {
        Pause();
        foreach (var lane in Lanes())
        {
            if (lane.Steps.Count == 0) continue;

            lane.At = (lane.At + way + lane.Steps.Count) % lane.Steps.Count;
            lane.Picked = lane.At;
            Paint(lane);
            Draw(lane);
        }
    }

    /// <summary>다음 장으로 넘긴다. 끝에 닿으면 되돌이면 처음으로, 아니면 멎는다.</summary>
    private void Advance(Lane lane, bool byHand = false)
    {
        if (lane.Steps.Count == 0) return;

        lane.At++;
        if (lane.At >= lane.Steps.Count)
        {
            if (!byHand && _loop.IsChecked != true)
            {
                lane.At = lane.Steps.Count - 1;
                lane.Clock.Stop();
                Draw(lane);

                // 두 쪽이 다 섰으면 토글도 「재생」으로 돌린다.
                if (Lanes().All(one => !one.Clock.IsEnabled)) Playing(false);
                return;
            }
            lane.At = 0;
        }

        Draw(lane);
        lane.Clock.Interval = TimeSpan.FromSeconds(lane.Steps[lane.At].Seconds);
    }

    // ── 알림 ────────────────────────────────────────────────────────────────

    /// <summary>쪽마다 몇 장에 몇 초인지 적는다.</summary>
    private void Tell()
    {
        foreach (var lane in Lanes())
            lane.Head.Text = lane.Name + Which(lane)
                + $" — {lane.Steps.Count}장 "
                + lane.Steps.Sum(s => s.Seconds).ToString("0.0", CultureInfo.InvariantCulture) + "초";

        _status.Text = "번호는 그림 밑에 적힌 그 번호다 (0~32). 장마다의 시간은 스피너로 0.1초씩 굴린다"
                     + " — 게임의 눈금 하나는 실은 0.067초(1/15초)다."
                     + "  차례 칸을 누르면 골라지고 ← → 로 앞뒤 장을 훑는다."
                     + "  자리를 바꾸는 것은 ◀ ▶ 나 Ctrl+← → 다."
                     + "  「점」은 그 장에서 상대 쪽으로 나가는 거리다 — 게임의 내지름이 30점이다.";
    }

    /// <summary>어느 스프라이트셋을 세워 두었는지 — 「(유럽 스프라이트셋 1)」 꼴로.</summary>
    private string Which(Lane lane) =>
        lane == _foe ? $" ({FoeSetNames[lane.Set - 1]} 스프라이트셋 {lane.Set})" : " (제독 스프라이트셋 0)";
}
