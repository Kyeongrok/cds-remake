using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using CdsHelper.Support.Local.Models;

namespace CdsHelper.Game.UI.Views;

/// <summary>현재 주인공의 기본 능력치·기술·언어를 편집한다. 적용 전에는 Player를 바꾸지 않는다.</summary>
public sealed class PlayerAbilityEditDialog : GameWindow
{
    private readonly Player _player;
    private readonly TextBox[] _fields = new TextBox[Ability.Shown];
    private readonly TextBlock[] _effective = new TextBlock[Ability.Shown];
    private readonly ComboBox[] _skillFields = new ComboBox[Skill.Names.Length];
    private readonly ComboBox[] _tongueFields = new ComboBox[Skill.Languages.Length];
    private readonly TabControl _tabs = new();
    private static readonly Brush Page = new SolidColorBrush(Color.FromRgb(0xFF, 0xEF, 0xD6));
    private readonly TextBlock _error = new()
    {
        Foreground = Brushes.DarkRed,
        TextWrapping = TextWrapping.Wrap,
        MinHeight = 24,
        Margin = new Thickness(0, 8, 0, 0),
    };

    private PlayerAbilityEditDialog(Player player)
    {
        _player = player;
        Title = "주인공 능력치 · 기술 · 언어";
        Width = 620;
        Height = 620;
        MaxHeight = Math.Max(400, SystemParameters.WorkArea.Height - 40);
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        Background = Page;
        Foreground = Brushes.Black;
        FontSize = 14;

        var body = new DockPanel { Margin = new Thickness(20) };
        var heading = new StackPanel();
        DockPanel.SetDock(heading, Dock.Top);
        body.Children.Add(heading);
        heading.Children.Add(new TextBlock
        {
            Text = string.IsNullOrWhiteSpace(player.Name) ? "주인공 능력치" : player.Name,
            FontSize = 18, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 0, 8),
        });
        heading.Children.Add(new TextBlock
        {
            Text = "현재 주인공의 값입니다. 기본 능력치 1~100, 기술·언어 0~3.\n모든 탭의 변경 사항은 ‘적용’ 후 게임을 저장하면 유지됩니다.",
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 16),
        });

        var grid = new Grid();
        foreach (double width in new[] { 100d, 100d, 120d, 130d })
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(width) });
        void Cell(UIElement element, int row, int column)
        {
            Grid.SetRow(element, row);
            Grid.SetColumn(element, column);
            grid.Children.Add(element);
        }
        static TextBlock Label(string text) => new()
        {
            Text = text, VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 7, 0, 7),
        };
        grid.RowDefinitions.Add(new RowDefinition());
        string[] headers = ["능력치", "기본 능력치", "작위 보너스", "적용 시 최종값"];
        for (int i = 0; i < headers.Length; i++)
        {
            var header = Label(headers[i]);
            header.FontWeight = FontWeights.Bold;
            Cell(header, 0, i);
        }
        for (int i = 0; i < _fields.Length; i++)
        {
            int which = i;
            grid.RowDefinitions.Add(new RowDefinition());
            Cell(Label(Ability.Names[i]), i + 1, 0);
            var field = new TextBox
            {
                Text = Ability.Display(player.Abilities[i]).ToString(CultureInfo.InvariantCulture),
                Width = 76, Padding = new Thickness(6, 4, 6, 4),
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalContentAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 4, 0, 4),
                TextAlignment = TextAlignment.Right,
                Foreground = Brushes.Black, Background = Brushes.White,
            };
            AutomationProperties.SetName(field, Ability.Names[i] + " 기본값");
            AutomationProperties.SetAutomationId(field, "Ability" + i);
            _fields[i] = field;
            Cell(field, i + 1, 1);
            Cell(Label(Player.AbilityBonus(player, i).ToString("+0;-0;0", CultureInfo.InvariantCulture)), i + 1, 2);
            _effective[i] = Label("");
            Cell(_effective[i], i + 1, 3);
            field.TextChanged += (_, _) => { _error.Text = ""; Preview(which); };
            Preview(i);
        }
        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 8, 0, 0),
        };
        var apply = new Button { Content = "적용", IsDefault = true, MinWidth = 82, Padding = new Thickness(14, 6, 14, 6) };
        AutomationProperties.SetAutomationId(apply, "ApplyAbilities");
        apply.Click += (_, _) => Apply();
        var cancel = new Button
        {
            Content = "취소", MinWidth = 82, Padding = new Thickness(14, 6, 14, 6),
            Margin = new Thickness(8, 0, 0, 0),
        };
        cancel.Click += (_, _) => DialogResult = false;
        buttons.Children.Add(apply);
        buttons.Children.Add(cancel);
        DockPanel.SetDock(buttons, Dock.Bottom);
        body.Children.Add(buttons);
        DockPanel.SetDock(_error, Dock.Bottom);
        body.Children.Add(_error);
        _tabs.Background = Page;
        _tabs.Foreground = Brushes.Black;
        _tabs.Items.Add(Tab("기본 능력치", grid));
        _tabs.Items.Add(Tab("기술 13종", Levels(Skill.Names, _skillFields, player.LevelOf, "Skill")));
        _tabs.Items.Add(Tab("언어 14종", Levels(Skill.Languages, _tongueFields, player.TongueOf, "Tongue")));
        body.Children.Add(_tabs);
        Content = body;
        Loaded += (_, _) => { _fields[0].Focus(); _fields[0].SelectAll(); };
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) { e.Handled = true; DialogResult = false; }
            // GameWindow의 게임 단축키가 편집 중 저장·다른 메뉴를 열지 않게 한다.
            else if (e.Key >= Key.A && e.Key <= Key.Z && Keyboard.FocusedElement is not TextBox)
                e.Handled = true;
        };
    }

    private static TabItem Tab(string title, UIElement contents) => new()
    {
        Header = title, Padding = new Thickness(14, 7, 14, 7), Foreground = Brushes.Black,
        Content = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content = new Border { Padding = new Thickness(14), Background = Page, Child = contents },
        },
    };

    private UIElement Levels(string[] names, ComboBox[] fields, Func<string, int> current, string id)
    {
        var body = new StackPanel();
        body.Children.Add(new TextBlock
        {
            Text = "0: 미습득 · 1~3: 습득 레벨\n개발 편집은 학습 비용·시간·지력에 따른 학습 한도를 적용하지 않습니다.",
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 10),
        });
        var grid = new Grid();
        foreach (double width in new[] { 150d, 70d, 24d, 150d, 70d })
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(width) });
        int rows = (names.Length + 1) / 2;
        for (int row = 0; row < rows; row++) grid.RowDefinitions.Add(new RowDefinition());
        for (int i = 0; i < names.Length; i++)
        {
            int column = i / rows * 3, row = i % rows;
            var label = new TextBlock { Text = names[i], VerticalAlignment = VerticalAlignment.Center };
            Grid.SetRow(label, row);
            Grid.SetColumn(label, column);
            grid.Children.Add(label);
            var field = new ComboBox
            {
                ItemsSource = Enumerable.Range(0, Skill.MaxLevel + 1).ToArray(),
                SelectedIndex = current(names[i]), IsEditable = false,
                Width = 60, Padding = new Thickness(6, 4, 6, 4),
                Margin = new Thickness(0, 4, 0, 4),
                Foreground = Brushes.Black, Background = Brushes.White,
            };
            AutomationProperties.SetName(field, names[i] + " 레벨");
            AutomationProperties.SetAutomationId(field, id + i);
            field.SelectionChanged += (_, _) => _error.Text = "";
            fields[i] = field;
            Grid.SetRow(field, row);
            Grid.SetColumn(field, column + 1);
            grid.Children.Add(field);
        }
        body.Children.Add(grid);
        return body;
    }

    private bool LevelsValid(ComboBox[] fields, string[] names, int tab)
    {
        for (int i = 0; i < fields.Length; i++)
        {
            if (fields[i].SelectedIndex >= 0 && fields[i].SelectedIndex <= Skill.MaxLevel) continue;
            _tabs.SelectedIndex = tab;
            _error.Text = $"{names[i]}: 0~3 사이의 레벨을 선택해 주세요.";
            fields[i].Focus();
            return false;
        }
        return true;
    }

    private bool Value(int which, out int value) =>
        int.TryParse(_fields[which].Text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out value)
        && value >= 1 && value <= Ability.Max;

    private void Preview(int which)
    {
        _effective[which].Text = Value(which, out int value)
            ? Math.Min(value + Player.AbilityBonus(_player, which), Ability.Max).ToString(CultureInfo.InvariantCulture)
            : "—";
    }

    private void Apply()
    {
        // 신앙심 등 화면에 내지 않은 값은 현재 주인공의 것을 그대로 보존한다.
        int[] next = [.. _player.Abilities];
        for (int i = 0; i < _fields.Length; i++)
        {
            if (!Value(i, out int value))
            {
                _error.Text = $"{Ability.Names[i]}: 1~100 사이의 정수를 입력해 주세요.";
                _tabs.SelectedIndex = 0;
                _fields[i].Focus();
                _fields[i].SelectAll();
                return;
            }
            next[i] = Ability.Store(value);
        }
        // 다른 탭의 잘못된 입력까지 모두 확인한 뒤에만 한꺼번에 적용한다.
        if (!LevelsValid(_skillFields, Skill.Names, 1) || !LevelsValid(_tongueFields, Skill.Languages, 2)) return;
        bool changed = !next.SequenceEqual(_player.Abilities);
        if (changed) _player.SetAbilities(next);
        for (int i = 0; i < _skillFields.Length; i++)
        {
            int level = _skillFields[i].SelectedIndex;
            if (_player.LevelOf(Skill.Names[i]) == level) continue;
            _player.SetSkill(Skill.Names[i], level);
            changed = true;
        }
        for (int i = 0; i < _tongueFields.Length; i++)
        {
            int level = _tongueFields[i].SelectedIndex;
            if (_player.TongueOf(Skill.Languages[i]) == level) continue;
            _player.SetTongue(Skill.Languages[i], level);
            changed = true;
        }
        DialogResult = changed;
    }

    /// <returns>실제로 능력치를 바꿔 적용한 경우만 true.</returns>
    public static bool Show(Window owner, Player player) =>
        new PlayerAbilityEditDialog(player) { Owner = owner }.ShowDialog() == true;
}
