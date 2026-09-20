using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using CdsHelper.Game.Local.Helpers;

namespace CdsHelper.Game.UI.Views;

/// <summary>줄 속 칸이 어느 쪽에 붙는지.</summary>
internal enum GameListDock
{
    /// <summary>왼쪽부터 차곡차곡.</summary>
    Left,

    /// <summary>오른쪽부터 차곡차곡. <b>먼저 준 칸이 더 바깥</b>이다(DockPanel 규칙).</summary>
    Right,

    /// <summary>남은 자리를 다 먹는다. 한 줄에 하나만 둔다.</summary>
    Fill,
}

/// <summary>목록의 칸 하나 — 어디에 붙고 얼마나 벌리는지.</summary>
/// <param name="Dock">붙는 쪽.</param>
/// <param name="Margin">좌우 여백.</param>
/// <param name="Width">0 이 아니면 그 폭으로 고정한다(콜론을 세로로 세울 때 쓴다).</param>
/// <param name="Align">고정 폭 안에서의 정렬.</param>
internal sealed record GameListColumn(GameListDock Dock, Thickness Margin, double Width = 0,
                                      HorizontalAlignment Align = HorizontalAlignment.Left);

/// <summary>줄을 고르는 방식.</summary>
internal enum GameListPick
{
    /// <summary>한 줄만 고른다. 시장·소지품처럼 하나를 골라 결정하는 창.</summary>
    One,

    /// <summary>두 줄을 눌러 맞바꾼다. 부하편성처럼 자리를 옮기는 창.</summary>
    Swap,

    /// <summary>
    /// 여러 줄을 골라 둔다. 시장 구입·매각이 그렇다 — 게임도 한 번에 여럿을 사고판다.
    /// </summary>
    /// <remarks>
    /// 고른 줄은 남색으로 뒤집히고, 지금 <b>손이 가 있는 줄</b>은 검은 테로 따로 알린다.
    /// 둘은 다른 것이다 — 게임 갈무리에도 남색 줄 둘 가운데 하나에만 테가 둘려 있다.
    /// </remarks>
    Many,
}

/// <summary>
/// 게임 목록 창의 알맹이 — 양피지 판 위에 줄을 세우고, 고른 줄을 남색으로 뒤집는다.
/// </summary>
/// <remarks>
/// 시장 구입·매각, 소지품, 부하편성이 다 같은 모양이었다. 넷이 각자 남색
/// (<c>0x3A5A9A</c>)과 흰 글씨 뒤집기와 위아래 키 다루기를 베껴 두고 있었고, 주석이
/// 서로를 가리키고 있었다("시장 목록과 같은 색이다"). 그 공통분을 여기로 모았다.
///
/// <b>메뉴(<see cref="GameUi.CommandBox"/>)와는 다른 물건이다.</b> 메뉴는 게임 띠 그림으로
/// 짓고 누르면 곧바로 일이 일어나지만, 목록은 양피지에 글자만 얹고 <b>고르기만</b> 한다 —
/// 실제로 하는 일은 창 아래 "결정" 이 맡는다.
///
/// <code>
///   ┌ 양피지 ─────────────────┐
///   │ 물의 향수      (사치품)  1200 │  <- 칸 셋: 이름(Fill) · 갈래(Right) · 값(Right)
///   │ 향신료        (교역품)   340 │
///   └───────────────────────┘
/// </code>
///
/// 글씨는 게임 비트맵 글꼴로 찍는다. 고른 줄은 남색 위라 흰빛으로 뒤집는데, 색이 글자를
/// <b>찍을 때</b> 정해지므로 고를 때마다 그 줄을 새로 짓는다(한 번에 바뀌는 줄은 둘뿐이다).
///
/// <b><see cref="BelongingsDialog"/> 는 일부러 안 옮겼다.</b> 그쪽은 목록 <b>둘</b>이 나란히
/// 놓이고(소지품일람·발견물일람), 판이 그리드 칸을 꽉 채우느라 테두리도 여백도 없으며 줄
/// 안쪽 여백도 다르다. 그것들을 다 손잡이로 빼면 창 하나 때문에 이 부품이 부푼다 —
/// 옮기지 않는 편이 낫다고 보고 그대로 두었다.
/// </remarks>
internal sealed class GameList : Border
{
    /// <summary>고른 줄에 씌우는 남색. 게임 화면에서 뽑았다.</summary>
    private static readonly Brush Picked = Frozen(Color.FromRgb(0x5C, 0x6F, 0x93));

    /// <summary>손이 가 있는 줄을 두르는 검은 테. 골라 둔 것과는 따로 논다.</summary>
    private static readonly Brush Caret = Frozen(Color.FromRgb(0x0C, 0x0A, 0x08));

    /// <summary>줄 하나의 바닥 키 — 글자 키에 여백과 테를 더한 것이다.</summary>
    private const double RowHeight = GameUi.ItemTextHeight + 6;

    private static SolidColorBrush Frozen(Color c)
    {
        var b = new SolidColorBrush(c);
        b.Freeze();
        return b;
    }

    private readonly IReadOnlyList<GameListColumn> _columns;
    private readonly Func<int, IReadOnlyList<string>> _cells;
    private readonly List<Border> _rows = [];
    private readonly StackPanel _stack = new();
    private readonly string _emptyText;

    /// <summary>맞바꾸기에서 먼저 잡은 줄. 안 잡았으면 -1.</summary>
    private int _held = -1;

    /// <summary>여럿 고르기에서 골라 둔 줄들.</summary>
    private readonly HashSet<int> _chosen = [];

    /// <param name="columns">칸 배치. 모든 줄이 같이 쓴다.</param>
    /// <param name="cells">줄 번호를 주면 그 줄의 칸 글자들을 내는 이. 칸 수는 <paramref name="columns"/> 와 같아야 한다.</param>
    /// <param name="count">줄 수.</param>
    /// <param name="emptyText">줄이 하나도 없을 때 대신 낼 글. 비우면 아무것도 안 낸다.</param>
    /// <param name="maxHeight">
    /// 0 이 아니면 그 높이에서 자르고 두루마리를 단다 — 소지품처럼 줄이 늘 수 있는 목록에 쓴다.
    /// </param>
    public GameList(IReadOnlyList<GameListColumn> columns,
                    Func<int, IReadOnlyList<string>> cells,
                    int count, string emptyText = "", double maxHeight = 0)
    {
        _columns = columns;
        _cells = cells;
        _emptyText = emptyText;

        // 게임은 줄을 어두운 창 바탕이 아니라 밝은 칸 위에 얹는다.
        Background = GameUi.PageFill;
        BorderBrush = GameUi.ItemEdge;
        BorderThickness = new Thickness(1);
        Margin = new Thickness(6, 4, 6, 4);
        // 굴림대는 게임 것을 쓴다 — 윈도 굴림대는 모양이 게임과 너무 다르다.
        Child = maxHeight > 0 ? GameUi.Scroller(_stack, maxHeight) : _stack;

        Fill(count);
    }

    /// <summary>
    /// 줄 수가 바뀌었으면 통째로 다시 세운다(판 물건을 목록에서 걷을 때). 고른 줄은 풀린다.
    /// </summary>
    public void Rebuild(int count)
    {
        Selected = -1;
        _held = -1;
        _chosen.Clear();
        _rows.Clear();
        _stack.Children.Clear();
        Fill(count);
        SelectionChanged?.Invoke();
    }

    private void Fill(int count)
    {
        for (int i = 0; i < count; i++)
        {
            var row = NewRow(i);
            _rows.Add(row);
            _stack.Children.Add(row);
        }

        if (count == 0 && _emptyText.Length > 0)
            _stack.Children.Add(new TextBlock
            {
                Text = _emptyText,
                Foreground = Brushes.Black,
                FontWeight = FontWeights.Bold,
                FontSize = 15,
                Margin = new Thickness(8, 10, 8, 10),
                HorizontalAlignment = HorizontalAlignment.Center,
            });
    }

    /// <summary>줄 고르는 방식. 기본은 하나 고르기.</summary>
    public GameListPick Pick { get; init; } = GameListPick.One;

    /// <summary>
    /// 이 목록에 손이 가 있는지. 목록이 <b>둘 나란히</b> 놓이는 창(자택 교환)에서 쓴다 —
    /// 손이 간 쪽은 고른 줄을 남색으로 뒤집고, 안 간 쪽은 검은 테로만 알린다.
    /// 게임 갈무리가 그렇다.
    /// </summary>
    public bool Focused
    {
        get => _focused;
        set { if (_focused != value) { _focused = value; Paint(); } }
    }

    private bool _focused = true;

    /// <summary>줄 수.</summary>
    public int Count => _rows.Count;

    /// <summary>고른 줄. 아무것도 안 골랐으면 -1. 여럿 고르기에서는 손이 가 있는 줄이다.</summary>
    public int Selected { get; private set; } = -1;

    /// <summary>
    /// 골라 둔 줄들 — 줄 번호가 오름차순이다. 여럿 고르기가 아니면 <see cref="Selected"/>
    /// 하나뿐이다.
    /// </summary>
    public IReadOnlyList<int> Chosen =>
        Pick == GameListPick.Many
            ? [.. _chosen.Order()]
            : Selected >= 0 ? [Selected] : [];

    /// <summary>고른 줄이 바뀔 때. "결정" 단추를 살리고 죽이는 데 쓴다.</summary>
    public event Action? SelectionChanged;

    /// <summary>맞바꾸기에서 두 줄이 정해졌을 때. 자료를 바꾸는 것은 받는 쪽 몫이다.</summary>
    public event Action<int, int>? Swapped;

    /// <summary>그 줄을 고른다. 범위 밖이면 아무 일도 없다.</summary>
    public void Select(int index)
    {
        if (index < 0 || index >= _rows.Count) return;
        Selected = index;
        Paint();
        SelectionChanged?.Invoke();
    }

    /// <summary>
    /// 위아래로 옮긴다. 끝에 닿으면 반대쪽으로 돈다 — 게임 목록도 그렇게 돈다.
    /// </summary>
    public void Move(int delta)
    {
        if (_rows.Count == 0) return;
        int next = Selected < 0
            ? (delta > 0 ? 0 : _rows.Count - 1)
            : (Selected + delta + _rows.Count) % _rows.Count;
        Select(next);
    }

    /// <summary>
    /// 위아래 키만 받아 쓴다. 먹었으면 true — 나머지(결정·중단)는 창이 맡는다.
    /// </summary>
    public bool HandleKey(Key key)
    {
        switch (key)
        {
            case Key.Up: Move(-1); return true;
            case Key.Down: Move(+1); return true;
            // 여럿 고르기는 스페이스로 켜고 끈다 — 엔터는 창의 "결정" 몫이라 건드리지 않는다.
            case Key.Space when Pick == GameListPick.Many && Selected >= 0:
                Toggle(Selected);
                return true;
            default: return false;
        }
    }

    /// <summary>줄 속을 다시 읽어 그린다. 자료가 바뀐 뒤에 부른다(맞바꾸기 따위).</summary>
    public void Refresh() => Paint();

    private Border NewRow(int index)
    {
        var row = new Border
        {
            Background = Brushes.Transparent,
            // 테는 늘 한 점 둔다(빛깔만 바뀐다) — 고를 때마다 두께가 바뀌면 줄이 들썩인다.
            BorderBrush = Brushes.Transparent,
            BorderThickness = new Thickness(1),
            Padding = new Thickness(1, 2, 1, 2),
            // 빈 줄도 키는 그대로다 — 자택 교환 창의 빈 칸이 납작해지면 안 된다.
            MinHeight = RowHeight,
            Cursor = Cursors.Hand,
            Child = Line(index, on: false),
        };
        // 누름도 여기서 삼킨다 — 창 끌기가 먼저 걸리면 마우스를 잡아 버려 뗌이 안 온다.
        row.MouseLeftButtonDown += (_, e) => e.Handled = true;
        row.MouseLeftButtonUp += (_, e) => { e.Handled = true; Touch(index); };
        return row;
    }

    /// <summary>
    /// 줄을 누른다. 하나 고르기면 그냥 고르고, 맞바꾸기면 먼저 잡았다 다음 줄과 바꾼다.
    /// 같은 줄을 다시 누르면 그냥 놓는다.
    /// </summary>
    private void Touch(int index)
    {
        if (Pick == GameListPick.One) { Select(index); return; }
        if (Pick == GameListPick.Many) { Toggle(index); return; }

        if (_held < 0)
        {
            _held = index;
            Selected = index;
            Paint();
            SelectionChanged?.Invoke();
            return;
        }

        int from = _held;
        _held = -1;
        Selected = -1;
        if (from != index) Swapped?.Invoke(from, index);
        Paint();
        SelectionChanged?.Invoke();
    }

    /// <summary>그 줄을 골라 두거나 놓는다. 손도 그 줄로 옮긴다.</summary>
    private void Toggle(int index)
    {
        if (index < 0 || index >= _rows.Count) return;
        if (!_chosen.Remove(index)) _chosen.Add(index);
        Selected = index;
        Paint();
        SelectionChanged?.Invoke();
    }

    private void Paint()
    {
        bool many = Pick == GameListPick.Many;
        for (int i = 0; i < _rows.Count; i++)
        {
            bool here = i == Selected;
            bool on = many ? _chosen.Contains(i) : here && _focused;
            _rows[i].Background = on ? Picked : Brushes.Transparent;
            // 손이 가 있는 줄은 검은 테로 따로 알린다.
            _rows[i].BorderBrush = here && (many || !_focused) ? Caret : Brushes.Transparent;
            _rows[i].Child = Line(i, on);
        }
    }

    /// <summary>줄 속 한 벌. 칸을 준 차례대로 붙인다.</summary>
    private FrameworkElement Line(int index, bool on)
    {
        var line = new DockPanel { LastChildFill = true };
        var cells = _cells(index);

        for (int c = 0; c < _columns.Count && c < cells.Count; c++)
        {
            var column = _columns[c];
            var label = Label(cells[c], column.Margin, on);
            label.HorizontalAlignment = column.Align;

            // 폭을 못 박은 칸은 <b>겉칸에 싸서</b> 그 안에서 글자를 민다. 글자 칸 자체에
            // 폭을 주면 그림이 칸을 꽉 채우는 셈이라 정렬이 먹지 않는다.
            FrameworkElement cell = column.Width > 0
                ? new Border { Width = column.Width, Child = label }
                : label;

            if (column.Dock != GameListDock.Fill)
                DockPanel.SetDock(cell,
                    column.Dock == GameListDock.Right ? Dock.Right : Dock.Left);
            line.Children.Add(cell);
        }
        return line;
    }

    /// <summary>
    /// 줄에 얹는 글씨. 줄 칸이 양피지라 글씨가 검고, 고른 줄만 남색 위라 흰빛으로 뒤집는다.
    /// </summary>
    private static FrameworkElement Label(string text, Thickness margin, bool on) =>
        new GameUi.GameLabel(on ? GameFont.WhiteColor : GameFont.ButtonColor)
        {
            Margin = margin,
            Text = text,
            // 글꼴을 못 읽으면 GameLabel 이 윈도 글꼴로 물러선다. 그때 색을 맞춰 준다.
            FallbackBrush = on ? Brushes.White : Brushes.Black,
        };
}
