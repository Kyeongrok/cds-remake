namespace CdsHelper.Game.Local.Helpers;

/// <summary>
/// CDS_95.EXE 안의 <b>시설 화자표</b> — 어느 건물에서 누가 말을 거는지(얼굴 번호).
/// </summary>
/// <remarks>
/// <code>
///   표 VA 0x0056823C, 한 줄 13개 int32 x 건물코드 16줄
///   +0x00~+0x28  [문화권 0~10] 얼굴 번호
///   +0x2C        여자 얼굴인지 — 1 이면 FEMALE.CDS 에서 꺼낸다
///   +0x30        딸린 값(안 쓴다)
/// </code>
/// 시설 객체는 들어설 때 화자를 <c>0x004A2500</c> 에서 <c>0x00477C20(건물종류)</c> 으로
/// 한 번에 정하고, 그 속이 이 표를 <c>[건물종류 * 13 + 문화권]</c> 으로 읽어 제 <c>+0x80</c>
/// 에 넣는다. 인사할 때 그 값을 얼굴 대사 창(<c>0x004692E0</c>)에 그대로 넘긴다 —
/// 조선소가 <c>0x0044B4D7</c> 이다.
///
/// 그래서 <b>같은 조선소라도 마을에 따라 다른 사람</b>이 나온다.
/// <code>
///   조선소(6)  402 402 402 · 315 315 315 · 374 · 315 369 387 402
///   왕궁(2)    229 (집사, 문화권 무관)      교회(3)  292 (문화권 무관)
///   도서관(8)  161 …                        조합(9)  44 …
/// </code>
/// 자택(11)·저택(12)·상관(13,14)·학자 저택(15) 줄은 통째로 0 이다 — 말을 거는 사람이 없다.
///
/// <b>여관(5)만 여자 칸이 서 있다</b> — 그래서 여관 주인은 FEMALE.CDS 에서 나온다.
/// </remarks>
public sealed class SpeakerFaceTable
{
    /// <summary>적어 둘 파일 이름(<c>%APPDATA%\CdsHelper\exe-tables\화자표.json</c>).</summary>
    private const string CacheName = "화자표";

    private const int TableVa = 0x0056823C;

    /// <summary>건물 코드 수(항구 0 … 학자 저택 15).</summary>
    public const int Kinds = 16;

    /// <summary>한 줄에 든 칸 수. 앞 열한 칸이 문화권이다.</summary>
    private const int Columns = 13;

    /// <summary>문화권 수.</summary>
    public const int Cultures = 11;

    /// <summary>판이 다른 EXE 를 잘못 읽지 않으려고 대 보는 칸 — 교회는 어디서나 292 다.</summary>
    private const int ProbeKind = 3, ProbeFace = 292;

    /// <summary>표가 갈리면 적어 둔 JSON 을 버리고 다시 읽는다.</summary>
    private const int SnapshotVersion = 2;

    internal sealed record Snapshot(int[] Faces, bool[] Female);

    private readonly int[] _faces;
    private readonly bool[] _female;

    private SpeakerFaceTable(Snapshot snapshot)
    {
        _faces = snapshot.Faces;
        _female = snapshot.Female;
    }

    /// <summary>못 열었을 때의 까닭. 열렸으면 빈 문자열.</summary>
    public static string LastError { get; private set; } = "";

    /// <summary>표를 연다. 한 번 읽으면 JSON 으로 적어 두고 다음부터는 그것을 쓴다.</summary>
    public static SpeakerFaceTable? Open(string gameDirectory)
    {
        var snapshot = ExeTable.Open<Snapshot>(CacheName, gameDirectory, ReadFromExe, out string error,
                                               SnapshotVersion);
        LastError = error;
        return snapshot == null ? null : new SpeakerFaceTable(snapshot);
    }

    private static Snapshot? ReadFromExe(PeImage exe, out string error)
    {
        error = "";

        var faces = new int[Kinds * Cultures];
        var female = new bool[Kinds];
        for (int kind = 0; kind < Kinds; kind++)
        {
            for (int culture = 0; culture < Cultures; culture++)
                faces[kind * Cultures + culture] =
                    exe.Int(TableVa + (kind * Columns + culture) * 4);

            female[kind] = exe.Int(TableVa + (kind * Columns + Cultures) * 4) != 0;
        }

        if (faces[ProbeKind * Cultures] != ProbeFace)
        {
            error = "화자표가 기대한 모양이 아닙니다(다른 판의 EXE 일 수 있습니다)";
            return null;
        }
        return new Snapshot(faces, female);
    }

    /// <summary>
    /// 그 건물에서 말을 거는 사람의 얼굴 번호. 말을 거는 사람이 없으면(자택 따위) <c>-1</c>.
    /// </summary>
    /// <param name="buildingCode">건물 코드(항구 0 · 교역소 1 · 왕궁 2 · 교회 3 · 술집 4 …).</param>
    /// <param name="culture">문화권 번호(<see cref="CityExeTable.CultureOf"/>).</param>
    public int FaceOf(int buildingCode, int culture)
    {
        if (buildingCode < 0 || buildingCode >= Kinds) return -1;
        if (culture < 0 || culture >= Cultures) culture = 0;   // 모르면 유럽 것으로 문다

        int face = _faces[buildingCode * Cultures + culture];
        return face > 0 ? face : -1;
    }

    /// <summary>
    /// 그 건물의 화자가 여자인지. <b>여관만 그렇다</b> — 그때는 FEMALE.CDS 에서 꺼낸다.
    /// </summary>
    public bool IsFemale(int buildingCode) =>
        buildingCode >= 0 && buildingCode < Kinds && _female[buildingCode];
}
