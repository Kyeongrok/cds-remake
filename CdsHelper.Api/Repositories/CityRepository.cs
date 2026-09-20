using CdsHelper.Api.Data;
using CdsHelper.Api.Entities;
using Microsoft.EntityFrameworkCore;

namespace CdsHelper.Api.Repositories;

public class CityRepository : ICityRepository
{
    private readonly AppDbContext _context;

    public CityRepository(AppDbContext context)
    {
        _context = context;
    }

    public async Task<List<CityEntity>> GetAllAsync()
    {
        return await _context.Cities.OrderBy(c => c.Id).ToListAsync();
    }

    public async Task<CityEntity?> GetByIdAsync(byte id)
    {
        return await _context.Cities.FindAsync(id);
    }

    public async Task<List<CityEntity>> GetByFilterAsync(
        string? nameSearch = null,
        string? culturalSphere = null,
        bool? hasLibrary = null,
        bool? hasShipyard = null)
    {
        var query = _context.Cities.AsQueryable();

        if (!string.IsNullOrWhiteSpace(nameSearch))
        {
            query = query.Where(c => c.Name.Contains(nameSearch));
        }

        if (!string.IsNullOrWhiteSpace(culturalSphere))
        {
            query = query.Where(c => c.CulturalSphere == culturalSphere);
        }

        if (hasLibrary.HasValue)
        {
            query = query.Where(c => c.HasLibrary == hasLibrary.Value);
        }

        if (hasShipyard.HasValue)
        {
            query = query.Where(c => c.HasShipyard == hasShipyard.Value);
        }

        return await query.OrderBy(c => c.Id).ToListAsync();
    }

    public async Task<CityEntity> AddAsync(CityEntity city)
    {
        _context.Cities.Add(city);
        await _context.SaveChangesAsync();
        return city;
    }

    public async Task<CityEntity> UpdateAsync(CityEntity city)
    {
        _context.Cities.Update(city);
        await _context.SaveChangesAsync();
        return city;
    }

    public async Task DeleteAsync(byte id)
    {
        var city = await _context.Cities.FindAsync(id);
        if (city != null)
        {
            _context.Cities.Remove(city);
            await _context.SaveChangesAsync();
        }
    }

    public async Task<List<string>> GetDistinctCulturalSpheresAsync()
    {
        return await _context.Cities
            .Where(c => c.CulturalSphere != null)
            .Select(c => c.CulturalSphere!)
            .Distinct()
            .OrderBy(cs => cs)
            .ToListAsync();
    }

    public async Task<bool> ExistsAsync(byte id)
    {
        return await _context.Cities.AnyAsync(c => c.Id == id);
    }

    public async Task<bool> AnyAsync()
    {
        return await _context.Cities.AnyAsync();
    }

    public async Task AddRangeAsync(IEnumerable<CityEntity> cities)
    {
        await _context.Cities.AddRangeAsync(cities);
        await _context.SaveChangesAsync();
    }
}
