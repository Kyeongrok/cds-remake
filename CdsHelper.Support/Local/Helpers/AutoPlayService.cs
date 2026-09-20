using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using CdsHelper.Support.Local.Models;
using CdsHelper.Support.Local.Settings;
using OpenCvSharp;
using OpenCvSharp.Extensions;

namespace CdsHelper.Support.Local.Helpers;

/// <summary>
/// 도시→항구→출항→항해→도착 전체 흐름을 자동화하는 서비스.
/// GameStateDetector가 감지한 장면에 따라 적절한 행동을 수행한다.
/// </summary>
public class AutoPlayService : IDisposable
{
    private CancellationTokenSource? _cts;
    private Task? _runningTask;
    private readonly string _menuTemplateDir;
    private readonly string _numberTemplateDir;
    private readonly Dictionary<string, Mat> _menuTemplates = new();
    private readonly Dictionary<int, Mat> _digitTemplates = new();
    private Mat? _minusTemplate;

    /// <summary>좌표 인식 서비스 (Windows OCR).</summary>
    public CoordinateOcrService CoordinateOcr { get; }

    /// <summary>바다/육지/배 분류 서비스.</summary>
    public SeaMapService SeaMap { get; }

    /// <summary>게임 메모리 직접 읽기 (OCR 대체용 — 분 단위 정밀도, 메뉴 가림 무관).</summary>
    private readonly GameMemoryReader _gameMemoryReader = new();

    /// <summary>현재 실행 중 여부.</summary>
    public bool IsRunning => _runningTask is { IsCompleted: false };

    /// <summary>상태 변경 이벤트.</summary>
    public event Action<AutoPlayStatus>? StatusChanged;

    /// <summary>로그 메시지 이벤트.</summary>
    public event Action<string>? LogMessage;

    public AutoPlayService(string? templateBaseDir = null)
    {
        var baseDir = templateBaseDir
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "git", "ml", "cds-ai", "assets", "templates");

        _menuTemplateDir = Path.Combine(baseDir, "menus");
        _numberTemplateDir = Path.Combine(baseDir, "numbers");

        CoordinateOcr = new CoordinateOcrService();
        CoordinateOcr.LogMessage += msg => LogMessage?.Invoke(msg);

        SeaMap = new SeaMapService();
        SeaMap.LogMessage += msg => LogMessage?.Invoke(msg);

        LoadMenuTemplates();
        LoadDigitTemplates();
    }

    /// <summary>자동 항해를 시작한다.</summary>
    public void Start(City destination, GameStateDetector detector)
    {
        if (IsRunning)
            return;

        _cts = new CancellationTokenSource();
        var token = _cts.Token;

        _runningTask = Task.Run(() => RunLoop(destination, detector, token), token);
    }

    /// <summary>자동 항해를 중지한다.</summary>
    public void Stop()
    {
        _cts?.Cancel();
        RaiseStatus(new AutoPlayStatus { State = AutoPlayState.Stopped, Message = "사용자에 의해 중지됨" });
        Log("자동플레이 중지됨");
    }

    private async Task RunLoop(City destination, GameStateDetector detector, CancellationToken token)
    {
        Log($"자동 항해 시작 — 목적지: {destination.Name} ({destination.LatitudeDisplay}, {destination.LongitudeDisplay})");

        var destLat = (double)(destination.Latitude ?? 0);
        var destLon = (double)(destination.Longitude ?? 0);

        // 게임 윈도우 찾기
        var hWnd = GameWindowHelper.FindGameWindow();
        if (hWnd == IntPtr.Zero)
        {
            Log("게임 윈도우를 찾을 수 없습니다.");
            RaiseStatus(new AutoPlayStatus { State = AutoPlayState.Error, Message = "게임 윈도우를 찾을 수 없음" });
            return;
        }

        Log("게임 윈도우 발견");
        var (clientW, clientH) = GameWindowHelper.GetClientSize(hWnd);
        var centerX = clientW / 2;
        var centerY = clientH / 2;
        Log($"클라이언트 크기: {clientW}x{clientH}");

        while (!token.IsCancellationRequested)
        {
            try
            {
                // 1) 현재 장면 감지
                var detection = detector.Detect();
                if (!detection.IsGameRunning)
                {
                    Log("게임이 실행되지 않음, 대기...");
                    RaiseStatus(new AutoPlayStatus { State = AutoPlayState.WaitingForGame, Message = "게임 대기 중..." });
                    await Task.Delay(3000, token);
                    continue;
                }

                // 윈도우 핸들 갱신 (재시작 대비)
                hWnd = GameWindowHelper.FindGameWindow();
                (clientW, clientH) = GameWindowHelper.GetClientSize(hWnd);
                centerX = clientW / 2;
                centerY = clientH / 2;

                var scene = detection.Scene;

                // 2) 장면별 행동
                switch (scene)
                {
                    // ── 도시 전경: 항구 아이콘 클릭 ──
                    case GameScene.City:
                        await HandleCity(hWnd, token);
                        break;

                    // ── 항구 메뉴: 출항 버튼 클릭 ──
                    case GameScene.PortMenu:
                        await HandlePortMenu(hWnd, token);
                        break;

                    // ── 건물 안: ESC로 나가기 ──
                    case GameScene.Trade:
                    case GameScene.Inn:
                    case GameScene.Guild:
                    case GameScene.Shipyard:
                    case GameScene.Library:
                    case GameScene.Palace:
                    case GameScene.Supply:
                        Log($"건물 내부({detection.SceneDetail}) — 나가는 중...");
                        RaiseStatus(new AutoPlayStatus
                        {
                            State = AutoPlayState.LeavingBuilding,
                            Message = $"{detection.SceneDetail}에서 나가는 중..."
                        });
                        // 우클릭 또는 ESC로 건물 나가기 (게임에 따라 조정 필요)
                        GameWindowHelper.SendRightClick(centerX, centerY);
                        await Task.Delay(1500, token);
                        break;

                    // ── 대화창: 확인/넘기기 ──
                    case GameScene.Dialog:
                        await HandleDialog(hWnd, centerX, centerY, token);
                        break;

                    // ── 해상 (정박): 닻 올리기 — 화면 중앙 클릭 ──
                    case GameScene.SeaAnchored:
                        Log("정박 상태 — 닻 올리기 (화면 중앙 클릭)");
                        RaiseStatus(new AutoPlayStatus
                        {
                            State = AutoPlayState.Navigating,
                            Message = "닻 올리는 중...",
                            DestinationName = destination.Name
                        });
                        GameWindowHelper.SendClickRelative(hWnd, centerX, centerY);
                        await Task.Delay(2000, token);
                        break;

                    // ── 해상 (항해 중): 좌표 읽고 방향 조종 ──
                    case GameScene.SeaNavigation:
                        var arrived = await HandleNavigation(hWnd, destLat, destLon, centerX, centerY, destination.Name, token);
                        if (arrived)
                        {
                            Log($"목적지 {destination.Name} 근처 도달!");
                            RaiseStatus(new AutoPlayStatus
                            {
                                State = AutoPlayState.Arrived,
                                Message = $"{destination.Name} 도착!",
                                DestinationName = destination.Name
                            });
                            return;
                        }
                        break;

                    // ── 전투: 도주 (화면 중앙 클릭으로 진행) ──
                    case GameScene.Combat:
                        Log("전투 감지 — 진행 클릭...");
                        RaiseStatus(new AutoPlayStatus { State = AutoPlayState.Navigating, Message = "전투 처리 중..." });
                        GameWindowHelper.SendClickRelative(hWnd, centerX, centerY);
                        await Task.Delay(2000, token);
                        break;

                    // ── 알 수 없는 상태 ──
                    default:
                        Log($"장면 불명({detection.SceneDetail}) — 대기...");
                        RaiseStatus(new AutoPlayStatus
                        {
                            State = AutoPlayState.Navigating,
                            Message = $"장면 인식 중... ({detection.SceneDetail})"
                        });
                        await Task.Delay(2000, token);
                        break;
                }

                await Task.Delay(500, token); // 행동 간 최소 대기
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                Log($"오류: {ex.Message}");
                RaiseStatus(new AutoPlayStatus { State = AutoPlayState.Error, Message = ex.Message });
                await Task.Delay(5000, token);
            }
        }
    }

    #region 장면별 핸들러

    /// <summary>도시 전경: 항구 아이콘을 찾아 클릭.</summary>
    private async Task HandleCity(IntPtr hWnd, CancellationToken token)
    {
        Log("도시 전경 — 항구 아이콘 검색...");
        RaiseStatus(new AutoPlayStatus { State = AutoPlayState.InCity, Message = "항구로 이동 중..." });

        var target = FindMenuButton(hWnd, "harbor_icon");
        if (target.HasValue)
        {
            Log($"항구 아이콘 발견 → 클릭 ({target.Value.x}, {target.Value.y})");
            GameWindowHelper.SendClickRelative(hWnd, target.Value.x, target.Value.y);
        }
        else
        {
            Log("항구 아이콘 미발견 — 화면 중앙 클릭으로 시도");
            var (w, h) = GameWindowHelper.GetClientSize(hWnd);
            GameWindowHelper.SendClickRelative(hWnd, w / 2, h / 2);
        }

        await Task.Delay(2000, token);
    }

    /// <summary>항구 메뉴: 출항 버튼을 찾아 클릭.</summary>
    private async Task HandlePortMenu(IntPtr hWnd, CancellationToken token)
    {
        Log("항구 메뉴 — 출항 버튼 검색...");
        RaiseStatus(new AutoPlayStatus { State = AutoPlayState.InPort, Message = "출항 준비 중..." });

        var target = FindMenuButton(hWnd, "depart");
        if (target.HasValue)
        {
            Log($"출항 버튼 발견 → 클릭 ({target.Value.x}, {target.Value.y})");
            GameWindowHelper.SendClickRelative(hWnd, target.Value.x, target.Value.y);
        }
        else
        {
            Log("출항 버튼 미발견 — 템플릿 캡처 필요");
        }

        await Task.Delay(2000, token);
    }

    /// <summary>대화창: 확인 버튼 찾아 클릭, 없으면 화면 중앙 클릭.</summary>
    private async Task HandleDialog(IntPtr hWnd, int centerX, int centerY, CancellationToken token)
    {
        if (!AppSettings.AutoConfirmDialog)
        {
            Log("대화창 — 자동 확인 꺼짐 (수동 처리 대기)");
            await Task.Delay(1000, token);
            return;
        }

        Log("대화창 — 확인 버튼 검색...");

        var target = FindMenuButton(hWnd, "confirm_button");
        if (target.HasValue)
        {
            GameWindowHelper.SendClickRelative(hWnd, target.Value.x, target.Value.y);
        }
        else
        {
            // 확인 버튼 없으면 화면 중앙 클릭으로 넘기기
            GameWindowHelper.SendClickRelative(hWnd, centerX, centerY);
        }

        await Task.Delay(1000, token);
    }

    /// <summary>해상 항해: 좌표 OCR → 방위각 계산 → 커서 이동.</summary>
    private async Task<bool> HandleNavigation(IntPtr hWnd, double destLat, double destLon,
        int centerX, int centerY, string destName, CancellationToken token)
    {
        // 화면 캡처
        using var bitmap = GameWindowHelper.CaptureClient(hWnd);
        if (bitmap == null)
        {
            Log("화면 캡처 실패");
            await Task.Delay(2000, token);
            return false;
        }

        // 좌표 OCR
        var (curLat, curLon, success) = ReadCoordinatesFromScreen(bitmap);

        if (!success)
        {
            // OCR 실패 시에도 방향키를 유지 (이전 방향으로 계속 진행)
            Log("좌표 인식 실패 — 대기...");
            RaiseStatus(new AutoPlayStatus
            {
                State = AutoPlayState.Navigating,
                Message = "좌표 인식 대기 중...",
                DestinationName = destName
            });
            await Task.Delay(2000, token);
            return false;
        }

        // 도착 판정
        if (NavigationCalculator.IsNear(curLat, curLon, destLat, destLon))
            return true;

        // 방위각 → 화면 오프셋 → 커서 이동
        var bearing = NavigationCalculator.BearingDegrees(curLat, curLon, destLat, destLon);
        var (dx, dy) = NavigationCalculator.BearingToScreenOffset(bearing, radius: 80);

        var targetX = centerX + dx;
        var targetY = centerY + dy;

        // 항해 중에는 이동만 (클릭 아님) — 게임이 커서 방향으로 항해
        GameWindowHelper.MoveCursorRelative(hWnd, targetX, targetY);

        RaiseStatus(new AutoPlayStatus
        {
            State = AutoPlayState.Navigating,
            Message = $"항해 중... 방위각 {bearing:F1}°",
            CurrentLat = curLat,
            CurrentLon = curLon,
            Bearing = bearing,
            DestinationName = destName
        });

        Log($"({curLat}, {curLon}) → 방위각 {bearing:F1}° → 커서({targetX}, {targetY})");
        await Task.Delay(3000, token);
        return false;
    }

    #endregion

    #region 메뉴 버튼 탐지

    /// <summary>현재 화면에서 메뉴 버튼 위치를 찾는다.</summary>
    private (int x, int y)? FindMenuButton(IntPtr hWnd, string buttonName)
    {
        // 해당 카테고리 폴더에서 템플릿 로드
        if (!_menuTemplates.TryGetValue(buttonName, out var tmpl))
            return null;

        using var bitmap = GameWindowHelper.CaptureClient(hWnd);
        if (bitmap == null)
            return null;

        using var screenMat = BitmapConverter.ToMat(bitmap);
        using var gray = new Mat();
        Cv2.CvtColor(screenMat, gray, ColorConversionCodes.BGR2GRAY);

        if (tmpl.Rows > gray.Rows || tmpl.Cols > gray.Cols)
            return null;

        using var result = new Mat();
        Cv2.MatchTemplate(gray, tmpl, result, TemplateMatchModes.CCoeffNormed);
        Cv2.MinMaxLoc(result, out _, out var maxVal, out _, out var maxLoc);

        if (maxVal < 0.7)
            return null;

        // 매칭된 영역의 중심 좌표 반환
        var cx = maxLoc.X + tmpl.Cols / 2;
        var cy = maxLoc.Y + tmpl.Rows / 2;
        return (cx, cy);
    }

    private void LoadMenuTemplates()
    {
        _menuTemplates.Clear();
        if (!Directory.Exists(_menuTemplateDir))
            return;

        foreach (var dir in Directory.GetDirectories(_menuTemplateDir))
        {
            var name = Path.GetFileName(dir);
            var pngs = Directory.GetFiles(dir, "*.png");
            if (pngs.Length == 0) continue;

            // 첫 번째 템플릿만 사용 (가장 대표적인 것)
            var mat = Cv2.ImRead(pngs[0], ImreadModes.Grayscale);
            if (!mat.Empty())
                _menuTemplates[name] = mat;
        }
    }

    #endregion

    #region 좌표 OCR

    /// <summary>현재 좌표를 읽는다. 1순위: 게임 메모리 (분 단위 정밀), 2순위: OCR.</summary>
    private (double lat, double lon, bool success) ReadCoordinatesFromScreen(Bitmap screenshot)
    {
        // 1차: 메모리 직접 읽기 (사실상 항상 성공)
        var memCoord = _gameMemoryReader.TryReadLatLon();
        if (memCoord.HasValue)
        {
            var (lat, lon) = memCoord.Value;
            return (lat, lon, true);
        }

        // 2차 폴백: OCR
        var prediction = CoordinateOcr.PredictOcrAsync(screenshot).GetAwaiter().GetResult();
        if (prediction == null)
            return (0, 0, false);

        return (prediction.ToLat(), prediction.ToLon(), true);
    }

    private void LoadDigitTemplates()
    {
        _digitTemplates.Clear();
        if (!Directory.Exists(_numberTemplateDir))
            return;

        for (var d = 0; d <= 9; d++)
        {
            var path = Path.Combine(_numberTemplateDir, $"{d}.png");
            if (!File.Exists(path)) continue;

            var mat = Cv2.ImRead(path, ImreadModes.Grayscale);
            if (!mat.Empty())
                _digitTemplates[d] = mat;
        }

        var minusPath = Path.Combine(_numberTemplateDir, "minus.png");
        if (File.Exists(minusPath))
        {
            var mat = Cv2.ImRead(minusPath, ImreadModes.Grayscale);
            if (!mat.Empty())
                _minusTemplate = mat;
        }
    }

    #endregion

    #region 유틸

    private void RaiseStatus(AutoPlayStatus status)
    {
        StatusChanged?.Invoke(status);
    }

    private void Log(string message)
    {
        var timestamped = $"[{DateTime.Now:HH:mm:ss}] {message}";
        LogMessage?.Invoke(timestamped);
    }

    public void Dispose()
    {
        _cts?.Cancel();
        _cts?.Dispose();

        foreach (var (_, mat) in _menuTemplates)
            mat.Dispose();
        _menuTemplates.Clear();

        foreach (var (_, mat) in _digitTemplates)
            mat.Dispose();
        _digitTemplates.Clear();

        _minusTemplate?.Dispose();
        CoordinateOcr.Dispose();
        SeaMap.Dispose();
        _gameMemoryReader.Dispose();
    }

    #endregion
}

/// <summary>자동플레이 상태 열거형.</summary>
public enum AutoPlayState
{
    Idle,
    WaitingForGame,
    InCity,
    InPort,
    LeavingBuilding,
    Navigating,
    Arrived,
    Stopped,
    Error
}

/// <summary>자동플레이 상태 정보.</summary>
public class AutoPlayStatus
{
    public AutoPlayState State { get; init; }
    public string Message { get; init; } = string.Empty;
    public string DestinationName { get; init; } = string.Empty;
    public double CurrentLat { get; init; }
    public double CurrentLon { get; init; }
    public double Bearing { get; init; }
}
