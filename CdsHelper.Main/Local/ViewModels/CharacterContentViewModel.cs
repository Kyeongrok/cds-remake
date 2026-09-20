using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CdsHelper.Support.Local.Events;
using CdsHelper.Support.Local.Helpers;
using CdsHelper.Support.Local.Models;
using CdsHelper.Support.Local.Settings;
using Prism.Events;

namespace CdsHelper.Main.Local.ViewModels;

public partial class CharacterContentViewModel : ObservableObject
{
    private readonly CharacterService _characterService;
    private readonly SaveDataService _saveDataService;

    private List<CharacterData> _allCharacters = new();
    private SaveGameInfo? _saveGameInfo;
    private PlayerData? _playerData;
    private bool _hasBackedUpThisSession = false;

    #region Collections

    [ObservableProperty] private ObservableCollection<CharacterData> _characters = new();

    #endregion

    #region Filter Properties

    [ObservableProperty] private bool _showGrayCharacters;
    partial void OnShowGrayCharactersChanged(bool value) => ApplyFilter();

    [ObservableProperty] private bool _showOnlyHirable = true;
    partial void OnShowOnlyHirableChanged(bool value) => ApplyFilter();

    [ObservableProperty] private string _characterNameSearch = "";
    partial void OnCharacterNameSearchChanged(string value) => ApplyFilter();

    [ObservableProperty] private bool _filterBySkill;
    partial void OnFilterBySkillChanged(bool value) => ApplyFilter();

    [ObservableProperty] private int _selectedSkillIndex = 0;
    partial void OnSelectedSkillIndexChanged(int value) { if (FilterBySkill) ApplyFilter(); }

    [ObservableProperty] private byte _selectedSkillLevel = 3;
    partial void OnSelectedSkillLevelChanged(byte value) { if (FilterBySkill) ApplyFilter(); }

    public List<string> SkillNames { get; } = CharacterService.SkillNames.Values.ToList();
    public List<byte> SkillLevels { get; } = new() { 1, 2, 3, 4, 5 };

    #endregion

    #region Status Properties

    [ObservableProperty] private string _statusText = "준비됨";
    [ObservableProperty] private ushort _playerFame;
    [ObservableProperty] private string _filePath = "파일 경로: 없음";
    [ObservableProperty] private string _toastMessage = "";
    [ObservableProperty] private bool _isToastVisible = false;

    #endregion

    public CharacterContentViewModel(
        CharacterService characterService,
        SaveDataService saveDataService,
        IEventAggregator eventAggregator)
    {
        _characterService = characterService;
        _saveDataService = saveDataService;

        CharacterData.OnBeforeFirstHireStatusChange = OnBeforeFirstHireStatusChange;

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
            StatusText = "캐릭터 데이터 로드 중...";
            _saveGameInfo = saveGameInfo;
            _playerData = playerData;
            _allCharacters = _saveGameInfo.Characters;

            PlayerFame = _playerData?.Fame ?? 0;
            foreach (var character in _allCharacters)
                character.PlayerFame = PlayerFame;

            ApplyFilter();
            StatusText = $"캐릭터 로드 완료: {Characters.Count}명";
        }
        catch (Exception ex)
        {
            StatusText = "로드 실패";
            System.Windows.MessageBox.Show($"캐릭터 데이터 로드 실패:\n\n{ex.Message}",
                "오류", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
        }
    }

    private void ApplyFilter()
    {
        if (_allCharacters.Count == 0) return;

        var filtered = _characterService.Filter(
            _allCharacters,
            ShowGrayCharacters,
            string.IsNullOrWhiteSpace(CharacterNameSearch) ? null : CharacterNameSearch,
            FilterBySkill ? SelectedSkillIndex + 1 : null,
            FilterBySkill ? SelectedSkillLevel : null);

        if (ShowOnlyHirable)
            filtered = filtered.Where(c => c.HireStatus == 2 && c.Location != "함대소속").ToList();

        Characters = new ObservableCollection<CharacterData>(filtered);

        var grayCount = _allCharacters.Count(c => c.IsGray);
        var normalCount = _allCharacters.Count - grayCount;
        var hirableCount = _allCharacters.Count(c => c.HireStatus == 2 && c.Location != "함대소속");

        StatusText = $"표시: {filtered.Count}명 (고용가능: {hirableCount}명)";
    }

    [RelayCommand]
    private void Backup()
    {
        var bakFilePath = PerformBackup();
        if (bakFilePath != null)
        {
            System.Windows.MessageBox.Show($"백업 완료:\n{bakFilePath}",
                "백업", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
        }
    }

    private string? PerformBackup()
    {
        try
        {
            var filePath = _saveDataService.CurrentFilePath;
            if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
                return null;

            var directory = Path.GetDirectoryName(filePath);
            var bakDirectory = Path.Combine(directory!, "bak");

            if (!Directory.Exists(bakDirectory))
                Directory.CreateDirectory(bakDirectory);

            var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            var fileName = Path.GetFileNameWithoutExtension(filePath);
            var extension = Path.GetExtension(filePath);
            var bakFileName = $"{fileName}_{timestamp}{extension}";
            var bakFilePath = Path.Combine(bakDirectory, bakFileName);

            File.Copy(filePath, bakFilePath, overwrite: true);

            return bakFilePath;
        }
        catch
        {
            return null;
        }
    }

    private void OnBeforeFirstHireStatusChange()
    {
        if (_hasBackedUpThisSession) return;

        var bakFilePath = PerformBackup();
        if (bakFilePath != null)
        {
            _hasBackedUpThisSession = true;
            ShowToast($"자동 백업 완료: {Path.GetFileName(bakFilePath)}");
        }
    }

    private async void ShowToast(string message)
    {
        ToastMessage = message;
        IsToastVisible = true;

        await Task.Delay(3000);

        IsToastVisible = false;
    }
}
