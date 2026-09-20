using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using CdsHelper.Game.Local.Helpers;

namespace CdsHelper.Game.UI.Views;

/// <summary>
/// 아이템 하나를 보여 주는 창 — 그림과 설명, 오른쪽 위에 속성과 효과.
/// </summary>
/// <remarks>
/// 게임 화면을 그대로 옮겼다. 다른 창과 달리 <b>남회색 바탕</b>이고 제목 띠가 없다 —
/// 맨 윗줄에 이름·속성·효과·닫기를 한 줄로 늘어놓는다.
/// <code>
///   바스타드소드      속성/병기      효과  48  X
///   ┌────────┐  독일, 스위스 등지에서 발달한
///   │  그림   │  단검. 보통때는 한손으로 사용
///   │120x120 │  하고, …
///   └────────┘                        확인
/// </code>
/// 시장에서 고른 뒤에도 뜨고, 소지품 일람에서도 뜬다. 그래서 어느 쪽에서 왔는지 모르게
/// 아이템 하나만 받는다.
///
/// 그림이 없는 아이템이 99개나 된다(<see cref="ItemTable.Record.HasPic"/>). 그럴 때는
/// 액자만 비워 두고 설명은 그대로 낸다 — 게임도 그림 자리를 비운다.
/// </remarks>
public sealed class ItemInfoDialog : GameWindow
{
    /// <summary>게임 화면에서 뽑은 남회색 바탕.</summary>
    private static readonly Brush Back = GameUi.InfoBack;

    /// <summary>글자색 — 바탕이 밝아 검은 글씨다.</summary>
    private static readonly Brush Ink = Freeze(Color.FromRgb(0x10, 0x10, 0x18));

    /// <summary>그림이 없을 때 액자를 채우는 색.</summary>
    private static readonly Brush Empty = Freeze(Color.FromRgb(0x48, 0x50, 0x66));

    private static SolidColorBrush Freeze(Color c)
    {
        var b = new SolidColorBrush(c);
        b.Freeze();
        return b;
    }

    private ItemInfoDialog(ItemTable.Record item, string description, ItemArt? art,
                           bool equipped)
    {
        Title = item.Name;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.Height;
        Width = 560;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        Background = Back;

        var head = HeadRow(item, equipped);

        var picture = new Border
        {
            Width = ItemArt.Width,
            Height = ItemArt.Height,
            Background = Empty,
            Margin = new Thickness(0, 0, 14, 0),
            VerticalAlignment = VerticalAlignment.Top,
        };
        var image = item.HasPic ? art?.TryGetImage(item.Pic) : null;
        if (image != null)
            picture.Child = new Image
            {
                Source = image,
                Width = ItemArt.Width,
                Height = ItemArt.Height,
                // 도트를 뭉개지 않는다 — 게임 그림은 원래 크기 그대로 낸다.
                SnapsToDevicePixels = true,
            };

        // 게임 글꼴은 한 줄을 그림 한 장으로 찍는다 — 접는 것은 이쪽 몫이다.
        var text = new StackPanel { Margin = new Thickness(0, 2, 0, 0) };
        foreach (string line in GameUi.Wrap(
                     description.Length > 0 ? description : "(설명이 없다)", TextWidth))
            text.Children.Add(Label(line));

        var body = new DockPanel { LastChildFill = true, Margin = new Thickness(14, 12, 14, 8) };
        DockPanel.SetDock(picture, Dock.Left);
        body.Children.Add(picture);
        body.Children.Add(text);

        var ok = GameUi.PushButton("확인", Close, 88);
        ok.HorizontalAlignment = HorizontalAlignment.Right;
        ok.Margin = new Thickness(0, 0, 14, 12);

        var stack = new StackPanel();
        stack.Children.Add(head);
        stack.Children.Add(body);
        stack.Children.Add(ok);
        Content = GameUi.InfoFrame(stack, Back);

        GameUi.EnableDrag(this, head);
        KeyDown += (_, e) => { if (e.Key is Key.Escape or Key.Enter or Key.Space) Close(); };
    }

    /// <summary>맨 윗줄 — 이름 · 속성 · 효과 · 닫기.</summary>
    /// <summary>
    /// 머리 줄 — 이름 · 속성 · 효과, 그리고 <b>「장비중」</b>.
    /// </summary>
    /// <remarks>
    /// 게임(<c>0x0046E6D7</c>~<c>0x0046E772</c>)은 소지품을 훑어 <b>같은 갈래에서 효과가 가장
    /// 센 것</b> 하나를 골라 두고, 지금 보는 것이 그것이면 효과 밑에 「장비중」(<c>0x005710C8</c>)
    /// 을 찍는다. 값이 같으면 <b>먼저 든 것</b>이 이긴다(견줌이 <c>&gt;</c> 다).
    /// </remarks>
    private FrameworkElement HeadRow(ItemTable.Record item, bool equipped)
    {
        var row = new DockPanel { LastChildFill = true, Margin = new Thickness(12, 8, 8, 4) };

        // 닫기는 게임 조각으로 그린 공용 것이다 — 창마다 손으로 짓지 않는다.
        var close = GameUi.CloseBox(Close);
        close.VerticalAlignment = VerticalAlignment.Center;
        DockPanel.SetDock(close, Dock.Right);
        row.Children.Add(close);

        var effect = new StackPanel { Margin = new Thickness(0, 0, 12, 0) };
        effect.Children.Add(Label($"효과  {item.Effect}"));
        if (equipped)
        {
            var worn = Label("장비중");
            worn.HorizontalAlignment = HorizontalAlignment.Right;
            effect.Children.Add(worn);
        }
        DockPanel.SetDock(effect, Dock.Right);
        row.Children.Add(effect);

        var kind = Label($"속성/{item.CategoryName}");
        kind.Margin = new Thickness(0, 0, 24, 0);
        DockPanel.SetDock(kind, Dock.Right);
        row.Children.Add(kind);

        row.Children.Add(Label(item.Name));
        return row;
    }

    /// <summary>설명 글이 놓이는 자리의 폭(점). 그림 자리를 뺀 나머지다.</summary>
    private const double TextWidth = 560 - ItemArt.Width - 60;

    /// <summary>
    /// 판 위의 글씨. <b>게임 글꼴</b>로 찍는다 — 바탕이 밝아 검은 글씨다.
    /// </summary>
    private static GameUi.GameLabel Label(string text) => new(GameFont.BlackColor)
    {
        Text = text,
        Bold = false,
        FallbackBrush = Ink,
        HorizontalAlignment = HorizontalAlignment.Left,
        VerticalAlignment = VerticalAlignment.Center,
    };

    /// <summary>아이템 하나를 보여 준다. 확인을 누르거나 ESC 로 닫는다.</summary>
    /// <param name="equipped">「장비중」을 찍을지 — <see cref="IsEquipped"/> 가 가른다.</param>
    public static void Show(Window owner, ItemTable.Record item, string description, ItemArt? art,
                            bool equipped = false) =>
        new ItemInfoDialog(item, description, art, equipped) { Owner = owner }.ShowDialog();

    /// <summary>
    /// 창을 띄워 둔 채 <paramref name="during"/> 을 하고 <b>바로 닫는다</b> — 고문서를 못 읽을 때다
    /// (<c>0x0046E97A</c> 로 창을 띄우고 말을 낸 뒤 <c>0x0046EBA8</c> 로 닫는다. 창 안의 고리는 안 돈다).
    /// </summary>
    public static void ShowWhile(Window owner, ItemTable.Record item, string description, ItemArt? art,
                                 bool equipped, Action<Window> during)
    {
        var dialog = new ItemInfoDialog(item, description, art, equipped) { Owner = owner };
        dialog.Show();
        try { during(dialog); }
        finally { dialog.Close(); }
    }

    /// <summary>
    /// 지닌 것 가운데 <b>그 갈래에서 가장 센 것</b>인지 — 이것 하나에만 「장비중」이 붙는다.
    /// </summary>
    /// <remarks>게임 <c>0x0046E6D7</c> 고리 그대로다. 같은 값이면 먼저 든 것이 이긴다.</remarks>
    public static bool IsEquipped(ItemTable.Record item, IEnumerable<int> bag, ItemTable? table)
    {
        if (table == null) return false;

        int best = -1, bestEffect = int.MinValue;
        foreach (int id in bag)
        {
            if (table.Find(id) is not { } one || one.Category != item.Category) continue;
            if (one.Effect <= bestEffect) continue;
            bestEffect = one.Effect;
            best = one.Id;
        }
        return best == item.Id;
    }
}
