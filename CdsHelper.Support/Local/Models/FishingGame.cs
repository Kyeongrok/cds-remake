namespace CdsHelper.Support.Local.Models;

/// <summary>
/// 미니 게임 「낚시 게임」 — 바늘을 떨어뜨려 바닥의 대어를 낚는 사다리 타기.
/// </summary>
/// <remarks>
/// 게임의 <c>0x0047BDD0</c> 이고 판을 까는 곳은 <c>0x0047B7A5</c> 다.
/// <code>
///   0x0056EDB0  바다에서 바늘을 떨어뜨려서 바닥에 있는 대어를 낚는 게임입니다.
///               낚시바늘은 줄을 따라 내려갑니다.
///               내려가는 도중에 화살표를 클릭하든지 ←→버튼을 누르면 교차하는
///               데에서 낚시바늘을 옆으로 이동할 수 있습니다만, 다음에 교차하는
///               데에서는 반드시 밑으로 내려갑니다.
/// </code>
/// 판은 <b>일곱 칸 x 여섯 줄</b>이다 — <c>0x0047B7C3</c> 이 <c>[0x118]</c> 부터
/// 마흔두 칸을 0 으로 지우고, 내려가는 곳(<c>0x0047AB41</c> 벌)이 자리를 <b>+7</b>(곧장
/// 아래) · <b>+8</b>(오른쪽 아래) · <b>+6</b>(왼쪽 아래) 로 옮긴다.
/// </remarks>
public sealed class FishingGame
{
    /// <summary>칸과 줄(<c>0x0047B7D2</c> 의 <c>0x2A</c> = 7 x 6).</summary>
    public const int Columns = 7, Rows = 6, Cells = Columns * Rows;

    /// <summary>길을 되짚는 걸음 수(<c>0x0047B85F</c> 의 <c>cmp $7</c>).</summary>
    private const int WalkSteps = 7;

    /// <summary>뿌리는 것 수(<c>0x0047B8D5</c> 의 <c>cmp $0xF</c>).</summary>
    private const int Scattered = 15;

    /// <summary>한 줄에 드는 틱(<c>0x0047AB0F</c> 의 <c>cmp $0x28</c>).</summary>
    public const int TicksPerRow = 40;

    /// <summary>바다 것을 만나는 틱(<c>0x0047AB7A</c> 의 <c>cmp $0xA</c>).</summary>
    private const int MeetTick = 10;

    /// <summary>바늘의 첫 높이와 바닥(<c>0x0047B7D7</c> 의 <c>0x40</c>, <c>0x0047AC50</c> 의 <c>0x168</c>).</summary>
    public const int TopY = 0x40, FloorY = 0x168;

    /// <summary>칸에 든 것.</summary>
    public const int Empty = 0, Path = 1, Squid = 2, Octopus = 4;

    /// <summary>
    /// 결과 — <c>[0x1F4]</c> 그 자체다. <c>0x0047AD41</c> 의 뜀표가 이 번호로 갈린다.
    /// </summary>
    public enum Catch
    {
        /// <summary>아직 내려가는 중.</summary>
        None = 0,

        /// <summary>오징어를 낚았다 — "왓! 오징어가 얼굴에 먹물을 토했다!"</summary>
        SquidCaught = 1,

        /// <summary>낙지를 낚았다 — "너무 징그러워서 갑판에 내동댕이쳤다."</summary>
        OctopusCaught = 2,

        /// <summary>잡어를 낚았다 — 두 갈래가 다 같은 글이다.</summary>
        SmallFry = 3,

        /// <summary>잡어를 낚았다(다른 갈래).</summary>
        SmallFryToo = 4,

        /// <summary><b>대어을 낚았다</b> — 이때만 이긴 것으로 친다.</summary>
        BigOne = 5,

        /// <summary>바닥에 걸렸다 — "[지구를 낚았다]고 해야하나."</summary>
        Seabed = 6,
    }

    /// <summary>헤엄쳐 다니는 것 — 열 마리다(<c>0x0047BA1E</c>).</summary>
    public const int Swimmers = 10;

    /// <summary>
    /// 헤엄치는 것에 바늘이 걸리는 틱 — 가는 쪽마다 다르다.
    /// </summary>
    /// <remarks>
    /// 오른쪽으로 가는 것은 틱 16(<c>0x0047ABD0</c>), 왼쪽은 틱 14(<c>0x0047AC10</c>)에서
    /// 잰다. 칸 안에서 자리가 그만큼 어긋나 있어서다.
    /// </remarks>
    public const int MeetRight = 16, MeetLeft = 14;

    /// <summary>헤엄치는 것 하나.</summary>
    /// <param name="Cell">지금 있는 칸.</param>
    /// <param name="Way">가는 쪽 — 1 이 오른쪽, 2 가 왼쪽이다.</param>
    /// <param name="Kind">그림 갈래(0·1). 다섯에 넷이 1 이다(<c>0x0047BA03</c>).</param>
    public readonly record struct Swimmer(int Cell, int Way, int Kind);

    private readonly Swimmer[] _swim = new Swimmer[Swimmers];

    /// <summary>헤엄쳐 다니는 것들.</summary>
    public IReadOnlyList<Swimmer> Fish => _swim;

    private readonly int[] _cell = new int[Cells];

    /// <summary>대어가 있는 칸(<c>[0x1DC]</c>).</summary>
    public int BigOneColumn { get; }

    /// <summary>바늘을 떨어뜨리는 칸(<c>[0x1D8]</c>). 고르는 것이 아니라 굴린다.</summary>
    public int DropColumn { get; }

    /// <summary>
    /// 바늘이 지금 있는 자리(<c>[0x1E0]</c>). 처음엔 판 위(음수)고, 바닥에 닿으면
    /// <see cref="Cells"/> 위다.
    /// </summary>
    public int At { get; private set; }

    /// <summary>다음 교차점에서 옆으로 갈지 — <c>1</c> 오른쪽, <c>-1</c> 왼쪽, <c>0</c> 곧장.</summary>
    public int Lean { get; private set; }

    /// <summary>그 칸에서 그 쪽으로 헤엄치는 잡어. 없으면 null.</summary>
    private Swimmer? SwimmerAt(int cell, int way)
    {
        foreach (var fish in _swim)
            if (fish.Cell == cell && fish.Way == way) return fish;
        return null;
    }

    /// <summary>낚은 것. <see cref="Catch.None"/> 이면 아직이다.</summary>
    public Catch Got { get; private set; }

    /// <summary>
    /// 걸린 잡어 — 잡어를 안 낚았으면 null. <b>그 놈의 그림이 그대로 딸려 올라온다.</b>
    /// </summary>
    /// <remarks>
    /// 갈래가 둘이라(<see cref="Swimmer.Kind"/>) 어느 놈이 걸렸는지 적어 두지 않으면 올라오는
    /// 그림을 고를 수 없다 — 적어 두기 전에는 늘 0번 그림이 올라와, 낚은 고기와 <b>딴 고기</b>가
    /// 딸려 왔다.
    /// </remarks>
    public Swimmer? Caught { get; private set; }

    /// <summary>바늘의 높이(<c>[0xF8]</c>). 한 틱에 한 점씩 내려간다.</summary>
    public int Y { get; private set; } = TopY;

    /// <summary>이 줄에서 몇 틱째인지(<c>[0x1E4]</c>). 마흔이면 다음 줄이다.</summary>
    public int Tick { get; private set; }

    /// <summary>떨어뜨리기 시작했나(<c>[0x200]</c>). 누르기 전에는 배 밑에 매달려 있다.</summary>
    public bool Started { get; private set; }

    /// <summary>이번 틱은 안 내려간다(<c>[0x1F8]</c>). 떨어뜨린 첫 틱이 그렇다 — 그래야 마흔 틱째에 y 103 가로줄에 닿는다.</summary>
    /// <summary>
    /// 이번 틱은 안 내려간다(<c>[0x1F8]</c>).
    /// </summary>
    /// <remarks>
    /// 처음 한 틱과, <b>방향을 받아들인 틱마다</b> 선다(<c>0x0047A935</c> 벌).
    /// </remarks>
    private bool _hold = true;

    /// <summary>
    /// 바늘의 가로 자리. 옆으로 가는 동안은 틱만큼 밀린다(<c>0x0047B0AC</c> ·
    /// <c>0x0047B0F0</c> 의 <c>± [0x1E4]</c>).
    /// </summary>
    public double HookX => Column * 40 + (Lean > 0 ? Tick : Lean < 0 ? -Tick : 0);

    /// <summary>한 줄(한 칸)의 길이. 가로 칸 사이도 세로 줄 사이도 이만큼이다.</summary>
    public const int Span = 40;

    /// <summary>그리는 가로 자리 — <see cref="HookX"/> 그대로다.</summary>
    /// <remarks>
    /// 게임은 옆으로 건너는 줄(<c>[0x1F0] ≠ 0</c>)에서 <b>마흔 틱 내내 가로로만</b> 한 점씩 가고
    /// 높이는 안 늘린다(<c>0x0047B05C</c> 의 <c>[0xF8]++</c> 는 곧장 내려갈 때만 탄다).
    /// 그래서 가로든 세로든 빠르기가 늘 틱당 한 점이다.
    /// </remarks>
    public double DrawX => HookX;

    /// <summary>그리는 세로 자리 — <see cref="Y"/> 그대로다. 건너는 동안은 멎어 있다.</summary>
    public double DrawY => Y;

    /// <summary>떨어뜨린다.</summary>
    public void Drop() => Started = true;

    /// <summary>대어를 낚았나(<c>0x0047AD6C</c> 의 <c>[0x9C] = 1</c>).</summary>
    public bool Won => Got == Catch.BigOne;

    /// <summary>그 칸에 든 것.</summary>
    public int CellAt(int cell) => _cell[cell];

    /// <summary>지금 칸 번호. 판 위에 있을 때도 칸은 있다.</summary>
    public int Column => ((At % Columns) + Columns) % Columns;

    /// <summary>지금 줄 번호. 아직 안 내려왔으면 -1 이다.</summary>
    public int Row => At < 0 ? -1 : At / Columns;

    /// <summary>
    /// 판을 깐다(<c>0x0047B7A5</c>).
    /// </summary>
    /// <remarks>
    /// <code>
    /// 47b7b3  칸 = rand(7)
    /// 47b7ff  일곱 걸음을 되짚는다 — 왼쪽 끝이면 rand(2), 오른쪽 끝이면 rand(2)*2,
    ///         아니면 rand(3). 2 는 왼쪽(-1)으로 바꾼다
    /// 47b882  걸어 온 끝 칸이 <b>대어 칸</b>이 된다
    /// 47b88e  그 길 여섯 칸을 1 로 막는다 — 여기엔 아무것도 안 뿌린다
    /// 47b89f  빈 칸에 열다섯을 뿌린다
    /// 47b8b8  rand(11) 이 7 밑이면 <b>오징어(2)</b>, 아니면 <b>낙지(4)</b>
    /// </code>
    /// 길을 먼저 막고 뿌리므로 <b>대어까지 가는 깨끗한 길이 늘 하나 있다</b>.
    /// </remarks>
    public FishingGame(Random rng)
    {
        DropColumn = rng.Next(Columns);
        var path = new int[WalkSteps];

        // <b>끝 칸이 출발 칸이면 처음부터 다시 굴린다</b>(0x0047B864) — 그래서 대어는
        // 반드시 딴 칸에 있고, 이기려면 적어도 한 번은 옆으로 건너야 한다.
        int column;
        do
        {
            column = DropColumn;
            for (int i = 0; i < WalkSteps; i++)
            {
                int step = column == 0 ? rng.Next(2)
                         : column == Columns - 1 ? rng.Next(2) * 2
                         : rng.Next(3);
                if (step == 2) step = -1;

                int at = i == 0 ? column : path[i - 1] + Columns;
                path[i] = at + step;
                column += step;
            }
        }
        while (column == DropColumn);

        BigOneColumn = column;

        // 길 여섯 칸을 막는다. 일곱째는 판 밖이라 안 쓴다.
        for (int i = 0; i < Rows; i++)
            if (path[i] >= 0 && path[i] < Cells) _cell[path[i]] = Path;

        for (int n = 0; n < Scattered; n++)
        {
            int at = rng.Next(Cells);
            if (_cell[at] > Empty) { n--; continue; }
            _cell[at] = rng.Next(11) < 7 ? Squid : Octopus;
        }

        // 헤엄쳐 다니는 것 열 마리(0x0047B92E~0x0047BA22).
        //
        // <b>빈 칸에만 놓는다</b> — 0x0047B941 이 칸 값이 0 보다 크면 그 마리를 물리고
        // 다시 굴린다. 길·오징어·낙지는 물론이고 <b>먼저 놓인 잡어도 피한다</b>
        // (0x0047B955 가 앉은 칸을 6 으로 적어 두기 때문이다).
        //
        // 가는 쪽도 굴리는 것이 아니라 <b>이웃을 보고</b> 정한다 — 0x0047B976 이 왼쪽 칸이
        // 오징어(2)나 낙지(4)면 그쪽으로 안 간다. 양쪽이 다 막혔으면 오른쪽으로 둔다.
        // 줄마다 <b>미리 걷는 걸음 수</b>(0x0047B8DA · 0x0047B902) — 바늘이 그 줄에 닿을
        // 때까지 흐른 줄 수다. 길이 옆으로 꺾인 줄에서는 한 걸음이 더 붙는다.
        var lead = new int[Rows];
        lead[0] = path[0] == DropColumn ? 1 : 2;
        for (int i = 1; i < Rows; i++)
            lead[i] = lead[i - 1] + 1 + (path[i] - path[i - 1] != Columns ? 1 : 0);

        var taken = new bool[Cells];
        for (int k = 0; k < Swimmers; k++)
        {
            int at;
            do { at = rng.Next(Cells); } while (_cell[at] != Empty || taken[at]);
            taken[at] = true;

            // <b>가는 쪽은 굴리지 않는다</b> — 한쪽으로 두고 그 줄의 걸음 수만큼 미리
            // 걷힌다(0x0047B961~0x0047B9E8). 벽이나 오징어·낙지에 닿으면 돌아선다.
            var fish = new Swimmer(at, 1, rng.Next(5) >= 4 ? 0 : 1);
            for (int step = 0; step < lead[at / Columns]; step++) fish = Ahead(fish);

            _swim[k] = fish;
        }

        // 바늘은 떨어뜨리는 칸의 <b>맨 윗줄 위</b>에서 시작한다(0x0047BA33 의 -7).
        At = DropColumn - Columns;
    }

    /// <summary>「←→」 — 다음 교차점에서 옆으로 갈지 말지 잡는다.</summary>
    /// <remarks>
    /// 게임은 <c>[0x1EC]</c> 에 적어 두었다가 <c>[0x1F0]</c> 과 같아질 때 옮긴다
    /// (<c>0x0047AB2C</c>). 곧 <b>한 교차점에 한 번</b>이고, 옮기고 나면 지워져
    /// 다음 교차점에서는 반드시 밑으로 내려간다.
    /// </remarks>
    /// <summary>
    /// 헤엄치는 것들이 <b>한 칸씩</b> 옮겨 간다 — 바늘이 한 줄 내려가는 것과 같은 빠르기다.
    /// </summary>
    /// <remarks>
    /// 게임도 틱이 마흔일 때에만 옮긴다(<c>0x0047B3E2</c> · <c>0x0047B4CA</c>).
    /// <b>앞 칸에 오징어나 낙지가 있거나 줄 끝이면 돌아선다</b> — 자리는 그대로 두고
    /// 가는 쪽만 뒤집는다(<c>0x0047B45B</c> 가 상태에 2 를 넣는 자리다).
    /// </remarks>
    private void Swim()
    {
        for (int k = 0; k < Swimmers; k++) _swim[k] = Ahead(_swim[k]);
    }

    /// <summary>한 마리를 한 걸음 옮긴다. 막혔으면 자리는 두고 돌아선다.</summary>
    private Swimmer Ahead(Swimmer fish)
    {
        int next = fish.Cell + (fish.Way == 1 ? 1 : -1);

        bool wall = fish.Way == 1 ? next % Columns == 0
                                  : fish.Cell % Columns == 0;
        if (wall || next < 0 || next >= Cells || _cell[next] >= Squid)
            return fish with { Way = fish.Way == 1 ? 2 : 1 };

        return fish with { Cell = next };
    }

    /// <summary>
    /// 옆으로 가겠다고 한다. <b>그 자리에서 바로 꺾지 않는다.</b>
    /// </summary>
    /// <remarks>
    /// 바늘은 사다리 위로만 다니므로 가로줄을 타려면 <b>꼭짓점</b>, 곧 줄과 줄이 만나는
    /// 데까지 가야 한다. 그래서 여기서는 <see cref="Wish"/> 에 적어만 두고, 줄이 바뀌는
    /// 자리에서 <see cref="Step"/> 이 그것을 <see cref="Lean"/> 으로 올린다.
    ///
    /// 줄 첫머리(<c>Tick == 0</c>)에 누르면 바로 그 자리가 꼭짓점이라 그 줄에서 곧장
    /// 건넌다 — 기다림이 없다.
    /// </remarks>
    public void Steer(int way)
    {
        if (Got != Catch.None || !Started) return;

        int wish = way > 0 && Column + Wish < Columns - 1 ? 1
                 : way < 0 && Column + Wish > 0 ? -1 : 0;

        // 건너는 중에는 안 받는다 — 게임은 여기서 [0x1EC] 가 바뀌면 줄 끝에서 건너기를 처음부터
        // 다시 해 바늘이 제자리로 튄다. 건넌 다음 꼭짓점은 어차피 반드시 밑으로 간다.
        if (Lean != 0) return;

        // <b>곧장 건너는 길은 없다</b> — 원본은 누른 것을 적어만 두고(0x0047A935 벌),
        // 다음 마흔 틱 경계에서 비로소 건너기를 시작한다(0x0047AB0F).
        Wish = wish;

        // 방향을 받아들인 틱은 <b>안 내려간다</b>(0x0047A935 의 [0x1F8] = 1).
        if (wish != 0) _hold = true;
    }

    /// <summary>다음 꼭짓점에서 꺾을 쪽. 0 이면 곧장 내려간다.</summary>
    public int Wish { get; private set; }

    /// <summary>
    /// 한 틱. 게임은 <c>0x0047AAA0</c> 이 화면을 새로 그릴 때마다 이걸 한다.
    /// </summary>
    /// <remarks>
    /// <code>
    /// 47aae9  [0x1E4]++                 ; 이 줄의 틱
    /// 47b05c  [0x1F8] 이 0 이면 [0xF8]++ ; 한 틱에 한 점 내려간다
    /// 47ab0f  [0x1E4] 이 0x28(40)이면 줄을 넘긴다 — 옆으로 갈 참이면 대각선으로
    /// 47ab7a  [0x1E4] 이 0xA(10)이면 그 칸에 뭐가 있는지 본다
    /// 47ac50  [0xF8] 이 0x168(360)이면 바닥이다
    /// </code>
    /// 한 줄이 마흔 틱이고 옆으로 가면 그동안 가로로도 한 틱에 한 점씩 밀려,
    /// 딱 마흔 점(한 칸)을 옮겨 간다.
    /// </remarks>
    /// <returns>아직 내려가는 중이면 true.</returns>
    public bool Step()
    {
        if (Got != Catch.None || !Started) return Got == Catch.None;

        Tick++;

        // 건너는 줄에서는 안 내려간다(0x0047AF5A → 0x0047B07F·0x0047B0C3 이 [0xF8]++ 를 건너뛴다).
        if (_hold) _hold = false;
        else if (Lean == 0) Y++;

        if (Tick >= TicksPerRow)
        {
            Tick = 0;

            // 0x0047AB22: 건너기를 마치면 대각선 아래 칸으로, 적어 둔 쪽이 있으면 칸은 그대로 두고
            // 이번 줄을 건너는 데 쓰고, 아니면 곧장 아래 칸으로 간다.
            if (Lean != 0)
            {
                At += Columns + Lean;
                Lean = 0;
                Wish = 0;
            }
            else if (Wish != 0)
            {
                Lean = Wish;
                Wish = 0;
            }
            else At += Columns;

            Swim();
        }
        else if ((Tick == MeetRight || Tick == MeetLeft) && Lean == 0 && At >= 0 && At < Cells
                 && SwimmerAt(At, Tick == MeetRight ? 1 : 2) is { } hooked)
        {
            Caught = hooked;
            // 오른쪽으로 헤엄치는 놈이면 3, 왼쪽이면 4 다(0x0047ABF1 · 0x0047AC31).
            // <b>말은 둘 다 같다</b> — 뜀표가 한 자리로 모인다(0x0047AD60).
            Got = Tick == MeetRight ? Catch.SmallFry : Catch.SmallFryToo;
            return false;
        }
        else if (Tick == MeetTick && At >= 0 && At < Cells)
        {
            Got = _cell[At] switch
            {
                Squid => Catch.SquidCaught,
                Octopus => Catch.OctopusCaught,
                _ => Catch.None,
            };
            if (Got != Catch.None) return false;
        }

        if (Y >= FloorY)
        {
            Got = Column == BigOneColumn ? Catch.BigOne : Catch.Seabed;
            return false;
        }
        return true;
    }

}
