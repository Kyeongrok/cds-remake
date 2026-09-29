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
///   탑재대포  컬버린                 ← 대포를 실었을 때만(0x0046CF17)
///   대포수  12
///                              [영상] [뒤로]
/// </code>
/// 줄 사이는 18점, 이름 칸은 왼쪽 20 · 값은 78 부터, 마스트는 218 · 돛 표시는 275 부터다.
/// 선수상을 달았으면 승원수 줄 오른쪽에 「뱃머리 %s」(0x00570E48, 짧은 이름 표 0x0054A0A0)를 쓰고
/// 그 아래에 선수상 아이템 그림(아이템 213 + 선수상)을 찍는다(0x0046CBF7 ~ 0x0046CC77 — 0x00406870 이
/// 원본 (0xF0, 0x70)에 120x120 한 장). 대포를 실었고 문수가 1 이상이면 맨 아래에 「탑재대포 %s」(0x00570E88)와
/// 「대포수 %d」(0x00570E98)를 더한다(0x0046CF17 ~ 0x0046CFD9). 둘 다 조건부라 선수상도 대포도 없는 배의
/// 갈무리에는 안 보였다.
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

    /// <summary>선수상 그림 자리 — 원본 (0xF0, 0x70)을 이 판의 잣대로 옮긴 것(승원수 줄에서 24 아래).</summary>
    private const double FigureheadLeft = 206, FigureheadTop = 96;

    /// <summary>「뱃머리」가 서는 자리 — 원본 0xD8 은 마스트(0xE8)보다 16 왼쪽이다.</summary>
    private const double BowLeft = MastLeft - 16;

    private ShipInfoDialog(Player player, int at, Engine.Game? game)
    {
        var ship = player.Ships[at];
        var rows = new StackPanel();
        var overlay = new Canvas { IsHitTestVisible = false };

        rows.Children.Add(Line("선명", ship.Name));
        var hull = Line("선체", ship.Hull.Name);
        Place(hull, Label("마스트"), MastLeft);
        Place(hull, Label(Sails(ship)), SailLeft);
        rows.Children.Add(hull);
        // 소유자는 제독의 이름(이름 칸)이다. 빌린 배도 제독 이름으로 적는다 — 원본에서 확인한 것은 제 배뿐이다.
        rows.Children.Add(Line("소유자", player.Given.Length > 0 ? player.Given : player.Name));

        rows.Children.Add(Gap(LineHeight));
        var crew = Line("승원수", $"{ship.Crew}");
        bool carved = Engine.Sea.Figureheads.Known(ship.Figurehead);
        if (carved)
        {
            // 0x0046CC46 — 「뱃머리 %s」, 0x0046CC77 — 0x00406870(x+0xF0, y+0x70, 0xD5 + 선수상).
            Place(crew, Label($"뱃머리  {Engine.Sea.Figureheads.ShortName(ship.Figurehead)}"), BowLeft);
            int item = Engine.Sea.Figureheads.ToItem(ship.Figurehead);
            if (game?.Items?.Find(item) is { HasPic: true } record
                && game.ItemPictures?.TryGetImage(record.Pic) is { } picture)
            {
                var image = new Image
                {
                    Source = picture,
                    Width = ItemArt.Width,
                    Height = ItemArt.Height,
                    SnapsToDevicePixels = true,
                };
                Canvas.SetLeft(image, FigureheadLeft);
                Canvas.SetTop(image, FigureheadTop);
                overlay.Children.Add(image);
            }
        }
        rows.Children.Add(crew);
        rows.Children.Add(Gauge("내구도", ship.Hp, ship.MaxHp));
        rows.Children.Add(Gauge("추진력", ship.Speed, ship.MaxSpeed));
        rows.Children.Add(Line("최대중량", $"{ship.Tonnage}", valueLeft: ValueLeft + 16));
        // 함대정보 짐용량과 같은 잣대다 — 포탑이 먹은 자리를 뺀 것(갈무리: 함대 10/128 · 이 판 128).
        rows.Children.Add(Line("최대용량", $"{ship.UsableCapacity}", valueLeft: ValueLeft + 16));

        // 대포를 실었고 문수가 1 이상일 때만 두 줄을 더한다(0x0046CF17 · 0x0046CF26).
        bool armed = Cannon.Of(ship.Gun) is not null && ship.Guns > 0;
        if (armed)
        {
            rows.Children.Add(Line("탑재대포", Cannon.Of(ship.Gun)!.Name, valueLeft: ValueLeft + 16));   // 0x0046CF79
            rows.Children.Add(Line("대포수", $"{ship.Guns}"));                                          // 0x0046CFCF
        }

        double height = BoardHeight + (armed ? LineHeight * 2 : 0);
        if (carved) height = Math.Max(height, FigureheadTop + ItemArt.Height + 4);
        var board = new Grid();
        board.Children.Add(rows);
        board.Children.Add(overlay);

        // 「영상」은 그 선체의 동영상 AVI\S%02d_0001.AVI 를 튼다 — 배 레코드 +0x28(선체)을 0x00422C10 에 넘기고
        // (0x0046D146 ~ 0x0046D15F), 누를 때마다 다시 튼다. 선체 0~7 에만 동영상이 있고 그 밖이면 아무 일도 없다
        // (0x00422C1B). 못 트는 배(등록해 넣은 배 · 파일 없음)면 단추를 흐려 둔다.
        string? movie = game == null ? null : MovieOf(game, ship.Hull);
        Build("", board, BoardWidth, height,
              new GameButton("영상", () => MoviePlayer.Play(GameUi.RootOf(this), movie, game?.Bgm)) { On = movie != null },
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
    /// <param name="game">「영상」 단추가 동영상을 찾고 곡을 멈추는 데 쓴다. 없으면 단추를 흐려 둔다.</param>
    public static void Show(Window owner, Player player, int at, ItemTable? items = null, Engine.Game? game = null)
    {
        if (at < 0 || at >= player.Ships.Count) return;
        new ShipInfoDialog(player, at, game) { Owner = owner }.ShowDialog();
    }

    /// <summary>그 선체의 동영상 자리 — 조선소와 같은 셈(올려 둔 것 먼저, 없으면 게임 폴더 원본). 없으면 null.</summary>
    private static string? MovieOf(Engine.Game game, Hull hull)
    {
        int n = MovieFiles.Hulls.IndexOf(hull.Name);
        return n < 0 ? null : MovieFiles.Resolve(game.Directory, MovieFiles.HullStem(n));
    }
}
