using CdsHelper.Api.Data;
using CdsHelper.Api.Entities;
using CdsHelper.Api.Repositories;
using Microsoft.EntityFrameworkCore;

namespace CdsHelper.Api.Controllers;

public class BookController
{
    private readonly IBookRepository _repository;

    public BookController(IBookRepository repository)
    {
        _repository = repository;
    }

    public static BookController Create(string dbPath)
    {
        var context = AppDbContextFactory.Create(dbPath);
        context.Database.EnsureCreated();
        EnsureBooksTablesExist(context);
        var repository = new BookRepository(context);
        return new BookController(repository);
    }

    private static void EnsureBooksTablesExist(AppDbContext context)
    {
        context.Database.ExecuteSqlRaw(@"
            CREATE TABLE IF NOT EXISTS Books (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Name TEXT NOT NULL,
                Language TEXT,
                Hint TEXT,
                Required TEXT,
                Condition TEXT
            )");

        context.Database.ExecuteSqlRaw(@"
            CREATE TABLE IF NOT EXISTS BookCities (
                BookId INTEGER NOT NULL,
                CityId INTEGER NOT NULL,
                PRIMARY KEY (BookId, CityId),
                FOREIGN KEY (BookId) REFERENCES Books(Id),
                FOREIGN KEY (CityId) REFERENCES Cities(Id)
            )");

        context.Database.ExecuteSqlRaw(@"
            CREATE TABLE IF NOT EXISTS BookHints (
                BookId INTEGER NOT NULL,
                HintId INTEGER NOT NULL,
                PRIMARY KEY (BookId, HintId),
                FOREIGN KEY (BookId) REFERENCES Books(Id),
                FOREIGN KEY (HintId) REFERENCES Hints(Id)
            )");
    }

    public async Task<List<BookEntity>> GetAllBooksAsync()
    {
        return await _repository.GetAllAsync();
    }

    public async Task<BookEntity?> GetBookByIdAsync(int id)
    {
        return await _repository.GetByIdAsync(id);
    }

    public async Task<List<BookEntity>> GetBooksByCityIdAsync(byte cityId)
    {
        return await _repository.GetByCityIdAsync(cityId);
    }

    public async Task<List<BookEntity>> GetBooksByFilterAsync(
        string? nameSearch = null,
        string? language = null,
        string? requiredSkill = null)
    {
        return await _repository.GetByFilterAsync(nameSearch, language, requiredSkill);
    }

    public async Task<List<string>> GetLanguagesAsync()
    {
        return await _repository.GetDistinctLanguagesAsync();
    }

    public async Task<List<string>> GetRequiredSkillsAsync()
    {
        return await _repository.GetDistinctRequiredSkillsAsync();
    }

    public async Task<BookEntity> AddBookAsync(BookEntity book)
    {
        return await _repository.AddAsync(book);
    }

    public async Task AddBooksAsync(IEnumerable<BookEntity> books)
    {
        await _repository.AddRangeAsync(books);
    }

    public async Task AddBookCityAsync(int bookId, byte cityId)
    {
        await _repository.AddBookCityAsync(bookId, cityId);
    }

    public async Task AddBookCitiesAsync(IEnumerable<BookCityEntity> bookCities)
    {
        await _repository.AddBookCitiesAsync(bookCities);
    }

    public async Task AddBookHintsAsync(IEnumerable<BookHintEntity> bookHints)
    {
        await _repository.AddBookHintsAsync(bookHints);
    }

    public async Task<bool> HasAnyDataAsync()
    {
        return await _repository.HasAnyDataAsync();
    }

    public async Task UpdateBookCitiesAsync(int bookId, List<byte> cityIds)
    {
        await _repository.UpdateBookCitiesAsync(bookId, cityIds);
    }

    public async Task DeleteBookAsync(int bookId)
    {
        await _repository.DeleteBookAsync(bookId);
    }
}
