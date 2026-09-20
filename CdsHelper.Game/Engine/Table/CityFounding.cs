namespace CdsHelper.Game.Local.Helpers;

/// <summary>
/// 도시가 지도에 <b>언제 나타나는가</b> — 신대륙 식민 도시 스물셋.
/// </summary>
/// <remarks>
/// 놀이를 켤 때 지도에 없는 도시가 스물셋이다. 정적 도시 표
/// (<c>0x004D14B0</c>, 한 줄 136바이트)의 <c>+0x62</c> 낱말 <b>비트 2</b>가 「아직 안
/// 세워졌다」고, 판을 열 때 그 값이 도시 레코드(<c>0x005863A8</c>, 92바이트씩)의
/// <c>+0x04</c> 로 옮겨진다(<c>0x004299B7</c>).
///
/// 그 비트를 걷어 주는 것은 도시 표가 아니라 <b>역사 사건 스크립트</b>
/// (<c>HIST_EV.CDS</c>)다.
/// <code>
///   26 08 &lt;도시&gt; 00   그 도시를 세운다
///   27 08 &lt;도시&gt; 00   그 도시가 있어야   (조건)
///   28 08 &lt;도시&gt; 00   그 도시가 없어야   (조건)
///
///   1C 17 &lt;월&gt; 16 &lt;해:2&gt;   그 해 그 달에만 본다
///   1B 17 &lt;월&gt; 16 &lt;해:2&gt;   그 해부터 — 조건이 맞을 때까지 기다린다
/// </code>
/// 자세한 것은 볼트 <c>75.분석-도시 등장 시기</c>.
///
/// <b>사람이 손댈 데가 없다.</b> 조건이 죄다 도시끼리라 날짜만 알면 그대로 되짚을 수
/// 있으므로, 세이브에 적어 두지 않고 <see cref="FoundedBy"/> 가 그때그때 굴린다.
/// </remarks>
public static class CityFounding
{
    /// <summary>놀이를 켤 때 지도에 없는 도시 스물셋.</summary>
    /// <remarks>
    /// EXE 를 다시 읽지 않고 번호로 적어 둔다 — 정적 값이라 판이 같으면 안 바뀐다.
    /// <c>0x004D14B0 + 도시*136 + 0x62</c> 의 비트 2 로 뽑은 것이다.
    /// </remarks>
    public static readonly IReadOnlySet<int> Hidden = new HashSet<int>
    {
        97, 99, 101, 173, 194, 195, 196, 197, 198, 199, 201, 205,
        208, 209, 210, 211, 212, 213, 214, 215, 216, 217, 222,
    };

    /// <summary>신도시 이벤트 하나.</summary>
    /// <param name="Year">터지는 해 · <paramref name="Month"/> 달.</param>
    /// <param name="Wait">
    /// <c>1B</c> 꼴인지 — 참이면 <b>그 해부터</b> 조건이 맞을 때까지 기다리고,
    /// 거짓이면(<c>1C</c>) <b>그 해 그 달에만</b> 본다.
    /// </param>
    /// <param name="Needs">먼저 서 있어야 하는 도시. −1 이면 조건이 없다.</param>
    /// <param name="Cities">세우는 도시들.</param>
    public readonly record struct Rule(int Year, int Month, bool Wait, int Needs, int[] Cities);

    /// <summary>
    /// <c>HIST_EV.CDS</c> 에서 뽑은 스무 벌. 파트 번호 차례다.
    /// </summary>
    /// <remarks>
    /// 조건에 늘 붙는 「그 도시가 <b>없어야</b>」는 안 적었다 — 두 번 세우지 않으려는
    /// 것이라 이미 선 도시를 건너뛰는 것으로 갈음한다.
    /// </remarks>
    private static readonly Rule[] Rules =
    [
        new(1482, 2, false, -1, [97]),          // 파트  2  산호르헤
        new(1484, 4, false, -1, [99]),          // 파트  3  산토메
        new(1489, 1, true, -1, [101]),          // 파트  8  케이프
        new(1494, 1, true, -1, [194]),          // 파트 15  산토도밍고
        new(1499, 1, true, -1, [197]),          // 파트 19  자메이카
        new(1500, 1, true, -1, [195]),          // 파트 21  산티아고
        new(1502, 1, true, -1, [213]),          // 파트 26  쿠마나
        new(1504, 1, true, -1, [198]),          // 파트 27  산후안
        new(1506, 1, true, -1, [196]),          // 파트 29  아바나
        new(1510, 1, true, -1, [215]),          // 파트 34  바이앙
        new(1515, 1, true, 215, [214]),         // 파트 38  페르남부쿠
        new(1517, 1, true, 215, [216]),         // 파트 43  리우데자네이루
        new(1520, 1, true, -1, [199]),          // 파트 47  베라클루즈
        new(1520, 1, true, 215, [217]),         // 파트 48  부에노스아이레스
        new(1522, 7, true, -1, [205]),          // 파트 50  아카풀코
        new(1523, 1, true, 210, [209]),         // 파트 56  레온     — 파나마가 있어야
        new(1525, 1, true, 209, [212, 208]),    // 파트 59  코로·투르히요
        new(1531, 3, false, 215, [210]),        // 파트 70  파나마   — 그 달에만!
        new(1535, 1, true, -1, [173]),          // 파트 75  오문
    ];

    /// <summary>놀이가 시작하는 해. 여기서부터 달을 훑는다.</summary>
    private const int FirstYear = 1480;

    /// <summary>
    /// <b>영영 안 나오는 세 도시.</b> 표에는 숨김 비트가 서 있는데 <c>HIST_EV.CDS</c>
    /// 어디에도 그 번호가 한 번도 안 나온다 — 걷어 줄 이벤트가 없다.
    /// </summary>
    /// <remarks>
    /// 멕시코(201) · 산타마르타(211) · 리마(222) 다. <c>STORY0~9.CDS</c> 에도 신도시
    /// 오피코드가 없고, <c>HISTCHR.CDS</c> 의 <c>2608</c> 은 뜻이 달라 역사 항해자가
    /// 들르는 <b>경유 항구</b>다. 코르테스 편에 멕시코가, 피사로 편에 리마가 나오는 것도
    /// 항로 지점일 뿐 도시를 세우는 것이 아니다.
    ///
    /// 다만 <b>멕시코는 발견 대본으로 선다</b> — 발견 이벤트 263(아스텍)이 끝에
    /// <c>22 08 C8 00</c>(테노치티틀란을 없앰) · <c>26 08 C9 00</c>(멕시코를 세움)을 둔다.
    /// 날짜로는 안 나오므로 <c>Player.ScriptedCities</c> 가 따로 든다.
    /// </remarks>
    public static readonly IReadOnlySet<int> NeverFounded = new HashSet<int> { 201, 211, 222 };

    /// <summary>
    /// 그 날짜까지 <b>세워진</b> 도시들. 처음부터 있던 도시는 안 들어간다.
    /// </summary>
    /// <remarks>
    /// 1480년 1월부터 달을 하나씩 훑는다. <c>1C</c> 꼴은 그 달에만 보므로 달을 건너뛰면
    /// 안 되고, <c>1B</c> 꼴은 그 해부터 조건이 맞을 때까지 기다린다 — 파나마가 늦어
    /// 레온·코로·투르히요가 1531년에 한꺼번에 나오는 것이 이 셈에서 그대로 나온다.
    /// </remarks>
    public static HashSet<int> FoundedBy(DateTime when)
    {
        var up = new HashSet<int>();
        var done = new bool[Rules.Length];

        int last = when.Year * 12 + when.Month;
        for (int at = FirstYear * 12 + 1; at <= last; at++)
        {
            int year = at / 12, month = at % 12;
            if (month == 0) { year--; month = 12; }

            for (int i = 0; i < Rules.Length; i++)
            {
                if (done[i]) continue;
                var rule = Rules[i];

                bool now = rule.Wait
                    ? year > rule.Year || (year == rule.Year && month >= rule.Month)
                    : year == rule.Year && month == rule.Month;
                if (!now) continue;

                // 그 달에만 보는 꼴은 조건이 안 맞으면 그대로 흘려보낸다.
                if (rule.Needs >= 0 && !up.Contains(rule.Needs))
                {
                    if (!rule.Wait) done[i] = true;
                    continue;
                }

                foreach (int city in rule.Cities) up.Add(city);
                done[i] = true;
            }
        }
        return up;
    }

    /// <summary>그 도시가 그 날짜에 지도에 있는지.</summary>
    /// <param name="scripted">발견 대본이 세우거나 없앤 도시(<c>Player.ScriptedCities</c>) — 날짜보다 먼저다.</param>
    public static bool Standing(int city, DateTime when, IReadOnlyDictionary<int, bool>? scripted = null) =>
        scripted != null && scripted.TryGetValue(city, out bool set) ? set
        : !Hidden.Contains(city) || FoundedBy(when).Contains(city);

    /// <summary>그 도시가 서는 해와 달. 영영 안 서면 null.</summary>
    public static (int Year, int Month)? WhenOf(int city)
    {
        foreach (var rule in Rules)
            if (rule.Cities.Contains(city)) return (rule.Year, rule.Month);
        return null;
    }
}
