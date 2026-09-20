using System.IO;
using CdsHelper.Support.Local.Helpers;

namespace CdsHelper.Game.Local.Helpers;

/// <summary>
/// 정박했을 때 배 옆에 내리는 닻 그림. 게임 것을 그대로 쓴다.
/// </summary>
/// <remarks>
/// <c>MISC.CDS</c> 의 <b>파트 11</b> 이고 <b>48x48</b> 8bpp 다. 배 그림과 같은 크기라서,
/// 배가 놓일 자리에 그대로 겹쳐 놓으면 닻이 알아서 배 왼쪽 아래에 걸린다 — 그림 안에서
/// 닻이 <c>x 2~15, y 27~46</c> 에만 찍혀 있고 나머지는 비침이다. 게임도 그렇게 얹는다.
///
/// <b>색은 해상 팔레트(<see cref="OceanPalette"/>)로 푼다.</b> 비침은 색인 0 이 아니라
/// <b>160</b> 이고, 그 자리 색이 <c>252,0,252</c>(마젠타)다 — 색을 안 쓰고 자리만 표시하는
/// 색키다. 배 그림(색인 0 이 비침)과 규칙이 다르니 조심할 것.
///
/// 예전에는 여기서 점을 찍어 16x16 짜리를 만들어 썼다. 게임 것과 모양도 색도 달랐다.
/// </remarks>
public sealed class AnchorSprite
{
    public const string FileName = "MISC.CDS";

    /// <summary>MISC.CDS 안에서 닻이 든 파트.</summary>
    private const int Part = 11;

    /// <summary>한 변. 배 그림과 같다.</summary>
    public const int Width = 48;

    public const int Size = Width * Width;   // 2,304

    /// <summary>비침으로 쓰는 색 번호. 해상 팔레트에서 마젠타 자리다.</summary>
    public const byte TransparentIndex = 160;

    private readonly uint[] _pixels;

    private AnchorSprite(uint[] pixels) => _pixels = pixels;

    /// <summary>왜 못 열었는지. 잘 열렸으면 빈 문자열.</summary>
    public static string LastError { get; private set; } = "";

    /// <summary>48x48 BGRA 한 장. 비침은 알파 0 이다.</summary>
    public ReadOnlySpan<uint> Pixels => _pixels;

    /// <summary>게임 폴더의 MISC.CDS 에서 닻을 꺼낸다. 못 꺼내면 null.</summary>
    public static AnchorSprite? LoadFromDirectory(string directory)
    {
        string path = CdsAssetPath.Resolve(directory, FileName);
        if (!File.Exists(path)) { LastError = $"{FileName} 없음"; return null; }

        var archive = Ls12Reader.Open(path);
        if (archive == null) { LastError = $"{FileName} 이 Ls12 형식이 아님"; return null; }
        if (archive.PartCount <= Part || archive.PartSize(Part) != Size)
        {
            LastError = $"{FileName} 파트{Part} 가 {Size}바이트가 아님 ({archive.PartSize(Part)})";
            return null;
        }

        var part = archive.Decode(Part);
        if (part == null) { LastError = $"{FileName} 파트{Part} 압축 해제 실패"; return null; }

        var pixels = new uint[Size];
        for (int i = 0; i < Size; i++)
        {
            byte ix = part[i];
            pixels[i] = ix == TransparentIndex
                ? 0u
                : 0xFF000000u | (uint)(OceanPalette.Rgb[ix * 3] << 16
                                       | OceanPalette.Rgb[ix * 3 + 1] << 8
                                       | OceanPalette.Rgb[ix * 3 + 2]);
        }

        LastError = "";
        return new AnchorSprite(pixels);
    }
}
