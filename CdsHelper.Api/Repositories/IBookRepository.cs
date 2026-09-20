using CdsHelper.Api.Entities;

namespace CdsHelper.Api.Repositories;

public interface IBookRepository
{
    Task<List<BookEntity>> GetAllAsync();
    Task<BookEntity?> GetByIdAsync(int id);
    Task<List<BookEntity>> GetByCityIdAsync(byte cityId);
    Task<List<BookEntity>> GetByFilterAsync(
        string? nameSearch = null,
        string? language = null,
        string? requiredSkill = null);
    Task<BookEntity> AddAsync(BookEntity book);
    Task AddRangeAsync(IEnumerable<BookEntity> books);
    Task AddBookCityAsync(int bookId, byte cityId);
    Task AddBookCitiesAsync(IEnumerable<BookCityEntity> bookCities);
    Task AddBookHintsAsync(IEnumerable<BookHintEntity> bookHints);
    Task UpdateBookCitiesAsync(int bookId, List<byte> cityIds);
    Task DeleteBookAsync(int bookId);
    Task<bool> HasAnyDataAsync();
    Task<List<string>> GetDistinctLanguagesAsync();
    Task<List<string>> GetDistinctRequiredSkillsAsync();
}
