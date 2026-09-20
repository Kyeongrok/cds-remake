namespace CdsHelper.Game.Local.Helpers;

/// <summary>
/// 도시가 딸린 <b>왕국</b>을 손으로 갈아 둔 것. 게임 표(<see cref="CityExeTable"/>) 위에
/// 덧씌운다.
/// </summary>
/// <remarks>
/// 문화권을 씌우는 것과 같은 결이다(<see cref="CityCultureEdits"/>) — <see
/// cref="CityExeTable.NationOf"/> 가 여기를 먼저 보므로, 왕국을 묻는 자리는 어디든
/// 저절로 따라온다. 왕국 이름은 나라 표(<see cref="NationTable"/>)에서 온다.
///
/// EXE 는 손대지 않는다. 갈아 둔 것은 <c>%APPDATA%\CdsHelper\exe-tables</c> 에 적어 두어
/// 앱을 껐다 켜도 남는다.
/// </remarks>
public static class CityNationEdits
{
    /// <summary>적어 둘 파일 이름(<c>도시-왕국-고친것.json</c>).</summary>
    private const string CacheName = "도시-왕국-고친것";

    /// <summary>갈아 둔 한 줄.</summary>
    public readonly record struct Entry(int City, int Nation);

    /// <summary>JSON 으로 적어 두는 알맹이.</summary>
    internal sealed record Snapshot(List<Entry> Cities);

    /// <summary>없음.</summary>
    public const int None = -1;

    private static Dictionary<int, int>? _map;

    /// <summary>왕국이 갈렸을 때 알린다.</summary>
    public static event Action? Changed;

    /// <summary>갈아 둔 도시 전부. 하나도 없으면 빈 목록.</summary>
    public static IReadOnlyDictionary<int, int> All => Map;

    /// <summary>그 도시에 씌워 둔 왕국. 안 갈았으면 <see cref="None"/>.</summary>
    public static int Of(int cityId) => Map.TryGetValue(cityId, out int nation) ? nation : None;

    /// <summary>그 도시의 왕국을 갈아 씌운다.</summary>
    public static void Set(int cityId, int nation)
    {
        if (nation < 0 || nation >= NationTable.Count) return;
        if (Of(cityId) == nation) return;
        Map[cityId] = nation;
        Save();
    }

    /// <summary>씌운 것을 걷어 게임 값으로 되돌린다.</summary>
    public static void Reset(int cityId)
    {
        if (!Map.Remove(cityId)) return;
        Save();
    }

    /// <summary>씌운 것을 몽땅 걷는다.</summary>
    public static void ResetAll()
    {
        if (Map.Count == 0) return;
        Map.Clear();
        Save();
    }

    private static Dictionary<int, int> Map => _map ??= Load();

    private static Dictionary<int, int> Load()
    {
        var saved = TableCache.Read<Snapshot>(CacheName);
        var map = new Dictionary<int, int>();
        foreach (var row in saved?.Data.Cities ?? [])
            if (row.Nation >= 0 && row.Nation < NationTable.Count) map[row.City] = row.Nation;
        return map;
    }

    private static void Save()
    {
        var rows = Map.OrderBy(p => p.Key).Select(p => new Entry(p.Key, p.Value)).ToList();
        TableCache.Write(CacheName, new TableCache.Cached<Snapshot>(
            $"{rows.Count}곳", new Snapshot(rows), "사람이 고친 것"));
        Changed?.Invoke();
    }
}
