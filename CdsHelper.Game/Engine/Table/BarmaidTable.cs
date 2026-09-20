using System.Text.Json.Serialization;

namespace CdsHelper.Game.Local.Helpers;

/// <summary>
/// CDS_95.EXE 안의 <b>여급 표</b> — 마을 술집에 서는 127명.
/// </summary>
/// <remarks>
/// <code>
///   표 VA 0x00517AF8, 127행 x 40바이트 (.rdata)
///   +0x00  이름 ptr("카를로타")      +0x04  얼굴 번호(FEMALE.CDS)
///   +0x08  등장년도 = 1495 - 값      +0x0C  별자리(0~11)
///   +0x10  혈액형(0 A · 1 B · 2 O · 3 AB)
///   +0x14  운명 얼굴 코드(0~30)      ← 궁합이 이 값 하나로 갈린다
///   +0x18  성격(0~7)                +0x1C  늘 4
///   +0x20  전수 언어 비트           +0x24  도시 번호
/// </code>
///
/// <b>궁합은 얼굴 코드다.</b> 게임은 <c>0x00465E70</c> 에서 이렇게 본다.
/// <code>
///   내 코드   = 0x0047CB10() = [0x005B60A0 + 8]     ; 주인공의 표시 얼굴 코드
///   그녀 코드 = [0x00517B0C + 번호 * 40]             ; 곧 이 표의 +0x14
///   같거나 ±1 이면 맞는다
/// </code>
/// 주인공 코드는 <b>서른여섯 살부터 16 을 더한다</b> — 초상화가 젊은 벌과 나이 든 벌로
/// 갈리고 그 사이가 16 이다. (이 두 가지는 <c>cds_save_editor</c> 의 여급 도감이 밝혀
/// 둔 것이고, 표 자리와 ±1 은 EXE 에서 되짚었다.)
///
/// 궁합이 맞으면 <b>첫 대화부터 말투가 다르고 친밀도가 50 오른다</b>(<c>0x00466730</c>).
/// 안 맞으면 3 이다 — 열일곱 배다.
/// </remarks>
public sealed class BarmaidTable
{
    /// <summary>적어 둘 파일 이름(<c>%APPDATA%\CdsHelper\exe-tables\여급표.json</c>).</summary>
    private const string CacheName = "여급표";

    private const int TableVa = 0x00517AF8;
    private const int RowSize = 40;

    /// <summary>여급 수.</summary>
    public const int Count = 127;

    /// <summary>
    /// 서른여섯 살부터 주인공 얼굴 코드에 더하는 값 — 게임 값이다.
    /// </summary>
    /// <remarks>
    /// 코드 공간의 짜임은 <see cref="FortuneCodes"/> 에 적어 두었다 — 0~15 가 젊은 제독,
    /// 16~31 이 그 중년 몫이라 <b>한 덩어리</b>다.
    /// </remarks>
    public const int AgedFaceStep = 16;

    /// <summary>그 나이부터 나이 든 얼굴로 친다.</summary>
    public const int AgedFrom = 36;

    /// <summary>궁합으로 쳐 주는 코드 차이.</summary>
    public const int DestinedGap = 1;

    /// <summary>등장년도를 내는 밑값 — <c>1495 - 표값</c> 이다.</summary>
    /// <remarks>
    /// 놀이가 시작하는 1480 년 아래로는 안 내려간다. 127명 가운데 <b>한 명</b>만
    /// 편집기 도감과 한 해 어긋나는데(94번), 나머지 126명이 맞으니 그쪽 오기로 본다.
    /// </remarks>
    public const int YearBase = 1495, FirstYear = 1480;

    /// <summary>궁합이 맞을 때와 아닐 때 첫 대화가 올리는 친밀도(<c>0x00466768</c> · <c>0x0046679A</c>).</summary>
    public const int DestinedLike = 50, PlainLike = 3;

    /// <summary>친밀도의 위아래(<c>0x00478530</c> 이 0~100 으로 자른다).</summary>
    public const int MaxLiking = 100;

    /// <summary>판이 다른 EXE 를 잘못 읽지 않으려고 대 보는 줄 — 첫 사람.</summary>
    private const string Probe = "카를로타";

    /// <summary>성격 이름(<c>+0x18</c> 의 색인).</summary>
    /// <remarks>
    /// <c>0x004A2F10</c> 이 내주는 여덟 낱말 그대로다 — 성미 칸이 <b>2</b> 쪽일 때의 말이다.
    /// 표 밖이면 <c>0x0054C878</c> 「친절한」이다.
    /// </remarks>
    public static readonly string[] Personalities =
        ["당당한", "강인한", "의지가 강한", "용감한", "친절한", "로맨틱한", "섬세한", "견실한"];

    /// <summary>혈액형 이름(<c>+0x10</c> 의 색인).</summary>
    public static readonly string[] Bloods = ["A형", "B형", "O형", "AB형"];

    /// <summary>여급 한 사람.</summary>
    /// <param name="Face">FEMALE.CDS 의 얼굴 번호.</param>
    /// <param name="Fortune">운명 얼굴 코드 — 궁합이 이 값으로 갈린다.</param>
    /// <param name="Tongues">가르쳐 주는 언어 비트.</param>
    [method: JsonConstructor]
    /// <param name="Year">이 해부터 그 술집에 선다.</param>
    public readonly record struct Barmaid(
        int Id, string Name, int City, int Face, int Zodiac, int Blood,
        int Fortune, int Personality, int Tongues, int Year)
    {
        /// <summary>성격 이름. 표 밖이면 빈 문자열.</summary>
        [JsonIgnore]
        public string PersonalityName =>
            Personality >= 0 && Personality < Personalities.Length ? Personalities[Personality] : "";

        /// <summary>혈액형 이름. 표 밖이면 빈 문자열.</summary>
        [JsonIgnore]
        public string BloodName => Blood >= 0 && Blood < Bloods.Length ? Bloods[Blood] : "";
    }

    /// <summary>JSON 으로 적어 두는 알맹이.</summary>
    internal sealed record Snapshot(List<Barmaid> Barmaids);

    private readonly List<Barmaid> _rows;

    private BarmaidTable(Snapshot snapshot) => _rows = snapshot.Barmaids;

    /// <summary>
    /// 여급 전부. 운명 코드는 <b>지금 걸음</b>으로 옮겨 낸다.
    /// </summary>
    /// <remarks>
    /// 적어 둔 표에는 EXE 값이 그대로 있고, 사람이 고쳐 둔 코드만
    /// <see cref="BarmaidEdits"/> 가 읽을 때 얹는다 — 표를 다시 굽지 않아도 되고,
    /// 되돌리면 값도 저절로 돌아온다.
    /// </remarks>
    public IReadOnlyList<Barmaid> Barmaids =>
        BarmaidEdits.All.Count == 0
            ? _rows
            : [.. _rows.Select(r => r with { Fortune = CodeOf(r) })];

    /// <summary>그 여급의 궁합 코드. 사람이 고쳐 둔 것이 있으면 그것이 이긴다.</summary>
    private static int CodeOf(Barmaid her)
    {
        int mine = BarmaidEdits.Of(her.Id);
        return mine >= 0 ? mine : her.Fortune;
    }

    /// <summary>왜 못 읽었는지. 잘 열렸으면 빈 문자열.</summary>
    public static string LastError { get; private set; } = "";

    /// <summary>그 사람. 없으면 null.</summary>
    public Barmaid? Find(int id) =>
        id >= 0 && id < _rows.Count ? Barmaids[id] : null;

    /// <summary>그 마을 술집에 서는 사람들.</summary>
    public List<Barmaid> InCity(int cityId) => [.. Barmaids.Where(b => b.City == cityId)];

    /// <summary>
    /// 그 마을 술집에 <b>지금</b> 서 있는 여급. 없으면 null.
    /// </summary>
    /// <remarks>
    /// 한 마을에 여럿이 적혀 있는데 등장년도가 다르다 — 리스본은 알다(1480) · 카를로타
    /// (1498) · 루치아(1522) 셋이다. 해가 갈수록 뒷사람으로 갈리므로 <b>나온 사람 가운데
    /// 가장 늦게 나온 이</b>를 세운다.
    /// </remarks>
    public Barmaid? Standing(int cityId, int year)
    {
        Barmaid? found = null;
        foreach (var b in Barmaids)
        {
            if (b.City != cityId || b.Year > year) continue;
            if (found is not { } had || b.Year >= had.Year) found = b;
        }
        return found;
    }

    /// <summary>
    /// 그 나이의 주인공이 내는 <b>표시 얼굴 코드</b>. 서른여섯부터 열여섯이 더 붙는다.
    /// </summary>
    /// <param name="fortune">
    /// 주인공의 운명 코드(0~15). <b>초상화 번호가 아니다</b> — 게임은 이것을 주인공 객체의
    /// <c>+0x08</c> 에 따로 들고 있고, 우리도 <c>Player.Fortune</c> 에 따로 적어 둔다.
    /// </param>
    /// <param name="age">지금 나이.</param>
    public static int FortuneOf(int fortune, int age) =>
        FortuneCodes.CodeOf(fortune, age >= AgedFrom);

    /// <summary>
    /// 궁합이 맞는지 — 두 코드가 같거나 하나 차이일 때다(<c>0x00465E90</c>).
    /// </summary>
    /// <remarks>
    /// 여급 도감은 "일치" 만 운명의 반려자로 적어 두었는데, 코드를 보면 <b>±1 도 맞는 것</b>으로
    /// 친다. 그 자리가 <c>je</c> 셋(같음 · -1 · 1)이라 에누리가 없다.
    /// </remarks>
    public static bool Destined(int playerFortune, int barmaidFortune) =>
        Math.Abs(playerFortune - barmaidFortune) <= DestinedGap;

    /// <summary>첫 대화가 올리는 친밀도.</summary>
    public static int LikingGain(bool destined) => destined ? DestinedLike : PlainLike;

    /// <summary>표를 연다. 적어 둔 JSON 이 있으면 그것을 읽는다.</summary>
    public static BarmaidTable? Open(string gameDirectory)
    {
        var snapshot = ExeTable.Open<Snapshot>(CacheName, gameDirectory, ReadFromExe, out string error);
        LastError = error;
        return snapshot == null ? null : new BarmaidTable(snapshot);
    }

    private static Snapshot? ReadFromExe(PeImage exe, out string error)
    {
        error = "";

        var rows = new List<Barmaid>(Count);
        for (int i = 0; i < Count; i++)
        {
            int at = TableVa + i * RowSize;
            string? name = exe.Text(exe.Word(at + 0x00));
            if (name == null) { error = "여급 표에서 이름을 못 읽었습니다"; return null; }

            rows.Add(new Barmaid(i, name,
                                 City: exe.Int(at + 0x24),
                                 Face: exe.Int(at + 0x04),
                                 Zodiac: exe.Int(at + 0x0C),
                                 Blood: exe.Int(at + 0x10),
                                 Fortune: exe.Int(at + 0x14),
                                 Personality: exe.Int(at + 0x18),
                                 Tongues: exe.Int(at + 0x20),
                                 Year: Math.Max(FirstYear, YearBase - exe.Int(at + 0x08))));
        }

        if (rows[0].Name != Probe)
        {
            error = "여급 표가 기대한 모양이 아닙니다(다른 판의 EXE 일 수 있습니다)";
            return null;
        }
        return new Snapshot(rows);
    }
}
