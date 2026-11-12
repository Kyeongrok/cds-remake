using System.Runtime.InteropServices;
using System.Windows.Threading;
using CdsHelper.Game.Local.Settings;

namespace CdsHelper.Game.Local.Helpers;

/// <summary>
/// 볼륨 믹서에서 맞춘 이 앱의 음량 · 음소거를 기억해 두었다가 다시 켤 때, 그리고 새 소리 세션이 생길 때마다 되돌린다(버그 리포트 #51).
/// </summary>
/// <remarks>
/// Windows 는 앱 음량을 <b>세션 이름(exe 경로 + 세션 GUID)</b>으로 기억한다. 효과음 일부를 내는 <c>PlaySound</c>(winmm)는
/// 경로 + GUID_NULL 세션이라 기억되지만, 배경음악 · 겹쳐 나는 효과음을 내는 WPF MediaPlayer(wmp.dll)와 WinRT MediaPlayer 는
/// 세션 이름이 <c>#%b{매번 새 GUID}</c> 라 Windows 가 기억할 거리가 없어 다시 켜면 100% 로 선다.
/// WPF MediaPlayer 는 틀개를 하나 더 만들 때마다 새 세션을 열어, 게임 도중에도 새 틀개는 100% 로 났다.
/// 볼륨 믹서는 같은 프로세스 세션을 한 줄로 묶어 보여 주고 그 줄을 움직이면 모든 세션을 함께 바꾼다.
///
/// 그래서 여기서 (1) 1초마다 이 프로세스의 세션을 훑어, 지난번에 본 값과 달라진 세션이 있으면 사람이 믹서에서 바꾼 것으로 보고
/// 그 값을 기억해 설정 파일(<see cref="GameSettings.AppVolume"/>)에 적고, (2) 새로 생긴 세션 · 다른 값의 세션은 기억한 값으로 맞춘다.
/// 새 세션은 <c>IAudioSessionNotification</c> 으로 곧바로 알아 맞춘다(1초를 기다리면 그새 크게 난다).
/// 한 번도 믹서를 만지지 않았으면 아무것도 바꾸지 않는다.
///
/// 오디오 COM 객체는 스레드 풀(MTA)에서만 만진다. 설정 파일은 UI 스레드에서 적는다.
/// </remarks>
public static class AppVolumeKeeper
{
    private static readonly object Gate = new();
    /// <summary>세션 인스턴스 이름 → 지난번에 보았거나 맞춘 값.</summary>
    private static readonly Dictionary<string, (float Level, bool Mute)> Seen = new();
    /// <summary>장치 id → 새 세션 알림을 걸어 둔 세션 관리자.</summary>
    private static readonly Dictionary<string, IAudioSessionManager2> Managers = new();
    private static readonly SessionWatcher Watcher = new();
    private static Guid _context = Guid.NewGuid();
    private static Timer? _timer;
    private static Dispatcher? _ui;
    private static uint _pid;
    private static float? _level;
    private static bool _mute;
    private static bool _dirty;
    private static int _applied;
    private static string _error = "";

    /// <summary>켠다 — UI 스레드에서 한 번 부른다.</summary>
    public static void Start()
    {
        if (_timer != null) return;
        _ui = Dispatcher.CurrentDispatcher;
        _pid = (uint)Environment.ProcessId;
        double saved = GameSettings.AppVolume;
        if (saved >= 0)
        {
            _level = (float)Math.Clamp(saved, 0, 1);
            _mute = GameSettings.AppMute;
        }
        _timer = new Timer(_ => Tick(), null, 0, 1000);
    }

    /// <summary>끈다 — 끄기 직전 믹서에서 바꾼 것까지 적는다. UI 스레드에서 부른다.</summary>
    public static void Stop()
    {
        if (_timer == null) return;
        _timer.Dispose();
        _timer = null;
        try { Task.Run(Tick).Wait(1000); } catch (Exception) { /* 못 읽어도 끈다 */ }
        SavePending();
    }

    /// <summary>버그 리포트 한 줄.</summary>
    public static string Describe()
    {
        lock (Gate)
        {
            string want = _level is float l ? $"{l * 100:0}%{(_mute ? " 음소거" : "")}" : "안 만짐";
            return $"기억 {want} · 세션 {Seen.Count} · 맞춤 {_applied}{(_error.Length > 0 ? $" · {_error}" : "")}";
        }
    }

    private static void Tick()
    {
        if (!Monitor.TryEnter(Gate)) return;
        try
        {
            var sessions = Mine();
            // 지난번에 본 세션의 값이 달라졌으면 사람이 믹서에서 바꾼 것이다 — 믹서는 묶인 세션을 다 같이 바꾼다.
            foreach (var s in sessions)
                if (Seen.TryGetValue(s.Id, out var was) && (Differs(was.Level, s.Level) || was.Mute != s.Mute))
                {
                    Learn(s.Level, s.Mute);
                    break;
                }

            var live = new HashSet<string>();
            foreach (var s in sessions)
            {
                live.Add(s.Id);
                Seen[s.Id] = Apply(s.Volume, s.Level, s.Mute);
            }
            foreach (string gone in Seen.Keys.Where(k => !live.Contains(k)).ToList()) Seen.Remove(gone);
            _error = "";
        }
        catch (Exception ex) { _error = $"{DateTime.Now:HH:mm:ss} {ex.GetType().Name} 0x{ex.HResult:X8}"; }
        finally { Monitor.Exit(Gate); }
    }

    /// <summary>기억한 값으로 맞추고 맞춘 뒤의 값을 낸다. Gate 안에서 부른다.</summary>
    private static (float, bool) Apply(ISimpleAudioVolume volume, float level, bool mute)
    {
        if (_level is not float want) return (level, mute);
        if (Differs(level, want) && volume.SetMasterVolume(want, ref _context) == 0) { level = want; _applied++; }
        if (mute != _mute && volume.SetMute(_mute, ref _context) == 0) mute = _mute;
        return (level, mute);
    }

    private static void Learn(float level, bool mute)
    {
        _level = level;
        _mute = mute;
        _dirty = true;
        try { _ui?.BeginInvoke(SavePending); } catch (Exception) { /* 끄는 중이면 Stop 이 적는다 */ }
    }

    private static void SavePending()
    {
        float level;
        bool mute;
        lock (Gate)
        {
            if (!_dirty || _level is not float l) return;
            _dirty = false;
            level = l;
            mute = _mute;
        }
        GameSettings.SetAppVolume(Math.Round(level, 3), mute);
    }

    private static bool Differs(float a, float b) => Math.Abs(a - b) > 0.004f;

    private readonly record struct Session(string Id, ISimpleAudioVolume Volume, float Level, bool Mute);

    /// <summary>켜진 모든 출력 장치에서 이 프로세스의 세션을 모은다. Gate 안에서 부른다.</summary>
    private static List<Session> Mine()
    {
        var found = new List<Session>();
        var enumerator = _enumerator ??= NewEnumerator();
        if (enumerator.EnumAudioEndpoints(EDataFlowRender, DeviceStateActive, out var list) != 0 || list == null) return found;
        list.GetCount(out uint count);
        for (uint i = 0; i < count; i++)
        {
            if (list.Item(i, out var device) != 0 || device == null) continue;
            if (device.GetId(out string? deviceId) != 0 || deviceId == null) continue;
            if (!Managers.TryGetValue(deviceId, out var manager))
            {
                var iid = typeof(IAudioSessionManager2).GUID;
                if (device.Activate(ref iid, ClsctxAll, IntPtr.Zero, out var activated) != 0 || activated is not IAudioSessionManager2 made) continue;
                manager = made;
                Managers[deviceId] = manager;
                // 세션 목록을 한 번 받아야 새 세션 알림이 온다(문서). 알림은 이 객체를 놓지 않는 한 산다.
                manager.GetSessionEnumerator(out _);
                manager.RegisterSessionNotification(Watcher);
            }
            if (manager.GetSessionEnumerator(out var sessions) != 0 || sessions == null) continue;
            sessions.GetCount(out int n);
            for (int k = 0; k < n; k++)
            {
                if (sessions.GetSession(k, out var raw) != 0 || raw is not IAudioSessionControl2 control || raw is not ISimpleAudioVolume volume) continue;
                if (!Ours(control, out string? id)) continue;
                volume.GetMasterVolume(out float level);
                volume.GetMute(out bool mute);
                found.Add(new Session($"{deviceId}|{id}", volume, level, mute));
            }
        }
        return found;
    }

    private static IMMDeviceEnumerator? _enumerator;

    /// <summary>
    /// 장치 열거자를 만든다 — <c>new MMDeviceEnumerator()</c> 를 쓰지 않는다. 그 객체는 프로세스에 하나라 RCW 가 함께 쓰이는데,
    /// 소리 진단(<see cref="SoundDiagnostics"/>)이 제 coclass 형으로 먼저 잡아 두면 여기서 「MMDeviceEnumerator 를
    /// MMDeviceEnumerator 로 못 바꾼다」로 넘어지고, 거꾸로도 그렇다. 따로 쓰는 RCW(GetUniqueObjectForIUnknown)로 받는다.
    /// </summary>
    private static IMMDeviceEnumerator NewEnumerator()
    {
        var clsid = new Guid("BCDE0395-E52F-467C-8E3D-C4579291692E");
        var iid = typeof(IMMDeviceEnumerator).GUID;
        Marshal.ThrowExceptionForHR(CoCreateInstance(ref clsid, IntPtr.Zero, ClsctxAll, ref iid, out IntPtr raw));
        try { return (IMMDeviceEnumerator)Marshal.GetUniqueObjectForIUnknown(raw); }
        finally { Marshal.Release(raw); }
    }

    [DllImport("ole32.dll")]
    private static extern int CoCreateInstance(ref Guid clsid, IntPtr outer, int clsCtx, ref Guid iid, out IntPtr made);

    /// <summary>이 프로세스의 살아 있는 세션인지.</summary>
    private static bool Ours(IAudioSessionControl2 control, out string? id)
    {
        id = null;
        // wmp.dll · WinRT 세션은 「#」(여러 프로세스) 세션이라 AUDCLNT_S_NO_SINGLE_PROCESS(성공 코드)를 내지만 pid 는 이 프로세스로 채운다.
        if (control.GetProcessId(out uint pid) < 0 || pid != _pid) return false;
        if (control.GetState(out int state) != 0 || state == AudioSessionStateExpired) return false;
        return control.GetSessionInstanceIdentifier(out id) == 0 && id != null;
    }

    /// <summary>새 세션이 생겼다 — 오디오 서비스 스레드에서 온다.</summary>
    private static void Created(object session)
    {
        if (session is not IAudioSessionControl2 control || session is not ISimpleAudioVolume volume) return;
        if (!Ours(control, out _)) return;
        lock (Gate)
        {
            // 장치 이름을 몰라 Seen 에는 안 넣는다 — 다음 훑기가 「새 세션」으로 보고 이미 맞춘 값을 적는다.
            if (_level is not float want) return;
            volume.GetMasterVolume(out float level);
            volume.GetMute(out bool mute);
            Apply(volume, level, mute);
        }
    }

    [ComVisible(true)]
    private sealed class SessionWatcher : IAudioSessionNotification
    {
        public int OnSessionCreated(object session)
        {
            try { Created(session); }
            catch (Exception) { /* 알림 스레드에서 던지지 않는다 */ }
            return 0;
        }
    }

    // ── COM ────────────────────────────────────────────────────────────────

    private const int EDataFlowRender = 0, DeviceStateActive = 1, ClsctxAll = 23, AudioSessionStateExpired = 2;

    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceEnumerator
    {
        [PreserveSig] int EnumAudioEndpoints(int dataFlow, int stateMask, out IMMDeviceCollection? devices);
    }

    [ComImport, Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceCollection
    {
        [PreserveSig] int GetCount(out uint count);
        [PreserveSig] int Item(uint index, out IMMDevice? device);
    }

    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDevice
    {
        [PreserveSig] int Activate(ref Guid iid, int clsCtx, IntPtr activationParams, [MarshalAs(UnmanagedType.IUnknown)] out object? activated);
        [PreserveSig] int OpenPropertyStore(int access, out IntPtr store);
        [PreserveSig] int GetId([MarshalAs(UnmanagedType.LPWStr)] out string? id);
    }

    [ComImport, Guid("77AA99A0-1BD6-484F-8BC7-2C654C9A9B6F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioSessionManager2
    {
        [PreserveSig] int GetAudioSessionControl(IntPtr session, uint flags, out IntPtr control);
        [PreserveSig] int GetSimpleAudioVolume(IntPtr session, uint flags, out IntPtr volume);
        [PreserveSig] int GetSessionEnumerator(out IAudioSessionEnumerator? sessions);
        [PreserveSig] int RegisterSessionNotification(IAudioSessionNotification notification);
        [PreserveSig] int UnregisterSessionNotification(IAudioSessionNotification notification);
    }

    [ComImport, Guid("E2F5BB11-0570-40CA-ACDD-3AA01277DEE8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioSessionEnumerator
    {
        [PreserveSig] int GetCount(out int count);
        [PreserveSig] int GetSession(int index, [MarshalAs(UnmanagedType.IUnknown)] out object? session);
    }

    [ComImport, Guid("641DD20B-4D41-49CC-ABA3-174B9477BB08"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioSessionNotification
    {
        [PreserveSig] int OnSessionCreated([MarshalAs(UnmanagedType.IUnknown)] object session);
    }

    [ComImport, Guid("BFB7FF88-7239-4FC9-8FA2-07C950BE9C6D"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioSessionControl2
    {
        [PreserveSig] int GetState(out int state);
        [PreserveSig] int GetDisplayName(out IntPtr name);
        [PreserveSig] int SetDisplayName(IntPtr name, ref Guid context);
        [PreserveSig] int GetIconPath(out IntPtr path);
        [PreserveSig] int SetIconPath(IntPtr path, ref Guid context);
        [PreserveSig] int GetGroupingParam(out Guid group);
        [PreserveSig] int SetGroupingParam(ref Guid group, ref Guid context);
        [PreserveSig] int RegisterAudioSessionNotification(IntPtr events);
        [PreserveSig] int UnregisterAudioSessionNotification(IntPtr events);
        [PreserveSig] int GetSessionIdentifier([MarshalAs(UnmanagedType.LPWStr)] out string? id);
        [PreserveSig] int GetSessionInstanceIdentifier([MarshalAs(UnmanagedType.LPWStr)] out string? id);
        [PreserveSig] int GetProcessId(out uint pid);
    }

    [ComImport, Guid("87CE5498-68D6-44E5-9215-6DA47EF883D8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ISimpleAudioVolume
    {
        [PreserveSig] int SetMasterVolume(float level, ref Guid context);
        [PreserveSig] int GetMasterVolume(out float level);
        [PreserveSig] int SetMute([MarshalAs(UnmanagedType.Bool)] bool mute, ref Guid context);
        [PreserveSig] int GetMute([MarshalAs(UnmanagedType.Bool)] out bool mute);
    }
}
