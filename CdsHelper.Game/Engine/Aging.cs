using CdsHelper.Support.Local.Models;

namespace CdsHelper.Game.Engine;

/// <summary>
/// 제독이 <b>늙는다</b> — 마흔을 넘기면 해마다 생일 뒤 첫 저장에서 능력치가 움직인다(<c>0x0047C680</c>).
/// </summary>
/// <remarks>
/// 원본은 이 셈을 <b>제독을 적는 함수 첫머리</b>에 두었다. 그래서 여관·기능 메뉴 저장(<c>0x004A2814</c>),
/// 중단저장(<c>0x0048B771</c>), NEW GAME 을 다 지은 뒤의 저장(<c>0x0045F775</c>)이 다 이것을 거친다.
/// <code>
///   0047c688  cmp [esi+4], 0          ; 제독만(남의 인물은 건너뛴다)
///   0047c694  call [vt+0xC]           ; 나이(0x0047CB20) >= 40 이고
///   0047c6b6  오늘 날수 >= (다음 해 +0x304, 생월 +0xEC, 생일 +0xF0) 의 날수면(0x0042E6A0)
///   0047c6ee  +0x304 = 올해 + 1
///   0047c700  능력치 i 에 +0x2EC[i] 를 얹는다(0x00432C50 — 보이는 값 1~100 으로 자른다), 여섯 칸
///   0047c70a  다음 해 몫을 새로 굴린다
///               체력 −rand(3) · 지력 +rand(3) · 무력 −rand(3) · 매력 +rand(3) · 운 rand(7)−3
///               신앙심(+0x300)은 안 굴린다 — NEW GAME 의 0 그대로다
/// </code>
/// 몸은 줄고 머리·말솜씨는 는다. 처음 늙을 때는 얹을 몫이 다 0 이라 움직임이 없고, 몫만 굴려 둔다.
/// 세대교체는 이 칸을 안 건드린다 — 아들이 마흔이 되면 아버지가 굴려 둔 몫이 곧 얹힌다.
/// 알리는 말은 없다.
/// </remarks>
public static class Aging
{
    /// <summary>늙기 시작하는 나이(<c>0x0047C697</c> 의 <c>cmp eax, 0x28</c>).</summary>
    public const int From = 40;

    /// <summary>저장하기 앞서 부른다. 늙었으면 true.</summary>
    public static bool OnSave(Player player, Random dice)
    {
        if (player.Age < From) return false;
        var today = player.Date;
        if ((today.Year, today.Month, today.Day).CompareTo((player.AgingYear, player.BirthMonth, player.BirthDay)) < 0)
            return false;

        player.AgingYear = today.Year + 1;
        var steps = player.AgingSteps;
        for (int i = 0; i < steps.Length; i++) player.AdjustAbility(i, steps[i]);

        steps[Ability.Body] = -dice.Next(3);
        steps[Ability.Mind] = dice.Next(3);
        steps[Ability.Might] = -dice.Next(3);
        steps[Ability.Charm] = dice.Next(3);
        steps[Ability.Luck] = dice.Next(7) - 3;
        return true;
    }
}
