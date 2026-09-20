using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using CdsHelper.Game.Local.Helpers;

namespace CdsHelper.Game.UI.Views;

/// <summary>
/// 「수리선박 선택」 — 고칠 배를 늘어놓고 <b>여럿을 골라</b> 한꺼번에 고친다.
/// </summary>
/// <remarks>
/// 게임의 <c>0x0044BA62</c> 가 여는 목록이다(<c>0x0046C3E0</c>, 제목 <c>0x005311E0</c>).
/// 고른 것을 <b>고름표</b>로 돌려주고, 값은 고른 배의 <b>손상을 다 더해</b> 한 번 굴린다.
/// <code>
///   0044BA83  고른 줄마다 0x0044BBF0(배) 를 더한다      ; 손상 = (최대내구-내구)+(최대돛-돛)
///   0044BAA1  값 = (rand(4) + 26) * 손상합
///   0044BABD  값 = 값 x 도시 시세 / 100                 ; 적어도 1
///   0044BAD7  "수리하는데 금화 %ld닢 필요하네. 좋나?"
/// </code>
/// 곧 <b>굴림은 한 번</b>이라, 한 척씩 고칠 때보다 값이 고르게 나온다.
/// </remarks>
internal sealed class ShipRepairDialog : GameWindow
{
    /// <summary>고른 줄의 바탕과 테. 매각 창과 같은 파랑이다.</summary>
    private static readonly Brush PickFill = Frozen(Color.FromRgb(0x4A, 0x64, 0x9E));

    private static Brush Frozen(Color c)
    {
        var b = new SolidColorBrush(c);
        b.Freeze();
        return b;
    }

    /// <summary>칸 너비(글자 칸). 한글 한 자가 두 칸이다.</summary>
    private const int MarkCells = 6, NameCells = 16, HullCells = 12;

    /// <summary>단추 하나의 폭과 둘 사이 틈.</summary>
    private const double ButtonWidth = 128, ButtonGap = 12;

    /// <summary>고칠 배 한 줄.</summary>
    /// <param name="Index">부르는 쪽 목록에서 몇째인지.</param>
    /// <param name="Docked">이 마을에 맡겨 둔 배인지.</param>
    /// <param name="Name">선명.</param>
    /// <param name="Hull">선체 이름.</param>
    /// <param name="Hp">지금 내구와 최대 내구.</param>
    /// <param name="MaxHp">최대 내구.</param>
    /// <param name="Need">손상 — 값을 셈하는 밑이다.</param>
    internal readonly record struct Row(int Index, bool Docked, string Name, string Hull,
                                        int Hp, int MaxHp, int Need);

    private readonly List<Row> _rows;
    private readonly List<Border> _lines = [];
    private readonly HashSet<int> _picked = [];
    private readonly GameUi.GameLabel _total;
    private readonly GameButton _decide;

    private ShipRepairDialog(List<Row> rows)
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
            var line = new Border
            {
                Background = Brushes.Transparent,
                BorderBrush = Brushes.Transparent,
                BorderThickness = new Thickness(1),
                Padding = new Thickness(4, 0, 4, 0),
                Cursor = Cursors.Hand,
                Child = Ink(Line(_rows[i])),
            };
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

        var title = GameUi.TitleBar("수리선박 선택", Cancel);
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
        GameUi.Pad("　", MarkCells) + GameUi.Pad("선명", NameCells)
      + GameUi.Pad("선체타입", HullCells) + "내구";

    /// <summary>줄 하나.</summary>
    private static string Line(Row row) =>
        GameUi.Pad(row.Docked ? "맡김" : "", MarkCells) + GameUi.Pad(row.Name, NameCells)
      + GameUi.Pad(row.Hull, HullCells) + $"{row.Hp,3}/{row.MaxHp,-3}";

    /// <summary>종이 위의 글 한 줄 — 검은 벌이다.</summary>
    private static GameUi.GameLabel Ink(string text) =>
        new(GameFont.BlackColor, GameUi.ItemTextHeight)
        {
            Text = text,
            Bold = false,
            FallbackBrush = Brushes.Black,
            HorizontalAlignment = HorizontalAlignment.Left,
        };

    /// <summary>한 줄을 골랐다 놓는다.</summary>
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

        // 값은 굴림이 끼어 여기서 못 내놓는다 — 고른 손상 합만 보인다.
        int need = 0;
        foreach (int at in _picked) need += _rows[at].Need;
        _total.Text = $"손상합계 {need}";
        _decide.On = _picked.Count > 0;
    }

    /// <summary>고른 배들(부르는 쪽 자리). 물렀으면 빈 목록.</summary>
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

    /// <summary>창을 띄우고 <b>고른 배들</b>을 낸다. 물렀으면 빈 목록.</summary>
    public static List<int> Ask(Window owner, List<Row> rows)
    {
        if (rows.Count == 0) return [];

        var dialog = new ShipRepairDialog(rows) { Owner = owner };
        dialog.ShowDialog();
        return dialog._result;
    }
}
