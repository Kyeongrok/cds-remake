using CdsHelper.Api.Data;
using CdsHelper.Api.Entities;
using Microsoft.EntityFrameworkCore;

namespace CdsHelper.Api.Repositories;

public class BookRepository : IBookRepository
{
    private readonly AppDbContext _context;

    public BookRepository(AppDbContext context)
    {
        _context = context;
    }

    public async Task<List<BookEntity>> GetAllAsync()
    {
        return await _context.Books
            .Include(b => b.BookCities)
                .ThenInclude(bc => bc.City)
            .Include(b => b.BookHints)
                .ThenInclude(bh => bh.Hint)
            .OrderBy(b => b.Name)
            .ToListAsync();
    }

    public async Task<BookEntity?> GetByIdAsync(int id)
    {
        return await _context.Books
            .Include(b => b.BookCities)
                .ThenInclude(bc => bc.City)
            .FirstOrDefaultAsync(b => b.Id == id);
    }

    public async Task<List<BookEntity>> GetByCityIdAsync(byte cityId)
    {
        return await _context.BookCities
            .Where(bc => bc.CityId == cityId)
            .Include(bc => bc.Book)
            .Select(bc => bc.Book)
            .OrderBy(b => b.Name)
            .ToListAsync();
    }

    public async Task<List<BookEntity>> GetByFilterAsync(
        string? nameSearch = null,
        string? language = null,
        string? requiredSkill = null)
    {
        var query = _context.Books
            .Include(b => b.BookCities)
                .ThenInclude(bc => bc.City)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(nameSearch))
        {
            query = query.Where(b => b.Name.Contains(nameSearch));
        }

        if (!string.IsNullOrWhiteSpace(language))
        {
            query = query.Where(b => b.Language == language);
        }

        if (!string.IsNullOrWhiteSpace(requiredSkill))
        {
            query = query.Where(b => b.Required == requiredSkill);
        }

        return await query.OrderBy(b => b.Name).ToListAsync();
    }

    public async Task<BookEntity> AddAsync(BookEntity book)
    {
        _context.Books.Add(book);
        await _context.SaveChangesAsync();
        return book;
    }

    public async Task AddRangeAsync(IEnumerable<BookEntity> books)
    {
        await _context.Books.AddRangeAsync(books);
        await _context.SaveChangesAsync();
    }

    public async Task AddBookCityAsync(int bookId, byte cityId)
    {
        var bookCity = new BookCityEntity { BookId = bookId, CityId = cityId };
        _context.BookCities.Add(bookCity);
        await _context.SaveChangesAsync();
    }

    public async Task AddBookCitiesAsync(IEnumerable<BookCityEntity> bookCities)
    {
        await _context.BookCities.AddRangeAsync(bookCities);
        await _context.SaveChangesAsync();
    }

    public async Task AddBookHintsAsync(IEnumerable<BookHintEntity> bookHints)
    {
        await _context.BookHints.AddRangeAsync(bookHints);
        await _context.SaveChangesAsync();
    }

    public async Task UpdateBookCitiesAsync(int bookId, List<byte> cityIds)
    {
        // Raw SQL로 기존 매핑 삭제
        await _context.Database.ExecuteSqlRawAsync(
            "DELETE FROM BookCities WHERE BookId = {0}", bookId);

        // Raw SQL로 새 매핑 추가
        foreach (var cityId in cityIds)
        {
            await _context.Database.ExecuteSqlRawAsync(
                "INSERT INTO BookCities (BookId, CityId) VALUES ({0}, {1})",
                bookId, cityId);
        }

        // Raw SQL 실행 후 Change Tracker 초기화 (캐시된 엔티티가 반환되지 않도록)
        _context.ChangeTracker.Clear();
    }

    public async Task DeleteBookAsync(int bookId)
    {
        await _context.Database.ExecuteSqlRawAsync(
            "DELETE FROM BookHints WHERE BookId = {0}", bookId);
        await _context.Database.ExecuteSqlRawAsync(
            "DELETE FROM BookCities WHERE BookId = {0}", bookId);
        await _context.Database.ExecuteSqlRawAsync(
            "DELETE FROM Books WHERE Id = {0}", bookId);

        _context.ChangeTracker.Clear();
    }

    public async Task<bool> HasAnyDataAsync()
    {
        return await _context.Books.AnyAsync();
    }

    public async Task<List<string>> GetDistinctLanguagesAsync()
    {
        return await _context.Books
            .Where(b => !string.IsNullOrEmpty(b.Language))
            .Select(b => b.Language)
            .Distinct()
            .OrderBy(l => l)
            .ToListAsync();
    }

    public async Task<List<string>> GetDistinctRequiredSkillsAsync()
    {
        return await _context.Books
            .Where(b => !string.IsNullOrEmpty(b.Required))
            .Select(b => b.Required)
            .Distinct()
            .OrderBy(r => r)
            .ToListAsync();
    }
}
