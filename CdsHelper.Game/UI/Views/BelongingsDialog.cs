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

    /// <summary>주인공 — 편리한 인벤토리가 보관 · 판매할 때 쓴다.</summary>
    private readonly Player _player;

    /// <summary>소지품 줄이 놓이는 칸.</summary>
    private readonly StackPanel _list;

    /// <summary>편리한 인벤토리 단추 둘. 모드를 껐으면 null.</summary>
    private readonly GameButton? _storeButton, _sellButton;

    /// <summary>고문서의 힌트를 읽힐 판. 없으면 아이템 창만 뜬다.</summary>
    private readonly Engine.Game? _game;

    private BelongingsDialog(Player player, ItemTable? items,
                             ItemDescriptions? descriptions, ItemArt? art,
                             IReadOnlyList<string> discoveries, Engine.Game? game)
    {
        _game = game;
        _player = player;
        _items = items;
        _descriptions = descriptions;
        _art = art;

        Title = "소지품 정보";
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;

        // 창은 <b>줄 수를 따라 자란다</b> — 원본도 넉 줄 남짓한 납작한 창으로 열리고
        // 지닌 것이 늘면 아래로 길어진다. 가로는 원본이 화면의 2/3 쯤이다.
        int virtualCount = game != null ? Engine.GameInfo.VirtualItems(game).Count : 0;
        // 「아이템 창 개선」을 켜면 소지품 줄이 그림만큼 높아진다 — 그만큼 덜 늘린다.
        bool rich = Rich;
        int lines = rich
            ? Math.Clamp(player.Items.Count + virtualCount, MinRows, MaxRichRows)
            : Math.Clamp(Math.Max(player.Items.Count + virtualCount, discoveries.Count), MinRows, MaxRows);
        Width = BoardWidth;
        Height = ChromeHeight + lines * (rich ? RichRowHeight : RowHeight);
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        Background = GameUi.Back;

        // 제목 띠 둘은 맨 위에 나란히, 그 밑이 통째로 양피지 칸이다.
        // 게임도 그렇게 나뉘어 있다 — 창 바탕은 갈색이고 줄이 놓이는 데만 밝다.
        var bands = new Grid();
        bands.ColumnDefinitions.Add(new ColumnDefinition());
        bands.ColumnDefinitions.Add(new ColumnDefinition());

        _bands = bands;
        SyncBagBand();

        var rightBand = Band("발견물일람");
        Grid.SetColumn(rightBand, 1);
        bands.Children.Add(rightBand);

        var columns = new Grid();
        columns.ColumnDefinitions.Add(new ColumnDefinition());
        columns.ColumnDefinitions.Add(new ColumnDefinition());

        var leftList = ListColumn();
        _list = leftList.Items;
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
        // 지닌 것이 없고 발견물만 있으면 왼쪽 칸은 빈 채로 둔다 — 원본은 소지품 줄만 찍고 따로 알리지 않는다.

        _decide = new GameButton("결정", Decide, width: 130) { On = false };

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 4, 0, 12),
        };
        buttons.Children.Add(_decide);
        // 편리한 인벤토리 — 결정과 중단 사이에 「보관함」 · 「판매」.
        if (Local.Settings.GameSettings.HandyInventory)
        {
            _storeButton = new GameButton("보관함", StoreHome, width: 110) { On = false };
            _sellButton = new GameButton("판매", SellNow, width: 110) { On = false };
            buttons.Children.Add(_storeButton);
            buttons.Children.Add(_sellButton);
        }
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

    /// <summary>제목 띠 둘이 놓이는 칸과, 그 왼쪽에 걸린 소지품 띠.</summary>
    private readonly Grid _bands;
    private FrameworkElement? _bagBand;

    /// <summary>
    /// 소지품 띠를 지금 값으로 다시 건다 — 정보 제공 등급이 「일반」부터면 <b>「소지품일람 3/16」</b>처럼
    /// 지닌 수와 칸 수(<see cref="Player.ItemLimit"/>)를 붙인다. 원본 띠는 이름뿐이라 몇 칸 남았는지 알 수 없다.
    /// </summary>
    private void SyncBagBand()
    {
        if (_bagBand != null) _bands.Children.Remove(_bagBand);
        bool counted = Local.Settings.GameSettings.Shows(Local.Settings.GameSettings.InfoNormal);
        _bagBand = Band(counted ? $"소지품일람 {_player.Items.Count}/{_player.ItemLimit}" : "소지품일람");
        Grid.SetColumn(_bagBand, 0);
        _bands.Children.Add(_bagBand);
    }

    /// <summary>창 폭 — 원본은 화면의 2/3 쯤(640 점 자로 408)이다.</summary>
    private const double BoardWidth = 630;

    /// <summary>줄 하나가 차지하는 키. <see cref="Row"/> 의 테(위아래 2)와 글자 키를 더한 값이다.</summary>
    private const double RowHeight = 24;

    /// <summary>줄 말고 드는 키 — 테 · 제목 띠 · 단추 줄 · 양피지 칸의 안쪽 여백.</summary>
    private const double ChromeHeight = 130;

    /// <summary>처음 여는 줄 수와, 이보다 길어지지 않는 줄 수.</summary>
    private const int MinRows = 4, MaxRows = 22;

    /// <summary>「아이템 창 개선」 — 줄마다 그림을 다는지(<see cref="GameSettings.ItemListPictures"/>).</summary>
    private static bool Rich => Local.Settings.GameSettings.ItemListPictures;

    /// <summary>그림을 다는 줄의 키와 그 그림 크기. 그림(120x120)을 줄여 건다 — 스폰서 일람 얼굴 높이쯤이다.</summary>
    private const double RichRowHeight = 50, ThumbSize = 44;

    /// <summary>그림을 다는 줄로 이보다 길어지지 않는다 — 넘치면 굴린다.</summary>
    private const int MaxRichRows = 10;

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
            // 굴림대는 게임 것(MISC.CDS 파트 3 화살표)이다 — 윈도 굴림대는 모양이 게임과 너무 다르다.
            // 칸 높이는 창이 정하므로 높이 한도는 두지 않는다.
            Child = GameUi.Scroller(items, double.PositiveInfinity),
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
            Child = Line(itemId, name, picked: false),
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
    /// 소지품 줄 속. 여느 때는 이름 한 줄이고, 「아이템 창 개선」을 켜면 스폰서 일람처럼 왼쪽에 그림,
    /// 이름 밑에 갈래 · 효과(무기 · 방어구) · 「장비중」이다.
    /// </summary>
    private FrameworkElement Line(int itemId, string name, bool picked)
    {
        if (!Rich) return Label(name, picked);

        var found = _items?.Find(itemId);
        var thumb = new Border
        {
            Width = ThumbSize,
            Height = ThumbSize,
            Margin = new Thickness(4, 1, 0, 1),
            VerticalAlignment = VerticalAlignment.Center,
        };
        if (found is { HasPic: true } pictured && _art?.TryGetImage(pictured.Pic) is { } art)
        {
            var image = new Image { Source = art, Width = ThumbSize, Height = ThumbSize, Stretch = Stretch.Uniform };
            // 크게 줄여 거는 것이라 곱게 줄인다 — 가장가까운점이면 점이 듬성듬성 빠진다.
            RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);
            thumb.Child = image;
        }

        var words = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        words.Children.Add(Label(name, picked));
        if (found is { } item)
        {
            string note = item.Category is Weapon or Armor ? $"{item.CategoryName}  효과 {item.Effect}" : item.CategoryName;
            if (_items != null && ItemInfoDialog.IsEquipped(item, _bag, _items)) note += "  장비중";
            if (note.Length > 0) words.Children.Add(Label(note, picked));
        }

        return new StackPanel { Orientation = Orientation.Horizontal, Children = { thumb, words } };
    }

    /// <summary>효과를 적는 갈래 — 무기 · 방어구(아이템 창과 같다).</summary>
    private const int Weapon = 3, Armor = 4;

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
            _rows[i].Row.Child = Line(_rows[i].ItemId, _rows[i].Name, on);
        }
        _decide.On = true;      // 게임도 아무것도 안 고른 동안은 이 단추가 흐리다
        // 보관 · 판매는 <b>실제 소지품 칸</b>만 — 뒤에 붙은 「아직 안 알린 발견물」 줄은 지닌 것이 아니다.
        bool real = index < _player.Items.Count;
        if (_storeButton != null) _storeButton.On = real;
        if (_sellButton != null) _sellButton.On = real;
    }

    /// <summary>
    /// 편리한 인벤토리 「보관함」 — 고른 소지품을 자택 보관함으로 곧장 보낸다(<see cref="Player.Store"/>).
    /// </summary>
    private void StoreHome()
    {
        if (_at < 0 || _at >= _player.Items.Count) return;
        string name = _rows[_at].Name;
        if (_player.IsStoreFull)
        {
            NoticeDialog.Show(this, "자택 보관함이 가득 찼습니다");
            return;
        }
        if (!ConfirmDialog.Ask(this, $"{name}{GameUi.Josa(name, "을", "를")} 자택 보관함으로 보내겠습니까?")) return;
        if (!_player.Store(_at)) return;
        RemoveRow(_at);
    }

    /// <summary>
    /// 편리한 인벤토리 「판매」 — 고른 소지품을 그 자리에서 판다. 값은 시장 매각과 같다
    /// (아이템 매각가 x 지금 도시 시세 — 바다 위면 시세 100, <see cref="Engine.Market.MarketRates.SellPrice(int, int)"/>).
    /// </summary>
    private void SellNow()
    {
        if (_at < 0 || _at >= _player.Items.Count || _game == null) return;
        if (_items?.Find(_rows[_at].ItemId) is not { } item) return;
        string name = _rows[_at].Name;

        int price = _game.Rates.SellPrice(item.SellList, _player.CityId);
        if (price <= 0)
        {
            NoticeDialog.Show(this, "값을 쳐 줄 수 없는 물건입니다");
            return;
        }
        if (!ConfirmDialog.Ask(this, $"{name}{GameUi.Josa(name, "을", "를")} 금화 {price:N0}닢에 팔겠습니까?")) return;

        // 그 칸을 정확히 뺀다 — 같은 아이템이 여럿이어도 고른 줄이 빠진다.
        _player.DropAt(_at);
        _player.Earn(price);
        RemoveRow(_at);
    }

    /// <summary>줄 하나를 걷고 나머지를 다시 칠한다(장비중 표시가 바뀔 수 있다).</summary>
    private void RemoveRow(int index)
    {
        _list.Children.Remove(_rows[index].Row);
        _rows.RemoveAt(index);
        _bag.Clear();
        _bag.AddRange(_player.Items);
        SyncBagBand();
        _at = -1;
        for (int i = 0; i < _rows.Count; i++)
        {
            _rows[i].Row.Background = Brushes.Transparent;
            _rows[i].Row.Child = Line(_rows[i].ItemId, _rows[i].Name, false);
        }
        _decide.On = false;
        if (_storeButton != null) _storeButton.On = false;
        if (_sellButton != null) _sellButton.On = false;
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
