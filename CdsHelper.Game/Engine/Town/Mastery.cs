using CdsHelper.Support.Local.Models;

namespace CdsHelper.Game.Engine.Town;

/// <summary>
/// 기능이나 언어를 <b>숙달</b>(레벨 3)하면 능력이 오른다(<c>0x00490B40</c>).
/// </summary>
/// <remarks>
/// 수련으로 익힌 뒤(<c>0x004912C6</c> 기능 · <c>0x0049131C</c> 언어) 부른다. 새 레벨이 3 일 때만 굴리고,
/// 기능마다 오르는 능력이 정해져 있다(점프표 <c>0x00490D20</c>, r3 = <c>rand(3)</c>).
/// <code>
///   항해술·운용술        체력 r3 · 매력 r3
///   검술                무력 r3+1
///   포술·사격술·조선기술   체력 r3 · 무력 r3
///   의학                체력 r3+1
///   웅변                매력 r3+1
///   측량                체력 r3 · 지력 r3
///   역사학·회계          지력 r3 · 매력 r3
///   신학·과학            운 r3+2
///   언어(어느 것이든)     지력 r3
/// </code>
/// 오른 만큼 능력마다 「%s%s %d 올라갔다!」(<c>0x0055A4A8</c>, 조사 이/가)를 얼굴 없이 알린다.
/// </remarks>
public static class Mastery
{
    /// <summary>능력 여섯에 더할 값. 숙달이 아니면 다 0 이다.</summary>
    /// <param name="skill">기능 번호(<see cref="Skill.Names"/> 차례). 언어면 −1.</param>
    public static int[] Gains(int skill, bool tongue, int newLevel, Random rng)
    {
        var up = new int[Ability.Names.Length];
        if (newLevel != Skill.MaxLevel) return up;

        int R() => rng.Next(3);
        if (tongue) { up[Ability.Mind] = R(); return up; }

        switch (skill)
        {
            case 0 or 1: up[Ability.Body] = R(); up[Ability.Charm] = R(); break;   // 항해술 · 운용술
            case 2: up[Ability.Might] = R() + 1; break;                              // 검술
            case 3 or 4 or 10: up[Ability.Body] = R(); up[Ability.Might] = R(); break; // 포술 · 사격술 · 조선기술
            case 5: up[Ability.Body] = R() + 1; break;                               // 의학
            case 6: up[Ability.Charm] = R() + 1; break;                              // 웅변
            case 7: up[Ability.Body] = R(); up[Ability.Mind] = R(); break;           // 측량
            case 8 or 9: up[Ability.Mind] = R(); up[Ability.Charm] = R(); break;     // 역사학 · 회계
            case 11 or 12: up[Ability.Luck] = R() + 2; break;                        // 신학 · 과학
        }
        return up;
    }
}
