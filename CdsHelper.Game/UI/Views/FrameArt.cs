using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace CdsHelper.Game.UI.Views;

/// <summary>
/// 게임 액자를 코드로 그린다. 어떤 크기로든 이음매 없이 나온다.
/// </summary>
/// <remarks>
/// 게임 그림(<c>MISC.CDS</c> 파트 0)을 픽셀 단위로 읽어 보니 규칙이 딱 떨어졌다 —
/// 바깥에서 안으로 겹이 이렇게 쌓인다.
/// <code>
///   깊이 0        짙은 밤색 한 줄
///   깊이 1~2      밝은 테 두 줄
///   깊이 3~5      구슬 무늬 띠 세 줄   (다섯 칸마다 되풀이)
///   깊이 6        밝은 테 한 줄
///   깊이 7        짙은 밤색 한 줄
///   깊이 8        그늘 한 줄
///   깊이 9~       속(한 색)
/// </code>
/// 그래서 그림을 잘라 늘리는 대신 그때그때 그린다. 잘라 쓰면 늘릴 때 이음매가 보이고 크기마다
/// 조각을 따로 떠야 하는데, 그릴 줄 알면 그 수고가 다 없어진다.
///
/// 띠 <b>안에 놓는 칸은 여기서 그리지 않는다</b> — 게임 것은 손으로 그린 상자가 아니라
/// 원본 조각으로 지은 <b>베이지 버튼 띠</b>였다(<see cref="GameButton"/>).
///
/// 색은 원본에서 그대로 뽑았다.
/// </remarks>
internal static class FrameArt
{
    private const uint Outer = 0xFF705C48;    // 짙은 밤색 테

    private const uint Light = 0xFFC4B494;   // 밝은 테
    private const uint Dark = 0xFF503824;    // 구슬 띠 바탕
    private const uint Mid = 0xFF584838;     // 구슬
    private const uint Shade = 0xFFBCA880;   // 속 가장자리 그늘
    private const uint Fill = 0xFFD4C8B0;    // 속

    /// <summary>테 두께. 이만큼 안쪽부터 속이다.</summary>
    public const int Border = 9;

    /// <summary>
    /// 얇은 테 두께. 상단·하단 띠처럼 낮아야 하는 자리에 쓴다.
    /// </summary>
    /// <remarks>
    /// 게임은 창 크기에 맞춰 화면을 줄인다. 큰 창에서 찍은 그림을 그대로 1배로 쓰면 우리
    /// 창에서는 테가 두꺼워 띠가 뚱뚱해진다.
    ///
    /// <b>줄일 때 밝은 테를 빼면 안 된다.</b> 구슬 띠는 두 어두운 색으로만 되어 있어서,
    /// 양쪽의 밝은 테가 없으면 무늬가 안 보이고 그냥 어두운 줄 하나로 뭉친다. 그래서 뺄 것은
    /// 밝은 테가 아니라 밝은 테 하나와 안쪽 짙은 선이다 —
    /// <c>짙은선(1) + 밝은테(1) + 구슬띠(3) + 밝은테(1) + 그늘(1)</c> 로 일곱 줄을 남긴다.
    ///
    /// 안쪽 그늘도 빼면 안 된다. 게임 띠를 확대해 보면 구슬 띠와 속 사이에 줄이 <b>둘</b>
    /// 보이는데, 밝은 테 한 줄과 이 그늘 한 줄이다. 그늘이 빠지면 속이 곧바로 붙어 밋밋해진다.
    /// </remarks>
    public const int ThinBorder = 7;

    /// <summary>구슬 무늬가 되풀이되는 참.</summary>
    private const int BeadPeriod = 5;

    /// <summary>
    /// 구슬 무늬 — 세로가 무늬 안에서의 줄(0~2), 가로가 변을 따라가는 자리(다섯 칸마다 되풀이).
    /// </summary>
    private static readonly uint[,] BeadPattern =
    {
        { Dark,  Mid,  Mid,  Dark,  Dark },
        { Outer, Dark, Dark, Outer, Dark },
        { Dark,  Mid,  Mid,  Dark,  Dark },
    };

    /// <summary>구슬 무늬의 한 점. <paramref name="row"/> 는 0~2 다.</summary>
    private static uint Bead(int row, int along) =>
        BeadPattern[row, ((along % BeadPeriod) + BeadPeriod) % BeadPeriod];

    /// <summary>
    /// 픽셀 하나가 테의 어느 겹에 드는지. 네 변 가운데 가장 가까운 변까지의 거리가 곧 깊이다.
    /// </summary>
    /// <remarks>
    /// <paramref name="along"/> 은 그 변을 따라간 자리다 — 위아래 변에 붙었으면 무늬가 가로로,
    /// 좌우 변이면 세로로 흐른다.
    /// </remarks>
    private static (int Depth, int Along) Where(int x, int y, int width, int height)
    {
        int dx = Math.Min(x, width - 1 - x);
        int dy = Math.Min(y, height - 1 - y);
        return (Math.Min(dx, dy), dy <= dx ? x : y);
    }

    /// <summary>점 배열을 그림으로 굳힌다.</summary>
    private static BitmapSource Freeze(uint[] px, int width, int height)
    {
        var bmp = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null,
                                      px, width * 4);
        bmp.Freeze();
        return bmp;
    }

    private static uint Pixel(int depth, int along, bool thin)
    {
        if (thin)
            return depth switch
            {
                0 => Outer,
                1 => Light,
                2 or 3 or 4 => Bead(depth - 2, along),
                5 => Light,
                6 => Shade,
                _ => Fill,
            };

        return depth switch
        {
            0 => Outer,
            1 or 2 => Light,
            3 or 4 or 5 => Bead(depth - 3, along),
            6 => Light,
            7 => Outer,
            8 => Shade,
            _ => Fill,
        };
    }

    /// <summary>액자 한 벌을 그린다. 크기가 테보다 작으면 null.</summary>
    /// <param name="thin">얇은 테(<see cref="ThinBorder"/>)로 그린다.</param>
    public static BitmapSource? Draw(int width, int height, bool thin = false)
    {
        int border = thin ? ThinBorder : Border;
        if (width < border * 2 || height < border * 2) return null;

        var px = new uint[width * height];
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                var (depth, along) = Where(x, y, width, height);
                px[y * width + x] = Pixel(depth, along, thin);
            }

        return Freeze(px, width, height);
    }
}
