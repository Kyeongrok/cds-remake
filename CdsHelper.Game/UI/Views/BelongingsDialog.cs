using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using CdsHelper.Game.Local.Helpers;
using CdsHelper.Support.Local.Models;

namespace CdsHelper.Game.UI.Views;

/// <summary>
/// 소지품 정보 창 — 왼쪽에 소지품, 오른쪽에 발견물을 나란히 늘어놓는다.
/// </summary>
/// <remarks>
/// 도시 커맨드 창의 "소지품 정보" 로 연다. 게임 화면을 그대로 옮겼다 — 양피지 바탕에
/// 제목 띠 둘이 나란히 서고, 밑에 결정·중단이 있다.
/// <code>
///   ┌ 소지품일람 ─┬ 발견물일람 ─┐
///   │ 육분의      │            │
///   │ 사해사본     │            │
///   │ …          │            │
///   └─────────┴──────────┘
///          [결정]  [중단]
/// </code>
/// 발견물 쪽은 지금까지 찾은 것이 <b>찾은 차례대로</b> 놓인다. 고를 수는 없다 — 게임도
/// 결정이 소지품 줄에만 걸린다.
///
/// 줄을 고르고 결정을 누르면 그 아이템 창이 뜬다(<see cref="ItemInfoDialog"/>) —
/// 시장에서 고른 뒤에 뜨는 것과 같은 창이다.
/// </remarks>
public sealed class BelongingsDialog : GameWindow
{
    /// <summary>고른 줄에 씌우는 남색. 시장 목록과 같은 색이다.</summary>
    private static readonly Brush Picked = Freeze(Color.FromRgb(0x5C, 0x6F, 0x93));

    /// <summary>글꼴을 못 읽었을 때 물러설 글씨색.</summary>
    private static readonly Brush Ink = Freeze(Color.FromRgb(0x20, 0x18, 0x10));

    private static SolidColorBrush Freeze(Color c)
    {
        var b = new SolidColorBrush(c);
        b.Freeze();
        return b;
    }

    private readonly ItemTable? _items;
    private readonly ItemDescriptions? _descriptions;
    private readonly ItemArt? _art;

    private readonly List<(int ItemId, Border Row, string Name)> _rows = [];

    /// <summary>지닌 것 — 「장비중」을 가리는 데 쓴다(갈래마다 가장 센 것 하나).</summary>
    private readonly List<int> _bag = [];
    private readonly GameButton _decide;
    private int _at = -1;

    /// <summary>고문서의 힌트를 읽힐 판. 없으면 아이템 창만 뜬다.</summary>
    private readonly Engine.Game? _game;

    private BelongingsDialog(Player player, ItemTable? items,
                             ItemDescriptions? descriptions, ItemArt? art,
                             IReadOnlyList<string> discoveries, Engine.Game? game)
    {
        _game = game;
        _items = items;
        _descriptions = descriptions;
        _art = art;

        Title = "소지품 정보";
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;

        // 창은 <b>줄 수를 따라 자란다</b> — 원본도 넉 줄 남짓한 납작한 창으로 열리고
        // 지닌 것이 늘면 아래로 길어진다. 가로는 원본이 화면의 2/3 쯤이다.
        int virtualCount = game != null ? Engine.GameInfo.VirtualItems(game).Count : 0;
        int lines = Math.Clamp(Math.Max(player.Items.Count + virtualCount, discoveries.Count),
                               MinRows, MaxRows);
        Width = BoardWidth;
        Height = ChromeHeight + lines * RowHeight;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        Background = GameUi.Back;

        // 제목 띠 둘은 맨 위에 나란히, 그 밑이 통째로 양피지 칸이다.
        // 게임도 그렇게 나뉘어 있다 — 창 바탕은 갈색이고 줄이 놓이는 데만 밝다.
        var bands = new Grid();
        bands.ColumnDefinitions.Add(new ColumnDefinition());
        bands.ColumnDefinitions.Add(new ColumnDefinition());

        var leftBand = Band("소지품일람");
        Grid.SetColumn(leftBand, 0);
        bands.Children.Add(leftBand);

        var rightBand = Band("발견물일람");
        Grid.SetColumn(rightBand, 1);
        bands.Children.Add(rightBand);

        var columns = new Grid();
        columns.ColumnDefinitions.Add(new ColumnDefinition());
        columns.ColumnDefinitions.Add(new ColumnDefinition());

        var leftList = ListColumn();
        Grid.SetColumn(leftList.Host, 0);
        columns.Children.Add(leftList.Host);

        var rightList = ListColumn();
        Grid.SetColumn(rightList.Host, 1);
        columns.Children.Add(rightList.Host);
        Discoveries = rightList.Items;

        // 발견물 쪽은 고를 수 없다 — 게임도 결정이 소지품 줄에만 걸린다.
        foreach (var name in discoveries)
            Discoveries.Children.Add(new Border
            {
                Padding = new Thickness(0, 2, 0, 2),
                Child = Label(name, picked: false),
            });

        _bag.AddRange(player.Items);
        // 실제 16칸 뒤에 아직 안 알린 발견물의 아이템이 붙는다(0x0044CBB3). 장비 셈은 실제 칸만 본다.
        var shown = player.Items.Concat(game != null ? Engine.GameInfo.VirtualItems(game) : []).ToList();
        foreach (int id in shown)
        {
            var row = Row(id);
            _rows.Add(row);
            leftList.Items.Children.Add(row.Row);
        }
        if (_rows.Count == 0)
            leftList.Items.Children.Add(new TextBlock
            {
                Text = "  지닌 것이 없다.",
                Foreground = Ink,
                FontWeight = FontWeights.Bold,
                FontSize = 15,
                Margin = new Thickness(6, 8, 6, 6),
            });

        _decide = new GameButton("결정", Decide, width: 130) { On = false };

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 4, 0, 12),
        };
        buttons.Children.Add(_decide);
        buttons.Children.Add(new GameButton("중단", Close, width: 130));

        var root = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(bands, Dock.Top);
        root.Children.Add(bands);
        DockPanel.SetDock(buttons, Dock.Bottom);
        root.Children.Add(buttons);
        root.Children.Add(columns);

        Content = new Border
        {
            Background = GameUi.Back,
            BorderBrush = GameUi.Edge,
            BorderThickness = new Thickness(2),
            Margin = new Thickness(4),
            Child = root,
        };

        GameUi.EnableDrag(this, bands);
        KeyDown += OnKey;
    }

    /// <summary>창 폭 — 원본은 화면의 2/3 쯤(640 점 자로 408)이다.</summary>
    private const double BoardWidth = 630;

    /// <summary>줄 하나가 차지하는 키. <see cref="Row"/> 의 테(위아래 2)와 글자 키를 더한 값이다.</summary>
    private const double RowHeight = 24;

    /// <summary>줄 말고 드는 키 — 테 · 제목 띠 · 단추 줄 · 양피지 칸의 안쪽 여백.</summary>
    private const double ChromeHeight = 130;

    /// <summary>처음 여는 줄 수와, 이보다 길어지지 않는 줄 수.</summary>
    private const int MinRows = 4, MaxRows = 22;

    /// <summary>발견물 칸. 지금까지 발견한 것이 찾은 차례대로 놓인다.</summary>
    public StackPanel Discoveries { get; }

    /// <summary>제목 띠 하나. 원본 조각을 못 읽었으면 띠 대신 글자만 낸다.</summary>
    private static FrameworkElement Band(string title) =>
        GameUi.TitleFrame(GameUi.Sprites, title) ?? new Border
        {
            Background = GameUi.MenuBack,
            BorderBrush = GameUi.Edge,
            BorderThickness = new Thickness(1),
            Child = new TextBlock
            {
                Text = title,
                Foreground = GameUi.Text,
                FontWeight = FontWeights.Bold,
                FontSize = 15,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 3, 0, 3),
            },
        };

    /// <summary>줄이 쌓이는 양피지 칸.</summary>
    private static (Border Host, StackPanel Items) ListColumn()
    {
        var items = new StackPanel { Margin = new Thickness(6, 4, 4, 4) };
        var host = new Border
        {
            Background = GameUi.PageFill,
            Child = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Content = items,
            },
        };
        return (host, items);
    }

    /// <summary>줄 하나 — 아이템 이름.</summary>
    private (int ItemId, Border Row, string Name) Row(int itemId)
    {
        string name = _items?.Find(itemId)?.Name ?? $"아이템 {itemId}";
        var row = new Border
        {
            Background = Brushes.Transparent,
            Padding = new Thickness(0, 2, 0, 2),
            Cursor = Cursors.Hand,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Child = Label(name, picked: false),
        };
        row.MouseLeftButtonDown += (_, e) => e.Handled = true;
        row.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            Pick(_rows.FindIndex(r => ReferenceEquals(r.Row, row)));
        };
        return (itemId, row, name);
    }

    /// <summary>
    /// 줄 글씨. 게임 비트맵 글꼴로 찍는다 — 윈도 글꼴은 같은 자리에서 더 크고 결이 다르다.
    /// </summary>
    /// <remarks>
    /// 고른 줄은 남색 위라 글씨를 흰빛으로 뒤집는다. 색이 생길 때 정해지므로 고를 때마다
    /// 새로 짓는다 — 한 번 고를 때 바뀌는 줄은 둘(놓은 줄과 잡은 줄)뿐이라 값싸다.
    /// </remarks>
    private static FrameworkElement Label(string name, bool picked)
    {
        var label = new GameUi.GameLabel(picked ? GameFont.WhiteColor : GameFont.ButtonColor)
        {
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(6, 0, 6, 0),
            Text = name,
        };
        // 글꼴을 못 읽었으면 GameLabel 이 윈도 글꼴로 물러선다. 그때 색을 맞춰 준다.
        label.FallbackBrush = picked ? Brushes.White : Ink;
        return label;
    }

    /// <summary>그 줄을 고른다.</summary>
    private void Pick(int index)
    {
        if (index < 0 || index >= _rows.Count) return;
        _at = index;
        for (int i = 0; i < _rows.Count; i++)
        {
            bool on = i == index;
            _rows[i].Row.Background = on ? Picked : Brushes.Transparent;
            _rows[i].Row.Child = Label(_rows[i].Name, on);
        }
        _decide.On = true;      // 게임도 아무것도 안 고른 동안은 이 단추가 흐리다
    }

    private void OnKey(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Escape:
                Close();
                break;
            case Key.Up:
                Pick(_at <= 0 ? _rows.Count - 1 : _at - 1);
                e.Handled = true;
                break;
            case Key.Down:
                Pick(_at < 0 || _at >= _rows.Count - 1 ? 0 : _at + 1);
                e.Handled = true;
                break;
            case Key.Enter or Key.Space when _at >= 0:
                Decide();
                e.Handled = true;
                break;
        }
    }

    /// <summary>고른 것을 들여다본다 — 시장에서 고른 뒤에 뜨는 것과 같은 창이다.</summary>
    private void Decide()
    {
        if (_at < 0 || _at >= _rows.Count) return;
        if (_items?.Find(_rows[_at].ItemId) is not { } item) return;

        string description = _descriptions?.Of(item.Id) ?? "";
        bool equipped = ItemInfoDialog.IsEquipped(item, _bag, _items);

        // 힌트가 걸린 고문서면 먼저 읽어 본다(0x0046E8D2). 못 읽으면 창을 띄운 채 말만 하고 닫는다.
        if (_game is { } game)
        {
            switch (Engine.Discovery.ItemHintReading.Check(game, item, out int hint, out string word))
            {
                case Engine.Discovery.ItemHintReading.Outcome.Failed:
                    ItemInfoDialog.ShowWhile(this, item, description, _art, equipped,
                                             shown => NoticeDialog.Show(shown, word));
                    return;
                case Engine.Discovery.ItemHintReading.Outcome.Read:
                    NoticeDialog.Show(this, Engine.Discovery.ItemHintReading.ReadWord);
                    if (game.Hints?.Find(hint) is { } row)
                        HintDetailDialog.Show(this, row, game.Hints.CategoryOf(row.Category),
                                              game.Player.Fame, game.MateSpeaks,
                                              game.Player.Contract?.Hint == row.Id);
                    game.Player.GainHint(hint);
                    break;
            }
        }

        ItemInfoDialog.Show(this, item, description, _art, equipped);
    }

    /// <summary>소지품 정보 창을 연다.</summary>
    /// <param name="discoveries">발견물 칸에 늘어놓을 이름. 찾은 차례대로 준다.</param>
    public static void Show(Window owner, Player player, ItemTable? items,
                            ItemDescriptions? descriptions, ItemArt? art,
                            IReadOnlyList<string> discoveries, Engine.Game? game = null)
    {
        // <b>두 칸이 다 비면 창을 아예 안 연다</b>(0x0044CD06) — 「소지품이 없습니다」
        // 알림 한 장으로 끝난다(0x0055AD90, 제목 없음).
        if (player.Items.Count == 0 && discoveries.Count == 0)
        {
            NoticeDialog.Show(owner, "소지품이 없습니다");
            return;
        }

        new BelongingsDialog(player, items, descriptions, art, discoveries, game) { Owner = owner }
            .ShowDialog();
    }
}
