using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using CdsHelper.Game.Engine.Market;
using CdsHelper.Game.Local.Helpers;
using CdsHelper.Support.Local.Models;

namespace CdsHelper.Game.UI.Views;

/// <summary>
/// 모드 「적하 시세 순위」 — 실은 교역품 한 칸을 <b>어느 도시에서 비싸게 팔 수 있는지</b> 차례로 늘어놓는다.
/// </summary>
/// <remarks>
/// 원본에 없는 창이다(대항해시대 2 의 적하일람 조언을 본떴다). 함대정보 「짐」 판에서 교역품을 누르거나 T 글쇠로 뜬다.
/// 값은 교역소 매각 단가 그대로다(<see cref="TradePost.SellPriceOf"/>) — 지금 시세 · 도시 상태 · 특산가가 다 든다.
/// <b>아는 도시</b> 가운데 지금 서 있고 교역소가 있는 곳만 본다.
/// </remarks>
internal sealed class CargoRankDialog : InfoDialog
{
    /// <summary>교역소의 건물 코드.</summary>
    private const int TradePostCode = 1;

    /// <summary>늘어놓는 도시 수.</summary>
    private const int Top = 15;

    private const double RankWidth = 40, CultureWidth = 110, CityWidth = 170, BoughtWidth = 110, PriceWidth = 110, TotalWidth = 150;

    /// <summary>짐 목록 칸 너비 — 교역품 · 개수 · 기한 · 매입가 · 가장 비싼 도시 · 그 단가. 합이 순위 표 너비와 같다.</summary>
    private const double GoodsWidth = 200, CountWidth = 60, ShelfWidth = 60, BestCityWidth = 150, BestPriceWidth = 110;

    /// <summary>고른 짐 줄 바탕.</summary>
    private static readonly Brush Picked = Frozen(Color.FromRgb(0x5C, 0x40, 0x38));

    private readonly Engine.Game _game;
    private readonly TradePost _post;
    private readonly IReadOnlyList<Player.Cargo> _hold;
    private readonly List<(int City, int Price)>[] _ranked;
    private readonly List<Border> _rows = [];
    private readonly StackPanel _rank = new();
    private int _index;

    /// <summary>
    /// 위에 실은 짐 목록, 밑에 고른 짐의 순위를 놓는다 — 줄을 누르거나 위 · 아래 글쇠로 고르면 밑 순위가 그 자리에서 바뀐다.
    /// 예전에는 짐이 여럿이면 첫 짐의 순위를 띄우고 위에 교역품 단추만 늘어놓아, 누르면 창을 닫았다 다시 열었다(건의).
    /// </summary>
    private CargoRankDialog(Engine.Game game, TradePost post, IReadOnlyList<Player.Cargo> hold, int index,
                            Action<Player.Cargo>? showInfo)
    {
        _game = game;
        _post = post;
        _hold = hold;
        _index = index;
        var player = game.Player;
        _ranked = [.. hold.Select(cargo => Enumerable.Range(0, CityExeTable.Count)
            .Where(city => game.CityVisible(city) && (game.CityRows?.HasBuilding(city, TradePostCode) ?? true))
            .Select(city => (City: city, Price: post.SellPriceOf(player, city, cargo)))
            .OrderByDescending(r => r.Price).ThenBy(r => r.City)
            .Take(Top).ToList())];

        var body = new StackPanel();
        // 창고 — 함대정보 「짐용량」과 같은 값(보급품까지 든 통 수 / 포탑을 뺀 용량)과, 교역품이 차지한 칸 수.
        int goods = hold.Sum(c => c.Count);
        int loaded = player.LoadedBarrels;
        body.Children.Add(Label($"짐용량 {loaded} / {player.Capacity}  (교역품 {goods} · 보급품 {loaded - goods})   교역품 칸 {hold.Count} / {Player.CargoSlots}"));
        body.Children.Add(Gap(6));

        body.Children.Add(Row(null, ("교역품", GoodsWidth, false), ("개수", CountWidth, true), ("기한", ShelfWidth, true),
                              ("매입가", BoughtWidth, true), ("가장 비싼 도시", BestCityWidth, false), ("단가", BestPriceWidth, true)));
        for (int i = 0; i < hold.Count; i++)
        {
            var cargo = hold[i];
            var best = _ranked[i].FirstOrDefault();
            bool any = !cargo.Spoiled && _ranked[i].Count > 0;
            int at = i;
            var row = Row(() => Pick(at),
                          (Engine.GameInfo.CargoLabel(game, cargo), GoodsWidth, false), ($"{cargo.Count}", CountWidth, true),
                          (cargo.Shelf == Player.NeverSpoils ? "-" : cargo.Spoiled ? "썩음" : $"{cargo.Months}개월", ShelfWidth, true),
                          ($"{post.BuyPrice(player, cargo.Origin, cargo.Kind)}닢", BoughtWidth, true),
                          (any ? game.CityName(best.City) : "-", BestCityWidth, false), (any ? $"{best.Price}닢" : "-", BestPriceWidth, true));
            _rows.Add(row);
            body.Children.Add(row);
        }
        body.Children.Add(Gap(10));
        body.Children.Add(_rank);
        Pick(index);

        Build("매입 정보", body, RankWidth + CultureWidth + CityWidth + BoughtWidth + PriceWidth + TotalWidth, double.NaN,
              new GameButton("교역품 정보", () => showInfo?.Invoke(_hold[_index])), new GameButton("취소", Close));

        PreviewKeyDown += (_, e) =>
        {
            if (e.Key is not (Key.Up or Key.Down)) return;
            Pick((_index + (e.Key == Key.Down ? 1 : -1) + _hold.Count) % _hold.Count);
            e.Handled = true;
        };
    }

    /// <summary>그 짐을 고른다 — 목록 줄을 칠하고 밑 순위를 그 짐으로 다시 짓는다.</summary>
    private void Pick(int index)
    {
        _index = index;
        for (int i = 0; i < _rows.Count; i++) _rows[i].Background = i == index ? Picked : Brushes.Transparent;

        var player = _game.Player;
        var cargo = _hold[index];
        var ranked = _ranked[index];
        // 매입가 — 교역소 「내 짐」 줄의 「매입 N닢」과 같다(원산지의 지금 매입 단가).
        int bought = _post.BuyPrice(player, cargo.Origin, cargo.Kind);

        _rank.Children.Clear();
        _rank.Children.Add(Label($"비싸게 팔리는 도시 — {Engine.GameInfo.CargoLabel(_game, cargo)}  {cargo.Count}개"));
        _rank.Children.Add(Gap(6));
        if (cargo.Spoiled) _rank.Children.Add(Label("썩어서 어디서도 값을 받지 못합니다."));
        else
        {
            _rank.Children.Add(Line("순위", "문화권", "도시", "매입가", "단가", "전부 팔면"));
            for (int i = 0; i < ranked.Count; i++)
            {
                var (city, price) = ranked[i];
                string culture = _game.CultureOf(city);
                _rank.Children.Add(Line($"{i + 1}", culture.Length > 0 ? Lang.T(culture) : "", _game.CityName(city), $"{bought}닢", $"{price}닢", $"{(long)price * cargo.Count}닢"));
            }
            _rank.Children.Add(Gap(6));
            _rank.Children.Add(Label("아는 도시의 지금 시세입니다."));
        }
        _rank.Children.Add(Gap(8));
    }

    /// <summary>짐 목록 한 줄. 누르는 손이 있으면 눌러 고르는 줄이다.</summary>
    private static Border Row(Action? pick, params (string Text, double Width, bool Right)[] cells)
    {
        var line = new StackPanel { Orientation = Orientation.Horizontal };
        foreach (var (text, width, right) in cells)
            line.Children.Add(Cell(text, width, right ? HorizontalAlignment.Right : HorizontalAlignment.Left));
        var row = new Border { Child = line, Padding = new Thickness(0, 2, 0, 1), Background = Brushes.Transparent };
        if (pick != null)
        {
            row.Cursor = Cursors.Hand;
            row.MouseLeftButtonUp += (_, _) => pick();
        }
        return row;
    }

    /// <summary>순위 · 문화권 · 도시 · 매입가 · 단가 · 합계 여섯 칸 한 줄. 값 셋은 오른쪽으로 붙인다.</summary>
    private static UIElement Line(string rank, string culture, string city, string bought, string price, string total)
    {
        var line = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 0) };
        line.Children.Add(Cell(rank, RankWidth, HorizontalAlignment.Left));
        line.Children.Add(Cell(culture, CultureWidth, HorizontalAlignment.Left));
        line.Children.Add(Cell(city, CityWidth, HorizontalAlignment.Left));
        line.Children.Add(Cell(bought, BoughtWidth, HorizontalAlignment.Right));
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

    /// <summary>
    /// 순위 창을 연다 — 실은 짐 가운데 <paramref name="index"/> 번째를 골라 둔 채로. 교역품 표를 못 읽은 게임이거나
    /// 그 짐이 없으면 거짓 — 부르는 쪽이 여느 교역품 창을 낸다.
    /// </summary>
    public static bool Show(Window owner, Engine.Game game, int index, Action<Player.Cargo>? showInfo)
    {
        if (game.Trade is not { } trade || game.Goods is not { } goods) return false;
        var hold = game.Player.CargoHold;
        if (index < 0 || index >= hold.Count) return false;
        var post = new TradePost(trade, goods, game.Rates, game.CityRows, game.Nations, game.Discoveries?.Table);
        new CargoRankDialog(game, post, hold, index, showInfo) { Owner = owner }.ShowDialog();
        return true;
    }
}
