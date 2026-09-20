using CdsHelper.Support.Local.Models;

namespace CdsHelper.Game.Engine.Sea;

/// <summary>
/// 적 함대의 배 짜기 — 해전 들머리 <c>0x00440D90</c> 그대로다.
/// </summary>
/// <remarks>
/// 적장의 <b>나라와 그 해</b>로 선체를 고르고, 척수·승원·대포 수는 <b>적장 능력</b>으로 굴린다
/// (볼트 <c>92.분석-적 함대 배 짜기(선체·내구·선원·대포)</c>).
/// <code>
///   척수     무력/14 (+rand(2) 해적·군인·정복자)  1~8                ; 4절
///   선체     나라 갈래(0x441968) → 규칙 A~E(0x441990), 뒤 슬롯이 큰 배   ; 5-1·5-2
///   배 값    선체표 아래값 그대로(흔들림 없음), 내구 가득, 돛 = 표 +0x3C  ; 5-3
///   대포     (갈래 x 연대) 표 (a,b) · 수 = min(위값, (아래값+2)*(무력+지력)/200 + rand(포술) + 1)
///   승원     기함 min(m+X, 5m) · 호위 min(m + X*7/10 + rand(4), 5m)      ; 5-5·5-6
/// </code>
/// 괴물(271~274)은 배를 짓지 않는다. 누적 캐릭터(276~280)를 습격하면 행적이 채운 함대 목록이
/// 척수와 선체를 대신 정한다(<see cref="Engine.AccReplay.FleetOf"/>).
/// </remarks>
public static class EnemyFleet
{
    /// <summary>직업 번호(인물 밑표 <c>+0x20</c>).</summary>
    public const int ExplorerJob = 0, ConquerorJob = 3, PirateJob = 4, MissionaryJob = 5,
                     MerchantJob = 6, SoldierJob = 7;

    /// <summary>괴물 인물 번호(<c>0x10F</c>~<c>0x112</c>) — 배가 없다.</summary>
    public const int FirstMonster = 271, LastMonster = 274;

    /// <summary>괴물 인물 번호 — 크라켄 · 시서펜트 · 식인상어 · 맨터.</summary>
    public const int Kraken = 271, SeaSerpent = 272, ManEater = 273, Manta = 274;

    /// <summary>
    /// 괴물을 퇴치하고 오르는 능력치(<c>0x0043553C</c>) — 판이 끝나면 제 자리에서 오른다.
    /// </summary>
    /// <remarks>
    /// <code>
    ///   식인상어  무력 +5                      0x0056A700
    ///   크라켄    무력·지력·매력 각각 +2        0x0056A738
    ///   시서펜트  매력 +5                      0x0056A780
    ///   맨터      지력 +5                      0x0056A7B8
    /// </code>
    /// </remarks>
    /// <remarks>글 앞의 <b>빈칸 둘</b>도 원본 그대로다.</remarks>
    public static (string Words, (int Ability, int By)[] Gains) MonsterPrize(int person) => person switch
    {
        ManEater => ("  식인 상어를 퇴치했다! 무력이 5 올라갔다!", [(Ability.Might, 5)]),
        Kraken => ("  크라켄을 퇴치했다! 무력, 매력, 지력이 각각 2 올라갔다!",
                   [(Ability.Might, 2), (Ability.Mind, 2), (Ability.Charm, 2)]),
        SeaSerpent => ("  시서펜트를 퇴치했다! 매력이 5 올라갔다!", [(Ability.Charm, 5)]),
        Manta => ("  맨터를 퇴치했다! 지력이 5 올라갔다!", [(Ability.Mind, 5)]),
        _ => ("", []),
    };

    /// <summary>적 배 한 척.</summary>
    /// <param name="Hull">게임 선체 번호(<see cref="Hull.Table"/>).</param>
    /// <param name="Crew">승원.</param>
    /// <param name="MinCrew">필요승원(표 <c>+0x34</c> + 10).</param>
    /// <param name="Gun">대포 갈래(0 세이커 · 1 캘버린 · 2 페리에 · 3 카논). 적은 −1 이 안 나온다.</param>
    /// <param name="Guns">대포 수.</param>
    public sealed record Ship(int Hull, string HullName, int Speed, int Hp, int Capacity,
                              int Crew, int MinCrew, int Gun, int Guns, int[] Sails);

    /// <summary>게임의 <c>rand(n)</c>(<c>0x004B7C0F</c>) — n 이 2 보다 작으면 0 이다.</summary>
    private static int Rand(Random rng, int n) => n < 2 ? 0 : rng.Next(n);

    /// <summary>
    /// 해전에 나오는 척수(<c>0x00440D90</c> 4절).
    /// </summary>
    /// <remarks>교섭 창의 척수(<see cref="Enemy.Ships"/>)와 달리 굴림이 있고 덤이 작다.</remarks>
    public static int CountOf(in Captain leader, Random rng)
    {
        int n = leader.Might / 14;
        if (leader.Job is PirateJob or SoldierJob or ConquerorJob) n += Rand(rng, 2);
        return Math.Clamp(n, 1, SeaBattle.PerSide);
    }

    /// <summary>나라 → 갈래 0~9(바이트 표 <c>0x00441968</c>). 표 밖은 9 다.</summary>
    public static int GroupOf(int nation) => nation switch
    {
        0 or 1 => 0,
        3 => 1,
        6 => 2,
        8 or 9 or 10 => 3,
        11 or 12 or 13 => 4,
        14 => 5,
        16 => 6,
        26 or 27 => 7,
        37 => 8,
        _ => 9,
    };

    /// <summary>갈래 → 대포 갈래(<c>0x00441940</c>).</summary>
    private static readonly int[] GunGroups = [0, 2, 3, 3, 1, 3, 0, 4, 4, 0];

    /// <summary>
    /// 대포표 (a, b) — [대포 갈래][연대]. 스택 지역표라 <c>0x004410FE</c> 가 채운다.
    /// </summary>
    /// <remarks>a 는 나쁜 포를 싣는 몫(%), b 는 포 등급이다.</remarks>
    private static readonly (int A, int B)[][] GunTable =
    [
        [(80, 3), (25, 5), (50, 5), (80, 5)],   // 0 포르투갈·에스파니아·덴마크·그 밖
        [(70, 1), (30, 3), (30, 4), (60, 4)],   // 1 잉글랜드
        [(60, 1), (80, 1), (80, 3), (60, 5)],   // 2 프랑스
        [(30, 1), (40, 1), (50, 3), (60, 3)],   // 3 이탈리아·신성로마
        [(40, 1), (40, 1), (60, 1), (60, 1)],   // 4 이슬람
    ];

    /// <summary>대포용 연대 — 1490 전 0 · 1500 전 1 · 1510 전 2 · 그 뒤 3.</summary>
    private static int EraOf(int year) => year < 1490 ? 0 : year < 1500 ? 1 : year < 1510 ? 2 : 3;

    /// <summary>
    /// 슬롯 <paramref name="i"/>(0 = 기함)의 선체. 규칙 줄은 <b>위에서부터 처음 맞는 것</b>이다.
    /// </summary>
    /// <remarks>
    /// 갈래 9(표 밖 나라)는 게임이 선체 칸에 안 쓰고 앞 판 값이 남는다(<c>jmp 0x4414CB</c>).
    /// 그 구멍은 옮길 수 없어 <b>카라벨로 갈음한다</b> — 우리 어림이다.
    /// </remarks>
    public static int HullOf(int nation, int year, int i)
    {
        int y = year;
        switch (GroupOf(nation))
        {
            case 0 or 6:        // A 포르투갈·에스파니아·덴마크 (0x00441378)
                if (y >= 1535 && i >= 4) return Hull.Galleon;
                if (y >= 1520 && y < 1535 && i >= 6) return Hull.HeavyCarrack;
                if ((y >= 1505 && i >= 5) || (y >= 1520 && i >= 3) || y >= 1535) return Hull.LargeCarrack;
                if ((y >= 1490 && i >= 6) || (y >= 1505 && i >= 2) || y >= 1520) return Hull.Carrack;
                if (i >= 6 || (y >= 1490 && i >= 3)) return Hull.LargeCaravel;
                return Hull.Caravel;

            case 1 or 5:        // B 프랑스·신성로마 (0x00441432)
                return (y >= 1500 && i >= 5) || (y >= 1520 && i >= 4) ? Hull.Carrack : Hull.Cog;

            case 2 or 3:        // C 제노바·베니스·교황령·나폴리 (0x0044145C)
                return (y >= 1485 && i >= 6) || (y >= 1505 && i >= 5) || (y >= 1520 && i >= 3)
                    ? Hull.LargeCaravel : Hull.Caravel;

            case 4:             // D 잉글랜드·스코틀랜드·아일랜드 (0x00441493)
                return (y >= 1490 && i >= 6) || (y >= 1515 && i >= 4) || y >= 1535
                    ? Hull.Carrack : Hull.Cog;

            case 7 or 8:        // E 맘루크·하프스·오스만 (0x004414C5)
                return Hull.Dhow;

            default:            // 갈래 9 — 게임은 앞 판 값. 우리는 카라벨.
                return Hull.Caravel;
        }
    }

    /// <summary>승원 덤 X(직업별, <c>0x004419E0</c>).</summary>
    private static int ExtraCrew(in Captain c, Random rng) => c.Job switch
    {
        ExplorerJob => Rand(rng, 5) + c.Might / 4 + c.Charm / 5 + 1,
        ConquerorJob or SoldierJob => c.Might * 4 / 5 + Rand(rng, 10) + 1,
        PirateJob => c.Might + Rand(rng, 20) + 1,
        MissionaryJob or MerchantJob => Rand(rng, 2) + (c.Faith + 1) / 7 + c.Charm / 7 + 1,
        _ => 0,
    };

    /// <summary>
    /// 적 함대를 짓는다. 괴물이면 빈 목록이다.
    /// </summary>
    /// <param name="year">그 해(<c>[0x5A4D20]</c>).</param>
    /// <param name="hulls">
    /// 누적 캐릭터를 습격할 때 넘기는 함대 목록(<c>0x0048CC20</c>). 있으면 척수가 목록 길이이고
    /// 선체도 목록 그대로다 — 척수 굴림(<c>0x0044104E</c>)과 선체 고르기(<c>0x0044135C</c>)를 건너뛴다.
    /// 대포·승원은 여느 적과 같다.
    /// </param>
    public static List<Ship> Build(in Captain leader, int year, Random rng, int[]? hulls = null)
    {
        var ships = new List<Ship>();
        if (leader.Id is >= FirstMonster and <= LastMonster) return ships;

        int count = hulls is { Length: > 0 } ? Math.Min(hulls.Length, SeaBattle.PerSide) : CountOf(leader, rng);
        var (a, b) = GunTable[GunGroups[GroupOf(leader.Nation)]][EraOf(year)];
        int better = count - a * count / 100 - 1;
        int x = ExtraCrew(leader, rng);

        for (int i = 0; i < count; i++)
        {
            var spec = Hull.Table[hulls is { Length: > 0 } ? hulls[i] : HullOf(leader.Nation, year, i)];

            // 대포 — 앞 슬롯(기함 쪽)이 좋은 포를 받는다. 수는 위값으로 자른다.
            int gun = better > i ? (b + 2) / 2 : (b - 1) / 2;
            int guns = (spec.GunsMin + 2) * (leader.Might + leader.Mind) / 200
                       + Rand(rng, leader.Gunnery) + 1;
            guns = Math.Min(guns, spec.GunsMax);

            // 승원 — 호위는 넘는지 볼 때와 넣을 때 rand(4) 를 따로 굴린다(0x004417A2).
            int m = spec.Crew;
            int crew;
            if (i == 0)
                crew = Math.Min(m + x, 5 * m);
            else
                crew = m + x * 7 / 10 + Rand(rng, 4) > 5 * m ? 5 * m : m + x * 7 / 10 + Rand(rng, 4);

            ships.Add(new Ship(spec.Id, spec.Name, spec.Speed, spec.Hp, spec.Capacity,
                               crew, m, gun, guns, spec.Sails));
        }
        return ships;
    }
}
