using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace CdsHelper.Game.UI.Views;

/// <summary>
/// 대항해시대2 모양 조각 — 베이지 바탕 · 일곱 줄 줄무늬 테 · 왕관 꼴 단추 판. 모드 「대항해시대2 스타일 대화창」 ·
/// 「대항해시대2 스타일 인물정보」가 같이 쓴다. 색은 대항해시대2 갈무리에서 집었다. 그림 없이 WPF 로 그린다.
/// </summary>
internal static class Dh2Style
{
    /// <summary>바탕 — <c>F2E3D3</c>.</summary>
    public static readonly Brush Paper = Frozen(Color.FromRgb(0xF2, 0xE3, 0xD3));

    /// <summary>
    /// 테 — 바깥에서 안쪽으로 한 점씩 일곱 줄: 검정 · 빨강(<c>D34001</c>) · 주황(<c>F3A261</c>) · 검정 · 빨강 · 초록(<c>00A261</c>) · 빨강.
    /// </summary>
    private static readonly Brush[] Stripes =
    [
        Frozen(Colors.Black), Frozen(Color.FromRgb(0xD3, 0x40, 0x01)), Frozen(Color.FromRgb(0xF3, 0xA2, 0x61)),
        Frozen(Colors.Black), Frozen(Color.FromRgb(0xD3, 0x40, 0x01)), Frozen(Color.FromRgb(0x00, 0xA2, 0x61)),
        Frozen(Color.FromRgb(0xD3, 0x40, 0x01)),
    ];

    /// <summary>항목 이름 글씨 — 공용 색표에서 대항해시대2 빨강(<c>D34001</c>)에 가장 가까운 60번(<c>944034</c>)이다.</summary>
    public const byte LabelColor = 60;

    /// <summary>단추 색 — 띠(바탕색) · 갈색 선 · 황갈 바둑판 테 · 고른 판(분홍 · 연보라, 파란 글씨) · 안 고른 판(짙은 초록, 옅은 글씨).</summary>
    public static readonly Brush Bar = Paper;   // 띠는 바탕과 같은 F2E3D3 — 다른 크림색이면 단추 둘레만 떠 보였다(요청)
    public static readonly Brush Brown = Frozen(Color.FromRgb(0x7A, 0x30, 0x10));
    private static readonly Brush Rim = GameUi.Dither(Color.FromRgb(0xF3, 0xA2, 0x61), Color.FromRgb(0xD3, 0x40, 0x01), 1);
    private static readonly Brush On = GameUi.Dither(Color.FromRgb(0xF0, 0xC0, 0xD8), Color.FromRgb(0xC8, 0xB0, 0xE0), 1);
    private static readonly Brush Off = GameUi.Dither(Color.FromRgb(0x1E, 0x5A, 0x34), Color.FromRgb(0x10, 0x40, 0x24), 1);
    private static readonly Brush OnText = Frozen(Color.FromRgb(0x28, 0x40, 0xA8));
    private static readonly Brush OffText = Frozen(Color.FromRgb(0xC0, 0xD4, 0xB8));

    /// <summary>단추 판의 높이와, 바깥 테에서 안쪽 글씨 판까지의 거리. 폭은 글에 따라 정한다.</summary>
    private const double PlaqueHeight = 30, PlaqueInset = 5, MinPlaqueWidth = 72;

    private static Brush Frozen(Color c)
    {
        var b = new SolidColorBrush(c);
        b.Freeze();
        return b;
    }

    /// <summary>알맹이를 줄무늬 테로 감싼다 — 줄 하나가 테 한 겹(한 점)이다.</summary>
    public static UIElement Frame(UIElement inner)
    {
        UIElement wrapped = inner;
        for (int i = Stripes.Length - 1; i >= 0; i--)
            wrapped = new Border { BorderBrush = Stripes[i], BorderThickness = new Thickness(1), SnapsToDevicePixels = true, Child = wrapped };
        return wrapped;
    }

    /// <summary>
    /// 왕관 꼴 단추 판 하나 — 바깥 테는 위아래에 이빨이 서고 네 귀가 뾰족한 다각형, 안쪽은 바둑판 바탕에 세리프 글씨다.
    /// <paramref name="focused"/> 로 고른 모양(분홍 · 파란 글씨)과 안 고른 모양(짙은 초록 · 옅은 글씨)을 갈아 끼운다.
    /// 누르면 <paramref name="run"/> 이다.
    /// </summary>
    public static FrameworkElement Plaque(string text, Action run, out Action<bool> focused)
    {
        var label = new TextBlock
        {
            Text = text,
            FontFamily = new FontFamily("Times New Roman, Batang"),
            FontWeight = FontWeights.Bold,
            FontSize = 15,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        label.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        double width = Math.Max(MinPlaqueWidth, Math.Ceiling(label.DesiredSize.Width) + PlaqueInset * 2 + 20);

        var rim = new System.Windows.Shapes.Polygon
        {
            Points = CrownPoints(width, PlaqueHeight),
            Fill = Rim,
            Stroke = Brown,
            StrokeThickness = 1,
            StrokeLineJoin = PenLineJoin.Miter,
            SnapsToDevicePixels = true,
        };
        var face = new Border
        {
            BorderBrush = Brown,
            BorderThickness = new Thickness(1),
            Margin = new Thickness(PlaqueInset),
            SnapsToDevicePixels = true,
            Child = label,
        };
        var plaque = new Grid
        {
            Width = width,
            Height = PlaqueHeight,
            Margin = new Thickness(0, 0, 3, 0),
            Cursor = Cursors.Hand,
            Background = Brushes.Transparent,   // 이빨 사이 빈 데를 눌러도 먹게
            Children = { rim, face },
        };
        // 누름은 여기서 삼킨다 — 창 테가 누름에 끌기(DragMove)를 걸어 두어, 누름이 올라가면 뗌이 안 와 단추가 안 먹었다(제보).
        plaque.MouseLeftButtonDown += (_, e) => { e.Handled = true; plaque.CaptureMouse(); };
        plaque.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            bool inside = plaque.IsMouseCaptured && plaque.IsMouseOver;
            plaque.ReleaseMouseCapture();
            if (inside) run();
        };
        focused = on =>
        {
            face.Background = on ? On : Off;
            label.Foreground = on ? OnText : OffText;
        };
        focused(false);
        return plaque;
    }

    /// <summary>단추 판들을 크림 띠로 감싼다. 띠에 테는 안 두른다 — 왕관 꼴 테가 단추의 테다(요청).</summary>
    public static Border PlaqueBar(IEnumerable<FrameworkElement> plaques)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        foreach (var p in plaques) row.Children.Add(p);
        return new Border
        {
            Background = Bar,
            Padding = new Thickness(3, 3, 0, 3), SnapsToDevicePixels = true, Child = row,
        };
    }

    /// <summary>
    /// 왕관 테의 꼭짓점들 — 위 · 아래 가장자리에 이빨(봉우리)을 <paramref name="tooth"/> 간격으로 세우고, 네 귀는 바깥으로
    /// 뾰족하게 내민다. 옆 가장자리 가운데도 한 번 오목하게 판다. 시계 방향으로 한 바퀴 돈다.
    /// </summary>
    private static PointCollection CrownPoints(double w, double h, double tooth = 6, double depth = 3)
    {
        var pts = new PointCollection { new(0, 0) };
        int n = Math.Max(2, (int)Math.Round((w - 2 * tooth) / tooth));
        double step = (w - 2 * tooth) / n;
        pts.Add(new Point(tooth, depth));
        for (int k = 0; k < n; k++)
        {
            double x = tooth + k * step;
            pts.Add(new Point(x + step / 2, k % 2 == 0 ? 0.5 : depth + 0.5));
            pts.Add(new Point(x + step, depth));
        }
        pts.Add(new Point(w, 0));
        pts.Add(new Point(w - depth, h / 2 - depth));
        pts.Add(new Point(w - depth - 1.5, h / 2));
        pts.Add(new Point(w - depth, h / 2 + depth));
        pts.Add(new Point(w, h));
        pts.Add(new Point(w - tooth, h - depth));
        for (int k = 0; k < n; k++)
        {
            double x = w - tooth - k * step;
            pts.Add(new Point(x - step / 2, k % 2 == 0 ? h - 0.5 : h - depth - 0.5));
            pts.Add(new Point(x - step, h - depth));
        }
        pts.Add(new Point(0, h));
        pts.Add(new Point(depth, h / 2 + depth));
        pts.Add(new Point(depth + 1.5, h / 2));
        pts.Add(new Point(depth, h / 2 - depth));
        pts.Freeze();
        return pts;
    }
}
