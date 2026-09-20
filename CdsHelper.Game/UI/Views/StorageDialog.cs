using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using CdsHelper.Game.Local.Helpers;
using CdsHelper.Support.Local.Models;

namespace CdsHelper.Game.UI.Views;

/// <summary>
/// 자택 보관 — 게임의 <b>「아이템 교환」</b> 창. 지닌 것과 맡겨 둔 것을 한 칸씩 맞바꾼다.
/// </summary>
/// <remarks>
/// 자택 명령 창의 "보관" 으로 연다. 게임의 <c>0x004608B0</c> 을 옮겼다.
/// <code>
///   소지 = 16칸을 읽는다 (0x004B09D0 → 0x0047CDD0, 플레이어 +0x118)
///   보관 = 99칸을 읽는다 (0x004AB750 → 0x0047CE70, 플레이어 +0x158)
///   0x004B1D30(16, 소지, "소지 아이템", 99, 보관, "보관 아이템")   ← 주고받는 창
///   끝나면 두 칸을 그대로 되쓴다 (0x0047CE50 · 0x0047CDB0)
/// </code>
///
/// <b>옮기는 것이 아니라 맞바꾼다.</b> 왼쪽에서 한 칸, 오른쪽에서 한 칸을 고르고 "교환" 을
/// 누르면 두 칸의 알맹이가 자리를 바꾼다. 빈 칸도 고를 수 있어서, 빈 칸과 바꾸면 그것이
/// 곧 맡기기(또는 되찾기)다. 그래서 창 이름이 "보관" 이 아니라 "아이템 교환" 이다.
///
/// <b>칸 자리는 창이 닫힐 때까지 그대로 남는다.</b> 게임이 열여섯·아흔아홉 자리 배열을
/// 들고 빈 자리를 -1 로 두기 때문이다 — 가운데 것을 맡겨도 아랫것이 위로 올라오지 않는다.
/// 우리 <see cref="Player"/> 는 빈 자리를 안 들고 다니므로, 이 창이 여는 동안만 제 배열을
/// 쥐고 있다가 닫힐 때 추려 되쓴다(<see cref="Player.ReplaceBelongings"/>).
///
/// 손이 간 쪽 목록만 고른 줄을 남색으로 뒤집고, 안 간 쪽은 검은 테로만 알린다 —
/// 게임 갈무리가 그렇다(<see cref="GameList.Focused"/>).
/// </remarks>
public sealed class StorageDialog : GameWindow
{
    /// <summary>빈 칸을 나타내는 값. 게임도 -1 로 둔다.</summary>
    private const int Empty = -1;

    /// <summary>목록 한 벌의 폭과 높이. 게임 갈무리를 재어 맞췄다(아홉 줄쯤 든다).</summary>
    private const double ListWidth = 240, ListHeight = 200;

    /// <summary>줄 속 칸 — 이름 하나다. 게임도 오른쪽맞춤이다.</summary>
    private static readonly GameListColumn[] Columns =
    [
        new(GameListDock.Fill, new Thickness(8, 0, 10, 0), Align: HorizontalAlignment.Right),
    ];

    private readonly Player _player;
    private readonly ItemTable? _items;

    /// <summary>칸 배열 — 빈 자리는 <see cref="Empty"/> 다.</summary>
    private readonly int[] _bag = new int[Player.MaxItems];
    private readonly int[] _box = new int[Player.MaxStored];

    private readonly GameList _bagList, _boxList;

    /// <summary>손이 마지막으로 간 쪽. 남색으로 뒤집히는 목록이다.</summary>
    private bool _onBag = true;

    private StorageDialog(Player player, ItemTable? items)
    {
        _player = player;
        _items = items;

        Title = "아이템 교환";
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.WidthAndHeight;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        Background = GameUi.Back;

        Load(_bag, player.Items);
        Load(_box, player.Stored);

        _bagList = NewList(_bag);
        _boxList = NewList(_box);
        _bagList.Select(0);
        _boxList.Select(0);
        _bagList.SelectionChanged += () => Hand(onBag: true);
        _boxList.SelectionChanged += () => Hand(onBag: false);
        Hand(onBag: true);

        var columns = new Grid();
        columns.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        columns.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var left = Column("소지 아이템", _bagList);
        var right = Column("보관 아이템", _boxList);
        right.Margin = new Thickness(8, 0, 0, 0);
        Grid.SetColumn(left, 0);
        Grid.SetColumn(right, 1);
        columns.Children.Add(left);
        columns.Children.Add(right);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 6, 0, 2),
        };
        buttons.Children.Add(new GameButton("교환", Swap, width: 110));
        buttons.Children.Add(new GameButton("종료", Close, width: 110));

        var title = GameUi.TitleBar("아이템 교환", Close);
        GameUi.EnableDrag(this, title);

        var stack = new StackPanel();
        stack.Children.Add(title);
        stack.Children.Add(columns);
        stack.Children.Add(buttons);

        Content = GameUi.DialogEdge(stack);

        KeyDown += OnKey;
        // 창을 닫을 때 한 번에 되쓴다 — 게임도 창이 끝나야 두 칸을 되쓴다.
        Closed += (_, _) => _player.ReplaceBelongings(_bag, _box);
    }

    /// <summary>지닌 차례대로 앞 칸에 담고 나머지는 빈 칸으로 둔다.</summary>
    private static void Load(int[] slots, IReadOnlyList<int> from)
    {
        for (int i = 0; i < slots.Length; i++)
            slots[i] = i < from.Count ? from[i] : Empty;
    }

    private GameList NewList(int[] slots) =>
        new(Columns, i => [ItemName(slots[i])], slots.Length, maxHeight: ListHeight)
        {
            Width = ListWidth,
            Margin = new Thickness(0),
            BorderBrush = GameUi.Edge,
        };

    /// <summary>빈 칸은 빈 줄이다 — 게임도 이름 자리만 비워 둔다.</summary>
    private string ItemName(int itemId) =>
        itemId < 0 ? "" : _items?.Find(itemId)?.Name ?? $"아이템 {itemId}";

    /// <summary>손이 간 쪽을 바꾼다. 남색으로 뒤집히는 목록은 늘 하나뿐이다.</summary>
    private void Hand(bool onBag)
    {
        _onBag = onBag;
        _bagList.Focused = onBag;
        _boxList.Focused = !onBag;
    }

    /// <summary>고른 두 칸을 맞바꾼다. 빈 칸끼리면 아무 일도 없다.</summary>
    private void Swap()
    {
        int a = _bagList.Selected, b = _boxList.Selected;
        if (a < 0 || a >= _bag.Length || b < 0 || b >= _box.Length) return;
        if (_bag[a] == Empty && _box[b] == Empty) return;

        (_bag[a], _box[b]) = (_box[b], _bag[a]);

        // 글자만 다시 찍으면 된다 — 줄 수는 그대로다.
        _bagList.Refresh();
        _boxList.Refresh();
    }

    private void OnKey(object sender, KeyEventArgs e)
    {
        // 위아래는 손이 간 쪽 목록이 받고, 좌우로 손을 옮긴다.
        if ((_onBag ? _bagList : _boxList).HandleKey(e.Key)) { e.Handled = true; return; }

        switch (e.Key)
        {
            case Key.Escape:
                Close();
                break;
            case Key.Left or Key.Right:
                Hand(e.Key == Key.Left);
                e.Handled = true;
                break;
            case Key.Enter:
                Swap();
                e.Handled = true;
                break;
        }
    }

    /// <summary>제목 띠를 얹은 목록 한 벌.</summary>
    private static FrameworkElement Column(string title, GameList list)
    {
        var stack = new StackPanel { Width = ListWidth };

        // 원본 조각을 못 읽었으면 띠 대신 글자만 낸다.
        if (GameUi.TitleFrame(GameUi.Sprites, title) is { } band) stack.Children.Add(band);
        else stack.Children.Add(new TextBlock
        {
            Text = title,
            Foreground = GameUi.Text,
            FontWeight = FontWeights.Bold,
            FontSize = 15,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 3, 0, 3),
        });

        stack.Children.Add(list);
        return stack;
    }

    /// <summary>아이템 교환 창을 연다.</summary>
    public static void Show(Window owner, Player player, ItemTable? items) =>
        new StorageDialog(player, items) { Owner = owner }.ShowDialog();
}
