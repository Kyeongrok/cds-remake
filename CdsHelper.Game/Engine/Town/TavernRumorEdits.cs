using CdsHelper.Game.Local.Helpers;

namespace CdsHelper.Game.Engine.Town;

/// <summary>
/// 술집 · 여관 무명 손님 소문을 손으로 고쳐 둔 것. 원본 벌(<see cref="TavernRumors"/>) 위에 갈래째 덧씌운다.
/// </summary>
/// <remarks>
/// 코드에 박힌 원본 줄은 건드리지 않는다 — 고친 갈래만 <c>tavern-rumors-edits.json</c>(표 이름 「손님소문-고친것」, 켠 커스텀 팩이 있으면 그 팩)에
/// 통째로 적어 두고, 소문을 고를 때 그 갈래는 이것을 쓴다. 도구 앱 「요소 → 소문」 창(<c>TavernRumorEditDialog</c>)이 고친다.
/// 예전에는 「더해 넣은 소문」(<see cref="Table.RumorTable"/>)과 같은 <c>rumors-added.json</c> 에 써서, 여기서 저장하면 더한 소문 줄이 지워졌다 —
/// 이제 제 파일을 쓰고, 옛 파일에 갈래가 있으면 처음 한 번 읽어 온다(<see cref="LegacyName"/>).
/// </remarks>
public static class TavernRumorEdits
{
    /// <summary>적어 둘 파일 이름.</summary>
    private const string CacheName = "손님소문-고친것";

    /// <summary>예전에 적던 자리 — 「더해 넣은 소문」과 같은 파일(<c>rumors-added.json</c>). 읽기만 한다.</summary>
    private const string LegacyName = "소문-고친것";

    /// <summary>고쳐 둔 갈래 하나 — 갈래 번호와 그 줄 전부.</summary>
    public sealed record SetEdit(int Set, List<TavernRumors.Line> Lines);

    /// <summary>JSON 으로 적어 두는 알맹이.</summary>
    internal sealed record Snapshot(List<SetEdit> Sets);

    private static Dictionary<int, List<TavernRumors.Line>>? _map;

    /// <summary>켠 커스텀 팩이 바뀌면 담아 둔 것을 버린다 — 다음에 물을 때 새 팩으로 다시 읽는다.</summary>
    static TavernRumorEdits() => TableCache.Reloaded += () => _map = null;

    private static Dictionary<int, List<TavernRumors.Line>> Map => _map ??= Load();

    /// <summary>그 갈래에 씌운 줄들. 안 고쳤으면 null.</summary>
    public static IReadOnlyList<TavernRumors.Line>? Of(int set) => Map.TryGetValue(set, out var lines) ? lines : null;

    /// <summary>고친 갈래 수.</summary>
    public static int Count => Map.Count;

    /// <summary>그 갈래를 이 줄들로 씌운다. 빈 글 줄은 버린다.</summary>
    public static void Set(int set, IEnumerable<TavernRumors.Line> lines)
    {
        Map[set] = [.. lines.Where(l => !string.IsNullOrWhiteSpace(l.Text)).Select(l => l with { Text = l.Text.Trim() })];
        Save();
    }

    /// <summary>그 갈래를 원본으로 되돌린다.</summary>
    public static void Reset(int set)
    {
        if (Map.Remove(set)) Save();
    }

    /// <summary>다시 읽는다 — 다른 앱이 고쳤을 때.</summary>
    public static void Reload() => _map = null;

    private static Dictionary<int, List<TavernRumors.Line>> Load()
    {
        var map = new Dictionary<int, List<TavernRumors.Line>>();
        // 제 파일이 없으면 옛 자리(더한 소문과 같은 파일)에 적혀 있던 갈래를 읽어 온다 — 다음 저장부터 제 파일에 적힌다.
        var sets = TableCache.Read<Snapshot>(CacheName)?.Data.Sets ?? TableCache.Read<Snapshot>(LegacyName)?.Data.Sets ?? [];
        foreach (var edit in sets)
            if (edit.Set >= 0 && edit.Set < TavernRumors.SetNames.Length && edit.Lines is { Count: > 0 })
                map[edit.Set] = edit.Lines;
        return map;
    }

    private static void Save()
    {
        var rows = Map.OrderBy(p => p.Key).Select(p => new SetEdit(p.Key, p.Value)).ToList();
        TableCache.Write(CacheName, new TableCache.Cached<Snapshot>(
            $"{rows.Count}갈래", new Snapshot(rows), "사람이 고친 것"));
    }
}
