using System.IO;
using CdsHelper.Support.Local.Helpers;

namespace CdsHelper.Game.Local.Helpers;

/// <summary>
/// MALE.CDS · FEMALE.CDS — 인물 초상화. 대사 창에 얼굴을 띄울 때 쓴다.
/// </summary>
/// <remarks>
/// 도시 그림과 같은 LS12 아카이브인데 훨씬 단순하다 — 파트 하나가 곧 얼굴 한 장이고,
/// 모두 정확히 7680바이트다.
/// <code>
///   파트 하나  7680바이트 = 80 x 96, 8bpp 색인, 위에서 아래로
///   MALE.CDS   414장 · FEMALE.CDS 144장
/// </code>
/// <b>제 팔레트가 없다.</b> 쓰이는 색인이 0~73 뿐이라 <see cref="GamePalette"/> 공용 색표만으로
/// 다 그려진다 — 그래서 도시 그림과 달리 팔레트 파트가 붙어 있지 않다.
///
/// 어느 얼굴을 쓰는지는 인물 표에 적혀 있다(<see cref="SponsorTable"/>).
///
/// <b>우리가 들고 있는 벌을 먼저 본다.</b> 얼굴은 놀이 내내 쓰이는 것이라 게임 폴더가
/// 잡혀 있어야만 나오면 곤란하다 — 부하 인물정보처럼 우리 세이브만으로 서야 하는 자리도
/// 있다. 그 벌이 어디 사는지는 <see cref="PortraitStore"/> 가 안다. 없을 때에만 게임
/// 폴더로 물러선다.
/// </remarks>
public sealed class Portraits
{
    public const int Width = 80;
    public const int Height = 96;

    /// <summary>
    /// 게임이 들고 나온 남자 얼굴 수. 이 뒤는 <b>우리가 더 넣은 것</b>이다.
    /// </summary>
    /// <remarks>
    /// 손 안 댄 <c>MALE.CDS</c> 의 파트 수다(<c>FEMALE.CDS</c> 는 144장). 주인공 얼굴을
    /// 고르는 자리가 「게임 것 앞의 열여섯」과 「더 넣은 것」을 가르는 데 쓴다.
    /// </remarks>
    public const int GameMaleCount = 414;
    private const int Pixels = Width * Height;

    private readonly Ls12Reader _male;
    private readonly Ls12Reader _female;

    private Portraits(Ls12Reader male, Ls12Reader female)
    {
        _male = male;
        _female = female;
    }

    /// <summary>왜 못 열었는지. 잘 열렸으면 빈 문자열.</summary>
    public static string LastError { get; private set; } = "";

    /// <summary>남자 얼굴 장수.</summary>
    public int MaleCount => _male.PartCount;

    /// <summary>여자 얼굴 장수.</summary>
    public int FemaleCount => _female.PartCount;

    /// <summary>
    /// 초상화 두 벌을 연다. 우리 벌(<see cref="PortraitStore"/>)을 먼저 보고 없으면
    /// <paramref name="gameDirectory"/> 로 물러선다. 하나라도 못 구하면 null.
    /// </summary>
    /// <param name="gameDirectory">게임 폴더. 몰라도 되므로 비워 둘 수 있다.</param>
    public static Portraits? Open(string gameDirectory = "")
    {
        LastError = "";
        var male = OpenOne(gameDirectory, female: false);
        if (male == null) return null;
        var woman = OpenOne(gameDirectory, female: true);
        if (woman == null) return null;
        return new Portraits(male, woman);
    }

    private static Ls12Reader? OpenOne(string gameDirectory, bool female)
    {
        string file = PortraitStore.NameOf(female);

        // 아직 꺼내 놓지 않았으면 여기서 꺼내 놓는다.
        var path = PortraitStore.PathOf(female);
        if (path.Length == 0 || !File.Exists(path))
        {
            if (gameDirectory.Length == 0)
            {
                LastError = PortraitStore.LastError.Length > 0
                    ? PortraitStore.LastError : $"{file} 이(가) 없습니다";
                return null;
            }
            path = Path.Combine(gameDirectory, file);
            if (!File.Exists(path)) { LastError = $"{path} 가 없습니다"; return null; }
        }

        var archive = Ls12Reader.Open(path);
        if (archive == null) { LastError = $"{path} 를 읽지 못했습니다"; return null; }
        if (archive.PartCount == 0) { LastError = $"{file} 에 얼굴이 없습니다"; return null; }
        return archive;
    }

    /// <summary>
    /// 얼굴 한 장을 80x96 BGRA 로 푼다. 없는 번호이거나 못 풀면 null.
    /// </summary>
    /// <param name="face">얼굴 번호(인물 표의 값).</param>
    /// <param name="female">여자 얼굴이면 true.</param>
    public uint[]? TryGetBgra(int face, bool female)
    {
        var archive = female ? _female : _male;
        if (face < 0 || face >= archive.PartCount) return null;

        var idx = archive.Decode(face);
        if (idx == null || idx.Length < Pixels) return null;

        var bgra = new uint[Pixels];
        for (int i = 0; i < Pixels; i++)
        {
            int c = idx[i] * 3;
            bgra[i] = (uint)(0xFF << 24 | GamePalette.Rgb[c] << 16
                             | GamePalette.Rgb[c + 1] << 8 | GamePalette.Rgb[c + 2]);
        }
        return bgra;
    }
}
