using System.IO;
using System.Net.Http;

namespace CdsHelper.Support.Local.Helpers;

/// <summary>게임 폴더에 없는 CDS 자료를 공개 CDSX 에셋으로 보충한다.</summary>
public static class CdsAssetPath
{
    private const string ReleaseBase =
        "https://github.com/Kyeongrok/cds-helper/releases/download/map-assets/";

    /// <summary>다운로드한 CDSX 에셋이 저장되는 실행 폴더.</summary>
    public static string DownloadDirectory => AppDomain.CurrentDomain.BaseDirectory;

    /// <summary>
    /// 고친 판(<c>asset/cds</c>)을 가장 먼저, 그다음 게임 폴더의 원본, 없으면 실행 폴더 캐시 또는 릴리즈 에셋을 쓴다.
    /// </summary>
    public static string Resolve(string gameDirectory, string cdsName)
    {
        if (Edited(cdsName) is { } edited) return edited;

        string original = Path.Combine(gameDirectory, cdsName);
        if (File.Exists(original)) return original;

        string assetName = string.Equals(cdsName, "WORLD.CDS", StringComparison.OrdinalIgnoreCase)
            ? "world.cdsx"
            : string.Equals(Path.GetExtension(cdsName), ".P", StringComparison.OrdinalIgnoreCase)
                ? Path.GetFileNameWithoutExtension(cdsName).ToUpperInvariant() + ".PX"
                : Path.GetFileNameWithoutExtension(cdsName).ToUpperInvariant() + ".CDSX";
        string local = Path.Combine(DownloadDirectory, assetName);
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

    /// <summary>고친 판이 드는 폴더 이름 — 저장소 <c>asset/cds</c> 이고 앱마다 실행 폴더로 복사된다.</summary>
    public const string EditedFolder = "asset/cds";

    /// <summary>
    /// 편집기에서 고쳐 둔 그 파일(<c>asset/cds/WORLD.CDS</c> 따위). 없으면 null.
    /// </summary>
    /// <remarks>
    /// 소스 옆을 먼저 본다 — 앱은 <c>asset</c> 을 빌드할 때 제 실행 폴더로 복사하므로, 굽힌 자리만 보면 편집기에서
    /// 고친 것이 다시 빌드하기 전까지 놀이에 안 먹는다. 내놓은 판에서 고친 것은 <c>%APPDATA%</c> 에 든다.
    /// </remarks>
    public static string? Edited(string cdsName)
    {
        foreach (var dir in EditedDirectories())
        {
            var path = Path.Combine(dir, cdsName);
            if (File.Exists(path)) return path;
        }
        return null;
    }

    /// <summary>고친 것을 적을 폴더 — 저장소 안이면 소스 옆 <c>asset/cds</c>, 아니면 <c>%APPDATA%</c>. 없으면 만든다.</summary>
    public static string EditedSaveDirectory()
    {
        var dir = SourceEditedDirectory() ?? UserEditedDirectory;
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static IEnumerable<string> EditedDirectories()
    {
        if (SourceEditedDirectory() is { } near) yield return near;
        yield return UserEditedDirectory;
        yield return Path.Combine(AppContext.BaseDirectory, EditedFolder);
    }

    private static string UserEditedDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CdsHelper", "asset", "cds");

    private static string? _source;
    private static bool _sourceLooked;

    /// <summary><c>.sln</c> 이 있는 저장소 뿌리의 <c>asset/cds</c>. 저장소 밖이면 null.</summary>
    private static string? SourceEditedDirectory()
    {
        if (_sourceLooked) return _source;
        _sourceLooked = true;
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        for (int up = 0; up < 8 && dir != null; up++, dir = dir.Parent)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, "asset")) && dir.EnumerateFiles("*.sln").Any())
                return _source = Path.Combine(dir.FullName, "asset", "cds");
        }
        return null;
    }
}
