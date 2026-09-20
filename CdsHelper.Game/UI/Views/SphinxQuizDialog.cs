using System.Windows;
using CdsHelper.Support.Local.Models;

namespace CdsHelper.Game.UI.Views;

/// <summary>
/// 미니 게임 「스핑크스 퀴즈」 — 게임처럼 <b>알림 창과 고르기 창만으로</b> 흘러간다.
/// </summary>
/// <remarks>
/// 게임의 <c>0x0047BFE0</c> 이고, 규칙은 <see cref="SphinxQuiz"/> 에 모아 두었다.
/// 이 놀이는 따로 판을 안 세운다 — <c>0x0049E3E0</c>(알림)과 <c>0x004878A0</c>(고르기)만
/// 번갈아 부른다. 그래서 여기서도 <see cref="NoticeDialog"/> 와
/// <see cref="MapPointDialog"/> 로만 낸다.
/// </remarks>
internal static class SphinxQuizDialog
{
    /// <summary>문제 글(<c>0x0056EFC8</c> · <c>0x0056F078</c> · <c>0x0056F1A8</c>).</summary>
    private static string Ask(SphinxQuiz.Riddled it, int step) => step switch
    {
        0 => "〈스핑크스〉 그럼 그대에게 묻겠다." + Environment.NewLine +
             $"다리가 4개 있는 괴물과 2개 있는 괴물이 {it.Beasts}마리 있다. " +
             $"그 다리는 합쳐서 {it.Legs}개가 있다." + Environment.NewLine +
             "그러면 다리가 4개있는 괴물은 몇 마린가?",

        1 => "다시 그대에게 묻겠다." + Environment.NewLine +
             $"다리가 4개와 2개 있는 괴물의 다리를 합쳐서 {it.Legs}개가 있다. " +
             "세월이 지나서 다리 4개의 괴물은 모두 다리가 2개가 되고, " +
             $"2개의 다리를 가지고 있는 것 중 {it.Grown}마리는 3개의 다리가 되었다." +
             Environment.NewLine +
             $"괴물들의 다리를 모두 합치니 {it.Aged}개가 되었다. " +
             "처음에 다리가 4개 있었던 괴물은 몇 마리였느냐?",

        _ => "〈스핑크스〉 마지막으로 그대에게 묻겠다." + Environment.NewLine +
             $"다리가 4개와 2개 있는 괴물의 다리를 합쳐 {it.Legs}개가 있다. " +
             "세월이 지나서 4개의 다리를 가진 괴물은 모두 다리가 2개로 성장하고, " +
             $"다리가 2개 있는 괴물중 {it.Grown}마리는 다리가 3개가 되었다. " +
             Environment.NewLine +
             "게다가 다리가 4개였던 괴물의 2배가 되는 4개의 다리를 가지는 괴물이 " +
             $"새로이 태어났다. 괴물들의 다리를 다 합쳐 {it.Born}개가 있다. " +
             Environment.NewLine +
             "그러면 처음에 다리가 4개 있었던 괴물은 몇 마리였느냐?",
    };

    /// <summary>
    /// 한 판 한다. 수수께끼를 틀리면 그 자리에서 쫓겨난다.
    /// </summary>
    /// <returns>
    /// 끝까지 맞혀 칭송을 받았는지. 게임 <c>0x0047BFE0</c> 은 이때 1 을 내고, 발견 대본의
    /// 미니게임 명령(<c>0E 04 01 00</c>)은 1 일 때만 이긴 것으로 친다(<c>0x00408D5E</c>).
    /// </returns>
    public static bool Play(Window owner, Random rng)
    {
        NoticeDialog.Show(owner,
            "〈스핑크스〉 아침에는 4개의 다리, 낮에는 2개의 다리." + Environment.NewLine +
            "밤에는 3개의 다리로 걷는 괴물은?", "스핑크스");

        // 고르는 창은 <b>제목이 없다</b> — 원본이 0x004878A0 에 제목 인자로 0 을 넘긴다.
        int said = MapPointDialog.Ask(owner, SphinxQuiz.Riddle, "");
        if (said < 0) return false;
        if (said != SphinxQuiz.RiddleAnswer) { Away(owner); return false; }

        var quiz = new SphinxQuiz(rng);
        var lines = Enumerable.Range(1, SphinxQuiz.Choices).Select(n => $"{n}마리").ToList();
        lines.Add("문제를 본다");

        while (true)
        {
            NoticeDialog.Show(owner, Ask(quiz.Now, quiz.Step), "스핑크스");

            int pick = MapPointDialog.Ask(owner, lines, "");
            var done = quiz.Answer(pick);

            if (done == null) continue;
            if (done.Value)
            {
                NoticeDialog.Show(owner, "〈스핑크스〉 그대의 예지를 칭송하리라.", "스핑크스");
                return true;
            }
            Away(owner);
            return false;
        }
    }

    /// <summary><c>0x0056EF80</c> — 틀렸을 때.</summary>
    private static void Away(Window owner) =>
        NoticeDialog.Show(owner, "〈스핑크스〉 성스러운 땅에 합당치 않는 자여! 물러가라!",
                          "스핑크스");
}
