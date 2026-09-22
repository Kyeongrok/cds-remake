using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using CdsHelper.Game.Engine.Market;
using CdsHelper.Game.Local.Helpers;
using CdsHelper.Support.Local.Models;

namespace CdsHelper.Game.UI.Views;

/// <summary>
/// 시장 구입 창 — 파는 것을 늘어놓고 하나를 고르게 한다.
/// </summary>
/// <remarks>
/// 게임 차례를 그대로 따른다.
/// <code>
///   1  구입 아이템 선택   줄을 고르면 "결정" 이 살아난다
///   2  아이템 창          그림·설명·효과            (ItemInfoDialog)
///   3  값 알림            "그렇다면 금화 %d닢 필요하네."
///   4  물음              "이 아이템을 구입하겠습니까?"  YES/NO
///   5  결과              돈이 되면 "고맙네!", 모자라면 "가난한 사람에게는 볼일 없네!"
/// </code>
/// 문구는 EXE 에서 그대로 옮겼다(<c>0x00544730</c> 벌). 값이 여럿일 때 "이것들의" 로 갈리는
/// 것까지 있지만 지금은 한 번에 하나만 고를 수 있어 "이" 쪽만 쓴다.
///
/// <b>돈 검사는 YES 를 고른 뒤다.</b> 목록에서도 값 알림에서도 막지 않는다 — 게임이
/// 그렇게 한다(구입 본체 <c>0x004B3AAD</c> 에서 소지금과 값을 견준다). 살 돈이 없는 줄도
/// 고를 수 있고 값도 알려 준다.
/// </remarks>
public sealed class MarketBuyDialog : GameWindow
{
    /// <summary>
    /// 줄 속 칸 — 값 · (갈래) · 이름. 오른쪽 것을 먼저 줘야 바깥에 선다.
    /// </summary>
    /// <remarks>
    /// <b>셋 다 오른쪽맞춤이고 앞의 둘은 폭이 못 박혀 있다.</b> 게임 갈무리를 재어 보면
    /// 이름 끝·갈래 끝·값 끝이 줄마다 <b>같은 자리</b>에 선다 — "(병기)" 처럼 짧은 갈래가
    /// 와도 이름 끝이 밀리지 않는다. 폭을 안 박으면 갈래 길이에 따라 이름이 흔들린다.
    /// </remarks>
    private static readonly GameListColumn[] Columns =
    [
        new(GameListDock.Right, new Thickness(0, 0, 10, 0), 66, HorizontalAlignment.Right),
        new(GameListDock.Right, new Thickness(0), 114, HorizontalAlignment.Right),
        new(GameListDock.Fill, new Thickness(10, 0, 0, 0), Align: HorizontalAlignment.Right),
    ];

    /// <summary>목록 바닥 폭. 게임 갈무리를 재어 맞췄다 — 이보다 넓으면 이름 칸이 휑하다.</summary>
    private const double ListWidth = 356;

    private readonly Player _player;
    private readonly Market _market;
    private readonly ItemDescriptions? _descriptions;
    private readonly ItemArt? _art;
    private readonly int _cityId;

    private readonly ItemTable.Record[] _stock;

    /// <summary>
    /// 줄마다의 발견물 번호 — 모조품이 아니면 -1 이다. <see cref="_stock"/> 과 차례가 같다.
    /// </summary>
    private readonly int[] _asDiscovery;

    private readonly Engine.Discovery.DiscoveryLog? _found;
    private readonly GameList _list;
    private readonly GameButton _decide;

    /// <summary>장사꾼 얼굴(시장 화자). 없으면 얼굴 없이 말한다.</summary>
    private readonly uint[]? _face;

    private MarketBuyDialog(Player player, Market market, int cityId,
                            ItemDescriptions? descriptions, ItemArt? art,
                            Engine.Discovery.DiscoveryLog? found, uint[]? face = null,
                            Engine.Game? game = null, int scale = 1)
    {
        _face = face;
        _game = game;
        _player = player;
        _market = market;
        _cityId = cityId;
        _descriptions = descriptions;
        _art = art;
        _found = found;

        Title = "구입 아이템 선택";
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.WidthAndHeight;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        Background = GameUi.Back;

        // 이 마을이 파는 <b>모조품</b>이 목록 맨 앞에 선다(0x004B38C7 이 그쪽을 먼저 채운다).
        // 이미 찾은 것은 빠진다 — 게임은 깃발 0x44 로 거른다(0x004B0BA5).
        (_stock, _asDiscovery) = Offer(player, market, cityId, found);
        _list = new GameList(Columns, Cells, _stock.Length, "  지금 내놓은 물건이 없다.  ")
        {
            // 게임은 한 번에 여럿을 산다 — 고른 줄이 여럿이면 값도 한꺼번에 부른다.
            Pick = GameListPick.Many,
            Margin = new Thickness(0),
            BorderBrush = GameUi.Edge,
        };

        _decide = new GameButton("결정", Decide, width: 110) { On = false };
        // 게임도 아무것도 안 고른 동안은 이 단추가 흐리다.
        _list.SelectionChanged += () => _decide.On = _list.Chosen.Count > 0;

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 4, 0, 0),
        };
        buttons.Children.Add(_decide);
        buttons.Children.Add(new GameButton("중단", Close, width: 110));

        var title = GameUi.TitleBar("구입 아이템 선택", Close);
        GameUi.EnableDrag(this, title);

        var stack = new StackPanel { MinWidth = ListWidth };
        stack.Children.Add(title);
        stack.Children.Add(_list);
        stack.Children.Add(buttons);

        // 도시 그림과 같은 배로 키운다 — 원본은 한 화면이라 시장 창도 그림과 같은 배율이다.
        var root = GameUi.DialogEdge(stack);
        root.LayoutTransform = new System.Windows.Media.ScaleTransform(scale, scale);
        Content = root;

        KeyDown += OnKey;
    }

    /// <summary>줄 하나의 칸 글자 — 값 · (갈래) · 이름. <see cref="Columns"/> 와 차례가 같다.</summary>
    private IReadOnlyList<string> Cells(int index)
    {
        var item = _stock[index];
        return [$"{_market.PriceOf(item, _cityId)}", $"({item.CategoryName})", item.Name];
    }

    private void OnKey(object sender, KeyEventArgs e)
    {
        if (_list.HandleKey(e.Key)) { e.Handled = true; return; }

        switch (e.Key)
        {
            case Key.Escape:
                Close();
                break;
            case Key.Enter when _list.Chosen.Count > 0:
                Decide();
                e.Handled = true;
                break;
        }
    }

    /// <summary>고른 것을 사는 데까지 끌고 간다(<c>0x004B3978</c> 부터의 되풀이).</summary>
    /// <remarks>
    /// <code>
    ///   0x004B39A1  지닌 것 + 고른 것 &gt; 16 이면 「이대로는 %d개 들을 수 없습니다. 괜찮습니까?」 — 아니오면 목록으로
    ///   0x004B3A2A  아이템 창을 한 장씩
    ///   0x004B3A79  장사꾼 「그렇다면 금화 %d닢 필요하네.」
    ///   0x004B3A99  「%s 아이템을 구입하겠습니까?」(이/이것들의) — 아니오면 목록으로
    ///   0x004B3AAD  돈이 모자라면 장사꾼 「가난한 사람에게는 볼일 없네!…」 — 목록으로
    ///   0x004B3B03  모조품은 그 자리에서 발견(0x004B37B0) — 물건은 소지품에 안 든다
    ///   0x004B3B52  나머지를 소지품에 넣는다 — 넘치면 물릴 수 없는 버리기 창(0x004B1710(…, 0))
    ///   0x004B3B65  돈을 빼고, 장사꾼 「고맙네!」 — 창을 닫는다
    /// </code>
    /// </remarks>
    private void Decide()
    {
        var picked = new List<ItemTable.Record>();
        var pickedAt = new List<int>();
        foreach (int at in _list.Chosen)
            if (at >= 0 && at < _stock.Length) { picked.Add(_stock[at]); pickedAt.Add(at); }
        if (picked.Count == 0) return;

        int over = _player.Items.Count + picked.Count - Player.MaxItems;
        if (over > 0 && !ConfirmDialog.Ask(this, $"이대로는 {over}개 들을 수 없습니다. 괜찮습니까?")) return;

        foreach (var item in picked)
            ItemInfoDialog.Show(this, item, _descriptions?.Of(item.Id) ?? "", _art);

        int total = picked.Sum(item => _market.PriceOf(item, _cityId));
        Say($"그렇다면 금화 {total}닢 필요하네.");

        string what = picked.Count > 1 ? "이것들의 아이템" : "이 아이템";
        if (!ConfirmDialog.Ask(this, $"{what}을 구입하겠습니까?")) return;

        if (!_player.CanAfford(total))
        {
            Say("가난한 사람에게는 볼일 없네! 안 살 거면 돌아가게!");
            return;
        }

        // 모조품은 산 그 자리에서 발견이 된다 — 증거 물건은 보고할 때까지 소지품에 안 든다.
        var goods = new List<int>();
        for (int k = 0; k < picked.Count; k++)
        {
            int id = _asDiscovery[pickedAt[k]];
            if (id < 0) { goods.Add(picked[k].Id); continue; }
            if (_found?.Table.Find(id) is not { } row || !_player.Discover(id)) continue;

            Say("자네, 보는 눈이 있군. 득보는 걸세.");
            ConfirmDialog.Tell(this, $"{row.Name}{GameUi.Josa(row.Name, "을", "를")} 발견했다!");
        }

        if (_game != null) ItemGain.AddForced(this, _game, goods);
        else foreach (int id in goods) _player.Take(id);
        _player.Pay(total);
        Say("고맙네!");
        Close();
    }

    /// <summary>장사꾼이 말한다(<c>0x004692E0</c> — 시장 화자 얼굴).</summary>
    private void Say(string text) => TalkDialog.Say(this, _face, "", text);

    /// <summary>소지품 넘침 창에서 이름을 찾을 판. 없으면 넘침 창 없이 넣는다.</summary>
    private readonly Engine.Game? _game;

    /// <summary>늘어놓을 줄 — 모조품(발견물 번호) 먼저, 그 다음 재고. 재고 줄의 발견물 번호는 −1.</summary>
    private static (ItemTable.Record[] Rows, int[] Marks) Offer(Player player, Market market, int cityId,
                                                               Engine.Discovery.DiscoveryLog? found)
    {
        var rows = new List<ItemTable.Record>();
        var marks = new List<int>();
        foreach (var one in found?.Table.SoldAt(cityId) ?? [])
        {
            if (player.Discoveries.Contains(one.Id)) continue;
            if (market.Find(one.ItemId) is not { } sold) continue;
            rows.Add(sold);
            marks.Add(one.Id);
        }

        foreach (var one in market.StockOf(cityId)) { rows.Add(one); marks.Add(-1); }
        return ([.. rows], [.. marks]);
    }

    /// <summary>시장 구입 창을 연다.</summary>
    /// <remarks>
    /// 창을 열기 전에 두 번 막는다(<c>0x004B3820</c>) — 소지품이 열여섯이면 얼굴 없이
    /// 「이 이상 가질 수 없습니다!」(<c>0x004B3BE4</c>), 팔 것이 하나도 없으면 장사꾼이
    /// 「미안하네, 지금 물건이 떨어지고 없네.」(<c>0x004B3BC9</c>)다.
    /// </remarks>
    public static void Show(Window owner, Player player, Market market, int cityId,
                            ItemDescriptions? descriptions, ItemArt? art,
                            Engine.Discovery.DiscoveryLog? found = null, uint[]? face = null,
                            Engine.Game? game = null)
    {
        if (player.Items.Count >= Player.MaxItems)
        {
            GameDialog.Show(owner, "이 이상 가질 수 없습니다!");
            return;
        }
        if (Offer(player, market, cityId, found).Rows.Length == 0)
        {
            TalkDialog.Say(owner, face, "", "미안하네, 지금 물건이 떨어지고 없네.");
            return;
        }
        new MarketBuyDialog(player, market, cityId, descriptions, art, found, face, game,
                            GameUi.CityScaleOf(owner))
            { Owner = owner }.ShowDialog();
    }
}
