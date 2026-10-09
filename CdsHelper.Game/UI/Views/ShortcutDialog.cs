using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using CdsHelper.Game.Local.Settings;

namespace CdsHelper.Game.UI.Views;

/// <summary>게임에서 쓰는 단축키를 확인하고 다시 지정하는 창.</summary>
/// <remarks>
/// 탭이 둘이다 — 「일반」은 창을 여는 글쇠, 「항해」는 뱃머리를 세우는 글쇠다(<see cref="GameSettings.SailActions"/>).
/// 항해 글쇠는 숫자판이 없는 자판을 위해 얹은 것이라, 숫자판 조타(1~9 · 5 · 0)는 이것과 상관없이 늘 먹는다.
/// </remarks>
public sealed class ShortcutDialog : GameWindow
{
    /// <summary>두 탭의 글쇠 칸 전부 — 같은 글쇠가 두 군데에 들어가지 않게 서로 본다.</summary>
    private readonly List<TextBox> _boxes = [];

    private ShortcutDialog()
    {
        Title = "단축키";
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.WidthAndHeight;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        Background = GameUi.Back;

        var general = new StackPanel();
        general.Children.Add(Row("저장", GameSettings.SaveKey, key => GameSettings.SaveKey = key));
        general.Children.Add(Row("발견물 지도", GameSettings.MapKey, key => GameSettings.MapKey = key));
        general.Children.Add(Row("모드", GameSettings.ModKey, key => GameSettings.ModKey = key));
        general.Children.Add(Row("소지품 정보", GameSettings.ItemsKey, key => GameSettings.ItemsKey = key));
        general.Children.Add(Row("힌트 정보", GameSettings.HintsKey, key => GameSettings.HintsKey = key));
        general.Children.Add(Row("인물정보", GameSettings.PersonKey, key => GameSettings.PersonKey = key));
        general.Children.Add(Row("후원자 정보", GameSettings.PatronKey, key => GameSettings.PatronKey = key));
        general.Children.Add(Note("각 칸을 누른 뒤 지정할 글쇠를 누르십시오."));

        var sail = new StackPanel();
        foreach (var (id, label, _) in GameSettings.SailActions)
            sail.Children.Add(Row(label, GameSettings.SailKey(id), key => GameSettings.SetSailKey(id, key), clearable: true));
        sail.Children.Add(Note("각 칸을 누른 뒤 지정할 글쇠를 누르십시오. Delete 로 비웁니다.\n"
                               + "동서남북 글쇠 둘을 함께 누르면 대각선으로 갑니다.\n"
                               + "숫자 글쇠 1~9(방위) · 5(정지) · 0(커맨드)은 늘 쓸 수 있습니다."));

        // 두 판을 한 칸에 겹쳐 두고 안 보이는 쪽은 Hidden 으로 — 탭을 넘겨도 창 크기가 안 바뀐다.
        var pages = new Grid();
        pages.Children.Add(general);
        pages.Children.Add(sail);

        var stack = new StackPanel();
        stack.Children.Add(GameUi.TitleBar("단축키", Close));
        stack.Children.Add(Tabs(("일반", general), ("항해", sail)));
        stack.Children.Add(pages);
        stack.Children.Add(new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 10),
            // 닫기도 게임 띠 단추다 — 윈도 글꼴 상자를 쓰면 위 줄들(게임 띠)과 결이 다르다.
            Children = { new GameButton("닫기", Close, width: 110) },
        });

        Content = new Border
        {
            Background = GameUi.Back,
            BorderBrush = GameUi.Edge,
            BorderThickness = new Thickness(2),
            Margin = new Thickness(4),
            Child = stack,
        };

        KeyDown += (_, e) => { if (e.Key is Key.Escape) Close(); };
        MouseRightButtonUp += (_, _) => Close();
    }

    /// <summary>탭 머리 — 누른 쪽 판만 보이고 머리는 밝게 선다(모드 창과 같은 꼴).</summary>
    private static FrameworkElement Tabs(params (string Text, FrameworkElement Page)[] tabs)
    {
        var bar = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(14, 8, 14, 0) };
        var heads = new List<(Border Head, FrameworkElement Page)>();

        void Select(FrameworkElement page)
        {
            foreach (var (head, p) in heads)
            {
                bool on = ReferenceEquals(p, page);
                p.Visibility = on ? Visibility.Visible : Visibility.Hidden;
                head.Background = on ? new SolidColorBrush(Color.FromArgb(0x60, 0xFF, 0xFF, 0xFF)) : Brushes.Transparent;
                ((TextBlock)head.Child).Opacity = on ? 1 : 0.6;
            }
        }

        foreach (var (text, page) in tabs)
        {
            var head = new Border
            {
                BorderBrush = GameUi.Edge,
                BorderThickness = new Thickness(1, 1, 1, 0),
                Padding = new Thickness(12, 4, 12, 4),
                Margin = new Thickness(0, 0, 4, 0),
                Cursor = Cursors.Hand,
                Child = new TextBlock { Text = text, Foreground = GameUi.Text, FontWeight = FontWeights.Bold, FontSize = 15 },
            };
            head.MouseLeftButtonDown += (_, _) => Select(page);
            heads.Add((head, page));
            bar.Children.Add(head);
        }
        Select(tabs[0].Page);
        return bar;
    }

    private static UIElement Note(string text) => new TextBlock
    {
        Text = text,
        Foreground = GameUi.Text,
        Margin = new Thickness(14, 6, 14, 10),
    };

    /// <summary>이름 한 칸에 글쇠 한 칸인 줄. 칸을 누르고 글쇠를 누르면 곧바로 적힌다.</summary>
    /// <param name="clearable">Delete · 백스페이스로 비울 수 있는지 — 항해 글쇠는 안 쓰는 것을 비워 둔다.</param>
    private UIElement Row(string name, string key, Action<string> store, bool clearable = false)
    {
        var box = new TextBox
        {
            Width = 80,
            IsReadOnly = true,
            Focusable = true,
            TextAlignment = TextAlignment.Center,
            Margin = new Thickness(8, 0, 0, 0),
            Text = key,
        };
        box.PreviewKeyDown += (_, e) => Assign(e, box, store, clearable);
        _boxes.Add(box);

        return new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(14, 8, 14, 0),
            Children =
            {
                new TextBlock
                {
                    Text = name,
                    Width = 110,
                    Foreground = GameUi.Text,
                    FontSize = 15,
                    VerticalAlignment = VerticalAlignment.Center,
                },
                box,
            },
        };
    }

    private void Assign(KeyEventArgs e, TextBox box, Action<string> store, bool clearable)
    {
        if (e.Key is Key.LeftShift or Key.RightShift or Key.LeftCtrl or Key.RightCtrl
            or Key.LeftAlt or Key.RightAlt or Key.System or Key.None or Key.Escape or Key.Tab)
            return;

        if (clearable && e.Key is Key.Delete or Key.Back)
        {
            box.Text = "";
            store("");
            e.Handled = true;
            return;
        }

        string key = e.Key.ToString();
        if (_boxes.Any(b => b != box && string.Equals(key, b.Text, StringComparison.OrdinalIgnoreCase)))
        {
            e.Handled = true;
            NoticeDialog.Show(this, "같은 글쇠를 두 단축키에 함께 지정할 수 없습니다.");
            return;
        }

        box.Text = key;
        store(key);
        e.Handled = true;
    }

    public static void Show(Window owner)
    {
        new ShortcutDialog { Owner = owner }.ShowDialog();
    }
}
