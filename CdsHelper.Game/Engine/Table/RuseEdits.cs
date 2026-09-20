namespace CdsHelper.Game.Local.Helpers;

/// <summary>
/// 묘책(기습명령)이 먹힐 확률을 손으로 고쳐 둔 것. 게임 표 위에 덧씌운다.
/// </summary>
/// <remarks>
/// 원본 표는 <c>0x00549B80</c> 의 네 바이트 x 서른셋이고, 성사 굴림
/// <c>0x00449080</c> 이 <c>표[문화권 * 3 + 묘책] &gt;= rand(100)</c> 으로 가른다.
/// <b>칸 간격이 셋인데 묘책은 넷</b>이라 이웃 문화권끼리 칸을 나눠 쓴다 — 원본 데이터
/// 그대로의 흠이라 그대로 두고 어느 짝이 어느 칸을 보는지만 창에 적어 준다.
///
/// <b>EXE 도 구워 둔 표도 건드리지 않는다</b> — 고친 것만 따로
/// <c>%APPDATA%\CdsHelper\exe-tables</c> 에 적어 두고 읽을 때 얹는다
/// (<see cref="FigureheadEdits"/> 와 같은 결이다).
/// </remarks>
public static class RuseEdits
{
    /// <summary>적어 둘 파일 이름(<c>묘책확률-고친것.json</c>).</summary>
    private const string CacheName = "묘책확률-고친것";

    /// <summary>고쳐 둔 한 줄 — 표의 칸 번호(0~32)와 확률.</summary>
    public readonly record struct Entry(int Index, int Odds);

    /// <summary>JSON 으로 적어 두는 알맹이.</summary>
    internal sealed record Snapshot(List<Entry> Ruses);

    private static Dictionary<int, int>? _map;

    /// <summary>확률이 고쳐졌을 때 알린다.</summary>
    public static event Action? Changed;

    /// <summary>고쳐 둔 것 전부 — 칸 번호 → 확률.</summary>
    public static IReadOnlyDictionary<int, int> All => Map;

    /// <summary>그 칸에 씌워 둔 확률. 안 고쳤으면 null.</summary>
    public static int? Of(int index) => Map.TryGetValue(index, out int v) ? v : null;

    /// <summary>씌운다. 0~100 밖은 안 받는다.</summary>
    public static void Set(int index, int odds)
    {
        if (!Known(index) || odds < 0 || odds > 100) return;
        Map[index] = odds;
        Save();
    }

    /// <summary>표에 있는 칸인지.</summary>
    public static bool Known(int index) => index >= 0 && index < Engine.Land.LandBattle.RuseOddsCount;

    /// <summary>씌운 것을 걷어 게임 값으로 되돌린다.</summary>
    public static void Reset(int index)
    {
        if (!Map.Remove(index)) return;
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
        foreach (var row in saved?.Data.Ruses ?? [])
            if (Known(row.Index) && row.Odds >= 0 && row.Odds <= 100)
                map[row.Index] = row.Odds;
        return map;
    }

    private static void Save()
    {
        var rows = Map.OrderBy(p => p.Key).Select(p => new Entry(p.Key, p.Value)).ToList();
        TableCache.Write(CacheName, new TableCache.Cached<Snapshot>(
            $"{rows.Count}개", new Snapshot(rows), "사람이 고친 것"));
        Changed?.Invoke();
    }
}
