using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace CdsHelper.Game.UI.Views;

/// <summary>
/// 색 하나를 고르는 창 — 빨강 · 초록 · 파랑 막대와 「RRGGBB」 칸, 처음 색 · 고르는 색을 나란히 보인다.
/// </summary>
/// <remarks>
/// 개발 창 「대항2」 탭이 대항해시대2 스타일 색(<see cref="Dh2Style.Slots"/>)을 고칠 때 연다.
/// 게임 쪽은 WinForms 를 안 쓰므로 색 고르기 창을 WPF 로 따로 둔다.
/// </remarks>
public sealed class ColorPickDialog : GameWindow
{
    private Color _color;
    private bool _ok, _syncing;

    private readonly Slider _r = Bar(), _g = Bar(), _b = Bar();
    private readonly TextBox _hex = new() { Width = 90, FontSize = 14, VerticalContentAlignment = VerticalAlignment.Center };
    private readonly Border _now = Swatch();

    private ColorPickDialog(string title, Color start)
    {
        _color = start;
        Title = title;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.WidthAndHeight;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        Background = GameUi.Back;

        var head = GameUi.TitleBar(title, Close);
        GameUi.EnableDrag(this, head);

        var before = Swatch();
        before.Background = new SolidColorBrush(start);
        var swatches = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
        swatches.Children.Add(Caption("처음"));
        swatches.Children.Add(before);
        swatches.Children.Add(Caption("  지금"));
        swatches.Children.Add(_now);

        var body = new StackPanel { Margin = new Thickness(14, 10, 14, 6) };
        body.Children.Add(swatches);
        body.Children.Add(Line("빨강", _r));
        body.Children.Add(Line("초록", _g));
        body.Children.Add(Line("파랑", _b));
        body.Children.Add(Line("RRGGBB", _hex));

        foreach (var slider in new[] { _r, _g, _b })
            slider.ValueChanged += (_, _) =>
            {
                if (_syncing) return;
                Show(Color.FromRgb((byte)_r.Value, (byte)_g.Value, (byte)_b.Value), fromHex: false);
            };
        _hex.TextChanged += (_, _) =>
        {
            if (!_syncing && Dh2Style.TryParse(_hex.Text, out var c)) Show(c, fromHex: true);
        };

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 10, 0, 8),
        };
        buttons.Children.Add(GameUi.PushButton("확인", () => { _ok = true; Close(); }, 90));
        var cancel = GameUi.PushButton("취소", Close, 90);
        cancel.Margin = new Thickness(10, 0, 0, 0);
        buttons.Children.Add(cancel);

        var stack = new StackPanel();
        stack.Children.Add(head);
        stack.Children.Add(body);
        stack.Children.Add(buttons);
        Content = new Border { BorderBrush = GameUi.Edge, BorderThickness = new Thickness(2), Margin = new Thickness(4), Child = stack };

        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) Close();
            else if (e.Key == Key.Enter) { _ok = true; Close(); }
        };
        Show(start, fromHex: false);
    }

    /// <summary>고르는 색을 바꾸고 막대 · 칸 · 견본을 맞춘다. 칸에 치는 중이면 칸 글은 안 건드린다.</summary>
    private void Show(Color c, bool fromHex)
    {
        _color = c;
        _syncing = true;
        _r.Value = c.R;
        _g.Value = c.G;
        _b.Value = c.B;
        if (!fromHex) _hex.Text = Dh2Style.Hex(c);
        _syncing = false;
        _now.Background = new SolidColorBrush(c);
    }

    private static Slider Bar() => new()
    {
        Minimum = 0, Maximum = 255, Width = 220, SmallChange = 1, LargeChange = 16,
        IsSnapToTickEnabled = true, TickFrequency = 1, VerticalAlignment = VerticalAlignment.Center,
    };

    private static Border Swatch() => new()
    {
        Width = 48, Height = 24, BorderBrush = Brushes.Black, BorderThickness = new Thickness(1),
        Margin = new Thickness(6, 0, 0, 0),
    };

    private static TextBlock Caption(string text) => new()
    {
        Text = text, Foreground = GameUi.Text, FontSize = 14, VerticalAlignment = VerticalAlignment.Center,
    };

    private static StackPanel Line(string label, FrameworkElement control)
    {
        var line = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 3, 0, 3) };
        var name = Caption(label);
        name.Width = 70;
        line.Children.Add(name);
        line.Children.Add(control);
        return line;
    }

    /// <summary>창을 띄운다. 「확인」으로 닫았으면 고른 색, 아니면 null.</summary>
    public static Color? Pick(Window owner, string title, Color start)
    {
        var dialog = new ColorPickDialog(title, start) { Owner = owner };
        dialog.ShowDialog();
        return dialog._ok ? dialog._color : null;
    }
}
