using CdsHelper.Api.Data;
using CdsHelper.Api.Entities;
using CdsHelper.Api.Repositories;

namespace CdsHelper.Api.Controllers;

public class CityController
{
    private readonly ICityRepository _repository;

    public CityController(ICityRepository repository)
    {
        _repository = repository;
    }

    /// <summary>
    /// DB 경로로 Controller 인스턴스 생성
    /// </summary>
    public static CityController Create(string dbPath)
    {
        var context = AppDbContextFactory.Create(dbPath);
        context.Database.EnsureCreated();
        var repository = new CityRepository(context);
        return new CityController(repository);
    }

    /// <summary>
    /// 모든 도시 조회
    /// </summary>
    public async Task<List<CityEntity>> GetAllCitiesAsync()
    {
        return await _repository.GetAllAsync();
    }

    /// <summary>
    /// ID로 도시 조회
    /// </summary>
    public async Task<CityEntity?> GetCityByIdAsync(byte id)
    {
        return await _repository.GetByIdAsync(id);
    }

    /// <summary>
    /// 필터로 도시 조회
    /// </summary>
    public async Task<List<CityEntity>> GetCitiesByFilterAsync(
        string? nameSearch = null,
        string? culturalSphere = null,
        bool? hasLibrary = null,
        bool? hasShipyard = null)
    {
        return await _repository.GetByFilterAsync(nameSearch, culturalSphere, hasLibrary, hasShipyard);
    }

    /// <summary>
    /// 문화권 목록 조회
    /// </summary>
    public async Task<List<string>> GetCulturalSpheresAsync()
    {
        return await _repository.GetDistinctCulturalSpheresAsync();
    }

    /// <summary>
    /// 도시 추가
    /// </summary>
    public async Task<CityEntity> AddCityAsync(CityEntity city)
    {
        return await _repository.AddAsync(city);
    }

    /// <summary>
    /// 도시 수정
    /// </summary>
    public async Task<CityEntity> UpdateCityAsync(CityEntity city)
    {
        return await _repository.UpdateAsync(city);
    }

    /// <summary>
    /// 도시 삭제
    /// </summary>
    public async Task DeleteCityAsync(byte id)
    {
        await _repository.DeleteAsync(id);
    }

    /// <summary>
    /// 도시명 조회 (ID로)
    /// </summary>
    public async Task<string> GetCityNameAsync(byte id)
    {
        if (id == 255)
            return "함대소속";

        var city = await _repository.GetByIdAsync(id);
        return city?.Name ?? $"미확인({id})";
    }

    /// <summary>
    /// 좌표가 있는 도시만 조회 (지도 마커용)
    /// </summary>
    public async Task<List<CityEntity>> GetCitiesWithCoordinatesAsync()
    {
        var cities = await _repository.GetAllAsync();
        return cities.Where(c => c.PixelX.HasValue && c.PixelY.HasValue && c.PixelX > 0 && c.PixelY > 0).ToList();
    }

    /// <summary>
    /// 여러 도시 일괄 추가 (초기 데이터 마이그레이션용)
    /// </summary>
    public async Task AddCitiesAsync(IEnumerable<CityEntity> cities)
    {
        await _repository.AddRangeAsync(cities);
    }

    /// <summary>
    /// DB에 도시 데이터가 하나라도 있는지 확인.
    /// 이전엔 Id=0만 확인해서 0이 지워진 경우 오감지 문제가 있었으므로 AnyAsync 사용.
    /// </summary>
    public async Task<bool> HasAnyDataAsync()
    {
        return await _repository.AnyAsync();
    }

    /// <summary>
    /// 도시 픽셀 좌표 업데이트
    /// </summary>
    public async Task<bool> UpdateCityPixelCoordinatesAsync(byte cityId, int? pixelX, int? pixelY)
    {
        var city = await _repository.GetByIdAsync(cityId);
        if (city == null)
            return false;

        city.PixelX = pixelX;
        city.PixelY = pixelY;
        await _repository.UpdateAsync(city);
        return true;
    }

    /// <summary>
    /// 도시 정보 업데이트 (이름 + 픽셀 좌표 + 도서관 여부 + 위도/경도 + 문화권 + 조합)
    /// </summary>
    public async Task<bool> UpdateCityInfoAsync(byte cityId, string name, int? pixelX, int? pixelY, bool hasLibrary, int? latitude = null, int? longitude = null, string? culturalSphere = null, bool hasGuild = false)
    {
        var city = await _repository.GetByIdAsync(cityId);
        if (city == null)
            return false;

        city.Name = name;
        city.PixelX = pixelX;
        city.PixelY = pixelY;
        city.HasLibrary = hasLibrary;
        city.Latitude = latitude;
        city.Longitude = longitude;
        city.CulturalSphere = culturalSphere;
        city.HasGuild = hasGuild;
        await _repository.UpdateAsync(city);
        return true;
    }

    /// <summary>
    /// 도시 조합 여부 업데이트
    /// </summary>
    public async Task<bool> UpdateCityGuildAsync(byte cityId, bool hasGuild)
    {
        var city = await _repository.GetByIdAsync(cityId);
        if (city == null)
            return false;

        city.HasGuild = hasGuild;
        await _repository.UpdateAsync(city);
        return true;
    }
}
