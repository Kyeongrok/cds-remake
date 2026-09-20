using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using CdsHelper.Support.Local.Settings;

namespace CdsHelper.Support.UI.Units;

[TemplatePart(Name = PART_LibraryButton, Type = typeof(Button))]
[TemplatePart(Name = PART_MainBorder, Type = typeof(Border))]
public class CityMarker : Control, INotifyPropertyChanged
{
    private const string PART_LibraryButton = "PART_LibraryButton";
    private const string PART_MainBorder = "PART_MainBorder";

    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    public static readonly DependencyProperty XProperty =
        DependencyProperty.Register(nameof(X), typeof(double), typeof(CityMarker),
            new PropertyMetadata(0.0, OnPositionChanged));

    public static readonly DependencyProperty YProperty =
        DependencyProperty.Register(nameof(Y), typeof(double), typeof(CityMarker),
            new PropertyMetadata(0.0, OnPositionChanged));

    public static readonly DependencyProperty MarkerSizeProperty =
        DependencyProperty.Register(nameof(MarkerSize), typeof(double), typeof(CityMarker),
            new PropertyMetadata(AppSettings.DefaultMarkerSize, OnSizeChanged));

    public static readonly DependencyProperty CityNameProperty =
        DependencyProperty.Register(nameof(CityName), typeof(string), typeof(CityMarker),
            new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty ShowLabelProperty =
        DependencyProperty.Register(nameof(ShowLabel), typeof(bool), typeof(CityMarker),
            new PropertyMetadata(false, OnDisplayPropertyChanged));

    public static readonly DependencyProperty ShowCoordinatesProperty =
        DependencyProperty.Register(nameof(ShowCoordinates), typeof(bool), typeof(CityMarker),
            new PropertyMetadata(false, OnDisplayPropertyChanged));

    private static void OnDisplayPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is CityMarker marker)
        {
            marker.OnPropertyChanged(nameof(DisplayText));
            marker.OnPropertyChanged(nameof(IsLabelVisible));
        }
    }

    public static readonly DependencyProperty LatitudeProperty =
        DependencyProperty.Register(nameof(Latitude), typeof(int?), typeof(CityMarker),
            new PropertyMetadata(null));

    public static readonly DependencyProperty LongitudeProperty =
        DependencyProperty.Register(nameof(Longitude), typeof(int?), typeof(CityMarker),
            new PropertyMetadata(null));

    public static readonly DependencyProperty HasLibraryProperty =
        DependencyProperty.Register(nameof(HasLibrary), typeof(bool), typeof(CityMarker),
            new PropertyMetadata(false));

    public static readonly DependencyProperty HasShipyardProperty =
        DependencyProperty.Register(nameof(HasShipyard), typeof(bool), typeof(CityMarker),
            new PropertyMetadata(false));

    public static readonly DependencyProperty ShowShipyardMarkerProperty =
        DependencyProperty.Register(nameof(ShowShipyardMarker), typeof(bool), typeof(CityMarker),
            new PropertyMetadata(false));

    public static readonly DependencyProperty CityIdProperty =
        DependencyProperty.Register(nameof(CityId), typeof(byte), typeof(CityMarker),
            new PropertyMetadata((byte)0));

    // 도서관 클릭 이벤트
    public static readonly RoutedEvent LibraryClickedEvent =
        EventManager.RegisterRoutedEvent(nameof(LibraryClicked), RoutingStrategy.Bubble,
            typeof(RoutedEventHandler), typeof(CityMarker));

    public event RoutedEventHandler LibraryClicked
    {
        add => AddHandler(LibraryClickedEvent, value);
        remove => RemoveHandler(LibraryClickedEvent, value);
    }

    // 마커 클릭 이벤트
    public static readonly RoutedEvent MarkerClickedEvent =
        EventManager.RegisterRoutedEvent(nameof(MarkerClicked), RoutingStrategy.Bubble,
            typeof(RoutedEventHandler), typeof(CityMarker));

    public event RoutedEventHandler MarkerClicked
    {
        add => AddHandler(MarkerClickedEvent, value);
        remove => RemoveHandler(MarkerClickedEvent, value);
    }

    public double X
    {
        get => (double)GetValue(XProperty);
        set => SetValue(XProperty, value);
    }

    public double Y
    {
        get => (double)GetValue(YProperty);
        set => SetValue(YProperty, value);
    }

    public double MarkerSize
    {
        get => (double)GetValue(MarkerSizeProperty);
        set => SetValue(MarkerSizeProperty, value);
    }

    public string CityName
    {
        get => (string)GetValue(CityNameProperty);
        set => SetValue(CityNameProperty, value);
    }

    public bool ShowLabel
    {
        get => (bool)GetValue(ShowLabelProperty);
        set => SetValue(ShowLabelProperty, value);
    }

    public bool ShowCoordinates
    {
        get => (bool)GetValue(ShowCoordinatesProperty);
        set => SetValue(ShowCoordinatesProperty, value);
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

    public bool HasLibrary
    {
        get => (bool)GetValue(HasLibraryProperty);
        set => SetValue(HasLibraryProperty, value);
    }

    public bool HasShipyard
    {
        get => (bool)GetValue(HasShipyardProperty);
        set => SetValue(HasShipyardProperty, value);
    }

    public bool ShowShipyardMarker
    {
        get => (bool)GetValue(ShowShipyardMarkerProperty);
        set => SetValue(ShowShipyardMarkerProperty, value);
    }

    public byte CityId
    {
        get => (byte)GetValue(CityIdProperty);
        set => SetValue(CityIdProperty, value);
    }

    public string LatitudeDisplay
    {
        get
        {
            if (!Latitude.HasValue) return "-";
            var absLat = Math.Abs(Latitude.Value);
            var direction = Latitude.Value >= 0 ? "북위" : "남위";
            return $"{direction}{absLat}";
        }
    }

    public string LongitudeDisplay
    {
        get
        {
            if (!Longitude.HasValue) return "-";
            var absLon = Math.Abs(Longitude.Value);
            var direction = Longitude.Value >= 0 ? "동경" : "서경";
            return $"{direction}{absLon}";
        }
    }

    public string DisplayText
    {
        get
        {
            if (ShowLabel && ShowCoordinates)
                return $"{CityName} ({LatitudeDisplay}, {LongitudeDisplay})";
            if (ShowCoordinates)
                return $"{LatitudeDisplay}, {LongitudeDisplay}";
            if (ShowLabel)
                return CityName;
            return string.Empty;
        }
    }

    public bool IsLabelVisible => ShowLabel || ShowCoordinates;

    static CityMarker()
    {
        DefaultStyleKeyProperty.OverrideMetadata(
            typeof(CityMarker),
            new FrameworkPropertyMetadata(typeof(CityMarker)));
    }

    public CityMarker()
    {
    }

    public CityMarker(double x, double y, string cityName = "", int? latitude = null, int? longitude = null, bool hasLibrary = false, bool hasShipyard = false, byte cityId = 0, double size = AppSettings.DefaultMarkerSize)
    {
        X = x;
        Y = y;
        CityName = cityName;
        Latitude = latitude;
        Longitude = longitude;
        HasLibrary = hasLibrary;
        HasShipyard = hasShipyard;
        CityId = cityId;
        MarkerSize = size;
        Width = size;
        Height = size;

        Canvas.SetLeft(this, x - size / 2);
        Canvas.SetTop(this, y - size / 2);
    }

    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate();

        if (GetTemplateChild(PART_LibraryButton) is Button libraryButton)
        {
            libraryButton.Click += (s, e) =>
            {
                e.Handled = true;
                RaiseLibraryClicked();
            };
        }

        if (GetTemplateChild(PART_MainBorder) is Border mainBorder)
        {
            mainBorder.MouseLeftButtonDown += (s, e) =>
            {
                e.Handled = true;
                RaiseMarkerClicked();
            };
        }
    }

    public void RaiseLibraryClicked()
    {
        RaiseEvent(new RoutedEventArgs(LibraryClickedEvent, this));
    }

    public void RaiseMarkerClicked()
    {
        RaiseEvent(new RoutedEventArgs(MarkerClickedEvent, this));
    }

    private static void OnPositionChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is CityMarker marker)
        {
            Canvas.SetLeft(marker, marker.X - marker.MarkerSize / 2);
            Canvas.SetTop(marker, marker.Y - marker.MarkerSize / 2);
        }
    }

    private static void OnSizeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is CityMarker marker)
        {
            marker.Width = marker.MarkerSize;
            marker.Height = marker.MarkerSize;
            Canvas.SetLeft(marker, marker.X - marker.MarkerSize / 2);
            Canvas.SetTop(marker, marker.Y - marker.MarkerSize / 2);
        }
    }
}
