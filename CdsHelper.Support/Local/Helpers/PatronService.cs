using System.IO;
using System.Text.Json;
using CdsHelper.Support.Local.Models;

namespace CdsHelper.Support.Local.Helpers;

public class PatronService
{
    public List<Patron> LoadPatrons(string filePath)
    {
        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException($"patrons.json 파일을 찾을 수 없습니다: {filePath}");
        }

        var json = File.ReadAllText(filePath);
        var patrons = JsonSerializer.Deserialize<List<Patron>>(json);

        return patrons ?? new List<Patron>();
    }

    /// <summary>
    /// 그 도시에 지금 있는 후원자들. 아직 안 나타났거나 이미 은퇴한 사람은 뺀다.
    /// </summary>
    public List<Patron> ActiveInCity(IEnumerable<Patron> patrons, string city, int year) =>
        patrons
            .Where(p => p.City.Equals(city, StringComparison.OrdinalIgnoreCase))
            .Where(p => p.IsActive(year))
            .ToList();

    /// <summary>
    /// 그 도시의 그 건물에 앉아 있는 후원자. 없으면 null.
    /// </summary>
    /// <remarks>
    /// 게임에서 설득이 뜨는 조건이 이것이다 — 그 건물에 후원자가 물려 있어야 한다.
    /// 어느 건물에 앉는지는 직업으로 정한다(<see cref="Patron.Seats"/>).
    /// 앞자리를 고집하지 않고, 그 도시에 실제로 있는 건물 가운데 가장 앞선 자리를 쓴다 —
    /// 그래야 총독부가 없는 도시의 총독도 앉을 데가 생긴다.
    /// </remarks>
    /// <param name="kindsInCity">그 도시에 있는 건물 종류들.</param>
    public Patron? SeatedAt(IEnumerable<Patron> patrons, string city, int year,
                            string buildingKind, IReadOnlyCollection<string> kindsInCity)
    {
        foreach (var patron in ActiveInCity(patrons, city, year))
        {
            foreach (var seat in patron.Seats)
            {
                if (!kindsInCity.Contains(seat)) continue;   // 이 도시에 없는 자리는 건너뛴다
                if (seat == buildingKind) return patron;
                break;                                       // 앉을 자리를 찾았는데 여기가 아니다
            }
        }
        return null;
    }

    public List<Patron> Filter(
        IEnumerable<Patron> patrons,
        string? nameSearch = null,
        string? citySearch = null,
        string? nationality = null,
        bool activeOnly = false,
        int currentYear = 1480)
    {
        var filtered = patrons.AsEnumerable();

        // 후원자명 검색
        if (!string.IsNullOrWhiteSpace(nameSearch))
        {
            filtered = filtered.Where(p => p.Name.Contains(nameSearch, StringComparison.OrdinalIgnoreCase));
        }

        // 도시 검색
        if (!string.IsNullOrWhiteSpace(citySearch))
        {
            filtered = filtered.Where(p => p.City.Contains(citySearch, StringComparison.OrdinalIgnoreCase));
        }

        // 국적 필터
        if (!string.IsNullOrWhiteSpace(nationality))
        {
            filtered = filtered.Where(p => p.Nationality.Equals(nationality, StringComparison.OrdinalIgnoreCase));
        }

        // 활동중인 후원자만
        if (activeOnly)
        {
            filtered = filtered.Where(p => p.IsActive(currentYear));
        }

        return filtered.ToList();
    }

    public List<PatronDisplay> ToDisplayList(IEnumerable<Patron> patrons, int currentYear, int playerFame = 0)
    {
        return patrons.Select(p => new PatronDisplay
        {
            Id = p.Id,
            Name = p.Name,
            Nationality = p.Nationality,
            City = p.City,
            Occupation = p.Occupation,
            SupportRate = p.SupportRate,
            Discernment = p.Discernment,
            AppearYear = p.AppearYear,
            RetireYear = p.RetireYear,
            StatusDisplay = p.StatusDisplay(currentYear),
            Fame = p.Fame,
            IsFameMet = playerFame > 0 && p.Fame <= playerFame,
            Wealth = p.Wealth,
            Power = p.Power,
            Preferences = p.PreferencesDisplay(),
            Note = p.Note
        }).ToList();
    }

    public List<string> GetDistinctNationalities(IEnumerable<Patron> patrons)
    {
        return patrons
            .Select(p => p.Nationality)
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Distinct()
            .OrderBy(n => n)
            .ToList();
    }
}
