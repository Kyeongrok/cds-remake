namespace CdsHelper.Game.Local.Helpers;

/// <summary>
/// 도시의 문화권을 손으로 갈아 둔 것. 게임 표(<see cref="CityExeTable"/>) 위에 덧씌운다.
/// </summary>
/// <remarks>
/// 도구 · 도시 · 문화권 창에서 세빌리아를 이슬람으로 갈면, 그 도시에 들어갔을 때 조선소에
/// 앉은 사람도 이슬람 쪽 얼굴로 바뀌어야 한다 — 화자표를 <c>[건물코드][문화권]</c> 으로
/// 타기 때문이다(<see cref="SpeakerFaceTable"/>).
///
/// 그래서 갈아 둔 값을 <b>표 안쪽</b>에 끼운다. <see cref="CityExeTable.CultureOf"/> 가
/// 여기를 먼저 보므로 문화권을 묻는 자리는 어디든(시설 화자 · 여관 값 · 도시정보 창) 저절로
/// 따라온다. 부르는 쪽마다 "갈아 둔 것이 있나" 를 따로 물을 일이 없다.
///
/// EXE 는 손대지 않는다 — <c>.rdata</c> 에 박힌 원본은 그대로 두고 앱이 읽은 값만 덮는다.
/// 갈아 둔 것은 <c>%APPDATA%\CdsHelper\exe-tables</c> 에 적어 두어 앱을 껐다 켜도 남는다.
/// </remarks>
public static class CityCultureEdits
{
    /// <summary>적어 둘 파일 이름(<c>도시-문화권-고친것.json</c>).</summary>
    private const string CacheName = "도시-문화권-고친것";

    /// <summary>문화권 이름 — 차례가 곧 번호다(게임 도시 표 <c>+0x20</c>).</summary>
    /// <remarks>
    /// 앱 DB 가 도시마다 들고 있는 이름과 같은 말을 쓴다(<see cref="CityTable.Entry.Culture"/>) —
    /// 건물 사진과 술집 손님이 그 <b>이름</b>으로 갈리기 때문에 번호만 갈아서는 반만 바뀐다.
    /// </remarks>
    public static readonly string[] Names =
    [
        "이베리아", "북유럽", "지중해", "아프리카", "이슬람", "인도",
        "중국", "중앙아시아", "동남아시아", "일본", "아메리카",
    ];

    /// <summary>갈아 둔 한 줄.</summary>
    public readonly record struct Entry(int City, int Culture);

    /// <summary>JSON 으로 적어 두는 알맹이.</summary>
    internal sealed record Snapshot(List<Entry> Cities);

    /// <summary>없음.</summary>
    public const int None = -1;

    private static Dictionary<int, int>? _map;

    /// <summary>문화권이 갈렸을 때 알린다 — 열려 있는 도시 창이 제 값을 다시 묻는다.</summary>
    public static event Action? Changed;

    /// <summary>갈아 둔 도시 전부. 하나도 없으면 빈 목록.</summary>
    public static IReadOnlyDictionary<int, int> All => Map;

    /// <summary>그 도시에 씌워 둔 문화권. 안 갈았으면 <see cref="None"/>.</summary>
    public static int Of(int cityId) => Map.TryGetValue(cityId, out int culture) ? culture : None;

    /// <summary>번호를 이름으로. 모르는 번호면 빈 문자열.</summary>
    public static string NameOf(int culture) =>
        culture >= 0 && culture < Names.Length ? Names[culture] : "";

    /// <summary>그 도시의 문화권을 갈아 씌운다.</summary>
    public static void Set(int cityId, int culture)
    {
        if (culture < 0 || culture >= Names.Length) return;
        if (Of(cityId) == culture) return;
        Map[cityId] = culture;
        Save();
    }

    /// <summary>씌운 것을 걷어 게임 값으로 되돌린다.</summary>
    public static void Reset(int cityId)
    {
        if (!Map.Remove(cityId)) return;
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
        foreach (var row in saved?.Data.Cities ?? [])
            if (row.Culture >= 0 && row.Culture < Names.Length) map[row.City] = row.Culture;
        return map;
    }

    private static void Save()
    {
        var rows = Map.OrderBy(p => p.Key).Select(p => new Entry(p.Key, p.Value)).ToList();
        TableCache.Write(CacheName, new TableCache.Cached<Snapshot>(
            $"{rows.Count}곳", new Snapshot(rows), "사람이 고친 것"));
        Changed?.Invoke();
    }
}
