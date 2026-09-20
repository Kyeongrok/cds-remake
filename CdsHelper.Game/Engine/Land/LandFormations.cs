using CdsHelper.Game.Local.Helpers;
using CdsHelper.Support.Local.Models;

namespace CdsHelper.Game.Engine.Land;

/// <summary>
/// 육상전에서 <b>적이 내는 부대 세트</b> 여덟 벌 — 맘루크·예니체리·사무라이가 여기서 나온다.
/// </summary>
/// <remarks>
/// 게임은 문화권으로 진형 여덟 가운데 하나를 고른다(<c>0x004A1320</c>, 뜀표
/// <c>0x004A13C0</c>). 어느 진형이든 얼개가 같다 — <b>첫 부대가 대장</b>이고 그 뒤에
/// 정해진 병종이 차례로 붙고, 자리가 남으면 <b>대장을 뺀 것 가운데 하나를 굴려
/// 되풀이</b>한다(<c>0x004A067F</c> 벌).
///
/// 몇몇 자리는 <b>적 대장의 기능</b>이 정한다. 진형 0(유럽)만 <c>기능 &gt;= 3</c> 하나로
/// 가르고(플레이어가 낼 병종을 고르는 규칙과 같다), 나머지는
/// <c>3 이면 큰 것 · 2 면 반반 · 그 밑이면 작은 것</c> 세 갈래다.
///
/// 여기 적힌 것이 <b>본</b>이고, 사람이 고친 것은 <see cref="LandFormationEdits"/> 가
/// 위에 얹는다. 놀이도 고치는 창도 <see cref="Of"/> 하나만 본다.
/// </remarks>
public static class LandFormations
{
    /// <summary>진형 수.</summary>
    public const int Count = 8;

    /// <summary>한 진형에 적어 둘 수 있는 자리 수 — 대장까지 여섯이다.</summary>
    public const int MaxUnits = LandBattle.PerSide;

    /// <summary>
    /// 자리 하나.
    /// </summary>
    /// <param name="Big">기능이 넉넉할 때 세우는 병종. 안 갈리는 자리면 이것 하나뿐이다.</param>
    /// <param name="Small">기능이 모자랄 때 세우는 병종. −1 이면 안 갈린다.</param>
    /// <param name="Skill">
    /// 무엇을 보고 가르는지 — <see cref="Support.Local.Models.Skill.Sword"/> 따위.
    /// −1 이면 안 갈리고, <see cref="AllThree"/> 면 <b>검술·포술·사격술이 다</b> 넉넉해야 한다.
    /// </param>
    public readonly record struct Slot(int Big, int Small = -1, int Skill = -1)
    {
        /// <summary>기능으로 갈리는 자리인지.</summary>
        public bool Splits => Small >= 0 && Skill != -1;
    }

    /// <summary>세 기능을 다 본다는 표 — 진형 0 의 대장 자리다.</summary>
    public const int AllThree = 99;

    /// <summary>
    /// 진형 한 벌.
    /// </summary>
    /// <param name="Coin">
    /// 기능이 하나 모자랄 때(2) <b>반반으로 굴리는지</b>. 진형 0 만 거짓이다 —
    /// 거기서는 <c>&gt;= 3</c> 하나로 딱 갈린다.
    /// </param>
    public readonly record struct Shape(string Name, string Where, bool Coin, Slot[] Units);

    /// <summary>
    /// 게임에 박힌 여덟 벌. 차례가 곧 진형 번호다.
    /// </summary>
    private static readonly Shape[] Stock =
    [
        // 0 — 0x004A0530. 플레이어가 낼 수 있는 넷과 똑같다.
        new("유럽 정규군", "이베리아 · 북유럽 · 지중해", false,
        [
            new(LandUnits.GreatAdmiral, LandUnits.Admiral, AllThree),
            new(LandUnits.Cannon, LandUnits.Gunner, Skill.Gunnery),
            new(LandUnits.Musket, LandUnits.Matchlock, Skill.Shooting),
            new(LandUnits.HeavyHorse, LandUnits.Horse, Skill.Sword),
        ]),

        // 1 — 0x004A06B0
        new("아프리카 부족군", "아프리카 · 동남아시아", true,
        [
            new(LandUnits.Chief), new(LandUnits.Shaman), new(LandUnits.Bow),
            new(LandUnits.Spear), new(LandUnits.Light),
        ]),

        // 2 — 0x004A07C0. 맘루크·예니체리가 이것이다.
        new("이슬람 상비군", "이슬람", true,
        [
            new(LandUnits.General),
            new(LandUnits.Cannon, LandUnits.Gunner, Skill.Gunnery),
            new(LandUnits.Camel, LandUnits.Light, Skill.Sword),
            new(LandUnits.Bow),
        ]),

        // 3 — 0x004A0980
        new("인도 코끼리군", "인도", true,
        [
            new(LandUnits.General), new(LandUnits.Monk),
            new(LandUnits.Elephant, LandUnits.Light, Skill.Sword),
            new(LandUnits.Bow),
        ]),

        // 4 — 0x004A0AE0
        new("중국 화포군", "중국", true,
        [
            new(LandUnits.General), new(LandUnits.Bombard), new(LandUnits.Bow),
            new(LandUnits.HeavyHorse, LandUnits.Horse, Skill.Sword),
        ]),

        // 5 — 0x004A0C30
        new("중앙아시아 기마군", "중앙아시아", true,
        [
            new(LandUnits.General), new(LandUnits.Bow),
            new(LandUnits.HeavyHorse, LandUnits.Horse, Skill.Sword),
        ]),

        // 6 — 0x004A0D50
        new("일본 무가군", "일본", true,
        [
            new(LandUnits.Lord),
            new(LandUnits.Hatamoto, LandUnits.Samurai, Skill.Sword),
            new(LandUnits.Ninja), new(LandUnits.Ninja),
        ]),

        // 7 — 0x004A0E70
        new("아메리카 부족군", "아메리카", true,
        [
            new(LandUnits.Chief), new(LandUnits.Leopard), new(LandUnits.Indio),
            new(LandUnits.Bow),
        ]),
    ];

    /// <summary>고치기 전, 게임에 박힌 그대로의 진형.</summary>
    public static Shape Original(int shape) =>
        Stock[Math.Clamp(shape, 0, Count - 1)] with { };

    /// <summary>그 진형. 사람이 고쳐 둔 것이 있으면 그것이 이긴다.</summary>
    public static Shape Of(int shape) =>
        LandFormationEdits.Apply(Math.Clamp(shape, 0, Count - 1), Original(shape));

    /// <summary>
    /// 그 문화권이 쓰는 진형 번호(<c>0x004A13C0</c>).
    /// </summary>
    public static int ShapeOf(int culture) => culture switch
    {
        0 or 1 or 2 => 0,
        3 or 8 => 1,
        4 => 2,
        5 => 3,
        6 => 4,
        7 => 5,
        9 => 6,
        _ => 7,
    };

    /// <summary>
    /// 그 진형이 이번 판에 실제로 낼 병종들. 자리가 남으면 굴려 되풀이한다.
    /// </summary>
    /// <param name="units">낼 부대 수.</param>
    /// <param name="sword">적 대장의 검술 · <paramref name="gunnery"/> 포술 ·
    /// <paramref name="shooting"/> 사격술.</param>
    public static int[] Muster(int shape, int units, int sword, int gunnery, int shooting,
                               GameRandom dice)
    {
        var set = Of(shape);
        var line = new int[Math.Max(1, units)];
        if (set.Units.Length == 0) return line;

        // 자리마다 기능을 한 번씩만 굴린다 — 되풀이해도 같은 병종이 나오게.
        var picked = new int[set.Units.Length];
        for (int i = 0; i < picked.Length; i++)
            picked[i] = Resolve(set, set.Units[i], sword, gunnery, shooting, dice);

        line[0] = picked[0];
        for (int i = 1; i < line.Length; i++)
            line[i] = i < picked.Length
                ? picked[i]
                // 늘어놓을 것이 다 떨어지면 대장을 뺀 것 가운데 하나를 굴린다.
                : picked.Length > 1 ? picked[1 + dice.Next(picked.Length - 1)] : picked[0];
        return line;
    }

    /// <summary>그 자리에 실제로 설 병종 하나.</summary>
    private static int Resolve(Shape set, Slot slot, int sword, int gunnery, int shooting,
                               GameRandom dice)
    {
        if (!slot.Splits) return slot.Big;

        int level = slot.Skill switch
        {
            AllThree => Math.Min(sword, Math.Min(gunnery, shooting)),
            Skill.Gunnery => gunnery,
            Skill.Shooting => shooting,
            _ => sword,
        };
        bool big = level >= Skill.MaxLevel
                   || (set.Coin && level == Skill.MaxLevel - 1 && dice.Next(2) == 0);
        return big ? slot.Big : slot.Small;
    }

    /// <summary>그 자리를 사람이 읽을 말로.</summary>
    public static string Describe(Slot slot)
    {
        string big = Name(slot.Big);
        if (!slot.Splits) return big;

        string how = slot.Skill switch
        {
            AllThree => "검술·포술·사격술",
            Skill.Gunnery => "포술",
            Skill.Shooting => "사격술",
            _ => "검술",
        };
        return $"{how} 넉넉하면 {big}, 아니면 {Name(slot.Small)}";
    }

    /// <summary>병종 이름. 범위 밖이면 번호를 그대로 낸다.</summary>
    public static string Name(int kind) =>
        kind >= 0 && kind < LandUnits.Names.Length ? LandUnits.Names[kind] : $"{kind}";
}
