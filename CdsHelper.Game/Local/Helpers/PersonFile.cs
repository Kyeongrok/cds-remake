using System.IO;
using System.Text;

namespace CdsHelper.Game.Local.Helpers;

/// <summary>
/// 세이브(<c>SAVEDATA.CDS</c>)에서 <b>인물 표를 읽어 온다</b> — 281명.
/// </summary>
/// <remarks>
/// <b>읽기만 한다.</b> 놀이가 늘 보는 것은 <see cref="PersonTable"/>(<c>인물표.json</c>)이고,
/// 이 손은 그 표를 <b>굽는 씨앗</b>일 뿐이다. 세이브를 되쓰지 않으므로 남의 판이 상할 일이 없다.
///
/// 자리는 볼트 <c>72.분석-인물 이동(역사 항해사와 매달 굴림)</c> 에서 왔고, 직렬화 손
/// <c>0x00431CB0</c>(읽기) · <c>0x00431E90</c>(쓰기) 를 따라가 파일 144바이트가 한 바이트도
/// 남지 않게 맞춘 것이다.
/// <code>
///   +0x00 능력 여섯(u8)   +0x0A 등급(u8)        +0x0B 기술 열셋(u8)   +0x18 언어 열넷(u8)
///   +0x26 명성(u32)       +0x2E 도시(i16)       +0x30 건물(i16)
///   +0x32 이름(19)        +0x45 성(19, cp949)   +0x58 얼굴(u32)
///   +0x5C 나이(i32)       +0x62 고용상태(i16)   +0x64 이동 갈래(i16)  +0x66 계약금 밑값(i32)
///   +0x6A 등장 여부(u32)  +0x72 목적지 도시(i16) +0x74 날 셈(i32)
/// </code>
/// <b>등장 여부는 +0x6A 다.</b> +0x0A 는 등급이고, 그것으로 자리(술집·여관)가 갈린다 —
/// 게임이 달마다 <c>[인물+0x3C] &gt;= 2 ? 술집 : 여관</c> 으로 놓는다.
///
/// 칸 수 281 은 EXE 가 못박은 값이다(<c>0x004319D0</c> 의 <c>cmp eax, 0x119</c>).
/// </remarks>
public static class PersonFile
{
    /// <summary>알맹이 기준 표 시작. 파일 자리는 판 문자열 길이에 딸려 움직인다.</summary>
    private const int TableStartRel = 0x9237;

    /// <summary>알맹이 기준 <b>해</b>가 놓인 자리(낱말).</summary>
    /// <remarks>
    /// 표에 적힌 나이는 <b>그 세이브의 그 해</b> 나이다 — 게임이 해마다 한 살씩 먹인다.
    /// 그래서 표를 구울 때 해를 함께 적어 두어야 다른 해의 나이를 셀 수 있다.
    /// </remarks>
    private const int YearRel = 0x02;

    /// <summary>마지막으로 읽은 세이브의 해. 못 읽었으면 0.</summary>
    public static int LastYear { get; private set; }

    /// <summary>한 칸의 크기.</summary>
    public const int RecordSize = 0x90;

    private const int StatAt = 0x00, GradeAt = 0x0A, SkillAt = 0x0B, LangAt = 0x18,
                      FameAt = 0x26, CityAt = 0x2E, BuildingAt = 0x30, FirstAt = 0x32,
                      LastAt = 0x45, FaceAt = 0x58, AgeAt = 0x5C, HireAt = 0x62,
                      KindAt = 0x64, FeeAt = 0x66, AppearAt = 0x6A, DestAt = 0x72, WaitAt = 0x74;

    /// <summary>이름·성 한 칸이 쓰는 바이트 수.</summary>
    private const int NameBytes = 0x13;

    /// <summary>왜 못 읽었는지. 잘 읽었으면 빈 문자열.</summary>
    public static string LastError { get; private set; } = "";

    /// <summary>
    /// 세이브에서 인물 281명을 읽는다. 못 읽으면 null 이고 까닭은 <see cref="LastError"/> 에 있다.
    /// </summary>
    public static List<PersonTable.Row>? ReadAll(string? savePath)
    {
        LastError = "";
        if (string.IsNullOrWhiteSpace(savePath) || !File.Exists(savePath))
        {
            LastError = "세이브 파일을 찾지 못했습니다";
            return null;
        }

        byte[] data;
        try { data = File.ReadAllBytes(savePath); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            LastError = ex.Message;
            return null;
        }

        int body = BodyStart(data);
        LastYear = body + YearRel + 2 <= data.Length
            ? BitConverter.ToUInt16(data, body + YearRel) : 0;

        int start = body + TableStartRel;
        if (start + (RecordSize * PersonTable.Count) > data.Length)
        {
            LastError = "세이브가 너무 짧습니다 — 인물 표가 다 들어 있지 않습니다";
            return null;
        }

        var rows = new List<PersonTable.Row>(PersonTable.Count);
        for (int i = 0; i < PersonTable.Count; i++)
        {
            int at = start + (i * RecordSize);
            var row = new PersonTable.Row
            {
                Id = i,
                First = Text(data, at + FirstAt),
                Last = Text(data, at + LastAt),
                Grade = data[at + GradeAt],
                Fame = BitConverter.ToInt32(data, at + FameAt),
                City = Where(BitConverter.ToInt16(data, at + CityAt)),
                Building = Sits(BitConverter.ToInt16(data, at + BuildingAt)),
                Face = BitConverter.ToInt32(data, at + FaceAt),
                Age = BitConverter.ToInt32(data, at + AgeAt),
                Hire = BitConverter.ToInt16(data, at + HireAt),
                Kind = BitConverter.ToInt16(data, at + KindAt),
                Fee = BitConverter.ToInt32(data, at + FeeAt),
                Appear = BitConverter.ToInt32(data, at + AppearAt),
                Dest = Where(BitConverter.ToInt16(data, at + DestAt)),
                Wait = BitConverter.ToInt32(data, at + WaitAt),
            };
            for (int s = 0; s < PersonTable.StatCount; s++) row.Stats[s] = data[at + StatAt + s];
            for (int s = 0; s < PersonTable.SkillCount; s++) row.Skills[s] = data[at + SkillAt + s];
            for (int l = 0; l < PersonTable.LangCount; l++) row.Languages[l] = data[at + LangAt + l];
            rows.Add(row);
        }
        return rows;
    }

    /// <summary>
    /// 도시 칸을 추린다 — 표 밖이면 <b>-1(없음)</b> 로 눕힌다.
    /// </summary>
    /// <remarks>
    /// 세이브에는 "없음" 이 <c>-1</c> 로도 <c>255</c> 로도 적혀 있다(1517년 판에 둘 다 있다).
    /// 게임은 둘을 똑같이 없는 것으로 본다 — <c>0x00429950</c> 이 <c>0xE2</c> 이상이면 0 을
    /// 돌려주기 때문이다. 표에서는 한 가지로 눕혀 둔다.
    /// </remarks>
    private static int Where(int city) =>
        city >= 0 && city < PersonTable.CityCount ? city : -1;

    /// <summary>건물 칸을 추린다 — 주점·여관이 아니면 -1.</summary>
    private static int Sits(int building) =>
        building is PersonTable.Tavern or PersonTable.Inn ? building : -1;

    /// <summary>알맹이 시작 자리를 판 문자열 길이에서 되짚는다(<c>0x00478B6E</c>).</summary>
    private static int BodyStart(byte[] data)
    {
        int i = 4;
        while (i < data.Length && data[i] != 0) i++;
        int start = i + 1;
        return start < data.Length ? start : 0x13;
    }

    private static string Text(byte[] data, int at)
    {
        int len = 0;
        while (len < NameBytes && data[at + len] != 0) len++;
        return len == 0 ? "" : Cp949.GetString(data, at, len).Trim();
    }

    private static Encoding? _cp949;

    private static Encoding Cp949
    {
        get
        {
            if (_cp949 != null) return _cp949;
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            return _cp949 = Encoding.GetEncoding(949);
        }
    }
}
