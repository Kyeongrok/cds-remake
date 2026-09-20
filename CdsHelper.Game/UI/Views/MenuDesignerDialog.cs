using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using CdsHelper.Game.Local.Helpers;
using CdsHelper.Game.Local.Settings;
using CdsHelper.Support.Local.Settings;
using CdsHelper.Support.UI.Units;

namespace CdsHelper.Game.UI.Views;

/// <summary>
/// 메뉴 디자이너 — 게임에 나는 창들을 손잡이로 짜 보고 눈으로 맞대는 자리.
/// </summary>
/// <remarks>
/// 게임 창은 밤색 판에 띠 단추를 얹은 몇 가지 꼴이 되풀이된다. 그 꼴을 코드에 적어 넣고
/// 놀이를 다시 구워 보는 대신, 여기서 <b>제목을 넣고 빼고 · 줄을 적고 · 폭과 줄 수를
/// 굴려</b> 그 자리에서 본다.
///
/// 미리보기는 <b>게임이 쓰는 그 조각들</b>로 짓는다 — 띠 단추는
/// <see cref="GameButton"/>, 글씨는 <see cref="GameUi.GameLabel"/>(게임 글꼴), 빛깔은
/// <see cref="GameUi"/> 의 것이다. 그래서 여기서 보이는 대로 게임에서도 보인다.
///
/// <b>어디까지 놀이에 드나.</b> 「단추 여백」은 <see cref="GameSettings.BandPad"/> 라
/// 곧바로 든다. 나머지(폭 · 줄 수 · 제목 · 글)는 <b>재어 보는 값</b>이라 창마다 코드에
/// 옮겨 적어야 한다 — 「값 보기」가 그 수를 한 줄로 내어 준다.
/// </remarks>
public sealed class MenuDesignerDialog : GameWindow
{
    /// <summary>지을 수 있는 꼴.</summary>
    private enum Kind
    {
        /// <summary>명령 창 — 띠 단추를 세로로 쌓은 것. 도시·일기토의 그 창이다.</summary>
        Menu,

        /// <summary>물음창 — 글 한 마디에 YES · NO. 알림은 단추 하나다.</summary>
        Ask,

        /// <summary>목록 — 줄을 죽 세우고 하나를 고르는 것. 시장·소지품이 그렇다.</summary>
        List,
    }

    private static readonly (string Name, Kind What)[] Kinds =
    [
        ("명령 창", Kind.Menu), ("물음창", Kind.Ask), ("목록", Kind.List),
    ];

    private readonly ComboBox _kind = new() { Width = 120 };

    private readonly CheckBox _titleOn = new() { Content = "제목 넣기", IsChecked = true, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBox _title = new() { Text = "커맨드", Width = 150 };

    private readonly TextBox _body = new()
    {
        Text = "데이터를 겹쳐 쓰겠습니까?",
        AcceptsReturn = true,
        TextWrapping = TextWrapping.Wrap,
        Height = 54,
    };

    private readonly TextBox _items = new()
    {
        Text = "항해\n상업\n인물\n정보\n기능\n떠난다",
        AcceptsReturn = true,
        Height = 120,
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
    };

    /// <summary>글자에 맞춰 폭을 잴지. 끄면 <see cref="_width"/> 로 박는다.</summary>
    private readonly CheckBox _fit = new() { Content = "글자에 맞춤", IsChecked = true, VerticalAlignment = VerticalAlignment.Center };

    private readonly NumericSpinner _width = new()
    {
        Minimum = 40, Maximum = 360, Step = 4, DecimalPlaces = 0, Value = 110, Width = 84,
    };

    /// <summary>한 화면에 낼 줄 수. 넘으면 굴려 본다.</summary>
    private readonly NumericSpinner _most = new()
    {
        Minimum = 1, Maximum = 24, Step = 1, DecimalPlaces = 0, Value = 8, Width = 84,
    };

    /// <summary>띠 마구리가 앉을 좌우 여백 — <b>이것만은 놀이에 곧바로 든다</b>.</summary>
    private readonly NumericSpinner _pad = new()
    {
        Minimum = GameSettings.MinBandPad, Maximum = GameSettings.MaxBandPad,
        Step = 1, DecimalPlaces = 0, Width = 84,
    };

    private readonly NumericSpinner _zoom = new()
    {
        Minimum = 1, Maximum = 4, Step = 1, DecimalPlaces = 0, Value = 2, Width = 84,
    };

    private readonly CheckBox _yesNo = new() { Content = "YES · NO", IsChecked = true, VerticalAlignment = VerticalAlignment.Center };

    private readonly Border _stage = new()
    {
        Background = Brushes.Black,
        Padding = new Thickness(20),
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center,
    };

    private readonly TextBlock _note = new()
    {
        Margin = new Thickness(12, 6, 12, 8),
        TextWrapping = TextWrapping.Wrap,
    };

    public MenuDesignerDialog()
    {
        Title = "메뉴 디자이너";
        Width = 1080;
        Height = 760;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        _pad.Value = GameSettings.BandPad;

        var page = new DockPanel();
        DockPanel.SetDock(_note, Dock.Bottom);
        page.Children.Add(_note);

        var split = new Grid();
        split.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(330) });
        split.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var hand = Handles();
        Grid.SetColumn(hand, 0);
        split.Children.Add(hand);

        var show = new Border
        {
            Background = Brushes.DimGray,
            Child = new ScrollViewer
            {
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Content = _stage,
            },
        };
        Grid.SetColumn(show, 1);
        split.Children.Add(show);

        page.Children.Add(split);
        Content = page;

        Loaded += (_, _) =>
        {
            Dress();
            Draw();
        };
    }

    /// <summary>
    /// 게임 조각을 읽어 둔다 — <b>띠 그림과 게임 글꼴</b>이 있어야 진짜 꼴이 나온다.
    /// </summary>
    /// <remarks>
    /// <see cref="GameUi.Sprites"/> 와 <see cref="GameUi.Font"/> 는 놀이가 뜰 때
    /// <see cref="ShipMapWindow"/> 가 한 번 넣는다. 헬퍼에서는 놀이가 안 도니 비어 있어,
    /// 띠 단추가 <b>맨 네모</b>로 나고 글씨도 윈도 글꼴로 나온다. 여기서 채워 넣는다 —
    /// 세이브를 연 자리에서 게임 폴더를 찾는다.
    /// </remarks>
    private void Dress()
    {
        string dir = Path.GetDirectoryName(AppSettings.LastSaveFilePath) ?? "";

        GameUi.Sprites ??= UiSprites.Open(dir);
        GameUi.Font ??= GameFont.Open(dir);

        if (GameUi.Sprites == null)
            _note.Text = $"띠 그림을 못 읽었습니다 — {UiSprites.LastError}."
                       + "  파일 → 세이브 파일 열기 로 게임 폴더를 먼저 일러 주십시오.";
    }

    // ── 손잡이 ──────────────────────────────────────────────────────────────

    private UIElement Handles()
    {
        foreach (var (name, _) in Kinds) _kind.Items.Add(name);
        _kind.SelectedIndex = 0;
        _kind.SelectionChanged += (_, _) => Draw();

        foreach (var box in new[] { _titleOn, _fit, _yesNo })
        {
            box.Checked += (_, _) => Draw();
            box.Unchecked += (_, _) => Draw();
        }

        foreach (var spin in new[] { _width, _most, _zoom })
            spin.ValueChanged += (_, _) => Draw();

        // 단추 여백만은 설정에 바로 적는다 — 놀이가 그 값을 본다.
        _pad.ValueChanged += (_, _) =>
        {
            GameSettings.BandPad = (int)_pad.Value;
            Draw();
        };

        _title.TextChanged += (_, _) => Draw();
        _body.TextChanged += (_, _) => Draw();
        _items.TextChanged += (_, _) => Draw();

        var rows = new StackPanel { Margin = new Thickness(12, 10, 12, 10) };
        rows.Children.Add(Line("꼴", _kind));
        rows.Children.Add(Line("제목", _titleOn, _title));
        rows.Children.Add(Label("글 (물음창)"));
        rows.Children.Add(_body);
        rows.Children.Add(Label("줄 (한 줄에 하나)"));
        rows.Children.Add(_items);
        rows.Children.Add(Line("가로", _fit, _width));
        rows.Children.Add(Line("한 화면 줄 수", _most));
        rows.Children.Add(Line("단추 여백", _pad));
        rows.Children.Add(Line("배율", _zoom));
        rows.Children.Add(Line("물음", _yesNo));

        var see = new Button
        {
            Content = "값 보기",
            Padding = new Thickness(10, 3, 10, 3),
            Margin = new Thickness(0, 10, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Left,
        };
        see.Click += (_, _) => Tell();
        rows.Children.Add(see);

        return new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = rows,
        };
    }

    private static TextBlock Label(string text) => new()
    {
        Text = text,
        Margin = new Thickness(0, 10, 0, 2),
        FontWeight = FontWeights.Bold,
    };

    private static UIElement Line(string name, params UIElement[] what)
    {
        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 6, 0, 0),
        };
        row.Children.Add(new TextBlock
        {
            Text = name,
            Width = 92,
            VerticalAlignment = VerticalAlignment.Center,
        });
        foreach (var one in what)
        {
            if (one is FrameworkElement box) box.Margin = new Thickness(0, 0, 6, 0);
            row.Children.Add(one);
        }
        return row;
    }

    // ── 미리보기 ────────────────────────────────────────────────────────────

    /// <summary>적어 넣은 줄들 — 빈 줄은 버린다.</summary>
    private string[] Items() =>
        [.. _items.Text.Split('\n').Select(one => one.Trim('\r', ' ')).Where(one => one.Length > 0)];

    /// <summary>이 꼴에서 쓸 단추 폭 — 「글자에 맞춤」이면 가장 긴 줄로 잰다.</summary>
    private double Wide()
    {
        if (_fit.IsChecked != true) return _width.Value;

        var items = Items();
        return items.Length == 0 ? _width.Value : items.Max(GameUi.BandWidthFor);
    }

    private void Draw()
    {
        var what = Kinds[Math.Max(0, _kind.SelectedIndex)].What;
        var body = what switch
        {
            Kind.Ask => AskBox(),
            Kind.List => ListBox_(),
            _ => MenuBox(),
        };

        body.LayoutTransform = new ScaleTransform(_zoom.Value, _zoom.Value);
        _stage.Child = body;
        Tell();
    }

    /// <summary>명령 창 — 띠 단추를 세로로 쌓는다.</summary>
    private FrameworkElement MenuBox()
    {
        double wide = Wide();
        var focus = new GameUi.FocusGroup();

        var column = new StackPanel();
        int most = (int)_most.Value;
        foreach (string text in Items().Take(most))
        {
            var key = focus.Add(text, () => { }, wide);
            key.Height = UiSprites.BandHeight;
            key.Margin = new Thickness(0, 0, 0, 2);
            column.Children.Add(key);
        }

        var stack = new StackPanel();
        if (_titleOn.IsChecked == true) stack.Children.Add(GameUi.TitleBar(_title.Text, null));
        stack.Children.Add(new Border { Padding = new Thickness(3), Child = column });

        return new Border
        {
            Background = GameUi.MenuBack,
            BorderBrush = GameUi.Edge,
            BorderThickness = new Thickness(1),
            Child = stack,
        };
    }

    /// <summary>물음창 — 글 한 마디에 단추 한둘.</summary>
    private FrameworkElement AskBox()
    {
        double wide = Wide();

        var text = new StackPanel { Margin = new Thickness(12, 10, 12, 8) };
        foreach (string line in GameUi.Wrap(_body.Text.Replace("\r", ""), Math.Max(80, wide * 2)))
            text.Children.Add(new GameUi.GameLabel(GameFont.ButtonColor)
            {
                Text = line,
                HorizontalAlignment = HorizontalAlignment.Center,
                FallbackBrush = GameUi.Text,
            });

        var keys = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 4, 0, 10),
        };
        var focus = new GameUi.FocusGroup();
        foreach (string name in _yesNo.IsChecked == true ? ["YES", "NO"] : new[] { "OK" })
        {
            var key = focus.Add(name, () => { }, Math.Max(56, wide / 2));
            key.Height = UiSprites.BandHeight;
            key.Margin = new Thickness(6, 0, 6, 0);
            keys.Children.Add(key);
        }

        var stack = new StackPanel();
        if (_titleOn.IsChecked == true) stack.Children.Add(GameUi.TitleBar(_title.Text, null));
        stack.Children.Add(text);
        stack.Children.Add(keys);

        return new Border
        {
            Background = GameUi.Back,
            BorderBrush = GameUi.Edge,
            BorderThickness = new Thickness(2),
            Child = stack,
        };
    }

    /// <summary>목록 — 줄을 죽 세운다. 줄 수를 넘으면 굴린다.</summary>
    private FrameworkElement ListBox_()
    {
        double wide = Math.Max(Wide(), 120);
        int most = (int)_most.Value;

        var rows = new StackPanel();
        var items = Items();
        for (int at = 0; at < items.Length; at++)
        {
            rows.Children.Add(new Border
            {
                Background = at % 2 == 0 ? GameUi.ItemFill : GameUi.PageFill,
                BorderBrush = GameUi.ItemEdge,
                BorderThickness = new Thickness(0, 0, 0, 1),
                Padding = new Thickness(6, 2, 6, 2),
                Width = wide,
                Child = new GameUi.GameLabel(GameFont.BlackColor)
                {
                    Text = items[at],
                    HorizontalAlignment = HorizontalAlignment.Left,
                    FallbackBrush = Brushes.Black,
                },
            });
        }

        // 한 화면에 낼 줄 수만큼만 보이고 나머지는 굴려 본다 — 게임도 그렇다.
        double tall = most * (UiSprites.BandHeight + 5);
        var scroll = new ScrollViewer
        {
            MaxHeight = tall,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content = rows,
        };

        var stack = new StackPanel();
        if (_titleOn.IsChecked == true) stack.Children.Add(GameUi.TitleBar(_title.Text, null));
        stack.Children.Add(new Border { Padding = new Thickness(3), Child = scroll });

        return new Border
        {
            Background = GameUi.Back,
            BorderBrush = GameUi.Edge,
            BorderThickness = new Thickness(2),
            Child = stack,
        };
    }

    // ── 값 보기 ─────────────────────────────────────────────────────────────

    /// <summary>지금 값을 한 줄로 적는다 — 코드에 옮겨 적을 때 보라고.</summary>
    private void Tell()
    {
        var items = Items();
        double wide = Wide();
        string longest = items.Length == 0 ? "" : items.OrderByDescending(GameUi.BandWidthFor).First();

        _note.Text =
            $"꼴 {Kinds[Math.Max(0, _kind.SelectedIndex)].Name}"
            + $" · 제목 {(_titleOn.IsChecked == true ? $"「{_title.Text}」" : "없음")}"
            + $" · 줄 {items.Length}개 (한 화면 {(int)_most.Value})"
            + $" · 가로 {wide:0}점 {(_fit.IsChecked == true ? $"(가장 긴 줄 「{longest}」에 맞춤)" : "(박은 값)")}"
            + $" · 단추 여백 {(int)_pad.Value}"
            + "   —  여백만 놀이에 곧바로 들고, 나머지는 창마다 코드에 옮겨 적는 값입니다.";
    }
}
