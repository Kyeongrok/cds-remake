using CdsHelper.Game.Engine.Disev;
using CdsHelper.Game.Local.Helpers;
using CdsHelper.Support.Local.Models;

namespace CdsHelper.Game.Engine.Discovery;

/// <summary>
/// <b>고문서를 읽는다</b> — 힌트가 걸린 아이템을 소지품 정보에서 들여다보면 그 힌트를 읽어 볼 수 있다.
/// </summary>
/// <remarks>
/// 게임의 <c>0x0046E7D0</c>(아이템 창)이 둘째 인자가 1 일 때만 한다 — 그렇게 부르는 곳은 소지품 정보
/// (<c>0x0044D1C5</c> · <c>0x0044D243</c>)뿐이고, 시장·대본에서 뜨는 아이템 창은 0 이다.
/// 힌트가 걸린 아이템은 아홉 개다(아이템 표 <c>+0x18</c>, <c>0x00465800</c>).
/// <code>
///   로제타석 30 · 사자의 책 82 · 슈메르의 점토판 59 · 그림성경 174 · 포포르·부흐 72
///   트로아노 고사본 73 · 페레시아누스 고사본 154 · 잉카의 키프 45 · 사해사본 60
/// </code>
/// 이미 아는 힌트면(<c>[힌트+4] &amp; 3</c>, <c>0x0046E8EC</c>) 아무 일 없이 아이템 창만 뜬다. 모르면 차례로 본다.
/// <code>
///   기능(힌트 +0x20)이 있고 함대에서 가장 잘하는 이가 자리(+0x28)에 못 미침
///        → 「뭔가 쓰여져 있으나 내용을 이해할 수 없다」                 0x005710F8
///   언어(힌트 +0x24)가 있고 가장 잘하는 이가 자리에 못 미침
///        → 「%s%s 뭔가 쓰여져 있으나 읽을 수가 없다」(언어+로/으로)      0x00571128
///   먼저 찾아 둘 발견물(0x0042CCC0)이 남음
///        → 「뭔가 쓰여져 있으나 그 의미를 알 수 없다」                   0x00571150
///   다 되면 「뭔가 쓰여져 있다···」(0x00571178) → 힌트 창 → 그 힌트를 안다(+4 |= 1, 0x0046EB58)
/// </code>
/// 못 읽는 세 갈래는 아이템 창을 띄운 채 말을 내고 <b>그대로 창을 닫는다</b>. 읽으면 힌트 창을 본 뒤에
/// 아이템 창이 여느 때처럼 뜬다. 띠에 「%s의 지식이 필요합니다」도 함께 나가지만(<c>0x0046E9BB</c>) 그것은
/// 안 보이는 힌트 패널 글이라 옮기지 않는다.
/// </remarks>
public static class ItemHintReading
{
    /// <summary>읽어 본 결과.</summary>
    public enum Outcome
    {
        /// <summary>걸린 힌트가 없거나 이미 안다 — 아이템 창만 뜬다.</summary>
        None,
        /// <summary>못 읽는다 — <see cref="Check"/> 가 낸 말을 띄우고 창을 닫는다.</summary>
        Failed,
        /// <summary>읽었다 — 「뭔가 쓰여져 있다···」 뒤에 힌트 창을 편다.</summary>
        Read,
    }

    /// <summary>다 읽었을 때의 말(<c>0x00571178</c>).</summary>
    public const string ReadWord = "뭔가 쓰여져 있다···";

    /// <summary>언어 조사 갈래 — 「로/으로」(<c>0x004281B0(언어, 10)</c>).</summary>
    private const int ToJosa = 10;

    /// <summary>
    /// 그 아이템에 걸린 힌트를 읽을 수 있는지 본다. 못 읽으면 <paramref name="word"/> 에 까닭 말을 낸다.
    /// </summary>
    public static Outcome Check(Game game, ItemTable.Record item, out int hint, out string word)
    {
        hint = item.Hint;
        word = "";
        var player = game.Player;
        if (hint < 0 || player.Hints.Contains(hint) || game.Books is not { } books) return Outcome.None;

        var need = books.NeedFor(hint);
        var names = game.Buildings;
        var mates = MateRows(player);

        if (need.Skill >= 0 && names != null && need.Skill < names.SkillNames.Count)
        {
            int best = player.LevelOf(names.SkillNames[need.Skill]);
            foreach (var mate in mates)
                if (need.Skill < mate.Skills.Length) best = Math.Max(best, mate.Skills[need.Skill]);
            if (best < need.Level)
            {
                word = "뭔가 쓰여져 있으나 내용을 이해할 수 없다";
                return Outcome.Failed;
            }
        }

        if (need.Language >= 0 && names != null && need.Language < names.LanguageNames.Count)
        {
            string language = names.LanguageNames[need.Language];
            int best = player.TongueOf(language);
            foreach (var mate in mates)
                if (need.Language < mate.Languages.Length) best = Math.Max(best, mate.Languages[need.Language]);
            if (best < need.Level)
            {
                word = $"{language}{NameToken.Of(language, ToJosa)} 뭔가 쓰여져 있으나 읽을 수가 없다";
                return Outcome.Failed;
            }
        }

        if (need.Parents is { } parents && parents.Any(id => !player.HasFound(id)))
        {
            word = "뭔가 쓰여져 있으나 그 의미를 알 수 없다";
            return Outcome.Failed;
        }

        return Outcome.Read;
    }

    /// <summary>
    /// 함대에 탄 부하들의 인물 표 줄 — 게임은 제독과 부하 자리 0~3 을 모두 훑어 가장 잘하는 이를 쓴다
    /// (기능 <c>0x0047CCA0</c> · 언어 <c>0x0047CD20</c>, 인자 0·1·2·3).
    /// </summary>
    private static List<PersonTable.Row> MateRows(Player player)
    {
        var people = PersonTable.Open().People;
        return [.. player.Mates.Where(n => n.Length > 0)
                               .Select(n => people.FirstOrDefault(r => r.Name == n))
                               .OfType<PersonTable.Row>()];
    }
}
