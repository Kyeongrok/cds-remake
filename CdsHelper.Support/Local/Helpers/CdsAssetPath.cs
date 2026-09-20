using System.IO;
using System.Net.Http;

namespace CdsHelper.Support.Local.Helpers;

/// <summary>게임 폴더에 없는 CDS 자료를 공개 CDSX 에셋으로 보충한다.</summary>
public static class CdsAssetPath
{
    private const string ReleaseBase =
        "https://github.com/Kyeongrok/cds-helper/releases/download/map-assets/";

    /// <summary>게임 폴더의 원본을 우선하고, 없으면 실행 폴더 캐시 또는 릴리즈 에셋을 쓴다.</summary>
    public static string Resolve(string gameDirectory, string cdsName)
    {
        string original = Path.Combine(gameDirectory, cdsName);
        if (File.Exists(original)) return original;

        string assetName = string.Equals(cdsName, "WORLD.CDS", StringComparison.OrdinalIgnoreCase)
            ? "world.cdsx"
            : string.Equals(Path.GetExtension(cdsName), ".P", StringComparison.OrdinalIgnoreCase)
                ? Path.GetFileNameWithoutExtension(cdsName).ToUpperInvariant() + ".PX"
                : Path.GetFileNameWithoutExtension(cdsName).ToUpperInvariant() + ".CDSX";
        string local = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, assetName);
        if (File.Exists(local)) return local;

        string temp = local + ".part";
        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
            byte[] data = client.GetByteArrayAsync(ReleaseBase + assetName).GetAwaiter().GetResult();
            File.WriteAllBytes(temp, data);
            File.Move(temp, local, overwrite: true);
            return local;
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or TaskCanceledException)
        {
            try { if (File.Exists(temp)) File.Delete(temp); } catch { }
            return original;
        }
    }
}
