using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using CdsHelper.Game.Local.Settings;

namespace CdsHelper.Game.UI.Views;

/// <summary>
/// 대항해시대2 모양 조각 — 베이지 바탕 · 일곱 줄 줄무늬 테 · 왕관 꼴 단추 판. 모드 「대항해시대2 스타일 대화창」 ·
/// 「대항해시대2 스타일 인물정보」가 같이 쓴다. 색은 대항해시대2 갈무리에서 집었다. 그림 없이 WPF 로 그린다.
/// </summary>
internal static class Dh2Style
{
    /// <summary>
    /// 고칠 수 있는 색 — 열쇠 · 이름 · 처음 색. 개발 창 「대항2」 탭에서 고르면 <see cref="GameSettings.Dh2Colors"/> 에 남고,
    /// 다음에 여는 창부터 든다. 처음 색은 대항해시대2 갈무리에서 집었다.
    /// </summary>
    public static readonly (string Key, string Label, Color Default)[] Slots =
    [
        ("paper", "바탕", Color.FromRgb(0xF2, 0xE3, 0xD3)),
        ("stripe0", "테 1 (바깥)", Colors.Black),
        ("stripe1", "테 2", Color.FromRgb(0xD3, 0x40, 0x01)),
        ("stripe2", "테 3", Color.FromRgb(0xF3, 0xA2, 0x61)),
        ("stripe3", "테 4", Colors.Black),
        ("stripe4", "테 5", Color.FromRgb(0xD3, 0x40, 0x01)),
        ("stripe5", "테 6", Color.FromRgb(0x00, 0xA2, 0x61)),
        ("stripe6", "테 7 (안쪽)", Color.FromRgb(0xD3, 0x40, 0x01)),
        ("brown", "단추 선", Color.FromRgb(0x7A, 0x30, 0x10)),
        ("rim1", "단추 테 1", Color.FromRgb(0xF3, 0xA2, 0x61)),
        ("rim2", "단추 테 2", Color.FromRgb(0xD3, 0x40, 0x01)),
        ("on1", "고른 단추 1", Color.FromRgb(0xF0, 0xC0, 0xD8)),
        ("on2", "고른 단추 2", Color.FromRgb(0xC8, 0xB0, 0xE0)),
        ("onText", "고른 단추 글씨", Color.FromRgb(0x28, 0x40, 0xA8)),
        ("off1", "안 고른 단추 1", Color.FromRgb(0x1E, 0x5A, 0x34)),
        ("off2", "안 고른 단추 2", Color.FromRgb(0x10, 0x40, 0x24)),
        ("offText", "안 고른 단추 글씨", Color.FromRgb(0xC0, 0xD4, 0xB8)),
    ];

    /// <summary>그 열쇠의 지금 색 — 고쳐 둔 것이 있으면 그것, 아니면 처음 색.</summary>
    public static Color ColorOf(string key)
    {
        if (GameSettings.Dh2Colors.TryGetValue(key, out string? hex) && TryParse(hex, out var mine)) return mine;
        foreach (var slot in Slots) if (slot.Key == key) return slot.Default;
        return Colors.Magenta;
    }

    /// <summary>「#RRGGBB」 또는 「RRGGBB」 를 읽는다.</summary>
    public static bool TryParse(string? hex, out Color color)
    {
        color = default;
        hex = hex?.Trim().TrimStart('#');
        if (hex is not { Length: 6 } || !uint.TryParse(hex, System.Globalization.NumberStyles.HexNumber, null, out uint v)) return false;
        color = Color.FromRgb((byte)(v >> 16), (byte)(v >> 8), (byte)v);
        return true;
    }

    /// <summary>색을 「RRGGBB」 로.</summary>
    public static string Hex(Color c) => $"{c.R:X2}{c.G:X2}{c.B:X2}";

    /// <summary>바탕 — 처음은 <c>F2E3D3</c>.</summary>
    public static Brush Paper => Frozen(ColorOf("paper"));

    /// <summary>
    /// 테 — 바깥에서 안쪽으로 한 점씩 일곱 줄: 검정 · 빨강(<c>D34001</c>) · 주황(<c>F3A261</c>) · 검정 · 빨강 · 초록(<c>00A261</c>) · 빨강.
    /// </summary>
    private static Brush[] Stripes => [.. Enumerable.Range(0, 7).Select(i => Frozen(ColorOf($"stripe{i}")))];

    /// <summary>항목 이름 글씨 — 공용 색표에서 대항해시대2 빨강(<c>D34001</c>)에 가장 가까운 60번(<c>944034</c>)이다.</summary>
    public const byte LabelColor = 60;

    /// <summary>단추 색 — 띠(바탕색) · 갈색 선 · 황갈 바둑판 테 · 고른 판(분홍 · 연보라, 파란 글씨) · 안 고른 판(짙은 초록, 옅은 글씨).</summary>
    public static Brush Bar => Paper;   // 띠는 바탕과 같다 — 다른 크림색이면 단추 둘레만 떠 보였다(요청)
    public static Brush Brown => Frozen(ColorOf("brown"));
    private static Brush Rim => GameUi.Dither(ColorOf("rim1"), ColorOf("rim2"), 1);
    private static Brush On => GameUi.Dither(ColorOf("on1"), ColorOf("on2"), 1);
    private static Brush Off => GameUi.Dither(ColorOf("off1"), ColorOf("off2"), 1);
    private static Brush OnText => Frozen(ColorOf("onText"));
    private static Brush OffText => Frozen(ColorOf("offText"));

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
        var stripes = Stripes;
        for (int i = stripes.Length - 1; i >= 0; i--)
            wrapped = new Border { BorderBrush = stripes[i], BorderThickness = new Thickness(1), SnapsToDevicePixels = true, Child = wrapped };
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
        Brush on = On, off = Off, onText = OnText, offText = OffText;
        focused = lit =>
        {
            face.Background = lit ? on : off;
            label.Foreground = lit ? onText : offText;
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
