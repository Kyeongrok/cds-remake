using CdsHelper.Game.Local.Helpers;
using CdsHelper.Support.Local.Helpers;

namespace CdsHelper.Game.UI.Views;

/// <summary>
/// 지도 셰이더가 읽는 표들을 WORLD.CDS 에서 짓는다 — 물 색 표 · 수심 표 · 타일 부류 표.
/// </summary>
/// <remarks>
/// 놀이의 지도(<see cref="ShipMapHost"/>)와 개발도구의 WORLD.CDS 편집기 미리보기(<see cref="MapShaderPreview"/>)가
/// 같은 셈을 쓰도록 한 곳에 둔다. 편집기는 칠할 때마다 고친 지도로 다시 짓는다.
/// </remarks>
public static class MapShaderData
{
    /// <summary>지형표에서 강 칸의 부류.</summary>
    public const int RiverClass = 5;

    /// <summary>칸 (x, y)의 값. 짝수 행이 지도의 왼쪽 절반, 홀수 행이 오른쪽 절반이다.</summary>
    public static int Cell(byte[] world, int x, int y)
    {
        bool right = x >= WorldMapRenderer.CellW;
        int at = (y * 2 + (right ? 1 : 0)) * WorldMapRenderer.RawStride + (right ? x - WorldMapRenderer.CellW : x) * 2;
        return at + 1 < world.Length ? world[at] | (world[at + 1] << 8) : 0;
    }

    /// <summary>
    /// 팔레트 색마다 물인지 — 지도 전체에서 그 색이 <b>물 칸(부류 0·1)·강 칸(5) 타일에 쓰인 수</b>가 뭍 칸의 네 배를 넘고,
    /// 흙빛(빨강이 파랑보다 뚜렷이 큰 색)이 아니면 물이다. 강 물빛(#5A6967 · #94A199)과 해안 물결의 짙은 점도
    /// 여기 든다 — 파란 기만으로 가르면 그것들이 원본 도트로 남아 고해상도 바다와 어긋났다.
    /// </summary>
    public static byte[] WaterPalette(byte[] world, TerrainTable terrain, OceanTiles ocean)
    {
        var uses = new Dictionary<int, int>();
        for (int i = 0; i + 1 < world.Length; i += 2)
        {
            int tile = (world[i] | (world[i + 1] << 8)) & OceanTiles.TileMask;
            uses[tile] = uses.GetValueOrDefault(tile) + 1;
        }
        var wet = new long[256];
        var dry = new long[256];
        var data = ocean.TileData;
        foreach (var (tile, n) in uses)
        {
            // 강 칸(부류 5)도 물 쪽에 센다 — 강물 빛(#5F7887)은 바다 타일에는 안 나오고 강 타일에만 쓰인다.
            int kind = terrain.ClassOfCell(tile);
            bool water = kind <= TerrainTable.WaterMax || kind == RiverClass;
            int at = tile * OceanTiles.TilePixels;
            for (int k = 0; k < OceanTiles.TilePixels; k++)
                if (water) wet[data[at + k]] += n; else dry[data[at + k]] += n;
        }
        var table = new byte[256];
        for (int c = 0; c < 256; c++)
        {
            int rgb = ocean.PaletteRgb[c];
            int red = (rgb >> 16) & 0xFF, blue = rgb & 0xFF;
            bool earthy = red - blue > 5;
            table[c] = (byte)(wet[c] > 0 && wet[c] > dry[c] * 4 && !earthy ? 1 : 0);
        }
        return table;
    }

    /// <summary>
    /// 타일마다 지형 부류(지형표 값). 지도에서 그림 비트(0x8000)가 선 칸에 쓰인 타일은 7(도시·발견물 그림)로 둔다 —
    /// 세부 질감이 그림을 흐리지 않게.
    /// </summary>
    public static byte[] TileKinds(byte[] world, TerrainTable terrain)
    {
        var kinds = new byte[OceanTiles.TileCount];
        for (int t = 0; t < kinds.Length; t++) kinds[t] = (byte)terrain.ClassOfCell(t);
        for (int i = 0; i + 1 < world.Length; i += 2)
        {
            int word = world[i] | (world[i + 1] << 8);
            if ((word & 0x8000) != 0) kinds[word & OceanTiles.TileMask] = 7;
        }
        return kinds;
    }

    /// <summary>
    /// 칸마다 뭍까지의 걸음 수(0 뭍 · 1~15 물, 15 에서 멈춘다). 지형표 부류 0·1 이 물이다(<see cref="TerrainTable.WaterMax"/>).
    /// 뭍 칸 전부를 한꺼번에 띄워 너비 우선으로 번진다 — 가로는 경도 -180/180 을 잇는다.
    /// </summary>
    /// <remarks>
    /// 위 네 비트에는 <b>강 칸까지의 걸음 수</b>(0~15)를 싣는다 — 고해상도 바다가 강과 강 어귀는 원본 도트로 두고,
    /// 강에서 멀어질수록 서서히 새 바다로 넘어가게 한다(강을 매끈한 물로 바꾸면 둑이 뭉개지고 색도 어긋났다).
    /// </remarks>
    public static byte[] SeaDepth(byte[] world, TerrainTable terrain)
    {
        int w = WorldMapRenderer.UnfoldedW, h = WorldMapRenderer.CellH;
        var kind = new byte[w * h];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                kind[y * w + x] = (byte)terrain.ClassOfCell(Cell(world, x, y));

        var depth = new byte[w * h];
        var queue = new int[w * h];
        int head = 0, tail = 0;
        for (int i = 0; i < depth.Length; i++)
        {
            if (kind[i] > TerrainTable.WaterMax) queue[tail++] = i;   // 뭍 = 0
            else depth[i] = 255;                                      // 아직 모름
        }
        while (head < tail)
        {
            int i = queue[head++];
            int x = i % w, y = i / w;
            byte next = (byte)Math.Min(15, depth[i] + 1);
            Span<int> around = [y * w + (x + 1) % w, y * w + (x + w - 1) % w,
                                y > 0 ? i - w : -1, y < h - 1 ? i + w : -1];
            foreach (int j in around)
            {
                if (j < 0 || depth[j] != 255) continue;
                depth[j] = next;
                queue[tail++] = j;
            }
        }
        for (int i = 0; i < depth.Length; i++) if (depth[i] == 255) depth[i] = 15;   // 뭍이 없는 줄(극지 밖)

        var river = new byte[w * h];
        Array.Fill(river, (byte)255);
        head = tail = 0;
        for (int i = 0; i < river.Length; i++)
            if (kind[i] == RiverClass) { river[i] = 0; queue[tail++] = i; }
        while (head < tail)
        {
            int i = queue[head++];
            int x = i % w, y = i / w;
            byte next = (byte)Math.Min(15, river[i] + 1);
            Span<int> around = [y * w + (x + 1) % w, y * w + (x + w - 1) % w,
                                y > 0 ? i - w : -1, y < h - 1 ? i + w : -1];
            foreach (int j in around)
            {
                if (j < 0 || river[j] != 255) continue;
                river[j] = next;
                if (next < 15) queue[tail++] = j;
            }
        }
        for (int i = 0; i < depth.Length; i++)
            depth[i] = (byte)(depth[i] | (Math.Min((int)river[i], 15) << 4));
        return depth;
    }
}
