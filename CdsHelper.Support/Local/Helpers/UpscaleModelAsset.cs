using System.IO;
using System.Net.Http;

namespace CdsHelper.Support.Local.Helpers;

/// <summary>
/// 배 그림 리마스터에 쓰는 업스케일 모델을 고정 릴리즈 <c>model-assets</c> 에서 받는다.
/// </summary>
/// <remarks>
/// 모델은 Rebout Blend 4x(LyonHrt, CC BY-NC-SA 4.0, ESRGAN 64nf 23nb)를 ONNX 로 바꾼 것이다
/// (<c>tools/convert_upscaler_onnx.py</c>). 64MB 라 앱에 싣지 않고 쓸 사람만 단추로 받는다.
/// 받은 것은 <c>%LOCALAPPDATA%\CdsHelper\model</c> 에 둔다 — 판이 바뀌어도 남는다.
/// </remarks>
public static class UpscaleModelAsset
{
    public const string FileName = "4x-ReboutBlend.onnx";

    /// <summary>화면에 적는 모델 이름과 출처.</summary>
    public const string Credit = "Rebout Blend 4x — LyonHrt, CC BY-NC-SA 4.0";

    private const string ReleaseBase =
        "https://github.com/Kyeongrok/cds-remake/releases/download/model-assets/";

    public static string CacheDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CdsHelper", "model");

    public static string CachePath => Path.Combine(CacheDirectory, FileName);

    public static bool Exists => File.Exists(CachePath);

    private static readonly HttpClient Client = new() { Timeout = TimeSpan.FromMinutes(10) };

    /// <summary>모델을 받는다 — 옆에 임시로 써 두고 다 받으면 제 이름으로 옮긴다. 받은 몫(0~1)을 알린다.</summary>
    public static async Task DownloadAsync(IProgress<double>? progress = null, CancellationToken token = default)
    {
        Directory.CreateDirectory(CacheDirectory);
        string temp = CachePath + ".part";

        try
        {
            using var response = await Client.GetAsync(
                ReleaseBase + FileName, HttpCompletionOption.ResponseHeadersRead, token);
            response.EnsureSuccessStatusCode();
            long? total = response.Content.Headers.ContentLength;

            await using (var input = await response.Content.ReadAsStreamAsync(token))
            await using (var output = File.Create(temp))
            {
                var buffer = new byte[1 << 16];
                long got = 0;
                int read;
                while ((read = await input.ReadAsync(buffer, token)) > 0)
                {
                    await output.WriteAsync(buffer.AsMemory(0, read), token);
                    got += read;
                    if (total > 0) progress?.Report((double)got / total.Value);
                }
            }

            File.Move(temp, CachePath, overwrite: true);
        }
        catch
        {
            try { if (File.Exists(temp)) File.Delete(temp); } catch { /* 다음에 덮어쓴다 */ }
            throw;
        }
    }
}
