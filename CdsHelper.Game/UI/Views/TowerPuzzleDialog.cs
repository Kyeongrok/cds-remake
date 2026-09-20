using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CdsHelper.Game.Local.Helpers;
using CdsHelper.Support.Local.Models;

namespace CdsHelper.Game.UI.Views;

/// <summary>
/// 미니 게임 「발라몬의 탑 퍼즐」 화면.
/// </summary>
/// <remarks>
/// 게임의 <c>0x00431740</c> 이고, 규칙은 <see cref="TowerPuzzle"/> 에 모아 두었다.
///
/// <b>그림은 게임 것 그대로다</b> — TOWER.CDS 에서 뽑아 <c>asset/minigame</c> 에 둔다.
/// 자리 표는 EXE 의 <c>0x00547400</c> 이고 <c>[200704, 303104, 405504]</c> 다.
/// <code>
///   0       448x448   배경 — 돌 받침 셋
///   200704  160x80 x8 돌 판자. 0x00431077 이 크기를, 0x00431067 이
///           «자리 + (판자번호 - 1) * 12800» 으로 몇째 벌인지 준다
/// </code>
/// 받침 자리는 배경에서 재어 썼다 — 위 하나, 아래 둘이 세모꼴로 놓인다.
/// <b>그림 말고는 아무것도 얹지 않는다</b> — 게임 화면에 없는 것을 덧대면 그만큼
/// 게임이 아니게 된다. 들고 있는 판자는 떠 있는 그림 자체가 알려 준다.
///
/// 판자는 <b>끌어다 옮길 수</b> 있고, 딸깍 두 번(집을 기둥 · 놓을 기둥)으로도 옮긴다.
/// 규칙은 <see cref="TowerPuzzle.Tap"/> 하나라 둘이 같은 길로 든다 — 끌기는 「집기
/// 딸깍」과 「놓기 딸깍」을 한 몸짓으로 묶은 것뿐이다.
/// </remarks>
internal sealed class TowerPuzzleDialog : InfoDialog
{
    private const int SceneWidth = 448, SceneHeight = 448;

    /// <summary>화면 점 기준 곱. <see cref="GameUi.PixelZoom"/> 이 배율로 나눈다.</summary>
    private const int Zoom = 2;

    private const int PlankW = 160, PlankH = 80;

    /// <summary>
    /// 받침 셋의 가운데 x 와 판자가 얹히는 y. 배경에서 잰 것이다.
    /// </summary>
    /// <remarks>
    /// <b>차례가 뜻을 가진다.</b> 다 모아야 하는 데가 <see cref="TowerPuzzle.Goal"/> —
    /// 곧 <b>셋째</b> 기둥이고(<c>0x004305EE</c> 가 <c>[esi+0x144]</c> 를 판자 수와 견준다),
    /// 그 자리는 배경에서 <b>가운데 위</b>에 홀로 놓인 받침이다. 쌓아 올리는 것이 이 놀이의
    /// 「탑」이니 눈에도 그렇게 보여야 한다. 아래 둘이 앞의 두 자리다.
    ///
    /// <b>값은 게임 화면을 448x448 로 되돌려 잰 것이다.</b> <c>y</c> 는 판자 <b>칸</b>의
    /// 가운데다 — 칸이 160x80 이므로 그림은 <c>y − 40</c> 에서 시작한다.
    ///
    /// <b>게임은 칸으로 쌓는다, 잉크로가 아니다.</b> 같은 배치를 나란히 놓고 재 보면
    /// 높이가 같은 왼·오른 받침인데 판자 밑이 <c>368</c> 과 <c>372</c> 로 넉 점 다르다.
    /// 놓인 판자가 서로 달라(잉크 밑이 49 · 54) 생기는 차이니, 칸을 맞춰 놓고 두꺼운
    /// 판자가 더 아래로 삐져나오게 두는 것이다.
    /// </remarks>
    private static readonly int[] PegX = [99, 353, 223];
    private static readonly int[] PegY = [358, 358, 208];

    /// <summary>
    /// 판자 한 장이 쌓일 때마다 <b>칸</b>이 올라가는 만큼.
    /// </summary>
    /// <remarks>
    /// 게임 화면에서 두 장 쌓인 탑의 높이(54점)를 재어 뽑았다. 여덟 장을 가운데 위
    /// 받침(칸 가운데 <c>208</c>)에 다 쌓아도 꼭대기 칸이 <c>y 21</c> 이라 판 안에 든다.
    /// </remarks>
    private const int Rise = 21;


    private readonly TowerPuzzle _game;

    /// <summary>처음 자리에서 모으는 가장 적은 수 — 끝날 때 「최소회수」로 보여 준다(<c>[+0x14C]</c>).</summary>
    private readonly int _shortest;

    /// <summary>「이동회수 %3d / 최소회수 %3d」(<c>0x0056BA50</c>) — 둘째 줄 끝에 덧말이 붙기도 한다.</summary>
    private string Tally(string tail = "") =>
        $"이동회수  {_game.Moves,3}{Environment.NewLine}최소회수  {_shortest,3}{tail}";
    private readonly Canvas _scene = new() { Width = SceneWidth, Height = SceneHeight };
    private readonly Border[] _spot = new Border[TowerPuzzle.Pegs];
    private readonly List<Image> _planks = [];
    private readonly Image _held = new()
    {
        Width = PlankW,
        Height = PlankH,
        Visibility = Visibility.Collapsed,
        IsHitTestVisible = false,
    };

    private TowerPuzzleDialog(int planks, Random rng)
    {
        _game = new TowerPuzzle(planks, rng);
        _shortest = _game.Shortest();

        Lay(Picture("tower-bg.png"), 0, 0, SceneWidth, SceneHeight);

        // 기둥마다 누르는 칸. 받침을 넉넉히 덮는다.
        for (int peg = 0; peg < TowerPuzzle.Pegs; peg++)
        {
            int here = peg;
            var box = new Border
            {
                Width = PlankW,
                Height = 150,
                Background = Brushes.Transparent,
                BorderBrush = Brushes.Transparent,
                BorderThickness = new Thickness(2),
                Cursor = Cursors.Hand,
            };
            box.MouseLeftButtonDown += (_, e) => Grab(here, e);
            Canvas.SetLeft(box, PegX[peg] - PlankW / 2);
            Canvas.SetTop(box, PegY[peg] - 110);
            _scene.Children.Add(box);
            _spot[peg] = box;
        }

        RenderOptions.SetBitmapScalingMode(_held, GameUi.SpriteScaling);
        Panel.SetZIndex(_held, 90);
        _scene.Children.Add(_held);

        _scene.Background = Brushes.Transparent;
        _scene.MouseLeftButtonDown += (_, e) => e.Handled = true;

        // 집는 순간 판이 손을 잡으므로 뗌은 늘 판에 온다 — 놓는 기둥은 좌표로 짚는다.
        _scene.MouseMove += Drag;
        _scene.MouseLeftButtonUp += Land;

        double zoom = GameUi.PixelZoom(this, Zoom);
        _scene.LayoutTransform = new ScaleTransform(zoom, zoom);

        // 게임은 미니 게임에 밤색 판도 제목도 아래 단추 줄도 안 두른다 — 그림에 금빛
        // 테만 두르고, 할 일은 오른쪽 단추 차림표가 맡는다(성배 퍼즐·미궁 64 와 같다).
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.WidthAndHeight;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        Background = GameUi.Back;
        Content = GameUi.GoldFrame(_scene, Close);
        GameUi.EnableDrag(this, _scene);

        // 오른쪽 단추는 <b>두 가지</b>를 한다 — 들고 있던 판자를 도로 놓고, 차림표를 편다.
        MouseRightButtonUp += (_, e) =>
        {
            _game.PutBack();
            Sync();
            GameUi.ContextMenuAt(this, e.GetPosition(this), Commands());
        };
        KeyDown += (_, e) => { if (e.Key is Key.Escape) { _game.PutBack(); Sync(); } };

        Sync();
    }

    /// <summary>오른쪽 단추가 부르는 차림표. 예전 아래 단추 줄이 그대로 여기로 왔다.</summary>
    private IReadOnlyList<(string, Action?)> Commands() =>
    [
        ("게임 설명", Explain),
        ("포기한다", GiveUp),
        ("게임 복귀", () => { }),   // 차림표만 닫는다
    ];

    /// <summary>「포기?」 — 물은 뒤 그때까지의 회수를 알리고 닫는다(0x004308CB · 0x004308F6).</summary>
    private void GiveUp()
    {
        if (!ConfirmDialog.Ask(this, "포기하겠습니까?", "포기?")) return;
        NoticeDialog.Show(this, Tally("  였습니다"), "포기");
        Close();
    }

    private void Lay(BitmapSource? art, double x, double y, double width, double height)
    {
        if (art == null) return;

        var image = new Image
        {
            Source = art,
            Width = width,
            Height = height,
            IsHitTestVisible = false,
        };
        RenderOptions.SetBitmapScalingMode(image, GameUi.SpriteScaling);
        Canvas.SetLeft(image, x);
        Canvas.SetTop(image, y);
        _scene.Children.Add(image);
    }

    private static BitmapImage? Picture(string name)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "asset", "minigame", name);
        if (!File.Exists(path)) return null;

        var bmp = new BitmapImage();
        bmp.BeginInit();
        bmp.UriSource = new Uri(path);
        bmp.CacheOption = BitmapCacheOption.OnLoad;
        bmp.EndInit();
        bmp.Freeze();
        return bmp;
    }

    // ── 끌어다 놓기 ────────────────────────────────────────────────────────────

    /// <summary>누른 기둥과 자리. 안 누르고 있으면 −1.</summary>
    private int _from = -1;
    private Point _grabbed;
    private bool _dragging;

    /// <summary>끌었다고 치는 거리(판 점).</summary>
    private const double DragSlop = 5;

    /// <summary>
    /// 기둥을 눌렀다 — <b>빈손이면 여기서 집는다</b>.
    /// </summary>
    /// <remarks>
    /// 이미 판자를 들고 있으면 여기서는 아무것도 안 한다. 놓는 것은 <see cref="Land"/>
    /// 라야 끌어서 놓는 것과 딸깍으로 놓는 것이 한 길로 든다.
    /// </remarks>
    private void Grab(int peg, MouseButtonEventArgs e)
    {
        e.Handled = true;
        if (_game.Won) return;

        _from = peg;
        _dragging = false;
        _grabbed = _cursor = e.GetPosition(_scene);
        _scene.CaptureMouse();

        if (_game.Held <= 0) Tap(peg);      // 빈손 — 집는다
    }

    /// <summary>
    /// 손이 어디 있는지. <b>들고 있는 판자는 늘 이 자리에 있다.</b>
    /// </summary>
    /// <remarks>
    /// 딸깍으로 집었든 끌어서 집었든 판자는 손끝을 따라온다 — 집는 자리에서 기둥 위
    /// 어딘가로 튀지 않는다.
    /// </remarks>
    private Point _cursor;

    private void Drag(object sender, MouseEventArgs e)
    {
        _cursor = e.GetPosition(_scene);

        if (_from >= 0 && !_dragging
            && (Math.Abs(_cursor.X - _grabbed.X) >= DragSlop
                || Math.Abs(_cursor.Y - _grabbed.Y) >= DragSlop))
            _dragging = true;

        if (_game.Held > 0) Carry();
    }

    /// <summary>들고 있는 판자를 손끝에 놓는다.</summary>
    private void Carry()
    {
        Canvas.SetLeft(_held, _cursor.X - PlankW / 2.0);
        Canvas.SetTop(_held, _cursor.Y - PlankH / 2.0);
    }

    private void Land(object sender, MouseButtonEventArgs e)
    {
        if (_from < 0) return;
        e.Handled = true;

        int from = _from;
        bool dragged = _dragging;
        var now = e.GetPosition(_scene);
        _from = -1;
        _dragging = false;
        _scene.ReleaseMouseCapture();

        if (_game.Held <= 0) return;

        // 끌지 않고 딸깍만 했으면 집은 채로 둔다 — 다음 딸깍이 놓을 기둥이다.
        if (!dragged) { if (PegAt(now) is int to && to != from) Tap(to); Sync(); return; }

        // 기둥 밖에 놓으면 없던 일이다 — 도로 제자리에 얹는다.
        if (PegAt(now) is int drop) Tap(drop);
        else _game.PutBack();
        Sync();
    }

    /// <summary>그 자리에 놓인 기둥 번호. 어느 기둥도 아니면 null.</summary>
    private int? PegAt(Point at)
    {
        for (int peg = 0; peg < TowerPuzzle.Pegs; peg++)
        {
            double x = PegX[peg] - PlankW / 2.0, y = PegY[peg] - 110;
            if (at.X >= x && at.X < x + PlankW && at.Y >= y && at.Y < y + 150) return peg;
        }
        return null;
    }

    private void Tap(int peg)
    {
        if (_game.Won) return;

        // 못 놓는 자리면 말 없이 안 놓인다 — 원본은 알리는 글이 없다.
        if (!_game.Tap(peg)) return;
        Sync();

        // 다 모으면 「클리어」에 이동회수·최소회수다(0x00430604).
        if (_game.Won)
        {
            NoticeDialog.Show(this, Tally(), "클리어");
            Close();
        }
    }

    private void Explain() =>
        NoticeDialog.Explain(this,
            "돌 판자를 셋째 기둥에 다 모으면 됩니다." + Environment.NewLine +
            "한 번에 맨 위 판자 하나만 옮길 수 있고, 저보다 작은 판자 위에는 놓지 " +
            "못합니다." + Environment.NewLine +
            "기둥을 눌러 집고, 다시 눌러 놓습니다. 오른쪽 단추로 도로 놓습니다.");

    private void Sync()
    {
        foreach (var image in _planks) _scene.Children.Remove(image);
        _planks.Clear();

        for (int peg = 0; peg < TowerPuzzle.Pegs; peg++)
        {
            var stack = _game.Stack(peg);
            // 아래에서부터 칸을 한 단씩 올려 쌓는다.
            for (int i = 0; i < stack.Count; i++)
                Plank(stack[i], i, PegX[peg], PegY[peg] - i * Rise);

            // 집은 기둥에 테를 두르지 않는다 — 게임에 없는 표시다. 판자가 떠 있는 것으로
            // 어디서 집었는지 이미 보인다.
        }

        // 들고 있는 판자는 손끝을 따라다닌다.
        if (_game.Held > 0)
        {
            _held.Source = Picture($"tower-plank-{_game.Held - 1}.png");
            _held.Visibility = Visibility.Visible;
            Carry();
        }
        else
        {
            _held.Visibility = Visibility.Collapsed;
        }
    }

    /// <summary>판자 한 장. 조각 번호는 <c>판자번호 - 1</c> 이다.</summary>
    /// <param name="level">아래에서 몇째로 쌓였는지. 앞뒤를 이것으로 가른다.</param>
    /// <param name="bottom">판자 <b>칸의 가운데</b>가 놓일 자리.</param>
    private void Plank(int plank, int level, int centre, int bottom)
    {
        var image = new Image
        {
            Source = Picture($"tower-plank-{plank - 1}.png"),
            Width = PlankW,
            Height = PlankH,
            IsHitTestVisible = false,
        };
        RenderOptions.SetBitmapScalingMode(image, GameUi.SpriteScaling);
        // 앞뒤는 <b>쌓인 차례</b>로 정한다 — 판자 번호로 정하면 큰 것이 늘 앞이라
        // 위에 얹은 작은 판자가 뒤로 숨는다.
        Panel.SetZIndex(image, 10 + level);
        Canvas.SetLeft(image, centre - PlankW / 2);
        Canvas.SetTop(image, bottom - PlankH / 2);
        _scene.Children.Add(image);
        _planks.Add(image);
    }

    /// <summary>
    /// 판자를 몇 장 쓸지 묻고 한 판 한다.
    /// </summary>
    /// <remarks>
    /// 게임도 <c>0x0045FB79</c> 에서 «판자를 몇 장 사용하겠습니까?»(<c>0x00571E90</c>)
    /// 를 먼저 묻는다 — <c>0x00481FE0(4, 4, 8, 1, 1)</c> 이라 넷에서 여덟까지다.
    ///
    /// <b>그 <c>0x00481FE0</c> 이 계산기다</b> — 나이·생일을 받는 것과 같은 물건이라
    /// (<see cref="NumberPadDialog"/>) 넷째·다섯째 인자가 <c>MIN·MAX</c> 단추가 넣는
    /// 값이다. 예전에는 «4장 · 5장 …» 을 늘어놓은 목록으로 물었는데, 게임은 목록을 안
    /// 낸다.
    /// </remarks>
    public static void Play(Window owner, Random rng)
    {
        int? planks = NumberPadDialog.Ask(owner, TowerPuzzle.LeastPlanks,
                                          TowerPuzzle.LeastPlanks, TowerPuzzle.MostPlanks,
                                          "판자를 몇 장 사용하겠습니까?");
        if (planks == null) return;

        new TowerPuzzleDialog(planks.Value, rng) { Owner = owner }.ShowDialog();
    }

    /// <summary>
    /// 발견 대본이 판자 수를 정해 한 판 시킨다. 다 모았으면 true.
    /// </summary>
    /// <remarks>
    /// 대본 <c>0E 14|1A [u32 판자] 04 05 00</c> 이 <c>0x00431740(판자, 1)</c> 을 부른다 — 묻지 않는다.
    /// 돌려준 값이 1 이어야 이긴 것이다(<c>0x00408E71</c>).
    /// </remarks>
    public static bool Play(Window owner, Random rng, int planks)
    {
        int count = Math.Clamp(planks, TowerPuzzle.LeastPlanks, TowerPuzzle.MostPlanks);
        var dialog = new TowerPuzzleDialog(count, rng) { Owner = owner };
        dialog.ShowDialog();
        return dialog._game.Won;
    }
}
