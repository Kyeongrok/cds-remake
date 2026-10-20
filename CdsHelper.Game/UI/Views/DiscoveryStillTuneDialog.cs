using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CdsHelper.Game.Local.Helpers;
using CdsHelper.Support.UI.Units;
using Microsoft.Win32;

namespace CdsHelper.Game.UI.Views;

/// <summary>
/// 그림 한 장을 <b>세기를 고르며</b> 발견물 그림(320x240)으로 바꿔 넣는 창 — 개발도구 「발견물 그림」의 「그림 다듬어 넣기」.
/// </summary>
/// <remarks>
/// 「그림 더하기」는 정해 둔 세기(<see cref="DiscoveryStillFilter.Options.Default"/>)로만 바꿔서, 사진에 따라 붓결이 세어 뭉개졌다
/// (카바 신전, 요청). 여기서는 붓결 · 번짐 · 밝기 굽이 · 본디 빛깔 · 테두리 · 색 단계와 자르기를 막대로 고르고, 바뀐 모양을
/// 곧바로 두 배로 본다. 미리 보기를 끌면 자를 자리가 움직이고 굴리면 당겨진다. 넣는 번호를 이미 있는 번호로 두면 그 그림을 바꾼다.
/// </remarks>
public sealed class DiscoveryStillTuneDialog : GameWindow
{
    private const int Scale = 2;

    private readonly Image _preview = new()
    {
        Width = DiscoveryStillFilter.Width * Scale,
        Height = DiscoveryStillFilter.Height * Scale,
        Stretch = Stretch.Fill,
        Cursor = Cursors.SizeAll,
    };

    private readonly TextBlock _picked = new() { Foreground = Brushes.DimGray, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _status = new() { Margin = new Thickness(0, 8, 0, 0), TextWrapping = TextWrapping.Wrap, MaxWidth = 900 };
    private readonly NumericSpinner _number = new() { Minimum = 0, Maximum = 9999, Step = 1, DecimalPlaces = 0, Width = 90 };

    private BitmapSource? _source;
    private DiscoveryStillFilter.Options _options = DiscoveryStillFilter.Options.Default;
    private uint[]? _made;
    private (Point At, double X, double Y)? _drag;
    private readonly List<Action> _syncSliders = [];
    private bool _syncing;

    /// <summary>넣은 번호. 안 넣었으면 −1.</summary>
    public int Saved { get; private set; } = -1;

    private DiscoveryStillTuneDialog(int suggested)
    {
        Title = "발견물 그림 다듬기";
        SizeToContent = SizeToContent.WidthAndHeight;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        _number.Value = suggested;

        RenderOptions.SetBitmapScalingMode(_preview, BitmapScalingMode.NearestNeighbor);
        _preview.MouseLeftButtonDown += (_, e) => { _drag = (e.GetPosition(_preview), _options.CenterX, _options.CenterY); _preview.CaptureMouse(); };
        _preview.MouseLeftButtonUp += (_, _) => { _drag = null; _preview.ReleaseMouseCapture(); };
        _preview.MouseMove += (_, e) => Pan(e.GetPosition(_preview));
        _preview.MouseWheel += (_, e) => Change(_options with { Zoom = Math.Clamp(_options.Zoom * (e.Delta > 0 ? 1.1 : 1 / 1.1), 1, 6) });

        var open = new Button { Content = "그림 고르기…", Padding = new Thickness(12, 3, 12, 3) };
        open.Click += (_, _) => Choose();

        var sliders = new StackPanel { Width = 330, Margin = new Thickness(14, 0, 0, 0) };
        sliders.Children.Add(Slider("붓결 세기", 0, 100, 1, o => o.Strokes * 100, (o, v) => o with { Strokes = v / 100 }, "%"));
        sliders.Children.Add(Slider("붓결 길이", 4, 48, 1, o => o.StrokeLength, (o, v) => o with { StrokeLength = (int)v }, "칸"));
        sliders.Children.Add(Slider("번짐", 0, 6, 1, o => o.Smear, (o, v) => o with { Smear = (int)v }, "칸"));
        sliders.Children.Add(Slider("밝기 굽이", 30, 150, 1, o => o.Gamma * 100, (o, v) => o with { Gamma = v / 100 }, "%"));
        sliders.Children.Add(Slider("본디 빛깔", 0, 100, 1, o => o.ColorKeep * 100, (o, v) => o with { ColorKeep = v / 100 }, "%"));
        sliders.Children.Add(Slider("테두리", 0, 40, 1, o => o.Fade, (o, v) => o with { Fade = v }, "칸"));
        sliders.Children.Add(Slider("붉은 끝", 0, 12, 1, o => o.Rim, (o, v) => o with { Rim = v }, "칸"));
        sliders.Children.Add(Slider("색 단계", 1, 32, 1, o => o.Steps, (o, v) => o with { Steps = (int)v }, ""));
        sliders.Children.Add(Slider("당기기", 100, 600, 5, o => o.Zoom * 100, (o, v) => o with { Zoom = v / 100 }, "%"));
        var reset = new Button { Content = "처음 값", Padding = new Thickness(12, 3, 12, 3), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 8, 0, 0) };
        reset.Click += (_, _) => Change(DiscoveryStillFilter.Options.Default);
        sliders.Children.Add(reset);
        sliders.Children.Add(new TextBlock
        {
            Text = "미리 보기를 끌면 자를 자리가 움직이고, 굴리면 당겨집니다. 처음 값이 「그림 더하기」와 같은 세기입니다.",
            Foreground = Brushes.DimGray, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0),
        });

        var put = new Button { Content = "넣는다", Padding = new Thickness(16, 3, 16, 3), Margin = new Thickness(0, 0, 8, 0) };
        put.Click += (_, _) => Put();
        var close = new Button { Content = "닫기", Padding = new Thickness(16, 3, 16, 3) };
        close.Click += (_, _) => Close();

        var top = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 10), Children = { open, _picked } };
        var middle = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Children = { new Border { BorderBrush = Brushes.Gray, BorderThickness = new Thickness(1), Child = _preview, VerticalAlignment = VerticalAlignment.Top }, sliders },
        };
        var bottom = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 12, 0, 0),
            Children =
            {
                new TextBlock { Text = "넣을 번호  D", VerticalAlignment = VerticalAlignment.Center },
                _number,
                new TextBlock { Text = ".png   (이미 있는 번호면 그 그림을 바꿉니다)", VerticalAlignment = VerticalAlignment.Center, Foreground = Brushes.DimGray, Margin = new Thickness(4, 0, 16, 0) },
                put, close,
            },
        };

        Content = new StackPanel { Margin = new Thickness(14), Children = { top, middle, bottom, _status } };
        Loaded += (_, _) => Choose();
    }

    /// <summary>막대 한 줄 — 이름 · 막대 · 값. 막대를 옮기면 그 값으로 다시 굽는다.</summary>
    private UIElement Slider(string label, double min, double max, double step,
                             Func<DiscoveryStillFilter.Options, double> get,
                             Func<DiscoveryStillFilter.Options, double, DiscoveryStillFilter.Options> set, string unit)
    {
        var value = new TextBlock { Width = 52, VerticalAlignment = VerticalAlignment.Center, TextAlignment = TextAlignment.Right };
        var slider = new Slider
        {
            Minimum = min, Maximum = max, TickFrequency = step, IsSnapToTickEnabled = true,
            Width = 200, VerticalAlignment = VerticalAlignment.Center, Value = get(_options),
        };
        void Show() => value.Text = $"{slider.Value:0}{unit}";
        slider.ValueChanged += (_, _) =>
        {
            Show();
            if (!_syncing) Change(set(_options, slider.Value));
        };
        _syncSliders.Add(() => { slider.Value = get(_options); Show(); });
        Show();
        return new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 3, 0, 3),
            Children = { new TextBlock { Text = label, Width = 70, VerticalAlignment = VerticalAlignment.Center }, slider, value },
        };
    }

    /// <summary>세기를 바꾸고 막대를 맞춘 뒤 다시 굽는다.</summary>
    private void Change(DiscoveryStillFilter.Options next)
    {
        _options = next;
        _syncing = true;
        foreach (var sync in _syncSliders) sync();
        _syncing = false;
        Render();
    }

    /// <summary>끈 만큼 자를 자리를 민다 — 당겨 볼수록 조금 움직인다.</summary>
    private void Pan(Point now)
    {
        if (_drag is not { } d || _source == null) return;
        double sx = (now.X - d.At.X) / _preview.Width / _options.Zoom;
        double sy = (now.Y - d.At.Y) / _preview.Height / _options.Zoom;
        // 그림 폭에 대한 몫으로 — 4:3 자르기가 그림 폭(또는 높이)을 다 쓰는 쪽에 맞춘다.
        double w = _source.PixelWidth, h = _source.PixelHeight;
        double cropW = Math.Min(w, h * 4 / 3.0), cropH = Math.Min(h, w * 3 / 4.0);
        Change(_options with
        {
            CenterX = Math.Clamp(d.X - sx * cropW / w, 0, 1),
            CenterY = Math.Clamp(d.Y - sy * cropH / h, 0, 1),
        });
    }

    private void Choose()
    {
        var box = new OpenFileDialog { Title = "발견물 그림으로 바꿀 그림", Filter = DiscoveryStillFiles.OpenFilter };
        if (box.ShowDialog(this) != true) return;
        _source = PortraitImport.Load(box.FileName);
        if (_source == null)
        {
            _status.Text = $"못 읽었습니다 — {PortraitImport.LastError}";
            _status.Foreground = Brushes.Firebrick;
            return;
        }
        _picked.Text = box.FileName;
        _status.Text = "";
        Change(_options with { CenterX = 0.5, CenterY = 0.5, Zoom = 1 });
    }

    private void Render()
    {
        if (_source == null) return;
        _made = DiscoveryStillFilter.Apply(_source, _options);
        var bmp = BitmapSource.Create(DiscoveryStillFilter.Width, DiscoveryStillFilter.Height, 96, 96,
                                      PixelFormats.Bgra32, null, _made, DiscoveryStillFilter.Width * 4);
        bmp.Freeze();
        _preview.Source = bmp;
    }

    private void Put()
    {
        if (_made == null) { _status.Text = "먼저 그림을 고르세요"; _status.Foreground = Brushes.Firebrick; return; }
        int n = (int)_number.Value;
        try
        {
            string path = DiscoveryStillFiles.Upload(_made, DiscoveryStillFilter.Width, DiscoveryStillFilter.Height, n);
            Saved = n;
            _status.Text = $"D{n}.png 로 넣었습니다 — {path}";
            _status.Foreground = Brushes.DimGray;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _status.Text = $"못 넣었습니다 — {ex.Message}";
            _status.Foreground = Brushes.Firebrick;
        }
    }

    /// <summary>창을 띄운다. 넣었으면 마지막으로 넣은 번호, 아니면 −1.</summary>
    /// <param name="suggested">처음 적어 둘 번호 — 보통 다음 빈 번호다.</param>
    public static int Show(Window? owner, int suggested)
    {
        var window = new DiscoveryStillTuneDialog(suggested);
        if (owner != null) window.Owner = owner;
        window.ShowDialog();
        return window.Saved;
    }
}
