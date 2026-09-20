using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using CdsHelper.Support.Local.Models;

using CdsHelper.Game.Local.Helpers;

namespace CdsHelper.Game.UI.Views;

/// <summary>
/// 「선명입력」 — 배 이름을 정하는 창.
/// </summary>
/// <remarks>
/// 게임 화면 그대로다. 맨 위에 지금 이름이 적힌 줄이 있고 그 오른쪽 끝에 작은 단추가 하나
/// 있다(계산기처럼 생겼다). 그 단추를 누르면 <see cref="TextInputDialog"/> 가 떠서 글자를
/// 하나씩 찍어 지을 수 있고, 밑의 목록에서 <b>미리 갖춰 둔 이름</b>(<see cref="ShipNames.All"/>)
/// 을 골라도 된다.
///
/// 게임은 이 창을 <c>0x00454D30(창, 버퍼, 0x24, 목록수, 목록, "선명입력")</c> 하나로 내고
/// 배를 살 때도 같은 창을 쓴다. 우리도 그렇다 — 개조의 "선명변경" 과 조선소의 "구입" 둘 다
/// 이 창을 낸다(<see cref="HullSelectDialog"/>).
/// <code>
///   0x0053C178  이름 포인터 표 스물하나 (문자열은 0x00531350 부터)
///   0x00531468  "선명입력"
///   0x00531478  "배의 이름을 정해 주십시오"
/// </code>
/// </remarks>
public sealed class ShipNameDialog : GameWindow
{
    private readonly GameUi.GameLabel _name;
    private readonly List<Border> _rows = [];
    private string? _result;

    /// <summary>물러날 길이 없는 창인지 — 배를 살 때가 그렇다.</summary>
    private readonly bool _mustName;

    private ShipNameDialog(string current, bool mustName)
    {
        _mustName = mustName;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.WidthAndHeight;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        Background = GameUi.Back;

        _name = Ink(current);
        _name.VerticalAlignment = VerticalAlignment.Center;
        _name.Margin = new Thickness(8, 3, 8, 3);

        // 오른쪽 끝의 작은 단추 — 원본 계산기 아이콘이다.
        var pad = GameUi.CalcButton(TypeIt, 18);
        pad.Margin = new Thickness(4, 0, 4, 0);

        var top = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(pad, Dock.Right);
        top.Children.Add(pad);
        top.Children.Add(_name);

        var list = new StackPanel();
        foreach (string name in ShipNames.All)
        {
            string pick = name;
            var row = new Border
            {
                Background = Brushes.Transparent,
                Padding = new Thickness(10, 1, 6, 1),
                Cursor = Cursors.Hand,
                Child = Ink(name),
            };
            row.MouseLeftButtonUp += (_, e) => { e.Handled = true; Choose(pick); };
            _rows.Add(row);
            list.Children.Add(row);
        }

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 8, 0, 8),
        };
        buttons.Children.Add(GameUi.PushButton("결정", Decide));
        if (!_mustName) buttons.Children.Add(GameUi.PushButton("중단", Cancel));

        // 이름을 꼭 지어야 하는 창은 제목 줄의 닫기도 안 단다.
        var title = _mustName ? GameUi.TitleBar("선명입력", null) : GameUi.TitleBar("선명입력", Cancel);
        GameUi.EnableDrag(this, title);

        var stack = new StackPanel();
        stack.Children.Add(title);
        stack.Children.Add(Framed(top, new Thickness(4, 4, 4, 0)));
        // 윈도 굴림대 대신 게임 굴림대(화살표 조각 + 도드라진 손잡이)를 단다.
        stack.Children.Add(Framed(new Border
        {
            Height = 300,
            Width = 300,
            Child = GameUi.Scroller(list, 300),
        }, new Thickness(4, 4, 4, 0)));
        stack.Children.Add(buttons);

        Content = new Border
        {
            Background = GameUi.Back,
            BorderBrush = GameUi.Edge,
            BorderThickness = new Thickness(2),
            Margin = new Thickness(4),
            Child = stack,
        };

        Mark(current);
        KeyDown += (_, e) => { if (e.Key is Key.Escape) Cancel(); };
        MouseRightButtonUp += (_, _) => Cancel();

        // 창을 닫는 다른 길(Alt+F4 따위)로 빠져나가도 이름은 남아야 한다.
        Closing += (_, _) => { if (_mustName) _result ??= _name.Text.Trim(); };
    }

    private static Border Framed(UIElement child, Thickness margin) => new()
    {
        Background = GameUi.PageFill,
        BorderBrush = GameUi.ItemEdge,
        BorderThickness = new Thickness(2),
        Margin = margin,
        Padding = new Thickness(2),
        Child = child,
    };

    /// <summary>목록에서 하나를 골랐다 — 위 줄에 올려만 놓고 결정은 따로 받는다.</summary>
    private void Choose(string name)
    {
        _name.Text = name;
        Mark(name);
    }

    /// <summary>위 줄과 같은 이름의 줄을 도드라지게 칠한다.</summary>
    private void Mark(string name)
    {
        for (int i = 0; i < _rows.Count; i++)
            _rows[i].Background = ShipNames.All[i] == name ? GameUi.ItemFill : Brushes.Transparent;
    }

    /// <summary>글자판을 열어 손으로 짓는다.</summary>
    private void TypeIt()
    {
        if (TextInputDialog.Ask(this, _name.Text, ShipNames.MaxLength) is { } typed)
        {
            _name.Text = typed;
            Mark(typed);
        }
    }

    /// <summary>결정 — 이름이 비었으면 안 닫는다. 배 이름이 빈 채로 넘어갈 수는 없다.</summary>
    private void Decide()
    {
        string name = _name.Text.Trim();
        if (_mustName && name.Length == 0) return;

        _result = name;
        Close();
    }

    private void Cancel()
    {
        if (_mustName) return;   // 물러날 길이 없는 창이다

        _result = null;
        Close();
    }

    /// <summary>
    /// 입력 칸과 자판 글자. <b>게임 글꼴</b>로 찍는다 — 바탕이 밝아 검은 글씨다.
    /// </summary>
    /// <remarks>
    /// 게임 글꼴을 못 읽었을 때만 윈도 글꼴로 물러선다(<see cref="GameUi.GameLabel"/>).
    /// </remarks>
    private static GameUi.GameLabel Ink(string text) => new(GameFont.BlackColor)
    {
        Text = text,
        Bold = false,
        FallbackBrush = System.Windows.Media.Brushes.Black,
        HorizontalAlignment = HorizontalAlignment.Left,
        VerticalAlignment = VerticalAlignment.Center,
    };

    /// <summary>
    /// 창을 띄우고 정한 이름을 낸다. 중단했거나 이름이 비었으면 null.
    /// </summary>
    /// <remarks>
    /// <b>그대로 결정한 것도 답이다.</b> 예전에는 <paramref name="current"/> 와 같으면 null 을
    /// 냈는데, 그러면 배를 살 때 골라 준 이름을 그대로 받아들인 것과 중단한 것을 가릴 수 없다.
    /// "안 바뀌었으니 할 일 없다" 는 판단은 이름을 고치는 쪽(선명변경)이 하면 된다.
    /// </remarks>
    /// <param name="owner">주인 창.</param>
    /// <param name="current">지금 이름. 창을 열 때 위 줄에 올려 둔다.</param>
    /// <param name="mustName">
    /// 참이면 중단이 없다 — 결정 말고는 나갈 길이 없고, 늘 이름을 낸다.
    /// 배를 살 때가 그렇다(<see cref="HullSelectDialog"/>). 살지 말지는 그 앞에서 이미 물었다.
    /// </param>
    public static string? Ask(Window owner, string current, bool mustName = false)
    {
        var dialog = new ShipNameDialog(current, mustName) { Owner = owner };
        dialog.ShowDialog();

        string? name = dialog._result;
        return string.IsNullOrWhiteSpace(name) ? (mustName ? current : null) : name;
    }
}
