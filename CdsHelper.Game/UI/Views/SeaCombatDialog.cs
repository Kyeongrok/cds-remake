using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using CdsHelper.Game.Engine.Sea;
using CdsHelper.Game.Local.Helpers;
using CdsHelper.Support.Local.Models;

namespace CdsHelper.Game.UI.Views;

/// <summary>
/// 해전 판 — 원본 800x600 화면 그대로 띠·바다·배를 깔고, 이동을 찍고, 「Set」으로 한 턴을 굴린다.
/// </summary>
/// <remarks>
/// 셈은 <see cref="SeaBattle"/> 이 다 하고 여기서는 그리고 받기만 한다. 그림은 게임 것 그대로다
/// (<see cref="CombatArt"/>, 볼트 <c>61.분석-해전 그림</c>).
/// <code>
///   위 띠      bar-b-00  800x32   (0, 0)
///   아래 띠    bar-b-01  800x32   (0, 568) — 오른쪽 끝에 Set · Cancel 이 그려져 있다
///   바다 바탕  sea-00    800x600  (0x40, 0x20) − 스크롤                     ; 0x0043FFC7
///   칸·배      x = X*32 − 스크롤 + 0x38 ,  y = (Y+1)*32 − 스크롤 + (X 짝수 ? 16 : 0)   ; 0x0044006C
/// </code>
/// 판(23x17)이 다 들어가 스크롤은 0 이다. 좌우 기둥·나침반·E·A 글자 조각은 아직 안 뽑아
/// E 는 글자로 대신 찍고, 기둥 자리는 비워 둔다.
///
/// 이동 지시 중에는 원본처럼 <b>이동력 안의 칸을 육각 테로</b> 깔고(cell-00), 커서가 놓인
/// 후보 칸까지의 길을 <b>회색 칸</b>(cell-01)으로 칠한다.
/// </remarks>
public sealed class SeaCombatDialog : GameWindow, SeaBattle.IStage
{
    /// <summary>
    /// 해전이 어떻게 끝났는지 — 게임은 <b>기함(0 · 8)이 빠질 때</b> 판을 닫는다(<c>0x004350F0</c>).
    /// </summary>
    public enum Outcome
    {
        /// <summary>내 기함이 퇴각했다(<c>+0x118 = 1</c>).</summary>
        Escaped,

        /// <summary>항복했다. 게임에는 없는 앱 차림표다 — 부르는 쪽이 도망처럼 다룬다.</summary>
        Surrendered,

        /// <summary>적 기함을 가라앉혔다(<c>+0x118 = 0</c>).</summary>
        Won,

        /// <summary>내 기함이 가라앉았다(<c>+0x118 = 2</c> → GAME OVER).</summary>
        Defeated,

        /// <summary>적 기함이 퇴각했다 — 게임은 승리 쪽(<c>+0x118 = 0</c>)으로 치고 전리품만 없다.</summary>
        EnemyRetreated,
    }

    /// <summary>판이 끝난 뒤 부르는 쪽이 값을 치르는 데 쓰는 알맹이.</summary>
    /// <param name="EnemyDowned">가라앉힌 적 배(상태 1).</param>
    /// <param name="EnemyCaptured">빼앗거나 승원을 없앤 적 배(상태 2). 전리품의 꺾음은 둘의 합이다.</param>
    public sealed record Report(Outcome Outcome, int EnemyDowned, int EnemyCaptured);

    /// <summary>포격 소리 파트 — 사운드 ID 0x29 발사 · 0x2A 명중 · 0x2B 빗나감 · 0x2E 격침(ID − 28).</summary>
    private const int FirePart = 0x29 - 28, HitPart = 0x2A - 28, MissPart = 0x2B - 28, SinkPart = 0x2E - 28;

    /// <summary>가까운 싸움 소리 — 0x2C 충돌 · 0x2D 백병전 · 0x30 불 · 0x1E 총격(WAVE 파트 = ID − 28).</summary>
    private const int CrashPart = 0x2C - 28, MeleePart = 0x2D - 28, IgnitePart = 0x30 - 28, GunfightPart = 0x1E - 28;

    /// <summary>원본 기다림 한 눈(<c>0x00428000(n, 끊기)</c> = n × 50ms).</summary>
    private static readonly TimeSpan Tick = TimeSpan.FromMilliseconds(50);

    /// <summary>승리 기다림 100(5초) · 패배 기다림 180(9초) — 못 끊는다(볼트 95 의 12절).</summary>
    private const int WinTicks = 100, LoseTicks = 180;

    /// <summary>불 그림이 한 장 바뀌는 눈 — <c>[+0x8F0] % 48 / 16</c>.</summary>
    private const int FlameTicks = 16;

    /// <summary>포격 연출의 한 장 참과 포탄이 날아가는 걸음 수(<c>0x004384E8</c>).</summary>
    private static readonly TimeSpan FxFrame = TimeSpan.FromMilliseconds(60);
    private const int BallSteps = 9;

    /// <summary>
    /// 포탄 한 걸음의 참. 걸음 0~9 열 번이라 한 발이 이것의 열 배 동안 난다 — 15ms 면 0.15초.
    /// 원본 주소에서 옮긴 값이 아니라 보기 좋게 맞춘 값이다(예전 25ms 는 좀 느렸다).
    /// </summary>
    private static readonly TimeSpan BallStepTime = TimeSpan.FromMilliseconds(15);

    /// <summary>원본 화면 크기.</summary>
    private const int ScreenWidth = CombatArt.SeaWidth, ScreenHeight = CombatArt.SeaHeight;

    /// <summary>위아래 띠 높이.</summary>
    private const int BandHeight = 32;

    /// <summary>아래 띠에 그려진 Set · Cancel 자리(가로).</summary>
    private const double SetLeft = 672, CancelLeft = 736;

    /// <summary>아래 띠의 윗변(넓은 화면은 한 줄 위로 올라 560 이다)과 기둥 키.</summary>
    private const int BottomBandTop = 560, FrameHeight = 504;

    /// <summary>나침반 자리와 크기(112x112, 자리는 갈무리로 잼).</summary>
    private const int CompassX = 64, CompassY = 32, CompassSize = 112;

    /// <summary>나침반 위에 겹치는 풍향 조각.</summary>
    private readonly Image _compass = new();

    /// <summary>한 걸음 사이의 참.</summary>
    private static readonly TimeSpan StepSpan = TimeSpan.FromMilliseconds(220);

    private readonly SeaBattle _battle;
    private readonly CombatArt _art;
    private readonly Enemy _foe;
    private readonly uint[]? _face;
    private readonly SoundBank? _sfx;

    /// <summary>포격 연출(포탄·폭발·물기둥·피해 숫자)을 얹는 층.</summary>
    private readonly Canvas _fx = new() { IsHitTestVisible = false };

    private readonly Canvas _board = new() { Width = ScreenWidth, Height = ScreenHeight, ClipToBounds = true };

    /// <summary>
    /// 밀리는 층 — 바다·칸·배·연출이 여기 얹힌다. 틀과 나침반은 <see cref="_board"/> 에 붙박이다.
    /// </summary>
    /// <remarks>
    /// 원본도 바탕과 칸만 스크롤 값을 빼고 찍는다(볼트 61 · 66) —
    /// <c>바다 (0x40 − 스크롤X, 0x20 − 스크롤Y)</c> · <c>칸 X*32 − 스크롤X + 0x38</c>.
    /// </remarks>
    private readonly Canvas _field = new();

    /// <summary>판이 밀린 만큼(원본 함대 <c>+0x0888</c> · <c>+0x088C</c>).</summary>
    private readonly TranslateTransform _slide = new(0, 0);

    /// <summary>가장자리 띠에 커서가 든 동안 판을 미는 눈금.</summary>
    private readonly DispatcherTimer _scrollTimer =
        new(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(40) };

    private int _scrollWay = -1;
    private readonly Canvas _marks = new() { IsHitTestVisible = false };
    private readonly Canvas _path = new() { IsHitTestVisible = false };
    private readonly Dictionary<int, Image> _shipArt = [];

    private SeaBattle.Ship? _picked;
    private List<(List<SeaBattle.Move> Plan, int X, int Y, int Way)> _options = [];

    /// <summary>
    /// 제자리 선회 칸 둘 — 뱃머리 오른앞·왼앞이다(<c>0x0043E6B8</c>). 길 후보보다 <b>먼저</b>
    /// 걸리므로, 그 두 칸을 누르면 한 칸 움직이는 지시가 아니라 뱃머리만 돌리는 지시가 된다.
    /// </summary>
    private List<(int X, int Y, SeaBattle.Move Turn)> _pivots = [];
    private bool _running;

    public Outcome Result { get; private set; } = Outcome.Surrendered;

    /// <summary>적장 얼굴 — 끝맺음에서 적장이 말할 때 쓴다(<c>0x00477AF0(1, id)</c>). 없으면 얼굴 없이.</summary>
    private readonly uint[]? _foeFace;

    /// <summary>판을 닫기 직전에 부르는 값 치르기 — 알림이 판 위에 뜨게 한다.</summary>
    private readonly Action<Window, Report>? _settle;

    /// <summary>승리·적 퇴각 소리 0x4D · 패배 소리 0x4A(파트 = ID − 28).</summary>
    private const int WinPart = 0x4D - 28, LosePart = 0x4A - 28;

    /// <summary>판을 연 제독. 모의해전 연습선만 띄웠으면 되쓸 배가 없다.</summary>
    private readonly Player? _player;

    /// <summary>판의 아군 칸과 함대 레코드의 짝 — 판 끝에 내구·승원·대포를 되쓴다.</summary>
    private readonly IReadOnlyList<(SeaBattle.Ship Slot, Support.Local.Models.Ship Record)> _fleet;

    /// <summary>결투 판을 여는 쪽(<c>0x004AA700</c>). 없으면 일기토가 안 열린다.</summary>
    private readonly Func<Window, bool?>? _duel;

    /// <summary>불붙은 배 위에 덮는 불꽃(blast-03~05).</summary>
    private readonly Dictionary<int, Image> _flames = [];

    /// <summary>불꽃 눈 — 원본 <c>[+0x8F0]</c>. 한 눈을 50ms 로 어림했다.</summary>
    private int _fireTick;

    private readonly DispatcherTimer _flameTimer;

    /// <summary>짐 창에 쓸 표(교역소·교역품·도시). 없으면 짐 창을 건너뛴다.</summary>
    private readonly Engine.Game? _game;
    private readonly Random _random;

    private SeaCombatDialog(SeaBattle battle, CombatArt art, in Enemy foe, uint[]? face, double zoom,
                            SoundBank? sfx, uint[]? foeFace = null, Action<Window, Report>? settle = null,
                            Player? player = null,
                            IReadOnlyList<(SeaBattle.Ship, Support.Local.Models.Ship)>? fleet = null,
                            Func<Window, bool?>? duel = null, Engine.Game? game = null, Random? rng = null)
    {
        _game = game;
        _random = rng ?? new Random();
        _battle = battle;
        _art = art;
        _foe = foe;
        _face = face;
        _sfx = sfx;
        _foeFace = foeFace;
        _settle = settle;
        _player = player;
        _fleet = fleet ?? [];
        _duel = duel;
        _flameTimer = new DispatcherTimer(DispatcherPriority.Render) { Interval = Tick };
        _flameTimer.Tick += (_, _) =>
        {
            if (++_fireTick % FlameTicks == 0) UpdateFlames();
        };
        Loaded += (_, _) => _flameTimer.Start();
        Closed += (_, _) => _flameTimer.Stop();
        // 밀리는 층을 먼저 깔고, 그 위에 붙박이 틀을 얹는다.
        _field.RenderTransform = _slide;
        Panel.SetZIndex(_field, 5);
        _board.Children.Add(_field);

        Panel.SetZIndex(_fx, 25);
        _field.Children.Add(_fx);

        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.WidthAndHeight;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        Background = Brushes.Black;

        // 바다는 (0x40, 0x20) 에 깔린다 — 틀 아래로 들어간 만큼은 가려진다.
        Put(_field, art.Sea(), SeaLeft, SeaTop, CombatArt.SeaWidth, CombatArt.SeaHeight, z: 0);

        // 틀 — 넓은 화면(800) 갈래의 파트 4 를 자른 것이다(볼트 61 의 5-2 절).
        //   위 띠 (0,0) 800x32 · 기둥 머리 (0,32)·(736,32) 64x32 · 기둥 (0,64)·(736,64) 64x504 · 아래 띠 (0,560)
        Put(_board, art.Path_("bar-b-00"), 0, 0, ScreenWidth, BandHeight, z: 30);
        Put(_board, art.Path_("pair-00"), 0, BandHeight, 64, 32, z: 30);
        Put(_board, art.Path_("pair-01"), ScreenWidth - 64, BandHeight, 64, 32, z: 30);
        Put(_board, art.Path_("frame-left"), 0, 64, 64, FrameHeight, z: 30);
        Put(_board, art.Path_("frame-right"), ScreenWidth - 64, 64, 64, FrameHeight, z: 30);
        Put(_board, art.Path_("bar-b-01"), 0, BottomBandTop, ScreenWidth, BandHeight, z: 30);

        // 나침반 — 장미(파트 19 첫 조각) 위에 풍향+1 번째 조각을 겹친다. 처음 자리는 갈무리로 잰 값이고,
        // 원본처럼 끌어다 옮길 수 있다(나침반은 제 창을 가진 딸 창이다 — 0x004337C0).
        var rose = new Canvas
        {
            Width = CompassSize,
            Height = CompassSize,
            Background = Brushes.Transparent,
            Cursor = Cursors.SizeAll,
        };
        Put(rose, art.Path_("compass-00"), 0, 0, CompassSize, CompassSize, z: 0);
        _compass.Width = _compass.Height = CompassSize;
        _compass.IsHitTestVisible = false;
        RenderOptions.SetBitmapScalingMode(_compass, GameUi.SpriteScaling);
        Panel.SetZIndex(_compass, 1);
        rose.Children.Add(_compass);
        Canvas.SetLeft(rose, CompassX);
        Canvas.SetTop(rose, CompassY);
        Panel.SetZIndex(rose, 20);
        _board.Children.Add(rose);
        DragCompass(rose);

        Panel.SetZIndex(_marks, 5);
        _field.Children.Add(_marks);
        Panel.SetZIndex(_path, 6);
        _field.Children.Add(_path);

        foreach (var ship in battle.Ships)
        {
            var image = new Image { Width = CombatArt.ShipSize, Height = CombatArt.ShipSize, IsHitTestVisible = false };
            RenderOptions.SetBitmapScalingMode(image, GameUi.SpriteScaling);
            Panel.SetZIndex(image, 10);
            _field.Children.Add(image);
            _shipArt[ship.Index] = image;

            var flame = new Image
            {
                Width = 48, Height = 48, IsHitTestVisible = false, Visibility = Visibility.Collapsed,
            };
            RenderOptions.SetBitmapScalingMode(flame, GameUi.SpriteScaling);
            Panel.SetZIndex(flame, 11);
            _field.Children.Add(flame);
            _flames[ship.Index] = flame;
        }

        _board.Background = Brushes.Transparent;
        _board.MouseLeftButtonUp += Touch;
        _board.MouseMove += (_, e) => AimScroll(e.GetPosition(_board));
        _board.MouseLeave += (_, _) => { _scrollWay = -1; _scrollTimer.Stop(); };
        _scrollTimer.Tick += (_, _) => Slide(_scrollWay);
        Closed += (_, _) => _scrollTimer.Stop();
        _board.LayoutTransform = new ScaleTransform(zoom, zoom);

        // 판 아래에 말 줄은 없다 — 원본은 말을 모두 창으로 띄운다.
        Content = _board;

        // 배를 우클릭하면 「해전전황정보(선박)」 — 아군·적 모두. 빈 바다면 항복 차림표다.
        MouseRightButtonUp += (_, e) =>
        {
            if (_running) return;
            var spot = e.GetPosition(_board);
            var (x, y) = CellAt(new Point(spot.X + _scrollX, spot.Y + _scrollY));
            if (x >= 0 && _battle.ShipAt(x, y) is { } ship)
            {
                SeaShipInfoDialog.Show(this, ship);
                return;
            }
            GameUi.ContextMenuAt(this, e.GetPosition(this),
                                 [("항복한다", Surrender), ("게임 복귀", () => { })]);
        };

        // 글쇠(0x0043EEDF 갈래) — PgUp 은 「해전전황정보(제독·함대수)」(0x0043F895),
        // PgDn 은 지금 고른 배의 「해전전황정보(선박)」(0x0043F8FF)이다.
        // (방향 글쇠·1~9·Enter·ESC·Space 는 원본의 커서 상태를 그대로 옮겨야 해서 아직 없다.)
        PreviewKeyDown += (_, e) =>
        {
            if (_running) return;
            if (e.Key == Key.PageUp)
            {
                SeaBattleInfoDialog.Show(this, _battle, _player, _foe.Leader);
                e.Handled = true;
            }
            else if (e.Key == Key.PageDown && _picked is { } shown)
            {
                SeaShipInfoDialog.Show(this, shown);
                e.Handled = true;
            }
        };

        Loaded += (_, _) => Redraw();

        // 들머리 — <b>판이 펼쳐진 뒤에</b> 바람과 퇴각지점을 알리고 이동 지시를 재촉한다(0x0043C4E0).
        //
        // <b>Loaded 로는 이르다.</b> 우리 창은 Show() 안에서 Loaded 가 올라오는데 그때는 판이
        // 아직 한 번도 안 그려졌다. 거기서 말을 띄우면 판은 안 보이고 <b>바다 지도 위에 대사만</b>
        // 떴다. ContentRendered 는 첫 그림이 나간 뒤에 오므로 그제서야 말한다.
        bool said = false;
        ContentRendered += (_, _) =>
        {
            if (said) return;
            said = true;
            OpenTurn();
        };
    }

    // ── 그리기 ────────────────────────────────────────────────────────────

    /// <summary>
    /// 나침반을 끌어다 옮긴다 — 판 안(바다 쪽)에서만 움직이고, 누름은 판의 칸 누름으로 새지 않는다.
    /// </summary>
    private void DragCompass(Canvas rose)
    {
        Point grab = default;
        rose.MouseLeftButtonDown += (_, e) =>
        {
            grab = e.GetPosition(rose);
            rose.CaptureMouse();
            e.Handled = true;
        };
        rose.MouseMove += (_, e) =>
        {
            if (!rose.IsMouseCaptured) return;
            var at = e.GetPosition(_board);
            Canvas.SetLeft(rose, Math.Clamp(at.X - grab.X, 64, ScreenWidth - 64 - CompassSize));
            Canvas.SetTop(rose, Math.Clamp(at.Y - grab.Y, BandHeight, BottomBandTop - CompassSize));
        };
        rose.MouseLeftButtonUp += (_, e) =>
        {
            rose.ReleaseMouseCapture();
            e.Handled = true;
        };
    }

    /// <summary>칸 조각(48x32)의 왼쪽 위 — 원본 식 그대로다(<c>0x0044006C</c>).</summary>
    private static (double X, double Y) ScreenOf(int x, int y) =>
        (x * CombatArt.Cell + 0x38, (y + 1) * CombatArt.Cell + ((x & 1) == 0 ? 16 : 0));

    /// <summary>
    /// 열두 방향 그림에서 육각 방향 하나를 고른다 — 두 장마다 한 장이다.
    /// </summary>
    /// <remarks>
    /// 그림 열둘은 <b>0 이 위(돛 뒤가 보인다), 6 이 아래(흰 돛 앞이 보인다)</b>이고 시계 방향으로 돈다 —
    /// 육각 방향 0(위)~5 와 같은 차례라 두 장마다 한 장이다. 처음에 +6 을 먹여 180도 돌아가 있었고,
    /// 그 뒤 좌우만 뒤집어 위아래가 거꾸로 나왔다.
    /// </remarks>
    private static int FrameOf(int way) => ((way * 2) % CombatArt.Ways + CombatArt.Ways) % CombatArt.Ways;

    private void Redraw()
    {
        _marks.Children.Clear();

        // 나침반의 풍향 조각(compass-01~06 = 풍향 0~5).
        _compass.Source = Bitmap(_art.Path_($"compass-{_battle.Wind + 1:D2}"));

        // 퇴각 지대 — 원본 화면의 큰 고딕 E(mark-09, 32x32).
        foreach (var (x, y) in _battle.RetreatCells())
        {
            var (sx, sy) = ScreenOf(x, y);
            Put(_marks, _art.Path_("mark-09"), sx + 8, sy, 32, 32, z: 0);
        }

        // 고른 배가 <b>실제로 갈 수 있는 칸</b>을 육각 테로 깐다. 걸음 수로만 재어 깔았더니
        // 선회 규칙(걸음마다 선회 하나 + 한 칸)으로는 못 닿는 옆·뒤 칸에도 테가 서서,
        // 그 칸을 눌러도 아무 일이 없었다 — 테가 선 칸은 모두 누를 수 있어야 한다.
        if (_picked is { } picked)
        {
            foreach (var option in _options)
                Cell(_marks, option.X, option.Y, lit: false);
            foreach (var (px, py, _) in _pivots)
                Cell(_marks, px, py, lit: false);

            // 찍어 둔 길은 회색 칸으로 칠한다 — 누른 칸까지의 길이다.
            if (picked.Plan.Count > 0
                && SeaBattle.Trace(picked.X, picked.Y, picked.Way, picked.Plan) is { Count: > 0 } trail)
            {
                foreach (var (px, py, _) in trail) Cell(_marks, px, py, lit: true);

                // 마지막 칸에는 그 칸에서 볼 방향의 화살표(mark-00~05 = 방향 0~5)를 얹는다.
                var (lx, ly, lway) = trail[^1];
                var (ax, ay) = ScreenOf(lx, ly);
                Put(_marks, _art.Path_($"mark-{lway:D2}"), ax + 8, ay, 32, 32, z: 0);
            }
        }

        foreach (var ship in _battle.Ships)
        {
            var image = _shipArt[ship.Index];
            image.Visibility = ship.CanAct ? Visibility.Visible : Visibility.Collapsed;
            if (!ship.CanAct) continue;

            image.Source = Bitmap(_art.Ship(ship.Art, FrameOf(ship.Way)));
            var (sx, sy) = ScreenOf(ship.X, ship.Y);

            // 내 배 발밑 칸 — 지시를 마쳤으면(부딪혀 못 움직이는 배 포함) 회색, 아직이면 빈 테다.
            // 턴이 도는(배가 움직이는) 동안은 칸 없이 배만 간다 — 게임 화면이 그렇다.
            if (ship.Mine && !_running) Cell(_marks, ship.X, ship.Y, lit: ship.Ordered || ship.Stuck);
            Canvas.SetLeft(image, sx);
            Canvas.SetTop(image, sy - 12);

            // 배 옆 글자 — <b>A(dot-01)는 기함</b> 표시라 양쪽 기함에만 찍고(0x004407D5, 칸 0·8만 돈다),
            // <b>E(dot-02)는 적</b> 표시라 적 배마다 찍는다(0x00440885). 그래서 적 기함에는 둘 다 붙는다.
            // 괴물 싸움(+0x8FC)이면 적 쪽은 둘 다 안 찍는다.
            bool foeMarks = ship.Mine || !_battle.Monster;
            if (ship.Flagship && foeMarks)
                Put(_marks, _art.Path_("dot-01"), sx + 8, sy, 8, 8, z: 0);
            if (!ship.Mine && foeMarks)
                Put(_marks, _art.Path_("dot-02"), sx + 32, sy + 24, 8, 8, z: 0);
        }
        UpdateFlames();
    }

    /// <summary>
    /// 불붙은 배(상태 5) 위에 blast-03·04·05 를 <c>[+0x8F0] % 48 / 16</c> 로 번갈아 덮는다(<c>0x004406AD</c>).
    /// </summary>
    private void UpdateFlames()
    {
        int frame = _fireTick % (FlameTicks * 3) / FlameTicks;
        foreach (var ship in _battle.Ships)
        {
            if (!_flames.TryGetValue(ship.Index, out var flame)) continue;
            bool on = ship.CanAct && ship.Burning;
            flame.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
            if (!on) continue;
            flame.Source = Bitmap(_art.Path_($"blast-{3 + frame:D2}"));
            var (sx, sy) = ScreenOf(ship.X, ship.Y);
            Canvas.SetLeft(flame, sx);
            Canvas.SetTop(flame, sy - 8);
        }
    }

    private void Cell(Canvas layer, int x, int y, bool lit)
    {
        if (Bitmap(_art.CellArt(lit)) is not { } source) return;
        var (sx, sy) = ScreenOf(x, y);
        var image = new Image { Source = source, Width = 48, Height = 32 };
        RenderOptions.SetBitmapScalingMode(image, GameUi.SpriteScaling);
        Canvas.SetLeft(image, sx);
        Canvas.SetTop(image, sy);
        layer.Children.Add(image);
    }

    /// <summary>
    /// 말은 판 위에 「해전」 창으로 띄운다 — 부관(없으면 뱃사람) 얼굴이다. 적의 움직임까지 끝나면
    /// 「각 함대에 이동 지시를 내려 주십시오.」가 이 창으로 뜨며 내 턴이 열린다.
    /// </summary>
    private void Say(string text) => ConfirmDialog.Tell(this, text, BattleTitle, _face);

    // ── 받기 ──────────────────────────────────────────────────────────────

    /// <summary>화면 점에서 가장 가까운 칸.</summary>
    /// <summary>
    /// 가장자리 <b>64점 띠</b>에 커서가 들면 판이 그쪽으로 밀린다(<c>0x0043C6B6</c>).
    /// </summary>
    /// <remarks>
    /// 원본은 커서 자리만 보고 <c>+0x08D0</c>(커서 모양·미는 쪽)을 매긴다 — 누르지 않아도
    /// 민다. 아래 띠의 <c>Set</c>·<c>Cancel</c> 자리에 들면 미는 것을 멈춘다.
    /// </remarks>
    private void AimScroll(Point at)
    {
        int way = -1;
        if (at.Y >= BandHeight && at.Y < BottomBandTop)
        {
            if (at.X < EdgeBand) way = 3;                       // 왼쪽
            else if (at.X >= ScreenWidth - EdgeBand) way = 1;   // 오른쪽
            else if (at.Y < BandHeight + EdgeBand) way = 0;     // 위
            else if (at.Y >= BottomBandTop - EdgeBand) way = 2; // 아래
        }

        _scrollWay = way;
        if (way < 0) _scrollTimer.Stop();
        else if (!_scrollTimer.IsEnabled) _scrollTimer.Start();
    }

    /// <summary>판을 그쪽으로 한 눈금 민다. 세계 밖으로는 안 나간다.</summary>
    private void Slide(int way)
    {
        if (way < 0) { _scrollTimer.Stop(); return; }

        double x = _scrollX + (way == 1 ? ScrollStep : way == 3 ? -ScrollStep : 0);
        double y = _scrollY + (way == 2 ? ScrollStep : way == 0 ? -ScrollStep : 0);
        Scroll(x, y);
    }

    /// <summary>
    /// 판이 밀린 만큼을 박는다. <b>바다 그림 밖으로는 안 민다</b> — 더 밀면 그림이 끝나
    /// 검은 바닥이 드러난다.
    /// </summary>
    /// <remarks>
    /// 바다(<c>800x600</c>)는 <c>(0x40, 0x20)</c> 에 깔리므로 틀 안쪽(<c>64~736</c> ·
    /// <c>32~560</c>)을 덮는 것은 가로 128 · 세로 72 까지다. 칸이 차지하는 넓이로 잰
    /// 값(136 · 80)보다 조금 작은데, 그 여덟 점이 <b>오른쪽·아래에 검은 띠</b>로 났다.
    /// 끝 칸은 이 안에서도 다 드러난다.
    /// </remarks>
    private void Scroll(double x, double y)
    {
        double cellsX = WorldWidth - (ScreenWidth - EdgeBand * 2);
        double cellsY = WorldHeight - (BottomBandTop - BandHeight);
        double seaX = SeaLeft + CombatArt.SeaWidth - (ScreenWidth - EdgeBand);
        double seaY = SeaTop + CombatArt.SeaHeight - BottomBandTop;

        _scrollX = Math.Clamp(x, 0, Math.Max(0, Math.Min(cellsX, seaX)));
        _scrollY = Math.Clamp(y, 0, Math.Max(0, Math.Min(cellsY, seaY)));
        _slide.X = -_scrollX;
        _slide.Y = -_scrollY;
    }

    /// <summary>바다 그림이 깔리는 자리(<c>0x40, 0x20</c>).</summary>
    private const double SeaLeft = 0x40, SeaTop = 0x20;

    /// <summary>판이 밀린 만큼.</summary>
    private double _scrollX, _scrollY;

    /// <summary>가장자리 띠 폭과 한 번에 미는 눈금.</summary>
    private const double EdgeBand = 64, ScrollStep = 8;

    /// <summary>
    /// 칸이 차지하는 넓이 — 스물세 칸 x 열일곱 줄에 배 그림 넓이를 더한 것이다.
    /// </summary>
    private const double WorldWidth = (SeaBattle.Cols - 1) * CombatArt.Cell + 0x38 + CombatArt.ShipSize,
                         WorldHeight = SeaBattle.Rows * CombatArt.Cell + 16 + CombatArt.ShipSize;

    private static (int X, int Y) CellAt(Point at)
    {
        int bestX = -1, bestY = -1;
        double best = double.MaxValue;
        for (int x = 0; x < SeaBattle.Cols; x++)
            for (int y = 0; y < SeaBattle.Rows; y++)
            {
                if (!SeaBattle.OnBoard(x, y)) continue;
                var (sx, sy) = ScreenOf(x, y);
                double dx = at.X - (sx + 24), dy = at.Y - (sy + 16);
                double d = dx * dx + dy * dy;
                if (d < best) { best = d; bestX = x; bestY = y; }
            }
        return best > 40 * 40 ? (-1, -1) : (bestX, bestY);
    }

    private void Touch(object sender, MouseButtonEventArgs e)
    {
        if (_running) return;

        // 위임 중이면 판에 손대는 순간 지휘를 되찾겠냐고 먼저 묻는다(0x0043EE32).
        if (!TookBack()) return;

        var at = e.GetPosition(_board);

        // 아래 띠의 Set · Cancel.
        if (at.Y >= BottomBandTop)
        {
            if (at.Y < BottomBandTop + BandHeight)
            {
                if (at.X >= CancelLeft) ClearOrders();
                else if (at.X >= SetLeft) Decide();
            }
            return;
        }

        var (x, y) = CellAt(new Point(at.X + _scrollX, at.Y + _scrollY));
        if (x < 0) return;

        var here = _battle.ShipAt(x, y);

        if (here is { Mine: true })
        {
            // 퇴각 지대(E)에 선 배를 누르면 곧바로 「해전」 창에 「퇴각하겠습니까?」를 묻는다(0x0056B600).
            // 아니오면 여느 때처럼 고른다.
            if (!ReferenceEquals(here, _picked) && _battle.IsRetreatCell(here.X, here.Y)
                && ConfirmDialog.Ask(this, "퇴각하겠습니까?", BattleTitle))
            {
                _battle.Retreat(here);
                Unpick();
                if (_battle.Over) Finish();
                else AfterOrder();
                return;
            }

            if (ReferenceEquals(here, _picked))
            {
                _battle.Order(here, []);
                Unpick();
                ConfirmDialog.Tell(this, "이동하지 않습니다", BattleTitle);
                AfterOrder();
                return;
            }

            if (here.Stuck)
            {
                Say("충돌 영향으로 다음 지시를 받을 때까지 이동할 수 없습니다.");
                return;
            }

            _picked = here;
            _options = _battle.Options(here);
            _pivots = _battle.Pivots(here);
            Redraw();
            return;
        }

        if (_picked is not { } ship) return;

        // 뱃머리 앞옆 두 칸은 <b>제자리 선회</b>다 — 길 후보보다 먼저 본다(0x0043E92B).
        if (_pivots.FirstOrDefault(p => p.X == x && p.Y == y) is { Turn: not SeaBattle.Move.Straight } pivot)
        {
            _battle.OrderPivot(ship, pivot.Turn);
            Redraw();
            ConfirmDialog.Tell(this, "선회 방향 결정!", BattleTitle);
            Unpick();
            AfterOrder();
            return;
        }

        var hit = _options.FirstOrDefault(o => o.X == x && o.Y == y);
        if (hit.Plan == null) return;

        // 누르면 그 칸까지의 길이 회색으로 칠해지고 「해전」 창이 결정을 알린다. 길 지시는
        // 선회가 끼어 있어도 <b>늘</b> 「이동 전방 결정!」이다(0x0043EAB9 의 0x0056B698) —
        // 「선회 방향 결정!」(0x0056B660)은 제자리 선회에만 뜬다(0x0043E994).
        _battle.Order(ship, hit.Plan);
        Redraw();
        ConfirmDialog.Tell(this, "이동 전방 결정!", BattleTitle);
        // 확인을 누르면 벌집(테·길)은 걷히고, 그 배 발밑 칸이 회색으로 바뀌어 지시가 끝났음을 보인다.
        Unpick();
        AfterOrder();
    }

    /// <summary>
    /// 부관의 성미 칸 0. 부관이 없거나 밑표에서 못 찾으면 1(여느 판정)이다.
    /// </summary>
    private static int MateTemperOf(Engine.Game? game, string mateName)
    {
        if (game == null || mateName.Length == 0) return 1;
        if (game.World?.People.FirstOrDefault(r => r.Name == mateName) is not { } row) return 1;
        if (game.PersonTemplates?.Find(row.Id) is not { } who) return 1;
        return FleetRaid.FortuneOf(who.Face, who.Blood, who.Nation)[0];
    }

    /// <summary>해전 창 제목.</summary>
    private const string BattleTitle = "해전";

    /// <summary>
    /// 한 턴을 연다(<c>0x0043C4E0</c>) — 부관이 맡겠다고 나서고, 아니면 바람과 이동 지시를 이른다.
    /// </summary>
    /// <remarks>
    /// 부관이 있고 아직 제독이 직접 몰고 있을 때만 묻는다(<c>0x0043C586</c> 의 <c>cmp 1</c>).
    /// 맡기면 바람 안내도 이동 지시 재촉도 <b>안 나온다</b>(<c>0x0043C5DE</c>).
    /// </remarks>
    private void OpenTurn()
    {
        if (!_battle.Delegated && HasMate
            && ConfirmDialog.Ask(this, SeaBattle.OfferToLead, BattleTitle, _face))
        {
            _battle.Delegated = true;
            return;
        }
        if (_battle.Delegated) return;

        Say(_battle.WindNotice());
        Say(_battle.OrderPrompt());
        // 괴물 판이면 숨었다는 말이 한 마디 더 붙는다(0x0043C670).
        if (_battle.MonsterHidWord() is { Length: > 0 } hid) Say(hid);
    }

    /// <summary>부관이 있는가 — 없으면 위임을 아예 못 묻는다(<c>+0x944</c> 가 0).</summary>
    private bool HasMate => _player is { } who && who.MateAt(0).Length > 0;

    /// <summary>
    /// 위임 중에 판이나 단추에 손을 대면 지휘를 되찾겠냐고 묻는다(<c>0x0043EE32</c>).
    /// </summary>
    /// <returns>되찾았으면 참 — 부르는 쪽이 하던 일을 이어 간다.</returns>
    private bool TookBack()
    {
        if (!_battle.Delegated) return true;
        if (!ConfirmDialog.Ask(this, SeaBattle.TakeBack, BattleTitle, _face)) return false;

        _battle.Delegated = false;
        foreach (var ship in _battle.Ships.Where(s => s.Mine && s.CanAct))
        {
            ship.Plan.Clear();
            ship.Ordered = false;
        }
        Say(_battle.OrderPrompt());
        return true;
    }

    /// <summary>
    /// 지시할 수 있는 내 배가 모두 지시를 마쳤으면 「이동 계획을 종료하겠습니까?」를 묻는다.
    /// </summary>
    private void AfterOrder()
    {
        if (_battle.Ships.Where(s => s.Mine && s.CanAct).All(s => s.Ordered || s.Stuck))
            Decide();
    }

    private void Unpick()
    {
        _picked = null;
        _options = [];
        _pivots = [];
        _path.Children.Clear();
        Redraw();
    }

    private void ClearOrders()
    {
        if (_running) return;
        foreach (var ship in _battle.Ships.Where(s => s.Mine && s.CanAct)) ship.Plan.Clear();
        Say(_battle.OrderPrompt());
        if (_battle.MonsterHidWord() is { Length: > 0 } hid) Say(hid);
        Unpick();
    }

    /// <summary>「Set」 — 계획을 마치고 한 턴을 굴린다(<c>0x0043DDD0</c> → <c>0x0043CA60</c>).</summary>
    private void Decide()
    {
        if (_running) return;
        // 「해전」 창에 YES/NO — 얼굴 없이 묻는다(0x0056B5A0).
        if (!ConfirmDialog.Ask(this, "이동 계획을 종료하겠습니까?", BattleTitle))
        {
            // 물리면 부관이 한 번 더 권한다(0x0043DEEB) — 맡기면 짜던 계획은 버려진다.
            if (!_battle.Delegated && HasMate
                && ConfirmDialog.Ask(this, SeaBattle.OfferAgain, BattleTitle, _face))
            {
                _battle.Delegated = true;
                foreach (var ship in _battle.Ships.Where(s => s.Mine && s.CanAct))
                {
                    ship.Plan.Clear();
                    ship.Ordered = false;
                }
                Unpick();
                Redraw();
            }
            return;
        }

        _running = true;
        _picked = null;
        _options = [];
        _pivots = [];
        _path.Children.Clear();
        int windBefore = _battle.Wind;
        try
        {
            _battle.EndPlanning();
            // 충돌·백병전·총격·포격·가라앉음은 일이 날 때마다 이 창의 IStage 로 불린다.
            _battle.Execute(this);
            Redraw();
        }
        finally
        {
            _running = false;
            Redraw();   // 다음 계획 차례 — 발밑 칸을 도로 깐다
        }

        if (_battle.Over) { Finish(); return; }

        // 괴물은 턴 끝마다 한 번 굴려 떠오르거나 잠긴다(0x0043DA81). 막 잠겼으면
        // 아래 MonsterHidWord 가 그때부터 한마디씩 붙는다.
        if (_player is { } admiral)
            _battle.TurnMonster(admiral.AbilityOf(Ability.Luck), admiral.AbilityOf(Ability.Mind));

        if (_battle.Wind != windBefore) Say(_battle.WindNotice());
        if (_battle.Delegated) return;      // 맡긴 동안은 재촉도 안내도 없다

        Say(_battle.OrderPrompt());
        if (_battle.MonsterHidWord() is { Length: > 0 } hid) Say(hid);

        // 지난 턴에 부딪혀 이번 턴에 못 움직이는 배만 남았으면 그대로 다음 계획으로 넘어간다.
        if (_battle.Ships.Where(s => s.Mine && s.CanAct).All(s => s.Stuck))
            Say("충돌 영향으로 다음 지시를 받을 때까지 이동할 수 없습니다.");
    }

    private void Surrender()
    {
        if (_running) return;
        if (!ConfirmDialog.Ask(this, "항복하겠습니까?", face: _face)) return;
        Result = Outcome.Surrendered;
        // 항복은 게임에 없는 앱 차림표라 도망(0x00435ABF)처럼 되쓴다 — 잃은 배·빼앗긴 배는 함대에서 빠진다.
        WriteBack(Result);
        _settle?.Invoke(this, new Report(Result, _battle.EnemyDowned, _battle.EnemyCaptured));
        CheckCrew();
        Close();
    }

    /// <summary>
    /// 판이 끝났다 — <b>먼저 빠진 기함</b>이 끝을 정한다(<c>0x004350F0</c>).
    /// </summary>
    /// <remarks>
    /// <code>
    ///   내 기함 격침·나포  소리 0x4A · 9초 · 적장 비웃음 rand(5)                         → 패배(GAME OVER)
    ///   적 기함 격침·나포  소리 0x4D · 5초 · 부관 rand(5) · 적장 rand(5)
    ///                      → 되찾은 배 · 레코드 되쓰기 · 명성·전리품 · 들임 차림표 · 편성 창  → 승리
    ///   내 기함 퇴각       부관 rand(5) → 되쓰기(잃은 배·빼앗긴 배는 뺀다) · 악명 · 편성 창  → 도망
    ///   적 기함 퇴각       소리 0x4D · 5초 · 부관 rand(5) · 명성
    ///                      → 되찾은 배(레코드는 싸움 전 값) · 나포선이 있으면 들임 차림표   → 적이 달아남
    /// </code>
    /// 값 치르기(<see cref="_settle"/>)와 들임·편성 창을 판 위에서 돌리고 닫는다 — 게임도 판을 닫기 전에 띄운다.
    /// 짐 창(<c>0x004879A0</c>, 빼앗은 보급품·교역품)은 들임 차림표 뒤에 뜬다(<see cref="LootDialog"/>). 음악을 끄고 켜는 것은 없다.
    /// </remarks>
    private void Finish()
    {
        Result = OutcomeOf(_battle);
        var report = new Report(Result, _battle.EnemyDowned, _battle.EnemyCaptured);

        switch (Result)
        {
            case Outcome.Defeated:
                _sfx?.Play(LosePart);
                Wait(Tick * LoseTicks);
                ConfirmDialog.Tell(this, _battle.TauntWord(), BattleTitle, _foeFace);
                _settle?.Invoke(this, report);
                break;

            case Outcome.Won:
                _sfx?.Play(WinPart);
                Wait(Tick * WinTicks);
                // 괴물은 문구가 따로다(0x004352DC).
                Say(_battle.Monster ? _battle.MonsterWonWord() : _battle.WonWord(_foe.Name));
                ConfirmDialog.Tell(this, _battle.BeatenWord(), BattleTitle, _foeFace);
                WriteBack(Result);
                _settle?.Invoke(this, report);
                Muster(Result);
                CheckCrew();
                break;

            case Outcome.EnemyRetreated:
                _sfx?.Play(WinPart);
                Wait(Tick * WinTicks);
                Say(_battle.FoeFledWord(_foe.Name));
                _settle?.Invoke(this, report);
                WriteBack(Result);
                Muster(Result);
                CheckCrew();
                break;

            default:
                Say(_battle.EscapedWord(_foe.Name));
                WriteBack(Result);
                _settle?.Invoke(this, report);
                CheckCrew();
                break;
        }

        Close();
    }

    /// <summary>
    /// 아군 배를 레코드에 되쓴다(<c>0x004350F0</c> 10.1~10.3).
    /// </summary>
    /// <remarks>
    /// <code>
    ///   가라앉은 배(1)       함대에서 뺀다
    ///   빼앗긴 배(2)         승리 — 「빼앗긴 배를 되찾았습니다.」 한 번, 승원 0 으로 되쓴다
    ///                        적 기함 퇴각 — 「빼앗긴 배를 되찾았습니다」 한 번, 레코드는 싸움 전 값 그대로(원본대로)
    ///                        내 기함 퇴각 — 함대에서 뺀다(빼앗긴 배는 잃는다)
    ///   그 밖(퇴각 3·떠 있음) 내구 · 승원 · 대포 문수를 되쓴다
    ///   탄약 = 판의 탄약 / 10
    /// </code>
    /// 추진력은 판에서 안 깎아(포격의 추진력 깎기를 안 옮김) 되쓸 것이 없다.
    /// </remarks>
    private void WriteBack(Outcome outcome)
    {
        if (_player is not { } player || _fleet.Count == 0) return;

        bool won = outcome == Outcome.Won;
        bool fled = outcome is Outcome.Escaped or Outcome.Surrendered;

        var before = player.CrewShares.ToList();
        var crew = new Dictionary<Support.Local.Models.Ship, int>();
        for (int i = 0; i < player.Ships.Count; i++) crew[player.Ships[i]] = before.ElementAtOrDefault(i);

        bool told = false;
        var lost = new List<Support.Local.Models.Ship>();
        foreach (var (slot, record) in _fleet)
        {
            if (slot.State == SeaBattle.ShipState.Sunk
                || (slot.State == SeaBattle.ShipState.Captured && fled))
            {
                lost.Add(record);
                continue;
            }
            if (slot.State == SeaBattle.ShipState.Captured)
            {
                if (!told)
                {
                    Say(SeaBattle.RecoveredWord(won));
                    told = true;
                }
                slot.Crew = 0;
                if (!won) continue;
            }
            // 판에서 깎인 <b>추진력</b>도 배에 되쓴다(0x0043570D 가 0x0044C810 을 부른다) —
            // 뱃전으로 부딪히면 그 배는 그 뒤로도 느리다.
            record.SetSpeed(slot.Speed);
            record.SetHp(slot.Hp);
            if (record.Gun >= 0 && slot.Guns != record.Guns) record.Load(record.Gun, slot.Guns);
            crew[record] = slot.Crew;
        }

        foreach (var ship in lost)
        {
            player.Release(ship);
            crew.Remove(ship);
        }

        var shares = player.Ships.Select(s => crew.GetValueOrDefault(s)).ToList();
        player.SetCrew(shares.Sum());
        player.SetCrewShares(shares);
        player.SetSupply(SupplyKind.Ammo, Math.Max(0, _battle.Ammo) / 10);
    }

    /// <summary>
    /// 나포선 들임(<c>0x00434D30</c>) — 적 칸 가운데 상태 2 또는 떠 있는 배가 후보다.
    /// </summary>
    /// <remarks>
    /// 적 기함을 격침·나포하면 <b>살아 있는 호위선까지</b> 모두 후보다. 적 기함이 퇴각했으면 나포해 둔 배가 하나라도
    /// 있어야 차림표가 뜨고, 그때도 산 호위선이 함께 후보가 된다. 이름은 선체 이름, 승원 0, 내구·대포는 판 끝 값이다.
    /// </remarks>
    private void Muster(Outcome outcome)
    {
        if (_player is not { } player || _fleet.Count == 0) return;
        var enemies = _battle.Ships.Where(s => !s.Mine).ToList();
        if (outcome == Outcome.EnemyRetreated && enemies.All(s => s.State != SeaBattle.ShipState.Captured)) return;

        var prizes = enemies
            .Where(s => s.State is SeaBattle.ShipState.Captured or SeaBattle.ShipState.Afloat)
            .Select(PrizeOf)
            .ToList();
        if (prizes.Count == 0) return;
        PrizeFleetMenu.Run(this, player, prizes);

        // 편성 뒤에 짐 창이 뜬다(0x00434D30 → 0x004879A0). 괴물과의 판에는 없다. 빼앗는 양은 편입과
        // 상관없이 잡은 배 전부로 센다 — 빈 용량(포탑 뺀 적재량)과 선체 중량 한도의 합이다.
        if (_battle.Monster || _game is not { } game) return;
        int volume = prizes.Sum(s => s.UsableCapacity), weight = prizes.Sum(s => s.Tonnage);
        int pool = BattleLoot.PoolOf(volume, weight);
        var loot = game.CityRows is { } cities && game.Trade is { } trade && game.Goods is { } goods
            ? BattleLoot.GoodsOf(_foe.Leader?.Nation ?? -1, volume, weight, pool, cities, trade, goods, _random)
            : null;
        LootDialog.Show(this, player, game.Goods, game.CityName, pool, loot);
    }

    /// <summary>빼앗은 적 칸 하나를 함대 배로 — 선체표 선체(<see cref="Hull.FromTable"/>), 이름은 선체 이름.</summary>
    private static Support.Local.Models.Ship PrizeOf(SeaBattle.Ship s)
    {
        var hull = Hull.FromTable(s.Art);
        var stats = new Support.Local.Models.Ship.Stats(
            MaxHp: Math.Max(1, s.MaxHp), Speed: Math.Max(1, s.Speed), Capacity: Math.Max(1, s.Cargo),
            Tonnage: hull.Tonnage, Crew: Math.Max(1, s.MinCrew), Turrets: Math.Max(hull.Guns, s.Guns),
            Gun: s.Gun, Guns: s.Guns, Sails: [.. s.Sails]);
        return new Support.Local.Models.Ship(hull, s.Hp, stats, s.HullName);
    }

    /// <summary>필요승원이 모자란 배가 있으면 편성 창(<c>0x004AC310</c>)을 연다.</summary>
    private void CheckCrew()
    {
        if (_player is not { } player || _fleet.Count == 0 || player.Ships.Count == 0) return;
        var shares = player.CrewShares;
        if (player.Ships.Where((s, i) => shares.ElementAtOrDefault(i) < Player.NeedCrewOf(s)).Any())
            CrewShareDialog.Show(this, player);
    }

    /// <summary>상태 1(격침)·2(나포/승원 0) — 판에서 잃은 배.</summary>
    private static bool Down(SeaBattle.Ship s) =>
        s.State is SeaBattle.ShipState.Sunk or SeaBattle.ShipState.Captured;

    /// <summary>
    /// 판의 끝을 가른다. 기함이 먼저 빠진 쪽을 보고, 기함이 안 빠졌는데 한 편이 다 떴으면 그 편으로 친다.
    /// </summary>
    private static Outcome OutcomeOf(SeaBattle battle)
    {
        if (battle.FirstFlagOut is { } flag)
        {
            bool lost = Down(flag);
            return flag.Mine
                ? lost ? Outcome.Defeated : Outcome.Escaped
                : lost ? Outcome.Won : Outcome.EnemyRetreated;
        }
        if (battle.AllEnemyGone)
            return battle.Ships.Any(s => !s.Mine && Down(s))
                ? Outcome.Won : Outcome.EnemyRetreated;
        return battle.Ships.Any(s => s.Mine && s.State == SeaBattle.ShipState.Retreated)
            ? Outcome.Escaped : Outcome.Defeated;
    }

    // ── 포격 연출 — 0x004384E8 ────────────────────────────────────────────

    /// <summary>
    /// 한 번의 포격을 그린다 — 발마다 포연·포탄, 맞으면 폭발, 빗나가면 물기둥, 끝에 피해 숫자, 가라앉으면 격침 소리.
    /// </summary>
    /// <remarks>
    /// <code>
    ///   쏠 때    소리 0x29 · 포연 blast-15~17 · 포탄(dot 8x8)이 아홉 걸음에 과녁까지
    ///   맞으면   소리 0x2A · 폭발 blast-00~02 (큰 한 방이면 blast-06~08)
    ///   빗나가면 소리 0x2B · 과녁 곁 칸(rand 4) 에 물기둥 mark-06~08
    ///   피해 숫자 파트 20 의 24x24 — 일의 자리 +0x50 · 십 +0x38 · 백 +0x20, 다섯 박자
    ///   격침     소리 0x2E
    /// </code>
    /// </remarks>
    private void Animate(SeaBattle.Volley volley)
    {
        var (sx, sy) = ScreenOf(volley.Shooter.X, volley.Shooter.Y);
        var (tx, ty) = ScreenOf(volley.Target.X, volley.Target.Y);
        var rng = Random.Shared;

        foreach (var shot in volley.Shots)
        {
            _sfx?.Play(FirePart);

            // 포연 세 장과 포탄이 날아가는 걸음을 함께 흘린다.
            var ball = Sprite(_art.Path_("dot-00"), sx + 20, sy + 12, 8, 8);
            for (int step = 0; step <= BallSteps; step++)
            {
                if (step % 3 == 0 && step / 3 < 3)
                    Flash(_art.Path_($"blast-{15 + step / 3:D2}"), sx, sy - 8, 48, 48);
                if (ball != null)
                {
                    Canvas.SetLeft(ball, sx + 20 + (tx - sx) * step / (double)BallSteps);
                    Canvas.SetTop(ball, sy + 12 + (ty - sy) * step / (double)BallSteps);
                }
                Wait(BallStepTime);
            }
            if (ball != null) _fx.Children.Remove(ball);

            if (shot.Hit)
            {
                _sfx?.Play(HitPart);
                int first = shot.Big ? 6 : 0;
                for (int f = 0; f < 3; f++)
                {
                    var blast = Sprite(_art.Path_($"blast-{first + f:D2}"), tx, ty - 8, 48, 48);
                    Wait(FxFrame);
                    if (blast != null) _fx.Children.Remove(blast);
                }
            }
            else
            {
                _sfx?.Play(MissPart);
                var (nx, ny) = SeaBattle.Step(volley.Target.X, volley.Target.Y, rng.Next(SeaBattle.Ways));
                var (wx, wy) = SeaBattle.OnBoard(nx, ny) ? ScreenOf(nx, ny) : (tx, ty);
                for (int f = 0; f < 3; f++)
                {
                    var splash = Sprite(_art.Path_($"mark-{6 + f:D2}"), wx + 8, wy - 4, 32, 32);
                    Wait(FxFrame);
                    if (splash != null) _fx.Children.Remove(splash);
                }
            }

            // <b>피해 숫자는 발마다 바로 뜬다</b>(0x00437330 이 발 고리 <b>안</b>에 있다).
            // 세 발을 다 쏘고 합을 한 번 찍었더니 마지막에만 숫자가 떴다.
            Wait(FxFrame * 2);
            if (shot.Hit && shot.Damage > 0) ShowNumbers((volley.Target, shot.Damage));
        }

    }

    /// <summary>
    /// 숫자(<c>0x00437330</c>) — 배마다 그 배 위에 파트 20 의 24x24 로 찍는다. 일·십·백 자리 x +0x50·+0x38·+0x20,
    /// 앞 0 은 안 찍는다. 값이 음수인 줄은 건너뛴다(<c>−1</c> 자리).
    /// </summary>
    private void ShowNumbers(params (SeaBattle.Ship Ship, int Value)[] rows)
    {
        int[] offsets = [0x50, 0x38, 0x20];
        var digits = new List<Image>();
        foreach (var (ship, value) in rows)
        {
            if (value < 0) continue;
            var (sx, sy) = ScreenOf(ship.X, ship.Y);
            int[] places = [value % 10, value / 10 % 10, value / 100 % 10];
            for (int p = 0; p < 3; p++)
            {
                if (p > 0 && value < (p == 1 ? 10 : 100)) break;
                if (Sprite(_art.Path_($"digit-{places[p]:D2}"), sx - 0x38 + offsets[p], sy - 24, 24, 24) is { } digit)
                    digits.Add(digit);
            }
        }
        if (digits.Count == 0) return;
        Wait(FxFrame * 5);
        foreach (var digit in digits) _fx.Children.Remove(digit);
    }

    /// <summary>
    /// 불꽃 48x48 석 장(<c>+0x270</c> 묶음, 오프셋 <c>0x900</c> 씩)을 그 자리들에 한 장씩 올린다 — 장 사이 기다림은
    /// 원본에 없어 한 번 화면 올림을 <see cref="FxFrame"/> 로 어림했다.
    /// </summary>
    private void Blast(int first, params (double X, double Y)[] at)
    {
        for (int f = 0; f < 3; f++)
        {
            var shown = new List<Image>();
            foreach (var (x, y) in at)
                if (Sprite(_art.Path_($"blast-{first + f:D2}"), x, y, 48, 48) is { } image) shown.Add(image);
            Wait(FxFrame);
            foreach (var image in shown) _fx.Children.Remove(image);
        }
    }

    /// <summary>그 배 칸의 48x48 장 자리 — <c>x = X*32 + 0x38</c>, <c>y = Y*32 + (X 짝수 ? 16 : 0) + 0x18</c>.</summary>
    private static (double X, double Y) BlastAt(SeaBattle.Ship ship)
    {
        var (sx, sy) = ScreenOf(ship.X, ship.Y);
        return (sx, sy - 8);
    }

    // ── 가까운 싸움 연출 — SeaBattle.IStage ───────────────────────────────

    void SeaBattle.IStage.Moved() => Redraw();

    /// <remarks>
    /// 원본 알림(<c>0x0049E3E0(0, "해전", 글)</c>)은 얼굴 없는 게임 창이다. 적끼리 부딪힌 것은 원본 갈래를 다 못 짚어
    /// (「아군끼리」 말이 적에게 뜨게 된다) 아군이 낄 때만 알린다 — 소리는 적이 끼면 난다.
    /// </remarks>
    void SeaBattle.IStage.Crash(SeaBattle.Ship mover, SeaBattle.Ship hit, bool friendly)
    {
        Redraw();
        if (!friendly) _sfx?.Play(CrashPart);
        if (mover.Mine || hit.Mine) ConfirmDialog.Tell(this, SeaBattle.CrashWord(mover, hit), BattleTitle);
    }

    void SeaBattle.IStage.HullLoss(SeaBattle.Ship mover, int moverLoss, SeaBattle.Ship hit, int hitLoss)
    {
        Wait(Tick * 2);
        ShowNumbers((mover, moverLoss), (hit, hitLoss));
        Wait(Tick * 2);
    }

    /// <remarks>소리 0x2D → 150ms → 맞은편 칸 blast-09~11 → 100ms → 숫자(제 배 위에 제 잃은 승원) → 100ms.</remarks>
    void SeaBattle.IStage.Melee(SeaBattle.Ship mover, SeaBattle.Ship target, int moverLoss, int targetLoss)
    {
        Redraw();
        _sfx?.Play(MeleePart);
        Wait(Tick * 3);
        Blast(9, BlastAt(target));
        Wait(Tick * 2);
        ShowNumbers((mover, moverLoss), (target, targetLoss));
        Wait(Tick * 2);
    }

    void SeaBattle.IStage.Ignite(SeaBattle.Ship target)
    {
        _sfx?.Play(IgnitePart);
        Blast(3, BlastAt(target));
        UpdateFlames();
    }

    /// <remarks>
    /// 소리 0x1E(파트 2) → 두 배 <b>가운데</b>에 blast-09~11 — <c>x = ((X쏜+X과녁)/2 + 2)*32</c>,
    /// <c>y = ((Y쏜+Y과녁)/2 + 1)*32 + 두 배 짝수X 밀림의 평균</c> → 숫자 둘. 말 창은 없다.
    /// </remarks>
    void SeaBattle.IStage.Gunfight(SeaBattle.Ship shooter, SeaBattle.Ship target, int shooterLoss, int targetLoss)
    {
        Redraw();
        _sfx?.Play(GunfightPart);
        Wait(Tick);
        int mx = (shooter.X + target.X) / 2, my = (shooter.Y + target.Y) / 2;
        double shift = (((shooter.X & 1) == 0 ? 16 : 0) + ((target.X & 1) == 0 ? 16 : 0)) / 2.0;
        Blast(9, ((mx + 2) * CombatArt.Cell, (my + 1) * CombatArt.Cell + shift));
        Wait(Tick);
        ShowNumbers((shooter, shooterLoss), (target, targetLoss));
        Wait(Tick);
    }

    void SeaBattle.IStage.Mine()
    {
        Say(SeaBattle.MineWord);
        _player?.Drop(SeaBattle.MineItem);          // 한 번 쓰면 그 칸이 빈다
    }

    void SeaBattle.IStage.RapidFire() => Say(SeaBattle.RapidFireWord);

    void SeaBattle.IStage.Volley(SeaBattle.Volley volley) => Animate(volley);

    void SeaBattle.IStage.Notice(string text) => ConfirmDialog.Tell(this, text, BattleTitle);

    /// <remarks>
    /// 소리 0x2E → 150ms → 배 칸마다 blast-12~14 → 기함 아닌 배마다 격침 말 → 판을 다시 그려 배가 사라진다.
    /// 배가 가라앉거나 뒤집히는 장은 없다(원본도 불꽃 석 장뿐이다).
    /// </remarks>
    void SeaBattle.IStage.Sink(IReadOnlyList<SeaBattle.Ship> ships)
    {
        _sfx?.Play(SinkPart);
        Wait(Tick * 3);
        Blast(12, ships.Select(BlastAt).ToArray());
        foreach (var ship in ships.Where(s => !s.Flagship)) Say(_battle.SinkWord(ship));
        Redraw();
    }

    void SeaBattle.IStage.Captured(SeaBattle.Ship ship)
    {
        Say(_battle.CapturedWord(ship));
        Redraw();
    }

    /// <remarks>제목·얼굴 없는 두 줄 고르기(<c>0x004878A0</c>). 「대기」·닫기는 아무 일도 없다.</remarks>
    bool SeaBattle.IStage.AskCapture(SeaBattle.Ship target) =>
        ChoiceDialog.Pick(this, "", SeaBattle.CaptureRows) == 0;

    bool SeaBattle.IStage.OfferDuel()
    {
        if (ChoiceDialog.Pick(this, "", SeaBattle.DuelRows) != 0) return false;
        ConfirmDialog.Tell(this, _battle.DuelTakenWord(), BattleTitle, _foeFace);
        return true;
    }

    bool SeaBattle.IStage.Challenged()
    {
        Say(_battle.ChallengeWord(_foe.Name));
        bool yes = ChoiceDialog.Pick(this, "", SeaBattle.ChallengeRows) == 0;
        ConfirmDialog.Tell(this, yes ? _battle.AcceptedWord() : _battle.RefusedWord(), BattleTitle, _foeFace);
        return yes;
    }

    bool? SeaBattle.IStage.Duel()
    {
        var won = _duel?.Invoke(this);
        Redraw();
        return won;
    }

    void SeaBattle.IStage.Burn(SeaBattle.Ship ship)
    {
        Wait(Tick * 2);
        ShowNumbers((ship, SeaBattle.BurnDamage));
        Wait(Tick * 2);
    }

    void SeaBattle.IStage.BeatDone(int beat)
    {
        Redraw();
        Wait(StepSpan);
    }

    /// <summary>연출 층에 그림 한 장을 올린다. 못 읽으면 null.</summary>
    private Image? Sprite(string? path, double x, double y, double w, double h)
    {
        if (Bitmap(path) is not { } source) return null;
        var image = new Image { Source = source, Width = w, Height = h };
        RenderOptions.SetBitmapScalingMode(image, GameUi.SpriteScaling);
        Canvas.SetLeft(image, x);
        Canvas.SetTop(image, y);
        _fx.Children.Add(image);
        return image;
    }

    /// <summary>그림 한 장을 한 참 띄웠다가 걷는다.</summary>
    private void Flash(string? path, double x, double y, double w, double h)
    {
        var image = Sprite(path, x, y, w, h);
        Wait(FxFrame);
        if (image != null) _fx.Children.Remove(image);
    }

    private static void Wait(TimeSpan span)
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer(span, DispatcherPriority.Normal,
                                        (_, _) => frame.Continue = false, Dispatcher.CurrentDispatcher);
        try { Dispatcher.PushFrame(frame); }
        finally { timer.Stop(); }
    }

    private static readonly Dictionary<string, BitmapSource> Cache = [];

    private static BitmapSource? Bitmap(string? path)
    {
        if (path == null) return null;
        if (Cache.TryGetValue(path, out var hit)) return hit;
        var bmp = new BitmapImage();
        bmp.BeginInit();
        bmp.UriSource = new Uri(System.IO.Path.GetFullPath(path));
        bmp.CacheOption = BitmapCacheOption.OnLoad;
        bmp.EndInit();
        bmp.Freeze();
        Cache[path] = bmp;
        return bmp;
    }

    private static void Put(Canvas board, string? path, double x, double y, double w, double h, int z)
    {
        if (Bitmap(path) is not { } source) return;
        var image = new Image { Source = source, Width = w, Height = h, IsHitTestVisible = false };
        RenderOptions.SetBitmapScalingMode(image, GameUi.SpriteScaling);
        Canvas.SetLeft(image, x);
        Canvas.SetTop(image, y);
        Panel.SetZIndex(image, z);
        board.Children.Add(image);
    }

    /// <summary>
    /// 판을 게임 창에 맞춰 키운다 — 원본 800x600 이 게임 창의 대부분을 채우게, 넘치지 않는 만큼.
    /// </summary>
    private static double ZoomFor(Window owner)
    {
        var stage = GameUi.RootOf(owner);
        double w = stage.ActualWidth > 0 ? stage.ActualWidth : SystemParameters.WorkArea.Width;
        double h = stage.ActualHeight > 0 ? stage.ActualHeight : SystemParameters.WorkArea.Height;
        double fit = Math.Min(w * 0.92 / ScreenWidth, (h * 0.92 - 40) / ScreenHeight);
        return Math.Max(1.0, Math.Floor(fit * 4) / 4);
    }

    // ── 열기 ──────────────────────────────────────────────────────────────

    /// <summary>
    /// 응전해 해전을 벌인다. 그림을 못 읽었으면 그렇다고 이르고 도망친 것으로 친다.
    /// </summary>
    /// <param name="face">말을 거는 얼굴 — 부관, 없으면 뱃사람.</param>
    /// <param name="seaWind">
    /// 함대 자리의 바다 바람(16방위, 세기). 원본은 이것으로 풍향·세기를 매긴다(<c>0x00441F1C</c>).
    /// 모르면 굴린다.
    /// </param>
    /// <param name="foeFace">적장 얼굴 — 끝맺음·일기토 말에 쓴다.</param>
    /// <param name="duel">결투 판을 여는 쪽. 이기면 true — 없으면 일기토가 안 열린다.</param>
    public static Outcome Fight(Window owner, Player player, in Enemy foe, Random rng, uint[]? face,
                                (int Dir, int Strength)? seaWind = null, SoundBank? sfx = null,
                                uint[]? foeFace = null, Func<Window, bool?>? duel = null,
                                BgmPlayer? bgm = null, bool monster = false, Engine.Game? game = null) =>
        Engage(owner, player, foe, rng, face, seaWind, sfx, foeFace, duel: duel, bgm: bgm,
               monster: monster, game: game).Outcome;

    /// <summary>
    /// 해전을 벌이고 끝의 알맹이를 낸다. 그림을 못 읽었으면 도망친 것으로 치고 값 치르기는 안 부른다.
    /// </summary>
    /// <param name="foeFace">적장 얼굴 — 끝맺음에서 적장이 말할 때 쓴다.</param>
    /// <param name="settle">판을 닫기 직전에 판 창을 주인으로 불러 값을 치르게 한다.</param>
    public static Report Engage(Window owner, Player player, in Enemy foe, Random rng, uint[]? face,
                                (int Dir, int Strength)? seaWind = null, SoundBank? sfx = null,
                                uint[]? foeFace = null, Action<Window, Report>? settle = null,
                                Func<Window, bool?>? duel = null, BgmPlayer? bgm = null,
                                bool monster = false, Engine.Game? game = null, int[]? hulls = null)
    {
        var art = CombatArt.Open();
        if (art == null)
        {
            NoticeDialog.Show(owner, $"해전 그림을 못 읽었다 — {CombatArt.LastError}");
            return new Report(Outcome.Escaped, 0, 0);
        }

        var battle = seaWind is { } w
            ? SeaBattle.FromSeaWind(rng, w.Dir, w.Strength)
            : new SeaBattle(rng, rng.Next(SeaBattle.Ways), rng.Next(3) + 1);

        // 바다 괴물과의 판은 달아나는 길이 없다(원본 판 종류 0).
        battle.Monster = monster;
        // 괴물이 누구인지로 이동력이 갈린다(0x00434CB5) — 적장 번호가 곧 괴물 번호다.
        battle.MonsterPerson = monster ? foe.Leader?.Id ?? -1 : -1;

        // 제독 값(0x00441D8A) — 제독·부관(부하 첫 자리) 가운데 큰 값이다. 능력은 +1, 기능은 그대로,
        // 운세칸[0] 은 제독 것(0x00477FE0). 무력도 +1 이다(예전에는 +1 을 안 먹였다).
        var mate = player.MateInfoOf(player.MateAt(0));
        int Best(int mine, int? theirs) => Math.Max(mine, theirs ?? 0);
        int SkillOf(int k) => player.LevelOf(Skill.Names[k]);
        battle.MineSide = new SeaBattle.Side(
            Gunnery: Best(SkillOf(Skill.Gunnery), mate?.Gunnery),
            Might: Best(player.AbilityOf(Ability.Might), mate?.Might) + 1,
            Defense: Best(player.AbilityOf(Ability.Luck), mate?.Luck) + 1,
            Mind: Best(player.AbilityOf(Ability.Mind), mate?.Mind) + 1,
            Charm: Best(player.AbilityOf(Ability.Charm), mate?.Charm) + 1,
            Sword: Best(SkillOf(Skill.Sword), mate?.Sword),
            Shooting: Best(SkillOf(Skill.Shooting), mate?.Shooting),
            Fortune: FleetRaid.AdmiralFortuneOf(player)[0]);
        // 적장 값(0x00440F23) — 적장 한 사람 값 그대로다(능력은 이미 +1 된 날값).
        var leader = foe.Leader ?? Encounter.CaptainOf(Encounter.PirateLeader);
        battle.EnemySide = new SeaBattle.Side(leader.Gunnery, leader.Might, leader.Luck,
                                              leader.Mind, leader.Charm, leader.Sword, leader.Shooting,
                                              leader.FortuneAt(0));
        battle.LeaderFortune = leader.FortuneAt(3);

        // 위임했을 때 아군 AI 가 보는 부관 성미 칸 0(0x0043B7B1) — 부관을 인물 밑표에서 찾아
        // 얼굴·혈액형·나라로 센다. 못 찾으면 1(여느 판정)로 둔다.
        battle.MateTemper = MateTemperOf(game, player.MateAt(0));
        // 탄약 = 함대 보급품 탄약 x 10(볼트 85). 잠수폭탄은 소지품 칸마다 굴린다(볼트 94 3.1).
        battle.Ammo = player.SupplyOf(SupplyKind.Ammo) * 10;
        battle.Mines = player.Items.Count(id => id == SeaBattle.MineItem);
        // 속사포는 판을 열 때 한 번 굴려 정해진다(0x00441EA5) — 먹으면 그 판 내내 여덟 발이다.
        battle.ArmRapidFire(player.Items.Count(id => id == SeaBattle.RapidFireItem));
        var ours = new List<(SeaBattle.Ship, Support.Local.Models.Ship)>();

        // 기함이 0번이다. 승원은 바다 커맨드 「편성」이 나눠 둔 배마다의 몫이고, 자리는 「대열」이 정한다.
        var ships = player.Ships;
        int flag = Math.Clamp(player.Flagship, 0, Math.Max(0, ships.Count - 1));
        var order = ships.Select((s, i) => (s, i)).OrderBy(p => p.i == flag ? 0 : 1).Take(SeaBattle.PerSide).ToList();
        var shares = player.CrewShares;
        for (int slot = 0; slot < order.Count; slot++)
        {
            var (ship, at) = order[slot];
            var placed = battle.Place(true, slot, ship.Name, ship.Speed, [.. ship.Sails],
                         art: ship.Hull.GameId,     // SCOMBAT 파트 5+선체 번호(0x00442D93)
                         hp: ship.Hp, crew: shares.ElementAtOrDefault(at), minCrew: ship.Crew,
                         gun: ship.Guns > 0 ? ship.Gun : -1, figurehead: ship.Figurehead,
                         formation: player.Formation,
                         hullName: ship.Hull.Name, cargo: ship.Capacity, guns: ship.Guns, maxHp: ship.MaxHp);
            ours.Add((placed, ship));
        }

        // 배가 한 척도 없으면(타이틀의 미니게임에서 여는 모의해전) 연습용 카라벨 한 척을 띄운다.
        if (order.Count == 0)
        {
            var practice = Hull.Cheapest;
            battle.Place(true, 0, practice.Name, practice.Speed,
                         [Support.Local.Models.Ship.Lateen, Support.Local.Models.Ship.Lateen, 0],
                         art: practice.GameId, hp: practice.Hp,
                         crew: practice.Crew + 20, minCrew: practice.Crew, gun: -1,
                         hullName: practice.Name, cargo: practice.Capacity);
        }

        // 적 배 — 적장의 나라와 그 해로 선체를, 적장 능력으로 척수·승원·대포를 짓는다(0x00440D90).
        // 이름은 게임이 일본 군함명 자리 채움(0x549A34)을 굴리는데 화면에는 선체 이름이 찍혀 무리 이름을 쓴다.
        // 적의 대열은 굴린다(0x004421F6 의 rand(8)).
        int enemyFormation = rng.Next(SeaBattle.FormationCount);
        var fleet = EnemyFleet.Build(leader, player.Date.Year, rng, hulls);
        for (int slot = 0; slot < fleet.Count; slot++)
        {
            var e = fleet[slot];
            battle.Place(false, slot, foe.Name, e.Speed, e.Sails,
                         art: e.Hull, hp: e.Hp, crew: e.Crew, minCrew: e.MinCrew, gun: e.Gun,
                         hullName: e.HullName, cargo: e.Capacity, guns: e.Guns,
                         formation: enemyFormation, maxHp: e.Hp);
        }

        var dialog = new SeaCombatDialog(battle, art, foe, face, ZoomFor(owner), sfx, foeFace, settle,
                                         player, ours, duel, game, rng)
        {
            Owner = owner,
        };
        // 싸우는 동안은 전투 곡(28)이 돈다 — 육상전과 같은 곡이다. 끝나면 돌던 곡으로 되돌린다.
        int was = bgm?.Track ?? -1;
        bgm?.Play(BgmPlayer.BattleTrack);
        try { dialog.ShowDialog(); }
        finally { if (was >= 0) bgm?.Play(was); }

        return new Report(dialog.Result, battle.EnemyDowned, battle.EnemyCaptured);
    }

    /// <summary>해전 연습 — 개발용 창에서 연다. 붙는 무리는 조우 굴림으로 짓는다.</summary>
    public static void Play(Window owner, Player player, Random rng) =>
        Fight(owner, player, Encounter.Roll(rng), rng, null);
}
