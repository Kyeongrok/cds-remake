using System.IO;
using System.Text.Json;
using CdsHelper.Support.Local.Models;

namespace CdsHelper.Support.Local.Helpers;

public class FigureheadService
{
    private List<Figurehead> _allFigureheads = new();

    public List<Figurehead> LoadFigureheads(string jsonPath)
    {
        if (!File.Exists(jsonPath))
            return new List<Figurehead>();

        var json = File.ReadAllText(jsonPath);
        _allFigureheads = JsonSerializer.Deserialize<List<Figurehead>>(json) ?? new List<Figurehead>();
        return _allFigureheads;
    }

    public List<Figurehead> Filter(string? nameSearch, string? selectedFunction, int? selectedLevel)
    {
        var result = _allFigureheads.AsEnumerable();

        if (!string.IsNullOrWhiteSpace(nameSearch))
            result = result.Where(f => f.Name.Contains(nameSearch, StringComparison.OrdinalIgnoreCase) ||
                                       f.Note.Contains(nameSearch, StringComparison.OrdinalIgnoreCase));

        if (!string.IsNullOrWhiteSpace(selectedFunction) && selectedFunction != "전체")
            result = result.Where(f => f.Function == selectedFunction);

        if (selectedLevel.HasValue && selectedLevel.Value >= 0)
            result = result.Where(f => f.Level == selectedLevel.Value);

        return result.ToList();
    }

    public List<string> GetDistinctFunctions()
    {
        var functions = _allFigureheads.Select(f => f.Function).Distinct().OrderBy(f => f).ToList();
        functions.Insert(0, "전체");
        return functions;
    }

    public List<string> GetDistinctLevels()
    {
        return new List<string> { "전체", "0", "1", "2", "3" };
    }
}
