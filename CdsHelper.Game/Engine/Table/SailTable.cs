namespace CdsHelper.Game.Local.Helpers;

/// <summary>
/// CDS_95.EXE 안의 <b>돛 효율표</b> — 돛 조합 스물일곱 x 상대각 열여섯.
/// </summary>
/// <remarks>
/// <code>
///   표 VA 0x00569800, int32 [27][16] (1728바이트)
/// </code>
/// 색인은 <b>돛대 셋을 3진수로 붙인 것</b>이다. 배 레코드 <c>+0x68</c> 워드의 아래 여섯
/// 비트가 돛대 셋이고, 값은 <c>0 없음 · 1 삼각돛 · 2 사각돛</c> 이다.
/// <code>
///   a = (돛 &gt;&gt; 4) &amp; 3      ; 선미마스트
///   b = (돛 &gt;&gt; 2) &amp; 3      ; 세브마스트
///   c =  돛        &amp; 3      ; 메인마스트
///   조합 = 9a + 3b + c                    (0~26)
///   효율 = 표[조합 * 16 + 상대각]
/// </code>
/// 상대각은 <c>(풍향 - 뱃머리) &amp; 15</c> 다 — <b>0 이 정순풍, 8 이 정면 역풍</b>.
///
/// 표를 읽으면 두 가지가 눈에 띈다.
/// <list type="bullet">
/// <item>꼭대기가 <c>rel=0</c> 이 아니라 <b>2~3</b> 이다. 진짜 범선이 데드런보다
///       브로드리치가 빠른 것을 그대로 넣었다.</item>
/// <item><b>사각돛은 역풍(6~10)에서 0</b> 이라 아예 못 나가고, <b>삼각돛은 정면 역풍에서도
///       1</b> 은 낸다. 조선소 대사가 말한 그대로다. 대신 순풍 꼭대기는 사각 셋(15)이
///       삼각 셋(8)의 두 배다.</item>
/// </list>
/// 돛대는 뒤에서부터 채우므로 있을 수 없는 조합(메인이 비었는데 뒤가 찬 것)은 줄이 통째로
/// 0 이다.
///
/// 자세한 것은 볼트 <c>30.분석-항해 속도(돛·바람·해류)</c>.
/// </remarks>
public sealed class SailTable
{
    /// <summary>적어 둘 파일 이름(<c>%APPDATA%\CdsHelper\exe-tables\돛효율표.json</c>).</summary>
    private const string CacheName = "돛효율표";

    private const int TableVa = 0x00569800;

    /// <summary>돛 조합 수(3의 세제곱).</summary>
    public const int Combos = 27;

    /// <summary>상대각 수(16방위).</summary>
    public const int Angles = 16;

    /// <summary>판이 다른 EXE 를 잘못 읽지 않으려고 대 보는 칸 — 사각돛 셋의 순풍 꼭대기.</summary>
    private const int ProbeCombo = 26, ProbeAngle = 2, ProbeValue = 15;

    internal sealed record Snapshot(int[] Efficiency);

    private readonly int[] _table;

    private SailTable(Snapshot snapshot) => _table = snapshot.Efficiency;

    /// <summary>못 열었을 때의 까닭. 열렸으면 빈 문자열.</summary>
    public static string LastError { get; private set; } = "";

    /// <summary>표를 연다. 한 번 읽으면 JSON 으로 적어 두고 다음부터는 그것을 쓴다.</summary>
    public static SailTable? Open(string gameDirectory)
    {
        var snapshot = ExeTable.Open<Snapshot>(CacheName, gameDirectory, ReadFromExe, out string error);
        LastError = error;
        return snapshot == null ? null : new SailTable(snapshot);
    }

    private static Snapshot? ReadFromExe(PeImage exe, out string error)
    {
        error = "";

        var table = new int[Combos * Angles];
        for (int i = 0; i < table.Length; i++) table[i] = exe.Int(TableVa + i * 4);

        if (table[ProbeCombo * Angles + ProbeAngle] != ProbeValue)
        {
            error = "돛 효율표가 기대한 모양이 아닙니다(다른 판의 EXE 일 수 있습니다)";
            return null;
        }
        return new Snapshot(table);
    }

    /// <summary>
    /// 돛대 셋을 표의 줄 번호로. <c>9a + 3b + c</c> 다.
    /// </summary>
    /// <param name="sails">메인·세브·선미 차례의 돛 번호(0 없음 · 1 삼각 · 2 사각).</param>
    public static int ComboOf(IReadOnlyList<int> sails)
    {
        int main = At(sails, 0), sub = At(sails, 1), stern = At(sails, 2);
        return stern * 9 + sub * 3 + main;

        static int At(IReadOnlyList<int> v, int i) =>
            i < v.Count ? Math.Clamp(v[i], 0, 2) : 0;
    }

    /// <summary>그 돛 조합이 그 상대각에서 내는 효율. 표 밖이면 0.</summary>
    public int Efficiency(int combo, int relative)
    {
        if (combo < 0 || combo >= Combos) return 0;
        return _table[combo * Angles + (relative & (Angles - 1))];
    }

    /// <summary>돛대 셋과 상대각으로 바로.</summary>
    public int Efficiency(IReadOnlyList<int> sails, int relative) =>
        Efficiency(ComboOf(sails), relative);
}
