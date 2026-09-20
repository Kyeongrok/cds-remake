using System.Text.Json.Serialization;

namespace CdsHelper.Game.Local.Helpers;

/// <summary>
/// CDS_95.EXE 안의 <b>주인공 이름 일람</b> — 신규 캐릭터 창의 "일람" 이 여는 목록이다.
/// </summary>
/// <remarks>
/// 성과 명이 표 둘로 갈려 있고 <b>둘 다 EXE 에 정적으로 박혀</b> 있다(파일에서 안 읽는다).
/// <code>
///   0x0057B208   성  48개            꺼내기 0x004ABA30(i)      i 가 0x30 밖이면 빈 문자열
///   0x0057B2C8   명  37행 x 2열      꺼내기 0x004ABA50(i, 국적)
/// </code>
/// <b>명은 국적마다 표기가 갈린다.</b> 둘째 열이 비어 있으면 첫 열을 쓴다
/// (<c>0x004ABA76</c> 가 null 을 보고 <c>[i*8]</c> 로 물러선다).
/// <code>
///   조안 / 후안 · 디오고 / 디에고 · 자이메 / 하이메 · 죠제 / 호세 · 미구알 / 미구엘
/// </code>
/// 서른일곱 가운데 여덟만 갈리고 나머지는 한 가지다.
///
/// 목록을 세우는 자리가 <c>0x0045C9C8</c> 인데 <c>0x004550B0(0x25)</c> 로 <b>서른일곱</b>
/// 칸을 잡는다 — 명 쪽 줄 수다. 성 쪽은 <c>0x004ABA30</c> 이 <c>0x30</c>(마흔여덟)에서
/// 자른다.
///
/// <b>후원자 이름을 갈라 쓰던 것을 이것으로 바꿨다.</b> 예전에는 이 표를 못 짚어
/// <see cref="SponsorTable"/> 의 여든한 명을 가운뎃점에서 갈라 명·성 목록으로 삼았는데,
/// 그러면 목록이 원본보다 훨씬 길고 사람도 다르다.
/// </remarks>
public sealed class PlayerNameTable
{
    /// <summary>적어 둘 파일 이름(<c>%APPDATA%\CdsHelper\exe-tables\이름표.json</c>).</summary>
    private const string CacheName = "이름표";

    private const int SnapshotVersion = 1;

    private const int FamilyVa = 0x0057B208, GivenVa = 0x0057B2C8;

    /// <summary>성 수와 명 줄 수. 게임이 자르는 값 그대로다(<c>0x30</c> · <c>0x25</c>).</summary>
    public const int FamilyCount = 48, GivenCount = 37;

    /// <summary>
    /// 여자 이름 열여섯(<c>0x0057B3F0</c>) — 딸이 태어나면 여기서 뽑는다(<c>0x004ABCC0</c>).
    /// </summary>
    /// <remarks>주인공은 남자뿐이라 신규 캐릭터 창에는 안 뜨고, 아이 이름에만 쓰인다.</remarks>
    public static readonly string[] Girls =
    [
        "안나", "카롤리나", "칼로타", "카타리나", "크리스티나", "도나", "프란시스카", "헬레나",
        "이자벨", "조안나", "루이자", "마르가리타", "마리아", "마틸다", "테레사", "펠리파",
    ];

    /// <summary>명 표의 열 수 — 국적 둘이다(0 포르투갈 · 1 에스파니아).</summary>
    public const int Nations = 2;

    internal sealed record Snapshot(string[] Family, string[] Given);

    private readonly string[] _family;

    /// <summary>명. <c>[줄 * <see cref="Nations"/> + 국적]</c> 으로 담았다.</summary>
    private readonly string[] _given;

    private PlayerNameTable(Snapshot snapshot)
    {
        _family = snapshot.Family;
        _given = snapshot.Given;
    }

    /// <summary>왜 못 열었는지. 잘 열렸으면 빈 문자열.</summary>
    public static string LastError { get; private set; } = "";

    /// <summary>성 마흔여덟. 표에 적힌 차례 그대로다.</summary>
    public IReadOnlyList<string> Family => _family;

    /// <summary>그 국적의 명 서른일곱. 그 국적 표기가 없으면 첫 열 것이다.</summary>
    public IReadOnlyList<string> GivenFor(int nation)
    {
        int column = nation >= 0 && nation < Nations ? nation : 0;
        var names = new string[GivenCount];
        for (int i = 0; i < GivenCount; i++)
        {
            string own = _given[i * Nations + column];
            names[i] = own.Length > 0 ? own : _given[i * Nations];
        }
        return names;
    }

    /// <summary>표를 연다. 한 번 읽으면 JSON 으로 적어 두고 다음부터는 그것을 쓴다.</summary>
    public static PlayerNameTable? Open(string gameDirectory)
    {
        var snapshot = ExeTable.Open<Snapshot>(CacheName, gameDirectory, ReadFromExe,
                                               out string error, SnapshotVersion);
        LastError = error;
        return snapshot == null ? null : new PlayerNameTable(snapshot);
    }

    private static Snapshot? ReadFromExe(PeImage exe, out string error)
    {
        error = "";

        var family = new string[FamilyCount];
        for (int i = 0; i < FamilyCount; i++)
            family[i] = exe.Text(exe.Word(FamilyVa + i * 4)) ?? "";

        var given = new string[GivenCount * Nations];
        for (int i = 0; i < GivenCount; i++)
            for (int c = 0; c < Nations; c++)
            {
                uint at = exe.Word(GivenVa + (i * Nations + c) * 4);
                given[i * Nations + c] = at == 0 ? "" : exe.Text(at) ?? "";
            }

        // 판이 다른 EXE 를 잘못 읽지 않으려고 대 본다 — 첫 성과 첫 명은 어느 판에서나 같다.
        if (family[0] != "아르바레스" || given[0] != "아퐁소")
        {
            error = "이름표가 기대한 모양이 아닙니다(다른 판의 EXE 일 수 있습니다)";
            return null;
        }

        return new Snapshot(family, given);
    }
}
