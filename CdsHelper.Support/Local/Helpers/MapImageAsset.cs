using System.IO;
using System.Net.Http;

namespace CdsHelper.Support.Local.Helpers;

/// <summary>
/// 세계지도 원본 이미지는 용량이 커서 저장소에 포함하지 않는다(.gitignore).
/// 실행 폴더에 없으면 GitHub 릴리스에서 내려받아 사용한다.
/// </summary>
public static class MapImageAsset
{
    public const string FileName = "대항해시대3-지도(발견물-이름-기준).jpg";
    public const string DownloadUrl = "https://github.com/Kyeongrok/cds-helper/releases/download/map-assets/3-.-.-.jpg";

    public static string FilePath => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, FileName);

    public static bool Exists => File.Exists(FilePath);

    /// <summary>
    /// 지도 이미지를 내려받는다. 성공하면 true.
    /// 17MB라 기본 타임아웃(100초)으로는 느린 회선에서 실패할 수 있어 넉넉히 잡는다.
    /// 중간에 끊겨 잘린 파일이 남으면 이후 File.Exists가 true라 영영 복구되지 않으므로
    /// 임시 파일에 받은 뒤 마지막에 옮긴다.
    /// </summary>
    public static async Task<bool> TryDownloadAsync(CancellationToken token = default)
    {
        var tempPath = FilePath + ".part";
        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
            var data = await client.GetByteArrayAsync(DownloadUrl, token);
            await File.WriteAllBytesAsync(tempPath, data, token);
            File.Move(tempPath, FilePath, overwrite: true);
            return true;
        }
        catch
        {
            try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch { }
            return false;
        }
    }
}
