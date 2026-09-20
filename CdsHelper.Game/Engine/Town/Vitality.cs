using CdsHelper.Game.Engine.Sea;
using CdsHelper.Support.Local.Models;

namespace CdsHelper.Game.Engine.Town;

/// <summary>
/// 제독의 컨디션 — 곧 <b>HP</b>(<c>0x005B60D8</c>, 디버그 글 <c>HP:%4d</c>, 0~2000)가 닳고 차는 규칙.
/// </summary>
/// <remarks>
/// <code>
///   닳음   0x0047CEE0  도시 밖에서 날이 갈 때 — 이레째 날이면 −1, 괴혈병이 서 있으면 −3, 전염병이면 −3.
///                      300 · 100 아래로 <b>막 떨어진 날</b>에 부관이 한마디 한다(0x005391E8 · 0x00539178).
///   재해   0x004748F6  괴혈병(0x00474AA2 전염병) 날에 HP 가 0 이면 「정신차리십시오!」 뒤 「돌아올 수 없는 사람이
///                      되었다」로 GAME OVER.
///   입항   0x00492717  HP 0 → 「괜찮습니까? 안색이…」「힘이 다해 쓰러졌다」「정신 차리세요!」「숨을 거두었다」 GAME OVER.
///                      100 미만 · 300 미만이면 부관이 쉬라고 한다(0x0053BB80 · 0x0053BBF0).
///   회복   여관 숙박(30일) +30~59 (0x0047FCFF) · 자택 휴양 달마다 +50~99, 아내가 있으면 +0~19 더 (0x00460859) ·
///          수련 지난 날/10 (0x00491270). 올리고 내리는 것은 0x00432C80 이 0~2000 으로 자른다.
/// </code>
/// </remarks>
public static class Vitality
{
    /// <summary>경고 문턱 — 이보다 낮아지면 부관이 쉬라고 한다.</summary>
    public const int Faint = 100, Pale = 300;

    /// <summary>이레째 날인지를 셀 밑날. 게임은 연월일을 날수로 바꿔(0x0042E6A0) 7로 나눈다.</summary>
    private static readonly DateTime Epoch = new(1480, 1, 1);

    /// <summary>
    /// 1480년 1월 1일부터의 <b>게임 날수</b>(<c>0x0042E6A0</c>) — 이레를 세는 자다.
    /// </summary>
    /// <remarks>
    /// 게임 달력은 <b>율리우스력</b>이라 100 으로 나뉘는 해도 윤년이다(<c>y % 4</c>) — 1500년 2월 29일이 있다.
    /// 우리 <see cref="DateTime"/> 은 그레고리력이라 그날이 없으므로, 1500년 3월부터는 하루를 더해 날수를 맞춘다.
    /// 그래야 이레마다 도는 것(컨디션 −1 · 바람)이 원본과 같은 날에 돈다.
    /// </remarks>
    public static int DaySerial(DateTime when) =>
        (when - Epoch).Days + (when >= JulianLeap ? 1 : 0);

    /// <summary>그레고리력에 없는 율리우스력 윤일(1500-02-29) 다음 날.</summary>
    private static readonly DateTime JulianLeap = new(1500, 3, 1);

    /// <summary>
    /// 도시 밖에서 하루가 갔다(<c>0x0047CEE0</c>). 막 문턱 아래로 떨어졌으면 부관 말을, 아니면 null 을 낸다.
    /// </summary>
    public static string? PassDay(Player player)
    {
        int before = player.Condition;
        if (DaySerial(player.Date) % 7 == 0) player.SetCondition(player.Condition - 1);
        if (player.Has(SeaAilment.Scurvy)) player.SetCondition(player.Condition - 3);
        if (player.Has(SeaAilment.Plague)) player.SetCondition(player.Condition - 3);

        int after = player.Condition;
        if (before >= Faint && after < Faint) return "제독, 안색이 좋지 않습니다! 일단 집이나 여관에서 쉬십시오!";
        if (before >= Pale && after < Pale) return "제독, 안색이 안 좋습니다. 일단 집이나 여관에서 쉬는 것이 좋지 않겠습니까?";
        return null;
    }

    /// <summary>
    /// 재해로 쓰러지는지(<c>0x004748F6</c> · <c>0x00474AA2</c>). 쓰러졌으면 그 병 이름 줄을, 아니면 null 을 낸다.
    /// </summary>
    public static string? DiseaseDeath(Player player)
    {
        if (player.Condition > 0) return null;
        string name = player.Name;
        string topic = GameUiJosa(name, "은", "는");
        if (player.Has(SeaAilment.Scurvy)) return $"{name}{topic} 괴혈병에 걸려, 돌아올 수 없는 사람이 되었다...";
        if (player.Has(SeaAilment.Plague)) return $"{name}{topic} 전염병으로 인해, 돌아올 수 없는 사람이 되었다...";
        return null;
    }

    /// <summary>입항 때 부관이 쉬라고 하는 말(<c>0x0049280E</c>). 괜찮으면 null.</summary>
    public static string? EntryWarning(Player player) => player.Condition switch
    {
        < Faint => "제독, 안색이 좋지 않습니다! 일단 집이나 여관에서 쉬십시오!",
        < Pale => "제독, 안색이 좋지 않습니다! 집이나 여관에서 휴양하는 것이 좋지 않겠습니까?",
        _ => null,
    };

    /// <summary>입항 때 HP 가 0 이면 이어지는 말 넷(<c>0x00492735</c>~). 부관 말은 짝수 자리다.</summary>
    public static string[] CollapseWords(Player player)
    {
        string name = player.Name;
        string topic = GameUiJosa(name, "은", "는");
        return
        [
            "제독, 괜찮습니까? 안색이 안 좋은데요···",
            $"{name}{topic} 힘이 다해 그 자리에서 쓰러졌다···",
            "제독, 정신 차리세요! 제독, 제독!!",
            $"{name}{topic} 극도의 피로로 숨을 거두었다···",
        ];
    }

    /// <summary>여관 숙박 한 번의 회복(<c>0x0047FCF1</c>) — 30 + rand(30).</summary>
    public static int InnRest(Random rng) => 30 + rng.Next(30);

    /// <summary>자택 휴양의 회복(<c>0x0046082C</c>) — 달마다 50 + rand(50), 아내가 있으면 rand(20) 더.</summary>
    public static int HomeRest(Random rng, int months, bool married) =>
        (50 + rng.Next(50) + (married ? rng.Next(20) : 0)) * months;

    private static string GameUiJosa(string word, string closed, string open) =>
        UI.Views.GameUi.Josa(word, closed, open);
}
