using CdsHelper.Game.Local.Helpers;

namespace CdsHelper.Game.Engine.Discovery;

/// <summary>
/// 역사 항해자가 무엇을 언제 채가는지 손으로 고쳐 둔 것 — <see cref="HistoryVoyages"/> 위에
/// 덧씌운다.
/// </summary>
/// <remarks>
/// <b>구워 둔 <c>역사항해자.json</c> 을 직접 고치지 않는다.</b> 그 파일은
/// <c>HISTCHR.CDS</c> 를 읽어 둔 <b>본</b>이라 게임 폴더가 바뀌면 통째로 다시 구워진다 —
/// 거기에 손을 대면 그때 없어진다. 그래서 고친 것만 따로 적어 두고 표가 읽힐 때 얹는다
/// (<see cref="NationEdits"/> 와 같은 결이다).
///
/// <b>줄 단위가 아니라 사람 단위로 갈아 끼운다.</b> 줄을 더하고 지우는 것까지 해야 해서,
/// 한 사람의 목록을 통째로 두는 편이 어긋날 일이 없다. 손대지 않은 사람은 파일 그대로다.
///
/// <c>HISTCHR.CDS</c> 는 손대지 않는다. 고친 것은
/// <c>%APPDATA%\CdsHelper\exe-tables\경쟁자-고친것.json</c> 에 남아 앱을 껐다 켜도 그대로다.
/// </remarks>
public static class VoyagerEdits
{
    /// <summary>적어 둘 파일 이름(<c>경쟁자-고친것.json</c>).</summary>
    private const string CacheName = "경쟁자-고친것";

    /// <summary>한 사람을 통째로 갈아 끼운 것.</summary>
    /// <param name="Voyager">역사 항해자 번호(0~13).</param>
    /// <param name="Finds">그 사람이 채가는 것 전부. 빈 목록이면 아무것도 안 채간다.</param>
    public readonly record struct Entry(int Voyager, List<HistoryVoyages.Voyage> Finds);

    /// <summary>JSON 으로 적어 두는 알맹이.</summary>
    internal sealed record Snapshot(List<Entry> Voyagers);

    private static Dictionary<int, List<HistoryVoyages.Voyage>>? _map;

    /// <summary>
    /// 고칠 때마다 하나씩 는다. 표를 들고 있는 쪽이 이 수를 재어 두었다가 <b>달라졌을 때만</b>
    /// 다시 얹는다(<see cref="HistoryVoyages.All"/>) — 사건을 걸어 두면 창이 닫힌 뒤에도
    /// 붙잡혀 있게 된다.
    /// </summary>
    public static int Revision { get; private set; }

    /// <summary>손댄 사람 수.</summary>
    public static int Count => Map.Count;

    /// <summary>그 사람을 손댔는지.</summary>
    public static bool Touched(int voyager) => Map.ContainsKey(voyager);

    /// <summary>파일에서 읽은 것 위에 고친 것을 얹어 낸다.</summary>
    public static List<HistoryVoyages.Voyage> Apply(List<HistoryVoyages.Voyage> fromFile)
    {
        if (Map.Count == 0) return fromFile;

        var rows = fromFile.Where(v => !Map.ContainsKey(v.Voyager)).ToList();
        foreach (var (_, finds) in Map) rows.AddRange(finds);
        rows.Sort(HistoryVoyages.ByVoyagerThenDate);
        return rows;
    }

    /// <summary>그 사람의 목록을 통째로 갈아 끼운다.</summary>
    public static void Set(int voyager, IEnumerable<HistoryVoyages.Voyage> finds)
    {
        if (voyager < 0 || voyager >= HistoryVoyages.Count) return;

        var rows = finds.Where(v => v.Voyager == voyager).ToList();
        rows.Sort(HistoryVoyages.ByVoyagerThenDate);
        Map[voyager] = rows;
        Save();
    }

    /// <summary>씌운 것을 걷어 파일 값으로 되돌린다.</summary>
    public static void Reset(int voyager)
    {
        if (!Map.Remove(voyager)) return;
        Save();
    }

    /// <summary>씌운 것을 몽땅 걷는다.</summary>
    public static void ResetAll()
    {
        if (Map.Count == 0) return;
        Map.Clear();
        Save();
    }

    private static Dictionary<int, List<HistoryVoyages.Voyage>> Map => _map ??= Load();

    private static Dictionary<int, List<HistoryVoyages.Voyage>> Load()
    {
        var saved = TableCache.Read<Snapshot>(CacheName);
        var map = new Dictionary<int, List<HistoryVoyages.Voyage>>();
        foreach (var row in saved?.Data.Voyagers ?? [])
            if (row.Voyager >= 0 && row.Voyager < HistoryVoyages.Count)
                map[row.Voyager] = row.Finds ?? [];
        return map;
    }

    private static void Save()
    {
        var rows = Map.OrderBy(p => p.Key).Select(p => new Entry(p.Key, p.Value)).ToList();
        int finds = rows.Sum(r => r.Finds.Count);
        TableCache.Write(CacheName, new TableCache.Cached<Snapshot>(
            $"{rows.Count}명 {finds}건", new Snapshot(rows), "사람이 고친 것"));
        Revision++;
    }
}
