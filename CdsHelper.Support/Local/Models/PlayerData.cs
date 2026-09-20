namespace CdsHelper.Support.Local.Models;

/// <summary>
/// 플레이어(주인공) 데이터
/// </summary>
public class PlayerData
{
    // 기본 정보
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string FullName => string.IsNullOrEmpty(LastName) ? FirstName : $"{FirstName}·{LastName}";

    // 능력치
    public byte Leadership { get; set; }      // 통솔
    public byte Intelligence { get; set; }    // 지력
    public byte Strength { get; set; }        // 무력
    public byte Charm { get; set; }           // 매력
    public byte Luck { get; set; }            // 운

    // 기능 스킬 (1~13)
    public byte Navigation { get; set; }      // 항해술
    public byte Seamanship { get; set; }      // 운용술
    public byte Swordsmanship { get; set; }   // 검술
    public byte Gunnery { get; set; }         // 포술
    public byte Shooting { get; set; }        // 사격술
    public byte Medicine { get; set; }        // 의학
    public byte Eloquence { get; set; }       // 웅변술
    public byte Surveying { get; set; }       // 측량술
    public byte History { get; set; }         // 역사학
    public byte Accounting { get; set; }       // 회계
    public byte Shipbuilding { get; set; }    // 조선술
    public byte Theology { get; set; }        // 신학
    public byte Science { get; set; }         // 과학

    // 언어 스킬 (14~27)
    public byte Spanish { get; set; }         // 스페인어
    public byte Portuguese { get; set; }      // 포르투갈어
    public byte Romance { get; set; }         // 로망스어
    public byte Germanic { get; set; }        // 게르만어
    public byte Slavic { get; set; }          // 슬라브어
    public byte Arabic { get; set; }          // 아랍어
    public byte Persian { get; set; }         // 페르시아어
    public byte Chinese { get; set; }         // 중국어
    public byte Hindi { get; set; }           // 힌두어
    public byte Uyghur { get; set; }          // 위그르어
    public byte African { get; set; }         // 아프리카어
    public byte American { get; set; }        // 아메리카어
    public byte SoutheastAsian { get; set; }  // 동남아시아어
    public byte EastAsian { get; set; }       // 동아시아어

    // 상태
    public ushort Fame { get; set; }          // 명성치
    public ushort Notoriety { get; set; }     // 악명치
    public byte CurrentCity { get; set; }     // 현재 도시
    public string CurrentCityName { get; set; } = string.Empty;

    // 개인 정보
    public byte Face { get; set; }            // 얼굴
    public byte Nationality { get; set; }     // 국적
    public byte Job { get; set; }             // 직업
    public ushort BirthYear { get; set; }     // 생일 년도
    public byte BirthMonth { get; set; }      // 생일 월
    public byte BirthDay { get; set; }        // 생일 일

    // 재산
    public uint Gold { get; set; }            // 소지금
    public uint Savings { get; set; }         // 저금
    public uint Debt { get; set; }            // 빚

    // 동료 (인덱스)
    public byte Adjutant { get; set; }        // 부관
    public byte Navigator { get; set; }       // 항해사
    public byte Surveyor { get; set; }        // 측량사
    public byte Interpreter { get; set; }     // 통역

    // 동료 이름
    public string AdjutantName { get; set; } = "없음";
    public string NavigatorName { get; set; } = "없음";
    public string SurveyorName { get; set; } = "없음";
    public string InterpreterName { get; set; } = "없음";

    // 동료 캐릭터 데이터
    public CharacterData? AdjutantData { get; set; }
    public CharacterData? NavigatorData { get; set; }
    public CharacterData? SurveyorData { get; set; }
    public CharacterData? InterpreterData { get; set; }

    // 아이템 (최대 16개)
    public byte[] Items { get; set; } = new byte[16];

    // 스킬 표시용 딕셔너리
    public Dictionary<string, byte> Skills => new()
    {
        { "항해술", Navigation },
        { "운용술", Seamanship },
        { "검술", Swordsmanship },
        { "포술", Gunnery },
        { "사격술", Shooting },
        { "의학", Medicine },
        { "웅변술", Eloquence },
        { "측량술", Surveying },
        { "역사학", History },
        { "회계", Accounting },
        { "조선술", Shipbuilding },
        { "신학", Theology },
        { "과학", Science },
    };

    public Dictionary<string, byte> Languages => new()
    {
        { "스페인어", Spanish },
        { "포르투갈어", Portuguese },
        { "로망스어", Romance },
        { "게르만어", Germanic },
        { "슬라브어", Slavic },
        { "아랍어", Arabic },
        { "페르시아어", Persian },
        { "중국어", Chinese },
        { "힌두어", Hindi },
        { "위그르어", Uyghur },
        { "아프리카어", African },
        { "아메리카어", American },
        { "동남아시아어", SoutheastAsian },
        { "동아시아어", EastAsian },
    };

    // 스킬 요약 문자열
    public string SkillsSummary
    {
        get
        {
            var skills = Skills.Where(s => s.Value > 0).Select(s => $"{s.Key}:{s.Value}");
            return string.Join(" ", skills);
        }
    }

    public string LanguagesSummary
    {
        get
        {
            var langs = Languages.Where(l => l.Value > 0).Select(l => $"{l.Key}:{l.Value}");
            return string.Join(" ", langs);
        }
    }
}
