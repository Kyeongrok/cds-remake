using System.Text.Json.Serialization;

namespace CdsHelper.Game.Local.Helpers;

/// <summary>
/// CDS_95.EXE 안의 발견물 표 274개 — 이름·갈래·보수와 <b>어디서 발견되는지</b>.
/// </summary>
/// <remarks>
/// <code>
///   표 VA 0x0051C540 (파일오프셋 0x11AB40), 92바이트 x 274, 끝 0x005227B8
///   +0x00  이름 ptr("희망봉")      +0x04  갈래 0~7
///   +0x08  발견물 일련번호(0~672)  — 힌트 표가 이 번호로 발견물을 가리킨다
///   +0x18  보수(닢)               +0x20  1이면 위치로는 안 잡힌다(유적 속 물건·인물·비보)
///   +0x24  1이면 처음부터 잡힌다   0이면 힌트를 얻어야 열린다(12 대발견·보물)
///   +0x28  0 = 바다에서, 1 = 뭍에서 발견
///   +0x2C  0이면 여러 사람이 거듭 발견할 수 있는 것(생물·교역품·도시 건물·지역)
///   +0x30  주는 아이템 번호(아이템 표 0x4FD558) — 274 중 210개가 준다
///   +0x34  <b>파는 도시 번호</b> — -1 이 아니면 그 도시 시장에 물건으로 나온다(모조품 28개)
///   +0x38  제 번호(= 줄 번호)
///   +0x44~0x50  세계지도 사각형 x1,y1,x2,y2 (칸) — 147개에만 있다
///   +0x54~0x5A  상륙지도 좌표 2x2 (u16 넷, 44개) — 아직 안 쓴다
/// </code>
/// 게임은 항해 루프를 한 번 돌 때마다 <c>0x0048D3F0</c> 에서 지금 칸을 이 사각형들과
/// 견준다. 자세한 차례는 <see cref="Engine.Discovery.DiscoveryLog"/> 에 적었다.
///
/// 이름과 사각형은 <see cref="Support.Local.Helpers.GameMapCoords"/> 에도 있지만 그쪽은
/// 지도에 점을 찍으려고 구워 둔 것이라 발견 규칙에 쓰는 칸(+0x20·+0x24·+0x28·+0x30)이
/// 없다. 판정은 이 표를 쓴다.
/// </remarks>
public sealed class DiscoveryTable
{
    /// <summary>적어 둘 파일 이름(<c>%APPDATA%\CdsHelper\exe-tables\발견물표.json</c>).</summary>
    private const string CacheName = "발견물표";

    /// <summary>알맹이 모양 판. 그림·동영상·파는 도시 칸을 더하면서 올렸다.</summary>
    private const int SnapshotVersion = 5;

    private const int TableVa = 0x0051C540;

    /// <summary>
    /// 발견했을 때 무엇을 보여 주는가 — <b>그림 아니면 동영상</b>이고 둘 다 없는 것도 있다.
    /// </summary>
    /// <remarks>
    /// <code>
    ///   +0x0C  DSTILL.CDS 그림 번호(0~83)   -1 이면 없다
    ///   +0x10  AVI 동영상 번호(0~69)        -1 이면 없다  →  AVI\I{번호:00}_0000.AVI
    ///   +0x14  DISCOVER.CDS 움직이는 그림(0~28) -1 이면 없다 — 보물 103~131 만 있다
    /// </code>
    /// 히랄다탑은 그림 69, 카르낙 거석군은 동영상 44 다. 동영상 파일이 <c>I00_0000.AVI</c>
    /// 부터 <c>I69_0000.AVI</c> 까지 일흔 개라 이 칸의 폭과 딱 맞는다.
    /// </remarks>
    public const string MovieFolder = "AVI";
    private const int RowSize = 92;

    /// <summary>
    /// 발견물을 <b>지웠을 때 깔 바탕 타일</b> 2×2 가 놓인 자리(<c>+0x54</c>).
    /// </summary>
    /// <remarks>
    /// 지도 렌더러(<c>0x0048A1E0</c>)가 도시와 똑같은 손으로 발견물도 가린다 —
    /// <c>0x00425720</c> 으로 그 칸의 발견물을 찾고, <c>0x004AADD0</c> 이 「이미 찾았나」를
    /// 본 뒤 아직이면 <c>0x004AAE90(dx, dy)</c> 이 낸 타일로 덮는다. 그 손이 짚는 자리가
    /// <c>[발견물*92 + 0x54 + dy*4 + dx*2]</c> 라 <b>2×2</b> 다 — 도시는 3×3 인데 이쪽은
    /// 렌더러가 <c>0x0048A45D</c> 에서 dx·dy 가 2 이상이면 아예 안 탄다.
    /// </remarks>
    private const int EraseOffset = 0x54;

    /// <summary>덧씌움 블록의 한 변과 「안 덮는다」 값.</summary>
    public const int EraseWidth = 2;
    public const ushort Keep = 0xFFFF;

    /// <summary>발견물 수.</summary>
    public const int Count = 274;

    /// <summary>판이 다른 EXE 를 잘못 읽지 않으려고 대 보는 줄.</summary>
    private const int ProbeId = 0;
    private const string ProbeName = "희망봉";

    /// <summary>갈래 이름. 표의 <c>+0x04</c> 가 이 차례를 가리킨다(힌트 갈래와 같은 표다).</summary>
    public static readonly string[] CategoryNames =
    [
        "지리", "역사", "보물", "종교", "교역품", "미신", "생물", "민족",
    ];

    /// <summary>사각형이 없는 발견물의 좌표 값.</summary>
    public const int NoPlace = -1;

    /// <summary>발견물 하나.</summary>
    /// <param name="Id">발견물 번호(0~273). 세이브의 줄 번호이기도 하다.</param>
    /// <param name="Hint">
    /// 발견물 일련번호. 힌트 표(<see cref="HintTable.Hint.Discovery"/>)가 이 번호로 발견물을
    /// 가리킨다 — 여럿이 같은 번호를 쓰기도 한다(기제의 피라미드와 스핑크스가 둘 다 113).
    /// </param>
    /// <param name="Reward">보수(닢).</param>
    /// <param name="ItemId">발견하면 주는 아이템 번호. 없으면 -1.</param>
    /// <param name="SoldIn">
    /// 이것을 <b>파는 도시</b> 번호(<c>+0x34</c>). -1 이면 어디서도 안 판다.
    /// </param>
    /// <param name="Indirect">
    /// 참이면 자리에 가도 잡히지 않는다 — 유적 속 물건·인물·비보처럼 다른 길로 얻는 것이다.
    /// 게임은 새 판을 열며 이런 줄의 깃발 <c>0x04</c> 를 지운다(<c>0x004AA97B</c>).
    /// </param>
    /// <param name="OpenAtStart">
    /// 참이면 처음부터 위치 판정에 걸린다. 거짓이면 <see cref="Hint"/> 를 가리키는 힌트를
    /// 얻어야 열린다(깃발 <c>0x08</c>) — 희망봉·신대륙 같은 12 대발견이 그렇다.
    /// </param>
    /// <param name="OnLand">참이면 뭍에서, 거짓이면 바다에서 발견된다.</param>
    /// <param name="Once">
    /// 거짓이면 게임에서 여러 사람이 거듭 발견할 수 있는 것이다(생물·교역품·도시 건물).
    /// 이 놀이에서는 한 판에 한 번으로 다룬다 — 원본도 한 판 안에서는 한 번만 뜬다.
    /// </param>
    /// <remarks>
    /// 레코드 <b>구조체</b>는 빈 생성자가 늘 있어서, 적어 둔 JSON 을 되읽을 때 어느 것을 쓸지
    /// 일러 주지 않으면 값이 전부 0 으로 들어온다.
    /// </remarks>
    [method: JsonConstructor]
    public readonly record struct Record(
        int Id, string Name, int Category, int Hint, int Reward, int ItemId,
        bool Indirect, bool OpenAtStart, bool OnLand, bool Once,
        int X1, int Y1, int X2, int Y2, int Picture = -1, int Movie = -1,
        ushort[]? Erase = null, int SoldIn = -1, int Clip = -1)
    {
        /// <summary>세계지도에 자리가 있는지. 없으면 다른 길로만 얻는다.</summary>
        [JsonIgnore] public bool HasPlace => X1 != NoPlace;

        /// <summary>갈래 이름. 모르는 번호면 빈 문자열.</summary>
        [JsonIgnore]
        public string CategoryName =>
            Category >= 0 && Category < CategoryNames.Length ? CategoryNames[Category] : "";

        /// <summary>주는 아이템이 있는지.</summary>
        [JsonIgnore] public bool GivesItem => ItemId >= 0;

        /// <summary>시장에서 살 수 있는 것인지(<c>+0x34</c>).</summary>
        [JsonIgnore] public bool OnSale => SoldIn >= 0 && ItemId >= 0;

        /// <summary>
        /// <b>모조품</b>인지 — 유적 번호(<see cref="Hint"/>)가 같은 진짜 유물의 싸구려 대역이다
        /// (「싸구려 도자기」·「유리해골」 따위).
        /// </summary>
        /// <remarks>
        /// EXE 발견물 표(<c>0x0051C540</c>, 274줄)를 유적 번호로 훑으면 227~130 번대 진짜
        /// 유물 스물여덟 자리가 <b>230~257번에 그대로 다시 나온다</b> — 이름만 「깨어진」·
        /// 「금도금」·「목조」·「장식용」·「싸구려」 따위로 바뀌고 유적 번호와 보수 구간은
        /// 진짜와 같은 자리에 묶인다. 번호 앞뒤(225~229 교역품, 258~ 동식물·인물)는 이 결에서
        /// 벗어나 번호 구간이 깔끔하게 갈린다 — 원본이 모조품 스물여덟 자리를 표 한 구석에
        /// 몰아 적어 둔 것으로 보인다(<see cref="Engine.Town.Palace.CounterfeitCaught"/> 참고).
        /// </remarks>
        [JsonIgnore] public bool IsCounterfeit => Id is >= 230 and <= 257;

        /// <summary>
        /// 좁을수록 앞세우려고 재는 값. 게임이 쓰는 것 그대로 <b>가로 길이의 제곱</b>이다
        /// (<c>0x004256D9</c>) — 세로는 안 본다.
        /// </summary>
        [JsonIgnore] public int Span => (X2 - X1) * (X2 - X1);

        /// <summary>그 칸이 이 발견물의 자리 안인지.</summary>
        public bool Covers(int cellX, int cellY) =>
            HasPlace && cellX >= X1 && cellX <= X2 && cellY >= Y1 && cellY <= Y2;
    }

    /// <summary>JSON 으로 적어 두는 알맹이.</summary>
    internal sealed record Snapshot(Record[] Discoveries);

    private readonly Record[] _rows;

    private DiscoveryTable(Snapshot snapshot) => _rows = snapshot.Discoveries;

    /// <summary>왜 못 읽었는지. 잘 열렸으면 빈 문자열.</summary>
    public static string LastError { get; private set; } = "";

    /// <summary>
    /// 표에 있는 발견물 전부 — 원본 274줄에 <see cref="DiscoveryEdits"/> 로 더하거나 갈아 끼운
    /// 것을 얹은 것. 색인이 곧 발견물 번호이던 것은 원본 274줄까지만 그렇다.
    /// </summary>
    public IReadOnlyList<Record> Discoveries
    {
        get
        {
            var edits = DiscoveryEdits.All;
            var merged = new List<Record>(_rows.Length + edits.Count);
            foreach (var row in _rows)
                merged.Add(Rename(edits.TryGetValue(row.Id, out var edited) ? edited : row));
            foreach (var (id, row) in edits)
                if (id < 0 || id >= _rows.Length) merged.Add(Rename(row));
            return merged;
        }
    }

    private static readonly Dictionary<int, string> Named = [];

    /// <summary>
    /// 놀이 안에서 <b>이름을 지어 준</b> 발견물(대본 <c>1F 0B</c> · <c>0x004AAB00</c>).
    /// </summary>
    /// <remarks>
    /// 게임은 발견물 레코드의 이름 칸을 그대로 갈아 버려서, 그 뒤로는 일람이든 지도든
    /// 어디서든 새 이름이 보인다. 우리도 표를 읽는 길목(<see cref="Find"/> ·
    /// <see cref="Discoveries"/>)에서 덧씌워 같은 꼴로 만든다. 판마다 다르므로 세이브에
    /// 적어 두었다가(<c>Player.NamedDiscoveries</c>) 판을 열 때 <see cref="ResetNames"/> 로 건다.
    /// </remarks>
    public static void SetName(int id, string name)
    {
        if (id < 0 || string.IsNullOrWhiteSpace(name)) return;
        Named[id] = name.Trim();
    }

    /// <summary>판을 열 때 이름을 통째로 갈아 끼운다. 안 주면 다 지운다.</summary>
    public static void ResetNames(IEnumerable<KeyValuePair<int, string>>? named)
    {
        Named.Clear();
        if (named == null) return;
        foreach (var (id, name) in named) SetName(id, name);
    }

    private static Record Rename(Record row) =>
        Named.TryGetValue(row.Id, out string? name) ? row with { Name = name } : row;

    /// <summary>그 번호의 발견물. 표 밖이면 null 이었지만, 더해 둔 것이 있으면 그것이 나온다.</summary>
    public Record? Find(int id)
    {
        if (DiscoveryEdits.Of(id) is { } edited) return Rename(edited);
        return id >= 0 && id < _rows.Length ? Rename(_rows[id]) : null;
    }

    /// <summary>
    /// 그 도시 시장에 <b>물건으로 나오는</b> 발견물(<c>0x004B0BA0</c>).
    /// </summary>
    /// <remarks>
    /// 게임은 구입 목록을 만들 때 이것들을 <b>여느 재고보다 앞에</b> 놓는다
    /// (<c>0x004B0AD0</c> 이 먼저, <c>0x004B0A80</c> 이 그 뒤다). 조건은 셋이다 —
    /// 아직 안 찾았고(깃발 <c>0x44</c> 가 꺼져 있고), 주는 아이템이 있고(<c>+0x30</c>),
    /// <c>+0x34</c> 가 이 도시다. 스물여덟 줄(<see cref="Record.IsCounterfeit"/>)이 걸린다.
    /// </remarks>
    public IEnumerable<Record> SoldAt(int cityId)
    {
        if (cityId < 0) yield break;
        foreach (var row in Discoveries)
            if (row.OnSale && row.SoldIn == cityId) yield return row;
    }

    /// <summary><see cref="DiscoveryEdits"/> 로 덧씌우기 전의 원본 게임 값. 표 밖이면 null.</summary>
    public Record? Original(int id) => id >= 0 && id < _rows.Length ? _rows[id] : null;

    /// <summary>그 번호의 이름. 표 밖이면 번호로 물러선다.</summary>
    public string NameOf(int id) => Find(id)?.Name ?? $"발견물 {id}";

    /// <summary>
    /// <b>일련번호</b>로 발견물을 찾는다 — 힌트가 가리키는 것을 찾을 때 쓴다.
    /// </summary>
    /// <remarks>
    /// 힌트 줄의 <see cref="HintTable.Hint.Discovery"/> 는 표에서 <b>몇째 줄</b>인지가 아니라
    /// <see cref="Record.Hint"/> 와 맞대어 볼 <b>일련번호</b>다(<c>0x004AACFD</c>). 줄 번호로
    /// 알고 <see cref="Find"/> 에 넣으면 엉뚱한 발견물이 나온다 — 「카르낙 거석군」(힌트 20,
    /// 일련번호 107)을 넣으면 표의 107째 줄인 「로제타석」이 나와, 술집이 브르타뉴가 아니라
    /// 중근동으로 가라고 일렀다.
    /// </remarks>
    public Record? FindBySerial(int serial)
    {
        if (serial < 0) return null;
        foreach (var row in Discoveries)
            if (row.Hint == serial) return row;
        return null;
    }

    /// <summary>
    /// 표를 연다. 적어 둔 JSON 이 있으면 그것을 읽고, 없거나 판이 갈렸으면 EXE 에서 읽어
    /// 적어 둔다. 둘 다 없을 때만 null 이다.
    /// </summary>
    public static DiscoveryTable? Open(string gameDirectory)
    {
        var snapshot = ExeTable.Open<Snapshot>(CacheName, gameDirectory, ReadFromExe,
                                              out string error, SnapshotVersion);
        LastError = error;
        return snapshot == null ? null : new DiscoveryTable(snapshot);
    }

    /// <summary>EXE 에서 발견물 줄을 통째로 읽어 낸다.</summary>
    private static Snapshot? ReadFromExe(PeImage exe, out string error)
    {
        error = "";

        var rows = new Record[Count];
        for (int k = 0; k < Count; k++)
        {
            int row = TableVa + k * RowSize;
            rows[k] = new Record(
                Id: k,
                Name: exe.Text(exe.Word(row + 0x00)) ?? "",
                Category: exe.Int(row + 0x04),
                Hint: exe.Int(row + 0x08),
                Reward: exe.Int(row + 0x18),
                ItemId: exe.Int(row + 0x30),
                Indirect: exe.Int(row + 0x20) != 0,
                OpenAtStart: exe.Int(row + 0x24) != 0,
                OnLand: exe.Int(row + 0x28) != 0,
                Once: exe.Int(row + 0x2C) != 0,
                X1: exe.Int(row + 0x44),
                Y1: exe.Int(row + 0x48),
                X2: exe.Int(row + 0x4C),
                Y2: exe.Int(row + 0x50),
                Picture: exe.Int(row + 0x0C),
                Movie: exe.Int(row + 0x10),
                SoldIn: exe.Int(row + 0x34),
                Clip: exe.Int(row + 0x14),
                // 아직 못 찾은 발견물을 지도에서 지울 때 깔 바탕 타일 2x2.
                Erase:
                [
                    (ushort)(exe.Word(row + EraseOffset) & 0xFFFF),
                    (ushort)(exe.Word(row + EraseOffset + 2) & 0xFFFF),
                    (ushort)(exe.Word(row + EraseOffset + 4) & 0xFFFF),
                    (ushort)(exe.Word(row + EraseOffset + 6) & 0xFFFF),
                ]);
        }

        if (rows[ProbeId].Name != ProbeName)
        {
            error = "발견물 표가 기대한 모양이 아닙니다(다른 판의 EXE 일 수 있습니다)";
            return null;
        }

        return new Snapshot(rows);
    }
}
