using CdsHelper.Game.Engine.Land;

namespace CdsHelper.Game.Local.Helpers;

/// <summary>
/// 적 부대 세트(<see cref="LandFormations"/>)를 손으로 고쳐 둔 것.
/// </summary>
/// <remarks>
/// EXE 는 손대지 않는다. 고친 것만 <c>%APPDATA%\CdsHelper\exe-tables\부대편성-고친것.json</c>
/// 에 남겨 두고 <see cref="LandFormations.Of"/> 가 읽을 때 얹는다 —
/// <see cref="NationEdits"/> · <see cref="CityCultureEdits"/> 와 같은 결이다.
/// </remarks>
public static class LandFormationEdits
{
    /// <summary>적어 둘 파일 이름.</summary>
    private const string CacheName = "부대편성-고친것";

    /// <summary>고쳐 둔 자리 하나. <see cref="LandFormations.Slot"/> 을 그대로 적는다.</summary>
    public readonly record struct Line(int Big, int Small, int Skill);

    /// <summary>고쳐 둔 진형 한 벌.</summary>
    public readonly record struct Entry(int Shape, string? Name, List<Line> Units);

    /// <summary>JSON 으로 적어 두는 알맹이.</summary>
    internal sealed record Snapshot(List<Entry> Shapes);

    private static Dictionary<int, Entry>? _map;

    /// <summary>편성이 고쳐졌을 때 알린다.</summary>
    public static event Action? Changed;

    /// <summary>고쳐 둔 것 전부.</summary>
    public static IReadOnlyDictionary<int, Entry> All => Map;

    /// <summary>그 진형을 고쳤는지.</summary>
    public static bool Edited(int shape) => Map.ContainsKey(shape);

    /// <summary>게임 값 위에 고친 것을 얹어 낸다.</summary>
    public static LandFormations.Shape Apply(int shape, LandFormations.Shape stock)
    {
        if (!Map.TryGetValue(shape, out var e)) return stock;

        var units = e.Units is { Count: > 0 }
            ? e.Units.Select(u => new LandFormations.Slot(u.Big, u.Small, u.Skill)).ToArray()
            : stock.Units;
        return stock with { Name = e.Name ?? stock.Name, Units = units };
    }

    /// <summary>그 진형을 갈아 씌운다.</summary>
    public static void Set(int shape, string? name, IReadOnlyList<LandFormations.Slot> units)
    {
        var lines = units.Take(LandFormations.MaxUnits)
                         .Select(u => new Line(u.Big, u.Small, u.Skill)).ToList();
        Map[shape] = new Entry(shape, name, lines);
        Save();
    }

    /// <summary>씌운 것을 걷어 게임 값으로 되돌린다.</summary>
    public static void Reset(int shape)
    {
        if (!Map.Remove(shape)) return;
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
        foreach (var row in saved?.Data.Shapes ?? [])
            if (row.Shape >= 0 && row.Shape < LandFormations.Count) map[row.Shape] = row;
        return map;
    }

    private static void Save()
    {
        var rows = Map.OrderBy(p => p.Key).Select(p => p.Value).ToList();
        TableCache.Write(CacheName, new TableCache.Cached<Snapshot>(
            $"{rows.Count}벌", new Snapshot(rows), "사람이 고친 것"));
        Changed?.Invoke();
    }
}
