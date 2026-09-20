using CdsHelper.Api.Entities;

namespace CdsHelper.Api.Repositories;

public interface ICityRepository
{
    Task<List<CityEntity>> GetAllAsync();
    Task<CityEntity?> GetByIdAsync(byte id);
    Task<List<CityEntity>> GetByFilterAsync(
        string? nameSearch = null,
        string? culturalSphere = null,
        bool? hasLibrary = null,
        bool? hasShipyard = null);
    Task<CityEntity> AddAsync(CityEntity city);
    Task<CityEntity> UpdateAsync(CityEntity city);
    Task DeleteAsync(byte id);
    Task<List<string>> GetDistinctCulturalSpheresAsync();
    Task<bool> ExistsAsync(byte id);
    Task<bool> AnyAsync();
    Task AddRangeAsync(IEnumerable<CityEntity> cities);
}
