using CdsHelper.Game.Engine.Disev;
using CdsHelper.Support.Local.Models;

namespace CdsHelper.Game.Engine.Discovery;

/// <summary>
/// 초심자(EASY) 캐릭터의 개인 이야기(이야기0·이야기1)를 맡는다 — 지금 틀 파트를 찾아 주고, 튼 뒤 진행을 적는다.
/// </summary>
/// <remarks>
/// 게임은 이야기 책마다 <b>진행 카운터 하나</b>만 둔다(이야기 관리자 <c>0x00629898</c> 의 첫 칸).
/// 그 값이 곧 <b>돌릴 파트 번호</b>다 — 사건이 나면 <c>0x0040D0B0(맥락, 책, 카운터)</c> 가 그 번호의 파트
/// 하나만 열어 슬롯 조건을 잰다. 다른 파트는 아예 안 본다.
/// <code>
///   06      카운터 += 1   (0x00408B2F → 0x004AB49E)
///   04      카운터 = −1   (0x004089EC → 0x004AB4A6) — 책이 끝나 더는 사건이 안 난다(0x004AB5A3)
/// </code>
/// 파트 머리의 <c>Step</c> 칸은 게임이 안 읽는다. 예전에는 <c>Step == 0</c> 인 파트마다 딴 장으로 쳐
/// 동시에 후보로 올렸더니, 파트 10(「코론이라는 제노바인이…」)이 판을 열자마자 떴다.
/// </remarks>
public static class StoryLog
{
    /// <summary>
    /// 지금 자리에서 열리는 이야기 파트 — 카운터가 가리키는 그 파트의 조건이 맞을 때만. 없으면 null.
    /// </summary>
    /// <param name="building">지금 들어온 건물 코드. 건물 밖(도시만 들어왔을 때)이면 -1.</param>
    public static int? NextPart(Player player, Game game, int building)
    {
        if (player.ActiveStoryBook is not { } cache) return null;
        if (player.IsStoryArcClosed(cache)) return null;
        if (DisevRunner.Open(game.Directory, cache) is not { } book) return null;

        int part = PartOf(player, cache);
        if (part < 0 || part >= book.Count) return null;
        return DisevRunner.IsEligible(game, cache, part, building) ? part : null;
    }

    /// <summary>
    /// 튼 파트의 뒤처리 — 대본이 <c>06</c> 을 겪은 만큼 카운터를 올리고, <c>04</c> 를 겪었으면 책을 닫는다.
    /// </summary>
    /// <remarks><see cref="DisevRunner.Run(System.Windows.Window, Game, string, int, int)"/> 바로 뒤에 불러야 한다.</remarks>
    public static void Advance(Player player, Game game, string cache, int partIndex)
    {
        if (DisevRunner.LastStoryArcCompleted)
        {
            player.CloseStoryArc(cache);
            return;
        }
        if (DisevRunner.LastStepsAdvanced > 0)
            player.SetStoryStep(cache, partIndex + DisevRunner.LastStepsAdvanced);
    }

    /// <summary>
    /// 그 책의 카운터. 장(章)마다 따로 적던 예전 세이브(<c>"이야기1:0"</c>)는 첫 장 값을 그대로 받는다 —
    /// 첫 장은 파트 0 부터라 그 진행도가 곧 파트 번호다.
    /// </summary>
    private static int PartOf(Player player, string cache)
    {
        if (player.StoryProgress.TryGetValue(cache, out int part)) return part;
        return player.StoryStepOf($"{cache}:0");
    }
}
