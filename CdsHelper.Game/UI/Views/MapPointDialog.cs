using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using CdsHelper.Game.Local.Helpers;

namespace CdsHelper.Game.UI.Views;

/// <summary>
/// 「어디로 들어 가시겠습니까?」 — 그 도시의 건물을 늘어놓고 하나를 고르게 한다.
/// </summary>
/// <remarks>
/// 도시 커맨드의 "맵 포인트에 들어간다"(<c>0x0053BE10</c>)가 여는 창이고, 제목은
/// <c>0x0053BF38</c> 이다. 줄은 <b>건물 이름</b>이다 — "베렌의 탑" · "리스본 왕립 도서관"
/// 처럼 그 도시만의 이름이 뜨고, 이름이 없는 건물은 종류로 낸다.
///
/// 줄이 열 몇 개라 오른쪽에 굴림대가 선다. 굴림대는 게임 화살표 조각으로 지은 것이고
/// (<see cref="GameUi.Scroller"/>), 창 테는 구슬 무늬 액자(<see cref="GameUi.WindowFrame"/>)다 —
/// 윈도 굴림대와 민 테를 쓰던 것을 게임 것으로 갈았다. 줄마다 게임 띠 단추를 그대로 쓰므로
/// 도시 그림에서 건물을 누르는 것과 같은 모습이 된다.
/// </remarks>
internal sealed class MapPointDialog : GameWindow
{
    /// <summary>줄이 이보다 많으면 굴림대를 낸다.</summary>
    private const int RowsShown = 12;

    /// <summary>줄 하나의 높이(띠 단추 + 사이).</summary>
    private const double RowHeight = 30;

    private int _picked = -1;

    /// <summary>닫히는 중인가. 초점을 잃었을 때 또 닫지 않으려고 둔다.</summary>
    private bool _closing;

    private MapPointDialog(IReadOnlyList<string> names, string title, double rowWidth,
                           IReadOnlyList<bool>? enabled = null)
    {
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.WidthAndHeight;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        Background = GameUi.Back;

        var rows = new StackPanel();
        for (int i = 0; i < names.Count; i++)
        {
            int pick = i;
            bool on = enabled == null || i >= enabled.Count || enabled[i];
            var button = new GameButton(names[i], on ? () => { _picked = pick; Close(); } : null,
                                        BandStyle.Button, rowWidth);
            button.Margin = new Thickness(0, 1, 0, 1);
            rows.Children.Add(button);
        }

        var bar = GameUi.TitleBar(title, Close);
        GameUi.EnableDrag(this, bar);

        var scroller = GameUi.Scroller(rows, RowsShown * RowHeight);
        scroller.Margin = new Thickness(3, 2, 3, 3);

        var stack = new StackPanel();
        stack.Children.Add(bar);
        stack.Children.Add(scroller);

        // 테는 도시 명령 창과 같은 한 점이다(GameMenu.BoxEdge). 무늬 액자를 두르면
        // 줄 단추의 테와 겹쳐 두꺼워 보인다.
        Content = new Border
        {
            Background = GameUi.MenuBack,
            BorderBrush = GameUi.MenuEdge,
            BorderThickness = new Thickness(1),
            Child = stack,
        };

        KeyDown += (_, e) => { if (e.Key is Key.Escape) Close(); };
        MouseRightButtonUp += (_, _) => Close();

        // 닫기 단추를 안 눌러도 다른 데를 누르면 닫힌다 — 도시 명령 창과 같은 결이다.
        //
        // <b>닫는 중인지 봐야 한다.</b> 줄을 골라 창이 닫히는 동안에도 초점을 잃으며
        // 이 손잡이가 또 불리는데, 그때 Close 를 부르면 «창을 닫는 중에는 Close 를 호출할
        // 수 없습니다» 로 터진다.
        Closing += (_, _) => _closing = true;
        Deactivated += (_, _) => { if (!_closing) Close(); };
    }

    /// <summary>줄 하나의 너비. 도시 이름이 붙은 긴 줄까지 들어가게 잡는다.</summary>
    public const double RowWidth = 340;

    /// <summary>도시 이름만 들어가면 되는 좁은 줄(항구 "마을정보").</summary>
    public const double NarrowWidth = 168;

    /// <summary>
    /// 이름만 늘어놓는 차림표(타이틀의 「미니 게임」)의 줄 너비.
    /// </summary>
    /// <remarks>
    /// 가장 긴 「화살표 입방체 퍼즐」이 들어가면 된다. 도시 이름이 붙는
    /// <see cref="RowWidth"/> 를 그대로 쓰면 글자 옆이 휑하다.
    /// </remarks>
    public const double MenuWidth = 224;

    /// <summary>
    /// 창을 띄우고 고른 줄 번호를 낸다. 물렀으면 -1.
    /// </summary>
    /// <param name="title">제목 줄. 미니 게임 고르기처럼 다른 데서도 쓴다.</param>
    /// <param name="rowWidth">줄 하나의 너비. 짧은 이름만 늘어놓을 때는 좁힌다.</param>
    /// <param name="enabled">줄마다 누를 수 있는지. 없으면 다 된다 — 안 되는 줄은 흐리게 선다.</param>
    public static int Ask(Window owner, IReadOnlyList<string> names,
                          string title = "어디로 들어 가시겠습니까?",
                          double rowWidth = RowWidth, IReadOnlyList<bool>? enabled = null)
    {
        if (names.Count == 0) return -1;

        var dialog = new MapPointDialog(names, title, rowWidth, enabled) { Owner = owner };
        dialog.ShowDialog();
        return dialog._picked;
    }
}
