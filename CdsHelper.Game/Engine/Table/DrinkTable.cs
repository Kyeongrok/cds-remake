using System.Text.Json.Serialization;

namespace CdsHelper.Game.Local.Helpers;

/// <summary>
/// CDS_95.EXE 안의 <b>술 표</b> — 술집이 그 고장에서 파는 술과 값.
/// </summary>
/// <remarks>
/// <code>
///   표 VA 0x004FF978, 55행 x 20바이트 (.rdata)
///   +0x00  이름 ptr("와인")
///   +0x04  값(닢)
///   +0x08  지역 무리(0~26) — 도시 표 +0x1C 와 같은 번호다
///   +0x0C  도수(와인 10 · 맥주 5 · 브랜디 40 · 위스키 45 · 럼주 50 · 보드카 60)
///   +0x10  별칭 ptr("붉은 술") — 그 고장 말을 모르면 이 이름으로 나온다
/// </code>
/// 술집 명령 창은 <b>제 지역 무리에 든 술</b>을 표 차례대로 앞에 붙인다. 이베리아(0)는
/// 와인 4닢 · 브랜디 9닢 · 럼주 7닢 셋이고, 세비야 갈무리와 딱 맞는다.
///
/// 이름을 진짜 이름으로 낼지 별칭으로 낼지는 <c>0x0042FB20</c> 이 가른다 — 그 도시 말을
/// <b>둘 이상</b> 알아야 진짜 이름이 나온다. 값 셈은 <c>0x0042F5CC</c> 로, 시세를 먹이고
/// 적어도 1닢이다.
///
/// 마시는 자리는 <c>0x0042EFE0</c> 다. 도수를 취기에 더하고, 취기가
/// <c>(주량 + 1) x 50</c> 을 넘으면 취한다. 안 취했으면 다섯 마디 가운데 하나가 뜬다 —
/// <b>피로도는 건드리지 않는다</b>. "피로가 풀렸다!" 는 그 다섯 중 하나일 뿐이다.
/// </remarks>
public sealed class DrinkTable
{
    /// <summary>적어 둘 파일 이름(<c>%APPDATA%\CdsHelper\exe-tables\술표.json</c>).</summary>
    private const string CacheName = "술표";

    /// <summary>알맹이 모양 판.</summary>
    private const int Version = 1;

    private const int TableVa = 0x004FF978;
    private const int RowSize = 0x14;

    /// <summary>표 줄 수.</summary>
    public const int Count = 55;

    /// <summary>술 한 가지.</summary>
    /// <param name="Name">진짜 이름("와인").</param>
    /// <param name="Price">값(닢). 시세를 먹이기 전 값이다.</param>
    /// <param name="Region">파는 지역 무리(도시 표 <c>+0x1C</c>).</param>
    /// <param name="Proof">도수. 마시면 이만큼 취기가 오른다.</param>
    /// <param name="Alias">그 고장 말을 모를 때 부르는 이름("붉은 술").</param>
    [method: JsonConstructor]
    public readonly record struct Drink(string Name, int Price, int Region, int Proof, string Alias);

    /// <summary>JSON 으로 적어 두는 알맹이.</summary>
    internal sealed record Snapshot(List<Drink> Drinks);

    private readonly List<Drink> _drinks;

    private DrinkTable(Snapshot snapshot) => _drinks = snapshot.Drinks;

    /// <summary>왜 못 읽었는지. 잘 열렸으면 빈 문자열.</summary>
    public static string LastError { get; private set; } = "";

    /// <summary>표에 있는 술 전부.</summary>
    public IReadOnlyList<Drink> Drinks => _drinks;

    /// <summary>그 지역 무리에서 파는 술. 표 차례 그대로다.</summary>
    public List<Drink> InRegion(int region)
    {
        var got = new List<Drink>();
        if (region < 0) return got;
        foreach (var drink in _drinks)
            if (drink.Region == region) got.Add(drink);
        return got;
    }

    /// <summary>
    /// 술집 줄에 적을 이름. 그 도시 말을 <b>둘 이상</b> 알아야 진짜 이름이 나온다
    /// (<c>0x0042FB44</c> 의 <c>cmp eax,1 / jle</c>).
    /// </summary>
    public static string NameFor(Drink drink, int tongue) => tongue > 1 ? drink.Name : drink.Alias;

    /// <summary>표를 연다. 못 읽으면 null.</summary>
    public static DrinkTable? Open(string gameDirectory)
    {
        var snapshot = ExeTable.Open<Snapshot>(CacheName, gameDirectory, ReadFromExe, out string error,
                                               Version);
        LastError = error;
        return snapshot == null ? null : new DrinkTable(snapshot);
    }

    private static Snapshot? ReadFromExe(PeImage exe, out string error)
    {
        error = "";

        var drinks = new List<Drink>(Count);
        for (int k = 0; k < Count; k++)
        {
            int row = TableVa + k * RowSize;
            var name = exe.Text(exe.Word(row + 0x00));
            var alias = exe.Text(exe.Word(row + 0x10));
            if (name == null || alias == null) break;

            drinks.Add(new Drink(name, exe.Int(row + 0x04), exe.Int(row + 0x08),
                                 exe.Int(row + 0x0C), alias));
        }

        // 판이 다른 EXE 를 잘못 읽지 않도록 첫 줄을 확인한다 — 이베리아의 와인 4닢이다.
        if (drinks.Count != Count || drinks[0] is not { Name: "와인", Price: 4, Region: 0 })
        {
            error = "술 표가 기대한 모양이 아닙니다(다른 판의 EXE 일 수 있습니다)";
            return null;
        }

        return new Snapshot(drinks);
    }
}
