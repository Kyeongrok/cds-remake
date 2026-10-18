using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using CdsHelper.Game.Engine.Sea;
using CdsHelper.Game.Local.Helpers;
using CdsHelper.Support.Local.Models;

namespace CdsHelper.Game.UI.Views;

/// <summary>
/// 해전 끝의 <b>짐 창</b> — 잡은 배에서 빼앗은 보급품·교역품을 함대에 옮겨 싣는다.
/// </summary>
/// <remarks>
/// 원본은 항구 보급 창과 같은 틀(<c>0x004211A0</c>)을 <b>값 없는 벌</b>로 쓴다(생성자 <c>0x004879A0</c>).
/// <code>
///   용량  합/한도   중량  합/한도   소지금 %8d닢          (넘으면 빨강, 색 0x3B)
///   탑재품명        단중량  현재량  보충량
///   식량 · 물 · 자재 · 탄약                             ↑↓   최대 = 원래 + N (풀에서 가져온다)
///   교역품 칸(함대 짐 칸 차례)                  [⇄]     ↑↓   최대 = 원래(덜어 내기만 된다)
///   미탑재품  식량%4d통　물%4d통　자재%4d통　탄약%4d통  빼앗은 교역품       [↑] 실는다
///                                               [결정] [취소]
/// </code>
/// 교역품 줄의 ⇄ 는 그 줄을 미탑재품과 맞바꾼다(<c>0x004880B0</c> — 미탑재품이 비었으면 그 줄을
/// 원래 갯수 그대로 내려 둔다). 미탑재품을 실으면 새 줄로 붙는데 짐이 벌써 여덟 가지면
/// 「짐 종류가 너무 많습니다」(<c>0x0055F408</c>)다(<c>0x004882C0</c>, 같은 품목 줄과 합치지 않는다).
///
/// <b>결정으로만 닫힌다</b> — 취소는 처음으로 되돌릴 뿐이다(<c>0x00487F00</c>). 결정은 용량·중량을 보고
/// (<c>0x00488980</c>) 넘으면 말만 하고 그대로 있는다. 되쓸 때 식량·물은 통 x 10 이고, 짐 칸은 갯수가
/// 있는 줄만 줄 차례로 다시 쓴다. <b>미탑재품과 풀에 남은 것은 버린다.</b>
///
/// 줄 이름을 누르면 수를 적는 창(<c>0x00454AA0</c> — 제목은 품목 이름, 「탑재수」「통」, 눈금 「현재수」
/// 「한통의 무게」)이 뜬다. ↑↓(Shift 로 열씩)도 된다.
/// 교역품 이름 뒤 「[%d]」(<c>0x0055F338</c>)는 유통 기한의 달수다.
/// </remarks>
public sealed class LootDialog : GameWindow
{
    private static readonly Brush Back = Frozen(Color.FromRgb(0x31, 0x18, 0x18));
    private static readonly Brush Line = Frozen(Color.FromRgb(0x11, 0x09, 0x09));
    private static readonly Brush Ink = Frozen(Color.FromRgb(0xCB, 0xC5, 0xC5));

    /// <summary>한도를 넘은 수의 색(<c>0x3B</c>).</summary>
    private const byte OverColor = 0x3B;

    private const double BoardWidth = 537;
    /// <remarks>보충량 칸은 계산기 단추 하나만큼(18) 넓혔다 — 판도 그만큼 넓다.</remarks>
    private const double UnitWidth = 70, HaveWidth = 84, AddWidth = 118, SwapWidth = 24;
    private const int Step = 1, FastStep = 10;

    private static SolidColorBrush Frozen(Color c)
    {
        var b = new SolidColorBrush(c);
        b.Freeze();
        return b;
    }

    /// <summary>교역품 한 줄 — 원래 갯수와 지금 실을 갯수.</summary>
    private sealed class GoodsRow(BattleLoot.Goods goods)
    {
        public BattleLoot.Goods Goods { get; } = goods;
        public int Original { get; } = goods.Count;
        public int Now { get; set; } = goods.Count;
    }

    private readonly Player _player;
    private readonly GoodsTable? _goods;
    private readonly Func<int, string> _cityName;
    private readonly int _pool;
    private readonly BattleLoot.Goods? _loot;

    private readonly int[] _original = new int[Supply.Count];
    private readonly int[] _now = new int[Supply.Count];
    private readonly List<GoodsRow> _rows = [];
    private BattleLoot.Goods? _aside;

    private readonly StackPanel _body = new();

    private LootDialog(Player player, GoodsTable? goods, Func<int, string> cityName, int pool,
                       BattleLoot.Goods? loot)
    {
        _player = player;
        _goods = goods;
        _cityName = cityName;
        _pool = pool;
        _loot = loot is { Count: > 0 } ? loot : null;

        Title = "짐";
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.WidthAndHeight;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        Background = Back;

        Reset();

        var right = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var decide = new GameButton("결정", Decide) { Margin = new Thickness(0, 0, 6, 0) };
        right.Children.Add(decide);
        right.Children.Add(new GameButton("취소", () => { Reset(); Paint(); }) { Margin = new Thickness(0) });

        // 「최대」·「10일분」은 원본에 없다 — 보급 창의 같은 이름 단추처럼 한 번에 채운다(풀에 있는 만큼만).
        var left = new StackPanel { Orientation = Orientation.Horizontal };
        left.Children.Add(new GameButton("최대", Fill) { Margin = new Thickness(0, 0, 6, 0) });
        left.Children.Add(new GameButton("10일분", TenDays) { Margin = new Thickness(0) });

        var buttons = new DockPanel { LastChildFill = false };
        DockPanel.SetDock(left, Dock.Left);
        DockPanel.SetDock(right, Dock.Right);
        buttons.Children.Add(left);
        buttons.Children.Add(right);

        var page = new StackPanel();
        page.Children.Add(new Border { Width = BoardWidth, Margin = new Thickness(14, 10, 14, 6), Child = _body });
        page.Children.Add(new Border { Margin = new Thickness(10, 0, 10, 10), Child = buttons });

        var frame = GameUi.InfoFrame(page, Back, Line);
        GameUi.EnableDrag(this, frame);
        Content = frame;

        // 창을 닫는 길은 결정뿐이다 — 창 닫기도 막는다.
        Closing += (_, e) => { if (!_decided) e.Cancel = true; };
        Paint();
    }

    private bool _decided;

    /// <summary>처음 값으로 — 보급품은 지금 실린 통, 교역품은 지금 짐 칸, 미탑재품은 빼앗은 것(<c>0x00487F00</c>).</summary>
    private void Reset()
    {
        for (int i = 0; i < Supply.Count; i++)
            _original[i] = _now[i] = _player.SupplyOf(Supply.All[i].Kind);
        _rows.Clear();
        foreach (var c in _player.CargoHold)
            _rows.Add(new GoodsRow(new BattleLoot.Goods(c.Kind, c.Count, c.Origin, c.UnitWeight, c.Shelf)));
        _aside = _loot;
    }

    // ── 셈 ───────────────────────────────────────────────────────────────────

    /// <summary>보급품 i 의 풀에 남은 통 — 처음 N, 실으면 줄고 덜면 는다(<c>0x00487C20</c>).</summary>
    private int PoolLeft(int i) => _pool + _original[i] - _now[i];

    private int Barrels => _now.Sum() + _rows.Sum(r => r.Now);

    private int Weight => Supply.All.Sum(s => _now[(int)s.Kind] * s.UnitWeight)
                          + _rows.Sum(r => r.Now * r.Goods.UnitWeight);

    /// <summary>무게 한도 — 배마다 톤수에서 대포 무게를 뺀 것(<c>0x004743F0</c>).</summary>
    private int WeightLimit => _player.Tonnage - _player.GunWeight;

    // ── 그리기 ───────────────────────────────────────────────────────────────

    private void Paint()
    {
        _body.Children.Clear();

        var head = new StackPanel { Orientation = Orientation.Horizontal };
        head.Children.Add(Label("용량 "));
        head.Children.Add(Label($"{Barrels,6}", Barrels > _player.Capacity));
        head.Children.Add(Label($"/ {_player.Capacity}"));
        // 모드 「중량 없음」이면 무게 칸을 안 낸다.
        if (!Player.IgnoresWeight())
        {
            head.Children.Add(Label("    중량 "));
            head.Children.Add(Label($"{Weight,6}", Weight > WeightLimit));
            head.Children.Add(Label($"/ {WeightLimit}"));
        }
        head.Children.Add(Label($"    소지금 {_player.Gold,8}닢"));
        _body.Children.Add(head);

        _body.Children.Add(Row(null, Label("탑재품명"),
                               Cell(Label("단중량"), UnitWidth), Cell(Label("현재량"), HaveWidth),
                               Cell(Label("보충량"), AddWidth)));

        for (int i = 0; i < Supply.Count; i++)
        {
            int at = i;
            var s = Supply.All[i];
            _body.Children.Add(Row(null, Clickable(Label(s.Name), () => Enter(s.Name, _now[at], _original[at] + _pool,
                                                                              _original[at], s.UnitWeight,
                                                                              v => _now[at] = v)),
                Cell(Label($"{s.UnitWeight,6}"), UnitWidth),
                Cell(Label($"{_original[i],6}통"), HaveWidth),
                Cell(Spin(_now[i] - _original[i], _ => BumpSupply(at, +1), _ => BumpSupply(at, -1),
                          () => Enter(s.Name, _now[at], _original[at] + _pool, _original[at], s.UnitWeight,
                                      v => _now[at] = v)), AddWidth)));
        }

        for (int k = 0; k < _rows.Count; k++)
        {
            int at = k;
            var r = _rows[k];
            _body.Children.Add(Row(Arrow(UiSprites.IconDown, () => Swap(at)),
                Clickable(Label($"{GoodsName(r.Goods.Kind)} [{Months(r.Goods.Shelf)}]"), () => Enter(GoodsName(r.Goods.Kind), r.Now, r.Original,
                                                                     r.Original, r.Goods.UnitWeight,
                                                                     v => r.Now = v)),
                Cell(Label($"{r.Goods.UnitWeight,6}"), UnitWidth),
                Cell(Label($"{r.Original,6}통"), HaveWidth),
                Cell(Spin(r.Now - r.Original, _ => BumpGoods(at, +1), _ => BumpGoods(at, -1),
                          () => Enter(GoodsName(r.Goods.Kind), r.Now, r.Original, r.Original, r.Goods.UnitWeight,
                                      v => r.Now = v)), AddWidth)));
        }

        // 미탑재품 — 빼앗은 교역품이 남았거나 풀에 남은 것이 있을 때만(0x00488180).
        bool anyPool = Enumerable.Range(0, Supply.Count).Any(i => PoolLeft(i) > 0);
        if (_aside is not null || anyPool)
        {
            _body.Children.Add(new Border { Height = 8 });
            _body.Children.Add(Row(null, Label("미탑재품")));
            _body.Children.Add(Row(null, Label(
                $"식량{PoolLeft(0),4}통　물{PoolLeft(1),4}통　자재{PoolLeft(2),4}통　탄약{PoolLeft(3),4}통")));
            if (_aside is { } aside)
                _body.Children.Add(Row(Arrow(UiSprites.IconUp, LoadAside),
                    Label($"{GameUi.Pad(GoodsName(aside.Kind), 12)}[{Months(aside.Shelf)}] {aside.Count}통 ({_cityName(aside.Origin)}산)")));
        }
    }

    /// <summary>「[%d]」 달수(<c>0x004B58F0</c>).</summary>
    private static int Months(int shelf) => shelf <= 0 ? 0 : (shelf + 29) / 30;

    private string GoodsName(int kind) => _goods?.Find(kind)?.Name ?? $"교역품 {kind}";

    /// <summary>수를 적는 창을 띄워 그 줄을 고친다.</summary>
    private void Enter(string name, int now, int max, int original, int unitWeight, Action<int> set)
    {
        if (CountDialog.Set(this, name, "탑재수", "통", now, max,
                            new CountDialog.Gauge("현재수", original),
                            new CountDialog.Gauge("한통의 무게", unitWeight, "")) is { } v)
        {
            set(Math.Clamp(v, 0, max));
            Paint();
        }
    }

    private static UIElement Clickable(FrameworkElement label, Action run)
    {
        label.Cursor = Cursors.Hand;
        label.MouseLeftButtonDown += (_, e) => e.Handled = true;
        label.MouseLeftButtonUp += (_, e) => { e.Handled = true; run(); };
        return label;
    }

    /// <summary>↑↓ 한 번 — 더 못 움직이면 <c>false</c>(누르고 있던 되풀이가 멈춘다).</summary>
    private bool BumpSupply(int i, int by)
    {
        int step = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? FastStep : Step;
        int to = Math.Clamp(_now[i] + by * step, 0, _original[i] + _pool);
        if (to == _now[i]) return false;
        _now[i] = to;
        Paint();
        return true;
    }

    private bool BumpGoods(int k, int by)
    {
        int step = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? FastStep : Step;
        int to = Math.Clamp(_rows[k].Now + by * step, 0, _rows[k].Original);
        if (to == _rows[k].Now) return false;
        _rows[k].Now = to;
        Paint();
        return true;
    }

    /// <summary>남은 용량(통) — 넘쳐 있으면 0.</summary>
    private int RoomLeft => Math.Max(0, _player.Capacity - Barrels);

    /// <summary>보급품 i 를 무게 여유로 몇 통 더 실을 수 있나. 「중량 없음」이면 끝없다.</summary>
    private int ByWeight(int i)
    {
        int unit = Supply.All[i].UnitWeight;
        if (Player.IgnoresWeight() || unit <= 0) return Player.NoWeightRoom;
        return Math.Max(0, WeightLimit - Weight) / unit;
    }

    /// <summary>
    /// 「최대」 — 풀에 남은 보급품을 실을 수 있는 데까지 싣는다. 식량과 물은 <b>한 통씩 번갈아</b> 올리고
    /// (보급 창 「최대」처럼 짝으로), 그다음 탄약 · 자재 차례다.
    /// </summary>
    private void Fill()
    {
        int food = (int)SupplyKind.Food, water = (int)SupplyKind.Water;
        bool moved = false;
        while (true)
        {
            int pick = PoolLeft(food) > 0 && (PoolLeft(water) <= 0 || _now[food] <= _now[water]) ? food
                     : PoolLeft(water) > 0 ? water : -1;
            if (pick < 0 || RoomLeft <= 0 || ByWeight(pick) <= 0) break;
            _now[pick]++;
            moved = true;
        }
        foreach (int i in new[] { (int)SupplyKind.Ammo, (int)SupplyKind.Material })
        {
            int n = Math.Min(PoolLeft(i), Math.Min(RoomLeft, ByWeight(i)));
            if (n <= 0) continue;
            _now[i] += n;
            moved = true;
        }
        if (moved) Paint();
    }

    /// <summary>
    /// 「10일분」 — 식량·물을 열흘 갈 만큼(선원수만큼의 통)까지 채운다(<see cref="Supply.BarrelsForDays"/>).
    /// 이미 그보다 많으면 그대로 두고, 풀 · 용량 · 무게가 허락하는 데까지만이다.
    /// </summary>
    private void TenDays()
    {
        int want = Supply.BarrelsForDays(10, _player.Crew);
        bool moved = false;
        foreach (int i in new[] { (int)SupplyKind.Food, (int)SupplyKind.Water })
        {
            int n = Math.Min(Math.Max(0, want - _now[i]), Math.Min(PoolLeft(i), Math.Min(RoomLeft, ByWeight(i))));
            if (n <= 0) continue;
            _now[i] += n;
            moved = true;
        }
        if (moved) Paint();
    }

    /// <summary>교역품 줄을 미탑재품과 맞바꾼다(<c>0x004880B0</c>).</summary>
    private void Swap(int k)
    {
        var down = _rows[k].Goods with { Count = _rows[k].Original };
        if (_aside is { } up) _rows[k] = new GoodsRow(up);
        else _rows.RemoveAt(k);
        _aside = down;
        Paint();
    }

    /// <summary>미탑재품을 새 줄로 싣는다(<c>0x004882C0</c>).</summary>
    private void LoadAside()
    {
        if (_aside is not { } aside) return;
        if (_rows.Count >= Player.CargoSlots)
        {
            GameDialog.Show(this, "짐 종류가 너무 많습니다");
            return;
        }
        _rows.Add(new GoodsRow(aside));
        _aside = null;
        Paint();
    }

    /// <summary>결정 — 한도를 보고 되쓴다(<c>0x00487DF0</c>).</summary>
    private void Decide()
    {
        bool heavy = !Player.IgnoresWeight() && Weight > WeightLimit, full = Barrels > _player.Capacity;
        if (heavy || full)
        {
            GameDialog.Show(this, heavy && full ? "중량도 용량도 한계를 넘고 있습니다!"
                                  : heavy ? "중량이 한계를 넘고 있습니다!" : "용량이 한계를 넘고 있습니다!");
            return;
        }

        for (int i = 0; i < Supply.Count; i++) _player.SetSupply(Supply.All[i].Kind, _now[i]);
        _player.RestoreCargo(_rows.Where(r => r.Now > 0)
                                  .Select(r => new Player.Cargo(r.Goods.Kind, r.Now, r.Goods.Origin, r.Goods.UnitWeight,
                                                                r.Goods.Shelf)));
        _decided = true;
        Close();
    }

    // ── 조각 ─────────────────────────────────────────────────────────────────

    private static UIElement Row(UIElement? lead, UIElement name, params UIElement[] cells)
    {
        var line = new DockPanel { Margin = new Thickness(0, 1, 0, 1), LastChildFill = false };
        var left = new StackPanel { Orientation = Orientation.Horizontal };
        left.Children.Add(new Border { Width = SwapWidth, Child = lead });
        left.Children.Add(name);
        DockPanel.SetDock(left, Dock.Left);
        line.Children.Add(left);
        for (int i = cells.Length - 1; i >= 0; i--)
        {
            DockPanel.SetDock(cells[i], Dock.Right);
            line.Children.Add(cells[i]);
        }
        return line;
    }

    private static FrameworkElement Cell(UIElement inner, double width) => new Border
    {
        Width = width,
        Child = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Children = { inner },
        },
    };

    /// <summary>「＋%4d」 / 「－%4d」 와 ↑↓, 그리고 계산기.</summary>
    /// <remarks>
    /// 계산기는 원본에 없다 — 보급 창처럼 <b>실을 총량</b>을 적는 창(이름을 눌렀을 때와 같은 것)을 연다.
    /// 화살표로 하나씩 올리기가 너무 번거로웠다.
    /// </remarks>
    /// <remarks>↑↓ 는 누르고 있으면 되풀이한다(<see cref="HoldRepeat"/>).</remarks>
    private static UIElement Spin(int delta, Func<bool, bool> up, Func<bool, bool> down, Action calc)
    {
        var spin = new StackPanel { Orientation = Orientation.Horizontal };
        spin.Children.Add(Label(delta < 0 ? $"－{-delta,4}" : $"＋{delta,4}"));
        spin.Children.Add(Repeating(UiSprites.IconUp, up));
        spin.Children.Add(Repeating(UiSprites.IconDown, down));
        var box = Arrow(UiSprites.IconCalc, calc);
        if (box is FrameworkElement f) f.ToolTip = "실을 양을 적는다";
        spin.Children.Add(box);
        return spin;
    }

    private static UIElement Arrow(int icon, Action run)
    {
        var box = IconBox(icon);
        box.MouseLeftButtonDown += (_, e) => e.Handled = true;
        box.MouseLeftButtonUp += (_, e) => { e.Handled = true; run(); };
        return box;
    }

    /// <summary>누르고 있으면 되풀이하는 화살표 — 수를 올리고 내린다.</summary>
    private static UIElement Repeating(int icon, Func<bool, bool> run)
    {
        var box = IconBox(icon);
        HoldRepeat.Attach(box, run);
        return box;
    }

    private static FrameworkElement IconBox(int icon)
    {
        FrameworkElement box = GameUi.GameIcon(icon)
            ?? (FrameworkElement)new Border
            {
                Width = UiSprites.IconWidth,
                Height = UiSprites.IconHeight,
                Background = GameUi.ItemFill,
                BorderBrush = GameUi.ItemEdge,
                BorderThickness = new Thickness(1),
            };
        box.Margin = new Thickness(1, 0, 0, 0);
        box.Cursor = Cursors.Hand;
        box.VerticalAlignment = VerticalAlignment.Center;
        return box;
    }

    private static GameUi.GameLabel Label(string text, bool over = false) =>
        new(over ? OverColor : GameFont.WhiteColor) { Text = text, FallbackBrush = over ? Brushes.Red : Ink };

    /// <summary>짐 창을 띄운다. 결정할 때까지 돌아오지 않는다.</summary>
    public static void Show(Window owner, Player player, GoodsTable? goods, Func<int, string> cityName,
                            int pool, BattleLoot.Goods? loot) =>
        new LootDialog(player, goods, cityName, pool, loot) { Owner = owner }.ShowDialog();
}
