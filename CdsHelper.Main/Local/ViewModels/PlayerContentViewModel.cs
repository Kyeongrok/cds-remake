using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CdsHelper.Support.Local.Events;
using CdsHelper.Support.Local.Helpers;
using CdsHelper.Support.Local.Models;
using CdsHelper.Support.Local.Settings;
using Prism.Events;

namespace CdsHelper.Main.Local.ViewModels;

public partial class PlayerContentViewModel : ObservableObject
{
    private readonly SaveDataService _saveDataService;
    private readonly HintService _hintService;

    private static readonly Dictionary<int, string> SkillIndexToName = new()
    {
        { 1, "항해술" }, { 2, "운용술" }, { 3, "검술" }, { 4, "포술" }, { 5, "사격술" },
        { 6, "의학" }, { 7, "웅변술" }, { 8, "측량술" }, { 9, "역사학" }, { 10, "회계" },
        { 11, "조선술" }, { 12, "신학" }, { 13, "과학" },
        { 14, "스페인어" }, { 15, "포르투갈어" }, { 16, "로망스어" }, { 17, "게르만어" },
        { 18, "슬라브어" }, { 19, "아랍어" }, { 20, "페르시아어" }, { 21, "중국어" },
        { 22, "힌두어" }, { 23, "위그르어" }, { 24, "아프리카어" }, { 25, "아메리카어" },
        { 26, "동남아시아어" }, { 27, "동아시아어" }
    };

    private List<CharacterData> _allCharacters = new();

    [ObservableProperty] private ObservableCollection<CharacterData> _availableCharacters = new();

    [ObservableProperty] private bool _isSimulationMode;
    partial void OnIsSimulationModeChanged(bool value) => BuildCombinedSkills();

    [ObservableProperty] private CharacterData? _simAdjutant;
    partial void OnSimAdjutantChanged(CharacterData? value) => BuildCombinedSkills();

    [ObservableProperty] private CharacterData? _simNavigator;
    partial void OnSimNavigatorChanged(CharacterData? value) => BuildCombinedSkills();

    [ObservableProperty] private CharacterData? _simSurveyor;
    partial void OnSimSurveyorChanged(CharacterData? value) => BuildCombinedSkills();

    [ObservableProperty] private CharacterData? _simInterpreter;
    partial void OnSimInterpreterChanged(CharacterData? value) => BuildCombinedSkills();

    [ObservableProperty] private PlayerData? _player;
    [ObservableProperty] private CharacterData? _selectedCrewMember;
    [ObservableProperty] private ObservableCollection<SkillDisplayItem> _combinedSkills = new();
    [ObservableProperty] private ObservableCollection<SkillDisplayItem> _combinedLanguages = new();
    [ObservableProperty] private ObservableCollection<HintData> _hints = new();
    [ObservableProperty] private string _hintSummary = "";
    [ObservableProperty] private string _statusText = "세이브 파일을 로드하세요";
    [ObservableProperty] private string _filePath = "";

    public PlayerContentViewModel(
        SaveDataService saveDataService,
        HintService hintService,
        IEventAggregator eventAggregator)
    {
        _saveDataService = saveDataService;
        _hintService = hintService;

        eventAggregator.GetEvent<SaveDataLoadedEvent>().Subscribe(OnSaveDataLoaded);

        if (saveDataService.CurrentSaveGameInfo != null && saveDataService.CurrentPlayerData != null)
            LoadSaveData(saveDataService.CurrentSaveGameInfo, saveDataService.CurrentPlayerData);
    }

    private void OnSaveDataLoaded(SaveDataLoadedEventArgs args)
    {
        if (args.PlayerData != null)
            LoadSaveData(args.SaveGameInfo, args.PlayerData);
    }

    public void LoadSaveData(SaveGameInfo saveGameInfo, PlayerData playerData)
    {
        try
        {
            StatusText = "플레이어 데이터 로드 중...";

            Player = playerData;
            SelectedCrewMember = null;

            _allCharacters = saveGameInfo.Characters
                .Where(c => !c.IsGray)
                .OrderBy(c => c.Name)
                .ToList();

            if (Player != null)
            {
                foreach (var c in _allCharacters)
                    c.PlayerFame = Player.Fame;
            }

            var hirableCharacters = _allCharacters
                .Where(c => c.HireStatus == 2 && c.Location != "함대소속")
                .ToList();
            AvailableCharacters = new ObservableCollection<CharacterData>(hirableCharacters);

            SimAdjutant = _allCharacters.FirstOrDefault(c => c.Name == Player?.AdjutantName);
            SimNavigator = _allCharacters.FirstOrDefault(c => c.Name == Player?.NavigatorName);
            SimSurveyor = _allCharacters.FirstOrDefault(c => c.Name == Player?.SurveyorName);
            SimInterpreter = _allCharacters.FirstOrDefault(c => c.Name == Player?.InterpreterName);

            var hintBookInfo = Task.Run(() => _hintService.GetHintBookInfoAsync()).GetAwaiter().GetResult();
            foreach (var hint in saveGameInfo.Hints)
            {
                hint.Name = _hintService.GetHintName(hint.Index - 1);

                if (hintBookInfo.TryGetValue(hint.Index - 1, out var bookInfo))
                {
                    hint.BookLanguage = bookInfo.Language;
                    hint.BookRequired = bookInfo.Required;
                    hint.BookCondition = bookInfo.Condition;
                }
            }
            Hints = new ObservableCollection<HintData>(saveGameInfo.Hints);
            HintSummary = $"발견: {saveGameInfo.DiscoveredHintCount} / {saveGameInfo.TotalHintCount}";

            if (Player != null)
            {
                BuildCombinedSkills();
                StatusText = $"플레이어 로드 완료: {Player.FullName} (고용가능: {hirableCharacters.Count}명)";
            }
            else
            {
                StatusText = "플레이어 데이터를 읽을 수 없습니다";
            }
        }
        catch (Exception ex)
        {
            StatusText = "로드 실패";
            System.Windows.MessageBox.Show($"플레이어 데이터 로드 실패:\n\n{ex.Message}",
                "오류", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
        }
    }

    [RelayCommand]
    private void ShowCrewMember(string role)
    {
        if (Player == null) return;

        SelectedCrewMember = role switch
        {
            "Adjutant" => Player.AdjutantData,
            "Navigator" => Player.NavigatorData,
            "Surveyor" => Player.SurveyorData,
            "Interpreter" => Player.InterpreterData,
            _ => null
        };
    }

    private void BuildCombinedSkills()
    {
        if (Player == null) return;

        var skills = new ObservableCollection<SkillDisplayItem>();
        var languages = new ObservableCollection<SkillDisplayItem>();

        var adjutant = IsSimulationMode ? SimAdjutant : Player.AdjutantData;
        var navigator = IsSimulationMode ? SimNavigator : Player.NavigatorData;
        var surveyor = IsSimulationMode ? SimSurveyor : Player.SurveyorData;
        var interpreter = IsSimulationMode ? SimInterpreter : Player.InterpreterData;

        for (int i = 1; i <= 13; i++)
        {
            if (!SkillIndexToName.TryGetValue(i, out var skillName)) continue;

            var item = new SkillDisplayItem
            {
                Name = skillName,
                PlayerLevel = GetPlayerSkillLevel(skillName),
                AdjutantLevel = GetCrewSkillLevel(adjutant, i),
                NavigatorLevel = GetCrewSkillLevel(navigator, i),
                SurveyorLevel = GetCrewSkillLevel(surveyor, i),
                InterpreterLevel = GetCrewSkillLevel(interpreter, i)
            };
            skills.Add(item);
        }

        for (int i = 14; i <= 27; i++)
        {
            if (!SkillIndexToName.TryGetValue(i, out var skillName)) continue;

            var item = new SkillDisplayItem
            {
                Name = skillName,
                PlayerLevel = GetPlayerLanguageLevel(skillName),
                AdjutantLevel = GetCrewSkillLevel(adjutant, i),
                NavigatorLevel = GetCrewSkillLevel(navigator, i),
                SurveyorLevel = GetCrewSkillLevel(surveyor, i),
                InterpreterLevel = GetCrewSkillLevel(interpreter, i)
            };
            languages.Add(item);
        }

        CombinedSkills = skills;
        CombinedLanguages = languages;
    }

    private byte GetPlayerSkillLevel(string skillName)
    {
        if (Player?.Skills.TryGetValue(skillName, out var level) == true)
            return level;
        return 0;
    }

    private byte GetPlayerLanguageLevel(string skillName)
    {
        if (Player?.Languages.TryGetValue(skillName, out var level) == true)
            return level;
        return 0;
    }

    private byte GetCrewSkillLevel(CharacterData? crew, int skillIndex)
    {
        if (crew?.RawSkills.TryGetValue(skillIndex, out var level) == true)
            return level;
        return 0;
    }
}
