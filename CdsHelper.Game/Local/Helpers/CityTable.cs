using System.IO;
using System.Text.Json.Serialization;
using CdsHelper.Support.Local.Helpers;
using CdsHelper.Support.Local.Models;
using Prism.Ioc;

namespace CdsHelper.Game.Local.Helpers;

/// <summary>
/// 도시 표 — 번호·이름·문화권·위경도. <see cref="CityBuildingTable.Building.City"/> 가
/// 가리키는 쪽이 이것이다.
/// </summary>
/// <remarks>
/// 건물 표와 도시 표는 1:n 이다. 건물 줄은 도시 번호 하나만 들고 있고(외래키), 이름과
/// 문화권은 여기 한 번씩만 적힌다.
/// <code>
///   도시표.json   Id 0   "리스본"  서유럽 …          1
///   건물표.json   City 0 "항구"·"교역소"·"왕궁" …    n
/// </code>
/// 원본은 게임 EXE 가 아니라 <b>앱 DB</b>(cdshelper.db)다 — 이름도 문화권도 게임에서 뽑은
/// 것이 아니라 앱이 들고 있는 값이고, 도시 창에서 고칠 수도 있다.
///
/// 그래서 <see cref="ExeTable"/> 의 규칙(도장이 같으면 적어 둔 것을 쓴다)을 쓰면 안 된다.
/// 고친 값이 게임에 안 비친다. 여기서는 반대로 <b>원본을 먼저</b> 본다.
/// <code>
///   원본을 읽었다        원본을 쓴다. 그 김에 적어 둔다(달라졌을 때만).
///   원본을 못 읽었다     적어 둔 것을 쓴다 (DB 가 없는 자리)
///   둘 다 없다           빈 표 — 이름은 "도시 3" 처럼 번호로 물러선다
/// </code>
/// 적어 두는 것은 두 몫이다. 게임데이터 창에서 눈으로 볼 수 있고, DB 를 못 읽는 자리에서도
/// 도시 이름이 나온다.
/// </remarks>
public sealed class CityTable
{
    /// <summary>적어 둘 파일 이름(<c>%APPDATA%\CdsHelper\exe-tables\도시표.json</c>).</summary>
    private const string CacheName = "도시표";

    /// <summary>도시 한 곳. 건물 표가 <see cref="Id"/> 로 이것을 가리킨다.</summary>
    /// <param name="Id">도시 번호(0~224).</param>
    /// <param name="Culture">문화권("서유럽", "이슬람" …). 모르면 빈 문자열.</param>
    [method: JsonConstructor]
    public readonly record struct Entry(
        int Id, string Name, string Culture,
        int? Latitude, int? Longitude, bool HasLibrary, bool HasGuild);

    /// <summary>JSON 으로 적어 두는 알맹이.</summary>
    internal sealed record Snapshot(List<Entry> Cities);

    private readonly Dictionary<int, Entry> _byId = [];

    private CityTable(Snapshot snapshot)
    {
        Cities = snapshot.Cities;
        foreach (var c in snapshot.Cities) _byId[c.Id] = c;
    }

    /// <summary>표에 있는 도시 전부.</summary>
    public IReadOnlyList<Entry> Cities { get; }

    /// <summary>어디서 읽었는지. 게임데이터 창이 아니라 디버그용이다.</summary>
    public static string LastError { get; private set; } = "";

    /// <summary>그 도시. 없으면 null.</summary>
    public Entry? Find(int id) => _byId.TryGetValue(id, out var c) ? c : null;

    /// <summary>도시 이름. 모르면 번호로 물러선다.</summary>
    public string NameOf(int id) =>
        _byId.TryGetValue(id, out var c) && c.Name.Length > 0 ? c.Name : $"도시 {id}";

    /// <summary>문화권. 모르면 빈 문자열 — 부르는 쪽이 기본 곡으로 물러선다.</summary>
    public string CultureOf(int id) =>
        _byId.TryGetValue(id, out var c) ? c.Culture : "";

    /// <summary>
    /// 표를 연다. 원본(앱 DB)을 먼저 보고, 못 읽으면 적어 둔 것을 읽는다.
    /// 둘 다 없으면 빈 표를 낸다 — 도시 이름이 번호로 나올 뿐 게임은 그대로 돈다.
    /// </summary>
    public static CityTable Open()
    {
        LastError = "";

        var fresh = FromApp();
        if (fresh != null)
        {
            // 달라졌을 때만 적는다 — 켤 때마다 같은 내용을 다시 적을 까닭이 없다.
            var cached = TableCache.Read<Snapshot>(CacheName);
            if (cached == null || !cached.Data.Cities.SequenceEqual(fresh.Cities))
                TableCache.Write(CacheName, new TableCache.Cached<Snapshot>(
                    $"{fresh.Cities.Count}곳", fresh, "cdshelper.db"));
            return new CityTable(fresh);
        }

        var saved = TableCache.Read<Snapshot>(CacheName);
        if (saved != null) return new CityTable(saved.Data);

        LastError = LastError.Length > 0 ? LastError : "도시 표를 읽지 못했고 적어 둔 것도 없습니다";
        return new CityTable(new Snapshot([]));
    }

    /// <summary>
    /// 앱이 들고 있는 도시 목록. DB 를 새로 두드리지 않고 이미 읽어 둔 것을 쓴다 —
    /// 앱이 켜질 때 <see cref="CityService.InitializeAsync"/> 가 채워 놓는다.
    /// 그것이 비어 있으면(앱 없이 이 창만 띄운 자리) 같이 깔린 cities.json 으로 물러선다.
    /// </summary>
    private static Snapshot? FromApp()
    {
        try
        {
            var service = ContainerLocator.Container?.Resolve<CityService>();
            var cities = service?.GetCachedCities();
            if (cities is { Count: > 0 }) return ToSnapshot(cities);
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
        }

        try
        {
            string path = Path.Combine(AppContext.BaseDirectory, "cities.json");
            if (File.Exists(path))
            {
                var cities = new CityService().LoadCities(path);
                if (cities.Count > 0) return ToSnapshot(cities);
            }
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
        }

        return null;
    }

    private static Snapshot ToSnapshot(IEnumerable<City> cities) =>
        new([.. cities.OrderBy(c => c.Id).Select(c => new Entry(
            c.Id, c.Name, c.CulturalSphere ?? "",
            c.Latitude, c.Longitude, c.HasLibrary, c.HasGuild))]);
}
