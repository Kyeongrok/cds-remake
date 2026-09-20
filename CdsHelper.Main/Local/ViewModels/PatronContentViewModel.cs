using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CdsHelper.Support.Local.Helpers;
using CdsHelper.Support.Local.Models;
using CdsHelper.Support.Local.Settings;

namespace CdsHelper.Main.Local.ViewModels;

public partial class PatronContentViewModel : ObservableObject
{
    private readonly PatronService _patronService;
    private readonly SaveDataService _saveDataService;

    private List<Patron> _allPatrons = new();
    private SaveGameInfo? _saveGameInfo;

    #region Collections

    [ObservableProperty] private ObservableCollection<PatronDisplay> _patrons = new();

    #endregion

    #region Filter Properties

    [ObservableProperty] private string _patronNameSearch = "";
    partial void OnPatronNameSearchChanged(string value) => ApplyFilter();

    [ObservableProperty] private string _patronCitySearch = "";
    partial void OnPatronCitySearchChanged(string value) => ApplyFilter();

    [ObservableProperty] private string? _selectedNationality;
    partial void OnSelectedNationalityChanged(string? value) => ApplyFilter();

    [ObservableProperty] private bool _activePatronsOnly = true;
    partial void OnActivePatronsOnlyChanged(bool value) => ApplyFilter();

    public ObservableCollection<string> Nationalities { get; } = new();

    #endregion

    #region Status Properties

    [ObservableProperty] private string _statusText = "준비됨";

    public int CurrentYear => _saveGameInfo?.Year ?? 1480;

    #endregion

    public PatronContentViewModel(
        PatronService patronService,
        SaveDataService saveDataService)
    {
        _patronService = patronService;
        _saveDataService = saveDataService;

        Initialize();
    }

    private void Initialize()
    {
        var basePath = AppDomain.CurrentDomain.BaseDirectory;
        var patronsPath = System.IO.Path.Combine(basePath, "patrons.json");

        if (System.IO.File.Exists(patronsPath))
            LoadPatrons(patronsPath);

        var savePath = @"C:\Users\ocean\Desktop\대항해시대3\savedata.cds";
        if (System.IO.File.Exists(savePath))
            LoadSaveFile(savePath);
    }

    private void LoadPatrons(string filePath)
    {
        try
        {
            _allPatrons = _patronService.LoadPatrons(filePath);

            Nationalities.Clear();
            foreach (var nat in _patronService.GetDistinctNationalities(_allPatrons))
                Nationalities.Add(nat);

            ApplyFilter();
            StatusText = $"후원자 로드 완료: {_allPatrons.Count}명";
        }
        catch (Exception ex)
        {
            StatusText = "후원자 로드 실패";
            System.Windows.MessageBox.Show($"후원자 파일 읽기 실패:\n\n{ex.Message}",
                "오류", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
        }
    }

    [RelayCommand]
    private void LoadSave()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "세이브 파일 (*.CDS)|*.CDS|모든 파일 (*.*)|*.*",
            Title = "세이브 파일 선택"
        };

        if (dialog.ShowDialog() == true)
            LoadSaveFile(dialog.FileName);
    }

    public void LoadSaveFile(string filePath)
    {
        try
        {
            _saveGameInfo = _saveDataService.ReadSaveFile(filePath);
            OnPropertyChanged(nameof(CurrentYear));
            ApplyFilter();
        }
        catch (Exception ex)
        {
            StatusText = "로드 실패";
            System.Windows.MessageBox.Show($"파일 읽기 실패:\n\n{ex.Message}",
                "오류", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
        }
    }

    private void ApplyFilter()
    {
        if (_allPatrons.Count == 0) return;

        var filtered = _patronService.Filter(
            _allPatrons,
            string.IsNullOrWhiteSpace(PatronNameSearch) ? null : PatronNameSearch,
            string.IsNullOrWhiteSpace(PatronCitySearch) ? null : PatronCitySearch,
            SelectedNationality,
            ActivePatronsOnly,
            CurrentYear);

        int playerFame = _saveDataService.CurrentPlayerData?.Fame ?? 0;
        var displayList = _patronService.ToDisplayList(filtered, CurrentYear, playerFame);
        Patrons = new ObservableCollection<PatronDisplay>(displayList);
        var fameMsg = playerFame > 0 ? $", 내 명성: {playerFame}" : "";
        StatusText = $"후원자: {displayList.Count}명 (기준년도: {CurrentYear}{fameMsg})";
    }

    [RelayCommand]
    private void ResetFilter()
    {
        PatronNameSearch = "";
        PatronCitySearch = "";
        SelectedNationality = null;
        ActivePatronsOnly = false;
    }
}
