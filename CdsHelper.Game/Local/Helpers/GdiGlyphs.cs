using System.Runtime.InteropServices;

namespace CdsHelper.Game.Local.Helpers;

/// <summary>
/// 윈도 글꼴로 한 글자씩 찍는 글리프 — 일본어판 원본이 그리는 길이다.
/// </summary>
/// <remarks>
/// 원본 일본어판(CDS95.EXE)에는 글꼴 파일이 없다. <c>0x004AC2FD</c> 에서
/// <c>CreateFontA(-16, 8, …, SHIFTJIS_CHARSET, …, "ＭＳ ゴシック")</c> 를 16x16 1bpp DIB 에 골라 두고,
/// 글자마다 비트맵을 비운 뒤 <c>TextOutA</c> 로 한 자를 찍어 그 점을 옮겨 쓴다(두 바이트 글자 16폭, 한 바이트 8폭).
/// 여기서도 GDI 로 똑같이 찍는다 — WPF 로 그리면 가장자리가 부드러워져 게임 글자와 결이 다르다.
///
/// 한국어판 글꼴(<see cref="GameFont"/>)에는 한자가 없다. 일본어로 켰을 때는 한글이 아닌 글자를 다 이것으로 찍고,
/// 한국어일 때도 한국어 글꼴에 없는 글자(한자 따위)는 이것으로 메운다.
/// MS 고딕이 없는 PC(일본어 글꼴을 안 깐 윈도)는 BIZ UD고딕 · 유 고딕으로 물러선다. 다 없으면 못 찍는다.
/// </remarks>
internal static class GdiGlyphs
{
    /// <summary>글자 칸 — 높이 16, 전각 16폭 · 반각 8폭.</summary>
    public const int Size = 16;

    private static readonly Dictionary<char, (byte[] Mask, int Width)?> Cache = [];
    private static readonly object Gate = new();

    private static IntPtr _dc, _bitmap, _font, _bits;
    private static bool _tried;

    /// <summary>
    /// 그 글자를 <paramref name="mask"/>(폭 <see cref="GameFont.MaxWidth"/> 로 재는 칸)에 1=획 / 0=바탕으로 푼다.
    /// 글꼴에 없는 글자거나 GDI 를 못 쓰면 false.
    /// </summary>
    public static bool TryGlyph(char ch, byte[] mask, out int width, out int height)
    {
        width = height = 0;
        (byte[] Mask, int Width)? got;
        lock (Gate)
        {
            if (!Cache.TryGetValue(ch, out got))
                Cache[ch] = got = Draw(ch);
        }
        if (got is not { } glyph) return false;

        Array.Copy(glyph.Mask, mask, Math.Min(glyph.Mask.Length, mask.Length));
        width = glyph.Width;
        height = Size;
        return true;
    }

    private static (byte[] Mask, int Width)? Draw(char ch)
    {
        if (!Ready()) return null;

        // 글꼴에 없는 글자는 GDI 가 네모나 딴 글꼴로 바꿔 찍는다 — 없는 것은 없다고 한다.
        var index = new ushort[1];
        if (GetGlyphIndicesW(_dc, ch.ToString(), 1, index, GgiMarkNonexistingGlyphs) == GdiError
            || index[0] == 0xFFFF)
            return null;

        int width = GetTextExtentPoint32W(_dc, ch.ToString(), 1, out var size) ? Math.Clamp(size.cx, 1, Size) : Size;
        width = width <= Size / 2 ? Size / 2 : Size;

        // 비우고 한 자를 찍는다 — 바탕 검정(0), 글자 흰빛(1).
        Marshal.Copy(new byte[Stride * Size], 0, _bits, Stride * Size);
        TextOutW(_dc, 0, 0, ch.ToString(), 1);
        GdiFlush();

        var raw = new byte[Stride * Size];
        Marshal.Copy(_bits, raw, 0, raw.Length);
        var mask = new byte[GameFont.MaxWidth * GameFont.MaxHeight];
        for (int r = 0; r < Size; r++)
            for (int c = 0; c < width; c++)
                mask[r * GameFont.MaxWidth + c] = (byte)((raw[r * Stride + c / 8] >> (7 - c % 8)) & 1);
        return (mask, width);
    }

    /// <summary>1bpp 16폭 한 줄의 바이트 수(4바이트로 맞춘다).</summary>
    private const int Stride = 4;

    /// <summary>DC · 16x16 1bpp DIB · 글꼴을 한 번 지어 둔다. 못 지으면 다시 시도하지 않는다.</summary>
    private static bool Ready()
    {
        if (_tried) return _font != IntPtr.Zero;
        _tried = true;
        try
        {
            _dc = CreateCompatibleDC(IntPtr.Zero);
            if (_dc == IntPtr.Zero) return false;

            var info = new BitmapInfo
            {
                Header = new BitmapInfoHeader
                {
                    Size = (uint)Marshal.SizeOf<BitmapInfoHeader>(),
                    Width = Size,
                    Height = -Size,          // 위에서 아래로
                    Planes = 1,
                    BitCount = 1,
                },
                Black = 0x00000000,
                White = 0x00FFFFFF,
            };
            _bitmap = CreateDIBSection(_dc, ref info, 0, out _bits, IntPtr.Zero, 0);
            if (_bitmap == IntPtr.Zero) return false;
            SelectObject(_dc, _bitmap);

            foreach (string face in new[] { "ＭＳ ゴシック", "MS Gothic", "BIZ UDGothic", "Yu Gothic" })
            {
                var font = CreateFontW(-Size, 0, 0, 0, 400, 0, 0, 0, ShiftJisCharset, 0, 0, NonAntialiasedQuality, 0, face);
                if (font == IntPtr.Zero) continue;
                SelectObject(_dc, font);
                // 고른 이름이 정말 깔려 있는지 본다 — 없으면 GDI 가 딴 글꼴을 내준다.
                var name = new char[64];
                int n = GetTextFaceW(_dc, name.Length, name);
                string got = n > 0 ? new string(name, 0, Math.Min(n, name.Length)).TrimEnd('\0') : "";
                if (got == face || (face.Contains("Gothic") && got.Contains("Gothic")) || got.Contains("ゴシック"))
                {
                    _font = font;
                    break;
                }
                DeleteObject(font);
            }
            if (_font == IntPtr.Zero) return false;

            SetTextColor(_dc, 0x00FFFFFF);
            SetBkColor(_dc, 0x00000000);
            SetBkMode(_dc, Opaque);
            return true;
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            return false;
        }
    }

    private const uint GdiError = 0xFFFFFFFF;
    private const uint GgiMarkNonexistingGlyphs = 1;
    private const uint ShiftJisCharset = 128;
    private const uint NonAntialiasedQuality = 3;
    private const int Opaque = 2;

    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapInfoHeader
    {
        public uint Size;
        public int Width, Height;
        public ushort Planes, BitCount;
        public uint Compression, SizeImage;
        public int XPelsPerMeter, YPelsPerMeter;
        public uint ClrUsed, ClrImportant;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapInfo
    {
        public BitmapInfoHeader Header;
        public uint Black, White;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SizeStruct { public int cx, cy; }

    [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleDC(IntPtr dc);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateDIBSection(IntPtr dc, ref BitmapInfo info, uint usage, out IntPtr bits, IntPtr section, uint offset);
    [DllImport("gdi32.dll")] private static extern IntPtr SelectObject(IntPtr dc, IntPtr obj);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr obj);
    [DllImport("gdi32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateFontW(int height, int width, int escapement, int orientation, int weight,
                                             uint italic, uint underline, uint strikeOut, uint charSet, uint outPrecision,
                                             uint clipPrecision, uint quality, uint pitchAndFamily, string face);
    [DllImport("gdi32.dll", CharSet = CharSet.Unicode)] private static extern int GetTextFaceW(IntPtr dc, int count, [Out] char[] name);
    [DllImport("gdi32.dll", CharSet = CharSet.Unicode)] private static extern uint GetGlyphIndicesW(IntPtr dc, string text, int count, [Out] ushort[] indices, uint flags);
    [DllImport("gdi32.dll", CharSet = CharSet.Unicode)] private static extern bool GetTextExtentPoint32W(IntPtr dc, string text, int count, out SizeStruct size);
    [DllImport("gdi32.dll", CharSet = CharSet.Unicode)] private static extern bool TextOutW(IntPtr dc, int x, int y, string text, int count);
    [DllImport("gdi32.dll")] private static extern uint SetTextColor(IntPtr dc, uint color);
    [DllImport("gdi32.dll")] private static extern uint SetBkColor(IntPtr dc, uint color);
    [DllImport("gdi32.dll")] private static extern int SetBkMode(IntPtr dc, int mode);
    [DllImport("gdi32.dll")] private static extern bool GdiFlush();
}
