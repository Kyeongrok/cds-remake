using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Threading;

namespace CdsHelper.Game.Local.Helpers;

/// <summary>
/// 창이 닫힐 때 <b>초점이 어디로 갔는지</b> 찍어 보는 진단용 도구.
/// </summary>
/// <remarks>
/// 시설 명령 창을 닫으면 초점이 우리 앱을 떠나 직전에 쓰던 앱으로 가는 일이 있다. 창 구조만
/// 흉내낸 앱에서는 재현이 안 돼서, 진짜 앱 안에서 어느 순간에 새는지 보려고 둔다.
///
/// 초점은 창을 부순 <b>다음에</b> 정해지므로 그 자리에서 읽으면 아직 옛 값이다. 그래서
/// 조금 있다가(<see cref="SettleMs"/>) 한 번 읽는다.
///
/// 놀이에는 쓰이지 않는다 — 다 잡고 나면 지운다.
/// </remarks>
internal static class FocusWatch
{
    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowTextW(IntPtr hwnd, StringBuilder text, int count);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassNameW(IntPtr hwnd, StringBuilder text, int count);

    [DllImport("user32.dll")]
    private static extern int GetWindowThreadProcessId(IntPtr hwnd, out int pid);

    /// <summary>창을 부순 뒤 초점이 자리잡기를 기다리는 시간.</summary>
    private const int SettleMs = 200;

    /// <summary>찍은 줄을 받아 갈 곳. 상태줄이 여기에 붙는다.</summary>
    public static Action<string>? Sink { get; set; }

    /// <summary>켜져 있는지. <see cref="Sink"/> 를 달아 주면 켜진다.</summary>
    public static bool On => Sink != null;

    /// <summary>
    /// 그 일이 있고 조금 뒤에 초점이 어디 있는지 한 번 찍는다.
    /// </summary>
    /// <param name="what">무슨 일이었는지("조선소 명령창 닫힘" 따위).</param>
    public static void After(string what)
    {
        if (Sink == null) return;

        var timer = new DispatcherTimer(DispatcherPriority.ApplicationIdle)
        {
            Interval = TimeSpan.FromMilliseconds(SettleMs),
        };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            Sink?.Invoke($"[초점] {what} → {Foreground()}   ·   {Ours()}");
        };
        timer.Start();
    }

    /// <summary>전경 창이 우리 것인지.</summary>
    public static bool ForegroundIsOurs()
    {
        var hwnd = GetForegroundWindow();
        if (hwnd == IntPtr.Zero) return false;
        GetWindowThreadProcessId(hwnd, out int pid);
        return pid == Environment.ProcessId;
    }

    /// <summary>
    /// 이 창을 닫을 때 <b>초점이 앱 밖으로 새지 않게</b> 붙든다.
    /// </summary>
    /// <remarks>
    /// 창을 부수면 보통 Windows 가 그 <b>주인 창</b>을 대신 띄운다. 그런데 시설 명령 창을
    /// 닫으면 초점이 직전에 쓰던 <b>남의 앱</b>으로 가는 일이 있다. 창 구조만 똑같이 흉내낸
    /// 앱에서는 (다섯 겹 소유 사슬 · 진짜 클릭 · 창 끌기 · 자식 HWND 를 다 넣어도) 재현이
    /// 안 돼서 까닭을 아직 못 짚었다.
    ///
    /// 그래서 <b>까닭을 고치는 것이 아니라 결과를 못 박는다</b> — 닫기 직전에 우리 앱이
    /// 앞에 있었다면, 닫은 뒤에도 우리 앱이 앞에 있어야 한다. 아니면 주인 창을 도로 띄운다.
    ///
    /// <b>닫기 전에 이미 남의 앱으로 가 있었으면 손대지 않는다</b> — 사람이 알트탭으로
    /// 옮겨 간 뒤에 창이 닫히는 경우까지 빼앗아 오면 그쪽이 더 나쁘다.
    /// </remarks>
    public static void KeepInApp(Window window)
    {
        Window? owner = null;
        bool wasOurs = false;

        window.Closing += (_, _) =>
        {
            owner = window.Owner;          // 닫히고 나면 Owner 가 비어 있을 수 있다
            wasOurs = ForegroundIsOurs();
        };

        window.Closed += (_, _) =>
        {
            if (!wasOurs || owner is not { } back) return;

            // 초점은 창을 부순 <b>다음에</b> 정해지고, 그것도 곧바로가 아니다. 두 번 본다 —
            // 한 번은 바로 뒤, 한 번은 좀 있다가(늦게 새는 것까지 잡으려고).
            Check(window, back, CheckMs);
            Check(window, back, CheckAgainMs);
        };
    }

    /// <summary>창을 부순 뒤 초점을 되짚어 보는 때(ms).</summary>
    private const int CheckMs = 120, CheckAgainMs = 450;

    /// <summary>그만큼 있다가 초점이 앱 밖에 있으면 주인 창을 도로 띄운다.</summary>
    private static void Check(Window window, Window back, int afterMs)
    {
        var timer = new DispatcherTimer(DispatcherPriority.Input, window.Dispatcher)
        {
            Interval = TimeSpan.FromMilliseconds(afterMs),
        };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            if (ForegroundIsOurs() || !back.IsLoaded) return;
            back.Activate();
            Sink?.Invoke($"[초점] {afterMs}ms 뒤 앱 밖으로 새어 {back.GetType().Name} 로 도로 붙들었다");
        };
        timer.Start();
    }

    /// <summary>지금 전경 창이 무엇인지 — 프로세스 이름과 창 제목·클래스.</summary>
    private static string Foreground()
    {
        var hwnd = GetForegroundWindow();
        if (hwnd == IntPtr.Zero) return "전경 창 없음(!)";

        GetWindowThreadProcessId(hwnd, out int pid);
        string process;
        try { process = Process.GetProcessById(pid).ProcessName; }
        catch { process = $"pid {pid}"; }

        bool mine = pid == Environment.ProcessId;
        return $"{(mine ? "우리" : "★남의 앱★")} {process} \"{Text(GetWindowTextW, hwnd)}\" "
             + $"[{Text(GetClassNameW, hwnd)}]";
    }

    /// <summary>WPF 가 보기에 우리 창 가운데 어느 것이 살아 있고 어느 것이 활성인지.</summary>
    private static string Ours()
    {
        var live = new List<string>();
        foreach (Window w in Application.Current?.Windows ?? new WindowCollection())
        {
            string name = w.GetType().Name;
            if (w.IsActive) name = $"<{name}>";
            live.Add(name);
        }
        return live.Count == 0 ? "우리 창 없음" : string.Join(" ", live);
    }

    private delegate int TextOf(IntPtr hwnd, StringBuilder text, int count);

    private static string Text(TextOf get, IntPtr hwnd)
    {
        var sb = new StringBuilder(160);
        get(hwnd, sb, sb.Capacity);
        return sb.ToString();
    }
}
