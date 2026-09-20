using CdsHelper.Support.Local.Helpers;

namespace CdsHelper.Game.Local.Helpers;

/// <summary>
/// 여급의 <b>궁합 코드</b>를 손으로 고쳐 둔 것.
/// </summary>
/// <remarks>
/// 코드는 0~31 한 덩어리고 아래 열여섯이 젊은 제독, 위 열여섯이 그 중년 몫이다
/// (<see cref="FortuneCodes"/>). 자리마다 여급이 고르게 있지 않아 — 열둘·열셋·열다섯은
/// 둘뿐이고 셋·여섯은 아홉이다 — 쏠린 데를 손볼 때 쓴다.
///
/// 고친 것은 <c>%APPDATA%\CdsHelper\exe-tables\여급-고친것.json</c> 에 남고, 적어 둔
/// 여급표(<c>여급표.json</c>)는 EXE 를 읽어 구운 본이라 손대지 않는다.
/// </remarks>
public static class BarmaidEdits
{
    /// <summary>적어 둘 파일 이름.</summary>
    private const string CacheName = "여급-고친것";

    /// <summary>고쳐 둔 한 줄.</summary>
    public readonly record struct Entry(int Id, int Fortune);

    /// <summary>JSON 으로 적어 두는 알맹이.</summary>
    internal sealed record Snapshot(List<Entry> Barmaids);

    private static Dictionary<int, int>? _map;

    /// <summary>코드가 바뀌었을 때 알린다.</summary>
    public static event Action? Changed;

    /// <summary>고쳐 둔 것 전부.</summary>
    public static IReadOnlyDictionary<int, int> All => Map;

    /// <summary>그 여급에 씌워 둔 코드. 안 고쳤으면 −1.</summary>
    public static int Of(int id) => Map.TryGetValue(id, out int code) ? code : -1;

    /// <summary>그 여급의 코드를 갈아 씌운다. −1 이면 씌운 것을 걷는다.</summary>
    public static void Set(int id, int fortune)
    {
        if (id < 0) return;
        if (fortune < 0) Map.Remove(id);
        else Map[id] = Math.Clamp(fortune, 0, FortuneCodes.Slots * 2 - 1);
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
        foreach (var row in saved?.Data.Barmaids ?? [])
            if (row.Id >= 0 && row.Fortune >= 0) map[row.Id] = row.Fortune;
        return map;
    }

    private static void Save()
    {
        var rows = Map.OrderBy(p => p.Key).Select(p => new Entry(p.Key, p.Value)).ToList();
        TableCache.Write(CacheName, new TableCache.Cached<Snapshot>(
            $"{rows.Count}명", new Snapshot(rows), "사람이 고친 것"));
        Changed?.Invoke();
    }
}
