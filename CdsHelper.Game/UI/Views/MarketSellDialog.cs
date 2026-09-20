using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using CdsHelper.Game.Engine.Market;
using CdsHelper.Game.Local.Helpers;
using CdsHelper.Support.Local.Models;

namespace CdsHelper.Game.UI.Views;

/// <summary>
/// 시장 매각 창 — 지닌 것을 늘어놓고 하나를 골라 판다.
/// </summary>
/// <remarks>
/// 게임 차례를 그대로 따른다(매각 본체 <c>0x004B3C76</c>).
/// <code>
///   0  지닌 것이 없으면   "응? 도대체 무엇을 팔겠다는 건가?"  하고 끝난다
///   1  들어서면           "팔고 싶은 물건이 있으면 어디 보여주게!"
///   2  소지품 일람        줄을 고르면 "결정" 이 살아난다
///   3  값을 부른다        "으~음. 금화 %ld닢이란 말이군."   YES/NO
///   4  YES 면 팔린다
/// </code>
/// 사는 쪽과 달리 <b>값을 부르는 창이 곧 물음창</b>이다 — 알림창 하나를 더 띄우지 않고
/// 그 자리에서 YES/NO 를 받는다(<c>push 2</c> 로 단추 두 개를 달아 부른다).
///
/// 무엇이든 받아 준다. 그 도시가 파는 물건인지는 안 따진다.
/// </remarks>
public sealed class MarketSellDialog : GameWindow
{
    /// <summary>
    /// 줄 속 칸 — (갈래) · 이름. 둘 다 오른쪽맞춤이다.
    /// </summary>
    /// <remarks>
    /// <b>값 칸은 없다.</b> 게임의 "소지품 일람" 은 이름과 갈래만 낸다 — 얼마를 쳐 주는지는
    /// 결정을 누른 뒤 "으~음. 금화 %ld닢이란 말이군." 으로 부른다.
    /// </remarks>
    private static readonly GameListColumn[] Columns =
    [
        new(GameListDock.Right, new Thickness(0, 0, 10, 0), 114, HorizontalAlignment.Right),
        new(GameListDock.Fill, new Thickness(10, 0, 0, 0), Align: HorizontalAlignment.Right),
    ];

    /// <summary>소지품이 늘면 창이 화면을 넘지 않게 여기서 자른다.</summary>
    private const double ListMaxHeight = 420;

    /// <summary>목록 바닥 폭. 게임 갈무리를 재어 맞췄다 — 이보다 넓으면 이름 칸이 휑하다.</summary>
    private const double ListWidth = 356;

    private readonly Player _player;
    private readonly Market _market;
    private readonly int _cityId;

    private readonly List<ItemTable.Record> _held = [];
    private readonly GameList _list;
    private readonly GameButton _decide;

    private MarketSellDialog(Player player, Market market, ItemTable items, int cityId)
    {
        _player = player;
        _market = market;
        _cityId = cityId;

        Title = "소지품 일람";
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.WidthAndHeight;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        Background = GameUi.Back;

        foreach (int id in player.Items)
            if (items.Find(id) is { } item) _held.Add(item);

        _list = new GameList(Columns, Cells, _held.Count, maxHeight: ListMaxHeight)
        {
            // 게임은 한 번에 여럿을 판다 — 고른 줄이 여럿이면 값도 합쳐서 부른다.
            Pick = GameListPick.Many,
            Margin = new Thickness(0),
            BorderBrush = GameUi.Edge,
        };

        _decide = new GameButton("결정", Decide, width: 110) { On = false };
        _list.SelectionChanged += () => _decide.On = _list.Chosen.Count > 0;

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 4, 0, 0),
        };
        buttons.Children.Add(_decide);
        buttons.Children.Add(new GameButton("중단", Close, width: 110));

        var title = GameUi.TitleBar("소지품 일람", Close);
        GameUi.EnableDrag(this, title);

        var stack = new StackPanel { MinWidth = ListWidth };
        stack.Children.Add(title);
        stack.Children.Add(_list);
        stack.Children.Add(buttons);

        Content = GameUi.DialogEdge(stack);

        KeyDown += OnKey;
    }

    /// <summary>줄 하나의 칸 글자 — (갈래) · 이름. <see cref="Columns"/> 와 차례가 같다.</summary>
    private IReadOnlyList<string> Cells(int index) =>
        [$"({_held[index].CategoryName})", _held[index].Name];

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

    /// <summary>값을 부르고, YES 면 판다. 고른 것이 여럿이면 값을 합쳐 부른다.</summary>
    /// <remarks>
    /// 원본은 <b>한 번 고르면 끝</b>이다(<c>0x004B3D59</c> → <c>0x004B3D90</c>) — 팔든 안 팔든 창을 닫는다.
    /// 값을 부르는 것은 장사꾼 얼굴이다(<c>0x004B3D51</c>).
    /// </remarks>
    private void Decide()
    {
        // 뒤에서부터 걷어야 앞 줄을 뺄 때 뒷 줄 번호가 밀리지 않는다.
        var at = _list.Chosen.Where(i => i >= 0 && i < _held.Count).OrderByDescending(i => i).ToList();
        if (at.Count == 0) return;

        int paid = at.Sum(i => _market.PaidFor(_held[i], _cityId));
        if (ConfirmDialog.Ask(this, $"으~음. 금화 {paid}닢이란 말이군.", face: _face))
            foreach (int i in at) _market.Sell(_player, _held[i], _cityId);
        Close();
    }

    /// <summary>장사꾼 얼굴(시장 화자).</summary>
    private uint[]? _face;

    /// <summary>
    /// 매각 창을 연다. 지닌 것이 없으면 창 대신 게임 그대로의 한 마디만 낸다.
    /// </summary>
    /// <remarks>두 마디 다 장사꾼 얼굴이다(<c>0x004B3C95</c> · <c>0x004B3CB3</c>).</remarks>
    public static void Show(Window owner, Player player, Market market, ItemTable items, int cityId,
                            uint[]? face = null)
    {
        if (player.Items.Count == 0)
        {
            TalkDialog.Say(owner, face, "", "응? 도대체 무엇을 팔겠다는 건가?");
            return;
        }

        TalkDialog.Say(owner, face, "", "팔고 싶은 물건이 있으면 어디 보여주게!");
        new MarketSellDialog(player, market, items, cityId) { Owner = owner, _face = face }.ShowDialog();
    }
}
