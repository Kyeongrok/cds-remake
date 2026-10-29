using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CdsHelper.Support.Local.Helpers;

namespace CdsHelper.Game.Local.Helpers;

/// <summary>
/// 도서관에서 책을 읽으면 뜨는 <b>펼친 책</b> — <c>asset/openbook</c> 의 <c>B{번호}.png</c> 다.
/// <b>게임 폴더의 <c>OPENBOOK.CDS</c> 는 안 읽는다.</b>
/// </summary>
/// <remarks>
/// 원본 서른석 장(<c>B0</c>~<c>B32</c>)은 <c>OPENBOOK.CDS</c> 에서 꺼내 실어 두었다. 삽화는 <c>B13</c> 부터라,
/// 새 삽화는 <c>B33</c>(삽화 번호 20)부터 그림 파일을 넣기만 하면 된다 — 크기는 160x240(선 것) 아니면 240x160(누운 것)이다.
/// 찾는 차례는 발견물 그림(<see cref="DiscoveryStillFiles"/>)과 같다 — 저장소의 <c>asset/openbook</c>,
/// <c>%APPDATA%CdsHelperassetopenbook</c>, 실행 파일 옆.
///
/// 아래는 꺼내 온 원본 아카이브의 모양이다(적어 둔다).
/// LS12 아카이브다. 앞의 파트 셋이 팔레트고, 그림은 <b>파트 3부터</b> 스물넷이다 —
/// 원본도 그림 번호 <c>n</c> 을 파트 <c>n+3</c> 으로 바꿔 읽는다(<c>0x00463FE0</c>).
/// <code>
///   파트 0   204바이트  68색 팔레트 — 책 틀과 삽화가 쓴다(밑동 74)
///   파트 1    48바이트  16색 — 밑동 160
///   파트 2    48바이트  16색 — 밑동 144
/// </code>
/// 밑동은 <c>0x00465120</c> 이 파트마다 박아 둔 값이다. 예전에는 파트 1 을 144 에, 파트 2 를
/// 160 에 얹어 <b>두 종이 색이 뒤바뀌어</b> 나왔다.
/// <code>
///   파트 3~   그림 33장. 크기는 EXE 표 0x005528A8 (8바이트 x 33) 에 적혀 있다
/// </code>
///
/// 그림이 하는 일은 이렇다.
/// <code>
///   0        544x304  펼친 책 틀(붉은 가죽)
///   1~10     256x288  낱장 열 벌 — 왼쪽 면과 오른쪽 면
///   11·12     16x16   작은 조각 둘
///   13~32    160x240 또는 240x160  삽화 스무 장
/// </code>
///
/// 어느 낱장을 쓰는지는 <c>0x00464D5D</c> 가 고른다 — 삽화가 없으면 왼쪽이 2 또는 6,
/// 있으면 3·4·7·8 이고, 오른쪽은 9 또는 10 이다. 2·3·4·9 는 흰 종이 벌이고
/// 6·7·8·10 은 누런 종이 벌이라 <b>두 면의 색이 늘 맞는다</b>.
/// </remarks>
public sealed class OpenBookArt
{
    /// <summary>그림 번호가 파트 번호보다 이만큼 작다.</summary>
    private const int FirstPicturePart = 3;

    /// <summary>그림 장수(<c>0x00463EB9</c> 의 <c>cmp eax,0x21</c>).</summary>
    public const int PictureCount = 33;

    /// <summary>펼친 책 틀.</summary>
    public const int Frame = 0, FrameWidth = 544, FrameHeight = 304;

    /// <summary>
    /// 낱장이 놓이는 자리와 크기. <b>두 면은 맞붙는다</b> — 사이에 틈이 없다.
    /// </summary>
    /// <remarks>
    /// 512 = 256 x 2 를 544 안에 넣으면 32가 남는데, 그 32는 <b>양 옆에 16씩</b>이다.
    /// 예전에는 8·16·8 로 나눠 가운데를 벌려 놓았는데, 원본 갈무리를 재어 보니 두 면
    /// 사이에 보이는 어두운 선은 <b>낱장 그림 제 가장자리</b>(열 점쯤)일 뿐이었다.
    /// </remarks>
    public const int PageWidth = 256, PageHeight = 288;
    public const int LeftPageX = 16, RightPageX = LeftPageX + PageWidth, PageY = 8;

    /// <summary>삽화가 없을 때 쓰는 낱장 — 누런 종이 벌이다.</summary>
    public const int LeftPage = 6, RightPage = 10;

    /// <summary>
    /// 흰 종이 벌(<c>0x00464D5D</c>) — 왼쪽은 삽화 없음 2 · 세로 삽화 3 · 가로 삽화 4, 오른쪽은 9.
    /// </summary>
    public const int WhiteLeft = 2, WhiteLeftTall = 3, WhiteLeftWide = 4, WhiteRight = 9;

    /// <summary>누런 종이 벌 — 왼쪽 6 · 7 · 8, 오른쪽 10. 찾아서 보고까지 한 힌트의 면이다.</summary>
    public const int YellowLeft = 6, YellowLeftTall = 7, YellowLeftWide = 8, YellowRight = 10;

    /// <summary>모서리 단추 그림 — 오른쪽(다음장) 11, 왼쪽(앞장) 12. 16x16 이다.</summary>
    public const int NextCorner = 11, PreviousCorner = 12;

    /// <summary>삽화 첫 그림. 힌트 줄 <c>+0x10</c> 의 값(0~19)을 더한다. 원본 삽화는 스무 장이다.</summary>
    public const int FirstIllustration = 13, IllustrationCount = 20;

    /// <summary>그림이 든 곳과 파일 이름.</summary>
    public const string AssetDirectory = "asset/openbook";
    private static string Name(int picture) => $"B{picture}.png";

    /// <summary>그 삽화 번호(힌트 줄의 값)의 그림이 있는지 — 원본 스무 장 뒤에 더해 넣은 것도 친다.</summary>
    public static bool HasIllustration(int illustration) =>
        illustration >= 0 && PathOf(FirstIllustration + illustration) != null;

    /// <summary>그림마다의 크기. EXE 표(<c>0x005528A8</c>)에서 그대로 옮겼다.</summary>
    private static readonly (int W, int H)[] Sizes =
    [
        (544, 304),
        (256, 288), (256, 288), (256, 288), (256, 288), (256, 288),
        (256, 288), (256, 288), (256, 288), (256, 288), (256, 288),
        (16, 16), (16, 16),
        (160, 240), (160, 240),
        (240, 160), (240, 160), (240, 160), (240, 160), (240, 160),
        (240, 160), (240, 160), (240, 160),
        (160, 240), (160, 240), (160, 240), (160, 240), (160, 240),
        (160, 240), (160, 240),
        (240, 160), (240, 160), (240, 160),
    ];

    private readonly Dictionary<int, uint[]?> _made = [];

    private OpenBookArt() { }

    /// <summary>왜 못 열었는지. 잘 열렸으면 빈 문자열.</summary>
    public static string LastError { get; private set; } = "";

    /// <summary>연다. 책 틀 그림이 없으면 null. <paramref name="gameDirectory"/> 는 안 쓴다(게임 폴더를 안 읽는다).</summary>
    public static OpenBookArt? Open(string gameDirectory)
    {
        LastError = "";
        if (PathOf(Frame) != null) return new OpenBookArt();
        LastError = $"{AssetDirectory} 에 {Name(Frame)} 가 없습니다";
        return null;
    }

    private static readonly Dictionary<int, (int W, int H)> AddedSizes = [];

    /// <summary>그 그림의 크기. 원본 서른석 장은 표로, 더해 넣은 것은 그림 파일에서 읽는다.</summary>
    public static (int W, int H) SizeOf(int picture)
    {
        if (picture < 0) return (0, 0);
        if (picture < Sizes.Length) return Sizes[picture];
        lock (AddedSizes)
        {
            if (AddedSizes.TryGetValue(picture, out var known)) return known;
            (int W, int H) size = (0, 0);
            if (PathOf(picture) is { } path)
                try
                {
                    var frame = new PngBitmapDecoder(new Uri(path), BitmapCreateOptions.None, BitmapCacheOption.OnLoad).Frames[0];
                    size = (frame.PixelWidth, frame.PixelHeight);
                }
                catch (Exception e) when (e is IOException or NotSupportedException or FileFormatException) { }
            return AddedSizes[picture] = size;
        }
    }

    /// <summary>그림 한 장을 BGRA 로 푼다. 없거나 크기가 표와 다르면 null.</summary>
    public uint[]? TryGetBgra(int picture)
    {
        if (picture < 0) return null;
        if (_made.TryGetValue(picture, out var kept)) return kept;

        uint[]? made = null;
        if (PathOf(picture) is { } path)
            try
            {
                BitmapSource frame = new PngBitmapDecoder(new Uri(path), BitmapCreateOptions.None, BitmapCacheOption.OnLoad).Frames[0];
                if (frame.Format != PixelFormats.Bgra32) frame = new FormatConvertedBitmap(frame, PixelFormats.Bgra32, null, 0);
                var (w, h) = SizeOf(picture);
                if (frame.PixelWidth == w && frame.PixelHeight == h)
                {
                    made = new uint[w * h];
                    frame.CopyPixels(made, w * 4, 0);
                }
            }
            catch (Exception e) when (e is IOException or NotSupportedException or FileFormatException) { }

        _made[picture] = made;
        return made;
    }

    /// <summary>그 그림 파일의 자리. 없으면 null.</summary>
    private static string? PathOf(int picture)
    {
        foreach (var dir in AssetDirectories())
        {
            var path = Path.Combine(dir, Name(picture));
            if (File.Exists(path)) return path;
        }
        return null;
    }

    /// <summary>찾아볼 폴더들. 소스 옆이 먼저다.</summary>
    private static IEnumerable<string> AssetDirectories()
    {
        if (SourceDirectory() is { } near) yield return near;
        yield return UserDataFolder.Combine("asset", "openbook");
        yield return Path.Combine(AppContext.BaseDirectory, AssetDirectory);
    }

    private static string? _source;
    private static bool _sourceLooked;

    /// <summary><c>.sln</c> 이 있는 저장소 뿌리의 <c>asset/openbook</c>. 저장소 밖이면 null.</summary>
    private static string? SourceDirectory()
    {
        if (_sourceLooked) return _source;
        _sourceLooked = true;

        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        for (int up = 0; up < 8 && dir != null; up++, dir = dir.Parent)
            if (Directory.Exists(Path.Combine(dir.FullName, "asset")) && dir.EnumerateFiles("*.sln").Any())
                return _source = Path.Combine(dir.FullName, "asset", "openbook");
        return null;
    }
}
