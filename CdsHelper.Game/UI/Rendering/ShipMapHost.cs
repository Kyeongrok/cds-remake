using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows;
using CdsHelper.Game.Local.Helpers;
using CdsHelper.Support.Local.Helpers;
using CdsHelper.Game.Engine.Discovery;
using CdsHelper.Support.Local.Models;
using Vortice.DXGI;
using Vortice.Direct3D11;

namespace CdsHelper.Game.UI.Views;

/// <summary>
/// 자식 창 하나를 만들어 그 위에 DXGI 스왑체인을 걸고 지도와 함대를 그린다.
/// </summary>
/// <remarks>
/// 세계지도 탭은 <c>D3DImage</c> 를 쓸 수 없는 것이 아니라 안 쓴다 — 그쪽은 마커·라벨이
/// WPF 요소로 지도 위에 얹혀 있어 비주얼 트리 안에 남아야 하기 때문이다. 이 창은 마커가
/// 없으므로 자식 창에 스왑체인을 곧바로 걸었다. 공유 표면을 거치지 않아 그만큼 짧다.
///
/// 대신 airspace 규칙대로 이 자식 창은 WPF 콘텐츠보다 늘 위에 그려진다. 이 창 안에서
/// D3D 화면 위에 WPF 로 무언가를 얹으려 해도 가려진다.
/// </remarks>
public sealed class ShipMapHost : HwndHost
{
    // 창 클래스를 새로 등록하지 않고 미리 있는 STATIC 을 쓴다. 스왑체인을 걸 HWND 하나가
    // 필요할 뿐이고, 정적 컨트롤은 기본적으로 WM_NCHITTEST 에 HTTRANSPARENT 를 돌려주므로
    // 마우스가 이 자식 창에 먹히지 않고 WPF 쪽으로 그대로 넘어간다 — 끌기·휠이 살아 있다.
    private const string WndClass = "STATIC";
    private const int WsChild = 0x40000000;
    private const int WsVisible = 0x10000000;

    // CharSet 을 적어야 한다. 빠뜨리면 W 함수에 ANSI 문자열이 넘어가 클래스 이름을 못 찾는다
    // (Win32 1407). 이것 때문에 창이 안 만들어져 HwndHost 가 통째로 터졌었다.
    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateWindowExW(int exStyle, string cls, string? name, int style,
                                                 int x, int y, int w, int h,
                                                 IntPtr parent, IntPtr menu, IntPtr inst, IntPtr param);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyWindow(IntPtr hwnd);

    private readonly MapD3DRenderer _renderer = new();
    private readonly GameShipReader _ship = new();

    /// <summary>WORLD.CDS 원본. 배가 육지에 걸리는지 보려고 들고 있는다.</summary>
    private byte[]? _world;

    private IDXGISwapChain1? _swapChain;
    private ID3D11RenderTargetView? _backBufferView;
    private IntPtr _hwnd;
    private int _pixelW, _pixelH;
    private bool _ready;

    /// <summary>
    /// 화면 한 점이 나아가는 칸 수. 작을수록 확대다. 뒤집으면 "칸당 화면 픽셀"이 된다 —
    /// 1/16 이면 칸당 16점으로 타일이 원본 크기, 1/32 면 그 두 배로 커진다.
    /// 게임 화면과 나란히 놓고 다시 맞춘 값이 <b>1/24</b> 다. 예전 1/32 은 원본보다 1.35배쯤 컸다 —
    /// 같은 갈무리에서 도시 표시(3x3 칸)가 원본 45점 · 우리 62점이었다(0.73배).
    /// </summary>
    private double _cellsPerPixel = CellsPerPixelFor(Local.Settings.GameSettings.MapScale);

    /// <summary>배율 1 일 때 한 점에 드는 칸 — 예전 크기다. 설정 배율이 이것을 나눈다.</summary>
    private const double BaseCellsPerPixel = 1.0 / 32;

    /// <summary>설정 배율(0.5~1.5)을 한 점당 칸으로 바꾼다. 0.75 가 1/24 다.</summary>
    private static double CellsPerPixelFor(double scale) => BaseCellsPerPixel / scale;

    /// <summary>설정 창에서 배율을 바꿨을 때 곧바로 든다. 배를 가운데 두고 본다.</summary>
    public void ApplyMapScale(double scale) => LookAt(CellsPerPixelFor(scale));

    /// <summary>화면 한가운데가 가리키는 칸 좌표.</summary>
    private double _centerX = 1185, _centerY = 357;

    /// <summary>리스본. 게임 도시 표의 0번이다.</summary>
    private const int LisbonCityId = 0;

    /// <summary>
    /// 시작 칸 — 리스본 앞바다. 열(칸 X) 1184 이고 행(칸 Y)은 357 이다.
    /// </summary>
    /// <remarks>
    /// 도시 칸(1185.5, 357.5)에서 가장 가까운 물칸을 고르면 1186 이 나오는데, 그 자리는
    /// 도시 바로 옆 강어귀라 사방이 뭍에 가깝다. 한 칸 서쪽 1184 는 강 건너 앞바다다 —
    /// 1184 는 357 행에서만 물이므로(356 행은 뭍) 행까지 같이 박아 둔다.
    /// 칸 가운데를 가리키려고 0.5 를 더한다.
    /// </remarks>
    private const double StartCellX = 1184.5, StartCellY = 357.5;

    /// <summary>
    /// 배가 화면 가장자리에서 이 점 수 안에 들어와야 화면을 다음 자리로 넘긴다(화면 실픽셀).
    /// </summary>
    /// <remarks>
    /// 예전에는 프레임마다 배를 화면 한가운데에 다시 놓았다. 배가 한 걸음 옮길 때마다
    /// 지도 전체를 새 원점으로 다시 그려야 해서, 배가 조금만 움직여도 화면이 통째로 갈렸다.
    /// 지금은 배가 가운데 여백 안을 다니는 동안 지도가 멈춰 있고 배만 그 위를 지난다 —
    /// 원점이 그대로면 <see cref="OnFrame"/> 이 아예 다시 그리지 않는다.
    /// </remarks>
    private const double EdgeMarginPixels = 200;

    private bool _follow = true;
    private bool _dragging;
    private Point _dragStart;
    private double _dragCx, _dragCy;

    private double _shipX, _shipY;      // 지금 배가 있는 자리(칸)
    private double _targetX, _targetY;  // 배가 향하는 자리(칸)
    private bool _shipKnown;

    /// <summary>실제 함대가 있을 때만 주인공 배 그림을 그린다.</summary>
    public bool ShowShip { get; set; } = true;

    /// <summary>한 틱의 길이. 게임처럼 틱마다 한 걸음씩 나아간다.</summary>
    private const double TickSeconds = 0.1;

    /// <summary>
    /// 바람이 없거나 함대를 모를 때 한 틱에 나아가는 칸 수.
    /// </summary>
    /// <remarks>
    /// 여기 오는 일은 거의 없다 — <see cref="FleetSpeed"/> 가 붙어 있으면 게임 셈으로
    /// 잰다(<see cref="Engine.Sea.Sailing"/>). 바람표를 못 읽었을 때의 물러설 자리다.
    /// </remarks>
    private const double CellsPerTick = 1.0;

    /// <summary>
    /// 함대 속도를 물어보는 이 — (풍향, 풍속, 뱃머리, 뭍인지) 를 받아 게임 속도를 낸다.
    /// </summary>
    /// <remarks>
    /// 지도는 함대도 돛 효율표도 모른다. 그래서 붙이는 쪽(<see cref="ShipMapWindow"/>)이
    /// 손을 하나 걸어 준다 — <see cref="MonthOf"/> 와 같은 얼개다.
    /// </remarks>
    public Func<int, int, int, bool, int>? FleetSpeed { get; set; }

    /// <summary>
    /// 기함이 한 틱에 도는 눈금 수를 물어보는 이. 안 걸어 두면
    /// <see cref="Engine.Sea.Sailing.DefaultTurnRate"/> 로 돈다.
    /// </summary>
    /// <remarks>
    /// 게임은 기함 종류로 표(<c>0x00569FC0</c>)를 찾는데 지도는 함대를 모른다 —
    /// <see cref="FleetSpeed"/> 와 같이 붙이는 쪽이 걸어 준다.
    /// </remarks>
    public Func<int>? TurnRateOf { get; set; }

    /// <summary>지난 걸음에 잰 함대 속도. 상태줄에 적으려고 남긴다.</summary>
    public int LastSpeed { get; private set; }

    /// <summary>지난 걸음의 바람(방위·세기)과 상대각.</summary>
    public (int Dir, int Speed, int Relative) LastWind { get; private set; }

    /// <summary>지난 걸음의 해류(방위·세기). 느린 부류의 칸에서는 안 받으므로 세기가 0 이다.</summary>
    public (int Dir, int Speed) LastFlow { get; private set; }

    /// <summary>지난 걸음에 발밑이 빠른 부류(그림 번호 <c>0x80</c>)였는지.</summary>
    public bool LastFast { get; private set; }

    /// <summary>지난 걸음에 실제로 나아간 칸 수(해류로 밀린 것은 뺀 것).</summary>
    public double LastStep { get; private set; }

    /// <summary>커서가 이 칸 수 안에 있으면 뱃머리를 그대로 둔다 — 배 위에서 빙빙 돌지 않게.</summary>
    private const double TurnDeadZoneCells = 1.0;

    private double _tickAccum;

    /// <summary>바람·해류 표. 못 열면 물결도 화살표도 안 나온다(지도는 그대로 돈다).</summary>
    private WindTable? _wind;

    /// <summary>
    /// 지금 쥐고 있는 바람 — 표 값에 방위를 <c>rand(3) − 1</c> 만큼 흔든 것(<c>0x00424E50</c>). 게임은 이 값을 바람 물건
    /// <c>0x00586168</c> 에 박아 두고, 다시 읽을 때까지 그대로 쓴다.
    /// </summary>
    private (int Cell, int Month, WindTable.Flow Wind)? _heldWind;

    /// <summary>
    /// 그 칸의 바람. 칸이나 달이 바뀌었거나 <see cref="ShiftWind"/> 로 흔들라고 했으면 새로 읽는다 — 게임은 지도가 넘어갈
    /// 때(<c>0x0047D1B0</c> 벌), 이레마다(<c>0x0044B27D</c>), 배에 오를 때(<c>0x0048EB94</c>) 다시 읽는다.
    /// </summary>
    private WindTable.Flow HeldWind(int cell, int month)
    {
        if (_heldWind is { } held && held.Cell == cell && held.Month == month) return held.Wind;
        var raw = _wind!.WindAt(cell, month);
        var wind = raw with { Dir = (raw.Dir + Random.Shared.Next(3) - 1) & 0xF };
        _heldWind = (cell, month, wind);
        return wind;
    }

    /// <summary>바람을 다시 흔든다 — 이레째 날이 넘어갔을 때와 배에 오를 때 부른다(<c>0x0044B27D</c> · <c>0x0048EB94</c>).</summary>
    public void ShiftWind() => _heldWind = null;

    /// <summary>물결이 흐른 틱 수. 게임의 <c>0x00569554</c> 자리다.</summary>
    private int _rippleTick;
    private double _rippleAccum;

    /// <summary>화살표 격자를 구운 달. 달이 바뀌면 바람 표가 갈리므로 다시 굽는다.</summary>
    private int _flowMonth = -1;

    /// <summary>지금 달을 알려 주는 이. 안 주면 4월로 본다(놀이 시작 달).</summary>
    public Func<int>? MonthOf { get; set; }

    /// <summary>
    /// 바람·해류 화살표를 지도에 얹을지. 게임에는 없는 것이라 커맨드 창에서 끄고 켠다.
    /// 물결은 이것과 상관없이 늘 흐른다 — 그쪽이 원본 모습이다.
    /// </summary>
    public bool ShowFlowArrows
    {
        get => _renderer.ShowArrows;
        set
        {
            if (_renderer.ShowArrows == value) return;
            _renderer.ShowArrows = value;
            _dirty = true;
        }
    }

    // 마지막 프레임의 화면 원점. 클릭한 자리를 칸으로 되돌릴 때 쓴다.
    private (double X, double Y) _lastOrigin;
    private double _lastDpiX = 1, _lastDpiY = 1;

    /// <summary>
    /// 해안 칸은 바다와 육지가 섞여 있어서, 지날 수 있는 기준이 모드마다 다르다.
    /// </summary>
    /// <remarks>
    /// 하나로 두면 물가에서 말이 갇힌다 — 상륙한 자리 둘레가 죄다 모래톱(육지 비율이
    /// 반 미만)이라 "바다" 로 판정돼 갈 데가 없어진다. 그래서 둘로 나눴다.
    /// 배는 육지가 반을 넘으면 못 가고, 말은 육지가 조금이라도 있으면 갈 수 있다.
    /// </remarks>
    private const double SailMaxLandRatio = 0.5;
    private const double WalkMinLandRatio = 0.2;

    /// <summary>육지에 막혀 있는지. 상태 줄에 알리려고 둔다.</summary>
    private bool _blocked;

    /// <summary>상륙해 뭍에 있는지. 배 대신 말이 나오고 지날 수 있는 칸이 뒤집힌다.</summary>
    private bool _onLand;

    /// <summary>닻을 내렸는지. 내리면 그 자리에 서고, 올려야 다시 나아간다.</summary>
    private bool _anchored;

    /// <summary>지금 뭍에 있는지.</summary>
    public bool IsOnLand => _onLand;

    /// <summary>지금 정박 중인지.</summary>
    public bool IsAnchored => _anchored;

    /// <summary>
    /// 방향 번호를 통째로 돌리는 값. 뱃머리가 일정하게 어긋날 때만 손대면 된다.
    /// 게임 방향은 0 = 북, 4 = 서, 8 = 남, 12 = 동으로 <b>반시계</b>로 돈다.
    /// </summary>
    private const int HeadingZeroOffset = 0;

    /// <summary>
    /// 8방위 이름. 게임 이름표(<c>0x569790</c>)와 같은 차례로 <b>반시계</b>로 돈다.
    /// </summary>
    /// <summary>8방위 이름. 16방위 번호를 하나 걸러 읽는다.</summary>
    public static IReadOnlyList<string> Compass => CompassNames;

    private static readonly string[] CompassNames =
        ["북", "북서", "서", "남서", "남", "남동", "동", "북동"];

    /// <summary>
    /// 지금 뱃머리를 8방위 이름으로. 속으로는 16방위 그대로 두고 보여줄 때만 절반으로 깎는다 —
    /// 게임도 <c>0x48ABA2</c> 에서 방위를 2로 나눠 8방위 이름표를 찾는다. 그래서 화면에 "서" 로
    /// 보여도 속은 4일 수도 5일 수도 있다.
    /// </summary>
    public string HeadingName => CompassNames[(_heading & 0xF) >> 1];

    private int _heading;                  // 지금 뱃머리(반시계, 16방위). 그림도 이동도 이것이다
    private int _desired;                  // 커서가 바라는 쪽(8방위라 늘 짝수)
    private bool _making;                  // 나아가는 중인지. 입항·자리 옮김에서 세워 둔다
    private Point _mouse;                  // 마지막 커서 자리(WPF 단위, 이 요소 기준)
    private bool _mouseInside;

    /// <summary>이번 프레임에 뱃머리가 바라볼 자리가 있는지 — 커서가 지도 안에 있거나 자동항해 중이다.</summary>
    private bool _hasHeadingTarget;
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private TimeSpan _lastFrame;
    private bool _spriteReady;

    /// <summary>배 그림을 올릴 때 쓰는 임시 자리(BGRA). 프레임마다 새로 잡지 않으려고 둔다.</summary>
    private readonly uint[] _spriteBuf = new uint[GameShipReader.SpriteSize];

    /// <summary>지난번에 올린 색인 그림. 같은 그림이면 다시 올리지도, 다시 그리지도 않는다.</summary>
    private byte[]? _lastIndices;

    /// <summary>텍스처에 올라가 있는 그림이 무엇인지. 같으면 게임 메모리를 읽지도 않는다.</summary>
    /// <summary>
    /// 지금 올려 둔 그림이 무엇인지. 같은 열쇠면 다시 올리지 않는다.
    /// </summary>
    /// <remarks>
    /// <see cref="ShipSprites.Generation"/> 이 열쇠에 든다 — 배를 사거나 기함을 바꾸면
    /// 뱃머리는 그대로인 채 그림 벌만 갈리는데, 그 낌새가 없으면 옛 배가 그대로 떠 있는다.
    /// </remarks>
    private (int Heading, bool OnLand, bool FromGame, int WalkPhase, int Skin)? _spriteKey;

    // 지난번에 실제로 그려 낸 값. 그대로면 이번 프레임은 건너뛴다.
    private (double X, double Y) _drawnOrigin;
    private (float X, float Y, float W, float H) _drawnShip, _drawnAnchor;

    /// <summary>지난 프레임에 그린 물결 무늬 자리. 이것이 그대로면 다시 그릴 것이 없다.</summary>
    private int _drawnFlow = -1;

    /// <summary>다음 프레임은 값이 같아도 반드시 그려야 하는지. 창 크기·그림이 바뀌면 선다.</summary>
    private bool _dirty = true;

    /// <summary>
    /// 참이면 배가 서 있는다. 물음창이 떠 있는 동안 계속 나아가지 않게 하려고 둔다 —
    /// 모달 창을 띄워도 CompositionTarget.Rendering 은 그대로 돈다.
    /// </summary>
    public bool Paused { get; set; }

    /// <summary>
    /// 배(말)를 옮긴 <b>걸음 수</b>. 게임의 항해 고리 한 바퀴에 맞먹는다.
    /// </summary>
    /// <remarks>
    /// 게임은 고리를 한 바퀴 돌 때마다 발밑 칸의 눈금을 하나 붙이고
    /// (<c>0x0048EF7D</c>) 마흔여덟 눈금이 하루다. 그 고리 한 바퀴가 여기서는
    /// <see cref="TickSeconds"/> 짜리 걸음 하나라, 날을 세는 쪽이 이 수를 본다.
    /// 멎어 있거나 닻을 내렸으면 안 오른다 — 게임도 그때는 고리가 안 돈다.
    /// </remarks>
    public long Steps { get; private set; }

    /// <summary>
    /// 고리를 돈 횟수 — <b>닻을 내렸든 안 움직였든</b> 틱마다 는다. 날이 가는 눈금은
    /// 여기에 매인다(<c>0x0048EF64</c> 의 <c>0x0044AF90</c> 이 조건 없이 돈다).
    /// </summary>
    public long Ticks { get; private set; }

    /// <summary>커서를 따라 배를 몬다. 끄면 게임 함대 자리를 그대로 따라간다.</summary>
    public bool SteerWithMouse { get; set; } = true;

    /// <summary>
    /// 커서 조타가 <b>깨어 있는지</b>(<c>this+0x104</c>). 왼쪽 클릭에서만 켜지고
    /// (<c>0x0048B080</c>), 승선하면 꺼진다(<c>0x0048B5F6</c>). 꺼져 있으면 커서 쪽으로
    /// 뱃머리를 안 돌린다(<c>0x0048ECC2</c> 의 <c>je</c>).
    /// </summary>
    public bool SteerArmed { get; set; }

    /// <summary>
    /// <b>커서 쪽 길찾기 보정</b>이 드는지 — 항해사(부하 자리 1)가 있고 나침반(아이템 <c>0x21</c>)을
    /// 지녔을 때만이다(<c>0x0048ECEF</c>~<c>0x0048ED13</c>). 켜져 있으면 커서 칸으로 곧장 뱃머리를
    /// 돌리지 않고, 바닷길을 찾아 <b>첫 길목</b> 쪽으로 돌린다(<c>0x0048EE2E</c>).
    /// </summary>
    public bool PathAssist { get; set; }

    /// <summary>길찾기 보정이 마지막으로 셈한 커서 칸과 그 첫 길목.</summary>
    private (int X, int Y) _assistFrom = (int.MinValue, int.MinValue);
    private (double X, double Y)? _assistStep;

    /// <summary>
    /// 뱃머리를 그 쪽(16방위)으로 <b>곧장</b> 세우고 다시 나아간다 — 숫자판 조타다
    /// (<c>0x0048B04E</c>: 표 <c>0x005696EC[글쇠]</c> 를 <c>0x005B63CC</c> 에 박고
    /// 닻 <c>0x005B3A00</c> 을 0 으로, 커서 조타 <c>+0x104</c> 를 0 으로 둔다).
    /// </summary>
    public void SteerTo(int heading)
    {
        _desired = heading & 0xF;
        _making = true;
        _anchored = false;
        SteerArmed = false;
    }

    /// <summary>
    /// 도시에 들어가 있는지. 참이면 지도 위에 남색 막을 씌운다 — 색을 칠하는 것이 아니라
    /// 지도가 그 밑으로 비쳐 보인다(게임도 그렇다).
    /// </summary>
    public bool InCity
    {
        get => _inCity;
        set
        {
            if (_inCity == value) return;
            _inCity = value;
            ApplyCover();
        }
    }

    private bool _inCity;

    /// <summary>
    /// 사건이 도는 동안 지도를 <b>같은 남색으로 덮는다</b>.
    /// </summary>
    /// <remarks>
    /// 발견 대본이 도는 내내 게임 화면이 파래진다 — 뭍의 모래빛까지 통째로 남빛이 된다.
    /// 원본은 팔레트를 갈아 끼우는 것이고, 우리는 도시에 들어갈 때 쓰는 그 덮개를 그대로
    /// 쓴다.
    ///
    /// <b>WPF 로는 못 덮는다.</b> 이 칸은 <see cref="System.Windows.Interop.HwndHost"/> 라
    /// 자식 창(D3D 스왑체인)이 WPF 그림 위에 뜬다 — 그 위에 사각형을 얹어 봐야 밑에
    /// 깔릴 뿐이다(좌표 상자도 그래서 제 창에 띄운다). 그리는 쪽에서 섞어야 한다.
    /// </remarks>
    public bool Shaded
    {
        get => _shaded;
        set
        {
            if (_shaded == value) return;
            _shaded = value;
            ApplyCover();
        }
    }

    private bool _shaded;

    /// <summary>덮개를 지금 상태에 맞춘다. 도시에 들어갔거나 사건이 도는 동안 덮는다.</summary>
    private void ApplyCover()
    {
        // 게임 화면에서 뽑은 남색. 짙기는 지도가 비쳐 보이는 만큼만 준다.
        _renderer.Cover = _inCity || _shaded
            ? (0x24 / 255f, 0x37 / 255f, 0x5B / 255f, 0.72f)
            : default;
        _dirty = true;
    }

    /// <summary>
    /// 참이면 바다 명령(닻·상륙·출항·배 놓기·조종)을 받지 않는다. 도시 화면이 떠 있는
    /// 동안이 그렇다 — 게임도 도시에 들어가면 함대 명령 대신 도시 커맨드만 낸다.
    /// 막는 곳을 창이 아니라 여기에 둔 것은, 지도를 만지는 길이 여럿이라
    /// (마우스·커맨드 창·조작 줄) 부르는 쪽마다 검사를 흩어 놓으면 하나씩 새기 때문이다.
    /// </summary>
    public bool SeaBlocked => _inCity;

    public string Status { get; private set; } = "";

    /// <summary>Present 까지 마친 프레임 수. 진짜 그려지고 있는지 밖에서 볼 때 쓴다.</summary>
    public long FrameCount { get; private set; }

    /// <summary>스왑체인을 걸다 난 문제. 없으면 빈 문자열.</summary>
    public string SwapChainError { get; private set; } = "";

    /// <summary>
    /// 지금 배가 있는 위도·경도. 칸 좌표를 도로 바꾼 것이다 —
    /// 가로 2500칸이 경도 -180~180, 세로 1250칸이 위도 90~-90 에 그대로 대응한다.
    /// </summary>
    public (double Lat, double Lon) ShipLatLon => (
        90.0 - _shipY * 180.0 / WorldMapRenderer.CellH,
        _shipX * 360.0 / WorldMapRenderer.UnfoldedW - 180.0);

    /// <summary>지금 스왑체인 크기(화면 실픽셀).</summary>
    public (int W, int H) SurfaceSize => (_pixelW, _pixelH);

    /// <summary>
    /// 게임 한 점이 화면 실픽셀 몇 개인지 — 구름을 그리는 셈(<see cref="UpdateClouds"/>)과 같이
    /// 칸 하나를 16점으로 친다.
    /// </summary>
    public double GamePixelScale => 1.0 / (_cellsPerPixel * GamePixelsPerCell);

    /// <summary>
    /// 배(뭍이면 말) 그림 한가운데가 스왑체인 어디에 있는지(실픽셀). 배 자리를 모르면 null.
    /// </summary>
    /// <remarks>그리는 자리(<see cref="SpriteRectAt"/>)와 같은 셈이다.</remarks>
    public Point? ShipOnSurface => _shipKnown && _pixelW > 0 && _pixelH > 0
        ? new Point(_pixelW / 2.0 + WrapDx(_shipX - _centerX) / _cellsPerPixel,
                    _pixelH / 2.0 + (_shipY - _centerY) / _cellsPerPixel)
        : null;

    /// <summary>배가 화면 밖으로 나가지 않게 따라다닐지.</summary>
    public bool Follow
    {
        get => _follow;
        set => _follow = value;
    }

    protected override HandleRef BuildWindowCore(HandleRef hwndParent)
    {
        _hwnd = CreateWindowExW(0, WndClass, null, WsChild | WsVisible, 0, 0, 1, 1,
                                hwndParent.Handle, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
        if (_hwnd == IntPtr.Zero)
            throw new InvalidOperationException(
                $"함대 창의 자식 창을 만들지 못했습니다 (Win32 {Marshal.GetLastWin32Error()})");
        return new HandleRef(this, _hwnd);
    }

    protected override void DestroyWindowCore(HandleRef hwnd)
    {
        CompositionTarget.Rendering -= OnFrame;
        _backBufferView?.Dispose();
        _swapChain?.Dispose();
        _renderer.Dispose();
        _ship.Dispose();
        if (_hwnd != IntPtr.Zero) { DestroyWindow(_hwnd); _hwnd = IntPtr.Zero; }
    }

    /// <summary>WORLD.CDS / OCEAN.CDS 를 올리고 스왑체인을 건다. 실패하면 까닭을 남기고 false.</summary>
    public bool Start(string gameDir)
    {
        var world = WorldMapRenderer.LoadWorldData(System.IO.Path.Combine(gameDir, "WORLD.CDS"));
        if (world == null) { Status = "WORLD.CDS 를 읽지 못했습니다"; return false; }
        _world = world;

        // 도시 어귀를 가리려면 도시가 앉은 칸과 차지하는 칸 수가 있어야 한다.
        _cities = CityExeTable.Open(gameDir);

        // 칸을 지날 수 있는지는 게임 표가 가른다. 못 읽으면 옛 어림으로 물러선다.
        _terrain = TerrainTable.Open(gameDir);
        if (_terrain == null)
            System.Diagnostics.Debug.WriteLine($"[ShipMap] 지형표 없음: {TerrainTable.LastError}");

        var ocean = OceanTiles.LoadFromDirectory(gameDir);
        if (ocean == null) { Status = $"OCEAN.CDS 를 읽지 못했습니다 ({OceanTiles.LastError})"; return false; }

        _renderer.Initialize(world, ocean);

        // 닻. 배 그림과 같은 48x48 이라 배가 놓일 자리에 그대로 겹치면 된다.
        var anchorSprite = AnchorSprite.LoadFromDirectory(gameDir);
        if (anchorSprite == null)
            System.Diagnostics.Debug.WriteLine($"[ShipMap] 닻 없음: {AnchorSprite.LastError}");
        else
        {
            _anchorPixels = [.. anchorSprite.Pixels];    // 덧그림을 나눠 쓰므로 들고 있는다
            _renderer.SetOverlay(_anchorPixels);
        }

        // 바람·해류. 못 열어도 지도는 그대로 돈다 — 물결이 안 일고 화살표가 안 나올 뿐이다.
        _wind = WindTable.Open(gameDir);
        if (_wind == null)
            System.Diagnostics.Debug.WriteLine($"[ShipMap] 바람표 없음: {WindTable.LastError}");
        else
            _renderer.SetRippleTiles(_wind.BuildRippleTiles(_terrain));

        // 구름. 없으면 안 뜰 뿐 나머지는 그대로 돈다.
        var clouds = CloudSprites.LoadFromDirectory(gameDir);
        if (clouds == null)
            System.Diagnostics.Debug.WriteLine($"[ShipMap] 구름 없음: {CloudSprites.LastError}");
        else
            _renderer.SetCloudSprites(clouds.Bgra);

        // 배는 리스본 앞바다에서 시작한다.
        var (sx, sy) = LisbonStart();
        _shipX = _targetX = sx;
        _shipY = _targetY = sy;
        _centerX = sx;
        _centerY = sy;
        _shipKnown = true;

        _ready = true;
        CompositionTarget.Rendering += OnFrame;
        return true;
    }

    private void EnsureSwapChain(int w, int h)
    {
        if (_hwnd == IntPtr.Zero || w <= 0 || h <= 0) return;
        if (_swapChain != null && _pixelW == w && _pixelH == h) return;
        try
        {
            EnsureSwapChainCore(w, h);
            SwapChainError = "";
        }
        catch (Exception ex)
        {
            SwapChainError = ex.Message;
        }
    }

    private void EnsureSwapChainCore(int w, int h)
    {

        _backBufferView?.Dispose();
        _backBufferView = null;

        if (_swapChain == null)
        {
            using var dxgiDevice = _renderer.Device.QueryInterface<IDXGIDevice>();
            using var adapter = dxgiDevice.GetAdapter();
            using var factory = adapter.GetParent<IDXGIFactory2>();
            var desc = new SwapChainDescription1
            {
                Width = (uint)w,
                Height = (uint)h,
                Format = Format.B8G8R8A8_UNorm,
                BufferCount = 2,
                BufferUsage = Usage.RenderTargetOutput,
                SampleDescription = new SampleDescription(1, 0),
                SwapEffect = SwapEffect.FlipDiscard,
                Scaling = Scaling.None,
            };
            _swapChain = factory.CreateSwapChainForHwnd(_renderer.Device, _hwnd, desc);
        }
        else
        {
            _swapChain.ResizeBuffers(2, (uint)w, (uint)h, Format.B8G8R8A8_UNorm, SwapChainFlags.None);
        }

        using var back = _swapChain.GetBuffer<ID3D11Texture2D>(0);
        _backBufferView = _renderer.Device.CreateRenderTargetView(back);
        _pixelW = w;
        _pixelH = h;
        _dirty = true;   // 새 백버퍼는 비어 있다 — 값이 같아도 한 번은 그려야 한다
    }

    /// <summary>
    /// 자식 창이 지워졌으면(가려졌다 드러나거나 창을 옮겼을 때) 한 번은 다시 그린다.
    /// 값이 그대로라고 건너뛰면 지워진 자리가 그대로 남는다.
    /// </summary>
    protected override IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        const int WmPaint = 0x000F;
        if (msg == WmPaint) _dirty = true;
        return base.WndProc(hwnd, msg, wParam, lParam, ref handled);
    }

    private void OnFrame(object? sender, EventArgs e)
    {
        if (!_ready || _hwnd == IntPtr.Zero) return;

        var dpi = VisualTreeHelper.GetDpi(this);
        int w = (int)Math.Round(ActualWidth * dpi.DpiScaleX);
        int h = (int)Math.Round(ActualHeight * dpi.DpiScaleY);
        EnsureSwapChain(w, h);
        if (_backBufferView == null) return;

        var now = _clock.Elapsed;
        double dt = Math.Min((now - _lastFrame).TotalSeconds, 0.1);   // 창이 멈췄다 살아나도 튀지 않게
        _lastFrame = now;

        int flowKey = UpdateFlow(dt);

        // 커서를 칸 좌표로 옮기려면 이번 프레임의 원점이 필요하다. 배를 옮기기 전 값으로 잡는다.
        var origin = (_centerX - w / 2.0 * _cellsPerPixel, _centerY - h / 2.0 * _cellsPerPixel);
        _lastOrigin = origin;
        _lastDpiX = dpi.DpiScaleX;
        _lastDpiY = dpi.DpiScaleY;
        UpdateShip(dt, origin, dpi.DpiScaleX, dpi.DpiScaleY);

        // 배를 따라간다 — 가장자리에 다가왔을 때만 화면을 넘긴다.
        if (_follow && _shipKnown) FollowShip(w, h);
        origin = (_centerX - w / 2.0 * _cellsPerPixel, _centerY - h / 2.0 * _cellsPerPixel);

        var rect = ShowShip && _shipKnown && _spriteReady ? SpriteRectAt(_shipX, _shipY, origin)
                                             : (0f, 0f, 0f, 0f);

        // 덧그림 한 장을 두 가지로 나눠 쓴다. 둘이 같이 뜰 일은 없다 — 상륙하면 닻이 풀린다.
        //   정박 중  닻. 그림이 배와 같은 48x48 이고 그 안에서 왼쪽 아래에만 찍혀 있으므로
        //           배와 같은 자리에 겹치면 게임처럼 배 왼쪽 아래에 걸린다.
        //   뭍에 있을 때  대 둔 배. 어디로 상륙했는지 그 자리에 남는다.
        var overlay = (0f, 0f, 0f, 0f);
        if (ShowShip && _anchored && !_onLand && _shipKnown && _spriteReady) overlay = rect;
        else if (ShowShip && _onLand && _moored) overlay = SpriteRectAt(_mooredX, _mooredY, origin);
        SyncOverlaySprite();

        // 남의 배가 옮겨 앉았으면 다시 그려야 한다. 자리가 그대로면 아무 일도 없다 —
        // 사람은 하루에 한 걸음이라 예순 프레임 가운데 쉰아홉은 같은 그림이다.
        if (SyncFolk(origin, w, h)) _dirty = true;

        // 자동항해 항로. 마디 자리는 고정된 칸이라 원점이 그대로면 화면 자리도 그대로다.
        SyncRoute(origin);

        // 지난 프레임과 똑같으면 그리지 않는다. 배는 0.1초에 한 걸음씩 옮기고 지도는
        // 가장자리에 닿아야 넘어가므로, 60fps 로 도는 동안 거의 다 같은 그림이다.
        if (!_dirty && origin == _drawnOrigin && rect == _drawnShip && overlay == _drawnAnchor
            && flowKey == _drawnFlow) return;

        _renderer.RenderTo(_backBufferView, w, h, origin, (_cellsPerPixel, _cellsPerPixel), rect, overlay);
        _swapChain!.Present(1, PresentFlags.None);
        _drawnOrigin = origin;
        _drawnShip = rect;
        _drawnAnchor = overlay;
        _drawnFlow = flowKey;
        _dirty = false;
        FrameCount++;
    }

    // ── 남의 배 ─────────────────────────────────────────────────────────────

    /// <summary>
    /// 지도에 함께 낼 사람들 — 세계 칸 자리와 뱃머리(16방위), 그리고 인물 번호다.
    /// </summary>
    /// <remarks>
    /// 누가 어디 있는지는 <c>PersonWorld</c> 가 안다. 여기서는 <b>받아서 그리기만</b> 한다 —
    /// 지도는 인물 표를 모르는 편이 낫다. 게임도 지도 객체가 인물 배열을 훑어 가까운
    /// 열여섯을 채운다(<c>0x00426790</c>).
    /// </remarks>
    public Func<IReadOnlyList<(double X, double Y, int Heading, int Person)>>? FolkAt { get; set; }

    /// <summary>이번 프레임에 실제로 그린 사람들 — 가까운 차례다.</summary>
    private readonly List<(double X, double Y, int Heading, int Person)> _folk = [];

    private readonly MapD3DRenderer.FolkDraw[] _folkDraw =
        new MapD3DRenderer.FolkDraw[MapD3DRenderer.MaxFolk];

    private bool _folkArtReady;

    /// <summary>
    /// 남의 그림 여덟 장을 한 번 올린다 — 배 넉 장, 말 넉 장이고 각각 북 · 서 · 남 · 동 차례다.
    /// </summary>
    /// <remarks>
    /// 내 배·말과 같은 <c>asset/ship</c> 벌을 쓴다. 게임도 남의 배를 따로 그리지 않고 내 것과 같은
    /// 그림 벌(바다 <c>0x00569FE4</c> · 뭍 <c>0x00569FE8</c>)을 쓴다.
    /// </remarks>
    private void UploadFolkSprites()
    {
        if (_folkArtReady) return;

        int one = MapD3DRenderer.FolkSize * MapD3DRenderer.FolkSize;
        var atlas = new uint[one * MapD3DRenderer.FolkFrames];

        // 16방위에서 북(0) · 서(4) · 남(8) · 동(12) 을 뽑는다. 앞 넉 장이 배, 뒤 넉 장이 말이다.
        for (int i = 0; i < MapD3DRenderer.FolkFrames; i++)
        {
            bool land = i >= MapD3DRenderer.FolkLandFrame;
            int heading = (i % MapD3DRenderer.FolkLandFrame) * 4;
            var frame = ShipSprites.Frame(heading, onLand: land);
            if (frame.Length != one) return;                 // 그림 벌이 아직 안 열렸다
            frame.CopyTo(atlas.AsSpan(i * one));
        }

        _renderer.SetFolkSprites(atlas);
        _folkArtReady = true;
    }

    /// <summary>
    /// 가까운 사람 열여섯을 골라 화면 자리로 옮긴다.
    /// </summary>
    /// <remarks>
    /// 게임은 인물 번호 차례로 앞에서 열여섯을 채우는데, 우리는 <b>가까운 차례</b>로 고른다 —
    /// 화면에 든 사람이 번호가 커서 밀리면 눈에 안 보여 이상하다.
    /// </remarks>
    /// <returns>지난 프레임과 <b>달라졌으면</b> 참 — 그때만 다시 그린다.</returns>
    private bool SyncFolk((double X, double Y) origin, int w, int h)
    {
        int was = _folkShown;
        _folk.Clear();
        _folkShown = 0;

        if (FolkAt == null) { _renderer.SetFolk([]); return was > 0; }

        UploadFolkSprites();
        if (!_folkArtReady) { _renderer.SetFolk([]); return was > 0; }

        // 화면에 든 것만 본다. 가장자리 한 칸은 그림이 걸쳐 보이도록 넉넉히 둔다.
        double left = origin.X - FolkMargin, top = origin.Y - FolkMargin;
        double right = origin.X + w * _cellsPerPixel + FolkMargin;
        double bottom = origin.Y + h * _cellsPerPixel + FolkMargin;

        foreach (var one in FolkAt())
        {
            double x = Fold(one.X, origin.X);
            if (x < left || x > right || one.Y < top || one.Y > bottom) continue;
            _folk.Add((x, one.Y, one.Heading, one.Person));
        }

        if (_folk.Count > MapD3DRenderer.MaxFolk)
        {
            _folk.Sort((a, b) => Near(a).CompareTo(Near(b)));
            _folk.RemoveRange(MapD3DRenderer.MaxFolk, _folk.Count - MapD3DRenderer.MaxFolk);
        }

        float size = (float)(3.0 / _cellsPerPixel);
        float scale = size / MapD3DRenderer.FolkSize;
        bool moved = was != _folk.Count;

        for (int i = 0; i < _folk.Count; i++)
        {
            var one = _folk[i];
            // 16방위 → 넉 장. 뭍 칸에 서 있으면 말 쪽 넉 장으로 내린다(0x0048A799).
            int frame = (one.Heading & 0xF) >> 2;
            if (FolkOnLand(one.X, one.Y)) frame += MapD3DRenderer.FolkLandFrame;

            var draw = new MapD3DRenderer.FolkDraw(
                (float)((one.X - origin.X) / _cellsPerPixel - size / 2),
                (float)((one.Y - origin.Y) / _cellsPerPixel - size / 2),
                frame,
                scale);

            if (!draw.Equals(_folkDraw[i])) moved = true;
            _folkDraw[i] = draw;
        }
        _folkShown = _folk.Count;

        if (moved) _renderer.SetFolk(_folkDraw.AsSpan(0, _folkShown));
        return moved;
    }

    /// <summary>지난 프레임에 그린 남의 배 수.</summary>
    private int _folkShown;

    /// <summary>
    /// 그 사람이 뭍 칸에 서 있는지 — 게임은 부류가 2 이상이면 배 대신 말을 그린다.
    /// </summary>
    /// <remarks>
    /// 게임의 <c>0x0048A794</c> 가 자리를 열여섯으로 나눠 칸을 잡고 <c>0x00426710</c> 으로 부류를
    /// 본 뒤 <c>cmp eax, 2 / jge</c> 로 가른다. 부류표를 못 열었으면 뱃길 판정으로 물러선다.
    /// </remarks>
    private bool FolkOnLand(double cellX, double cellY)
    {
        if (_terrain != null && _world != null)
            return _terrain.ClassOfCell(CellValue(cellX, cellY)) >= 2;
        return IsLand(cellX, cellY);
    }

    /// <summary>화면 밖 몇 칸까지 그릴지 — 그림이 세 칸이라 그 반이면 넉넉하다.</summary>
    private const double FolkMargin = 3;

    /// <summary>내 배에서 그 사람까지 칸 거리의 제곱.</summary>
    private double Near((double X, double Y, int Heading, int Person) one)
    {
        double dx = one.X - _shipX, dy = one.Y - _shipY;
        return dx * dx + dy * dy;
    }

    /// <summary>세계가 감기므로 화면 쪽으로 당겨 놓는다 — 동경 180도를 넘어도 이어 보인다.</summary>
    private static double Fold(double x, double originX)
    {
        double w = WorldMapRenderer.UnfoldedW;
        while (x - originX < -w / 2) x += w;
        while (x - originX > w / 2) x -= w;
        return x;
    }

    /// <summary>
    /// 내 배에서 <paramref name="radiusCells"/> 칸 안(경계 포함)에 든 사람들의 번호.
    /// </summary>
    /// <remarks>
    /// 지금 그리고 있는 사람들 가운데서 고른다 — 게임도 화면 열여섯 칸에 든 사람만 잰다
    /// (<c>0x0048C049</c>).
    /// </remarks>
    public List<int> FolkWithin(double radiusCells)
    {
        var got = new List<int>();
        double limit = radiusCells * radiusCells;
        foreach (var one in _folk)
            if (Near(one) <= limit) got.Add(one.Person);
        return got;
    }

    /// <summary>
    /// 칸 좌표에 48x48 그림 한 장이 놓일 화면 사각형. 게임에서 한 칸이 16점이니 세 칸이다.
    /// </summary>
    private (float X, float Y, float W, float H) SpriteRectAt(
        double cellX, double cellY, (double X, double Y) origin)
    {
        float size = (float)(3.0 / _cellsPerPixel);
        return ((float)((cellX - origin.X) / _cellsPerPixel - size / 2),
                (float)((cellY - origin.Y) / _cellsPerPixel - size / 2),
                size, size);
    }

    // ── 자동항해 ────────────────────────────────────────────────────────────

    /// <summary>
    /// 지금 짜 둔 바닷길. 시작 칸부터 도착 칸까지, 뭍을 피해 갈 수 있는 마디(칸 좌표)들이다.
    /// null 이면 자동항해 중이 아니다.
    /// </summary>
    private List<(double X, double Y)>? _autoRoute;

    /// <summary>지금 향하고 있는 마디의 <see cref="_autoRoute"/> 안 차례.</summary>
    private int _autoIndex;

    /// <summary>자동항해 중인지.</summary>
    public bool AutoSailing => _autoRoute != null;

    /// <summary>지금 항로의 마디 수. 자동항해 중이 아니면 0.</summary>
    public int AutoRouteCount => _autoRoute?.Count ?? 0;

    /// <summary>지금 향하는 마디 차례(0부터). 자동항해 중이 아니면 0.</summary>
    public int AutoWaypointIndex => _autoIndex;

    /// <summary>
    /// 자동항해가 끝났을 때 알린다 — 도착했다, 또는 길이 막혀 멈췄다.
    /// </summary>
    public event Action<string>? AutoSailEnded;

    /// <summary>도착으로 칠 만큼 마디에 다가섰는지(칸).</summary>
    private const double AutoWaypointRadius = 2.0;

    /// <summary>이만큼(칸) 움직이지 않고 이 틱 수를 넘기면 막힌 것으로 보고 멈춘다.</summary>
    private const double StuckMoveThreshold = 1.0;
    private const int StuckTickLimit = 150;   // 0.1초 x 150 = 15초

    private double _stuckX, _stuckY;
    private int _stuckTicks;

    /// <summary>
    /// 그 칸까지 바닷길을 찾아 자동항해를 시작한다. 뭍이나 도시 안에서는, 지도를 아직 못
    /// 읽었으면, 바닷길을 못 찾았으면 시작하지 않는다.
    /// </summary>
    public (bool Ok, string Message) StartAutoSail(double destX, double destY)
    {
        if (!_ready || _world == null || _terrain == null) return (false, "지도를 아직 읽지 못했습니다");
        if (SeaBlocked) return (false, "도시 안에서는 자동항해를 쓸 수 없습니다");
        if (!_shipKnown || _onLand) return (false, "바다에 있을 때만 자동항해를 쓸 수 있습니다");

        var route = Engine.Sea.SeaPathfinder.FindRoute(_world, _terrain, (_shipX, _shipY), (destX, destY));
        if (route == null) return (false, "바닷길을 찾지 못했습니다");
        if (route.Count < 2) return (false, "이미 그 자리 가까이 있습니다");

        _autoRoute = route;
        _autoIndex = 0;
        _anchored = false;
        _tickAccum = 0;
        _stuckX = _shipX;
        _stuckY = _shipY;
        _stuckTicks = 0;
        _dirty = true;
        return (true, $"{route.Count}개 마디로 바닷길을 짰습니다");
    }

    /// <summary>자동항해를 끈다. 그 자리에 세우지 않는다 — 손으로 이어서 몰 수 있게 둔다.</summary>
    public void StopAutoSail()
    {
        if (_autoRoute == null) return;
        _autoRoute = null;
        _autoIndex = 0;
        _dirty = true;
    }

    /// <summary>
    /// 다음 마디를 바라보게 목표 자리를 잡는다. 이미 다가선 마디는 건너뛴다.
    /// 마지막 마디까지 다다랐으면 도착으로 치고 닻을 내린다.
    /// </summary>
    private void UpdateAutoTarget()
    {
        if (_autoRoute is not { } route) return;
        while (_autoIndex < route.Count)
        {
            var (wx, wy) = route[_autoIndex];
            double dx = WrapDx(wx - _shipX), dy = wy - _shipY;
            if (dx * dx + dy * dy <= AutoWaypointRadius * AutoWaypointRadius) { _autoIndex++; continue; }
            _targetX = _shipX + dx;
            _targetY = _shipY + dy;
            return;
        }
        CompleteAutoSail("도착했습니다 — 닻을 내렸습니다");
    }

    private void CompleteAutoSail(string message)
    {
        _autoRoute = null;
        _autoIndex = 0;
        _anchored = true;
        _tickAccum = 0;
        _dirty = true;
        AutoSailEnded?.Invoke(message);
    }

    /// <summary>
    /// 자동항해 중에 오래 못 나아가면 멈춘다 — 길찾기가 어긋나 뭍 가까이서 맴도는 것을
    /// 막는 마지막 안전판이다. <see cref="Sail"/> 이 걸음마다 부른다.
    /// </summary>
    private void UpdateStuckGuard()
    {
        double dx = _shipX - _stuckX, dy = _shipY - _stuckY;
        if (dx * dx + dy * dy >= StuckMoveThreshold * StuckMoveThreshold)
        {
            _stuckX = _shipX;
            _stuckY = _shipY;
            _stuckTicks = 0;
            return;
        }
        if (++_stuckTicks < StuckTickLimit) return;

        _autoRoute = null;
        _autoIndex = 0;
        _stuckTicks = 0;
        _dirty = true;
        AutoSailEnded?.Invoke("길이 막혀 자동항해를 멈췄습니다");
    }

    /// <summary>지난 프레임에 그린 항로 마디 수.</summary>
    private int _routeShown;

    private readonly MapD3DRenderer.RouteDraw[] _routeDraw =
        new MapD3DRenderer.RouteDraw[MapD3DRenderer.MaxRoutePoints];

    /// <summary>
    /// 항로 마디를 화면 자리로 옮겨 렌더러에 건넨다. 마디가 화면에 다 못 실으면 고르게 골라 줄인다 —
    /// 길찾기 자체는 그대로 다 쓴다(<see cref="UpdateAutoTarget"/>).
    /// </summary>
    private void SyncRoute((double X, double Y) origin)
    {
        if (_autoRoute is not { Count: > 0 } route)
        {
            if (_routeShown > 0) { _renderer.SetRoute([]); _routeShown = 0; }
            return;
        }

        int n = route.Count;
        int shown = Math.Min(n, MapD3DRenderer.MaxRoutePoints);
        for (int i = 0; i < shown; i++)
        {
            int src = shown == 1 ? 0 : i * (n - 1) / (shown - 1);
            var (wx, wy) = route[src];
            double x = Fold(wx, origin.X);
            _routeDraw[i] = new MapD3DRenderer.RouteDraw(
                (float)((x - origin.X) / _cellsPerPixel),
                (float)((wy - origin.Y) / _cellsPerPixel), true);
        }
        _renderer.SetRoute(_routeDraw.AsSpan(0, shown));
        _routeShown = shown;
    }

    // ── 대 둔 배 ────────────────────────────────────────────────────────────

    /// <summary>상륙하며 배를 대 둔 자리와 그때의 뱃머리.</summary>
    private double _mooredX, _mooredY;
    private int _mooredHeading;
    private bool _moored;

    /// <summary>덧그림 자리에 지금 무엇이 올라가 있는지.</summary>
    private enum OverlayArt { None, Anchor, MooredShip }

    private OverlayArt _overlayArt = OverlayArt.None;
    private uint[]? _anchorPixels;

    /// <summary>
    /// 덧그림을 지금 쓸 것으로 갈아 끼운다. 닻과 대 둔 배가 같은 자리를 나눠 쓰므로,
    /// 무엇이 올라가야 하는지 바뀔 때만 텍스처를 올린다.
    /// </summary>
    private void SyncOverlaySprite()
    {
        // 뭍에서 선 것은 닻이 아니다 — 그 자리에는 대 둔 배가 그대로 남아 있어야 한다.
        var want = _anchored && !_onLand ? OverlayArt.Anchor
                 : _onLand && _moored ? OverlayArt.MooredShip
                 : OverlayArt.None;
        if (want == _overlayArt || want == OverlayArt.None) { _overlayArt = want; return; }

        if (want == OverlayArt.Anchor)
        {
            if (_anchorPixels != null) _renderer.SetOverlay(_anchorPixels);
        }
        else
        {
            // 대 둔 배는 상륙할 때의 뱃머리 그대로 둔다. 게임 그림이 아니라 asset 것을 쓴다 —
            // 살아 있는 게임 함대를 읽는 길(GameShipReader)은 지금 뱃머리만 내주므로
            // 대 둔 배의 방향을 물을 수가 없다.
            var frame = ShipSprites.Frame(_mooredHeading, onLand: false);
            if (!frame.IsEmpty) _renderer.SetOverlay(frame);
        }
        _overlayArt = want;
        _dirty = true;
    }

    /// <summary>표를 못 열었을 때 볼 달. 놀이가 시작하는 달이다.</summary>
    private const int DefaultMonth = 4;

    /// <summary>
    /// 물결과 화살표 자료를 이번 프레임 것으로 맞춘다. 돌려주는 값은 <b>물결 무늬가 선 자리</b>라,
    /// 이것이 지난 프레임과 같으면 다시 그릴 것이 없다.
    /// </summary>
    /// <remarks>
    /// 게임은 항해 루프를 한 번 돌 때마다 틱을 하나 올린다(<c>0x0048EF82</c>). 여기서도 한
    /// 걸음(<see cref="TickSeconds"/>)과 같은 길이로 올린다 — 60fps 로 올리면 물결이 게임보다
    /// 여섯 배 빨리 흐르고, 프레임마다 지도를 다시 그리게 된다.
    ///
    /// 무늬는 <c>세기 x 틱 x 16 / 64</c> 만큼 흐르므로 세기가 1 이면 네 틱에 한 칸이다.
    /// 그 몫이 바뀔 때만 다시 그린다.
    /// </remarks>
    private int UpdateFlow(double dt)
    {
        if (_wind == null) return 0;

        // 창이 떠 있는 동안에는 물결도 구름도 선다 — 도시 창이 지도를 남색 막으로 덮은
        // 때(<see cref="_inCity"/>)만이 아니라 커맨드 창·물음창으로 멈춘 때
        // (<see cref="Paused"/>)도 그렇다. 게임은 창이 뜨면 항해 루프째로 서서
        // 날짜도 구름도 그 자리에 멎는다 — 하늘만 살아 있으면 멈춘 것으로 안 보인다.
        int ticks = 0;
        if (!_inCity && !Paused)
        {
            _rippleAccum += dt;
            while (_rippleAccum >= TickSeconds) { _rippleAccum -= TickSeconds; _rippleTick++; ticks++; }
        }

        int month = MonthOf?.Invoke() ?? DefaultMonth;
        if (month != _flowMonth) { _flowMonth = month; RefreshFlowGrid(month); }

        // 게임은 함대가 선 칸의 바람·해류 하나로 화면 전체를 흘린다. 여기서도 그대로 한다.
        int cell = WindTable.CellOf((int)(_shipX * OceanTiles.TileW), (int)(_shipY * OceanTiles.TileW));
        var flow = cell < 0 ? default : _wind.CurrentAt(cell);
        var (dx, dy) = _wind.Vector(flow.Dir);
        _renderer.Ripple = (dx, dy, flow.Speed, _rippleTick);

        var wind = cell < 0 ? default : HeldWind(cell, month);
        UpdateClouds(wind.Dir, wind.Speed, ticks);

        // 구름이 떠 있으면 틱마다 자리가 달라지므로 틱 자체가 곧 그림이다.
        if (_cloudCount > 0) return _rippleTick;
        return (flow.Dir << 26) | (flow.Speed << 22) | ((flow.Speed * _rippleTick / 4) & 0x3FFFFF);
    }

    /// <summary>
    /// 화살표가 읽을 50x25 격자를 굽는다. 달이 바뀔 때만 부른다.
    /// </summary>
    /// <remarks>
    /// 표에는 뭍 칸에도 값이 들어 있다 — 격자 한 칸이 지도 50x50 칸(경위도 7.2도)이라
    /// 대륙 한가운데도 제 방위를 갖는다. 그대로 그리면 아메리카 복판에 바람 화살표가 뜬다.
    /// 배가 갈 데가 아니니 <b>물이 넉넉한 칸만</b> 남긴다.
    /// </remarks>
    private void RefreshFlowGrid(int month)
    {
        if (_wind == null) return;
        var grid = new uint[WindTable.Count];
        for (int i = 0; i < WindTable.Count; i++)
        {
            if (!WorthDrawing(i)) continue;   // 0 이면 세기가 0 이라 셰이더가 안 그린다
            grid[i] = Pack(_wind.WindAt(i, month)) | (Pack(_wind.CurrentAt(i)) << 16);
        }
        _renderer.SetFlowGrid(grid);
        _dirty = true;
    }

    /// <summary>격자 한 칸에서 물이 이만큼은 돼야 화살표를 그린다.</summary>
    private const double ArrowMinWaterRatio = 0.25;

    /// <summary>격자 한 칸을 몇 칸 걸러 재는지. 50x50 을 다 보지 않아도 비율은 나온다.</summary>
    private const int ArrowProbeStep = 5;

    private bool WorthDrawing(int flowCell)
    {
        if (_world == null || _terrain == null) return true;   // 못 재면 다 그린다

        int cellsPerSide = WindTable.CellRaw / OceanTiles.TileW;    // 800 / 16 = 50
        int x0 = flowCell % WindTable.Cols * cellsPerSide;
        int y0 = flowCell / WindTable.Cols * cellsPerSide;

        int water = 0, total = 0;
        for (int y = 0; y < cellsPerSide; y += ArrowProbeStep)
            for (int x = 0; x < cellsPerSide; x += ArrowProbeStep)
            {
                total++;
                if (_terrain.CanSail(CellValue(x0 + x, y0 + y))) water++;
            }
        return water >= total * ArrowMinWaterRatio;
    }

    /// <summary>화살표가 읽는 낱말. 방위와 세기를 게임 표와 같은 자리에 넣는다.</summary>
    private static uint Pack(WindTable.Flow f) => (uint)(f.Dir | (f.Speed << 4));

    // ── 구름 ─────────────────────────────────────────────────────────────────

    /// <summary>구름 한 장의 지금 상태. 자리는 <b>게임 점</b>(칸당 16점) 기준이다.</summary>
    private struct Cloud { public int X, Y, AccX, AccY, Shape; }

    private readonly Cloud[] _cloudState = new Cloud[MapD3DRenderer.MaxClouds];
    private readonly MapD3DRenderer.CloudDraw[] _cloudDraw =
        new MapD3DRenderer.CloudDraw[MapD3DRenderer.MaxClouds];
    private bool _cloudsPlaced;
    private int _cloudCount;
    private readonly Random _cloudRng = new();

    /// <summary>구름 여섯의 밑그림 번호(<c>0x00519C70</c>). 작은 것 셋, 큰 것 셋이다.</summary>
    private static readonly int[] CloudBase = [3, 3, 3, 0, 0, 0];

    /// <summary>구름 여섯의 속도 배수(<c>0x00519C88</c>). 큰 것이 조금 빠르다.</summary>
    private static readonly int[] CloudSpeed = [3, 3, 3, 4, 4, 4];

    /// <summary>그림 넘김표(<c>0x00519CA0</c>). 색인은 틱마다 (색인+1) % 3 으로 돈다.</summary>
    private static readonly int[] CloudShape = [0, 1, 2, 1];

    /// <summary>게임 화면 크기. 구름 몇 장이 어울리는지 이것으로 견준다.</summary>
    private const double CloudRefW = 640, CloudRefH = 480;

    /// <summary>이보다 작아지면 안 그린다. 멀리서 보면 점 여섯 개라 티끌만 남는다.</summary>
    private const double CloudMinPixels = 48;

    /// <summary>게임 한 점이 덮는 칸 수. 게임은 칸 하나를 16점으로 그린다.</summary>
    private const double GamePixelsPerCell = OceanTiles.TileW;

    /// <summary>
    /// 구름을 한 틱 흘리고 이번 프레임에 그릴 자리를 renderer 에 건넨다.
    /// </summary>
    /// <remarks>
    /// 게임(<c>0x004893D0</c>)은 구름을 <b>화면 좌표</b>로 들고 640x480 을 돌린다. 여기서는
    /// 지도를 키우고 줄일 수 있으니 <b>게임 점</b>(칸당 16점)으로 들고 있다가 그릴 때만
    /// 배율을 곱한다 — 그래야 구름 한 장이 늘 지도 10칸 x 7.5칸을 덮어, 게임에서 보던
    /// 크기 그대로다. 장 수도 보이는 넓이에 맞춰 줄인다(640x480 에 여섯 장 꼴).
    /// </remarks>
    private void UpdateClouds(int windDir, int windSpeed, int ticks)
    {
        double scale = 1.0 / (_cellsPerPixel * GamePixelsPerCell);   // 실픽셀 / 게임점
        if (scale <= 0 || _pixelW <= 0 || _pixelH <= 0
            || CloudSprites.Width * scale < CloudMinPixels)
        {
            _cloudCount = 0;
            _renderer.SetClouds(default);
            return;
        }

        int gw = Math.Max(1, (int)(_pixelW / scale));
        int gh = Math.Max(1, (int)(_pixelH / scale));
        _cloudCount = Math.Clamp(
            (int)Math.Round(MapD3DRenderer.MaxClouds * (gw * (double)gh) / (CloudRefW * CloudRefH)),
            1, MapD3DRenderer.MaxClouds);

        if (!_cloudsPlaced) PlaceClouds(gw, gh);

        var (vx, vy) = _wind!.Vector(windDir);
        for (int t = 0; t < ticks; t++)
            for (int i = 0; i < _cloudCount; i++)
                DriftCloud(ref _cloudState[i], vx * windSpeed * CloudSpeed[i],
                           vy * windSpeed * CloudSpeed[i], gw, gh);

        for (int i = 0; i < _cloudCount; i++)
            _cloudDraw[i] = new MapD3DRenderer.CloudDraw(
                (float)(_cloudState[i].X * scale), (float)(_cloudState[i].Y * scale),
                CloudBase[i] + CloudShape[_cloudState[i].Shape], (float)scale);
        _renderer.SetClouds(_cloudDraw.AsSpan(0, _cloudCount));
    }

    /// <summary>게임과 같이 3열로 벌려 놓는다(<c>0x0048906B</c>).</summary>
    private void PlaceClouds(int gw, int gh)
    {
        for (int i = 0; i < _cloudState.Length; i++)
        {
            _cloudState[i] = new Cloud { X = i % 3 * 128 % gw, Y = i / 3 * 128 % gh };
            FixParity(ref _cloudState[i]);
        }
        _cloudsPlaced = true;
    }

    private void DriftCloud(ref Cloud c, int stepX, int stepY, int gw, int gh)
    {
        c.AccX += stepX;
        c.AccY += stepY;
        while (c.AccX >= WindTable.VectorLength) { c.AccX -= WindTable.VectorLength; c.X++; }
        while (c.AccX <= -WindTable.VectorLength) { c.AccX += WindTable.VectorLength; c.X--; }
        while (c.AccY >= WindTable.VectorLength) { c.AccY -= WindTable.VectorLength; c.Y++; }
        while (c.AccY <= -WindTable.VectorLength) { c.AccY += WindTable.VectorLength; c.Y--; }

        // 화면 밖으로 나가면 반대쪽 끝에서 아무 자리로 다시 들어온다(0x00489456~).
        int w = CloudSprites.Width, h = CloudSprites.Height;
        if (c.X <= -w) { c.X = gw - 1; c.Y = _cloudRng.Next(gh) - (h - 1); }
        else if (c.X >= gw) { c.X = -(w - 1); c.Y = _cloudRng.Next(gh) - (h - 1); }
        if (c.Y <= -h) { c.Y = gh - 1; c.X = _cloudRng.Next(gw) - (w - 1); }
        else if (c.Y >= gh) { c.Y = -(h - 1); c.X = _cloudRng.Next(gw) - (w - 1); }

        FixParity(ref c);
        c.Shape = (c.Shape + 1) % 3;
    }

    /// <summary>
    /// <c>x + y</c> 를 짝수로 맞춘다(<c>0x004890CF</c>). 구름은 바둑판으로 반만 찍힌
    /// 반투명 그림이라, 격자 짝이 어긋나면 무늬가 뭉개진다.
    /// </summary>
    private static void FixParity(ref Cloud c)
    {
        if (((c.X + c.Y) & 1) != 0) c.X++;
    }

    /// <summary>
    /// 배가 화면 가장자리 <see cref="EdgeMarginPixels"/> 점 안에 들어왔으면 화면을 다음
    /// 자리로 넘긴다. 여백 안에 있는 동안에는 화면을 그대로 둔다.
    /// </summary>
    private void FollowShip(int w, int h)
    {
        // 창이 여백 두 겹보다 좁으면 여백이 화면을 다 먹는다 — 반보다는 작게 잡는다.
        double mx = Math.Min(EdgeMarginPixels, w / 2.0 - 1);
        double my = Math.Min(EdgeMarginPixels, h / 2.0 - 1);

        double sx = w / 2.0 + WrapDx(_shipX - _centerX) / _cellsPerPixel;
        double sy = h / 2.0 + (_shipY - _centerY) / _cellsPerPixel;
        if (sx >= mx && sx <= w - mx && sy >= my && sy <= h - my) return;

        // 넘길 때는 배를 화면 한가운데에 놓는다. 어느 쪽으로 가든 다시 여백에 닿을 때까지
        // 반 화면이 남으므로, 가장자리를 스치듯 지나도 화면이 들썩이지 않는다.
        _centerX = _shipX;
        _centerY = _shipY;
    }

    /// <summary>가로로 이어진 지도에서 가장 가까운 쪽으로 잰 가로 차이.</summary>
    private static double WrapDx(double dx) =>
        dx - Math.Floor(dx / WorldMapRenderer.UnfoldedW + 0.5) * WorldMapRenderer.UnfoldedW;

    private void UpdateShip(double dt, (double X, double Y) origin, double dpiX, double dpiY)
    {
        // 그림은 게임 것을 쓴다 — 게임이 떠 있어야 배 모양이 나온다.
        if (!_ship.IsAttached) _ship.TryAttach();

        if (SteerWithMouse)
        {
            if (AutoSailing)
            {
                UpdateAutoTarget();
            }
            else if (_mouseInside && SteerArmed)
            {
                // 커서가 가리키는 칸으로 뱃머리를 돌린다.
                _targetX = origin.X + _mouse.X * dpiX * _cellsPerPixel;
                _targetY = origin.Y + _mouse.Y * dpiY * _cellsPerPixel;

                // 항해사와 나침반이 있으면 곧장 돌지 않고 바닷길의 첫 길목으로 돈다(0x0048EE2E).
                if (PathAssist && !_onLand && NextWaypoint(_targetX, _targetY) is { } step)
                {
                    _targetX = step.X;
                    _targetY = step.Y;
                }
            }
            _hasHeadingTarget = AutoSailing || (_mouseInside && SteerArmed);
            Sail(dt);
            // <b>멈춤과 커서 놓침을 먼저 적는다.</b> 이 둘은 뱃머리가 안 도는 까닭인데,
            // 예전 줄은 그래도 "커서 쪽으로 항해 중" 이라 적어 서 있는 배와 구별이 안 됐다.
            Status = AutoSailing
                ? $"자동항해 중 {_shipX:F1}, {_shipY:F1} 칸 · 방향 {HeadingName} · " +
                  (Paused ? "멈춤(창이 떠 있다)" : $"마디 {_autoIndex + 1}/{AutoRouteCount} 쪽으로")
                : $"{(_onLand ? "말" : "배")} {_shipX:F1}, {_shipY:F1} 칸 · 방향 {HeadingName} · " +
                     (Paused ? "멈춤(창이 떠 있다)"
                             : _anchored ? (_onLand ? "멈춰 서 있다" : "닻을 내리고 정박 중")
                             : _blocked ? (_onLand ? "바다에 막혔습니다" : "육지에 막혔습니다")
                             : !_mouseInside ? "가던 쪽으로(커서 놓침)"
                             : _onLand ? "커서 쪽으로 이동 중" : "커서 쪽으로 항해 중") +
                     (_ship.IsAttached ? "" : " · 그림은 구워 둔 것");
        }
        else
        {
            // 게임 함대를 따라가는 예전 방식.
            var cell = _ship.TryReadCell();
            if (cell != null)
            {
                _targetX = cell.Value.CellX;
                _targetY = cell.Value.CellY;
                if (!_shipKnown) { _shipX = _targetX; _shipY = _targetY; _shipKnown = true; }
                Status = $"게임 함대 {_targetX:F1}, {_targetY:F1} 칸";
            }
            else
            {
                Status = _ship.IsAttached ? "게임이 아직 항해 중이 아닙니다" : "게임(cds_95)이 떠 있지 않습니다";
            }
            // 표본이 띄엄띄엄 와도 이어져 보이도록 조금씩 따라붙는다.
            _shipX += (_targetX - _shipX) * 0.15;
            _shipY += (_targetY - _shipY) * 0.15;
            var spr0 = _ship.TryReadSprite();
            if (spr0 != null) UploadGameSprite(spr0);
            _spriteKey = null;   // 이쪽에서 올린 그림은 우리 뱃머리와 무관하다 — 돌아가면 다시 올린다
            return;
        }

        // 게임이 떠 있으면 그 그림을(함선 종류에 맞는 4벌 중 하나), 아니면 asset/ship 의 것을 쓴다.
        // 같은 그림이면 게임 메모리를 읽지도, 텍스처를 올리지도 않는다 — 뱃머리가 그대로면
        // 프레임마다 할 일이 없다.
        var key = (_heading, _onLand, _ship.IsAttached, _onLand ? _walkPhase : 0,
                   ShipSprites.Generation);
        if (_spriteKey == key) return;

        var indices = _ship.IsAttached
            ? _ship.TryReadSprite(_heading, _onLand, _onLand ? _walkPhase : -1)
            : null;
        if (indices != null) UploadGameSprite(indices);
        else
        {
            var frame = ShipSprites.Frame(_heading, _onLand, _onLand ? _walkPhase : -1);
            if (!frame.IsEmpty)
            {
                _renderer.SetSprite(frame);
                _spriteReady = true;
                _lastIndices = null;
                _dirty = true;
            }
        }
        _spriteKey = key;
    }

    /// <summary>게임에서 읽은 팔레트 색인 그림을 색으로 풀어 올린다. 색인 0 은 비침이다.</summary>
    private void UploadGameSprite(byte[] indices)
    {
        // 같은 그림이면 아무것도 하지 않는다. 게임 함대를 따라가는 쪽은 방향을 우리가 모르므로
        // 그림 자체를 견줘야 안다(2304바이트뿐이라 프레임마다 견줘도 싸다).
        if (_lastIndices != null && _lastIndices.AsSpan().SequenceEqual(indices)) return;
        _lastIndices = [.. indices];
        _dirty = true;

        for (int i = 0; i < _spriteBuf.Length; i++)
        {
            int ix = indices[i];
            _spriteBuf[i] = ix == 0
                ? 0u
                : 0xFF000000u | (uint)((OceanPalette.Rgb[ix * 3] << 16)
                                     | (OceanPalette.Rgb[ix * 3 + 1] << 8)
                                     | OceanPalette.Rgb[ix * 3 + 2]);
        }
        _renderer.SetSprite(_spriteBuf);
        _spriteReady = true;
    }

    /// <summary>
    /// 커서 쪽으로 뱃머리를 돌리고, 틱마다 그 <b>뱃머리로</b> 한 걸음 나아간다.
    /// 커서는 바라는 쪽만 정한다 — 커서 자리에 도착해서 멈추는 것도, 창 밖으로 나갔다고
    /// 서는 것도 아니다. 한 번 뱃머리를 잡으면 막힐 때까지 그 쪽으로 간다.
    /// </summary>
    /// <remarks>
    /// 게임의 조타 그대로다(볼트 <c>86.분석-바다 조타(커서 방향·뱃머리·이동 벡터)</c>).
    /// <code>
    ///   커서 → 바라는 쪽   0x0048ECC2   기울기 1/2·2 로 가르는 8방위(늘 짝수)
    ///   한 틱 돌기·가기    0x0048D0A0   뱃머리를 1~3 눈금 돌리고 그 벡터로 나아간다
    ///   누산 += 벡터[h] * 이동값        (x 는 * 경도보정 / 100)   0x0048D23A
    /// </code>
    /// <b>이동 벡터를 따로 두면 안 된다.</b> 예전에는 커서 각을 16방위로 꺾어 그대로 이동
    /// 벡터로 삼았는데, 배 그림은 뱃머리를 둘로 접은 여덟 장뿐이라(<c>h &gt;&gt; 1</c>) 홀수
    /// 방위로 갈 때마다 선체가 <b>22.5도 틀어진 채</b> 갔다 — 배가 옆으로 미끄러져 보이던
    /// 것이 그것이다. 바라는 쪽을 짝수로만 내고 이동을 뱃머리 벡터로 되돌리면, 서 있을 때는
    /// 늘 그림과 맞고 도는 동안만 게임처럼 잠깐 어긋난다.
    ///
    /// 죽은 구역(<see cref="TurnDeadZoneCells"/>)은 우리 것이다 — 게임은 커서가 배 한가운데에
    /// 와도 동쪽으로 돌린다.
    /// </remarks>
    private void Sail(double dt)
    {
        if (Paused) { _tickAccum = 0; return; }

        // 커서가 창 밖으로 나가도 배는 가던 쪽으로 계속 간다. 커서는 바라는 쪽을 바꿀 때만 쓴다.
        // 자동항해 중에는 커서 대신 다음 마디가 같은 몫을 한다(_hasHeadingTarget).
        double dx = _targetX - _shipX, dy = _targetY - _shipY;
        if (_hasHeadingTarget && dx * dx + dy * dy > TurnDeadZoneCells * TurnDeadZoneCells)
        {
            _desired = (Sector8(dx, dy) + HeadingZeroOffset) & 0xF;
            _making = true;
        }

        _tickAccum += dt;
        while (_tickAccum >= TickSeconds)
        {
            _tickAccum -= TickSeconds;
            Ticks++;                       // 날 눈금은 서 있어도 쌓인다(0x0044AF90)
            Turn();

            // 닻을 내렸으면 뱃머리만 돌고 그 자리에 선다 — 게임도 돌기가 닻 검사보다 앞이라,
            // 서서 뱃머리를 맞춰 두었다가 닻을 올리면 곧바로 그 쪽으로 나아간다.
            if (_anchored || !_making) continue;

            var (step, driftX, driftY) = Push();
            var (vx, vy) = HeadingVector();

            // 가로는 경도 보정만큼 늘린다(0x0048D23A) — 위도가 높을수록 경도 한 칸이 짧아서,
            // 같은 걸음이라도 지도 위에서는 더 많은 칸을 지난다. 해류도 같은 값을 받는다.
            double lon = Engine.Sea.Sailing.LonScale(ShipLatLon.Lat);
            Step(vx * step * lon + driftX, vy * step + driftY);
            Steps++;
            if (AutoSailing) UpdateStuckGuard();
        }
    }

    /// <summary>
    /// 커서 쪽을 <b>8방위</b> 하나로. 값은 늘 짝수다.
    /// </summary>
    /// <remarks>
    /// 게임(<c>0x0048ECC2</c>)은 atan 표 없이 <c>dx-2dy · 2dx-dy · 2dx+dy · dx+2dy</c> 의
    /// 부호만 보고 가른다. 경계가 기울기 <c>1/2</c> 와 <c>2</c> 인 직선이라 칸 너비가 고르지
    /// 않다 — <b>동서남북 칸이 53도, 대각 칸이 37도</b>다. 여기서는 같은 경계를 크기 비교로
    /// 낸다.
    /// </remarks>
    /// <param name="dx">동쪽이 +.</param>
    /// <param name="dy">남쪽(화면 아래)이 +.</param>
    private static int Sector8(double dx, double dy)
    {
        double ax = Math.Abs(dx), ay = Math.Abs(dy);
        if (ay * 2 <= ax) return dx >= 0 ? 12 : 4;             // 동 · 서 (가운데면 게임처럼 동)
        if (ax * 2 <= ay) return dy > 0 ? 8 : 0;               // 남 · 북
        return dx > 0 ? (dy > 0 ? 10 : 14) : (dy > 0 ? 6 : 2); // 남동 · 북동 · 남서 · 북서
    }

    /// <summary>
    /// 한 틱 만큼 뱃머리를 바라는 쪽으로 돌린다. 게임 <c>0x0048D0A0</c> 의 앞머리다.
    /// </summary>
    /// <remarks>
    /// 배는 종류마다 한 틱에 <b>1~3 눈금</b>씩만 돈다(<c>0x00569FC0</c>, <see cref="TurnRateOf"/>).
    /// 말은 곧장 돈다. 게임 방위는 반시계가 +라, 가까운 쪽으로 돌되 정반대(여덟 눈금)면
    /// 시계로 돈다.
    /// </remarks>
    private void Turn()
    {
        if (_heading == _desired) return;
        if (_onLand) { _heading = _desired; return; }

        int rate = TurnRateOf?.Invoke() ?? Engine.Sea.Sailing.DefaultTurnRate;
        for (int i = 0; i < rate && _heading != _desired; i++)
        {
            int d = (_desired - _heading) & 0xF;
            _heading = (_heading + (d < 8 ? 1 : -1)) & 0xF;
        }
    }

    /// <summary>
    /// 지금 뱃머리의 단위 벡터. 배가 나아가는 쪽이 <b>이것뿐</b>이다.
    /// </summary>
    /// <remarks>
    /// 게임 방위 벡터표(<c>0x00569558</c>, 크기 64)를 그대로 쓴다 — 해류가 쓰는 표와 같은
    /// 것이라 둘이 어긋날 일이 없다. 바람표를 못 읽었으면 각으로 대신 짓는다.
    /// </remarks>
    private (double X, double Y) HeadingVector()
    {
        if (_wind != null)
        {
            var (vx, vy) = _wind.Vector(_heading);
            return (vx / (double)WindTable.VectorLength, vy / (double)WindTable.VectorLength);
        }

        // 게임 방위는 반시계다 — 번호를 시계 각으로 되돌려 쓴다.
        double a = ((16 - _heading) & 0xF) * (Math.PI * 2 / 16);
        return (Math.Sin(a), -Math.Cos(a));
    }

    /// <summary>
    /// 이 걸음에 나아갈 칸 수와, 해류가 옆으로 미는 만큼.
    /// </summary>
    /// <remarks>
    /// 게임 셈 그대로다(볼트 <c>30.분석-항해 속도(돛·바람·해류)</c>).
    /// <code>
    ///   속도  = 0x0048BCF0()                         ; 추진력 x (풍속+1) x 돛효율 / 100
    ///   부류 1 : 이동 = 9 * 속도 / 10        + 해류
    ///   그 밖  : 이동 = (3 * 속도 + 54) / 10 , 해류 없음
    /// </code>
    /// <b>부류는 칸 그림 번호가 가른다.</b> 게임 부류표(<c>0x004CD048</c>)에서 부류 1 인
    /// 그림은 <c>0x80</c> 하나뿐이고, 바다 칸은 <c>...00</c> 과 <c>...80</c> 이 지도에
    /// 반반 섞여 있다 — 그래서 한 칸 걸러 두 식을 오간다.
    /// </remarks>
    private (double Step, double DriftX, double DriftY) Push()
    {
        if (FleetSpeed == null || _wind == null)
            return (CellsPerTick, 0, 0);

        int cell = WindTable.CellOf((int)(_shipX * OceanTiles.TileW), (int)(_shipY * OceanTiles.TileW));
        if (cell < 0) return (CellsPerTick, 0, 0);

        int month = MonthOf?.Invoke() ?? DefaultMonth;
        var wind = HeldWind(cell, month);
        int speed = FleetSpeed(wind.Dir, wind.Speed, _heading, _onLand);

        LastSpeed = speed;
        LastWind = (wind.Dir, wind.Speed, (wind.Dir - _heading) & 0xF);

        bool fast = FastTile();
        double step = Engine.Sea.Sailing.CellsPerTick(speed, fast, _onLand);
        LastFast = fast;
        LastStep = step;
        LastFlow = (0, 0);
        if (!fast || _onLand) return (step, 0, 0);

        var flow = _wind.CurrentAt(cell);
        LastFlow = (flow.Dir, flow.Speed);
        if (flow.Speed <= 0) return (step, 0, 0);

        var (dx, dy) = _wind.Vector(flow.Dir);
        var (driftX, driftY) = Engine.Sea.Sailing.Drift(
            (dx, dy), flow.Speed, Engine.Sea.Sailing.LonScale(ShipLatLon.Lat));
        return (step, driftX, driftY);
    }

    /// <summary>항해지도의 색 — 안 밝힌 곳 · 바다 · 뭍(게임 색표 색인).</summary>
    /// <remarks>
    /// <c>0x00416AED</c> 가 <c>0x2E</c>(바다) · <c>0x18</c>(뭍) 을, <c>0x00416AFB</c> 가
    /// 안 밝힌 곳에 <c>0x1A</c> 를 넣는다. <c>0x1A</c> 는 크림빛(196,180,148)이라
    /// 펼쳐 놓은 양피지처럼 보인다.
    /// </remarks>
    private const byte ChartSea = 0x2E, ChartLand = 0x18, ChartUnknown = 0x1A;

    /// <summary>
    /// 항해지도 한 장을 BGRA 로 짓는다. 지도를 못 읽었으면 null.
    /// </summary>
    /// <remarks>
    /// 게임의 <c>0x00416A00</c> 그대로다. 점 하나가 칸 <b>4x4</b> 고, 그 열여섯 칸 가운데
    /// 뭍이 바다보다 많으면 뭍색이다(같으면 바다). <b>안 밝힌 점은 아예 안 본다</b> — 그 자리는
    /// 양피지로 남는다. 배·도시·발견물 같은 <b>표식은 하나도 안 찍는다</b>.
    /// </remarks>
    public uint[]? Chart(ExploredMap seen, out int width, out int height)
    {
        width = ExploredMap.Width;
        height = ExploredMap.Height;
        if (_world == null || _terrain == null) return null;

        int step = ExploredMap.CellsPerBlock;
        var argb = new uint[width * height];

        for (int by = 0; by < height; by++)
            for (int bx = 0; bx < width; bx++)
            {
                byte color = ChartUnknown;
                if (seen.Seen(bx, by))
                {
                    int water = 0, land = 0;
                    for (int y = 0; y < step; y++)
                        for (int x = 0; x < step; x++)
                        {
                            int off = RawAt(bx * step + x, by * step + y).Offset;
                            if (IsSeaClass(CellAt(off))) water++;
                            else land++;
                        }
                    color = land > water ? ChartLand : ChartSea;
                }

                argb[by * width + bx] = PaletteBgra(color);
            }

        return argb;
    }

    /// <summary>
    /// 지도 그림에서 바다로 치는 칸인지 — 부류(<c>0x00426710</c> = <c>0x004CD048[타일]</c>)가
    /// <b>0·1</b> 일 때만이다. 음수를 포함한 그 밖은 뭍이다.
    /// </summary>
    private bool IsSeaClass(int cell) => _terrain!.ClassOfCell(cell) is 0 or 1;

    /// <summary>게임 색표 색인 하나를 BGRA 한 점으로.</summary>
    private static uint PaletteBgra(byte index)
    {
        int k = index * 3;
        return 0xFF000000u
             | ((uint)GamePalette.Rgb[k] << 16)
             | ((uint)GamePalette.Rgb[k + 1] << 8)
             | GamePalette.Rgb[k + 2];
    }

    /// <summary>주변지도의 색 — 배 · 도시 · 발견물 · 뭍 · 바다 · 극지 밖(게임 색표 색인).</summary>
    /// <remarks>
    /// <c>0x00416CB4</c> 배 · <c>0x00416DE2</c> 도시 · <c>0x00416DE7</c> 발견물 ·
    /// <c>0x00416DDD</c> 뭍 · <c>0x00416DEC</c> 바다. 칸y 가 1250 밖인 줄은 <c>0x49</c>
    /// (24,20,12 거의 검정)로 채운다.
    /// </remarks>
    private const byte NearShip = 0x0A, NearCity = 0x24, NearFind = 0x38,
                       NearLand = 0x18, NearSea = 0x2E, NearPole = 0x49;

    /// <summary>주변지도가 한 점에 나아가는 거리 밑값(1/16 칸). 게임은 <c>측량 + 2</c> 다.</summary>
    public const int LocalStepBase = 2;

    /// <summary>WORLD.CDS 칸 낱말에서 <b>그림이 박힌 칸</b>(도시·발견물 그림 조각)을 뜻하는 비트.</summary>
    private const int PictureBit = 0x8000;

    /// <summary>발견물 칸 깃발 — 내가 찾았다 · 발표됐다.</summary>
    private const byte FindFound = 1, FindAnnounced = 2;

    /// <summary>
    /// 주변지도 한 장을 BGRA 로 짓는다. 지도를 못 읽었거나 배가 없으면 null.
    /// </summary>
    /// <remarks>
    /// 게임의 <c>0x00416B60(칸x, 칸y, 측량 + 2)</c> 다. 항해지도와 크기는 같은데
    /// (<b>625 x 313</b>) <b>배 둘레를 크게 본 것</b>이고, 밝힘과 상관없이 다 보인다.
    /// <code>
    ///   416ba6  왼쪽 끝 = 배칸x * 16 - 625 * r / 2       (자리는 1/16 칸)
    ///   416bcf  위  끝 = 배칸y * 16 - 313 * r / 2
    ///   416df4  한 점에 r 만큼 나아간다
    ///   줄마다  칸y = 위 &gt;&gt; 4 — -1 이면 윗줄을 옮겨 적고(0x4B7E47), 1250 밖이면 0x49
    ///   점마다  칸x = ((x &gt;&gt; 4) + 2500) % 2500, 앞 점과 칸이 같으면 앞 점 색 그대로
    /// </code>
    /// <c>r</c> 이 <c>측량 + 2</c> 라 측량 0 이면 한 점이 <b>1/8 칸</b>이다 — 칸 하나가 8x8
    /// 점으로 커지고, 화면에는 78 x 39 칸쯤이 담긴다. 측량이 오르면 한 점이 넓어져
    /// <b>더 멀리</b> 보인다.
    ///
    /// 도시·발견물은 <b>그림이 박힌 칸</b>(<see cref="PictureBit"/>)에서만 색이 바뀐다 —
    /// 둘레를 네모로 칠하는 것이 아니라 그림 조각 모양 그대로 밝아진다(<see cref="NearPaint"/>).
    /// </remarks>
    /// <param name="log">발견물. 없으면 발견물 점이 안 선다.</param>
    /// <param name="player">주인공. 찾은 것·발표한 것을 본다.</param>
    /// <param name="cityShown">지도에 뜨는 도시인지 — 알고(<c>+0x04 &amp; 1</c>) 서 있는
    /// (<c>!(+0x04 &amp; 4)</c>) 도시만 참이다. null 이면 모두 뜬다.</param>
    /// <param name="survey">측량술. <c>r = 측량 + 2</c> 가 된다.</param>
    public uint[]? LocalChart(DiscoveryLog? log, Player player, Func<int, bool>? cityShown,
                              int survey, out int width, out int height)
    {
        width = ExploredMap.Width;
        height = ExploredMap.Height;
        if (_world == null || _terrain == null || !_shipKnown) return null;

        int r = Math.Max(0, survey) + LocalStepBase;
        int shipX = Wrap((int)Math.Floor(_shipX)), shipY = (int)Math.Floor(_shipY);
        int left = shipX * 16 - width * r / 2;
        int top = shipY * 16 - height * r / 2;

        var cities = CityCells(cityShown);
        var finds = FindCells(log, player);
        uint pole = PaletteBgra(NearPole);

        var argb = new uint[width * height];
        for (int py = 0; py < height; py++)
        {
            int row = py * width;
            int cy = (top + py * r) >> 4;

            // 가장자리 한 줄은 윗줄을 그대로 옮겨 적는다(0x4B7E47).
            if (cy == -1 && py > 0)
            {
                Array.Copy(argb, row - width, argb, row, width);
                continue;
            }
            if (cy < 0 || cy >= WorldMapRenderer.CellH)
            {
                Array.Fill(argb, pole, row, width);
                continue;
            }

            int lastX = int.MinValue;
            uint last = 0;
            for (int px = 0; px < width; px++)
            {
                int cx = Wrap((left + px * r) >> 4);
                if (cx != lastX)
                {
                    lastX = cx;
                    last = PaletteBgra(NearPaint(cx, cy, shipX, shipY, cities, finds));
                }
                argb[row + px] = last;
            }
        }
        return argb;
    }

    /// <summary>
    /// 주변지도에서 그 칸이 무슨 색인지 — <c>0x00416C6E</c>.
    /// </summary>
    /// <remarks>
    /// <code>
    ///   칸 == 배칸                                → 0x0A (깜박이지 않는다)
    ///   w &amp; 0x8000 이면 dy·dx 0..2 로 (칸x-dx, 칸y-dy) 를 본다
    ///     그 칸이 도시 칸(0x4255A0)이고 반지름(+0x0C) &gt; dx, &gt; dy, 알고 서 있으면 → 0x24
    ///     그 칸이 발견물 칸(0x425640)이고 지도 그림이 있고(0x4AAE90 != FFFF)
    ///       (dx&lt;2 &amp;&amp; dy&lt;2 &amp;&amp; 내가 찾음) 또는 발표(+0x16 &amp; 0x80) 이면     → 0x38
    ///   부류 0·1 → 0x2E 바다, 그 밖 → 0x18 뭍
    /// </code>
    /// 힌트로 열린 못 찾은 발견물은 <b>안 선다</b> — 힌트 깃발 <c>0x08</c> 은 안 본다.
    /// </remarks>
    private byte NearPaint(int cx, int cy, int shipX, int shipY,
                           Dictionary<(int X, int Y), int> cities,
                           Dictionary<(int X, int Y), byte> finds)
    {
        if (cx == shipX && cy == shipY) return NearShip;

        int w = CellAt(RawAt(cx, cy).Offset);
        if ((w & PictureBit) != 0)
        {
            for (int dy = 0; dy < 3; dy++)
            {
                int y = cy - dy;
                if (y < 0) continue;
                for (int dx = 0; dx < 3; dx++)
                {
                    var at = (Wrap(cx - dx), y);
                    if (cities.TryGetValue(at, out int reach) && reach > dx && reach > dy)
                        return NearCity;
                    if (finds.TryGetValue(at, out byte f)
                        && ((dx < 2 && dy < 2 && (f & FindFound) != 0) || (f & FindAnnounced) != 0))
                        return NearFind;
                }
            }
        }
        return IsSeaClass(w) ? NearSea : NearLand;
    }

    /// <summary>지도에 뜨는 도시가 앉은 칸과 그 반지름(<c>+0x0C</c>).</summary>
    private Dictionary<(int X, int Y), int> CityCells(Func<int, bool>? cityShown)
    {
        var cells = new Dictionary<(int X, int Y), int>();
        if (_cities is not { } table) return cells;

        for (int id = 0; id < CityExeTable.Count; id++)
        {
            if (cityShown != null && !cityShown(id)) continue;
            if (!table.TryCell(id, out int x, out int y, out int reach)) continue;
            cells[(Wrap(x), y)] = reach;
        }
        return cells;
    }

    /// <summary>
    /// 주변지도에 설 수 있는 발견물 — 지도 그림이 있고(<c>+0x54</c> 첫 칸이 FFFF 가 아님)
    /// 내가 찾았거나 발표된 것. 사각형 왼쪽 위 칸에 깃발을 단다.
    /// </summary>
    private static Dictionary<(int X, int Y), byte> FindCells(DiscoveryLog? log, Player player)
    {
        var cells = new Dictionary<(int X, int Y), byte>();
        if (log == null) return cells;

        foreach (var row in log.Table.Discoveries)
        {
            if (!row.HasPlace || row.Erase is not { Length: > 0 } block) continue;
            if (block[0] == DiscoveryTable.Keep) continue;

            byte flag = 0;
            if (player.HasFound(row.Id)) flag |= FindFound;
            if (player.HasAnnounced(row.Id)) flag |= FindAnnounced;
            if (flag == 0) continue;

            var at = (Wrap(row.X1), row.Y1);
            cells[at] = (byte)(cells.GetValueOrDefault(at) | flag);
        }
        return cells;
    }

    /// <summary>가로로 이어진 지도를 접는다.</summary>
    private static int Wrap(int cellX)
    {
        int w = WorldMapRenderer.UnfoldedW;
        cellX %= w;
        return cellX < 0 ? cellX + w : cellX;
    }

    /// <summary>발밑이 빠른 부류(그림 번호 <c>0x80</c>)인지.</summary>
    private bool FastTile()
    {
        if (_world == null) return false;
        var (_, _, _, _, off) = RawAt(_shipX, _shipY);
        return (WorldMapRenderer.CellToTile(_world, off) & 0x80) != 0;
    }

    /// <summary>
    /// 한 걸음 옮긴다. 육지에 걸리면 가로·세로를 따로 밀어 본다 — 해안을 따라 미끄러지듯
    /// 나아가게 하려는 것이다. 둘 다 막히면 그 자리에 선다.
    /// </summary>
    private void Step(double dx, double dy)
    {
        if (CanGo(_shipX + dx, _shipY + dy)) { Move(dx, dy); _blocked = false; return; }
        if (dx != 0 && CanGo(_shipX + dx, _shipY)) { Move(dx, 0); _blocked = true; return; }
        if (dy != 0 && CanGo(_shipX, _shipY + dy)) { Move(0, dy); _blocked = true; return; }
        _blocked = true;
    }

    /// <summary>
    /// 말의 걸음 번호(0~7). 한 걸음 뗄 때마다 늘어 다리가 움직인다.
    /// </summary>
    /// <remarks>
    /// 게임도 이렇게 한다 — 지도 한 틱마다 <c>0x00569550</c> 이 늘고, 그림 번호가
    /// <c>(방향 &gt;&gt; 2) * 8 + 걸음</c> 이다. 우리 틱은 0.1초라 여덟 걸음이 0.8초에 돈다.
    /// 멈춰 있으면 늘지 않으므로 선 말은 다리도 선다.
    /// </remarks>
    private int _walkPhase;

    private void Move(double dx, double dy)
    {
        if (_onLand && (dx != 0 || dy != 0))
            _walkPhase = (_walkPhase + 1) % ShipSprites.WalkPhases;

        _shipX += dx;
        _shipY += dy;
        // 지도 밖으로는 못 나간다. 가로는 이어져 있으므로 나머지로 접는다.
        // 접힌 바퀴 수를 세어 알린다 — 세계일주 장면이 이 값으로 「며칠 어긋났다」를 센다
        // (0x0047D11B: 경도가 0 밑이면 +40000 하며 −1, 40000 을 넘으면 −40000 하며 +1).
        double laps = Math.Floor(_shipX / WorldMapRenderer.UnfoldedW);
        _shipX -= laps * WorldMapRenderer.UnfoldedW;
        if (laps != 0) Lapped?.Invoke((int)laps);
        _shipY = Math.Clamp(_shipY, 0, WorldMapRenderer.CellH - 1);
    }

    /// <summary>
    /// 게임 지형표. 칸을 지날 수 있는지는 이것이 가른다.
    /// </summary>
    private TerrainTable? _terrain;

    /// <summary>지금 모드에서 지날 수 있는 칸인지. 배는 물을, 말은 뭍을 간다.</summary>
    /// <remarks>
    /// 게임이 쓰는 표를 그대로 본다(<see cref="TerrainTable"/>) — 칸의 <b>첫 바이트</b>로
    /// 부류를 찾고, 0·1 이면 물, 2 이상이면 뭍이다.
    ///
    /// 예전에는 그림을 그리려고 재어 둔 육지 비율이 반을 넘으면 막았다. 그것은 색을 섞는
    /// 비율이지 통행 규칙이 아니어서, 런던 앞 하구처럼 육지가 50~55% 인 칸이 막혀
    /// 게임에서는 들어가지는 데를 못 들어갔다. 표를 못 읽을 때만 그 어림으로 물러선다.
    /// </remarks>
    private bool CanGo(double cellX, double cellY)
    {
        if (_terrain != null && _world != null)
        {
            int cell = CellValue(cellX, cellY);
            return _onLand ? _terrain.CanWalk(cell) : _terrain.CanSail(cell);
        }

        double land = LandRatioAt(cellX, cellY);
        return _onLand ? land >= WalkMinLandRatio : land < SailMaxLandRatio;
    }

    /// <summary>
    /// 그 자리에서 가장 가까운 물칸. 한 칸씩 넓혀 가며 테두리만 훑는다.
    /// 못 찾으면(있을 수 없지만) 준 자리를 그대로 돌려준다.
    /// </summary>
    private (double X, double Y) NearestWater(double cellX, double cellY)
    {
        if (!IsLand(cellX, cellY)) return (cellX, cellY);
        for (int r = 1; r <= 64; r++)
        {
            for (int dy = -r; dy <= r; dy++)
            {
                for (int dx = -r; dx <= r; dx++)
                {
                    if (Math.Abs(dx) != r && Math.Abs(dy) != r) continue;   // 테두리만
                    double x = cellX + dx, y = cellY + dy;
                    if (!IsLand(x, y)) return (x, y);
                }
            }
        }
        return (cellX, cellY);
    }

    /// <summary>
    /// 그 칸이 배가 못 가는 땅인지. WORLD.CDS 의 지형값을 그대로 본다 —
    /// 0 이 바다, 1 이 육지, 그 밖은 바다와 육지가 섞인 해안 칸이다.
    /// </summary>
    private bool IsLand(double cellX, double cellY)
    {
        if (_terrain != null && _world != null)
            return !_terrain.CanSail(CellValue(cellX, cellY));
        return LandRatioAt(cellX, cellY) >= SailMaxLandRatio;
    }

    /// <summary>그 칸의 육지 비율(0 이면 온통 바다, 1 이면 온통 육지). 지도 밖은 육지로 본다.</summary>
    private double LandRatioAt(double cellX, double cellY)
    {
        if (_world == null) return 0;
        if (cellY < 0 || cellY >= WorldMapRenderer.CellH) return 1;

        int off = RawAt(cellX, cellY).Offset;
        byte terrain = (byte)(_world[off] & 0x7F);
        if (terrain == 0) return 0;   // 바다
        if (terrain == 1) return 1;   // 육지
        return WorldMapRenderer.GetCoastLandRatio(terrain);   // 해안 칸
    }

    /// <summary>
    /// 칸 좌표가 WORLD.CDS 파일 안에서 놓인 자리. 파일은 2500바이트 행이 2500줄이고,
    /// 짝수 행이 지도의 왼쪽 절반, 홀수 행이 오른쪽 절반이다(한 칸 2바이트).
    /// </summary>
    /// <summary>
    /// 그 칸의 값 — <b>두 바이트</b>다(아래가 지형, 위가 속성).
    /// </summary>
    /// <remarks>
    /// 지형 표는 이 값 열네 비트로 찾는다(<see cref="Engine.Table.TerrainTable"/>). 아래
    /// 한 바이트만으로 찾으면 같은 지형이라도 속성이 다른 칸을 못 지나는 것으로 본다.
    /// </remarks>
    private int CellValue(double cellX, double cellY) => CellAt(RawAt(cellX, cellY).Offset);

    /// <summary>WORLD.CDS 의 그 자리에 적힌 칸 값.</summary>
    private int CellAt(int offset) =>
        _world == null || offset + 1 >= _world.Length
            ? 0
            : _world[offset] | (_world[offset + 1] << 8);

    private static (int CellX, int CellY, int Row, int Col, int Offset) RawAt(double cellX, double cellY)
    {
        int cx = (int)Math.Floor(cellX);
        int cy = (int)Math.Floor(cellY);
        cx -= (int)Math.Floor(cx / (double)WorldMapRenderer.UnfoldedW) * WorldMapRenderer.UnfoldedW;
        cy = Math.Clamp(cy, 0, WorldMapRenderer.CellH - 1);

        bool right = cx >= WorldMapRenderer.CellW;
        int col = right ? cx - WorldMapRenderer.CellW : cx;
        int row = cy * 2 + (right ? 1 : 0);
        return (cx, cy, row, col, row * WorldMapRenderer.RawStride + col * 2);
    }

    /// <summary>한 칸이 WORLD.CDS 안에서 어디에 어떻게 적혀 있는지. 좌표 겹쳐 보기에 쓴다.</summary>
    public readonly record struct CellInfo(
        double X, double Y, int CellX, int CellY,
        int Row, int Col, int Offset, byte Terrain, byte Attr, int Tile, double LandRatio);

    /// <summary>지금 배가 선 칸.</summary>
    /// <summary>
    /// 날짜변경선을 넘을 때 부른다 — 동으로 넘으면 +1, 서로 넘으면 −1 이다(<c>0x0047D11B</c>).
    /// </summary>
    public Action<int>? Lapped;

    public CellInfo? ShipCell => _shipKnown ? Describe(_shipX, _shipY) : null;

    /// <summary>
    /// 지금 선 칸의 <b>지형 부류</b>(0~6). 모르면 -1.
    /// </summary>
    /// <remarks>
    /// 게임도 뭍 사건을 이 값으로 가른다 — <c>0x00426740</c> 이 자리를 열여섯으로 나눠
    /// 칸을 잡고, 그 칸의 그림 번호(하위 열넉 비트)로 부류표(<c>0x004CD048</c>)를 찾는다.
    /// 짐승과 회오리는 <b>2</b>, 독충은 <b>6</b> 에서만 난다.
    /// </remarks>
    public int TerrainClass =>
        _terrain != null && ShipCell is { } cell
            ? _terrain.ClassOfCell(CellValue(cell.CellX, cell.CellY))
            : -1;

    /// <summary>커서가 가리키는 칸. 커서가 창 밖이면 null.</summary>
    public CellInfo? MouseCell => _mouseInside && _ready
        ? Describe(_lastOrigin.X + _mouse.X * _lastDpiX * _cellsPerPixel,
                   _lastOrigin.Y + _mouse.Y * _lastDpiY * _cellsPerPixel)
        : null;

    /// <summary>그 자리의 칸 정보를 모아 준다. 지도를 아직 안 읽었으면 값이 0 이다.</summary>
    private CellInfo? Describe(double cellX, double cellY)
    {
        if (_world == null) return null;
        var (cx, cy, row, col, off) = RawAt(cellX, cellY);
        byte terrain = (byte)(_world[off] & 0x7F);
        return new CellInfo(cellX, cellY, cx, cy, row, col, off,
                            terrain, _world[off + 1],
                            WorldMapRenderer.CellToTile(_world, off),
                            LandRatioAt(cellX, cellY));
    }

    /// <summary>
    /// 닻을 내리거나 올린다. 내리면 배가 그 자리에서 즉시 서고, 다시 올리면 가던 쪽으로 간다.
    /// 도시에 들어가 있는 동안은 받지 않는다(<see cref="SeaBlocked"/>).
    /// </summary>
    /// <remarks>
    /// <b>뭍에서도 받는다.</b> 말에게 내릴 닻은 없지만 서고 가는 것은 같아야 해서,
    /// 같은 스위치를 쓰고 그림과 문구만 갈라 낸다 — 뭍에서는 닻 그림을 얹지 않고
    /// (그 자리에는 대 둔 배가 있다) 상태 줄도 "멈춰 서 있다" 로 나온다.
    /// </remarks>
    /// <returns>이제 서 있으면 true.</returns>
    public bool ToggleAnchor()
    {
        if (SeaBlocked) return false;
        _anchored = !_anchored;
        _tickAccum = 0;
        return _anchored;
    }

    /// <summary>
    /// 커서 자리를 알려 준다. 배는 이 쪽으로 나아간다. 도시에 들어가 있는 동안은
    /// 커서가 지도 위에 있어도 없는 셈 친다 — 도시 화면 옆을 스쳐도 뱃머리가 돌지 않게.
    /// </summary>
    public void SetMouse(Point p, bool inside)
    {
        _mouse = p;
        _mouseInside = inside && !SeaBlocked;
    }

    /// <summary>그 배율로 배를 가운데 두고 본다.</summary>
    private void LookAt(double cellsPerPixel)
    {
        _cellsPerPixel = cellsPerPixel;
        if (!_shipKnown) return;
        _centerX = _shipX;
        _centerY = _shipY;
    }

    /// <summary>휠 확대. 커서 밑 지점이 제자리에 남도록 한다.</summary>
    public void Zoom(int dir, Point cursor)
    {
        double f = dir > 0 ? 1 / 1.25 : 1.25;
        double next = Math.Clamp(_cellsPerPixel * f, 1.0 / 64, 4.0);
        if (Math.Abs(next - _cellsPerPixel) < 1e-9) return;

        // 커서가 가리키던 칸을 구해 두고, 배율을 바꾼 뒤 그 칸이 같은 자리에 오도록 가운데를 민다.
        double dx = cursor.X - ActualWidth / 2, dy = cursor.Y - ActualHeight / 2;
        double atX = _centerX + dx * _cellsPerPixel, atY = _centerY + dy * _cellsPerPixel;
        _cellsPerPixel = next;
        _centerX = atX - dx * _cellsPerPixel;
        _centerY = atY - dy * _cellsPerPixel;
    }

    public void BeginDrag(Point p) { _dragging = true; _dragStart = p; _dragCx = _centerX; _dragCy = _centerY; }
    public void EndDrag() => _dragging = false;

    public void Drag(Point p)
    {
        if (!_dragging) return;
        _follow = false;   // 손으로 끌면 따라다니기를 놓는다
        _centerX = _dragCx - (p.X - _dragStart.X) * _cellsPerPixel;
        _centerY = _dragCy - (p.Y - _dragStart.Y) * _cellsPerPixel;
    }

    /// <summary>
    /// 배를 그 화면 자리로 옮긴다. 뭍이면 가장 가까운 물칸으로 밀어 넣는다.
    /// 시작 자리를 손으로 잡을 때 쓴다.
    /// </summary>
    public void PlaceShipAt(Point p)
    {
        if (!_ready || SeaBlocked) return;
        StopAutoSail();   // 다른 자리로 옮기면 짜 둔 항로가 더는 안 맞는다
        double cx = _lastOrigin.X + p.X * _lastDpiX * _cellsPerPixel;
        double cy = _lastOrigin.Y + p.Y * _lastDpiY * _cellsPerPixel;
        cy = Math.Clamp(cy, 0, WorldMapRenderer.CellH - 1);
        (cx, cy) = NearestWater(cx, cy);

        _shipX = _targetX = cx;
        _shipY = _targetY = cy;
        _shipKnown = true;
        _blocked = false;
        _anchored = false;
        _tickAccum = 0;
        if (_follow) { _centerX = cx; _centerY = cy; }
    }

    /// <summary>시작 칸. 혹시 뭍이면(WORLD.CDS 가 다르면) 가장 가까운 물칸으로 밀어 낸다.</summary>
    private (double X, double Y) LisbonStart() => NearestWater(StartCellX, StartCellY);

    /// <summary>
    /// 배를 그 도시 앞바다에 갖다 놓는다(불러오기에 쓴다). 도시 번호가 표에 없으면 false.
    /// </summary>
    public bool PlaceAtCity(int cityId)
    {
        if (!_ready) return false;
        if (!GameMapCoords.TryCityCell(cityId, out double cx, out double cy)) return false;

        StopAutoSail();   // 다른 자리로 옮기면 짜 둔 항로가 더는 안 맞는다
        (cx, cy) = NearestWater(cx, cy);   // 도시 칸은 뭍이라 앞바다로 밀어 낸다
        _shipX = _targetX = cx;
        _shipY = _targetY = cy;
        _shipKnown = true;
        _blocked = false;
        _anchored = false;
        _onLand = false;
        _tickAccum = 0;
        _making = false;                   // 세워 둔다 — 커서가 다시 쪽을 줄 때까지
        _desired = _heading;
        _centerX = cx;
        _centerY = cy;
        _follow = true;
        _dirty = true;
        return true;
    }

    /// <summary>
    /// 바다에서 적을 배 자리. 뭍에 올라 있으면 대 둔 배 자리다. 자리를 모르면 null.
    /// </summary>
    public (double X, double Y)? SeaSpot =>
        !_shipKnown ? null : _onLand ? (_moored ? (_mooredX, _mooredY) : null) : (_shipX, _shipY);

    /// <summary>
    /// 배를 그 칸에 갖다 놓는다(바다에서 적은 판을 불러올 때). 뭍이면 가까운 물칸으로 민다.
    /// </summary>
    public bool PlaceAtSea(double x, double y)
    {
        if (!_ready) return false;
        StopAutoSail();   // 다른 자리로 옮기면 짜 둔 항로가 더는 안 맞는다
        (x, y) = NearestWater(x, Math.Clamp(y, 0, WorldMapRenderer.CellH - 1));
        _shipX = _targetX = x;
        _shipY = _targetY = y;
        _shipKnown = true;
        _blocked = false;
        _anchored = true;                  // 닻을 내린 채로 연다 — 곧바로 흘러가지 않게
        SteerArmed = false;                // 커서 조타도 잠든다(0x0048EB32 언저리)
        _onLand = false;
        _moored = false;
        _tickAccum = 0;
        _making = false;
        _desired = _heading;
        _centerX = x;
        _centerY = y;
        _follow = true;
        _dirty = true;
        return true;
    }

    /// <summary>배를 리스본 앞바다로 되돌린다.</summary>
    public void ResetToLisbon()
    {
        StopAutoSail();   // 다른 자리로 옮기면 짜 둔 항로가 더는 안 맞는다
        var (lx, ly) = LisbonStart();
        _shipX = _targetX = lx;
        _shipY = _targetY = ly;
        _shipKnown = true;
        _blocked = false;
        _anchored = false;
        _tickAccum = 0;
        _centerX = lx;
        _centerY = ly;
        _follow = true;
    }

    /// <summary>
    /// 배 둘레 <paramref name="radiusCells"/> 칸 안에 뭍이 있는지. 상륙할 수 있는 자리인지 볼 때 쓴다.
    /// </summary>
    /// <summary>
    /// 상륙할 수 있는 자리인지 — 배 칸 둘레 <b>3x3</b> 에 <b>부류 2(육지)</b> 칸이 있어야 한다
    /// (<c>0x0048B248</c>~<c>0x0048B25F</c> 의 <c>dx·dy −1..1</c> 과 <c>cmp eax, 2</c>).
    /// 산(3)·사막(4)·숲(6)에는 못 내린다.
    /// </summary>
    public bool IsNearLand()
    {
        if (!_shipKnown || _terrain == null) return false;
        for (int dy = -1; dy <= 1; dy++)
            for (int dx = -1; dx <= 1; dx++)
                if (_terrain.ClassOfCell(CellValue(_shipX + dx, _shipY + dy)) == PlainLandClass)
                    return true;
        return false;
    }

    /// <summary>내릴 수 있는 뭍의 지형 부류(<c>0x0048B25C</c> 의 <c>cmp eax, 2</c>).</summary>
    private const int PlainLandClass = 2;

    /// <summary>
    /// 커서 칸으로 가는 바닷길의 <b>첫 길목</b>. 길을 못 찾거나 이미 그 자리면 null 이다.
    /// 같은 칸을 두 번 셈하지 않게 마지막 결과를 쥐고 있는다.
    /// </summary>
    private (double X, double Y)? NextWaypoint(double toX, double toY)
    {
        if (_world == null || _terrain == null) return null;

        var want = ((int)Math.Floor(toX), (int)Math.Floor(toY));
        if (want == _assistFrom) return _assistStep;

        _assistFrom = want;
        _assistStep = null;

        var route = Engine.Sea.SeaPathfinder.FindRoute(_world, _terrain, (_shipX, _shipY), (toX, toY));
        if (route is { Count: > 1 }) _assistStep = (route[1].X + 0.5, route[1].Y + 0.5);
        return _assistStep;
    }

    /// <summary>둘레 3x3 에서 가장 가까운 부류 2 칸. 없으면 null.</summary>
    private (double X, double Y)? NearestPlainLand()
    {
        if (_terrain == null) return null;
        (double X, double Y)? best = null;
        double near = double.MaxValue;
        for (int dy = -1; dy <= 1; dy++)
            for (int dx = -1; dx <= 1; dx++)
            {
                double x = Math.Floor(_shipX) + dx + 0.5, y = Math.Floor(_shipY) + dy + 0.5;
                if (_terrain.ClassOfCell(CellValue(x, y)) != PlainLandClass) continue;
                double far = (x - _shipX) * (x - _shipX) + (y - _shipY) * (y - _shipY);
                if (far >= near) continue;
                near = far;
                best = (x, y);
            }
        return best;
    }

    /// <summary>배 둘레에 물이 있는지. 뭍에서 출항할 수 있는 자리인지 볼 때 쓴다.</summary>
    public bool IsNearWater(int radiusCells = 2) => IsNear(false, radiusCells);

    private bool IsNear(bool land, int radiusCells)
    {
        if (!_shipKnown) return false;
        for (int dy = -radiusCells; dy <= radiusCells; dy++)
            for (int dx = -radiusCells; dx <= radiusCells; dx++)
                if (IsLand(_shipX + dx, _shipY + dy) == land) return true;
        return false;
    }

    /// <summary>
    /// 대 둔 배 곁인지 — 해상 커맨드의 「승선」은 상륙한 자리(<c>0x005B63B8</c>·<c>0x005B63BC</c>)에서
    /// 가로세로 두 칸(1/16 눈금 0x20) 안일 때만 선다(<c>0x0048B397</c>). 경도는 한 바퀴 돌아 잰다.
    /// </summary>
    public bool IsNearMoor(int radiusCells = 2)
    {
        if (!_onLand || !_moored) return false;
        double dx = _shipX - _mooredX, w = WorldMapRenderer.CellW;
        if (dx < -w / 2) dx += w;
        else if (dx > w / 2) dx -= w;
        return Math.Abs(dx) <= radiusCells && Math.Abs(_shipY - _mooredY) <= radiusCells;
    }

    /// <summary>
    /// 상륙. 가장 가까운 뭍으로 한 칸 올라서고 말로 바뀐다. 지날 수 있는 칸도 뒤집힌다.
    /// </summary>
    public bool Land()
    {
        if (SeaBlocked || _onLand) return false;
        // 내리는 자리는 <b>둘레 3x3 의 부류 2 칸</b>이다 — 원본은 비트 0x4000 이 선 칸들을
        // 밝혀 주고 숫자판 1~9 로 고르게 하는데(0x0048B840 · 0x0048B95F), 우리는 고르개 없이
        // 가장 가까운 것을 잡는다. 그런 칸이 없으면 예전처럼 가까운 뭍을 찾는다.
        var spot = NearestPlainLand() ?? NearestCell(_shipX, _shipY, wantLand: true, maxRing: 3);
        if (spot == null) return false;

        StopAutoSail();   // 뭍에 오르면 자동항해는 뜻이 없다

        // 배는 지금 자리에 대 둔다 — 뭍에 있는 동안 그 자리에 남아 어디로 상륙했는지 보인다.
        // 게임도 자리를 적어 두었다가 출항할 때 그대로 되돌린다(0x004936DE).
        _mooredX = _shipX;
        _mooredY = _shipY;
        _mooredHeading = _heading;
        _moored = true;

        (_shipX, _shipY) = spot.Value;
        _targetX = _shipX;
        _targetY = _shipY;
        _onLand = true;
        _blocked = false;
        _anchored = false;
        _tickAccum = 0;
        if (_follow) { _centerX = _shipX; _centerY = _shipY; }
        return true;
    }

    /// <summary>출항. 가장 가까운 물칸으로 내려가 배로 돌아간다.</summary>
    public bool Embark()
    {
        if (SeaBlocked || !_onLand) return false;
        ShiftWind();   // 배에 오르면 바람을 다시 읽는다(0x0048EB94)

        // 대 둔 자리로 돌아간다 — 게임도 상륙할 때 적어 둔 자리를 그대로 되돌린다
        // (0x004936DE). 그 자리가 물이 아니게 됐으면(있을 수 없지만) 가까운 물칸으로 간다.
        var spot = _moored && !IsLand(_mooredX, _mooredY)
            ? (_mooredX, _mooredY)
            : NearestCell(_shipX, _shipY, wantLand: false, maxRing: 3);
        if (spot == null) return false;

        _moored = false;
        (_shipX, _shipY) = spot.Value;
        _targetX = _shipX;
        _targetY = _shipY;
        _onLand = false;
        _blocked = false;
        // 배에 오르면 <b>닻을 내린 채</b>고 커서 조타도 잠든다 — 왼쪽 클릭으로 출발한다
        // (0x0048B601 이 0x005B3A00 에 1, 0x0048B5F6 이 +0x104 에 0).
        _anchored = true;
        SteerArmed = false;
        _tickAccum = 0;
        if (_follow) { _centerX = _shipX; _centerY = _shipY; }
        return true;
    }

    /// <summary>둘레를 한 칸씩 넓혀 가며 원하는 쪽(뭍/물) 칸을 찾는다. 없으면 null.</summary>
    private (double X, double Y)? NearestCell(double cx, double cy, bool wantLand, int maxRing)
    {
        if (IsLand(cx, cy) == wantLand) return (cx, cy);
        for (int r = 1; r <= maxRing; r++)
            for (int dy = -r; dy <= r; dy++)
                for (int dx = -r; dx <= r; dx++)
                {
                    if (Math.Abs(dx) != r && Math.Abs(dy) != r) continue;
                    double x = cx + dx, y = cy + dy;
                    if (y < 0 || y >= WorldMapRenderer.CellH) continue;
                    if (IsLand(x, y) == wantLand) return (x, y);
                }
        return null;
    }

    /// <summary>도시 중심이 이보다 멀면 항구 칸을 구해 볼 것도 없다. 거르는 데만 쓴다.</summary>
    private const double HarborSearchCells = 8;

    /// <summary>도시 ID -> 항구 칸(도시에서 가장 가까운 물칸). 한 번 구하면 들고 있는다.</summary>
    private readonly Dictionary<int, (double X, double Y)> _harbors = [];

    /// <summary>
    /// 배가 닿은 도시 ID. 없으면 -1.
    /// </summary>
    /// <remarks>
    /// <b>뭍과 같은 규칙이다.</b> 게임도 바다와 뭍을 <see cref="CityAt"/> 한 자리에서 가리고
    /// (<c>0x0048DA19</c>), <c>[0x005B61B4]</c> 로는 어느 표시를 볼지만 가른다 — 바다면
    /// 항구가 있는가(<c>+0x1C &amp; 1</c>), 뭍이면 성문이 있는가(<c>+0x1D &amp; 4</c>).
    /// 그 두 표시는 아직 안 든다.
    ///
    /// 예전에는 도시 앞 물칸을 따로 구해 거기서 두 칸 안에 들어야 물었다. 그 물칸은
    /// 도시 <b>중심</b>에서 가장 가까운 물을 찾은 것이라 도시 그림과 어긋나기 일쑤였고,
    /// 그래서 그림에 배를 바짝 붙여도 안 묻는 자리가 있었다.
    /// </remarks>
    public int NearestCity() => CityAt();

    /// <summary>도시 표. 도시가 앉은 칸과 차지하는 칸 수를 여기서 얻는다.</summary>
    private CityExeTable? _cities;

    /// <summary>
    /// 도시 칸에서 <b>왼쪽·위로</b> 이만큼 더 나가도 닿은 것으로 친다(<c>0x0048DA29</c> 의
    /// <c>dx &lt;= 1</c>).
    /// </summary>
    private const int TownSlack = 1;

    /// <summary>
    /// 배·말 그림이 제 칸에서 사방으로 뻗는 칸 수. 그림이 세 칸 폭이고 <b>칸 가운데에</b>
    /// 놓이므로 한 칸이다(<see cref="SpriteRectAt"/> 의 <c>size / 2</c>).
    /// </summary>
    /// <remarks>
    /// 게임은 함대 자리를 그림 왼쪽 위께로 잡아 훑는 칸이 <c>-3 .. +1</c> 로 한쪽에
    /// 쏠려 있다. 우리는 가운데에 놓으므로 그만큼을 양쪽으로 갈라 얹는다 — 그래야
    /// <b>그림끼리 닿는 순간</b> 물어본다. 안 얹으면 도시 그림 한복판까지 밀고
    /// 들어가야 물었다.
    /// </remarks>
    private const int SpriteHalfCells = 1;

    /// <summary>
    /// 도시에 <b>닿았다</b>고 칠 때 얹는 여유 칸.
    /// </summary>
    /// <remarks>
    /// 예전에는 <see cref="SpriteHalfCells"/>(1) 을 얹었다 — 그림끼리 스치기만 해도
    /// 물어보라는 뜻이었는데, 화면에서 보면 <b>아직 한참 떨어져 있는데</b> 창이 떴다.
    /// 게임 셈(<c>0x0048DA29</c> 의 <c>dx &lt;= 1</c>)에는 그런 여유가 없으므로 0 으로 둔다 —
    /// 말이 도시 칸에 실제로 들어서야 묻는다.
    /// </remarks>
    private const int TouchSlack = 0;

    /// <summary>
    /// 말이 닿은 도시 ID. 없으면 -1.
    /// </summary>
    /// <remarks>
    /// 게임 그대로다(<c>0x0048DA19</c>). 거리를 재는 것이 아니라 <b>둘레 칸을 훑는다</b>.
    /// <code>
    ///   0048DA21  dy = -3 .. +1
    ///   0048DA34  칸y = [0x5B63B4] &gt;&gt; 4          ; 원본값 열여섯이 한 칸
    ///   0048DA5A  dx = -3 .. +1
    ///   0048DA7E  그 칸(칸x+dx, 칸y+dy)에 도시가 있나
    ///   0048DA9C  dx &lt; 0 이면 도시가 차지하는 칸 수 &gt;= -dx 여야 한다
    ///   0048DAAF  dy 도 같다
    /// </code>
    /// 곧 <b>도시 칸에서 오른쪽·아래로 차지하는 칸 수만큼, 왼쪽·위로 한 칸</b>이 그 도시의
    /// 어귀다. 차지하는 칸 수는 표의 <c>+0x0C</c> 로 2 아니면 3 이다(<see cref="CityExeTable.TryCell"/>).
    ///
    /// 예전에는 도시 <b>가운데</b>에서 두 칸 안으로 들어야 물었다. 도시 그림이 서너 칸을
    /// 차지하는데 가운데만 재니, 그림 어귀에 닿아도 안 물어보고 그림 한복판까지 밀고
    /// 들어가야 했다.
    /// </remarks>
    public int NearestTown() => CityAt();

    /// <summary>
    /// 지금 선 자리가 어느 도시의 어귀인지. 없으면 -1. 바다와 뭍이 같이 쓴다.
    /// </summary>
    /// <summary>
    /// 그 도시가 지금 지도에 있는지 물어보는 손. 안 걸면 다 있는 것으로 본다.
    /// </summary>
    /// <remarks>
    /// 신대륙 식민 도시 스물셋은 해가 가야 하나씩 선다(<c>CityFounding</c>). 아직 안 선
    /// 도시는 <b>다가가도 안 물어본다</b> — 지도 타일에는 그림이 박혀 있지만 들어갈 데가
    /// 아니다.
    /// </remarks>
    public Func<int, bool>? CityOpen { get; set; }

    /// <summary>
    /// 아직 안 선 도시를 지도에서 <b>지운다</b>.
    /// </summary>
    /// <remarks>
    /// 지도 자료에는 도시 그림이 박혀 있어 그냥 두면 안 선 도시도 보인다. 게임은 그릴
    /// 때마다 도시 표 <c>+0x74</c> 의 3×3 바탕 타일로 덮는데(<c>0x0048A1E0</c>), 우리는
    /// 칸 지도를 한 장으로 올려 두므로 그 자리를 미리 갈아 끼워 둔다.
    ///
    /// <paramref name="hidden"/> 이 바뀔 때만 한 장을 새로 짓는다 — 도시가 서는 것은
    /// 예순 해에 스무 번뿐이라 값이 싸다.
    /// </remarks>
    public void HideCities(IEnumerable<int> hidden,
                           IEnumerable<(int X, int Y, ushort[] Block)>? places = null)
    {
        if (_cities is not { } cities) return;

        var patch = new Dictionary<(int X, int Y), ushort>();

        void Lay(int x0, int y0, ushort[] block, int width, ushort keep)
        {
            for (int i = 0; i < block.Length; i++)
            {
                if (block[i] == keep) continue;
                patch[(x0 + i % width, y0 + i / width)] = block[i];
            }
        }

        foreach (int city in hidden)
        {
            if (!cities.TryCell(city, out int cx, out int cy, out _)) continue;
            Lay(cx, cy, cities.EraseOf(city), CityExeTable.EraseWidth, CityExeTable.Keep);
        }

        // 아직 못 찾은 발견물도 같은 손으로 가린다 — 그쪽은 2×2 다.
        foreach (var (x, y, block) in places ?? [])
            Lay(x, y, block, DiscoveryTable.EraseWidth, DiscoveryTable.Keep);

        _renderer.Erase(patch);
    }

    /// <summary>
    /// 지금 선 자리가 어귀인 도시 <b>모두</b> — 게임 커맨드 창은 창 안에 드는 도시마다 「…에 들어간다」 줄을
    /// 하나씩 단다(<c>0x0048B1E2</c> 가 둘레 칸을 훑는다). 차례는 도시 번호 차례다.
    /// </summary>
    public List<int> TownsAt()
    {
        var got = new List<int>();
        if (!_shipKnown || _cities == null) return got;

        int fx = (int)Math.Floor(_shipX);
        int fy = (int)Math.Floor(_shipY);
        for (int id = 0; id < CityExeTable.Count; id++)
        {
            if (CityOpen is { } open && !open(id)) continue;
            if (!_cities.TryCell(id, out int cx, out int cy, out int reach)) continue;
            if (fy < cy - TownSlack - TouchSlack || fy > cy + reach + TouchSlack) continue;
            int dx = fx - cx;
            if (dx > WorldMapRenderer.UnfoldedW / 2) dx -= WorldMapRenderer.UnfoldedW;
            if (dx < -WorldMapRenderer.UnfoldedW / 2) dx += WorldMapRenderer.UnfoldedW;
            if (dx < -TownSlack - TouchSlack || dx > reach + TouchSlack) continue;
            got.Add(id);
        }
        return got;
    }

    private int CityAt()
    {
        if (!_shipKnown || _cities == null) return -1;

        int fx = (int)Math.Floor(_shipX);
        int fy = (int)Math.Floor(_shipY);
        for (int id = 0; id < CityExeTable.Count; id++)
        {
            if (CityOpen is { } open && !open(id)) continue;   // 아직 안 선 도시
            if (!_cities.TryCell(id, out int cx, out int cy, out int reach)) continue;
            if (fy < cy - TownSlack - TouchSlack || fy > cy + reach + TouchSlack) continue;

            // 가로는 이어져 있다 — 날짜변경선을 넘어도 같은 도시다.
            int dx = fx - cx;
            if (dx > WorldMapRenderer.UnfoldedW / 2) dx -= WorldMapRenderer.UnfoldedW;
            if (dx < -WorldMapRenderer.UnfoldedW / 2) dx += WorldMapRenderer.UnfoldedW;
            if (dx < -TownSlack - TouchSlack || dx > reach + TouchSlack) continue;

            return id;
        }
        return -1;
    }

    /// <summary>
    /// 배에 가장 가까운 항구와 그 거리(칸). <paramref name="radiusCells"/> 밖이면 ID 가 -1 이다.
    /// 반지름은 꼭 적는다 — 넓을수록 도시마다 항구 칸을 찾아야 해서 무거워진다.
    /// </summary>
    public (int Id, double Cells) NearestDock(double radiusCells)
    {
        if (!_shipKnown) return (-1, double.NaN);
        int best = -1;
        double bestD = radiusCells * radiusCells;
        double coarse = radiusCells + HarborSearchCells;
        double coarseSq = coarse * coarse;

        for (int id = 0; id < GameMapCoords.CityCount; id++)
        {
            if (!GameMapCoords.TryCityCell(id, out double cx, out double cy)) continue;
            // 먼 도시는 항구 칸을 찾을 것도 없이 도시 중심만으로 거른다 — 항구 찾기가 무겁다.
            if (DistanceSq(cx, cy) > coarseSq) continue;

            var harbor = Harbor(id, cx, cy);
            double d = DistanceSq(harbor.X, harbor.Y);
            if (d < bestD) { bestD = d; best = id; }
        }
        return (best, best < 0 ? double.NaN : Math.Sqrt(bestD));
    }

    /// <summary>배가 닿을 수 있는 도시 앞 물칸. 도시 중심이 뭍이므로 한 칸씩 넓혀 가며 찾는다.</summary>
    private (double X, double Y) Harbor(int id, double cityX, double cityY)
    {
        if (_harbors.TryGetValue(id, out var cached)) return cached;
        var spot = NearestWater(cityX, cityY);
        _harbors[id] = spot;
        return spot;
    }

    /// <summary>배에서 그 칸까지 거리의 제곱. 가로가 이어져 있는 것을 셈에 넣는다.</summary>
    private double DistanceSq(double cellX, double cellY)
    {
        double dx = cellX - _shipX;
        if (dx > WorldMapRenderer.UnfoldedW / 2.0) dx -= WorldMapRenderer.UnfoldedW;
        if (dx < -WorldMapRenderer.UnfoldedW / 2.0) dx += WorldMapRenderer.UnfoldedW;
        double dy = cellY - _shipY;
        return dx * dx + dy * dy;
    }

    /// <summary>입항. 배를 세우고 알린다.</summary>
    public void EnterPort(string cityName)
    {
        _tickAccum = 0;
        _making = false;            // 세워 둔다 — 커서가 다시 쪽을 줄 때까지
        _desired = _heading;
        Status = $"[{cityName}] 입항 — {_shipX:F1}, {_shipY:F1} 칸";
    }

    /// <summary>
    /// 도시에서 나오면 <b>닻을 내린 채</b> 선다 — 왼쪽 클릭으로 닻을 올려야 나아간다.
    /// </summary>
    /// <remarks>
    /// 게임은 도시 화면(<c>0x00492430</c>)에서 돌아오자마자 <c>0x005B3A00</c> 에 1 을 놓는다
    /// (<c>0x0048EB32</c>) — 바다든 뭍이든 가리지 않는다. 입항할 때 <see cref="EnterPort"/> 가
    /// 나아가기를 세워 두므로, 닻이 없으면 닻도 안 보이고 움직이지도 않는 어정쩡한 채로 남았다.
    /// </remarks>
    public void HoldAfterCity()
    {
        if (!_ready) return;
        _anchored = true;
        _tickAccum = 0;
        _dirty = true;
    }

    /// <summary>배가 있는 자리로 되돌아가 다시 따라다닌다.</summary>
    public void RecenterOnShip()
    {
        if (!_shipKnown) return;
        _centerX = _shipX;
        _centerY = _shipY;
        _follow = true;
    }
}
