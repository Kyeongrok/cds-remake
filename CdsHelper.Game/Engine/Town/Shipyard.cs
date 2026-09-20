using CdsHelper.Game.Engine.Models;
using CdsHelper.Support.Local.Models;

namespace CdsHelper.Game.Engine.Town;

/// <summary>
/// 조선소에서 치르는 값 — 매각 · 수리 · 개조.
/// </summary>
/// <remarks>
/// 값은 다 <b>선체값에 견준 비율</b>이다. 게임 선체값은 만~이십오만 닢인데 우리
/// <see cref="Hull.Price"/> 는 조선소 화면에서 옮긴 100~500 짜리 사다리라 자릿수가
/// 다르다 — 그래서 액수가 아니라 나누는 수만 게임 것을 쓴다.
///
/// 묻고 알리는 것은 화면(<see cref="UI.Views.CityPicView"/>)이 맡는다. 여기 있는 것은
/// 얼마인지와 무엇을 잃는지뿐이다.
/// </remarks>
public static class Shipyard
{
    /// <summary>
    /// 이 마을 조선소가 그 선체를 손댈 수 있는지(<c>0x004969F9</c>).
    /// </summary>
    /// <remarks>
    /// 도시 문화권(<c>0x004A1820</c> — 도시 레코드 <c>+0x58</c>)이 <b>0·1·2·10</b>(유럽과
    /// 신대륙)이면 <b>다우선만</b> 못 고치고, 그 밖(아프리카·이슬람·인도·중국·중앙아시아·
    /// 동남아·일본)이면 <b>다우선만</b> 고친다. 못 고치면
    /// 「이 배 형은 내가 어떻게 할 수 없다.」(<c>0x00532338</c>)다.
    ///
    /// 우리 선체 다섯에 다우선이 없으니 유럽권 밖에서는 개조 자체가 안 된다.
    /// <c>0x00532310</c> 「이교도의 배는 내가 어떻게 할 수 없다.」는 <b>어디서도 안 쓰이는
    /// 죽은 글</b>이다 — 두 갈래로 나누려다 한쪽만 남은 것으로 보인다.
    /// </remarks>
    public static bool CanRefitHere(int hullId, int culture) =>
        culture is 0 or 1 or 2 or 10 ? hullId != Hull.Dhow : hullId == Hull.Dhow;

    /// <summary>배를 팔 때 받는 값 — 선체 매각값에 도시 시세를 먹인다(<c>0x0044C0A0</c>).</summary>
    public static int SellPrice(Ship ship, int cityRate) =>
        Math.Max(1, ship.Hull.SellPrice * cityRate / 100);

    /// <summary>손상 한 점을 고치는 값의 밑수. 게임은 여기에 <c>rand(4)</c> 를 더한다.</summary>
    public const int RepairRate = 26;

    /// <summary>
    /// 고른 배를 고치는 값 — 손상 <b>합</b>으로 <b>굴림 한 번</b>이다.
    /// </summary>
    /// <remarks>
    /// <code>
    ///   0x0044BBF0  손상 = (최대내구 - 지금내구) + (최대돛 - 지금돛)   ; 음수는 0
    ///   0x0044BA83  고른 줄마다 그 손상을 더한다
    ///   0x0044BAA1  값 = (rand(4) + 26) * 손상합                      ; 26~29 곱, 한 번만
    ///   0x0044BABD  값 = 값 x 도시 시세 / 100                          ; 적어도 1
    /// </code>
    /// 우리 선체 표에는 돛 값이 없어 <b>내구만</b> 센다.
    /// </remarks>
    public static int RepairCostOf(int need, int cityRate, Random random) =>
        Math.Max(1, (RepairRate + random.Next(4)) * need * cityRate / 100);

    /// <summary>
    /// 이 마을에서 고칠 수 있는 배 — 함대 먼저, 그 뒤가 이 마을이 맡은 배다.
    /// </summary>
    /// <remarks>
    /// 게임의 <c>0x0044BC50(도시, 0)</c> 이다. 이 목록이 비면 조선소 차림표의 <b>"수리" 줄이
    /// 꺼진다</b>(<c>0x0044BD40</c> 이 <c>0x0044BC50 &gt; 0</c> 을 본다) — 그래서 평소에는
    /// "수리가 필요한 배는 없네!" 를 볼 일이 없다.
    /// </remarks>
    public static List<(Ship Ship, bool Docked)> RepairTargets(Player player, int cityId)
    {
        var hurt = new List<(Ship Ship, bool Docked)>();
        // 함대 배는 함대가 이 도시에 있을 때만 든다(0x0044BC6B → 0x0040E1C0(도시, 0)).
        if (player.FleetHere(cityId))
            foreach (var ship in player.Ships) if (ship.NeedsRepair) hurt.Add((ship, false));
        foreach (var ship in player.DockedAt(cityId)) if (ship.NeedsRepair) hurt.Add((ship, true));
        return hurt;
    }

    /// <summary>개조 값을 나누는 수(<c>0x004955F9</c> 의 <c>mov $0xf,%ecx ; idiv</c>).</summary>
    public const int RefitDivisor = 15;

    /// <summary>마스트 값을 나누는 수(<c>0x00494C32</c> 의 <c>mov $5,%ecx</c>).</summary>
    public const int MastDivisor = 5;

    /// <summary>돛 값을 나누는 수(<c>mov $0x14,%ecx</c>) — 돛종류 변경도 같다.</summary>
    public const int SailDivisor = 20;

    /// <summary>
    /// 개조 값의 밑 — 그 도시에서 이 선체를 살 값이다(<c>0x0044B450</c>: 선체값 x 시세 / 100, 최소 1).
    /// 개조마다 이것을 나눈다 — 예전에는 시세를 안 먹여 비싼 도시·싼 도시가 같았다.
    /// </summary>
    private static int Base(Ship ship, int rate) => Math.Max(1, ship.Hull.Price * rate / 100);

    /// <summary>개조 한 번 값 — 그 도시 선체값의 열다섯 분의 일.</summary>
    public static int RefitCost(Ship ship, int rate) => Base(ship, rate) / RefitDivisor;

    /// <summary>마스트 하나를 세우는 값 — 그 도시 선체값의 다섯 분의 일.</summary>
    public static int MastCost(Ship ship, int rate) => Base(ship, rate) / MastDivisor;

    /// <summary>돛 하나를 달거나 갈아 다는 값 — 그 도시 선체값의 스무 분의 일.</summary>
    public static int SailCost(Ship ship, int rate) => Base(ship, rate) / SailDivisor;

    /// <summary>
    /// 그 줄이 무엇을 얻고 무엇을 잃는지 알려 주는 물음. 게임 문구 그대로다
    /// (<c>0x00531938</c> 벌).
    /// </summary>
    public static string RefitWarning(string item) => item switch
    {
        Facility.RefitTonnage =>
            "적재용량과 함께 중량도 조금 올라가지만, 스피드와 내구력이 조금 떨어지네. 괜찮겠나?",
        // 보강만 말투가 다르다(0x00531B70) — 앞의 둘과 달리 「그래도 괜찮은가?」다.
        Facility.RefitReinforce =>
            "적재중량과 스피드가 조금 떨어지는데, 그래도 괜찮은가?",
        _ => "용량과 함께 적재용량도 조금 올라가지만, 스피드와 내구력이 조금 떨어지네. 괜찮겠나?",
    };
}
