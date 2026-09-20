using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using CdsHelper.Game.Local.Helpers;

namespace CdsHelper.Game.UI.Views;

/// <summary>
/// 일기토 판 — 두 사람이 마주 서서 칼을 겨루는 그림판.
/// </summary>
/// <remarks>
/// 게임 화면은 <c>0x004AA700</c> 이 짓는 <b>384x256</b> 칸이고 그 위층 384x136 이 여기다.
/// 배경 그림은 뒤에 깔리고(<see cref="DuelArt"/>) 이 판에는 사람 둘만 얹는다.
///
/// <b>몸짓은 여기서 셈하지 않는다.</b> 어느 눈금에 어느 장을 어디에 낼지는 죄다
/// <see cref="DuelMotions"/> 의 표에 적혀 있고, 이 판은 그것을 눈금에 맞춰 읽어 그리기만
/// 한다. 그래서 모션 메이커에서 고쳐 저장하면(<c>asset/duel/motion.json</c>) 다시 굽지
/// 않아도 놀이에 그대로 든다.
///
/// 한 판이 <b>서른세 눈금</b>이고(<c>0x00572A84</c>) 눈금 하나가 0.067초(1/15초)다.
/// <code>
///   눈금 0~7    여느 자세로 다가서거나 물러난다 — 한 눈금에 5점, 여덟 눈금에 40점
///   눈금 8      고른 명령 이름이 뜬다             [0x00572A6C]
///   눈금 8·9    찌르는 첫 장
///   눈금 10     둘째 장
///   눈금 11     부위 체력이 깎이고 소리가 난다  [0x00572A74]
///   눈금 11~14  셋째 장 — 서른 점 더 내지른 채
///   눈금 15~32  여느 첫 장 하나로 선다
/// </code>
/// </remarks>
public sealed class DuelStage : Canvas
{
    /// <summary>판 크기. 게임 것과 같다(<c>0x004AA7BB</c> 의 <c>0x180</c> x <c>0x100</c>).</summary>
    public const int StageWidth = DuelArt.ArenaWidth, StageHeight = DuelArt.ArenaHeight;

    /// <summary>
    /// 두 사람이 <b>다 모여 서는</b> 자리.
    /// </summary>
    /// <remarks>
    /// <b>상대가 왼쪽, 내가 오른쪽</b>이다. 그림이 그렇게 그려져 있다 — 제독
    /// 스프라이트셋(0)은 <b>왼쪽을 보고</b> 상대 것은 <b>오른쪽을 본다</b>.
    ///
    /// 자리는 게임이 판을 차릴 때 박아 넣는 값 그대로다.
    /// <code>
    ///   004a9465  mov [ecx+0x14c], 0x98    ; 내   x = 152
    ///   004a946f  mov [ecx+0x150], 0x50    ; 상대 x =  80
    /// </code>
    /// 갈무리를 눈대중해 60 · 173 으로 두었던 것은 둘 다 스무 점씩 어긋나 있었다.
    /// 이 값이라야 다가올 때 상대가 <b>판 왼끝(0)에서</b> 걸어 나오고, 벽 한계
    /// (40 · 200, <c>0x004A6EF0</c>)도 서는 자리에서 마흔 점씩으로 맞아떨어진다.
    /// </remarks>
    private const double FoeStand = 80, MyStand = 152;

    /// <summary>자리 한계 — 상대는 40 아래로, 나는 200 위로 안 간다(<c>0x004A6EF0</c>).</summary>
    private const double WallNear = 40, WallFar = 200;

    /// <summary>한 판의 눈금 수 — <b>서른셋</b>이다(<c>0x00572A84</c>).</summary>
    public const int Ticks = 33;

    /// <summary>
    /// 꼬리를 걷은 판의 눈금 수 — 찌르기가 끝나는 자리까지다(<c>0x00572A78</c>).
    /// </summary>
    /// <remarks>
    /// 눈금 15 부터 32 까지는 여느 자세로 서 있기만 한다. 아무도 안 맞은 판은 그 열여덟
    /// 눈금(1.2초)을 기다릴 것 없이 여기서 끊는다.
    /// </remarks>
    public const int ShortTicks = 15;

    /// <summary>
    /// 맞은 판의 눈금 수 — <b>빨강이 다 찬 뒤 한 박자</b>까지다.
    /// </summary>
    /// <remarks>
    /// 부위 체력이 깎이는 눈금이 11 이고 빨강이 다섯 눈금에 걸쳐 차므로 16 이면 다 찬다.
    /// 거기에 한 박자만 두고 끊는다 — 서른셋까지 다 돌리면 볼 것 없는 열일곱 눈금
    /// (1.1초)을 더 서 있게 된다.
    /// </remarks>
    public const int HitTicks = 18;

    /// <summary>고른 명령 이름이 뜨는 눈금과 부위 체력이 깎이는 눈금.</summary>
    private const int SayTick = 8, HurtTick = 11;

    /// <summary>
    /// 눈금 하나의 길이 — <b>67밀리초</b>(1/15초)다.
    /// </summary>
    /// <remarks>
    /// 갈무리(「주인공 중단공격」)의 <c>fcTL</c> 을 읽으면 열일곱 장이 죄다 0.067초다.
    /// 55밀리초로 두었던 것은 어림값이라 그만큼 빨랐다.
    /// </remarks>
    private static readonly TimeSpan TickTime = TimeSpan.FromSeconds(DuelMotions.Tick);

    private readonly FighterSprites _art;
    private readonly int _foeSet;
    private readonly Image _me = new();
    private readonly Image _foe = new();
    /// <summary>
    /// 판을 도는 시계 — <b>그리기 앞차례</b>로 올려 둔다.
    /// </summary>
    /// <remarks>
    /// <c>new DispatcherTimer()</c> 는 <see cref="DispatcherPriority.Background"/> 로 도는데,
    /// 그 자리는 그리기·입력보다 뒤라 눈금이 <b>67밀리초보다 늦게</b> 온다. 한 판이 서른세
    /// 눈금이라 눈금마다 몇 밀리초씩만 밀려도 한 판이 눈에 띄게 처진다.
    /// </remarks>
    private readonly DispatcherTimer _timer = new(DispatcherPriority.Render);

    /// <summary>이 판에 두 사람이 짓는 몸짓.</summary>
    private DuelMotions.Motion? _myMotion, _foeMotion;

    /// <summary>지금 두 사람이 선 자리 — 판이 끝날 때마다 다가서거나 물러난 만큼 옮겨진다.</summary>
    private double _foeLeft = FoeStand, _myLeft = MyStand;

    /// <summary>다가오는 눈금. −1 이면 다 모여 판이 도는 중이다.</summary>
    private int _walkTick;

    private int _tick;

    /// <summary>이번 판을 몇 눈금까지 돌릴지. 막힌 판은 꼬리를 걷는다.</summary>
    private int _ticks = Ticks;

    private Action? _onSay, _onHurt, _onDone;

    /// <summary>푼 장을 담아 둔다 — 눈금마다 다시 짜면 그만큼 늦어진다.</summary>
    private readonly Dictionary<(int Set, int Frame), BitmapSource> _kept = [];

    public DuelStage(FighterSprites art, int foeSet)
    {
        _art = art;
        _foeSet = foeSet;
        Width = StageWidth;
        Height = StageHeight;
        // 바탕은 비운다 — 뒤에 깔린 배경 그림(asset/duel)이 그대로 비쳐야 한다.
        Background = Brushes.Transparent;
        ClipToBounds = true;

        foreach (var image in new[] { _me, _foe })
        {
            image.Width = FighterSprites.Width;
            image.Height = FighterSprites.Height;
            RenderOptions.SetBitmapScalingMode(image, GameUi.SpriteScaling);
            RenderOptions.SetEdgeMode(image, EdgeMode.Aliased);
            SetTop(image, StageHeight - FighterSprites.Height);
            Children.Add(image);
        }

        _timer.Interval = TickTime;
        _timer.Tick += (_, _) => Advance();

        // 들어올 때는 아직 벽 쪽이다 — WalkIn 이 가운데로 데려온다.
        _myMotion = _foeMotion = DuelMotions.Find(DuelMotions.Idle);
        _walkTick = 0;
        _tick = 0;
        Draw();
    }

    /// <summary>
    /// 둘 다 기본 자세로 세운다 — <b>선 자리는 그대로다</b>.
    /// </summary>
    /// <remarks>
    /// 판과 판 사이에 부른다. <see cref="_walkTick"/> 을 0 으로 두면 다가오기 첫 눈금이라
    /// 두 사람이 <b>벽으로 되돌아가</b> 버린다 — 판이 끝난 자리에서 이어 싸워야 한다.
    /// </remarks>
    public void Rest()
    {
        _myMotion = _foeMotion = DuelMotions.Find(DuelMotions.Idle);
        _walkTick = -1;
        _tick = 0;
        Draw();
    }

    /// <summary>
    /// 한 판을 돌린다. <paramref name="onSay"/> 는 여덟째 눈금, <paramref name="onHurt"/> 는
    /// 열한째 눈금, <paramref name="onDone"/> 은 끝난 뒤에 부른다.
    /// </summary>
    /// <param name="way">
    /// 이 판에 두 사람이 <b>함께</b> 옮겨 갈 쪽 — <c>−1</c> 내가 몰아붙임(앞으로) ·
    /// <c>+1</c> 내가 물러남 · <c>0</c> 맞부딪힘이라 제자리.
    /// </param>
    /// <param name="ticks">
    /// 몇 눈금까지 돌릴지. 여느 판은 <see cref="Ticks"/>, 아무도 안 맞은 판은
    /// <see cref="ShortTicks"/> 로 꼬리를 걷는다.
    /// </param>
    /// <remarks>
    /// 다가서고 물러나는 것은 <b>몸짓 안에 들어 있다</b> — 공격 몸짓은 다가서는 자리를,
    /// 막는 몸짓은 물러나는 자리를 제 표에 적어 두고 있다. 그래서 여기서는 맞부딪힘인지만
    /// 가리면 된다.
    /// </remarks>
    public void Play(FighterSprites.Move mine, FighterSprites.Move theirs, int way, int ticks,
                     Action? onSay, Action? onHurt, Action onDone)
    {
        _ticks = Math.Clamp(ticks, 1, Ticks);
        // 벽에 닿았으면 이 판에는 안 옮긴다 — 게임도 <b>판 갈래를 정할 때</b> 지금 자리를
        // 보고 가린다(0x004A6EF0: 상대 x >= 40 이라야 몰아붙이고, 내 x <= 200 이라야 물러난다).
        // 옮긴 <b>뒤</b> 자리로 가리면, 몸짓은 이미 나아갔는데 자리를 안 담아 되돌아간다.
        if (way < 0 && _foeLeft < WallNear) way = 0;
        if (way > 0 && _myLeft > WallFar) way = 0;

        _myMotion = MotionFor(mine, way);
        _foeMotion = MotionFor(theirs, way);
        _onSay = onSay;
        _onHurt = onHurt;
        _onDone = onDone;
        _tick = 0;
        _walkTick = -1;
        Draw();
        _timer.Start();
    }

    /// <summary>
    /// 다가오기 — 둘이 <b>벽에서 가운데로</b> 걸어 나온다. 다 모이면 <paramref name="done"/>.
    /// </summary>
    /// <remarks>
    /// 단계 0 이 열여섯 눈금이고(<c>0x00572A68</c>) 그동안 한쪽이 여든 점씩 다가온다 —
    /// 한 눈금에 다섯 점이다(<c>0x004A7593</c>). 다 모이고 <b>나서야</b> 명령 창이 뜬다.
    /// </remarks>
    public void WalkIn(Action done)
    {
        var walk = DuelMotions.Find(DuelMotions.Walk);
        int ticks = walk == null ? 0 : (int)Math.Round(walk.Length / DuelMotions.Tick);

        _walkTick = 0;
        Draw();

        var clock = new DispatcherTimer(DispatcherPriority.Render) { Interval = TickTime };
        clock.Tick += (_, _) =>
        {
            _walkTick++;
            Draw();
            if (_walkTick < ticks) return;

            clock.Stop();

            // 걸어온 끝자리를 선 자리에 담는다 — 다가오기가 0 이 아닌 자리에서 끝나도
            // 첫 판이 그 자리에서 이어진다. 안 담으면 판이 열리며 그만큼 도로 튄다.
            if (walk is { Steps.Length: > 0 })
            {
                double end = walk.Steps[^1].Push;
                _myLeft -= end;
                _foeLeft += end;
            }

            _walkTick = -1;                  // 다 왔으면 판 눈금으로 넘어간다
            _tick = 0;
            Draw();
            done();
        };
        clock.Start();
    }

    /// <summary>
    /// 판을 끝맺는다 — 진 쪽은 쓰러지고 <b>이긴 쪽은 승리 몸짓</b>이다.
    /// </summary>
    /// <remarks>
    /// 둘 다 여섯 장짜리라 열여섯 눈금에 한 번 돈다(<see cref="DuelMotions"/>). 판 눈금
    /// (<c>_timer</c>)을 그대로 쓰면 말·피해 알림이 다시 울리므로 여기서만 도는 눈금을 따로 둔다.
    /// 예전에는 마지막 장으로 곧장 넘겨 <b>이긴 쪽이 찌른 자세 그대로</b> 서 있었다.
    /// </remarks>
    public void Fall(bool mine)
    {
        _timer.Stop();
        var fall = DuelMotions.Find(DuelMotions.Fall);
        var win = DuelMotions.Find(DuelMotions.Victory);
        _myMotion = mine ? fall : win;
        _foeMotion = mine ? win : fall;

        _walkTick = -1;
        _tick = 0;
        Draw();

        var clock = new DispatcherTimer(DispatcherPriority.Render) { Interval = TickTime };
        clock.Tick += (_, _) =>
        {
            if (++_tick >= EndTicks) { _tick = EndTicks; clock.Stop(); }
            Draw();
        };
        clock.Start();
    }

    /// <summary>끝맺는 몸짓 한 바퀴에 드는 눈금 — 여섯 장을 두 눈금씩이다.</summary>
    private const int EndTicks = 16;

    /// <summary>그 몸짓을 적어 둔 표에서 찾는다.</summary>
    /// <param name="way">0 이면 제자리 갈래를 쓴다 — 맞부딪힘이거나 벽에 닿았을 때다.</param>
    private static DuelMotions.Motion? MotionFor(FighterSprites.Move move, int way) => move switch
    {
        FighterSprites.Move.HighThrust or
        FighterSprites.Move.MidThrust or
        FighterSprites.Move.LowThrust =>
            DuelMotions.Find(DuelMotions.ThrustKey((int)move, way == 0 ? 0 : 1)),

        FighterSprites.Move.Jump or
        FighterSprites.Move.Dodge or
        FighterSprites.Move.Crouch =>
            DuelMotions.Find(DuelMotions.GuardKey((int)move - 3, way == 0 ? 0 : -1)),

        FighterSprites.Move.Victory => DuelMotions.Find(DuelMotions.Victory),
        FighterSprites.Move.Fall => DuelMotions.Find(DuelMotions.Fall),
        _ => DuelMotions.Find(DuelMotions.Idle),
    };

    private void Advance()
    {
        _tick++;
        if (_tick == SayTick) _onSay?.Invoke();
        if (_tick == HurtTick) _onHurt?.Invoke();
        Draw();
        if (_tick < _ticks) return;

        _timer.Stop();
        Settle();

        var done = _onDone;
        _onDone = _onSay = _onHurt = null;
        done?.Invoke();
    }

    /// <summary>
    /// 판이 끝나면 <b>다가서거나 물러난 만큼을 선 자리에 담는다</b>(<c>0x004A6D9A</c>).
    /// </summary>
    /// <remarks>
    /// 몸짓 끝자리의 점이 곧 이 판에 옮겨 간 거리다 — 공격이면 마흔, 막기면 −마흔,
    /// 맞부딪힘이면 0 이다. 두 사람이 <b>같은 쪽으로</b> 가므로 사이는 그대로다.
    ///
    /// <b>여기서는 벽을 안 본다.</b> 갈 수 있는지는 판을 열 때 이미 가렸다
    /// (<see cref="Play"/>) — 몸짓이 나아간 만큼은 반드시 담아야 판이 끝난 자리에서
    /// 이어 싸운다. 여기서 무르면 몸짓만 나아갔다가 제자리로 되돌아간다.
    /// </remarks>
    private void Settle()
    {
        if (_myMotion is not { Steps.Length: > 0 } mine) return;
        if (_foeMotion is not { Steps.Length: > 0 } foe) return;

        _myLeft -= mine.Steps[^1].Push;         // 나는 왼쪽이 앞이다
        _foeLeft += foe.Steps[^1].Push;         // 상대는 오른쪽이 앞이다
    }

    private void Draw()
    {
        if (_walkTick >= 0)
        {
            var walk = DuelMotions.Find(DuelMotions.Walk);
            Put(_me, 0, walk, _walkTick, MyStand, forward: false);
            Put(_foe, _foeSet, walk, _walkTick, FoeStand, forward: true);
            return;
        }

        Put(_me, 0, _myMotion, _tick, _myLeft, forward: false);
        Put(_foe, _foeSet, _foeMotion, _tick, _foeLeft, forward: true);
    }

    /// <summary>그 스프라이트셋의 그 장. 한 번 푼 것은 담아 둔다.</summary>
    private BitmapSource? Bitmap(int set, int frame)
    {
        if (_kept.TryGetValue((set, frame), out var kept)) return kept;

        var px = _art.TryGetBgra(set, frame);
        if (px == null) return null;

        var bmp = BitmapSource.Create(FighterSprites.Width, FighterSprites.Height, 96, 96,
                                      PixelFormats.Bgra32, null, px, FighterSprites.Width * 4);
        bmp.Freeze();
        _kept[(set, frame)] = bmp;
        return bmp;
    }

    /// <summary>그 몸짓의 그 눈금을 판에 건다. 앞은 상대 쪽이다.</summary>
    private void Put(Image image, int set, DuelMotions.Motion? motion, int tick,
                     double stand, bool forward)
    {
        if (motion == null) { image.Source = null; return; }

        var (frame, push) = motion.At(tick);
        var bmp = Bitmap(set, frame);
        if (bmp == null) { image.Source = null; return; }

        image.Source = bmp;

        SetLeft(image, forward ? stand + push : stand - push);
    }
}
