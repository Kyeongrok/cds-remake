namespace CdsHelper.Support.Local.Models;

/// <summary>
/// 원본이 미리 갖춰 둔 배 이름 스물하나.
/// </summary>
/// <remarks>
/// 조선소 개조의 "선명변경" 과 배를 살 때 뜨는 <b>선명입력</b> 창에서 이 목록이 먼저 뜬다.
/// 원본은 이름 포인터 표를 <c>0x0053C178</c> 에 두고 스물하나를 늘어놓는다
/// (문자열은 <c>0x00531350</c> 부터 죽 이어져 있다).
///
/// <see cref="Hull.All"/> 과 같은 까닭으로 표를 읽지 않고 여기 적어 둔다 — 스물하나뿐이고
/// 판이 바뀔 것도 아니다.
/// </remarks>
public static class ShipNames
{
    /// <summary>게임 표 차례 그대로.</summary>
    public static readonly string[] All =
    [
        "산티아고", "산마르코", "산안토니오", "산마르틴", "산세바스찬", "산죠르디",
        "아르메리아", "루이자", "카타리나", "콘세프시온", "블랑카", "에레오노라",
        "요한나", "테레사", "디니스", "산타마리아", "후안나", "아사냐",
        "콘스타시아", "베렌게라", "트리니다드",
    ];

    /// <summary>이름에 쓸 수 있는 가장 긴 길이(<c>0x00423CF0</c> 의 <c>push 0x24</c>).</summary>
    public const int MaxLength = 36;

    /// <summary>
    /// 아직 안 쓴 이름 하나. 스물하나를 다 썼으면 번호를 붙여 낸다.
    /// </summary>
    /// <remarks>
    /// 이미 쓰는 이름이 있으면 <b>안 쓴 것 가운데 아무거나</b> 고른다(요청) — 목록 차례대로 내면 배를 살 때마다 같은 차례의
    /// 이름이 나왔다. 처음 한 척은 목록 맨 앞 그대로다. 창에서 사람이 같은 이름을 골라 적는 것은 막지 않는다.
    /// </remarks>
    /// <param name="taken">이미 쓰고 있는 이름.</param>
    public static string Suggest(IEnumerable<string> taken, Random? random = null)
    {
        var used = new HashSet<string>(taken);
        var free = All.Where(name => !used.Contains(name)).ToList();
        if (free.Count > 0) return used.Count == 0 ? free[0] : free[(random ?? Random.Shared).Next(free.Count)];

        for (int n = 2; ; n++)
            foreach (string name in All)
                if (used.Add($"{name} {n}")) return $"{name} {n}";
    }
}
