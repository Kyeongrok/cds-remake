using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Text.Json;
using CdsHelper.Game.Local.Settings;

namespace CdsHelper.Game.Local.Helpers;

/// <summary>
/// 놀이 통계 — 무엇을 발견하고 어느 도시에 들르고 어느 차림표를 누르고 얼마나 싸우는지 세어 받는 곳으로 보낸다.
/// </summary>
/// <remarks>
/// 받는 곳은 <c>tools/stats-worker</c>(Cloudflare Worker + D1)다. 세는 것은 다섯이고 모드 옵션은 켤 때 한 번 찍는다.
/// <code>
///   discovery  발견물 번호            city    도시 번호
///   menu       「창 제목/줄 글」       battle  sea:Won · sea:Escaped · land:win · land:lose …
///   error      「오류 갈래@클래스.메서드」
/// </code>
/// 셈은 메모리에 모았다가 <see cref="FlushEvery"/> 마다, 그리고 끌 때 한 덩이로 줄(파일)에 세워 뒤에서 보낸다 —
/// 인터넷이 없으면 다음에 켤 때 다시 보낸다. <b>놀이 exe 가 <see cref="Start"/> 를 불러야만</b> 세기 시작하고,
/// 모은다고 한 번 알린 뒤(<see cref="GameSettings.SendStats"/>)에만, 태그로 낸 릴리즈 판에서만 보낸다.
/// 누가 보냈는지는 설치마다 만든 무작위 번호뿐이다 — 이름 · 세이브 · IP 는 안 적는다.
/// </remarks>
public static class PlayStats
{
    /// <summary>받는 곳의 주소를 적어 두는 파일 — 저장소 뿌리에 있고 exe 옆에 실린다.</summary>
    public const string UrlFile = "stats-url.txt";

    /// <summary>모은 셈을 줄에 세우는 사이.</summary>
    private static readonly TimeSpan FlushEvery = TimeSpan.FromMinutes(5);

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

    /// <summary>차림표 줄을 눌렀다 — 열쇠는 「창 제목/줄 글」이다.</summary>
    public static void Menu(string title, string row) =>
        Count("menu", title.Length > 0 ? $"{title}/{row}" : row, null);

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

    private static void Count(string kind, string key, string? name)
    {
        if (!Started) return;
        if (key.Length > MaxKey) key = key[..MaxKey];

        bool due;
        lock (Gate)
        {
            Counts.TryGetValue((kind, key), out var was);
            Counts[(kind, key)] = (name ?? was.Name ?? "", was.Count + 1);
            due = DateTime.UtcNow - _flushedAt >= FlushEvery;
        }
        if (due) Flush();
    }

    /// <summary>
    /// 모은 셈을 줄에 세우고 뒤에서 보낸다. 끌 때도 부른다 — 보내지 못하고 꺼져도 줄에 남아 다음에 간다.
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
        return mods;
    }

    /// <summary>참·거짓이 아니라 단계로 고르는 옵션들.</summary>
    private static readonly HashSet<string> LevelMods =
    [
        nameof(GameSettings.InfoLevel), nameof(GameSettings.SeaRaidScale), nameof(GameSettings.MutinyRate),
        nameof(GameSettings.Resolution), nameof(GameSettings.PortDays),
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
