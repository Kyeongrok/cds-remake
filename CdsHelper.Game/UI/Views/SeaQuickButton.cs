using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace CdsHelper.Game.UI.Views;

/// <summary>
/// 모드 「빠른 메뉴」 — 항해 중 지도 오른쪽 위에 띄우는 ☰ 단추. 누르면 함대정보 · 실린 짐을 고르는 메뉴가 뜬다(요청).
/// </summary>
/// <remarks>
/// 지도는 D3D 자식 창이라 그 위에 WPF 를 그릴 수 없다(airspace) — 항해 일수 동그라미(<see cref="SeaDaysBadge"/>)처럼
/// 제 창으로 띄운다. 초점은 뺏지 않고 작업표시줄에도 안 뜬다. 동영상이 도는 동안에는 다른 쪽지처럼 걷힌다(<see cref="OverlayNotes"/>).
/// </remarks>
internal sealed class SeaQuickButton : Window
{
    /// <summary>단추 한 변.</summary>
    public const double Size = 36;

    private static readonly Brush Idle = Frozen(Color.FromArgb(0xC0, 0x10, 0x18, 0x28));
    private static readonly Brush Lit = Frozen(Color.FromArgb(0xE0, 0x30, 0x40, 0x58));

    private static Brush Frozen(Color c)
    {
        var b = new SolidColorBrush(c);
        b.Freeze();
        return b;
    }

    private readonly Border _face;
    private bool _wanted;
    private bool _closed;

    /// <param name="click">눌렀을 때 — 단추 왼쪽 아래 화면 자리(WPF 단위)를 넘긴다.</param>
    public SeaQuickButton(Window owner, Action<Point> click)
    {
        Owner = GameWindow.Alive(owner, this);
        Title = "빠른 메뉴";
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        ShowActivated = false;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        Width = Size;
        Height = Size;
        WindowStartupLocation = WindowStartupLocation.Manual;

        _face = new Border
        {
            Width = Size,
            Height = Size,
            CornerRadius = new CornerRadius(5),
            Background = Idle,
            BorderBrush = new SolidColorBrush(Color.FromArgb(0xE0, 0xD4, 0xC8, 0xB0)),
            BorderThickness = new Thickness(2),
            Cursor = Cursors.Hand,
            ToolTip = Local.Helpers.Lang.T("함대정보 · 실린 짐"),
            Child = new TextBlock
            {
                Text = "☰",
                Foreground = Brushes.White,
                FontSize = 19,
                FontWeight = FontWeights.Bold,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, -2, 0, 0),
            },
        };
        _face.MouseEnter += (_, _) => _face.Background = Lit;
        _face.MouseLeave += (_, _) => _face.Background = Idle;
        _face.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            click(new Point(Left, Top + Size + 2));
        };
        Content = _face;

        OverlayNotes.Changed += Apply;
        Closed += (_, _) => { _closed = true; OverlayNotes.Changed -= Apply; };
    }

    public bool IsClosed => _closed;

    /// <summary>화면 자리(WPF 단위, 단추 왼쪽 위)에 놓고 보이거나 감춘다.</summary>
    public void Set(bool show, Point at)
    {
        if (_closed) return;
        _wanted = show;
        if (show)
        {
            Left = at.X;
            Top = at.Y;
        }
        Apply();
    }

    private void Apply()
    {
        if (_closed) return;
        bool show = _wanted && !OverlayNotes.Held;
        try
        {
            if (show && !IsVisible) Show();
            else if (!show && IsVisible) Hide();
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            // 투명 창을 못 띄우면 이 단추는 버린다 — 다음 번에 새로 짓는다(WorldMapWindow.SyncQuickButton).
            _closed = true;
            OverlayNotes.Changed -= Apply;
            GameWindow.TryClose(this);
        }
    }
}
