using System.Text.Json.Serialization;

namespace CdsHelper.Support.Local.Models;

public class PatronPreferences
{
    [JsonPropertyName("geography")]
    public bool Geography { get; set; }

    [JsonPropertyName("treasure")]
    public bool Treasure { get; set; }

    [JsonPropertyName("tradeGoods")]
    public bool TradeGoods { get; set; }

    [JsonPropertyName("creature")]
    public bool Creature { get; set; }

    [JsonPropertyName("history")]
    public bool History { get; set; }

    [JsonPropertyName("religion")]
    public bool Religion { get; set; }

    [JsonPropertyName("superstition")]
    public bool Superstition { get; set; }

    [JsonPropertyName("ethnicity")]
    public bool Ethnicity { get; set; }
}

public class Patron
{
    [JsonPropertyName("id")]
    public int? Id { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("nationality")]
    public string Nationality { get; set; } = string.Empty;

    [JsonPropertyName("city")]
    public string City { get; set; } = string.Empty;

    [JsonPropertyName("supportRate")]
    public string SupportRate { get; set; } = string.Empty;

    [JsonPropertyName("discernment")]
    public int Discernment { get; set; }

    [JsonPropertyName("occupation")]
    public string Occupation { get; set; } = string.Empty;

    [JsonPropertyName("appearYear")]
    public int? AppearYear { get; set; }

    [JsonPropertyName("retireYear")]
    public int? RetireYear { get; set; }

    [JsonPropertyName("preferences")]
    public PatronPreferences Preferences { get; set; } = new();

    [JsonPropertyName("fame")]
    public int Fame { get; set; }

    [JsonPropertyName("wealth")]
    public int Wealth { get; set; }

    [JsonPropertyName("power")]
    public string Power { get; set; } = string.Empty;

    [JsonPropertyName("note")]
    public string Note { get; set; } = string.Empty;

    /// <summary>
    /// 후원율을 수로. 자료에는 "82%" 처럼 적혀 있다. 못 읽으면 0 이다.
    /// </summary>
    /// <remarks>
    /// 게임은 힌트에 적힌 자금(힌트 표 <c>+0x10</c>)에 이 비율을 곱해 낼 돈을 정한다 —
    /// <see cref="Helpers.HintTable.FundsFor"/> 참고.
    /// </remarks>
    [JsonIgnore]
    public int SupportRatePercent =>
        int.TryParse(SupportRate.TrimEnd('%', ' '), out int rate) ? rate : 0;

    /// <summary>
    /// 이 후원자가 앉는 건물 종류. 앞에 있는 것부터 찾아 도시에 있는 첫 건물에 앉힌다.
    /// </summary>
    /// <remarks>
    /// 게임은 건물마다 후원자를 하나 물려 두고(시설 객체 <c>+0xB4</c>), 물린 것이 없으면
    /// 설득 줄을 아예 감춘다. 그 짝이 어디에 적혀 있는지는 아직 못 찾아서 직업으로 맺는다.
    ///
    /// 자료를 맞춰 보고 정한 것이다 — 건물 표(225개 도시)와 후원자 81명을 대 보면 짝이
    /// 뚜렷하다. 총독부가 있는 도시 10곳에 총독이 11명, 상관 9곳에 상인이 10명, 왕궁이 있는
    /// 도시마다 국왕이 있고, 황궁 한 곳(로마)에 교황 둘이 있다. 두 번째 자리는 그 건물이
    /// 없는 도시를 위한 것이다(피렌체의 총독은 총독부가 없어 왕궁에 앉는다).
    /// </remarks>
    [JsonIgnore]
    public string[] Seats => Occupation switch
    {
        "국왕" => ["왕궁", "황궁", "성"],
        "교황" => ["황궁", "교회"],
        "총독" => ["총독부", "왕궁"],
        "상인" => ["상관", "교역소"],
        "학자" => ["학자 저택", "저택"],
        "관리" => ["관청", "저택"],
        "귀족" => ["저택", "성", "왕궁"],
        "신부" => ["교회", "사원"],
        _ => [],
    };

    public bool IsActive(int currentYear)
    {
        if (AppearYear.HasValue && currentYear < AppearYear.Value)
            return false;

        if (RetireYear.HasValue && currentYear >= RetireYear.Value)
            return false;

        return true;
    }

    public string StatusDisplay(int currentYear)
    {
        if (AppearYear.HasValue && currentYear < AppearYear.Value)
            return "미등장";

        if (RetireYear.HasValue && currentYear >= RetireYear.Value)
            return "은퇴";

        return "활동중";
    }

    /// <summary>
    /// 그 갈래를 좋아하는지. 갈래 번호는 게임 힌트 표의 것이다 —
    /// 0 지리 · 1 역사 · 2 보물 · 3 종교 · 4 교역품 · 5 미신 · 6 생물 · 7 민족
    /// (이름표 <c>0x00560C60</c>).
    /// </summary>
    public bool Likes(int category) => category switch
    {
        0 => Preferences.Geography,
        1 => Preferences.History,
        2 => Preferences.Treasure,
        3 => Preferences.Religion,
        4 => Preferences.TradeGoods,
        5 => Preferences.Superstition,
        6 => Preferences.Creature,
        7 => Preferences.Ethnicity,
        _ => false,
    };

    public string PreferencesDisplay()
    {
        var prefs = new List<string>();
        if (Preferences.Geography) prefs.Add("지리");
        if (Preferences.History) prefs.Add("역사");
        if (Preferences.Treasure) prefs.Add("보물");
        if (Preferences.Religion) prefs.Add("종교");
        if (Preferences.TradeGoods) prefs.Add("교역");
        if (Preferences.Superstition) prefs.Add("미신");
        if (Preferences.Creature) prefs.Add("생물");
        if (Preferences.Ethnicity) prefs.Add("민족");
        return string.Join(", ", prefs);
    }
}
