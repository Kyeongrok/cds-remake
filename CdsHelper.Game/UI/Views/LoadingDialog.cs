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
        "R 키나 제목 줄의 「네비게이션」으로 목적지 도시를 골라 자동항해합니다.",
        "도시 안에서 네비게이션으로 목적지를 고르면 출항해서 그 항구 앞까지 저절로 갑니다.",
        "네비게이션은 바람과 해류를 보고, 돌아가는 편이 빠르면 그 길로 갑니다.",
        "네비게이션의 「최소 조타」를 고르면 뱃머리를 덜 틀며 곧게 갑니다.",
        "자동항해는 지도를 누르거나 스페이스 · 방향키를 누르면 꺼집니다.",
        "네비게이션에서 「항해 시작」을 누르면 항로 후보가 지도와 함께 뜹니다. 하나를 골라 「확인」을 누르십시오.",
        "네비게이션의 도시 목록은 문화권 탭으로 나뉩니다. 닻 표시가 붙은 도시가 항구 도시입니다.",
        "네비게이션에서 도시를 두 번 누르면 곧바로 항로 후보로 넘어갑니다.",
        "「가장 빠른 길」은 바람과 해류를 탄 길, 「가장 짧은 길」은 거리가 가장 짧은 길입니다.",
        "항로 후보 옆의 날수는 지금 함대와 이번 달 바람으로 어림한 값입니다.",
        "내륙 도시를 고르면 네비게이션이 배를 댈 해안을 골라 줍니다. 거기서 내려 걸어가면 됩니다.",
        "내륙 도시는 항해 날수와 걷는 날수를 더해 가장 빠른 해안이 먼저 나옵니다.",
        "걷는 길이 싫다면 내륙 도시의 항로 후보에서 「덜 걷는 길」을 고르십시오.",
        "항로 후보 아래의 목록은 어디서 어느 쪽으로 뱃머리를 트는지 적은 것입니다.",
        "자동항해는 목적지 항구 앞에서 닻을 내리고 들어갈지 묻습니다. 지나가는 항구에서는 묻지 않습니다.",
        "자동항해 중 배가 육지에 걸리면 그 자리에서 길을 다시 찾습니다.",
        "한 번 찾은 항로는 저장해 둡니다. 같은 도시로 다시 갈 때는 금방 뜹니다.",
        "지도에서 Shift 를 누른 채 오른쪽 단추를 누르면 그 자리로 자동항해합니다.",
        "네비게이션 단축키는 햄버거 메뉴의 「단축키」에서 바꿀 수 있습니다.",
        "출항할 때 보급이 20일에 못 미치면 네비게이션으로 떠나도 한 번 물어봅니다.",
        "향상된 그래픽은 M 키로 모드 창을 열고 「고해상도」 탭에서 켜고 끕니다. 체크를 풀면 원본 화면 그대로입니다.",
        "「고해상도」 탭의 「바다 입체 효과」는 바다에 물결 굴곡과 햇빛 반짝임, 해안 물보라를 얹습니다. 밝기는 아래 막대로 고릅니다.",
        "「고해상도」 탭의 「고해상도 바다」는 지도를 키웠을 때 바다를 화면 해상도로 새로 그리고 해안선을 곡선으로 다듬습니다.",
        "「고해상도 바다」 밑의 「해류 결」 막대로 해류가 흐르는 쪽의 물결을 짙게 또는 옅게 고릅니다.",
        "「고해상도」 탭의 「뭍 세부 질감」은 사막 · 산 · 숲 · 평지에 잔무늬를 얹습니다. 원본 도트는 그대로 둡니다.",
        "「고해상도」 탭에는 「도트 확대 필터」 · 「배 항적」 · 「부드러운 구름」도 있습니다. 하나씩 켜 보고 마음에 드는 것만 남기십시오.",
        "그래픽 옵션은 켜는 즉시 지도에 반영됩니다. 화면이 느려지면 「바다 입체 효과」부터 꺼 보십시오.",
        "원본 그대로의 화면이 좋다면 모드 창 「고해상도」 탭의 체크를 모두 풀면 됩니다.",
        "도시에 들어가고 나올 때 날짜가 너무 많이 지나간다면, M 키로 모드 창을 열고 「편의성」 탭의 「출입 일수」를 줄여 보십시오.",
        "「출입 일수」는 항구 · 마을에 들고 날 때 각각 지나는 날수입니다. 원본은 열흘씩이고, 바꾼 값은 다음 출입부터 듭니다.",
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
