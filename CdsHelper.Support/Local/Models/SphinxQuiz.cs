namespace CdsHelper.Support.Local.Models;

/// <summary>
/// 미니 게임 「스핑크스 퀴즈」 — 수수께끼 하나와 셈 문제 셋.
/// </summary>
/// <remarks>
/// 게임의 <c>0x0047BFE0</c> 이다. 글은 <c>0x0056EEB8</c> 벌에 그대로 있다.
///
/// 먼저 그 유명한 수수께끼를 낸다.
/// <code>
///   0x0056EEC8  〈스핑크스〉 아침에는 4개의 다리, 낮에는 2개의 다리.
///               밤에는 3개의 다리로 걷는 괴물은?
///   0x0056EF20  스핑크스 · 자신 · 기타 · 없다 · 모른다 · 무시한다 · 도망간다
///   0x0047C059  cmp $1,%eax — <b>1번(자신)이라야 넘어간다</b>
/// </code>
/// 그러고 나서 셈 문제 셋인데, 셋 다 <b>답이 같다</b> — 다리 넷 달린 괴물의 수다.
/// </remarks>
public sealed class SphinxQuiz
{
    /// <summary>수수께끼의 보기(<c>0x0056EF20</c> 벌).</summary>
    public static readonly string[] Riddle =
        ["스핑크스", "자신", "기타", "없다", "모른다", "무시한다", "도망간다"];

    /// <summary>수수께끼의 답 — <b>자신</b>이다(<c>0x0047C059</c>).</summary>
    public const int RiddleAnswer = 1;

    /// <summary>셈 문제 수.</summary>
    public const int Questions = 3;

    /// <summary>고를 수 있는 마리 수(<c>0x0056F338</c> 부터 열).</summary>
    public const int Choices = 10;

    /// <summary>「문제를 본다」 줄(<c>0x0056F388</c>). 열한째다.</summary>
    public const int LookAgain = 10;

    /// <summary>
    /// 문제 하나 — 굴린 수와 그 수로 지은 글.
    /// </summary>
    /// <param name="Four">다리 넷 달린 괴물 수. <b>이게 답이다.</b></param>
    /// <param name="Two">다리 둘 달린 괴물 수.</param>
    /// <param name="Grown">그 가운데 다리가 셋이 된 수.</param>
    public sealed record Riddled(int Four, int Two, int Grown)
    {
        /// <summary>처음 다리를 다 더한 수.</summary>
        public int Legs => Four * 4 + Two * 2;

        /// <summary>괴물 마리 수.</summary>
        public int Beasts => Four + Two;

        /// <summary>둘째 문제의 나중 다리 수 — 넷은 둘이 되고 <c>Grown</c> 마리가 셋이 된다.</summary>
        public int Aged => (Four + Two) * 2 + Grown;

        /// <summary>
        /// 셋째 문제의 나중 다리 수 — 거기에 <b>넷 달린 것의 두 곱</b>이 새로 태어난다.
        /// </summary>
        /// <remarks>
        /// <c>0x0047C103</c> 이 <c>(넷*5 + 둘)*2 + 자란수</c> 로 낸다. 풀어 보면
        /// <c>둘*2 + 자란수*3 + (둘-자란수)*2 + 넷*2*4</c> 와 같다 — 곧 새로 태어난
        /// 것들이 <c>넷 * 2</c> 마리다.
        /// </remarks>
        public int Born => (Four * 5 + Two) * 2 + Grown;
    }

    private readonly Random _rng;
    private Riddled _now;

    /// <summary>몇째 문제인지(0~2).</summary>
    public int Step { get; private set; }

    /// <summary>지금 문제.</summary>
    public Riddled Now => _now;

    public SphinxQuiz(Random rng)
    {
        _rng = rng;
        _now = Roll();
    }

    /// <summary>
    /// 수를 굴린다(<c>0x0047C093</c> 벌).
    /// </summary>
    /// <remarks>
    /// <code>
    /// 47c093  esi = rand(8) + 2      ; 다리 넷 달린 괴물
    /// 47c0a0  edi = rand(8) + 2      ; 다리 둘 달린 괴물
    /// 47c0ad  ebx = rand(edi) + 1    ; 그 가운데 다리가 셋이 될 수
    /// </code>
    /// 넷 달린 것이 2~9 라 보기 열(1~10마리)이 답을 늘 덮는다.
    /// </remarks>
    private Riddled Roll()
    {
        int four = _rng.Next(8) + 2;
        int two = _rng.Next(8) + 2;
        return new Riddled(four, two, _rng.Next(two) + 1);
    }

    /// <summary>
    /// 고른 줄을 매긴다.
    /// </summary>
    /// <remarks>
    /// <code>
    /// 47c19c  물렀으면(-1) 수를 다시 굴려 다음 문제로 — 벌은 없다
    /// 47c1a1  10 이면 「문제를 본다」 — 수를 그대로 두고 같은 문제를 다시 낸다
    /// 47c1b0  esi - 고른줄 == 1 이라야 맞다 — 곧 <b>고른 마리 수 == 넷 달린 괴물 수</b>
    /// </code>
    /// </remarks>
    /// <param name="pick">고른 줄. 0~9 가 1~10마리, <see cref="LookAgain"/> 이 다시 보기,
    /// -1 이 무름.</param>
    /// <returns>아직 놀 수 있으면 null, 끝났으면 이겼는지.</returns>
    public bool? Answer(int pick)
    {
        if (pick == LookAgain) return null;          // 같은 문제를 다시 낸다

        if (pick >= 0 && _now.Four - pick != 1) return false;

        // 물러도 다음 문제로 넘어간다. 수는 다시 굴린다.
        Step++;
        if (Step >= Questions) return true;

        _now = Roll();
        return null;
    }
}
