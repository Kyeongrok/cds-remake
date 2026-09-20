using System.IO;
using CdsHelper.Support.Local.Helpers;

namespace CdsHelper.Game.Local.Helpers;

/// <summary>
/// 게임 폴더의 CITYCG.CDS — 도시 그림 226장. 도시 번호가 <see cref="GameMapCoords"/> 의
/// 도시 색인과 그대로 맞는다(29 = 베니스, 225 = 원주민 마을).
/// </summary>
/// <remarks>
/// LS12 아카이브이고 그림 한 장이 파트 두 개를 쓴다.
/// <code>
///   파트 2p     128,000바이트 = 400x320, 8bpp 색인, 위에서 아래로   (p = 0~225)
///   파트 2p+1        258바이트 = 86색 팔레트, 한 색이 3바이트
/// </code>
/// 색인 74 위쪽은 그 그림 제 팔레트에서, 그 아래는 <see cref="GamePalette"/> 공용 색표에서
/// 색을 가져온다. 제 팔레트의 한 색은 파일에 (파랑, 빨강, 초록) 순으로 적혀 있다 —
/// 흔한 R,G,B 가 아니다.
///
/// cds95-mod 의 plugins-src/CityPicKR/src/citycg.c 에서 자리와 규칙을 가져왔다. 읽기만 한다.
/// 파일이 20MB 라 다 쓰면 놓는 편이 낫다(참조를 버리면 된다).
/// </remarks>
public sealed class CityPictures
{
    public const int Width = 400;
    public const int Height = 320;
    private const int Pixels = Width * Height;

    /// <summary>팔레트 파트 크기. 86색 x 3바이트.</summary>
    private const int PaletteSize = 258;

    private readonly Ls12Reader _archive;
    private uint[]? _cache;
    private int _cached = -1;

    private CityPictures(Ls12Reader archive) => _archive = archive;

    /// <summary>그림 장수. 도시 번호는 0 부터 이 수 미만이다.</summary>
    public int Count => _archive.PartCount / 2;

    /// <summary>왜 못 열었는지. 성공하면 빈 문자열.</summary>
    public static string LastError { get; private set; } = "";

    /// <summary>게임 폴더의 CITYCG.CDS 를 연다. 없거나 형식이 아니면 null.</summary>
    public static CityPictures? Open(string gameDirectory)
    {
        LastError = "";
        var path = CdsAssetPath.Resolve(gameDirectory, "CITYCG.CDS");
        if (!File.Exists(path)) { LastError = $"{path} 가 없습니다"; return null; }

        var archive = Ls12Reader.Open(path);
        if (archive == null) { LastError = $"{path} 를 읽지 못했습니다"; return null; }
        if (archive.PartCount < 2) { LastError = "CITYCG.CDS 에 그림이 없습니다"; return null; }
        return new CityPictures(archive);
    }

    /// <summary>
    /// 도시 그림 한 장을 400x320 BGRA 로 푼다. 못 풀면 null.
    /// 방금 푼 그림은 들고 있다가 그대로 돌려주므로, 고쳐 쓰지 말 것.
    /// </summary>
    public uint[]? TryGetBgra(int cityId)
    {
        if (cityId == _cached) return _cache;
        if (cityId < 0 || cityId >= Count) return null;

        var idx = _archive.Decode(cityId * 2);
        if (idx == null || idx.Length < Pixels) return null;
        var pal = _archive.Decode(cityId * 2 + 1);
        if (pal == null || pal.Length < 3) return null;
        int palLen = Math.Min(pal.Length, PaletteSize);

        var argb = new uint[Pixels];
        for (int i = 0; i < Pixels; i++)
        {
            byte v = idx[i];
            int k = (v - GamePalette.OwnPaletteBase) * 3;
            byte r, g, b;
            if (v >= GamePalette.OwnPaletteBase && k + 2 < palLen)
            {
                b = pal[k];            // 파일 속 팔레트는 (파랑, 빨강, 초록) 순이다
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

        _cache = argb;
        _cached = cityId;
        return argb;
    }
}
