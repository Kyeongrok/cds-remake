using System.IO;

namespace CdsHelper.Support.Local.Helpers;

/// <summary>
/// OCEAN.CDS — 게임이 해상 화면을 그릴 때 쓰는 타일 그림. 세계지도를 게임과 같은 그림으로
/// 그리는 데 쓴다.
/// </summary>
/// <remarks>
/// 파일은 Ls12 압축이고 파트가 둘이다.
/// <list type="bullet">
///   <item>파트0 원본 4,194,304 (0x400000) — 16x16 8bpp 타일 16,384장 (한 장 256바이트)</item>
///   <item>파트1 원본 258 — 정체 미상. 쓰지 않는다.</item>
/// </list>
/// WORLD.CDS 의 칸 u16 하위 14비트가 곧 타일 번호다(게임 0x48A40A 의 <c>and cx,0x3FFF</c>).
/// 색은 <see cref="OceanPalette"/> 로 푼다.
///
/// 세계지도는 칸 하나를 <c>scale</c> 픽셀로 그리므로 16x16 타일을 그대로 쓸 수 없다.
/// 대신 타일마다 scale x scale 평균색을 미리 계산해 두고 그걸 칠한다 — 그리는 비용이
/// 지금(팔레트 계산)과 같으면서 그림은 게임 것이 된다.
/// </remarks>
public sealed class OceanTiles
{
    public const int TileW = 16;
    public const int TilePixels = TileW * TileW;            // 256
    public const int TileCount = 16384;
    public const int DataSize = TilePixels * TileCount;     // 0x400000
    public const int TileMask = 0x3FFF;

    public const string FileName = "OCEAN.CDS";

    private readonly byte[] _tiles;                          // 4MB, 타일당 256바이트 인덱스
    private readonly int[] _rgb = new int[256];              // 인덱스 -> 0xRRGGBB
    private readonly Dictionary<int, int[]> _averages = [];  // scale -> 타일당 scale*scale 색

    private static OceanTiles? _cached;
    private static string? _cachedPath;

    private OceanTiles(byte[] tiles)
    {
        _tiles = tiles;
        for (int i = 0; i < 256; i++)
        {
            int c = i * 3;
            _rgb[i] = (OceanPalette.Rgb[c] << 16) | (OceanPalette.Rgb[c + 1] << 8) | OceanPalette.Rgb[c + 2];
        }
    }

    /// <summary>못 올렸으면 그 까닭 한 줄. 올렸으면 빈 문자열.</summary>
    public static string LastError { get; private set; } = "";

    /// <summary>
    /// 타일 그림 원본. 타일 하나가 256바이트(16x16 8bpp 인덱스)이고 자리는
    /// <c>tile * 256 + py * 16 + px</c>. 확대해서 그릴 때 점 하나씩 뽑아 쓴다. 읽기 전용.
    /// </summary>
    public byte[] TileData => _tiles;

    /// <summary>팔레트를 미리 푼 것. 인덱스 -> 0xRRGGBB. 읽기 전용.</summary>
    public int[] PaletteRgb => _rgb;

    /// <summary>
    /// WORLD.CDS 와 같은 폴더의 OCEAN.CDS 를 풀어 올린다. 실패하면 null 이고
    /// <see cref="LastError"/> 에 까닭이 남는다. 한 번 올린 것은 경로가 같으면 다시 쓴다.
    /// </summary>
    public static OceanTiles? LoadFromDirectory(string directory)
    {
        var path = CdsAssetPath.Resolve(directory, FileName);
        if (_cached != null && string.Equals(_cachedPath, path, StringComparison.OrdinalIgnoreCase))
        {
            LastError = "";
            return _cached;
        }

        if (!File.Exists(path))
        {
            LastError = $"{FileName} 없음";
            return null;
        }

        var reader = Ls12Reader.Open(path);
        if (reader == null)
        {
            LastError = $"{FileName} 이 Ls12 형식이 아님";
            return null;
        }
        if (reader.PartCount < 1 || reader.PartSize(0) != DataSize)
        {
            LastError = $"{FileName} 파트0 크기가 다름 ({reader.PartSize(0)}, 파트 {reader.PartCount}개)";
            return null;
        }

        var tiles = reader.Decode(0);
        if (tiles == null)
        {
            LastError = $"{FileName} 압축 해제 실패";
            return null;
        }

        LastError = "";
        _cached = new OceanTiles(tiles);
        _cachedPath = path;
        return _cached;
    }

    /// <summary>
    /// 타일마다 <paramref name="scale"/> x <paramref name="scale"/> 로 줄인 평균색 표.
    /// 색 하나는 0xRRGGBB, 자리는 <c>tile * scale * scale + qy * scale + qx</c>.
    /// 같은 scale 로 다시 부르면 앞서 만든 것을 그대로 돌려준다.
    /// </summary>
    public int[] GetAverages(int scale)
    {
        if (scale < 1 || scale > TileW)
            throw new ArgumentOutOfRangeException(nameof(scale), scale, $"1~{TileW} 이어야 한다");
        if (_averages.TryGetValue(scale, out var cachedAvg)) return cachedAvg;

        int block = TileW / scale;              // 한 칸이 덮는 원본 픽셀 폭
        int per = scale * scale;
        var avg = new int[TileCount * per];

        for (int t = 0; t < TileCount; t++)
        {
            int tileBase = t * TilePixels;
            for (int qy = 0; qy < scale; qy++)
            {
                for (int qx = 0; qx < scale; qx++)
                {
                    int r = 0, g = 0, b = 0, n = 0;
                    // scale 이 16의 약수가 아니면 마지막 칸이 짧아진다 — 남는 픽셀도 마지막 칸에 넣는다.
                    int y1 = qy == scale - 1 ? TileW : (qy + 1) * block;
                    int x1 = qx == scale - 1 ? TileW : (qx + 1) * block;
                    for (int y = qy * block; y < y1; y++)
                    {
                        int row = tileBase + y * TileW;
                        for (int x = qx * block; x < x1; x++)
                        {
                            int c = _rgb[_tiles[row + x]];
                            r += (c >> 16) & 0xFF;
                            g += (c >> 8) & 0xFF;
                            b += c & 0xFF;
                            n++;
                        }
                    }
                    avg[t * per + qy * scale + qx] = ((r / n) << 16) | ((g / n) << 8) | (b / n);
                }
            }
        }

        _averages[scale] = avg;
        return avg;
    }
}
