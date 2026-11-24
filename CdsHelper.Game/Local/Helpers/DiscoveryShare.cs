using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using CdsHelper.Game.Engine.Table;

namespace CdsHelper.Game.Local.Helpers;

/// <summary>
/// 커스텀 발견물 공유 — 내보내기 파일(<c>.cdsdiscovery</c>)을 통계 받는 곳(<c>tools/stats-worker/src/shares.js</c>)에 올리고,
/// 남이 올린 것의 목록을 받아 내려받는다. 내려받은 파일은 「불러오기」(<see cref="UserDiscoveries.Import"/>)가 그대로 들인다.
/// 원본에 없는 것이다.
/// </summary>
/// <remarks>
/// 받는 곳은 버그 리포트와 같다(<see cref="BugReport.Url"/>) — 태그로 낸 릴리즈 판만 쓰고, 시험은 <c>CDS_STATS_URL</c> 로 돌린다.
/// 누가 올렸는지는 설치 번호(<see cref="PlayStats.InstallId"/>)뿐이고, 같은 발견물을 고쳐 올리거나 지우는 것은 처음 올린 설치만 된다.
/// </remarks>
public static class DiscoveryShare
{
    /// <summary>동영상 한도 — 받는 곳 파일 한도(8MB) 안에 그림 · 대본 · 아이템이 함께 들어야 한다.</summary>
    public const long MaxMovieBytes = 5L * 1024 * 1024;

    /// <summary>파일 하나 한도(받는 곳 <c>MAX_FILE</c> 와 같다).</summary>
    public const long MaxFileBytes = 8L * 1024 * 1024;

    /// <summary>미리보기 그림 한도(받는 곳 <c>MAX_PICTURE</c>). 넘으면 그림 없이 올린다.</summary>
    private const long MaxPictureBytes = 512 * 1024;

    private static readonly JsonSerializerOptions Json = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    /// <summary>받는 곳이 있는 판인지 — 개발 빌드는 없다(<c>CDS_STATS_URL</c> 을 주면 그쪽).</summary>
    public static bool Available => BugReport.Url.Length > 0;

    /// <summary>올라온 발견물 한 줄.</summary>
    public sealed record Shared(string Uuid, string Name, int Category, string Author, string License, string Description,
                                long Size, bool HasMovie, bool HasPicture, string Version, string UpdatedAt, int Revision,
                                int Downloads, bool Mine, bool WantsOfficial);

    /// <summary>판 이력 한 줄 — 몇 판 · 고친 내용 · 크기 · 올린 때.</summary>
    public sealed record Revision(int Rev, string Note, long Size, string CreatedAt);

    /// <summary>
    /// 이 PC 가 아는 공유 상태(발견물 폴더의 <c>share.json</c>) — 내보내기 파일에는 안 담긴다.
    /// </summary>
    /// <param name="Mine">내가 올린 것인지. 거짓이면 「공유 발견물」에서 받은 남의 것이라 고칠 수 없다(복제해서 고친다).</param>
    /// <param name="Rev">올리거나 받은 판.</param>
    /// <param name="Hash">그때의 알맹이 셈(<see cref="ContentHash"/>) — 다르면 올린 뒤 고친 것이 있다.</param>
    public sealed record LocalState(bool Mine, int Rev, string Hash);

    private const string StateFile = "share.json";

    /// <summary>그 발견물의 공유 상태. 올리거나 받은 적 없으면 null.</summary>
    public static LocalState? StateOf(string uuid)
    {
        try
        {
            string path = Path.Combine(UserDiscoveries.Folder, uuid, StateFile);
            return File.Exists(path) ? JsonSerializer.Deserialize<LocalState>(File.ReadAllText(path)) : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { return null; }
    }

    /// <summary>공유 상태를 적는다 — 발견물 폴더가 없으면 안 적는다.</summary>
    public static void SetState(string uuid, LocalState state)
    {
        try
        {
            string dir = Path.Combine(UserDiscoveries.Folder, uuid);
            if (Directory.Exists(dir)) File.WriteAllText(Path.Combine(dir, StateFile), JsonSerializer.Serialize(state, Json));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    /// <summary>공유 상태를 지운다 — 내 공유를 내렸을 때.</summary>
    public static void ClearState(string uuid)
    {
        try { File.Delete(Path.Combine(UserDiscoveries.Folder, uuid, StateFile)); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    /// <summary>받은 남의 발견물인지 — 고칠 수 없다.</summary>
    public static bool IsOthers(string uuid) => StateOf(uuid) is { Mine: false };

    /// <summary>
    /// 알맹이 셈 — discovery.json · 그림 · 동영상 바이트를 묶어 SHA-256. 올린 판과 지금이 같은지 볼 때 쓴다. 못 읽으면 빈 글.
    /// </summary>
    public static string ContentHash(string uuid)
    {
        if (UserDiscoveries.All.FirstOrDefault(l => l.Entry.Uuid == uuid) is not { } loaded) return "";
        try
        {
            using var sha = System.Security.Cryptography.IncrementalHash.CreateHash(System.Security.Cryptography.HashAlgorithmName.SHA256);
            foreach (string? path in new[] { Path.Combine(loaded.Folder, "discovery.json"), loaded.PicturePath, loaded.MoviePath })
            {
                if (path == null) { sha.AppendData([0]); continue; }
                sha.AppendData(File.ReadAllBytes(path));
                sha.AppendData([1]);
            }
            return Convert.ToHexString(sha.GetHashAndReset());
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return ""; }
    }

    /// <summary>내가 올린 것인데 올린 뒤 고친 것이 있는지.</summary>
    public static bool ChangedSinceUpload(string uuid) =>
        StateOf(uuid) is { Mine: true } state && state.Hash.Length > 0 && state.Hash != ContentHash(uuid);

    private static HttpClient Client(int seconds) => new() { Timeout = TimeSpan.FromSeconds(seconds) };

    /// <summary>
    /// 그 발견물을 올린다 — 이미 올린 것이면 새 버전으로 바뀐다. 잘 됐으면 null, 아니면 알릴 까닭.
    /// <paramref name="official"/> 은 올린 분이 정식 버전(게임 기본 발견물)에 넣어도 좋다고 했는지다 — 정식 버전에 넣은 것은
    /// 받는 곳에서 지워 용량을 비운다.
    /// </summary>
    /// <param name="note">고친 내용 — 판 이력에 남는다(200자).</param>
    public static async Task<string?> Upload(string uuid, bool official, string note = "")
    {
        string url = BugReport.Url;
        if (url.Length == 0) return "이 버전에서는 공유할 수 없습니다(개발 빌드).";
        if (UserDiscoveries.All.FirstOrDefault(l => l.Entry.Uuid == uuid) is not { } loaded) return "그 발견물이 없습니다";
        if (loaded.MoviePath is { } movie && new FileInfo(movie).Length > MaxMovieBytes)
            return $"동영상이 {MaxMovieBytes / 1024 / 1024}MB 를 넘습니다 — 줄이거나 빼고 올려 주세요.";

        string temp = Path.Combine(Path.GetTempPath(), $"cds-share-{Guid.NewGuid():N}.cdsdiscovery");
        try
        {
            if (UserDiscoveries.Export(uuid, temp) is { } why) return $"파일을 만들지 못했습니다 — {why}";
            byte[] file = await File.ReadAllBytesAsync(temp);
            if (file.Length > MaxFileBytes) return $"파일이 {MaxFileBytes / 1024 / 1024}MB 를 넘습니다 — 동영상 · 그림을 줄여 주세요.";

            var e = loaded.Entry;
            var meta = new
            {
                v = 1, install = PlayStats.InstallId(), version = PlayStats.Version, uuid = e.Uuid, name = e.Name,
                category = e.Category, author = e.Author, license = e.License,
                description = e.Description.Length > 300 ? e.Description[..300] : e.Description,
                hasMovie = loaded.MoviePath != null, official,
                note = note.Trim().Length > 200 ? note.Trim()[..200] : note.Trim(),
            };
            // 이름은 따옴표로 싼다 — .NET 은 form-data 이름을 따옴표 없이 적는데, Workers 의 formData() 는 그것을 못 읽는다.
            using var form = new MultipartFormDataContent();
            form.Add(new StringContent(JsonSerializer.Serialize(meta, Json), Encoding.UTF8), "\"meta\"");
            var fileContent = new ByteArrayContent(file);
            fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/zip");
            form.Add(fileContent, "\"file\"", "\"discovery.cdsdiscovery\"");
            if (loaded.PicturePath is { } picture && new FileInfo(picture).Length <= MaxPictureBytes)
            {
                var pictureContent = new ByteArrayContent(await File.ReadAllBytesAsync(picture));
                pictureContent.Headers.ContentType = new MediaTypeHeaderValue("image/png");
                form.Add(pictureContent, "\"picture\"", "\"picture.png\"");
            }

            using var http = Client(120);
            using var reply = await http.PostAsync(url + "/v1/shares", form);
            if (reply.IsSuccessStatusCode)
            {
                // 받는 곳이 판 번호를 돌려주면 그것을, 예전 받는 곳이면 목록에서 찾는다.
                int rev = 0;
                try
                {
                    using var doc = JsonDocument.Parse(await reply.Content.ReadAsStringAsync());
                    if (doc.RootElement.TryGetProperty("rev", out var r) && r.ValueKind == JsonValueKind.Number) rev = r.GetInt32();
                }
                catch (JsonException) { }
                if (rev == 0) rev = (await List())?.FirstOrDefault(x => x.Uuid == uuid)?.Revision ?? 0;
                SetState(uuid, new LocalState(true, rev, ContentHash(uuid)));
                return null;
            }
            return (int)reply.StatusCode switch
            {
                403 => "다른 분이 이미 올린 발견물입니다 — 처음 올린 분만 고쳐 올릴 수 있습니다.",
                413 => "파일이 너무 큽니다.",
                429 => "오늘 올릴 수 있는 수를 넘었습니다 — 내일 다시 올려 주세요.",
                503 => ServerReply.Why(reply.StatusCode),
                _ => $"올리지 못했습니다(응답 {(int)reply.StatusCode}).",
            };
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException or UnauthorizedAccessException)
        {
            return $"올리지 못했습니다 — {ex.Message}";
        }
        finally
        {
            try { File.Delete(temp); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
    }

    /// <summary><see cref="List"/> 가 null 을 냈을 때 그 까닭(사용자에게 보일 글).</summary>
    public static string ListFailure { get; private set; } = "";

    /// <summary>올라온 목록(새로 고친 차례). 못 받으면 null — 까닭은 <see cref="ListFailure"/>.</summary>
    public static async Task<List<Shared>?> List()
    {
        string url = BugReport.Url;
        ListFailure = Lang.T("이 버전에서는 공유할 수 없습니다(개발 빌드).");
        if (url.Length == 0) return null;
        try
        {
            using var http = Client(15);
            using var reply = await http.GetAsync($"{url}/v1/shares?install={Uri.EscapeDataString(PlayStats.InstallId())}");
            if (!reply.IsSuccessStatusCode) { ListFailure = ServerReply.Why(reply.StatusCode); return null; }
            using var doc = JsonDocument.Parse(await reply.Content.ReadAsStringAsync());
            var list = new List<Shared>();
            foreach (var s in doc.RootElement.GetProperty("shares").EnumerateArray())
            {
                string Str(string key) => s.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
                long Num(string key) => s.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetInt64() : 0;
                bool Flag(string key) => s.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.True;
                list.Add(new Shared(Str("uuid"), Str("name"), (int)Num("category"), Str("author"), Str("license"), Str("description"),
                                    Num("size"), Flag("has_movie"), Flag("has_picture"), Str("version"), Str("updated_at"),
                                    (int)Num("rev"), (int)Num("downloads"), Flag("mine"), Flag("wants_official")));
            }
            return list;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or KeyNotFoundException or InvalidOperationException)
        {
            System.Diagnostics.Debug.WriteLine($"[DiscoveryShare] list {ex.GetType().Name}: {ex.Message}");
            ListFailure = ServerReply.Why(ex);
            return null;
        }
    }

    /// <summary>판 이력(새 판부터). 못 받으면 null — 이력을 모르는 예전 받는 곳도 null.</summary>
    public static async Task<List<Revision>?> History(string uuid)
    {
        string url = BugReport.Url;
        if (url.Length == 0) return null;
        try
        {
            using var http = Client(15);
            using var reply = await http.GetAsync($"{url}/v1/shares/{uuid}/history");
            if (!reply.IsSuccessStatusCode) return null;
            using var doc = JsonDocument.Parse(await reply.Content.ReadAsStringAsync());
            var list = new List<Revision>();
            foreach (var r in doc.RootElement.GetProperty("revisions").EnumerateArray())
            {
                string Str(string key) => r.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
                long Num(string key) => r.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetInt64() : 0;
                list.Add(new Revision((int)Num("rev"), Str("note"), Num("size"), Str("created_at")));
            }
            return list;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or KeyNotFoundException or InvalidOperationException)
        {
            return null;
        }
    }

    /// <summary>미리보기 그림(PNG 바이트). 없으면 null.</summary>
    public static async Task<byte[]?> Picture(string uuid)
    {
        string url = BugReport.Url;
        if (url.Length == 0) return null;
        try
        {
            using var http = Client(15);
            using var reply = await http.GetAsync($"{url}/v1/shares/{uuid}/picture");
            return reply.IsSuccessStatusCode ? await reply.Content.ReadAsByteArrayAsync() : null;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException) { return null; }
    }

    /// <summary>파일을 내려받아 임시 파일로 둔다 — 그 자리, 못 받으면 null. 다 쓰면 부른 쪽이 지운다.</summary>
    public static async Task<string?> Download(string uuid)
    {
        string url = BugReport.Url;
        if (url.Length == 0) return null;
        try
        {
            using var http = Client(120);
            using var reply = await http.GetAsync($"{url}/v1/shares/{uuid}/file");
            if (!reply.IsSuccessStatusCode) return null;
            string path = Path.Combine(Path.GetTempPath(), $"cds-share-{Guid.NewGuid():N}.cdsdiscovery");
            await File.WriteAllBytesAsync(path, await reply.Content.ReadAsByteArrayAsync());
            return path;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>제가 올린 것을 내린다. 잘 됐으면 true.</summary>
    public static Task<bool> Delete(string uuid) => Post($"/v1/shares/{uuid}/delete", new { install = PlayStats.InstallId() });

    /// <summary>신고한다(설치마다 한 번 센다). 잘 받았으면 true.</summary>
    public static Task<bool> Report(string uuid, string reason) =>
        Post($"/v1/shares/{uuid}/report", new { install = PlayStats.InstallId(), reason });

    private static async Task<bool> Post(string path, object body)
    {
        string url = BugReport.Url;
        if (url.Length == 0) return false;
        try
        {
            using var http = Client(15);
            using var content = new StringContent(JsonSerializer.Serialize(body, Json), Encoding.UTF8, "application/json");
            using var reply = await http.PostAsync(url + path, content);
            return reply.IsSuccessStatusCode;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException) { return false; }
    }
}
