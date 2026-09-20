using CdsHelper.Support.Local.Models;

namespace CdsHelper.Support.Local.Helpers;

public class CharacterService
{
    public static readonly Dictionary<int, string> SkillNames = new()
    {
        { 1, "항해술" },
        { 2, "운용술" },
        { 3, "검술" },
        { 4, "포술" },
        { 5, "사격술" },
        { 6, "의학" },
        { 7, "웅변술" },
        { 8, "측량술" },
        { 9, "역사학" },
        { 10, "회계" },
        { 11, "조선술" },
        { 12, "신학" },
        { 13, "과학" },
        { 14, "스페인어" },
        { 15, "포르투갈어" },
        { 16, "로망스어" },
        { 17, "게르만어" },
        { 18, "슬라브어" },
        { 19, "아랍어" },
        { 20, "페르시아어" },
        { 21, "중국어" },
        { 22, "힌두어" },
        { 23, "위그르어" },
        { 24, "아프리카 토착어" },
        { 25, "아메리카 토착어" },
        { 26, "동남아시아 토착어" },
        { 27, "동아시아 토착어" },
    };

    public List<CharacterData> Filter(
        IEnumerable<CharacterData> characters,
        bool showGray = true,
        string? nameSearch = null,
        int? skillIndex = null,
        byte? skillLevel = null)
    {
        var filtered = characters.AsEnumerable();

        // 회색(미등장/은퇴) 필터
        if (!showGray)
        {
            filtered = filtered.Where(c => !c.IsGray);
        }

        // 이름 검색 필터
        if (!string.IsNullOrWhiteSpace(nameSearch))
        {
            filtered = filtered.Where(c => c.Name.Contains(nameSearch, StringComparison.OrdinalIgnoreCase));
        }

        // 특기 필터
        if (skillIndex.HasValue && skillLevel.HasValue)
        {
            filtered = filtered.Where(c => c.HasSkill(skillIndex.Value, skillLevel.Value));
        }

        return filtered.ToList();
    }

    public string GetSkillName(int skillIndex)
    {
        return SkillNames.TryGetValue(skillIndex, out var name) ? name : $"스킬{skillIndex}";
    }
}
