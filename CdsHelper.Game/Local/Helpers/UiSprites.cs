using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CdsHelper.Support.Local.Helpers;

namespace CdsHelper.Game.Local.Helpers;

/// <summary>게임 메뉴 띠의 세 가지 무늬.</summary>
public enum BandStyle
{
    /// <summary>진홍 장식 — 메뉴 <b>타이틀</b>에 쓴다.</summary>
    Title = 0,

    /// <summary>베이지 — 보통 버튼.</summary>
    Button = 1,

    /// <summary>회녹색 — 다른 상태 버튼.</summary>
    Alt = 2,
}

/// <summary>
/// 게임 폴더의 MISC.CDS — 화면 장식 조각들. 메뉴 타이틀·버튼 띠가 여기서 온다.
/// </summary>
/// <remarks>
/// 도시 그림·초상화와 같은 LS12 아카이브다.
/// <code>
///   파트 0   16 x  96   테두리 상자 조각
///   파트 3   16 x  96   16x16 아이콘 여섯 — X · ↑ · ↓ · 빈칸 둘 · 계산기
///   파트 4   2,880바이트  메뉴 띠 껍데기          ← 이것을 쓴다
///   파트 7   24 x 240   숫자 글꼴 0~9  ← 계산기 판이 이것으로 값을 찍는다
///   파트 8  592 x 448   양피지 바탕
///   파트 11  48 x  48   닻
/// </code>
///
/// <b>파트 4 는 한 장짜리 그림이 아니다.</b> 한 벌 960바이트씩 <b>세 벌</b>이고,
/// 한 벌 안은 조각 셋이다. 조각마다 <b>제 폭으로</b> 위에서 아래로 담긴 8bpp 색인이다.
/// <code>
///   +0     16폭 x 24행 (384바이트)   왼끝
///   +384    8폭 x 24행 (192바이트)   가운데  ← 폭만큼 옆으로 되풀이한다
///   +576   16폭 x 24행 (384바이트)   오른끝
/// </code>
/// 그래서 띠는 늘 <b>24행</b>이고 폭은 <c>16 + 8*n + 16</c> 이다.
///
/// 전체를 한 폭으로 보고 가로로 자르면 안 된다 — 조각마다 폭이 달라서 엉뚱한 경계가
/// 잡힌다(16x180 으로 보고 y36·60·96 을 경계로 삼았던 적이 있는데 전부 헛것이었다).
///
/// 게임도 똑같이 짓는다 — 조각 꺼내기 <c>0x00463710(벌, 조각)</c>, 조각 시작 열 표
/// <c>0x00552898</c> = <c>{0, 16, 24}</c>, 띠 짓는 자리 <c>0x0041F606</c>.
/// 파트 4 는 <c>0x00463590</c> 이 게임 시작 때 객체 <c>0x005AA3B8+0x14</c> 로 읽어 둔다.
///
/// 쓰는 색인이 0~73 뿐이라 <see cref="GamePalette"/> 만으로 다 그려진다.
/// </remarks>
public sealed class UiSprites
{
    private const int BandPart = 4;

    /// <summary>
    /// 작은 아이콘이 든 파트. <b>16폭 x 96행</b>, 곧 16x16 짜리 여섯 칸이 세로로 쌓여 있다.
    /// </summary>
    /// <remarks>
    /// <code>
    ///   0  X(못 누름)   1  ↑   2  ↓
    ///   3  어두운 빈 칸  4  밝은 빈 상자   5  계산기
    /// </code>
    /// 예전에는 이 파트를 <b>32폭 x 48행</b>으로 보고 "16x8 화살표가 뗌·눌림 두 줄" 이라고
    /// 읽었다. 그러면 (x,y) 가 실제로는 <c>2y + x/16</c> 번째 줄을 가리켜서, 왼쪽 칸에는
    /// 짝수 줄만·오른쪽 칸에는 홀수 줄만 담긴다 — 화살표가 <b>세로로 반쯤 눌린 채</b>
    /// 그려지고, 눌린 꼴처럼 보이던 오른쪽 칸은 사실 같은 그림의 나머지 절반이었다.
    /// 그래서 계산기 아이콘도 안 보였다(다섯째 칸이 두 쪽으로 갈려 있었다).
    /// </remarks>
    private const int IconPart = 3;

    /// <summary>
    /// 숫자 글꼴이 든 파트. 24x24 짜리 기울임체 숫자 열 장이 <b>세로로</b> 쌓여 있다.
    /// </summary>
    /// <remarks>
    /// 비침은 색인 <b>74</b> 다(0 이 아니다) — 조각의 절반이 그 값이고, 팔레트에서
    /// 참값이 든 자리는 0~73 뿐이라 74 부터가 쓰이지 않는 자리다.
    /// </remarks>
    private const int DigitPart = 7;

    /// <summary>숫자 한 장의 크기와 장 수.</summary>
    public const int DigitWidth = 24, DigitHeight = 24, DigitCount = 10;

    /// <summary>숫자 조각의 비침 색인.</summary>
    private const byte DigitClear = 74;

    /// <summary>아이콘 한 칸의 크기. 여섯 칸이 세로로 쌓여 있다.</summary>
    public const int IconWidth = 16, IconHeight = 16, IconCount = 6;

    /// <summary>아이콘 칸 차례.</summary>
    public const int IconNone = 0, IconUp = 1, IconDown = 2,
                     IconDark = 3, IconBlank = 4, IconCalc = 5;

    /// <summary>띠 한 벌의 크기와 조각 배치.</summary>
    private const int StyleBytes = 960, StyleCount = 3;

    /// <summary>띠 높이. 게임이 늘 이 높이로 그린다.</summary>
    public const int BandHeight = 24;

    /// <summary>양 끝 조각의 폭.</summary>
    public const int CapWidth = 16;

    /// <summary>가운데 조각의 폭. 이만큼씩 되풀이해 띠를 늘린다.</summary>
    public const int MidWidth = 8;

    // 벌 안에서 조각이 앉은 자리. 게임 표 0x552898 의 열 {0,16,24} 을 바이트로 옮긴 것이다.
    private static readonly int[] PieceOffset = [0, 384, 576];
    private static readonly int[] PieceWidth = [CapWidth, MidWidth, CapWidth];

    /// <summary>MISC.CDS 파트4 원본(팔레트 인덱스). asset 조각으로 지었으면 null.</summary>
    private readonly byte[]? _band;

    /// <summary>
    /// asset/ui/band 의 아홉 조각(BGRA, 이미 참값 색). style*3+k 로 찾는다.
    /// MISC.CDS 로 지었으면 null.
    /// </summary>
    private readonly uint[][]? _bandPieces;

    private readonly byte[]? _icons;
    private readonly byte[]? _digits;

    /// <summary>asset/ui/misc-03.png (16x96, BGRA). 있으면 이것으로 아이콘을 낸다.</summary>
    private readonly uint[]? _iconsBgra;

    /// <summary>
    /// asset/ui/misc-07.png (24x240, BGRA) — 배경(<see cref="DigitClear"/> 색)은 이미
    /// 알파 0 으로 지워 뒀다. 있으면 이것으로 숫자를 낸다.
    /// </summary>
    private readonly uint[]? _digitsBgra;

    /// <summary>
    /// 계산기 조각 한 파트(MISC.CDS 파트 6) 통째 — 픽셀 50,112 개를 파일 차례 그대로 든다.
    /// </summary>
    /// <remarks>
    /// <b>조각마다 폭이 다르다.</b> 게임은 이 파트를 이렇게 나눠 쓴다(<c>0x00463750</c>).
    /// <code>
    ///   0      ~ 0x8700   판 바탕 160x216   돌무늬 틀과 위 표시줄
    ///   0x8700 + k*768    글쇠 32x24 열일곱  0 1 2 3 4 5 6 7 8 9 00 000 AC DEL MAX MIN CAN-CEL
    ///   0xBA00            ENTER 104x24
    /// </code>
    /// <c>asset/ui/misc-06.png</c> 는 이 파트를 <b>폭 32 로 감아</b> 뽑은 것이라(32x1566) 판과
    /// ENTER 는 줄무늬로 보이지만 픽셀 차례는 그대로다 — 오프셋으로 다시 자르면 제 그림이 된다.
    /// 예전에는 글쇠만 오려 쓰고 판은 밤색 틀로, ENTER 는 띠 단추로 지어 원본과 딴판이었다.
    /// </remarks>
    private readonly uint[]? _padBgra;

    /// <summary>계산기 글쇠 한 장의 크기와 장수.</summary>
    public const int PadWidth = 32, PadHeight = 24, PadCount = 17;

    /// <summary>계산기 판 바탕과 ENTER 의 크기.</summary>
    public const int PanelWidth = 160, PanelHeight = 216, EnterWidth = 104;

    /// <summary>파트 안에서 글쇠와 ENTER 가 시작하는 픽셀 차례.</summary>
    private const int KeyOffset = PanelWidth * PanelHeight,                  // 0x8700
                      EnterOffset = KeyOffset + PadWidth * PadHeight * PadCount; // 0xBA00

    /// <summary>파트 온 크기 — <c>misc-06.png</c> 가 32 폭으로 감아 둔 높이다.</summary>
    private const int PadStripWidth = 32, PadStripHeight = 1566;

    /// <summary>글쇠 차례에서의 번호.</summary>
    public const int PadDoubleZero = 10, PadTripleZero = 11, PadClear = 12,
                     PadBack = 13, PadMost = 14, PadLeast = 15, PadCancel = 16;

    /// <summary>계산기 조각이 있는지.</summary>
    public bool HasPad => _padBgra != null;

    /// <summary>글쇠 한 장을 BGRA 로. 없거나 번호가 밖이면 null.</summary>
    public uint[]? Pad(int at) =>
        at < 0 || at >= PadCount ? null : Slice(KeyOffset + at * PadWidth * PadHeight, PadWidth * PadHeight);

    /// <summary>계산기 판 바탕 160x216 을 BGRA 로. 없으면 null.</summary>
    public uint[]? Panel() => Slice(0, PanelWidth * PanelHeight);

    /// <summary>ENTER 글쇠 104x24 를 BGRA 로. 없으면 null.</summary>
    public uint[]? Enter() => Slice(EnterOffset, EnterWidth * PadHeight);

    private uint[]? Slice(int offset, int length)
    {
        if (_padBgra == null || offset + length > _padBgra.Length) return null;
        var made = new uint[length];
        Array.Copy(_padBgra, offset, made, 0, length);
        return made;
    }

    private UiSprites(byte[]? band, uint[][]? bandPieces,
                      byte[]? icons, uint[]? iconsBgra, byte[]? digits, uint[]? digitsBgra,
                      uint[]? padBgra = null)
    {
        _padBgra = padBgra;
        _band = band;
        _bandPieces = bandPieces;
        _icons = icons;
        _iconsBgra = iconsBgra;
        _digits = digits;
        _digitsBgra = digitsBgra;
    }

    /// <summary>숫자 조각을 읽었는지.</summary>
    public bool HasDigits => _digits != null || _digitsBgra != null;

    /// <summary>
    /// 숫자 한 장을 BGRA 로 꺼낸다(비침은 알파 0). 조각이 없거나 0~9 밖이면 null.
    /// </summary>
    public uint[]? Digit(int digit)
    {
        if (digit < 0 || digit >= DigitCount) return null;

        var bgra = new uint[DigitWidth * DigitHeight];
        int at = digit * DigitWidth * DigitHeight;

        if (_digitsBgra != null)
        {
            Array.Copy(_digitsBgra, at, bgra, 0, bgra.Length);
            return bgra;
        }

        if (_digits == null) return null;
        for (int k = 0; k < bgra.Length; k++)
        {
            byte ix = _digits[at + k];
            if (ix == DigitClear) continue;                       // 비침
            int i = ix * 3;
            bgra[k] = (uint)(0xFF << 24 | GamePalette.Rgb[i] << 16
                            | GamePalette.Rgb[i + 1] << 8 | GamePalette.Rgb[i + 2]);
        }
        return bgra;
    }

    /// <summary>왜 못 열었는지. 잘 열렸으면 빈 문자열.</summary>
    public static string LastError { get; private set; } = "";

    /// <summary>asset/ui/band 조각 파일 이름. <see cref="BandStyle"/> 값 순서 그대로다.</summary>
    private static readonly string[] StyleFileNames = ["title", "button", "alt"];

    /// <summary>왼끝·가운데·오른끝 파일 이름. <see cref="PieceOffset"/> 차례 그대로다.</summary>
    private static readonly string[] PieceFileNames = ["left", "mid", "right"];

    /// <summary>
    /// 띠·아이콘·숫자 조각을 연다. <b>asset/ui 를 먼저 본다</b> — 게임 폴더가 없어도
    /// 앱에 실려 있고, 손으로 다듬은 그림도 그대로 반영된다. 거기 조각이 없거나 깨졌을
    /// 때만 게임 폴더의 MISC.CDS 로 대신한다.
    /// </summary>
    /// <param name="gameDirectory">게임 폴더. 없어도 asset 조각만으로 열릴 수 있다.</param>
    public static UiSprites? Open(string gameDirectory)
    {
        LastError = "";

        var bandPieces = LoadBandAsset();
        var iconsBgra = LoadPiecePng(AssetPath("misc-03.png"), IconWidth, IconHeight * IconCount);
        var digitsBgra = LoadDigitAsset();
        var padBgra = LoadPadAsset();

        byte[]? cdsBand = null;
        byte[]? icons = null;
        byte[]? digits = null;

        var path = CdsAssetPath.Resolve(gameDirectory, "MISC.CDS");
        if (File.Exists(path))
        {
            var archive = Ls12Reader.Open(path);
            if (archive != null && archive.PartCount > BandPart)
            {
                // 띠 조각은 asset 을 못 읽었을 때만 CDS 에서 마저 찾는다.
                if (bandPieces == null)
                {
                    var part = archive.Decode(BandPart);
                    if (part != null && part.Length >= StyleBytes * StyleCount) cdsBand = part;
                }

                // 아이콘도 asset 을 못 읽었을 때만 — 없어도 창은 열린다(글자 화살표로 물러선다).
                if (iconsBgra == null)
                {
                    icons = archive.PartCount > IconPart ? archive.Decode(IconPart) : null;
                    if (icons != null && icons.Length < IconWidth * IconHeight * IconCount) icons = null;
                }

                // 숫자도 마찬가지 — 없으면 계산기가 윈도 글꼴로 물러선다.
                if (digitsBgra == null)
                {
                    digits = archive.PartCount > DigitPart ? archive.Decode(DigitPart) : null;
                    if (digits != null && digits.Length < DigitWidth * DigitHeight * DigitCount) digits = null;
                }
            }
        }

        if (bandPieces == null && cdsBand == null)
        {
            LastError = $"asset/ui/band 조각도 {path} 도 못 읽었습니다";
            return null;
        }

        return new UiSprites(cdsBand, bandPieces, icons, iconsBgra, digits, digitsBgra, padBgra);
    }

    /// <summary><c>asset/ui</c> 밑의 파일 자리.</summary>
    private static string AssetPath(string fileName) =>
        Path.Combine(AppContext.BaseDirectory, "asset", "ui", fileName);

    /// <summary>
    /// asset/ui/misc-07.png 를 읽고 배경(<see cref="DigitClear"/> 색)을 알파 0 으로 지운다.
    /// PNG 는 CDS 원본과 달리 배경도 불투명하게 뜬 것이라 그대로 쓰면 숫자마다 검은 상자가
    /// 진다 — CDS 경로가 색인 <see cref="DigitClear"/> 를 건너뛰는 것과 같은 뜻으로,
    /// 그 색과 정확히 같은 픽셀만 지운다.
    /// </summary>
    /// <summary>
    /// <c>asset/ui/misc-06.png</c> 를 통째로 읽는다 — 계산기 판·글쇠·ENTER 가 한 파트에 있다.
    /// </summary>
    /// <remarks>
    /// 그 그림은 파트를 폭 32 로 감은 띠(32 x 1566)라 크기를 따지고 픽셀 차례 그대로 든다.
    /// 조각은 <see cref="Panel"/> · <see cref="Pad"/> · <see cref="Enter"/> 가 오프셋으로 자른다.
    /// 짧거나 없으면 null 이고, 그때는 계산기가 띠 단추로 물러선다.
    /// </remarks>
    private static uint[]? LoadPadAsset() =>
        LoadPiecePng(AssetPath("misc-06.png"), PadStripWidth, PadStripHeight);

    private static uint[]? LoadDigitAsset()
    {
        var pixels = LoadPiecePng(AssetPath("misc-07.png"), DigitWidth, DigitHeight * DigitCount);
        if (pixels == null) return null;

        int ci = DigitClear * 3;
        uint clear = 0xFF000000u | (uint)(GamePalette.Rgb[ci] << 16
                                         | GamePalette.Rgb[ci + 1] << 8 | GamePalette.Rgb[ci + 2]);
        for (int i = 0; i < pixels.Length; i++)
            if (pixels[i] == clear) pixels[i] = 0;
        return pixels;
    }

    /// <summary>
    /// asset/ui/band 의 아홉 조각(title·button·alt × 왼끝·가운데·오른끝)을 읽는다.
    /// 하나라도 없거나 크기가 다르면 통째로 null 이다 — 조각이 섞이면 이음매가 어긋난다.
    /// </summary>
    private static uint[][]? LoadBandAsset()
    {
        var pieces = new uint[StyleCount * 3][];
        for (int s = 0; s < StyleCount; s++)
            for (int k = 0; k < 3; k++)
            {
                var file = AssetPath($"band/{StyleFileNames[s]}-{PieceFileNames[k]}.png");
                var px = LoadPiecePng(file, PieceWidth[k], BandHeight);
                if (px == null) return null;
                pieces[s * 3 + k] = px;
            }
        return pieces;
    }

    /// <summary>조각 PNG 한 장을 BGRA 로 읽는다. 없거나 크기가 다르거나 못 읽으면 null.</summary>
    private static uint[]? LoadPiecePng(string path, int width, int height)
    {
        if (!File.Exists(path)) return null;
        try
        {
            var frame = BitmapFrame.Create(new Uri(path, UriKind.Absolute),
                                           BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
            if (frame.PixelWidth != width || frame.PixelHeight != height) return null;

            var converted = new FormatConvertedBitmap(frame, PixelFormats.Bgra32, null, 0);
            var pixels = new uint[width * height];
            converted.CopyPixels(pixels, width * 4, 0);
            return pixels;
        }
        catch (Exception ex) when (ex is IOException or NotSupportedException or FileFormatException)
        {
            return null;
        }
    }

    /// <summary>가운데를 <paramref name="cells"/> 번 되풀이했을 때의 띠 폭.</summary>
    public static int WidthFor(int cells) => CapWidth * 2 + MidWidth * Math.Max(1, cells);

    /// <summary>
    /// 담고 싶은 폭을 넣으면 그만큼을 덮는 가장 작은 칸 수를 준다.
    /// 띠 폭은 8픽셀씩만 늘어나므로 딱 맞는 폭이 안 나올 수 있다.
    /// </summary>
    public static int CellsFor(double contentWidth)
    {
        double need = contentWidth - CapWidth * 2;
        if (need < 0) need = 0;
        return Math.Max(1, (int)Math.Ceiling(need / MidWidth));
    }

    /// <summary>
    /// 글자를 <b>가운데 조각만으로</b> 덮는 칸 수. 마구리는 글자 바깥에 놓인다.
    /// </summary>
    /// <remarks>
    /// <see cref="CellsFor"/> 와 다르다. 그쪽은 마구리까지 글자 자리로 세므로 긴 버튼에는 맞지만
    /// 이름표처럼 짧은 것에서는 덩굴 무늬가 글자를 덮는다 — "시장" 두 글자가 32점뿐이라
    /// 마구리 둘(32점)에 통째로 먹힌다. 게임 이름표는 그 반대로 짓는다. 화면에서 잰
    /// "시장" 이름표가 띠 64점(마구리 32 + 가운데 넉 칸)이라 글자 32점이 가운데에 딱 든다.
    /// </remarks>
    public static int CellsAround(double contentWidth) =>
        Math.Max(1, (int)Math.Ceiling(contentWidth / MidWidth));

    /// <summary>
    /// 띠 하나를 BGRA 로 짓는다. 높이는 늘 <see cref="BandHeight"/> 다.
    /// 왼끝을 깔고 가운데를 <paramref name="cells"/> 번 되풀이한 뒤 오른끝을 덮는다 —
    /// 게임이 하는 그대로다.
    /// </summary>
    public uint[] Band(BandStyle style, int cells, out int width)
    {
        cells = Math.Max(1, cells);
        width = WidthFor(cells);

        var bgra = new uint[width * BandHeight];
        Blit(bgra, width, style, 0, 0);
        for (int i = 0; i < cells; i++)
            Blit(bgra, width, style, 1, CapWidth + i * MidWidth);
        Blit(bgra, width, style, 2, width - CapWidth);
        return bgra;
    }

    /// <summary>
    /// 조각 하나를 BGRA 로 꺼낸다. <paramref name="k"/> 는 0 왼끝 / 1 가운데 / 2 오른끝.
    /// 높이는 늘 <see cref="BandHeight"/> 다.
    /// </summary>
    /// <remarks>
    /// 화면에 붙일 때는 이 셋을 (왼끝 고정 · 가운데 이어 깔기 · 오른끝 고정)으로 놓으면
    /// 어떤 폭에도 맞고 도트도 안 뭉개진다. 게임이 하는 그대로다.
    /// </remarks>
    public uint[] Piece(BandStyle style, int k, out int width)
    {
        k = Math.Clamp(k, 0, 2);
        width = PieceWidth[k];
        var bgra = new uint[width * BandHeight];
        Blit(bgra, width, style, k, 0);
        return bgra;
    }

    /// <summary>
    /// 아이콘 한 칸(16x16)을 BGRA 로 꺼낸다. 조각이 없거나 칸 밖이면 null.
    /// </summary>
    /// <param name="index"><see cref="IconUp"/> · <see cref="IconCalc"/> 따위.</param>
    public uint[]? Icon(int index)
    {
        if (index < 0 || index >= IconCount) return null;

        var bgra = new uint[IconWidth * IconHeight];
        int at = index * IconWidth * IconHeight;

        if (_iconsBgra != null)
        {
            Array.Copy(_iconsBgra, at, bgra, 0, bgra.Length);
            return bgra;
        }

        if (_icons == null) return null;
        for (int k = 0; k < bgra.Length; k++)
        {
            int i = _icons[at + k] * 3;
            bgra[k] = (uint)(0xFF << 24 | GamePalette.Rgb[i] << 16
                            | GamePalette.Rgb[i + 1] << 8 | GamePalette.Rgb[i + 2]);
        }
        return bgra;
    }

    /// <summary>조각 하나(<paramref name="k"/> = 0 왼끝 / 1 가운데 / 2 오른끝)를 x 자리에 옮긴다.</summary>
    private void Blit(uint[] dst, int stride, BandStyle style, int k, int x)
    {
        int w = PieceWidth[k];

        if (_bandPieces != null)
        {
            var piece = _bandPieces[(int)style * 3 + k];
            for (int r = 0; r < BandHeight; r++)
                Array.Copy(piece, r * w, dst, r * stride + x, w);
            return;
        }

        int src = (int)style * StyleBytes + PieceOffset[k];
        for (int r = 0; r < BandHeight; r++)
            for (int c = 0; c < w; c++)
            {
                int i = _band![src + r * w + c] * 3;
                dst[r * stride + x + c] = (uint)(0xFF << 24 | GamePalette.Rgb[i] << 16
                                                | GamePalette.Rgb[i + 1] << 8 | GamePalette.Rgb[i + 2]);
            }
    }
}
