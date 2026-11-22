using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using CdsHelper.Game.Local.Helpers;
using CdsHelper.Support.Local.Helpers;
using CdsHelper.Support.Local.Settings;
using Rectangle = System.Windows.Shapes.Rectangle;

namespace CdsHelper.Game.UI.Views;

/// <summary>
/// 도구 「도시 이미지 메이커」 — 문화권 · 배경 · 테두리 · 건물을 골라 도시 그림을 새로 짜 맞춘다(<see cref="CityComposer"/>).
/// </summary>
/// <remarks>
/// 배경 · 테두리 · 건물은 모두 원본 도시 그림 226장에서 뽑은 겹이다. 모드 「도시 그림 업스케일링」이 받아 둔 그림이 있으면
/// 「4배로 보기」에서 그 그림의 겹을 쓴다. 건물은 끌어 옮기고 오른쪽 단추(또는 Delete)로 뺀다.
/// </remarks>
public sealed class CityImageMakerDialog : Window
{
    private const int Preview = 2;   // 미리보기는 1배 점의 두 배 크기

    /// <summary>건물 목록에 늘어놓을 그림의 최소 점 수.</summary>
    private const int MinIconPoints = 150;

    private CityComposer? _composer;
    private readonly List<CityComposer.Placed> _placed = [];
    private int _selected = -1;
    private (int Index, Point Grab, int X, int Y)? _drag;

    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8) };
    private readonly ComboBox _culture = new();
    private readonly ComboBox _background = new();
    private readonly ComboBox _frame = new();
    private readonly ListBox _icons = new() { Height = 300 };
    private readonly CheckBox _hd = new() { Content = "4배(업스케일 그림)로 보기", Margin = new Thickness(0, 8, 0, 0) };
    private readonly Image _view = new() { Width = CityComposer.W * Preview, Height = CityComposer.H * Preview, Stretch = Stretch.Fill };
    private readonly Canvas _overlay = new() { Width = CityComposer.W * Preview, Height = CityComposer.H * Preview, Background = Brushes.Transparent };
    private readonly Rectangle _mark = new() { Stroke = Brushes.Yellow, StrokeThickness = 2, StrokeDashArray = [3, 2], IsHitTestVisible = false, Visibility = Visibility.Collapsed };
    private readonly List<Control> _needsData = [];
    private bool _syncing;

    private CityImageMakerDialog()
    {
        Title = "도시 이미지 메이커";
        Width = 1240;
        Height = 820;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        RenderOptions.SetBitmapScalingMode(_view, BitmapScalingMode.HighQuality);
        _overlay.Children.Add(_mark);

        var left = new StackPanel { Width = 330, Margin = new Thickness(10) };
        left.Children.Add(_status);
        left.Children.Add(Label("문화권 — 고르면 그 문화권 첫 도시를 배경 · 테두리 · 건물로 깐다"));
        left.Children.Add(_culture);
        left.Children.Add(Label("배경 — 이 도시 그림에서 건물 · 테두리를 걷어 낸 풍경 (★ 업스케일 그림 있음)"));
        left.Children.Add(_background);
        left.Children.Add(Label("테두리"));
        left.Children.Add(_frame);
        left.Children.Add(Label("건물 — 두 번 누르면 그림 가운데에 놓는다"));
        left.Children.Add(_icons);
        var add = Button("건물 더하기", AddSelectedIcon);
        var reset = Button("배경 도시의 건물로 되돌리기", ResetBuildings);
        var clear = Button("건물 모두 빼기", () => { _placed.Clear(); _selected = -1; Render(); });
        left.Children.Add(add);
        left.Children.Add(reset);
        left.Children.Add(clear);
        left.Children.Add(_hd);
        var save = Button("PNG 로 저장…", SavePicture);
        var export = Button("건물 · 테두리 겹 모두 내보내기(4배)", ExportLayers);
        left.Children.Add(save);
        left.Children.Add(export);
        left.Children.Add(new TextBlock
        {
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 0), Foreground = Brushes.DimGray,
            Text = "그림 위 건물을 끌어 옮기고, 오른쪽 단추나 Delete 로 뺍니다. 그 갈래에서 혼자인 건물(고유 시설 따위)은 그림을 못 뽑아 배경에 남습니다.",
        });
        _needsData.AddRange([_culture, _background, _frame, _icons, add, reset, clear, _hd, save, export]);
        foreach (var c in _needsData) c.IsEnabled = false;

        var picture = new Grid { HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 10, 10, 10) };
        picture.Children.Add(_view);
        picture.Children.Add(_overlay);

        var root = new DockPanel { Background = Brushes.White };
        DockPanel.SetDock(left, Dock.Left);
        root.Children.Add(new ScrollViewer { Content = left, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        root.Children.Add(new Border { BorderBrush = Brushes.Gray, BorderThickness = new Thickness(1), Child = picture,
                                       HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 10, 10, 10) });
        Content = root;

        _culture.SelectionChanged += (_, _) => { if (!_syncing) CultureChanged(); };
        _background.SelectionChanged += (_, _) => { if (!_syncing) BackgroundChanged(); };
        _frame.SelectionChanged += (_, _) => { if (!_syncing) Render(); };
        _hd.Checked += (_, _) => Render();
        _hd.Unchecked += (_, _) => Render();
        _icons.MouseDoubleClick += (_, _) => AddSelectedIcon();
        _overlay.MouseLeftButtonDown += OnDown;
        _overlay.MouseMove += OnMove;
        _overlay.MouseLeftButtonUp += OnUp;
        _overlay.MouseRightButtonDown += (_, e) =>
        {
            int hit = HitTest(e.GetPosition(_overlay));
            if (hit < 0) return;
            _placed.RemoveAt(hit);
            _selected = -1;
            Render();
        };
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Delete && _selected >= 0 && _selected < _placed.Count)
            {
                _placed.RemoveAt(_selected);
                _selected = -1;
                Render();
            }
        };
        Loaded += async (_, _) => await LoadAsync();
    }

    private static TextBlock Label(string text) => new() { Text = text, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 2), FontWeight = FontWeights.SemiBold };

    private static Button Button(string text, Action run)
    {
        var b = new Button { Content = text, Margin = new Thickness(0, 6, 0, 0), Padding = new Thickness(8, 3, 8, 3) };
        b.Click += (_, _) => run();
        return b;
    }

    private sealed record CityItem(int Id, string Text) { public override string ToString() => Text; }

    private sealed record IconItem(BuildingSprites.Sprite Sprite);

    private async Task LoadAsync()
    {
        string dir = Path.GetDirectoryName(AppSettings.LastSaveFilePath) ?? "";
        var table = CityBuildingTable.Open(dir);
        var pictures = dir.Length > 0 ? CityPictures.Open(dir) : null;
        if (table == null || pictures == null)
        {
            _status.Text = "게임 폴더의 도시 그림(CITYCG.CDS) · 건물표를 못 읽었습니다 — 세이브 파일을 먼저 열어 주세요.";
            return;
        }
        var cities = CityTable.Open();
        _status.Text = "도시 그림 226장에서 건물 · 테두리를 가르는 중… (몇 초 걸립니다)";
        try { _composer = await Task.Run(() => CityComposer.Build(pictures, table, cities)); }
        catch (Exception ex)
        {
            _status.Text = $"겹을 가르지 못했습니다 — {ex.Message}";
            return;
        }

        var c = _composer;
        _status.Text = c.HdCount < c.Count
            ? $"건물 그림 {c.Sprites.Count}가지 · 테두리 {c.Frames.Count}가지를 뽑았습니다. 업스케일 그림 {c.HdCount}/{c.Count}장 — 없는 도시는 점을 네 배로 늘려 씁니다(모드 「도시 그림 업스케일링」을 켜 두면 받습니다)."
            : $"건물 그림 {c.Sprites.Count}가지 · 테두리 {c.Frames.Count}가지를 뽑았습니다. 업스케일 그림 {c.HdCount}/{c.Count}장.";
        _syncing = true;
        // 「기타」(원주민 마을 따위)는 맨 뒤로.
        foreach (var culture in Enumerable.Range(0, c.Count).Where(c.HasPicture).Select(c.CultureOf).Distinct()
                                          .OrderBy(x => x == "기타" ? 1 : 0).ThenBy(x => x))
            _culture.Items.Add(culture);
        _frame.Items.Add("없음");
        foreach (var f in c.Frames) _frame.Items.Add(f);
        _syncing = false;
        foreach (var ctl in _needsData) ctl.IsEnabled = true;
        _hd.IsEnabled = c.HdCount > 0;
        _hd.IsChecked = c.HdCount > 0;
        _culture.SelectedIndex = 0;
    }

    private void CultureChanged()
    {
        if (_composer is not { } c || _culture.SelectedItem is not string culture) return;
        _syncing = true;
        _background.Items.Clear();
        // 고른 문화권 도시를 앞에, 나머지를 뒤에 — 배경은 문화권을 넘나들어도 된다.
        var ids = Enumerable.Range(0, c.Count).Where(c.HasPicture)
                            .OrderBy(id => c.CultureOf(id) == culture ? 0 : 1).ThenBy(id => c.CultureOf(id)).ThenBy(id => id);
        foreach (int id in ids)
            _background.Items.Add(new CityItem(id, $"{(c.HasHd(id) ? "★ " : "")}{c.Cities.NameOf(id)} · {c.CultureOf(id)}"));
        _icons.Items.Clear();
        // 남은 점이 너무 적은 그림(맞대 보니 거의 다 빠진 것)은 쓸 데가 없어 늘어놓지 않는다.
        foreach (var s in c.Sprites.Where(s => s.Points >= MinIconPoints)
                                   .OrderBy(s => s.Culture == culture ? 0 : 1).ThenBy(s => s.Culture).ThenBy(s => s.Kind))
            _icons.Items.Add(IconRow(s));
        _syncing = false;
        _background.SelectedIndex = 0;
    }

    private ListBoxItem IconRow(BuildingSprites.Sprite s)
    {
        var (bgra, alpha) = _composer!.Icon(s, 1);
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(new Image
        {
            Source = CityComposer.ToBitmap(bgra, alpha, BuildingSprites.BoxW, BuildingSprites.BoxH),
            Width = BuildingSprites.BoxW / 2.0, Height = BuildingSprites.BoxH / 2.0, Margin = new Thickness(0, 0, 6, 0),
        });
        row.Children.Add(new TextBlock { Text = s.ToString(), VerticalAlignment = VerticalAlignment.Center });
        return new ListBoxItem { Content = row, Tag = new IconItem(s) };
    }

    private void BackgroundChanged()
    {
        if (_composer is not { } c || _background.SelectedItem is not CityItem city) return;
        _syncing = true;
        _frame.SelectedItem = c.FrameOf(city.Id) is { } f ? f : _frame.Items[0];
        _syncing = false;
        ResetBuildings();
    }

    private void ResetBuildings()
    {
        if (_composer is not { } c || _background.SelectedItem is not CityItem city) return;
        _placed.Clear();
        _placed.AddRange(c.PlacedOf(city.Id));
        _selected = -1;
        Render();
    }

    private void AddSelectedIcon()
    {
        if ((_icons.SelectedItem as ListBoxItem)?.Tag is not IconItem item) return;
        _placed.Add(new CityComposer.Placed(item.Sprite, (CityComposer.W - BuildingSprites.BoxW) / 2, (CityComposer.H - BuildingSprites.BoxH) / 2));
        _selected = _placed.Count - 1;
        Render();
    }

    private int Scale => _hd.IsChecked == true ? CityComposer.Hd : 1;

    private void Render(bool quick = false)
    {
        if (_composer is not { } c || _background.SelectedItem is not CityItem city) return;
        int scale = quick ? 1 : Scale;
        var old = Cursor;
        Cursor = Cursors.Wait;
        try
        {
            var px = c.Compose(city.Id, _frame.SelectedItem as CityComposer.FrameStyle, _placed, scale);
            _view.Source = CityComposer.ToBitmap(px, CityComposer.W * scale, CityComposer.H * scale);
        }
        finally { Cursor = old; }
        Mark();
    }

    private void Mark()
    {
        if (_selected < 0 || _selected >= _placed.Count) { _mark.Visibility = Visibility.Collapsed; return; }
        var p = _placed[_selected];
        Canvas.SetLeft(_mark, p.X * Preview);
        Canvas.SetTop(_mark, p.Y * Preview);
        _mark.Width = BuildingSprites.BoxW * Preview;
        _mark.Height = BuildingSprites.BoxH * Preview;
        _mark.Visibility = Visibility.Visible;
    }

    /// <summary>그 자리를 누르면 잡히는 건물 — 앞에 그린(아래쪽) 것부터, 상자 가운데 절반을 본다(원본 누르는 자리와 같다).</summary>
    private int HitTest(Point at)
    {
        double x = at.X / Preview, y = at.Y / Preview;
        foreach (int i in Enumerable.Range(0, _placed.Count).OrderByDescending(i => _placed[i].Y))
        {
            var p = _placed[i];
            if (x >= p.X + BuildingSprites.BoxW / 4.0 && x < p.X + BuildingSprites.BoxW * 3 / 4.0
                && y >= p.Y + BuildingSprites.BoxH / 4.0 && y < p.Y + BuildingSprites.BoxH * 3 / 4.0)
                return i;
        }
        return -1;
    }

    private void OnDown(object sender, MouseButtonEventArgs e)
    {
        var at = e.GetPosition(_overlay);
        _selected = HitTest(at);
        Focus();
        if (_selected >= 0)
        {
            _drag = (_selected, at, _placed[_selected].X, _placed[_selected].Y);
            _overlay.CaptureMouse();
        }
        Mark();
    }

    private void OnMove(object sender, MouseEventArgs e)
    {
        if (_drag is not { } d || e.LeftButton != MouseButtonState.Pressed) return;
        var at = e.GetPosition(_overlay);
        int nx = d.X + (int)Math.Round((at.X - d.Grab.X) / Preview), ny = d.Y + (int)Math.Round((at.Y - d.Grab.Y) / Preview);
        var p = _placed[d.Index];
        if (p.X == nx && p.Y == ny) return;
        _placed[d.Index] = p with { X = nx, Y = ny };
        // 끄는 동안은 1배로 빨리 그리고, 놓으면 고른 배율로 다시 그린다.
        Render(quick: true);
    }

    private void OnUp(object sender, MouseButtonEventArgs e)
    {
        if (_drag == null) return;
        _drag = null;
        _overlay.ReleaseMouseCapture();
        Render();
    }

    private void SavePicture()
    {
        if (_composer is not { } c || _background.SelectedItem is not CityItem city) return;
        int scale = Scale;
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Filter = "PNG|*.png",
            FileName = $"city-{city.Id:000}-{CityComposer.W * scale}x{CityComposer.H * scale}.png",
        };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            var px = c.Compose(city.Id, _frame.SelectedItem as CityComposer.FrameStyle, _placed, scale);
            CityComposer.SavePng(CityComposer.ToBitmap(px, CityComposer.W * scale, CityComposer.H * scale), dialog.FileName);
            _status.Text = $"저장했습니다: {dialog.FileName}";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(this, $"저장하지 못했습니다:\n{ex.Message}", Title);
        }
    }

    private async void ExportLayers()
    {
        if (_composer is not { } c) return;
        string folder = UserDataFolder.Combine("citylayers");
        _status.Text = "겹을 내보내는 중…";
        IsEnabled = false;
        try
        {
            int n = await Task.Run(() => c.ExportLayers(folder, CityComposer.Hd,
                                                        (done, total) => Dispatcher.BeginInvoke(() => _status.Text = $"겹을 내보내는 중… {done}/{total}")));
            _status.Text = $"{n}장을 적었습니다: {folder}";
            Process.Start(new ProcessStartInfo(folder) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _status.Text = $"내보내지 못했습니다 — {ex.Message}";
        }
        finally { IsEnabled = true; }
    }

    /// <summary>창을 연다.</summary>
    public static void Show(Window owner) =>
        new CityImageMakerDialog { Owner = GameWindow.Alive(owner) }.ShowDialog();
}
