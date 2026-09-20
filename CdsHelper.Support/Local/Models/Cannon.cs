namespace CdsHelper.Support.Local.Models;

/// <summary>
/// 배에 싣는 대포 한 종류.
/// </summary>
/// <param name="Name">이름.</param>
/// <param name="Price">한 문 값(닢).</param>
/// <param name="Weight">한 문 무게. 적재중량을 먹는다.</param>
/// <param name="Word">조선소 영감이 붙이는 말. 골랐을 때 한 번 낸다.</param>
/// <remarks>
/// 게임 표는 <c>0x00549DB0</c> 이고 한 줄이 12바이트다 — 이름 포인터 · 단가 · 중량.
/// <code>
///   0x0056B848 세이커포   250  15      0x0056B868 페리에포  2100  25
///   0x0056B858 캘버린포  1600  20      0x0056B878 카논포    2500  30
/// </code>
/// 말은 <c>0x00532008</c> 벌이다.
/// </remarks>
public sealed record Cannon(string Name, int Price, int Weight, string Word)
{
    /// <summary>넷. 게임 표 차례 그대로다.</summary>
    public static readonly Cannon[] All =
    [
        new("세이커포", 250, 15, "이것은 적당한 값이지만, 위협정도의 위력밖에 없네."),
        new("캘버린포", 1600, 20, "이것은 멀리서 겨냥해 맞추는 데는 최고의 대포라네."),
        new("페리에포", 2100, 25, "이것은 추천하겠네. 뭐라해도 바란스가 좋다네."),
        new("카논포", 2500, 30, "이것은 사정 거리는 짧지만, 맞추면 일격필살로 대단한 것이네."),
    ];

    /// <summary>갈래 수.</summary>
    public static int Count => All.Length;

    /// <summary>번호로 찾는다. 안 실었으면(-1) null.</summary>
    public static Cannon? Of(int index) =>
        index >= 0 && index < All.Length ? All[index] : null;

    /// <summary>포탑 한 자리를 다는 값(<c>0x00496234</c> 의 <c>(새-지금) x 5 x 5 x 8</c>).</summary>
    public const int TurretPrice = 200;

    /// <summary>
    /// 포탑을 줄일 때 넘치는 대포를 되사 주는 비율(%).
    /// </summary>
    /// <remarks>
    /// "지금 싣고 있는 것은 가격의 30프로로 사 주겠네."(<c>0x00531F80</c>) —
    /// <c>0x004960FE</c> 의 <c>x3 / 10</c> 이다.
    /// </remarks>
    public const int BuyBackPercent = 30;
}
