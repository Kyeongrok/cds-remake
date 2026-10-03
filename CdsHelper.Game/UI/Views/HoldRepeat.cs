using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;

namespace CdsHelper.Game.UI.Views;

/// <summary>
/// <b>누르고 있으면 되풀이하는</b> 단추 — 보급 창 ↑·↓ 처럼 수를 올리고 내리는 화살표에 건다.
/// </summary>
/// <remarks>
/// 누르는 순간 한 번 돌고, <see cref="FirstDelay"/> 동안 계속 누르고 있으면 그 뒤로 <see cref="Interval"/> 마다 돈다.
/// 떼거나 마우스를 놓치면 멈춘다. 일이 <c>false</c> 를 내면(더 못 움직이면) 거기서 멈춘다 — 막는 말이 되풀이해 뜨지 않게.
/// 예전에는 뗄 때 한 번만 돌아, 누르고 있어도 수가 안 움직였다.
/// </remarks>
internal static class HoldRepeat
{
    /// <summary>되풀이를 시작하기 전 기다림.</summary>
    private static readonly TimeSpan FirstDelay = TimeSpan.FromMilliseconds(400);

    /// <summary>되풀이 사이.</summary>
    private static readonly TimeSpan Interval = TimeSpan.FromMilliseconds(70);

    /// <summary>
    /// 그 칸에 건다. <paramref name="run"/> 은 첫 번이면 <c>true</c> 를 받는다(막는 말은 첫 번에만 내라고) —
    /// 더 움직일 수 없으면 <c>false</c> 를 낸다.
    /// </summary>
    public static void Attach(FrameworkElement box, Func<bool, bool> run)
    {
        var timer = new DispatcherTimer { Interval = FirstDelay };
        timer.Tick += (_, _) =>
        {
            timer.Interval = Interval;
            if (!run(false)) Stop();
        };

        void Stop()
        {
            timer.Stop();
            if (box.IsMouseCaptured) box.ReleaseMouseCapture();
        }

        box.MouseLeftButtonDown += (_, e) =>
        {
            // 누름은 삼킨다 — 판 끌기가 먼저 걸리면 마우스를 잡아 버린다.
            e.Handled = true;
            box.CaptureMouse();
            if (!run(true)) { Stop(); return; }
            timer.Interval = FirstDelay;
            timer.Start();
        };
        box.MouseLeftButtonUp += (_, e) => { e.Handled = true; Stop(); };
        box.LostMouseCapture += (_, _) => timer.Stop();
        box.Unloaded += (_, _) => timer.Stop();
    }
}
