using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using CdsHelper.Api.Data;
using CdsHelper.Api.Entities;
using Microsoft.EntityFrameworkCore;

namespace CdsHelper.Support.Local.Helpers;

public class DiscoveryService
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never
    };

    private const string MetaKeyBundledHash = "BundledJsonHash";

    private AppDbContext? _dbContext;
    private Dictionary<int, DiscoveryEntity> _discoveries = new();
    private Dictionary<string, int> _nameToIdMap = new(); // 발견물 이름 -> ID 매핑
    private List<DiscoveryJsonRecord> _jsonRecords = new();
    private string? _userJsonPath;
    private bool _initialized;

    /// <summary>
    /// 발견물 데이터 초기화.
    /// 사용자 JSON(<paramref name="userJsonPath"/>)이 평소엔 source of truth. 번들 JSON 해시가 바뀌면(앱 업데이트로 새 좌표가 들어온 경우)
    /// 사용자 JSON을 번들로 덮어쓰고 Discoveries 테이블을 비운 뒤 재마이그레이션한다.
    /// </summary>
    public async Task InitializeAsync(string dbPath, string bundledJsonPath, string userJsonPath)
    {
        if (_initialized) return;

        _dbContext = AppDbContextFactory.Create(dbPath);
        _dbContext.Database.EnsureCreated();
        EnsureTablesExist();

        _userJsonPath = userJsonPath;

        // 번들 JSON 해시 변경 감지: 변경되었으면 사용자 JSON과 DB를 강제 갱신
        var bundledHash = File.Exists(bundledJsonPath) ? ComputeFileHash(bundledJsonPath) : null;
        var storedHash = await GetMetaAsync(MetaKeyBundledHash);
        var bundleChanged = bundledHash != null && bundledHash != storedHash;

        // 사용자 JSON이 없거나 번들이 갱신되었으면 번들에서 복사
        if (File.Exists(bundledJsonPath) && (!File.Exists(userJsonPath) || bundleChanged))
        {
            var dir = Path.GetDirectoryName(userJsonPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);
            File.Copy(bundledJsonPath, userJsonPath, overwrite: true);
        }

        // 사용자 JSON 로드 → DB 마이그레이션 (번들이 갱신되었으면 강제 재이식)
        if (File.Exists(userJsonPath))
        {
            await LoadJsonAsync(userJsonPath);
            await MigrateFromJsonAsync(force: bundleChanged);
        }

        if (bundledHash != null && bundleChanged)
            await SetMetaAsync(MetaKeyBundledHash, bundledHash);

        // 캐시 로드
        await RefreshCacheAsync();
        _initialized = true;
    }

    private void EnsureTablesExist()
    {
        _dbContext?.Database.ExecuteSqlRaw(@"
            CREATE TABLE IF NOT EXISTS Discoveries (
                Id INTEGER PRIMARY KEY,
                Name TEXT NOT NULL,
                HintId INTEGER,
                AppearCondition TEXT,
                BookName TEXT,
                LatFrom INTEGER,
                LatTo INTEGER,
                LonFrom INTEGER,
                LonTo INTEGER,
                FOREIGN KEY (HintId) REFERENCES Hints(Id) ON DELETE SET NULL
            )");

        // 기존 테이블에 좌표 컬럼이 없으면 추가
        try { _dbContext?.Database.ExecuteSqlRaw("ALTER TABLE Discoveries ADD COLUMN LatFrom INTEGER"); } catch { }
        try { _dbContext?.Database.ExecuteSqlRaw("ALTER TABLE Discoveries ADD COLUMN LatTo INTEGER"); } catch { }
        try { _dbContext?.Database.ExecuteSqlRaw("ALTER TABLE Discoveries ADD COLUMN LonFrom INTEGER"); } catch { }
        try { _dbContext?.Database.ExecuteSqlRaw("ALTER TABLE Discoveries ADD COLUMN LonTo INTEGER"); } catch { }

        _dbContext?.Database.ExecuteSqlRaw(@"
            CREATE TABLE IF NOT EXISTS DiscoveryParents (
                DiscoveryId INTEGER NOT NULL,
                ParentDiscoveryId INTEGER NOT NULL,
                PRIMARY KEY (DiscoveryId, ParentDiscoveryId),
                FOREIGN KEY (DiscoveryId) REFERENCES Discoveries(Id) ON DELETE CASCADE,
                FOREIGN KEY (ParentDiscoveryId) REFERENCES Discoveries(Id) ON DELETE RESTRICT
            )");

        _dbContext?.Database.ExecuteSqlRaw(@"
            CREATE TABLE IF NOT EXISTS DiscoveryMeta (
                Key TEXT PRIMARY KEY,
                Value TEXT NOT NULL
            )");
    }

    private async Task LoadJsonAsync(string jsonPath)
    {
        await using var stream = File.OpenRead(jsonPath);
        var records = await JsonSerializer.DeserializeAsync<List<DiscoveryJsonRecord>>(stream, JsonOpts);
        _jsonRecords = records ?? new List<DiscoveryJsonRecord>();
    }

    private async Task MigrateFromJsonAsync(bool force = false)
    {
        if (_dbContext == null || _jsonRecords.Count == 0) return;

        var connection = _dbContext.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open)
            await connection.OpenAsync();

        if (force)
        {
            // 번들 갱신: 기존 행을 비우고 재이식.
            // DiscoveryParents.ParentDiscoveryId가 ON DELETE RESTRICT라서
            // Discoveries를 먼저 지우면 부모로 참조되는 행에서 FK가 막힌다.
            // 따라서 자식 매핑 테이블을 먼저 비운 뒤 Discoveries를 삭제한다.
            using (var clearChildCmd = connection.CreateCommand())
            {
                clearChildCmd.CommandText = "DELETE FROM DiscoveryParents";
                await clearChildCmd.ExecuteNonQueryAsync();
            }
            using var deleteCmd = connection.CreateCommand();
            deleteCmd.CommandText = "DELETE FROM Discoveries";
            await deleteCmd.ExecuteNonQueryAsync();
        }
        else
        {
            // 기존 데이터 확인
            using var countCmd = connection.CreateCommand();
            countCmd.CommandText = "SELECT COUNT(*) FROM Discoveries";
            var count = Convert.ToInt32(await countCmd.ExecuteScalarAsync());
            if (count > 0) return; // 이미 데이터가 있으면 스킵
        }

        // 힌트 이름 -> ID 매핑 로드
        var hintNameToId = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var hints = await _dbContext.Hints.AsNoTracking().ToListAsync();
        foreach (var hint in hints)
        {
            if (!hintNameToId.ContainsKey(hint.Name))
                hintNameToId[hint.Name] = hint.Id;
        }

        // 이름 -> ID 매핑 생성 (선행 발견물 매핑용)
        var nameToId = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var d in _jsonRecords)
        {
            if (string.IsNullOrEmpty(d.Name)) continue;
            if (!nameToId.ContainsKey(d.Name))
                nameToId[d.Name] = d.Id;
        }

        // 발견물 INSERT
        foreach (var d in _jsonRecords)
        {
            if (string.IsNullOrEmpty(d.Name)) continue;

            // 힌트 이름으로 힌트 ID 찾기
            int? hintId = null;
            if (!string.IsNullOrEmpty(d.Hint))
            {
                hintId = FindHintId(d.Hint, hintNameToId);
            }

            using var insertCmd = connection.CreateCommand();
            insertCmd.CommandText = @"
                INSERT OR REPLACE INTO Discoveries (Id, Name, HintId, AppearCondition, BookName, LatFrom, LatTo, LonFrom, LonTo)
                VALUES (@id, @name, @hintId, @appearCondition, @bookName, @latFrom, @latTo, @lonFrom, @lonTo)";

            AddParameter(insertCmd, "@id", d.Id);
            AddParameter(insertCmd, "@name", d.Name);
            AddParameter(insertCmd, "@hintId", hintId);
            AddParameter(insertCmd, "@appearCondition", d.AppearCondition);
            AddParameter(insertCmd, "@bookName", d.BookName);
            AddParameter(insertCmd, "@latFrom", d.LatFrom);
            AddParameter(insertCmd, "@latTo", d.LatTo);
            AddParameter(insertCmd, "@lonFrom", d.LonFrom);
            AddParameter(insertCmd, "@lonTo", d.LonTo);

            await insertCmd.ExecuteNonQueryAsync();
        }

        // 선행 발견물 매핑 INSERT
        foreach (var d in _jsonRecords)
        {
            if (string.IsNullOrEmpty(d.Condition)) continue;

            var parentNames = ParseCondition(d.Condition);
            foreach (var parentName in parentNames)
            {
                var parentId = FindDiscoveryId(parentName, nameToId);
                if (parentId == null) continue;

                using var insertCmd = connection.CreateCommand();
                insertCmd.CommandText = @"
                    INSERT OR IGNORE INTO DiscoveryParents (DiscoveryId, ParentDiscoveryId)
                    VALUES (@discoveryId, @parentId)";

                AddParameter(insertCmd, "@discoveryId", d.Id);
                AddParameter(insertCmd, "@parentId", parentId.Value);

                await insertCmd.ExecuteNonQueryAsync();
            }
        }

        var label = force ? "재이식" : "마이그레이션";
        EventQueueService.Instance.DataLoaded("DiscoveryService", $"발견물 {_jsonRecords.Count}개 {label} 완료");
    }

    private static string ComputeFileHash(string path)
    {
        using var sha = SHA256.Create();
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(sha.ComputeHash(stream));
    }

    private async Task<string?> GetMetaAsync(string key)
    {
        if (_dbContext == null) return null;

        var connection = _dbContext.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open)
            await connection.OpenAsync();

        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT Value FROM DiscoveryMeta WHERE Key = @key";
        AddParameter(cmd, "@key", key);
        return await cmd.ExecuteScalarAsync() as string;
    }

    private async Task SetMetaAsync(string key, string value)
    {
        if (_dbContext == null) return;

        var connection = _dbContext.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open)
            await connection.OpenAsync();

        using var cmd = connection.CreateCommand();
        cmd.CommandText = "INSERT OR REPLACE INTO DiscoveryMeta (Key, Value) VALUES (@key, @value)";
        AddParameter(cmd, "@key", key);
        AddParameter(cmd, "@value", value);
        await cmd.ExecuteNonQueryAsync();
    }

    /// <summary>
    /// 힌트 이름으로 힌트 ID 찾기 (유사 매칭 지원)
    /// </summary>
    private int? FindHintId(string hintName, Dictionary<string, int> hintNameToId)
    {
        // 정확한 매칭
        if (hintNameToId.TryGetValue(hintName, out var id))
            return id;

        // 공백 제거 후 매칭
        var normalized = hintName.Replace(" ", "");
        foreach (var kvp in hintNameToId)
        {
            if (kvp.Key.Replace(" ", "").Equals(normalized, StringComparison.OrdinalIgnoreCase))
                return kvp.Value;
        }

        // 부분 매칭
        foreach (var kvp in hintNameToId)
        {
            if (kvp.Key.Contains(hintName, StringComparison.OrdinalIgnoreCase) ||
                hintName.Contains(kvp.Key, StringComparison.OrdinalIgnoreCase))
                return kvp.Value;
        }

        return null;
    }

    /// <summary>
    /// 게재조건 파싱 (쉼표로 구분된 선행 발견물 목록)
    /// </summary>
    private List<string> ParseCondition(string condition)
    {
        var result = new List<string>();

        // "희망봉,말라카해협,마젤란해협 발견" 같은 형식 파싱
        // " 발견" 제거
        var cleaned = Regex.Replace(condition, @"\s*발견\s*$", "", RegexOptions.IgnoreCase);

        // 쉼표로 분리
        var parts = cleaned.Split(',', StringSplitOptions.RemoveEmptyEntries);
        foreach (var part in parts)
        {
            var trimmed = part.Trim();
            // "발견" 제거 (각 항목에서도)
            trimmed = Regex.Replace(trimmed, @"\s*발견\s*$", "", RegexOptions.IgnoreCase);
            if (!string.IsNullOrEmpty(trimmed))
                result.Add(trimmed);
        }

        return result;
    }

    /// <summary>
    /// 발견물 이름으로 ID 찾기 (유사 매칭 지원)
    /// </summary>
    private int? FindDiscoveryId(string name, Dictionary<string, int> nameToId)
    {
        // 정확한 매칭
        if (nameToId.TryGetValue(name, out var id))
            return id;

        // 공백 제거 후 매칭
        var normalized = name.Replace(" ", "");
        foreach (var kvp in nameToId)
        {
            if (kvp.Key.Replace(" ", "").Equals(normalized, StringComparison.OrdinalIgnoreCase))
                return kvp.Value;
        }

        // 부분 매칭 (name이 키에 포함되거나, 키가 name에 포함)
        foreach (var kvp in nameToId)
        {
            if (kvp.Key.Contains(name, StringComparison.OrdinalIgnoreCase) ||
                name.Contains(kvp.Key, StringComparison.OrdinalIgnoreCase))
                return kvp.Value;
        }

        return null;
    }

    private void AddParameter(System.Data.Common.DbCommand cmd, string name, object? value)
    {
        var param = cmd.CreateParameter();
        param.ParameterName = name;
        param.Value = value ?? DBNull.Value;
        cmd.Parameters.Add(param);
    }

    private async Task RefreshCacheAsync()
    {
        if (_dbContext == null) return;

        var discoveries = await _dbContext.Discoveries
            .Include(d => d.Hint)
            .AsNoTracking()
            .ToListAsync();
        _discoveries = discoveries.ToDictionary(d => d.Id);
        _nameToIdMap = discoveries.ToDictionary(d => d.Name, d => d.Id, StringComparer.OrdinalIgnoreCase);
    }

    public DiscoveryEntity? GetDiscovery(int id)
    {
        return _discoveries.TryGetValue(id, out var discovery) ? discovery : null;
    }

    public int? GetDiscoveryIdByName(string name)
    {
        return _nameToIdMap.TryGetValue(name, out var id) ? id : null;
    }

    public Dictionary<int, DiscoveryEntity> GetAllDiscoveries()
    {
        return new Dictionary<int, DiscoveryEntity>(_discoveries);
    }

    /// <summary>
    /// 특정 발견물의 선행 발견물 ID 목록 조회
    /// </summary>
    public async Task<List<int>> GetParentDiscoveryIdsAsync(int discoveryId)
    {
        if (_dbContext == null) return new List<int>();

        return await _dbContext.DiscoveryParents
            .Where(dp => dp.DiscoveryId == discoveryId)
            .Select(dp => dp.ParentDiscoveryId)
            .ToListAsync();
    }

    /// <summary>
    /// 모든 발견물의 선행 발견물 매핑 조회
    /// </summary>
    public async Task<Dictionary<int, List<int>>> GetAllParentMappingsAsync()
    {
        if (_dbContext == null) return new Dictionary<int, List<int>>();

        var mappings = await _dbContext.DiscoveryParents
            .AsNoTracking()
            .ToListAsync();

        return mappings
            .GroupBy(dp => dp.DiscoveryId)
            .ToDictionary(g => g.Key, g => g.Select(dp => dp.ParentDiscoveryId).ToList());
    }

    public async Task UpdateCoordinateAsync(int id, int? latFrom, int? latTo, int? lonFrom, int? lonTo)
    {
        if (_dbContext == null) return;

        var entity = await _dbContext.Discoveries.FindAsync(id);
        if (entity == null) return;

        entity.LatFrom = latFrom;
        entity.LatTo = latTo;
        entity.LonFrom = lonFrom;
        entity.LonTo = lonTo;

        await _dbContext.SaveChangesAsync();

        // 캐시 갱신
        if (_discoveries.ContainsKey(id))
        {
            _discoveries[id].LatFrom = latFrom;
            _discoveries[id].LatTo = latTo;
            _discoveries[id].LonFrom = lonFrom;
            _discoveries[id].LonTo = lonTo;
        }

        // JSON 백업 갱신 (앱 업데이트로 install 폴더가 갈려도 보존)
        var rec = _jsonRecords.FirstOrDefault(r => r.Id == id);
        if (rec != null)
        {
            rec.LatFrom = latFrom;
            rec.LatTo = latTo;
            rec.LonFrom = lonFrom;
            rec.LonTo = lonTo;
            await SaveJsonAsync();
        }
    }

    public async Task UpdateNameAsync(int id, string newName)
    {
        if (_dbContext == null) return;

        var entity = await _dbContext.Discoveries.FindAsync(id);
        if (entity == null) return;

        var oldName = entity.Name;
        entity.Name = newName;
        await _dbContext.SaveChangesAsync();

        // 캐시 갱신
        if (_discoveries.ContainsKey(id))
            _discoveries[id].Name = newName;

        // 이름→ID 맵 갱신
        _nameToIdMap.Remove(oldName);
        _nameToIdMap[newName] = id;

        // JSON 백업 갱신
        var rec = _jsonRecords.FirstOrDefault(r => r.Id == id);
        if (rec != null)
        {
            rec.Name = newName;
            await SaveJsonAsync();
        }
    }

    private async Task SaveJsonAsync()
    {
        if (string.IsNullOrEmpty(_userJsonPath)) return;
        await WriteJsonToAsync(_userJsonPath);
    }

    /// <summary>
    /// 현재 발견물 마스터를 임의 경로에 발견물.json 포맷으로 내보낸다.
    /// </summary>
    public async Task ExportJsonAsync(string targetPath)
    {
        await WriteJsonToAsync(targetPath);
    }

    /// <summary>현재 내보내기 대상 레코드 수.</summary>
    public int RecordCount => _jsonRecords.Count;

    private async Task WriteJsonToAsync(string targetPath)
    {
        var dir = Path.GetDirectoryName(targetPath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);

        await using var stream = File.Create(targetPath);
        await JsonSerializer.SerializeAsync(stream, _jsonRecords, JsonOpts);
    }

    private sealed class DiscoveryJsonRecord
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string? Hint { get; set; }
        public string? Condition { get; set; }
        public string? AppearCondition { get; set; }
        public string? BookName { get; set; }
        public int? LatFrom { get; set; }
        public int? LatTo { get; set; }
        public int? LonFrom { get; set; }
        public int? LonTo { get; set; }
    }
}
