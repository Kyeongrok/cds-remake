using CdsHelper.Support.Local.Models;

namespace CdsHelper.Game.Engine.Land;

/// <summary>
/// 부대배치 — <b>낼 수 있는 병종 넷</b>과 갈래마다 낼 수 있는 부대 수.
/// </summary>
/// <remarks>
/// 오래 못 짚던 「플레이어가 실제로 낼 수 있는 병종의 범위」가 여기다. 배치 화면은
/// 병종 스물넷을 다 내주지 않는다 — <b>제 기능에 따라 정해지는 넉 자리</b>만 내준다.
/// 화면이 지어질 때 그 넷을 <c>+0x120</c>~<c>+0x12C</c> 에 적어 두고
/// (<c>0x0049ED4C</c>~<c>0x0049EDF9</c>), 자리를 누르면 그 넷과 「없음」다섯 줄짜리
/// 「선택」 차림표를 편다(<c>0x0049F010</c>).
///
/// <code>
///   0049ed4c  검술·사격술·포술이 모두 3 이면  +0x120 = 3 무적제독, 아니면 2 제독
///   0049eda5  검술   == 3 이면  +0x124 = 1 중장기병,   아니면 0  기병
///   0049edc6  사격술 == 3 이면  +0x128 = 16 머스켓총대, 아니면 15 화승총대
///   0049edf1  포술   == 3 이면  +0x12c = 19 캐논포병,   아니면 18 포병
/// </code>
///
/// <b>곁증거가 둘.</b> 배치 화면이 읽어 들이는 부대 그림이 정확히 파트 8~15 여덟 장이고
/// (<c>0x004A020B</c>), 병종을 그 버퍼 안 자리로 옮기는 표(<c>0x0049F854</c>)도 이 여덟
/// 말고는 −1 을 낸다. 곧 <b>기병 · 중장기병 · 제독 · 무적제독 · 화승총대 · 머스켓총대 ·
/// 포병 · 캐논포병</b> 여덟이 플레이어가 볼 수 있는 전부다.
///
/// 기능 값은 <b>제독과 부관 중 높은 쪽</b>이다(<c>0x00446F70</c> 이 아군 대장
/// <c>[+0x98]</c> 의 기능과 부하 0번 자리 <c>0x005B60A0</c> 의 기능을 견주어 큰 것을 낸다).
/// </remarks>
public sealed class LandRoster
{
    /// <summary>판의 자리 수 — 앞열 셋 뒤열 셋.</summary>
    public const int SlotCount = 6;

    /// <summary>고를 수 있는 자리 넷.</summary>
    public const int Leader = 0, Melee = 1, Shot = 2, Cannon = 3;

    /// <summary>고를 수 있는 자리 수.</summary>
    public const int ChoiceCount = 4;

    /// <summary>「없음」 — 「선택」 차림표의 마지막 줄(<c>0x005594B0</c>).</summary>
    public const int None = 4;

    /// <summary>「선택」 차림표의 제목(<c>0x005594B8</c>).</summary>
    public const string PickTitle = " 선택 ";

    /// <summary>「없음」 줄의 글.</summary>
    public const string NoneWord = "없음";

    /// <summary>「결정」에 대장이 없을 때(<c>0x00559570</c>).</summary>
    public const string NeedLeaderWord = "제독의 부대를 배치해 주십시오";

    /// <summary>부대를 더 못 낼 때(<c>0x00559530</c>). 제목은 「경고」(<c>0x00559528</c>).</summary>
    public const string NoMoreWord = "더 이상, 대원은 배정하지 못합니다";

    /// <summary>그 경고의 제목.</summary>
    public const string WarnTitle = " 경고 ";

    /// <summary>지난번 배치가 지금 낼 수 있는 수를 넘을 때(<c>0x005594C8</c>).</summary>
    public const string TooManyWord = "현재의 부대배치가능수 보다 많아\n배치할 수 없습니다";

    /// <summary>사람이 모자라 지난번 배치를 못 펼 때(<c>0x00559500</c>).</summary>
    public const string CannotSplitWord = "현재 인원수로는 나눌 수 없습니다";

    /// <summary>갈래마다 보는 기능 번호 — 근접 검술 · 사격 사격술 · 포 포술.</summary>
    /// <remarks>
    /// <c>0x0049ED47</c> 이 차례로 2 · 4 · 3 을 물어보는데, 그것이 곧 기능표
    /// (<c>0x00560A10</c>)의 검술 · 사격술 · 포술이다. 행동속도 셈(<c>0x00447C20</c>)이
    /// 갈래마다 보는 기능과도 같다.
    /// </remarks>
    public static int SkillOf(int choice) => choice switch
    {
        Melee => Skill.Sword,
        Shot => Skill.Shooting,
        Cannon => Skill.Gunnery,
        _ => -1,
    };

    private readonly int[] _levels = new int[ChoiceCount];

    /// <summary>제독과 부관을 견주어 자리 넷을 정한다.</summary>
    /// <param name="men">총인원 — 선원 + 1(제독 자신). <c>0x0044A7CB</c> 이 그렇게 센다.</param>
    public LandRoster(int sword, int shooting, int gunnery, int men)
    {
        Men = Math.Max(1, men);
        _levels[Melee] = Clamp(sword);
        _levels[Shot] = Clamp(shooting);
        _levels[Cannon] = Clamp(gunnery);
        // 대장 자리는 셋을 다 익혔을 때만 무적제독이 된다.
        _levels[Leader] = Math.Min(_levels[Melee], Math.Min(_levels[Shot], _levels[Cannon]));
    }

    /// <summary>총인원.</summary>
    public int Men { get; }

    /// <summary>그 자리에 세우는 병종 번호.</summary>
    public int KindAt(int choice) => choice switch
    {
        Leader => Full(Leader) ? LandUnits.GreatAdmiral : LandUnits.Admiral,
        Melee => Full(Melee) ? LandUnits.HeavyHorse : LandUnits.Horse,
        Shot => Full(Shot) ? LandUnits.Musket : LandUnits.Matchlock,
        Cannon => Full(Cannon) ? LandUnits.Cannon : LandUnits.Gunner,
        _ => -1,
    };

    /// <summary>그 자리의 기능 자리(0~3).</summary>
    public int LevelAt(int choice) =>
        choice >= 0 && choice < ChoiceCount ? _levels[choice] : 0;

    /// <summary>
    /// 그 갈래를 몇 부대까지 낼 수 있는지 — <b>기능 + 1, 다 익혔으면 하나 더</b>.
    /// </summary>
    /// <remarks>
    /// <c>0x0049F70F</c> 어름이 <c>기능 − 놓은 수 + 1</c> 을 셈하고 기능이 3 이면 하나를
    /// 더 얹는다. 곧 자리는 0 이면 하나, 1 이면 둘, 2 면 셋, 3 이면 <b>다섯</b>이다.
    /// 대장은 이 셈 밖이라 늘 하나고, 갈래를 셀 때도 빠진다(<c>0x00447580</c> 이
    /// 대장 표시가 선 부대를 안 센다).
    /// </remarks>
    public int CapAt(int choice)
    {
        if (choice == Leader) return 1;
        int level = LevelAt(choice);
        return level + 1 + (level >= Skill.MaxLevel ? 1 : 0);
    }

    /// <summary>그 자리 이름 — 차림표에 세우는 글은 병종 이름 그대로다.</summary>
    public string NameAt(int choice)
    {
        int kind = KindAt(choice);
        return kind >= 0 && kind < LandUnits.Names.Length ? LandUnits.Names[kind] : "";
    }

    /// <summary>그 병종이 어느 자리에서 온 것인지. 아니면 −1.</summary>
    public int ChoiceOf(int kind)
    {
        for (int i = 0; i < ChoiceCount; i++) if (KindAt(i) == kind) return i;
        return -1;
    }

    private bool Full(int choice) => LevelAt(choice) >= Skill.MaxLevel;

    private static int Clamp(int level) => Math.Clamp(level, 0, Skill.MaxLevel);

    /// <summary>
    /// 제독과 부관 중 높은 쪽 기능으로 자리 넷을 짓는다(<c>0x00446F70</c>).
    /// </summary>
    /// <param name="borrowedMen">
    /// 발견 대본이 빌려 준 병력(<c>26 1C 02</c>). 주면 함대 선원 대신 이 수로 부대를 짓는다 —
    /// 파르테논 신전은 70명을 준다. 안 주면(−1) 함대 선원이다.
    /// </param>
    public static LandRoster For(Player player, Player.MateInfo? aide, int borrowedMen = -1)
    {
        int Best(int slot, int mine) => Math.Max(mine, Mate(slot, aide));

        return new LandRoster(
            Best(Skill.Sword, player.LevelOf(Skill.Names[Skill.Sword])),
            Best(Skill.Shooting, player.LevelOf(Skill.Names[Skill.Shooting])),
            Best(Skill.Gunnery, player.LevelOf(Skill.Names[Skill.Gunnery])),
            (borrowedMen >= 0 ? borrowedMen : player.Crew) + 1);
    }

    private static int Mate(int slot, Player.MateInfo? aide) => aide is not { } who ? 0 : slot switch
    {
        Skill.Sword => who.Sword,
        Skill.Shooting => who.Shooting,
        Skill.Gunnery => who.Gunnery,
        _ => 0,
    };
}
