using System.Windows;
using System.Windows.Controls;
using CdsHelper.Game.Engine.Market;
using CdsHelper.Game.Local.Helpers;

namespace CdsHelper.Game.UI.Views;

/// <summary>
/// 매입 정보 「시세」 — 세계지도에서 누른 도시에서 <b>비싸게 팔리는 교역품</b> 20 가지(요청). 원본에 없는 창이다.
/// </summary>
/// <remarks>
/// 값은 매입 정보 순위와 같은 교역소 매각 단가다(<see cref="TradePost.SellPrice"/>) — 지금 시세 · 도시 상태 · 특산가 ·
/// 모드(교역품별 시세 · 값 배율)가 다 든다. 지금 게임에 나와 있는 교역품(<see cref="TradePost.OnSale"/>)만 센다.
/// </remarks>
internal sealed class CityPriceDialog : InfoDialog
{
    /// <summary>교역소의 건물 코드.</summary>
    private const int TradePostCode = 1;

    /// <summary>늘어놓는 교역품 수.</summary>
    private const int Top = 20;

    private const double RankWidth = 40, PictureWidth = 30, PictureSize = 22, GoodsWidth = 200, CategoryWidth = 130, PriceWidth = 110, HeldWidth = 110;

    private readonly Engine.Game _game;
    private readonly TradePost _post;
    private readonly int _city;
    private readonly StackPanel _page = new();

    private CityPriceDialog(Engine.Game game, TradePost post, int city)
    {
        _game = game;
        _post = post;
        _city = city;
        ShowPrices();
        // 「시세」(팔 때 받는 값 순위)와 「판매 물품」(이 도시 교역소가 파는 것)을 오간다(요청).
        Build("", _page, RankWidth + PictureWidth + GoodsWidth + CategoryWidth + PriceWidth + HeldWidth, double.NaN,
              new GameButton("시세", ShowPrices), new GameButton("판매 물품", ShowSold), new GameButton("닫기", Close));
    }

    /// <summary>머리 — 「시세 — 마르세유 (지중해)」. 교역소가 없으면 그렇다고 적고 거짓.</summary>
    private bool Head(string what)
    {
        _page.Children.Clear();
        string culture = _game.CultureOf(_city);
        _page.Children.Add(Label($"{Lang.T(what)} — {_game.CityName(_city)}{(culture.Length > 0 ? "  (" + Lang.T(culture) + ")" : "")}"));
        _page.Children.Add(Gap(6));
        if (_game.CityRows?.HasBuilding(_city, TradePostCode) != false) return true;
        _page.Children.Add(Label("이 도시에는 교역소가 없습니다."));
        _page.Children.Add(Gap(8));
        return false;
    }

    /// <summary>
    /// 판매 물품 — 이 도시 교역소가 파는 것과 구입 단가 · 남은 재고(<see cref="TradePost.RowsOf"/>, 교역소 매매 창과 같은 줄).
    /// 특산품(딸린 내륙 도시 것 포함)은 이름 뒤에 표시한다.
    /// </summary>
    private void ShowSold()
    {
        if (!Head("판매 물품")) return;
        var rows = _post.RowsOf(_game.Player, _city);
        _page.Children.Add(Line("", null, "교역품", "분류", "구입가", "재고"));
        foreach (var row in rows)
        {
            var goods = _game.Goods?.Find(row.Kind);
            string name = _post.NameOf(row.Kind) + (row.Cell == TradePost.SpecialCell ? "  ★" : "");
            _page.Children.Add(Line("", goods is { } g ? _game.ItemPictures?.TryGetImage(g.Pic) : null, name,
                                    goods is { } c ? _game.Goods!.CategoryName(c.Category) : "", $"{row.Price}닢", $"{row.Supply}"));
        }
        if (rows.Count == 0) _page.Children.Add(Label("파는 물건이 없습니다."));
        _page.Children.Add(Gap(6));
        _page.Children.Add(Label("이 도시 교역소에서 살 때 내는 지금 값입니다. ★ 는 특산품입니다."));
        _page.Children.Add(Gap(8));
    }

    /// <summary>시세 — 이 도시에서 비싸게 팔리는 교역품 20 가지.</summary>
    private void ShowPrices()
    {
        var game = _game;
        var post = _post;
        int city = _city;
        var player = game.Player;
        var body = _page;
        if (Head("시세"))
        {
            var held = player.CargoHold.Where(c => !c.Spoiled).Select(c => c.Kind).ToHashSet();
            var ranked = (game.Goods?.Items ?? [])
                .Where(g => post.OnSale(player, g.Id))
                .Select(g => (Goods: g, Price: post.SellPrice(player, city, g.Id)))
                .OrderByDescending(r => r.Price).ThenBy(r => r.Goods.Id)
                .Take(Top).ToList();
            body.Children.Add(Line("순위", null, "교역품", "분류", "단가", ""));
            for (int i = 0; i < ranked.Count; i++)
            {
                var (goods, price) = ranked[i];
                int kind = goods.Id, sell = price;
                body.Children.Add(Clickable(Line($"{i + 1}", game.ItemPictures?.TryGetImage(goods.Pic), goods.Name, game.Goods!.CategoryName(goods.Category), $"{price}닢",
                                                 held.Contains(goods.Id) ? "싣고 있음" : ""), () => ShowSources(kind, sell)));
            }
            body.Children.Add(Gap(6));
            body.Children.Add(Label("이 도시 교역소에 팔 때 받는 지금 시세입니다. 교역품을 누르면 파는 도시가 나옵니다."));
            body.Children.Add(Gap(8));
        }
    }

    /// <summary>
    /// 그 교역품을 <b>파는 도시</b>와 구입가 — 싼 차례(요청). 아는 도시 가운데 교역소가 그것을 파는 곳만이고(교역소 매매 창과 같은 줄,
    /// <see cref="TradePost.RowsOf"/>), 끝 칸은 이 도시에서 팔 때의 한 통 차익이다. 「시세」 단추로 돌아간다.
    /// </summary>
    private void ShowSources(int kind, int sellHere)
    {
        string goods = _post.NameOf(kind);
        if (!Head("파는 도시")) return;
        var player = _game.Player;
        var sources = Enumerable.Range(0, CityExeTable.Count)
            .Where(c => _game.CityVisible(c) && (_game.CityRows?.HasBuilding(c, TradePostCode) ?? true))
            .SelectMany(c => _post.RowsOf(player, c).Where(r => r.Kind == kind).Select(r => (City: c, r.Price, Special: r.Cell == TradePost.SpecialCell)))
            .OrderBy(r => r.Price).ThenBy(r => r.City)
            .Take(Top).ToList();
        _page.Children.Add(Label($"{goods} — {_game.CityName(_city)}{Lang.T("에서 팔면")} {sellHere}닢"));
        _page.Children.Add(Gap(6));
        _page.Children.Add(Line("순위", null, "도시", "문화권", "구입가", "차익"));
        for (int i = 0; i < sources.Count; i++)
        {
            var (city, price, special) = sources[i];
            string culture = _game.CultureOf(city);
            _page.Children.Add(Line($"{i + 1}", null, _game.CityName(city) + (special ? "  ★" : ""), culture.Length > 0 ? Lang.T(culture) : "",
                                    $"{price}닢", $"{sellHere - price}닢"));
        }
        if (sources.Count == 0) _page.Children.Add(Label("아는 도시 가운데 이것을 파는 곳이 없습니다."));
        _page.Children.Add(Gap(6));
        _page.Children.Add(Label("아는 도시 교역소에서 살 때 내는 지금 값입니다. ★ 는 특산품으로 파는 곳입니다."));
        _page.Children.Add(Gap(8));
    }

    /// <summary>줄을 누르면 그 일을 한다 — 손 모양 커서. 누름은 여기서 받아 창 끌기로 새지 않게 한다.</summary>
    private static UIElement Clickable(UIElement line, Action run)
    {
        var row = new Border { Child = line, Background = System.Windows.Media.Brushes.Transparent, Cursor = System.Windows.Input.Cursors.Hand };
        row.MouseLeftButtonDown += (_, e) => { e.Handled = true; run(); };
        return row;
    }

    /// <summary>순위 · 교역품 · 분류 · 단가 · 싣고 있는지 한 줄. 단가는 오른쪽으로 붙인다.</summary>
    private static UIElement Line(string rank, System.Windows.Media.ImageSource? picture, string goods, string category, string price, string held)
    {
        var line = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 0) };
        line.Children.Add(Cell(rank, RankWidth, HorizontalAlignment.Left));
        // 교역품 그림 — 머리 줄은 같은 너비를 비운다.
        var slot = new Border { Width = PictureWidth, VerticalAlignment = VerticalAlignment.Center };
        if (picture != null)
            slot.Child = new Image { Source = picture, Width = PictureSize, Height = PictureSize, Stretch = System.Windows.Media.Stretch.Uniform, HorizontalAlignment = HorizontalAlignment.Left };
        line.Children.Add(slot);
        line.Children.Add(Cell(goods, GoodsWidth, HorizontalAlignment.Left));
        line.Children.Add(Cell(category, CategoryWidth, HorizontalAlignment.Left));
        line.Children.Add(Cell(price, PriceWidth, HorizontalAlignment.Right));
        line.Children.Add(Cell(held, HeldWidth, HorizontalAlignment.Right));
        return line;
    }

    private static UIElement Cell(string text, double width, HorizontalAlignment align)
    {
        var label = Label(text);
        label.HorizontalAlignment = align;
        return new Border { Width = width, Child = label, VerticalAlignment = VerticalAlignment.Center };
    }

    /// <summary>그 도시의 시세 창을 띄운다 — 교역품 표를 못 읽은 게임이면 안 뜬다.</summary>
    public static void Show(Window owner, Engine.Game game, int city, bool sold = false, int sourcesOf = -1)
    {
        if (game.Trade is not { } trade || game.Goods is not { } goods) return;
        var post = new TradePost(trade, goods, game.Rates, game.CityRows, game.Nations, game.Discoveries?.Table);
        var dialog = new CityPriceDialog(game, post, city) { Owner = owner };
        if (sold) dialog.ShowSold();
        if (sourcesOf >= 0) dialog.ShowSources(sourcesOf, post.SellPrice(game.Player, city, sourcesOf));
        dialog.ShowDialog();
    }
}
