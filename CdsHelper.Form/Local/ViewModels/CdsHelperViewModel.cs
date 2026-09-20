using System.Collections.ObjectModel;
using System.Net.Http;
using CdsHelper.Form.Local.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CdsHelper.Support.Local.Events;
using CdsHelper.Support.Local.Helpers;
using CdsHelper.Support.Local.Models;
using CdsHelper.Support.Local.Settings;
using Prism.Events;

namespace CdsHelper.Form.Local.ViewModels;

public partial class CdsHelperViewModel : ObservableObject
{
    private readonly IRegionManager _regionManager;
    private readonly IEventAggregator _eventAggregator;
    private readonly CharacterService _characterService;
    private readonly BookService _bookService;
    private readonly CityService _cityService;
    private readonly PatronService _patronService;
    private readonly FigureheadService _figureheadService;
    private readonly ItemService _itemService;
    private readonly SaveDataService _saveDataService;
    private readonly HintService _hintService;
    private readonly DiscoveryService _discoveryService;
    private readonly UpdateService _updateService;

    // Raw data
    private List<CharacterData> _allCharacters = new();
    private List<Book> _allBooks = new();
    private List<City> _allCities = new();
    private List<Patron> _allPatrons = new();
    private List<Figurehead> _allFigureheads = new();
    private List<Item> _allItems = new();
    private SaveGameInfo? _saveGameInfo;

    #region Collections for DataGrid

    [ObservableProperty] private ObservableCollection<CharacterData> _characters = new();
    [ObservableProperty] private ObservableCollection<Book> _books = new();
    [ObservableProperty] private ObservableCollection<City> _cities = new();
    [ObservableProperty] private ObservableCollection<PatronDisplay> _patrons = new();
    [ObservableProperty] private ObservableCollection<Figurehead> _figureheads = new();
    [ObservableProperty] private ObservableCollection<Item> _items = new();

    #endregion

    #region Character Filter Properties

    [ObservableProperty] private bool _showGrayCharacters;
    partial void OnShowGrayCharactersChanged(bool value) => ApplyCharacterFilter();

    [ObservableProperty] private string _characterNameSearch = "";
    partial void OnCharacterNameSearchChanged(string value) => ApplyCharacterFilter();

    [ObservableProperty] private bool _filterBySkill;
    partial void OnFilterBySkillChanged(bool value) => ApplyCharacterFilter();

    [ObservableProperty] private int _selectedSkillIndex = 0;
    partial void OnSelectedSkillIndexChanged(int value) { if (FilterBySkill) ApplyCharacterFilter(); }

    [ObservableProperty] private byte _selectedSkillLevel = 3;
    partial void OnSelectedSkillLevelChanged(byte value) { if (FilterBySkill) ApplyCharacterFilter(); }

    public List<string> SkillNames { get; } = CharacterService.SkillNames.Values.ToList();
    public List<byte> SkillLevels { get; } = new() { 1, 2, 3, 4, 5 };

    #endregion

    #region Book Filter Properties

    [ObservableProperty] private string _bookNameSearch = "";
    partial void OnBookNameSearchChanged(string value) => ApplyBookFilter();

    [ObservableProperty] private string _librarySearch = "";
    partial void OnLibrarySearchChanged(string value) => ApplyBookFilter();

    [ObservableProperty] private string _hintSearch = "";
    partial void OnHintSearchChanged(string value) => ApplyBookFilter();

    [ObservableProperty] private string? _selectedLanguage;
    partial void OnSelectedLanguageChanged(string? value) => ApplyBookFilter();

    [ObservableProperty] private string? _selectedRequiredSkill;
    partial void OnSelectedRequiredSkillChanged(string? value) => ApplyBookFilter();

    public ObservableCollection<string> Languages { get; } = new();
    public ObservableCollection<string> RequiredSkills { get; } = new();

    #endregion

    #region City Filter Properties

    [ObservableProperty] private string _cityNameSearch = "";
    partial void OnCityNameSearchChanged(string value) => ApplyCityFilter();

    [ObservableProperty] private string? _selectedCulturalSphere;
    partial void OnSelectedCulturalSphereChanged(string? value) => ApplyCityFilter();

    [ObservableProperty] private bool _libraryOnly;
    partial void OnLibraryOnlyChanged(bool value) => ApplyCityFilter();

    [ObservableProperty] private bool _shipyardOnly;
    partial void OnShipyardOnlyChanged(bool value) => ApplyCityFilter();

    public ObservableCollection<string> CulturalSpheres { get; } = new();

    [ObservableProperty] private City? _selectedCity;

    [ObservableProperty] private bool _groupByCulturalSphere;
    partial void OnGroupByCulturalSphereChanged(bool value) => UpdateCityGrouping();

    [ObservableProperty] private bool _groupByLibrary;
    partial void OnGroupByLibraryChanged(bool value) => UpdateCityGrouping();

    [ObservableProperty] private string? _cityGroupPropertyNames;
    [ObservableProperty] private bool _isCityGroupingEnabled;

    private void UpdateCityGrouping()
    {
        var props = new List<string>();
        if (GroupByCulturalSphere) props.Add("CulturalSphere");
        if (GroupByLibrary) props.Add("HasLibraryDisplay");

        CityGroupPropertyNames = props.Count > 0 ? string.Join(",", props) : null;
        IsCityGroupingEnabled = props.Count > 0;
    }

    #endregion

    #region Patron Filter Properties

    [ObservableProperty] private string _patronNameSearch = "";
    partial void OnPatronNameSearchChanged(string value) => ApplyPatronFilter();

    [ObservableProperty] private string _patronCitySearch = "";
    partial void OnPatronCitySearchChanged(string value) => ApplyPatronFilter();

    [ObservableProperty] private string? _selectedNationality;
    partial void OnSelectedNationalityChanged(string? value) => ApplyPatronFilter();

    [ObservableProperty] private bool _activePatronsOnly;
    partial void OnActivePatronsOnlyChanged(bool value) => ApplyPatronFilter();

    public ObservableCollection<string> Nationalities { get; } = new();

    #endregion

    #region Figurehead Filter Properties

    [ObservableProperty] private string _figureheadNameSearch = "";
    partial void OnFigureheadNameSearchChanged(string value) => ApplyFigureheadFilter();

    [ObservableProperty] private string? _selectedFigureheadFunction;
    partial void OnSelectedFigureheadFunctionChanged(string? value) => ApplyFigureheadFilter();

    [ObservableProperty] private string? _selectedFigureheadLevel;
    partial void OnSelectedFigureheadLevelChanged(string? value) => ApplyFigureheadFilter();

    public ObservableCollection<string> FigureheadFunctions { get; } = new();
    public ObservableCollection<string> FigureheadLevels { get; } = new();

    #endregion

    #region Item Filter Properties

    [ObservableProperty] private string _itemNameSearch = "";
    partial void OnItemNameSearchChanged(string value) => ApplyItemFilter();

    [ObservableProperty] private string? _selectedItemCategory;
    partial void OnSelectedItemCategoryChanged(string? value) => ApplyItemFilter();

    [ObservableProperty] private string _itemDiscoverySearch = "";
    partial void OnItemDiscoverySearchChanged(string value) => ApplyItemFilter();

    public ObservableCollection<string> ItemCategories { get; } = new();

    #endregion

    #region Status Properties

    [ObservableProperty] private string _statusText = "준비됨";
    [ObservableProperty] private string _filePath = "파일 경로: 없음";
    [ObservableProperty] private string _windowTitle = "대항해시대3 세이브 뷰어";

    public int CurrentYear => _saveGameInfo?.Year ?? 1480;

    #endregion

    public CdsHelperViewModel(
        IRegionManager regionManager,
        IEventAggregator eventAggregator,
        CharacterService characterService,
        BookService bookService,
        CityService cityService,
        PatronService patronService,
        FigureheadService figureheadService,
        ItemService itemService,
        SaveDataService saveDataService,
        HintService hintService,
        DiscoveryService discoveryService,
        UpdateService updateService)
    {
        _regionManager = regionManager;
        _eventAggregator = eventAggregator;
        _characterService = characterService;
        _bookService = bookService;
        _cityService = cityService;
        _patronService = patronService;
        _figureheadService = figureheadService;
        _itemService = itemService;
        _saveDataService = saveDataService;
        _hintService = hintService;
        _discoveryService = discoveryService;
        _updateService = updateService;

        // 5분마다 세이브 파일 자동 새로고침
        var autoRefreshTimer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromMinutes(5)
        };
        autoRefreshTimer.Tick += (_, _) => RefreshSaveFile();
        autoRefreshTimer.Start();

        Initialize();
    }

    private async void Initialize()
    {
        var basePath = AppDomain.CurrentDomain.BaseDirectory;
        var dbPath = System.IO.Path.Combine(basePath, "cdshelper.db");
        var isNewDb = !System.IO.File.Exists(dbPath);

        if (isNewDb)
            StatusText = "DB 초기화 중...";

        var citiesPath = System.IO.Path.Combine(basePath, "cities.json");
        var hintPath = System.IO.Path.Combine(basePath, "hint.json");
        var bundledDiscoveryJsonPath = System.IO.Path.Combine(basePath, "발견물.json");
        var userDiscoveryJsonPath = AppSettings.DiscoveryFilePath;
        var booksPath = System.IO.Path.Combine(basePath, "books.json");
        var patronsPath = System.IO.Path.Combine(basePath, "patrons.json");
        var figureheadsPath = System.IO.Path.Combine(basePath, "figurehead.json");
        var itemsPath = System.IO.Path.Combine(basePath, "item.json");

        try
        {
            StatusText = "도시 데이터 로드 중...";
            await _cityService.InitializeAsync(dbPath, citiesPath);
            await LoadCitiesFromDbAsync();

            // HintService는 BookService보다 먼저 (BookHints가 Hints를 참조)
            StatusText = "힌트 데이터 로드 중...";
            await _hintService.InitializeAsync(dbPath, hintPath);

            StatusText = "발견물 데이터 로드 중...";
            await _discoveryService.InitializeAsync(dbPath, bundledDiscoveryJsonPath, userDiscoveryJsonPath);

            StatusText = "도서 데이터 로드 중...";
            await _bookService.InitializeAsync(dbPath, booksPath);
            await LoadBooksFromDbAsync();

            if (System.IO.File.Exists(patronsPath))
                LoadPatrons(patronsPath);

            if (System.IO.File.Exists(figureheadsPath))
                LoadFigureheads(figureheadsPath);

            if (System.IO.File.Exists(itemsPath))
                LoadItems(itemsPath);

            if (!string.IsNullOrEmpty(AppSettings.LastSaveFilePath) &&
                System.IO.File.Exists(AppSettings.LastSaveFilePath))
            {
                LoadSaveFile(AppSettings.LastSaveFilePath);
            }
            else
            {
                StatusText = isNewDb ? "DB 초기화 완료" : "준비됨";
            }
        }
        catch (Exception ex)
        {
            StatusText = "초기화 실패";
            System.Windows.MessageBox.Show($"초기화 오류:\n{ex.Message}", "오류",
                System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
        }
    }

    private async Task LoadCitiesFromDbAsync()
    {
        try
        {
            _allCities = await _cityService.LoadCitiesFromDbAsync();

            CulturalSpheres.Clear();
            CulturalSpheres.Add("전체");
            foreach (var cs in _cityService.GetDistinctCulturalSpheres(_allCities))
                CulturalSpheres.Add(cs);
            SelectedCulturalSphere = "전체";

            ApplyCityFilter();
            StatusText = $"도시 로드 완료: {_allCities.Count}개";
        }
        catch (Exception ex)
        {
            StatusText = "도시 로드 실패";
            System.Windows.MessageBox.Show($"도시 로드 실패:\n\n{ex.Message}",
                "오류", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
        }
    }

    private async Task LoadBooksFromDbAsync()
    {
        try
        {
            _allBooks = await _bookService.LoadBooksFromDbAsync();

            Languages.Clear();
            foreach (var lang in _bookService.GetDistinctLanguages(_allBooks))
                Languages.Add(lang);

            RequiredSkills.Clear();
            foreach (var skill in _bookService.GetDistinctRequiredSkills(_allBooks))
                RequiredSkills.Add(skill);

            ApplyBookFilter();
            StatusText = $"도서 로드 완료: {_allBooks.Count}개";
        }
        catch (Exception ex)
        {
            StatusText = "도서 로드 실패";
            System.Windows.MessageBox.Show($"도서 로드 실패:\n\n{ex.Message}",
                "오류", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
        }
    }

    #region Load Methods

    [RelayCommand]
    private void LoadSave()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "세이브 파일 (SAVEDATA.CDS)|SAVEDATA.CDS",
            Title = "세이브 파일 선택"
        };

        if (dialog.ShowDialog() == true)
            LoadSaveFile(dialog.FileName);
    }

    [RelayCommand(CanExecute = nameof(CanRefreshSaveFile))]
    private void RefreshSave() => RefreshSaveFile();

    public void LoadSaveFile(string filePath)
    {
        try
        {
            StatusText = "파일 읽는 중...";
            _saveDataService.SetCities(_allCities);
            _saveGameInfo = _saveDataService.ReadSaveFile(filePath);
            var playerData = _saveDataService.ReadPlayerData(filePath);
            _allCharacters = _saveGameInfo.Characters;

            FilePath = $"파일 경로: {filePath}";
            WindowTitle = $"대항해시대3 세이브 뷰어 - {_saveGameInfo.DateString}";

            AppSettings.LastSaveFilePath = filePath;

            ApplyCharacterFilter();
            ApplyPatronFilter();

            _eventAggregator.GetEvent<SaveDataLoadedEvent>().Publish(new SaveDataLoadedEventArgs
            {
                SaveGameInfo = _saveGameInfo,
                PlayerData = playerData,
                FilePath = filePath
            });

            StatusText = $"로드 완료: {_saveGameInfo.DateString}";
            RefreshSaveCommand.NotifyCanExecuteChanged();
        }
        catch (Exception ex)
        {
            StatusText = "로드 실패";
            System.Windows.MessageBox.Show($"파일 읽기 실패:\n\n{ex.Message}",
                "오류", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
        }
    }

    private bool CanRefreshSaveFile()
    {
        return !string.IsNullOrEmpty(AppSettings.LastSaveFilePath) &&
               System.IO.File.Exists(AppSettings.LastSaveFilePath);
    }

    private void RefreshSaveFile()
    {
        if (CanRefreshSaveFile())
            LoadSaveFile(AppSettings.LastSaveFilePath!);
    }

    public void LoadPatrons(string filePath)
    {
        try
        {
            _allPatrons = _patronService.LoadPatrons(filePath);

            Nationalities.Clear();
            foreach (var nat in _patronService.GetDistinctNationalities(_allPatrons))
                Nationalities.Add(nat);

            ApplyPatronFilter();
            StatusText = $"후원자 로드 완료: {_allPatrons.Count}명";
        }
        catch (Exception ex)
        {
            StatusText = "후원자 로드 실패";
            System.Windows.MessageBox.Show($"후원자 파일 읽기 실패:\n\n{ex.Message}",
                "오류", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
        }
    }

    public void LoadFigureheads(string filePath)
    {
        try
        {
            _allFigureheads = _figureheadService.LoadFigureheads(filePath);

            FigureheadFunctions.Clear();
            foreach (var func in _figureheadService.GetDistinctFunctions())
                FigureheadFunctions.Add(func);

            FigureheadLevels.Clear();
            foreach (var level in _figureheadService.GetDistinctLevels())
                FigureheadLevels.Add(level);

            ApplyFigureheadFilter();
            StatusText = $"선수상 로드 완료: {_allFigureheads.Count}개";
        }
        catch (Exception ex)
        {
            StatusText = "선수상 로드 실패";
            System.Windows.MessageBox.Show($"선수상 파일 읽기 실패:\n\n{ex.Message}",
                "오류", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
        }
    }

    public void LoadItems(string filePath)
    {
        try
        {
            _allItems = _itemService.LoadItems(filePath);

            ItemCategories.Clear();
            foreach (var category in _itemService.GetDistinctCategories())
                ItemCategories.Add(category);

            ApplyItemFilter();
            StatusText = $"아이템 로드 완료: {_allItems.Count}개";
        }
        catch (Exception ex)
        {
            StatusText = "아이템 로드 실패";
            System.Windows.MessageBox.Show($"아이템 파일 읽기 실패:\n\n{ex.Message}",
                "오류", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
        }
    }

    #endregion

    #region Filter Methods

    private void ApplyCharacterFilter()
    {
        if (_allCharacters.Count == 0) return;

        var filtered = _characterService.Filter(
            _allCharacters,
            ShowGrayCharacters,
            string.IsNullOrWhiteSpace(CharacterNameSearch) ? null : CharacterNameSearch,
            FilterBySkill ? SelectedSkillIndex + 1 : null,
            FilterBySkill ? SelectedSkillLevel : null);

        Characters = new ObservableCollection<CharacterData>(filtered);

        var grayCount = _allCharacters.Count(c => c.IsGray);
        var normalCount = _allCharacters.Count - grayCount;

        StatusText = ShowGrayCharacters
            ? $"로드 완료: {_allCharacters.Count}개 (등장: {normalCount}, 미등장: {grayCount})"
            : $"로드 완료: {normalCount}개 (미등장 {grayCount}개 숨김)";
    }

    private void ApplyBookFilter()
    {
        if (_allBooks.Count == 0) return;

        var filtered = _bookService.Filter(
            _allBooks,
            string.IsNullOrWhiteSpace(BookNameSearch) ? null : BookNameSearch,
            string.IsNullOrWhiteSpace(LibrarySearch) ? null : LibrarySearch,
            string.IsNullOrWhiteSpace(HintSearch) ? null : HintSearch,
            SelectedLanguage,
            SelectedRequiredSkill);

        Books = new ObservableCollection<Book>(filtered);
        StatusText = $"도서: {filtered.Count}개";
    }

    private void ApplyCityFilter()
    {
        if (_allCities.Count == 0) return;

        var filtered = _cityService.Filter(
            _allCities,
            string.IsNullOrWhiteSpace(CityNameSearch) ? null : CityNameSearch,
            SelectedCulturalSphere == "전체" ? null : SelectedCulturalSphere,
            LibraryOnly,
            ShipyardOnly);

        Cities = new ObservableCollection<City>(filtered);
        StatusText = $"도시: {filtered.Count}개";
    }

    private void ApplyPatronFilter()
    {
        if (_allPatrons.Count == 0) return;

        var filtered = _patronService.Filter(
            _allPatrons,
            string.IsNullOrWhiteSpace(PatronNameSearch) ? null : PatronNameSearch,
            string.IsNullOrWhiteSpace(PatronCitySearch) ? null : PatronCitySearch,
            SelectedNationality,
            ActivePatronsOnly,
            CurrentYear);

        var displayList = _patronService.ToDisplayList(filtered, CurrentYear);
        Patrons = new ObservableCollection<PatronDisplay>(displayList);
        StatusText = $"후원자: {displayList.Count}명 (기준년도: {CurrentYear})";
    }

    private void ApplyFigureheadFilter()
    {
        if (_allFigureheads.Count == 0) return;

        int? level = null;
        if (!string.IsNullOrWhiteSpace(SelectedFigureheadLevel) && SelectedFigureheadLevel != "전체")
        {
            if (int.TryParse(SelectedFigureheadLevel, out var parsed))
                level = parsed;
        }

        var filtered = _figureheadService.Filter(
            string.IsNullOrWhiteSpace(FigureheadNameSearch) ? null : FigureheadNameSearch,
            SelectedFigureheadFunction,
            level);

        Figureheads = new ObservableCollection<Figurehead>(filtered);
        StatusText = $"선수상: {filtered.Count}개";
    }

    private void ApplyItemFilter()
    {
        if (_allItems.Count == 0) return;

        var filtered = _itemService.Filter(
            string.IsNullOrWhiteSpace(ItemNameSearch) ? null : ItemNameSearch,
            SelectedItemCategory,
            string.IsNullOrWhiteSpace(ItemDiscoverySearch) ? null : ItemDiscoverySearch);

        Items = new ObservableCollection<Item>(filtered);
        StatusText = $"아이템: {filtered.Count}개";
    }

    #endregion

    #region Reset Methods

    [RelayCommand]
    private void ResetBookFilter()
    {
        BookNameSearch = "";
        LibrarySearch = "";
        HintSearch = "";
        SelectedLanguage = null;
        SelectedRequiredSkill = null;
    }

    [RelayCommand]
    private void ResetCityFilter()
    {
        CityNameSearch = "";
        SelectedCulturalSphere = "전체";
        LibraryOnly = false;
        ShipyardOnly = false;
    }

    [RelayCommand]
    private void ResetPatronFilter()
    {
        PatronNameSearch = "";
        PatronCitySearch = "";
        SelectedNationality = null;
        ActivePatronsOnly = false;
    }

    [RelayCommand]
    private void ResetFigureheadFilter()
    {
        FigureheadNameSearch = "";
        SelectedFigureheadFunction = null;
        SelectedFigureheadLevel = null;
    }

    [RelayCommand]
    private void ResetItemFilter()
    {
        ItemNameSearch = "";
        SelectedItemCategory = null;
        ItemDiscoverySearch = "";
    }

    #endregion

    #region City Edit Methods

    [RelayCommand]
    private async Task ExportCitiesToJson()
    {
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Filter = "JSON 파일 (*.json)|*.json",
            Title = "도시 정보 내보내기",
            FileName = "cities.json"
        };

        if (dialog.ShowDialog() != true) return;

        try
        {
            await _cityService.ExportToJsonAsync(dialog.FileName);
            StatusText = $"도시 정보 내보내기 완료: {dialog.FileName}";
            System.Windows.MessageBox.Show($"도시 정보를 저장했습니다.\n\n{dialog.FileName}",
                "완료", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show($"내보내기 실패: {ex.Message}", "오류",
                System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
        }
    }

    [RelayCommand]
    private async Task EditCityPixel(City? city)
    {
        if (city == null) return;

        var dialog = new CdsHelper.Support.UI.Views.EditCityPixelDialog(
            city.Name, city.PixelX, city.PixelY, city.HasLibrary, city.Latitude, city.Longitude, city.CulturalSphere);

        dialog.Owner = System.Windows.Application.Current.MainWindow;

        if (dialog.ShowDialog() != true) return;

        try
        {
            var result = await _cityService.UpdateCityInfoAsync(
                city.Id, dialog.CityName, dialog.PixelX, dialog.PixelY, dialog.HasLibrary, dialog.Latitude, dialog.Longitude, dialog.CulturalSphere);

            if (result)
            {
                city.Name = dialog.CityName;
                city.PixelX = dialog.PixelX;
                city.PixelY = dialog.PixelY;
                city.HasLibrary = dialog.HasLibrary;
                city.Latitude = dialog.Latitude;
                city.Longitude = dialog.Longitude;
                city.CulturalSphere = dialog.CulturalSphere;
                ApplyCityFilter();
                StatusText = $"{dialog.CityName} 정보 업데이트 완료: 문화권 {dialog.CulturalSphere ?? "-"}, 도서관: {(dialog.HasLibrary ? "있음" : "없음")}";
            }
            else
            {
                System.Windows.MessageBox.Show("도시 정보를 찾을 수 없습니다.", "오류",
                    System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            }
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show($"업데이트 실패: {ex.Message}", "오류",
                System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
        }
    }

    #endregion

    #region Native Deps Download

    private static readonly (string Name, string Url)[] NativeDependencies =
    [
        ("OpenCvSharpExtern.dll", "https://github.com/Kyeongrok/cds-helper/releases/download/native-deps/OpenCvSharpExtern.dll"),
        ("onnxruntime.dll", "https://github.com/Kyeongrok/cds-helper/releases/download/native-deps/onnxruntime.dll"),
        ("onnxruntime_providers_shared.dll", "https://github.com/Kyeongrok/cds-helper/releases/download/native-deps/onnxruntime_providers_shared.dll"),
    ];

    /// <summary>
    /// 네이티브 DLL의 버전-독립적 공유 캐시 경로.
    /// Velopack 업데이트마다 BaseDirectory가 바뀌어 DLL을 매번 재다운로드하던 문제 해결.
    /// </summary>
    private static string GetNativeCacheDir() => System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "CdsHelper",
        "native");

    public async Task CheckAndDownloadNativeDepsAsync()
    {
        var basePath = AppDomain.CurrentDomain.BaseDirectory;
        var cacheDir = GetNativeCacheDir();
        System.IO.Directory.CreateDirectory(cacheDir);

        // 1단계: basePath에 없으면 캐시에서 복사 (업데이트 직후 시나리오)
        foreach (var (name, _) in NativeDependencies)
        {
            var baseDll = System.IO.Path.Combine(basePath, name);
            if (System.IO.File.Exists(baseDll)) continue;
            var cacheDll = System.IO.Path.Combine(cacheDir, name);
            if (System.IO.File.Exists(cacheDll))
            {
                try { System.IO.File.Copy(cacheDll, baseDll, overwrite: false); }
                catch { /* 복사 실패해도 다음 단계에서 재다운로드로 폴백 */ }
            }
        }

        // 2단계: 여전히 없는 것만 원격에서 다운로드
        var missing = NativeDependencies
            .Where(d => !System.IO.File.Exists(System.IO.Path.Combine(basePath, d.Name)))
            .ToList();

        if (missing.Count == 0) return;

        var result = System.Windows.MessageBox.Show(
            $"지도/자동진행 기능에 필요한 라이브러리({missing.Count}개)가 없습니다.\n다운로드하시겠습니까? (약 70MB)",
            "라이브러리 다운로드",
            System.Windows.MessageBoxButton.YesNo,
            System.Windows.MessageBoxImage.Question);

        if (result != System.Windows.MessageBoxResult.Yes) return;

        try
        {
            using var client = new HttpClient();
            foreach (var (name, url) in missing)
            {
                StatusText = $"다운로드 중: {name}...";
                var data = await client.GetByteArrayAsync(url);
                // 공유 캐시와 현재 버전 폴더 양쪽에 저장 → 다음 버전부터는 캐시에서 복사만
                await System.IO.File.WriteAllBytesAsync(System.IO.Path.Combine(cacheDir, name), data);
                await System.IO.File.WriteAllBytesAsync(System.IO.Path.Combine(basePath, name), data);
            }
            StatusText = "라이브러리 다운로드 완료";
        }
        catch (Exception ex)
        {
            StatusText = "라이브러리 다운로드 실패";
            System.Windows.MessageBox.Show($"다운로드 실패:\n{ex.Message}", "오류",
                System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
        }
    }

    #endregion

    #region Auto Update

    public async Task CheckForUpdateAsync()
    {
        var newVersion = await _updateService.CheckForUpdateAsync();
        if (newVersion == null)
        {
            // 진단 메시지를 상태바에 노출 → 왜 감지 안 되는지 원인 확인 가능
            var diag = _updateService.LastDiagnostic;
            var cur = _updateService.CurrentVersion;
            if (!string.IsNullOrEmpty(diag))
                StatusText = $"[업데이트] {diag} (현재 {cur ?? "unknown"})";
            return;
        }

        StatusText = $"새 버전 {newVersion} 다운로드 중...";
        await _updateService.DownloadUpdateAsync(p => StatusText = $"업데이트 다운로드 중... {p}%");

        var result = System.Windows.MessageBox.Show(
            $"버전 {newVersion}으로 업데이트할 준비가 됐습니다.\n지금 재시작하시겠습니까?",
            "업데이트 완료",
            System.Windows.MessageBoxButton.YesNo,
            System.Windows.MessageBoxImage.Information);

        if (result == System.Windows.MessageBoxResult.Yes)
            _updateService.ApplyUpdateAndRestart();
        else
            StatusText = "업데이트 준비 완료 (다음 재시작 시 적용)";
    }

    #endregion

    #region Region Navigation

    public void NavigateToContent(string viewName)
    {
        if (string.IsNullOrEmpty(viewName)) return;
        System.Diagnostics.Debug.WriteLine($"[Navigate] Requesting: {viewName}");
        _regionManager.RequestNavigate("MainContentRegion", viewName, result => { });
    }

    #endregion
}
