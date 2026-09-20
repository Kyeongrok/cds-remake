using System.Text.Json.Serialization;

namespace CdsHelper.Game.Local.Helpers;

/// <summary>
/// CDS_95.EXE 안의 건물 표. (도시 × 건물) 한 쌍마다 한 줄이고, 그 줄에 이름·종류·도시 그림
/// 좌표·가르치는 기능까지 다 들어 있다.
/// </summary>
/// <remarks>
/// <code>
///   표     VA 0x00500918, 1504행 x 0x38 (.rdata)
///   +0x00  이름 ptr("베렌의 탑")     +0x04  종류이름 ptr("항구")
///   +0x08  도시 번호                 +0x0C  건물 코드
///   +0x14  발견물 그림 번호(DSTILL)  +0x1C  발견물 번호 (-1 = 발견물이 아님)
///   +0x20,+0x24  도시 그림 좌표      +0x28,+0x2C  96 x 80 (고정)
///   +0x30  기능·언어 비트마스크      +0x34  0
///   기능 이름표 0x00560A10[13] · 언어 이름표 0x00560A48[14]
/// </code>
/// 좌표는 도시 그림(400x320) 기준이고, 96x80 상자의 <b>가운데</b>가 건물이다. 리스본에서
/// 건물 여덟 개를 그림에서 따로 찾아 대 보니 가운데가 ±9점 안에 들었다.
///
/// 예전에는 건물 그림을 도시마다 맞춰서 자리를 찾았는데, 그 방식은 리스본 말고는 믿을 수
/// 없었다 — 같은 그림이 도시마다 다른 시설로 쓰이기 때문이다(베니스의 붉은 지붕 집은 술집이
/// 아니다). 이 표가 게임이 실제로 쓰는 자리다.
///
/// 표는 EXE 에서 <b>한 번만</b> 읽고 JSON 으로 적어 둔다(<see cref="ExeTable"/>). 그 뒤로는
/// 게임이 없어도 열린다 — 건물 이름·자리·가르치는 기능은 판이 같으면 늘 같은 값이라
/// 켤 때마다 3MB EXE 를 뒤질 까닭이 없다. 판이 갈리면 도장이 달라져 저절로 다시 읽는다.
///
/// 판이 다른 EXE 를 잘못 읽어 엉뚱한 값을 적어 두지 않도록, 읽은 뒤 첫 줄이 "항구"인지
/// 보고 아니면 물러난다.
/// </remarks>
public sealed class CityBuildingTable
{
    private const int TableVa = 0x00500918;
    private const int RowCount = 1504;
    private const int RowSize = 0x38;
    /// <summary>
    /// 건물 <b>해설</b>의 글 표. 건물 줄의 <c>+0x18</c> 이 이 표의 몇 번째인지를 든다.
    /// </summary>
    /// <remarks>
    /// <code>
    ///   004733B1  [건물표 + 번호*0x38 + 0x18] 이 -1 이면 해설이 없다
    ///   004733D7  eax = [0x00567B38 + 그 값 * 4]
    /// </code>
    /// 히랄다탑은 2 번이다. 발견한 뒤에야 명령 창에 "해설" 줄이 붙는다.
    /// </remarks>
    private const int CommentTableVa = 0x00567B38;

    /// <summary>해설 글 한 줄이 길어 넉넉히 잡는다.</summary>
    private const int CommentLimit = 512;

    private const int SkillNamesVa = 0x00560A10;
    private const int LanguageNamesVa = 0x00560A48;

    /// <summary>기능 이름 수(비트 0~12).</summary>
    public const int SkillCount = 13;

    /// <summary>언어 이름 수(비트 13~26).</summary>
    public const int LanguageCount = 14;

    /// <summary>건물 상자 크기. 표에 96x80 으로 박혀 있다.</summary>
    public const int BoxWidth = 96, BoxHeight = 80;

    /// <summary>건물 한 채.</summary>
    /// <param name="City">도시 번호.</param>
    /// <param name="Code">건물 코드(항구 0 · 교역소 1 · 왕궁 2 · 교회 3 · 술집 4 …).</param>
    /// <param name="Kind">종류 이름("항구", "조합" …). 지도 이름표에 이것이 뜬다.</param>
    /// <param name="Name">건물 이름("베렌의 탑"). 명령 창 제목에 이것이 뜬다.</param>
    /// <param name="X">도시 그림에서 상자의 왼쪽 위(400x320 기준).</param>
    /// <param name="TeachMask">가르치는 기능·언어 비트. 0 이면 안 가르친다.</param>
    /// <param name="Discovery">
    /// 이 건물이 곧 발견물이면 그 번호(<c>+0x1C</c>), 아니면 -1. 세빌리아 교회는 51번
    /// 히랄다탑이다 — 건물에 들어서면 그 자리에서 발견한 것으로 적힌다.
    /// </param>
    /// <param name="Picture">
    /// 발견했을 때 낼 그림 번호(<c>+0x14</c>, <c>DSTILL.CDS</c>). 발견물이라도 그림이
    /// 없으면 -1 이다. 발견물 표에도 같은 번호가 적혀 있다(<c>0x0051C54C</c>).
    /// </param>
    /// <param name="Comment">
    /// 발견하고 나면 뜨는 <b>해설</b>. 건물 줄의 <c>+0x18</c> 이 글 번호고 -1 이면 없다.
    /// </param>
    /// <remarks>
    /// 레코드 <b>구조체</b>는 빈 생성자가 늘 있어서, 적어 둔 JSON 을 되읽을 때 어느 것을 쓸지
    /// 일러 주지 않으면 값이 전부 0 으로 들어온다. 그래서 <c>[method: JsonConstructor]</c> 로
    /// 이 생성자를 짚어 둔다. 계산으로 나오는 값(<see cref="CenterX"/> 따위)은 적지 않는다.
    /// </remarks>
    [method: JsonConstructor]
    public readonly record struct Building(
        int City, int Code, string Kind, string Name, int X, int Y, uint TeachMask,
        int Discovery = -1, int Picture = -1, string Comment = "")
    {
        /// <summary>이 건물 자체가 발견물인지.</summary>
        [JsonIgnore] public bool IsDiscovery => Discovery >= 0;

        /// <summary>건물이 그려진 자리(상자 가운데).</summary>
        [JsonIgnore] public int CenterX => X + BoxWidth / 2;
        [JsonIgnore] public int CenterY => Y + BoxHeight / 2;

        /// <summary>
        /// 누를 자리(<c>0x004733E0</c>) — 상자의 <b>가운데 절반</b>이다(<c>X + 너비/4, Y + 높이/4, 너비/2, 높이/2</c>).
        /// 마을 사람도 같은 셈이다(<c>0x00473880</c>).
        /// </summary>
        [JsonIgnore] public int HitX => X + BoxWidth / 4;
        [JsonIgnore] public int HitY => Y + BoxHeight / 4;
        [JsonIgnore] public int HitWidth => BoxWidth / 2;
        [JsonIgnore] public int HitHeight => BoxHeight / 2;

        /// <summary>무언가 가르치는 건물인지(조합·교회·학자 저택).</summary>
        [JsonIgnore] public bool Teaches => TeachMask != 0;
    }

    /// <summary>적어 둘 파일 이름(<c>%APPDATA%\CdsHelper\exe-tables\건물표.json</c>).</summary>
    private const string CacheName = "건물표";

    /// <summary>알맹이 모양 판. 발견물 번호와 그림 번호를 더하며 2 로 올렸다.</summary>
    private const int Version = 3;

    /// <summary>
    /// JSON 으로 적어 두는 알맹이. EXE 를 읽어야만 알 수 있는 것이 여기 다 들어 있다 —
    /// 건물 줄뿐 아니라 기능·언어 이름표도 함께 적어야 EXE 없이 비트마스크를 풀 수 있다.
    /// </summary>
    internal sealed record Snapshot(List<Building> Buildings, string[] SkillNames, string[] LanguageNames);

    private readonly List<Building> _buildings;
    private readonly Dictionary<int, List<Building>> _byCity = [];

    private CityBuildingTable(Snapshot snapshot)
    {
        _buildings = snapshot.Buildings;
        SkillNames = snapshot.SkillNames;
        LanguageNames = snapshot.LanguageNames;
        foreach (var b in _buildings)
        {
            if (!_byCity.TryGetValue(b.City, out var list)) _byCity[b.City] = list = [];
            list.Add(b);
        }
    }

    /// <summary>왜 못 읽었는지. 잘 열렸으면 빈 문자열.</summary>
    public static string LastError { get; private set; } = "";

    /// <summary>기능 이름 13개(비트 0~12 차례).</summary>
    public IReadOnlyList<string> SkillNames { get; }

    /// <summary>언어 이름 14개(비트 13~26 차례).</summary>
    public IReadOnlyList<string> LanguageNames { get; }

    /// <summary>표에 있는 건물 전부.</summary>
    public IReadOnlyList<Building> Buildings => _buildings;

    /// <summary>그 도시의 건물들. 없으면 빈 목록.</summary>
    public IReadOnlyList<Building> InCity(int cityId) =>
        _byCity.TryGetValue(cityId, out var list) ? list : [];

    /// <summary>그 도시의 그 종류 건물. 없으면 null.</summary>
    public Building? Find(int cityId, string kind)
    {
        foreach (var b in InCity(cityId))
            if (b.Kind == kind) return b;
        return null;
    }

    /// <summary>비트마스크를 이름으로 푼다 — 기능 먼저, 언어 나중(게임 차례 그대로).</summary>
    public List<string> Teaches(uint mask)
    {
        var got = new List<string>();
        for (int i = 0; i < SkillCount; i++)
            if ((mask >> i & 1) != 0) got.Add(SkillNames[i]);
        for (int i = 0; i < LanguageCount; i++)
            if ((mask >> (SkillCount + i) & 1) != 0) got.Add(LanguageNames[i]);
        return got;
    }

    /// <summary>
    /// 표를 연다. 적어 둔 JSON 이 있으면 그것을 읽고, 없거나 판이 갈렸으면 EXE 에서 읽어
    /// 적어 둔다. 둘 다 없을 때만 null 이다.
    /// </summary>
    public static CityBuildingTable? Open(string gameDirectory)
    {
        var snapshot = ExeTable.Open<Snapshot>(CacheName, gameDirectory, ReadFromExe, out string error,
                                               Version);
        LastError = error;
        return snapshot == null ? null : new CityBuildingTable(snapshot);
    }

    /// <summary>EXE 에서 건물 줄과 기능·언어 이름표를 통째로 읽어 낸다.</summary>
    /// <summary>그 번호의 해설 글. 번호가 -1 이거나 못 읽으면 빈 글이다.</summary>
    private static string CommentOf(PeImage exe, int index) =>
        index < 0 ? "" : exe.Text(exe.Word(CommentTableVa + index * 4), CommentLimit) ?? "";

    private static Snapshot? ReadFromExe(PeImage exe, out string error)
    {
        error = "";

        var buildings = new List<Building>(RowCount);
        for (int k = 0; k < RowCount; k++)
        {
            int row = TableVa + k * RowSize;
            var name = exe.Text(exe.Word(row + 0x00));
            var kind = exe.Text(exe.Word(row + 0x04));
            if (name == null || kind == null) continue;

            buildings.Add(new Building(
                exe.Int(row + 0x08), exe.Int(row + 0x0C), kind, name,
                exe.Int(row + 0x20), exe.Int(row + 0x24), exe.Word(row + 0x30),
                exe.Int(row + 0x1C), exe.Int(row + 0x14),
                CommentOf(exe, exe.Int(row + 0x18))));
        }

        // 판이 다른 EXE 를 잘못 읽지 않도록 첫 줄을 확인한다.
        if (buildings.Count == 0 || buildings[0].Kind != "항구")
        {
            error = "건물 표가 기대한 모양이 아닙니다(다른 판의 EXE 일 수 있습니다)";
            return null;
        }

        var skills = new string[SkillCount];
        for (int i = 0; i < SkillCount; i++)
            skills[i] = exe.Text(exe.Word(SkillNamesVa + i * 4)) ?? "";
        var languages = new string[LanguageCount];
        for (int i = 0; i < LanguageCount; i++)
            languages[i] = exe.Text(exe.Word(LanguageNamesVa + i * 4)) ?? "";

        return new Snapshot(buildings, skills, languages);
    }
}
