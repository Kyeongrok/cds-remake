namespace CdsHelper.Game.Local.Helpers;

/// <summary>
/// 바람·해류를 지도에 얹을 때 쓰는 화살표 그림. 16방위를 미리 돌려 한 줄로 이어 둔다.
/// </summary>
/// <remarks>
/// 게임에는 없는 것이다 — 원본은 화살표 대신 바다 타일을 갈아끼워 흐름을 보인다
/// (<see cref="WindTable.BuildRippleTiles"/>). 이쪽은 지도로 볼 때 한눈에 방향을 읽으라고
/// 앱이 얹는 것이라, 게임 그림에서 뽑지 않고 여기서 그린다.
///
/// 방위는 <see cref="WindTable.Vector"/> 와 같은 뜻이다 — <b>불어가는 쪽</b>을 가리킨다.
/// 화살촉이 그 방향으로 선다.
///
/// 색은 넣지 않는다. 알파만 있는 흰 그림이라 셰이더가 바람·해류에 각각 다른 색을 입힌다.
/// 테두리는 알파 채널 안에서 <b>더 옅은 값</b>으로 남겨 두고(셰이더가 어둡게 깐다),
/// 그래야 남색 바다 위에서도 형태가 보인다.
/// </remarks>
public static class FlowArrows
{
    /// <summary>한 칸의 한 변(점).</summary>
    public const int CellSize = 32;

    /// <summary>칸 수 = 방위 수.</summary>
    public const int Count = WindTable.DirCount;

    /// <summary>이어 붙인 그림의 폭.</summary>
    public const int AtlasWidth = CellSize * Count;

    /// <summary>알파 안에서 테두리로 쓰는 값. 이보다 크면 화살표 속이다.</summary>
    public const byte BodyAlpha = 255;

    /// <summary>테두리 알파. 셰이더가 이 값을 보고 어두운 색을 깐다.</summary>
    public const byte EdgeAlpha = 128;

    // 화살표는 위(-y)를 보는 모양으로 그려 놓고 방위만큼 돌린다. 좌표는 칸 가운데가 원점이다.
    private const float Half = CellSize / 2f;
    private const float TipY = -12f;      // 화살촉 끝
    private const float NeckY = -4f;      // 촉과 자루가 만나는 곳
    private const float TailY = 11f;      // 자루 끝
    private const float BarbX = 6f;       // 촉 날개의 벌어짐
    private const float Body = 2.0f;      // 자루 굵기(반)
    private const float Edge = 1.3f;      // 테두리 두께

    /// <summary>넉넉히 뜨는 배수. 가장자리를 부드럽게 하려고 한 점을 이만큼 쪼개 본다.</summary>
    private const int Super = 4;

    private static byte[]? _alpha;

    /// <summary>
    /// 16방위를 가로로 이어 붙인 알파 그림(<see cref="AtlasWidth"/> x <see cref="CellSize"/>).
    /// 한 번 그려 두고 계속 쓴다.
    /// </summary>
    public static byte[] Alpha => _alpha ??= Build();

    private static byte[] Build()
    {
        var buf = new byte[AtlasWidth * CellSize];
        for (int dir = 0; dir < Count; dir++)
        {
            // 방위 벡터를 그대로 쓰지 않고 각도로 돌린다 — 표가 없어도 그릴 수 있어야 한다.
            // 방위는 반시계로 22.5도씩이고 0 이 북(위)이다.
            double a = dir * (2 * Math.PI / Count);
            float ux = (float)(-Math.Sin(a));    // 방위 0 -> (0, -1) = 위
            float uy = (float)(-Math.Cos(a));

            int originX = dir * CellSize;
            for (int y = 0; y < CellSize; y++)
                for (int x = 0; x < CellSize; x++)
                {
                    int inside = 0, ring = 0;
                    for (int sy = 0; sy < Super; sy++)
                        for (int sx = 0; sx < Super; sx++)
                        {
                            float px = x + (sx + 0.5f) / Super - Half;
                            float py = y + (sy + 0.5f) / Super - Half;

                            // 화면 점을 화살표 제 좌표로 되돌린다(돌림표의 전치).
                            float lx = -uy * px + ux * py;
                            float ly = -ux * px - uy * py;

                            float d = Distance(lx, ly);
                            if (d <= Body) inside++;
                            else if (d <= Body + Edge) ring++;
                        }

                    int total = Super * Super;
                    byte v = inside > 0
                        ? (byte)(EdgeAlpha + (BodyAlpha - EdgeAlpha) * inside / total)
                        : (byte)(EdgeAlpha * ring / total);
                    buf[(y * AtlasWidth) + originX + x] = v;
                }
        }
        return buf;
    }

    /// <summary>화살표 뼈대(자루 하나 + 날개 둘)까지의 거리.</summary>
    private static float Distance(float x, float y)
    {
        float d = Segment(x, y, 0, TailY, 0, TipY);
        d = Math.Min(d, Segment(x, y, 0, TipY, -BarbX, NeckY));
        d = Math.Min(d, Segment(x, y, 0, TipY, BarbX, NeckY));
        return d;
    }

    private static float Segment(float px, float py, float ax, float ay, float bx, float by)
    {
        float vx = bx - ax, vy = by - ay;
        float wx = px - ax, wy = py - ay;
        float len2 = vx * vx + vy * vy;
        float t = len2 <= 0 ? 0 : Math.Clamp((wx * vx + wy * vy) / len2, 0f, 1f);
        float dx = wx - t * vx, dy = wy - t * vy;
        return MathF.Sqrt(dx * dx + dy * dy);
    }
}
