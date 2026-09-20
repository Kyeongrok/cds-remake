using CdsHelper.Game.Local.Helpers;
using CdsHelper.Support.Local.Models;

namespace CdsHelper.Game.Engine.Town;

/// <summary>
/// 델포이 신탁(발견 대본 명령 <c>31</c>, 해석기 <c>0x0040A4C0</c>)이 성격 풀이 뒤에 잇는 말들.
/// </summary>
/// <remarks>
/// <code>
///   0040A615  내 운명 코드(vtbl+8, 서른여섯부터 +16) 줄 0x0051ACA0[코드] 앞 여섯 칸 중 가장 큰 양수 칸
///             → "그대의 아이는 %s에 뛰어날 것이오."          (칸 = 능력 이름 0x00560A88)
///   0040A665  아내가 없으면 여급 127명(0x005B3C60, 60바이트)을 차례로 보아 운명 코드가 <b>나와 같은</b> 첫 사람
///             → "그리고! 그대의 반려자가 될 사람이 %s에 있으니 빨리 구혼하라."   (그 여급의 도시)
///   0040A6D1  아내가 있으면 아내 운명 코드 줄 0x0051B0A0[코드] 앞 여섯 칸 중 가장 큰 양수 칸
///             → "그리고! 그대의 반려자는 %s에 뛰어나니 아이의 능력에 기대하라."
///   0040A735  컨디션(0x005B60D8) ≤ 100 → "그대의 수명이 얼마 안 남았으니 앞으로 조심하라."
///   0040A74F  "신의 계시는 이상이오. 주의하시오."
/// </code>
/// 반려자 찾기는 궁합(±1, <see cref="BarmaidTable.Destined"/>)이 아니라 <b>같은 코드</b>만 본다.
/// 두 표(32줄 x int 여덟)는 EXE 에서 떠 온 값이다 — 앞 여섯이 능력(체력·지력·무력·매력·운·신앙심) 칸이다.
/// </remarks>
public static class Oracle
{
    /// <summary>운명 코드마다의 능력 기울기 — 내 쪽(<c>0x0051ACA0</c>). 직업 보정표와 같은 자리다.</summary>
    private static readonly int[,] Mine =
    {
        { 0, 0, 0, 0, 0, 0 }, { 2, -3, -2, 5, -2, 0 }, { 2, -3, 2, -3, 2, 0 }, { -2, 3, -2, 3, -2, 0 },
        { -2, -4, 5, -1, 2, 0 }, { -2, 4, -3, -1, 2, 0 }, { -3, 2, 4, -2, -1, 0 }, { 2, 2, 2, -5, 2, 0 },
        { -5, -1, -1, 4, 3, 0 }, { 5, -4, 4, 1, -5, 0 }, { 3, 5, -3, -3, -1, 0 }, { 2, 0, -2, -3, 3, 0 },
        { 4, -2, 3, -4, 0, 0 }, { -3, -1, 4, -3, 3, 0 }, { 1, -3, -3, 0, 5, 0 }, { 2, 2, 2, -3, -3, 0 },
        { -2, 3, 3, -2, -2, 0 }, { -2, -2, 0, 2, 2, 0 }, { -3, -3, 5, 1, 0, 0 }, { 0, 4, 0, 0, -4, 0 },
        { 0, -2, 4, 0, 2, 0 }, { 0, 3, -2, -4, 3, 0 }, { -3, 3, 2, 0, -2, 0 }, { 3, -1, -1, -1, 0, 0 },
        { 1, 0, -3, 5, -3, 0 }, { 1, -2, 1, 3, -3, 0 }, { -3, 5, -1, -3, 2, 0 }, { -5, 3, 0, 0, 2, 0 },
        { -1, -1, -1, -1, 5, 0 }, { -3, -1, 4, -3, 3, 0 }, { 5, -4, -5, 2, 2, 0 }, { 0, 0, 0, 0, 0, 0 },
    };

    /// <summary>운명 코드마다의 능력 기울기 — 아내 쪽(<c>0x0051B0A0</c>).</summary>
    private static readonly int[,] Wife =
    {
        { 0, 0, 0, 0, 0, 0 }, { 0, 0, 0, 0, 0, 0 }, { 0, 0, 0, 0, 0, 0 }, { 0, 0, 0, 0, 0, 0 },
        { 0, 0, 0, 0, 0, 0 }, { -1, 2, -2, 4, -3, 0 }, { 3, -5, 5, 1, -4, 0 }, { -4, 1, 3, -3, 4, 0 },
        { 2, -2, -1, 2, -1, 0 }, { -1, 5, 4, -5, -3, 0 }, { -4, -3, 0, 3, 4, 0 }, { 5, 3, -1, -2, -5, 0 },
        { -3, -1, 0, -1, 5, 0 }, { -1, -2, 3, 4, -4, 0 }, { -4, 4, -4, 2, 4, 0 }, { 0, -3, 1, -2, 4, 0 },
        { 0, 2, -2, -1, 1, 0 }, { 0, 2, -3, 1, 1, 0 }, { -4, -4, 4, 2, 3, 0 }, { -5, 5, 3, 1, -3, 0 },
        { -4, 2, 4, -1, 0, 0 }, { 0, -5, -1, 4, 3, 0 }, { -3, 4, 0, -3, 3, 0 }, { 2, 0, -3, 0, 1, 0 },
        { -3, -1, 4, -2, 3, 0 }, { 3, 2, 4, -4, -5, 0 }, { -2, 1, -4, 1, 4, 0 }, { -4, -4, 2, 3, 3, 0 },
        { 1, 1, -2, 1, -1, 0 }, { 3, -3, -5, 3, 2, 0 }, { -2, 3, -5, 2, 2, 0 }, { 5, -2, 3, -1, -5, 0 },
    };

    /// <summary>
    /// 아내 운명 코드 줄의 그 칸 값(<c>0x0051B0A0</c>) — 아이를 가질 때 능력치에 이 값이 얹힌다(<c>0x00461139</c>).
    /// 코드나 칸이 표 밖이면 0.
    /// </summary>
    public static int WifeSlope(int code, int ability) =>
        code >= 0 && code < Wife.GetLength(0) && ability >= 0 && ability < 6 ? Wife[code, ability] : 0;

    /// <summary>줄에서 가장 큰 양수 칸(같으면 앞 칸). 양수가 없으면 −1.</summary>
    private static int Best(int[,] rows, int code)
    {
        if (code < 0 || code >= rows.GetLength(0)) return -1;
        int best = -1, top = 0;
        for (int i = 0; i < 6; i++)
            if (rows[code, i] > top) { top = rows[code, i]; best = i; }
        return best;
    }

    /// <summary>신탁이 성격 풀이 뒤에 내는 말들, 차례대로.</summary>
    public static List<string> Words(Game game)
    {
        var player = game.Player;
        var lines = new List<string>();
        int code = BarmaidTable.FortuneOf(player.Fortune, player.Age);

        if (Best(Mine, code) is var child and >= 0)
            lines.Add($"그대의 아이는 {Ability.Names[child]}에 뛰어날 것이오.");

        var barmaids = game.Barmaids;
        if (player.SpouseId < 0 || barmaids?.Find(player.SpouseId) is not { } wife)
        {
            // 아직 이 해에 안 선 여급은 뺀다 — 게임은 판에 들어와 있는 여급만 본다(vtbl+0x38).
            if (barmaids?.Barmaids.FirstOrDefault(b => b.Fortune == code && b.Year <= player.Date.Year) is { Name.Length: > 0 } her)
                lines.Add($"그리고! 그대의 반려자가 될 사람이 {game.CityName(her.City)}에 있으니 빨리 구혼하라.");
        }
        else if (Best(Wife, wife.Fortune) is var trait and >= 0)
        {
            lines.Add($"그리고! 그대의 반려자는 {Ability.Names[trait]}에 뛰어나니 아이의 능력에 기대하라.");
        }

        if (player.Condition <= Player.ConditionFull)
            lines.Add("그대의 수명이 얼마 안 남았으니 앞으로 조심하라.");
        lines.Add("신의 계시는 이상이오. 주의하시오.");
        return lines;
    }
}
