using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CdsHelper.Game.Local.Helpers;

namespace CdsHelper.Game.UI.Views;

/// <summary>
/// 백과사전 한 권 — 한 쪽에 발견물 하나다(<c>0x00462E60</c>, 그림은 <c>ENC.CDS</c>).
/// </summary>
/// <remarks>
/// <b>쪽은 갈래의 발견물 전부</b>다 — 발견물 표 <c>+0x20</c> 이 0 인 것(판을 열 때 인스턴스 깃발
/// <c>0x04</c> 가 서는 것, <c>0x004AA97B</c>)을 번호 차례로 늘어놓는다. 보고·발표를 안 한 쪽은
/// 쪽 번호만 있는 <b>빈 쪽</b>이다(발견자 칸 2 가 비면 더 안 그린다, <c>0x00462910</c>).
/// <code>
///   창 544x304 — 책 틀, 왼쪽 면 (16,8), 오른쪽 면 (272,8) 256x288
///   "-%d-"        (쪽+1)  y=280, 왼쪽 면 가운데
///   이름          (288,16)  224x32   — 붙인 이름이 먼저(0x004AAB30)
///   설명          (288,48)  224x192  — 발견물 설명문(0x0057AA78)
///   교역품 그림   (32,16)   120x120  — 교역품 갈래이고 아이템을 주면(노예 229 는 빼고)
///   "발견자"      (288,240) · 이름 (352,240) — 「·」 앞까지
///   "발견년  %4d년 %d월"  (288,272) — 발견자가 제가 찾은 것일 때만
///   앞장 (16,280) · 다음장 (508,280) 16x16 · "삽화" (192,264) 48x24
/// </code>
/// 「삽화」는 보고한 쪽이고 동영상·움직이는 그림·그림 가운데 하나라도 있을 때만 눌린다(<c>0x004AAEC0</c>).
/// 누르면 동영상 → 움직이는 그림(<c>DISCOVER.CDS</c>) → 그림(<c>DSTILL.CDS</c>) 차례로 하나를 튼다
/// (<c>0x004AAF30</c>). 쪽을 넘기는 소리는 없다.
/// </remarks>
public sealed class EncyclopediaPageDialog : GameWindow
{
    private const int FrameWidth = 544, FrameHeight = 304;
    private const double LeftPageX = 16, RightPageX = 272, PageY = 8;
    private const double NameX = 288, NameY = 16, NameHeight = 32;
    private const double TextX = 288, TextY = 48, TextHeight = 192, TextWidth = 224;
    private const double LineHeight = 16;
    private const double FinderLabelX = 288, FinderNameX = 352, FinderY = 240;
    private const double FoundX = 288, FoundY = 272;
    private const double NumberY = 280;
    private const double PreviousX = 16, NextX = 508, CornerY = 280, CornerSize = 16;
    private const double PlateX = 192, PlateY = 264, PlateWidth = 48, PlateHeight = 24;
    private const double ItemX = 32, ItemY = 16;
    private const double HoverWidth = 96, HoverHeight = 24;
    private const double CellWidth = 8;

    /// <summary>교역품 갈래 번호.</summary>
    private const int GoodsCategory = 4;

    /// <summary>교역품이어도 그림을 안 싣는 발견물 — 노예(<c>0x00462A9B</c> 의 <c>cmp 0xE5</c>).</summary>
    private const int NoPictureDiscovery = 229;

    private readonly Engine.Game _game;
    private readonly EncyclopediaArt _art;
    private readonly IReadOnlyList<DiscoveryTable.Record> _pages;
    private readonly int _scale;
    private readonly Canvas _canvas;
    private readonly Canvas _layer = new();
    private readonly FrameworkElement _previous, _next;
    private readonly Popup _hover;
    private readonly GameUi.GameLabel _hoverText;
    private int _index;

    private EncyclopediaPageDialog(Engine.Game game, EncyclopediaArt art,
                                   IReadOnlyList<DiscoveryTable.Record> pages, int scale)
    {
        _game = game;
        _art = art;
        _pages = pages;
        _scale = scale;

        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.WidthAndHeight;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        Background = GameUi.Back;

        _canvas = new Canvas
        {
            Width = FrameWidth * scale,
            Height = FrameHeight * scale,
            Background = GameUi.Back,
        };
        _canvas.Children.Add(Picture(EncyclopediaArt.Frame, 0, 0));
        _canvas.Children.Add(_layer);

        _previous = Corner(EncyclopediaArt.PreviousCorner, PreviousX, () => Turn(_index - 1));
        _next = Corner(EncyclopediaArt.NextCorner, NextX, () => Turn(_index + 1));

        var close = GameUi.CloseBox(Close, scale);
        close.Margin = new Thickness(0);
        close.MouseLeftButtonDown += (_, e) => { e.Handled = true; Close(); };
        Canvas.SetLeft(close, (FrameWidth - 32) * scale);
        Canvas.SetTop(close, 16 * scale);
        Panel.SetZIndex(close, 10);
        _canvas.Children.Add(close);

        // 「앞장」「다음장」 풍선(0x00462CB0) — 도서관 펼친 책과 같은 자리다.
        var (tag, text) = GameUi.HoverTag();
        tag.Visibility = Visibility.Visible;
        tag.Width = HoverWidth * scale;
        tag.Height = HoverHeight * scale;
        _hoverText = text;
        _hover = new Popup
        {
            PlacementTarget = _canvas,
            Placement = PlacementMode.Relative,
            AllowsTransparency = false,
            Focusable = false,
            Child = tag,
        };

        Content = _canvas;

        _canvas.MouseLeftButtonDown += OnBodyDown;
        _canvas.MouseMove += (_, e) => Hover(e.GetPosition(_canvas));
        _canvas.MouseLeave += (_, _) => _hover.IsOpen = false;
        Deactivated += (_, _) => _hover.IsOpen = false;
        Closed += (_, _) => _hover.IsOpen = false;
        KeyDown += OnKey;
        MouseRightButtonUp += (_, _) => Close();

        ShowPage();
    }

    private bool CanGoBack => _index > 0;
    private bool CanGoOn => _index < _pages.Count - 1;

    private void Turn(int to)
    {
        if (to < 0 || to >= _pages.Count || to == _index) return;
        _hover.IsOpen = false;
        _index = to;
        ShowPage();
    }

    /// <summary>지금 쪽을 그린다(<c>0x004627F0</c> 바탕 · <c>0x00462910</c> 글).</summary>
    private void ShowPage()
    {
        _previous.Visibility = CanGoBack ? Visibility.Visible : Visibility.Collapsed;
        _next.Visibility = CanGoOn ? Visibility.Visible : Visibility.Collapsed;
        _layer.Children.Clear();

        var row = _pages[_index];
        var player = _game.Player;
        bool goods = row.Category == GoodsCategory && row.ItemId >= 0;

        // 교역품이고 아이템을 주는 것을 찾았으면 왼쪽 면이 그림 틀 벌이다.
        _layer.Children.Add(Picture(goods && player.HasFound(row.Id)
                                        ? EncyclopediaArt.LeftPageFramed : EncyclopediaArt.LeftPage,
                                    LeftPageX, PageY));
        _layer.Children.Add(Picture(EncyclopediaArt.RightPage, RightPageX, PageY));

        string number = $"-{_index + 1}-";
        Ink(number, LeftPageX + (256 - number.Length * CellWidth) / 2, NumberY);

        bool reported = player.HasAnnounced(row.Id);
        bool hasArt = row.Movie >= 0 || row.Clip >= 0 || row.Picture >= 0;
        AddPlate(reported && hasArt ? () => Illustrate(row) : null);
        if (!reported) return;   // 발견자가 없으면 빈 쪽이다

        string name = player.NamedDiscoveries.TryGetValue(row.Id, out var named) && named.Length > 0
            ? named : row.Name;
        Block(name, NameX, NameY, NameHeight);
        Block(_game.DiscoveryText?.Of(row.Id) ?? "", TextX, TextY, TextHeight);

        if (goods && row.Id != NoPictureDiscovery
            && _game.Items?.Find(row.ItemId) is { HasPic: true } item
            && _game.ItemPictures?.TryGetImage(item.Pic) is { } picture)
        {
            var image = new Image
            {
                Source = picture,
                Width = ItemArt.Width * _scale,
                Height = ItemArt.Height * _scale,
                IsHitTestVisible = false,
            };
            RenderOptions.SetBitmapScalingMode(image, GameUi.SpriteScaling);
            Canvas.SetLeft(image, ItemX * _scale);
            Canvas.SetTop(image, ItemY * _scale);
            _layer.Children.Add(image);
        }

        // 발견자 — 이름은 「·」 앞까지만 쓴다(0x005499EC).
        string finder = player.Name;
        int dot = finder.IndexOf('·');
        if (dot > 0) finder = finder[..dot];
        Ink("발견자", FinderLabelX, FinderY);
        Ink(finder, FinderNameX, FinderY);

        // 발견년 — 찾은 사람이 보고한 사람과 같을 때만 그 칸의 해·달을 쓴다.
        if (player.FoundDateOf(row.Id) is { } found)
            Ink($"발견년  {found.Year,4}년 {found.Month}월", FoundX, FoundY);
    }

    /// <summary>「삽화」 판 — 못 누르면 흐리다.</summary>
    private void AddPlate(Action? run)
    {
        var plate = GameUi.PushButton("삽화", run, PlateWidth * _scale);
        plate.Margin = new Thickness(0);
        plate.Height = PlateHeight * _scale;
        Canvas.SetLeft(plate, PlateX * _scale);
        Canvas.SetTop(plate, PlateY * _scale);
        Panel.SetZIndex(plate, 5);
        _layer.Children.Add(plate);
    }

    /// <summary>삽화를 튼다 — 동영상 → 움직이는 그림 → 그림(<c>0x004AAF30</c>).</summary>
    private void Illustrate(DiscoveryTable.Record row)
    {
        _hover.IsOpen = false;
        if (row.Movie >= 0)
            MoviePlayer.Play(this, DiscoveryDialog.MovieOf(_game.Directory, row.Movie));
        else if (row.Clip >= 0)
            DiscoveryClipPlayer.Play(this, _game.Clips, row.Clip);
        else if (row.Picture >= 0)
            DiscoveryDialog.ShowPicture(this, _game.Stills, row.Picture);
    }

    /// <summary>창 몸통 — 끌면 옮기고, 제자리에서 떼면 왼쪽 면은 앞장·오른쪽 면은 다음장(<c>0x00462BF0</c>).</summary>
    private void OnBodyDown(object sender, MouseButtonEventArgs e)
    {
        var at = e.GetPosition(_canvas);
        _hover.IsOpen = false;

        double left = Left, top = Top;
        if (Mouse.LeftButton == MouseButtonState.Pressed) DragMove();
        if (Left != left || Top != top) return;

        if (at.Y < PageY * _scale) return;
        if (at.X > _canvas.Width / 2) { if (CanGoOn) Turn(_index + 1); }
        else if (CanGoBack) Turn(_index - 1);
    }

    private void Hover(Point at)
    {
        bool rightHalf = at.X > _canvas.Width / 2;
        if (at.Y < PageY * _scale || !(rightHalf ? CanGoOn : CanGoBack))
        {
            _hover.IsOpen = false;
            return;
        }
        _hoverText.Text = rightHalf ? "다음장" : "앞장";
        _hover.HorizontalOffset = rightHalf ? (FrameWidth - HoverWidth) * _scale : 0;
        _hover.VerticalOffset = FrameHeight * _scale;
        _hover.IsOpen = true;
    }

    private void OnKey(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Left:
                e.Handled = true;
                if (CanGoBack) Turn(_index - 1);
                break;
            case Key.Right:
                e.Handled = true;
                if (CanGoOn) Turn(_index + 1);
                break;
            case Key.Escape:
                e.Handled = true;
                Close();
                break;
        }
    }

    private FrameworkElement Corner(EncyclopediaArt.Piece piece, double x, Action run)
    {
        var image = Picture(piece, x, CornerY);
        image.IsHitTestVisible = true;
        image.Cursor = Cursors.Hand;
        image.MouseLeftButtonDown += (_, e) => { e.Handled = true; _hover.IsOpen = false; run(); };
        Panel.SetZIndex(image, 5);
        _canvas.Children.Add(image);
        return image;
    }

    private Image Picture(EncyclopediaArt.Piece piece, double x, double y)
    {
        var image = new Image
        {
            Width = piece.Width * _scale,
            Height = piece.Height * _scale,
            IsHitTestVisible = false,
        };
        var bmp = BitmapSource.Create(piece.Width, piece.Height, 96, 96, PixelFormats.Bgra32, null,
                                      _art.Bgra(piece), piece.Width * 4);
        bmp.Freeze();
        image.Source = bmp;
        RenderOptions.SetBitmapScalingMode(image, GameUi.SpriteScaling);
        RenderOptions.SetEdgeMode(image, EdgeMode.Aliased);
        Canvas.SetLeft(image, x * _scale);
        Canvas.SetTop(image, y * _scale);
        return image;
    }

    /// <summary>상자 안에 끊어 앉힌다 — 넘치는 줄은 버린다.</summary>
    private void Block(string text, double x, double y, double height)
    {
        double at = y;
        foreach (string line in Wrap(text, TextWidth))
        {
            if (at + LineHeight > y + height) break;
            Ink(line, x, at);
            at += LineHeight;
        }
    }

    private void Ink(string line, double x, double y)
    {
        if (line.Length == 0) return;
        var label = new GameUi.GameLabel(GameFont.BlackColor, GameUi.ItemTextHeight * _scale)
        {
            Text = line,
            Bold = false,
            FallbackBrush = Brushes.Black,
            IsHitTestVisible = false,
        };
        Canvas.SetLeft(label, x * _scale);
        Canvas.SetTop(label, y * _scale);
        _layer.Children.Add(label);
    }

    private static List<string> Wrap(string text, double width)
    {
        var lines = new List<string>();
        var line = new StringBuilder();
        double used = 0;
        foreach (char c in text)
        {
            if (c == '\n') { lines.Add(line.ToString()); line.Clear(); used = 0; continue; }
            double w = c < 0x80 ? CellWidth : CellWidth * 2;
            if (used + w > width) { lines.Add(line.ToString()); line.Clear(); used = 0; }
            if (used == 0 && c == ' ') continue;
            line.Append(c);
            used += w;
        }
        if (line.Length > 0) lines.Add(line.ToString());
        return lines;
    }

    /// <summary>
    /// 그 갈래 책을 편다. 쪽이 하나도 없으면 「선택된 종류의 발견물은 하나도 보고되어 있지 않습니다」
    /// (<c>0x0055C850</c>)로 끝난다 — 여덟 갈래 모두 쪽이 있어 원본에서는 안 나온다.
    /// </summary>
    public static void Show(Window owner, Engine.Game game, int category)
    {
        if (game.Discoveries?.Table is not { } table) return;
        var pages = table.Discoveries.Where(r => r.Category == category && !r.Indirect)
                                     .OrderBy(r => r.Id).ToList();
        if (pages.Count == 0)
        {
            NoticeDialog.Show(owner, "선택된 종류의 발견물은 하나도 보고되어 있지 않습니다");
            return;
        }

        var art = EncyclopediaArt.Open(game.Directory);
        if (art == null)
        {
            NoticeDialog.Show(owner, $"백과사전 그림을 열지 못했다 — {EncyclopediaArt.LastError}");
            return;
        }

        int scale = owner.ActualHeight > 800 ? 2 : 1;
        new EncyclopediaPageDialog(game, art, pages, scale) { Owner = owner }.ShowDialog();
    }
}
