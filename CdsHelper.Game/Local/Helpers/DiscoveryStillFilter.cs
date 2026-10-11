using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace CdsHelper.Game.Local.Helpers;

/// <summary>
/// 아무 그림이나 <b>발견물 그림(<c>asset/dstill</c>)처럼</b> 바꾼다 — 320x240, 바랜 밤빛, 비스듬한 붓결, 양피지 테두리.
/// </summary>
/// <remarks>
/// 원본에 있는 셈이 아니다 — 원본 여든넉 장을 눈으로 보고 흉내 낸 것이다. 새 발견물에 넣을 그림을 사진 한 장으로
/// 만들려고 둔다. 같은 그림이면 늘 같은 결과가 나온다(난수 대신 자리로 셈한 값을 쓴다).
/// </remarks>
public static class DiscoveryStillFilter
{
    /// <summary>발견물 그림의 크기.</summary>
    public const int Width = 320, Height = 240;

    /// <summary>붓결 한 줄의 길이(세로 칸) · 붓결을 따라 번지는 폭 · 테두리가 바래는 폭 · 붉게 물드는 가장자리 폭.</summary>
    private const int StrokeLength = 22, Smear = 2, Fade = 16, Rim = 4;

    // 어두운 데 · 가운데 · 밝은 데의 빛깔(R, G, B) — 원본 그림에서 집은 것.
    private static readonly double[] Dark = [34, 24, 14], Mid = [150, 122, 88], Light = [236, 226, 204];
    private static readonly double[] Paper = [240, 229, 203], RimTint = [204, 138, 112];

    /// <summary>그림 파일을 읽어 발견물 그림처럼 바꾼다. 못 읽으면 null.</summary>
    public static uint[]? Apply(string path, out int width, out int height)
    {
        width = Width;
        height = Height;
        return PortraitImport.Load(path) is { } source ? Apply(source) : null;
    }

    /// <summary>그림을 320x240 으로 맞추고(가운데를 4:3 으로 잘라) 발견물 그림처럼 바꾼다.</summary>
    public static uint[] Apply(BitmapSource source)
    {
        var px = Fit(source);
        int n = Width * Height;

        // 밝기 — 여느 밝기와 빨강 · 초록 · 파랑 가운데 <b>짙고 옅은 데가 가장 크게 갈리는 것</b>을 쓴다. 물속 사진처럼
        // 온통 한 빛깔이면 여느 밝기로는 바탕과 몸이 한 덩어리가 된다. 그리고 아래위 2% 를 잘라 펴 준다.
        var lum = new double[n];
        double bestSpread = -1;
        foreach (var (r, g, b) in new[] { (0.299, 0.587, 0.114), (1.0, 0.0, 0.0), (0.0, 1.0, 0.0), (0.0, 0.0, 1.0) })
        {
            var tried = new double[n];
            double sum = 0, squares = 0;
            for (int i = 0; i < n; i++)
            {
                uint c = px[i];
                double v = (r * (c >> 16 & 255) + g * (c >> 8 & 255) + b * (c & 255)) / 255.0;
                tried[i] = v;
                sum += v;
                squares += v * v;
            }
            double spread = squares / n - sum / n * (sum / n);
            // 여느 밝기를 조금 봐준다 — 엇비슷하면 눈에 보이는 대로가 낫다.
            if (r < 1 && g < 1 && b < 1) spread *= 1.3;
            if (spread <= bestSpread) continue;
            bestSpread = spread;
            lum = tried;
        }
        var histogram = new int[256];
        for (int i = 0; i < n; i++) histogram[(int)(lum[i] * 255)]++;
        double low = Percentile(histogram, n, 0.02), high = Percentile(histogram, n, 0.98);
        if (high - low < 0.1) { low = 0; high = 1; }

        var made = new uint[n];
        for (int y = 0; y < Height; y++)
            for (int x = 0; x < Width; x++)
            {
                // 붓결 — 오른쪽 위로 가파르게 선 줄(가로 1 에 세로 2). 줄마다 토막을 내고, 토막마다 붓결을 따라
                // 조금 밀린 자리의 밝기를 가져와 번지게 한다.
                int line = 2 * x + y;
                int piece = (y + (int)(Hash(line, 7) * StrokeLength)) / StrokeLength;
                double roll = Hash(line, piece);
                int slide = (int)Math.Round((Hash(piece, line) - 0.5) * 2 * Smear);
                int sx = Math.Clamp(x + slide, 0, Width - 1), sy = Math.Clamp(y - 2 * slide, 0, Height - 1);
                int at = sy * Width + sx;

                double t = Math.Clamp((lum[at] - low) / (high - low), 0, 1);
                // 토막 몇은 긁힌 자국이다 — 밝은 데는 어둡게, 어두운 데는 밝게 민다.
                t = Smooth(Math.Pow(t, 0.7));                                      // 바탕을 종이 쪽으로 띄우고, 짙고 옅은 데를 더 가른다
                if (roll < 0.09) t += (0.45 - t) * 0.5;
                t += (Hash(x * 3 + 1, y * 5 + 2) - 0.5) * 0.06;     // 종이 결
                t = Math.Clamp(t, 0, 1);

                // 테두리 — 가장자리로 갈수록 종이 빛으로 바래고, 맨 끝은 붉게 물든다. 경계는 울퉁불퉁하다.
                double edge = Math.Min(Math.Min(x, Width - 1 - x), Math.Min(y, Height - 1 - y));
                double wobble = (Noise(x / 9.0, y / 9.0) - 0.5) * 12 + (roll - 0.5) * 3;
                double keep = Smooth(Math.Clamp((edge + wobble) / Fade, 0, 1));
                double rim = Math.Clamp(1 - edge / Rim, 0, 1) * 0.6;

                uint src = px[at];
                double srcLum = 0.299 * (src >> 16 & 255) + 0.587 * (src >> 8 & 255) + 0.114 * (src & 255);
                uint packed = 0xFF000000;
                for (int k = 0; k < 3; k++)
                {
                    double tone = t < 0.5 ? Dark[k] + (Mid[k] - Dark[k]) * (t * 2) : Mid[k] + (Light[k] - Mid[k]) * (t * 2 - 1);
                    // 본디 빛깔을 조금만 남긴다 — 원본도 살빛 · 붉은 기가 엷게 비친다.
                    double channel = src >> (16 - 8 * k) & 255;
                    double v = tone + (channel - srcLum) * 0.12;
                    v = Paper[k] + (v - Paper[k]) * keep;
                    v += (RimTint[k] - v) * rim;
                    // 원본은 색이 백 남짓이다 — 여덟 단계씩 끊어 결을 맞춘다.
                    int level = Math.Clamp((int)Math.Round(v / 8) * 8, 0, 255);
                    packed |= (uint)level << (16 - 8 * k);
                }
                made[y * Width + x] = packed;
            }
        return made;
    }

    /// <summary>가운데를 4:3 으로 잘라 320x240 BGRA 로 맞춘다.</summary>
    private static uint[] Fit(BitmapSource source)
    {
        int w = source.PixelWidth, h = source.PixelHeight;
        int cropW = Math.Min(w, h * Width / Height), cropH = Math.Min(h, w * Height / Width);
        cropW = Math.Max(1, cropW);
        cropH = Math.Max(1, cropH);
        BitmapSource cut = new CroppedBitmap(source, new Int32Rect((w - cropW) / 2, (h - cropH) / 2, cropW, cropH));

        var visual = new DrawingVisual();
        RenderOptions.SetBitmapScalingMode(visual, BitmapScalingMode.Fant);
        using (var dc = visual.RenderOpen())
            dc.DrawImage(cut, new Rect(0, 0, Width, Height));
        var target = new RenderTargetBitmap(Width, Height, 96, 96, PixelFormats.Pbgra32);
        target.Render(visual);

        var px = new uint[Width * Height];
        target.CopyPixels(px, Width * 4, 0);
        return px;
    }

    private static double Percentile(int[] histogram, int total, double part)
    {
        int want = (int)(total * part), seen = 0;
        for (int i = 0; i < histogram.Length; i++)
            if ((seen += histogram[i]) > want) return i / 255.0;
        return 1;
    }

    private static double Smooth(double t) => t * t * (3 - 2 * t);

    /// <summary>부드럽게 이어지는 0~1 의 값 — 테두리가 네모나게 끊기지 않게 한다.</summary>
    private static double Noise(double x, double y)
    {
        int ix = (int)Math.Floor(x), iy = (int)Math.Floor(y);
        double fx = Smooth(x - ix), fy = Smooth(y - iy);
        double top = Hash(ix, iy + 91) + (Hash(ix + 1, iy + 91) - Hash(ix, iy + 91)) * fx;
        double bottom = Hash(ix, iy + 92) + (Hash(ix + 1, iy + 92) - Hash(ix, iy + 92)) * fx;
        return top + (bottom - top) * fy;
    }

    /// <summary>두 수로 셈한 0 이상 1 미만의 값 — 난수 대신 쓴다.</summary>
    private static double Hash(int a, int b)
    {
        uint h = unchecked((uint)a * 0x9E3779B1u ^ (uint)b * 0x85EBCA77u);
        h ^= h >> 15;
        h *= 0x2C1B3C6Du;
        h ^= h >> 12;
        h *= 0x297A2D39u;
        h ^= h >> 15;
        return (h & 0xFFFFFF) / (double)0x1000000;
    }
}
