using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CdsHelper.Support.Local.Helpers;

namespace CdsHelper.Main.UI.Views;

/// <summary>
/// 「DHO 배 찍기」 — DHO(대항해시대 온라인) 배 모형을 3D 로 그려 항해 지도 8방향 그림과 선체 동영상(한 바퀴 도는 MP4)을 굽는다.
/// 선박 등록 창의 「DHO에서 찍기…」로 연다.
/// </summary>
/// <remarks>
/// 그리는 것은 저장소의 <c>tools/dho_ship_render.py</c> 이고, 그 스크립트는 DHO 저장소의 <c>tools/gvo</c> 로 설치된 GV Online
/// 클라이언트를 읽는다. 그래서 이 창은 그 둘이 있는 PC(우리 쪽)에서만 쓴다 — 플레이어는 결과물만 받는다.
/// MP4 는 <see cref="FrameMovie"/> 가 윈도의 것으로 묶는다(ffmpeg 를 안 쓴다).
///
/// 모형 하나에 돛 자리가 여럿이고, 한 돛대에 가로돛 · 삼각돛이 대체로 겹쳐 서는 모형(바사 따위)이 있어 자리마다 넣고 뺀다.
/// </remarks>
public sealed class DhoShipShotDialog : Window
{
    /// <summary>창의 값 — <c>%APPDATA%\CdsHelper\dho-ship-shot.json</c> 에 남는다.</summary>
    private sealed class Options
    {
        public string DhoFolder { get; set; } = @"C:\Users\ocean\git\dho";
        public string Python { get; set; } = "python";
        public int Model { get; set; } = 1;
        public double SpriteElevation { get; set; } = 20;
        public bool Hd { get; set; } = true;
        public int Frames { get; set; } = 60;
        public int Fps { get; set; } = 15;
        public int Width { get; set; } = 240;
        public int Height { get; set; } = 176;
        public double Elevation { get; set; } = 15;
        public double Turns { get; set; } = 1;
        public string Background { get; set; } = "40,60,90";
    }

    private static string OptionsPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CdsHelper", "dho-ship-shot.json");

    private readonly Options _options;

    private readonly TextBox _dho = Box(320), _python = Box(120), _model = Box(50);
    private readonly ComboBox _ships = new() { Width = 220, Margin = new Thickness(0, 0, 8, 0) };
    private readonly WrapPanel _slots = new();
    private readonly TextBox _spriteElev = Box(50), _frames = Box(50), _fps = Box(40), _width = Box(50), _height = Box(50),
                             _elev = Box(50), _turns = Box(50), _bg = Box(90);
    private readonly CheckBox _hd = new() { Content = "고해상도(192x192)도", VerticalAlignment = VerticalAlignment.Center };
    private readonly WrapPanel _previews = new() { Margin = new Thickness(0, 6, 0, 0) };
    private readonly Image _movieFrame = new() { Width = 240, Height = 176, Stretch = Stretch.Uniform, Margin = new Thickness(0, 6, 0, 0) };
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 0) };
    private readonly List<Button> _busyButtons = [];
    private readonly Button _apply;

    private List<(string Name, int Model)> _shipList = [];

    /// <summary>마지막으로 찍은 8방향 그림이 든 폴더(<c>ship_0.png</c> ~ · <c>ship_0_hd.png</c> ~). 아직 안 찍었으면 null.</summary>
    public string? SpriteFolder { get; private set; }

    /// <param name="canApply">참이면 「이 그림 넣기」 단추가 서고, 누르면 창이 참으로 닫힌다(선박 등록 창이 그림을 가져간다).</param>
    public DhoShipShotDialog(bool canApply = false)
    {
        Title = "DHO 배 찍기";
        Width = 820;
        Height = 760;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        _options = LoadOptions();
        _dho.Text = _options.DhoFolder;
        _python.Text = _options.Python;
        _model.Text = $"{_options.Model}";
        _spriteElev.Text = $"{_options.SpriteElevation}";
        _hd.IsChecked = _options.Hd;
        _frames.Text = $"{_options.Frames}";
        _fps.Text = $"{_options.Fps}";
        _width.Text = $"{_options.Width}";
        _height.Text = $"{_options.Height}";
        _elev.Text = $"{_options.Elevation}";
        _turns.Text = $"{_options.Turns}";
        _bg.Text = _options.Background;

        var form = new StackPanel { Margin = new Thickness(14) };

        form.Children.Add(Section("DHO"));
        form.Children.Add(Row(Label("DHO 저장소"), _dho, MakeButton("찾기…", PickDho, busy: false), Label("파이썬"), _python));
        form.Children.Add(Note("DHO 저장소의 tools/gvo 와 data/extracted/ships.json 을 쓴다. 설치된 GV Online 클라이언트가 있어야 한다(환경 변수 GVO_DIR, 없으면 기본 설치 자리)."));

        form.Children.Add(Section("배"));
        _ships.SelectionChanged += (_, _) =>
        {
            if (_ships.SelectedIndex >= 0 && _ships.SelectedIndex < _shipList.Count)
            {
                _model.Text = $"{_shipList[_ships.SelectedIndex].Model}";
                _ = ReadSlots();
            }
        };
        _model.LostFocus += (_, _) => _ = ReadSlots();
        form.Children.Add(Row(_ships, Label("모형 번호"), _model, MakeButton("돛 자리 읽기", () => _ = ReadSlots())));
        form.Children.Add(Row(Label("돛 자리"), _slots));
        form.Children.Add(Note("모형 하나에 돛 자리가 여럿이다. 한 돛대에 가로돛과 삼각돛이 겹쳐 서는 모형(바사 따위)은 하나만 남긴다."));

        form.Children.Add(Section("항해 지도 그림 (8방향)"));
        form.Children.Add(Row(Label("내려다보는 각도"), _spriteElev, _hd, MakeButton("그림 찍기", () => _ = ShootSprites())));
        form.Children.Add(Note("48x48 여덟 장 — 0 북에서 반시계(0 북 · 2 서 · 4 남 · 6 동). 여덟 장은 같은 배율이고, 붙박이 배 그림과 크기를 맞췄다."));
        form.Children.Add(_previews);

        form.Children.Add(Section("선체 동영상 (한 바퀴)"));
        form.Children.Add(Row(Label("장수"), _frames, Label("초당"), _fps, Label("가로"), _width, Label("세로"), _height));
        form.Children.Add(Row(Label("내려다보는 각도"), _elev, Label("바퀴"), _turns, Label("배경 R,G,B"), _bg,
                              MakeButton("동영상 만들기…", () => _ = ShootMovie())));
        form.Children.Add(Note("원본 선체 동영상은 240x176 · 초당 15장 · 40장이다. 첫 장은 뱃머리가 왼쪽을 본다. 만든 MP4 는 동영상 은행에 올려 쓴다."));
        form.Children.Add(_movieFrame);

        form.Children.Add(_status);

        _apply = new Button { Content = "이 그림 넣기", Padding = new Thickness(14, 4, 14, 4), Margin = new Thickness(0, 0, 8, 0), IsEnabled = false };
        _apply.Click += (_, _) => { SaveOptions(); DialogResult = true; };
        var close = new Button { Content = "닫기", Padding = new Thickness(14, 4, 14, 4) };
        close.Click += (_, _) => Close();
        var bottom = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(14, 0, 14, 12) };
        if (canApply) bottom.Children.Add(_apply);
        bottom.Children.Add(close);

        var page = new DockPanel();
        DockPanel.SetDock(bottom, Dock.Bottom);
        page.Children.Add(bottom);
        page.Children.Add(new ScrollViewer { Content = form, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        Content = page;

        Loaded += (_, _) => { LoadShips(); _ = ReadSlots(); };
        Closed += (_, _) => SaveOptions();
    }

    // ── 배 목록 · 돛 자리 ────────────────────────────────────────────────

    /// <summary>DHO 배 표(data/extracted/ships.json)에서 모형마다 첫 이름을 뽑는다.</summary>
    private void LoadShips()
    {
        _shipList = [];
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(_dho.Text.Trim(), "data", "extracted", "ships.json")));
            var seen = new HashSet<int>();
            foreach (var row in doc.RootElement.EnumerateArray())
            {
                int model = row.GetProperty("Model").GetInt32();
                if (seen.Add(model)) _shipList.Add((row.GetProperty("Name").GetString() ?? "", model));
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException or KeyNotFoundException or InvalidOperationException)
        {
            _status.Text = $"DHO 배 표를 못 읽었습니다 — {ex.Message}. 모형 번호를 바로 적어도 된다.";
        }
        _ships.ItemsSource = _shipList.Select(s => $"{s.Name} (모형 {s.Model})").ToList();
        int at = _shipList.FindIndex(s => $"{s.Model}" == _model.Text.Trim());
        if (at >= 0) _ships.SelectedIndex = at;
    }

    private int _slotsFor = -1;

    /// <summary>지금 모형의 돛 자리를 스크립트에 묻고 체크 칸을 다시 세운다. 모형이 그대로면 둔다.</summary>
    private async Task ReadSlots()
    {
        if (!int.TryParse(_model.Text.Trim(), out int model) || model == _slotsFor) return;
        _slotsFor = model;
        _slots.Children.Clear();
        var (ok, output) = await RunScript(["--model", $"{model}", "--out", Path.GetTempPath(), "--info"], quiet: true);
        var line = output.LastOrDefault(l => l.StartsWith("slots", StringComparison.Ordinal));
        if (!ok || line == null) { _slotsFor = -1; return; }
        foreach (var part in line[5..].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            _slots.Children.Add(new CheckBox { Content = $"자리 {part}", Tag = part, IsChecked = true, Margin = new Thickness(0, 0, 12, 0), VerticalAlignment = VerticalAlignment.Center });
    }

    private string SkipSlots() => string.Join(",", _slots.Children.OfType<CheckBox>().Where(c => c.IsChecked != true).Select(c => (string)c.Tag));

    // ── 찍기 ──────────────────────────────────────────────────────────

    private async Task ShootSprites()
    {
        if (!int.TryParse(_model.Text.Trim(), out int model)) { _status.Text = "모형 번호를 적어 주십시오."; return; }
        string out_ = NewWorkFolder();
        var args = new List<string> { "--model", $"{model}", "--out", out_, "--sprites", "--sprite-elev", _spriteElev.Text.Trim(), "--skip-slots", SkipSlots() };
        if (_hd.IsChecked == true) args.Add("--hd");
        var (ok, _) = await RunScript(args);
        if (!ok) return;

        SpriteFolder = out_;
        _apply.IsEnabled = true;
        _previews.Children.Clear();
        for (int i = 0; i < ShipRegistry.Directions; i++)
        {
            var image = new Image { Width = 96, Height = 96, Margin = new Thickness(0, 0, 4, 4), Source = Read(Path.Combine(out_, $"ship_{i}.png")),
                                    ToolTip = ShipRegistry.DirectionNames[i] };
            RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.NearestNeighbor);
            _previews.Children.Add(new Border { Background = new SolidColorBrush(Color.FromRgb(40, 60, 90)), Child = image, Margin = new Thickness(0, 0, 4, 4) });
        }
        _status.Text = $"여덟 장을 찍었습니다 — {out_}";
    }

    private async Task ShootMovie()
    {
        if (!int.TryParse(_model.Text.Trim(), out int model)) { _status.Text = "모형 번호를 적어 주십시오."; return; }
        if (!int.TryParse(_fps.Text.Trim(), out int fps) || !int.TryParse(_width.Text.Trim(), out int width)
            || !int.TryParse(_height.Text.Trim(), out int height) || width % 2 != 0 || height % 2 != 0)
        {
            _status.Text = "초당 장수와 가로 · 세로를 숫자로 적어 주십시오. 가로 · 세로는 짝수라야 한다(H.264).";
            return;
        }
        string name = _ships.SelectedIndex >= 0 && _ships.SelectedIndex < _shipList.Count ? _shipList[_ships.SelectedIndex].Name : $"model{model}";
        var save = new Microsoft.Win32.SaveFileDialog { Title = "선체 동영상 저장", Filter = "MP4|*.mp4", FileName = $"{name}.mp4" };
        if (save.ShowDialog(this) != true) return;

        string out_ = NewWorkFolder();
        var (ok, _) = await RunScript(["--model", $"{model}", "--out", out_, "--frames", _frames.Text.Trim(),
                                       "--width", $"{width}", "--height", $"{height}", "--elev", _elev.Text.Trim(),
                                       "--turns", _turns.Text.Trim(), "--bg", _bg.Text.Trim(), "--skip-slots", SkipSlots()]);
        if (!ok) return;

        var frames = Directory.GetFiles(out_, "frame_*.png").OrderBy(f => f, StringComparer.Ordinal).ToList();
        if (frames.Count > 0) _movieFrame.Source = Read(frames[0]);
        Busy(true);
        try
        {
            await FrameMovie.WriteAsync(frames, fps, width, height, save.FileName,
                                        new Progress<double>(p => _status.Text = $"동영상을 굽는 중 {p:P0}"));
            _status.Text = $"동영상을 만들었습니다 — {save.FileName} ({frames.Count}장, {frames.Count / (double)fps:0.0}초)";
        }
        catch (Exception ex)
        {
            _status.Text = $"동영상을 굽지 못했습니다 — {ex.Message}";
        }
        finally
        {
            Busy(false);
        }
    }

    /// <summary>스크립트를 돌린다. 줄마다 아래 상태 줄에 띄우고, 다 나온 줄을 돌려준다.</summary>
    private async Task<(bool Ok, List<string> Output)> RunScript(IEnumerable<string> args, bool quiet = false)
    {
        var lines = new List<string>();
        string? script = FindScript();
        if (script == null)
        {
            _status.Text = "tools/dho_ship_render.py 를 못 찾았습니다 — 저장소에서 빌드한 편집기로 여십시오.";
            return (false, lines);
        }
        string gvo = Path.Combine(_dho.Text.Trim(), "tools", "gvo");
        if (!Directory.Exists(gvo))
        {
            _status.Text = $"DHO 도구 폴더가 없습니다 — {gvo}";
            return (false, lines);
        }

        var start = new ProcessStartInfo(_python.Text.Trim())
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = Path.GetTempPath(),
        };
        start.Environment["PYTHONIOENCODING"] = "utf-8";
        start.ArgumentList.Add(script);
        start.ArgumentList.Add("--gvo");
        start.ArgumentList.Add(gvo);
        foreach (var a in args) start.ArgumentList.Add(a);

        if (!quiet) Busy(true);
        try
        {
            using var process = Process.Start(start)!;
            process.OutputDataReceived += (_, e) =>
            {
                if (e.Data == null) return;
                lock (lines) lines.Add(e.Data);
                if (!quiet) Dispatcher.BeginInvoke(() => _status.Text = e.Data);
            };
            process.BeginOutputReadLine();
            string error = await process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();
            if (process.ExitCode != 0)
            {
                var last = error.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).LastOrDefault();
                _status.Text = $"그리지 못했습니다 — {last ?? $"끝 값 {process.ExitCode}"}";
                return (false, lines);
            }
            return (true, lines);
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            _status.Text = $"파이썬을 못 띄웠습니다 — {ex.Message}";
            return (false, lines);
        }
        finally
        {
            if (!quiet) Busy(false);
        }
    }

    /// <summary>실행 폴더에서 위로 올라가며 <c>tools/dho_ship_render.py</c> 를 찾는다 — 배포 앱에는 안 싣는다.</summary>
    private static string? FindScript()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
        {
            string path = Path.Combine(dir.FullName, "tools", "dho_ship_render.py");
            if (File.Exists(path)) return path;
        }
        return null;
    }

    private static string NewWorkFolder()
    {
        string path = Path.Combine(Path.GetTempPath(), "cds-dho-shot", DateTime.Now.ToString("yyyyMMdd-HHmmss-fff"));
        Directory.CreateDirectory(path);
        return path;
    }

    private void Busy(bool on)
    {
        foreach (var b in _busyButtons) b.IsEnabled = !on;
        Cursor = on ? System.Windows.Input.Cursors.Wait : null;
    }

    private void PickDho()
    {
        var dlg = new Microsoft.Win32.OpenFolderDialog { Title = "DHO 저장소 폴더", InitialDirectory = _dho.Text };
        if (dlg.ShowDialog(this) != true) return;
        _dho.Text = dlg.FolderName;
        _slotsFor = -1;
        LoadShips();
        _ = ReadSlots();
    }

    // ── 값 남기기 ────────────────────────────────────────────────────────

    private static Options LoadOptions()
    {
        try { return JsonSerializer.Deserialize<Options>(File.ReadAllText(OptionsPath)) ?? new(); }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException) { return new(); }
    }

    private void SaveOptions()
    {
        _options.DhoFolder = _dho.Text.Trim();
        _options.Python = _python.Text.Trim();
        if (int.TryParse(_model.Text.Trim(), out int model)) _options.Model = model;
        if (double.TryParse(_spriteElev.Text.Trim(), out double se)) _options.SpriteElevation = se;
        _options.Hd = _hd.IsChecked == true;
        if (int.TryParse(_frames.Text.Trim(), out int frames)) _options.Frames = frames;
        if (int.TryParse(_fps.Text.Trim(), out int fps)) _options.Fps = fps;
        if (int.TryParse(_width.Text.Trim(), out int width)) _options.Width = width;
        if (int.TryParse(_height.Text.Trim(), out int height)) _options.Height = height;
        if (double.TryParse(_elev.Text.Trim(), out double elev)) _options.Elevation = elev;
        if (double.TryParse(_turns.Text.Trim(), out double turns)) _options.Turns = turns;
        _options.Background = _bg.Text.Trim();
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(OptionsPath)!);
            File.WriteAllText(OptionsPath, JsonSerializer.Serialize(_options, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    // ── 칸 ──────────────────────────────────────────────────────────────

    private static BitmapImage? Read(string path)
    {
        if (!File.Exists(path)) return null;
        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;   // 파일을 붙들고 있지 않게
        image.UriSource = new Uri(path);
        image.EndInit();
        image.Freeze();
        return image;
    }

    private static TextBox Box(double width) => new() { Width = width, Margin = new Thickness(0, 0, 8, 0), VerticalContentAlignment = VerticalAlignment.Center };

    private static TextBlock Label(string text) => new() { Text = text, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) };

    private Button MakeButton(string text, Action run, bool busy = true)
    {
        var button = new Button { Content = text, Padding = new Thickness(12, 3, 12, 3), Margin = new Thickness(0, 0, 8, 0) };
        button.Click += (_, _) => run();
        if (busy) _busyButtons.Add(button);
        return button;
    }

    private static TextBlock Section(string text) => new() { Text = text, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 14, 0, 4) };

    private static TextBlock Note(string text) => new() { Text = text, Foreground = Brushes.Gray, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0) };

    private static StackPanel Row(params UIElement[] children)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 2) };
        foreach (var child in children) row.Children.Add(child);
        return row;
    }
}
