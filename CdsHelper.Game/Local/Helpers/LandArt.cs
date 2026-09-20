using System.IO;
using CdsHelper.Support.Local.Helpers;

namespace CdsHelper.Game.Local.Helpers;

/// <summary>
/// 게임 폴더의 <c>LANDDATA.CDS</c> — 육상전의 싸움터와 부대 그림.
/// </summary>
/// <remarks>
/// 자세한 것은 볼트 <c>65.분석-육상전</c> 의 「부대 그림 고르기」 절에 있다.
/// <code>
///   파트 0 · 5 · 7    팔레트 셋 (258 · 258 · 261바이트, 차례는 파랑·빨강·초록)
///   파트 1~4          싸움터 넷 640x480 (도시 · 초지 · 숲 · 황무지)
///   파트 6            부대배치 판 592x320 — 팔레트는 파트 5 다
///   파트 8~50         부대 그림 — 한 장에 몸짓 여덟(2x4)
///   파트 51 · 52      조각 둘
/// </code>
/// 부대 그림은 <b>192x192(칸 96x48)</b> 이고, 낙타병·코끼리병만 <b>256x256(칸 128x64)</b> 다.
/// </remarks>
public sealed class LandArt
{
    /// <summary>파일 이름. 게임 폴더는 대소문자를 안 가리지만 이름을 그대로 적어 둔다.</summary>
    private const string FileName = "LANDDATA.CDS";

    /// <summary>부대 그림 한 장에 든 몸짓 수 — 가로 둘 세로 넷이다.</summary>
    public const int FrameCols = 2, FrameRows = 4;

    /// <summary>그림 파트가 쓰는 팔레트.</summary>
    private const int UnitPalette = 7;

    /// <summary>
    /// 부대배치 판이 쓰는 팔레트.
    /// </summary>
    /// <remarks>
    /// 배치 화면은 파트 5 를 색자리 <b>160</b> 에 앉히고(<c>0x004A01C2</c> 의
    /// <c>0x004BA161(0xA0, 0x56, 팔레트)</c>) 파트 6 의 점값에 그만큼 더해 찍는다
    /// (<c>0x004A01FE</c> 의 <c>0x0041F990(0x2E400, 판, 0xA0, -1)</c>). 그 더하기를 도로
    /// 빼면 파트 5 의 여든여섯 색을 그대로 쓰는 셈이라 여기서는 0 자리부터 찾는다.
    /// </remarks>
    private const int BoardPalette = 5;

    /// <summary>싸움터 파트가 쓰는 팔레트.</summary>
    private const int FieldPalette = 0;

    /// <summary>싸움터 한 장의 크기.</summary>
    public const int FieldWidth = 640, FieldHeight = 480;

    /// <summary>
    /// 부대배치 판 한 장의 크기 — <b>592 x 320</b>.
    /// </summary>
    /// <remarks>
    /// 파트 6 은 189,440바이트라 나누어떨어지는 꼴이 넷이나 되어 오래 못 짚었는데,
    /// 고를 것 없이 <b>게임이 찍는 크기</b>가 답이었다. <c>0x0049FEFB</c> 가
    /// <c>0x004B5CB9(0, 0, 0x250, 0x140, 판)</c> 으로 <b>592 x 320</b> 을 (0,0)에 찍는다.
    /// 592 x 320 = 189,440 이라 자투리 없이 딱 맞고, 줄 우선으로 읽으면 그림이 선다.
    /// </remarks>
    public const int BoardWidth = 592, BoardHeight = 320;

    /// <summary>싸움터 파트 — 지형 번호(0 도시 · 1 초지 · 2 숲 · 3 황무지) 차례다.</summary>
    private const int FirstField = 1;

    /// <summary>
    /// 비침 색인 — 팔레트 <b>맨 끝</b> 자리다.
    /// </summary>
    /// <remarks>
    /// 다른 벌들처럼 마젠타 자리인데 <b>번호가 다르다</b>. 부대 팔레트(파트 7)는 여든일곱
    /// 색이고 그 마지막 86번이 <c>FF00FF</c> 라, 부대 그림도 숫자 조각도 바탕이 86 이다
    /// (파트 8 은 36,864점 중 18,510점이 86 이다). 64 로 두었던 동안에는 바탕이 안 뚫려
    /// 부대마다 자주색 네모가 따라다녔다.
    /// </remarks>
    private const byte Transparent = 86;

    private readonly Ls12Reader _archive;
    private readonly List<(byte R, byte G, byte B)> _unitColors;
    private readonly List<(byte R, byte G, byte B)> _fieldColors;
    private readonly List<(byte R, byte G, byte B)> _boardColors;

    private LandArt(Ls12Reader archive)
    {
        _archive = archive;
        _unitColors = Colors(archive, UnitPalette);
        _fieldColors = Colors(archive, FieldPalette);
        _boardColors = Colors(archive, BoardPalette);
    }

    /// <summary>왜 못 열었는지. 잘 열렸으면 빈 글.</summary>
    public static string LastError { get; private set; } = "";

    /// <summary>게임 폴더의 LANDDATA.CDS 를 연다. 없거나 모양이 다르면 null.</summary>
    public static LandArt? Open(string gameDirectory)
    {
        LastError = "";

        string path = CdsAssetPath.Resolve(gameDirectory, FileName);
        if (!File.Exists(path)) { LastError = $"{path} 가 없습니다"; return null; }

        var archive = Ls12Reader.Open(path);
        if (archive == null) { LastError = $"{path} 를 읽지 못했습니다"; return null; }
        if (archive.PartCount <= LandUnitArt.LastPart)
        {
            LastError = "LANDDATA.CDS 에 부대 그림이 모자랍니다";
            return null;
        }
        return new LandArt(archive);
    }

    /// <summary>
    /// 그 병종의 몸짓 한 장을 BGRA 로 푼다. 못 풀면 null.
    /// </summary>
    /// <param name="kind">병종 0~23.</param>
    /// <param name="friend">아군 자리인지 — 게임은 슬롯 0~5 를 아군으로 본다.</param>
    /// <param name="culture">상대 문화권 0~10. 몇몇 병종의 그림이 이것으로 갈린다.</param>
    /// <param name="frame">몸짓 0~7.</param>
    public uint[]? TryGetUnit(int kind, bool friend, int culture, int frame,
                              out int width, out int height)
    {
        width = height = 0;
        if (frame < 0 || frame >= FrameCols * FrameRows) return null;

        int part = LandUnitArt.PartOf(kind, friend, culture);
        var pixels = _archive.Decode(part);
        if (pixels == null) return null;

        int side = Side(pixels.Length);
        if (side == 0) return null;

        width = side / FrameCols;
        height = side / FrameRows;

        int left = frame % FrameCols * width;
        int top = frame / FrameCols * height;

        var bgra = new uint[width * height];
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                byte v = pixels[(top + y) * side + left + x];
                if (Clear(_unitColors, v)) continue;
                bgra[y * width + x] = Argb(_unitColors, v);
            }
        return bgra;
    }

    /// <summary>
    /// 부대배치 판 한 장(592x320)을 BGRA 로 푼다. 못 풀면 null.
    /// </summary>
    /// <remarks>비침이 없다 — 판이 화면 바닥이라 온통 칠해져 있다.</remarks>
    public uint[]? TryGetBoard()
    {
        var pixels = _archive.Decode(BoardPart);
        if (pixels == null || pixels.Length < BoardWidth * BoardHeight) return null;

        var bgra = new uint[BoardWidth * BoardHeight];
        for (int i = 0; i < bgra.Length; i++) bgra[i] = Argb(_boardColors, pixels[i]);
        return bgra;
    }

    /// <summary>
    /// 배치 화면에 세우는 부대 그림 한 칸(96x96). 그 병종을 못 내면 null.
    /// </summary>
    /// <remarks>
    /// <b>몸짓 한 장(96x48)이다.</b> 판을 96 세로줄·48 가로줄로 그어 보면 2x4 로 딱
    /// 떨어지고 칸마다 무리 하나가 들어 있다 — 96x96 을 떼면 두 칸이 겹쳐 나와 말이
    /// 넷으로 보인다. 여기 세울 수 있는 병종은
    /// <see cref="LandUnitArt.DeploySheet"/> 가 −1 을 안 내는 여덟뿐이다.
    /// </remarks>
    public uint[]? TryGetDeployUnit(int kind, out int width, out int height)
    {
        width = DeployWidth;
        height = DeployHeight;
        if (LandUnitArt.DeploySheet(kind) < 0) return null;

        var pixels = _archive.Decode(LandUnitArt.PartOf(kind, friend: true, culture: 0));
        if (pixels == null) return null;

        int side = Side(pixels.Length);
        if (side < DeployWidth) return null;

        var bgra = new uint[DeployWidth * DeployHeight];
        for (int y = 0; y < DeployHeight; y++)
            for (int x = 0; x < DeployWidth; x++)
            {
                byte v = pixels[y * side + x];
                if (Clear(_unitColors, v)) continue;
                bgra[y * DeployWidth + x] = Argb(_unitColors, v);
            }
        return bgra;
    }

    /// <summary>
    /// 배치 화면이 부대 인원과 남은 수를 찍는 <b>숫자 조각 열</b>(24x24) 한 자.
    /// 그 자리가 아니면 null.
    /// </summary>
    /// <remarks>
    /// 파트 52 는 5,760바이트라 24x24 짜리 열 자다. 게임도 그렇게 읽는다 — 자리는
    /// <c>0x0049FD62</c> 가 <c>숫자 x 0x240 + 0xF00</c> 으로 잡는데, <c>0xF00</c> 은
    /// 파트 51 과 52 를 한 버퍼에 이어 붙일 때 파트 52 가 시작하는 자리고(<c>0x0044A710</c>)
    /// <c>0x240</c> 이 24x24 다. 찍는 크기도 <c>0x18 x 0x18</c> 이다.
    /// </remarks>
    public uint[]? TryGetDigit(int digit)
    {
        if (digit < 0 || digit > 9) return null;

        var pixels = _archive.Decode(DigitPart);
        if (pixels == null || pixels.Length < (digit + 1) * DigitSide * DigitSide) return null;

        int at = digit * DigitSide * DigitSide;
        var bgra = new uint[DigitSide * DigitSide];
        for (int i = 0; i < bgra.Length; i++)
        {
            byte v = pixels[at + i];
            if (Clear(_unitColors, v)) continue;
            bgra[i] = Argb(_unitColors, v);
        }
        return bgra;
    }

    /// <summary>
    /// 말풍선을 짜는 <b>16x16 조각</b> 한 장(파트 51). 그 자리가 아니면 null.
    /// </summary>
    /// <remarks>
    /// 게임은 판 위 말을 창이 아니라 <b>말풍선</b>으로 낸다. 짜는 자리가
    /// <c>0x00445B20</c> 인데, 파트 51·52 를 한 버퍼에 이어 붙인 뱅크(<c>0x005A4A8C</c>)
    /// 에서 <c>+0x300</c>·<c>+0x400</c>·<c>+0x100</c>(또는 <c>+0x900</c>)·
    /// <c>+0x200</c>(또는 <c>+0xA00</c>) 넷을 가져다 쓴다 — 곧 파트 51 의 조각
    /// 3 · 4 · 1(9) · 2(10) 이고 한 장이 <c>0x100</c> = 16x16 이다.
    ///
    /// 조각 열다섯 장을 뽑아 보면 <b>둥근 상자의 네 귀</b>다.
    /// <code>
    ///    0 · 5 · 8    채움          6 왼 테     7 위 테
    ///    3 왼위        4 오른위
    ///    1 왼아래(민)  2 오른아래(<b>꼬리</b>)   ← 적이 말할 때
    ///    9 왼아래(<b>꼬리</b>)  10 오른아래(민)  ← 아군이 말할 때
    /// </code>
    /// 꼬리가 말하는 부대 쪽을 가리키므로 편에 따라 갈라 쓴다.
    /// </remarks>
    public uint[]? TryGetBubble(int piece)
    {
        if (piece < 0) return null;

        var pixels = _archive.Decode(BubblePart);
        int side = BubbleSide * BubbleSide;
        if (pixels == null || pixels.Length < (piece + 1) * side) return null;

        int at = piece * side;
        var bgra = new uint[side];
        for (int i = 0; i < bgra.Length; i++)
        {
            byte v = pixels[at + i];
            if (Clear(_unitColors, v)) continue;
            bgra[i] = Argb(_unitColors, v);
        }
        return bgra;
    }

    /// <summary>말풍선 조각이 든 파트와 한 장의 한 변.</summary>
    private const int BubblePart = 51;

    /// <summary>말풍선 조각 한 장의 한 변.</summary>
    public const int BubbleSide = 16;

    /// <summary>말풍선 조각 번호 — 네 귀와 꼬리 붙은 두 벌.</summary>
    public const int BubbleTopLeft = 3, BubbleTopRight = 4,
                     BubbleFoeLeft = 1, BubbleFoeRight = 2,
                     BubbleMineLeft = 9, BubbleMineRight = 10;

    /// <summary>숫자 조각이 든 파트와 한 자의 크기.</summary>
    private const int DigitPart = 52;

    /// <summary>숫자 한 자의 한 변.</summary>
    public const int DigitSide = 24;

    /// <summary>배치 판이 든 파트.</summary>
    private const int BoardPart = 6;

    /// <summary>배치 화면이 찍는 부대 한 칸 — <b>몸짓 한 장</b>이다.</summary>
    public const int DeployWidth = 192 / FrameCols, DeployHeight = 192 / FrameRows;

    /// <summary>자리 한 칸의 한 변. 그림은 그 안에 아래로 붙인다.</summary>
    public const int DeploySide = 96;

    /// <summary>싸움터 한 장(640x480)을 BGRA 로 푼다. 못 풀면 null.</summary>
    /// <param name="terrain">0 도시 · 1 초지 · 2 숲 · 3 황무지.</param>
    public uint[]? TryGetField(int terrain)
    {
        if (terrain < 0 || terrain > 3) return null;

        var pixels = _archive.Decode(FirstField + terrain);
        if (pixels == null || pixels.Length < FieldWidth * FieldHeight) return null;

        var bgra = new uint[FieldWidth * FieldHeight];
        for (int i = 0; i < bgra.Length; i++) bgra[i] = Argb(_fieldColors, pixels[i]);
        return bgra;
    }

    /// <summary>한 변의 길이. 그림은 정사각이라 넓이에서 되짚는다.</summary>
    private static int Side(int pixels) => pixels switch
    {
        36864 => 192,
        65536 => 256,
        _ => 0,
    };

    /// <summary>비칠 자리인지 — 마지막 마젠타 자리이거나 팔레트 밖이면 그렇다.</summary>
    private static bool Clear(List<(byte R, byte G, byte B)> colors, byte index) =>
        index == Transparent || index >= colors.Count;

    private static uint Argb(List<(byte R, byte G, byte B)> colors, byte index) =>
        index < colors.Count
            ? (uint)(0xFF << 24 | colors[index].R << 16 | colors[index].G << 8 | colors[index].B)
            : 0xFFFF00FFu;

    /// <summary>
    /// 팔레트 한 벌. 세 바이트가 한 색이고 차례는 <b>파랑 · 빨강 · 초록</b> 이다.
    /// </summary>
    /// <remarks>여섯 비트(0~63)로 적혀 있어 넷을 곱해야 눈에 보이는 밝기가 된다.</remarks>
    private static List<(byte R, byte G, byte B)> Colors(Ls12Reader archive, int part)
    {
        var raw = archive.Decode(part);
        var colors = new List<(byte, byte, byte)>();
        if (raw == null) return colors;

        bool dim = true;
        foreach (byte b in raw) if (b >= 64) { dim = false; break; }

        for (int i = 0; i + 2 < raw.Length; i += 3)
        {
            byte blue = raw[i], red = raw[i + 1], green = raw[i + 2];
            colors.Add(dim
                ? ((byte)(red * 4), (byte)(green * 4), (byte)(blue * 4))
                : (red, green, blue));
        }
        return colors;
    }
}

/// <summary>
/// 병종에서 <c>LANDDATA.CDS</c> 파트를 고르는 표 — 게임의 <c>0x0044A1F0</c> 이다.
/// </summary>
/// <remarks>
/// 아군(슬롯 0~5)과 적(6~11)이 다른 그림을 쓰는 병종이 여섯이고, 문화권으로 갈리는
/// 병종이 다섯이다. 문화권은 마을 공략이면 <b>그 도시의 것</b>이다
/// (<c>0x00447070</c> 이 전투 갈래 2·4 일 때 <c>[[+0xA0]+0x58]</c> 을 낸다).
/// </remarks>
public static class LandUnitArt
{
    /// <summary>부대 그림이 든 마지막 파트.</summary>
    public const int LastPart = 45;

    /// <summary>병종 수.</summary>
    public const int KindCount = 24;

    /// <summary>
    /// 배치 화면이 미리 읽어 두는 부대 그림 여덟(파트 8~15) 중 그 병종의 자리.
    /// 못 내는 병종이면 −1 이다.
    /// </summary>
    /// <remarks>
    /// <c>0x0049F7D0</c> 이 병종을 <b>버퍼 안 자리</b>로 옮기는데, 그 자리가 죄다
    /// <c>0x9000</c>(36,864 = 192x192)의 배수라 곧 <b>파트 번호 − 8</b> 이다. 표
    /// (<c>0x0049F854</c>)를 보면 값이 있는 병종이 여덟뿐이고, 나머지는 −1 을 낸다.
    /// 배치 화면이 읽어 들이는 것도 정확히 파트 8~15 여덟 장이다(<c>0x004A020B</c>).
    /// 곧 <b>플레이어가 낼 수 있는 병종은 이 여덟이 전부</b>다.
    /// </remarks>
    public static int DeploySheet(int kind) => kind switch
    {
        0 => 0,    // 기병       — 파트 8
        1 => 1,    // 중장기병   — 파트 9
        15 => 2,   // 화승총대   — 파트 10
        16 => 3,   // 머스켓총대 — 파트 11
        18 => 4,   // 포병       — 파트 12
        19 => 5,   // 캐논포병   — 파트 13
        2 => 6,    // 제독       — 파트 14
        3 => 7,    // 무적제독   — 파트 15
        _ => -1,
    };

    /// <summary>그 병종의 파트 번호.</summary>
    public static int PartOf(int kind, bool friend, int culture) => kind switch
    {
        0 => friend ? 8 : culture is 0 or 1 or 2 ? 16 : 17,      // 기병
        1 => friend ? 9 : culture is 0 or 1 or 2 ? 18 : 19,      // 중장기병
        2 => friend ? 14 : 24,                                    // 제독
        3 => friend ? 15 : 25,                                    // 무적제독
        4 => 29,                                                  // 창병
        5 => culture is 3 or 8 ? 30 : 31,                         // 경보병
        6 => 32,                                                  // 낙타병 (256x256)
        7 => 33,                                                  // 코끼리병 (256x256)
        8 => 36,                                                  // 사무라이
        9 => 37,                                                  // 하타모토
        10 => 38,                                                 // 닌자
        11 => 40,                                                 // 인디오
        12 => 43,                                                 // 족장
        13 => 45,                                                 // 영주
        14 => culture is 4 or 5 ? 41 : 42,                        // 장군
        15 => friend ? 10 : 20,                                   // 화승총대
        16 => friend ? 11 : 21,                                   // 머스켓총대
        17 => culture is 6 or 7 ? 27 : 26,                        // 궁병
        18 => friend ? 12 : 22,                                   // 포병
        19 => friend ? 13 : 23,                                   // 캐논포병
        20 => 35,                                                 // 화포병
        21 => 28,                                                 // 주술사
        22 => 34,                                                 // 고승
        23 => 39,                                                 // 표범
        _ => friend ? 8 : 16,                                     // 표 밖 — 게임도 이렇게 물러선다
    };
}
