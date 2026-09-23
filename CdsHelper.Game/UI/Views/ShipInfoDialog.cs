using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using CdsHelper.Game.Local.Helpers;
using CdsHelper.Support.Local.Models;

namespace CdsHelper.Game.UI.Views;

/// <summary>
/// 「선박정보」 — 배 한 척의 판. 함대정보에서 이름 단추를 누르면 열린다.
/// </summary>
/// <remarks>
/// 원본 갈무리(2배)를 재어 맞췄다. 판 색은 함대정보와 같은 강청색이고 제목 줄이 없다.
/// <code>
///   선명    테레사
///   선체    카라벨                   마스트  ＿△＿
///   소유자  에스테반
///
///   승원수  15
///   내구도  [━━━━━━━━]              ← 막대 112 x 13 · 밝은 테 · 검정 바탕에 붉은 살
///                        20/ 20      ← 막대 아래, 오른쪽 끝에 맞춘다
///   추진력  [━━━━━━━━]
///                        80/ 80
///   최대중량  1250
///   최대용량  128
///                              [영상] [뒤로]
/// </code>
/// 줄 사이는 18점, 이름 칸은 왼쪽 20 · 값은 78 부터, 마스트는 218 · 돛 표시는 275 부터다.
/// 예전 판(선종·포·선수상을 글줄로 적은 것)은 원본에 없어 걷었다 — 대포·선수상은 조선소 개조 목록이 보인다.
/// </remarks>
internal sealed class ShipInfoDialog : InfoDialog
{
    /// <summary>판 크기. 함대정보 판과 폭을 맞췄다.</summary>
    private const double BoardWidth = 375, BoardHeight = 212;

    /// <summary>값이 서는 자리 · 마스트 이름과 돛 표시가 서는 자리.</summary>
    private const double ValueLeft = 58, MastLeft = 198, SailLeft = 255;

    /// <summary>한 줄 높이(갈무리 36점 ÷ 2).</summary>
    private const double LineHeight = 18;

    /// <summary>막대 크기.</summary>
    private const double BarWidth = 112, BarHeight = 13;

    /// <summary>막대 테 · 빈 쪽 · 찬 쪽. 찬 쪽은 함대정보 막대와 같은 붉은색이다.</summary>
    private static readonly Brush BarEdge = Frozen(Color.FromRgb(0xE0, 0xDC, 0xDC));
    private static readonly Brush BarBack = Frozen(Color.FromRgb(0, 0, 0));
    private static readonly Brush BarFill = Frozen(Color.FromRgb(135, 21, 10));

    /// <inheritdoc/>
    protected override Brush Board => Steel;

    /// <inheritdoc/>
    protected override Brush BoardEdge => SteelEdge;

    private ShipInfoDialog(Player player, int at)
    {
        var ship = player.Ships[at];
        var rows = new StackPanel();

        rows.Children.Add(Line("선명", ship.Name));
        var hull = Line("선체", ship.Hull.Name);
        Place(hull, Label("마스트"), MastLeft);
        Place(hull, Label(Sails(ship)), SailLeft);
        rows.Children.Add(hull);
        // 소유자는 제독의 이름(이름 칸)이다. 빌린 배도 제독 이름으로 적는다 — 원본에서 확인한 것은 제 배뿐이다.
        rows.Children.Add(Line("소유자", player.Given.Length > 0 ? player.Given : player.Name));

        rows.Children.Add(Gap(LineHeight));
        rows.Children.Add(Line("승원수", $"{ship.Crew}"));
        rows.Children.Add(Gauge("내구도", ship.Hp, ship.MaxHp));
        rows.Children.Add(Gauge("추진력", ship.Speed, ship.MaxSpeed));
        rows.Children.Add(Line("최대중량", $"{ship.Tonnage}", valueLeft: ValueLeft + 16));
        // 함대정보 짐용량과 같은 잣대다 — 포탑이 먹은 자리를 뺀 것(갈무리: 함대 10/128 · 이 판 128).
        rows.Children.Add(Line("최대용량", $"{ship.UsableCapacity}", valueLeft: ValueLeft + 16));

        // 「영상」은 배 그림을 크게 띄우는 단추다. 그 그림은 아직 옮기지 않아 흐려 둔다.
        Build("", rows, BoardWidth, BoardHeight,
              new GameButton("영상", () => { }) { On = false },
              new GameButton("뒤로", Close));
    }

    /// <summary>이름 한 칸에 값 한 칸인 줄. 값은 못 박은 자리에 선다.</summary>
    private static Canvas Line(string name, string value, double valueLeft = ValueLeft)
    {
        var line = new Canvas { Height = LineHeight };
        Place(line, Label(name), 0);
        Place(line, Label(value), valueLeft);
        return line;
    }

    private static void Place(Canvas line, UIElement element, double left)
    {
        Canvas.SetLeft(element, left);
        Canvas.SetTop(element, 0);
        line.Children.Add(element);
    }

    /// <summary>
    /// 이름 · 막대, 그 아래 줄에 「지금/최대」를 막대 오른쪽 끝에 맞춰 적는다(<c>%3d/%3d</c>).
    /// </summary>
    private static UIElement Gauge(string name, int now, int max)
    {
        double part = max > 0 ? Math.Clamp((double)now / max, 0, 1) : 0;

        var bar = new Border
        {
            Width = BarWidth,
            Height = BarHeight,
            Background = BarBack,
            BorderBrush = BarEdge,
            BorderThickness = new Thickness(1),
            Child = new Border
            {
                Width = (BarWidth - 2) * part,
                Background = BarFill,
                HorizontalAlignment = HorizontalAlignment.Left,
            },
        };

        var top = new Canvas { Height = LineHeight };
        Place(top, Label(name), 0);
        Canvas.SetTop(bar, 2);
        Canvas.SetLeft(bar, ValueLeft - 2);
        top.Children.Add(bar);

        var number = Label($"{now,3}/{max,3}");
        number.HorizontalAlignment = HorizontalAlignment.Right;
        var under = new Grid
        {
            Width = ValueLeft - 2 + BarWidth,
            Height = LineHeight,
            HorizontalAlignment = HorizontalAlignment.Left,
            Children = { number },
        };

        var both = new StackPanel { Margin = new Thickness(0, 4, 0, 4) };
        both.Children.Add(top);
        both.Children.Add(under);
        return both;
    }

    /// <summary>마스트 셋의 돛 — 없음 <c>＿</c> · 삼각 <c>△</c> · 사각 <c>□</c>(<c>0x00545610</c> 벌).</summary>
    private static string Sails(Ship ship) => string.Concat(ship.Sails.Select(sail => sail switch
    {
        Ship.Lateen => "△",
        Ship.Square => "□",
        _ => "＿",
    }));

    /// <summary>그 배의 판을 연다.</summary>
    /// <param name="items">예전 판이 선수상 이름을 내던 표. 지금 판은 안 쓴다 — 부르는 쪽을 그대로 두려고 남겼다.</param>
    public static void Show(Window owner, Player player, int at, ItemTable? items = null)
    {
        if (at < 0 || at >= player.Ships.Count) return;
        new ShipInfoDialog(player, at) { Owner = owner }.ShowDialog();
    }
}
