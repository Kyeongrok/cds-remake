using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CdsHelper.Game.Local.Helpers;
using CdsHelper.Support.Local.Models;

namespace CdsHelper.Game.UI.Views;

/// <summary>
/// 후원자 상세 창 — 「스폰서 일람」에서 한 줄을 고르면 뜬다.
/// </summary>
/// <remarks>
/// 게임의 <c>0x004703A0</c>(창) · <c>0x0046FE??~0x00470230</c>(그리기) 를 옮겼다. 창은
/// <b>464 x 232</b> 이고 줄 자리가 코드에 그대로 박혀 있어 그 좌표를 그대로 쓴다.
/// <code>
///   ( 96,   8)  "이름  %s"
///   ( 96,  24)  "국적  %s"        나라 표 0x004CA370
///   ( 96,  40)  "도시  %s"
///   ( 96,  56)  "직업  %-10s 권력  %s"
///   ( 96,  72)  "친밀도 %4d       %s"      끝의 %s 는 파산이면 "파산"
///   ( 40, 112)  "발견물의 취향"
///   ( 40 + 96*(i/2), 136 + 16*(i%2))  갈래 이름 여덟 칸 — 취향인 것만 찍는다
///   (400, 192)  [취소]  48 x 24
/// </code>
/// 직업 이름은 표 <c>0x00560AA8</c>[14~21], 권력은 <c>0x00560F50</c>[(권력값-1)/20] 로
/// <c>E·D·C·B·A</c>, 갈래 이름은 <c>0x00560C60</c>[8] 이다. 갈래 차례는 힌트·발견물 표와
/// 같다(<see cref="Patron.Likes"/>).
///
/// <b>친밀도는 0 으로 둔다.</b> 게임은 후원자 객체 <c>+0x20</c> 에 들고 새 판에서 0 으로
/// 시작하는데(<c>0x004AD850</c> 이 건드리지 않는다), 그것을 올리는 길(선물·보고)을 아직
/// 흉내내지 않아 늘 0 이다. 파산(객체 <c>+0x28</c> 의 비트 12)도 마찬가지라 안 찍는다.
/// </remarks>
public sealed class PatronInfoDialog : GameWindow
{
    /// <summary>화면 바탕 — 인물정보·힌트정보와 같은 강청색 <c>#5C6F93</c> 이다(원본 갈무리).</summary>
    private static readonly Brush Back = GameUi.InfoBack;

    /// <summary>테를 두르는 짙은 선.</summary>
    private static readonly Brush Line = GameUi.InfoLine;

    /// <summary>글꼴 조각을 못 읽었을 때 물러설 글씨색. 강청색 판은 검은 글씨다.</summary>
    private static readonly Brush Ink = Brushes.Black;

    private static SolidColorBrush Frozen(Color c)
    {
        var b = new SolidColorBrush(c);
        b.Freeze();
        return b;
    }

    /// <summary>창 크기. 게임 것 그대로다.</summary>
    private const double BoardWidth = 464, BoardHeight = 232;

    /// <summary>줄이 놓이는 왼쪽 끝과 줄 사이.</summary>
    private const double LineX = 96, LineTop = 8, LineGap = 16;

    /// <summary>취향 격자 — 왼쪽 끝, 칸 사이, 첫 줄, 줄 사이.</summary>
    private const double LikeX = 40, LikeStepX = 96, LikeY = 136, LikeStepY = 16;

    /// <summary>갈래 이름. 게임 표 <c>0x00560C60</c> 그대로다.</summary>
    private static readonly string[] Categories =
        ["지리", "역사", "보물", "종교", "교역품", "미신", "생물", "민족"];

    /// <summary>그 후원자가 좋아하는 갈래를 한 줄로 — 「지리 · 보물」. 스폰서 일람이 이름 아래에 단다.</summary>
    public static string LikesText(Patron patron) =>
        string.Join(" ", Categories.Where((_, i) => patron.Likes(i)));

    /// <summary>초상화가 놓이는 자리. 글줄이 <see cref="LineX"/> 에서 시작하니 그 앞이다.</summary>
    private const double FaceX = 8, FaceY = 8;

    private PatronInfoDialog(Patron patron, string name, string job, int closeness,
                             int face, bool female, string gameDirectory)
    {
        Title = "후원자 정보";
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.WidthAndHeight;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        Background = Back;

        var board = new Canvas { Width = BoardWidth, Height = BoardHeight };

        if (Portrait(face, female, gameDirectory) is { } portrait)
        {
            Canvas.SetLeft(portrait, FaceX);
            Canvas.SetTop(portrait, FaceY);
            board.Children.Add(portrait);
        }

        Put(board, LineX, LineTop + LineGap * 0, $"이름  {name}");
        Put(board, LineX, LineTop + LineGap * 1, $"국적  {patron.Nationality}");
        Put(board, LineX, LineTop + LineGap * 2, $"도시  {patron.City}");
        Put(board, LineX, LineTop + LineGap * 3, $"직업  {Pad(job, 10)} 권력  {patron.Power}");
        Put(board, LineX, LineTop + LineGap * 4, $"친밀도 {closeness,4}");

        Put(board, LikeX, 112, "발견물의 취향");
        for (int i = 0; i < Categories.Length; i++)
        {
            if (!patron.Likes(i)) continue;
            Put(board, LikeX + LikeStepX * (i / 2), LikeY + LikeStepY * (i % 2), Categories[i]);
        }

        var cancel = new GameButton("취소", Close);
        Canvas.SetLeft(cancel, 386);
        Canvas.SetTop(cancel, 188);
        board.Children.Add(cancel);

        var frame = GameUi.InfoFrame(board, Back, Line);
        GameUi.EnableDrag(this, frame);
        Content = frame;

        KeyDown += (_, e) => { if (e.Key is Key.Escape or Key.Enter) Close(); };
        MouseRightButtonUp += (_, _) => Close();
    }

    /// <summary>
    /// 왼쪽 초상화. 얼굴 번호가 없거나 그림을 못 읽으면 안 세운다.
    /// </summary>
    /// <remarks>
    /// 얼굴 번호는 후원자 표 <c>+0x04</c> 다(<see cref="SponsorTable.Sponsor.Face"/>).
    /// 여자 후원자는 <c>FEMALE.CDS</c> 를 본다.
    /// </remarks>
    private static UIElement? Portrait(int face, bool female, string gameDirectory)
    {
        if (face < 0) return null;
        if (Portraits.Open(gameDirectory)?.TryGetBgra(face, female) is not { } px) return null;

        var bmp = BitmapSource.Create(Portraits.Width, Portraits.Height, 96, 96,
                                      PixelFormats.Bgra32, null, px, Portraits.Width * 4);
        bmp.Freeze();

        var image = new Image { Source = bmp, Width = Portraits.Width, Height = Portraits.Height };
        RenderOptions.SetBitmapScalingMode(image, GameUi.SpriteScaling);
        RenderOptions.SetEdgeMode(image, EdgeMode.Aliased);

        return new Border
        {
            BorderBrush = Line,
            BorderThickness = new Thickness(2),
            Child = image,
        };
    }

    /// <summary>게임의 <c>%-10s</c> 처럼 바이트로 세어 채운다.</summary>
    private static string Pad(string text, int width) => GameUi.Pad(text, width);

    /// <summary>강청색 판 위 그 자리에 검은 글씨를 얹는다.</summary>
    private static void Put(Canvas board, double x, double y, string text)
    {
        var label = new GameUi.GameLabel(GameFont.BlackColor)
        {
            Text = text,
            FallbackBrush = Ink,
            HorizontalAlignment = HorizontalAlignment.Left,
        };
        Canvas.SetLeft(label, x);
        Canvas.SetTop(label, y);
        board.Children.Add(label);
    }

    /// <summary>후원자 상세 창을 연다.</summary>
    /// <param name="name">화면에 낼 이름. 게임 표 이름(가운뎃점)이 있으면 그것을 준다.</param>
    /// <param name="job">직업 이름. 게임 표에서 온 것이면 그것을 준다.</param>
    /// <param name="closeness">
    /// 친밀도(0~100). 게임은 후원자 객체 <c>+0x20</c> 에 들고 <b>0 에서 시작한다</b> —
    /// 발견물을 보고할 때마다 움직인다(<c>0x004111D0</c>,
    /// <see cref="Engine.Town.Palace.ClosenessFor"/>).
    /// </param>
    /// <param name="face">얼굴 번호. 모르면 −1 이고 그때는 초상화를 안 세운다.</param>
    /// <param name="female">여자 후원자인지 — 얼굴 벌이 갈린다.</param>
    /// <param name="gameDirectory">얼굴을 못 찾았을 때 물러설 게임 폴더.</param>
    public static void Show(Window owner, Patron patron, string? name = null, string? job = null,
                            int closeness = 0, int face = -1, bool female = false,
                            string gameDirectory = "") =>
        new PatronInfoDialog(patron,
                             string.IsNullOrEmpty(name) ? patron.Name : name,
                             string.IsNullOrEmpty(job) ? patron.Occupation : job,
                             closeness, face, female, gameDirectory)
        { Owner = owner }.ShowDialog();
}
