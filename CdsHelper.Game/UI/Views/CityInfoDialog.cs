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
///      규모   ■■■■■□□□
///      상태   통상
///      시세   127
///      언어   게르만어
///      특산품  [ 대포 ]
///              [ 철광석 ]
///                              [취소]
/// </code>
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

    /// <summary>규모 막대 — 찬 쪽은 붉고 빈 쪽은 검다. 게임 화면에서 뽑았다.</summary>
    private static readonly Brush BarFull = Freeze(Color.FromRgb(0xA8, 0x20, 0x20));
    private static readonly Brush BarEmpty = Freeze(Color.FromRgb(0x10, 0x10, 0x10));

    /// <summary>규모가 다 찼을 때의 값. 막대를 이 값에 맞춰 채운다.</summary>
    /// <remarks>
    /// EXE 도시 표의 규모가 0~7 이다. 게임 화면(런던, 규모 5)에서 막대를 재니 붉은 칸이
    /// 전체의 71% 였다 — 5/7 이지 5/8 이 아니다.
    /// </remarks>
    private const int MaxScale = 7;

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

        var head = new StackPanel { Margin = new Thickness(16, 12, 0, 0) };
        head.Children.Add(Text(cityName, 16));
        head.Children.Add(Text(nation?.Name ?? "", 16));

        // 닫기는 게임 조각으로 그린 공용 것이다 — 창마다 손으로 짓지 않는다.
        var close = GameUi.CloseBox(Close);

        var top = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(close, Dock.Right);
        top.Children.Add(close);
        top.Children.Add(head);

        var rows = new StackPanel { Margin = new Thickness(36, 18, 24, 6) };
        rows.Children.Add(Row("규모", ScaleBar(cities?.ScaleOf(cityId) ?? 0)));
        rows.Children.Add(Row("상태", Text(CityState.NameOf(rates.StateOf(cityId)), 15)));
        rows.Children.Add(Row("시세", Text($"{rates.Of(cityId)}", 15)));
        rows.Children.Add(Row("언어", Text(language, 15)));

        // 특산품은 줄마다 단추다. 누르면 그 교역품 창이 뜬다.
        var specials = new StackPanel();
        foreach (int id in cities?.SpecialsOf(cityId) ?? [])
        {
            if (goods?.Find(id) is not { } g) continue;
            var button = GameUi.PushButton(g.Name, () =>
                GoodsInfoDialog.Show(this, g, goods.CategoryName(g.Category), art), 190);
            button.HorizontalAlignment = HorizontalAlignment.Left;
            button.Margin = new Thickness(0, 0, 0, 4);
            specials.Children.Add(button);
        }
        if (specials.Children.Count > 0) rows.Children.Add(Row("특산품", specials));

        var cancel = GameUi.PushButton("취소", Close, 78);
        cancel.HorizontalAlignment = HorizontalAlignment.Right;
        cancel.Margin = new Thickness(0, 6, 16, 14);

        var stack = new StackPanel { MinWidth = 400 };
        stack.Children.Add(top);
        stack.Children.Add(rows);
        stack.Children.Add(cancel);
        Content = GameUi.InfoFrame(stack, Back);

        GameUi.EnableDrag(this, top);
        KeyDown += (_, e) => { if (e.Key is Key.Escape) Close(); };
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

    /// <summary>줄 하나 — 이름과 값.</summary>
    private static FrameworkElement Row(string name, FrameworkElement value)
    {
        var line = new DockPanel { LastChildFill = true, Margin = new Thickness(0, 0, 0, 6) };
        var label = Text(name, 15);
        label.Width = 76;
        DockPanel.SetDock(label, Dock.Left);
        line.Children.Add(label);
        value.HorizontalAlignment = HorizontalAlignment.Left;
        value.VerticalAlignment = VerticalAlignment.Center;
        line.Children.Add(value);
        return line;
    }

    /// <summary>
    /// 판 위의 글씨. <b>게임 글꼴</b>로 찍는다 — 바탕이 밝아 검은 글씨다.
    /// </summary>
    /// <remarks>
    /// 게임 글꼴은 크기가 한 가지라 <paramref name="size"/> 는 <b>물러설 때만</b> 든다 —
    /// 글꼴을 못 읽었을 때의 WPF 글자 크기다.
    /// </remarks>
    private static GameUi.GameLabel Text(string text, double size) => new(GameFont.BlackColor)
    {
        Text = text,
        Bold = false,
        FallbackBrush = Ink,
        VerticalAlignment = VerticalAlignment.Center,
    };

    /// <summary>규모 막대. 찬 만큼 붉고 나머지는 검다.</summary>
    private static FrameworkElement ScaleBar(int scale)
    {
        int full = Math.Clamp(scale, 0, MaxScale);
        var bar = new Grid { Width = 210, Height = 16 };
        bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(full, GridUnitType.Star) });
        bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(MaxScale - full, GridUnitType.Star) });

        var left = new Border { Background = BarFull };
        Grid.SetColumn(left, 0);
        bar.Children.Add(left);

        var right = new Border { Background = BarEmpty };
        Grid.SetColumn(right, 1);
        bar.Children.Add(right);

        return new Border
        {
            BorderBrush = Ink,
            BorderThickness = new Thickness(1),
            Child = bar,
            ToolTip = $"규모 {scale}",
        };
    }

    /// <summary>도시 정보 창을 연다.</summary>
    public static void Show(Window owner, string cityName, int cityId, CityExeTable? cities,
                            NationTable? nations, GoodsTable? goods, ItemArt? art, MarketRates rates) =>
        new CityInfoDialog(cityName, cities, nations, goods, art, rates, cityId) { Owner = owner }.ShowDialog();
}
