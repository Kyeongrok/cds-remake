using System.Text.Json.Serialization;

namespace CdsHelper.Game.Local.Helpers;

/// <summary>
/// 인물 281명의 밑표(CDS_95.EXE) — 세이브에 없는 <b>나라</b>와 <b>직업</b>이 여기서 온다.
/// </summary>
/// <remarks>
/// <code>
///   표 0x004DF3F0 · 204바이트(0xCC) x 281   꺼내기 0x00431A70 (번호 x 204)
///   +0x00 이름 ptr  +0x04 성 ptr  +0x08 얼굴  +0x10 나이
///   +0x14 나라  → 인물 +0x14 (vtbl+0x14 = 0x0041B210)
///   +0x20 직업  → 인물 +0x1C (vtbl+0x18 = 0x0041B220)
/// </code>
/// 판을 열 때 <c>0x00431A90</c> 이 이 줄을 인물 객체에 옮긴다. 세이브 쪽 직렬화는 두 칸을 안
/// 건드리므로 나라와 직업은 늘 이 표 그대로다.
///
/// 나라 0 은 포르투갈(디아스 · 다 가마), 1 은 에스파니아(베스풋치 · 피사로), 27 은 이슬람
/// (하산 · 아랍 해적)이다. 직업 4 가 해적이다(케말 레이스 · 사략 함대 · 콜세르).
/// 바다에서 붙은 배를 가를 때 쓴다(볼트 <c>59.분석-해적 조우</c> 6절).
/// </remarks>
public sealed class PersonTemplate
{
    /// <summary>적어 둘 파일 이름(<c>%APPDATA%\CdsHelper\exe-tables\인물밑표.json</c>).</summary>
    private const string CacheName = "인물밑표";

    private const int TableVa = 0x004DF3F0;
    private const int RowSize = 0xCC;

    /// <summary>줄 수 — 인물 수와 같다.</summary>
    public const int Count = 281;

    /// <summary>해적 직업 번호. <c>0x0048C2D9</c> 의 <c>cmp eax, 4</c> 다.</summary>
    public const int PirateJob = 4;

    /// <summary>직업 이름표(<c>0x00560AA8</c>, 글 포인터 줄). 인물은 0·3·4·5·6·7·13·21 을 쓴다.</summary>
    private const int JobNameVa = 0x00560AA8;

    /// <summary>표에 든 가장 큰 직업 번호보다 넉넉히.</summary>
    private const int JobNameCount = 32;

    /// <summary>한 사람의 밑줄.</summary>
    /// <param name="Face">밑표 <c>+0x08</c> 얼굴 — 별자리 셈에 들어간다.</param>
    /// <param name="Blood">밑표 <c>+0x1C</c> 혈액형(0 A · 1 B · 2 O · 3 AB).</param>
    /// <param name="JobName">직업 이름(<c>0x00560AA8[직업]</c>).</param>
    [method: JsonConstructor]
    public readonly record struct Template(int Id, int Nation, int Job,
                                           int Face = 0, int Blood = 0, string JobName = "")
    {
        /// <summary>
        /// 별자리. <b>생일 칸이 없어</b> 게임이 <c>(얼굴 + 혈액형 + 나라) % 12</c> 로 지어낸다
        /// (<c>0x004780B0</c>). 비센테(194+0+1)%12=3 → 게좌.
        /// </summary>
        [JsonIgnore]
        public string Zodiac =>
            Support.Local.Models.Player.Zodiacs[((Face + Blood + Nation) % 12 + 12) % 12];

        /// <summary>혈액형 이름.</summary>
        [JsonIgnore]
        public string BloodName =>
            Support.Local.Models.Player.BloodTypes[Math.Clamp(Blood, 0, 3)];
    }

    /// <summary>JSON 으로 적어 두는 알맹이.</summary>
    internal sealed record Snapshot(Template[] Rows);

    private readonly Template[] _rows;

    private PersonTemplate(Template[] rows) => _rows = rows;

    /// <summary>왜 못 열었는지. 잘 열렸으면 빈 문자열.</summary>
    public static string LastError { get; private set; } = "";

    /// <summary>표를 연다. 적어 둔 JSON 이 있으면 그것을 읽는다.</summary>
    public static PersonTemplate? Open(string gameDirectory)
    {
        // 판 2: 얼굴·혈액형·직업 이름을 더했다.
        var snapshot = ExeTable.Open<Snapshot>(CacheName, gameDirectory, ReadFromExe, out string error,
                                               version: 2);
        LastError = error;
        return snapshot == null ? null : new PersonTemplate(snapshot.Rows);
    }

    /// <summary>그 사람의 밑줄. 범위 밖이면 null.</summary>
    public Template? Find(int id) => id >= 0 && id < _rows.Length ? _rows[id] : null;

    private static Snapshot? ReadFromExe(PeImage exe, out string error)
    {
        error = "";

        var rows = new Template[Count];
        for (int i = 0; i < Count; i++)
        {
            int row = TableVa + i * RowSize;
            int job = exe.Int(row + 0x20);
            string jobName = job is >= 0 and < JobNameCount
                ? exe.Text(exe.Word(JobNameVa + job * 4)) ?? "" : "";
            rows[i] = new Template(i, exe.Int(row + 0x14), job,
                                   exe.Int(row + 0x08), exe.Int(row + 0x1C), jobName);
        }

        // 3번 바스코·다 가마는 포르투갈, 6번 아메리고·베스풋치는 에스파니아다 — 판이 다른
        // EXE 를 잘못 읽으면 여기서 어긋난다.
        if (rows[3].Nation != 0 || rows[6].Nation != 1)
        {
            error = "인물 밑표가 기대한 모양이 아닙니다(다른 판의 EXE 일 수 있습니다)";
            return null;
        }

        return new Snapshot(rows);
    }
}
