using System.Runtime.InteropServices;
using CdsHelper.Game.Local.Settings;

namespace CdsHelper.Game.Local.Helpers;

/// <summary>
/// 버그 리포트에 싣는 소리 진단 — 「소리가 아예 안 난다」(버그 리포트 #38)는 기록이 없어 장치 탓인지 앱 탓인지 가를 길이 없었다.
/// </summary>
/// <remarks>
/// 싣는 것: 배경음악 · 효과음 상태와 마지막 오류, 트는 손(<see cref="MediaVoice.Backend"/>)과 wmp.dll 유무,
/// 기본 출력 장치 이름 · 켜진 출력 장치 수 · 장치 음량/음소거, <b>이 앱의 볼륨 믹서 음량/음소거</b>(Windows 가 앱마다 기억한다),
/// waveOut 장치 수, <b>장치 음량이 바뀐 자취</b>(언제 · 무엇을 하던 중에 — 버그 리포트 #45). 어느 하나를 못 읽어도 나머지는 싣는다.
/// 장치 음량 · 앱 음량은 <b>읽기만</b> 한다 — 이 앱은 Windows 음량을 바꾸지 않는다.
/// </remarks>
public static class SoundDiagnostics
{
    public static Dictionary<string, object?> Collect()
    {
        var sound = new Dictionary<string, object?>();
        void Try(string key, Func<object?> read)
        {
            try { sound[key] = read(); }
            catch (Exception ex) { sound[key] = $"? {ex.GetType().Name} 0x{ex.HResult:X8}"; }
        }

        Try("bgm", () => BgmPlayer.Current?.Describe() ?? "(틀개 없음)");
        Try("bgmError", () => BgmPlayer.Current?.LastError is { Length: > 0 } e ? e : null);
        Try("muteInBackground", () => GameSettings.MuteBgmInBackground);
        Try("sfx", () =>
            $"{(GameSettings.SfxEnabled ? "켬" : "끔")} · 크기 {GameSettings.SfxVolume} · {SoundBank.Current?.Origin ?? "없음"}"
            + $" · 낸 {SoundBank.Plays} · 못 낸 {SoundBank.PlayFailures}");
        Try("sfxError", () => SoundBank.LastPlayError is { Length: > 0 } e ? e : SoundBank.LastError is { Length: > 0 } o ? o : null);
        Try("media", () => $"{MediaVoice.Backend} · wmp.dll {(MediaVoice.WmpPresent ? "있음" : "없음")} · 못 튼 {MediaVoice.Failures}");
        Try("mediaError", () => MediaVoice.LastFailure is { Length: > 0 } e ? e : null);
        Try("waveOut", () => SoundBank.waveOutGetNumDevs());
        Try("device", DefaultDevice);
        Try("devices", ActiveDevices);
        Try("session", AppSession);
        Try("deviceChanges", () => Changes());
        return sound;
    }

    // ── 장치 음량이 바뀐 자취 (버그 리포트 #45) ─────────────────────────────
    // 「폭풍이 지나가면 Windows 음량이 100 이 된다」 — 앱은 장치 음량을 쓰지 않는다(위 IAudioEndpointVolume 은 읽기만).
    // 언제 · 무엇을 하던 중에 · 누가(바꾼 쪽이 넘긴 이벤트 문맥 GUID) 바꿨는지 남겨, 다음 리포트에서 장치 탓인지 가른다.

    // 버그 리포트 #46 · #47 — 육상전 뒤에 음량이 뛰었다는데 deviceChanges 가 null 로 왔다. 예전 감시는 앱을 켤 때 잡은
    // 기본 장치의 음량 알림 하나뿐이라, (1) 감시가 걸렸는지조차 리포트에 안 남았고 (2) USB 장치가 쉬었다 깨며 다시 잡히면
    // (끊겼다 붙음 · 기본 장치 바뀜) 옛 장치 객체의 알림이 끊겨 그 뒤의 바뀜을 하나도 못 잡았다.
    // 이제는 감시를 건 때 · 그때 음량을 늘 싣고, 장치가 붙고 떨어지고 기본이 바뀌는 것(IMMNotificationClient)도 적고,
    // 소리 쪽 일을 할 때마다(Doing) · 리포트를 모을 때 음량을 몸소 다시 읽어 알림 없이 바뀐 것도 적고 새 장치로 감시를 옮겨 건다.

    private static readonly object Gate = new();
    private static readonly Queue<string> Recent = new();
    private const int KeepChanges = 12;
    private static volatile string _doing = "-";
    private static IAudioEndpointVolume? _watched;
    private static string? _watchedId;
    private static VolumeWatcher? _watcher;
    private static IntPtr _watcherPtr;
    private static IMMDeviceEnumerator? _deviceEnumerator;
    private static DeviceWatcher? _deviceWatcher;
    private static IntPtr _deviceWatcherPtr;
    private static float _lastLevel = -1;
    /// <summary>감시를 건 때 · 그때 음량 — 또는 못 건 까닭. 리포트에 늘 싣는다.</summary>
    private static string _watchNote = "감시 안 함";

    /// <summary>소리 쪽에서 지금 하는 일 — 장치 음량이 바뀌면 이것과 함께 적는다. 그 김에 음량을 몸소 다시 읽는다.</summary>
    public static void Doing(string what)
    {
        _doing = $"{DateTime.Now:HH:mm:ss} {what}";
        Peek();
    }

    /// <summary>기본 출력 장치의 음량 바뀜과 장치 붙고 떨어짐을 지켜보기 시작한다. 한 번만 건다 — 못 걸면 까닭을 남기고 넘어간다.</summary>
    public static void Watch()
    {
        try
        {
            if (_deviceWatcher == null)
            {
                _deviceEnumerator = Enumerator();
                _deviceWatcher = new DeviceWatcher();
                _deviceWatcherPtr = Marshal.GetComInterfaceForObject(_deviceWatcher, typeof(IMMNotificationClient));
                int hr = _deviceEnumerator.RegisterEndpointNotificationCallback(_deviceWatcherPtr);
                if (hr != 0) Note($"장치 알림 못 검 0x{hr:X8}");
            }
        }
        catch (Exception ex) { Note($"장치 알림 못 검 — {ex.GetType().Name} 0x{ex.HResult:X8}"); }
        if (_watched == null) WatchDefault();
    }

    /// <summary>지금 기본 장치에 음량 알림을 건다 — 앞서 건 것이 있으면 풀고.</summary>
    private static void WatchDefault()
    {
        try
        {
            if (_watched != null)
            {
                try { _watched.UnregisterControlChangeNotify(_watcherPtr); } catch (Exception) { /* 이미 사라진 장치다 */ }
                _watched = null;
                _watchedId = null;
            }
            var enumerator = Enumerator();
            int hr = enumerator.GetDefaultAudioEndpoint(EDataFlowRender, ERoleConsole, out var device);
            if (hr != 0 || device == null) { _watchNote = $"{DateTime.Now:HH:mm:ss} 감시 못 함 — 기본 장치 없음 0x{hr:X8}"; return; }
            string? id = IdOf(device);
            var iid = typeof(IAudioEndpointVolume).GUID;
            hr = device.Activate(ref iid, ClsctxAll, IntPtr.Zero, out var activated);
            if (hr != 0 || activated is not IAudioEndpointVolume volume) { _watchNote = $"{DateTime.Now:HH:mm:ss} 감시 못 함 — 음량 0x{hr:X8}"; return; }
            volume.GetMasterVolumeLevelScalar(out float level);
            lock (Gate) _lastLevel = level;
            _watcher ??= new VolumeWatcher();
            if (_watcherPtr == IntPtr.Zero) _watcherPtr = Marshal.GetComInterfaceForObject(_watcher, typeof(IAudioEndpointVolumeCallback));
            hr = volume.RegisterControlChangeNotify(_watcherPtr);
            if (hr != 0) { _watchNote = $"{DateTime.Now:HH:mm:ss} 감시 못 함 — 알림 0x{hr:X8}"; return; }
            _watched = volume;
            _watchedId = id;
            _watchNote = $"{DateTime.Now:HH:mm:ss} 감시 {level * 100:0}% {FriendlyName(device)}";
        }
        catch (Exception ex) { _watchNote = $"{DateTime.Now:HH:mm:ss} 감시 못 함 — {ex.GetType().Name} 0x{ex.HResult:X8}"; }
    }

    /// <summary>
    /// 기본 장치의 음량을 몸소 다시 읽는다 — 알림 없이 바뀐 것을 적고, 기본 장치가 갈렸으면(USB 장치가 다시 잡힘) 감시를 옮겨 건다.
    /// </summary>
    private static void Peek()
    {
        if (_watched == null && _deviceWatcher == null) return;   // 감시를 건 적이 없다(시작 전)
        try
        {
            var enumerator = Enumerator();
            if (enumerator.GetDefaultAudioEndpoint(EDataFlowRender, ERoleConsole, out var device) != 0 || device == null) return;
            string? id = IdOf(device);
            if (_watched == null)
            {
                // 못 걸었던 감시를 다시 건다 — 걸렸을 때만 적는다(못 걸면 일마다 같은 줄이 쌓인다).
                WatchDefault();
                if (_watched != null) Note($"감시 다시 걺 — {_watchNote}");
                return;
            }
            if (id != _watchedId)
            {
                WatchDefault();
                Note($"기본 장치가 다시 잡혀 감시 옮김 — {_watchNote}");
                return;
            }
            var iid = typeof(IAudioEndpointVolume).GUID;
            if (device.Activate(ref iid, ClsctxAll, IntPtr.Zero, out var activated) != 0 || activated is not IAudioEndpointVolume volume) return;
            volume.GetMasterVolumeLevelScalar(out float level);
            float was;
            lock (Gate) { was = _lastLevel; if (Math.Abs(level - was) >= 0.005f) _lastLevel = level; }
            if (Math.Abs(level - was) >= 0.005f) Note($"{was * 100:0}→{level * 100:0}% (알림 없이 — 다시 읽어 앎)");
        }
        catch (Exception) { /* 진단일 뿐이다 */ }
    }

    private static string? IdOf(IMMDevice device)
    {
        if (device.GetId(out var ptr) != 0 || ptr == IntPtr.Zero) return null;
        try { return Marshal.PtrToStringUni(ptr); }
        finally { Marshal.FreeCoTaskMem(ptr); }
    }

    private static string? Changes()
    {
        Peek();
        lock (Gate) return Recent.Count == 0 ? $"{_watchNote} · 바뀜 없음" : $"{_watchNote} · " + string.Join(" / ", Recent);
    }

    /// <summary>자취 한 줄을 적는다 — 때와 하던 일을 붙인다.</summary>
    private static void Note(string what)
    {
        string line = $"{DateTime.Now:HH:mm:ss} {what} (하던 일 {_doing})";
        lock (Gate)
        {
            Recent.Enqueue(line);
            while (Recent.Count > KeepChanges) Recent.Dequeue();
        }
    }

    private static void Noted(Guid context, bool mute, float level)
    {
        // 음소거만 바뀐 알림은 음량이 같다 — 그래도 적는다(드물다).
        string from = context == Guid.Empty ? "문맥 없음" : context.ToString()[..8];
        float was;
        lock (Gate) { was = _lastLevel; _lastLevel = level; }
        Note($"{was * 100:0}→{level * 100:0}%{(mute ? " 음소거" : "")} ({from})");
    }

    /// <summary>장치가 붙고 떨어지고 기본 장치가 바뀌는 알림을 받는 COM 손. 딴 스레드에서 불린다 — 여기서는 적기만 한다.</summary>
    private sealed class DeviceWatcher : IMMNotificationClient
    {
        private static string Which(string? id) => id != null && id == _watchedId ? "지켜보던 장치" : "다른 장치";

        public int OnDeviceStateChanged(string? id, int state)
        {
            string word = state switch { 1 => "켜짐", 2 => "꺼짐", 4 => "없음", 8 => "뽑힘", _ => state.ToString() };
            Note($"{Which(id)} {word}");
            return 0;
        }

        public int OnDeviceAdded(string? id) { Note($"{Which(id)} 붙음"); return 0; }

        public int OnDeviceRemoved(string? id) { Note($"{Which(id)} 떨어짐"); return 0; }

        public int OnDefaultDeviceChanged(int flow, int role, string? id)
        {
            if (flow == EDataFlowRender && role == ERoleConsole) Note($"기본 출력 장치 바뀜 → {Which(id)}");
            return 0;
        }

        public int OnPropertyValueChanged(string? id, PropertyKey key) => 0;   // 잦고 쓸모없다
    }

    /// <summary>장치 음량 알림을 받는 COM 손. 딴 스레드에서 불린다.</summary>
    private sealed class VolumeWatcher : IAudioEndpointVolumeCallback
    {
        public int OnNotify(IntPtr data)
        {
            try
            {
                // AUDIO_VOLUME_NOTIFICATION_DATA: GUID guidEventContext; BOOL bMuted; float fMasterVolume; …
                var context = Marshal.PtrToStructure<Guid>(data);
                bool mute = Marshal.ReadInt32(data, 16) != 0;
                float level = BitConverter.Int32BitsToSingle(Marshal.ReadInt32(data, 20));
                Noted(context, mute, level);
            }
            catch (Exception) { /* 진단일 뿐이다 */ }
            return 0;
        }
    }

    /// <summary>한 줄 요약 — 버그 리포트 창 아래에 보인다.</summary>
    public static string Summary(Dictionary<string, object?> sound)
    {
        string Word(string key) => sound.TryGetValue(key, out var v) && v != null ? v.ToString() ?? "" : "";
        string bgm = Word("bgm");
        return $"{Word("device")} · BGM {(bgm.StartsWith("켬") ? "on" : "off")} · SFX {(GameSettings.SfxEnabled ? "on" : "off")}"
               + (Word("bgmError").Length > 0 || Word("mediaError").Length > 0 || Word("sfxError").Length > 0 ? " · !" : "");
    }

    // ── Core Audio (MMDevice) ───────────────────────────────────────────

    private static IMMDeviceEnumerator Enumerator() => (IMMDeviceEnumerator)new MMDeviceEnumeratorCom();

    /// <summary>기본 출력 장치의 이름 · 장치 음량 · 음소거.</summary>
    private static string DefaultDevice()
    {
        var enumerator = Enumerator();
        int hr = enumerator.GetDefaultAudioEndpoint(EDataFlowRender, ERoleConsole, out var device);
        if (hr != 0 || device == null) return $"(기본 출력 장치 없음 0x{hr:X8})";
        string name = FriendlyName(device);

        string level = "";
        var iid = typeof(IAudioEndpointVolume).GUID;
        if (device.Activate(ref iid, ClsctxAll, IntPtr.Zero, out var activated) == 0 && activated is IAudioEndpointVolume volume)
        {
            volume.GetMasterVolumeLevelScalar(out float scalar);
            volume.GetMute(out bool mute);
            level = $" · 장치 음량 {scalar * 100:0}%{(mute ? " 음소거" : "")}";
        }
        return name + level;
    }

    /// <summary>켜져 있는 출력 장치 수와 이름.</summary>
    private static string ActiveDevices()
    {
        var enumerator = Enumerator();
        if (enumerator.EnumAudioEndpoints(EDataFlowRender, DeviceStateActive, out var list) != 0 || list == null) return "?";
        list.GetCount(out uint count);
        var names = new List<string>();
        for (uint i = 0; i < count && i < 8; i++)
            if (list.Item(i, out var device) == 0 && device != null) names.Add(FriendlyName(device));
        return $"{count}: {string.Join(" / ", names)}";
    }

    /// <summary>
    /// 이 앱의 소리 세션(볼륨 믹서의 「CostaDelSol」 줄) 음량 · 음소거 — Windows 가 앱마다 기억하므로 한 번 내려 두면 늘 조용하다.
    /// </summary>
    private static string AppSession()
    {
        var enumerator = Enumerator();
        if (enumerator.GetDefaultAudioEndpoint(EDataFlowRender, ERoleConsole, out var device) != 0 || device == null) return "?";
        var iid = typeof(IAudioSessionManager).GUID;
        if (device.Activate(ref iid, ClsctxAll, IntPtr.Zero, out var activated) != 0 || activated is not IAudioSessionManager manager)
            return "?";
        var none = Guid.Empty;
        if (manager.GetSimpleAudioVolume(ref none, 0, out var simple) != 0 || simple == null) return "?";
        simple.GetMasterVolume(out float level);
        simple.GetMute(out bool mute);
        return $"앱 음량 {level * 100:0}%{(mute ? " 음소거" : "")}";
    }

    private static string FriendlyName(IMMDevice device)
    {
        if (device.OpenPropertyStore(StgmRead, out var store) != 0 || store == null) return "?";
        var key = new PropertyKey { Format = new Guid("a45c254e-df1c-4efd-8020-67d146a850e0"), Id = 14 };
        if (store.GetValue(ref key, out var value) != 0) return "?";
        try
        {
            return value.Type == VtLpwstr && value.Pointer != IntPtr.Zero ? Marshal.PtrToStringUni(value.Pointer) ?? "?" : "?";
        }
        finally { PropVariantClear(ref value); }
    }

    private const int EDataFlowRender = 0, ERoleConsole = 0, DeviceStateActive = 1, ClsctxAll = 0x17, StgmRead = 0;
    private const ushort VtLpwstr = 31;

    [DllImport("ole32.dll")]
    private static extern int PropVariantClear(ref PropVariant value);

    [StructLayout(LayoutKind.Sequential)]
    private struct PropertyKey
    {
        public Guid Format;
        public int Id;
    }

    [StructLayout(LayoutKind.Explicit, Size = 24)]
    private struct PropVariant
    {
        [FieldOffset(0)] public ushort Type;
        [FieldOffset(8)] public IntPtr Pointer;
    }

    [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
    private class MMDeviceEnumeratorCom;

    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceEnumerator
    {
        [PreserveSig] int EnumAudioEndpoints(int dataFlow, int stateMask, out IMMDeviceCollection? devices);
        [PreserveSig] int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice? device);
        [PreserveSig] int GetDevice([MarshalAs(UnmanagedType.LPWStr)] string id, out IMMDevice? device);
        [PreserveSig] int RegisterEndpointNotificationCallback(IntPtr client);
        [PreserveSig] int UnregisterEndpointNotificationCallback(IntPtr client);
    }

    [ComImport, Guid("7991EEC9-7E89-4D85-8390-6C703CEC60C0"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMNotificationClient
    {
        [PreserveSig] int OnDeviceStateChanged([MarshalAs(UnmanagedType.LPWStr)] string? id, int state);
        [PreserveSig] int OnDeviceAdded([MarshalAs(UnmanagedType.LPWStr)] string? id);
        [PreserveSig] int OnDeviceRemoved([MarshalAs(UnmanagedType.LPWStr)] string? id);
        [PreserveSig] int OnDefaultDeviceChanged(int flow, int role, [MarshalAs(UnmanagedType.LPWStr)] string? id);
        [PreserveSig] int OnPropertyValueChanged([MarshalAs(UnmanagedType.LPWStr)] string? id, PropertyKey key);
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
        [PreserveSig] int OpenPropertyStore(int access, out IPropertyStore? store);
        [PreserveSig] int GetId(out IntPtr id);
    }

    [ComImport, Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPropertyStore
    {
        [PreserveSig] int GetCount(out uint count);
        [PreserveSig] int GetAt(uint index, out PropertyKey key);
        [PreserveSig] int GetValue(ref PropertyKey key, out PropVariant value);
    }

    [ComImport, Guid("5CDF2C82-841E-4546-9722-0CF74078229A"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioEndpointVolume
    {
        [PreserveSig] int RegisterControlChangeNotify(IntPtr notify);
        [PreserveSig] int UnregisterControlChangeNotify(IntPtr notify);
        [PreserveSig] int GetChannelCount(out uint count);
        [PreserveSig] int SetMasterVolumeLevel(float level, ref Guid context);
        [PreserveSig] int SetMasterVolumeLevelScalar(float level, ref Guid context);
        [PreserveSig] int GetMasterVolumeLevel(out float level);
        [PreserveSig] int GetMasterVolumeLevelScalar(out float level);
        [PreserveSig] int SetChannelVolumeLevel(uint channel, float level, ref Guid context);
        [PreserveSig] int SetChannelVolumeLevelScalar(uint channel, float level, ref Guid context);
        [PreserveSig] int GetChannelVolumeLevel(uint channel, out float level);
        [PreserveSig] int GetChannelVolumeLevelScalar(uint channel, out float level);
        [PreserveSig] int SetMute([MarshalAs(UnmanagedType.Bool)] bool mute, ref Guid context);
        [PreserveSig] int GetMute([MarshalAs(UnmanagedType.Bool)] out bool mute);
    }

    [ComImport, Guid("657804FA-D6AD-4496-8A60-352752AF4F89"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioEndpointVolumeCallback
    {
        [PreserveSig] int OnNotify(IntPtr data);
    }

    [ComImport, Guid("BFA971F1-4D5E-40BB-935E-967039BFBEE4"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioSessionManager
    {
        [PreserveSig] int GetAudioSessionControl(ref Guid session, uint flags, out IntPtr control);
        [PreserveSig] int GetSimpleAudioVolume(ref Guid session, uint flags, out ISimpleAudioVolume? volume);
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
