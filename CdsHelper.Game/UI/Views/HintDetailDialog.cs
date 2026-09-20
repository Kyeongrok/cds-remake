using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using CdsHelper.Game.Local.Helpers;

namespace CdsHelper.Game.UI.Views;

/// <summary>
/// 힌트 하나를 펴 본 <b>파란 판</b> — 이름과 갈래, 그리고 그 이야기.
/// </summary>
/// <remarks>
/// 「취득 힌트 일람」에서 한 줄을 고르고 결정을 누르면 뜬다. 글은 힌트 표의
/// <see cref="HintTable.Hint.Text"/> 다(힌트 줄 <c>+0x1C</c> 가 가리키는 글, 표
/// <c>0x00543FA0</c>) — 도서관에서 책을 읽을 때 펼친 책에 적히는 그 글이다.
///
/// <b>부관의 평은 이 판에 안 붙는다.</b> 게임은 판 하나와 <b>말 창 하나</b>를 따로 띄운다 —
/// 파란 판은 화면 위쪽에 뜨고, "발견할 수 있을 것 같군요." 는 여느 대사처럼 아래쪽 말 창에
/// 뜬다. 그 말 창의 확인을 누르면 둘 다 닫힌다. 예전에는 한 창에 붙여 두었는데 그러면
/// 판이 세로로 길어지고 글도 잘렸다.
///
/// 평 글은 게임 표 <c>0x00560F38</c> 에서 온다. 한 줄이 <b>여덟 바이트</b>라 앞이 부관이
/// 있을 때, 뒤가 없을 때다(<c>0x0046EE92</c> 와 <c>0x0046EEBA</c>).
/// </remarks>
public sealed class HintDetailDialog : GameWindow
{
    /// <remarks>
    /// <b>원본 코드에서 뽑은 자리다.</b> 창 <c>0x0046ED57</c> 은 320x240(테 포함)이고, 속(테 8점 안쪽)
    /// 좌표로 그린다(<c>0x0046EC2F</c>).
    /// <code>
    ///   0x0046EC48  제목 "%s(%s)"          (8, 8)
    ///   0x0046ECD8  글 상자                (8, 40) 288 x 176 — 반각 36칸, 줄 사이 4
    /// </code>
    /// 판 색은 인물정보·후원자 정보와 같은 강청색 <c>#5C6F93</c> 이고 테도 같은 세 줄이다.
    /// </remarks>
    private const double BoardWidth = 304, BoardHeight = 224;

    /// <summary>제목 자리와 글 상자 자리.</summary>
    private const double TitleX = 8, TitleY = 8, TextX = 8, TextY = 40;

    /// <summary>글 상자 폭(반각 칸)과 한 줄 높이(글자 16 + 줄 사이 4).</summary>
    private const int TextCells = 36;
    private const double LineHeight = 20;

    private static Brush Frozen(Color c)
    {
        var b = new SolidColorBrush(c);
        b.Freeze();
        return b;
    }

    private HintDetailDialog(string head, string body)
    {
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.WidthAndHeight;
        WindowStartupLocation = WindowStartupLocation.Manual;
        ShowInTaskbar = false;
        Background = Brushes.Transparent;

        var board = new Canvas { Width = BoardWidth, Height = BoardHeight, ClipToBounds = true };

        Put(board, Ink(head), TitleX, TitleY);

        var lines = Wrap(body, TextCells);
        for (int i = 0; i < lines.Count; i++)
            Put(board, Ink(lines[i]), TextX, TextY + i * LineHeight);

        // 판은 인물정보·후원자 정보와 같은 강청색 세 줄 테다.
        Content = GameUi.InfoFrame(board, GameUi.InfoBack);

        KeyDown += (_, e) => { if (e.Key is Key.Escape) Close(); };

        // 판도 끌어 옮길 수 있어야 한다 — 아래 말 창과 겹치면 손으로 비켜 놓는다.
        GameUi.EnableDrag(this, (UIElement)Content);
    }

    /// <summary>판 위에 글 한 줄 — 검은 벌이다.</summary>
    private static UIElement Ink(string line) =>
        new GameUi.GameLabel(GameFont.BlackColor, GameUi.ItemTextHeight)
        {
            Text = line,
            Bold = false,
            FallbackBrush = Brushes.Black,
            HorizontalAlignment = HorizontalAlignment.Left,
        };

    /// <summary>
    /// 반각 칸으로 세어 끊는다 — 원본은 <b>낱말이 아니라 글자</b>에서 끊는다.
    /// </summary>
    /// <remarks>
    /// 원본 갈무리의 끊김이 이 셈과 딱 맞는다: 「새하얀 」까지 35칸이라 「석」(2칸)이 넘쳐 다음
    /// 줄로 가고, 「없을 정」에서 36칸이 차 「도이다.」가 넘어간다. 한글은 두 칸, ASCII 는 한 칸이고,
    /// 새 줄 머리의 빈칸은 버린다.
    /// </remarks>
    private static List<string> Wrap(string text, int cells)
    {
        var lines = new List<string>();
        var line = new StringBuilder();
        int used = 0;

        foreach (char c in text)
        {
            int w = c < 0x80 ? 1 : 2;
            if (used + w > cells && line.Length > 0)
            {
                lines.Add(line.ToString());
                line.Clear();
                used = 0;
            }
            if (used == 0 && c == ' ') continue;        // 줄 머리 빈칸은 버린다
            line.Append(c);
            used += w;
        }
        if (line.Length > 0) lines.Add(line.ToString());
        return lines;
    }

    /// <summary>속 좌표로 캔버스에 놓는다.</summary>
    private static void Put(Canvas canvas, UIElement element, double x, double y)
    {
        Canvas.SetLeft(element, x);
        Canvas.SetTop(element, y);
        canvas.Children.Add(element);
    }

    /// <summary>
    /// 부관의 평(<c>0x00560F38</c>). 명성과 힌트 등급을 견주어 셋 가운데 하나다.
    /// </summary>
    /// <remarks>
    /// <code>
    ///   0046EE40  잣대 = 명성 / 2000
    ///   0046EE55  잣대 - 등급 == -1 이면  자리 = 1
    ///   0046EE7C  등급 &gt; 잣대 + 1 이면    자리 += 1
    ///   0046EE92  부관이 있으면 [자리][0], 없으면 [자리][1]
    /// </code>
    /// </remarks>
    public static string CommentOn(int grade, int fame, bool hasMate)
    {
        int mark = fame / FameStep;
        int at = mark - grade == -1 ? 1 : 0;
        if (grade > mark + 1) at++;
        at = Math.Clamp(at, 0, Comments.Length - 1);
        return Comments[at][hasMate ? 0 : 1];
    }

    /// <summary>명성을 재는 눈금(<c>0x0046EE67</c> 의 <c>0x7D0</c>).</summary>
    private const int FameStep = 2000;

    /// <summary>평 세 줄 — 앞이 부관이 있을 때, 뒤가 없을 때다.</summary>
    private static readonly string[][] Comments =
    [
        ["이거라면 발견할 수 있을 것 같군요. 빨리 찾으러 갑시다!", "발견할 수 있을 것 같습니다"],
        ["흥미있을 것 같군요. 스폰서를 찾읍시다!", "발견할 수 있을 것 같군요."],
        ["터무니 없는 이야기인 것 같군요. 찾기 힘들 것 같군요.", "찾을 수 있을 것 같지 않습니다."],
    ];

    /// <summary>
    /// 판을 처음 앉힐 때 <b>아래 말 창 몫</b>으로 미리 비워 두는 높이.
    /// </summary>
    /// <remarks>
    /// 말 창은 떠 봐야 크기를 알므로, 그때 <see cref="GameUi.PlaceUnder"/> 가 둘을 한
    /// 덩이로 다시 앉힌다. 여기서는 그 값에 가깝게 어림잡아 두어 판이 눈에 띄게 튀지
    /// 않게만 한다 — 한 줄짜리 평에 확인 단추면 이만큼이다.
    /// </remarks>
    private const double NoticeRoom = 122;

    /// <summary>
    /// 힌트 하나를 펴 본다 — 파란 판을 띄우고, 부관의 평은 <b>따로</b> 말 창으로 낸다.
    /// </summary>
    /// <param name="contracted">
    /// 지금 계약으로 좇는 힌트인지. 그러면 평 대신 「현재 계약중입니다.」다(<c>0x0046EDDA</c> —
    /// 계약 물건 <c>0x0061D1D0</c> 이 있고 그 힌트 <c>0x0061D1E0</c> 과 같을 때).
    /// </param>
    /// <param name="mateFace">
    /// 부하 첫 자리(부관)가 있으면 그 얼굴 — 평은 부관이 얼굴을 걸고 한다(<c>0x0046EE81</c> 이 <c>0x0047CC50(0)</c>
    /// 을 보고 <c>0x00478280</c>). 없으면 null 이고 얼굴 없는 상자다(<c>0x0049E3E0</c>).
    /// </param>
    public static void Show(Window owner, HintTable.Hint hint, string category,
                            int fame, uint[]? mateFace, bool contracted = false)
    {
        bool hasMate = mateFace != null;
        string head = category.Length > 0 ? $"{hint.Name}({category})" : hint.Name;
        var panel = new HintDetailDialog(head, hint.Text) { Owner = owner };

        // 판과 말 창은 <b>한 덩이로 게임 창 가운데</b>에 앉는다. 예전에는 말 창이 주인
        // 창 가운데에 떠 설명 글을 반쯤 가렸고, 주인이 도시 커맨드 창처럼 작으면 둘 다
        // 화면 구석으로 몰렸다.
        var stage = GameUi.RootOf(owner);
        panel.SourceInitialized += (_, _) =>
        {
            panel.Left = stage.Left + (stage.ActualWidth - panel.ActualWidth) / 2;
            panel.Top = stage.Top + (stage.ActualHeight - panel.ActualHeight - NoticeRoom) / 2;
        };
        panel.Show();

        try
        {
            ConfirmDialog.Tell(owner, contracted ? "현재 계약중입니다." : CommentOn(hint.Grade, fame, hasMate),
                               face: mateFace, under: panel);
        }
        finally
        {
            panel.Close();
        }
    }
}
