using System.Text.Json;
using CdsHelper.Api.Controllers;
using CdsHelper.Api.Entities;

namespace CdsHelper.Api.Migrations;

public static class DataMigrator
{
    /// <summary>
    /// cities.json 파일에서 SQLite DB로 데이터 마이그레이션
    /// </summary>
    public static async Task MigrateCitiesFromJsonAsync(
        CityController controller,
        string jsonPath,
        Action<string>? onSkipped = null,
        Action<string>? onMigrated = null)
    {
        if (!File.Exists(jsonPath))
        {
            throw new FileNotFoundException($"cities.json 파일을 찾을 수 없습니다: {jsonPath}");
        }

        // DB에 이미 데이터가 있으면 스킵
        if (await controller.HasAnyDataAsync())
        {
            onSkipped?.Invoke("Cities 데이터가 이미 DB에 존재하여 마이그레이션 스킵");
            return;
        }

        var json = await File.ReadAllTextAsync(jsonPath);
        var jsonCities = JsonSerializer.Deserialize<List<JsonCityData>>(json);

        if (jsonCities == null || jsonCities.Count == 0)
        {
            return;
        }

        var entities = jsonCities.Select(c => new CityEntity
        {
            Id = c.Id,
            Name = c.Name,
            Latitude = c.Latitude,
            Longitude = c.Longitude,
            HasLibrary = c.HasLibrary,
            HasShipyard = c.HasShipyard,
            CulturalSphere = c.CulturalSphere,
            PixelX = c.PixelX,
            PixelY = c.PixelY
        }).ToList();

        try
        {
            await controller.AddCitiesAsync(entities);
            onMigrated?.Invoke($"Cities {entities.Count}개 마이그레이션 완료");
        }
        catch (Exception ex)
        {
            var innerMsg = ex.InnerException?.Message ?? "없음";
            throw new Exception($"Cities 마이그레이션 실패.\n원인: {ex.Message}\nInner: {innerMsg}", ex);
        }
    }

    /// <summary>
    /// books.json 파일에서 SQLite DB로 데이터 마이그레이션
    /// </summary>
    public static async Task MigrateBooksFromJsonAsync(
        BookController bookController,
        CityController cityController,
        string jsonPath,
        Action<string>? onSkipped = null,
        Action<string>? onMigrated = null)
    {
        if (!File.Exists(jsonPath))
        {
            throw new FileNotFoundException($"books.json 파일을 찾을 수 없습니다: {jsonPath}");
        }

        // DB에 이미 데이터가 있으면 스킵
        if (await bookController.HasAnyDataAsync())
        {
            onSkipped?.Invoke("Books 데이터가 이미 DB에 존재하여 마이그레이션 스킵");
            return;
        }

        var json = await File.ReadAllTextAsync(jsonPath);
        var jsonBooks = JsonSerializer.Deserialize<List<JsonBookData>>(json);

        if (jsonBooks == null || jsonBooks.Count == 0)
        {
            return;
        }

        // 도시 목록 가져오기 (이름으로 ID 매핑용)
        var cities = await cityController.GetAllCitiesAsync();
        var cityNameToId = cities.ToDictionary(c => c.Name, c => c.Id);

        // Book 엔티티 생성 및 저장
        var bookEntities = new List<BookEntity>();
        var bookCityMappings = new List<(int BookIndex, List<byte> CityIds)>();

        var bookHintMappings = new List<(int BookIndex, List<int> HintIds)>();

        for (int i = 0; i < jsonBooks.Count; i++)
        {
            var jb = jsonBooks[i];
            var bookEntity = new BookEntity
            {
                Id = jb.Id,
                Name = jb.Name,
                Language = jb.Language ?? string.Empty,
                Hint = jb.HintName ?? string.Empty,
                Required = jb.Required ?? string.Empty,
                Condition = jb.Condition ?? string.Empty
            };
            bookEntities.Add(bookEntity);

            // 소재 도서관 파싱 (쉼표로 구분된 도시명)
            var cityIds = new List<byte>();
            if (!string.IsNullOrWhiteSpace(jb.Library))
            {
                var cityNames = jb.Library.Split(',', StringSplitOptions.RemoveEmptyEntries);
                foreach (var cityName in cityNames)
                {
                    var trimmedName = cityName.Trim();
                    if (cityNameToId.TryGetValue(trimmedName, out var cityId))
                    {
                        cityIds.Add(cityId);
                    }
                }
            }
            bookCityMappings.Add((i, cityIds));

            // hint 배열 저장
            bookHintMappings.Add((i, jb.HintIds ?? new List<int>()));
        }

        // Books 저장
        await bookController.AddBooksAsync(bookEntities);

        // BookCity 관계 저장
        var bookCityEntities = new List<BookCityEntity>();
        for (int i = 0; i < bookEntities.Count; i++)
        {
            var bookId = bookEntities[i].Id;
            var cityIds = bookCityMappings[i].CityIds;
            foreach (var cityId in cityIds)
            {
                bookCityEntities.Add(new BookCityEntity
                {
                    BookId = bookId,
                    CityId = cityId
                });
            }
        }

        if (bookCityEntities.Any())
        {
            await bookController.AddBookCitiesAsync(bookCityEntities);
        }

        // BookHint 관계 저장
        var bookHintEntities = new List<BookHintEntity>();
        for (int i = 0; i < bookEntities.Count; i++)
        {
            var bookId = bookEntities[i].Id;
            var hintIds = bookHintMappings[i].HintIds;
            foreach (var hintId in hintIds)
            {
                bookHintEntities.Add(new BookHintEntity
                {
                    BookId = bookId,
                    HintId = hintId
                });
            }
        }

        if (bookHintEntities.Any())
        {
            await bookController.AddBookHintsAsync(bookHintEntities);
        }

        onMigrated?.Invoke($"Books {bookEntities.Count}개, BookCities {bookCityEntities.Count}개, BookHints {bookHintEntities.Count}개 마이그레이션 완료");
    }

    private class JsonCityData
    {
        [System.Text.Json.Serialization.JsonPropertyName("id")]
        public byte Id { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [System.Text.Json.Serialization.JsonPropertyName("latitude")]
        public int? Latitude { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("longitude")]
        public int? Longitude { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("hasLibrary")]
        public bool HasLibrary { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("hasShipyard")]
        public bool HasShipyard { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("culturalSphere")]
        public string? CulturalSphere { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("pixelX")]
        public int? PixelX { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("pixelY")]
        public int? PixelY { get; set; }
    }

    private class JsonBookData
    {
        [System.Text.Json.Serialization.JsonPropertyName("id")]
        public int Id { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("도서명")]
        public string Name { get; set; } = string.Empty;

        [System.Text.Json.Serialization.JsonPropertyName("언어")]
        public string? Language { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("소재 도서관")]
        public string? Library { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("게제 힌트")]
        public string? HintName { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("hint")]
        public List<int>? HintIds { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("필요")]
        public string? Required { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("개제조건")]
        public string? Condition { get; set; }
    }
}
