using System.Text.Json.Serialization;

namespace CdsHelper.Game.Local.Helpers;

/// <summary>
/// 교역품 표 70가지 — 이름·그림·분류·개체중량. CDS_95.EXE 에 박혀 있다.
/// </summary>
/// <remarks>
/// <code>
///   표 VA 0x004DCBB0, 136바이트 x 70 (색인 = 교역품 종류)
///   +0x00  이름 ptr("대포")        +0x04  그림 번호(134~203)
///   +0x08  분류(0~12)             +0x0C~ 27칸 지역별 시세
///   +0x7C  개체중량               +0x80  수명(달) — 7 은 안 썩음, 어육 4·쇠고기 5·포도주·말·노예 6
///   분류 이름표 0x00547210, 13칸
/// </code>
/// 아이템 표(<see cref="ItemTable"/>)와는 <b>딴 표</b>다. 교역품 이름은 그쪽에 아예 없다.
/// 그림은 아이템과 한 파일(ITEM.CDS)에 있어 <see cref="ItemArt"/> 로 같이 꺼낸다 —
/// 교역품 70가지가 134~203 에 이름 차례 그대로 놓여 있다.
///
/// 대포는 분류 4(무기)에 개체중량 20, 철광석은 분류 9(광석)에 20 이다 — 게임 화면과 맞다.
/// </remarks>
public sealed class GoodsTable
{
    /// <summary>적어 둘 파일 이름(<c>%APPDATA%\CdsHelper\exe-tables\교역품표.json</c>).</summary>
    private const string CacheName = "교역품표";

    private const int TableVa = 0x004DCBB0;
    private const int RowSize = 0x88;
    private const int CategoryNamesVa = 0x00547210;

    /// <summary>교역품 수.</summary>
    public const int Count = 70;

    /// <summary>분류 수.</summary>
    public const int CategoryCount = 13;

    private const int ProbeId = 22;
    private const string ProbeName = "대포";

    /// <summary>교역품 한 가지.</summary>
    /// <param name="Pic">ITEM.CDS 그림 번호(134~203).</param>
    /// <param name="Weight">개체중량. 창에 그대로 뜬다.</param>
    [method: JsonConstructor]
    /// <param name="Life">수명(달). 짐 기한 첫값이 이것 x 30 이다(<c>0x004B5910</c>).</param>
    public readonly record struct Goods(int Id, string Name, int Pic, int Category, int Weight, int Life = 7)
    {
        /// <summary>새로 산 짐의 기한(날).</summary>
        public int FreshShelf => Life * 30;
    }

    /// <summary>JSON 으로 적어 두는 알맹이.</summary>
    internal sealed record Snapshot(Goods[] Items, string[] CategoryNames);

    private readonly Goods[] _items;

    private GoodsTable(Snapshot snapshot)
    {
        _items = snapshot.Items;
        CategoryNames = snapshot.CategoryNames;
    }

    /// <summary>왜 못 읽었는지. 잘 열렸으면 빈 문자열.</summary>
    public static string LastError { get; private set; } = "";

    /// <summary>분류 이름 13개(번호 차례).</summary>
    public IReadOnlyList<string> CategoryNames { get; }

    /// <summary>교역품 전부. 색인이 곧 종류 번호다.</summary>
    public IReadOnlyList<Goods> Items => _items;

    /// <summary>그 교역품. 범위 밖이면 null.</summary>
    public Goods? Find(int id) => id >= 0 && id < _items.Length ? _items[id] : null;

    /// <summary>분류 이름. 모르는 번호면 빈 문자열.</summary>
    public string CategoryName(int category) =>
        category >= 0 && category < CategoryNames.Count ? CategoryNames[category] : "";

    /// <summary>표를 연다. 적어 둔 JSON 이 있으면 그것을 읽는다.</summary>
    public static GoodsTable? Open(string gameDirectory)
    {
        var snapshot = ExeTable.Open<Snapshot>(CacheName, gameDirectory, ReadFromExe, out string error, 2);
        LastError = error;
        return snapshot == null ? null : new GoodsTable(snapshot);
    }

    private static Snapshot? ReadFromExe(PeImage exe, out string error)
    {
        error = "";

        var items = new Goods[Count];
        for (int id = 0; id < Count; id++)
        {
            int row = TableVa + id * RowSize;
            items[id] = new Goods(
                id,
                exe.Text(exe.Word(row + 0x00)) ?? "",
                exe.Int(row + 0x04),
                exe.Int(row + 0x08),
                exe.Int(row + 0x7C),
                exe.Int(row + 0x80));
        }

        if (items[ProbeId].Name != ProbeName)
        {
            error = "교역품 표가 기대한 모양이 아닙니다(다른 판의 EXE 일 수 있습니다)";
            return null;
        }

        var names = new string[CategoryCount];
        for (int i = 0; i < CategoryCount; i++)
            names[i] = exe.Text(exe.Word(CategoryNamesVa + i * 4)) ?? "";

        return new Snapshot(items, names);
    }
}
