using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace CdsHelper.Game.Local.Helpers;

/// <summary>
/// 발견물 <b>움직이는 그림</b>(DISCOVER.CDS 자리) 파일을 찾는다 — 올려 둔 장 무더기를 먼저,
/// 없으면 게임 폴더의 <c>DISCOVER.CDS</c>(<see cref="DiscoveryClips"/>) 를 본다.
/// </summary>
/// <remarks>
/// <see cref="DiscoveryStillFiles"/> 와 같은 결이다. 한 편이 png 여러 장(<c>000.png</c>,
/// <c>001.png</c>, …)이 든 폴더 하나 — <c>asset/dclip/D{번호}/</c> 다.
/// </remarks>
public static class DiscoveryClipFiles
{
    /// <summary>올려 둔 편이 든 곳.</summary>
    public const string AssetDirectory = "asset/dclip";

    /// <summary>원본 DISCOVER.CDS 편 수(볼트 <c>82</c> 확인). 새로 더하는 것은 이 뒤 번호를 받는다.</summary>
    public const int OriginalCount = 29;

    private static string FolderName(int n) => $"D{n}";

    /// <summary>올려 둔 편 번호들.</summary>
    public static SortedSet<int> UploadedNumbers()
    {
        var numbers = new SortedSet<int>();
        foreach (var dir in AssetDirectories())
        {
            if (!Directory.Exists(dir)) continue;
            foreach (var sub in Directory.EnumerateDirectories(dir, "D*"))
            {
                var name = Path.GetFileName(sub);
                if (name.Length > 1 && int.TryParse(name.AsSpan(1), out int n) && n >= 0
                    && Directory.EnumerateFiles(sub, "*.png").Any())
                    numbers.Add(n);
            }
        }
        return numbers;
    }

    /// <summary>새 편이 받을 번호 — <see cref="OriginalCount"/> 부터, 안 쓰는 첫 번호.</summary>
    public static int NextFreeNumber(IEnumerable<int> taken)
    {
        var used = UploadedNumbers();
        used.UnionWith(taken);
        int n = OriginalCount;
        while (used.Contains(n)) n++;
        return n;
    }

    /// <summary>올려 둔 편의 폴더. 없으면 null.</summary>
    public static string? UploadedFolder(int n)
    {
        foreach (var dir in AssetDirectories())
        {
            var sub = Path.Combine(dir, FolderName(n));
            if (Directory.Exists(sub) && Directory.EnumerateFiles(sub, "*.png").Any()) return sub;
        }
        return null;
    }

    /// <summary>올려 둔 편의 장들을 BGRA 로 푼다. 없으면 null — 장마다 크기가 같다고 본다.</summary>
    public static uint[][]? Frames(int n, out int width, out int height)
    {
        width = height = 0;
        if (UploadedFolder(n) is not { } dir) return null;
        var files = Directory.GetFiles(dir, "*.png").OrderBy(f => f, StringComparer.Ordinal).ToArray();
        if (files.Length == 0) return null;

        var frames = new uint[files.Length][];
        for (int i = 0; i < files.Length; i++)
        {
            var decoder = new PngBitmapDecoder(new Uri(files[i]), BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
            var src = decoder.Frames[0];
            BitmapSource converted = src.Format == PixelFormats.Bgra32 ? src : new FormatConvertedBitmap(src, PixelFormats.Bgra32, null, 0);
            if (i == 0) { width = converted.PixelWidth; height = converted.PixelHeight; }
            var bgra = new uint[converted.PixelWidth * converted.PixelHeight];
            converted.CopyPixels(bgra, converted.PixelWidth * 4, 0);
            frames[i] = bgra;
        }
        return frames;
    }

    /// <summary>장 목록을 그 번호로 올린다 — <see cref="DiscoveryPackage"/> 가 불러올 때 쓴다.</summary>
    public static void Upload(IReadOnlyList<(uint[] Bgra, int Width, int Height)> frames, int n)
    {
        var dir = Path.Combine(UploadDirectory(), FolderName(n));
        var temp = dir + ".part";
        if (Directory.Exists(temp)) Directory.Delete(temp, true);
        Directory.CreateDirectory(temp);

        for (int i = 0; i < frames.Count; i++)
        {
            var (bgra, w, h) = frames[i];
            var bmp = BitmapSource.Create(w, h, 96, 96, PixelFormats.Bgra32, null, bgra, w * 4);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bmp));
            using var fs = File.Create(Path.Combine(temp, $"{i:000}.png"));
            encoder.Save(fs);
        }

        Remove(n);
        Directory.Move(temp, dir);
    }

    /// <summary>그 번호로 올려 둔 편을 지운다.</summary>
    public static void Remove(int n)
    {
        foreach (var dir in AssetDirectories())
        {
            var sub = Path.Combine(dir, FolderName(n));
            if (Directory.Exists(sub)) Directory.Delete(sub, true);
        }
    }

    /// <summary>올릴 때 쓸 폴더 — 소스 옆 <c>asset/dclip</c>, 저장소가 아니면 굽힌 자리. 없으면 만든다.</summary>
    public static string UploadDirectory()
    {
        var dir = SourceDirectory() ?? UserDirectory;
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static IEnumerable<string> AssetDirectories()
    {
        if (SourceDirectory() is { } near) yield return near;
        yield return UserDirectory;
        yield return Path.Combine(AppContext.BaseDirectory, AssetDirectory);
    }

    private static string UserDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CdsHelper", "asset", "dclip");

    private static string? _source;
    private static bool _sourceLooked;

    private static string? SourceDirectory()
    {
        if (_sourceLooked) return _source;
        _sourceLooked = true;

        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        for (int up = 0; up < 8 && dir != null; up++, dir = dir.Parent)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, "asset")) && dir.EnumerateFiles("*.sln").Any())
                return _source = Path.Combine(dir.FullName, "asset", "dclip");
        }
        return null;
    }
}
