using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using CdsHelper.Game.Local.Helpers;
using CdsHelper.Support.Local.Models;

namespace CdsHelper.Game.UI.Views;

/// <summary>
/// 교역품 하나를 보여 주는 창 — 그림과 이름·분류·개체중량.
/// </summary>
/// <remarks>
/// 도시정보 창에서 특산품 단추를 누르면 뜬다. <see cref="ItemInfoDialog"/> 와 같은 남회색
/// 바탕이지만 오른쪽에 설명문 대신 <b>세 줄</b>만 놓는다 — 게임도 그렇다.
/// <code>
///   ┌────────┐  대포
///   │  그림   │  분류     무기
///   │120x120 │  개체중량  20
///   └────────┘                 [취소]
/// </code>
/// 함대정보 짐 판에서 열면(0x0046FD1E — 짐 칸의 기한을 창 +0xAC 에 넘긴다) 썩는 짐일 때
/// 「내구도」 줄이 더 붙는다(0x0046D3A2 ~ 0x0046D4D3). 기한이 −1(도시 특산품, 0x004709B1)이거나
/// 0xD2(안 썩음)면 안 붙는다.
/// <code>
///   (0x88, 0x38)   내구도
///   (0x88, 0x5C)   막대 128x8 — 기한 / (수명 x 30)          0x0046C8E0
///   (0x110, 0x58)  "%d/%d" — 기한 / 30 , 수명(달)            0x00560F64
/// </code>
/// 그림은 아이템과 한 파일에 있다(<see cref="ItemArt"/>) — 교역품 70가지가 134~203 에
/// 이름 차례 그대로 놓여 있다.
/// </remarks>
public sealed class GoodsInfoDialog : GameWindow
{
    /// <summary>게임 화면에서 뽑은 남회색 바탕. 아이템 창과 같다.</summary>
    private static readonly Brush Back = GameUi.InfoBack;
    private static readonly Brush Ink = Freeze(Color.FromRgb(0x10, 0x10, 0x18));
    private static readonly Brush Empty = Freeze(Color.FromRgb(0x48, 0x50, 0x66));

    private static SolidColorBrush Freeze(Color c)
    {
        var b = new SolidColorBrush(c);
        b.Freeze();
        return b;
    }

    /// <summary>막대 크기(0x0046D440 의 0x80 x 8).</summary>
    private const double BarWidth = 128, BarHeight = 8;

    private static readonly Brush BarBack = Freeze(Color.FromRgb(0, 0, 0));
    private static readonly Brush BarFill = Freeze(Color.FromRgb(135, 21, 10));

    private GoodsInfoDialog(GoodsTable.Goods goods, string category, ItemArt? art, int shelf)
    {
        Title = goods.Name;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.WidthAndHeight;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        Background = Back;

        var picture = new Border
        {
            Width = ItemArt.Width,
            Height = ItemArt.Height,
            Background = Empty,
            Margin = new Thickness(14, 14, 16, 14),
            VerticalAlignment = VerticalAlignment.Top,
        };
        var image = art?.TryGetImage(goods.Pic);
        if (image != null)
            picture.Child = new Image
            {
                Source = image,
                Width = ItemArt.Width,
                Height = ItemArt.Height,
                SnapsToDevicePixels = true,
            };

        var rows = new StackPanel { Margin = new Thickness(0, 14, 14, 0), MinWidth = 220 };
        rows.Children.Add(Row(goods.Name, ""));
        rows.Children.Add(Row("분류", category));
        rows.Children.Add(Row("개체중량", $"{goods.Weight}"));
        if (shelf >= 0 && shelf != Player.NeverSpoils) rows.Children.Add(Shelf(shelf, goods.Life));

        // 닫기는 게임 조각으로 그린 공용 것이다 — 창마다 손으로 짓지 않는다.
        var close = GameUi.CloseBox(Close);
        close.HorizontalAlignment = HorizontalAlignment.Right;
        close.VerticalAlignment = VerticalAlignment.Top;
        close.Margin = new Thickness(0, 12, 12, 0);

        // 취소는 게임 띠 단추다 — 도시 정보 창의 취소(0x004707AB, 48x24)와 같은 것이다.
        var cancel = new GameButton("취소", Close, width: 48)
        {
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 0, 14, 12),
        };

        var right = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(close, Dock.Top);
        right.Children.Add(close);
        DockPanel.SetDock(cancel, Dock.Bottom);
        right.Children.Add(cancel);
        right.Children.Add(rows);

        var body = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(picture, Dock.Left);
        body.Children.Add(picture);
        body.Children.Add(right);
        Content = GameUi.InfoFrame(body, Back);

        GameUi.EnableDrag(this, body);
        KeyDown += (_, e) => { if (e.Key is Key.Escape or Key.Enter or Key.Space) Close(); };
    }

    /// <summary>
    /// 「내구도」 두 줄 — 이름, 그 아래 막대와 「기한/30 / 수명」(0x0046D3F1 ~ 0x0046D4D3).
    /// 막대는 기한을 수명 x 30 에 견준다(0x0046D41E ~ 0x0046D460). 달수는 내림이다(<c>idiv 0x1E</c>).
    /// </summary>
    private static FrameworkElement Shelf(int shelf, int life)
    {
        int full = Math.Max(1, life * Player.DaysPerMonth);
        var bar = new Border
        {
            Width = BarWidth,
            Height = BarHeight,
            Background = BarBack,
            VerticalAlignment = VerticalAlignment.Center,
            Child = new Border
            {
                Width = BarWidth * Math.Clamp((double)shelf / full, 0, 1),
                Background = BarFill,
                HorizontalAlignment = HorizontalAlignment.Left,
            },
        };
        var number = Label($"{shelf / Player.DaysPerMonth}/{life}");
        number.Margin = new Thickness(8, 0, 0, 0);

        var gauge = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 4) };
        gauge.Children.Add(bar);
        gauge.Children.Add(number);

        var both = new StackPanel();
        both.Children.Add(Row("내구도", " "));
        both.Children.Add(gauge);
        return both;
    }

    /// <summary>줄 하나 — 이름과 값. 값이 없으면 이름만 굵게 낸다(맨 윗줄).</summary>
    private static FrameworkElement Row(string name, string value)
    {
        var line = new DockPanel { LastChildFill = true, Margin = new Thickness(0, 0, 0, 4) };
        var label = Label(name, value.Length > 0 ? 80 : double.NaN);
        DockPanel.SetDock(label, Dock.Left);
        line.Children.Add(label);

        if (value.Length > 0) line.Children.Add(Label(value));
        return line;
    }

    /// <summary>
    /// 판 위의 글씨. <b>게임 글꼴</b>로 찍는다 — 바탕이 밝아 검은 글씨다.
    /// </summary>
    private static GameUi.GameLabel Label(string text, double width = double.NaN) =>
        new(GameFont.BlackColor)
        {
            Text = text,
            Bold = false,
            FallbackBrush = Ink,
            Width = width,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center,
        };

    /// <summary>교역품 하나를 보여 준다.</summary>
    /// <summary>교역품 번호로 띄운다 — 표에 없으면 아무것도 안 한다.</summary>
    /// <param name="shelf">짐 칸의 기한(날). 함대정보 짐 판에서만 넘긴다 — −1 이면 「내구도」가 없다.</param>
    public static void Show(Window owner, Engine.Game game, int kind, int shelf = -1)
    {
        if (game.Goods is not { } table || table.Find(kind) is not { } goods) return;
        Show(owner, goods, table.CategoryName(goods.Category), game.ItemPictures, shelf);
    }

    public static void Show(Window owner, GoodsTable.Goods goods, string category, ItemArt? art, int shelf = -1) =>
        new GoodsInfoDialog(goods, category, art, shelf) { Owner = owner }.ShowDialog();
}
