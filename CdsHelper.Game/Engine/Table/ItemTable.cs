using System.Text.Json.Serialization;

namespace CdsHelper.Game.Local.Helpers;

/// <summary>
/// CDS_95.EXE 안의 아이템 표 286개 — 이름·그림번호·정가·효과·분류.
/// </summary>
/// <remarks>
/// <code>
///   표 VA 0x004FD558 (파일오프셋 0x0FBB58), 28바이트 x 286 (색인 = 아이템 번호)
///   +0x00  이름 ptr("바스타드소드")   +0x04  그림번호(-1 = 없음, 0~205)
///   +0x08  살 때 정가(12000)        +0x0C  팔 때 정가(6000)
///   +0x10  효과(48)                 +0x14  분류(0~8)
///   +0x18  걸린 힌트(-1 = 없음) — 로제타석·사자의 책 같은 고문서 아홉 개만 있다(0x00465800)
/// </code>
/// 자리는 cds95-mod 의 <c>CharacterUtilKR/src/itemdb.h</c> 가 밝힌 것이다. 값·효과가
/// <c>item.json</c> 과 맞는 것을 대조해 확인했다(286 중 값 271개·효과 전부).
///
/// <b>그림번호는 아이템 번호와 1:1 이 아니다.</b> 99개는 그림이 없고(-1), 한 그림을 여럿이
/// 나눠 쓰기도 한다(58번은 아이템 12개). 그림은 반드시 <see cref="Record.Pic"/> 으로 찾는다.
///
/// <c>item.json</c> 과 겹치지만 이쪽이 원본이다 — 이름 띄어쓰기가 몇 개 다르고("수수경단"
/// 대 "수수 경단"), 값도 15개가 어긋난다. 시장 값은 이 표를 쓴다.
/// </remarks>
public sealed class ItemTable
{
    /// <summary>적어 둘 파일 이름(<c>%APPDATA%\CdsHelper\exe-tables\아이템표.json</c>).</summary>
    private const string CacheName = "아이템표";

    private const int TableVa = 0x004FD558;
    private const int RowSize = 28;

    /// <summary>아이템 수.</summary>
    public const int Count = 286;

    /// <summary>그림 번호의 최대값. <c>asset/item/item-205.png</c> 까지 있다.</summary>
    public const int MaxPic = 205;

    /// <summary>분류 이름. 표의 <c>+0x14</c> 가 이 차례를 가리킨다.</summary>
    public static readonly string[] CategoryNames =
    [
        "발명품", "선물·보석", "항해도구", "무기", "방어구",
        "교역품·미술품", "조각상", "서적·유물", "동물",
    ];

    /// <summary>판이 다른 EXE 를 잘못 읽지 않으려고 대 보는 줄.</summary>
    private const int ProbeId = 42;
    private const string ProbeName = "바스타드소드";

    /// <summary>아이템 한 개.</summary>
    /// <param name="Pic">그림 번호. 없으면 -1.</param>
    /// <param name="BuyList">살 때 정가. 시세를 먹이기 전 값이다.</param>
    /// <param name="SellList">팔 때 정가.</param>
    /// <param name="Effect">효과. 아이템 창 오른쪽 위에 뜨는 수다.</param>
    /// <param name="Category">분류 번호(0~8). 이름은 <see cref="CategoryNames"/>.</param>
    /// <param name="Hint">
    /// 걸린 힌트 번호. 없으면 -1. 소지품 정보에서 이 아이템을 보면 그 힌트를 읽어 볼 수 있다(<c>0x0046E8D2</c>).
    /// </param>
    /// <remarks>
    /// 레코드 <b>구조체</b>는 빈 생성자가 늘 있어서, 적어 둔 JSON 을 되읽을 때 어느 것을 쓸지
    /// 일러 주지 않으면 값이 전부 0 으로 들어온다.
    /// </remarks>
    [method: JsonConstructor]
    public readonly record struct Record(
        int Id, string Name, int Pic, int BuyList, int SellList, int Effect, int Category, int Hint = -1)
    {
        /// <summary>그림이 있는지.</summary>
        [JsonIgnore] public bool HasPic => Pic >= 0 && Pic <= MaxPic;

        /// <summary>분류 이름. 모르는 번호면 빈 문자열.</summary>
        [JsonIgnore]
        public string CategoryName =>
            Category >= 0 && Category < CategoryNames.Length ? CategoryNames[Category] : "";
    }

    /// <summary>알맹이 모양 판. 힌트 칸(<see cref="Record.Hint"/>)을 더하면서 2 로 올렸다.</summary>
    private const int SnapshotVersion = 2;

    /// <summary>JSON 으로 적어 두는 알맹이.</summary>
    internal sealed record Snapshot(Record[] Items);

    private readonly Record[] _items;

    private ItemTable(Snapshot snapshot) => _items = snapshot.Items;

    /// <summary>왜 못 읽었는지. 잘 열렸으면 빈 문자열.</summary>
    public static string LastError { get; private set; } = "";

    /// <summary>아이템 전부. 색인이 곧 아이템 번호다.</summary>
    public IReadOnlyList<Record> Items => _items;

    /// <summary>그 아이템. 범위 밖이면 null.</summary>
    public Record? Find(int id) => id >= 0 && id < _items.Length ? _items[id] : null;

    /// <summary>이름으로 찾는다. 없으면 null.</summary>
    public Record? Find(string name)
    {
        foreach (var r in _items)
            if (r.Name == name) return r;
        return null;
    }

    /// <summary>
    /// 표를 연다. 적어 둔 JSON 이 있으면 그것을 읽고, 없거나 판이 갈렸으면 EXE 에서 읽어
    /// 적어 둔다. 둘 다 없을 때만 null 이다.
    /// </summary>
    public static ItemTable? Open(string gameDirectory)
    {
        var snapshot = ExeTable.Open<Snapshot>(CacheName, gameDirectory, ReadFromExe, out string error,
                                               SnapshotVersion);
        LastError = error;
        return snapshot == null ? null : new ItemTable(snapshot);
    }

    private static Snapshot? ReadFromExe(PeImage exe, out string error)
    {
        error = "";

        var items = new Record[Count];
        for (int id = 0; id < Count; id++)
        {
            int row = TableVa + id * RowSize;
            items[id] = new Record(
                id,
                exe.Text(exe.Word(row + 0x00)) ?? "",
                exe.Int(row + 0x04),
                exe.Int(row + 0x08),
                exe.Int(row + 0x0C),
                exe.Int(row + 0x10),
                exe.Int(row + 0x14),
                exe.Int(row + 0x18));
        }

        if (items[ProbeId].Name != ProbeName)
        {
            error = "아이템 표가 기대한 모양이 아닙니다(다른 판의 EXE 일 수 있습니다)";
            return null;
        }

        return new Snapshot(items);
    }
}
