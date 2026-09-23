using System.IO;
using System.Net.Http;

namespace CdsHelper.Game.Local.Helpers;

/// <summary>CDSX 에셋처럼 고정 릴리즈에서 BGM 곡을 받아 앱 캐시에 저장한다.</summary>
/// <remarks>
/// <b>먼저 쓰는 곡부터 받는다.</b> 타이틀(23) · 도시(10) · 바다(15) 셋을 받으면
/// <see cref="DownloadAsync"/> 가 돌아오고, 나머지는 그 뒤로 뒤에서 하나씩 받는다.
/// 한 벌이 130MB 라 다 받을 때까지 붙잡아 두면 첫 화면이 한참 조용하다.
///
/// 앱을 켤 때 미리 받기(<c>App.PrefetchBgmAsync</c>)와 놀이 창의 받기가 겹칠 수 있어,
/// 한 번에 한 곡만 받고 이미 있는 곡은 건너뛴다.
/// </remarks>
public static class BgmAssetDownloader
{
    private const string ReleaseBase =
        "https://github.com/Kyeongrok/cds-helper/releases/download/bgm-assets/";

    /// <summary>릴리즈에 올라 있는 곡 번호 범위.</summary>
    private const int FirstTrack = 2, LastTrack = 29;

    /// <summary>먼저 받는 곡 — 처음 켜서 차례로 듣게 되는 타이틀 · 도시 · 바다.</summary>
    private static readonly int[] UrgentTracks =
        [BgmPlayer.TitleTrack, BgmPlayer.CityTrack, BgmPlayer.SeaTrack];

    private static readonly HttpClient Client = new() { Timeout = TimeSpan.FromMinutes(10) };

    /// <summary>한 번에 한 곡만 받는다 — 같은 곡을 두 쪽에서 받으면 임시 파일이 겹친다.</summary>
    private static readonly SemaphoreSlim OneAtATime = new(1, 1);

    private static readonly object RestLock = new();
    private static Task? _rest;

    public static string CachePath(int track) =>
        Path.Combine(AppContext.BaseDirectory, "bgm", $"Track{track:D2}.mp3");

    /// <summary>다운로드한 BGM 파일이 저장되는 폴더.</summary>
    public static string CacheDirectory =>
        Path.Combine(AppContext.BaseDirectory, "bgm");

    /// <summary>
    /// 먼저 쓰는 곡 셋을 받고 돌아온다. 나머지는 뒤에서 이어 받는다.
    /// </summary>
    public static async Task<(bool Success, string Error)> DownloadAsync(
        CancellationToken cancellationToken = default)
    {
        try
        {
            foreach (int track in UrgentTracks)
                await FetchAsync(track, cancellationToken);

            StartRest();

            return File.Exists(CachePath(BgmPlayer.RequiredTrack))
                ? (true, "")
                : (false, $"Track{BgmPlayer.RequiredTrack:D2}.mp3를 다운로드하지 못했습니다.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return (false, "다운로드가 취소되었습니다.");
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }

    /// <summary>나머지 곡을 뒤에서 받기 시작한다. 이미 돌고 있으면 그대로 둔다.</summary>
    private static void StartRest()
    {
        lock (RestLock)
        {
            if (_rest is { IsCompleted: false }) return;
            _rest = Task.Run(RestAsync);
        }
    }

    private static async Task RestAsync()
    {
        for (int track = FirstTrack; track <= LastTrack; track++)
        {
            if (UrgentTracks.Contains(track)) continue;
            try
            {
                await FetchAsync(track, CancellationToken.None);
            }
            catch (Exception ex)
            {
                // 한 곡이 안 받아져도 다음 곡으로 간다 — 다음에 켤 때 빠진 곡만 다시 받는다.
                System.Diagnostics.Debug.WriteLine($"[BGM] Track{track:D2} 받기 실패: {ex.Message}");
            }
        }
    }

    /// <summary>한 곡을 받는다. 이미 있으면 아무 일도 안 한다.</summary>
    private static async Task FetchAsync(int track, CancellationToken cancellationToken)
    {
        string target = CachePath(track);
        if (File.Exists(target)) return;

        await OneAtATime.WaitAsync(cancellationToken);
        try
        {
            if (File.Exists(target)) return;   // 기다리는 사이 딴 쪽이 받았다

            string tempDirectory = Path.Combine(CacheDirectory, ".part");
            Directory.CreateDirectory(tempDirectory);
            string temp = Path.Combine(tempDirectory, $"Track{track:D2}.mp3");

            using var response = await Client.GetAsync(
                $"{ReleaseBase}Track{track:D2}.mp3",
                HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            response.EnsureSuccessStatusCode();
            await using (var input = await response.Content.ReadAsStreamAsync(cancellationToken))
            await using (var output = File.Create(temp))
                await input.CopyToAsync(output, cancellationToken);
            File.Move(temp, target, overwrite: true);
        }
        finally
        {
            OneAtATime.Release();
        }
    }
}
