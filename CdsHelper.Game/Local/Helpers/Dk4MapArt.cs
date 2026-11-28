using System.IO;
using CdsHelper.Support.Local.Helpers;

namespace CdsHelper.Game.Local.Helpers;

/// <summary>
/// 대항해시대4 HD 의 세계지도 그림 — 칸 지도(<c>World.dat</c>)와 타일 그림(<c>Chip.DK4</c>).
/// 모드 창 「대항해시대4 지도 그림」을 켜면 지도를 이것으로 그린다(통행 · 항해 규칙은 WORLD.CDS 그대로).
/// </summary>
/// <remarks>
/// 두 게임의 지도는 <b>같은 격자</b>다 — 2500x1250 칸, 칸당 16비트, 동서가 이어진다. 육지 모양을 겹쳐 보면 어긋남 없이
/// 94.6% 가 맞는다(나머지는 북극 맨 윗줄 · 카스피해 · 작은 섬). 다만 WORLD.CDS 와 달리 행을 반으로 접지 않고 <c>[y*2500+x]</c> 로 그대로 놓였다.
/// <code>
///   World.dat   2500x1250 x uint16, 타일 번호 0~1023
///   Chip.DK4    G1T. +0x0C = 표 위치, 표 첫 dword = 첫 그림까지 거리. 그림 머리 +2 의 바이트가 크기(아래 네 비트 log2 폭, 위 log2 높이),
///               +0x14 부터 BC3(DXT5) 512x2048 = 32px 타일 16열 x 64행
/// </code>
/// 게임 폴더에 없는 자료라 릴리즈 <c>map-assets</c> 에 <c>DK4WORLD.DAT</c> · <c>DK4CHIP.G1T</c> 로 올려 두고 받아 쓴다(파일 이름은 ASCII).
/// 읽는 법은 Dh4Launcher 의 WorldMapKR 플러그인과 같다.
/// </remarks>
public sealed class Dk4MapArt
{
    public const int Width = 2500, Height = 1250;

    /// <summary>타일 한 변(점)과 그림 판의 열 수 · 타일 수.</summary>
    public const int TileSide = 32, AtlasCols = 16, TileCount = 1024;

    public const int AtlasWidth = AtlasCols * TileSide;                  // 512
    public const int AtlasHeight = TileCount / AtlasCols * TileSide;     // 2048

    /// <summary>밉 단계 수 — 32 · 16 · 8 · 4 · 2 · 1 점. 한 단계 내리는 2x2 평균이 타일 경계를 넘지 않는다.</summary>
    public const int MipLevels = 6;

    public const string WorldAsset = "DK4WORLD.DAT", ChipAsset = "DK4CHIP.G1T";

    /// <summary>칸마다 타일 번호(<c>[y*Width+x]</c>).</summary>
    public ushort[] Cells { get; }

    /// <summary>
    /// 단계마다 그림 판(BGRA, 알파를 곱해 둔 꼴). 0 단계가 512x2048 이다.
    /// 먼바다 타일(0 · 1 · 16 · 17)은 <b>통째로 투명</b>하다 — 대항해시대4 는 그 밑에 물을 따로 그리므로, 셰이더가 수심 빛 · 잔물결 · 해류 결이
    /// 흐르는 바다(OpenSea)를 깔고 해안 타일의 반투명 여울도 그 위에 섞는다. 예전엔 단색 하나를 깔아 바다가 밋밋했다.
    /// </summary>
    public uint[][] Mips { get; }

    private Dk4MapArt(ushort[] cells, uint[][] mips)
    {
        Cells = cells;
        Mips = mips;
    }

    /// <summary>왜 못 열었는지. 잘 열렸으면 빈 문자열.</summary>
    public static string LastError { get; private set; } = "";

    private static Dk4MapArt? _cached;

    /// <summary>받아 두거나 내려받아 연다. 한 번 연 것은 들고 있는다. 못 열면 null.</summary>
    public static Dk4MapArt? Open()
    {
        if (_cached != null) return _cached;
        LastError = "";
        try
        {
            string? worldPath = CdsAssetPath.Fetch(WorldAsset);
            string? chipPath = CdsAssetPath.Fetch(ChipAsset);
            if (worldPath == null || chipPath == null)
            {
                LastError = "대항해시대4 지도 파일을 내려받지 못했습니다";
                return null;
            }

            var raw = File.ReadAllBytes(worldPath);
            if (raw.Length != Width * Height * 2)
            {
                LastError = $"{WorldAsset} 크기가 뜻밖입니다({raw.Length}바이트)";
                return null;
            }
            var cells = new ushort[Width * Height];
            Buffer.BlockCopy(raw, 0, cells, 0, raw.Length);
            for (int i = 0; i < cells.Length; i++)
                if (cells[i] >= TileCount) cells[i] = 0;

            var atlas = DecodeChip(File.ReadAllBytes(chipPath));
            if (atlas == null)
            {
                LastError = $"{ChipAsset} 를 풀지 못했습니다";
                return null;
            }
            return _cached = new Dk4MapArt(cells, BuildMips(atlas));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            LastError = ex.Message;
            return null;
        }
    }

    /// <summary>G1T 의 첫 그림을 풀어 바다색 위에 얹은 BGRA 판으로. 모양이 다르면 null.</summary>
    private static uint[]? DecodeChip(byte[] chip)
    {
        if (chip.Length < 0x20 || chip[0] != 'G' || chip[1] != 'T' || chip[2] != '1' || chip[3] != 'G') return null;
        int table = BitConverter.ToInt32(chip, 0x0C);
        if (table < 0 || table + 4 > chip.Length) return null;
        long head = table + (long)BitConverter.ToUInt32(chip, table);
        if (head + 0x14 >= chip.Length) return null;
        byte dim = chip[head + 2];
        int w = 1 << (dim & 15), h = 1 << ((dim >> 4) & 15);
        if (w != AtlasWidth || h < AtlasHeight || head + 0x14 + (long)w * h > chip.Length) return null;

        var atlas = new uint[AtlasWidth * AtlasHeight];
        DecodeBc3(chip, (int)head + 0x14, w, AtlasHeight, atlas);
        return atlas;
    }

    private static void Rgb565(int c, out int r, out int g, out int b)
    {
        r = (((c >> 11) & 31) * 255 + 15) / 31;
        g = (((c >> 5) & 63) * 255 + 31) / 63;
        b = ((c & 31) * 255 + 15) / 31;
    }

    /// <summary>BC3(DXT5) 를 푼다 — 결과는 <b>알파를 곱해 둔</b> BGRA(0xAARRGGBB). 셰이더가 그 밑에 바다를 깐다.</summary>
    private static void DecodeBc3(byte[] src, int at, int w, int h, uint[] dst)
    {
        Span<int> alpha = stackalloc int[8];
        Span<int> cr = stackalloc int[4], cg = stackalloc int[4], cb = stackalloc int[4];
        int p = at;
        for (int by = 0; by < h; by += 4)
            for (int bx = 0; bx < w; bx += 4, p += 16)
            {
                alpha[0] = src[p];
                alpha[1] = src[p + 1];
                if (alpha[0] > alpha[1])
                    for (int i = 1; i <= 6; i++) alpha[i + 1] = ((7 - i) * alpha[0] + i * alpha[1]) / 7;
                else
                {
                    for (int i = 1; i <= 4; i++) alpha[i + 1] = ((5 - i) * alpha[0] + i * alpha[1]) / 5;
                    alpha[6] = 0;
                    alpha[7] = 255;
                }
                ulong abits = 0;
                for (int i = 0; i < 6; i++) abits |= (ulong)src[p + 2 + i] << (8 * i);

                Rgb565(src[p + 8] | (src[p + 9] << 8), out cr[0], out cg[0], out cb[0]);
                Rgb565(src[p + 10] | (src[p + 11] << 8), out cr[1], out cg[1], out cb[1]);
                cr[2] = (2 * cr[0] + cr[1]) / 3; cg[2] = (2 * cg[0] + cg[1]) / 3; cb[2] = (2 * cb[0] + cb[1]) / 3;
                cr[3] = (cr[0] + 2 * cr[1]) / 3; cg[3] = (cg[0] + 2 * cg[1]) / 3; cb[3] = (cb[0] + 2 * cb[1]) / 3;
                uint bits = BitConverter.ToUInt32(src, p + 12);

                for (int k = 0; k < 16; k++)
                {
                    int x = bx + (k & 3), y = by + (k >> 2);
                    int ci = (int)((bits >> (2 * k)) & 3);
                    int a = alpha[(int)((abits >> (3 * k)) & 7)];
                    int r = (cr[ci] * a + 127) / 255;
                    int g = (cg[ci] * a + 127) / 255;
                    int b = (cb[ci] * a + 127) / 255;
                    dst[y * w + x] = ((uint)a << 24) | ((uint)r << 16) | ((uint)g << 8) | (uint)b;
                }
            }
    }

    /// <summary>2x2 평균으로 반씩 줄인 단계들. 타일이 2의 거듭제곱 자리에 놓여 한 타일 안에서만 섞인다.</summary>
    private static uint[][] BuildMips(uint[] level0)
    {
        var mips = new uint[MipLevels][];
        mips[0] = level0;
        int w = AtlasWidth, h = AtlasHeight;
        for (int l = 1; l < MipLevels; l++)
        {
            int nw = w / 2, nh = h / 2;
            var prev = mips[l - 1];
            var next = new uint[nw * nh];
            for (int y = 0; y < nh; y++)
                for (int x = 0; x < nw; x++)
                {
                    uint a = prev[2 * y * w + 2 * x], b = prev[2 * y * w + 2 * x + 1];
                    uint c = prev[(2 * y + 1) * w + 2 * x], d = prev[(2 * y + 1) * w + 2 * x + 1];
                    uint r = (((a >> 16) & 255) + ((b >> 16) & 255) + ((c >> 16) & 255) + ((d >> 16) & 255) + 2) / 4;
                    uint g = (((a >> 8) & 255) + ((b >> 8) & 255) + ((c >> 8) & 255) + ((d >> 8) & 255) + 2) / 4;
                    uint bl = ((a & 255) + (b & 255) + (c & 255) + (d & 255) + 2) / 4;
                    uint al = ((a >> 24) + (b >> 24) + (c >> 24) + (d >> 24) + 2) / 4;
                    next[y * nw + x] = (al << 24) | (r << 16) | (g << 8) | bl;
                }
            mips[l] = next;
            w = nw;
            h = nh;
        }
        return mips;
    }

    /// <summary><see cref="Plan"/> 칸 값 — 원본 해안선을 따라 그리는 칸.</summary>
    public const ushort FollowOriginal = 0x8000;

    /// <summary><see cref="Plan"/> 칸 값 — 그 칸의 대항해시대4 타일이 물이다(<see cref="FollowOriginal"/> 칸에만 쓴다).</summary>
    public const ushort Dk4Water = 0x4000;

    /// <summary><see cref="Plan"/> 칸 값 — 원본 칸이 물이다(<see cref="FollowOriginal"/> 칸에만 쓴다).</summary>
    public const ushort CdsWater = 0x2000;

    /// <summary>
    /// 칸마다 그릴 것 — 대항해시대4 타일 번호(아래 10비트). <see cref="FollowOriginal"/> 이 선 칸은 원본 해안선을 따른다:
    /// 원본 그림(OCEAN.CDS)에서 물인 점은 대항해시대4 <b>바다 타일</b>로, 뭍인 점은 그 칸 타일이 뭍이면 그것으로, 물이면 <b>들 타일</b>로 칠한다.
    /// 함께 그 바다 타일 · 들 타일 번호를 낸다(가장 많이 쓰인 물 · 뭍 타일).
    /// </summary>
    /// <remarks>
    /// 통행은 WORLD.CDS 가 가르므로 <b>물 · 뭍이 크게 엇갈리는 자리</b>는 원본 해안선을 따른다 — 배가 육지 그림 위를 떠다니거나
    /// 원본 섬이 바다 그림에 묻히지 않게. 원본 그림을 그대로 내면 바다색이 옅어 네모난 판이 드러났다. 대항해시대4 타일마다 물인지는
    /// 그 타일이 놓인 칸의 원본 부류로 다수결한다. 해안이 한 칸 어긋난 것까지 갈면 해안을 따라 점점이 박히므로, 둘레 3x3 이 <b>모두</b>
    /// 반대인 칸만 고르고 해안이 끊기지 않게 한 칸 넓힌다. 이렇게 골라지는 곳은 북극 맨 윗줄 · 카스피해 · 작은 섬들 · 운하 자리다.
    /// </remarks>
    public (ushort[] Cells, int SeaTile, int LandTile) Plan(byte[] world, TerrainTable? terrain)
    {
        const int Half = Width / 2, Stride = 2500;
        var water = new bool[Width * Height];
        for (int y = 0; y < Height; y++)
            for (int x = 0; x < Width; x++)
            {
                bool right = x >= Half;
                int o = (y * 2 + (right ? 1 : 0)) * Stride + (right ? x - Half : x) * 2;
                int word = world[o] | (world[o + 1] << 8);
                water[y * Width + x] = terrain != null
                    ? terrain.ClassOfCell(word & TerrainTable.CellMask) <= TerrainTable.WaterMax
                    : (word & WorldCells.LandBit) == 0;
            }

        var wet = new int[TileCount];
        var dry = new int[TileCount];
        for (int i = 0; i < Cells.Length; i++)
            if (water[i]) wet[Cells[i]]++;
            else dry[Cells[i]]++;
        var tileWater = new bool[TileCount];
        for (int t = 0; t < TileCount; t++) tileWater[t] = wet[t] > dry[t];

        // 깔 타일은 온대 띠(위아래 1/5 을 뺀 곳)에서 가장 많이 쓰인 것 — 지도 전체로 세면 남극 얼음이 뭍 타일로 뽑힌다.
        var used = new int[TileCount];
        for (int y = Height / 5; y < Height * 4 / 5; y++)
            for (int x = 0; x < Width; x++) used[Cells[y * Width + x]]++;
        int seaTile = 0, landTile = 0;
        for (int t = 0; t < TileCount; t++)
        {
            if (tileWater[t] && used[t] > used[seaTile]) seaTile = t;
            if (!tileWater[t] && (tileWater[landTile] || used[t] > used[landTile])) landTile = t;
        }

        var strong = new bool[Width * Height];
        for (int y = 0; y < Height; y++)
            for (int x = 0; x < Width; x++)
            {
                bool want = water[y * Width + x], all = true;
                for (int dy = -1; dy <= 1 && all; dy++)
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        int ny = Math.Clamp(y + dy, 0, Height - 1), nx = (x + dx + Width) % Width;
                        if (tileWater[Cells[ny * Width + nx]] == want) { all = false; break; }
                    }
                strong[y * Width + x] = all;
            }

        var outp = (ushort[])Cells.Clone();
        for (int y = 0; y < Height; y++)
            for (int x = 0; x < Width; x++)
            {
                bool near = false;
                for (int dy = -1; dy <= 1 && !near; dy++)
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        int ny = y + dy;
                        if (ny < 0 || ny >= Height) continue;
                        if (strong[ny * Width + (x + dx + Width) % Width]) { near = true; break; }
                    }
                if (!near) continue;
                int i = y * Width + x;
                outp[i] = (ushort)(FollowOriginal | Cells[i]
                                   | (tileWater[Cells[i]] ? Dk4Water : 0) | (water[i] ? CdsWater : 0));
            }
        return (outp, seaTile, landTile);
    }

    /// <summary>대항해시대4 그림을 안 건 칸 — 원본(OCEAN.CDS) 그림으로 그린다.</summary>
    public const ushort UseOriginal = 0xFFFF;
}
