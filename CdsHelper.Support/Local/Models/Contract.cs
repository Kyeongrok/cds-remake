namespace CdsHelper.Support.Local.Models;

/// <summary>
/// 후원자와 맺은 계약 하나. 힌트 하나를 좇기로 하고 자금을 받는다.
/// </summary>
/// <remarks>
/// 게임은 계약을 실행 중 객체 하나(<c>0x0061D1D0</c>)로 들고 있다 — <c>+0x10</c> 이 힌트
/// 번호고(-1 이면 계약 없음), 계약금은 따로 전역 <c>0x005B619C</c> 에 둔다. 계약을 맺는
/// 자리는 <c>0x004ADEE0</c> 이고 그 안에서
/// <code>
///   0x004ADF31  *0x5B619C = 계약금
///   0x004ADF3E  소지금 += 계약금 / 2      ; 선금은 그 자리에서 받는다
/// </code>
/// 를 한다. 계약 정보 화면(<c>0x0047F1D0</c>)은 <b>선금도 미불도 계약금의 절반</b>으로 낸다 —
/// 둘 다 <c>계약금 / 2</c> 라 홀수면 합이 계약금보다 하나 모자란데, 게임이 그렇게 낸다.
/// </remarks>
public sealed class Contract
{
    /// <summary>한 해를 며칠로 세는지. 게임이 남은 기한을 셀 때 쓰는 수다(<c>0x0047F3DE</c>).</summary>
    public const int DaysPerYear = 365;

    /// <summary>한 달을 며칠로 세는지(<c>0x0047F3F8</c>).</summary>
    public const int DaysPerMonth = 30;

    /// <summary>화면에 내는 개월 수의 위쪽 끝. 게임도 11 로 자른다(<c>0x0047F44A</c>).</summary>
    public const int MaxMonths = 11;

    /// <param name="Hint">좇기로 한 힌트 번호.</param>
    /// <param name="Sponsor">후원자 이름.</param>
    /// <param name="City">계약을 맺은 마을.</param>
    /// <param name="Amount">계약금(닢). 절반은 선금으로 받고 절반은 성공한 뒤에 받는다.</param>
    /// <param name="SignedOn">맺은 날.</param>
    /// <param name="Years">계약 기한(년).</param>
    /// <param name="Inspector">딸려 온 감찰관 이름. 옛 세이브를 읽을 때만 빈 문자열이다.</param>
    public Contract(int hint, string sponsor, string city, int amount,
                    DateTime signedOn, int years, string inspector = "")
    {
        Hint = hint;
        Sponsor = sponsor;
        City = city;
        Amount = amount;
        SignedOn = signedOn;
        Years = years;
        Inspector = inspector;
    }

    /// <summary>좇기로 한 힌트 번호.</summary>
    public int Hint { get; }

    /// <summary>후원자 이름.</summary>
    public string Sponsor { get; }

    /// <summary>계약을 맺은 마을.</summary>
    public string City { get; }

    /// <summary>계약금(닢).</summary>
    public int Amount { get; }

    /// <summary>맺은 날.</summary>
    public DateTime SignedOn { get; }

    /// <summary>계약 기한(년).</summary>
    public int Years { get; }

    /// <summary>
    /// 딸려 온 감찰관 이름. 계약을 맺으면 <b>반드시 하나 붙는다</b>.
    /// </summary>
    /// <remarks>
    /// 얼굴은 누구든 같고 이름만 갈린다 — 이름표와 얼굴 번호는 놀이 쪽
    /// <c>CdsHelper.Game.Engine.Town.Inspector</c> 에 있다(게임 <c>0x004AF450</c>).
    /// 옛 세이브에는 없어 빈 문자열일 수 있다.
    /// </remarks>
    public string Inspector { get; }

    /// <summary>
    /// 이 계약으로 스폰서가 배를 대 주었는지.
    /// </summary>
    /// <remarks>
    /// 게임은 전역 <c>0x0061D1E8</c> 한 칸에 <b>세 자리</b>를 담는다.
    /// <code>
    ///   0  아직 안 빌렸다      41069D  이 자리라야 "좋다. 배를 …" 첫 대출 말이 나온다
    ///   1  빌렸고 아직 안 알렸다 41079D  배를 내주며 박는다
    ///   2  항구에서 알렸다     476F40  인사를 낸 바로 뒤에 올린다
    /// </code>
    /// 그래서 항구 인사는 <b>딱 한 번</b> 나오고, 1 이 아닌 자리에서는 다시 안 나온다.
    /// </remarks>
    public bool ShipsLent { get; set; }

    /// <summary>
    /// 감찰관을 아직 매수할 수 있는지(계약 <c>+0x14</c>) — <b>계약마다 한 번뿐</b>이다.
    /// </summary>
    /// <remarks>
    /// 계약을 맺을 때 켜지고(<c>0x00493EF5</c>), 매수 창을 열어 어떤 결말이든 보면 꺼진다
    /// (<c>0x0041C723</c> — 물려도, 돈이 모자라도 꺼진다).
    /// </remarks>
    public bool BribeOpen { get; set; } = true;

    /// <summary>항구에서 대출 배 이야기를 이미 들었는지(게임 <c>0x0061D1E8 == 2</c>).</summary>
    public bool LoanAnnounced { get; set; }

    /// <summary>선금 — 맺으면서 받은 돈.</summary>
    public int Advance => Amount / 2;

    /// <summary>미불 — 성공하면 받을 돈.</summary>
    public int Unpaid => Amount / 2;

    /// <summary>기한이 다하는 날.</summary>
    public DateTime DueOn => SignedOn.AddYears(Years);

    /// <summary>
    /// 그 날짜 기준으로 기한이 며칠 남았는지. 이미 지났으면 0 이다.
    /// </summary>
    /// <remarks>
    /// 상단 띠의 「남은일수」 칸이 이 값이다 — 게임도 계약이 있을 때만 수를 내고
    /// 없으면 <c>남은일수----</c> 로 둔다(<c>0x0047DEF8</c>, 서식 <c>0x0056BF90</c>).
    /// 셈은 계약 정보 화면과 같은 눈이다 — 한 해를 <see cref="DaysPerYear"/> 로 센다
    /// (<c>0x0047F3DE</c>).
    /// </remarks>
    public int DaysLeftOn(DateTime today) =>
        (int)Math.Max(0, (DueOn - today).TotalDays);

    private readonly List<int> _found = [];

    /// <summary>이 계약을 맺은 뒤에 발견한 것(발견물 번호). 찾은 차례대로다.</summary>
    public IReadOnlyList<int> Found => _found;

    /// <summary>발견한 것을 이 계약에 얹는다. 처음 얹는 것이면 true.</summary>
    public bool Add(int discovery)
    {
        if (discovery < 0 || _found.Contains(discovery)) return false;
        _found.Add(discovery);
        return true;
    }

    /// <summary>세이브를 되돌릴 때 발견한 것을 그대로 채운다.</summary>
    public void Restore(IEnumerable<int>? found)
    {
        _found.Clear();
        if (found == null) return;
        foreach (int id in found) Add(id);
    }

    /// <summary>그 날 기준으로 남은 날수. 0 이하면 기한이 지난 것이다.</summary>
    public int DaysLeft(DateTime now) => (int)(DueOn - now).TotalDays;

    /// <summary>기한을 넘겼는지.</summary>
    /// <remarks>
    /// 게임은 <b>기한이 지났다고 저절로 무슨 일이 일어나지는 않는다</b> — 후원자를 다시
    /// 찾아갔을 때에야 따진다. 계약중단(<c>0x0044F7B0</c>)과 보고가 이 값으로 갈린다.
    /// </remarks>
    public bool IsOverdue(DateTime now) => DaysLeft(now) <= 0;

    /// <summary>
    /// 계약을 깨는 값 — <b>계약금의 절반</b>이라 받은 선금과 같다.
    /// </summary>
    /// <remarks>게임의 <c>0x0044F826</c> 이다 — <c>[0x5B619C] / 2</c>.</remarks>
    public int Penalty => Amount / 2;

    /// <summary>
    /// 남은 기한을 게임처럼 "몇 년 몇 개월" 로 쪼갠다.
    /// </summary>
    /// <remarks>
    /// 게임은 남은 <b>날수</b>를 365 로 나눠 햇수를, 그 나머지를 30 으로 나눠 달수를 낸다.
    /// 달수는 11 을 넘지 않게 자른다.
    /// </remarks>
    public (int Years, int Months) Remaining(DateTime now)
    {
        int days = DaysLeft(now);
        if (days <= 0) return (0, 0);

        int years = days / DaysPerYear;
        int months = (days - years * DaysPerYear) / DaysPerMonth;
        return (years, Math.Min(months, MaxMonths));
    }
}
