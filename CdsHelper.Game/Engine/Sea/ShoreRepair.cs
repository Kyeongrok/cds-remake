using CdsHelper.Support.Local.Models;

namespace CdsHelper.Game.Engine.Sea;

/// <summary>
/// 상륙해서 <b>자재로 배를 고친다</b>(<c>0x0048E140</c>) — 상륙 차림표의 셋째 줄이다.
/// </summary>
/// <remarks>
/// 조선소와 달리 <b>돈이 안 들고 날도 안 간다</b> — 드는 것은 자재뿐이다. 선원·피로도·규율·
/// 명성 어느 것도 안 건드린다.
/// <code>
///   0048e15e  자재가 0 통이면  「수리하는데 필요한 자재가 없습니다!」            0x00570AB8
///   0048e1a9  조선기술이 0 이면「조선기술을 가진 사람이 없습니다!」              0x00570AE0
///   0048e36b  고칠 배가 없으면「어느 배도 다 완전합니다. 수리할 필요는 없습니다.」0x00570B30
///   0048e4f8  올림값 = (조선기술 + 1) x 자재 통수                               ; 주사위 없음
///   0048e541  내구 = min(내구 + 올림값, 최대내구)
/// </code>
/// <b>되돌이다</b> — 한 척을 고치고 나면 자재가 남는 한 배 고르기부터 다시 묻는다.
///
/// 조선기술은 <b>제독과 부관(자리 0) 가운데 높은 쪽</b>이고(<c>0x0047CCA0(0xA,0,-1,-1,-1)</c>),
/// 부관이 더 높으면 부관이 「제가 수리하겠습니다.」(<c>0x00570BB0</c>) 하고 나선다.
///
/// 자재 수리는 <b>추진력과 내구를 같은 만큼</b> 올린다(<c>0x0048E4FE</c>·<c>0x0048E537</c>) —
/// 각각 제 최대치에서 잘린다. 그래서 폭풍에 상한 배는 여기서 둘 다 되돌아온다.
/// 알리는 말도 넷으로 갈린다(<c>0x00570BE8</c>·<c>0x00570C10</c>·<c>0x00570C38</c>·<c>0x00570C58</c>).
/// </remarks>
public static class ShoreRepair
{
    /// <summary>조선기술 자리(기능 열셋 가운데 열한째).</summary>
    public static readonly string Skill = Support.Local.Models.Skill.Names[Support.Local.Models.Skill.Shipwright];

    /// <summary>자재 한 통이 올리는 값 — <c>조선기술 + 1</c>(<c>0x0048E446</c>).</summary>
    public static int PerBarrel(int shipwright) => shipwright + 1;

    /// <summary>
    /// 그 배를 다 고치는 데 드는 통 수(<c>0x0048E447</c>) — 모자란 만큼을 올려 나눈다.
    /// </summary>
    public static int BarrelsFor(Ship ship, int shipwright)
    {
        int per = PerBarrel(shipwright);
        return per <= 0 ? 0 : (ship.MaxHp - ship.Hp + per - 1) / per;
    }

    /// <summary>고칠 데가 있는 배인지 — 성한 배는 목록에 안 오른다(<c>0x0048E284</c>).</summary>
    public static bool Damaged(Ship ship) => ship.Hp < ship.MaxHp || ship.Speed < ship.MaxSpeed;

    /// <summary>
    /// 고친 뒤에 내는 말(<c>0x0048E1BF</c> 벌) — 무엇이 얼마나 올랐는지로 넷이 갈린다.
    /// </summary>
    public static string RepairWord(int hp, int speed) =>
        hp > 0 && speed > 0
            ? hp == speed ? $"내구력, 추진력이 {hp}씩 올라갔습니다!"
                          : $"내구력이 {hp}, 추진력이 {speed} 올라갔습니다!"
        : speed > 0 ? $"추진력이 {speed} 올라갔습니다!"
        : hp > 0 ? $"내구력이 {hp} 올라갔습니다!"
        : "수리하는데 실패했습니다!";

    /// <summary>한 번에 고르게 하는 배 수(<c>0x0048E360</c> 의 <c>cmp 8</c>).</summary>
    public const int MaxListed = 8;
}
