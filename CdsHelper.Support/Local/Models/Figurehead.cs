using System.Text.Json.Serialization;

namespace CdsHelper.Support.Local.Models;

public class Figurehead
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("function")]
    public string Function { get; set; } = string.Empty;

    [JsonPropertyName("level")]
    public int Level { get; set; }

    [JsonPropertyName("note")]
    public string Note { get; set; } = string.Empty;
}
