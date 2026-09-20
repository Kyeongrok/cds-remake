using System.IO;
using CdsHelper.Game.Local.Helpers;
using CdsHelper.Support.Local.Helpers;

namespace CdsHelper.Game.Engine.Discovery;

/// <summary>
/// 역사 항해자 열넷과 그들이 채가는 발견물 — <c>HISTCHR.CDS</c> 에서 읽는다.
/// </summary>
/// <remarks>
/// <b>이 놀이에서 경쟁자가 하는 일은 이것 하나뿐이다.</b> 게임은 인물 하나하나의 나날을
/// 돌리다가(<c>0x004327F0</c>) 인물 번호가 0~13 이면 그 사람의 대본을 튼다.
/// <code>
///   004329b6  eax = 0x004783C0(사람)          ; 인물 ID
///   004329c0  0x0040CF60(&amp;ctx, eax)          ; ctx->+0x10 = 그 사람  ★
///   004329d5  ecx = 0x0058BDD8 + n*20         ; HISTCHR 행 (14행 x 20바이트)
///   004329e8  0x0040CF70(&amp;커서, 행의 대본)     ; 그 대본을 돌린다
/// </code>
/// 대본이 <c>01 0B &lt;u16 발견물번호&gt;</c> 를 만나면 <c>0x004088CE</c> → <c>0x004AAC10</c>
/// 이 되는데, 사람의 <c>+4</c> 가 1(주인공이 아님)이라 발견물 인스턴스의 <b>사람 칸 1</b> 에
/// 그 이름이 들어간다. 주인공이 제 발견으로 같은 명령을 탈 때는 <c>+4</c> 가 0 이라
/// 칸 0 에 들어가고 깃발 <c>0x40</c> 이 함께 선다.
///
/// <b>먼저 가져가면 잠긴다.</b> <c>0x004AAC10</c> 첫 줄이 발견물 표 <c>+0x2C</c>
/// (<see cref="DiscoveryTable.Record.Once"/>)를 보고, 한 번짜리인데 칸 0·1 에 이미 이름이
/// 있으면 <b>아무것도 적지 않는다</b>. 그러면 깃발 <c>0x40</c> 이 안 서서 항구 발표 목록
/// (<c>0x00476DA0</c>)에도 안 뜨고 명성도 못 얻는다. 먼저 가는 쪽이 임자다.
///
/// 파일 짜임은 이렇다(파트 하나가 사람 하나다).
/// <code>
///   +0x00  u16 ?          +0x02  u16 칸 수 N
///   +0x04  N x (u16 조건 오프셋, u16 본문 오프셋)     ; 둘 다 +4 를 더해야 파일 자리다
///   조건 : 1C 17 &lt;달&gt; 16 &lt;u16 해&gt; FF
///   본문 : 명령들 — FF 로 끊는다
/// </code>
///
/// 본문에서 우리가 읽는 명령은 둘이다.
/// <code>
///   01 0B &lt;u16 발견물&gt;   그 발견물을 이 사람 이름으로 채간다
///   3C 08 &lt;u16 도시&gt;     이 사람을 그 도시로 <b>떠나보낸다</b>   (0x0040AB77)
///   3C 0B &lt;u16 발견물&gt;   이 사람을 그 발견물 자리로 보낸다       (0x0040AC82)
/// </code>
/// <b>사람은 피연산자가 아니다</b> — <c>3C</c> 는 대본의 주인을 옮긴다(<c>ctx+0x10</c>).
/// 둘 다 끝에서 목적지·출발좌표를 박고 <c>날 셈 = 0</c>, <c>[인물+0xB4] = -1</c> 로 도시에서
/// 빼낸다. 그래서 <b>바다 위에서 역사 항해자와 마주친다</b> — 지도에 사람을 띄우는
/// <c>0x00426790</c> 은 281명을 통째로 훑으면서 「지금 자리가 있는가」만 본다.
/// </remarks>
public sealed class HistoryVoyages
{
    /// <summary>대본이 든 파일.</summary>
    public const string FileName = "HISTCHR.CDS";

    /// <summary>구워 둘 파일 이름(<c>%APPDATA%\CdsHelper\exe-tables\역사항해자.json</c>).</summary>
    public const string CacheName = "역사항해자";

    /// <summary>알맹이 모양 판.</summary>
    private const int SnapshotVersion = 2;

    /// <summary>역사 항해자 수. 파일 파트 수이자 인물 번호 0~13 이다.</summary>
    public const int Count = 14;

    /// <summary>
    /// 열넷의 이름. EXE 의 <c>0x005498E0</c>~<c>0x005499DC</c> 에 성·이름이 짝지어 있고,
    /// 차례는 <c>HISTCHR.CDS</c> 의 파트 차례와 같다.
    /// </summary>
    /// <remarks>
    /// 파일에는 이름이 없어 여기 적어 둔다. 짝이 맞는지는 대본의 날짜로 확인했다 —
    /// 0번이 1488년 1월 희망봉(디아스), 5번이 1492년 10월 신대륙(코론), 3번이 1498년 5월
    /// 인도(다·가마), 8번이 1520년 10월 마젤란해협, 10번이 1522년 9월 세계일주항로(엘카노),
    /// 12번이 1497년 6월 신대륙(캐벗)이다.
    /// </remarks>
    public static readonly string[] Names =
    [
        "바르톨로메우·디아스", "페드로·알바레스·카브랄", "프란시스코·데·아르메이다",
        "바스코·다·가마", "아퐁소·데·알브켈케", "크리스트발·코론",
        "아메리고·베스풋치", "프란시스코·피사로", "페르난도·데·마가랴네스",
        "에르난·콜테스", "세바스찬·데·엘카노", "잭·칼티에",
        "존·캐벗", "하산·분·무하마드",
    ];

    /// <summary>누가 언제 무엇을 가져가는지 한 줄.</summary>
    /// <param name="Voyager">역사 항해자 번호(0~13). 이름은 <see cref="Names"/>.</param>
    /// <param name="Year">그 해.</param>
    /// <param name="Month">그 달.</param>
    /// <param name="Discovery">발견물 번호.</param>
    public readonly record struct Voyage(int Voyager, int Year, int Month, int Discovery)
    {
        /// <summary>그 달 <b>1일</b>. 날짜를 견줄 때 쓴다 — 대본에 날은 없다.</summary>
        [System.Text.Json.Serialization.JsonIgnore]
        public DateTime On => new(Year, Month, 1);
    }

    /// <summary>JSON 으로 구워 두는 알맹이.</summary>
    /// <summary>
    /// 대본이 사람을 옮기는 한 수.
    /// </summary>
    /// <param name="City">가는 도시. <c>3C 08</c> 이 아니면 −1.</param>
    /// <param name="Discovery">가는 발견물 자리. <c>3C 0B</c> 이 아니면 −1.</param>
    public readonly record struct Move(int Voyager, int Year, int Month, int City, int Discovery)
    {
        /// <summary>대본이 도는 날 — 달의 첫날이다.</summary>
        public DateTime On => new(Year, Month, 1);

        /// <summary>도시로 가는 수인가.</summary>
        public bool ToCity => City >= 0;
    }

    internal sealed record Snapshot(List<Voyage> Voyages, List<Move>? Moves = null);

    /// <summary>사람 차례, 그 다음 날짜 차례.</summary>
    /// <summary>사람 차례 · 날짜 차례로 세우는 견줌.</summary>
    public static readonly Comparison<Move> MoveOrder = (a, b) =>
        a.Voyager != b.Voyager ? a.Voyager - b.Voyager
        : a.Year != b.Year ? a.Year - b.Year
        : a.Month != b.Month ? a.Month - b.Month
        : a.City != b.City ? a.City - b.City : a.Discovery - b.Discovery;

    public static readonly Comparison<Voyage> ByVoyagerThenDate =
        (a, b) => a.Voyager != b.Voyager ? a.Voyager - b.Voyager : a.On.CompareTo(b.On);

    private readonly List<Voyage> _original;
    private List<Voyage> _voyages;
    private readonly List<Move> _moves;
    private int _stamp = -1;

    private HistoryVoyages(List<Voyage> original, List<Move> moves)
    {
        _moves = moves;
        _original = original;
        _voyages = original;
    }

    /// <summary>왜 못 읽었는지. 잘 열렸으면 빈 문자열.</summary>
    public static string LastError { get; private set; } = "";

    /// <summary>
    /// 채가는 것 전부 — <b>손으로 고친 것이 얹힌 뒤</b>다. 사람 차례 · 날짜 차례.
    /// </summary>
    /// <remarks>
    /// 고침이 바뀌었을 때에만 다시 얹는다(<see cref="VoyagerEdits.Revision"/>). 그래서
    /// <b>편집 창에서 고치면 놀이가 돌아가는 중에도 곧바로 든다</b> — 표를 다시 열 것 없다.
    /// </remarks>
    public IReadOnlyList<Voyage> All
    {
        get
        {
            if (_stamp == VoyagerEdits.Revision) return _voyages;
            _stamp = VoyagerEdits.Revision;
            return _voyages = VoyagerEdits.Apply(_original);
        }
    }

    /// <summary>파일에서 읽은 그대로. 고친 창이 견줄 밑값이다.</summary>
    public IReadOnlyList<Voyage> Original => _original;

    /// <summary>
    /// 대본이 사람을 옮기는 수 전부. 사람 차례 · 날짜 차례다.
    /// </summary>
    /// <remarks>
    /// 채가는 것과 달리 <b>손으로 고치는 길이 없다</b> — 자리를 옮기는 것뿐이라 판을
    /// 흔들 일이 없고, 고칠 데를 늘리면 <see cref="VoyagerEdits"/> 가 지고 있는 짐만
    /// 커진다. <see cref="PersonWorld"/> 가 매월 1일에 이 목록을 본다.
    /// </remarks>
    public IReadOnlyList<Move> Moves => _moves;

    /// <summary>그 사람이 그 달에 떠나는 수들. 없으면 빈 목록.</summary>
    public IEnumerable<Move> MovesOn(int voyager, int year, int month)
    {
        foreach (var move in _moves)
            if (move.Voyager == voyager && move.Year == year && move.Month == month)
                yield return move;
    }

    /// <summary>
    /// 게임 폴더에서 읽는다. 못 읽으면 구워 둔 JSON 으로 물러서고, 그것도 없으면 null.
    /// </summary>
    /// <remarks>
    /// 읽어 낸 것은 <c>역사항해자.json</c> 으로 구워 둔다 — 사람이 눈으로 보고, 게임 폴더를
    /// 못 찾는 자리(세이브 편집기 쪽)에서도 표를 쓸 수 있게 하려는 것이다.
    /// 손으로 고친 것은 <see cref="VoyagerEdits"/> 가 따로 들고 여기서 얹는다.
    /// </remarks>
    public static HistoryVoyages? Open(string gameDirectory)
    {
        LastError = "";

        var moves = new List<Move>();
        var voyages = FromFile(gameDirectory, moves);
        if (voyages != null)
            TableCache.Write(CacheName, new TableCache.Cached<Snapshot>(
                $"{Count}명 {voyages.Count}건 · 이동 {moves.Count}수",
                new Snapshot(voyages, moves), FileName, SnapshotVersion));
        else if (TableCache.Read<Snapshot>(CacheName)?.Data is { } kept)
        {
            voyages = kept.Voyages;
            moves = kept.Moves ?? [];
        }

        if (voyages == null || voyages.Count == 0)
        {
            if (LastError.Length == 0) LastError = $"{FileName} 도 구워 둔 표도 없습니다";
            return null;
        }

        LastError = "";
        return new HistoryVoyages(voyages, moves);
    }

    /// <summary><c>HISTCHR.CDS</c> 에서 읽어 낸다. 못 읽으면 null 이고 까닭이 남는다.</summary>
    private static List<Voyage>? FromFile(string gameDirectory, List<Move> moves)
    {
        if (gameDirectory.Length == 0) { LastError = "게임 폴더를 모릅니다"; return null; }

        string path = Path.Combine(gameDirectory, FileName);
        var archive = Ls12Reader.Open(path);
        if (archive == null) { LastError = $"{path} 를 읽지 못했습니다"; return null; }
        if (archive.PartCount < Count)
        {
            LastError = $"{FileName} 에 파트가 {archive.PartCount}개뿐입니다";
            return null;
        }

        var voyages = new List<Voyage>();
        for (int who = 0; who < Count; who++)
            if (archive.Decode(who) is { } part) Read(who, part, voyages, moves);

        if (voyages.Count == 0) { LastError = $"{FileName} 에서 발견 기록을 못 찾았습니다"; return null; }

        voyages.Sort(ByVoyagerThenDate);
        moves.Sort(MoveOrder);
        return voyages;
    }

    /// <summary>
    /// 그 날까지 역사가 <b>가져가 버린</b> 발견물인가. 가져갔으면 그 사람 번호, 아니면 -1.
    /// </summary>
    /// <remarks>
    /// 게임은 이것을 발견물 인스턴스의 사람 칸 1 로 들고 있지만, 우리는 들고 있지 않아도
    /// 된다 — 대본이 날짜로 못박아 두어 <b>날짜만 알면 답이 정해지기</b> 때문이다.
    /// 내가 먼저 찾았으면 부르는 쪽이 이미 걸러 낸다.
    /// </remarks>
    public int TakenBy(int discovery, DateTime date)
    {
        foreach (var voyage in All)
            if (voyage.Discovery == discovery && voyage.On <= date) return voyage.Voyager;
        return -1;
    }

    /// <summary>그 사람 이름. 번호가 표 밖이면 빈 문자열.</summary>
    public static string NameOf(int voyager) =>
        voyager >= 0 && voyager < Names.Length ? Names[voyager] : "";

    /// <summary>대본 하나에서 날짜 붙은 발견 기록과 이동을 뽑는다.</summary>
    private static void Read(int who, byte[] part, List<Voyage> into, List<Move> moves)
    {
        if (part.Length < 4) return;

        int slots = U16(part, 2);
        if (slots <= 0 || 4 + slots * 4 > part.Length) return;

        // 칸마다 (조건, 본문) 두 자리다. 오프셋은 커서가 서는 자리(파일 +4)를 0 으로 센다
        // (0x0040CF70 이 커서를 데이터 + 4 에 놓는다).
        var blocks = new List<(int Body, int Year, int Month)>();
        for (int i = 0; i < slots; i++)
        {
            int at = 4 + i * 4;
            int cond = U16(part, at) + 4;
            int body = U16(part, at + 2) + 4;
            if (cond + 6 >= part.Length || body >= part.Length) continue;

            // 1C 17 <달> 16 <u16 해> FF
            if (part[cond] != 0x1C || part[cond + 1] != 0x17 || part[cond + 3] != 0x16) continue;

            int month = part[cond + 2];
            int year = U16(part, cond + 4);
            if (month is < 1 or > 12 || year is < 1400 or > 1700) continue;

            blocks.Add((body, year, month));
        }
        if (blocks.Count == 0) return;
        blocks.Sort((a, b) => a.Body - b.Body);

        for (int i = 0; i + 3 < part.Length; i++)
        {
            if (part[i] != 0x01 || part[i + 1] != 0x0B) continue;

            int id = U16(part, i + 2);
            if (id >= DiscoveryTable.Count) continue;      // 글 속의 우연한 두 바이트를 거른다

            // 그 자리를 품은 본문 — 시작이 이 자리보다 앞인 것 중 가장 뒤엣것이다.
            int at = -1;
            for (int k = 0; k < blocks.Count && blocks[k].Body <= i; k++) at = k;
            if (at < 0) continue;

            into.Add(new Voyage(who, blocks[at].Year, blocks[at].Month, id));
        }

        // 3C 08 <도시> · 3C 0B <발견물> — 대본의 주인을 옮긴다.
        for (int i = 0; i + 3 < part.Length; i++)
        {
            if (part[i] != 0x3C) continue;

            int sub = part[i + 1], arg = U16(part, i + 2);
            bool city = sub == 0x08 && arg < CityExeTable.Count;
            bool spot = sub == 0x0B && arg < DiscoveryTable.Count;
            if (!city && !spot) continue;               // 글 속의 우연한 두 바이트를 거른다

            int at = -1;
            for (int k = 0; k < blocks.Count && blocks[k].Body <= i; k++) at = k;
            if (at < 0) continue;

            // 대본이 같은 수를 잇달아 두 번 적어 둔 데가 많다 — 한 번만 담는다.
            var move = new Move(who, blocks[at].Year, blocks[at].Month,
                                city ? arg : -1, spot ? arg : -1);
            if (!moves.Contains(move)) moves.Add(move);
        }
    }

    private static int U16(byte[] data, int at) => data[at] | (data[at + 1] << 8);
}
