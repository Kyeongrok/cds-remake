using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using OpenCvSharp;
using OpenCvSharp.Extensions;

namespace CdsHelper.Support.Local.Helpers;

/// <summary>
/// 게임 상태 감지기.
/// 주기적으로 게임 윈도우를 확인하고 화면을 캡처하여 현재 상태를 판별한다.
/// </summary>
public class GameStateDetector : IDisposable
{
    private readonly Timer _timer;
    private readonly string _templateDir;
    private readonly double _threshold;
    private readonly Dictionary<GameScene, List<Mat>> _templates = new();
    private bool _disposed;

    /// <summary>상태 변경 이벤트.</summary>
    public event Action<GameDetectionResult>? StateChanged;

    /// <summary>로그 이벤트.</summary>
    public event Action<string>? LogMessage;

    public string TemplateDir => _templateDir;

    public GameStateDetector(string? templateDir = null, double threshold = 0.8)
    {
        _threshold = threshold;

        // 템플릿 디렉토리: cds-ai 프로젝트의 assets/templates/states
        _templateDir = templateDir
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "git", "ml", "cds-ai", "assets", "templates", "states");

        LoadTemplates();

        _timer = new Timer(OnTick, null, Timeout.Infinite, Timeout.Infinite);
    }

    /// <summary>감지 시작.</summary>
    public void Start(int intervalMs = 2000)
    {
        _timer.Change(0, intervalMs);
    }

    /// <summary>감지 중지.</summary>
    public void Stop()
    {
        _timer.Change(Timeout.Infinite, Timeout.Infinite);
    }

    /// <summary>템플릿을 (재)로드한다.</summary>
    public void LoadTemplates()
    {
        // 기존 템플릿 해제
        foreach (var (_, mats) in _templates)
            foreach (var mat in mats)
                mat.Dispose();
        _templates.Clear();

        if (!Directory.Exists(_templateDir))
        {
            Log($"템플릿 디렉토리 없음: {_templateDir}");
            return;
        }

        var totalCount = 0;
        foreach (var dir in Directory.GetDirectories(_templateDir))
        {
            var folderName = Path.GetFileName(dir);
            if (!FolderToScene.TryGetValue(folderName, out var scene))
                continue;

            var pngFiles = Directory.GetFiles(dir, "*.png");
            foreach (var png in pngFiles)
            {
                var mat = Cv2.ImRead(png, ImreadModes.Grayscale);
                if (mat.Empty())
                    continue;

                if (!_templates.ContainsKey(scene))
                    _templates[scene] = new List<Mat>();

                _templates[scene].Add(mat);
                totalCount++;
            }
        }

        Log($"템플릿 로드 완료: {totalCount}개 ({_templates.Count}개 장면)");
    }

    /// <summary>
    /// 현재 게임 화면을 지정된 장면 폴더에 템플릿으로 저장한다.
    /// </summary>
    public string? CaptureTemplate(GameScene scene)
    {
        var hWnd = GameWindowHelper.FindGameWindow();
        if (hWnd == IntPtr.Zero)
        {
            Log("캡처 실패: 게임 윈도우를 찾을 수 없음");
            return null;
        }

        using var bitmap = GameWindowHelper.CaptureClient(hWnd);
        if (bitmap == null)
        {
            Log("캡처 실패: 화면 캡처 실패");
            return null;
        }

        // 장면에 맞는 폴더명 결정
        var folderName = SceneToFolder(scene);
        var saveDir = Path.Combine(_templateDir, folderName);
        Directory.CreateDirectory(saveDir);

        // 파일명: 타임스탬프 기반
        var fileName = $"{folderName}_{DateTime.Now:yyyyMMdd_HHmmss}.png";
        var filePath = Path.Combine(saveDir, fileName);

        bitmap.Save(filePath, ImageFormat.Png);
        Log($"템플릿 저장: {filePath}");

        // 새 템플릿 즉시 반영
        var mat = Cv2.ImRead(filePath, ImreadModes.Grayscale);
        if (!mat.Empty())
        {
            if (!_templates.ContainsKey(scene))
                _templates[scene] = new List<Mat>();
            _templates[scene].Add(mat);
        }

        return filePath;
    }

    /// <summary>현재 게임 상태를 감지한다.</summary>
    public GameDetectionResult Detect()
    {
        // 1) 게임 윈도우 찾기
        var hWnd = GameWindowHelper.FindGameWindow();
        if (hWnd == IntPtr.Zero)
        {
            return new GameDetectionResult
            {
                IsGameRunning = false,
                Scene = GameScene.Unknown,
                SceneDetail = "게임이 실행되지 않음"
            };
        }

        // 2) 화면 캡처
        using var bitmap = GameWindowHelper.CaptureClient(hWnd);
        if (bitmap == null)
        {
            return new GameDetectionResult
            {
                IsGameRunning = true,
                Scene = GameScene.Unknown,
                SceneDetail = "화면 캡처 실패"
            };
        }

        // 3) 템플릿 없으면 윈도우만 확인
        if (_templates.Count == 0)
        {
            return new GameDetectionResult
            {
                IsGameRunning = true,
                Scene = GameScene.Unknown,
                SceneDetail = "감지 템플릿 없음 (윈도우 감지됨)"
            };
        }

        // 4) OpenCvSharp 템플릿 매칭
        using var screenMat = BitmapConverter.ToMat(bitmap);
        using var grayScreen = new Mat();
        Cv2.CvtColor(screenMat, grayScreen, ColorConversionCodes.BGR2GRAY);

        var bestScene = GameScene.Unknown;
        var bestScore = 0.0;

        foreach (var (scene, templates) in _templates)
        {
            foreach (var tmpl in templates)
            {
                if (tmpl.Rows > grayScreen.Rows || tmpl.Cols > grayScreen.Cols)
                    continue;

                using var result = new Mat();
                Cv2.MatchTemplate(grayScreen, tmpl, result, TemplateMatchModes.CCoeffNormed);
                Cv2.MinMaxLoc(result, out _, out var maxVal, out _, out _);

                if (maxVal > _threshold && maxVal > bestScore)
                {
                    bestScore = maxVal;
                    bestScene = scene;
                }
            }
        }

        return new GameDetectionResult
        {
            IsGameRunning = true,
            Scene = bestScene,
            SceneDetail = SceneToDisplayName(bestScene, bestScore),
            Confidence = bestScore
        };
    }

    private void OnTick(object? state)
    {
        try
        {
            var result = Detect();
            StateChanged?.Invoke(result);
        }
        catch
        {
            // 감지 실패 시 무시
        }
    }

    private void Log(string msg)
    {
        LogMessage?.Invoke($"[{DateTime.Now:HH:mm:ss}] {msg}");
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _timer.Dispose();
        foreach (var (_, mats) in _templates)
            foreach (var mat in mats)
                mat.Dispose();
        _templates.Clear();
    }

    #region Scene ↔ Folder 매핑

    /// <summary>폴더명 → GameScene 매핑.</summary>
    private static readonly Dictionary<string, GameScene> FolderToScene = new()
    {
        // 도시
        ["city_view"] = GameScene.City,
        ["port_city"] = GameScene.City,

        // 항구 메뉴
        ["port_main_menu"] = GameScene.PortMenu,

        // 건물 내부
        ["port_trade"] = GameScene.Trade,
        ["port_trade_buy"] = GameScene.Trade,
        ["port_trade_sell"] = GameScene.Trade,
        ["port_inn"] = GameScene.Inn,
        ["port_guild"] = GameScene.Guild,
        ["port_shipyard"] = GameScene.Shipyard,
        ["port_library"] = GameScene.Library,
        ["port_palace"] = GameScene.Palace,
        ["port_supply"] = GameScene.Supply,

        // 해상
        ["sea_anchored"] = GameScene.SeaAnchored,
        ["sea_navigation"] = GameScene.SeaNavigation,

        // 전투
        ["combat_start"] = GameScene.Combat,
        ["combat_command"] = GameScene.Combat,
        ["combat_result"] = GameScene.Combat,

        // 대화
        ["dialog"] = GameScene.Dialog,
    };

    /// <summary>GameScene → 대표 폴더명.</summary>
    private static string SceneToFolder(GameScene scene) => scene switch
    {
        GameScene.City => "city_view",
        GameScene.PortMenu => "port_main_menu",
        GameScene.Trade => "port_trade",
        GameScene.Inn => "port_inn",
        GameScene.Guild => "port_guild",
        GameScene.Shipyard => "port_shipyard",
        GameScene.Library => "port_library",
        GameScene.Palace => "port_palace",
        GameScene.Supply => "port_supply",
        GameScene.SeaAnchored => "sea_anchored",
        GameScene.SeaNavigation => "sea_navigation",
        GameScene.Combat => "combat_start",
        GameScene.Dialog => "dialog",
        _ => "unknown",
    };

    /// <summary>GameScene → 표시명.</summary>
    private static string SceneToDisplayName(GameScene scene, double score) => scene switch
    {
        GameScene.City => "도시 전경",
        GameScene.PortMenu => "항구 메뉴",
        GameScene.Trade => "교역소",
        GameScene.Inn => "여관",
        GameScene.Guild => "길드",
        GameScene.Shipyard => "조선소",
        GameScene.Library => "도서관",
        GameScene.Palace => "왕궁",
        GameScene.Supply => "보급소",
        GameScene.SeaAnchored => "해상 (정박)",
        GameScene.SeaNavigation => "해상 (항해 중)",
        GameScene.Combat => "전투",
        GameScene.Dialog => "대화창",
        _ => $"알 수 없음 (최고 점수: {score:F2})",
    };

    #endregion
}

/// <summary>게임 화면 장면 분류.</summary>
public enum GameScene
{
    Unknown,

    // 도시
    City,

    // 항구
    PortMenu,

    // 건물 내부
    Trade,
    Inn,
    Guild,
    Shipyard,
    Library,
    Palace,
    Supply,

    // 해상
    SeaAnchored,
    SeaNavigation,

    // 전투
    Combat,

    // 대화
    Dialog,

    // 탐험
    Exploration,

    // 정보/힌트 화면
    InfoMenu,
    HintList,
}

/// <summary>게임 상태 감지 결과.</summary>
public class GameDetectionResult
{
    public bool IsGameRunning { get; init; }
    public GameScene Scene { get; init; }
    public string SceneDetail { get; init; } = string.Empty;
    public double Confidence { get; init; }

    public bool IsInBuilding => Scene is GameScene.Trade or GameScene.Inn or GameScene.Guild
        or GameScene.Shipyard or GameScene.Library or GameScene.Palace or GameScene.Supply;

    public bool IsAtSea => Scene is GameScene.SeaAnchored or GameScene.SeaNavigation or GameScene.Combat;
}
