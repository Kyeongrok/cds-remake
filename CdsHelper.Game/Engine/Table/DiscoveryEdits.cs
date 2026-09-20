namespace CdsHelper.Game.Local.Helpers;

/// <summary>
/// 발견물 표 위에 사람이 손으로 얹어 둔 줄 — 원본에 없는 <b>새 발견물</b>을 더하거나,
/// 있는 줄을 통째로 갈아 끼운다.
/// </summary>
/// <remarks>
/// <see cref="HintEdits"/> 와 결이 같지만 <b>칸별로 덧씌우지 않고 줄째로 갈아 끼운다</b> —
/// 새로 더하는 줄은 어차피 원본에 없어 덧씌울 밑감이 없기 때문이다. 표 밖 번호(274 이상)도
/// 받는다 — 그게 이 표를 두는 까닭이다.
///
/// <b>적어 둔 표(<c>발견물표.json</c>)를 직접 고치지 않는다.</b> 그 파일은 EXE 를 읽어 구워 둔
/// 본이라 <see cref="DiscoveryTable"/> 의 <c>SnapshotVersion</c> 이 오르면 통째로 다시
/// 구워져 손으로 넣은 줄이 사라진다. 여기 적어 둔 것은 <b>따로</b> 있어 그 재굽기를
/// 타지 않는다 — 앱을 새로 깔아도, 판이 올라도 그대로 남는다.
/// </remarks>
public static class DiscoveryEdits
{
    /// <summary>적어 둘 파일 이름(<c>발견물-고친것.json</c>).</summary>
    private const string CacheName = "발견물-고친것";

    /// <summary>JSON 으로 적어 두는 알맹이.</summary>
    internal sealed record Snapshot(List<DiscoveryTable.Record> Discoveries);

    private static Dictionary<int, DiscoveryTable.Record>? _map;

    /// <summary>발견물이 더해지거나 고쳐졌을 때 알린다 — 표를 들고 있던 쪽이 다시 읽는다.</summary>
    public static event Action? Changed;

    /// <summary>더하거나 갈아 끼운 줄 전부. 번호가 곧 <see cref="DiscoveryTable.Record.Id"/> 다.</summary>
    public static IReadOnlyDictionary<int, DiscoveryTable.Record> All => Map;

    /// <summary>그 번호에 얹어 둔 줄. 없으면 null.</summary>
    public static DiscoveryTable.Record? Of(int id) => Map.TryGetValue(id, out var e) ? e : null;

    /// <summary>
    /// 그 번호로 줄을 얹는다 — 원본에 있던 번호면 통째로 갈아 끼우고, 없던 번호면 새로 더한다.
    /// </summary>
    public static void Set(DiscoveryTable.Record row)
    {
        Map[row.Id] = row;
        Save();
    }

    /// <summary>얹어 둔 줄을 걷는다 — 원본에 있던 번호면 원본 값으로 되돌아간다.</summary>
    public static void Reset(int id)
    {
        if (!Map.Remove(id)) return;
        Save();
    }

    /// <summary>얹어 둔 것을 몽땅 걷는다.</summary>
    public static void ResetAll()
    {
        if (Map.Count == 0) return;
        Map.Clear();
        Save();
    }

    private static Dictionary<int, DiscoveryTable.Record> Map => _map ??= Load();

    private static Dictionary<int, DiscoveryTable.Record> Load()
    {
        var saved = TableCache.Read<Snapshot>(CacheName);
        var map = new Dictionary<int, DiscoveryTable.Record>();
        foreach (var row in saved?.Data.Discoveries ?? []) map[row.Id] = row;
        return map;
    }

    private static void Save()
    {
        var rows = Map.OrderBy(p => p.Key).Select(p => p.Value).ToList();
        TableCache.Write(CacheName, new TableCache.Cached<Snapshot>(
            $"{rows.Count}줄", new Snapshot(rows), "사람이 더한 것"));
        Changed?.Invoke();
    }
}
