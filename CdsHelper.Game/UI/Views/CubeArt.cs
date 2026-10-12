using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using CdsHelper.Support.Local.Models;

namespace CdsHelper.Game.UI.Views;

/// <summary>
/// 「화살표 입방체 퍼즐」 왼쪽 칸의 <b>입방체</b> — 모델이 든 귀퉁이 좌표를 그때그때 비춘다.
/// </summary>
/// <remarks>
/// 원본의 입방체 객체(<c>0x004AC9E0</c>, 그리기 <c>0x004ACF80</c>)를 옮긴 것이다. 그림 파일이 아니라
/// 면마다 세 조각(면 · 화살촉 · 화살대)을 지어(<c>0x0041B600</c> · <c>0x0041B900</c>) 먼 면부터 덧칠한다
/// (<c>0x004AD230</c>) — 뒷면을 가려내지 않고 여섯 면을 다 그린다.
///
/// <b>비추는 법</b>(<c>0x004AD490</c>) — 돌린 각 A = 225 도, 기운 각 B = 30 도, 눈까지 800, 배율 300 이다.
/// <code>
///   u  = x·cosA + y·sinA            d = z·sinB + u·cosB            k = 300 / (800 − d)
///   X  = (y·cosA − x·sinA)·k        Y = (z·cosB − u·sinB)·k        멀기 = 800 − d
///   화면 = 상자 가운데 + (X, −Y)
/// </code>
/// <b>화살표</b>는 면 가운데에서 <b>귀퉁이 쪽</b>으로 난다 — 값 a 가 그 면의 a 번 귀퉁이다.
/// </remarks>
internal sealed class CubeArt
{
    private const double Eye = 800, Focal = 300;
    private static readonly double CosA = Math.Cos(225 * Math.PI / 180), SinA = Math.Sin(225 * Math.PI / 180);
    private static readonly double CosB = Math.Cos(30 * Math.PI / 180), SinB = Math.Sin(30 * Math.PI / 180);

    private readonly Polygon[] _face = new Polygon[6];
    private readonly Polygon[] _head = new Polygon[6];
    private readonly Polygon[] _shaft = new Polygon[6];
    private readonly Point _center;
    private readonly int _zIndex;

    public CubeArt(Canvas scene, Point center, Brush fill, Brush edge, Brush arrow, int zIndex)
    {
        _center = center;
        _zIndex = zIndex;

        for (int i = 0; i < 6; i++)
        {
            _face[i] = new Polygon { Fill = fill, Stroke = edge, StrokeThickness = 1, IsHitTestVisible = false };
            _head[i] = new Polygon { Fill = arrow, IsHitTestVisible = false };
            _shaft[i] = new Polygon { Fill = arrow, IsHitTestVisible = false };
            scene.Children.Add(_face[i]);
            scene.Children.Add(_head[i]);
            scene.Children.Add(_shaft[i]);
        }
    }

    /// <summary>
    /// 그 단추 쪽으로 <paramref name="degrees"/> 만큼 더 돌린 모습으로 그린다 — 0 이면 지금 그대로다.
    /// </summary>
    /// <remarks>
    /// 원본은 15 도씩 여섯 걸음으로 돈다(<c>0x004ACFF0</c>). 축은 단추마다 다르다 — 2 · 5 가 x 축,
    /// 3 · 4 가 y 축, 6 · 7 이 z 축이고 짝끼리 도는 쪽이 반대다(<c>0x00463CD0</c> · <c>0x00463BF0</c> · <c>0x00463B10</c>).
    /// </remarks>
    public void Draw(CubePuzzle game, int button, double degrees)
    {
        var far = new double[6];
        var corners = new double[6][][];

        for (int f = 0; f < 6; f++)
        {
            corners[f] = new double[4][];
            for (int i = 0; i < 4; i++)
            {
                var (x, y, z) = game.CornerAt(f, i);
                corners[f][i] = Turned(button, degrees, [x, y, z]);
                far[f] = Math.Max(far[f], Depth(corners[f][i]));
            }
        }

        // 먼 면부터 칠한다 — 열쇠는 면 귀퉁이 넷 가운데 가장 먼 것이다.
        int[] order = [.. Enumerable.Range(0, 6).OrderByDescending(f => far[f])];
        for (int rank = 0; rank < 6; rank++)
        {
            int f = order[rank];
            var v = corners[f];
            int a = game.ArrowOf(f);

            // 화살표 꼴(0x0041B900) — 가운데 C 에서 귀퉁이 a 쪽으로 반쯤 나간 촉 T, 양옆 귀퉁이 쪽으로 삼분의 일씩 벌린 P1 · P3.
            var c = Mid(v[a], v[(a + 2) & 3]);
            var tip = Mid(v[a], c);
            var p1 = Third(v[(a + 1) & 3], c);
            var p3 = Third(v[(a + 3) & 3], c);
            var s1 = Mid(c, p1);
            var s3 = Mid(c, p3);
            double[] back = [(v[(a + 2) & 3][0] - c[0]) / 2, (v[(a + 2) & 3][1] - c[1]) / 2, (v[(a + 2) & 3][2] - c[2]) / 2];

            _face[f].Points = Points(v[0], v[1], v[2], v[3]);
            _head[f].Points = Points(tip, p1, p3);
            _shaft[f].Points = Points(s1, s3, Plus(s3, back), Plus(s1, back));

            Panel.SetZIndex(_face[f], _zIndex + rank * 2);
            Panel.SetZIndex(_head[f], _zIndex + rank * 2 + 1);
            Panel.SetZIndex(_shaft[f], _zIndex + rank * 2 + 1);
        }
    }

    /// <summary>그 단추의 축으로 돌린 자리.</summary>
    private static double[] Turned(int button, double degrees, double[] p)
    {
        if (degrees == 0) return p;

        double t = degrees * Math.PI / 180;
        if (button is CubePuzzle.TipDownRight or CubePuzzle.TipUpRight or CubePuzzle.SpinLeft) t = -t;
        double c = Math.Cos(t), s = Math.Sin(t);

        return button switch
        {
            CubePuzzle.TipUpLeft or CubePuzzle.TipDownRight => [p[0], c * p[1] + s * p[2], c * p[2] - s * p[1]],
            CubePuzzle.TipDownLeft or CubePuzzle.TipUpRight => [c * p[0] - s * p[2], p[1], c * p[2] + s * p[0]],
            _ => [c * p[0] + s * p[1], c * p[1] - s * p[0], p[2]],
        };
    }

    private static double Depth(double[] p) => Eye - (p[2] * SinB + (p[0] * CosA + p[1] * SinA) * CosB);

    private Point Screen(double[] p)
    {
        double u = p[0] * CosA + p[1] * SinA;
        double k = Focal / (Eye - (p[2] * SinB + u * CosB));
        return new Point(_center.X + (p[1] * CosA - p[0] * SinA) * k, _center.Y - (p[2] * CosB - u * SinB) * k);
    }

    private PointCollection Points(params double[][] corners)
    {
        var points = new PointCollection();
        foreach (var p in corners) points.Add(Screen(p));
        return points;
    }

    private static double[] Mid(double[] a, double[] b) => [(a[0] + b[0]) / 2, (a[1] + b[1]) / 2, (a[2] + b[2]) / 2];

    /// <summary>(a + 2·c) / 3 — 가운데 c 에서 a 쪽으로 삼분의 일.</summary>
    private static double[] Third(double[] a, double[] c) =>
        [(a[0] + 2 * c[0]) / 3, (a[1] + 2 * c[1]) / 3, (a[2] + 2 * c[2]) / 3];

    private static double[] Plus(double[] a, double[] b) => [a[0] + b[0], a[1] + b[1], a[2] + b[2]];
}
