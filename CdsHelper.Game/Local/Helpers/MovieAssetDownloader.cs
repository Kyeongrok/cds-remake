using System.IO;
using System.Net.Http;

namespace CdsHelper.Game.Local.Helpers;

/// <summary>
/// 원본 동영상(AVI 를 MP4 로 바꾼 것)을 고정 릴리즈 <c>movie-assets</c> 에서 <b>뒤에서 하나씩</b> 받는다.
/// </summary>
/// <remarks>
/// 원본 AVI 는 Indeo 5 코덱이라 요즘 윈도에서는 안 틀리고, 게임 폴더가 없으면 아예 없다. 그래서
/// 같은 이름 줄기(<c>I28_0000.mp4</c>)로 바꿔 릴리즈에 올려 두고 게임을 켤 때 빠진 것만 받는다.
///
/// <b>이미 갈아 끼운 것(<see cref="MovieFiles.Uploaded"/>)은 안 받는다</b> — 새로 만든 동영상이 있으면 그것이 먼저다.
/// 받은 것은 <c>%LOCALAPPDATA%\CdsHelper\movie</c> 에 둔다. 단일 파일 exe 는 굽힌 자리가 임시 풀림
/// 폴더라 거기 두면 판이 바뀔 때 사라진다.
/// </remarks>
public static class MovieAssetDownloader
{
    private const string ReleaseBase =
        "https://github.com/Kyeongrok/cds-remake/releases/download/movie-assets/";

    /// <summary>받아 둔 동영상이 드는 곳.</summary>
    public static string CacheDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CdsHelper", "movie");

    /// <summary>받아 둔 그 줄기의 파일 자리(있든 없든).</summary>
    public static string CachePath(string stem) => Path.Combine(CacheDirectory, stem + ".mp4");

    /// <summary>
    /// 릴리즈에 올라 있는 줄기 전부 — 로고·오프닝을 앞에 두고 발견물 70 · 선체 8 · 엔딩 차례로 받는다.
    /// </summary>
    public static IEnumerable<string> Stems()
    {
        yield return MovieFiles.LogoStem;
        yield return MovieFiles.OpeningStem;
        for (int n = 0; n < MovieFiles.OriginalDiscoveryMovies; n++) yield return MovieFiles.DiscoveryStem(n);
        for (int h = 0; h < MovieFiles.Hulls.Count; h++) yield return MovieFiles.HullStem(h);
        yield return MovieFiles.EndingStem;
    }

    private static readonly HttpClient Client = new() { Timeout = TimeSpan.FromMinutes(5) };

    private static readonly object Lock = new();
    private static Task? _running;

    /// <summary>
    /// 빠진 동영상을 뒤에서 받기 시작한다. 이미 돌고 있으면 그대로 둔다. 기다리지 않는다.
    /// </summary>
    public static void StartBackground()
    {
        lock (Lock)
        {
            if (_running is { IsCompleted: false }) return;
            _running = Task.Run(FetchMissingAsync);
        }
    }

    private static async Task FetchMissingAsync()
    {
        foreach (string stem in Stems())
        {
            if (MovieFiles.Uploaded(stem) != null || File.Exists(CachePath(stem))) continue;
            try
            {
                await FetchAsync(stem);
            }
            catch (Exception ex)
            {
                // 하나가 안 받아져도 다음으로 간다 — 다음에 켤 때 빠진 것만 다시 받는다.
                System.Diagnostics.Debug.WriteLine($"[동영상] {stem} 받기 실패: {ex.Message}");
            }
        }
    }

    /// <summary>하나를 받는다 — 옆에 임시로 써 두고 다 받으면 제 이름으로 옮긴다.</summary>
    private static async Task FetchAsync(string stem)
    {
        string target = CachePath(stem);
        string partDirectory = Path.Combine(CacheDirectory, ".part");
        Directory.CreateDirectory(partDirectory);
        string temp = Path.Combine(partDirectory, stem + ".mp4");

        using var response = await Client.GetAsync(ReleaseBase + stem + ".mp4", HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();
        await using (var input = await response.Content.ReadAsStreamAsync())
        await using (var output = File.Create(temp))
            await input.CopyToAsync(output);
        File.Move(temp, target, overwrite: true);
    }
}
