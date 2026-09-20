using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CdsHelper.Game.Local.Helpers;

namespace CdsHelper.Game.UI.Views;

/// <summary>
/// 펼친 책(<c>COpenBookWindow</c>, 만들기 <c>0x00464680</c>) — 도서관에서 책을 누르면 이 창으로 편다.
/// </summary>
/// <remarks>
/// 그림은 <see cref="OpenBookArt"/> 가 낸다. 붉은 가죽 틀(544x304) 위에 낱장 둘을
/// <c>(16,8)</c> 과 <c>(272,8)</c> 에 얹고, <b>오른쪽 면에만 글이 앉는다</b> —
/// 왼쪽 면은 낱장 그림에 이미 찍혀 있는 라틴어 흉내다.
///
/// <b>책의 힌트 칸 하나가 펼침면 하나다</b>(볼트 <c>89.분석-책 읽기</c>). 칸 <c>i</c> 의 쪽 번호는
/// <c>-(2i+1)-</c> · <c>-(2i+2)-</c> 이고, 처음 펴면 칸 0 이다(<c>0x00464882</c>). 넘기는 길은 넷이다.
/// <code>
///   모서리 단추   왼쪽 (21,280) 그림 12 — i &gt; 0 일 때만 보인다
///                 오른쪽 (510,280) 그림 11 — i &lt; 7 이고 칸 i+1 이 있을 때만 보인다(0x004649C0)
///   ← / →        켜진 단추를 누른 것과 같다(0x00465230)
///   창 몸통 클릭  오른쪽 반이면 다음, 왼쪽 반이면 앞(0x004654E0)
///   마우스 올림   창 아래 모서리 밖에 96x24 「다음장」/「앞장」(0x00465360)
/// </code>
/// 면이 화면에 나올 때마다(처음 펼 때 한 번, 넘길 때마다 한 번) 부른 쪽 알림을 부른다 —
/// 게임이 그 자리에서 힌트를 주고 띠 말을 내는 <c>0x00464A30</c> 이다.
/// </remarks>
public sealed class OpenBookDialog : GameWindow
{
    /// <summary>
    /// 펼침면 하나에 그릴 것 — 부른 쪽이 게임 규칙(<c>0x00464C50</c>)대로 채운다.
    /// </summary>
    /// <param name="HasHint">
    /// 힌트 칸이 차 있는지. 비었으면(-1) 쪽 번호도 없다.
    /// </param>
    /// <param name="Illustration">
    /// 왼쪽 면에 얹을 삽화 그림 번호. -1 이면 없다. 개방·선행 발견물만 보고 얹는다 —
    /// 기능·언어가 모자라도 삽화는 나온다.
    /// </param>
    /// <param name="Yellow">누런 종이 벌을 쓰는지 — 힌트 상태 <c>(+4 &amp; 3) == 3</c>(찾아서 보고함).</param>
    /// <param name="Title">제목. 못 읽는 면이면 빈 문자열이라 오른쪽 면이 빈 종이다.</param>
    /// <param name="Text">본문. 제목과 같이 비운다.</param>
    public readonly record struct Spread(bool HasHint, int Illustration, bool Yellow,
                                         string Title, string Text);

    /// <summary>책 한 권의 힌트 칸 수 — 펼침면도 이만큼이다.</summary>
    public const int MaxSpreads = 8;

    /// <summary>제목 칸 (288,16) 224x32 — <c>0x005AA488</c>.</summary>
    private const double TitleX = 288, TitleY = 16, TitleHeight = 32;

    /// <summary>본문 칸 (288,48) 224x208 — <c>0x005AA438</c>.</summary>
    private const double BodyX = 288, BodyY = 48, BodyHeight = 208;

    /// <summary>두 칸의 너비. 글상자의 칸은 8x16 이고 줄바꿈이 켜져 있다.</summary>
    private const double TextWidth = 224, LineHeight = 16;

    /// <summary>
    /// 본문 줄 높이 — 글자 16 에 줄 사이 4 를 더한 <b>20</b> 이다. 게임 글 창이 늘 이 피치로 찍는다
    /// (물음창 높이 <c>줄수 x 20 + 71</c>, <c>0x0049D7B0</c>). 16 으로 붙여 찍으면 원본보다 줄이 빽빽하다.
    /// 제목 칸(32)은 한 줄뿐이라 그대로 16 이다.
    /// </summary>
    private const double BodyLineHeight = 20;

    /// <summary>
    /// 쪽 번호 자리 — (112,280) · (368,280) 에서 <b>왼쪽 맞춤</b>이다. 두 면 가운데(144/400)에서
    /// 32 왼쪽일 뿐 가운데 맞춤이 아니다(<c>0x005AA410</c>).
    /// </summary>
    private const double LeftNumberX = 112, RightNumberX = 368, NumberY = 280;

    /// <summary>모서리 단추 자리(<c>0x005AA458</c> / <c>0x005AA470</c>)와 크기.</summary>
    private const double PreviousX = 21, NextX = 510, CornerY = 280, CornerSize = 16;

    /// <summary>삽화 자리 — 가로 그림은 (24,30), 세로 그림은 (30,26).</summary>
    private const double WideX = 24, WideY = 30, TallX = 30, TallY = 26;

    /// <summary>말풍선 크기(<c>0x00465360</c>).</summary>
    private const double HoverWidth = 96, HoverHeight = 24;

    /// <summary>한 줄에 드는 글자 폭. 게임 글꼴은 한글 한 자가 두 칸(16점)이다.</summary>
    private const double CellWidth = 8;

    private readonly OpenBookArt _art;
    private readonly int _scale;
    private readonly int _count;
    private readonly int _firstPage;
    private readonly Func<int, Spread> _spreadAt;
    private readonly Action<int>? _shown;

    private readonly Canvas _canvas;
    private readonly Canvas _pages = new();
    private readonly FrameworkElement _previous, _next;
    private readonly Popup _hover;
    private readonly GameUi.GameLabel _hoverText;

    /// <summary>지금 펼친 면(<c>+0xB0</c>).</summary>
    private int _index;

    private OpenBookDialog(OpenBookArt art, int count, int firstPage, Func<int, Spread> spreadAt,
                           Action<int>? shown, int scale)
    {
        _art = art;
        _scale = scale;
        _count = Math.Clamp(count, 0, MaxSpreads);
        _firstPage = firstPage;
        _spreadAt = spreadAt;
        _shown = shown;

        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.WidthAndHeight;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        // 책 틀이 네모라 비침이 필요 없다 — 레이어드 창은 겹칠 때마다 깜빡인다.
        Background = GameUi.Back;

        _canvas = new Canvas
        {
            Width = OpenBookArt.FrameWidth * scale,
            Height = OpenBookArt.FrameHeight * scale,
            Background = GameUi.Back,
        };

        _canvas.Children.Add(Picture(OpenBookArt.Frame, 0, 0));
        _canvas.Children.Add(_pages);

        _previous = Corner(OpenBookArt.PreviousCorner, PreviousX, () => Turn(_index - 1));
        _next = Corner(OpenBookArt.NextCorner, NextX, () => Turn(_index + 1));

        // 닫기 단추는 오른쪽 위 모서리다 — 게임 창들과 같은 X 상자다.
        var close = GameUi.CloseBox(Close, scale);
        close.Margin = new Thickness(0);
        // 누른 자리에서 바로 닫는다. 창 끌기(DragMove)가 마우스를 붙들어
        // 버려서 ButtonUp 이 이 단추까지 오지 않는다 — 그래서 눌러도 안 닫혔다.
        close.MouseLeftButtonDown += (_, e) => { e.Handled = true; Close(); };
        Canvas.SetLeft(close, (OpenBookArt.FrameWidth - 32) * scale);
        Canvas.SetTop(close, 16 * scale);
        Panel.SetZIndex(close, 10);
        _canvas.Children.Add(close);

        // 말풍선은 창 <b>밖</b>(아래 모서리)에 뜬다 — 창 안에 둘 수 없어 따로 띄운다.
        var (tag, text) = GameUi.HoverTag();
        tag.Visibility = Visibility.Visible;
        tag.Width = HoverWidth * scale;
        tag.Height = HoverHeight * scale;
        _hoverText = text;
        _hover = new Popup
        {
            PlacementTarget = _canvas,
            Placement = PlacementMode.Relative,
            AllowsTransparency = false,
            Focusable = false,
            Child = tag,
        };

        Content = _canvas;

        _canvas.MouseLeftButtonDown += OnBodyDown;
        _canvas.MouseMove += (_, e) => Hover(e.GetPosition(_canvas));
        _canvas.MouseLeave += (_, _) => _hover.IsOpen = false;
        Deactivated += (_, _) => _hover.IsOpen = false;
        Closed += (_, _) => _hover.IsOpen = false;

        KeyDown += OnKey;
        MouseRightButtonUp += (_, _) => Close();

        ShowSpread();
        // 창을 열 때 한 번 — 게임도 창을 세우며 0x00464A30 을 부른다(0x00464890).
        Loaded += (_, _) => _shown?.Invoke(_index);
    }

    /// <summary>앞장으로 갈 수 있는지(<c>0x004649C0</c> 왼쪽 단추).</summary>
    private bool CanGoBack => _index > 0;

    /// <summary>다음장으로 갈 수 있는지 — 다음 칸이 차 있어야 한다.</summary>
    private bool CanGoOn => _index < MaxSpreads - 1 && _index + 1 < _count;

    /// <summary>
    /// 면을 넘긴다 — 말풍선 치우기, 단추 갱신, 다시 그리기, 힌트 주기 차례다(<c>0x00465303</c>).
    /// </summary>
    private void Turn(int to)
    {
        if (to == _index || to < 0 || to >= MaxSpreads) return;
        if (to < _index ? !CanGoBack : !CanGoOn) return;

        _hover.IsOpen = false;
        _index = to;
        ShowSpread();
        _shown?.Invoke(_index);
    }

    /// <summary>지금 면을 그리고 모서리 단추를 켜고 끈다. 꺼진 단추는 아예 안 보인다.</summary>
    private void ShowSpread()
    {
        _previous.Visibility = CanGoBack ? Visibility.Visible : Visibility.Collapsed;
        _next.Visibility = CanGoOn ? Visibility.Visible : Visibility.Collapsed;

        _pages.Children.Clear();
        var spread = _spreadAt(_index);
        bool tall = false, drawn = spread.Illustration >= 0;
        if (drawn)
        {
            var (w, h) = OpenBookArt.SizeOf(spread.Illustration);
            tall = !(h < w);
        }

        int left = !drawn
            ? (spread.Yellow ? OpenBookArt.YellowLeft : OpenBookArt.WhiteLeft)
            : spread.Yellow
                ? (tall ? OpenBookArt.YellowLeftTall : OpenBookArt.YellowLeftWide)
                : (tall ? OpenBookArt.WhiteLeftTall : OpenBookArt.WhiteLeftWide);
        int right = spread.Yellow ? OpenBookArt.YellowRight : OpenBookArt.WhiteRight;

        _pages.Children.Add(Picture(left, OpenBookArt.LeftPageX, OpenBookArt.PageY));
        // 삽화는 첫 점의 색을 투명색으로 삼아 비쳐 찍는다(0x0041F9D0).
        if (drawn)
            _pages.Children.Add(Picture(spread.Illustration, tall ? TallX : WideX,
                                        tall ? TallY : WideY, keyed: true));
        _pages.Children.Add(Picture(right, OpenBookArt.RightPageX, OpenBookArt.PageY));

        if (!spread.HasHint) return;   // 칸이 비면 쪽 번호도 없다

        int page = _firstPage + _index * 2;
        Ink($"-{page}-", LeftNumberX, NumberY);
        Ink($"-{page + 1}-", RightNumberX, NumberY);

        double y = TitleY;
        foreach (string line in Wrap(spread.Title, TextWidth))
        {
            if (y + LineHeight > TitleY + TitleHeight) break;
            Ink(line, TitleX, y);
            y += LineHeight;
        }
        y = BodyY;
        foreach (string line in Wrap(spread.Text, TextWidth))
        {
            if (y + BodyLineHeight > BodyY + BodyHeight) break;
            Ink(line, BodyX, y);
            y += BodyLineHeight;
        }
    }

    /// <summary>
    /// 창 몸통을 누른다. 끌어서 옮기면 창 끌기이고, 제자리에서 떼면 반쪽 판정으로 넘긴다
    /// (<c>0x004654E0</c>: 가운데보다 오른쪽이면 다음, 아니면 앞 — 그쪽 단추가 켜졌을 때만).
    /// </summary>
    private void OnBodyDown(object sender, MouseButtonEventArgs e)
    {
        var at = e.GetPosition(_canvas);
        _hover.IsOpen = false;

        double left = Left, top = Top;
        if (Mouse.LeftButton == MouseButtonState.Pressed) DragMove();
        if (Left != left || Top != top) return;

        if (at.Y < OpenBookArt.PageY * _scale) return;
        if (at.X > _canvas.Width / 2) { if (CanGoOn) Turn(_index + 1); }
        else if (CanGoBack) Turn(_index - 1);
    }

    /// <summary>
    /// 마우스가 올라간 반쪽의 단추가 켜져 있으면 말풍선을 띄운다 — 오른쪽 반은 「다음장」을
    /// 창 아래 오른쪽 모서리 밖(x+w-96, y+h)에, 왼쪽 반은 「앞장」을 아래 왼쪽 밖(x, y+h)에.
    /// </summary>
    private void Hover(Point at)
    {
        bool rightHalf = at.X > _canvas.Width / 2;
        if (at.Y < OpenBookArt.PageY * _scale || !(rightHalf ? CanGoOn : CanGoBack))
        {
            _hover.IsOpen = false;
            return;
        }

        _hoverText.Text = rightHalf ? "다음장" : "앞장";
        _hover.HorizontalOffset = rightHalf ? (OpenBookArt.FrameWidth - HoverWidth) * _scale : 0;
        _hover.VerticalOffset = OpenBookArt.FrameHeight * _scale;
        _hover.IsOpen = true;
    }

    /// <summary>← / → 는 켜진 단추를 누른 것과 같다(<c>0x00465264</c> · <c>0x00465290</c>).</summary>
    private void OnKey(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Left:
                e.Handled = true;
                if (CanGoBack) Turn(_index - 1);
                break;
            case Key.Right:
                e.Handled = true;
                if (CanGoOn) Turn(_index + 1);
                break;
            case Key.Escape or Key.Enter or Key.Space:
                e.Handled = true;
                Close();
                break;
        }
    }

    /// <summary>모서리 단추 하나(16x16, <c>0x00464010</c>).</summary>
    private FrameworkElement Corner(int picture, double x, Action run)
    {
        var image = Picture(picture, x, CornerY);
        image.Width = CornerSize * _scale;
        image.Height = CornerSize * _scale;
        image.Cursor = Cursors.Hand;
        // 누름을 삼킨다 — 몸통의 창 끌기·반쪽 판정이 먼저 걸리지 않게.
        image.MouseLeftButtonDown += (_, e) => { e.Handled = true; _hover.IsOpen = false; run(); };
        Panel.SetZIndex(image, 5);
        _canvas.Children.Add(image);
        return image;
    }

    /// <summary>그림 한 장을 자리에 세운다. 못 풀면 빈 그림이다.</summary>
    /// <param name="keyed">첫 점의 색을 투명하게 뺄지 — 삽화가 그렇다.</param>
    private Image Picture(int picture, double x, double y, bool keyed = false)
    {
        var (w, h) = OpenBookArt.SizeOf(picture);
        var image = new Image { Width = w * _scale, Height = h * _scale, IsHitTestVisible = false };
        var px = _art.TryGetBgra(picture);
        if (px != null)
        {
            if (keyed && px.Length > 0)
            {
                px = (uint[])px.Clone();
                uint key = px[0];
                for (int i = 0; i < px.Length; i++)
                    if (px[i] == key) px[i] = 0;
            }
            var bmp = BitmapSource.Create(w, h, 96, 96, PixelFormats.Bgra32, null, px, w * 4);
            bmp.Freeze();
            image.Source = bmp;
        }
        RenderOptions.SetBitmapScalingMode(image, GameUi.SpriteScaling);
        RenderOptions.SetEdgeMode(image, EdgeMode.Aliased);
        Canvas.SetLeft(image, x * _scale);
        Canvas.SetTop(image, y * _scale);
        return image;
    }

    /// <summary>종이에 글 한 줄을 앉힌다 — 게임 글꼴의 검은 벌이다.</summary>
    private void Ink(string line, double x, double y)
    {
        if (line.Length == 0) return;
        var label = new GameUi.GameLabel(GameFont.BlackColor, GameUi.ItemTextHeight * _scale)
        {
            Text = line,
            Bold = false,
            FallbackBrush = Brushes.Black,
            IsHitTestVisible = false,
        };
        Canvas.SetLeft(label, x * _scale);
        Canvas.SetTop(label, y * _scale);
        _pages.Children.Add(label);
    }

    /// <summary>종이 너비에 맞춰 끊는다. 한글 한 자가 두 칸이다.</summary>
    private static List<string> Wrap(string text, double width)
    {
        var lines = new List<string>();
        var line = new StringBuilder();
        double used = 0;

        foreach (char c in text)
        {
            if (c == '\n') { lines.Add(line.ToString()); line.Clear(); used = 0; continue; }
            double w = c < 0x80 ? CellWidth : CellWidth * 2;
            if (used + w > width) { lines.Add(line.ToString()); line.Clear(); used = 0; }
            line.Append(c);
            used += w;
        }
        if (line.Length > 0) lines.Add(line.ToString());
        return lines;
    }

    /// <summary>
    /// 책 한 권을 편다(<c>0x00471EA0</c>). 창을 닫을 때까지 돌아오지 않는다. 그림을 못 읽으면 false.
    /// </summary>
    /// <param name="count">힌트 칸 수(0~8). 0 이면 빈 칸 하나짜리 면만 보인다.</param>
    /// <param name="spreadAt">면 <c>i</c> 에 그릴 것.</param>
    /// <param name="shown">
    /// 면 <c>i</c> 가 화면에 나왔을 때 — 펼 때 한 번, 넘길 때마다 한 번(<c>0x00464A30</c>).
    /// </param>
    public static bool Read(Window owner, OpenBookArt? art, int count,
                            Func<int, Spread> spreadAt, Action<int> shown)
    {
        if (art == null || art.TryGetBgra(OpenBookArt.Frame) == null) return false;

        int scale = owner.ActualHeight > 800 ? 2 : 1;
        new OpenBookDialog(art, count, 1, spreadAt, shown, scale) { Owner = owner }.ShowDialog();
        return true;
    }
}
