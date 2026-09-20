using System.IO;
using CdsHelper.Support.Local.Helpers;

namespace CdsHelper.Game.Local.Helpers;

/// <summary>
/// 게임 폴더의 DSTILL.CDS — 발견했을 때 뜨는 그림 84장.
/// </summary>
/// <remarks>
/// LS12 아카이브이고 그림 한 장이 파트 <b>셋</b>을 쓴다.
/// <code>
///   파트 3p     76,800바이트 = 240x320 또는 320x240, 8bpp 색인, 위에서 아래로
///   파트 3p+1      258바이트 = 86색 팔레트, 한 색이 (파랑, 빨강, 초록)
///   파트 3p+2        8바이트 = 가로 · 세로 (int32 둘)
/// </code>
/// 그림이 선 것도 누운 것도 있어서 크기를 박아 두지 않고 셋째 파트에서 읽는다 —
/// 히랄다탑(69)은 240x320 이고 산 피에트로 대성당(48)은 320x240 이다.
///
/// <b>제 팔레트가 얹히는 자리가 도시 그림과 다르다.</b> <see cref="CityPictures"/> 는 74
/// 부터인데 이쪽은 <b>160</b> 부터다 — 84장을 다 훑어 보면 쓰인 색인이 죄다 11~65 와
/// 160~245 두 무리로 갈리고, 86색 팔레트를 160 에 얹어야 딱 245 에서 끝난다. 74 에 얹으면
/// 그림이 온통 시커멓게 나온다.
///
/// 그림 번호는 건물 표(<see cref="CityBuildingTable.Building.Picture"/>)와 발견물 표
/// (<c>0x0051C54C</c>)에 적혀 있다.
/// </remarks>
public sealed class DiscoveryStills
{
    /// <summary>팔레트 파트 크기. 86색 x 3바이트.</summary>
    private const int PaletteSize = 258;

    /// <summary>그림 제 팔레트가 얹히는 첫 색인. 도시 그림(74)과 다르다.</summary>
    private const int OwnPaletteBase = 160;

    /// <summary>그림 한 장이 쓰는 파트 수 — 그림 · 팔레트 · 크기.</summary>
    private const int PartsPerPicture = 3;

    private readonly Ls12Reader _archive;

    private DiscoveryStills(Ls12Reader archive) => _archive = archive;

    /// <summary>그림 장수. 그림 번호는 0 부터 이 수 미만이다.</summary>
    public int Count => _archive.PartCount / PartsPerPicture;

    /// <summary>왜 못 열었는지. 성공하면 빈 문자열.</summary>
    public static string LastError { get; private set; } = "";

    /// <summary>게임 폴더의 DSTILL.CDS 를 연다. 없거나 형식이 아니면 null.</summary>
    public static DiscoveryStills? Open(string gameDirectory) => Open(gameDirectory, "DSTILL.CDS");

    /// <summary>
    /// 같은 짜임의 다른 스틸 묶음을 연다 — <c>EVSTILL.CDS</c>(사건 스틸 열여섯)가 그것이다.
    /// </summary>
    /// <remarks>
    /// 둘 다 <b>파트 셋이 한 묶음</b>이다 — <c>3g</c> 그림(320x240 8bpp) · <c>3g+1</c>
    /// 팔레트(258) · <c>3g+2</c> 여덟 바이트. 그래서 읽는 손을 나눌 것 없이 파일 이름만
    /// 갈아 준다(볼트 <c>22.분석-이벤트 그림</c>).
    /// </remarks>
    public static DiscoveryStills? Open(string gameDirectory, string fileName)
    {
        LastError = "";
        string path = CdsAssetPath.Resolve(gameDirectory, fileName);
        if (!File.Exists(path)) { LastError = $"{path} 가 없습니다"; return null; }

        var archive = Ls12Reader.Open(path);
        if (archive == null) { LastError = $"{path} 를 읽지 못했습니다"; return null; }
        if (archive.PartCount < PartsPerPicture) { LastError = $"{fileName} 에 그림이 없습니다"; return null; }
        return new DiscoveryStills(archive);
    }

    /// <summary>그림 한 장을 BGRA 로 푼다. 못 풀면 null.</summary>
    public uint[]? TryGetBgra(int picture, out int width, out int height)
    {
        width = height = 0;
        if (picture < 0 || picture >= Count) return null;

        int at = picture * PartsPerPicture;
        var size = _archive.Decode(at + 2);
        if (size == null || size.Length < 8) return null;
        width = BitConverter.ToInt32(size, 0);
        height = BitConverter.ToInt32(size, 4);
        if (width <= 0 || height <= 0) return null;

        var idx = _archive.Decode(at);
        int pixels = width * height;
        if (idx == null || idx.Length < pixels) return null;
        var pal = _archive.Decode(at + 1);
        if (pal == null || pal.Length < 3) return null;
        int palLen = Math.Min(pal.Length, PaletteSize);

        var argb = new uint[pixels];
        for (int i = 0; i < pixels; i++)
        {
            byte v = idx[i];
            int k = (v - OwnPaletteBase) * 3;
            byte r, g, b;
            if (v >= OwnPaletteBase && k + 2 < palLen)
            {
                b = pal[k];            // 도시 그림과 같은 (파랑, 빨강, 초록) 차례다
                r = pal[k + 1];
                g = pal[k + 2];
            }
            else
            {
                r = GamePalette.Rgb[v * 3];
                g = GamePalette.Rgb[v * 3 + 1];
                b = GamePalette.Rgb[v * 3 + 2];
            }
            argb[i] = 0xFF000000u | ((uint)r << 16) | ((uint)g << 8) | b;
        }
        return argb;
    }
}
