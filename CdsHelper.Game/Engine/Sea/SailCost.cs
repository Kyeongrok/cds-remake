using CdsHelper.Game.Local.Helpers;
using CdsHelper.Support.Local.Helpers;

namespace CdsHelper.Game.Engine.Sea;

/// <summary>
/// 바닷길 찾기가 쓰는 <b>걸음 값</b> — 그 칸을 그 쪽으로 한 칸 지나는 데 드는 틱 수.
/// </summary>
/// <remarks>
/// 게임에는 없는 것이다. 바람과 해류 때문에 돌아가는 편이 빠른 구간이 있어, 자동항해가 거리 대신
/// <b>시간</b>으로 길을 고르게 하려고 둔다. 셈은 배가 실제로 움직이는 것(<c>ShipMapHost.Push</c>)과 같은
/// 식을 쓴다 — 함대 속도(<see cref="Sailing.SpeedOf"/>) → 칸(<see cref="Sailing.CellsPerTick"/>) + 해류
/// (<see cref="Sailing.Drift"/>), 가로는 경도 보정(<see cref="Sailing.LonScale"/>).
///
/// <b>어림인 데가 셋이다.</b>
/// <list type="bullet">
/// <item>바람은 길을 짤 때의 달 것을 끝까지 쓴다 — 긴 항해가 6월에서 7월로 넘어가면 표가 바뀐다.</item>
/// <item>게임이 풍향을 이레마다 한 눈금씩 흔드는 것은 넣지 않는다 — 표에 적힌 풍향 그대로다.</item>
/// <item>함대가 달라지면(돛 · 선원) 답도 달라진다 — 길을 짤 때의 함대로 센다.</item>
/// </list>
/// 바람 표는 바다를 50 × 25 로 나눈 것이라(한 칸이 지도 50 × 50 칸) 값도 그 칸마다 여덟 쪽씩만 세어 둔다.
/// </remarks>
public sealed class SailCost
{
    private const int MapW = WorldMapRenderer.UnfoldedW;   // 2500
    private const int MapH = WorldMapRenderer.CellH;       // 1250

    /// <summary>바람 표 한 칸이 덮는 지도 칸 수.</summary>
    private const int Block = MapW / WindTable.Cols;

    /// <summary>길찾기의 여덟 걸음(<see cref="SeaPathfinder"/> 와 같은 차례)과 그 뱃머리(16방위, 반시계).</summary>
    internal static readonly (int Dx, int Dy, int Heading)[] Steps =
    [
        (1, 0, 12), (-1, 0, 4), (0, 1, 8), (0, -1, 0),
        (1, 1, 10), (1, -1, 14), (-1, 1, 6), (-1, -1, 2),
    ];

    /// <summary>이보다 느린 걸음은 이 값으로 친다 — 돛 효율이 0 인 각에서 값이 끝없이 커지지 않게.</summary>
    private const double Slowest = 0.004;

    /// <summary>[바람 칸 × 8 + 걸음] 한 틱에 가는 칸 수 — 얕은 바다(해류 없음)와 난바다(해류 있음).</summary>
    private readonly double[] _shallow = new double[WindTable.Count * 8];
    private readonly double[] _deep = new double[WindTable.Count * 8];

    /// <summary>[행 × 8 + 걸음] 경도 보정을 넣은 걸음 길이 — 위도가 높으면 가로 한 칸이 짧다.</summary>
    private readonly double[] _length = new double[MapH * 8];

    private readonly double[] _lon = new double[MapH];

    /// <summary>가장 빠른 걸음(칸/틱) — 어림(휴리스틱)의 바닥을 이것으로 잡는다.</summary>
    public double Fastest { get; }

    /// <summary>
    /// 이 걸음 값의 지문 — 달과 함대가 같으면 같다. 찾은 길을 적어 둘 때 열쇠로 쓴다(<see cref="RouteCache"/>). 0 은 안 나온다.
    /// </summary>
    public long Signature { get; }

    /// <param name="fleetSpeed">풍향 · 풍속 · 뱃머리로 함대 속도를 내는 손(<c>ShipMapHost.FleetSpeed</c> 의 바다 쪽).</param>
    public SailCost(WindTable wind, int month, Func<int, int, int, int> fleetSpeed)
    {
        double fastest = Slowest;
        for (int cell = 0; cell < WindTable.Count; cell++)
        {
            var air = wind.WindAt(cell, month);
            var flow = wind.CurrentAt(cell);
            var (fx, fy) = wind.Vector(flow.Dir);
            var (pushX, pushY) = flow.Speed > 0 ? Sailing.Drift((fx, fy), flow.Speed, 1.0) : (0.0, 0.0);

            for (int k = 0; k < 8; k++)
            {
                var (dx, dy, heading) = Steps[k];
                int speed = fleetSpeed(air.Dir, air.Speed, heading);
                double norm = Math.Sqrt(dx * dx + dy * dy);
                double along = (pushX * dx + pushY * dy) / norm;

                double shallow = Math.Max(Slowest, Sailing.CellsPerTick(speed, false));
                double deep = Math.Max(Slowest, Sailing.CellsPerTick(speed, true) + along);
                _shallow[cell * 8 + k] = shallow;
                _deep[cell * 8 + k] = deep;
                fastest = Math.Max(fastest, Math.Max(shallow, deep));
            }
        }
        Fastest = fastest;

        // 걸음 값 표를 훑은 FNV-1a. 값은 천분의 일까지만 본다 — 셈 끝자리가 흔들려도 같은 지문이다.
        ulong hash = 14695981039346656037UL;
        foreach (var table in new[] { _shallow, _deep })
            foreach (double v in table)
            {
                hash ^= (ulong)(long)Math.Round(v * 1000);
                hash *= 1099511628211UL;
            }
        Signature = hash == 0 ? 1 : unchecked((long)hash);

        for (int y = 0; y < MapH; y++)
        {
            double lon = Sailing.LonScale(90.0 - (y + 0.5) * 180.0 / MapH);
            _lon[y] = lon;
            for (int k = 0; k < 8; k++)
            {
                double dx = Steps[k].Dx / lon, dy = Steps[k].Dy;
                _length[y * 8 + k] = Math.Sqrt(dx * dx + dy * dy);
            }
        }
    }

    /// <summary>그 행의 경도 보정(1 이상).</summary>
    public double LonOf(int y) => _lon[Math.Clamp(y, 0, MapH - 1)];

    /// <summary>그 칸에서 그 걸음을 떼는 데 드는 틱 수.</summary>
    /// <param name="x">0 ~ 2499 로 접은 가로 칸.</param>
    /// <param name="deep">난바다(부류 1)인지 — 빠른 식에 해류가 붙는다.</param>
    public double Ticks(int x, int y, int step, bool deep)
    {
        int cell = y / Block * WindTable.Cols + x / Block;
        return _length[y * 8 + step] / (deep ? _deep : _shallow)[cell * 8 + step];
    }

    /// <summary>걸음(−1 · 0 · 1 둘)의 차례. 제자리면 −1.</summary>
    internal static int StepOf(int dx, int dy)
    {
        for (int k = 0; k < 8; k++)
            if (Steps[k].Dx == dx && Steps[k].Dy == dy) return k;
        return -1;
    }
}
