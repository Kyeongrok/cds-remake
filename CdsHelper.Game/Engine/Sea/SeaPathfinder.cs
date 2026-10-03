using CdsHelper.Game.Local.Helpers;
using CdsHelper.Support.Local.Helpers;

namespace CdsHelper.Game.Engine.Sea;

/// <summary>
/// 두 칸 사이의 <b>바닷길</b>을 찾는다. 자동항해가 쓸 마디(waypoint) 목록을 낸다.
/// </summary>
/// <remarks>
/// 바람·해류·돛 효율은 이미 <see cref="Sailing"/> 이 매 틱 그대로 셈하므로, 여기서는
/// 그 값을 흉내 낼 까닭이 없다 — 이 표는 오직 <b>뭍을 피해 갈 수 있는 길</b>만 찾는다.
/// 실제로 얼마나 빨리 가는지, 바람이 밀어 주는지 미는지는 항해 중 그때그때
/// <see cref="Sailing"/> 이 정한다 — 자동조타는 이 길을 따라가기만 한다.
///
/// 지형 판정은 <see cref="TerrainTable.CanSail"/> 그대로다(부류 0·1 만 바다).
/// 8방위 격자 A* 로 찾고, 찾은 뒤에는 <b>직선으로 이을 수 있는 자리끼리 건너뛰어</b> 마디
/// 수를 줄인다(string-pulling) — 격자를 한 칸씩 밟은 계단 모양 대신 매끄러운 직선 몇
/// 토막으로 남는다.
/// </remarks>
public static class SeaPathfinder
{
    private const int MapW = WorldMapRenderer.UnfoldedW;   // 2500
    private const int MapH = WorldMapRenderer.CellH;       // 1250
    private const int RawStride = WorldMapRenderer.RawStride;
    private const int CellW = WorldMapRenderer.CellW;

    /// <summary>8방위 걸음과 그 칸 수(대각은 루트2).</summary>
    private static readonly (int Dx, int Dy, double Cost)[] Steps =
    [
        (1, 0, 1), (-1, 0, 1), (0, 1, 1), (0, -1, 1),
        (1, 1, 1.4142135623730951), (1, -1, 1.4142135623730951),
        (-1, 1, 1.4142135623730951), (-1, -1, 1.4142135623730951),
    ];

    /// <summary>둘레를 넓혀 가며 다시 찾아보는 횟수.</summary>
    private const int MaxAttempts = 5;

    /// <summary>둘레 가로가 이보다 넓어지지 않는다 — 지도 폭(2500)의 곱절 남짓이면 충분하다.</summary>
    private const int MaxBoxWidth = 3200;

    /// <summary>
    /// 시작 칸에서 도착 칸까지 바닷길을 찾는다. 둘 다 뭍이면 가까운 물칸으로 민다.
    /// 못 찾으면 null.
    /// </summary>
    /// <param name="land">참이면 <b>뭍길</b>을 찾는다(<see cref="TerrainTable.CanWalk"/> — 뭍 자동이동). 기본은 바닷길이다.</param>
    public static List<(double X, double Y)>? FindRoute(
        byte[] world, TerrainTable terrain, (double X, double Y) start, (double X, double Y) goal, bool land = false)
    {
        bool ok(int x, int y) => Passable(world, terrain, x, y, land);

        int sx = Wrap((int)Math.Floor(start.X));
        int sy = Math.Clamp((int)Math.Floor(start.Y), 0, MapH - 1);
        int gx = Wrap((int)Math.Floor(goal.X));
        int gy = Math.Clamp((int)Math.Floor(goal.Y), 0, MapH - 1);

        if (!ok(sx, sy))
        {
            if (Nearest(ok, sx, sy) is not { } s) return null;
            (sx, sy) = s;
        }
        if (!ok(gx, gy))
        {
            if (Nearest(ok, gx, gy) is not { } g) return null;
            (gx, gy) = g;
        }

        // 가로는 이어져 있다 — 날짜변경선을 넘는 쪽이 더 가까우면 그쪽으로 편다.
        int dx = gx - sx;
        if (dx > MapW / 2) gx -= MapW;
        else if (dx < -MapW / 2) gx += MapW;

        double span = Math.Max(Math.Abs(gx - sx), Math.Abs(gy - sy));
        double margin = Math.Max(80, span * 0.6);

        for (int attempt = 0; attempt < MaxAttempts; attempt++, margin *= 2.2)
        {
            var path = TryAStar(ok, sx, sy, gx, gy, margin);
            if (path != null) return Simplify(ok, path);
        }
        return null;
    }

    private static List<(int X, int Y)>? TryAStar(
        Func<int, int, bool> ok, int sx, int sy, int gx, int gy, double margin)
    {
        int minX = (int)Math.Floor(Math.Min(sx, gx) - margin);
        int maxX = (int)Math.Ceiling(Math.Max(sx, gx) + margin);
        if (maxX - minX > MaxBoxWidth)
        {
            int mid = (minX + maxX) / 2;
            minX = mid - MaxBoxWidth / 2;
            maxX = mid + MaxBoxWidth / 2;
        }
        int minY = Math.Max(0, (int)Math.Floor(Math.Min(sy, gy) - margin));
        int maxY = Math.Min(MapH - 1, (int)Math.Ceiling(Math.Max(sy, gy) + margin));

        int w = maxX - minX + 1, h = maxY - minY + 1;
        int Idx(int x, int y) => (y - minY) * w + (x - minX);
        bool InBox(int x, int y) => x >= minX && x <= maxX && y >= minY && y <= maxY;

        var gScore = new double[w * h];
        Array.Fill(gScore, double.PositiveInfinity);
        var visited = new bool[w * h];
        var cameFrom = new int[w * h];
        Array.Fill(cameFrom, -1);

        int startIdx = Idx(sx, sy), goalIdx = Idx(gx, gy);
        gScore[startIdx] = 0;

        var open = new PriorityQueue<int, double>();
        open.Enqueue(startIdx, Heuristic(sx, sy, gx, gy));

        while (open.Count > 0)
        {
            int cur = open.Dequeue();
            if (visited[cur]) continue;
            visited[cur] = true;
            if (cur == goalIdx) return Reconstruct(cameFrom, cur, minX, minY, w);

            int cx = minX + cur % w, cy = minY + cur / w;
            foreach (var (dx, dy, cost) in Steps)
            {
                int nx = cx + dx, ny = cy + dy;
                if (!InBox(nx, ny)) continue;
                if (!ok(nx, ny)) continue;
                // 대각으로 막힌 모서리를 스치듯 가로지르지 않는다 — 두 이웃이 다 막혔으면 막는다.
                if (dx != 0 && dy != 0 && !ok(cx + dx, cy) && !ok(cx, cy + dy))
                    continue;

                int ni = Idx(nx, ny);
                if (visited[ni]) continue;
                double ng = gScore[cur] + cost;
                if (ng < gScore[ni])
                {
                    gScore[ni] = ng;
                    cameFrom[ni] = cur;
                    open.Enqueue(ni, ng + Heuristic(nx, ny, gx, gy));
                }
            }
        }
        return null;
    }

    /// <summary>팔방 격자에 맞는 어림(옥타일 거리) — 늘 실제 거리 이하라 A* 가 어긋나지 않는다.</summary>
    private static double Heuristic(int x, int y, int gx, int gy)
    {
        double dx = Math.Abs(x - gx), dy = Math.Abs(y - gy);
        return Math.Max(dx, dy) + (Math.Sqrt(2) - 1) * Math.Min(dx, dy);
    }

    private static List<(int X, int Y)> Reconstruct(int[] cameFrom, int idx, int minX, int minY, int w)
    {
        var path = new List<(int X, int Y)>();
        for (int i = idx; i >= 0; i = cameFrom[i])
            path.Add((minX + i % w, minY + i / w));
        path.Reverse();
        return path;
    }

    /// <summary>
    /// 격자 길을 <b>직선으로 이을 수 있는 자리끼리 건너뛰어</b> 줄인다. 이분 탐색으로 가장
    /// 먼 보이는 자리를 찾으므로, 못 찾아도(휘어진 물길이 시야를 가려도) 그보다 못 미친
    /// 자리를 골라 안전은 늘 지킨다.
    /// </summary>
    private static List<(double X, double Y)> Simplify(
        Func<int, int, bool> ok, List<(int X, int Y)> path)
    {
        var outPts = new List<(double X, double Y)> { Center(path[0]) };
        int n = path.Count, i = 0;
        while (i < n - 1)
        {
            int lo = i + 1, hi = n - 1, best = i + 1;
            while (lo <= hi)
            {
                int mid = (lo + hi) / 2;
                if (LineIsOpen(ok, path[i], path[mid])) { best = mid; lo = mid + 1; }
                else hi = mid - 1;
            }
            outPts.Add(Center(path[best]));
            i = best;
        }
        return outPts;
    }

    private static (double X, double Y) Center((int X, int Y) c) => (c.X + 0.5, c.Y + 0.5);

    /// <summary>두 칸을 잇는 직선이 처음부터 끝까지 지나갈 수 있는 칸인지 — 브레젠험으로 훑는다.</summary>
    private static bool LineIsOpen(Func<int, int, bool> ok, (int X, int Y) a, (int X, int Y) b)
    {
        int x0 = a.X, y0 = a.Y, x1 = b.X, y1 = b.Y;
        int dx = Math.Abs(x1 - x0), sx = x0 < x1 ? 1 : -1;
        int dy = -Math.Abs(y1 - y0), sy = y0 < y1 ? 1 : -1;
        int err = dx + dy, x = x0, y = y0;

        while (true)
        {
            if (!ok(x, y)) return false;
            if (x == x1 && y == y1) return true;
            int e2 = 2 * err;
            if (e2 >= dy) { err += dy; x += sx; }
            if (e2 <= dx) { err += dx; y += sy; }
        }
    }

    /// <summary>그 자리에서 가장 가까운 지나갈 수 있는 칸(테두리만 훑는다). 못 찾으면 null.</summary>
    private static (int X, int Y)? Nearest(Func<int, int, bool> ok, int x, int y)
    {
        for (int r = 1; r <= 64; r++)
            for (int dy = -r; dy <= r; dy++)
                for (int dx = -r; dx <= r; dx++)
                {
                    if (Math.Abs(dx) != r && Math.Abs(dy) != r) continue;
                    int ny = y + dy;
                    if (ny < 0 || ny >= MapH) continue;
                    if (ok(x + dx, ny)) return (Wrap(x + dx), ny);
                }
        return null;
    }

    /// <summary>그 칸을 지나갈 수 있는지 — 바다면 바다 칸, 뭍이면 걸을 수 있는 칸이다.</summary>
    private static bool Passable(byte[] world, TerrainTable terrain, int x, int y, bool land)
    {
        if (y < 0 || y >= MapH) return false;
        int cell = CellValue(world, x, y);
        return land ? terrain.CanWalk(cell) : terrain.CanSail(cell);
    }

    /// <summary>
    /// 그 칸의 원본 낱말(두 바이트) — <see cref="Rendering.ShipMapHost"/> 의 <c>RawAt</c>·
    /// <c>CellAt</c> 과 같은 자리 셈이다(짝수 행이 왼쪽 절반, 홀수 행이 오른쪽 절반).
    /// </summary>
    private static int CellValue(byte[] world, int x, int y)
    {
        int cx = Wrap(x);
        bool right = cx >= CellW;
        int col = right ? cx - CellW : cx;
        int row = y * 2 + (right ? 1 : 0);
        int off = row * RawStride + col * 2;
        return world[off] | (world[off + 1] << 8);
    }

    private static int Wrap(int x)
    {
        x %= MapW;
        return x < 0 ? x + MapW : x;
    }
}
