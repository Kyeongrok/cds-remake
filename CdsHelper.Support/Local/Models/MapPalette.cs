using System.IO;
using System.Text.Json;
using System.Windows.Media;

namespace CdsHelper.Support.Local.Models;

/// <summary>
/// 세계지도 렌더링에 쓰이는 색상 팔레트.
/// JSON 파일로 저장/로드되며, 사용자가 수정 후 "팔레트 다시 불러오기"로 반영한다.
/// </summary>
public class MapPalette
{
    /// <summary>바다 기본색</summary>
    public string SeaBase { get; set; } = "#395367";

    /// <summary>해안선 색 (바다 쪽 attr=0 셀). 바람 표시와 무관하게 항상 이 색 사용.</summary>
    public string Coastline { get; set; } = "#2D4658";

    /// <summary>바람/해류(바다) attr → 색상</summary>
    public Dictionary<string, string> Wind { get; set; } = new();

    /// <summary>기본 바람색 (매칭되는 키가 없을 때)</summary>
    public string WindDefault { get; set; } = "#446075";

    /// <summary>육지 attr → 색상 (64=사막, 66=초원, 67=밀림, 68=온대림)</summary>
    public Dictionary<string, string> Land { get; set; } = new();

    /// <summary>기본 육지색 (매칭되는 키가 없을 때)</summary>
    public string LandDefault { get; set; } = "#897E5E";

    /// <summary>사막 색 (attr 86-116 중 <see cref="MountainAttrThreshold"/> 미만)</summary>
    public string Desert { get; set; } = "#C8B087";

    /// <summary>산 색 (attr 86-116 중 <see cref="MountainAttrThreshold"/> 이상)</summary>
    public string Mountain { get; set; } = "#8B6F52";

    /// <summary>
    /// 산/사막 구분 임계 attr 값.
    /// 이 값 미만은 사막, 이상은 산으로 표시한다.
    /// 게임 실제 attr 할당이 정확하지 않으면 이 값을 조정해 둘을 구분.
    /// </summary>
    public int MountainAttrThreshold { get; set; } = 100;

    /// <summary>기본 팔레트 (초기 배포용).</summary>
    public static MapPalette CreateDefault() => new()
    {
        SeaBase = "#395367",
        Coastline = "#2D4658",
        WindDefault = "#446075",
        Wind = new Dictionary<string, string>
        {
            ["1"] = "#324B5D",
            ["5"] = "#3A5469",
            ["6"] = "#375266",
            ["7"] = "#3E586C",
            ["8"] = "#415A6E",
            ["9"] = "#3C566A",
            ["10"] = "#3F596D",
        },
        LandDefault = "#897E5E",
        Land = new Dictionary<string, string>
        {
            ["64"] = "#A18666",   // 사막
            ["66"] = "#948B6C",   // 해안 근처 육지
            ["67"] = "#B1A889",   // 중간 내륙 (해안보다 밝음)
            ["68"] = "#B7B09B",   // 깊은 내륙 (가장 밝음)
        },
        Desert = "#C8B087",
        Mountain = "#8B6F52",
        MountainAttrThreshold = 100,
    };

    /// <summary>JSON 파일에서 로드. 실패 시 기본 팔레트 반환.</summary>
    public static MapPalette LoadOrDefault(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                var json = File.ReadAllText(path);
                var loaded = JsonSerializer.Deserialize<MapPalette>(json, SerializerOptions);
                if (loaded != null) return loaded;
            }
        }
        catch { /* fallthrough to default */ }
        return CreateDefault();
    }

    /// <summary>JSON으로 저장.</summary>
    public void Save(string path)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        File.WriteAllText(path, JsonSerializer.Serialize(this, SerializerOptions));
    }

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
    };

    // ---- 렌더링에 쓰는 해석 메서드 ----

    public Color ResolveSeaBase() => Parse(SeaBase);

    public Color ResolveWind(byte wind)
    {
        if (wind == 0) return Parse(Coastline);
        if (Wind.TryGetValue(wind.ToString(), out var hex)) return Parse(hex);
        return Parse(WindDefault);
    }

    /// <summary>해안선 색을 얻는다 (ShowWind 꺼져 있을 때도 호출).</summary>
    public Color ResolveCoastline() => Parse(Coastline);

    public Color ResolveLand(byte attr)
    {
        if (Land.TryGetValue(attr.ToString(), out var hex)) return Parse(hex);
        if (attr >= 86 && attr <= 116)
            return attr >= MountainAttrThreshold ? Parse(Mountain) : Parse(Desert);
        return Parse(LandDefault);
    }

    private static Color Parse(string hex)
    {
        try
        {
            var obj = ColorConverter.ConvertFromString(hex);
            if (obj is Color c) return c;
        }
        catch { }
        return Colors.Magenta;
    }
}
