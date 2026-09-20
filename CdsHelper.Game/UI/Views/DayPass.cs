using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;

namespace CdsHelper.Game.UI.Views;

/// <summary>
/// 여러 날을 한꺼번에 보낼 때의 <b>암전 연출</b>(<c>0x004A59F0</c> → <c>0x004A5AE0</c> → <c>0x004A5AA0</c>).
/// </summary>
/// <remarks>
/// 게임은 수련·교육·허드렛일처럼 날을 통째로 넘기는 자리마다 이 셋을 부른다 — 화면을 덮고, 날을 보내고,
/// 다시 밝힌다. 우리는 게임 창 위에 검은 창을 씌웠다 걷는 것으로 흉내낸다.
/// </remarks>
internal static class DayPass
{
    /// <summary>덮고 밝히는 걸음 수와 한 걸음의 밀리초, 다 덮은 채로 머무는 밀리초.</summary>
    private const int Steps = 5, StepMs = 40, HoldMs = 400;

    /// <summary>화면을 덮은 채로 <paramref name="during"/> 을 하고 도로 밝힌다.</summary>
    public static void Blackout(Window view, Action during)
    {
        var root = GameUi.RootOf(view);
        var shade = new Window
        {
            Owner = root,
            WindowStyle = WindowStyle.None,
            ResizeMode = ResizeMode.NoResize,
            AllowsTransparency = true,
            Background = Brushes.Black,
            ShowInTaskbar = false,
            ShowActivated = false,
            WindowStartupLocation = WindowStartupLocation.Manual,
            Left = root.Left,
            Top = root.Top,
            Width = root.ActualWidth > 0 ? root.ActualWidth : root.Width,
            Height = root.ActualHeight > 0 ? root.ActualHeight : root.Height,
            Opacity = 0,
        };
        shade.Show();
        for (int i = 1; i <= Steps; i++) { shade.Opacity = (double)i / Steps; Wait(StepMs); }
        during();
        Wait(HoldMs);
        for (int i = Steps - 1; i >= 0; i--) { shade.Opacity = (double)i / Steps; Wait(StepMs); }
        shade.Close();
    }

    /// <summary>화면은 그리게 두고 그만큼 쉰다.</summary>
    private static void Wait(int ms)
    {
        var frame = new DispatcherFrame();
        var clock = new DispatcherTimer(TimeSpan.FromMilliseconds(ms), DispatcherPriority.Render,
                                        (_, _) => frame.Continue = false, Dispatcher.CurrentDispatcher);
        clock.Start();
        Dispatcher.PushFrame(frame);
        clock.Stop();
    }
}
