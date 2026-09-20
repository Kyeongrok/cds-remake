using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using CdsHelper.Support.Local.Models;

namespace CdsHelper.Game.UI.Views;

/// <summary>
/// 「화살표 입방체 퍼즐」 왼쪽 칸의 <b>입방체</b> — 여섯 꼭짓점을 돌려 그때그때 비춘다.
/// </summary>
/// <remarks>
/// 이 그림은 <c>MGGRAPH.CDS</c> 에 없다(이 놀이가 쓰는 파트 열둘을 다 짚었다). 면마다
/// 화살표가 돌아가고 <b>넘어뜨릴 때 굴러가는 도중까지 보이므로</b> 게임도 그때그때
/// 그리는 것으로 보인다. 그래서 여기서도 작은 3차원으로 그린다.
///
/// <b>비추는 법.</b> 몸 좌표 축 셋을 화면 벡터 셋으로 곧장 옮긴다 — 원본 갈무리에서 잰
/// 윗면 마름모(110 x 50)와 옆면 높이(68)가 그대로 이 셋이다.
/// <code>
///   +X → ( 55,  25)     오른쪽 아래
///   +Y → (-55,  25)     왼쪽 아래
///   +Z → (  0, -68)     위
/// </code>
/// 이 비춤이 <b>안 보이는 쪽</b>은 <c>(1, 1, 0.735)</c> 다(세 벡터를 더해 0 이 되는 쪽).
/// 면의 법선을 이것과 견주어 앞을 보는 셋만 그린다 — 늘 위 · 왼쪽 · 오른쪽 셋이다.
///
/// <b>면 번호</b>는 <see cref="CubePuzzle"/> 것을 그대로 쓴다 — 0 이 <c>+Z</c>(위),
/// 5 가 <c>-Z</c>, 1 이 <c>+Y</c>, 4 가 <c>-Y</c>, 2 가 <c>+X</c>, 3 이 <c>-X</c> 다.
/// 마주 보는 면이 <c>5-i</c> 라는 모델의 규칙과 맞는다.
/// </remarks>
internal sealed class CubeArt
{
    /// <summary>비춤 벡터 — 원본에서 잰 마름모 110x50 과 옆면 68 이 이 값이다.</summary>
    private static readonly Vector Ax = new(55, 25), Ay = new(-55, 25), Az = new(0, -68);

    /// <summary>안 보이는 쪽. 면 법선과 이것을 곱해 앞뒤를 가린다.</summary>
    private static readonly double[] View = [1, 1, 50.0 / 68.0];

    /// <summary>면 여섯의 법선.</summary>
    private static readonly double[][] Normal =
    [
        [0, 0, 1],    // 0 위
        [0, 1, 0],    // 1
        [1, 0, 0],    // 2
        [-1, 0, 0],   // 3
        [0, -1, 0],   // 4
        [0, 0, -1],   // 5 아래
    ];

    /// <summary>
    /// 면 여섯을 <b>윗면 본</b>에서 만들어 내는 회전.
    /// </summary>
    /// <remarks>
    /// <b>이것이 화살표가 가리키는 쪽을 모델과 맞추는 열쇠다.</b> 면마다 제멋대로 «오른쪽·위»
    /// 를 잡으면, 그 면이 위로 올라왔을 때 그려진 쪽과 <see cref="CubePuzzle"/> 가 셈한 쪽이
    /// 어긋난다(위로 가는 화살표가 떴는데 좌대는 왼쪽으로 가는 식이다).
    ///
    /// 그래서 <b>윗면 하나만</b> 본을 만들고, 나머지 다섯은 «그 면을 위로 올리는 회전의
    /// 거꾸로» 를 먹여 만든다. 그러면 면 k 가 굴러 위로 올라올 때 그 회전이 상쇄되어
    /// 화살표가 정확히 <c>Ways[값]</c> 쪽을 가리킨다.
    /// <code>
    ///   0 (+Z)  그대로          1 (+Y)  X 축 -90      2 (+X)  Y 축 +90
    ///   3 (-X)  Y 축 -90        4 (-Y)  X 축 +90      5 (-Z)  X 축 180
    /// </code>
    /// </remarks>
    private static readonly double[][,] FromTop =
    [
        Identity(), Turn(0, -90), Turn(1, 90), Turn(1, -90), Turn(0, 90), Turn(0, 180),
    ];

    /// <summary>
    /// 윗면에서 화살표 값이 가리키는 쪽 — <b>면의 대각선</b>이다.
    /// </summary>
    /// <remarks>
    /// 판의 네 쪽이 이 비춤에서 가로·세로로 나오기 때문이다.
    /// <code>
    ///   북 = -X-Y → 화면 (   0, -50)      동 = +X-Y → (110, 0)
    ///   남 = +X+Y → 화면 (   0,  50)      서 = -X+Y → (-110, 0)
    /// </code>
    /// </remarks>
    private static readonly double[][] TopWay =
    [
        [-D, -D, 0], [D, -D, 0], [D, D, 0], [-D, D, 0],
    ];

    private const double D = 0.70710678;

    /// <summary>화살표 꼴 — 칸 가운데를 0 으로 두고 위(+)를 본다.</summary>
    private static readonly (double X, double Y)[] Arrow =
    [
        (0.00, 0.42), (0.30, 0.10), (0.13, 0.10),
        (0.13, -0.40), (-0.13, -0.40), (-0.13, 0.10), (-0.30, 0.10),
    ];

    private readonly Polygon[] _face = new Polygon[6];
    private readonly Polygon[] _mark = new Polygon[6];
    private readonly Point _center;

    /// <summary>지금 얼마나 돌아 있는가 — 몸 좌표를 판 좌표로 옮기는 행렬이다.</summary>
    private double[,] _rot = Identity();

    public CubeArt(Canvas scene, Point center, Brush fill, Brush edge, Brush arrow, int zIndex)
    {
        _center = center;

        for (int i = 0; i < 6; i++)
        {
            _face[i] = new Polygon
            {
                Fill = fill,
                Stroke = edge,
                StrokeThickness = 1,
                IsHitTestVisible = false,
            };
            Panel.SetZIndex(_face[i], zIndex);
            scene.Children.Add(_face[i]);

            _mark[i] = new Polygon { Fill = arrow, IsHitTestVisible = false };
            Panel.SetZIndex(_mark[i], zIndex + 1);
            scene.Children.Add(_mark[i]);
        }
    }

    /// <summary>넘어뜨리거나 돌릴 때 입방체가 어느 축으로 도는가.</summary>
    /// <remarks>
    /// 모델의 <c>Roll</c> 이 하는 면 갈아치기와 같은 회전이다 — 북으로 넘어뜨리면
    /// 위 면이 북으로 가니 <c>+X</c> 축으로 +90 도다. 수평 돌리기는 <c>+Z</c> 축이다.
    /// </remarks>
    public static (int Axis, int Turn) Spin(int way) => way switch
    {
        0 => (0, +1),    // 북 — X 축
        1 => (1, -1),    // 동 — Y 축
        2 => (0, -1),    // 남
        3 => (1, +1),    // 서
        _ => (2, +1),    // 수평 — Z 축
    };

    /// <summary>그 축으로 <paramref name="degrees"/> 만큼 더 돌린 모습으로 그린다.</summary>
    public void Draw(CubePuzzle game, int axis, double degrees)
    {
        var now = Multiply(Turn(axis, degrees), _rot);

        for (int i = 0; i < 6; i++)
        {
            var n = Apply(now, Normal[i]);
            bool front = n[0] * View[0] + n[1] * View[1] + n[2] * View[2] > 0.001;

            _face[i].Visibility = front ? Visibility.Visible : Visibility.Collapsed;
            _mark[i].Visibility = _face[i].Visibility;
            if (!front) continue;

            _face[i].Points = Corners(now, i);
            _mark[i].Points = Mark(now, i, game.PaintedArrow(i));
        }
    }

    /// <summary>돌린 것을 굳힌다 — 애니메이션이 끝나면 부른다.</summary>
    public void Settle(int axis, int quarter) =>
        _rot = Multiply(Turn(axis, quarter * 90.0), _rot);

    /// <summary>그 면 네 귀퉁이의 화면 자리 — 윗면 본을 그 면으로 돌려 만든다.</summary>
    private PointCollection Corners(double[,] rot, int face)
    {
        var points = new PointCollection();
        foreach (var (sx, sy) in new[] { (-0.5, -0.5), (0.5, -0.5), (0.5, 0.5), (-0.5, 0.5) })
            points.Add(Screen(Apply(rot, Apply(FromTop[face], [sx, sy, 0.5]))));
        return points;
    }

    /// <summary>그 면에 그려진 화살표. 윗면 본에서 그려 그 면으로 돌린다.</summary>
    private PointCollection Mark(double[,] rot, int face, int way)
    {
        var dir = TopWay[way & 3];
        var perp = new[] { dir[1], -dir[0], 0.0 };

        var points = new PointCollection();
        foreach (var (ax, ay) in Arrow)
        {
            double[] p =
            [
                perp[0] * ax + dir[0] * ay,
                perp[1] * ax + dir[1] * ay,
                0.502,
            ];
            points.Add(Screen(Apply(rot, Apply(FromTop[face], p))));
        }
        return points;
    }

    /// <summary>몸 좌표를 화면 자리로.</summary>
    private Point Screen(double[] p) =>
        _center + Ax * p[0] + Ay * p[1] + Az * p[2];

    private static double[] Apply(double[,] m, double[] v) =>
    [
        m[0, 0] * v[0] + m[0, 1] * v[1] + m[0, 2] * v[2],
        m[1, 0] * v[0] + m[1, 1] * v[1] + m[1, 2] * v[2],
        m[2, 0] * v[0] + m[2, 1] * v[1] + m[2, 2] * v[2],
    ];

    private static double[,] Identity() =>
        new double[,] { { 1, 0, 0 }, { 0, 1, 0 }, { 0, 0, 1 } };

    /// <summary>그 축으로 도는 행렬.</summary>
    private static double[,] Turn(int axis, double degrees)
    {
        double t = degrees * Math.PI / 180.0;
        double c = Math.Cos(t), s = Math.Sin(t);
        return axis switch
        {
            0 => new double[,] { { 1, 0, 0 }, { 0, c, -s }, { 0, s, c } },
            1 => new double[,] { { c, 0, s }, { 0, 1, 0 }, { -s, 0, c } },
            _ => new double[,] { { c, -s, 0 }, { s, c, 0 }, { 0, 0, 1 } },
        };
    }

    private static double[,] Multiply(double[,] a, double[,] b)
    {
        var m = new double[3, 3];
        for (int i = 0; i < 3; i++)
        for (int j = 0; j < 3; j++)
            m[i, j] = a[i, 0] * b[0, j] + a[i, 1] * b[1, j] + a[i, 2] * b[2, j];
        return m;
    }
}
