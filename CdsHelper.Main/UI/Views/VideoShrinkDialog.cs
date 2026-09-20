using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using CdsHelper.Support.Local.Helpers;
using CdsHelper.Support.UI.Units;

namespace CdsHelper.Main.UI.Views;

/// <summary>
/// 동영상 한 편을 틀어 보면서 크기를 줄이고, 소리를 빼고, 압축해 MP4 로 쓰는 창.
/// </summary>
/// <remarks>
/// 실제로 바꾸는 일은 <see cref="VideoShrinker"/> 가 한다 — 여기는 보여 주고 고르기만 한다.
/// 이미지 줄이기 창(<see cref="ImageShrinkDialog"/>)과 꼴을 맞췄다. 동영상은 오래 걸리므로
/// 진행 막대와 그만두기 단추를 둔다.
/// </remarks>
public sealed class VideoShrinkDialog : Window
{
    private readonly MediaElement _player;
    private readonly Button _playButton;
    private readonly TextBlock _playingText;
    private readonly TextBlock _previewInfo;
    private readonly TextBlock _pathText;
    private readonly TextBlock _status;
    private readonly ProgressBar _progress;

    private readonly ComboBox _mode;
    private readonly NumericSpinner _amount;
    private readonly TextBlock _amountUnit;
    private readonly CheckBox _removeAudio;
    private readonly TextBlock _audioHint;
    private readonly ComboBox _quality;
    private readonly NumericSpinner _bitrate;
    private readonly TextBlock _qualityHint;
    private readonly ComboBox _where;
    private readonly TextBox _suffix;
    private readonly Button _folderButton;
    private readonly TextBlock _folderText;
    private readonly Button _saveButton;
    private readonly Button _cancelButton;
    private readonly Button _revealButton;
    private readonly Button _playResultButton;

    private readonly List<Control> _inputs = [];

    private string? _path;
    private string? _folder;
    private string? _lastOutput;
    private VideoShrinker.Probe? _probe;
    private int _probeToken;
    private bool _running;
    private bool _playing;
    private bool _closeWhenDone;
    private CancellationTokenSource? _cancel;

    public VideoShrinkDialog()
    {
        Title = "동영상 줄이기";
        Width = 1100;
        Height = 720;
        MinWidth = 820;
        MinHeight = 540;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        AllowDrop = true;

        // 차례가 VideoShrinker.SizeMode 와 하나씩 맞물린다 — 손대면 양쪽을 같이 고쳐야 한다.
        _mode = Combo(230, [
            "크기는 그대로",
            "가로를 맞춘다 (px)",
            "세로를 맞춘다 (px)",
            "긴 변을 맞춘다 (px)",
            "비율로 줄인다 (%)",
        ]);
        _amount = Spinner(1280, 2, 16000, 10, 92);
        _amountUnit = new TextBlock { Text = "px", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 0, 0) };

        _removeAudio = new CheckBox { Content = "소리(배경음악) 빼기", Margin = new Thickness(0, 2, 0, 0) };
        _inputs.Add(_removeAudio);
        _audioHint = Hint();

        // 차례가 VideoShrinker.Quality 와 하나씩 맞물린다.
        _quality = Combo(230, ["화질 좋게", "보통", "작게", "비트레이트 직접 (kbps)"]);
        _quality.SelectedIndex = 1;
        _bitrate = Spinner(1500, 150, 50000, 100, 92);
        _qualityHint = Hint();

        _where = Combo(230, ["원본 옆에 저장", "다른 폴더에 저장"]);
        _suffix = Box("_small", 80);
        _folderButton = MakeButton("폴더…", PickFolder);
        _folderText = Hint();

        _player = new MediaElement
        {
            LoadedBehavior = MediaState.Manual,
            UnloadedBehavior = MediaState.Close,
            ScrubbingEnabled = true,
            Stretch = Stretch.Uniform,
        };
        _playButton = MakeButton("▶ 재생", TogglePlay);
        _playButton.IsEnabled = false;
        _playingText = new TextBlock { Foreground = Brushes.Gray, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };

        _previewInfo = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0), Foreground = Brushes.DimGray };
        _pathText = new TextBlock
        {
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = Brushes.Gray,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Text = "연 동영상이 없습니다 — 파일을 열거나 창에 끌어다 놓으세요.",
        };
        _status = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0) };
        _progress = new ProgressBar { Height = 6, Minimum = 0, Maximum = 1, Margin = new Thickness(0, 6, 0, 0), Visibility = Visibility.Collapsed };

        _saveButton = MakeButton("줄여서 저장", () => _ = SaveAsync());
        _revealButton = MakeButton("만든 파일 보기", RevealLast);
        _playResultButton = MakeButton("만든 것 틀기", PlayResult);

        // 돌리는 중에도 눌려야 하므로 _inputs 에 넣지 않는다.
        _cancelButton = new Button { Content = "그만두기", Padding = new Thickness(12, 4, 12, 4), Margin = new Thickness(0, 0, 8, 0), Visibility = Visibility.Collapsed };
        _cancelButton.Click += (_, _) => _cancel?.Cancel();

        Content = BuildContent();

        _mode.SelectionChanged += (_, _) => OnModeChanged();
        _quality.SelectionChanged += (_, _) => UpdateInfo();
        _where.SelectionChanged += (_, _) => OnWhereChanged();
        _amount.ValueChanged += (_, _) => UpdateInfo();
        _bitrate.ValueChanged += (_, _) => UpdateInfo();
        _removeAudio.Checked += (_, _) => OnAudioChanged();
        _removeAudio.Unchecked += (_, _) => OnAudioChanged();

        _player.MediaOpened += (_, _) => OnMediaOpened();
        _player.MediaEnded += (_, _) => { _player.Stop(); SetPlaying(false); };
        _player.MediaFailed += (_, e) =>
        {
            SetPlaying(false);
            _playButton.IsEnabled = false;
            _playingText.Text = $"미리 보기를 틀지 못했습니다 — {e.ErrorException?.Message}";
        };

        DragOver += OnDragOver;
        Drop += OnDrop;
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape && !_running) Close();
        };

        // 돌리는 중에 닫으면 그만두게 하고, 멈춘 뒤에 닫는다 — 반쪽 임시 파일을 치울 틈을 준다.
        Closing += (_, e) =>
        {
            if (!_running) return;

            e.Cancel = true;
            _closeWhenDone = true;
            _cancel?.Cancel();
            _status.Text = "그만두는 중입니다 — 멈추면 닫힙니다.";
        };
        Closed += (_, _) => _player.Close();

        OnModeChanged();
        OnWhereChanged();
        OnAudioChanged();
        UpdateEnabled();
    }

    // ── 화면 짜기 ───────────────────────────────────────────────────────────

    private UIElement BuildContent()
    {
        var top = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
        top.Children.Add(MakeButton("파일 열기…", OpenFile));
        top.Children.Add(_pathText);

        var previewHead = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
        var title = new TextBlock { Text = "미리 보기", FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0) };
        DockPanel.SetDock(title, Dock.Left);
        DockPanel.SetDock(_playButton, Dock.Left);
        previewHead.Children.Add(title);
        previewHead.Children.Add(_playButton);
        previewHead.Children.Add(_playingText);

        var frame = new Border
        {
            BorderBrush = Brushes.LightGray,
            BorderThickness = new Thickness(1),
            Background = Brushes.Black,
            Child = _player,
        };

        var left = new Grid();
        left.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        left.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        left.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        Place(left, previewHead, 0);
        Place(left, frame, 1);
        Place(left, _previewInfo, 2);

        var options = new StackPanel { Margin = new Thickness(12, 0, 0, 0) };
        options.Children.Add(Section("크기"));
        options.Children.Add(_mode);
        options.Children.Add(Row(_amount, _amountUnit));

        options.Children.Add(Section("소리"));
        options.Children.Add(_removeAudio);
        options.Children.Add(_audioHint);

        options.Children.Add(Section("압축"));
        options.Children.Add(_quality);
        options.Children.Add(Row(_bitrate, Label("kbps", 6)));
        options.Children.Add(_qualityHint);

        options.Children.Add(Section("저장 위치"));
        options.Children.Add(_where);
        options.Children.Add(Row(Label("꼬리말"), _suffix, _folderButton));
        options.Children.Add(_folderText);

        var body = new Grid();
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(270) });
        Grid.SetColumn(left, 0);
        Grid.SetColumn(options, 1);
        body.Children.Add(left);
        body.Children.Add(options);

        var foot = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 8, 0, 0) };
        foot.Children.Add(_playResultButton);
        foot.Children.Add(_revealButton);
        foot.Children.Add(_cancelButton);
        foot.Children.Add(_saveButton);
        var close = MakeButton("닫기", Close);
        close.Margin = new Thickness(0);   // 줄 끝이라 오른쪽 여백을 뗀다
        _inputs.Remove(close);             // 돌리는 중에 눌러도 그만두고 닫게 둔다
        foot.Children.Add(close);

        var grid = new Grid { Margin = new Thickness(12) };
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        Place(grid, top, 0);
        Place(grid, body, 1);
        Place(grid, _progress, 2);
        Place(grid, _status, 3);
        Place(grid, foot, 4);
        return grid;
    }

    private static void Place(Grid grid, UIElement child, int row)
    {
        Grid.SetRow(child, row);
        grid.Children.Add(child);
    }

    private static TextBlock Section(string text) => new()
    {
        Text = text,
        FontWeight = FontWeights.SemiBold,
        Margin = new Thickness(0, 12, 0, 4),
    };

    private static StackPanel Row(params UIElement[] children)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 0) };
        foreach (var child in children) row.Children.Add(child);
        return row;
    }

    private static TextBlock Label(string text, double left = 0) => new()
    {
        Text = text,
        VerticalAlignment = VerticalAlignment.Center,
        Margin = new Thickness(left, 0, 6, 0),
    };

    private ComboBox Combo(double width, string[] items)
    {
        var combo = new ComboBox { Width = width, HorizontalAlignment = HorizontalAlignment.Left, ItemsSource = items, SelectedIndex = 0 };
        _inputs.Add(combo);
        return combo;
    }

    private TextBox Box(string text, double width)
    {
        var box = new TextBox
        {
            Text = text,
            Width = width,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalContentAlignment = VerticalAlignment.Center,
        };
        _inputs.Add(box);
        return box;
    }

    private NumericSpinner Spinner(double value, double min, double max, double step, double width)
    {
        var spinner = new NumericSpinner
        {
            Minimum = min,
            Maximum = max,
            Step = step,
            DecimalPlaces = 0,
            Value = value,
            Width = width,
        };
        _inputs.Add(spinner);
        return spinner;
    }

    private static TextBlock Hint() => new()
    {
        Foreground = Brushes.Gray,
        TextWrapping = TextWrapping.Wrap,
        Margin = new Thickness(0, 2, 0, 0),
    };

    private Button MakeButton(string text, Action run)
    {
        var button = new Button
        {
            Content = text,
            Padding = new Thickness(12, 4, 12, 4),
            Margin = new Thickness(0, 0, 8, 0),
        };
        button.Click += (_, _) => run();
        _inputs.Add(button);
        return button;
    }

    // ── 동영상 열기 ─────────────────────────────────────────────────────────

    private void OpenFile()
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title = "줄일 동영상 고르기",
            Filter = VideoShrinker.FileFilter,
        };
        if (dlg.ShowDialog(this) != true) return;

        Load(dlg.FileName);
    }

    private async void Load(string path)
    {
        if (!File.Exists(path))
        {
            _status.Text = $"{path} 를 찾지 못했습니다";
            return;
        }

        if (!VideoShrinker.IsSupported(path))
        {
            _status.Text = $"{Path.GetFileName(path)} 는 이 도구가 열 수 있는 동영상이 아닙니다";
            return;
        }

        _path = path;
        _probe = null;
        _lastOutput = null;
        _pathText.Text = path;
        _pathText.ToolTip = path;
        _status.Foreground = Brushes.Black;
        _status.Text = "동영상을 읽는 중…";
        OpenInPlayer(path, "원본");
        UpdateInfo();
        UpdateEnabled();

        int token = ++_probeToken;
        try
        {
            var probe = await Task.Run(() => VideoShrinker.ProbeAsync(path));
            if (token != _probeToken) return;   // 그새 딴 동영상으로 넘어갔다

            _probe = probe;
            _status.Text = "";
        }
        catch (Exception ex)
        {
            if (token != _probeToken) return;

            _status.Foreground = Brushes.Firebrick;
            _status.Text = $"{Path.GetFileName(path)} 를 읽지 못했습니다 — {ex.Message}";
        }

        UpdateInfo();
        UpdateEnabled();
    }

    private void OnDragOver(object sender, DragEventArgs e)
    {
        e.Effects = !_running && e.Data.GetDataPresent(DataFormats.FileDrop)
            ? DragDropEffects.Copy
            : DragDropEffects.None;
        e.Handled = true;
    }

    private void OnDrop(object sender, DragEventArgs e)
    {
        if (_running) return;

        if (e.Data.GetData(DataFormats.FileDrop) is string[] { Length: > 0 } paths)
        {
            Load(paths[0]);
            if (paths.Length > 1) _status.Text = $"한 번에 한 편만 다룹니다 — {Path.GetFileName(paths[0])} 만 열었습니다.";
        }

        e.Handled = true;
    }

    // ── 미리 보기 ───────────────────────────────────────────────────────────

    private void OpenInPlayer(string path, string label)
    {
        SetPlaying(false);
        _player.Close();
        _playButton.IsEnabled = false;
        _playingText.Text = $"{label} · {Path.GetFileName(path)}";
        _player.Source = new Uri(path);

        // Manual 이면 부르기 전에는 열지도 않는다. 틀었다 곧장 세워 첫 장면을 띄운다.
        _player.Play();
        _player.Pause();
    }

    private void OnMediaOpened()
    {
        _player.Position = TimeSpan.Zero;
        _playButton.IsEnabled = !_running;
    }

    private void TogglePlay()
    {
        if (_player.Source == null) return;

        if (_playing) _player.Pause();
        else _player.Play();

        SetPlaying(!_playing);
    }

    private void SetPlaying(bool playing)
    {
        _playing = playing;
        _playButton.Content = playing ? "❚❚ 멈춤" : "▶ 재생";
    }

    private void PlayResult()
    {
        if (_lastOutput is not { } output || !File.Exists(output)) return;

        OpenInPlayer(output, "만든 것");
        _player.IsMuted = false;   // 소리를 뺐는지 제 귀로 들어 보게 한다
    }

    // ── 설정 읽기 ───────────────────────────────────────────────────────────

    private VideoShrinker.SizeMode Mode => (VideoShrinker.SizeMode)Math.Max(0, _mode.SelectedIndex);

    private VideoShrinker.Quality Quality => (VideoShrinker.Quality)Math.Max(0, _quality.SelectedIndex);

    private VideoShrinker.Destination Where => (VideoShrinker.Destination)Math.Max(0, _where.SelectedIndex);

    private void OnModeChanged()
    {
        bool percent = Mode == VideoShrinker.SizeMode.Percent;
        _amount.Maximum = percent ? 100 : 16000;
        _amount.Step = percent ? 5 : 10;
        _amount.Value = percent ? 50 : Mode == VideoShrinker.SizeMode.Height ? 720 : 1280;
        _amountUnit.Text = percent ? "%" : "px";
        UpdateEnabled();
        UpdateInfo();
    }

    private void OnAudioChanged()
    {
        // 원본을 틀 때는 소리를 뺀 결과가 어떨지 들려준다.
        _player.IsMuted = _removeAudio.IsChecked == true;
        UpdateInfo();
    }

    private void OnWhereChanged()
    {
        _suffix.IsEnabled = !_running && Where == VideoShrinker.Destination.NextToSource;
        _folderButton.IsEnabled = !_running && Where == VideoShrinker.Destination.Folder;
        _folderText.Text = Where == VideoShrinker.Destination.Folder
            ? _folder ?? "폴더를 아직 안 골랐습니다"
            : "결과는 늘 MP4(H.264) 로 씁니다";
    }

    private void PickFolder()
    {
        var dlg = new Microsoft.Win32.OpenFolderDialog { Title = "줄인 동영상을 놓을 폴더" };
        if (dlg.ShowDialog(this) != true) return;

        _folder = dlg.FolderName;
        OnWhereChanged();
    }

    private VideoShrinker.Options PeekOptions()
    {
        int amount = (int)Math.Round(_amount.Value);
        return new VideoShrinker.Options
        {
            Mode = Mode,
            Pixels = Math.Max(2, amount),
            Percent = Mode == VideoShrinker.SizeMode.Percent ? Math.Clamp(amount, 1, 100) : 100,
            RemoveAudio = _removeAudio.IsChecked == true,
            Quality = Quality,
            BitrateKbps = (int)Math.Round(_bitrate.Value),
            Where = Where,
            Folder = _folder,
            Suffix = _suffix.Text.Trim(),
        };
    }

    private VideoShrinker.Options? ReadOptions()
    {
        var options = PeekOptions();

        if (Where == VideoShrinker.Destination.Folder && string.IsNullOrWhiteSpace(_folder))
        {
            _status.Text = "저장할 폴더를 먼저 골라 주세요";
            return null;
        }

        if (Where == VideoShrinker.Destination.NextToSource && options.Suffix.Length == 0 &&
            string.Equals(Path.GetExtension(_path), ".mp4", StringComparison.OrdinalIgnoreCase))
        {
            _status.Text = "꼬리말이 비면 원본 MP4 와 이름이 겹칩니다 — 꼬리말을 적어 주세요";
            return null;
        }

        return options;
    }

    /// <summary>줄이면 크기·비트레이트·용량이 얼마쯤 되는지 적어 준다.</summary>
    private void UpdateInfo()
    {
        var options = PeekOptions();

        _audioHint.Text = "소리 트랙을 통째로 뺀다 — 효과음·말소리도 같이 빠진다";

        _qualityHint.Text = options.Quality switch
        {
            VideoShrinker.Quality.High => "원본에 가깝게 — 용량은 덜 준다",
            VideoShrinker.Quality.Small => "용량을 크게 줄인다 — 빠른 장면은 뭉개질 수 있다",
            VideoShrinker.Quality.Bitrate => "적은 값을 그대로 쓴다 — 원본보다 크게 적으면 오히려 커진다",
            _ => "화질과 용량의 중간",
        };

        if (_path == null || _probe is not { } probe)
        {
            _previewInfo.Text = "";
            return;
        }

        var (tw, th) = VideoShrinker.TargetSize(probe.Width, probe.Height, options);
        uint videoBitrate = VideoShrinker.TargetVideoBitrate(probe, tw, th, options);
        bool keepsAudio = VideoShrinker.KeepsAudio(probe, options);

        string fps = probe.FrameRate > 0 ? $" · {probe.FrameRate:0.##}fps" : "";
        string sourceBitrate = probe.VideoBitrate > 0 ? $" · {probe.VideoBitrate / 1000} kbps" : "";
        string audio = probe.HasAudio ? "소리 있음" : "소리 없음";

        var text = $"원본 {probe.Width}×{probe.Height}{fps} · {Clock(probe.Duration)}{sourceBitrate} · {audio} · {ImageShrinkDialog.Human(probe.Bytes)}";
        text += $"\n→ {tw}×{th} · 영상 {videoBitrate / 1000} kbps · {(keepsAudio ? $"소리 {VideoShrinker.AudioBitrate / 1000} kbps" : "소리 없음")}";
        text += $" · 어림 {ImageShrinkDialog.Human(VideoShrinker.EstimateBytes(probe, options))}";

        if (options.Mode != VideoShrinker.SizeMode.Keep && tw >= (probe.Width & ~1) && th >= (probe.Height & ~1))
            text += "\n원본이 이미 그만큼 작아 크기는 안 바뀝니다 — 늘리지는 않습니다.";

        _previewInfo.Text = text;
    }

    private static string Clock(TimeSpan time) =>
        time.TotalHours >= 1 ? time.ToString(@"h\:mm\:ss") : time.ToString(@"m\:ss");

    // ── 돌리기 ──────────────────────────────────────────────────────────────

    private async Task SaveAsync()
    {
        if (_running || _path is not { } path) return;

        var options = ReadOptions();
        if (options == null) return;

        _player.Pause();
        SetPlaying(false);

        using var cancel = new CancellationTokenSource();
        _cancel = cancel;
        SetRunning(true);
        _status.Foreground = Brushes.Black;
        _status.Text = "줄이는 중…";
        _progress.Value = 0;

        var progress = new Progress<double>(value =>
        {
            if (!_running) return;
            _progress.Value = value;
            _status.Text = $"줄이는 중… {value * 100:F0}%";
        });

        var result = await Task.Run(() => VideoShrinker.ShrinkAsync(path, options, progress, cancel.Token));

        _cancel = null;
        SetRunning(false);
        Report(result);

        if (_closeWhenDone) Close();
    }

    private void Report(VideoShrinker.Result result)
    {
        // 멈춘 변환기는 임시 파일을 놓지 않는다 — 못 지웠으면 어디 남았는지 알려 준다.
        string leftover = result.LeftoverPath == null
            ? ""
            : $"\n쓰던 임시 파일을 변환기가 붙들고 있어 못 지웠습니다 — 앱을 닫은 뒤 지워 주세요: {result.LeftoverPath}";

        if (result.Canceled)
        {
            _status.Foreground = Brushes.Black;
            _status.Text = leftover.Length == 0 ? "그만두었습니다 — 쓰던 파일은 치웠습니다." : "그만두었습니다." + leftover;
            return;
        }

        if (result.Error != null)
        {
            _status.Foreground = Brushes.Firebrick;
            _status.Text = $"줄이지 못했습니다 — {result.Error}{leftover}";
            return;
        }

        _status.Foreground = Brushes.Black;
        _lastOutput = result.OutputPath;
        UpdateEnabled();

        var text = $"저장했습니다 · {result.OutputWidth}×{result.OutputHeight}";
        if (result.AudioRemoved) text += " · 소리 뺌";
        text += $" · {ImageShrinkDialog.Human(result.SourceBytes)} → {ImageShrinkDialog.Human(result.OutputBytes)}";
        if (result.SourceBytes > 0 && result.OutputBytes > 0)
        {
            text += result.Saved >= 0
                ? $" ({result.Saved * 100:F0}% 줄임)"
                : $" (오히려 {-result.Saved * 100:F0}% 커짐 — 화질을 낮추거나 크기를 더 줄여 보세요)";
        }

        text += $" · {Clock(result.Elapsed)} 걸림\n{result.OutputPath}";
        _status.Text = text;
    }

    private void RevealLast()
    {
        if (_lastOutput is not { } target || !File.Exists(target)) return;

        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"/select,\"{target}\"",
                UseShellExecute = true,
            });
        }
        catch (Exception ex)
        {
            _status.Text = $"탐색기를 열지 못했습니다 — {ex.Message}";
        }
    }

    private void SetRunning(bool running)
    {
        _running = running;
        foreach (var input in _inputs) input.IsEnabled = !running;
        _playButton.IsEnabled = !running && _player.Source != null;
        _cancelButton.Visibility = running ? Visibility.Visible : Visibility.Collapsed;
        _progress.Visibility = running ? Visibility.Visible : Visibility.Collapsed;

        if (!running)
        {
            OnWhereChanged();
            UpdateEnabled();
        }
    }

    private void UpdateEnabled()
    {
        _saveButton.IsEnabled = !_running && _path != null && _probe != null;
        _revealButton.IsEnabled = !_running && _lastOutput != null;
        _playResultButton.IsEnabled = !_running && _lastOutput != null;
        _amount.IsEnabled = !_running && Mode != VideoShrinker.SizeMode.Keep;
        _bitrate.IsEnabled = !_running && Quality == VideoShrinker.Quality.Bitrate;
    }
}
