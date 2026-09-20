namespace CdsHelper.Support.Local.Models;

/// <summary>
/// 미니 게임 「성배 퍼즐」 — 대·중·소 바가지로 성배 열을 성수로 딱 맞게 채우는 놀이.
/// </summary>
/// <remarks>
/// 게임의 <c>0x004684D0</c> 이 바깥 껍데기, <c>0x00467D50</c> 이 본체다. 화면 설명은
/// <c>0x00559068</c> 에 그대로 있다.
/// <code>
///   성공조건 [바로 앞에 있는 10개의 성배를 성수로 채워라.]
///
///   대·중·소의 물바가지를 잘 써서 큰 항아리 속의 성수로 모든 성배를 채워라.
///   탐험자가 움직일 수 있는 것은 물바가지 뿐이다. 큰 항아리는 물을 풀 수도 있고
///   다시 놓을 수도 있다. 바가지와 바가지의 이동으로는 물이 넘칠 일은 없다.
///   성배에서 물이 넘치게 되면 당신은 죽게 된다.
/// </code>
/// 그릇은 <b>열다섯 자리</b>다(<c>0x004678F0</c> 이 <c>0..14</c> 를 훑는다). 자리마다
/// 종류가 있고, 종류는 그릇 클래스의 첫 가상 자리가 낸다.
/// <code>
///   자리  종류  용량                     만드는 곳
///   0     2     0                        0x00467FFB  — 아무것도 못 주고 못 받는다(그림)
///   1     1     9999                     0x0046809E  — 큰 항아리
///   2·3·4 3     문제표[문제][0·1·2]      0x0046812E  — 바가지 소·중·대
///   5~14  4     1 2 3 … 10               0x004681CB  — 성배 열
/// </code>
/// <b>성배는 한 홉부터 열 홉까지 하나씩</b>이고 합이 55 다. 바가지만 문제마다 바뀐다.
/// </remarks>
public sealed class GrailPuzzle
{
    /// <summary>그릇 자리 수(<c>0x004678F0</c> 의 <c>cmp $0xf</c>).</summary>
    public const int Slots = 15;

    /// <summary>자리 번호.</summary>
    public const int Idle = 0, Jar = 1, FirstDipper = 2, FirstGrail = 5;

    /// <summary>바가지 셋과 성배 열.</summary>
    public const int Dippers = 3, Grails = 10;

    /// <summary>큰 항아리 용량(<c>0x0046806B</c> 의 <c>0x270F</c>). 사실상 무한이다.</summary>
    public const int JarSize = 9999;

    /// <summary>종류 — 1 항아리, 2 그림, 3 바가지, 4 성배.</summary>
    public const int KindJar = 1, KindIdle = 2, KindDipper = 3, KindGrail = 4;

    /// <summary>
    /// 문제 스물넷. 앞 셋이 바가지 소·중·대의 용량이고 넷째는 <b>안 읽는다</b>.
    /// </summary>
    /// <remarks>
    /// <c>0x005AA6F8</c> 에 열여섯 바이트씩 스물넷이고, 채우는 곳은 <c>0x00466EA0</c>
    /// 벌이다(값이 코드 안에 박혀 있다). 바가지 loop 은 <c>0x004680FC</c> 에서
    /// <c>표[문제 * 4 + i]</c> 로 앞 셋만 꺼내 쓴다 — 넷째는 아무도 안 본다. 26~38
    /// 인 것을 보면 <b>모범 답의 수</b>를 적어 둔 자리로 보인다(잘 풀면 35수, 그럭저럭
    /// 이면 50수가 가름선이니 자릿수가 맞는다).
    /// </remarks>
    public static readonly int[][] Problems =
    [
        [2, 4, 7, 28], [2, 4, 9, 28], [2, 5, 7, 32], [2, 5, 8, 29],
        [2, 5, 9, 28], [2, 5, 10, 28], [2, 6, 9, 27], [2, 7, 9, 34],
        [2, 7, 10, 27], [3, 5, 7, 28], [3, 5, 8, 34], [3, 5, 9, 26],
        [3, 5, 10, 29], [3, 6, 8, 32], [3, 6, 10, 30], [3, 7, 9, 29],
        [3, 7, 10, 38], [3, 8, 10, 29], [4, 6, 9, 28], [4, 7, 9, 27],
        [4, 7, 10, 30], [5, 7, 9, 30], [5, 7, 10, 31], [5, 8, 10, 33],
    ];

    /// <summary>바가지 이름. 표의 차례가 곧 작은 것부터다.</summary>
    public static readonly string[] DipperNames = ["소", "중", "대"];

    /// <summary>
    /// 결과 — <c>0x00424C10</c> 이 매긴다. 이 번호가 그대로 <c>0x004684D0</c> 의
    /// 갈림길이 된다.
    /// </summary>
    public enum Result
    {
        /// <summary>포기했다 — "근성이 없는 녀석이로군···"</summary>
        GaveUp = 0,

        /// <summary>성배에서 넘쳤다 — "대실패".</summary>
        Spilled = 1,

        /// <summary>채우긴 했는데 쉰 수를 넘겼다 — "다시 한번 찬스를 주겠다".</summary>
        Slow = 2,

        /// <summary>서른여섯에서 쉰 수 — "성공".</summary>
        Good = 3,

        /// <summary>서른다섯 수 안 — "멋지게 성공", 금화 5000닢.</summary>
        Great = 4,
    }

    /// <summary>결과가 갈리는 수(<c>cmp $0x23</c> · <c>cmp $0x32</c>).</summary>
    public const int GreatMoves = 35, GoodMoves = 50;

    /// <summary>「멋지게 성공」의 상금(<c>0x00468623</c> 의 <c>0x1388</c>).</summary>
    public const int Prize = 5000;

    private readonly int[] _size = new int[Slots];
    private readonly int[] _water = new int[Slots];
    private readonly int[] _kind = new int[Slots];

    // 되돌리기는 <b>한 수뿐</b>이다(0x00424C60 이 열여섯 바이트에 둘만 적는다).
    // 항아리에서 뜨거나 버릴 때는 한쪽만 적힌다 — 그때 _backTo 는 -1 이다.
    private int _backFrom = -1, _backTo = -1, _backFromWater, _backToWater;

    /// <summary>몇 번째 문제인지(0~23).</summary>
    public int Problem { get; }

    /// <summary>지금까지 부은 수.</summary>
    public int Moves { get; private set; }

    /// <summary>끝났으면 그 결과. 아직이면 null.</summary>
    public Result? Over { get; private set; }

    /// <param name="problem">문제 번호. 게임은 <c>rand(24)</c> 로 고른다.</param>
    public GrailPuzzle(int problem)
    {
        Problem = ((problem % Problems.Length) + Problems.Length) % Problems.Length;

        _kind[Idle] = KindIdle;
        _kind[Jar] = KindJar;
        _size[Jar] = JarSize;
        _water[Jar] = JarSize;

        for (int i = 0; i < Dippers; i++)
        {
            _kind[FirstDipper + i] = KindDipper;
            _size[FirstDipper + i] = Problems[Problem][i];
        }

        for (int i = 0; i < Grails; i++)
        {
            _kind[FirstGrail + i] = KindGrail;
            _size[FirstGrail + i] = i + 1;      // 한 홉부터 열 홉까지
        }
    }

    /// <summary>그 자리의 종류.</summary>
    public int KindAt(int slot) => _kind[slot];

    /// <summary>그 자리의 용량.</summary>
    public int SizeAt(int slot) => _size[slot];

    /// <summary>그 자리에 든 물.</summary>
    public int WaterAt(int slot) => _water[slot];

    /// <summary>되돌릴 수가 있는지.</summary>
    public bool CanUndo => Moves > 0 && _backFrom >= 0;

    /// <summary>집을 수 있는 그릇인지 — <b>바가지뿐</b>이다.</summary>
    /// <remarks>
    /// 설명 글이 못 박아 둔다 — "탐험자가 움직일 수 있는 것은 물바가지 뿐이다".
    /// </remarks>
    public bool CanGrab(int slot) => Over == null && _kind[slot] == KindDipper;

    /// <summary>
    /// 집은 그릇을 다른 그릇 위에 놓는다.
    /// </summary>
    /// <remarks>
    /// <b>무슨 일이 일어날지는 놓는 자리의 종류가 정한다</b>(<c>0x00467BD0</c>). 집은
    /// 그릇의 종류는 안 본다.
    /// <code>
    /// 467bf6  eax = 놓는 자리.Kind()
    /// 467c07  jmp *0x467cf0(,%eax-1,4)
    /// 467c0e  종류 1 큰 항아리 — 집은 것이 안 찼으면 0x00424CC0 으로 가득 채운다
    /// 467c34  종류 2 버리는 곳 — 집은 것에 물이 있으면 0x00424C90 으로 비운다
    /// 467c55  종류 3 바가지    — 0x00424CF0(집은 것, 놓는 자리)
    /// 467c85  종류 4 성배      — 0x00424CF0(집은 것, 놓는 자리)
    /// </code>
    /// 그래서 <b>바가지를 항아리에 끌어다 놓으면 물이 떠진다</b> — 붓는 것이 아니다.
    /// </remarks>
    public bool Drop(int grabbed, int target)
    {
        if (Over != null || grabbed == target) return false;

        return _kind[target] switch
        {
            KindJar => Fill(grabbed),
            KindIdle => Spill(grabbed),
            _ => Pour(grabbed, target),
        };
    }

    /// <summary>가득 채운다(<c>0x00424CC0</c>). 이미 찼으면 아무 일도 안 한다.</summary>
    private bool Fill(int slot)
    {
        if (_size[slot] <= _water[slot]) return false;

        Remember(slot, -1);
        _water[slot] = _size[slot];
        Moves++;
        return true;
    }

    /// <summary>비운다(<c>0x00424C90</c>). 빈 것이면 아무 일도 안 한다.</summary>
    private bool Spill(int slot)
    {
        if (_water[slot] == 0) return false;

        Remember(slot, -1);
        _water[slot] = 0;
        Moves++;
        return true;
    }

    /// <summary>되돌리기 한 수를 적어 둔다(<c>0x00424C60</c>).</summary>
    private void Remember(int from, int to)
    {
        _backFrom = from;
        _backTo = to;
        _backFromWater = _water[from];
        _backToWater = to >= 0 ? _water[to] : 0;
    }

    /// <summary>
    /// <paramref name="from"/> 의 물을 <paramref name="to"/> 에 붓는다.
    /// </summary>
    /// <remarks>
    /// <c>0x00424CF0</c> 그대로다.
    /// <code>
    /// 424d0d  ebp = 준 쪽의 물
    /// 424d14  물이 없으면 아무 일도 안 일어난다
    /// 424d1e  받는 쪽이 바가지(3)인데 이미 찼으면 아무 일도 안 일어난다
    /// 424d44  0x00424C60 — 되돌리기 한 수를 적어 둔다
    /// 424d4e  꺼낸다 = 준 쪽.Take(전부)          ; 0x004B2000
    /// 424d56  남은 것 = 받는 쪽.Put(꺼낸 것)      ; 0x004B1FD0
    /// 424d63  남은 것이 있는데 받는 쪽이 성배(4)면 <b>넘쳤다</b> — 0 을 낸다
    /// 424d74  아니면 남은 것을 준 쪽에 도로 붓는다
    /// </code>
    /// 항아리는 종류 1 이라 <c>Take</c> 가 달라는 대로 다 주고 <c>Put</c> 이 다 받는다
    /// (<c>0x00468370</c> · <c>0x00468360</c>). 그래서 푸고 되돌리는 것이 공짜다.
    /// </remarks>
    /// <returns>판이 움직였으면 true. 넘쳤으면 <see cref="Over"/> 가 채워진다.</returns>
    public bool Pour(int from, int to)
    {
        if (Over != null) return false;
        if (from == to) return false;

        int have = _water[from];
        if (have == 0) return false;
        if (_kind[to] == KindDipper && _size[to] <= _water[to]) return false;

        Remember(from, to);
        Moves++;

        int took = Take(from, have);
        int left = Put(to, took);

        if (left != 0)
        {
            if (_kind[to] == KindGrail)
            {
                Over = Result.Spilled;
                return true;
            }
            Put(from, left);
        }

        if (Filled()) Over = Grade();
        return true;
    }

    /// <summary>「한 수 되돌림」 — <c>0x00424D90</c>. 딱 한 수만 된다.</summary>
    public bool Undo()
    {
        if (Over != null || !CanUndo) return false;

        _water[_backFrom] = _backFromWater;
        if (_backTo >= 0) _water[_backTo] = _backToWater;
        _backFrom = -1;
        Moves--;                                // 0x00424C00 — 0 밑으로는 안 내려간다
        return true;
    }

    /// <summary>「항복」 — 포기하고 나간다.</summary>
    public void GiveUp() => Over ??= Result.GaveUp;

    /// <summary>성배가 다 찼는지(<c>0x004678F0</c>).</summary>
    public bool Filled()
    {
        for (int i = 0; i < Slots; i++)
            if (_kind[i] == KindGrail && _water[i] != _size[i]) return false;
        return true;
    }

    /// <summary>수로 등수를 매긴다(<c>0x00424C10</c>).</summary>
    private Result Grade() => Moves <= GreatMoves ? Result.Great
                            : Moves <= GoodMoves ? Result.Good
                            : Result.Slow;

    /// <summary><c>0x004B2000</c> — 있는 만큼만 준다.</summary>
    private int Take(int slot, int want)
    {
        if (_kind[slot] == KindJar) return want;    // 0x00468370 — 무한 원천
        int given = Math.Min(want, _water[slot]);
        _water[slot] -= given;
        return given;
    }

    /// <summary><c>0x004B1FD0</c> — 넘치는 만큼을 돌려준다.</summary>
    private int Put(int slot, int amount)
    {
        if (_kind[slot] == KindJar) return 0;       // 0x00468360 — 무한히 받는다
        if (_kind[slot] == KindIdle) return amount;

        int room = _size[slot] - _water[slot];
        int took = Math.Min(amount, Math.Max(0, room));
        _water[slot] += took;
        return amount - took;
    }
}
