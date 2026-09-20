using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace CdsHelper.Game.UI.Views;

/// <summary>
/// 지금 함대를 적어 두는 <b>쪽지 창</b> — 배마다 한 줄 「배 이름(선체)」.
/// </summary>
/// <remarks>
/// 예전에는 도시 그림 왼쪽 위에 글씨를 얹었는데, 그러면 그림(테두리·건물)을 가리는 데다
/// 자리를 옮길 수도 없었다. 그래서 <b>도시 창 밖</b>에 제 창으로 띄우고 <b>끌어 옮길 수</b>
/// 있게 했다.
///
/// 자리는 주인 창(도시 그림) 왼쪽 위에서 잰 <b>거리</b>로 들고 있다. 그래서
/// <list type="bullet">
///   <item>도시 창을 끌면 쪽지도 같은 사이를 두고 따라온다.</item>
///   <item>쪽지를 끌면 그 거리가 새로 적히고, 다음에 다른 도시에 들어가도 그 자리에 뜬다.</item>
/// </list>
/// 게임에는 없는 덧그림이라 창 테도 없고 작업표시줄에도 안 뜬다. 초점도 뺏지 않는다 —
/// 끌고 놓으면 곧바로 도시 창에 초점을 돌려준다.
/// </remarks>
public sealed class FleetLabelWindow : Window
{
    /// <summary>도시 창과 쪽지 사이. 처음에는 그림 오른쪽에 이만큼 띄워 붙인다.</summary>
    private const double Gap = 10;

    /// <summary>
    /// 주인 창 왼쪽 위에서 잰 쪽지 자리. 한 번 옮겨 두면 앱이 도는 동안 그대로다 —
    /// 도시에 드나들 때마다 쪽지가 제자리로 돌아가면 옮긴 뜻이 없다.
    /// </summary>
    private static double _dx = double.NaN, _dy;

    private readonly TextBlock _text = new()
    {
        Foreground = Brushes.White,
        FontWeight = FontWeights.Bold,
        Effect = new DropShadowEffect
        {
            Color = Colors.Black,
            ShadowDepth = 1.5,
            BlurRadius = 3,
            Opacity = 1,
        },
    };

    private readonly Window _anchor;

    private FleetLabelWindow(Window anchor, double fontSize)
    {
        _anchor = anchor;
        _text.FontSize = fontSize;

        Title = "함대";
        Owner = anchor;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        ShowActivated = false;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        SizeToContent = SizeToContent.WidthAndHeight;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Cursor = Cursors.SizeAll;       // 끌어 옮기는 창이라고 알린다

        Content = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(0xB0, 0x00, 0x00, 0x00)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x50, 0xFF, 0xFF, 0xFF)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(10, 6, 10, 6),
            Child = _text,
        };

        // 아무 데나 눌러 끈다. 놓으면 새 자리를 적어 두고 초점을 도시 창에 돌려준다.
        MouseLeftButtonDown += (_, _) =>
        {
            DragMove();
            Remember();
            _anchor.Activate();
        };

        // 도시 창이 움직이면 같은 사이를 두고 따라간다(펼침 효과로 미끄러져 들어올 때도).
        anchor.LocationChanged += OnAnchorMoved;
        anchor.SizeChanged += OnAnchorMoved;
        Closed += (_, _) =>
        {
            anchor.LocationChanged -= OnAnchorMoved;
            anchor.SizeChanged -= OnAnchorMoved;
        };

        SizeChanged += (_, _) => Place();
    }

    private void OnAnchorMoved(object? sender, EventArgs e) => Place();

    /// <summary>주인 창 옆에 쪽지를 띄운다. 글이 비면 안 뜬다.</summary>
    public static FleetLabelWindow Attach(Window anchor, double fontSize)
    {
        var note = new FleetLabelWindow(anchor, fontSize);
        note.Show();
        note.Place();
        return note;
    }

    /// <summary>적을 글을 갈아 준다. 빈 글이면 쪽지를 감춘다.</summary>
    public void Set(string text)
    {
        _text.Text = text;
        Visibility = text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>
    /// 사건이 도는 동안 감춘다 — 도시 그림은 파란 막에 덮이는데 쪽지만 훤하면 어색하다.
    /// </summary>
    public void Shade(bool on)
    {
        if (_text.Text.Length == 0) return;
        Visibility = on ? Visibility.Collapsed : Visibility.Visible;
    }

    /// <summary>지금 자리를 주인 창에서 잰 거리로 적어 둔다.</summary>
    private void Remember()
    {
        _dx = Left - _anchor.Left;
        _dy = Top - _anchor.Top;
    }

    /// <summary>
    /// 적어 둔 거리대로 자리를 잡는다. 아직 옮긴 적이 없으면 <b>그림 오른쪽</b>에 붙이고,
    /// 거기가 화면 밖이면 왼쪽으로 돌려 붙인다.
    /// </summary>
    private void Place()
    {
        if (double.IsNaN(_anchor.Left) || double.IsNaN(_anchor.Top)) return;

        double width = ActualWidth > 0 ? ActualWidth : Width;
        if (double.IsNaN(width)) width = 0;

        if (double.IsNaN(_dx))
        {
            _dx = _anchor.ActualWidth + Gap;
            _dy = 0;

            // 오른쪽이 화면 밖이면 그림 왼쪽에 붙인다.
            double right = SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth;
            if (_anchor.Left + _dx + width > right) _dx = -(width + Gap);
        }

        Left = _anchor.Left + _dx;
        Top = _anchor.Top + _dy;
    }
}
