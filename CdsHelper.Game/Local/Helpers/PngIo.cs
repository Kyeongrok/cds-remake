using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace CdsHelper.Game.Local.Helpers;

/// <summary>BGRA 그림을 스트림으로 PNG 로 쓰고 읽는다 — zip 항목처럼 파일 자리가 없는 곳에 쓴다.</summary>
public static class PngIo
{
    /// <remarks>
    /// <see cref="PngBitmapEncoder"/> 는 쓰면서 앞으로 되감아 청크 길이를 채워 넣어 <b>되감을 수
    /// 있는 스트림</b>이 있어야 한다 — zip 항목 스트림처럼 앞으로만 쓸 수 있는 자리에 바로
    /// 물리면 <c>NotSupportedException</c> 이 난다. 그래서 메모리에 먼저 굽고 그대로 복사한다.
    /// </remarks>
    public static void Write(Stream stream, uint[] bgra, int width, int height)
    {
        var bmp = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, bgra, width * 4);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bmp));

        using var buffer = new MemoryStream();
        encoder.Save(buffer);
        buffer.Position = 0;
        buffer.CopyTo(stream);
    }

    public static (uint[] Bgra, int Width, int Height) Read(Stream stream)
    {
        var decoder = new PngBitmapDecoder(stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
        var src = decoder.Frames[0];
        BitmapSource converted = src.Format == PixelFormats.Bgra32
            ? src : new FormatConvertedBitmap(src, PixelFormats.Bgra32, null, 0);
        var bgra = new uint[converted.PixelWidth * converted.PixelHeight];
        converted.CopyPixels(bgra, converted.PixelWidth * 4, 0);
        return (bgra, converted.PixelWidth, converted.PixelHeight);
    }
}
