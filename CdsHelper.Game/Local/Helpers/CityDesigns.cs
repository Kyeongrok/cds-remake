using System.IO;
using System.Text;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CdsHelper.Support.Local.Helpers;

namespace CdsHelper.Game.Local.Helpers;

/// <summary>
/// 도시 그림 <b>디자인</b> — 226곳이 아홉 가지 그림을 돌려 쓴다(문화권마다 소도시 하나 · 큰 도시 몇).
/// </summary>
/// <remarks>
/// 도시 칸 타일과 바탕 타일(도시 표 <c>+0x74</c>)을 점마다 견주어 다른 점만 남기면 도시 그림이 된다
/// (<see cref="SpriteOf"/>). 그 그림을 왼쪽 위로 당겨 맞춘 모양이 같으면 같은 디자인이다 — 3x3 블록 안의 자리만 다르다.
///
/// 번호는 <b>그 디자인을 쓰는 가장 작은 도시 번호 차례</b>다. 게임과 편집기가 같은 셈을 하므로 번호가 갈리지 않는다.
///
/// 고해상도 그림은 <c>asset/citysprite/design_{번호}.png</c> 다(<see cref="HiResPath"/>). 디자인 크기의 <b>정수 배</b>여야
/// 하고(4배면 가로·세로 다 4배), 비침은 알파 0 이다. 없으면 원본 그림을 그대로 쓴다.
/// </remarks>
public static class CityDesigns
{
    /// <summary>고해상도 그림이 드는 폴더.</summary>
    public const string Folder = "asset/citysprite";

    /// <summary>블록 한 변의 점 수(3칸 x 16).</summary>
    public const int BlockSide = CityExeTable.EraseWidth * OceanTiles.TileW;   // 48

    /// <summary>디자인 하나 — 다듬은 크기, 색 번호(-1 비침), 그것을 쓰는 도시와 블록 안 자리.</summary>
    public sealed record Design(int Index, int Width, int Height, int[] Pixels, List<(int City, int OffX, int OffY)> Members);

    /// <summary>
    /// 도시 하나의 그림 — 48x48 색 번호, 바탕과 같은 점은 -1. <paramref name="word"/> 는 칸 (x, y) 의 지도 낱말이다.
    /// </summary>
    public static int[] SpriteOf(Func<int, int, int> word, OceanTiles ocean, int cx, int cy, ushort[] erase)
    {
        var sprite = new int[BlockSide * BlockSide];
        Array.Fill(sprite, -1);
        if (erase.Length != CityExeTable.EraseCells) return sprite;
        var tiles = ocean.TileData;
        for (int k = 0; k < erase.Length; k++)
        {
            if (erase[k] == CityExeTable.Keep) continue;
            int dx = k % CityExeTable.EraseWidth, dy = k / CityExeTable.EraseWidth;
            int tile = word(cx + dx, cy + dy) & OceanTiles.TileMask, under = erase[k] & OceanTiles.TileMask;
            for (int p = 0; p < OceanTiles.TilePixels; p++)
            {
                byte a = tiles[tile * OceanTiles.TilePixels + p], b = tiles[under * OceanTiles.TilePixels + p];
                if (a != b)
                    sprite[(dy * OceanTiles.TileW + p / OceanTiles.TileW) * BlockSide + dx * OceanTiles.TileW + p % OceanTiles.TileW] = a;
            }
        }
        return sprite;
    }

    /// <summary>모든 도시를 디자인으로 묶는다. 번호는 가장 작은 도시 번호 차례다.</summary>
    public static List<Design> Find(Func<int, int, int> word, OceanTiles ocean, CityExeTable cities)
    {
        var byKey = new Dictionary<string, Design>();
        var order = new List<Design>();
        for (int id = 0; id < GameMapCoords.CityCount; id++)
        {
            if (!cities.TryCell(id, out int cx, out int cy, out _)) continue;
            var sprite = SpriteOf(word, ocean, cx, cy, cities.EraseOf(id));
            if (!Bounds(sprite, out int x0, out int y0, out int w, out int h)) continue;

            var pixels = new int[w * h];
            for (int y = 0; y < h; y++)
                Array.Copy(sprite, (y0 + y) * BlockSide + x0, pixels, y * w, w);
            string key = $"{w}x{h}:" + string.Join(",", pixels);
            if (!byKey.TryGetValue(key, out var design))
            {
                design = new Design(order.Count, w, h, pixels, []);
                byKey[key] = design;
                order.Add(design);
            }
            design.Members.Add((id, x0, y0));
        }
        return order;
    }

    private static bool Bounds(int[] sprite, out int x0, out int y0, out int w, out int h)
    {
        int minX = BlockSide, minY = BlockSide, maxX = -1, maxY = -1;
        for (int y = 0; y < BlockSide; y++)
            for (int x = 0; x < BlockSide; x++)
                if (sprite[y * BlockSide + x] >= 0)
                {
                    minX = Math.Min(minX, x); minY = Math.Min(minY, y);
                    maxX = Math.Max(maxX, x); maxY = Math.Max(maxY, y);
                }
        x0 = minX; y0 = minY; w = maxX - minX + 1; h = maxY - minY + 1;
        return maxX >= 0;
    }

    /// <summary>
    /// Scale2x — 위·왼쪽이 같고 오른쪽·아래와 다르면 그 모서리를 이웃 색으로 하는 픽셀아트 두 배 키우기. 두 번 하면 Scale4x 다.
    /// </summary>
    public static int[] Scale2x(int[] a, int w, int h)
    {
        var o = new int[w * 2 * h * 2];
        int At(int x, int y) => a[Math.Clamp(y, 0, h - 1) * w + Math.Clamp(x, 0, w - 1)];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int e = At(x, y), b = At(x, y - 1), d = At(x - 1, y), f = At(x + 1, y), hh = At(x, y + 1);
                int e0 = e, e1 = e, e2 = e, e3 = e;
                if (b != hh && d != f)
                {
                    if (d == b) e0 = d;
                    if (b == f) e1 = f;
                    if (d == hh) e2 = d;
                    if (hh == f) e3 = f;
                }
                int row = y * 2 * w * 2;
                o[row + x * 2] = e0; o[row + x * 2 + 1] = e1;
                o[row + w * 2 + x * 2] = e2; o[row + w * 2 + x * 2 + 1] = e3;
            }
        return o;
    }

    /// <summary>색 번호 그림을 BGRA 로(-1 은 알파 0).</summary>
    public static uint[] ToBgra(int[] pixels, OceanTiles ocean) =>
        [.. pixels.Select(v => v < 0 ? 0u : 0xFF000000u | (uint)ocean.PaletteRgb[v])];

    /// <summary>그 디자인의 고해상도 그림 자리. 없으면 null — 소스 옆 <c>asset/citysprite</c> 를 먼저, 실행 폴더를 다음에 본다.</summary>
    public static string? HiResPath(int design)
    {
        foreach (var dir in Directories())
        {
            var path = Path.Combine(dir, $"design_{design}.png");
            if (File.Exists(path)) return path;
        }
        return null;
    }

    /// <summary>
    /// 고해상도 그림을 읽는다 — 디자인 크기의 정수 배가 아니면 null(<paramref name="scale"/> 은 0).
    /// </summary>
    public static uint[]? LoadHiRes(Design design, out int scale)
    {
        scale = 0;
        if (HiResPath(design.Index) is not { } path) return null;
        try
        {
            var frame = BitmapDecoder.Create(new Uri(path), BitmapCreateOptions.PreservePixelFormat,
                                             BitmapCacheOption.OnLoad).Frames[0];
            var bgra = new FormatConvertedBitmap(frame, PixelFormats.Bgra32, null, 0);
            int w = bgra.PixelWidth, h = bgra.PixelHeight;
            if (w % design.Width != 0 || h % design.Height != 0 || w / design.Width != h / design.Height) return null;
            scale = w / design.Width;
            var pixels = new uint[w * h];
            bgra.CopyPixels(pixels, w * 4, 0);
            return pixels;
        }
        catch (Exception ex) when (ex is IOException or NotSupportedException or FileFormatException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>
    /// 디자인을 내보낸다 — <c>reference/design_N_1x.png</c> · <c>_4x.png</c>(원본 도트 그대로 키운 밑그림),
    /// 디자인마다 쓰는 도시 목록(<c>designs.txt</c>), 그리고 아직 없으면 Scale4x 로 키운 <c>design_N.png</c> 기본 그림.
    /// </summary>
    /// <returns>새로 만든 기본 그림 수.</returns>
    public static int Export(string folder, IReadOnlyList<Design> designs, OceanTiles ocean, Func<int, string> cityName)
    {
        string reference = Path.Combine(folder, "reference");
        Directory.CreateDirectory(reference);
        int made = 0;
        var list = new StringBuilder();
        list.AppendLine("# 도시 그림 디자인 — design_N.png 를 정수 배(예: 4배) 크기로 그려 넣으면 그 디자인을 쓰는 도시가 모두 바뀐다.");
        list.AppendLine("# 비침은 알파 0. reference 폴더의 _1x(원본) · _4x(원본을 그대로 4배)를 밑그림으로 쓴다.");
        foreach (var d in designs)
        {
            Save(Path.Combine(reference, $"design_{d.Index}_1x.png"), ToBgra(d.Pixels, ocean), d.Width, d.Height);
            Save(Path.Combine(reference, $"design_{d.Index}_4x.png"), ToBgra(Nearest(d.Pixels, d.Width, d.Height, 4), ocean),
                 d.Width * 4, d.Height * 4);
            string hi = Path.Combine(folder, $"design_{d.Index}.png");
            if (!File.Exists(hi))
            {
                var s4 = Scale2x(Scale2x(d.Pixels, d.Width, d.Height), d.Width * 2, d.Height * 2);
                Save(hi, ToBgra(s4, ocean), d.Width * 4, d.Height * 4);
                made++;
            }
            list.AppendLine($"design_{d.Index}  {d.Width}x{d.Height}  도시 {d.Members.Count}곳: "
                            + string.Join(", ", d.Members.Select(m => cityName(m.City))));
        }
        File.WriteAllText(Path.Combine(folder, "designs.txt"), list.ToString(), new UTF8Encoding(true));
        return made;
    }

    private static int[] Nearest(int[] a, int w, int h, int s)
    {
        var o = new int[w * s * h * s];
        for (int y = 0; y < h * s; y++)
            for (int x = 0; x < w * s; x++)
                o[y * w * s + x] = a[(y / s) * w + x / s];
        return o;
    }

    private static void Save(string path, uint[] bgra, int w, int h)
    {
        var bmp = BitmapSource.Create(w, h, 96, 96, PixelFormats.Bgra32, null, bgra, w * 4);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bmp));
        using var file = File.Create(path);
        encoder.Save(file);
    }

    /// <summary>내보낼 폴더 — 저장소 안이면 소스 옆 <c>asset/citysprite</c>, 아니면 <c>%APPDATA%</c>. 없으면 만든다.</summary>
    public static string SaveDirectory()
    {
        var dir = SourceDirectory() ?? UserDirectory;
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static IEnumerable<string> Directories()
    {
        if (SourceDirectory() is { } near) yield return near;
        yield return UserDirectory;
        yield return Path.Combine(AppContext.BaseDirectory, Folder);
    }

    private static string UserDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CdsHelper", "asset", "citysprite");

    private static string? SourceDirectory()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        for (int up = 0; up < 8 && dir != null; up++, dir = dir.Parent)
            if (Directory.Exists(Path.Combine(dir.FullName, "asset")) && dir.EnumerateFiles("*.sln").Any())
                return Path.Combine(dir.FullName, "asset", "citysprite");
        return null;
    }
}
