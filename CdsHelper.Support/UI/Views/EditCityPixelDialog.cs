using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using CdsHelper.Support.Local.Helpers;

namespace CdsHelper.Support.UI.Views;

[TemplatePart(Name = PART_CityNameTextBox, Type = typeof(TextBox))]
[TemplatePart(Name = PART_PixelXTextBox, Type = typeof(TextBox))]
[TemplatePart(Name = PART_PixelYTextBox, Type = typeof(TextBox))]
[TemplatePart(Name = PART_HasLibraryCheckBox, Type = typeof(CheckBox))]
[TemplatePart(Name = PART_LatitudeTextBox, Type = typeof(TextBox))]
[TemplatePart(Name = PART_LongitudeTextBox, Type = typeof(TextBox))]
[TemplatePart(Name = PART_CulturalSphereTextBox, Type = typeof(TextBox))]
[TemplatePart(Name = PART_MapScrollViewer, Type = typeof(ScrollViewer))]
[TemplatePart(Name = PART_MapImage, Type = typeof(Image))]
[TemplatePart(Name = PART_MapCanvas, Type = typeof(Canvas))]
[TemplatePart(Name = PART_MapMessage, Type = typeof(TextBlock))]
[TemplatePart(Name = PART_OkButton, Type = typeof(Button))]
[TemplatePart(Name = PART_CancelButton, Type = typeof(Button))]
public class EditCityPixelDialog : Window
{
    private const string PART_CityNameTextBox = "PART_CityNameTextBox";
    private const string PART_PixelXTextBox = "PART_PixelXTextBox";
    private const string PART_PixelYTextBox = "PART_PixelYTextBox";
    private const string PART_HasLibraryCheckBox = "PART_HasLibraryCheckBox";
    private const string PART_LatitudeTextBox = "PART_LatitudeTextBox";
    private const string PART_LongitudeTextBox = "PART_LongitudeTextBox";
    private const string PART_CulturalSphereTextBox = "PART_CulturalSphereTextBox";
    private const string PART_MapScrollViewer = "PART_MapScrollViewer";
    private const string PART_MapImage = "PART_MapImage";
    private const string PART_MapCanvas = "PART_MapCanvas";
    private const string PART_MapMessage = "PART_MapMessage";
    private const string PART_OkButton = "PART_OkButton";
    private const string PART_CancelButton = "PART_CancelButton";

    private TextBox? _cityNameTextBox;
    private TextBox? _pixelXTextBox;
    private TextBox? _pixelYTextBox;
    private CheckBox? _hasLibraryCheckBox;
    private TextBox? _latitudeTextBox;
    private TextBox? _longitudeTextBox;
    private TextBox? _culturalSphereTextBox;
    private ScrollViewer? _mapScrollViewer;
    private Image? _mapImage;
    private Canvas? _mapCanvas;
    private TextBlock? _mapMessage;
    private Ellipse? _currentMarker;
    private bool _mapInitStarted;

    public static readonly DependencyProperty CityNameProperty =
        DependencyProperty.Register(nameof(CityName), typeof(string), typeof(EditCityPixelDialog),
            new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty PixelXProperty =
        DependencyProperty.Register(nameof(PixelX), typeof(int?), typeof(EditCityPixelDialog),
            new PropertyMetadata(null));

    public static readonly DependencyProperty PixelYProperty =
        DependencyProperty.Register(nameof(PixelY), typeof(int?), typeof(EditCityPixelDialog),
            new PropertyMetadata(null));

    public static readonly DependencyProperty HasLibraryProperty =
        DependencyProperty.Register(nameof(HasLibrary), typeof(bool), typeof(EditCityPixelDialog),
            new PropertyMetadata(false));

    public static readonly DependencyProperty LatitudeProperty =
        DependencyProperty.Register(nameof(Latitude), typeof(int?), typeof(EditCityPixelDialog),
            new PropertyMetadata(null));

    public static readonly DependencyProperty LongitudeProperty =
        DependencyProperty.Register(nameof(Longitude), typeof(int?), typeof(EditCityPixelDialog),
            new PropertyMetadata(null));

    public static readonly DependencyProperty CulturalSphereProperty =
        DependencyProperty.Register(nameof(CulturalSphere), typeof(string), typeof(EditCityPixelDialog),
            new PropertyMetadata(null));

    public string CityName
    {
        get => (string)GetValue(CityNameProperty);
        set => SetValue(CityNameProperty, value);
    }

    public int? PixelX
    {
        get => (int?)GetValue(PixelXProperty);
        set => SetValue(PixelXProperty, value);
    }

    public int? PixelY
    {
        get => (int?)GetValue(PixelYProperty);
        set => SetValue(PixelYProperty, value);
    }

    public bool HasLibrary
    {
        get => (bool)GetValue(HasLibraryProperty);
        set => SetValue(HasLibraryProperty, value);
    }

    public int? Latitude
    {
        get => (int?)GetValue(LatitudeProperty);
        set => SetValue(LatitudeProperty, value);
    }

    public int? Longitude
    {
        get => (int?)GetValue(LongitudeProperty);
        set => SetValue(LongitudeProperty, value);
    }

    public string? CulturalSphere
    {
        get => (string?)GetValue(CulturalSphereProperty);
        set => SetValue(CulturalSphereProperty, value);
    }

    static EditCityPixelDialog()
    {
        DefaultStyleKeyProperty.OverrideMetadata(
            typeof(EditCityPixelDialog),
            new FrameworkPropertyMetadata(typeof(EditCityPixelDialog)));
    }

    public EditCityPixelDialog(string cityName, int? currentX, int? currentY, bool hasLibrary = false, int? latitude = null, int? longitude = null, string? culturalSphere = null)
    {
        Title = "도시 정보 수정";
        Width = 800;
        Height = 580;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.CanResize;

        CityName = cityName;
        PixelX = currentX;
        PixelY = currentY;
        HasLibrary = hasLibrary;
        Latitude = latitude;
        Longitude = longitude;
        CulturalSphere = culturalSphere;
    }

    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate();

        _cityNameTextBox = GetTemplateChild(PART_CityNameTextBox) as TextBox;
        _pixelXTextBox = GetTemplateChild(PART_PixelXTextBox) as TextBox;
        _pixelYTextBox = GetTemplateChild(PART_PixelYTextBox) as TextBox;
        _hasLibraryCheckBox = GetTemplateChild(PART_HasLibraryCheckBox) as CheckBox;
        _latitudeTextBox = GetTemplateChild(PART_LatitudeTextBox) as TextBox;
        _longitudeTextBox = GetTemplateChild(PART_LongitudeTextBox) as TextBox;
        _culturalSphereTextBox = GetTemplateChild(PART_CulturalSphereTextBox) as TextBox;
        _mapScrollViewer = GetTemplateChild(PART_MapScrollViewer) as ScrollViewer;
        _mapImage = GetTemplateChild(PART_MapImage) as Image;
        _mapCanvas = GetTemplateChild(PART_MapCanvas) as Canvas;
        _mapMessage = GetTemplateChild(PART_MapMessage) as TextBlock;

        if (GetTemplateChild(PART_OkButton) is Button okButton)
            okButton.Click += OnOkClick;

        if (GetTemplateChild(PART_CancelButton) is Button cancelButton)
            cancelButton.Click += OnCancelClick;

        if (_mapImage != null)
            _mapImage.MouseLeftButtonDown += OnMapClick;

        if (_cityNameTextBox != null)
            _cityNameTextBox.Text = CityName;

        if (_pixelXTextBox != null)
            _pixelXTextBox.Text = PixelX?.ToString() ?? "";

        if (_pixelYTextBox != null)
            _pixelYTextBox.Text = PixelY?.ToString() ?? "";

        if (_hasLibraryCheckBox != null)
            _hasLibraryCheckBox.IsChecked = HasLibrary;

        if (_latitudeTextBox != null)
            _latitudeTextBox.Text = Latitude?.ToString() ?? "";

        if (_longitudeTextBox != null)
            _longitudeTextBox.Text = Longitude?.ToString() ?? "";

        if (_culturalSphereTextBox != null)
            _culturalSphereTextBox.Text = CulturalSphere ?? "";

        // OnApplyTemplate은 레이아웃 패스(Dispatcher 처리가 중단된 구간) 안에서 호출된다.
        // 이 안에서 MessageBox/ShowDialog 같은 모달 UI를 띄우거나 메시지 펌핑이 일어나면
        // "디스패처 처리는 일시 중단되었지만 메시지가 여전히 처리되고 있습니다" 예외가 난다.
        // 지도 로딩(파일 없으면 다운로드)과 포커스 이동은 레이아웃이 끝난 뒤로 미룬다.
        if (!_mapInitStarted)
        {
            _mapInitStarted = true;
            Dispatcher.BeginInvoke(new Action(async () =>
            {
                _cityNameTextBox?.Focus();
                _cityNameTextBox?.SelectAll();
                await InitMapAsync();
            }), System.Windows.Threading.DispatcherPriority.Loaded);
        }
    }

    private async Task InitMapAsync()
    {
        if (_mapImage == null || _mapCanvas == null) return;

        var mapPath = MapImageAsset.FilePath;

        if (!File.Exists(mapPath))
        {
            SetMapMessage("지도 이미지를 내려받는 중입니다...");
            if (!await MapImageAsset.TryDownloadAsync())
            {
                SetMapMessage($"지도 이미지를 불러올 수 없습니다.\n" +
                              $"실행 폴더에 {MapImageAsset.FileName} 파일을 넣어주세요.\n" +
                              $"(좌표는 위/경도 입력란으로 직접 수정할 수 있습니다)");
                return;
            }
        }

        try
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.UriSource = new Uri(mapPath, UriKind.Absolute);
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.EndInit();

            _mapImage.Source = bitmap;
            _mapImage.Width = bitmap.PixelWidth;
            _mapImage.Height = bitmap.PixelHeight;

            _mapCanvas.Width = bitmap.PixelWidth;
            _mapCanvas.Height = bitmap.PixelHeight;
        }
        catch (Exception ex)
        {
            SetMapMessage($"지도 이미지 로드 실패: {ex.Message}");
            return;
        }

        SetMapMessage(null);

        if (PixelX.HasValue && PixelY.HasValue)
        {
            AddMarkerAt(PixelX.Value, PixelY.Value);
            ScrollToPosition(PixelX.Value, PixelY.Value);
        }
    }

    /// <summary>지도 영역에 안내 문구를 표시한다. null이면 감춘다.</summary>
    private void SetMapMessage(string? message)
    {
        if (_mapMessage == null) return;
        _mapMessage.Text = message ?? string.Empty;
        _mapMessage.Visibility = string.IsNullOrEmpty(message) ? Visibility.Collapsed : Visibility.Visible;
    }

    private void ScrollToPosition(int x, int y)
    {
        if (_mapScrollViewer == null) return;

        Dispatcher.BeginInvoke(new Action(() =>
        {
            var viewportWidth = _mapScrollViewer.ViewportWidth;
            var viewportHeight = _mapScrollViewer.ViewportHeight;

            var scrollX = Math.Max(0, x - viewportWidth / 2);
            var scrollY = Math.Max(0, y - viewportHeight / 2);

            _mapScrollViewer.ScrollToHorizontalOffset(scrollX);
            _mapScrollViewer.ScrollToVerticalOffset(scrollY);
        }), System.Windows.Threading.DispatcherPriority.Loaded);
    }

    private void AddMarkerAt(int x, int y)
    {
        if (_mapCanvas == null) return;

        if (_currentMarker != null)
            _mapCanvas.Children.Remove(_currentMarker);

        const int markerSize = 12;
        _currentMarker = new Ellipse
        {
            Width = markerSize,
            Height = markerSize,
            Fill = Brushes.Red,
            Stroke = Brushes.White,
            StrokeThickness = 2
        };

        Canvas.SetLeft(_currentMarker, x - markerSize / 2);
        Canvas.SetTop(_currentMarker, y - markerSize / 2);
        _mapCanvas.Children.Add(_currentMarker);
    }

    private void OnMapClick(object sender, MouseButtonEventArgs e)
    {
        if (_mapImage == null) return;

        var position = e.GetPosition(_mapImage);
        var x = (int)position.X;
        var y = (int)position.Y;

        if (_pixelXTextBox != null)
            _pixelXTextBox.Text = x.ToString();

        if (_pixelYTextBox != null)
            _pixelYTextBox.Text = y.ToString();

        AddMarkerAt(x, y);
    }

    private void OnOkClick(object sender, RoutedEventArgs e)
    {
        CityName = _cityNameTextBox?.Text?.Trim() ?? CityName;
        PixelX = int.TryParse(_pixelXTextBox?.Text, out var x) ? x : null;
        PixelY = int.TryParse(_pixelYTextBox?.Text, out var y) ? y : null;
        HasLibrary = _hasLibraryCheckBox?.IsChecked ?? false;
        Latitude = int.TryParse(_latitudeTextBox?.Text, out var lat) ? lat : null;
        Longitude = int.TryParse(_longitudeTextBox?.Text, out var lon) ? lon : null;
        CulturalSphere = string.IsNullOrWhiteSpace(_culturalSphereTextBox?.Text) ? null : _culturalSphereTextBox.Text.Trim();
        DialogResult = true;
    }

    private void OnCancelClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}
