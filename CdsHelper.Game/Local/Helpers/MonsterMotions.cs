using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CdsHelper.Game.Local.Helpers;

/// <summary>
/// 해전 괴물 몸짓 표 — 괴물마다 어느 장을 몇 초 동안, 얼마나 앞으로 내밀어 낼지. 일기토 몸짓 표(<see cref="DuelMotions"/>)와 같은 꼴이다.
/// </summary>
/// <remarks>
/// <b>괴물 관리 창(<see cref="UI.Views.MonsterMotionDialog"/>)과 해전 판(<see cref="UI.Views.SeaCombatDialog"/>)이 같이 보는 표다.</b>
/// 고쳐 저장하면 <c>asset/scombat/monster-motion.json</c> 이 되고, 해전이 그 파일을 읽어 돈다. 파일이 없으면 밑값을 쓴다.
///
/// 괴물 그림은 벌마다 방향 여섯 x 다섯 장이다(<c>방향*5 + 장</c>, <see cref="CombatArt.Monster"/>). 장 번호는 <b>그 방향 안의 0~4</b>로
/// 적어 여섯 방향에 같이 쓴다. 밑값은 원본 그대로다.
/// <code>
///   떠 있음  3 · 4 장을 16 눈(0.05초 x 16 = 0.8초)마다 번갈아   0x00440760 의 ([+0x8F0] % 32) / 16
///   덮침     0 · 1 · 2 장을 한 장씩                              0x00438108 ~ 0x00438162
/// </code>
/// 밀기는 그 괴물이 보는 쪽으로 내미는 점이다 — 원본은 안 내민다(0).
/// </remarks>
public static class MonsterMotions
{
    /// <summary>몸짓 이름 — 떠 있을 때 · 덮칠 때.</summary>
    public const string Idle = "idle", Strike = "strike";

    /// <summary>방향 하나의 장 수.</summary>
    public const int FramesPerWay = 5;

    /// <summary>괴물 그림 벌 이름 — 벌 번호 차례(<see cref="Engine.Sea.SeaBattle.MonsterStats"/> 의 Art).</summary>
    public static readonly string[] KindNames = ["식인 상어", "크라켄", "시서펜트", "맨터"];

    /// <summary>차례 한 자리.</summary>
    /// <param name="Frame">방향 안의 장 번호(0~4).</param>
    /// <param name="Seconds">그 장이 머무는 시간.</param>
    /// <param name="Push">그동안 보는 쪽으로 내밀어 있는 거리(점). 물러나면 음수다.</param>
    public sealed record Step(int Frame, double Seconds, double Push);

    /// <summary>몸짓 하나.</summary>
    public sealed record Motion(string Key, string Name, Step[] Steps)
    {
        /// <summary>다 도는 데 걸리는 시간.</summary>
        public double Length => Steps.Sum(s => Math.Max(0.01, s.Seconds));

        /// <summary>시작에서 <paramref name="seconds"/> 지났을 때의 자리 — 끝나면 처음부터 다시 돈다.</summary>
        public Step AtLooped(double seconds)
        {
            if (Steps.Length == 0) return new Step(3, 1, 0);
            double t = Length <= 0 ? 0 : seconds % Length, at = 0;
            foreach (var step in Steps)
            {
                at += Math.Max(0.01, step.Seconds);
                if (t < at) return step;
            }
            return Steps[^1];
        }
    }

    /// <summary>괴물 한 벌의 몸짓들.</summary>
    public sealed record Set(int Kind, Motion[] Motions);

    /// <summary>밑값 — 원본 그대로다.</summary>
    public static Motion[] Defaults() =>
    [
        new(Idle, "떠 있음", [new Step(3, 0.8, 0), new Step(4, 0.8, 0)]),
        new(Strike, "덮침", [new Step(0, 0.06, 0), new Step(1, 0.06, 0), new Step(2, 0.06, 0)]),
    ];

    // ── 읽고 쓰기 ───────────────────────────────────────────────────────────

    public const string FileName = "monster-motion.json";

    private static Set[]? _kept;

    public static string LastError { get; private set; } = "";

    /// <summary>괴물 넷의 몸짓 — 적어 둔 것, 없으면 밑값.</summary>
    public static IReadOnlyList<Set> All => _kept ??= Read();

    /// <summary>그 괴물의 그 몸짓. 적어 둔 것이 없으면 밑값.</summary>
    public static Motion Find(int kind, string key) =>
        All.FirstOrDefault(s => s.Kind == kind)?.Motions.FirstOrDefault(m => m.Key == key)
        ?? Defaults().First(m => m.Key == key);

    /// <summary>다시 읽게 한다 — 관리 창에서 저장한 뒤.</summary>
    public static void Forget() => _kept = null;

    /// <summary>
    /// 표 파일 자리 — 소스 옆(개발 중, <c>.sln</c> 이 있는 폴더의 <c>asset/scombat</c>)이 먼저고, 아니면 앱 폴더의 그림 자리다.
    /// </summary>
    public static string Path_()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        for (int up = 0; up < 8 && dir != null; up++, dir = dir.Parent)
        {
            string near = Path.Combine(dir.FullName, "asset", "scombat");
            if (Directory.Exists(near) && dir.EnumerateFiles("*.sln").Any())
                return Path.Combine(near, FileName);
        }
        return Path.Combine(AppContext.BaseDirectory, CombatArt.ArtDirectory, FileName);
    }

    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };

    private static Set[] Read()
    {
        LastError = "";
        var sets = Enumerable.Range(0, KindNames.Length).Select(k => new Set(k, Defaults())).ToArray();
        string path = Path_();
        if (!File.Exists(path)) return sets;
        try
        {
            foreach (var read in JsonSerializer.Deserialize<Set[]>(File.ReadAllText(path), Json) ?? [])
            {
                if (read.Kind < 0 || read.Kind >= sets.Length) continue;
                // 적어 둔 몸짓만 갈아 끼운다 — 모자란 것은 밑값이 남는다. 장 번호는 0~4 로 자른다.
                var motions = Defaults().Select(d => read.Motions?.FirstOrDefault(m => m.Key == d.Key) is { Steps.Length: > 0 } m
                    ? d with { Steps = [.. m.Steps.Select(s => s with { Frame = Math.Clamp(s.Frame, 0, FramesPerWay - 1) })] }
                    : d).ToArray();
                sets[read.Kind] = new Set(read.Kind, motions);
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            LastError = $"{path} 을 읽지 못했습니다 — {ex.Message}";
        }
        return sets;
    }

    /// <summary>넷을 통째로 적는다. 못 적으면 까닭, 적었으면 null.</summary>
    public static string? Save(IEnumerable<Set> sets)
    {
        string path = Path_();
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, JsonSerializer.Serialize(sets.ToArray(), Json));
            _kept = null;
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return ex.Message;
        }
    }
}
