using System.Runtime.CompilerServices;
using CdsHelper.Game.Local.Helpers;
using CdsHelper.Support.Local.Models;

namespace CdsHelper.Game.Engine.Town;

/// <summary>
/// 시설 주인(술집 · 여관 · 교역소 · 조선소 · 항구 · 조합 · 도서관 · 상점 · 왕궁 문지기 …)의 말이 <b>말 수준만큼 뭉개져</b> 들리는 규칙.
/// </summary>
/// <remarks>
/// 원본은 대사를 골라 뭉개지 않는다 — <b>얼굴 대사 창이 뭉갠다</b>. 얼굴 대사 창 <c>0x00478280</c> 은 화자가 제독이 아니면
/// 늘 <c>0x004780E0(화자, 제독)</c> 을 거치고, 시설 대사 창 <c>0x004692E0</c>(260곳에서 부른다)도 이 창을 지난다.
/// <code>
///   수준       = max(제독의 그 말 수준) — 주인이 하는 말들 가운데(0x00478050, 공유 수준)
///   주인의 말  = 그 도시 소유국의 말 + 그 건물이 가르치는 말(건물 표 +0x30 비트 13+i)    (0x00477EE0, 그 말이면 3)
///   뭉개기     = 2바이트 글자마다 rand(10) &lt; {10,8,4,0}[수준] 이면 ×                     (0x004252F0)
///   통역       = 부관(자리 0) · 통역(자리 3) 가운데 같은 셈이 제독보다 높은 사람이            (0x00468F70)
///                뭉개진 말 다음에 「[원문]라고 하는 것 같네요.」로 <b>원문 그대로</b> 옮긴다    (0x004692E0)
/// </code>
/// 우리 대사 창은 <see cref="UI.Views.ConfirmDialog"/> 하나로 모이므로 거기서 본다. 주인 얼굴은 늘
/// <see cref="Game.SpeakerFace"/> 에서 나오고, 그것이 낸 얼굴을 여기 적어 두어(<see cref="Mark"/>) 대사 창이 화자를 알아본다.
/// 무엇을 들려줄지(<see cref="Here"/>)는 건물에 들 때 도시 화면이 건다.
/// </remarks>
public static class KeeperTongue
{
    /// <summary>지금 든 건물에서 주인 말을 얼마나 알아듣는지.</summary>
    /// <param name="Level">제독의 수준(0~3).</param>
    /// <param name="Relayer">더 잘 알아듣는 부관 · 통역의 얼굴. 없으면 null.</param>
    /// <param name="RelayerLevel">그 사람의 수준.</param>
    public readonly record struct Listener(int Level, uint[]? Relayer, int RelayerLevel);

    /// <summary>지금 든 건물의 듣기 — 건물에 들 때 도시 화면이 건다. 도시를 나서면 null 이다.</summary>
    public static Func<Listener>? Here { get; set; }

    private static readonly ConditionalWeakTable<uint[], object> Keepers = new();
    private static readonly object Seen = new();

    /// <summary>시설 주인 얼굴로 적어 둔다 — 그 얼굴로 나가는 대사는 뭉개진다.</summary>
    public static uint[]? Mark(uint[]? face)
    {
        if (face != null) Keepers.AddOrUpdate(face, Seen);
        return face;
    }

    /// <summary>
    /// 주인 얼굴을 <b>안 뭉개는 얼굴</b>로 떠 준다 — 손님 이야기 · 술꾼 인사처럼 제 셈(<see cref="StrangerTalk"/>)으로 이미
    /// 뭉갠 말을 주인 얼굴로 낼 때 두 번 뭉개지지 않게.
    /// </summary>
    public static uint[]? Plain(uint[]? face) => face == null || !Keepers.TryGetValue(face, out _) ? face : [.. face];

    /// <summary>
    /// 그 얼굴로 내는 말을 들리는 대로 바꾼다. 주인 얼굴이 아니거나 다 알아들으면 null — 원문 그대로 낸다.
    /// </summary>
    /// <returns>뭉갠 말(번역한 뒤에 뭉갰다)과, 통역이 있으면 통역 얼굴 · 통역이 옮긴 말.</returns>
    public static (string Heard, uint[]? RelayFace, string? Relay)? Hear(string text, uint[]? face)
    {
        if (face == null || string.IsNullOrEmpty(text) || Here is not { } here || !Keepers.TryGetValue(face, out _))
            return null;
        var who = here();
        if (who.Level >= Skill.MaxLevel) return null;

        // 번역한 뒤에 뭉갠다 — 뭉갠 한국어는 번역 표에서 못 찾는다.
        string shown = Lang.T(text);
        string heard = StrangerTalk.Garble(shown, who.Level, Random.Shared, translated: true);
        // 통역도 제 수준만큼 뭉개 옮긴다 — 손님 이야기 · 마을 사람과 같다. 3 이라야 온전하다.
        string? relay = who.Relayer != null && who.RelayerLevel > who.Level
            ? StrangerTalk.Relay(StrangerTalk.Garble(shown, who.RelayerLevel, Random.Shared, translated: true), female: false)
            : null;
        return (heard, relay == null ? null : who.Relayer, relay);
    }
}
