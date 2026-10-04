using System.Windows;
using CdsHelper.Game.Engine.Discovery;

namespace CdsHelper.Game.Engine.Disev;

/// <summary>
/// 대사 사건 — 엔진이 마을 사람 대사를 내기 직전에 대본에 「대사 사건(번호)」을 올린다. 대본(전역 이야기 「마을대사」)에
/// 그 번호를 조건으로 하는 슬롯이 있으면 <b>대본이 말하고</b> 엔진 대사는 건너뛴다. 없으면 엔진이 제 대사를 낸다.
/// </summary>
/// <remarks>
/// <code>
///   조건 명령   70 10 [번호 u16]  — 원본에 없는 꼴. JSON 은 {"Call": "SpeechIs", "Args": {"Line": 번호, "LineName": "이름"}}
///   자리표      대사 속 &lt;금액&gt; · &lt;개월&gt; 따위 — 엔진이 그때 값으로 넘긴다(<see cref="Line.Vars"/>)
///   물음 대사   대본이 YES/NO 를 묻고 4D(결과 1)로 끝나면 「아니오」, 그 밖(4C · FF)이면 「예」로 받는다
/// </code>
/// 번호는 바이트에 들어가는 몫이고, 사람은 <see cref="Line.Key"/>(「교회.기부금물음」)로 본다 — 편집기 풀이도 번호와 이름을 같이 낸다.
/// 엔진 대사(<see cref="Line.Text"/>)는 대본이 그 줄을 안 맡았을 때 그대로 나오는 몫이다.
/// </remarks>
public static class TownLines
{
    /// <summary>대사 한 줄 — 번호 · 이름 · 엔진 대사 · 물음인지 · 넘기는 자리표 이름들.</summary>
    public sealed record Line(int Id, string Key, string Text, bool Ask = false, params string[] Vars);

    /// <summary>대사 표. 번호는 한 번 정하면 바꾸지 않는다 — 대본이 번호로 가리킨다.</summary>
    public static readonly IReadOnlyList<Line> All =
    [
        // ── 교회 (수련 · 후원자) ──────────────────────────────────────────
        new(1, "교회.수련불가", "죄송하지만, 여기서는 수련이 불가능합니다."),
        new(2, "교회.수련인사", "주의 배움의 터전에 잘 오셨습니다. 어떤 학문, 기능을 배우고 싶습니까?"),
        new(3, "교회.수련배웅", "용건이 있을 경우에는 언제든지 와 주십시오."),
        new(4, "교회.숙달", "당신은 벌써 숙달해 있습니다. 제가 가르쳐 드릴 것은 아무것도 없습니다."),
        new(5, "교회.기부금물음", "기부금으로 <금액>닢 받겠습니다. 습득하는데는 <개월>개월 정도 걸립니다. 좋습니까?",
            Ask: true, "금액", "개월", "기능"),
        new(6, "교회.기부금부족", "안됐지만 기부금이 모자랍니다. 다음 기회에 와 주십시오."),
        // 교회 후원자를 설득하려는데 명성이 모자랄 때 교회 사람이 돌려보내는 말(0x004AE1F0).
        new(7, "교회.명성부족", "<후원자>님은 바쁘셔서 만나실 수 없습니다.", Ask: false, "후원자"),

        // ── 교역소 ───────────────────────────────────────────────────────
        // 사고팔 것이 둘 다 없을 때(0x00532928).
        new(8, "교역소.팔것없음", "미안하지만, 자네에게 팔 물건은 아무것도 없네."),
        // 원본에 없는 말 — 이 고장 물건이 판매 깃발에 걸려 있을 때 까닭을 일러 준다. <교역품> 은 「커피, 상아, 노예」 꼴.
        new(9, "교역소.미발견", "이 고장의 <교역품><은는> 아직 세상에 알려지지 않아서 말이지.", Ask: false, "교역품", "은는"),
        // 원본에 없는 알림 — 발견 대본이 판매 깃발을 켠 뒤(01 15) 대본이 끝나면 품목마다 한 번.
        new(10, "알림.교역품개시", "이제 교역소에서 [<교역품>]<을를> 다룹니다", Ask: false, "교역품", "을를"),
    ];

    /// <summary>번호로 찾는다.</summary>
    public static Line? Find(int id) => All.FirstOrDefault(l => l.Id == id);

    /// <summary>이름으로 찾는다.</summary>
    public static Line? Find(string key) => All.FirstOrDefault(l => l.Key == key);

    /// <summary>
    /// 그 대사 사건을 대본에 올린다. 맡은 대본이 있으면 돌리고 결과(예 = true)를 낸다. 맡은 대본이 없으면 null.
    /// </summary>
    public static bool? Run(Window owner, Game game, string key, params (string Name, object Value)[] vars)
    {
        if (Find(key) is not { } line) return null;
        var values = vars.ToDictionary(v => v.Name, v => v.Value?.ToString() ?? "");
        var ev = DisevEvent.Speech(line.Id, values);
        foreach (string book in StoryLog.GlobalBooks())
        {
            if (StoryLog.NextPart(game.Player, game, ev, book) is not { } part) continue;
            DisevRunner.Run(owner, game, book, part, ev);
            StoryLog.Advance(game.Player, game, book, part);
            return DisevRunner.LastResult != 1;
        }
        return null;
    }

    /// <summary>대사 한 줄 — 대본이 맡으면 대본이, 아니면 <paramref name="fallback"/>(엔진 대사)이 말한다.</summary>
    public static void Say(Window owner, Game game, string key, Action fallback, params (string Name, object Value)[] vars)
    {
        if (Run(owner, game, key, vars) == null) fallback();
    }

    /// <summary>물음 한 줄 — 대본이 맡으면 그 답을, 아니면 <paramref name="fallback"/>(엔진 물음)의 답을 낸다.</summary>
    public static bool Ask(Window owner, Game game, string key, Func<bool> fallback, params (string Name, object Value)[] vars) =>
        Run(owner, game, key, vars) ?? fallback();
}
