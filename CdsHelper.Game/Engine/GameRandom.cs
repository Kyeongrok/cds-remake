namespace CdsHelper.Game.Engine;

/// <summary>
/// 게임이 쓰는 주사위. CDS_95.EXE 의 것을 그대로 옮겨 <b>같은 씨앗이면 같은 눈</b>이 나온다.
/// </summary>
/// <remarks>
/// <code>
///   4B7BDD  rand()      seed = seed * 0x41C64E6D + 0x3039
///                       return (seed &amp; 0x7FFF0000) >> 16      (0~32767)
///   4B7BFB  srand(x)    seed = x                                (0x005801A8)
///   4B7C09  getseed()   return seed
///   4B7C0F  rand(n)     n &lt; 2 이면 0, 아니면 rand() % n
/// </code>
/// 흔한 LCG 지만 <b>내는 자리가 다르다</b> — 곱셈 뒤 <c>16~30</c>번째 비트만 쓴다.
/// 그래서 <see cref="System.Random"/> 으로는 같은 눈이 나오지 않는다.
///
/// 이것을 옮겨 둔 값어치는 <b>모양이 게임과 똑같아진다</b>는 데 있다. 도서관 서가처럼
/// 씨앗을 박고 굴리는 자리(<see cref="Town.Library"/>)는 이 주사위를 써야 게임과 같은
/// 자리에 같은 책이 꽂힌다.
///
/// 게임은 이런 자리에서 <b>씨앗을 꺼내 두었다가 되돌려 놓는다</b>(<c>0x004721A1</c> 과
/// <c>0x00472261</c>). 잠깐 박은 씨앗이 놀이 전체의 주사위 흐름을 흔들지 않게 하는 것이다 —
/// 우리는 이 주사위를 그때그때 새로 지어 쓰므로 흔들 것이 없다.
/// </remarks>
/// <param name="seed">박을 씨앗.</param>
public sealed class GameRandom(int seed)
{
    private uint _state = unchecked((uint)seed);

    /// <summary>0 ~ 32767.</summary>
    public int Next()
    {
        unchecked
        {
            _state = _state * 0x41C64E6D + 0x3039;
        }
        return (int)((_state & 0x7FFF0000) >> 16);
    }

    /// <summary>0 ~ <paramref name="below"/>-1. 2 보다 작으면 늘 0 이다(게임도 그렇다).</summary>
    public int Next(int below) => below < 2 ? 0 : Next() % below;
}
