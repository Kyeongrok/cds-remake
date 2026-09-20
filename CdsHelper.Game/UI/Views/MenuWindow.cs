using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using CdsHelper.Game.Local.Helpers;

namespace CdsHelper.Game.UI.Views;

/// <summary>
/// 명령 창 하나를 담아 제 창(HWND)으로 띄운다. 도시 그림 창 옆에 붙여 놓으려고 쓴다 —
/// 그림 안에 그리면 그림이 작을 때 창을 꽉 채워 버린다(게임도 그림 옆에 따로 띄운다).
/// </summary>
public sealed class MenuWindow : GameWindow
{
    private readonly Border _root;

    /// <summary>
    /// 담고 있는 것을 갈아 끼운다. 시설 명령 창에서 "기능" 처럼 한 창 안에서 줄이 바뀔 때 쓴다 —
    /// 창을 새로 띄우면 자리가 튀어 보인다.
    /// </summary>
    public void SetContent(UIElement content) => _root.Child = content;

    /// <summary>
    /// 줄이 바뀌어 창 크기가 달라졌을 때 다시 한가운데로 민다.
    /// </summary>
    /// <remarks>
    /// 게임은 창을 낼 때마다 <c>원점 + 크기/2</c> 를 다시 잰다(<c>0x00469E80</c>).
    /// 우리는 창을 그대로 두고 알맹이만 갈아 끼우므로, 열한 줄짜리 자택 차림표에서
    /// 네 줄짜리 <b>기능</b> 으로 들어가면 <b>왼쪽 위에 그대로 걸려</b> 있었다.
    /// </remarks>
    /// <summary>정해 준 자리로 옮긴다. 화면 밖으로는 안 나간다.</summary>
    public void MoveTo(Point at)
    {
        UpdateLayout();
        Left = Math.Max(0, Math.Min(at.X, SystemParameters.VirtualScreenWidth - ActualWidth));
        Top = Math.Max(0, Math.Min(at.Y, SystemParameters.VirtualScreenHeight - ActualHeight));
    }

    public void Recenter()
    {
        if (Owner is not { } owner) return;
        UpdateLayout();
        Left = Math.Max(0, owner.Left + (owner.Width - ActualWidth) / 2);
        Top = Math.Max(0, owner.Top + (owner.Height - ActualHeight) / 2);
    }

    // ── 여닫는 짓시늉 ────────────────────────────────────────────────────────

    /// <summary>
    /// 창이 점에서 자라고 점으로 오므라드는 데 걸리는 시간과, 시작할 때의 크기.
    /// </summary>
    /// <remarks>
    /// 게임도 시설 명령 창을 낼 때 네모가 가운데 한 점에서 펼쳐지고, 나갈 때 도로 오므라든다.
    /// 창(HWND) 크기만 줄이면 <b>알맹이가 잘리는</b> 것으로 보이므로, 창 크기와 함께
    /// 알맹이도 같은 배로 줄인다(<see cref="ScaleTransform"/>) — 그래야 통째로 작아진다.
    /// 배는 <see cref="UIElement.RenderTransform"/> 이라 자리 계산에 안 끼어든다.
    /// </remarks>
    private static readonly Duration ZoomTime = new(TimeSpan.FromMilliseconds(110));
    private const double ZoomLeast = 0.06;

    private readonly ScaleTransform _zoom = new(1, 1);

    /// <summary>오므라드는 중인지. 닫으라는 말이 두 번 들어오는 것을 막는다.</summary>
    private bool _shrinking;

    /// <summary>
    /// 이미 닫힌 창인지. <b>애니메이션이 끝난 뒤에 손대면 터진다</b> — WPF 는 닫힌 창에
    /// <c>Close()</c> 나 크기 손질을 하면 예외를 던진다.
    /// </summary>
    /// <remarks>
    /// 주인 창이 먼저 닫히면 거느린 이 창도 함께 부서지는데, 그때 굴러가던 짓시늉의
    /// 끝맺음이 뒤늦게 와서 부서진 창을 건드렸다. 그 예외가 타이머를 타고 올라가
    /// <b>입항 물음이 아예 안 뜨는</b> 것으로 나타났다.
    /// </remarks>
    private bool _closed;

    /// <summary>
    /// 닫히는 중인지. 주인 창이 닫히며 이 창을 거두는 도중에 오므리기가 끝나 <c>Close()</c> 를 또 부르면
    /// 「창을 닫는 중에는 … Close … 를 호출할 수 없습니다」로 터진다 — 그 틈을 막는다.
    /// </summary>
    private bool _closing;

    /// <summary>점에서 펼친다. 자리를 다 잡은 뒤에 부른다.</summary>
    private void Grow() => Zoom(grow: true, done: null);

    /// <summary>점으로 오므린 뒤 닫는다.</summary>
    public void CloseZoomed()
    {
        if (_shrinking || _closed) return;
        _shrinking = true;

        // <b>알맹이를 먼저 감춘다</b> — 줄과 글은 사라지고 테두리만 남아 접히는 꼴이 된다.
        // 자리는 그대로 두므로(Hidden) 접히는 크기가 달라지지 않는다.
        if (_root.Child is { } inside) inside.Visibility = Visibility.Hidden;

        Zoom(grow: false, done: () => { if (!_closed && !_closing) Close(); });
    }

    private void Zoom(bool grow, Action? done)
    {
        double w = ActualWidth, h = ActualHeight, l = Left, t = Top;
        if (_closed || w <= 0 || h <= 0) { done?.Invoke(); return; }

        // 애니메이션 동안에는 크기를 못 박아 둔다 — 알맹이에 맞춰 크기를 다시 잡는
        // 규칙이 살아 있으면 애니메이션이 먹지 않는다.
        SizeToContent = SizeToContent.Manual;

        double sw = w * ZoomLeast, sh = h * ZoomLeast;
        double sl = l + (w - sw) / 2, st = t + (h - sh) / 2;
        var ease = new SineEase { EasingMode = EasingMode.EaseOut };

        Move(LeftProperty, grow ? sl : l, grow ? l : sl);
        Move(TopProperty, grow ? st : t, grow ? t : st);
        Move(WidthProperty, grow ? sw : w, grow ? w : sw);
        Move(HeightProperty, grow ? sh : h, grow ? h : sh);

        var scale = new DoubleAnimation(grow ? ZoomLeast : 1, grow ? 1 : ZoomLeast, ZoomTime)
        {
            EasingFunction = ease,
        };
        if (grow) scale.Completed += (_, _) => Settle(l, t);
        else if (done != null) scale.Completed += (_, _) => done();
        _zoom.BeginAnimation(ScaleTransform.ScaleXProperty, scale);
        _zoom.BeginAnimation(ScaleTransform.ScaleYProperty, scale);

        void Move(DependencyProperty what, double from, double to) =>
            BeginAnimation(what, new DoubleAnimation(from, to, ZoomTime) { EasingFunction = ease });
    }

    /// <summary>
    /// 다 펼쳐지고 나면 애니메이션을 걷고 <b>알맹이에 맞춰 크기를 잡는 규칙</b>을 되돌린다.
    /// 안 걷으면 그 뒤로 크기를 못 바꾼다 — 애니메이션 값이 계속 이긴다.
    /// </summary>
    private void Settle(double left, double top)
    {
        if (_closed) return;

        foreach (var what in new[] { LeftProperty, TopProperty, WidthProperty, HeightProperty })
            BeginAnimation(what, null);
        _zoom.BeginAnimation(ScaleTransform.ScaleXProperty, null);
        _zoom.BeginAnimation(ScaleTransform.ScaleYProperty, null);
        _zoom.ScaleX = _zoom.ScaleY = 1;

        Width = double.NaN;
        Height = double.NaN;
        SizeToContent = SizeToContent.WidthAndHeight;
        Left = left;
        Top = top;
    }

    private MenuWindow(UIElement content)
    {
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.WidthAndHeight;
        WindowStartupLocation = WindowStartupLocation.Manual;
        ShowInTaskbar = false;
        // <b>레이어드 창으로 두지 않는다.</b> AllowsTransparency 를 켜면 창이 소프트웨어로
        // 합성되어, 겹쳐 있는 창들 사이에서 활성 창이 오갈 때마다 통째로 다시 그려진다 —
        // 대화 창을 여닫을 때 화면이 깜빡이던 것이 그것이다. 이 창은 네모지고 속이 꽉
        // 차 있어 비침이 필요 없다.
        Background = GameUi.Back;

        _root = new Border
        {
            Background = GameUi.Back,
            Child = content,
            RenderTransform = _zoom,
            RenderTransformOrigin = new Point(0.5, 0.5),
        };
        Content = _root;
        GameUi.EnableDrag(this, _root);
        GameUi.CarryOwnedWindows(this);   // 이 창에서 연 창(힌트 일람 따위)도 같이 옮긴다

        KeyDown += (_, e) => { if (e.Key is Key.Escape) CloseZoomed(); };
        MouseRightButtonUp += (_, _) => CloseZoomed();

        // <b>닫기 전에 주인 창을 먼저 띄운다.</b> 창을 부수고 나서 초점을 정하게 두면
        // 윈도가 다음 창을 z 차례에서 고르는데, 우리 창들은 테 없는 데다 작업표시줄에도
        // 안 나와서 <b>다른 앱으로 새어 나간다</b> — 도서관에서 나올 때 편집기나 터미널이
        // 잠깐 앞으로 나왔다 들어오던 것이 그것이다. 부수기 전에 주인을 띄워 두면
        // 고를 것이 이미 정해져 있어 샐 일이 없다.
        Closing += (_, _) => { _closing = true; Owner?.Activate(); };

        // 그래도 새면 붙들어 온다(위 한 줄로 안 잡히는 자리가 남아 있을 수 있다).
        FocusWatch.KeepInApp(this);

        // 초점이 어디로 가는지 보려고 둔 진단(FocusWatch). 다 잡고 나면 지운다.
        Closed += (_, _) => { _closed = true; FocusWatch.After("명령창 닫힘"); };
        Deactivated += (_, _) => FocusWatch.After("명령창 초점 잃음");
    }

    /// <summary>
    /// 주인 창 <b>한가운데</b>에 띄운다. 게임이 시설 명령 창을 내는 자리다.
    /// </summary>
    /// <remarks>
    /// 게임은 누른 건물과 상관없이 늘 그리는 영역 한가운데에 낸다 — 메뉴 객체의 자리가
    /// 음수(<c>POINT(-1,-1)</c> = 안 정함)면 <c>원점 + 크기/2</c> 로 잡고, 한 번 잡으면
    /// 그 자리를 계속 쓴다(<c>0x00469E80</c>, 볼트 <c>15.분석-시설 화면 엔진</c>).
    ///
    /// 예전에는 주인 창 오른쪽에 붙여 냈는데 게임에는 없는 모습이었다.
    /// </remarks>
    public static MenuWindow ShowCentered(Window owner, UIElement content)
    {
        var window = new MenuWindow(content) { Owner = owner };
        window.Show();      // 크기를 알아야 자리를 잡는다

        window.Left = Math.Max(0, owner.Left + (owner.Width - window.ActualWidth) / 2);
        window.Top = Math.Max(0, owner.Top + (owner.Height - window.ActualHeight) / 2);
        window.Grow();
        return window;
    }

    /// <summary>
    /// 정해 준 자리(화면 좌표, WPF 단위)에 왼쪽 위 모서리를 맞춰 띄운다. 상단 띠에서 부르는
    /// 도시정보 창처럼 누른 자리 밑에 붙여야 하는 것에 쓴다. 화면 밖으로는 안 나가게 민다.
    /// </summary>
    public static MenuWindow ShowAt(Window owner, UIElement content, Point at)
    {
        var window = new MenuWindow(content) { Owner = owner };
        window.Show();      // 크기를 알아야 화면 안으로 밀어 넣을 수 있다

        window.Left = Math.Max(0, Math.Min(at.X, SystemParameters.VirtualScreenWidth - window.ActualWidth));
        window.Top = Math.Max(0, Math.Min(at.Y, SystemParameters.VirtualScreenHeight - window.ActualHeight));
        window.Grow();
        return window;
    }
}
