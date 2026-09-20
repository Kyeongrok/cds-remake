using System.Drawing;
using System.IO;
using OpenCvSharp;
using OpenCvSharp.Extensions;

namespace CdsHelper.Support.Local.Helpers;

/// <summary>
/// 직업 선택 화면에서 능력치(지력 등)가 목표치 이상이 될 때까지
/// 두 직업 버튼을 번갈아 클릭하여 능력치를 리롤하는 서비스.
/// 게임 화면을 캡처하여 다이얼로그, 버튼, 능력치 영역을 자동 감지한다.
/// </summary>
public class StatRerollService : IDisposable
{
    private CancellationTokenSource? _cts;
    private Task? _runningTask;
    private readonly Dictionary<int, Mat> _digitTemplates = new();
    private readonly string _numberTemplateDir;
    private readonly Lazy<OnnxDigitRecognizer> _onnx = new(() => new OnnxDigitRecognizer());

    public bool IsRunning => _runningTask is { IsCompleted: false };
    public string NumberTemplateDir => _numberTemplateDir;

    /// <summary>
    /// 사용자가 지정한 클릭 좌표 (게임 클라이언트 기준). null이면 자동 감지한 사냥꾼 버튼을 클릭한다.
    /// </summary>
    public (int X, int Y)? CustomClickPoint { get; set; }

    public event Action<string>? LogMessage;
    public event Action<int, int>? Progress;
    public event Action<int, int>? Completed;
    public event Action? Stopped;
    public event Action? TemplatesChanged;

    /// <summary>감지된 레이아웃 정보.</summary>
    public record DetectedLayout(
        Rect Dialog,
        Rect[] StatRois,            // 10개 박스 (5행 × 십/일의자리)
        OpenCvSharp.Point HunterBtn // 사냥꾼 버튼 중심
    );

    public StatRerollService(string? templateBaseDir = null)
    {
        var baseDir = templateBaseDir
            ?? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "templates");
        _numberTemplateDir = Path.Combine(baseDir, "numbers");
        LoadDigitTemplates();
    }

    /// <summary>
    /// 마지막으로 리롤이 끝난 이유. <see cref="Stopped"/> 직전에 채워지므로
    /// UI가 "중지됨" 대신 실제 원인을 그대로 보여줄 수 있다.
    /// </summary>
    public string LastStopReason { get; private set; } = "중지됨";

    /// <summary>리롤을 시작한다. targets: 체력,지력,무력,매력,운,보너스 (0=무시)</summary>
    public void Start(int[] targets, int clickDelay = 300, int maxAttempts = 50)
    {
        // 이전 실행이 아직 살아 있으면 조용히 무시하지 않고 중지 이벤트를 돌려준다.
        // (그냥 return하면 UI가 "감지 중..." 상태로 굳고 시작 버튼이 영영 안 풀린다)
        if (IsRunning)
        {
            Fail("이전 리롤이 아직 실행 중입니다. 잠시 후 다시 시도하세요.");
            Stopped?.Invoke();
            return;
        }

        _cts = new CancellationTokenSource();
        _runningTask = Task.Run(() => RunLoop(targets, clickDelay, maxAttempts, _cts.Token));
    }

    public void Stop()
    {
        _cts?.Cancel();
        LastStopReason = "사용자 중지";
        Log("리롤 중지됨");
        Stopped?.Invoke();
    }

    /// <summary>중지 사유를 로그와 <see cref="LastStopReason"/>에 함께 남긴다.</summary>
    private void Fail(string reason)
    {
        LastStopReason = reason;
        Log(reason);
    }

    /// <summary>테스트: 모든 능력치 읽기.</summary>
    public int TestRead()
    {
        var (gray, bitmap, layout) = CaptureAndDetect();
        if (layout == null || bitmap == null) return -1;

        using (gray) using (bitmap)
        {
            if (layout.StatRois.Length < 4) { Log("박스 부족"); return -1; }

            string[] names = { "체력", "지력", "무력", "매력", "운", "보너스" };
            var results = new List<string>();
            for (int i = 0; i < Math.Min(6, layout.StatRois.Length / 2); i++)
            {
                int val = ReadTwoDigits(bitmap, layout.StatRois[i * 2], layout.StatRois[i * 2 + 1]);
                results.Add($"{names[i]}={val}");
            }
            Log($"인식 결과: {string.Join(", ", results)}");

            // 지력 값 반환
            return ReadTwoDigits(bitmap, layout.StatRois[2], layout.StatRois[3]);
        }
    }

    /// <summary>
    /// 사용자가 게임 화면에서 클릭할 위치를 직접 찍게 한다.
    /// 반환값은 게임 클라이언트 기준 좌표이며, 취소/시간초과 시 null.
    /// </summary>
    public async Task<(int x, int y)?> PickClickPointAsync(int timeoutMs = 15000)
    {
        var hWnd = GameWindowHelper.FindGameWindow();
        if (hWnd == IntPtr.Zero) { Log("게임 윈도우를 찾을 수 없습니다."); return null; }

        GameWindowHelper.BringToFront(hWnd);
        Log($"게임 화면에서 리롤에 사용할 버튼을 좌클릭하세요. (ESC 또는 {timeoutMs / 1000}초 경과 시 취소)");

        var point = await GameWindowHelper.WaitForClientClickAsync(hWnd, timeoutMs);
        if (point == null)
        {
            Log("좌표 지정이 취소되었습니다.");
            return null;
        }

        Log($"클릭 좌표 지정됨: ({point.Value.x},{point.Value.y})");
        return point;
    }

    /// <summary>지정된 클릭 좌표(없으면 자동 감지 사냥꾼 버튼)를 한 번 클릭해 본다.</summary>
    public void TestClickPoint()
    {
        var hWnd = GameWindowHelper.FindGameWindow();
        if (hWnd == IntPtr.Zero) { Log("게임 윈도우를 찾을 수 없습니다."); return; }

        (int X, int Y) point;
        if (CustomClickPoint is { } custom)
        {
            point = custom;
            GameWindowHelper.BringToFront(hWnd);
            Thread.Sleep(300);
        }
        else
        {
            var (gray, bitmap, layout) = CaptureAndDetect();
            if (layout == null) return;
            using (gray) using (bitmap) { point = (layout.HunterBtn.X, layout.HunterBtn.Y); }
        }

        Log($"테스트 클릭: ({point.X},{point.Y})");
        GameWindowHelper.SendClickRelative(hWnd, point.X, point.Y);
    }

    /// <summary>자동 감지한 사냥꾼 버튼 좌표를 반환한다. 감지 실패 시 null.</summary>
    public (int x, int y)? GetDetectedHunterPoint()
    {
        var (gray, bitmap, layout) = CaptureAndDetect();
        if (layout == null) return null;
        using (gray) using (bitmap) { return (layout.HunterBtn.X, layout.HunterBtn.Y); }
    }

    /// <summary>5개 능력치를 한 번에 학습한다. 입력: "79,50,74,59,80" (체력,지력,무력,매력,운).</summary>
    public void LearnDigits(string allValues)
    {
        // 입력 파싱
        var values = allValues
            .Split(new[] { ',', ' ', '/' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(s => s.Trim())
            .Where(s => int.TryParse(s, out _))
            .Select(int.Parse)
            .ToList();

        if (values.Count == 0 || values.Count > 6)
        {
            Log("능력치 값을 입력하세요 (예: 79,50,74,59,80)");
            return;
        }

        var (gray, bitmap, layout) = CaptureAndDetect();
        if (layout == null || gray == null || bitmap == null) return;

        using (gray) using (bitmap)
        {
            // 5개 값 → 10자리 (십의자리, 일의자리 교차)
            var allDigits = new List<int>();
            foreach (var v in values)
            {
                string s = v.ToString().PadLeft(2, '0');
                allDigits.Add(s[0] - '0');
                allDigits.Add(s[1] - '0');
            }

            int count = Math.Min(allDigits.Count, layout.StatRois.Length);
            Log($"입력 {values.Count}개 → {count}자리 학습 (박스 {layout.StatRois.Length}개)");
            Directory.CreateDirectory(_numberTemplateDir);
            int savedCount = 0;

            for (int i = 0; i < count; i++)
            {
                var roi = ClampRect(layout.StatRois[i], gray.Cols, gray.Rows);
                if (roi.Width <= 0 || roi.Height <= 0) continue;

                using var roiMat = new Mat(gray, roi);
                using var binary = new Mat();
                Cv2.Threshold(roiMat, binary, 0, 255, ThresholdTypes.Binary | ThresholdTypes.Otsu);

                var path = Path.Combine(_numberTemplateDir, $"{allDigits[i]}.png");
                if (File.Exists(path))
                {
                    Log($"  [{i}] '{allDigits[i]}' 이미 존재 — 건너뜀");
                    continue;
                }
                Cv2.ImWrite(path, binary);
                savedCount++;
            }

            Log($"학습: {savedCount}자리 저장");

            // 템플릿 리로드
            LoadDigitTemplates();
            Log($"학습 완료! {savedCount}개 저장, 총 {_digitTemplates.Count}종 템플릿 사용 가능");

            // 부족한 숫자 확인
            var missing = Enumerable.Range(0, 10).Where(d => !_digitTemplates.ContainsKey(d)).ToList();
            if (missing.Count > 0)
                Log($"미학습 숫자: {string.Join(", ", missing)} — 다른 값이 보일 때 추가 학습하세요");
        }
    }

    /// <summary>ONNX 모델로 화면의 숫자를 자동 인식하여 템플릿을 생성한다.</summary>
    public void AutoLearnDigits()
    {
        var (gray, bitmap, layout) = CaptureAndDetect();
        if (layout == null || gray == null || bitmap == null)
        {
            Log("게임 화면 감지 실패");
            return;
        }

        using (gray) using (bitmap)
        {
            int count = layout.StatRois.Length;
            Log($"자동 학습: {count}개 박스 감지, ONNX 인식 중...");
            Directory.CreateDirectory(_numberTemplateDir);
            int savedCount = 0;

            for (int i = 0; i < count; i++)
            {
                var roi = ClampRect(layout.StatRois[i], gray.Cols, gray.Rows);
                if (roi.Width <= 0 || roi.Height <= 0) continue;

                using var roiMat = new Mat(gray, roi);
                using var binary = new Mat();
                Cv2.Threshold(roiMat, binary, 0, 255, ThresholdTypes.Binary | ThresholdTypes.Otsu);

                int digit;
                float conf;
                try { (digit, conf) = _onnx.Value.Recognize(binary, 0.5f); }
                catch (Exception ex) { Log($"ONNX 오류: {ex.Message}"); return; }

                if (digit < 0)
                {
                    Log($"  [{i}] 인식 실패 (conf={conf:P0})");
                    continue;
                }

                Log($"  [{i}] → '{digit}' (conf={conf:P0})");

                var path = Path.Combine(_numberTemplateDir, $"{digit}.png");
                if (File.Exists(path))
                    continue;
                Cv2.ImWrite(path, binary);
                savedCount++;
            }

            LoadDigitTemplates();
            Log($"자동 학습 완료! {savedCount}개 저장, 총 {_digitTemplates.Count}종 템플릿");

            var missing = Enumerable.Range(0, 10).Where(d => !_digitTemplates.ContainsKey(d)).ToList();
            if (missing.Count > 0)
                Log($"미학습 숫자: {string.Join(", ", missing)}");
        }
    }

    #region 레이아웃 자동 감지

    /// <summary>게임 윈도우를 활성화하고 캡처 + 레이아웃 감지.</summary>
    private (Mat? gray, Bitmap? bitmap, DetectedLayout? layout) CaptureAndDetect()
    {
        var hWnd = GameWindowHelper.FindGameWindow();
        if (hWnd == IntPtr.Zero) { Log("게임 윈도우를 찾을 수 없습니다."); return (null, null, null); }

        GameWindowHelper.BringToFront(hWnd);
        Thread.Sleep(500);

        var (cw, ch) = GameWindowHelper.GetClientSize(hWnd);
        Log($"클라이언트 크기: {cw}x{ch}");

        var bitmap = GameWindowHelper.CaptureClient(hWnd);
        if (bitmap == null) { Log("화면 캡처 실패"); return (null, null, null); }

        var mat = BitmapConverter.ToMat(bitmap);
        var gray = new Mat();
        Cv2.CvtColor(mat, gray, ColorConversionCodes.BGR2GRAY);

        Directory.CreateDirectory(_numberTemplateDir);
        mat.Dispose();

        var layout = DetectLayout(gray);
        if (layout == null) { gray.Dispose(); bitmap.Dispose(); return (null, null, null); }

        Log($"다이얼로그: ({layout.Dialog.X},{layout.Dialog.Y}) {layout.Dialog.Width}x{layout.Dialog.Height}");

        // 디버그: 캡처 위에 감지 영역 표시
        using var debugMat = BitmapConverter.ToMat(bitmap);
        Cv2.Rectangle(debugMat, layout.Dialog, new Scalar(0, 255, 0), 1);
        for (int i = 0; i < layout.StatRois.Length; i++)
            Cv2.Rectangle(debugMat, layout.StatRois[i], new Scalar(0, 0, 255), 1);
        Cv2.Circle(debugMat, layout.HunterBtn, 5, new Scalar(255, 0, 0), 1);
        if (CustomClickPoint is { } custom)
            Cv2.Circle(debugMat, new OpenCvSharp.Point(custom.X, custom.Y),
                5, new Scalar(0, 255, 255), 1); // 사용자 지정 좌표 (노랑)

        var debugPath = Path.Combine(_numberTemplateDir, $"debug_{DateTime.Now:HHmmss}.png");
        Cv2.ImWrite(debugPath, debugMat);
        Log($"디버그 이미지 저장: {debugPath}");

        return (gray, bitmap, layout);
    }

    /// <summary>게임 화면에서 능력치 다이얼로그, 버튼, 5개 능력치 행 영역을 자동 감지한다.</summary>
    private DetectedLayout? DetectLayout(Mat gray)
    {
        // 1) 어두운 다이얼로그 영역 찾기
        using var darkMask = new Mat();
        Cv2.Threshold(gray, darkMask, 70, 255, ThresholdTypes.BinaryInv);

        using var kernel = Cv2.GetStructuringElement(MorphShapes.Rect, new OpenCvSharp.Size(7, 7));
        using var closed = new Mat();
        Cv2.MorphologyEx(darkMask, closed, MorphTypes.Close, kernel);

        Cv2.FindContours(closed, out var contours, out _,
            RetrievalModes.External, ContourApproximationModes.ApproxSimple);

        // 적절한 크기/비율의 어두운 사각형 중 가장 큰 것 = 다이얼로그
        // 위치 제한 없음 (다이얼로그는 화면 어디에나 있을 수 있음)
        Rect? dialogRect = null;
        double maxArea = 0;

        foreach (var contour in contours)
        {
            var r = Cv2.BoundingRect(contour);
            var area = (double)r.Width * r.Height;
            var screenArea = (double)gray.Cols * gray.Rows;

            // 크기: 화면의 1~40%
            if (area < screenArea * 0.01 || area > screenArea * 0.40) continue;

            // 가로세로 비율: 1.0 ~ 2.5
            double aspect = (double)r.Width / r.Height;
            if (aspect < 1.0 || aspect > 2.5) continue;

            if (area > maxArea) { maxArea = area; dialogRect = r; }
        }

        if (dialogRect == null)
        {
            Log($"다이얼로그 영역을 찾을 수 없습니다. (후보 윤곽: {contours.Length}개)");
            return null;
        }
        var dlg = dialogRect.Value;

        // 2) 숫자 영역: 1자리씩 10개 박스 (5행 × 십의자리/일의자리)
        //    실측: 다이얼로그(288x208) 내 십의자리 (79,24)-(87,38), 일의자리 (87,24)-(95,38)
        double tensXPct = 0.277;  // 79/288
        double onesXPct = 0.305;  // 87/288
        double digitWPct = 0.028; // 8/288
        double digitHPct = 0.080; // 14/208
        double[] rowYPcts = { 0.116, 0.235, 0.350, 0.465, 0.580 };

        int tensX = dlg.X + (int)(dlg.Width * tensXPct);
        int onesX = dlg.X + (int)(dlg.Width * onesXPct);
        int digitW = Math.Max(1, (int)(dlg.Width * digitWPct));
        int digitH = Math.Max(1, (int)(dlg.Height * digitHPct));

        // 보너스 포인트 위치 (다이얼로그 내)
        double bonusTensXPct = 0.360;
        double bonusOnesXPct = 0.388;
        double bonusYPct = 0.81;

        var statRois = rowYPcts.SelectMany(yPct =>
        {
            int y = dlg.Y + (int)(dlg.Height * yPct);
            return new[]
            {
                new Rect(tensX, y, digitW, digitH),
                new Rect(onesX, y, digitW, digitH),
            };
        }).Concat(new[] // 보너스 포인트 (인덱스 10, 11)
        {
            new Rect(dlg.X + (int)(dlg.Width * bonusTensXPct), dlg.Y + (int)(dlg.Height * bonusYPct), digitW, digitH),
            new Rect(dlg.X + (int)(dlg.Width * bonusOnesXPct), dlg.Y + (int)(dlg.Height * bonusYPct), digitW, digitH),
        }).ToArray();

        Log($"숫자 영역 6행 (다이얼로그 {dlg.Width}x{dlg.Height} 기준):");
        for (int i = 0; i < statRois.Length; i++)
            Log($"  행{i}: ({statRois[i].X},{statRois[i].Y}) {statRois[i].Width}x{statRois[i].Height}");

        // 3) 사냥꾼 버튼 위치: 고정 비율 (3번째 직업 버튼)
        //    다이얼로그 우측 중앙, Y는 3번째 행 ≈ 무력과 같은 높이
        var hunterBtn = new OpenCvSharp.Point(
            dlg.X + (int)(dlg.Width * 0.75),
            dlg.Y + (int)(dlg.Height * 0.47));

        Log($"사냥꾼 버튼: ({hunterBtn.X},{hunterBtn.Y})");
        return new DetectedLayout(dlg, statRois, hunterBtn);
    }

    #endregion

    #region 리롤 루프

    /// <summary>
    /// 어떤 경로로 끝나든(감지 실패·예외 포함) 반드시 <see cref="Stopped"/>를 한 번 발생시킨다.
    /// 이게 없으면 UI가 "감지 중..." 상태로 멈춘 채 시작 버튼이 다시 활성화되지 않는다.
    /// </summary>
    private async Task RunLoop(int[] targets, int delay, int maxAttempts, CancellationToken token)
    {
        try
        {
            await RunLoopCore(targets, delay, maxAttempts, token);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            Fail($"리롤 중단 (예외): {ex.Message}");
        }
        finally
        {
            Stopped?.Invoke();
        }
    }

    private async Task RunLoopCore(int[] targets, int delay, int maxAttempts, CancellationToken token)
    {
        LastStopReason = "중지됨";

        var hWnd = GameWindowHelper.FindGameWindow();
        if (hWnd == IntPtr.Zero) { Fail("게임 윈도우를 찾을 수 없습니다."); return; }

        GameWindowHelper.BringToFront(hWnd);
        await Task.Delay(500, token);

        using var initBitmap = GameWindowHelper.CaptureClient(hWnd);
        if (initBitmap == null) { Fail("화면 캡처 실패"); return; }

        using var initMat = BitmapConverter.ToMat(initBitmap);
        using var initGray = new Mat();
        Cv2.CvtColor(initMat, initGray, ColorConversionCodes.BGR2GRAY);

        var layout = DetectLayout(initGray);
        if (layout == null) { Fail("다이얼로그 감지 실패 — 직업 선택 화면이 열려 있는지 확인하세요."); return; }

        if (layout.StatRois.Length < 4)
        {
            Fail("지력 박스를 감지할 수 없습니다.");
            return;
        }

        // 클릭 좌표: 사용자 지정이 있으면 그것을, 없으면 자동 감지한 사냥꾼 버튼을 사용
        var clickPoint = CustomClickPoint ?? (layout.HunterBtn.X, layout.HunterBtn.Y);
        Log(CustomClickPoint != null
            ? $"클릭 위치: 사용자 지정 ({clickPoint.X},{clickPoint.Y})"
            : $"클릭 위치: 자동 감지 사냥꾼 버튼 ({clickPoint.X},{clickPoint.Y})");

        // 0~9 템플릿이 모두 없으면 사냥꾼 클릭 → 자동학습 반복 (최대 20회)
        var missingDigits = Enumerable.Range(0, 10).Where(d => !_digitTemplates.ContainsKey(d)).ToList();
        if (missingDigits.Count > 0)
        {
            Log($"미학습 숫자: {string.Join(", ", missingDigits)} — 자동 학습 시작");
            for (int autoAttempt = 0; autoAttempt < 20 && !token.IsCancellationRequested; autoAttempt++)
            {
                if (IsHotkeyPressed())
                {
                    Fail("Ctrl+Alt 감지 — 자동 학습 중지");
                    return;
                }

                // 직업 버튼 클릭 (새 능력치 생성)
                GameWindowHelper.SendClickRelative(hWnd, clickPoint.X, clickPoint.Y);
                await Task.Delay(delay > 0 ? delay : 100, token);

                // 자동 학습
                AutoLearnDigits();

                TemplatesChanged?.Invoke();

                missingDigits = Enumerable.Range(0, 10).Where(d => !_digitTemplates.ContainsKey(d)).ToList();
                if (missingDigits.Count == 0)
                {
                    Log($"자동 학습 완료! ({autoAttempt + 1}회 시도) — 0~9 모두 확보");
                    break;
                }
                Log($"[자동학습 {autoAttempt + 1}/20] 미학습: {string.Join(", ", missingDigits)}");
            }

            if (missingDigits.Count > 0)
            {
                Fail($"20회 시도 후에도 미학습 숫자 존재: {string.Join(", ", missingDigits)} — 리롤 중단");
                return;
            }
        }

        string[] names = { "체력", "지력", "무력", "매력", "운", "보너스" };
        var goalParts = new List<string>();
        for (int i = 0; i < Math.Min(targets.Length, 6); i++)
            if (targets[i] > 0) goalParts.Add($"{names[i]}>={targets[i]}");
        Log($"리롤 시작 — 목표: {string.Join(", ", goalParts)}, 최대 {maxAttempts}회 (템플릿 {_digitTemplates.Count}종)");

        int attempts = 0;

        while (!token.IsCancellationRequested && attempts < maxAttempts)
        {
            try
            {
                if (IsHotkeyPressed())
                {
                    Fail("Ctrl+Alt 감지 — 리롤 중지");
                    return;
                }

                hWnd = GameWindowHelper.FindGameWindow();
                if (hWnd == IntPtr.Zero) { await Task.Delay(2000, token); continue; }

                // 직업 버튼 클릭
                var (ox, oy) = GameWindowHelper.GetClientOrigin(hWnd);
                var (cw, ch) = GameWindowHelper.GetClientSize(hWnd);
                int screenX = ox + clickPoint.X;
                int screenY = oy + clickPoint.Y;
                if (attempts == 0)
                {
                    // 좌표는 모두 물리 픽셀. dpi는 진단용 표시일 뿐 좌표 계산에 쓰지 않는다.
                    Log($"클릭좌표: client({clickPoint.X},{clickPoint.Y}) origin({ox},{oy}) screen({screenX},{screenY}) " +
                        $"clientSize({cw},{ch}) 화면배율={GameWindowHelper.GetDpiScale(hWnd):P0}");
                }
                GameWindowHelper.SendClickRelative(hWnd, clickPoint.X, clickPoint.Y);
                attempts++;

                await Task.Delay(delay, token);

                using var bitmap = GameWindowHelper.CaptureClient(hWnd);
                if (bitmap == null) continue;

                // 모든 능력치 읽기
                int statCount = Math.Min(6, layout.StatRois.Length / 2);
                var readValues = new int[statCount];
                var vals = new List<string>();
                int totalSum = 0;
                for (int i = 0; i < statCount; i++)
                {
                    readValues[i] = ReadTwoDigits(bitmap, layout.StatRois[i * 2], layout.StatRois[i * 2 + 1]);
                    vals.Add($"{names[i]}={readValues[i]}");
                    if (readValues[i] > 0) totalSum += readValues[i];
                }
                vals.Add($"합계={totalSum}");

                Progress?.Invoke(readValues.Length > 1 ? readValues[1] : -1, attempts);
                Log($"[{attempts}회] {string.Join(", ", vals)}");

                // 목표 달성 조건: 지정된 목표(>0) 모두 충족
                // 인식 실패(-1)인 항목은 미충족 처리
                bool hasTarget = false;
                bool allMet = true;
                for (int i = 0; i < Math.Min(targets.Length, statCount); i++)
                {
                    if (targets[i] <= 0) continue;
                    hasTarget = true;
                    if (readValues[i] < targets[i]) { allMet = false; break; }
                }

                if (hasTarget && allMet)
                {
                    Log($"[{attempts}회] 목표 달성!");
                    Completed?.Invoke(readValues.Length > 1 ? readValues[1] : 0, attempts);
                    return;
                }
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                Log($"오류: {ex.Message}");
                await Task.Delay(2000, token);
            }
        }

        if (attempts >= maxAttempts)
            Fail($"최대 시도 횟수({maxAttempts}회) 도달 — 중지");
    }

    #endregion

    #region 숫자 인식

    /// <summary>2개 박스에서 십의자리 + 일의자리를 읽어 2자리 값을 반환한다.</summary>
    private int ReadTwoDigits(Bitmap screenshot, Rect tensRoi, Rect onesRoi)
    {
        int tens = ReadSingleDigit(screenshot, tensRoi);
        int ones = ReadSingleDigit(screenshot, onesRoi);
        if (tens < 0 || ones < 0) return -1;
        return tens * 10 + ones;
    }

    /// <summary>1개 박스에서 숫자 하나를 읽는다.</summary>
    private int ReadSingleDigit(Bitmap screenshot, Rect roi)
    {
        using var screenMat = BitmapConverter.ToMat(screenshot);
        using var gray = new Mat();
        Cv2.CvtColor(screenMat, gray, ColorConversionCodes.BGR2GRAY);

        var clamped = ClampRect(roi, gray.Cols, gray.Rows);
        if (clamped.Width <= 0 || clamped.Height <= 0) return -1;

        using var roiMat = new Mat(gray, clamped);
        using var binary = new Mat();
        Cv2.Threshold(roiMat, binary, 0, 255, ThresholdTypes.Binary | ThresholdTypes.Otsu);

        return MatchDigit(binary);
    }

    private int MatchDigit(Mat digitMat)
    {
        if (_digitTemplates.Count == 0) return -1;

        int bestDigit = -1;
        double bestScore = -1;

        foreach (var (digit, template) in _digitTemplates)
        {
            using var resized = new Mat();
            Cv2.Resize(digitMat, resized, new OpenCvSharp.Size(template.Cols, template.Rows));

            using var result = new Mat();
            Cv2.MatchTemplate(resized, template, result, TemplateMatchModes.CCoeffNormed);
            var score = result.At<float>(0, 0);

            if (score > bestScore) { bestScore = score; bestDigit = digit; }
        }

        return bestScore > 0.5 ? bestDigit : -1;
    }

    public void ClearTemplates()
    {
        foreach (var (_, m) in _digitTemplates) m.Dispose();
        _digitTemplates.Clear();

        if (Directory.Exists(_numberTemplateDir))
        {
            foreach (var f in Directory.GetFiles(_numberTemplateDir, "*.png"))
                File.Delete(f);
        }
        Log("템플릿 초기화 완료");
    }

    private void LoadDigitTemplates()
    {
        foreach (var (_, m) in _digitTemplates) m.Dispose();
        _digitTemplates.Clear();
        if (!Directory.Exists(_numberTemplateDir)) return;

        for (int d = 0; d <= 9; d++)
        {
            var path = Path.Combine(_numberTemplateDir, $"{d}.png");
            if (!File.Exists(path)) continue;
            var mat = Cv2.ImRead(path, ImreadModes.Grayscale);
            if (!mat.Empty()) _digitTemplates[d] = mat;
        }

        if (_digitTemplates.Count > 0)
            Log($"숫자 템플릿 {_digitTemplates.Count}종 로드됨");
    }

    #endregion

    #region 유틸

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);

    private const int VK_CONTROL = 0x11;
    private const int VK_MENU = 0x12; // Alt

    private bool IsHotkeyPressed()
        => (GetAsyncKeyState(VK_CONTROL) & 0x8000) != 0
        && (GetAsyncKeyState(VK_MENU) & 0x8000) != 0;

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int nIndex);

    private static Mat? CaptureFullScreen()
    {
        try
        {
            int w = GetSystemMetrics(0); // SM_CXSCREEN
            int h = GetSystemMetrics(1); // SM_CYSCREEN
            using var bmp = new Bitmap(w, h);
            using var g = Graphics.FromImage(bmp);
            g.CopyFromScreen(0, 0, 0, 0, new System.Drawing.Size(w, h));
            return BitmapConverter.ToMat(bmp);
        }
        catch { return null; }
    }

    private static Rect ClampRect(Rect r, int maxW, int maxH)
    {
        int x = Math.Clamp(r.X, 0, maxW - 1);
        int y = Math.Clamp(r.Y, 0, maxH - 1);
        int w = Math.Min(r.Width, maxW - x);
        int h = Math.Min(r.Height, maxH - y);
        return new Rect(x, y, Math.Max(0, w), Math.Max(0, h));
    }

    private void Log(string msg) => LogMessage?.Invoke($"[{DateTime.Now:HH:mm:ss}] {msg}");

    public void Dispose()
    {
        _cts?.Cancel(); _cts?.Dispose();
        foreach (var (_, m) in _digitTemplates) m.Dispose();
        _digitTemplates.Clear();
    }

    #endregion
}
