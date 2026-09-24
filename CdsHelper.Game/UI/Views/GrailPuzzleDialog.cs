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
/// 미니 게임 「성배 퍼즐」 화면.
/// </summary>
/// <remarks>
/// 게임의 <c>0x00467D50</c> 이다. 규칙과 판정은 <see cref="GrailPuzzle"/> 에 모아 두었다.
/// <code>
///   0x00559068  게임 설명
///   0x0056DFB0  "%d번째"          — 지금 몇 수인지
///   0x0056DFC8  "한 수 되돌립니까?"
///   0x0056DFE8  "다시 할 수 없습니다"
///   0x0056E048  "게임을 포기하겠습니까?"
/// </code>
/// <b>그림은 게임 것 그대로다</b> — MGGRAPH.CDS 에서 뽑아 <c>asset/minigame</c> 에
/// 둔다(<c>tools/extract_minigame_art.py</c>). 조각 크기는 <c>0x00549E98</c> 표에
/// 있고 <b>조각 n 이 곧 파트 n+3</b> 이다.
/// <code>
///   조각 14~16 · 20~22  48x64  바가지 소·중·대 — 빈 것과 물 든 것
///   조각 26~35 · 36~45  24x72  성배 열       — 빈 것과 찬 것
///   조각 48            368x432 배경
/// </code>
/// 그릇 자리도 게임이 쓰는 좌표를 그대로 쓴다.
/// <code>
///   0x0046810E  바가지 셋 — x = 12 · 60 · 108, y = 0x90 (144)
///   0x004681AE  성배 열   — x = 0x00559040 의 열 값, y = 0x13E (318)
///   0x00468077  큰 항아리 — (0xD5, 0x74) = (213, 116)
///   0x00467FD6  버리는 곳 — (0xA0, 0x30) = (160, 48), 조각 47(64x32) · 자리 0 · 종류 2
/// </code>
/// <b>버리는 곳은 석상이 든 뿔잔</b>이다. 바가지를 거기에 놓으면 비워진다(<see cref="GrailPuzzle.Drop"/> 의
/// 종류 2 갈래, <c>0x00467C34</c>). 뿔잔도 항아리도 배경 그림에 이미 그려져 있어 조각을 따로 안 얹는다.
/// 값은 게임처럼 <b>분수</b>로 적는다 — 위가 든 물, 아래가 용량이다. 잡은 그릇에는
/// 흰 네모를 두른다.
///
/// <b>바가지를 끌어다 놓는다.</b> 게임과 같다 — 바가지를 집어 큰 항아리에 놓으면
/// 물이 떠지고, 성배나 다른 바가지에 놓으면 부어진다. 무슨 일이 일어날지는
/// <b>놓는 자리</b>가 정한다(<see cref="GrailPuzzle.Drop"/>). 끌지 않고 딸깍 눌렀다
/// 놓으면 집힌 채로 있어서, 다음에 누른 자리에 놓인다.
/// </remarks>
internal sealed class GrailPuzzleDialog : InfoDialog
{
    /// <summary>게임 그림의 크기. 자리 값이 다 이 눈금이다.</summary>
    private const int SceneWidth = 368, SceneHeight = 432;

    /// <summary>
    /// 그림을 <b>화면 점</b> 기준으로 몇 배로 놓을지. 1 이면 원본 크기다.
    /// </summary>
    /// <remarks>
    /// <see cref="GameUi.PixelZoom"/> 이 <b>모니터 배율로 나눠</b> 준다. 그냥 2 를
    /// 걸면 배율 175% 인 화면에서 3.5배가 돼 점이 뭉갠다.
    /// </remarks>
    private const int Zoom = 2;

    /// <summary>바가지 셋의 왼쪽 끝과 줄 높이(<c>0x0046810E</c>).</summary>
    private static readonly int[] DipperX = [12, 60, 108];
    private const int DipperY = 144, DipperW = 48, DipperH = 64;

    /// <summary>성배 열의 왼쪽 끝(<c>0x00559040</c>)과 줄 높이(<c>0x004681AE</c>).</summary>
    private static readonly int[] GrailX = [19, 49, 80, 112, 145, 179, 214, 250, 287, 325];
    private const int GrailY = 318, GrailW = 24, GrailH = 72;

    /// <summary>큰 항아리 자리(<c>0x00468077</c>) 언저리.</summary>
    private const int JarX = 196, JarY = 100, JarBoxW = 132, JarBoxH = 158;

    /// <summary>
    /// 버리는 곳(뿔잔) 자리(<c>0x00467FD6</c> — (160, 48)에 64x32) 언저리. 뿔잔 입이 다 들게 조금 넓힌다.
    /// </summary>
    /// <remarks>예전에는 이 자리를 안 만들어 바가지를 비울 수가 없었다.</remarks>
    private const int DrainX = 152, DrainY = 40, DrainBoxW = 80, DrainBoxH = 48;

    private static readonly Brush Ring = Frozen(Colors.White);

    private readonly GrailPuzzle _game;
    private readonly Canvas _scene = new() { Width = SceneWidth, Height = SceneHeight };
    private readonly Dictionary<int, Rect> _box = [];
    private readonly Dictionary<int, Border> _spot = [];
    private readonly Dictionary<int, Image> _art = [];
    private readonly Dictionary<int, GameUi.GameLabel> _now = [];
    private readonly GameUi.GameLabel _count = new(GameFont.WhiteColor) { Bold = false };

    /// <summary>끌고 다니는 그림.</summary>
    private readonly Image _ghost = new() { Visibility = Visibility.Collapsed, IsHitTestVisible = false };

    private int _pick = -1, _grab = -1, _over = -1;

    private GrailPuzzleDialog(int problem)
    {
        _game = new GrailPuzzle(problem);

        if (Backdrop() is { } picture) _scene.Children.Add(picture);

        _count.FallbackBrush = Ring;
        Canvas.SetLeft(_count, 14);
        Canvas.SetTop(_count, 12);
        _scene.Children.Add(_count);

        // 성배를 먼저 얹고 바가지를 나중에 — 겹치는 데는 없지만 차례는 게임과 같다.
        for (int i = 0; i < GrailPuzzle.Grails; i++)
            Art(GrailPuzzle.FirstGrail + i, GrailX[i], GrailY, GrailW, GrailH);
        for (int i = 0; i < GrailPuzzle.Dippers; i++)
            Art(GrailPuzzle.FirstDipper + i, DipperX[i], DipperY, DipperW, DipperH);

        Spot(GrailPuzzle.Jar, JarX, JarY, JarBoxW, JarBoxH, label: false);
        Spot(GrailPuzzle.Idle, DrainX, DrainY, DrainBoxW, DrainBoxH, label: false);
        for (int i = 0; i < GrailPuzzle.Dippers; i++)
            Spot(GrailPuzzle.FirstDipper + i, DipperX[i], DipperY, DipperW, DipperH);
        for (int i = 0; i < GrailPuzzle.Grails; i++)
            Spot(GrailPuzzle.FirstGrail + i, GrailX[i], GrailY + 4, GrailW, GrailH - 8);

        RenderOptions.SetBitmapScalingMode(_ghost, GameUi.SpriteScaling);
        Panel.SetZIndex(_ghost, 100);
        _scene.Children.Add(_ghost);

        // 빈 데도 누름을 받아야 끌기가 끊기지 않는다.
        _scene.Background = Brushes.Transparent;
        _scene.MouseLeftButtonDown += SceneDown;
        _scene.MouseMove += SceneMove;
        _scene.MouseLeftButtonUp += SceneUp;

        // 모니터 배율을 물어 나눠 준다 — 그림 점 하나가 화면 점 하나가 되게.
        double zoom = GameUi.PixelZoom(this, Zoom);
        _scene.LayoutTransform = new ScaleTransform(zoom, zoom);

        // 게임은 미니 게임에 밤색 판도 제목도 아래 단추도 안 두른다 — 그림에 금빛 액자만
        // 두르고, 할 일은 오른쪽 단추 차림표가 맡는다.
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.WidthAndHeight;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        Background = GameUi.Back;
        Content = GameUi.GoldFrame(_scene, Close);
        GameUi.EnableDrag(this, _scene);

        MouseRightButtonUp += (_, e) =>
        {
            _pick = -1;
            Sync();
            GameUi.ContextMenuAt(this, e.GetPosition(this), Commands());
        };
        KeyDown += (_, e) => { if (e.Key is Key.Escape) { _pick = -1; Sync(); } };

        Sync();
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

    /// <summary>게임 그림. 못 읽으면 안 깐다 — 자리 네모만으로도 놀 수는 있다.</summary>
    private static UIElement? Backdrop()
    {
        if (Picture("grail-bg.png") is not { } bmp) return null;

        var image = new Image { Source = bmp, Width = SceneWidth, Height = SceneHeight };
        RenderOptions.SetBitmapScalingMode(image, GameUi.SpriteScaling);
        return image;
    }

    /// <summary>그릇 조각을 그 자리에 얹는다. 물이 들면 다른 조각으로 갈아 끼운다.</summary>
    private void Art(int slot, int x, int y, int width, int height)
    {
        var image = new Image { Width = width, Height = height, IsHitTestVisible = false };
        RenderOptions.SetBitmapScalingMode(image, GameUi.SpriteScaling);
        Canvas.SetLeft(image, x);
        Canvas.SetTop(image, y);
        _scene.Children.Add(image);
        _art[slot] = image;
    }

    /// <summary>그 자리에 지금 놓일 조각.</summary>
    private BitmapImage? ArtFor(int slot)
    {
        if (slot >= GrailPuzzle.FirstGrail)
        {
            int i = slot - GrailPuzzle.FirstGrail;
            bool full = _game.WaterAt(slot) == _game.SizeAt(slot);
            return Picture(full ? $"grail-cup-full-{i}.png" : $"grail-cup-{i}.png");
        }

        int d = slot - GrailPuzzle.FirstDipper;
        return Picture(_game.WaterAt(slot) > 0
                       ? $"grail-dipper-full-{d}.png" : $"grail-dipper-{d}.png");
    }

    /// <summary>그릇 한 자리 — 누르는 칸과, 게임처럼 <b>분수</b>로 적는 값.</summary>
    private void Spot(int slot, int x, int y, int width, int height, bool label = true)
    {
        var box = new Border
        {
            Width = width,
            Height = height,
            Background = Brushes.Transparent,
            BorderBrush = Brushes.Transparent,
            BorderThickness = new Thickness(1),
            IsHitTestVisible = false,
        };
        Canvas.SetLeft(box, x);
        Canvas.SetTop(box, y);
        _scene.Children.Add(box);
        _spot[slot] = box;
        _box[slot] = new Rect(x, y, width, height);

        if (!label) return;

        // 든 물 / 가로줄 / 용량. 게임도 이렇게 두 줄로 쌓아 적는다.
        bool grail = slot >= GrailPuzzle.FirstGrail;
        var now = new GameUi.GameLabel(GameFont.WhiteColor) { Bold = false, FallbackBrush = Ring };
        var cap = new GameUi.GameLabel(GameFont.WhiteColor) { Bold = false, FallbackBrush = Ring };
        cap.Text = $"{_game.SizeAt(slot)}";
        _now[slot] = now;

        // 글자가 누름을 가로채면 안 된다 — 밑의 그릇 칸이 받아야 한다.
        var stack = new StackPanel
        {
            HorizontalAlignment = HorizontalAlignment.Left,
            IsHitTestVisible = false,
        };
        stack.Children.Add(now);
        stack.Children.Add(new Border
        {
            Height = 2,
            Width = 16,
            Background = Ring,
            HorizontalAlignment = HorizontalAlignment.Left,
        });
        stack.Children.Add(cap);

        // 성배는 잔 밑동에, 바가지는 자루 왼쪽에 붙는다.
        Canvas.SetLeft(stack, x + (grail ? 4 : 1));
        Canvas.SetTop(stack, y + (grail ? 16 : 2));
        _scene.Children.Add(stack);
    }

    /// <summary>그 자리에 있는 그릇. 없으면 -1.</summary>
    private int SlotAt(Point at)
    {
        foreach (var (slot, box) in _box)
            if (box.Contains(at)) return slot;
        return -1;
    }

    private void SceneDown(object sender, MouseButtonEventArgs e)
    {
        // 판에 걸린 창 끌기(GameUi.EnableDrag)가 물고 가지 않게 여기서 먹는다.
        e.Handled = true;
        if (_game.Over != null) return;

        int slot = SlotAt(e.GetPosition(_scene));

        // 이미 집어 둔 것이 있으면 여기가 놓을 자리다.
        if (_pick >= 0 && slot >= 0 && slot != _pick)
        {
            PutDown(_pick, slot);
            return;
        }

        if (slot < 0 || !_game.CanGrab(slot)) { _pick = -1; Sync(); return; }

        _grab = slot;
        _pick = slot;
        _scene.CaptureMouse();
        Sync();
        Follow(e.GetPosition(_scene));
    }

    private void SceneMove(object sender, MouseEventArgs e)
    {
        if (_grab < 0) return;

        var at = e.GetPosition(_scene);
        int slot = SlotAt(at);
        if (slot != _over) { _over = slot; Sync(); }
        Follow(at);
    }

    private void SceneUp(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        if (_grab < 0) return;

        int from = _grab;
        _grab = -1;
        _over = -1;
        _scene.ReleaseMouseCapture();
        _ghost.Visibility = Visibility.Collapsed;

        int slot = SlotAt(e.GetPosition(_scene));

        // 집은 자리에서 그냥 뗐으면 집힌 채로 둔다 — 다음에 누른 자리에 놓인다.
        if (slot < 0 || slot == from) { Sync(); return; }
        PutDown(from, slot);
    }

    /// <summary>끌고 다니는 그림을 손 자리에 맞춘다.</summary>
    private void Follow(Point at)
    {
        _ghost.Source = ArtFor(_grab);
        _ghost.Width = DipperW;
        _ghost.Height = DipperH;
        _ghost.Visibility = Visibility.Visible;
        Canvas.SetLeft(_ghost, at.X - DipperW / 2.0);
        Canvas.SetTop(_ghost, at.Y - DipperH / 2.0);
    }

    /// <summary>놓는다. 무슨 일이 일어날지는 놓는 자리가 정한다.</summary>
    private void PutDown(int from, int to)
    {
        int was = _game.Moves;
        _game.Drop(from, to);
        _pick = -1;
        Sync();

        // 물이 실제로 옮겨졌을 때만 소리가 난다 — 게임도 붓는 몸짓을 도는 자리에서
        // 낸다(0x00467A90). 헛손질에는 아무 소리도 안 난다.
        if (_game.Moves != was) _sfx?.Play(SoundBank.PourPart);

        if (_game.Over != null)
        {
            // 다 채웠으면 수를 얼마나 썼든 소리가 난다 — 게임도 「모든 성배를 성수로
            // 채웠다!」 바로 앞에서 낸다(0x0046860A).
            if (_game.Over is not (GrailPuzzle.Result.GaveUp or GrailPuzzle.Result.Spilled))
                _sfx?.Play(SoundBank.GrailWonPart);
            Close();
        }
    }

    /// <summary>효과음 벌. 게임 폴더를 모르면 null 이라 조용히 논다.</summary>
    private SoundBank? _sfx;

    /// <summary>오른쪽 단추 차림표의 줄. 게임 갈무리 차례 그대로다.</summary>
    private IReadOnlyList<(string, Action?)> Commands() =>
    [
        ("한 수 되돌림", _game.CanUndo ? AskUndo : null),
        ("포기한다", AskGiveUp),
        ("게임 설명", Explain),
        ("게임 복귀", () => { }),   // 차림표만 닫는다
    ];

    private void AskUndo()
    {
        if (!_game.CanUndo)
        {
            NoticeDialog.Show(this, "다시 할 수 없습니다", "경고");
            return;
        }
        if (!ConfirmDialog.Ask(this, "한 수 되돌립니까?", "취소")) return;

        _game.Undo();
        _pick = -1;
        Sync();
    }

    private void Explain() =>
        NoticeDialog.Explain(this,
            "성공조건 [바로 앞에 있는 10개의 성배를 성수로 채워라.]" + Environment.NewLine +
            Environment.NewLine +
            "대·중·소의 물바가지를 잘 써서 큰 항아리 속의 성수로 모든 성배를 채워라." +
            Environment.NewLine +
            "탐험자가 움직일 수 있는 것은 물바가지 뿐이다. 큰 항아리는 물을 풀 수도 있고 " +
            "다시 놓을 수도 있다. 바가지와 바가지의 이동으로는 물이 넘칠 일은 없다." +
            Environment.NewLine +
            "성배에서 물이 넘치게 되면 당신은 죽게 된다.");

    private void AskGiveUp()
    {
        if (!ConfirmDialog.Ask(this, "게임을 포기하겠습니까?", "항복")) return;
        _game.GiveUp();
        Close();
    }

    private void Sync()
    {
        _count.Text = $"{_game.Moves}번째";

        foreach (var (slot, image) in _art)
        {
            image.Source = ArtFor(slot);
            image.Visibility = Visibility.Visible;
        }

        foreach (var (slot, box) in _spot)
        {
            // 집은 자리와, 끌고 온 손 밑의 자리에 흰 네모를 두른다.
            box.BorderBrush = slot == _pick || slot == _over ? Ring : Brushes.Transparent;
            if (_now.TryGetValue(slot, out var now)) now.Text = $"{_game.WaterAt(slot)}";
        }

        // 집은 바가지는 제자리에서 감춘다 — 손에 들려 있으니.
        if (_art.TryGetValue(_grab, out var held)) held.Visibility = Visibility.Hidden;

    }

    /// <summary>
    /// 놀이를 한 판 하고, <c>0x004684D0</c> 이 하듯 결과를 알린 뒤 상금을 준다.
    /// </summary>
    /// <remarks>
    /// 게임은 「대실패」와 「다시 한번 찬스」 뒤에 "다시 도전하겠습니까?" 를 묻고
    /// 그러겠다면 문제를 <b>새로 굴려</b> 다시 시작한다(<c>0x00468511</c> 로 돌아간다).
    /// </remarks>
    /// <returns>
    /// 성배를 다 채웠는지(「성공」·「멋지게 성공」). 게임 <c>0x004684D0</c> 은 이때만 0 이 아닌 값을
    /// 내고, 발견 대본의 미니게임 명령(<c>0E 04 00 00</c>)이 그 값으로 갈라진다.
    /// </returns>
    public static bool Play(Window owner, Player player, Random rng, SoundBank? sfx = null)
    {
        // 「대실패」(넘침) 뒤의 다시 하기는 <b>딱 한 번</b>이다 — 0x004684F5 가 깃발을 세우고 0x00468566 이
        // 두 번째부터는 묻지도 않고 0 을 낸다. 「다시 한번 찬스」(쉰 수 넘김)는 그런 문이 없다.
        bool spilledOnce = false;

        while (true)
        {
            var dialog = new GrailPuzzleDialog(rng.Next(GrailPuzzle.Problems.Length))
            {
                Owner = owner,
                _sfx = sfx,
            };
            dialog.ShowDialog();

            switch (dialog._game.Over ?? GrailPuzzle.Result.GaveUp)
            {
                case GrailPuzzle.Result.GaveUp:
                    NoticeDialog.Show(owner, "근성이 없는 녀석이로군···", "성스러운 항아리");
                    return false;

                case GrailPuzzle.Result.Spilled:
                    NoticeDialog.Show(owner, "성배에서 물이 넘쳤다!", "대실패");
                    if (spilledOnce) return false;
                    spilledOnce = true;
                    NoticeDialog.Show(owner, "재주가 없는 녀석이로군···한번 더 찬스를 주겠다",
                                      "성스러운 항아리");
                    if (!ConfirmDialog.Ask(owner, "다시 도전하겠습니까?", "메시지")) return false;
                    break;

                case GrailPuzzle.Result.Slow:
                    NoticeDialog.Show(owner, "성수를 가득 채운 항아리가 뭔가 말하기 시작했습니다!",
                                      "메시지");
                    NoticeDialog.Show(owner,
                                      "재주가 없는 녀석이로군···으음···다시 한번 찬스를 주겠다",
                                      "성스러운 항아리");
                    if (!ConfirmDialog.Ask(owner, "다시 도전하겠습니까?", "메시지")) return false;
                    break;

                case GrailPuzzle.Result.Good:
                    NoticeDialog.Show(owner, "모든 성배를 성수로 채웠다!", "성공");
                    return true;

                case GrailPuzzle.Result.Great:
                    NoticeDialog.Show(owner, "성배로부터 눈부신 빛이 넘치기 시작했다!", "멋지게 성공");
                    NoticeDialog.Show(owner, $"금화 {GrailPuzzle.Prize} 닢을 손에 넣었습니다!", "성공");
                    player.Earn(GrailPuzzle.Prize);
                    return true;
            }
        }
    }
}
