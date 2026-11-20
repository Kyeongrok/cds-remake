using CdsHelper.Game.Local.Helpers;
using CdsHelper.Game.Local.Settings;
using CdsHelper.Support.Local.Helpers;

namespace CdsHelper.Game.Engine.Sea;

/// <summary>
/// 모드 「수에즈 운하」 · 「파나마 운하」 — 지협에 배가 지나갈 바닷길을 뚫는다(원본에 없는 것, 운하는 1869년 · 1914년).
/// </summary>
/// <remarks>
/// 세계지도(WORLD.CDS)를 읽어 들일 때 운하 자리의 뭍 칸을 바다 칸으로 갈아 끼운다 — 그림 · 지나갈 수 있는지 · 자동항해
/// 길찾기가 다 같은 지도 낱말을 보므로 한 번에 맞는다. 파일은 안 건드린다.
/// <code>
///   수에즈   지중해 해안 (1474, 407) 포트사이드 — 수에즈만 끝 (1474, 416)
///            뚫는 칸 x 1474~1475, y 407~417  (지중해 쪽 한 줄은 바다, 만 끝 두 줄은 물이어도 해안 타일로 덮는다)
///   파나마   카리브해 (701, 559) — 파나마만 (701, 562), 도시 파나마(699~700, 561~562) 바로 동쪽
///            뚫는 칸 x 701~702, y 559~562    (카리브해 쪽 한 줄은 바다, 파나마만 쪽 한 줄은 해안 타일로 덮는다)
/// </code>
/// 칸은 위경도 1도에 6.94칸(가로 2500 · 세로 1250)이다 — x = (경도 + 180) × 6.94, y = (90 − 위도) × 6.94.
/// 도시 · 발견물 그림 칸(0x8000)과 이미 물인 칸(입구 줄 빼고)은 건드리지 않는다.
/// 모드 창에서 켜고 끄면 지도 화면이 곧바로 다시 건다(<see cref="Capture"/> 로 원래 칸을 들고 있다가 끌 때 되돌린다).
/// </remarks>
public static class Canals
{
    /// <summary>
    /// 운하 하나 — 뚫는 칸 네모, 바다로 덮을 입구 줄(<paramref name="SeaMouth"/> 이하), 해안 타일로 덮을 입구 줄
    /// (<paramref name="CoastMouth"/> 이상), 바다 칸 그림을 베껴 올 탁 트인 칸.
    /// </summary>
    private sealed record Canal(Func<bool> On, int Left, int Right, int Top, int Bottom,
                                int SeaMouth, int CoastMouth, int SeaX, int SeaY);

    /// <summary>
    /// 수에즈 — 아래 두 줄(416~417)은 수에즈만 끝 칸이라 물이어도 해안 타일로 덮는다(그 칸 그림의 위쪽이 뭍이라
    /// 운하와 만 사이에 뭍 띠가 남아 보였다). 지중해 쪽 입구 줄은 그냥 바다 — 해안 타일을 깔면 둑이 바다로 삐져나와 보였다.
    /// </summary>
    private static readonly Canal Suez = new(() => GameSettings.SuezCanal, 1474, 1475, 407, 417, 407, 416, 1470, 398);

    /// <summary>파나마 — 카리브해 쪽 한 줄(559)은 바다, 파나마만 쪽 한 줄(562)은 해안 타일.</summary>
    private static readonly Canal Panama = new(() => GameSettings.PanamaCanal, 701, 702, 559, 562, 559, 562, 690, 545);

    private static readonly Canal[] All = [Suez, Panama];

    /// <summary>
    /// 운하 두 줄에 까는 해안 타일 — 왼쪽 줄은 「왼쪽이 뭍, 오른쪽이 바다」(0x000F), 오른쪽 줄은 그 반대(0x001C).
    /// 지도에서 곧게 세로로 뻗은 해안에 쓰인 물 칸 타일을 골랐다 — 둘 다 배가 지나가는 칸(부류 0)이다.
    /// 통짜 바다 타일을 깔면 뭍 사이에 네모난 물 판이 박혀 어색했다.
    /// </summary>
    private const int WestCoast = 0x000F, EastCoast = 0x001C;

    /// <summary>운하 자리들의 원래 낱말 — 끌 때 되돌리려고 읽어 둔다(<see cref="Restore"/>).</summary>
    public static int[] Capture(byte[] world) =>
        [.. All.SelectMany(c => Cells(c).Select(p => Word(world, p.X, p.Y)))];

    /// <summary>운하 자리들을 읽어 둔 원래 낱말로 되돌린다.</summary>
    public static void Restore(byte[] world, int[] words)
    {
        int k = 0;
        foreach (var (x, y) in All.SelectMany(Cells))
            if (k < words.Length) SetWord(world, x, y, words[k++]);
    }

    /// <summary>켠 운하마다 지도 낱말에 바닷길을 뚫는다. 뚫은 칸 수를 낸다.</summary>
    public static int Apply(byte[] world, TerrainTable? terrain)
    {
        if (terrain == null) return 0;
        if (world.Length < WorldMapRenderer.RawStride * WorldMapRenderer.CellH * 2) return 0;
        int dug = 0;
        foreach (var canal in All)
            if (canal.On()) dug += Dig(world, terrain, canal);
        return dug;
    }

    private static int Dig(byte[] world, TerrainTable terrain, Canal c)
    {
        int sea = Word(world, c.SeaX, c.SeaY);
        if (!terrain.CanSail(sea)) return 0;   // 지도가 달라 바다 칸을 못 찾으면 손대지 않는다

        int west = terrain.CanSail(WestCoast) ? WestCoast : sea;
        int east = terrain.CanSail(EastCoast) ? EastCoast : sea;
        int dug = 0;
        foreach (var (x, y) in Cells(c))
        {
            int word = Word(world, x, y);
            // 양 끝 입구 줄은 물이어도 덮는다 — 해안 칸 그림에 뭍이 그려져 있어 입구가 막혀 보였다.
            bool mouth = y <= c.SeaMouth || y >= c.CoastMouth;
            if ((word & 0x8000) != 0 || (terrain.CanSail(word) && !mouth)) continue;
            SetWord(world, x, y, y <= c.SeaMouth ? sea : x == c.Left ? west : x == c.Right ? east : sea);
            dug++;
        }
        return dug;
    }

    private static IEnumerable<(int X, int Y)> Cells(Canal c)
    {
        for (int y = c.Top; y <= c.Bottom; y++)
            for (int x = c.Left; x <= c.Right; x++)
                yield return (x, y);
    }

    private static int Offset(int x, int y)
    {
        bool right = x >= WorldMapRenderer.CellW;
        int col = right ? x - WorldMapRenderer.CellW : x;
        return (y * 2 + (right ? 1 : 0)) * WorldMapRenderer.RawStride + col * 2;
    }

    private static int Word(byte[] world, int x, int y)
    {
        int off = Offset(x, y);
        return world[off] | (world[off + 1] << 8);
    }

    private static void SetWord(byte[] world, int x, int y, int word)
    {
        int off = Offset(x, y);
        world[off] = (byte)word;
        world[off + 1] = (byte)(word >> 8);
    }
}
