using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using CdsHelper.Game.Engine.Sea;
using CdsHelper.Game.Local.Helpers;
using CdsHelper.Support.Local.Models;

namespace CdsHelper.Game.UI.Views;

/// <summary>
/// 배 한 척을 고르는 표 — 「개조선박의 선택」 같은 창. <b>머리글을 누르면 칸 묶음이 바뀐다.</b>
/// </summary>
/// <remarks>
/// 원본 배 목록 창(<c>0x0046C3E0</c> → <c>0x0046BBE0</c>)은 칸 표 <c>0x00560D68</c>(칸마다 머리글 · 서식 · 폭)와
/// 묶음 표 <c>0x00560E10</c>(묶음마다 칸 번호 열넷, −1 로 끝)를 쓴다.
/// <code>
///   칸   0 선명 %99s · 1 승원수 %3d/%3d · 2 내구력 %3d/%3d · 3 중량 %5d · 4 용적 %5d · 5 선체타입 %12s
///        6 선두상 %6s · 7 추진력 %3d/%3d · 8 대포명  포문수 %12s %2d/%2d문 · 9 대포수 %6d · 10 포탑수 %6d
///        11 소유 %4s · 12 돛종류 %6s · 13 함대 %4s
///   묶음 0 선명 승원수 내구력 중량 용적 함대
///        1 선명 추진력 대포명·포문수 돛종류
///        2 선명 선체타입 추진력 내구력 선두상
///        3 선명 선체타입 내구력 중량 용적 소유
///        4 선명 대포명·포문수 선두상 소유 함대
/// </code>
/// 머리글을 누르면 <c>0x0046C300</c> 이 묶음을 하나 넘긴다 — 인자가 0 이면 +1, 아니면 −1, 0~4 에서 돈다.
/// 지금 묶음은 <b>부른 쪽이 넘긴 칸</b>에 적혀 창을 닫아도 남는다 — 개조는 <c>0x0056E290</c>(처음 1),
/// 수리 <c>0x00549D60</c>(2), 기함 변경 <c>0x005602C8</c>(3), 선박삭제·파기 4 따위다.
/// 우리는 좌클릭을 +1, 우클릭을 −1 로 둔다(어느 단추가 어느 쪽인지는 원본 인자를 끝까지 못 따랐다).
///
/// 선수상 칸 머리글은 EXE 글이 「선두상」이지만 화면에 「선수상」으로 보여 그렇게 적는다
/// (<see cref="ShipyardMenu"/> 의 선수상 목록과 같은 까닭).
/// </remarks>
public sealed class ShipPickDialog : GameWindow
{
    /// <summary>칸 — 머리글과 폭(점), 숫자인지.</summary>
    private readonly record struct Column(string Head, double Width, bool Number);

    private static readonly Column[] Columns =
    [
        new("선명", 104, false), new("승원수", 72, true), new("내구력", 72, true), new("중량", 56, true),
        new("용적", 56, true), new("선체타입", 100, true), new("선수상", 88, false), new("추진력", 72, true),
        new("대포명  포문수", 176, false), new("대포수", 60, true), new("포탑수", 60, true), new("소유", 48, false),
        new("돛종류", 64, false), new("함대", 48, false),
        // 14 — 매각 견적. 원본 배 목록 칸이 아니다(매각 창이 이 표를 쓰면서 붙인 칸).
        new("견적가격", 80, true),
    ];

    /// <summary>매각 견적 칸 번호.</summary>
    private const int PriceColumn = 14;

    /// <summary>
    /// 매각 창의 첫 묶음 — 선명 · 선체타입 · 대포명/포문수 · 포탑수 · 돛종류 · 선수상. 팔 배를 가를 때 보는 것을 한눈에 둔다.
    /// 그 뒤로는 여느 묶음 다섯을 돈다. 견적가격 칸은 어느 묶음에나 끝에 붙는다.
    /// </summary>
    private static readonly int[] SellLead = [0, 5, 8, 10, 12, 6];

    /// <summary>묶음 표(<c>0x00560E10</c>).</summary>
    private static readonly int[][] Sets =
    [
        [0, 1, 2, 3, 4, 13],
        [0, 7, 8, 12],
        [0, 5, 7, 2, 6],
        [0, 5, 2, 3, 4, 11],
        [0, 8, 6, 11, 13],
    ];

    /// <summary>창마다 지금 묶음 — 게임처럼 닫아도 남는다. 열쇠는 창 제목이다.</summary>
    private static readonly Dictionary<string, int> Remembered = [];

    private static readonly Brush HeadFill = Frozen(Color.FromRgb(0xDE, 0xC6, 0xAD));
    private static readonly Brush RowFill = Frozen(Color.FromRgb(0xFF, 0xEF, 0xD6));
    private static readonly Brush PickFill = Frozen(Color.FromRgb(0xC4, 0xA8, 0x8C));

    private static Brush Frozen(Color c)
    {
        var b = new SolidColorBrush(c);
        b.Freeze();
        return b;
    }

    /// <summary>
    /// 표의 한 줄 — 배와 그 배의 승원, 기함인지, 고를 수 있는지다.
    /// </summary>
    /// <param name="Ship">배.</param>
    /// <param name="Crew">태운 선원(승원수 칸). 맡겨 둔 배는 0 이다.</param>
    /// <param name="Flagship">기함인지(함대 칸).</param>
    /// <param name="On">고를 수 있는지. 거짓이면 흐리고 안 눌린다.</param>
    public readonly record struct Entry(Ship Ship, int Crew, bool Flagship, bool On = true);

    /// <summary>
    /// 부른 쪽이 넘기는 방식 비트(<c>0x0046BBE0</c> 의 <c>[ebp+0x14]</c>).
    /// </summary>
    /// <remarks>
    /// <code>
    ///   0046BCA6  비트 1 — 제독 것이 아닌 배(0x0044C730 ≠ 0x004783C0)를 흐린다   ; 빌린 배
    ///   0046BCBF  비트 2 — 기함(0x0044C6B0 = 0x00473CD0)을 흐린다
    /// </code>
    /// 기함 변경·선박 삭제는 2, 선박 파기는 3, 편입·수리·개조는 0 이다.
    /// </remarks>
    public const int NoLent = 1, NoFlagship = 2;

    private readonly IReadOnlyList<Entry> _entries;
    private readonly ItemTable? _items;
    private readonly string _title;

    /// <summary>이 창이 도는 묶음들 — 여느 다섯, 매각이면 앞에 <see cref="SellLead"/> 가 붙는다.</summary>
    private readonly int[][] _sets;

    /// <summary>줄마다 견적가(매각 창). 없으면 null — 견적 칸과 합계 줄이 없다.</summary>
    private readonly IReadOnlyList<int>? _prices;

    /// <summary>「견적합계 N닢」 줄. 매각 창에서만.</summary>
    private readonly GameUi.GameLabel? _total;
    private readonly bool _many;
    /// <summary>
    /// 표 — 바탕을 줄 색으로 깔고 줄 경계를 화소에 맞춘다. 창이 배율로 늘면 줄 경계가 반 화소에 걸려
    /// 그 틈으로 어두운 창 바탕이 비쳐 줄 사이에 가는 선이 섰다.
    /// </summary>
    private readonly StackPanel _table = new() { Background = RowFill, UseLayoutRounding = true };
    private readonly GameButton _decide;
    private readonly List<Border> _rows = [];
    private readonly HashSet<int> _picked = [];

    /// <summary>고른 줄들(넘겨받은 목록의 자리). 안 골랐으면 비어 있다.</summary>
    private List<int> _chosen = [];

    /// <summary>여럿 고르기면 끌어서 한꺼번에 켜고 끈다(선박 편입 · 수리 따위). 한 척 고르기면 null.</summary>
    private readonly DragPick? _drag;

    private ShipPickDialog(IReadOnlyList<Entry> entries, ItemTable? items, string title, bool many,
                           IReadOnlyList<int>? prices = null)
    {
        _entries = entries;
        _items = items;
        _title = title;
        _many = many;
        _prices = prices;
        _sets = prices != null ? [SellLead, .. Sets] : Sets;

        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.WidthAndHeight;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        Background = GameUi.Back;

        _decide = new GameButton("결정", Decide) { On = false };
        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 8, 0, 8),
        };
        buttons.Children.Add(_decide);
        buttons.Children.Add(new GameButton("중단", Close));

        var head = GameUi.TitleBar(title, Close);
        GameUi.EnableDrag(this, head);

        var stack = new StackPanel();
        stack.Children.Add(head);
        stack.Children.Add(new Border { Margin = new Thickness(8, 4, 8, 0), Child = _table });
        if (prices != null)
        {
            _total = new GameUi.GameLabel(GameFont.WhiteColor)
            {
                Bold = false,
                FallbackBrush = GameUi.Text,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(8, 4, 12, 0),
            };
            stack.Children.Add(_total);
        }
        stack.Children.Add(buttons);
        Content = GameUi.DialogEdge(stack);

        // 여럿 고르기는 시장 · 발표 목록처럼 끌어서 고른다 — 한 척씩 누르기가 번거로웠다.
        if (many)
            _drag = new DragPick(_table, _rows,
                                 i => _picked.Contains(i),
                                 (i, on) => { if (on) _picked.Add(i); else _picked.Remove(i); },
                                 _ => Repaint(),
                                 i => i >= 0 && i < _entries.Count && _entries[i].On);

        Paint();
        KeyDown += (_, e) => { if (e.Key is Key.Escape) Close(); };
    }

    /// <summary>고른 줄 바탕과 결정 단추만 다시 칠한다.</summary>
    private void Repaint()
    {
        for (int i = 0; i < _rows.Count; i++) _rows[i].Background = _picked.Contains(i) ? PickFill : RowFill;
        _decide.On = _picked.Count > 0;
        if (_total != null && _prices != null)
            _total.Text = $"견적합계 {_picked.Sum(i => i < _prices.Count ? _prices[i] : 0)}닢";
    }

    private int Set => Remembered.TryGetValue(_title, out int set) ? Math.Clamp(set, 0, _sets.Length - 1) : 0;

    /// <summary>묶음을 하나 넘긴다(<c>0x0046C300</c>) — 0~4 에서 돈다(매각 창은 앞 묶음까지 여섯).</summary>
    private void Turn(int by)
    {
        Remembered[_title] = ((Set + by) % _sets.Length + _sets.Length) % _sets.Length;
        Paint();
    }

    private void Paint()
    {
        _table.Children.Clear();
        _rows.Clear();
        int[] set = _prices != null ? [.. _sets[Set], PriceColumn] : _sets[Set];

        var header = Row(set, set.Select(c => Columns[c].Head).ToArray(), header: true);
        header.Cursor = Cursors.Hand;
        header.MouseLeftButtonUp += (_, e) => { e.Handled = true; Turn(+1); };
        header.MouseRightButtonUp += (_, e) => { e.Handled = true; Turn(-1); };
        _table.Children.Add(header);

        for (int i = 0; i < _entries.Count; i++)
        {
            int at = i;
            var row = Row(set, set.Select(c => CellOf(c, at)).ToArray(), header: false, dim: !_entries[at].On);
            if (_entries[at].On)
            {
                row.Cursor = Cursors.Hand;
                if (_drag != null) _drag.Attach(row, at);
                else row.MouseLeftButtonUp += (_, e) => { e.Handled = true; Pick(at); };
            }
            // 못 고르는 줄은 <b>글씨만 회색</b>이다 — 예전에는 줄째 흐려 바탕까지 칠한 듯 보였다.
            if (_picked.Contains(at)) row.Background = PickFill;
            _rows.Add(row);
            _table.Children.Add(row);
        }
        Repaint();
    }

    /// <summary>칸 하나의 글 — 서식은 칸 표(<c>0x00560D68</c>) 그대로다.</summary>
    private string CellOf(int column, int at)
    {
        var entry = _entries[at];
        var ship = entry.Ship;
        return column switch
        {
            0 => ship.Name,
            1 => $"{entry.Crew,3}/{ship.Crew,3}",
            2 => $"{ship.Hp,3}/{ship.MaxHp,3}",
            3 => $"{ship.Tonnage,5}",
            4 => $"{ship.UsableCapacity,5}",
            5 => ship.Hull.Name,
            6 => ship.Figurehead >= 0
                ? _items?.Find(Figureheads.ToItem(ship.Figurehead))?.Name ?? $"선수상 {ship.Figurehead}"
                : "---",
            7 => $"{ship.MaxSpeed,3}/{ship.Hull.SpeedCeiling,3}",
            8 => ship.Gun >= 0 && ship.Gun < Cannon.Count
                ? $"{Cannon.All[ship.Gun].Name} {ship.Guns,2}/{ship.Turrets,2}문"
                : "",
            9 => $"{ship.Guns,6}",
            10 => $"{ship.Turrets,6}",
            11 => ship.Lent ? "대출" : "소유",
            12 => string.Concat(ship.Sails.Select(ShipyardMenu.SailMark)),
            13 => entry.Flagship ? "기함" : "",
            PriceColumn => _prices is { } prices && at < prices.Count ? $"{prices[at],7}" : "",
            _ => "",
        };
    }

    /// <summary>표 한 줄 — 이름은 가운데, 숫자는 오른쪽이다(구입 표와 같은 모양).</summary>
    /// <summary>못 고르는 줄의 글씨색 — 공용 색표의 회색(144,140,140).</summary>
    private const byte DimColor = 11;

    private static Border Row(int[] set, string[] cells, bool header, bool dim = false)
    {
        var grid = new Grid();
        for (int c = 0; c < set.Length; c++)
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Columns[set[c]].Width) });

        for (int c = 0; c < cells.Length; c++)
        {
            var label = new GameUi.GameLabel(dim ? DimColor : GameFont.BlackColor)
            {
                Text = cells[c],
                Bold = false,
                FallbackBrush = dim ? Brushes.Gray : Brushes.Black,
                Margin = new Thickness(3, 1, 3, 1),
                HorizontalAlignment = header || !Columns[set[c]].Number
                    ? HorizontalAlignment.Center
                    : HorizontalAlignment.Right,
            };
            Grid.SetColumn(label, c);
            grid.Children.Add(label);
        }
        return new Border { Background = header ? HeadFill : RowFill, Child = grid };
    }

    /// <summary>
    /// 줄을 고른다. 여럿 고르기(<c>0x0046C3E0</c> 의 여섯째 인자가 있을 때 — 수리)면 눌러 켰다 끈다.
    /// </summary>
    private void Pick(int at)
    {
        if (_many) { if (!_picked.Remove(at)) _picked.Add(at); }
        else { _picked.Clear(); _picked.Add(at); }
        Repaint();
    }

    private void Decide()
    {
        if (_picked.Count == 0) return;
        _chosen = [.. _picked.Order()];
        Close();
    }

    /// <summary>
    /// 함대의 배를 원본 차례로 늘어놓는다 — <b>기함이 맨 앞</b>, 나머지는 칸 차례다(<c>0x0049D360</c>).
    /// </summary>
    /// <param name="mode"><see cref="NoLent"/> · <see cref="NoFlagship"/> 를 겹친 방식 비트.</param>
    /// <returns>줄과 그 줄의 함대 자리.</returns>
    public static (List<Entry> Rows, List<int> Slots) Fleet(Player player, int mode)
    {
        var slots = new List<int>();
        int flag = player.Flagship;
        if (flag >= 0 && flag < player.Ships.Count) slots.Add(flag);
        for (int i = 0; i < player.Ships.Count; i++) if (i != flag) slots.Add(i);

        var shares = player.CrewShares;
        var rows = slots.Select(i =>
        {
            var ship = player.Ships[i];
            bool on = !((mode & NoLent) != 0 && ship.Lent) && !((mode & NoFlagship) != 0 && i == flag);
            return new Entry(ship, shares.ElementAtOrDefault(i), i == flag, on);
        }).ToList();
        return (rows, slots);
    }

    /// <summary>
    /// 함대의 배 한 척을 고르게 한다(<c>0x0049D3F0</c>). 고른 배의 <b>함대 자리</b>를 낸다(중단이면 −1).
    /// </summary>
    /// <param name="startSet">이 창을 처음 열 때의 묶음 — 원본이 부른 쪽마다 들고 있는 처음 값이다.</param>
    /// <param name="mode">흐릴 줄(<see cref="NoLent"/> · <see cref="NoFlagship"/>).</param>
    public static int Pick(Window owner, Player player, ItemTable? items, string title, int startSet, int mode = 0)
    {
        var (rows, slots) = Fleet(player, mode);
        int at = Pick(owner, rows, items, title, startSet);
        return at < 0 ? -1 : slots[at];
    }

    /// <summary>늘어놓은 줄에서 한 줄을 고르게 한다(<c>0x0046C3E0</c>). 고른 줄 자리를 낸다(중단이면 −1).</summary>
    public static int Pick(Window owner, IReadOnlyList<Entry> rows, ItemTable? items, string title, int startSet)
    {
        var chosen = Show(owner, rows, items, title, startSet, many: false);
        return chosen.Count > 0 ? chosen[0] : -1;
    }

    /// <summary>여러 줄을 고르게 한다 — 수리처럼 고름표로 받는 자리다. 물렀으면 빈 목록.</summary>
    public static List<int> PickMany(Window owner, IReadOnlyList<Entry> rows, ItemTable? items, string title, int startSet) =>
        Show(owner, rows, items, title, startSet, many: true);

    /// <summary>
    /// 「매각선박의 선택」 — 여럿을 골라 함께 판다. 견적가격 칸과 「견적합계」 줄이 붙고, 첫 묶음은 대포 · 포탑 · 돛까지 보인다.
    /// 견적가가 0 인 줄(빌린 배)은 <paramref name="rows"/> 에서 흐리게(On 거짓) 넘긴다. 물렀으면 빈 목록.
    /// </summary>
    public static List<int> PickToSell(Window owner, IReadOnlyList<Entry> rows, IReadOnlyList<int> prices,
                                       ItemTable? items, string title) =>
        Show(owner, rows, items, title, 0, many: true, prices);

    private static List<int> Show(Window owner, IReadOnlyList<Entry> rows, ItemTable? items, string title,
                                  int startSet, bool many, IReadOnlyList<int>? prices = null)
    {
        if (rows.Count == 0) return [];
        if (!Remembered.ContainsKey(title)) Remembered[title] = Math.Max(0, startSet);
        var dialog = new ShipPickDialog(rows, items, title, many, prices) { Owner = owner };
        dialog.ShowDialog();
        return dialog._chosen;
    }
}
