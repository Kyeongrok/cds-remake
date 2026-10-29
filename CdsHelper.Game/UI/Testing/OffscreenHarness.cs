using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using CdsHelper.Game.Local.Settings;
using CdsHelper.Game.UI.Views;
using CdsHelper.Support.Local.Helpers;

namespace CdsHelper.Game.UI.Testing;

/// <summary>
/// <b>화면 밖 시험 장치</b> — 게임 창을 사람 화면 밖에서 띄우고, 바로 바다로 들이고, 화면을 PNG 로 떠 준다.
/// </summary>
/// <remarks>
/// 스위치는 모두 환경 변수다(<see cref="OffscreenTest"/>). 하나도 없으면 <see cref="Prepare"/> · <see cref="Attach"/> 가
/// 아무것도 안 한다 — 게임을 그냥 켜면 예전과 같다.
/// <code>
///   창 자리        CDS_OFFSCREEN=1 이면 (-8000, -8000) 에 포커스 · 작업 표시줄 단추 없이. 이 실(UI 스레드)의 창은
///                  아예 활성이 안 된다(CBT 훅) — 대화 창이 Activate() 로 사람의 포커스를 뺏지 않고, 커서도 배를 안 몬다.
///   스냅샷         창에 PostMessage(0x8001) → OffscreenTest.SnapshotPath(%TEMP% 또는 CDS_SAVEDIR 의 cds_snapshot.png).
///                  WPF 화면(RenderTargetBitmap) 위에 지도 칸(D3D 스왑체인 백버퍼)과 딸린 창(도시 화면 · 대화 창)을 겹쳐 그린다.
///   바로 시작      CDS_START=sea:&lt;도시&gt;:&lt;선체 key&gt; (+ CDS_HEADING · CDS_SAIL) — WorldMapWindow.StartAtSeaForTest.
///   찍고 끄기      CDS_SHOT=&lt;png&gt;:&lt;초&gt;[;&lt;png&gt;:&lt;초&gt;…] — 창이 뜬 지 그 초에 찍고, 마지막 것을 찍으면 창을 닫는다.
/// </code>
/// 무엇을 했는지는 <c>OffscreenTest.OutputFolder\cds_test.log</c> 에 한 줄씩 적는다. 창 핸들은 <see cref="OffscreenTest.HwndFile"/>.
///
/// 못 찍는 것: WPF <c>Popup</c>(좌표 상자 · 미니맵 · 비 · 눈 — 어차피 창이 활성일 때만 뜬다)과 딴 실의 창(불러오는 쪽지).
/// </remarks>
public static class OffscreenHarness
{
    /// <summary>화면 밖 창 자리(DIP). duel-dx 와 같은 값이다.</summary>
    public const double OffscreenLeft = -8000, OffscreenTop = -8000;

    /// <summary>스냅샷 때 지도 프레임을 기다리는 끝 — 지도가 안 돌면(타이틀) 이만큼 뒤 WPF 화면만 뜬다.</summary>
    private static readonly TimeSpan GrabWait = TimeSpan.FromSeconds(1.5);

    /// <summary>
    /// 창을 띄우기 <b>전에</b> 부른다 — 화면 밖 자리 · 포커스 안 뺏기 · 작업 표시줄 단추 없음을 건다.
    /// <c>CDS_OFFSCREEN</c> 이 없으면 아무것도 안 한다.
    /// </summary>
    public static void Prepare(Window window)
    {
        if (!OffscreenTest.Enabled) return;
        window.WindowStartupLocation = WindowStartupLocation.Manual;
        window.Left = OffscreenLeft;
        window.Top = OffscreenTop;
        window.ShowActivated = false;
        window.ShowInTaskbar = false;
        BlockActivation();
    }

    /// <summary>
    /// 게임 창에 시험 장치를 붙인다 — 스냅샷 메시지, 창 핸들 파일, 바로 시작, 찍고 끄기. 창을 띄우기 전에 부른다.
    /// 훅이 하나도 없으면(<see cref="OffscreenTest.Any"/>) 아무것도 안 한다.
    /// </summary>
    public static void Attach(WorldMapWindow window)
    {
        if (!OffscreenTest.Any) return;

        window.SourceInitialized += (_, _) =>
        {
            var handle = new WindowInteropHelper(window).Handle;
            HwndSource.FromHwnd(handle)?.AddHook((IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled) =>
            {
                if (msg != OffscreenTest.SnapshotMessage) return IntPtr.Zero;
                handled = true;
                Save(window, OffscreenTest.SnapshotPath, null);
                return IntPtr.Zero;
            });
            try
            {
                Directory.CreateDirectory(OffscreenTest.OutputFolder);
                File.WriteAllText(OffscreenTest.HwndFile, handle.ToInt64().ToString());
            }
            catch (Exception ex) { Log($"창 핸들을 적지 못했다 — {ex.Message}"); }
            Log($"창 {handle.ToInt64()} · 화면 밖 {OffscreenTest.Enabled} · 소리 끔 {OffscreenTest.Muted} · 자료 {UserDataFolder.Roaming}");
        };

        // 창의 OnLoaded(게임 폴더 · 그림 · 타이틀)가 다 돈 뒤에 — 그 처리기가 먼저 걸려 있다.
        window.Loaded += (_, _) => window.Dispatcher.BeginInvoke(() =>
        {
            if (OffscreenTest.Start is { } start) RunStart(window, start);
            ScheduleShots(window);
        }, DispatcherPriority.ApplicationIdle);
    }

    // ── 바로 시작 ───────────────────────────────────────────────────────────

    /// <summary><c>sea:&lt;도시&gt;:&lt;선체 key&gt;</c> 를 풀어 바다로 든다.</summary>
    private static void RunStart(WorldMapWindow window, string start)
    {
        var parts = start.Split(':');
        if (parts.Length != 3 || parts[0] != "sea" || !int.TryParse(parts[1], out int city))
        {
            Log($"CDS_START 를 못 읽었다 — [{start}] (꼴: sea:<도시 번호>:<선체 key>)");
            return;
        }
        // 모드 「추가 선박」을 켜 둔다 — 시험 폴더(CDS_SAVEDIR)일 때만. 사람 설정은 안 건드린다(선체는 꺼져 있어도 찾힌다).
        if (OffscreenTest.SaveDir != null && !GameSettings.ExtraShips) GameSettings.ExtraShips = true;

        int heading = (OffscreenTest.Heading ?? 0) * 2;   // 8방위 → 16방위
        string error;
        try { error = window.StartAtSeaForTest(city, parts[2], heading, OffscreenTest.Sail); }
        catch (Exception ex) { error = $"{ex.GetType().Name}: {ex.Message}"; }
        Log(error.Length == 0
            ? $"바다로 들었다 — 도시 {city} · 선체 {parts[2]} · 뱃머리 {ShipRegistry.DirectionNames[heading / 2]} · 항해 {OffscreenTest.Sail}"
            : $"바로 시작 실패 — {error}");
    }

    // ── 찍고 끄기 ───────────────────────────────────────────────────────────

    /// <summary><c>CDS_SHOT</c> 의 차례대로 찍고, 마지막 것을 찍으면 창을 닫는다.</summary>
    private static void ScheduleShots(WorldMapWindow window)
    {
        if (OffscreenTest.Shot is not { } spec) return;
        var shots = new List<(string Path, double Seconds)>();
        foreach (string item in spec.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            // 경로에 드라이브 콜론(C:)이 있으니 마지막 콜론으로 가른다.
            int at = item.LastIndexOf(':');
            if (at <= 1 || !double.TryParse(item[(at + 1)..], System.Globalization.CultureInfo.InvariantCulture, out double seconds))
            {
                Log($"CDS_SHOT 을 못 읽었다 — [{item}] (꼴: <png 경로>:<초>)");
                continue;
            }
            shots.Add((item[..at], seconds));
        }
        shots.Sort((a, b) => a.Seconds.CompareTo(b.Seconds));
        if (shots.Count == 0) return;

        var clock = System.Diagnostics.Stopwatch.StartNew();
        int next = 0;
        void Arm()
        {
            var (path, seconds) = shots[next];
            var wait = TimeSpan.FromSeconds(Math.Max(0, seconds - clock.Elapsed.TotalSeconds));
            var timer = new DispatcherTimer(DispatcherPriority.Normal, window.Dispatcher) { Interval = wait };
            timer.Tick += (_, _) =>
            {
                timer.Stop();
                Save(window, path, () =>
                {
                    if (++next < shots.Count) { Arm(); return; }
                    Log("CDS_SHOT 을 다 찍었다 — 창을 닫는다");
                    window.Close();
                });
            };
            timer.Start();
        }
        Arm();
    }

    // ── 스냅샷 ─────────────────────────────────────────────────────────────

    /// <summary>
    /// 지금 화면을 PNG 로 적는다. 지도가 돌고 있으면 다음 프레임의 백버퍼를 받아 겹친다.
    /// 다 적으면 <paramref name="done"/>.
    /// </summary>
    public static void Save(WorldMapWindow window, string path, Action? done)
    {
        var host = window.MapHost;
        bool finished = false;
        void Finish(byte[]? map, int w, int h)
        {
            if (finished) return;
            finished = true;
            try
            {
                Compose(window, map, w, h, path);
                Log($"스냅샷 {path}{(map is { Length: > 0 } ? $" (지도 {w}x{h})" : " (지도 없음)")}");
            }
            catch (Exception ex) { Log($"스냅샷 실패 {path} — {ex.GetType().Name}: {ex.Message}"); }
            done?.Invoke();
        }

        if (!host.IsVisible || host.FrameCount == 0 || host.ActualWidth <= 0)
        {
            Finish(null, 0, 0);
            return;
        }
        var timeout = new DispatcherTimer(DispatcherPriority.Normal, window.Dispatcher) { Interval = GrabWait };
        timeout.Tick += (_, _) => { timeout.Stop(); Finish(null, 0, 0); };
        timeout.Start();
        // 프레임 안(CompositionTarget.Rendering)에서 불린다 — 그리는 일은 한 박자 뒤로 미룬다.
        host.GrabNextFrame((pixels, w, h) => window.Dispatcher.BeginInvoke(() =>
        {
            timeout.Stop();
            Finish(pixels, w, h);
        }));
    }

    /// <summary>창 그림 + 지도 백버퍼 + 딸린 창을 한 장에 겹쳐 적는다(임시 파일에 적고 옮겨 반쯤 적힌 파일이 안 보이게).</summary>
    private static void Compose(WorldMapWindow window, byte[]? map, int mapW, int mapH, string path)
    {
        var dpi = VisualTreeHelper.GetDpi(window);
        double width = window.ActualWidth, height = window.ActualHeight;
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle(window.Background ?? Brushes.Black, null, new Rect(0, 0, width, height));
            dc.DrawImage(Render(window), new Rect(0, 0, width, height));

            // 지도는 자식 창(D3D)이라 WPF 그림에는 비어 나온다 — 그 자리에 백버퍼를 얹는다. 실제 화면도 지도가 WPF 위에 뜬다.
            if (map is { Length: > 0 } && mapW > 0 && mapH > 0 && window.MapHost.IsVisible)
            {
                var image = BitmapSource.Create(mapW, mapH, 96 * dpi.DpiScaleX, 96 * dpi.DpiScaleY,
                                                PixelFormats.Bgr32, null, map, mapW * 4);
                var at = window.MapHost.TransformToAncestor(window).Transform(new Point(0, 0));
                dc.DrawImage(image, new Rect(at.X, at.Y, mapW / dpi.DpiScaleX, mapH / dpi.DpiScaleY));
            }

            // 딸린 창(도시 화면 · 대화 창 · 쪽지)은 제 자리에 — 주인 창에 견준 자리로 놓는다.
            foreach (var owned in Owned(window))
            {
                if (owned.ActualWidth <= 0 || owned.ActualHeight <= 0) continue;
                dc.DrawImage(Render(owned), new Rect(owned.Left - window.Left, owned.Top - window.Top,
                                                     owned.ActualWidth, owned.ActualHeight));
            }
        }

        var shot = new RenderTargetBitmap((int)Math.Ceiling(width * dpi.DpiScaleX), (int)Math.Ceiling(height * dpi.DpiScaleY),
                                          96 * dpi.DpiScaleX, 96 * dpi.DpiScaleY, PixelFormats.Pbgra32);
        shot.Render(visual);

        string full = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        string temp = full + ".part";
        using (var stream = File.Create(temp))
        {
            var png = new PngBitmapEncoder();
            png.Frames.Add(BitmapFrame.Create(shot));
            png.Save(stream);
        }
        File.Move(temp, full, overwrite: true);
    }

    /// <summary>창 하나를 실픽셀로 그린다.</summary>
    private static BitmapSource Render(Window window)
    {
        var dpi = VisualTreeHelper.GetDpi(window);
        var bitmap = new RenderTargetBitmap(Math.Max(1, (int)Math.Ceiling(window.ActualWidth * dpi.DpiScaleX)),
                                            Math.Max(1, (int)Math.Ceiling(window.ActualHeight * dpi.DpiScaleY)),
                                            96 * dpi.DpiScaleX, 96 * dpi.DpiScaleY, PixelFormats.Pbgra32);
        bitmap.Render(window);
        return bitmap;
    }

    /// <summary>보이는 딸린 창 — 주인부터 깊이 딸린 것 차례(위에 뜨는 것이 나중).</summary>
    private static IEnumerable<Window> Owned(Window owner)
    {
        foreach (Window w in owner.OwnedWindows)
        {
            if (!w.IsVisible) continue;
            yield return w;
            foreach (var sub in Owned(w)) yield return sub;
        }
    }

    // ── 포커스 안 뺏기 ─────────────────────────────────────────────────────

    private delegate IntPtr HookProc(int code, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern IntPtr SetWindowsHookExW(int idHook, HookProc proc, IntPtr module, uint threadId);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    private const int WhCbt = 5, HcbtActivate = 5;

    private static HookProc? _cbt;     // 대리자가 GC 에 걷히지 않게 들고 있는다
    private static IntPtr _cbtHook;

    /// <summary>
    /// 이 실(UI 스레드)의 창이 <b>활성이 되지 않게</b> 한다 — CBT 훅이 <c>HCBT_ACTIVATE</c> 를 막는다. 대화 창 · 도시 화면이
    /// <c>Activate()</c> 를 불러도 사람이 쓰던 창의 포커스가 그대로다. 창이 활성이 아니니 게임도 커서로 배를 몰지 않는다(SyncMouse).
    /// </summary>
    private static void BlockActivation()
    {
        if (_cbtHook != IntPtr.Zero) return;
        _cbt = (code, wParam, lParam) => code == HcbtActivate ? 1 : CallNextHookEx(_cbtHook, code, wParam, lParam);
        _cbtHook = SetWindowsHookExW(WhCbt, _cbt, IntPtr.Zero, GetCurrentThreadId());
        if (_cbtHook == IntPtr.Zero) Log($"활성 막기 훅을 못 걸었다 (Win32 {Marshal.GetLastWin32Error()})");
    }

    // ── 기록 ──────────────────────────────────────────────────────────────

    /// <summary>시험 기록 한 줄 — <c>OutputFolder\cds_test.log</c>.</summary>
    public static void Log(string line)
    {
        try
        {
            Directory.CreateDirectory(OffscreenTest.OutputFolder);
            File.AppendAllText(Path.Combine(OffscreenTest.OutputFolder, "cds_test.log"),
                               $"{DateTime.Now:HH:mm:ss.fff} [{Environment.ProcessId}] {line}{Environment.NewLine}");
        }
        catch { /* 기록을 못 남겨도 시험은 돈다 */ }
    }
}
