using System.IO;

namespace CdsHelper.Game.Local.Helpers;

/// <summary>
/// 동영상 파일 자리를 찾는다 — <b>올려 둔 것(<c>asset/movie</c>)을 먼저</b>, 없으면 게임 폴더의
/// <c>AVI</c> 를 본다.
/// </summary>
/// <remarks>
/// 원본 AVI 는 옛 코덱이라 안 틀리는 수가 있고, 게임 폴더가 없으면 아예 없다. 그래서
/// 같은 <b>이름 줄기</b>(<c>I44_0000</c>, <c>S03_0001</c>)로 MP4 따위를 <c>asset/movie</c> 에
/// 넣어 두면 그것이 원본 자리를 갈아 끼운다. 확장자는 무엇이든 된다(<see cref="Extensions"/>).
///
/// 올려 둔 폴더는 <b>소스 옆 <c>asset/movie</c> 를 먼저</b> 찾는다 — 앱마다 <c>asset</c> 을 제
/// 출력 폴더로 복사해 가므로, 굽힌 자리만 보면 도구에서 올린 것이 다시 굽기 전까지 놀이에
/// 안 보인다(<see cref="DuelMotions.Path_"/> 와 같은 까닭). 내놓은 판이면 굽힌 자리를 쓴다.
/// </remarks>
public static class MovieFiles
{
    /// <summary>올려 둔 동영상이 든 곳.</summary>
    public const string AssetDirectory = "asset/movie";

    /// <summary>원본 동영상이 든 게임 폴더 밑 자리.</summary>
    public const string GameFolder = "AVI";

    /// <summary>올려 둘 수 있는 꼴. 앞의 것이 먼저 잡힌다.</summary>
    public static readonly string[] Extensions = [".mp4", ".m4v", ".wmv", ".mov", ".avi"];

    /// <summary>파일 고르기 창의 거르개.</summary>
    public const string OpenFilter =
        "동영상 파일|*.mp4;*.m4v;*.wmv;*.mov;*.avi|모든 파일|*.*";

    /// <summary>발견물 동영상 줄기. <c>AVI\I{번호:00}_0000.AVI</c> 다.</summary>
    public static string DiscoveryStem(int movie) => $"I{movie:00}_0000";

    /// <summary>원본 발견물 동영상 수(<c>I00</c>~<c>I69</c>). 새로 더하는 것은 이 뒤 번호를 받는다.</summary>
    public const int OriginalDiscoveryMovies = 70;

    /// <summary>올려 둔 발견물 동영상 번호들(<c>I{번호}_0000.*</c>). 원본 자리를 갈아 끼운 것도 든다.</summary>
    public static SortedSet<int> UploadedDiscoveryNumbers()
    {
        var numbers = new SortedSet<int>();
        foreach (var dir in AssetDirectories())
        {
            if (!Directory.Exists(dir)) continue;
            foreach (var path in Directory.EnumerateFiles(dir, "I*_0000.*"))
            {
                var name = Path.GetFileNameWithoutExtension(path);
                if (!Extensions.Contains(Path.GetExtension(path).ToLowerInvariant())) continue;
                if (int.TryParse(name[1..^5], out int n) && n >= 0) numbers.Add(n);
            }
        }
        return numbers;
    }

    /// <summary>
    /// 새 동영상이 받을 번호 — <see cref="OriginalDiscoveryMovies"/> 부터, 올린 것도 없고 <paramref name="taken"/>
    /// (발견물 표가 이미 쓰는 번호)에도 없는 첫 번호.
    /// </summary>
    public static int NextFreeDiscoveryNumber(IEnumerable<int> taken)
    {
        var used = UploadedDiscoveryNumbers();
        used.UnionWith(taken);
        int n = OriginalDiscoveryMovies;
        while (used.Contains(n)) n++;
        return n;
    }

    /// <summary>
    /// 선체 번호 차례(<c>0x004FC1E0</c>). 동영상 <c>S00</c>~<c>S07</c> 이 이 차례와 짝이다.
    /// </summary>
    public static readonly List<string> Hulls =
        ["코구", "카라벨", "대형카라벨", "카락", "대형카락", "중카락", "갤리온", "다우"];

    /// <summary>선체 동영상 줄기. <c>AVI\S{선체:02}_0001.AVI</c> 다.</summary>
    public static string HullStem(int hull) => $"S{hull:00}_0001";

    /// <summary>
    /// <b>엔딩</b> 동영상 줄기 — <c>AVI\END.AVI</c>(<c>0x0054A1C8</c>).
    /// </summary>
    /// <remarks>
    /// 세계일주를 보고하고 나면 게임이 이것을 튼다(<c>0x0045B8F0</c>). 앞뒤로 색표를 서른·예순
    /// 걸음에 걸쳐 어둡혔다 밝히는데, 우리 재생기는 그 자리를 검은 바탕으로 덮어 대신한다.
    /// </remarks>
    public const string EndingStem = "END";

    /// <summary>
    /// 틀 파일을 찾는다. 올려 둔 것 → 게임 폴더 차례고, 둘 다 없으면 null.
    /// </summary>
    public static string? Resolve(string? gameDirectory, string stem) =>
        Uploaded(stem) ?? Original(gameDirectory, stem);

    /// <summary>올려 둔 파일. 없으면 null.</summary>
    public static string? Uploaded(string stem)
    {
        foreach (var dir in AssetDirectories())
            foreach (var ext in Extensions)
            {
                var path = Path.Combine(dir, stem + ext);
                if (File.Exists(path)) return path;
            }
        return null;
    }

    /// <summary>게임 폴더의 원본 파일. 없으면 null.</summary>
    public static string? Original(string? gameDirectory, string stem)
    {
        if (string.IsNullOrEmpty(gameDirectory)) return null;
        var path = Path.Combine(gameDirectory, GameFolder, stem + ".AVI");
        return File.Exists(path) ? path : null;
    }

    /// <summary>
    /// 올릴 때 쓸 폴더 — 소스 옆 <c>asset/movie</c>, 저장소가 아니면 굽힌 자리. 없으면 만든다.
    /// </summary>
    public static string UploadDirectory()
    {
        // 내놓은 판은 <b>%APPDATA%</b> 에 쓴다 — 단일 파일 exe 는 굽힌 자리가 임시 풀림 폴더라 거기 쓰면 판이 바뀔 때 사라진다.
        var dir = SourceDirectory() ?? UserDirectory;
        Directory.CreateDirectory(dir);
        return dir;
    }

    /// <summary>
    /// 파일 하나를 그 줄기로 올린다. 같은 줄기로 올려 둔 것은(확장자가 달라도) 먼저 지운다 —
    /// 안 그러면 앞 확장자의 옛 파일이 계속 잡힌다.
    /// </summary>
    /// <returns>올린 자리.</returns>
    public static string Upload(string source, string stem)
    {
        var ext = Path.GetExtension(source).ToLowerInvariant();
        if (!Extensions.Contains(ext))
            throw new InvalidDataException($"{ext} 는 올릴 수 없는 꼴입니다 — {string.Join(" ", Extensions)}");

        var dir = UploadDirectory();
        var target = Path.Combine(dir, stem + ext);
        if (string.Equals(Path.GetFullPath(source), Path.GetFullPath(target), StringComparison.OrdinalIgnoreCase))
            return target;

        // 새 것을 옆에 먼저 써 두고 옛 것을 지운다 — 복사가 깨져도 옛 것은 남는다.
        var temp = target + ".part";
        File.Copy(source, temp, overwrite: true);
        Remove(stem);
        File.Move(temp, target, overwrite: true);
        return target;
    }

    /// <summary>그 줄기로 올려 둔 것을 모두 지운다(소스 옆과 굽힌 자리 둘 다).</summary>
    public static void Remove(string stem)
    {
        foreach (var dir in AssetDirectories())
            foreach (var ext in Extensions)
            {
                var path = Path.Combine(dir, stem + ext);
                if (File.Exists(path)) File.Delete(path);
            }
    }

    /// <summary>찾아볼 올린 폴더들. 소스 옆이 먼저다.</summary>
    private static IEnumerable<string> AssetDirectories()
    {
        if (SourceDirectory() is { } near) yield return near;
        yield return UserDirectory;
        yield return Path.Combine(AppContext.BaseDirectory, AssetDirectory);
    }

    /// <summary>내놓은 판에서 올린 동영상이 드는 곳 — <c>%APPDATA%\CdsHelpersset\movie</c>.</summary>
    private static string UserDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CdsHelper", "asset", "movie");

    private static string? _source;
    private static bool _sourceLooked;

    /// <summary><c>.sln</c> 이 있는 저장소 뿌리의 <c>asset/movie</c>. 저장소 밖이면 null.</summary>
    private static string? SourceDirectory()
    {
        if (_sourceLooked) return _source;
        _sourceLooked = true;

        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        for (int up = 0; up < 8 && dir != null; up++, dir = dir.Parent)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, "asset")) && dir.EnumerateFiles("*.sln").Any())
                return _source = Path.Combine(dir.FullName, "asset", "movie");
        }
        return null;
    }
}
