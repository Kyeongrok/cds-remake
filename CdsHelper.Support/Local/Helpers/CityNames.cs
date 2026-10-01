using System.IO;
using System.Text.Json;

namespace CdsHelper.Support.Local.Helpers;

/// <summary>
/// 도시 이름을 <b>번호로</b> 찾는다 — 코드에 「세비야」 같은 이름을 박지 않고 표 한 곳(cities.json)만 고치면 따라 바뀌게.
/// </summary>
/// <remarks>
/// 앱이 읽어 둔 도시 목록(<see cref="CityService"/>)을 먼저 보고, 없으면(놀이 앱처럼 DB 를 안 여는 자리)
/// 실행 파일 옆 <c>cities.json</c> 을 한 번 읽어 둔다.
/// </remarks>
public static class CityNames
{
    /// <summary>자주 짚는 도시 번호 — 리스본 · 세비야(포르투갈 · 에스파니아 자택).</summary>
    public const int LisbonId = 0, SevilleId = 7;

    private static Dictionary<int, string>? _fromJson;

    /// <summary>그 번호의 도시 이름. 모르면 「도시 N」.</summary>
    public static string Of(int id)
    {
        try
        {
            var service = Prism.Ioc.ContainerLocator.Container?.Resolve(typeof(CityService)) as CityService;
            var hit = service?.GetCachedCities().FirstOrDefault(c => c.Id == id);
            if (hit is { Name.Length: > 0 }) return hit.Name;
        }
        catch (Exception) { /* 컨테이너가 없는 자리 — JSON 으로 물러선다 */ }

        return FromJson().TryGetValue(id, out var name) && name.Length > 0 ? name : $"도시 {id}";
    }

    /// <summary>리스본 — XAML 에서 <c>{x:Static}</c> 으로 짚는다.</summary>
    public static string Lisbon => Of(LisbonId);

    /// <summary>세비야 — XAML 에서 <c>{x:Static}</c> 으로 짚는다.</summary>
    public static string Seville => Of(SevilleId);

    private static Dictionary<int, string> FromJson()
    {
        if (_fromJson != null) return _fromJson;
        var map = new Dictionary<int, string>();
        try
        {
            string path = Path.Combine(AppContext.BaseDirectory, "cities.json");
            if (File.Exists(path))
                foreach (var c in new CityService().LoadCities(path))
                    map[c.Id] = c.Name;
        }
        catch (Exception ex) when (ex is IOException or JsonException or InvalidOperationException or FileNotFoundException) { }
        return _fromJson = map;
    }
}
