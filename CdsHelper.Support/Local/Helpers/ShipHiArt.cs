using System.IO;

namespace CdsHelper.Support.Local.Helpers;

/// <summary>
/// 붙박이 배 그림벌의 <b>고해상도</b> 그림(<c>ship_{방향}_hd.png</c>)이 드는 자리.
/// </summary>
/// <remarks>
/// 편집기가 리마스터해 적고 게임이 읽는다 — 두 앱이 같은 자리를 봐야 해서 여기 둔다.
/// 찾는 차례는 <see cref="CdsAssetPath.Edited"/> 와 같다: 저장소 안이면 소스 옆 <c>asset</c> 을 먼저 보고
/// (다시 빌드하지 않아도 게임에 먹는다), 그다음 <c>%APPDATA%</c>, 끝으로 굽힌 자리다.
/// 등록해 넣은 배는 제 폴더(<see cref="ShipRegistry.FolderOf"/>)에 같은 이름으로 둔다.
/// </remarks>
public static class ShipHiArt
{
    public static string FileName(int direction) => $"ship_{direction}_hd.png";

    /// <summary>그 그림벌 폴더(<c>ship-g2</c> 따위)의 그 방향 고해상도 그림. 없으면 null.</summary>
    public static string? Find(string assetFolder, int direction)
    {
        foreach (var dir in Directories(assetFolder))
        {
            var path = Path.Combine(dir, FileName(direction));
            if (File.Exists(path)) return path;
        }
        return null;
    }

    /// <summary>리마스터한 것을 적을 폴더 — 저장소 안이면 소스 옆 <c>asset</c>, 아니면 <c>%APPDATA%</c>. 없으면 만든다.</summary>
    public static string SaveDirectory(string assetFolder)
    {
        var dir = SourceAsset() is { } near ? Path.Combine(near, assetFolder) : UserDirectory(assetFolder);
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static IEnumerable<string> Directories(string assetFolder)
    {
        if (SourceAsset() is { } near) yield return Path.Combine(near, assetFolder);
        yield return UserDirectory(assetFolder);
        yield return Path.Combine(AppContext.BaseDirectory, "asset", assetFolder);
    }

    private static string UserDirectory(string assetFolder) => UserDataFolder.Combine("asset", assetFolder);

    private static string? _source;
    private static bool _sourceLooked;

    /// <summary><c>.sln</c> 이 있는 저장소 뿌리의 <c>asset</c>. 저장소 밖이면 null.</summary>
    private static string? SourceAsset()
    {
        if (_sourceLooked) return _source;
        _sourceLooked = true;
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        for (int up = 0; up < 8 && dir != null; up++, dir = dir.Parent)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, "asset")) && dir.EnumerateFiles("*.sln").Any())
                return _source = Path.Combine(dir.FullName, "asset");
        }
        return null;
    }
}
