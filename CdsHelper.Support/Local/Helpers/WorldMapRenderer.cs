using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CdsHelper.Support.Local.Models;

namespace CdsHelper.Support.Local.Helpers;

/// <summary>
/// WORLD.CDS 파일을 읽어 단일 타일(2500x1250) 비트맵으로 렌더링하는 헬퍼.
/// 색상/좌표 변환 로직은 WorldMapContent와 일치한다 — OCEAN.CDS가 있으면 게임 타일 그림을,
/// 없으면 MapPalette로 계산한 색을 쓴다.
/// </summary>
public static class WorldMapRenderer
{
    public const int RawStride = 2500;   // bytes per row in file
    public const int CellW = 1250;       // cells per row (2 bytes/cell)
    public const int CellH = 1250;       // unfolded rows (2500 raw / 2)
    public const int UnfoldedW = 2500;   // unfolded width (left half + right half)

    /// <summary>
    /// WORLD.CDS 파일을 로드한다. 유효하지 않으면 null 반환.
    /// </summary>
    public static byte[]? LoadWorldData(string path)
    {
        if (!File.Exists(path))
            path = CdsAssetPath.Resolve("", "WORLD.CDS");
        if (!File.Exists(path)) return null;
        var data = File.ReadAllBytes(path);
        if (data.Length != RawStride * CellH * 2) return null;
        return data;
    }

    /// <summary>
    /// WORLD.CDS 데이터로부터 2500x1250 단일 타일을 렌더링한 WriteableBitmap 생성.
    /// ocean이 주어지면 게임 타일 그림(칸당 16x16의 평균색)으로 그리고, null이면 palette로 색을 계산한다.
    /// palette가 null이면 기본 팔레트를 사용한다.
    /// </summary>
    public static WriteableBitmap RenderSingleTile(byte[] worldData, MapPalette? palette = null, bool showCoast = true, bool showWind = false, OceanTiles? ocean = null)
    {
        palette ??= MapPalette.CreateDefault();
        var pixels = new int[UnfoldedW * CellH];
        // 칸 하나가 픽셀 하나이므로 타일 전체의 평균색 하나면 된다.
        var avg = ocean?.GetAverages(1);

        for (int ry = 0; ry < CellH; ry++)
        {
            int evenRow = ry * 2;
            int oddRow = ry * 2 + 1;

            for (int cx = 0; cx < CellW; cx++)
            {
                int offE = evenRow * RawStride + cx * 2;
                int offO = oddRow * RawStride + cx * 2;

                int colorLeft, colorRight;
                if (avg != null)
                {
                    colorLeft = avg[CellToTile(worldData, offE)];
                    colorRight = avg[CellToTile(worldData, offO)];
                }
                else
                {
                    byte tE = (byte)(worldData[offE] & 0x7F);
                    byte aE = worldData[offE + 1];
                    byte tO = (byte)(worldData[offO] & 0x7F);
                    byte aO = worldData[offO + 1];
                    colorLeft = ColorToInt(GetCellColor(palette, tE, aE, showWind, showCoast));
                    colorRight = ColorToInt(GetCellColor(palette, tO, aO, showWind, showCoast));
                }

                pixels[ry * UnfoldedW + cx] = colorLeft;
                pixels[ry * UnfoldedW + cx + CellW] = colorRight;
            }
        }

        var bmp = new WriteableBitmap(UnfoldedW, CellH, 96, 96, PixelFormats.Bgr32, null);
        bmp.WritePixels(new Int32Rect(0, 0, UnfoldedW, CellH), pixels, UnfoldedW * 4, 0);
        return bmp;
    }

    /// <summary>위도/경도를 단일 타일 기준 픽셀 좌표로 변환.</summary>
    public static (double px, double py) LatLonToPixel(double lat, double lon)
    {
        double cellX = (lon + 180.0) / 360.0 * UnfoldedW;
        double cellY = (90.0 - lat) / 180.0 * CellH;
        return (cellX, cellY);
    }

    /// <summary>단일 타일 픽셀 좌표를 위도/경도로 역변환.</summary>
    public static (double lat, double lon) PixelToLatLon(double px, double py)
    {
        double lon = px * 360.0 / UnfoldedW - 180;
        double lat = 90.0 - py * 180.0 / CellH;
        return (lat, lon);
    }

    /// <summary>
    /// WORLD.CDS 칸(2바이트 리틀엔디안)에서 OCEAN.CDS 타일 번호를 뽑는다.
    /// 게임도 하위 14비트만 쓴다(0x48A40A 의 <c>and cx,0x3FFF</c>).
    /// </summary>
    public static int CellToTile(byte[] worldData, int offset) =>
        (worldData[offset] | (worldData[offset + 1] << 8)) & OceanTiles.TileMask;

    /// <summary>
    /// 지형(0~127) x 속성(0~255) 조합의 색을 미리 계산한 표. 자리는 <c>terrain * 256 + attr</c>,
    /// 값은 0xRRGGBB. OCEAN.CDS가 없을 때 칸마다 색을 다시 계산하지 않으려고 쓴다.
    /// </summary>
    public static int[] BuildCellColorLut(MapPalette? palette, bool showCoast = true, bool showWind = false)
    {
        palette ??= MapPalette.CreateDefault();
        var lut = new int[128 * 256];
        for (int t = 0; t < 128; t++)
            for (int a = 0; a < 256; a++)
                lut[t * 256 + a] = ColorToInt(GetCellColor(palette, (byte)t, (byte)a, showWind, showCoast));
        return lut;
    }

    #region 색상 변환 (WorldMapContent와 동일)

    private static Color GetCellColor(MapPalette palette, byte terrain, byte attr, bool showWind, bool showCoast)
    {
        if (terrain == 0)
        {
            if (attr == 0) return palette.ResolveCoastline();
            return showWind ? palette.ResolveWind(attr) : palette.ResolveSeaBase();
        }
        if (terrain == 1)
            return palette.ResolveLand(attr);
        float landRatio = GetCoastLandRatio(terrain);
        if (!showCoast)
            landRatio = Math.Clamp(landRatio, 0.2f, 0.8f);
        var sea = palette.ResolveSeaBase();
        var land = attr <= 10 ? palette.ResolveLand(0) : palette.ResolveLand(attr);
        return BlendColor(sea, land, landRatio);
    }

    private static Color BlendColor(Color a, Color b, float t)
    {
        return Color.FromRgb(
            (byte)(a.R + (b.R - a.R) * t),
            (byte)(a.G + (b.G - a.G) * t),
            (byte)(a.B + (b.B - a.B) * t));
    }

    private static int ColorToInt(Color c) => (c.R << 16) | (c.G << 8) | c.B;

    /// <summary>지형별 육지 비율(0~1). 해안 칸의 바다색/육지색 섞는 비율이자 셀 정보 표시에 쓴다.</summary>
    public static float GetCoastLandRatio(byte terrain)
    {
        return terrain switch
        {
            2 => 0.39f, 3 => 0.18f, 4 => 0.49f, 5 => 0.00f,
            6 => 0.50f, 7 => 0.43f, 8 => 0.52f, 9 => 0.50f,
            10 => 0.14f, 11 => 0.55f, 12 => 0.29f, 13 => 0.00f,
            14 => 0.11f, 15 => 0.55f, 16 => 0.42f, 17 => 0.35f,
            18 => 0.84f, 19 => 0.43f, 20 => 0.50f, 21 => 0.03f,
            22 => 0.00f, 23 => 0.29f, 24 => 0.43f, 25 => 0.31f,
            26 => 0.00f, 27 => 0.00f, 28 => 0.00f, 29 => 0.29f,
            30 => 0.12f, 31 => 0.11f, 32 => 0.27f, 33 => 0.27f,
            34 => 0.60f, 35 => 0.51f, 36 => 0.12f, 37 => 0.54f,
            38 => 0.29f, 39 => 0.66f, 40 => 0.00f, 41 => 0.25f,
            42 => 1.00f, 43 => 0.78f, 44 => 0.00f, 45 => 0.57f,
            46 => 0.50f, 48 => 0.92f, 49 => 0.54f, 50 => 0.28f,
            51 => 0.14f, 53 => 0.99f, 55 => 0.78f, 56 => 0.00f,
            57 => 0.83f, 58 => 0.34f, 59 => 0.40f, 60 => 1.00f,
            61 => 0.04f, 62 => 0.10f, 64 => 0.00f, 65 => 0.06f,
            66 => 0.97f, 67 => 0.58f, 68 => 0.17f, 70 => 0.49f,
            71 => 0.26f, 72 => 1.00f, 73 => 0.12f, 75 => 0.91f,
            76 => 0.96f, 77 => 0.62f, 78 => 1.00f, 79 => 0.49f,
            81 => 0.66f, 82 => 0.99f, 83 => 0.03f, 84 => 0.93f,
            85 => 0.23f, 92 => 0.44f, 96 => 0.96f, 103 => 1.00f,
            118 => 0.94f,
            _ => Math.Min(terrain / 127f, 1f),
        };
    }

    #endregion
}
