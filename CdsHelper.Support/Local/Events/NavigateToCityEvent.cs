using Prism.Events;

namespace CdsHelper.Support.Local.Events;

/// <summary>
/// 지도에서 특정 도시로 이동 요청 이벤트
/// </summary>
public class NavigateToCityEvent : PubSubEvent<NavigateToCityEventArgs>
{
}

/// <summary>
/// 도시 이동 이벤트 인자
/// </summary>
public class NavigateToCityEventArgs
{
    public byte CityId { get; set; }
    public string CityName { get; set; } = string.Empty;
    public int? PixelX { get; set; }
    public int? PixelY { get; set; }
}
