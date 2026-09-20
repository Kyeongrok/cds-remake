using System.Diagnostics;
using System.Runtime.InteropServices;

namespace CdsHelper.Support.Local.Helpers;

/// <summary>
/// CDS_95.EXE 프로세스 메모리에서 보트 위치(world.cds 셀 좌표)를 직접 읽어 위/경도 반환.
/// OCR 좌표 추적의 대체 경로 — 메뉴 가림에 영향 없고 분 단위 정밀도까지 추출 가능.
///
/// 주소는 프로세스 재시작 시 변경될 수 있어 폴백 경로(OCR)는 유지 권장.
/// </summary>
public sealed class GameMemoryReader : IDisposable
{
    private const string ProcessName = "cds_95";

    // 보트의 월드 셀 좌표 (4바이트씩, 정수). WORLD.CDS 좌표계와 동일.
    // cellX: 0..2500 (서경↔동경), cellY: 0..1250 (북위↔남위)
    private static readonly IntPtr CellXAddress = (IntPtr)0x0019EEE0;
    private static readonly IntPtr CellYAddress = (IntPtr)0x0019EEE4;

    // 현재 입항 중인 도시 ID (1바이트). 도시 안에서만 의미 있음.
    // cities.json의 City.Id와 매핑. 해상에서는 stale/이전 값.
    private static readonly IntPtr CurrentCityIdAddress = (IntPtr)0x005B6154;

    // 서경 51° 이서(대서양 서쪽)에서 cellY 메모리(0x19EEE4)가 다른 용도로 재사용되어 손상.
    // 그 영역에서는 메모리 사용을 포기하고 OCR 폴백. 경계 cellX = (180-51) × 6.944 = 897.
    private const int SafeMinCellX = 898;

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint access, bool inherit, int pid);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ReadProcessMemory(
        IntPtr hProcess, IntPtr address, byte[] buffer, int size, out int read);

    [DllImport("kernel32.dll")]
    private static extern bool CloseHandle(IntPtr h);

    private const uint PROCESS_VM_READ = 0x0010;
    private const uint PROCESS_QUERY_INFORMATION = 0x0400;

    private IntPtr _handle;

    public bool IsAttached => _handle != IntPtr.Zero;

    public GameMemoryReader()
    {
        TryAttach();
    }

    /// <summary>프로세스가 다시 떠 있을 때 재연결 시도.</summary>
    public bool TryAttach()
    {
        if (_handle != IntPtr.Zero) return true;
        var p = Process.GetProcessesByName(ProcessName).FirstOrDefault();
        if (p == null) return false;
        _handle = OpenProcess(PROCESS_VM_READ | PROCESS_QUERY_INFORMATION, false, p.Id);
        return _handle != IntPtr.Zero;
    }

    /// <summary>
    /// 현재 위치(위도/경도)를 메모리에서 읽어 반환. 실패 시 null → 호출 측은 OCR 폴백.
    /// 서경 51° 이서 영역에서는 cellY 손상으로 항상 null 반환 (의도된 동작).
    /// </summary>
    public (double lat, double lon)? TryReadLatLon()
    {
        if (!IsAttached && !TryAttach()) return null;

        var buf = new byte[4];
        if (!ReadProcessMemory(_handle, CellXAddress, buf, 4, out int rx) || rx != 4) return Detach();
        int cellX = BitConverter.ToInt32(buf, 0);

        if (!ReadProcessMemory(_handle, CellYAddress, buf, 4, out int ry) || ry != 4) return Detach();
        int cellY = BitConverter.ToInt32(buf, 0);

        if (cellX < 0 || cellX >= 2500) return null;
        if (cellX < SafeMinCellX) return null;     // 서경 51°↑ 메모리 불안정 영역 → OCR로
        if (cellY < 0 || cellY >= 1250) return null;

        double lon = cellX * 360.0 / WorldMapRenderer.UnfoldedW - 180.0;
        double lat = 90.0 - cellY * 180.0 / WorldMapRenderer.CellH;
        return (lat, lon);
    }

    /// <summary>현재 게임 위치 상태.</summary>
    public enum Location
    {
        /// <summary>프로세스 미실행/연결 실패.</summary>
        GameNotFound,
        /// <summary>바다 위 (월드맵에서 항해 중).</summary>
        AtSea,
        /// <summary>바다가 아닌 곳 (도시/이벤트/전투/타이틀 등).</summary>
        NotAtSea,
    }

    /// <summary>
    /// 현재 위치가 바다인지 도시/이벤트인지 판단. cellX만으로 판단(cellY는 일부 영역에서 손상).
    /// </summary>
    public Location DetectLocation()
    {
        if (!IsAttached && !TryAttach()) return Location.GameNotFound;

        var buf = new byte[4];
        if (!ReadProcessMemory(_handle, CellXAddress, buf, 4, out int read) || read != 4)
        {
            DetachHandle();
            return Location.GameNotFound;
        }

        int cellX = BitConverter.ToInt32(buf, 0);

        // 도시/이벤트 진입 시 cellX가 음수 또는 범위 밖 sentinel 값으로 변함 (검증된 동작)
        if (cellX < 0 || cellX >= 2500)
            return Location.NotAtSea;

        return Location.AtSea;
    }

    /// <summary>현재 입항 중인 도시 ID 읽기. 해상이면 stale 값이라 DetectLocation()로 먼저 도시인지 확인 권장.</summary>
    public byte? TryReadCurrentCityId()
    {
        if (!IsAttached && !TryAttach()) return null;
        var buf = new byte[1];
        if (!ReadProcessMemory(_handle, CurrentCityIdAddress, buf, 1, out int read) || read != 1)
        {
            DetachHandle();
            return null;
        }
        return buf[0];
    }

    /// <summary>변화 감지용 — 셀 좌표 raw 값. 같은 위치면 같은 값. 빠른 비교용.</summary>
    public long? TryReadCellPair()
    {
        if (!IsAttached && !TryAttach()) return null;
        var buf = new byte[8];
        if (!ReadProcessMemory(_handle, CellXAddress, buf, 8, out int read) || read != 8)
        {
            DetachHandle();
            return null;
        }
        return BitConverter.ToInt64(buf, 0);
    }

    private (double, double)? Detach()
    {
        DetachHandle();
        return null;
    }

    private void DetachHandle()
    {
        if (_handle != IntPtr.Zero) { CloseHandle(_handle); _handle = IntPtr.Zero; }
    }

    public void Dispose()
    {
        if (_handle != IntPtr.Zero) { CloseHandle(_handle); _handle = IntPtr.Zero; }
    }
}
