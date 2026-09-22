using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace CdsHelper.Game.Local.Helpers;

/// <summary>
/// 발견물 정지그림 파일 자리를 찾는다 — <b>올려 둔 것(<c>asset/dstill</c>)을 먼저</b>, 없으면
/// 게임 폴더의 <c>DSTILL.CDS</c>(<see cref="DiscoveryStills"/>) 를 본다.
/// </summary>
/// <remarks>
/// <see cref="MovieFiles"/> 와 같은 결이다. 원본 그림은 번호로만 가리키는 Ls12 묶음이라
/// 사람이 새로 그린 그림을 끼울 자리가 없었다 — 파일 하나(png)를 <c>D{번호}.png</c> 이름으로
/// 올려 두면 그 번호를 쓰는 자리(<see cref="DiscoveryDialog"/>)가 원본보다 먼저 그것을 쓴다.
/// <see cref="DiscoveryPackage"/> 가 발견물을 내보내고 불러올 때 이 자리를 쓴다.
/// </remarks>
public static class DiscoveryStillFiles
{
    /// <summary>올려 둔 그림이 든 곳.</summary>
    public const string AssetDirectory = "asset/dstill";

    /// <summary>원본 DSTILL.CDS 그림 수(볼트 <c>89</c> 확인). 새로 더하는 것은 이 뒤 번호를 받는다.</summary>
    public const int OriginalCount = 84;

    /// <summary>파일 고르기 창의 거르개.</summary>
    public const string OpenFilter = "그림 파일|*.png;*.jpg;*.jpeg;*.bmp|모든 파일|*.*";

    private static string Name(int n) => $"D{n}.png";

    /// <summary>올려 둔 그림 번호들(원본 자리를 갈아 끼운 것도 든다).</summary>
    public static SortedSet<int> UploadedNumbers()
    {
        var numbers = new SortedSet<int>();
        foreach (var dir in AssetDirectories())
        {
            if (!Directory.Exists(dir)) continue;
            foreach (var path in Directory.EnumerateFiles(dir, "D*.png"))
            {
                var name = Path.GetFileNameWithoutExtension(path);
                if (name.Length > 1 && int.TryParse(name.AsSpan(1), out int n) && n >= 0)
                    numbers.Add(n);
            }
        }
        return numbers;
    }

    /// <summary>
    /// 새 그림이 받을 번호 — <see cref="OriginalCount"/> 부터, 올린 것도 없고 <paramref name="taken"/>
    /// (발견물 표가 이미 쓰는 번호)에도 없는 첫 번호.
    /// </summary>
    public static int NextFreeNumber(IEnumerable<int> taken)
    {
        var used = UploadedNumbers();
        used.UnionWith(taken);
        int n = OriginalCount;
        while (used.Contains(n)) n++;
        return n;
    }

    /// <summary>올려 둔 그림 파일 자리. 없으면 null.</summary>
    public static string? Uploaded(int n)
    {
        foreach (var dir in AssetDirectories())
        {
            var path = Path.Combine(dir, Name(n));
            if (File.Exists(path)) return path;
        }
        return null;
    }

    /// <summary>올려 둔 그림을 BGRA 로 푼다. 없으면 null.</summary>
    public static uint[]? TryGetBgra(int n, out int width, out int height)
    {
        width = height = 0;
        if (Uploaded(n) is not { } path) return null;
        try
        {
            var decoder = new PngBitmapDecoder(new Uri(path), BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
            var converted = ToBgra32(decoder.Frames[0]);
            width = converted.PixelWidth;
            height = converted.PixelHeight;
            var bgra = new uint[width * height];
            converted.CopyPixels(bgra, width * 4, 0);
            return bgra;
        }
        catch (Exception e) when (e is IOException or NotSupportedException or FileFormatException)
        {
            return null;
        }
    }

    private static BitmapSource ToBgra32(BitmapSource src) =>
        src.Format == PixelFormats.Bgra32 ? src : new FormatConvertedBitmap(src, PixelFormats.Bgra32, null, 0);

    /// <summary>파일 하나를 그 번호로 올린다(png 아니면 다시 구워 png 로 맞춘다).</summary>
    /// <returns>올린 자리.</returns>
    public static string Upload(string source, int n)
    {
        var dir = UploadDirectory();
        var target = Path.Combine(dir, Name(n));
        var temp = target + ".part";

        if (Path.GetExtension(source).Equals(".png", StringComparison.OrdinalIgnoreCase))
            File.Copy(source, temp, overwrite: true);
        else
            SavePng(PortraitImport.Load(source) ?? throw new InvalidDataException($"{source} 를 못 읽었습니다"), temp);

        Remove(n);
        File.Move(temp, target, overwrite: true);
        return target;
    }

    /// <summary>이미 읽어 둔 BGRA 그림을 그 번호로 올린다 — <see cref="DiscoveryPackage"/> 가 불러올 때 쓴다.</summary>
    public static string Upload(uint[] bgra, int width, int height, int n)
    {
        var dir = UploadDirectory();
        var target = Path.Combine(dir, Name(n));
        var temp = target + ".part";

        var bmp = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, bgra, width * 4);
        SavePng(bmp, temp);

        Remove(n);
        File.Move(temp, target, overwrite: true);
        return target;
    }

    private static void SavePng(BitmapSource source, string path)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(source));
        using var fs = File.Create(path);
        encoder.Save(fs);
    }

    /// <summary>그 번호로 올려 둔 것을 지운다.</summary>
    public static void Remove(int n)
    {
        foreach (var dir in AssetDirectories())
        {
            var path = Path.Combine(dir, Name(n));
            if (File.Exists(path)) File.Delete(path);
        }
    }

    /// <summary>올릴 때 쓸 폴더 — 소스 옆 <c>asset/dstill</c>, 저장소가 아니면 굽힌 자리. 없으면 만든다.</summary>
    public static string UploadDirectory()
    {
        var dir = SourceDirectory() ?? UserDirectory;
        Directory.CreateDirectory(dir);
        return dir;
    }

    /// <summary>찾아볼 올린 폴더들. 소스 옆이 먼저다.</summary>
    private static IEnumerable<string> AssetDirectories()
    {
        if (SourceDirectory() is { } near) yield return near;
        yield return UserDirectory;
        yield return Path.Combine(AppContext.BaseDirectory, AssetDirectory);
    }

    /// <summary>내놓은 판에서 올린 그림이 드는 곳 — <c>%APPDATA%\CdsHelper\asset\dstill</c>.</summary>
    private static string UserDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CdsHelper", "asset", "dstill");

    private static string? _source;
    private static bool _sourceLooked;

    /// <summary><c>.sln</c> 이 있는 저장소 뿌리의 <c>asset/dstill</c>. 저장소 밖이면 null.</summary>
    private static string? SourceDirectory()
    {
        if (_sourceLooked) return _source;
        _sourceLooked = true;

        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        for (int up = 0; up < 8 && dir != null; up++, dir = dir.Parent)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, "asset")) && dir.EnumerateFiles("*.sln").Any())
                return _source = Path.Combine(dir.FullName, "asset", "dstill");
        }
        return null;
    }
}
