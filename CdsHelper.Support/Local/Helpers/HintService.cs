using System.IO;
using System.Text.Json;
using CdsHelper.Api.Data;
using CdsHelper.Api.Entities;
using Microsoft.EntityFrameworkCore;

namespace CdsHelper.Support.Local.Helpers;

public class HintService
{
    private AppDbContext? _dbContext;
    private Dictionary<int, string> _hintNames = new();
    private bool _initialized;

    public async Task InitializeAsync(string dbPath, string? jsonPath = null)
    {
        if (_initialized) return;

        _dbContext = AppDbContextFactory.Create(dbPath);
        _dbContext.Database.EnsureCreated();
        EnsureHintsTableExists();

        // JSON 파일이 있으면 마이그레이션
        if (!string.IsNullOrEmpty(jsonPath) && File.Exists(jsonPath))
        {
            await MigrateFromJsonAsync(jsonPath);
        }

        // 캐시 로드
        await RefreshCacheAsync();
        _initialized = true;
    }

    private void EnsureHintsTableExists()
    {
        _dbContext?.Database.ExecuteSqlRaw(@"
            CREATE TABLE IF NOT EXISTS Hints (
                Id INTEGER PRIMARY KEY,
                Name TEXT NOT NULL
            )");
    }

    private async Task MigrateFromJsonAsync(string jsonPath)
    {
        if (_dbContext == null) return;

        var connection = _dbContext.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open)
            await connection.OpenAsync();

        // 기존 데이터 확인
        using (var countCmd = connection.CreateCommand())
        {
            countCmd.CommandText = "SELECT COUNT(*) FROM Hints";
            var count = Convert.ToInt32(await countCmd.ExecuteScalarAsync());
            if (count > 0) return; // 이미 데이터가 있으면 스킵
        }

        var json = await File.ReadAllTextAsync(jsonPath);
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        var hintData = JsonSerializer.Deserialize<List<HintJsonModel>>(json, options);

        if (hintData == null || hintData.Count == 0) return;

        // INSERT OR REPLACE 사용
        foreach (var hint in hintData)
        {
            using var insertCmd = connection.CreateCommand();
            insertCmd.CommandText = "INSERT OR REPLACE INTO Hints (Id, Name) VALUES (@id, @name)";

            var idParam = insertCmd.CreateParameter();
            idParam.ParameterName = "@id";
            idParam.Value = hint.Id;
            insertCmd.Parameters.Add(idParam);

            var nameParam = insertCmd.CreateParameter();
            nameParam.ParameterName = "@name";
            nameParam.Value = hint.Name;
            insertCmd.Parameters.Add(nameParam);

            await insertCmd.ExecuteNonQueryAsync();
        }

        EventQueueService.Instance.DataLoaded("HintService", $"힌트 {hintData.Count}개 마이그레이션 완료");
    }

    private class HintJsonModel
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
    }

    private async Task RefreshCacheAsync()
    {
        if (_dbContext == null) return;

        var hints = await _dbContext.Hints.AsNoTracking().ToListAsync();
        _hintNames = hints.ToDictionary(h => h.Id, h => h.Name);
    }

    public string GetHintName(int index)
    {
        return _hintNames.TryGetValue(index, out var name) ? name : $"힌트 {index + 1}";
    }

    public Dictionary<int, string> GetAllHintNames()
    {
        return new Dictionary<int, string>(_hintNames);
    }

    /// <summary>
    /// 특정 힌트를 수록한 도서 목록 조회
    /// </summary>
    public async Task<List<(string BookName, string Language, string Required, string Condition, List<string> Cities)>> GetBooksByHintIndexAsync(int hintIndex)
    {
        if (_dbContext == null) return new();

        var bookHints = await _dbContext.BookHints
            .Include(bh => bh.Book)
                .ThenInclude(b => b.BookCities)
                    .ThenInclude(bc => bc.City)
            .Where(bh => bh.HintId == hintIndex)
            .AsNoTracking()
            .ToListAsync();

        return bookHints.Select(bh => (
            bh.Book.Name,
            bh.Book.Language ?? "",
            bh.Book.Required ?? "",
            bh.Book.Condition ?? "",
            bh.Book.BookCities.Select(bc => bc.City.Name).ToList()
        )).ToList();
    }

    /// <summary>
    /// 힌트별 책 정보 (언어, 필요 스킬, 개제조건) 조회
    /// </summary>
    public async Task<Dictionary<int, (string Language, string Required, string Condition)>> GetHintBookInfoAsync()
    {
        var result = new Dictionary<int, (string Language, string Required, string Condition)>();
        if (_dbContext == null) return result;

        var bookHints = await _dbContext.BookHints
            .Include(bh => bh.Book)
            .AsNoTracking()
            .ToListAsync();

        // 힌트별로 그룹화
        var grouped = bookHints.GroupBy(bh => bh.HintId);
        foreach (var group in grouped)
        {
            var languages = group
                .Select(bh => bh.Book.Language)
                .Where(l => !string.IsNullOrEmpty(l))
                .Distinct()
                .ToList();

            var requireds = group
                .Select(bh => bh.Book.Required)
                .Where(r => !string.IsNullOrEmpty(r))
                .Distinct()
                .ToList();

            var conditions = group
                .Select(bh => bh.Book.Condition)
                .Where(c => !string.IsNullOrEmpty(c))
                .Distinct()
                .ToList();

            result[group.Key] = (
                string.Join(", ", languages),
                string.Join(", ", requireds),
                string.Join(", ", conditions)
            );
        }

        return result;
    }
}
