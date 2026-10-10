using System.IO;
using System.Text.Json;

namespace CdsHelper.Game.Engine.Sea;

/// <summary>
/// 한 번 찾은 바닷길을 적어 두는 곳 — 같은 길을 다시 물으면 찾지 않고 꺼내 쓴다.
/// </summary>
/// <remarks>
/// 게임에는 없는 것이다. 먼 도시까지의 길찾기는 한 번에 반 초쯤 걸리는데, 네비게이션은 후보를 여럿 보여 주고
/// 고른 뒤 또 잡고, 길이 막히면 다시 잡는다 — 그때마다 처음부터 찾으면 굼떠 보인다.
///
/// 꺼내 쓰는 길이 둘이다.
/// <list type="bullet">
/// <item><b>같은 길</b> — 떠나는 칸 · 닿는 칸 · 걸음 값(<see cref="SailCost.Signature"/>)이 같으면 그대로 준다.</item>
/// <item><b>이어 붙이기</b> — 같은 데로 가는 길이 배 가까이를 지나면 그 길에 올라타는 데까지만 새로 찾는다
/// (<see cref="Near"/>). 도시 안에서 떠나는 자리가 몇 칸 달라졌을 때, 가다가 멈췄다 다시 갈 때, 막혀서 다시 짤 때다.</item>
/// </list>
/// 지도가 바뀌면(모드 「수에즈 운하」) 적어 둔 길이 안 맞는다 — 지도 지문(<see cref="Open"/>)이 다르면 다 버린다.
/// 파일은 <c>%AppData%\CdsHelper\route-cache.json</c> 이라 게임을 껐다 켜도 남는다.
/// </remarks>
public sealed class RouteCache
{
    /// <summary>적어 두는 길의 수. 넘치면 오래된 것부터 버린다.</summary>
    private const int Capacity = 400;

    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CdsHelper", "route-cache.json");

    /// <summary>적어 둔 길 하나.</summary>
    /// <param name="Pace">걸음 값의 지문. 가장 짧은 길(걸음 값 없음)은 0.</param>
    /// <param name="Whole">처음부터 끝까지 새로 찾은 길인지 — 이어 붙인 길에는 또 이어 붙이지 않는다(차츰 구부러진다).</param>
    /// <param name="Points">마디 — x, y 가 번갈아 든다.</param>
    public sealed record Entry(int Sx, int Sy, int Gx, int Gy, long Pace, bool Whole, double[] Points);

    private sealed record Snapshot(long World, List<Entry> Routes);

    private readonly List<Entry> _routes = [];
    private long _world;
    private bool _dirty;

    /// <summary>
    /// 그 지도의 길을 연다. 파일에 적힌 지문이 같으면 적어 둔 길을 읽고, 다르면 빈 채로 시작한다.
    /// </summary>
    public void Open(byte[] world)
    {
        _routes.Clear();
        _world = Fingerprint(world);
        _dirty = false;
        try
        {
            if (!File.Exists(FilePath)) return;
            var saved = JsonSerializer.Deserialize<Snapshot>(File.ReadAllText(FilePath));
            if (saved == null || saved.World != _world) return;
            _routes.AddRange(saved.Routes.Where(r => r.Points.Length >= 4 && r.Points.Length % 2 == 0));
        }
        catch { /* 못 읽으면 빈 채로 — 다시 찾으면 된다 */ }
    }

    /// <summary>지도 지문 — 바이트를 띄엄띄엄 훑은 FNV-1a. 운하를 내면 달라진다.</summary>
    private static long Fingerprint(byte[] world)
    {
        ulong hash = 14695981039346656037UL;
        for (int i = 0; i < world.Length; i += 3)
        {
            hash ^= world[i];
            hash *= 1099511628211UL;
        }
        return unchecked((long)hash);
    }

    /// <summary>같은 길을 적어 둔 것이 있으면 준다.</summary>
    public List<(double X, double Y)>? Find(int sx, int sy, int gx, int gy, long pace)
    {
        foreach (var r in _routes)
            if (r.Sx == sx && r.Sy == sy && r.Gx == gx && r.Gy == gy && r.Pace == pace) return Unpack(r.Points);
        return null;
    }

    /// <summary>
    /// 같은 데로 가는 길 가운데 그 자리에 가장 가까이 지나는 것 — 그 길과, 올라탈 마디의 차례. 없으면 null.
    /// </summary>
    /// <param name="reach">이만큼(칸) 안을 지나야 쓴다.</param>
    public (List<(double X, double Y)> Route, int Join)? Near(double x, double y, int gx, int gy, long pace, double reach)
    {
        List<(double X, double Y)>? best = null;
        int join = 0;
        double near = reach * reach;
        foreach (var r in _routes)
        {
            if (!r.Whole || r.Gx != gx || r.Gy != gy || r.Pace != pace) continue;
            var pts = r.Points;
            for (int i = 0; i + 3 < pts.Length; i += 2)
            {
                // 토막 위 가장 가까운 자리까지.
                double ax = pts[i], ay = pts[i + 1], bx = pts[i + 2], by = pts[i + 3];
                double ux = bx - ax, uy = by - ay, len2 = ux * ux + uy * uy;
                double t = len2 > 0 ? Math.Clamp(((x - ax) * ux + (y - ay) * uy) / len2, 0, 1) : 0;
                double dx = ax + ux * t - x, dy = ay + uy * t - y, far = dx * dx + dy * dy;
                if (far >= near) continue;
                near = far;
                best = Unpack(pts);
                join = i / 2 + 1;   // 그 토막의 끝 마디 — 뒤로 돌아가지 않는다
            }
        }
        return best == null ? null : (best, join);
    }

    /// <summary>길을 적어 둔다. 같은 열쇠가 있으면 갈아 끼운다.</summary>
    public void Add(int sx, int sy, int gx, int gy, long pace, bool whole, IReadOnlyList<(double X, double Y)> route)
    {
        if (route.Count < 2) return;
        _routes.RemoveAll(r => r.Sx == sx && r.Sy == sy && r.Gx == gx && r.Gy == gy && r.Pace == pace);

        var points = new double[route.Count * 2];
        for (int i = 0; i < route.Count; i++) { points[i * 2] = route[i].X; points[i * 2 + 1] = route[i].Y; }
        _routes.Add(new Entry(sx, sy, gx, gy, pace, whole, points));
        if (_routes.Count > Capacity) _routes.RemoveRange(0, _routes.Count - Capacity);
        _dirty = true;
    }

    /// <summary>바뀐 것이 있으면 파일에 적는다.</summary>
    public void Save()
    {
        if (!_dirty) return;
        _dirty = false;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(new Snapshot(_world, _routes)));
        }
        catch { /* 못 적어도 놀이는 그대로다 */ }
    }

    private static List<(double X, double Y)> Unpack(double[] points)
    {
        var route = new List<(double X, double Y)>(points.Length / 2);
        for (int i = 0; i + 1 < points.Length; i += 2) route.Add((points[i], points[i + 1]));
        return route;
    }
}
