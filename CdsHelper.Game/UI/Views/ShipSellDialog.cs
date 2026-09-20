using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using CdsHelper.Game.Local.Helpers;
using CdsHelper.Support.Local.Models;

namespace CdsHelper.Game.UI.Views;

/// <summary>
/// 「매각선박의 선택」 — 함대의 배를 늘어놓고 <b>여럿을 골라</b> 한꺼번에 판다.
/// </summary>
/// <remarks>
/// 게임의 <c>0x00423750</c> 이다. 고른 것을 <b>비트마스크</b>로 돌려주므로 한 척씩이 아니라
/// 여러 척을 함께 판다 — 그래서 아래에 "견적합계" 가 붙는다.
///
/// 못 파는 배는 값이 0 으로 들어오고(<c>0x0044B889</c> 의 <c>[배+0x64] != 0</c> — 기함이다)
/// 줄이 흐리게 나온다. 눌러도 안 골라진다.
///
/// 줄은 네 칸이다 — 선명 · 선체타입 · 선수상 · 견적가격. 게임 글꼴이 칸을 고르게 먹으므로
/// 한 줄을 통째로 채워 세로줄을 맞춘다.
/// </remarks>
internal sealed class ShipSellDialog : GameWindow
{
    /// <summary>고른 줄의 바탕과 테. 힌트 일람과 같은 파랑이다.</summary>
    private static readonly Brush PickFill = Frozen(Color.FromRgb(0x4A, 0x64, 0x9E));

    private static Brush Frozen(Color c)
    {
        var b = new SolidColorBrush(c);
        b.Freeze();
        return b;
    }

    /// <summary>칸 너비(글자 칸). 한글 한 자가 두 칸이다.</summary>
    private const int NameCells = 16, HullCells = 12, CarvedCells = 8, PriceCells = 8;

    /// <summary>단추 하나의 폭과 둘 사이 틈.</summary>
    private const double ButtonWidth = 128, ButtonGap = 12;

    /// <summary>팔 배 한 줄.</summary>
    /// <param name="Index">함대에서 몇째인지.</param>
    /// <param name="Name">선명.</param>
    /// <param name="Hull">선체 이름.</param>
    /// <param name="Carved">선수상 이름. 안 달았으면 "---".</param>
    /// <param name="Price">견적가. 0 이면 못 파는 배다(기함).</param>
    internal readonly record struct Row(int Index, string Name, string Hull, string Carved, int Price);

    private readonly List<Row> _rows;
    private readonly List<Border> _lines = [];
    private readonly HashSet<int> _picked = [];
    private readonly GameUi.GameLabel _total;
    private readonly GameButton _decide;

    private ShipSellDialog(List<Row> rows)
    {
        _rows = rows;

        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.WidthAndHeight;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        Background = GameUi.Back;

        var list = new StackPanel();
        list.Children.Add(new Border
        {
            Padding = new Thickness(4, 0, 4, 0),
            Child = Ink(Head()),
        });

        for (int i = 0; i < _rows.Count; i++)
        {
            int at = i;
            var row = _rows[i];
            var line = new Border
            {
                Background = Brushes.Transparent,
                BorderBrush = Brushes.Transparent,
                BorderThickness = new Thickness(1),
                Padding = new Thickness(4, 0, 4, 0),
                Cursor = row.Price > 0 ? Cursors.Hand : Cursors.Arrow,
                // 못 파는 배는 흐리게 — 게임도 값이 0 인 줄을 그렇게 낸다.
                Opacity = row.Price > 0 ? 1.0 : 0.45,
                Child = Ink(Line(row)),
            };
            if (row.Price > 0)
                line.MouseLeftButtonUp += (_, e) => { e.Handled = true; Toggle(at); };
            _lines.Add(line);
            list.Children.Add(line);
        }

        _total = Ink("");
        list.Children.Add(new Border
        {
            Padding = new Thickness(4, 0, 4, 0),
            Child = _total,
        });

        _decide = new GameButton("결정", Decide, BandStyle.Button, ButtonWidth)
        {
            Height = UiSprites.BandHeight,
            Margin = new Thickness(0, 0, ButtonGap / 2, 0),
            On = false,
        };
        var stop = new GameButton("중단", Cancel, BandStyle.Button, ButtonWidth)
        {
            Height = UiSprites.BandHeight,
            Margin = new Thickness(ButtonGap / 2, 0, 0, 0),
        };
        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 6, 0, 4),
            Children = { _decide, stop },
        };

        var title = GameUi.TitleBar("매각선박의 선택", Cancel);
        GameUi.EnableDrag(this, title);

        var stack = new StackPanel();
        stack.Children.Add(title);
        stack.Children.Add(new Border
        {
            Background = GameUi.PageFill,
            BorderBrush = GameUi.ItemEdge,
            BorderThickness = new Thickness(2),
            Margin = new Thickness(3, 3, 3, 0),
            Padding = new Thickness(2, 2, 2, 2),
            Child = list,
        });
        stack.Children.Add(buttons);

        Content = GameUi.WindowFrame(stack);

        Paint();
        KeyDown += (_, e) => { if (e.Key is Key.Escape) Cancel(); };
        MouseRightButtonUp += (_, _) => Cancel();
    }

    /// <summary>머리 줄 — 칸 이름.</summary>
    private static string Head() =>
        GameUi.Pad("　선명", NameCells) + GameUi.Pad("선체타입", HullCells)
      + GameUi.Pad("선수상", CarvedCells) + "견적가격";

    /// <summary>줄 하나 — 값은 오른쪽에 붙인다.</summary>
    private static string Line(Row row) =>
        GameUi.Pad(row.Name, NameCells) + GameUi.Pad(row.Hull, HullCells)
      + GameUi.Pad(row.Carved, CarvedCells) + $"{row.Price,PriceCells}";

    /// <summary>종이 위의 글 한 줄 — 검은 벌이다.</summary>
    private static GameUi.GameLabel Ink(string text) =>
        new(GameFont.BlackColor, GameUi.ItemTextHeight)
        {
            Text = text,
            Bold = false,
            FallbackBrush = Brushes.Black,
            HorizontalAlignment = HorizontalAlignment.Left,
        };

    /// <summary>한 줄을 골랐다 놓는다. 여러 척을 함께 팔 수 있다.</summary>
    private void Toggle(int at)
    {
        if (!_picked.Remove(at)) _picked.Add(at);
        Paint();
    }

    private void Paint()
    {
        for (int i = 0; i < _lines.Count; i++)
        {
            bool on = _picked.Contains(i);
            _lines[i].Background = on ? PickFill : Brushes.Transparent;
            _lines[i].BorderBrush = on ? Brushes.Black : Brushes.Transparent;
        }
        _total.Text = $"견적합계 {Total()}닢";
        _decide.On = _picked.Count > 0;
    }

    /// <summary>고른 배 값의 합.</summary>
    private int Total()
    {
        int sum = 0;
        foreach (int at in _picked) sum += _rows[at].Price;
        return sum;
    }

    /// <summary>고른 배들(함대에서의 자리). 물렀으면 빈 목록.</summary>
    private List<int> _result = [];

    private void Decide()
    {
        if (_picked.Count == 0) return;
        _result = [.. _picked.Select(at => _rows[at].Index)];
        Close();
    }

    private void Cancel()
    {
        _result = [];
        Close();
    }

    /// <summary>
    /// 창을 띄우고 <b>고른 배들</b>을 낸다(함대에서의 자리). 물렀으면 빈 목록.
    /// </summary>
    public static List<int> Ask(Window owner, List<Row> rows)
    {
        if (rows.Count == 0) return [];

        var dialog = new ShipSellDialog(rows) { Owner = owner };
        dialog.ShowDialog();
        return dialog._result;
    }
}
