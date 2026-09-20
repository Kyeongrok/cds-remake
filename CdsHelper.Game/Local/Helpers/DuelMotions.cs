using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CdsHelper.Game.Local.Helpers;

/// <summary>
/// 일기토 몸짓 표 — 어느 장을 몇 초 동안, 얼마나 앞으로 나가서 낼지.
/// </summary>
/// <remarks>
/// <b>모션 메이커와 놀이가 같이 보는 표다.</b> 메이커에서 고쳐 저장하면
/// <c>asset/duel/motion.json</c> 이 되고, 놀이의 일기토 판(<see cref="UI.Views.DuelStage"/>)이
/// 그 파일을 읽어 돈다 — 다시 굽지 않아도 든다. 파일이 없으면 여기 박아 둔 밑값을 쓴다.
///
/// 밑값은 게임 표 <c>0x00572A40</c> 에서 짚은 그대로다.
/// <code>
///   눈금 하나  0.067초(1/15초)
///   눈금 0~7   여느 자세로 다가서거나 물러난다 — 한 눈금에 5점, 여덟 눈금에 40점
///   눈금 8·9   찌르는 첫 장          10  둘째 장       11~14  셋째 장(+30점)
///   눈금 15~   여느 첫 장 하나로 선다
/// </code>
/// <b>점은 「선 자리에서 상대 쪽으로」 나간 거리다.</b> 그래서 아군이든 적군이든 같은 수를
/// 쓴다 — 그리는 쪽에서 왼쪽이냐 오른쪽이냐만 갈린다.
/// </remarks>
public static class DuelMotions
{
    /// <summary>눈금 하나의 길이. 게임이 1/15초로 돈다.</summary>
    public const double Tick = 0.067;

    /// <summary>
    /// 밑값 몸짓에서 <b>장마다 덜어 내는 시간</b>.
    /// </summary>
    /// <remarks>
    /// 갈무리로 잰 눈금은 0.067초인데, 화면에 맞대어 보면 그대로 돌리면 <b>느리다</b>.
    /// 장마다 0.01초씩 덜어 내면 맞는다 — 한 눈금짜리 장은 0.07 에서 0.06 으로,
    /// 넉 눈금짜리는 0.27 에서 0.26 으로 준다.
    ///
    /// 눈금 길이 자체를 줄이지 않은 까닭은, 그 값이 갈무리에서 <b>잰 값</b>이라서다.
    /// 여기서 덜어 내면 「어디까지가 잰 것이고 어디부터가 맞춘 것인지」가 갈린다.
    /// </remarks>
    public const double Trim = 0.01;

    /// <summary>눈금 <paramref name="ticks"/> 개짜리 장이 머무는 시간.</summary>
    private static double Span(double ticks) => Math.Max(0.01, Tick * ticks - Trim);

    /// <summary>한 눈금에 옮기는 거리와, 한 판에 다가서거나 물러나는 거리.</summary>
    public const double StepWay = 5, Drift = 40;

    /// <summary>찌를 때 더 나가는 거리(<c>0x004A794A</c> 의 <c>sub eax,0x1E</c>).</summary>
    public const double LungeWay = 30;

    /// <summary>
    /// 주고받는 동안 <b>한 쪽이 더 붙는 거리</b>.
    /// </summary>
    /// <remarks>
    /// 표에서 짚은 값(다가섬 40 · 내지름 30)만으로 돌리면 맞는 눈금에 두 사람 발 사이가
    /// 88점이라 화면보다 멀다. 두 쪽이 열 점씩 더 붙어 <b>68점</b>이라야 눈에 맞는다.
    ///
    /// <b>코드에서 나온 값이 아니라 화면에 맞대어 잡은 값이다.</b> 주고받는 세 장에만
    /// 얹고 꼬리에는 안 얹는다 — 꼬리에까지 얹으면 판이 끝날 때 담기는 거리가 늘어
    /// 판을 거듭할수록 둘이 붙어 버린다.
    /// </remarks>
    public const double Close = 10;

    /// <summary>적어 둔 파일이 앉는 곳.</summary>
    public const string FileName = "motion.json";

    /// <summary>차례 한 자리.</summary>
    /// <param name="Frame">스프라이트셋 안의 장 번호(0~32).</param>
    /// <param name="Seconds">그 장이 머무는 시간.</param>
    /// <param name="Push">그동안 상대 쪽으로 나가 있는 거리(점). 뒤로 물러나면 음수다.</param>
    public sealed record Step(int Frame, double Seconds, double Push);

    /// <summary>몸짓 하나.</summary>
    /// <param name="Key">놀이가 찾아 쓰는 이름 — 이것으로 짝을 짓는다.</param>
    /// <param name="Name">사람이 읽는 이름.</param>
    /// <param name="Steps">장 차례.</param>
    public sealed record Motion(string Key, string Name, Step[] Steps)
    {
        /// <summary>다 도는 데 걸리는 시간.</summary>
        public double Length => Steps.Sum(s => s.Seconds);

        /// <summary>
        /// 그 눈금에 보일 장과 그때 나가 있는 거리. 차례가 끝났으면 마지막 자리다.
        /// </summary>
        public (int Frame, double Push) At(int tick)
        {
            if (Steps.Length == 0) return (0, 0);

            double want = tick * Tick, at = 0;
            foreach (var step in Steps)
            {
                at += step.Seconds;
                if (want < at - Tick / 2) return (step.Frame, step.Push);
            }
            var last = Steps[^1];
            return (last.Frame, last.Push);
        }
    }

    // ── 이름 ────────────────────────────────────────────────────────────────

    public const string Walk = "walk", Idle = "idle", Victory = "victory", Fall = "fall";

    /// <summary>찌르는 몸짓 이름 — 다가서는 갈래마다 따로다.</summary>
    /// <param name="line">0 상단 · 1 중단 · 2 하단.</param>
    /// <param name="way">
    /// <c>+1</c> 다가서며 찌른다(공격 판) · <c>0</c> 제자리에서 찌른다(맞부딪힘).
    /// </param>
    public static string ThrustKey(int line, int way) =>
        (way == 0 ? "clash-" : "attack-") + Lines[Math.Clamp(line, 0, 2)];

    /// <summary>막는 몸짓 이름 — 물러나는 갈래마다 따로다.</summary>
    /// <param name="guard">0 뛴다 · 1 피한다 · 2 웅크린다.</param>
    /// <param name="way">
    /// <c>−1</c> 물러나며 막는다 · <c>0</c> 제자리에서 막는다(벽에 닿았거나 맞부딪힘).
    /// </param>
    public static string GuardKey(int guard, int way) =>
        (way == 0 ? "hold-" : "guard-") + Guards[Math.Clamp(guard, 0, 2)];

    private static readonly string[] Lines = ["high", "mid", "low"];
    private static readonly string[] Guards = ["jump", "dodge", "crouch"];

    // ── 밑값 ────────────────────────────────────────────────────────────────

    /// <summary>
    /// 판이 열리고 여덟 눈금 동안 여느 자세로 다가서거나 물러나는 앞머리.
    /// </summary>
    /// <remarks>
    /// 여느 자세는 <c>0 · 1 · 0 · 2</c> 로 돌고(<c>0x004A7528</c>), 자리는 한 눈금에 다섯
    /// 점씩 간다(<c>0x004A759C</c>).
    /// </remarks>
    private static IEnumerable<Step> Opening(int way)
    {
        for (int tick = 0; tick < 8; tick++)
            yield return new Step(IdleFrame(tick), Span(1), way * tick * StepWay);
    }

    /// <summary>그 눈금에 설 여느 장 — 짝수면 30, 넷으로 나눠 1 이면 31, 3 이면 32 다.</summary>
    public static int IdleFrame(int tick) =>
        30 + (tick % 2 == 0 ? 0 : tick % 4 == 1 ? 1 : 2);

    /// <summary>찌르는 한 판. <paramref name="way"/> 가 0 이면 제자리에서 찌른다.</summary>
    private static Motion Thrust(string key, string name, int first, int way) => new(key, name,
    [
        .. Opening(way),
        new Step(first, Span(2), way * Drift + Close),
        new Step(first + 1, Span(1), way * Drift + Close),
        new Step(first + 2, Span(4), way * Drift + LungeWay + Close),
        new Step(30, Span(18), way * Drift),
    ]);

    /// <summary>막는 한 판. <paramref name="way"/> 가 0 이면 제자리에서 막는다.</summary>
    private static Motion Block(string key, string name, int first, int way) => new(key, name,
    [
        .. Opening(way),
        new Step(first, Span(2), way * Drift + Close),
        new Step(first + 1, Span(1), way * Drift + Close),
        new Step(first + 2, Span(4), way * Drift + Close),
        new Step(30, Span(18), way * Drift),
    ]);

    /// <summary>여섯 장짜리 — 두 눈금에 한 장, 끝 두 장을 번갈아 낸다(<c>0x004A8155</c>).</summary>
    private static Motion Six(string key, string name, int first) => new(key, name,
    [
        new Step(first, Span(2), 0), new Step(first + 1, Span(2), 0),
        new Step(first + 2, Span(2), 0), new Step(first + 3, Span(2), 0),
        new Step(first + 4, Span(2), 0), new Step(first + 5, Span(2), 0),
        new Step(first + 4, Span(2), 0), new Step(first + 5, Span(2), 0),
    ]);

    /// <summary>
    /// 걸어 나오기를 마친 자리 — 선 자리 그대로다.
    /// </summary>
    /// <remarks>
    /// 한때 −70 에서 시작하게 열 점 당겨 두었는데, 그것은 <b>서는 자리를 잘못 잡아</b>
    /// 생긴 어긋남이었다. 참값(상대 80 · 내 152, <c>0x004A9465</c>)으로 고치고 나면
    /// −80 에서 0 이 맞다 — 그래야 상대가 판 왼끝(80−80=0)에서 걸어 나온다.
    /// </remarks>
    public const double WalkEnd = 0;

    /// <summary>다가오기 — 벽에서 열여섯 눈금에 걸어 나온다(−70 → +10).</summary>
    private static Motion WalkIn()
    {
        var steps = new List<Step>();
        for (int tick = 0; tick <= 16; tick++)
            steps.Add(new Step(IdleFrame(tick), Span(1), -(16 - tick) * StepWay + WalkEnd));
        return new Motion(Walk, "다가오기", [.. steps]);
    }

    /// <summary>파일이 없을 때 쓰는 밑값 — 게임 표에서 짚은 그대로다.</summary>
    public static Motion[] Defaults() =>
    [
        WalkIn(),
        Thrust(ThrustKey(0, +1), "상단 공격", 0, +1),
        Thrust(ThrustKey(1, +1), "중단 공격", 3, +1),
        Thrust(ThrustKey(2, +1), "하단 공격", 6, +1),
        Thrust(ThrustKey(0, 0), "상단 맞부딪힘", 0, 0),
        Thrust(ThrustKey(1, 0), "중단 맞부딪힘", 3, 0),
        Thrust(ThrustKey(2, 0), "하단 맞부딪힘", 6, 0),
        Block(GuardKey(0, -1), "뛴다(막기)", 9, -1),
        Block(GuardKey(1, -1), "피한다(막기)", 12, -1),
        Block(GuardKey(2, -1), "웅크린다(막기)", 15, -1),
        Block(GuardKey(0, 0), "뛴다(제자리)", 9, 0),
        Block(GuardKey(1, 0), "피한다(제자리)", 12, 0),
        Block(GuardKey(2, 0), "웅크린다(제자리)", 15, 0),
        Six(Victory, "승리", 18),
        Six(Fall, "쓰러짐", 24),
        new(Idle, "가만히 서기",
        [
            new Step(30, Span(4), 0), new Step(31, Span(1), 0),
            new Step(30, Span(2), 0), new Step(32, Span(1), 0),
        ]),
    ];

    // ── 읽고 쓰기 ───────────────────────────────────────────────────────────

    private static Motion[]? _kept;

    /// <summary>왜 못 읽거나 못 썼는지. 잘 됐으면 빈 문자열.</summary>
    public static string LastError { get; private set; } = "";

    /// <summary>적어 둔 몸짓들. 파일이 있으면 그것, 없으면 밑값이다.</summary>
    public static IReadOnlyList<Motion> All => _kept ??= Read();

    /// <summary>그 이름의 몸짓. 없으면 null.</summary>
    public static Motion? Find(string key) =>
        All.FirstOrDefault(m => string.Equals(m.Key, key, StringComparison.OrdinalIgnoreCase));

    /// <summary>다음에 부를 때 파일을 다시 읽게 한다.</summary>
    public static void Forget() => _kept = null;

    /// <summary>
    /// 적어 두는 파일 자리 — 뽑아 둔 그림 옆이다.
    /// </summary>
    /// <remarks>
    /// <b>소스 옆 <c>asset/duel</c> 을 먼저 찾는다.</b> 앱마다 <c>asset</c> 을 제 출력
    /// 폴더로 복사해 가므로(<c>CdsHelper.csproj</c> · <c>CostaDelSol.Play.csproj</c>) 굽힌
    /// 자리에 적으면 헬퍼와 놀이가 <b>딴 파일</b>을 보게 된다. 그래서 <c>.sln</c> 이 있는
    /// 저장소 뿌리를 거슬러 올라가 찾고, 못 찾으면(내놓은 판이면) 굽힌 자리를 쓴다.
    /// </remarks>
    public static string Path_()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        for (int up = 0; up < 8 && dir != null; up++, dir = dir.Parent)
        {
            string near = System.IO.Path.Combine(dir.FullName, "asset", "duel");
            if (Directory.Exists(near) && dir.EnumerateFiles("*.sln").Any())
                return System.IO.Path.Combine(near, FileName);
        }

        string made = System.IO.Path.Combine(AppContext.BaseDirectory, DuelArt.ArtDirectory);
        return System.IO.Path.Combine(made, FileName);
    }

    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };

    /// <summary>
    /// 파일을 읽는다. 없거나 깨졌으면 밑값으로 물러선다.
    /// </summary>
    /// <remarks>
    /// <b>밑값을 잃지 않는다.</b> 파일에 적힌 것만 갈아 끼우고 없는 몸짓은 밑값을 그대로
    /// 둔다 — 한 몸짓만 손봐 저장해도 나머지가 사라지지 않는다.
    /// </remarks>
    private static Motion[] Read()
    {
        LastError = "";
        var made = Defaults().ToDictionary(m => m.Key, m => m);

        string path = Path_();
        if (!File.Exists(path)) return [.. made.Values];

        try
        {
            var read = JsonSerializer.Deserialize<Motion[]>(File.ReadAllText(path), Json);
            if (read != null)
                foreach (var one in read)
                    if (!string.IsNullOrWhiteSpace(one.Key) && one.Steps is { Length: > 0 })
                        made[one.Key] = one;
        }
        catch (Exception e)
        {
            LastError = $"{path} 를 읽지 못했습니다 — {e.Message}";
        }
        return [.. made.Values];
    }

    /// <summary>지금 표를 파일로 적는다. 잘 됐으면 true.</summary>
    public static bool Save(IEnumerable<Motion> motions)
    {
        LastError = "";
        try
        {
            string path = Path_();
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
            File.WriteAllText(path, JsonSerializer.Serialize(motions.ToArray(), Json));
            _kept = [.. motions];
            return true;
        }
        catch (Exception e)
        {
            LastError = e.Message;
            return false;
        }
    }
}
