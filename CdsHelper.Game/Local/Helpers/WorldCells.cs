using System.IO;
using CdsHelper.Support.Local.Helpers;

namespace CdsHelper.Game.Local.Helpers;

/// <summary>
/// WORLD.CDS 의 칸 낱말. 지도 창 없이도 엔진이 그 칸이 뭍인지 볼 때 쓴다.
/// </summary>
/// <remarks>
/// 파일은 2500바이트 행이 2500줄이고, 짝수 행이 지도 왼쪽 절반, 홀수 행이 오른쪽 절반이다
/// (칸 하나 2바이트). 게임은 이것을 펼쳐 <c>[y*2500 + x]</c> 로 읽는다(<c>0x00426D70</c>).
/// <code>
///   아래 14비트  타일 번호(TerrainTable)
///   0x4000       뭍 비트 — 0x00425BE0 이 test ah,0x40 으로 바다 칸을 가른다
/// </code>
/// 대서양 한가운데(1100,500)는 <c>0x0880</c>, 마드리드 곁(1240,350)은 <c>0x434B</c> 다.
/// </remarks>
public sealed class WorldCells
{
    /// <summary>지도 칸 수 — 가로 2500 · 세로 1250.</summary>
    public const int Width = 2500, Height = 1250;

    /// <summary>뭍 비트.</summary>
    public const int LandBit = 0x4000;

    private const int HalfWidth = Width / 2;
    private const int RawStride = 2500;

    private readonly byte[] _raw;

    private WorldCells(byte[] raw) => _raw = raw;

    /// <summary>왜 못 열었는지. 잘 열렸으면 빈 문자열.</summary>
    public static string LastError { get; private set; } = "";

    /// <summary>게임 폴더의 WORLD.CDS 를 연다. 못 열면 null.</summary>
    public static WorldCells? Open(string gameDirectory)
    {
        LastError = "";
        try
        {
            string path = CdsAssetPath.Resolve(gameDirectory, "WORLD.CDS");
            if (!File.Exists(path))
            {
                LastError = "WORLD.CDS가 없고 대체 지도도 내려받지 못했습니다";
                return null;
            }

            var raw = File.ReadAllBytes(path);
            if (raw.Length < RawStride * Height * 2)
            {
                LastError = $"WORLD.CDS 크기가 뜻밖입니다({raw.Length}바이트)";
                return null;
            }
            return new WorldCells(raw);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            LastError = ex.Message;
            return null;
        }
    }

    /// <summary>그 칸의 16비트 낱말. 가로는 감기고, 세로 밖은 뭍으로 본다.</summary>
    public int Word(int x, int y)
    {
        x = ((x % Width) + Width) % Width;
        if (y < 0 || y >= Height) return LandBit;

        bool right = x >= HalfWidth;
        int offset = (y * 2 + (right ? 1 : 0)) * RawStride + (right ? x - HalfWidth : x) * 2;
        return _raw[offset] | (_raw[offset + 1] << 8);
    }

    /// <summary>뭍 비트가 켜진 칸인지.</summary>
    public bool IsLand(int x, int y) => (Word(x, y) & LandBit) != 0;
}
