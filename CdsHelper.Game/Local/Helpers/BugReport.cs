using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Windows;
using CdsHelper.Support.Local.Helpers;

namespace CdsHelper.Game.Local.Helpers;

/// <summary>
/// 버그 리포트 — 왼쪽 위 메뉴의 「버그 리포트」 창(<see cref="UI.Views.BugReportDialog"/>)에 쓴 글을
/// 게임 통계를 받는 곳(<c>tools/stats-worker</c> 의 <c>POST /v1/bug</c>)으로 보낸다. 대시보드 「버그 리포트」 탭에서 본다.
/// 원본에 없는 것이다.
/// </summary>
/// <remarks>
/// 사용자가 보내기를 눌렀으니 「통계 보내기」 답과 상관없이 보낸다. 다만 태그로 낸 릴리즈 판만 보낸다
/// (<see cref="PlayStats.UploadUrl"/>) — 개발 빌드의 시험 글이 받는 곳에 쌓이지 않게. 못 보내면 줄(폴더)에 두었다가 다음에 켤 때 다시 보낸다.
/// 시험은 <c>CDS_STATS_URL</c>(로컬 <c>wrangler dev</c> 주소도 된다)을 주면 그쪽으로 보낸다.
/// </remarks>
public static class BugReport
{
    public const int MaxText = 8000;

    /// <summary>오류 기록을 이만큼만 싣는다 — 받는 쪽 context 한도(32KB)에 모드 값까지 함께 들어야 한다.</summary>
    private const int LogTailChars = 16000, LogTailLines = 200;

    /// <summary>오래 못 보낸 것이 한없이 쌓이지 않게 새 것부터 이만큼만 둔다.</summary>
    private const int MaxQueued = 30;

    /// <summary>
    /// 한글을 그대로 적는다 — 기본값은 <c>\uXXXX</c> 로 적어 글이 여섯 배로 불어나, 긴 글이 받는 쪽 몸통 한도(48KB)에 걸려 버려진다.
    /// </summary>
    private static readonly JsonSerializerOptions Json = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    private static readonly SemaphoreSlim Gate = new(1, 1);

    private static string QueueFolder => UserDataFolder.Combine("bug-queue");

    /// <summary>보낸 결과.</summary>
    public enum Result { Sent, Later, NotThisBuild }

    private static string Url
    {
        get
        {
            string over = Environment.GetEnvironmentVariable("CDS_STATS_URL")?.Trim() ?? "";
            if (over.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
                || over.StartsWith("http://127.0.0.1", StringComparison.OrdinalIgnoreCase)
                || over.StartsWith("http://localhost", StringComparison.OrdinalIgnoreCase))
                return over.TrimEnd('/');
            return OffscreenTest.Isolated ? "" : PlayStats.UploadUrl;
        }
    }

    /// <summary>글 하나를 줄에 세우고 보낸다.</summary>
    public static async Task<Result> Submit(string text, Dictionary<string, object?> context)
    {
        string url = Url;
        if (url.Length == 0) return Result.NotThisBuild;
        text = text.Trim();
        if (text.Length > MaxText) text = text[..MaxText];
        try
        {
            Directory.CreateDirectory(QueueFolder);
            var body = new { v = 1, install = PlayStats.InstallId(), version = PlayStats.Version, text, context };
            // 이름은 적은 때 — 오래된 것부터 보낸다.
            string name = $"{DateTime.UtcNow:yyyyMMddHHmmssfff}-{Guid.NewGuid():N}.json";
            await File.WriteAllTextAsync(Path.Combine(QueueFolder, name), JsonSerializer.Serialize(body, Json));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return Result.Later; }
        return await SendQueued(url) ? Result.Sent : Result.Later;
    }

    /// <summary>지난번에 못 보낸 것을 뒤에서 보낸다 — 켤 때.</summary>
    public static void ResendInBackground()
    {
        string url = Url;
        if (url.Length > 0 && Directory.Exists(QueueFolder)) _ = Task.Run(() => SendQueued(url));
    }

    /// <summary>줄을 한 번 훑어 보낸다 — 남은 것 없이 다 보냈으면 true.</summary>
    private static async Task<bool> SendQueued(string url)
    {
        await Gate.WaitAsync();
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            var files = new DirectoryInfo(QueueFolder).GetFiles("*.json").OrderBy(f => f.Name).ToList();
            foreach (var old in files.SkipLast(MaxQueued)) old.Delete();
            foreach (var file in files.TakeLast(MaxQueued))
            {
                using var content = new StringContent(await File.ReadAllTextAsync(file.FullName), Encoding.UTF8, "application/json");
                using var reply = await http.PostAsync(url + "/v1/bug", content);
                int code = (int)reply.StatusCode;
                // 받는 쪽이 「못 쓸 글」이라 하면(4xx) 다시 보내도 같다 — 지운다. 너무 잦다(429) · 받는 쪽 고장(5xx)은 다음에 다시.
                if (reply.IsSuccessStatusCode || code is >= 400 and < 500 and not 429) file.Delete();
                else return false;
            }
            return true;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException or UnauthorizedAccessException)
        {
            System.Diagnostics.Debug.WriteLine($"[BugReport] {ex.GetType().Name}: {ex.Message}");
            return false;
        }
        finally { Gate.Release(); }
    }

    /// <summary>게임이 저절로 붙이는 것 — 받는 곳의 <c>bug_reports.context</c> 에 JSON 으로 들어간다.</summary>
    /// <param name="where">타이틀 · 도시 · 육지 · 바다.</param>
    /// <param name="city">도시 안이면 그 도시.</param>
    /// <param name="date">게임 날짜. 타이틀이면 null.</param>
    /// <param name="nation">나라.</param>
    /// <param name="flagship">기함 선체.</param>
    public static Dictionary<string, object?> Context(string where, string? city, string? date, string? nation, string? flagship)
    {
        var (width, height, scale) = PlayStats.Screen();
        var context = new Dictionary<string, object?>
        {
            ["game"] = PlayStats.Version.Length > 0 ? "v" + PlayStats.Version : "(개발 빌드)",
            ["os"] = $"{Environment.OSVersion.VersionString}{(Environment.Is64BitOperatingSystem ? " x64" : "")}",
            ["lang"] = Lang.Current.Code,
            ["screen"] = $"{width}x{height} {scale}%",
            ["where"] = where,
            ["city"] = city,
            ["date"] = date,
            ["nation"] = nation,
            ["flagship"] = flagship,
            ["windows"] = OpenWindows(),
            ["mods"] = PlayStats.Mods(),
        };
        if (CrashLogTail() is { } crash) { context["crashLogAt"] = crash.At; context["crashLog"] = crash.Tail; }
        return context;
    }

    /// <summary>떠 있는 창의 갈래 이름 — 무슨 창에서 일어난 일인지 가늠한다.</summary>
    private static List<string> OpenWindows() =>
        [.. Application.Current.Windows.OfType<Window>().Where(w => w.IsVisible).Select(w => w.GetType().Name).Take(12)];

    /// <summary>가장 최근 오류 기록(<see cref="CrashReport"/>)의 끝. 없으면 null.</summary>
    private static (string At, string Tail)? CrashLogTail()
    {
        try
        {
            var folder = new DirectoryInfo(CrashReport.Folder);
            if (!folder.Exists) return null;
            var latest = folder.GetFiles("crash-*.txt").OrderByDescending(f => f.Name).FirstOrDefault();
            if (latest == null) return null;
            var lines = File.ReadAllLines(latest.FullName);
            string text = Scrub(string.Join('\n', lines.TakeLast(LogTailLines)));
            if (text.Length > LogTailChars) text = text[^LogTailChars..];
            return (latest.LastWriteTimeUtc.ToString("yyyy-MM-ddTHH:mm:ssZ"), text);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return null; }
    }

    /// <summary>개인 정보를 지운다 — 자료 폴더 · 임시 폴더 · 사용자 폴더 경로와 사용자 이름을 자리 이름으로 바꾼다.</summary>
    private static string Scrub(string text)
    {
        static string Trim(string path) => Path.TrimEndingDirectorySeparator(path);
        foreach (var (path, label) in new[]
                 {
                     (Trim(UserDataFolder.Roaming), "%CDS%"), (Trim(Path.GetTempPath()), "%TEMP%"),
                     (Trim(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)), "%USERPROFILE%"),
                 })
            if (path.Length > 3) text = text.Replace(path, label, StringComparison.OrdinalIgnoreCase);
        string user = Environment.UserName;
        if (user.Length >= 3) text = text.Replace(user, "<user>", StringComparison.OrdinalIgnoreCase);
        return text;
    }

    /// <summary>창 아래에 보이는 요약 — 무엇을 같이 보내는지.</summary>
    public static string Summary(Dictionary<string, object?> context)
    {
        string? Word(string key) => context[key] is string { Length: > 0 } value ? value : null;
        string place = string.Join(" · ", new[]
        {
            Lang.T(Word("where") ?? ""), Word("city") is { } city ? Lang.T(city) : null, Word("date"),
            Word("nation") is { } nation ? Lang.T(nation) : null, Word("flagship") is { } hull ? Lang.T(hull) : null,
        }.Where(s => !string.IsNullOrEmpty(s)));
        int on = context["mods"] is Dictionary<string, int> mods ? mods.Count(m => m.Value != 0) : 0;
        var lines = new List<string>
        {
            $"{Lang.T("게임")} {context["game"]} · {context["os"]}",
            $"{Lang.T("화면")} {context["screen"]} · {context["lang"]}",
            $"{Lang.T("지금 자리")}: {place}",
            $"{Lang.T("켠 모드")}: {on}{Lang.T("개")}",
        };
        if (context.TryGetValue("crashLogAt", out var at)) lines.Add($"{Lang.T("오류 기록의 끝")}: {at}");
        lines.Add(Lang.T("이름 · 세이브 · 폴더 경로는 보내지 않습니다. 설치마다 하나인 무작위 번호만 같이 갑니다."));
        return string.Join(Environment.NewLine, lines);
    }
}
