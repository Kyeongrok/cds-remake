using CdsHelper.Support.Local.Models;

namespace CdsHelper.Game.Engine.Sea;

/// <summary>
/// 해전 판 한 벌 — 격자·배·바람·이동 계획·동시 이동·충돌·퇴각·적의 길 짜기. 화면은 모른다.
/// </summary>
/// <remarks>
/// 볼트 <c>47.분석-해전</c> · <c>66.분석-해전 이동계획</c> · <c>83.분석-해전 적 AI와 들머리</c> 를 옮겼다.
/// <code>
///   격자        23 x 17 육각 — 짝수 X 줄에는 Y=16 이 없다(0x00441C5F)
///   배          열여섯 — 0~7 아군 · 8~15 적
///   처음 자리   아군 판종류 k = rand(4)(0x004421B9) · 적 (k+2)%4 — 위 · 오른쪽 · 아래 · 왼쪽(<see cref="Place"/>)
///   걸음 하나   먼저 돈다(0 그대로 · 1 오른쪽 · 2 왼쪽) → <b>그리고 한 칸 나아간다</b>(0x0043AC00)
///   이동력      0x004349A0 — (바람세기+1) * 추진력 * 돛효율 / 100 + 1, 최대 6, 정면 역풍이면 1
///   한 턴       계획 → 결정 → 적의 길(0x0043B710) → 모든 배가 걸음 하나씩 나란히(0x0043CA60)
///   충돌        들어갈 칸에 배가 있으면 선다 — 다음 턴 한 번 못 움직인다(0x00439858 · 0x0043D7E7)
///   턴 끝       rand(10)==0 이면 바람이 (풍향+5)%6 으로 돈다
///   퇴각 지대   바람이 정한 가장자리 한 곳(0x0043E090)
/// </code>
/// 가까운 싸움은 볼트 <c>94.분석-해전 총격전</c> · <c>95.분석-해전 충돌·백병전·나포·일기토</c> 를 옮겼다 —
/// 충돌(<c>0x004397E0</c>) → 백병전(<c>0x00439D50</c>) → 불 → 나포·일기토(<c>0x0043A200</c>), 총격전(<c>0x004362E0</c>).
/// 원본 결함(적이 걸 때 곱하는 승원이 뒤바뀜 · 적이 나포할 때 막는 매력만 적 것)도 그대로 옮기고 그 자리에 적었다.
/// 괴물 싸움(<c>+0x8FC</c>)과 부관 위임(<c>+0x944</c>)도 옮겼다 —
/// <see cref="Monster"/> · <see cref="MonsterUp"/> · <see cref="Delegated"/>.
/// 괴물은 머리(칸 8)와 둘레 몸 여섯(칸 9~14)으로 일곱 칸을 차지하고(<see cref="PlaceMonster"/>),
/// 입는 피해는 모두 머리 내구로 간다.
/// </remarks>
public sealed class SeaBattle
{
    /// <summary>격자 가로 칸(X 0~22)과 세로 칸(Y 0~16).</summary>
    public const int Cols = 23, Rows = 17;

    /// <summary>한 편 배 수. 0~7 이 아군, 8~15 가 적이다.</summary>
    public const int PerSide = 8;

    /// <summary>방향 가짓수(육각).</summary>
    public const int Ways = 6;

    /// <summary>한 턴에 갈 수 있는 걸음의 끝.</summary>
    public const int MaxPower = 6;

    /// <summary>이동력이 하나 붙는 선수상 번호(<c>0x0044CA30</c> == 0x21).</summary>
    public const int SwiftFigurehead = 0x21;

    /// <summary>걸음 하나의 선회 — 0 그대로 · 1 오른쪽(방향+1) · 2 왼쪽(방향+5).</summary>
    public enum Move { Straight = 0, TurnRight = 1, TurnLeft = 2 }

    /// <summary>
    /// 배 한 척의 상태. 원본 배 칸 <c>+0x30C</c> — 4 이상 떠 있음(5 는 불, <see cref="Ship.Burning"/>) · 3 퇴각 ·
    /// 2 나포/승원 0 · 1 가라앉음. 판 그리기(<c>0x00440480</c>)는 4 이상만 그린다.
    /// </summary>
    public enum ShipState { Afloat, Retreated, Sunk, Captured }

    /// <summary>판 위의 배 한 척.</summary>
    public sealed class Ship
    {
        public int Index { get; init; }
        public bool Mine => Index < PerSide;
        public bool Flagship => Index % PerSide == 0;
        public string Name { get; init; } = "";
        /// <summary>선박 종류 이름(<c>+0x314</c>) — 해전전황정보(선박)에 낸다.</summary>
        public string HullName { get; init; } = "";
        /// <summary>적재량(<c>+0x300</c>).</summary>
        public int Cargo { get; init; }
        /// <summary>대포 문수(<c>+0x31C</c>). 큰 한 방을 맞으면 준다.</summary>
        public int Guns { get; internal set; }
        public int X { get; internal set; }
        public int Y { get; internal set; }
        /// <summary>뱃머리 0~5. 0 이 위(Y−), 시계 방향이다.</summary>
        public int Way { get; internal set; }
        /// <summary>추진력(배 칸 <c>+0x14</c>). 큰 한 방(대포 35문 넘는 배)에 맞으면 준다.</summary>
        public int Speed { get; internal set; }
        /// <summary>마스트 셋의 돛(0 없음 · 1 삼각 · 2 사각) — 이동력 셈의 a·b·c.</summary>
        public int[] Sails { get; init; } = new int[3];
        /// <summary>선수상 번호. 0x21 이면 이동력 +1.</summary>
        public int Figurehead { get; init; } = -1;
        /// <summary>내구(<c>+0x304</c>).</summary>
        public int Hp { get; internal set; }
        /// <summary>최대 내구(<c>0x0044C880</c>) — 불사조상이 턴 끝에 여기까지 되살린다.</summary>
        public int MaxHp { get; init; }
        /// <summary>승원(<c>+0x308</c>).</summary>
        public int Crew { get; internal set; }
        /// <summary>필요승원 = <c>선박표[0x4FC214]+10</c>(선체표 +0x34 + 10) — 적이 물러설지 볼 때 승원과 견준다.</summary>
        public int MinCrew { get; init; }
        /// <summary>대포 갈래(−1 없음 · 0 세이커 · 1 캘버린 · 2 페리에 · 3 카논).</summary>
        public int Gun { get; init; } = -1;
        /// <summary>이번 턴 이동력(<c>+0x2F8</c>).</summary>
        public int Power { get; internal set; }
        public ShipState State { get; internal set; } = ShipState.Afloat;
        /// <summary>그림 벌(SCOMBAT 파트 5~12).</summary>
        public int Art { get; init; }
        /// <summary>이번 턴 걸음(<c>+0x324</c> 여섯). 걸음마다 선회 하나 + 한 칸.</summary>
        public List<Move> Plan { get; } = [];
        /// <summary>지시를 마쳤는지(<c>+0x320</c> 1).</summary>
        public bool Ordered { get; internal set; }
        /// <summary>
        /// 제자리 선회(<c>+0x8D0</c>) — 걸음 수 0 에 선회만 적힌 지시다. 한 칸도 안 가고
        /// 뱃머리만 돌린다(<c>0x0043E92B</c>). <see cref="Move.Straight"/> 면 없는 것이다.
        /// </summary>
        public Move Pivot { get; internal set; }
        /// <summary>
        /// 지시상태의 충돌 몫(<c>+0x320</c>) — 3 이번 턴에 들이받음 · 2 받혔거나 지난 턴에 들이받음 · 0 멀쩡.
        /// </summary>
        /// <remarks>턴 끝(<c>0x0043D7E7</c>)에 3 → 2, 그 밖 → 0 이다.</remarks>
        internal int Bump { get; set; }
        /// <summary>이번 턴에 들이받아 섰는지(3) — 이번 턴 남은 걸음과 다음 턴 걸음·총격을 잃는다. 대포는 쏜다.</summary>
        public bool Crashed => Bump == 3;
        /// <summary>계획 차례에서는 지난 턴에 들이받아 이번 턴 못 움직이는지(2). 실행 중에는 받힌 배도 2 다.</summary>
        public bool Stuck => Bump == 2;
        /// <summary>걸음·총격을 건너뛰는지 — 지시상태 ≥ 2(<c>0x0043CB4F</c>).</summary>
        public bool Halted => Bump >= 2;
        /// <summary>판 밖으로 나가려다 이번 턴 남은 걸음을 거뒀는지 — 원본 길 짜기는 판 밖을 안 내어 우리 어림이다.</summary>
        internal bool Blocked { get; set; }
        /// <summary>
        /// 불이 붙었는지(상태 5, <c>0x00437E3A</c>). <b>꺼지지 않는다</b> — 턴마다 내구 3 을 깎는다. 이동·총격·포격은 그대로 한다.
        /// </summary>
        public bool Burning { get; internal set; }

        /// <summary>
        /// 괴물의 <b>몸 조각</b>(칸 9~14)인지 — 머리(칸 8) 둘레 여섯 칸을 차지할 뿐 스스로는 움직이지도 싸우지도 않는다
        /// (<c>0x00440EBF</c> 가 칸 8 값을 베끼고, 판 되돌이 <c>0x0043CB2D</c> 가 칸 8 뒤를 건너뛴다).
        /// </summary>
        public bool Body { get; init; }

        public bool CanAct => State == ShipState.Afloat;
    }

    private readonly Ship?[] _ships = new Ship?[PerSide * 2];
    private readonly Random _rng;

    /// <summary>격자 표시 — 비트 8 은 먼저 짠 배가 지나갈 칸, 4 는 위험 칸(<c>+0x96C</c>).</summary>
    private readonly int[,] _marks = new int[Cols, Rows];

    /// <summary>풍향 0~5(<c>+0x0860</c>). 0 이 북풍이다.</summary>
    public int Wind { get; private set; }

    /// <summary>
    /// 판을 열 때의 풍향 — 퇴각 지대가 이것으로 정해진다(<c>+0x08DC</c>, <c>0x0044200A</c> 에서 한 번 적는다).
    /// </summary>
    /// <remarks>
    /// 턴 끝에 바람이 돌아도(<c>0x0043DA5F</c> 는 <c>+0x860</c> 만 바꾼다) 퇴각 판정(<c>0x0043E0B2</c>) ·
    /// E 표시(<c>0x004403DB</c>) · 적의 퇴각 길(<c>0x0043B336</c> · <c>0x0043B8CB</c>)은 이 값을 본다.
    /// 예전에는 지금 바람을 봐서 바람이 돌면 퇴각 칸이 다른 가장자리로 옮겨 갔다.
    /// </remarks>
    public int RetreatWind { get; }

    /// <summary>바람 세기(<c>+0x0864</c>).</summary>
    public int WindStrength { get; private set; }

    public IEnumerable<Ship> Ships => _ships.OfType<Ship>();

    public Ship? At(int index) => index >= 0 && index < _ships.Length ? _ships[index] : null;

    public SeaBattle(Random rng, int wind, int windStrength)
    {
        _rng = rng;
        Wind = ((wind % Ways) + Ways) % Ways;
        RetreatWind = Wind;
        WindStrength = Math.Max(0, windStrength);
    }

    /// <summary>
    /// 바다의 바람(16방위 · 세기)으로 판을 연다(<c>0x00441F1C</c>).
    /// </summary>
    /// <remarks>
    /// <code>
    ///   방위 0·15 → 3      1~3 → 4      4 → 4 또는 5      5·6 → 5
    ///   방위 7~9  → 0      10·11 → 1    12 → 1 또는 2     13·14 → 2
    /// </code>
    /// 세기는 그대로 <c>+0x864</c> 에 든다.
    /// </remarks>
    public static SeaBattle FromSeaWind(Random rng, int dir16, int strength)
    {
        int d = ((dir16 % 16) + 16) % 16;
        int wind = d switch
        {
            0 or 15 => 3,
            >= 1 and <= 3 => 4,
            4 => rng.Next(2) == 0 ? 4 : 5,
            5 or 6 => 5,
            >= 7 and <= 9 => 0,
            10 or 11 => 1,
            12 => rng.Next(2) == 0 ? 1 : 2,
            _ => 2,
        };
        return new SeaBattle(rng, wind, strength);
    }

    // ── 격자 ───────────────────────────────────────────────────────────────

    /// <summary>판 안의 칸인지.</summary>
    public static bool OnBoard(int x, int y) =>
        x >= 0 && x < Cols && y >= 0 && y < Rows && !(y == Rows - 1 && (x & 1) == 0);

    /// <summary>그 방향으로 한 칸(<c>0x0043AD22</c>).</summary>
    public static (int X, int Y) Step(int x, int y, int way)
    {
        bool even = (x & 1) == 0;
        return (((way % Ways) + Ways) % Ways) switch
        {
            0 => (x, y - 1),
            1 => (x + 1, even ? y : y - 1),
            2 => (x + 1, even ? y + 1 : y),
            3 => (x, y + 1),
            4 => (x - 1, even ? y + 1 : y),
            _ => (x - 1, even ? y : y - 1),
        };
    }

    /// <summary>선회 하나를 먹인 방향.</summary>
    public static int Turn(int way, Move move) => move switch
    {
        Move.TurnRight => (way + 1) % Ways,
        Move.TurnLeft => (way + 5) % Ways,
        _ => way,
    };

    /// <summary>그 칸에 떠 있는 배. 없으면 null.</summary>
    public Ship? ShipAt(int x, int y) =>
        Ships.FirstOrDefault(s => s.CanAct && s.X == x && s.Y == y);

    /// <summary>두 칸 사이 걸음 거리(판 안에서). 판 밖으로 돌아가는 길은 안 센다.</summary>
    public static int Distance(int x1, int y1, int x2, int y2) => BfsDistance(x1, y1, x2, y2);

    /// <summary>걸음으로 잰 거리(판 안에서만). 칸이 적어 너비 우선으로 잰다.</summary>
    private static int BfsDistance(int x1, int y1, int x2, int y2)
    {
        if (x1 == x2 && y1 == y2) return 0;
        var seen = new bool[Cols, Rows];
        var queue = new Queue<(int X, int Y, int D)>();
        queue.Enqueue((x1, y1, 0));
        seen[x1, y1] = true;
        while (queue.Count > 0)
        {
            var (x, y, d) = queue.Dequeue();
            for (int w = 0; w < Ways; w++)
            {
                var (nx, ny) = Step(x, y, w);
                if (!OnBoard(nx, ny) || seen[nx, ny]) continue;
                if (nx == x2 && ny == y2) return d + 1;
                seen[nx, ny] = true;
                queue.Enqueue((nx, ny, d + 1));
            }
        }
        return int.MaxValue;
    }

    // ── 배 놓기 ────────────────────────────────────────────────────────────

    /// <summary>
    /// 배를 판에 올린다. <paramref name="slot"/> 은 그 편 안의 차례(0~7), 0 이 기함이다.
    /// </summary>
    /// <remarks>
    /// 편의 판종류(<see cref="SideOf"/>)로 기함 자리와 대열 벌·부호를 가른다(<c>0x00442219</c>~<c>0x00442B38</c>).
    /// <code>
    ///   k 0 위       기함 (11, 3)   방향 3             호위 (11 − ΔX, 3 − ΔY)   첫 벌
    ///   k 1 오른쪽   기함 (17, R)   방향 rand(2)+4     호위 (17 − ΔX, R − ΔY)   둘째 벌
    ///   k 2 아래     기함 (11, 13)  방향 0             호위 (11 + ΔX, 13 + ΔY)  첫 벌
    ///   k 3 왼쪽     기함 (5, L)    방향 rand(2)+1     호위 (5 + ΔX, L + ΔY)    둘째 벌
    ///   R = 대열 0·4·6 이면 7 · 5 면 9 · 그 밖 8 ,  L = 대열 0·4·6 이면 9 · 5 면 7 · 그 밖 8
    /// </code>
    /// 부호는 짝수 k 가 (k − 1), 홀수 k 가 (k − 2) 이고 <b>ΔX 와 ΔY 둘 다</b>에 곱한다(<c>0x00442B12</c> · <c>0x00442B22</c>).
    /// 그래서 맞선 두 편의 대열은 판 가운데에서 점대칭한 꼴로 마주 본다. 대열이 없으면(모의해전 연습선) 대열 0 으로 친다.
    /// </remarks>
    public Ship Place(bool mine, int slot, string name, int speed, int[] sails, int art,
                      int hp = 50, int crew = 30, int minCrew = 10, int gun = -1, int figurehead = -1,
                      int formation = -1, string hullName = "", int cargo = 0, int guns = 0, int maxHp = 0)
    {
        int index = (mine ? 0 : PerSide) + Math.Clamp(slot, 0, PerSide - 1);
        int side = SideOf(mine);
        int f = formation is >= 0 and < FormationCount ? formation : 0;
        var (fx, fy) = FlagshipCell(side, f);
        int x = fx, y = fy;
        if (slot > 0)
        {
            bool vertical = (side & 1) == 0;
            int sign = vertical ? side - 1 : side - 2;
            var (dx, dy) = (vertical ? FormationsTopBottom : Formations)[f][Math.Min(slot, 7) - 1];
            x = fx + sign * dx;
            y = fy + sign * dy;
        }
        (x, y) = FreeCellNear(x, y);

        var ship = new Ship
        {
            Index = index,
            Name = name,
            X = x,
            Y = y,
            Way = SideWay(mine),
            Speed = speed,
            Sails = sails,
            Art = art,
            Hp = hp,
            MaxHp = Math.Max(hp, maxHp),
            Crew = crew,
            MinCrew = minCrew,
            Gun = gun,
            Figurehead = figurehead,
            HullName = hullName,
            Cargo = cargo,
            Guns = gun < 0 ? 0 : guns,
        };
        _ships[index] = ship;
        ship.Power = PowerOf(ship);
        return ship;
    }

    /// <summary>편마다 한 번 굴린 처음 방향 — [0] 아군 · [1] 적.</summary>
    private readonly int?[] _sideWay = new int?[2];

    /// <summary>
    /// 그 편의 처음 방향. 원본은 편마다 기함 자리를 정할 때 방향을 <b>한 번만</b> 정해 <c>[edi+4]</c> 에 두고
    /// (<c>0x0044222F</c> · <c>0x00442279</c> · <c>0x0044229E</c> · <c>0x004422DC</c>), 호위선은 그 값을 베낀다
    /// (<c>0x00442B3B</c> → <c>[ebx+8]</c>) — 한 편의 배는 모두 같은 쪽을 보고 선다. 위 편은 3, 아래 편은 0,
    /// 오른쪽 편은 rand(2)+4, 왼쪽 편은 rand(2)+1 이다.
    /// </summary>
    private int SideWay(bool mine)
    {
        int k = mine ? 0 : 1;
        return _sideWay[k] ??= SideOf(mine) switch
        {
            0 => 3,
            1 => _rng.Next(2) + 4,
            2 => 0,
            _ => _rng.Next(2) + 1,
        };
    }

    /// <summary>
    /// 아군 판종류 0~3 — 0 위 · 1 오른쪽 · 2 아래 · 3 왼쪽. 원본은 해전마다 rand(4) 로 굴린다(<c>0x004421B9</c>,
    /// 괴물 판도 같다). 배를 놓기 전에 매겨야 한다. 굴리지 않으면 오른쪽이다.
    /// </summary>
    public int OurSide
    {
        get => _ourSide;
        set => _ourSide = ((value % 4) + 4) % 4;
    }

    private int _ourSide = 1;

    /// <summary>그 편의 판종류 — 적은 아군의 맞은편 (k+2)%4 다(<c>0x004421CB</c>).</summary>
    public int SideOf(bool mine) => mine ? OurSide : (OurSide + 2) % 4;

    /// <summary>
    /// 그 판종류의 기함 칸(<c>0x00442219</c>~<c>0x004422D6</c>) — 위 (11, 3) · 아래 (11, 13) 은 대열과 상관없고,
    /// 오른쪽 (17, R) · 왼쪽 (5, L) 의 줄은 대열로 가른다(<see cref="Place"/>).
    /// </summary>
    public static (int X, int Y) FlagshipCell(int side, int formation) => (((side % 4) + 4) % 4) switch
    {
        0 => (11, 3),
        1 => (17, formation is 0 or 4 or 6 ? 7 : formation == 5 ? 9 : 8),
        2 => (11, 13),
        _ => (5, formation is 0 or 4 or 6 ? 9 : formation == 5 ? 7 : 8),
    };

    // ── 괴물 놓기 ─────────────────────────────────────────────────────────

    /// <summary>
    /// 괴물 네 마리의 값(<c>0x00440DC1</c>~<c>0x00440E56</c>) — 무력(<c>+0x924</c>) · 내구(<c>+0x5A4</c>) · 적재(<c>+0x5A0</c>).
    /// 그림 벌(<c>+0x900</c>, SCOMBAT 파트 13+벌, <c>0x0044307B</c>)도 함께 둔다.
    /// </summary>
    public static (int Might, int Hp, int Cargo, int Art) MonsterStats(int person) => person switch
    {
        0x111 => (100, 250, 250, 0),     // 식인 상어
        0x110 => (90, 280, 300, 2),      // 시서펜트
        0x112 => (70, 260, 350, 3),      // 맨터
        _ => (80, 300, 400, 1),          // 크라켄(0x10F)
    };

    /// <summary>
    /// 괴물 몸 여섯 조각의 자리 — 머리 둘레 육각 이웃이다(<c>0x00442D00</c> · <c>0x00439440</c> 의 스택 표).
    /// 짝수 X 면 뒤 두 쌍의 Y 에 2 를 더한다(<c>0x00442D60</c> — 짝수 줄은 반 칸 아래로 밀렸다).
    /// </summary>
    private static readonly (int Dx, int Dy)[] BodyCells = [(0, -1), (0, 1), (-1, 0), (1, 0), (-1, -1), (1, -1)];

    /// <summary>
    /// 괴물을 판에 올린다 — 머리는 칸 8, 몸은 칸 9~14 이고 칸 15 는 빈다(<c>0x00440D90</c> · <c>0x00442B8E</c>).
    /// </summary>
    /// <remarks>
    /// <code>
    ///   값        무력·내구·적재는 <see cref="MonsterStats"/> , 승원 = 내구/5 , 상태 4
    ///   적 제독   방어(+0x930) 20 · 검술(+0x938)·사격술(+0x93C)·운세(+0x940) 1 · 적 척수 [0x848] 7
    ///   몸        칸 8 의 선체·내구·적재·승원·상태를 베끼고 걸음은 0(0x00440EBF~0x00440F02)
    ///   자리      적 판종류 k = (아군 판종류 + 2) % 4 (0x004421CB)
    ///     k 0   X = rand(21)+1 ,  Y = 7 − rand(7) ,   방향 3
    ///     k 1   X = rand(12)+10 , Y = rand(15)+1 ,  방향 rand(2)+4
    ///     k 2   X = rand(21)+1 ,  Y = rand(7)+9 ,   방향 0
    ///     k 3   X = 12 − rand(12) , Y = rand(15)+1 , 방향 rand(2)+1
    ///   몸 조각의 방향은 늘 2 다(0x00442D43)
    /// </code>
    /// 괴물 판도 아군 판종류를 rand(4) 로 굴리므로(<c>0x004421B9</c>) 괴물은 <see cref="SideOf"/>(적) 쪽 표로 선다.
    /// </remarks>
    public Ship PlaceMonster(int person)
    {
        var (might, hp, cargo, art) = MonsterStats(person);
        int x, y, way;
        switch (SideOf(mine: false))
        {
            case 0: x = _rng.Next(21) + 1; y = 7 - _rng.Next(7); way = 3; break;
            case 1: x = _rng.Next(12) + 10; y = _rng.Next(15) + 1; way = _rng.Next(2) + 4; break;
            case 2: x = _rng.Next(21) + 1; y = _rng.Next(7) + 9; way = 0; break;
            default: x = 12 - _rng.Next(12); y = _rng.Next(15) + 1; way = _rng.Next(2) + 1; break;
        }

        MonsterPerson = person;
        EnemySide = new Side(Gunnery: 0, Might: might, Defense: 20, Mind: 0, Charm: 0,
                             Sword: 1, Shooting: 1, Fortune: 1);
        // 선체 칸(+0x5B4)은 괴물 판에서 안 채워 0 으로 본다 — 물러설 때 견주는 필요승원이 그 줄 값이다.
        int minCrew = Hull.Table[0].Crew;

        Ship Make(int index, int px, int py, int pway, bool body) => new()
        {
            Index = index,
            Name = "괴물",
            X = px,
            Y = py,
            Way = pway,
            Speed = 0,
            Sails = new int[3],
            Art = art,
            Hp = hp,
            MaxHp = hp,
            Crew = hp / 5,
            MinCrew = minCrew,
            Gun = -1,
            Cargo = cargo,
            Body = body,
        };

        var head = Make(PerSide, x, y, way, body: false);
        _ships[PerSide] = head;
        for (int k = 0; k < BodyCells.Length; k++)
            _ships[PerSide + 1 + k] = Make(PerSide + 1 + k, x, y, 2, body: true);
        _ships[PerSide * 2 - 1] = null;
        LayMonsterBody();
        foreach (var s in Ships.Where(s => !s.Mine)) s.Power = PowerOf(s);
        return head;
    }

    /// <summary>괴물 머리(칸 8). 괴물 판이 아니면 null.</summary>
    public Ship? MonsterHead => Monster ? At(PerSide) : null;

    /// <summary>
    /// 몸 여섯 조각을 머리 둘레에 다시 깐다 — 머리가 한 칸 옮길 때마다다(<c>0x00439425</c>~<c>0x004394DF</c>).
    /// </summary>
    private void LayMonsterBody()
    {
        if (At(PerSide) is not { } head) return;
        bool even = (head.X & 1) == 0;
        for (int k = 0; k < BodyCells.Length; k++)
        {
            if (At(PerSide + 1 + k) is not { Body: true } part) continue;
            var (dx, dy) = BodyCells[k];
            part.X = head.X + dx;
            part.Y = head.Y + dy + (even && k >= 4 ? 2 : 0);
        }
    }

    /// <summary>괴물 판에서 칸 8 이상(머리·몸)인지 — 피해가 모두 머리 내구로 간다.</summary>
    private bool IsMonster(Ship s) => Monster && !s.Mine && _ships[PerSide] is not null;

    // ── 대열 ──────────────────────────────────────────────────────────────

    /// <summary>대열 가짓수.</summary>
    public const int FormationCount = 8;

    /// <summary>대열 이름 — 창에는 그림만 있어 우리가 붙인 이름이다.</summary>
    public static readonly string[] FormationNames =
        ["세로 한 줄", "둘러싸기", "넓게 감싸기", "흩어짐", "빗금 쐐기", "오른빗금", "왼빗금", "부채"];

    /// <summary>
    /// 호위함 일곱의 (ΔX, ΔY) 둘째 벌 — 오른쪽·왼쪽 편이 쓴다(볼트 84, <c>0x0044231E</c>~).
    /// </summary>
    /// <remarks>
    /// 원본 스택 표는 <b>대열 여덟 x 벌 둘</b>(한 대열이 112바이트 = 일곱 쌍 x 두 벌)이고,
    /// 자리를 고르는 셈이 벌을 이렇게 가른다(<c>0x00442AC4</c>~<c>0x00442B22</c>).
    /// <code>
    ///   ecx = 표 + 대열 * 112
    ///   판종류 &amp; 1
    ///     짝(0 위 · 2 아래)     첫 벌 [ecx]        · ΔX·ΔY x (판종류 − 1)   ; 위 −1 · 아래 +1
    ///     홀(1 오른쪽 · 3 왼쪽) 둘째 벌 [ecx+0x38] · ΔX·ΔY x (판종류 − 2)   ; 오른쪽 −1 · 왼쪽 +1
    /// </code>
    /// 곧 오른쪽(1)·왼쪽(3) 편은 둘째 벌을, 위(0)·아래(2) 편은 첫 벌(<see cref="FormationsTopBottom"/>)을 쓴다.
    /// 여기 옮긴 것이 그 둘째 벌이다 — 예전에는 볼트에 적힌 앞 여덟 줄(대열 0~3 의 두 벌)을 여덟 대열로
    /// 늘어놓아, 쓰이지 않는 벌이 절반이고 대열 4~7 은 아예 빠져 있었다.
    ///
    /// 표는 스택에 상수로 깔리는 것이라 코드에서 떠냈다. <b>대열 7 의 다섯째 ΔY</b> 하나만
    /// 레지스터로 들어가는데(<c>0x00442AA3 mov [ebp-0x68], ebx</c>), 그 ebx 는 <c>0x0044244A</c> 에서
    /// 3 으로 한 번 채워지고 그 사이에 안 바뀌므로 3 이다. 홀수 ΔX 짝은 육각 격자에서 (1,−4)·(1,3) 이
    /// 위아래로 맞선다(대열 1 의 (1,−2)·(1,1) 과 같은 꼴) — 예전에는 넷째의 짝으로 보아 4 로 뒀다.
    /// </remarks>
    public static readonly (int Dx, int Dy)[][] Formations =
    [
        [(0, -2), (0, 2), (0, -4), (0, 4), (0, -6), (0, 6), (0, -8)],
        [(2, 0), (1, -2), (1, 1), (0, -3), (0, 3), (-2, -1), (-2, 1)],
        [(2, 0), (1, 1), (1, -2), (0, 3), (0, -3), (-2, 4), (-2, -4)],
        [(2, -1), (2, 1), (-1, -3), (-1, 2), (0, -4), (0, 4), (-2, 0)],
        [(0, -2), (1, 1), (1, -4), (2, 3), (2, -5), (3, 4), (3, -7)],
        [(2, 2), (-2, -2), (2, 4), (-2, -4), (2, 6), (-2, -6), (2, 8)],
        [(2, -2), (-2, 2), (2, -4), (-2, 4), (2, -6), (-2, 6), (2, -8)],
        [(2, 0), (0, -1), (0, 1), (1, -4), (1, 3), (-2, -2), (-2, 2)],
    ];

    /// <summary>
    /// 호위함 일곱의 (ΔX, ΔY) 첫 벌 — 위(판종류 0)·아래(2) 편이 쓴다(<c>0x0044231E</c>~ 스택 표의 대열마다 앞 56바이트,
    /// <c>0x00442B12</c> 가 <c>[ecx]</c> · <c>[ecx+4]</c> 를 읽는다). 둘째 벌(<see cref="Formations"/>)을 가로세로로
    /// 눕힌 꼴이다.
    /// </summary>
    public static readonly (int Dx, int Dy)[][] FormationsTopBottom =
    [
        [(-2, 0), (2, 0), (-4, 0), (4, 0), (-6, 0), (6, 0), (-8, 0)],
        [(0, -3), (-2, -2), (2, -2), (-3, -1), (3, -1), (-1, 1), (1, 1)],
        [(0, -3), (2, -2), (-2, -2), (3, 0), (-3, -2), (5, 1), (-5, 1)],
        [(-1, -3), (1, -3), (-2, 1), (2, 1), (-4, -1), (4, -1), (0, 2)],
        [(-2, 0), (2, -1), (-4, -1), (4, -2), (-6, -2), (6, -3), (-8, -3)],
        [(1, -3), (-1, 1), (3, -3), (-3, 1), (5, -3), (-5, 1), (7, -3)],
        [(-1, -3), (1, 1), (-3, -3), (3, 1), (-5, -3), (5, 1), (-7, -3)],
        [(0, -3), (-1, 0), (1, 0), (-3, -2), (3, -2), (-3, 1), (3, 1)],
    ];

    /// <summary>
    /// 판 안에서 비어 있는 가장 가까운 칸. 원본이 판 밖·겹침을 비키는 셈(<c>0x00442B0A</c> 뒤)은 아직
    /// 못 짚어, 판 안으로 당긴 뒤 걸음이 가까운 빈 칸으로 옮긴다.
    /// </summary>
    private (int X, int Y) FreeCellNear(int x, int y)
    {
        x = Math.Clamp(x, 0, Cols - 1);
        y = Math.Clamp(y, 0, Rows - 1);
        if (!OnBoard(x, y)) y = Rows - 2;
        if (ShipAt(x, y) is null) return (x, y);

        var seen = new bool[Cols, Rows];
        var queue = new Queue<(int, int)>();
        queue.Enqueue((x, y));
        seen[x, y] = true;
        while (queue.Count > 0)
        {
            var (cx, cy) = queue.Dequeue();
            for (int w = 0; w < Ways; w++)
            {
                var (nx, ny) = Step(cx, cy, w);
                if (!OnBoard(nx, ny) || seen[nx, ny]) continue;
                if (ShipAt(nx, ny) is null) return (nx, ny);
                seen[nx, ny] = true;
                queue.Enqueue((nx, ny));
            }
        }
        return (x, y);
    }

    // ── 이동력 ────────────────────────────────────────────────────────────

    /// <summary>돛 효율표 — 줄 k = a+2b+4c (3~14), 칸 바람각 갈래(0 / 1·5 / 2·4).</summary>
    private static readonly int[,] SailTable =
    {
        { 5, 8, 5 }, { 8, 9, 3 }, { 8, 8, 4 }, { 10, 9, 2 }, { 6, 11, 6 }, { 8, 11, 5 },
        { 9, 10, 5 }, { 9, 11, 4 }, { 9, 10, 5 }, { 9, 11, 4 }, { 9, 11, 4 }, { 10, 11, 3 },
    };

    /// <summary>이동력(<c>0x004349A0</c>).</summary>
    public int PowerOf(Ship ship)
    {
        // 괴물은 돛 셈을 건너뛴다(0x00434CB5).
        if (Monster && !ship.Mine) return MonsterPower;

        int angle = ((Wind - ship.Way) % Ways + Ways) % Ways;
        int a = ship.Sails.ElementAtOrDefault(0), b = ship.Sails.ElementAtOrDefault(1), c = ship.Sails.ElementAtOrDefault(2);
        int band = angle == 0 ? 0 : angle is 1 or 5 ? 1 : 2;

        int e;
        if (angle == 3) e = 0;
        else if (b == 0 && c == 0)
            e = band switch { 0 => a <= 1 ? 6 : 9, 1 => a <= 1 ? 5 : 6, _ => a <= 1 ? 3 : 1 };
        // 원본은 첨자를 <b>안 자른다</b>(0x00434BE9) — 돛 값이 크면 표 밖 스택을 읽는다.
        // 우리는 떨어지지 않게 자른다(돛 값이 0~2 면 어차피 같다).
        else e = SailTable[Math.Clamp(a + 2 * b + 4 * c, 3, 14) - 3, band];

        int power = e == 0 ? 1 : (WindStrength + 1) * ship.Speed * e / 100 + 1;
        if (ship.Mine && ship.Figurehead == SwiftFigurehead) power++;
        return Math.Min(MaxPower, power);
    }

    // ── 길 ────────────────────────────────────────────────────────────────

    /// <summary>
    /// 걸음을 따라간 칸들 — 걸음마다 먼저 돌고 한 칸 나아간다. 판 밖으로 나가면 null.
    /// </summary>
    public static List<(int X, int Y, int Way)>? Trace(int x, int y, int way, IReadOnlyList<Move> plan)
    {
        var path = new List<(int, int, int)>(plan.Count);
        foreach (var move in plan)
        {
            way = Turn(way, move);
            (x, y) = Step(x, y, way);
            if (!OnBoard(x, y)) return null;
            path.Add((x, y, way));
        }
        return path;
    }

    /// <summary>
    /// 길 후보 전부(<c>0x0043AC00</c>) — 걸음 수 1 부터 이동력까지, 선회 갈래 셋을 모두 늘어놓는다.
    /// 짧은 길이 앞에 온다.
    /// </summary>
    /// <param name="avoidReserved">먼저 짠 배가 지나갈 칸(+8)과 배가 선 칸을 피할지(모드 2 이상).</param>
    /// <param name="avoidDanger">위험 칸(+4)도 피할지(모드 2).</param>
    private IEnumerable<(List<Move> Plan, int X, int Y, int Way)> Paths(Ship ship, bool avoidReserved, bool avoidDanger)
    {
        for (int len = 1; len <= ship.Power; len++)
        {
            int total = (int)Math.Pow(3, len);
            for (int code = 0; code < total; code++)
            {
                // 늘어놓는 차례는 <b>마지막 걸음이 가장 빨리</b> 바뀐다 — 원본의 자릿수 올림이
                // 끝자리부터 올라간다(0x0043AB4D~0x0043AB7A). 같은 칸에 닿는 길이 여럿이면
                // 먼저 걸린 것을 쓰므로 차례가 고르는 길을 바꾼다.
                // (원본은 자릿수가 절대 방향 여섯이고 우리는 선회 셋이라 꼭 같지는 않다.)
                var plan = new List<Move>(new Move[len]);
                int rest = code;
                for (int i = len - 1; i >= 0; i--) { plan[i] = (Move)(rest % 3); rest /= 3; }

                int x = ship.X, y = ship.Y, way = ship.Way;
                bool ok = true;
                foreach (var move in plan)
                {
                    way = Turn(way, move);
                    (x, y) = Step(x, y, way);
                    if (!StepFree(ship, x, y, avoidReserved)) { ok = false; break; }
                    if (avoidDanger && (_marks[x, y] & 4) != 0) { ok = false; break; }
                }
                if (ok) yield return (plan, x, y, way);
            }
        }
    }

    /// <summary>
    /// 길 후보의 한 칸이 들어갈 만한지(<c>0x0043AD91</c>~<c>0x0043AE8A</c>) — 판 안이고, 모드 2 이상이면 먼저 짠 배가
    /// 지나갈 칸(+8)과 배가 선 칸을 피한다.
    /// </summary>
    /// <remarks>
    /// <b>괴물 머리</b>(괴물 판의 칸 8)는 다르다 — 칸 차지를 안 보고(<c>0x0043ADC6</c>) 몸 여섯 조각이 판 밖으로
    /// 안 나가게 X 1~21 · Y 1~15 안에 머문다. Y 15 에서는 짝수 X 를 뺀다 — 그 아래 칸(Y 16)이 짝수 줄에 없다(<c>0x0043AE25</c>).
    /// </remarks>
    private bool StepFree(Ship ship, int x, int y, bool avoidReserved)
    {
        if (Monster && ship.Index == PerSide)
            return x is >= 1 and <= Cols - 2 && y is >= 1 and <= Rows - 2 && !(y == Rows - 2 && (x & 1) == 0);
        if (!OnBoard(x, y)) return false;
        return !avoidReserved || ((_marks[x, y] & 8) == 0 && ShipAt(x, y) is null);
    }

    /// <summary>사람이 찍을 수 있는 끝 칸들 — 같은 칸이면 가장 짧은 길 하나만 남긴다(모드 0).</summary>
    public List<(List<Move> Plan, int X, int Y, int Way)> Options(Ship ship)
    {
        var list = new List<(List<Move>, int, int, int)>();
        if (!ship.CanAct || ship.Stuck) return list;
        var seen = new HashSet<(int, int)>();
        foreach (var option in Paths(ship, avoidReserved: false, avoidDanger: false))
            if (seen.Add((option.X, option.Y))) list.Add(option);
        return list;
    }

    /// <summary>지시를 적는다. 빈 걸음이면 「이동하지 않습니다」다.</summary>
    public void Order(Ship ship, IEnumerable<Move> plan)
    {
        ship.Plan.Clear();
        ship.Pivot = Move.Straight;
        if (!ship.Stuck) ship.Plan.AddRange(plan.Take(ship.Power));
        ship.Ordered = true;
    }

    /// <summary>
    /// <b>제자리 선회</b> 칸 둘 — 뱃머리 오른앞(방향+1)과 왼앞(방향+5)이다
    /// (<c>0x0043E6B8</c>~<c>0x0043E777</c> 이 배 <c>+0xA8</c>·<c>+0xB0</c> 에 세운다).
    /// </summary>
    public List<(int X, int Y, Move Turn)> Pivots(Ship ship)
    {
        var list = new List<(int, int, Move)>();
        if (!ship.CanAct || ship.Stuck) return list;
        foreach (var turn in new[] { Move.TurnRight, Move.TurnLeft })
        {
            var (x, y) = Step(ship.X, ship.Y, Turn(ship.Way, turn));
            if (OnBoard(x, y)) list.Add((x, y, turn));
        }
        return list;
    }

    /// <summary>
    /// 제자리 선회를 적는다(<c>0x0043E92B</c>) — <b>걸음 수 0</b> 에 선회만 1 또는 2 다.
    /// 한 칸도 안 가고 뱃머리만 돌린다.
    /// </summary>
    public void OrderPivot(Ship ship, Move turn)
    {
        ship.Plan.Clear();
        ship.Pivot = ship.Stuck ? Move.Straight : turn;
        ship.Ordered = true;
    }

    /// <summary>
    /// 계획을 마친다(<c>0x0043DDD0</c>) — 지시 안 한 배는 제자리로 두고, 적의 길을 짠다(<c>0x0043B710</c>).
    /// </summary>
    public void EndPlanning()
    {
        // 위임 중이면 아군 길도 같은 AI 가 짠다(0x0043B730 의 바깥 되돌이가 한 바퀴 더 돈다).
        if (Delegated) PlanSide(mine: true);

        foreach (var ship in Ships.Where(s => s.Mine && s.CanAct && !s.Ordered))
        {
            ship.Plan.Clear();
            ship.Ordered = true;
        }
        PlanSide(mine: false);
    }

    // ── 부관 위임 — +0x944 ────────────────────────────────────────────────

    /// <summary>
    /// 부관에게 <b>전투 지휘를 맡겼는가</b>(<c>+0x944</c> 가 2).
    /// </summary>
    /// <remarks>
    /// 칸 값은 0 부관 없음 · 1 제독이 직접 · 2 위임 중 · 3 방금 풀었음(턴이 넘어가면 1 로
    /// 돌아간다)이다. 세우는 곳은 <c>0x00441CFA</c> 고, 켜는 곳은 <b>차림표 줄이 아니라
    /// 물음</b> 둘이다.
    /// <code>
    ///   0x0043C586  턴이 시작될 때   「제독, 이번에는 저에게 맡겨 주십시오!」       0x0056B4A8
    ///   0x0043DEEB  계획을 안 끝낼 때「한번 더 부관에게 전투 지휘를 위임하겠습니까?」0x0056B5C8
    /// </code>
    /// 위임 중에 아무 데나 누르면 「제독이 명령하시겠습니까?」(<c>0x0056AF28</c>)로 되찾는다.
    ///
    /// 켜 두면 <b>아군 여덟 칸도 적과 같은 AI 가 짜고</b>(<c>0x0043B730</c>), 바람 안내와
    /// 이동 지시 재촉 대사가 안 나온다(<c>0x0043C5DE</c>).
    /// </remarks>
    public bool Delegated { get; set; }

    /// <summary>부관이 맡겠다고 나서는 말(<c>0x0056B4A8</c>).</summary>
    public const string OfferToLead = "제독, 이번에는 저에게 맡겨 주십시오!";

    /// <summary>계획을 안 끝내고 물릴 때 다시 묻는 말(<c>0x0056B5C8</c>).</summary>
    public const string OfferAgain = "한번 더 부관에게 전투 지휘를 위임하겠습니까?";

    /// <summary>위임 중에 손을 대면 되찾겠냐고 묻는 말(<c>0x0056AF28</c>).</summary>
    public const string TakeBack = "제독이 명령하시겠습니까?";

    // ── 적의 길 — 0x0043B710 ─────────────────────────────────────────────

    /// <summary>
    /// 한 편 여덟의 길을 짠다. 먼저 짠 배가 지나갈 칸을 표시해(+8) 뒤의 배가 비켜 가게 한다.
    /// </summary>
    private void PlanSide(bool mine)
    {
        Array.Clear(_marks);
        // 괴물 몸은 따로 짜지 않는다 — 원본은 머리 길을 몸 칸 9~15 에 베끼지만(0x0043BD0C) 몸은 판 되돌이에서
        // 건너뛰고(0x0043CB2D) 머리가 옮길 때 둘레에 다시 깔 뿐이다(LayMonsterBody).
        var side = Ships.Where(s => s.Mine == mine && s.CanAct && !s.Body).OrderBy(s => s.Index).ToList();
        var foes = Ships.Where(s => s.Mine != mine && s.CanAct).ToList();

        foreach (var ship in side)
        {
            ship.Plan.Clear();
            if (ship.Stuck) { ship.Ordered = true; continue; }       // 지난 턴 충돌 — 걸음 0

            List<Move>? plan = null;
            bool aimed = false;                                      // 노릴 자리(+0x8C4)를 채웠는지

            if (WantsRetreat(ship, mine))
            {
                if (IsRetreatCell(ship.X, ship.Y))
                {
                    // 퇴각 지대에 서 있으면 판을 뜬다 — 편을 가리지 않는다(+0x8DC 로 보고, 0x0043B91C 의
                    // cmp edx,8 ; jl 0x43BA19 로 위임한 아군도 상태 3 이 된다). 예전에는 적만 떴다.
                    ship.State = ShipState.Retreated;
                    ship.Ordered = true;
                    NoteFlag(ship);
                    continue;
                }

                // 살아 있는 상대 배 둘레(X±3 · Y±2)를 위험 칸으로 칠하고 가장자리 쪽 길을 찾는다.
                foreach (var foe in foes)
                    for (int dx = -3; dx <= 3; dx++)
                        for (int dy = -2; dy <= 2; dy++)
                        {
                            // 판 밖은 건너뛰지 않고 <b>가장자리로 자른다</b>(0x0043BA66~0x0043BAA1
                            // 의 clamp(x,0,0x16)·clamp(y,0,0x10)) — 그래서 가장자리 칸이 겹쳐 칠해진다.
                            int cx = Math.Clamp(foe.X + dx, 0, Cols - 1);
                            int cy = Math.Clamp(foe.Y + dy, 0, Rows - 1);
                            _marks[cx, cy] |= 4;
                        }

                plan = TowardEdge(ship);

                for (int x = 0; x < Cols; x++)
                    for (int y = 0; y < Rows; y++)
                        _marks[x, y] &= ~4;
            }
            else if (PickTarget(ship, foes) is { } target)
            {
                var (ax, ay) = AimPoint(target, aimer: ship);
                aimed = true;
                // 괴물 머리는 뱃전(모드 3) 대신 모드 6 이다(0x0043BC68 — 칸 8 이고 +0x8FC > 0 이면 push 6).
                plan = Monster && ship.Index == PerSide ? MonsterApproach(ship, ax, ay) : Broadside(ship, ax, ay);
            }
            else
            {
                plan = TowardFlagship(ship, foes);
            }

            // 길을 못 찾으면 걸음은 0 이고 선회 하나만 굴린다 — rand(3) 을 첫 걸음 칸에 넣고 걸음 수 0
            // (0x0043BC92~0x0043BCC4)이라 틱 0 에 제자리 선회가 된다(0x0043CCD7). 이 굴림은 노릴 자리 +0x8C4 가
            // −1 로 남았을 때, 곧 물러서기·기함 쪽 길(모드 2·4·5)에서만 탄다 — 뱃전 모드 3 은 노릴 자리를 채워 두어
            // 안 탄다. 예전에는 선회도 없이 섰다.
            if (plan == null)
            {
                ship.Plan.Clear();
                if (!aimed) ship.Pivot = (Move)_rng.Next(3);
            }
            else
            {
                ship.Plan.AddRange(plan);
                // 지나갈 칸을 +8 로 잡아 둔다(0x0043B5B0).
                var path = Trace(ship.X, ship.Y, ship.Way, plan);
                if (path != null)
                    foreach (var (px, py, _) in path) _marks[px, py] |= 8;
            }
            ship.Ordered = true;
        }

        Array.Clear(_marks);
    }

    /// <summary>
    /// <b>바다 괴물과의 판</b>인지(원본 판 종류 0). 괴물은 달아나지 않는다.
    /// </summary>
    /// <remarks>
    /// 발견 대본이 거는 해전(<c>0D 0D [인물]</c>)이 이 갈래다 — 크라켄·시서펜트·식인상어·맨터.
    /// 괴물이 「제독이 무서워서 도망간 것 같군요」 하고 사라지면 발견이 그대로 물거품이 된다.
    /// </remarks>
    public bool Monster { get; set; }

    /// <summary>
    /// 부관 성미 칸 3 — 위임했을 때 아군이 물러서는 잣대다(<c>0x0043B7B1</c>).
    /// 부관이 없거나 못 찾으면 1(여느 판정)이다.
    /// </summary>
    public int MateTemper { get; set; } = 1;

    /// <summary>
    /// 괴물 인물 번호(<c>0x0044307B</c>) — 271 크라켄 · 272 시서펜트 · 273 식인 상어 · 274 맨터.
    /// 괴물 판이 아니면 −1 이다.
    /// </summary>
    public int MonsterPerson { get; set; } = -1;

    /// <summary>
    /// 괴물의 이동력(<c>0x00434CB5</c>) — 그림 갈래(<c>+0x900</c>)로 갈린다.
    /// 식인 상어 6 · 시서펜트 5 · 크라켄과 맨터 4 다. 돛 셈을 아예 안 탄다.
    /// </summary>
    private int MonsterPower => MonsterPerson switch { 273 => 6, 272 => 5, _ => 4 };

    /// <summary>
    /// 괴물이 <b>떠올라 있는가</b>(<c>+0x8FC</c> 가 2 면 참, 1 이면 잠수).
    /// </summary>
    /// <remarks>
    /// 괴물 판은 <c>+0x8FC</c> 를 <b>1(잠수)</b> 로 세우고 시작한다(<c>0x00440EAF</c>).
    /// 턴이 끝날 때마다 한 번 굴려 떠오르고 잠기며(<see cref="MonsterRises"/>), 3·4 는 그
    /// 사이 한 틱짜리 연출이라 우리는 안 쓴다.
    /// </remarks>
    public bool MonsterUp { get; set; }

    /// <summary>
    /// 괴물이 떠오르는가 — <c>rand(100) &lt; (담력 + 지력) / 2 − 20</c>(<c>0x0043DA8E</c>).
    /// </summary>
    /// <remarks>
    /// 두 값은 <b>제독과 부관 가운데 높은 쪽</b>에 1 을 더한 것이다(<c>0x00441DB6</c> ·
    /// <c>0x00441DE7</c>) — 능력표 <c>0x005B60C0</c> 의 넷째 칸(<b>운</b>)과 둘째 칸(<b>지력</b>)이다.
    /// 굴림에 <b>이기면 잠겨 있던 괴물이 떠오르고</b>, 지면 떠 있던 괴물이 잠긴다 —
    /// 한 턴에 한쪽만 걸린다.
    ///
    /// 원본은 이 값을 <b>부호 없이</b> 재므로(<c>0x004B7C62</c>) 0 밑으로 떨어지면 도리어
    /// 늘 참이 된다 — 담력·지력이 낮을수록 괴물이 늘 떠 있는 셈이다. 그대로 옮긴다.
    /// </remarks>
    public static bool MonsterRises(int daring, int wit, Random dice)
    {
        int odds = (daring + wit) / 2 - 20;
        return (uint)dice.Next(100) < (uint)odds;
    }

    /// <summary>
    /// 한 턴이 끝날 때 괴물이 잠기거나 떠오른다(<c>0x0043DA81</c>).
    /// </summary>
    /// <returns>막 잠겼으면 참 — 그때만 부관이 한마디 한다(<c>0x0043DBAE</c>).</returns>
    public bool TurnMonster(int daring, int wit)
    {
        if (!Monster) return false;

        bool rise = MonsterRises(daring, wit, _rng);
        if (rise && !MonsterUp) { MonsterUp = true; return false; }
        if (!rise && MonsterUp) { MonsterUp = false; return true; }
        return false;
    }

    /// <summary>
    /// 턴 끝 굴림을 판의 아군 값으로 한다 — <c>[+0x910]</c>(운+1) 과 <c>[+0x908]</c>(지력+1), 둘 다 제독·부관 가운데 큰 값이다.
    /// </summary>
    /// <remarks>예전에는 창이 제독 날값(+1 도, 부관 견주기도 없이)을 넘겼다.</remarks>
    public bool TurnMonster() => TurnMonster(MineSide.Defense, MineSide.Mind);

    /// <summary>
    /// 물러설 배인지 — 내구 10 이하, 승원이 필요승원(선체표 +0x34 + 10) 이하, 또는 아군 수/3 이 적 수 이상.
    /// </summary>
    /// <remarks>
    /// <b>괴물은 잠수해 있을 때만</b> 물러설 마음을 먹는다 — 내 배 가운데 격침·나포된 것이
    /// 하나라도 있으면 가장자리로 간다(<c>0x0043B7A1</c>: <c>+0x8FC == 1</c> 이고 내 배
    /// 1~7 에 상태 1·2 가 있을 때). 떠 있을 때(<c>+0x8FC == 2</c>)는 여느 내구·승원·척수
    /// 판정을 그대로 탄다(<c>0x0043B79A</c> → <c>0x0043B7AA</c> → <c>0x0043B7F5</c> → <c>0x0043B84E</c>).
    /// 다만 괴물 판의 적 척수는 <c>[0x848] = 7</c>(<c>0x00440EB5</c>)이라 척수 판정은 걸리지 않는다.
    /// 예전에는 떠 있는 괴물은 아예 물러서지 않았다.
    /// </remarks>
    private bool WantsRetreat(Ship ship, bool mine)
    {
        if (!mine && Monster && !MonsterUp)
            return Ships.Any(s => s.Mine && s.State is ShipState.Sunk or ShipState.Captured);

        // 위임했을 때 아군은 <b>부관 성미 칸 3</b> 으로 셋으로 갈린다(0x0043B7B1 · 0x0043B807).
        //   0        내 배 가운데 격침·나포된 것이 있으면 물러선다(0x0043B7C8)
        //   1        여느 판정(내구·승원·척수)
        //   그 밖    그 배에 대포가 없거나 함대 탄약이 0 이면 물러선다(0x0043B829)
        if (mine && Delegated)
        {
            if (MateTemper == 0)
                return Ships.Any(s => s.Mine && s.State is ShipState.Sunk or ShipState.Captured);
            if (MateTemper != 1)
                return ship.Guns == 0 || Ammo <= 0;
        }

        int ours = Ships.Count(s => s.Mine && s.CanAct);
        int theirs = Ships.Count(s => !s.Mine && s.CanAct);
        if (!mine && Monster) theirs = 7;           // 괴물 판 적 척수 [0x848] = 7(0x00440EB5)
        int enemyCount = mine ? ours : theirs;      // 배의 편
        int opposing = mine ? theirs : ours;        // 상대 편
        // 승원 문턱은 선체표 +0x34 에 10 을 더한 값이다(0x0043B866~0x0043B88A 의 [0x4FC214+종류*64]+10 >= 승원).
        // MinCrew 가 이미 그 값(필요승원 = +0x34 + 10)이라 더 얹지 않는다 — 예전에는 10 을 또 더해 필요승원보다 10명 많을 때 벌써 물러섰다.
        return ship.Hp <= 10 || ship.Crew <= ship.MinCrew || opposing / 3 >= enemyCount;
    }

    /// <summary>
    /// 노릴 배(<c>0x0043A880(5, 0, 편)</c>) — 걸음 1 부터 5 까지 고리를 넓혀, 처음 걸린 고리에서
    /// <b>기함이면 곧장</b>, 아니면 내구가 가장 낮은 배를 고른다. 없으면 null.
    /// </summary>
    private Ship? PickTarget(Ship ship, IReadOnlyList<Ship> foes)
    {
        for (int ring = 1; ring <= 5; ring++)
        {
            var hits = foes.Where(f => BfsDistance(ship.X, ship.Y, f.X, f.Y) == ring).ToList();
            if (hits.Count == 0) continue;
            return hits.FirstOrDefault(f => f.Flagship) ?? hits.OrderBy(f => f.Hp).First();
        }
        return null;
    }

    /// <summary>
    /// 노릴 자리 — 상대가 역풍이 아니면 바람 쪽으로 n 칸 앞질러 본다(<c>0x0043B7xx</c>).
    /// </summary>
    /// <remarks>
    /// <code>
    ///   n = max(0, rand(2) + 상대이동력/2 − 1)
    ///   바람 0: Y −= n     바람 3: Y += n
    ///   X 짝수 && 바람 2·4: Y += (n+1)/2      X 홀수 && 바람 1·5: Y += (−1−n)/2
    ///   바람 4 이상: X −= n     바람 1·2: X += n
    ///   짝수 X 에 Y 16 이면 상대 자리 그대로
    /// </code>
    /// </remarks>
    private (int X, int Y) AimPoint(Ship target, Ship? aimer = null)
    {
        int ax = target.X, ay = target.Y;
        if ((target.Way - Wind + Ways) % Ways == 3) return (ax, ay);

        // 괴물이 겨눌 때는 앞지르는 칸 수가 <b>늘 2</b> 다(0x0043BBB2).
        bool monsterAims = Monster && aimer is { Mine: false };
        int n = monsterAims ? 2 : Math.Max(0, _rng.Next(2) + target.Power / 2 - 1);
        if (Wind == 0) ay -= n;
        if (Wind == 3) ay += n;
        if ((ax & 1) == 0 && Wind is 2 or 4) ay += (n + 1) / 2;
        if ((ax & 1) == 1 && Wind is 1 or 5) ay += (-1 - n) / 2;
        if (Wind >= 4) ax -= n;
        if (Wind is 1 or 2) ax += n;
        // 판 밖으로 나가도 그대로 둔다 — 원본은 (짝수 X && Y == 16) 일 때만 제자리로
        // 되돌리고 그 밖에는 자르지 않는다(0x0043BC33).
        if ((ax & 1) == 0 && ay == Rows - 1) return (target.X, target.Y);
        return (ax, ay);
    }

    /// <summary>대포 사거리(<c>0x0043699D</c> 벌) — 캘버린 4 · 카논 2 · 그 밖 3.</summary>
    public static int RangeOf(int gun) => gun switch { 1 => 4, 3 => 2, _ => 3 };

    /// <summary>
    /// 모드 3 — 끝 칸에서 노릴 자리가 <b>뱃전</b> 거리 2~사거리에 들어오는 첫 길을 고른다
    /// (<c>0x0043AF72</c>~<c>0x0043B1E6</c>). 판정은 <see cref="AimInBroadside"/> 다.
    /// </summary>
    /// <remarks>
    /// 그런 길이 없으면 <b>아무것도 적지 않는다</b> — 모드 3 은 가장 좋은 길을 따로 쥐지 않고 그냥 끝난다
    /// (<c>0x0043B1E6</c> → <c>0x0043B4F6</c>). 걸음 수는 턴 끝(<c>0x0043D80C</c>)에 비워 둔 0 이고, 노릴 자리
    /// <c>+0x8C4</c> 가 채워져 있어 선회 굴림(<c>0x0043BC92</c>)도 안 탄다 — 그 배는 가만히 선다.
    /// 예전에는 노릴 자리에 가장 가까워지는 길로 다가갔다.
    /// </remarks>
    private List<Move>? Broadside(Ship ship, int ax, int ay)
    {
        int range = RangeOf(ship.Gun);
        foreach (var (plan, x, y, way) in Paths(ship, avoidReserved: true, avoidDanger: false))
            for (int d = 2; d <= range; d++)
                if (AimInBroadside(x, y, way, ax, ay, d)) return plan;
        return null;
    }

    /// <summary>
    /// 모드 6 — 괴물 머리가 끝 칸에서 노릴 자리를 <b>제 손 닿는 고리</b>(<see cref="InMonsterReach"/>)에 두는 첫 길을 고른다
    /// (<c>0x0043B1EB</c>~<c>0x0043B2C4</c>). 고르면 걸음을 칸 8~15 에 베낀다(<c>0x0043B568</c>).
    /// </summary>
    /// <remarks>
    /// 끝 칸 + (dX, dY) 가 노릴 자리와 같으면 된다. 고리의 홀짝은 끝 칸이 아니라 <b>괴물의 지금 X</b>(<c>[+0x588]</c>)로 본다 —
    /// 원본 버릇 그대로 옮긴다. 길 후보는 칸 차지를 안 보고 X 1~21 · Y 1~15 안에서만 센다(<see cref="StepFree"/>).
    /// 그런 길이 없으면 아무것도 안 적는다 — 노릴 자리가 채워져 있어 선회 굴림도 안 탄다.
    /// </remarks>
    private List<Move>? MonsterApproach(Ship ship, int ax, int ay)
    {
        bool even = (ship.X & 1) == 0;
        foreach (var (plan, x, y, _) in Paths(ship, avoidReserved: true, avoidDanger: false))
            for (int dx = -2; dx <= 2; dx++)
                for (int dy = -2; dy <= 2; dy++)
                    if (InMonsterReach(dx, dy, even) && x + dx == ax && y + dy == ay) return plan;
        return null;
    }

    /// <summary>
    /// 괴물 머리에서 (dX, dY) 가 <b>몸 바깥 한 겹</b>(열두 칸)인지 — 괴물이 총격으로 붙잡는 칸이다
    /// (<c>0x0043634F</c>~<c>0x004363E2</c> · 모드 6 의 <c>0x0043B1F6</c>~<c>0x0043B28C</c>, 두 곳이 같은 셈이다).
    /// </summary>
    /// <remarks>
    /// <code>
    ///   |dX·dY| == 4 는 뺀다(네 귀퉁이)
    ///   짝수 X   |dX|&lt;2 · |dY|&lt;2 면 (±1, −1) 만 남긴다 ; dY == 2 이고 dX ≠ 0 이면 뺀다
    ///   홀수 X   |dX|&lt;2 · |dY|&lt;2 면 (±1, +1) 만 남긴다 ; dY == −2 이고 dX ≠ 0 이면 뺀다
    /// </code>
    /// 걸음 거리 2 고리와 꼭 같지는 않다 — 짝수 X 에서 (±1, −2)(거리 3)를 들이고 (±1, +2)(거리 2)를 빼며,
    /// 홀수 X 는 그 거울이다. 원본 셈 그대로 옮긴다.
    /// </remarks>
    public static bool InMonsterReach(int dx, int dy, bool evenX)
    {
        if (Math.Abs(dx * dy) == 4) return false;
        int adx = Math.Abs(dx);
        if (adx < 2 && Math.Abs(dy) < 2 && !(adx == 1 && dy == (evenX ? -1 : 1))) return false;
        if (dy == (evenX ? 2 : -2) && dx != 0) return false;
        return true;
    }

    /// <summary>
    /// 포격 과녁 칸(<c>0x004369E0</c>~<c>0x00436D99</c>) — 쏘는 배에서 걸음 거리 <paramref name="d"/> 인 고리 가운데
    /// <b>옆면 두 변</b>이다. 뱃머리가 w 면 (w+1)·(w+2) 모서리 사이 변과 (w+4)·(w+5) 모서리 사이 변이고 칸은 2(d+1) 이다.
    /// </summary>
    /// <remarks>
    /// 셈은 원본 그대로 옮긴다(나눗셈은 0 쪽 버림, 홀짝은 <b>쏘는 배 X</b>).
    /// <code>
    ///   뱃머리 0·3   X = sx ± d ,  Y = (sx 짝수 ? sy − d/2 : sy + (−1−d)/2) + k      k 0..d
    ///   그 밖        X = sx + k*e (e = ±1) ,  q = (sx &amp; 1) + e
    ///     뱃머리 1·4   q 가 −1·2 면 Y = (d + (−1−k)/2)*e + sy , 아니면 (d − k/2)*e + sy
    ///     뱃머리 2·5   q 가 −1·2 면 Y = (k/2 − d)*e + sy ,     아니면 ((k+1)/2 − d)*e + sy
    /// </code>
    /// 예전에는 고리에서 이물·고물 줄만 뺀 6d−2 칸을 과녁으로 삼아 앞뒤 사선의 배도 쐈다.
    /// </remarks>
    public static bool InBroadside(int sx, int sy, int way, int tx, int ty, int d)
    {
        int w = ((way % Ways) + Ways) % Ways;
        for (int e = -1; e <= 1; e += 2)
            for (int k = 0; k <= d; k++)
            {
                int x, y;
                if (w is 0 or 3)
                {
                    x = sx + e * d;
                    y = ((sx & 1) == 0 ? sy - d / 2 : sy + (-1 - d) / 2) + k;
                }
                else
                {
                    x = sx + k * e;
                    bool q = ((sx & 1) + e) is -1 or 2;
                    y = w % 3 == 1
                        ? (q ? (d + (-1 - k) / 2) * e + sy : (d - k / 2) * e + sy)
                        : (q ? (k / 2 - d) * e + sy : ((k + 1) / 2 - d) * e + sy);
                }
                if (x == tx && y == ty) return true;
            }
        return false;
    }

    /// <summary>
    /// AI 모드 3 의 뱃전 판정(<c>0x0043AFF4</c>~<c>0x0043B1CA</c>) — 끝 칸(ex, ey, 방향 w)에서 노릴 자리가 거리 d 뱃전에 드는지.
    /// </summary>
    /// <remarks>
    /// <see cref="InBroadside"/> 와 같은 꼴이지만 <b>노릴 자리에서 끝 칸 쪽으로</b> 셈하고 홀짝은 <b>끝 칸 X</b> 로 본다.
    /// 그래서 대각 방향이면 홀수 열에서 Y 가 한 칸 어긋난 칸이 섞인다 — 원본 버릇 그대로 옮긴다.
    /// </remarks>
    private static bool AimInBroadside(int ex, int ey, int way, int ax, int ay, int d)
    {
        int w = ((way % Ways) + Ways) % Ways;
        for (int e = -1; e <= 1; e += 2)
            for (int k = 0; k <= d; k++)
            {
                int x, y;
                if (w is 0 or 3)
                {
                    x = ax + e * d;
                    y = ((ex & 1) == 0 ? ay - d / 2 : ay + (-1 - d) / 2) + k;
                }
                else
                {
                    x = ax + k * e;
                    bool q = ((ex & 1) + e) is -1 or 2;
                    y = w % 3 == 1
                        ? (q ? (d + (-1 - k) / 2) * e + ay : (d - k / 2) * e + ay)
                        : (q ? (k / 2 - d) * e + ay : ((k + 1) / 2 - d) * e + ay);
                }
                if (x == ex && y == ey) return true;
            }
        return false;
    }

    /// <summary>모드 4 — 노릴 배가 없으면 <b>0번 배</b>(아군 기함)에 |dX|+|dY| 가 가장 작아지는 길.</summary>
    /// <remarks>
    /// 셈 자리가 <c>[해전+0x2E8]</c>·<c>[+0x2EC]</c>, 곧 0번 배 자리로 박혀 있다(<c>0x0043B2E1</c>~<c>0x0043B329</c>).
    /// 적에게는 상대 기함이지만 위임한 아군에게는 <b>제 기함</b>이다 — 원본 그대로 옮긴다. 상태도 안 본다.
    /// 예전에는 위임한 아군이 적 기함(8번) 쪽으로 갔다.
    /// </remarks>
    private List<Move>? TowardFlagship(Ship ship, IReadOnlyList<Ship> foes)
    {
        var flag = At(0);
        if (flag == null) return null;
        List<Move>? pick = null;
        int best = int.MaxValue;
        foreach (var (plan, x, y, _) in Paths(ship, avoidReserved: true, avoidDanger: false))
        {
            int score = Math.Abs(x - flag.X) + Math.Abs(y - flag.Y);
            if (score < best) { best = score; pick = plan; }
        }
        return pick;
    }

    /// <summary>
    /// 모드 2·5 — 퇴각 가장자리에 가장 가까워지는 길(<c>0x0043B336</c>~<c>0x0043B414</c> · <c>0x0043B4A6</c>).
    /// </summary>
    /// <remarks>
    /// 모드 2 는 위험 칸(+4)도 피한다. 한 걸음 수를 다 늘어놓은 뒤 <b>마지막 조합이 실패했고 그때까지 위험 칸에
    /// 한 번이라도 막혔으면</b> 모드 5(위험 무시)로 바꿔 걸음 수 1 부터 다시 센다 — 그때까지 쥔 가장 좋은 길은 그대로 둔다
    /// (<c>0x0043B4A6</c>~<c>0x0043B4CD</c>). 새 길은 아래가 참일 때만 갈아 든다(처음 찾은 길은 그냥 든다).
    /// <code>
    ///   바람 0     새 Y &lt; 쥔 Y ,  또는 새 Y == 0  이고 |새X−10| &lt; |쥔X−10|
    ///   바람 1·2   새 X &gt; 쥔 X ,  또는 새 X == 22 이고 |새Y−7|  &lt; |쥔Y−7|
    ///   바람 3     새 Y &gt; 쥔 Y ,  또는 새 Y == 16 이고 |새X−10| &lt; |쥔X−10|
    ///   바람 4·5   새 X &lt; 쥔 X ,  또는 새 X == 0  이고 |새Y−7|  &lt; |<b>쥔X</b>−7|    ← 원본이 쥔 X 를 쓴다(0x0043B3FF)
    /// </code>
    /// 예전에는 안전한 길이 하나도 없을 때만 위험을 무시했고, 버금 잣대를 가장자리 줄이 아니어도 썼다.
    /// </remarks>
    private List<Move>? TowardEdge(Ship ship)
    {
        List<Move>? pick = null;
        int bx = 0, by = 0;
        bool avoidDanger = true, dangerHit = false;

        for (int len = 1; len <= ship.Power; len++)
        {
            bool lastFailed = false;
            int total = (int)Math.Pow(3, len);
            for (int code = 0; code < total; code++)
            {
                var plan = new List<Move>(new Move[len]);
                int rest = code;
                for (int i = len - 1; i >= 0; i--) { plan[i] = (Move)(rest % 3); rest /= 3; }

                int x = ship.X, y = ship.Y, way = ship.Way;
                bool ok = true;
                foreach (var move in plan)
                {
                    way = Turn(way, move);
                    (x, y) = Step(x, y, way);
                    if (!StepFree(ship, x, y, avoidReserved: true)) { ok = false; break; }
                    if (avoidDanger && (_marks[x, y] & 4) != 0) { dangerHit = true; ok = false; break; }
                }
                lastFailed = !ok;
                if (!ok) continue;

                bool better = pick == null || RetreatWind switch
                {
                    0 => y < by || (y == 0 && Math.Abs(x - 10) < Math.Abs(bx - 10)),
                    1 or 2 => x > bx || (x == Cols - 1 && Math.Abs(y - 7) < Math.Abs(by - 7)),
                    3 => y > by || (y == Rows - 1 && Math.Abs(x - 10) < Math.Abs(bx - 10)),
                    _ => x < bx || (x == 0 && Math.Abs(y - 7) < Math.Abs(bx - 7)),
                };
                if (better) { pick = plan; bx = x; by = y; }
            }

            if (avoidDanger && lastFailed && dangerHit)
            {
                avoidDanger = false;
                dangerHit = false;
                len = 0;
            }
        }
        return pick;
    }

    // ── 실행 ──────────────────────────────────────────────────────────────

    /// <summary>
    /// 판이 일마다 부르는 화면 쪽 — 셈은 판이 하고, 소리·그림·말·고르기는 받는 쪽이 한다.
    /// </summary>
    /// <remarks>
    /// 원본은 충돌·백병전·나포·일기토를 <b>걸음을 딛는 그 자리에서</b> 곧바로 치른다(<c>0x0043D374</c>).
    /// 「배를 뺏앗는다」·「일기토」·「응한다」가 다음 배의 걸음보다 먼저 정해져야 해서, 박자가 끝난 뒤 모아
    /// 그리던 옛 방식 대신 일이 날 때마다 부른다. 받는 쪽이 없으면 고르기는 모두 「대기」, 결투는 안 열린다.
    /// </remarks>
    public interface IStage
    {
        /// <summary>하위단계 0 — 모든 배가 걸음 하나를 디딘 뒤.</summary>
        void Moved();

        /// <summary>충돌(<c>0x004397E0</c>) — 적이 끼면 소리 0x2C, 알림 「충돌했다!」/「위험하다! 정지!…」.</summary>
        void Crash(Ship mover, Ship hit, bool friendly);

        /// <summary>충돌 내구 피해 숫자 — 기다림 2 · 숫자(제 배 위에 제 잃은 값) · 기다림 2.</summary>
        void HullLoss(Ship mover, int moverLoss, Ship hit, int hitLoss);

        /// <summary>백병전 연출 갈래 3(<c>0x00437A94</c>) — 소리 0x2D · 맞은편 칸 blast-09~11 · 잃는 승원.</summary>
        void Melee(Ship mover, Ship target, int moverLoss, int targetLoss);

        /// <summary>불이 붙었다(<c>0x00437E3A</c>) — 소리 0x30 · 맞은편 칸 blast-03~05.</summary>
        void Ignite(Ship target);

        /// <summary>총격전 연출 갈래 4(<c>0x00437FDF</c>) — 소리 0x1E · 두 배 가운데 blast-09~11 · 숫자 둘.</summary>
        void Gunfight(Ship shooter, Ship target, int shooterLoss, int targetLoss);

        /// <summary>잠수폭탄을 썼다 — 「다 빈치선생님의 수중기뢰를 씁시다.」(<c>0x0056AE60</c>), 소지품에서 뺀다.</summary>
        void Mine();

        /// <summary>속사포를 꺼낸다(<c>0x004384E8</c>) — 판마다 한 번, 부관 얼굴로 「속사포를 씁시다.」.</summary>
        void RapidFire();

        /// <summary>포격 한 번(<c>0x004384E8</c>).</summary>
        void Volley(Volley volley);

        /// <summary>얼굴 없는 「해전」 알림.</summary>
        void Notice(string text);

        /// <summary>가라앉음 연출 갈래 1(<c>0x004375F5</c>) — 소리 0x2E · blast-12~14 · 기함 아닌 배의 격침 말.</summary>
        void Sink(IReadOnlyList<Ship> ships);

        /// <summary>승원 0·나포(<c>0x004358EE</c>) — 불꽃·소리 없이 기함 아닌 배의 말만.</summary>
        void Captured(Ship ship);

        /// <summary>「배를 뺏앗는다 / 대기」(<c>0x0056B048</c>) — 뺏으면 true.</summary>
        bool AskCapture(Ship target);

        /// <summary>「일기토 / 대기」(<c>0x0056B060</c>) 뒤 적장 말 — 결투를 열면 true.</summary>
        bool OfferDuel();

        /// <summary>결투 신청(<c>0x0043A567</c>) — 부관 말 · 「응한다 / 거절한다」 · 적장 말. 응하면 true.</summary>
        bool Challenged();

        /// <summary>결투 판(<c>0x004AA700(적장, 0, 0, −1)</c>). 이기면 true, 지면 false, 못 열면 null.</summary>
        bool? Duel();

        /// <summary>턴 끝 불 피해 — 그 배 위에 숫자 3.</summary>
        void Burn(Ship ship);

        /// <summary>박자 하나가 끝났다.</summary>
        void BeatDone(int beat);
    }

    private IStage? _stage;

    /// <summary>한 턴의 틱 수(<c>0x0043CA60</c> 의 되돌이) — 걸음과 사격이 이 눈금에 흩어진다.</summary>
    public const int Ticks = 60;

    /// <summary>
    /// 한 턴을 실행한다(<c>0x0043CA60</c>).
    /// </summary>
    /// <remarks>
    /// <code>
    ///   틱 0..59 x 하위단계 0..3 x 배 i (번호 차례)
    ///     상태 &lt; 4 → 건너뜀 ;  하위단계 0·1 이고 지시상태 ≥ 2 → 건너뜀
    ///     0 걸음 — 앞 칸에 산 배가 있으면 충돌 → 백병전 → 불 → 나포·일기토 (그 자리에서)
    ///     1 총격 0x004362E0 — 이웃 칸 맞은편 배(기함 먼저)
    ///     2 포격 0x00436900
    ///     3 턴 끝 (틱 59)
    ///   하위단계 1·2 는 (이동력*(틱+1)) % 60 == 0 틱에만 — 곧 한 턴에 이동력 번
    /// </code>
    /// 틱을 그대로 돈다 — 걸음 둘짜리 배는 틱 29·59 에, 여섯짜리는 9·19·29·39·49·59 에 딛는다.
    /// 걸음 수와 이동력이 서로 다른 눈금이라 <b>배마다 딛는 때가 어긋난다</b> — 걸음이 짧은 배는
    /// 턴 끝에서야 움직인다. 번호가 낮은 배가 먼저 칸을 차지하면 뒤에 오는 배가 거기로
    /// 들어가려다 충돌한다.
    /// </remarks>
    public void Execute(IStage? stage = null)
    {
        _stage = stage;

        // 제자리 선회는 걸음이 없으므로 맨 앞에 한 번만 먹인다(걸음 수 0 · 선회 1·2).
        foreach (var ship in Ships)
            if (ship.CanAct && ship.Pivot != Move.Straight)
            {
                ship.Way = Turn(ship.Way, ship.Pivot);
                ship.Pivot = Move.Straight;
            }
        _stage?.Moved();

        int beat = 0;
        for (int tick = 0; tick < Ticks && !Over; tick++)
        {
            bool stirred = false;

            // 하위단계 0 — 걸음. (틱+1) x 걸음수 가 60 의 배수인 틱에만 한 걸음 딛는다(0x0043CBE7).
            foreach (var ship in Ships.ToList())
            {
                if (Over) break;
                if (!ship.CanAct || ship.Halted || ship.Blocked || ship.Body) continue;
                int steps = ship.Plan.Count;
                if (steps == 0 || (tick + 1) * steps % Ticks != 0) continue;
                int k = (tick + 1) * steps / Ticks - 1;
                if (k < 0 || k >= steps) continue;

                stirred = true;
                int way = Turn(ship.Way, ship.Plan[k]);
                var (nx, ny) = Step(ship.X, ship.Y, way);
                ship.Way = way;                                   // 돌기는 이미 먹었다
                if (Monster && ship.Index == PerSide) { MonsterStep(ship, nx, ny); continue; }
                if (!OnBoard(nx, ny)) { ship.Blocked = true; continue; }
                if (ShipAt(nx, ny) is { } hit)
                {
                    Collide(ship, hit);                           // 그 칸에는 안 들어간다
                    continue;
                }
                ship.X = nx;
                ship.Y = ny;
            }
            if (stirred) _stage?.Moved();

            // 하위단계 1·2 는 (틱+1) x 이동력 이 60 의 배수인 틱에만 — 곧 한 턴에 이동력 번이다
            // (0x0043CBA4). 걸음 눈금과 달라 배마다 쏘는 때가 어긋난다.
            bool Fires(Ship ship) => ship.Power > 0 && (tick + 1) * ship.Power % Ticks == 0;

            // 하위단계 1 — 총격. 충돌한 배(지시상태 ≥ 2)는 먼저 걸지 않는다(걸리는 쪽으로는 맞는다).
            foreach (var ship in Ships.ToList())
            {
                if (Over) break;
                if (!ship.CanAct || ship.Halted || ship.Body || !Fires(ship)) continue;
                stirred = true;
                Gunfight(ship);
            }

            // 하위단계 2 — 포격. 지시상태를 안 본다(부딪힌 배도 쏜다).
            foreach (var ship in Ships.ToList())
            {
                if (Over) break;
                if (!ship.CanAct || ship.Body || !Fires(ship)) continue;
                stirred = true;
                if (Fire(ship) is not { } volley) continue;
                _stage?.Volley(volley);
                if (volley.Sunk) Sink([volley.Target]);           // 0x0043D79D → 연출 갈래 1
            }

            if (stirred) _stage?.BeatDone(beat++);
        }

        EndTurn();
        _stage = null;
    }

    /// <summary>
    /// 괴물 머리가 한 칸 딛는다(<c>0x0043CF65</c>~<c>0x0043D4C5</c>) — 일곱 칸짜리 몸이 나아갈 때 <b>새로 덮는 앞 세 칸</b>
    /// (새 머리의 방향−1 · 방향 · 방향+1 이웃)을 본다.
    /// </summary>
    /// <remarks>
    /// <code>
    ///   앞 세 칸에 선 산 배마다(칸 차례 0~5, 배 번호 차례) 충돌 — 여느 배와 달리 한 척에 걸려도 멈추지 않고 다 친다
    ///   부딪히지 않았으면(지시상태 1 그대로) 머리가 옮기고 몸 여섯이 그 둘레에 다시 깔린다(0x00439425)
    ///   잠수 중(+0x8FC == 1)에 부딪히면 잠깐 떠올라 보인다(0x0043D389 → 4, 연출이 끝나면 0x00439524 가 1 로 되돌린다)
    /// </code>
    /// 괴물 몸 칸은 괴물 제 몸이라 안 부딪힌다(<c>0x0043D356</c>).
    /// </remarks>
    private void MonsterStep(Ship head, int nx, int ny)
    {
        var cells = new List<(int X, int Y)>();
        for (int w = 0; w < Ways; w++)
        {
            int rel = ((w - head.Way) % Ways + Ways) % Ways;
            if (rel is 0 or 1 or 5) cells.Add(Step(nx, ny, w));
        }
        var hits = cells.SelectMany(c => Ships.Where(s => s.CanAct && s.Mine && s.X == c.X && s.Y == c.Y)).ToList();
        // 원본은 칸 차지 격자(+0x96C)로 칸을 먼저 추리고 그 칸 수를 [0x880] 에 센다 — 둘 이상이면 괴물이 입는
        // 선체 피해를 따로 모아 두는 갈래를 탄다(0x00439ABC).
        _monsterCrowd = cells.Count(c => Ships.Any(s => s.CanAct && s.X == c.X && s.Y == c.Y && !IsMonster(s)));
        foreach (var hit in hits)
        {
            if (Over || !head.CanAct) break;
            if (!hit.CanAct) continue;
            Collide(head, hit);
        }
        _monsterCrowd = 0;
        if (hits.Count > 0 || !head.CanAct) return;

        head.X = nx;
        head.Y = ny;
        LayMonsterBody();
    }

    /// <summary>괴물이 한 걸음에 부딪힌 칸 수(<c>[+0x880]</c>) — 충돌 피해 갈래가 본다.</summary>
    private int _monsterCrowd;

    /// <summary>
    /// 괴물이 여러 척을 한꺼번에 들이받을 때 따로 모아 두는 제 선체 피해(<c>[+0x884]</c>).
    /// </summary>
    /// <remarks>
    /// 원본은 과녁 X·Y 가 칸 목록의 <b>칸 수 번째</b>(쓰지 않은 빈 자리) 값과 같을 때만 모은 것을 머리 내구에서 깎는다
    /// (<c>0x00439AC1</c>~<c>0x00439B10</c>) — 사실상 안 깎인다. 그대로 옮긴다.
    /// </remarks>
    private int _monsterPooled;

    /// <summary>
    /// 잠수한 괴물이 부딪히거나 총격을 주고받는 동안 잠깐 보이는지(<c>+0x8FC</c> 4) — 연출이 끝나면 도로 잠긴다.
    /// </summary>
    private bool _monsterPeek;

    /// <summary>괴물 그림을 판에 찍을지 — 떠 있거나(2) 잠깐 떠오른 때(4)다(<c>0x0044073F</c> 의 짝수 판정).</summary>
    public bool MonsterShown => Monster && (MonsterUp || _monsterPeek);

    // ── 포격 — 0x00436900 ─────────────────────────────────────────────────

    /// <summary>
    /// 한 편 제독의 싸움 값(<c>0x00441D8A</c> 아군 · <c>0x00440F23</c> 적). 아군은 제독·부관 가운데 큰 값이다.
    /// </summary>
    /// <param name="Gunnery">포술 <c>+0x914</c>.</param>
    /// <param name="Might">무력 <c>+0x904</c>(능력+1).</param>
    /// <param name="Defense">운 <c>+0x910</c>(능력+1) — 포격 방어와 잠수폭탄 굴림.</param>
    /// <param name="Mind">지력 <c>+0x908</c>(능력+1) — 불 막기·나포.</param>
    /// <param name="Charm">매력 <c>+0x90C</c>(능력+1) — 나포 막기.</param>
    /// <param name="Sword">검술 <c>+0x918</c> — 백병전·불.</param>
    /// <param name="Shooting">사격술 <c>+0x91C</c> — 총격전.</param>
    /// <param name="Fortune">운세칸[0] <c>+0x920</c>(0~2) — 나포 막기·일기토 걸기.</param>
    public readonly record struct Side(int Gunnery, int Might, int Defense, int Mind = 0, int Charm = 0,
                                       int Sword = 0, int Shooting = 0, int Fortune = 0);

    /// <summary>아군 제독(<c>[0x904]</c>~).</summary>
    public Side MineSide { get; set; } = new(0, 50, 50);

    /// <summary>적 제독(<c>[0x924]</c>~).</summary>
    public Side EnemySide { get; set; } = new(0, 50, 50);

    /// <summary>
    /// 아군 탄약 — 들머리에 함대 보급품 탄약 x 10 이다. 0 이 되면 한 번 알리고 −1(더는 안 쏜다). 적은 안 본다.
    /// </summary>
    public int Ammo { get; set; }

    private bool _noGunsWarned;

    /// <summary>한 발 — 맞았는지, 피해, 큰 한 방인지.</summary>
    public readonly record struct Shot(bool Hit, int Damage, bool Big);

    /// <summary>한 번의 포격 — 쏜 배, 과녁, 발들, 과녁이 가라앉았는지.</summary>
    public sealed record Volley(Ship Shooter, Ship Target, IReadOnlyList<Shot> Shots, bool Sunk);

    /// <summary>한 번에 쏘는 발 수(<c>0x00436DDC</c>) — 여느 때 셋, 속사포를 지닌 내 배는 여덟.</summary>
    public const int ShotsPerVolley = 3, RapidShots = 8;

    /// <summary>속사포 아이템 번호(소지품).</summary>
    public const int RapidFireItem = 1;

    /// <summary>
    /// 속사포 형편(<c>[함대+0x834]</c>) — 0 없음 · 1 지녔지만 아직 안 알림 · 2 알렸음.
    /// </summary>
    public int RapidFire { get; private set; }

    /// <summary>속사포를 꺼내며 부관이 하는 말(<c>0x0056AF08</c>).</summary>
    public const string RapidFireWord = "속사포를 씁시다.";

    /// <summary>
    /// 판을 열 때 속사포가 먹는지 굴린다(<c>0x00441EA5</c>) — 소지품 칸마다 한 번, 먹으면 그 판 내내 여덟 발이다.
    /// </summary>
    /// <remarks>
    /// 굴림은 <c>rand(100) &lt; 포술 x 5 + 방어 / 15</c> 다. <b>망가지는 길은 없다</b> — 망가짐 판정
    /// (<c>0x00438D5E</c>)이 해를 1947 과 견주어 영영 안 걸린다.
    /// </remarks>
    public void ArmRapidFire(int carried)
    {
        for (int i = 0; i < carried; i++)
            if (_rng.Next(100) < MineSide.Gunnery * 5 + MineSide.Defense / 15)
            {
                RapidFire = 1;
                return;
            }
    }

    /// <summary>대포 위력 — 세이커 3 · 캘버린 4 · 페리에 5 · 카논 8(<c>0x0043699D</c> 벌).</summary>
    public static int GunPower(int gun) => gun switch { 0 => 3, 1 => 4, 2 => 5, 3 => 8, _ => 0 };

    /// <summary>
    /// 한 번 쏜다. 탄약·대포가 없거나 과녁이 없으면 null.
    /// </summary>
    /// <remarks>
    /// <code>
    ///   발마다  명중 = 굴림(max(1, 6*포술 + 17*√대포수 − 24))
    ///           피해 = (무력/(4−포술) + 대포수*위력)/10 − rand(상대 방어/20) ,  ≤ 0 이면 rand(4)+1
    ///   큰 한 방 (대포 ≥ 10, 포격당 한 번, 확률 5*포술 + 제 방어/10)
    ///           대포 ≥ 25 → x2 (≥ 35 면 과녁 대포 −(rand(3)+3)) · 20~24 → x3/2 · 그 밖 → x6/5
    ///   과녁 내구 = max(0, 내구 − 피해) — 0 이면 남은 발을 거두고 가라앉는다
    ///   아군이면 탄약 −= 쏜 발 수
    /// </code>
    /// 큰 한 방 뒤에 <b>선수상</b>이 값을 비튼다(<c>0x00437065</c>) — 쏘는 쪽이 내 배면 불꽃신 6/5 · 사신 2배 ·
    /// 마왕 2/3(저주다), 적이 쏘면 맞는 내 배의 여신상이 4/5 로 깎는다. <b>화면에 뜨는 숫자는 이 보정 앞의 값이다</b>
    /// (원본이 숫자를 먼저 적어 두고 안 고친다).
    /// </remarks>
    private Volley? Fire(Ship ship)
    {
        if (ship.Mine)
        {
            if (Ammo == 0)
            {
                _stage?.Notice("탄약이 떨어졌습니다! 공격할 수 없습니다!");
                Ammo = -1;
                return null;
            }
            if (Ammo < 0) return null;
        }

        if (ship.Gun < 0 || ship.Guns <= 0)
        {
            if (ship.Mine && !_noGunsWarned && FireTarget(ship, RangeOf(ship.Gun)) is not null)
            {
                _noGunsWarned = true;
                _stage?.Notice("대포를 싣지 않은 함선은 발사할 수 없습니다!");
            }
            return null;
        }

        if (FireTarget(ship, RangeOf(ship.Gun)) is not { } target) return null;
        // 괴물 몸 조각을 맞혀도 깎이는 것은 머리 내구다(0x00437116 — 과녁 > 8 이면 [+0x5A4]).
        if (IsMonster(target)) target = MonsterHead!;

        var me = ship.Mine ? MineSide : EnemySide;
        var them = ship.Mine ? EnemySide : MineSide;
        int power = GunPower(ship.Gun);
        int gunnery = Math.Clamp(me.Gunnery, 0, 3);

        var shots = new List<Shot>();
        bool bigUsed = false;
        // 속사포는 내 배만, 한 번에 여덟 발이다(0x00436DDC). 명중률도 피해도 그대로다.
        int volley = ship.Mine && RapidFire > 0 ? RapidShots : ShotsPerVolley;
        if (ship.Mine && RapidFire == 1)
        {
            RapidFire = 2;                         // 알리는 것은 판에 한 번뿐이다(0x004384E8)
            _stage?.RapidFire();
        }
        for (int i = 0; i < volley; i++)
        {
            // 명중률 = 6 x 포술 + 17 x isqrt(대포수 x 100) / 10 − 24 (0x00436E29).
            // 0 이면 1 로 올리고(0x00436E51), 굴림은 <b>부호 없이</b> 재므로(0x004B7C6F 의 CF)
            // 음수면 늘 맞는다 — 대포가 적고 포술이 낮은 배가 그렇다.
            int odds = 6 * gunnery + 17 * Isqrt(ship.Guns * 100) / 10 - 24;
            if (odds == 0) odds = 1;
            if (odds >= 0 && _rng.Next(100) >= odds)
            {
                shots.Add(new Shot(false, 0, false));
                continue;
            }

            int damage = (me.Might / Math.Max(1, 4 - gunnery) + ship.Guns * power) / 10
                         - _rng.Next(Math.Max(1, them.Defense / 20));
            if (damage <= 0) damage = _rng.Next(4) + 1;

            bool big = false;
            if (!bigUsed && ship.Guns >= 10 && _rng.Next(100) < 5 * gunnery + me.Defense / 10)
            {
                bigUsed = big = true;
                if (ship.Guns >= 25)
                {
                    damage *= 2;
                    if (ship.Guns >= 35)
                    {
                        // 과녁의 추진력과 대포가 함께 깎인다(0x00436F80). 대포는 쏜 쪽 솜씨로 갈린다.
                        target.Speed = Math.Max(0, target.Speed - (_rng.Next(4) + 5));
                        int skill = me.Might / 4 + gunnery * 10;
                        target.Guns = Math.Max(0, target.Guns
                                                  - (skill > 40 ? _rng.Next(3) + 3 : _rng.Next(2) + 1));
                    }
                }
                else if (ship.Guns >= 20) damage = damage * 3 / 2;
                else damage = damage * 6 / 5;
            }

            // 숫자는 선수상 보정 <b>앞</b>의 값이다 — 원본도 먼저 적어 두고 고치지 않는다(0x00436F0F).
            shots.Add(new Shot(true, damage, big));

            // 선수상(0x00437065) — 쏘는 쪽이 내 배면 쏘는 배의 것, 적이 쏘면 맞는 내 배의 것을 본다.
            if (ship.Mine)
                damage = ship.Figurehead switch
                {
                    FlameGod => damage * 6 / 5,
                    DeathGod => damage * 2,
                    DemonKing => damage * 2 / 3,
                    _ => damage,
                };
            else if (target.Figurehead == Goddess) damage = damage * 4 / 5;

            target.Hp = Math.Max(0, target.Hp - damage);
            if (target.Hp == 0) break;
        }

        // 탄약은 <b>한 벌의 발 수</b>만큼 깎인다 — 과녁이 먼저 가라앉아 발을 거뒀어도
        // 다 깎인다(0x00437213 의 max(탄약 − 발수, 0), 발수는 3 또는 8 그대로다).
        if (ship.Mine) Ammo = Math.Max(0, Ammo - volley);

        bool sunk = target.Hp == 0;
        if (sunk) target.State = ShipState.Sunk;       // 판 닫기·연출은 Execute 의 Sink 가 한다
        return new Volley(ship, target, shots, sunk);
    }

    /// <summary>
    /// 게임이 쓰는 정수 제곱근(<c>0x004B7C2C</c>) — 뉴턴 법이라 <c>Math.Sqrt</c> 와 값이 갈릴 수 있다.
    /// </summary>
    private static int Isqrt(int value)
    {
        if (value <= 0) return 0;
        uint n = (uint)value, guess = 1, half = n;
        while (half > guess) { half >>= 1; guess += guess; }
        uint last;
        do
        {
            last = guess;
            guess = (guess + n / guess) >> 1;
        } while (last > guess);
        return (int)last;
    }

    /// <summary>
    /// 과녁 — 거리 2 부터 사거리까지 <b>뱃전</b> 칸(<see cref="InBroadside"/>, 고리의 옆면 두 변)의 맞은편 산 배를
    /// 찾아, 그 거리에서 기함이면 곧장, 아니면 내구가 가장 낮은 배(<c>0x004369E0</c>~).
    /// </summary>
    private Ship? FireTarget(Ship ship, int range)
    {
        for (int d = 2; d <= range; d++)
        {
            var hits = Ships.Where(s => s.CanAct && s.Mine != ship.Mine
                                        && InBroadside(ship.X, ship.Y, ship.Way, s.X, s.Y, d))
                            .ToList();
            if (hits.Count == 0) continue;
            return hits.FirstOrDefault(s => s.Flagship) ?? hits.OrderBy(s => s.Hp).First();
        }
        return null;
    }

    /// <summary>불이 턴마다 깎는 내구 · 불사조상이 턴마다 되살리는 내구(<c>0x0043D7E7</c>).</summary>
    public const int BurnDamage = 3, PhoenixMend = 5;

    /// <summary>
    /// 턴 끝(틱 59 하위단계 3, <c>0x0043D7E7</c>) — 바람이 열에 하나로 돌고, 배마다 차례로:
    /// </summary>
    /// <remarks>
    /// <code>
    ///   지시상태 3 → 2 , 그 밖 → 0              ; 들이받은 배는 다음 턴도 못 움직인다
    ///   아군 · 선수상 0x20 불사조 → 내구 = min(내구+5, 최대내구)
    ///   불(상태 5) → 내구 −3 , 숫자 3 , 0 이면 가라앉음(연출 1)
    /// </code>
    /// 떠 있는 배(상태 ≥ 4)만 본다. 불은 번지지도 꺼지지도 않는다.
    /// </remarks>
    private void EndTurn()
    {
        // 이동력은 <b>바람을 돌리기 전에</b> 다시 센다(0x0043D9CB 의 0x004349A0 이 0x0043D9D0 의
        // 굴림보다 먼저다) — 그래서 다음 턴 이동력은 바뀌기 전 바람으로 잰 값이다.
        foreach (var ship in Ships) ship.Power = PowerOf(ship);

        // 바람은 <b>양쪽으로</b> 돈다 — rand(10) 이 0 이면 시계로 한 눈금(+1), 1 이면 반시계로
        // 한 눈금(+5)이고 그 밖이면 그대로다(0x0043D9D0~0x0043DA5F). 곧 각각 1/10 이다.
        int turn = _rng.Next(10);
        if (turn == 0) Wind = (Wind + 1) % Ways;
        else if (turn == 1) Wind = (Wind + 5) % Ways;

        foreach (var ship in Ships.ToList())
        {
            ship.Plan.Clear();
            ship.Pivot = Move.Straight;
            ship.Ordered = false;
            ship.Blocked = false;
            ship.Bump = ship.Bump == 3 ? 2 : 0;

            if (ship.CanAct && !Over)
            {
                if (ship.Mine && ship.Figurehead == Phoenix)
                    ship.Hp = Math.Max(ship.Hp, Math.Min(ship.Hp + PhoenixMend, ship.MaxHp));
                if (ship.Burning)
                {
                    ship.Hp = Math.Max(0, ship.Hp - BurnDamage);
                    _stage?.Burn(ship);
                    if (ship.Hp == 0)
                    {
                        ship.State = ShipState.Sunk;
                        Sink([ship]);
                    }
                }
            }
        }
    }

    // ── 가까운 싸움 — 충돌·백병전·불·나포·일기토·총격 ──────────────────────

    /// <summary>
    /// 선수상 번호(표 <c>0x0054A0A0</c>, 아이템 번호는 여기에 213 을 더한 값) —
    /// 여신 · 해신 · 수룡 · 불꽃신 · 청룡 · 백호 · 불사조 · 사신 · 마왕.
    /// </summary>
    public const int Goddess = 0x1A, SeaGod = 0x1B, WaterDragon = 0x1C, FlameGod = 0x1D,
                     BlueDragon = 0x1E, WhiteTiger = 0x1F, Phoenix = 0x20, DeathGod = 0x22,
                     DemonKing = 0x23;

    /// <summary>
    /// 적장 운세칸[3](<c>0x004319D0([+0x120])</c>) — 아군 기함이 적 기함을 받을 때 일기토 굴림에 든다.
    /// </summary>
    public int LeaderFortune { get; set; }

    /// <summary>
    /// 소지품의 잠수폭탄(아이템 <see cref="MineItem"/>, 다 빈치 수중기뢰) 수 — 칸마다 한 번씩 굴린다. 쓰면 준다.
    /// </summary>
    public int Mines { get; set; }

    /// <summary>잠수폭탄 아이템 번호.</summary>
    public const int MineItem = 0;

    /// <summary>게임의 <c>rand(n)</c>(<c>0x004B7C0F</c>) — n 이 2 보다 작으면 0.</summary>
    private int Rand(int n) => n < 2 ? 0 : _rng.Next(n);

    /// <summary>
    /// 충돌(<c>0x004397E0</c>) — 들이받은 배 <paramref name="m"/> 가 <paramref name="t"/> 의 칸으로 못 들어갔다.
    /// </summary>
    /// <remarks>
    /// <code>
    ///   지시상태  나 3 , 상대 (3 아니면) 2          ; 아군끼리면 여기서 끝(피해·백병전 없음)
    ///   a = 적재용량(+0x300)/5
    ///   |방향차| == 3 (정면)  둘 다 −(상대용량/5 + a)/5
    ///   그 밖                  들이받은 −(a/5)*8/10 · 받힌 −(a/5)*13/10
    ///   내구 0 → 가라앉음(연출 1) ; 둘 다 떴으면 백병전
    /// </code>
    /// 「정면」은 자리 관계가 아니라 <b>뱃머리 방향만</b> 본다. 방향차는 mod 가 아니라 절댓값이다(0·3, 1·4, 2·5).
    /// 굴림·선수상·능력이 없고 최소 1 도 없다(용량 25 밑이면 0). 밑값은 최대 내구가 아니라 <b>적재용량</b>이다.
    /// 멈추고 끝내는 것은 <b>둘 다 아군</b>일 때뿐이다(<c>0x00439986</c> — 두 번호가 다 8 미만). 적끼리 부딪히면
    /// 소리 0x2C · 「충돌했다!」 · 선체 피해 · 백병전(<c>0x004399F7</c>)을 다 치른다 — 백병전과 나포(<c>0x0043A200</c>)도
    /// 번호로만 편을 가르므로 들이받은 적은 적 쪽 셈을 탄다. 예전에는 적끼리도 아군처럼 그냥 섰다.
    /// </remarks>
    private void Collide(Ship m, Ship t)
    {
        bool friendly = m.Mine && t.Mine;
        // 잠수한 괴물이 부딪히면 잠깐 떠올라 보인다(0x0043D380 → 0x0043D389).
        bool peek = Monster && !friendly && !MonsterUp;
        if (peek) _monsterPeek = true;
        try { CollideCore(m, t, friendly); }
        finally { if (peek) _monsterPeek = false; }
    }

    private void CollideCore(Ship m, Ship t, bool friendly)
    {
        _stage?.Crash(m, t, friendly);
        m.Bump = 3;
        if (t.Bump != 3) t.Bump = 2;
        if (friendly) return;

        int a = m.Cargo / 5;
        int mLoss, tLoss;
        if (Math.Abs(m.Way - t.Way) == 3)
        {
            mLoss = tLoss = (t.Cargo / 5 + a) / 5;
        }
        else
        {
            int b = a / 5;
            mLoss = b * 8 / 10;
            tLoss = b * 13 / 10;
        }
        if (Monster && MonsterHead is { } head)
        {
            // 괴물 판 — 칸 8 이상(머리·몸)이 입는 피해는 모두 머리 내구로 간다(0x00439AA9~0x00439BC9).
            // 괴물이 한 걸음에 둘 이상을 들이받으면 제 피해는 따로 모아 두고 안 깎는다(_monsterPooled).
            // 몸 조각을 받으면 판정할 배도 머리로 옮긴다(0x00439BA1).
            if (IsMonster(m) && _monsterCrowd > 1) _monsterPooled += mLoss;
            else if (IsMonster(m)) head.Hp = Math.Max(0, head.Hp - mLoss);
            else m.Hp = Math.Max(0, m.Hp - mLoss);
            if (IsMonster(t)) head.Hp = Math.Max(0, head.Hp - tLoss);
            else t.Hp = Math.Max(0, t.Hp - tLoss);
        }
        else
        {
            m.Hp = Math.Max(0, m.Hp - mLoss);
            t.Hp = Math.Max(0, t.Hp - tLoss);
        }
        _stage?.HullLoss(m, mLoss, t, tLoss);

        var sunk = new List<Ship>();
        Ship mm = IsMonster(m) ? MonsterHead! : m, tt = IsMonster(t) ? MonsterHead! : t;
        if (mm.Hp <= 0) sunk.Add(mm);
        if (tt.Hp <= 0) sunk.Add(tt);
        if (sunk.Count > 0)
        {
            foreach (var s in sunk) s.State = ShipState.Sunk;
            Sink(sunk);
            return;
        }
        Melee(m, t);
    }

    /// <summary>
    /// 백병전(<c>0x00439D50</c>) — 충돌한 두 배가 모두 떠 있을 때 곧바로 이어진다.
    /// </summary>
    /// <remarks>
    /// <code>
    ///   P = ((검술p+1) * 들이받은 배 승원 + 무력p − 50) / 10
    ///   E = ((검술e+1) * 받힌 배 승원    + 무력e − 50) / 10
    ///   아군이 들이받음: 받힌 적 −P , 아군 −E
    ///   적이 들이받음  : 받힌 아군 −E , 적 −P        ← 원본 결함: 능력은 제 편 것인데 곱하는 승원은 잃는 쪽 배 것
    ///   아군이 들이받을 때  선수상 0x1F 백호 ×2 , 아니고 0x23 마왕 ×3/2   (받힌 적이 잃는 값)
    ///   아군이 받힐 때      선수상 0x1C 수룡 ×3/10                        (받힌 아군이 잃는 값)
    ///   둘 다 최소 1 · 연출 갈래 3 · 불 굴림 · 승원 적용 · 승원 0 이면 상태 2(기함이면 끝)
    ///   둘 다 떴으면 나포·일기토
    /// </code>
    /// 굴림·상한이 없다. C 나눗셈(0 쪽 버림)은 C# 과 같다.
    /// </remarks>
    private void Melee(Ship m, Ship t)
    {
        var p = MineSide;
        var e = EnemySide;
        int pLoss = ((p.Sword + 1) * m.Crew + p.Might - 50) / 10;
        int eLoss = ((e.Sword + 1) * t.Crew + e.Might - 50) / 10;
        int tLoss = m.Mine ? pLoss : eLoss;
        int mLoss = m.Mine ? eLoss : pLoss;

        if (m.Mine)
        {
            if (m.Figurehead == WhiteTiger) tLoss *= 2;
            else if (m.Figurehead == DemonKing) tLoss = tLoss * 3 / 2;
        }
        if (t.Mine && t.Figurehead == WaterDragon) tLoss = tLoss * 3 / 10;   // 0x4B7B96(v,3,10)
        tLoss = Math.Max(1, tLoss);
        mLoss = Math.Max(1, mLoss);

        _stage?.Melee(m, t, mLoss, tLoss);
        Ignite(m, t);                                     // 연출 갈래 3 끝 — 승원 셈 전이다

        if (MonsterHead is { } head && (IsMonster(m) || IsMonster(t)))
        {
            // 괴물 판 — 괴물 쪽 몫은 승원이 아니라 <b>머리 내구</b>에서 깎는다(0x00439EE9~0x00439F7F).
            if (IsMonster(m)) { head.Hp = Math.Max(0, head.Hp - mLoss); t.Crew = Math.Max(0, t.Crew - tLoss); }
            else { m.Crew = Math.Max(0, m.Crew - mLoss); head.Hp = Math.Max(0, head.Hp - tLoss); }

            // 내 기함 승원이 다하면 짐, 괴물 내구가 다하면 이김 — 둘 다 상태 2 다(0x00439FFA~0x0043A03B).
            if (At(0) is { Crew: <= 0 } flag) { flag.Crew = 0; CloseBy(flag); return; }
            if (head.Hp <= 0) { CloseBy(head); return; }
            CrewOut(IsMonster(t) ? m : t);
            // 괴물과는 나포·일기토가 없다(0x0043A1E3).
            return;
        }

        m.Crew = Math.Max(0, m.Crew - mLoss);
        t.Crew = Math.Max(0, t.Crew - tLoss);
        CrewOut(t, m);
        if (Over) return;

        if (m.CanAct && t.CanAct) Board(m, t);
    }

    /// <summary>
    /// 불(<c>0x00437E3A</c>) — <b>들이받은 쪽이 맞은편에</b> 지른다. 아군·적 같은 식이다.
    /// </summary>
    /// <remarks>
    /// <code>
    ///   맞은편이 이미 불이면 안 붙음 · 괴물 판에서 맞은편이 적(괴물)이면 안 붙음(0x00437E5F)
    ///   c = max(0, 무력 / (4 − 검술) − 상대 지력/2)      ; 들이받은 편 무력·검술, 받힌 편 지력
    ///   rand(100) ≤ c (부호 없는 비교) → 상태 5, 소리 0x30, blast-03~05
    /// </code>
    /// 검술 4 면 0 으로 나누지만 기능은 0~3 이라 안 난다 — 그래도 판이 죽지 않게 1 로 받친다.
    /// </remarks>
    private void Ignite(Ship m, Ship t)
    {
        if (t.Burning) return;
        if (!t.Mine && Monster) return;                   // 괴물에게는 불이 안 붙는다 — 예전에는 붙었다
        var (me, them) = m.Mine ? (MineSide, EnemySide) : (EnemySide, MineSide);
        int c = Math.Max(0, me.Might / Math.Max(1, 4 - me.Sword) - them.Mind / 2);
        if ((uint)_rng.Next(100) > (uint)c) return;
        t.Burning = true;
        _stage?.Ignite(t);
    }

    /// <summary>
    /// 승원 0 뒤처리(<c>0x0043D51E</c> · <c>0x00439D50</c> 끝) — 차례대로 상태 2 로 빼고, 기함이면 판을 닫는다.
    /// </summary>
    /// <remarks>
    /// <code>
    ///   승원 ≤ 0 → 상태 2
    ///     아군 기함 승원 ≤ 0 → 끝 1(짐) ; 아니고 적 기함 승원 ≤ 0 → 끝 2(이김, 1 을 덮지 않음) ; 아니면 나포 말
    /// </code>
    /// 가라앉지 않는다 — 판에서 빠질 뿐이고 판 끝에 들임 후보가 된다.
    /// </remarks>
    private void CrewOut(params Ship[] ships)
    {
        int end = 0;
        foreach (var s in ships)
        {
            if (s.Crew > 0 || !s.CanAct) continue;
            s.Crew = 0;
            s.State = ShipState.Captured;
            if (At(0) is { Crew: <= 0 }) end = 1;
            else if (At(PerSide) is { Crew: <= 0 }) { if (end != 1) end = 2; }
            else _stage?.Captured(s);
        }
        if (end == 1) CloseBy(At(0)!);
        else if (end == 2) CloseBy(At(PerSide)!);
    }

    /// <summary>그 기함이 빠져 판이 끝났다. 아직 떠 있으면(승원만 0) 상태 2 로 뺀다.</summary>
    private void CloseBy(Ship flag, ShipState state = ShipState.Captured)
    {
        if (flag.CanAct) flag.State = state;
        NoteFlag(flag);
    }

    /// <summary>
    /// 나포·일기토(<c>0x0043A200</c>) — 충돌해서 둘 다 살아남았을 때만 온다. 총격전으로는 안 열린다.
    /// </summary>
    /// <remarks>
    /// <code>
    ///   받힌 배가 기함 아님 — 나포
    ///     아군이 들이받음  공 = 3지p + 2무p ,  수 = 3(지e + 매e) + 10운세e
    ///     적이 들이받음    공 = 3지e + 2무e ,  수 = 3(지p + 매e) + 10운세p     ← 원본 결함: 매력만 적 것
    ///     c = max(0, 공 − 수)/5 + rand(2) ;  rand(100) &lt; c 가 아니면 끝
    ///     아군 「배를 뺏앗는다 / 대기」 · 적 rand(5) != 0 (4/5) → 상태 2, 승원은 안 옮긴다
    ///   아군 기함 → 적 기함 — c = (운세e[0] + 적장 운세칸[3])*20 + rand(5) → 「일기토 / 대기」 → 적장 말 → 결투
    ///   적 기함 → 아군 기함 — 아군 산 배 &gt; 적 산 배 · 검술e &gt; 0 · rand(2)==1 → 결투 신청
    ///   호위선이 기함을 받으면 아무것도 없다
    /// </code>
    /// </remarks>
    private void Board(Ship m, Ship t)
    {
        var p = MineSide;
        var e = EnemySide;

        if (!t.Flagship)
        {
            int attack, guard;
            if (m.Mine)
            {
                attack = 3 * p.Mind + 2 * p.Might;
                guard = 3 * (e.Mind + e.Charm) + 10 * e.Fortune;
            }
            else
            {
                attack = 3 * e.Mind + 2 * e.Might;
                guard = 3 * (p.Mind + e.Charm) + 10 * p.Fortune;      // 원본 그대로 — 매력은 적 것
            }
            int c = Math.Max(0, attack - guard) / 5 + Rand(2);
            if (_rng.Next(100) >= c) return;

            if (m.Mine) { if (_stage?.AskCapture(t) != true) return; }   // 「대기」는 그냥 닫는다
            else if (Rand(5) == 0) return;

            t.State = ShipState.Captured;
            _stage?.Captured(t);
            return;
        }

        if (m.Index == 0 && t.Index == PerSide)
        {
            int c = (e.Fortune + LeaderFortune) * 20 + Rand(5);
            if (_rng.Next(100) >= c) return;
            if (_stage?.OfferDuel() != true) return;
            Duel();
            return;
        }

        if (m.Index == PerSide && t.Index == 0)
        {
            int ours = Ships.Count(s => s.Mine && s.CanAct);
            int theirs = Ships.Count(s => !s.Mine && s.CanAct);
            if (ours <= theirs || e.Sword <= 0 || Rand(2) != 1) return;
            if (_stage?.Challenged() != true) return;                     // 거절해도 값은 안 바뀐다
            Duel();
        }
    }

    /// <summary>
    /// 결투를 넘기고 결과를 판에 먹인다(<c>0x0043A200</c> 6.4) — 이기면(끝값 ≤1) 적 기함 상태 1, 지면(용서받아도) 아군 기함 상태 1.
    /// </summary>
    /// <remarks>적 기함은 나포가 아니라 격침으로 끝나 판 끝에 들임 후보가 안 된다.</remarks>
    private void Duel()
    {
        if (_stage?.Duel() is not { } won) return;
        CloseBy(At(won ? PerSide : 0)!, ShipState.Sunk);
    }

    /// <summary>
    /// 총격전(<c>0x004362E0</c>) — 이웃 칸의 맞은편 배와 승원만 주고받는다. 아군·적 같은 코드다.
    /// </summary>
    /// <remarks>
    /// <code>
    ///   과녁  이웃 여섯 칸의 맞은편 산 배 — 기함이면 곧, 아니면 번호 낮은 배
    ///   기뢰  아군이 적 기함을 쏠 때: 잠수폭탄 칸마다 rand(100) &lt; rand(운) → 적 기함 승원 0 · 숫자 999/0 · 칸 비움
    ///   A = ((사격술p+1) * 승원 + 무력p − 50)/10 ,  E = ((사격술e+1) * 승원 + 무력e − 50)/10
    ///   아군이 걸음: 과녁 −A(쏜 아군 승원) , 쏜 배 −E(적 과녁 승원) ; 선수상 0x1E 청룡·0x23 마왕 → 과녁 몫 ×3/2
    ///   적이 걸음  : 과녁 −E(아군 과녁 승원) , 쏜 배 −A(쏜 적 승원)   ← 원본 결함: 승원이 뒤바뀐다
    ///                아군 과녁 선수상 0x1B 해신 → 과녁 몫 /2
    ///   둘 다 최소 1 · 굴림·운 없음 · 내구는 안 깎는다 · 승원 0 이면 상태 2
    /// </code>
    /// </remarks>
    private void Gunfight(Ship s)
    {
        if (s.Body) return;                                   // 몸 조각은 안 건다(0x0043630C)
        if (GunTarget(s) is not { } t) return;
        if (MonsterHead is { } head && (IsMonster(s) || IsMonster(t)))
        {
            // 잠수한 괴물도 총격을 주고받는 동안은 떠올라 보인다(0x00436575 — 1 이면 4).
            bool peek = !MonsterUp;
            if (peek) _monsterPeek = true;
            try { MonsterGunfight(s, t, head); }
            finally { if (peek) _monsterPeek = false; }
            return;
        }

        int sLoss, tLoss;
        if (s.Mine && t.Index == PerSide && MineHits())
        {
            tLoss = 999;
            sLoss = 0;
        }
        else
        {
            var p = MineSide;
            var e = EnemySide;
            int Ours(int crew) => ((p.Shooting + 1) * crew + p.Might - 50) / 10;
            int Theirs(int crew) => ((e.Shooting + 1) * crew + e.Might - 50) / 10;

            if (s.Mine)
            {
                tLoss = Ours(s.Crew);
                sLoss = Theirs(t.Crew);
                if (s.Figurehead is BlueDragon or DemonKing) tLoss = tLoss * 3 / 2;
            }
            else
            {
                tLoss = Theirs(t.Crew);
                sLoss = Ours(s.Crew);
                if (t.Figurehead == SeaGod) tLoss /= 2;
            }
            tLoss = Math.Max(1, tLoss);
            sLoss = Math.Max(1, sLoss);
        }

        s.Crew = Math.Max(0, s.Crew - sLoss);
        t.Crew = Math.Max(0, t.Crew - tLoss);
        _stage?.Gunfight(s, t, sLoss, tLoss);
        CrewOut(t, s);
    }

    /// <summary>
    /// 괴물 판의 총격전(<c>0x0043661D</c>~<c>0x00436821</c> · <c>0x0043D51E</c>) — 셈은 여느 때와 같고 괴물 쪽 몫만
    /// <b>머리 내구</b>에서 깎는다. 잠수폭탄은 괴물에게 안 쓴다(<c>0x00436588</c>).
    /// </summary>
    /// <remarks>
    /// <code>
    ///   머리 승원이 0 이면 안 건다(0x0043662E) — 괴물 승원은 안 줄어 늘 걸린다
    ///   A = ((사격술p+1) * 아군 승원 + 무력p − 50)/10 ,  E = ((사격술e+1) * … + 무력e − 50)/10 , 둘 다 최소 1
    ///   아군이 걸음   쏜 배 승원 −E ,  괴물 내구 −A
    ///   괴물이 걸음   괴물 내구 −A(쏜 괴물 승원으로 셈) ,  과녁 승원 −E
    ///   끝           내 기함 승원 0 → 짐(상태 2) ; 괴물 내구 0 → 이김(상태 2) ; 그 밖은 여느 승원 0 뒤처리
    /// </code>
    /// </remarks>
    private void MonsterGunfight(Ship s, Ship t, Ship head)
    {
        if (IsMonster(t) && head.Crew <= 0) return;
        var p = MineSide;
        var e = EnemySide;
        int Ours(int crew) => ((p.Shooting + 1) * crew + p.Might - 50) / 10;
        int Theirs(int crew) => ((e.Shooting + 1) * crew + e.Might - 50) / 10;

        int sLoss, tLoss;
        if (s.Mine)
        {
            tLoss = Ours(s.Crew);
            sLoss = Theirs(t.Crew);
            if (s.Figurehead is BlueDragon or DemonKing) tLoss = tLoss * 3 / 2;
        }
        else
        {
            tLoss = Theirs(t.Crew);
            sLoss = Ours(s.Crew);
            if (t.Figurehead == SeaGod) tLoss /= 2;
        }
        tLoss = Math.Max(1, tLoss);
        sLoss = Math.Max(1, sLoss);

        if (s.Mine)
        {
            s.Crew = Math.Max(0, s.Crew - sLoss);
            head.Hp = Math.Max(0, head.Hp - tLoss);
        }
        else
        {
            head.Hp = Math.Max(0, head.Hp - sLoss);
            t.Crew = Math.Max(0, t.Crew - tLoss);
        }
        _stage?.Gunfight(s, t, sLoss, tLoss);

        if (At(0) is { Crew: <= 0 } flag) { flag.Crew = 0; CloseBy(flag); return; }
        if (head.Hp <= 0) { CloseBy(head); return; }
        CrewOut(s.Mine ? s : t);
    }

    /// <summary>
    /// 괴물 머리의 총격 과녁(<c>0x0043632C</c>~<c>0x00436459</c>) — 내 배 0~7 가운데 머리에서 <see cref="InMonsterReach"/> 칸에
    /// 선 배, 기함이면 곧장 아니면 번호 낮은 배다. 홀짝은 머리 X 로 본다.
    /// </summary>
    private Ship? MonsterGunTarget(Ship head)
    {
        bool even = (head.X & 1) == 0;
        Ship? pick = null;
        for (int j = 0; j < PerSide; j++)
        {
            if (At(j) is not { CanAct: true } o || !InMonsterReach(o.X - head.X, o.Y - head.Y, even)) continue;
            if (j == 0) return o;
            pick ??= o;
        }
        return pick;
    }

    /// <summary>잠수폭탄 굴림 — 든 칸마다 <c>rand(100) &lt; rand(운)</c>. 맞으면 하나 쓰고 말한다.</summary>
    private bool MineHits()
    {
        for (int slot = 0; slot < Mines; slot++)
        {
            if (_rng.Next(100) >= Rand(MineSide.Defense)) continue;
            Mines--;
            _stage?.Mine();
            return true;
        }
        return false;
    }

    /// <summary>총격 과녁 — 맞은편 편 칸 차례로 이웃한 산 배, 기함이면 곧장(<c>0x004362E0</c>).</summary>
    private Ship? GunTarget(Ship s)
    {
        if (Monster && s.Index == PerSide && MonsterHead is not null) return MonsterGunTarget(s);
        int first = s.Mine ? PerSide : 0;
        Ship? pick = null;
        for (int j = first; j < first + PerSide; j++)
        {
            if (At(j) is not { CanAct: true } o || !Adjacent(s.X, s.Y, o.X, o.Y)) continue;
            // 괴물 판에서 내 배가 괴물 몸 어디에 붙든 과녁은 머리다(0x00436508 → 0x00436524).
            if (IsMonster(o)) return MonsterHead;
            if (j == first) return o;
            pick ??= o;
        }
        return pick;
    }

    /// <summary>
    /// 육각 이웃인지 — dX 0 이면 dY ±1, 짝수 X 는 dY 0·+1, 홀수 X 는 dY −1·0(짝수 X 줄이 16점 아래로 밀렸다).
    /// </summary>
    public static bool Adjacent(int x, int y, int ox, int oy)
    {
        int dx = ox - x, dy = oy - y;
        if (dx is < -1 or > 1) return false;
        if (dx == 0) return dy is 1 or -1;
        return (x & 1) == 0 ? dy is 0 or 1 : dy is -1 or 0;
    }

    /// <summary>
    /// 가라앉은 배들 — 기함이면 판을 닫고(아군 기함이 먼저 끝을 정한다), 연출 갈래 1 과 격침 말을 부른다.
    /// </summary>
    private void Sink(IReadOnlyList<Ship> ships)
    {
        foreach (var s in ships.Where(s => s.Mine)) NoteFlag(s);
        foreach (var s in ships.Where(s => !s.Mine)) NoteFlag(s);
        _stage?.Sink(ships);
    }

    // ── 퇴각 ──────────────────────────────────────────────────────────────

    /// <summary>그 칸이 퇴각 지대인지(<c>0x0043E090</c>) — 판을 열 때의 풍향(<see cref="RetreatWind"/>)으로 본다.</summary>
    public bool IsRetreatCell(int x, int y) => RetreatWind switch
    {
        0 => x is >= 9 and <= 13 && y == 0,
        1 or 2 => x == Cols - 1 && y is >= 5 and <= 10,
        3 => x is >= 9 and <= 13 && y == 15 + (x & 1),
        _ => x == 0 && y is >= 5 and <= 10,
    };

    /// <summary>퇴각 지대 칸 전부.</summary>
    public IEnumerable<(int X, int Y)> RetreatCells()
    {
        for (int x = 0; x < Cols; x++)
            for (int y = 0; y < Rows; y++)
                if (OnBoard(x, y) && IsRetreatCell(x, y)) yield return (x, y);
    }

    /// <summary>내 배를 퇴각시킨다 — 판에서 걷는다(<c>+0x24 = 3</c>).</summary>
    public void Retreat(Ship ship)
    {
        if (!ship.Mine || !ship.CanAct || !IsRetreatCell(ship.X, ship.Y)) return;
        ship.State = ShipState.Retreated;
        NoteFlag(ship);
    }

    /// <summary>내 배가 모두 판을 떴는지.</summary>
    public bool AllMineGone => Ships.Where(s => s.Mine).All(s => !s.CanAct);

    /// <summary>적이 모두 판을 떴는지.</summary>
    public bool AllEnemyGone => Ships.Where(s => !s.Mine).All(s => !s.CanAct);

    /// <summary>
    /// 먼저 판을 뜬 기함(0 또는 8). 아직 둘 다 떠 있으면 null.
    /// </summary>
    /// <remarks>
    /// 게임은 배 상태(<c>+0x30C</c>)가 바뀔 때마다 <c>0x004350F0(배)</c> 를 부르고, <b>기함이 빠질 때</b>
    /// 판을 닫는다(볼트 93 의 5절). 한 박자에 두 기함이 함께 빠져도 먼저 바뀐 쪽이 끝을 정한다.
    /// </remarks>
    public Ship? FirstFlagOut
    {
        get
        {
            if (_flagOut == null)
                foreach (var ship in Ships)
                    if (ship.Flagship && !ship.CanAct) { _flagOut = ship; break; }
            return _flagOut;
        }
    }

    private Ship? _flagOut;

    /// <summary>판이 끝났는지 — 어느 기함이 빠졌거나 한 편이 다 떴다.</summary>
    public bool Over => AllMineGone || AllEnemyGone || FirstFlagOut != null;

    /// <summary>
    /// 가라앉힌 적 배 수(상태 1). 게임의 꺾음 m 은 이것과 <see cref="EnemyCaptured"/> 의 합이다
    /// (<c>0x004350F0</c> 승리 갈래). 일기토로 이긴 적 기함도 상태 1 이라 여기에 든다.
    /// </summary>
    public int EnemyDowned => Ships.Count(s => !s.Mine && s.State == ShipState.Sunk);

    /// <summary>빼앗거나 승원을 없앤 적 배 수(상태 2).</summary>
    public int EnemyCaptured => Ships.Count(s => !s.Mine && s.State == ShipState.Captured);

    /// <summary>상태가 바뀌는 자리마다 불러, 기함이 먼저 빠진 차례를 붙잡는다.</summary>
    private void NoteFlag(Ship ship)
    {
        if (_flagOut == null && ship.Flagship && !ship.CanAct) _flagOut = ship;
    }

    // ── 말 ────────────────────────────────────────────────────────────────

    /// <summary>「바람은 %s풍입니다. 퇴각지점은 바람이 부는 %s쪽에 있습니다.」(<c>0x0056B4E8</c>).</summary>
    /// <remarks>
    /// 게임은 방위 넷 [북·동·남·서](<c>0x0056B4D0</c>~)에서 <c>push ecx ; push eax ; push 서식</c> 으로 넣는다
    /// (<c>0x0043C5EB</c>~<c>0x0043C64C</c>) — 첫 %s(풍향)가 eax, 둘째(퇴각 쪽)가 ecx 다.
    /// <code>
    ///   바람 0     남풍 / 북쪽        바람 3     북풍 / 남쪽
    ///   바람 1·2   서풍 / 동쪽        바람 4·5   동풍 / 서쪽
    /// </code>
    /// 예전에는 둘을 맞바꿔 읽어 네 경우가 다 거꾸로였다 — 퇴각 지대(<see cref="IsRetreatCell"/>)와도 어긋났다.
    /// </remarks>
    public string WindNotice()
    {
        (string from, string to) = Wind switch
        {
            0 => ("남", "북"),
            1 or 2 => ("서", "동"),
            3 => ("북", "남"),
            _ => ("동", "서"),
        };
        return $"바람은 {from}풍입니다. 퇴각지점은 바람이 부는 {to}쪽에 있습니다.";
    }

    /// <summary>이동 지시를 재촉하는 세 벌(<c>0x0056B328</c> 벌).</summary>
    public string OrderPrompt() => _rng.Next(3) switch
    {
        0 => "제독, 각 함선에 이동 지시를!",
        1 => "준비는 완벽합니다! 이동 지시를 내려 주십시오!",
        _ => "각 함대에 이동 지시를 내려 주십시오.",
    };

    /// <summary>
    /// 괴물 판이면 이동 지시 뒤에 <b>한 마디가 더</b> 붙는다(<c>0x0043C670</c> · <c>0x0043BF01</c>).
    /// </summary>
    /// <remarks>
    /// 괴물은 물속에 숨어 있어 어디 있는지 모른다는 말이다(<c>0x0056B3A8</c>~). 괴물 판이
    /// 아니면 빈 글이다.
    /// </remarks>
    public string MonsterHidWord() => !Monster || MonsterUp ? "" : _rng.Next(5) switch
    {
        0 => "제독, 괴물이 잠수해 버려 어디 있는지 알 수가 없습니다.",
        1 => "괴물 놈, 어디 있는 거냐!",
        2 => "아뿔사! 바다 속에 잠수해 버렸다.",
        3 => "제독, 괴물이 바다 속으로 모습을 감추어 버렸습니다!",
        _ => "바다 속으로 모습을 감추었다고? 주위를 경계해야지···",
    };

    /// <summary>
    /// 충돌 말(<c>0x0056AFF0</c> · <c>0x0056B038</c>). 원본 첫째 줄은 <c>\n</c> 으로 끊기는데 우리 창은 띄어쓰기로만
    /// 끊어 빈칸으로 둔다.
    /// </summary>
    public static string CrashWord(Ship mover, Ship hit) =>
        mover.Mine && hit.Mine ? "위험하다! 정지!\n·····하마터면 아군끼리 부딪칠 뻔 했다." : "충돌했다!";   // 0x0056AFF0 · 0x0056B038

    private string One(string[] lines) => lines[_rng.Next(lines.Length)];

    /// <summary>잠수폭탄 말(<c>0x0056AE60</c>).</summary>
    public const string MineWord = "다 빈치선생님의 수중기뢰를 씁시다.";

    /// <summary>격침 말(<c>0x004350F0</c>, rand(5)) — 아군 <c>0x0056A850</c>~ · 적 <c>0x0056A910</c>~.</summary>
    public string SinkWord(Ship ship) => ship.Mine
        ? string.Format(One([
            "큰일났습니다! {0}호가 침몰하고 말았습니다.",
            "제독, {0}호가 가라앉고 있습니다!",
            "큰일입니다! {0}호가 격침당했습니다!",
            "{0}호가 공격당했습니다!",
            "아아, {0}호가 가라앉아 버렸습니다!",
        ]), ship.Name)
        : One([
            "적함 한 척을 격침시켰습니다!",
            "한 척을 격침시켰습니다!",
            "제독, 적함 한 척을 가라앉혔습니다.",
            "적함 한 척을 바다속에 가라앉혔습니다.",
            "적함 1척 격침! 꼴좋군!",
        ]);

    /// <summary>우리 배를 잃은 말 열 줄(<c>0x0056A9B8</c>~).</summary>
    private static readonly string[] LostLines =
    [
        "배를 빼앗겼습니다.",
        "제독, 죄송합니다. 배를 빼앗겼습니다.",
        "어찌 된 일인가! 배를 빼앗겼습니다.",
        "어찌 된 일인가! {0}호가 당했습니다.",
        "{0}호의 선원이 당했습니다!",
        "제독, {0}호가 당했습니다!",
        "큰일입니다. 배를 빼앗겼습니다!",
        "적에게 빈틈을 보여 배를 빼앗겼습니다!",
        "앗! {0}호를 빼앗겼습니다!",
        "제독, {0}호가 적의 손에 들어갔습니다!",
    ];

    /// <summary>앞 <paramref name="count"/> 줄에서만 고른다.</summary>
    private string OneOf(string[] lines, int count) => lines[_rng.Next(Math.Min(count, lines.Length))];

    /// <summary>나포·승원 0 말(<c>0x004358EE</c>, rand(10)) — 빼앗김 <c>0x0056A9B8</c>~ · 빼앗음 <c>0x0056AB28</c>~.</summary>
    /// <remarks>
    /// 괴물 판에서 배를 잃으면 <b>앞 여섯 줄</b>에서만 고른다(<c>0x0043594C</c> → <c>0x0043597B</c> 의 rand(6)).
    /// 예전에는 괴물 판에도 열 줄에서 골랐다.
    /// </remarks>
    public string CapturedWord(Ship ship) => ship.Mine
        ? string.Format(Monster ? OneOf(LostLines, 6) : One(LostLines), ship.Name)
        : One([
            "적함을 빼앗았습니다.",
            "적함을 빼앗았다! 꼴 좋군.",
            "제독, 적함을 나포했습니다.",
            "헤헤, 적함을 빼앗았습니다!",
            "제독, 빈틈을 타서 적함을 빼앗았습니다!",
            "적함 한 척을 빼앗았어요.",
            "적함 한 척을 전투 불가능하게 해 놓았습니다.",
            "이 배는 우리들 것이다!",
            "적함의 선원을 해치웠습니다.",
            "제독, 적함 한 척을 없애버렸습니다.",
        ]);

    /// <summary>나포 고르기(<c>0x0056B048</c> · <c>0x0056B058</c>).</summary>
    public static readonly string[] CaptureRows = ["배를 뺏앗는다", "대기"];

    /// <summary>일기토 고르기(<c>0x0056B060</c> · <c>0x0056B068</c>).</summary>
    public static readonly string[] DuelRows = ["일기토", "대기"];

    /// <summary>결투 신청 고르기(<c>0x0056B210</c> · <c>0x0056B218</c>).</summary>
    public static readonly string[] ChallengeRows = ["응한다", "거절한다"];

    /// <summary>아군이 일기토를 걸었을 때 적장 말(<c>0x0056B070</c>~).</summary>
    public string DuelTakenWord() => One([
        "물고기 밥을 만들어 주겠다! 덤벼라!",
        "배짱은 좋군, 상대해 주마.",
        "남자답군. 상대해 주마.",
        "아니, 일대일로 싸우고 싶다고? 좋지!",
        "신청해 놓고 나중에 후회하지 마라.",
    ]);

    /// <summary>
    /// 적이 결투를 신청할 때 부관 말(<c>0x0056B128</c>~). <c>%s%s</c> 는 적장 이름과 조사 이/가 다
    /// (<c>0x0043A60D</c> 의 <c>0x004281B0(이름, 0)</c> — 갈래 0 이 가/이다).
    /// </summary>
    public string ChallengeWord(string foe)
    {
        string who = foe + Josa(foe, "이", "가");
        return One([
            $"{who} 일대일 결투를 신청해 왔습니다!",
            "제독, 적이 일대일 결투를 원하고 있습니다!",
            $"{who} 제독과 일대일 결투를 하고 싶다고 합니다!",
            "제독, 적이 일대일 승부를 겨루고 싶다고 합니다!",
            "제독, 적의 결투 신청에 응하겠습니까?",
        ]);
    }

    /// <summary>결투 신청에 응했을 때 적장 말(<c>0x0056B228</c>~).</summary>
    public string AcceptedWord() => One([
        "그래야지!", "이얏! 간다!", "제법 배짱이 좋군.", "도망가지 않았다는 점은 칭찬해 주지.", "그럼, 슬슬 시작할까.",
    ]);

    /// <summary>결투 신청을 거절했을 때 적장 말(<c>0x0056B2A8</c>~).</summary>
    public string RefusedWord() => One([
        "이 겁장이!", "겨우 그정도냐.", "쳇, 시시한 놈이군.", "도망가느냐, 겁장이.", "그러고도 남자냐, 겁장이 같으니라고!",
    ]);

    /// <summary>
    /// 되찾은 배 말 — 승리 <c>0x0056A6D8</c>(마침표 있음) · 적 기함 퇴각 <c>0x0056AE38</c>(마침표 없음).
    /// </summary>
    public static string RecoveredWord(bool won) => won ? "빼앗긴 배를 되찾았습니다." : "빼앗긴 배를 되찾았습니다";

    /// <summary>
    /// 「로부터 / 으로부터」(<c>0x004281B0</c> 의 색인 0xD, 표 <c>0x0053C420</c>) — 받침이 없거나 ㄹ이면 「로부터」다.
    /// </summary>
    public static string FromJosa(string word)
    {
        if (word.Length == 0) return "로부터";
        char last = word[^1];
        if (last is < '가' or > '힣') return "로부터";
        int jong = (last - '가') % 28;
        return jong is 0 or 8 ? "로부터" : "으로부터";
    }

    /// <summary>다 빠져나갔을 때의 다섯 벌(<c>0x00435ABF</c>).</summary>
    public string EscapedWord(string foe)
    {
        string eul = Josa(foe, "을", "를");
        return _rng.Next(5) switch
        {
            0 => "휴, 간신히 도망쳐 나왔습니다.",
            1 => "휴, 아슬아슬했다···",
            2 => $"겨우 {foe}{eul} 물리쳤습니다.",
            3 => $"제독, {foe}{FromJosa(foe)} 도망쳐 나왔습니다.",
            _ => $"{foe}의 추격을 물리친 것 같습니다!",
        };
    }

    /// <summary>내 기함이 가라앉았을 때 적장이 비웃는 다섯 벌(<c>0x0056A418</c>~, <c>0x004350F0</c> 패배 갈래).</summary>
    public string TauntWord() => _rng.Next(5) switch
    {
        0 => "흐흐흐, 물고기 밥이 되었군.",
        1 => "네놈의 항해도 이제 끝이다.",
        2 => "상어의 밥이나 되라.",
        3 => "바다에서 죽는 것이 소원이겠지.",
        _ => "상대를 잘못 만났군. 죽어라!",
    };

    /// <summary>
    /// <b>괴물</b>을 퇴치했을 때의 다섯 벌(<c>0x004352DC</c>, <c>0x0056A4B8</c>~).
    /// </summary>
    /// <remarks>여느 함대를 꺾었을 때(<see cref="WonWord"/>)와 문구가 아주 다르다.</remarks>
    public string MonsterWonWord() => _rng.Next(5) switch
    {
        0 => "해냈습니다! 괴물을 퇴치했습니다!",
        1 => "해냈다! 괴물을 퇴치했습니다.",
        2 => "꼴 좋군, 괴물!",
        3 => "알겠느냐, 우리들의 실력을!",
        _ => "우리들의 적이 아니였던 것 같군.",
    };

    /// <summary>적 기함을 꺾었을 때 부관의 다섯 벌(<c>0x0056A558</c>~).</summary>
    public string WonWord(string foe) => _rng.Next(5) switch
    {
        0 => $"{foe}의 함선을 물리쳤습니다.",
        1 => "해냈습니다. 기함을 물리쳤습니다!",
        2 => "제독, 기함을 물리쳤습니다.",
        3 => "기함을 물리쳤습니다! 우리가 이겼습니다!",
        _ => "적 기함을 물리쳤습니다! 우리가 이겼습니다!",
    };

    /// <summary>진 적장의 다섯 벌(<c>0x0056A618</c>~).</summary>
    public string BeatenWord() => _rng.Next(5) switch
    {
        0 => "내 인생도 이제 끝인가···",
        1 => "네놈 따위에게 당할 줄이야···",
        2 => "바다에서 죽을 수만 있다면 미련은 없다.",
        3 => "내가 질 줄이야···방심했군.",
        _ => "저승으로 가게 될 줄이야···",
    };

    /// <summary>
    /// 적 기함이 달아났을 때 부관의 다섯 벌(<c>0x0056AD40</c>~). 끝 줄의 조사는 은/는이다
    /// (<c>0x00435FDA</c> 의 <c>0x004281B0(이름, 1)</c>).
    /// </summary>
    public string FoeFledWord(string foe) => _rng.Next(5) switch
    {
        0 => "꽁무니를 빼고 도망갔습니다!",
        1 => "모처럼의 사냥감을 놓쳤군요.",
        2 => "하하하, 꼴 좋군.",
        3 => "제독이 무서워서 도망간 것 같군요.",
        _ => $"제독, {foe}{Josa(foe, "은", "는")} 도망친 것 갔습니다!",
    };

    private static string Josa(string word, string batchim, string plain)
    {
        if (word.Length == 0) return plain;
        char last = word[^1];
        if (last < 0xAC00 || last > 0xD7A3) return plain;
        return (last - 0xAC00) % 28 != 0 ? batchim : plain;
    }
}
