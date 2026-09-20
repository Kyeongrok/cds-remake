using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using CdsHelper.Main.Local.ViewModels;
using CdsHelper.Support.Local.Helpers;
using CdsHelper.Support.Local.Models;
using CdsHelper.Support.Local.Settings;
using Ellipse = System.Windows.Shapes.Ellipse;
using Rectangle = System.Windows.Shapes.Rectangle;
using Path = System.IO.Path;

namespace CdsHelper.Main.UI.Views;

/// <summary>
/// 발견물 좌표 편집 다이얼로그.
/// 세계지도 위에서 좌클릭=From, 우클릭=To 로 좌표 지정.
/// </summary>
public class EditDiscoveryDialog : Window
{
    private readonly TextBox _txtLatFrom;
    private readonly TextBox _txtLatTo;
    private readonly TextBox _txtLonFrom;
    private readonly TextBox _txtLonTo;
    private readonly Canvas _overlayCanvas;
    private readonly Image _mapImage;
    private readonly ScrollViewer _scrollViewer;
    private readonly TextBlock _txtCoordPreview;

    private const double MarkerSize = 8;
    private Ellipse? _fromMarker;
    private Ellipse? _toMarker;
    private Rectangle? _rangeRect;

    // 줌/팬 상태
    private readonly ScaleTransform _scaleTransform = new(1.0, 1.0);
    // 마커가 줌과 무관하게 화면상 크기를 유지하도록 1/scale로 역보정
    private readonly ScaleTransform _markerCounterScale = new(1.0, 1.0);
    private Point _mouseDownPos;
    private double _mouseDownHOffset;
    private double _mouseDownVOffset;
    private bool _isDragging;
    private const double DragThreshold = 4.0;
    private const double MinScale = 0.3;
    private const double MaxScale = 6.0;
    private const double ScaleFactor = 1.2;

    public int? LatFrom { get; private set; }
    public int? LatTo { get; private set; }
    public int? LonFrom { get; private set; }
    public int? LonTo { get; private set; }

    public EditDiscoveryDialog(DiscoveryDisplayItem item)
    {
        Title = $"발견물 좌표 편집 - {item.Name}";
        Width = 1100;
        Height = 720;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;

        var root = new Grid { Margin = new Thickness(10) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });       // 제목
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });       // 좌표 입력
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });       // 안내
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // 지도
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });       // 버튼

        // 0행: 발견물 이름
        var lblName = new TextBlock
        {
            Text = item.Name,
            FontSize = 16,
            FontWeight = FontWeights.Bold,
            Margin = new Thickness(0, 0, 0, 6)
        };
        Grid.SetRow(lblName, 0);
        root.Children.Add(lblName);

        // 1행: 좌표 입력 패널
        var coordPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 0, 0, 4)
        };
        _txtLatFrom = CreateCoordBox(item.LatFrom);
        _txtLatTo = CreateCoordBox(item.LatTo);
        _txtLonFrom = CreateCoordBox(item.LonFrom);
        _txtLonTo = CreateCoordBox(item.LonTo);

        coordPanel.Children.Add(new TextBlock { Text = "위도:", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 4, 0) });
        coordPanel.Children.Add(_txtLatFrom);
        coordPanel.Children.Add(new TextBlock { Text = "~", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 4, 0) });
        coordPanel.Children.Add(_txtLatTo);
        coordPanel.Children.Add(new TextBlock { Text = "   경도:", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 4, 0) });
        coordPanel.Children.Add(_txtLonFrom);
        coordPanel.Children.Add(new TextBlock { Text = "~", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 4, 0) });
        coordPanel.Children.Add(_txtLonTo);

        var btnClear = new Button { Content = "초기화", Width = 60, Height = 24, Margin = new Thickness(12, 0, 0, 0) };
        btnClear.Click += (_, _) => { ClearAll(); };
        coordPanel.Children.Add(btnClear);
        Grid.SetRow(coordPanel, 1);
        root.Children.Add(coordPanel);

        // 2행: 안내 + 좌표 미리보기
        var infoPanel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 6) };
        infoPanel.Children.Add(new TextBlock
        {
            Text = "좌클릭 = From, 우클릭 = To (N=양수, S=음수 / E=양수, W=음수)",
            Foreground = Brushes.Gray,
            FontSize = 11,
            VerticalAlignment = VerticalAlignment.Center
        });
        _txtCoordPreview = new TextBlock
        {
            Foreground = Brushes.DarkBlue,
            FontSize = 11,
            Margin = new Thickness(12, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center
        };
        infoPanel.Children.Add(_txtCoordPreview);
        Grid.SetRow(infoPanel, 2);
        root.Children.Add(infoPanel);

        // 3행: 지도 (ScrollViewer + Image + Canvas overlay)
        _mapImage = new Image
        {
            Stretch = Stretch.None,
            SnapsToDevicePixels = true
        };
        _overlayCanvas = new Canvas
        {
            Width = WorldMapRenderer.UnfoldedW,
            Height = WorldMapRenderer.CellH,
            Background = Brushes.Transparent
        };
        var mapLayer = new Grid
        {
            Width = WorldMapRenderer.UnfoldedW,
            Height = WorldMapRenderer.CellH,
            LayoutTransform = _scaleTransform
        };
        mapLayer.Children.Add(_mapImage);
        mapLayer.Children.Add(_overlayCanvas);

        _scrollViewer = new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Background = Brushes.DimGray,
            Content = mapLayer
        };
        Grid.SetRow(_scrollViewer, 3);
        root.Children.Add(_scrollViewer);

        // 드래그 팬: 버튼 다운에서 시작, 일정 거리 이상 움직이면 드래그로 판정, 그 외엔 클릭으로 From/To 설정
        _overlayCanvas.MouseLeftButtonDown += (_, e) => OnMouseDownStartDrag(e);
        _overlayCanvas.MouseRightButtonDown += (_, e) => OnMouseDownStartDrag(e);
        _overlayCanvas.MouseMove += OnOverlayMouseMove;
        _overlayCanvas.MouseLeftButtonUp += (_, e) => OnMouseUpFinishDrag(e, isFrom: true);
        _overlayCanvas.MouseRightButtonUp += (_, e) => OnMouseUpFinishDrag(e, isFrom: false);

        // 휠 줌
        _scrollViewer.PreviewMouseWheel += OnPreviewMouseWheel;

        // 4행: 버튼
        var btnPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 8, 0, 0)
        };
        var btnSave = new Button { Content = "저장", Width = 80, Height = 28, Margin = new Thickness(0, 0, 8, 0), IsDefault = true };
        btnSave.Click += (_, _) =>
        {
            LatFrom = ParseInt(_txtLatFrom.Text);
            LatTo = ParseInt(_txtLatTo.Text);
            LonFrom = ParseInt(_txtLonFrom.Text);
            LonTo = ParseInt(_txtLonTo.Text);
            DialogResult = true;
        };
        var btnCancel = new Button { Content = "취소", Width = 80, Height = 28, IsCancel = true };
        btnPanel.Children.Add(btnSave);
        btnPanel.Children.Add(btnCancel);
        Grid.SetRow(btnPanel, 4);
        root.Children.Add(btnPanel);

        Content = root;

        Loaded += OnLoaded;

        // 좌표 텍스트 변경 → 마커 갱신
        _txtLatFrom.TextChanged += (_, _) => RefreshOverlay();
        _txtLatTo.TextChanged += (_, _) => RefreshOverlay();
        _txtLonFrom.TextChanged += (_, _) => RefreshOverlay();
        _txtLonTo.TextChanged += (_, _) => RefreshOverlay();
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        LoadWorldMap();
        RefreshOverlay();
        CenterOnExistingCoord();
    }

    private void LoadWorldMap()
    {
        var savePath = AppSettings.LastSaveFilePath;
        if (string.IsNullOrEmpty(savePath)) return;
        var dir = Path.GetDirectoryName(savePath);
        if (string.IsNullOrEmpty(dir)) return;
        var worldPath = CdsAssetPath.Resolve(dir, "WORLD.CDS");
        var data = WorldMapRenderer.LoadWorldData(worldPath);
        if (data == null) return;
        var palette = MapPalette.LoadOrDefault(AppSettings.MapPaletteFilePath);
        // 세계지도 창과 같은 그림이 되도록 OCEAN.CDS가 있으면 게임 타일을 쓴다.
        var ocean = OceanTiles.LoadFromDirectory(dir);
        _mapImage.Source = WorldMapRenderer.RenderSingleTile(data, palette, showCoast: true, showWind: false, ocean);
    }

    private void CenterOnExistingCoord()
    {
        var lat = ParseInt(_txtLatFrom.Text);
        var lon = ParseInt(_txtLonFrom.Text);
        if (lat == null || lon == null) return;
        var (px, py) = WorldMapRenderer.LatLonToPixel(lat.Value, lon.Value);
        _scrollViewer.ScrollToHorizontalOffset(Math.Max(0, px - _scrollViewer.ViewportWidth / 2));
        _scrollViewer.ScrollToVerticalOffset(Math.Max(0, py - _scrollViewer.ViewportHeight / 2));
    }

    private void OnMouseDownStartDrag(MouseButtonEventArgs e)
    {
        _mouseDownPos = e.GetPosition(_scrollViewer);
        _mouseDownHOffset = _scrollViewer.HorizontalOffset;
        _mouseDownVOffset = _scrollViewer.VerticalOffset;
        _isDragging = false;
        _overlayCanvas.CaptureMouse();
        e.Handled = true;
    }

    private void OnOverlayMouseMove(object sender, MouseEventArgs e)
    {
        // 좌표 프리뷰 업데이트
        var posOverlay = e.GetPosition(_overlayCanvas);
        var (lat, lon) = WorldMapRenderer.PixelToLatLon(posOverlay.X, posOverlay.Y);
        var latDir = lat >= 0 ? "N" : "S";
        var lonDir = lon >= 0 ? "E" : "W";
        _txtCoordPreview.Text = $"커서: {Math.Abs(lat):F0}°{latDir}, {Math.Abs(lon):F0}°{lonDir}";

        // 마우스 캡처 중이면 드래그 팬 처리
        if (!_overlayCanvas.IsMouseCaptured) return;
        var posScroll = e.GetPosition(_scrollViewer);
        var dx = posScroll.X - _mouseDownPos.X;
        var dy = posScroll.Y - _mouseDownPos.Y;
        if (_isDragging || Math.Abs(dx) > DragThreshold || Math.Abs(dy) > DragThreshold)
        {
            _isDragging = true;
            _scrollViewer.ScrollToHorizontalOffset(_mouseDownHOffset - dx);
            _scrollViewer.ScrollToVerticalOffset(_mouseDownVOffset - dy);
        }
    }

    private void OnMouseUpFinishDrag(MouseButtonEventArgs e, bool isFrom)
    {
        if (!_overlayCanvas.IsMouseCaptured) return;
        _overlayCanvas.ReleaseMouseCapture();

        // 드래그 임계치 미만이면 클릭으로 간주 → From/To 좌표 설정
        if (!_isDragging)
        {
            var pos = e.GetPosition(_overlayCanvas);
            var (lat, lon) = WorldMapRenderer.PixelToLatLon(pos.X, pos.Y);
            var latI = (int)Math.Round(lat);
            var lonI = (int)Math.Round(lon);

            if (isFrom)
            {
                _txtLatFrom.Text = latI.ToString();
                _txtLonFrom.Text = lonI.ToString();
                if (string.IsNullOrWhiteSpace(_txtLatTo.Text)) _txtLatTo.Text = latI.ToString();
                if (string.IsNullOrWhiteSpace(_txtLonTo.Text)) _txtLonTo.Text = lonI.ToString();
            }
            else
            {
                _txtLatTo.Text = latI.ToString();
                _txtLonTo.Text = lonI.ToString();
            }
        }

        _isDragging = false;
        e.Handled = true;
    }

    private void OnPreviewMouseWheel(object? sender, MouseWheelEventArgs e)
    {
        // 커서 위치를 맵 기준으로 먼저 캡처 (줌 전)
        var viewportPos = e.GetPosition(_scrollViewer);
        double mapX = _scrollViewer.HorizontalOffset + viewportPos.X;
        double mapY = _scrollViewer.VerticalOffset + viewportPos.Y;

        var oldScale = _scaleTransform.ScaleX;
        var factor = e.Delta > 0 ? ScaleFactor : 1.0 / ScaleFactor;
        var newScale = Math.Clamp(oldScale * factor, MinScale, MaxScale);
        if (Math.Abs(newScale - oldScale) < 1e-6) { e.Handled = true; return; }

        _scaleTransform.ScaleX = newScale;
        _scaleTransform.ScaleY = newScale;
        _markerCounterScale.ScaleX = 1.0 / newScale;
        _markerCounterScale.ScaleY = 1.0 / newScale;

        // 커서 아래 지점을 그대로 유지하도록 스크롤 오프셋 보정
        _scrollViewer.UpdateLayout();
        double ratio = newScale / oldScale;
        _scrollViewer.ScrollToHorizontalOffset(mapX * ratio - viewportPos.X);
        _scrollViewer.ScrollToVerticalOffset(mapY * ratio - viewportPos.Y);

        e.Handled = true;
    }

    private void RefreshOverlay()
    {
        // 기존 마커 제거
        if (_fromMarker != null) { _overlayCanvas.Children.Remove(_fromMarker); _fromMarker = null; }
        if (_toMarker != null) { _overlayCanvas.Children.Remove(_toMarker); _toMarker = null; }
        if (_rangeRect != null) { _overlayCanvas.Children.Remove(_rangeRect); _rangeRect = null; }

        var latFrom = ParseInt(_txtLatFrom.Text);
        var lonFrom = ParseInt(_txtLonFrom.Text);
        var latTo = ParseInt(_txtLatTo.Text);
        var lonTo = ParseInt(_txtLonTo.Text);

        // 범위 사각형 (From/To가 모두 있고 서로 다를 때)
        if (latFrom.HasValue && lonFrom.HasValue && latTo.HasValue && lonTo.HasValue &&
            (latFrom != latTo || lonFrom != lonTo))
        {
            var latMin = Math.Min(latFrom.Value, latTo.Value);
            var latMax = Math.Max(latFrom.Value, latTo.Value);
            var lonMin = Math.Min(lonFrom.Value, lonTo.Value);
            var lonMax = Math.Max(lonFrom.Value, lonTo.Value);
            var (x1, y1) = WorldMapRenderer.LatLonToPixel(latMax, lonMin);
            var (x2, y2) = WorldMapRenderer.LatLonToPixel(latMin, lonMax);
            _rangeRect = new Rectangle
            {
                Width = Math.Max(x2 - x1, 2),
                Height = Math.Max(y2 - y1, 2),
                Fill = new SolidColorBrush(Color.FromArgb(60, 220, 50, 50)),
                Stroke = new SolidColorBrush(Color.FromArgb(220, 200, 30, 30)),
                StrokeThickness = 2,
                IsHitTestVisible = false
            };
            Canvas.SetLeft(_rangeRect, x1);
            Canvas.SetTop(_rangeRect, y1);
            _overlayCanvas.Children.Add(_rangeRect);
        }

        // From 마커 (파란색)
        if (latFrom.HasValue && lonFrom.HasValue)
        {
            var (px, py) = WorldMapRenderer.LatLonToPixel(latFrom.Value, lonFrom.Value);
            _fromMarker = CreateMarker(Color.FromRgb(30, 120, 255), "From");
            Canvas.SetLeft(_fromMarker, px - MarkerSize / 2);
            Canvas.SetTop(_fromMarker, py - MarkerSize / 2);
            _overlayCanvas.Children.Add(_fromMarker);
        }

        // To 마커 (빨간색) - From과 다를 때만 표시
        if (latTo.HasValue && lonTo.HasValue && (latTo != latFrom || lonTo != lonFrom))
        {
            var (px, py) = WorldMapRenderer.LatLonToPixel(latTo.Value, lonTo.Value);
            _toMarker = CreateMarker(Color.FromRgb(230, 60, 60), "To");
            Canvas.SetLeft(_toMarker, px - MarkerSize / 2);
            Canvas.SetTop(_toMarker, py - MarkerSize / 2);
            _overlayCanvas.Children.Add(_toMarker);
        }
    }

    private Ellipse CreateMarker(Color fill, string tooltip)
    {
        return new Ellipse
        {
            Width = MarkerSize,
            Height = MarkerSize,
            Fill = new SolidColorBrush(fill),
            Stroke = Brushes.White,
            StrokeThickness = 2,
            IsHitTestVisible = false,
            ToolTip = tooltip,
            RenderTransformOrigin = new Point(0.5, 0.5),
            RenderTransform = _markerCounterScale,
        };
    }

    private void ClearAll()
    {
        _txtLatFrom.Text = "";
        _txtLatTo.Text = "";
        _txtLonFrom.Text = "";
        _txtLonTo.Text = "";
    }

    private static TextBox CreateCoordBox(int? value)
    {
        return new TextBox
        {
            Text = value?.ToString() ?? "",
            Width = 70,
            Height = 24,
            VerticalContentAlignment = VerticalAlignment.Center,
            TextAlignment = TextAlignment.Center
        };
    }

    private static int? ParseInt(string? text)
    {
        return int.TryParse(text?.Trim(), out var v) ? v : null;
    }
}
