using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using CdsHelper.Support.Local.Helpers;
using CdsHelper.Support.UI.Units;

namespace CdsHelper.Main.UI.Views;

/// <summary>
/// 그림 한 장을 눈으로 보면서 줄이고, 시키면 격자로 나눠 주는 창.
/// </summary>
/// <remarks>
/// 실제로 줄이고 나누는 일은 <see cref="ImageShrinker"/> 가 한다 — 여기는 보여 주고 고르기만 한다.
/// 한 번에 한 장만 다룬다. 설정을 만질 때마다 미리 보기를 새로 푸는데, 글자를 칠 때마다 풀면
/// 손이 무거우니 잠깐 묵혔다가(<see cref="_debounce"/>) 한 번만 푼다.
/// </remarks>
public sealed class ImageShrinkDialog : Window
{
    private const int RefreshDelayMs = 250;
    private const double TileSize = 92;
    private const double GalleryHeight = 128;

    private readonly PreviewCanvas _preview;
    private readonly ScrollViewer _previewScroll;
    private readonly TextBlock _previewInfo;
    private readonly TextBlock _pathText;
    private readonly TextBlock _status;
    private readonly CheckBox _asResult;
    private readonly CheckBox _actualSize;

    private readonly ComboBox _mode;
    private readonly NumericSpinner _amount;
    private readonly TextBlock _amountUnit;
    private readonly NumericSpinner _rowsBox;
    private readonly NumericSpinner _columnsBox;
    private readonly TextBlock _splitHint;
    private readonly ComboBox _padMode;
    private readonly NumericSpinner _padX;
    private readonly NumericSpinner _padY;
    private readonly ComboBox _padFill;
    private readonly TextBlock _padHint;
    private readonly ComboBox _format;
    private readonly NumericSpinner _quality;
    private readonly ComboBox _where;
    private readonly TextBox _suffix;
    private readonly Button _folderButton;
    private readonly TextBlock _folderText;
    private readonly Button _saveButton;
    private readonly Button _revealButton;

    private readonly ItemsControl _gallery;
    private readonly TextBlock _galleryHead;
    private readonly Grid _galleryHost;

    private readonly List<Control> _inputs = [];
    private readonly DispatcherTimer _debounce;

    private string? _path;
    private string? _folder;
    private string? _lastOutput;
    private int _sourceWidth;
    private int _sourceHeight;
    private int _previewToken;
    private int _galleryToken;
    private bool _running;

    public ImageShrinkDialog()
    {
        Title = "이미지 크기 줄이기";
        Width = 1100;
        Height = 720;
        MinWidth = 820;
        MinHeight = 540;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        AllowDrop = true;

        // 차례가 ImageShrinker.SizeMode 와 하나씩 맞물린다 — 손대면 양쪽을 같이 고쳐야 한다.
        _mode = Combo(230, [
            "가로를 맞춘다 (px)",
            "한 칸 가로를 맞춘다 (px)",
            "세로를 맞춘다 (px)",
            "긴 변을 맞춘다 (px)",
            "비율로 줄인다 (%)",
        ]);
        _amount = Spinner(1280, 1, 100000, 10, 92);
        _amountUnit = new TextBlock { Text = "px", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 0, 0) };

        _rowsBox = Spinner(1, 1, ImageShrinker.MaxSplit, 1, 66);
        _columnsBox = Spinner(1, 1, ImageShrinker.MaxSplit, 1, 66);
        _splitHint = Hint();

        // 차례가 ImageShrinker.PadMode 와 하나씩 맞물린다.
        _padMode = Combo(230, ["여백 없음", "정사각형이 되도록", "좌우·상하 직접"]);
        _padX = Spinner(0, 0, 4000, 4, 66);
        _padY = Spinner(0, 0, 4000, 4, 66);
        _padFill = Combo(230, ["투명 (JPEG 는 흰색)", "흰색", "검정"]);
        _padHint = Hint();

        _format = Combo(230, ["원본 형식 그대로", "JPEG", "PNG"]);
        _quality = Spinner(85, 1, 100, 5, 72);

        _where = Combo(230, ["원본 옆에 저장", "다른 폴더에 저장", "원본 덮어쓰기"]);
        _suffix = Box("_small", 80);
        _folderButton = MakeButton("폴더…", PickFolder);
        _folderText = new TextBlock
        {
            Foreground = Brushes.Gray,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 2, 0, 0),
        };

        _asResult = new CheckBox { Content = "줄인 결과로 보기", VerticalAlignment = VerticalAlignment.Center };
        _actualSize = new CheckBox { Content = "실제 크기(100%)", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(14, 0, 0, 0) };

        _preview = new PreviewCanvas();
        _previewScroll = new ScrollViewer
        {
            Content = _preview,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Background = Brushes.Transparent,
        };
        _previewInfo = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0), Foreground = Brushes.DimGray };
        _pathText = new TextBlock
        {
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = Brushes.Gray,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Text = "연 그림이 없습니다 — 파일을 열거나 창에 끌어다 놓으세요.",
        };
        _status = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0) };

        _galleryHead = new TextBlock { FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 4) };
        _gallery = new ItemsControl { ItemsPanel = HorizontalStrip() };
        _galleryHost = BuildGallery();

        _saveButton = MakeButton("줄여서 저장", () => _ = SaveAsync());
        _revealButton = MakeButton("만든 파일 보기", RevealLast);
        _revealButton.IsEnabled = false;

        Content = BuildContent();

        _mode.SelectionChanged += (_, _) => OnModeChanged();
        _where.SelectionChanged += (_, _) => OnWhereChanged();
        _asResult.Checked += (_, _) => RefreshPreview();
        _asResult.Unchecked += (_, _) => RefreshPreview();
        _actualSize.Checked += (_, _) => ApplyZoom();
        _actualSize.Unchecked += (_, _) => ApplyZoom();

        foreach (var spinner in new[] { _amount, _rowsBox, _columnsBox, _padX, _padY, _quality })
            spinner.ValueChanged += (_, _) => QueueRefresh();

        _format.SelectionChanged += (_, _) => QueueRefresh();
        _padFill.SelectionChanged += (_, _) => QueueRefresh();
        _padMode.SelectionChanged += (_, _) => OnPadChanged();

        _debounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(RefreshDelayMs) };
        _debounce.Tick += (_, _) => { _debounce.Stop(); RefreshPreview(); };

        DragOver += OnDragOver;
        Drop += OnDrop;
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape && !_running) Close();
        };

        // 돌리는 중에 닫으면 남은 일이 주인 없는 창을 두드린다 — 끝나야 닫힌다.
        Closing += (_, e) =>
        {
            if (!_running) return;

            e.Cancel = true;
            _status.Text = "아직 줄이는 중입니다 — 끝나면 닫힙니다.";
        };

        OnWhereChanged();
        OnPadChanged();
        UpdateInfo();
        UpdateEnabled();
    }

    // ── 화면 짜기 ───────────────────────────────────────────────────────────

    private UIElement BuildContent()
    {
        var top = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
        top.Children.Add(MakeButton("파일 열기…", OpenFile));
        top.Children.Add(_pathText);

        var previewHead = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 6) };
        previewHead.Children.Add(new TextBlock { Text = "미리 보기", FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center });
        previewHead.Children.Add(new TextBlock { Text = "  ·  나눈 자리는 주황 선으로 그린다", Foreground = Brushes.Gray, VerticalAlignment = VerticalAlignment.Center });
        previewHead.Children.Add(new StackPanel { Width = 16 });
        previewHead.Children.Add(_asResult);
        previewHead.Children.Add(_actualSize);

        var frame = new Border
        {
            BorderBrush = Brushes.LightGray,
            BorderThickness = new Thickness(1),
            Background = new SolidColorBrush(Color.FromRgb(0xF7, 0xF7, 0xF7)),
            Child = _previewScroll,
        };

        var left = new Grid();
        left.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        left.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        left.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        Place(left, previewHead, 0);
        Place(left, frame, 1);
        Place(left, _previewInfo, 2);

        var options = new StackPanel { Margin = new Thickness(12, 0, 0, 0) };
        options.Children.Add(Section("줄이기"));
        options.Children.Add(_mode);
        options.Children.Add(Row(_amount, _amountUnit));

        options.Children.Add(Section("나누기"));
        options.Children.Add(Row(_rowsBox, Label("행", 6), Label("×", 8), _columnsBox, Label("열", 6)));
        options.Children.Add(_splitHint);

        options.Children.Add(Section("여백"));
        options.Children.Add(_padMode);
        options.Children.Add(Row(_padX, Label("좌우", 6), _padY, Label("상하", 6)));
        options.Children.Add(_padFill);
        options.Children.Add(_padHint);

        options.Children.Add(Section("형식"));
        options.Children.Add(_format);
        options.Children.Add(Row(Label("JPEG 품질"), _quality));

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
        foot.Children.Add(_revealButton);
        foot.Children.Add(_saveButton);
        var close = MakeButton("닫기", Close);
        close.Margin = new Thickness(0);   // 줄 끝이라 오른쪽 여백을 뗀다
        foot.Children.Add(close);

        var grid = new Grid { Margin = new Thickness(12) };
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        Place(grid, top, 0);
        Place(grid, body, 1);
        Place(grid, _status, 2);
        Place(grid, _galleryHost, 3);
        Place(grid, foot, 4);
        return grid;
    }

    /// <summary>조각을 한 줄로 늘어놓는 판. 넘치면 옆으로 굴린다.</summary>
    private static ItemsPanelTemplate HorizontalStrip()
    {
        var factory = new FrameworkElementFactory(typeof(StackPanel));
        factory.SetValue(StackPanel.OrientationProperty, Orientation.Horizontal);
        return new ItemsPanelTemplate(factory);
    }

    /// <summary>만든 조각을 늘어놓을 자리. 만들기 전에는 자리를 안 차지한다.</summary>
    private Grid BuildGallery()
    {
        var strip = new ScrollViewer
        {
            Content = _gallery,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Height = GalleryHeight,
            Padding = new Thickness(0, 0, 0, 4),
        };

        var host = new Grid { Margin = new Thickness(0, 8, 0, 0), Visibility = Visibility.Collapsed };
        host.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        host.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        Place(host, _galleryHead, 0);
        Place(host, strip, 1);
        return host;
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
            DecimalPlaces = 0,   // 여기 숫자는 다 픽셀·칸 수라 소수점이 없다
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

    private Button MakeButton(string text, Action run, string? tip = null)
    {
        var button = new Button
        {
            Content = text,
            Padding = new Thickness(12, 4, 12, 4),
            Margin = new Thickness(0, 0, 8, 0),
            ToolTip = tip,
        };
        button.Click += (_, _) => run();
        _inputs.Add(button);
        return button;
    }

    // ── 그림 열기 ───────────────────────────────────────────────────────────

    private void OpenFile()
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title = "줄일 그림 고르기",
            Filter = ImageShrinker.FileFilter,
        };
        if (dlg.ShowDialog(this) != true) return;

        Load(dlg.FileName);
    }

    private void Load(string path)
    {
        if (!File.Exists(path))
        {
            _status.Text = $"{path} 를 찾지 못했습니다";
            return;
        }

        if (!ImageShrinker.IsSupported(path))
        {
            _status.Text = $"{Path.GetFileName(path)} 는 이 도구가 열 수 있는 그림이 아닙니다";
            return;
        }

        _path = path;
        _lastOutput = null;
        _revealButton.IsEnabled = false;
        ClearGallery();
        _pathText.Text = path;
        _pathText.ToolTip = path;
        _status.Text = "";
        RefreshPreview();
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

        // 한 장짜리 도구다 — 여럿을 떨어뜨리면 첫 장만 받는다.
        if (e.Data.GetData(DataFormats.FileDrop) is string[] { Length: > 0 } paths)
        {
            Load(paths[0]);
            if (paths.Length > 1) _status.Text = $"한 번에 한 장만 다룹니다 — {Path.GetFileName(paths[0])} 만 열었습니다.";
        }

        e.Handled = true;
    }

    // ── 설정 읽기 ───────────────────────────────────────────────────────────

    private ImageShrinker.SizeMode Mode => (ImageShrinker.SizeMode)Math.Max(0, _mode.SelectedIndex);

    private ImageShrinker.Destination Where => (ImageShrinker.Destination)Math.Max(0, _where.SelectedIndex);

    private void OnModeChanged()
    {
        // 퍼센트는 100 이 위끝이고 픽셀은 훨씬 크다 — 눈금을 방식에 맞춰 갈아 끼운다.
        bool percent = Mode == ImageShrinker.SizeMode.Percent;
        _amount.Maximum = percent ? 100 : 100000;
        _amount.Step = percent ? 5 : 10;
        _amount.Value = Mode switch
        {
            ImageShrinker.SizeMode.Percent => 50,
            ImageShrinker.SizeMode.CellWidth => 400,
            _ => 1280,
        };

        _amountUnit.Text = Mode switch
        {
            ImageShrinker.SizeMode.Percent => "%",
            ImageShrinker.SizeMode.CellWidth => "px  (칸마다)",
            _ => "px",
        };

        QueueRefresh();
    }

    private ImageShrinker.PadMode Pad => (ImageShrinker.PadMode)Math.Max(0, _padMode.SelectedIndex);

    private void OnPadChanged()
    {
        bool fixedPad = Pad == ImageShrinker.PadMode.Fixed;
        _padX.IsEnabled = _padY.IsEnabled = !_running && fixedPad;
        _padFill.IsEnabled = !_running && Pad != ImageShrinker.PadMode.None;
        QueueRefresh();
    }

    private void OnWhereChanged()
    {
        _suffix.IsEnabled = !_running && Where == ImageShrinker.Destination.NextToSource;
        _folderButton.IsEnabled = !_running && Where == ImageShrinker.Destination.Folder;
        _folderText.Text = Where switch
        {
            ImageShrinker.Destination.Folder => _folder ?? "폴더를 아직 안 골랐습니다",
            ImageShrinker.Destination.Overwrite => "원본을 지우고 그 자리에 씁니다 — 되돌릴 수 없고, 나눌 때는 쓸 수 없습니다",
            _ => "",
        };
        _folderText.Foreground = Where == ImageShrinker.Destination.Overwrite ? Brushes.Firebrick : Brushes.Gray;
    }

    private void PickFolder()
    {
        var dlg = new Microsoft.Win32.OpenFolderDialog { Title = "줄인 그림을 놓을 폴더" };
        if (dlg.ShowDialog(this) != true) return;

        _folder = dlg.FolderName;
        OnWhereChanged();
    }

    /// <summary>미리 보기를 그리는 데 쓸 설정. 값이 덜 적혔어도 어떻게든 하나 내놓는다.</summary>
    private ImageShrinker.Options PeekOptions()
    {
        // 스피너가 이미 눈금 안으로 다잡아 주므로 여기서는 그대로 옮겨 담기만 한다.
        bool percent = Mode == ImageShrinker.SizeMode.Percent;
        int amount = (int)Math.Round(_amount.Value);

        return new ImageShrinker.Options
        {
            Mode = Mode,
            Pixels = Math.Max(1, amount),
            Percent = percent ? Math.Clamp(amount, 1, 100) : 100,
            Rows = (int)Math.Round(_rowsBox.Value),
            Columns = (int)Math.Round(_columnsBox.Value),
            Pad = Pad,
            PadX = (int)Math.Round(_padX.Value),
            PadY = (int)Math.Round(_padY.Value),
            PadFill = (ImageShrinker.PadColor)Math.Max(0, _padFill.SelectedIndex),
            Format = (ImageShrinker.OutputFormat)Math.Max(0, _format.SelectedIndex),
            JpegQuality = (int)Math.Round(_quality.Value),
            Where = Where,
            Folder = _folder,
            Suffix = _suffix.Text.Trim(),
        };
    }

    /// <summary>정말 돌릴 설정. 못 할 조합이면 알려 주고 null 을 준다.</summary>
    /// <remarks>
    /// 숫자는 스피너가 눈금 안으로 다잡아 주므로 하나하나 따질 게 없다. 여기서는 숫자끼리·
    /// 설정끼리 안 맞물리는 것만 본다.
    /// </remarks>
    private ImageShrinker.Options? ReadOptions()
    {
        var options = PeekOptions();

        if (Where == ImageShrinker.Destination.Folder && string.IsNullOrWhiteSpace(_folder))
        {
            _status.Text = "저장할 폴더를 먼저 골라 주세요";
            return null;
        }

        if (Where == ImageShrinker.Destination.NextToSource && options.Suffix.Length == 0)
        {
            _status.Text = "꼬리말이 비면 원본을 덮어씁니다 — 꼬리말을 적거나 덮어쓰기를 고르세요";
            return null;
        }

        if (options.Splits && Where == ImageShrinker.Destination.Overwrite)
        {
            _status.Text = "나눈 조각을 원본 한 자리에 넣을 수는 없습니다 — 다른 저장 위치를 고르세요";
            return null;
        }

        // 조각이 픽셀보다 많을 수는 없다.
        var (tw, th) = ImageShrinker.TargetSize(_sourceWidth, _sourceHeight, options);
        if (tw > 0 && (options.Columns > tw || options.Rows > th))
        {
            _status.Text = $"줄이면 {tw}×{th} 인데 {options.Rows}행 {options.Columns}열로는 못 나눕니다";
            return null;
        }

        return options;
    }

    // ── 미리 보기 ───────────────────────────────────────────────────────────

    private void QueueRefresh()
    {
        UpdateInfo();
        _debounce.Stop();
        _debounce.Start();
    }

    private async void RefreshPreview()
    {
        UpdateInfo();

        if (_path is not { } path)
        {
            _preview.Show(null, 1, 1);
            return;
        }

        var options = PeekOptions();
        bool asResult = _asResult.IsChecked == true;
        int token = ++_previewToken;

        try
        {
            var preview = await Task.Run(() => ImageShrinker.LoadPreview(path, options, asResult));
            if (token != _previewToken) return;   // 그새 딴 그림·딴 설정으로 넘어갔다

            _sourceWidth = preview.SourceWidth;
            _sourceHeight = preview.SourceHeight;
            _preview.Show(preview.Image, options.Rows, options.Columns, PadInPreview(preview, options));
            ApplyZoom();
            UpdateInfo();
        }
        catch (Exception ex)
        {
            if (token != _previewToken) return;

            _preview.Show(null, 1, 1);
            _status.Text = $"{Path.GetFileName(path)} 를 열지 못했습니다 — {ex.Message}";
        }
    }

    /// <summary>
    /// 두를 여백이 미리 보기 그림에서 몇 픽셀에 해당하는지 셈한다.
    /// </summary>
    /// <remarks>
    /// 여백은 줄인 뒤 크기로 잰 값인데, 미리 보기 그림은 원본이거나 줄인 결과라 눈금이 다르다.
    /// 그림 쪽 눈금으로 바꿔 넘겨야 점선이 제 자리에 그려진다.
    /// </remarks>
    private static (double X, double Y) PadInPreview(ImageShrinker.Preview preview, ImageShrinker.Options options)
    {
        if (options.Pad == ImageShrinker.PadMode.None) return (0, 0);

        var columns = ImageShrinker.Slices(preview.TargetWidth, options.Columns);
        var rows = ImageShrinker.Slices(preview.TargetHeight, options.Rows);
        var (padWidth, padHeight) = ImageShrinker.PieceSize(columns, rows, options);

        double scale = (double)preview.Image.PixelWidth / Math.Max(1, preview.TargetWidth);
        return ((padWidth - columns[0].Length) / 2.0 * scale, (padHeight - rows[0].Length) / 2.0 * scale);
    }

    /// <summary>미리 보기를 창에 맞출지, 실제 크기로 둘지 정한다.</summary>
    private void ApplyZoom()
    {
        bool actual = _actualSize.IsChecked == true && _preview.Image != null;
        if (actual)
        {
            _preview.Width = _preview.Image!.PixelWidth;
            _preview.Height = _preview.Image.PixelHeight;
            _previewScroll.HorizontalScrollBarVisibility = ScrollBarVisibility.Auto;
            _previewScroll.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        }
        else
        {
            _preview.Width = double.NaN;
            _preview.Height = double.NaN;
            _previewScroll.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
            _previewScroll.VerticalScrollBarVisibility = ScrollBarVisibility.Disabled;
        }
    }

    /// <summary>줄이면 몇 픽셀이 되고 몇 장으로 갈리는지, 여백까지 두르면 얼마가 되는지 적어 준다.</summary>
    private void UpdateInfo()
    {
        var options = PeekOptions();
        int pieces = options.Rows * options.Columns;

        _splitHint.Text = pieces > 1
            ? $"{options.Rows}행 {options.Columns}열 → {pieces}장"
            : "1행 1열이면 안 나눈다";

        _padHint.Text = options.Pad switch
        {
            ImageShrinker.PadMode.Square => "짧은 쪽에 여백을 둘러 조각을 정사각형으로 만든다",
            ImageShrinker.PadMode.Fixed => "조각 둘레에 이만큼씩 더 붙인다 (좌우는 양쪽 각각)",
            _ => "여백을 안 두르면 잘린 그대로다",
        };

        if (_path == null || _sourceWidth <= 0)
        {
            _previewInfo.Text = "";
            return;
        }

        var (tw, th) = ImageShrinker.TargetSize(_sourceWidth, _sourceHeight, options);
        var columns = ImageShrinker.Slices(tw, options.Columns);
        var rows = ImageShrinker.Slices(th, options.Rows);
        int cellWidth = columns[0].Length, cellHeight = rows[0].Length;

        var text = $"원본 {_sourceWidth}×{_sourceHeight}  →  줄이면 {tw}×{th}";
        if (pieces > 1)
            text += $"  →  {rows.Count}행 {columns.Count}열, {rows.Count * columns.Count}장 (조각 {cellWidth}×{cellHeight})";

        if (options.Pad != ImageShrinker.PadMode.None)
        {
            var (padWidth, padHeight) = ImageShrinker.PieceSize(columns, rows, options);
            int sides = (padWidth - cellWidth) / 2, ends = (padHeight - cellHeight) / 2;
            text += sides > 0 || ends > 0
                ? $"\n여백을 두르면 조각 {padWidth}×{padHeight} (좌우 {sides}px, 상하 {ends}px)"
                : $"\n조각이 이미 {cellWidth}×{cellHeight} 라 두를 여백이 없습니다";
        }

        // 한 칸 가로를 시켰는데 원본이 그만큼 크지 않으면 시킨 대로 못 준다 — 키우지는 않으므로.
        if (options.Mode == ImageShrinker.SizeMode.CellWidth && cellWidth < options.Pixels)
            text += $"\n칸마다 {options.Pixels}px 를 시켰지만 원본이 작아 {cellWidth}px 까지입니다 — 늘리지는 않습니다.";

        _previewInfo.Text = text;
    }

    // ── 돌리기 ──────────────────────────────────────────────────────────────

    private async Task SaveAsync()
    {
        if (_running || _path is not { } path) return;

        var options = ReadOptions();
        if (options == null) return;

        // 덮어쓰기는 되돌릴 수 없으니 한 번 묻는다.
        if (options.Where == ImageShrinker.Destination.Overwrite &&
            MessageBox.Show(
                this,
                $"{Path.GetFileName(path)} 를 줄여서 원본 자리에 덮어씁니다.\n원본은 되돌릴 수 없습니다. 그대로 할까요?",
                "원본 덮어쓰기",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning,
                MessageBoxResult.No) != MessageBoxResult.Yes)
        {
            _status.Text = "덮어쓰기를 그만두었습니다.";
            return;
        }

        SetRunning(true);
        _status.Text = "줄이는 중…";

        var result = await Task.Run(() => ImageShrinker.Shrink(path, options));

        SetRunning(false);
        Report(result);
    }

    private void Report(ImageShrinker.Result result)
    {
        if (result.Error != null)
        {
            _status.Text = $"줄이지 못했습니다 — {result.Error}";
            _status.Foreground = Brushes.Firebrick;
            ClearGallery();
            return;
        }

        _status.Foreground = Brushes.Black;

        if (result.Skipped)
        {
            _status.Text = "줄일 것도 나눌 것도 없어 그냥 두었습니다.";
            ClearGallery();
            return;
        }

        ShowGallery(result.OutputPaths);

        _lastOutput = result.OutputPath;
        _revealButton.IsEnabled = _lastOutput != null;

        var text = result.PieceCount > 1
            ? $"{result.PieceCount}장으로 나눠 저장했습니다 · 조각 {result.PieceWidth}×{result.PieceHeight}"
            : $"저장했습니다 · {result.ScaledWidth}×{result.ScaledHeight}";

        text += $" · {Human(result.SourceBytes)} → {Human(result.OutputBytes)}";
        if (result.SourceBytes > 0 && result.OutputBytes > 0)
            text += $" ({result.Saved * 100:F0}% 줄임)";

        if (result.OutputPath != null)
        {
            text += result.PieceCount > 1
                ? $"\n{Path.GetDirectoryName(result.OutputPath)} 에 {Path.GetFileName(result.OutputPath)} 부터"
                : $"\n{result.OutputPath}";
        }

        _status.Text = text;

        // 덮어썼으면 방금 쓴 그림이 곧 원본이다 — 미리 보기를 다시 읽는다.
        if (result.OutputPath != null && string.Equals(result.OutputPath, _path, StringComparison.OrdinalIgnoreCase))
            RefreshPreview();
    }

    // ── 만든 조각 갤러리 ────────────────────────────────────────────────────

    /// <summary>
    /// 비쳐 보이는 자리가 드러나도록 깔아 두는 바둑판.
    /// </summary>
    /// <remarks>흰 바탕에 얹으면 투명 여백인지 흰 여백인지 눈으로 가릴 수 없다.</remarks>
    private static readonly Brush Checker = MakeChecker();

    private static Brush MakeChecker()
    {
        var cells = new GeometryGroup();
        cells.Children.Add(new RectangleGeometry(new Rect(0, 0, 8, 8)));
        cells.Children.Add(new RectangleGeometry(new Rect(8, 8, 8, 8)));

        var drawing = new DrawingGroup();
        drawing.Children.Add(new GeometryDrawing(Brushes.White, null, new RectangleGeometry(new Rect(0, 0, 16, 16))));
        drawing.Children.Add(new GeometryDrawing(new SolidColorBrush(Color.FromRgb(0xE4, 0xE4, 0xE4)), null, cells));

        var brush = new DrawingBrush(drawing)
        {
            TileMode = TileMode.Tile,
            Viewport = new Rect(0, 0, 16, 16),
            ViewportUnits = BrushMappingMode.Absolute,
            Stretch = Stretch.None,
        };
        brush.Freeze();
        return brush;
    }

    private void ClearGallery()
    {
        _galleryToken++;
        _gallery.Items.Clear();
        _galleryHost.Visibility = Visibility.Collapsed;
    }

    /// <summary>방금 만든 조각들을 작은 그림으로 늘어놓는다.</summary>
    private async void ShowGallery(List<string> paths)
    {
        ClearGallery();
        if (paths.Count == 0) return;

        int token = ++_galleryToken;
        _galleryHead.Text = $"만든 조각 {paths.Count}장 — 찍으면 탐색기에서 짚어 준다";
        _galleryHost.Visibility = Visibility.Visible;

        // 읽기는 딴 실에서 한꺼번에 한다. 얼려 오므로 그대로 걸어도 된다.
        var tiles = await Task.Run(() => paths.Select(path =>
        {
            try
            {
                return (Path: path, Image: (BitmapSource?)ImageShrinker.LoadThumbnail(path, (int)TileSize));
            }
            catch
            {
                return (Path: path, Image: null);
            }
        }).ToList());

        if (token != _galleryToken) return;   // 그새 딴 걸 만들었다

        foreach (var tile in tiles)
            _gallery.Items.Add(MakeTile(tile.Path, tile.Image));
    }

    private UIElement MakeTile(string path, BitmapSource? image)
    {
        var frame = new Border
        {
            Width = TileSize,
            Height = TileSize,
            Background = Checker,
            BorderBrush = Brushes.LightGray,
            BorderThickness = new Thickness(1),
            Child = image == null
                ? new TextBlock
                {
                    Text = "?",
                    Foreground = Brushes.Gray,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                }
                : new Image { Source = image, Stretch = Stretch.Uniform, Margin = new Thickness(2) },
        };

        var stack = new StackPanel { Margin = new Thickness(0, 0, 8, 0) };
        stack.Children.Add(frame);
        stack.Children.Add(new TextBlock
        {
            Text = TileLabel(path),
            FontSize = 10,
            Foreground = Brushes.DimGray,
            MaxWidth = TileSize,
            TextAlignment = TextAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Margin = new Thickness(0, 2, 0, 0),
        });

        var button = new Button
        {
            Content = stack,
            ToolTip = path,
            Padding = new Thickness(0),
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Cursor = Cursors.Hand,
        };
        button.Click += (_, _) => Reveal(path);
        return button;
    }

    /// <summary>이름표는 자리표(r1c2)만 뽑아 적는다 — 파일 이름을 통째로 적으면 넘친다.</summary>
    private static string TileLabel(string path)
    {
        string name = Path.GetFileNameWithoutExtension(path);
        int at = name.LastIndexOf("_r", StringComparison.Ordinal);
        return at >= 0 ? name[(at + 1)..] : name;
    }

    private void RevealLast()
    {
        if (_lastOutput is { } target) Reveal(target);
    }

    private void Reveal(string target)
    {
        if (!File.Exists(target)) return;

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
        Cursor = running ? Cursors.Wait : null;

        if (!running)
        {
            OnWhereChanged();
            UpdateEnabled();
        }
    }

    private void UpdateEnabled()
    {
        _saveButton.IsEnabled = !_running && _path != null;
        _revealButton.IsEnabled = !_running && _lastOutput != null;
    }

    /// <summary>바이트를 사람이 읽을 만한 단위로 적는다.</summary>
    internal static string Human(long bytes) => bytes switch
    {
        <= 0 => "",
        < 1024 => $"{bytes} B",
        < 1024 * 1024 => $"{bytes / 1024.0:F0} KB",
        _ => $"{bytes / (1024.0 * 1024.0):F1} MB",
    };

    /// <summary>
    /// 그림을 비율 그대로 담고, 나눌 자리에 선과 이름표를 얹어 그린다.
    /// </summary>
    /// <remarks>
    /// <see cref="Image"/> 에 <c>Stretch</c> 를 걸고 선을 따로 얹으면 여백 계산이 두 군데로 갈린다.
    /// 한 자리에서 그리면 선이 그림 밖으로 새는 일이 없다.
    /// </remarks>
    private sealed class PreviewCanvas : FrameworkElement
    {
        private static readonly Pen Edge = Freeze(new Pen(Brushes.OrangeRed, 1.5));
        private static readonly Pen Halo = Freeze(new Pen(Brushes.White, 3.5));
        private static readonly Brush Plate = Freeze(new SolidColorBrush(Color.FromArgb(0xB0, 0, 0, 0)));
        private static readonly Pen PadEdge = Freeze(new Pen(new SolidColorBrush(Color.FromRgb(0x10, 0x4F, 0x89)), 1)
        {
            DashStyle = new DashStyle([3, 3], 0),
        });
        private static readonly Typeface Face = new("Segoe UI");

        public BitmapSource? Image { get; private set; }
        private int _rows = 1;
        private int _columns = 1;
        private double _padX;
        private double _padY;

        public void Show(BitmapSource? image, int rows, int columns, (double X, double Y) pad = default)
        {
            Image = image;
            _rows = Math.Max(1, rows);
            _columns = Math.Max(1, columns);
            _padX = Math.Max(0, pad.X);
            _padY = Math.Max(0, pad.Y);
            InvalidateVisual();
        }

        protected override void OnRender(DrawingContext dc)
        {
            if (Image == null || RenderSize.Width <= 0 || RenderSize.Height <= 0) return;

            // 비율 그대로 담고 가운데에 놓는다. 늘리지는 않는다 — 작은 그림을 억지로 키워 봐야
            // 흐려지기만 하고, 줄인 결과를 볼 때는 1:1 로 보이는 편이 맞다.
            double scale = Math.Min(1, Math.Min(RenderSize.Width / Image.PixelWidth, RenderSize.Height / Image.PixelHeight));
            if (scale <= 0) return;

            double w = Image.PixelWidth * scale, h = Image.PixelHeight * scale;
            var rect = new Rect((RenderSize.Width - w) / 2, (RenderSize.Height - h) / 2, w, h);
            dc.DrawImage(Image, rect);

            DrawPads(dc, rect, scale);

            if (_rows <= 1 && _columns <= 1) return;

            // 흰 선을 깔고 그 위에 주황을 얹는다 — 어떤 그림 위에서도 보이게.
            foreach (var pen in new[] { Halo, Edge })
            {
                for (int c = 1; c < _columns; c++)
                {
                    double x = rect.X + w * c / _columns;
                    dc.DrawLine(pen, new Point(x, rect.Y), new Point(x, rect.Bottom));
                }

                for (int r = 1; r < _rows; r++)
                {
                    double y = rect.Y + h * r / _rows;
                    dc.DrawLine(pen, new Point(rect.X, y), new Point(rect.Right, y));
                }

                dc.DrawRectangle(null, pen, rect);
            }

            DrawTags(dc, rect);
        }

        /// <summary>두를 여백까지 넣으면 조각이 어디까지 커지는지 점선으로 그린다.</summary>
        private void DrawPads(DrawingContext dc, Rect rect, double scale)
        {
            if (_padX <= 0 && _padY <= 0) return;

            double cellW = rect.Width / _columns, cellH = rect.Height / _rows;
            double padX = _padX * scale, padY = _padY * scale;

            for (int r = 0; r < _rows; r++)
            {
                for (int c = 0; c < _columns; c++)
                {
                    dc.DrawRectangle(null, PadEdge, new Rect(
                        rect.X + cellW * c - padX,
                        rect.Y + cellH * r - padY,
                        cellW + padX * 2,
                        cellH + padY * 2));
                }
            }
        }

        /// <summary>칸마다 저장될 이름의 자리표를 적는다. 칸이 좁으면 접는다.</summary>
        private void DrawTags(DrawingContext dc, Rect rect)
        {
            double cellW = rect.Width / _columns, cellH = rect.Height / _rows;
            if (cellW < 46 || cellH < 24) return;

            double dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
            int rowDigits = _rows.ToString().Length, columnDigits = _columns.ToString().Length;

            for (int r = 0; r < _rows; r++)
            {
                for (int c = 0; c < _columns; c++)
                {
                    string tag = $"r{(r + 1).ToString().PadLeft(rowDigits, '0')}c{(c + 1).ToString().PadLeft(columnDigits, '0')}";
                    var text = new FormattedText(tag, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
                        Face, 11, Brushes.White, dpi);

                    var at = new Point(rect.X + cellW * c + 5, rect.Y + cellH * r + 4);
                    dc.DrawRoundedRectangle(Plate, null,
                        new Rect(at.X - 3, at.Y - 2, text.Width + 6, text.Height + 4), 3, 3);
                    dc.DrawText(text, at);
                }
            }
        }

        private static Pen Freeze(Pen pen) { pen.Freeze(); return pen; }
        private static Brush Freeze(Brush brush) { brush.Freeze(); return brush; }
    }
}
