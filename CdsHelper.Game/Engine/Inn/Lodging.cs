using CdsHelper.Game.Engine.Market;
using CdsHelper.Game.Local.Helpers;
using CdsHelper.Support.Local.Models;

namespace CdsHelper.Game.Engine.Inn;

/// <summary>묵으려 했을 때 벌어진 일.</summary>
public enum StayResult
{
    /// <summary>묵었다.</summary>
    Ok,

    /// <summary>값을 못 치른다 — "소지금이 모자랍니다".</summary>
    NotEnoughGold,
}

/// <summary>
/// 여관 숙박 규칙 — 얼마인지, 묵으면 무엇이 달라지는지.
/// </summary>
/// <remarks>
/// 값은 <b>한 달 선불</b>이고 도시의 <b>문화권</b>으로 갈린다. 게임 EXE 의 표
/// <c>0x005692B8</c>(4바이트 x 11)에서 그대로 읽었다.
/// <code>
///    0 이베리아  150      1 북유럽    120      2 지중해     124
///    3 아프리카   90      4 이슬람     15      5 인도        60
///    6 중국       90      7 중앙아시아  30      8 동남아시아   30
///    9 일본       60     10 아메리카   30
/// </code>
/// 값을 내는 자리는 <c>0x0047FA11</c> 이다 — 문화권으로 이 표를 타고, 시세를 먹인 뒤
/// 최소 1닢으로 잘라 낸다. 여기서도 시장과 같은 <see cref="MarketRates"/> 를 거치게 두었다.
/// 지금은 시세가 다 100 이라 표 값 그대로다.
///
/// 차례는 숙박 본체 <c>0x0047FC0E</c> 그대로다.
/// <code>
///   1  "선불이네. 우리 집은 한 달에 금화 %d닢인데, 머물고 갈텐가?"   YES/NO
///   2  YES 면 소지금과 값을 견준다(0x0047FC6F)  — 모자라면 "소지금이 모자랍니다"
///   3  값을 치르고 한 달이 간다
///   4  셋 중 하나를 무작위로 낸다 — 0x0047FCD4 의 rand(3)
/// </code>
/// <b>돈 검사가 YES 뒤</b>인 것은 시장과 같다.
///
/// <b>허드렛일</b>(<c>0x0047FD60</c>)은 딴 줄이다 — 숙박에서 돈이 모자라도 그쪽으로 끌려가지 않는다.
/// 값을 못 치르면 「소지금이 모자랍니다」로 끝난다.
///
/// 묵어도 허드렛일을 해도 그 고장 <b>말을 한 조각 배울 수 있다</b>(<c>0x0047FAE0</c>, <see cref="LearnTongue"/>).
/// </remarks>
public sealed class Lodging
{
    /// <summary>
    /// 문화권별 한 달 값. 색인이 문화권 번호(<see cref="CityExeTable.CultureOf"/>)다.
    /// </summary>
    private static readonly int[] MonthlyByCulture =
        [150, 120, 124, 90, 15, 60, 90, 30, 30, 60, 30];

    /// <summary>문화권을 모를 때 부르는 값. 표의 가운데쯤이다.</summary>
    private const int Unknown = 90;

    /// <summary>아무리 싸도 이만큼은 받는다(<c>0x0047FA2E</c> 의 <c>cmp eax,1</c>).</summary>
    private const int Least = 1;

    /// <summary>한 번 묵으면 가는 달 수.</summary>
    public const int Months = 1;

    /// <summary>한 번 묵으면 흐르는 날(<c>0x1E</c>).</summary>
    public const int StayDays = 30;

    /// <summary>
    /// 소지금이 이 값 이하(<c>0x0047FE70</c> 의 <c>cmp 소지금, 100 / jle</c>)일 때만 허드렛일 줄이 나온다.
    /// </summary>
    public const int OddJobMaxGold = 100;

    // ── 허드렛일 — 0x0047FD60 ──────────────────────────────────────────────────

    /// <summary>여관 주인이 묻는 말(<c>0x00544468</c>).</summary>
    public const string OddJobAsk =
        "뭐야, 자네 돈이 없나? 어쩔 수 없군~. 그렇다면 일년간 충분히 일을 부리겠네! 괜찮겠나?";

    /// <summary>일을 마치고 주인이 하는 말(<c>0x005444C0</c>).</summary>
    public const string OddJobDone =
        "수고했네. 이것이 약속한 돈이네. 자네도 바다의 사나이라면 이런 창피한 일을 하는 것이 아니네, 알았나?";

    /// <summary>허드렛일로 가는 날 수 — 꼬박 한 해다(<c>0x004A2AD0(0x16D, 1)</c>).</summary>
    public const int OddJobDays = 365;

    /// <summary>품삯의 밑값(<c>0x0047FD8D</c> 의 <c>0x4B0</c>) — 시세를 먹인다.</summary>
    private const int OddJobBase = 1200;

    /// <summary>그 도시에서 한 해 일하고 받는 삯. 시세가 100 이면 1200 닢이다.</summary>
    public int OddJobPay(int cityId) => Math.Max(Least, _rates.BuyPrice(OddJobBase, cityId));

    /// <summary>일하고 나면 차는 컨디션(<c>0x0047FDB9</c> 의 <c>rand(10)+10</c>).</summary>
    public static int OddJobRest(Random random) => random.Next(10) + 10;

    /// <summary>
    /// 그 고장 말을 한 조각 배운다(<c>0x0047FAE0</c>) — 숙박과 허드렛일이 함께 쓴다.
    /// </summary>
    /// <remarks>
    /// 이미 아는 말이면 아무 일도 없다. 셋에 한 번 걸리고, 걸리면 <c>rand(100) &lt; 지력 + 1</c> 로 갈린다.
    /// 배우면 그 말이 <b>1</b> 이 된다(올리는 것이 아니라 박는 것이다).
    /// </remarks>
    /// <returns>배웠으면 말 이름, 아니면 null.</returns>
    public string? LearnTongue(Player player, int cityId, NationTable? nations, Random random)
    {
        int nation = _cities?.NationOf(cityId) ?? -1;
        int language = nations?.Find(nation)?.Language ?? -1;
        if (language < 0 || language >= Skill.Languages.Length) return null;

        string name = Skill.Languages[language];
        if (player.TongueOf(name) > 0) return null;
        if (random.Next(3) != 0) return null;
        if (random.Next(100) >= player.AbilityOf(Ability.Mind) + 1) return null;

        player.SetTongue(name, 1);
        return name;
    }

    /// <summary>묵고 일어났을 때 하는 말. 셋 중 하나가 무작위로 나온다.</summary>
    public static readonly string[] WakeWords =
        ["피로가 풀렸다!", "체력이 회복됐다!", "기분이 매우 좋다!"];

    private readonly CityExeTable? _cities;
    private readonly MarketRates _rates;

    public Lodging(CityExeTable? cities, MarketRates rates)
    {
        _cities = cities;
        _rates = rates;
    }

    /// <summary>그 도시 여관의 한 달 값.</summary>
    public int PriceAt(int cityId)
    {
        int culture = _cities?.CultureOf(cityId) ?? -1;
        int listed = culture >= 0 && culture < MonthlyByCulture.Length
            ? MonthlyByCulture[culture]
            : Unknown;
        return Math.Max(Least, _rates.BuyPrice(listed, cityId));
    }

    /// <summary>
    /// 묵는다. 값을 치르고 한 달을 넘긴다. 돈이 모자라면 아무것도 하지 않는다.
    /// </summary>
    public StayResult Stay(Player player, int cityId)
    {
        int price = PriceAt(cityId);
        if (!player.CanAfford(price)) return StayResult.NotEnoughGold;

        player.Pay(price);
        // 달력 한 달이 아니라 <b>서른 날</b>이다(0x0047FC9C 의 0x004A2AD0(0x1E, 1)).
        player.AdvanceDays(StayDays);
        return StayResult.Ok;
    }

    /// <summary>
    /// 일어났을 때 할 말 하나. 게임은 <c>rand(3)</c> 으로 고른다.
    /// </summary>
    public static string WakeWord(Random random) => WakeWords[random.Next(WakeWords.Length)];
}
