using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace CdsHelper.Game.UI.Views;

/// <summary>
/// 지도를 처음 올릴 때 띄우는 <b>준비 쪽지</b> — 「지도를 읽는 중...」이 깜빡인다. 멈춘 듯 보이지 않게.
/// </summary>
/// <remarks>
/// 지도와 고해상도 표를 짓는 일은 화면 실을 통째로 붙잡는다. 그래서 쪽지는 <b>제 실</b>에서 제 창으로 돈다 —
/// 화면 실이 막혀 있어도 깜빡인다. 실이 다르니 주인 창을 걸 수 없어, 주인 창 가운데 자리를 재서 맨 위 창으로 띄운다.
/// </remarks>
internal sealed class LoadingDialog
{
    private const double W = 520, H = 104;

    /// <summary>쪽지 아래에 하나씩 뽑아 적는 팁 — 원본에 없이 새로 든 것들.</summary>
    private static readonly string[] Tips =
    [
        "M 키로 모드 창을 엽니다. 원본에 없는 기능은 여기서 켜고 끕니다.",
        "X 키로 인물정보를, D 키로 발견물 지도를 엽니다.",
        "모드 「미니맵」 탭에서 표식을 종류별로 켜고 끌 수 있습니다.",
        "모드 「수에즈 운하」를 켜면 지중해에서 홍해로 곧장 지나갑니다.",
        "모드 「작위」를 켜면 발견물 보고와 교역소 투자로 공적이 쌓입니다.",
        "작위 혜택은 누적됩니다. 높은 작위는 아래 작위의 혜택도 함께 받습니다.",
        "모드 「정보 등급」을 상세로 두면 도서관 책의 읽기 조건이 보입니다.",
        "발견물 지도에서 「도」가 적힌 도시에는 도서관이 있습니다.",
        "해전에서 이기면 적 배에 실려 있던 짐을 옮겨 실을 수 있습니다.",
        "모드 「접근 함대 정보」를 켜면 다가간 함대의 국적과 규모가 보입니다.",
        "모드 「부하 해고」를 켜면 부하편성 창에 「해고」 단추가 생깁니다.",
        "모드 「뭍 자동이동」: 발견물 지도의 점을 오른쪽 단추로 누르면 걸어갑니다.",
    ];

    private Window? _window;
    private readonly ManualResetEventSlim _shown = new();

    /// <summary>쪽지를 띄운다. 다 지었으면 <see cref="Close"/>.</summary>
    public static LoadingDialog Open(Window owner, string text)
    {
        var dialog = new LoadingDialog();
        // 주인 창 가운데(화면 점) — 제 실의 창은 DIP 로 자리를 잡으므로 DPI 배율로 나눈다.
        var dpi = VisualTreeHelper.GetDpi(owner);
        var mid = owner.PointToScreen(new Point(owner.ActualWidth / 2, owner.ActualHeight / 2));
        double left = mid.X / dpi.DpiScaleX - W / 2, top = mid.Y / dpi.DpiScaleY - H / 2;

        string tip = Tips[Random.Shared.Next(Tips.Length)];
        var thread = new Thread(() =>
        {
            var label = new TextBlock
            {
                Text = text,
                Foreground = new SolidColorBrush(Color.FromRgb(0xF4, 0xE8, 0xE0)),
                FontSize = 16,
                FontWeight = FontWeights.Bold,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            };
            label.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(1, 0.2, TimeSpan.FromSeconds(0.6))
            {
                AutoReverse = true,
                RepeatBehavior = RepeatBehavior.Forever,
            });
            var window = new Window
            {
                WindowStyle = WindowStyle.None,
                ResizeMode = ResizeMode.NoResize,
                ShowInTaskbar = false,
                ShowActivated = false,
                Topmost = true,
                Width = W,
                Height = H,
                Left = left,
                Top = top,
                WindowStartupLocation = WindowStartupLocation.Manual,
                Background = new SolidColorBrush(Color.FromRgb(0x2A, 0x18, 0x18)),
                Content = new Border
                {
                    BorderBrush = new SolidColorBrush(Color.FromRgb(0x8A, 0x70, 0x58)),
                    BorderThickness = new Thickness(2),
                    Child = new StackPanel
                    {
                        VerticalAlignment = VerticalAlignment.Center,
                        Children =
                        {
                            label,
                            new TextBlock
                            {
                                Text = "TIP  " + tip,
                                Foreground = new SolidColorBrush(Color.FromRgb(0xC8, 0xB8, 0xA8)),
                                FontSize = 13,
                                TextWrapping = TextWrapping.Wrap,
                                TextAlignment = TextAlignment.Center,
                                Margin = new Thickness(14, 12, 14, 0),
                            },
                        },
                    },
                },
            };
            dialog._window = window;
            window.Show();
            dialog._shown.Set();
            Dispatcher.Run();
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        dialog._shown.Wait(TimeSpan.FromSeconds(2));
        return dialog;
    }

    /// <summary>쪽지를 거두고 제 실을 끝낸다.</summary>
    public void Close()
    {
        if (_window is not { } window) return;
        _window = null;
        window.Dispatcher.BeginInvoke(() =>
        {
            window.Close();
            Dispatcher.CurrentDispatcher.InvokeShutdown();
        });
    }
}
