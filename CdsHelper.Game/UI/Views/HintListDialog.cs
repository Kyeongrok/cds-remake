using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CdsHelper.Game.Local.Helpers;

namespace CdsHelper.Game.UI.Views;

/// <summary>
/// 지금까지 얻은 힌트를 늘어놓는 「취득 힌트 일람」. 그냥 보여 주기도 하고
/// (커맨드 창의 "힌트 정보"), 하나를 고르게 하기도 한다(왕궁의 "설득").
/// </summary>
/// <remarks>
/// 게임도 창 하나를 두 군데에서 쓴다. 고르는 쪽(<see cref="Pick"/>)은 EXE 의
/// <c>0x004769A0</c> 이 하는 일 그대로다 — 목록을 띄우고 고른 힌트 번호를 내며,
/// 중단하면 -1 이다. 힌트는 책을 읽으면 는다(볼트 <c>20.분석-도서관 책과 책등 색</c>).
/// </remarks>
public sealed class HintListDialog : GameWindow
{
    /// <summary>목록 칸의 폭과 가장 높은 자리. 게임 갈무리에서 잰 값이다.</summary>
    /// <remarks>
    /// 글자 높이를 자로 삼아 원본과 대 보면 판이 <b>한 뼘 넓었다</b> — 제목 글씨 폭에 견준
    /// 판 폭이 원본은 4.8배인데 우리는 5.4배였다. 300 에서 264 로 줄여 맞췄다.
    /// </remarks>
    private const double ListWidth = 264, ListMaxHeight = 280;

    /// <summary>고른 줄의 바탕. 게임 갈무리에서 집은 파랑이다.</summary>
    private static readonly Brush PickFill = Frozen(Color.FromRgb(0x43, 0x56, 0x7A));

    /// <summary>고른 줄의 테. 바탕보다 훨씬 짙은 남색이다.</summary>
    private static readonly Brush PickEdge = Frozen(Color.FromRgb(0x05, 0x06, 0x09));

    /// <summary>
    /// 도드라진 줄의 바탕 <c>#DEC6AD</c> — 설득의 「제안 선택」에서 후원자가 좋아하는 갈래인 힌트다.
    /// </summary>
    /// <remarks>게임 창에는 없는 표시다. 종이색보다 한 톤 짙은 갈색이라 검은 글씨가 그대로 읽힌다.</remarks>
    private static readonly Brush MarkFill = Frozen(Color.FromRgb(0xDE, 0xC6, 0xAD));

    /// <summary>줄마다 도드라지게 칠할지. 없으면 어느 줄도 안 칠한다.</summary>
    private readonly IReadOnlyList<bool>? _marks;

    /// <summary>그 줄을 고르지 않았을 때의 바탕.</summary>
    private Brush RestFill(int index) =>
        _marks != null && index < _marks.Count && _marks[index] ? MarkFill : Brushes.Transparent;


    private static Brush Frozen(Color c)
    {
        var b = new SolidColorBrush(c);
        b.Freeze();
        return b;
    }

    /// <summary>줄 좌우 여백. 게임은 종이 테에 바짝 붙여 찍는다.</summary>
    private const double RowPad = 3;

    /// <summary>배경 판을 창 테에서 물려 놓는 만큼. 도시 명령 창과 같은 세 점이다.</summary>
    private const double PanelInset = 3;

    /// <summary>바깥 테와 그 안쪽 테 사이의 틈. 한 점 띄워야 줄이 둘로 보인다.</summary>
    private const double EdgeGap = 1;

    /// <summary>
    /// 아래 단추 둘의 폭·높이와 사이. 게임 조각 단추다.
    /// </summary>
    /// <remarks>단추도 같은 자로 재면 원본이 제목 글씨의 1.34배인데 우리는 1.63배였다.</remarks>
    private const double ButtonWidth = 106, ButtonGap = 12;

    /// <summary>
    /// 줄 왼쪽 얼굴의 크기와 글씨까지의 틈 — 스폰서 일람이 쓴다. 초상화 80x96 의 절반이다.
    /// </summary>
    /// <remarks>게임 창에는 얼굴이 없다. 누구인지 한눈에 보려고 더한 것이라 작게 둔다.</remarks>
    private const double FaceWidth = 40, FaceHeight = 48, FaceGap = 4;

    /// <summary>고른 줄. 아무것도 안 골랐으면 -1.</summary>
    private int _picked = -1;

    /// <summary>
    /// 여럿 고르기 — 누를 때마다 그 줄이 켜졌다 꺼진다. 항구 「발표」 창(<c>0x0047EA80</c>)이 쓴다.
    /// </summary>
    private readonly bool _multi;

    /// <summary>여럿 고르기에서 켜진 줄.</summary>
    private readonly SortedSet<int> _chosen = [];

    /// <summary>줄마다의 판. 고른 줄만 도드라지게 칠한다.</summary>
    private readonly List<Border> _rows = [];

    private readonly GameButton _decide;

    private HintListDialog(IReadOnlyList<string> hints, bool choosing, string caption,
                           string header = "", IReadOnlyList<uint[]?>? faces = null,
                           IReadOnlyList<string>? subtitles = null, IReadOnlyList<bool>? marks = null,
                           bool multi = false)
    {
        _marks = marks;
        _multi = multi;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.WidthAndHeight;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        Background = GameUi.Back;

        var list = new StackPanel();

        // 머리글 — 고를 수 없는 줄 하나를 맨 위에 둔다(조선소 개조 목록이 쓴다).
        if (header.Length > 0)
            list.Children.Add(new Border
            {
                Margin = new Thickness(1),
                Padding = new Thickness(RowPad, 0, RowPad, 0),
                Child = new GameUi.GameLabel(GameFont.BlackColor, GameUi.ItemTextHeight)
                {
                    Text = header,
                    Bold = false,
                    FallbackBrush = Brushes.Black,
                    HorizontalAlignment = HorizontalAlignment.Left,
                },
            });

        for (int i = 0; i < hints.Count; i++)
        {
            int index = i;
            // 줄은 게임 비트맵 글꼴로 찍는다 — 종이 위라 검은 벌이다.
            var label = new GameUi.GameLabel(GameFont.BlackColor, GameUi.ItemTextHeight)
            {
                Text = hints[i],
                // 줄은 <b>겹쳐 찍지 않는다</b> — 오른쪽 아래로 한 점 겹친 자국이
                // 그림자처럼 보인다. 게임 목록 글씨는 민 글씨다.
                Bold = false,
                FallbackBrush = Brushes.Black,
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Center,
            };

            // 얼굴을 받았으면 줄 왼쪽에 작게 붙인다. 못 읽은 얼굴은 빈칸으로 두어 글씨 줄을 맞춘다.
            // 이름 아래 한 줄(스폰서 일람의 취향). 얼굴과 함께 쓰면 두 줄이 얼굴 높이 안에 든다.
            FrameworkElement text = label;
            if (subtitles != null && i < subtitles.Count && subtitles[i].Length > 0)
                text = new StackPanel
                {
                    VerticalAlignment = VerticalAlignment.Center,
                    Children =
                    {
                        label,
                        new GameUi.GameLabel(GameFont.BlackColor, GameUi.ItemTextHeight)
                        {
                            Text = subtitles[i],
                            Bold = false,
                            FallbackBrush = Brushes.Black,
                            HorizontalAlignment = HorizontalAlignment.Left,
                        },
                    },
                };

            FrameworkElement content = text;
            if (faces != null)
                content = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Children = { Face(i < faces.Count ? faces[i] : null), text },
                };

            var row = new Border
            {
                Background = RestFill(i),
                BorderBrush = Brushes.Transparent,
                BorderThickness = new Thickness(1),
                // 테 <b>바깥</b>으로 한 점을 비운다 — 고른 줄의 테가 위아래 줄에 맞닿지 않게.
                Margin = new Thickness(1),
                Padding = new Thickness(RowPad, 0, RowPad, 0),
                Cursor = choosing ? Cursors.Hand : Cursors.Arrow,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Child = content,
            };
            if (choosing) row.MouseLeftButtonUp += (_, e) => { e.Handled = true; Select(index); };
            _rows.Add(row);
            list.Children.Add(row);
        }

        // 고르는 창이라도 아직 아무것도 안 골랐으면 결정은 흐리다 — 게임도 그렇다.
        _decide = new GameButton("결정", Decide, BandStyle.Button, ButtonWidth)
        {
            Height = UiSprites.BandHeight,
            Margin = new Thickness(0, 0, ButtonGap / 2, 0),
            On = false,
        };

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 5, 0, 5),
        };
        var stop = new GameButton("중단", Cancel, BandStyle.Button, ButtonWidth)
        {
            Height = UiSprites.BandHeight,
            Margin = new Thickness(ButtonGap / 2, 0, 0, 0),
        };
        buttons.Children.Add(_decide);
        buttons.Children.Add(stop);

        var title = GameUi.TitleBar(caption, Cancel);
        GameUi.EnableDrag(this, title);

        var stack = new StackPanel();
        stack.Children.Add(title);
        stack.Children.Add(new Border
        {
            Background = GameUi.PageFill,
            BorderBrush = GameUi.ItemEdge,
            // 테는 도시 명령 창과 같은 한 점이다(GameMenu.BoxEdge) — 두 점씩 두 겹이면
            // 바깥 테와 겹쳐 두꺼워 보인다.
            BorderThickness = new Thickness(1),
            Margin = new Thickness(3, 3, 3, 0),
            Padding = new Thickness(2, 1, 2, 1),
            // <b>가로로 넓고 세로는 줄 수를 따라간다.</b> 게임 창이 그렇다 — 두 줄이면
            // 두 줄만큼만 높고, 길어지면 그때 늘어나다 스무 줄쯤에서 멎고 굴러간다.
            // 예전에는 280x300 으로 박아 두어 줄이 몇 없어도 아래가 텅 비었다.
            Child = new ScrollViewer
            {
                // 얼굴을 붙이면 그만큼 넓힌다 — 이름 칸 폭은 그대로 둔다.
                Width = faces == null ? ListWidth : ListWidth + FaceWidth + FaceGap,
                MaxHeight = ListMaxHeight,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Content = list,
            },
        });
        stack.Children.Add(buttons);

        // 창은 <b>두 겹</b>이다 — 바깥 검은 줄 하나와 그 안의 배경 판이다.
        // <code>
        //   ┌ 검은 줄 ─────────────┐  바깥 테
        //   │ ┌ 검은 줄 ─────────┐ │  배경 판(테 사이를 띄운다)
        //   │ │ 제목·목록·단추
        // </code>
        // 예전에는 가운데에 검은 줄을 한 겹 더 두었는데, 안쪽 판에도 테가 있어
        // <b>검은 줄이 둘로 겹쳐</b> 보였다.
        Content = new Border
        {
            Background = GameUi.Back,
            BorderBrush = GameUi.Edge,
            BorderThickness = new Thickness(1),
            Child = new Border
            {
                Background = GameUi.MenuBack,
                BorderBrush = GameUi.MenuEdge,
                BorderThickness = new Thickness(1),
                Margin = new Thickness(EdgeGap + PanelInset),
                Child = stack,
            },
        };

        KeyDown += (_, e) => { if (e.Key is Key.Escape) Cancel(); };
        MouseRightButtonUp += (_, _) => Cancel();
    }

    /// <summary>한 줄을 고른다. 고르고 나야 결정이 살아난다.</summary>
    private void Select(int index)
    {
        if (_multi)
        {
            if (!_chosen.Remove(index)) _chosen.Add(index);
            for (int i = 0; i < _rows.Count; i++) Paint(i, _chosen.Contains(i));
            _decide.On = _decideReady = _chosen.Count > 0;
            return;
        }

        _picked = index;
        // 고른 줄은 <b>남색 바탕에 흰 글씨</b>다 — 종이 위 검은 글씨를 그대로 두면
        // 바탕에 묻힌다. 테는 바탕보다 훨씬 짙어 한 겹 파인 것처럼 보인다.
        for (int i = 0; i < _rows.Count; i++) Paint(i, i == index);

        _decide.On = true;
        _decideReady = true;
    }

    /// <summary>줄 하나를 고른 모양 또는 쉬는 모양으로 칠한다.</summary>
    private void Paint(int i, bool on)
    {
        // 고르지 않은 줄은 제 바탕으로 — 도드라진 줄이면 갈색이 남는다.
        _rows[i].Background = on ? PickFill : RestFill(i);
        _rows[i].BorderBrush = on ? PickEdge : Brushes.Transparent;
        // 얼굴 · 둘째 줄이 붙은 줄은 글씨가 판 안에 들어 있다 — 든 글씨를 다 뒤집는다.
        // 글씨색만 뒤집는다 — 겹쳐 찍기는 어느 줄에서도 안 한다.
        foreach (var label in LabelsIn(_rows[i].Child))
            label.TextColor = on ? GameFont.WhiteColor : GameFont.BlackColor;
    }

    private bool _decideReady;

    /// <summary>줄 안의 글씨를 다 찾는다 — 이름 한 줄이거나, 얼굴·둘째 줄과 함께 판에 들어 있다.</summary>
    private static IEnumerable<GameUi.GameLabel> LabelsIn(object? element) => element switch
    {
        GameUi.GameLabel label => [label],
        Panel panel => panel.Children.Cast<object>().SelectMany(LabelsIn),
        _ => [],
    };

    /// <summary>줄 왼쪽의 작은 얼굴. 그림이 없으면 같은 크기의 빈칸이다.</summary>
    private static FrameworkElement Face(uint[]? bgra)
    {
        var margin = new Thickness(0, 1, FaceGap, 1);
        if (bgra == null || bgra.Length < Portraits.Width * Portraits.Height)
            return new Border { Width = FaceWidth, Height = FaceHeight, Margin = margin };

        var bitmap = BitmapSource.Create(Portraits.Width, Portraits.Height, 96, 96,
                                         PixelFormats.Bgra32, null, bgra, Portraits.Width * 4);
        bitmap.Freeze();
        var image = new Image
        {
            Source = bitmap,
            Width = FaceWidth,
            Height = FaceHeight,
            Stretch = Stretch.Fill,
            Margin = margin,
        };
        RenderOptions.SetBitmapScalingMode(image, GameUi.SpriteScaling);
        return image;
    }

    /// <summary>결정 — 고른 것이 있어야 눌린다.</summary>
    private void Decide()
    {
        if (_decideReady) Close();
    }

    private void Cancel()
    {
        _picked = -1;
        _chosen.Clear();
        Close();
    }

    /// <summary>줄을 늘어놓기만 한다. 하나도 없으면 그렇다고 알린다.</summary>
    /// <param name="caption">창 제목. 안 주면 「취득 힌트 일람」이다.</param>
    /// <param name="whenEmpty">줄이 하나도 없을 때 알릴 말.</param>
    public static void Show(Window owner, IReadOnlyList<string> hints,
                            string caption = "취득 힌트 일람",
                            string whenEmpty = "설득 가능한 힌트가 없습니다")
    {
        if (hints.Count == 0)
        {
            NoticeDialog.Show(owner, whenEmpty);
            return;
        }
        new HintListDialog(hints, choosing: false, caption) { Owner = owner }.ShowDialog();
    }

    /// <summary>
    /// 한 줄을 고르게 한다. 고른 줄 번호를 내고, 중단하면 -1 이다.
    /// 고를 것이 없으면 <paramref name="whenEmpty"/> 로 알리고 -1 을 낸다.
    /// </summary>
    /// <remarks>
    /// 게임도 창 하나를 「취득 힌트 일람」과 「스폰서 일람」 두 군데에 쓴다
    /// (<c>0x004769A0</c> 와 <c>0x00476660</c> 이 같은 모양이다).
    /// </remarks>
    /// <param name="header">줄 위에 얹을 머리글. 빈 글이면 안 얹는다.</param>
    /// <param name="faces">줄마다 왼쪽에 붙일 초상화(80x96 BGRA). 없으면 글씨만 늘어놓는다.</param>
    /// <param name="subtitles">줄마다 이름 아래에 붙일 한 줄. 빈 글이면 그 줄은 이름만 있다.</param>
    /// <param name="marks">줄마다 <c>#DEC6AD</c> 바탕으로 도드라지게 할지. 없으면 안 칠한다.</param>
    public static int Pick(Window owner, IReadOnlyList<string> items,
                           string caption = "취득 힌트 일람",
                           string whenEmpty = "설득 가능한 힌트가 없습니다",
                           string header = "",
                           IReadOnlyList<uint[]?>? faces = null,
                           IReadOnlyList<string>? subtitles = null,
                           IReadOnlyList<bool>? marks = null)
    {
        if (items.Count == 0)
        {
            NoticeDialog.Show(owner, whenEmpty);
            return -1;
        }

        var dlg = new HintListDialog(items, choosing: true, caption, header, faces, subtitles, marks) { Owner = owner };
        dlg.ShowDialog();
        return dlg._picked;
    }

    /// <summary>
    /// 여러 줄을 고르게 한다 — 누를 때마다 켜고 끈다. 켠 줄 번호를 차례대로 내고, 중단하면 빈 목록이다.
    /// </summary>
    /// <param name="preset">처음부터 켜 둘 줄 — 물음에 아니오로 되돌아올 때 고르던 것을 살린다.</param>
    public static IReadOnlyList<int> PickMany(Window owner, IReadOnlyList<string> items, string caption,
                                              IReadOnlyCollection<int>? preset = null)
    {
        if (items.Count == 0) return [];

        var dlg = new HintListDialog(items, choosing: true, caption, multi: true) { Owner = owner };
        if (preset != null)
            foreach (int i in preset)
                if (i >= 0 && i < items.Count) dlg.Select(i);
        dlg.ShowDialog();
        return [.. dlg._chosen];
    }
}
