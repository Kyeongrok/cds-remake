namespace CdsHelper.Support.Local.Models;

/// <summary>
/// 미니 게임 「코인 게임」 — 이름은 <b>천칭 퍼즐</b>이고, 무게가 다른 금화 하나를
/// 나무 천칭 세 번으로 가려내는 놀이다.
/// </summary>
/// <remarks>
/// 게임의 <c>0x004531F0</c> 이고 판을 까는 곳은 <c>0x00452B90</c> 이다.
/// <code>
///   0x0053B0C0  금 천칭에는 함정이 있습니다. 함정에 빠지지 않게 하기 위해서는
///               무게가 다른 금화를 가려내고 천칭이 평형을 이루게 해야 합니다.
///   0x0053B13D  나무 천칭을 3번까지 쓰고 무게가 다른 금화를 선택해 주십시오.
///   0x0053B048  무게를 단다(WEIGH) · 금화를 내린다(CLEAR) · 가짜 금화 선택(DECIDE)
/// </code>
/// </remarks>
public sealed class CoinPuzzle
{
    /// <summary>금화 수는 <c>rand(5) + 9</c> 다(<c>0x00452C25</c>).</summary>
    public const int LeastCoins = 9, MoreCoins = 5;

    /// <summary>천칭은 세 번까지(<c>0x00450F47</c> 의 <c>movl $0x3</c>).</summary>
    public const int Weighings = 3;

    /// <summary>접시 하나에 여섯까지(<c>0x00450710</c> 의 <c>cmp $0x6</c>).</summary>
    public const int PanRoom = 6;

    /// <summary>성한 금화의 무게. 가짜만 하나 더하거나 덜하다.</summary>
    public const int Sound = 2;

    /// <summary>놀이 속에서 깨끗이 풀었을 때의 삯(<c>0x00450C79</c> 의 <c>0xBB8</c>).</summary>
    public const int Prize = 3000;

    /// <summary>천칭이 기운 쪽.</summary>
    public enum Tilt
    {
        /// <summary>평형.</summary>
        Level = 0,

        /// <summary>왼쪽이 무겁다.</summary>
        Left = 1,

        /// <summary>오른쪽이 무겁다.</summary>
        Right = -1,
    }

    /// <summary>단 한 번의 자취 — 어느 금화를 어느 접시에 놓고 어찌 기울었나.</summary>
    public sealed record Record(int[] Left, int[] Right, Tilt Result);

    private readonly int[] _weight;
    private readonly List<int> _left = [];
    private readonly List<int> _right = [];
    private readonly List<Record> _log = [];

    /// <summary>금화 수(9~13).</summary>
    public int Coins => _weight.Length;

    /// <summary>가짜 금화 번호(0부터).</summary>
    public int Fake { get; private set; }

    /// <summary>가짜가 무거운 쪽인지.</summary>
    public bool Heavy { get; private set; }

    /// <summary>지금까지 단 횟수.</summary>
    public int Weighed => _log.Count;

    /// <summary>단 자취.</summary>
    public IReadOnlyList<Record> Log => _log;

    /// <summary>왼쪽·오른쪽 접시에 놓인 금화.</summary>
    public IReadOnlyList<int> Left => _left;

    public IReadOnlyList<int> Right => _right;

    /// <summary>가려냈으면 true, 잘못 골랐으면 false, 아직이면 null.</summary>
    public bool? Won { get; private set; }

    /// <summary>
    /// 판을 깐다(<c>0x00452A80</c>).
    /// </summary>
    /// <remarks>
    /// <code>
    /// 452af9  금화를 다 무게 2 로 깐다
    /// 452b15  rand(2) 로 무거운 가짜인지 가벼운 가짜인지 고르고
    /// 452b32  rand(금화수) 자리의 무게를 하나 올리거나(452b43) 내린다
    /// </code>
    /// 그래서 <b>가짜가 무거운지 가벼운지도 모르는 채로</b> 시작한다 — 셈이 훨씬 까다롭다.
    /// </remarks>
    public CoinPuzzle(Random rng)
    {
        _rng = rng;
        _weight = new int[rng.Next(MoreCoins) + LeastCoins];
        Deal();
    }

    private readonly Random _rng;

    /// <summary>
    /// 무게를 다시 깐다 — 금화 수는 그대로다(<c>0x00452A80</c> 은 <c>+0x110</c> 을 안 건드린다).
    /// </summary>
    private void Deal()
    {
        Array.Fill(_weight, Sound);
        Heavy = _rng.Next(2) == 0;
        Fake = _rng.Next(_weight.Length);
        _weight[Fake] += Heavy ? 1 : -1;

        _left.Clear();
        _right.Clear();
        _log.Clear();
    }

    /// <summary>접시에 더 놓을 자리가 있는지.</summary>
    public bool RoomOn(bool left) => (left ? _left : _right).Count < PanRoom;

    /// <summary>그 금화가 어느 접시에 있는지 — 0 이면 아직 상 위다.</summary>
    public int PanOf(int coin) => _left.Contains(coin) ? 1 : _right.Contains(coin) ? -1 : 0;

    /// <summary>금화를 접시에 놓는다. 이미 어딘가에 있거나 자리가 없으면 false.</summary>
    public bool Put(int coin, bool left)
    {
        if (Won != null || PanOf(coin) != 0 || !RoomOn(left)) return false;
        (left ? _left : _right).Add(coin);
        return true;
    }

    /// <summary>「금화를 내린다(CLEAR)」 — 두 접시를 다 비운다.</summary>
    public void Clear()
    {
        _left.Clear();
        _right.Clear();
    }

    /// <summary>더 달 수 있는지.</summary>
    public bool CanWeigh => Won == null && _log.Count < Weighings;

    /// <summary>
    /// 「무게를 단다(WEIGH)」.
    /// </summary>
    /// <remarks>
    /// 접시가 비었으면 <c>0x0053AF18</c> "접시 위에는 아무 것도 없습니다", 양쪽 수가
    /// 다르면 <c>0x0053AFD0</c> "양쪽 접시에 같은 수량의 금화가 놓여지지 않았습니다",
    /// 세 번을 다 썼으면 <c>0x0053AF48</c> "더 이상 천칭으로 금화의 무게를 달 수는
    /// 없습니다" 다.
    /// </remarks>
    /// <returns>기운 쪽. 못 달았으면 null.</returns>
    public Tilt? Weigh()
    {
        if (!CanWeigh) return null;
        if (_left.Count == 0 || _right.Count == 0) return null;
        if (_left.Count != _right.Count) return null;

        int left = _left.Sum(c => _weight[c]);
        int right = _right.Sum(c => _weight[c]);
        var tilt = left > right ? Tilt.Left : left < right ? Tilt.Right : Tilt.Level;

        _log.Add(new Record([.. _left], [.. _right], tilt));
        Clear();
        return tilt;
    }

    /// <summary>
    /// 「가짜 금화 선택(DECIDE)」 — <c>0x0053AC38</c> 로 한 번 묻고 나서 이걸 부른다.
    /// </summary>
    public bool Decide(int coin)
    {
        if (coin == Fake) { Won = true; return true; }

        // <b>첫 실패는 끝이 아니다</b>(0x00450CA9) — 판을 새로 깔고 한 번 더 준다.
        // 두 번째로 틀려야 진짜 끝이고, 삯은 <b>첫 판에 맞혔을 때만</b> 나온다(0x00450C4C).
        if (!Missed) { Missed = true; Deal(); return false; }

        Won = false;
        return false;
    }

    /// <summary>
    /// 한 번 잘못 골랐는가(<c>+0x150</c>) — 그러면 삯이 없고 다음에 틀리면 끝이다.
    /// </summary>
    public bool Missed { get; private set; }

    /// <summary>포기.</summary>
    public void GiveUp() => Won ??= false;
}
