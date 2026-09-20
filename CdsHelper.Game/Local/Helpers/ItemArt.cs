using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CdsHelper.Support.Local.Helpers;

namespace CdsHelper.Game.Local.Helpers;

/// <summary>
/// 아이템·교역품 그림 206장. 아이템 창에 뜨는 양피지 위의 그림이다.
/// </summary>
/// <remarks>
/// 그림은 <c>asset/item/item-000.png</c> ~ <c>item-205.png</c> 에서 읽는다. 게임 폴더가
/// 없어도 아이템 창을 낼 수 있게 미리 뽑아 둔 것이다(<c>tools/extract_item_pics.py</c>).
/// 파일이 빠졌으면 예전처럼 <c>ITEM.CDS</c> 에서 그때그때 푼다.
///
/// 파일 이름은 <b>그림 번호</b>다 — 아이템 번호가 아니다. 어느 아이템이 몇 번을 쓰는지는
/// <see cref="ItemTable.Record.Pic"/> 가 낸다. 99개는 그림이 없고, 한 그림을 여럿이
/// 나눠 쓰기도 한다.
///
/// ITEM.CDS 는 LS12 아카이브고 파트가 둘씩 짝을 이룬다.
/// <code>
///   파트 2p     14400바이트 = 120x120, 8bpp 색인
///   파트 2p+1     258바이트 = 86색 팔레트 (파랑, 빨강, 초록 차례)
///   색인 >= 160  이 그림 제 팔레트(k = 색인-160)
///   색인 <  160  게임 공용 색표(<see cref="GamePalette"/>)
/// </code>
/// 이 규칙은 cds95-mod 의 <c>CharacterUtilKR/src/itempic.c</c> 를 따랐다.
/// </remarks>
public sealed class ItemArt
{
    /// <summary>뽑아 둔 그림이 든 곳.</summary>
    public const string ArtDirectory = "asset/item";

    /// <summary>한 장의 크기.</summary>
    public const int Width = 120, Height = 120;

    private const int Size = Width * Height;

    /// <summary>그림 제 팔레트가 얹히는 첫 색인.</summary>
    private const int PaletteBase = 160;

    /// <summary>팔레트 파트 크기(86색 x 3바이트).</summary>
    private const int PaletteBytes = 258;

    /// <summary>ITEM.CDS. asset 에 그림이 다 있으면 안 열어도 된다.</summary>
    private readonly Ls12Reader? _archive;

    private readonly Dictionary<int, uint[]> _cache = [];

    private ItemArt(Ls12Reader? archive, int count)
    {
        _archive = archive;
        Count = count;
    }

    /// <summary>왜 못 열었는지. 잘 열렸으면 빈 문자열.</summary>
    public static string LastError { get; private set; } = "";

    /// <summary>그림 장수.</summary>
    public int Count { get; }

    /// <summary>
    /// 그림을 연다. <c>asset/item</c> 만 있으면 게임 폴더 없이도 열린다.
    /// </summary>
    public static ItemArt Open(string gameDirectory)
    {
        LastError = "";

        int onDisk = 0;
        for (int p = 0; p <= ItemTable.MaxPic; p++)
            if (File.Exists(ArtPath(p))) onDisk++;

        // 다 있으면 CDS 는 아예 안 연다.
        if (onDisk > ItemTable.MaxPic) return new ItemArt(null, onDisk);

        Ls12Reader? archive = null;
        if (!string.IsNullOrEmpty(gameDirectory))
        {
            string path = CdsAssetPath.Resolve(gameDirectory, "ITEM.CDS");
            archive = File.Exists(path) ? Ls12Reader.Open(path) : null;
            if (archive == null) LastError = $"{path} 를 열지 못했습니다";
        }
        else if (onDisk == 0)
        {
            LastError = $"{ArtDirectory} 에 그림이 없고 게임 폴더도 모릅니다";
        }

        int count = Math.Max(onDisk, archive == null ? 0 : archive.PartCount / 2);
        return new ItemArt(archive, count);
    }

    private static string ArtPath(int pic) =>
        Path.Combine(AppContext.BaseDirectory, ArtDirectory, $"item-{pic:D3}.png");

    /// <summary>
    /// 그림 한 장을 BGRA 로 낸다(<see cref="Width"/> x <see cref="Height"/>). 못 내면 null.
    /// 한 번 푼 것은 들고 있는다 — 목록을 오르내릴 때마다 다시 풀 까닭이 없다.
    /// </summary>
    public uint[]? TryGetBgra(int pic)
    {
        if (pic < 0 || pic > ItemTable.MaxPic) return null;
        if (_cache.TryGetValue(pic, out var got)) return got;

        var bgra = FromAsset(pic) ?? FromArchive(pic);
        if (bgra != null) _cache[pic] = bgra;
        return bgra;
    }

    /// <summary>그림 한 장을 화면에 바로 얹을 수 있는 꼴로 낸다. 없으면 null.</summary>
    public BitmapSource? TryGetImage(int pic)
    {
        var bgra = TryGetBgra(pic);
        if (bgra == null) return null;

        var bmp = BitmapSource.Create(Width, Height, 96, 96, PixelFormats.Bgra32, null,
                                      bgra, Width * 4);
        bmp.Freeze();
        return bmp;
    }

    /// <summary>뽑아 둔 PNG 에서 푼다. 없거나 크기가 다르면 null 을 내어 CDS 로 물러선다.</summary>
    private static uint[]? FromAsset(int pic)
    {
        string path = ArtPath(pic);
        if (!File.Exists(path)) return null;
        try
        {
            using var fs = File.OpenRead(path);
            var decoder = new PngBitmapDecoder(fs, BitmapCreateOptions.PreservePixelFormat,
                                               BitmapCacheOption.OnLoad);
            var src = new FormatConvertedBitmap(decoder.Frames[0], PixelFormats.Bgra32, null, 0);
            if (src.PixelWidth != Width || src.PixelHeight != Height) return null;

            var bgra = new uint[Size];
            src.CopyPixels(bgra, Width * 4, 0);
            return bgra;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                                      or NotSupportedException or ArgumentException)
        {
            return null;
        }
    }

    /// <summary>ITEM.CDS 에서 그때그때 푼다.</summary>
    private uint[]? FromArchive(int pic)
    {
        if (_archive == null || 2 * pic + 1 >= _archive.PartCount) return null;

        var idx = _archive.Decode(2 * pic);
        if (idx == null || idx.Length != Size) return null;

        var pal = _archive.Decode(2 * pic + 1);
        if (pal == null || pal.Length < 3) return null;

        var bgra = new uint[Size];
        for (int i = 0; i < Size; i++)
        {
            byte v = idx[i];
            int k = v - PaletteBase;
            byte r, g, b;
            if (v >= PaletteBase && k * 3 + 2 < Math.Min(pal.Length, PaletteBytes))
            {
                // 파일 속 팔레트는 (파랑, 빨강, 초록) 순이다.
                b = pal[k * 3];
                r = pal[k * 3 + 1];
                g = pal[k * 3 + 2];
            }
            else
            {
                // 공용 색표는 256색 x 3바이트(R,G,B) 한 줄로 늘어서 있다.
                r = GamePalette.Rgb[v * 3];
                g = GamePalette.Rgb[v * 3 + 1];
                b = GamePalette.Rgb[v * 3 + 2];
            }
            bgra[i] = (uint)(0xFF << 24 | r << 16 | g << 8 | b);
        }
        return bgra;
    }
}
