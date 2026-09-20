using System.IO;
using System.Text.Json;

namespace CdsHelper.Support.Local.Settings;

public class ViewOption
{
    public string Name { get; set; } = "";
    public string DisplayName { get; set; } = "";
}

public class AppSettingsData
{
    public double MarkerSize { get; set; } = AppSettings.DefaultMarkerSize;
    public string DefaultView { get; set; } = AppSettings.DefaultDefaultView;
    public string? LastSaveFilePath { get; set; }
    public string? TrailDirectory { get; set; }
    public HashSet<int> CheckedDiscoveryIds { get; set; } = new();
    public WorldMapOptions WorldMap { get; set; } = new();
    /// <summary>
    /// 자동 항해 중에 뜨는 대화창의 확인을 대신 눌러 줄지.
    /// </summary>
    /// <remarks>
    /// <b>설정 창에는 안 낸다.</b> 자동 항해를 쓰는 자리에서 끌 일이 없어 칸만 차지했다 —
    /// <see cref="Local.Helpers.AutoPlayService"/> 가 이 값을 그대로 쓴다.
    /// </remarks>
    public bool AutoConfirmDialog { get; set; } = true;

    public RerollOptions Reroll { get; set; } = new();
}

/// <summary>능력치 리롤 옵션.</summary>
public class RerollOptions
{
    /// <summary>true면 자동 감지한 사냥꾼 버튼 대신 사용자가 지정한 좌표를 클릭한다.</summary>
    public bool UseCustomClickPoint { get; set; }

    /// <summary>사용자 지정 클릭 좌표 (게임 클라이언트 기준).</summary>
    public int ClickX { get; set; }

    public int ClickY { get; set; }
}

public class WorldMapOptions
{
    public bool ShowCoast { get; set; } = true;
    public bool ShowWind { get; set; } = false;
    public bool ShowDiscoveries { get; set; } = true;
    public bool ShowCityLabels { get; set; } = false;
    public bool HideFound { get; set; } = false;
    public bool ShowSpeed { get; set; } = false;
    public double Zoom { get; set; } = 2.0;
    /// <summary>
    /// 자동 스크롤 임계 비율 (0.0~1.0). 마커가 뷰포트 중심에서
    /// (viewport_half × 이 비율)만큼 벗어나면 재중앙 정렬한다.
    /// 0.5 = 뷰포트 중앙 50% 안전영역 (기본), 1.0 = 가장자리까지 가야 스크롤.
    /// </summary>
    public double AutoScrollThreshold { get; set; } = 0.5;

    /// <summary>수동 좌표추적 주기 (초). 매 N초마다 화면 캡처 + OCR.</summary>
    public double TrackingIntervalSeconds { get; set; } = 2.0;
}

public static class AppSettings
{
    public const double DefaultMarkerSize = 11.0;

    public const string DefaultDefaultView = "PlayerContent";

    private static readonly string SettingsFilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "CdsHelper",
        "settings.json");

    /// <summary>
    /// 세계지도 색상 팔레트 JSON 파일 경로 (%APPDATA%\CdsHelper\map_palette.json).
    /// 사용자가 직접 수정 후 세계지도에서 "팔레트 다시 불러오기" 버튼으로 반영.
    /// </summary>
    public static string MapPaletteFilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "CdsHelper",
        "map_palette.json");

    /// <summary>
    /// 발견물 마스터 JSON 파일 경로 (%APPDATA%\CdsHelper\발견물.json).
    /// CSV를 대체하는 source of truth. 좌표/이름 편집이 즉시 저장되며 앱 업데이트로 install 폴더가 갈려도 보존된다.
    /// </summary>
    public static string DiscoveryFilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "CdsHelper",
        "발견물.json");

    public static event Action? SettingsChanged;

    private static double _markerSize = DefaultMarkerSize;
    private static string _defaultView = DefaultDefaultView;
    private static string? _lastSaveFilePath;
    private static HashSet<int> _checkedDiscoveryIds = new();
    private static string _trailDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "trails");
    private static WorldMapOptions _worldMap = new();
    private static bool _autoConfirmDialog = true;
    private static RerollOptions _reroll = new();

    static AppSettings()
    {
        LoadSettings();
    }

    public static double MarkerSize
    {
        get => _markerSize;
        set
        {
            _markerSize = Math.Clamp(value, 4.0, 20.0);
            SaveSettings();
            SettingsChanged?.Invoke();
        }
    }

    public static string DefaultView
    {
        get => _defaultView;
        set
        {
            _defaultView = value;
            SaveSettings();
            SettingsChanged?.Invoke();
        }
    }

    public static string? LastSaveFilePath
    {
        get => _lastSaveFilePath;
        set
        {
            _lastSaveFilePath = value;
            SaveSettings();
        }
    }

    public static string TrailDirectory
    {
        get => _trailDirectory;
        set
        {
            _trailDirectory = value;
            SaveSettings();
        }
    }

    public static HashSet<int> CheckedDiscoveryIds => _checkedDiscoveryIds;

    public static void SetDiscoveryChecked(int discoveryId, bool isChecked)
    {
        if (isChecked)
        {
            _checkedDiscoveryIds.Add(discoveryId);
        }
        else
        {
            _checkedDiscoveryIds.Remove(discoveryId);
        }
        SaveSettings();
    }

    public static bool IsDiscoveryChecked(int discoveryId)
    {
        return _checkedDiscoveryIds.Contains(discoveryId);
    }

    public static WorldMapOptions WorldMap => _worldMap;

    public static void SaveWorldMapOptions()
    {
        SaveSettings();
    }

    public static RerollOptions Reroll => _reroll;

    public static void SaveRerollOptions()
    {
        SaveSettings();
    }

    /// <summary>
    /// 자동 항해 중 대화창 "확인" 버튼을 OCR/템플릿 매칭으로 자동 클릭할지 여부.
    /// 끄면 다이얼로그가 떠도 클릭하지 않고 사용자가 수동으로 처리하게 둔다.
    /// </summary>
    public static bool AutoConfirmDialog
    {
        get => _autoConfirmDialog;
        set
        {
            _autoConfirmDialog = value;
            SaveSettings();
            SettingsChanged?.Invoke();
        }
    }

    public static readonly List<ViewOption> AvailableViews = new()
    {
        new() { Name = "PlayerContent", DisplayName = "플레이어" },
        new() { Name = "CharacterContent", DisplayName = "항해사" },
        new() { Name = "ItemContent", DisplayName = "아이템" },
        new() { Name = "WorldMapContent", DisplayName = "세계지도" },
        new() { Name = "SphinxCalculatorContent", DisplayName = "스핑크스" }
    };

    private static void LoadSettings()
    {
        try
        {
            if (File.Exists(SettingsFilePath))
            {
                var json = File.ReadAllText(SettingsFilePath);
                var data = JsonSerializer.Deserialize<AppSettingsData>(json);
                if (data != null)
                {
                    _markerSize = data.MarkerSize;
                    // 뺀 화면(도서·도시·발견물·지도·후원자)을 시작 화면으로 적어 둔 설정은 기본 화면으로 돌린다 —
                    // 없는 화면으로 가면 본문이 텅 빈 채로 뜬다.
                    _defaultView = AvailableViews.Exists(v => v.Name == data.DefaultView)
                        ? data.DefaultView
                        : DefaultDefaultView;
                    _lastSaveFilePath = data.LastSaveFilePath;
                    if (!string.IsNullOrEmpty(data.TrailDirectory))
                        _trailDirectory = data.TrailDirectory;
                    _checkedDiscoveryIds = data.CheckedDiscoveryIds ?? new();
                    _worldMap = data.WorldMap ?? new();
                    _autoConfirmDialog = data.AutoConfirmDialog;
                    _reroll = data.Reroll ?? new();
                }
            }
        }
        catch
        {
            // 설정 로드 실패 시 기본값 사용
        }
    }

    private static void SaveSettings()
    {
        try
        {
            var directory = Path.GetDirectoryName(SettingsFilePath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var data = new AppSettingsData
            {
                MarkerSize = _markerSize,
                DefaultView = _defaultView,
                LastSaveFilePath = _lastSaveFilePath,
                TrailDirectory = _trailDirectory,
                CheckedDiscoveryIds = _checkedDiscoveryIds,
                WorldMap = _worldMap,
                AutoConfirmDialog = _autoConfirmDialog,
                Reroll = _reroll
            };

            var json = JsonSerializer.Serialize(data, new JsonSerializerOptions
            {
                WriteIndented = true
            });
            File.WriteAllText(SettingsFilePath, json);
        }
        catch
        {
            // 설정 저장 실패 시 무시
        }
    }
}
