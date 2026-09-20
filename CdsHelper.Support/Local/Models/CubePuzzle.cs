namespace CdsHelper.Support.Local.Models;

/// <summary>
/// 미니 게임 「화살표 입방체 퍼즐」 — 화살표가 그려진 입방체를 굴려 좌대를 옮긴다.
/// </summary>
/// <remarks>
/// 게임의 <c>0x0049B3C0</c> 이 껍데기, <c>0x0049B310</c> 이 한 판, <c>0x0049BE10</c> 이
/// 본체다. 설명 글은 <c>0x0056DB38</c> 벌에 그대로 있다.
/// <code>
///   성공조건 [자기가 타고 있는 좌대를 움직여서 출구로 이동한다]
///
///   입방체를 지면에 수직으로 90도 회전시키면 그 때 위의 면에 나온 화살표가
///   가리키는 방향으로 좌대가 하나만 움직인다.
///   지면에 수평방향으로 회전시켜도 대좌는 움직이지 않는다. 이 수평 회전은
///   한번에 90도이지만, 연달아 돌릴 수는 없다.
///   입방체가 서로 면하는 면은 대칭이 되어 있으며 반대편의 면이 그대로 비치는
///   것 처럼 되어 있다.
/// </code>
/// 지면 밑으로 떨어지면 진다. 그런데 <b>몇 번이고 봐 준다</b> —
/// <c>0x0049B3C0</c> 이 진 판을 받으면 "아니! 밑바닥으로 떨어진 줄 알았는데 실은 그
/// 아래층이 존재했다! 자, 모험자여! 이것이 마지막 찬스다!"(<c>0x0056DD58</c>) 하고
/// 한 판을 더 주는데, 그 자리가 <b>돌림</b>이라 나가거나 포기할 때까지 되풀이된다.
///
/// 이기면 <c>0x0049B366</c> 이 <c>0x0047CBC0(0x3E8)</c> 로 <b>금화 1000닢</b>을 준다 —
/// 글은 "금화로 따지면 %ld 닢에 상당되는 금괴를 손에 넣었다!"(<c>0x0056DDF8</c>)다.
/// </remarks>
public sealed class CubePuzzle
{
    /// <summary>
    /// 판 한 변. 게임이 <c>0x0049B9FD</c> 에서 <c>CPoint(5, 5)</c> 를 넘긴다.
    /// </summary>
    public const int Side = 5;

    /// <summary>출구가 놓이는 줄 — <b>판 위쪽 바깥</b>이다(<c>0x0049D0EB</c>).</summary>
    public const int ExitRow = -1;

    /// <summary>이기면 받는 금화(<c>0x0049B366</c> 의 <c>0x3E8</c>).</summary>
    public const int Prize = 1000;

    /// <summary>네 방향 — 북·동·남·서. 화살표 번호가 이 차례다.</summary>
    public static readonly (int Dx, int Dy, string Name)[] Ways =
        [(0, -1, "북"), (1, 0, "동"), (0, 1, "남"), (-1, 0, "서")];

    /// <summary>
    /// 입방체의 여섯 면에 그려진 화살표. 값은 <see cref="Ways"/> 의 번호다.
    /// </summary>
    /// <remarks>
    /// <b>마주 보는 면은 대칭</b>이라 했으니, 위·아래가 같은 쪽을 가리키게 짝을 맞춘다.
    /// 곧 굴려서 어느 면이 올라오든 «보이는 화살표» 가 곧 갈 쪽이다.
    /// </remarks>
    private readonly int[] _face = new int[6];

    /// <summary>
    /// 입방체가 <b>얼마나 돌아 있는가</b> — 몸 좌표를 판 좌표로 옮기는 정수 행렬이다.
    /// </summary>
    /// <remarks>
    /// 예전에는 「위·북·동 면 번호 셋 + 수평으로 몇 번 돌렸나」로 들었는데 <b>그것으로는
    /// 모자랐다</b>. 넘어뜨리기를 섞으면 윗면이 판 위에서 얼마나 돌아 있는지가 그 셋에 안
    /// 잡혀서, 그림은 오른쪽을 가리키는데 좌대는 위로 가는 일이 생겼다.
    /// 회전은 회전으로 들어야 어긋날 데가 없다.
    /// </remarks>
    private int[,] _rot = { { 1, 0, 0 }, { 0, 1, 0 }, { 0, 0, 1 } };

    /// <summary>면 여섯의 법선. 0 이 위, 5 가 아래, 1·4 가 남북, 2·3 이 동서다.</summary>
    private static readonly int[][] Normal =
    [
        [0, 0, 1], [0, 1, 0], [1, 0, 0], [-1, 0, 0], [0, -1, 0], [0, 0, -1],
    ];

    /// <summary>
    /// 면 k 를 <b>윗면 본에서 만들어 내는</b> 회전 — 그 면을 위로 올리는 회전의 거꾸로다.
    /// </summary>
    private static readonly int[][,] FromTop =
    [
        Spin(0, 0), Spin(0, -1), Spin(1, 1), Spin(1, -1), Spin(0, 1), Spin(0, 2),
    ];

    /// <summary>윗면에서 화살표 값이 가리키는 쪽 — 면의 대각선이다.</summary>
    private static readonly int[][] TopWay =
    [
        [-1, -1, 0], [1, -1, 0], [1, 1, 0], [-1, 1, 0],
    ];

    /// <summary>좌대가 선 자리.</summary>
    public int X { get; private set; }

    public int Y { get; private set; }

    /// <summary>출구 자리.</summary>
    public int ExitX { get; }

    public int ExitY { get; }

    /// <summary>몇 번 움직였는지.</summary>
    public int Moves { get; private set; }

    /// <summary>바로 앞에 수평으로 돌렸나. 그러면 또 못 돌린다.</summary>
    public bool JustSpun { get; private set; }

    /// <summary>끝났으면 이겼는지. 아직이면 null.</summary>
    public bool? Over { get; private set; }

    /// <summary>
    /// 지금 <b>화면에 보이는 그대로</b>, 윗면 화살표가 가리키는 쪽.
    /// </summary>
    /// <remarks>
    /// 회전 행렬에서 곧장 뽑는다 — 위에 온 면을 찾고, 그 면에 그려진 화살표를 판 좌표로
    /// 옮기면 네 대각선 중 하나가 나온다. 그림도 같은 셈을 쓰므로 <b>보이는 쪽과 가는
    /// 쪽이 어긋날 수 없다</b>.
    /// </remarks>
    public int Arrow
    {
        get
        {
            int up = UpFace();
            var dir = Apply(_rot, Apply(FromTop[up], TopWay[_face[up] & 3]));
            return dir[0] < 0 ? (dir[1] < 0 ? 0 : 3) : (dir[1] < 0 ? 1 : 2);
        }
    }

    /// <summary>지금 위에 온 면. 돌린 법선이 <c>+Z</c> 인 면이다.</summary>
    private int UpFace()
    {
        for (int i = 0; i < 6; i++)
            if (Apply(_rot, Normal[i])[2] > 0) return i;
        return 0;
    }

    /// <summary>그 축으로 90 도씩 <paramref name="quarter"/> 번 도는 정수 행렬.</summary>
    private static int[,] Spin(int axis, int quarter)
    {
        int[,] m = { { 1, 0, 0 }, { 0, 1, 0 }, { 0, 0, 1 } };
        int[,] one = axis switch
        {
            0 => new[,] { { 1, 0, 0 }, { 0, 0, -1 }, { 0, 1, 0 } },
            1 => new[,] { { 0, 0, 1 }, { 0, 1, 0 }, { -1, 0, 0 } },
            _ => new[,] { { 0, -1, 0 }, { 1, 0, 0 }, { 0, 0, 1 } },
        };
        for (int i = 0; i < ((quarter % 4) + 4) % 4; i++) m = Times(one, m);
        return m;
    }

    private static int[] Apply(int[,] m, int[] v) =>
    [
        m[0, 0] * v[0] + m[0, 1] * v[1] + m[0, 2] * v[2],
        m[1, 0] * v[0] + m[1, 1] * v[1] + m[1, 2] * v[2],
        m[2, 0] * v[0] + m[2, 1] * v[1] + m[2, 2] * v[2],
    ];

    private static int[,] Times(int[,] a, int[,] b)
    {
        var m = new int[3, 3];
        for (int i = 0; i < 3; i++)
        for (int j = 0; j < 3; j++)
            m[i, j] = a[i, 0] * b[0, j] + a[i, 1] * b[1, j] + a[i, 2] * b[2, j];
        return m;
    }

    /// <summary>
    /// 그 면에 <b>그려진</b> 화살표(0~3). 입방체를 그릴 때 쓴다.
    /// </summary>
    /// <remarks>
    /// 면에 칠해진 것이라 입방체가 돌아도 안 바뀐다 — 어느 쪽을 가리키는지는 <b>입방체가
    /// 얼마나 돌아 있는가</b>가 정한다. 그래서 그림 쪽은 제 회전 행렬을 따로 들고
    /// 이 값을 그대로 얹는다. 면 번호는 <c>0</c> 이 위, <c>5</c> 가 아래,
    /// <c>1</c>·<c>4</c> 가 남북, <c>2</c>·<c>3</c> 이 동서다(마주 보는 면은 <c>5-i</c>).
    /// </remarks>
    public int PaintedArrow(int face) => _face[face];

    /// <summary>바닥이 있는 칸. 줄마다 하나씩 뚫려 있다.</summary>
    private readonly bool[,] _floor = new bool[Side, Side];

    /// <summary>그 칸에 바닥이 있는지. 판 밖은 늘 거짓이다.</summary>
    public bool Floor(int x, int y) =>
        x >= 0 && x < Side && y >= 0 && y < Side && _floor[x, y];

    /// <summary>금괴가 놓인 칸.</summary>
    public int GoldX { get; }
    public int GoldY { get; }

    /// <summary>금괴를 집었는지.</summary>
    public bool GotGold { get; private set; }

    /// <summary>
    /// 판을 짓는다 — 게임의 <c>0x0049CFB0</c> 차례 그대로다.
    /// </summary>
    /// <remarks>
    /// <code>
    ///   0049cff7  시작 = (rand(5), 4)                     ; 맨 아랫줄
    ///   0049d040  칸을 모두 1 로 채운다
    ///   0049d052  맨 아랫줄 구멍 — 시작 칸은 피한다
    ///   0049d0a3  위로 올라가며 줄마다 하나씩, 이웃 줄 구멍과 두 칸 이상 떨어져야
    ///   0049d0d7  출구 = (rand(5), -1)                    ; 맨 윗줄 구멍과 다른 칸
    ///   0049d111  금괴 = 시작도 출구도 아닌 칸
    /// </code>
    /// </remarks>
    public CubePuzzle(Random rng)
    {
        // <b>마주 보는 면은 대칭</b>이라 했으니 짝끼리 같은 쪽을 가리키게 둔다.
        _face[0] = _face[5] = rng.Next(4);
        _face[1] = _face[3] = rng.Next(4);
        _face[2] = _face[4] = rng.Next(4);

        X = rng.Next(Side);
        Y = Side - 1;

        for (int y = 0; y < Side; y++)
        for (int x = 0; x < Side; x++)
            _floor[x, y] = true;

        // 맨 아랫줄 구멍 — 시작 칸은 피한다.
        int hole;
        do { hole = rng.Next(Side); } while (hole == X);
        _floor[hole, Side - 1] = false;

        // 위로 올라가며 줄마다 하나씩. 이웃 줄 구멍과 두 칸 이상 떨어져야 한다.
        for (int row = Side - 2; row >= 0; row--)
        {
            int next;
            do { next = rng.Next(Side); } while (Math.Abs(next - hole) < 2);
            hole = next;
            _floor[hole, row] = false;
        }

        // 출구는 판 위쪽 바깥이고, 맨 윗줄 구멍과 같은 칸은 안 된다.
        int door;
        do { door = rng.Next(Side); } while (door == hole);
        ExitX = door;
        ExitY = ExitRow;

        // 금괴는 <b>바닥이 있는</b> 칸이라야 한다 — 시작도 출구도 아니어야 하고, 구멍에
        // 놓이면 밟을 수가 없어 얻을 길이 없다(구멍을 밟으면 그대로 떨어진다).
        int gx, gy;
        do { gx = rng.Next(Side); gy = rng.Next(Side); }
        while ((gx == X && gy == Y) || (gx == ExitX && gy == ExitY) || !_floor[gx, gy]);
        GoldX = gx;
        GoldY = gy;
    }

    /// <summary>
    /// 수직으로 90도 굴린다 — 그 쪽으로 넘어뜨린다.
    /// </summary>
    /// <remarks>
    /// 넘어뜨리면 그 쪽 옆면이 위로 온다. 그러고 나서 <b>새로 위에 온 면의
    /// 화살표</b>가 가리키는 쪽으로 좌대가 한 칸 움직인다 — 넘어뜨린 쪽이 아니다.
    /// </remarks>
    /// <param name="way">넘어뜨릴 쪽(<see cref="Ways"/> 번호).</param>
    public void Roll(int way)
    {
        if (Over != null) return;

        // 그쪽 면이 위로 오게 넘어뜨린다 — 그림 쪽(CubeArt) 과 같은 회전이다.
        _rot = Times(way switch
        {
            0 => Spin(0, 1),      // 북 — 남쪽 면이 위로
            1 => Spin(1, -1),     // 동
            2 => Spin(0, -1),     // 남
            _ => Spin(1, 1),      // 서
        }, _rot);

        JustSpun = false;
        Moves++;

        var (dx, dy, _) = Ways[Arrow];
        X += dx;
        Y += dy;

        // 출구는 판 위쪽 바깥이라 판 밖 검사보다 먼저 본다.
        if (X == ExitX && Y == ExitY) { Over = true; return; }

        // 판 밖이거나 구멍이면 떨어진다.
        if (!Floor(X, Y)) { Over = false; return; }

        if (X == GoldX && Y == GoldY) GotGold = true;
    }

    /// <summary>스스로 포기했는가 — 떨어진 것과 갈라 본다(떨어지면 한 판을 더 준다).</summary>
    public bool GaveUp { get; private set; }

    /// <summary>포기한다 — 진 것으로 끝낸다.</summary>
    public void GiveUp()
    {
        if (Over == null) { Over = false; GaveUp = true; }
    }

    /// <summary>수평으로 90도 돌린다. 좌대는 안 움직이고, 연달아는 못 한다.</summary>
    public bool Spin()
    {
        if (Over != null || JustSpun) return false;

        _rot = Times(Spin(2, 1), _rot);
        JustSpun = true;
        Moves++;
        return true;
    }
}
