using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using CdsHelper.Game.Local.Helpers;

namespace CdsHelper.Game.UI.Views;

/// <summary>
/// 커맨드 "정보" 아래에 뜨는 판들의 밑바탕 — 밤색 판에 밝은 글씨, 오른쪽 위에 닫기.
/// </summary>
/// <remarks>
/// 게임의 정보 창(<c>0x00425E40</c>)은 일곱 줄이다.
/// <code>
///   함대정보 0x0046F340 · 인물정보 0x0046DF70 · 소지품정보 0x0044CB20
///   힌트정보 · 계약정보 · 지도를 본다 · 돌아간다
/// </code>
/// 판 모양은 계약 정보 창(<see cref="ContractDialog"/>)과 같다 — 게임도 이 판들을 한 벌로
/// 그린다. 그래서 바탕색·글씨색·닫기 단추를 여기 모아 두고 물려 쓴다.
/// </remarks>
internal abstract class InfoDialog : GameWindow
{
    /// <summary>화면 바탕. 보급·계약 화면과 같은 밤색 판이다.</summary>
    protected static readonly Brush Back = Frozen(Color.FromRgb(0x31, 0x18, 0x18));

    /// <summary>테를 두르는 짙은 선.</summary>
    protected static readonly Brush Line = Frozen(Color.FromRgb(0x11, 0x09, 0x09));

    /// <summary>
    /// 강청색 판. 정보 판 가운데 <b>인물정보와 함대정보</b>가 이 색이다 — 나머지는 밤색이다.
    /// 화면에서 뽑은 값이다.
    /// </summary>
    protected static readonly Brush Steel = Frozen(Color.FromRgb(92, 111, 147));

    /// <summary>강청색 판의 테.</summary>
    protected static readonly Brush SteelEdge = Frozen(Color.FromRgb(54, 65, 86));

    /// <summary>글꼴 조각을 못 읽었을 때 물러설 글씨색.</summary>
    protected static readonly Brush Ink = Frozen(Color.FromRgb(0xCB, 0xC5, 0xC5));

    /// <summary>판 바탕. 화면마다 다르면 물려받아 갈아 끼운다.</summary>
    protected virtual Brush Board => Back;

    /// <summary>판 테. 화면마다 다르면 물려받아 갈아 끼운다.</summary>
    protected virtual Brush BoardEdge => Line;

    /// <summary>
    /// 오른쪽 위에 닫기(X)를 둘지. <b>새 놀이 화면 셋에는 없다</b> — 게임 갈무리에 없다.
    /// </summary>
    protected virtual bool ShowClose => true;

    /// <summary>판 둘레 여백과 단추 줄 여백. 좁게 짓고 싶은 화면이 물려받아 갈아 끼운다.</summary>
    protected virtual Thickness BoardPad => new(14, 10, 14, 2);

    protected virtual Thickness ButtonPad => new(10, 0, 10, 10);

    /// <summary>얼려서 돌려준다. 물려받은 쪽이 제 색을 만들 때 쓴다.</summary>
    protected static Brush Frozen(Color c)
    {
        var b = new SolidColorBrush(c);
        b.Freeze();
        return b;
    }

    protected InfoDialog()
    {
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.WidthAndHeight;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        Background = Back;   // 물려받은 쪽이 Board 를 갈면 Build 에서 다시 잡힌다
    }

    /// <summary>
    /// 제목 줄 · 속 · 아래 단추를 한 판으로 묶는다.
    /// </summary>
    /// <param name="title">왼쪽 위에 적을 제목.</param>
    /// <param name="body">판 속.</param>
    /// <param name="width">판 너비.</param>
    /// <param name="height">판 높이.</param>
    /// <param name="buttons">아래 오른쪽에 설 단추들. 마지막은 보통 "취소" 다.</param>
    protected void Build(string title, UIElement body, double width, double height,
                         params UIElement[] buttons)
    {
        // 닫기(X)는 판 <b>위에 겹쳐</b> 놓는다. 줄로 쌓으면 그만큼 속이 줄어 마지막 줄이
        // 잘린다 — 인물정보의 "빚" 줄이 그래서 안 보였다. 게임도 판 오른쪽 위 여백에
        // 얹혀 있지 제 줄을 차지하지 않는다.
        var inner = new StackPanel();
        if (title.Length > 0)
        {
            inner.Children.Add(Label(title));
            inner.Children.Add(Gap(6));
        }
        inner.Children.Add(body);

        var board = new Grid
        {
            Width = width,
            Height = height,
            Margin = BoardPad,
        };
        board.Children.Add(inner);

        if (ShowClose)
        {
            var close = CloseBox();
            close.HorizontalAlignment = HorizontalAlignment.Right;
            close.VerticalAlignment = VerticalAlignment.Top;
            board.Children.Add(close);
        }

        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = ButtonPad,
        };
        foreach (var button in buttons) row.Children.Add(button);

        var page = new StackPanel();
        page.Children.Add(board);
        page.Children.Add(row);

        Background = Board;
        var frame = GameUi.InfoFrame(page, Board, BoardEdge);
        GameUi.EnableDrag(this, frame);
        Content = frame;

        KeyDown += (_, e) => { if (e.Key is Key.Escape or Key.Enter) Close(); };
        MouseRightButtonUp += (_, _) => Close();
    }

    /// <summary>
    /// 판 위에 얹는 글씨. 줄이 세로로 쌓이므로 <b>왼쪽에 붙여</b> 둔다.
    /// </summary>
    /// <param name="text">적을 글.</param>
    /// <param name="color">
    /// 글자색(공용 색표 색인). 밤색 판은 밝은 글씨지만 강청색 판(인물정보)은 검정이다.
    /// </param>
    protected static GameUi.GameLabel Label(string text,
                                            byte color = GameFont.WhiteColor) => new(color)
    {
        Text = text,
        FallbackBrush = color == GameFont.WhiteColor ? Ink : System.Windows.Media.Brushes.Black,
        HorizontalAlignment = HorizontalAlignment.Left,
    };

    /// <summary>줄 사이를 띄우는 빈 칸.</summary>
    protected static UIElement Gap(double height = 10) => new Border { Height = height };

    /// <summary>
    /// 묶음 머리 — 게임은 <c>━━━━━━━━기술━━━━━━━━</c> 처럼 줄표로 싼다.
    /// </summary>
    protected static UIElement Divider(string text, byte color = GameFont.WhiteColor) =>
        Label($"   ━━━━━━━━{text}━━━━━━━━", color);

    /// <summary>제목 줄 오른쪽 끝의 닫기(X).</summary>
    /// <summary>
    /// 오른쪽 위 닫기(X). 조선소·시장 창과 <b>같은 상자</b>다 — 게임도 한 가지만 쓴다.
    /// </summary>
    private FrameworkElement CloseBox()
    {
        var box = GameUi.CloseBox(Close);
        box.Margin = new Thickness(0, 2, 2, 0);
        return box;
    }

    /// <summary>줄을 죽 늘어놓는 칸. 비어 있으면 자리만 비워 둔다(게임도 그렇다).</summary>
    protected static UIElement List(IEnumerable<string> lines, double height) =>
        List(lines, height, null);

    /// <summary>
    /// 굴러가는 줄 목록. <paramref name="page"/> 를 주면 그 색 양피지 판에 얹고 게임 굴림대를
    /// 단다 — 새 놀이 확인 화면이 그렇다(게임 것은 <c>#FFEFD6</c> 판이다).
    /// </summary>
    protected static UIElement List(IEnumerable<string> lines, double height, Brush? page)
    {
        var stack = new StackPanel { Margin = new Thickness(0, 2, 0, 0) };
        bool onPage = page != null;
        foreach (string line in lines)
            stack.Children.Add(onPage ? Page($"  {line}") : Label($"      {line}"));

        return new Border
        {
            Height = height,
            Background = page,
            BorderBrush = onPage ? GameUi.ItemEdge : null,
            BorderThickness = new Thickness(onPage ? 1 : 0),
            // 게임 굴림대는 화살표 조각으로 짓는다 — 윈도 굴림대는 모양이 너무 다르다.
            Child = GameUi.Scroller(stack, height),
        };
    }

    /// <summary>양피지 판에 얹는 줄 — 바탕이 밝으니 글씨는 검다.</summary>
    private static FrameworkElement Page(string text) =>
        new GameUi.GameLabel(GameFont.BlackColor)
        {
            Text = text,
            FallbackBrush = Brushes.Black,
            HorizontalAlignment = HorizontalAlignment.Left,
        };
}
