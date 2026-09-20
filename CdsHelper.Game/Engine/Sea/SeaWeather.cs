namespace CdsHelper.Game.Engine.Sea;

/// <summary>
/// 바다의 <b>비·눈</b> — 날마다 굴려 오고, 안 걸리는 날에 그친다.
/// </summary>
/// <remarks>
/// 게임의 날 넘김 <c>0x0044AFD0</c> 첫머리다. 도시 밖이고 뭍이 아닐 때만 돈다.
/// <code>
///   N = 표 0x0053C220[기후대 + 13 x 반기]          기후대 = 바람 표 낱말 비트 8~11(0x00424FA0)
///       1~6월 줄   4,20, 0, 0,10, 4,10,10,20,10,20, 4,20
///       7~12월 줄  4, 4,20, 0,20,20,10,10,10, 0,10, 4,20
///   rand(N) == 0 (N 이 0·1 이면 늘 걸린다)
///     그친 뒤면 위도 0x0ADA ≤ y &lt; 0x4346 (대략 남북 65도 안)이면 비, 밖이면 눈으로 온다
///     걸리면 셈 = 1
///   안 걸리면 셈이 0 이 아닐 때 하나 줄이고, 0 이 되면 비·눈이 그친다(흐려지며 사라진다)
/// </code>
/// 반기를 가르는 위도 비교가 <c>625</c>(원래 적도 10000 이었을 것)라 남반구에서도 뒤집히지 않는다 —
/// 원본 버릇 그대로 둔다. 비·눈은 <b>놀이에 아무 영향이 없다</b> — 그림과 빗소리(WAVES 35)뿐이다.
/// 기후대는 원본이 이레마다 새로 뜬 바람 칸의 것을 쓰는데 여기서는 지금 칸의 것을 쓴다.
/// </remarks>
public sealed class SeaWeather
{
    public enum Kind { None, Rain, Snow }

    /// <summary>지금 오는 것.</summary>
    public Kind Now { get; private set; }

    private int _count;

    private static readonly int[] FirstHalf = [4, 20, 0, 0, 10, 4, 10, 10, 20, 10, 20, 4, 20];
    private static readonly int[] SecondHalf = [4, 4, 20, 0, 20, 20, 10, 10, 10, 0, 10, 4, 20];

    /// <summary>비가 오는 위도 폭(원본 위도 0~20000).</summary>
    private const int RainFrom = 0x0ADA, RainTo = 0x4346;

    /// <summary>
    /// 하루를 굴린다. 새로 오기 시작했으면 그것을, 그쳤으면 <see cref="Kind.None"/> 을, 달라진 것이 없으면 null.
    /// </summary>
    public Kind? Roll(int zone, int month, int latRaw, Random random)
    {
        int[] row = month is >= 1 and <= 6 ? FirstHalf : SecondHalf;
        int n = zone >= 0 && zone < row.Length ? row[zone] : 20;
        bool hit = n < 2 || random.Next(n) == 0;

        if (hit)
        {
            _count = 1;
            if (Now != Kind.None) return null;
            Now = latRaw >= RainFrom && latRaw < RainTo ? Kind.Rain : Kind.Snow;
            return Now;
        }
        if (_count == 0 || --_count > 0) return null;
        if (Now == Kind.None) return null;
        Now = Kind.None;
        return Kind.None;
    }

    /// <summary>그치게 한다 — 입항·해전·육상전·일기토(<c>0x0048EACA</c> · <c>0x00443822</c> …).</summary>
    public void Stop()
    {
        Now = Kind.None;
        _count = 0;
    }
}
