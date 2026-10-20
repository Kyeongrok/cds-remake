using CdsHelper.Support.Local.Helpers;

namespace CdsHelper.Game.Local.Helpers;

/// <summary>
/// 여급의 <b>궁합 코드</b>와 <b>얼굴</b>을 손으로 고쳐 둔 것.
/// </summary>
/// <remarks>
/// 코드는 0~31 한 덩어리고 아래 열여섯이 젊은 제독, 위 열여섯이 그 중년 몫이다
/// (<see cref="FortuneCodes"/>). 자리마다 여급이 고르게 있지 않아 — 열둘·열셋·열다섯은
/// 둘뿐이고 셋·여섯은 아홉이다 — 쏠린 데를 손볼 때 쓴다.
/// 얼굴은 여급 수첩에서 바꾼다 — 여자 초상화 번호이고, 궁합 코드와는 따로 논다.
///
/// 고친 것은 <c>%APPDATA%\CdsHelper\exe-tables\여급-고친것.json</c> 에 남고, 적어 둔
/// 여급표(<c>여급표.json</c>)는 EXE 를 읽어 구운 본이라 손대지 않는다.
/// </remarks>
public static class BarmaidEdits
{
    /// <summary>적어 둘 파일 이름.</summary>
    private const string CacheName = "여급-고친것";

    /// <summary>고쳐 둔 한 줄. 안 고친 칸은 −1 이다(얼굴 칸이 없던 파일도 −1 로 읽는다).</summary>
    public readonly record struct Entry(int Id, int Fortune, int Face = -1);

    /// <summary>JSON 으로 적어 두는 알맹이.</summary>
    internal sealed record Snapshot(List<Entry> Barmaids);

    private static Dictionary<int, Entry>? _map;

    /// <summary>코드나 얼굴이 바뀌었을 때 알린다.</summary>
    public static event Action? Changed;

    /// <summary>고쳐 둔 것 전부.</summary>
    public static IReadOnlyDictionary<int, Entry> All => Map;

    /// <summary>그 여급에 씌워 둔 코드. 안 고쳤으면 −1.</summary>
    public static int Of(int id) => Map.TryGetValue(id, out var row) ? row.Fortune : -1;

    /// <summary>그 여급에 씌워 둔 얼굴(여자 초상화 번호). 안 고쳤으면 −1.</summary>
    public static int FaceOf(int id) => Map.TryGetValue(id, out var row) ? row.Face : -1;

    /// <summary>그 여급의 코드를 갈아 씌운다. −1 이면 씌운 것을 걷는다.</summary>
    public static void Set(int id, int fortune) =>
        Put(id, row => row with { Fortune = fortune < 0 ? -1 : Math.Clamp(fortune, 0, FortuneCodes.Slots * 2 - 1) });

    /// <summary>그 여급의 얼굴을 갈아 씌운다. −1 이면 씌운 것을 걷는다.</summary>
    public static void SetFace(int id, int face) => Put(id, row => row with { Face = Math.Max(-1, face) });

    /// <summary>한 줄을 고친다 — 코드도 얼굴도 안 씌운 줄이면 지운다.</summary>
    private static void Put(int id, Func<Entry, Entry> change)
    {
        if (id < 0) return;
        var row = change(Map.TryGetValue(id, out var had) ? had : new Entry(id, -1));
        if (row.Fortune < 0 && row.Face < 0) Map.Remove(id);
        else Map[id] = row;
        Save();
    }

    /// <summary>씌운 것을 몽땅 걷는다.</summary>
    public static void ResetAll()
    {
        if (Map.Count == 0) return;
        Map.Clear();
        Save();
    }

    private static Dictionary<int, Entry> Map => _map ??= Load();

    private static Dictionary<int, Entry> Load()
    {
        var saved = TableCache.Read<Snapshot>(CacheName);
        var map = new Dictionary<int, Entry>();
        foreach (var row in saved?.Data.Barmaids ?? [])
            if (row.Id >= 0 && (row.Fortune >= 0 || row.Face >= 0)) map[row.Id] = row;
        return map;
    }

    private static void Save()
    {
        var rows = Map.OrderBy(p => p.Key).Select(p => p.Value).ToList();
        TableCache.Write(CacheName, new TableCache.Cached<Snapshot>(
            $"{rows.Count}명", new Snapshot(rows), "사람이 고친 것"));
        Changed?.Invoke();
    }
}
