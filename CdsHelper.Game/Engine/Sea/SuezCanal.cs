using CdsHelper.Game.Local.Helpers;
using CdsHelper.Game.Local.Settings;
using CdsHelper.Support.Local.Helpers;

namespace CdsHelper.Game.Engine.Sea;

/// <summary>
/// 모드 「수에즈 운하」 — 지중해와 수에즈만 사이 지협에 배가 지나갈 바닷길을 뚫는다(원본에 없는 것, 운하는 1869년).
/// </summary>
/// <remarks>
/// 세계지도(WORLD.CDS)를 읽어 들일 때 운하 자리의 뭍 칸을 바다 칸으로 갈아 끼운다 — 그림 · 지나갈 수 있는지 · 자동항해
/// 길찾기가 다 같은 지도 낱말을 보므로 한 번에 맞는다. 파일은 안 건드린다.
/// <code>
///   지중해 해안  (1474, 407)   포트사이드 — 북위 31.3 동경 32.3
///   수에즈만 끝  (1474, 416)   수에즈     — 북위 30.0 동경 32.5
///   뚫는 칸      x 1474~1475, y 407~417  (두 칸 폭, 지중해 해안 한 줄과 만 끝 두 줄은 물이어도 덮는다)
/// </code>
/// 칸은 위경도 1도에 6.94칸(가로 2500 · 세로 1250)이다. 도시 · 발견물 그림 칸(0x8000)과 이미 물인 칸은 건드리지 않는다.
/// 모드 창에서 켜고 끄면 지도 화면이 곧바로 다시 건다(<see cref="Capture"/> 로 원래 칸을 들고 있다가 끌 때 되돌린다).
/// </remarks>
public static class SuezCanal
{
    /// <summary>뚫는 칸 네모. 아래 두 줄(416~417)은 수에즈만 끝 칸이라 물이어도 해안 타일로 덮는다 — 그 칸 그림의
    /// 위쪽이 뭍이라 운하와 만 사이에 뭍 띠가 남아 보였다.</summary>
    private const int Left = 1474, Right = 1475, Top = 407, Bottom = 417, GulfTop = 416, MedBottom = 407;

    /// <summary>바다 칸 그림을 베껴 올 탁 트인 지중해 칸 — 해안 타일을 못 쓸 때 물러설 몫.</summary>
    private const int SeaX = 1470, SeaY = 398;

    /// <summary>
    /// 운하 두 줄에 까는 해안 타일 — 왼쪽 줄은 「왼쪽이 뭍, 오른쪽이 바다」(0x000F), 오른쪽 줄은 그 반대(0x001C).
    /// 지도에서 곧게 세로로 뻗은 해안에 쓰인 물 칸 타일을 골랐다 — 둘 다 배가 지나가는 칸(부류 0)이다.
    /// 통짜 바다 타일을 깔면 뭍 사이에 네모난 물 판이 박혀 어색했다.
    /// </summary>
    private const int WestCoast = 0x000F, EastCoast = 0x001C;

    /// <summary>운하 자리의 원래 낱말들 — 끌 때 되돌리려고 읽어 둔다(<see cref="Restore"/>).</summary>
    public static int[] Capture(byte[] world)
    {
        var words = new int[(Right - Left + 1) * (Bottom - Top + 1)];
        int k = 0;
        for (int y = Top; y <= Bottom; y++)
            for (int x = Left; x <= Right; x++)
                words[k++] = Word(world, x, y);
        return words;
    }

    /// <summary>운하 자리를 읽어 둔 원래 낱말로 되돌린다.</summary>
    public static void Restore(byte[] world, int[] words)
    {
        int k = 0;
        for (int y = Top; y <= Bottom; y++)
            for (int x = Left; x <= Right; x++)
                if (k < words.Length) SetWord(world, x, y, words[k++]);
    }

    /// <summary>모드를 켰으면 지도 낱말에 운하를 뚫는다. 뚫은 칸 수를 낸다.</summary>
    public static int Apply(byte[] world, TerrainTable? terrain)
    {
        if (!GameSettings.SuezCanal || terrain == null) return 0;
        if (world.Length < WorldMapRenderer.RawStride * WorldMapRenderer.CellH * 2) return 0;

        int sea = Word(world, SeaX, SeaY);
        if (!terrain.CanSail(sea)) return 0;   // 지도가 달라 바다 칸을 못 찾으면 손대지 않는다

        int west = terrain.CanSail(WestCoast) ? WestCoast : sea;
        int east = terrain.CanSail(EastCoast) ? EastCoast : sea;
        int dug = 0;
        for (int y = Top; y <= Bottom; y++)
            for (int x = Left; x <= Right; x++)
            {
                int word = Word(world, x, y);
                // 양 끝(지중해 해안 한 줄 · 수에즈만 끝 두 줄)은 물이어도 덮는다 — 해안 칸 그림에 뭍이 그려져 있어 입구가 막혀 보였다.
                bool mouth = y <= MedBottom || y >= GulfTop;
                if ((word & 0x8000) != 0 || (terrain.CanSail(word) && !mouth)) continue;
                // 지중해 쪽 입구 줄은 그냥 바다 — 해안 타일을 깔면 둑이 바다로 삐져나와 보였다.
                SetWord(world, x, y, y <= MedBottom ? sea : x == Left ? west : x == Right ? east : sea);
                dug++;
            }
        return dug;
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
