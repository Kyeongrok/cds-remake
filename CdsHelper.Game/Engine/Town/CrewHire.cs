namespace CdsHelper.Game.Engine.Town;

/// <summary>선원을 모으고 내보내는 규칙.</summary>
public static class CrewHire
{
    /// <summary>아무리 이름이 높아도 이만큼은 준다.</summary>
    public const int LeastPrice = 10;

    /// <summary>이름값을 나누는 수. 명성 400 마다 한 닢씩 싸진다.</summary>
    public const int FameDivisor = 400;

    /// <summary>이름이 아주 없을 때의 밑값.</summary>
    public const int Base = 10000;

    /// <summary>
    /// 선원 한 사람 값. 명성이 높을수록 싸고, 아무리 높아도 열 닢 밑으로는 안 내려간다.
    /// </summary>
    /// <remarks>
    /// 게임의 <c>0x00477370</c> 그대로다 — <c>(10000 - 명성) / 400</c> 을 하고 10 과 견줘
    /// 큰 쪽을 쓴다. 명성(<c>0x005B614C</c>)이 1700 이면 스무 닢, 6000 을 넘으면 열 닢이다.
    /// </remarks>
    public static int PriceFor(int fame) => Math.Max(LeastPrice, (Base - fame) / FameDivisor);
}
