using System.Windows.Controls;
using CdsHelper.Game.Engine.Menu;

namespace CdsHelper.Game.UI.Views;

/// <summary>
/// 상단 띠에 무엇을 띄울지 고르는 창 — 게임의 <b>도시정보</b> 다. 도시 안에서 띠를
/// 오른쪽 단추로 누르면 뜬다.
/// </summary>
/// <remarks>
/// 줄 차례와 말은 게임 화면에서 그대로 옮겼다. 줄마다 오른쪽에 <c>:ON</c>·<c>:OFF</c> 를
/// 붙이고, 누르면 그 칸이 띠에서 뜨거나 사라진다. 마지막 "취소" 는 회녹색 띠다
/// (<see cref="BandStyle.Alt"/>) — 게임이 나가기 줄을 그렇게 갈라 놓는다.
///
/// <b>아직 값이 없는 줄</b>은 게임 줄을 그대로 두되 흐리게 낸다(누르는 손이 안 달린다) —
/// 켜 봐야 띄울 것이 없기 때문이다.
/// </remarks>
internal static class CityInfoMenu
{
    /// <summary>
    /// 띠에 칸을 달아 둔 줄 이름. 이 글자가 곧 열쇠라 상단 띠 쪽과 한 자라도 어긋나면
    /// 그 줄이 조용히 흐려진다 — 그래서 양쪽이 이 이름을 함께 쓴다.
    /// </summary>
    public const string Date = "날짜", Coord = "위도·경도", Gold = "소지금",
                        Fame = "명성", City = "도시명", Fatigue = "피로도", Morale = "규칙",
                        Crew = "선원·대원", Stores = "물·식량", DaysLeft = "남은일수",
                        Wind = "풍향·풍속", Language = "언어", Rate = "시세",
                        Current = "해류", Vitality = "생명력";

    /// <summary>
    /// 게임 도시정보 창의 줄 차례. 화면에서 그대로 옮겼다.
    /// </summary>
    /// <remarks>
    /// 이름표 배열은 <c>0x005691D0</c>(열다섯 줄)이고 차례는 날짜 · 선원·대원 · 물·식량 ·
    /// 위도·경도 · 소지금 · 피로도 · 명성 · 도시명 · 언어 · 시세 · 남은일수 · <b>규칙</b> ·
    /// <b>풍향/풍속</b> · <b>해류</b> · <b>생명력</b> 이다.
    /// 「규칙」은 함대 값 <c>0x005B3954</c> 고(<c>0x0047DF4E</c>), 「생명력」은 제독 컨디션
    /// <c>0x005B60D8</c> 이다(<c>0x0047DFC9</c>).
    /// </remarks>
    public static readonly string[] Rows =
    [
        Date, Crew, Stores, Coord, Gold, Fatigue,
        Fame, City, Language, Rate, DaysLeft, Morale, Wind, Current, Vitality,
    ];

    private const string OnMark = ":ON", OffMark = ":OFF";

    /// <summary>
    /// 창 하나를 짓는다.
    /// </summary>
    /// <param name="state">
    /// 그 줄이 켜져 있는지. 아직 띄울 값이 없는 줄이면 null 이고, 그런 줄은 흐리게 낸다.
    /// </param>
    /// <param name="toggle">그 줄을 뒤집는다. 부른 쪽이 창을 다시 지어 글자를 새로 찍는다.</param>
    /// <param name="close">"취소" 를 눌렀을 때.</param>
    public static GameMenu Build(Func<string, bool?> state, Action<string> toggle, Action close)
    {
        int width = RowWidth();
        var items = new List<(string Text, Action? Run)>();
        foreach (var row in Rows)
        {
            bool? on = state(row);
            items.Add((Label(row, on == true, width), on == null ? null : () => toggle(row)));
        }
        items.Add(("취소", close));

        return new GameMenu("도시정보", null, [.. items]);
    }

    /// <summary>
    /// 이름과 <c>:ON</c>·<c>:OFF</c> 사이를 빈칸으로 메워 줄마다 값이 같은 자리에 오게 한다.
    /// </summary>
    /// <remarks>
    /// 띠 위의 글자는 가운데로 놓이므로, 줄마다 <b>전체 폭</b>만 같으면 이름은 왼쪽 끝에,
    /// 값은 오른쪽 끝에 나란히 선다. 게임 글꼴은 한글 16점 · ASCII 8점으로 폭이 8의 배수라
    /// 빈칸(8점)으로 딱 떨어지게 메울 수 있다. <c>:ON</c> 은 <c>:OFF</c> 보다 한 글자 짧으니
    /// 빈칸이 하나 더 들어가 폭이 그대로 맞는다.
    /// </remarks>
    private static string Label(string name, bool on, int width)
    {
        string value = on ? OnMark : OffMark;

        var font = GameUi.Font;
        int space = font?.TextWidth(" ") ?? 0;
        if (font == null || space <= 0) return $"{name}  {value}";   // 글꼴이 없으면 눈대중

        int gap = Math.Max(space, width - font.TextWidth(name) - font.TextWidth(value));
        return name + new string(' ', gap / space) + value;
    }

    /// <summary>가장 긴 이름에 빈칸 둘과 <c>:OFF</c> 를 더한 폭. 모든 줄이 이 폭에 맞는다.</summary>
    private static int RowWidth()
    {
        var font = GameUi.Font;
        if (font == null) return 0;

        int name = 0;
        foreach (var row in Rows) name = Math.Max(name, font.TextWidth(row));
        return name + font.TextWidth(" ") * 2 + font.TextWidth(OffMark);
    }
}
