using CdsHelper.Support.Local.Helpers;

namespace CdsHelper.Game.Local.Helpers;

/// <summary>
/// 게임 폴더의 <c>CITYFRM.CDS</c> — 도시 그림(400x320) 둘레에 두르는 여덟 점짜리 틀.
/// </summary>
/// <remarks>
/// LS12 아카이브에 416x336 짜리 파트 둘이다. 도시 화면을 열 때(<c>0x004924DB</c>) 둘 다 풀어
/// 두고, 안쪽 (8, 8) 점의 색인(74)과 같은 점을 색인 0x49 로 갈아 비친다(<c>0x00492547</c>) —
/// 곧 틀만 남기고 가운데 400x320 은 도시 그림이 채운다.
/// <code>
///   파트 0   금빛 틀   공용 색표 색인 12~72 — 도시 화면의 틀
///   파트 1   파란 틀   공용 색표 색인 44~53
/// </code>
/// 색은 모두 공용 색표(<see cref="GamePalette"/>) 안에 있어 도시마다의 제 팔레트가 필요 없다.
/// </remarks>
public static class CityFrame
{
    public const string FileName = "CITYFRM.CDS";

    /// <summary>틀의 크기와 두께 — 도시 그림 400x320 에 여덟 점씩이다.</summary>
    public const int Width = 416, Height = 336, Border = 8;

    /// <summary>파트 번호 — 금빛(도시 화면)과 파란 것.</summary>
    public const int GoldPart = 0, BluePart = 1;

    /// <summary>안쪽을 채운 색인 — 이것이 비치는 자리다.</summary>
    private const int Hollow = 74;

    private static readonly Dictionary<(string, int), uint[]?> Cache = [];

    /// <summary>왜 못 읽었는지. 잘 읽었으면 빈 문자열.</summary>
    public static string LastError { get; private set; } = "";

    /// <summary>
    /// 틀 한 장을 BGRA 로 편다. 안쪽은 알파 0 이다. 파일이 없거나 깨졌으면 null.
    /// </summary>
    public static uint[]? TryGetBgra(string gameDirectory, int part = GoldPart)
    {
        var key = (gameDirectory, part);
        lock (Cache)
        {
            if (Cache.TryGetValue(key, out var cached)) return cached;
            var made = Read(gameDirectory, part);
            Cache[key] = made;
            return made;
        }
    }

    private static uint[]? Read(string gameDirectory, int part)
    {
        LastError = "";
        var path = CdsAssetPath.Resolve(gameDirectory, FileName);
        var archive = Ls12Reader.Open(path);
        if (archive == null) { LastError = $"{FileName} 을 열지 못했습니다 ({path})"; return null; }
        if (part < 0 || part >= archive.PartCount) { LastError = $"{FileName} 에 파트 {part} 가 없습니다"; return null; }

        var data = archive.Decode(part);
        if (data == null || data.Length < Width * Height)
        {
            LastError = $"{FileName} 파트 {part} 가 {Width}x{Height} 이 아닙니다";
            return null;
        }

        var bgra = new uint[Width * Height];
        for (int i = 0; i < bgra.Length; i++)
        {
            int v = data[i];
            if (v == Hollow) continue;                 // 알파 0 — 비친다
            uint r = GamePalette.Rgb[v * 3], g = GamePalette.Rgb[v * 3 + 1], b = GamePalette.Rgb[v * 3 + 2];
            bgra[i] = 0xFF000000u | (r << 16) | (g << 8) | b;
        }
        return bgra;
    }
}
