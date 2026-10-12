namespace CdsHelper.Support.Local.Models;

/// <summary>
/// 미니 게임 「화살표 입방체 퍼즐」 — 화살표가 그려진 입방체를 굴려 좌대를 옮긴다.
/// </summary>
/// <remarks>
/// 원본의 <c>0x0049B3C0</c> 이 껍데기, <c>0x0049B310</c> 이 한 판, <c>0x0049BE10</c> 이
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
    /// 판 한 변. 원본이 <c>0x0049B9FD</c> 에서 <c>CPoint(5, 5)</c> 를 넘긴다.
    /// </summary>
    public const int Side = 5;

    /// <summary>출구가 놓이는 줄 — <b>판 위쪽 바깥</b>이다(<c>0x0049D0EB</c>).</summary>
    public const int ExitRow = -1;

    /// <summary>이기면 받는 금화(<c>0x0049B366</c> 의 <c>0x3E8</c>).</summary>
    public const int Prize = 1000;

    /// <summary>
    /// 좌대가 가는 네 쪽 — 번호는 <b>윗면의 어느 귀퉁이를 가리키는가</b>다(<c>0x0049D1B0</c>, 뜀표 <c>0x0049D244</c>).
    /// </summary>
    /// <remarks>
    /// 0 은 가까운 귀퉁이 V0 — 한 줄 아래, 1 은 V1 — 오른쪽 칸, 2 는 먼 귀퉁이 V2 — 한 줄 위(출구 쪽), 3 은 V3 — 왼쪽 칸이다.
    /// 윗면 마름모에서 V0 이 아래, V1 이 오른쪽, V2 가 위, V3 이 왼쪽에 비치므로 <b>보이는 쪽이 곧 가는 쪽</b>이다.
    /// </remarks>
    public static readonly (int Dx, int Dy, string Name)[] Ways =
        [(0, 1, "아래"), (1, 0, "오른쪽"), (0, -1, "위"), (-1, 0, "왼쪽")];

    /// <summary>입방체 여덟 귀퉁이(<c>0x00585EE8</c>~, 정적 초기화 <c>0x0041BF60</c>).</summary>
    private static readonly int[][] Corner =
    [
        [-100, -100, 100], [100, -100, 100], [100, 100, 100], [-100, 100, 100],
        [-100, -100, -100], [100, -100, -100], [100, 100, -100], [-100, 100, -100],
    ];

    /// <summary>
    /// 면 여섯의 귀퉁이 넷(<c>0x004ACB7D</c>~<c>0x004ACE38</c>) — <b>차례가 뜻이 있다</b>.
    /// </summary>
    /// <remarks>
    /// 0 이 위(z=+100), 1 이 아래, 2 가 처음에 보이는 오른쪽 옆면(y=−100), 3 이 그 맞은편, 4 가 보이는 왼쪽 옆면(x=−100),
    /// 5 가 그 맞은편이다. 마주 보는 면은 같은 차례의 귀퉁이가 입방체를 뚫고 마주 본다 — 설명 글의
    /// 「반대편의 면이 그대로 비치는 것처럼」이 이것이다.
    /// </remarks>
    private static readonly int[][] FaceCorners =
    [
        [0, 1, 2, 3], [4, 5, 6, 7], [0, 1, 5, 4], [3, 2, 6, 7], [0, 3, 7, 4], [1, 2, 6, 5],
    ];

    /// <summary>단추 번호 — 넘어뜨리기 넷(2~5)과 수평 돌리기 둘(6 · 7). 원본 단추 id 그대로다(<c>0x0049C353</c>~).</summary>
    public const int TipUpLeft = 2, TipDownLeft = 3, TipUpRight = 4, TipDownRight = 5, SpinLeft = 6, SpinRight = 7;

    /// <summary>
    /// 면마다 귀퉁이 넷의 <b>지금 자리</b> — 원본도 좌표 스물넷을 통째로 돌린다(<c>0x004ACFF0</c>). 면 갈아치기 표는 없다.
    /// </summary>
    private readonly int[][][] _corner = new int[6][][];

    /// <summary>
    /// 면마다 화살표가 가리키는 <b>귀퉁이 번호</b>(0~3) — 면 가운데에서 그 귀퉁이 쪽으로 난다. 한 번 정하면 안 바뀐다.
    /// </summary>
    /// <remarks>
    /// <c>rand(4)</c> 셋을 마주 보는 면 둘에 똑같이 넣는다(<c>0x004ACE4A</c>~<c>0x004ACE67</c>) — 다른 조건은 없다.
    /// </remarks>
    private readonly int[] _arrow = new int[6];

    /// <summary>그 면 그 귀퉁이의 지금 좌표. 그림(입방체)이 쓴다.</summary>
    public (int X, int Y, int Z) CornerAt(int face, int index)
    {
        var p = _corner[face][index];
        return (p[0], p[1], p[2]);
    }

    /// <summary>그 면의 화살표가 가리키는 귀퉁이 번호.</summary>
    public int ArrowOf(int face) => _arrow[face];

    /// <summary>
    /// 단추가 입방체를 90 도 돌린 끝 자리(<c>0x004ACFF0</c>, 뜀표 <c>0x004AD0D4</c>).
    /// </summary>
    private static int[] Turned(int button, int[] p) => button switch
    {
        TipUpLeft => [p[0], p[2], -p[1]],      // 보이는 오른쪽 옆면이 위로
        TipDownLeft => [-p[2], p[1], p[0]],    // 위가 보이는 왼쪽 옆면으로
        TipUpRight => [p[2], p[1], -p[0]],     // 보이는 왼쪽 옆면이 위로
        TipDownRight => [p[0], -p[2], p[1]],   // 위가 보이는 오른쪽 옆면으로
        SpinLeft => [-p[1], p[0], p[2]],       // 위에서 보아 시계 반대쪽
        _ => [p[1], -p[0], p[2]],              // 시계 쪽
    };

    private void Turn(int button)
    {
        foreach (var face in _corner)
            for (int i = 0; i < 4; i++) face[i] = Turned(button, face[i]);
    }

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
    /// 지금 윗면 화살표가 가리키는 쪽(<see cref="Ways"/> 번호) — <c>0x0041B700</c>.
    /// </summary>
    /// <remarks>
    /// 귀퉁이 넷이 모두 윗면(z=+100)에 있는 면을 찾고(<c>0x0041B8A0</c>), 그 면의 화살표 귀퉁이가 V0~V3 가운데
    /// 어느 것인지 본다. 그림도 같은 좌표를 비추므로 보이는 쪽과 가는 쪽이 어긋날 수 없다.
    /// </remarks>
    public int Arrow
    {
        get
        {
            for (int f = 0; f < 6; f++)
            {
                if (!_corner[f].All(c => c[2] == 100)) continue;
                var p = _corner[f][_arrow[f]];
                for (int k = 0; k < 4; k++)
                    if (Corner[k][0] == p[0] && Corner[k][1] == p[1]) return k;
            }
            return 0;
        }
    }

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
    /// 판을 짓는다 — 원본의 <c>0x0049CFB0</c> 차례 그대로다.
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
        for (int f = 0; f < 6; f++)
            _corner[f] = [.. FaceCorners[f].Select(v => (int[])Corner[v].Clone())];

        // 마주 보는 면 둘에 같은 값을 넣는다(0x004ACE4A) — 귀퉁이 차례가 마주 보게 짜여 있어 비친 꼴이 된다.
        _arrow[0] = _arrow[1] = rng.Next(4);
        _arrow[2] = _arrow[3] = rng.Next(4);
        _arrow[4] = _arrow[5] = rng.Next(4);

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
    /// 수직으로 90도 굴린다 — 넘어뜨리기 단추 넷 가운데 하나다.
    /// </summary>
    /// <remarks>
    /// 다 구르면 <b>새로 위에 온 면의 화살표</b>가 가리키는 쪽으로 좌대가 한 칸 움직인다(<c>0x0049C660</c>) —
    /// 넘어뜨린 쪽이 아니다.
    /// </remarks>
    /// <param name="button"><see cref="TipUpLeft"/> · <see cref="TipDownLeft"/> · <see cref="TipUpRight"/> · <see cref="TipDownRight"/>.</param>
    public void Roll(int button)
    {
        if (Over != null) return;

        Turn(button);

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

    /// <summary>
    /// 수평으로 90도 돌린다. 좌대는 안 움직이고, 연달아는 못 한다.
    /// </summary>
    /// <remarks>
    /// 원본에는 깃발이 따로 없다 — 수평 단추를 누르면 <b>두 단추가 다 꺼져</b> 사라지고(<c>0x0049CC6C</c>),
    /// 넘어뜨리기가 끝나야 다시 켜진다(<c>0x0049CB97</c>). 그래서 왼쪽으로 돌린 뒤 오른쪽으로도 못 돌린다.
    /// </remarks>
    /// <param name="clockwise">위에서 보아 시계 쪽인가(<see cref="SpinRight"/>). 아니면 <see cref="SpinLeft"/>.</param>
    public bool Spin(bool clockwise)
    {
        if (Over != null || JustSpun) return false;

        Turn(clockwise ? SpinRight : SpinLeft);
        JustSpun = true;
        Moves++;
        return true;
    }
}
