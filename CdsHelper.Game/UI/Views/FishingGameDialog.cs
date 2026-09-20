using System.IO;
using System.Windows.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CdsHelper.Game.Local.Helpers;
using CdsHelper.Support.Local.Models;

namespace CdsHelper.Game.UI.Views;

/// <summary>
/// 미니 게임 「낚시 게임」 화면.
/// </summary>
/// <remarks>
/// 게임의 <c>0x0047BDD0</c> 이고, 규칙은 <see cref="FishingGame"/> 에 모아 두었다.
///
/// <b>그림은 게임 것 그대로다</b> — FISHING.CDS 에서 뽑아 <c>asset/minigame</c> 에 둔다
/// (<c>tools/extract_minigame_art.py</c>). 자리 표는 EXE 의 <c>0x00569194</c> 이고
/// 배경은 <c>0x0047ADF2</c> 가 <b>336x392</b> 를 통째로 찍는다.
///
/// 격자 자리는 <b>그림에서 재어</b> 썼다 — 물빛 줄이 세로로 <c>x = 48 + 칸 * 40</c>
/// 일곱, 가로로 <c>y = 63 + 줄 * 40</c> 일곱이라 그 사이가 여섯 줄이다. 칸 사이가
/// 마흔인 것은 <c>0x0047A8F4</c> 의 <c>40 * 칸</c> 과 맞는다.
///
/// 바늘은 <b>스스로 내려간다</b> — 한 틱에 한 점이고 한 줄이 마흔 틱이다
/// (<c>0x0047AB0F</c>). 옆으로 가는 동안은 가로로도 한 틱에 한 점씩 밀려 딱 한 칸을
/// 옮겨 간다. 게임은 <c>0x00428000(0, 0)</c> 으로 <b>안 기다리고</b> 그리는 대로
/// 도는데, 여기서는 20밀리초에 한 틱으로 잡았다(다 내려가는 데 여섯 해 남짓).
///
/// <b>바다 것들은 처음부터 다 보인다.</b> 어디에 오징어와 낙지가 있는지 보고 피해
/// 가는 놀이라 감추면 안 된다. 대어도 바닥에 보인다.
/// </remarks>
internal sealed class FishingGameDialog : InfoDialog
{
    private const int SceneWidth = 336, SceneHeight = 392;

    /// <summary>
    /// 그림을 <b>화면 점</b> 기준으로 몇 배로 놓을지. 1 이면 원본 크기다.
    /// </summary>
    /// <remarks>
    /// <see cref="GameUi.PixelZoom"/> 이 <b>모니터 배율로 나눠</b> 준다. 그냥 2 를
    /// 걸면 배율 175% 인 화면에서 3.5배가 돼 점이 뭉갠다.
    /// </remarks>
    private const int Zoom = 2;

    /// <summary>물빛 세로줄 자리. 그림에서 잰 것이다(첫 세로줄 x 48, 가로줄은 y 103 부터 40 마다).</summary>
    /// <remarks>
    /// 게임은 배경을 <c>(8, 0)</c> 에 찍는다(<c>0x0047AE06</c>). 그래서 아래 자리들은 모두
    /// 게임이 그리는 자리에서 <b>x 만 8 을 뺀 값</b>이다. y 는 그대로다.
    /// </remarks>
    private const int Step = 40;

    private const int BeastSize = 32, HookSize = 16, FishW = 32, FishH = 16, BigW = 64, BigH = 32;

    /// <summary>한 틱에 얼마나 쉴지. 게임은 안 쉬고 그리는 대로 돈다.</summary>
    private static readonly TimeSpan TickTime = TimeSpan.FromMilliseconds(TickMs);

    /// <summary>
    /// 한 틱에 드는 밀리초. 한 줄(<see cref="FishingGame.TicksPerRow"/> = 40틱)이
    /// <c>40 x 40 = 1.6초</c>다.
    /// </summary>
    /// <remarks>예전에는 20ms 라 한 줄이 0.8초였는데 눈으로 좇기에 너무 빨랐다.</remarks>
    private const int TickMs = 40;

    private readonly FishingGame _game;
    private readonly Canvas _scene = new() { Width = SceneWidth, Height = SceneHeight };
    private readonly Image _hook = new() { Width = HookSize, Height = HookSize };
    private readonly Image _boat = new() { Width = BeastSize, Height = BeastSize };
    private readonly Image _bigOne = new() { Width = BigW, Height = BigH, IsHitTestVisible = false };
    private readonly Image[] _arrow = new Image[2];

    /// <summary>헤엄쳐 다니는 것 열 마리.</summary>
    private readonly Image[] _swim = new Image[FishingGame.Swimmers];
    private readonly GameUi.GameLabel _line = new(GameFont.WhiteColor) { Bold = false };
    private readonly DispatcherTimer _clock = new();

    private FishingGameDialog(Random rng)
    {
        _game = new FishingGame(rng);

        Lay(Picture("fish-bg.png"), 0, 0, SceneWidth, SceneHeight);

        // 바다 것들 — 게임은 (칸*40+0x24, 줄*40+0x72) 에 찍는다(0x0047B1B2).
        // 첫 가로줄(y 103) 밑이 0 줄이다. 그 위 물낯 띠에는 아무것도 없다.
        for (int at = 0; at < FishingGame.Cells; at++)
        {
            int what = _game.CellAt(at);
            if (what < FishingGame.Squid) continue;

            var art = Picture(what == FishingGame.Squid ? "fish-big-1.png" : "fish-big-2.png");
            Lay(art, at % FishingGame.Columns * Step + 0x24 - 8,
                at / FishingGame.Columns * Step + 0x72, BeastSize, BeastSize);
        }

        // 대어(실러캔스)는 바닥에 눕는다 — 64x32 를 (칸*40+0x29, 0x168) 에(0x0047B698).
        Ready(_bigOne, Picture("fish-bigone.png"));
        Canvas.SetLeft(_bigOne, _game.BigOneColumn * Step + 0x29 - 8);
        Canvas.SetTop(_bigOne, FishingGame.FloorY);
        Panel.SetZIndex(_bigOne, 20);
        _scene.Children.Add(_bigOne);

        // 배와 바늘. 배는 바늘을 따라 옆으로도 간다(0x0047AF7B, y 0x26).
        Ready(_boat, Picture("fish-big-0.png"));
        Canvas.SetTop(_boat, 0x26);
        Panel.SetZIndex(_boat, 40);
        _scene.Children.Add(_boat);

        // 헤엄쳐 다니는 것들. 그림은 갈래 둘 x 가는 쪽 둘이다.
        for (int k = 0; k < FishingGame.Swimmers; k++)
        {
            var image = new Image { Width = FishW, Height = FishH, IsHitTestVisible = false };
            RenderOptions.SetBitmapScalingMode(image, GameUi.SpriteScaling);
            Panel.SetZIndex(image, 30);
            _scene.Children.Add(image);
            _swim[k] = image;
        }

        Ready(_hook, Picture("fish-hook.png"));
        Panel.SetZIndex(_hook, 50);
        _scene.Children.Add(_hook);

        // 왼쪽·오른쪽 화살표. 게임도 오른쪽 위에 나란히 둔다.
        for (int i = 0; i < 2; i++)
        {
            int way = i == 0 ? -1 : +1;
            var image = new Image { Width = FishW, Height = FishH, Cursor = Cursors.Hand };
            Ready(image, Picture($"fish-arrow-{i}.png"));
            Canvas.SetLeft(image, 224 + i * FishW);
            Canvas.SetTop(image, 14);
            image.MouseLeftButtonDown += (_, e) => e.Handled = true;
            image.MouseLeftButtonUp += (_, e) => { e.Handled = true; Steer(way); };
            _scene.Children.Add(image);
            _arrow[i] = image;
        }

        // 알림줄은 하늘 자리에 얹는다 — 화살표(x 224)와 안 겹친다.
        _line.FallbackBrush = Brushes.White;
        _line.IsHitTestVisible = false;
        Canvas.SetLeft(_line, 10);
        Canvas.SetTop(_line, 12);
        Panel.SetZIndex(_line, 60);
        _scene.Children.Add(_line);

        _scene.Background = Brushes.Transparent;
        _scene.MouseLeftButtonDown += (_, e) => e.Handled = true;
        // 모니터 배율을 물어 나눠 준다 — 그림 점 하나가 화면 점 하나가 되게.
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

        MouseRightButtonUp += (_, e) =>
            GameUi.ContextMenuAt(this, e.GetPosition(this), Commands());

        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Left) Steer(-1);
            else if (e.Key == Key.Right) Steer(+1);
            else if (e.Key is Key.Down or Key.Enter or Key.Space) LetGo();
        };

        _clock.Interval = TickTime;
        _clock.Tick += (_, _) => Beat();
        Closed += (_, _) => _clock.Stop();

        Sync();
    }

    private static void Ready(Image image, BitmapSource? art)
    {
        RenderOptions.SetBitmapScalingMode(image, GameUi.SpriteScaling);
        image.Source = art;
    }

    /// <summary>그림 한 장을 그 자리에 깐다.</summary>
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

    private void Steer(int way)
    {
        _game.Steer(way);
        Sync();
    }

    /// <summary>
    /// 오른쪽 단추가 부르는 차림표. 예전 아래 단추 줄이 그대로 여기로 왔다.
    /// </summary>
    /// <remarks>
    /// 바늘이 내려가는 동안에는 「떨어뜨린다」가 죽는다 — 예전에는 단추의
    /// <c>On</c> 을 내려 같은 일을 했다.
    /// </remarks>
    private IReadOnlyList<(string, Action?)> Commands() =>
    [
        ("떨어뜨린다", _game.Started || _game.Got != FishingGame.Catch.None ? null : LetGo),
        ("← 왼쪽으로", _game.Started ? () => Steer(-1) : null),
        ("→ 오른쪽으로", _game.Started ? () => Steer(+1) : null),
        ("게임 설명", Explain),
        ("게임 복귀", () => { }),   // 차림표만 닫는다
    ];

    /// <summary>떨어뜨린다. 한 번 놓으면 스스로 내려간다.</summary>
    private void LetGo()
    {
        if (_game.Started || _game.Got != FishingGame.Catch.None) return;

        _game.Drop();
        _clock.Start();
        Sync();
    }

    /// <summary>한 틱.</summary>
    private void Beat()
    {
        // 걸린 뒤에는 셈을 멈추고 끌어 올리는 것만 보인다.
        if (_reeling >= 0) { Reel(); return; }

        if (!_game.Step())
        {
            Sync();
            _reeling = (int)_game.Y;      // 걸린 그 깊이에서 감아올리기 시작한다
            return;
        }
        Sync();
    }

    /// <summary>
    /// 걸린 것을 <b>끌어 올린다</b>. 다 올라오면 창을 닫는다.
    /// </summary>
    /// <remarks>
    /// 게임도 무엇이든 걸리면 그 자리에서 멎지 않고 줄을 감아 올린다. 내려갈 때보다
    /// 빠르게 올린다 — 기다리는 맛이 없는 대목이라 <see cref="ReelStep"/> 만큼씩 당긴다.
    /// </remarks>
    private void Reel()
    {
        _reeling -= ReelStep;
        if (_reeling <= FishingGame.TopY)
        {
            _clock.Stop();
            Close();
            return;
        }
        Canvas.SetTop(_hook, _reeling);

        // 걸린 것도 함께 딸려 올라온다.
        if (_catch != null) Canvas.SetTop(_catch, _reeling + (HookSize - _catch.Height) / 2.0);
    }

    /// <summary>한 틱에 감아 올리는 깊이. 내려갈 때(한 틱에 1)보다 빠르다.</summary>
    private const int ReelStep = 6;

    /// <summary>감아 올리는 중인 깊이. −1 이면 아직 걸리지 않았다.</summary>
    private int _reeling = -1;

    /// <summary>바늘에 딸려 올라오는 것. 없으면 null.</summary>
    private Image? _catch;

    private void Explain() =>
        NoticeDialog.Explain(this,
            "바다에서 바늘을 떨어뜨려서 바닥에 있는 대어를 낚는 게임입니다. " +
            "낚시바늘은 줄을 따라 내려갑니다." + Environment.NewLine +
            "내려가는 도중에 화살표를 클릭하든지 ←→버튼을 누르면 교차하는 데에서 " +
            "낚시바늘을 옆으로 이동할 수 있습니다만, 다음에 교차하는 데에서는 반드시 " +
            "밑으로 내려갑니다.");

    private void Sync()
    {
        // 다음에 어느 쪽으로 꺾는지만 알린다 — 깊이는 화면에 그대로 보이고, 조작 안내는
        // 오른쪽 차림표가 맡는다. 게임 화면에 없는 것을 덧대지 않는다.
        // 지금 건너는 중이면 그쪽, 아니면 <b>다음 꼭짓점에서</b> 꺾겠다고 적어 둔 쪽이다.
        int turn = _game.Lean != 0 ? _game.Lean : _game.Wish;
        string way = turn > 0 ? "오른쪽으로" : turn < 0 ? "왼쪽으로" : "곧장 아래로";
        _line.Text = _game.Started ? $"  다음 교차점에서 {way}" : "";

        // 바늘은 <b>사다리 위에서만</b> 간다 — 가로줄을 건널 때는 높이가 멎고,
        // 다 건넌 뒤에 세로줄을 내려간다(FishingGame.DrawX · DrawY).
        // 게임은 0x0047B6FE 에서 (x=[0xF4], y=[0xF8], 16, 16) 으로 찍는다 — [0xF4] 는 칸*40+0x2E 에서
        // 시작해 건널 때 틱만큼 밀린다. 높이는 <b>그림 윗변</b>이라 바늘 고리가 줄 위에 놓인다.
        Canvas.SetLeft(_hook, 0x2E - 8 + _game.DrawX);
        Canvas.SetTop(_hook, _game.DrawY);

        // 배도 바늘과 같이 옆으로 간다 — 칸*40+0x38 ± 틱(0x0047B0AC · 0x0047B0F0).
        Canvas.SetLeft(_boat, 0x38 - 8 + _game.DrawX);

        // 걸린 순간, 무엇이 걸렸는지 붙잡아 둔다 — 감아 올릴 때 함께 딸려 온다.
        if (_catch == null && _game.Got != FishingGame.Catch.None) Hooked();

        // 헤엄치는 것은 한 줄 시간(마흔 틱)에 한 칸을 간다 — 그 사이를 틱만큼 미끄러진다.
        for (int k = 0; k < _swim.Length; k++)
        {
            var fish = _game.Fish[k];
            int col = fish.Cell % FishingGame.Columns;
            int row = fish.Cell / FishingGame.Columns;
            int tick = _game.Started ? _game.Tick : 0;

            // 머리가 가는 쪽을 본다. 벌 둘 가운데 <b>0 이 왼쪽</b>을 보므로(잉크가 왼쪽에
            // 몰려 있다) 오른쪽으로 가는 갈래 1 에는 <b>1</b> 을 걸어야 한다 —
            // 거꾸로 걸어 두어 지느러미 쪽으로 나아가고 있었다.
            _swim[k].Source = Picture($"fish-small-{fish.Kind * 2 + (fish.Way == 1 ? 1 : 0)}.png");
            // 게임(0x0047B3A2): 오른쪽으로 가면 (칸*40+0x38−0x30+틱, 줄*40+0x68+0x10),
            // 왼쪽으로 가면 (칸*40+0x38+0x0E−틱, 줄*40+0x68+0x0E). 가는 쪽마다 자리가 다르다.
            bool right = fish.Way == 1;
            Canvas.SetLeft(_swim[k], col * Step + 0x38 - 8 + (right ? tick - 0x30 : 0x0E - tick));
            Canvas.SetTop(_swim[k], row * Step + 0x68 + (right ? 0x10 : 0x0E));
        }
    }

    /// <summary>
    /// 걸린 것을 바늘 자리에 붙인다. 헤엄치던 놈이면 그 그림을 그대로 물려받는다.
    /// </summary>
    private void Hooked()
    {
        // 대어는 바닥에 누운 그 그림이 그대로 딸려 올라온다(0x0047B632 가 같은 [0x5691A4] 를 찍는다).
        if (_game.Got == FishingGame.Catch.BigOne)
        {
            _catch = _bigOne;
            Panel.SetZIndex(_catch, 80);
            return;
        }

        string? art = _game.Got switch
        {
            FishingGame.Catch.SquidCaught => "fish-big-1.png",
            FishingGame.Catch.OctopusCaught => "fish-big-2.png",
            // 걸린 그 놈의 그림이다 — 갈래도 보는 쪽도 헤엄칠 때 그대로다.
            FishingGame.Catch.SmallFry or FishingGame.Catch.SmallFryToo when _game.Caught is { } one
                => $"fish-small-{one.Kind * 2 + (one.Way == 1 ? 1 : 0)}.png",
            FishingGame.Catch.SmallFry or FishingGame.Catch.SmallFryToo => "fish-small-0.png",
            _ => null,
        };
        if (art == null) return;

        bool beast = _game.Got is FishingGame.Catch.SquidCaught or FishingGame.Catch.OctopusCaught;
        _catch = new Image
        {
            Source = Picture(art),
            Width = beast ? BeastSize : FishW,
            Height = beast ? BeastSize : FishH,
            IsHitTestVisible = false,
        };
        RenderOptions.SetBitmapScalingMode(_catch, GameUi.SpriteScaling);
        Panel.SetZIndex(_catch, 80);
        Canvas.SetLeft(_catch, 0x2E - 8 + _game.DrawX + (HookSize - _catch.Width) / 2.0);
        Canvas.SetTop(_catch, _game.DrawY + (HookSize - _catch.Height) / 2.0);
        _scene.Children.Add(_catch);
    }

    /// <summary>게임 EXE 의 설명 글 그대로(<c>0x0056EDB0</c>).</summary>
    private static readonly string Rules =
        " 바다에서 바늘을 떨어뜨려서 바닥에 있는 대어를" + Environment.NewLine +
        "낚는 게임입니다. 낚시바늘은 줄을 따라 내려갑니다." + Environment.NewLine +
        "내려가는 도중에 화살표를 클릭하든지 ←→버튼을" + Environment.NewLine +
        "누르면 교차하는 데에서 낚시바늘을 옆으로 이동할 수" + Environment.NewLine +
        "있습니다만, 다음에 교차하는 데에서는 반드시 밑으로" + Environment.NewLine +
        "내려갑니다.";

    /// <summary>
    /// 한 판 한다. 결과 글은 <c>0x0047AD31</c> 의 뜀표 그대로다.
    /// </summary>
    /// <returns>
    /// <b>대어를 낚았는지</b>. 게임은 대어 갈래에서만 결과 <c>[+0x9C] = 1</c> 을 박고
    /// (<c>0x0047AD6C</c>) 나머지는 0 이라, 발견 대본(<c>0E 04 03</c>)은 대어만 이긴 것으로 친다.
    /// </returns>
    public static bool Play(Window owner, Random rng)
    {
        // 판을 열기 전에 설명부터 낸다 — 게임도 그렇다(0x0047BD7E).
        NoticeDialog.Explain(owner, Rules);

        var dialog = new FishingGameDialog(rng) { Owner = owner };
        // 설명에서 확인을 누르면 <b>그 길로 내려간다</b> — 「떨어뜨린다」를 따로 안 누른다.
        dialog.Loaded += (_, _) => dialog.LetGo();
        dialog.ShowDialog();

        switch (dialog._game.Got)
        {
            case FishingGame.Catch.SquidCaught:
                NoticeDialog.Show(owner, "왓! 오징어가 얼굴에 먹물을 토했다!", "오징어를 낚았다");
                break;

            case FishingGame.Catch.OctopusCaught:
                NoticeDialog.Show(owner,
                    "악마의 물고기다! 너무 징그러워서" + Environment.NewLine +
                    "갑판에 내동댕이쳤다.", "낙지를 낚았다");
                break;

            case FishingGame.Catch.SmallFry:
            case FishingGame.Catch.SmallFryToo:
                NoticeDialog.Show(owner,
                    "재수없게 잡어를 낚았군." + Environment.NewLine +
                    "주방장에게 갖다 줄까···", "잡어를 낚았다");
                break;

            case FishingGame.Catch.BigOne:
                NoticeDialog.Show(owner, "잘 됐다! 바다 깊숙히 있는 고기를 낚았다!",
                                  "대어을 낚았다");
                break;

            case FishingGame.Catch.Seabed:
                NoticeDialog.Show(owner,
                    "아무리 당겨도 끌어올릴 수 없다." + Environment.NewLine +
                    "[지구를 낚았다]고 해야하나.", "바닥에 걸렸다");
                break;
        }

        return dialog._game.Got == FishingGame.Catch.BigOne;
    }
}
