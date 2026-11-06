using System.Text.Json.Serialization;

namespace CdsHelper.Game.Local.Helpers;

/// <summary>
/// 후원자 수치를 손으로 고쳐 둔 것. 후원자 표(<see cref="SponsorTable"/>)가 읽을 때 번호로 얹는다.
/// </summary>
/// <remarks>
/// 도구 앱 「요소 → 후원자」 창(<c>SponsorListDialog</c>)이 고친다 — 안목 · 인심 · 취향 · 재력 · 언어 · 자리 · 등장/은퇴 연도.
/// <b>이름은 못 바꾼다</b> — 후원자 친밀도 · 계약은 세이브가 이름으로 찾는다.
///
/// 구워 둔 표(<c>sponsors.json</c>)는 건드리지 않고 고친 줄만 <c>sponsors-edits.json</c> 에 적는다(<see cref="NationEdits"/> 와 같은 결).
/// 커스텀 팩을 켜 두면 그 팩 폴더에 적힌다(<see cref="TableCache.Write{T}"/>) — 업데이트가 갈아 끼우는 설치 폴더 밖이라 남는다.
/// </remarks>
public static class SponsorEdits
{
    /// <summary>적어 둘 표 이름(<c>sponsors-edits.json</c>).</summary>
    private const string CacheName = "후원자-고친것";

    /// <summary>고쳐 둔 한 줄. 안 고친 칸은 null 이라 표 값이 그대로 남는다.</summary>
    /// <param name="Index">후원자 번호(<see cref="SponsorTable.Sponsor.Index"/>).</param>
    /// <param name="Appear">나오는 해 − 1480(표 칸과 같은 꼴).</param>
    /// <param name="Retire">물러나는 해(0 이면 안 물러난다).</param>
    /// <remarks>안 고친 칸(null)은 파일에 안 적는다 — 팩을 열어 보면 고친 칸만 보인다.</remarks>
    public sealed record Entry(int Index,
                               [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? Eye = null,
                               [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? Closeness = null,
                               [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? Tastes = null,
                               [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? Wealth = null,
                               [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? Languages = null,
                               [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? City = null,
                               [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? Building = null,
                               [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? Appear = null,
                               [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? Retire = null);

    /// <summary>JSON 으로 적어 두는 알맹이.</summary>
    internal sealed record Snapshot(List<Entry> Sponsors);

    private static Dictionary<int, Entry>? _map;

    /// <summary>켠 커스텀 팩이 바뀌면 담아 둔 것을 버린다 — 다음에 물을 때 새 팩으로 다시 읽는다.</summary>
    static SponsorEdits() => TableCache.Reloaded += () => _map = null;

    /// <summary>후원자가 고쳐졌을 때 알린다.</summary>
    public static event Action? Changed;

    /// <summary>그 후원자에 씌워 둔 것. 안 고쳤으면 null.</summary>
    public static Entry? Of(int index) => Map.TryGetValue(index, out var e) ? e : null;

    /// <summary>표 값 위에 고친 것을 얹어 낸다.</summary>
    public static SponsorTable.Sponsor Apply(SponsorTable.Sponsor s)
    {
        if (Of(s.Index) is not { } e) return s;
        return s with
        {
            Eye = e.Eye ?? s.Eye,
            Closeness = e.Closeness ?? s.Closeness,
            Tastes = e.Tastes ?? s.Tastes,
            Wealth = e.Wealth ?? s.Wealth,
            Languages = e.Languages ?? s.Languages,
            City = e.City ?? s.City,
            Building = e.Building ?? s.Building,
            Appear = e.Appear ?? s.Appear,
            Retire = e.Retire ?? s.Retire,
        };
    }

    /// <summary>
    /// 그 후원자를 고쳐 씌운다 — <paramref name="game"/>(고치기 전 표 값)과 같은 칸은 null 로 두어 고친 칸만 남긴다.
    /// 다 같으면 씌운 것을 걷는다.
    /// </summary>
    public static void Set(SponsorTable.Sponsor game, SponsorTable.Sponsor edited)
    {
        var entry = new Entry(game.Index,
            Diff(game.Eye, edited.Eye), Diff(game.Closeness, edited.Closeness), Diff(game.Tastes, edited.Tastes),
            Diff(game.Wealth, edited.Wealth), Diff(game.Languages, edited.Languages), Diff(game.City, edited.City),
            Diff(game.Building, edited.Building), Diff(game.Appear, edited.Appear), Diff(game.Retire, edited.Retire));
        if (entry == new Entry(game.Index)) { Reset(game.Index); return; }
        Map[game.Index] = entry;
        Save();

        static int? Diff(int was, int now) => was == now ? null : now;
    }

    /// <summary>씌운 것을 걷어 표 값으로 되돌린다.</summary>
    public static void Reset(int index)
    {
        if (!Map.Remove(index)) return;
        Save();
    }

    private static Dictionary<int, Entry> Map => _map ??= Load();

    private static Dictionary<int, Entry> Load()
    {
        var map = new Dictionary<int, Entry>();
        foreach (var row in TableCache.Read<Snapshot>(CacheName)?.Data.Sponsors ?? [])
            if (row.Index >= 0) map[row.Index] = row;
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
