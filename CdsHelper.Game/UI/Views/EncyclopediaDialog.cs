using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CdsHelper.Game.Local.Helpers;
using CdsHelper.Support.Local.Models;

namespace CdsHelper.Game.UI.Views;

/// <summary>
/// 자택 → <b>백과사전을 본다</b>. 책장에 갈래마다 한 권씩 꽂혀 있고, 발견한 것이
/// 그 갈래 책에 <b>한 쪽씩</b> 쌓인다.
/// </summary>
/// <remarks>
/// 게임의 갈래는 여덟이고 이름표가 EXE 에 그대로 있다(<c>0x00560C60</c>).
/// <code>
///   지리 · 역사 · 보물 · 종교 · 교역품 · 미신 · 생물 · 민족
/// </code>
/// 발견물 표의 <c>+0x04</c> 가 이 차례를 가리키므로(<see cref="DiscoveryTable.CategoryNames"/>)
/// 갈래는 새로 정할 것이 없다 — 발견물이 곧 제 책을 안다.
/// <code>
///   0x00471B5A  갈래 이름표 0x560C60[갈래]
///   0x00471B62  창 제목 "백과사전 (%s)"
///   0x00471B7F  갈래마다 72바이트 레코드 — 0x581120 + 갈래 x 72
///   0x00471BAA  책 이름표 "「%s」"
/// </code>
/// 책장 그림과 책등은 도서관 열람 화면과 같은 <c>BOOKSHEL.CDS</c> 다
/// (<see cref="BookShelf"/>). 게임 갈무리에서 백과사전 책등은 <b>빨강</b>이다.
///
/// 책등을 누르면 그 갈래 책이 <c>ENC.CDS</c> 화면으로 펴진다(<see cref="EncyclopediaPageDialog"/>,
/// <c>0x00470F60</c> → <c>0x00471FB0</c> → <c>0x00471FF0</c> → <c>0x00462E60</c>). 책등 풍선은
/// 「백과사전 (%s)」(<c>0x0055AD10</c>)이다.
///
/// 책장은 <c>0x004722F0</c> 이 짓는다 — 갈래마다 책 한 권을 꽂고 곧바로 <b>빈 책등</b>을
/// <c>보고 수 x 5 / 쪽 수</c>(0~5, <c>0x00472430</c>) 개 더 꽂아 많이 보고한 갈래일수록 두꺼워 보인다.
/// 자리는 한 줄에 17칸이다(<c>0x00471550</c>). 책등 빛(<c>0x004716A0</c>, 모드 1)은 빈 책등 0(초록),
/// 갈래 책은 보고한 것이 있으면 1(파랑) · 없으면 2(빨강)이다.
/// </remarks>
public sealed class EncyclopediaDialog : GameWindow
{
    /// <summary>선반 셋의 윗변 — 도서관 서가와 같다.</summary>
    private static readonly double[] ShelfTops = [66, 146.5, 227];

    /// <summary>한 선반의 칸 수(<c>0x00471550</c> 의 <c>÷ 0x11</c>).</summary>
    private const int SlotsPerShelf = 17;

    /// <summary>첫 자리와 자리 사이. 도서관 서가와 같은 치수다(책등이 반쯤 겹쳐 꽂힌다).</summary>
    private const double FirstSlotX = 30, SlotStep = 16.1;

    /// <summary>책등 빛깔 — 0 초록(빈 책등) · 1 파랑(보고한 것이 있다) · 2 빨강(없다).</summary>
    private const int SpineGreen = 0, SpineBlue = 1, SpineRed = 2;

    /// <summary>닫기 조각의 크기와 양피지 모서리에서 떨어진 거리. 도서관 것과 같다.</summary>
    private const double CloseInset = 10;

    private readonly Engine.Game _game;
    private readonly Canvas _layer = new();
    private readonly Border _tag;
    private readonly GameUi.GameLabel _tagText;
    private readonly int _scale;

    private EncyclopediaDialog(BookShelf art, Engine.Game game, int scale)
    {
        _game = game;
        _scale = scale;

        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.WidthAndHeight;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        Background = GameUi.Back;

        var shelf = new Image
        {
            Source = ToBitmap(art.Shelf, BookShelf.ShelfWidth, BookShelf.ShelfHeight),
            Width = BookShelf.ShelfWidth * scale,
            Height = BookShelf.ShelfHeight * scale,
            Stretch = Stretch.Fill,
        };
        RenderOptions.SetBitmapScalingMode(shelf, GameUi.SpriteScaling);

        var box = new Grid
        {
            Width = BookShelf.ShelfWidth * scale,
            Height = BookShelf.ShelfHeight * scale,
        };
        box.Children.Add(shelf);
        box.Children.Add(_layer);

        // 갈래 책 뒤에 보고한 비율만큼 빈 책등을 붙여 꽂는다(0x004722F0).
        var spines = art.Spines.Select(p => ToBitmap(p, BookShelf.SpineWidth, BookShelf.SpineHeight)).ToArray();
        int slot = 0;
        var table = game.Discoveries!.Table;
        for (int i = 0; i < DiscoveryTable.CategoryNames.Length; i++)
        {
            var pages = table.Discoveries.Where(r => r.Category == i && !r.Indirect).ToList();
            int reported = pages.Count(r => game.Player.HasAnnounced(r.Id));
            AddBook(i, slot++, spines[reported > 0 ? SpineBlue : SpineRed]);
            int filler = pages.Count > 0 && reported <= pages.Count ? reported * 5 / pages.Count : 0;
            for (int k = 0; k < filler; k++) AddBook(-1, slot++, spines[SpineGreen]);
        }

        // 책등 밑에 뜨는 이름표 — 게임도 「지리」 처럼 낫표를 두른다.
        _tagText = new GameUi.GameLabel(GameFont.WhiteColor) { FallbackBrush = GameUi.Text };
        _tag = new Border
        {
            Background = GameUi.Back,
            BorderBrush = GameUi.Edge,
            BorderThickness = new Thickness(1),
            Padding = new Thickness(4, 1, 4, 1),
            Visibility = Visibility.Collapsed,
            Child = _tagText,
        };
        _layer.Children.Add(_tag);

        var close = GameUi.CloseBox(Close, scale);
        close.Margin = new Thickness(0);
        close.MouseLeftButtonDown += (_, e) => { e.Handled = true; Close(); };
        Canvas.SetLeft(close, (BookShelf.ShelfWidth - GameUi.CloseBoxSize - CloseInset) * scale);
        Canvas.SetTop(close, CloseInset * scale);
        _layer.Children.Add(close);

        Content = GameUi.DialogEdge(box);
        GameUi.EnableDrag(this, box);

        KeyDown += (_, e) => { if (e.Key is Key.Escape) Close(); };
        MouseRightButtonUp += (_, _) => Close();
    }

    /// <summary>갈래 한 권을 서가에 꽂는다.</summary>
    private void AddBook(int category, int slot, BitmapSource spine)
    {
        if (slot >= ShelfTops.Length * SlotsPerShelf) return;
        double x = FirstSlotX + slot % SlotsPerShelf * SlotStep;
        double top = ShelfTops[slot / SlotsPerShelf];

        var image = new Image
        {
            Source = spine,
            Width = BookShelf.SpineWidth * _scale,
            Height = BookShelf.SpineHeight * _scale,
            Stretch = Stretch.Fill,
        };
        RenderOptions.SetBitmapScalingMode(image, GameUi.SpriteScaling);
        Canvas.SetLeft(image, x * _scale);
        Canvas.SetTop(image, top * _scale);
        _layer.Children.Add(image);

        // 빈 책등(-1)은 눌리지 않는다(0x00470F60 이 -1 을 거른다).
        if (category < 0) return;
        image.Cursor = Cursors.Hand;
        image.MouseEnter += (_, _) => ShowTag(category, x, top);
        image.MouseLeave += (_, _) => _tag.Visibility = Visibility.Collapsed;
        image.MouseLeftButtonDown += (_, e) => e.Handled = true;
        image.MouseLeftButtonUp += (_, e) => { e.Handled = true; Read(category); };
    }

    /// <summary>책등 밑에 「백과사전 (갈래)」를 띄운다(<c>0x004718C0</c>).</summary>
    private void ShowTag(int category, double x, double top)
    {
        _tagText.Text = $"백과사전 ({DiscoveryTable.CategoryNames[category]})";
        _tag.Visibility = Visibility.Visible;
        _tag.UpdateLayout();

        double w = _tag.ActualWidth > 0 ? _tag.ActualWidth : 120;
        double left = (x + BookShelf.SpineWidth / 2.0) * _scale - w / 2;
        Canvas.SetLeft(_tag, Math.Clamp(left, 0, Math.Max(0, BookShelf.ShelfWidth * _scale - w)));
        Canvas.SetTop(_tag, (top + BookShelf.SpineHeight + 2) * _scale);
    }

    /// <summary>한 권을 편다.</summary>
    private void Read(int category) => EncyclopediaPageDialog.Show(this, _game, category);

    private static BitmapSource ToBitmap(uint[] bgra, int width, int height)
    {
        var bmp = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null,
                                      bgra, width * 4);
        bmp.Freeze();
        return bmp;
    }

    /// <summary>백과사전 책장을 연다. 그림이나 발견물 표가 없으면 그렇다고 이른다.</summary>
    public static void Show(Window owner, Engine.Game game)
    {
        if (game.Discoveries?.Table == null)
        {
            NoticeDialog.Show(owner, "발견물 표를 읽지 못했다.");
            return;
        }

        var art = BookShelf.Open(game.Directory);
        if (art == null)
        {
            NoticeDialog.Show(owner, $"책장을 열지 못했다 — {BookShelf.LastError}");
            return;
        }

        int scale = owner.ActualHeight > 800 ? 2 : 1;
        new EncyclopediaDialog(art, game, scale) { Owner = owner }
            .ShowDialog();
    }
}
