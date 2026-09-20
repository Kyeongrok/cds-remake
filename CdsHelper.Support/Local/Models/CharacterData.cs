using System.ComponentModel;

namespace CdsHelper.Support.Local.Models;

/// <summary>
/// 캐릭터 데이터 모델
/// </summary>
public class CharacterData : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>
    /// 고용 상태 변경 전 호출되는 콜백 (첫 변경 시 백업용)
    /// </summary>
    public static Action? OnBeforeFirstHireStatusChange { get; set; }

    /// <summary>
    /// 고용 상태 변경 시 호출되는 콜백 (characterIndex, hireStatus)
    /// </summary>
    public static Action<int, byte>? OnHireStatusChanged { get; set; }

    /// <summary>
    /// 연령 변경 시 호출되는 콜백 (characterIndex, age)
    /// </summary>
    public static Action<int, byte>? OnAgeChanged { get; set; }

    /// <summary>
    /// 소재 변경 시 호출되는 콜백 (characterIndex, locationIndex)
    /// </summary>
    public static Action<int, byte>? OnLocationChanged { get; set; }

    /// <summary>
    /// 등장 여부 변경 시 호출되는 콜백 (characterIndex, available)
    /// </summary>
    public static Action<int, byte>? OnAvailableChanged { get; set; }

    /// <summary>
    /// 건물 변경 시 호출되는 콜백 (characterIndex, building)
    /// </summary>
    public static Action<int, byte>? OnBuildingChanged { get; set; }

    /// <summary>
    /// 명성 변경 시 호출되는 콜백 (characterIndex, fame)
    /// </summary>
    public static Action<int, ushort>? OnFameChanged { get; set; }

    protected void OnPropertyChanged(string propertyName)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    public int Index { get; set; }  // 캐릭터 인덱스 (세이브 파일 내 순서)
    public string Name { get; set; } = "";
    public byte HP { get; set; }
    public byte Intelligence { get; set; }
    public byte Strength { get; set; }
    public byte Charm { get; set; }
    public byte Luck { get; set; }
    public string Skills { get; set; } = "";

    // 언어 특기 목록 (축약형)
    // 스=스페인어, 갈=포르투갈어, 로=로망스어, 게=게르만어, 슬=슬라브어
    // 랍=아랍어, 페=페르시아어, 중=중국어, 힌=힌두어, 위=위그르어
    // 아=아프리카어, 미=아메리카어, 남=동남아시아어, 동=동아시아어
    private static readonly HashSet<string> LanguageSkills = new()
    {
        "스", "갈", "로", "게", "슬", "랍", "페", "중", "힌", "위", "아", "미", "남", "동"
    };

    /// <summary>
    /// 일반 특기만 (항해술, 포술 등)
    /// </summary>
    public string GeneralSkills => string.Join(" ", Skills.Split(' ')
        .Where(s => !string.IsNullOrEmpty(s) && !IsLanguageSkill(s)));

    /// <summary>
    /// 언어 특기만 (스페인어, 중국어 등)
    /// </summary>
    public string LanguageSkillsText => string.Join(" ", Skills.Split(' ')
        .Where(s => !string.IsNullOrEmpty(s) && IsLanguageSkill(s)));

    /// <summary>
    /// 언어 특기인지 확인 (예: "스:3" -> "스"가 언어목록에 있는지)
    /// </summary>
    private static bool IsLanguageSkill(string skill)
    {
        var skillName = skill.Split(':')[0];
        return LanguageSkills.Contains(skillName);
    }
    private ushort _fame;
    public ushort Fame
    {
        get => _fame;
        set
        {
            if (_fame != value)
            {
                OnBeforeFirstHireStatusChange?.Invoke();
                _fame = value;
                OnPropertyChanged(nameof(Fame));
                OnPropertyChanged(nameof(CanRecruit));
                OnFameChanged?.Invoke(Index, _fame);
            }
        }
    }

    private byte _locationIndex;
    public byte LocationIndex
    {
        get => _locationIndex;
        set
        {
            if (_locationIndex != value)
            {
                OnBeforeFirstHireStatusChange?.Invoke();
                _locationIndex = value;
                Location = LocationIndexToName(_locationIndex);
                OnPropertyChanged(nameof(LocationIndex));
                OnPropertyChanged(nameof(Location));
                OnPropertyChanged(nameof(LocationSelectIndex));
                OnLocationChanged?.Invoke(Index, _locationIndex);
            }
        }
    }

    public string Location { get; set; } = "";

    /// <summary>
    /// 소재 선택 인덱스 (ComboBox용): 0=리스본, 1=세빌리아, 2=함대소속
    /// </summary>
    public int LocationSelectIndex
    {
        get => _locationIndex switch
        {
            0 => 0,    // 리스본
            7 => 1,    // 세빌리아
            255 => 2,  // 함대소속
            _ => -1    // 기타 (선택 안됨)
        };
        set
        {
            var newIndex = value switch
            {
                0 => (byte)0,    // 리스본
                1 => (byte)7,    // 세빌리아
                2 => (byte)255,  // 함대소속
                _ => _locationIndex
            };
            if (_locationIndex != newIndex)
            {
                LocationIndex = newIndex;
            }
        }
    }

    private static string LocationIndexToName(byte index) => index switch
    {
        0 => "리스본",
        7 => "세빌리아",
        255 => "함대소속",
        _ => $"기타({index})"
    };

    public byte Face { get; set; }

    private byte _building;
    public byte Building
    {
        get => _building;
        set
        {
            if (_building != value)
            {
                OnBeforeFirstHireStatusChange?.Invoke();
                _building = value;
                OnPropertyChanged(nameof(Building));
                OnPropertyChanged(nameof(BuildingName));
                OnBuildingChanged?.Invoke(Index, _building);
            }
        }
    }

    public string BuildingName => Building switch
    {
        4 => "주점",
        5 => "여관",
        _ => Building.ToString()
    };

    /// <summary>
    /// 건물 선택 인덱스 (ComboBox용): 0=주점, 1=여관
    /// </summary>
    public int BuildingSelectIndex
    {
        get => Building switch
        {
            4 => 0,  // 주점
            5 => 1,  // 여관
            _ => -1
        };
        set
        {
            var newBuilding = value switch
            {
                0 => (byte)4,  // 주점
                1 => (byte)5,  // 여관
                _ => _building
            };
            if (_building != newBuilding)
            {
                Building = newBuilding;
            }
        }
    }

    private sbyte _age;
    public sbyte Age
    {
        get => _age;
        set
        {
            if (_age != value)
            {
                // 변경 전 콜백 (첫 변경 시 백업용)
                OnBeforeFirstHireStatusChange?.Invoke();

                _age = value;
                OnPropertyChanged(nameof(Age));
                OnPropertyChanged(nameof(IsGray));

                // 세이브 파일에 저장
                OnAgeChanged?.Invoke(Index, unchecked((byte)_age));
            }
        }
    }

    public string Constellation { get; set; } = "";

    private byte _available;
    public byte Available
    {
        get => _available;
        set
        {
            if (_available != value)
            {
                OnBeforeFirstHireStatusChange?.Invoke();
                _available = value;
                OnPropertyChanged(nameof(Available));
                OnAvailableChanged?.Invoke(Index, _available);
            }
        }
    }

    /// <summary>
    /// 고용 상태: 1=대화만, 2=고용가능, 3=고용완료
    /// </summary>
    private byte _hireStatus;
    public byte HireStatus
    {
        get => _hireStatus;
        set
        {
            if (_hireStatus != value)
            {
                _hireStatus = value;
                OnPropertyChanged(nameof(HireStatus));
                OnPropertyChanged(nameof(HireStatusText));
            }
        }
    }

    /// <summary>
    /// 고용 상태 표시 텍스트
    /// </summary>
    public string HireStatusText => HireStatus switch
    {
        1 => "대화만",
        2 => "고용가능",
        3 => "고용중",
        _ => "-"
    };

    /// <summary>
    /// 고용 상태 인덱스 (ComboBox용): 0=대화만, 1=고용가능, 2=고용중
    /// </summary>
    public int HireStatusIndex
    {
        get => HireStatus - 1;
        set
        {
            var newStatus = (byte)(value + 1);
            if (_hireStatus != newStatus)
            {
                // 변경 전 콜백 (첫 변경 시 백업용)
                OnBeforeFirstHireStatusChange?.Invoke();

                _hireStatus = newStatus;
                OnPropertyChanged(nameof(HireStatus));
                OnPropertyChanged(nameof(HireStatusIndex));
                OnPropertyChanged(nameof(HireStatusText));

                // 세이브 파일에 저장
                OnHireStatusChanged?.Invoke(Index, _hireStatus);
            }
        }
    }

    /// <summary>
    /// 특기별 레벨 (특기 인덱스 -> 레벨)
    /// </summary>
    public Dictionary<int, byte> RawSkills { get; set; } = new();

    /// <summary>
    /// 회색 표시 여부: 18세 미만 또는 60세 초과
    /// </summary>
    public bool IsGray => Age < 18 || Age > 60;

    /// <summary>
    /// 플레이어 명성 (비교용)
    /// </summary>
    public ushort PlayerFame { get; set; }

    /// <summary>
    /// 고용 가능 여부: 캐릭터 명성이 플레이어 명성 이하
    /// </summary>
    public bool CanRecruit => Fame <= PlayerFame;

    /// <summary>
    /// 고용 상태 수정 가능 여부: 고용중(3)이 아닐 때만 수정 가능
    /// </summary>
    public bool CanEditHireStatus => HireStatus != 3;

    /// <summary>
    /// 특정 특기가 특정 레벨인지 확인
    /// </summary>
    public bool HasSkill(int skillIndex, byte level)
    {
        return RawSkills.TryGetValue(skillIndex, out var skillLevel) && skillLevel == level;
    }
}
