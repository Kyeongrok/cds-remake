using System.IO;
using System.Text;
using CdsHelper.Support.Local.Models;

namespace CdsHelper.Support.Local.Helpers;

/// <summary>
/// SAVEDATA.CDS 파일 읽기 서비스
/// </summary>
public class SaveDataService
{
    private readonly CityService _cityService;
    private List<City> _cities = new();

    /// <summary>
    /// 현재 로드된 세이브 게임 정보
    /// </summary>
    public SaveGameInfo? CurrentSaveGameInfo { get; private set; }

    /// <summary>
    /// 현재 로드된 플레이어 데이터
    /// </summary>
    public PlayerData? CurrentPlayerData { get; private set; }

    /// <summary>
    /// 현재 로드된 파일 경로
    /// </summary>
    public string? CurrentFilePath { get; private set; }

    // ── 자리 잡기 ───────────────────────────────────────────────────────────
    // SAVEDATA.CDS 는 [u32 파일길이][ASCIIZ 판 문자열][알맹이] 꼴이고,
    // 알맹이 시작 = 4 + strlen(판 문자열) + 1 이다 (CDS_95.EXE 0x00478B6E).
    // 한국어판은 "1, 0, 0, 10018" 이라 0x13 이지만 판이 다르면 통째로 밀린다.
    // 그래서 파일 자리를 상수로 박지 않고 알맹이 기준(_REL)으로 두고 매번 더한다.
    private const int DEFAULT_BODY_START = 0x13;
    private int _bodyStart = DEFAULT_BODY_START;

    /// <summary>알맹이가 시작하는 파일 자리. 판 문자열 길이에 딸려 움직인다.</summary>
    public int BodyStart => _bodyStart;

    private const int CHARACTER_START_REL = 0x9237;   // 한국어판 파일 0x924A
    private const int CHARACTER_SIZE = 0x90;
    private const int CHARACTER_COUNT = 461;
    private const int YEAR_REL = 0x02;
    private const int MONTH_REL = 0x06;
    private const int DAY_REL = 0x07;

    // 힌트 관련 상수
    private const int HINT_START_REL = 0x1A612;       // 한국어판 파일 0x1A625
    private const int HINT_SIZE = 6;
    private const int HINT_STATUS_OFFSET = 4;  // 6바이트 블록 내 상태 바이트 위치
    private const int HINT_COUNT = 186;

    // 발견물 슬롯 테이블 (행 0 = 발견물 ID 0, 상태 바이트는 행 안 +0x15)
    private const int DISCOVERY_START_REL = 0x1AA6E;  // 한국어판 파일 0x1AA81
    private const int DISCOVERY_SIZE = 164;
    private const int DISCOVERY_STATE_REL = 0x15;
    // 칸 수는 EXE 고리가 못박는다 — 0x61E4C8 에서 0x629898 까지 0xA8 씩 = 274.
    // 즉치값이라 놀이 중에 늘어나지 않는다. DiscoveryTable.Count 와 같은 값이다
    // (Support 는 Game 을 참조할 수 없어 상수로 둔다).
    private const int DISCOVERY_COUNT = 274;

    /// <summary>알맹이 시작 자리를 판 문자열 길이에서 되짚는다.</summary>
    private static int ReadBodyStart(byte[] data)
    {
        int i = 4;
        while (i < data.Length && data[i] != 0) i++;
        int start = i + 1;
        return start < data.Length ? start : DEFAULT_BODY_START;
    }

    private int CharacterOffset(int index) =>
        _bodyStart + CHARACTER_START_REL + (index * CHARACTER_SIZE);

    private int DiscoveryStateOffset(int slotIndex) =>
        _bodyStart + DISCOVERY_START_REL + (slotIndex * DISCOVERY_SIZE) + DISCOVERY_STATE_REL;

    /// <summary>
    /// 발견물 표를 옳게 짚었는지. 아니면 쓰기를 막는다.
    /// </summary>
    public bool DiscoveryLayoutOk { get; private set; }

    /// <summary>
    /// 상태 바이트가 죄다 그럴듯한지 본다. 하위 6비트는 갈래별 밑값
    /// (0x00·0x04·0x08·0x0C), bit6 = 발견, bit7 = 보고다.
    /// </summary>
    private static bool CheckDiscoveryLayout(byte[] data, int start)
    {
        if (start < 0 || start + (DISCOVERY_SIZE * DISCOVERY_COUNT) > data.Length)
            return false;

        bool any = false;
        for (int i = 0; i < DISCOVERY_COUNT; i++)
        {
            byte state = data[start + (i * DISCOVERY_SIZE) + DISCOVERY_STATE_REL];
            if ((state & 0x3F) > 0x0F) return false;                       // 밑값이 아니다
            if ((state & 0x80) != 0 && (state & 0x40) == 0) return false;  // 미발견인데 보고했다
            if (state != 0) any = true;
        }

        return any;   // 274칸이 죄다 0 이면 상태 자리가 아니다
    }

    private static readonly Dictionary<int, string> SkillsMap = new()
    {
        { 1, "항" }, { 2, "운" }, { 3, "검" }, { 4, "포" }, { 5, "사" },
        { 6, "의" }, { 7, "웅" }, { 8, "측" }, { 9, "역" }, { 10, "회" },
        { 11, "조" }, { 12, "신" }, { 13, "과" }, { 14, "스" }, { 15, "갈" },
        { 16, "로" }, { 17, "게" }, { 18, "슬" }, { 19, "랍" }, { 20, "페" },
        { 21, "중" }, { 22, "힌" }, { 23, "위" }, { 24, "아" }, { 25, "미" },
        { 26, "남" }, { 27, "동" },
    };

    public SaveDataService(CityService cityService)
    {
        _cityService = cityService;
    }

    public void SetCities(List<City> cities)
    {
        _cities = cities;
    }

    public SaveGameInfo ReadSaveFile(string filePath)
    {
        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException($"세이브 파일을 찾을 수 없습니다: {filePath}");
        }

        var saveInfo = new SaveGameInfo();
        var data = File.ReadAllBytes(filePath);

        _bodyStart = ReadBodyStart(data);
        DiscoveryLayoutOk = CheckDiscoveryLayout(data, _bodyStart + DISCOVERY_START_REL);

        if (data.Length > _bodyStart + DAY_REL)
        {
            saveInfo.Year = BitConverter.ToUInt16(data, _bodyStart + YEAR_REL);
            saveInfo.Month = data[_bodyStart + MONTH_REL];
            saveInfo.Day = data[_bodyStart + DAY_REL];
        }

        var characters = new List<CharacterData>();

        for (int i = 0; i < CHARACTER_COUNT; i++)
        {
            int offset = CharacterOffset(i);
            if (offset + CHARACTER_SIZE > data.Length)
                break;

            var character = ReadCharacterData(data, offset);

            if (character != null && character.Name != "???")
            {
                character.Index = i;  // 캐릭터 인덱스 저장
                characters.Add(character);
            }
        }

        saveInfo.Characters = characters;

        // 힌트 데이터 읽기
        saveInfo.Hints = ReadHintData(data);

        // 발견물 슬롯 데이터 읽기
        saveInfo.Discoveries = ReadDiscoveryData(data);

        // 현재 로드된 데이터 캐싱
        CurrentSaveGameInfo = saveInfo;
        CurrentFilePath = filePath;
        _backedUpPath = null;

        // CharacterData에서 고용 상태/연령/소재/등장여부/건물 변경 시 저장할 수 있도록 콜백 설정
        CharacterData.OnHireStatusChanged = SaveCharacterHireStatus;
        CharacterData.OnAgeChanged = SaveCharacterAge;
        CharacterData.OnLocationChanged = SaveCharacterLocation;
        CharacterData.OnAvailableChanged = SaveCharacterAvailable;
        CharacterData.OnBuildingChanged = SaveCharacterBuilding;
        CharacterData.OnFameChanged = SaveCharacterFame;

        return saveInfo;
    }

    private string? _backedUpPath;

    /// <summary>
    /// 이 파일에 처음 손대기 전에 한 번만 뒷갈망을 뜬다.
    /// (DisevArchive.Save 와 같은 이름꼴 — 파일이름.yyyyMMdd_HHmmss.bak)
    /// </summary>
    private void BackupOnce()
    {
        if (string.IsNullOrEmpty(CurrentFilePath)) return;
        if (_backedUpPath == CurrentFilePath) return;

        try
        {
            string backup = $"{CurrentFilePath}.{DateTime.Now:yyyyMMdd_HHmmss}.bak";
            if (!File.Exists(backup))
                File.Copy(CurrentFilePath, backup);
            _backedUpPath = CurrentFilePath;
        }
        catch
        {
            // 뒷갈망을 못 떠도 쓰기까지 막지는 않는다
        }
    }

    /// <summary>
    /// 발견물 슬롯의 state 바이트를 세이브 파일에 저장 (bit 6=발견, bit 7=보고)
    /// </summary>
    public void SaveDiscoveryState(int slotIndex, byte state)
    {
        if (string.IsNullOrEmpty(CurrentFilePath) || !File.Exists(CurrentFilePath))
            return;
        if (slotIndex < 0 || slotIndex >= DISCOVERY_COUNT) return;
        if (!DiscoveryLayoutOk) return;   // 자리가 미덥지 않으면 손대지 않는다

        BackupOnce();

        int offset = DiscoveryStateOffset(slotIndex);

        using (var stream = new FileStream(CurrentFilePath, FileMode.Open, FileAccess.Write))
        {
            stream.Seek(offset, SeekOrigin.Begin);
            stream.WriteByte(state);
        }

        // 캐시 동기화
        if (CurrentSaveGameInfo?.Discoveries != null)
        {
            var entry = CurrentSaveGameInfo.Discoveries.FirstOrDefault(d => d.Id == slotIndex);
            if (entry != null) entry.State = state;
        }
    }

    /// <summary>
    /// 캐릭터 고용 상태를 세이브 파일에 저장
    /// </summary>
    public void SaveCharacterHireStatus(int characterIndex, byte hireStatus)
    {
        if (string.IsNullOrEmpty(CurrentFilePath) || !File.Exists(CurrentFilePath))
            return;

        int offset = CharacterOffset(characterIndex) + 0x62;

        using var stream = new FileStream(CurrentFilePath, FileMode.Open, FileAccess.Write);
        stream.Seek(offset, SeekOrigin.Begin);
        stream.WriteByte(hireStatus);
    }

    /// <summary>
    /// 캐릭터 연령을 세이브 파일에 저장
    /// </summary>
    public void SaveCharacterAge(int characterIndex, byte age)
    {
        if (string.IsNullOrEmpty(CurrentFilePath) || !File.Exists(CurrentFilePath))
            return;

        int offset = CharacterOffset(characterIndex) + 0x5C;

        using var stream = new FileStream(CurrentFilePath, FileMode.Open, FileAccess.Write);
        stream.Seek(offset, SeekOrigin.Begin);
        stream.WriteByte(age);
    }

    /// <summary>
    /// 캐릭터 소재를 세이브 파일에 저장
    /// </summary>
    public void SaveCharacterLocation(int characterIndex, byte locationIndex)
    {
        if (string.IsNullOrEmpty(CurrentFilePath) || !File.Exists(CurrentFilePath))
            return;

        int offset = CharacterOffset(characterIndex) + 0x2E;

        using var stream = new FileStream(CurrentFilePath, FileMode.Open, FileAccess.Write);
        stream.Seek(offset, SeekOrigin.Begin);
        stream.WriteByte(locationIndex);
    }

    /// <summary>
    /// 캐릭터 등장 여부를 세이브 파일에 저장
    /// </summary>
    public void SaveCharacterAvailable(int characterIndex, byte available)
    {
        if (string.IsNullOrEmpty(CurrentFilePath) || !File.Exists(CurrentFilePath))
            return;

        int offset = CharacterOffset(characterIndex) + 0x0A;

        using var stream = new FileStream(CurrentFilePath, FileMode.Open, FileAccess.Write);
        stream.Seek(offset, SeekOrigin.Begin);
        stream.WriteByte(available);
    }

    /// <summary>
    /// 캐릭터 건물을 세이브 파일에 저장
    /// </summary>
    public void SaveCharacterBuilding(int characterIndex, byte building)
    {
        if (string.IsNullOrEmpty(CurrentFilePath) || !File.Exists(CurrentFilePath))
            return;

        int offset = CharacterOffset(characterIndex) + 0x30;

        using var stream = new FileStream(CurrentFilePath, FileMode.Open, FileAccess.Write);
        stream.Seek(offset, SeekOrigin.Begin);
        stream.WriteByte(building);
    }

    /// <summary>
    /// 캐릭터 명성을 세이브 파일에 저장
    /// </summary>
    public void SaveCharacterFame(int characterIndex, ushort fame)
    {
        if (string.IsNullOrEmpty(CurrentFilePath) || !File.Exists(CurrentFilePath))
            return;

        int offset = CharacterOffset(characterIndex) + 0x26;

        using var stream = new FileStream(CurrentFilePath, FileMode.Open, FileAccess.Write);
        stream.Seek(offset, SeekOrigin.Begin);
        stream.Write(BitConverter.GetBytes(fame), 0, 2);
    }

    /// <summary>
    /// 힌트 획득 데이터 읽기 (1~186)
    /// </summary>
    private List<HintData> ReadHintData(byte[] data)
    {
        var hints = new List<HintData>();

        for (int i = 0; i < HINT_COUNT; i++)
        {
            int offset = _bodyStart + HINT_START_REL + (i * HINT_SIZE) + HINT_STATUS_OFFSET;
            if (offset >= data.Length)
                break;

            hints.Add(new HintData
            {
                Index = i + 1,
                Value = data[offset]
            });
        }

        return hints;
    }

    /// <summary>
    /// 발견물 슬롯 데이터 읽기 (ID 0~273, 상태 바이트의 bit 6 = 발견, bit 7 = 보고)
    /// </summary>
    private List<DiscoveryData> ReadDiscoveryData(byte[] data)
    {
        var discoveries = new List<DiscoveryData>();

        for (int i = 0; i < DISCOVERY_COUNT; i++)
        {
            int offset = DiscoveryStateOffset(i);
            if (offset >= data.Length)
                break;

            discoveries.Add(new DiscoveryData
            {
                Id = i,
                State = data[offset]
            });
        }

        return discoveries;
    }

    private CharacterData? ReadCharacterData(byte[] data, int offset)
    {
        if (offset + CHARACTER_SIZE > data.Length)
            return null;

        var character = new CharacterData();

        // 이름 추출
        try
        {
            var name1Bytes = new ArraySegment<byte>(data, offset + 0x32, 20);
            var name2Bytes = new ArraySegment<byte>(data, offset + 0x45, 20);

            string name1 = ReadString(name1Bytes);
            string name2 = ReadString(name2Bytes);

            if (!string.IsNullOrEmpty(name1) && !string.IsNullOrEmpty(name2))
                character.Name = $"{name1}·{name2}";
            else if (!string.IsNullOrEmpty(name1))
                character.Name = name1;
            else if (!string.IsNullOrEmpty(name2))
                character.Name = name2;
            else
                character.Name = "???";
        }
        catch
        {
            character.Name = "???";
        }

        // 능력치
        character.HP = data[offset + 0x00];
        character.Intelligence = data[offset + 0x01];
        character.Strength = data[offset + 0x02];
        character.Charm = data[offset + 0x03];
        character.Luck = data[offset + 0x04];
        character.Available = data[offset + 0x0A];

        // 특기
        var skills = new List<string>();
        var rawSkills = new Dictionary<int, byte>();
        for (int i = 0; i < 28; i++)
        {
            int skillOffset = 0x0A + i;
            int skillId = i;
            if (skillOffset < CHARACTER_SIZE)
            {
                byte skillLevel = data[offset + skillOffset];
                if (skillLevel > 0 && SkillsMap.ContainsKey(skillId))
                {
                    skills.Add($"{SkillsMap[skillId]}:{skillLevel}");
                    rawSkills[skillId] = skillLevel;
                }
            }
        }
        character.Skills = string.Join(" ", skills);
        character.RawSkills = rawSkills;

        // 명성
        character.Fame = BitConverter.ToUInt16(data, offset + 0x26);

        // 소재
        byte locationIdx = data[offset + 0x2E];
        character.LocationIndex = locationIdx;
        character.Location = _cityService.GetCityName(locationIdx, _cities);

        // 건물 (도시가 2바이트이므로 0x30)
        character.Building = data[offset + 0x30];

        // 연령
        byte ageRaw = data[offset + 0x5C];
        character.Age = unchecked((sbyte)ageRaw);

        // 고용 상태 (나이 + 6바이트 = 0x62)
        character.HireStatus = data[offset + 0x62];

        // 얼굴
        character.Face = data[offset + 0x60];

        // 성좌
        try
        {
            var constellationBytes = new ArraySegment<byte>(data, offset + 0x70, 20);
            character.Constellation = ReadString(constellationBytes);
        }
        catch
        {
            character.Constellation = "";
        }

        return character;
    }

    private string ReadString(ArraySegment<byte> bytes)
    {
        int nullPos = -1;
        for (int i = 0; i < bytes.Count; i++)
        {
            if (bytes[i] == 0)
            {
                nullPos = i;
                break;
            }
        }

        if (nullPos == 0)
            return "";

        int length = nullPos > 0 ? nullPos : bytes.Count;
        var validBytes = new byte[length];
        Array.Copy(bytes.Array!, bytes.Offset, validBytes, 0, length);

        try
        {
            var encoding = Encoding.GetEncoding(51949);
            return encoding.GetString(validBytes).Trim();
        }
        catch
        {
            return "";
        }
    }

    /// <summary>
    /// 플레이어(주인공) 데이터 읽기
    /// </summary>
    public PlayerData? ReadPlayerData(string filePath)
    {
        if (!File.Exists(filePath))
            return null;

        var data = File.ReadAllBytes(filePath);
        if (data.Length < 0xC0)
            return null;

        // 아래 자리들은 한국어판 파일 자리다. 판이 다르면 알맹이 시작이 옮겨가므로
        // 그만큼 밀어서 읽는다 (1절 참조 — 알맹이 = 4 + strlen(판 문자열) + 1).
        _bodyStart = ReadBodyStart(data);
        int shift = _bodyStart - DEFAULT_BODY_START;
        byte At(int fileOffset) => data[fileOffset + shift];

        var player = new PlayerData();

        // 이름 (0x5F: 이름, 0x72: 성)
        try
        {
            var firstName = ReadString(new ArraySegment<byte>(data, 0x5F + shift, 14));
            var lastName = ReadString(new ArraySegment<byte>(data, 0x72 + shift, 14));
            player.FirstName = firstName;
            player.LastName = lastName;
        }
        catch { }

        // 기능 스킬 (0x38~0x44)
        player.Navigation = At(0x38);      // 항해술
        player.Seamanship = At(0x39);      // 운용술
        player.Swordsmanship = At(0x3A);   // 검술
        player.Gunnery = At(0x3B);         // 포술
        player.Shooting = At(0x3C);        // 사격술
        player.Medicine = At(0x3D);        // 의학
        player.Eloquence = At(0x3E);       // 웅변술
        player.Surveying = At(0x3F);       // 측량술
        player.History = At(0x40);         // 역사학
        player.Accounting = At(0x41);      // 회계
        player.Shipbuilding = At(0x42);    // 조선기술
        player.Theology = At(0x43);        // 신학
        player.Science = At(0x44);         // 과학

        // 언어 스킬 (0x45~0x52)
        player.Spanish = At(0x45);         // 스페인어
        player.Portuguese = At(0x46);      // 포르투갈어
        player.Romance = At(0x47);         // 로망스어
        player.Germanic = At(0x48);        // 게르만어
        player.Slavic = At(0x49);          // 슬라브어
        player.Arabic = At(0x4A);          // 아랍어
        player.Persian = At(0x4B);         // 페르시아어
        player.Chinese = At(0x4C);         // 중국어
        player.Hindi = At(0x4D);           // 힌두어
        player.Uyghur = At(0x4E);          // 위그르어
        player.African = At(0x4F);         // 아프리카어
        player.American = At(0x50);        // 아메리카어
        player.SoutheastAsian = At(0x51);  // 동남아시아어
        player.EastAsian = At(0x52);       // 동아시아어

        // 명성 (0x53-0x54)
        player.Fame = BitConverter.ToUInt16(data, 0x53 + shift);

        // 악명 (0x55-0x56)
        player.Notoriety = BitConverter.ToUInt16(data, 0x55 + shift);

        // 현재 도시 (0x57)
        player.CurrentCity = At(0x57);
        player.CurrentCityName = _cityService.GetCityName(player.CurrentCity, _cities);

        // 동료 (0xA5-0xA8) - 캐릭터 인덱스
        player.Adjutant = At(0xA5);      // 부관
        player.Navigator = At(0xA7);     // 항해사
        player.Surveyor = At(0xA9);      // 측량사
        player.Interpreter = At(0xAB);   // 통역

        // 동료 캐릭터 데이터 조회
        player.AdjutantData = ReadCharacterByIndex(data, player.Adjutant);
        player.NavigatorData = ReadCharacterByIndex(data, player.Navigator);
        player.SurveyorData = ReadCharacterByIndex(data, player.Surveyor);
        player.InterpreterData = ReadCharacterByIndex(data, player.Interpreter);

        // 동료 이름 설정
        player.AdjutantName = player.AdjutantData?.Name ?? "없음";
        player.NavigatorName = player.NavigatorData?.Name ?? "없음";
        player.SurveyorName = player.SurveyorData?.Name ?? "없음";
        player.InterpreterName = player.InterpreterData?.Name ?? "없음";

        // 소지금 (추후 확인 필요)
        // player.Gold = ...;

        // 현재 로드된 플레이어 데이터 캐싱
        CurrentPlayerData = player;

        return player;
    }

    /// <summary>
    /// 캐릭터 인덱스로 캐릭터 데이터 조회
    /// </summary>
    private CharacterData? ReadCharacterByIndex(byte[] data, byte characterIndex)
    {
        // 0 또는 0xFF(255)는 미고용 상태
        if (characterIndex == 0 || characterIndex == 0xFF)
            return null;

        int offset = CharacterOffset(characterIndex);
        if (offset + CHARACTER_SIZE > data.Length)
            return null;

        var character = ReadCharacterData(data, offset);
        if (character != null)
        {
            character.Index = characterIndex;
        }
        return character;
    }
}
