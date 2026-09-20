using System.Drawing;
using OpenCvSharp;
using OpenCvSharp.Extensions;

namespace CdsHelper.Support.Local.Helpers;

/// <summary>
/// 게임 화면에서 바다/육지/배를 색상 기반으로 분류.
/// 출력: 2차원 int 배열 (0=육지, 1=바다, 2=배)
/// </summary>
public class SeaMapService : IDisposable
{
    /// <summary>출력 그리드 크기. 게임 화면을 이 크기로 나눔.</summary>
    public int GridCols { get; set; } = 128;
    public int GridRows { get; set; } = 96;

    /// <summary>상단바 비율 (화면 높이 대비). DPI 스케일링 무관.</summary>
    public double TopBarRatio { get; set; } = 0.05;

    /// <summary>하단바 비율 (화면 높이 대비).</summary>
    public double BottomBarRatio { get; set; } = 0.055;

    /// <summary>BGR에서 B-R 차이가 이 값 이상이면 바다 픽셀.</summary>
    public int SeaBRDiffThreshold { get; set; } = 10;

    /// <summary>바다로 판정할 셀 내 바다 픽셀 비율 임계값.</summary>
    public double SeaThreshold { get; set; } = 0.5;

    /// <summary>배 인식: 최소 blob 면적 (픽셀). 이보다 작으면 갈매기 등 노이즈.</summary>
    public int ShipMinArea { get; set; } = 100;

    /// <summary>배 인식: 최대 blob 면적 (픽셀). 이보다 크면 육지 등.</summary>
    public int ShipMaxArea { get; set; } = 5000;

    public event Action<string>? LogMessage;

    public SeaMapService() { }

    /// <summary>
    /// 게임 화면을 분석하여 바다/육지/배 그리드를 생성.
    /// </summary>
    /// <returns>int[행,열] — 0=육지, 1=바다, 2=배</returns>
    public int[,] Analyze(Bitmap screenshot)
    {
        using var full = BitmapConverter.ToMat(screenshot);
        return AnalyzeMat(full);
    }

    /// <summary>Mat 기반 분석.</summary>
    public int[,] AnalyzeMat(Mat full)
    {
        var grid = new int[GridRows, GridCols];

        // 게임 영역 (상단바, 하단바 제외)
        var gameTop = (int)(full.Rows * TopBarRatio);
        var gameBottom = (int)(full.Rows * (1.0 - BottomBarRatio));
        var gameHeight = gameBottom - gameTop;

        if (gameHeight <= 0 || full.Cols <= 0)
            return grid;

        using var gameArea = new Mat(full, new Rect(0, gameTop, full.Cols, gameHeight));

        // BGR 채널 분리 → B - R 차이로 바다 판정
        using var seaMask = new Mat(gameArea.Rows, gameArea.Cols, MatType.CV_8UC1);
        var channels = Cv2.Split(gameArea); // B, G, R
        using var b = channels[0];
        using var g = channels[1];
        using var r = channels[2];

        // B - R > threshold → 바다
        // byte 연산이므로 float 변환
        using var bFloat = new Mat();
        using var rFloat = new Mat();
        b.ConvertTo(bFloat, MatType.CV_32F);
        r.ConvertTo(rFloat, MatType.CV_32F);

        using var diff = new Mat();
        Cv2.Subtract(bFloat, rFloat, diff);

        // threshold 적용
        Cv2.Threshold(diff, seaMask, SeaBRDiffThreshold, 255, ThresholdTypes.Binary);
        seaMask.ConvertTo(seaMask, MatType.CV_8UC1);

        // 구름 제거: HSV에서 고밝기 + 저채도 픽셀 → 바다로 처리
        using var hsv = new Mat();
        Cv2.CvtColor(gameArea, hsv, ColorConversionCodes.BGR2HSV);
        var hsvChannels = Cv2.Split(hsv);
        using var sChannel = hsvChannels[1];
        using var vChannel = hsvChannels[2];
        hsvChannels[0].Dispose();

        using var lowSat = new Mat();
        using var highVal = new Mat();
        Cv2.Threshold(sChannel, lowSat, 80, 255, ThresholdTypes.BinaryInv);   // 채도 < 80
        Cv2.Threshold(vChannel, highVal, 120, 255, ThresholdTypes.Binary);    // 밝기 > 120
        using var cloudMask = new Mat();
        Cv2.BitwiseAnd(lowSat, highVal, cloudMask);
        Cv2.BitwiseOr(seaMask, cloudMask, seaMask); // 구름 영역을 바다로 편입

        // 갈매기 등 소형 노이즈 제거 (모폴로지 Opening)
        using var morphKernel = Cv2.GetStructuringElement(MorphShapes.Ellipse, new OpenCvSharp.Size(5, 5));
        Cv2.MorphologyEx(seaMask, seaMask, MorphTypes.Open, morphKernel);

        // 셀 크기
        var cellW = (double)gameArea.Cols / GridCols;
        var cellH = (double)gameArea.Rows / GridRows;

        // 각 셀의 바다 비율로 분류
        for (var row = 0; row < GridRows; row++)
        for (var col = 0; col < GridCols; col++)
        {
            var x = (int)(col * cellW);
            var y = (int)(row * cellH);
            var w = (int)Math.Min(cellW, gameArea.Cols - x);
            var h = (int)Math.Min(cellH, gameArea.Rows - y);

            if (w <= 0 || h <= 0) continue;

            using var cellMask = new Mat(seaMask, new Rect(x, y, w, h));
            var seaPixels = Cv2.CountNonZero(cellMask);
            var totalPixels = w * h;
            var seaRatio = (double)seaPixels / totalPixels;

            grid[row, col] = seaRatio >= SeaThreshold ? 1 : 0;
        }

        // 배 위치 찾기 (색상 기반)
        var shipPos = FindShip(gameArea, seaMask);
        if (shipPos.HasValue)
        {
            var shipCol = (int)(shipPos.Value.x / cellW);
            var shipRow = (int)(shipPos.Value.y / cellH);

            if (shipRow >= 0 && shipRow < GridRows && shipCol >= 0 && shipCol < GridCols)
                grid[shipRow, shipCol] = 2;
        }

        return grid;
    }

    /// <summary>
    /// 색상 기반 배 탐지: 바다 영역 내 고립된 non-sea blob 중 적절한 크기의 것을 배로 판정.
    /// 배는 바다 위의 어두운/갈색 물체이므로, 바다가 아닌 픽셀 덩어리를 찾는다.
    /// </summary>
    public (int x, int y)? FindShip(Mat gameArea, Mat seaMask)
    {
        // seaMask 반전 → non-sea 픽셀 (배, 갈매기, 해안선 근처 육지 조각 등)
        using var nonSeaMask = new Mat();
        Cv2.BitwiseNot(seaMask, nonSeaMask);

        // 모폴로지: 작은 노이즈(갈매기 등) 제거 후 근접 픽셀 연결
        using var openKernel = Cv2.GetStructuringElement(MorphShapes.Ellipse, new OpenCvSharp.Size(3, 3));
        Cv2.MorphologyEx(nonSeaMask, nonSeaMask, MorphTypes.Open, openKernel);
        using var closeKernel = Cv2.GetStructuringElement(MorphShapes.Ellipse, new OpenCvSharp.Size(5, 5));
        Cv2.MorphologyEx(nonSeaMask, nonSeaMask, MorphTypes.Close, closeKernel);

        // Connected Components로 blob 찾기
        using var labels = new Mat();
        using var stats = new Mat();
        using var centroids = new Mat();
        var nLabels = Cv2.ConnectedComponentsWithStats(nonSeaMask, labels, stats, centroids);

        // 적절한 크기의 blob 중 가장 큰 것 = 배
        var bestArea = 0;
        (int x, int y)? bestCenter = null;

        for (var i = 1; i < nLabels; i++) // 0 = 배경
        {
            var area = stats.At<int>(i, (int)ConnectedComponentsTypes.Area);

            if (area < ShipMinArea || area > ShipMaxArea)
                continue;

            if (area > bestArea)
            {
                bestArea = area;
                var cx = (int)centroids.At<double>(i, 0);
                var cy = (int)centroids.At<double>(i, 1);
                bestCenter = (cx, cy);
            }
        }

        if (bestCenter.HasValue)
            Log($"배 감지: ({bestCenter.Value.x}, {bestCenter.Value.y}), 면적={bestArea}px");
        else
            Log($"배 미감지 (blob {nLabels - 1}개, 범위 {ShipMinArea}~{ShipMaxArea} 내 없음)");

        return bestCenter;
    }

    /// <summary>배의 화면 절대 좌표 (상단바 포함).</summary>
    public (int x, int y)? FindShipScreen(Bitmap screenshot)
    {
        using var full = BitmapConverter.ToMat(screenshot);
        var gameTop = (int)(full.Rows * TopBarRatio);
        var gameBottom = (int)(full.Rows * (1.0 - BottomBarRatio));
        var gameHeight = gameBottom - gameTop;
        if (gameHeight <= 0) return null;

        using var gameArea = new Mat(full, new Rect(0, gameTop, full.Cols, gameHeight));

        // 바다 마스크 생성
        var channels = Cv2.Split(gameArea);
        using var b = channels[0];
        using var g = channels[1];
        using var r = channels[2];
        using var bFloat = new Mat();
        using var rFloat = new Mat();
        b.ConvertTo(bFloat, MatType.CV_32F);
        r.ConvertTo(rFloat, MatType.CV_32F);
        using var diff = new Mat();
        Cv2.Subtract(bFloat, rFloat, diff);
        using var seaMask = new Mat();
        Cv2.Threshold(diff, seaMask, SeaBRDiffThreshold, 255, ThresholdTypes.Binary);
        seaMask.ConvertTo(seaMask, MatType.CV_8UC1);

        var pos = FindShip(gameArea, seaMask);
        if (pos == null) return null;

        return (pos.Value.x, pos.Value.y + gameTop);
    }

    /// <summary>디버그용: 그리드를 시각화한 Mat 생성.</summary>
    public Mat VisualizeGrid(int[,] grid)
    {
        var rows = grid.GetLength(0);
        var cols = grid.GetLength(1);
        var cellSize = 6;
        var vis = new Mat(rows * cellSize, cols * cellSize, MatType.CV_8UC3);

        for (var r = 0; r < rows; r++)
        for (var c = 0; c < cols; c++)
        {
            var color = grid[r, c] switch
            {
                0 => new Scalar(60, 130, 180),   // 육지 (갈색 BGR)
                1 => new Scalar(200, 120, 50),    // 바다 (파란 BGR)
                2 => new Scalar(200, 240, 255),   // 배 (상아색 BGR)
                _ => new Scalar(0, 0, 0)
            };

            Cv2.Rectangle(vis,
                new Rect(c * cellSize, r * cellSize, cellSize, cellSize),
                color, -1);
        }

        return vis;
    }

    /// <summary>디버그용: 분석 결과를 파일로 저장.</summary>
    public void SaveDebugImage(Bitmap screenshot, string path)
    {
        var grid = Analyze(screenshot);
        using var vis = VisualizeGrid(grid);
        Cv2.ImWrite(path, vis);
    }

    private void Log(string message)
    {
        LogMessage?.Invoke($"[{DateTime.Now:HH:mm:ss}] [해도] {message}");
    }

    public void Dispose() { }
}
