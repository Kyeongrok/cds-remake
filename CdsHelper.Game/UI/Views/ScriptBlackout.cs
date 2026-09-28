using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace CdsHelper.Game.UI.Views;

/// <summary>
/// 이벤트 대본의 <b>암전</b>(<c>48</c> · <c>49</c>)과 <b>기다리기</b>(<c>29 1A</c>).
/// </summary>
/// <remarks>
/// <code>
///   0040b1d5  48  0x004BA340(10, 0xEC, 둔 곳)            ; 팔레트 10~245 를 떠 두고
///                 0x004B7E5D(검정, 0x2C4)                 ; 236색 x 3 = 0x2C4 바이트를 0 으로
///                 0x004BA213(10, 0xEC, 검정, 1, 1)         ; 한 걸음에 얹는다 — 화면이 곧장 깜깜해진다
///   0040b220  49  0x004BA213(10, 0xEC, 둔 곳, 1, 1)        ; 떠 둔 팔레트를 도로 얹는다
///   0040a2c6  29 1A [u32 n]  0x00428000(n x 20, 1)         ; 한 참이 1/50초(0x004BA4BB) — n 초를 쉰다
/// </code>
/// 기다리는 동안 누르거나 키를 치면 곧장 끝난다(<c>0x00428027</c>·<c>0x00428035</c>, 둘째 인자 1).
/// 대본은 늘 <c>48 · 29 1A 01 · 49</c> 로 써서 「일행은 유적 안에 발을 들여놓았다」 뒤에 한 초 깜깜해진다.
/// 팔레트 대신 게임 창 위에 검은 창을 씌운다(<see cref="DayPass"/> 와 같은 꼴).
/// </remarks>
internal static class ScriptBlackout
{
    /// <summary>화면을 곧장 검게 덮는다(48). 걷을 때는 돌려준 창을 닫는다(49).</summary>
    public static Window Cover(Window view)
    {
        var root = GameUi.RootOf(view);
        var shade = new Window
        {
            Owner = view,
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
        };
        shade.Show();
        return shade;
    }

    /// <summary>
    /// 그만큼 쉰다(29 1A). 화면은 그리게 두고, 누르거나 키를 치면 곧장 끝낸다(<c>0x00428000(…, 1)</c>).
    /// </summary>
    /// <param name="view">대본을 띄운 창 — 누름·키를 여기서 받는다.</param>
    /// <param name="shade">덮어 둔 검은 창. 없으면 null — 있으면 그 위의 누름도 받는다.</param>
    /// <param name="ms">쉴 밀리초.</param>
    public static void Hold(Window view, Window? shade, int ms)
    {
        if (ms <= 0) return;
        var root = GameUi.RootOf(view);
        var frame = new DispatcherFrame();
        void Skip(object? s, EventArgs e) => frame.Continue = false;

        var clock = new DispatcherTimer(TimeSpan.FromMilliseconds(ms), DispatcherPriority.Normal,
                                        (_, _) => frame.Continue = false, Dispatcher.CurrentDispatcher);
        var watched = new[] { root, view, shade }.OfType<Window>().Distinct().ToArray();
        foreach (var w in watched)
        {
            w.PreviewMouseDown += Skip;
            w.PreviewKeyDown += Skip;
        }
        clock.Start();
        try
        {
            Dispatcher.PushFrame(frame);
        }
        finally
        {
            clock.Stop();
            foreach (var w in watched)
            {
                w.PreviewMouseDown -= Skip;
                w.PreviewKeyDown -= Skip;
            }
        }
    }
}
