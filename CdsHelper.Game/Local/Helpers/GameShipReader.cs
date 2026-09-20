using System.Diagnostics;
using System.Runtime.InteropServices;
using CdsHelper.Support.Local.Helpers;

namespace CdsHelper.Game.Local.Helpers;

/// <summary>
/// 실행 중인 CDS_95 에서 함대의 자리·방향과 게임이 실제로 그리는 함대 그림을 읽어 온다.
/// 읽기만 한다.
/// </summary>
/// <remarks>
/// 자리와 고르는 규칙은 cds95-mod plugins-src/WorldMapKR/src/sprite.c 에서 가져왔고,
/// 그쪽은 게임 렌더러 0x48A1E0(0x48A82E~0x48A8A4)에서 읽어낸 것이다.
/// 게임 함수를 부르지 않고 값만 짚는다 — 남의 프로세스에서 코드를 부를 수도 없거니와,
/// 그리는 도중에 부르면 게임 상태가 꼬인다.
///
/// 주소는 모듈 기준 VA 다(이미지 베이스 0x400000 + RVA). 32비트 고정 베이스라
/// <see cref="GameMemoryReader"/> 가 쓰는 0x5B6154 같은 값과 같은 좌표계다.
/// </remarks>
public sealed class GameShipReader : IDisposable
{
    private const string ProcessName = "cds_95";

    // sprite.c 의 RVA 에 이미지 베이스 0x400000 을 더한 값.
    private const int AtlasSea = 0x5D68C8;    // 배 4벌 x 8방향
    private const int AtlasLand = 0x6092D0;   // 말(대상) — 육상·정박
    private const int Docked = 0x5B61B4;      // 0 이면 항해 중
    private const int Heading = 0x5B63C8;     // 16방향
    private const int ClassTab = 0x5695D8;    // 함선종류 -> 그림 클래스
    private const int LandBase = 0x569550;    // 말 그림의 밑번호
    private const int FleetObj = 0x5B3928;    // 함대 객체. +4 가 여덟 칸의 첫 칸
    private const int ShipArray = 0x5A4E18;   // 배 struct 배열
    private const int ShipStride = 0x6C;
    private const int ShipTypeOffset = 0x28;

    // 함대 위치 원본값. 경도 0~40000(0=서경180), 위도 0~20000(0=북위90).
    // GameMemoryReader 의 셀 좌표(0x19EEE0)보다 16배 곱게 나와 배를 부드럽게 움직이기에 맞다.
    private const int PosLon = 0x5B63B0;
    private const int PosLat = 0x5B63B4;

    public const int SpriteW = 48;
    public const int SpriteSize = SpriteW * SpriteW;   // 2304
    private const int SeaFrames = 32;
    private const int LandFrames = 32;

    /// <summary>말 아틀라스는 한 방향에 여덟 걸음이다(4방향 x 8걸음 = 32장).</summary>
    private const int LandPhases = 8;

    public const int LonRawMax = 40000;
    public const int LatRawMax = 20000;

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint access, bool inherit, int pid);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ReadProcessMemory(IntPtr hProcess, IntPtr address, byte[] buffer, int size, out int read);

    [DllImport("kernel32.dll")]
    private static extern bool CloseHandle(IntPtr h);

    private const uint ProcessVmRead = 0x0010;
    private const uint ProcessQueryInformation = 0x0400;

    private IntPtr _handle;
    private readonly byte[] _word = new byte[4];
    private readonly byte[] _sprite = new byte[SpriteSize];

    public bool IsAttached => _handle != IntPtr.Zero;

    /// <summary>게임이 떠 있으면 붙는다. 이미 붙어 있으면 그대로 true.</summary>
    public bool TryAttach()
    {
        if (_handle != IntPtr.Zero) return true;
        var p = Process.GetProcessesByName(ProcessName).FirstOrDefault();
        if (p == null) return false;
        _handle = OpenProcess(ProcessVmRead | ProcessQueryInformation, false, p.Id);
        return _handle != IntPtr.Zero;
    }

    private void Detach()
    {
        if (_handle != IntPtr.Zero) { CloseHandle(_handle); _handle = IntPtr.Zero; }
    }

    private bool ReadInt(int address, out int value)
    {
        value = 0;
        if (!ReadProcessMemory(_handle, (IntPtr)address, _word, 4, out int got) || got != 4) return false;
        value = BitConverter.ToInt32(_word, 0);
        return true;
    }

    /// <summary>지금 함대 자리. 칸 좌표(x 0~2500, y 0~1250)로 소수까지 돌려준다.</summary>
    public (double CellX, double CellY)? TryReadCell()
    {
        if (!IsAttached && !TryAttach()) return null;
        if (!ReadInt(PosLon, out int lon) || !ReadInt(PosLat, out int lat)) { Detach(); return null; }
        if (lon < 0 || lon > LonRawMax || lat < 0 || lat > LatRawMax) return null;
        // 둘 다 0 은 날짜변경선 위의 북극이라 게임에 나올 수 없다 — 아직 값이 안 찬 것이다.
        if (lon == 0 && lat == 0) return null;
        return (lon * (double)WorldMapRenderer.UnfoldedW / LonRawMax,
                lat * (double)WorldMapRenderer.CellH / LatRawMax);
    }

    /// <summary>기함의 함선종류. 못 잡으면 -1. sprite.c FlagshipType 과 같은 길이다.</summary>
    private int ReadFlagshipType()
    {
        if (!ReadInt(FleetObj + 4, out int id)) return -1;
        if (id < 0 || id >= 16) return -1;
        if (!ReadInt(ShipArray + id * ShipStride + ShipTypeOffset, out int type)) return -1;
        return type;
    }

    /// <summary>
    /// 지금 그려야 할 48x48 팔레트 색인 그림. 항해 중이면 배, 육상·정박이면 말이 나온다.
    /// 색인 0 은 비침이고 색은 <see cref="OceanPalette"/> 와 같은 표다. 못 읽으면 null.
    /// </summary>
    public byte[]? TryReadSprite()
    {
        if (!IsAttached && !TryAttach()) return null;
        if (!ReadInt(Docked, out int docked) || !ReadInt(Heading, out int heading)) { Detach(); return null; }
        return TryReadSprite(heading, docked != 0);
    }

    /// <summary>
    /// 방향을 밖에서 정해 그림을 가져온다. 게임 함대를 따라가는 대신 우리가 배를 몰 때 쓴다 —
    /// 그림은 게임 것을 그대로 쓰되 어느 쪽을 보는지는 우리가 정한다.
    /// </summary>
    /// <param name="heading16">16방향(0~15).</param>
    /// <param name="onLand">참이면 말(육상·정박) 그림.</param>
    /// <param name="phase">
    /// 말의 걸음 번호(0~7). -1 이면 게임이 들고 있는 번호(<c>0x00569550</c>)를 쓴다.
    /// 우리가 말을 몰 때는 우리 걸음을 넣어야 다리가 움직인다 — 게임 쪽 번호는 게임이
    /// 걸을 때만 는다.
    /// </param>
    public byte[]? TryReadSprite(int heading16, bool onLand, int phase = -1)
    {
        if (!IsAttached && !TryAttach()) return null;
        int heading = heading16 & 0xF;
        bool docked = onLand;

        int frame, atlas, frames;
        if (!docked)
        {
            int cls = 0;
            int type = ReadFlagshipType();
            if (type >= 0 && type < 16 && ReadInt(ClassTab + type * 4, out int c) && c >= 0 && c < 4)
                cls = c;
            frame = cls * 8 + (heading >> 1);     // 16방향을 8방향으로 접는다
            atlas = AtlasSea;
            frames = SeaFrames;
        }
        else
        {
            int d = heading + 1;
            if (d < 0) d = -d;
            d &= 0xF;
            int landBase;
            if (phase >= 0) landBase = phase % LandPhases;
            else ReadInt(LandBase, out landBase);
            frame = (d >> 2) * 8 + landBase;
            atlas = AtlasLand;
            frames = LandFrames;
        }
        if (frame < 0 || frame >= frames) frame = 0;

        if (!ReadProcessMemory(_handle, (IntPtr)(atlas + frame * SpriteSize), _sprite, SpriteSize, out int got)
            || got != SpriteSize) return null;
        return _sprite;
    }

    public void Dispose() => Detach();
}
