using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;

namespace CdsHelper.Game.UI.Views;

/// <summary>
/// <b>누르고 있으면 되풀이하는</b> 단추 — 보급 창 ↑·↓ 처럼 수를 올리고 내리는 화살표에 건다.
/// </summary>
/// <remarks>
/// 누르는 순간 한 번 돌고, <see cref="FirstDelay"/> 동안 계속 누르고 있으면 그 뒤로 <see cref="Interval"/> 마다 돈다.
/// <see cref="FastAfter"/> 번 넘게 돌면 <see cref="FastInterval"/> 로 빨라진다 — 큰 수를 옮길 때 덜 기다리게.
/// 떼면 멈춘다. 일이 <c>false</c> 를 내면(더 못 움직이면) 거기서 멈춘다 — 막는 말이 되풀이해 뜨지 않게.
/// 예전에는 뗄 때 한 번만 돌아, 누르고 있어도 수가 안 움직였다.
///
/// 뗐는지는 단추의 MouseUp 이 아니라 <b>마우스 단추 상태</b>로 본다 — 전리품 창처럼 한 번 돌 때마다 판을
/// 통째로 다시 짓는 창은 누른 단추가 바로 걷혀 MouseUp 이 그 단추로 오지 않는다. 창이 닫히면 멈춘다.
/// </remarks>
internal static class HoldRepeat
{
    /// <summary>되풀이를 시작하기 전 기다림.</summary>
    private static readonly TimeSpan FirstDelay = TimeSpan.FromMilliseconds(400);

    /// <summary>되풀이 사이.</summary>
    private static readonly TimeSpan Interval = TimeSpan.FromMilliseconds(60);

    /// <summary>오래 누르고 있을 때의 되풀이 사이.</summary>
    private static readonly TimeSpan FastInterval = TimeSpan.FromMilliseconds(25);

    /// <summary>이만큼 돌고 나면 빨라진다(약 1초).</summary>
    private const int FastAfter = 16;

    /// <summary>
    /// 그 칸에 건다. <paramref name="run"/> 은 첫 번이면 <c>true</c> 를 받는다(막는 말은 첫 번에만 내라고) —
    /// 더 움직일 수 없으면 <c>false</c> 를 낸다.
    /// </summary>
    public static void Attach(FrameworkElement box, Func<bool, bool> run)
    {
        box.MouseLeftButtonDown += (_, e) =>
        {
            // 누름은 삼킨다 — 판 끌기가 먼저 걸리면 마우스를 잡아 버린다.
            e.Handled = true;
            box.CaptureMouse();
            if (!run(true)) { Release(box); return; }

            var window = Window.GetWindow(box);
            int count = 0;
            var timer = new DispatcherTimer { Interval = FirstDelay };
            timer.Tick += (_, _) =>
            {
                if (Mouse.LeftButton != MouseButtonState.Pressed || window is { IsVisible: false })
                {
                    timer.Stop();
                    Release(box);
                    return;
                }
                timer.Interval = ++count > FastAfter ? FastInterval : Interval;
                if (!run(false)) { timer.Stop(); Release(box); }
            };
            timer.Start();
        };
        box.MouseLeftButtonUp += (_, e) => { e.Handled = true; Release(box); };
    }

    private static void Release(FrameworkElement box)
    {
        if (box.IsMouseCaptured) box.ReleaseMouseCapture();
    }
}
