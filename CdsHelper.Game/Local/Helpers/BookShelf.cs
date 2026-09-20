using System.IO;
using CdsHelper.Support.Local.Helpers;

namespace CdsHelper.Game.Local.Helpers;

/// <summary>
/// 게임 폴더의 <c>BOOKSHEL.CDS</c> — 도서관 열람 화면의 빈 책장과 책등 세 벌.
/// </summary>
/// <remarks>
/// LS12 아카이브이고 파트가 다섯이다.
/// <code>
///   0  261바이트  팔레트 87색(한 색이 파랑·빨강·초록 순)
///   1  122,880    빈 책장 384x320, 8bpp 색인
///   2~4  2,048씩  책등 32x64 — 차례대로 초록 · 파랑 · 빨강
/// </code>
/// 색인 규칙은 도시 그림과 같다(74 위쪽은 제 팔레트, 아래는 공용 색표). 다만 <b>색인 160</b>
/// (제 팔레트의 마지막 색)이 비침 자리다 — 책등 둘레가 그 색으로 채워져 있다.
///
/// 책등 번호는 게임의 <c>0x4716A0</c> 이 돌려주는 값과 그대로 짝이다 — 0 초록 · 1 파랑 ·
/// 2 빨강. 뜻은 볼트 <c>20.분석-도서관 책과 책등 색</c> 참고.
/// </remarks>
public sealed class BookShelf
{
    public const int ShelfWidth = 384, ShelfHeight = 320;
    public const int SpineWidth = 32, SpineHeight = 64;

    /// <summary>비침으로 치는 색인. 제 팔레트의 마지막 색이다.</summary>
    private const int TransparentIndex = 160;

    private BookShelf(uint[] shelf, uint[][] spines)
    {
        Shelf = shelf;
        Spines = spines;
    }

    /// <summary>왜 못 읽었는지. 잘 열렸으면 빈 문자열.</summary>
    public static string LastError { get; private set; } = "";

    /// <summary>빈 책장 384x320(BGRA).</summary>
    public uint[] Shelf { get; }

    /// <summary>책등 32x64 세 벌(BGRA). 차례는 0 초록 · 1 파랑 · 2 빨강.</summary>
    public uint[][] Spines { get; }

    /// <summary>게임 폴더에서 읽는다. 못 읽으면 null.</summary>
    public static BookShelf? Open(string gameDirectory)
    {
        LastError = "";
        var path = CdsAssetPath.Resolve(gameDirectory, "BOOKSHEL.CDS");
        var archive = Ls12Reader.Open(path);
        if (archive == null) { LastError = $"{path} 를 읽지 못했습니다"; return null; }
        if (archive.PartCount < 5) { LastError = "BOOKSHEL.CDS 에 파트가 모자랍니다"; return null; }

        var palette = archive.Decode(0);
        var shelfIndices = archive.Decode(1);
        if (palette == null || shelfIndices == null ||
            shelfIndices.Length < ShelfWidth * ShelfHeight)
        {
            LastError = "책장 그림을 못 풀었습니다";
            return null;
        }

        var spines = new uint[3][];
        for (int i = 0; i < 3; i++)
        {
            var indices = archive.Decode(2 + i);
            if (indices == null || indices.Length < SpineWidth * SpineHeight)
            {
                LastError = $"책등 {i} 을 못 풀었습니다";
                return null;
            }
            spines[i] = ToBgra(indices, palette, SpineWidth * SpineHeight);
        }
        return new BookShelf(ToBgra(shelfIndices, palette, ShelfWidth * ShelfHeight), spines);
    }

    /// <summary>색인 그림을 색으로 푼다. 비침 자리는 알파 0 이다.</summary>
    private static uint[] ToBgra(byte[] indices, byte[] palette, int count)
    {
        var px = new uint[count];
        for (int i = 0; i < count; i++)
        {
            byte v = indices[i];
            if (v == TransparentIndex) { px[i] = 0; continue; }

            int k = (v - GamePalette.OwnPaletteBase) * 3;
            byte r, g, b;
            if (v >= GamePalette.OwnPaletteBase && k + 2 < palette.Length)
            {
                b = palette[k];            // 제 팔레트는 (파랑, 빨강, 초록) 순이다
                r = palette[k + 1];
                g = palette[k + 2];
            }
            else
            {
                r = GamePalette.Rgb[v * 3];
                g = GamePalette.Rgb[v * 3 + 1];
                b = GamePalette.Rgb[v * 3 + 2];
            }
            px[i] = 0xFF000000u | ((uint)r << 16) | ((uint)g << 8) | b;
        }
        return px;
    }
}
