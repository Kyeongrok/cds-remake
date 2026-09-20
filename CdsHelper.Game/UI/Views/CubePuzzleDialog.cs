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
/// 미니 게임 「화살표 입방체 퍼즐」 화면.
/// </summary>
/// <remarks>
/// 게임의 <c>0x0049B3C0</c> 이고, 규칙은 <see cref="CubePuzzle"/> 에 모아 두었다.
///
/// 이 놀이는 <b>제 그림 파일이 없다</b> — <c>0x0049B422</c> 가 <c>0x00455DE0</c> 을
/// 부르니 <b>MGGRAPH.CDS</b> 를 함께 쓴다. 쓰는 파트는 열둘이다.
/// <code>
///   파트 3~8    64x48   입방체를 돌리는 화살표 여섯 (왼쪽 검은 칸)
///   파트 9      64x48   좌대에 선 모험자
///   파트 10     64x80   움직이는 모험자
///   파트 11     64x88   돌 블록
///   파트 12     64x88   흰 화살표가 얹힌 돌 블록 — 출구
///   파트 13     64x48   금괴
///   파트 14    512x352  배경
/// </code>
///
/// <b>칸 자리는 게임에서 뽑았다</b>(<c>0x0049BD40</c>). 밑자리와 간격이 그 위 두 조각에
/// 박혀 있다.
/// <code>
///   0049bd00  [0x0061E2A0] = Point(0xA0, 0x40)   ; 밑자리 (160, 64)
///   0049bd30  [0x0061E2A8] = Point(0x33, 0x21)   ; 간격  (51, 33)
///   0049bd5d  y = 줄 * 33
///   0049bd79  x = 칸 * 51 + (판 세로 - 줄) * 11   ; lea 둘로 11 배를 만든다
/// </code>
/// 판은 <b>5 x 5</b> 다(<c>0x0049B9FD</c> 가 <c>CPoint(5, 5)</c> 를 넘긴다). 그래서
/// 기울기 항의 밑값이 <c>5 * 11 = 55</c> 이고 첫 칸 x 가 215 다. 원본 갈무리에서 잰
/// 돌 블록 여덟이 <b>한 점도 안 틀리고</b> 이 자리에 앉는다.
///
/// <b>아직 못 찾은 것.</b> 왼쪽 칸 가운데의 <b>입방체 상자</b>(면마다 화살표가 새겨진 회색
/// 입방체)는 미리 구운 그림이 아니다 — MGGRAPH 에서 이 놀이가 쓰는 파트 열둘을 다 짚었고
/// 그 안에 없다. 돌 때마다 면을 그려 내는 것으로 보이는데 그 자리를 아직 못 짚었다.
/// 그때까지는 지금 위에 온 화살표를 글로 적어 둔다.
/// </remarks>
internal sealed class CubePuzzleDialog : GameWindow
{
    private const int SceneWidth = 512, SceneHeight = 352;

    /// <summary>화면 점 기준 곱. <see cref="GameUi.PixelZoom"/> 이 배율로 나눈다.</summary>
    private const int Zoom = 2;

    /// <summary>돌 블록 한 장(<c>MGGRAPH</c> 파트 11).</summary>
    private const int TileW = 64, TileH = 88;

    /// <summary>칸 자리 — 게임 <c>0x0049BD40</c> 의 셈 그대로다.</summary>
    /// <remarks>
    /// 기울기 항이 <c>(판 세로 - 줄) * 11</c> 이라, 밑자리 160 에 <c>세로 * 11 = 55</c> 가
    /// 늘 얹힌다. 그래서 첫 칸이 <b>215</b> 다.
    /// </remarks>
    private const int BaseX = 160 + CubePuzzle.Side * Shear;
    private const int BaseY = 64, StepX = 51, StepY = 33, Shear = 11;

    /// <summary>
    /// 모험자와 금괴가 블록 <b>윗면</b>에 앉는 자리 — 칸 자리에서 이만큼 옮긴다.
    /// </summary>
    /// <remarks>
    /// 돌 블록(64x88)의 윗면은 <b>y 0~33</b> 이다(그 아래로 밝기가 135 에서 54 로 떨어진다).
    /// 두 그림 다 <b>밑동이 y = 27 에 앉게</b> 맞추면 윗면에 놓인 것이 된다.
    /// <code>
    ///   모험자  그림이 찬 데 y 0~47  →  27 - 47 = -20
    ///   금괴    그림이 찬 데 y 0~27  →  27 - 27 =   0
    /// </code>
    /// 가로는 그림이 찬 폭(모험자 53, 금괴 38)을 블록 64 가운데에 놓은 값이다.
    /// 원본 갈무리에서 잰 자리와도 맞는다 — 모험자가 4줄 0칸에 <c>(+6, -20)</c> 이었다.
    /// </remarks>
    private const int HeroDx = 6, HeroDy = -20, HeroW = 64, HeroH = 48;
    private const int GoldDx = 8, GoldDy = 0;

    /// <summary>왼쪽 검은 칸의 화살표 여섯 자리(원본 갈무리에서 잰 것).</summary>
    private static readonly (int X, int Y)[] TurnSpots =
        [(15, 14), (79, 14), (15, 62), (79, 62), (15, 288), (79, 288)];

    /// <summary>
    /// 칸마다 어느 그림이 앉는지 — <b>파트 차례와 칸 차례가 다르다</b>.
    /// </summary>
    /// <remarks>
    /// 원본 갈무리의 칸 여섯을 그림 여섯과 하나씩 대 봐서 얻었다(어긋남 5~13, 다음 것과는
    /// 29 이상 벌어져 헷갈릴 여지가 없다). 왼쪽 줄이 0·1·2, 오른쪽 줄이 3·4·5 다.
    /// </remarks>
    private static readonly int[] TurnArt = [0, 3, 1, 4, 2, 5];

    /// <summary>
    /// 화살표 여섯이 하는 일 — 앞의 넷이 넘어뜨리기, 뒤의 둘이 수평 회전이다.
    /// </summary>
    /// <remarks>
    /// <b>단추는 판의 동서남북이 아니라 입방체의 축 쪽으로 넘어뜨린다.</b> 2x2 로 놓인
    /// 넷이 화면의 11시 · 1시 · 7시 · 5시 를 가리키고, 그쪽으로 넘어뜨리면 <b>그 반대쪽
    /// 면이 위로 온다</b>.
    /// <code>
    ///   11시  -X 로 넘어뜨린다 → 오른쪽 면(+X)이 위로 → 모델의 「동」
    ///    1시  -Y             → 왼쪽 면(+Y)이 위로   → 「북」
    ///    7시  +Y             → 뒤(-Y)가 위로        → 「남」
    ///    5시  +X             → 뒤(-X)가 위로        → 「서」
    /// </code>
    /// 좌대가 어느 쪽으로 가는지는 <b>위로 온 면의 화살표</b>가 정하므로(모델의 <c>Roll</c>),
    /// 여기서는 어느 면이 올라오는지만 맞으면 된다.
    /// </remarks>
    private static readonly int[] TurnWay = [1, 0, 2, 3, -1, -1];

    private readonly CubePuzzle _game;
    private readonly Canvas _scene = new() { Width = SceneWidth, Height = SceneHeight };
    private readonly Image[,] _tile = new Image[CubePuzzle.Side, CubePuzzle.Side];

    /// <summary>출구 블록 — 판 위쪽 바깥 줄(<c>0x0049D0EB</c>)에 홀로 선다.</summary>
    private readonly Image _door = new()
    {
        Width = TileW,
        Height = TileH,
        IsHitTestVisible = false,
    };

    /// <summary>금괴. 집으면 사라진다.</summary>
    private readonly Image _gold = new()
    {
        Width = 64,
        Height = 48,
        IsHitTestVisible = false,
    };
    private readonly Image _hero = new()
    {
        Width = HeroW,
        Height = HeroH,
        IsHitTestVisible = false,
    };
    /// <summary>왼쪽 칸의 입방체 그림.</summary>
    private CubeArt? _cube;

    /// <summary>굴러가는 중인가. 도는 동안은 단추를 안 받는다.</summary>
    private bool _turning;

    /// <summary>
    /// 굴러가는 짓시늉 — <b>90 도를 도는 데 걸리는 시간(ms)</b>이다. 속도는 이것만 만진다.
    /// </summary>
    /// <remarks>
    /// 각속도로 치면 <c>90 / TurnMillis</c> 도/ms 다. 420 이면 초당 214 도쯤 된다.
    /// </remarks>
    private const int TurnMillis = 420;

    /// <summary>한 칸 그리는 간격(ms). 16 이면 초당 예순 번이라 이만하면 부드럽다.</summary>
    private const int TurnTick = 16;

    /// <summary>90 도를 몇 칸에 나눠 도는지 — 위 둘에서 나온다.</summary>
    private const int TurnSteps = TurnMillis / TurnTick;

    /// <summary>
    /// 좌대가 <b>한 칸 미끄러지는 데 걸리는 시간(ms)</b>. 걸음 빠르기는 이것만 만진다.
    /// </summary>
    private const int MoveMillis = 320;

    /// <summary>한 칸을 몇 번에 나눠 가는지.</summary>
    private const int MoveSteps = MoveMillis / TurnTick;

    /// <summary>
    /// 입방체 상자 자리와 크기 — 원본 갈무리에서 점 단위로 쟀다.
    /// </summary>
    /// <remarks>
    /// 밤색 칸 (16, 144) 128x128 안에 입방체가 112 x 118 로 든다. 윗면 마름모가
    /// <b>110 x 50</b>, 옆면 높이가 <b>68</b> 이라 <c>a=55, b=25, c=68</c> 이다.
    /// 빛깔도 갈무리에서 뽑았다 — 세 면이 <b>다 같은 회색</b>이고 모서리만 밝다.
    /// </remarks>
    private const int BoxX = 16, BoxY = 144, BoxW = 128, BoxH = 128;
    private const double CubeA = 55, CubeB = 25, CubeC = 68;

    private static readonly Brush CubeBack = Frozen(Color.FromRgb(0x31, 0x18, 0x18));
    private static readonly Brush CubeFill = Frozen(Color.FromRgb(0x5A, 0x5A, 0x63));
    private static readonly Brush CubeEdge = Frozen(Color.FromRgb(0x80, 0x85, 0x8C));
    private static readonly Brush ArrowFill = Frozen(Color.FromRgb(0x42, 0x39, 0x39));

    private static Brush Frozen(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    private CubePuzzleDialog(Random rng)
    {
        _game = new CubePuzzle(rng);

        Lay(Picture("cube-bg.png"), 0, 0, SceneWidth, SceneHeight, zIndex: -100);

        // 판은 <b>뒷줄부터</b> 얹는다 — 블록이 88 점이라 앞줄이 뒷줄을 덮어야 한다.
        for (int row = 0; row < CubePuzzle.Side; row++)
        for (int col = 0; col < CubePuzzle.Side; col++)
            Tile(col, row);

        // 출구는 판 밖 한 칸 위라 어느 줄보다도 뒤에 놓인다.
        RenderOptions.SetBitmapScalingMode(_door, GameUi.SpriteScaling);
        _door.Source = Picture("cube-mark.png");
        var door = Spot(_game.ExitX, CubePuzzle.ExitRow);
        Canvas.SetLeft(_door, door.X);
        Canvas.SetTop(_door, door.Y);
        Panel.SetZIndex(_door, -10);
        _scene.Children.Add(_door);

        RenderOptions.SetBitmapScalingMode(_gold, GameUi.SpriteScaling);
        _gold.Source = Picture("cube-gold.png");
        var gold = Spot(_game.GoldX, _game.GoldY);
        Canvas.SetLeft(_gold, gold.X + GoldDx);
        Canvas.SetTop(_gold, gold.Y + GoldDy);
        // 제 칸의 깊이에 둔다 — 앞줄 블록이 덮어야 판에 «놓인» 것으로 보인다.
        Panel.SetZIndex(_gold, Depth(_game.GoldX, _game.GoldY));
        _scene.Children.Add(_gold);

        RenderOptions.SetBitmapScalingMode(_hero, GameUi.SpriteScaling);
        _hero.Source = Picture("cube-hero.png");
        _scene.Children.Add(_hero);

        for (int i = 0; i < TurnSpots.Length; i++) Turn(i);

        // 게임 화면에는 <b>수를 세는 글이 없다</b> — 화살표 · 입방체 · 화살표뿐이다.
        Cube();

        _scene.Background = Brushes.Transparent;
        _scene.MouseLeftButtonDown += (_, e) => e.Handled = true;

        double zoom = GameUi.PixelZoom(this, Zoom);
        _scene.LayoutTransform = new ScaleTransform(zoom, zoom);

        // 게임은 미니 게임에 밤색 판도 제목도 아래 단추 줄도 안 두른다 — 금빛 액자뿐이고
        // 할 일은 오른쪽 단추 차림표가 맡는다. 미궁·성배와 같은 결이다.
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.WidthAndHeight;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        Background = GameUi.Back;
        Content = GameUi.GoldFrame(_scene, Close);
        GameUi.EnableDrag(this, _scene);

        MouseRightButtonUp += (_, e) =>
            GameUi.ContextMenuAt(this, e.GetPosition(this), Commands());

        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Up) Roll(0);
            else if (e.Key == Key.Right) Roll(1);
            else if (e.Key == Key.Down) Roll(2);
            else if (e.Key == Key.Left) Roll(3);
            else if (e.Key == Key.Space) DoSpin();
            else if (e.Key == Key.Escape) Close();
        };

        Sync();
    }

    /// <summary>
    /// 왼쪽 칸 가운데의 <b>입방체 상자</b> — 밤색 바탕에 <see cref="CubeArt"/> 를 얹는다.
    /// </summary>
    private void Cube()
    {
        var box = new System.Windows.Shapes.Rectangle
        {
            Width = BoxW,
            Height = BoxH,
            Fill = CubeBack,
            IsHitTestVisible = false,
        };
        Canvas.SetLeft(box, BoxX);
        Canvas.SetTop(box, BoxY);
        Panel.SetZIndex(box, 205);
        _scene.Children.Add(box);

        _cube = new CubeArt(_scene, new Point(BoxX + BoxW / 2.0, BoxY + BoxH / 2.0),
                            CubeFill, CubeEdge, ArrowFill, 206);
    }

    /// <summary>
    /// 입방체가 굴러가는 짓시늉. 다 돌면 그때 모델을 움직인다.
    /// </summary>
    /// <remarks>
    /// 게임도 단추를 누르면 <b>입방체가 90 도 굴러가는 도중</b>이 보이고, 다 구른 뒤에야
    /// 좌대가 옮겨간다. 도는 동안은 다른 단추를 안 받는다.
    /// </remarks>
    private void Spin(int way, Action done)
    {
        if (_turning) return;
        _turning = true;

        var (axis, quarter) = CubeArt.Spin(way);
        int step = 0;

        var timer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(TurnTick),
        };
        timer.Tick += (_, _) =>
        {
            step++;
            _cube?.Draw(_game, axis, quarter * 90.0 * step / TurnSteps);
            if (step < TurnSteps) return;

            timer.Stop();
            _cube?.Settle(axis, quarter);
            _turning = false;
            done();
        };
        timer.Start();
    }

    /// <summary>
    /// 그 칸의 <b>깊이</b>. 앞줄이 뒷줄을 덮고, 한 줄 안에서는 오른쪽이 왼쪽을 덮는다.
    /// </summary>
    /// <remarks>
    /// 모험자와 금괴도 <b>제 칸의 깊이</b>를 쓴다. 예전에는 이 둘을 모든 블록 위에 그렸는데,
    /// 그러면 앞줄 블록까지 덮어 버려 <b>판 위에 떠 있는 것처럼</b> 보였다.
    /// 얹힌 것이라 같은 칸의 블록보다는 한 단 위에 둔다.
    /// </remarks>
    private static int Depth(int col, int row) => (row + 1) * 10 + col;

    /// <summary>그 칸의 왼쪽 위 자리(<c>0x0049BD40</c>).</summary>
    private static Point Spot(int col, int row) =>
        new(BaseX + col * StepX - row * Shear, BaseY + row * StepY);

    /// <summary>돌 블록 한 장. 구멍인 칸은 안 뜬다.</summary>
    private void Tile(int col, int row)
    {
        var at = Spot(col, row);
        var image = new Image
        {
            Width = TileW,
            Height = TileH,
            Cursor = Cursors.Hand,
        };
        RenderOptions.SetBitmapScalingMode(image, GameUi.SpriteScaling);
        Canvas.SetLeft(image, at.X);
        Canvas.SetTop(image, at.Y);
        Panel.SetZIndex(image, Depth(col, row));
        _scene.Children.Add(image);
        _tile[col, row] = image;
    }

    /// <summary>왼쪽 검은 칸의 화살표 하나. 누르면 그쪽으로 돌린다.</summary>
    private void Turn(int at)
    {
        var image = new Image
        {
            Width = 64,
            Height = 48,
            Source = Picture($"cube-turn-{TurnArt[at]}.png"),
            Cursor = Cursors.Hand,
        };
        RenderOptions.SetBitmapScalingMode(image, GameUi.SpriteScaling);
        Canvas.SetLeft(image, TurnSpots[at].X);
        Canvas.SetTop(image, TurnSpots[at].Y);
        Panel.SetZIndex(image, 210);

        image.MouseLeftButtonDown += (_, e) => e.Handled = true;
        image.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            int way = TurnWay[at];
            if (way >= 0) Roll(way); else DoSpin();
        };

        _scene.Children.Add(image);
    }

    private void Lay(BitmapSource? art, double x, double y, double width, double height,
                     int zIndex = 0)
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
        Panel.SetZIndex(image, zIndex);
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

    /// <summary>
    /// 오른쪽 단추 차림표 — 게임은 <b>「게임 설명」과 「게임 복귀」 두 줄뿐</b>이다.
    /// </summary>
    /// <remarks>
    /// 넘어뜨리고 돌리는 것은 왼쪽 칸의 화살표 여섯이 맡으므로 차림표에 안 둔다.
    /// 「포기한다」만 우리 쪽에서 더했다 — 창을 닫을 길이 있어야 한다.
    /// </remarks>
    private IReadOnlyList<(string, Action?)> Commands() =>
    [
        ("게임 설명", Explain),
        ("포기한다", AskGiveUp),
        ("게임 복귀", () => { }),
    ];

    private void AskGiveUp()
    {
        if (_turning) return;
        if (!ConfirmDialog.Ask(this, "이 게임을 포기하겠습니까?", "포기한다")) return;

        _game.GiveUp();
        Close();
    }

    /// <summary>금괴를 밟았을 때 부를 것 — 벌이와 알림은 <see cref="Play"/> 가 맡는다.</summary>
    private Action? _onGold;

    private void Roll(int way)
    {
        if (_turning || _game.Over != null) return;

        Spin(way, () =>
        {
            int fromX = _game.X, fromY = _game.Y;
            bool had = _game.GotGold;
            _game.Roll(way);
            Slide(fromX, fromY, () =>
            {
                Sync();

                // 금괴는 <b>밟는 그 자리에서</b> 알린다 — 나가고 나서가 아니다.
                if (!had && _game.GotGold) _onGold?.Invoke();

                if (_game.Over != null) Close();
            });
        });
    }

    /// <summary>
    /// 좌대가 <b>한 칸 미끄러지는</b> 짓시늉. 입방체가 다 구른 뒤에 돈다.
    /// </summary>
    /// <remarks>
    /// 게임도 입방체가 서고 나서 좌대가 스르르 옮겨간다. 예전에는 한 판에 툭 옮겨 놓아
    /// 너무 빨랐다.
    /// </remarks>
    private void Slide(int fromX, int fromY, Action done)
    {
        var from = Spot(fromX, fromY);
        var to = Spot(_game.X, _game.Y);
        _turning = true;
        int step = 0;

        var timer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(TurnTick),
        };
        timer.Tick += (_, _) =>
        {
            step++;
            double t = (double)step / MoveSteps;
            Canvas.SetLeft(_hero, from.X + (to.X - from.X) * t + HeroDx);
            Canvas.SetTop(_hero, from.Y + (to.Y - from.Y) * t + HeroDy);
            if (step < MoveSteps) return;

            timer.Stop();
            _turning = false;
            done();
        };
        timer.Start();
    }

    private void DoSpin()
    {
        if (_turning || _game.Over != null) return;

        // 연달아 못 돌리는 것은 굴리기 전에 가린다 — 헛돌고 나서 물리면 이상하다.
        if (_game.JustSpun)
        {
            NoticeDialog.Show(this, "2번 계속해서 수평으로 회전할 수 없다.", "게임 설명");
            return;
        }

        Spin(4, () => { _game.Spin(); Sync(); });
    }

    /// <summary>게임 EXE 의 설명 글 그대로(<c>0x0056BB60</c>).</summary>
    private static readonly string Rules =
        "성공조건 [자기가 타고 있는 좌대를 움직여서 출구로 이동한다]" + Environment.NewLine +
        Environment.NewLine +
        "입방체를 지면에 수직으로 돌리면 위의 면에 온 화살표 방향으로 좌대는 움직인다." +
        Environment.NewLine +
        "지면에 대하여 수평으로 돌려도 좌대는 움직이지 않는다." + Environment.NewLine +
        "2번 계속해서 수평으로 회전할 수 없다.";

    private static void Explain(Window owner) => NoticeDialog.Explain(owner, Rules);

    private void Explain() => Explain(this);

    private void Sync()
    {
        _cube?.Draw(_game, 0, 0);

        var stone = Picture("cube-stand.png");

        for (int row = 0; row < CubePuzzle.Side; row++)
        for (int col = 0; col < CubePuzzle.Side; col++)
        {
            bool floor = _game.Floor(col, row);
            _tile[col, row].Source = stone;
            _tile[col, row].Visibility = floor ? Visibility.Visible : Visibility.Collapsed;
        }

        _gold.Visibility = _game.GotGold ? Visibility.Collapsed : Visibility.Visible;

        // 출구는 판 밖 한 칸 위라 자르지 않는다 — 자르면 나가는 그림이 안 나온다.
        int px = Math.Clamp(_game.X, -1, CubePuzzle.Side);
        int py = Math.Clamp(_game.Y, CubePuzzle.ExitRow, CubePuzzle.Side);
        Panel.SetZIndex(_hero, Depth(px, py) + 1);
        var at = Spot(px, py);
        Canvas.SetLeft(_hero, at.X + HeroDx);
        Canvas.SetTop(_hero, at.Y + HeroDy);
    }

    /// <summary>놀이를 한 판 하고 <c>0x0049B3C0</c> 이 하듯 결과를 알린다.</summary>
    /// <summary>금괴를 밟을 때 나는 소리 — 사운드 0x27(<c>0x0049C918</c>), WAVE 파트 11 이다.</summary>
    private const int GoldSoundPart = 0x27 - 28;

    public static void Play(Window owner, Player player, Random rng, Local.Helpers.SoundBank? sfx = null)
    {
        // 판을 열기 전에 설명부터 낸다 — 게임도 그렇다.
        Explain(owner);

        bool paid = false;
        while (true)
        {
            var dialog = new CubePuzzleDialog(rng) { Owner = owner };

            // 금괴를 밟으면 <b>그 자리에서는 글만</b> 뜬다(0x0049C91F, 0x0056DDF8) — 돈은 판을 마쳤을 때
            // 0x0049B366 이 넣는다. 밟고 떨어지면 못 받는다. 판을 다시 줘도 삯은 한 번뿐이다.
            dialog._onGold = () =>
            {
                if (paid) return;
                paid = true;
                sfx?.Play(GoldSoundPart);
                NoticeDialog.Show(dialog,
                    $"금화로 따지면 {CubePuzzle.Prize} 닢에 상당되는 금괴를 손에 넣었다!",
                    "금괴 취득");   // 제목도 원본 그대로다(0x0056DDE8)
            };

            dialog.ShowDialog();

            if (dialog._game.Over != false || dialog._game.GaveUp)
            {
                // 판을 마쳤을 때만 금괴 값이 들어온다(0x0049B366).
                if (paid && dialog._game.Over == true) player.Earn(CubePuzzle.Prize);
                return;
            }

            // 떨어져도 끝이 아니다 — 아래층이 있었다며 판을 새로 깔아 준다(0x0049B3C0).
            NoticeDialog.Show(owner,
                "아니! 밑바닥으로 떨어진 줄 알았는데 실은 그 아래층이 존재했다!"
                + Environment.NewLine
                + "자, 모험자여! 이것이 마지막 찬스다! 입방체를 잘 조작하여 이 상황을 타파하라!",
                "다시 한번 도전할 수 있습니다!");
        }
    }
}
