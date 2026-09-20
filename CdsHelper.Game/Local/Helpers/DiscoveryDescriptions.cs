namespace CdsHelper.Game.Local.Helpers;

/// <summary>
/// 발견물 설명문 274개 — 백과사전 오른쪽 면에 뜨는 그 글이다.
/// </summary>
/// <remarks>
/// <code>
///   표 VA 0x0057AA78   4바이트 포인터 x 274 (색인 = 발견물 번호, 0x004AAE80)
///   [0]   -> 0x0057A4CC  "아프리카의 최남단. 역사상으로는 포르투갈의 바르톨로메우·디아스가 1488년에 발견했다."
///   [229] -> 0x00572B80  "권리와 자유를 빼앗긴 인간. …"
/// </code>
/// <see cref="ItemDescriptions"/> 와 같은 꼴이다. EXE 에서 한 번만 읽고 JSON 으로 적어 둔다.
/// </remarks>
public sealed class DiscoveryDescriptions
{
    /// <summary>적어 둘 파일 이름(<c>%APPDATA%\CdsHelper\exe-tables\발견물설명.json</c>).</summary>
    private const string CacheName = "발견물설명";

    /// <summary>포인터 표.</summary>
    private const int TableVa = 0x0057AA78;

    /// <summary>발견물 수.</summary>
    public const int Count = 274;

    /// <summary>한 줄의 길이 한계(바이트). 가장 긴 것이 284 바이트다.</summary>
    private const int MaxLength = 512;

    /// <summary>판이 다른 EXE 를 잘못 읽지 않으려고 대 보는 줄.</summary>
    private const int ProbeId = 0;
    private const string ProbeHead = "아프리카의 최남단";

    /// <summary>JSON 으로 적어 두는 알맹이. 색인이 곧 발견물 번호다.</summary>
    internal sealed record Snapshot(string[] Descriptions);

    private readonly string[] _texts;

    private DiscoveryDescriptions(Snapshot snapshot) => _texts = snapshot.Descriptions;

    /// <summary>왜 못 읽었는지. 잘 열렸으면 빈 문자열.</summary>
    public static string LastError { get; private set; } = "";

    /// <summary>그 발견물의 설명문. 없으면 빈 문자열.</summary>
    public string Of(int discovery) =>
        discovery >= 0 && discovery < _texts.Length ? _texts[discovery] : "";

    /// <summary>표를 연다. 적어 둔 JSON 이 있으면 그것을, 없으면 EXE 를 읽는다. 둘 다 없으면 null.</summary>
    public static DiscoveryDescriptions? Open(string gameDirectory)
    {
        var snapshot = ExeTable.Open<Snapshot>(CacheName, gameDirectory, ReadFromExe, out string error);
        LastError = error;
        return snapshot == null ? null : new DiscoveryDescriptions(snapshot);
    }

    private static Snapshot? ReadFromExe(PeImage exe, out string error)
    {
        error = "";

        var texts = new string[Count];
        for (int id = 0; id < Count; id++)
            texts[id] = exe.Text(exe.Word(TableVa + id * 4), MaxLength) ?? "";

        if (!texts[ProbeId].StartsWith(ProbeHead, StringComparison.Ordinal))
        {
            error = "발견물 설명 표가 기대한 모양이 아닙니다(다른 판의 EXE 일 수 있습니다)";
            return null;
        }

        return new Snapshot(texts);
    }
}
