using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CdsHelper.Game.Local.Helpers;

namespace CdsHelper.Game.UI.Views;

/// <summary>
/// 사람이 지어 넣는 발견물(<see cref="UserDiscoveries"/>)을 짓고 고치고, 파일 하나로 내보내고 불러오는 창 — 원본에 없는 것이다.
/// </summary>
/// <remarks>
/// 왼쪽은 지어 둔 것 목록, 오른쪽은 고를 때 채우는 칸이다. 자리는 「지도에서 고르기…」가 세계 지도를 띄워
/// 드래그로 잡는다(<see cref="DiscoveryAreaPicker"/>). 그림은 사진을 골라 발견물 그림처럼 바꿔(<see cref="DiscoveryStillFilter"/>) 넣는다.
/// 장면은 한 줄에 하나 — 「화자: 대사」, 「[그림]」, 「[발견]」(뒤에 글을 붙이면 그 글로 알린다).
/// </remarks>
public sealed class UserDiscoveryDialog : GameWindow
{
    /// <summary>자리를 고를 세계 지도. 지도를 못 읽었으면 null.</summary>
    private readonly Func<DiscoveryAreaPicker.Map?>? _map;

    private readonly ListBox _list = new() { Width = 220, Margin = new Thickness(10, 10, 6, 10) };
    private readonly StackPanel _form = new() { Margin = new Thickness(6, 10, 10, 4) };
    private readonly TextBlock _status = new() { Margin = new Thickness(10, 4, 10, 8), TextWrapping = TextWrapping.Wrap };
    private readonly Image _picture = new() { Width = 320, Height = 240, Stretch = Stretch.Uniform };

    private readonly TextBox _name = Box(), _reward = Box(), _x1 = Box(60), _y1 = Box(60), _x2 = Box(60), _y2 = Box(60), _chance = Box(60);
    private readonly ComboBox _category = new() { Width = 160 };
    private readonly CheckBox _land = new() { Content = "육지", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) };
    private readonly CheckBox _open = new() { Content = "처음부터 열림(힌트 없이도 발견)", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) };
    private readonly TextBox _hintName = Box(), _hintGrade = Box(60), _hintCategory = Box(60), _hintFunds = Box(), _hintDeadline = Box(60);
    private readonly TextBox _hintText = Area(60), _rumorCities = Box(), _rumorText = Area(48), _book = Box(), _description = Area(60);
    private readonly TextBox _scene = Area(120), _credit = Area(40), _author = Box();

    private UserDiscoveries.Entry? _editing;
    private (uint[] Bgra, int Width, int Height)? _newPicture;

    /// <summary>갈아 넣을 동영상 — null 이면 그대로, 빈 글이면 뺀다.</summary>
    private string? _newMovie;
    private readonly TextBlock _movie = new() { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0), Width = 320 };

    public UserDiscoveryDialog(Func<DiscoveryAreaPicker.Map?>? map)
    {
        _map = map;
        Title = "커스텀 발견물";
        Width = 1120;
        Height = 860;
        Background = Brushes.White;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        foreach (string name in DiscoveryTable.CategoryNames) _category.Items.Add(name);

        _form.Children.Add(Line("이름", _name, Label("갈래"), _category, Label("보수"), _reward));
        _form.Children.Add(Line("자리 X1", _x1, Label("Y1"), _y1, Label("X2"), _x2, Label("Y2"), _y2, _land,
                                Push("지도에서 고르기…", () => PickArea())));
        _form.Children.Add(Line("확률", _chance, Label("분의 1"), _open));
        _form.Children.Add(Line("힌트 이름", _hintName, Label("등급(1~3)"), _hintGrade, Label("갈래"), _hintCategory,
                                Label("자금"), _hintFunds, Label("기한(년)"), _hintDeadline));
        _form.Children.Add(Line("힌트 글", _hintText));
        _form.Children.Add(Line("소문 도시", _rumorCities, Label("도시 번호를 쉼표로(셋까지)"), Label("힌트를 실을 책"), _book));
        _form.Children.Add(Line("소문 글", _rumorText));
        _form.Children.Add(Line("설명", _description));
        _form.Children.Add(Line("장면", _scene));
        _form.Children.Add(new TextBlock
        {
            Margin = new Thickness(90, 0, 0, 6), Foreground = Brushes.DimGray, TextWrapping = TextWrapping.Wrap,
            Text = "한 줄에 하나 — 「부관: 제독！ 저기를 보십시오．」 · 「[그림]」 · 「[동영상]」 · 「[발견]」(뒤에 글을 붙이면 그 글로 알립니다, 비우면 「~을 발견했다！」)",
        });
        _form.Children.Add(Line("출처", _credit, Label("만든 이"), _author));
        _form.Children.Add(Line("그림", new Border { BorderBrush = Brushes.Gray, BorderThickness = new Thickness(1), Child = _picture },
                                Push("그림 고르기…", PickPicture)));
        _form.Children.Add(Line("동영상", _movie, Push("동영상 고르기…", PickMovie), Push("동영상 빼기", DropMovie)));

        var bar = new WrapPanel { Margin = new Thickness(10, 0, 10, 0) };
        foreach (var (text, act) in new (string, Action)[]
                 { ("새 발견물", Add), ("저장", Save), ("지우기", Remove), ("내보내기…", Export), ("불러오기…", Import) })
            bar.Children.Add(Push(text, act));

        var body = new DockPanel();
        DockPanel.SetDock(_list, Dock.Left);
        body.Children.Add(_list);
        body.Children.Add(new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = _form });

        var page = new DockPanel();
        DockPanel.SetDock(_status, Dock.Bottom);
        DockPanel.SetDock(bar, Dock.Bottom);
        page.Children.Add(_status);
        page.Children.Add(bar);
        page.Children.Add(body);
        Content = page;

        _list.SelectionChanged += (_, _) => { if (_list.SelectedItem is ListBoxItem { Tag: string uuid }) Show(uuid); };
        Loaded += (_, _) =>
        {
            Refresh(null);
            Tell(Local.Settings.GameSettings.ExtraDiscoveries ? "" : "모드 「추가 발견물」이 꺼져 있어 지은 발견물이 게임에 안 나옵니다.");
        };
    }

    private static TextBox Box(double width = 160) => new() { Width = width, Margin = new Thickness(0, 0, 8, 0), VerticalContentAlignment = VerticalAlignment.Center };

    private static TextBox Area(double height) => new()
    {
        Width = 720, Height = height, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap,
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
    };

    private static TextBlock Label(string text) => new() { Text = text, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) };

    private static Button Push(string text, Action act)
    {
        var button = new Button { Content = text, Padding = new Thickness(10, 2, 10, 2), Margin = new Thickness(0, 4, 6, 4) };
        button.Click += (_, _) => act();
        return button;
    }

    private static StackPanel Line(string head, params UIElement[] parts)
    {
        var line = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 3, 0, 3) };
        line.Children.Add(new TextBlock { Text = head, Width = 90, FontWeight = FontWeights.Bold, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 3, 0, 0) });
        foreach (var part in parts) line.Children.Add(part);
        return line;
    }

    private void Tell(string text) => _status.Text = text;

    /// <summary>목록을 다시 채우고 그 UUID 를 고른다.</summary>
    private void Refresh(string? pick)
    {
        _list.Items.Clear();
        foreach (var l in UserDiscoveries.All.OrderBy(l => l.Entry.Name, StringComparer.Ordinal))
            _list.Items.Add(new ListBoxItem { Content = $"{l.Numbers.Id}  {l.Entry.Name}", Tag = l.Entry.Uuid });
        var item = _list.Items.OfType<ListBoxItem>().FirstOrDefault(i => (string)i.Tag == pick) ?? _list.Items.OfType<ListBoxItem>().FirstOrDefault();
        if (item != null) _list.SelectedItem = item;
        else { _editing = null; _form.IsEnabled = false; }
    }

    private void Show(string uuid)
    {
        if (UserDiscoveries.All.FirstOrDefault(l => l.Entry.Uuid == uuid) is not { } l) return;
        var e = l.Entry;
        _editing = e;
        _newPicture = null;
        _newMovie = null;
        _movie.Text = l.MoviePath is { } clip
            ? $"{System.IO.Path.GetFileName(clip)}  ({new System.IO.FileInfo(clip).Length / 1048576.0:0.0}MB)" : "(없음)";
        _form.IsEnabled = true;
        _name.Text = e.Name;
        _category.SelectedIndex = Math.Clamp(e.Category, 0, _category.Items.Count - 1);
        _reward.Text = $"{e.Reward}";
        (_x1.Text, _y1.Text, _x2.Text, _y2.Text) = ($"{e.X1}", $"{e.Y1}", $"{e.X2}", $"{e.Y2}");
        _land.IsChecked = e.OnLand;
        _open.IsChecked = e.OpenAtStart;
        _chance.Text = $"{e.Chance}";
        _hintName.Text = e.HintName;
        _hintGrade.Text = $"{e.HintGrade}";
        _hintCategory.Text = $"{e.HintCategory}";
        _hintFunds.Text = $"{e.HintFunds}";
        _hintDeadline.Text = $"{e.HintDeadline}";
        _hintText.Text = e.HintText;
        _rumorCities.Text = string.Join(",", e.RumorCities ?? []);
        _rumorText.Text = e.RumorText;
        _book.Text = e.Book;
        _description.Text = e.Description;
        _scene.Text = string.Join("\n", (e.Lines ?? []).Select(SceneText));
        _credit.Text = e.Credit;
        _author.Text = e.Author;
        _picture.Source = l.PicturePath is { } path ? LoadPicture(path) : null;
        Tell($"발견물 {l.Numbers.Id}번 · 힌트 {l.Numbers.Hint}번 · 그림 {l.Numbers.Picture}번 — UUID {e.Uuid}");
    }

    private static BitmapImage? LoadPicture(string path)
    {
        try
        {
            var art = new BitmapImage();
            art.BeginInit();
            art.CacheOption = BitmapCacheOption.OnLoad;
            art.UriSource = new Uri(path);
            art.EndInit();
            art.Freeze();
            return art;
        }
        catch (Exception ex) when (ex is System.IO.IOException or NotSupportedException) { return null; }
    }

    private static string SceneText(UserDiscoveries.SceneLine line) => line.Kind switch
    {
        "picture" => "[그림]",
        "movie" => "[동영상]",
        "found" => line.Text.Length > 0 ? $"[발견] {line.Text}" : "[발견]",
        _ => string.IsNullOrWhiteSpace(line.Speaker) ? line.Text : $"{line.Speaker}: {line.Text}",
    };

    private static List<UserDiscoveries.SceneLine> ParseScene(string text)
    {
        var lines = new List<UserDiscoveries.SceneLine>();
        foreach (string raw in text.Split('\n'))
        {
            string s = raw.Trim();
            if (s.Length == 0) continue;
            if (s == "[그림]") lines.Add(new("picture"));
            else if (s == "[동영상]") lines.Add(new("movie"));
            else if (s.StartsWith("[발견]")) lines.Add(new("found", null, s[4..].Trim()));
            else if (s.IndexOf(':') is var at and > 0 and <= 12 && !s[..at].Contains(' '))
                lines.Add(new("say", s[..at].Trim(), s[(at + 1)..].Trim()));
            else lines.Add(new("say", null, s));
        }
        return lines;
    }

    /// <summary>칸들을 거둔다. 어긋난 칸이 있으면 null 이고 까닭을 아래에 적는다.</summary>
    private UserDiscoveries.Entry? Collect()
    {
        if (_editing is not { } e) return null;
        int? N(TextBox box, string what)
        {
            if (int.TryParse(box.Text.Trim(), out int v)) return v;
            Tell($"{what}에 수를 적어 주세요.");
            return null;
        }
        if (string.IsNullOrWhiteSpace(_name.Text)) { Tell("이름이 비었습니다."); return null; }
        if (N(_reward, "보수") is not { } reward || N(_x1, "X1") is not { } x1 || N(_y1, "Y1") is not { } y1
            || N(_x2, "X2") is not { } x2 || N(_y2, "Y2") is not { } y2 || N(_chance, "확률") is not { } chance
            || N(_hintGrade, "힌트 등급") is not { } grade || N(_hintCategory, "힌트 갈래") is not { } hintCategory
            || N(_hintFunds, "힌트 자금") is not { } funds || N(_hintDeadline, "힌트 기한") is not { } deadline)
            return null;
        var cities = new List<int>();
        foreach (string cell in _rumorCities.Text.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            if (!int.TryParse(cell, out int city)) { Tell("소문 도시는 도시 번호를 쉼표로 적어 주세요."); return null; }
            cities.Add(city);
        }
        if (cities.Count > 3) { Tell("소문 도시는 셋까지입니다."); return null; }

        return e with
        {
            Name = _name.Text.Trim(), Category = Math.Max(0, _category.SelectedIndex), Reward = Math.Max(0, reward),
            OnLand = _land.IsChecked == true, OpenAtStart = _open.IsChecked == true,
            X1 = x1, Y1 = y1, X2 = x2, Y2 = y2, Chance = Math.Max(1, chance),
            HintName = _hintName.Text.Trim(), HintGrade = Math.Clamp(grade, 1, 3), HintCategory = Math.Max(0, hintCategory),
            HintFunds = Math.Max(0, funds), HintDeadline = Math.Max(1, deadline), HintText = _hintText.Text.Trim(),
            RumorCities = [.. cities], RumorText = _rumorText.Text.Trim(), Book = _book.Text.Trim(),
            Description = _description.Text.Trim(), Lines = ParseScene(_scene.Text),
            Credit = _credit.Text.Trim(), Author = _author.Text.Trim(),
        };
    }

    private void Add()
    {
        var made = UserDiscoveries.Blank();
        if (UserDiscoveries.Save(made) is { } why) { Tell($"짓지 못했습니다 — {why}"); return; }
        Refresh(made.Uuid);
        // 자리부터 고른다 — 자리 없는 발견물은 어디서도 안 잡힌다.
        Tell(PickArea()
            ? "새 발견물을 지었습니다 — 나머지 칸을 채우고 「저장」을 눌러 주세요."
            : "새 발견물을 지었습니다 — 「지도에서 고르기…」로 자리를 잡고 칸을 채운 뒤 「저장」을 눌러 주세요.");
    }

    private void Save()
    {
        if (Collect() is not { } e) return;
        if (e.X1 < 0 || e.Y1 < 0) { Tell("자리가 없습니다 — 「지도에서 고르기…」로 자리를 잡아 주세요."); return; }
        if (UserDiscoveries.Save(e, _newPicture, _newMovie) is { } why) { Tell($"적지 못했습니다 — {why}"); return; }
        Refresh(e.Uuid);
        if (UserDiscoveries.All.FirstOrDefault(l => l.Entry.Uuid == e.Uuid) is { } l && UserDiscoveries.Compile(l, out string error) == null)
            Tell($"적었지만 장면을 굽지 못했습니다 — {error}");
        else Tell($"「{e.Name}」 을 적었습니다.");
    }

    private void Remove()
    {
        if (_editing is not { } e) return;
        if (!ConfirmDialog.Ask(this, $"「{e.Name}」 을 지웁니다. 이 발견물을 찾은 세이브에서는 그 기록이 표에 없는 것으로 남습니다. 좋습니까?", "발견물 지우기"))
            return;
        UserDiscoveries.Delete(e.Uuid);
        Refresh(null);
        Tell($"「{e.Name}」 을 지웠습니다.");
    }

    /// <summary>세계 지도를 띄워 자리를 고른다. 골랐으면 true.</summary>
    private bool PickArea()
    {
        if (_editing is not { } e) return false;
        if (_map?.Invoke() is not { } map) { Tell("세계 지도를 못 읽었습니다 — 게임에서 개발 창을 열어 주세요."); return false; }
        DiscoveryAreaPicker.Area? start = int.TryParse(_x1.Text, out int x1) && int.TryParse(_y1.Text, out int y1)
                                          && int.TryParse(_x2.Text, out int x2) && int.TryParse(_y2.Text, out int y2) && x1 >= 0 && y1 >= 0
            ? new(x1, y1, x2, y2, _land.IsChecked == true) : null;
        // 고치는 발견물 자신은 다른 발견물 자리에서 뺀다.
        var others = map.Others.Where(o => !(start is { } s && o.X1 == s.X1 && o.Y1 == s.Y1 && o.X2 == s.X2 && o.Y2 == s.Y2 && o.Name == e.Name)).ToList();
        if (DiscoveryAreaPicker.Pick(this, map with { Others = others }, start) is not { } a) return false;
        (_x1.Text, _y1.Text, _x2.Text, _y2.Text) = ($"{a.X1}", $"{a.Y1}", $"{a.X2}", $"{a.Y2}");
        _land.IsChecked = a.Land;
        Tell($"자리를 ({a.X1}, {a.Y1}) ~ ({a.X2}, {a.Y2}) 로 잡았습니다 — {(a.Land ? "육지" : "바다")}. 「저장」을 눌러야 적힙니다.");
        return true;
    }

    private void PickPicture()
    {
        if (_editing == null) return;
        var pick = new Microsoft.Win32.OpenFileDialog { Title = "발견물 그림 고르기", Filter = DiscoveryStillFiles.OpenFilter };
        if (pick.ShowDialog(this) != true) return;
        if (DiscoveryStillFilter.Apply(pick.FileName, out int w, out int h) is not { } bgra) { Tell("그림을 읽지 못했습니다."); return; }
        _newPicture = (bgra, w, h);
        var art = BitmapSource.Create(w, h, 96, 96, PixelFormats.Bgra32, null, bgra, w * 4);
        art.Freeze();
        _picture.Source = art;
        Tell("그림을 발견물 그림처럼 바꿨습니다 — 「저장」을 눌러야 적힙니다. 출처와 라이선스를 「출처」 칸에 적어 주세요.");
    }

    private void PickMovie()
    {
        if (_editing == null) return;
        var pick = new Microsoft.Win32.OpenFileDialog
        {
            Title = "발견물 동영상 고르기",
            Filter = "동영상|" + string.Join(";", MovieFiles.Extensions.Select(x => "*" + x)) + "|모든 파일|*.*",
        };
        if (pick.ShowDialog(this) != true) return;
        _newMovie = pick.FileName;
        _movie.Text = System.IO.Path.GetFileName(pick.FileName);
        Tell("동영상을 골랐습니다 — 장면에 「[동영상]」 줄을 넣어야 재생되고, 「저장」을 눌러야 적힙니다. 내보내면 파일에 함께 담깁니다.");
    }

    private void DropMovie()
    {
        if (_editing == null) return;
        _newMovie = "";
        _movie.Text = "(없음)";
        Tell("동영상을 뺍니다 — 「저장」을 눌러야 적힙니다.");
    }

    private void Export()
    {
        if (_editing is not { } e) return;
        var pick = new Microsoft.Win32.SaveFileDialog
        {
            Title = "발견물 내보내기", FileName = $"{e.Name}.cdsdiscovery",
            Filter = "발견물 (*.cdsdiscovery)|*.cdsdiscovery", DefaultExt = ".cdsdiscovery",
        };
        if (pick.ShowDialog(this) != true) return;
        Tell(UserDiscoveries.Export(e.Uuid, pick.FileName) is { } why ? $"내보내지 못했습니다 — {why}" : $"「{e.Name}」 을 내보냈습니다 — {pick.FileName}");
    }

    private void Import()
    {
        var pick = new Microsoft.Win32.OpenFileDialog
        {
            Title = "발견물 불러오기", Filter = "발견물 (*.cdsdiscovery)|*.cdsdiscovery|모든 파일 (*.*)|*.*",
        };
        if (pick.ShowDialog(this) != true) return;
        var (entry, message) = UserDiscoveries.Import(pick.FileName, (had, got) => ConfirmDialog.Ask(this,
            $"같은 발견물 「{had.Name}」 이 이미 있습니다. 불러온 「{got.Name}」 으로 덮어쓸까요?", "발견물 불러오기"));
        Refresh(entry?.Uuid ?? _editing?.Uuid);
        Tell(message);
    }

    /// <summary>창을 띄운다.</summary>
    public static void Show(Window? owner, Func<DiscoveryAreaPicker.Map?>? map)
    {
        var window = new UserDiscoveryDialog(map);
        if (owner != null) window.Owner = owner;
        window.ShowDialog();
    }
}
