using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using CdsHelper.Game.Local.Helpers;

namespace CdsHelper.Game.UI.Views;

/// <summary>
/// 게임 띠로 지은 단추 하나. 메뉴 줄도, 창 아래 결정·중단도, 메뉴 제목 줄도 다 이것이다.
/// </summary>
/// <remarks>
/// <b>제목 줄도 단추다.</b> 띠 무늬(<see cref="BandStyle"/>)가 세 벌인데 셋 다 MISC.CDS
/// 파트 4 의 같은 그림에서 나오고, <see cref="GameUi.BandFrame"/> 하나가 셋을 다 짓는다.
/// 제목은 <b>무늬가 Title 이고 눌리지 않는 단추</b>일 뿐이다.
/// <code>
///   Title  = 0   진홍 장식    제목 줄
///   Button = 1   베이지       보통 줄
///   Alt    = 2   회녹색       끝 줄(취소·나가기)
/// </code>
///
/// 예전에는 같은 것이 <b>넷</b>이었다. 하는 일은 같은데 할 줄 아는 것이 조금씩 달랐다.
/// <code>
///   GameUi.MenuItem              무늬 셋 · 흐림       초점 없음 · 켜고 끄기 없음
///   GameUi.BandButton            켜고 끄기            초점 없음 · Button 무늬만
///   GameUi.FocusGroup.Add        초점                 켜고 끄기 없음 · Button 무늬만
///   ShipMapWindow.TitleMenuItem  초점(따로 구현)       나머지 없음
/// </code>
/// 넷째는 <c>FocusLight</c>·<c>FocusDark</c>·<c>FocusBlink</c> 를 제 것으로 다시 선언하고
/// 깜빡임을 손수 굴리기까지 했다 — <see cref="GameUi"/> 에 같은 값이 이미 있는데도.
/// 이 클래스가 그 넷을 하나로 접는다.
///
/// <b>다시 지을 때 초점 솔은 그대로 든다.</b> 글자나 켜짐이 바뀌면 속을 통째로 다시 짓는데
/// (속 모양이 한 가지가 아니라 — 원본 조각을 읽었으면 띠 그림 위에 비트맵 글씨를 얹은
/// <c>Grid</c> 고, 못 읽었을 때만 <c>TextBlock</c> 이다), 깜빡임은 <b>솔</b>에 걸린
/// 애니메이션이라 솔만 지켜 주면 끊기지 않는다.
/// </remarks>
internal sealed class GameButton : Border
{
    /// <summary>단추 사이를 벌리는 여백. 메뉴 줄은 붙여 쌓으므로 <c>default</c> 로 덮는다.</summary>
    public static readonly Thickness Spacing = new(10, 0, 10, 0);

    private BandStyle _style;
    private readonly double _width;

    /// <summary>초점 테 색. 깜빡임이 이 솔에 걸리므로 다시 지어도 이것만은 안 갈아 낸다.</summary>
    private readonly SolidColorBrush _ring = new(Colors.Transparent);

    private string _text;
    private Action? _run;
    private bool _on = true;
    private bool _lit;
    private bool _focused;

    /// <param name="text">줄에 적히는 글.</param>
    /// <param name="run">누르면 할 일. null 이면 흐리고 안 눌린다(제목 줄이 그렇다).</param>
    /// <param name="style">띠 무늬.</param>
    /// <param name="width">0 이면 글자 길이에 맞춘다.</param>
    public GameButton(string text, Action? run = null,
                      BandStyle style = BandStyle.Button, double width = 0)
    {
        _text = text;
        _run = run;
        _style = style;
        _width = width;
        Margin = Spacing;

        // 누름과 뗌은 여기서 한 번만 건다. 속은 다시 지어도 이 껍데기는 그대로다.
        // 누름도 삼킨다 — 창 끌기(DragMove)가 먼저 걸리면 마우스를 잡아 버려 뗌이 안 온다.
        MouseLeftButtonDown += (_, e) => e.Handled = true;
        MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            if (_on) _run?.Invoke();
        };

        Build();
    }

    /// <summary>줄에 적히는 글. 바꾸면 다시 짓는다.</summary>
    public string Text
    {
        get => _text;
        set { if (_text != value) { _text = value; Build(); } }
    }

    /// <summary>누르면 할 일. 바꾸면 다시 짓는다(흐림이 갈리므로).</summary>
    public Action? Run
    {
        get => _run;
        set { _run = value; Build(); }
    }

    /// <summary>눌리게 할지. 끄면 글자가 회색이 되고 손 모양 커서도 사라진다.</summary>
    public bool On
    {
        get => _on;
        set { if (_on != value) { _on = value; Build(); } }
    }

    /// <summary>
    /// 초점이 왔는지. 오면 안쪽 테가 0.5초마다 깜빡인다 — 게임이 지금 고른 줄을 그렇게 알린다.
    /// </summary>
    public bool Focused
    {
        get => _focused;
        set
        {
            if (_focused == value) return;
            _focused = value;
            if (value) GameUi.StartBlink(_ring);
            else GameUi.StopBlink(_ring);
        }
    }

    /// <summary>
    /// 누를 일이 없어도 밝은 글씨로 둘지. <b>보여 주기만 하는 칸</b>이 그렇다 —
    /// 상단 띠의 날짜·좌표 칸은 눌리지 않지만 흐리지도 않다.
    /// </summary>
    /// <remarks>
    /// 이것이 없으면 <see cref="Run"/> 이 null 인 줄은 무조건 회색(색인 21)이 된다.
    /// 못 누르는 것과 할 일이 없는 것은 다르다.
    /// </remarks>
    public bool Lit
    {
        get => _lit;
        set { if (_lit != value) { _lit = value; Build(); } }
    }

    /// <summary>제목 무늬인지. 제목 줄은 초점도 안 받고 눌리지도 않는다.</summary>
    public bool IsTitle => _style == BandStyle.Title;

    /// <summary>
    /// 띠 무늬. 바꾸면 다시 짓는다 — 고른 것과 안 고른 것을 갈라 낼 때 쓴다.
    /// </summary>
    public BandStyle Band
    {
        get => _style;
        set { if (_style != value) { _style = value; Build(); } }
    }

    private void Build()
    {
        bool live = _on && (_run != null || _lit);

        // 제목은 밝은 글씨에 그림자를 지고, 나머지는 검은 글씨에 그림자가 없다.
        // 흐린 줄은 회색(색인 21)이다.
        byte color = _style == BandStyle.Title ? GameFont.TitleColor
                   : live ? GameFont.ButtonColor
                   : (byte)21;

        var band = GameUi.BandFrame(GameUi.Sprites, _style, _text, color,
                                    shadow: _style == BandStyle.Title, 1, null, _width);

        var ring = new Border
        {
            BorderBrush = _ring,
            BorderThickness = new Thickness(1),
            Margin = new Thickness(2),
        };

        if (band?.Child is Grid grid)
        {
            // 띠 위에 테만 겹친다(바탕 없음). 나중에 넣은 것이 위에 그려진다.
            Grid.SetColumnSpan(ring, 3);
            grid.Children.Add(ring);
            if (_width > 0) band.Width = _width;
            band.Margin = default;              // 바깥 여백은 이 감싸개가 든다
            Child = band;
        }
        else
        {
            // 원본 조각을 못 읽었을 때만 민색 상자로 물러선다.
            FrameworkElement? label = GameUi.GameFontLabel(_text, color, 1,
                                                          GameUi.ItemTextHeight, shadow: false);
            label ??= new TextBlock
            {
                Text = _text,
                Foreground = live ? Brushes.Black : Brushes.Gray,
                FontWeight = FontWeights.Bold,
                FontSize = 14,
            };
            label.HorizontalAlignment = HorizontalAlignment.Center;

            ring.Padding = new Thickness(0, 1, 0, 1);
            ring.Child = label;
            Child = new Border
            {
                Width = _width > 0 ? _width : double.NaN,
                Background = GameUi.ItemFill,
                BorderBrush = GameUi.ItemEdge,
                BorderThickness = new Thickness(1),
                Padding = new Thickness(12, 2, 12, 2),
                Child = ring,
            };
        }

        // 손 모양은 <b>정말 눌리는</b> 줄에만 준다 — 보여 주기만 하는 칸은 밝아도 화살표다.
        Cursor = _on && _run != null ? Cursors.Hand : Cursors.Arrow;
    }
}
