using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace CdsHelper.Support.UI.Units;

public class AreaMarker : Control, INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    public static readonly DependencyProperty MinXProperty =
        DependencyProperty.Register(nameof(MinX), typeof(double), typeof(AreaMarker),
            new PropertyMetadata(0.0, OnBoundsChanged));

    public static readonly DependencyProperty MaxXProperty =
        DependencyProperty.Register(nameof(MaxX), typeof(double), typeof(AreaMarker),
            new PropertyMetadata(0.0, OnBoundsChanged));

    public static readonly DependencyProperty MinYProperty =
        DependencyProperty.Register(nameof(MinY), typeof(double), typeof(AreaMarker),
            new PropertyMetadata(0.0, OnBoundsChanged));

    public static readonly DependencyProperty MaxYProperty =
        DependencyProperty.Register(nameof(MaxY), typeof(double), typeof(AreaMarker),
            new PropertyMetadata(0.0, OnBoundsChanged));

    public static readonly DependencyProperty AreaNameProperty =
        DependencyProperty.Register(nameof(AreaName), typeof(string), typeof(AreaMarker),
            new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty AreaColorProperty =
        DependencyProperty.Register(nameof(AreaColor), typeof(Color), typeof(AreaMarker),
            new PropertyMetadata(Colors.Red, OnColorChanged));

    public static readonly DependencyProperty ShowLabelProperty =
        DependencyProperty.Register(nameof(ShowLabel), typeof(bool), typeof(AreaMarker),
            new PropertyMetadata(true));

    public static readonly DependencyProperty FillBrushProperty =
        DependencyProperty.Register(nameof(FillBrush), typeof(Brush), typeof(AreaMarker),
            new PropertyMetadata(null));

    public static readonly DependencyProperty StrokeBrushProperty =
        DependencyProperty.Register(nameof(StrokeBrush), typeof(Brush), typeof(AreaMarker),
            new PropertyMetadata(Brushes.Red));

    public double MinX
    {
        get => (double)GetValue(MinXProperty);
        set => SetValue(MinXProperty, value);
    }

    public double MaxX
    {
        get => (double)GetValue(MaxXProperty);
        set => SetValue(MaxXProperty, value);
    }

    public double MinY
    {
        get => (double)GetValue(MinYProperty);
        set => SetValue(MinYProperty, value);
    }

    public double MaxY
    {
        get => (double)GetValue(MaxYProperty);
        set => SetValue(MaxYProperty, value);
    }

    public string AreaName
    {
        get => (string)GetValue(AreaNameProperty);
        set => SetValue(AreaNameProperty, value);
    }

    public Color AreaColor
    {
        get => (Color)GetValue(AreaColorProperty);
        set => SetValue(AreaColorProperty, value);
    }

    public bool ShowLabel
    {
        get => (bool)GetValue(ShowLabelProperty);
        set => SetValue(ShowLabelProperty, value);
    }

    public Brush FillBrush
    {
        get => (Brush)GetValue(FillBrushProperty);
        set => SetValue(FillBrushProperty, value);
    }

    public Brush StrokeBrush
    {
        get => (Brush)GetValue(StrokeBrushProperty);
        set => SetValue(StrokeBrushProperty, value);
    }

    public double AreaWidth => Math.Max(0, MaxX - MinX);
    public double AreaHeight => Math.Max(0, MaxY - MinY);

    static AreaMarker()
    {
        DefaultStyleKeyProperty.OverrideMetadata(
            typeof(AreaMarker),
            new FrameworkPropertyMetadata(typeof(AreaMarker)));
    }

    public AreaMarker()
    {
        InitializeBrushes();
    }

    public AreaMarker(double minX, double minY, double maxX, double maxY, string areaName, Color color)
    {
        MinX = minX;
        MinY = minY;
        MaxX = maxX;
        MaxY = maxY;
        AreaName = areaName;
        AreaColor = color;

        InitializeBrushes();
        UpdateBounds();
    }

    private void InitializeBrushes()
    {
        var color = AreaColor;
        FillBrush = new SolidColorBrush(Color.FromArgb(50, color.R, color.G, color.B));
        StrokeBrush = new SolidColorBrush(color);
    }

    private void UpdateBounds()
    {
        Width = AreaWidth;
        Height = AreaHeight;
        Canvas.SetLeft(this, MinX);
        Canvas.SetTop(this, MinY);
    }

    private static void OnBoundsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is AreaMarker marker)
        {
            marker.OnPropertyChanged(nameof(AreaWidth));
            marker.OnPropertyChanged(nameof(AreaHeight));
            marker.UpdateBounds();
        }
    }

    private static void OnColorChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is AreaMarker marker && e.NewValue is Color color)
        {
            marker.FillBrush = new SolidColorBrush(Color.FromArgb(50, color.R, color.G, color.B));
            marker.StrokeBrush = new SolidColorBrush(color);
        }
    }
}
