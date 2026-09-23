namespace CdsHelper.Game.Local.Helpers;

/// <summary>
/// 대본 풀이에 붙일 이름 — 번호 뒤에 괄호로 단다(「1(북유럽)」 · 「2(라코루냐)」).
/// </summary>
/// <remarks>
/// 풀이는 바이트만 보고 짓는 정적 셈이라 표를 여기서 한 번씩만 연다. 표가 없거나 번호가 표 밖이면
/// 번호만 낸다 — 풀이는 사람이 읽으라고 붙이는 것이라 이름이 빠져도 대본은 그대로 돈다.
/// </remarks>
internal static class DisevNames
{
    private static readonly Lazy<CityTable> Cities = new(CityTable.Open);
    private static readonly Lazy<GoodsTable?> Goods = new(() => GoodsTable.Open(""));
    private static readonly Lazy<NationTable?> Nations = new(() => NationTable.Open(""));

    /// <summary>도시 — 「2(라코루냐)」.</summary>
    public static string City(int id) =>
        With(id, Cities.Value.Find(id) is { Name.Length: > 0 } c ? c.Name : null);

    /// <summary>건물 코드 — 「4(술집)」. 0~11 만 이름이 있다.</summary>
    public static string Building(int code) => With(code, Engine.Models.Facility.CodeName(code));

    /// <summary>문화권 — 「1(북유럽)」.</summary>
    public static string Culture(int id) =>
        With(id, id >= 0 && id < CityCultureEdits.Names.Length ? CityCultureEdits.Names[id] : null);

    /// <summary>나라 — 「0(포르투갈)」.</summary>
    public static string Nation(int id) => With(id, Nations.Value?.Find(id)?.Name);

    /// <summary>교역품 — 「39(견직물)」.</summary>
    public static string Good(int id) => With(id, Goods.Value?.Find(id)?.Name);

    private static string With(int id, string? name) =>
        string.IsNullOrEmpty(name) ? id.ToString() : $"{id}({name})";
}
