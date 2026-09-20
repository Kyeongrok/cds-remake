using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using CdsHelper.Game.Local.Helpers;
using CdsHelper.Support.Local.Models;

namespace CdsHelper.Game.UI.Views;

/// <summary>
/// 짐 덜기 창 — 배가 줄어 짐이 넘치면 <b>물릴 수 없이</b> 뜬다(<c>0x0044DEF0</c> → <c>0x0044DCB0</c>).
/// </summary>
/// <remarks>
/// 게임은 항구 보급 창과 같은 틀(<c>0x004211A0</c>)을 <b>값 있는 벌</b>로 쓴다(생성자 <c>0x0044D630</c>).
/// <code>
///   용량  합/한도   중량  합/한도   소지금 %8d닢            (넘으면 빨강, 색 0x3B)
///   탑재품명        단중량/단가   현재량  보충량      가격     (0x0055F2F8)
///   식량 · 물 · 자재 · 탄약     단가 0      ↑↓ 덜기만 된다
///   교역품 칸 「[%d]」          단가 = 도시 기준가 / 2 (0x0044D910 — 바다거나 썩었으면 0)
///   총계                                              %8ld닢   (0x0055F380)
///                          [되돌린다] [결정] [전삭제]
/// </code>
/// 들어오는 곳은 여덟이다 — 빌린 배 돌려주기(<c>0x0040FEFD</c>) · 빌린 배 탈주(<c>0x00410354</c>) ·
/// 조선소 배 매각(<c>0x0044B968</c>) · 함대편성 끝(<c>0x0046A66E</c>) · 개조 결과 셋과 대포 결과
/// (<c>0x00494BA1</c> · <c>0x00495A95</c> · <c>0x00496168</c> · <c>0x004963AF</c>).
///
/// <b>결정으로만 닫힌다</b> — 취소 단추가 없다. 결정은 용량·중량을 보고(<c>0x0044D9C0</c> →
/// <c>0x0044D590</c>) 넘으면 말만 하고 그대로 있는다. 닫히면 덜어 낸 교역품 값(가격 칸의 합)이
/// 소지금에 붙는다(<c>0x0047CBC0</c>) — 보급품은 값이 없다. 식량·물은 통 x 10 으로 되쓴다.
/// </remarks>
public sealed class CargoDropDialog : GameWindow
{
    private static readonly Brush Back = Frozen(Color.FromRgb(0x31, 0x18, 0x18));
    private static readonly Brush Line = Frozen(Color.FromRgb(0x11, 0x09, 0x09));
    private static readonly Brush Ink = Frozen(Color.FromRgb(0xCB, 0xC5, 0xC5));

    /// <summary>한도를 넘은 수의 색(<c>0x3B</c>).</summary>
    private const byte OverColor = 0x3B;

    private const double BoardWidth = 600;
    private const double UnitWidth = 130, HaveWidth = 84, AddWidth = 100, PriceWidth = 90;
    private const int Step = 1, FastStep = 10;

    private static SolidColorBrush Frozen(Color c)
    {
        var b = new SolidColorBrush(c);
        b.Freeze();
        return b;
    }

    /// <summary>교역품 한 줄 — 원래 갯수와 남길 갯수, 한 통 값.</summary>
    private sealed class GoodsRow(Player.Cargo cargo, int unitPrice)
    {
        public Player.Cargo Cargo { get; } = cargo;
        public int UnitPrice { get; } = unitPrice;
        public int Now { get; set; } = cargo.Count;
    }

    private readonly Player _player;
    private readonly Func<int, string> _goodsName;
    private readonly int[] _original = new int[Supply.Count];
    private readonly int[] _now = new int[Supply.Count];
    private readonly List<GoodsRow> _rows = [];
    private readonly StackPanel _body = new();

    private CargoDropDialog(Player player, Func<int, string> goodsName, Func<Player.Cargo, int> unitPrice)
    {
        _player = player;
        _goodsName = goodsName;

        for (int i = 0; i < Supply.Count; i++)
            _original[i] = _now[i] = player.SupplyOf(Supply.All[i].Kind);
        foreach (var c in player.CargoHold) _rows.Add(new GoodsRow(c, unitPrice(c)));

        Title = "짐";
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.WidthAndHeight;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        Background = Back;

        // 단추 셋(0x0044DA70) — 되돌린다(0x0044D9F0) · 결정 · 전삭제(0x0044DA30).
        var right = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        right.Children.Add(new GameButton("되돌린다", Restore) { Margin = new Thickness(0, 0, 6, 0) });
        right.Children.Add(new GameButton("결정", Decide) { Margin = new Thickness(0, 0, 6, 0) });
        right.Children.Add(new GameButton("전삭제", DropAll) { Margin = new Thickness(0) });

        var page = new StackPanel();
        page.Children.Add(new Border { Width = BoardWidth, Margin = new Thickness(14, 10, 14, 6), Child = _body });
        page.Children.Add(new Border { Margin = new Thickness(10, 0, 10, 10), Child = right });

        var frame = GameUi.InfoFrame(page, Back, Line);
        GameUi.EnableDrag(this, frame);
        Content = frame;

        // 창을 닫는 길은 결정뿐이다 — 창 닫기도 막는다.
        Closing += (_, e) => { if (!_decided) e.Cancel = true; };
        Paint();
    }

    private bool _decided;

    // ── 셈 ───────────────────────────────────────────────────────────────────

    private int Barrels => _now.Sum() + _rows.Sum(r => r.Now);

    private int Weight => Supply.All.Sum(s => _now[(int)s.Kind] * s.UnitWeight)
                          + _rows.Sum(r => r.Now * r.Cargo.UnitWeight);

    /// <summary>무게 한도 — 배마다 톤수에서 대포 무게를 뺀 것(<c>0x004743F0</c>).</summary>
    private int WeightLimit => _player.Tonnage - _player.GunWeight;

    /// <summary>덜어 낸 교역품 값의 합 — 창이 내는 값(<c>0x00420920</c>)이다.</summary>
    private int Total => _rows.Sum(r => (r.Cargo.Count - r.Now) * r.UnitPrice);

    // ── 그리기 ───────────────────────────────────────────────────────────────

    private void Paint()
    {
        _body.Children.Clear();

        var head = new StackPanel { Orientation = Orientation.Horizontal };
        head.Children.Add(Label("용량 "));
        head.Children.Add(Label($"{Barrels,6}", Barrels > _player.Capacity));
        head.Children.Add(Label($"/ {_player.Capacity}"));
        head.Children.Add(Label("    중량 "));
        head.Children.Add(Label($"{Weight,6}", Weight > WeightLimit));
        head.Children.Add(Label($"/ {WeightLimit}"));
        head.Children.Add(Label($"    소지금 {_player.Gold,8}닢"));
        _body.Children.Add(head);

        _body.Children.Add(Row(Label("탑재품명"),
                               Cell(Label("단중량/단가"), UnitWidth), Cell(Label("현재량"), HaveWidth),
                               Cell(Label("보충량"), AddWidth), Cell(Label("가격"), PriceWidth)));

        for (int i = 0; i < Supply.Count; i++)
        {
            int at = i;
            var s = Supply.All[i];
            _body.Children.Add(Row(
                Clickable(Label(s.Name), () => Enter(s.Name, _now[at], _original[at], s.UnitWeight, v => _now[at] = v)),
                Cell(Label($"{s.UnitWeight,6}/ {0,6}"), UnitWidth),
                Cell(Label($"{_original[i],6}통"), HaveWidth),
                Cell(Spin(_now[i] - _original[i], () => Bump(ref _now[at], _original[at], +1),
                                                  () => Bump(ref _now[at], _original[at], -1)), AddWidth),
                Cell(Label($"{0,6}닢"), PriceWidth)));
        }

        for (int k = 0; k < _rows.Count; k++)
        {
            var r = _rows[k];
            string name = _goodsName(r.Cargo.Kind);
            _body.Children.Add(Row(
                Clickable(Label($"{name} [{Months(r.Cargo.Shelf)}]"),
                          () => Enter(name, r.Now, r.Cargo.Count, r.Cargo.UnitWeight, v => r.Now = v)),
                Cell(Label($"{r.Cargo.UnitWeight,6}/ {r.UnitPrice,6}"), UnitWidth),
                Cell(Label($"{r.Cargo.Count,6}통"), HaveWidth),
                Cell(Spin(r.Now - r.Cargo.Count, () => BumpRow(r, +1), () => BumpRow(r, -1)), AddWidth),
                Cell(Label($"{(r.Cargo.Count - r.Now) * r.UnitPrice,6}닢"), PriceWidth)));
        }

        _body.Children.Add(new Border { Height = 6 });
        _body.Children.Add(Row(Label("총계"), Cell(Label($"{Total,8}닢"), PriceWidth)));
    }

    /// <summary>「[%d]」 달수(<c>0x004B58F0</c>).</summary>
    private static int Months(int shelf) => shelf <= 0 ? 0 : (shelf + 29) / 30;

    /// <summary>
    /// 수를 적는 창(<c>0x00454AA0</c>) — 제목은 품목 이름, 「탑재수」「통」, 눈금 「현재수」「한통의 무게」.
    /// 0 에서 원래 수까지다.
    /// </summary>
    private void Enter(string name, int now, int max, int unitWeight, Action<int> set)
    {
        if (CountDialog.Set(this, name, "탑재수", "통", now, max,
                            new CountDialog.Gauge("현재수", max),
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

    /// <summary>↑↓ — 원래 수를 넘어 더할 수는 없다.</summary>
    private void Bump(ref int value, int max, int by)
    {
        int step = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? FastStep : Step;
        value = Math.Clamp(value + by * step, 0, max);
        Paint();
    }

    private void BumpRow(GoodsRow r, int by)
    {
        int now = r.Now;
        Bump(ref now, r.Cargo.Count, by);
        r.Now = now;
    }

    private void Restore()
    {
        Array.Copy(_original, _now, Supply.Count);
        foreach (var r in _rows) r.Now = r.Cargo.Count;
        Paint();
    }

    private void DropAll()
    {
        Array.Clear(_now);
        foreach (var r in _rows) r.Now = 0;
        Paint();
    }

    /// <summary>결정 — 한도를 보고 되쓰고, 덜어 낸 값을 받는다.</summary>
    private void Decide()
    {
        bool heavy = Weight > WeightLimit, full = Barrels > _player.Capacity;
        if (heavy || full)
        {
            GameDialog.Show(this, OverloadLine(heavy, full));
            return;
        }

        for (int i = 0; i < Supply.Count; i++) _player.SetSupply(Supply.All[i].Kind, _now[i]);
        _player.RestoreCargo(_rows.Where(r => r.Now > 0).Select(r => r.Cargo with { Count = r.Now }));
        _player.Earn(Total);
        _decided = true;
        Close();
    }

    /// <summary>넘쳤다는 말 — 둘 다 <c>0x0055AC60</c>, 중량만 <c>0x0055AC88</c>, 용량만 <c>0x0055ACA8</c>.</summary>
    private static string OverloadLine(bool heavy, bool full) =>
        heavy && full ? "중량도 용량도 한계를 넘고 있습니다!"
        : heavy ? "중량이 한계를 넘고 있습니다!" : "용량이 한계를 넘고 있습니다!";

    // ── 조각 ─────────────────────────────────────────────────────────────────

    private static UIElement Row(UIElement name, params UIElement[] cells)
    {
        var line = new DockPanel { Margin = new Thickness(0, 1, 0, 1), LastChildFill = false };
        DockPanel.SetDock(name, Dock.Left);
        line.Children.Add(name);
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

    /// <summary>「＋%4d」 / 「－%4d」 와 ↑↓.</summary>
    private static UIElement Spin(int delta, Action up, Action down)
    {
        var spin = new StackPanel { Orientation = Orientation.Horizontal };
        spin.Children.Add(Label(delta < 0 ? $"－{-delta,4}" : $"＋{delta,4}"));
        spin.Children.Add(Arrow(UiSprites.IconUp, up));
        spin.Children.Add(Arrow(UiSprites.IconDown, down));
        return spin;
    }

    private static UIElement Arrow(int icon, Action run)
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
        box.MouseLeftButtonDown += (_, e) => e.Handled = true;
        box.MouseLeftButtonUp += (_, e) => { e.Handled = true; run(); };
        return box;
    }

    private static GameUi.GameLabel Label(string text, bool over = false) =>
        new(over ? OverColor : GameFont.WhiteColor) { Text = text, FallbackBrush = over ? Brushes.Red : Ink };

    /// <summary>
    /// 짐이 넘치면 알리고 짐 덜기 창을 띄운다(<c>0x0044DEF0</c>). 안 넘치면 아무것도 안 한다.
    /// </summary>
    /// <param name="city">지금 도시 — 교역품 단가가 이 도시 지역 기준가의 절반이다. 바다면 -1(값 0).</param>
    public static void Force(Window owner, Engine.Game game, int city)
    {
        var player = game.Player;
        bool heavy = player.LoadedWeight > player.Tonnage;
        bool full = player.LoadedBarrels > player.Capacity;
        if (!heavy && !full) return;

        GameDialog.Show(owner, OverloadLine(heavy, full));

        var trade = game.Trade;
        var goods = game.Goods;
        // 한 통 값(0x0044D910) — 도시 지역 기준가(0x0042E3C0)의 절반, 바다거나 썩었으면 0.
        int UnitPrice(Player.Cargo c) =>
            city < 0 || c.Spoiled || trade == null ? 0 : trade.BasePrice(trade.RegionOf(city), c.Kind) / 2;
        string Name(int kind) => goods?.Find(kind)?.Name ?? $"교역품 {kind}";

        new CargoDropDialog(player, Name, UnitPrice) { Owner = owner }.ShowDialog();
    }
}
