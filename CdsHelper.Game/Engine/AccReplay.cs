using CdsHelper.Game.Local.Helpers;
using CdsHelper.Support.Local.Models;

namespace CdsHelper.Game.Engine;

/// <summary>
/// 누적 캐릭터의 <b>행적을 되돌려 튼다</b>(<c>0x00432740</c> → <c>0x0040D1D0</c>).
/// </summary>
/// <remarks>
/// 은퇴한 제독은 다음 판에서 인물 276~280 에 앉고(<see cref="AccData.Place"/>), 날마다 제
/// 행적 대본을 한 줄씩 읽어 그때 그 자리로 옮겨 다닌다. 게임은 인물 레코드에 세 칸을 둔다.
/// <code>
///   +0x110  지난 날수      +0x114  대본 파일 위치      +0x118  달 오프셋(늦어짐)
/// </code>
/// 우리는 대본 대신 <see cref="Player.Trace"/> 목록을 그대로 걸어 두고, <b>판이 열린 날부터
/// 흐른 날수</b>에 늦어짐을 뺀 값으로 줄을 짚는다. 줄이 다하면 세상에서 사라진다
/// (원본도 <c>0x0040D1D0</c> 이 −1 을 주면 그렇게 한다).
///
/// 늦어짐은 <c>0x004A4A3D</c> 다 — 술집 일기토에서 지면 <c>(rand(3) x 3 + 3) x 4</c> 날만큼
/// 밀리고 「%s의 행동이 늦어졌습니다」가 뜬다(그 자리는 <see cref="Delay"/>).
/// </remarks>
public sealed class AccReplay
{
    /// <summary>누적 캐릭터 하나가 어디까지 왔는지.</summary>
    private sealed class Runner
    {
        public required int Person { get; init; }
        public required Player.Trace[] Track { get; init; }

        /// <summary>대본이 시작하는 날 — 그 사람의 첫 행적 날짜다.</summary>
        public required DateTime From { get; init; }

        /// <summary>늦어진 날수(<c>인물 +0x118</c>).</summary>
        public int Late { get; set; }

        /// <summary>다 틀었는지(<c>0x0040D1D0</c> 이 −1 을 준 뒤).</summary>
        public bool Done { get; set; }

        /// <summary>다음에 틀 줄.</summary>
        public int Next { get; set; }

        /// <summary>
        /// 함대 목록 — 선체 번호 여덟 칸, 빈 칸은 −1(<c>0x00589C70 + 인물 x 32</c>, 처음엔 다 비었다 <c>0x00431C4C</c>).
        /// </summary>
        public int[] Hulls { get; } = [-1, -1, -1, -1, -1, -1, -1, -1];
    }

    private readonly List<Runner> _runners = [];
    private readonly DateTime _opened;

    /// <summary>판이 열린 날부터 센다.</summary>
    public AccReplay(DateTime opened) => _opened = opened;

    /// <summary>
    /// 올라 있는 사람들의 대본을 건다 — <see cref="AccData.Place"/> 바로 뒤에 부른다.
    /// </summary>
    public void Load()
    {
        _runners.Clear();
        var all = AccData.Load();
        for (int i = 0; i < all.Count && i < AccData.Slots; i++)
        {
            var track = all[i].Track;
            if (track is not { Length: > 0 }) continue;
            _runners.Add(new Runner
            {
                Person = AccData.FirstPerson + i,
                Track = track,
                From = track[0].On,
            });
        }
    }

    /// <summary>공략 줄을 틀 때 부른다 — (인물, 도시, 나라). 도시를 넘기고 알리는 것은 부르는 쪽이 한다.</summary>
    public Action<int, int, int>? Captured { get; set; }

    /// <summary>
    /// 발견물 보고 줄을 틀 때 부른다 — (인물, 발견물). 적어 두고 알리는 것은 부르는 쪽이 한다.
    /// </summary>
    /// <remarks>
    /// 행적 갈래 9 는 되살아날 때 대본 명령 <c>68 0B [발견물]</c> 이 되고
    /// (<c>0x0041A7CF</c>), 그 명령이 발견물 칸 2 에 그 사람 이름을 올린다
    /// (<c>0x0040B916</c> → <c>0x004AACA0</c>). 그래서 <b>내가 먼저 찾아 두었어도</b>
    /// 남이 세상에 알려 버리면 보고 사례가 깎인다.
    /// </remarks>
    public Action<int, int>? Announced { get; set; }

    /// <summary>걸린 대본이 하나라도 있는지.</summary>
    public bool Any => _runners.Count > 0;

    /// <summary>
    /// 하루를 넘긴다 — 사람마다 그날 있어야 할 자리로 옮긴다.
    /// </summary>
    /// <param name="today">놀이 날짜.</param>
    /// <param name="people">인물 표.</param>
    public void PassDay(DateTime today, IReadOnlyList<PersonTable.Row> people)
    {
        foreach (var run in _runners)
        {
            if (run.Done || run.Person >= people.Count) continue;

            // 판이 열린 날부터 흐른 날수에서 늦어진 만큼을 뺀다.
            int gone = (int)(today - _opened).TotalDays - run.Late;
            if (gone < 0) continue;

            var at = run.From.AddDays(gone);
            int step = Array.FindLastIndex(run.Track, t => t.On <= at);
            if (step < 0) continue;

            // 그날까지의 줄을 차례로 튼다 — 배가 드나든 줄은 함대 목록을 채우고 지운다.
            int arrival = -1;
            for (; run.Next <= step; run.Next++)
            {
                var line = run.Track[run.Next];
                if (line.Kind == Player.TraceArrival) arrival = run.Next;
                else if (line.Kind == Player.TraceShipIn) AddHull(run.Hulls, line.A);
                else if (line.Kind == Player.TraceShipOut) RemoveHull(run.Hulls, line.A);
                else if (line.Kind == Player.TraceCapture) Captured?.Invoke(run.Person, line.A, line.B);
                else if (line.Kind == Player.TraceDiscovery) Announced?.Invoke(run.Person, line.A);
            }

            // 마지막 줄까지 갔고 그날도 지났으면 세상에서 사라진다.
            if (step == run.Track.Length - 1 && at > run.Track[^1].On)
            {
                people[run.Person].Appear = 0;
                run.Done = true;
                continue;
            }

            if (arrival < 0) continue;
            var row = people[run.Person];
            row.City = run.Track[arrival].A;
            row.Building = PersonTable.Tavern;
            row.Appear = 1;
        }
    }

    /// <summary>
    /// 명령 <c>69 [선체]</c>(<c>0x0040B9ED</c>) — 선체를 0~7 로 자르고(밖이면 0) 첫 빈 칸에 넣는다.
    /// 빈 칸이 없으면 아무 일도 없다.
    /// </summary>
    private static void AddHull(int[] hulls, int hull)
    {
        if (hull is < 0 or > 7) hull = 0;
        int at = Array.IndexOf(hulls, -1);
        if (at >= 0) hulls[at] = hull;
    }

    /// <summary>명령 <c>6A [선체]</c>(<c>0x0040BA52</c>) — 그 선체가 든 첫 칸을 비운다.</summary>
    private static void RemoveHull(int[] hulls, int hull)
    {
        if (hull is < 0 or > 7) hull = 0;
        int at = Array.IndexOf(hulls, hull);
        if (at >= 0) hulls[at] = -1;
    }

    /// <summary>
    /// 그 누적 캐릭터를 습격할 때의 함대 — 빈 칸을 걷어 앞으로 모은 선체 번호들(<c>0x0048CC62</c>).
    /// </summary>
    /// <returns>누적 캐릭터가 아니거나 목록이 비었으면 null — 그러면 여느 적처럼 짓는다(<c>0x0048CCA4</c>).</returns>
    public int[]? FleetOf(int person)
    {
        var run = _runners.Find(r => r.Person == person);
        if (run == null) return null;
        int[] hulls = [.. run.Hulls.Where(h => h != -1)];
        return hulls.Length > 0 ? hulls : null;
    }

    /// <summary>
    /// 그 사람의 행적을 늦춘다(<c>0x004A4A3D</c>) — <c>(rand(3) x 3 + 3) x 4</c> 날이다.
    /// </summary>
    /// <returns>늦춘 날수. 그 사람이 대본을 안 들고 있으면 0.</returns>
    public int Delay(int person, Random dice)
    {
        var run = _runners.Find(r => r.Person == person && !r.Done);
        if (run == null) return 0;

        int days = (dice.Next(3) * 3 + 3) * 4;
        run.Late += days;
        return days;
    }

    /// <summary>「%s의 행동이 늦어졌습니다」(<c>0x005516B8</c>).</summary>
    public static string Delayed(string name) => $"{name}의 행동이 늦어졌습니다";

    /// <summary>늦추고 나서 붙는 악명(<c>0x004A4A66</c> 의 <c>0x4697C0(1, 100)</c>).</summary>
    public const int DelayInfamy = 100;
}
