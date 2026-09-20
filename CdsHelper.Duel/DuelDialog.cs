using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CdsHelper.Game.Local.Helpers;
using CdsHelper.Game.UI.Views;
using CdsHelper.Support.Local.Settings;

namespace CdsHelper.Duel;

/// <summary>
/// 「일기토」 화면.
/// </summary>
/// <remarks>
/// 게임의 그리는 자리는 <c>0x004A7050</c> 이다. 위에 배경 384x136, 아래에 눈금판
/// 384x112 를 깔고, 얼굴을 오른쪽(296, 144)·왼쪽(8, 144)에 얹는다.
/// <b>오른쪽이 나, 왼쪽이 적</b>이다.
///
/// <b>그림은 게임 것 그대로다</b> — FIGHTER.CDS 에서 뽑아 <c>asset/duel</c> 에 둔다
/// (<c>tools/extract_duel_art.py</c>). 팔레트가 8비트인 것만 다른 CDS 들과 다르다.
///
/// 눈금 막대는 <c>0x004A71A9</c> 벌이 <b>한 줄씩 세로로 지워</b> 그린다. 그 자리를
/// 풀면 이렇다.
/// <code>
///   길이 = 값 * 72 / 100                       ; 0x004A71B1
///   적(왼쪽)  x 112~183, 오른쪽에 붙어 왼쪽으로 자란다   ; 0x6F + i
///   나(오른쪽) x 200~271, 왼쪽에 붙어 오른쪽으로 자란다  ; 0xC7 + i
///   첫 줄 y = 188(창) = 52(눈금판), 줄마다 16, 높이 8   ; [0xBC]
/// </code>
/// 게임은 몸짓 그림 33장짜리 스프라이트셋 아홉을 애니메이션으로 돌리는데, 여기서는 아직
/// 안 옮겼다 — 한 수씩 눌러 주고받는 것만 한다.
/// </remarks>
internal sealed class DuelDialog : InfoDialog
{
    /// <summary>배경과 눈금판을 합친 크기.</summary>
    private const int SceneWidth = 384, ArenaHeight = 136, PanelHeight = 112;
    private const int SceneHeight = ArenaHeight + PanelHeight;

    /// <summary>화면 점 기준 곱. <see cref="GameUi.PixelZoom"/> 이 배율로 나눈다.</summary>
    private const int Zoom = 2;

    /// <summary>눈금 막대의 자리(<c>0x004A71A9</c> 벌).</summary>
    private const int BarLength = 72, BarHeight = 8;
    private const int TheirBarX = 112, MyBarX = 200;
    private const int BarTop = ArenaHeight + 52, BarStep = 16;

    /// <summary>얼굴 칸(<c>0x004A70E8</c>·<c>0x004A7147</c>).</summary>
    private const int FaceW = 84, FaceH = 88;
    private const int MyFaceX = 292, TheirFaceX = 8, FaceY = ArenaHeight + 8;

    /// <summary>이름 칸 — 눈금판에서 잰 것이다.</summary>
    private const int TheirNameX = 112, MyNameX = 200, NameY = ArenaHeight + 26;

    private static readonly Brush Spent = Frozen(Color.FromArgb(0xD0, 0x14, 0x10, 0x0E));
    private static readonly Brush Blood = Frozen(Color.FromRgb(0xC8, 0x3A, 0x30));
    /// <summary>상대 얼굴 자리를 채우는 푸른색. 내 쪽 <see cref="Blood"/> 와 짝이다.</summary>
    private static readonly Brush Foe = Frozen(Color.FromRgb(0x6C, 0xA8, 0xD8));

    /// <summary>
    /// 싸움꾼 그림 — <c>FIGHTER.CDS</c> 의 몸짓 스프라이트셋 아홉, 한 스프라이트셋이 144x136 짜리 33장이다.
    /// </summary>
    /// <remarks>
    /// 색인에서 <b>160</b> 을 빼야 팔레트에 닿는다(다른 CDS 는 74 다). 마젠타가 바탕이다.
    /// 지고 이기는 대목만 뽑아 두었다 — <c>tools/extract_duel_art.py</c> 의
    /// <c>FIGHTER_KEEP</c> 다.
    /// <code>
    ///   24     맞고 칼을 내린다        25     무릎을 꿇는다
    ///   26~29  엎드린다 — <b>진 자세</b>
    ///   30~32  칼을 치켜든다 — <b>이긴 자세</b>
    /// </code>
    /// </remarks>
    private const int FighterW = 144, FighterH = 136;

    /// <summary>진 쪽과 이긴 쪽이 짓는 몸짓.</summary>
    private const int FellFrame = 27, WonFrame = 31;

    /// <summary>겨루기 전에 서 있는 자세.</summary>
    private const int ReadyFrame = 0;

    /// <summary>싸우는 배경. 그림과 눈금판이 이 이름으로 짝지어 있다.</summary>
    private const string Arena = "deck";

    /// <summary>
    /// <see cref="Duel.MyPose"/> 를 몸짓 그림으로 — 치는 자리 셋과 막는 몸짓 셋이다.
    /// </summary>
    /// <remarks>
    /// <b>이 짝은 그림을 눈으로 보고 지었다</b>(EXE 에서 뽑은 것이 아니다). 33장을 펴 놓고
    /// 자세가 뚜렷한 것을 골랐다.
    /// <code>
    ///   상단 22  칼을 머리 위로 치켜든다        뛴다   10  두 발이 땅에서 떨어진다
    ///   중단 18  앞으로 곧게 찌른다             피한다 11  몸을 비틀어 옆으로 뺀다
    ///   하단 17  몸을 낮춰 칼을 낮게 뻗는다      웅크린다 9  주저앉아 몸을 웅크린다
    /// </code>
    /// </remarks>
    private static readonly int[] PoseFrame = [22, 18, 17, 10, 11, 9];

    private readonly Image _myArt = new() { Width = FighterW, Height = FighterH };
    private readonly Image _theirArt = new() { Width = FighterW, Height = FighterH };

    private readonly Duel _game;
    private readonly Canvas _scene = new() { Width = SceneWidth, Height = SceneHeight };

    /// <summary>막대의 «빈» 쪽을 덮는 조각. 눈금판의 막대 무늬가 나머지로 비친다.</summary>
    private readonly Border[] _theirGap = new Border[Duel.Zones];
    private readonly Border[] _myGap = new Border[Duel.Zones];
    private readonly GameUi.GameLabel[] _theirNum = new GameUi.GameLabel[Duel.Zones];
    private readonly GameUi.GameLabel[] _myNum = new GameUi.GameLabel[Duel.Zones];

    private readonly GameUi.GameLabel _step = new(GameFont.WhiteColor)
    {
        Bold = false,
        HorizontalAlignment = HorizontalAlignment.Left,
    };

    private readonly GameUi.GameLabel _line = new(GameFont.WhiteColor)
    {
        HorizontalAlignment = HorizontalAlignment.Left,
    };
    private readonly GameButton[] _pick = new GameButton[Duel.Zones];
    private readonly GameButton _blow;

    private bool _useBlow;

    private DuelDialog(Duel game)
    {
        _game = game;

        // 배경과 눈금판은 <b>짝</b>이다 — 뽑을 때 duel-<배경>.png · duel-panel-<배경>.png 로 났다.
        // 예전에는 "duel-panel.png" 를 찾아 그림이 없어 그냥 밤색 판만 보였다.
        Lay(Picture($"duel-{Arena}.png"), 0, 0, SceneWidth, ArenaHeight);
        Lay(Picture($"duel-panel-{Arena}.png"), 0, ArenaHeight, SceneWidth, PanelHeight);

        // 얼굴은 MALE.CDS 에서 꺼낸다. 못 읽으면 예전처럼 빛깔 칸으로 물러선다.
        var book = Portraits.Open(Path.GetDirectoryName(AppSettings.LastSaveFilePath) ?? "");
        Face(TheirFaceX, Foe, book, _game.Theirs.Face);
        Face(MyFaceX, Blood, book, _game.Mine.Face);

        Caption(_game.Theirs.Name, TheirNameX);
        Caption(_game.Mine.Name, MyNameX);

        for (int zone = 0; zone < Duel.Zones; zone++)
        {
            int top = BarTop + zone * BarStep;

            // 적 막대는 오른쪽에 붙으니 빈 쪽이 왼쪽이다.
            _theirGap[zone] = BarGap(TheirBarX, top);
            _myGap[zone] = BarGap(MyBarX, top);

            _theirNum[zone] = Number(TheirBarX - 26, top - 2);
            _myNum[zone] = Number(MyBarX + BarLength + 2, top - 2);
        }

        _scene.Background = Brushes.Transparent;
        _scene.MouseLeftButtonDown += (_, e) => e.Handled = true;

        double zoom = GameUi.PixelZoom(this, Zoom);
        _scene.LayoutTransform = new ScaleTransform(zoom, zoom);

        for (int zone = 0; zone < Duel.Zones; zone++)
        {
            int here = zone;
            _pick[zone] = new GameButton("", () => Take(here));
        }
        _blow = new GameButton("필살", Toggle);

        // <b>창틀은 그대로 둔다.</b> 한때 미니 게임들처럼 금빛 액자만 두르고 글을 눈금판에
        // 얹어 봤는데, 글 바탕이 얼굴과 막대를 덮어 어수선해졌다. 제목 줄의 닫기 단추도
        // 겨루는 도중에 나갈 길이라 없애면 안 된다.
        var rows = new StackPanel();
        rows.Children.Add(_step);
        rows.Children.Add(_line);
        rows.Children.Add(Gap(4));
        rows.Children.Add(_scene);

        Build("일기토", rows, SceneWidth * zoom + 30, SceneHeight * zoom + 96,
              _pick[0], _pick[1], _pick[2], _blow,
              new GameButton("설명", Explain));

        MouseRightButtonUp += (_, e) =>
            GameUi.ContextMenuAt(this, e.GetPosition(this), Commands());

        Loaded += (_, _) => PoseRound();   // 겨루기 전에는 둘 다 선 자세다

        KeyDown += (_, e) =>
        {
            if (e.Key is Key.D1 or Key.NumPad1) Take(0);
            else if (e.Key is Key.D2 or Key.NumPad2) Take(1);
            else if (e.Key is Key.D3 or Key.NumPad3) Take(2);
            else if (e.Key == Key.Space) Toggle();
        };

        Sync();
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
        RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.NearestNeighbor);
        Canvas.SetLeft(image, x);
        Canvas.SetTop(image, y);
        _scene.Children.Add(image);
    }

    private static BitmapImage? Picture(string name)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "asset", "duel", name);
        if (!File.Exists(path)) return null;

        var bmp = new BitmapImage();
        bmp.BeginInit();
        bmp.UriSource = new Uri(path);
        bmp.CacheOption = BitmapCacheOption.OnLoad;
        bmp.EndInit();
        bmp.Freeze();
        return bmp;
    }

    /// <summary>얼굴 자리. <b>초상화는 아직 안 옮겨</b> 빛깔 판으로 둔다.</summary>
    private void Face(int x, Brush tint, Portraits? book, int face)
    {
        if (book?.TryGetBgra(face, false) is { } pixels)
        {
            var art = new Image
            {
                Width = FaceW,
                Height = FaceH,
                Source = BitmapSource.Create(Portraits.Width, Portraits.Height, 96, 96,
                                             PixelFormats.Bgra32, null, pixels,
                                             Portraits.Width * 4),
                IsHitTestVisible = false,
            };
            RenderOptions.SetBitmapScalingMode(art, BitmapScalingMode.NearestNeighbor);
            Canvas.SetLeft(art, x);
            Canvas.SetTop(art, FaceY);
            _scene.Children.Add(art);
            return;
        }

        var box = new Border
        {
            Width = FaceW,
            Height = FaceH,
            Background = tint,
            Opacity = 0.55,
            IsHitTestVisible = false,
        };
        Canvas.SetLeft(box, x);
        Canvas.SetTop(box, FaceY);
        _scene.Children.Add(box);
    }

    private void Caption(string text, int x)
    {
        var label = new GameUi.GameLabel(GameFont.WhiteColor)
        {
            Text = text,
            IsHitTestVisible = false,
        };
        Canvas.SetLeft(label, x + 2);
        Canvas.SetTop(label, NameY);
        _scene.Children.Add(label);
    }

    private Border BarGap(int x, int y)
    {
        var box = new Border
        {
            Width = 0,
            Height = BarHeight,
            Background = Spent,
            IsHitTestVisible = false,
        };
        Canvas.SetLeft(box, x);
        Canvas.SetTop(box, y);
        _scene.Children.Add(box);
        return box;
    }

    private GameUi.GameLabel Number(int x, int y)
    {
        var label = new GameUi.GameLabel(GameFont.WhiteColor) { IsHitTestVisible = false };
        Canvas.SetLeft(label, x);
        Canvas.SetTop(label, y);
        _scene.Children.Add(label);
        return label;
    }

    private void Toggle()
    {
        if (!_game.CanBlow) return;

        _useBlow = !_useBlow;
        Sync();
    }

    private void Take(int pick)
    {
        if (_game.Over != null) return;

        _game.Play(pick, _useBlow);
        _useBlow = false;
        Sync();
        PoseRound();

        if (_game.Over == null) return;

        // 끝난 자세를 <b>보여 주고</b> 닫는다 — 곧바로 닫으면 이긴 몸짓이 안 보인다.
        Finish(_game.Over == true);

        var linger = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(LingerMillis),
        };
        linger.Tick += (_, _) => { linger.Stop(); Close(); };
        linger.Start();
    }

    /// <summary>끝난 자세를 보여 주는 시간(ms).</summary>
    private const int LingerMillis = 900;

    /// <summary>
    /// 끝난 자리 — <b>진 쪽은 엎드리고 이긴 쪽은 칼을 치켜든다</b>.
    /// </summary>
    /// <remarks>
    /// 지면 상대가 한마디 한다. 게임은 다섯 마디를 굴려 고른다
    /// (<c>0x004A9E96</c>~<c>0x004A9EB6</c>).
    /// </remarks>
    private void Finish(bool won)
    {
        Pose(_myArt, MyArtX, won ? WonFrame : FellFrame, mirror: false, _game.Mine.Kit);
        Pose(_theirArt, TheirArtX, won ? FellFrame : WonFrame, mirror: true, _game.Theirs.Kit);
        _scene.UpdateLayout();

        if (won) return;

        NoticeDialog.Show(this, Taunts[new Random().Next(Taunts.Length)], "일기토");
    }

    /// <summary>진 쪽에게 상대가 하는 말(<c>0x00534560</c> 벌 다섯).</summary>
    private static readonly string[] Taunts =
    [
        "죽어라!",
        "상대를 잘못 만난 것 같군...",
        "미안하지만, 죽어줘야겠네.",
        "네 여행도 여기까지다.",
        "저 세상에서나 후회하거라.",
    ];

    /// <summary>
    /// 싸움꾼을 그 자세로 세운다. 그림은 <b>왼쪽을 보고</b> 그려져 있어 적은 뒤집는다.
    /// </summary>
    private void Pose(Image art, double x, int frame, bool mirror, int kit)
    {
        var picture = Picture($"duel-fighter-{kit}-{frame:00}.png");
        if (picture == null) return;

        art.Source = picture;
        RenderOptions.SetBitmapScalingMode(art, BitmapScalingMode.NearestNeighbor);
        art.RenderTransformOrigin = new Point(0.5, 0.5);
        art.RenderTransform = mirror ? new ScaleTransform(-1, 1) : null;
        Canvas.SetLeft(art, x);
        Canvas.SetTop(art, ArenaHeight - FighterH);
        Panel.SetZIndex(art, 20);
        if (!_scene.Children.Contains(art)) _scene.Children.Add(art);
    }

    /// <summary>바로 앞 수의 몸짓을 둘 다 세운다.</summary>
    private void PoseRound()
    {
        Pose(_myArt, MyArtX, Frame(_game.MyPose), mirror: false, _game.Mine.Kit);
        Pose(_theirArt, TheirArtX, Frame(_game.TheirPose), mirror: true, _game.Theirs.Kit);
    }

    /// <summary>몸짓 번호를 그림 번호로. 아직 한 수도 안 두었으면 선 자세다.</summary>
    private static int Frame(int pose) =>
        pose >= 0 && pose < PoseFrame.Length ? PoseFrame[pose] : ReadyFrame;

    /// <summary>싸움꾼이 서는 자리 — 배경 가운데를 두고 좌우로 갈린다.</summary>
    private const int TheirArtX = 24, MyArtX = SceneWidth - FighterW - 24;

    /// <summary>오른쪽 단추 차림표 — 칠 자리 셋과 필살 · 설명이다.</summary>
    private IReadOnlyList<(string, Action?)> Commands()
    {
        string what = _game.Now == Duel.Step.Guard ? "막는다" : "친다";
        var rows = new List<(string, Action?)>();
        for (int zone = 0; zone < Duel.Zones; zone++)
        {
            int here = zone;
            string name = _game.Now == Duel.Step.Guard
                        ? Duel.GuardNames[zone] : Duel.ZoneNames[zone];
            rows.Add(($"{name} {what}", () => Take(here)));
        }

        rows.Add((_useBlow ? "필살 — 켬" : "필살", _game.CanBlow ? Toggle : null));
        rows.Add(("게임 설명", Explain));
        rows.Add(("게임 복귀", () => { }));
        return rows;
    }

    private void Explain() =>
        NoticeDialog.Show(this,
            "몸을 상·중·하 세 자리로 나누고 자리마다 따로 체력이 붙습니다. " +
            "어느 한 자리가 0 이 되면 집니다." + Environment.NewLine + Environment.NewLine +
            "먼저 자리를 골라 선제를 가립니다 — 상이 중을, 중이 하를, 하가 상을 " +
            "이기고, 같은 자리를 고르면 무력과 검술로 가릅니다." + Environment.NewLine +
            "이기면 칠 자리를, 지면 막을 몸짓을 고릅니다. 상단은 뛰면 정통으로 " +
            "맞고, 중단은 웅크리면, 하단은 피하면 정통입니다." + Environment.NewLine +
            "정통으로 맞으면 같은 쪽이 한 번 더 칩니다." + Environment.NewLine +
            "필살은 한 판에 한 번뿐이고, 피해가 두 배가 됩니다.", "일기토");

    private void Sync()
    {
        _step.Text = _game.Now switch
        {
            Duel.Step.First => $"  {_game.Moves + 1}수   자리를 골라 선제를 가린다",
            Duel.Step.Strike => $"  {_game.Moves + 1}수   내가 친다" +
                                (_useBlow ? "   [필살]" : ""),
            _ => $"  {_game.Moves + 1}수   막는다",
        };
        _line.Text = "  " + _game.Line;

        bool guard = _game.Now == Duel.Step.Guard;
        for (int zone = 0; zone < Duel.Zones; zone++)
        {
            _pick[zone].Text = guard ? Duel.GuardNames[zone] : Duel.ZoneNames[zone];

            Fill(_theirGap[zone], TheirBarX, _game.Theirs.Health[zone], toRight: false);
            Fill(_myGap[zone], MyBarX, _game.Mine.Health[zone], toRight: true);

            _theirNum[zone].Text = $"{_game.Theirs.Health[zone],3}";
            _myNum[zone].Text = $"{_game.Mine.Health[zone],3}";
        }

        _blow.On = _game.CanBlow;
        _blow.Text = _useBlow ? "필살 *" : "필살";
    }

    /// <summary>
    /// 막대의 빈 쪽을 덮는다. 적 막대는 오른쪽에, 내 막대는 왼쪽에 붙는다.
    /// </summary>
    private static void Fill(Border gap, int x, int health, bool toRight)
    {
        int length = Math.Clamp(health, 0, Duel.Full) * BarLength / Duel.Full;
        gap.Width = BarLength - length;
        Canvas.SetLeft(gap, toRight ? x + length : x);
    }

    /// <summary>
    /// 한 판 한다. 돌려주는 값은 이겼는지다 — 그만두면 null.
    /// </summary>
    public static bool? Play(Window owner, Duel game)
    {
        var dialog = new DuelDialog(game) { Owner = owner };
        dialog.ShowDialog();
        return dialog._game.Over;
    }
}
