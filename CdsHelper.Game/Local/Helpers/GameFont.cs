using System.IO;
using System.Text;

namespace CdsHelper.Game.Local.Helpers;

/// <summary>
/// 게임 비트맵 글꼴 — 화면에 찍히는 것과 똑같은 글자를 얻는다.
/// </summary>
/// <remarks>
/// 게임의 메뉴 글자는 윈도 글꼴이 아니라 게임 폴더의 글꼴 파일 두 개에서 온다.
/// <code>
///   ALL_FONT.16P  30바이트 레코드 3,478개
///     +0  u16 코드   리틀엔디언. 값은 KS X 1001 두 바이트를 빅엔디언으로 본 수
///                    (CP949 로 "가" = B0 A1 이면 코드 0xB0A1, 파일에는 A1 B0 으로 들어 있다)
///     +2  28바이트   16폭 x 14행 1bpp, 한 행이 2바이트, MSB 가 왼쪽
///     코드 범위 0xA1A1~0xC8FE — 기호와 완성형 한글 2,350자. 한자는 없다.
///
///   ANKFONT.DAT   16바이트 글리프 96개
///     8폭 x 16행 1bpp, 한 행이 1바이트. ASCII 0x20(빈칸)부터 0x7F 까지 차례로.
/// </code>
/// 게임도 이 둘을 이 차례로 읽는다 — <c>0x004109C3</c> / <c>0x004109CD</c>
/// (문자열 <c>"C:ALL_FONT.16P"</c> = <c>0x535CCC</c> · <c>"C:ANKFONT.DAT"</c> = <c>0x535CBC</c>).
///
/// 한글과 ASCII 는 높이가 다르다(14 와 16). 띠에 얹을 때는 <see cref="UiSprites.BandHeight"/>
/// 안에서 세로 가운데로 맞춘다 — 게임이 그렇게 한다.
///
/// 색은 <see cref="GamePalette"/> 색인이다. 화면에서 되짚어 보면 타이틀 글자는 <b>26</b>,
/// 베이지 버튼 글자는 <b>17</b> 이다.
/// </remarks>
public sealed class GameFont
{
    private const int HanRecord = 30, HanWidth = 16, HanHeight = 14;
    private const ushort HanLow = 0xA1A1, HanHigh = 0xC8FE;
    private const int AnkLow = 0x20, AnkCount = 96, AnkWidth = 8, AnkHeight = 16;

    /// <summary>타이틀 띠 글자색(공용 색표 색인). 크림 <c>196,180,148</c>.</summary>
    public const byte TitleColor = 26;

    /// <summary>
    /// 글자에만 쓰는 먹색 칸. 공용 색표에 <c>1,1,1</c> 이 없어 <b>글자 셈에서만</b> 이 번호를
    /// <c>#010101</c> 로 읽는다 — 그림들은 색표를 그대로 읽으므로 색표 자체는 건드리지 않는다.
    /// </summary>
    public const byte InkColor = 254;

    /// <summary>
    /// 베이지 띠(메뉴 줄·단추) 글자색. 게임은 짙은 갈색(<c>341C14</c>)이 아니라 <c>#010101</c> 로 찍는다.
    /// </summary>
    public const byte ButtonColor = InkColor;

    /// <summary>흰빛 글자색. 공용 색표에서 가장 흰 <c>244,232,224</c> 다.</summary>
    public const byte WhiteColor = 10;

    /// <summary>글자 그림자색. 거의 검정인 <c>12,7,6</c> 이다.</summary>
    public const byte ShadowColor = 74;

    /// <summary>
    /// 검정 글자색. 그림자와 같은 칸이다 — 강청색 판(인물정보)은 글씨가 검정이다.
    /// </summary>
    public const byte BlackColor = ShadowColor;

    private readonly byte[] _han;
    private readonly short[] _index;      // 코드 - HanLow → 레코드 번호, 없으면 -1
    private readonly byte[]? _ank;

    private GameFont(byte[] han, short[] index, byte[]? ank)
    {
        _han = han;
        _index = index;
        _ank = ank;
    }

    /// <summary>왜 못 열었는지. 잘 열렸으면 빈 문자열.</summary>
    public static string LastError { get; private set; } = "";

    /// <summary>
    /// 글꼴 두 개를 읽는다. <b>asset 을 먼저 본다</b> — 게임 폴더가 없어도 앱에 실려 있다
    /// (<see cref="ResolvePath"/>). 한글 글꼴이 없으면 null — ANK 는 없어도 한글은
    /// 찍히므로 그것만 빠진 채로 연다.
    /// </summary>
    public static GameFont? Open(string gameDirectory)
    {
        LastError = "";
        var hanPath = ResolvePath(gameDirectory, "ALL_FONT.16P");
        if (hanPath == null) { LastError = "ALL_FONT.16P 를 asset 에도 게임 폴더에도 못 찾았습니다"; return null; }

        byte[] han;
        try { han = File.ReadAllBytes(hanPath); }
        catch (IOException ex) { LastError = ex.Message; return null; }

        if (han.Length < HanRecord) { LastError = "ALL_FONT.16P 가 너무 짧습니다"; return null; }

        var index = new short[HanHigh - HanLow + 1];
        Array.Fill(index, (short)-1);
        int records = han.Length / HanRecord;
        for (int i = 0; i < records; i++)
        {
            int code = han[i * HanRecord] | (han[i * HanRecord + 1] << 8);
            if (code >= HanLow && code <= HanHigh) index[code - HanLow] = (short)i;
        }

        byte[]? ank = null;
        var ankPath = ResolvePath(gameDirectory, "ANKFONT.DAT");
        if (ankPath != null)
        {
            try
            {
                var raw = File.ReadAllBytes(ankPath);
                if (raw.Length >= AnkCount * AnkHeight) ank = raw;
            }
            catch (IOException) { /* 영문만 못 쓸 뿐이다 */ }
        }

        return new GameFont(han, index, ank);
    }

    /// <summary>
    /// asset 에 실어 둔 것을 먼저 본다 — 게임 폴더가 없어도 늘 있다. 거기 없을 때만
    /// 게임 폴더에서 찾는다. 어느 쪽에도 없으면 null.
    /// </summary>
    private static string? ResolvePath(string gameDirectory, string fileName)
    {
        var asset = Path.Combine(AppContext.BaseDirectory, "asset", fileName);
        if (File.Exists(asset)) return asset;

        var game = Path.Combine(gameDirectory, fileName);
        return File.Exists(game) ? game : null;
    }

    private static Encoding? _cp949;

    private static Encoding Cp949
    {
        get
        {
            if (_cp949 != null) return _cp949;
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            return _cp949 = Encoding.GetEncoding(949);
        }
    }

    /// <summary>
    /// 한 글자를 <paramref name="mask"/>(폭 <see cref="MaxWidth"/> 로 재는 칸)에
    /// 1=획 / 0=바탕으로 푼다. 글꼴에 없는 글자면 false — 그 자리는 건너뛴다.
    /// </summary>
    public bool TryGlyph(char ch, byte[] mask, out int width, out int height)
    {
        width = height = 0;
        Array.Clear(mask);

        var bytes = Cp949.GetBytes([ch]);
        if (bytes.Length == 0) return false;

        if (bytes.Length >= 2)
        {
            int code = (bytes[0] << 8) | bytes[1];
            if (code < HanLow || code > HanHigh) return false;
            short record = _index[code - HanLow];
            if (record < 0) return false;

            int at = record * HanRecord + 2;
            for (int r = 0; r < HanHeight; r++)
            {
                int bits = (_han[at + r * 2] << 8) | _han[at + r * 2 + 1];
                for (int c = 0; c < HanWidth; c++)
                    mask[r * MaxWidth + c] = (byte)((bits >> (15 - c)) & 1);
            }
            width = HanWidth;
            height = HanHeight;
            return true;
        }

        int k = bytes[0];
        if (_ank == null || k < AnkLow || k >= AnkLow + AnkCount) return false;
        for (int r = 0; r < AnkHeight; r++)
        {
            int bits = _ank[(k - AnkLow) * AnkHeight + r];
            for (int c = 0; c < AnkWidth; c++)
                mask[r * MaxWidth + c] = (byte)((bits >> (7 - c)) & 1);
        }
        width = AnkWidth;
        height = AnkHeight;
        return true;
    }

    /// <summary>글리프 칸의 폭·높이. 한글 16x14 와 ASCII 8x16 이 다 들어간다.</summary>
    public const int MaxWidth = 16, MaxHeight = 16;

    /// <summary>문자열을 찍는 데 드는 폭(픽셀). 없는 글자는 0폭으로 센다.</summary>
    public int TextWidth(string? text)
    {
        if (string.IsNullOrEmpty(text)) return 0;
        var mask = new byte[MaxWidth * MaxHeight];
        int total = 0;
        foreach (var ch in text)
            if (TryGlyph(ch, mask, out int w, out _)) total += w;
        return total;
    }

    /// <summary>
    /// 글자를 <paramref name="height"/> 높이 안에 세로 가운데로 찍어 BGRA 로 돌려준다.
    /// 바탕은 비침(알파 0)이라 띠 위에 그대로 겹치면 된다. 글자가 없으면 null.
    /// </summary>
    /// <param name="shadow">
    /// 오른쪽 아래로 한 점 그림자. 게임도 그림자를 먼저 깔고 그 위에 본 글자를 찍는다 —
    /// 획이 겹쳐도 본 글자가 이긴다.
    /// </param>
    public uint[]? Render(string? text, byte color, bool shadow, byte shadowColor,
                          int height, out int width)
    {
        width = TextWidth(text);
        if (width <= 0 || height <= 0) return null;

        int stride = width + (shadow ? 1 : 0);
        var bgra = new uint[stride * height];
        var mask = new byte[MaxWidth * MaxHeight];

        for (int pass = shadow ? 0 : 1; pass < 2; pass++)
        {
            int off = pass == 0 ? 1 : 0;
            uint argb = Argb(pass == 0 ? shadowColor : color);
            int x = 0;
            foreach (var ch in text!)
            {
                if (!TryGlyph(ch, mask, out int gw, out int gh)) continue;
                int top = (height - gh) / 2;
                for (int r = 0; r < gh; r++)
                {
                    int yy = top + r + off;
                    if (yy < 0 || yy >= height) continue;
                    for (int c = 0; c < gw; c++)
                    {
                        if (mask[r * MaxWidth + c] == 0) continue;
                        int xx = x + c + off;
                        if (xx < 0 || xx >= stride) continue;
                        bgra[yy * stride + xx] = argb;
                    }
                }
                x += gw;
            }
        }

        width = stride;
        return bgra;
    }

    private static uint Argb(byte index) => TextArgb(index);

    /// <summary>글자색 번호의 ARGB. <see cref="InkColor"/> 만 색표 밖의 <c>#010101</c> 이다.</summary>
    public static uint TextArgb(byte index)
    {
        if (index == InkColor) return 0xFF010101u;
        int i = index * 3;
        return (uint)(0xFF << 24 | GamePalette.Rgb[i] << 16
                      | GamePalette.Rgb[i + 1] << 8 | GamePalette.Rgb[i + 2]);
    }
}
