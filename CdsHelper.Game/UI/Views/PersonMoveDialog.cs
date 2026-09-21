using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CdsHelper.Game.Engine;
using CdsHelper.Game.Local.Helpers;
using CdsHelper.Support.Local.Models;

namespace CdsHelper.Game.UI.Views;

/// <summary>
/// 인물들이 어느 도시로 가고 있는지, 다음에 어디로 갈 수 있는지 늘어놓는 창. 제목 줄 햄버거에서 연다.
/// </summary>
/// <remarks>
/// 셈은 모두 <see cref="PersonWorld"/> 가 한다 — 여기서는 보여 주기만 한다.
/// <list type="bullet">
///   <item>14~200 은 매월 1일에 다섯 달에 한 번꼴로 떠난다. 고를 수 있는 도시는 갈래(해역 ·
///         문화권 · 나라)가 정한다(<c>0x004327F0</c>).</item>
///   <item>0~13 역사 항해자는 주사위가 아니라 대본(<c>HISTCHR.CDS</c>)대로 떠난다 — 앞으로의
///         수를 함께 보인다.</item>
///   <item>닿으면 예순 날 쉰다. 201 이상은 움직이지 않는다.</item>
/// </list>
/// 볼트 <c>72.분석-인물 이동(역사 항해사와 매달 굴림)</c>.
/// </remarks>
public sealed class PersonMoveDialog : GameWindow
{
    /// <summary>역사 항해자의 대본을 몇 달 앞까지 보일지.</summary>
    private const int ScriptMonths = 60;

    private static readonly string[] KindNames = ["해역", "문화권", "안 움직임", "나라"];

    private static readonly string[] Views = ["길 위에 있는 사람", "움직일 수 있는 사람", "모두"];

    private readonly Engine.Game _game;
    private readonly PersonWorld _world;

    private readonly ComboBox _view = new() { Width = 180, VerticalAlignment = VerticalAlignment.Center };

    /// <summary>도시 거르개. 첫 줄 「전체」 뒤로 도시가 선다 — 줄의 Tag 가 도시 번호다.</summary>
    private readonly ComboBox _city = new() { Width = 160, VerticalAlignment = VerticalAlignment.Center };

    private readonly TextBox _search = new()
    {
        Width = 170,
        VerticalAlignment = VerticalAlignment.Center,
        ToolTip = "이름으로 찾기",
    };

    private readonly CheckBox _hireableOnly = new()
    {
        Content = "고용 가능만",
        IsChecked = true,
        VerticalAlignment = VerticalAlignment.Center,
        Margin = new Thickness(16, 0, 0, 0),
    };

    /// <summary>「보기」에서 「모두」의 자리.</summary>
    private const int ViewAll = 2;

    /// <summary>상태 갈래 — 거르개 칸의 차례와 같다.</summary>
    private enum State { Sailing, Script, Event, Hidden, Fixed, Resting, Ready }

    /// <summary>상태 거르개 칸 이름.</summary>
    private static readonly string[] StateNames =
        ["항해 중", "대본", "이벤트 인물", "나오지 않음", "갈래 2 안 움직임", "쉬는 중", "떠날 수 있음"];

    /// <summary>상태 거르개. 기본은 「나오지 않음」만 끈다.</summary>
    private readonly CheckBox[] _states = new CheckBox[StateNames.Length];

    private readonly ListBox _grid = new()
    {
        SelectionMode = SelectionMode.Single,
        BorderThickness = new Thickness(0),
        Background = Brushes.Transparent,
        HorizontalContentAlignment = HorizontalAlignment.Stretch,
    };

    private sealed class FaceImageConverter(Portraits? faces) : IValueConverter
    {
        private readonly Dictionary<int, BitmapSource?> _cache = [];

        public object? Convert(object? value, Type targetType, object? parameter,
                               System.Globalization.CultureInfo culture)
        {
            if (value is not int face) return null;
            if (_cache.TryGetValue(face, out var image)) return image;

            if (faces?.TryGetBgra(face, female: false) is not { } pixels)
            {
                _cache[face] = null;
                return null;
            }

            image = BitmapSource.Create(Portraits.Width, Portraits.Height, 96, 96,
                                        PixelFormats.Bgra32, null, pixels, Portraits.Width * 4);
            image.Freeze();
            _cache[face] = image;
            return image;
        }

        public object ConvertBack(object? value, Type targetType, object? parameter,
                                  System.Globalization.CultureInfo culture) =>
            Binding.DoNothing;
    }

    private readonly TextBlock _detail = new()
    {
        Margin = new Thickness(10),
        TextWrapping = TextWrapping.Wrap,
    };

    private readonly ScrollViewer _detailScroll;

    private readonly TextBlock _status = new() { Margin = new Thickness(10, 6, 10, 8) };

    /// <summary>표 한 줄.</summary>
    private sealed record Row(int Id, string Name, int Age, string Now, string To, string Left,
                              string State, string Kind, string Skills, string Languages,
                              PersonTable.Row Source);

    private PersonMoveDialog(Engine.Game game, PersonWorld world)
    {
        _game = game;
        _world = world;

        Title = "인물 이동";
        Width = 1000;
        Height = 640;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        _grid.SetValue(ScrollViewer.VerticalScrollBarVisibilityProperty, ScrollBarVisibility.Auto);
        _grid.SetValue(ScrollViewer.HorizontalScrollBarVisibilityProperty, ScrollBarVisibility.Disabled);

        ConfigureCards();

        foreach (var name in Views) _view.Items.Add(name);
        _view.SelectedIndex = ViewAll;
        _view.SelectionChanged += (_, _) => Rebuild();

        // 도시는 기본이 지금 들어와 있는 도시다. 바다 위에서는 기본만 「전체」고 고르기는 된다.
        int here = game.Player.CityId;
        _city.Items.Add(new ComboBoxItem { Content = "전체", Tag = -1 });
        for (int id = 0; id < PersonTable.CityCount; id++)
        {
            string name = game.CityName(id);
            if (string.IsNullOrEmpty(name)) continue;
            var item = new ComboBoxItem { Content = name, Tag = id };
            _city.Items.Add(item);
            if (id == here) _city.SelectedItem = item;
        }
        if (_city.SelectedItem == null) _city.SelectedIndex = 0;
        _city.SelectionChanged += (_, _) => Rebuild();
        _search.TextChanged += (_, _) => Rebuild();
        _hireableOnly.Checked += (_, _) => Rebuild();
        _hireableOnly.Unchecked += (_, _) => Rebuild();
        _grid.SelectionChanged += (_, _) => ShowDetail();

        var bar = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(10, 8, 10, 4),
            Children =
            {
                new TextBlock
                {
                    Text = "보기:",
                    Margin = new Thickness(0, 0, 6, 0),
                    VerticalAlignment = VerticalAlignment.Center,
                },
                _view,
                new TextBlock
                {
                    Text = "도시:",
                    Margin = new Thickness(16, 0, 6, 0),
                    VerticalAlignment = VerticalAlignment.Center,
                },
                _city,
                new TextBlock
                {
                    Text = "이름:",
                    Margin = new Thickness(16, 0, 6, 0),
                    VerticalAlignment = VerticalAlignment.Center,
                },
                _search,
                _hireableOnly,
            },
        };

        // 상태는 여럿을 함께 고른다.
        var stateBar = new WrapPanel { Margin = new Thickness(10, 0, 10, 4) };
        stateBar.Children.Add(new TextBlock
        {
            Text = "상태:",
            Margin = new Thickness(0, 0, 6, 0),
            VerticalAlignment = VerticalAlignment.Center,
        });
        for (int i = 0; i < StateNames.Length; i++)
        {
            _states[i] = new CheckBox
            {
                Content = StateNames[i],
                IsChecked = i != (int)State.Hidden,
                Margin = new Thickness(0, 2, 14, 2),
                VerticalAlignment = VerticalAlignment.Center,
            };
            _states[i].Checked += (_, _) => Rebuild();
            _states[i].Unchecked += (_, _) => Rebuild();
            stateBar.Children.Add(_states[i]);
        }

        var top = new StackPanel { Children = { bar, stateBar } };

        _detailScroll = new ScrollViewer
        {
            Width = 280,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = _detail,
            Visibility = Visibility.Collapsed,
        };

        var split = new Grid();
        split.ColumnDefinitions.Add(new ColumnDefinition());
        split.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(280) });
        Grid.SetColumn(_grid, 0);
        Grid.SetColumn(_detailScroll, 1);
        split.Children.Add(_grid);
        split.Children.Add(_detailScroll);

        var page = new DockPanel();
        DockPanel.SetDock(top, Dock.Top);
        DockPanel.SetDock(_status, Dock.Bottom);
        page.Children.Add(top);
        page.Children.Add(_status);
        page.Children.Add(split);
        Content = page;

        Rebuild();
    }

    /// <summary>창을 연다. 인물 표를 못 읽었으면 그렇다고 알린다.</summary>
    public static void Show(Window owner, Engine.Game game)
    {
        if (game.World is not { } world)
        {
            NoticeDialog.Show(owner, $"인물 표를 읽지 못했습니다 {PersonTable.LastError}".TrimEnd());
            return;
        }

        // 그 날짜까지 따라잡고 본다 — 지도를 안 열었으면 세상이 멈춰 있을 수 있다.
        world.Advance(game.Player.Date);
        new PersonMoveDialog(game, world) { Owner = owner }.ShowDialog();
    }

    private void ConfigureCards()
    {
        var panel = new FrameworkElementFactory(typeof(StackPanel));
        panel.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Stretch);
        _grid.ItemsPanel = new ItemsPanelTemplate(panel);

        var card = new FrameworkElementFactory(typeof(Border));
        card.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Stretch);
        card.SetValue(FrameworkElement.MinWidthProperty, 620d);
        card.SetValue(FrameworkElement.MinHeightProperty, 112d);
        card.SetValue(FrameworkElement.MarginProperty, new Thickness(5));
        card.SetValue(Border.BorderBrushProperty, Brushes.Silver);
        card.SetValue(Border.BorderThicknessProperty, new Thickness(1));
        card.SetValue(Border.BackgroundProperty, Brushes.White);
        card.SetValue(Border.PaddingProperty, new Thickness(6));

        var body = new FrameworkElementFactory(typeof(Grid));
        var imageColumn = new FrameworkElementFactory(typeof(ColumnDefinition));
        imageColumn.SetValue(ColumnDefinition.WidthProperty, GridLength.Auto);
        body.AppendChild(imageColumn);
        var textColumn = new FrameworkElementFactory(typeof(ColumnDefinition));
        textColumn.SetValue(ColumnDefinition.WidthProperty, new GridLength(230));
        body.AppendChild(textColumn);
        var abilitiesColumn = new FrameworkElementFactory(typeof(ColumnDefinition));
        abilitiesColumn.SetValue(ColumnDefinition.WidthProperty, new GridLength(1, GridUnitType.Star));
        body.AppendChild(abilitiesColumn);

        var image = new FrameworkElementFactory(typeof(Image));
        image.SetBinding(Image.SourceProperty,
                         new Binding($"{nameof(Row.Source)}.{nameof(PersonTable.Row.Face)}")
                         { Converter = new FaceImageConverter(_game.Faces) });
        image.SetValue(FrameworkElement.WidthProperty, 72d);
        image.SetValue(FrameworkElement.HeightProperty, 86d);
        image.SetValue(Image.StretchProperty, Stretch.Fill);
        image.SetValue(RenderOptions.BitmapScalingModeProperty, BitmapScalingMode.HighQuality);
        image.SetValue(Grid.ColumnProperty, 0);
        body.AppendChild(image);

        var text = new FrameworkElementFactory(typeof(StackPanel));
        text.SetValue(FrameworkElement.MarginProperty, new Thickness(8, 0, 0, 0));
        text.SetValue(Grid.ColumnProperty, 1);
        AddCardText(text, nameof(Row.Name), fontSize: 17, bold: true);
        AddCardText(text, nameof(Row.Id), "번호 {0}");
        AddCardText(text, nameof(Row.Age), "나이 {0}세");
        AddCardText(text, nameof(Row.Now), "현재: {0}");
        AddCardText(text, nameof(Row.To), "목적지: {0}");
        AddCardText(text, nameof(Row.State), fontSize: 12);
        body.AppendChild(text);

        var abilities = new FrameworkElementFactory(typeof(StackPanel));
        abilities.SetValue(FrameworkElement.MarginProperty, new Thickness(24, 0, 0, 0));
        abilities.SetValue(Grid.ColumnProperty, 2);
        AddCardText(abilities, nameof(Row.Skills), fontSize: 12);
        AddCardText(abilities, nameof(Row.Languages), fontSize: 12);
        body.AppendChild(abilities);
        card.AppendChild(body);

        _grid.ItemTemplate = new DataTemplate { VisualTree = card };
    }

    private static void AddCardText(FrameworkElementFactory parent, string path,
                                    string? format = null, double fontSize = 13, bool bold = false)
    {
        var label = new FrameworkElementFactory(typeof(TextBlock));
        label.SetBinding(TextBlock.TextProperty, new Binding(path)
        {
            StringFormat = format,
        });
        label.SetValue(TextBlock.FontSizeProperty, fontSize);
        label.SetValue(TextBlock.FontWeightProperty, bold ? FontWeights.Bold : FontWeights.Normal);
        label.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 0, 0, 2));
        label.SetValue(TextBlock.TextWrappingProperty, TextWrapping.Wrap);
        parent.AppendChild(label);
    }

    private string CityOf(int city) => city >= 0 ? _game.CityName(city) : "—";

    /// <summary>표를 다시 짓는다. 보고 있던 사람은 그대로 붙들어 둔다.</summary>
    private void Rebuild()
    {
        int keep = _grid.SelectedItem is Row picked ? picked.Id : -1;
        var date = _game.Player.Date;

        int moving = 0, resting = 0;
        var rows = new List<Row>();
        foreach (var person in _world.People)
        {
            bool onRoad = PersonWorld.Moving(person);
            bool active = _world.IsActive(person);
            if (onRoad) moving++;
            else if (active && person.Wait < 0) resting++;

            bool show = _view.SelectedIndex switch
            {
                0 => onRoad,
                1 => onRoad || Movable(person, active),
                _ => true,
            };
            if (!show) continue;

            // 도시를 골랐으면 그 도시에 앉은 사람과 그리로 가는 사람만 둔다.
            int city = _city.SelectedItem is ComboBoxItem { Tag: int c } ? c : -1;
            if (city >= 0 && person.City != city && !(onRoad && person.Dest == city)) continue;

            if (_states[(int)StateKind(person, active, onRoad)].IsChecked != true) continue;
            if (_hireableOnly.IsChecked == true && person.Hire != PersonTable.Hireable) continue;
            if (_search.Text.Length > 0 && !person.Name.Contains(_search.Text, StringComparison.OrdinalIgnoreCase))
                continue;

            string to = person.Dest == PersonWorld.SpotDest ? "발견물 자리" : CityOf(person.Dest);
            string left = _world.DaysLeft(person) is { } days ? $"{days}일" : "";
            string kind = person.Kind >= 0 && person.Kind < KindNames.Length
                ? KindNames[person.Kind] : $"{person.Kind}";
            if (person.Id < PersonTable.VoyagerCount) kind = "대본";
            string skills = Levels(person.Skills, Skill.Names, "기능");
            string languages = Levels(person.Languages, Skill.Languages, "언어");

            rows.Add(new Row(person.Id, person.Name, _world.Table.AgeOn(person, date.Year),
                             CityOf(person.City), onRoad ? to : "", left,
                             StateOf(person, active, onRoad), kind, skills, languages, person));
        }

        _grid.ItemsSource = rows;
        if (keep >= 0) _grid.SelectedItem = rows.FirstOrDefault(r => r.Id == keep);

        _status.Text = $"{date:yyyy년 M월 d일} · 길 위 {moving}명 · 쉬는 중 {resting}명 · 목록 {rows.Count}명";
        ShowDetail();
    }

    private static string Levels(int[] values, string[] names, string label)
    {
        var learned = names
            .Select((name, i) => (name, level: i < values.Length ? values[i] : 0))
            .Where(x => x.level > 0)
            .Select(x => $"{x.name} {x.level}");
        string text = string.Join(" · ", learned);
        return text.Length == 0 ? $"{label}: 없음" : $"{label}: {text}";
    }

    /// <summary>언젠가 스스로 떠날 수 있는 사람인가.</summary>
    private static bool Movable(PersonTable.Row person, bool active) =>
        person.Id < PersonTable.VoyagerCount
        || (active && person.Id < PersonTable.MovingEnd && person.Kind != 2);

    /// <summary>상태 갈래 — <see cref="StateOf"/> 와 같은 차례로 가른다.</summary>
    private static State StateKind(PersonTable.Row person, bool active, bool onRoad)
    {
        if (onRoad) return State.Sailing;
        if (person.Id < PersonTable.VoyagerCount) return State.Script;
        if (person.Id >= PersonTable.MovingEnd) return State.Event;
        if (!active) return State.Hidden;
        if (person.Kind == 2) return State.Fixed;
        if (person.Wait < 0) return State.Resting;
        return State.Ready;
    }

    private string StateOf(PersonTable.Row person, bool active, bool onRoad)
    {
        if (onRoad) return person.Dest == PersonWorld.SpotDest ? "발견물 자리로 항해 중" : "항해 중";

        if (person.Id < PersonTable.VoyagerCount)
        {
            var next = _world.ScriptAhead(person, _game.Player.Date, ScriptMonths).FirstOrDefault();
            return next.Year > 0
                ? $"대본 {next.Year}.{next.Month:D2} → {(next.ToCity ? CityOf(next.City) : "발견물 자리")}"
                : "남은 대본 없음";
        }

        if (person.Id >= PersonTable.MovingEnd) return "이벤트 인물 — 안 움직임";
        if (!active) return "나오지 않음(등장·나이)";
        if (person.Kind == 2) return "갈래 2 — 안 움직임";
        if (person.Wait < 0) return $"쉬는 중 {-person.Wait}일 남음";
        // 굴리는 때와 확률은 개발 창에서 바꿀 수 있다 — 원본은 매월 1일 5분의 1이다.
        int every = Local.Settings.GameSettings.PersonRollDays;
        string when = every == 0 ? "매월 1일" : $"{every}일마다";
        int odds = Local.Settings.GameSettings.PersonMoveOdds;
        return odds == 1 ? $"{when} 반드시 떠남" : $"{when} {odds}분의 1로 떠남";
    }

    /// <summary>고른 사람의 자세한 것 — 갈 수 있는 도시나 앞으로의 대본.</summary>
    private void ShowDetail()
    {
        if (_grid.SelectedItem is not Row row)
        {
            _detailScroll.Visibility = Visibility.Collapsed;
            return;
        }

        _detailScroll.Visibility = Visibility.Visible;
        var person = row.Source;
        var lines = new List<string> { $"{person.Name} (#{person.Id})", "" };

        if (PersonWorld.Moving(person))
            lines.Add($"{CityOf(person.From)} → {row.To}  ·  남은 날 {row.Left}");

        if (person.Id < PersonTable.VoyagerCount)
        {
            lines.Add($"앞으로 {ScriptMonths / 12}년의 대본");
            var moves = _world.ScriptAhead(person, _game.Player.Date, ScriptMonths).ToList();
            if (moves.Count == 0) lines.Add("  (없음)");
            foreach (var move in moves)
                lines.Add($"  {move.Year}.{move.Month:D2}  {(move.ToCity ? CityOf(move.City) : $"발견물 {move.Discovery} 자리")}");
        }
        else if (person.City >= 0)
        {
            var picks = _world.CandidatesOf(person);
            lines.Add($"다음에 갈 수 있는 도시 {picks.Count}곳 (갈래: {row.Kind})");
            lines.Add(picks.Count == 0 ? "  (없음 — 굴려도 안 떠난다)"
                                       : "  " + string.Join(", ", picks.Select(CityOf)));
        }
        else if (!PersonWorld.Moving(person))
        {
            lines.Add("어느 도시에도 앉아 있지 않아 후보를 모을 수 없습니다.");
        }

        _detail.Text = string.Join("\n", lines);
    }
}
