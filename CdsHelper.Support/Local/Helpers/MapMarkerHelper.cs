using System.Windows.Controls;
using System.Windows.Media;
using CdsHelper.Support.Local.Models;
using CdsHelper.Support.Local.Settings;
using CdsHelper.Support.Local.ViewModel;
using CdsHelper.Support.UI.Units;

namespace CdsHelper.Support.Local.Helpers;

public static class MapMarkerHelper
{
    /// <summary>
    /// Canvas에 도시 마커들을 추가합니다.
    /// </summary>
    public static void AddCityMarkers(Canvas canvas, IEnumerable<City> cities, bool showLabels = false, bool showCoordinates = false, double markerSize = AppSettings.DefaultMarkerSize)
    {
        if (canvas == null) return;

        foreach (var city in cities)
        {
            if (!city.PixelX.HasValue || !city.PixelY.HasValue || city.PixelX <= 0 || city.PixelY <= 0)
                continue;

            var x = city.PixelX.Value;
            var y = city.PixelY.Value;

            var viewModel = new CityMarkerViewModel(city, markerSize)
            {
                ShowLabel = showLabels,
                ShowCoordinates = showCoordinates
            };

            var marker = new CityMarker(x, y, city.Name, city.Latitude, city.Longitude, city.HasLibrary, city.HasShipyard, city.Id, markerSize)
            {
                ShowLabel = showLabels,
                ShowCoordinates = showCoordinates,
                DataContext = viewModel
            };
            canvas.Children.Add(marker);
        }
    }

    /// <summary>
    /// 모든 마커의 크기를 변경합니다.
    /// </summary>
    public static void SetMarkerSize(Canvas canvas, double size)
    {
        if (canvas == null) return;

        foreach (var child in canvas.Children)
        {
            if (child is CityMarker marker)
            {
                marker.MarkerSize = size;
            }
        }
    }

    /// <summary>
    /// 도시 라벨 표시 여부를 변경합니다.
    /// </summary>
    public static void SetLabelsVisibility(Canvas canvas, bool visible)
    {
        if (canvas == null) return;

        foreach (var child in canvas.Children)
        {
            if (child is CityMarker marker)
            {
                marker.ShowLabel = visible;
            }
        }
    }

    /// <summary>
    /// 좌표 표시 여부를 변경합니다.
    /// </summary>
    public static void SetCoordinatesVisibility(Canvas canvas, bool visible)
    {
        if (canvas == null) return;

        foreach (var child in canvas.Children)
        {
            if (child is CityMarker marker)
            {
                marker.ShowCoordinates = visible;
            }
        }
    }

    /// <summary>
    /// 조선소 마커 표시 여부를 변경합니다.
    /// </summary>
    public static void SetShipyardMarkersVisibility(Canvas canvas, bool visible)
    {
        if (canvas == null) return;

        foreach (var child in canvas.Children)
        {
            if (child is CityMarker marker)
            {
                marker.ShowShipyardMarker = visible;
            }
        }
    }

    /// <summary>
    /// Canvas의 모든 자식 요소를 제거합니다.
    /// </summary>
    public static void ClearMarkers(Canvas canvas)
    {
        canvas?.Children.Clear();
    }

    /// <summary>
    /// 문화권별 색상 맵핑
    /// </summary>
    private static readonly Dictionary<string, Color> CulturalSphereColors = new()
    {
        { "유럽", Colors.Blue },
        { "이슬람", Colors.Green },
        { "인도", Colors.Orange },
        { "동남아시아", Colors.Purple },
        { "동아시아", Colors.Red },
        { "오세아니아", Colors.Cyan },
        { "아프리카", Colors.Brown },
        { "신대륙", Colors.Gold },
        { "북해", Colors.DarkBlue },
        { "지중해", Colors.DodgerBlue },
        { "서아프리카", Colors.DarkOliveGreen },
        { "동아프리카", Colors.Sienna },
        { "아라비아", Colors.DarkGreen },
        { "페르시아", Colors.Teal },
        { "중앙아시아", Colors.Maroon },
        { "일본", Colors.Crimson },
        { "중국", Colors.Tomato },
        { "조선", Colors.IndianRed },
    };

    /// <summary>
    /// 문화권에 해당하는 색상을 반환합니다.
    /// </summary>
    private static Color GetCulturalSphereColor(string culturalSphere)
    {
        if (string.IsNullOrEmpty(culturalSphere))
            return Colors.Gray;

        return CulturalSphereColors.GetValueOrDefault(culturalSphere, Colors.Gray);
    }

    /// <summary>
    /// 도시 목록에서 문화권별 바운딩 박스를 계산합니다.
    /// </summary>
    public static Dictionary<string, (double MinX, double MinY, double MaxX, double MaxY)> CalculateCulturalSphereBounds(IEnumerable<City> cities)
    {
        var result = new Dictionary<string, (double MinX, double MinY, double MaxX, double MaxY)>();

        var citiesWithCoords = cities
            .Where(c => c.PixelX.HasValue && c.PixelY.HasValue && c.PixelX > 0 && c.PixelY > 0 && !string.IsNullOrEmpty(c.CulturalSphere))
            .ToList();

        var grouped = citiesWithCoords.GroupBy(c => c.CulturalSphere);

        foreach (var group in grouped)
        {
            var culturalSphere = group.Key!;
            var cityList = group.ToList();

            if (cityList.Count < 2) continue; // 최소 2개 이상의 도시가 있어야 영역 표시

            var minX = cityList.Min(c => c.PixelX!.Value);
            var maxX = cityList.Max(c => c.PixelX!.Value);
            var minY = cityList.Min(c => c.PixelY!.Value);
            var maxY = cityList.Max(c => c.PixelY!.Value);

            // 마진 추가 (마커가 영역 안에 잘 보이도록)
            const int margin = 30;
            result[culturalSphere] = (minX - margin, minY - margin, maxX + margin, maxY + margin);
        }

        return result;
    }

    /// <summary>
    /// Canvas에 문화권 영역 마커들을 추가합니다.
    /// </summary>
    public static void AddAreaMarkers(Canvas canvas, IEnumerable<City> cities, bool showLabels = true)
    {
        if (canvas == null) return;

        var bounds = CalculateCulturalSphereBounds(cities);

        foreach (var kvp in bounds)
        {
            var culturalSphere = kvp.Key;
            var (minX, minY, maxX, maxY) = kvp.Value;
            var color = GetCulturalSphereColor(culturalSphere);

            var areaMarker = new AreaMarker(minX, minY, maxX, maxY, culturalSphere, color)
            {
                ShowLabel = showLabels
            };

            // 영역 마커는 도시 마커 뒤에 표시되도록 맨 앞에 삽입
            canvas.Children.Insert(0, areaMarker);
        }
    }

    /// <summary>
    /// 영역 마커의 라벨 표시 여부를 변경합니다.
    /// </summary>
    public static void SetAreaLabelsVisibility(Canvas canvas, bool visible)
    {
        if (canvas == null) return;

        foreach (var child in canvas.Children)
        {
            if (child is AreaMarker marker)
            {
                marker.ShowLabel = visible;
            }
        }
    }

    /// <summary>
    /// 영역 마커의 표시 여부를 변경합니다.
    /// </summary>
    public static void SetAreaMarkersVisibility(Canvas canvas, bool visible)
    {
        if (canvas == null) return;

        foreach (var child in canvas.Children)
        {
            if (child is AreaMarker marker)
            {
                marker.Visibility = visible ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
            }
        }
    }

    /// <summary>
    /// 영역 마커만 제거합니다.
    /// </summary>
    public static void ClearAreaMarkers(Canvas canvas)
    {
        if (canvas == null) return;

        var areaMarkers = canvas.Children.OfType<AreaMarker>().ToList();
        foreach (var marker in areaMarkers)
        {
            canvas.Children.Remove(marker);
        }
    }
}
