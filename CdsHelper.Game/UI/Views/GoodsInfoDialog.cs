using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using CdsHelper.Game.Local.Helpers;

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

    private GoodsInfoDialog(GoodsTable.Goods goods, string category, ItemArt? art)
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

        // 닫기는 게임 조각으로 그린 공용 것이다 — 창마다 손으로 짓지 않는다.
        var close = GameUi.CloseBox(Close);
        close.HorizontalAlignment = HorizontalAlignment.Right;
        close.VerticalAlignment = VerticalAlignment.Top;
        close.Margin = new Thickness(0, 12, 12, 0);

        var cancel = GameUi.PushButton("취소", Close, 78);
        cancel.HorizontalAlignment = HorizontalAlignment.Right;
        cancel.Margin = new Thickness(0, 0, 14, 12);

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
    public static void Show(Window owner, GoodsTable.Goods goods, string category, ItemArt? art) =>
        new GoodsInfoDialog(goods, category, art) { Owner = owner }.ShowDialog();
}
