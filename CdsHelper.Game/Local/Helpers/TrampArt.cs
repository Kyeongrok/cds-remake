using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CdsHelper.Support.Local.Helpers;

namespace CdsHelper.Game.Local.Helpers;

/// <summary>
/// 술집 포카 그림 — <c>TRAMP.CDS</c> 와 팔레트 <c>TRAMP.P</c>.
/// </summary>
/// <remarks>
/// 게임은 <c>0x00401800</c> 에서 둘을 읽고(<c>0x0052F638</c> <c>C:Tramp.CDS</c> ·
/// <c>0x0052F644</c> <c>C:Tramp.P</c>), 다 읽으면 <c>[포카+0x4C0] = 1</c> 을 박는다. 못 읽으면
/// 들머리(<c>0x00406470</c>)가 <b>아무 말 없이</b> 나간다 — 우리도 <see cref="Open"/> 이 null 이면 그렇다.
///
/// TRAMP.CDS 는 LS12 아카이브고 파트가 다섯이다.
/// <code>
///   파트 0   576x416      바탕 (창 크기와 같다)
///   파트 1   조각 여럿    0x0000 덱 더미 80x96 · 0x1E00 기울인 뒷면 80x80 · 0x3700 카드 마스크
///                         0x5000~0x5700 말풍선 테 16x16 여덟 · 0x5800 금화 24x24(선 표시)
///   파트 2   40x64 x52    선 카드
///   파트 3   80x80 x52    기울여 놓은 카드
///   파트 4   16x16 x4     무늬 표시 — 클럽·다이아·하트·스페이드
/// </code>
/// 카드 쉰두 장은 <b>무늬 x 13 + 끗수</b> 차례다(<see cref="Engine.Town.Poker.PictureOf"/>).
///
/// <b>TRAMP.P 는 날것 팔레트</b>다(423바이트 = 141색, 바이트 차례 파랑·빨강·초록). 게임은 이것을
/// 공용 팔레트의 <b>74번부터</b> 얹는다(<c>0x004BA161(0x4A, 0xAC, +0xB6)</c>). 그 아래는 공용
/// 색표(<see cref="GamePalette"/>)다 — 창·말풍선 바탕 10, 글자 0x49 가 거기서 온다.
///
/// 읽은 뒤 파트 1·2·3 의 <b>색 214(0xD6) 를 비침</b>으로 바꾼다(<c>0x00401800</c>). 무늬 표시(파트 4)도
/// 같은 자리가 노랑 바탕이라 함께 비운다. 색인 73 은 TRAMP.P 밖이지만 검정이다.
/// </remarks>
public sealed class TrampArt
{
    public const int BoardWidth = 576, BoardHeight = 416;
    public const int DeckWidth = 80, DeckHeight = 96;
    public const int TiltSize = 80;
    public const int CardWidth = 40, CardHeight = 64;
    public const int TileSize = 16, CoinSize = 24, MarkSize = 16;

    /// <summary>말풍선 테 조각 수 — 위·아래·왼·오른 변, 왼위·왼아래·오른위·오른아래 모서리.</summary>
    public const int TileCount = 8;

    private const int BackAt = 0x1E00, TilesAt = 0x5000, CoinAt = 0x5800;

    private const byte ClearIndex = 214, BlackIndex = 73;

    private const int CardCount = 52, SuitCount = 4;

    private readonly byte[] _palette;

    private TrampArt(byte[] palette, byte[][] parts)
    {
        _palette = palette;

        Board = Cut(parts[0], 0, BoardWidth, BoardHeight, clear: false);
        Deck = Cut(parts[1], 0, DeckWidth, DeckHeight, clear: true);
        Back = Cut(parts[1], BackAt, TiltSize, TiltSize, clear: true);
        Coin = Cut(parts[1], CoinAt, CoinSize, CoinSize, clear: true);

        var tiles = new BitmapSource[TileCount];
        for (int i = 0; i < TileCount; i++)
            tiles[i] = Cut(parts[1], TilesAt + i * TileSize * TileSize, TileSize, TileSize, clear: true);
        Tiles = tiles;

        var cards = new BitmapSource[CardCount];
        var tilted = new BitmapSource[CardCount];
        for (int n = 0; n < CardCount; n++)
        {
            cards[n] = Cut(parts[2], n * CardWidth * CardHeight, CardWidth, CardHeight, clear: true);
            tilted[n] = Cut(parts[3], n * TiltSize * TiltSize, TiltSize, TiltSize, clear: true);
        }
        Cards = cards;
        Tilted = tilted;

        var marks = new BitmapSource[SuitCount];
        for (int s = 0; s < SuitCount; s++)
            marks[s] = Cut(parts[4], s * MarkSize * MarkSize, MarkSize, MarkSize, clear: true);
        Marks = marks;
    }

    /// <summary>왜 못 열었는지. 잘 열렸으면 빈 문자열.</summary>
    public static string LastError { get; private set; } = "";

    /// <summary>카드판 바탕 576x416.</summary>
    public BitmapSource Board { get; }

    /// <summary>덱 더미 80x96. 카드판 (248,160) 에 선다.</summary>
    public BitmapSource Deck { get; }

    /// <summary>기울인 뒷면 80x80 — 뒤집힌 칸과 버린 더미.</summary>
    public BitmapSource Back { get; }

    /// <summary>선 표시 금화 24x24.</summary>
    public BitmapSource Coin { get; }

    /// <summary>말풍선 테 16x16 여덟(<see cref="TileCount"/> 차례).</summary>
    public IReadOnlyList<BitmapSource> Tiles { get; }

    /// <summary>선 카드 40x64, 그림 번호 차례.</summary>
    public IReadOnlyList<BitmapSource> Cards { get; }

    /// <summary>기울인 카드 80x80, 그림 번호 차례.</summary>
    public IReadOnlyList<BitmapSource> Tilted { get; }

    /// <summary>무늬 표시 16x16 — 클럽·다이아·하트·스페이드.</summary>
    public IReadOnlyList<BitmapSource> Marks { get; }

    /// <summary>
    /// 두 파일을 읽는다. 하나라도 없거나 파트 크기가 어긋나면 null.
    /// </summary>
    public static TrampArt? Open(string gameDirectory)
    {
        LastError = "";
        if (string.IsNullOrEmpty(gameDirectory))
        {
            LastError = "게임 폴더를 모릅니다";
            return null;
        }

        string cds = CdsAssetPath.Resolve(gameDirectory, "TRAMP.CDS");
        string pal = CdsAssetPath.Resolve(gameDirectory, "TRAMP.P");

        var archive = Ls12Reader.Open(cds);
        if (archive == null || archive.PartCount < 5)
        {
            LastError = $"{cds} 를 열지 못했습니다";
            return null;
        }

        byte[] palette;
        try { palette = File.ReadAllBytes(pal); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            LastError = $"{pal} 를 읽지 못했습니다 — {ex.Message}";
            return null;
        }

        int[] sizes =
        [
            BoardWidth * BoardHeight,
            CoinAt + CoinSize * CoinSize,
            CardWidth * CardHeight * CardCount,
            TiltSize * TiltSize * CardCount,
            MarkSize * MarkSize * SuitCount,
        ];
        var parts = new byte[5][];
        for (int p = 0; p < parts.Length; p++)
        {
            var bytes = archive.Decode(p);
            if (bytes == null || bytes.Length < sizes[p])
            {
                LastError = $"{cds} 파트 {p} 크기가 안 맞습니다";
                return null;
            }
            parts[p] = bytes;
        }
        return new TrampArt(palette, parts);
    }

    /// <summary>
    /// 그 색인의 색. 74 아래는 공용 색표, 74~214 는 TRAMP.P, 73 은 검정이다.
    /// </summary>
    public Color ColorOf(int index)
    {
        if (index == BlackIndex) return Colors.Black;

        int k = index - GamePalette.OwnPaletteBase;
        if (index >= GamePalette.OwnPaletteBase && k * 3 + 2 < _palette.Length)
            // 파일 속 차례는 (파랑, 빨강, 초록)이다.
            return Color.FromRgb(_palette[k * 3 + 1], _palette[k * 3 + 2], _palette[k * 3]);

        int i = Math.Clamp(index, 0, 255) * 3;
        return Color.FromRgb(GamePalette.Rgb[i], GamePalette.Rgb[i + 1], GamePalette.Rgb[i + 2]);
    }

    /// <summary>파트 한 자리에서 한 조각을 끊어 BGRA 그림으로 낸다.</summary>
    private BitmapSource Cut(byte[] pixels, int at, int width, int height, bool clear)
    {
        var lookup = new uint[256];
        for (int v = 0; v < 256; v++)
        {
            var c = ColorOf(v);
            lookup[v] = (uint)(0xFF << 24 | c.R << 16 | c.G << 8 | c.B);
        }

        var bgra = new uint[width * height];
        for (int i = 0; i < bgra.Length; i++)
        {
            byte v = pixels[at + i];
            bgra[i] = clear && v == ClearIndex ? 0u : lookup[v];
        }

        var bmp = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, bgra, width * 4);
        bmp.Freeze();
        return bmp;
    }
}
