using System.IO;
using System.Windows.Media.Imaging;
using System.Windows.Media;
using CdsHelper.Game.Local.Helpers;
using CdsHelper.Support.Local.Helpers;

namespace CdsHelper.Game.UI.Views;

/// <summary>
/// MISC.CDS 의 화면 조각을 PNG 로 뽑아 <c>asset/ui</c> 에 넣는다. 도구 앱 「에셋」 줄이 부른다.
/// </summary>
/// <remarks>
/// 앱이 CDS 를 그때그때 읽어도 되지만, 손으로 다듬으려면 그림 파일이 있어야 한다 —
/// 배 그림(<c>asset/ship-g0</c>)과 타이틀 무늬(<c>asset/title</c>)도 같은 길로 넣었다.
///
/// 폭은 <b>행 간 상관</b>으로 찾는다. 파트마다 폭이 적혀 있지 않아서, 후보 폭으로 잘라
/// 위아래 줄이 얼마나 닮는지 재고 가장 닮는 것을 고른다. 진짜 폭에서 값이 확 낮아진다.
///
/// 파트 4 는 16 폭에 <b>16x36 과 16x24 짜리 상자가 번갈아</b> 여섯 장 들어 있다
/// (경계 y0·36·60·96·120·156). 낱장으로도 따로 떠 준다 — 제목 상자를 9-슬라이스로 늘릴 때
/// 그중 하나를 바탕으로 쓴다.
/// </remarks>
public static class UiSpriteDump
{
    /// <summary>
    /// 폭을 굳이 안 재고 아는 대로 박아 두는 파트. <see cref="BestWidth"/> 의 행 간 상관은
    /// 후보 폭이 둘 다 실제 길이를 나누어떨어뜨리면 헷갈릴 수 있다 — 파트3(아이콘)이 그래서
    /// 한때 32 로 잘못 잡혔다(진짜는 16). <see cref="UiSprites.IconPart"/> 문서에 그 사연이
    /// 있다.
    /// </summary>
    private static readonly Dictionary<int, int> KnownWidths = new() { [3] = 16 };

    /// <summary>파트 4 안에 든 상자들의 자리와 높이, 그리고 붙일 이름.</summary>
    private static readonly (int Y, int H, string Name)[] TitleBoxes =
    [
        (0, 36, "box-dark-36"),
        (36, 24, "box-dark-24"),
        (60, 36, "box-light-36"),
        (96, 24, "box-light-24"),
        (120, 36, "box-tan-36"),
        (156, 24, "box-tan-24"),
    ];

    /// <summary>뽑아서 넣는다. 넣은 자리를 돌려준다(못 하면 까닭).</summary>
    public static string Run(string gameDirectory)
    {
        var path = Path.Combine(gameDirectory, "MISC.CDS");
        if (!File.Exists(path)) return $"{path} 가 없습니다";

        var archive = Ls12Reader.Open(path);
        if (archive == null) return $"{path} 를 읽지 못했습니다";

        string outDir = Path.Combine(AssetRoot(), "ui");
        Directory.CreateDirectory(outDir);

        int made = 0;
        for (int i = 0; i < archive.PartCount; i++)
        {
            var d = archive.Decode(i);
            if (d == null || d.Length < 64) continue;

            int w = KnownWidths.TryGetValue(i, out var known) ? known : BestWidth(d);
            if (w <= 0) continue;
            Write(d, w, d.Length / w, 0, Path.Combine(outDir, $"misc-{i:D2}.png"));
            made++;

            // 제목 상자 낱장은 따로 떠 둔다.
            if (i == 4 && w == 16)
                foreach (var (y, h, name) in TitleBoxes)
                {
                    if ((y + h) * w > d.Length) continue;
                    Write(d, w, h, y * w, Path.Combine(outDir, $"{name}.png"));
                    made++;
                }
        }
        return made == 0 ? "뽑을 것이 없습니다" : $"{made}장 → {outDir}";
    }

    /// <summary>
    /// 위아래 줄이 가장 닮는 폭. 그 폭으로 잘라야 그림이 어긋나지 않는다.
    /// </summary>
    private static int BestWidth(byte[] d)
    {
        int best = 0;
        double bestScore = double.MaxValue;
        for (int w = 8; w <= 1024; w++)
        {
            if (d.Length % w != 0) continue;
            int h = d.Length / w;
            if (h < 8) continue;

            long sum = 0;
            int n = 0;
            for (int y = 1; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    sum += Math.Abs(d[y * w + x] - d[(y - 1) * w + x]);
                    n++;
                }
            double score = (double)sum / n;
            if (score < bestScore) { bestScore = score; best = w; }
        }
        return best;
    }

    /// <summary>색인 그림 한 조각을 PNG 로 적는다.</summary>
    private static void Write(byte[] d, int w, int h, int offset, string file)
    {
        var bgra = new uint[w * h];
        for (int i = 0; i < bgra.Length; i++)
        {
            int c = d[offset + i] * 3;
            bgra[i] = (uint)(0xFF << 24 | GamePalette.Rgb[c] << 16
                             | GamePalette.Rgb[c + 1] << 8 | GamePalette.Rgb[c + 2]);
        }
        var bmp = BitmapSource.Create(w, h, 96, 96, PixelFormats.Bgra32, null, bgra, w * 4);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bmp));
        using var stream = File.Create(file);
        encoder.Save(stream);
    }

    /// <summary>
    /// 그림을 넣을 <c>asset</c> 자리. 저장소 안에서 돌고 있으면 저장소의 것을 쓴다 —
    /// 빌드 폴더에 넣으면 다음 빌드에 지워져 손댈 새가 없다.
    /// </summary>
    private static string AssetRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "cds-helper.sln")))
                return Path.Combine(dir.FullName, "asset");
            dir = dir.Parent;
        }
        return Path.Combine(AppContext.BaseDirectory, "asset");
    }
}
