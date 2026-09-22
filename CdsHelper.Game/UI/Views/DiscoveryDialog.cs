using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CdsHelper.Game.Local.Helpers;

namespace CdsHelper.Game.UI.Views;

/// <summary>
/// 발견 알림 — 그림 한 장을 세우고 그 아래에 "…을 발견했다!" 를 적는다.
/// </summary>
/// <remarks>
/// 세빌리아 교회처럼 <b>건물 자체가 발견물</b>인 자리에서 뜬다. 그림은 DSTILL.CDS 에서
/// 오고(<see cref="DiscoveryStills"/>), 어느 그림인지는 건물 표가 들고 있다
/// (<see cref="CityBuildingTable.Building.Picture"/>).
///
/// 그림에는 <b>액자</b>가 둘린다 — 밤색 판에 까만 줄 두 겹이다. 그림 안쪽의 크림빛
/// 테는 그림에 그려진 것이고, 액자는 그 바깥에 따로 있다.
///
/// 아래 칸은 게임 알림창과 같은 꼴이다(<see cref="ConfirmDialog"/> 와 같은 자리값).
/// </remarks>
public sealed class DiscoveryDialog : GameWindow
{
    /// <summary>글 칸의 여백과 단추 자리. 게임 알림창에서 그대로 가져왔다.</summary>
    private const double SidePad = 7, TopPad = 7, BottomPad = 15;
    private const double EdgeThickness = 1, TextGap = 10;

    /// <summary>
    /// 그림에 두르는 액자 — 까만 줄 · 밤색 판 · 까만 줄이다.
    /// </summary>
    /// <remarks>게임 갈무리에서 잰 값이다. 판이 여덟 점쯤이고 줄은 한 점씩이다.</remarks>
    private const double FrameLine = 1, FrameWide = 8;

    /// <summary>액자가 그림 좌우로 더 먹는 폭.</summary>
    private const double FrameGrow = (FrameLine + FrameWide + FrameLine) * 2;

    /// <summary>액자의 까만 줄과 밤색 판. 알림 칸 바탕보다 조금 밝다.</summary>
    private static readonly Brush FrameEdge = Frozen(Color.FromRgb(0x11, 0x09, 0x09));
    private static readonly Brush FrameFill = Frozen(Color.FromRgb(0x4A, 0x2E, 0x24));

    /// <summary>동영상 칸의 크기. 게임 것이 320x240 이다.</summary>
    private const double MovieWidth = 320, MovieHeight = 240;

    /// <summary>그림에 바짝 붙는 까만 줄, 그 바깥에 밤색 판, 다시 까만 줄.</summary>
    private static UIElement Framed(UIElement inner) => new Border
    {
        Background = FrameFill,
        BorderBrush = FrameEdge,
        BorderThickness = new Thickness(FrameLine),
        Padding = new Thickness(FrameWide),
        HorizontalAlignment = HorizontalAlignment.Center,
        Child = new Border
        {
            BorderBrush = FrameEdge,
            BorderThickness = new Thickness(FrameLine),
            Child = inner,
        },
    };

    private static Brush Frozen(Color c)
    {
        var b = new SolidColorBrush(c);
        b.Freeze();
        return b;
    }
    private const double ButtonWidth = 64, ButtonHeight = UiSprites.BandHeight;

    private readonly GameUi.FocusGroup _focus = new();

    /// <summary>
    /// 발견물 <b>그림만</b> 띄우는 창.
    /// </summary>
    /// <remarks>
    /// 게임은 그림과 알림을 <b>따로</b> 띄운다 — 알림만 끌 수 있고 그림은 제자리에
    /// 남는다. 예전에는 둘을 한 창에 붙여 두어 같이 움직였다.
    /// </remarks>
    private sealed class Still : GameWindow
    {
        public Still(UIElement art, double width)
        {
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            SizeToContent = SizeToContent.WidthAndHeight;
            ShowInTaskbar = false;
            Background = GameUi.Back;
            IsHitTestVisible = false;          // 그림은 손을 안 받는다 — 못 끈다
            Content = new StackPanel { Width = width, Children = { Framed(art) } };
        }
    }

    /// <summary>그림 창과 알림 창 사이 틈 — 원본 갈무리에서 둘이 <b>맞닿아</b> 있다.</summary>
    private const double StillGap = 0;

    private DiscoveryDialog(string text, string? title)
    {
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.WidthAndHeight;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        Background = GameUi.Back;

        // 해설(제목 띠가 붙는 것)은 게임도 넓게 편다 — 한 줄이 예순두 칸쯤이다.
        bool comment = !string.IsNullOrEmpty(title);
        var stack = new StackPanel
        {
            Width = comment ? CommentCells * CellWidth + SidePad * 2 + EdgeThickness * 2 : MinWidth_,
        };

        // 해설은 여러 줄이라 왼쪽에 붙이고, 발견 알림은 한 줄이라 가운데다.
        var lines = Wrap(text, stack.Width - SidePad * 2);
        var words = new StackPanel();
        foreach (string line in lines)
            words.Children.Add(new GameUi.GameLabel(GameFont.WhiteColor, GameUi.ItemTextHeight)
            {
                Text = line,
                Bold = false,
                FallbackBrush = GameUi.Text,
                HorizontalAlignment = lines.Count == 1 ? HorizontalAlignment.Center
                                                       : HorizontalAlignment.Left,
            });

        var ok = _focus.Add("확인", () => { DialogResult = true; }, ButtonWidth);
        ok.Height = ButtonHeight;
        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Center,
            Height = ButtonHeight,
            Margin = new Thickness(0, TextGap, 0, 0),
            Children = { ok },
        };

        var below = new StackPanel();
        // 해설에는 제목 띠가 붙는다 — 발견물 이름이다.
        if (!string.IsNullOrEmpty(title)
            && GameUi.TitleFrame(GameUi.Sprites, title!) is { } bar)
        {
            bar.Margin = new Thickness(0, 0, 0, 6);
            below.Children.Add(bar);
        }
        below.Children.Add(words);
        below.Children.Add(buttons);
        stack.Children.Add(new Border
        {
            Background = GameUi.Back,
            BorderBrush = PanelEdge,
            BorderThickness = new Thickness(EdgeThickness),
            Padding = new Thickness(SidePad, TopPad, SidePad, BottomPad),
            Child = below,
        });

        Content = stack;

        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) { DialogResult = true; return; }
            if (_focus.HandleKey(e.Key)) e.Handled = true;
        };
        GameUi.EnableDrag(this, stack);
    }

    /// <summary>
    /// 발견을 알린다. 그림을 못 구하면 글만 낸다 — 발견은 이미 적혔고 그림은 덤이다.
    /// </summary>
    /// <param name="owner">알림을 얹을 창.</param>
    /// <param name="stills">발견물 그림. 없으면 글만 낸다.</param>
    /// <param name="picture">그림 번호. -1 이면 그림이 없는 발견물이다.</param>
    /// <param name="text">적을 글("히랄다탑을 발견했다!").</param>
    /// <param name="movie">틀 동영상 파일. 없으면 null 이고 그때 그림을 본다.</param>
    /// <param name="title">제목 띠에 적을 이름. 없으면 띠가 안 붙는다.</param>
    /// <param name="face">
    /// 말하는 사람 얼굴. 주면 글 창이 <b>얼굴 대화창</b>(<see cref="ConfirmDialog"/>)이 된다 — 발견 대본에서 부관이
    /// 그림을 보며 한마디 하는 자리가 그렇다(원본 갈무리: 그림 바로 밑에 얼굴 창이 붙고 폭은 그림보다 넓다).
    /// </param>
    public static void Show(Window owner, DiscoveryStills? stills, int picture, string text,
                            string? movie = null, string? title = null, uint[]? face = null)
    {
        BitmapSource? art = null;
        double width = MinWidth_;

        if (movie != null && !File.Exists(movie)) movie = null;

        if (movie == null && picture >= 0 && BgraOf(stills, picture, out int w, out int h) is { } bgra)
        {
            var bmp = BitmapSource.Create(w, h, 96, 96, PixelFormats.Bgra32, null, bgra, w * 4);
            bmp.Freeze();
            art = bmp;
            width = w;
        }

        // 그림이 있으면 <b>따로</b> 띄운다 — 알림만 끌 수 있고 그림은 제자리에 남는다.
        var still = Picture(art, movie, width, out var stop);
        var show = still == null ? null : new Still(still, width + FrameGrow) { Owner = owner };
        if (show != null)
        {
            show.Show();
            Centre(show, owner, up: true);
        }

        if (face != null)
        {
            // 얼굴 대화창을 그림 바로 밑 가운데에 붙인다.
            try
            {
                ConfirmDialog.Tell(owner, text, face: face, place: show == null ? null : box =>
                {
                    box.Left = show.Left + (show.ActualWidth - box.ActualWidth) / 2;
                    box.Top = show.Top + show.ActualHeight + StillGap;
                });
            }
            finally { stop?.Invoke(); show?.Close(); }
            return;
        }

        var say = new DiscoveryDialog(text, title) { Owner = owner };
        if (show != null)
        {
            // 알림은 그림 바로 아래 가운데다. 거기서부터 끌고 다닐 수 있다.
            say.WindowStartupLocation = WindowStartupLocation.Manual;
            say.Loaded += (_, _) =>
            {
                say.Left = show.Left + (show.ActualWidth - say.ActualWidth) / 2;
                say.Top = show.Top + show.ActualHeight + StillGap;
            };
        }

        try { say.ShowDialog(); }
        finally { stop?.Invoke(); show?.Close(); }
    }

    /// <summary>
    /// 그림 <b>한 장만</b> 띄우고 누르면 닫는다 — 글 창이 안 붙는다.
    /// </summary>
    /// <remarks>
    /// 보고·발표가 발견물을 다시 보일 때(<c>0x004AAF30</c>)는 DSTILL 그림을 판에 찍고(<c>0x004AD640</c>) 손을 기다릴 뿐,
    /// 이름 줄도 확인 단추도 없다. 이름은 바로 앞의 「…의 발견을 보고했다!!」가 이미 말했다.
    /// </remarks>
    public static void ShowPicture(Window owner, DiscoveryStills? stills, int picture)
    {
        if (picture < 0 || BgraOf(stills, picture, out int w, out int h) is not { } bgra) return;

        var bmp = BitmapSource.Create(w, h, 96, 96, PixelFormats.Bgra32, null, bgra, w * 4);
        bmp.Freeze();
        if (Picture(bmp, null, w, out _) is not { } art) return;

        var show = new Still(art, w + FrameGrow) { Owner = owner, IsHitTestVisible = true, Cursor = System.Windows.Input.Cursors.Hand };
        show.MouseLeftButtonUp += (_, _) => show.Close();
        show.MouseRightButtonUp += (_, _) => show.Close();
        show.KeyDown += (_, _) => show.Close();
        show.Loaded += (_, _) => Centre(show, owner, up: false);
        show.ShowDialog();
    }

    /// <summary>그림 창 속에 넣을 것. 그림도 동영상도 없으면 null.</summary>
    private static UIElement? Picture(BitmapSource? art, string? movie, double width,
                                      out Action? stop)
    {
        stop = null;
        if (movie != null)
        {
            // 코덱이 없어 못 틀면 그 창만 비고 알림은 그대로 나온다 — 그림은 덤이다.
            var player = new MediaElement
            {
                Source = new Uri(movie),
                LoadedBehavior = MediaState.Manual,
                UnloadedBehavior = MediaState.Close,
                Stretch = Stretch.Uniform,
                Width = MovieWidth,
                Height = MovieHeight,
            };
            player.MediaFailed += (_, _) => player.Visibility = Visibility.Collapsed;
            player.MediaEnded += (_, _) => player.Stop();
            player.Loaded += (_, _) => player.Play();
            stop = player.Close;
            return player;
        }
        if (art == null) return null;

        var image = new Image { Source = art, Width = art.PixelWidth, Height = art.PixelHeight };
        RenderOptions.SetBitmapScalingMode(image, GameUi.SpriteScaling);
        RenderOptions.SetEdgeMode(image, EdgeMode.Aliased);
        return image;
    }

    /// <summary>
    /// 그 창을 주인 가운데에 놓는다. <paramref name="up"/> 이면 알림이 들어갈 만큼 위로 올린다.
    /// </summary>
    private static void Centre(Window what, Window owner, bool up)
    {
        what.UpdateLayout();
        double lift = up ? (what.ActualHeight + StillGap) / 4 : 0;
        what.Left = owner.Left + (owner.ActualWidth - what.ActualWidth) / 2;
        what.Top = owner.Top + (owner.ActualHeight - what.ActualHeight) / 2 - lift;
    }

    /// <summary>
    /// 그 발견물의 동영상 파일 자리. 올려 둔 것(<c>asset/movie</c>)이 먼저고, 없으면 게임 폴더의
    /// 원본이다(<see cref="MovieFiles"/>). 둘 다 없으면 null.
    /// </summary>
    public static string? MovieOf(string gameDirectory, int movie) =>
        movie < 0 ? null : MovieFiles.Resolve(gameDirectory, MovieFiles.DiscoveryStem(movie));

    /// <summary>
    /// 그 그림 번호의 BGRA — 올려 둔 그림(<see cref="DiscoveryStillFiles"/>)이 먼저고, 없으면
    /// 게임 폴더의 원본(<paramref name="stills"/>)이다.
    /// </summary>
    private static uint[]? BgraOf(DiscoveryStills? stills, int picture, out int w, out int h)
    {
        if (DiscoveryStillFiles.TryGetBgra(picture, out w, out h) is { } uploaded) return uploaded;
        return stills?.TryGetBgra(picture, out w, out h);
    }

    /// <summary>그림이 없을 때의 글 칸 너비. 게임 알림창의 가장 좁은 폭이다.</summary>
    private const double MinWidth_ = 272;

    /// <summary>해설 한 줄의 칸 수. 게임 갈무리의 창 폭(확인 단추와 견주어 약 517점)에서 잰 값이다.</summary>
    private const int CommentCells = 62;

    /// <summary>
    /// 알림 칸의 테. 공용 테(<see cref="GameUi.Edge"/>)는 거의 검정인데 게임 것은 밝은 회갈색이다 —
    /// 물음창(<see cref="ConfirmDialog"/>)과 같은 빛이다.
    /// </summary>
    private static readonly Brush PanelEdge = Frozen(Color.FromRgb(0x9B, 0x8D, 0x7D));

    /// <summary>글자 한 칸 — 한글 한 자가 두 칸이다.</summary>
    private const double CellWidth = 8;

    /// <summary>칸 너비에 맞춰 끊는다.</summary>
    private static List<string> Wrap(string text, double width)
    {
        var lines = new List<string>();
        var line = new System.Text.StringBuilder();
        double used = 0;

        foreach (char c in text)
        {
            double w = c < 0x80 ? CellWidth : CellWidth * 2;
            if (used + w > width) { lines.Add(line.ToString()); line.Clear(); used = 0; }
            line.Append(c);
            used += w;
        }
        if (line.Length > 0) lines.Add(line.ToString());
        return lines.Count > 0 ? lines : [text];
    }
}
