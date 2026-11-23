using System.IO;
using CdsHelper.Support.Local.Helpers;

namespace CdsHelper.Game.Local.Helpers;

/// <summary>
/// 원본 226곳 뒤에 <b>더해 넣은 도시</b>(<c>exe-tables/cities-added.json</c>, 표 이름 「도시-더한것」) — 나소(226) 따위.
/// </summary>
/// <remarks>
/// 원본 도시 표는 EXE 에서 구운 스냅숏이라(다시 구우면 손댄 것이 날아간다) 거기에 줄을 더하지 않고, 이 표를 읽어
/// 열 때마다 뒤에 붙인다. 한 줄은 <see cref="AddedCity.CopyFrom"/> 도시를 본뜨고 적은 칸만 고친다 —
/// 도시 표(<see cref="CityExeTable"/>) · 교역소 표(<see cref="TradeTable"/>) · 건물 표(<see cref="CityBuildingTable"/>) ·
/// 도시 이름 표(<see cref="CityTable"/>) · 지도 좌표(<see cref="GameMapCoords"/>) · 도시 그림(<see cref="CityPictures"/>) · 지도 그림.
/// 번호는 226 부터 빈틈없이 이어져야 한다 — 어긋난 줄부터는 버린다.
/// </remarks>
public static class AddedCities
{
    private const string CacheName = "도시-더한것";

    /// <summary>더한 도시 한 곳.</summary>
    public sealed class AddedCity
    {
        public int Id { get; init; }
        public string Name { get; init; } = "";
        /// <summary>문화권 이름(도시 이름 표의 Culture) — 곡 고르기 따위가 쓴다.</summary>
        public string CultureName { get; init; } = "";
        /// <summary>본뜰 원본 도시.</summary>
        public int CopyFrom { get; init; } = -1;
        /// <summary>도시 블록 왼쪽 위 칸과 차지하는 칸 수.</summary>
        public int CellX { get; init; }
        public int CellY { get; init; }
        public int Reach { get; init; } = 2;
        /// <summary>도시 가운데 원본 좌표(경도 0~40000 · 위도 0~20000) — 칸 x 16.</summary>
        public int LonRaw { get; init; }
        public int LatRaw { get; init; }
        public int? Latitude { get; init; }
        public int? Longitude { get; init; }
        /// <summary>도시 블록 3x3 의 바탕 타일 — 그 칸의 지형 타일(0xFFFF 는 블록 밖).</summary>
        public int[] Erase { get; init; } = [];
        public int? Flags { get; init; }
        public int? Scale { get; init; }
        public int? HullMask { get; init; }
        public int? Nation { get; init; }
        public int? Culture { get; init; }
        public int? Region { get; init; }
        /// <summary>둘 건물 코드 — 본뜬 도시의 그 코드 줄(자리 · 이름)을 가져온다. 없으면 본뜬 도시 그대로.</summary>
        public int[]? Buildings { get; init; }
        /// <summary>건물 코드 → 바꿀 이름.</summary>
        public Dictionary<string, string>? BuildingNames { get; init; }
        /// <summary>딸린 내륙 도시. 없으면 비운다(본뜬 도시의 내륙 도시를 물려받지 않는다).</summary>
        public int[]? Inland { get; init; }
        /// <summary>지도 그림을 빌려 올 도시.</summary>
        public int ArtFrom { get; init; } = -1;
        /// <summary>도시 그림(CITYCG)을 빌려 올 도시.</summary>
        public int PictureFrom { get; init; } = -1;
        /// <summary>제 도시 그림(400x320 PNG, 앱 폴더 기준 자리). 있으면 <see cref="PictureFrom"/> 대신 쓴다. 옆에 <c>@4x</c> 그림이 있으면 업스케일 그림으로 넣어 둔다.</summary>
        public string Picture { get; init; } = "";
        /// <summary>건물 코드 → 상자 왼쪽 위(1배 점). 적은 건물은 본뜬 도시 자리 대신 여기에 선다.</summary>
        public Dictionary<string, int[]>? BuildingSpots { get; init; }
        public string Note { get; init; } = "";
    }

    internal sealed record Snapshot(List<AddedCity> Cities);

    private static List<AddedCity>? _rows;

    /// <summary>더한 도시들 — 226 부터 차례로.</summary>
    public static IReadOnlyList<AddedCity> Rows => _rows ??= Load();

    /// <summary>더한 도시 수.</summary>
    public static int Count => Rows.Count;

    private static List<AddedCity> Load()
    {
        var got = new List<AddedCity>();
        try
        {
            var cached = TableCache.Read<Snapshot>(CacheName);
            foreach (var row in cached?.Data.Cities ?? [])
            {
                if (row.Id != CityExeTable.ExeCount + got.Count) break;
                if (row.CopyFrom < 0 || row.CopyFrom >= CityExeTable.ExeCount) break;
                got.Add(row);
            }
        }
        catch (Exception ex) when (ex is System.Text.Json.JsonException or IOException or InvalidOperationException)
        {
            System.Diagnostics.Debug.WriteLine($"[AddedCities] {ex.Message}");
        }
        GameMapCoords.SetExtraCities([.. got.Select(r => (r.LonRaw, r.LatRaw))]);
        return got;
    }

    /// <summary>지도 좌표 표에 더한 도시를 올린다 — 도시 표를 열기 전에 좌표부터 쓰는 자리를 위해 앱이 켜질 때 부른다.</summary>
    public static void Install() => _ = Rows;

    /// <summary>그 번호가 더한 도시면 그 줄. 아니면 null.</summary>
    public static AddedCity? Find(int city)
    {
        int i = city - CityExeTable.ExeCount;
        return i >= 0 && i < Rows.Count ? Rows[i] : null;
    }

    /// <summary>도시 그림을 꺼낼 번호 — 더한 도시면 빌려 올 도시, 아니면 그대로.</summary>
    public static int PictureOf(int city) =>
        Find(city) is { } row ? (row.PictureFrom >= 0 ? row.PictureFrom : row.CopyFrom) : city;

    private static readonly Dictionary<string, uint[]?> _png = [];

    /// <summary>
    /// 앱 폴더의 PNG 를 BGRA 로 읽는다(한 번 읽으면 들고 있다). 크기가 다르거나 없으면 null.
    /// 옆에 <c>이름@4x.png</c> 가 있으면 업스케일 그림 폴더에 그 그림의 열쇠로 넣어 둔다 — 도시 그림 업스케일링이 켜져 있으면 곧바로 4배 그림이 뜬다.
    /// </summary>
    public static uint[]? LoadPng(string relative, int width, int height)
    {
        if (string.IsNullOrEmpty(relative)) return null;
        if (_png.TryGetValue(relative, out var got)) return got;
        uint[]? px = null;
        try
        {
            string path = Path.Combine(AppContext.BaseDirectory, relative);
            if (File.Exists(path))
            {
                using var fs = File.OpenRead(path);
                var dec = new System.Windows.Media.Imaging.PngBitmapDecoder(fs,
                    System.Windows.Media.Imaging.BitmapCreateOptions.PreservePixelFormat,
                    System.Windows.Media.Imaging.BitmapCacheOption.OnLoad);
                var src = new System.Windows.Media.Imaging.FormatConvertedBitmap(dec.Frames[0],
                    System.Windows.Media.PixelFormats.Bgra32, null, 0);
                if (src.PixelWidth == width && src.PixelHeight == height)
                {
                    px = new uint[width * height];
                    src.CopyPixels(px, width * 4, 0);
                    string hd = Path.ChangeExtension(path, null) + "@4x.png";
                    string seeded = Path.Combine(CityUpscale.Folder, CityUpscale.KeyOf(px, width, height) + ".png");
                    if (File.Exists(hd) && !File.Exists(seeded))
                    {
                        Directory.CreateDirectory(CityUpscale.Folder);
                        File.Copy(hd, seeded);
                    }
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException)
        {
            px = null;
        }
        return _png[relative] = px;
    }

    /// <summary>더한 도시의 제 도시 그림. 없으면 null — 그때는 빌려 올 도시 그림을 쓴다.</summary>
    public static uint[]? OwnPicture(int city) =>
        Find(city) is { Picture.Length: > 0 } row ? LoadPng(row.Picture, CityPictures.Width, CityPictures.Height) : null;

    /// <summary>
    /// 더한 도시의 그 건물 그림(96x80, 비침은 알파) — 도시 그림 옆 <c>city-NNN-bCC.png</c>. 자택처럼 그림 위에 따로 올린다(CityPicView). 없으면 null.
    /// </summary>
    public static uint[]? BuildingArt(int city, int code) =>
        Find(city) is { Picture.Length: > 0 } row
            ? LoadPng(Path.Combine(Path.GetDirectoryName(row.Picture) ?? "", $"city-{city:D3}-b{code:D2}.png"),
                      BuildingSprites.BoxW, BuildingSprites.BoxH)
            : null;

    /// <summary>지도 그림을 빌려 올 도시. 더한 도시가 아니면 -1.</summary>
    public static int ArtOf(int city) =>
        Find(city) is { } row ? (row.ArtFrom >= 0 ? row.ArtFrom : row.CopyFrom) : -1;

    private static T[] Grow<T>(T[] source, Func<AddedCity, T, T> make)
    {
        if (Rows.Count == 0 || source.Length != CityExeTable.ExeCount) return source;
        var grown = new T[source.Length + Rows.Count];
        Array.Copy(source, grown, source.Length);
        for (int i = 0; i < Rows.Count; i++)
            grown[source.Length + i] = make(Rows[i], source[Rows[i].CopyFrom]);
        return grown;
    }

    /// <summary>도시 표(EXE 스냅숏) 뒤에 더한 도시 줄을 붙인다.</summary>
    internal static CityExeTable.Snapshot Extend(CityExeTable.Snapshot s)
    {
        int BuildingWord(AddedCity r, int copied) =>
            r.Buildings is { } codes ? codes.Aggregate(0, (w, c) => c is >= 0 and < 16 ? w | 1 << c : w) : copied;
        return s with
        {
            Stock = Grow(s.Stock, (_, v) => (int[])v.Clone()),
            Cultures = Grow(s.Cultures, (r, v) => r.Culture ?? v),
            Scales = Grow(s.Scales, (r, v) => r.Scale ?? v),
            Nations = Grow(s.Nations, (r, v) => r.Nation ?? v),
            Specials = Grow(s.Specials, (_, v) => (int[])v.Clone()),
            CellX = Grow(s.CellX, (r, _) => r.CellX),
            CellY = Grow(s.CellY, (r, _) => r.CellY),
            Reach = Grow(s.Reach, (r, _) => r.Reach),
            Regions = Grow(s.Regions, (r, v) => r.Region ?? v),
            Erase = Grow(s.Erase, (r, v) => r.Erase.Length == CityExeTable.EraseCells
                                                ? [.. r.Erase.Select(e => (ushort)e)] : (ushort[])v.Clone()),
            Flags = Grow(s.Flags, (r, v) => r.Flags ?? v),
            Buildings = s.Buildings is { } b ? Grow(b, BuildingWord) : null,
            HullMasks = s.HullMasks is { } h ? Grow(h, (r, v) => r.HullMask ?? v) : null,
            Discoveries = s.Discoveries is { } d ? Grow(d, (_, _) => []) : null,
        };
    }

    /// <summary>교역소 표 뒤에 더한 도시 줄을 붙인다 — 지역 · 특산품 · 시세는 본뜬 도시 것, 내륙 도시는 적은 것만.</summary>
    internal static TradeTable.Snapshot Extend(TradeTable.Snapshot s) => s with
    {
        Region = Grow(s.Region, (_, v) => v),
        Inland = Grow(s.Inland, (r, _) => r.Inland ?? []),
        Special = Grow(s.Special, (_, v) => v),
        SpecialPrice = Grow(s.SpecialPrice, (_, v) => v),
        SpecialGrade = Grow(s.SpecialGrade, (_, v) => v),
        Rate = Grow(s.Rate, (_, v) => v),
    };

    /// <summary>건물 표에 더한 도시의 건물 줄 — 본뜬 도시의 그 코드 줄을 번호만 바꿔 가져온다.</summary>
    internal static IEnumerable<CityBuildingTable.Building> BuildingsOf(IReadOnlyList<CityBuildingTable.Building> original)
    {
        foreach (var row in Rows)
            foreach (var b in original.Where(b => b.City == row.CopyFrom))
            {
                if (row.Buildings is { } codes && !codes.Contains(b.Code)) continue;
                string name = row.BuildingNames?.TryGetValue(b.Code.ToString(), out var renamed) == true ? renamed : b.Name;
                var at = row.BuildingSpots?.TryGetValue(b.Code.ToString(), out var spot) == true && spot.Length == 2
                    ? (spot[0], spot[1]) : (b.X, b.Y);
                // 본뜬 도시의 발견물 · 그림 · 해설은 그 도시 몫이라 안 가져온다.
                yield return b with { City = row.Id, Name = name, X = at.Item1, Y = at.Item2, Discovery = -1, Picture = -1, Comment = "" };
            }
    }

    /// <summary>도시 이름 표에 더할 줄 — 이미 있는 번호는 건너뛴다.</summary>
    internal static IEnumerable<CityTable.Entry> EntriesMissingFrom(IReadOnlyCollection<int> ids) =>
        Rows.Where(r => !ids.Contains(r.Id))
            .Select(r => new CityTable.Entry(r.Id, r.Name, r.CultureName, r.Latitude, r.Longitude, false, false));
}
