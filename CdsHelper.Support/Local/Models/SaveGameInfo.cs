namespace CdsHelper.Support.Local.Models;

public class SaveGameInfo
{
    public int Year { get; set; }
    public int Month { get; set; }
    public int Day { get; set; }
    public List<CharacterData> Characters { get; set; } = new();
    public List<HintData> Hints { get; set; } = new();
    public List<DiscoveryData> Discoveries { get; set; } = new();

    public string DateString => $"{Year}년 {Month}월 {Day}일";

    public int DiscoveredHintCount => Hints.Count(h => h.IsDiscovered);
    public int TotalHintCount => Hints.Count;
}
