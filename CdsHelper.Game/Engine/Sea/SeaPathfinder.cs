using CdsHelper.Game.Local.Helpers;
using CdsHelper.Support.Local.Helpers;

namespace CdsHelper.Game.Engine.Sea;

/// <summary>
/// 두 칸 사이의 <b>바닷길</b>을 찾는다. 자동항해가 쓸 마디(waypoint) 목록을 낸다.
/// </summary>
/// <remarks>
/// 걸음 값(<see cref="SailCost"/>)을 안 주면 <b>뭍을 피해 가는 가장 짧은 길</b>을 찾는다.
/// 주면 거리 대신 <b>시간</b>으로 고른다 — 바람과 해류 때문에 돌아가는 편이 빠른 구간이 있다.
/// 어느 쪽이든 실제로 얼마나 빨리 가는지는 항해 중 그때그때 <see cref="Sailing"/> 이 정하고,
/// 자동조타는 이 길을 따라가기만 한다.
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
    /// <param name="pace">걸음 값. 주면 가장 <b>빠른</b> 길을 찾는다(바닷길에서만 뜻이 있다).</param>
    public static List<(double X, double Y)>? FindRoute(
        byte[] world, TerrainTable terrain, (double X, double Y) start, (double X, double Y) goal, bool land = false,
        SailCost? pace = null)
    {
        bool ok(int x, int y) => Passable(world, terrain, x, y, land);

        // 걸음 값 — 거리(또는 시간)에 <b>뭍 가까운 칸의 덤</b>을 곱한다. 배는 8방위로만 가고 해류에도
        // 밀려서 그은 길에서 한두 칸은 벗어난다 — 길이 해안선에 붙어 있으면 그대로 뭍에 걸린다.
        byte[]? shore = land ? null : ShoreOf(world, terrain);
        double cost(int x, int y, int k)
        {
            double step = pace == null ? Steps[k].Cost
                : pace.Ticks(Wrap(x), y, k, terrain.ClassOfCell(CellValue(world, x, y)) == 1);
            if (shore == null) return step;
            int ny = y + Steps[k].Dy;
            if (ny < 0 || ny >= MapH) return step;
            return step * ShoreWeight[shore[ny * MapW + Wrap(x + Steps[k].Dx)]];
        }

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

        // 먼저 모서리를 스치지 않는 길을 찾는다. 대각으로만 이어진 물길뿐이라 못 찾으면 그때 스쳐 간다.
        foreach (bool graze in new[] { false, true })
        {
            double reach = margin;
            for (int attempt = 0; attempt < MaxAttempts; attempt++, reach *= 2.2)
            {
                var path = TryAStar(ok, sx, sy, gx, gy, reach, pace, cost, graze);
                if (path != null) return Simplify(ok, path, cost);
            }
        }
        return null;
    }

    /// <summary>
    /// 항구 없는 내륙 도시로 갈 때 <b>배를 댈 자리</b> — 거기서 도시까지 뭍으로 <b>곧게</b> 걸어갈 수 있는
    /// 물칸 가운데 도시에 가장 가까운 것. 없으면 null.
    /// </summary>
    /// <remarks>
    /// 도시에 가장 가까운 물칸으로 가면 안 된다 — 그 물이 배가 못 닿는 딴 바다(호수 · 내해)이거나, 닿아도
    /// 산과 강에 막혀 걸어갈 수 없는 자리일 수 있다. 배가 떠 있는 바다와 <b>이어진</b> 물칸만 보고, 그 칸에서
    /// 도시까지 그은 곧은 선이 걸을 수 있는 뭍으로만 지나는지 본다.
    /// </remarks>
    /// <param name="town">도시가 앉은 칸.</param>
    /// <param name="reach">도시가 차지하는 칸 수.</param>
    public static (double X, double Y)? Landing(byte[] world, TerrainTable terrain, (double X, double Y) ship,
                                               (int X, int Y) town, int reach, int radius = 200)
    {
        bool sea(int x, int y) => Passable(world, terrain, x, y, false);
        bool walk(int x, int y) => Passable(world, terrain, x, y, true);

        int sx = Wrap((int)Math.Floor(ship.X)), sy = Math.Clamp((int)Math.Floor(ship.Y), 0, MapH - 1);
        if (!sea(sx, sy))
        {
            if (Nearest(sea, sx, sy) is not { } s) return null;
            (sx, sy) = s;
        }

        // 배가 떠 있는 바다 — 네 이웃으로 번져 가며 칠한다.
        var mine = new bool[MapW * MapH];
        var todo = new Queue<int>();
        mine[sy * MapW + sx] = true;
        todo.Enqueue(sy * MapW + sx);
        while (todo.Count > 0)
        {
            int at = todo.Dequeue(), x = at % MapW, y = at / MapW;
            foreach (var (dx, dy) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
            {
                int nx = Wrap(x + dx), ny = y + dy;
                if (ny < 0 || ny >= MapH || mine[ny * MapW + nx] || !sea(nx, ny)) continue;
                mine[ny * MapW + nx] = true;
                todo.Enqueue(ny * MapW + nx);
            }
        }

        double midX = town.X + reach / 2.0, midY = town.Y + reach / 2.0;
        bool inTown(int x, int y) => x >= town.X - 1 && x <= town.X + reach && y >= town.Y - 1 && y <= town.Y + reach;

        var shore = new List<(double Far, int X, int Y)>();
        for (int y = Math.Max(0, town.Y - radius); y <= Math.Min(MapH - 1, town.Y + radius); y++)
            for (int x = town.X - radius; x <= town.X + radius; x++)
            {
                if (!mine[y * MapW + Wrap(x)]) continue;
                if (!walk(x + 1, y) && !walk(x - 1, y) && !walk(x, y + 1) && !walk(x, y - 1)) continue;
                double fx = x + 0.5 - midX, fy = y + 0.5 - midY;
                shore.Add((fx * fx + fy * fy, x, y));
            }
        shore.Sort((a, b) => a.Far.CompareTo(b.Far));

        foreach (var (far, x, y) in shore)
        {
            double len = Math.Sqrt(far);
            int n = Math.Max(1, (int)Math.Ceiling(len * 4));
            bool open = true;
            for (int i = 1; i <= n && open; i++)
            {
                int px = (int)Math.Floor(x + 0.5 + (midX - x - 0.5) * i / n);
                int py = (int)Math.Floor(y + 0.5 + (midY - y - 0.5) * i / n);
                if (px == x && py == y) continue;      // 아직 배 댄 칸이다
                if (inTown(px, py)) break;             // 도시에 닿았다
                open = walk(px, py);
            }
            if (open) return (Wrap(x) + 0.5, y + 0.5);
        }
        return null;
    }

    /// <summary>
    /// 항구 없는 내륙 도시로 갈 때 <b>배를 댈 자리</b> — 거기까지 <b>배로 가는 시간과 거기서 도시까지 걸어가는
    /// 시간을 더한 것</b>이 가장 짧은 해안의 물칸. 없으면 null.
    /// </summary>
    /// <remarks>
    /// 도시에 가장 가까운 해안이 답이 아니다 — 리스본에서 이스파한으로 가는데 페르시아만에 대면 걸을 길은 짧아도
    /// 아프리카를 돌아야 한다. 지중해 동쪽 끝에 대고 더 걷는 편이 훨씬 빠르다.
    ///
    /// 배가 선 자리에서 바다를 시간 순으로 훑어 나가며(A*), 닿은 해안 칸마다 「여기까지 온 시간 + 여기서 도시까지
    /// 곧게 걸어가는 시간」을 견준다. 앞으로 닿을 어느 칸도 지금 답보다 빠를 수 없게 되면 멎는다. 걸어갈 선은
    /// 걸을 수 있는 뭍으로만 지나야 한다.
    /// </remarks>
    /// <param name="town">도시가 앉은 칸.</param>
    /// <param name="reach">도시가 차지하는 칸 수.</param>
    /// <param name="pace">바다 걸음 값(틱).</param>
    /// <param name="landStep">뭍에서 한 틱에 가는 칸 수.</param>
    /// <param name="radius">도시에서 이만큼(칸) 안의 해안만 본다.</param>
    /// <param name="walkWeight">걷는 시간에 곱하는 값 — 1 보다 크면 덜 걷는 자리를 고른다(어림은 그대로라 조금 더 훑는다).</param>
    public static (double X, double Y)? LandingByTime(byte[] world, TerrainTable terrain, (double X, double Y) ship,
                                                     (int X, int Y) town, int reach, SailCost pace, double landStep,
                                                     int radius = 260, double walkWeight = 1)
    {
        bool sea(int x, int y) => Passable(world, terrain, x, y, false);
        bool walk(int x, int y) => Passable(world, terrain, x, y, true);
        if (landStep <= 0) return null;

        int sx = Wrap((int)Math.Floor(ship.X)), sy = Math.Clamp((int)Math.Floor(ship.Y), 0, MapH - 1);
        if (!sea(sx, sy))
        {
            if (Nearest(sea, sx, sy) is not { } s) return null;
            (sx, sy) = s;
        }

        double midX = town.X + reach / 2.0, midY = town.Y + reach / 2.0;
        bool inTown(int x, int y) => x >= town.X - 1 && x <= town.X + reach && y >= town.Y - 1 && y <= town.Y + reach;
        static double Across(double dx) => dx > MapW / 2 ? dx - MapW : dx < -MapW / 2 ? dx + MapW : dx;

        // 어림 — 도시까지 곧은 거리를 바다 · 뭍 가운데 빠른 걸음으로 간 셈. 가로는 배와 도시 줄 가운데 큰 경도 보정으로 줄인다.
        double widest = Math.Max(pace.LonOf(sy), pace.LonOf((int)midY));
        double fastest = Math.Max(pace.Fastest, landStep);
        double Guess(int x, int y)
        {
            double ex = Across(x + 0.5 - midX) / widest, ey = y + 0.5 - midY;
            return Math.Sqrt(ex * ex + ey * ey) / fastest;
        }

        // 그 해안 칸에서 도시까지 걸어가는 틱 수. 곧은 선이 막히면 무한대다.
        double Afoot(int x, int y)
        {
            double wx = Across(midX - x - 0.5), wy = midY - y - 0.5;
            if (Math.Abs(wx) > radius || Math.Abs(wy) > radius) return double.PositiveInfinity;
            if (!walk(x + 1, y) && !walk(x - 1, y) && !walk(x, y + 1) && !walk(x, y - 1)) return double.PositiveInfinity;

            int n = Math.Max(1, (int)Math.Ceiling(Math.Sqrt(wx * wx + wy * wy) * 4));
            for (int i = 1; i <= n; i++)
            {
                int px = (int)Math.Floor(x + 0.5 + wx * i / n), py = (int)Math.Floor(y + 0.5 + wy * i / n);
                if (px == x && py == y) continue;          // 아직 배 댄 칸이다
                if (inTown(Wrap(px), py)) break;           // 도시에 닿았다
                if (!walk(px, py)) return double.PositiveInfinity;
            }
            // 말도 8방위로 걷는다 — 곧은 거리보다 조금 더 든다.
            double lon = pace.LonOf((int)((y + midY) / 2));
            double sxl = wx / lon;
            return Math.Sqrt(sxl * sxl + wy * wy) * 1.05 / landStep * walkWeight;
        }

        var spent = new double[MapW * MapH];
        Array.Fill(spent, double.PositiveInfinity);
        var done = new bool[MapW * MapH];
        var open = new PriorityQueue<int, double>();
        spent[sy * MapW + sx] = 0;
        open.Enqueue(sy * MapW + sx, Guess(sx, sy));

        double bestTotal = double.PositiveInfinity;
        (double X, double Y)? best = null;
        while (open.TryDequeue(out int cur, out double lowest))
        {
            if (lowest >= bestTotal) break;    // 남은 어느 칸도 지금 답보다 빠를 수 없다
            if (done[cur]) continue;
            done[cur] = true;
            int cx = cur % MapW, cy = cur / MapW;

            double total = spent[cur] + Afoot(cx, cy);
            if (total < bestTotal) { bestTotal = total; best = (cx + 0.5, cy + 0.5); }

            bool deep = terrain.ClassOfCell(CellValue(world, cx, cy)) == 1;
            for (int k = 0; k < Steps.Length; k++)
            {
                var (dx, dy, _) = Steps[k];
                int nx = Wrap(cx + dx), ny = cy + dy;
                if (!sea(nx, ny)) continue;
                if (dx != 0 && dy != 0 && (!sea(cx + dx, cy) || !sea(cx, cy + dy))) continue;
                int ni = ny * MapW + nx;
                if (done[ni]) continue;
                double ng = spent[cur] + pace.Ticks(cx, cy, k, deep);
                if (ng >= spent[ni]) continue;
                spent[ni] = ng;
                open.Enqueue(ni, ng + Guess(nx, ny));
            }
        }
        return best;
    }

    /// <summary>
    /// 길의 토막을 <b>8방위로 곧게 뻗는 토막</b>으로 바꾼다(최소 조타) — 비스듬한 토막 하나를 대각 한 번과
    /// 곧은 한 번으로 꺾는다.
    /// </summary>
    /// <remarks>
    /// 뱃머리가 8방위뿐이라 비스듬한 선은 두 방위를 번갈아 틀며 따라간다. 같은 두 방위를 한 번씩 몰아서
    /// 가면 간 거리는 같고 뱃머리는 한 번만 튼다. 꺾은 길이 뭍에 걸리면 반으로 나눠 다시 꺾어 보고
    /// (<see cref="DoglegDepth"/> 번까지), 그래도 안 되면 그 토막은 그대로 둔다.
    /// </remarks>
    public static List<(double X, double Y)> Dogleg(byte[] world, TerrainTable terrain, IReadOnlyList<(double X, double Y)> route)
    {
        bool ok(int x, int y) => Passable(world, terrain, x, y, false);
        var legs = new List<(double X, double Y)>();
        if (route.Count == 0) return legs;

        var from = ((int)Math.Floor(route[0].X), (int)Math.Floor(route[0].Y));
        legs.Add(Center(from));
        for (int i = 1; i < route.Count; i++)
        {
            var to = ((int)Math.Floor(route[i].X), (int)Math.Floor(route[i].Y));
            Bend(ok, from, to, DoglegDepth, legs);
            from = to;
        }
        return legs;
    }

    private const int DoglegDepth = 3;

    /// <summary><paramref name="a"/> 다음부터 <paramref name="b"/> 까지의 마디를 <paramref name="legs"/> 에 싣는다.</summary>
    private static void Bend(Func<int, int, bool> ok, (int X, int Y) a, (int X, int Y) b, int depth,
                             List<(double X, double Y)> legs)
    {
        int dx = b.X - a.X, dy = b.Y - a.Y;
        int slant = Math.Min(Math.Abs(dx), Math.Abs(dy));
        if (slant == 0 || Math.Abs(dx) == Math.Abs(dy)) { legs.Add(Center(b)); return; }   // 이미 8방위다

        int sx = Math.Sign(dx) * slant, sy = Math.Sign(dy) * slant;
        // 대각 먼저, 안 되면 곧은 쪽 먼저.
        foreach (var knee in new[] { (X: a.X + sx, Y: a.Y + sy), (X: b.X - sx, Y: b.Y - sy) })
            if (LineIsOpen(ok, a, knee) && LineIsOpen(ok, knee, b))
            {
                legs.Add(Center(knee));
                legs.Add(Center(b));
                return;
            }

        var half = (X: a.X + dx / 2, Y: a.Y + dy / 2);
        if (depth > 0 && half != a && half != b && ok(half.X, half.Y))
        {
            Bend(ok, a, half, depth - 1, legs);
            Bend(ok, half, b, depth - 1, legs);
            return;
        }
        legs.Add(Center(b));
    }

    /// <summary>뭍까지의 칸 수(0 = 뭍, 1 = 뭍에 닿은 물, … <see cref="ShoreFar"/> = 그보다 멀다)마다 걸음 값에 곱하는 덤.</summary>
    private static readonly double[] ShoreWeight = [1, 3, 1.8, 1.25, 1];

    private const int ShoreFar = 4;

    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<byte[], byte[]> Shores = new();

    /// <summary>칸마다 뭍까지의 칸 수(<see cref="ShoreFar"/> 에서 자른다). 지도마다 한 번만 센다.</summary>
    private static byte[] ShoreOf(byte[] world, TerrainTable terrain) => Shores.GetValue(world, w =>
    {
        var near = new byte[MapW * MapH];
        for (int y = 0; y < MapH; y++)
            for (int x = 0; x < MapW; x++)
                near[y * MapW + x] = Passable(w, terrain, x, y, false) ? (byte)ShoreFar : (byte)0;

        // 한 겹씩 번져 나간다 — 둘레 여덟 칸에 한 겹 안쪽이 있으면 그 다음 겹이다. 지도 위아래 끝은 뭍으로 친다.
        for (int d = 1; d < ShoreFar; d++)
            for (int y = 0; y < MapH; y++)
                for (int x = 0; x < MapW; x++)
                {
                    if (near[y * MapW + x] != ShoreFar) continue;
                    bool touches = d == 1 && (y == 0 || y == MapH - 1);
                    for (int dy = -1; dy <= 1 && !touches; dy++)
                    {
                        int ny = y + dy;
                        if (ny < 0 || ny >= MapH) continue;
                        for (int dx = -1; dx <= 1; dx++)
                            if (near[ny * MapW + Wrap(x + dx)] == d - 1) { touches = true; break; }
                    }
                    if (touches) near[y * MapW + x] = (byte)d;
                }
        return near;
    });

    /// <summary>그 길을 다 가는 데 드는 틱 수(어림) — 짧은 길과 빠른 길을 견줄 때 쓴다.</summary>
    public static double RouteTicks(byte[] world, TerrainTable terrain, IReadOnlyList<(double X, double Y)> route, SailCost pace)
    {
        double ticks(int x, int y, int k) => pace.Ticks(Wrap(x), y, k, terrain.ClassOfCell(CellValue(world, x, y)) == 1);
        double sum = 0;
        for (int i = 1; i < route.Count; i++)
            sum += LineTicks(ticks, ((int)Math.Floor(route[i - 1].X), (int)Math.Floor(route[i - 1].Y)),
                             ((int)Math.Floor(route[i].X), (int)Math.Floor(route[i].Y)));
        return sum;
    }

    /// <summary>
    /// 두 칸을 곧게 잇는 데 드는 틱 수(어림).
    /// </summary>
    /// <remarks>
    /// 뱃머리는 8방위뿐이라 비스듬한 직선은 <b>대각 걸음과 곧은 걸음을 섞어</b> 간다. 직선 위의 칸마다
    /// 그 두 걸음의 값을 섞인 몫만큼 더한다.
    /// </remarks>
    private static double LineTicks(Func<int, int, int, double> ticks, (int X, int Y) a, (int X, int Y) b)
    {
        int dx = b.X - a.X, dy = b.Y - a.Y;
        int ax = Math.Abs(dx), ay = Math.Abs(dy);
        int n = Math.Max(ax, ay);
        if (n == 0) return 0;

        int diagonal = SailCost.StepOf(Math.Sign(dx), Math.Sign(dy));
        int straight = ax >= ay ? SailCost.StepOf(Math.Sign(dx), 0) : SailCost.StepOf(0, Math.Sign(dy));
        double slanted = Math.Min(ax, ay) / (double)n;

        double sum = 0;
        for (int i = 0; i < n; i++)
        {
            int x = a.X + (int)Math.Round(dx * (double)i / n);
            int y = Math.Clamp(a.Y + (int)Math.Round(dy * (double)i / n), 0, MapH - 1);
            if (slanted > 0) sum += slanted * ticks(x, y, diagonal);
            if (slanted < 1) sum += (1 - slanted) * ticks(x, y, straight);
        }
        return sum;
    }

    private static List<(int X, int Y)>? TryAStar(
        Func<int, int, bool> ok, int sx, int sy, int gx, int gy, double margin,
        SailCost? pace, Func<int, int, int, double> cost, bool graze)
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

        // 시간으로 찾을 때의 어림 — 가로를 이 둘레에서 가장 큰 경도 보정으로 줄인 곧은 거리를 가장 빠른
        // 걸음으로 간 셈이다. 늘 실제보다 작거나 같다.
        double widest = pace == null ? 1 : Math.Max(pace.LonOf(minY), pace.LonOf(maxY));
        double Guess(int x, int y)
        {
            if (pace == null) return Heuristic(x, y, gx, gy);
            double ex = (x - gx) / widest, ey = y - gy;
            return Math.Sqrt(ex * ex + ey * ey) / pace.Fastest;
        }

        var open = new PriorityQueue<int, double>();
        open.Enqueue(startIdx, Guess(sx, sy));

        while (open.Count > 0)
        {
            int cur = open.Dequeue();
            if (visited[cur]) continue;
            visited[cur] = true;
            if (cur == goalIdx) return Reconstruct(cameFrom, cur, minX, minY, w);

            int cx = minX + cur % w, cy = minY + cur / w;
            for (int k = 0; k < Steps.Length; k++)
            {
                var (dx, dy, _) = Steps[k];
                int nx = cx + dx, ny = cy + dy;
                if (!InBox(nx, ny)) continue;
                if (!ok(nx, ny)) continue;
                // 대각으로 막힌 모서리를 스치듯 가로지르지 않는다 — 배는 칸 한가운데로만 다니지 않아서
                // 모서리에 걸린다. 스쳐도 되는 때(graze)에도 두 이웃이 다 막혔으면 막는다.
                if (dx != 0 && dy != 0)
                {
                    bool side = ok(cx + dx, cy), up = ok(cx, cy + dy);
                    if (graze ? !side && !up : !side || !up) continue;
                }

                int ni = Idx(nx, ny);
                if (visited[ni]) continue;
                double ng = gScore[cur] + cost(cx, cy, k);
                if (ng < gScore[ni])
                {
                    gScore[ni] = ng;
                    cameFrom[ni] = cur;
                    open.Enqueue(ni, ng + Guess(nx, ny));
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
    /// <remarks>
    /// <b>곧게 펴도 값이 늘지 않을 때만</b> 편다 — 뭍을 멀찍이 돌거나 바람을 피해 돌아간 자리를 직선으로
    /// 이으면 찾은 보람이 없다. 탁 트인 바다의 계단 모양은 펴도 값이 같아 그대로 펴진다.
    /// </remarks>
    private static List<(double X, double Y)> Simplify(
        Func<int, int, bool> ok, List<(int X, int Y)> path, Func<int, int, int, double> ticks)
    {
        var outPts = new List<(double X, double Y)> { Center(path[0]) };
        int n = path.Count, i = 0;

        // 길을 따라 쌓인 틱 수 — 직선으로 이은 값과 견준다.
        var spent = new double[n];
        for (int j = 1; j < n; j++)
        {
            int k = SailCost.StepOf(path[j].X - path[j - 1].X, path[j].Y - path[j - 1].Y);
            spent[j] = spent[j - 1] + (k < 0 ? 0 : ticks(path[j - 1].X, path[j - 1].Y, k));
        }
        bool Straightens(int from, int to) =>
            LineIsOpen(ok, path[from], path[to])
            && (to == from + 1
                || LineTicks(ticks, path[from], path[to]) <= (spent[to] - spent[from]) * StraightenSlack);

        while (i < n - 1)
        {
            int lo = i + 1, hi = n - 1, best = i + 1;
            while (lo <= hi)
            {
                int mid = (lo + hi) / 2;
                if (Straightens(i, mid)) { best = mid; lo = mid + 1; }
                else hi = mid - 1;
            }
            outPts.Add(Center(path[best]));
            i = best;
        }
        return outPts;
    }

    /// <summary>곧게 편 길이 이만큼까지는 더 걸려도 편다 — 어림끼리의 견줌이라 조금 봐준다.</summary>
    private const double StraightenSlack = 1.02;

    private static (double X, double Y) Center((int X, int Y) c) => (c.X + 0.5, c.Y + 0.5);

    /// <summary>직선 양옆으로 이만큼(칸)까지 열려 있어야 그 직선으로 잇는다.</summary>
    private const double LineHalfWidth = 0.45;

    /// <summary>
    /// 두 칸의 한가운데를 잇는 직선이 처음부터 끝까지 지나갈 수 있는지 — <b>폭이 있는 띠</b>로 훑는다.
    /// </summary>
    /// <remarks>
    /// 칸만 골라 훑으면(브레젠험) 직선이 뭍 칸의 모서리를 스쳐도 열린 것으로 친다 — 배는 그 직선을
    /// 반 칸쯤 벗어나 다니므로 그 모서리에 걸려 선다. 직선과 그 양옆(<see cref="LineHalfWidth"/>)을
    /// 사분 칸마다 짚는다.
    /// </remarks>
    private static bool LineIsOpen(Func<int, int, bool> ok, (int X, int Y) a, (int X, int Y) b)
    {
        double ax = a.X + 0.5, ay = a.Y + 0.5, dx = b.X - a.X, dy = b.Y - a.Y;
        double len = Math.Sqrt(dx * dx + dy * dy);
        if (len == 0) return ok(a.X, a.Y);

        double px = -dy / len * LineHalfWidth, py = dx / len * LineHalfWidth;
        int n = (int)Math.Ceiling(len * 4);
        for (int i = 0; i <= n; i++)
        {
            double x = ax + dx * i / n, y = ay + dy * i / n;
            if (!ok((int)Math.Floor(x), (int)Math.Floor(y))
                || !ok((int)Math.Floor(x + px), (int)Math.Floor(y + py))
                || !ok((int)Math.Floor(x - px), (int)Math.Floor(y - py)))
                return false;
        }
        return true;
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
