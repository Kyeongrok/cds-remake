namespace CdsHelper.Maze;

/// <summary>
/// 미니 게임 「미궁 64 퍼즐」 — 방 예순넷을 <b>한 번씩만</b> 밟고 출구로 나가는 놀이.
/// </summary>
/// <remarks>
/// 게임의 <c>0x0042C8A0</c> 이고 판을 까는 곳은 <c>0x0042A770</c> 이다. 설명 글은
/// <c>0x00559D40</c> 벌에 그대로 있다.
/// <code>
///   바닥을 전부 한번씩만 통과해, 출구로 나가 주십시오.
///   나아갈 방향의 표시 위에서 마우스의 왼쪽 버튼을 누르며 이동합니다.
///   되돌릴 때에는 [U N D O(취소)]의 위에서 왼쪽 버튼을 누릅니다.
///   [U N D O(취소)]를 사용할 수 있는 것은 3회까지입니다.
///   키보드의 경우, ↑↓←→와 Roll(Page)Up·Roll(Page)Down 키로 나아갈 방향을 지정해
///   Return(Enter)키로 이동합니다.
///   보물 상자는 숫자가 적은 순서로 밖에 열지 못합니다만 열지 않아도 밖으로 나갈 수
///   있습니다. 바닥을 전부 통과하지 않고 출구로 가면 처음으로 돌아갑니다.
///   이것도 3회까지입니다.
/// </code>
/// <b>벽이 없다.</b> 판을 까는 <c>0x0042A770</c> 이 만드는 것은 밟음 표와 온 길뿐이고,
/// 이기는 조건도 「예순넷을 다 밟았나」 하나다(<c>0x0042AFE9</c>). 곧 <b>4x4x4 격자의
/// 해밀턴 경로</b>를 찾는 놀이다 — 위아래로도 한 층씩 오르내린다.
/// </remarks>
public sealed class MazePuzzle
{
    /// <summary>한 변의 칸 수와 방 수(<c>0x0042A7A3</c> 의 <c>4·4·4</c>).</summary>
    public const int Side = 4, Rooms = 64;

    /// <summary>보물 상자 수. 넷을 다 열어야 온전한 클리어다(<c>cmp $0x4,0x2f8</c>).</summary>
    public const int Chests = 4;

    /// <summary>되돌리기와 처음부터 다시가 각각 몇 번까지인지.</summary>
    public const int MaxUndo = 3, MaxRestart = 3;

    /// <summary>
    /// 상자를 연 수만큼 주는 금화(<c>0x0042B136</c> 의 점프표 <c>0x0042B170</c>).
    /// </summary>
    /// <remarks>
    /// 한 개 10 · 둘 110 · 셋 1110 · 넷 11110 닢이다. <b>발견 대본이 거는 미궁이 이 갈래다</b> —
    /// <c>0x00408D80</c> 이 <c>0x0042C8A0(1)</c> 로 부르고, 그 1 이 인스턴스 <c>+0x310</c> 이 된다.
    /// </remarks>
    public static readonly int[] ChestPrize = [10, 110, 1110, 11110];

    /// <summary>연 상자만큼의 금화. 하나도 못 열었으면 0.</summary>
    public int Prize =>
        Opened >= 1 && Opened <= Chests ? ChestPrize[Opened - 1] : 0;

    /// <summary>
    /// 여섯 방향. <b>차례가 게임 것</b>이다 — 화살표 조각도 이 차례로 두 장씩이다.
    /// </summary>
    /// <remarks>
    /// 갈 수 있는 이웃을 재는 곳이 <c>0x0042A560</c> 이고, 그 결과를 <c>0x2C0</c>부터
    /// 넉 바이트씩 여섯 자리에 적는다. 화살표를 그리는 곳은 <c>0x0042C0A0</c> 부터
    /// 여섯 벌인데, 벌마다 <c>[0x2FC]</c>(지금 짚은 방향)와 견줘 <b>흰 것과 금빛 것</b>을
    /// 갈라 쓴다.
    /// <code>
    /// 42c12a  ebp = 3                  ; 이 방향의 금빛 조각 번호
    /// 42c12f  eax = [0x2FC] - 2        ; 이 방향인가
    /// 42c136  cmp $1,%eax
    /// 42c139  adc $-1,%ebp             ; 아니면 하나 앞(흰 것)
    /// 42c141  ebp = ebp * 256 * 3      ; 조각 하나가 768바이트(32x24)
    /// </code>
    /// </remarks>
    public static readonly (int Step, string Name)[] Ways =
    [
        (-Side * Side, "위"), (+Side * Side, "아래"),
        (-Side, "뒤"), (+Side, "앞"),
        (-1, "왼쪽"), (+1, "오른쪽"),
    ];

    /// <summary>
    /// 층 한 벌의 <b>한가운데 넷</b>. 출구로는 안 쓴다.
    /// </summary>
    /// <remarks>
    /// <c>0x0042A898</c> 이 출구를 <c>5·6·9·10</c> 과 견주고, 열여섯씩 얹어 가며 네 층을
    /// 다 본다. 걸리면 <c>0x0042A7E4</c> 로 돌아가 출구만 다시 굴린다.
    /// </remarks>
    public static readonly int[] Middle = [5, 6, 9, 10];

    /// <summary>끝난 꼴.</summary>
    public enum Result
    {
        /// <summary>아직.</summary>
        Playing,

        /// <summary>순서를 어겨 덫이 작동했다(<c>0x0042AADC</c>).</summary>
        Trapped,

        /// <summary>세 번째로 다 못 밟고 출구에 닿았다(<c>0x0042B0BF</c>).</summary>
        Failed,

        /// <summary>포기했다.</summary>
        GaveUp,

        /// <summary>돌파했다.</summary>
        Cleared,

        /// <summary>실수 없이 돌파하고 보물까지 얻었다.</summary>
        Perfect,
    }

    private readonly int[] _step = new int[Rooms];      // 몇 번째로 밟았나(0 이면 아직)
    private readonly int[] _chestAt = new int[Chests];  // 상자가 앉은 방
    private readonly bool[] _opened = new bool[Chests];
    private readonly List<int> _path = [];

    /// <summary>들머리와 출구.</summary>
    public int Start { get; private set; }

    public int Exit { get; private set; }

    /// <summary>지금 선 방.</summary>
    public int Here { get; private set; }

    /// <summary>지금까지 연 상자 수(<c>0x2F8</c>).</summary>
    public int Opened { get; private set; }

    /// <summary>되돌린 횟수(<c>0x304</c>)와 처음부터 다시 한 횟수(<c>0x308</c>).</summary>
    public int Undone { get; private set; }

    public int Restarted { get; private set; }

    /// <summary>끝났으면 그 꼴.</summary>
    public Result Over { get; private set; } = Result.Playing;

    private readonly Random _rng;

    public MazePuzzle(Random rng)
    {
        _rng = rng;
        Deal();
    }

    /// <summary>
    /// 판을 깐다(<c>0x0042A770</c>).
    /// </summary>
    /// <remarks>
    /// 들머리와 출구를 <c>rand(64)</c> 로 굴리는데 <b>짝이 맞아야 한다</b> —
    /// <c>0x0042A886</c> 이 두 자리의 좌표 차를 더해 홀수인지 본다. 4x4x4 격자는 흑백이
    /// 서른둘씩이라, 예순넷을 다 밟는 길은 반드시 <b>다른 색</b>에서 끝난다. 짝이 안 맞으면
    /// 아예 못 푸는 판이라 출구만 다시 굴린다.
    ///
    /// 상자 넷은 <c>rand(16)</c> 으로 층 안 자리를 잡고(<c>0x0042A8D6</c>), 들머리·출구와
    /// 겹치거나 서로 겹치면 다시 굴린다. 그러고는 <c>0x0042A94C</c> 가 <b>한 층에 하나씩</b>
    /// 가도록 층을 나눠 준다.
    /// </remarks>
    private void Deal()
    {
        Array.Clear(_step);
        Array.Clear(_opened);
        _path.Clear();
        Opened = 0;

        Start = _rng.Next(Rooms);
        do
        {
            Exit = _rng.Next(Rooms);
        }
        while (Exit == Start || Parity(Start) == Parity(Exit)
               || Middle.Contains(Exit % (Side * Side)));

        // 층 안 자리는 서로 다르게, 들머리·출구와도 안 겹치게.
        var spots = new List<int>();
        while (spots.Count < Chests)
        {
            int spot = _rng.Next(Side * Side);
            if (spots.Contains(spot)) continue;
            bool clash = false;
            for (int layer = 0; layer < Side; layer++)
            {
                int room = layer * Side * Side + spot;
                if (room == Start || room == Exit) clash = true;
            }
            if (!clash) spots.Add(spot);
        }

        // 층은 하나씩 나눠 갖는다.
        var layers = Enumerable.Range(0, Side).OrderBy(_ => _rng.Next()).ToArray();
        for (int i = 0; i < Chests; i++)
            _chestAt[i] = layers[i] * Side * Side + spots[i];

        Here = Start;
        _step[Here] = 1;
        _path.Add(Here);
    }

    /// <summary>격자를 흑백으로 나눈 색. 좌표 셋을 더한 값의 홀짝이다.</summary>
    public static int Parity(int room) =>
        (room % Side + room / Side % Side + room / (Side * Side)) & 1;

    /// <summary>몇 번째로 밟았나. 0 이면 아직 안 밟았다.</summary>
    public int StepAt(int room) => _step[room];

    /// <summary>밟은 방 수.</summary>
    public int Walked => _path.Count;

    /// <summary>그 방에 앉은 상자 번호(1~4). 없으면 0.</summary>
    public int ChestAt(int room)
    {
        for (int i = 0; i < Chests; i++)
            if (_chestAt[i] == room) return i + 1;
        return 0;
    }

    /// <summary>그 상자를 열었나.</summary>
    public bool ChestOpen(int number) => _opened[number - 1];

    /// <summary>여기서 갈 수 있는 방들.</summary>
    public IEnumerable<(int Room, string Name)> Moves()
    {
        foreach (var (step, name) in Ways)
        {
            int next = Neighbour(Here, step);
            if (next >= 0 && _step[next] == 0) yield return (next, name);
        }
    }

    /// <summary>한 칸 옆 방. 격자를 벗어나면 -1.</summary>
    public static int Neighbour(int room, int step)
    {
        int next = room + step;
        if (next < 0 || next >= Rooms) return -1;
        // 가로로 갈 때만 줄이 바뀌면 안 된다. 세로·층은 번호로 걸러진다.
        if (Math.Abs(step) == 1 && room / Side != next / Side) return -1;
        if (Math.Abs(step) == Side
            && room / (Side * Side) != next / (Side * Side)) return -1;
        return next;
    }

    /// <summary>그 방으로 옮긴다. 못 가면 false.</summary>
    public bool Walk(int room)
    {
        if (Over != Result.Playing) return false;
        if (!Moves().Any(m => m.Room == room)) return false;

        Here = room;
        _path.Add(room);
        _step[room] = _path.Count;
        return true;
    }

    /// <summary>
    /// 지금 선 방의 상자를 연다.
    /// </summary>
    /// <remarks>
    /// <b>숫자가 적은 것부터만 열린다</b>. 어기면 <c>0x0042AADC</c> — "순서를 지키지
    /// 않았으므로 보물 상자에 장치된 덫이 작동!" 하고 그 자리에서 끝난다.
    /// </remarks>
    /// <returns>연 상자 번호. 열 것이 없으면 0.</returns>
    public int OpenChest()
    {
        if (Over != Result.Playing) return 0;

        int number = ChestAt(Here);
        if (number == 0) return 0;

        // 이미 연 상자를 또 누르면 빈 상자라고 이른다(0x0042BA17 의 cmp 2).
        if (_opened[number - 1]) { Emptied = true; return 0; }

        if (number != Opened + 1)
        {
            Over = Result.Trapped;
            return number;
        }

        _opened[number - 1] = true;
        Opened++;
        return number;
    }

    /// <summary>
    /// 마지막 <see cref="OpenChest"/> 가 <b>이미 연 상자</b>였는지(<c>0x0042BA29</c>).
    /// </summary>
    /// <remarks>부르는 쪽이 「이 보물 상자는 이미 열려져 있었습니다.」를 내고 도로 내린다.</remarks>
    public bool Emptied { get; set; }

    /// <summary>되돌릴 수 있는지 — 한 발 이상 왔고 세 번을 안 넘겼을 때.</summary>
    public bool CanUndo => Over == Result.Playing && _path.Count > 1 && Undone < MaxUndo;

    /// <summary>「앞으로 돌아간다」 — 한 발 물린다.</summary>
    public bool Undo()
    {
        if (!CanUndo) return false;

        _step[_path[^1]] = 0;
        _path.RemoveAt(_path.Count - 1);
        Here = _path[^1];
        Undone++;
        return true;
    }

    /// <summary>
    /// 출구에 닿았다 — 다 밟았으면 나가고, 아니면 처음으로 돌아간다.
    /// </summary>
    /// <remarks>
    /// <c>0x0042AFA5</c> 가 예순넷을 훑어 안 밟은 방이 있는지 본다. 있으면
    /// <c>0x0042B03C</c> — 세 번째면 그대로 끝, 아니면 "방을 전부 돌지 않았기 때문에
    /// 입구로 되돌아 오고 말았다!" 하고 판을 다시 깐다(연 상자도 도로 잠긴다).
    ///
    /// 다 밟았으면 <c>0x0042AFF9</c> 가 <b>상자 넷을 다 열었고 되돌리기도 다시 하기도
    /// 안 썼는지</b> 본다 — 그래야 "실수하지 않고 미궁을 돌파해 보물을 손에 넣었네!" 다.
    /// </remarks>
    public Result Arrive()
    {
        if (Over != Result.Playing) return Over;
        if (Here != Exit) return Result.Playing;

        bool all = true;
        for (int i = 0; i < Rooms; i++)
            if (_step[i] == 0) { all = false; break; }

        if (!all)
        {
            if (Restarted >= MaxRestart - 1)
            {
                Restarted++;
                Over = Result.Failed;
                return Over;
            }
            Restarted++;
            int undone = Undone;
            Deal();
            Undone = undone;
            return Result.Playing;
        }

        Over = Opened == Chests && Undone == 0 && Restarted == 0
             ? Result.Perfect : Result.Cleared;
        return Over;
    }

    /// <summary>「포기한다」.</summary>
    public void GiveUp()
    {
        if (Over == Result.Playing) Over = Result.GaveUp;
    }
}
