using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Threading;
using CdsHelper.Game.Local.Settings;
using CdsHelper.Support.Local.Models;
using MoonSharp.Interpreter;
using CdsHelper.Support.Local.Helpers;

namespace CdsHelper.Game.UI.AddOns;

/// <summary>
/// 지도 창이 애드온에 넘겨 주는 것들 — 게임 · 돌고 있는지 · 아래 띠 · 배 자리 · 육지에 섰는지 · 게임 창 열기.
/// </summary>
/// <param name="Open">
/// <c>OpenWindow(이름)</c> 이 여는 게임 창 — 단축키로 여는 것과 같은 창이다. 모르는 이름이면 null.
/// </param>
public sealed record AddOnStage(
    FrameworkElement Anchor,
    Func<Engine.Game> Game,
    Func<bool> Running,
    Action<string> Say,
    Func<(double Lat, double Lon)?> Position,
    Func<bool> OnLand,
    Func<string, Action?> Open);

/// <summary>
/// 와우처럼 Lua 애드온을 읽어 지도 위에 제 패널을 띄우게 한다 — 원본에 없는 것이다(모드 「애드온」).
/// </summary>
/// <remarks>
/// <code>
///   자리       %APPDATA%\CdsHelper\AddOns\이름\이름.toc   (## Title · ## Notes · ## SavedVariables · 읽을 .lua 줄)
///   저장 변수  %APPDATA%\CdsHelper\AddOns\SavedVariables\이름.lua — 애드온을 갈아 끼워도 남는다
///   막는 것    MoonSharp 의 SoftSandbox — 파일 · 프로그램 실행 · load · require 가 없다
///   읽는 값    화면에서 볼 수 있는 것만(AddOnApi) — 게임을 고치는 함수는 없다
///   이벤트     하루 지남 · 금화 · 입항 · 출항은 100ms 마다 값을 대 보고 낸다. 발견 · 해전 · 육상전은 그 자리가 부른다
/// </code>
/// 애드온 하나가 오류를 열 번 내면 그 애드온만 멈춘다. 끝없는 고리는 막지 못한다 — 게임이 멈춘다.
/// 하드코어 랭킹에는 모드처럼 「AddOns」로 적힌다(<see cref="Engine.HardcoreRules"/>).
/// </remarks>
public static class AddOnHost
{
    /// <summary>애드온 폴더.</summary>
    public static string Root { get; private set; } = UserDataFolder.Combine("AddOns");

    /// <summary>저장 변수 · 패널 자리를 두는 폴더 — 애드온 폴더를 지우고 새로 깔아도 남게 따로 둔다.</summary>
    internal static string SavedDir => Path.Combine(Root, "SavedVariables");

    /// <summary>애드온이 낸 글과 오류를 적는 파일 — 켤 때마다 새로 쓴다.</summary>
    public static string LogPath => Path.Combine(Root, "addons.log");

    /// <summary>앱과 같이 깔리는 예시 애드온 자리 — 처음 보는 예시만 애드온 폴더로 베껴 준다(<see cref="SeedSamples"/>).</summary>
    private static string SampleDir => Path.Combine(AppContext.BaseDirectory, "AddOnSamples");

    private static readonly List<AddOn> _addons = [];

    /// <summary>읽어 들인 애드온들.</summary>
    public static IReadOnlyList<AddOn> Loaded => _addons;

    internal static AddOnStage? Stage { get; private set; }

    private static DispatcherTimer? _timer;
    private static readonly Stopwatch _clock = Stopwatch.StartNew();
    private static double _lastTick;
    private static bool _on;

    /// <summary>지도가 보여 패널을 띄워도 될 때인지 — 지도 창이 <c>SyncOverlay</c> 에서 알려 준다.</summary>
    internal static bool Visible => _mapShown && !_covered;

    private static bool _mapShown, _covered;

    /// <summary>앱이 켜진 뒤 흐른 초 — <c>GetTime()</c> 과 타이머가 쓴다.</summary>
    internal static double Now => _clock.Elapsed.TotalSeconds;

    /// <summary>지도 창이 뜰 때 건다. 모드가 켜져 있으면 곧바로 읽는다.</summary>
    public static void Attach(AddOnStage stage)
    {
        Stage = stage;
        _timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(100) };
        _timer.Tick += (_, _) => Tick();
        _timer.Start();
        GameSettings.Changed += SyncEnabled;
        SyncEnabled();
    }

    /// <summary>지도 창이 닫힐 때 푼다 — 저장 변수를 적고 패널을 걷는다.</summary>
    public static void Detach()
    {
        _timer?.Stop();
        _timer = null;
        GameSettings.Changed -= SyncEnabled;
        Unload();
        _on = false;
        Stage = null;
    }

    /// <summary>모드를 켜고 끄면 그 자리에서 읽거나 걷는다. 딴 설정이 바뀐 것이면 아무 일 없다.</summary>
    private static void SyncEnabled()
    {
        bool want = GameSettings.AddOns;
        if (want == _on) return;
        _on = want;
        if (want) Load();
        else Unload();
    }

    /// <summary>모두 걷고 다시 읽는다 — 애드온 파일을 고친 뒤 게임을 안 끄고 보려고 쓴다.</summary>
    public static void Reload()
    {
        Unload();
        if (_on) Load();
    }

    private static void Load()
    {
        try
        {
            Directory.CreateDirectory(Root);
            SeedSamples();
            File.WriteAllText(LogPath, "");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return; }

        AddOnFrame.LoadPlaces();
        var off = Disabled();
        foreach (var dir in Directory.GetDirectories(Root).OrderBy(d => d, StringComparer.OrdinalIgnoreCase))
        {
            string name = Path.GetFileName(dir);
            if (name.Equals("SavedVariables", StringComparison.OrdinalIgnoreCase)) continue;
            string toc = Path.Combine(dir, name + ".toc");
            if (!File.Exists(toc)) continue;
            var addon = AddOn.Read(name, dir, toc);
            _addons.Add(addon);
            // 끈 애드온도 목록에는 둔다 — 도시락 「애드온」 창이 늘어놓고 다시 켤 수 있게. 돌리지만 않는다.
            if (off.Contains(name)) continue;
            addon.Start();
            Fire("ADDON_LOADED", name);
        }
        Log($"애드온 {_addons.Count}개를 읽었습니다.");
        _player = null;
        _lastTick = Now;
    }

    /// <summary>끈 애드온 이름을 적는 파일 — 한 줄에 하나. 새로 넣은 애드온은 켜진 채로 시작한다.</summary>
    private static string DisabledPath => Path.Combine(SavedDir, "disabled.txt");

    private static HashSet<string> Disabled()
    {
        try
        {
            return File.Exists(DisabledPath)
                ? new HashSet<string>(File.ReadAllLines(DisabledPath).Where(l => l.Length > 0), StringComparer.OrdinalIgnoreCase)
                : new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }
    }

    /// <summary>애드온 하나를 켜거나 끈다 — 적어 두고 모두 다시 읽는다(도시락 「애드온」 창).</summary>
    public static void SetEnabled(string name, bool enabled)
    {
        var off = Disabled();
        if (enabled ? !off.Remove(name) : !off.Add(name)) return;
        try
        {
            Directory.CreateDirectory(SavedDir);
            File.WriteAllLines(DisabledPath, off.Order(StringComparer.OrdinalIgnoreCase));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return; }
        Reload();
    }

    private static void Unload()
    {
        foreach (var addon in _addons) addon.Stop();
        _addons.Clear();
        AddOnFrame.SavePlaces();
    }

    /// <summary>
    /// 앱과 같이 깔린 예시를 베껴 준다 — 예시마다 <b>한 번만</b>이다. 베낀 이름은 <c>SavedVariables\samples.txt</c> 에
    /// 적어 두어, 지운 예시가 다시 살아나지 않고 새 판에 더한 예시만 새로 들어온다. 설명서(README.md)는 늘 새것으로 갈아 둔다.
    /// </summary>
    private static void SeedSamples()
    {
        if (!Directory.Exists(SampleDir)) return;
        string listPath = Path.Combine(SavedDir, "samples.txt");
        var seeded = File.Exists(listPath)
            ? new HashSet<string>(File.ReadAllLines(listPath), StringComparer.OrdinalIgnoreCase)
            : new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        // 예전 판처럼 목록 없이 이미 들어 있는 예시도 베낀 것으로 친다.
        foreach (var dir in Directory.GetDirectories(SampleDir))
            if (Directory.Exists(Path.Combine(Root, Path.GetFileName(dir)))) seeded.Add(Path.GetFileName(dir));

        foreach (var dir in Directory.GetDirectories(SampleDir))
        {
            string name = Path.GetFileName(dir);
            if (!seeded.Add(name)) continue;
            foreach (var file in Directory.GetFiles(dir, "*", SearchOption.AllDirectories))
            {
                string to = Path.Combine(Root, name, Path.GetRelativePath(dir, file));
                Directory.CreateDirectory(Path.GetDirectoryName(to)!);
                if (!File.Exists(to)) File.Copy(file, to);
            }
        }
        foreach (var file in Directory.GetFiles(SampleDir))
            File.Copy(file, Path.Combine(Root, Path.GetFileName(file)), overwrite: true);

        Directory.CreateDirectory(SavedDir);
        File.WriteAllLines(listPath, seeded.Order(StringComparer.OrdinalIgnoreCase));
    }

    /// <summary>패널을 띄울 때인지 바꾼다 — 지도가 가려지거나 창이 뒤로 가면 다 걷는다.</summary>
    public static void SetVisible(bool visible)
    {
        if (_mapShown == visible) return;
        _mapShown = visible;
        foreach (var addon in _addons) addon.SyncFrames();
    }

    /// <summary>지도 창이 옮겨지거나 크기가 바뀌면 패널 자리를 다시 잡는다.</summary>
    public static void Relayout()
    {
        foreach (var addon in _addons) addon.SyncFrames();
    }

    /// <summary>
    /// 이벤트를 낸다 — 그 이벤트를 <c>RegisterEvent</c> 한 패널마다 <c>OnEvent(self, 이벤트, ...)</c> 를 부른다.
    /// </summary>
    public static void Fire(string name, params object?[] args)
    {
        if (_addons.Count == 0) return;
        foreach (var addon in _addons.ToArray()) addon.Dispatch(name, args);
    }

    // ── 값 대 보기 ──────────────────────────────────────────────────────────

    private static Player? _player;
    private static DateTime _date;
    private static int _gold, _city = -1;
    private static string _cityName = "";

    private static void Tick()
    {
        if (_addons.Count == 0 || Stage is not { } stage) return;
        if (stage.Running()) Poll(stage.Game().Player);

        // 지도 창에 딸린 대화 창(모드 · 힌트 · 물음 · 도시 그림 따위)이 떠 있으면 패널을 걷는다 — 팝업이라 그냥 두면 그 창 위에
        // 얹힌다. 대화 창은 모두 GameWindow 다. 지도 위 쪽지(속도 · 항해 일수 · 기능 표)는 그냥 Window 라 안 센다 — 세면
        // 바다에서는 쪽지가 늘 떠 있어 패널이 영영 숨는다.
        bool covered = Window.GetWindow(stage.Anchor) is { } map
                       && map.OwnedWindows.OfType<Views.GameWindow>().Any(w => w.IsVisible);
        if (covered != _covered)
        {
            _covered = covered;
            foreach (var addon in _addons) addon.SyncFrames();
        }

        double now = Now, elapsed = now - _lastTick;
        _lastTick = now;
        foreach (var addon in _addons.ToArray()) addon.Update(now, elapsed);
    }

    private static void Poll(Player p)
    {
        if (!ReferenceEquals(p, _player))
        {
            // 새 게임 · 불러오기 — 지난 값과 대 보지 않고 처음부터 잡는다.
            _player = p;
            _date = p.Date;
            _gold = p.Gold;
            _city = p.CityId;
            _cityName = p.CityName;
            Fire("PLAYER_ENTERING_WORLD");
            return;
        }
        if (p.Date != _date)
        {
            _date = p.Date;
            Fire("DAY_CHANGED", p.Date.Year, p.Date.Month, p.Date.Day);
        }
        if (p.Gold != _gold)
        {
            int was = _gold;
            _gold = p.Gold;
            Fire("GOLD_CHANGED", p.Gold, p.Gold - was);
        }
        if (p.CityId != _city)
        {
            if (_city >= 0) Fire("CITY_LEFT", _city, _cityName);
            _city = p.CityId;
            _cityName = p.CityName;
            if (_city >= 0) Fire("CITY_ENTERED", _city, _cityName);
        }
    }

    // ── 글 ──────────────────────────────────────────────────────────────────

    /// <summary><c>print</c> — 로그 파일에 적고 아래 띠에도 띄운다.</summary>
    internal static void Print(AddOn addon, string text)
    {
        Log($"[{addon.Name}] {text}");
        Stage?.Say($"[{addon.Title}] {text}");
    }

    internal static void Log(string line)
    {
        try { File.AppendAllText(LogPath, $"{DateTime.Now:HH:mm:ss} {line}\n", Encoding.UTF8); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    internal static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
}

/// <summary>
/// 애드온 하나 — 제 Lua 판(<see cref="Script"/>)과 패널 · 타이머를 든다. 애드온끼리는 전역을 나눠 쓰지 않는다.
/// </summary>
public sealed class AddOn
{
    /// <summary>애드온이 오류를 이만큼 내면 멈춘다.</summary>
    private const int MaxErrors = 10;

    private AddOn(string name, string dir) { Name = name; Dir = dir; Title = name; }

    /// <summary>폴더 이름.</summary>
    public string Name { get; }

    /// <summary>폴더 자리.</summary>
    public string Dir { get; }

    /// <summary>toc 의 <c>## Title</c>. 없으면 폴더 이름.</summary>
    public string Title { get; private set; }

    /// <summary>toc 의 <c>## Notes</c>.</summary>
    public string Notes { get; private set; } = "";

    /// <summary>돌고 있는지 — 도시락 「애드온」 창에서 끈 것은 읽기만 하고 안 돌린다.</summary>
    public bool Running => Lua != null;

    /// <summary>멈췄으면 마지막 오류. 돌고 있으면 null.</summary>
    public string? Failed { get; private set; }

    private readonly List<string> _files = [];
    private readonly List<string> _saved = [];
    private readonly List<AddOnFrame> _frames = [];
    private readonly List<LuaTimer> _timers = [];
    private int _errors;

    internal Script? Lua { get; private set; }

    private string SavedPath => Path.Combine(AddOnHost.SavedDir, Name + ".lua");

    /// <summary>toc 를 읽는다 — <c>## 열쇠: 값</c> 줄과 읽을 파일 줄. <c>#</c> 로 시작하는 딴 줄은 주석이다.</summary>
    internal static AddOn Read(string name, string dir, string toc)
    {
        var addon = new AddOn(name, dir);
        foreach (var raw in File.ReadAllLines(toc, Encoding.UTF8))
        {
            string line = raw.Trim();
            if (line.Length == 0) continue;
            if (line.StartsWith("##"))
            {
                int colon = line.IndexOf(':');
                if (colon < 0) continue;
                string key = line[2..colon].Trim(), value = line[(colon + 1)..].Trim();
                if (key.Equals("Title", StringComparison.OrdinalIgnoreCase) && value.Length > 0) addon.Title = value;
                else if (key.Equals("Notes", StringComparison.OrdinalIgnoreCase)) addon.Notes = value;
                else if (key.Equals("SavedVariables", StringComparison.OrdinalIgnoreCase))
                    addon._saved.AddRange(value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
                continue;
            }
            if (line.StartsWith('#')) continue;
            addon._files.Add(line.Replace('\\', '/'));
        }
        return addon;
    }

    /// <summary>Lua 판을 열고 저장 변수 · 파일을 차례로 돌린다.</summary>
    internal void Start()
    {
        var lua = Lua = new Script(CoreModules.Preset_SoftSandbox);
        lua.Options.DebugPrint = text => AddOnHost.Print(this, text);
        AddOnApi.Install(this);

        if (File.Exists(SavedPath)) Run(() => lua.DoString(File.ReadAllText(SavedPath, Encoding.UTF8), null, $"{Name}/SavedVariables"));

        foreach (var file in _files)
        {
            if (Failed != null) break;
            string full = Path.GetFullPath(Path.Combine(Dir, file));
            // 제 폴더 밖 파일은 안 읽는다(../../ 따위).
            if (!full.StartsWith(Path.GetFullPath(Dir) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) || !File.Exists(full))
            {
                Fail($"파일이 없습니다: {file}");
                continue;
            }
            Run(() => lua.DoString(File.ReadAllText(full, Encoding.UTF8), null, $"{Name}/{file}"));
        }
    }

    /// <summary>저장 변수를 적고 패널 · 타이머를 걷는다.</summary>
    internal void Stop()
    {
        Save();
        foreach (var frame in _frames) frame.Close();
        _frames.Clear();
        _timers.Clear();
    }

    private void Save()
    {
        if (_saved.Count == 0 || Lua is not { } lua) return;
        var sb = new StringBuilder();
        foreach (var name in _saved)
            sb.Append(name).Append(" = ").Append(LuaSaved.Write(lua.Globals.Get(name))).Append('\n');
        try
        {
            Directory.CreateDirectory(AddOnHost.SavedDir);
            File.WriteAllText(SavedPath, sb.ToString(), Encoding.UTF8);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AddOnHost.Log($"[{Name}] 저장 변수를 못 적었습니다: {ex.Message}");
        }
    }

    internal AddOnFrame CreateFrame(string? name)
    {
        var frame = new AddOnFrame(this, name);
        _frames.Add(frame);
        return frame;
    }

    internal LuaTimer AddTimer(double seconds, DynValue fn, bool repeat)
    {
        var timer = new LuaTimer(this, AddOnHost.Now + Math.Max(0, seconds), Math.Max(0.05, seconds), fn, repeat);
        _timers.Add(timer);
        return timer;
    }

    internal void Dispatch(string name, object?[] args)
    {
        if (Failed != null) return;
        foreach (var frame in _frames.ToArray()) frame.Dispatch(name, args);
    }

    internal void Update(double now, double elapsed)
    {
        if (Failed != null) return;
        foreach (var timer in _timers.ToArray())
        {
            if (timer.Cancelled) { _timers.Remove(timer); continue; }
            if (now < timer.Due) continue;
            if (timer.Repeat) timer.Due = now + timer.Every;
            else _timers.Remove(timer);
            Call(timer.Fn);
        }
        foreach (var frame in _frames.ToArray()) frame.Update(elapsed);
    }

    internal void SyncFrames()
    {
        foreach (var frame in _frames) frame.Sync();
    }

    /// <summary>Lua 함수를 부른다 — 오류는 잡아 적고, 열 번 넘으면 이 애드온을 멈춘다.</summary>
    internal void Call(DynValue fn, params object?[] args)
    {
        if (Failed != null || Lua is not { } lua || fn.Type is not (DataType.Function or DataType.ClrFunction)) return;
        Run(() => lua.Call(fn, args.Select(a => a is DynValue d ? d : DynValue.FromObject(lua, a)).ToArray()));
    }

    private void Run(Action action)
    {
        try { action(); }
        catch (InterpreterException ex) { Fail(ex.DecoratedMessage ?? ex.Message); }
        catch (Exception ex) { Fail($"{ex.GetType().Name}: {ex.Message}"); }
    }

    private void Fail(string message)
    {
        AddOnHost.Log($"[{Name}] 오류: {message}");
        if (++_errors < MaxErrors && _errors > 1) return;
        if (_errors >= MaxErrors)
        {
            Failed = message;
            foreach (var frame in _frames) frame.Close();
            AddOnHost.Stage?.Say($"애드온 「{Title}」: 오류가 거듭 나서 멈췄습니다. addons.log 를 보세요.");
            return;
        }
        // 첫 오류만 띠에 띄운다 — 틱마다 나는 오류가 띠를 덮지 않게.
        AddOnHost.Stage?.Say($"애드온 「{Title}」 오류: {message}");
    }
}

/// <summary><c>C_Timer.After</c> · <c>C_Timer.NewTicker</c> 가 건 타이머.</summary>
internal sealed class LuaTimer(AddOn owner, double due, double every, DynValue fn, bool repeat)
{
    public AddOn Owner { get; } = owner;
    public double Due { get; set; } = due;
    public double Every { get; } = every;
    public DynValue Fn { get; } = fn;
    public bool Repeat { get; } = repeat;
    public bool Cancelled { get; set; }
}
