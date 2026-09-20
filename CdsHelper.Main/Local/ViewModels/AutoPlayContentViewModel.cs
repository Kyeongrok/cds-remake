using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CdsHelper.Support.Local.Helpers;
using CdsHelper.Support.Local.Models;
using CdsHelper.Support.Local.Settings;
using Prism.Ioc;

namespace CdsHelper.Main.Local.ViewModels;

public partial class AutoPlayContentViewModel : ObservableObject
{
    private readonly AutoPlayService _autoPlayService = new();
    private readonly StatRerollService _rerollService = new();
    private readonly CityService _cityService;
    private readonly GameStateDetector _detector;

    public AutoPlayContentViewModel()
    {
        _cityService = ContainerLocator.Container.Resolve<CityService>();
        _detector = new GameStateDetector();

        _autoPlayService.StatusChanged += OnStatusChanged;
        _autoPlayService.LogMessage += OnLogMessage;
        _detector.StateChanged += OnGameStateChanged;
        _detector.LogMessage += OnLogMessage;

        _rerollService.LogMessage += OnLogMessage;
        _rerollService.Progress += OnRerollProgress;
        _rerollService.Completed += OnRerollCompleted;
        _rerollService.Stopped += () => Application.Current?.Dispatcher.Invoke(() =>
        {
            // 목표 달성/수동 중지로 이미 정리됐으면 그때 표시한 문구를 덮어쓰지 않는다.
            if (!IsRerolling) return;
            IsRerolling = false;
            RerollStatusText = _rerollService.LastStopReason;
        });
        _rerollService.TemplatesChanged += () => Application.Current?.Dispatcher.Invoke(RefreshDigitImages);

        LoadCities();
        LoadCaptureScenes();
        RefreshDigitImages();
    }

    #region Game Detection Properties

    [ObservableProperty] private bool _isGameRunning;
    [ObservableProperty] private string _gameRunningDisplay = "-";
    [ObservableProperty] private string _gameSceneDisplay = "-";
    [ObservableProperty] private string _gameSceneIcon = "⏹️";

    #endregion

    #region Capture Template Properties

    [ObservableProperty] private ObservableCollection<CaptureSceneItem> _captureScenes = new();

    [NotifyCanExecuteChangedFor(nameof(CaptureTemplateCommand))]
    [ObservableProperty] private CaptureSceneItem? _selectedCaptureScene;

    #endregion

    #region AutoPlay Properties

    [ObservableProperty] private ObservableCollection<City> _cities = new();
    [ObservableProperty] private City? _selectedCity;

    [NotifyCanExecuteChangedFor(nameof(StartCommand))]
    [NotifyCanExecuteChangedFor(nameof(StopCommand))]
    [NotifyCanExecuteChangedFor(nameof(CaptureTemplateCommand))]
    [ObservableProperty] private bool _isRunning;

    [ObservableProperty] private string _statusText = "대기 중";
    [ObservableProperty] private string _currentLatDisplay = "-";
    [ObservableProperty] private string _currentLonDisplay = "-";
    [ObservableProperty] private string _bearingDisplay = "-";
    [ObservableProperty] private string _destinationDisplay = "-";
    [ObservableProperty] private ObservableCollection<string> _logMessages = new();

    #endregion

    #region 능력치 리롤 Properties

    [NotifyCanExecuteChangedFor(nameof(StartRerollCommand))]
    [NotifyCanExecuteChangedFor(nameof(StopRerollCommand))]
    [NotifyCanExecuteChangedFor(nameof(PickClickPointCommand))]
    [ObservableProperty] private bool _isRerolling;

    [ObservableProperty] private int _targetHp;
    [ObservableProperty] private int _targetInt = 76;
    [ObservableProperty] private int _targetStr;
    [ObservableProperty] private int _targetCha;
    [ObservableProperty] private int _targetLuck;
    [ObservableProperty] private int _targetBonus;
    [ObservableProperty] private int _rerollAttempts;
    [ObservableProperty] private string _rerollStatusText = "대기 중";
    [ObservableProperty] private int _clickDelay = 50;
    [ObservableProperty] private int _maxAttempts = 20;
    [ObservableProperty] private string _learnStatValues = "79,50,74,59,80,15";

    /// <summary>true면 자동 감지한 사냥꾼 버튼 대신 <see cref="CustomClickX"/>/<see cref="CustomClickY"/>를 클릭한다.</summary>
    [ObservableProperty] private bool _useCustomClickPoint = AppSettings.Reroll.UseCustomClickPoint;
    [ObservableProperty] private int _customClickX = AppSettings.Reroll.ClickX;
    [ObservableProperty] private int _customClickY = AppSettings.Reroll.ClickY;
    [NotifyCanExecuteChangedFor(nameof(PickClickPointCommand))]
    [ObservableProperty] private bool _isPickingClickPoint;

    [ObservableProperty] private BitmapImage? _digit0;
    [ObservableProperty] private BitmapImage? _digit1;
    [ObservableProperty] private BitmapImage? _digit2;
    [ObservableProperty] private BitmapImage? _digit3;
    [ObservableProperty] private BitmapImage? _digit4;
    [ObservableProperty] private BitmapImage? _digit5;
    [ObservableProperty] private BitmapImage? _digit6;
    [ObservableProperty] private BitmapImage? _digit7;
    [ObservableProperty] private BitmapImage? _digit8;
    [ObservableProperty] private BitmapImage? _digit9;

    #endregion

    #region 좌표 인식 Properties

    [NotifyCanExecuteChangedFor(nameof(StartCollectCoordsCommand))]
    [NotifyCanExecuteChangedFor(nameof(StopCollectCoordsCommand))]
    [ObservableProperty] private bool _isCollectingCoords;

    [NotifyCanExecuteChangedFor(nameof(TrainCoordModelCommand))]
    [ObservableProperty] private bool _isTrainingCoords;

    [ObservableProperty] private string _coordStatusText = "대기 중";
    [ObservableProperty] private int _coordTrainEpochs = 50;
    [ObservableProperty] private string _coordLabelInput = "0,38,1,10";
    [ObservableProperty] private int _recognizeInterval = 500;

    [ObservableProperty] private bool _targetIsNorth = true;
    [ObservableProperty] private int _targetLat;
    [ObservableProperty] private bool _targetIsEast = true;
    [ObservableProperty] private int _targetLon;

    [NotifyCanExecuteChangedFor(nameof(StartNavCommand))]
    [NotifyCanExecuteChangedFor(nameof(StopNavCommand))]
    [ObservableProperty] private bool _isNavigating;

    [ObservableProperty] private string _navStatusText = "-";

    [NotifyCanExecuteChangedFor(nameof(StartRecognizeCommand))]
    [NotifyCanExecuteChangedFor(nameof(StopRecognizeCommand))]
    [ObservableProperty] private bool _isRecognizing;

    [ObservableProperty] private string _recognizedCoordText = "-";

    #endregion

    private void LoadCities()
    {
        var cities = _cityService.GetCachedCities();
        Cities = new ObservableCollection<City>(
            cities.Where(c => c.Latitude.HasValue && c.Longitude.HasValue)
                  .OrderBy(c => c.Name));
    }

    private void LoadCaptureScenes()
    {
        CaptureScenes = new ObservableCollection<CaptureSceneItem>
        {
            new(GameScene.City, "도시 전경"),
            new(GameScene.PortMenu, "항구 메뉴"),
            new(GameScene.Trade, "교역소"),
            new(GameScene.Inn, "여관"),
            new(GameScene.Guild, "길드"),
            new(GameScene.Shipyard, "조선소"),
            new(GameScene.Library, "도서관"),
            new(GameScene.Palace, "왕궁"),
            new(GameScene.Supply, "보급소"),
            new(GameScene.SeaAnchored, "해상 (정박)"),
            new(GameScene.SeaNavigation, "해상 (항해 중)"),
            new(GameScene.Combat, "전투"),
            new(GameScene.Dialog, "대화창"),
        };
    }

    [RelayCommand(CanExecute = nameof(CanStart))]
    private void Start()
    {
        IsRunning = true;
        StatusText = "감지 시작...";
        AddLog("Ctrl + Alt 키를 동시에 누르면 자동 플레이를 중지할 수 있습니다.");

        _detector.Start(2000);

        if (SelectedCity != null)
        {
            DestinationDisplay = $"{SelectedCity.Name} ({SelectedCity.LatitudeDisplay}, {SelectedCity.LongitudeDisplay})";
            _autoPlayService.Start(SelectedCity, _detector);
        }
    }
    private bool CanStart() => !IsRunning;

    [RelayCommand(CanExecute = nameof(CanStop))]
    private void Stop()
    {
        _detector.Stop();
        _autoPlayService.Stop();
        IsRunning = false;
        StatusText = "대기 중";

        IsGameRunning = false;
        GameRunningDisplay = "-";
        GameSceneDisplay = "-";
        GameSceneIcon = "⏹️";
    }
    private bool CanStop() => IsRunning;

    [RelayCommand(CanExecute = nameof(CanCaptureTemplate))]
    private void CaptureTemplate()
    {
        if (SelectedCaptureScene == null) return;

        var path = _detector.CaptureTemplate(SelectedCaptureScene.Scene);
        if (path != null)
            AddLog($"템플릿 저장 완료: {SelectedCaptureScene.DisplayName} → {path}");
        else
            AddLog($"템플릿 캡처 실패: {SelectedCaptureScene.DisplayName}");
    }
    private bool CanCaptureTemplate() => IsRunning && SelectedCaptureScene != null;

    private void OnGameStateChanged(GameDetectionResult result)
    {
        Application.Current?.Dispatcher.Invoke(() =>
        {
            IsGameRunning = result.IsGameRunning;
            GameRunningDisplay = result.IsGameRunning ? "실행 중" : "실행되지 않음";
            GameSceneDisplay = result.SceneDetail;

            GameSceneIcon = result.Scene switch
            {
                GameScene.City => "🏙️",
                GameScene.PortMenu => "⚓",
                GameScene.Trade => "🏪",
                GameScene.Inn => "🏨",
                GameScene.Guild => "🏛️",
                GameScene.Shipyard => "🚢",
                GameScene.Library => "📚",
                GameScene.Palace => "👑",
                GameScene.Supply => "📦",
                GameScene.SeaAnchored => "⚓",
                GameScene.SeaNavigation => "🌊",
                GameScene.Combat => "⚔️",
                GameScene.Dialog => "💬",
                _ => result.IsGameRunning ? "❓" : "⛔"
            };
        });
    }

    private void OnStatusChanged(AutoPlayStatus status)
    {
        Application.Current?.Dispatcher.Invoke(() =>
        {
            StatusText = status.Message;

            if (status.CurrentLat != 0 || status.CurrentLon != 0)
            {
                CurrentLatDisplay = status.CurrentLat.ToString("F1");
                CurrentLonDisplay = status.CurrentLon.ToString("F1");
            }

            if (status.Bearing != 0)
                BearingDisplay = $"{status.Bearing:F1}°";

            if (status.State is AutoPlayState.Arrived or AutoPlayState.Stopped or AutoPlayState.Error)
                IsRunning = false;
        });
    }

    private void OnLogMessage(string message)
    {
        Application.Current?.Dispatcher.Invoke(() => AddLog(message));
    }

    #region 능력치 리롤 핸들러

    [RelayCommand(CanExecute = nameof(CanStartReroll))]
    private void StartReroll()
    {
        if (UseCustomClickPoint && CustomClickX <= 0 && CustomClickY <= 0)
        {
            AddLog("클릭 좌표가 지정되지 않았습니다. '화면에서 찍기'로 위치를 먼저 지정하세요.");
            RerollStatusText = "좌표 미지정";
            return;
        }

        IsRerolling = true;
        RerollAttempts = 0;
        RerollStatusText = "감지 중...";

        ApplyClickPoint();

        _rerollService.Start(
            new[] { TargetHp, TargetInt, TargetStr, TargetCha, TargetLuck, TargetBonus },
            ClickDelay, MaxAttempts);
    }
    private bool CanStartReroll() => !IsRerolling;

    /// <summary>사용자 지정 클릭 좌표를 서비스에 반영하고 설정에 저장한다.</summary>
    private void ApplyClickPoint()
    {
        _rerollService.CustomClickPoint = UseCustomClickPoint
            ? (CustomClickX, CustomClickY)
            : null;

        AppSettings.Reroll.UseCustomClickPoint = UseCustomClickPoint;
        AppSettings.Reroll.ClickX = CustomClickX;
        AppSettings.Reroll.ClickY = CustomClickY;
        AppSettings.SaveRerollOptions();
    }

    /// <summary>게임 화면에서 클릭할 위치를 직접 찍어 좌표로 저장한다.</summary>
    [RelayCommand(CanExecute = nameof(CanPickClickPoint))]
    private async Task PickClickPoint()
    {
        IsPickingClickPoint = true;
        RerollStatusText = "게임 화면에서 클릭할 위치를 좌클릭하세요...";
        try
        {
            var point = await _rerollService.PickClickPointAsync();
            if (point != null)
            {
                CustomClickX = point.Value.x;
                CustomClickY = point.Value.y;
                UseCustomClickPoint = true;
                ApplyClickPoint();
                RerollStatusText = $"좌표 지정됨 ({point.Value.x},{point.Value.y})";
            }
            else
            {
                RerollStatusText = "좌표 지정 취소됨";
            }
        }
        finally
        {
            IsPickingClickPoint = false;
        }
    }
    private bool CanPickClickPoint() => !IsPickingClickPoint && !IsRerolling;

    /// <summary>지정한 좌표를 한 번 클릭해 제대로 눌리는지 확인한다.</summary>
    [RelayCommand]
    private void TestClickPoint()
    {
        ApplyClickPoint();
        _rerollService.TestClickPoint();
    }

    /// <summary>자동 감지한 사냥꾼 버튼 좌표를 입력란에 채워 넣는다.</summary>
    [RelayCommand]
    private void LoadDetectedClickPoint()
    {
        var point = _rerollService.GetDetectedHunterPoint();
        if (point == null)
        {
            AddLog("사냥꾼 버튼을 자동 감지하지 못했습니다. 직업 선택 화면이 열려 있는지 확인하세요.");
            return;
        }

        CustomClickX = point.Value.x;
        CustomClickY = point.Value.y;
        AddLog($"자동 감지 좌표를 불러왔습니다: ({point.Value.x},{point.Value.y})");
    }

    [RelayCommand(CanExecute = nameof(CanStopReroll))]
    private void StopReroll()
    {
        _rerollService.Stop();
        IsRerolling = false;
        RerollStatusText = "중지됨";
    }
    private bool CanStopReroll() => IsRerolling;

    [RelayCommand]
    private void TestReadStat() => _rerollService.TestRead();

    [RelayCommand]
    private void LearnDigits()
    {
        _rerollService.LearnDigits(LearnStatValues);
        RefreshDigitImages();
    }

    [RelayCommand]
    private void AutoLearnDigits()
    {
        _rerollService.AutoLearnDigits();
        RefreshDigitImages();
    }

    [RelayCommand]
    private void ClearTemplates()
    {
        _rerollService.ClearTemplates();
        RefreshDigitImages();
    }

    private void RefreshDigitImages()
    {
        var dir = _rerollService.NumberTemplateDir;
        for (int d = 0; d <= 9; d++)
        {
            var path = Path.Combine(dir, $"{d}.png");
            BitmapImage? img = null;
            if (File.Exists(path))
            {
                img = new BitmapImage();
                img.BeginInit();
                img.CacheOption = BitmapCacheOption.OnLoad;
                img.UriSource = new Uri(path, UriKind.Absolute);
                img.EndInit();
                img.Freeze();
            }
            switch (d)
            {
                case 0: Digit0 = img; break;
                case 1: Digit1 = img; break;
                case 2: Digit2 = img; break;
                case 3: Digit3 = img; break;
                case 4: Digit4 = img; break;
                case 5: Digit5 = img; break;
                case 6: Digit6 = img; break;
                case 7: Digit7 = img; break;
                case 8: Digit8 = img; break;
                case 9: Digit9 = img; break;
            }
        }
    }

    private void OnRerollProgress(int value, int attempts)
    {
        Application.Current?.Dispatcher.Invoke(() =>
        {
            RerollAttempts = attempts;
            RerollStatusText = $"리롤 중... ({attempts}회)";
        });
    }

    private void OnRerollCompleted(int value, int attempts)
    {
        Application.Current?.Dispatcher.Invoke(() =>
        {
            RerollAttempts = attempts;
            RerollStatusText = $"목표 달성! ({attempts}회)";
            IsRerolling = false;
        });
    }

    #endregion

    #region 좌표 인식 핸들러

    private CancellationTokenSource? _recognizeCts;

    [RelayCommand(CanExecute = nameof(CanStartRecognize))]
    private void StartRecognize()
    {
        IsRecognizing = true;
        RecognizedCoordText = "인식 중...";
        _recognizeCts = new CancellationTokenSource();

        Task.Run(async () =>
        {
            var token = _recognizeCts.Token;
            while (!token.IsCancellationRequested)
            {
                try
                {
                    var hWnd = GameWindowHelper.FindGameWindow();
                    if (hWnd == IntPtr.Zero)
                    {
                        UpdateRecognizedText("게임 윈도우 없음");
                        await Task.Delay(2000, token);
                        continue;
                    }

                    using var bitmap = GameWindowHelper.CaptureClient(hWnd);
                    if (bitmap == null)
                    {
                        await Task.Delay(RecognizeInterval, token);
                        continue;
                    }

                    var prediction = await _autoPlayService.CoordinateOcr.PredictOcrAsync(bitmap);
                    if (prediction != null)
                        UpdateRecognizedText(prediction.ToString());
                    else
                        UpdateRecognizedText("인식 실패");

                    await Task.Delay(RecognizeInterval, token);
                }
                catch (OperationCanceledException) { break; }
                catch (Exception ex)
                {
                    UpdateRecognizedText($"오류: {ex.Message}");
                    await Task.Delay(2000, token);
                }
            }
        });
    }
    private bool CanStartRecognize() => !IsRecognizing;

    [RelayCommand(CanExecute = nameof(CanStopRecognize))]
    private void StopRecognize()
    {
        _recognizeCts?.Cancel();
        IsRecognizing = false;
        RecognizedCoordText = "중지됨";
    }
    private bool CanStopRecognize() => IsRecognizing;

    private CancellationTokenSource? _navCts;

    [RelayCommand(CanExecute = nameof(CanStartNav))]
    private void StartNav()
    {
        IsNavigating = true;
        NavStatusText = "항해 시작...";
        _navCts = new CancellationTokenSource();

        var destLat = TargetIsNorth ? TargetLat : -TargetLat;
        var destLon = TargetIsEast ? TargetLon : -TargetLon;

        Task.Run(async () =>
        {
            var token = _navCts.Token;
            while (!token.IsCancellationRequested)
            {
                try
                {
                    var hWnd = GameWindowHelper.FindGameWindow();
                    if (hWnd == IntPtr.Zero)
                    {
                        UpdateNavStatus("게임 윈도우 없음");
                        await Task.Delay(2000, token);
                        continue;
                    }

                    using var bitmap = GameWindowHelper.CaptureClient(hWnd);
                    if (bitmap == null)
                    {
                        await Task.Delay(RecognizeInterval, token);
                        continue;
                    }

                    var prediction = await _autoPlayService.CoordinateOcr.PredictOcrAsync(bitmap);
                    if (prediction == null)
                    {
                        UpdateNavStatus("좌표 인식 실패");
                        await Task.Delay(RecognizeInterval, token);
                        continue;
                    }

                    var curLat = prediction.ToLat();
                    var curLon = prediction.ToLon();

                    if (NavigationCalculator.IsNear(curLat, curLon, destLat, destLon, threshold: 2.0))
                    {
                        GameWindowHelper.SendNumpadKey(hWnd, 5);
                        UpdateNavStatus($"도착! {prediction}");
                        UpdateRecognizedText(prediction.ToString());
                        Application.Current?.Dispatcher.Invoke(() => IsNavigating = false);
                        AddLog($"목적지 도착: {prediction}");
                        return;
                    }

                    var bearing = NavigationCalculator.BearingDegrees(curLat, curLon, destLat, destLon);
                    var numpad = GameWindowHelper.BearingToNumpad(bearing);

                    GameWindowHelper.SendNumpadKey(hWnd, numpad);

                    var dirLabel = numpad switch
                    {
                        8 => "N↑", 9 => "NE↗", 6 => "E→", 3 => "SE↘",
                        2 => "S↓", 1 => "SW↙", 4 => "W←", 7 => "NW↖",
                        _ => "?"
                    };

                    UpdateNavStatus($"{prediction} → {dirLabel} (방위 {bearing:F0}°, 키:{numpad})");
                    UpdateRecognizedText(prediction.ToString());

                    await Task.Delay(RecognizeInterval, token);
                }
                catch (OperationCanceledException) { break; }
                catch (Exception ex)
                {
                    UpdateNavStatus($"오류: {ex.Message}");
                    await Task.Delay(2000, token);
                }
            }
        });
    }
    private bool CanStartNav() => !IsNavigating;

    [RelayCommand(CanExecute = nameof(CanStopNav))]
    private void StopNav()
    {
        _navCts?.Cancel();
        IsNavigating = false;
        NavStatusText = "중지됨";

        var hWnd = GameWindowHelper.FindGameWindow();
        if (hWnd != IntPtr.Zero)
            GameWindowHelper.SendNumpadKey(hWnd, 5);
    }
    private bool CanStopNav() => IsNavigating;

    private void UpdateNavStatus(string text)
    {
        Application.Current?.Dispatcher.Invoke(() => NavStatusText = text);
    }

    [RelayCommand]
    private void TestSeaMap()
    {
        var hWnd = GameWindowHelper.FindGameWindow();
        if (hWnd == IntPtr.Zero)
        {
            AddLog("게임 윈도우를 찾을 수 없습니다.");
            return;
        }

        GameWindowHelper.BringToFront(hWnd);
        Thread.Sleep(300);

        using var bitmap = GameWindowHelper.CaptureClient(hWnd);
        if (bitmap == null) return;

        var dataDir = _autoPlayService.CoordinateOcr.DataDirectory;
        var debugPath = Path.Combine(dataDir, "seamap_debug.png");
        _autoPlayService.SeaMap.SaveDebugImage(bitmap, debugPath);

        var grid = _autoPlayService.SeaMap.Analyze(bitmap);
        var shipFound = false;
        for (var r = 0; r < grid.GetLength(0) && !shipFound; r++)
        for (var c = 0; c < grid.GetLength(1) && !shipFound; c++)
            if (grid[r, c] == 2) shipFound = true;

        AddLog($"해도 분석 완료: {debugPath}");
        AddLog($"  배 감지: {(shipFound ? "성공" : "실패 — ship 템플릿 필요")}");
        System.Diagnostics.Process.Start("explorer.exe", debugPath);
    }

    private void UpdateRecognizedText(string text)
    {
        Application.Current?.Dispatcher.Invoke(() => RecognizedCoordText = text);
    }

    [RelayCommand(CanExecute = nameof(CanStartCollectCoords))]
    private void StartCollectCoords()
    {
        IsCollectingCoords = true;
        CoordStatusText = "데이터 수집 중...";
        _autoPlayService.CoordinateOcr.StartCollecting();
    }
    private bool CanStartCollectCoords() => !IsCollectingCoords;

    [RelayCommand(CanExecute = nameof(CanStopCollectCoords))]
    private void StopCollectCoords()
    {
        _autoPlayService.CoordinateOcr.StopCollecting();
        IsCollectingCoords = false;
        var count = _autoPlayService.CoordinateOcr.GetCollectedCount();
        CoordStatusText = $"수집 완료 ({count}장)";
    }
    private bool CanStopCollectCoords() => IsCollectingCoords;

    [RelayCommand(CanExecute = nameof(CanTrainCoordModel))]
    private void TrainCoordModel()
    {
        CoordStatusText = "학습 기능은 별도 프로젝트로 분리되었습니다.";
    }
    private bool CanTrainCoordModel() => !IsTrainingCoords;

    [RelayCommand]
    private void CapturePreview()
    {
        var path = _autoPlayService.CoordinateOcr.CapturePreview();
        if (path != null)
            AddLog($"스크린샷 저장: {path}");
        else
            AddLog("캡처 실패 — 게임이 실행 중인지 확인하세요.");
    }

    [RelayCommand]
    private void AutoLabelCoords()
    {
        CoordStatusText = "자동 라벨링 중 (Windows OCR)...";

        Task.Run(async () =>
        {
            var count = await _autoPlayService.CoordinateOcr.AutoLabelAsync();
            var total = _autoPlayService.CoordinateOcr.GetLabeledCount();
            Application.Current?.Dispatcher.Invoke(() =>
            {
                CoordStatusText = $"자동 라벨링 완료: {count}장 (총 {total}장)";
            });
        });
    }

    [RelayCommand]
    private void OpenCoordDataFolder()
    {
        var folder = _autoPlayService.CoordinateOcr.DataDirectory;
        Directory.CreateDirectory(folder);
        System.Diagnostics.Process.Start("explorer.exe", folder);
    }

    [RelayCommand]
    private void AddCoordLabel()
    {
        var parts = CoordLabelInput.Split(',');
        if (parts.Length != 4)
        {
            AddLog("라벨 형식 오류: latDir,latVal,lonDir,lonVal (예: 0,38,1,10)");
            return;
        }

        var imageDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "git", "ml", "cds-ai", "assets", "coordinate_data", "images");

        if (!Directory.Exists(imageDir)) return;

        var latest = Directory.GetFiles(imageDir, "*.png")
            .OrderByDescending(f => f)
            .FirstOrDefault();

        if (latest == null)
        {
            AddLog("라벨링할 이미지가 없습니다.");
            return;
        }

        _autoPlayService.CoordinateOcr.AddLabel(
            Path.GetFileName(latest),
            int.Parse(parts[0]), int.Parse(parts[1]),
            int.Parse(parts[2]), int.Parse(parts[3]));
    }

    #endregion

    private void AddLog(string message)
    {
        if (!message.StartsWith('['))
            message = $"[{DateTime.Now:HH:mm:ss}] {message}";

        LogMessages.Insert(0, message);
        while (LogMessages.Count > 200)
            LogMessages.RemoveAt(LogMessages.Count - 1);
    }
}

public class CaptureSceneItem
{
    public GameScene Scene { get; }
    public string DisplayName { get; }

    public CaptureSceneItem(GameScene scene, string displayName)
    {
        Scene = scene;
        DisplayName = displayName;
    }

    public override string ToString() => DisplayName;
}
