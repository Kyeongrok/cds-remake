namespace CdsHelper.Game.Local.Helpers;

/// <summary>
/// 나라 표를 손으로 고쳐 둔 것. 게임 표(<see cref="NationTable"/>) 위에 덧씌운다.
/// </summary>
/// <remarks>
/// <b>적어 둔 표(<c>나라표.json</c>)를 직접 고치지 않는다.</b> 그 파일은 EXE 를 읽어
/// 구워 둔 <b>본</b>이라, 게임 판이 바뀌거나 알맹이 모양이 올라가면 통째로 다시 구워진다 —
/// 거기에 손을 대면 그때 없어진다. 그래서 고친 것만 따로 적어 두고
/// <see cref="NationTable"/> 이 읽을 때 얹는다(<see cref="CityCultureEdits"/> 와 같은 결이다).
///
/// EXE 는 손대지 않는다. 고친 것은 <c>%APPDATA%\CdsHelper\exe-tables</c> 에 남아 앱을
/// 껐다 켜도 그대로다.
/// </remarks>
public static class NationEdits
{
    /// <summary>적어 둘 파일 이름(<c>나라-고친것.json</c>).</summary>
    private const string CacheName = "나라-고친것";

    /// <summary>고쳐 둔 한 줄. 안 고친 칸은 null 이라 게임 값이 그대로 남는다.</summary>
    public readonly record struct Entry(int Id, string? Name, int? Language, int? Capital,
                                       int? Access = null);

    /// <summary>JSON 으로 적어 두는 알맹이.</summary>
    internal sealed record Snapshot(List<Entry> Nations);

    private static Dictionary<int, Entry>? _map;

    /// <summary>나라가 고쳐졌을 때 알린다 — 표를 들고 있던 쪽이 다시 읽는다.</summary>
    public static event Action? Changed;

    /// <summary>고쳐 둔 나라 전부.</summary>
    public static IReadOnlyDictionary<int, Entry> All => Map;

    /// <summary>그 나라에 씌워 둔 것. 안 고쳤으면 null.</summary>
    public static Entry? Of(int id) => Map.TryGetValue(id, out var e) ? e : null;

    /// <summary>게임 값 위에 고친 것을 얹어 낸다.</summary>
    public static NationTable.Nation Apply(NationTable.Nation game)
    {
        if (Of(game.Id) is not { } e) return game;
        return game with
        {
            Name = e.Name ?? game.Name,
            Language = e.Language ?? game.Language,
            Capital = e.Capital ?? game.Capital,
            Entry = e.Access ?? game.Entry,
        };
    }

    /// <summary>그 나라를 고쳐 씌운다. 넷 다 null 이면 씌운 것을 걷는다.</summary>
    /// <param name="access">
    /// 출입여부(<see cref="NationTable.Nation.Entry"/>). 0~2 밖은 안 받는다.
    /// </param>
    public static void Set(int id, string? name, int? language, int? capital,
                           int? access = null)
    {
        if (id < 0 || id >= NationTable.Count) return;
        if (access is { } a && (a < 0 || a >= NationTable.EntryCount)) access = null;

        if (name == null && language == null && capital == null && access == null)
        {
            Reset(id);
            return;
        }
        Map[id] = new Entry(id, name, language, capital, access);
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
        foreach (var row in saved?.Data.Nations ?? [])
            if (row.Id >= 0 && row.Id < NationTable.Count) map[row.Id] = row;
        return map;
    }

    private static void Save()
    {
        var rows = Map.OrderBy(p => p.Key).Select(p => p.Value).ToList();
        TableCache.Write(CacheName, new TableCache.Cached<Snapshot>(
            $"{rows.Count}곳", new Snapshot(rows), "사람이 고친 것"));
        Changed?.Invoke();
    }
}
