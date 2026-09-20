using System.IO;
using System.Text.Json;
using CdsHelper.Api.Controllers;
using CdsHelper.Api.Entities;
using CdsHelper.Api.Migrations;
using CdsHelper.Support.Local.Models;

namespace CdsHelper.Support.Local.Helpers;

public class BookService
{
    private BookController? _controller;
    private CityController? _cityController;
    private List<Book> _cachedBooks = new();
    private bool _initialized;

    public async Task InitializeAsync(string dbPath, string? jsonPath = null)
    {
        if (_initialized) return;

        _controller = BookController.Create(dbPath);
        _cityController = CityController.Create(dbPath);

        // JSON 파일이 있으면 마이그레이션 시도
        if (!string.IsNullOrEmpty(jsonPath) && File.Exists(jsonPath))
        {
            await DataMigrator.MigrateBooksFromJsonAsync(
                _controller,
                _cityController,
                jsonPath,
                onSkipped: msg => EventQueueService.Instance.MigrationSkipped("BookService", msg),
                onMigrated: msg => EventQueueService.Instance.DataLoaded("BookService", msg));
        }

        // 캐시 로드
        await RefreshCacheAsync();
        _initialized = true;
    }

    private async Task RefreshCacheAsync()
    {
        if (_controller == null) return;

        var entities = await _controller.GetAllBooksAsync();
        _cachedBooks = entities.Select(ToModel).ToList();
    }

    /// <summary>
    /// 도시 ID로 해당 도서관의 책 목록 조회
    /// </summary>
    public async Task<List<Book>> GetBooksByCityIdAsync(byte cityId)
    {
        if (_controller == null)
            throw new InvalidOperationException("BookService가 초기화되지 않았습니다.");

        var entities = await _controller.GetBooksByCityIdAsync(cityId);
        return entities.Select(ToModel).ToList();
    }

    /// <summary>
    /// 모든 책 로드 (기존 호환성 - JSON)
    /// </summary>
    public List<Book> LoadBooks(string filePath)
    {
        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException($"books.json 파일을 찾을 수 없습니다: {filePath}");
        }

        var json = File.ReadAllText(filePath);
        var books = JsonSerializer.Deserialize<List<Book>>(json);

        return books ?? new List<Book>();
    }

    /// <summary>
    /// DB에서 모든 책 로드
    /// </summary>
    public async Task<List<Book>> LoadBooksFromDbAsync()
    {
        if (_controller == null)
            throw new InvalidOperationException("BookService가 초기화되지 않았습니다.");

        var entities = await _controller.GetAllBooksAsync();
        return entities.Select(ToModel).ToList();
    }

    /// <summary>
    /// 캐시된 책 목록 반환
    /// </summary>
    public List<Book> GetCachedBooks()
    {
        return _cachedBooks;
    }

    /// <summary>
    /// 필터링 (기존 호환성 - 메모리)
    /// </summary>
    public List<Book> Filter(
        IEnumerable<Book> books,
        string? nameSearch = null,
        string? librarySearch = null,
        string? hintSearch = null,
        string? language = null,
        string? requiredSkill = null)
    {
        var filtered = books.AsEnumerable();

        if (!string.IsNullOrWhiteSpace(nameSearch))
        {
            filtered = filtered.Where(b => b.Name.Contains(nameSearch, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(librarySearch))
        {
            filtered = filtered.Where(b => b.LibraryCityNames.Any(cn =>
                cn.Contains(librarySearch, StringComparison.OrdinalIgnoreCase)));
        }

        if (!string.IsNullOrWhiteSpace(hintSearch))
        {
            filtered = filtered.Where(b => b.HintNames.Any(h => h.Contains(hintSearch, StringComparison.OrdinalIgnoreCase))
                || b.HintText.Contains(hintSearch, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(language))
        {
            filtered = filtered.Where(b => b.Language.Equals(language, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(requiredSkill))
        {
            // 스킬 이름으로 시작하는 모든 항목 필터링 (예: "신학" 선택 시 "신학1", "신학2", "신학3" 모두 포함)
            filtered = filtered.Where(b => !string.IsNullOrWhiteSpace(b.Required) &&
                b.Required.StartsWith(requiredSkill, StringComparison.OrdinalIgnoreCase));
        }

        return filtered.ToList();
    }

    /// <summary>
    /// DB에서 필터링
    /// </summary>
    public async Task<List<Book>> FilterFromDbAsync(
        string? nameSearch = null,
        string? language = null,
        string? requiredSkill = null)
    {
        if (_controller == null)
            throw new InvalidOperationException("BookService가 초기화되지 않았습니다.");

        var entities = await _controller.GetBooksByFilterAsync(nameSearch, language, requiredSkill);
        return entities.Select(ToModel).ToList();
    }

    /// <summary>
    /// 언어 목록 (DB)
    /// </summary>
    public async Task<List<string>> GetDistinctLanguagesFromDbAsync()
    {
        if (_controller == null)
            throw new InvalidOperationException("BookService가 초기화되지 않았습니다.");

        return await _controller.GetLanguagesAsync();
    }

    /// <summary>
    /// 필요 스킬 목록 (DB)
    /// </summary>
    public async Task<List<string>> GetDistinctRequiredSkillsFromDbAsync()
    {
        if (_controller == null)
            throw new InvalidOperationException("BookService가 초기화되지 않았습니다.");

        return await _controller.GetRequiredSkillsAsync();
    }

    /// <summary>
    /// 언어 목록 (기존 호환성 - 메모리)
    /// </summary>
    public List<string> GetDistinctLanguages(IEnumerable<Book> books)
    {
        return books
            .Select(b => b.Language)
            .Where(l => !string.IsNullOrWhiteSpace(l))
            .Distinct()
            .OrderBy(l => l)
            .ToList();
    }

    /// <summary>
    /// 필요 스킬 목록 (기존 호환성 - 메모리)
    /// 숫자를 제외한 스킬 이름만 반환 (예: "신학1", "신학2" -> "신학")
    /// </summary>
    public List<string> GetDistinctRequiredSkills(IEnumerable<Book> books)
    {
        return books
            .Select(b => b.Required)
            .Where(r => !string.IsNullOrWhiteSpace(r))
            .Select(r => ExtractSkillName(r))
            .Distinct()
            .OrderBy(r => r)
            .ToList();
    }

    /// <summary>
    /// 스킬 문자열에서 숫자를 제거하여 스킬 이름만 추출
    /// 예: "신학2" -> "신학", "역사학3" -> "역사학"
    /// </summary>
    private static string ExtractSkillName(string skill)
    {
        if (string.IsNullOrWhiteSpace(skill))
            return skill;

        // 끝에서 숫자 제거
        return skill.TrimEnd('0', '1', '2', '3', '4', '5', '6', '7', '8', '9');
    }

    /// <summary>
    /// 도서-도시 매핑 업데이트
    /// </summary>
    public async Task UpdateBookCitiesAsync(int bookId, List<byte> cityIds)
    {
        if (_controller == null)
            throw new InvalidOperationException("BookService가 초기화되지 않았습니다.");

        await _controller.UpdateBookCitiesAsync(bookId, cityIds);

        // 캐시 갱신
        await RefreshCacheAsync();
    }

    /// <summary>
    /// 도서 삭제
    /// </summary>
    public async Task DeleteBookAsync(int bookId)
    {
        if (_controller == null)
            throw new InvalidOperationException("BookService가 초기화되지 않았습니다.");

        await _controller.DeleteBookAsync(bookId);
        await RefreshCacheAsync();
    }

    /// <summary>
    /// Entity -> Model 변환
    /// </summary>
    private static Book ToModel(BookEntity entity)
    {
        return new Book
        {
            Id = entity.Id,
            Name = entity.Name,
            Language = entity.Language,
            HintText = entity.Hint,
            Required = entity.Required,
            Condition = entity.Condition,
            LibraryCityIds = entity.BookCities?.Select(bc => bc.CityId).ToList() ?? new List<byte>(),
            LibraryCityNames = entity.BookCities?.Select(bc => bc.City?.Name ?? "").Where(n => !string.IsNullOrEmpty(n)).ToList() ?? new List<string>(),
            HintIds = entity.BookHints?.Select(bh => bh.HintId).ToList() ?? new List<int>(),
            HintNames = entity.BookHints?.Select(bh => bh.Hint?.Name ?? "").Where(n => !string.IsNullOrEmpty(n)).ToList() ?? new List<string>()
        };
    }
}
