namespace CdsHelper.Game.Engine.Town;

/// <summary>
/// 후원자를 설득할 때의 셈 — 이야기를 받아 줄지, 얼마를 낼지.
/// </summary>
/// <remarks>
/// 게임 자리는 이렇다.
/// <code>
///   0x004AEF50  설득 본체 — 명성 관문, 하트 애니메이션(3번), 기분 관문, 자금 셈
///   0x004AE8E0  들고 있는 힌트를 늘어놓고 하나 고르게 한다(안 되면 또 고른다)
///   0x004AE5F0  <b>받아 줄지 가리는 곳</b>
///   0x004ADAE0  후원자가 그 갈래를 좋아하는지(표 +0x38 의 여덟 비트)
/// </code>
///
/// 가리는 차례가 이렇다.
/// <list type="number">
///   <item><b>이야기가 감당할 만한가</b> — 힌트 등급을 <c>명성/2000</c> 과 견준다.
///         너무 크면 물리거나(그 자리에서 끝) 한 번 더 묻는다.</item>
///   <item><b>좋아하는 갈래인가</b> — 맞으면 두말없이 원조하고 자금도 많다.</item>
///   <item>아니면 <b>안목·웅변·매력</b>으로 굴린다. 되면 마지못해 원조한다.</item>
///   <item>그것도 안 되면 다시 굴려 <b>다른 이야기를 물을지</b> 아주 물릴지 가른다.
///         아주 물리면 <b>기분이 상해</b> 그 뒤로 한동안 안 만나 준다.</item>
/// </list>
/// </remarks>
public static class Persuasion
{
    /// <summary>가린 끝. 값은 게임이 내는 것 그대로다(<c>0x004AE8C5</c> 가 이 값을 낸다).</summary>
    public enum Verdict
    {
        /// <summary>좋아하는 갈래다 — 두말없이 원조한다. 자금이 많다.</summary>
        Interested = 0,

        /// <summary>내키지 않지만 들어준다 — 자금이 적다.</summary>
        Reluctant = 1,

        /// <summary>다른 이야기를 물어본다 — 다시 고를 수 있다.</summary>
        AskAnother = 2,

        /// <summary>아주 물린다. 기분이 상한다.</summary>
        Refused = 3,

        /// <summary>이야기가 너무 커서 아예 못 받는다.</summary>
        TooBig = 4,
    }

    /// <summary>명성을 재는 눈금(<c>0x004AE642</c> 의 <c>0x7D0</c>).</summary>
    private const int FameStep = 2000;

    /// <summary>안 좋아하는 갈래일 때 굴리는 주사위와 그 다음 주사위.</summary>
    private const int FirstDice = 200, SecondDice = 150;

    /// <summary>웅변에 얹는 무게. 첫 굴림은 서른셋, 두 번째는 스물다섯이다.</summary>
    private const int FirstRhetoric = 33, SecondRhetoric = 25, SecondBase = 25;

    /// <summary>갈래 수 — 후원자 취향 비트가 여덟이다.</summary>
    public const int Categories = 8;

    /// <summary>
    /// 이야기가 <b>감당할 만한가</b>. 0 이면 괜찮고, 1 이면 무겁지만 우겨 볼 만하고,
    /// 2 면 아예 못 받는다.
    /// </summary>
    /// <remarks><c>0x004AE64B</c> — 등급을 <c>명성/2000 + 1</c> 과 <c>+ 2</c> 에 견준다.</remarks>
    public static int Weight(int grade, int fame)
    {
        int mark = fame / FameStep;
        if (grade <= mark + 1) return 0;
        return grade <= mark + 2 ? 1 : 2;
    }

    /// <summary>
    /// 후원자 <b>안목</b>이 이야기를 가늠할 만한가(<c>0x004AF0DE</c>).
    /// </summary>
    /// <remarks>
    /// <code>
    ///   4af0de  안목(후원자 표 +0x20) / 20                     → ebx
    ///   4af0f2  등급 - 5 가 1 보다 작으면 1, 아니면 2 를 더한다  ← 등급 5 만 1 이다
    ///   4af10b  그 값이 <b>등급에 못 미치면</b> 물린다
    /// </code>
    /// 등급이 1·2 면 어떤 안목으로도 걸리지 않고, 3 은 안목 20, 4 는 40, 5 는 80 이
    /// 있어야 넘어간다. <b>설득을 다 이기고 자금까지 셈한 뒤에</b> 따지는 관문이라
    /// 여기서 물려도 기분은 상하지 않는다(<c>0x004AF3FC</c> 가 그냥 0 을 낸다).
    /// </remarks>
    public static bool Grasps(int eye, int grade) =>
        eye / EyeStep + (grade == KeenGrade ? 1 : 2) >= grade;

    /// <summary>안목을 재는 눈금과, 하나만 얹어 주는 등급(<c>0x004AF0E3</c>·<c>0x004AF0F7</c>).</summary>
    private const int EyeStep = 20, KeenGrade = 5;

    /// <summary>그 갈래를 좋아하는가(<c>0x004ADAE0</c>, 표 <c>+0x38</c> 의 비트).</summary>
    public static bool Likes(int tastes, int category) =>
        category >= 0 && category < Categories && (tastes & (1 << category)) != 0;

    /// <summary>
    /// 안 좋아하는 갈래를 <b>말솜씨로 넘기는</b> 굴림(<c>0x004AE789</c>).
    /// </summary>
    /// <remarks><c>안목 + (웅변*33 + 매력+1) / 2 &gt;= rand(200)</c> 이면 넘어간다.</remarks>
    public static bool Talks(int eye, int rhetoric, int charm, GameRandom dice) =>
        eye + (rhetoric * FirstRhetoric + charm + 1) / 2 >= dice.Next(FirstDice);

    /// <summary>
    /// 넘기지 못했을 때 <b>다른 이야기라도 물어볼지</b> 가리는 굴림(<c>0x004AE7E7</c>).
    /// </summary>
    /// <remarks>
    /// <c>안목 + ((웅변*5+5)*5 + 매력+1) / 2 &gt; rand(150)</c> 이면 다른 이야기를 묻고,
    /// 아니면 아주 물리며 기분이 상한다.
    /// </remarks>
    public static bool Softens(int eye, int rhetoric, int charm, GameRandom dice) =>
        eye + (rhetoric * SecondRhetoric + SecondBase + charm + 1) / 2 > dice.Next(SecondDice);

    /// <summary>
    /// 낼 자금(<c>0x004AF07E</c>).
    /// </summary>
    /// <remarks>
    /// <code>
    ///   자금 = max(힌트 자금, 5000)
    ///        * (친밀도/2 + 50) / 100
    ///        * (두말없이 받으면 125, 마지못해면 75) / 100
    ///   적어도 20 닢
    /// </code>
    /// <b>힌트 자금이 5000 밑이면 5000 으로 친다</b> — 작은 이야기라도 밑돈은 준다.
    /// </remarks>
    public static int Funds(int hintFunds, int closeness, Verdict verdict)
    {
        int money = Math.Max(hintFunds, FundsFloor);
        int paid = money * (closeness / 2 + ClosenessBase) / 100;
        paid = paid * (verdict == Verdict.Interested ? EagerPercent : ReluctantPercent) / 100;
        return Math.Max(paid, MinFunds);
    }

    private const int FundsFloor = 5000, ClosenessBase = 50;
    private const int EagerPercent = 125, ReluctantPercent = 75, MinFunds = 20;
}
