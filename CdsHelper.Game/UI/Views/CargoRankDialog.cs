using System.Windows;
using System.Windows.Controls;
using CdsHelper.Game.Engine.Market;
using CdsHelper.Game.Local.Helpers;
using CdsHelper.Support.Local.Models;

namespace CdsHelper.Game.UI.Views;

/// <summary>
/// 모드 「적하 시세 순위」 — 실은 교역품 한 칸을 <b>어느 도시에서 비싸게 팔 수 있는지</b> 차례로 늘어놓는다.
/// </summary>
/// <remarks>
/// 원본에 없는 창이다(대항해시대 2 의 적하일람 조언을 본떴다). 함대정보 「짐」 판에서 교역품을 누르면 뜬다.
/// 값은 교역소 매각 단가 그대로다(<see cref="TradePost.SellPriceOf"/>) — 지금 시세 · 도시 상태 · 특산가가 다 든다.
/// <b>아는 도시</b> 가운데 지금 서 있고 교역소가 있는 곳만 본다.
/// </remarks>
internal sealed class CargoRankDialog : InfoDialog
{
    /// <summary>교역소의 건물 코드.</summary>
    private const int TradePostCode = 1;

    /// <summary>늘어놓는 도시 수.</summary>
    private const int Top = 15;

    private const double RankWidth = 40, CityWidth = 190, PriceWidth = 110, TotalWidth = 150;

    private CargoRankDialog(Engine.Game game, TradePost post, Player.Cargo cargo, Action? showInfo)
    {
        var player = game.Player;
        var ranked = Enumerable.Range(0, CityExeTable.Count)
            .Where(city => game.CityVisible(city) && (game.CityRows?.HasBuilding(city, TradePostCode) ?? true))
            .Select(city => (City: city, Price: post.SellPriceOf(player, city, cargo)))
            .OrderByDescending(r => r.Price).ThenBy(r => r.City)
            .Take(Top).ToList();

        var rows = new StackPanel();
        rows.Children.Add(Label($"{Engine.GameInfo.CargoLabel(game, cargo)}  {cargo.Count}개"));
        rows.Children.Add(Gap(6));

        if (cargo.Spoiled) rows.Children.Add(Label("썩어서 어디서도 값을 받지 못합니다."));
        else
        {
            rows.Children.Add(Line("순위", "도시", "단가", "전부 팔면"));
            for (int i = 0; i < ranked.Count; i++)
            {
                var (city, price) = ranked[i];
                rows.Children.Add(Line($"{i + 1}", game.CityName(city), $"{price}닢", $"{(long)price * cargo.Count}닢"));
            }
            rows.Children.Add(Gap(6));
            rows.Children.Add(Label("아는 도시의 지금 시세입니다."));
        }
        rows.Children.Add(Gap(8));

        Build("비싸게 팔리는 도시", rows, RankWidth + CityWidth + PriceWidth + TotalWidth, double.NaN,
              new GameButton("교역품 정보", () => showInfo?.Invoke()), new GameButton("취소", Close));
    }

    /// <summary>순위 · 도시 · 단가 · 합계 네 칸 한 줄. 값 둘은 오른쪽으로 붙인다.</summary>
    private static UIElement Line(string rank, string city, string price, string total)
    {
        var line = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 0) };
        line.Children.Add(Cell(rank, RankWidth, HorizontalAlignment.Left));
        line.Children.Add(Cell(city, CityWidth, HorizontalAlignment.Left));
        line.Children.Add(Cell(price, PriceWidth, HorizontalAlignment.Right));
        line.Children.Add(Cell(total, TotalWidth, HorizontalAlignment.Right));
        return line;
    }

    private static UIElement Cell(string text, double width, HorizontalAlignment align)
    {
        var label = Label(text);
        label.HorizontalAlignment = align;
        return new Border { Width = width, Child = label };
    }

    /// <summary>순위 창을 연다. 교역품 표를 못 읽은 판이면 거짓 — 부르는 쪽이 여느 교역품 창을 낸다.</summary>
    public static bool Show(Window owner, Engine.Game game, Player.Cargo cargo, Action? showInfo)
    {
        if (game.Trade is not { } trade || game.Goods is not { } goods) return false;
        var post = new TradePost(trade, goods, game.Rates, game.CityRows, game.Nations, game.Discoveries?.Table);
        new CargoRankDialog(game, post, cargo, showInfo) { Owner = owner }.ShowDialog();
        return true;
    }
}
