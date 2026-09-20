using System.Text.Json.Serialization;

namespace CdsHelper.Game.Local.Helpers;

/// <summary>
/// 나라 표 40개 — 이름·언어·수도. CDS_95.EXE 에 박혀 있다.
/// </summary>
/// <remarks>
/// <code>
///   표 VA 0x004CA370, 24바이트 x 78 (색인 = 나라 번호)
///   +0x00  이름 ptr("잉글랜드 왕국")   +0x04  언어(0~13)   +0x08  수도 도시 번호
///   +0x0C  갈래(0x004A1800 이 읽는다)
///   +0x10  게임 판을 열 때 이 나라가 있는가(0/1)
///   +0x14  출입여부(0/1/2)
/// </code>
/// <b>나라는 마흔이 아니라 일흔여덟이다.</b> 예전에는 마흔에서 끊어 명·조선·일본과
/// 아메리카 나라들이 통째로 빠졌다 — 세이브가 이 표를 <c>0x005859C0</c> 부터
/// <c>0x00585EA0</c> 까지 적는데(<c>0x0047858E</c>) 한 칸이 열여섯이라 <b>일흔여덟</b>
/// 칸이다.</code>
/// <b>도시의 언어는 여기서 온다.</b> 도시 줄에는 언어가 없고 나라 번호만 있다
/// (<see cref="CityExeTable.NationOf"/>) — 도시를 정복하면 정복한 나라의 말을 쓰게 되므로
/// 도시에 박아 둘 수가 없다.
///
/// 잉글랜드 왕국은 언어 3(게르만어)에 수도 38(런던)이다 — 게임 화면과 맞다.
/// 나라 40개 중 37개는 제 수도의 나라 번호와 맞물린다. 어긋나는 셋(스웨덴·프로이센·
/// 모노모타파)은 <b>수도를 다른 나라와 함께 쓰는</b> 나라들이라, 그 도시가 한쪽만
/// 가리킬 수밖에 없는 것이다.
/// </remarks>
public sealed class NationTable
{
    /// <summary>적어 둘 파일 이름(<c>%APPDATA%\CdsHelper\exe-tables\나라표.json</c>).</summary>
    private const string CacheName = "나라표";

    private const int TableVa = 0x004CA370;
    private const int RowSize = 24;

    /// <summary>나라 수.</summary>
    public const int Count = 78;

    /// <summary>알맹이 모양 판 — 갈래를 더하고 나라 수를 고치면서 올렸다.</summary>
    private const int Shape = 3;

    private const int ProbeId = 11;
    private const string ProbeName = "잉글랜드 왕국";

    /// <summary>나라 하나.</summary>
    /// <param name="Language">언어 번호. 이름은 <see cref="CityBuildingTable.LanguageNames"/>.</param>
    /// <param name="Capital">수도 도시 번호.</param>
    /// <param name="Sect">
    /// 나라 갈래(<c>+0x0C</c>). <b>도시의 문화권과는 다른 물건</b>이다
    /// (<see cref="CityExeTable.CultureOf"/>).
    /// </param>
    /// <param name="Entry">
    /// <b>출입여부</b>(<c>+0x14</c>). 0 · 1 · 2 세 가지뿐이다 — <see cref="EntryNames"/>.
    /// </param>
    /// <remarks>
    /// 갈래는 유럽이 0, 동유럽·정교권이 2, <b>이슬람권이 3</b>(사파비만 4), 인도가 3·5·6,
    /// 명·조선이 7, 일본이 6, 아메리카가 8 이다. <b>3 이나 4 일 때만 적대 도시에
    /// 「잠입한다」가 켜진다</b>(<c>0x004A1800</c>) — 명이나 조선에 못 숨어드는 까닭이다.
    ///
    /// <b>출입여부</b>는 나라 형편 판의 씨앗이다. 판을 열 때 <c>0x0041B320</c> 이 나라마다
    /// 열여섯 바이트짜리 형편 칸(<c>0x005859C0</c>)을 이 표에서 떠 준다.
    /// <code>
    ///   41b32b  형편 +0x00 = 나라표 +0x08   수도 도시 번호
    ///   41b33e  형편 +0x04 = 나라표 +0x10   이 나라가 있는가
    ///   41b347  형편 +0x08 = 0
    ///   41b35e  형편 +0x0C = 나라표 +0x14   출입여부   ← 여기
    /// </code>
    /// 그 뒤로는 형편 칸이 움직인다(사건 스크립트가 <c>0x00409DAD</c> · <c>0x00409F15</c> 에서
    /// 갈아 끼운다). 읽는 곳은 <c>0x00429D90</c> 하나이고, 부르는 데가 셋이다.
    /// <code>
    ///   004687fd  출입여부 &gt; 0   → 적대 차림표 0x004A56F0(1)   ; 1 = 마을
    ///   004770bd  출입여부 == 2  → 적대 차림표 0x004A56F0(0)   ; 0 = 항구
    ///   00476f85  출입여부 == 2  면 건너뛴다
    /// </code>
    /// 인자가 마을이라는 것은 차림표 앞머리 <c>0x004A5210</c> 에서 드러난다 — 인자가 0 이
    /// 아닐 때만 "…어쩐지 <b>마을</b>에는 들여보내어 주지 않을 것 같습니다"를 고른다.
    ///
    /// <b>그래서 1 은 마을만 막고 항구는 연다. 2 라야 항구까지 막는다.</b> 1 인 열다섯이
    /// 죄다 이슬람권과 명이고, 2 인 둘이 그라나다와 오스만·투르크인 것이 이 읽기와 맞는다.
    ///
    /// <c>+0x10</c> 은 「판을 열 때 이 나라가 있는가」다. 0 인 열하나(스웨덴 · 프로이센 ·
    /// 무갈 · 사파비 · 세이바니 · 히바 · 벵갈 · 카슈가르 · 퉁구 버마 · 데마크 · 모노모타파)가
    /// 죄다 1480년대 뒤에 선 나라들이다. 아직 우리가 쓰지 않아 칸으로 두지 않았다.
    /// </remarks>
    [method: JsonConstructor]
    public readonly record struct Nation(int Id, string Name, int Language, int Capital,
                                         int Sect = 0, int Entry = 0);

    /// <summary>출입여부 세 가지의 이름.</summary>
    /// <remarks>
    /// 게임에 글 표가 따로 없다 — 코드가 하는 일을 보고 붙인 이름이다.
    /// </remarks>
    public static readonly string[] EntryNames =
        ["자유롭게 드나든다", "마을에는 못 들어간다", "항구까지 막는다"];

    /// <summary>출입여부 값의 개수.</summary>
    public const int EntryCount = 3;

    /// <summary>그 출입여부의 이름. 모르는 값이면 숫자 그대로.</summary>
    public static string EntryName(int entry) =>
        entry >= 0 && entry < EntryNames.Length ? EntryNames[entry] : $"{entry}";

    /// <summary>JSON 으로 적어 두는 알맹이.</summary>
    internal sealed record Snapshot(Nation[] Nations);

    private readonly Nation[] _nations;

    private NationTable(Snapshot snapshot) => _nations = snapshot.Nations;

    /// <summary>왜 못 읽었는지. 잘 열렸으면 빈 문자열.</summary>
    public static string LastError { get; private set; } = "";

    /// <summary>
    /// 나라 전부. 색인이 곧 나라 번호다.
    /// </summary>
    /// <remarks>사람이 고쳐 둔 것이 있으면 그것이 이긴다(<see cref="NationEdits"/>).</remarks>
    public IReadOnlyList<Nation> Nations => [.. _nations.Select(NationEdits.Apply)];

    /// <summary>그 나라. 범위 밖이면 null.</summary>
    public Nation? Find(int id) =>
        id >= 0 && id < _nations.Length ? NationEdits.Apply(_nations[id]) : null;

    /// <summary>고치기 전, EXE 에 적힌 그대로의 나라. 범위 밖이면 null.</summary>
    public Nation? Original(int id) => id >= 0 && id < _nations.Length ? _nations[id] : null;

    /// <summary>표를 연다. 적어 둔 JSON 이 있으면 그것을 읽는다.</summary>
    public static NationTable? Open(string gameDirectory)
    {
        var snapshot = ExeTable.Open<Snapshot>(CacheName, gameDirectory, ReadFromExe, out string error,
                                              Shape);
        LastError = error;
        return snapshot == null ? null : new NationTable(snapshot);
    }

    private static Snapshot? ReadFromExe(PeImage exe, out string error)
    {
        error = "";

        var nations = new Nation[Count];
        for (int id = 0; id < Count; id++)
        {
            int row = TableVa + id * RowSize;
            nations[id] = new Nation(
                id,
                exe.Text(exe.Word(row + 0x00)) ?? "",
                exe.Int(row + 0x04),
                exe.Int(row + 0x08),
                exe.Int(row + 0x0C),
                exe.Int(row + 0x14));
        }

        if (nations[ProbeId].Name != ProbeName)
        {
            error = "나라 표가 기대한 모양이 아닙니다(다른 판의 EXE 일 수 있습니다)";
            return null;
        }

        return new Snapshot(nations);
    }
}
