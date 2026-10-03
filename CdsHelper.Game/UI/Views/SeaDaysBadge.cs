using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace CdsHelper.Game.UI.Views;

/// <summary>
/// 모드 「항해 일수」 — 항해 중 지도 왼쪽 위에 <b>동그라미 안에 출항한 지 며칠</b>인지 띄우는 작은 창.
/// </summary>
/// <remarks>
/// 지도는 D3D 자식 창이라 그 위에 WPF 를 그릴 수 없다(airspace) — 배 속도 쪽지처럼 제 창으로 띄운다.
/// 초점은 뺏지 않고, 작업표시줄에도 안 뜬다. 동영상이 도는 동안에는 다른 쪽지처럼 걷힌다(<see cref="OverlayNotes"/>).
/// </remarks>
internal sealed class SeaDaysBadge : Window
{
    /// <summary>동그라미 지름.</summary>
    private const double Size = 44;

    private readonly TextBlock _days = new()
    {
        Foreground = Brushes.White,
        FontWeight = FontWeights.Bold,
        FontSize = 17,
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center,
        Effect = new DropShadowEffect { Color = Colors.Black, ShadowDepth = 1.2, BlurRadius = 2, Opacity = 1 },
    };

    /// <summary>보여야 하는지 — 항해 중이고 모드가 켜졌을 때. 동영상이 도는 동안에는 이것과 상관없이 감춘다.</summary>
    private bool _wanted;

    private bool _closed;

    public SeaDaysBadge(Window owner)
    {
        Owner = owner;
        Title = "항해 일수";
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        ShowActivated = false;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        Width = Size;
        Height = Size;
        WindowStartupLocation = WindowStartupLocation.Manual;
        IsHitTestVisible = false;

        Content = new Border
        {
            Width = Size,
            Height = Size,
            CornerRadius = new CornerRadius(Size / 2),
            Background = new SolidColorBrush(Color.FromArgb(0xC0, 0x10, 0x18, 0x28)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0xE0, 0xD4, 0xC8, 0xB0)),
            BorderThickness = new Thickness(2),
            ToolTip = "출항한 지 며칠",
            Child = _days,
        };

        OverlayNotes.Changed += Apply;
        Closed += (_, _) => { _closed = true; OverlayNotes.Changed -= Apply; };
    }

    public bool IsClosed => _closed;

    /// <summary>일수를 적고 화면 자리(WPF 단위)에 놓는다. <paramref name="days"/> 가 음수면 감춘다.</summary>
    public void Set(int days, Point at)
    {
        if (_closed) return;
        _wanted = days >= 0;
        if (_wanted)
        {
            _days.Text = days.ToString();
            // 세 자리면 글자를 줄여 동그라미 안에 든다.
            _days.FontSize = days >= 100 ? 14 : 17;
            Left = at.X;
            Top = at.Y;
        }
        Apply();
    }

    private void Apply()
    {
        if (_closed) return;
        bool show = _wanted && !OverlayNotes.Held;
        if (show && !IsVisible) Show();
        else if (!show && IsVisible) Hide();
    }
}
