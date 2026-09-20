using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace CdsHelper.Support.Local.Models;

/// <summary>
/// 도서관 도시 정보 (XAML 바인딩용)
/// </summary>
public class LibraryCityInfo
{
    public byte Id { get; set; }
    public string Name { get; set; } = string.Empty;
}

public class Book
{
    public int Id { get; set; }

    [JsonPropertyName("도서명")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("언어")]
    public string Language { get; set; } = string.Empty;

    [JsonPropertyName("게제 힌트")]
    public string HintText { get; set; } = string.Empty;

    // BookHints 매핑 테이블을 통한 힌트 ID 목록
    public List<int> HintIds { get; set; } = new();

    // 힌트 이름 목록 (표시용)
    public List<string> HintNames { get; set; } = new();

    // 게제 힌트 (힌트 이름들을 쉼표로 구분)
    [JsonIgnore]
    public string Hint => HintNames.Count > 0 ? string.Join(", ", HintNames) : HintText;

    // 발견된 힌트 ID 목록 (플레이어 데이터에서 설정) - IsDiscovered
    [JsonIgnore]
    public HashSet<int>? DiscoveredHintIds { get; set; }

    // 힌트 보유 ID 목록 (플레이어 데이터에서 설정) - HasHint
    [JsonIgnore]
    public HashSet<int>? HasHintIds { get; set; }

    // 발견물 발견 여부 (HintIds 중 하나라도 발견되었으면 true)
    [JsonIgnore]
    public bool IsDiscoveryFound => DiscoveredHintIds != null && HintIds.Any(id => DiscoveredHintIds.Contains(id));

    // 힌트 보유 여부 (HintIds 중 하나라도 힌트를 얻었으면 true)
    [JsonIgnore]
    public bool IsHintObtained => HasHintIds != null && HintIds.Any(id => HasHintIds.Contains(id));

    [JsonPropertyName("필요")]
    public string Required { get; set; } = string.Empty;

    [JsonPropertyName("개제조건")]
    public string Condition { get; set; } = string.Empty;

    // 소재 도서관 도시 ID 목록 (정규화된 데이터)
    public List<byte> LibraryCityIds { get; set; } = new();

    // 소재 도서관 도시명 목록 (표시용)
    public List<string> LibraryCityNames { get; set; } = new();

    // 소재 도서관 도시 목록 (ID와 이름 쌍) - XAML 바인딩용
    [JsonIgnore]
    public List<LibraryCityInfo> LibraryCities
    {
        get
        {
            var result = new List<LibraryCityInfo>();
            for (var i = 0; i < Math.Min(LibraryCityIds.Count, LibraryCityNames.Count); i++)
            {
                result.Add(new LibraryCityInfo { Id = LibraryCityIds[i], Name = LibraryCityNames[i] });
            }
            return result;
        }
    }

    // 기존 호환성용 (쉼표 구분 문자열)
    [JsonPropertyName("소재 도서관")]
    public string Library
    {
        get => string.Join(", ", LibraryCityNames);
        set { } // JSON 역직렬화용 (무시)
    }

    // 플레이어 스킬 데이터 (읽기 가능 여부 판단용)
    [JsonIgnore]
    public Dictionary<string, byte>? PlayerSkills { get; set; }

    [JsonIgnore]
    public Dictionary<string, byte>? PlayerLanguages { get; set; }

    /// <summary>
    /// 읽기 가능 여부: 언어 레벨 3 이상 + 필요 스킬 충족
    /// </summary>
    [JsonIgnore]
    public bool CanRead
    {
        get
        {
            if (PlayerLanguages == null || PlayerSkills == null)
                return true; // 플레이어 데이터 없으면 기본 표시

            // 언어 체크 (3레벨 이상)
            if (!string.IsNullOrEmpty(Language))
            {
                if (!PlayerLanguages.TryGetValue(Language, out var langLevel) || langLevel < 3)
                    return false;
            }

            // 필요 스킬 체크
            if (!string.IsNullOrEmpty(Required))
            {
                // "역사학 2", "항해술 1" 등의 형식 파싱
                var match = Regex.Match(Required, @"(.+?)\s*(\d+)");
                if (match.Success)
                {
                    var skillName = match.Groups[1].Value.Trim();
                    var requiredLevel = byte.Parse(match.Groups[2].Value);

                    if (!PlayerSkills.TryGetValue(skillName, out var playerLevel) || playerLevel < requiredLevel)
                        return false;
                }
            }

            return true;
        }
    }
}
