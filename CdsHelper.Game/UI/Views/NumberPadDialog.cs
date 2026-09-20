using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CdsHelper.Game.Local.Helpers;

namespace CdsHelper.Game.UI.Views;

/// <summary>
/// 숫자를 찍어 넣는 계산기 판 — 연령·생일처럼 작은 칸 옆의 田 단추가 연다.
/// </summary>
/// <remarks>
/// 게임 화면 그대로다. 위에 지금 값이 크게 뜨고 밑에 스무 칸이 있다.
/// <code>
///   7  8  9   AC
///   4  5  6   DEL
///   1  2  3   MAX
///   0  00 000 MIN
///   ENTER         CAN/CEL
/// </code>
/// <b>AC</b> 는 통째로 지우고 <b>DEL</b> 은 한 자리 지운다. <b>MAX·MIN</b> 은 그 칸이 받을
/// 수 있는 가장 큰 값과 작은 값을 바로 넣는다 — 게임에도 있는 단추다.
///
/// 판 조각은 MISC.CDS 에서 온다(<see cref="GameUi"/> 의 띠 단추와 같은 벌) — 게임도
/// 같은 조각으로 찍는다. 조각을 못 읽으면 민색 네모로 물러선다.
///
/// 위 칸의 값은 <b>게임 숫자 글꼴</b>로 찍는다(MISC.CDS 파트 7, 24x24 기울임체 열 장 ·
/// <see cref="UiSprites.Digit"/>). 조각이 없을 때만 윈도 기울임꼴로 물러선다.
/// </remarks>
internal sealed class NumberPadDialog : GameWindow
{
    /// <summary>값이 찍히는 칸. 게임 숫자 조각을 이어 붙인 그림이다.</summary>
    private readonly Image _digits = new()
    {
        Stretch = Stretch.None,
        HorizontalAlignment = HorizontalAlignment.Right,
        VerticalAlignment = VerticalAlignment.Center,
        Margin = new Thickness(8, 2, 10, 2),
    };

    private readonly GameUi.GameLabel _screen;
    private readonly int _min, _max;
    private string _typed;
    private int? _result;

    private NumberPadDialog(int start, int min, int max, string prompt)
    {
        _min = min;
        _max = max;
        _typed = $"{Math.Clamp(start, min, max)}";

        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.WidthAndHeight;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        Background = GameUi.Back;

        // 숫자 표시줄도 게임 글꼴이다. 게임은 계산기 칸을 <b>기울여 찍지 않는다</b> —
        // 예전에는 WPF 글자를 기울여 계산기처럼 보이게 했었다.
        _screen = new GameUi.GameLabel(GameFont.BlackColor)
        {
            Bold = false,
            FallbackBrush = System.Windows.Media.Brushes.Black,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(8, 2, 10, 2),
        };

        // 게임 조각이 다 있으면 <b>원본 판 그대로</b> 짓는다 — 돌무늬 판 한 장 위에 글쇠를 제자리에
        // 얹는다(MISC.CDS 파트 6, 짓기 0x004821C0). 조각이 모자랄 때만 아래의 옛 판으로 물러선다.
        if (GameBoard() is { } board)
        {
            _gameBoard = true;
            Content = prompt.Length > 0 ? WithPrompt(prompt, board) : board;
            Sync();
            HookKeys();
            return;
        }

        // 게임 글쇠 그림이 있으면 그것으로, 없으면 예전처럼 띠 단추로 짓는다.
        var grid = new StackPanel { Margin = new Thickness(4) };
        grid.Children.Add(Line((7, Type), (8, Type), (9, Type), (UiSprites.PadClear, Clear)));
        grid.Children.Add(Line((4, Type), (5, Type), (6, Type), (UiSprites.PadBack, Back)));
        grid.Children.Add(Line((1, Type), (2, Type), (3, Type), (UiSprites.PadMost, Most)));
        grid.Children.Add(Line((0, Type), (UiSprites.PadDoubleZero, Type),
                               (UiSprites.PadTripleZero, Type), (UiSprites.PadLeast, Least)));

        var last = new StackPanel { Orientation = Orientation.Horizontal };
        last.Children.Add(Wide("ENTER", Enter));
        last.Children.Add(PadKey(UiSprites.PadCancel, _ => Close()));
        grid.Children.Add(last);

        RenderOptions.SetBitmapScalingMode(_digits, GameUi.SpriteScaling);
        RenderOptions.SetEdgeMode(_digits, EdgeMode.Aliased);

        // 숫자 조각이 있으면 그림으로, 없으면 윈도 글꼴로 찍는다.
        bool art = GameUi.Sprites?.HasDigits == true;
        _screen.Visibility = art ? Visibility.Collapsed : Visibility.Visible;
        _digits.Visibility = art ? Visibility.Visible : Visibility.Collapsed;

        var stack = new StackPanel();

        // 물음이 딸려 오면 숫자 칸 위에 한 줄로 얹는다 — 발라몬의 탑이 «판자를 몇 장
        // 사용하겠습니까?» 를 그렇게 묻는다(0x0045FB79). 안 주면 예전처럼 숫자만 뜬다.
        if (prompt.Length > 0)
            stack.Children.Add(new GameUi.GameLabel(GameFont.WhiteColor)
            {
                Text = prompt,
                Bold = false,
                FallbackBrush = System.Windows.Media.Brushes.White,
                Margin = new Thickness(8, 6, 8, 0),
            });

        stack.Children.Add(new Border
        {
            Background = GameUi.PageFill,
            BorderBrush = GameUi.ItemEdge,
            BorderThickness = new Thickness(2),
            Margin = new Thickness(6, 6, 6, 2),
            MinHeight = UiSprites.DigitHeight + 8,
            Child = new Grid { Children = { _screen, _digits } },
        });
        stack.Children.Add(grid);

        Content = new Border
        {
            Background = GameUi.Back,
            BorderBrush = GameUi.Edge,
            BorderThickness = new Thickness(2),
            Margin = new Thickness(4),
            Child = stack,
        };

        Sync();
        HookKeys();
    }

    /// <summary>원본 판으로 지었는지. 숫자를 겹쳐 오른쪽부터 찍는 방식이 여기서 갈린다.</summary>
    private bool _gameBoard;

    /// <summary>글쇠판을 거치지 않는 입력 — ESC · Enter · 숫자 · Backspace · 오른쪽 단추.</summary>
    private void HookKeys()
    {
        KeyDown += (_, e) =>
        {
            if (e.Key is Key.Escape) Close();
            else if (e.Key is Key.Enter) Enter();
            else if (e.Key is >= Key.D0 and <= Key.D9) Type($"{e.Key - Key.D0}");
            else if (e.Key is >= Key.NumPad0 and <= Key.NumPad9) Type($"{e.Key - Key.NumPad0}");
            else if (e.Key is Key.Back) Back("");
        };
        MouseRightButtonUp += (_, _) => Close();
    }

    // ── 원본 판 ────────────────────────────────────────────────────────────────

    /// <summary>
    /// 글쇠 자리 — 게임 <c>0x004822E0</c> 의 <c>x = 8 + 열*0x24</c> · <c>y = 0x38 + 줄*0x20</c>.
    /// </summary>
    private const int KeyLeft = 8, KeyTop = 56, KeyStepX = 36, KeyStepY = 32;

    /// <summary>ENTER · CANCEL 이 서는 줄(<c>+0xB8</c>)과 CANCEL 의 x(<c>+0x74</c>).</summary>
    private const int BottomTop = 184, CancelLeft = 116;

    /// <summary>
    /// 표시 숫자 — 일의 자리 오른끝이 x 155(<c>0x98 − 0x15 + 24</c>), 한 자리마다 21 씩 왼쪽,
    /// y 20(<c>0x00482880</c>). 숫자 폭 24 보다 좁게 가서 세 점씩 겹친다.
    /// </summary>
    private const int DigitRight = 155, DigitTop = 20, DigitStep = 21;

    /// <summary>
    /// 원본 계산기 판을 짓는다. 판 바탕·ENTER·숫자 조각 가운데 하나라도 없으면 null.
    /// </summary>
    /// <remarks>
    /// 배치는 표 <c>0x00569370</c>(갈래·번호·ID) 그대로다 — 왼쪽 세 칸이 숫자, 오른쪽 칸이
    /// AC · DEL · MAX · MIN. 게임은 CANCEL 을 물릴 수 있는 창일 때만 세우는데 이 판은 늘 물릴
    /// 수 있어 늘 세운다.
    /// </remarks>
    private UIElement? GameBoard()
    {
        var sprites = GameUi.Sprites;
        if (sprites?.HasDigits != true) return null;
        if (sprites.Panel() is not { } panel || sprites.Enter() is not { } enter) return null;

        var board = new Canvas { Width = UiSprites.PanelWidth, Height = UiSprites.PanelHeight };
        board.Children.Add(Sprite(panel, UiSprites.PanelWidth, UiSprites.PanelHeight));

        int[,] keys =
        {
            { 7, 8, 9, UiSprites.PadClear },
            { 4, 5, 6, UiSprites.PadBack },
            { 1, 2, 3, UiSprites.PadMost },
            { 0, UiSprites.PadDoubleZero, UiSprites.PadTripleZero, UiSprites.PadLeast },
        };
        for (int row = 0; row < 4; row++)
            for (int col = 0; col < 4; col++)
            {
                int key = keys[row, col];
                var face = PadKey(key, RunOf(key), zoom: 1);
                Canvas.SetLeft(face, KeyLeft + col * KeyStepX);
                Canvas.SetTop(face, KeyTop + row * KeyStepY);
                board.Children.Add(face);
            }

        var enterKey = Clickable(Sprite(enter, UiSprites.EnterWidth, UiSprites.PadHeight), Enter);
        Canvas.SetLeft(enterKey, KeyLeft);
        Canvas.SetTop(enterKey, BottomTop);
        board.Children.Add(enterKey);

        var cancel = PadKey(UiSprites.PadCancel, _ => Close(), zoom: 1);
        Canvas.SetLeft(cancel, CancelLeft);
        Canvas.SetTop(cancel, BottomTop);
        board.Children.Add(cancel);

        _digits.Margin = default;
        _digits.HorizontalAlignment = HorizontalAlignment.Left;
        _digits.VerticalAlignment = VerticalAlignment.Top;
        RenderOptions.SetBitmapScalingMode(_digits, GameUi.SpriteScaling);
        RenderOptions.SetEdgeMode(_digits, EdgeMode.Aliased);
        Canvas.SetTop(_digits, DigitTop);
        board.Children.Add(_digits);
        return board;
    }

    /// <summary>글쇠 번호마다 할 일.</summary>
    private Action<string> RunOf(int key) => key switch
    {
        UiSprites.PadClear => Clear,
        UiSprites.PadBack => Back,
        UiSprites.PadMost => Most,
        UiSprites.PadLeast => Least,
        _ => Type,
    };

    /// <summary>물음이 딸려 오면 판 위에 한 줄을 얹는다.</summary>
    private static UIElement WithPrompt(string prompt, UIElement board) => new StackPanel
    {
        Background = GameUi.Back,
        Children =
        {
            new GameUi.GameLabel(GameFont.WhiteColor)
            {
                Text = prompt,
                Bold = false,
                FallbackBrush = System.Windows.Media.Brushes.White,
                Margin = new Thickness(8, 6, 8, 6),
            },
            board,
        },
    };

    /// <summary>BGRA 조각 한 장을 제 크기로 거는 그림.</summary>
    private static Image Sprite(uint[] px, int width, int height)
    {
        var bmp = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, px, width * 4);
        bmp.Freeze();
        var image = new Image { Source = bmp, Width = width, Height = height, Stretch = Stretch.Fill };
        RenderOptions.SetBitmapScalingMode(image, GameUi.SpriteScaling);
        RenderOptions.SetEdgeMode(image, EdgeMode.Aliased);
        return image;
    }

    /// <summary>눌리는 껍데기를 씌운다.</summary>
    private static Border Clickable(UIElement child, Action run)
    {
        var box = new Border
        {
            Child = child,
            Cursor = System.Windows.Input.Cursors.Hand,
            Background = System.Windows.Media.Brushes.Transparent,
        };
        box.MouseLeftButtonDown += (_, e) => e.Handled = true;
        box.MouseLeftButtonUp += (_, e) => { e.Handled = true; run(); };
        return box;
    }

    /// <summary>
    /// 원본 판의 표시 숫자. 일의 자리부터 오른쪽에서 왼쪽으로 찍는다 — 뒤에 찍는 윗자리가 겹친
    /// 세 점을 덮는다(<c>0x004828AD</c> 의 <c>ebx −= 0x15</c>).
    /// </summary>
    private static BitmapSource? PrintGame(string text)
    {
        var sprites = GameUi.Sprites;
        if (sprites?.HasDigits != true || text.Length == 0) return null;

        int w = UiSprites.DigitWidth, h = UiSprites.DigitHeight;
        int width = w + (text.Length - 1) * DigitStep;
        var all = new uint[width * h];
        for (int k = text.Length - 1; k >= 0; k--)
        {
            var one = sprites.Digit(text[k] - '0');
            if (one == null) continue;
            int left = k * DigitStep;
            for (int r = 0; r < h; r++)
                for (int c = 0; c < w; c++)
                {
                    uint p = one[r * w + c];
                    if (p >> 24 == 0) continue;        // 비침
                    all[r * width + left + c] = p;
                }
        }
        return BitmapSource.Create(width, h, 96, 96, PixelFormats.Bgra32, null, all, width * 4);
    }

    /// <summary>글쇠를 몇 배로 키워 걸지. 조각이 32x24 라 두 배가 보기 좋다.</summary>
    private const int KeyZoom = 2;

    /// <summary>조각이 없을 때 물러설 띠 단추의 너비.</summary>
    private const double KeyWidth = 56;

    /// <summary>글쇠 번호마다 눌렀을 때 넘길 글. 숫자는 그대로, 나머지는 이름이다.</summary>
    private static readonly string[] KeyText =
    [
        "0", "1", "2", "3", "4", "5", "6", "7", "8", "9",
        "00", "000", "AC", "DEL", "MAX", "MIN", "CANCEL",
    ];

    private StackPanel Line(params (int Key, Action<string> Run)[] keys)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        foreach (var (key, run) in keys) row.Children.Add(PadKey(key, run));
        return row;
    }

    /// <summary>
    /// 글쇠 한 칸. <b>게임 조각</b>이 있으면 그것으로, 없으면 띠 단추로 찍는다.
    /// </summary>
    /// <param name="zoom">몇 배로 걸지. 원본 판은 제 크기(1), 옛 판은 두 배다.</param>
    private static UIElement PadKey(int key, Action<string> run, int zoom = KeyZoom)
    {
        string text = KeyText[key];
        if (GameUi.Sprites?.Pad(key) is not { } px) return Band(text, run, KeyWidth);

        var image = Sprite(px, UiSprites.PadWidth, UiSprites.PadHeight);
        image.Width = UiSprites.PadWidth * zoom;
        image.Height = UiSprites.PadHeight * zoom;
        return Clickable(image, () => run(text));
    }

    /// <summary>
    /// 가로로 긴 글쇠(ENTER). <b>조각 벌에 없어서</b> 띠 단추로 그린다.
    /// </summary>
    private UIElement Wide(string text, Action run)
    {
        double width = GameUi.Sprites?.HasPad == true
            ? UiSprites.PadWidth * KeyZoom * 3 : KeyWidth * 3 + 4;
        return Band(text, _ => run(), width);
    }

    /// <summary>게임 띠 단추로 찍는 글쇠.</summary>
    private static UIElement Band(string text, Action<string> run, double width) =>
        new GameButton(text, () => run(text), BandStyle.Button, width)
        {
            Margin = default,
        };

    private void Type(string digits)
    {
        // 앞자리 0 은 안 쌓는다. 넘치면 안 받는다.
        string next = (_typed == "0" ? "" : _typed) + digits;
        if (next.Length > 9) return;
        if (int.TryParse(next, out int n) && n <= _max) { _typed = $"{n}"; Sync(); }
    }

    private void Clear(string _) { _typed = "0"; Sync(); }

    private void Back(string _)
    {
        _typed = _typed.Length <= 1 ? "0" : _typed[..^1];
        Sync();
    }

    private void Most(string _) { _typed = $"{_max}"; Sync(); }

    private void Least(string _) { _typed = $"{_min}"; Sync(); }

    private void Sync()
    {
        _screen.Text = _typed;
        if (!_gameBoard)
        {
            _digits.Source = Print(_typed);
            return;
        }

        // 원본 판은 일의 자리 오른끝을 한자리에 못 박고 왼쪽으로 늘여 간다.
        var printed = PrintGame(_typed);
        _digits.Source = printed;
        if (printed != null) Canvas.SetLeft(_digits, DigitRight - printed.PixelWidth);
    }

    /// <summary>
    /// 값을 게임 숫자 조각으로 찍는다. 조각이 없으면 null 이라 글자 칸이 대신 나온다.
    /// </summary>
    private static BitmapSource? Print(string text)
    {
        var sprites = GameUi.Sprites;
        if (sprites?.HasDigits != true || text.Length == 0) return null;

        int w = UiSprites.DigitWidth, h = UiSprites.DigitHeight;
        var all = new uint[w * text.Length * h];
        int stride = w * text.Length;
        for (int k = 0; k < text.Length; k++)
        {
            var one = sprites.Digit(text[k] - '0');
            if (one == null) continue;
            for (int r = 0; r < h; r++)
                Array.Copy(one, r * w, all, r * stride + k * w, w);
        }
        return BitmapSource.Create(stride, h, 96, 96, PixelFormats.Bgra32, null, all, stride * 4);
    }

    private void Enter()
    {
        if (!int.TryParse(_typed, out int n)) { Close(); return; }
        _result = Math.Clamp(n, _min, _max);
        Close();
    }

    /// <summary>
    /// 판을 띄우고 찍은 수를 낸다. 물렀으면 null.
    /// </summary>
    /// <param name="owner">주인 창.</param>
    /// <param name="start">처음 들어 있을 수.</param>
    /// <param name="min">가장 작은 값(MIN 단추가 넣는 값).</param>
    /// <param name="max">가장 큰 값(MAX 단추가 넣는 값).</param>
    /// <param name="prompt">숫자 칸 위에 얹을 물음. 안 주면 숫자만 뜬다.</param>
    public static int? Ask(Window owner, int start, int min, int max, string prompt = "")
    {
        var dialog = new NumberPadDialog(start, min, max, prompt) { Owner = owner };
        dialog.ShowDialog();
        return dialog._result;
    }
}
