using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using CdsHelper.Game.Local.Helpers;
using CdsHelper.Support.Local.Models;

namespace CdsHelper.Game.UI.Views;

/// <summary>
/// 계약 정보 창 — 지금 맺고 있는 계약을 보여 준다. 도시 커맨드의 "계약 정보" 로 연다.
/// </summary>
/// <remarks>
/// 게임 화면을 그대로 옮겼다. 제목은 <b>힌트 이름</b>이다.
/// <code>
///   신세계해협                                    [X]
///      스폰서  헨리 7세
///        마을  런던
///      계약금    124300닢
///        선금     62150닢    계약 기한
///        미불     62150닢       나머지 2년
///      발견물
///
///      증거품
///                                            [취소]
/// </code>
/// 줄 글은 게임 서식 그대로다(<c>0x0055A3A0</c> 벌) — 앞의 빈칸까지 그대로 두면 게임
/// 글꼴(한글 16점 · 빈칸 8점)에서 "스폰서"와 "  마을", "계약금"과 "  선금" 의 오른쪽 끝이
/// 저절로 맞는다.
///
/// <code>
///   0x0055A3A0  "스폰서  %s"          0x0055A3B0  "  마을  %s"
///   0x0055A3C0  "계약금  %8ld닢"      0x0055A3D0  "  선금  %8ld닢    계약 기한"
///   0x0055A3F0  "  미불  %8ld닢      "  0x0055A408 "나머지" / "%2d년" / "%2d개월"
///   0x0055A420  "기한이 지났습니다"    0x0055A438  "발견물"   0x0055A440  "증거품"
/// </code>
///
/// <b>선금도 미불도 계약금의 절반이다</b> — 그리는 자리(<c>0x0047F38C</c> · <c>0x0047F3C8</c>)가
/// 둘 다 <c>계약금 / 2</c> 를 낸다. 남은 기한은 날수를 365 로 나눠 햇수를, 나머지를 30 으로
/// 나눠 달수를 내며, 햇수가 있으면 달수는 0 일 때 안 적는다(<c>0x0047F444</c>).
///
/// <b>발견물</b> 은 이 계약을 맺은 뒤에 발견한 것이고, <b>증거품</b> 은 그것들이 준 물건 중
/// 아직 지니고 있는 것이다. 게임은 후원자에게 보고할 때 이 둘을 내민다.
///
/// 계약이 없으면 이 창을 열지 않고 게임처럼 한 줄로 물린다 — "계약을 맺지 않았습니다"
/// (<c>0x00533228</c>, 부르는 곳 <c>0x00426018</c>).
/// </remarks>
public sealed class ContractDialog : GameWindow
{
    /// <summary>화면 바탕. 보급 화면과 같은 밤색 판이다.</summary>
    private static readonly Brush Back = Frozen(Color.FromRgb(0x31, 0x18, 0x18));

    /// <summary>테를 두르는 짙은 선.</summary>
    private static readonly Brush Line = Frozen(Color.FromRgb(0x11, 0x09, 0x09));

    /// <summary>글꼴 조각을 못 읽었을 때 물러설 글씨색.</summary>
    private static readonly Brush Ink = Frozen(Color.FromRgb(0xCB, 0xC5, 0xC5));

    private static SolidColorBrush Frozen(Color c)
    {
        var b = new SolidColorBrush(c);
        b.Freeze();
        return b;
    }

    /// <summary>
    /// 글이 놓이는 판의 크기. 발견물·증거품 칸이 게임처럼 넉넉히 비도록 못 박는다.
    /// </summary>
    /// <remarks>
    /// <b>원본은 가로세로가 거의 같다.</b> 화면으로 재 보면 판이 1.05:1 인데 예전 값
    /// (560x420)은 1.33:1 이라 옆으로 퍼져 보였고, 아래가 통째로 비었다.
    ///
    /// <b>그다음 값 470x440 은 갈무리 점을 그대로 옮긴 것이라 1.75배쯤 컸다.</b> 글씨는 제
    /// 크기인데 판만 커서, 가장 긴 줄(「미불 … 나머지 8개월」, 약 294점)을 두고도 옆이 한참
    /// 비고 증거품 아래가 통째로 비었다. 글 줄에 맞춰 줄였다 — 제목·줄 다섯·틈 둘·목록 둘을
    /// 쌓으면 키가 256점쯤이다.
    /// </remarks>
    /// <remarks>
    /// <b>지금 값은 원본 코드에서 뽑았다.</b> 창은 폭 <c>0x180</c>(384, 테 포함 — <c>0x0047F55F</c>)이고
    /// 갈무리로 재면 키가 368 이라, 테 8점을 뺀 속이 368x352 다. 자리는 모두 속에서 잰 값이다.
    /// <code>
    ///   0x0047F22A  제목(힌트 이름)        (0, 8)
    ///   0x0047F29C  "스폰서  %s"           (16, 40)       이후 줄은 x 16 에 선다
    ///   0x0047F2FB  "  마을  %s"           y +20 = 60
    ///   0x0047F352  "계약금  %8ld닢"       y +24 = 84
    ///   0x0047F37E  "  선금 …  계약 기한"  y +20 = 104
    ///   0x0047F3BA  "  미불 …" + 기한      y +20 = 124
    ///   0x0047F4B3  "발견물"               y +24 = 148    목록 (64,160) 304x60 — 0x0047F66E
    ///   0x0047F4E5  "증거품"               y +84 = 232    목록 (64,244) 304x60
    ///   취소 96x24 는 속 오른쪽 아래에서 8점 안쪽 (264,320) — 갈무리로 잼
    /// </code>
    /// 예전 300x262 는 글 줄에 맞춰 어림한 것이라 「스폰서」 이름과 「나머지 11개월」 끝이 잘렸다.
    /// </remarks>
    private const double BoardWidth = 368, BoardHeight = 352;

    /// <summary>발견물·증거품 목록 상자(<c>0x0047F66E</c>: 0x130 x 0x3C).</summary>
    private const double ListLeft = 64, ListWidth = 304, ListHeight = 60;

    /// <summary>줄이 서는 왼쪽 자리.</summary>
    private const double RowLeft = 16;

    /// <summary>취소 단추 — 속 오른쪽 아래에서 8점 안쪽이다.</summary>
    private const double CancelWidth = 96, CancelHeight = 24, CancelInset = 8;

    private ContractDialog(Contract contract, DateTime today, string title, string sponsorShown,
                           IReadOnlyList<string> found, IReadOnlyList<string> evidence)
    {
        Title = "계약 정보";
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.WidthAndHeight;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        Background = Back;

        // 원본 자리 그대로 캔버스에 놓는다(속 좌표). 줄 글은 게임 서식 그대로 — 앞 빈칸이
        // "스폰서"와 "  마을", "계약금"과 "  선금" 의 오른쪽 끝을 맞춘다.
        var board = new Canvas { Width = BoardWidth, Height = BoardHeight, ClipToBounds = true };

        Put(board, Label(title), 0, 8);

        var close = CloseBox();
        Put(board, close, BoardWidth - 21, 3);

        Put(board, Label($"스폰서  {sponsorShown}"), RowLeft, 40);
        if (contract.City.Length > 0) Put(board, Label($"  마을  {contract.City}"), RowLeft, 60);

        Put(board, Label($"계약금  {contract.Amount,8}닢"), RowLeft, 84);
        Put(board, Label($"  선금  {contract.Advance,8}닢    계약 기한"), RowLeft, 104);
        Put(board, Label($"  미불  {contract.Unpaid,8}닢      {Deadline(contract, today)}"), RowLeft, 124);

        Put(board, Label("발견물"), RowLeft, 148);
        Put(board, List(found), ListLeft, 160);
        Put(board, Label("증거품"), RowLeft, 232);
        Put(board, List(evidence), ListLeft, 244);

        Put(board, new GameButton("취소", Close, width: CancelWidth) { Margin = new Thickness(0) },
            BoardWidth - CancelInset - CancelWidth, BoardHeight - CancelInset - CancelHeight);

        var frame = GameUi.InfoFrame(board, Back, Line);
        GameUi.EnableDrag(this, frame);
        Content = frame;

        KeyDown += (_, e) => { if (e.Key is Key.Escape or Key.Enter) Close(); };
        MouseRightButtonUp += (_, _) => Close();
    }

    /// <summary>
    /// 기한 칸의 글. 지났으면 그렇다고 적고, 아니면 "나머지 2년" · "나머지 5개월" 이다.
    /// </summary>
    /// <remarks>
    /// 햇수가 남았으면 달수는 0 일 때 안 적는다 — "나머지 2년" 이지 "나머지 2년 0개월" 이
    /// 아니다. 햇수가 0 이면 달수만 적는다.
    /// </remarks>
    private static string Deadline(Contract contract, DateTime today)
    {
        if (contract.DaysLeft(today) <= 0) return "기한이 지났습니다";

        var (years, months) = contract.Remaining(today);
        string text = "나머지";
        if (years > 0) text += $" {years,2}년";
        if (years == 0 || months > 0) text += $" {months,2}개월";
        return text;
    }

    /// <summary>제목 줄 오른쪽 끝의 닫기(X). 게임 창들도 그 자리에 있다.</summary>
    private FrameworkElement CloseBox()
    {
        var box = new Border
        {
            Background = GameUi.ItemFill,
            BorderBrush = GameUi.ItemEdge,
            BorderThickness = new Thickness(2),
            Padding = new Thickness(5, 0, 5, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Cursor = Cursors.Hand,
            ToolTip = "닫기",
            Child = new TextBlock
            {
                Text = "✕",
                Foreground = Brushes.Black,
                FontWeight = FontWeights.Bold,
                FontSize = 13,
            },
        };
        // 누름은 삼킨다 — 판 끌기가 먼저 걸리면 마우스를 잡아 버려 뗌이 안 온다.
        box.MouseLeftButtonDown += (_, e) => e.Handled = true;
        box.MouseLeftButtonUp += (_, e) => { e.Handled = true; Close(); };
        return box;
    }

    /// <summary>속 좌표로 캔버스에 놓는다.</summary>
    private static void Put(Canvas canvas, UIElement element, double x, double y)
    {
        Canvas.SetLeft(element, x);
        Canvas.SetTop(element, y);
        canvas.Children.Add(element);
    }

    /// <summary>
    /// 이름을 죽 늘어놓는 칸. <b>담긴 것이 있으면 양피지 상자</b>가 깔리고, 비어 있으면
    /// 자리만 비워 둔다.
    /// </summary>
    /// <remarks>
    /// 게임 화면을 보면 발견물에는 밝은 상자가 깔리는데 증거품 자리는 그냥 밤색이다 —
    /// 그때 발견물에는 든 것이 있었고 증거품은 비어 있었다. 곧 <b>빈 칸에는 상자를 안
    /// 깐다</b>. 상자는 라벨보다 안쪽에서 시작해 오른쪽 끝까지 간다.
    /// </remarks>
    private static UIElement List(IReadOnlyList<string> names)
    {
        var stack = new StackPanel { Margin = new Thickness(6, 2, 0, 0) };
        foreach (var name in names) stack.Children.Add(Ledger(name));

        var box = new Border
        {
            Width = ListWidth,
            Height = ListHeight,
            // 넘치면 게임 굴림대로 굴린다 — 윈도 굴림대는 모양이 너무 다르다.
            Child = GameUi.Scroller(stack, ListHeight),
        };
        if (names.Count == 0) return box;

        box.Background = GameUi.PageFill;
        box.BorderBrush = GameUi.ItemEdge;
        box.BorderThickness = new Thickness(2);
        return box;
    }

    /// <summary>양피지 상자 안의 글씨. 바탕이 밝아 검은 글꼴 조각을 쓴다.</summary>
    private static GameUi.GameLabel Ledger(string text) => new(GameFont.BlackColor)
    {
        Text = text,
        FallbackBrush = Brushes.Black,
        HorizontalAlignment = HorizontalAlignment.Left,
    };

    /// <summary>
    /// 밤색 판 위에 얹는 밝은 글씨. 줄이 세로로 쌓이므로 <b>왼쪽에 붙여</b> 둔다 — 그냥 두면
    /// 칸이 가로로 늘어나 글자가 가운데로 간다.
    /// </summary>
    private static GameUi.GameLabel Label(string text) => new(GameFont.WhiteColor)
    {
        Text = text,
        FallbackBrush = Ink,
        HorizontalAlignment = HorizontalAlignment.Left,
    };

    /// <summary>
    /// 계약 정보 창을 연다. 계약이 없으면 게임처럼 한 줄로 물린다.
    /// </summary>
    /// <param name="hintName">제목에 쓸 힌트 이름.</param>
    /// <param name="sponsorName">
    /// 화면에 낼 후원자 이름. 게임 표에 적힌 대로 <b>가운뎃점</b>이 든 이름이다
    /// (「프란시스코 · 레이넬 · 파레일로」). 안 주면 계약에 적힌 이름을 그대로 쓴다.
    /// </param>
    /// <param name="found">이 계약을 맺은 뒤 발견한 것의 이름.</param>
    /// <param name="evidence">그 발견물이 준 물건 중 아직 지닌 것의 이름.</param>
    public static void Show(Window owner, Contract? contract, DateTime today,
                            string hintName,
                            IReadOnlyList<string> found, IReadOnlyList<string> evidence,
                            string? sponsorName = null)
    {
        if (contract == null)
        {
            NoticeDialog.Show(owner, "계약을 맺지 않았습니다");
            return;
        }

        string shown = string.IsNullOrEmpty(sponsorName) ? contract.Sponsor : sponsorName;
        new ContractDialog(contract, today, hintName, shown, found, evidence) { Owner = owner }
            .ShowDialog();
    }
}
