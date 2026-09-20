using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using System.Windows.Media.Imaging;
using CdsHelper.Game.Engine;
using CdsHelper.Game.Engine.Town;
using CdsHelper.Game.Local.Helpers;

namespace CdsHelper.Game.UI.Views;

/// <summary>
/// 일기토 판 — 부위 셋을 두고 칼을 겨룬다.
/// </summary>
/// <remarks>
/// 셈은 <see cref="Duel"/> 이 다 하고 이 창은 보여 주기만 한다. 판은 게임과 같은
/// <b>384x248 두 층</b>이다(<c>0x004AA7BB</c> 의 <c>0x180 x 0x100</c>).
/// <code>
///   위 384x136  배경 — 그림 바탕에 두 사람이 선다(asset/duel, FighterSprites)
///   아래 384x112 눈금판 — 초상 둘 · 고른 명령 둘 · 부위 막대 여섯
/// </code>
/// 눈금판 위의 자리는 <b>그림에 찍힌 자리표를 재어</b> 얻었다
/// (<see cref="DuelArt.Slots"/>) — 눈으로 맞춘 값이 아니다.
/// <b>왼쪽이 상대, 오른쪽이 나</b>다.
///
/// 명령을 고를 때만 오른쪽 초상 자리 위에 <b>작은 명령 창</b>이 뜬다 — 게임도 그 자리다.
///
/// 상대가 하는 말은 게임 표(<c>0x005729E0</c> 부터 여섯씩 넉 줄)를 그대로 옮겼다.
/// </remarks>
public sealed class DuelDialog : GameWindow
{
    /// <summary>
    /// 막대의 <b>검은 덮개</b> — 색 <c>0x49</c>, 공용 색표에서 (24, 20, 12)다.
    /// </summary>
    /// <remarks>
    /// 게임은 막대 줄마다 <c>S(체력+1)</c> 넘는 자리를 이 색 네모로 채운다
    /// (<c>0x004A71A9</c> ~ <c>0x004A73A7</c>, <c>0x4B9663(0, 0x49, 0)</c> → <c>0x4B9C04</c>).
    /// 눈금판의 검은 홈과 같은 빛깔이다.
    /// </remarks>
    private static readonly Brush BarCover = Frozen(Color.FromRgb(24, 20, 12));

    /// <summary>
    /// 부위 막대를 칠하는 붓 셋 — <b>게임 조각을 그대로</b> 깐다.
    /// </summary>
    /// <remarks>
    /// 게임은 1점 폭 x 8점 높이 조각을 72번까지 찍어 막대를 그린다. 그 조각들이
    /// 눈금판 파트(<c>FIGHTER.CDS</c> 32)의 <b>앞 16바이트</b>에 들어 있어
    /// <c>asset/duel/duel-bar-*.png</c> 로 뽑아 두었다(<c>tools/extract_duel_art.py</c>).
    /// <code>
    ///   duel-bar-full   가득 찬 자리(파랑)   — 눈금판 그림에서 오려 낸 것
    ///   duel-bar-hurt   맞은 자리(빨강)      — 자리 0   (0x004A7279)
    ///   duel-bar-empty  빈 칸(나뭇결)        — 자리 8   (0x004A71D0)
    /// </code>
    /// 손으로 고른 빛깔 하나로 칠하던 것과 달리 여덟 줄의 결(검정 · 어둠 · 밝음 · 중간 …)이
    /// 그대로 살아난다.
    /// </remarks>
    private static readonly Brush Left_ = Tile("duel-bar-full", Color.FromRgb(0x4C, 0x8C, 0xC4));
    private static readonly Brush Hurt = Tile("duel-bar-hurt", Color.FromRgb(0xC4, 0x30, 0x28));
    private static readonly Brush Empty_ = Tile("duel-bar-empty", Color.FromRgb(0x0A, 0x08, 0x08));

    /// <summary>
    /// 1점 폭 조각을 가로로 이어 까는 붓. 그림이 없으면 그 빛깔로 물러선다.
    /// </summary>
    private static Brush Tile(string name, Color fallback)
    {
        string? path = DuelArt.Open()?.Path_(name);
        if (path == null) return Frozen(fallback);

        var brush = new ImageBrush(new BitmapImage(new Uri(path, UriKind.RelativeOrAbsolute)))
        {
            TileMode = TileMode.Tile,
            Viewport = new Rect(0, 0, 1, DuelArt.Slots.BarH),
            ViewportUnits = BrushMappingMode.Absolute,
            Stretch = Stretch.Fill,
        };
        RenderOptions.SetBitmapScalingMode(brush, GameUi.SpriteScaling);
        brush.Freeze();
        return brush;
    }

    private static SolidColorBrush Frozen(Color c)
    {
        var b = new SolidColorBrush(c);
        b.Freeze();
        return b;
    }

    /// <summary>상대가 하는 말 넉 줄 — 게임 표 <c>0x005729E0</c>·<c>F8</c>·<c>0x00572A10</c>·<c>28</c>.</summary>
    private static readonly string[][] Taunts =
    [
        // 내가 맞았을 때
        [
            "찔렀다!",
            "빈틈투성이로군.\n한눈 팔고 있으면\n저세상행이지.",
            "헤헤.\n내가 한수 위로군!",
            "그게\n방어하는 건가.",
            "좀 하는 녀석인 줄\n알았더니...\n뜻밖이군.",
            "슬슬 본 실력을\n내 보시지.\n시시하군.",
        ],
        // 내 공격이 막혔을 때
        [
            "미지근한데.\n그만두겠는가?",
            "안됐군.\n이길 것 같지도 않군.",
            "오~옳지, 아깝군.\n조금 더다.",
            "얏!\n피했다.",
            "그 정도 솜씨로...\n아직이야!",
            "너 같은 녀석에게\n당할 것 같았느냐!",
        ],
        // 상대의 공격을 내가 막았을 때
        [
            "피했나.\n제법이군!",
            "앗!\n실패했다.",
            "이런 바보같은...",
            "이것을 피하리라고는\n곤란하게 됐군.",
            "실패했다!",
            "아니!\n제법이군, 자네.",
        ],
        // 상대가 맞았을 때
        [
            "자, 지금부터네.",
            "제법이야.\n할 마음이 생기는군.",
            "안됐네만\n이 댓가는\n비싸네.",
            "아직이다.\n아직 끝나지 않았다.\n승부는 지금부터다!",
            "으윽!\n방심한 것 같군.",
            "우오오옷!\n제법이군, 자네.",
        ],
    ];

    private readonly Duel _duel;
    private readonly GameRandom _dice;
    private readonly uint[]? _face;
    private readonly DuelStage? _stage;

    private readonly StackPanel _keys = new();

    /// <summary>
    /// 부위 막대 한 칸 — 게임이 찍는 차례대로 조각 넷을 겹친다(<c>0x004A7050</c>).
    /// </summary>
    /// <remarks>
    /// 게임 막대는 <b>72점 폭에 눈금의 끝이 100 으로 박혀 있다</b>(<c>0x004A8D65</c> 가
    /// <c>+0x168</c>·<c>+0x16C</c> 에 100 을 쓴다). 곧 <c>S(v) = v x 72 / 100</c>(버림)이고
    /// 한 줄은 이렇다(나 쪽, 상대는 거울).
    /// <code>
    ///   0          S(지금)          S(체력+1)          72
    ///   |-- 파랑 ---|---- 빨강 --------|---- 검정 --------|
    ///   눈금판 그림    조각 0(잃은 값)     색 0x49(끝내 없는 몫)
    /// </code>
    /// 게임은 파랑(눈금판) 위에 ① 옅은 나뭇결(조각 8) ② 빨강(조각 0) ③ 검은 네모를
    /// 차례로 <b>막대 끝까지</b> 찍는다. 그래서 여기서도 넷을 모두 먼 끝(나는 오른쪽,
    /// 상대는 왼쪽)에 붙이고 폭만 <c>72 - 시작점</c> 으로 준다.
    /// </remarks>
    private sealed class BarView
    {
        public Border Keep = null!;    // 파랑 — 눈금판 그림, 늘 72점
        public Border Fresh = null!;   // ① 옅은 나뭇결(조각 8) — 방금 깎인 자리
        public Border Lost = null!;    // ② 빨강(조각 0) — 잃은 값
        public Border Black = null!;   // ③ 색 0x49 — S(체력+1) 넘는 자리

        /// <summary>줄어드는 결을 도는 중인가.</summary>
        public bool Hurting;

        /// <summary>새 끝 <c>+0x128</c> · 옛 끝 <c>+0x12C</c> · 걸음 <c>+0xF0</c> · 걸음수 <c>+0x130</c>.</summary>
        public int NewEnd, OldEnd, Step, Count;
    }

    private readonly BarView[] _mine = new BarView[Duel.Lines];
    private readonly BarView[] _theirs = new BarView[Duel.Lines];

    /// <summary>가운데 라벨 둘 — 이번에 고른 명령.</summary>
    private readonly GameUi.GameLabel _myMove = MoveLabel();
    private readonly GameUi.GameLabel _foeMove = MoveLabel();

    /// <summary>앞 판 값(나 <c>+0x104</c>~, 상대 <c>+0x11C</c>~) — 빨강이 차기 시작하는 옛 끝이다.</summary>
    private readonly int[] _wasMine = new int[Duel.Lines];
    private readonly int[] _wasFoe = new int[Duel.Lines];

    /// <summary>빨강이 차는 결을 돌리는 눈금(1/15초). 막대가 다 차면 멈춘다.</summary>
    private readonly DispatcherTimer _hurtTimer = new(DispatcherPriority.Render);

    /// <summary>명령 창이 앉는 자리 — 판 오른쪽 아래다.</summary>
    private readonly Border _keyBox = new();

    /// <summary>
    /// 명령 창이 앉는 자리 — 판 <b>오른쪽 아래</b>, 눈금판 위에 걸친다.
    /// </summary>
    /// <remarks>
    /// 배경 한가운데에 두었더니 싸우는 두 사람을 가렸다. 게임 화면을 재어 보면 창이
    /// 눈금판 위쪽에 걸쳐 오른쪽으로 붙어 있다 — 내 초상 자리를 덮는 대신 마당을
    /// 비워 두는 것이다.
    ///
    /// <b>아래를 붙박고 위로 자라게 둔다.</b> 위를 붙박으면 필살이 붙어 여섯 줄이 되는
    /// 공격 판에서 창이 판 밑으로 잘려 나간다.
    /// </remarks>
    private const double KeyBoxRight = 20, KeyBoxBottom = 25;

    /// <summary>
    /// 말풍선 자리 — 두 초상 사이다. 화면에서 재어 맞췄다.
    /// </summary>
    private const double BubbleX = 120, BubbleY = 12, BubbleW = 180, BubbleH = 76;

    /// <summary>상대가 하는 말이 적히는 흰 말풍선. 할 말이 없으면 안 보인다.</summary>
    private readonly StackPanel _bubbleText = new()
    {
        VerticalAlignment = VerticalAlignment.Center,
        Margin = new Thickness(10, 4, 8, 4),
    };

    private readonly Border _bubble = new()
    {
        Background = System.Windows.Media.Brushes.White,
        BorderBrush = System.Windows.Media.Brushes.Black,
        BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(10),
        Visibility = Visibility.Collapsed,
    };

    private DuelDialog(Duel duel, GameRandom dice, uint[]? face, uint[]? myFace,
                       FighterSprites? art, int foeSet, DuelArt? board, string arena)
    {
        _duel = duel;
        _dice = dice;
        _face = face;
        // 몸짓 표를 <b>판을 열 때마다</b> 다시 읽는다 — 모션 메이커에서 고쳐 저장한 것이
        // 놀이를 다시 띄우지 않아도 다음 일기토부터 들게 하려는 것이다.
        DuelMotions.Forget();
        if (art != null) _stage = new DuelStage(art, foeSet);

        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.WidthAndHeight;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        Background = GameUi.Back;

        var canvas = new Canvas
        {
            Width = DuelArt.BoardWidth,
            Height = DuelArt.BoardHeight,
            Background = GameUi.Back,
        };

        // ── 위층: 배경 그림과 두 사람 ─────────────────────────────────────
        Put(canvas, Picture(board?.Path_(arena)), 0, 0, DuelArt.ArenaWidth, DuelArt.ArenaHeight);
        if (_stage != null) Put(canvas, _stage, 0, 0);

        // ── 아래층: 눈금판 ────────────────────────────────────────────────
        const int Top = DuelArt.ArenaHeight;
        // 눈금판은 배경 빛깔을 따라간다 — 초원이면 초원 것, 갑판이면 갑판 것.
        Put(canvas, Picture(board?.Path_(DuelArt.PanelFor(arena))), 0, Top,
            DuelArt.PanelWidth, DuelArt.PanelHeight);

        // 왼쪽이 상대, 오른쪽이 나다.
        Put(canvas, Portrait(face), FoeFaceX, Top + DuelArt.Slots.PortraitY);
        Put(canvas, Portrait(myFace), MyFaceX, Top + DuelArt.Slots.PortraitY);

        Put(canvas, Framed(_foeMove), DuelArt.Slots.FoeMoveX, Top + DuelArt.Slots.MoveY,
            DuelArt.Slots.MoveW, DuelArt.Slots.MoveH);
        Put(canvas, Framed(_myMove), DuelArt.Slots.MyMoveX, Top + DuelArt.Slots.MoveY,
            DuelArt.Slots.MoveW, DuelArt.Slots.MoveH);

        for (int i = 0; i < Duel.Lines; i++)
        {
            Put(canvas, Bar(out _theirs[i], mirror: true),
                DuelArt.Slots.FoeBarX, Top + DuelArt.Slots.BarY[i],
                DuelArt.Slots.BarW, DuelArt.Slots.BarH);
            Put(canvas, Bar(out _mine[i], mirror: false),
                DuelArt.Slots.MyBarX, Top + DuelArt.Slots.BarY[i],
                DuelArt.Slots.BarW, DuelArt.Slots.BarH);
        }

        // 상대가 하는 말은 <b>눈금판 위의 흰 말풍선</b>이다 — 두 초상 사이를 채운다.
        Put(canvas, _bubble, BubbleX, Top + BubbleY, BubbleW, BubbleH);

        // 판 밑에는 아무것도 안 붙인다. 게임 판은 384x248 이 전부이고, 상대의 말은
        // 제목 「일기토」가 붙은 <b>제 창</b>으로 따로 난다. 어느 판인지(맞부딪힘·공격·
        // 방어)는 명령 창의 줄 이름이 그대로 일러 준다.
        // 명령을 고를 때만 뜨는 작은 명령 창. <b>오른쪽 아래</b>에 뜬다 — 게임도 그 자리다.
        _keyBox.Background = GameUi.MenuBack;
        _keyBox.BorderBrush = GameUi.Edge;
        _keyBox.BorderThickness = new Thickness(1);
        _keyBox.Padding = new Thickness(3);
        _keyBox.Child = _keys;
        _keyBox.HorizontalAlignment = HorizontalAlignment.Right;
        _keyBox.VerticalAlignment = VerticalAlignment.Bottom;
        _keyBox.Margin = new Thickness(0, 0, KeyBoxRight, KeyBoxBottom);

        // 판 위에 겹쳐 놓아야 판 밖으로 삐져나가지 않는다 — 예전에는 자리를 못 박아
        // 오른쪽으로 벗어났다.
        var page = new Grid { Width = DuelArt.BoardWidth, Height = DuelArt.BoardHeight };
        page.Children.Add(canvas);
        page.Children.Add(_keyBox);

        Content = new Border
        {
            BorderBrush = GameUi.Edge,
            BorderThickness = new Thickness(1),
            Child = page,
        };

        GameUi.EnableDrag(this, page);
        KeyDown += (_, e) => { if (e.Key == Key.Escape) e.Handled = true; };   // 판은 물러날 수 없다

        _hurtTimer.Interval = TimeSpan.FromSeconds(DuelMotions.Tick);
        _hurtTimer.Tick += (_, _) => HurtTick();

        Refresh();
        Rebuild();

        // 다가오기 — 둘이 벽에서 가운데로 걸어 나온 <b>뒤에야</b> 명령 창이 뜬다
        // (갈무리 「일기토의 초기화」). 그림이 없는 판은 걸을 것도 없다.
        if (_stage is { } stage)
        {
            _keyBox.Visibility = Visibility.Hidden;
            bool walked = false;
            Loaded += (_, _) =>
            {
                if (walked) return;
                walked = true;
                stage.WalkIn(() => _keyBox.Visibility = Visibility.Visible);
            };
        }
    }

    /// <summary>칸 하나를 판 위 그 자리에 앉힌다.</summary>
    private static void Put(Canvas canvas, UIElement? what, double x, double y,
                            double w = 0, double h = 0)
    {
        if (what == null) return;

        Canvas.SetLeft(what, x);
        Canvas.SetTop(what, y);
        if (w > 0 && what is FrameworkElement box) { box.Width = w; box.Height = h; }
        canvas.Children.Add(what);
    }

    /// <summary>뽑아 둔 그림 한 장. 파일이 없으면 null 이고 그 자리는 빈 채로 둔다.</summary>
    private static Image? Picture(string? path)
    {
        if (path == null) return null;

        var image = new Image { Source = new BitmapImage(new Uri(path, UriKind.RelativeOrAbsolute)) };
        RenderOptions.SetBitmapScalingMode(image, GameUi.SpriteScaling);
        RenderOptions.SetEdgeMode(image, EdgeMode.Aliased);
        return image;
    }

    /// <summary>
    /// 초상 자리 — 상대 (8, 144), 나 (296, 144). 판 전체 기준이고 80x96 이다.
    /// </summary>
    /// <remarks>
    /// 게임은 <c>0x004A714E</c> 에서 상대 얼굴을 x 8 에, <c>0x004A70F3</c> 에서 내 얼굴을
    /// x <c>0x128</c>(296)에 곧바로 찍는다. 84 폭 자리표(<see cref="DuelArt.Slots"/>)의 가운데에
    /// 앉히던 것은 9 · 295 로 한 점씩 어긋났다.
    /// </remarks>
    private const int FoeFaceX = 8, MyFaceX = 296;

    /// <summary>초상 한 장. 얼굴이 없으면 자리를 비운다.</summary>
    private static UIElement? Portrait(uint[]? face)
    {
        if (face == null) return null;

        var bmp = BitmapSource.Create(Portraits.Width, Portraits.Height, 96, 96,
                                      PixelFormats.Bgra32, null, face, Portraits.Width * 4);
        bmp.Freeze();

        var image = new Image
        {
            Source = bmp,
            Width = Portraits.Width,
            Height = Portraits.Height,
        };
        RenderOptions.SetBitmapScalingMode(image, GameUi.SpriteScaling);
        RenderOptions.SetEdgeMode(image, EdgeMode.Aliased);
        return image;
    }

    /// <summary>
    /// 고른 명령이 적히는 검은 홈. <b>게임 글꼴</b>로 찍는다.
    /// </summary>
    /// <remarks>
    /// 글꼴을 못 읽었을 때만 윈도 글꼴로 물러선다 — 검은 홈이라 그때 쓸 색을 흰빛으로
    /// 일러 준다(<see cref="GameUi.GameLabel.FallbackBrush"/>).
    /// </remarks>
    private static GameUi.GameLabel MoveLabel() => new(GameFont.WhiteColor)
    {
        // <b>굵게 찍지 않는다.</b> 굵게 하면 한 점 겹쳐 찍느라 획이 두꺼워지고 오른쪽
        // 아래로 그림자가 진 것처럼 보인다. 원본의 고른 명령 글씨는 그림자가 없다.
        Bold = false,
        FallbackBrush = Brushes.White,
        // 게임은 칸 <b>왼쪽 위</b>(x 0x70 + 0x60*쪽, y 0xA0)에 "%s" 로 곧바로 찍는다 —
        // 가운데 맞춤이 없다(0x004A6764). 글꼴 높이가 16 이라 칸 높이와 같다.
        HorizontalAlignment = HorizontalAlignment.Left,
        VerticalAlignment = VerticalAlignment.Top,
    };

    /// <summary>상대 쪽에서 본 판 갈래 — 내가 치면 상대는 막고, 내가 막으면 상대가 친다.</summary>
    private static Duel.Phase Flip(Duel.Phase was) => was switch
    {
        Duel.Phase.Attack => Duel.Phase.Guard,
        Duel.Phase.Guard => Duel.Phase.Attack,
        _ => was,                                   // 맞부딪힘은 둘 다 친다
    };

    /// <summary>
    /// 명령 칸. 바탕을 칠하지 않는다 — 칸이 비면 눈금판 그림의 검은 홈이 그대로 보인다
    /// (게임도 눈금판을 다시 깔아 칸을 비운다).
    /// </summary>
    private static Border Framed(UIElement inner) => new() { Child = inner };

    /// <summary>
    /// 말풍선에 상대의 말을 적는다. 빈 글이면 풍선을 걷는다.
    /// </summary>
    /// <remarks>
    /// 게임은 이 말을 <b>판 위 흰 말풍선</b>으로 낸다 — 제목 붙은 딴 창이 아니다.
    /// 글꼴도 판과 같은 게임 글꼴이고 바탕이 희어 검은 글씨다.
    /// </remarks>
    private void Speak(string text)
    {
        _bubbleText.Children.Clear();

        if (text.Length == 0)
        {
            _bubble.Visibility = Visibility.Collapsed;
            return;
        }

        foreach (string line in GameUi.Wrap(text, BubbleW - 20))
            _bubbleText.Children.Add(new GameUi.GameLabel(GameFont.BlackColor)
            {
                Text = line,
                Bold = false,
                FallbackBrush = System.Windows.Media.Brushes.Black,
                HorizontalAlignment = HorizontalAlignment.Left,
            });

        _bubble.Child = _bubbleText;
        _bubble.Visibility = Visibility.Visible;
    }

    /// <summary>그 판에 고른 명령의 이름 — 맞부딪힘·공격이면 치는 줄, 방어면 막는 명령이다.</summary>
    private static string MoveName(Duel.Phase was, int move)
    {
        if (move < 0) return "";
        return was == Duel.Phase.Guard
            ? (move < Duel.Guards.Length ? Duel.Guards[move] : "")
            : (move < Duel.Attacks.Length ? Duel.Attacks[move]
                                          : (move - Duel.Lines < Duel.Finishers.Length
                                             ? Duel.Finishers[move - Duel.Lines] : ""));
    }

    /// <summary>
    /// 부위 막대 한 칸 — 파랑 · 옅은 나뭇결 · 빨강 · 검정을 겹친다.
    /// </summary>
    private static Border Bar(out BarView view, bool mirror)
    {
        // <b>두 쪽이 서로 거울이다.</b> 내 막대는 왼쪽에 파랑이 붙어 오른쪽에서 빨개지고,
        // 상대 막대는 오른쪽에 파랑이 붙어 <b>왼쪽에서</b> 빨개진다. 게임도 그렇다 —
        // 상대 쪽 셈만 <c>neg eax; idiv [+0x16C]; add eax, 0x48</c> 로 72 에서 빼며 센다.
        // 조각은 파랑 반대쪽 끝(나는 오른쪽, 상대는 왼쪽)에 붙인다.
        var far = mirror ? HorizontalAlignment.Left : HorizontalAlignment.Right;

        view = new BarView
        {
            Keep = new Border { Background = Left_ },
            Fresh = new Border { Background = Empty_, HorizontalAlignment = far },
            Lost = new Border { Background = Hurt, HorizontalAlignment = far },
            Black = new Border { Background = BarCover, HorizontalAlignment = far },
        };

        // 게임이 찍는 차례 그대로 — 눈금판(파랑) 위에 ① 나뭇결 ② 빨강 ③ 검정.
        var stack = new Grid();
        stack.Children.Add(view.Keep);
        stack.Children.Add(view.Fresh);
        stack.Children.Add(view.Lost);
        stack.Children.Add(view.Black);

        return new Border { Child = stack };
    }

    /// <summary>
    /// 막대 눈금의 끝 — <b>누구나 100</b>이다(<c>0x004A8D65</c>, <c>+0x168</c>·<c>+0x16C</c>).
    /// </summary>
    /// <remarks>
    /// 제 처음 값(체력+1)이 아니다. 그래서 체력이 낮을수록 막대 끝에 검은 몫이 길다 —
    /// 체력 60 이면 72점 가운데 43점만 보이고 29점이 검다.
    /// </remarks>
    private const int BarScale = 100;

    /// <summary><c>S(v) = v x 72 / 100</c> — 정수 나눗셈 버림. 막대 밖으로는 안 나가게 자른다.</summary>
    private static int Scale(int v) => Math.Clamp(v * DuelArt.Slots.BarW / BarScale, 0, DuelArt.Slots.BarW);

    /// <summary>
    /// 막대 여섯을 <b>가만히 있는 모양</b>으로 그리고 앞 판 값을 지금 값에 맞춘다.
    /// </summary>
    /// <remarks>
    /// 줄어드는 결이 도는 중이면 그 자리에서 끝낸다 — 다 찬 모양과 같다
    /// (<c>0x004A6AB3</c> 에서 앞 판 값 = 사본).
    /// </remarks>
    private void Refresh()
    {
        _hurtTimer.Stop();
        for (int i = 0; i < Duel.Lines; i++)
        {
            Rest(_mine[i], _duel.MyParts[i], _duel.MyFull);
            Rest(_theirs[i], _duel.FoeParts[i], _duel.FoeFull);
            _wasMine[i] = _duel.MyParts[i];
            _wasFoe[i] = _duel.FoeParts[i];
        }
    }

    /// <summary>한 줄의 가만히 있는 모양 — 파랑 [0, S(지금)) · 빨강 [S(지금), S(처음)) · 검정 [S(처음), 72).</summary>
    private static void Rest(BarView view, int now, int full)
    {
        view.Hurting = false;
        view.Step = view.Count = 0;
        Paint(view, pale: DuelArt.Slots.BarW, red: Scale(now), full);
    }

    /// <summary>
    /// 한 줄을 칠한다. 세 조각의 <b>시작점</b>만 받는다 — 끝은 모두 막대 끝이다.
    /// </summary>
    /// <param name="pale">옅은 나뭇결(조각 8)이 시작하는 점. 72 면 없다.</param>
    /// <param name="red">빨강(조각 0)이 시작하는 점.</param>
    /// <param name="full">부위의 처음 값(체력+1). <c>S(full)</c> 부터 검정이다.</param>
    private static void Paint(BarView view, int pale, int red, int full)
    {
        int width = DuelArt.Slots.BarW;
        int black = Scale(full);

        view.Keep.Width = width;
        view.Fresh.Width = width - Math.Clamp(pale, 0, width);
        view.Lost.Width = width - Math.Clamp(red, 0, width);
        view.Black.Width = width - black;
    }

    /// <summary>
    /// 맞은 줄의 <b>틱 11</b> — 깎인 몫을 한꺼번에 옅은 나뭇결로 찍고 빨강이 차는 결을 건다.
    /// </summary>
    /// <remarks>
    /// 한 틱 함수 <c>0x004A6730</c> 의 <c>0x004A680F</c> 다.
    /// <code>
    ///   새 끝 +0x128 = S(사본)
    ///   옛 끝 +0x12C = S(앞 판 값) - 1
    ///   새 끝 .. 옛 끝 : 조각 8          ; 빨강은 아직 S(앞 판 값) 부터
    ///   걸음수 +0x130 = 1
    /// </code>
    /// 맞은 쪽만 돈다 — 값이 안 바뀐 줄(막힌 쪽·안 맞은 부위)은 그대로 둔다.
    /// </remarks>
    private void StartHurt()
    {
        bool any = false;
        for (int i = 0; i < Duel.Lines; i++)
        {
            any |= StartHurt(_mine[i], _duel.MyParts[i], _wasMine[i], _duel.MyFull);
            any |= StartHurt(_theirs[i], _duel.FoeParts[i], _wasFoe[i], _duel.FoeFull);
        }
        if (any) _hurtTimer.Start();
    }

    private static bool StartHurt(BarView view, int now, int was, int full)
    {
        if (now == was) return false;

        view.Hurting = true;
        view.NewEnd = Scale(now);
        view.OldEnd = Scale(was) - 1;
        view.Step = 0;
        view.Count = 1;
        Paint(view, pale: view.NewEnd, red: Scale(was), full);
        return true;
    }

    /// <summary>
    /// 틱 12 부터 — 빨강이 옛 끝에서 새 끝 쪽으로 <b>한 틱에 한 걸음씩</b> 파고든다.
    /// </summary>
    /// <remarks>
    /// <c>0x004A69CD</c> · <c>0x004A69F8</c>.
    /// <code>
    ///   걸음 +0xF0 = (옛 끝 - 새 끝) / 8 + 1      ; 처음 한 번, 버림
    ///   k = 옛 끝 - 걸음 x 걸음수
    ///   k > 새 끝 : [새 끝, k) 나뭇결 · [k, S(처음)) 빨강, 걸음수++
    ///   아니면    : [새 끝, S(처음)) 모두 빨강, 앞 판 값 = 사본 (0x004A6AB3)
    /// </code>
    /// 곧 깎인 것이 8점을 넘으면 여덟 틱쯤, 8점 아래면 깎인 점수만큼 틱이 걸린다.
    /// </remarks>
    private void HurtTick()
    {
        bool any = false;
        for (int i = 0; i < Duel.Lines; i++)
        {
            if (HurtTick(_mine[i], _duel.MyFull)) any = true;
            else _wasMine[i] = _duel.MyParts[i];

            if (HurtTick(_theirs[i], _duel.FoeFull)) any = true;
            else _wasFoe[i] = _duel.FoeParts[i];
        }
        if (!any) _hurtTimer.Stop();
    }

    /// <returns>아직 도는 중이면 true.</returns>
    private static bool HurtTick(BarView view, int full)
    {
        if (!view.Hurting) return false;

        // C# 의 정수 나눗셈도 0 쪽으로 버려 idiv 와 같다.
        if (view.Step == 0) view.Step = (view.OldEnd - view.NewEnd) / 8 + 1;

        int k = view.OldEnd - view.Step * view.Count;
        if (k > view.NewEnd)
        {
            Paint(view, pale: view.NewEnd, red: k, full);
            view.Count++;
            return true;
        }

        Paint(view, pale: DuelArt.Slots.BarW, red: view.NewEnd, full);
        view.Hurting = false;
        view.Step = view.Count = 0;
        return false;
    }

    /// <summary>
    /// 명령 단추의 폭 — 명령 이름 가운데 가장 긴 것에 맞춘다.
    /// </summary>
    /// <remarks>
    /// 명령은 죄다 넉 자 안쪽이다 — 공격과 필살이 「상단공격」·「상단필살」로 넉 자,
    /// 막기가 「뛴다」 두 자에서 「웅크린다」 넉 자다. 가장 긴 것으로 한 번 재어 붙박아
    /// 두면 어느 판에서나 창 폭이 같다.
    /// </remarks>
    private static double KeyWidth =>
        Duel.Attacks.Concat(Duel.Finishers).Concat(Duel.Guards).Max(GameUi.BandWidthFor);

    /// <summary>이번 판에 고를 손으로 단추를 다시 짓는다.</summary>
    private void Rebuild()
    {
        // 판을 열면 명령 칸을 비운다(0x004A6F39 가 [+0xE4] = 0) — 고르는 동안은 검게 비어 있다.
        _myMove.Text = "";
        _foeMove.Text = "";

        _keys.Children.Clear();
        var names = _duel.Choices();
        var focus = new GameUi.FocusGroup();

        // 게임은 명령을 <b>세로로 쌓아</b> 낸다 — 갈무리의 상단·중단·하단 공격이 한 줄씩이다.
        // 폭은 <b>명령 이름 가운데 가장 긴 것</b>에 맞춰 붙박는다 — 96 으로 박아 두었더니
        // 좌우가 휑했다.
        double width = KeyWidth;

        var column = new StackPanel();
        for (int i = 0; i < names.Length; i++)
        {
            int pick = i;
            // 명령을 누르면 칼 부딪히는 소리부터 낸다(사운드 ID 72).
            var key = focus.Add(names[i], () => { Clang(); Step(pick); }, width);
            key.Height = UiSprites.BandHeight;
            key.Margin = new Thickness(0, 0, 0, 2);
            column.Children.Add(key);
        }

        _keys.Children.Add(column);
        _keyBox.Visibility = Visibility.Visible;

        KeyDown -= OnKey;
        _focus = focus;
        KeyDown += OnKey;
    }

    private GameUi.FocusGroup? _focus;

    /// <summary>
    /// 명령을 고를 때 나는 칼 부딪히는 소리. 효과음을 못 열면 조용히 넘어간다.
    /// </summary>
    /// <remarks>
    /// 효과음 묶음은 <see cref="SoundBank.Shared"/> 가 한 벌만 들고 있고, 게임 폴더는
    /// 마지막으로 연 세이브 파일 자리에서 찾는다 — 이 창은 게임 판을 안 들고 있다.
    /// </remarks>
    private void Clang() => Sound(SoundBank.ClashPart);

    /// <summary>효과음 한 자락. 묶음을 못 열면 조용히 넘어간다.</summary>
    private static void Sound(int part)
    {
        var dir = System.IO.Path.GetDirectoryName(
            CdsHelper.Support.Local.Settings.AppSettings.LastSaveFilePath);
        if (string.IsNullOrEmpty(dir)) return;
        SoundBank.Shared(dir)?.Play(part);
    }

    private void OnKey(object sender, KeyEventArgs e)
    {
        if (_focus != null && _focus.HandleKey(e.Key)) e.Handled = true;
    }

    /// <summary>한 판을 치른다. 그림이 있으면 다 돌고 나서 말을 낸다.</summary>
    private void Step(int pick)
    {
        var turn = _duel.Play(pick);

        if (_stage == null) { Settle(turn); return; }

        // 명령을 고르고 나면 단추를 걷는다 — 그림이 도는 동안은 아무것도 못 누른다.
        _keys.Children.Clear();
        _keyBox.Visibility = Visibility.Collapsed;
        Speak("");                 // 새 판이 시작되면 앞 말은 걷는다
        _focus = null;

        // 앞 판의 빨강이 덜 찼으면 여기서 끝낸다 — 새 판은 다 찬 모양에서 시작한다.
        if (_hurtTimer.IsEnabled) Refresh();

        // 두 사람이 고른 명령은 <b>주고받기 틱 8</b>에 가운데 홈에 뜬다(0x004A6764).
        // 갈래는 <b>내 쪽에서 본 것</b>이라 상대는 뒤집어 읽어야 한다 — 내가 치는 판이면
        // 상대는 막는 것이고, 내가 막는 판이면 상대가 친다. 안 뒤집었더니 상대가 웅크렸는데
        // 「하단공격」이라고 떴다.
        string myName = MoveName(turn.Was, turn.MyMove);
        string foeName = MoveName(Flip(turn.Was), turn.FoeMove);

        // 판 갈래대로 두 사람이 통째로 마흔 점 옮겨 간다(0x004A6EE5) — 내가 몰아붙이면
        // 상대 쪽으로, 막기만 하면 내 쪽으로다. 맞부딪힘은 제자리다. 맞았는지는 안 본다.
        // 이 판이 시작할 때 옮겨 가므로 <b>공격이면 다가서며 찌르고 방어면 물러나면서</b>
        // 뛴다 — 앞 판에서 옮겨 두면 뛰는 판에 앞으로 나가는 꼴이 된다.
        var (mine, theirs) = Moves(turn);
        int way = turn.Was switch
        {
            Duel.Phase.Attack => -1,
            Duel.Phase.Guard => +1,
            _ => 0,
        };
        // <b>꼬리를 걷는다.</b> 한 판이 서른세 눈금이지만 볼 것은 그 앞쪽에서 끝난다 —
        // 찌르기는 눈금 15 에, 빨강은 눈금 16 에 다 찬다. 남은 눈금은 여느 자세로 서
        // 있기만 하므로 기다릴 까닭이 없다.
        int ticks = turn.Blow == Duel.Blow.Blocked ? DuelStage.ShortTicks : DuelStage.HitTicks;

        _stage.Play(mine, theirs, way, ticks,
                    onSay: () => { _myMove.Text = myName; _foeMove.Text = foeName; },
                    onHurt: StartHurt,
                    onDone: () => Settle(turn));
    }

    /// <summary>
    /// 이번 판에 두 사람이 지을 몸짓.
    /// </summary>
    /// <remarks>
    /// 앞으로 나가고 뒤로 물러나는 것은 <b>몸짓 표가 갖고 있다</b>(<see cref="DuelMotions"/>) —
    /// 여기서는 누가 찌르고 누가 막는지만 고른다.
    /// </remarks>
    private static (FighterSprites.Move Mine, FighterSprites.Move Theirs) Moves(in Duel.Turn turn)
    {
        static FighterSprites.Move Thrust(int line) => (FighterSprites.Move)line;
        static FighterSprites.Move Guard(int g) => (FighterSprites.Move)(3 + g);

        return turn.Was switch
        {
            // 맞부딪힘 — 둘이 한꺼번에 내지른다.
            Duel.Phase.Clash => (Thrust(turn.MyMove), Thrust(turn.FoeMove)),

            // 내가 친다 — 상대는 막는 몸짓이다. 필살도 <b>여느 찌르는 그림</b>을 쓴다.
            // 스프라이트셋 6 은 이겼을 때의 몸짓이라 여기에 걸 것이 아니다(FighterSprites.Move.Victory).
            Duel.Phase.Attack => (Thrust(turn.MyMove), Guard(turn.FoeMove)),

            // 내가 막는다.
            _ => (Guard(turn.MyMove), Thrust(turn.FoeMove)),
        };
    }

    /// <summary>판이 끝난 자리 — 말을 내고 다음 명령을 묻는다.</summary>
    private void Settle(in Duel.Turn turn)
    {
        // 판 그림은 꼬리를 걷어 틱 18 에 끝나지만 빨강은 여덟 틱쯤 걸릴 수 있다 — 도는 중이면
        // 끝까지 두고, 아니면(그림 없는 판) 곧바로 가만히 있는 모양으로 그린다.
        if (!_hurtTimer.IsEnabled) Refresh();

        if (_duel.Over)
        {
            // 끝판에는 <b>비아냥도 「확인」도 없다</b> — 게임은 판이 끝나면 곧바로 뒤처리
            // (0x004A9E50)로 넘어간다. 예전에는 끝판에도 「아직이다. 아직 끝나지 않았다…」 같은
            // 판 중 말풍선을 띄우고 확인 단추를 세웠다. 쓰러지는 모습만 잠깐 보여 주고 닫는다 —
            // 뒤의 말(처형·놓아 준다·모두 뺏는다, 반란 진압)은 부른 쪽이 낸다.
            Speak("");
            // 이겼으면 77, 졌으면 74 가 난다(0x004A6FBF).
            Sound(_duel.Won == true ? SoundBank.DuelWinPart : SoundBank.DuelLosePart);
            _stage?.Fall(mine: _duel.Won != true);
            _keys.Children.Clear();
            _keyBox.Visibility = Visibility.Collapsed;
            _focus = null;

            // 끝맺는 몸짓(쓰러짐·승리)이 다 돌고 나서 닫는다 — 열여섯 눈금에 한 바퀴라
            // 비아냥 눈금(15)으로는 마지막 장을 못 보고 닫혔다.
            var end = new DispatcherTimer(DispatcherPriority.Render)
            {
                Interval = TimeSpan.FromSeconds(DuelMotions.Tick * EndTicks),
            };
            end.Tick += (_, _) =>
            {
                end.Stop();
                DialogResult = _duel.Won;
            };
            end.Start();
            return;
        }

        // 상대의 말은 판 위 흰 말풍선으로 난다 — 게임도 그 자리다.
        Speak(Taunt(turn));

        _stage?.Rest();

        // 말풍선과 명령 창은 <b>같이 서 있지 않는다</b> — 말이 잠깐 떴다 사라지고 나서야
        // 명령 창이 뜬다. 할 말이 없는 판(맞부딪힘)은 곧바로 낸다.
        if (Taunt(turn).Length == 0) { Rebuild(); return; }

        _keyBox.Visibility = Visibility.Collapsed;
        var wait = new DispatcherTimer(DispatcherPriority.Render)
        {
            Interval = TimeSpan.FromSeconds(DuelMotions.Tick * TauntTicks),
        };
        wait.Tick += (_, _) =>
        {
            wait.Stop();
            Speak("");
            Rebuild();
        };
        wait.Start();
    }

    /// <summary>상대의 말이 떠 있는 눈금 — 이만큼 지나면 걷고 명령 창을 낸다.</summary>
    private const int TauntTicks = 15;

    /// <summary>판이 끝나고 닫히기까지 — 끝맺는 몸짓 한 바퀴(16눈금)에 한 박자 더.</summary>
    private const int EndTicks = 18;

    // 「이번 판에 무엇이 오갔는지」를 한 줄로 적던 줄은 걷었다. 게임은 그런 줄을 안
    // 낸다 — 오간 명령은 눈금판 가운데 라벨 둘이, 맞고 안 맞고는 그림과 체력 막대가
    // 일러 준다. 말풍선에는 상대의 <b>비아냥</b>만 뜬다.

    /// <summary>상대가 하는 말. 어느 줄에서 고를지는 게임과 같다(<c>0x004A6E77</c>).</summary>
    private string Taunt(in Duel.Turn turn)
    {
        if (turn.Was == Duel.Phase.Clash) return "";
        int group = turn.Blow switch
        {
            Duel.Blow.MeHit or Duel.Blow.MeGrazed => 0,
            Duel.Blow.Blocked => turn.Was == Duel.Phase.Attack ? 1 : 2,
            _ => 3,
        };
        var row = Taunts[group];
        return row[_dice.Next(row.Length)];
    }

    /// <summary>판이 열리기 직전 — 바다 지도가 비·눈을 거둔다(<c>0x004AA87F</c>).</summary>
    public static event Action? Opening;

    /// <summary>판을 연다. 이겼으면 true.</summary>
    /// <param name="art">싸움 그림. 없으면 막대와 글로만 낸다.</param>
    /// <param name="foeSet">상대 스프라이트셋(1~8).</param>
    /// <param name="bgm">
    /// 배경음악. 주면 판이 도는 동안 일기토 곡(<see cref="BgmPlayer.DuelTrack"/>, 트랙 11)을 틀고
    /// 끝나면 앞서 돌던 곡으로 되돌린다 — 게임도 들머리에서 곡을 갈아 튼다(<c>0x004AA8A0</c>).
    /// </param>
    public static bool Show(Window owner, Duel duel, GameRandom dice, uint[]? face,
                            FighterSprites? art = null, int foeSet = 1,
                            uint[]? myFace = null, string arena = DuelArt.Field,
                            BgmPlayer? bgm = null)
    {
        Opening?.Invoke();
        int before = bgm?.Track ?? -1;
        bgm?.Play(BgmPlayer.DuelTrack);
        try
        {
            var window = new DuelDialog(duel, dice, face, myFace, art, foeSet,
                                        DuelArt.Open(), arena) { Owner = owner };
            window.ShowDialog();
        }
        finally
        {
            // 앞 곡으로 곧바로 돌린다 — 해상 곡 맞추기(PlayWhenDone)에 맡기면 일기토 곡이 끝까지 돈다.
            if (bgm != null && before >= 0) bgm.Play(before);
        }
        return duel.Won == true;
    }
}
