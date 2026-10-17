using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using CdsHelper.Game.Local.Helpers;

namespace CdsHelper.Game.UI.Views;

/// <summary>
/// 괴물 관리 — 해전 괴물 넷의 몸짓(떠 있음 · 덮침)을 장 번호 · 시간 · 밀기 차례로 고치고 돌려 보는 창.
/// 일기토 몸짓을 고치는 「모션 메이커」(<see cref="MotionMakerDialog"/>)와 같은 수다.
/// </summary>
/// <remarks>
/// 고친 것은 <see cref="MonsterMotions"/> 의 표 파일(<c>asset/scombat/monster-motion.json</c>)이 되고, 해전이 그것을 읽어 돈다.
/// 장 번호는 방향 안의 0~4 다 — 여섯 방향이 같은 차례를 쓴다. 아래 그림 줄을 누르면 그 장을 차례 끝에 더한다.
/// </remarks>
public sealed class MonsterMotionDialog : GameWindow
{
    /// <summary>표의 한 줄 — 칸을 고치면 곧바로 돌려 보기에 든다.</summary>
    private sealed class Row
    {
        public int Frame { get; set; }
        public double Seconds { get; set; }
        public double Push { get; set; }
    }

    private readonly CombatArt? _art = CombatArt.Open();
    private readonly List<MonsterMotions.Set> _sets;
    private readonly ListBox _kinds = new() { Width = 140, Margin = new Thickness(10, 10, 6, 10) };
    private readonly ComboBox _motion = new() { Width = 120, Margin = new Thickness(0, 0, 10, 0) };
    private readonly ComboBox _way = new() { Width = 80, Margin = new Thickness(0, 0, 10, 0) };
    private readonly ObservableCollection<Row> _rows = [];
    private readonly DataGrid _grid = new()
    {
        AutoGenerateColumns = false, CanUserAddRows = false, CanUserDeleteRows = false, CanUserSortColumns = false,
        HeadersVisibility = DataGridHeadersVisibility.Column, Height = 220, Width = 330, Margin = new Thickness(0, 6, 0, 6),
    };
    private readonly StackPanel _strip = new() { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0) };
    private readonly Canvas _stage = new() { Width = 320, Height = 260, Background = new SolidColorBrush(Color.FromRgb(0x18, 0x3C, 0x6A)), ClipToBounds = true };
    private readonly Image _preview = new() { Width = 192, Height = 192 };
    private readonly TextBlock _status = new() { Margin = new Thickness(10, 4, 10, 8), TextWrapping = TextWrapping.Wrap };
    private readonly DispatcherTimer _timer = new(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(20) };
    private readonly System.Diagnostics.Stopwatch _clock = System.Diagnostics.Stopwatch.StartNew();
    private bool _filling;

    private MonsterMotionDialog()
    {
        Title = "괴물 관리";
        Width = 960;
        Height = 620;
        Background = Brushes.White;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        _sets = [.. MonsterMotions.All.Select(s => s with { Motions = [.. s.Motions] })];
        for (int k = 0; k < MonsterMotions.KindNames.Length; k++) _kinds.Items.Add(MonsterMotions.KindNames[k]);
        foreach (var m in MonsterMotions.Defaults()) _motion.Items.Add(new ComboBoxItem { Content = m.Name, Tag = m.Key });
        for (int w = 0; w < 6; w++) _way.Items.Add($"방향 {w}");

        _grid.Columns.Add(new DataGridTextColumn { Header = "장(0~4)", Binding = new System.Windows.Data.Binding(nameof(Row.Frame)), Width = 80 });
        _grid.Columns.Add(new DataGridTextColumn { Header = "시간(초)", Binding = new System.Windows.Data.Binding(nameof(Row.Seconds)), Width = 110 });
        _grid.Columns.Add(new DataGridTextColumn { Header = "밀기(점)", Binding = new System.Windows.Data.Binding(nameof(Row.Push)), Width = 110 });
        _grid.ItemsSource = _rows;
        _grid.CellEditEnding += (_, _) => Dispatcher.BeginInvoke(Keep, DispatcherPriority.Background);

        Canvas.SetLeft(_preview, 64);
        Canvas.SetTop(_preview, 34);
        _stage.Children.Add(_preview);

        var head = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 4) };
        head.Children.Add(Label("몸짓"));
        head.Children.Add(_motion);
        head.Children.Add(Label("보기"));
        head.Children.Add(_way);

        var buttons = new WrapPanel();
        foreach (var (text, act) in new (string, Action)[]
                 {
                     ("줄 지우기", RemoveRow), ("위로", () => MoveRow(-1)), ("아래로", () => MoveRow(+1)),
                     ("이 몸짓 밑값으로", ResetMotion), ("저장", Save),
                 })
        {
            var b = new Button { Content = text, Padding = new Thickness(10, 2, 10, 2), Margin = new Thickness(0, 0, 6, 4) };
            b.Click += (_, _) => act();
            buttons.Children.Add(b);
        }

        var editor = new StackPanel { Margin = new Thickness(6, 10, 10, 6) };
        editor.Children.Add(head);
        editor.Children.Add(_grid);
        editor.Children.Add(buttons);
        editor.Children.Add(new TextBlock { Text = "그림을 누르면 그 장을 차례 끝에 더합니다:", Margin = new Thickness(0, 8, 0, 0) });
        editor.Children.Add(_strip);

        var right = new StackPanel { Margin = new Thickness(10) };
        right.Children.Add(new Border { BorderBrush = Brushes.Gray, BorderThickness = new Thickness(1), Child = _stage });
        right.Children.Add(new TextBlock
        {
            Width = 320, TextWrapping = TextWrapping.Wrap, Foreground = Brushes.DimGray, Margin = new Thickness(0, 6, 0, 0),
            Text = "몸짓을 고치면 바로 위에서 돌아갑니다(두 배 크기). 밀기는 괴물이 보는 쪽으로 내미는 점입니다 — 원본은 0 입니다."
                   + " 「저장」하면 해전이 다음 판부터 이 몸짓으로 그립니다.",
        });

        var body = new DockPanel();
        DockPanel.SetDock(_kinds, Dock.Left);
        DockPanel.SetDock(right, Dock.Right);
        body.Children.Add(_kinds);
        body.Children.Add(right);
        body.Children.Add(editor);

        var page = new DockPanel();
        DockPanel.SetDock(_status, Dock.Bottom);
        page.Children.Add(_status);
        page.Children.Add(body);
        Content = page;

        _kinds.SelectionChanged += (_, _) => { Fill(); Strip(); };
        _motion.SelectionChanged += (_, _) => Fill();
        _way.SelectionChanged += (_, _) => Strip();
        _timer.Tick += (_, _) => Animate();
        Loaded += (_, _) =>
        {
            _filling = true;
            _kinds.SelectedIndex = 1;   // 크라켄
            _motion.SelectedIndex = 0;
            _way.SelectedIndex = 2;
            _filling = false;
            Fill();
            Strip();
            _timer.Start();
            Tell(_art == null ? CombatArt.LastError : MonsterMotions.LastError.Length > 0 ? MonsterMotions.LastError : $"표 파일: {MonsterMotions.Path_()}");
        };
        Closed += (_, _) => _timer.Stop();
    }

    private static TextBlock Label(string text) => new() { Text = text, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) };

    private void Tell(string text) => _status.Text = text;

    private int Kind => Math.Max(0, _kinds.SelectedIndex);
    private string Key => (_motion.SelectedItem as ComboBoxItem)?.Tag as string ?? MonsterMotions.Idle;
    private int Way => Math.Max(0, _way.SelectedIndex);

    /// <summary>고른 괴물 · 몸짓을 표에 채운다.</summary>
    private void Fill()
    {
        if (_filling) return;
        var motion = _sets[Kind].Motions.First(m => m.Key == Key);
        _rows.Clear();
        foreach (var s in motion.Steps) _rows.Add(new Row { Frame = s.Frame, Seconds = s.Seconds, Push = s.Push });
        _clock.Restart();
    }

    /// <summary>표의 줄들을 고른 몸짓에 들인다.</summary>
    private void Keep()
    {
        var steps = _rows.Select(r => new MonsterMotions.Step(Math.Clamp(r.Frame, 0, MonsterMotions.FramesPerWay - 1),
                                                              Math.Max(0.01, r.Seconds), r.Push)).ToArray();
        var set = _sets[Kind];
        _sets[Kind] = set with { Motions = [.. set.Motions.Select(m => m.Key == Key ? m with { Steps = steps } : m)] };
        _clock.Restart();
    }

    /// <summary>고른 방향의 다섯 장을 늘어놓는다 — 누르면 차례 끝에 더한다.</summary>
    private void Strip()
    {
        _strip.Children.Clear();
        if (_art == null) return;
        for (int f = 0; f < MonsterMotions.FramesPerWay; f++)
        {
            int frame = f;
            var cell = new StackPanel { Margin = new Thickness(0, 0, 4, 0), Cursor = System.Windows.Input.Cursors.Hand };
            cell.Children.Add(new Image { Width = 64, Height = 64, Source = Load(_art.Monster(Kind, Way * MonsterMotions.FramesPerWay + f)) });
            cell.Children.Add(new TextBlock { Text = $"{f}" + (f < 3 ? " 덮침" : " 떠 있음"), HorizontalAlignment = HorizontalAlignment.Center });
            cell.MouseLeftButtonUp += (_, _) =>
            {
                _rows.Add(new Row { Frame = frame, Seconds = Key == MonsterMotions.Strike ? 0.06 : 0.8, Push = 0 });
                Keep();
            };
            _strip.Children.Add(cell);
        }
    }

    /// <summary>돌려 보기 — 고친 차례를 처음부터 되풀이한다. 밀기는 오른쪽 아래로 내미는 것으로 보인다.</summary>
    private void Animate()
    {
        if (_art == null) return;
        var motion = _sets[Kind].Motions.First(m => m.Key == Key);
        var step = motion.AtLooped(_clock.Elapsed.TotalSeconds);
        _preview.Source = Load(_art.Monster(Kind, Way * MonsterMotions.FramesPerWay + Math.Clamp(step.Frame, 0, MonsterMotions.FramesPerWay - 1)));
        Canvas.SetLeft(_preview, 64 + step.Push * 2 * 0.7);
        Canvas.SetTop(_preview, 34 + step.Push * 2 * 0.7);
    }

    private void RemoveRow()
    {
        if (_grid.SelectedItem is not Row row) return;
        _rows.Remove(row);
        Keep();
    }

    private void MoveRow(int by)
    {
        if (_grid.SelectedItem is not Row row) return;
        int at = _rows.IndexOf(row), to = at + by;
        if (to < 0 || to >= _rows.Count) return;
        _rows.Move(at, to);
        _grid.SelectedItem = row;
        Keep();
    }

    private void ResetMotion()
    {
        var made = MonsterMotions.Defaults().First(m => m.Key == Key);
        var set = _sets[Kind];
        _sets[Kind] = set with { Motions = [.. set.Motions.Select(m => m.Key == Key ? made : m)] };
        Fill();
        Tell($"{MonsterMotions.KindNames[Kind]} 「{made.Name}」 을 원본 밑값으로 돌렸습니다 — 「저장」을 눌러야 적힙니다.");
    }

    private void Save()
    {
        _grid.CommitEdit(DataGridEditingUnit.Row, true);
        Keep();
        if (_sets.Any(s => s.Motions.Any(m => m.Steps.Length == 0))) { Tell("빈 몸짓이 있습니다 — 장을 하나 이상 넣어 주세요."); return; }
        Tell(MonsterMotions.Save(_sets) is { } why ? $"적지 못했습니다 — {why}" : $"적었습니다 — {MonsterMotions.Path_()}");
    }

    private static readonly Dictionary<string, BitmapSource> Cache = [];

    private static BitmapSource? Load(string? path)
    {
        if (path == null) return null;
        if (Cache.TryGetValue(path, out var hit)) return hit;
        var bmp = new BitmapImage();
        bmp.BeginInit();
        bmp.UriSource = new Uri(System.IO.Path.GetFullPath(path));
        bmp.CacheOption = BitmapCacheOption.OnLoad;
        bmp.EndInit();
        bmp.Freeze();
        Cache[path] = bmp;
        return bmp;
    }

    /// <summary>창을 띄운다.</summary>
    public static void Show(Window? owner)
    {
        var window = new MonsterMotionDialog();
        if (owner != null) window.Owner = owner;
        window.ShowDialog();
    }
}
