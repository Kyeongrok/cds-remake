using CdsHelper.Support.Local.Helpers;
using CdsHelper.Support.Local.Models;

namespace CdsHelper.Game.Local.Helpers;

/// <summary>
/// 게임 폴더의 <c>SHIPSTIL.CDS</c> — 조선소에서 마스트·돛을 손볼 때 뜨는 <b>배 그림</b>(320x240).
/// </summary>
/// <remarks>
/// 원본은 마스트 추가(<c>0x00494C0C</c>)와 돛종류 변경(<c>0x00494F4A</c>)을 시작할 때
/// <c>0x00494900</c> 으로 이 그림 창을 세우고, 돛을 달거나 바꾸면 다시 그린다(<c>0x004949E0</c>).
/// 그림을 짓는 것은 <c>0x00472BC0</c> → <c>0x00472880</c> 이다.
/// <code>
///   파트 0        제 팔레트 256색 — 색인 74 부터 얹힌다(74 아래는 공용 색표)
///   파트 1~6      선체 바탕 320x240 여섯 벌
///   파트 7~25     마스트·돛 조각 — 크기는 표 0x00560FE8(파트마다 폭·높이)
///   0x005610B8    선체 번호 → 바탕 벌  [0,0,1,2,3,4,5,1]   ; 코구·카라벨이 한 벌, 다우는 대형카라벨 벌
///   0x005005F8    벌마다 96바이트: 바탕(파트,x,y) · 덧댄 마스트(파트,x,y) ·
///                 마스트 셋의 삼각돛(파트,x,y) x3 · 사각돛(파트,x,y) x3
/// </code>
/// 덧댄 마스트는 <b>선체표에는 없던 마스트를 세웠을 때</b>만 그린다(<c>0x00472BE8</c> — 표 +0x3C 의
/// 세브·선미 비트가 0 인데 배 +0x68 에는 있을 때). 조각의 바탕 색인 74 는 비친다.
/// </remarks>
public static class ShipStill
{
    public const string FileName = "SHIPSTIL.CDS";

    /// <summary>그림 크기.</summary>
    public const int Width = 320, Height = 240;

    /// <summary>비치는 색인 — 조각의 바탕이다.</summary>
    private const int Hollow = 74;

    /// <summary>
    /// 파트마다 폭·높이(<c>0x00560FE8</c>). 파트 7 만 표에 63x127 로 적혀 있는데 풀면 8192바이트라
    /// 64x128 로 둔다 — 63 폭으로 펴면 한 줄마다 한 점씩 밀려 기둥이 기운다.
    /// </summary>
    private static readonly (int W, int H)[] Sizes =
    [
        (0, 0),
        (320, 240), (320, 240), (320, 240), (320, 240), (320, 240), (320, 240),
        (64, 128), (64, 112), (96, 144), (48, 96), (144, 144), (48, 96), (80, 80), (96, 96),
        (128, 160), (80, 112), (112, 144), (80, 112), (112, 176), (80, 80), (176, 176),
        (128, 128), (144, 144), (80, 80), (128, 144),
    ];

    /// <summary>선체 번호 → 바탕 벌(<c>0x005610B8</c>).</summary>
    private static readonly int[] SetOfHull = [0, 0, 1, 2, 3, 4, 5, 1];

    /// <summary>한 조각 — 파트 번호와 바탕 위 자리.</summary>
    private readonly record struct Piece(int Part, int X, int Y);

    /// <summary>벌 하나(<c>0x005005F8</c>, 96바이트).</summary>
    private readonly record struct Layout(Piece Back, Piece Extra, Piece[] Lateen, Piece[] Square);

    private static readonly Layout[] Layouts =
    [
        new(new(1, 0, 0), new(7, 84, 49),
            [new(21, 124, 3), new(20, 74, 86), new(20, 0, 0)],
            [new(11, 0, 0), new(11, 0, 0), new(11, 0, 0)]),
        new(new(2, 0, 0), new(8, 193, 74),
            [new(21, 84, 4), new(20, 49, 85), new(22, 181, 33)],
            [new(11, 103, 50), new(12, 69, 82), new(13, 195, 84)]),
        new(new(3, 0, 0), new(9, 178, 39),
            [new(21, 92, 13), new(20, 47, 91), new(22, 188, 46)],
            [new(11, 107, 59), new(13, 65, 92), new(14, 193, 78)]),
        new(new(4, 0, 0), new(10, 223, 83),
            [new(23, 114, 34), new(20, 60, 90), new(22, 206, 54)],
            [new(15, 137, 21), new(13, 76, 92), new(13, 221, 103)]),
        new(new(5, 0, 0), new(7, 0, 0),
            [new(23, 111, 32), new(22, 48, 54), new(24, 230, 74)],
            [new(17, 135, 43), new(16, 66, 69), new(18, 226, 59)]),
        new(new(6, 0, 0), new(7, 0, 0),
            [new(25, 105, 28), new(22, 36, 50), new(24, 217, 73)],
            [new(19, 136, 9), new(16, 65, 60), new(18, 224, 57)]),
    ];

    private static readonly Dictionary<string, (byte[]? Palette, byte[]?[] Parts)> Cache = [];

    /// <summary>
    /// 그 배의 그림을 BGRA 로 짓는다. 파일이 없거나 깨졌으면 null.
    /// </summary>
    public static uint[]? Compose(string gameDirectory, Ship ship)
    {
        var (palette, parts) = Load(gameDirectory);
        if (palette == null) return null;

        int hull = Math.Clamp(ship.Hull.GameId, 0, 7);
        var layout = Layouts[SetOfHull[hull]];

        var bgra = new uint[Width * Height];
        if (!Draw(bgra, palette, parts, layout.Back, opaque: true)) return null;

        // 선체표에는 없던 마스트를 세웠으면 그 기둥을 덧댄다(0x00472BC0).
        var table = Hull.Table[hull].Sails;
        bool extra = (table[1] == Ship.NoSail && ship.Sails[1] != Ship.NoSail)
                  || (table[2] == Ship.NoSail && ship.Sails[2] != Ship.NoSail);
        if (extra) Draw(bgra, palette, parts, layout.Extra, opaque: false);

        for (int i = 0; i < Ship.MastSlots; i++)
        {
            int sail = ship.Sails[i];
            if (sail == Ship.NoSail) continue;
            Draw(bgra, palette, parts, sail == Ship.Lateen ? layout.Lateen[i] : layout.Square[i], opaque: false);
        }
        return bgra;
    }

    private static (byte[]? Palette, byte[]?[] Parts) Load(string gameDirectory)
    {
        lock (Cache)
        {
            if (Cache.TryGetValue(gameDirectory, out var cached)) return cached;

            var archive = Ls12Reader.Open(CdsAssetPath.Resolve(gameDirectory, FileName));
            (byte[]?, byte[]?[]) made = (null, []);
            if (archive != null && archive.PartCount >= Sizes.Length)
            {
                var parts = new byte[]?[Sizes.Length];
                for (int p = 1; p < Sizes.Length; p++) parts[p] = archive.Decode(p);
                made = (archive.Decode(0), parts);
            }
            Cache[gameDirectory] = made;
            return made;
        }
    }

    /// <summary>조각 하나를 얹는다. 바탕(<paramref name="opaque"/>)이면 74 도 흰 바탕으로 칠한다.</summary>
    private static bool Draw(uint[] bgra, byte[] palette, byte[]?[] parts, Piece piece, bool opaque)
    {
        if (piece.Part <= 0 || piece.Part >= parts.Length || parts[piece.Part] is not { } data) return false;
        var (w, h) = Sizes[piece.Part];
        if (data.Length < w * h) return false;

        for (int y = 0; y < h; y++)
        {
            int ty = piece.Y + y;
            if (ty < 0 || ty >= Height) continue;
            for (int x = 0; x < w; x++)
            {
                int tx = piece.X + x;
                if (tx < 0 || tx >= Width) continue;
                int v = data[y * w + x];
                if (v == Hollow && !opaque) continue;
                bgra[ty * Width + tx] = ColorOf(v, palette);
            }
        }
        return true;
    }

    private static uint ColorOf(int v, byte[] palette)
    {
        byte[] rgb;
        int k;
        if (v >= GamePalette.OwnPaletteBase) { rgb = palette; k = (v - GamePalette.OwnPaletteBase) * 3; }
        else { rgb = GamePalette.Rgb; k = v * 3; }
        if (k + 2 >= rgb.Length) return 0xFF000000u;
        return 0xFF000000u | ((uint)rgb[k] << 16) | ((uint)rgb[k + 1] << 8) | rgb[k + 2];
    }
}
