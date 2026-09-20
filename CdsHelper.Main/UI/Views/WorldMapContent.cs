using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Ellipse = System.Windows.Shapes.Ellipse;
using Line = System.Windows.Shapes.Line;
using Polyline = System.Windows.Shapes.Polyline;
using Rectangle = System.Windows.Shapes.Rectangle;
using CdsHelper.Api.Entities;
using CdsHelper.Main.Local.ViewModels;
using CdsHelper.Support.Local.Events;
using CdsHelper.Support.Local.Helpers;
using CdsHelper.Support.Local.Models;
using CdsHelper.Support.Local.Settings;
using CdsHelper.Support.UI.Views;
using Microsoft.Win32;
using Prism.Events;
using Prism.Ioc;

namespace CdsHelper.Main.UI.Views;

public class WorldMapContent : ContentControl
{
    private WorldMapSurface? _mapImage;
    private ScrollViewer? _scrollViewer;
    private ScaleTransform? _scaleTransform;
    private TextBlock? _txtStatus;
    private TextBlock? _txtCellInfo;
    private Button? _btnOpen;
    private CheckBox? _chkShowCoast;
    private CheckBox? _chkShowWind;
    private CheckBox? _chkShowDiscoveries;

    /// <summary>발견물을 그리는지. 도구 줄에서 체크상자를 뺐으므로(fb-ui-22) 상자가 없으면 늘 그린다.</summary>
    private bool DiscoveriesOn => _chkShowDiscoveries?.IsChecked ?? true;
    private Button? _btnLeaveCity;
    private Button? _btnTrackCoordinate;
    private TextBlock? _txtCurrentCoordinate;
    private TextBlock? _txtNavStatus;
    private Button? _btnStopNav;
    private TextBlock? _txtZoomLabel;
    private Canvas? _overlayCanvas;
    private DiscoveryVisualHost? _discoveryVisualHost;
    private CanvasVisualHost? _speedLabelHost;
    private DispatcherTimer? _arrivalClearTimer;
    private MapPalette _palette = MapPalette.CreateDefault();

    // 좌표 추적 / 화면 감지
    private readonly CoordinateOcrService _coordinateOcr = new();
    private readonly GameScreenDetector _screenDetector;
    private readonly GameMemoryReader _gameMemoryReader = new();
    private List<City>? _cityCache;

    public WorldMapContent()
    {
        _screenDetector = new GameScreenDetector(_coordinateOcr);
    }
    private DispatcherTimer? _trackingTimer;
    private bool _isTracking;
    private bool _isProcessing;
    private bool _centerOnFirstPosition = true;
    private readonly Ellipse?[] _positionMarkers = new Ellipse?[3];
    private readonly Ellipse?[] _positionPulses = new Ellipse?[3];
    private readonly Polyline?[] _trailLines = new Polyline?[3];
    private readonly PointCollection[] _trailPointsByTile = { new(), new(), new() };
    // 속도별 색상 세그먼트 라인 (좌/중/우 타일 각각 Line 리스트)
    private readonly List<Line>[] _trailSegmentLines = { new(), new(), new() };
    // 세션 단위 자동 저장 파일 경로 (첫 점 추가 시 lazy 생성, 앱 종료까지 같은 파일에 append)
    private string? _sessionTrailPath;
    // 발견물 클릭 시 도서/필요스킬 정보를 표시하는 팝업
    private Popup? _discoveryInfoPopup;
    private readonly PointCollection _trailPoints = new(); // 논리적 위치 (중앙 타일 기준)
    private readonly List<DateTime> _trailTimestamps = new();

    // 발견물 표시
    private readonly List<UIElement> _discoveryMarkers = new();
    private readonly List<UIElement> _cityLabels = new();
    private readonly List<UIElement> _foundMarkers = new();
    private bool _discoveriesLoaded;
    private string? _loadedSavePath;
    private CheckBox? _chkShowCityLabels;
    private CheckBox? _chkHideFound;
    private CheckBox? _chkShowSpeed;
    private ComboBox? _cmbAutoScrollThreshold;
    private ComboBox? _cmbTrackingInterval;
    private HashSet<int>? _foundDiscoveryIds;
    private SubscriptionToken? _saveDataLoadedToken;

    private double _currentScale = 2.0;

    // 배율은 WorldMapKR 처럼 정해진 단계로 오르내린다. 칸 하나가 화면에서 차지하는 픽셀은
    // 배율 x RenderScale 이므로, x8 에서 칸당 16픽셀 — OCEAN.CDS 타일 원본 해상도에 닿는다.
    // 그보다 키우면 타일 점이 그대로 커진다(게임 확대와 같은 모양).
    private static readonly double[] ZoomSteps = [0.25, 0.5, 1.0, 1.5, 2.0, 3.0, 4.0, 6.0, 8.0, 12.0, 16.0];
    private const double MinScale = 0.25;
    private const double MaxScale = 16.0;
    /// <summary>타일 원본 해상도에 닿는 배율. 이 위로는 더 나올 그림이 없다.</summary>
    private const double NativeScale = 8.0;

    private byte[]? _mapData;
    private OceanTiles? _ocean;           // OCEAN.CDS 타일 그림. 없으면 _palette로 색을 계산한다.
    private const int RawStride = WorldMapRenderer.RawStride;   // bytes per row in file
    private const int CellW = WorldMapRenderer.CellW;           // cells per row (2 bytes/cell)
    private const int CellH = WorldMapRenderer.CellH;           // unfolded rows (2500 raw / 2)
    private const int UnfoldedW = WorldMapRenderer.UnfoldedW;   // unfolded width (left half + right half)
    private const int TileCount = WorldMapSurface.TileCopies;   // 3 copies for infinite horizontal scroll
    private const int RenderScale = WorldMapSurface.LogicalScale; // 논리 좌표에서 칸당 픽셀
    private const int RenderTileW = WorldMapSurface.TileLogicalW; // 5000
    private const int RenderW = WorldMapSurface.LogicalW;         // 15000
    private const int RenderH = WorldMapSurface.LogicalH;         // 2500

    // 마커를 모든 타일에 복제하기 위한 X 오프셋 (왼쪽/중앙/오른쪽)
    private static readonly double[] TileOffsets = { -(double)RenderTileW, 0.0, (double)RenderTileW };

    private bool _isDragging;
    private Point _lastMousePos;

    // 자동이동
    private CancellationTokenSource? _navCts;
    private bool _isNavigating;
    private readonly Ellipse?[] _destMarkers = new Ellipse?[3];

    static WorldMapContent()
    {
        DefaultStyleKeyProperty.OverrideMetadata(
            typeof(WorldMapContent),
            new FrameworkPropertyMetadata(typeof(WorldMapContent)));
    }

    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate();

        _mapImage = GetTemplateChild("PART_MapImage") as WorldMapSurface;
        _scrollViewer = GetTemplateChild("PART_ScrollViewer") as ScrollViewer;
        _txtStatus = GetTemplateChild("PART_Status") as TextBlock;
        _txtCellInfo = GetTemplateChild("PART_CellInfo") as TextBlock;
        _btnOpen = GetTemplateChild("PART_BtnOpen") as Button;
        _chkShowCoast = GetTemplateChild("PART_ShowCoast") as CheckBox;
        _chkShowWind = GetTemplateChild("PART_ShowWind") as CheckBox;
        _chkShowDiscoveries = GetTemplateChild("PART_ShowDiscoveries") as CheckBox;
        _chkShowCityLabels = GetTemplateChild("PART_ShowCityLabels") as CheckBox;
        _chkHideFound = GetTemplateChild("PART_HideFound") as CheckBox;
        _chkShowSpeed = GetTemplateChild("PART_ShowSpeed") as CheckBox;
        _cmbAutoScrollThreshold = GetTemplateChild("PART_AutoScrollThreshold") as ComboBox;
        _cmbTrackingInterval = GetTemplateChild("PART_TrackingInterval") as ComboBox;

        // 저장된 옵션 불러오기 (Click 핸들러 연결 전에 수행하여 Rerender 방지)
        var opts = AppSettings.WorldMap;
        if (_chkShowCoast != null) _chkShowCoast.IsChecked = opts.ShowCoast;
        if (_chkShowWind != null) _chkShowWind.IsChecked = opts.ShowWind;
        if (_chkShowDiscoveries != null) _chkShowDiscoveries.IsChecked = opts.ShowDiscoveries;
        if (_chkShowCityLabels != null) _chkShowCityLabels.IsChecked = opts.ShowCityLabels;
        if (_chkHideFound != null) _chkHideFound.IsChecked = opts.HideFound;
        if (_chkShowSpeed != null) _chkShowSpeed.IsChecked = opts.ShowSpeed;
        SelectAutoScrollThresholdItem(opts.AutoScrollThreshold);
        SelectTrackingIntervalItem(opts.TrackingIntervalSeconds);
        _currentScale = SnapZoom(opts.Zoom);

        _scaleTransform = new ScaleTransform(_currentScale, _currentScale);
        var mapGrid = GetTemplateChild("PART_MapGrid") as Grid;
        if (mapGrid != null)
            mapGrid.LayoutTransform = _scaleTransform;
        if (_mapImage != null)
            _mapImage.MouseMove += MapImage_MouseMove;

        // 팔레트 로드: 사용자가 %APPDATA%\CdsHelper\map_palette.json을 수정했으면 반영
        _palette = MapPalette.LoadOrDefault(AppSettings.MapPaletteFilePath);

        if (_btnOpen != null) _btnOpen.Click += (_, _) => OpenFile();
        var btnReloadPalette = GetTemplateChild("PART_BtnReloadPalette") as Button;
        if (btnReloadPalette != null) btnReloadPalette.Click += (_, _) => ReloadPalette();

        if (_chkShowCoast != null) _chkShowCoast.Click += (_, _) => { Rerender(); SaveWorldMapOptions(); };
        if (_chkShowWind != null) _chkShowWind.Click += (_, _) => { Rerender(); SaveWorldMapOptions(); };
        if (_chkShowDiscoveries != null) _chkShowDiscoveries.Click += (_, _) => { ToggleDiscoveries(); SaveWorldMapOptions(); };
        if (_chkShowCityLabels != null) _chkShowCityLabels.Click += (_, _) => { ToggleCityLabels(); SaveWorldMapOptions(); };
        if (_chkHideFound != null) _chkHideFound.Click += (_, _) => { ToggleHideFound(); SaveWorldMapOptions(); };
        if (_chkShowSpeed != null) _chkShowSpeed.Click += (_, _) => { ToggleSpeedLabels(); SaveWorldMapOptions(); };
        if (_cmbAutoScrollThreshold != null) _cmbAutoScrollThreshold.SelectionChanged += (_, _) => SaveWorldMapOptions();
        if (_cmbTrackingInterval != null) _cmbTrackingInterval.SelectionChanged += (_, _) => { SaveWorldMapOptions(); ApplyTrackingInterval(); };

        _btnLeaveCity = GetTemplateChild("PART_BtnLeaveCity") as Button;
        if (_btnLeaveCity != null)
            _btnLeaveCity.Click += (_, _) => LeaveCityForExploration();

        _btnTrackCoordinate = GetTemplateChild("PART_BtnTrackCoordinate") as Button;
        _txtCurrentCoordinate = GetTemplateChild("PART_TxtCurrentCoordinate") as TextBlock;
        _txtNavStatus = GetTemplateChild("PART_TxtNavStatus") as TextBlock;
        _btnStopNav = GetTemplateChild("PART_BtnStopNav") as Button;
        if (_btnStopNav != null) _btnStopNav.Click += (_, _) => StopNavigation();
        _overlayCanvas = GetTemplateChild("PART_OverlayCanvas") as Canvas;
        _txtZoomLabel = GetTemplateChild("PART_ZoomLabel") as TextBlock;
        if (_txtZoomLabel != null) _txtZoomLabel.Text = $"x{_currentScale:F1}";
        if (_overlayCanvas != null)
        {
            _overlayCanvas.MouseRightButtonDown += OnMapRightClick;
            _overlayCanvas.MouseLeftButtonDown += OnOverlayLeftClick;

            // 발견물 점/영역 DrawingVisual 호스트 — 라벨/도시 마커보다 뒤에 렌더되도록 첫 자식으로 삽입
            _discoveryVisualHost = new DiscoveryVisualHost
            {
                Width = RenderW,
                Height = RenderH,
                FoundVisible = _chkHideFound?.IsChecked != true
            };
            Canvas.SetLeft(_discoveryVisualHost, 0);
            Canvas.SetTop(_discoveryVisualHost, 0);
            _overlayCanvas.Children.Insert(0, _discoveryVisualHost);

            // 속도 라벨 호스트 — 항상 최상단에 렌더되도록 ZIndex 높게 설정
            _speedLabelHost = new CanvasVisualHost
            {
                Width = RenderW,
                Height = RenderH,
                Visibility = _chkShowSpeed?.IsChecked == true ? Visibility.Visible : Visibility.Collapsed
            };
            Canvas.SetLeft(_speedLabelHost, 0);
            Canvas.SetTop(_speedLabelHost, 0);
            Panel.SetZIndex(_speedLabelHost, 1000);
            _overlayCanvas.Children.Add(_speedLabelHost);
        }
        if (_btnTrackCoordinate != null)
            _btnTrackCoordinate.Click += (_, _) => ToggleTracking();

        var btnClearTrail = GetTemplateChild("PART_BtnClearTrail") as Button;
        var btnSaveTrail = GetTemplateChild("PART_BtnSaveTrail") as Button;
        var btnLoadTrail = GetTemplateChild("PART_BtnLoadTrail") as Button;
        if (btnClearTrail != null) btnClearTrail.Click += (_, _) => ClearTrail();
        if (btnSaveTrail != null) btnSaveTrail.Click += (_, _) => SaveTrail();
        if (btnLoadTrail != null) btnLoadTrail.Click += (_, _) => LoadTrail();

        var btnCenterPosition = GetTemplateChild("PART_BtnCenterPosition") as Button;
        if (btnCenterPosition != null) btnCenterPosition.Click += (_, _) => GoToMyLocation();

        if (_scrollViewer != null)
        {
            _scrollViewer.PreviewMouseWheel += (s, e) =>
            {
                ZoomAtCursor(e.Delta > 0 ? 1 : -1, e);
                e.Handled = true;
            };
            _scrollViewer.PreviewMouseLeftButtonDown += ScrollViewer_MouseDown;
            _scrollViewer.PreviewMouseLeftButtonUp += ScrollViewer_MouseUp;
            _scrollViewer.PreviewMouseMove += ScrollViewer_MouseMove;
            _scrollViewer.ScrollChanged += ScrollViewer_WrapHorizontal;
        }

        // 세이브 파일 새로고침/로드 감지 → 발견물 마커 무효화 후 표시 중이면 즉시 재빌드
        if (_saveDataLoadedToken == null)
        {
            try
            {
                var eventAggregator = ContainerLocator.Container.Resolve<IEventAggregator>();
                _saveDataLoadedToken = eventAggregator.GetEvent<SaveDataLoadedEvent>()
                    .Subscribe(OnSaveDataLoaded, ThreadOption.UIThread);
            }
            catch (System.Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[WorldMap] SaveDataLoadedEvent subscribe failed: {ex.Message}");
            }
        }

        // 세이브 파일 경로 기준으로 WORLD.CDS 자동 로드
        var savePath = AppSettings.LastSaveFilePath;
        if (!string.IsNullOrEmpty(savePath))
        {
            var dir = Path.GetDirectoryName(savePath);
            if (dir != null)
            {
                LoadAndRender(CdsAssetPath.Resolve(dir, "WORLD.CDS"));
            }
        }

        // 좌표 추적 자동 시작
        var autoHWnd = GameWindowHelper.FindGameWindow();
        if (autoHWnd != IntPtr.Zero)
            StartTracking();
    }

    private void ScrollViewer_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (_scrollViewer == null) return;
        // 클릭 가능한 요소(Border/Ellipse with Tag)에서 시작된 클릭은 드래그 무시
        if (e.OriginalSource is FrameworkElement fe &&
            (fe is Border { Tag: not null } || fe is Ellipse { Tag: not null }
             || FindParent<Border>(fe) is { Tag: not null }))
            return;
        _isDragging = true;
        _lastMousePos = e.GetPosition(_scrollViewer);
        _scrollViewer.Cursor = Cursors.Hand;
        e.Handled = true;
    }

    private static T? FindParent<T>(FrameworkElement? element) where T : FrameworkElement
    {
        while (element != null)
        {
            element = element.Parent as FrameworkElement ?? VisualTreeHelper.GetParent(element) as FrameworkElement;
            if (element is T match) return match;
        }
        return null;
    }

    private void ScrollViewer_MouseUp(object sender, MouseButtonEventArgs e)
    {
        _isDragging = false;
        if (_scrollViewer != null) _scrollViewer.Cursor = Cursors.Arrow;
    }

    private void ScrollViewer_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_isDragging || _scrollViewer == null) return;
        var pos = e.GetPosition(_scrollViewer);
        var dx = pos.X - _lastMousePos.X;
        var dy = pos.Y - _lastMousePos.Y;
        _scrollViewer.ScrollToHorizontalOffset(_scrollViewer.HorizontalOffset - dx);
        _scrollViewer.ScrollToVerticalOffset(_scrollViewer.VerticalOffset - dy);
        _lastMousePos = pos;
    }

    private bool _isWrapping;

    private void ScrollViewer_WrapHorizontal(object sender, ScrollChangedEventArgs e)
    {
        // 스크롤·창 크기가 바뀌었으니 보이는 영역을 다시 그린다.
        UpdateSurfaceView();

        if (_isWrapping || _scrollViewer == null || _mapData == null) return;

        double tileW = RenderTileW * _currentScale;
        double offset = _scrollViewer.HorizontalOffset;

        // Keep scroll within the middle tile (tile index 1)
        if (offset < tileW * 0.5)
        {
            _isWrapping = true;
            _scrollViewer.ScrollToHorizontalOffset(offset + tileW);
            _isWrapping = false;
        }
        else if (offset > tileW * 1.5)
        {
            _isWrapping = true;
            _scrollViewer.ScrollToHorizontalOffset(offset - tileW);
            _isWrapping = false;
        }
    }

    private void CenterScrollToMiddleTile()
    {
        if (_scrollViewer == null) return;
        _scrollViewer.UpdateLayout();
        // 초기 뷰는 리스본(38°N, 9°W)이 중앙에 오도록 스크롤
        const double lisbonLat = 38;
        const double lisbonLon = -9;
        var (px, py) = LatLonToPixel(lisbonLat, lisbonLon);
        ScrollToImagePosition(px, py);
    }

    private void MapImage_MouseMove(object sender, MouseEventArgs e)
    {
        if (_mapData == null || _mapImage?.HasMap != true || _txtCellInfo == null) return;

        var pos = e.GetPosition(_mapImage);
        int rx = (int)pos.X;
        int ry = (int)pos.Y;
        if (rx < 0 || rx >= RenderW || ry < 0 || ry >= RenderH) return;

        // Map render pixel to unfolded coordinate (wrap within one tile)
        int tileX = rx % RenderTileW;
        bool isRightHalf = tileX >= CellW * RenderScale;
        int cx = isRightHalf ? tileX - CellW * RenderScale : tileX;
        int cellCx = Math.Min(cx / RenderScale, CellW - 1);
        int cellRy = Math.Min(ry / RenderScale, CellH - 1);

        int rawRow = isRightHalf ? cellRy * 2 + 1 : cellRy * 2;
        int off = rawRow * RawStride + cellCx * 2;
        byte terrain = (byte)(_mapData[off] & 0x7F);
        byte attr = _mapData[off + 1];

        int unfoldedX = isRightHalf ? cellCx + CellW : cellCx;
        double lon = unfoldedX * 360.0 / UnfoldedW - 180;
        double lat = 90.0 - cellRy * 180.0 / CellH;

        string terrainName = terrain switch
        {
            0 => "바다",
            1 => "육지",
            _ => $"해안({terrain})"
        };

        string attrName = terrain switch
        {
            0 => $"해류={attr}",
            1 => attr switch
            {
                68 => "온대림",
                67 => "밀림",
                66 => "초원",
                64 => "사막",
                _ when attr >= 86 && attr <= 116 => $"기후대({attr})",
                _ => $"지형={attr}"
            },
            _ => $"해안비율={WorldMapRenderer.GetCoastLandRatio(terrain):P0} attr={attr}"
        };

        string latDir = lat >= 0 ? "N" : "S";
        string lonDir = lon >= 0 ? "E" : "W";
        _txtCellInfo.Text = $"셀({unfoldedX},{cellRy}) | {Math.Abs(lat):F1}°{latDir}, {Math.Abs(lon):F1}°{lonDir} | {terrainName} | {attrName} | 0x{_mapData[off]:X2} 0x{attr:X2}";
    }

    private void OpenFile()
    {
        var dlg = new OpenFileDialog
        {
            Filter = "CDS 파일 (*.CDS)|*.CDS|모든 파일 (*.*)|*.*",
            Title = "WORLD.CDS 파일 선택"
        };
        if (dlg.ShowDialog() == true)
            LoadAndRender(dlg.FileName);
    }

    private void LoadAndRender(string path)
    {
        try
        {
            _mapData = File.ReadAllBytes(path);
            if (_mapData.Length != RawStride * CellH * 2)
            {
                if (_txtStatus != null)
                    _txtStatus.Text = $"파일 크기 불일치: {_mapData.Length} (예상: {RawStride * CellH * 2})";
                _mapData = null;
                return;
            }

            // 같은 폴더의 OCEAN.CDS로 게임 타일 그림을 쓴다. 없으면 _palette로 그린다.
            var gameDir = Path.GetDirectoryName(path);
            _ocean = gameDir != null ? OceanTiles.LoadFromDirectory(gameDir) : null;
            string source = _ocean != null ? "게임 그림" : $"팔레트 ({OceanTiles.LastError})";

            if (_txtStatus != null)
                _txtStatus.Text = $"로드 완료: {Path.GetFileName(path)} (펼침: {UnfoldedW}x{CellH}, {RenderW}x{RenderH}px, {source})";
            Rerender();

            // 발견물 체크 되어 있으면 표시
            if (DiscoveriesOn)
                ShowDiscoveries();
        }
        catch (Exception ex)
        {
            if (_txtStatus != null)
                _txtStatus.Text = $"로드 실패: {ex.Message}";
        }
    }

    private void ReloadPalette()
    {
        try
        {
            var dlg = new EditMapPaletteDialog(_palette) { Owner = Window.GetWindow(this) };
            if (dlg.ShowDialog() != true) return;
            _palette = dlg.ResultPalette;
            _palette.Save(AppSettings.MapPaletteFilePath);
            Rerender();
            if (_txtStatus != null)
                _txtStatus.Text = _ocean != null
                    // OCEAN.CDS가 있으면 그 그림으로 그리므로 팔레트는 화면에 반영되지 않는다.
                    ? $"팔레트 저장: {AppSettings.MapPaletteFilePath} (지금은 OCEAN.CDS 게임 그림으로 그리는 중이라 화면에는 적용되지 않습니다)"
                    : $"팔레트 저장: {AppSettings.MapPaletteFilePath}";
        }
        catch (Exception ex)
        {
            if (_txtStatus != null)
                _txtStatus.Text = $"팔레트 편집 실패: {ex.Message}";
        }
    }

    private void Rerender()
    {
        if (_mapData == null || _mapImage == null) return;

        // 해안선 상세/해류 토글 UI는 제거됨. 해안선 상세는 항상 켜진 상태로 고정.
        const bool showCoast = true;
        const bool showWind = false;

        // 통짜 비트맵을 굽지 않는다 — 보이는 영역만 WorldMapSurface가 그때그때 그린다.
        _mapImage.SetSource(_mapData, _ocean, _palette, showCoast, showWind);

        if (_overlayCanvas != null)
        {
            _overlayCanvas.Width = RenderW;
            _overlayCanvas.Height = RenderH;
        }

        CenterScrollToMiddleTile();
        UpdateSurfaceView();
    }

    // 칸 색 계산과 해안 비율표는 WorldMapRenderer 에 하나로 모았다.
    // 바탕 그림은 WorldMapSurface 가, 셀 정보 표시는 아래 GetCoastLandRatio 호출이 쓴다.

    /// <summary>주어진 배율에 가장 가까운 단계를 고른다.</summary>
    private static double SnapZoom(double scale)
    {
        double best = ZoomSteps[0];
        foreach (var z in ZoomSteps)
            if (Math.Abs(z - scale) < Math.Abs(best - scale)) best = z;
        return best;
    }

    /// <summary>한 단계 위/아래 배율. 끝이면 그대로.</summary>
    private static double StepZoom(double scale, int dir)
    {
        int i = Array.IndexOf(ZoomSteps, SnapZoom(scale));
        return ZoomSteps[Math.Clamp(i + dir, 0, ZoomSteps.Length - 1)];
    }

    private void ZoomAtCursor(int dir, MouseWheelEventArgs e)
    {
        if (_scrollViewer == null || _scaleTransform == null) return;

        var mousePos = e.GetPosition(_scrollViewer);
        double oldScale = _currentScale;
        _currentScale = StepZoom(_currentScale, dir);
        if (Math.Abs(_currentScale - oldScale) < 0.001) return;

        double ratio = _currentScale / oldScale;

        double pointX = _scrollViewer.HorizontalOffset + mousePos.X;
        double pointY = _scrollViewer.VerticalOffset + mousePos.Y;

        ApplyScale();
        _scrollViewer.UpdateLayout();

        _isWrapping = true;
        _scrollViewer.ScrollToHorizontalOffset(pointX * ratio - mousePos.X);
        _scrollViewer.ScrollToVerticalOffset(pointY * ratio - mousePos.Y);
        _isWrapping = false;
        UpdateSurfaceView();
        SaveWorldMapOptions();
    }

    private void Zoom(int dir)
    {
        _currentScale = StepZoom(_currentScale, dir);
        ApplyScale();
        _scrollViewer?.UpdateLayout();
        UpdateSurfaceView();
        SaveWorldMapOptions();
    }

    private void ApplyScale()
    {
        if (_scaleTransform == null) return;
        _scaleTransform.ScaleX = _currentScale;
        _scaleTransform.ScaleY = _currentScale;
        if (_txtZoomLabel != null)
        {
            // 타일 원본 해상도를 넘어서면 더 나올 그림이 없다는 것을 알린다.
            _txtZoomLabel.Text = _currentScale > NativeScale
                ? $"x{_currentScale:F1} (원본 초과)"
                : $"x{_currentScale:F1}";
        }
    }

    /// <summary>
    /// 지금 창에 보이는 논리 좌표 영역을 바탕 그림에 알려 준다. 스크롤/확대/창 크기가
    /// 바뀔 때마다 불러야 한다 — 이걸로 그 영역만 다시 그린다.
    /// </summary>
    private void UpdateSurfaceView()
    {
        if (_scrollViewer == null || _mapImage == null) return;
        double s = _currentScale;
        if (s <= 0) return;

        var view = new Rect(_scrollViewer.HorizontalOffset / s,
                            _scrollViewer.VerticalOffset / s,
                            _scrollViewer.ViewportWidth / s,
                            _scrollViewer.ViewportHeight / s);
        // 화면 실픽셀 기준으로 그려야 확대해도 타일 점이 살아난다.
        double dpi = VisualTreeHelper.GetDpi(this).DpiScaleX;
        _mapImage.SetView(view, s * dpi);
    }

    private void SaveWorldMapOptions()
    {
        var opts = AppSettings.WorldMap;
        opts.ShowCoast = _chkShowCoast?.IsChecked == true;
        opts.ShowWind = _chkShowWind?.IsChecked == true;
        opts.ShowDiscoveries = DiscoveriesOn;
        opts.ShowCityLabels = _chkShowCityLabels?.IsChecked == true;
        opts.HideFound = _chkHideFound?.IsChecked == true;
        opts.ShowSpeed = _chkShowSpeed?.IsChecked == true;
        opts.AutoScrollThreshold = GetAutoScrollThreshold();
        opts.TrackingIntervalSeconds = GetTrackingInterval();
        opts.Zoom = _currentScale;
        AppSettings.SaveWorldMapOptions();
    }

    #region 발견물 표시

    private void ToggleDiscoveries()
    {
        if (DiscoveriesOn)
            ShowDiscoveries();
        else
            HideDiscoveries();
    }

    private void OnSaveDataLoaded(SaveDataLoadedEventArgs args)
    {
        // 새로 로드된 세이브 데이터를 반영하도록 기존 발견물 마커를 무효화
        if (_overlayCanvas == null) return;

        if (_discoveriesLoaded)
        {
            foreach (var m in _discoveryMarkers)
                _overlayCanvas.Children.Remove(m);
            _discoveryMarkers.Clear();
            _foundMarkers.Clear();
            _cityLabels.Clear();
            _discoveryVisualHost?.Clear();
            _discoveriesLoaded = false;
            _loadedSavePath = null;
        }

        // 발견물 표시가 켜져 있으면 즉시 재빌드
        if (DiscoveriesOn)
            ShowDiscoveries();
    }

    private void ShowDiscoveries()
    {
        if (_overlayCanvas == null) return;

        // 세이브 파일이 바뀌었으면 마커를 처음부터 다시 빌드
        var saveDataServiceCheck = ContainerLocator.Container.Resolve<SaveDataService>();
        var currentSavePath = saveDataServiceCheck.CurrentFilePath;
        if (_discoveriesLoaded && _loadedSavePath != currentSavePath)
        {
            foreach (var m in _discoveryMarkers)
                _overlayCanvas.Children.Remove(m);
            _discoveryMarkers.Clear();
            _foundMarkers.Clear();
            _cityLabels.Clear();
            _discoveryVisualHost?.Clear();
            _discoveriesLoaded = false;
        }

        // 모든 마커를 일단 숨김 (가시성 재계산을 위해)
        foreach (var marker in _discoveryMarkers)
            marker.Visibility = Visibility.Collapsed;

        if (!_discoveriesLoaded)
        {
            try
            {
                // 발견 여부 로드
                try
                {
                    var saveDataService = ContainerLocator.Container.Resolve<SaveDataService>();
                    if (saveDataService.CurrentSaveGameInfo?.Discoveries != null)
                    {
                        _foundDiscoveryIds = saveDataService.CurrentSaveGameInfo.Discoveries
                            .Where(d => d.IsDiscovered)
                            .Select(d => d.Id)
                            .ToHashSet();
                    }
                }
                catch { /* 세이브 데이터 없으면 무시 */ }

                var service = ContainerLocator.Container.Resolve<DiscoveryService>();
                var discoveries = service.GetAllDiscoveries();

                // 게임 발견물 표(274개)를 기준으로 돈다 — DB 는 232개뿐이라 42개가 빠져 있고
                // 불국사·돌고래처럼 Id 가 어긋난 것도 있다. DB 는 힌트/서적 같은 덤을 얹을 때만 본다.
                for (int gi = 0; gi < GameMapCoords.DiscoveryCount; gi++)
                {
                    if (!GameMapCoords.TryDiscoveryCells(gi, out var gx1, out var gy1, out var gx2, out var gy2))
                        continue;   // 좌표로 찾는 발견물이 아니다(항로·인물·비보 등)

                    int dbId = GameMapCoords.DbIdFromIndex(gi);
                    // 이름은 게임 표를 따른다. DB 에 있으면 힌트/서적을 물려받되 이름은 덮어쓴다.
                    var d = discoveries.TryGetValue(dbId, out var row)
                        ? row
                        : new DiscoveryEntity { Id = dbId };
                    d.Name = GameMapCoords.DiscoveryNames[gi];

                    // 세이브 슬롯 번호 = 게임 발견물 번호
                    bool isFound = _foundDiscoveryIds?.Contains(gi) == true;

                    // 한두 칸짜리는 점, 그보다 넓으면 영역으로 그린다.
                    if (gx2 - gx1 <= 2 && gy2 - gy1 <= 2)
                        AddDiscoveryPoint(d, isFound);
                    else
                        AddDiscoveryArea(d, isFound);
                }

                // 도시 좌표 표시 (파란색)
                try
                {
                    var cityService = ContainerLocator.Container.Resolve<CityService>();
                    var cities = cityService.GetCitiesWithCoordinatesFromDbAsync().Result;
                    foreach (var city in cities)
                    {
                        if (city.Latitude == null || city.Longitude == null) continue;
                        AddCityPoint(city.Id, city.Name, city.Latitude.Value, city.Longitude.Value, city.HasLibrary);
                    }
                }
                catch (Exception cityEx)
                {
                    System.Diagnostics.Debug.WriteLine($"[WorldMap] City load error: {cityEx.Message}");
                }

                _discoveriesLoaded = true;
                _loadedSavePath = currentSavePath;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[WorldMap] Discovery load error: {ex.Message}");
            }
        }

        var showLabels = _chkShowCityLabels?.IsChecked == true;
        var hideFound = _chkHideFound?.IsChecked == true;
        foreach (var marker in _discoveryMarkers)
        {
            bool hidden = (!showLabels && _cityLabels.Contains(marker))
                       || (hideFound && _foundMarkers.Contains(marker));
            marker.Visibility = hidden ? Visibility.Collapsed : Visibility.Visible;
        }
        if (_discoveryVisualHost != null)
        {
            _discoveryVisualHost.Visibility = Visibility.Visible;
            _discoveryVisualHost.FoundVisible = !hideFound;
        }

        // 보이는 레이블만 모아서 겹침 해소 (다음 layout 패스 후 실제 사이즈 알 수 있으니 BeginInvoke)
        Dispatcher.BeginInvoke(new Action(ResolveDiscoveryLabelOverlap),
            System.Windows.Threading.DispatcherPriority.Loaded);
    }

    /// <summary>
    /// 발견물/도시 레이블이 겹치면 아래쪽으로 밀어내서 가독성 확보.
    /// Y 좌표 기준으로 정렬한 뒤, X 범위가 겹치는 이전 레이블들과 비교하여
    /// 충돌이 없을 때까지 newTop을 아래로 조정.
    /// </summary>
    private void ResolveDiscoveryLabelOverlap()
    {
        if (_overlayCanvas == null) return;
        _overlayCanvas.UpdateLayout();

        // discovery 레이블은 Border + Tag(int discoveryId), city 레이블도 동일 형태
        var labels = _discoveryMarkers
            .OfType<Border>()
            .Where(b => b.Visibility == Visibility.Visible &&
                        b.ActualWidth > 0 && b.ActualHeight > 0 &&
                        !double.IsNaN(Canvas.GetLeft(b)) && !double.IsNaN(Canvas.GetTop(b)))
            .Select(b => new
            {
                Label = b,
                Left = Canvas.GetLeft(b),
                OriginalTop = Canvas.GetTop(b),
                Width = b.ActualWidth,
                Height = b.ActualHeight,
            })
            .OrderBy(r => r.OriginalTop)
            .ThenBy(r => r.Left)
            .ToList();

        if (labels.Count == 0) return;

        var placed = new List<(double Left, double Top, double Width, double Height)>();
        const double Padding = 1.0;
        const int MaxIter = 20;

        foreach (var r in labels)
        {
            double newTop = r.OriginalTop;
            for (int iter = 0; iter < MaxIter; iter++)
            {
                bool moved = false;
                foreach (var p in placed)
                {
                    bool xOverlap = r.Left < p.Left + p.Width && r.Left + r.Width > p.Left;
                    if (!xOverlap) continue;
                    bool yOverlap = newTop < p.Top + p.Height && newTop + r.Height > p.Top;
                    if (yOverlap)
                    {
                        newTop = p.Top + p.Height + Padding;
                        moved = true;
                    }
                }
                if (!moved) break;
            }

            if (Math.Abs(newTop - r.OriginalTop) > 0.1)
                Canvas.SetTop(r.Label, newTop);
            placed.Add((r.Left, newTop, r.Width, r.Height));
        }
    }

    private void HideDiscoveries()
    {
        foreach (var marker in _discoveryMarkers)
            marker.Visibility = Visibility.Collapsed;
        if (_discoveryVisualHost != null)
            _discoveryVisualHost.Visibility = Visibility.Collapsed;
        // _foundMarkers/_cityLabels는 분류 정보이므로 비우면 안 됨 (다시 보일 때 필터링이 망가짐)
    }

    private void ToggleHideFound()
    {
        var hideFound = _chkHideFound?.IsChecked == true;
        foreach (var marker in _foundMarkers)
            marker.Visibility = hideFound ? Visibility.Collapsed : Visibility.Visible;
        if (_discoveryVisualHost != null)
            _discoveryVisualHost.FoundVisible = !hideFound;
    }

    // 발견물 마커 공유 브러시/펜 (Freeze → 수천 개 마커가 같은 인스턴스를 참조)
    private static readonly SolidColorBrush DotFillFound = CreateFrozenBrush(150, 100, 100, 100);
    private static readonly SolidColorBrush DotFillUnfound = CreateFrozenBrush(200, 220, 40, 40);
    private static readonly Pen DotPenFound = CreateFrozenPen(180, 80, 80, 80, 1.0);
    private static readonly Pen DotPenUnfound = CreateFrozenPen(220, 160, 20, 20, 1.0);

    private static readonly SolidColorBrush AreaFillFound = CreateFrozenBrush(20, 100, 100, 100);
    private static readonly SolidColorBrush AreaFillUnfound = CreateFrozenBrush(40, 50, 100, 220);
    private static readonly Pen AreaPenFound = CreateFrozenPen(100, 80, 80, 80, 1.0);
    private static readonly Pen AreaPenUnfound = CreateFrozenPen(160, 50, 100, 220, 1.0);

    private static SolidColorBrush CreateFrozenBrush(byte a, byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromArgb(a, r, g, b));
        brush.Freeze();
        return brush;
    }

    private static Pen CreateFrozenPen(byte a, byte r, byte g, byte b, double thickness)
    {
        var pen = new Pen(CreateFrozenBrush(a, r, g, b), thickness);
        pen.Freeze();
        return pen;
    }

    private void AddDiscoveryPoint(DiscoveryEntity d, bool isFound = false)
    {
        if (_overlayCanvas == null || _discoveryVisualHost == null) return;

        // 게임 원본 좌표표가 먼저다. 없을 때만 DB 의 도 단위 값을 쓴다.
        var (px, py) = GameMapCoords.TryDiscoveryCells(GameMapCoords.IndexFromDbId(d.Id), out var gx1, out var gy1, out var gx2, out var gy2)
            ? CellToPixel((gx1 + gx2 + 1) / 2.0, (gy1 + gy2 + 1) / 2.0)
            : LatLonToPixel(d.LatFrom!.Value, d.LonFrom!.Value);

        const double radius = 3.0; // 기존 size=6 기준 지름의 절반
        var fill = isFound ? DotFillFound : DotFillUnfound;
        var pen = isFound ? DotPenFound : DotPenUnfound;

        // 좌/중/우 타일에 동일 마커 복제
        foreach (var ox in TileOffsets)
        {
            var dotVisual = new DrawingVisual();
            using (var dc = dotVisual.RenderOpen())
            {
                dc.DrawEllipse(fill, pen, new Point(px + ox, py), radius, radius);
            }
            _discoveryVisualHost.AddVisual(dotVisual, isFound);

            var label = CreateDiscoveryLabel(d.Id, d.Name);
            Canvas.SetLeft(label, px + ox + radius + 2);
            Canvas.SetTop(label, py - 6);
            _overlayCanvas.Children.Add(label);
            _discoveryMarkers.Add(label);
            if (isFound) _foundMarkers.Add(label);
        }
    }

    private void AddDiscoveryArea(DiscoveryEntity d, bool isFound = false)
    {
        if (_overlayCanvas == null || _discoveryVisualHost == null) return;

        double x1, y1, x2, y2;
        if (GameMapCoords.TryDiscoveryCells(GameMapCoords.IndexFromDbId(d.Id), out var gx1, out var gy1, out var gx2, out var gy2))
        {
            // 게임 원본 좌표표. 칸 끝을 포함해야 하므로 오른쪽/아래를 한 칸 늘린다.
            var (cx1, cy1, cx2, cy2) = ClampSpan(Math.Min(gx1, gx2), Math.Min(gy1, gy2),
                                                 Math.Max(gx1, gx2) + 1, Math.Max(gy1, gy2) + 1);
            (x1, y1) = CellToPixel(cx1, cy1);
            (x2, y2) = CellToPixel(cx2, cy2);
        }
        else
        {
            var latFrom = d.LatFrom ?? 0;
            var latTo = d.LatTo ?? latFrom;
            var lonFrom = d.LonFrom ?? 0;
            var lonTo = d.LonTo ?? lonFrom;

            // 위도/경도 범위의 min/max 정리
            var latMin = Math.Min(latFrom, latTo);
            var latMax = Math.Max(latFrom, latTo);
            var lonMin = Math.Min(lonFrom, lonTo);
            var lonMax = Math.Max(lonFrom, lonTo);

            (x1, y1) = LatLonToPixel(latMax, lonMin); // 좌상단
            (x2, y2) = LatLonToPixel(latMin, lonMax); // 우하단
        }

        var w = Math.Max(x2 - x1, 2);
        var h = Math.Max(y2 - y1, 2);

        var fill = isFound ? AreaFillFound : AreaFillUnfound;
        var pen = isFound ? AreaPenFound : AreaPenUnfound;

        // 좌/중/우 타일에 동일 마커 복제
        foreach (var ox in TileOffsets)
        {
            var rectVisual = new DrawingVisual();
            using (var dc = rectVisual.RenderOpen())
            {
                dc.DrawRectangle(fill, pen, new Rect(x1 + ox, y1, w, h));
            }
            _discoveryVisualHost.AddVisual(rectVisual, isFound);

            var label = CreateDiscoveryLabel(d.Id, d.Name);
            Canvas.SetLeft(label, x2 + ox + 2);
            Canvas.SetTop(label, y1);
            _overlayCanvas.Children.Add(label);
            _discoveryMarkers.Add(label);
            if (isFound) _foundMarkers.Add(label);
        }
    }

    /// <summary>
    /// 도시 ID로 편집 다이얼로그 열기. 저장 성공 시 상태 텍스트로 안내.
    /// 지도에 반영하려면 맵을 다시 로드하거나 재진입 필요.
    /// </summary>
    private async void EditCityById(byte cityId)
    {
        try
        {
            var cityService = ContainerLocator.Container.Resolve<CityService>();
            var city = cityService.GetCachedCities().FirstOrDefault(c => c.Id == cityId);
            if (city == null)
            {
                if (_txtStatus != null) _txtStatus.Text = $"도시 ID {cityId}를 찾을 수 없습니다.";
                return;
            }

            var dialog = new EditCityPixelDialog(
                city.Name,
                city.PixelX,
                city.PixelY,
                city.HasLibrary,
                city.Latitude,
                city.Longitude,
                city.CulturalSphere)
            {
                Owner = Window.GetWindow(this)
            };

            if (dialog.ShowDialog() == true)
            {
                city.Name = dialog.CityName;
                city.PixelX = dialog.PixelX;
                city.PixelY = dialog.PixelY;
                city.HasLibrary = dialog.HasLibrary;
                city.Latitude = dialog.Latitude;
                city.Longitude = dialog.Longitude;
                city.CulturalSphere = dialog.CulturalSphere;

                await cityService.UpdateCityAsync(city);
                if (_txtStatus != null)
                    _txtStatus.Text = $"도시 '{city.Name}' 수정 완료 (지도 반영은 재로드 필요)";
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"도시 정보 수정 실패: {ex.Message}", "오류",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void AddCityPoint(byte cityId, string name, int lat, int lon, bool hasLibrary = false)
    {
        if (_overlayCanvas == null) return;

        // 게임 원본 좌표표(경도 0~40000 / 위도 0~20000)가 먼저다. DB 의 도 단위 값은
        // 반올림으로 3~4칸씩 밀리고 부호가 뒤집힌 것도 있어 표에 없는 ID 일 때만 쓴다.
        var (px, py) = GameMapCoords.TryCityCell(cityId, out var gcx, out var gcy)
            ? CellToPixel(gcx, gcy)
            : LatLonToPixel(lat, lon);

        const double size = 7;
        var fillColor = Color.FromArgb(220, 30, 120, 255);   // 파란색 (도시)
        var strokeColor = Color.FromArgb(255, 0, 40, 140);
        var labelFg = Color.FromRgb(0, 30, 120);

        // 좌/중/우 타일에 동일 마커 복제
        foreach (var ox in TileOffsets)
        {
            var dot = new Ellipse
            {
                Width = size,
                Height = size,
                Fill = new SolidColorBrush(fillColor),
                Stroke = new SolidColorBrush(strokeColor),
                StrokeThickness = 1.5,
                IsHitTestVisible = true,
                Cursor = Cursors.Hand,
                ToolTip = $"{name} (클릭하여 편집)",
                Tag = cityId   // ScrollViewer_MouseDown이 Tag 있는 Ellipse 클릭은 드래그 무시
            };
            dot.MouseLeftButtonDown += (_, e) =>
            {
                e.Handled = true;
                EditCityById(cityId);
            };

            Canvas.SetLeft(dot, px + ox - size / 2);
            Canvas.SetTop(dot, py - size / 2);
            _overlayCanvas.Children.Add(dot);
            _discoveryMarkers.Add(dot);

            // 도서관 배지 ("도") - 점 위쪽에 작은 주황색 버튼
            if (hasLibrary)
            {
                const double badgeSize = 11;
                var badge = new Border
                {
                    Width = badgeSize,
                    Height = badgeSize,
                    Background = new SolidColorBrush(Color.FromRgb(255, 165, 0)),     // Orange
                    BorderBrush = new SolidColorBrush(Color.FromRgb(204, 102, 0)),    // DarkOrange
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(badgeSize / 2),
                    Cursor = Cursors.Hand,
                    IsHitTestVisible = true,
                    ToolTip = $"{name} 도서관 - 클릭하여 도서목록 보기",
                    Tag = cityId,   // ScrollViewer_MouseDown이 Tag 있는 Border 클릭은 드래그 무시
                    Child = new TextBlock
                    {
                        Text = "도",
                        FontSize = 8,
                        FontWeight = FontWeights.Bold,
                        Foreground = Brushes.White,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center,
                        IsHitTestVisible = false
                    }
                };
                badge.MouseLeftButtonDown += (_, e) =>
                {
                    e.Handled = true;
                    ShowLibraryBookList(cityId, name);
                };
                Canvas.SetLeft(badge, px + ox + size / 2);
                Canvas.SetTop(badge, py - size / 2 - badgeSize + 2);
                _overlayCanvas.Children.Add(badge);
                _discoveryMarkers.Add(badge);
            }

            // 라벨: 반투명 배경으로 가독성 확보
            var label = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(180, 255, 255, 255)),
                CornerRadius = new CornerRadius(2),
                Padding = new Thickness(2, 0, 2, 0),
                IsHitTestVisible = false,
                Visibility = _chkShowCityLabels?.IsChecked == true ? Visibility.Visible : Visibility.Collapsed,
                Child = new TextBlock
                {
                    Text = name,
                    FontSize = 7,
                    Foreground = new SolidColorBrush(labelFg),
                }
            };
            Canvas.SetLeft(label, px + ox + size / 2 + 1);
            Canvas.SetTop(label, py - 7);
            _overlayCanvas.Children.Add(label);
            _discoveryMarkers.Add(label);
            _cityLabels.Add(label);
        }
    }

    private void ToggleCityLabels()
    {
        var show = _chkShowCityLabels?.IsChecked == true;
        foreach (var label in _cityLabels)
            label.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ShowLibraryBookList(byte cityId, string cityName)
    {
        try
        {
            var bookService = ContainerLocator.Container.Resolve<BookService>();
            var dialog = new LibraryBookListDialog(cityId, cityName, bookService)
            {
                Owner = Window.GetWindow(this)
            };
            dialog.ShowDialog();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[WorldMap] Library dialog error: {ex.Message}");
            MessageBox.Show($"도서 목록을 표시할 수 없습니다: {ex.Message}", "오류",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private Border CreateDiscoveryLabel(int discoveryId, string name)
    {
        var textBlock = new TextBlock
        {
            Text = name,
            FontSize = 7,
            Foreground = Brushes.Black
        };
        var border = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(160, 255, 255, 255)),
            CornerRadius = new CornerRadius(2),
            Padding = new Thickness(2, 0, 2, 0),
            IsHitTestVisible = true,
            Cursor = Cursors.Hand,
            Tag = discoveryId,
            Child = textBlock
        };
        border.MouseLeftButtonDown += OnDiscoveryLabelClick;
        return border;
    }

    private async void OnDiscoveryLabelClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Border border || border.Tag is not int discoveryId) return;

        e.Handled = true;

        var service = ContainerLocator.Container.Resolve<DiscoveryService>();
        var discovery = service.GetDiscovery(discoveryId);
        if (discovery == null) return;

        // 단일 클릭: 도서/필요스킬 정보 팝업 표시
        if (e.ClickCount == 1)
        {
            ShowDiscoveryInfoPopup(border, discovery);
            return;
        }

        // 더블클릭: 첫 클릭에서 열린 팝업이 남아있으면 닫고 좌표 편집 다이얼로그만 띄움
        if (_discoveryInfoPopup != null) _discoveryInfoPopup.IsOpen = false;

        var item = new DiscoveryDisplayItem
        {
            Id = discovery.Id,
            Name = discovery.Name,
            LatFrom = discovery.LatFrom,
            LatTo = discovery.LatTo,
            LonFrom = discovery.LonFrom,
            LonTo = discovery.LonTo
        };

        var dialog = new EditDiscoveryDialog(item)
        {
            Owner = Window.GetWindow(this)
        };
        if (dialog.ShowDialog() != true) return;

        try
        {
            if (dialog.LatFrom != discovery.LatFrom || dialog.LatTo != discovery.LatTo ||
                dialog.LonFrom != discovery.LonFrom || dialog.LonTo != discovery.LonTo)
            {
                await service.UpdateCoordinateAsync(discoveryId,
                    dialog.LatFrom, dialog.LatTo, dialog.LonFrom, dialog.LonTo);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"수정 실패: {ex.Message}", "오류",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    /// <summary>
    /// 발견물 라벨 클릭 시 관련 도서/언어/필요스킬/소재 도서관 도시를 팝업으로 표시.
    /// 우측 상단 X 버튼으로 닫는다 (StaysOpen=true 이므로 다른 곳 클릭해도 유지).
    /// </summary>
    private void ShowDiscoveryInfoPopup(UIElement target, DiscoveryEntity discovery)
    {
        // 도서 정보 찾기 (쉼표 구분된 여러 도서명일 수 있음)
        var bookNames = string.IsNullOrWhiteSpace(discovery.BookName)
            ? new List<string>()
            : discovery.BookName.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();

        var bookService = ContainerLocator.Container.Resolve<BookService>();
        var cached = bookService.GetCachedBooks();
        var matchedBooks = bookNames
            .Select(n => cached.FirstOrDefault(b => string.Equals(b.Name, n, StringComparison.OrdinalIgnoreCase)))
            .Where(b => b != null)
            .Cast<Book>()
            .ToList();

        // 최상단 헤더 (이름 + X 버튼)
        var headerGrid = new Grid { Margin = new Thickness(0, 0, 0, 6) };
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var titleBlock = new TextBlock
        {
            Text = discovery.Name,
            FontWeight = FontWeights.Bold,
            FontSize = 13,
            VerticalAlignment = VerticalAlignment.Center,
            TextWrapping = TextWrapping.Wrap
        };
        Grid.SetColumn(titleBlock, 0);
        headerGrid.Children.Add(titleBlock);

        var btnClose = new Button
        {
            Content = "✕",
            Width = 20,
            Height = 20,
            Padding = new Thickness(0),
            Margin = new Thickness(8, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Top,
            FontSize = 11,
            Cursor = Cursors.Hand,
            Background = Brushes.Transparent,
            BorderBrush = new SolidColorBrush(Color.FromRgb(200, 200, 200)),
            BorderThickness = new Thickness(1),
            ToolTip = "닫기"
        };
        btnClose.Click += (_, _) =>
        {
            if (_discoveryInfoPopup != null) _discoveryInfoPopup.IsOpen = false;
        };
        Grid.SetColumn(btnClose, 1);
        headerGrid.Children.Add(btnClose);

        var panel = new StackPanel { Margin = new Thickness(10, 8, 10, 8) };
        panel.Children.Add(headerGrid);

        if (matchedBooks.Count == 0 && bookNames.Count == 0)
        {
            panel.Children.Add(new TextBlock { Text = "관련 도서 없음", Foreground = Brushes.Gray, FontSize = 11 });
        }
        else if (matchedBooks.Count == 0)
        {
            // DB에서 못찾았지만 이름은 있음
            panel.Children.Add(new TextBlock { Text = $"도서: {string.Join(", ", bookNames)}", FontSize = 11 });
        }
        else
        {
            foreach (var b in matchedBooks)
            {
                var bookPanel = new StackPanel { Margin = new Thickness(0, 2, 0, 2) };
                bookPanel.Children.Add(new TextBlock { Text = $"📖 {b.Name}", FontWeight = FontWeights.SemiBold, FontSize = 12 });
                if (!string.IsNullOrWhiteSpace(b.Language))
                    bookPanel.Children.Add(new TextBlock { Text = $"  언어: {b.Language}", FontSize = 11, Foreground = Brushes.DimGray });
                if (!string.IsNullOrWhiteSpace(b.Required))
                    bookPanel.Children.Add(new TextBlock { Text = $"  필요스킬: {b.Required}", FontSize = 11, Foreground = Brushes.DimGray });
                if (b.LibraryCityNames?.Count > 0)
                {
                    bookPanel.Children.Add(new TextBlock
                    {
                        Text = $"  소재 도시: {string.Join(", ", b.LibraryCityNames)}",
                        FontSize = 11,
                        Foreground = Brushes.DimGray,
                        TextWrapping = TextWrapping.Wrap,
                        MaxWidth = 320
                    });
                }
                panel.Children.Add(bookPanel);
            }
        }

        var border = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(245, 255, 255, 255)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(80, 80, 80)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Child = panel,
            Effect = new System.Windows.Media.Effects.DropShadowEffect
            {
                ShadowDepth = 2,
                BlurRadius = 6,
                Opacity = 0.3
            }
        };

        if (_discoveryInfoPopup == null)
        {
            _discoveryInfoPopup = new Popup
            {
                Placement = PlacementMode.Right,
                StaysOpen = true, // 다른 곳 클릭해도 유지. X 버튼으로만 닫힘
                AllowsTransparency = true
            };
        }

        _discoveryInfoPopup.PlacementTarget = target;
        _discoveryInfoPopup.Child = border;
        _discoveryInfoPopup.IsOpen = true;
    }

    #endregion

    #region 좌표 추적

    private void ToggleTracking()
    {
        if (_isTracking)
        {
            StopTracking();
        }
        else
        {
            var hWnd = GameWindowHelper.FindGameWindow();
            if (hWnd == IntPtr.Zero)
            {
                MessageBox.Show("게임 창을 찾을 수 없습니다.", "알림",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            StartTracking();
        }
    }

    private void StartTracking()
    {
        _isTracking = true;
        _isProcessing = false;
        _centerOnFirstPosition = true;
        if (_btnTrackCoordinate != null) _btnTrackCoordinate.Content = "추적 중지";

        _trackingTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(GetTrackingInterval()) };
        _trackingTimer.Tick += OnTrackingTick;
        _trackingTimer.Start();
    }

    private void StopTracking()
    {
        _isTracking = false;
        _trackingTimer?.Stop();
        _trackingTimer = null;

        if (_btnTrackCoordinate != null) _btnTrackCoordinate.Content = "좌표 추적";
        if (_txtCurrentCoordinate != null) _txtCurrentCoordinate.Text = "";
        for (int i = 0; i < 3; i++)
        {
            if (_positionMarkers[i] != null) _positionMarkers[i]!.Visibility = Visibility.Collapsed;
            if (_positionPulses[i] != null) _positionPulses[i]!.Visibility = Visibility.Collapsed;
        }
    }

    private async void OnTrackingTick(object? sender, EventArgs e)
    {
        if (_isProcessing) return;
        _isProcessing = true;

        try
        {
            var hWnd = GameWindowHelper.FindGameWindow();
            if (hWnd == IntPtr.Zero) return;

            using var bitmap = GameWindowHelper.CaptureClient(hWnd);
            if (bitmap == null) return;

            bool inCity = GameScreenDetector.IsInCity(bitmap);

            // 자동이동 중이면 nav 루프에서 처리하므로 생략
            if (!_isNavigating)
            {
                var detection = await _screenDetector.DetectScreenWithOcrAsync(bitmap);
                // 메뉴가 떠 있으면 좌표 OCR이 가려짐. 사용자가 연 것일 수 있으니 닫지는 않고 상태만 표시
                if (detection.Screen is GameScreen.CommandMenu or GameScreen.HintList or GameScreen.InfoMenu)
                {
                    if (_txtCurrentCoordinate != null)
                        _txtCurrentCoordinate.Text = $"📋 메뉴 열림 ({detection.Screen}) - 추적 대기";
                    return;
                }
            }

            // 1차: 메모리 직접 읽기 (분 단위 정밀, 메뉴 가림 무관)
            var location = _gameMemoryReader.DetectLocation();
            if (location == GameMemoryReader.Location.AtSea)
            {
                var memCoord = _gameMemoryReader.TryReadLatLon();
                if (memCoord.HasValue)
                {
                    var (memLat, memLon) = memCoord.Value;
                    string coordText = $"{(memLat >= 0 ? "북위" : "남위")} {Math.Abs(memLat):F1}  {(memLon >= 0 ? "동경" : "서경")} {Math.Abs(memLon):F1}";
                    if (_txtCurrentCoordinate != null)
                        _txtCurrentCoordinate.Text = $"🧭 {coordText}";
                    if (_mapData != null)
                        UpdatePositionMarker(memLat, memLon);
                    return;
                }
            }
            if (location == GameMemoryReader.Location.NotAtSea)
            {
                // 도시/이벤트/메뉴 등. inCity 이미지 감지가 실패해도
                // 메모리의 도시 ID가 유효 범위면 도시로 판단.
                var cityId = _gameMemoryReader.TryReadCurrentCityId();
                var city = ResolveCity(cityId);

                if (_txtCurrentCoordinate != null)
                {
                    _txtCurrentCoordinate.Text = city != null
                        ? $"📍 {city.Name}"
                        : (inCity ? "📍 도시 안" : "📋 이벤트/메뉴 진행 중");
                }

                // 도시 위도/경도 알면 그 위치에 마커 표시
                if (city != null && city.Latitude.HasValue && city.Longitude.HasValue && _mapData != null)
                    UpdatePositionMarker(city.Latitude.Value, city.Longitude.Value);

                return;
            }

            // 2차 폴백: 게임 프로세스 못 찾을 때만 OCR
            var prediction = await Task.Run(() => _coordinateOcr.PredictOcrAsync(bitmap));

            if (prediction == null)
            {
                if (_txtCurrentCoordinate != null)
                    _txtCurrentCoordinate.Text = inCity ? "📍 도시 안" : "🧭 탐험 중 - 좌표 인식 실패";
                return;
            }

            if (_txtCurrentCoordinate != null)
                _txtCurrentCoordinate.Text = inCity
                    ? $"📍 도시 안 ({prediction})"
                    : $"🧭 탐험 중 - {prediction}";

            if (_mapData != null)
                UpdatePositionMarker(prediction.ToLat(), prediction.ToLon());
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[WorldMap] Tracking error: {ex.Message}");
        }
        finally
        {
            _isProcessing = false;
        }
    }

    private (double px, double py) LatLonToPixel(double lat, double lon)
    {
        double cellX = (lon + 180.0) / 360.0 * RenderTileW;
        double cellY = (90.0 - lat) / 180.0 * RenderH;
        // 중앙 타일(index 1) 오프셋 적용
        return (cellX + RenderTileW, cellY);
    }

    /// <summary>
    /// 펼친 칸 좌표(x 0~2500, y 0~1250)를 지도 픽셀로. 게임 원본 좌표표를 쓰는 마커가 이 길로 온다 —
    /// 도 단위를 거치지 않아 반올림이 끼지 않는다.
    /// </summary>
    private static (double px, double py) CellToPixel(double cellX, double cellY)
        => (cellX * RenderScale + RenderTileW, cellY * RenderScale);

    /// <summary>
    /// 너무 넓은 발견물 범위는 지도를 뒤덮어 다른 마커를 가린다(남극대륙 2500x150, 신대륙 481x751).
    /// WorldMapKR 과 같이 가운데를 기준으로 이 칸 수까지만 그린다.
    /// </summary>
    private const double MaxDiscoverySpanCells = 300;

    private static (double x1, double y1, double x2, double y2) ClampSpan(double x1, double y1, double x2, double y2)
    {
        if (x2 - x1 > MaxDiscoverySpanCells)
        {
            double mid = (x1 + x2) / 2;
            x1 = mid - MaxDiscoverySpanCells / 2;
            x2 = mid + MaxDiscoverySpanCells / 2;
        }
        if (y2 - y1 > MaxDiscoverySpanCells)
        {
            double mid = (y1 + y2) / 2;
            y1 = mid - MaxDiscoverySpanCells / 2;
            y2 = mid + MaxDiscoverySpanCells / 2;
        }
        return (x1, y1, x2, y2);
    }

    /// <summary>도시 ID → City 객체. 캐시 미스 시 동기 로드 시도 후 한 번만 캐싱.</summary>
    private City? ResolveCity(byte? id)
    {
        if (id == null) return null;
        if (_cityCache == null)
        {
            try
            {
                var svc = ContainerLocator.Container.Resolve<CityService>();
                _cityCache = svc.GetCitiesWithCoordinatesFromDbAsync().GetAwaiter().GetResult();
            }
            catch { return null; }
        }
        return _cityCache.FirstOrDefault(c => c.Id == id.Value);
    }

    /// <summary>두 점 사이 구간 속도 (°/분) 계산.</summary>
    private static string ComputeSegmentSpeed(Point prev, Point cur, DateTime tPrev, DateTime tCur)
    {
        double degPerMin = ComputeSegmentSpeedValue(prev, cur, tPrev, tCur);
        return degPerMin > 0 ? $"{degPerMin:F1}°/분" : "";
    }

    /// <summary>구간 속도(°/분) 반환. 시간 간격이 너무 짧으면 0.</summary>
    private static double ComputeSegmentSpeedValue(Point prev, Point cur, DateTime tPrev, DateTime tCur)
    {
        var span = tCur - tPrev;
        if (span.TotalSeconds < 0.5) return 0;

        const double degPerPixel = 360.0 / UnfoldedW;
        double dxDeg = (cur.X - prev.X) * degPerPixel;
        double dyDeg = (cur.Y - prev.Y) * degPerPixel;
        double distDeg = Math.Sqrt(dxDeg * dxDeg + dyDeg * dyDeg);
        return distDeg / span.TotalMinutes;
    }

    /// <summary>속도(°/분) → 색상. 0~SpeedMax 범위를 회색→빨강으로 보간.</summary>
    private const double SpeedColorMax = 300.0;

    /// <summary>
    /// 두 trail 점 사이 시간 간격이 이 값(분)을 넘으면 segment 선을 그리지 않음.
    /// 불러온 경로의 마지막 점 ↔ 다음 추적 세션 첫 점 사이 대각선 생기는 문제 방지.
    /// </summary>
    private const double TrailSegmentGapMinutes = 1.0;
    private static Color SpeedToColor(double degPerMin)
    {
        double t = Math.Clamp(degPerMin / SpeedColorMax, 0.0, 1.0);
        // 회색 #888888 ~ 빨강 #FF3C3C 보간
        byte r = (byte)(0x88 + (0xFF - 0x88) * t);
        byte g = (byte)(0x88 + (0x3C - 0x88) * t);
        byte b = (byte)(0x88 + (0x3C - 0x88) * t);
        return Color.FromArgb(200, r, g, b);
    }

    /// <summary>이전 점에서 현재 점까지 좌/중/우 타일에 속도별 색상 Line segment 추가.</summary>
    private void AddTrailSegment(Point prev, Point cur, double speed)
    {
        if (_overlayCanvas == null) return;

        // 세계 래핑 보정: X 차이가 지도 폭의 절반 이상이면 반대편 경계로 넘어간 것.
        // cur를 ±RenderTileW 평행이동해 맵 밖으로 짧게 연장시키고, 3-tile 복제가 반대편 경계를 그려줌.
        double dx = cur.X - prev.X;
        if (dx > RenderTileW / 2.0)
            cur = new Point(cur.X - RenderTileW, cur.Y);
        else if (dx < -RenderTileW / 2.0)
            cur = new Point(cur.X + RenderTileW, cur.Y);

        var brush = new SolidColorBrush(SpeedToColor(speed));
        brush.Freeze();
        for (int i = 0; i < 3; i++)
        {
            var ox = TileOffsets[i];
            var line = new Line
            {
                X1 = prev.X + ox,
                Y1 = prev.Y,
                X2 = cur.X + ox,
                Y2 = cur.Y,
                Stroke = brush,
                StrokeThickness = 2,
                IsHitTestVisible = false,
            };
            _overlayCanvas.Children.Add(line);
            _trailSegmentLines[i].Add(line);
        }
    }

    // 속도 라벨 공유 리소스 (Freeze → 모든 라벨이 공유)
    private static readonly SolidColorBrush SpeedLabelBgBrush = CreateFrozenBrush(180, 255, 255, 255);
    private static readonly Typeface SpeedLabelTypeface = new("Segoe UI");
    private const double SpeedLabelFontSize = 6;
    private const double SpeedLabelPadX = 1;

    /// <summary>새 궤적 점에 구간 속도 라벨을 좌/중/우 타일 각각에 추가.</summary>
    private void AddTrailSpeedLabel(Point point, string text)
    {
        if (_speedLabelHost == null) return;

        var pixelsPerDip = VisualTreeHelper.GetDpi(_speedLabelHost).PixelsPerDip;
        var formatted = new FormattedText(
            text,
            System.Globalization.CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            SpeedLabelTypeface,
            SpeedLabelFontSize,
            Brushes.Black,
            pixelsPerDip);

        var bgW = formatted.Width + SpeedLabelPadX * 2;
        var bgH = formatted.Height;

        for (int i = 0; i < 3; i++)
        {
            var visual = new DrawingVisual();
            using (var dc = visual.RenderOpen())
            {
                var x = point.X + TileOffsets[i] + 3;
                var y = point.Y + 2;
                dc.DrawRectangle(SpeedLabelBgBrush, null, new Rect(x, y, bgW, bgH));
                dc.DrawText(formatted, new Point(x + SpeedLabelPadX, y));
            }
            _speedLabelHost.AddVisual(visual);
        }
    }

    private void UpdatePositionMarker(double lat, double lon)
    {
        if (_overlayCanvas == null) return;

        var (px, py) = LatLonToPixel(lat, lon);

        const double markerSize = 7;
        const double pulseSize = 12;

        // 좌/중/우 타일에 pulse, marker 생성 (trail은 segment 단위로 동적 생성)
        for (int i = 0; i < 3; i++)
        {
            if (_positionPulses[i] == null)
            {
                _positionPulses[i] = new Ellipse
                {
                    Width = pulseSize, Height = pulseSize,
                    Fill = Brushes.Transparent,
                    Stroke = new SolidColorBrush(Color.FromRgb(0, 120, 212)),
                    StrokeThickness = 2,
                    Opacity = 0.5
                };
                _overlayCanvas.Children.Add(_positionPulses[i]!);
            }

            if (_positionMarkers[i] == null)
            {
                _positionMarkers[i] = new Ellipse
                {
                    Width = markerSize, Height = markerSize,
                    Fill = new SolidColorBrush(Color.FromRgb(255, 50, 50)),
                    Stroke = Brushes.White,
                    StrokeThickness = 2
                };
                _overlayCanvas.Children.Add(_positionMarkers[i]!);
            }
        }

        // 경로에 포인트 추가 (각 타일에 오프셋 적용)
        var newPoint = new Point(px, py);
        var now = DateTime.Now;
        if (_trailPoints.Count == 0 || DistanceSq(_trailPoints[^1], newPoint) > 4)
        {
            // 새 점에 대한 구간 속도 계산 및 segment line / 라벨 생성 (이전 점이 있을 때만)
            if (_trailPoints.Count >= 1)
            {
                var prev = _trailPoints[^1];
                var tPrev = _trailTimestamps[^1];
                // 시간 간격이 너무 크면(불러온 경로의 마지막 점 ↔ 세션 재개 등) 구간 연결 스킵
                if ((now - tPrev).TotalMinutes <= TrailSegmentGapMinutes)
                {
                    double speed = ComputeSegmentSpeedValue(prev, newPoint, tPrev, now);
                    if (speed > 0)
                    {
                        AddTrailSegment(prev, newPoint, speed);
                        AddTrailSpeedLabel(newPoint, $"{speed:F1}°/분");
                    }
                }
            }

            _trailPoints.Add(newPoint);
            _trailTimestamps.Add(now);
            for (int i = 0; i < 3; i++)
            {
                _trailPointsByTile[i].Add(new Point(newPoint.X + TileOffsets[i], newPoint.Y));
            }
            AppendTrailPointToSession(newPoint, now);
        }
        else if (_trailTimestamps.Count > 0)
        {
            // 같은 자리에 머물러도 세션이 계속 살아있다는 신호로 마지막 타임스탬프 갱신.
            // → 정지 후 재이동 시 TrailSegmentGapMinutes 가드에 걸려 선이 끊기는 문제 방지.
            _trailTimestamps[_trailTimestamps.Count - 1] = now;
        }

        for (int i = 0; i < 3; i++)
        {
            var ox = TileOffsets[i];
            _positionMarkers[i]!.Visibility = Visibility.Visible;
            _positionPulses[i]!.Visibility = Visibility.Visible;

            Canvas.SetLeft(_positionMarkers[i], px + ox - markerSize / 2);
            Canvas.SetTop(_positionMarkers[i], py - markerSize / 2);
            Canvas.SetLeft(_positionPulses[i], px + ox - pulseSize / 2);
            Canvas.SetTop(_positionPulses[i], py - pulseSize / 2);
        }

        if (_centerOnFirstPosition)
        {
            _centerOnFirstPosition = false;
            ScrollToImagePosition(px, py);
        }
        else
        {
            ScrollToMarkerIfNeeded(px, py);
        }
    }

    private void GoToMyLocation()
    {
        // 중앙 타일(_positionMarkers[1]) 기준으로 좌표 계산
        var marker = _positionMarkers[1];
        if (marker == null || marker.Visibility != Visibility.Visible) return;

        var px = Canvas.GetLeft(marker) + 3.5; // markerSize/2
        var py = Canvas.GetTop(marker) + 3.5;

        ScrollToImagePosition(px, py);
    }

    private void ScrollToImagePosition(double imageX, double imageY)
    {
        if (_scrollViewer == null) return;

        var offsetX = (imageX * _currentScale) - (_scrollViewer.ViewportWidth / 2);
        var offsetY = (imageY * _currentScale) - (_scrollViewer.ViewportHeight / 2);

        _isWrapping = true;
        _scrollViewer.ScrollToHorizontalOffset(Math.Max(0, offsetX));
        _scrollViewer.ScrollToVerticalOffset(Math.Max(0, offsetY));
        _isWrapping = false;
    }

    private void ScrollToMarkerIfNeeded(double pixelX, double pixelY)
    {
        if (_scrollViewer == null) return;

        var viewW = _scrollViewer.ViewportWidth;
        var viewH = _scrollViewer.ViewportHeight;

        // 마커는 좌/중/우 3타일에 복제 표시되므로, 뷰포트 중심에 가장 가까운 복제본을 기준으로 검사.
        var bestScreenX = double.NaN;
        var bestDistFromCenter = double.PositiveInfinity;
        foreach (var ox in TileOffsets)
        {
            var sx = (pixelX + ox) * _currentScale - _scrollViewer.HorizontalOffset;
            var dist = Math.Abs(sx - viewW / 2.0);
            if (dist < bestDistFromCenter)
            {
                bestDistFromCenter = dist;
                bestScreenX = sx;
            }
        }
        var screenY = (pixelY * _currentScale) - _scrollViewer.VerticalOffset;

        // threshold = 안전영역 폭 비율. 0.5 → 뷰포트 가운데 50%가 안전영역 (marginX = viewW * 0.25)
        var threshold = Math.Clamp(GetAutoScrollThreshold(), 0.0, 1.0);
        var marginX = viewW * (1.0 - threshold) / 2.0;
        var marginY = viewH * (1.0 - threshold) / 2.0;

        if (bestScreenX < marginX || bestScreenX > viewW - marginX ||
            screenY < marginY || screenY > viewH - marginY)
        {
            ScrollToImagePosition(pixelX, pixelY);
        }
    }

    private static double DistanceSq(Point a, Point b)
    {
        var dx = a.X - b.X;
        var dy = a.Y - b.Y;
        return dx * dx + dy * dy;
    }

    private void ClearTrail()
    {
        _trailPoints.Clear();
        _trailTimestamps.Clear();
        for (int i = 0; i < 3; i++)
        {
            _trailPointsByTile[i].Clear();
            if (_overlayCanvas != null)
            {
                foreach (var line in _trailSegmentLines[i])
                    _overlayCanvas.Children.Remove(line);
            }
            _trailSegmentLines[i].Clear();
        }
        _speedLabelHost?.Clear();
    }

    private record TrailCoord(double lat, double lon, string? time = null);

    /// <summary>
    /// 세션 시작 시 첫 점이 들어올 때 lazy 생성. 같은 세션 동안 같은 파일에 append.
    /// </summary>
    private string GetOrCreateSessionTrailPath()
    {
        if (_sessionTrailPath != null) return _sessionTrailPath;

        var dir = AppSettings.TrailDirectory;
        if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

        var fileName = $"session_{DateTime.Now:yyyyMMdd_HHmmss}.trail.jsonl";
        _sessionTrailPath = Path.Combine(dir, fileName);
        return _sessionTrailPath;
    }

    /// <summary>현재 세션 파일에 좌표 한 줄을 append. 실패는 조용히 무시 (추적 동작에 영향 없도록).</summary>
    private void AppendTrailPointToSession(Point point, DateTime time)
    {
        try
        {
            // 픽셀 → 경위도 (3-타일 무한 스크롤 정규화)
            double cellX = ((point.X % RenderTileW) + RenderTileW) % RenderTileW;
            double lon = cellX * 360.0 / RenderTileW - 180;
            double lat = 90.0 - point.Y * 180.0 / RenderH;

            var coord = new TrailCoord(lat, lon, time.ToString("yyyy-MM-dd HH:mm:ss.fff"));
            var line = JsonSerializer.Serialize(coord);
            File.AppendAllText(GetOrCreateSessionTrailPath(), line + Environment.NewLine);
        }
        catch { /* 자동 저장 실패는 추적 흐름을 막지 않음 */ }
    }

    private void SaveTrail()
    {
        if (_trailPoints.Count == 0)
        {
            MessageBox.Show("저장할 경로가 없습니다.", "경로 저장",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var dir = AppSettings.TrailDirectory;
        if (!Directory.Exists(dir))
            Directory.CreateDirectory(dir);

        var fileName = $"worldmap_trail_{DateTime.Now:yyyyMMdd_HHmmss}.trail.json";
        var filePath = Path.Combine(dir, fileName);

        var coords = _trailPoints.Select((p, i) =>
        {
            // 픽셀 → 경위도 역변환 (중앙 타일 기준)
            double cellX = p.X - RenderTileW;
            double lon = cellX * 360.0 / RenderTileW - 180;
            double lat = 90.0 - p.Y * 180.0 / RenderH;
            var time = i < _trailTimestamps.Count
                ? _trailTimestamps[i].ToString("yyyy-MM-dd HH:mm:ss.fff")
                : "";
            return new TrailCoord(lat, lon, time);
        }).ToList();

        var json = JsonSerializer.Serialize(coords, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(filePath, json);
        MessageBox.Show($"저장 완료: {fileName}", "경로 저장",
            MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void LoadTrail()
    {
        var dir = AppSettings.TrailDirectory;
        if (!Directory.Exists(dir))
        {
            MessageBox.Show("경로 디렉토리가 없습니다.", "경로 불러오기",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var files = Directory.GetFiles(dir, "*.trail.json")
            .OrderByDescending(f => File.GetLastWriteTime(f))
            .ToArray();

        if (files.Length == 0)
        {
            MessageBox.Show("저장된 경로 파일이 없습니다.", "경로 불러오기",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var dlg = new OpenFileDialog
        {
            InitialDirectory = dir,
            Filter = "Trail 파일 (*.trail.json)|*.trail.json",
            Title = "경로 파일 선택"
        };
        if (dlg.ShowDialog() != true) return;

        try
        {
            var json = File.ReadAllText(dlg.FileName);
            var coords = JsonSerializer.Deserialize<List<TrailCoord>>(json);
            if (coords == null || coords.Count == 0) return;

            // 기존 경로 완전히 초기화 (segment line 포함)
            ClearTrail();

            foreach (var c in coords)
            {
                var (px, py) = LatLonToPixel(c.lat, c.lon);
                var newPoint = new Point(px, py);
                var t = DateTime.TryParse(c.time, out var parsed) ? parsed : DateTime.Now;

                if (_trailPoints.Count >= 1)
                {
                    var prev = _trailPoints[^1];
                    var tPrev = _trailTimestamps[^1];
                    // 세션 간 큰 시간 간격은 연결 선 생략 (불러온 경로 안에서도 별도 세션이면 자연스럽게 끊김)
                    if ((t - tPrev).TotalMinutes <= TrailSegmentGapMinutes)
                    {
                        double speed = ComputeSegmentSpeedValue(prev, newPoint, tPrev, t);
                        if (speed > 0)
                        {
                            AddTrailSegment(prev, newPoint, speed);
                            AddTrailSpeedLabel(newPoint, $"{speed:F1}°/분");
                        }
                    }
                }

                _trailPoints.Add(newPoint);
                _trailTimestamps.Add(t);
                for (int i = 0; i < 3; i++)
                {
                    _trailPointsByTile[i].Add(new Point(px + TileOffsets[i], py));
                }
            }

            if (_txtStatus != null)
                _txtStatus.Text = $"경로 로드: {coords.Count}개 포인트";
        }
        catch (Exception ex)
        {
            MessageBox.Show($"경로 불러오기 실패: {ex.Message}", "오류",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    #endregion

    #region 우클릭 메뉴 / 자동이동

    private (double lat, double lon) PixelToLatLon(double px, double py)
    {
        // 3-타일 무한 스크롤: 왼쪽/가운데/오른쪽 어느 복사본을 클릭해도 같은 경도가 되도록
        // 한 타일 폭 안으로 정규화 (modulo).
        double cellX = ((px % RenderTileW) + RenderTileW) % RenderTileW;
        double lon = cellX * 360.0 / RenderTileW - 180;
        double lat = 90.0 - py * 180.0 / RenderH;
        return (lat, lon);
    }

    private void OnOverlayLeftClick(object sender, MouseButtonEventArgs e)
    {
        // 드래그용으로 통과
        e.Handled = false;
    }

    private void OnMapRightClick(object sender, MouseButtonEventArgs e)
    {
        if (_overlayCanvas == null || _mapData == null) return;

        var pos = e.GetPosition(_overlayCanvas);
        var (lat, lon) = PixelToLatLon(pos.X, pos.Y);

        var latDir = lat >= 0 ? "N" : "S";
        var lonDir = lon >= 0 ? "E" : "W";
        var coordText = $"{Math.Abs(lat):F0}{latDir} {Math.Abs(lon):F0}{lonDir}";

        var menu = new ContextMenu();

        var navItem = new MenuItem { Header = $"이 위치로 이동 ({coordText})" };
        navItem.Click += (_, _) =>
        {
            ShowDestination(pos.X, pos.Y);
            StartNavigation(lat, lon);
        };
        menu.Items.Add(navItem);

        if (_isNavigating)
        {
            var stopItem = new MenuItem { Header = "이동 중지" };
            stopItem.Click += (_, _) => StopNavigation();
            menu.Items.Add(stopItem);
        }

        menu.IsOpen = true;
        e.Handled = true;
    }

    private void ShowDestination(double px, double py)
    {
        if (_overlayCanvas == null) return;

        const double size = 14;
        for (int i = 0; i < 3; i++)
        {
            if (_destMarkers[i] == null)
            {
                _destMarkers[i] = new Ellipse
                {
                    Width = size,
                    Height = size,
                    Fill = new SolidColorBrush(Color.FromArgb(100, 0, 200, 0)),
                    Stroke = new SolidColorBrush(Color.FromRgb(0, 180, 0)),
                    StrokeThickness = 2,
                    IsHitTestVisible = false
                };
                _overlayCanvas.Children.Add(_destMarkers[i]!);
            }

            _destMarkers[i]!.Visibility = Visibility.Visible;
            Canvas.SetLeft(_destMarkers[i], px + TileOffsets[i] - size / 2);
            Canvas.SetTop(_destMarkers[i], py - size / 2);
        }
    }

    private void SetNavStatus(string text)
    {
        if (_txtNavStatus != null) _txtNavStatus.Text = text;
    }

    private void ToggleSpeedLabels()
    {
        if (_speedLabelHost == null) return;
        var visible = _chkShowSpeed?.IsChecked == true;
        _speedLabelHost.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
    }

    private double GetAutoScrollThreshold()
    {
        if (_cmbAutoScrollThreshold?.SelectedItem is ComboBoxItem item &&
            item.Tag is string tag &&
            double.TryParse(tag, System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out var v))
            return v;
        return 0.5;
    }

    private double GetTrackingInterval()
    {
        if (_cmbTrackingInterval?.SelectedItem is ComboBoxItem item &&
            item.Tag is string tag &&
            double.TryParse(tag, System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out var v))
            return v;
        return 2.0;
    }

    private void SelectTrackingIntervalItem(double value)
    {
        if (_cmbTrackingInterval == null) return;
        foreach (var obj in _cmbTrackingInterval.Items)
        {
            if (obj is ComboBoxItem item && item.Tag is string tag &&
                double.TryParse(tag, System.Globalization.NumberStyles.Any,
                    System.Globalization.CultureInfo.InvariantCulture, out var v) &&
                Math.Abs(v - value) < 0.001)
            {
                _cmbTrackingInterval.SelectedItem = item;
                return;
            }
        }
        // 일치 없으면 2초 기본값 선택
        foreach (var obj in _cmbTrackingInterval.Items)
        {
            if (obj is ComboBoxItem item && (item.Tag as string) == "2")
            {
                _cmbTrackingInterval.SelectedItem = item;
                return;
            }
        }
    }

    /// <summary>추적 중일 때 주기 설정이 바뀌면 기존 타이머에 새 간격 적용.</summary>
    private void ApplyTrackingInterval()
    {
        if (_trackingTimer != null)
            _trackingTimer.Interval = TimeSpan.FromSeconds(GetTrackingInterval());
    }

    private void SelectAutoScrollThresholdItem(double value)
    {
        if (_cmbAutoScrollThreshold == null) return;
        foreach (var obj in _cmbAutoScrollThreshold.Items)
        {
            if (obj is ComboBoxItem item && item.Tag is string tag &&
                double.TryParse(tag, System.Globalization.NumberStyles.Any,
                    System.Globalization.CultureInfo.InvariantCulture, out var v) &&
                Math.Abs(v - value) < 0.001)
            {
                _cmbAutoScrollThreshold.SelectedItem = item;
                return;
            }
        }
        // 일치하는 항목이 없으면 기본 50%
        foreach (var obj in _cmbAutoScrollThreshold.Items)
        {
            if (obj is ComboBoxItem item && (item.Tag as string) == "0.5")
            {
                _cmbAutoScrollThreshold.SelectedItem = item;
                return;
            }
        }
    }

    private void ShowArrivedStatus(CoordinatePrediction prediction)
    {
        SetNavStatus($"✅ 도착! ({prediction})");
        if (_btnStopNav != null) _btnStopNav.Visibility = Visibility.Collapsed;

        // 10초 후 자동으로 도착 메시지 지움
        _arrivalClearTimer?.Stop();
        _arrivalClearTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(10) };
        _arrivalClearTimer.Tick += (_, _) =>
        {
            _arrivalClearTimer?.Stop();
            _arrivalClearTimer = null;
            if (!_isNavigating) SetNavStatus("");
        };
        _arrivalClearTimer.Start();
    }

    private void StartNavigation(double destLat, double destLon)
    {
        StopNavigation();

        var hWnd = GameWindowHelper.FindGameWindow();
        if (hWnd == IntPtr.Zero)
        {
            MessageBox.Show("게임 창을 찾을 수 없습니다.", "알림",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        // 추적이 꺼져 있으면 자동으로 켜기
        if (!_isTracking)
            StartTracking();

        _arrivalClearTimer?.Stop();
        _arrivalClearTimer = null;

        _isNavigating = true;
        _navCts = new CancellationTokenSource();

        var destLatDir = destLat >= 0 ? "N" : "S";
        var destLonDir = destLon >= 0 ? "E" : "W";
        SetNavStatus($"🎯 자동이동 중 → {Math.Abs(destLat):F0}°{destLatDir} {Math.Abs(destLon):F0}°{destLonDir}");
        if (_btnStopNav != null) _btnStopNav.Visibility = Visibility.Visible;

        Task.Run(async () =>
        {
            var token = _navCts.Token;

            // 게임 창 활성화
            GameWindowHelper.BringToFront(hWnd);
            await Task.Delay(300, token);

            // 이동 루프
            while (!token.IsCancellationRequested)
            {
                try
                {
                    var navHWnd = GameWindowHelper.FindGameWindow();
                    if (navHWnd == IntPtr.Zero)
                    {
                        await Task.Delay(2000, token);
                        continue;
                    }

                    using var bitmap = GameWindowHelper.CaptureClient(navHWnd);
                    if (bitmap == null)
                    {
                        await Task.Delay(500, token);
                        continue;
                    }

                    // 다이얼로그/힌트 화면이 떠있으면 닫기
                    var detection = await _screenDetector.DetectScreenWithOcrAsync(bitmap);
                    var screen = detection.Screen;
                    if (screen is GameScreen.HintList or GameScreen.InfoMenu or GameScreen.CommandMenu)
                    {
                        Dispatcher.Invoke(() => SetNavStatus($"📋 화면 닫는 중... ({screen})"));
                        await GameScreenDetector.DismissDialogAsync(navHWnd, screen, token);
                        await Task.Delay(500, token);
                        continue;
                    }

                    // 1차: 메모리 직접 읽기 (서경 51° 이서는 손상되어 null)
                    double curLat, curLon;
                    string coordLabel;
                    var memCoord = _gameMemoryReader.TryReadLatLon();
                    CoordinatePrediction? prediction = null;
                    if (memCoord.HasValue)
                    {
                        (curLat, curLon) = memCoord.Value;
                        coordLabel = $"{(curLat >= 0 ? "북위" : "남위")} {Math.Abs(curLat):F1}  {(curLon >= 0 ? "동경" : "서경")} {Math.Abs(curLon):F1}";
                    }
                    else
                    {
                        // 2차 폴백: OCR
                        prediction = await _coordinateOcr.PredictOcrAsync(bitmap);
                        if (prediction == null)
                        {
                            await Task.Delay(500, token);
                            continue;
                        }
                        curLat = prediction.ToLat();
                        curLon = prediction.ToLon();
                        coordLabel = prediction.ToString();
                    }

                    // 도착 판정
                    if (NavigationCalculator.IsNear(curLat, curLon, destLat, destLon, threshold: 2.0))
                    {
                        GameWindowHelper.SendNumpadKey(navHWnd, 5);
                        // OCR로 받은 게 없으면 메모리 좌표로 합성
                        var arrivalPrediction = prediction ?? new CoordinatePrediction(
                            curLat >= 0, (int)Math.Round(Math.Abs(curLat)),
                            curLon >= 0, (int)Math.Round(Math.Abs(curLon)));
                        Dispatcher.Invoke(() =>
                        {
                            _isNavigating = false;
                            for (int i = 0; i < 3; i++)
                                if (_destMarkers[i] != null) _destMarkers[i]!.Visibility = Visibility.Collapsed;
                            ShowArrivedStatus(arrivalPrediction);
                        });
                        return;
                    }

                    // 방위각 → 숫자패드
                    var bearing = NavigationCalculator.BearingDegrees(curLat, curLon, destLat, destLon);
                    var numpad = GameWindowHelper.BearingToNumpad(bearing);
                    GameWindowHelper.SendNumpadKey(navHWnd, numpad);

                    var dirLabel = numpad switch
                    {
                        8 => "N", 9 => "NE", 6 => "E", 3 => "SE",
                        2 => "S", 1 => "SW", 4 => "W", 7 => "NW",
                        _ => "?"
                    };

                    Dispatcher.Invoke(() => SetNavStatus($"🎯 자동이동 중 - {coordLabel} → {dirLabel}"));

                    await Task.Delay(500, token);
                }
                catch (OperationCanceledException) { break; }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[WorldMap] Nav error: {ex.Message}");
                    await Task.Delay(2000, token);
                }
            }
        });
    }

    private void StopNavigation()
    {
        var wasNavigating = _isNavigating;
        _navCts?.Cancel();
        _navCts = null;
        _isNavigating = false;

        var hWnd = GameWindowHelper.FindGameWindow();
        if (hWnd != IntPtr.Zero)
            GameWindowHelper.SendNumpadKey(hWnd, 5);

        for (int i = 0; i < 3; i++)
            if (_destMarkers[i] != null) _destMarkers[i]!.Visibility = Visibility.Collapsed;

        if (_btnStopNav != null) _btnStopNav.Visibility = Visibility.Collapsed;
        // 사용자가 수동으로 중지했을 때만 메시지 변경 (도착 상태 유지 X)
        if (wasNavigating)
        {
            _arrivalClearTimer?.Stop();
            _arrivalClearTimer = null;
            SetNavStatus("⏹ 자동이동 중지됨");
        }
    }

    #endregion

    #region 탐험 떠나기

    /// <summary>
    /// 도시에서 성문 → 탐험을 떠난다 → YES 순서로 키를 전송한다.
    /// 건물 이름을 OCR로 인식하여 "성문"을 찾을 때까지 아래 키를 누른다.
    /// </summary>
    private async Task LeaveCityAsync(IntPtr hWnd, CancellationToken token = default)
    {
        GameWindowHelper.BringToFront(hWnd);
        await Task.Delay(300, token);

        var debugDir = Path.Combine(Path.GetTempPath(), "cds_leave_city");
        Directory.CreateDirectory(debugDir);

        // 건물 목록은 순환 구조이므로 Down 키로 한 바퀴 돌며 "성문"을 찾음
        bool foundGate = false;
        for (int i = 0; i < 15; i++)
        {
            using var bmp = GameWindowHelper.CaptureClient(hWnd);
            if (bmp != null)
            {
                int regionY = Math.Min(20, bmp.Height - 1);
                int regionH = Math.Max(bmp.Height * 70 / 100, 1);
                var region = new System.Drawing.Rectangle(0, regionY, bmp.Width, regionH);

                // 디버그 이미지 저장
                try
                {
                    var debugPath = Path.Combine(debugDir, $"gate_{i}.png");
                    using var cropped = bmp.Clone(region, bmp.PixelFormat);
                    cropped.Save(debugPath);
                    System.Diagnostics.Debug.WriteLine($"[LeaveCity] Debug image: {debugPath}");
                }
                catch { /* ignore */ }

                var text = await _coordinateOcr.RecognizeRegionAsync(bmp, region);
                System.Diagnostics.Debug.WriteLine($"[LeaveCity] OCR({i}): '{text}'");

                if (text != null && text.Contains("성문"))
                {
                    foundGate = true;
                    break;
                }
            }

            GameWindowHelper.SendDownKey(hWnd);
            await Task.Delay(500, token);
        }

        if (!foundGate)
        {
            System.Diagnostics.Debug.WriteLine("[LeaveCity] 성문을 찾지 못함");
            return;
        }

        // 2. 성문 선택
        await Task.Delay(200, token);
        GameWindowHelper.SendEnterKey(hWnd);
        await Task.Delay(500, token);

        // 3. "탐험을 떠난다" (첫 번째 옵션)
        GameWindowHelper.SendEnterKey(hWnd);
        await Task.Delay(500, token);

        // 4. "탐험을 떠납니까?" → YES
        GameWindowHelper.SendEnterKey(hWnd);
        await Task.Delay(300, token);
    }

    private async void LeaveCityForExploration()
    {
        var hWnd = GameWindowHelper.FindGameWindow();
        if (hWnd == IntPtr.Zero)
        {
            MessageBox.Show("게임 창을 찾을 수 없습니다.", "알림",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (_btnLeaveCity != null) _btnLeaveCity.IsEnabled = false;
        if (_txtStatus != null) _txtStatus.Text = "도시에서 탐험 떠나는 중...";

        try
        {
            await LeaveCityAsync(hWnd);
            if (_txtStatus != null) _txtStatus.Text = "탐험 출발 완료";
        }
        catch (Exception ex)
        {
            if (_txtStatus != null) _txtStatus.Text = $"탐험 떠나기 실패: {ex.Message}";
        }
        finally
        {
            if (_btnLeaveCity != null) _btnLeaveCity.IsEnabled = true;
        }
    }

    #endregion
}
