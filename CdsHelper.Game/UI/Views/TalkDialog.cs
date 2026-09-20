using System.Text;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using System.Windows.Media;
using System.Windows;
using CdsHelper.Game.Local.Helpers;

namespace CdsHelper.Game.UI.Views;

/// <summary>
/// 인물이 말하는 창 — 왼쪽에 얼굴, 오른쪽에 대사. 게임의 인물 대사 창(<c>0x004692E0</c>)과
/// 같은 모양이다.
/// </summary>
/// <remarks>
/// 얼굴은 MALE.CDS · FEMALE.CDS 에서 온다(<see cref="Portraits"/>, 80x96 도트 그림).
/// 얼굴을 못 구하면 대사만 낸다 — 그림이 없다고 말까지 막을 일은 아니다.
///
/// 물어보는 창(승낙/교섭 따위)으로도 쓸 수 있다. <paramref name="choices"/> 를 주면
/// 그 단추들이 서고, 고른 자리를 낸다.
/// </remarks>
public sealed class TalkDialog : GameWindow
{
    /// <summary>
    /// 게임 대사 창을 재어 맞춘 자리들(그림 점). 얼굴은 <b>1배</b>로 놓는다 —
    /// 두 배로 걸면 얼굴만 커져 글자와 단추가 딸려 보인다.
    /// </summary>
    /// <remarks>
    /// 화면(1.7778배로 늘어난 갈무리)에서 잰 값이다. 얼굴 142x169 는 80x96 의 1.7778배이고,
    /// 창 높이 276 은 <c>14 + 96 + 8 + 24 + 14</c> 의 1.7778배로 딱 떨어진다.
    /// 글자는 얼굴과 <b>윗선</b>이 맞는다 — 가운데로 맞추지 않는다.
    /// </remarks>
    private const double Pad = 14, ButtonGap = 8;

    /// <summary>
    /// 창 폭. <b>글에 맞춰 늘이지 않는다</b> — 게임 대사 창은 늘 이만큼이다.
    /// </summary>
    /// <remarks>
    /// 갈무리 두 장(도서관 사서 · 부관)에서 잰 폭이 둘 다 640점, 곧 <c>640/1.7778 = 360</c>
    /// 이었다. 글이 짧아도 창이 안 줄고 오른쪽이 넓게 빈다. 단추가 창 한가운데(180)에
    /// 서는 것도 이 폭이라야 갈무리와 맞는다.
    /// </remarks>
    private const double BoxWidth = 360;

    /// <summary>
    /// 대사가 얼굴에서 떨어지는 가장 가까운 거리.
    /// </summary>
    /// <remarks>
    /// 대사는 <b>창 한가운데에 놓되 얼굴을 넘지 않게</b> 민다. 갈무리 두 장이 그렇게 풀린다 —
    /// 짧은 "책을 찾고 계십니까?"(폭 136)는 가운데인 112 에서 시작하고, 긴 "제독, 바다에
    /// 나가시겠습니까?"(폭 219)는 가운데로 치면 70 이라 얼굴에 물리므로 얼굴 오른쪽
    /// <c>14+80+8 = 102</c> 로 밀려 있다.
    /// </remarks>
    private const double FaceTextGap = 8;

    /// <summary>확인 단추 폭. 게임 것은 마구리 둘에 가운데 넉 칸이다(16+8*4+16).</summary>
    private const double OkWidth = 64;

    private int _picked = -1;
    private readonly GameUi.FocusGroup _focus = new();

    private TalkDialog(uint[]? face, string speaker, string text, string[] choices)
    {
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.Height;
        Width = BoxWidth + 2;                    // 좌우 테 한 점씩
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        Background = GameUi.Back;

        // 얼굴과 글은 <b>겹칠 수 있는 한 판</b>에 놓는다. 글 자리가 얼굴 오른쪽 칸이 아니라
        // 창 전체를 기준으로 잡히기 때문이다(<see cref="FaceTextGap"/>).
        var line = new Grid { Margin = new Thickness(0, Pad, 0, 0) };

        double textLeft = Pad;
        if (face != null)
        {
            var picture = BitmapSource.Create(Portraits.Width, Portraits.Height, 96, 96,
                                              PixelFormats.Bgra32, null, face, Portraits.Width * 4);
            picture.Freeze();
            // 테를 두르지 않는다 — 게임은 얼굴을 창 바탕에 그대로 얹는다.
            var image = new Image
            {
                Source = picture,
                Width = Portraits.Width,
                Height = Portraits.Height,
                Stretch = Stretch.Fill,
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(Pad, 0, 0, 0),
            };
            RenderOptions.SetBitmapScalingMode(image, GameUi.SpriteScaling);
            line.Children.Add(image);
            textLeft = Pad + Portraits.Width + FaceTextGap;
        }

        var words = new StackPanel
        {
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
        };
        if (speaker.Length > 0) words.Children.Add(Line(speaker, textLeft, GameFont.TitleColor));
        foreach (string one in Wrap(text, BoxWidth - Pad - textLeft))
            words.Children.Add(Line(one, textLeft, GameFont.WhiteColor));
        line.Children.Add(words);

        // 얼굴이 있으면 그 높이만큼은 자리를 잡아 둔다 — 말이 짧아도 창이 안 쪼그라들게.
        if (face != null) line.MinHeight = Portraits.Height;

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, ButtonGap, 0, Pad),
        };
        if (choices.Length == 0)
        {
            buttons.Children.Add(_focus.Add("확인", Close, OkWidth));
        }
        else
        {
            for (int i = 0; i < choices.Length; i++)
            {
                int index = i;
                buttons.Children.Add(_focus.Add(choices[i], () => { _picked = index; Close(); }, 110));
            }
        }

        var stack = new StackPanel();
        stack.Children.Add(line);
        stack.Children.Add(buttons);

        // 게임 창은 밝은 선 한 점만 두른다 — 두 점으로 두르면 그 선이 먼저 눈에 든다.
        var root = new Border
        {
            Background = GameUi.Back,
            BorderBrush = GameUi.Edge,
            BorderThickness = new Thickness(1),
            Child = stack,
        };
        Content = root;
        GameUi.EnableDrag(this, root);
        GameUi.CarryOwnedWindows(this);

        KeyDown += (_, e) =>
        {
            if (e.Key is Key.Escape) { Close(); return; }
            if (_focus.HandleKey(e.Key)) e.Handled = true;
        };
        MouseRightButtonUp += (_, _) => Close();
    }

    /// <summary>
    /// 대사 한 줄. 창 한가운데에 놓되 <paramref name="least"/> 보다 왼쪽으로는 안 간다.
    /// </summary>
    private static GameUi.GameLabel Line(string text, double least, byte color) =>
        new(color)
        {
            Text = text,
            Bold = false,
            FallbackBrush = color == GameFont.TitleColor ? GameUi.Edge : GameUi.Text,
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(Math.Max(least, (BoxWidth - TextWidth(text)) / 2), 0, 0, 0),
        };

    /// <summary>그 글을 게임 글꼴로 찍었을 때의 폭. 글꼴이 아직 없으면 대충 센다.</summary>
    private static double TextWidth(string text) =>
        GameUi.Font?.TextWidth(text) ?? text.Length * 12;

    /// <summary>글을 <paramref name="room"/> 안에 들도록 띄어쓰기에서 끊는다.</summary>
    private static List<string> Wrap(string text, double room)
    {
        var lines = new List<string>();
        var line = new StringBuilder();
        foreach (string word in text.Split(' '))
        {
            string joined = line.Length == 0 ? word : $"{line} {word}";
            if (line.Length > 0 && TextWidth(joined) > room)
            {
                lines.Add(line.ToString());
                line.Clear();
                line.Append(word);
            }
            else
            {
                line.Clear();
                line.Append(joined);
            }
        }
        if (line.Length > 0) lines.Add(line.ToString());
        return lines.Count > 0 ? lines : [text];
    }

    /// <summary>얼굴을 띄우고 한마디 한다. 확인만 받는다.</summary>
    /// <remarks>
    /// <b>게임 알림창을 그대로 쓴다</b>(<see cref="ConfirmDialog"/>). 이 창은 폭이
    /// 360 으로 박혀 있어서, 얼굴이 없으면 짧은 말에도 창이 넓게 남고 얼굴이 있으면
    /// 한 줄이면 될 말이 두 줄로 접혔다 — 게임은 <b>글 길이에 맞춰 창을 늘인다</b>
    /// (칸수 = max(30, 가장 긴 줄), 너비 = 칸수 x 8 + 32, 얼굴이 서면 + 96).
    /// </remarks>
    public static void Say(Window owner, uint[]? face, string speaker, string text) =>
        ConfirmDialog.Tell(owner, text, speaker.Length > 0 ? speaker : null, face);

    /// <summary>
    /// 얼굴을 띄우고 물어본다. 고른 자리를 내고, 그냥 닫으면 -1 이다.
    /// </summary>
    /// <remarks>
    /// <b>대사와 고를 줄은 딴 창이다.</b> 게임은 먼저 대사 창을 내고(확인 하나), 확인을
    /// 누르면 그제야 세로로 선 메뉴를 낸다 — 술집에서 여성을 누르면 "아름다운 여성이 있다"
    /// 가 뜨고, 확인하면 "한잔 산다 · 무시한다" 가 뜨는 그 차례다.
    ///
    /// 예전에는 둘을 한 상자에 담아 글 밑에 단추를 가로로 늘어놓았다.
    ///
    /// 메뉴에서 물러나면(ESC · 오른쪽 단추) <b>마지막 줄</b>을 고른 것으로 친다 — 마지막
    /// 줄이 늘 "무시한다"·"떠난다" 같은 나가기 줄이기 때문이다.
    /// </remarks>
    public static int Ask(Window owner, uint[]? face, string speaker, string text,
                          params string[] choices)
    {
        if (text.Length > 0) Say(owner, face, speaker, text);
        if (choices.Length == 0) return -1;

        int picked = ChoiceDialog.Ask(owner, "", choices[..^1], choices[^1]);
        return picked >= 0 ? picked : choices.Length - 1;
    }
}
