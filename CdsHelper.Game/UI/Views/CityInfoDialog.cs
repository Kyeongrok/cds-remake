using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using CdsHelper.Game.Engine.Market;
using CdsHelper.Game.Local.Helpers;

namespace CdsHelper.Game.UI.Views;

/// <summary>
/// 도시 정보 창 — 도시와 나라 이름, 규모·상태·시세·언어, 그리고 특산품.
/// </summary>
/// <remarks>
/// 도시 커맨드 창의 "도시 정보" 로 연다. 아이템 창과 같은 남회색 바탕이다.
/// <code>
///   런던
///   잉글랜드 왕국                      X
///
///     규모    ■■■■■□□
///     상태    통상
///     시세     127
///     언어    게르만어
///     특산품 [ 대포 ]
///            [ 철광석 ]
///                              [취소]
/// </code>
/// 줄은 16 점마다 딱 붙여 찍고, 단추는 모두 게임 띠 단추다.
/// 값이 어디서 오는지가 제각각이다.
/// <list type="bullet">
/// <item>나라·언어 — 나라 표(<see cref="NationTable"/>). 도시에는 나라 번호만 있고 말은
/// 나라가 낸다. 정복하면 말이 바뀌기 때문이다.</item>
/// <item>규모·특산품 — EXE 도시 표(<see cref="CityExeTable"/>).</item>
/// <item>시세 — <see cref="MarketRates"/>. 거래가 밀고 매달 흔들린다.</item>
/// <item>상태 — <see cref="CityState"/>(도시정보 「상태 %s」, <c>0x00470564</c>). 역사 대본이 전쟁·전염병·대조선 따위로 바꾼다.</item>
/// </list>
/// </remarks>
public sealed class CityInfoDialog : GameWindow
{
    /// <summary>게임 화면에서 뽑은 남회색 바탕. 아이템 창과 같다.</summary>
    private static readonly Brush Back = GameUi.InfoBack;
    private static readonly Brush Ink = Freeze(Color.FromRgb(0x10, 0x10, 0x18));

    /// <summary>
    /// 규모 막대 — 찬 쪽은 붉고 빈 쪽은 검다. 게임 갈무리에서 뽑은 값이다(볼트 28:
    /// 찬 쪽 <c>#87150A</c> · 빈 쪽 <c>#000000</c>). 게임은 색표 <c>0x3B</c> · <c>0x49</c> 로 칠한다(<c>0x004704F5</c>).
    /// </summary>
    private static readonly Brush BarFull = Freeze(Color.FromRgb(0x87, 0x15, 0x0A));
    private static readonly Brush BarEmpty = Freeze(Color.FromRgb(0x00, 0x00, 0x00));

    /// <summary>글색 — 창 전체가 색표 <c>0x49</c> 한 빛이다(<c>0x0047040A</c>).</summary>
    private const byte InkColor = 0x49;

    /// <summary>규모가 다 찼을 때의 값. 막대를 이 값에 맞춰 채운다.</summary>
    /// <remarks>
    /// EXE 도시 표의 규모가 0~7 이다. 막대를 그리는 <c>0x0046C8E0</c> 에도 만점 <c>7</c> 이 넘어간다(<c>0x00470500</c>).
    /// </remarks>
    private const int MaxScale = 7;

    /// <summary>
    /// 창 속 크기와 자리 — 게임 코드에서 옮겼다(<c>0x004706C6</c> 창 336x256, 테 8 을 뺀 속, <c>0x004703C0</c> 글).
    /// <code>
    ///   도시 이름 (8,8) · 나라 (8,24)
    ///   "규모" (24,56) — 막대 (88,58) 112x12
    ///   "상태    %s" (24,72) · "시세    %4d" (24,88) · "언어    %s" (24,104) · "특산품" (24,120)
    ///   특산품 단추 (96, 120 + 24i) 144x24 — 제 것 뒤로 딸린 내륙 도시 것
    ///   취소 (264,208) 48x24
    /// </code>
    /// </remarks>
    private const double BoardWidth = 320, BoardHeight = 240;

    private static SolidColorBrush Freeze(Color c)
    {
        var b = new SolidColorBrush(c);
        b.Freeze();
        return b;
    }

    private CityInfoDialog(string cityName, CityExeTable? cities, NationTable? nations,
                           GoodsTable? goods, ItemArt? art, MarketRates rates, int cityId)
    {
        Title = cityName;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.WidthAndHeight;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        Background = Back;

        int nationId = cities?.NationOf(cityId) ?? -1;
        var nation = nations?.Find(nationId);
        string language = nation is { } n
            ? LanguageName(n.Language)
            : "";

        var board = new Canvas { Width = BoardWidth, Height = BoardHeight, Background = Back };

        Put(board, Text(cityName), 8, 8);
        Put(board, Text(nation?.Name ?? ""), 8, 24);

        // 줄 글은 게임 서식 그대로 이름과 값이 한 줄이다(0x005714A8 「상태    %s」 벌).
        Put(board, Text("규모"), 24, 56);
        Put(board, ScaleBar(cities?.ScaleOf(cityId) ?? 0), 88, 58);
        Put(board, Text($"상태    {CityState.NameOf(rates.StateOf(cityId))}"), 24, 72);
        Put(board, Text($"시세    {rates.Of(cityId),4}"), 24, 88);
        Put(board, Text($"언어    {language}"), 24, 104);

        // 특산품은 줄마다 띠 단추다(0x00470855 → 0x00413450). 누르면 그 교역품 창이 뜬다.
        int row = 0;
        foreach (int id in cities?.SpecialsOf(cityId) ?? [])
        {
            if (goods?.Find(id) is not { } g) continue;
            var button = new GameButton(g.Name, () =>
                GoodsInfoDialog.Show(this, g, goods.CategoryName(g.Category), art), width: 144)
            {
                Margin = new Thickness(0),
            };
            Put(board, button, 96, 120 + 24 * row++);
        }
        if (row > 0) Put(board, Text("특산품"), 24, 120);

        // 취소도 띠 단추다(0x004707AB).
        Put(board, new GameButton("취소", Close, width: 48) { Margin = new Thickness(0) }, 264, 208);

        // 닫기는 게임 조각으로 그린 공용 것이다 — 창마다 손으로 짓지 않는다.
        var close = GameUi.CloseBox(Close);
        close.Margin = new Thickness(0);
        Canvas.SetRight(close, 4);
        Canvas.SetTop(close, 4);
        board.Children.Add(close);

        Content = GameUi.InfoFrame(board, Back);

        GameUi.EnableDrag(this, board);
        KeyDown += (_, e) => { if (e.Key is Key.Escape) Close(); };
    }

    private static void Put(Canvas board, UIElement element, double x, double y)
    {
        Canvas.SetLeft(element, x);
        Canvas.SetTop(element, y);
        board.Children.Add(element);
    }

    /// <summary>언어 이름. 표를 못 읽었으면 번호로 물러선다.</summary>
    private static string LanguageName(int language) =>
        language >= 0 && language < LanguageNames.Length ? LanguageNames[language] : $"언어 {language}";

    /// <summary>
    /// 언어 이름 14가지. EXE 표(<c>0x00560A48</c>)에서 옮겨 적었다 — 이 창 하나 때문에
    /// 표를 또 구울 까닭이 없다.
    /// </summary>
    private static readonly string[] LanguageNames =
    [
        "스페인어", "포르투갈어", "로망스어", "게르만어", "슬라브·그리스어", "아랍어",
        "페르시아어", "중국어", "힌두어", "위굴어", "아프리카토착어", "중남미토착어",
        "동남아시아토착어", "동아시아토착어",
    ];

    /// <summary>
    /// 판 위의 글씨. <b>게임 글꼴</b>로 찍는다.
    /// </summary>
    private static GameUi.GameLabel Text(string text) => new(InkColor)
    {
        Text = text,
        Bold = false,
        FallbackBrush = Ink,
    };

    /// <summary>규모 막대 112x12(<c>0x0047050C</c>). 찬 만큼 붉고 나머지는 검다.</summary>
    private static FrameworkElement ScaleBar(int scale)
    {
        int full = Math.Clamp(scale, 0, MaxScale);
        var bar = new Grid { Width = 112, Height = 12, ToolTip = $"규모 {scale}" };
        bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(full, GridUnitType.Star) });
        bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(MaxScale - full, GridUnitType.Star) });

        var left = new Border { Background = BarFull };
        Grid.SetColumn(left, 0);
        bar.Children.Add(left);

        var right = new Border { Background = BarEmpty };
        Grid.SetColumn(right, 1);
        bar.Children.Add(right);
        return bar;
    }

    /// <summary>도시 정보 창을 연다.</summary>
    public static void Show(Window owner, string cityName, int cityId, CityExeTable? cities,
                            NationTable? nations, GoodsTable? goods, ItemArt? art, MarketRates rates) =>
        new CityInfoDialog(cityName, cities, nations, goods, art, rates, cityId) { Owner = owner }.ShowDialog();
}
