using CdsHelper.Game.Engine;
using CdsHelper.Game.Engine.Discovery;

namespace CdsHelper.Game.Local.Helpers;

/// <summary>
/// 인물이 도시 사이를 옮겨 다니는 시늉 — 매월 1일에 목적지를 뽑고, 닿으면 예순 날 쉰다.
/// </summary>
/// <remarks>
/// 게임의 손 넷을 그대로 옮긴 것이다(볼트 <c>72.분석-인물 이동(역사 항해사와 매달 굴림)</c>).
/// <code>
///   0x004327F0  달 넘김   14~200 만 굴린다. 활동 중 · 쉬는 날이 끝났고 · 가는 데가 없고
///                         · rand(5)==0 이라야 226곳에서 후보를 모아 하나를 뽑는다
///   0x00432995  달 넘김   0~13 은 굴리지 않고 제 대본(HISTCHR.CDS)을 돌린다 —
///                         그 안의 3C 08 <도시> 가 사람을 떠나보낸다
///   0x00432740  하루 넘김 날 셈에 1 을 더한다
///   0x00432470  자리 재기 날 셈 x 24 가 거리에 닿으면 도착
///   0x004325F0  도착      소재 도시 = 목적지, 날 셈 = -60
/// </code>
///
/// <b>상태를 적어 두지 않는다.</b> 굴림의 주사위를 <c>(인물 번호, 해, 달)</c> 로 씨를 뿌려
/// 굴리므로 같은 날짜면 늘 같은 세상이 된다 — 그래서 세이브에 넣을 것이 없고, 불러온 판도
/// <see cref="Advance"/> 한 번으로 그 날짜까지 따라잡는다. 마흔 해를 따라잡아도 굴림이
/// 한 달에 서른일곱 번쯤이라 사백만 셈이 채 안 된다.
///
/// <b>게임과 다른 것 둘.</b>
/// <list type="number">
///   <item><b>역사 항해자는 나이를 안 본다.</b> 아래 것과 같은 까닭이다.</item>
///
///   <item><b>역사 항해자 열넷은 활동 판정을 안 본다.</b> 게임은
///   <c>0x004327F0</c> 첫 줄에서 활동 판정을 먼저 하지만, 이들은 <b>대본이 곧 한살이</b>라
///   — 디아스는 1480~1500, 코론은 1485~1506 이 전부다 — 날짜를 대본에 맡기는 편이 판에
///   맞는다. 표의 0번 디아스는 1480년 세이브에서 나이가 <c>255</c> 라 나이로는 셀 수도
///   없다.</item>
/// </list>
///
/// <b>나이는 먹인다.</b> 게임은 해마다 한 살씩 올리고, 그것이 곧 사람이 나타나고
/// 스러지는 문이다 — 1480년 세이브에서 일곱 살인 후안·데·에스칸데는 1491년에야 술집에
/// 앉는다. 셈은 표가 한다(<see cref="PersonTable.ActiveOn"/>).
/// </remarks>
public sealed class PersonWorld
{
    /// <summary>하루에 나아가는 거리. <c>0x00432587</c> 의 <c>x24</c> 다.</summary>
    private const int SpeedPerDay = 24;

    /// <summary>닿고 나서 쉬는 날. <c>0x00432617</c> 의 <c>push -0x3c</c> 다.</summary>
    private const int RestDays = 60;

    /// <summary>세계가 감기는 너비. <c>0x9C4</c> 다.</summary>
    private const int WorldWidth = 0x9C4;

    /// <summary>몇 번 굴림에 한 번꼴로 움직이는가. 원본은 <c>0x0043284A</c> 의 <c>push 5</c> 다.</summary>
    /// <remarks>개발 창 「떠날 확률」로 1~5 사이에서 바꿀 수 있다.</remarks>
    private static int Odds => Local.Settings.GameSettings.PersonMoveOdds;

    /// <summary>굴림 간격 — 0 이면 매월 1일(원본), 1~30 이면 그 날수마다.</summary>
    /// <remarks>개발 창 「이동 주기」로 바꾼다.</remarks>
    private static int RollDays => Local.Settings.GameSettings.PersonRollDays;

    /// <summary>N일마다 굴릴 때 날을 세기 시작하는 날 — 판이 열리는 날이다.</summary>
    private static readonly DateTime RollEpoch = new(1480, 1, 1);

    /// <summary>
    /// 아직 세워지지 않은 도시 — 갈래 3(같은 나라)이 목적지로 삼지 않는다.
    /// </summary>
    /// <remarks>
    /// 언제 어느 도시가 서는지는 <see cref="CityFounding"/> 에 모아 두었다 —
    /// <c>HIST_EV.CDS</c> 의 신도시 이벤트 스무 벌이다. 날짜가 가면 하나씩 열리므로
    /// 이 목록도 달마다 달라진다.
    /// </remarks>
    private HashSet<int> NotFoundedYet =>
        [.. CityFounding.Hidden.Where(c => !CityFounding.FoundedBy(_asOf).Contains(c))];

    private readonly PersonTable _table;
    private readonly List<PersonTable.Row> _rows;
    private readonly CityExeTable? _cities;
    private readonly bool[] _harbor;

    /// <summary>뭍 비트를 볼 지도. 없으면 끝점을 도시 칸 그대로 쓴다.</summary>
    private readonly WorldCells? _map;

    /// <summary>도시마다 떠나고 닿는 칸. 한 번 재면 적어 둔다.</summary>
    private readonly (int X, int Y)?[] _access = new (int X, int Y)?[PersonTable.CityCount];

    /// <summary>역사 항해자 열넷의 대본. 없으면 그들은 안 움직인다.</summary>
    private readonly HistoryVoyages? _script;

    /// <summary>발견물 자리를 재려면 그 표가 있어야 한다.</summary>
    private readonly DiscoveryTable? _places;

    /// <summary>
    /// <b>도시가 아닌 자리</b>로 가는(또는 가 있는) 사람들 — 인물 번호 → 세계 좌표.
    /// </summary>
    /// <remarks>
    /// <c>3C 0B</c> 는 도시가 아니라 발견물 자리로 보내므로 목적지를 도시 번호로 적을 수가
    /// 없다. 인물 줄은 표에 구워 두는 것이라 여기에 곁으로 들고 있는다 —
    /// <see cref="PersonWorld"/> 는 어차피 아무것도 적어 두지 않고 날짜만으로 다시 셈한다.
    /// </remarks>
    private readonly Dictionary<int, (int X, int Y)> _bound = [];

    /// <summary>
    /// 목적지가 도시가 아니라 좌표일 때 <see cref="PersonTable.Row.Dest"/> 에 박는 값.
    /// </summary>
    public const int SpotDest = -2;

    private DateTime _asOf;

    /// <summary>
    /// 표를 받아 세상을 연다.
    /// </summary>
    /// <param name="start">놀이가 시작하는 날. 여기서부터 따라잡는다.</param>
    /// <param name="script">
    /// 역사 항해자 대본(<c>HISTCHR.CDS</c>). 없으면 0~13번은 제자리에 앉아 있는다.
    /// </param>
    /// <param name="places">
    /// 발견물 표. <c>3C 0B</c> 이 보내는 자리를 여기서 잰다 — 없으면 그 수는 건너뛴다.
    /// </param>
    /// <param name="map">
    /// WORLD.CDS 칸. 떠나고 닿는 칸을 도시 곁 바다·뭍 칸으로 잡는 데 쓴다 — 없으면 도시 칸 그대로다.
    /// </param>
    public PersonWorld(PersonTable table, CityExeTable? cities, CityBuildingTable? buildings,
                       DateTime start, HistoryVoyages? script = null,
                       DiscoveryTable? places = null, WorldCells? map = null)
    {
        _table = table;
        _rows = [.. table.People];
        _cities = cities;
        _harbor = Harbors(buildings);
        _script = script;
        _places = places;
        _map = map;
        _asOf = start;

        // 구워 온 표에는 길 위에 있던 사람이 그대로 들어 있는데(1517년 판에 쉰 명쯤)
        // 떠나 온 도시가 없어 거리를 잴 수가 없다. 길에서 걷어 제자리에 세운다.
        foreach (var row in _rows)
            if (row.Dest >= 0 && row.From < 0) row.Dest = -1;
    }

    /// <summary>지금 인물들. 표를 연 그 줄을 그대로 옮겨 다닌다.</summary>
    public IReadOnlyList<PersonTable.Row> People => _rows;

    /// <summary>밑에 깔린 표 — 나이를 셈할 때 쓴다(구운 해를 알고 있다).</summary>
    public PersonTable Table => _table;

    /// <summary>누가 움직일 때마다 하나씩 오른다 — 술집 목록을 다시 짤 때가 언제인지 알린다.</summary>
    public int Revision { get; private set; }

    /// <summary>어느 날까지 따라잡았는지.</summary>
    public DateTime AsOf => _asOf;

    /// <summary>지금 길 위에 있는 사람 수.</summary>
    public int Walking => _rows.Count(Moving);

    /// <summary>길 위에 있는가 — 도시로 가든 발견물 자리로 가든.</summary>
    public static bool Moving(PersonTable.Row row) => row.Dest >= 0 || row.Dest == SpotDest;

    /// <summary>
    /// 도시 밖에 서 있는 사람의 세계 좌표. 도시에 앉아 있으면 null.
    /// </summary>
    /// <remarks>발견물 자리로 간 사람은 닿은 뒤에도 그 자리에 머문다 — 다음 수까지다.</remarks>
    public (int X, int Y)? SpotOf(int person) =>
        _bound.TryGetValue(person, out var at) ? at : null;

    /// <summary>
    /// 누적 캐릭터의 행적을 되돌려 트는 손. 걸어 두면 날마다 함께 돈다.
    /// </summary>
    /// <remarks>
    /// 누적 캐릭터(인물 276~280)는 번호가 <see cref="PersonTable.MovingEnd"/> 뒤라 달마다
    /// 굴리지 않는다 — 대신 옛 판의 발자취를 그대로 따라 걷는다(<c>0x00432740</c>).
    /// </remarks>
    public Engine.AccReplay? Replay { get; set; }

    /// <summary>그 날짜까지 따라잡는다. 이미 지난 날이면 아무것도 안 한다.</summary>
    public void Advance(DateTime today)
    {
        if (today <= _asOf) return;

        // 매월 1일(대본)과 굴림 날(설정 간격)마다 끊어 나아간다.
        var at = _asOf;
        while (at < today)
        {
            var nextRoll = NextRollDay(at);
            var step = nextRoll <= today ? nextRoll : today;

            Walk((step - at).Days);
            at = step;
            if (at == nextRoll) Roll(at);
        }
        _asOf = today;

        Replay?.PassDay(today, People);
    }

    /// <summary>
    /// 그 날 뒤 첫 끊는 날 — 다음 달 1일(대본이 드는 날)과 다음 굴림 날 가운데 이른 쪽.
    /// </summary>
    private static DateTime NextRollDay(DateTime after)
    {
        var nextMonth = new DateTime(after.Year, after.Month, 1).AddMonths(1);
        if (RollDays <= 0) return nextMonth;

        // 1480년 1월 1일부터 센 날수를 간격으로 나눠 다음 배수로 올린다.
        long index = (after.Date - RollEpoch).Days;
        long next = (long)Math.Floor(index / (double)RollDays) * RollDays + RollDays;
        var nextRoll = RollEpoch.AddDays(next);
        return nextRoll < nextMonth ? nextRoll : nextMonth;
    }

    /// <summary>그 날이 떠날지 굴리는 날인가 — 원본은 매월 1일, 설정하면 간격의 배수 날.</summary>
    private static bool IsRollDay(DateTime day)
    {
        if (RollDays <= 0) return day.Day == 1;
        long index = (day.Date - RollEpoch).Days;
        return ((index % RollDays) + RollDays) % RollDays == 0;
    }

    // ── 하루 넘김과 도착 ───────────────────────────────────────────────────────

    private void Walk(int days)
    {
        if (days <= 0) return;

        foreach (var row in _rows)
        {
            row.Wait += days;
            if (Moving(row)) Arrive(row);
        }
    }

    private void Arrive(PersonTable.Row row)
    {
        int far = Distance(row);
        if (far < 0) return;                          // 자리를 모르면 그 자리에 둔다
        if (row.Wait * SpeedPerDay < far) return;     // 아직 가는 중

        // 발견물 자리로 간 사람은 앉을 도시가 없다 — 그 좌표에 그대로 선다.
        row.City = row.Dest == SpotDest ? -1 : row.Dest;
        row.From = -1;
        row.Dest = -1;
        row.Wait = -RestDays;
        Revision++;
    }

    /// <summary>출발 도시에서 목적지까지. 자리를 모르면 -1.</summary>
    private int Distance(PersonTable.Row row)
    {
        if (Leg(row) is not { } leg) return -1;
        return (int)Math.Sqrt((double)leg.Dx * leg.Dx + (double)leg.Dy * leg.Dy);
    }

    /// <summary>
    /// 지금 가고 있는 다리 — 떠난 자리와 <b>거기서부터 잰 어긋남</b>. 못 재면 null.
    /// </summary>
    /// <remarks>
    /// 세계가 <see cref="WorldWidth"/> 폭으로 감기므로 반 바퀴를 넘으면 짧은 쪽으로 돌린다
    /// (<c>0x004324E9</c> 의 <c>0x4E2</c> 견줌).
    /// </remarks>
    private (int Fx, int Fy, int Dx, int Dy)? Leg(PersonTable.Row row)
    {
        // 끝점은 도시 칸이 아니라 도시 곁의 바다·뭍 칸이다(Access).
        if (Access(row.From) is not { } from) return null;
        var (fx, fy) = from;

        int tx, ty;
        if (row.Dest == SpotDest)
        {
            if (!_bound.TryGetValue(row.Id, out var to)) return null;
            (tx, ty) = to;
        }
        else if (Access(row.Dest) is { } dest) (tx, ty) = dest;
        else return null;

        int dx = tx - fx, dy = ty - fy;
        if (Math.Abs(dx) >= WorldWidth / 2) dx += dx > 0 ? -WorldWidth : WorldWidth;
        return (fx, fy, dx, dy);
    }

    /// <summary>
    /// 그 사람이 <b>지금 서 있는 세계 칸</b>. 도시에 앉아 있으면 null.
    /// </summary>
    /// <remarks>
    /// 게임의 <c>0x00432470</c> 이다 — 하루하루 좌표를 옮겨 적지 않고 <b>물어볼 때 셈해
    /// 낸다</b>. 떠난 자리에서 목표 쪽으로 <c>날 셈 x 24 / 거리</c> 만큼 간 데다.
    /// 게임은 하루를 마흔여덟 눈금으로 쪼개 <c>(날 셈 x 48 + 눈금)</c> 으로 재므로
    /// <b>하루 안에서도 부드럽게</b> 움직인다(<c>0x0043259A</c> 의 <c>[0x5A4D2C]</c>).
    /// <paramref name="dayPart"/> 가 그 눈금 몫이다 — 안 주면 날 단위로 하루에 24칸씩 뛴다.
    ///
    /// 발견물 자리로 갔던 사람은 <b>닿은 뒤에도 그 자리에 서 있다</b> — 앉을 도시가 없다.
    /// </remarks>
    /// <param name="dayPart">오늘 하루 가운데 지난 몫(0 이상 1 미만).</param>
    public (double X, double Y)? CellOf(PersonTable.Row row, double dayPart = 0)
    {
        if (!Moving(row))
            return _bound.TryGetValue(row.Id, out var stood) ? (stood.X, stood.Y) : null;

        if (Leg(row) is not { } leg) return null;

        int far = (int)Math.Sqrt((double)leg.Dx * leg.Dx + (double)leg.Dy * leg.Dy);
        double days = row.Wait + Math.Clamp(dayPart, 0, 1);
        double gone = far <= 0 ? 1 : Math.Clamp(days * SpeedPerDay / far, 0, 1);

        double x = leg.Fx + leg.Dx * gone, y = leg.Fy + leg.Dy * gone;
        if (x < 0) x += WorldWidth;
        else if (x >= WorldWidth) x -= WorldWidth;
        return (x, y);
    }

    /// <summary>
    /// 그 사람의 뱃머리 — 16방위(0 북 · 4 서 · 8 남 · 12 동). 서 있으면 남쪽을 본다.
    /// </summary>
    /// <remarks>
    /// 게임은 네 쪽만 쓴다(<c>0x00432596</c>) — 어긋남이 큰 축을 골라 그 부호로 정한다.
    /// </remarks>
    public int HeadingOf(PersonTable.Row row)
    {
        if (Leg(row) is not { } leg) return 8;
        return Math.Abs(leg.Dy) > Math.Abs(leg.Dx)
            ? (leg.Dy < 0 ? 0 : 8)
            : (leg.Dx < 0 ? 4 : 12);
    }

    /// <summary>
    /// 지도에 세울 사람들 — 도시 밖에 자리가 있는 이들이다.
    /// </summary>
    /// <remarks>
    /// 게임의 <c>0x00426790</c> 은 281명을 통째로 훑어 <b>자리가 있는 사람</b>만 골라
    /// 앞에서부터 열여섯 칸을 채운다. 번호로 거르지 않으므로 역사 항해자도 그대로
    /// 걸린다 — 바다에서 마주치는 것이 이 때문이다. 몇을 낼지는 부르는 쪽이 정한다.
    /// </remarks>
    /// <param name="dayPart">오늘 하루 가운데 지난 몫. <see cref="CellOf"/> 에 그대로 넘긴다.</param>
    public IEnumerable<(PersonTable.Row Who, double X, double Y, int Heading)> Afloat(double dayPart = 0)
    {
        foreach (var row in _rows)
        {
            if (!Active(row) && row.Id >= PersonTable.VoyagerCount) continue;
            if (CellOf(row, dayPart) is not { } at) continue;
            yield return (row, at.X, at.Y, HeadingOf(row));
        }
    }

    /// <summary>
    /// 바다에서 붙고 난 사람을 제 나라 수도로 돌려보내고 예순 날 재운다.
    /// </summary>
    /// <remarks>
    /// 게임의 <c>0x00432400</c> 이다 — 해전 뒤끝(<c>0x0048CCD7</c>)이 부른다. 목적지를 지우고
    /// 좌표를 걷고, 소재 도시를 나라 형편 칸의 수도로 박고, 날 셈을 -60 으로 둔다.
    /// 우리는 표를 적어 두지 않으므로 판을 다시 열면 이 일은 잊힌다.
    /// </remarks>
    /// <param name="capital">돌려보낼 수도. 모르면 -1 — 게임도 그 값을 그대로 박는다.</param>
    public void SendHome(PersonTable.Row row, int capital)
    {
        row.Dest = -1;
        row.From = -1;
        _bound.Remove(row.Id);
        row.City = capital;
        row.Wait = -RestDays;
        Revision++;
    }

    // ── 달 넘김 ────────────────────────────────────────────────────────────────

    private void Roll(DateTime when)
    {
        bool rollDay = IsRollDay(when);

        foreach (var row in _rows)
        {
            // 역사 항해자는 여기서 갈린다 — 활동 판정보다 앞이다(위 <b>다른 것 둘</b>).
            // 대본은 달 단위라 1일에만 든다(달 중간 굴림에 들면 한 달 수를 두 번 둔다).
            if (row.Id < PersonTable.VoyagerCount) { if (when.Day == 1) Sail(row, when); continue; }
            if (!rollDay) continue;                             // 대본만 드는 1일이다
            if (row.Id >= PersonTable.MovingEnd) continue;     // 이벤트 인물은 안 움직인다
            if (!Active(row)) continue;
            if (row.Wait < 0) continue;                        // 아직 쉬는 중
            if (row.Dest >= 0) continue;                       // 이미 가는 중

            var dice = new GameRandom(Seed(row.Id, when));
            if (dice.Next(Odds) != 0) continue;

            var picks = Candidates(row);
            if (picks.Count == 0) continue;

            row.From = row.City;
            row.Dest = picks[dice.Next(picks.Count)];
            row.City = -1;                                     // 도시에서 빠진다
            row.Wait = 0;
            Revision++;
        }
    }

    /// <summary>
    /// 역사 항해자 하나를 제 대본대로 떠나보낸다(<c>0x00432995</c>).
    /// </summary>
    /// <remarks>
    /// 이들은 주사위를 안 굴린다 — 대본에 적힌 달이 오면 <b>쉬는 중이든 가는 중이든</b>
    /// 그 자리에서 다시 떠난다. 게임의 <c>3C 08</c>(<c>0x0040AB77</c>)이 목적지를 박기 전에
    /// 아무 문턱도 안 보기 때문이다.
///
    /// 출발 자리는 지금 앉은 도시고, 이미 길 위였으면 <b>떠나 온 도시를 그대로 물려받는다</b>.
    /// 게임은 그때 지금 좌표를 재어 새 출발점으로 삼는데(<c>0x0040AC17</c>), 우리는 좌표를
    /// 안 들고 있어 도시 둘 사이로만 거리를 잰다 — 대본이 한 달에 한 수씩이라 어긋나 봐야
    /// 한 다리다.
///
    /// <b><c>3C 0B</c>(발견물 자리로)는 원본을 안 따르고 <b>고쳐서</b> 옮긴다.</b> 게임이
    /// 내는 자리가 <c>((x2-x1)/2, (y2-y1)/2)</c> 라 <b>넓이의 절반</b>이지 가운데가 아니다
    /// (<c>0x0040AD15</c> 의 <c>sub eax, edi</c> — 바이트가 <c>2B C7</c> 이라 <c>03 C7</c>
    /// 의 오타로 보인다). 발견물 6번이면 <c>(0, 0)</c> 이 나와 지도 왼쪽 위 모서리로
    /// 날아간다. 그래서 <c>(x1+x2)/2</c> 로 <b>진짜 가운데</b>를 낸다 — 마가랴네스·엘카노가
    /// 제 항로에 뜬다. 열두 수뿐이고 발견물도 <c>6 · 185 · 186</c> 셋뿐이다.
    /// </remarks>
    private void Sail(PersonTable.Row row, DateTime when)
    {
        if (_script is not { } script) return;

        foreach (var move in script.MovesOn(row.Id, when.Year, when.Month))
        {
            if (move.ToCity && move.City == row.City) continue;   // 이미 그 도시에 앉아 있다

            (int X, int Y)? spot = move.ToCity ? null : Middle(move.Discovery);
            if (!move.ToCity && spot == null) continue;           // 자리를 못 재면 그냥 둔다

            int from = row.City >= 0 ? row.City : row.From;
            if (from < 0)
            {
                // 떠나는 도시를 모른다. 두 가지다 — 구워 온 표에 자리가 없는 사람 셋
                // (디아스 · 엘카노 · 칼티에)의 첫 수이거나, 발견물 자리에 서 있다가
                // 다시 떠나는 수다. 거리는 도시 둘 사이로만 재므로 길 없이 그 자리에
                // 세우고, 다음 수부터 제대로 항해한다.
                if (move.ToCity) { row.City = move.City; _bound.Remove(row.Id); }
                else _bound[row.Id] = spot!.Value;

                row.Wait = 0;
                Revision++;
                return;
            }

            row.From = from;
            row.City = -1;                                       // 도시에서 빠진다
            row.Wait = 0;

            if (move.ToCity) { row.Dest = move.City; _bound.Remove(row.Id); }
            else { row.Dest = SpotDest; _bound[row.Id] = spot!.Value; }

            Revision++;
            return;                                              // 한 달에 한 수면 넉넉하다
        }
    }

    /// <summary>
    /// 그 발견물이 놓인 네모의 <b>가운데</b>. 자리가 없는 발견물이면 null.
    /// </summary>
    /// <remarks>
    /// 게임은 여기서 <c>(x2-x1)/2</c> 를 내는데 그것은 넓이의 절반이다 — 위 주석 참고.
    /// </remarks>
    private (int X, int Y)? Middle(int discovery)
    {
        if (_places?.Find(discovery) is not { } spot || !spot.HasPlace) return null;
        return ((spot.X1 + spot.X2) / 2, (spot.Y1 + spot.Y2) / 2);
    }

    /// <summary>등장했고 열여덟에서 예순 사이인가.</summary>
    private bool Active(PersonTable.Row row) => _table.ActiveOn(row, _asOf.Year);

    /// <summary>갈 만한 도시를 모은다. 하나도 없으면 그 달은 안 움직인다.</summary>
    private List<int> Candidates(PersonTable.Row row)
    {
        var got = new List<int>();
        if (_cities is not { } cities || row.City < 0) return got;

        int region = cities.RegionOf(row.City);
        int culture = cities.CultureOf(row.City);
        int nation = cities.NationOf(row.City);

        for (int city = 0; city < PersonTable.CityCount; city++)
        {
            bool ok = row.Kind switch
            {
                0 => cities.RegionOf(city) == region && Harbor(city),
                1 => cities.CultureOf(city) == culture && Harbor(city),
                3 => cities.NationOf(city) == nation && !NotFoundedYet.Contains(city),
                _ => false,                       // 갈래 2 는 후보를 못 담아 영영 안 움직인다
            };
            if (ok) got.Add(city);
        }
        return got;
    }

    /// <summary>
    /// 그 달 그 사람의 주사위. <c>(번호, 해, 달)</c> 로 씨를 뿌려 언제 따라잡아도 같게 나온다.
    /// </summary>
    /// <remarks>
    /// 15일 굴림은 날을 더 섞어 1일과 다른 눈이 나오게 한다. 1일은 예전 씨 그대로라
    /// 매월 1일만 굴리는 원본 설정의 세상은 바뀌지 않는다.
    /// </remarks>
    private static int Seed(int id, DateTime when) =>
        (id * 10007) ^ (when.Year * 137 + when.Month * 11 + (when.Day == 1 ? 0 : when.Day * 7919));

    // ── 들여다보기 ─────────────────────────────────────────────────────────────

    /// <summary>그 사람이 지금 돌아다닐 수 있는가 — 등장했고 열여덟에서 예순 사이다.</summary>
    public bool IsActive(PersonTable.Row row) => Active(row);

    /// <summary>
    /// 목적지까지 남은 날. 길 위가 아니거나 자리를 못 재면 null.
    /// </summary>
    /// <remarks>
    /// 닿는 셈이 <c>날 셈 x 24 ≥ 거리</c> 라(<c>0x00432587</c>) 거리를 24 로 올림 나눈 날에서
    /// 이미 간 날을 뺀다.
    /// </remarks>
    public int? DaysLeft(PersonTable.Row row)
    {
        if (!Moving(row)) return null;
        int far = Distance(row);
        if (far < 0) return null;
        int need = (far + SpeedPerDay - 1) / SpeedPerDay;
        return Math.Max(0, need - row.Wait);
    }

    /// <summary>
    /// 다음 굴림에서 고를 수 있는 도시들. 도시에 앉아 있지 않으면 빈 목록이다.
    /// </summary>
    /// <remarks>달 넘김(<c>0x004327F0</c>)이 모으는 후보 그대로다 — 갈래가 해역·문화권·나라를 가른다.</remarks>
    public IReadOnlyList<int> CandidatesOf(PersonTable.Row row) => Candidates(row);

    /// <summary>
    /// 역사 항해자가 <paramref name="from"/> 부터 <paramref name="months"/> 달 안에 둘 대본 수들.
    /// 대본이 없거나 역사 항해자가 아니면 빈 목록이다.
    /// </summary>
    public IEnumerable<HistoryVoyages.Move> ScriptAhead(PersonTable.Row row, DateTime from, int months)
    {
        if (_script is not { } script || row.Id >= PersonTable.VoyagerCount) yield break;

        var month = new DateTime(from.Year, from.Month, 1);
        for (int i = 0; i < months; i++, month = month.AddMonths(1))
            foreach (var move in script.MovesOn(row.Id, month.Year, month.Month))
                yield return move;
    }

    // ── 끝점 ───────────────────────────────────────────────────────────────────

    /// <summary>
    /// 사람이 그 도시를 떠나고 닿는 칸 — 도시 칸이 아니라 곁의 바다 칸(항구) 또는 뭍 칸이다.
    /// 도시 자리를 모르면 null.
    /// </summary>
    /// <remarks>
    /// 게임의 <c>0x00425BE0</c>(항구 있음) · <c>0x00425D10</c>(없음)이다. 떠날 때는 떠나는 도시의
    /// 항구로, 갈 때는 목적 도시의 항구로 가른다.
    /// <code>
    ///   도시 칸 (x, y) · r = 도시가 차지하는 칸 수(+0x0C, 2·3)
    ///   dy·dx 를 각각 -1 ~ r 로 훑는다(가운데가 아니라 한 칸 앞에서 시작한다)
    ///   항구면 뭍 비트(0x4000)가 꺼진 칸, 아니면 켜진 칸만 본다
    ///   무게 = 표[dy + 4r] + 표[dx + 4r]   (뭍은 여덟 칸 뒤)
    ///   무게가 0x20 보다 작은 칸 가운데 처음 나온 가장 가벼운 칸. 없으면 도시 칸 그대로
    /// </code>
    /// 리스본(1185,355)은 (1185,357), 오포르토(1189,338)는 (1188,338), 고아(1762,517)는
    /// (1761,517)로 떨어진다. 내륙 마그데부르크는 무게 0 인 제 칸 그대로다.
    /// </remarks>
    private (int X, int Y)? Access(int city)
    {
        if (city < 0 || city >= _access.Length || _cities is not { } cities) return null;
        if (_access[city] is { } known) return known;
        if (!cities.TryCell(city, out int cx, out int cy, out int reach)) return null;

        var best = (cx, cy);
        if (_map is { } map)
        {
            bool sea = Harbor(city);
            int shift = 4 * reach + (sea ? 0 : 8);
            int least = 0x20;

            for (int dy = -1; dy <= reach; dy++)
            {
                int y = cy + dy;
                if (y < 0 || y >= WorldCells.Height) continue;

                for (int dx = -1; dx <= reach; dx++)
                {
                    int x = cx + dx;
                    if (map.IsLand(x, y) == sea) continue;

                    int i = dy + shift, j = dx + shift;
                    if (i < 0 || j < 0 || i >= Weights.Length || j >= Weights.Length) continue;

                    int weight = Weights[i] + Weights[j];
                    if (weight >= least) continue;
                    least = weight;
                    best = (((x % WorldCells.Width) + WorldCells.Width) % WorldCells.Width, y);
                }
            }
        }

        _access[city] = best;
        return best;
    }

    /// <summary>
    /// 끝점 무게표 <c>0x0053C34C</c>. 바다는 <c>[off + 4r]</c>, 뭍은 여덟 칸 뒤를 읽는다.
    /// </summary>
    /// <remarks>
    /// 마지막 값은 표가 아니라 그 뒤에 붙은 포인터다 — 뭍 도시 중 r=3 인 곳의 가장자리가 거기까지
    /// 읽어 절대 뽑히지 않는다. 게임이 그렇게 굴러가므로 그대로 옮겼다.
    /// </remarks>
    private static readonly int[] Weights =
        [1, 4, 1, 1, 1, 5, 1, 1, 0, 1, 4, 1, 0, 0, 1, 1, 0, 1, 4, 1, 0, 0, 1, 5454144];

    // ── 항구 ───────────────────────────────────────────────────────────────────

    private bool Harbor(int city) =>
        city >= 0 && city < _harbor.Length && _harbor[city];

    /// <summary>
    /// 도시마다 항구가 있는지. 건물 표를 못 읽으면 <b>다 있는 셈</b> 친다 —
    /// 게임 폴더를 모르는 자리에서도 사람이 돌아다니게 해 두는 편이 낫다.
    /// </summary>
    /// <remarks>항구는 건물 코드 0 이다(<see cref="CityBuildingTable.Building.Code"/>).</remarks>
    private static bool[] Harbors(CityBuildingTable? buildings)
    {
        var got = new bool[PersonTable.CityCount];
        if (buildings == null)
        {
            Array.Fill(got, true);
            return got;
        }
        foreach (var building in buildings.Buildings)
            if (building.Code == 0 && building.City >= 0 && building.City < got.Length)
                got[building.City] = true;
        return got;
    }
}
