namespace CdsHelper.Support.Local.Helpers;

/// <summary>
/// 두 좌표 간 방위각 계산 및 화면 오프셋 변환.
/// </summary>
public static class NavigationCalculator
{
    /// <summary>
    /// 두 좌표 간 방위각을 계산한다 (0=N, 90=E, 180=S, 270=W).
    /// 게임 좌표는 실제 구면이 아니므로 평면 근사를 사용한다.
    /// </summary>
    public static double BearingDegrees(double lat1, double lon1, double lat2, double lon2)
    {
        var dlat = lat2 - lat1;
        var dlon = lon2 - lon1;

        // atan2(동쪽, 북쪽) → 북쪽 기준 시계방향 각도
        var angle = Math.Atan2(dlon, dlat) * (180.0 / Math.PI);
        return ((angle % 360) + 360) % 360;
    }

    /// <summary>
    /// 방위각을 화면 오프셋 (dx, dy)으로 변환한다.
    /// 화면 좌표계: x 오른쪽 +, y 아래쪽 +
    /// 방위각: 0=N(위), 90=E(오른쪽), 180=S(아래), 270=W(왼쪽)
    /// </summary>
    public static (int dx, int dy) BearingToScreenOffset(double bearing, int radius = 100)
    {
        var rad = bearing * (Math.PI / 180.0);
        var dx = (int)(radius * Math.Sin(rad));
        var dy = (int)(-radius * Math.Cos(rad)); // 화면 y축은 아래가 +
        return (dx, dy);
    }

    /// <summary>
    /// 두 좌표가 threshold 이내로 가까운지 판정.
    /// </summary>
    public static bool IsNear(double lat1, double lon1, double lat2, double lon2, double threshold = 3.0)
    {
        var dlat = lat2 - lat1;
        var dlon = lon2 - lon1;
        return Math.Sqrt(dlat * dlat + dlon * dlon) < threshold;
    }
}
