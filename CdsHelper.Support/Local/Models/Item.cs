using System.Text.Json.Serialization;

namespace CdsHelper.Support.Local.Models;

public class Item
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("category")]
    public string Category { get; set; } = string.Empty;

    [JsonPropertyName("effect")]
    public int Effect { get; set; }

    [JsonPropertyName("sellPrice")]
    public int SellPrice { get; set; }

    [JsonPropertyName("buyPrice")]
    public int BuyPrice { get; set; }

    [JsonPropertyName("hint")]
    public string Hint { get; set; } = string.Empty;

    [JsonPropertyName("relatedDiscovery")]
    public string RelatedDiscovery { get; set; } = string.Empty;
}
