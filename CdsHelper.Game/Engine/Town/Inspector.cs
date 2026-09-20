namespace CdsHelper.Game.Engine.Town;

/// <summary>
/// 감찰관 — 계약을 맺으면 후원자가 딸려 보내는 사람이다.
/// </summary>
/// <remarks>
/// 사람 하나를 그 자리에서 지어 낸다. <b>얼굴은 늘 같고 이름만 매번 갈린다.</b>
/// <code>
///   0x004AF450  감찰관 세우기
///     4AF459      0x004ADD50()          ; 빈 사람 물건 하나
///     4AF46E      0x004AF490(문화권)     ; 이름표에서 하나 뽑는다
///     4AF479      0x00493BE0(사람, 이름, 0xE8)   ; 이름과 얼굴(232) 을 박는다
///   0x004AF490  이름표 고르기
///     4AF4B0      문화권 1 → 0x0057C2D0 에서 rand(11)
///     4AF4C2      문화권 2 → 0x0057C2A8 에서 rand(10)
///     4AF49E      그 밖    → 0x0057C300 에서 rand(13)
/// </code>
/// 뽑고 나면 후원자가 「감찰관으로서 … 따라가 주게. %s, 부탁하네」(<c>0x00546CE0</c>) 라
/// 이르고 감찰관이 「하앗, 알겠습니다.」(<c>0x00546D10</c>) 라 답한다.
///
/// 이 사람은 인물 표에 없는 <b>그때그때 짓는 사람</b>이라 우리도 이름만 적어 둔다.
/// 발견 이벤트(DISEV)가 감찰관이 있을 때 그의 대사를 끼워 넣는 데 그 이름을 쓴다.
/// </remarks>
public static class Inspector
{
    /// <summary>감찰관 얼굴 번호. 누구를 뽑든 이 얼굴이다(<c>0x004AF479</c> 의 <c>0xE8</c>).</summary>
    public const int Face = 232;

    /// <summary>문화권 1 의 이름 열하나(<c>0x0057C2D0</c>).</summary>
    private static readonly string[] Culture1 =
    [
        "호리스", "길베르트", "구스타프", "니콜라스", "마티아스", "요제프",
        "제롬", "스테판", "빅토르", "토마스", "앙드레",
    ];

    /// <summary>문화권 2 의 이름 열(<c>0x0057C2A8</c>).</summary>
    private static readonly string[] Culture2 =
    [
        "로베르토", "파올로", "마르코", "야코포", "자코모",
        "쥬제뻬", "조르지오", "필리포", "후리오", "그레고리오",
    ];

    /// <summary>그 밖 문화권의 이름 열셋(<c>0x0057C300</c>).</summary>
    private static readonly string[] Others =
    [
        "베니토", "파블로", "페드로", "호세", "미구엘", "로드비고", "우고",
        "아메디오", "로도리게스", "헤랄드", "두알테", "아브라함", "마르코",
    ];

    /// <summary>그 문화권의 이름표.</summary>
    public static string[] NamesOf(int culture) => culture switch
    {
        1 => Culture1,
        2 => Culture2,
        _ => Others,
    };

    /// <summary>감찰관 이름을 하나 뽑는다.</summary>
    public static string Pick(int culture, Random dice)
    {
        var names = NamesOf(culture);
        return names[dice.Next(names.Length)];
    }
}
