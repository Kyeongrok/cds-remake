using System.IO;

namespace CdsHelper.Support.Local.Helpers;

/// <summary>
/// 게임 폴더에 WORLD.CDS가 없을 때 쓸 세계지도 원본.
/// </summary>
public static class WorldMapAsset
{
    public const string FileName = "world.cdsx";
    public const string DownloadUrl =
        "https://github.com/Kyeongrok/cds-helper/releases/download/map-assets/world.cdsx";

    public static string FilePath => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, FileName);

    /// <summary>WORLD.CDS 대체 파일을 내려받고, 성공하면 저장된 경로를 돌려준다.</summary>
    public static string? EnsureDownloaded()
    {
        if (File.Exists(FilePath)) return FilePath;
        string path = CdsAssetPath.Resolve("", "WORLD.CDS");
        return File.Exists(path) ? path : null;
    }
}
