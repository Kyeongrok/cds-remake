namespace CdsHelper.Game.Local.Helpers;

/// <summary>
/// 힌트 표를 손으로 고쳐 둔 것. 게임 표(<see cref="HintTable"/>) 위에 덧씌운다.
/// </summary>
/// <remarks>
/// <b>적어 둔 표(<c>힌트표.json</c>)를 직접 고치지 않는다.</b> 그 파일은 EXE 를 읽어 구워 둔
/// <b>본</b>이라 판이 바뀌거나 알맹이 모양이 올라가면 통째로 다시 구워진다. 그래서 고친
/// 것만 따로 적어 두고 표가 읽힐 때 얹는다 — <see cref="NationEdits"/> 와 같은 결이다.
///
/// EXE 는 손대지 않는다. 고친 것은 <c>%APPDATA%\CdsHelper\exe-tables</c> 에 남아 앱을
/// 껐다 켜도 그대로다.
/// </remarks>
public static class HintEdits
{
    /// <summary>적어 둘 파일 이름(<c>힌트-고친것.json</c>).</summary>
    private const string CacheName = "힌트-고친것";

    /// <summary>고쳐 둔 한 줄. 안 고친 칸은 null 이라 게임 값이 그대로 남는다.</summary>
    /// <param name="Discovery">
    /// 가리킬 발견물 <b>일련번호</b>. 표에서 몇째 줄인지가 아니다
    /// (<see cref="HintTable.Hint.Discovery"/>).
    /// </param>
    public readonly record struct Entry(int Id, string? Name, int? Grade, int? Category,
                                        int? Funds, int? Deadline, int? Discovery,
                                        string? Text = null);

    /// <summary>JSON 으로 적어 두는 알맹이.</summary>
    internal sealed record Snapshot(List<Entry> Hints);

    private static Dictionary<int, Entry>? _map;

    /// <summary>힌트가 고쳐졌을 때 알린다 — 표를 들고 있던 쪽이 다시 읽는다.</summary>
    public static event Action? Changed;

    /// <summary>고쳐 둔 힌트 전부.</summary>
    public static IReadOnlyDictionary<int, Entry> All => Map;

    /// <summary>그 힌트에 씌워 둔 것. 안 고쳤으면 null.</summary>
    public static Entry? Of(int id) => Map.TryGetValue(id, out var e) ? e : null;

    /// <summary>게임 값 위에 고친 것을 얹어 낸다.</summary>
    public static HintTable.Hint Apply(HintTable.Hint game)
    {
        if (Of(game.Id) is not { } e) return game;
        return game with
        {
            Name = e.Name ?? game.Name,
            Grade = e.Grade ?? game.Grade,
            Category = e.Category ?? game.Category,
            Funds = e.Funds ?? game.Funds,
            Deadline = e.Deadline ?? game.Deadline,
            Discovery = e.Discovery ?? game.Discovery,
            Text = e.Text ?? game.Text,
        };
    }

    /// <summary>
    /// 밑감(게임 줄)이 없는 번호를 위해, 고친 칸만으로 힌트 하나를 짓는다 — 안 채운 칸은
    /// 무난한 기본값으로 채운다. 원본에 있던 번호는 <see cref="Apply"/> 를 쓴다.
    /// </summary>
    public static HintTable.Hint Synthesize(Entry e) => new(
        e.Id, e.Name ?? $"새 힌트 {e.Id}", e.Grade ?? 1, e.Category ?? 0,
        e.Funds ?? 10000, e.Deadline ?? 3, e.Discovery ?? -1, e.Text ?? "");

    /// <summary>
    /// 그 힌트를 고쳐 씌운다. 죄다 null 이면 씌운 것을 걷는다.
    /// </summary>
    /// <remarks>
    /// 번호를 186 아래로 막지 않는다 — 원본에 있던 186줄을 고치는 것과, 원본에 없던 새
    /// 힌트를 더하는 것이 같은 길이다. 새 번호면 <see cref="Synthesize"/> 가 기본값을 채운다.
    /// </remarks>
    public static void Set(int id, string? name, int? grade, int? category, int? funds,
                           int? deadline, int? discovery, string? text)
    {
        if (id < 0) return;

        if (name == null && grade == null && category == null && funds == null
            && deadline == null && discovery == null && text == null)
        {
            Reset(id);
            return;
        }
        Map[id] = new Entry(id, name, grade, category, funds, deadline, discovery, text);
        Save();
    }

    /// <summary>씌운 것을 걷어 게임 값으로 되돌린다.</summary>
    public static void Reset(int id)
    {
        if (!Map.Remove(id)) return;
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
        foreach (var row in saved?.Data.Hints ?? [])
            if (row.Id >= 0) map[row.Id] = row;
        return map;
    }

    private static void Save()
    {
        var rows = Map.OrderBy(p => p.Key).Select(p => p.Value).ToList();
        TableCache.Write(CacheName, new TableCache.Cached<Snapshot>(
            $"{rows.Count}줄", new Snapshot(rows), "사람이 고친 것"));
        Changed?.Invoke();
    }
}
