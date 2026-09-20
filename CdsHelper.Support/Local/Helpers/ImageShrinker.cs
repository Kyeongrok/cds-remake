using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace CdsHelper.Support.Local.Helpers;

/// <summary>
/// 그림 파일을 작게 줄이고, 시키면 격자로 나눠 여러 장으로 써 주는 도구.
/// </summary>
/// <remarks>
/// WPF 가 이미 안고 있는 WIC 로만 한다 — 라이브러리를 새로 물리지 않는다.
/// 줄이기는 <see cref="BitmapImage.DecodePixelWidth"/> 에 맡긴다. 통째로 푼 뒤에 줄이는 게 아니라
/// 디코더가 줄여서 풀어 주므로 큰 사진도 메모리를 덜 먹고 결과도 곱게 나온다.
/// 어느 방식으로 줄이든 가로세로 비는 그대로 지킨다 — 찌그러뜨리지 않는다.
/// 여러 장이 든 GIF·TIFF 는 첫 장만 쓴다.
/// </remarks>
public static class ImageShrinker
{
    /// <summary>얼마나 줄일지 정하는 방식. 어느 쪽이든 가로세로 비는 지킨다.</summary>
    public enum SizeMode
    {
        /// <summary>가로를 정해진 픽셀에 맞춘다. 세로는 비율대로 따라간다.</summary>
        Width,

        /// <summary>
        /// 나눈 조각 한 칸의 가로를 정해진 픽셀에 맞춘다.
        /// 전체 가로는 <see cref="Options.Columns"/> 를 곱한 만큼이 된다.
        /// </summary>
        CellWidth,

        /// <summary>세로를 정해진 픽셀에 맞춘다. 가로는 비율대로 따라간다.</summary>
        Height,

        /// <summary>긴 변을 정해진 픽셀에 맞춘다. 세로 그림·가로 그림이 섞여 있을 때 좋다.</summary>
        LongestSide,

        /// <summary>원래 크기의 몇 퍼센트로 줄인다.</summary>
        Percent,
    }

    /// <summary>어떤 형식으로 다시 쓸지.</summary>
    public enum OutputFormat
    {
        /// <summary>원본과 같은 형식으로 쓴다 — 확장자가 바뀌지 않는다.</summary>
        KeepSource,
        Jpeg,
        Png,
    }

    /// <summary>조각 둘레에 여백을 어떻게 두를지.</summary>
    public enum PadMode
    {
        /// <summary>안 두른다.</summary>
        None,

        /// <summary>
        /// 정사각형이 되도록 모자란 쪽에 두른다.
        /// 한 변은 격자에서 제일 긴 변으로 잡아 조각이 모두 같은 크기가 되게 한다.
        /// </summary>
        Square,

        /// <summary>정해 준 픽셀만큼 좌우·상하에 두른다.</summary>
        Fixed,
    }

    /// <summary>여백을 무엇으로 채울지.</summary>
    public enum PadColor
    {
        /// <summary>비워 둔다. JPEG 는 투명을 못 담으므로 흰색으로 채운다.</summary>
        Transparent,
        White,
        Black,
    }

    /// <summary>어디에 쓸지.</summary>
    public enum Destination
    {
        /// <summary>원본 옆에 꼬리말을 붙여 쓴다.</summary>
        NextToSource,

        /// <summary>정해 준 폴더에 같은 이름으로 쓴다.</summary>
        Folder,

        /// <summary>원본 자리에 덮어쓴다.</summary>
        Overwrite,
    }

    /// <summary>이 도구가 열 수 있는 그림 확장자.</summary>
    public static readonly string[] Extensions =
        [".png", ".jpg", ".jpeg", ".bmp", ".gif", ".tif", ".tiff"];

    /// <summary>파일 고르기 창에 걸 거르개.</summary>
    public const string FileFilter =
        "그림 파일|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.tif;*.tiff|모든 파일|*.*";

    /// <summary>나눌 수 있는 최대 행·열.</summary>
    public const int MaxSplit = 50;

    public sealed class Options
    {
        public SizeMode Mode { get; init; } = SizeMode.Width;

        /// <summary>맞출 픽셀. <see cref="SizeMode.Percent"/> 가 아닐 때 본다.</summary>
        public int Pixels { get; init; } = 1280;

        /// <summary>원래 크기 대비 퍼센트. <see cref="SizeMode.Percent"/> 일 때만 본다.</summary>
        public double Percent { get; init; } = 50;

        /// <summary>줄인 그림을 몇 행으로 나눌지. 1 이면 안 나눈다.</summary>
        public int Rows { get; init; } = 1;

        /// <summary>줄인 그림을 몇 열로 나눌지. 1 이면 안 나눈다.</summary>
        public int Columns { get; init; } = 1;

        /// <summary>조각 둘레에 여백을 두를지.</summary>
        public PadMode Pad { get; init; } = PadMode.None;

        /// <summary><see cref="PadMode.Fixed"/> 일 때 좌·우에 각각 두를 픽셀.</summary>
        public int PadX { get; init; }

        /// <summary><see cref="PadMode.Fixed"/> 일 때 위·아래에 각각 두를 픽셀.</summary>
        public int PadY { get; init; }

        /// <summary>여백을 채울 색.</summary>
        public PadColor PadFill { get; init; } = PadColor.Transparent;

        public OutputFormat Format { get; init; } = OutputFormat.KeepSource;

        /// <summary>JPEG 로 쓸 때만 쓰는 품질(1~100).</summary>
        public int JpegQuality { get; init; } = 85;

        public Destination Where { get; init; } = Destination.NextToSource;

        /// <summary><see cref="Destination.Folder"/> 일 때 쓸 폴더.</summary>
        public string? Folder { get; init; }

        /// <summary><see cref="Destination.NextToSource"/> 일 때 이름 뒤에 붙일 꼬리말.</summary>
        public string Suffix { get; init; } = "_small";

        /// <summary>줄일 것도 나눌 것도 없으면 건드리지 않는다.</summary>
        public bool SkipWhenNoGain { get; init; } = true;

        /// <summary>나누기를 시켰는지.</summary>
        public bool Splits => Rows > 1 || Columns > 1;
    }

    public sealed class Result
    {
        public required string SourcePath { get; init; }
        public int SourceWidth { get; set; }
        public int SourceHeight { get; set; }
        public long SourceBytes { get; set; }

        /// <summary>줄인 뒤 전체 크기. 나누기 전 크기다.</summary>
        public int ScaledWidth { get; set; }
        public int ScaledHeight { get; set; }

        /// <summary>조각 하나의 크기. 안 나눴으면 <see cref="ScaledWidth"/> 와 같다.</summary>
        public int PieceWidth { get; set; }
        public int PieceHeight { get; set; }

        /// <summary>써 놓은 파일들. 안 나눴으면 한 개다.</summary>
        public List<string> OutputPaths { get; } = [];

        /// <summary>써 놓은 파일 용량을 다 더한 값.</summary>
        public long OutputBytes { get; set; }

        /// <summary>줄일 것도 나눌 것도 없어 그냥 둔 경우.</summary>
        public bool Skipped { get; set; }

        public string? Error { get; set; }

        public string? OutputPath => OutputPaths.Count > 0 ? OutputPaths[0] : null;
        public int PieceCount => OutputPaths.Count;

        /// <summary>용량이 얼마나 줄었는지(0~1). 오히려 늘었으면 음수다.</summary>
        public double Saved => SourceBytes > 0 && OutputBytes > 0
            ? 1.0 - (double)OutputBytes / SourceBytes
            : 0;
    }

    /// <summary>미리 보기에 쓸 그림과 셈해 둔 크기.</summary>
    public sealed record Preview(
        BitmapSource Image,
        int SourceWidth,
        int SourceHeight,
        int TargetWidth,
        int TargetHeight);

    /// <summary>확장자만 보고 열어 볼 만한 그림인지 가린다.</summary>
    public static bool IsSupported(string path) =>
        Extensions.Contains(Path.GetExtension(path).ToLowerInvariant());

    /// <summary>
    /// 길이 <paramref name="total"/> 를 <paramref name="parts"/> 토막으로 자를 자리를 셈한다.
    /// </summary>
    /// <remarks>
    /// 딱 안 나눠떨어지면 앞쪽 토막이 한 픽셀씩 더 가져간다 — 그래야 한 줄도 안 흘린다.
    /// </remarks>
    public static List<(int Offset, int Length)> Slices(int total, int parts)
    {
        parts = Math.Clamp(parts, 1, Math.Max(1, total));

        var slices = new List<(int, int)>(parts);
        int each = total / parts, extra = total % parts, at = 0;
        for (int i = 0; i < parts; i++)
        {
            int length = each + (i < extra ? 1 : 0);
            slices.Add((at, length));
            at += length;
        }

        return slices;
    }

    /// <summary>줄이고 나면 전체가 몇 픽셀이 될지 셈한다. 미리 보기에 쓴다.</summary>
    public static (int Width, int Height) TargetSize(int sourceWidth, int sourceHeight, Options options)
    {
        if (sourceWidth <= 0 || sourceHeight <= 0) return (0, 0);

        int width = TargetWidth(sourceWidth, sourceHeight, options);
        return (width, HeightFor(sourceWidth, sourceHeight, width));
    }

    /// <summary>미리 보기에 쓸 그림을 읽는다. 원본 그대로 또는 줄인 결과로 푼다.</summary>
    /// <param name="cap">원본으로 볼 때 이 픽셀보다 넓게는 풀지 않는다 — 화면에 띄울 것뿐이다.</param>
    public static Preview LoadPreview(string path, Options options, bool asResult, int cap = 1400)
    {
        var bytes = File.ReadAllBytes(path);
        var (sw, sh) = ReadSize(bytes);
        var (tw, th) = TargetSize(sw, sh, options);

        int decodeWidth = asResult ? tw : Math.Min(sw, cap);
        var image = Decode(bytes, Math.Max(1, decodeWidth));
        return new Preview(image, sw, sh, tw, th);
    }

    /// <summary>만들어 놓은 조각을 목록에 걸 만한 작은 그림으로 읽는다.</summary>
    /// <remarks>딴 실에서 여러 장을 한꺼번에 읽으므로 얼린 채로(Freeze) 돌려준다.</remarks>
    public static BitmapSource LoadThumbnail(string path, int width) =>
        Decode(File.ReadAllBytes(path), Math.Max(1, width));

    /// <summary>그림 한 장을 줄이고(시켰으면 나눠) 쓴다. 실패해도 던지지 않고 <see cref="Result.Error"/> 에 담아 준다.</summary>
    public static Result Shrink(string path, Options options)
    {
        var result = new Result { SourcePath = path };
        try
        {
            var file = new FileInfo(path);
            if (!file.Exists)
            {
                result.Error = "파일이 없습니다";
                return result;
            }

            // 조각을 한 자리에 겹쳐 쓸 수는 없다. 나눌 거면 덮어쓰기를 막는다.
            if (options.Splits && options.Where == Destination.Overwrite)
            {
                result.Error = "나눌 때는 원본을 덮어쓸 수 없습니다";
                return result;
            }

            result.SourceBytes = file.Length;

            // 파일을 물고 있지 않도록 통째로 읽어 두고 그 위에서만 만진다.
            // 덮어쓰기 때 원본 손잡이가 남아 있으면 쓰다가 막힌다.
            var bytes = File.ReadAllBytes(path);

            var (sw, sh) = ReadSize(bytes);
            result.SourceWidth = sw;
            result.SourceHeight = sh;
            if (sw <= 0 || sh <= 0)
            {
                result.Error = "크기를 읽지 못했습니다";
                return result;
            }

            int targetWidth = TargetWidth(sw, sh, options);
            var codec = ResolveCodec(path, options.Format);

            // 크기도 그대로, 형식도 그대로, 나눌 것도 두를 것도 없으면 다시 써 봐야 얻을 게 없다.
            if (options.SkipWhenNoGain && targetWidth >= sw
                && options.Format == OutputFormat.KeepSource && !options.Splits
                && options.Pad == PadMode.None)
            {
                result.Skipped = true;
                result.ScaledWidth = result.PieceWidth = sw;
                result.ScaledHeight = result.PieceHeight = sh;
                return result;
            }

            var image = Decode(bytes, targetWidth);
            result.ScaledWidth = image.PixelWidth;
            result.ScaledHeight = image.PixelHeight;

            // JPEG 에는 투명이 없다. 나누기 전에 한 번만 섞어 둔다.
            BitmapSource whole = codec == Codec.Jpeg ? FlattenOnWhite(image) : image;

            var columns = Slices(whole.PixelWidth, options.Columns);
            var rows = Slices(whole.PixelHeight, options.Rows);
            bool splits = rows.Count > 1 || columns.Count > 1;

            var (padWidth, padHeight) = PieceSize(columns, rows, options);
            result.PieceWidth = padWidth;
            result.PieceHeight = padHeight;

            var fill = ResolvePad(options.PadFill, codec);
            int rowDigits = rows.Count.ToString().Length;
            int columnDigits = columns.Count.ToString().Length;

            for (int r = 0; r < rows.Count; r++)
            {
                for (int c = 0; c < columns.Count; c++)
                {
                    BitmapSource piece = splits
                        ? new CroppedBitmap(whole, new Int32Rect(columns[c].Offset, rows[r].Offset, columns[c].Length, rows[r].Length))
                        : whole;

                    if (options.Pad != PadMode.None)
                        piece = PadAround(piece, padWidth, padHeight, fill);

                    string tag = splits
                        ? $"_r{(r + 1).ToString().PadLeft(rowDigits, '0')}c{(c + 1).ToString().PadLeft(columnDigits, '0')}"
                        : "";

                    // 인코더는 한 번 쓰고 버린다 — 조각마다 새로 잡는다.
                    var encoder = MakeEncoder(codec, options.JpegQuality);
                    encoder.Frames.Add(BitmapFrame.Create(piece));

                    string outPath = BuildOutputPath(path, codec, options, tag);
                    WriteAtomically(encoder, outPath);

                    result.OutputPaths.Add(outPath);
                    result.OutputBytes += new FileInfo(outPath).Length;
                }
            }

            return result;
        }
        catch (Exception ex)
        {
            result.Error = ex.Message;
            return result;
        }
    }

    // ── 속살 ────────────────────────────────────────────────────────────────

    private enum Codec { Jpeg, Png, Bmp, Gif, Tiff }

    /// <summary>픽셀은 풀지 않고 머리말만 읽어 크기를 본다.</summary>
    private static (int Width, int Height) ReadSize(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes, writable: false);
        var frame = BitmapFrame.Create(stream, BitmapCreateOptions.DelayCreation, BitmapCacheOption.None);
        return (frame.PixelWidth, frame.PixelHeight);
    }

    private static int TargetWidth(int sw, int sh, Options options)
    {
        double width = options.Mode switch
        {
            SizeMode.Width => options.Pixels,
            // 칸마다 이만큼씩 가져가야 하니 열 수를 곱한 게 전체 가로다.
            SizeMode.CellWidth => (double)options.Pixels * Math.Max(1, options.Columns),
            SizeMode.Height => WidthForHeight(sw, sh, options.Pixels),
            SizeMode.LongestSide => sw >= sh ? options.Pixels : WidthForHeight(sw, sh, options.Pixels),
            _ => sw * Math.Clamp(options.Percent, 1, 100) / 100.0,
        };

        // 줄이는 도구다 — 원본보다 키우지는 않는다.
        return Math.Clamp((int)Math.Round(width), 1, sw);
    }

    /// <summary>
    /// 가로를 <paramref name="width"/> 로 시켰을 때 디코더가 내놓을 세로.
    /// </summary>
    /// <remarks>WIC 는 반올림이 아니라 내림으로 잡는다 — 어림셈도 똑같이 해야 미리 보기가 안 어긋난다.</remarks>
    private static int HeightFor(int sw, int sh, int width) =>
        Math.Max(1, (int)Math.Floor(sh * (double)width / sw));

    /// <summary>
    /// 세로를 <paramref name="wantHeight"/> 로 만들려면 가로를 몇으로 시켜야 하는지 찾는다.
    /// </summary>
    /// <remarks>
    /// 비례식으로 곧장 나눈 값은 내림 때문에 한 픽셀 모자라기 일쑤다(1600×900 을 세로 300 →
    /// 가로 533 이면 세로가 299 로 떨어진다). 언저리를 훑어 딱 맞는 가로를 집는다.
    /// </remarks>
    private static int WidthForHeight(int sw, int sh, int wantHeight)
    {
        int guess = Math.Max(1, (int)Math.Round(wantHeight * (double)sw / sh));
        int best = guess, bestGap = int.MaxValue;

        for (int width = Math.Max(1, guess - 2); width <= guess + 2; width++)
        {
            int gap = Math.Abs(HeightFor(sw, sh, width) - wantHeight);
            if (gap >= bestGap) continue;

            best = width;
            bestGap = gap;
        }

        return best;
    }

    private static BitmapSource Decode(byte[] bytes, int targetWidth)
    {
        using var stream = new MemoryStream(bytes, writable: false);
        var image = new BitmapImage();
        image.BeginInit();
        image.StreamSource = stream;
        image.DecodePixelWidth = targetWidth;   // 세로는 비율대로 따라온다
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
        image.EndInit();
        image.Freeze();
        return image;
    }

    private static Codec ResolveCodec(string path, OutputFormat format) => format switch
    {
        OutputFormat.Jpeg => Codec.Jpeg,
        OutputFormat.Png => Codec.Png,
        _ => Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".jpg" or ".jpeg" => Codec.Jpeg,
            ".bmp" => Codec.Bmp,
            ".gif" => Codec.Gif,
            ".tif" or ".tiff" => Codec.Tiff,
            _ => Codec.Png,
        },
    };

    private static string ExtensionOf(Codec codec) => codec switch
    {
        Codec.Jpeg => ".jpg",
        Codec.Bmp => ".bmp",
        Codec.Gif => ".gif",
        Codec.Tiff => ".tif",
        _ => ".png",
    };

    private static BitmapEncoder MakeEncoder(Codec codec, int quality) => codec switch
    {
        Codec.Jpeg => new JpegBitmapEncoder { QualityLevel = Math.Clamp(quality, 1, 100) },
        Codec.Bmp => new BmpBitmapEncoder(),
        Codec.Gif => new GifBitmapEncoder(),
        Codec.Tiff => new TiffBitmapEncoder(),
        _ => new PngBitmapEncoder(),
    };

    /// <summary>
    /// 비쳐 보이는 자리를 흰 종이 위에 얹은 셈으로 섞어 없앤다.
    /// </summary>
    /// <remarks>
    /// JPEG 에는 투명이 없다. 그냥 넘기면 비쳐 보이던 자리가 시커멓게 나오므로 미리 섞어 둔다.
    /// 그림판을 하나 더 그리는 <c>RenderTargetBitmap</c> 대신 픽셀을 손으로 섞는다 — 그쪽은
    /// UI 실이 아닌 데서 돌리면 탈이 나는데, 이 일은 딴 실에서 여러 장을 한꺼번에 돌린다.
    /// </remarks>
    private static BitmapSource FlattenOnWhite(BitmapSource image)
    {
        // Bgra32 로 맞춰 두면 알파가 안 곱해진 상태라 그대로 섞을 수 있다.
        BitmapSource bgra = image.Format == PixelFormats.Bgra32
            ? image
            : new FormatConvertedBitmap(image, PixelFormats.Bgra32, null, 0);

        int w = bgra.PixelWidth, h = bgra.PixelHeight;
        int srcStride = w * 4, dstStride = w * 3;
        var src = new byte[srcStride * h];
        bgra.CopyPixels(src, srcStride, 0);

        var dst = new byte[dstStride * h];
        for (int y = 0; y < h; y++)
        {
            int s = y * srcStride, d = y * dstStride;
            for (int x = 0; x < w; x++, s += 4, d += 3)
            {
                int a = src[s + 3];
                if (a == 255)
                {
                    dst[d + 0] = src[s + 0];
                    dst[d + 1] = src[s + 1];
                    dst[d + 2] = src[s + 2];
                    continue;
                }

                int bg = 255 * (255 - a);
                dst[d + 0] = (byte)((src[s + 0] * a + bg) / 255);
                dst[d + 1] = (byte)((src[s + 1] * a + bg) / 255);
                dst[d + 2] = (byte)((src[s + 2] * a + bg) / 255);
            }
        }

        double dpiX = image.DpiX > 0 ? image.DpiX : 96;
        double dpiY = image.DpiY > 0 ? image.DpiY : 96;
        var flat = BitmapSource.Create(w, h, dpiX, dpiY, PixelFormats.Bgr24, null, dst, dstStride);
        flat.Freeze();
        return flat;
    }

    /// <summary>
    /// 여백까지 두르고 난 조각 한 장의 크기.
    /// </summary>
    /// <remarks>
    /// 정사각형은 격자에서 제일 긴 변을 한 변으로 삼는다. 조각마다 제 크기에 맞춰 따로 재면
    /// 안 나눠떨어질 때 한 픽셀씩 어긋나 조각들이 서로 다른 크기가 된다 — 그러면 늘어놓지 못한다.
    /// </remarks>
    public static (int Width, int Height) PieceSize(
        List<(int Offset, int Length)> columns,
        List<(int Offset, int Length)> rows,
        Options options)
    {
        int width = columns[0].Length, height = rows[0].Length;

        return options.Pad switch
        {
            PadMode.Square => Square(columns.Max(s => s.Length), rows.Max(s => s.Length)),
            PadMode.Fixed => (width + Math.Max(0, options.PadX) * 2, height + Math.Max(0, options.PadY) * 2),
            _ => (width, height),
        };

        static (int, int) Square(int widest, int tallest)
        {
            int side = Math.Max(widest, tallest);
            return (side, side);
        }
    }

    /// <summary>줄이고 나눈 뒤 조각이 몇 픽셀이 될지 셈한다. 미리 보기에 쓴다.</summary>
    public static (int Width, int Height) PieceSize(int sourceWidth, int sourceHeight, Options options)
    {
        var (tw, th) = TargetSize(sourceWidth, sourceHeight, options);
        if (tw <= 0) return (0, 0);

        return PieceSize(Slices(tw, options.Columns), Slices(th, options.Rows), options);
    }

    /// <summary>조각을 정해진 크기 한가운데에 놓고 둘레를 <paramref name="fill"/> 로 채운다.</summary>
    /// <remarks>
    /// 그림판을 하나 더 그리는 <c>RenderTargetBitmap</c> 대신 픽셀을 손으로 옮긴다 — 그쪽은
    /// UI 실이 아닌 데서 돌리면 탈이 난다. 옮기기는 줄 단위 통짜 복사라 한 장이 금방이다.
    /// </remarks>
    private static BitmapSource PadAround(BitmapSource piece, int width, int height, Color fill)
    {
        BitmapSource src = piece.Format == PixelFormats.Bgra32
            ? piece
            : new FormatConvertedBitmap(piece, PixelFormats.Bgra32, null, 0);

        int w = Math.Min(src.PixelWidth, width), h = Math.Min(src.PixelHeight, height);
        int srcStride = src.PixelWidth * 4;
        var pixels = new byte[srcStride * src.PixelHeight];
        src.CopyPixels(pixels, srcStride, 0);

        int stride = width * 4;
        var canvas = new byte[stride * height];
        if (fill.A != 0)
        {
            for (int i = 0; i < canvas.Length; i += 4)
            {
                canvas[i + 0] = fill.B;
                canvas[i + 1] = fill.G;
                canvas[i + 2] = fill.R;
                canvas[i + 3] = fill.A;
            }
        }

        int left = (width - w) / 2, top = (height - h) / 2;
        for (int y = 0; y < h; y++)
            Buffer.BlockCopy(pixels, y * srcStride, canvas, (top + y) * stride + left * 4, w * 4);

        double dpiX = piece.DpiX > 0 ? piece.DpiX : 96;
        double dpiY = piece.DpiY > 0 ? piece.DpiY : 96;
        var padded = BitmapSource.Create(width, height, dpiX, dpiY, PixelFormats.Bgra32, null, canvas, stride);
        padded.Freeze();
        return padded;
    }

    /// <summary>JPEG 에는 투명이 없다 — 비워 두라고 해도 흰 종이를 깔아 준다.</summary>
    private static Color ResolvePad(PadColor pad, Codec codec) => pad switch
    {
        PadColor.White => Colors.White,
        PadColor.Black => Colors.Black,
        _ => codec == Codec.Jpeg ? Colors.White : Colors.Transparent,
    };

    /// <param name="tag">나눈 조각이면 <c>_r1c2</c> 같은 자리표. 안 나눴으면 빈 문자열.</param>
    private static string BuildOutputPath(string path, Codec codec, Options options, string tag)
    {
        if (options.Where == Destination.Overwrite) return path;

        string name = Path.GetFileNameWithoutExtension(path);
        string ext = ExtensionOf(codec);
        string dir = options.Where == Destination.Folder && !string.IsNullOrWhiteSpace(options.Folder)
            ? options.Folder
            : Path.GetDirectoryName(path) ?? ".";

        // 폴더를 따로 정했으면 이름을 그대로 쓴다. 원본 옆에 쓸 때만 꼬리말을 붙인다 —
        // 안 붙이면 형식이 같을 때 원본을 덮어 버린다.
        string suffix = options.Where == Destination.NextToSource ? options.Suffix : "";
        string candidate = Path.Combine(dir, name + suffix + tag + ext);

        // 폴더를 따로 정했는데 하필 원본과 같은 자리라면 그래도 덮지 않게 꼬리말을 붙인다.
        // 나눈 조각은 자리표가 이미 이름을 갈라 놓아 부딪히지 않는다.
        if (tag.Length == 0 && options.Where == Destination.Folder && SamePath(candidate, path))
            candidate = Path.Combine(dir, name + options.Suffix + ext);

        return candidate;
    }

    private static bool SamePath(string a, string b)
    {
        try
        {
            return string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>임시 파일에 다 쓴 뒤 자리를 바꾼다 — 쓰다 말면 원본이 반쪽으로 남는다.</summary>
    private static void WriteAtomically(BitmapEncoder encoder, string outPath)
    {
        string dir = Path.GetDirectoryName(outPath) ?? ".";
        Directory.CreateDirectory(dir);

        string temp = Path.Combine(dir, Path.GetFileName(outPath) + ".shrink.tmp");
        try
        {
            using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                encoder.Save(stream);
            }

            File.Move(temp, outPath, overwrite: true);
        }
        catch
        {
            if (File.Exists(temp))
            {
                try { File.Delete(temp); } catch { /* 치우다 실패한 건 넘긴다 */ }
            }

            throw;
        }
    }
}
