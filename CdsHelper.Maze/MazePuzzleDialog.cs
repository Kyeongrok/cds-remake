using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CdsHelper.Game.Local.Helpers;
using CdsHelper.Game.UI.Views;

namespace CdsHelper.Maze;

/// <summary>
/// 미니 게임 「미궁 64 퍼즐」 화면.
/// </summary>
/// <remarks>
/// 게임의 <c>0x0042C8A0</c> 이다. 규칙과 판정은 <see cref="MazePuzzle"/> 에 모아 두었다.
///
/// <b>그림은 게임 것 그대로다</b> — MAZE.CDS 에서 뽑아 <c>asset/minigame</c> 에 둔다
/// (<c>tools/extract_minigame_art.py</c>). 바탕에 <b>네 층의 돌바닥이 비스듬히</b>
/// 그려져 있고, 밟은 칸에 바닥 조각을 얹는 식이다 — 1인칭이 아니다.
/// <code>
///   0x0042BCEA  배경 352x432 를 (8, 8) 에 찍는다
///   0x0042BE87  y0 = 0x1F(31), 층마다 0x6A(106) · 줄마다 +0x16(22)
///   0x0042BE99  x  = 0x64(100), 줄마다 -0x16(22), 칸마다 +0x34(52)
///   0x0042BECE  밟은 칸에 80x24 조각
///   0x0042BF93  상자는 (x + 0x7B, y + 0x13) — 곧 칸에서 (+23, -12)
///   0x0042C0A0  방향 화살표 여섯 벌 — 32x24
/// </code>
/// <b>화살표는 갈 수 있는 칸 위에 뜬다.</b> 여섯 벌의 자리 상수를 풀어 보면 다 같은
/// 꼴이다 — <b>가는 칸에서 (+25, -2)</b>. 짚은 방향만 금빛 조각으로 갈린다.
/// <code>
///   방향 1 위층  y = 층*106 + 줄*22 - 0x4D   x = 0x7D + 칸*52 - 줄*22
///   방향 2 아래  y = …            + 0x87    x = 0x7D + …
///   방향 3 줄-1  y = …            + 0x07    x = 0x93 + …
///   방향 4 줄+1  y = …            + 0x33    x = 0x67 + …
///   방향 5 칸-1  y = …            + 0x1D    x = 0x49 + …
///   방향 6 칸+1  y = …            + 0x1D    x = 0xB1 + …
/// </code>
/// 그래서 칸 자리가 이렇게 난다.
/// <code>
///   x = 100 + 칸 * 52 - 줄 * 22
///   y =  31 + 층 * 106 + 줄 * 22
/// </code>
/// </remarks>
internal sealed class MazePuzzleDialog : InfoDialog
{
    /// <summary>게임 그림의 크기. 자리 값이 다 이 눈금이다.</summary>
    private const int SceneWidth = 352, SceneHeight = 432;

    /// <summary>
    /// 그림을 <b>화면 점</b> 기준으로 몇 배로 놓을지. 1 이면 원본 크기다.
    /// </summary>
    /// <remarks>
    /// <see cref="GameUi.PixelZoom"/> 이 <b>모니터 배율로 나눠</b> 준다. 그냥 2 를
    /// 걸면 배율 175% 인 화면에서 3.5배가 돼 점이 뭉갠다.
    /// </remarks>
    private const int Zoom = 2;

    /// <summary>바닥 칸 조각과 그 자리 셈(<c>0x0042BE87</c> 벌).</summary>
    private const int FloorW = 80, FloorH = 24;
    /// <summary>
    /// 배경이 화면에 놓이는 자리(<c>0x0042BCEA</c>). <b>칸 좌표는 화면 좌표라</b>
    /// 배경 왼쪽 위를 원점으로 삼는 우리 판에서는 이만큼 빼야 맞는다.
    /// </summary>
    /// <remarks>
    /// 이걸 안 뺐더니 밟은 칸이 격자에서 여덟 점 어긋나 떠 보였다 — 게임 화면과 대 보면
    /// 바닥 조각이 칸에 딱 앉는데 우리 것만 비껴 있었다.
    /// </remarks>
    private const int BackX = 8, BackY = 8;

    /// <summary>첫 칸의 자리(<c>0x0042BE87</c> · <c>0x0042BE99</c>) — 화면 좌표다.</summary>
    private const int OriginX = 100 - BackX, OriginY = 31 - BackY;
    private const int ColStep = 52, RowStep = 22, LayerStep = 106;

    /// <summary>상자·문은 칸에서 이만큼 옮겨 얹는다(<c>0x0042BF93</c>).</summary>
    private const int ItemDx = 23, ItemDy = -12, ItemSize = 32;

    /// <summary>탐험가는 40x40 이고 칸 가운데 선다.</summary>
    private const int HeroSize = 40, HeroDx = 20, HeroDy = -22;

    /// <summary>화살표는 <b>가는 칸</b>에서 이만큼 옮겨 얹는다(<c>0x0042C0A0</c> 벌).</summary>
    private const int ArrowW = 32, ArrowH = 24, ArrowDx = 25, ArrowDy = -2;

    /// <summary>UNDO 쪽지의 숫자 자리(<c>0x0042BE55</c>) — 화면 좌표에서 배경 자리를 뺀 것.</summary>
    private const int UndoX = 34 - BackX, UndoY = 41 - BackY;

    private static readonly Brush Ring = Frozen(Colors.White);

    private readonly MazePuzzle _game;
    private readonly Canvas _scene = new() { Width = SceneWidth, Height = SceneHeight };
    private readonly Image[] _floor = new Image[MazePuzzle.Rooms];
    private readonly Image[] _item = new Image[MazePuzzle.Rooms];
    private readonly Image[] _arrow = new Image[MazePuzzle.Ways.Length];
    private readonly Image _hero = new()
    {
        Width = HeroSize,
        Height = HeroSize,
        IsHitTestVisible = false,
    };
    /// <summary>
    /// 왼쪽 위 <b>UNDO 쪽지</b>에 적히는 취소 횟수. 쪽지 그림은 배경에 그려져 있고
    /// (「UNDO」 글자까지), 숫자만 게임이 얹는다.
    /// </summary>
    /// <remarks>
    /// <code>
    ///   0042be55  [0x62B2D4] = 0x29        ; y = 41
    ///   0042be5f  eax = 0x22               ; x = 34
    ///   0042be7f  0x004B60C0(판, "%d/3", [this+0x304])
    /// </code>
    /// 자리는 <b>화면 좌표</b>라 배경이 놓이는 (8, 8) 을 빼야 우리 판의 자리가 된다.
    /// 오른쪽 아래 「GIVE UP」 쪽지에는 숫자가 없어 배경 그림 그대로다.
    /// </remarks>
    private readonly GameUi.GameLabel _undo = new(GameFont.BlackColor) { Bold = false };

    /// <summary>지금 짚은 방향. 게임의 <c>[0x2FC]</c> 다.</summary>
    private int _point = -1;

    /// <summary>상금 갈래인지(<c>+0x310</c>) — 발견 대본이 걸 때만이다. 상자 말과 덫 말이 갈린다.</summary>
    private readonly bool _stakes;

    /// <summary>효과음. 없으면 조용하다.</summary>
    private readonly CdsHelper.Game.Local.Helpers.SoundBank? _sfx;

    /// <summary>상자를 열었을 때의 소리 — 사운드 0x22(<c>0x0042AACE</c>), WAVE 파트 6.</summary>
    private const int ChestSoundPart = 0x22 - 28;

    private MazePuzzleDialog(Random rng, bool stakes = false, CdsHelper.Game.Local.Helpers.SoundBank? sfx = null)
    {
        _stakes = stakes;
        _sfx = sfx;
        _game = new MazePuzzle(rng);

        if (Picture("maze-bg.png") is { } back)
        {
            var image = new Image { Source = back, Width = SceneWidth, Height = SceneHeight };
            RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.NearestNeighbor);
            _scene.Children.Add(image);
        }

        for (int room = 0; room < MazePuzzle.Rooms; room++) Cell(room);
        for (int way = 0; way < MazePuzzle.Ways.Length; way++) Arrow(way);

        RenderOptions.SetBitmapScalingMode(_hero, BitmapScalingMode.NearestNeighbor);
        _hero.Source = Picture("maze-hero.png");
        Panel.SetZIndex(_hero, 50);
        _scene.Children.Add(_hero);

        // 왼쪽 위 UNDO 쪽지 위의 숫자. 쪽지와 「UNDO」 글자는 배경 그림에 있다.
        _undo.IsHitTestVisible = false;
        Canvas.SetLeft(_undo, UndoX);
        Canvas.SetTop(_undo, UndoY);
        Panel.SetZIndex(_undo, 70);
        _scene.Children.Add(_undo);

        // 빈 데도 누름을 받아야 한다. 누름을 여기서 먹어야 판에 걸린 창 끌기가 안 물고 간다.
        _scene.Background = Brushes.Transparent;
        _scene.MouseLeftButtonDown += (_, e) => e.Handled = true;
        _scene.MouseLeftButtonUp += SceneUp;
        // 모니터 배율을 물어 나눠 준다 — 그림 점 하나가 화면 점 하나가 되게.
        double zoom = GameUi.PixelZoom(this, Zoom);
        _scene.LayoutTransform = new ScaleTransform(zoom, zoom);

        // 게임은 미니 게임에 밤색 판도 제목도 아래 단추도 안 두른다 — 그림에 금빛 액자만
        // 두르고, 할 일은 오른쪽 단추 차림표가 맡는다. 셈(밟은 방·상자·취소·다시)은
        // 게임에서도 판 안 왼쪽 위의 UNDO 쪽지에 적히므로 따로 띠를 두지 않는다.
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.WidthAndHeight;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        Background = GameUi.Back;
        Content = GameUi.GoldFrame(_scene, Close);
        GameUi.EnableDrag(this, _scene);

        MouseRightButtonUp += (_, e) =>
            GameUi.ContextMenuAt(this, e.GetPosition(this), Commands());

        KeyDown += OnKey;
        Sync();
    }

    /// <summary>그 칸의 왼쪽 위 자리.</summary>
    private static Point Spot(int room)
    {
        int col = room % MazePuzzle.Side;
        int row = room / MazePuzzle.Side % MazePuzzle.Side;
        int layer = room / (MazePuzzle.Side * MazePuzzle.Side);
        return new Point(OriginX + col * ColStep - row * RowStep,
                         OriginY + layer * LayerStep + row * RowStep);
    }

    /// <summary>칸 하나 — 밟은 바닥, 갈 수 있음을 알리는 테, 상자나 문.</summary>
    private void Cell(int room)
    {
        var at = Spot(room);

        var floor = new Image
        {
            Width = FloorW,
            Height = FloorH,
            Visibility = Visibility.Collapsed,
            IsHitTestVisible = false,
        };
        RenderOptions.SetBitmapScalingMode(floor, BitmapScalingMode.NearestNeighbor);
        Canvas.SetLeft(floor, at.X);
        Canvas.SetTop(floor, at.Y);
        _scene.Children.Add(floor);
        _floor[room] = floor;

        var item = new Image
        {
            Width = ItemSize,
            Height = ItemSize,
            Visibility = Visibility.Collapsed,
            IsHitTestVisible = false,
        };
        RenderOptions.SetBitmapScalingMode(item, BitmapScalingMode.NearestNeighbor);
        Canvas.SetLeft(item, at.X + ItemDx);
        Canvas.SetTop(item, at.Y + ItemDy);
        Panel.SetZIndex(item, 20);
        _scene.Children.Add(item);
        _item[room] = item;
    }

    /// <summary>방향 화살표 하나. 갈 수 있을 때만 뜨고, 누르면 그리로 간다.</summary>
    private void Arrow(int way)
    {
        var image = new Image
        {
            Width = ArrowW,
            Height = ArrowH,
            Visibility = Visibility.Collapsed,
            Cursor = Cursors.Hand,
        };
        RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.NearestNeighbor);
        Panel.SetZIndex(image, 60);

        image.MouseLeftButtonDown += (_, e) => e.Handled = true;
        image.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            int next = MazePuzzle.Neighbour(_game.Here, MazePuzzle.Ways[way].Step);
            if (next >= 0) Step(next);
        };

        // 짚으면 금빛 조각으로 갈린다 — 게임의 [0x2FC] 자리다.
        image.MouseEnter += (_, _) => { _point = way; Sync(); };
        image.MouseLeave += (_, _) => { if (_point == way) { _point = -1; Sync(); } };

        _scene.Children.Add(image);
        _arrow[way] = image;
    }

    /// <summary>뽑아 둔 그림 한 장. 없으면 null.</summary>
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

    /// <summary>누른 자리에서 가장 가까운 칸. 너무 멀면 -1.</summary>
    private static int RoomAt(Point at)
    {
        int best = -1;
        double near = double.MaxValue;
        for (int room = 0; room < MazePuzzle.Rooms; room++)
        {
            var spot = Spot(room);
            double dx = at.X - (spot.X + FloorW / 2.0);
            double dy = at.Y - (spot.Y + FloorH / 2.0);
            // 칸이 마름모라 세로를 늘려 재야 이웃 층으로 안 샌다.
            double far = dx * dx + dy * dy * 4;
            if (far < near) { near = far; best = room; }
        }
        return near <= 40 * 40 ? best : -1;
    }

    private void SceneUp(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        int room = RoomAt(e.GetPosition(_scene));
        if (room >= 0) Step(room);
    }

    private void OnKey(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Space) { AskUndo(); return; }

        int step = e.Key switch
        {
            Key.Right => +1,
            Key.Left => -1,
            Key.Down => +MazePuzzle.Side,
            Key.Up => -MazePuzzle.Side,
            Key.PageDown => +MazePuzzle.Side * MazePuzzle.Side,
            Key.PageUp => -MazePuzzle.Side * MazePuzzle.Side,
            _ => 0,
        };
        if (step == 0) return;

        int next = MazePuzzle.Neighbour(_game.Here, step);
        if (next >= 0) Step(next);
        e.Handled = true;
    }

    private void Step(int room)
    {
        if (_game.Over != MazePuzzle.Result.Playing) return;
        if (!_game.Walk(room)) return;

        Sync();

        int number = _game.ChestAt(room);
        if (number != 0 && !_game.ChestOpen(number)
            && ConfirmDialog.Ask(this, "보물 상자가 있습니다. 열겠습니까?", "보물 상자 발견"))
        {
            OpenChest();
            if (_game.Over != MazePuzzle.Result.Playing) return;
        }

        if (room != _game.Exit) return;

        int was = _game.Restarted;
        var result = _game.Arrive();
        Sync();

        if (result == MazePuzzle.Result.Playing && _game.Restarted > was)
        {
            NoticeDialog.Show(this, "방을 전부 돌지 않았기 때문에" + Environment.NewLine +
                                    "입구로 되돌아 오고 말았다!", "처음부터 다시");
            return;
        }
        if (result != MazePuzzle.Result.Playing) Close();
    }

    /// <summary>오른쪽 단추 차림표의 줄. 게임 갈무리 차례 그대로다.</summary>
    /// <remarks>
    /// 글귀와 차례는 게임 것 그대로다(<c>0x00559BE8</c>~<c>0x00559C30</c>) —
    /// 「보물 상자를 연다」는 상자를 밟고 섰을 때만 끼워진다.
    /// </remarks>
    private IReadOnlyList<(string, Action?)> Commands()
    {
        var rows = new List<(string, Action?)>();
        if (_game.ChestAt(_game.Here) != 0 && !_game.ChestOpen(_game.ChestAt(_game.Here)))
            rows.Add(("보물 상자를 연다", OpenChest));

        rows.Add(("ＵＮＤＯ(취소)", _game.CanUndo ? AskUndo : null));
        rows.Add(("포기한다", AskGiveUp));
        rows.Add(("게임 설명", Explain));
        rows.Add(("게임으로 돌아간다", () => { }));   // 차림표만 닫는다
        return rows;
    }

    private void OpenChest()
    {
        int number = _game.OpenChest();
        if (number == 0)
        {
            // 이미 연 상자를 또 눌렀다(0x0042BA29).
            if (!_game.Emptied) return;
            _game.Emptied = false;
            NoticeDialog.Show(this, "이 보물 상자는 이미 열려져 있었습니다.", "빈 보물 상자");
            return;
        }

        if (_game.Over == MazePuzzle.Result.Trapped)
        {
            Sync();
            Close();
            return;
        }

        // 상금 갈래면 상자마다 금화를 알린다 — n 번째 상자가 10^n 닢이다(0x0042AA05). 셈은 끝에 한 번에
        // 치른다(0x0042B11A). 미니 게임은 마지막 상자면 보물, 아니면 다음 상자의 열쇠다.
        if (_stakes)
            NoticeDialog.Show(this, $"금화 {(int)Math.Pow(10, number)}닢을 손에 넣었다!", "보물 발견");
        else
            NoticeDialog.Show(this,
                number == MazePuzzle.Chests
                    ? "보물을 손에 넣었다!"
                    : $"보물 상자 {number + 1}의 열쇠를 손에 넣었다!",
                number == MazePuzzle.Chests ? "보물 발견" : "열쇠 발견");
        _sfx?.Play(ChestSoundPart);
        Sync();
    }

    private void AskUndo()
    {
        if (_game.Undone >= MazePuzzle.MaxUndo)
        {
            NoticeDialog.Show(this,
                "[U N D O (취소) ] 는 3회까지입니다. 더 이상 사용할 수 없습니다.", "횟수 오버");
            return;
        }
        if (!_game.CanUndo) return;
        if (!ConfirmDialog.Ask(this, "한발 앞의 상태로 돌아가겠습니까?", "앞으로 돌아간다")) return;

        _game.Undo();
        Sync();
    }

    /// <summary>
    /// 「게임 설명」 — 게임 EXE 의 글을 <b>한 자도 안 고치고</b> 옮겼다
    /// (<c>0x00559D40</c>, 제목 <c>0x00559D30</c>).
    /// </summary>
    /// <remarks>
    /// 게임은 이 글을 두 군데서 낸다 — <b>판을 열기 전에 한 번</b>(<c>0x0042C84E</c>)과,
    /// 놀이 도중 차림표의 「게임 설명」을 골랐을 때(<c>0x0042B839</c>)다. 둘 다
    /// <c>0x0049E3E0</c>(제목 달린 알림창)로 낸다.
    /// </remarks>
    private static void Explain(Window owner) =>
        NoticeDialog.Explain(owner, Rules);

    private void Explain() => Explain(this);

    /// <summary>게임 EXE 의 설명 글 그대로(<c>0x00559D40</c>) — 일곱 줄이다.</summary>
    private static readonly string Rules =
        "바닥을 전부 한번씩만 통과해, 출구로 나가 주십시오." + Environment.NewLine +
        "나아갈 방향의 표시 위에서 마우스의 왼쪽 버튼을 누르며 이동합니다." + Environment.NewLine +
        "되돌릴 때에는 [U N D O(취소)]의 위에서 왼쪽 버튼을 누릅니다." + Environment.NewLine +
        "[U N D O(취소)]를 사용할 수 있는 것은 3회까지입니다." + Environment.NewLine +
        "키보드의 경우, ↑↓←→와 Roll(Page)Up·Roll(Page)Down 키로 나아갈 방향을 지정해 Return(Enter)키로 이동합니다." + Environment.NewLine +
        "[U N D O(취소)]는 스페이스키를 누릅니다." + Environment.NewLine +
        "보물 상자는 숫자가 적은 순서로 밖에 열지 못합니다만 열지 않아도 밖으로 나갈 수 있습니다. 바닥을 전부 통과하지 않고 출구로 가면 처음으로 돌아갑니다. 이것도 3회까지입니다.";

    private void AskGiveUp()
    {
        if (!ConfirmDialog.Ask(this, "미궁으로부터의 탈출을 포기하겠습니까?", "포기한다")) return;
        _game.GiveUp();
        Close();
    }

    private void Sync()
    {
        // 게임이 적는 것은 취소 횟수 하나뿐이다("%d/3", 0x0053CED4).
        _undo.Text = $"{_game.Undone}/{MazePuzzle.MaxUndo}";

        var floor = Picture("maze-floor.png");

        for (int room = 0; room < MazePuzzle.Rooms; room++)
        {
            bool walked = _game.StepAt(room) != 0;
            _floor[room].Source = floor;
            _floor[room].Visibility = walked ? Visibility.Visible : Visibility.Collapsed;

            int chest = _game.ChestAt(room);
            var art = room == _game.Exit ? Picture("maze-door.png")
                    : chest == 0 ? null
                    : Picture(_game.ChestOpen(chest)
                              ? $"maze-chest-open-{chest - 1}.png"
                              : $"maze-chest-{chest - 1}.png");

            _item[room].Source = art;
            _item[room].Visibility = art == null ? Visibility.Collapsed : Visibility.Visible;
        }

        // 갈 수 있는 방향에만 화살표를 세운다. <b>가는 칸</b> 위에 뜬다.
        for (int way = 0; way < MazePuzzle.Ways.Length; way++)
        {
            int next = _game.Over == MazePuzzle.Result.Playing
                     ? MazePuzzle.Neighbour(_game.Here, MazePuzzle.Ways[way].Step) : -1;
            bool open = next >= 0 && _game.StepAt(next) == 0;

            _arrow[way].Visibility = open ? Visibility.Visible : Visibility.Collapsed;
            if (!open) continue;

            _arrow[way].Source = Picture($"maze-arrow-{way * 2 + (_point == way ? 1 : 0)}.png");
            var to = Spot(next);
            Canvas.SetLeft(_arrow[way], to.X + ArrowDx);
            Canvas.SetTop(_arrow[way], to.Y + ArrowDy);
        }

        var at = Spot(_game.Here);
        Canvas.SetLeft(_hero, at.X + HeroDx);
        Canvas.SetTop(_hero, at.Y + HeroDy);

    }

    /// <summary>놀이를 한 판 하고 <c>0x0042C8A0</c> 이 하듯 결과를 알린다.</summary>
    /// <returns>
    /// 돌파했는지. 게임은 돌파 보상을 치른 갈래에서만 결과 <c>[+0x9C] = 1</c> 을 박고
    /// (<c>0x0042B154</c>) 덫·실패·포기는 0 이다 — 발견 대본(<c>0E 04 02</c>)이 이 값으로 갈라진다.
    /// </returns>
    public static bool Play(Window owner, Random rng,
                            CdsHelper.Support.Local.Models.Player? player = null,
                            CdsHelper.Game.Local.Helpers.SoundBank? sfx = null)
    {
        // 판을 열기 전에 설명부터 낸다 — 게임도 그렇다(0x0042C84E).
        Explain(owner);

        // 상금 갈래(+0x310)는 발견 대본이 걸 때뿐이다(0x0042C8A0(1)). 미니 게임은 0 이라 말도 다르고
        // 금화도 없다(0x0042B11A).
        bool stakes = player != null;

        var dialog = new MazePuzzleDialog(rng, stakes, sfx) { Owner = owner };
        dialog.ShowDialog();

        switch (dialog._game.Over)
        {
            case MazePuzzle.Result.Trapped:
                // 덫 말도 갈래마다 다르다(0x0042AADC) — 상금 갈래는 미궁이 흔들린다.
                NoticeDialog.Show(owner,
                    "순서를 지키지 않았으므로 보물 상자에 장치된 덫이 작동!" + Environment.NewLine +
                    (stakes ? "갑자기 미궁이 흔들리기 시작했다." : "순식간에 목숨을 잃고 말았다!"), "게임 오버");
                break;

            case MazePuzzle.Result.Failed:
                // 발견 대본이 거는 미궁은 <b>상금 갈래</b>다(0x00408D80 이 0x0042C8A0(1)).
                // 그 갈래의 실패 말이 이것이고(0x0042B0C8), 미니 게임은 문이 닫힌다(0x0042B0D4).
                NoticeDialog.Show(owner, stakes
                    ? "이번엔 되돌아 오지 않았지만, 바닥이 불길한 소리를 내기 시작했다!"
                    : "이번엔 되돌아 오지 않았지만 문이 닫히고 말았다. 이제 탈출은 불가능하다.",
                    "클리어 실패");
                NoticeDialog.Show(owner, "게임 오버입니다. 다음 번엔 노력합시다.", "게임 오버");
                break;

            case MazePuzzle.Result.GaveUp:
                NoticeDialog.Show(owner, "포기하자, 바닥이 차츰 웅웅거리기 시작했다!", "게임 오버");
                NoticeDialog.Show(owner, "게임 오버입니다. 다음 번엔 노력합시다.", "게임 오버");
                break;

            // 상금 갈래는 <b>실수를 안 따진다</b> — 다 밟고 나가면 한 가지 말이고,
            // 연 상자 수만큼 금화를 준다(0x0042B11A 의 점프표).
            case MazePuzzle.Result.Cleared:
            case MazePuzzle.Result.Perfect:
                // 미니 게임에서 상자 넷을 다 열고 한 번도 안 무르고 안 되돌아갔으면 말이 따로 있다
                // (0x0042AFF9 → 0x00559A80).
                NoticeDialog.Show(owner, !stakes && dialog._game.Over == MazePuzzle.Result.Perfect
                    ? "축하하네! 자네는 실수하지 않고 미궁을 돌파해 보물을 손에 넣었네!"
                    : "축하하네! 드디어 자네는 미궁을 돌파했네!", "게임 클리어");
                // <b>말없이 넣어 준다</b> — 코인 게임과 달리 알리는 글이 없다(0x0042B136 이
                // 곧바로 0x0047CBC0 으로 간다).
                if (stakes && dialog._game.Prize > 0) player!.Earn(dialog._game.Prize);
                break;
        }

        return dialog._game.Over is MazePuzzle.Result.Cleared or MazePuzzle.Result.Perfect;
    }
}
