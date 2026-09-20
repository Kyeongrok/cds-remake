using System.IO;
using CdsHelper.Support.Local.Helpers;

namespace CdsHelper.Game.Local.Helpers;

/// <summary>
/// 게임 폴더의 DISCOVER.CDS — 발견 대본이 트는 <b>움직이는 그림</b> 스물아홉 편.
/// </summary>
/// <remarks>
/// 대본 명령 <c>00 0C [u16 n]</c>(해석기 <c>0x00408429</c>)가 파트 n 을 통째로 풀어
/// 틀기 손 <c>0x00466C90</c> 에 넘긴다. 존왕의 술잔(발견물 104)이 파트 4 다.
/// <code>
///   +0x000  258바이트   86색 팔레트, 한 색이 (파랑, 빨강, 초록) — 색인 160~245 에 얹는다
///   +0x102  240x176     8bpp 색인 한 장씩 이어 붙음, 위에서 아래로
/// </code>
/// 장 수는 게임이 <c>(길이 − 0x300) / 240 / 176</c> 으로 센다(<c>0x00408541</c>). 머리가
/// 실제로는 0x102 라 <b>마지막 한 장은 안 튼다</b> — 파트 4 는 25장이 들었지만 24장만 돈다.
/// 팔레트 밖 색인(대개 <c>0x49</c> 검정)은 공용 팔레트다. DSTILL 과 같은 짜임이다.
/// </remarks>
public sealed class DiscoveryClips
{
    /// <summary>한 장의 가로.</summary>
    public const int Width = 240;

    /// <summary>한 장의 세로.</summary>
    public const int Height = 176;

    /// <summary>팔레트 크기. 86색 x 3바이트.</summary>
    private const int PaletteSize = 258;

    /// <summary>제 팔레트가 얹히는 첫 색인.</summary>
    private const int OwnPaletteBase = 160;

    /// <summary>게임이 장 수를 셀 때 빼는 머리 크기(<c>0x00408541</c>).</summary>
    private const int CountedHeader = 0x300;

    private readonly Ls12Reader _archive;

    private DiscoveryClips(Ls12Reader archive) => _archive = archive;

    /// <summary>편 수. 번호는 0 부터 이 수 미만이다.</summary>
    public int Count => _archive.PartCount;

    /// <summary>왜 못 열었는지. 성공하면 빈 문자열.</summary>
    public static string LastError { get; private set; } = "";

    /// <summary>게임 폴더의 DISCOVER.CDS 를 연다. 없거나 형식이 아니면 null.</summary>
    public static DiscoveryClips? Open(string gameDirectory)
    {
        LastError = "";
        string path = CdsAssetPath.Resolve(gameDirectory, "DISCOVER.CDS");
        if (!File.Exists(path)) { LastError = $"{path} 가 없습니다"; return null; }

        var archive = Ls12Reader.Open(path);
        if (archive == null) { LastError = $"{path} 를 읽지 못했습니다"; return null; }
        if (archive.PartCount == 0) { LastError = "DISCOVER.CDS 에 파트가 없습니다"; return null; }
        return new DiscoveryClips(archive);
    }

    /// <summary>그 편의 장들을 BGRA 로 푼다. 못 풀면 null.</summary>
    public uint[][]? Frames(int clip)
    {
        if (clip < 0 || clip >= Count) return null;

        var data = _archive.Decode(clip);
        int frameSize = Width * Height;
        if (data == null || data.Length < PaletteSize + frameSize) return null;

        int count = Math.Max(0, (data.Length - CountedHeader) / frameSize);
        count = Math.Min(count, (data.Length - PaletteSize) / frameSize);
        if (count == 0) return null;

        var frames = new uint[count][];
        for (int f = 0; f < count; f++)
        {
            int start = PaletteSize + f * frameSize;
            var argb = new uint[frameSize];
            for (int i = 0; i < frameSize; i++)
            {
                byte v = data[start + i];
                int k = (v - OwnPaletteBase) * 3;
                byte r, g, b;
                if (v >= OwnPaletteBase && k + 2 < PaletteSize)
                {
                    b = data[k];            // DSTILL 과 같은 (파랑, 빨강, 초록) 차례다
                    r = data[k + 1];
                    g = data[k + 2];
                }
                else
                {
                    r = GamePalette.Rgb[v * 3];
                    g = GamePalette.Rgb[v * 3 + 1];
                    b = GamePalette.Rgb[v * 3 + 2];
                }
                argb[i] = 0xFF000000u | ((uint)r << 16) | ((uint)g << 8) | b;
            }
            frames[f] = argb;
        }
        return frames;
    }
}
