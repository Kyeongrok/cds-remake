using System.IO;
using CdsHelper.Support.Local.Helpers;

namespace CdsHelper.Game.Local.Helpers;

/// <summary>
/// CLOUD.CDS — 지도 위를 흘러가는 구름 그림 여섯 장.
/// </summary>
/// <remarks>
/// Ls12 파트 여섯이고 한 장이 <b>19,200바이트 = 160 x 120</b> 8bpp 다. 게임 읽개
/// (<c>0x00488EF0</c>)는 160 x 720 표면을 만들어 여섯 장을 세로로 쌓는다.
///
/// <b>색 번호 <c>0x49</c> 가 비침이다.</b> 게임도 읽으면서 그 값을 0 으로 갈아 놓는다
/// (<c>0x00489022</c>, 19,200번 돈다). 나머지 색은 해상 팔레트(<see cref="OceanPalette"/>)로 푼다 —
/// 구름에는 제 팔레트가 없다.
///
/// 그림은 <b>바둑판으로 반만 찍힌 덩어리</b>다. 8bpp 시절의 반투명 흉내라, 밑의 지도가
/// 한 점 걸러 비쳐 보인다. 그래서 게임은 구름 자리를 잡을 때 <c>x + y</c> 를 늘 짝수로
/// 맞춘다(<c>0x004890CF</c>) — 격자 짝이 어긋나면 무늬가 뭉개진다.
///
/// 큰 것 셋(0~2)과 작은 것 셋(3~5)이 있고, 구름 여섯은 그중 하나를 밑번호로 잡아
/// 세 장을 돌려 쓴다. 어느 구름이 어느 것을 쓰는지는 <see cref="ShipMapHost"/> 쪽에 있다.
/// </remarks>
public sealed class CloudSprites
{
    public const string FileName = "CLOUD.CDS";

    /// <summary>한 장의 크기.</summary>
    public const int Width = 160, Height = 120;

    /// <summary>한 장의 바이트 수.</summary>
    public const int FramePixels = Width * Height;   // 19,200

    /// <summary>장 수.</summary>
    public const int FrameCount = 6;

    /// <summary>
    /// 비침으로 쓰는 색 번호 <b>둘</b>. 그림마다 어느 쪽을 썼는지가 다르다.
    /// </summary>
    /// <remarks>
    /// 큰 것 셋(0~2)은 바탕이 <c>0x00</c> 이고 작은 것 셋(3~5)은 <c>0x49</c> 다. 게임 읽개가
    /// <c>0x49</c> 를 <c>0</c> 으로 갈아 놓으므로(<c>0x00489022</c>) 게임 안에서는 둘 다
    /// 0(비침)이 된다. 한쪽만 비우면 <b>큰 구름 셋이 까만 네모를 달고 다닌다</b>.
    ///
    /// 그림 속도 이 값으로 성글게 찍혀 있다 — 바둑판으로 한 점 걸러 바탕이라, 두 값을 다
    /// 비워야 밑의 지도가 비쳐 보이는 반투명이 된다.
    /// </remarks>
    public const byte TransparentIndex = 0x49, TransparentIndex2 = 0x00;

    private readonly uint[] _bgra;

    private CloudSprites(uint[] bgra) => _bgra = bgra;

    /// <summary>못 올렸으면 그 까닭 한 줄. 올렸으면 빈 문자열.</summary>
    public static string LastError { get; private set; } = "";

    /// <summary>
    /// 여섯 장을 세로로 이어 붙인 BGRA(<see cref="Width"/> x <see cref="Height"/> *
    /// <see cref="FrameCount"/>). 비침은 알파 0 이다. 게임이 만드는 표면과 같은 배치다.
    /// </summary>
    public ReadOnlySpan<uint> Bgra => _bgra;

    /// <summary>이어 붙인 그림의 높이.</summary>
    public const int AtlasHeight = Height * FrameCount;   // 720

    /// <summary>
    /// 게임 폴더의 CLOUD.CDS 를 풀어 올린다. 못 열면 null 이고 <see cref="LastError"/> 에
    /// 까닭이 남는다.
    /// </summary>
    public static CloudSprites? LoadFromDirectory(string directory)
    {
        string path = CdsAssetPath.Resolve(directory, FileName);
        if (!File.Exists(path)) { LastError = $"{FileName} 없음"; return null; }

        var reader = Ls12Reader.Open(path);
        if (reader == null) { LastError = $"{FileName} 이 Ls12 형식이 아님"; return null; }
        if (reader.PartCount < FrameCount)
        {
            LastError = $"{FileName} 파트가 모자람 ({reader.PartCount}개)";
            return null;
        }

        var bgra = new uint[FramePixels * FrameCount];
        for (int f = 0; f < FrameCount; f++)
        {
            if (reader.PartSize(f) != FramePixels)
            {
                LastError = $"{FileName} 파트{f} 크기가 다름 ({reader.PartSize(f)})";
                return null;
            }
            var frame = reader.Decode(f);
            if (frame == null) { LastError = $"{FileName} 파트{f} 압축 해제 실패"; return null; }

            int dst = f * FramePixels;
            for (int i = 0; i < FramePixels; i++)
            {
                byte ix = frame[i];
                bgra[dst + i] = ix is TransparentIndex or TransparentIndex2
                    ? 0u
                    : 0xFF000000u | (uint)(OceanPalette.Rgb[ix * 3] << 16
                                           | OceanPalette.Rgb[ix * 3 + 1] << 8
                                           | OceanPalette.Rgb[ix * 3 + 2]);
            }
        }

        LastError = "";
        return new CloudSprites(bgra);
    }
}
