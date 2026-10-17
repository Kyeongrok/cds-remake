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
///
/// 목록 맨 위의 「(예제) 돌묵상어」는 앱에 실린 돌묵상어를 칸 모양으로 옮긴 것이다 — 보기만 하고 적거나 내보내지 않는다.
/// 비밀번호를 걸고 적으면 잠긴다 — 불러온 사람도 그 비밀번호로 「잠금 풀기」를 해야 고칠 수 있다(<see cref="UserDiscoveries.LockOf"/>).
/// </remarks>
public sealed class UserDiscoveryDialog : GameWindow
{
    /// <summary>자리를 고를 세계 지도. 지도를 못 읽었으면 null.</summary>
    private readonly Func<DiscoveryAreaPicker.Map?>? _map;

    private readonly ListBox _list = new() { Width = 220, Margin = new Thickness(10, 10, 6, 10) };
    private readonly StackPanel _form = new() { Margin = new Thickness(6, 10, 10, 4) };

    /// <summary>「힌트」 탭 — 힌트가 있는지와 힌트 · 힌트를 실을 책 · 술집 소문.</summary>
    private readonly StackPanel _hints = new() { Margin = new Thickness(6, 10, 10, 4) };

    /// <summary>힌트가 있는지 — 없으면 힌트 · 책 · 소문을 안 내고, 처음부터 열린 것을 확률로만 만난다(원본 크라켄처럼).</summary>
    private readonly CheckBox _hasHint = new() { Content = "힌트 있음", VerticalAlignment = VerticalAlignment.Center, FontWeight = FontWeights.Bold };

    /// <summary>힌트 탭에서 「힌트 있음」을 풀면 꺼지는 줄들.</summary>
    private readonly List<UIElement> _hintParts = [];

    /// <summary>「출처 · 공유」 탭 — 출처 · 만든 이 · 라이선스 · 비밀번호. 발견물 칸과 따로 둔다.</summary>
    private readonly StackPanel _share = new() { Margin = new Thickness(6, 10, 10, 4) };
    private readonly TextBlock _status = new() { Margin = new Thickness(10, 4, 10, 8), TextWrapping = TextWrapping.Wrap };
    private readonly Image _picture = new() { Width = 320, Height = 240, Stretch = Stretch.Uniform };

    private readonly TextBox _name = Box(), _reward = Box(), _x1 = Box(60), _y1 = Box(60), _x2 = Box(60), _y2 = Box(60), _chance = Box(60);
    private readonly ComboBox _category = new() { Width = 160 };
    private readonly CheckBox _land = new() { Content = "육지", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) };
    private readonly CheckBox _open = new() { Content = "처음부터 열림(힌트 없이도 발견)", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) };
    private readonly TextBox _hintName = Box(), _hintGrade = Box(60), _hintCategory = Box(60), _hintFunds = Box(), _hintDeadline = Box(60);
    private readonly TextBox _hintText = Area(60), _rumorText = Area(48), _book = Box(), _description = Area(60);

    /// <summary>소문 도시 — 도시 이름으로 보이고 「도시 고르기…」로만 바꾼다. 번호는 <see cref="_rumorIds"/> 가 든다.</summary>
    private readonly TextBox _rumorCities = new()
    {
        Width = 320, Margin = new Thickness(0, 0, 8, 0), VerticalContentAlignment = VerticalAlignment.Center,
        IsReadOnly = true, Background = Brushes.WhiteSmoke,
    };

    private List<int> _rumorIds = [];

    /// <summary>자리 칸을 게임 위도 · 경도로 옮겨 보인다.</summary>
    private readonly TextBlock _where = new() { Margin = new Thickness(90, 0, 0, 6), Foreground = Brushes.DarkSlateBlue };
    private readonly TextBox _scene = Area(120), _credit = Area(40), _author = Box();
    private readonly ComboBox _license = new() { Width = 160, IsEditable = true };
    private readonly PasswordBox _password = new() { Width = 160, Margin = new Thickness(0, 0, 8, 0), VerticalContentAlignment = VerticalAlignment.Center };
    private readonly TextBlock _lockState = new() { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 0, 0) };

    /// <summary>책 표 · 도시 이름을 볼 게임. 없으면 책 고르기 창이 비어 뜬다.</summary>
    private readonly Engine.Game? _game;

    /// <summary>이 창을 연 동안 비밀번호로 푼 발견물.</summary>
    private readonly HashSet<string> _unlocked = [];

    /// <summary>예제를 보고 있는지 — 적기 · 지우기 · 내보내기를 막는다.</summary>
    private bool _example;

    /// <summary>「비밀번호 빼기」를 눌렀는지 — 다음 「저장」에서 잠금을 걷는다.</summary>
    private bool _dropLock;

    /// <summary>칸을 고치는 단추들 — 예제 · 잠긴 것을 볼 때 끈다.</summary>
    private readonly List<Button> _editors = [];
    private readonly Button _save, _remove, _export, _unlock, _unlockDrop;

    private UserDiscoveries.Entry? _editing;
    private (uint[] Bgra, int Width, int Height)? _newPicture;

    /// <summary>갈아 넣을 동영상 — null 이면 그대로, 빈 글이면 뺀다.</summary>
    private string? _newMovie;
    private readonly TextBlock _movie = new() { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0), Width = 320 };

    public UserDiscoveryDialog(Func<DiscoveryAreaPicker.Map?>? map, Engine.Game? game = null)
    {
        _map = map;
        _game = game;
        Title = "커스텀 발견물";
        Width = 1120;
        Height = 860;
        Background = Brushes.White;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        foreach (string name in DiscoveryTable.CategoryNames) _category.Items.Add(name);
        foreach (string license in UserDiscoveries.Licenses) _license.Items.Add(license);

        _form.Children.Add(Line("이름", _name, Label("갈래"), _category, Label("보수"), _reward));
        _form.Children.Add(Line("자리 X1", _x1, Label("Y1"), _y1, Label("X2"), _x2, Label("Y2"), _y2, _land,
                                Edit("지도에서 고르기…", () => PickArea())));
        _form.Children.Add(_where);
        foreach (var box in new[] { _x1, _y1, _x2, _y2 }) box.TextChanged += (_, _) => ShowWhere();
        _form.Children.Add(Line("확률", _chance, Label("분의 1"), _open));
        _hints.Children.Add(Line("힌트", _hasHint));
        _hints.Children.Add(new TextBlock
        {
            Margin = new Thickness(90, 0, 0, 8), Foreground = Brushes.DimGray, TextWrapping = TextWrapping.Wrap,
            Text = "힌트가 없는 발견물도 있습니다(원본 크라켄). 체크를 풀면 힌트 · 책 · 소문을 게임에 내지 않고, 적어 둔 값은 그대로 둡니다."
                   + " 힌트가 없으면 「발견물」 탭의 「처음부터 열림」을 켜야 확률로 만날 수 있습니다.",
        });
        foreach (var part in new UIElement[]
                 {
                     Line("힌트 이름", _hintName, Label("등급(1~3)"), _hintGrade, Label("갈래"), _hintCategory,
                          Label("자금"), _hintFunds, Label("기한(년)"), _hintDeadline),
                     Line("힌트 글", _hintText),
                     Line("힌트를 실을 책", _book, Edit("책 고르기…", PickBook)),
                     Line("소문 도시", _rumorCities, Edit("도시 고르기…", PickCities)),
                     Line("소문 글", _rumorText),
                 })
        {
            _hintParts.Add(part);
            _hints.Children.Add(part);
        }
        _hasHint.Checked += (_, _) => ShowHintParts();
        _hasHint.Unchecked += (_, _) => ShowHintParts();
        _form.Children.Add(Line("설명", _description));
        _form.Children.Add(Line("장면", _scene));
        _form.Children.Add(new TextBlock
        {
            Margin = new Thickness(90, 0, 0, 6), Foreground = Brushes.DimGray, TextWrapping = TextWrapping.Wrap,
            Text = "한 줄에 하나 — 「부관: 제독！ 저기를 보십시오．」 · 「[그림]」 · 「[동영상]」 · 「[발견]」(뒤에 글을 붙이면 그 글로 알립니다, 비우면 「~을 발견했다！」)\n"
                   + "바다 괴물과 싸우려면 「[해전] 크라켄」(시서펜트 · 식인상어 · 맨터도 됩니다) — 그 아래 줄은 이겼을 때, 「[놓쳤을 때]」 아래 줄은"
                   + " 지거나 도망쳤을 때입니다. 놓치면 다음에 그 바다에 가면 또 만납니다. 「(예제) 크라켄」을 보세요.",
        });
        _share.Children.Add(Line("출처", _credit));
        _share.Children.Add(new TextBlock
        {
            Margin = new Thickness(90, 0, 0, 6), Foreground = Brushes.DimGray, TextWrapping = TextWrapping.Wrap,
            Text = "그림 · 글을 어디서 가져왔는지와 그 라이선스를 적어 주세요.",
        });
        _share.Children.Add(Line("만든 이", _author, Label("닉네임이나 이름")));
        _share.Children.Add(Line("라이선스", _license));
        _unlock = Push("잠금 풀기", Unlock);
        _unlockDrop = Push("비밀번호 빼기", DropLock);
        _share.Children.Add(Line("비밀번호", _password, _unlock, _unlockDrop, _lockState));
        _share.Children.Add(new TextBlock
        {
            Margin = new Thickness(90, 0, 0, 6), Foreground = Brushes.DimGray, TextWrapping = TextWrapping.Wrap,
            Text = "비밀번호를 적고 「저장」하면 잠깁니다. 잠긴 발견물은 내보내 다른 분이 불러와도 게임에는 나오지만, 비밀번호 없이는 고칠 수 없습니다."
                   + " 비밀번호는 파일에 남기지 않으니 잊지 마세요.",
        });
        _form.Children.Add(Line("그림", new Border { BorderBrush = Brushes.Gray, BorderThickness = new Thickness(1), Child = _picture },
                                Edit("그림 고르기…", PickPicture)));
        _form.Children.Add(Line("동영상", _movie, Edit("동영상 고르기…", PickMovie), Edit("동영상 빼기", DropMovie)));

        var bar = new WrapPanel { Margin = new Thickness(10, 0, 10, 0) };
        _save = Push("저장", Save);
        _remove = Push("지우기", Remove);
        _export = Push("내보내기…", Export);
        bar.Children.Add(Push("새 발견물", Add));
        bar.Children.Add(Push("복제", Duplicate));
        bar.Children.Add(_save);
        bar.Children.Add(_remove);
        bar.Children.Add(_export);
        bar.Children.Add(Push("불러오기…", Import));

        var body = new DockPanel();
        DockPanel.SetDock(_list, Dock.Left);
        body.Children.Add(_list);
        var tabs = new TabControl { Margin = new Thickness(6, 10, 10, 4) };
        tabs.Items.Add(new TabItem { Header = "발견물", Content = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = _form } });
        tabs.Items.Add(new TabItem { Header = "힌트", Content = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = _hints } });
        tabs.Items.Add(new TabItem { Header = "출처 · 공유", Content = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = _share } });
        body.Children.Add(tabs);

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

    /// <summary>칸을 고치는 단추 — 예제 · 잠긴 것을 볼 때 함께 꺼진다.</summary>
    private Button Edit(string text, Action act)
    {
        var button = Push(text, act);
        _editors.Add(button);
        return button;
    }

    private IEnumerable<TextBox> Boxes =>
    [
        _name, _reward, _x1, _y1, _x2, _y2, _chance, _hintName, _hintGrade, _hintCategory, _hintFunds, _hintDeadline,
        _hintText, _rumorText, _book, _description, _scene, _credit, _author,
    ];

    /// <summary>칸을 고칠 수 있게 하거나 읽기만 하게 둔다 — 읽기만 해도 글은 골라 베낄 수 있다.</summary>
    private void SetEditable(bool on)
    {
        foreach (var box in Boxes) box.IsReadOnly = !on;
        _category.IsEnabled = _land.IsEnabled = _open.IsEnabled = _license.IsEnabled = _hasHint.IsEnabled = on;
        foreach (var button in _editors) button.IsEnabled = on;
        _save.IsEnabled = on;
        _remove.IsEnabled = _export.IsEnabled = !_example;

        bool locked = _editing is { } e && UserDiscoveries.IsLocked(e);
        _password.IsEnabled = !_example;
        _unlock.IsEnabled = !_example && locked && !on;
        _unlockDrop.IsEnabled = on && locked;
        _lockState.Text = _example ? "예제는 고칠 수 없습니다"
            : !locked ? "잠기지 않음"
            : on ? "잠금 풀림 — 고칠 수 있습니다" : "잠김 — 비밀번호를 적고 「잠금 풀기」를 눌러 주세요";
    }

    /// <summary>「힌트 있음」을 풀면 힌트 줄들을 흐리게 — 값은 그대로 둔다.</summary>
    private void ShowHintParts()
    {
        foreach (var part in _hintParts) part.Opacity = _hasHint.IsChecked == true ? 1 : 0.4;
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
        // 맨 위는 예제 — 무엇을 어떻게 적는지 보라고 둔다.
        foreach (var (example, _) in UserDiscoveries.Examples)
            _list.Items.Add(new ListBoxItem { Content = $"(예제) {example.Name}", Tag = example.Uuid, FontStyle = FontStyles.Italic });
        foreach (var l in UserDiscoveries.All.OrderBy(l => l.Entry.Name, StringComparer.Ordinal))
            _list.Items.Add(new ListBoxItem
            {
                Content = $"{l.Numbers.Id}  {l.Entry.Name}" + (UserDiscoveries.IsLocked(l.Entry) ? "  🔒" : ""),
                Tag = l.Entry.Uuid,
            });
        var items = _list.Items.OfType<ListBoxItem>().ToList();
        // 고를 것이 없으면 지어 둔 첫 것, 그것도 없으면 예제다.
        _list.SelectedItem = items.FirstOrDefault(i => (string)i.Tag == pick)
                             ?? items.Skip(UserDiscoveries.Examples.Count).FirstOrDefault() ?? items[0];
    }

    private void Show(string uuid)
    {
        _newPicture = null;
        _newMovie = null;
        _dropLock = false;
        _password.Clear();
        _form.IsEnabled = _share.IsEnabled = _hints.IsEnabled = true;

        if (UserDiscoveries.ExampleOf(uuid) is { } shown)
        {
            _example = true;
            Fill(shown.Entry);
            _movie.Text = "(없음)";
            _picture.Source = shown.Picture >= 0 && DiscoveryStillFiles.TryGetBgra(shown.Picture, out int w, out int h) is { } bgra
                ? BitmapSource.Create(w, h, 96, 96, PixelFormats.Bgra32, null, bgra, w * 4) : null;
            SetEditable(false);
            Tell($"예제입니다 — 게임에 들어 있는 {shown.Entry.Name}을(를) 이 칸들로 옮긴 것입니다. 「복제」를 누르면 베껴서 고쳐 쓸 수 있습니다.");
            return;
        }

        if (UserDiscoveries.All.FirstOrDefault(l => l.Entry.Uuid == uuid) is not { } l) return;
        _example = false;
        var e = l.Entry;
        Fill(e);
        _movie.Text = l.MoviePath is { } clip
            ? $"{System.IO.Path.GetFileName(clip)}  ({new System.IO.FileInfo(clip).Length / 1048576.0:0.0}MB)" : "(없음)";
        _picture.Source = l.PicturePath is { } path ? LoadPicture(path) : null;
        SetEditable(!UserDiscoveries.IsLocked(e) || _unlocked.Contains(e.Uuid));
        Tell($"발견물 {l.Numbers.Id}번 · 힌트 {l.Numbers.Hint}번 · 그림 {l.Numbers.Picture}번 — UUID {e.Uuid}");
    }

    /// <summary>칸들을 그 발견물로 채운다.</summary>
    private void Fill(UserDiscoveries.Entry e)
    {
        _editing = e;
        _name.Text = e.Name;
        _category.SelectedIndex = Math.Clamp(e.Category, 0, _category.Items.Count - 1);
        _reward.Text = $"{e.Reward}";
        (_x1.Text, _y1.Text, _x2.Text, _y2.Text) = ($"{e.X1}", $"{e.Y1}", $"{e.X2}", $"{e.Y2}");
        _land.IsChecked = e.OnLand;
        _open.IsChecked = e.OpenAtStart;
        _chance.Text = $"{e.Chance}";
        _hasHint.IsChecked = e.HasHint;
        ShowHintParts();
        _hintName.Text = e.HintName;
        _hintGrade.Text = $"{e.HintGrade}";
        _hintCategory.Text = $"{e.HintCategory}";
        _hintFunds.Text = $"{e.HintFunds}";
        _hintDeadline.Text = $"{e.HintDeadline}";
        _hintText.Text = e.HintText;
        ShowCities([.. e.RumorCities ?? []]);
        _rumorText.Text = e.RumorText;
        _book.Text = e.Book;
        _description.Text = e.Description;
        _scene.Text = string.Join("\n", (e.Lines ?? []).Select(SceneText));
        _credit.Text = e.Credit;
        _author.Text = e.Author;
        _license.Text = e.License.Length > 0 ? e.License : UserDiscoveries.DefaultLicense;
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
        "battle" => $"[해전] {line.Text}",
        "lose" => "[놓쳤을 때]",
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
            else if (s.StartsWith("[해전]")) lines.Add(new("battle", null, s[4..].Trim() is { Length: > 0 } monster ? monster : "크라켄"));
            else if (s == "[놓쳤을 때]") lines.Add(new("lose"));
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
        var cities = _rumorIds;
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
            License = _license.Text.Trim() is { Length: > 0 } license ? license : UserDiscoveries.DefaultLicense,
            HasHint = _hasHint.IsChecked == true,
        };
    }

    /// <summary>지난번에 적은 만든 이 — 같은 사람이 여럿 짓는 일이 많다.</summary>
    private static string LastAuthor() =>
        UserDiscoveries.All.Select(l => l.Entry.Author).LastOrDefault(a => a.Length > 0) ?? "";

    /// <summary>
    /// 고른 발견물을 새 UUID 로 베낀다 — 그림 · 동영상도 함께. 예제(돌묵상어)를 베껴 고쳐 쓰라고 둔다.
    /// </summary>
    /// <remarks>
    /// 잠긴 것은 풀어야 베낀다 — 베낀 것은 잠금이 없어, 안 그러면 베껴서 고치는 길로 잠금을 비켜 간다.
    /// 자리는 그대로라 원본과 겹친다 — 「지도에서 고르기…」로 옮겨 달라고 알린다.
    /// </remarks>
    private void Duplicate()
    {
        if (_editing is not { } source) return;
        if (!_example && UserDiscoveries.IsLocked(source) && !_unlocked.Contains(source.Uuid))
        {
            Tell("잠긴 발견물은 비밀번호로 잠금을 푼 뒤에 복제할 수 있습니다.");
            return;
        }

        var copy = source with
        {
            Uuid = Guid.NewGuid().ToString(),
            Name = $"{source.Name} 복사본",
            Lock = "",
            Author = _example ? LastAuthor() : source.Author,
            Version = UserDiscoveries.FormatVersion,
        };

        (uint[] Bgra, int Width, int Height)? picture = null;
        string? movie = null;
        if (_example)
        {
            if (UserDiscoveries.ExampleOf(source.Uuid) is { Picture: >= 0 } shown
                && DiscoveryStillFiles.TryGetBgra(shown.Picture, out int w, out int h) is { } bgra) picture = (bgra, w, h);
        }
        else if (UserDiscoveries.All.FirstOrDefault(l => l.Entry.Uuid == source.Uuid) is { } loaded)
        {
            if (loaded.PicturePath is { } path && ReadBgra(path) is { } art) picture = art;
            movie = loaded.MoviePath;
        }

        if (UserDiscoveries.Save(copy, picture, movie) is { } why) { Tell($"복제하지 못했습니다 — {why}"); return; }
        Refresh(copy.Uuid);
        Tell($"「{source.Name}」 을 「{copy.Name}」 으로 복제했습니다 — 자리가 원본과 같으니 「지도에서 고르기…」로 옮기고, 이름 · 힌트를 고친 뒤 「저장」을 눌러 주세요.");
    }

    /// <summary>png 한 장을 BGRA 로 푼다. 못 읽으면 null.</summary>
    private static (uint[] Bgra, int Width, int Height)? ReadBgra(string path)
    {
        try
        {
            var decoder = BitmapDecoder.Create(new Uri(path), BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
            BitmapSource frame = decoder.Frames[0];
            if (frame.Format != PixelFormats.Bgra32) frame = new FormatConvertedBitmap(frame, PixelFormats.Bgra32, null, 0);
            var bgra = new uint[frame.PixelWidth * frame.PixelHeight];
            frame.CopyPixels(bgra, frame.PixelWidth * 4, 0);
            return (bgra, frame.PixelWidth, frame.PixelHeight);
        }
        catch (Exception ex) when (ex is System.IO.IOException or NotSupportedException or System.IO.FileFormatException) { return null; }
    }

    private void Add()
    {
        var made = UserDiscoveries.Blank(LastAuthor());
        if (UserDiscoveries.Save(made) is { } why) { Tell($"짓지 못했습니다 — {why}"); return; }
        Refresh(made.Uuid);
        // 자리부터 고른다 — 자리 없는 발견물은 어디서도 안 잡힌다.
        Tell(PickArea()
            ? "새 발견물을 지었습니다 — 나머지 칸을 채우고 「저장」을 눌러 주세요."
            : "새 발견물을 지었습니다 — 「지도에서 고르기…」로 자리를 잡고 칸을 채운 뒤 「저장」을 눌러 주세요.");
    }

    private void Save()
    {
        if (_example) { Tell("예제는 적을 수 없습니다 — 「새 발견물」을 눌러 새로 만들어 주세요."); return; }
        if (_editing is { } now && UserDiscoveries.IsLocked(now) && !_unlocked.Contains(now.Uuid))
        {
            Tell("잠긴 발견물입니다 — 비밀번호를 적고 「잠금 풀기」를 눌러 주세요.");
            return;
        }
        if (Collect() is not { } e) return;
        if (!e.HasHint && !e.OpenAtStart)
        {
            Tell("힌트가 없으면 「처음부터 열림」을 켜야 게임에서 만날 수 있습니다 — 「발견물」 탭에서 켜 주세요.");
            return;
        }
        if (e.Lines.Count(x => x.Kind == "battle") > 1) { Tell("「[해전]」 줄은 하나만 둘 수 있습니다."); return; }
        // 비밀번호를 적었으면 그것으로 잠그고, 「비밀번호 빼기」를 눌렀으면 걷는다. 둘 다 아니면 잠금은 그대로다.
        if (_password.Password.Length > 0) { e = e with { Lock = UserDiscoveries.LockOf(_password.Password) }; _unlocked.Add(e.Uuid); }
        else if (_dropLock) e = e with { Lock = "" };
        if (e.X1 < 0 || e.Y1 < 0) { Tell("자리가 없습니다 — 「지도에서 고르기…」로 자리를 잡아 주세요."); return; }
        if (UserDiscoveries.Save(e, _newPicture, _newMovie) is { } why) { Tell($"적지 못했습니다 — {why}"); return; }
        Refresh(e.Uuid);
        if (UserDiscoveries.All.FirstOrDefault(l => l.Entry.Uuid == e.Uuid) is { } l && UserDiscoveries.Compile(l, out string error) == null)
            Tell($"적었지만 장면을 굽지 못했습니다 — {error}");
        else Tell($"「{e.Name}」 을 적었습니다.");
    }

    private void Remove()
    {
        if (_example || _editing is not { } e) return;
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
        // 지도 창이 지도를 들고 있으면 그것을 쓰고, 아니면(도시 · 첫 화면) 게임 폴더의 WORLD.CDS 로 짓는다.
        if ((_map?.Invoke() ?? DiscoveryAreaPicker.FromGame(_game)) is not { } map)
        {
            Tell("세계 지도를 못 읽었습니다 — 게임 폴더에 WORLD.CDS 가 있는지 확인해 주세요.");
            return false;
        }
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
        if (_example || _editing is not { } e) return;
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

    /// <summary>비밀번호로 잠금을 푼다 — 이 창을 연 동안만 풀려 있다.</summary>
    private void Unlock()
    {
        if (_example || _editing is not { } e) return;
        if (!UserDiscoveries.Unlocks(e, _password.Password)) { Tell("비밀번호가 맞지 않습니다."); return; }
        _unlocked.Add(e.Uuid);
        _password.Clear();
        SetEditable(true);
        Tell($"「{e.Name}」 의 잠금을 풀었습니다 — 고친 뒤 「저장」을 눌러 주세요. 잠금은 그대로 남습니다.");
    }

    /// <summary>다음 「저장」에서 비밀번호를 걷는다.</summary>
    private void DropLock()
    {
        _dropLock = true;
        _password.Clear();
        Tell("「저장」을 누르면 비밀번호가 빠집니다.");
    }

    /// <summary>문화권 탭으로 도시를 늘어놓는 창을 띄워 소문 도시를 셋까지 고른다.</summary>
    private void PickCities()
    {
        if (_game == null) { Tell("도시 표를 읽지 못했습니다 — 게임 안에서 개발 창을 열어 주세요."); return; }
        if (CityPickerDialog.Pick(this, _game, _rumorIds) is not { } picked) return;
        ShowCities(picked);
        Tell($"소문 도시: {_rumorCities.Text} — 「저장」을 눌러야 적힙니다.");
    }

    /// <summary>
    /// 자리 칸(세계 지도 칸)을 게임의 위도 · 경도로 — 함대정보 판의 함대좌표처럼 「북위 38도 서경 9도」 꼴이다.
    /// 칸 1250 이 위도 180도, 칸 2500 이 경도 360도다(<see cref="Rendering.WorldMapHost.DescribeRoute"/> 와 같은 셈).
    /// </summary>
    private void ShowWhere()
    {
        if (!int.TryParse(_x1.Text, out int x1) || !int.TryParse(_y1.Text, out int y1)
            || !int.TryParse(_x2.Text, out int x2) || !int.TryParse(_y2.Text, out int y2) || x1 < 0 || y1 < 0)
        {
            _where.Text = "게임 위치: (자리를 아직 안 골랐습니다)";
            return;
        }
        static int Lat(int y) => (int)Math.Round(90.0 - y * 180.0 / Support.Local.Helpers.WorldMapRenderer.CellH);
        static int Lon(int x)
        {
            int w = Support.Local.Helpers.WorldMapRenderer.UnfoldedW;
            return (int)Math.Round(((x % w) + w) % w * 360.0 / w - 180.0);
        }
        static string Span(int a, int b, string plus, string minus)
        {
            string One(int v) => $"{(v >= 0 ? plus : minus)} {Math.Abs(v)}도";
            return a == b ? One(a) : $"{One(a)} ~ {One(b)}";
        }
        int top = Lat(Math.Min(y1, y2)), bottom = Lat(Math.Max(y1, y2));
        int west = Lon(Math.Min(x1, x2)), east = Lon(Math.Max(x1, x2));
        _where.Text = $"게임 위치: {Span(top, bottom, "북위", "남위")}  ·  {Span(west, east, "동경", "서경")}";
    }

    /// <summary>소문 도시를 들고 이름으로 보인다. 도시 표가 없으면 번호로.</summary>
    private void ShowCities(List<int> ids)
    {
        _rumorIds = ids;
        _rumorCities.Text = ids.Count == 0 ? "(없음)"
            : string.Join(", ", ids.Select(id => _game?.CityName(id) is { Length: > 0 } name ? name : $"도시 {id}"));
    }

    /// <summary>책 고르기 창을 띄워 힌트를 실을 책을 고른다.</summary>
    private void PickBook()
    {
        if (BookPickerDialog.Pick(this, _game, _book.Text.Trim()) is { } title)
        {
            _book.Text = title;
            Tell($"힌트를 『{title}』 에 싣습니다 — 「저장」을 눌러야 적힙니다.");
        }
    }

    /// <summary>창을 띄운다.</summary>
    public static void Show(Window? owner, Func<DiscoveryAreaPicker.Map?>? map, Engine.Game? game = null)
    {
        var window = new UserDiscoveryDialog(map, game);
        if (owner != null) window.Owner = owner;
        window.ShowDialog();
    }
}
