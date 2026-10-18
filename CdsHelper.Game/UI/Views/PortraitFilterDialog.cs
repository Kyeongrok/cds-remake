using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CdsHelper.Game.Local.Helpers;
using CdsHelper.Support.Local.Settings;

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
    private readonly Image _referenceView = new() { Width = W * Zoom, Height = H * Zoom };
    private readonly Canvas _cropFrame = new() { Width = 300, Height = 360, IsHitTestVisible = false };
    private readonly System.Windows.Shapes.Rectangle _cropBox = new() { Stroke = Brushes.Yellow, StrokeThickness = 2 };

    private readonly Slider _cx = Bar(0, 1, 0.5), _cy = Bar(0, 1, 0.45), _size = Bar(0.1, 1, 0.6);
    private readonly Slider _bright = Bar(-60, 60, 0), _contrast = Bar(50, 160, 100), _saturation = Bar(0, 200, 100);
    private readonly ComboBox _colors = Pick("공용 색표 64색 (원본)", "공용 색표 + 검정 · 흰색", "색 제한 없음");
    private readonly ComboBox _dither = Pick("플로이드-스타인버그", "바둑판 2x2", "바둑판 4x4", "디더링 없음");
    private readonly ComboBox _scaling = Pick("곱게 줄이기", "가장 가까운 점");
    private readonly TextBox _reference = new() { Text = "248", Width = 60 };
    private readonly TextBox _faceNumber = new() { Text = "9003", Width = 60 };
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
        foreach (var c in new[] { _colors, _dither, _scaling }) c.SelectionChanged += (_, _) => Redraw();
        _reference.TextChanged += (_, _) => ShowReference();

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
        views.Children.Add(Captioned("바꾼 얼굴(게임에서 보이는 결)", _resultView));
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
        if (_colors.SelectedIndex < 2) Quantize(px, Palette(_colors.SelectedIndex == 1), _dither.SelectedIndex);

        _result = px;
        var bmp = BitmapSource.Create(W, H, 96, 96, PixelFormats.Bgra32, null, px, W * 4);
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
        if (_faces == null || !int.TryParse(_reference.Text, out int face)) return;
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
        var dlg = new Microsoft.Win32.SaveFileDialog { Filter = "PNG|*.png", FileName = $"male-{_faceNumber.Text}.png" };
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
        if (!int.TryParse(_faceNumber.Text, out int face) || face <= Portraits.ShippedBase)
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
