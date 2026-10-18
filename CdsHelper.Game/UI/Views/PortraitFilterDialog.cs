using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CdsHelper.Game.Local.Helpers;
using CdsHelper.Support.Local.Settings;
using CdsHelper.Support.UI.Units;

namespace CdsHelper.Game.UI.Views;

/// <summary>
/// 도구 앱 「초상화 필터」 — 사람이 그린 초상화를 원본 얼굴 화풍(80x96 · 공용 색표 · 디더링)으로 바꿔 본다.
/// </summary>
/// <remarks>
/// 원본 얼굴은 80x96 에 공용 색표 10~73번(64색, <see cref="GamePalette"/>)만 쓰고 두 색을 바둑판으로 섞어 중간색을 낸다.
/// 게임은 80x96 그림을 점을 살려 키워 걸므로, 같은 꼴로 만들어 <c>asset/face/male-90NN.png</c> 로 넣으면 원본 후원자들과
/// 같은 결로 보인다(메흐메트 2세, 건의). 자르기 · 밝기 · 대비 · 채도 · 색 · 디더링을 바꿔 가며 원본 얼굴과 나란히 본다.
/// 같은 변환의 명령줄 판은 <c>tools/portrait_to_game.py</c> 다.
/// </remarks>
public sealed class PortraitFilterDialog : GameWindow
{
    private const int W = Portraits.Width, H = Portraits.Height, Zoom = 4;

    private BitmapSource? _source;
    private byte[]? _result;   // W x H x BGRA

    private readonly Image _sourceView = new() { Width = 300, Height = 360, Stretch = Stretch.Uniform };
    private readonly Image _resultView = new() { Width = W * Zoom, Height = H * Zoom };

    // 바꾼 얼굴 확대 · 격자 — 점을 하나하나 찍으려면 크게 보고 칸 경계가 보여야 한다(건의).
    private static readonly int[] Zooms = [4, 6, 8, 12, 16];
    private int _zoom = Zoom;
    private readonly ComboBox _zoomPick = Pick("4배", "6배", "8배", "12배", "16배");
    private readonly CheckBox _gridOn = new() { Content = "격자 보기", Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
    private readonly System.Windows.Shapes.Rectangle _grid = new() { IsHitTestVisible = false, Visibility = Visibility.Collapsed };
    private readonly Image _referenceView = new() { Width = W * Zoom, Height = H * Zoom };
    private readonly Canvas _cropFrame = new() { Width = 300, Height = 360, IsHitTestVisible = false };
    private readonly System.Windows.Shapes.Rectangle _cropBox = new() { Stroke = Brushes.Yellow, StrokeThickness = 2 };

    private readonly Slider _cx = Bar(0, 1, 0.5), _cy = Bar(0, 1, 0.45), _size = Bar(0.1, 1, 0.6);
    private readonly Slider _bright = Bar(-60, 60, 0), _contrast = Bar(50, 160, 100), _saturation = Bar(0, 200, 100);
    private readonly ComboBox _colors = Pick("공용 색표 64색 (원본)", "공용 색표 + 검정 · 흰색", "색 제한 없음");
    private readonly ComboBox _dither = Pick("플로이드-스타인버그", "바둑판 2x2", "바둑판 4x4", "디더링 없음");
    private readonly ComboBox _scaling = Pick("곱게 줄이기", "가장 가까운 점");

    // 배경 그라데이션 — 원본 얼굴들은 뒤를 두 색 그라데이션으로 칠해 두었다(대개 짙은 밤색, 회녹색 · 회색 · 크림도 있다).
    private readonly CheckBox _bgOn = new() { Content = "배경을 그라데이션으로 바꾸기", Margin = new Thickness(0, 2, 0, 2) };
    // 허용치 기본 12 — 45 쯤이면 배경에 닿은 짙은 수염까지 번졌고, 12 가 가장 자연스러웠다(메흐메트 2세, 건의).
    private readonly Slider _bgTol = Bar(3, 100, 12);
    private readonly ComboBox _bgPreset = Pick("밤색(원본에 가장 흔함)", "회녹색", "회색", "크림", "직접 적기");
    private readonly ComboBox _bgDir = Pick("위 → 아래", "왼쪽 위 → 오른쪽 아래", "왼쪽 → 오른쪽", "가운데 → 가장자리");
    private readonly TextBox _bgFrom = new() { Text = "4A3528", Width = 70 };
    private readonly TextBox _bgTo = new() { Text = "1C150E", Width = 70 };
    private readonly CheckBox _bgSoft = new() { Content = "가장자리 부드럽게", IsChecked = true, Margin = new Thickness(0, 2, 0, 2) };

    /// <summary>배경 기준점(80x96 자리) — 이 점의 색에 가까운 것이 배경이다. 바뀐 얼굴을 누르면 옮긴다.</summary>
    private (int X, int Y) _bgPoint = (1, 1);

    // 배경 칠하기 — 저절로 가려내기가 잘 안 되는 그림(배경과 머리 · 수염이 같은 색)은 손으로 칠한다(건의).
    // 칠하는 동안은 칠한 점을 형광분홍으로 보이고, 끄면 칠한 곳을 그라데이션으로 바꾼다. 칠한 점이 있으면 저절로 가려내기 대신 쓴다.
    private readonly bool[] _painted = new bool[W * H];
    private readonly System.Windows.Controls.Primitives.ToggleButton _paint = new()
    {
        Content = "배경 칠하기", Margin = new Thickness(0, 4, 0, 0), Padding = new Thickness(8, 3, 8, 3),
    };
    private readonly ComboBox _brush = Pick("붓 1점", "붓 3x3", "붓 5x5");
    private static readonly Color Pink = Color.FromRgb(0xFF, 0x14, 0x93);

    /// <summary>배경 미리 정한 색(위쪽 · 아래쪽) — 원본 후원자 얼굴 귀퉁이에서 뽑은 값이다.</summary>
    private static readonly (string From, string To)[] BgPresets =
        [("4A3528", "1C150E"), ("646C58", "3C4030"), ("8B9091", "45403A"), ("EADFCC", "B8A48C")];
    // 번호는 숫자 돌리개로 — 위아래로 하나씩 넘겨 가며 원본 얼굴을 견줘 본다(건의).
    private readonly NumericSpinner _reference = new() { Minimum = 0, Maximum = 999, Step = 1, DecimalPlaces = 0, Value = 248, Width = 90 };
    private readonly NumericSpinner _faceNumber = new() { Minimum = Portraits.ShippedBase + 1, Maximum = 9999, Step = 1, DecimalPlaces = 0, Value = 9003, Width = 90 };
    private readonly TextBlock _status = new() { Margin = new Thickness(10, 4, 10, 8), TextWrapping = TextWrapping.Wrap };

    private Portraits? _faces;

    public PortraitFilterDialog()
    {
        Title = "초상화 필터";
        Width = 1100;
        Height = 760;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        _cropFrame.Children.Add(_cropBox);
        foreach (var s in new[] { _cx, _cy, _size, _bright, _contrast, _saturation }) s.ValueChanged += (_, _) => Redraw();
        foreach (var c in new[] { _colors, _dither, _scaling, _bgDir }) c.SelectionChanged += (_, _) => Redraw();
        _bgTol.ValueChanged += (_, _) => Redraw();
        _resultView.MouseLeftButtonDown += (_, e) =>
        {
            var at = e.GetPosition(_resultView);
            if (_paint.IsChecked == true) { _resultView.CaptureMouse(); Dab(at, true); return; }
            _bgPoint = (Math.Clamp((int)(at.X / _zoom), 0, W - 1), Math.Clamp((int)(at.Y / _zoom), 0, H - 1));
            _bgOn.IsChecked = true;
            Redraw();
        };
        _resultView.MouseRightButtonDown += (_, e) =>
        {
            if (_paint.IsChecked != true) return;
            _resultView.CaptureMouse();
            Dab(e.GetPosition(_resultView), false);
        };
        _resultView.MouseMove += (_, e) =>
        {
            if (_paint.IsChecked != true) return;
            if (e.LeftButton == System.Windows.Input.MouseButtonState.Pressed) Dab(e.GetPosition(_resultView), true);
            else if (e.RightButton == System.Windows.Input.MouseButtonState.Pressed) Dab(e.GetPosition(_resultView), false);
        };
        _resultView.MouseLeftButtonUp += (_, _) => _resultView.ReleaseMouseCapture();
        _resultView.MouseRightButtonUp += (_, _) => _resultView.ReleaseMouseCapture();
        _paint.Checked += (_, _) => { _bgOn.IsChecked = true; Redraw(); };
        _paint.Unchecked += (_, _) => Redraw();
        _bgOn.Checked += (_, _) => Redraw(); _bgOn.Unchecked += (_, _) => Redraw();
        _bgSoft.Checked += (_, _) => Redraw(); _bgSoft.Unchecked += (_, _) => Redraw();
        _bgFrom.TextChanged += (_, _) => Redraw(); _bgTo.TextChanged += (_, _) => Redraw();
        _bgPreset.SelectionChanged += (_, _) =>
        {
            if (_bgPreset.SelectedIndex is >= 0 and var i && i < BgPresets.Length)
            {
                _bgFrom.Text = BgPresets[i].From;
                _bgTo.Text = BgPresets[i].To;
            }
        };
        _reference.ValueChanged += (_, _) => ShowReference();

        var open = Button("그림 열기…", OpenImage);
        var export = Button("PNG 로 내보내기…", Export);
        var install = Button("앱 얼굴로 넣기", Install);

        var controls = new StackPanel { Width = 300, Margin = new Thickness(10) };
        controls.Children.Add(open);
        controls.Children.Add(Label("자르기 — 가운데 가로 · 세로, 크기(폭 5 : 높이 6)"));
        controls.Children.Add(Row("가로", _cx));
        controls.Children.Add(Row("세로", _cy));
        controls.Children.Add(Row("크기", _size));
        controls.Children.Add(Label("색 다듬기"));
        controls.Children.Add(Row("밝기", _bright));
        controls.Children.Add(Row("대비 %", _contrast));
        controls.Children.Add(Row("채도 %", _saturation));
        controls.Children.Add(Label("배경 — 기준점 색에 가깝고 테두리에 이어진 곳을 배경으로 친다. 바뀐 얼굴의 배경을 누르면 그 점이 기준이 된다(처음은 왼쪽 위)"));
        controls.Children.Add(_bgOn);
        controls.Children.Add(Row("허용치", _bgTol));
        controls.Children.Add(Row("색", _bgPreset));
        controls.Children.Add(Row("방향", _bgDir));
        controls.Children.Add(Row("시작색", _bgFrom));
        controls.Children.Add(Row("끝색", _bgTo));
        controls.Children.Add(_bgSoft);
        controls.Children.Add(Label("손으로 칠하기 — 켜고 바뀐 얼굴을 누르거나 끌면 형광분홍 점이 찍힌다(오른쪽 단추는 지우기). 끄면 칠한 곳이 그라데이션이 된다"));
        controls.Children.Add(_paint);
        controls.Children.Add(Row("붓", _brush));
        controls.Children.Add(Button("칠한 것 지우기", () => { Array.Clear(_painted); Redraw(); }));
        controls.Children.Add(Label("줄이기 · 색 · 디더링"));
        controls.Children.Add(Row("줄이기", _scaling));
        controls.Children.Add(Row("색", _colors));
        controls.Children.Add(Row("디더링", _dither));
        controls.Children.Add(Label("견줄 원본 얼굴(MALE.CDS 번호)"));
        controls.Children.Add(Row("번호", _reference));
        controls.Children.Add(Label("넣을 얼굴 번호(9000 부터 — asset/face/male-번호.png)"));
        controls.Children.Add(Row("번호", _faceNumber));
        controls.Children.Add(export);
        controls.Children.Add(install);

        var source = new Grid { Children = { _sourceView, _cropFrame } };
        var views = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 10, 0) };
        views.Children.Add(Captioned("원본 그림", source));
        // 바꾼 얼굴 — 배율 · 격자 줄을 머리에 달고, 크게 키우면 굴려 본다. Ctrl+휠로도 키우고 줄인다.
        var resultHead = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 0, 0, 4),
            Children = { new TextBlock { Text = "바꾼 얼굴(게임에서 보이는 결)", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) }, _zoomPick, _gridOn },
        };
        _zoomPick.Width = 70;
        var resultBox = new Grid { HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, Children = { _resultView, _grid } };
        var resultScroll = new ScrollViewer
        {
            Content = resultBox, MaxWidth = W * 8 + 20, MaxHeight = H * 7,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        };
        _zoomPick.SelectionChanged += (_, _) => SetZoom(Zooms[Math.Max(0, _zoomPick.SelectedIndex)]);
        _gridOn.Checked += (_, _) => _grid.Visibility = Visibility.Visible;
        _gridOn.Unchecked += (_, _) => _grid.Visibility = Visibility.Collapsed;
        resultScroll.PreviewMouseWheel += (_, e) =>
        {
            if ((System.Windows.Input.Keyboard.Modifiers & System.Windows.Input.ModifierKeys.Control) == 0) return;
            e.Handled = true;
            _zoomPick.SelectedIndex = Math.Clamp(_zoomPick.SelectedIndex + (e.Delta > 0 ? 1 : -1), 0, Zooms.Length - 1);
        };
        SetZoom(Zoom);
        views.Children.Add(new StackPanel { Margin = new Thickness(0, 0, 14, 0), Children = { resultHead, resultScroll } });
        views.Children.Add(Captioned("견줄 원본 얼굴", _referenceView));
        RenderOptions.SetBitmapScalingMode(_resultView, BitmapScalingMode.NearestNeighbor);
        RenderOptions.SetBitmapScalingMode(_referenceView, BitmapScalingMode.NearestNeighbor);

        var page = new DockPanel();
        DockPanel.SetDock(_status, Dock.Bottom);
        DockPanel.SetDock(controls, Dock.Left);
        page.Children.Add(_status);
        page.Children.Add(new ScrollViewer { Content = controls, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        page.Children.Add(views);
        Content = page;

        Loaded += (_, _) =>
        {
            _faces = Portraits.Open(Path.GetDirectoryName(AppSettings.LastSaveFilePath) ?? "");
            ShowReference();
            _status.Text = "그림을 열면 바로 바뀐 얼굴이 보입니다. 원본은 공용 색표 64색 · 플로이드-스타인버그에 가깝습니다.";
        };
    }

    // ── 화면 조각 ──────────────────────────────────────────────────────────

    private static Slider Bar(double min, double max, double value) =>
        new() { Minimum = min, Maximum = max, Value = value, Width = 180, VerticalAlignment = VerticalAlignment.Center };

    private static ComboBox Pick(params string[] items)
    {
        var box = new ComboBox { Width = 180 };
        foreach (var item in items) box.Items.Add(item);
        box.SelectedIndex = 0;
        return box;
    }

    private static Button Button(string text, Action run)
    {
        var b = new Button { Content = text, Margin = new Thickness(0, 6, 0, 0), Padding = new Thickness(8, 3, 8, 3) };
        b.Click += (_, _) => run();
        return b;
    }

    private static TextBlock Label(string text) =>
        new() { Text = text, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 10, 0, 2), TextWrapping = TextWrapping.Wrap };

    private static UIElement Row(string label, FrameworkElement control) => new StackPanel
    {
        Orientation = Orientation.Horizontal,
        Margin = new Thickness(0, 2, 0, 2),
        Children = { new TextBlock { Text = label, Width = 60, VerticalAlignment = VerticalAlignment.Center }, control },
    };

    private static UIElement Captioned(string caption, UIElement body) => new StackPanel
    {
        Margin = new Thickness(0, 0, 14, 0),
        Children = { new TextBlock { Text = caption, Margin = new Thickness(0, 0, 0, 4) }, body },
    };

    // ── 열기 · 그리기 ────────────────────────────────────────────────────────

    private void OpenImage()
    {
        var dlg = new Microsoft.Win32.OpenFileDialog { Filter = "그림 파일|*.png;*.jpg;*.jpeg;*.bmp|모든 파일|*.*" };
        if (dlg.ShowDialog(this) != true) return;
        try
        {
            var art = new BitmapImage();
            using (var file = File.OpenRead(dlg.FileName))
            {
                art.BeginInit();
                art.CacheOption = BitmapCacheOption.OnLoad;
                art.StreamSource = file;
                art.EndInit();
            }
            art.Freeze();
            _source = new FormatConvertedBitmap(art, PixelFormats.Bgra32, null, 0);
            _source.Freeze();
            _sourceView.Source = _source;
            Redraw();
            _status.Text = $"{Path.GetFileName(dlg.FileName)} — {_source.PixelWidth}x{_source.PixelHeight}";
        }
        catch (Exception ex) when (ex is IOException or NotSupportedException or FileFormatException)
        {
            _status.Text = $"그림을 못 열었습니다: {ex.Message}";
        }
    }

    /// <summary>자를 네모(원본 그림 점) — 폭 : 높이 = 5 : 6, 그림 밖으로 안 나가게 민다.</summary>
    private Int32Rect CropRect()
    {
        int sw = _source!.PixelWidth, sh = _source.PixelHeight;
        int w = (int)Math.Max(W, Math.Min(sw, sh * W / (double)H) * _size.Value);
        int h = w * H / W;
        if (h > sh) { h = sh; w = h * W / H; }
        int x = Math.Clamp((int)(_cx.Value * sw - w / 2.0), 0, sw - w);
        int y = Math.Clamp((int)(_cy.Value * sh - h / 2.0), 0, sh - h);
        return new Int32Rect(x, y, w, h);
    }

    private void Redraw()
    {
        if (_source == null || !IsLoaded) return;
        var crop = CropRect();

        // 원본 그림 위에 자를 네모를 그린다 — 보이는 칸 크기에 맞춰 줄인다.
        double scale = Math.Min(_sourceView.Width / _source.PixelWidth, _sourceView.Height / _source.PixelHeight);
        double ox = (_sourceView.Width - _source.PixelWidth * scale) / 2, oy = (_sourceView.Height - _source.PixelHeight * scale) / 2;
        Canvas.SetLeft(_cropBox, ox + crop.X * scale);
        Canvas.SetTop(_cropBox, oy + crop.Y * scale);
        _cropBox.Width = crop.Width * scale;
        _cropBox.Height = crop.Height * scale;

        var small = Shrink(new CroppedBitmap(_source, crop), _scaling.SelectedIndex == 0);
        var px = new byte[W * H * 4];
        small.CopyPixels(px, W * 4, 0);
        Adjust(px, _bright.Value, _contrast.Value / 100, _saturation.Value / 100);
        if (_bgOn.IsChecked == true && Hex(_bgFrom.Text) is { } from && Hex(_bgTo.Text) is { } to)
            Background(px, _bgPoint, _bgTol.Value, from, to, _bgDir.SelectedIndex, _bgSoft.IsChecked == true,
                       _painted.Any(p => p) ? _painted : null, _paint.IsChecked == true);
        if (_colors.SelectedIndex < 2) Quantize(px, Palette(_colors.SelectedIndex == 1), _dither.SelectedIndex);

        _result = px;
        var shown = px;
        if (_paint.IsChecked == true)
        {
            // 칠한 점은 형광분홍으로 얹어 보인다 — 내보낼 그림(_result)에는 안 든다.
            shown = (byte[])px.Clone();
            for (int k = 0; k < _painted.Length; k++)
                if (_painted[k]) { shown[k * 4] = Pink.B; shown[k * 4 + 1] = Pink.G; shown[k * 4 + 2] = Pink.R; }
        }
        var bmp = BitmapSource.Create(W, H, 96, 96, PixelFormats.Bgra32, null, shown, W * 4);
        bmp.Freeze();
        _resultView.Source = bmp;
    }

    /// <summary>80x96 으로 줄인다 — 곱게(여러 점을 섞어) 또는 가장 가까운 점으로.</summary>
    private static BitmapSource Shrink(BitmapSource crop, bool smooth)
    {
        var visual = new DrawingVisual();
        RenderOptions.SetBitmapScalingMode(visual, smooth ? BitmapScalingMode.HighQuality : BitmapScalingMode.NearestNeighbor);
        using (var dc = visual.RenderOpen()) dc.DrawImage(crop, new Rect(0, 0, W, H));
        var target = new RenderTargetBitmap(W, H, 96, 96, PixelFormats.Pbgra32);
        target.Render(visual);
        return new FormatConvertedBitmap(target, PixelFormats.Bgra32, null, 0);
    }

    /// <summary>밝기(더하기) · 대비(가운데 128 기준 곱하기) · 채도(회색과 섞기).</summary>
    private static void Adjust(byte[] px, double bright, double contrast, double saturation)
    {
        for (int i = 0; i < px.Length; i += 4)
        {
            double b = px[i], g = px[i + 1], r = px[i + 2];
            double grey = 0.299 * r + 0.587 * g + 0.114 * b;
            r = grey + (r - grey) * saturation; g = grey + (g - grey) * saturation; b = grey + (b - grey) * saturation;
            r = (r - 128) * contrast + 128 + bright; g = (g - 128) * contrast + 128 + bright; b = (b - 128) * contrast + 128 + bright;
            px[i] = Clamp(b); px[i + 1] = Clamp(g); px[i + 2] = Clamp(r); px[i + 3] = 255;
        }
    }

    private static byte Clamp(double v) => (byte)Math.Clamp((int)Math.Round(v), 0, 255);

    /// <summary>바꾼 얼굴의 배율을 바꾼다 — 그림 크기와 격자 칸(한 점 = 한 칸)을 같이 맞춘다.</summary>
    private void SetZoom(int zoom)
    {
        _zoom = zoom;
        _resultView.Width = _grid.Width = W * zoom;
        _resultView.Height = _grid.Height = H * zoom;
        var line = new Pen(new SolidColorBrush(Color.FromArgb(0x70, 0x80, 0x80, 0x80)), 1);
        line.Freeze();
        var tile = new DrawingBrush(new GeometryDrawing(null, line, new RectangleGeometry(new Rect(0, 0, zoom, zoom))))
        {
            TileMode = TileMode.Tile, Viewport = new Rect(0, 0, zoom, zoom), ViewportUnits = BrushMappingMode.Absolute,
            Viewbox = new Rect(0, 0, zoom, zoom), ViewboxUnits = BrushMappingMode.Absolute,
        };
        tile.Freeze();
        _grid.Fill = tile;
    }

    /// <summary>바뀐 얼굴 위 그 자리에 붓으로 칠하거나(참) 지운다(거짓).</summary>
    private void Dab(Point at, bool on)
    {
        int cx = (int)(at.X / _zoom), cy = (int)(at.Y / _zoom), r = _brush.SelectedIndex;   // 0 · 1 · 2 → 반지름
        bool changed = false;
        for (int y = cy - r; y <= cy + r; y++)
            for (int x = cx - r; x <= cx + r; x++)
            {
                if (x < 0 || y < 0 || x >= W || y >= H || _painted[y * W + x] == on) continue;
                _painted[y * W + x] = on;
                changed = true;
            }
        if (changed) Redraw();
    }

    /// <summary>「4A3528」 꼴의 16진 색. 못 읽으면 null.</summary>
    private static (int R, int G, int B)? Hex(string text)
    {
        text = text.Trim().TrimStart('#');
        return text.Length == 6 && int.TryParse(text, System.Globalization.NumberStyles.HexNumber, null, out int v)
            ? ((v >> 16) & 255, (v >> 8) & 255, v & 255) : null;
    }

    /// <summary>
    /// 배경을 그라데이션으로 바꾼다 — 기준점(<paramref name="seed"/>) 둘레 3x3 의 평균색에서 <paramref name="tolerance"/> 안쪽인 점을,
    /// 테두리나 기준점에서부터 이어진 것만 배경으로 친다(얼굴 속 같은 색은 안 건드린다). 테두리 전체 평균을 쓰면 위쪽 흰 터번 ·
    /// 아래쪽 옷이 섞여 배경을 못 잡았다. 부드럽게면 배경 몫을 3x3 으로 한 번 펴서 경계를 섞는다.
    /// 디더링 앞에 칠하므로 그라데이션도 원본처럼 점으로 섞인다.
    /// </summary>
    /// <param name="painted">손으로 칠한 곳. 있으면 저절로 가려내기 대신 이것을 배경으로 친다.</param>
    /// <param name="marking">칠하는 중 — 칠한 곳을 그라데이션 대신 형광분홍으로 찍어 보인다(디더링 뒤에 다시 찍는다).</param>
    private static void Background(byte[] px, (int X, int Y) seed, double tolerance, (int R, int G, int B) from,
                                   (int R, int G, int B) to, int direction, bool soft, bool[]? painted = null, bool marking = false)
    {
        if (painted != null && marking) return;   // 칠하는 동안은 그림을 그대로 두고 분홍 점만 얹는다(Redraw)
        double sr = 0, sg = 0, sb = 0;
        int n = 0;
        for (int y = Math.Max(0, seed.Y - 1); y <= Math.Min(H - 1, seed.Y + 1); y++)
            for (int x = Math.Max(0, seed.X - 1); x <= Math.Min(W - 1, seed.X + 1); x++)
            {
                int i = (y * W + x) * 4;
                sb += px[i]; sg += px[i + 1]; sr += px[i + 2]; n++;
            }
        double mr = sr / n, mg = sg / n, mb = sb / n, tol2 = tolerance * tolerance;
        bool Near(int k)
        {
            int i = k * 4;
            double dr = px[i + 2] - mr, dg = px[i + 1] - mg, db = px[i] - mb;
            return dr * dr + dg * dg + db * db <= tol2;
        }

        // 테두리에서 번지기
        var mask = new double[W * H];
        var queue = new Queue<int>();
        for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
            {
                if (x != 0 && y != 0 && x != W - 1 && y != H - 1) continue;
                int k = y * W + x;
                if (mask[k] == 0 && Near(k)) { mask[k] = 1; queue.Enqueue(k); }
            }
        int s0 = seed.Y * W + seed.X;
        if (mask[s0] == 0 && Near(s0)) { mask[s0] = 1; queue.Enqueue(s0); }
        while (queue.Count > 0)
        {
            int k = queue.Dequeue(), x = k % W, y = k / W;
            foreach (var (dx, dy) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
            {
                int nx = x + dx, ny = y + dy;
                if (nx < 0 || ny < 0 || nx >= W || ny >= H) continue;
                int nk = ny * W + nx;
                if (mask[nk] == 0 && Near(nk)) { mask[nk] = 1; queue.Enqueue(nk); }
            }
        }

        if (painted != null)
            for (int k = 0; k < mask.Length; k++) mask[k] = painted[k] ? 1 : 0;

        if (soft)
        {
            var spread = new double[W * H];
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    double sum = 0; int c = 0;
                    for (int dy = -1; dy <= 1; dy++)
                        for (int dx = -1; dx <= 1; dx++)
                        {
                            int nx = x + dx, ny = y + dy;
                            if (nx < 0 || ny < 0 || nx >= W || ny >= H) continue;
                            sum += mask[ny * W + nx]; c++;
                        }
                    spread[y * W + x] = sum / c;
                }
            mask = spread;
        }

        for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
            {
                double a = mask[y * W + x];
                if (a <= 0) continue;
                double t = direction switch
                {
                    1 => (x / (double)(W - 1) + y / (double)(H - 1)) / 2,
                    2 => x / (double)(W - 1),
                    3 => Math.Min(1, Math.Sqrt(Math.Pow((x - W / 2.0) / (W / 2.0), 2) + Math.Pow((y - H / 2.0) / (H / 2.0), 2)) / Math.Sqrt(2)),
                    _ => y / (double)(H - 1),
                };
                double gr = from.R + (to.R - from.R) * t, gg = from.G + (to.G - from.G) * t, gb = from.B + (to.B - from.B) * t;
                int i = (y * W + x) * 4;
                px[i + 2] = Clamp(px[i + 2] * (1 - a) + gr * a);
                px[i + 1] = Clamp(px[i + 1] * (1 - a) + gg * a);
                px[i] = Clamp(px[i] * (1 - a) + gb * a);
            }
    }

    /// <summary>쓸 색들 — 공용 색표 10~73(원본 얼굴이 쓰는 64색). 바라면 검정 · 흰색을 더한다.</summary>
    private static List<(int R, int G, int B)> Palette(bool withBlackWhite)
    {
        var list = new List<(int, int, int)>();
        for (int i = 10; i < GamePalette.OwnPaletteBase; i++)
            list.Add((GamePalette.Rgb[i * 3], GamePalette.Rgb[i * 3 + 1], GamePalette.Rgb[i * 3 + 2]));
        if (withBlackWhite) { list.Add((0, 0, 0)); list.Add((255, 255, 255)); }
        return list;
    }

    private static readonly int[,] Bayer2 = { { 0, 2 }, { 3, 1 } };
    private static readonly int[,] Bayer4 = { { 0, 8, 2, 10 }, { 12, 4, 14, 6 }, { 3, 11, 1, 9 }, { 15, 7, 13, 5 } };

    /// <summary>색표로 줄인다 — 0 플로이드-스타인버그 · 1 바둑판 2x2 · 2 바둑판 4x4 · 3 없음.</summary>
    private static void Quantize(byte[] px, List<(int R, int G, int B)> palette, int dither)
    {
        var err = new double[W * H * 3];
        for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
            {
                int i = (y * W + x) * 4, e = (y * W + x) * 3;
                // 넘겨받은 오차는 0~255 로 자른다 — 안 자르면 어두운 데서 오차가 쌓여 초록 · 빨강 점이 튄다.
                double r = Math.Clamp(px[i + 2] + err[e], 0, 255), g = Math.Clamp(px[i + 1] + err[e + 1], 0, 255),
                       b = Math.Clamp(px[i] + err[e + 2], 0, 255);
                if (dither is 1 or 2)
                {
                    // 바둑판 — 칸마다 문턱을 흔들어 두 색을 번갈아 고르게 한다(원본 그림의 바둑판 섞기).
                    double t = dither == 1 ? (Bayer2[y % 2, x % 2] + 0.5) / 4 - 0.5 : (Bayer4[y % 4, x % 4] + 0.5) / 16 - 0.5;
                    r += t * 32; g += t * 32; b += t * 32;
                }
                var (pr, pg, pb) = Nearest(palette, r, g, b);
                px[i + 2] = (byte)pr; px[i + 1] = (byte)pg; px[i] = (byte)pb;
                if (dither != 0) continue;

                double er = r - pr, eg = g - pg, eb = b - pb;
                Spread(err, x + 1, y, er, eg, eb, 7 / 16.0);
                Spread(err, x - 1, y + 1, er, eg, eb, 3 / 16.0);
                Spread(err, x, y + 1, er, eg, eb, 5 / 16.0);
                Spread(err, x + 1, y + 1, er, eg, eb, 1 / 16.0);
            }
    }

    private static void Spread(double[] err, int x, int y, double r, double g, double b, double k)
    {
        if (x < 0 || x >= W || y >= H) return;
        int e = (y * W + x) * 3;
        err[e] += r * k; err[e + 1] += g * k; err[e + 2] += b * k;
    }

    private static (int R, int G, int B) Nearest(List<(int R, int G, int B)> palette, double r, double g, double b)
    {
        var best = palette[0];
        double bestD = double.MaxValue;
        foreach (var c in palette)
        {
            double dr = r - c.R, dg = g - c.G, db = b - c.B;
            double d = dr * dr + dg * dg + db * db;
            if (d < bestD) { bestD = d; best = c; }
        }
        return best;
    }

    private void ShowReference()
    {
        if (_faces == null) return;
        int face = (int)_reference.Value;
        _referenceView.Source = _faces.TryGetBgra(face, female: false) is { } px
            ? BitmapSource.Create(W, H, 96, 96, PixelFormats.Bgra32, null, px, W * 4)
            : null;
    }

    // ── 내보내기 ─────────────────────────────────────────────────────────────

    private void Save(string path)
    {
        var bmp = BitmapSource.Create(W, H, 96, 96, PixelFormats.Bgra32, null, _result!, W * 4);
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(bmp));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var file = File.Create(path);
        enc.Save(file);
    }

    private void Export()
    {
        if (_result == null) { _status.Text = "먼저 그림을 여세요."; return; }
        var dlg = new Microsoft.Win32.SaveFileDialog { Filter = "PNG|*.png", FileName = $"male-{(int)_faceNumber.Value}.png" };
        if (dlg.ShowDialog(this) != true) return;
        Save(dlg.FileName);
        _status.Text = $"{dlg.FileName} 에 적었습니다.";
    }

    /// <summary>
    /// 앱 얼굴로 넣는다 — 소스 옆 <c>asset/face</c>(개발 때) 또는 실행 파일 옆에 <c>male-번호.png</c> 로 적는다. 번호는 9000 부터다
    /// (<see cref="Portraits.ShippedBase"/>) — 그 아래는 MALE.CDS 의 얼굴이다.
    /// </summary>
    private void Install()
    {
        if (_result == null) { _status.Text = "먼저 그림을 여세요."; return; }
        int face = (int)_faceNumber.Value;
        if (face <= Portraits.ShippedBase)
        {
            _status.Text = $"얼굴 번호는 {Portraits.ShippedBase + 1} 부터입니다.";
            return;
        }
        string folder = FaceFolder();
        string path = Path.Combine(folder, Portraits.ShippedFileName(false, face));
        if (File.Exists(path) && !ConfirmDialog.Ask(this, $"{Path.GetFileName(path)} 이 이미 있습니다. 덮어쓸까요?", "초상화 필터")) return;
        Save(path);
        Portraits.ForgetShipped();
        _status.Text = $"{path} 에 넣었습니다. 후원자 · 인물의 얼굴 번호를 {face} 로 두면 이 그림이 나옵니다.";
    }

    /// <summary>얼굴 그림 자리 — 저장소의 <c>asset/face</c> 가 위쪽에 있으면 그것, 없으면 실행 파일 옆이다.</summary>
    private static string FaceFolder()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
        {
            string candidate = Path.Combine(dir.FullName, "asset", "face");
            if (Directory.Exists(candidate) && File.Exists(Path.Combine(dir.FullName, "cds-helper.sln"))) return candidate;
        }
        return Path.Combine(AppContext.BaseDirectory, "asset", "face");
    }

    public static void Show(Window? owner)
    {
        var window = new PortraitFilterDialog();
        if (owner != null) window.Owner = owner;
        window.ShowDialog();
    }
}
