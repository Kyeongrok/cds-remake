using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using CdsHelper.Game.Local.Settings;

namespace CdsHelper.Game.UI.Views;

/// <summary>
/// <b>녹화용 거울</b> — 딸린 창(도시 화면)을 지도 창 <b>안에</b> 그대로 비춰 그린다(모드 실험 「녹화용 창 합치기」).
/// </summary>
/// <remarks>
/// 녹화 앱의 「창 지정」은 창 하나만 찍는다. 도시 화면은 지도 창에 딸린 <b>딴 창</b>이라 빠진다. 창을 지도 창 안으로
/// 옮기자면 그 창을 주인으로 삼는 상자 수십 개를 다 고쳐야 하므로, 창은 그대로 두고 <b>같은 자리에 비춘 그림</b>을
/// 지도 창의 자식 창으로 얹는다 — 진짜 창이 그 위를 딱 덮고 있어 눈에는 달라지는 것이 없고, 녹화에만 든다.
///
/// 자식 창(HWND)이어야 한다 — 지도는 D3D 자식 창이라 WPF 그림은 그 밑에 깔린다(airspace).
/// 자식 창은 반투명을 못 하니 <b>비치는 창</b>(쪽지 따위)은 안 비춘다. 손은 안 받는다(WS_DISABLED) — 진짜 창이 받는다.
/// </remarks>
internal sealed class CaptureMirror
{
    private readonly Window _main;
    private readonly Canvas _layer;
    private readonly Dictionary<Window, Host> _hosts = [];
    private readonly DispatcherTimer _timer;

    public CaptureMirror(Window main, Canvas layer)
    {
        _main = main;
        _layer = layer;
        _timer = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(33) };
        _timer.Tick += (_, _) => Sync();
        _timer.Start();
    }

    /// <summary>비출 창인지 — 지금은 도시 화면만이다. 비치는 바탕의 창은 자식 창으로 못 비춘다.</summary>
    private static bool Mirrorable(Window w) => w is CityPicView && w.Content is Visual;

    private static IEnumerable<Window> Collect(Window owner)
    {
        foreach (Window w in owner.OwnedWindows)
        {
            if (!w.IsVisible) continue;
            if (Mirrorable(w)) yield return w;
            foreach (var sub in Collect(w)) yield return sub;
        }
    }

    private void Sync()
    {
        bool on = GameSettings.CaptureMirror && _main.IsVisible && _main.WindowState != WindowState.Minimized
                  && PresentationSource.FromVisual(_layer) != null;
        var live = on ? Collect(_main).ToList() : [];

        foreach (var gone in _hosts.Keys.Where(w => !live.Contains(w)).ToList())
        {
            _layer.Children.Remove(_hosts[gone]);
            _hosts[gone].Dispose();
            _hosts.Remove(gone);
        }

        foreach (var w in live)
        {
            if (PresentationSource.FromVisual(w) == null || w.Content is not Visual body) continue;
            if (!_hosts.TryGetValue(w, out var host))
            {
                host = new Host(body);
                _hosts[w] = host;
                _layer.Children.Add(host);
                // 지도(D3D 자식 창) 위로 올린다 — 새 자식 창이 늘 맨 위에 서지는 않아, 안 올리면 지도에 가려 녹화에 안 든다.
                _layer.UpdateLayout();
                if (host.Handle != IntPtr.Zero) SetWindowPos(host.Handle, IntPtr.Zero, 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0010);
            }

            // 진짜 창의 알맹이 자리(화면) → 지도 창 속 자리. 화면 밖으로 밀어 둔 창(들어오는 효과)은 그대로 밖에 선다.
            var at = _layer.PointFromScreen(w.PointToScreen(new Point(0, 0)));
            double width = Math.Max(1, w.ActualWidth), height = Math.Max(1, w.ActualHeight);
            if (host.Width != width) host.Width = width;
            if (host.Height != height) host.Height = height;
            if (Canvas.GetLeft(host) != at.X) Canvas.SetLeft(host, at.X);
            if (Canvas.GetTop(host) != at.Y) Canvas.SetTop(host, at.Y);
        }
    }

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int cx, int cy, uint flags);

    /// <summary>비춘 그림 하나 — 지도 창의 자식 창에 WPF 그림을 건다.</summary>
    private sealed class Host(Visual body) : HwndHost
    {
        private const int WsChild = 0x40000000, WsVisible = 0x10000000, WsDisabled = 0x08000000, WsClipSiblings = 0x04000000;

        private HwndSource? _source;

        protected override HandleRef BuildWindowCore(HandleRef hwndParent)
        {
            var root = new Border
            {
                Background = Brushes.Black,
                Child = new Rectangle { Fill = new VisualBrush(body) { Stretch = Stretch.Fill } },
            };
            _source = new HwndSource(new HwndSourceParameters("CaptureMirror")
            {
                ParentWindow = hwndParent.Handle,
                WindowStyle = WsChild | WsVisible | WsDisabled | WsClipSiblings,
                Width = 1,
                Height = 1,
            })
            { RootVisual = root };
            return new HandleRef(this, _source.Handle);
        }

        protected override void DestroyWindowCore(HandleRef hwnd)
        {
            _source?.Dispose();
            _source = null;
        }
    }
}
