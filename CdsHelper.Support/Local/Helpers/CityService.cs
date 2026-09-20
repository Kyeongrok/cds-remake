using System.IO;
using CdsHelper.Api.Controllers;
using CdsHelper.Api.Entities;
using CdsHelper.Api.Migrations;
using CdsHelper.Support.Local.Models;

namespace CdsHelper.Support.Local.Helpers;

public class CityService
{
    private CityController? _controller;
    private List<City> _cachedCities = new();
    private bool _initialized;
    // 여러 ViewModel(CdsHelperViewModel, CityContentViewModel 등)이 동시에 InitializeAsync를
    // 호출해서 마이그레이션이 이중으로 돌면 EF Core tracker가 같은 Id 엔티티 두 번을 추적해 예외.
    // 첫 호출만 실제 초기화하고 나머지는 대기 후 빠져나오게 serialize.
    private readonly SemaphoreSlim _initLock = new(1, 1);

    /// <summary>
    /// Controller 초기화 및 데이터 마이그레이션
    /// </summary>
    public async Task InitializeAsync(string dbPath, string? jsonPath = null)
    {
        if (_initialized) return;
        await _initLock.WaitAsync();
        try
        {
            if (_initialized) return;

            _controller = CityController.Create(dbPath);

            // JSON 파일이 있으면 마이그레이션 시도
            if (!string.IsNullOrEmpty(jsonPath) && System.IO.File.Exists(jsonPath))
            {
                await DataMigrator.MigrateCitiesFromJsonAsync(
                    _controller,
                    jsonPath,
                    onSkipped: msg => EventQueueService.Instance.MigrationSkipped("CityService", msg),
                    onMigrated: msg => EventQueueService.Instance.DataLoaded("CityService", msg));
            }

            // 캐시 로드
            await RefreshCacheAsync();
            _initialized = true;
        }
        finally
        {
            _initLock.Release();
        }
    }

    /// <summary>
    /// 캐시 새로고침
    /// </summary>
    private async Task RefreshCacheAsync()
    {
        if (_controller == null) return;

        var entities = await _controller.GetAllCitiesAsync();
        _cachedCities = entities.Select(ToModel).ToList();
    }

    /// <summary>
    /// 모든 도시 로드 (동기 - 기존 호환성)
    /// </summary>
    public List<City> LoadCities(string filePath)
    {
        // 기존 방식 유지 (JSON에서 직접 로드)
        if (!System.IO.File.Exists(filePath))
        {
            throw new FileNotFoundException($"cities.json 파일을 찾을 수 없습니다: {filePath}");
        }

        var json = System.IO.File.ReadAllText(filePath);
        var cities = System.Text.Json.JsonSerializer.Deserialize<List<City>>(json);

        return cities ?? new List<City>();
    }

    /// <summary>
    /// DB에서 모든 도시 로드 (비동기)
    /// </summary>
    public async Task<List<City>> LoadCitiesFromDbAsync()
    {
        if (_controller == null)
            throw new InvalidOperationException("CityService가 초기화되지 않았습니다. InitializeAsync를 먼저 호출하세요.");

        var entities = await _controller.GetAllCitiesAsync();
        return entities.Select(ToModel).ToList();
    }

    /// <summary>
    /// 캐시된 도시 목록 반환
    /// </summary>
    public List<City> GetCachedCities()
    {
        return _cachedCities;
    }

    /// <summary>
    /// 필터링된 도시 목록 반환 (DB 쿼리)
    /// </summary>
    public async Task<List<City>> FilterFromDbAsync(
        string? nameSearch = null,
        string? culturalSphere = null,
        bool? libraryOnly = null,
        bool? shipyardOnly = null)
    {
        if (_controller == null)
            throw new InvalidOperationException("CityService가 초기화되지 않았습니다.");

        var entities = await _controller.GetCitiesByFilterAsync(
            nameSearch,
            culturalSphere,
            libraryOnly == true ? true : null,
            shipyardOnly == true ? true : null);

        return entities.Select(ToModel).ToList();
    }

    /// <summary>
    /// 필터링 (기존 호환성 - 메모리 필터)
    /// </summary>
    public List<City> Filter(
        IEnumerable<City> cities,
        string? nameSearch = null,
        string? culturalSphere = null,
        bool libraryOnly = false,
        bool shipyardOnly = false)
    {
        var filtered = cities.AsEnumerable();

        if (!string.IsNullOrWhiteSpace(nameSearch))
        {
            filtered = filtered.Where(c => c.Name.Contains(nameSearch, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(culturalSphere))
        {
            filtered = filtered.Where(c => c.CulturalSphere != null &&
                c.CulturalSphere.Equals(culturalSphere, StringComparison.OrdinalIgnoreCase));
        }

        if (libraryOnly)
        {
            filtered = filtered.Where(c => c.HasLibrary);
        }

        if (shipyardOnly)
        {
            filtered = filtered.Where(c => c.HasShipyard);
        }

        return filtered.ToList();
    }

    /// <summary>
    /// 문화권 목록 (DB)
    /// </summary>
    public async Task<List<string>> GetDistinctCulturalSpheresFromDbAsync()
    {
        if (_controller == null)
            throw new InvalidOperationException("CityService가 초기화되지 않았습니다.");

        return await _controller.GetCulturalSpheresAsync();
    }

    /// <summary>
    /// 문화권 목록 (기존 호환성 - 메모리)
    /// </summary>
    public List<string> GetDistinctCulturalSpheres(IEnumerable<City> cities)
    {
        return cities
            .Select(c => c.CulturalSphere)
            .Where(cs => !string.IsNullOrWhiteSpace(cs))
            .Distinct()
            .OrderBy(cs => cs)
            .ToList()!;
    }

    /// <summary>
    /// 도시명 조회 (DB)
    /// </summary>
    public async Task<string> GetCityNameFromDbAsync(byte index)
    {
        if (_controller == null)
            throw new InvalidOperationException("CityService가 초기화되지 않았습니다.");

        return await _controller.GetCityNameAsync(index);
    }

    /// <summary>
    /// 도시명 조회 (기존 호환성 - 메모리)
    /// </summary>
    public string GetCityName(byte index, IEnumerable<City> cities)
    {
        if (index == 255)
            return "함대소속";

        var city = cities.FirstOrDefault(c => c.Id == index);
        return city?.Name ?? $"미확인({index})";
    }

    /// <summary>
    /// 좌표가 있는 도시 목록 (DB)
    /// </summary>
    public async Task<List<City>> GetCitiesWithCoordinatesFromDbAsync()
    {
        if (_controller == null)
            throw new InvalidOperationException("CityService가 초기화되지 않았습니다.");

        var entities = await _controller.GetCitiesWithCoordinatesAsync();
        return entities.Select(ToModel).ToList();
    }

    /// <summary>
    /// 픽셀 좌표 업데이트 (DB)
    /// </summary>
    public async Task<bool> UpdatePixelCoordinatesAsync(byte cityId, int? pixelX, int? pixelY)
    {
        if (_controller == null)
            throw new InvalidOperationException("CityService가 초기화되지 않았습니다.");

        var result = await _controller.UpdateCityPixelCoordinatesAsync(cityId, pixelX, pixelY);

        // 캐시 갱신
        if (result)
        {
            await RefreshCacheAsync();
        }

        return result;
    }

    /// <summary>
    /// City 모델로 도시 정보 업데이트
    /// </summary>
    public async Task<bool> UpdateCityAsync(City city)
    {
        return await UpdateCityInfoAsync(
            city.Id,
            city.Name,
            city.PixelX,
            city.PixelY,
            city.HasLibrary,
            city.Latitude,
            city.Longitude,
            city.CulturalSphere,
            city.HasGuild);
    }

    /// <summary>
    /// 도시 정보 업데이트 (이름 + 픽셀 좌표 + 도서관 여부 + 위도/경도 + 문화권 + 조합)
    /// </summary>
    public async Task<bool> UpdateCityInfoAsync(byte cityId, string name, int? pixelX, int? pixelY, bool hasLibrary, int? latitude = null, int? longitude = null, string? culturalSphere = null, bool hasGuild = false)
    {
        if (_controller == null)
            throw new InvalidOperationException("CityService가 초기화되지 않았습니다.");

        var result = await _controller.UpdateCityInfoAsync(cityId, name, pixelX, pixelY, hasLibrary, latitude, longitude, culturalSphere, hasGuild);

        // 캐시 갱신
        if (result)
        {
            await RefreshCacheAsync();
        }

        return result;
    }

    /// <summary>
    /// DB의 도시 정보를 JSON 파일로 저장
    /// </summary>
    public async Task ExportToJsonAsync(string jsonPath)
    {
        if (_controller == null)
            throw new InvalidOperationException("CityService가 초기화되지 않았습니다.");

        var entities = await _controller.GetAllCitiesAsync();
        var jsonData = entities.Select(e => new
        {
            id = (int)e.Id,
            name = e.Name,
            latitude = e.Latitude,
            longitude = e.Longitude,
            hasLibrary = e.HasLibrary,
            pixelX = e.PixelX,
            pixelY = e.PixelY,
            hasShipyard = e.HasShipyard,
            hasGuild = e.HasGuild,
            culturalSphere = e.CulturalSphere
        }).ToList();

        var options = new System.Text.Json.JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };

        var json = System.Text.Json.JsonSerializer.Serialize(jsonData, options);
        await File.WriteAllTextAsync(jsonPath, json, System.Text.Encoding.UTF8);
    }

    /// <summary>
    /// Entity -> Model 변환
    /// </summary>
    private static City ToModel(CityEntity entity)
    {
        return new City
        {
            Id = entity.Id,
            Name = entity.Name,
            Latitude = entity.Latitude,
            Longitude = entity.Longitude,
            HasLibrary = entity.HasLibrary,
            HasShipyard = entity.HasShipyard,
            HasGuild = entity.HasGuild,
            CulturalSphere = entity.CulturalSphere,
            PixelX = entity.PixelX,
            PixelY = entity.PixelY
        };
    }
}
