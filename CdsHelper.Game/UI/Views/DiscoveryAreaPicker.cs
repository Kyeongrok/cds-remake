using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CdsHelper.Support.Local.Models;

namespace CdsHelper.Game.UI.Views;

/// <summary>
/// 발견물 자리(칸 범위)를 세계지도에서 끌어 고른다 — 원본에 없는 것이다(<see cref="UserDiscoveryDialog"/>).
/// </summary>
/// <remarks>
/// 창은 도시락 「세계지도」(<see cref="LiveMapWindow"/>)를 고르기 모드로 띄운 것이다 — 같은 게임 지도 그림 · 확대 · 이동 ·
/// 좌표 줄을 쓰고, 왼쪽 단추로 끌어 범위를 잡는다. 다른 발견물 자리는 파란 네모로 깔아 겹치지 않게 본다.
/// 육지인지는 고른 범위 칸 가운데 육지가 바다보다 많은지로 정하고, 창에서 바꿀 수 있다.
/// </remarks>
public static class DiscoveryAreaPicker
{
    /// <summary>창이 쓰는 지도.</summary>
    /// <param name="Chart">항해지도 그림(점 하나가 칸 4x4) — 양피지 바탕이자, 게임 지도를 못 지을 때 갈음하는 바탕.</param>
    /// <param name="IsLand">칸이 육지인지.</param>
    /// <param name="Others">깔아 둘 다른 발견물 자리(X1, Y1, X2, Y2, 이름).</param>
    public sealed record Map(BitmapSource Chart, Func<int, int, bool?> IsLand,
                             IReadOnlyList<(int X1, int Y1, int X2, int Y2, string Name)> Others)
    {
        /// <summary>게임 지도 타일 그림(<see cref="Rendering.WorldMapHost.GameTiles"/>). 지도 창이 지도를 안 읽었으면 null — 그때는 <see cref="Chart"/> 만 쓴다.</summary>
        public Func<int, int, int, int, int, uint[]?>? Tiles { get; init; }

        /// <summary>지도에 찍을 도시(이름 · 칸 · 도서관).</summary>
        public Func<IReadOnlyList<(string Name, double X, double Y, bool Library)>>? Cities { get; init; }

        /// <summary>내 배 자리(칸). 모르면 null.</summary>
        public Func<(double X, double Y)?>? Ship { get; init; }

        /// <summary>바탕 덮개(안 선 도시 · 못 찾은 발견물)가 바뀔 때마다 오르는 번호.</summary>
        public Func<int>? MapVersion { get; init; }
    }

    /// <summary>고른 범위와 육지인지.</summary>
    public readonly record struct Area(int X1, int Y1, int X2, int Y2, bool Land);

    /// <summary>게임 폴더에서 지어 둔 지도 그림과 칸 — 한 번 지으면 들고 있는다.</summary>
    private static (string Dir, BitmapSource Chart, Func<int, int, bool?> IsLand)? _fromGame;

    private const int Cells = ExploredMap.CellsPerBlock;

    /// <summary>
    /// 지도 창 없이 게임 폴더의 <c>WORLD.CDS</c> 로 지도를 짓는다 — 항해지도와 같은 셈(점 하나가 칸 4x4, 육지가 많으면 육지색)이다.
    /// 지도 창이 지도를 아직 안 읽었거나(도시 · 첫 화면) 지도 창 밖에서 열었을 때 쓴다. 못 읽으면 null.
    /// </summary>
    public static Map? FromGame(Engine.Game? game)
    {
        if (game is not { Directory.Length: > 0 }) return null;
        if (_fromGame is not { } made || made.Dir != game.Directory)
        {
            if (Support.Local.Helpers.WorldMapRenderer.LoadWorldData(Support.Local.Helpers.CdsAssetPath.Resolve(game.Directory, "WORLD.CDS")) is not { } world
                || Local.Helpers.TerrainTable.Open(game.Directory) is not { } terrain)
                return null;

            int w = ExploredMap.Width, h = ExploredMap.Height;
            var dots = new uint[w * h];
            uint Color(byte index)
            {
                int k = index * 3;
                return 0xFF000000u | ((uint)Local.Helpers.GamePalette.Rgb[k] << 16)
                     | ((uint)Local.Helpers.GamePalette.Rgb[k + 1] << 8) | Local.Helpers.GamePalette.Rgb[k + 2];
            }
            for (int by = 0; by < h; by++)
                for (int bx = 0; bx < w; bx++)
                {
                    int sea = 0;
                    for (int y = 0; y < Cells; y++)
                        for (int x = 0; x < Cells; x++)
                            if (Engine.Sea.SeaPathfinder.IsSeaCell(world, terrain, bx * Cells + x, by * Cells + y)) sea++;
                    // 항해지도 색(0x00416AED) — 바다 0x2E · 육지 0x18, 같으면 바다다.
                    dots[by * w + bx] = Color(Cells * Cells - sea > sea ? (byte)0x18 : (byte)0x2E);
                }
            var chart = BitmapSource.Create(w, h, 96, 96, PixelFormats.Bgra32, null, dots, w * 4);
            chart.Freeze();
            made = (game.Directory, chart, (x, y) => !Engine.Sea.SeaPathfinder.IsSeaCell(world, terrain, x, y));
            _fromGame = made;
        }
        var others = (game.Discoveries?.Table?.Discoveries ?? [])
            .Where(r => r.HasPlace).Select(r => (r.X1, r.Y1, r.X2, r.Y2, r.Name)).ToList();
        return new Map(made.Chart, made.IsLand, others);
    }

    /// <summary>세계지도 창을 고르기 모드로 띄워 범위를 고르게 한다. 그만두면 null.</summary>
    public static Area? Pick(Window owner, Map map, Area? start)
    {
        // 양피지 바탕은 항해지도 그림 그대로다 — 한 번만 펴 둔다.
        var chart = map.Chart;
        int w = chart.PixelWidth, h = chart.PixelHeight;
        uint[]? pixels = null;
        (uint[] Pixels, int Width, int Height)? Parchment()
        {
            if (pixels == null)
            {
                var bgra = chart.Format == PixelFormats.Bgra32 ? chart : new FormatConvertedBitmap(chart, PixelFormats.Bgra32, null, 0);
                pixels = new uint[w * h];
                bgra.CopyPixels(pixels, w * 4, 0);
            }
            return (pixels, w, h);
        }

        var src = new LiveMapWindow.Source(
            Ship: map.Ship ?? (() => null),
            MapVersion: map.MapVersion ?? (() => 0),
            Tiles: map.Tiles ?? ((_, _, _, _, _) => null),
            Parchment: Parchment,
            IsLand: map.IsLand,
            Cities: map.Cities ?? (() => []),
            Spots: () => []);
        return LiveMapWindow.PickArea(owner, src, new LiveMapWindow.PickSetup(start, map.Others));
    }
}
