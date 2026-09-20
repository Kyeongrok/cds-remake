using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CdsHelper.Game.Local.Helpers;
using CdsHelper.Support.Local.Models;

namespace CdsHelper.Game.UI.Views;

/// <summary>
/// 미니 게임 「코인 게임」(천칭 퍼즐) 화면.
/// </summary>
/// <remarks>
/// 게임의 <c>0x004531F0</c> 이고, 규칙은 <see cref="CoinPuzzle"/> 에 모아 두었다.
///
/// <b>그림은 게임 것 그대로다</b> — BALANCE.CDS 에서 뽑아 <c>asset/minigame</c> 에 둔다
/// (<c>tools/extract_minigame_art.py</c>). 자리 표가 EXE 에 셋으로 나뉘어 있다.
/// <code>
///   0x00549E10  파트 0 — 금화 32x32 <b>스물여덟 장</b>
///                        0~12 번호 새긴 1~13 · 13~25 같은 열셋(<b>손이 얹혔을 때</b>)
///                        26~27 납작하게 누운 하나와 그 손 얹힌 벌
///   0x00549E20  파트 1 — 기둥 64x160 · 대 176x16 · 나무 천칭 192x144 둘
///                        · 금 천칭 208x168 셋
///   0x00549E3C  파트 2 — 단추 64x32 셋 · 접시 80x144 둘 · 받침 96x48 · 배경 448x384
/// </code>
/// 자리는 그리는 곳이 그대로 준다.
/// <code>
///   0x00451F91  배경 448x384 를 (8, 8) 에            ; 창이 464x400
///   0x00452599  금 천칭 208x168 을 (39, 49) 에
///   0x00452709  단추 64x32 를 (112, 240) 에
///   0x0045274A  다음 단추를 (192, 240) 에
/// </code>
/// 배경을 (8, 8) 에 놓는다는 것이 곧 <b>게임 좌표에서 여덟을 빼면 우리 좌표</b>라는
/// 말이다 — 464x400 창에 8점 테를 두르고 그 안이 448x384 다. 아래 자리들은 다 그렇게
/// 옮겨 적었다.
/// 배경에 <b>오른쪽 흰 테 칸</b>과 <b>아래 검은 칸</b>이 비어 있다 — 금화를 늘어놓는
/// 데와 자취를 적는 데다. <b>그 둘 말고는 아무것도 얹지 않는다</b> — 게임 화면에 없는
/// 것을 덧대면 그만큼 게임이 아니게 된다.
///
/// <b>천칭은 한 장이 아니라 조각을 겹쳐 세운다.</b> 기둥과 받침을 놓고, 평형이면 곧은
/// 대에 접시 둘을 걸고, 기울면 그 벌(나무 천칭 192x144)로 갈아 끼운다. 자리는 게임
/// 화면을 448x384 로 되돌려 조각마다 맞춰 찾은 것이다.
/// <code>
///   기둥 coin-post   ( 97, 44)  64x160      받침 coin-stand  ( 88, 29)  96x48
///   대   coin-beam   ( 51, 53) 176x16
///   접시 coin-pan-0  ( 27, 65)  80x144      coin-pan-0       (163, 65)  80x144
///   기움 coin-wood-0 ( 33, 41) 192x144      coin-wood-1      ( 29, 41)  192x144
/// </code>
/// <b>접시 두 벌은 좌우가 아니다</b> — <c>0x00452627</c> 을 보면 왼쪽도 오른쪽도
/// <c>0x549E48</c>(coin-pan-0) 한 벌을 쓰고, <c>0x549E4C</c>(coin-pan-1) 는
/// <b>그 접시를 고른 동안</b>만 갈아 끼운다. 좌우로 나눠 걸어 두어 오른 접시가
/// 어두웠다.
/// <b>금 천칭은 안 쓴다</b> — 왼 접시에 얹힌 장식은 다 풀고 난 뒤에 나오는 것이다.
///
/// 금화는 <b>끌어다</b> 접시에 놓는다 — 원본 설명도 「금화 위에서 마우스 왼쪽을 클릭하여
/// 버튼을 누른 체 금화를 이동하면 움직일 수 있습니다」 한 줄뿐이다(<c>0x0053B0C0</c>).
/// <b>끌지 않고 딸깍하면 그 닢을 고른다</b> — 「가짜 금화 선택」이 고른 닢을 쓰고
/// (<c>+0x11C</c>), 고른 닢은 손이 얹힌 벌로 바뀐다. 오른쪽 단추로는 오른 접시에 바로 놓는다.
/// 접시에 올린 금화는 쟁반에서 빠져 접시에 쌓이는데,
/// <b>쟁반의 빈자리는 그대로 둔다</b> — 남은 금화가 앞으로 당겨지지 않는다.
/// </remarks>
internal sealed class CoinPuzzleDialog : InfoDialog
{
    private const int SceneWidth = 448, SceneHeight = 384;

    /// <summary>화면 점 기준 곱. <see cref="GameUi.PixelZoom"/> 이 배율로 나눈다.</summary>
    private const int Zoom = 2;

    /// <summary>천칭 조각들이 놓이는 자리. 게임 화면에서 맞춰 찾은 것이다.</summary>
    private static readonly (int X, int Y) PostAt = (97, 44), StandAt = (88, 29),
                                           BeamAt = (51, 53),
                                           LeftPanAt = (27, 65), RightPanAt = (163, 65);

    /// <summary>기운 벌 둘 — 0 은 왼쪽이 내려간 것, 1 은 오른쪽이 내려간 것.</summary>
    /// <remarks><c>0x004524FE</c> 가 (41, 49) 에, <c>0x0045254C</c> 가 (37, 49) 에 놓는다.</remarks>
    private static readonly (int X, int Y)[] WoodAt = [(33, 41), (29, 41)];

    /// <summary>
    /// 단추 자리. 아는 둘이 <c>0x00452709</c> 의 (112, 240) 과 <c>0x0045274A</c> 의
    /// (192, 240) 이고 — 테 8점을 빼면 104 · 184 다. 간격이 80 이니 첫 단추는 24 다.
    /// </summary>
    private const int ButtonY = 232, ButtonW = 64, ButtonH = 32;
    private static readonly int[] ButtonX = [24, 104, 184];

    /// <summary>
    /// 금화를 늘어놓는 흰 테 칸. <b>배경 그림과 게임 화면을 재어 맞춘 것이다.</b>
    /// </summary>
    /// <remarks>
    /// <c>coin-bg.png</c> 의 흰 테가 가로 <c>271~272</c>·<c>430~431</c>, 세로
    /// <c>16~17</c>·<c>270~271</c> 이라 속이 <c>(273, 18)</c> 에서 157x252 다.
    /// 게임 화면에서 금화가 <b>한 줄에 셋</b>이고, 칸 속을 1 로 보면 금화가 0.199 ·
    /// 간격이 0.292 · 첫 칸이 왼쪽에서 0.062 · 위에서 0.057 이다. 그걸 157·252 에
    /// 옮기면 아래 값이 된다.
    /// </remarks>
    /// <remarks>
    /// 뒤에 <c>0x004527F4</c> 에서 셈을 그대로 읽었다 — 게임은
    /// <c>x = 46*칸 + 0x122</c> · <c>y = 46*줄 + 0x28</c> 이니 테 여덟을 빼면 아래다.
    /// </remarks>
    private const int TrayX = 282, TrayY = 32, TrayStep = 46, TrayPer = 3;

    /// <summary>
    /// 자취를 적는 아래 검은 칸 — <b>줄마다 자리를 짚어</b> 적는다.
    /// </summary>
    /// <remarks>
    /// 예전에는 한 줄을 통째로 글로 이어 붙여 <see cref="StackPanel"/> 에 쌓았더니
    /// 줄이 가운데로 몰려 「2-」·「3-」이 들쭉날쭉했다. 게임은 <c>0x004520F0</c> 부터
    /// <b>자리를 하나하나 대고</b> 찍는다.
    /// <code>
    ///   0x004520F0  "1-" "2-" "3-" 을 x 0x2C, y 0x138·0x14C·0x160 에
    ///   0x004521C5  기울기 표를 왼쪽 x 0x52 · 오른쪽 x 0x190 에
    ///   0x004522FB  왼 접시 번호를 x 0xE6 에서 <b>왼쪽으로</b> 0x12 씩
    ///   0x00452349  오른 접시 번호를 x 0x104 에서 <b>오른쪽으로</b> 0x12 씩
    /// </code>
    /// 테 여덟을 뺀 것이 아래 값이다. 번호는 <b>금화 차례로</b> 도는데 왼쪽은 자리가
    /// 왼쪽으로 가므로 <b>거꾸로 적힌다</b> — 1·2 를 왼 접시에 올리면 「2 1」이다.
    /// </remarks>
    private const int LogX = 36, LogY = 304, LogStep = 20;

    /// <summary>기울기 표를 찍는 두 자리 — 줄의 양 끝이다.</summary>
    private const int LogLeftMark = 74, LogRightMark = 392;

    /// <summary>왼 접시 번호가 <b>끝나는</b> 자리와 오른 접시 번호가 <b>시작하는</b> 자리.</summary>
    private const int LogLeftEnd = 222, LogRightStart = 252;

    /// <summary>번호 한 자리마다의 걸음. 두 자리 수는 여덟 점을 더 먹는다.</summary>
    private const int LogRun = 18, LogWide = 8;

    /// <summary>
    /// 두 접시의 가운데와 <b>금화가 얹히는 높이</b> — 천칭 그림에서 잰 것이다.
    /// </summary>
    /// <remarks>
    /// <c>0x0045283A</c> 의 그리는 셈을 그대로 읽었다 — 평형이면 왼쪽이 <c>(0x32, 0x94)</c>
    /// 오른쪽이 <c>(0xBC, 0x94)</c> 이고 한 닢 올릴 때마다 <c>y</c> 가 여덟씩 준다.
    /// 기울면 거기서 조금씩 밀린다. 테 여덟을 뺀 것이 아래 값이다.
    /// </remarks>
    private static readonly (int X, int Y) LevelLeftPile = (42, 140), LevelRightPile = (180, 140);

    /// <summary>기운 벌에서 금화가 쌓이는 자리 — <c>[기움][0]</c> 왼쪽 · <c>[기움][1]</c> 오른쪽.</summary>
    private static readonly (int X, int Y)[][] WoodPile =
    [
        [(50, 158), (176, 125)],   // 왼쪽이 내려갔다
        [(46, 125), (174, 159)],   // 오른쪽이 내려갔다
    ];

    /// <summary>
    /// 금화 한 닢을 더 얹을 때마다 올라가는 높이.
    /// </summary>
    /// <remarks>
    /// <c>0x00452948</c> 의 <c>add ebx, 8</c> 이 그것이다 — 자리마다 <c>y</c> 를 여덟씩
    /// 뺀다. 납작 금화의 잉크가 열여섯이니 반씩 겹쳐 쌓인다.
    /// </remarks>
    private const int StackRise = 8;


    /// <summary>
    /// 금화를 끌어다 놓는 두 접시의 네모 — 금 천칭 그림 안에서 잰 자리다.
    /// </summary>
    /// <remarks>
    /// 기둥(<c>97~161</c>)을 비켜 좌우만 잡는다. 접시가 기울어도 그 언저리를 벗어나지
    /// 않으므로 줄까지 넉넉히 덮어 둔다.
    /// </remarks>
    private static readonly (int X, int Y, int W, int H) LeftPan = (20, 60, 76, 130);
    private static readonly (int X, int Y, int W, int H) RightPan = (156, 60, 76, 130);

    private readonly CoinPuzzle _game;
    private readonly Canvas _scene = new() { Width = SceneWidth, Height = SceneHeight };

    /// <summary>천칭에서 <b>기울기에 따라 갈아 끼우는</b> 조각들. 기둥·받침은 안 바뀐다.</summary>
    private readonly List<Image> _arm = [];
    private readonly Border[] _coin;
    private readonly Canvas _log = new();

    /// <summary>접시에 쌓아 둔 납작 금화들. 다시 그릴 때마다 걷고 새로 놓는다.</summary>
    private readonly List<Image> _piled = [];

    /// <summary>
    /// 놀이 속 천칭인지(<c>+0x154</c>) — 들어올 때 받은 인자다. 미니 게임(0)은 한 번 틀리면 곧 끝나고
    /// 삯도 없다(<c>0x00450C9C</c> → <c>0x00450D3D</c>).
    /// </summary>
    private readonly bool _stakes;

    /// <summary>다시 하라고 이르는 얼굴 — 부관, 없으면 뱃사람(<c>0x00450D01</c> 의 <c>0x0047CC60(0,0)</c>).</summary>
    private readonly uint[]? _aide;

    private CoinPuzzleDialog(Random rng, bool stakes, uint[]? aide)
    {
        _stakes = stakes;
        _aide = aide;
        _game = new CoinPuzzle(rng);
        _coin = new Border[_game.Coins];

        Lay(Picture("coin-bg.png"), 0, 0, SceneWidth, SceneHeight);

        // 기둥과 받침은 늘 그 자리다. 대와 접시는 기울기마다 갈아 끼운다.
        Lay(Picture("coin-post.png"), PostAt.X, PostAt.Y, 64, 160);
        Lay(Picture("coin-stand.png"), StandAt.X, StandAt.Y, 96, 48);

        // 금화를 오른쪽 칸에 늘어놓는다. 끌어다 접시에 놓고, 딸깍하면 그 닢을 고른다.
        for (int i = 0; i < _game.Coins; i++)
        {
            int coin = i;
            var box = new Border
            {
                Width = 32,
                Height = 32,
                Background = new ImageBrush(Face(coin)) { Stretch = Stretch.Fill },
                Cursor = Cursors.Hand,
            };
            box.MouseLeftButtonDown += (_, e) => Grab(coin, e);
            box.MouseRightButtonUp += (_, e) => { e.Handled = true; Tap(coin, left: false); };

            Canvas.SetLeft(box, TrayX + i % TrayPer * TrayStep);
            Canvas.SetTop(box, TrayY + i / TrayPer * TrayStep);
            _scene.Children.Add(box);
            _coin[i] = box;
        }

        // 단추 셋 — WEIGH · CLEAR · DECIDE.
        Button(0, "coin-button-0.png", DoWeigh);
        Button(1, "coin-button-1.png", () => { _game.Clear(); Sync(); });
        Button(2, "coin-button-2.png", DoDecide);

        // 자취 칸은 판 전체를 덮는 빈 겹이다 — 글자마다 판 좌표로 자리를 짚는다.
        Canvas.SetLeft(_log, 0);
        Canvas.SetTop(_log, 0);
        _log.Width = SceneWidth;
        _log.Height = SceneHeight;
        _log.IsHitTestVisible = false;
        _scene.Children.Add(_log);

        // 끌고 다니는 동안 손끝에 붙어 다니는 금화.
        _ghost.IsHitTestVisible = false;
        _ghost.Visibility = Visibility.Collapsed;
        Panel.SetZIndex(_ghost, 80);
        _scene.Children.Add(_ghost);

        _scene.Background = Brushes.Transparent;
        _scene.MouseLeftButtonDown += (_, e) => e.Handled = true;

        // 집는 순간 판이 손을 잡으므로 뗌은 늘 판에 온다 — 놓는 데는 좌표로 짚는다
        // (부대배치 창과 같은 까닭이다).
        _scene.MouseMove += Drag;
        _scene.MouseLeftButtonUp += Land;

        double zoom = GameUi.PixelZoom(this, Zoom);
        _scene.LayoutTransform = new ScaleTransform(zoom, zoom);

        // 게임은 미니 게임에 밤색 판도 제목도 아래 단추 줄도 안 두른다 — 그림에 금빛
        // 테만 두르고, 할 일은 오른쪽 단추 차림표가 맡는다(성배 퍼즐·미궁 64 와 같다).
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.WidthAndHeight;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        Background = GameUi.Back;
        Content = GameUi.GoldFrame(_scene, Close);
        GameUi.EnableDrag(this, _scene);

        // 오른쪽 단추는 <b>두 가지</b>를 한다 — 접시에 올린 금화를 내리고, 차림표를 편다.
        // 예전에는 내리기만 했다.
        MouseRightButtonUp += (_, e) =>
        {
            _game.Clear();
            Sync();
            GameUi.ContextMenuAt(this, e.GetPosition(this), Commands());
        };
        KeyDown += (_, e) => { if (e.Key is Key.Escape) { _game.Clear(); Sync(); } };

        Sync();
    }

    /// <summary>오른쪽 단추가 부르는 차림표. 예전 아래 단추 줄이 그대로 여기로 왔다.</summary>
    private IReadOnlyList<(string, Action?)> Commands() =>
    [
        ("게임 설명", Explain),
        ("포기한다", AskGiveUp),
        ("게임 복귀", () => { }),   // 차림표만 닫는다
    ];

    /// <summary>단추 하나. 그림은 게임 것을 그대로 쓴다.</summary>
    private void Button(int at, string art, Action run)
    {
        var image = new Image
        {
            Source = Picture(art),
            Width = ButtonW,
            Height = ButtonH,
            Cursor = Cursors.Hand,
        };
        RenderOptions.SetBitmapScalingMode(image, GameUi.SpriteScaling);
        Canvas.SetLeft(image, ButtonX[at]);
        Canvas.SetTop(image, ButtonY);
        image.MouseLeftButtonDown += (_, e) => e.Handled = true;
        image.MouseLeftButtonUp += (_, e) => { e.Handled = true; run(); };
        _scene.Children.Add(image);
    }

    private void Lay(BitmapSource? art, double x, double y, double width, double height)
    {
        if (art == null) return;

        var image = new Image
        {
            Source = art,
            Width = width,
            Height = height,
            IsHitTestVisible = false,
        };
        RenderOptions.SetBitmapScalingMode(image, GameUi.SpriteScaling);
        Canvas.SetLeft(image, x);
        Canvas.SetTop(image, y);
        _scene.Children.Add(image);
    }

    private static BitmapImage? Picture(string name)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "asset", "minigame", name);
        if (!File.Exists(path)) return null;

        var bmp = new BitmapImage();
        bmp.BeginInit();
        bmp.UriSource = new Uri(path);
        bmp.CacheOption = BitmapCacheOption.OnLoad;
        bmp.EndInit();
        bmp.Freeze();
        return bmp;
    }

    // ── 끌어다 놓기 ────────────────────────────────────────────────────────────

    private readonly Image _ghost = new() { Width = 32, Height = 32 };

    /// <summary>지금 끌고 있는 금화. 안 끌고 있으면 −1.</summary>
    private int _held = -1;

    /// <summary>집은 자리 — 여기서 얼마쯤 움직여야 「끌었다」로 친다.</summary>
    private Point _grabbed;
    private bool _dragging;

    /// <summary>
    /// 지금 고른 금화(<c>+0x11C</c>) — 「가짜 금화 선택」이 이것을 쓴다.
    /// </summary>
    /// <remarks>
    /// 원본은 목록 창 없이 <b>판에서 누른 닢</b>을 그대로 쓴다(<c>0x00450AB9</c>). 아무것도
    /// 안 누르고 눌러도 처음 값 0(1번 금화)이 잡히는 것까지 그대로다.
    /// </remarks>
    private int Chosen { get; set; }

    /// <summary>끌었다고 치는 거리(판 점).</summary>
    private const double DragSlop = 4;

    private void Grab(int coin, MouseButtonEventArgs e)
    {
        e.Handled = true;
        if (_game.Won != null) return;

        _held = coin;
        _dragging = false;
        _grabbed = e.GetPosition(_scene);
        _scene.CaptureMouse();
    }

    private void Drag(object sender, MouseEventArgs e)
    {
        if (_held < 0) return;

        var now = e.GetPosition(_scene);
        if (!_dragging)
        {
            if (Math.Abs(now.X - _grabbed.X) < DragSlop && Math.Abs(now.Y - _grabbed.Y) < DragSlop)
                return;

            _dragging = true;
            _ghost.Source = Face(_held);
            RenderOptions.SetBitmapScalingMode(_ghost, GameUi.SpriteScaling);
            _ghost.Visibility = Visibility.Visible;
        }
        Canvas.SetLeft(_ghost, now.X - 16);
        Canvas.SetTop(_ghost, now.Y - 16);
    }

    private void Land(object sender, MouseButtonEventArgs e)
    {
        if (_held < 0) return;
        e.Handled = true;

        int coin = _held;
        bool dragged = _dragging;
        var now = e.GetPosition(_scene);
        Release();

        // <b>끌지 않고 딸깍하면 고를 뿐이다</b> — 원본 설명도 「누른 체 이동」만 이른다
        // (0x0053B0C0). 「가짜 금화 선택(DECIDE)」이 이 고른 닢을 쓴다(인스턴스 +0x11C).
        if (!dragged) { Chosen = coin; Sync(); return; }

        if (In(now, LeftPan)) Tap(coin, left: true, dropped: true);
        else if (In(now, RightPan)) Tap(coin, left: false, dropped: true);
        else if (_game.PanOf(coin) != 0) { _game.Clear(); Sync(); }   // 접시 밖에 내려놓으면 내린다
    }

    private void Release()
    {
        _held = -1;
        _dragging = false;
        _ghost.Visibility = Visibility.Collapsed;
        _scene.ReleaseMouseCapture();
    }

    private static bool In(Point at, (int X, int Y, int W, int H) box) =>
        at.X >= box.X && at.X < box.X + box.W && at.Y >= box.Y && at.Y < box.Y + box.H;

    /// <summary>쟁반에 놓는 번호 새긴 금화.</summary>
    /// <remarks>
    /// 어두운 벌(<c>coin-face-dim-*</c>)도 뽑아 두었지만 여기서는 안 쓴다 — 접시에 올린
    /// 금화는 쟁반에서 빠지고 <b>납작하게 누운 벌</b>로 접시에 쌓인다.
    /// </remarks>
    private static BitmapImage? Face(int coin) => Picture($"coin-face-{coin}.png");

    /// <summary>손이 얹힌 벌 — 고른 닢을 이것으로 낸다(파트 0 의 13~25).</summary>
    private static BitmapImage? Held(int coin) =>
        Picture($"coin-face-dim-{coin}.png") ?? Face(coin);

    /// <summary>금화를 눌렀다 — 접시에 놓거나, 이미 접시에 있으면 두 접시를 비운다.</summary>
    /// <param name="dropped">끌어다 놓았는지 — 원본은 끌어 놓을 때(0x004506B1)와 글쇠로 놓을 때(0x00451409) 막는 말이 다르다.</param>
    private void Tap(int coin, bool left, bool dropped = false)
    {
        if (_game.Won != null) return;

        if (_game.PanOf(coin) != 0) { _game.Clear(); Sync(); return; }

        if (!_game.Put(coin, left))
        {
            // 접시 하나에 여섯까지다(0x00450710). 끌어 놓았으면 0x0053ABC8 · 0x0053AC00, 아니면 0x0053B018.
            NoticeDialog.Show(this, dropped ? " 더 이상 접시에 금화를 실을 수 없습니다"
                                            : " 접시 위에는 더 이상 금화를 실을 수 없습니다", "천칭 퍼즐");
            return;
        }
        Sync();
    }

    private void DoWeigh()
    {
        if (!_game.CanWeigh)
        {
            NoticeDialog.Explain(this,
                "더 이상 천칭으로 금화의 무게를 달 수는 없습니다." + Environment.NewLine +
                "지금까지 얻은 결과를 분석해서 무게가 다른 금화를" + Environment.NewLine +
                "선택해 주십시오.", "천칭 퍼즐");
            return;
        }
        if (_game.Left.Count == 0 && _game.Right.Count == 0)
        {
            NoticeDialog.Show(this, "접시 위에는 아무 것도 없습니다", "천칭 퍼즐");
            return;
        }
        if (_game.Left.Count != _game.Right.Count)
        {
            NoticeDialog.Show(this, "양쪽 접시에 같은 수량의 금화가 놓여지지 않았습니다", "천칭 퍼즐");
            return;
        }

        _game.Weigh();
        Sync();
    }

    /// <summary>「가짜 금화 선택(DECIDE)」 — 어느 닢인지 고르게 하고 한 번 더 묻는다.</summary>
    private void DoDecide()
    {
        if (_game.Won != null) return;

        // 목록 창은 없다 — 판에서 누른 닢을 그대로 쓴다(0x00450AB9).
        if (!ConfirmDialog.Ask(this, "이 금화가 딴 것과 무게가 다르다고 단정해도 좋습니까?",
                               "천칭 퍼즐")) return;

        if (_game.Decide(Chosen)) { Close(); return; }

        // 첫 실패는 끝이 아니다(0x00450CE4) — 판을 새로 깔고 한 번 더 준다. 다만 미니 게임이면
        // 여기서 끝난다(0x00450C9C).
        if (_game.Won == null && _stakes)
        {
            NoticeDialog.Show(this,
                " 가려야 할 금화를 잘못 고른 것 같다. 천칭은 기울어져 금화를 떨어뜨리기 시작했다." +
                Environment.NewLine + "장치가 작동된 것 같은 소리가 들리고" +
                Environment.NewLine + "방이 흔들흔들 움직였다. 여기저기 벽에 금이 가기 시작한다.",
                "클리어 실패");
            // 알림 상자가 아니라 부관이 얼굴을 띄우고 이른다(0x00450D1C → 0x00478280).
            TalkDialog.Say(this, _aide, "천칭 퍼즐",
                " 한번 더 방이 흔들리면 찌그러질 겁니다. 빨리 가짜 금화를 발견해서 이곳으로부터 탈출합시다.");
            Chosen = 0;
            Sync();
            return;
        }

        Close();
    }

    /// <summary>「게임 설명」 — 원본 글 그대로다(<c>0x0053B0C0</c>, 세 줄 한 벌).</summary>
    /// <remarks>
    /// 예전에는 여기에 없는 문장을 덧대 두었다 — 딸깍으로 접시에 놓는 법, 접시가 여섯까지라는
    /// 것, 가벼운지 무거운지 안 알려 준다는 것. <b>원본에 없는 말은 걷었다.</b>
    /// </remarks>
    private void Explain() =>
        NoticeDialog.Explain(this,
            " 금 천칭에는 함정이 있습니다. 함정에 빠지지 않게 하기 위해서는 무게가 다른 " +
            "금화를 가려내고 천칭이 평형을 이루게 해야 합니다." + Environment.NewLine +
            " 나무 천칭을 3번까지 쓰고 무게가 다른 금화를 선택해 주십시오." + Environment.NewLine +
            " 금화 위에서 마우스 왼쪽을 클릭하여 버튼을 누른 체 금화를 이동하면 움직일 수 있습니다.");

    private void AskGiveUp()
    {
        if (!ConfirmDialog.Ask(this, "천칭 퍼즐을 포기하겠습니까?", "포기한다")) return;
        _game.GiveUp();
        Close();
    }

    private void Sync()
    {
        // 마지막으로 단 결과대로 천칭을 기울인다.
        var tilt = _game.Log.Count == 0 ? CoinPuzzle.Tilt.Level : _game.Log[^1].Result;

        foreach (var image in _arm) _scene.Children.Remove(image);
        _arm.Clear();

        (int X, int Y) leftPile, rightPile;
        if (tilt == CoinPuzzle.Tilt.Level)
        {
            Arm("coin-beam.png", BeamAt, 176, 16);
            // 좌우가 <b>같은 벌</b>이다 — coin-pan-1 은 그 접시를 고른 동안만 쓴다.
            Arm("coin-pan-0.png", LeftPanAt, 80, 144);
            Arm("coin-pan-0.png", RightPanAt, 80, 144);
            (leftPile, rightPile) = (LevelLeftPile, LevelRightPile);
        }
        else
        {
            int down = tilt == CoinPuzzle.Tilt.Left ? 0 : 1;
            Arm($"coin-wood-{down}.png", WoodAt[down], 192, 144);
            (leftPile, rightPile) = (WoodPile[down][0], WoodPile[down][1]);
        }

        // 쟁반 — 접시에 올린 것만 숨긴다. <b>빈자리는 그대로 둔다</b> — 게임도 남은
        // 금화를 앞으로 당기지 않는다(1·2 를 올리면 3 이 첫 줄 오른쪽에 홀로 남는다).
        for (int i = 0; i < _game.Coins; i++)
        {
            _coin[i].Visibility = _game.PanOf(i) != 0 ? Visibility.Collapsed : Visibility.Visible;

            // 고른 닢은 <b>손이 얹힌 벌</b>로 갈아 끼운다 — 파트 0 의 13~25 가 그것이다
            // (자리 표 0x00549E10). 「가짜 금화 선택」이 이 닢을 쓴다.
            _coin[i].Background = new ImageBrush(i == Chosen ? Held(i) : Face(i))
            {
                Stretch = Stretch.Fill,
            };
        }

        // 접시 — 납작하게 누운 금화를 쌓는다.
        foreach (var image in _piled) _scene.Children.Remove(image);
        _piled.Clear();
        Pile(_game.Left.Count, leftPile);
        Pile(_game.Right.Count, rightPile);

        // 검은 칸에는 <b>늘 세 줄</b>이 서 있다 — 게임도 「1-」「2-」「3-」을 미리 그어
        // 두고 잰 차례대로 채운다. 아직 안 잰 첫 줄에는 지금 접시에 올린 것을 보인다.
        _log.Children.Clear();
        for (int n = 0; n < CoinPuzzle.Weighings; n++)
        {
            int y = LogY + n * LogStep;
            Say($"{n + 1}-", LogX, y);

            if (n < _game.Log.Count)
            {
                var record = _game.Log[n];
                var (left, right) = Marks(record.Result);
                Say(left, LogLeftMark, y);
                Say(right, LogRightMark, y);
                Numbers(record.Left, record.Right, y);
            }
            else if (n == _game.Log.Count) Numbers(_game.Left, _game.Right, y);
        }
    }

    /// <summary>
    /// 그 기울기의 표 두 짝 — 줄 <b>왼끝</b>과 <b>오른끝</b>에 하나씩이다.
    /// </summary>
    /// <remarks>
    /// <c>0x00453B1E0</c> 언저리의 글이 그대로다 — 평형은 양쪽 다 <c>＝</c>, 기울면
    /// 내려간 쪽이 <c>↓</c> 올라간 쪽이 <c>↑</c> 다.
    /// </remarks>
    private static (string Left, string Right) Marks(CoinPuzzle.Tilt tilt) => tilt switch
    {
        CoinPuzzle.Tilt.Left => ("↓", "↑"),
        CoinPuzzle.Tilt.Right => ("↑", "↓"),
        _ => ("＝", "＝"),
    };

    /// <summary>
    /// 한 줄에 금화 번호를 늘어놓는다.
    /// </summary>
    /// <remarks>
    /// 왼 접시는 <see cref="LogLeftEnd"/> 에서 <b>왼쪽으로</b> 적어 나가므로 번호가
    /// 거꾸로 놓이고, 오른 접시는 <see cref="LogRightStart"/> 에서 오른쪽으로 적는다.
    /// 두 자리 수는 왼쪽으로 여덟 점 더 물러나 <b>오른끝이 그대로</b> 맞는다.
    ///
    /// 도는 차례는 <b>금화 번호 순</b>이다 — 게임이 금화를 0번부터 훑으며 어느 접시에
    /// 있는지 보기 때문이다. 올린 차례가 아니다.
    /// </remarks>
    private void Numbers(IReadOnlyList<int> left, IReadOnlyList<int> right, int y)
    {
        int x = LogLeftEnd;
        foreach (int coin in left.Order())
        {
            if (coin + 1 >= 10) x -= LogWide;
            Say($"{coin + 1}", x, y);
            x -= LogRun;
        }

        x = LogRightStart;
        foreach (int coin in right.Order())
        {
            Say($"{coin + 1}", x, y);
            x += coin + 1 >= 10 ? LogRun + LogWide : LogRun;
        }
    }

    /// <summary>자취 칸에 글자 한 덩이를 그 자리에 찍는다.</summary>
    private void Say(string text, int x, int y)
    {
        var label = new GameUi.GameLabel(GameFont.WhiteColor) { Text = text };
        Canvas.SetLeft(label, x);
        Canvas.SetTop(label, y);
        _log.Children.Add(label);
    }

    /// <summary>접시 하나에 금화 <paramref name="count"/> 닢을 쌓는다.</summary>
    private void Pile(int count, (int X, int Y) at)
    {
        for (int i = 0; i < count; i++)
        {
            // 납작 금화는 <b>한 벌</b>이다 — coin-gold-1 은 손이 얹혔을 때 쓰는 벌이라
            // 번갈아 깔면 안 된다.
            var image = Piece(Picture("coin-gold-0.png"), at.X, at.Y - i * StackRise, 32, 32);
            Panel.SetZIndex(image, 40 + i);
            _piled.Add(image);
        }
    }

    /// <summary>기울기마다 갈아 끼우는 조각 하나.</summary>
    private void Arm(string art, (int X, int Y) at, int width, int height)
    {
        var image = Piece(Picture(art), at.X, at.Y, width, height);
        Panel.SetZIndex(image, 20);
        _arm.Add(image);
    }

    /// <summary>그림 한 조각을 판에 놓고 그 <see cref="Image"/> 를 낸다.</summary>
    private Image Piece(BitmapImage? art, int x, int y, int width, int height)
    {
        var image = new Image
        {
            Source = art,
            Width = width,
            Height = height,
            IsHitTestVisible = false,
        };
        RenderOptions.SetBitmapScalingMode(image, GameUi.SpriteScaling);
        Canvas.SetLeft(image, x);
        Canvas.SetTop(image, y);
        _scene.Children.Add(image);
        return image;
    }

    /// <summary>
    /// 놀이를 한 판 하고 <c>0x00450C2D</c> 이 하듯 결과를 알린다.
    /// </summary>
    /// <remarks>
    /// 삯 3000닢은 <b>놀이 속 천칭</b>에서만 나온다 — <c>0x00450C4C</c> 가
    /// <c>[0x154] != 0</c> 일 때만 <c>0x0047CBC0(0xBB8)</c> 을 부르는데, 그 값은
    /// 들어올 때 받은 인자이고 미니 게임은 0 을 준다(<c>0x0045FB54</c>).
    /// </remarks>
    /// <returns>
    /// 가려냈는지. 발견 대본 <c>0E 14|1A … 04 04 00</c> 은 <c>0x004531F0(1)</c> 이 1 을 돌려줄 때만
    /// 이긴 것으로 친다(<c>0x00408E44</c>).
    /// </returns>
    /// <param name="player">삯을 받을 제독. 없으면 금화만 안 준다.</param>
    public static bool Play(Window owner, Random rng,
                            Support.Local.Models.Player? player = null, uint[]? aide = null)
    {
        bool stakes = player != null;
        var dialog = new CoinPuzzleDialog(rng, stakes, aide) { Owner = owner };
        dialog.ShowDialog();

        bool won = dialog._game.Won == true;
        if (won)
        {
            NoticeDialog.Show(owner,
                "무게가 다른 금화를 잘 가려낸 것 같다. 천칭은 평형을 이루고" +
                Environment.NewLine + "보물 상자를 무사히 가질 수 있었다.", "게임 클리어");

            // 삯은 <b>첫 판에 맞혔을 때만</b> 나온다(0x00450C4C 가 +0x150 을 본다).
            if (stakes && !dialog._game.Missed)
            {
                player?.Earn(CoinPuzzle.Prize);
                NoticeDialog.Show(owner, $" 금화 {CoinPuzzle.Prize}닢을 손에 넣었다!", "게임 클리어");
            }
        }
        else if (stakes)
        {
            NoticeDialog.Show(owner,
                " 금화를 잘못 가려낸 것 같다. 천칭은 기울어지고 말았다. " +
                Environment.NewLine + "순식간에 장치가 작동되어 방이 무너져 간다.",
                "클리어 실패");
        }
        // 미니 게임은 방이 무너지지 않는다 — 한 줄로 끝난다(0x00450D3F · 0x0053AEC8).
        else if (dialog._game.Missed)
        {
            NoticeDialog.Show(owner, " 가려야 할 금화를 잘못 고른 것 같다. 천칭은 기울어지고 말았다.",
                              "클리어 실패");
        }
        return won;
    }
}
