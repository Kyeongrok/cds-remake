using Vortice.Direct3D11;
using Vortice.DXGI;

namespace CdsHelper.Game.UI.Views;

/// <summary>
/// 크래시 보고에 붙일 그래픽 카드 정보 — 어느 GPU · 드라이버였는지, 장치가 끊겼으면 왜 끊겼는지.
/// </summary>
/// <remarks>
/// 버그 리포트에 <c>DXGI_ERROR_DEVICE_REMOVED</c> 가 왔는데(지도 렌더러 <c>CreateCellMap</c>) 무슨 카드인지,
/// 드라이버가 멈췄는지 · 리셋됐는지를 몰라 원인을 못 좁혔다. 그래서 장치를 열 때 카드 정보를 한 번 적어 두고
/// (끊긴 뒤에는 못 물을 수 있다), 장치 분실 오류가 나면 <see cref="ID3D11Device.DeviceRemovedReason"/> 를 묻는다.
/// 여기서 무엇이 터져도 삼킨다 — 보고를 적다가 또 오류가 나면 보고조차 못 남는다.
/// </remarks>
public static class GpuInfo
{
    private static WeakReference<ID3D11Device>? _device;
    private static string _adapter = "";

    /// <summary>지도 렌더러가 장치를 연 뒤 부른다.</summary>
    public static void Watch(ID3D11Device device, bool software)
    {
        _device = new WeakReference<ID3D11Device>(device);
        try
        {
            using var dxgi = device.QueryInterface<IDXGIDevice>();
            using var adapter = dxgi.GetAdapter();
            var d = adapter.Description;
            string driver = adapter.CheckInterfaceSupport(typeof(IDXGIDevice), out long umd)
                ? $"{(umd >> 48) & 0xFFFF}.{(umd >> 32) & 0xFFFF}.{(umd >> 16) & 0xFFFF}.{umd & 0xFFFF}"
                : "?";
            _adapter = $"{d.Description.Trim()}  ·  VEN_{d.VendorId:X4} DEV_{d.DeviceId:X4}  ·  "
                       + $"VRAM {(ulong)d.DedicatedVideoMemory / (1024 * 1024)}MB  ·  드라이버 {driver}"
                       + (software ? "  ·  WARP(소프트웨어)" : "");
        }
        catch (Exception ex)
        {
            _adapter = $"(그래픽 카드 정보를 못 읽음: {ex.GetType().Name})";
        }
    }

    /// <summary>보고 글에 붙일 줄 — 지도 장치를 아직 안 열었으면 빈 글이다.</summary>
    public static string Describe(Exception ex)
    {
        if (_adapter.Length == 0) return "";
        string text = "그래픽: " + _adapter;
        if (LostCode(ex) is { } code)
        {
            text += Environment.NewLine + $"장치 분실: {Name(code)}";
            try
            {
                if (_device != null && _device.TryGetTarget(out var device))
                {
                    int reason = device.DeviceRemovedReason.Code;
                    text += $"  ·  이유 {Name(reason)}";
                }
            }
            catch (Exception inner) { text += $"  ·  이유를 못 물음({inner.GetType().Name})"; }
        }
        return text;
    }

    /// <summary>오류(안쪽 오류까지)가 장치 분실 갈래이면 그 HRESULT.</summary>
    private static int? LostCode(Exception? ex)
    {
        for (; ex != null; ex = ex.InnerException)
            if (unchecked((uint)ex.HResult) is 0x887A0005 or 0x887A0006 or 0x887A0007 or 0x887A0020) return ex.HResult;
        return null;
    }

    private static string Name(int code) => unchecked((uint)code) switch
    {
        0 => "S_OK(끊기지 않음)",
        0x887A0005 => "DXGI_ERROR_DEVICE_REMOVED",
        0x887A0006 => "DXGI_ERROR_DEVICE_HUNG(GPU 가 명령을 너무 오래 붙잡음)",
        0x887A0007 => "DXGI_ERROR_DEVICE_RESET(드라이버가 장치를 리셋함)",
        0x887A0020 => "DXGI_ERROR_DRIVER_INTERNAL_ERROR(드라이버 내부 오류)",
        0x887A0001 => "DXGI_ERROR_INVALID_CALL",
        0x8007000E => "E_OUTOFMEMORY",
        _ => $"0x{unchecked((uint)code):X8}",
    };
}
