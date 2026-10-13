using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace CdsHelper.Support.Local.Helpers;

/// <summary>
/// 48x48 배 그림을 업스케일 모델로 키워 고해상도 그림(<c>ship_{방향}_hd.png</c>)을 만든다.
/// </summary>
/// <remarks>
/// 모델은 <see cref="UpscaleModelAsset"/> 이 받아 둔 ONNX 다. 한 장을 이렇게 다룬다.
/// <code>
///   1. 돛의 점무늬를 지운다      원본 돛은 밝은 바탕에 파란 점을 찍어 놓은 것이라, 그대로 키우면 물방울무늬가 된다
///   2. 비치는 자리를 메운다      둘레 색을 바깥으로 번지게 해 둔다 — 안 그러면 키울 때 가장자리에 검은 테가 낀다
///   3. 색을 키운다               모델에 RGB 로 넣는다
///   4. 비침을 키운다             알파를 흑백 그림으로 같은 모델에 넣는다 — 계단진 윤곽이 색과 같은 모양으로 펴진다
/// </code>
/// 한 변은 모델 배율(4배)만큼 는다. 48 → 192 라 <c>ShipSprites.HiFrame</c> 이 받는 크기다.
/// </remarks>
public sealed class ShipRemaster : IDisposable
{
    private readonly InferenceSession _session;
    private readonly string _input;

    public ShipRemaster(string modelPath)
    {
        _session = new InferenceSession(modelPath);
        _input = _session.InputMetadata.Keys.First();
    }

    public void Dispose() => _session.Dispose();

    /// <summary>그림 파일 한 장을 키워 PNG 로 적는다.</summary>
    public void RemasterFile(string sourcePath, string targetPath, bool descreen)
    {
        var (pixels, side) = ReadBgra(sourcePath);
        var (made, madeSide) = Remaster(pixels, side, descreen);

        var image = BitmapSource.Create(madeSide, madeSide, 96, 96, PixelFormats.Bgra32, null, made, madeSide * 4);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(image));

        string temp = targetPath + ".tmp";
        using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            encoder.Save(stream);
        }

        File.Move(temp, targetPath, overwrite: true);
    }

    /// <summary>정사각 BGRA(곧은 알파) 한 장을 키운다. 돌려주는 것도 BGRA 곧은 알파다.</summary>
    public (byte[] Pixels, int Side) Remaster(byte[] bgra, int side, bool descreen)
    {
        var work = (byte[])bgra.Clone();
        if (descreen) Descreen(work, side);
        Bleed(work, side);

        var color = new DenseTensor<float>([1, 3, side, side]);
        var mask = new DenseTensor<float>([1, 3, side, side]);
        for (int y = 0; y < side; y++)
            for (int x = 0; x < side; x++)
            {
                int at = (y * side + x) * 4;
                color[0, 0, y, x] = work[at + 2] / 255f;
                color[0, 1, y, x] = work[at + 1] / 255f;
                color[0, 2, y, x] = work[at] / 255f;
                float a = bgra[at + 3] / 255f;
                mask[0, 0, y, x] = mask[0, 1, y, x] = mask[0, 2, y, x] = a;
            }

        var (rgb, big) = Run(color);
        var (alpha, _) = Run(mask);

        int plane = big * big;
        var made = new byte[plane * 4];
        for (int i = 0; i < plane; i++)
        {
            // 흑백으로 넣었으니 세 채널이 거의 같다. 가운데를 가파르게 세워 흐린 테를 좁힌다.
            float a = (alpha[i] + alpha[plane + i] + alpha[2 * plane + i]) / 3f;
            a = Math.Clamp((a - 0.25f) / 0.5f, 0f, 1f);
            if (a <= 0f) continue;

            made[i * 4] = ToByte(rgb[2 * plane + i]);
            made[i * 4 + 1] = ToByte(rgb[plane + i]);
            made[i * 4 + 2] = ToByte(rgb[i]);
            made[i * 4 + 3] = ToByte(a);
        }

        return (made, big);
    }

    private (float[] Planes, int Side) Run(DenseTensor<float> tensor)
    {
        using var results = _session.Run([NamedOnnxValue.CreateFromTensor(_input, tensor)]);
        var output = results.First().AsTensor<float>();
        return (output.ToArray(), output.Dimensions[3]);
    }

    private static byte ToByte(float value) => (byte)Math.Clamp((int)MathF.Round(value * 255f), 0, 255);

    /// <summary>
    /// 돛의 점무늬를 지운다 — 밝은 이웃에 둘러싸인 <b>외딴 파란 점</b>을 이웃 색으로 갈아 낸다.
    /// </summary>
    /// <remarks>
    /// 원본 돛은 <c>DED6BD</c>(밝은 천) 바탕에 <c>5C6F93</c>(파랑)을 띄엄띄엄 찍은 것이다. 깃발처럼 파랑이
    /// 뭉쳐 있는 자리는 이웃도 파랗기 때문에 걸리지 않는다.
    /// </remarks>
    private static void Descreen(byte[] bgra, int side)
    {
        var source = (byte[])bgra.Clone();
        var b = new List<byte>(8);
        var g = new List<byte>(8);
        var r = new List<byte>(8);

        for (int y = 0; y < side; y++)
            for (int x = 0; x < side; x++)
            {
                int at = (y * side + x) * 4;
                if (source[at + 3] == 0 || !IsBlueDot(source, at)) continue;

                b.Clear(); g.Clear(); r.Clear();
                int luma = Luma(source, at);
                for (int dy = -1; dy <= 1; dy++)
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        int nx = x + dx, ny = y + dy;
                        if ((dx == 0 && dy == 0) || nx < 0 || ny < 0 || nx >= side || ny >= side) continue;
                        int n = (ny * side + nx) * 4;
                        if (source[n + 3] == 0 || IsBlueDot(source, n) || Luma(source, n) <= luma) continue;
                        b.Add(source[n]); g.Add(source[n + 1]); r.Add(source[n + 2]);
                    }

                if (b.Count < 5) continue;
                b.Sort(); g.Sort(); r.Sort();
                bgra[at] = b[b.Count / 2];
                bgra[at + 1] = g[g.Count / 2];
                bgra[at + 2] = r[r.Count / 2];
            }

        SmoothCloth(bgra, side);
    }

    /// <summary>
    /// 돛 천의 얼룩을 고른다 — 천 빛깔끼리 붙어 있는 자리를 이웃 천의 평균색으로 누그러뜨린다.
    /// </summary>
    /// <remarks>
    /// 점을 걷어도 천이 밝은 색 · 회색 · 푸른 회색 셋을 섞어 찍은 것이라, 그대로 키우면 지렁이 같은 결이 생긴다.
    /// 밧줄과 돛대는 가는 줄이라 천 이웃이 모자라 그대로 남는다.
    /// </remarks>
    private static void SmoothCloth(byte[] bgra, int side)
    {
        var source = (byte[])bgra.Clone();
        for (int y = 0; y < side; y++)
            for (int x = 0; x < side; x++)
            {
                int at = (y * side + x) * 4;
                if (!IsCloth(source, at)) continue;

                int sb = 0, sg = 0, sr = 0, n = 0;
                for (int dy = -1; dy <= 1; dy++)
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        int nx = x + dx, ny = y + dy;
                        if (nx < 0 || ny < 0 || nx >= side || ny >= side) continue;
                        int j = (ny * side + nx) * 4;
                        if (!IsCloth(source, j)) continue;
                        sb += source[j]; sg += source[j + 1]; sr += source[j + 2];
                        n++;
                    }

                if (n < 6) continue;   // 저까지 여섯 — 천 한가운데만 고른다
                bgra[at] = (byte)(sb / n);
                bgra[at + 1] = (byte)(sg / n);
                bgra[at + 2] = (byte)(sr / n);
            }
    }

    /// <summary>돛 천 빛깔인지 — 밝고 색기가 옅다.</summary>
    private static bool IsCloth(byte[] bgra, int at)
    {
        if (bgra[at + 3] == 0) return false;
        int max = Math.Max(bgra[at], Math.Max(bgra[at + 1], bgra[at + 2]));
        int min = Math.Min(bgra[at], Math.Min(bgra[at + 1], bgra[at + 2]));
        return Luma(bgra, at) >= 0x80 && max - min <= 0x38;
    }

    private static bool IsBlueDot(byte[] bgra, int at) => bgra[at] - bgra[at + 2] >= 0x28;

    private static int Luma(byte[] bgra, int at) => (bgra[at] * 29 + bgra[at + 1] * 150 + bgra[at + 2] * 77) >> 8;

    /// <summary>비치는 자리에 둘레 색을 번지게 한다. 끝까지 안 닿은 자리는 그림의 평균색으로 메운다.</summary>
    private static void Bleed(byte[] bgra, int side)
    {
        var filled = new bool[side * side];
        long sumB = 0, sumG = 0, sumR = 0, count = 0;
        for (int i = 0; i < filled.Length; i++)
        {
            if (bgra[i * 4 + 3] == 0) continue;
            filled[i] = true;
            sumB += bgra[i * 4]; sumG += bgra[i * 4 + 1]; sumR += bgra[i * 4 + 2];
            count++;
        }
        if (count == 0) return;

        for (int pass = 0; pass < 6; pass++)
        {
            var next = (bool[])filled.Clone();
            for (int y = 0; y < side; y++)
                for (int x = 0; x < side; x++)
                {
                    int i = y * side + x;
                    if (filled[i]) continue;

                    int nb = 0, ng = 0, nr = 0, n = 0;
                    for (int dy = -1; dy <= 1; dy++)
                        for (int dx = -1; dx <= 1; dx++)
                        {
                            int nx = x + dx, ny = y + dy;
                            if (nx < 0 || ny < 0 || nx >= side || ny >= side) continue;
                            int j = ny * side + nx;
                            if (!filled[j]) continue;
                            nb += bgra[j * 4]; ng += bgra[j * 4 + 1]; nr += bgra[j * 4 + 2];
                            n++;
                        }
                    if (n == 0) continue;

                    bgra[i * 4] = (byte)(nb / n);
                    bgra[i * 4 + 1] = (byte)(ng / n);
                    bgra[i * 4 + 2] = (byte)(nr / n);
                    next[i] = true;
                }
            filled = next;
        }

        for (int i = 0; i < filled.Length; i++)
        {
            if (filled[i]) continue;
            bgra[i * 4] = (byte)(sumB / count);
            bgra[i * 4 + 1] = (byte)(sumG / count);
            bgra[i * 4 + 2] = (byte)(sumR / count);
        }
    }

    private static (byte[] Pixels, int Side) ReadBgra(string path)
    {
        using var stream = File.OpenRead(path);
        var decoder = new PngBitmapDecoder(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        var source = new FormatConvertedBitmap(decoder.Frames[0], PixelFormats.Bgra32, null, 0);
        if (source.PixelWidth != source.PixelHeight)
            throw new InvalidOperationException($"{path} 가 정사각이 아닙니다 — {source.PixelWidth}x{source.PixelHeight}");

        var pixels = new byte[source.PixelWidth * source.PixelHeight * 4];
        source.CopyPixels(pixels, source.PixelWidth * 4, 0);
        return (pixels, source.PixelWidth);
    }
}
