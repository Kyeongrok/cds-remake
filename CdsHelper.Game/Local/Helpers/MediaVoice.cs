using System.IO;
using System.Windows.Threading;

namespace CdsHelper.Game.Local.Helpers;

/// <summary>
/// 파일 소리 하나를 트는 손 — 배경음악(<see cref="BgmPlayer"/>)과 겹쳐 나는 효과음 · 되풀이 소리(<see cref="SoundBank"/>)가 쓴다.
/// WPF <see cref="System.Windows.Media.MediaPlayer"/> 로 틀다가 그것이 못 틀면 WinRT
/// <see cref="Windows.Media.Playback.MediaPlayer"/> 로 갈아탄다.
/// </summary>
/// <remarks>
/// WPF MediaPlayer 는 속에서 <b>Windows Media Player(wmp.dll)</b>에 기댄다. Windows 11 은 「Windows Media Player 레거시」가
/// 선택적 기능이라 지울 수 있고, N 판은 처음부터 없다. 그러면 <c>Open</c> 이 오류 없이 돌아오고 <c>MediaFailed</c> 만 뒤에서
/// 울려, 이전에는 그 소리를 아무도 듣지 않아 배경음악 · 효과음이 <b>조용히</b> 안 났다(버그 리포트 #38).
/// WinRT MediaPlayer 는 wmp.dll 없이 Media Foundation 으로 바로 튼다. wmp.dll 이 없으면 처음부터 그쪽으로,
/// WPF 쪽이 한 번이라도 못 틀면 그때부터 모든 손이 그쪽으로 간다. 시험은 <c>CDS_AUDIO=winrt</c>.
///
/// 만든 스레드(UI)에 매인다. WinRT 쪽 알림은 딴 스레드에서 오므로 만든 스레드로 넘겨 낸다.
/// </remarks>
public sealed class MediaVoice
{
    /// <summary>지금 쓰는 쪽 — 버그 리포트에 싣는다.</summary>
    public static string Backend => _useWinRt ? "winrt" : "wpf";

    /// <summary>Windows Media Player(wmp.dll)가 깔려 있는지 — WPF MediaPlayer 가 이것에 기댄다.</summary>
    public static bool WmpPresent { get; } =
        File.Exists(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "wmp.dll"));

    /// <summary>WinRT 쪽으로 트는지 — 모든 손이 함께 따른다. <see cref="WmpPresent"/> 보다 뒤에 둔다(정적 초기화 차례).</summary>
    private static bool _useWinRt = Environment.GetEnvironmentVariable("CDS_AUDIO") == "winrt" || !WmpPresent;

    /// <summary>어느 손에서든 마지막으로 못 튼 까닭. 없으면 빈 문자열.</summary>
    public static string LastFailure { get; private set; } = "";

    /// <summary>못 튼 횟수(모든 손 합).</summary>
    public static int Failures { get; private set; }

    /// <summary>끝까지 다 틀었다.</summary>
    public event EventHandler? MediaEnded;

    /// <summary>두 쪽 다 못 틀었다 — 까닭 한 줄.</summary>
    public event Action<string>? Failed;

    private readonly Dispatcher _dispatcher = Dispatcher.CurrentDispatcher;
    private System.Windows.Media.MediaPlayer? _wpf;
    private Windows.Media.Playback.MediaPlayer? _rt;
    private Uri? _uri;
    private bool _playing;
    private double _volume = 1;

    /// <summary>소리 크기(0~1).</summary>
    public double Volume
    {
        get => _volume;
        set
        {
            _volume = Math.Clamp(value, 0, 1);
            try
            {
                if (_wpf != null) _wpf.Volume = _volume;
                if (_rt != null) _rt.Volume = _volume;
            }
            catch (Exception ex) { Note($"크기 — {ex.Message}"); }
        }
    }

    /// <summary>지금 자리. 모르면 0.</summary>
    public TimeSpan Position
    {
        get
        {
            try { return _rt?.PlaybackSession.Position ?? _wpf?.Position ?? TimeSpan.Zero; }
            catch (Exception) { return TimeSpan.Zero; }
        }
    }

    /// <summary>곡 길이. 아직 모르면 null.</summary>
    public TimeSpan? Duration
    {
        get
        {
            try
            {
                if (_rt != null) return _rt.PlaybackSession.NaturalDuration is { Ticks: > 0 } d ? d : null;
                return _wpf is { NaturalDuration.HasTimeSpan: true } p ? p.NaturalDuration.TimeSpan : null;
            }
            catch (Exception) { return null; }
        }
    }

    /// <summary>지금 재생 상태 한 줄 — 버그 리포트에 싣는다.</summary>
    public string State
    {
        get
        {
            try
            {
                if (_rt != null) return $"winrt {_rt.PlaybackSession.PlaybackState}";
                if (_wpf != null) return $"wpf {(_playing ? "Playing" : "Idle")}{(_wpf.HasAudio ? "" : " (소리 없음)")}";
                return "-";
            }
            catch (Exception ex) { return $"? {ex.GetType().Name}"; }
        }
    }

    public void Open(Uri uri)
    {
        _uri = uri;
        _playing = false;
        if (_useWinRt) OpenRt(uri);
        else OpenWpf(uri);
    }

    public void Play()
    {
        _playing = true;
        try
        {
            if (_rt != null) _rt.Play();
            else _wpf?.Play();
        }
        catch (Exception ex) { Fail($"틀기 — {ex.Message}"); }
    }

    public void Pause()
    {
        _playing = false;
        try
        {
            _rt?.Pause();
            _wpf?.Pause();
        }
        catch (Exception ex) { Note($"멈추기 — {ex.Message}"); }
    }

    /// <summary>처음으로 되감는다.</summary>
    public void Rewind()
    {
        try
        {
            if (_rt != null) _rt.PlaybackSession.Position = TimeSpan.Zero;
            if (_wpf != null) _wpf.Position = TimeSpan.Zero;
        }
        catch (Exception ex) { Note($"되감기 — {ex.Message}"); }
    }

    public void Stop()
    {
        _playing = false;
        try
        {
            _wpf?.Stop();
            if (_rt != null)
            {
                _rt.Pause();
                _rt.PlaybackSession.Position = TimeSpan.Zero;
            }
        }
        catch (Exception ex) { Note($"끊기 — {ex.Message}"); }
    }

    public void Close()
    {
        _playing = false;
        _uri = null;
        try
        {
            _wpf?.Close();
            if (_rt != null) { _rt.Pause(); _rt.Source = null; }
        }
        catch (Exception ex) { Note($"닫기 — {ex.Message}"); }
    }

    private void OpenWpf(Uri uri)
    {
        try
        {
            if (_wpf == null)
            {
                _wpf = new System.Windows.Media.MediaPlayer();
                _wpf.MediaEnded += (_, _) => MediaEnded?.Invoke(this, EventArgs.Empty);
                _wpf.MediaFailed += (_, e) => WpfFailed(e.ErrorException);
            }
            _wpf.Open(uri);
            _wpf.Volume = _volume;
        }
        catch (Exception ex) { WpfFailed(ex); }
    }

    /// <summary>WPF 쪽이 못 틀었다 — 모든 손을 WinRT 로 돌리고, 이 손은 같은 파일을 그쪽으로 다시 연다.</summary>
    private void WpfFailed(Exception? ex)
    {
        Note($"wpf {Describe(ex)}");
        _useWinRt = true;
        DropWpf();
        if (_uri is not { } uri) return;
        bool play = _playing;
        OpenRt(uri);
        if (play) Play();
    }

    private void DropWpf()
    {
        if (_wpf == null) return;
        try { _wpf.Close(); } catch (Exception) { /* 놓기만 한다 */ }
        _wpf = null;
    }

    private void OpenRt(Uri uri)
    {
        DropWpf();   // WPF 쪽에서 갈아탄 손이면 놓는다
        try
        {
            if (_rt == null)
            {
                _rt = new Windows.Media.Playback.MediaPlayer();
                // 시스템 미디어 단추(작업 표시줄 · 볼륨 띄움 창의 재생 단추)에 게임 곡이 걸리지 않게.
                _rt.CommandManager.IsEnabled = false;
                _rt.MediaEnded += (_, _) => _dispatcher.BeginInvoke(() => MediaEnded?.Invoke(this, EventArgs.Empty));
                _rt.MediaFailed += (_, e) =>
                {
                    string why = $"winrt {e.Error} {e.ErrorMessage} (0x{e.ExtendedErrorCode?.HResult:X8})";
                    _dispatcher.BeginInvoke(() => Fail(why));
                };
            }
            _rt.Source = Windows.Media.Core.MediaSource.CreateFromUri(uri);
            _rt.Volume = _volume;
        }
        catch (Exception ex) { Fail($"winrt {Describe(ex)}"); }
    }

    private void Fail(string why)
    {
        Note(why);
        Failed?.Invoke(why);
    }

    private static void Note(string why)
    {
        LastFailure = $"{DateTime.Now:HH:mm:ss} {why}";
        Failures++;
        System.Diagnostics.Debug.WriteLine($"[MediaVoice] {why}");
    }

    private static string Describe(Exception? ex) =>
        ex == null ? "(까닭 없음)" : $"{ex.GetType().Name} 0x{ex.HResult:X8} {ex.Message}";
}
