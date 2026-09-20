using System.Text.Json.Serialization;

namespace CdsHelper.Game.Local.Helpers;

/// <summary>
/// CDS_95.EXE 안의 힌트 표. 힌트 하나마다 한 줄이고, 이름·갈래·등급과 후원자에게 손 벌릴 때
/// 쓰는 자금·기한이 다 들어 있다.
/// </summary>
/// <remarks>
/// <code>
///   표     VA 0x004D8E80, 186행 x 80바이트
///   +0x00  이름 ptr("아프리카남단")   +0x04  등급(1~5)
///   +0x08  발견물 일련번호(0~527)     +0x0C  갈래(0~7)
///   +0x14  자금(닢, 5000~300000)     +0x18  기한 기준(0~7)
///   +0x1C  제 번호
///   갈래 이름표 0x00560C60[8]
/// </code>
/// 왕궁에서 후원자를 설득할 때 게임이 이 표를 그대로 쓴다(<c>0x004AEF50</c>) —
/// 자금은 <c>+0x14</c> 에 후원율을 곱해 10닢 단위로 내리고, 계약 기한은 <c>+0x18</c> 에 하나를
/// 더한 햇수다(<c>0x004AF19D</c> 가 <c>0x004D8E98 = 표 + 0x18</c> 를 읽는다).
///
/// <b>표머리는 0x004D8E84 가 아니라 0x004D8E80 이다.</b> 네 바이트 뒤에서 끊어 읽으면 이름만
/// 한 줄씩 밀려, 힌트 이름이 하나같이 다음 힌트 것으로 나온다("아프리카남단"이 "서회항로"로).
/// 값 칸은 밀려도 자리가 맞아떨어져서 오래 티가 안 났다 — 발견물 번호로 발견물을 찾아 보고서야
/// 드러났다. 지금 자리로 읽으면 186줄이 모두 이름을 갖고, 발견물과도 하나씩 정확히 짝을 맺는다
/// (짝 짓는 곳은 <c>0x004AACFD</c> — 발견물 표 <c>+0x08</c> 과 이 표 <c>+0x08</c> 을 견준다).
///
/// 힌트를 <b>얻었는지</b>는 이 표가 아니라 실행 중 배열(<c>0x0058B4E0</c>, 8바이트 x 186)에
/// 있다. 그쪽은 EXE 파일에 없다(BSS) — 세이브에서 읽어야 한다.
///
/// 표는 EXE 에서 그때그때 읽는다. 판이 다르면 열리지 않을 뿐 엉뚱한 값을 내지 않도록
/// 첫 줄이 "아프리카남단"인지 보고 아니면 물러난다.
/// </remarks>
public sealed class HintTable
{
    private const int TableVa = 0x004D8E80;
    private const int RowCount = 186;
    private const int RowSize = 80;
    private const int CategoryNamesVa = 0x00560C60;

    /// <summary>
    /// 힌트 <b>설명</b>의 글 표. 힌트 줄의 <c>+0x1C</c> 가 이 표의 몇 번째인지를 든다.
    /// </summary>
    /// <remarks>
    /// <code>
    ///   0042CC70  eax = 힌트 번호
    ///             eax *= 80                        ; 힌트 줄 크기
    ///             ecx = [0x004D8E9C + eax]         ; 곧 힌트 줄의 +0x1C
    ///             eax = [0x00543FA0 + ecx*4]       ; 설명 글
    /// </code>
    /// 도서관에서 책을 읽으면 이 글이 <b>펼친 책</b>의 오른쪽 면에 적힌다.
    /// 번호가 힌트 번호와 <b>따로 논다</b> — 알 함브라 궁전은 힌트 75 인데 설명은 181 이다.
    /// </remarks>
    private const int TextTableVa = 0x00543FA0;

    /// <summary>설명 한 줄의 가장 긴 길이(바이트). 예순넷으로는 어림도 없다 —
    /// 두어 줄짜리 설명이 흔해서 그 길이를 넘으면 <b>통째로 null 이 되어</b> 빈 글이 됐다.</summary>
    private const int TextLimit = 512;

    /// <summary>판이 다른 EXE 를 잘못 읽지 않으려고 대 보는 첫 줄.</summary>
    private const string ProbeName = "아프리카남단";

    /// <summary>
    /// 이름이 놓일 수 있는 가장 낮은 자리(.rdata 시작). 이보다 낮으면 코드 구역이라 이름이 아니다.
    /// </summary>
    private const uint LowestNameVa = 0x004C3000;

    /// <summary>
    /// 알맹이 모양 판. 이름 자리를 바로잡으면서 올렸다 — 옛 모양으로 적어 둔 JSON 은 버리고
    /// 다시 굽게 한다.
    /// </summary>
    private const int SnapshotVersion = 4;

    /// <summary>갈래 수 — 지리·역사·보물·종교·교역품·미신·생물·민족.</summary>
    public const int CategoryCount = 8;

    /// <summary>힌트 한 줄.</summary>
    /// <param name="Id">힌트 번호(0~185).</param>
    /// <param name="Name">이름("인도항로", "아가멤논의 마스크").</param>
    /// <param name="Grade">등급 1~5. 후원자 안목이 모자라면 "이야기가 막연하다" 고 물린다.</param>
    /// <param name="Category">갈래 0~7. 후원자마다 좋아하는 갈래가 다르다.</param>
    /// <param name="Funds">이 힌트를 좇는 데 드는 자금(닢). 후원율을 곱하기 전 값이다.</param>
    /// <param name="Deadline">계약 기한(년).</param>
    /// <param name="Text">펼친 책에 적히는 설명. 힌트 줄의 <c>+0x1C</c> 가 가리키는 글이다.</param>
    /// <param name="Discovery">
    /// 발견물 일련번호. 발견물 표의 <see cref="DiscoveryTable.Record.Hint"/> 와 <b>같은 값</b>이면
    /// 그 발견물을 가리킨다 — 게임도 그렇게 짝을 짓는다(<c>0x004AACFD</c>).
    /// </param>
    /// <remarks>
    /// 레코드 <b>구조체</b>는 빈 생성자가 늘 있어서, 적어 둔 JSON 을 되읽을 때 어느 것을 쓸지
    /// 일러 주지 않으면 값이 전부 0 으로 들어온다.
    /// </remarks>
    [method: JsonConstructor]
    public readonly record struct Hint(
        int Id, string Name, int Grade, int Category, int Funds, int Deadline, int Discovery,
        string Text = "");

    /// <summary>적어 둘 파일 이름(<c>%APPDATA%\CdsHelper\exe-tables\힌트표.json</c>).</summary>
    private const string CacheName = "힌트표";

    /// <summary>JSON 으로 적어 두는 알맹이. EXE 를 읽어야만 알 수 있는 것 전부다.</summary>
    internal sealed record Snapshot(List<Hint> Hints, string[] CategoryNames);

    private readonly List<Hint> _hints;

    private HintTable(Snapshot snapshot)
    {
        _hints = snapshot.Hints;
        CategoryNames = snapshot.CategoryNames;
    }

    /// <summary>왜 못 읽었는지. 잘 열렸으면 빈 문자열.</summary>
    public static string LastError { get; private set; } = "";

    /// <summary>갈래 이름 8개(번호 차례).</summary>
    public IReadOnlyList<string> CategoryNames { get; }

    /// <summary>
    /// 표에 있는 힌트 전부 — 원본 186줄에 <see cref="HintEdits"/> 로 더한 새 힌트를 얹은 것.
    /// </summary>
    /// <remarks>사람이 고쳐 둔 것이 있으면 그것이 이긴다.</remarks>
    public IReadOnlyList<Hint> Hints
    {
        get
        {
            var merged = _hints.Select(HintEdits.Apply).ToList();
            var baseIds = _hints.Select(h => h.Id).ToHashSet();
            foreach (var (id, entry) in HintEdits.All)
                if (!baseIds.Contains(id)) merged.Add(HintEdits.Synthesize(entry));
            return merged;
        }
    }

    /// <summary>고치기 전 게임 값. 되돌리거나 견줄 때 쓴다. 원본에 없던 번호면 null.</summary>
    public Hint? Original(int id)
    {
        if (id < 0) return null;
        if (id < _hints.Count && _hints[id].Id == id) return _hints[id];
        foreach (var h in _hints)
            if (h.Id == id) return h;
        return null;
    }

    /// <summary>
    /// 그 번호의 힌트. 원본에 없어도 <see cref="HintEdits"/> 로 더한 것이면 나온다.
    /// </summary>
    /// <remarks>
    /// 186줄이 다 차 있어 자리와 번호가 늘 같지만, 옛 판의 JSON 이 남아 있을 수도 있으므로
    /// 자리로 먼저 짚어 보고 번호가 다르면 훑는다.
    /// </remarks>
    public Hint? Find(int id)
    {
        if (Original(id) is { } row) return HintEdits.Apply(row);
        return HintEdits.Of(id) is { } entry ? HintEdits.Synthesize(entry) : null;
    }

    /// <summary>그 힌트의 이름. 표 밖이면 번호로 물러선다.</summary>
    public string NameOf(int id) => Find(id)?.Name ?? $"힌트 {id}";

    /// <summary>그 갈래의 이름. 번호가 이상하면 빈 문자열.</summary>
    public string CategoryOf(int category) =>
        category >= 0 && category < CategoryNames.Count ? CategoryNames[category] : "";

    /// <summary>
    /// 후원자가 낼 자금. 게임처럼 후원율을 곱해 10닢 단위로 내리고, 20닢 밑으로는 안 내려간다.
    /// </summary>
    /// <param name="supportRate">후원율(%). patrons.json 의 supportRate 다.</param>
    public static int FundsFor(Hint hint, int supportRate)
    {
        long paid = (long)hint.Funds * supportRate / 100;
        if (paid < 20) paid = 20;
        return (int)(paid / 10 * 10);
    }

    /// <summary>
    /// 표를 연다. 적어 둔 JSON 이 있으면 그것을 읽고, 없거나 판이 갈렸으면 EXE 에서 읽어
    /// 적어 둔다. 둘 다 없을 때만 null 이다.
    /// </summary>
    public static HintTable? Open(string gameDirectory)
    {
        var snapshot = ExeTable.Open<Snapshot>(CacheName, gameDirectory, ReadFromExe, out string error,
                                               SnapshotVersion);
        LastError = error;
        return snapshot == null ? null : new HintTable(snapshot);
    }

    /// <summary>EXE 에서 힌트 줄과 갈래 이름을 통째로 읽어 낸다.</summary>
    private static Snapshot? ReadFromExe(PeImage exe, out string error)
    {
        error = "";

        var hints = new List<Hint>(RowCount);
        for (int k = 0; k < RowCount; k++)
        {
            int row = TableVa + k * RowSize;
            uint namePtr = exe.Word(row + 0x00);
            if (namePtr < LowestNameVa) continue;        // 이름이 없는 줄

            var name = exe.Text(namePtr);
            if (string.IsNullOrEmpty(name)) continue;

            hints.Add(new Hint(
                Id: k,
                Name: name,
                Grade: exe.Int(row + 0x04),
                Category: exe.Int(row + 0x0C),
                Funds: exe.Int(row + 0x14),
                Deadline: exe.Int(row + 0x18) + 1,
                Discovery: exe.Int(row + 0x08),
                Text: exe.Text(exe.Word(TextTableVa + exe.Int(row + 0x1C) * 4),
                              TextLimit) ?? ""));
        }

        // 판이 다른 EXE 를 잘못 읽지 않도록 첫 줄을 확인한다.
        if (hints.Count == 0 || hints[0].Name != ProbeName)
        {
            error = "힌트 표가 기대한 모양이 아닙니다(다른 판의 EXE 일 수 있습니다)";
            return null;
        }

        var categories = new string[CategoryCount];
        for (int i = 0; i < CategoryCount; i++)
            categories[i] = exe.Text(exe.Word(CategoryNamesVa + i * 4)) ?? "";

        return new Snapshot(hints, categories);
    }
}
