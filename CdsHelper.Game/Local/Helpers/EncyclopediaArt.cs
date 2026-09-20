using System.IO;
using CdsHelper.Support.Local.Helpers;

namespace CdsHelper.Game.Local.Helpers;

/// <summary>
/// 게임 폴더의 <c>ENC.CDS</c> — 자택 백과사전의 펼친 책.
/// </summary>
/// <remarks>
/// LS12 두 파트다(<c>0x00462610</c> 이 연다).
/// <code>
///   파트 0   258바이트  86색 팔레트 — 밑동 0x4A(74)부터 얹는다(0x004BA161(0x4A, 0x56))
///   파트 1   460800바이트  8bpp 덩이 하나, 그림이 오프셋으로 붙어 있다
///     0x00000   544x304  책 틀
///     0x28600   256x288  오른쪽 면
///     0x3A600   256x288  (안 쓴다)
///     0x4C600   256x288  왼쪽 면
///     0x5E600   256x288  왼쪽 면 — 교역품 그림 틀(교역품 갈래이고 아이템을 줄 때, 0x004627F0)
///     0x70600    16x16   다음장 모서리
///     0x70700    16x16   앞장 모서리
/// </code>
/// 팔레트 바이트 차례는 <see cref="OpenBookArt"/> 와 같이 (파랑, 빨강, 초록)이다.
/// </remarks>
public sealed class EncyclopediaArt
{
    /// <summary>그림 하나 — 덩이 안 자리와 크기.</summary>
    public readonly record struct Piece(int Offset, int Width, int Height);

    public static readonly Piece Frame = new(0x00000, 544, 304);
    public static readonly Piece RightPage = new(0x28600, 256, 288);
    public static readonly Piece LeftPage = new(0x4C600, 256, 288);
    public static readonly Piece LeftPageFramed = new(0x5E600, 256, 288);
    public static readonly Piece NextCorner = new(0x70600, 16, 16);
    public static readonly Piece PreviousCorner = new(0x70700, 16, 16);

    /// <summary>팔레트가 얹히는 첫 색인.</summary>
    private const int PaletteBase = 0x4A;

    private readonly byte[] _pixels;
    private readonly byte[] _palette;
    private readonly Dictionary<int, uint[]> _made = [];

    private EncyclopediaArt(byte[] pixels, byte[] palette)
    {
        _pixels = pixels;
        _palette = palette;
    }

    /// <summary>왜 못 열었는지. 잘 열렸으면 빈 문자열.</summary>
    public static string LastError { get; private set; } = "";

    /// <summary>게임 폴더에서 연다. 없거나 형식이 아니면 null.</summary>
    public static EncyclopediaArt? Open(string gameDirectory)
    {
        LastError = "";
        string path = CdsAssetPath.Resolve(gameDirectory, "ENC.CDS");
        if (!File.Exists(path)) { LastError = $"{path} 가 없습니다"; return null; }
        var archive = Ls12Reader.Open(path);
        if (archive == null || archive.PartCount < 2) { LastError = $"{path} 를 읽지 못했습니다"; return null; }

        var palette = archive.Decode(0);
        var pixels = archive.Decode(1);
        if (palette == null || pixels == null || pixels.Length < PreviousCorner.Offset + 16 * 16)
        {
            LastError = "ENC.CDS 가 기대한 모양이 아닙니다";
            return null;
        }
        return new EncyclopediaArt(pixels, palette);
    }

    /// <summary>그림 한 장을 BGRA 로 푼다.</summary>
    public uint[] Bgra(Piece piece)
    {
        if (_made.TryGetValue(piece.Offset, out var kept)) return kept;

        var made = new uint[piece.Width * piece.Height];
        for (int i = 0; i < made.Length; i++)
        {
            byte v = _pixels[piece.Offset + i];
            int k = (v - PaletteBase) * 3;
            byte r, g, b;
            if (v >= PaletteBase && k + 2 < _palette.Length)
            {
                b = _palette[k];
                r = _palette[k + 1];
                g = _palette[k + 2];
            }
            else
            {
                r = GamePalette.Rgb[v * 3];
                g = GamePalette.Rgb[v * 3 + 1];
                b = GamePalette.Rgb[v * 3 + 2];
            }
            made[i] = 0xFF000000u | ((uint)r << 16) | ((uint)g << 8) | b;
        }
        _made[piece.Offset] = made;
        return made;
    }
}
