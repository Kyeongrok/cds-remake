namespace CdsHelper.Game.Local.Helpers;

/// <summary>
/// 선수상 등급을 손으로 고쳐 둔 것. 게임 표(<see cref="Engine.Sea.Figureheads"/>) 위에 덧씌운다.
/// </summary>
/// <remarks>
/// 원본 표는 <c>0x0054A0A0</c>(이름 ptr, 등급) 여덟 바이트 x 서른여섯이고 등급은 놀이 안에서
/// 안 바뀐다. 여기서 고친 등급은 <b>막을 확률(등급 x 30 − 20)과 다는 삯</b>에 그대로 들어가
/// 놀이에서 바로 듣는다.
///
/// <b>EXE 도 구워 둔 표도 건드리지 않는다</b> — 고친 것만 따로
/// <c>%APPDATA%\CdsHelper\exe-tables</c> 에 적어 두고 읽을 때 얹는다
/// (<see cref="NationEdits"/> 와 같은 결이다).
/// </remarks>
public static class FigureheadEdits
{
    /// <summary>적어 둘 파일 이름(<c>선수상-고친것.json</c>).</summary>
    private const string CacheName = "선수상-고친것";

    /// <summary>고쳐 둔 한 줄 — 선수상 번호(0~35)와 등급.</summary>
    public readonly record struct Entry(int Index, int Grade);

    /// <summary>JSON 으로 적어 두는 알맹이.</summary>
    internal sealed record Snapshot(List<Entry> Figureheads);

    private static Dictionary<int, int>? _map;

    /// <summary>등급이 고쳐졌을 때 알린다.</summary>
    public static event Action? Changed;

    /// <summary>고쳐 둔 것 전부 — 선수상 번호 → 등급.</summary>
    public static IReadOnlyDictionary<int, int> All => Map;

    /// <summary>그 선수상에 씌워 둔 등급. 안 고쳤으면 null.</summary>
    public static int? Of(int index) => Map.TryGetValue(index, out int g) ? g : null;

    /// <summary>등급을 씌운다. 0~3 밖은 안 받는다.</summary>
    public static void Set(int index, int grade)
    {
        if (!Engine.Sea.Figureheads.Known(index) || grade < 0 || grade > MaxGrade) return;
        Map[index] = grade;
        Save();
    }

    /// <summary>등급이 들 수 있는 끝 — 삯 표(<c>0x0056E280</c>)가 넷이다.</summary>
    public const int MaxGrade = 3;

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
        foreach (var row in saved?.Data.Figureheads ?? [])
            if (Engine.Sea.Figureheads.Known(row.Index) && row.Grade >= 0 && row.Grade <= MaxGrade)
                map[row.Index] = row.Grade;
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
