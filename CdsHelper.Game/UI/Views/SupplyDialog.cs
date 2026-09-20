using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using CdsHelper.Game.Local.Helpers;
using CdsHelper.Support.Local.Models;

namespace CdsHelper.Game.UI.Views;

/// <summary>
/// 항구 보급 화면 — 식량·물·자재·탄약을 통 단위로 사서 싣는다.
/// </summary>
/// <remarks>
/// 게임 화면을 그대로 옮겼다.
/// <code>
///   용량 2871/2872   중량 21745/26375   소지금 3805119닢
///   탑재품명   단중량/단가   현재량   보충량       가격
///   식량            5/  19   1151통  +  276 ↑↓   5244닢
///   물             10/  12   1151통  +  276 ↑↓   3312닢
///   자재            5/  31      0통  +    0 ↑↓      0닢
///   탄약           20/  31     17통  +    0 ↑↓      0닢
///
///                                        총계    8556닢
///   [최대] [10일분] [일지정] [전회분]     [결정] [돌아간다]
/// </code>
///
/// <b>이 화면은 밤색 판 하나다.</b> 목록 창들과 달리 양피지 칸이 없다 — <c>#311818</c> 바탕에
/// 밝은 글자와 단추가 바로 얹힌다. 테는 검은 선 <b>셋</b>이다. 게임 화면 왼쪽 테를 한 점씩
/// 찍으면 <c>(17,9,9) (46,22,22) (49,24,24) (46,22,22) (17,9,9) (11,5,5)</c> 처럼 짙은 선과
/// 바탕이 번갈아 나오는데, 남회색 정보 창과 짜임이 같다. 그래서
/// <see cref="GameUi.InfoFrame(UIElement, Brush, Brush)"/> 를 색만 바꿔 쓴다.
/// <b>제목 줄도 없다.</b>
///
/// 단추 글은 게임 것 그대로다(<c>0x00545650</c> 벌 — 결정·최대·10일분·일지정·전회분).
/// 품목 이름도 그렇다(<c>0x0055F248</c>~, 갈래 분기는 <c>0x004208A0</c>).
///
/// <b>날수는 선원수로 센다.</b> 게임 <c>0x00494010</c> 이
/// <c>날수 = min(식량통, 물통) * 10 / 총선원수</c> 이므로, 한 사람이 하루에 한 단위를 쓰고
/// 한 통이 열 단위다. 그래서 <b>10일분은 선원수만큼의 통</b>이다.
///
/// <b>일지정</b>(<c>0x0040F6E0</c>)은 「최대」로 채울 수 있는 날수를 윗한도로 수 적기 창
/// 「항해일수 보급」을 띄우고, 고른 날수로 10일분과 같은 셈을 한다. <b>전회분</b>(<c>0x0040EC60</c>)은
/// 지난번 결정한 총량(<see cref="Player.LastSupply"/>)으로 되돌린다 — 탄약만은 그 도시가 탄약을 팔 때만
/// 따라가고(<c>0x0040EC40</c>), 아니면 지금 실린 그대로다.
///
/// <b>탄약은 도시 형편 비트 8 이 선 곳에서만 판다</b>(121곳, 거의 유럽). 아니면 단가가 −1 이라
/// (<c>0x00493FB0</c>) 단가 칸이 「---」(<c>0x0055F368</c>)이고 더 실을 수 없다 — 덜어 내기만 된다.
///
/// 용량·중량은 함대가 실을 수 있는 양이다(<see cref="Player.Capacity"/> ·
/// <see cref="Player.Tonnage"/> — 배마다의 적재량·톤수를 더한 것). 게임은 여기에 실어 둔
/// 교역품까지 같이 세는데, 우리 쪽은 아직 보급품만 센다.
/// </remarks>
public sealed class SupplyDialog : GameWindow
{
    /// <summary>화면 바탕. 게임 화면에서 뽑았다.</summary>
    private static readonly Brush Back = Frozen(Color.FromRgb(0x31, 0x18, 0x18));

    /// <summary>테를 두르는 짙은 선.</summary>
    private static readonly Brush Line = Frozen(Color.FromRgb(0x11, 0x09, 0x09));

    /// <summary>글꼴 조각을 못 읽었을 때 물러설 글씨색.</summary>
    private static readonly Brush Ink = Frozen(Color.FromRgb(0xCB, 0xC5, 0xC5));

    private static SolidColorBrush Frozen(Color c)
    {
        var b = new SolidColorBrush(c);
        b.Freeze();
        return b;
    }

    /// <summary>글이 놓이는 판의 크기. 품목과 총계 사이가 게임처럼 비도록 키를 못 박는다.</summary>
    /// <remarks>
    /// 게임 갈무리를 재어 맞췄다. 머리글이 <b>한 줄짜리 문자열</b>이라
    /// (<c>0x0055F2F8</c> — 전각 빈칸으로 칸을 벌려 놓은 것이다) 폭을 셀 수 있다.
    /// <code>
    ///   머리글 62칸 x 8점 = 496점        ← 이것으로 갈무리 배율 1.74 를 얻었다
    ///   창 바깥                563 x 412 점
    ///   테 8 + 여백 10 + 글 527 + 여백 10 + 테 8 = 563
    /// </code>
    /// 예전에는 <c>620</c> 이라 창이 <c>674</c> 로 나왔다 — 게임보다 <b>111점 넓었다</b>.
    /// 키는 그때도 맞았다(412).
    /// </remarks>
    private const double BoardWidth = 519, BoardHeight = 300;

    /// <summary>칸 폭 — 단중량/단가 · 현재량 · 보충량 · 가격.</summary>
    /// <remarks>판을 줄인 만큼(0.837배) 같이 줄였다. 칸 차례와 결은 그대로다.</remarks>
    private const double UnitWidth = 134, HaveWidth = 84, AddWidth = 100, CostWidth = 92;

    /// <summary>
    /// 한 무리 안의 단추 사이. 게임 갈무리에서 단추 사이가 단추 폭의 한 켜쯤(열 점 안팎)이다.
    /// </summary>
    private const double ButtonGap = 6;

    /// <summary>↑↓ 한 번에 움직이는 통 수. Shift 를 누르면 열 배로 뛴다.</summary>
    private const int Step = 1, FastStep = 10;

    private readonly Player _player;
    private readonly int _rate;

    /// <summary>줄마다 지금 더 실으려는 통 수.</summary>
    private readonly int[] _add = new int[Supply.Count];

    private readonly GameUi.GameLabel[] _addLabels = new GameUi.GameLabel[Supply.Count];
    private readonly GameUi.GameLabel[] _costLabels = new GameUi.GameLabel[Supply.Count];
    private readonly GameUi.GameLabel[] _signLabels = new GameUi.GameLabel[Supply.Count];
    private readonly GameUi.GameLabel _capacity = Label("");
    private readonly GameUi.GameLabel _weight = Label("");
    private readonly GameUi.GameLabel _gold = Label("");
    private readonly GameUi.GameLabel _total = Label("");
    private readonly GameButton _decide;

    /// <summary>이 도시가 탄약을 파는지(도시 형편 비트 8, <c>0x00493FB0</c> · <c>0x0040EC40</c>).</summary>
    private readonly bool _ammoSold;

    private SupplyDialog(Player player, int rate, bool ammoSold)
    {
        _ammoSold = ammoSold;
        _mate = player.MateAt(0).Length > 0;
        _player = player;
        _rate = rate;

        Title = "보급";
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.WidthAndHeight;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        Background = Back;

        // 맨 윗줄 — 용량 · 중량 · 소지금.
        var head = new StackPanel { Orientation = Orientation.Horizontal };
        head.Children.Add(Label("용량 "));
        head.Children.Add(_capacity);
        head.Children.Add(Label("    중량 "));
        head.Children.Add(_weight);
        head.Children.Add(Label("    소지금 "));
        head.Children.Add(_gold);

        var rows = new StackPanel();
        rows.Children.Add(head);
        rows.Children.Add(HeaderRow());
        for (int i = 0; i < Supply.Count; i++) rows.Children.Add(ItemRow(i));

        // 총계는 판 아래쪽에 붙는다 — 게임은 품목과 총계 사이를 통째로 비워 둔다.
        var totalRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        totalRow.Children.Add(Label("총계    "));
        totalRow.Children.Add(_total);

        var board = new DockPanel
        {
            Width = BoardWidth,
            Height = BoardHeight,
            Margin = new Thickness(14, 10, 14, 2),
            LastChildFill = false,
        };
        DockPanel.SetDock(rows, Dock.Top);
        board.Children.Add(rows);
        DockPanel.SetDock(totalRow, Dock.Bottom);
        board.Children.Add(totalRow);

        // 단추는 <b>왼쪽 넷 · 오른쪽 둘</b>로 모인다 — 게임 화면이 그렇다. 단추마다 기본 바깥
        // 여백(GameButton.Spacing)을 그대로 두면 여섯이 판 폭을 꽉 채워 고르게 벌어지고 두 무리
        // 사이가 안 벌어졌다. 그래서 단추 사이만 좁게 띄우고 남는 폭은 두 무리 사이로 몬다.
        static GameButton Tight(GameButton button, double gap)
        {
            button.Margin = new Thickness(0, 0, gap, 0);
            return button;
        }

        _decide = Tight(new GameButton("결정", Decide) { On = false }, ButtonGap);

        var left = new StackPanel { Orientation = Orientation.Horizontal };
        left.Children.Add(Tight(new GameButton("최대", Fill), ButtonGap));
        left.Children.Add(Tight(new GameButton("10일분", TenDays), ButtonGap));
        left.Children.Add(Tight(new GameButton("일지정", AskDays), ButtonGap));
        left.Children.Add(Tight(new GameButton("전회분", Last), 0));

        var right = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        right.Children.Add(_decide);
        right.Children.Add(Tight(new GameButton("돌아간다", Close), 0));

        var buttons = new DockPanel { Margin = new Thickness(10, 0, 10, 10) };
        DockPanel.SetDock(left, Dock.Left);
        buttons.Children.Add(left);
        buttons.Children.Add(right);

        var page = new StackPanel();
        page.Children.Add(board);
        page.Children.Add(buttons);

        // 제목 줄이 없으므로(게임에도 없다) 판 아무 데나 잡아 옮긴다.
        var frame = GameUi.InfoFrame(page, Back, Line);
        GameUi.EnableDrag(this, frame);
        Content = frame;

        KeyDown += (_, e) => { if (e.Key is Key.Escape) Close(); };
        Paint();
    }

    // ── 줄 짓기 ──────────────────────────────────────────────────────────────

    private static UIElement HeaderRow() =>
        Row(Label("탑재품명"),
            Cell(Label("단중량/단가"), UnitWidth),
            Cell(Label("현재량"), HaveWidth),
            Cell(Label("보충량"), AddWidth),
            Cell(Label("가격"), CostWidth));

    private UIElement ItemRow(int index)
    {
        var supply = Supply.All[index];

        _addLabels[index] = Label("");
        _costLabels[index] = Label("");
        _signLabels[index] = Label("+");

        // 보충량 칸은 "+ 000 ↑↓" 한 벌이다. 덜어 내면 부호가 "-" 로 바뀐다.
        var spin = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        spin.Children.Add(_signLabels[index]);
        spin.Children.Add(_addLabels[index]);
        spin.Children.Add(Arrow("↑", () => Bump(index, +1)));
        spin.Children.Add(Arrow("↓", () => Bump(index, -1)));

        return Row(Label(supply.Name),
                   Cell(Label(Sold(supply) ? $"{supply.UnitWeight,3}/{supply.PriceAt(_rate),4}"
                                           : $"{supply.UnitWeight,3}/ ---"), UnitWidth),
                   Cell(Label($"{_player.SupplyOf(supply.Kind),5}통"), HaveWidth),
                   Cell(spin, AddWidth),
                   Cell(_costLabels[index], CostWidth));
    }

    /// <summary>
    /// 줄 하나. 첫 칸(품목 이름)은 왼쪽에 붙고 나머지는 <b>못 박은 폭</b>으로 오른쪽에 선다 —
    /// 그래야 줄마다 숫자가 세로로 맞는다.
    /// </summary>
    /// <remarks>
    /// 오른쪽 붙이기는 <b>먼저 넣은 것이 더 바깥</b>이므로 칸을 거꾸로 넣는다. 그래야 눈에
    /// 보이는 차례가 준 차례(단중량/단가 · 현재량 · 보충량 · 가격)와 같아진다.
    /// </remarks>
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

    /// <summary>오른쪽으로 밀어 붙인 한 칸.</summary>
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

    /// <summary>
    /// ↑·↓ 한 칸. 게임 조각(<c>MISC.CDS</c> 파트 3, 16x16)을 그대로 건다 — 능력치·기술 화면과
    /// 같은 화살표다. 조각을 못 읽었을 때만 글자 화살표로 물러선다.
    /// </summary>
    private static UIElement Arrow(string mark, Action run)
    {
        bool up = mark == "↑";
        FrameworkElement box = GameUi.GameIcon(up ? UiSprites.IconUp : UiSprites.IconDown)
            ?? (FrameworkElement)new Border
            {
                Width = UiSprites.IconWidth,
                Height = UiSprites.IconHeight,
                Background = GameUi.ItemFill,
                BorderBrush = GameUi.ItemEdge,
                BorderThickness = new Thickness(1),
                Child = new TextBlock
                {
                    Text = mark,
                    Foreground = Brushes.Black,
                    FontSize = 11,
                    HorizontalAlignment = HorizontalAlignment.Center,
                },
            };
        box.Margin = new Thickness(1, 0, 0, 0);
        box.Cursor = Cursors.Hand;
        box.VerticalAlignment = VerticalAlignment.Center;
        // 누름은 삼킨다 — 판 끌기가 먼저 걸리면 마우스를 잡아 버려 뗌이 안 온다.
        box.MouseLeftButtonDown += (_, e) => e.Handled = true;
        box.MouseLeftButtonUp += (_, e) => { e.Handled = true; run(); };
        return box;
    }

    /// <summary>밤색 판 위에 얹는 밝은 글씨.</summary>
    private static GameUi.GameLabel Label(string text) =>
        new(GameFont.WhiteColor) { Text = text, FallbackBrush = Ink };

    // ── 셈 ───────────────────────────────────────────────────────────────────

    /// <summary>더 실으려는 것까지 넣은 통 수.</summary>
    private int Barrels => _player.LoadedBarrels + _add.Sum();

    /// <summary>더 실으려는 것까지 넣은 무게.</summary>
    private int Weight => _player.LoadedWeight
                          + Supply.All.Sum(s => _add[(int)s.Kind] * s.UnitWeight);

    /// <summary>줄 값. 덜어 내는 것(음수)은 값을 쳐 주지 않는다 — 버리는 것이다.</summary>
    private int Cost(int index) => Math.Max(0, _add[index]) * Supply.All[index].PriceAt(_rate);

    private int Total => Enumerable.Range(0, Supply.Count).Sum(Cost);

    /// <summary>
    /// 한 통 더 실을 수 있는지 — 용량·중량·소지금을 다 본다. 못 실으면 <b>왜 못 싣는지</b>를
    /// 게임 글로 돌려준다(<c>0x0040F480</c>~<c>0x0040F4E0</c>). 실을 수 있으면 빈 문자열이다.
    /// </summary>
    /// <remarks>
    /// <code>
    ///   0040f494  용량 넘침   0x00545708 「용량 오버입니다.」
    ///   0040f4ba  중량 넘침   0x00545720 「중량 오버입니다.」
    ///   0040f439  소지금 0    0x00545698 · 0x005456B8
    ///   0040f44e  값 모자람   0x005456D0 · 0x005456F0
    /// </code>
    /// 돈 쪽 둘은 <b>부관 있음·없음 두 벌</b>이다(<c>0x00469680</c>).
    /// </remarks>
    /// <summary>이 도시에서 파는 보급품인지 — 탄약만 도시를 가린다.</summary>
    private bool Sold(Supply supply) => supply.Kind != SupplyKind.Ammo || _ammoSold;

    private string WhyNot(int index)
    {
        var supply = Supply.All[index];
        if (!Sold(supply) && _add[index] >= 0) return "-";
        if (Barrels + 1 > _player.Capacity) return "용량 오버입니다.";
        if (Weight + supply.UnitWeight > _player.Tonnage) return "중량 오버입니다.";
        if (Total + supply.PriceAt(_rate) > _player.Gold)
            return _player.Gold == 0
                ? _mate ? "제독, 안됐지만 빈털터리입니다!" : "소지금이 없습니다"
                : _mate ? "제독, 금화가 모자랍니다!" : "소지금이 모자랍니다.";
        return "";
    }

    /// <summary>부관이 있는가 — 막는 말이 갈린다.</summary>
    private readonly bool _mate;

    private bool CanAdd(int index) => WhyNot(index).Length == 0;

    private void Bump(int index, int by)
    {
        int step = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? FastStep : Step;
        for (int i = 0; i < step; i++)
        {
            // <b>왜 못 싣는지 말해 준다</b> — 게임도 누를 때마다 낸다. 한 번 눌러 여러 통이
            // 올라가는 자리(Shift)에서는 첫 걸음에서 막혔을 때만 낸다.
            if (by > 0 && WhyNot(index) is { Length: > 0 } why)
            {
                if (i == 0 && why != "-") GameDialog.Show(Owner ?? this, why);
                break;
            }
            if (by > 0 && !CanAdd(index)) break;
            // 내리면 0 에서 멈추지 않고 실어 둔 것까지 덜어 낸다 — 현재량 밑으로는 못 간다.
            if (by < 0 && _add[index] <= -_player.SupplyOf(Supply.All[index].Kind)) break;
            _add[index] += by > 0 ? 1 : -1;
        }
        Paint();
    }

    /// <summary>
    /// 게임의 "최대" — <b>식량과 물을 같은 통 수로</b> 실을 수 있는 데까지 맞춘다.
    /// </summary>
    /// <remarks>
    /// 게임의 <c>0x0040F670</c> → <c>0x0040ED20</c> 다. 자재·탄약은 지금 실린 그대로 두고
    /// (보충량 0), 식량과 물을 <b>한 쌍씩</b> 센다.
    /// <code>
    ///   40ed4b  칸 = (용량 - 자재 - 탄약) / 2
    ///   40ed5c  무게 = (중량 한도 - 자재·탄약 무게) / (식량 무게 + 물 무게)
    ///   40ed71  n = max(0, min(칸, 무게))            ; 식량 = 물 = n (싣는 총량)
    ///   40ede5  값이 소지금을 넘으면:
    ///   40ee23    적은 쪽을 먼저 많은 쪽까지 올리고, 남은 돈을 한 쌍 값으로 나눠 둘 다 올린다
    ///   40ef49    그러고도 남는 돈은 식량에 얹는다
    /// </code>
    /// 총량을 맞추는 것이라 지금 실린 것이 n 보다 많으면 보충량이 음수(덜어 냄)가 된다.
    /// </remarks>
    private void Fill()
    {
        var (foodTo, waterTo) = FillTargets();
        int haveFood = _player.SupplyOf(SupplyKind.Food);
        int haveWater = _player.SupplyOf(SupplyKind.Water);

        // 「최대」 단추는 말이 없다 — 더 못 실으면 그냥 그대로다. 여유가 없다는 말은 창을 열 때만 한다(Show).
        if (foodTo <= haveFood && waterTo <= haveWater) return;

        _add[(int)SupplyKind.Food] = foodTo - haveFood;
        _add[(int)SupplyKind.Water] = waterTo - haveWater;
        _add[(int)SupplyKind.Material] = 0;
        _add[(int)SupplyKind.Ammo] = 0;
        Paint();
    }

    /// <summary>「최대」가 맞출 식량·물 총량(<c>0x0040ED20</c>). 일지정의 윗한도도 이것으로 센다.</summary>
    private (int Food, int Water) FillTargets()
    {
        var food = Supply.Of(SupplyKind.Food);
        var water = Supply.Of(SupplyKind.Water);
        var material = Supply.Of(SupplyKind.Material);
        var ammo = Supply.Of(SupplyKind.Ammo);

        int haveFood = _player.SupplyOf(SupplyKind.Food);
        int haveWater = _player.SupplyOf(SupplyKind.Water);
        int haveMaterial = _player.SupplyOf(SupplyKind.Material);
        int haveAmmo = _player.SupplyOf(SupplyKind.Ammo);

        int room = (_player.Capacity - _player.CargoCount - haveMaterial - haveAmmo) / 2;
        int free = _player.Tonnage - _player.GunWeight - _player.CargoWeight
                   - haveMaterial * material.UnitWeight - haveAmmo * ammo.UnitWeight;
        int byWeight = free / (food.UnitWeight + water.UnitWeight);
        int pair = Math.Max(0, Math.Min(room, byWeight));

        int foodPrice = food.PriceAt(_rate), waterPrice = water.PriceAt(_rate);
        int foodTo = pair, waterTo = pair;
        int gold = _player.Gold;
        int cost = Math.Max(0, pair - haveFood) * foodPrice + Math.Max(0, pair - haveWater) * waterPrice;

        if (cost > gold)
        {
            int gap = Math.Abs(haveFood - haveWater);
            int foodMore = 0, waterMore = 0;
            if (gap > 0 && haveFood < haveWater)
            {
                if (foodPrice * gap > gold) foodMore = gold / foodPrice;
                else
                {
                    int each = (gold - foodPrice * gap) / (foodPrice + waterPrice);
                    waterMore = each;
                    foodMore = each + gap;
                }
            }
            else if (gap > 0)
            {
                if (waterPrice * gap > gold) waterMore = gold / waterPrice;
                else
                {
                    int each = (gold - waterPrice * gap) / (foodPrice + waterPrice);
                    foodMore = each;
                    waterMore = each + gap;
                }
            }
            else
            {
                foodMore = waterMore = gold / (foodPrice + waterPrice);
            }

            int left = gold - foodMore * foodPrice - waterMore * waterPrice;
            if (left > 0) foodMore += left / foodPrice;

            foodTo = haveFood + foodMore;
            waterTo = haveWater + waterMore;
        }
        return (foodTo, waterTo);
    }

    /// <summary>
    /// 「일지정」 — 며칠분인지 적게 한다(<c>0x0040F6E0</c>).
    /// </summary>
    /// <remarks>
    /// 윗한도는 「최대」로 맞출 식량·물이 버티는 날수다(<c>0x0040EFA0</c> → <c>0x00494010</c>).
    /// 선원이 없거나 한도가 0 이면 창도 안 뜬다.
    /// <code>
    ///   0x00454AA0("항해일수 보급", "항해일수", "일", 한도, "최대일수", 한도, "승원수", 선원, "명")
    /// </code>
    /// </remarks>
    private void AskDays()
    {
        if (_player.Crew <= 0) return;
        var (food, water) = FillTargets();
        int most = Supply.DaysLeft(food, water, _player.Crew);
        if (most <= 0) return;

        int days = CountDialog.Ask(this, "항해일수 보급", "항해일수", "일", most, 1, false,
                                   new CountDialog.Gauge("최대일수", most),
                                   new CountDialog.Gauge("승원수", _player.Crew, "명"));
        if (days > 0) FillDays(days);
    }

    /// <summary>「전회분」 — 지난번 결정한 총량으로(<c>0x0040EC60</c>).</summary>
    private void Last()
    {
        for (int i = 0; i < Supply.Count; i++)
            _add[i] = _player.LastSupply[i] - _player.SupplyOf(Supply.All[i].Kind);
        if (!_ammoSold) _add[(int)SupplyKind.Ammo] = 0;
        Paint();
    }

    /// <summary>
    /// 열흘 갈 만큼 채운다. <b>선원수만큼의 통</b>이다 — 한 사람이 하루에 한 단위를 쓰고
    /// 한 통이 열 단위이므로(<see cref="Supply.BarrelsForDays"/>) 열흘이면 딱 선원수다.
    /// </summary>
    private void TenDays() => FillDays(10);

    /// <remarks>
    /// 게임의 <c>0x0040F600(날수)</c> 다 — <b>식량과 물을 똑같이</b> 그 날수치 총량으로 맞춘다.
    /// <code>
    ///   40f61d  통 = (날수 * 선원 + 9) / 10
    ///   40f62c  식량 총량 = 통 · 물 총량 = 통
    ///   40f63f  자재·탄약은 지금 실린 그대로(보충량 0)
    /// </code>
    /// 용량·소지금은 여기서 안 본다(결정할 때 본다). 지금 실린 것이 더 많으면 보충량이 음수가 된다.
    /// </remarks>
    private void FillDays(int days)
    {
        int want = Supply.BarrelsForDays(days, _player.Crew);
        for (int i = 0; i < Supply.Count; i++)
            _add[i] = Supply.All[i].IsDaily ? want - _player.SupplyOf(Supply.All[i].Kind) : 0;
        Paint();
    }

    private void Paint()
    {
        for (int i = 0; i < Supply.Count; i++)
        {
            _signLabels[i].Text = _add[i] < 0 ? "-" : "+";
            _addLabels[i].Text = $"{Math.Abs(_add[i]),5}";
            _costLabels[i].Text = $"{Cost(i)}닢";
        }
        _capacity.Text = $"{Barrels}/{_player.Capacity}";
        _weight.Text = $"{Weight}/{_player.Tonnage}";
        _gold.Text = $"{_player.Gold}닢";
        _total.Text = $"{Total}닢";
        _decide.On = _add.Any(a => a != 0);
    }

    /// <summary>산 것을 싣고 값을 치른다. 덜어 낸 것은 내린다(값은 안 돌려준다).</summary>
    private void Decide()
    {
        int total = Total;
        if (_add.All(a => a == 0) || total > _player.Gold) return;

        // 맞춘 총량을 「전회분」으로 적어 둔다(0x0040F541 → 0x0040ECA0).
        _player.SetLastSupply([.. Enumerable.Range(0, Supply.Count)
                                            .Select(i => _player.SupplyOf(Supply.All[i].Kind) + _add[i])]);
        for (int i = 0; i < Supply.Count; i++)
            if (_add[i] != 0) _player.AddSupply(Supply.All[i].Kind, _add[i]);
        _player.SetGold(_player.Gold - total);

        // 알림 없이 그냥 닫는다 — 실은 것은 창이 닫히며 상단 띠에 그대로 비친다.
        Close();
    }

    /// <summary>보급 화면을 연다. 배가 없으면 실을 데가 없다.</summary>
    /// <param name="ammoSold">그 도시가 탄약을 파는지 — 도시 형편 비트 8.</param>
    /// <remarks>
    /// 여는 차례(<c>0x0040F38B</c>): 먼저 <b>전회분</b>을 채워 두고(<c>0x0040EC60</c>), 그것이 남은 중량·용량을
    /// 넘으면 「최대」로 다시 맞춘다(<c>0x0040F3C9</c>). 그러고도 더 실을 여유가 전혀 없으면 부관이(없으면
    /// 알림으로) 「이 이상 실을 여유가 없습니다.」(<c>0x00545678</c>) 하고 창이 곧 닫힌다(<c>0x0040F3F5</c>).
    /// </remarks>
    public static void Show(Window owner, Player player, int rate = 100, bool ammoSold = true,
                            uint[]? mateFace = null)
    {
        if (player.Ships.Count == 0)
        {
            GameDialog.Show(owner, "실을 배가 없지 않은가.");
            return;
        }
        var dialog = new SupplyDialog(player, rate, ammoSold) { Owner = owner };
        dialog.Last();
        if (dialog.Weight > player.Tonnage || dialog.Barrels > player.Capacity) dialog.Fill();

        var (foodTo, waterTo) = dialog.FillTargets();
        if (foodTo <= player.SupplyOf(SupplyKind.Food) && waterTo <= player.SupplyOf(SupplyKind.Water))
        {
            if (mateFace != null) ConfirmDialog.Tell(owner, "이 이상 실을 여유가 없습니다.", face: mateFace);
            else GameDialog.Show(owner, "이 이상 실을 여유가 없습니다.");
            return;
        }
        dialog.ShowDialog();
    }
}
