using System.IO;
using System.Text.Json;
using CdsHelper.Support.Local.Models;

namespace CdsHelper.Support.Local.Helpers;

public class ItemService
{
    private List<Item> _allItems = new();

    public List<Item> LoadItems(string jsonPath)
    {
        if (!File.Exists(jsonPath))
            return new List<Item>();

        var json = File.ReadAllText(jsonPath);
        _allItems = JsonSerializer.Deserialize<List<Item>>(json) ?? new List<Item>();
        return _allItems;
    }

    public List<Item> Filter(string? nameSearch, string? selectedCategory, string? discoverySearch)
    {
        var result = _allItems.AsEnumerable();

        if (!string.IsNullOrWhiteSpace(nameSearch))
            result = result.Where(i => i.Name.Contains(nameSearch, StringComparison.OrdinalIgnoreCase) ||
                                       i.Hint.Contains(nameSearch, StringComparison.OrdinalIgnoreCase));

        if (!string.IsNullOrWhiteSpace(selectedCategory) && selectedCategory != "전체")
            result = result.Where(i => i.Category == selectedCategory);

        if (!string.IsNullOrWhiteSpace(discoverySearch))
            result = result.Where(i => i.RelatedDiscovery.Contains(discoverySearch, StringComparison.OrdinalIgnoreCase));

        return result.ToList();
    }

    public List<string> GetDistinctCategories()
    {
        var categories = _allItems.Select(i => i.Category).Distinct().OrderBy(c => c).ToList();
        categories.Insert(0, "전체");
        return categories;
    }
}
