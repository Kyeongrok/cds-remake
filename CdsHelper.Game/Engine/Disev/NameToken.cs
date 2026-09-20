namespace CdsHelper.Game.Local.Helpers;

/// <summary>
/// 대본 글 속의 <b>자리표</b>(<c>81 93 82 xx</c>) — 제독 이름과 그 뒤에 붙는 <b>조사</b>다.
/// </summary>
/// <remarks>
/// 대본 본문에는 <c>※</c>(CP932 <c>81 93</c>) 뒤에 전각 글자 하나가 붙은 자리표가 섞여 있다.
/// 놀이가 그 자리에 제독 이름과 조사를 채워 넣는다(<c>0x0040C410</c>).
/// <code>
///   ※ｓ · ※Ｓ   제독 이름 그대로
///   ※Ｇ · ※ｇ   가 / 이      (조사 갈래 0)
///   ※Ｈ · ※ｈ   는 / 은      (갈래 1)
///   ※Ｔ · ※ｔ   와 / 과      (갈래 3)
///   ※Ｙ · ※ｙ   라는 / 이라는 (갈래 8)
///   ※Ｄ · ※ｄ   (없음) / 이   (갈래 16 — 「길동이」의 그 이 다)
///   ※Ｗ 협 · ※Ｘ 대륙 · ※Ｍ 남방대륙      (글자 그대로 박는다)
/// </code>
/// 조사는 <c>0x004281B0</c> 이 고른다 — 앞 낱말의 <b>끝 글자에 받침이 있는지</b> 보고 표
/// 두 줄 가운데 하나를 집는다(<c>0x0053C424</c> 받침 없음 · <c>0x0053C4A0</c> 받침 있음).
/// 원본은 받침 없는 음절 377개를 스택에 늘어놓고 끝 글자를 찾지만, 유니코드로는 셈으로
/// 바로 나온다(<c>(글자 - 가) % 28 == 0</c>).
///
/// <c>으로</c> 갈래(10~15)만 한 가지가 더 있다 — 받침이 <b>ㄹ</b> 이면 <c>로</c> 다
/// (<c>0x00429576</c> 이 ㄹ 받침 음절 58개를 따로 본다). 한국어판 대본에는 이 갈래를 쓰는
/// 자리표가 없지만, 표를 옮기는 김에 규칙까지 옮겨 둔다.
/// </remarks>
public static class NameToken
{
    /// <summary>조사 표 — 갈래마다 (받침 없음, 받침 있음) 한 쌍.</summary>
    /// <remarks>
    /// <c>0x0053C424</c> · <c>0x0053C4A0</c> 두 줄을 차례 그대로 옮겼다. 갈래 10~15 의
    /// 받침 있는 쪽은 ㄹ 받침이면 앞의 것(로 계열)이 되므로 여기에는 <c>으</c> 붙은 꼴을 적고
    /// <see cref="Of"/> 가 ㄹ 을 가른다.
    /// </remarks>
    private static readonly (string Open, string Closed)[] Table =
    [
        ("가", "이"), ("는", "은"), ("를", "을"), ("와", "과"),
        ("와는", "과는"), ("와의", "과의"), ("라", "이라"), ("라고", "이라고"),
        ("라는", "이라는"), ("라면", "이라면"), ("로", "으로"), ("로는", "으로는"),
        ("로의", "으로의"), ("로부터", "으로부터"), ("로부터의", "으로부터의"),
        ("로부터도", "으로부터도"), ("", "이"),
    ];

    /// <summary>자리표 한 글자가 가리키는 조사 갈래. 조사가 아니면 -1.</summary>
    /// <remarks>대문자와 소문자가 같은 자리로 간다 — 원본이 둘 다 받는다.</remarks>
    public static int KindOf(byte code) => code switch
    {
        0x66 or 0x87 => 0,      // Ｇ ｇ  가/이
        0x67 or 0x88 => 1,      // Ｈ ｈ  는/은
        0x73 or 0x94 => 3,      // Ｔ ｔ  와/과
        0x78 or 0x99 => 8,      // Ｙ ｙ  라는/이라는
        0x63 or 0x84 => 16,     // Ｄ ｄ  (없음)/이
        _ => -1,
    };

    /// <summary>제독 이름을 그대로 박는 자리표인지(※ｓ · ※Ｓ).</summary>
    public static bool IsName(byte code) => code is 0x93 or 0x72;

    /// <summary>글자 그대로 박는 자리표. 아니면 null.</summary>
    public static string? WordOf(byte code) => code switch
    {
        0x76 => "협",
        0x77 => "대륙",
        0x6C => "남방대륙",
        _ => null,
    };

    /// <summary>
    /// 그 낱말 뒤에 붙을 조사. 갈래를 모르면 빈 글이다.
    /// </summary>
    public static string Of(string word, int kind)
    {
        if (kind < 0 || kind >= Table.Length) return "";

        var (open, closed) = Table[kind];
        int tail = FinalOf(word);
        if (tail < 0) return closed;                 // 한글이 아니면 받침 있는 쪽(원본도 그렇다)
        if (tail == 0) return open;                  // 받침 없음
        // ㄹ 받침은 「으」를 빼고 로 계열을 쓴다(10~15 갈래에서만 갈린다).
        return tail == 8 && kind is >= 10 and <= 15 ? open : closed;
    }

    /// <summary>
    /// 끝 글자의 <b>받침 번호</b>(0 없음 · 1~27 있음). 한글 음절이 아니면 -1.
    /// </summary>
    private static int FinalOf(string word)
    {
        for (int i = word.Length - 1; i >= 0; i--)
        {
            char ch = word[i];
            if (ch is ' ' or '　') continue;
            return ch is >= '가' and <= '힣' ? (ch - '가') % 28 : -1;
        }
        return -1;
    }
}
