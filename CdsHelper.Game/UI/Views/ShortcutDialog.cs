using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using CdsHelper.Game.Local.Settings;

namespace CdsHelper.Game.UI.Views;

/// <summary>게임에서 쓰는 단축키를 확인하고 다시 지정하는 창.</summary>
public sealed class ShortcutDialog : GameWindow
{
    private readonly TextBox _save = KeyBox();
    private readonly TextBox _map = KeyBox();

    private ShortcutDialog()
    {
        Title = "단축키";
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.WidthAndHeight;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        Background = GameUi.Back;

        _save.Text = GameSettings.SaveKey;
        _map.Text = GameSettings.MapKey;
        _save.PreviewKeyDown += (_, e) => Assign(e, _save, isSave: true);
        _map.PreviewKeyDown += (_, e) => Assign(e, _map, isSave: false);

        var stack = new StackPanel();
        stack.Children.Add(GameUi.TitleBar("단축키", Close));
        stack.Children.Add(Row("저장", _save));
        stack.Children.Add(Row("발견물 지도", _map));
        stack.Children.Add(new TextBlock
        {
            Text = "각 칸을 누른 뒤 지정할 글쇠를 누르십시오.",
            Foreground = GameUi.Text,
            Margin = new Thickness(14, 6, 14, 10),
        });
        stack.Children.Add(new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 10),
            Children = { GameUi.PushButton("닫기", Close) },
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

    private static TextBox KeyBox() => new()
    {
        Width = 80,
        IsReadOnly = true,
        Focusable = true,
        TextAlignment = TextAlignment.Center,
        Margin = new Thickness(8, 0, 0, 0),
    };

    private static UIElement Row(string name, TextBox box) => new StackPanel
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

    private void Assign(KeyEventArgs e, TextBox box, bool isSave)
    {
        if (e.Key is Key.LeftShift or Key.RightShift or Key.LeftCtrl or Key.RightCtrl
            or Key.LeftAlt or Key.RightAlt or Key.System or Key.None)
            return;

        string key = e.Key.ToString();
        string other = isSave ? _map.Text : _save.Text;
        if (string.Equals(key, other, StringComparison.OrdinalIgnoreCase))
        {
            e.Handled = true;
            NoticeDialog.Show(this, "같은 글쇠를 두 단축키에 함께 지정할 수 없습니다.");
            return;
        }

        box.Text = key;
        if (isSave) GameSettings.SaveKey = key;
        else GameSettings.MapKey = key;
        e.Handled = true;
    }

    public static void Show(Window owner)
    {
        new ShortcutDialog { Owner = owner }.ShowDialog();
    }
}
