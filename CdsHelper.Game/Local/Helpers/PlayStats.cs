using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Text.Json;
using CdsHelper.Game.Local.Settings;

namespace CdsHelper.Game.Local.Helpers;

/// <summary>
/// 놀이 통계 — 무엇을 발견하고 어느 도시에 들르고 어디서 어디로 며칠 걸려 가고 얼마나 싸우는지 세어 받는 곳으로 보낸다.
/// </summary>
/// <remarks>
/// 받는 곳은 <c>tools/stats-worker</c>(Cloudflare Worker + D1)다. 모드 옵션은 켤 때 한 번 찍는다.
/// <code>
///   discovery     발견물 번호                  city    도시 번호
///   voyage        「떠난 도시>닿은 도시」 횟수    battle  sea:Won · sea:Escaped · land:win · land:lose …
///   voyage_days   같은 열쇠, 걸린 날수의 합      error   「오류 갈래@클래스.메서드」
///   voyage_turns  같은 열쇠, 뱃머리를 돌린 횟수의 합
///   nav           네비게이션 — pick:quick · helm:steady · end:arrived · trip:auto …
/// </code>
/// 받는 쪽은 열쇠마다 수를 <b>더하기만</b> 한다 — 그래서 항해는 횟수 · 날수 · 선회를 갈래 셋으로 갈라 보내고,
/// 평균은 보는 쪽이 나눠서 낸다. 차림표 줄(menu)은 예전에 셌는데 이제 안 센다.
/// 셈은 메모리에 모았다가 <b>도시를 나설 때</b>(출항 · 성문)와 끌 때 한 덩이로 줄(파일)에 세워 뒤에서 보낸다
/// (나서지 않고 오래 있으면 <see cref="FlushEvery"/> 마다) —
/// 인터넷이 없으면 다음에 켤 때 다시 보낸다. <b>놀이 exe 가 <see cref="Start"/> 를 불러야만</b> 세기 시작하고,
/// 모은다고 한 번 알린 뒤(<see cref="GameSettings.SendStats"/>)에만, 태그로 낸 릴리즈 판에서만 보낸다.
/// 누가 보냈는지는 설치마다 만든 무작위 번호뿐이다 — 이름 · 세이브 · IP 는 안 적는다.
/// </remarks>
public static class PlayStats
{
    /// <summary>받는 곳의 주소를 적어 두는 파일 — 저장소 뿌리에 있고 exe 옆에 실린다.</summary>
    public const string UrlFile = "stats-url.txt";

    /// <summary>
    /// 도시를 나서지 않고 이만큼 지나면 그때까지 모은 셈을 줄에 세운다 — 바다나 도시에 오래 머물다 강제로 꺼져도
    /// 다 잃지 않게 둔 안전판이다. 예전에는 5분마다 보냈는데 덩이가 너무 잦아 받는 곳의 하루 한도가 찼다.
    /// </summary>
    private static readonly TimeSpan FlushEvery = TimeSpan.FromMinutes(30);

    /// <summary>열쇠·이름의 가장 긴 길이. 받는 쪽도 같은 데서 자른다.</summary>
    private const int MaxKey = 96;

    /// <summary>오래 못 보낸 것이 한없이 쌓이지 않게 새 것부터 이만큼만 보낸다.</summary>
    private const int MaxQueued = 50;

    private static readonly string Folder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CdsHelper");

    private static string InstallPath => Path.Combine(Folder, "install-id.txt");
    private static string QueueFolder => Path.Combine(Folder, "stats-queue");

    private static readonly object Gate = new();
    private static readonly Dictionary<(string Kind, string Key), (string Name, int Count)> Counts = [];

    private static string _url = "";
    private static string _version = "";
    private static bool _release, _modsSent;
    private static DateTime _flushedAt = DateTime.UtcNow;
    private static int _uploading;

    /// <summary>세기 시작했는가 — 놀이 exe 만 켠다. 개발도구에서 돌린 놀이는 안 센다.</summary>
    public static bool Started { get; private set; }

    /// <summary>이 판이 보낼 수 있는 판인가(회원의 답과 무관) — 받는 곳 주소가 있고 태그로 낸 릴리즈 판일 때.</summary>
    public static bool CanUploadBuild => Started && _url.Length > 0 && _release;

    private static bool CanUpload => CanUploadBuild && GameSettings.SendStats == true;

    /// <summary>
    /// 놀이를 켤 때 한 번 — 받는 곳 주소와 판 번호를 읽고, 지난번에 못 보낸 것을 뒤에서 보낸다.
    /// </summary>
    /// <param name="app">놀이 exe 의 어셈블리. 손으로 빌드한 판은 판 번호가 csproj 값(1.12.0) 그대로라 릴리즈로 안 친다.</param>
    public static void Start(Assembly app)
    {
        _url = ReadUrl();
        var version = app.GetName().Version;
        _version = version is null ? "" : ReleaseNotes.Trim(version).ToString();
        // 릴리즈 노트에 대목이 있는 판만 릴리즈다 — 릴리즈 빌드만 태그 번호로 판을 박는다(release.yml).
        _release = version is not null && ReleaseNotes.Since(ReleaseNotes.Read(), "", version).Length > 0;
        Started = true;
        UploadInBackground();
    }

    private static string ReadUrl()
    {
        try
        {
            string path = Path.Combine(AppContext.BaseDirectory, UrlFile);
            if (!File.Exists(path)) return "";
            string url = File.ReadLines(path).Select(l => l.Trim())
                             .FirstOrDefault(l => l.Length > 0 && !l.StartsWith('#')) ?? "";
            return url.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ? url.TrimEnd('/') : "";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return ""; }
    }

    /// <summary>발견물 하나를 새로 발견했다.</summary>
    public static void Found(int id, string? name) => Count("discovery", id.ToString(), name);

    /// <summary>도시에 들어섰다.</summary>
    public static void City(int id, string? name) => Count("city", id.ToString(), name);

    /// <summary>
    /// 배로 한 도시를 떠나 다른 도시에 닿았다 — 어디서 어디로, 며칠 걸려, 뱃머리를 몇 번 돌려 갔는지.
    /// </summary>
    /// <param name="auto">가는 동안 자동항해를 한 번이라도 걸었는지.</param>
    public static void Voyage(int from, string fromName, int to, string toName, int days, int turns, bool auto)
    {
        string key = $"{from}>{to}", name = $"{fromName} > {toName}";
        Count("voyage", key, name);
        Count("voyage_days", key, name, Math.Max(0, days));
        Count("voyage_turns", key, name, Math.Max(0, turns));
        Count("nav", auto ? "trip:auto" : "trip:manual", null);
    }

    /// <summary>
    /// 네비게이션(자동항해)에서 무엇을 골랐고 어떻게 끝났는지 — <c>pick:quick</c> · <c>helm:steady</c> ·
    /// <c>from:city</c> · <c>end:arrived</c> 따위.
    /// </summary>
    public static void Nav(string what) => Count("nav", what, null);

    /// <summary>해전 한 판이 끝났다.</summary>
    public static void SeaBattle(string outcome) => Count("battle", $"sea:{outcome}", null);

    /// <summary>육상전 한 판이 끝났다.</summary>
    public static void LandBattle(bool won) => Count("battle", won ? "land:win" : "land:lose", null);

    /// <summary>
    /// 잡지 못한 오류가 났다 — <b>오류 갈래와 난 자리(클래스.메서드)만</b> 적는다. 오류 글은 파일 경로 따위가
    /// 섞일 수 있어 안 싣는다. 곧 꺼질 수도 있으니 바로 줄에 세운다.
    /// </summary>
    public static void Error(Exception ex)
    {
        var at = ex.TargetSite;
        Count("error", $"{ex.GetType().Name}@{at?.DeclaringType?.Name}.{at?.Name}", null);
        Flush();
    }

    private static void Count(string kind, string key, string? name, int by = 1)
    {
        if (!Started) return;
        if (key.Length > MaxKey) key = key[..MaxKey];
        if (name is { Length: > MaxKey }) name = name[..MaxKey];

        bool due;
        lock (Gate)
        {
            Counts.TryGetValue((kind, key), out var was);
            Counts[(kind, key)] = (name ?? was.Name ?? "", was.Count + by);
            due = DateTime.UtcNow - _flushedAt >= FlushEvery;
        }
        if (due) Flush();
    }

    /// <summary>
    /// 모은 셈을 줄에 세우고 뒤에서 보낸다. 도시를 나설 때와 끌 때 부른다 — 보내지 못하고 꺼져도 줄에 남아 다음에 간다.
    /// </summary>
    public static void Flush()
    {
        if (!Started) return;

        List<object> rows;
        lock (Gate)
        {
            _flushedAt = DateTime.UtcNow;
            if (Counts.Count == 0) return;
            rows = [.. Counts.Select(p => (object)new { kind = p.Key.Kind, key = p.Key.Key, name = p.Value.Name, n = p.Value.Count })];
            Counts.Clear();
        }
        if (!CanUpload) return;   // 보내지 않을 판 · 답은 셈을 버린다 — 쌓아 두지 않는다

        try
        {
            string batch = Guid.NewGuid().ToString();
            var body = new
            {
                v = 1,
                batch,
                install = InstallId(),
                version = _version,
                counts = rows,
                // 모드 옵션은 켠 뒤 첫 덩이에만 싣는다 — 받는 쪽이 설치마다 마지막 값으로 덮는다.
                mods = _modsSent ? null : Mods(),
            };
            Directory.CreateDirectory(QueueFolder);
            File.WriteAllText(Path.Combine(QueueFolder, batch + ".json"), JsonSerializer.Serialize(body));
            _modsSent = true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return; }

        UploadInBackground();
    }

    /// <summary>
    /// 지금 켜 둔 옵션들 — 설정의 참·거짓 값 전부와 단계가 있는 몇 가지다. 글로 된 값(글쇠 이름 따위)은 안 싣는다.
    /// </summary>
    private static Dictionary<string, int> Mods()
    {
        var mods = new Dictionary<string, int>();
        foreach (var p in typeof(GameSettings).GetProperties(BindingFlags.Public | BindingFlags.Static))
        {
            if (!p.CanRead || p.GetIndexParameters().Length > 0 || p.Name == nameof(GameSettings.SendStats)) continue;
            try
            {
                if (p.PropertyType == typeof(bool)) mods[p.Name] = (bool)p.GetValue(null)! ? 1 : 0;
                else if (p.PropertyType == typeof(int) && LevelMods.Contains(p.Name)) mods[p.Name] = (int)p.GetValue(null)!;
            }
            catch (TargetInvocationException) { /* 못 읽는 값은 건너뛴다 */ }
        }

        // 화면 해상도(주 모니터의 화면 점)와 배율(%) — 창이 화면 밖으로 삐져나오는 자리를 가늠하려고 싣는다.
        var (width, height, scale) = Screen();
        if (width > 0)
        {
            mods["ScreenWidth"] = width;
            mods["ScreenHeight"] = height;
            mods["ScreenScale"] = scale;
        }
        return mods;
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern uint GetDpiForSystem();

    /// <summary>주 모니터의 폭 · 높이(화면 점)와 배율(100 이 100%). 못 읽으면 0 이다.</summary>
    private static (int Width, int Height, int Scale) Screen()
    {
        try
        {
            double dpi = GetDpiForSystem();
            if (dpi <= 0) dpi = 96;
            // WPF 는 화면 크기를 DIP 로 내므로 배율을 곱해 화면 점으로 되돌린다.
            return ((int)Math.Round(System.Windows.SystemParameters.PrimaryScreenWidth * dpi / 96),
                    (int)Math.Round(System.Windows.SystemParameters.PrimaryScreenHeight * dpi / 96),
                    (int)Math.Round(dpi * 100 / 96));
        }
        catch (Exception ex) when (ex is EntryPointNotFoundException or DllNotFoundException or InvalidOperationException)
        {
            return (0, 0, 0);
        }
    }

    /// <summary>참·거짓이 아니라 단계로 고르는 옵션들.</summary>
    private static readonly HashSet<string> LevelMods =
    [
        nameof(GameSettings.InfoLevel), nameof(GameSettings.SeaRaidScale), nameof(GameSettings.MutinyRate),
        nameof(GameSettings.Resolution), nameof(GameSettings.PortDays),
        nameof(GameSettings.ModPreset),      // 권장 옵션 — 0 오리지널 · 1 초보 · 2 중수 · 3 고수
    ];

    /// <summary>설치마다 하나인 무작위 번호 — 누구인지는 모르고 같은 설치인지만 안다.</summary>
    private static string InstallId()
    {
        if (File.Exists(InstallPath) && Guid.TryParse(File.ReadAllText(InstallPath).Trim(), out var known))
            return known.ToString();
        string id = Guid.NewGuid().ToString();
        Directory.CreateDirectory(Folder);
        File.WriteAllText(InstallPath, id);
        return id;
    }

    /// <summary>줄에 선 덩이를 뒤에서 보낸다 — 받은 것은 지우고, 안 되면 조용히 남겨 둔다.</summary>
    private static void UploadInBackground()
    {
        if (!CanUpload || !Directory.Exists(QueueFolder) || Interlocked.Exchange(ref _uploading, 1) == 1) return;
        string url = _url + "/v1/play";
        _ = Task.Run(async () =>
        {
            try
            {
                using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
                var files = new DirectoryInfo(QueueFolder).GetFiles("*.json")
                                .OrderByDescending(f => f.LastWriteTimeUtc).ToList();
                foreach (var old in files.Skip(MaxQueued)) old.Delete();
                foreach (var file in files.Take(MaxQueued))
                {
                    using var content = new StringContent(await File.ReadAllTextAsync(file.FullName),
                                                          Encoding.UTF8, "application/json");
                    using var reply = await http.PostAsync(url, content);
                    // 받는 쪽이 「못 쓸 글」이라 하면(4xx) 다시 보내도 같다 — 지운다. 그 밖의 실패는 다음에 다시.
                    if (reply.IsSuccessStatusCode || (int)reply.StatusCode is >= 400 and < 500) file.Delete();
                    else break;
                }
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException
                                             or IOException or UnauthorizedAccessException)
            {
                System.Diagnostics.Debug.WriteLine($"[PlayStats] {ex.GetType().Name}: {ex.Message}");
            }
            finally { Interlocked.Exchange(ref _uploading, 0); }
        });
    }
}
