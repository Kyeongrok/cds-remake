using CdsHelper.Support.Local.Models;

namespace CdsHelper.Game.Engine;

/// <summary>
/// 초심자(EASY)용으로 미리 만든 주인공 둘 — 라몬·데·마르시아스와 에밀리오·알발레스.
/// </summary>
/// <remarks>
/// 게임의 <c>0x0045E670</c> 이 제독 물건(<c>0x005B60A0</c>)에 값을 곧바로 박는다.
/// <code>
///   +0x08 얼굴(= 운명 자리)  +0x14 나라  +0x18 혈액형  +0x1C 직업
///   0x5B6188·8C·90 태어난 해·달·날   0x5B60C0 능력 여섯   0x5B60E0 기능 열셋   0x5B6114 언어
///   0x5B60D8 컨디션 = (체력 x 4 + 4) x 5   0x5B614C 명성 1500   0x5B6194 돈
///   0x5B6154 = 나라 수도, 그 도시 +0x1D |= 8(모항)   0x004AB420("C:STORY0/1.CDS")
/// </code>
/// 만들기 본체(<c>0x0045EBE0</c>)는 이 길로 오면 <c>[0x005A4D1A] |= 8</c> 을 세워 <b>누적 캐릭터 등록</b>을 막는다.
/// 우리 쪽은 그 비트 대신 <see cref="Support.Local.Models.Player.ActiveStoryBook"/>(이야기 책을 든 주인공)으로
/// 가린다 — 자택 「은퇴한다」가 그것으로 첫 물음을 가른다.
/// </remarks>
public static class Beginner
{
    /// <summary>
    /// 초심자용 주인공인지(<c>0x005A4D1A</c> 비트 8) — 이야기0·1 을 든 주인공이다. 새 주인공도 개인 이야기를 들므로
    /// 책이 있다는 것만으로는 가릴 수 없다.
    /// </summary>
    public static bool IsBeginnerBook(string? book) => book is "이야기0" or "이야기1";

    /// <summary>미리 만든 주인공 한 사람.</summary>
    /// <param name="Skills">기능 열셋(<see cref="Skill.Names"/> 차례).</param>
    /// <param name="Tongues">언어(<see cref="Skill.Languages"/> 차례, 모자라면 0).</param>
    public sealed record Hero(string Given, string Family, int BirthYear, int Month, int Day,
                              int Face, int Nation, int Blood, int Job,
                              int[] Abilities, int[] Skills, int[] Tongues,
                              int Gold, string StoryBook);

    /// <summary>명성 — 둘 다 1500 이다(<c>0x0045E888</c> · <c>0x0045EAF6</c>).</summary>
    public const int Fame = 1500;

    /// <summary>0 라몬(<c>0x0045E70E</c>) · 1 에밀리오(<c>0x0045E961</c>).</summary>
    public static readonly Hero[] All =
    [
        new("라몬", "데·마르시아스", 1456, 3, 19, Face: 5, Nation: 0, Blood: 0, Job: 0,
            Abilities: [75, 73, 75, 80, 78, 49],
            //         항해 운용 검술 포술 사격 의학 웅변 측량 역사 회계 조선 신학 과학
            Skills:    [2,   2,   1,   0,   1,   0,   1,   1,   2,   1,   0,   0,   1],
            //         스페인 포르투갈 로망스 게르만 슬라브 아랍
            Tongues:   [2,     3,       3,     0,     0,     1],
            Gold: 5000, StoryBook: "이야기0"),
        new("에밀리오", "알발레스", 1457, 2, 15, Face: 14, Nation: 1, Blood: 1, Job: 1,
            Abilities: [64, 81, 59, 87, 69, 62],
            Skills:    [2,   2,   2,   1,   1,   0,   0,   1,   1,   1,   0,   1,   0],
            Tongues:   [3,     2,       3,     0,     0,     1],
            Gold: 2500, StoryBook: "이야기1"),
    ];

    /// <summary>
    /// 새로 앉힌 주인공(<see cref="Game.NewPlayer"/> 뒤, 1480년 1월 1일)에 그 사람을 박는다.
    /// </summary>
    public static void Apply(Player player, Hero hero)
    {
        // 나이는 오늘과 생일로 태어난 해를 되짚는 셈이라, 태어난 해가 맞게 나이를 거꾸로 낸다.
        var today = player.Date;
        bool ahead = hero.Month > today.Month || (hero.Month == today.Month && hero.Day > today.Day);
        int age = today.Year - hero.BirthYear - (ahead ? 1 : 0);

        player.SetProfile(hero.Family, hero.Given, age, hero.Month, hero.Day,
                          hero.Blood, hero.Nation, hero.Face, fortune: hero.Face);
        player.JobIndex = hero.Job;
        player.SetAbilities(hero.Abilities);
        player.SetCondition((hero.Abilities[Ability.Body] * 4 + 4) * 5);

        for (int i = 0; i < Skill.Names.Length; i++)
            player.SetSkill(Skill.Names[i], i < hero.Skills.Length ? hero.Skills[i] : 0);
        for (int i = 0; i < Skill.Languages.Length; i++)
            player.SetTongue(Skill.Languages[i], i < hero.Tongues.Length ? hero.Tongues[i] : 0);

        player.SetGold(hero.Gold);
        player.Fame = Fame;

        // 개인 이야기(STORY0/1.CDS)를 겪게 묶는다 — 건물·도시·연도 조건이 맞을 때마다
        // StoryLog 가 장면을 찾아 튼다(CityPicView.CheckStory).
        player.SetActiveStoryBook(hero.StoryBook);
    }
}
