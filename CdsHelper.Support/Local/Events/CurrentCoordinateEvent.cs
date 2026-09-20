using Prism.Events;

namespace CdsHelper.Support.Local.Events;

/// <summary>
/// 게임에서 인식된 현재 좌표를 지도에 전달하는 이벤트
/// </summary>
public class CurrentCoordinateEvent : PubSubEvent<CurrentCoordinateEventArgs>
{
}

public class CurrentCoordinateEventArgs
{
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public bool IsTracking { get; set; }
    /// <summary>인식 실패로 마지막 좌표를 재사용하는 경우 true</summary>
    public bool IsStale { get; set; }
    /// <summary>게임 내 날짜 (예: "1505년 9월 23일")</summary>
    public string? GameDate { get; set; }
}
