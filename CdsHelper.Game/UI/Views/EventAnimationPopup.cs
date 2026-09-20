using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using CdsHelper.Game.Local.Helpers;
using CdsHelper.Support.Local.Helpers;

namespace CdsHelper.Game.UI.Views;

/// <summary>
/// 지도 위에 사건 애니메이션(<c>EVANIME.CDS</c>) 한 장면을 끝까지 돌리고 닫힌다.
/// </summary>
/// <remarks>
/// 게임의 <c>0x0048E820(장면)</c> 이다. 장면 번호를 지도 창 <c>+0x108</c> 에 두고
/// <c>+0x10C</c>(걸음)를 0 으로 놓은 뒤, <c>+0x108</c> 이 -1 이 될 때까지 메시지를 돌린다.
/// <code>
///   0x0048B120  GetTickCount 가 0x005697D4 보다 100ms 넘게 갔으면 결과 2(한 걸음)
///   0x0048AAD8  걸음 = [+0x10C]++ ; 0x0049ABB0(장면, 걸음, 뒷버퍼, 지도 네모)
///   0x0048AB2E  0 이 돌아오면 [+0x108] = -1 — 장면이 끝난다
/// </code>
/// 한 걸음이 <b>0.1초</b>다. 그리는 곳은 <b>지도 영역</b>(메뉴 띠 32점 아래)이고 그 네모로
/// 잘린다(<c>0x0049A620</c>). 지도를 어둡게 덮지 않고 그 위에 비침 색 0 을 빼고 찍는다.
/// 도는 동안 지도는 멈춘다(<c>0x004258A0</c> 이 <c>+0xA0</c> 비트 0 을 끄고
/// <c>0x00425840</c> 이 되켠다). 누르거나 키를 쳐도 안 끝난다 — 16번 장면만 누르면 끝난다
/// (<c>0x0048B006</c>).
///
/// 원본은 지도 영역을 게임 점(640 폭 화면)으로 셈한다. 우리 지도는 크기가 들쭉날쭉하므로
/// 지도 창이 구름을 그리는 셈과 같이 <b>게임 한 점 = 칸 1/16</b> 로 잡아 넓이를 게임 점으로
/// 바꾸고, 셈은 게임 식을 정수 그대로 돌린다.
/// </remarks>
internal sealed class EventAnimationPopup : Window
{
    /// <summary>한 걸음 참(<c>0x0048B14A</c> 의 100).</summary>
    private static readonly TimeSpan StepSpan = TimeSpan.FromMilliseconds(100);

    private readonly SceneSurface _surface;

    private EventAnimationPopup(Rect area, double scale)
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        ShowActivated = false;
        IsHitTestVisible = false;

        WindowStartupLocation = WindowStartupLocation.Manual;
        Left = area.X;
        Top = area.Y;
        Width = area.Width;
        Height = area.Height;

        _surface = new SceneSurface(scale);
        RenderOptions.SetBitmapScalingMode(_surface, GameUi.SpriteScaling);
        RenderOptions.SetEdgeMode(_surface, EdgeMode.Aliased);
        Content = _surface;
    }

    /// <summary>
    /// 장면 하나를 끝까지 돌리고 닫는다. 그림을 못 읽거나 모르는 장면이면 아무 일도 없다.
    /// </summary>
    /// <param name="owner">지도 창.</param>
    /// <param name="game">그림·효과음·곡을 꺼낼 게임.</param>
    /// <param name="scene">장면 번호(<see cref="EventAnimation"/> 의 상수).</param>
    /// <param name="area">지도가 화면에서 차지한 자리(WPF 단위).</param>
    /// <param name="scale">게임 한 점이 WPF 몇 단위인지.</param>
    /// <param name="ship">함대 그림 한가운데 — <paramref name="area"/> 왼쪽 위에서 잰 WPF 단위. 모르면 null.</param>
    public static void Play(Window owner, Engine.Game game, int scene, Rect area, double scale, Point? ship)
    {
        if (area.Width <= 0 || area.Height <= 0 || scale <= 0) return;
        if (game.EventAnims is not { } anims) return;

        Scene? play = scene switch
        {
            EventAnimation.Storm => new StormScene(),
            EventAnimation.Blizzard => new BlizzardScene(),
            EventAnimation.Bush => new BushScene(),
            EventAnimation.Tornado => new TornadoScene(),
            EventAnimation.Aurora => new AuroraScene(),
            EventAnimation.Meteor => new MeteorScene(),
            // 오아시스(0x00497D60) — 물이 비쳤다 사라졌다 한다. 소리가 둘 걸린다.
            EventAnimation.Oasis => new StripScene(6, 0x80, 0x80, 0x22, up: 0x20,
                soundAt: 0x0B, sound: 0x3A, endAt: 0x3A, pick: c => c switch
                {
                    < 0x0B => 0,
                    < 0x11 => 1 + (c - 0x0B) / 2,
                    < 0x17 => 0,                       // 신기루가 한 번 사라진다
                    < 0x1D => c - 0x13,
                    < 0x29 => 10,
                    < 0x32 => 11 + (c - 0x29) / 3,
                    _ => 0,
                }),
            EventAnimation.Landslide => new LandslideScene(),
            // 늪(0x004981D0) — 걸음 열하나부터 천천히 가라앉는다.
            EventAnimation.Swamp => new StripScene(8, 0x60, 0x60, 0x24,
                soundAt: 5, sound: 0x43, soundOff: 0x31, endAt: 0x36,
                pick: c => c < 0x0B ? 0 : c < 0x29 ? 1 + (c - 0x0B) / 2 : 17),
            // 유사(0x004983B0) — 발버둥이 두 번 돌고 나서 빨려 들어간다.
            EventAnimation.Quicksand => new StripScene(9, 0x60, 0x60, 0x25,
                soundAt: 5, sound: 0x44, soundOff: 0x35, endAt: 0x3A, pick: c => c switch
                {
                    < 5 => 0,
                    < 0x11 => (c - 5) / 2,
                    < 0x1D => (c - 0x11) / 2,
                    < 0x27 => 6 + (c - 0x1D) / 2,
                    < 0x29 => 10,
                    < 0x35 => 11 + (c - 0x29) / 2,
                    _ => 16,
                }),
            EventAnimation.Iceberg => new IcebergScene(),
            _ => null,
        };
        if (play == null || !play.Load(anims)) return;

        int w = (int)(area.Width / scale), h = (int)(area.Height / scale);
        Point? at = ship is { } p ? new Point(p.X / scale, p.Y / scale) : null;
        play.Start(w, h, at, new Random());

        // 곡을 끊는 장면은 폭풍 하나다(0x00497768 가 모든 소리를 끄고, 끝에 0x004229F0 이 곡을 되튼다).
        int track = play.StopsMusic ? game.Bgm.Track : -1;
        if (track >= 0) game.Bgm.Stop();
        var sfx = game.Sfx;
        play.Sfx = sfx;
        if (play.SoundPart >= 0) sfx?.Play(play.SoundPart);

        var popup = new EventAnimationPopup(area, scale) { Owner = owner };
        popup.Show();
        try
        {
            Run(popup._surface, play);
        }
        finally
        {
            popup.Close();
            if (play.SoundPart >= 0) sfx?.Stop();          // 끝에 제 소리를 끈다(0x00422A40(소리, 3))
            if (track >= 0) game.Bgm.Play(track);
        }
    }

    /// <summary>0.1초마다 한 걸음씩 그린다. 끝난 걸음에 그린 것도 한 참은 보여 준다.</summary>
    private static void Run(SceneSurface surface, Scene play)
    {
        var frame = new DispatcherFrame();
        int step = 0;
        bool closing = false;

        void Tick()
        {
            if (closing) { frame.Continue = false; return; }
            surface.Draws.Clear();
            bool done = play.Step(step++, surface.Draws);
            surface.Dim = play.Dim;
            surface.InvalidateVisual();
            if (!done) return;
            if (surface.Draws.Count == 0) frame.Continue = false;
            else closing = true;
        }

        var timer = new DispatcherTimer(StepSpan, DispatcherPriority.Normal, (_, _) => Tick(),
                                        Dispatcher.CurrentDispatcher);
        Tick();                                  // 게임도 여는 즉시 첫 걸음(걸음 0)을 그린다
        try
        {
            if (frame.Continue) Dispatcher.PushFrame(frame);
        }
        finally
        {
            timer.Stop();
        }
    }

    // ── 그리기 ──────────────────────────────────────────────────────────────

    /// <summary>한 걸음에 찍을 그림 한 장 — 게임 점 좌표.</summary>
    private readonly record struct Draw(BitmapSource Art, int X, int Y);

    /// <summary>게임 점으로 받은 그림들을 배율대로 찍고 지도 네모로 자른다(<c>0x0049A620</c>).</summary>
    private sealed class SceneSurface(double scale) : FrameworkElement
    {
        public List<Draw> Draws { get; } = [];

        /// <summary>지도를 덮는 검은 막의 짙기(0 없음 ~ 1 깜깜). 오로라가 쓴다.</summary>
        public double Dim { get; set; }

        protected override void OnRender(DrawingContext dc)
        {
            dc.PushClip(new RectangleGeometry(new Rect(RenderSize)));
            if (Dim > 0)
            {
                var shade = new SolidColorBrush(Color.FromArgb(
                    (byte)Math.Clamp(Dim * 255, 0, 255), 0, 0, 0));
                shade.Freeze();
                dc.DrawRectangle(shade, null, new Rect(RenderSize));
            }
            foreach (var d in Draws)
                dc.DrawImage(d.Art, new Rect(d.X * scale, d.Y * scale,
                                             d.Art.PixelWidth * scale, d.Art.PixelHeight * scale));
            dc.Pop();
        }
    }

    /// <summary>띠를 장마다 잘라 굳혀 둔다.</summary>
    private static BitmapSource[]? Frames(EventAnimation anims, int part, int width, int frameHeight,
                                         int palette)
    {
        if (anims.TryGetStrip(part, width, frameHeight, palette) is not { } strip) return null;

        var frames = new BitmapSource[strip.Count];
        int stride = strip.Width * 4;
        var one = new uint[strip.Width * strip.FrameHeight];
        for (int f = 0; f < strip.Count; f++)
        {
            Array.Copy(strip.Bgra, f * one.Length, one, 0, one.Length);
            var bmp = BitmapSource.Create(strip.Width, strip.FrameHeight, 96, 96,
                                          PixelFormats.Bgra32, null, one, stride);
            bmp.Freeze();
            frames[f] = bmp;
        }
        return frames;
    }

    // ── 장면 ────────────────────────────────────────────────────────────────

    /// <summary>
    /// 장면 하나. 게임 장면 객체의 세 손 — 읽기·첫자리(A), 한 걸음(B), 걷기(C) — 을 옮긴다.
    /// </summary>
    private abstract class Scene
    {
        /// <summary>여는 참에 내는 효과음 파트. 없으면 -1.</summary>
        public virtual int SoundPart => -1;

        /// <summary>도는 동안 곡을 끊는가.</summary>
        public virtual bool StopsMusic => false;

        /// <summary>이번 걸음에 지도를 덮을 검은 막의 짙기(0~1). 덮지 않는 장면은 0 이다.</summary>
        public virtual double Dim => 0;

        /// <summary>도는 도중에 소리를 내는 장면이 쓴다(오로라가 열 걸음째에 낸다).</summary>
        public SoundBank? Sfx { get; set; }

        public abstract bool Load(EventAnimation anims);

        /// <param name="w">지도 폭(게임 점). 게임의 <c>[0x005AA2D8]</c>.</param>
        /// <param name="h">지도 키(게임 점). 게임의 <c>[0x005AA2DC]</c>.</param>
        /// <param name="ship">함대 그림 한가운데(게임 점).</param>
        /// <param name="rng">게임의 <c>0x004B7C0F(n)</c> 자리.</param>
        public abstract void Start(int w, int h, Point? ship, Random rng);

        /// <summary>한 걸음을 그린다. 장면이 끝났으면 true.</summary>
        /// <param name="count">걸음 수(<c>[0x0061D780]</c>) — 0 부터.</param>
        public abstract bool Step(int count, List<Draw> draws);
    }

    /// <summary>
    /// 13 회오리 — 회오리 둘이 좌우에서 뱀처럼 굽이치며 지나간다(<c>0x0061D268</c>).
    /// </summary>
    /// <remarks>
    /// <code>
    ///   0x00499810  파트 17, 96 x 1024(96x128 여덟 장), 팔레트 43
    ///   0x004997A0  첫자리  0번 x = W, 1번 x = -96, y = (H-128)/2
    ///   0x00499830  소리 0x42(WAVE 파트 38) — 곡은 안 끊는다
    ///   0x004994B0  한 걸음
    ///     0번을 장 (걸음 % 8) 로 찍고 왼쪽으로 W/80 옮긴다(0x00499560)
    ///     걸음 &gt; 10 이면 1번을 장 ((걸음+1) % 8) 로 찍고 오른쪽으로 W/80 옮긴다(0x00499670)
    ///     0번이 왼쪽 끝을 넘고(x+96 &lt; 0) 1번이 오른쪽 끝을 넘으면(x &gt; W) 끝(0x00499780)
    ///   0x0049B060  끝에 소리 0x42 를 끈다
    /// </code>
    /// y 는 네 토막 포물선이다 — 네 등분 자리마다 가운데(H/2)와 ±50 을 오간다.
    /// W 가 640 이면 한 걸음 8점이라 104걸음(10.4초)쯤 돈다.
    /// </remarks>
    /// <summary>
    /// 11 오로라 — 지도를 깜깜하게 덮고 밤하늘에 빛의 장막을 펼친다(<c>0x0061D280</c>).
    /// </summary>
    /// <remarks>
    /// <code>
    ///   0x00499200  파트 18, 640 x 4224(640x192 스물두 장), 팔레트 44
    ///   0x004991A0  첫자리  x = (W − 640) / 2 · y = 0      ; 가로 한가운데, 지도 꼭대기
    ///   0x004992A0  한 걸음([0x0061D780])
    ///     걸음 &lt; 4        0x0049A1A0(4 − 걸음)   ; 4 가 제 밝기 · 0 이 깜깜 — 어두워진다
    ///     4 ~ 9           장 0
    ///     걸음 == 10      소리 0x3A(0x004225A0)
    ///     10 ~ 13         장 걸음 − 9             ; 1 2 3 4
    ///     14 ~ 20         장 0
    ///     21 ~ 41         장 걸음 − 16            ; 5 …
    ///     48 ~ 51         0x0049A1A0(걸음 − 47)   ; 1 2 3 4 — 도로 밝아진다
    ///     52              끝(0x0049939A)
    /// </code>
    /// <b>장 번호가 스물두 장을 넘어선다</b> — 마지막 걸음들이 26번까지 가리키는데 띠에는
    /// 스물두 장뿐이라 여기서는 마지막 장에 붙여 둔다. 게임은 버퍼 밖을 그대로 읽는 듯한데
    /// 그것까지 흉내낼 값어치가 없다.
    ///
    /// 검은 막은 우리 것이다 — 게임은 팔레트를 단계별로 어둡게 하지만(<c>0x0049A1A0</c>)
    /// 우리는 지도 위에 검은 네모를 덮어 같은 꼴을 낸다.
    /// </remarks>
    /// <summary>
    /// 밤하늘을 가르는 <b>유성</b> 장면(<c>0x004992A0</c>, 장면 12) — 뭍에서 8월·12월에 뜬다.
    /// </summary>
    /// <remarks>
    /// EVANIME 파트 19(368x192, 스물여섯 장, 팔레트 45)를 지도 가운데에 한 장씩 얹는다.
    /// <code>
    ///   0~3      지도가 어두워진다(그림 없음)
    ///   4~9      첫 장
    ///   10       소리 0x3A                        14~20  첫 장
    ///   10~13    걸음-9 (1~4)                     21~41  걸음-16 (5~25)
    ///   42~47    첫 장                            48~51  지도가 밝아진다
    ///   52       끝                               곡은 안 끊는다
    /// </code>
    /// </remarks>
    private sealed class MeteorScene : Scene
    {
        private const int FrameW = 368, FrameH = 0xC0;
        private const int DarkStep = 4, SoundStep = 10, LightStep = 48, EndStep = 52;

        /// <summary>가장 어두울 때의 짙기. <b>완전히 덮지 않는다</b> — 원본도 뭍과 물결이 비쳐 보인다.</summary>
        private const double DarkMost = 0.82;

        private BitmapSource[] _art = [];
        private int _x, _y;
        private double _dim;
        private bool _rang;

        /// <summary>곡은 그대로 돈다(<c>0x00499460</c> 은 <c>0x00422A40</c> 을 안 부른다).</summary>
        public override bool StopsMusic => false;

        public override double Dim => _dim;

        /// <summary>여는 참에는 소리가 없다 — 열 걸음째에 0x3A 를 낸다.</summary>
        public override int SoundPart => -1;

        public override bool Load(EventAnimation anims)
        {
            _art = Frames(anims, 19, FrameW, FrameH, 0x2D) ?? [];
            return _art.Length > 0;
        }

        public override void Start(int w, int h, Point? ship, Random rng)
        {
            _x = (w - FrameW) / 2;
            _y = 0;
            _dim = 0;
            _rang = false;
        }

        public override bool Step(int step, List<Draw> draws)
        {
            if (step >= EndStep) { _dim = 0; return true; }

            // 어두워지고 밝아지는 동안에는 그림이 없다 — 지도만 여닫힌다.
            if (step < DarkStep) { _dim = DarkMost * step / DarkStep; return false; }
            if (step >= LightStep) { _dim = DarkMost * (EndStep - step) / DarkStep; return false; }

            _dim = DarkMost;

            if (step >= SoundStep && !_rang)
            {
                _rang = true;
                Sfx?.Play(0x3A - WaveBank.FirstSoundId);
            }

            int frame = step switch
            {
                >= SoundStep and < 14 => step - 9,
                >= 21 and < 42 => step - 16,
                _ => 0,
            };
            draws.Add(new Draw(_art[Math.Clamp(frame, 0, _art.Length - 1)], _x, _y));
            return false;
        }
    }

    /// <summary>
    /// 밤하늘에 펴지는 <b>오로라</b> 장면(<c>0x00498FF0</c>, 장면 11) — 발견 대본의 특수 조우 4 가 부른다.
    /// </summary>
    /// <remarks>
    /// EVANIME 파트 18(640x192, 스물두 장, 팔레트 44)을 지도 가운데에 얹는다. 유성과 달리 <b>곡을 끊고</b>
    /// 일흔세 걸음(7.3초)을 도는데, 장이 오르내리기를 되풀이해 빛의 장막이 일렁이는 것처럼 보인다.
    /// <code>
    ///   0~3      어두워진다        10   소리 0x45        63   소리를 끈다
    ///   10~22    걸음-9            23~29  36-걸음        30~42  걸음-23
    ///   43~47    62-걸음           48~54  걸음-33        55~58  76-걸음
    ///   59~62    걸음-41           그 밖 첫 장           69~72  밝아진다 · 73 끝
    /// </code>
    /// </remarks>
    private sealed class AuroraScene : Scene
    {
        private const int FrameW = 640, FrameH = 0xC0;
        private const int DarkStep = 4, SoundStep = 10, HushStep = 63, LightStep = 69, EndStep = 73;
        private const double DarkMost = 0.82;

        private BitmapSource[] _art = [];
        private int _x, _y;
        private double _dim;
        private bool _rang, _hushed;

        public override bool StopsMusic => true;

        public override double Dim => _dim;

        public override int SoundPart => -1;

        public override bool Load(EventAnimation anims)
        {
            _art = Frames(anims, 18, FrameW, FrameH, 0x2C) ?? [];
            return _art.Length > 0;
        }

        public override void Start(int w, int h, Point? ship, Random rng)
        {
            _x = (w - FrameW) / 2;
            _y = 0;
            _dim = 0;
            _rang = _hushed = false;
        }

        public override bool Step(int step, List<Draw> draws)
        {
            if (step >= EndStep) { _dim = 0; return true; }

            if (step < DarkStep) { _dim = DarkMost * step / DarkStep; return false; }
            if (step >= LightStep) { _dim = DarkMost * (EndStep - step) / DarkStep; return false; }

            _dim = DarkMost;

            if (step >= SoundStep && !_rang)
            {
                _rang = true;
                Sfx?.Play(0x45 - WaveBank.FirstSoundId);
            }
            if (step >= HushStep && !_hushed)
            {
                _hushed = true;
                Sfx?.Stop();                       // 0x00422A40(0x45, 3)
            }

            int frame = step switch
            {
                >= 10 and <= 22 => step - 9,
                >= 23 and <= 29 => 36 - step,
                >= 30 and <= 42 => step - 23,
                >= 43 and <= 47 => 62 - step,
                >= 48 and <= 54 => step - 33,
                >= 55 and <= 58 => 76 - step,
                >= 59 and <= 62 => step - 41,
                _ => 0,
            };
            draws.Add(new Draw(_art[Math.Clamp(frame, 0, _art.Length - 1)], _x, _y));
            return false;
        }
    }

    private sealed class TornadoScene : Scene
    {
        private BitmapSource[] _art = [];
        private int _w, _h;
        private readonly int[] _x = new int[2], _y = new int[2];

        public override int SoundPart => 0x42 - WaveBank.FirstSoundId;

        public override bool Load(EventAnimation anims)
        {
            _art = Frames(anims, 17, 0x60, 0x80, 0x2B) ?? [];
            return _art.Length >= 8;
        }

        public override void Start(int w, int h, Point? ship, Random rng)
        {
            _w = w; _h = h;
            _x[0] = w;
            _x[1] = -0x60;
            _y[0] = _y[1] = (h - 0x80) / 2;
        }

        public override bool Step(int count, List<Draw> draws)
        {
            draws.Add(new Draw(_art[count % 8], _x[0], _y[0]));
            MoveLeft();
            if (count > 10)
            {
                draws.Add(new Draw(_art[(count + 1) % 8], _x[1], _y[1]));
                MoveRight();
            }
            return _x[0] + 0x60 < 0 && _x[1] > _w;
        }

        /// <summary>0번 — 왼쪽으로 간다(<c>0x00499560</c>).</summary>
        private void MoveLeft()
        {
            int d = Math.Max(1, _w / 0x50);
            int x = _x[0] -= d;
            int half = _h / 2;
            int t;
            if (x >= _w * 3 / 4) { t = (x - _w * 3 / 4) / d; _y[0] = half - t * t / 8 + 50; }
            else if (x >= _w / 2) { t = (_w / 2 - x) / d + 20; _y[0] = half - t * t / 8 + 50; }
            else if (x >= _w / 4) { t = (x - _w / 4) / d; _y[0] = t * t / 8 + half - 50; }
            else { t = 20 - x / d; _y[0] = t * t / 8 + half - 50; }
        }

        /// <summary>1번 — 오른쪽으로 간다(<c>0x00499670</c>).</summary>
        private void MoveRight()
        {
            int d = Math.Max(1, _w / 0x50);
            int x = _x[1] += d;
            int half = _h / 2;
            int t;
            if (x <= _w / 4) { t = (_w / 4 - x) / d; _y[1] = half - t * t / 8 + 50; }
            else if (x <= _w / 2) { t = (x - _w / 2) / d + 20; _y[1] = half - t * t / 8 + 50; }
            else if (x <= _w * 3 / 4) { t = (_w * 3 / 4 - x) / d; _y[1] = t * t / 8 + half - 50; }
            else { t = (x - _w) / d + 20; _y[1] = t * t / 8 + half - 50; }
        }
    }

    /// <summary>
    /// 14 유빙 — 얼음덩이가 흔들리며 떠 있고 밑동에 물보라가 인다(<c>0x0061D7A8</c>).
    /// </summary>
    /// <remarks>
    /// <code>
    ///   0x00499BC0  파트 15, 192 x 384(192x96 넉 장), 팔레트 0x2A   ; 얼음덩이
    ///   0x00499BDE  파트 16,  32 x 256( 32x32 여덟 장), 팔레트 0x2A ; 물보라
    ///   0x00499C40  여는 참에 소리 0x36
    ///   0x004998B0  얼음덩이 장 = (걸음 % 8) 이 0·1 → 0 · 4·5 → 2 · 그 밖 → 1
    ///   0x004999B0  물보라   장 = (걸음 % 4) 이 0 → 0 · 2 → 2 · 그 밖 → 1
    /// </code>
    /// <b>부딪히고 나서의 갈래는 안 옮겼다</b> — 원본은 배가 닿은 걸음(<c>+0x18</c>)을 적어 두고
    /// 장 3 으로 바꾼 뒤 여섯 걸음 동안 좌우로 4 점씩 흔든다. 우리는 부딪히는 자리가 따로 없어
    /// 떠 있는 결만 돌린다. <b>도는 길이도 원본에 없다</b> — 원본은 지도가 그만두라 할 때까지
    /// 도는데, 여기서는 여덟 걸음짜리 한 바퀴를 세 번 돌고 끝낸다.
    /// </remarks>
    private sealed class IcebergScene : Scene
    {
        private const int BergW = 0xC0, BergH = 0x60, SprayW = 0x20, SprayH = 0x20;

        /// <summary>여덟 걸음짜리 한 바퀴를 세 번.</summary>
        private const int Steps = 8 * 3;

        public override int SoundPart => 0x36;

        private BitmapSource[] _berg = [], _spray = [];
        private int _x, _y;

        public override bool Load(EventAnimation anims)
        {
            _berg = Frames(anims, 15, BergW, BergH, 0x2A) ?? [];
            _spray = Frames(anims, 16, SprayW, SprayH, 0x2A) ?? [];
            return _berg.Length >= 3;
        }

        public override void Start(int w, int h, Point? ship, Random rng)
        {
            var at = ship ?? new Point(w / 2.0, h / 2.0);
            _x = (int)at.X - BergW / 2;
            _y = (int)at.Y - BergH / 2;
        }

        public override bool Step(int c, List<Draw> draws)
        {
            if (c >= Steps) return true;

            int m = c % 8;
            int f = m < 2 ? 0 : m is 4 or 5 ? 2 : 1;
            draws.Add(new Draw(_berg[Math.Clamp(f, 0, _berg.Length - 1)], _x, _y));

            if (_spray.Length == 0) return false;
            int k = c % 4 switch { 0 => 0, 2 => 2, _ => 1 };
            draws.Add(new Draw(_spray[Math.Clamp(k, 0, _spray.Length - 1)],
                               _x + (BergW - SprayW) / 2, _y + BergH - SprayH / 2));
            return false;
        }
    }

    /// <summary>
    /// 5 사태 — 함대 자리 위로 바위가 무너져 내린다(<c>0x0061E008</c>).
    /// </summary>
    /// <remarks>
    /// 다섯 가운데 <b>걸음별 장 표까지 푼 것은 이것뿐</b>이다(<c>0x00497F80</c>).
    /// <code>
    ///   0x00498160  파트 7, 128 x 1920(128x128 열다섯 장), 팔레트 0x23
    ///   c &lt; 5              아직 안 찍는다
    ///   5 &lt;= c &lt; 11        장 (c-5)/2               ; c==5 에 소리 0x3C
    ///   11 &lt;= c &lt; 23       장 3 + ((c-11) % 6)/2    ; 3,3,4,4,5,5 되풀이
    ///   23 &lt;= c &lt; 27       장 3 + (c-23)/2
    ///   27 &lt;= c &lt; 43       장 6 + (c-27)/2
    ///   43 &lt;= c &lt; 49       장 14                    ; c==43 에 소리 끔
    ///   49 &lt;= c &lt; 54       사라지는 동안
    /// </code>
    /// </remarks>
    private sealed class LandslideScene : Scene
    {
        private const int FrameW = 0x80, FrameH = 0x80;

        private BitmapSource[] _art = [];
        private int _x, _y;

        public override bool Load(EventAnimation anims)
        {
            _art = Frames(anims, 7, FrameW, FrameH, 0x23) ?? [];
            return _art.Length >= 15;
        }

        public override void Start(int w, int h, Point? ship, Random rng)
        {
            var at = ship ?? new Point(w / 2.0, h / 2.0);
            int left = (int)at.X - 24, top = (int)at.Y - 24;
            _x = left + (0x30 - FrameW) / 2;
            _y = top + (0x30 - FrameH) / 2 - 7;
        }

        public override bool Step(int c, List<Draw> draws)
        {
            if (c >= 0x36) return true;
            if (c == 5) Sfx?.Play(0x3C);
            if (c == 0x2B) Sfx?.Stop();
            if (c < 5) return false;

            int f = c switch
            {
                < 0x0B => (c - 5) / 2,
                < 0x17 => 3 + (c - 0x0B) % 6 / 2,
                < 0x1B => 3 + (c - 0x17) / 2,
                < 0x2B => 6 + (c - 0x1B) / 2,
                _ => 14,
            };
            draws.Add(new Draw(_art[Math.Clamp(f, 0, _art.Length - 1)], _x, _y));
            return false;
        }
    }

    /// <summary>
    /// 함대 자리에서 띠 한 벌을 처음부터 끝까지 넘기는 장면.
    /// </summary>
    /// <remarks>
    /// 오아시스·사태·늪·유사·유빙 다섯이 이것을 쓴다. 그림 파트·크기·팔레트는 EXE 에서 그대로
    /// 옮겼다.
    /// <code>
    ///   4  오아시스  0x00497F10  파트 6  128x128 x14  팔레트 0x22   자리는 7 점 위가 아니라 32 점 위
    ///   5  사태      0x00498160  파트 7  128x128 x15  팔레트 0x23
    ///   6  늪        0x00498340  파트 8   96x96  x18  팔레트 0x24
    ///   7  유사      0x00498590  파트 9   96x96  x17  팔레트 0x25
    ///   14 유빙      0x00499BC0  파트 15 192x96  x4   팔레트 0x2A
    /// </code>
    /// 걸음별 장 표는 <paramref name="pick"/> 으로 준다 — 오아시스·늪·유사는 원본 표를 그대로
    /// 옮겼고, <b>유빙만 아직이라</b> 한 걸음에 한 장씩 곧이 넘긴다. 유빙의 물보라(파트 16,
    /// 32x32 여덟 장)도 아직 안 얹었다.
    /// </remarks>
    private sealed class StripScene(int part, int frameW, int frameH, int palette, int up = 7,
                                    int soundAt = -1, int sound = -1, int soundOff = -1,
                                    int endAt = -1, Func<int, int>? pick = null)
        : Scene
    {
        private BitmapSource[] _art = [];
        private int _x, _y;

        public override bool Load(EventAnimation anims)
        {
            _art = Frames(anims, part, frameW, frameH, palette) ?? [];
            return _art.Length > 0;
        }

        public override void Start(int w, int h, Point? ship, Random rng)
        {
            var at = ship ?? new Point(w / 2.0, h / 2.0);
            int left = (int)at.X - 24, top = (int)at.Y - 24;     // 48x48 함대 그림 왼쪽 위
            _x = left + (0x30 - frameW) / 2;
            _y = top + (0x30 - frameH) / 2 - up;
        }

        public override bool Step(int count, List<Draw> draws)
        {
            if (count == soundAt && sound >= 0) Sfx?.Play(sound);
            if (count == soundOff) Sfx?.Stop();

            // 걸음별 장 표가 있으면 그대로 따르고, 없으면 한 걸음에 한 장씩 곧이 넘긴다.
            if (count >= (endAt >= 0 ? endAt : _art.Length)) return true;
            int f = pick is null ? count : pick(count);
            draws.Add(new Draw(_art[Math.Clamp(f, 0, _art.Length - 1)], _x, _y));
            return false;
        }
    }

    /// <summary>
    /// 8 덤불 — 함대 자리의 덤불 속에서 두 눈이 번뜩인다(<c>0x0061DFF8</c>). 짐승·독충이 같이 쓴다.
    /// </summary>
    /// <remarks>
    /// <code>
    ///   0x004987D0  파트 10, 96 x 960(96x64 열다섯 장), 팔레트 38 — 소리 없음
    ///   0x00498770  자리 = 함대 그림(48x48) 왼쪽 위 + ((48-96)/2, (48-64)/2 - 7)
    ///   0x00498600  한 걸음(걸음 c)
    ///     c &lt; 15   장 0            15~30  1·1·2·2 되풀이   31~40  3 + (c-31)/2
    ///     41~46  장 8   47~50  장 9   51~60  장 10   61~66  장 8   67~74  11 + (c-67)/2
    ///     75~    장 0
    ///     c &lt; 5 는 줄을 솎아 찍고(스며 나옴), 79~83 은 거꾸로 솎는다(0x0049A7D0)
    ///     c &gt;= 84 면 끝
    /// </code>
    /// 함대 자리 <c>0x0047D020</c>·<c>0x0047D050</c> 은 함대 좌표에서 화면 원점을 빼고 24 를 더
    /// 뺀 값이라 곧 48x48 그림의 왼쪽 위다. 우리 배 그림은 한가운데로 잡으므로 −24 를 도로 한다.
    /// </remarks>
    private sealed class BushScene : Scene
    {
        private const int FrameW = 0x60, FrameH = 0x40;

        private BitmapSource[] _art = [];
        private readonly BitmapSource?[] _fade = new BitmapSource?[5];
        private EventAnimation.Strip? _strip;
        private int _x, _y;

        public override bool Load(EventAnimation anims)
        {
            _strip = anims.TryGetStrip(10, FrameW, FrameH, 0x26);
            _art = Frames(anims, 10, FrameW, FrameH, 0x26) ?? [];
            return _strip != null && _art.Length >= 15;
        }

        public override void Start(int w, int h, Point? ship, Random rng)
        {
            // 배 자리를 모르면 지도 한가운데에 둔다.
            var at = ship ?? new Point(w / 2.0, h / 2.0);
            int left = (int)at.X - 24, top = (int)at.Y - 24;      // 48x48 함대 그림 왼쪽 위
            _x = left + (0x30 - FrameW) / 2;
            _y = top + (0x30 - FrameH) / 2 - 7;
        }

        public override bool Step(int c, List<Draw> draws)
        {
            if (c >= 0x54) return true;

            int f = c switch
            {
                < 15 => 0,
                < 0x1F => 2 * ((15 - c) / 4) + (c - 15) / 2 + 1,
                < 0x29 => (c - 0x1F) / 2 + 3,
                < 0x2F => 8,
                < 0x33 => 9,
                < 0x3D => 10,
                < 0x43 => 8,
                < 0x4B => (c - 0x43) / 2 + 11,
                _ => 0,
            };

            if (c < 5) draws.Add(new Draw(Faded(c), _x, _y));
            else if (c < 0x4F) draws.Add(new Draw(_art[f], _x, _y));
            else draws.Add(new Draw(Faded(0x53 - c), _x, _y));
            return false;
        }

        /// <summary>첫 장을 그 단계만큼 줄을 솎아 굳힌다.</summary>
        private BitmapSource Faded(int progress)
        {
            if (_fade[progress] is { } made) return made;

            var strip = _strip!;
            var one = new uint[FrameW * FrameH];
            for (int row = 0; row < FrameH; row++)
                if (RowShown(FrameH, progress, row))
                    Array.Copy(strip.Bgra, row * FrameW, one, row * FrameW, FrameW);

            var bmp = BitmapSource.Create(FrameW, FrameH, 96, 96, PixelFormats.Bgra32, null, one, FrameW * 4);
            bmp.Freeze();
            return _fade[progress] = bmp;
        }

        /// <summary>
        /// 그 단계에서 그 줄을 찍는가(<c>0x0049A900(높이, 단계, 줄, 0)</c>).
        /// </summary>
        /// <remarks>
        /// <code>
        ///   단계 0  위 1/4 에서 네 줄에 하나
        ///   단계 1  위 3/4 에서 네 줄에 하나
        ///   단계 2  위 절반은 세 줄에 하나, 아래 절반은 네 줄에 하나
        ///   단계 3  위 절반은 두 줄에 하나, 아래 절반은 세 줄에 하나
        ///   단계 4  홀수 줄
        /// </code>
        /// </remarks>
        private static bool RowShown(int h, int progress, int row) => progress switch
        {
            0 => row < h / 4 && row % 4 == 0,
            1 => row < h * 3 / 4 && row % 4 == 0,
            2 => row < h / 2 ? row % 3 == 0 : row % 4 == 0,
            3 => row < h / 2 ? row % 2 == 0 : row % 3 == 0,
            _ => row % 2 != 0,
        };
    }

    /// <summary>
    /// 2 폭풍 — 먹구름이 오른쪽에서 들어와 왼쪽으로 빠지고 빗줄기가 비스듬히 쏟아진다
    /// (<c>0x0061D7D8</c>).
    /// </summary>
    /// <remarks>
    /// <code>
    ///   0x004976F0  빗방울 파트 2(32x32 한 장) · 구름 파트 3(320 x 2640 = 320x240 열한 장), 팔레트 32
    ///   0x004975F0  첫자리  구름 x = W, y = (H-240)/3
    ///               빗방울 125개 x = rand(W/20) + (i%10)·W/10, y = -96·(i/10) - rand(16) - 64·(i%2)
    ///   0x00497730  모든 소리를 끄고(0x00422A40(-1, 3)) 소리 0x40(WAVE 파트 36)
    ///   0x00497390  한 걸음
    ///     구름 x + 320 &lt; 0 이면 끝(그리기 전에)
    ///     구름을 장 (걸음 % 11) 로 찍고 x += ((320 - W)/2)/10
    ///     빗방울마다 — 구름이 왼쪽 끝을 넘은 만큼 솎는다(다 · 짝수 · 넷에 하나 · 여섯에 하나)
    ///     찍든 안 찍든 옮긴다(0x004974F0)
    ///   0x0049AC8D  끝에 소리 0x40 을 끄고 0x004229F0 이 설정대로 곡을 되튼다
    /// </code>
    /// W 가 640 이면 구름이 한 걸음 16점씩 가서 60걸음(6초)이다.
    /// </remarks>
    private sealed class StormScene : Scene
    {
        private const int Drops = 0x7D, CloudW = 0x140, CloudH = 0xF0, DropSide = 0x20;

        private BitmapSource[] _cloud = [], _drop = [];
        private int _w, _h, _cloudX, _cloudY;
        private readonly int[] _x = new int[Drops], _y = new int[Drops];
        private Random _rng = new();

        public override int SoundPart => 0x40 - WaveBank.FirstSoundId;
        public override bool StopsMusic => true;

        public override bool Load(EventAnimation anims)
        {
            _drop = Frames(anims, 2, DropSide, DropSide, 0x20) ?? [];
            _cloud = Frames(anims, 3, CloudW, CloudH, 0x20) ?? [];
            return _drop.Length >= 1 && _cloud.Length >= 11;
        }

        public override void Start(int w, int h, Point? ship, Random rng)
        {
            _w = w; _h = h; _rng = rng;
            _cloudX = w;
            _cloudY = (h - CloudH) / 3;
            int step = w / 10;
            for (int i = 0; i < Drops; i++)
            {
                _x[i] = rng.Next(Math.Max(1, step / 2)) + i % 10 * step;
                _y[i] = i / -10 * 96 - rng.Next(16) - i % 2 * 64;
            }
        }

        public override bool Step(int count, List<Draw> draws)
        {
            if (_cloudX + CloudW < 0) return true;

            draws.Add(new Draw(_cloud[count % 11], _cloudX, _cloudY));
            _cloudX += (CloudW - _w) / 2 / 10;

            for (int i = 0; i < Drops; i++)
            {
                if (Shown(i)) draws.Add(new Draw(_drop[0], _x[i], _y[i]));
                Move(i);
            }
            return false;
        }

        /// <summary>구름이 빠져나가는 만큼 빗방울을 솎는다(<c>0x00497430</c>).</summary>
        private bool Shown(int i) =>
            _cloudX >= 0 ? true
            : _cloudX + 0xA0 > 0 ? i % 2 == 0
            : _cloudX + 0xF0 > 0 ? i % 4 == 0
            : i % 6 == 0;

        /// <summary>빗방울 하나를 왼쪽 아래로 옮긴다(<c>0x004974F0</c>).</summary>
        private void Move(int i)
        {
            int r = i % 3 * 32;
            _x[i] += r / -3 - 0x40;
            if (_x[i] < 0)
            {
                _x[i] = _w - _rng.Next(16);                // 0x00497560
                _y[i] += _rng.Next(16);
            }
            _y[i] += r / 4 + 0x40;
            if (_y[i] >= _h)
            {
                _x[i] += _rng.Next(16);                    // 0x004975A0
                _y[i] = (-2 - (i + 1) % 4) * 8 - _rng.Next(16);
            }
        }
    }

    /// <summary>
    /// 3 눈보라 — 눈송이 떼가 흩날리고 눈구름 둘이 왼쪽으로 스친다(<c>0x0061D2A0</c>).
    /// </summary>
    /// <remarks>
    /// <code>
    ///   0x00497C60  눈송이 파트 4(16x16 세 장) · 눈구름 파트 5(240x320 한 장), 팔레트 33
    ///   0x00497AD0  첫자리  눈송이 125개(스물다섯에 하나는 옆바람을 탄다), 눈구름 (W+120, 0)·(W, H/2)
    ///   0x00497CA0  소리 0x41(WAVE 파트 37) — 곡은 안 끊는다
    ///   0x004977A0  한 걸음
    ///     걸음 ≤ 5 · ≥ 55 는 여섯에 하나, ≤ 15 · ≥ 45 는 짝수만 찍고 옮긴다(나머지는 멈춰 있다)
    ///     눈송이 장은 i%4 로 0·2·1·2, i%25 == 14 면 0
    ///     눈구름을 찍고 x -= W/10, 왼쪽 끝을 넘으면 x = W 에서 y 를 0 ↔ H/2 로 바꾼다
    ///   0x0049AD64  걸음 &gt; 60 이면 끝 — 소리 0x41 을 끈다
    /// </code>
    /// 걸음 수로 끝나므로 지도 넓이와 상관없이 62걸음(6.2초)이다.
    /// </remarks>
    private sealed class BlizzardScene : Scene
    {
        private const int Flakes = 0x7D, CloudW = 0xF0, FlakeSide = 0x10;

        private BitmapSource[] _flake = [], _cloud = [];
        private int _w, _h;
        private readonly int[] _x = new int[Flakes], _y = new int[Flakes];
        private readonly int[] _cloudX = new int[2], _cloudY = new int[2];
        private Random _rng = new();

        public override int SoundPart => 0x41 - WaveBank.FirstSoundId;

        public override bool Load(EventAnimation anims)
        {
            _flake = Frames(anims, 4, FlakeSide, FlakeSide, 0x21) ?? [];
            _cloud = Frames(anims, 5, CloudW, 0x140, 0x21) ?? [];
            return _flake.Length >= 3 && _cloud.Length >= 1;
        }

        public override void Start(int w, int h, Point? ship, Random rng)
        {
            _w = w; _h = h; _rng = rng;
            int step = w / 20;
            for (int i = 0; i < Flakes; i++)
            {
                if (i % 25 < 24)
                {
                    _x[i] = rng.Next(Math.Max(1, step / 2)) + i % 20 * step;
                    _y[i] = i / -10 * 80 - rng.Next(16) - i % 2 * 32;
                }
                else
                {
                    int k = i / 24;
                    _x[i] = rng.Next(16) + step * k * 2 + w;
                    _y[i] = rng.Next(16) + h / 5 * k + 8;
                }
            }
            _cloudX[0] = w + 0x78; _cloudY[0] = 0;
            _cloudX[1] = w;        _cloudY[1] = h / 2;
        }

        public override bool Step(int c, List<Draw> draws)
        {
            for (int i = 0; i < Flakes; i++)
            {
                if (c <= 5 && i % 3 != 0) continue;
                if (c <= 15 && i % 2 != 0) continue;
                if (c >= 0x2D && i % 2 != 0) continue;
                if (c >= 0x37 && i % 3 != 0) continue;

                int f = (i % 4) switch { 0 => 0, 2 => 1, _ => 2 };
                if (i % 25 == 14) f = 0;
                draws.Add(new Draw(_flake[f], _x[i], _y[i]));
                Move(c, i);
            }

            for (int j = 0; j < 2; j++)
            {
                draws.Add(new Draw(_cloud[0], _cloudX[j], _cloudY[j]));
                if (_cloudX[j] + CloudW < 0)
                {
                    _cloudX[j] = _w;
                    _cloudY[j] = _cloudY[j] == 0 ? _h / 2 : 0;
                }
                _cloudX[j] += _w / -10;
            }
            return c > 0x3C;
        }

        /// <summary>눈송이 하나를 옮긴다(<c>0x00497940</c>).</summary>
        private void Move(int c, int i)
        {
            if (i % 25 < 24)
            {
                int m = i % 3;
                _x[i] += (m - 2) * 48 / 2 - 0x40;
                _y[i] += (4 - m) * 8;
            }
            else
            {
                _x[i] -= 0x20;
                int phase = (c + i) % 8;
                _y[i] += phase < 3 ? -8 : phase < 6 ? 16 : 8;
            }

            if (_x[i] < 0)
            {
                if (c > 0x28 && _w / 2 < _y[i])
                {
                    _y[i] = _rng.Next(16);
                    _x[i] = i % 20 * _w / 20;
                }
                else
                {
                    _x[i] = _w - _rng.Next(16);            // 0x00497A60
                    _y[i] += _rng.Next(16);
                }
            }
            if (_y[i] >= _h)
            {
                _x[i] += _rng.Next(16);                    // 0x00497AA0
                _y[i] = -_rng.Next(16);
            }
        }
    }
}
