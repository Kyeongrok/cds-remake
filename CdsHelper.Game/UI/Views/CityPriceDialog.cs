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

    private CityPriceDialog(Engine.Game game, TradePost post, int city)
    {
        var player = game.Player;
        string name = game.CityName(city);
        var body = new StackPanel();
        string culture = game.CultureOf(city);
        body.Children.Add(Label($"{name}{(culture.Length > 0 ? "  (" + Lang.T(culture) + ")" : "")}"));
        body.Children.Add(Gap(6));

        if (game.CityRows?.HasBuilding(city, TradePostCode) == false)
            body.Children.Add(Label("이 도시에는 교역소가 없습니다."));
        else
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
                body.Children.Add(Line($"{i + 1}", game.ItemPictures?.TryGetImage(goods.Pic), goods.Name, game.Goods!.CategoryName(goods.Category), $"{price}닢",
                                       held.Contains(goods.Id) ? "싣고 있음" : ""));
            }
            body.Children.Add(Gap(6));
            body.Children.Add(Label("이 도시 교역소에 팔 때 받는 지금 시세입니다."));
        }
        body.Children.Add(Gap(8));

        Build("시세", body, RankWidth + PictureWidth + GoodsWidth + CategoryWidth + PriceWidth + HeldWidth, double.NaN, new GameButton("닫기", Close));
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
    public static void Show(Window owner, Engine.Game game, int city)
    {
        if (game.Trade is not { } trade || game.Goods is not { } goods) return;
        var post = new TradePost(trade, goods, game.Rates, game.CityRows, game.Nations, game.Discoveries?.Table);
        new CityPriceDialog(game, post, city) { Owner = owner }.ShowDialog();
    }
}
