namespace CdsHelper.Game.Local.Helpers;

/// <summary>
/// 아이템 설명문 286개. 아이템 창에 뜨는 그 글이다.
/// </summary>
/// <remarks>
/// 이름·갈래·효과·정가는 <c>item.json</c> 에 있는데 설명문만 거기 없다. 그것은 EXE 안에
/// 있고, 색인이 <c>item.json</c> 의 <c>id</c> 와 그대로 맞는다.
/// <code>
///   표 VA 0x00558B80   4바이트 포인터 x 286
///   [42] -> 0x00557080  "독일, 스위스 등지에서 발달한 단검. 보통때는 한손으로 …"
/// </code>
/// 286 개가 모두 이름과 맞는 것을 하나씩 대 보고 확인했다 — 33 나침반이 "항해 필수품. 배의
/// 방위를 알기 위한 도구", 45 투핸드소드가 "양손용 대검" 이다.
///
/// 처음에는 포인터 간격을 12 로 잘못 짚었다. 바스타드소드(42)와 투핸드소드(45)의 포인터가
/// 12바이트 떨어져 있었는데, 그 둘이 <b>세 칸</b> 차이라는 것을 보고 4 로 바로잡았다.
///
/// EXE 에서 한 번만 읽고 JSON 으로 적어 둔다(<see cref="ExeTable"/>) — 게임 없이도 아이템
/// 창을 낼 수 있어야 한다.
/// </remarks>
public sealed class ItemDescriptions
{
    /// <summary>적어 둘 파일 이름(<c>%APPDATA%\CdsHelper\exe-tables\아이템설명.json</c>).</summary>
    private const string CacheName = "아이템설명";

    /// <summary>포인터 표. 4바이트짜리가 <see cref="Count"/> 개 늘어서 있다.</summary>
    private const int TableVa = 0x00558B80;

    /// <summary>아이템 수. <c>item.json</c> 과 같아야 한다.</summary>
    public const int Count = 286;

    /// <summary>
    /// 설명문 한 줄의 길이 한계(바이트). 가장 긴 것이 200 바이트쯤이라 넉넉히 잡았다.
    /// </summary>
    private const int MaxLength = 512;

    /// <summary>
    /// 판이 다른 EXE 를 잘못 읽지 않으려고 대 보는 줄. 이것이 어긋나면 표를 안 받는다.
    /// </summary>
    private const int ProbeId = 42;
    private const string ProbeHead = "독일, 스위스 등지에서 발달한 단검";

    /// <summary>JSON 으로 적어 두는 알맹이. 색인이 곧 아이템 id 다.</summary>
    internal sealed record Snapshot(string[] Descriptions);

    private readonly string[] _texts;

    private ItemDescriptions(Snapshot snapshot) => _texts = snapshot.Descriptions;

    /// <summary>왜 못 읽었는지. 잘 열렸으면 빈 문자열.</summary>
    public static string LastError { get; private set; } = "";

    /// <summary>그 아이템의 설명문. 없으면 빈 문자열 — 부르는 쪽이 자리를 비워 두면 된다.</summary>
    public string Of(int itemId) =>
        itemId >= 0 && itemId < _texts.Length ? _texts[itemId] : "";

    /// <summary>설명문 전부. 색인이 아이템 id 다.</summary>
    public IReadOnlyList<string> All => _texts;

    /// <summary>
    /// 표를 연다. 적어 둔 JSON 이 있으면 그것을 읽고, 없거나 판이 갈렸으면 EXE 에서 읽어
    /// 적어 둔다. 둘 다 없을 때만 null 이다.
    /// </summary>
    public static ItemDescriptions? Open(string gameDirectory)
    {
        var snapshot = ExeTable.Open<Snapshot>(CacheName, gameDirectory, ReadFromExe, out string error);
        LastError = error;
        return snapshot == null ? null : new ItemDescriptions(snapshot);
    }

    /// <summary>EXE 의 포인터 표를 따라가 설명문을 다 읽어 온다.</summary>
    private static Snapshot? ReadFromExe(PeImage exe, out string error)
    {
        error = "";

        var texts = new string[Count];
        for (int id = 0; id < Count; id++)
            texts[id] = exe.Text(exe.Word(TableVa + id * 4), MaxLength) ?? "";

        if (!texts[ProbeId].StartsWith(ProbeHead, StringComparison.Ordinal))
        {
            error = "아이템 설명 표가 기대한 모양이 아닙니다(다른 판의 EXE 일 수 있습니다)";
            return null;
        }

        return new Snapshot(texts);
    }
}
