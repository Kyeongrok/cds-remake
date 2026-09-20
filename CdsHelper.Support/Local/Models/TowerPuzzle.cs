namespace CdsHelper.Support.Local.Models;

/// <summary>
/// 미니 게임 「발라몬의 탑 퍼즐」 — 돌 판자를 한 기둥에 모으는 하노이 탑.
/// </summary>
/// <remarks>
/// 미니 게임 차림표의 여섯째 줄에서 곧바로 이어진다(<c>0x0045FB60</c>). 먼저
/// <c>0x00481FE0(4, 4, 8, 1, 1)</c> 로 «판자를 몇 장 사용하겠습니까?»(<c>0x00571E90</c>)
/// 를 물어 <b>넷에서 여덟까지</b> 받고, 그 수로 <c>0x00431740</c> 을 부른다.
///
/// 판을 까는 곳은 <c>0x00431284</c> 벌이다.
/// <code>
/// 431286  판자 여덟을 돈다 (cmp $8)
/// 43128c  쓰기로 한 장수보다 크면 0 — 안 쓴다
/// 431290  rand(3) + 1                 ; <b>기둥 셋 가운데 아무 데나</b>
/// 43129d  3 이 아니면 «아직 다 안 모였다» 를 세운다
/// </code>
/// 곧 <b>판자를 세 기둥에 흩어 놓고 시작해서 셋째 기둥에 다 모으면 이긴다</b>.
/// 여느 하노이 탑처럼 한 번에 맨 위 하나만 옮기고, 저보다 작은 판자 위에는 못 놓는다.
/// </remarks>
public sealed class TowerPuzzle
{
    /// <summary>기둥 수(<c>0x0043129D</c> 의 <c>cmp $3</c>).</summary>
    public const int Pegs = 3;

    /// <summary>판자는 넷에서 여덟까지(<c>0x0045FB6C</c> 의 <c>push $4 · push $8</c>).</summary>
    public const int LeastPlanks = 4, MostPlanks = 8;

    /// <summary>다 모아야 하는 기둥. 0부터 세어 <b>셋째</b>다.</summary>
    public const int Goal = 2;

    private readonly List<int>[] _peg = [[], [], []];

    /// <summary>판자 수.</summary>
    public int Planks { get; }

    /// <summary>몇 번 옮겼는지.</summary>
    public int Moves { get; private set; }

    /// <summary>들고 있는 판자. 없으면 -1.</summary>
    public int Held { get; private set; } = -1;

    /// <summary>들고 있는 판자를 집은 기둥.</summary>
    public int HeldFrom { get; private set; } = -1;

    /// <summary>다 모았나.</summary>
    public bool Won => _peg[Goal].Count == Planks;

    /// <param name="planks">쓸 판자 수(4~8).</param>
    public TowerPuzzle(int planks, Random rng)
    {
        Planks = Math.Clamp(planks, LeastPlanks, MostPlanks);

        // 판자마다 기둥을 굴린다. 큰 것이 밑에 가게 기둥마다 추려 쌓는다 —
        // 안 그러면 처음부터 규칙을 어긴 판이 나온다.
        for (int plank = 1; plank <= Planks; plank++)
            _peg[rng.Next(Pegs)].Add(plank);

        foreach (var peg in _peg) peg.Sort((a, b) => b.CompareTo(a));
    }

    /// <summary>그 기둥에 쌓인 판자. 앞이 밑(큰 것)이다.</summary>
    public IReadOnlyList<int> Stack(int peg) => _peg[peg];

    /// <summary>그 기둥의 맨 위 판자. 비었으면 0.</summary>
    public int Top(int peg) => _peg[peg].Count == 0 ? 0 : _peg[peg][^1];

    /// <summary>
    /// 기둥을 눌렀다 — 들고 있지 않으면 집고, 들고 있으면 놓는다.
    /// </summary>
    /// <remarks>
    /// 놓을 수 있는 자리는 <b>빈 기둥</b>이거나 <b>저보다 큰 판자 위</b>다.
    /// 집은 자리에 도로 놓는 것은 한 수로 안 센다.
    /// </remarks>
    /// <returns>무언가 일어났으면 true.</returns>
    public bool Tap(int peg)
    {
        if (Won) return false;

        if (Held < 0)
        {
            if (_peg[peg].Count == 0) return false;
            Held = _peg[peg][^1];
            HeldFrom = peg;
            _peg[peg].RemoveAt(_peg[peg].Count - 1);
            return true;
        }

        int top = Top(peg);
        if (top != 0 && top < Held) return false;      // 작은 것 위에는 못 놓는다

        _peg[peg].Add(Held);
        if (peg != HeldFrom) Moves++;

        Held = -1;
        HeldFrom = -1;
        return true;
    }

    /// <summary>들고 있던 것을 도로 놓는다.</summary>
    public void PutBack()
    {
        if (Held < 0) return;

        _peg[HeldFrom].Add(Held);
        Held = -1;
        HeldFrom = -1;
    }

    /// <summary>이 판을 푸는 데 드는 가장 적은 수. 뽐낼 거리로 쓴다.</summary>
    /// <remarks>
    /// 흩어진 자리에서 한 기둥으로 모으는 최소 수는 널비 우선으로 재면 되는데,
    /// 판자가 여덟이면 자리가 3^8 = 6561 뿐이라 금방 끝난다.
    /// </remarks>
    public int Shortest()
    {
        var start = new int[Planks];
        for (int peg = 0; peg < Pegs; peg++)
            foreach (int plank in _peg[peg]) start[plank - 1] = peg;
        if (Held > 0) start[Held - 1] = HeldFrom;

        string Key(int[] at) => string.Join(",", at);
        var seen = new HashSet<string> { Key(start) };
        var queue = new Queue<(int[] At, int Steps)>();
        queue.Enqueue((start, 0));

        while (queue.Count > 0)
        {
            var (at, steps) = queue.Dequeue();
            if (at.All(p => p == Goal)) return steps;

            for (int plank = 0; plank < Planks; plank++)
            {
                // 저보다 작은 것이 같은 기둥에 있으면 맨 위가 아니다.
                bool onTop = true;
                for (int other = 0; other < plank; other++)
                    if (at[other] == at[plank]) { onTop = false; break; }
                if (!onTop) continue;

                for (int peg = 0; peg < Pegs; peg++)
                {
                    if (peg == at[plank]) continue;

                    bool room = true;
                    for (int other = 0; other < plank; other++)
                        if (at[other] == peg) { room = false; break; }
                    if (!room) continue;

                    var next = (int[])at.Clone();
                    next[plank] = peg;
                    if (seen.Add(Key(next))) queue.Enqueue((next, steps + 1));
                }
            }
        }
        return -1;
    }
}
