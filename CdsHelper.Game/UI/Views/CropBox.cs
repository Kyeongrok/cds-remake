using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CdsHelper.Game.Local.Helpers;

namespace CdsHelper.Game.UI.Views;

/// <summary>
/// 그림 한 장에서 정해진 크기의 네모를 떠 오는 칸 — 안을 <b>끌면</b> 그림이 움직이고 <b>굴리면</b> 크기가 바뀐다.
/// 「가득 채우기」(<see cref="PortraitImport.Fit.Cover"/>)는 네모를 채우고 넘치는 만큼 자르고, 「통째로 넣기」(<see cref="PortraitImport.Fit.Contain"/>)는
/// 그림 전체가 들어가게 줄인다.
/// </summary>
/// <remarks>
/// 초상화 넣기 창(<see cref="PortraitAddDialog"/>)의 끌기 · 굴리기 · 맞추기를 크기만 바꿔 떼어 낸 것이다 — 셈은 같은
/// <see cref="PortraitImport.Crop"/> · <see cref="PortraitImport.Shape(BitmapSource, PortraitImport.Crop, int, int, Brush, bool)"/> 를 쓴다.
/// 커스텀 발견물 창의 아이템 그림(120x120)이 쓴다.
/// </remarks>
public sealed class CropBox : Border
{
    /// <summary>한 번 굴리거나 누를 때 바뀌는 배수 — 초상화 넣기 창과 같다.</summary>
    public const double ZoomStep = 1.15;

    private readonly int _width, _height, _scale;
    private readonly Brush _fill;
    private readonly Image _view;
    private BitmapSource? _source;
    private PortraitImport.Crop _crop;
    private (Point At, PortraitImport.Crop From)? _drag;

    /// <summary>바뀌었을 때(새 그림 · 끌기 · 굴리기 · 맞추기 · 비우기).</summary>
    public event Action? Changed;

    /// <param name="width">떠 올 그림의 폭(점).</param>
    /// <param name="height">떠 올 그림의 높이(점).</param>
    /// <param name="scale">보일 때 키우는 배수.</param>
    /// <param name="fill">그림보다 네모가 클 때 남는 자리를 메울 색.</param>
    public CropBox(int width, int height, int scale, Brush fill)
    {
        (_width, _height, _scale, _fill) = (width, height, scale, fill);
        BorderBrush = Brushes.Gray;
        BorderThickness = new Thickness(1);
        // 투명한 자리가 보이게 바둑판을 깐다.
        Background = Checker();
        _view = new Image { Width = width * scale, Height = height * scale, Stretch = Stretch.Fill, Cursor = Cursors.SizeAll };
        RenderOptions.SetBitmapScalingMode(_view, BitmapScalingMode.NearestNeighbor);
        Child = _view;
        _view.MouseLeftButtonDown += (_, e) =>
        {
            if (_source == null) return;
            _drag = (e.GetPosition(_view), _crop);
            _view.CaptureMouse();
        };
        _view.MouseMove += (_, e) =>
        {
            if (_drag is not { } drag) return;
            // 보일 때 키운 만큼 되나눠야 그림 점이 된다.
            var now = e.GetPosition(_view);
            _crop = drag.From.Moved((now.X - drag.At.X) / _scale, (now.Y - drag.At.Y) / _scale);
            Reshape();
        };
        _view.MouseLeftButtonUp += (_, _) =>
        {
            _drag = null;
            _view.ReleaseMouseCapture();
        };
        _view.MouseWheel += (_, e) =>
        {
            if (_source == null) return;
            e.Handled = true;
            Zoom(e.Delta > 0 ? ZoomStep : 1 / ZoomStep);
        };
    }

    /// <summary>떠 온 그림(폭 x 높이 BGRA). 고른 그림이 없으면 null.</summary>
    public uint[]? Shaped { get; private set; }

    /// <summary>고른 그림이 있는지 — 있으면 끌고 굴릴 수 있다.</summary>
    public bool HasSource => _source != null;

    /// <summary>지금 배수(원본 점 하나가 몇 점이 되는지).</summary>
    public double ZoomLevel => _crop.Zoom;

    /// <summary>그림을 새로 고른다 — <paramref name="fit"/> 으로 첫 자리를 잡는다.</summary>
    public void Load(BitmapSource source, PortraitImport.Fit fit = PortraitImport.Fit.Contain)
    {
        _source = source;
        Fit(fit);
    }

    /// <summary>그 결로 자리를 처음부터 다시 잡는다.</summary>
    public void Fit(PortraitImport.Fit fit)
    {
        if (_source is not { } source) return;
        _crop = PortraitImport.Crop.For(source.PixelWidth, source.PixelHeight, fit, _width, _height);
        Reshape();
    }

    /// <summary>가운데는 그대로 두고 크기만 <paramref name="by"/> 배로 한다.</summary>
    public void Zoom(double by)
    {
        if (_source == null) return;
        _crop = _crop with { Zoom = Math.Clamp(_crop.Zoom * by, PortraitImport.MinZoom, PortraitImport.MaxZoom) };
        Reshape();
    }

    /// <summary>고른 그림 없이 이미 있는 그림(폭 x 높이 BGRA, 없으면 null)을 보이기만 한다 — 끌 수 없다.</summary>
    public void ShowFixed(uint[]? bgra)
    {
        _source = null;
        _drag = null;
        Shaped = null;
        _view.Source = bgra is { } px && px.Length == _width * _height ? Bitmap(px) : null;
        Changed?.Invoke();
    }

    private void Reshape()
    {
        if (_source is not { } source) return;
        Shaped = PortraitImport.Shape(source, _crop, _width, _height, _fill, smooth: true);
        _view.Source = Bitmap(Shaped);
        Changed?.Invoke();
    }

    private BitmapSource Bitmap(uint[] bgra)
    {
        var made = BitmapSource.Create(_width, _height, 96, 96, PixelFormats.Bgra32, null, bgra, _width * 4);
        made.Freeze();
        return made;
    }

    private static DrawingBrush Checker()
    {
        var light = new GeometryDrawing(Brushes.White, null, new RectangleGeometry(new Rect(0, 0, 16, 16)));
        var dark = new GeometryDrawing(new SolidColorBrush(Color.FromRgb(0xE4, 0xE4, 0xE4)), null,
            new GeometryGroup { Children = { new RectangleGeometry(new Rect(0, 0, 8, 8)), new RectangleGeometry(new Rect(8, 8, 8, 8)) } });
        var brush = new DrawingBrush(new DrawingGroup { Children = { light, dark } })
        {
            TileMode = TileMode.Tile, Viewport = new Rect(0, 0, 16, 16), ViewportUnits = BrushMappingMode.Absolute,
        };
        brush.Freeze();
        return brush;
    }
}
