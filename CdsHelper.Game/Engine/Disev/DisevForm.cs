using System.Buffers.Binary;
using System.Text;

namespace CdsHelper.Game.Local.Helpers;

/// <summary>
/// 명령 한 줄을 <b>칸으로 나눠</b> 고칠 수 있게 하는 본.
/// </summary>
/// <remarks>
/// <see cref="DisevScript"/> 가 풀어 보인 명령 가운데 <b>뜻이 확실한 것만</b> 칸을 준다.
/// 칸은 그 명령의 날바이트 안에서 자리와 폭으로 잡으므로, 고쳐도 <b>나머지 바이트는
/// 그대로</b>다 — 짚이지 않은 자리를 건드리지 않는다.
///
/// 대사만 길이가 자유라 따로 다룬다(<see cref="BuildDialogue"/>).
/// </remarks>
public static class DisevForm
{
    /// <summary>칸에 붙는 이름표 — 숫자 뒤에 무엇을 함께 보일지.</summary>
    public enum Lookup
    {
        /// <summary>그냥 숫자.</summary>
        None,
        /// <summary>능력치 번호.</summary>
        Stat,
        /// <summary>발견물 번호.</summary>
        Discovery,
        /// <summary>아이템 번호.</summary>
        Item,
        /// <summary>도시 번호.</summary>
        City,
        /// <summary>상대 이동값 — 덩이 밖으로 뛰면 위험하다.</summary>
        Relative,
        /// <summary>미니게임 번호(<see cref="DisevMinigame"/>).</summary>
        Minigame,

        /// <summary>특수 조우 연출 번호(<see cref="DisevScript.Encounters"/>).</summary>
        Encounter,
    }

    /// <summary>고칠 수 있는 칸 하나.</summary>
    /// <param name="Label">칸 이름.</param>
    /// <param name="Offset">명령 날바이트 안의 자리.</param>
    /// <param name="Width">1 · 2 · 4 바이트.</param>
    /// <param name="Kind">이름표 갈래.</param>
    public readonly record struct Field(string Label, int Offset, int Width, Lookup Kind = Lookup.None);

    private static Field[] F(params Field[] fields) => fields;

    /// <summary>
    /// 이 명령에 줄 칸들. 뜻을 모르는 명령이면 빈 배열이고, 그때는 날바이트로만 고친다.
    /// </summary>
    /// <param name="op">푼 명령.</param>
    public static Field[] FieldsFor(DisevScript.Op op)
    {
        // 대사는 길이가 자유라 칸으로 안 다룬다 — 창이 따로 받는다.
        if (op.Kind is "대사" or "다중 선택지 대사" or "도시 소문 등록") return [];

        switch (op.Kind)
        {
            case "AVI 재생" when op.Length == 4:
            case "CG 애니메이션 재생" when op.Length == 4:
            case "EVSTILL 이미지 표시":
            case "DSTILL 이미지 표시":
            case "음원 재생":
            case "음원 정지":
                return F(new Field("슬롯", 2, 2));

            case "미니게임":
                return F(new Field("미니게임", 2, 2, Lookup.Minigame));

            // 0E 14|1A [u32 판자] 04 [u16 n] — 판자는 발라몬의 탑만 쓴다.
            case "특수 조우 연출":
                return F(new Field("연출", 2, 2, Lookup.Encounter));

            case "퍼즐 미니게임" when op.Length == 9:
                return F(new Field("미니게임", 7, 2, Lookup.Minigame), new Field("판자", 2, 4));

            case "발견물 등록/발견 처리":
                return F(new Field("발견물", 2, 2, Lookup.Discovery));

            case "국가 조건":
            case "건물 조건":
            case "문화권 조건":
                return F(new Field("값", 2, 2));
            case "도시 조건":
                return F(new Field("도시", 2, 2, Lookup.City));

            case "연도 조건":
            case "기준 연도 일치 조건":
                return F(new Field("연도", 2, 2));
            case "연월 조건":
                return F(new Field("월", 2, 1), new Field("연도", 4, 2));
            case "연도 범위 조건":
                return F(new Field("시작 연도", 2, 2), new Field("끝 연도", 5, 2));

            case "발견 완료 조건":
            case "미발견 조건":
                return F(new Field("발견물", 2, 2, Lookup.Discovery));

            case "아이템 소지 조건":
            case "아이템 비소지 조건":
            case "아이템 보이기":
            case "아이템 상실":
            case "아이템 획득(발견물 제외)":
            case "아이템 획득(버리기 창)":
                return F(new Field("아이템", 2, 2, Lookup.Item));

            case "힌트 획득":
                return F(new Field("힌트", 2, 2));
            case "교역품 활성화":
                return F(new Field("교역품", 2, 2));
            case "국가 멸망 처리":
                return F(new Field("나라", 2, 2));
            case "도시 점령지 설정":
            case "도시 점령지 해제":
            case "도시 제거":
            case "이벤트 대상 도시 이동":
                return F(new Field("도시", 2, 2, Lookup.City));
            case "도시 국적 변경":
                return F(new Field("도시", 5, 2, Lookup.City));
            case "도시 시설 제거":
                return F(new Field("시설 비트", 2, 2), new Field("도시", 5, 2, Lookup.City));
            case "인물 조우 처리":
            case "통역 고용·교체":
            case "일기토":
                return F(new Field("인물", 2, 2));
            case "능력 판정":
                return F(new Field("능력", 2, 2, Lookup.Stat));
            case "상태값 참조 증가":
                return F(new Field("상태값", 2, 2, Lookup.Stat), new Field("피연산자", 5, 2, Lookup.Stat));

            case "힌트 상태 활성 조건":
            case "힌트 상태 미활성 조건":
                return F(new Field("힌트 상태", 2, 2));

            case "인물 런타임 조건":
            case "후원자 런타임 조건":
                return F(new Field("번호", 2, 2));
            case "일기토 연출 세트 설정":
                return F(new Field("세트(0~6)", 2, 2));

            case "육상전(인물)":
                return F(new Field("적 대장 인물", 2, 2));
            case "육상전(도시)":
                return F(new Field("도시", 2, 2, Lookup.City));

            case "힌트 조건 분기":
                return F(new Field("힌트", 3, 2), new Field("상대 이동", 5, 2, Lookup.Relative));

            case "금화 증가":
            case "금화 감소":
                return F(new Field("금화", 2, 4));

            case "능력치 조건":
            case "수치 비교 (초과)":
            case "수치 비교 (이하)":
            case "수치 비교 (미만)":
                return F(new Field("능력치", 2, 2, Lookup.Stat), new Field("값", 5, 4));

            case "능력치 증가":
            case "능력치 감소":
            case "능력치 설정":
            case "능력치/기한 설정":
                return op.Length >= 13
                    ? F(new Field("능력치", 2, 2, Lookup.Stat),
                        new Field("무작위 폭", 5, 4), new Field("시작값", 9, 4))
                    : F(new Field("능력치", 2, 2, Lookup.Stat), new Field("값", 5, 4));

            case "무작위 확률 조건":
                return F(new Field("분모", 2, 4), new Field("성공값", 7, 4));

            case "신도시 생성":
                return F(new Field("도시", 2, 2, Lookup.City));
            case "특수 건물 생성":
                return F(new Field("건물", 2, 2), new Field("도시", 5, 2, Lookup.City));

            case "결과 거짓 시 이동":
            case "결과 참 시 이동":
            case "이전 조건 참 시 이동":
            case "부관 고용 시 이동":
            case "STORY0.CDS 외 분기":
            case "STORY1.CDS 외 분기":
                return F(new Field("상대 이동", 2, 2, Lookup.Relative));

            case "선택지 분기":
                return F(new Field("선택값", 3, 1), new Field("상대 이동", 4, 2, Lookup.Relative));

            case "미확인 0015 분기":
                return F(new Field("값", 2, 2), new Field("상대 이동", 4, 2, Lookup.Relative));

            case "아이템 조건 분기":
            case "아이템 미소지 분기":
                return F(new Field("아이템", 3, 2, Lookup.Item),
                         new Field("상대 이동", 5, 2, Lookup.Relative));
            case "발견물 조건 분기":
            case "미발견 분기":
                return F(new Field("발견물", 3, 2, Lookup.Discovery),
                         new Field("상대 이동", 5, 2, Lookup.Relative));
            case "힌트 미활성 분기":
                return F(new Field("힌트", 3, 2), new Field("상대 이동", 5, 2, Lookup.Relative));
            case "기준 연도 분기":
            case "연도 상한 분기":
                return F(new Field("연도", 3, 2), new Field("상대 이동", 5, 2, Lookup.Relative));
            case "연도 범위 분기":
                return F(new Field("시작 연도", 3, 2), new Field("끝 연도", 6, 2),
                         new Field("상대 이동", 8, 2, Lookup.Relative));
            case "도시 분기":
                return F(new Field("도시", 3, 2, Lookup.City), new Field("상대 이동", 5, 2, Lookup.Relative));
            case "인물 조우 분기":
            case "후원자 활성 분기":
                return F(new Field("번호", 3, 2), new Field("상대 이동", 5, 2, Lookup.Relative));
            case "도시 국적 분기":
                return F(new Field("나라", 3, 2), new Field("도시", 6, 2, Lookup.City),
                         new Field("상대 이동", 8, 2, Lookup.Relative));

            // 43 2B~2E 1C [u16 칸] [값 식] [u16 이동] — 값 식 꼴마다 칸 자리가 다르다.
            case DisevScript.CompareKind when op.Length == 12:
                return F(new Field("상태값", 3, 2, Lookup.Stat), new Field("값", 6, 4),
                         new Field("상대 이동", 10, 2, Lookup.Relative));
            case DisevScript.CompareKind when op.Length == 10 && op.Hex.Length >= 17 && op.Hex.Substring(15, 2) == "1C":
                return F(new Field("상태값", 3, 2, Lookup.Stat), new Field("견줄 상태값", 6, 2, Lookup.Stat),
                         new Field("상대 이동", 8, 2, Lookup.Relative));
            case DisevScript.CompareKind when op.Length == 10:
                return F(new Field("상태값", 3, 2, Lookup.Stat), new Field("값", 6, 2),
                         new Field("상대 이동", 8, 2, Lookup.Relative));
            case DisevScript.CompareKind when op.Length == 16:
                return F(new Field("상태값", 3, 2, Lookup.Stat), new Field("무작위 폭", 6, 4),
                         new Field("시작값", 10, 4), new Field("상대 이동", 14, 2, Lookup.Relative));

            case "교역품 조건 분기":
                return F(new Field("원산 도시", 3, 2, Lookup.City), new Field("교역품", 6, 2),
                         new Field("수량", 9, 4), new Field("상대 이동", 13, 2, Lookup.Relative));

            default:
                return [];
        }
    }

    /// <summary>칸의 지금 값을 읽는다.</summary>
    public static long Read(ReadOnlySpan<byte> raw, Field field) => field.Width switch
    {
        1 => field.Offset < raw.Length ? raw[field.Offset] : 0,
        2 => field.Offset + 2 <= raw.Length ? BinaryPrimitives.ReadUInt16LittleEndian(raw[field.Offset..]) : 0,
        _ => field.Offset + 4 <= raw.Length ? BinaryPrimitives.ReadUInt32LittleEndian(raw[field.Offset..]) : 0,
    };

    /// <summary>칸에 값을 써 넣은 새 날바이트를 만든다. 폭을 넘으면 null.</summary>
    public static byte[]? Write(byte[] raw, Field field, long value, out string error)
    {
        error = "";
        long max = field.Width switch { 1 => byte.MaxValue, 2 => ushort.MaxValue, _ => uint.MaxValue };
        if (value < 0 || value > max)
        {
            error = $"{field.Label}: 0 ~ {max} 사이라야 합니다";
            return null;
        }
        if (field.Offset + field.Width > raw.Length)
        {
            error = $"{field.Label}: 명령 길이를 벗어납니다";
            return null;
        }

        var output = (byte[])raw.Clone();
        switch (field.Width)
        {
            case 1: output[field.Offset] = (byte)value; break;
            case 2: BinaryPrimitives.WriteUInt16LittleEndian(output.AsSpan(field.Offset), (ushort)value); break;
            default: BinaryPrimitives.WriteUInt32LittleEndian(output.AsSpan(field.Offset), (uint)value); break;
        }
        return output;
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

    /// <summary>
    /// 대사 한 줄을 다시 짠다 — <c>[창 플래그] 0A [화자 태그 81 46] [본문 CP949] 00</c>.
    /// </summary>
    /// <param name="flag">창 플래그. 없으면 <c>0A</c> 로 바로 연다.</param>
    /// <param name="speakerTag">화자 태그 날바이트(CP932). 없으면 빈 배열.</param>
    /// <param name="text">창에 보이던 본문 그대로. 전각으로 되돌려 넣는다.</param>
    /// <remarks>
    /// <paramref name="text"/> 는 <see cref="DisevScript.DecodeDialogue(ReadOnlySpan{byte}, bool)"/> 를
    /// <c>normalize: false</c> 로 부른 <b>무손실 글</b>이라야 한다. 그래야 전각·자리표·구분자가
    /// 그대로 되돌아간다. 반각 ASCII 는 놀이 글꼴에 맞춰 전각으로 굽는다.
    /// </remarks>
    public static byte[] BuildDialogue(int? flag, ReadOnlySpan<byte> speakerTag, string text)
    {
        var output = new List<byte>(text.Length * 2 + 8);
        if (flag is { } value) output.Add((byte)value);
        output.Add(0x0A);
        if (speakerTag.Length > 0)
        {
            output.AddRange(speakerTag.ToArray());
            output.Add(0x81);
            output.Add(0x46);
        }
        output.AddRange(EncodeBody(text));
        output.Add(0x00);
        return output.ToArray();
    }

    /// <summary>본문을 원본 표기(전각·자리표·구분자)로 되돌려 CP949 로 굽는다.</summary>
    private static byte[] EncodeBody(string text)
    {
        var output = new List<byte>(text.Length * 2);
        var run = new StringBuilder();

        void Flush()
        {
            if (run.Length == 0) return;
            output.AddRange(Cp949.GetBytes(run.ToString()));
            run.Clear();
        }

        for (int i = 0; i < text.Length;)
        {
            // 자리표는 놀이가 그때그때 채우는 것이라 원래 바이트로 돌려놓는다.
            if (text[i] == '<' && TryToken(text, i, out byte token, out int used))
            {
                Flush();
                output.AddRange([0x81, 0x93, 0x82, token]);
                i += used;
                continue;
            }

            // <01> · <CR> 처럼 적어 둔 안 보이는 글자를 되돌린다.
            if (text[i] == '<' && TryControl(text, i, out byte control, out int span))
            {
                Flush();
                output.Add(control);
                i += span;
                continue;
            }

            char ch = text[i++];
            if (ch == '/')
            {
                Flush();
                output.AddRange([0x81, 0x5E]);
                continue;
            }
            // 반각 사이띄개만 전각으로 올린다. 나머지 글자는 <b>그대로 둔다</b> —
            // 원본에 반각이 있었다면 그대로 남겨야 바이트가 어긋나지 않는다.
            // 새로 적은 글을 전각으로 고르는 것은 창의 「전각으로」 단추가 한다.
            run.Append(ch == ' ' ? '　' : ch);
        }

        Flush();
        return output.ToArray();
    }

    /// <summary>자리표 이름표. 무손실로 푼 글에는 이 꼴로 들어 있다.</summary>
    private static readonly (string Text, byte Value)[] Tokens =
        [("<남방대륙>", 0x6C), ("<제독>", 0x93), ("<대륙>", 0x77), ("<협>", 0x76),
         ("<(이)라는>", 0x78), ("<(이)>", 0x63), ("<이/가>", 0x66), ("<은/는>", 0x67),
         ("<와/과>", 0x73)];

    private const string TokenPrefix = "<자리표 0x";

    private static bool TryToken(string text, int at, out byte value, out int used)
    {
        foreach (var (name, code) in Tokens)
        {
            if (string.CompareOrdinal(text, at, name, 0, name.Length) == 0)
            {
                value = code;
                used = name.Length;
                return true;
            }
        }

        // 이름을 못 짚은 자리표는 "<자리표 0xNN>" 으로 적어 두었다 — 그대로 되돌린다.
        int digits = at + TokenPrefix.Length;
        if (digits + 3 <= text.Length &&
            string.CompareOrdinal(text, at, TokenPrefix, 0, TokenPrefix.Length) == 0 &&
            Uri.IsHexDigit(text[digits]) && Uri.IsHexDigit(text[digits + 1]) && text[digits + 2] == '>')
        {
            value = Convert.ToByte(text.Substring(digits, 2), 16);
            used = TokenPrefix.Length + 3;
            return true;
        }

        value = 0;
        used = 0;
        return false;
    }

    private static readonly (string Text, byte Value)[] Controls =
        [("<CR>", 0x0D), ("<LF>", 0x0A), ("<TAB>", 0x09)];

    private static bool TryControl(string text, int at, out byte value, out int span)
    {
        foreach (var (name, code) in Controls)
        {
            if (string.CompareOrdinal(text, at, name, 0, name.Length) == 0)
            {
                value = code;
                span = name.Length;
                return true;
            }
        }
        // <XX> 꼴.
        if (at + 4 <= text.Length && text[at + 3] == '>' &&
            Uri.IsHexDigit(text[at + 1]) && Uri.IsHexDigit(text[at + 2]))
        {
            value = Convert.ToByte(text.Substring(at + 1, 2), 16);
            span = 4;
            return true;
        }
        value = 0;
        span = 0;
        return false;
    }

    /// <summary>
    /// 반각 글자를 놀이 글꼴에 맞는 전각으로 올린다 — 창의 「전각으로」 단추가 쓴다.
    /// <c>&lt;01&gt;</c> 같은 이름표 안은 건드리지 않는다.
    /// </summary>
    public static string ToWide(string text)
    {
        var output = new StringBuilder(text.Length);
        for (int i = 0; i < text.Length;)
        {
            if (text[i] == '<' && (TryToken(text, i, out _, out int used) || TryControl(text, i, out _, out used)))
            {
                output.Append(text, i, used);
                i += used;
                continue;
            }
            char ch = text[i++];
            output.Append(ch switch
            {
                ' ' => '　',
                >= '!' and <= '~' => (char)(ch + 0xFEE0),
                _ => ch,
            });
        }
        return output.ToString();
    }

    /// <summary>대사 명령을 창 플래그 · 화자 태그 · 본문으로 가른다.</summary>
    public static (int? Flag, byte[] SpeakerTag) SplitDialogue(byte[] raw)
    {
        int? flag = raw.Length > 0 && raw[0] == 0x0A ? null : raw.Length > 0 ? raw[0] : null;
        int textStart = flag == null ? 1 : 2;

        byte[] tag = [];
        int look = Math.Min(raw.Length - 1, textStart + 40);
        for (int i = textStart; i + 1 < look; i++)
        {
            if (raw[i] != 0x81 || raw[i + 1] != 0x46) continue;
            tag = raw[textStart..i];
            textStart = i + 2;
            break;
        }

        return (flag, tag);
    }
}
