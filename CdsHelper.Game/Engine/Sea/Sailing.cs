using CdsHelper.Game.Local.Helpers;
using CdsHelper.Support.Local.Models;

namespace CdsHelper.Game.Engine.Sea;

/// <summary>
/// 함대가 얼마나 빨리 가는가 — 바람과 돛과 선원이 정한다.
/// </summary>
/// <remarks>
/// 한 줄 답: <b>배속도 = 추진력 x (풍속+1) x 돛효율 / 100</b> 이고, 함대속도는
/// <b>기함 속도와 함대 평균의 가운데</b>다.
/// <code>
/// ; 함대 속도  0x0048BCF0
/// if 뭍(말)이면              return 2
/// 풍향, 풍속 = 0x00424F40()
/// if 풍속 == 0               return 1                 ; 무풍
/// rel = (풍향 - 뱃머리) &amp; 15
///
/// for 배 여덟 칸:
///     효율 = 돛효율표[조합(배+0x68) * 16 + rel]
///     v = 추진력(배+0x38) * (풍속 + 1) * 효율 / 100
///     if 필요선원(배+0x30 + 10) &gt; 선원(배+0x34):
///         v = min(선원 * v / 필요, (v+1)/2)            ; 반토막 아래로
///     합 += v ; 수 += 1
///     기함이면 기함v = v
///
/// return (기함v + 합/수) / 2
/// </code>
/// <b>한 배만 좋아도, 다 좋아도 안 된다</b> — 느린 배 하나가 함대를 반쯤 끌어내린다.
///
/// 그 값을 칸으로 바꾸는 것은 발밑 지형 부류로 갈린다(<c>0x0048D0F0</c> 언저리).
/// <code>
///   부류 == 1 :  이동 = 9 * 속도 / 10        + 해류
///   그 밖     :  이동 = (3 * 속도 + 54) / 10 , 해류 없음
/// </code>
/// 누산기는 <b>64</b> 마다 한 칸을 넘긴다. 바다 칸은 두 부류가 지도에 반반 섞여 있어
/// 실제로는 한 칸 걸러 두 식을 오간다 — <b>좋은 바람의 값어치가 절반쯤 깎여</b> 들어오고,
/// 무풍이어도 느린 식이 5.4 를 내주므로 발이 아주 묶이지는 않는다.
///
/// 자세한 것은 볼트 <c>30.분석-항해 속도(돛·바람·해류)</c>.
/// </remarks>
public static class Sailing
{
    /// <summary>누산기가 <b>한 걸음</b>을 넘기는 값. 방위 벡터의 크기와 같다.</summary>
    /// <remarks>
    /// 게임은 누산기가 <c>±0x20</c> 을 넘으면 <c>0x40</c> 씩 덜어내며 한 걸음씩 옮긴다
    /// (<c>0x0048D2BA</c>). 벡터의 크기가 곧 이 값이라, 한 틱에 배는 <b>이동값 걸음</b>을
    /// 간다.
    /// </remarks>
    public const int CellUnits = 64;

    /// <summary>한 칸에 든 걸음 수. 게임의 자리 단위가 <b>1/16 칸</b>이다.</summary>
    /// <remarks>
    /// 위도 값(<c>0x005B63B4</c>)이 0~20000 인데 지도가 세로 1250칸이라 한 칸이 16 이다.
    /// 발견 판정도(<c>칸 = 0x5B63B4 / 16</c>), 주변지도가 배 자리를 넘길 때도
    /// (<c>0x00426150</c>) 같은 값으로 나눈다.
    ///
    /// <b>여기를 빠뜨려 배가 게임보다 네 배 느렸다.</b> 걸음을 <see cref="CellUnits"/> 로
    /// 나눠 곧장 칸으로 삼았는데, 그것은 누산기 눈금이지 칸이 아니다.
    /// </remarks>
    public const int StepsPerCell = 16;

    /// <summary>뭍(말)일 때의 붙박이 속도.</summary>
    public const int LandSpeed = 2;

    /// <summary>
    /// 배 종류마다 한 틱에 <b>뱃머리가 도는 눈금 수</b>. 게임 표(<c>0x00569FC0</c>, int32 x8)다.
    /// </summary>
    /// <remarks>
    /// 차례는 선체 번호(<see cref="Hull.GameId"/> — 게임에서는 기함 레코드 <c>+0x28</c>)다.
    /// 눈금 하나가 22.5도라, 코구는 한 틱에 67.5도를 돌고 대형카락·중카락은 22.5도밖에
    /// 못 돈다. 자세한 것은 볼트 <c>86.분석-바다 조타(커서 방향·뱃머리·이동 벡터)</c>.
    /// </remarks>
    public static readonly int[] TurnRates = [3, 2, 2, 2, 1, 1, 2, 2];

    /// <summary>선체를 모를 때 쓰는 도는 빠르기. 여덟 중 여섯이 이 값이다.</summary>
    public const int DefaultTurnRate = 2;

    /// <summary>그 선체가 한 틱에 도는 눈금 수.</summary>
    public static int TurnRateOf(Hull? hull)
    {
        if (hull == null) return DefaultTurnRate;
        int id = hull.GameId;
        return id >= 0 && id < TurnRates.Length ? TurnRates[id] : DefaultTurnRate;
    }

    /// <summary>
    /// 바닥 속도. 무풍에 셈이 0 으로 떨어졌을 때 이 값으로 받쳐 준다
    /// (<c>0x0048BE22</c>) — 표도 함대도 없어 셀 것이 없을 때도 이 값이다.
    /// </summary>
    public const int CalmSpeed = 1;

    /// <summary>
    /// 함대 속도. 게임의 <c>0x0048BCF0</c> 이다.
    /// </summary>
    /// <param name="player">함대.</param>
    /// <param name="sails">돛 효율표. 못 읽었으면 null — 그때는 돛이 없는 셈 친다.</param>
    /// <param name="windDir">풍향(16방위).</param>
    /// <param name="windSpeed">풍속. 0(무풍)이어도 셈은 그대로 돈다 — 게임은 여기서
    /// 물러서지 않고 <c>추진력 x 1 x 돛효율 / 100</c> 을 그대로 낸다.</param>
    /// <param name="heading">뱃머리(16방위).</param>
    /// <param name="onLand">뭍에 있는지.</param>
    public static int SpeedOf(Player player, SailTable? sails,
                              int windDir, int windSpeed, int heading, bool onLand)
    {
        if (onLand) return LandSpeed;
        if (sails == null || player.Ships.Count == 0) return CalmSpeed;

        int relative = (windDir - heading) & 0xF;
        int sum = 0, count = 0, flagship = 0;

        for (int i = 0; i < player.Ships.Count; i++)
        {
            var ship = player.Ships[i];
            int v = ship.Speed * (windSpeed + 1) * sails.Efficiency(ship.Sails, relative) / 100;

            // 사람이 모자라면 반토막 아래로 떨어진다.
            int need = ship.Crew;
            int aboard = CrewOn(player, i);
            if (need > aboard) v = Math.Min(aboard * v / Math.Max(1, need), (v + 1) / 2);

            // 무풍에 0 으로 떨어진 배만 한 칸 받쳐 준다(0x0048BE22). 바람이 있으면
            // 받쳐 주지 않는다 — 돛 효율이 0 인 각도(정면 역풍)에서는 정말 안 나간다.
            if (windSpeed == 0 && v == 0) v = CalmSpeed;

            sum += v;
            count++;
            if (i == player.Flagship) flagship = v;
        }

        return (flagship + sum / Math.Max(1, count)) / 2;
    }

    /// <summary>
    /// 그 배에 탄 선원 수.
    /// </summary>
    /// <remarks>
    /// 게임은 배마다 선원을 따로 담는데(<c>배+0x34</c>) 우리는 함대가 통째로 태운다.
    /// 그래서 <b>필요승원에 견주어 나눠 준다</b> — 함대 선원이 최저 승원에 못 미치면
    /// 배마다 고르게 모자란 셈이 된다.
    /// </remarks>
    private static int CrewOn(Player player, int index)
    {
        int need = player.MinCrew;
        if (need <= 0) return player.Crew;
        return (int)((long)player.Crew * player.Ships[index].Crew / need);
    }

    /// <summary>
    /// 속도를 한 틱에 나아갈 칸 수로. 발밑 부류가 1 이면 빠른 식, 아니면 느린 식이다.
    /// </summary>
    /// <remarks>
    /// 게임은 그림 번호가 <c>...80</c> 인 칸만 부류 1 로 보고 나머지 바다 칸은 부류 0 이다.
    /// 그 둘이 지도에 <b>반반 섞여</b> 있어 한 칸 걸러 두 식을 오간다. 우리 쪽에도 부류를
    /// 물어보는 길이 있으므로 그대로 쓴다.
    /// </remarks>
    /// <param name="onLand">
    /// 뭍이면 <b>칸 부류를 안 본다</b>. 부류는 바다 칸 그림이 가르는 것이라 뭍에서는 뜻이
    /// 없는데, 빠른 식에 넣으면 말이 <c>9x2/10 = 1.8</c> 걸음(0.11칸)으로 <b>기어간다</b> —
    /// 칸에 따라 섰다 갔다 하던 것이 그것이다. 뭍은 늘 같은 걸음이다.
    /// </param>
    public static double CellsPerTick(int speed, bool fastTile, bool onLand = false) =>
        (!onLand && fastTile ? 9.0 * speed / 10.0 : (3.0 * speed + 54.0) / 10.0) / StepsPerCell;

    /// <summary>
    /// 해류가 미는 만큼(칸). 빠른 부류의 칸에서만 받는다.
    /// </summary>
    /// <remarks>
    /// 게임은 배와 해류를 <b>같은 누산기</b>에 더한다(<c>0x0048D24C</c> · <c>0x0048D29C</c>).
    /// <code>
    ///   배    누산기 += 벡터[뱃머리] * 이동값            (x 는 * 경도보정 / 100)
    ///   해류  누산기 += 벡터[해류방위] * 세기 / 8        (x 는 * 경도보정 / 100)
    ///   누산기가 ±0x20 을 넘으면 0x40 씩 덜어내며 한 걸음씩 옮긴다  (0x0048D2BA)
    /// </code>
    /// 벡터의 크기가 곧 <c>0x40</c> 이므로 <b>배는 틱마다 이동값 걸음, 해류는 세기/8 걸음</b>
    /// 이다 — 해류는 배의 <c>세기 / (8 x 이동값)</c> 밖에 안 된다. 이동값이 9(무풍 언저리)
    /// 이고 세기가 8 이라도 배가 아홉 걸음 갈 때 해류는 한 걸음이다.
    ///
    /// <b>여기를 잘못 옮겨 배가 밀렸다.</b> 예전에는 걸음을 <see cref="CellUnits"/> 로 한 번만
    /// 나눠 해류가 <c>세기/8</c> <b>칸</b>이 되었는데, 배 쪽은 <c>이동값/64</c> 칸이라 해류가
    /// 64배로 세졌다. 역풍에 배가 뒤로 밀려나던 것이 이것이다 — 게임에서는 삼각돛이 정면
    /// 역풍에서도 1 을 내며 앞으로 나아간다.
    ///
    /// 걸음을 칸으로 바꾸는 것은 <see cref="StepsPerCell"/> 몫이다. 배와 해류가 같은
    /// 눈금을 쓰므로 둘 다 같은 수로 나눠야 한다.
    ///
    /// 경도 보정은 위도가 높을수록 경도 한 칸이 짧아지는 것을 메우는 값이다.
    /// </remarks>
    public static (double X, double Y) Drift((int X, int Y) vector, int strength, double lonScale) =>
        (vector.X * strength / 8.0 / CellUnits / StepsPerCell * lonScale,
         vector.Y * strength / 8.0 / CellUnits / StepsPerCell);

    /// <summary>
    /// 경도 보정 — 위도가 높을수록 경도 한 칸이 짧다.
    /// </summary>
    /// <remarks>
    /// 게임은 <c>65536*cos</c> 표(<c>0x005695F8</c>)를 63도에서 자르고 보간해 쓴다.
    /// 우리는 그냥 <c>1 / cos</c> 을 쓴다 — 게임 보간에는 부호가 뒤집힌 데가 있는데
    /// (더해야 할 자리에서 빼지 않는다) 어긋남이 0.5% 라 굳이 옮기지 않는다.
    /// </remarks>
    public static double LonScale(double lat)
    {
        double capped = Math.Min(Math.Abs(lat), MaxLatForScale);
        return 1.0 / Math.Max(0.25, Math.Cos(capped * Math.PI / 180.0));
    }

    /// <summary>경도 보정을 자르는 위도(게임은 <c>7000</c> = 63도에서 자른다).</summary>
    public const double MaxLatForScale = 63;
}
