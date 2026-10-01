using System.IO;
using System.Text;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace CdsHelper.Game.Local.Helpers;

/// <summary>
/// 도시 그림(CITYCG.CDS)에 박힌 <b>건물 그림</b>을 문화권·건물 갈래마다 뽑는다.
/// </summary>
/// <remarks>
/// 같은 문화권의 같은 건물(리스본·세비야의 술집 따위)은 <b>같은 그림</b>이 건물 상자(96x80, 건물표 X·Y) 안 같은 자리에 찍혀 있다.
/// 다만 도시 그림은 색 74번 위를 <b>그림마다 제 팔레트</b>로 칠해 같은 건물도 색 번호·색 값이 조금씩 다르다 — 그래서 점을
/// 「색이 가까운가」(RGB 거리 <see cref="Tolerance"/> 안)로 견준다.
///
/// 한 문화권·갈래 안에도 모양이 두세 가지 섞여 있다(교회·교역소 따위). 그래서 먼저 <b>변형</b>으로 가른다 — 서로 가까운 점이
/// <see cref="SameShape"/> 개를 넘는 도시끼리 한 변형이고, 가장 많은 도시와 닮은 도시를 기준으로 삼아 묶는다.
/// 변형 안에서는 <see cref="Agreement"/> 넘는 도시에서 기준과 가까운 점만 남긴다 — 배경은 도시마다 달라 빠지고 건물만 남는다.
///
/// 건물 색은 기준 도시의 색이다. 다른 도시에 얹을 때는 그 도시 팔레트의 가장 가까운 색으로 바꿔 칠하면 원본과 맞는다.
/// </remarks>
public static class BuildingSprites
{
    /// <summary>건물 상자 크기.</summary>
    public const int BoxW = CityBuildingTable.BoxWidth, BoxH = CityBuildingTable.BoxHeight;

    /// <summary>같은 점으로 볼 RGB 거리.</summary>
    public const int Tolerance = 24;

    /// <summary>두 도시 상자가 같은 모양으로 볼 가까운 점 수.</summary>
    public const int SameShape = 900;

    /// <summary>변형 안에서 건물 점으로 남길 도시 비율.</summary>
    public const double Agreement = 0.7;

    /// <summary>뽑을 폴더 이름.</summary>
    public const string Folder = "asset/buildingsprite";

    /// <summary>뽑은 건물 그림 하나 — 문화권 · 갈래 · 변형 번호 · 쓰는 건물들 · 96x80 BGRA(비침 알파 0) · 남은 점 수.</summary>
    public sealed record Sprite(string Culture, string Kind, int Variant, List<CityBuildingTable.Building> Members,
                                uint[] Bgra, int Points)
    {
        public string FileName => $"{Culture}_{Kind}_{Variant}.png";
        public override string ToString() => $"{Culture} · {Kind} {Variant + 1}  ({Members.Count}곳 · {Points}점)";
    }

    /// <summary>모든 문화권·갈래·변형의 건물 그림을 뽑는다. <paramref name="cultureOf"/> 는 도시 → 문화권 이름.</summary>
    public static List<Sprite> Extract(CityPictures pictures, CityBuildingTable table, Func<int, string> cultureOf,
                                       int minMembers = 2)
    {
        var result = new List<Sprite>();
        foreach (var byCulture in table.Buildings.GroupBy(b => cultureOf(b.City)).OrderBy(g => g.Key))
        {
            // 한 문화권씩 그림을 들고 놓는다 — 226장을 다 들면 백 MB 가 넘는다.
            var cache = new Dictionary<int, uint[]?>();
            uint[]? Pic(int city) => cache.TryGetValue(city, out var p) ? p : cache[city] = pictures.TryGetBgra(city);

            foreach (var byKind in byCulture.GroupBy(b => b.Kind).OrderBy(g => g.Key))
            {
                var rest = byKind.Where(b => Pic(b.City) != null).ToList();
                int variant = 0;
                while (rest.Count >= minMembers)
                {
                    // 가장 많은 도시와 닮은 것을 기준으로.
                    var similar = rest.Select(a => rest.Where(b => Same(Pic(a.City)!, a, Pic(b.City)!, b) >= SameShape).ToList()).ToList();
                    int best = Enumerable.Range(0, rest.Count).MaxBy(i => similar[i].Count);
                    var members = similar[best];
                    if (members.Count < minMembers) break;
                    var refB = rest[best];
                    var (bgra, points) = Consensus(Pic(refB.City)!, refB, members.Select(m => (Pic(m.City)!, m)).ToList());
                    result.Add(new Sprite(byCulture.Key, byKind.Key, variant++, members, bgra, points));
                    rest = [.. rest.Except(members)];
                }
            }
        }
        return result;
    }

    private static bool Near(uint a, uint b)
    {
        int dr = (int)((a >> 16) & 255) - (int)((b >> 16) & 255);
        int dg = (int)((a >> 8) & 255) - (int)((b >> 8) & 255);
        int db = (int)(a & 255) - (int)(b & 255);
        return dr * dr + dg * dg + db * db <= Tolerance * Tolerance;
    }

    private static uint At(uint[] pic, int x, int y) =>
        x >= 0 && y >= 0 && x < CityPictures.Width && y < CityPictures.Height ? pic[y * CityPictures.Width + x] : 0;

    /// <summary>두 건물 상자에서 색이 가까운 점 수.</summary>
    private static int Same(uint[] pa, CityBuildingTable.Building a, uint[] pb, CityBuildingTable.Building b)
    {
        int same = 0;
        for (int y = 0; y < BoxH; y++)
            for (int x = 0; x < BoxW; x++)
                if (Near(At(pa, a.X + x, a.Y + y), At(pb, b.X + x, b.Y + y))) same++;
        return same;
    }

    private static (uint[] Bgra, int Points) Consensus(uint[] refPic, CityBuildingTable.Building refB,
                                                       List<(uint[] Pic, CityBuildingTable.Building B)> members)
    {
        var bgra = new uint[BoxW * BoxH];
        int points = 0;
        for (int y = 0; y < BoxH; y++)
            for (int x = 0; x < BoxW; x++)
            {
                uint c = At(refPic, refB.X + x, refB.Y + y);
                if (c == 0) continue;
                int agree = members.Count(m => Near(At(m.Pic, m.B.X + x, m.B.Y + y), c));
                if (agree < members.Count * Agreement) continue;
                bgra[y * BoxW + x] = c | 0xFF000000u;
                points++;
            }
        return (bgra, points);
    }

    /// <summary>뽑은 그림을 PNG 와 목록(<c>buildings.txt</c>)으로 적는다.</summary>
    public static void Export(string folder, IReadOnlyList<Sprite> sprites, Func<int, string> cityName)
    {
        Directory.CreateDirectory(folder);
        var list = new StringBuilder();
        list.AppendLine("# 도시 그림의 건물 그림 — 문화권_갈래_변형.png (96x80, 비침 알파 0). 색은 기준 도시 것이다.");
        foreach (var s in sprites)
        {
            var bmp = BitmapSource.Create(BoxW, BoxH, 96, 96, PixelFormats.Bgra32, null, s.Bgra, BoxW * 4);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bmp));
            using (var file = File.Create(Path.Combine(folder, s.FileName))) encoder.Save(file);
            list.AppendLine($"{s.FileName}  점 {s.Points}  도시 {s.Members.Count}곳: "
                            + string.Join(", ", s.Members.Select(m => cityName(m.City))));
        }
        File.WriteAllText(Path.Combine(folder, "buildings.txt"), list.ToString(), new UTF8Encoding(true));
    }

    /// <summary>뽑을 폴더 — 저장소 안이면 소스 옆 <c>asset/buildingsprite</c>, 아니면 <c>%APPDATA%</c>.</summary>
    public static string SaveDirectory()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        for (int up = 0; up < 8 && dir != null; up++, dir = dir.Parent)
            if (Directory.Exists(Path.Combine(dir.FullName, "asset")) && dir.EnumerateFiles("*.sln").Any())
                return Path.Combine(dir.FullName, "asset", "buildingsprite");
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CdsHelper", "asset", "buildingsprite");
    }
}
