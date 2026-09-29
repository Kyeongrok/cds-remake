using System.Windows;

namespace CdsHelper.Game.UI.Views;

/// <summary>
/// 아이템 여럿을 한꺼번에 들인다 — 열여섯 칸이 넘치면 <b>물릴 수 없는</b> 버리기 창을 돌린다.
/// </summary>
/// <remarks>
/// 게임의 <c>0x004B1710(목록, n, 0)</c> 이다. 항구 발표·후원자 보고·매수한 증거품이 이것을 쓴다.
/// <code>
///   지금 + 새 것 ≤ 16   그냥 넣는다
///   넘치면             「더 이상 가질 수 없습니다! 소지품을 삭제해 주십시오」(0x00544A30)
///                      → 「삭제 아이템의 선택」(0x005449D8) 여럿 고르기 창 하나 — 결정했는데 16 을 넘으면
///                      「소지품을 앞으로 %d개 삭제해 주십시오」(0x00544A00)를 내고 그 창으로 돌아간다.
///                      목록에 <b>새 것도 들어 있어</b> 그것을 버려도 된다
///   끝나면             16칸을 비우고 남긴 차례대로 다시 채운다
/// </code>
/// 셋째 인자가 0 이라 중단 단추가 없다 — 창을 닫아도 다시 뜬다.
/// </remarks>
public static class ItemGain
{
    public static void AddForced(Window owner, Engine.Game game, IReadOnlyList<int> items)
    {
        if (items.Count == 0) return;
        var player = game.Player;
        var all = player.Items.Concat(items).ToList();

        if (all.Count > Support.Local.Models.Player.MaxItems)
        {
            GameDialog.Show(owner, "더 이상 가질 수 없습니다! 소지품을 삭제해 주십시오");
            all = Drop(owner, game, all, cancellable: false)!;
        }
        player.ReplaceBelongings(all, player.Stored.ToList());   // 보관함은 그대로 — 비우기 전에 떠 둔다
    }

    /// <summary>
    /// 아이템 하나를 들이되 <b>물릴 수 있다</b> — <c>0x004B1710(목록, 1, 1)</c>. 넘치면 버리기 창에 「취소」가 붙고,
    /// 무르면 아무것도 안 바뀌고 false 다.
    /// </summary>
    public static bool TryAdd(Window owner, Engine.Game game, int item)
    {
        var player = game.Player;
        var all = player.Items.Append(item).ToList();

        if (all.Count > Support.Local.Models.Player.MaxItems)
        {
            GameDialog.Show(owner, "더 이상 가질 수 없습니다! 소지품을 삭제해 주십시오");
            if (Drop(owner, game, all, cancellable: true) is not { } kept) return false;
            all = kept;
        }
        player.ReplaceBelongings(all, player.Stored.ToList());
        return true;
    }

    /// <summary>
    /// 「삭제 아이템의 선택」 — 버릴 것을 <b>한 창에서 여럿</b> 켜 두고 결정한다(<c>0x004B13F0</c>).
    /// 남긴 목록을 낸다. 물렸으면 null 이다.
    /// </summary>
    /// <remarks>
    /// 원본은 목록 창(<c>0x004B14A2</c>, 띠 목록 벌 <c>0x004C3F88</c>) 하나를 띄워 두고 결정마다 센다.
    /// <code>
    ///   004B15B3  남는 수 = 모두 − 켠 줄 수
    ///   004B15BA  남는 수 &gt; 16 이면 「소지품을 앞으로 %d개 삭제해 주십시오」(제목 「소지품 제한」)를 내고
    ///             <b>같은 창으로 돌아간다</b> — 켜 둔 줄은 그대로다
    ///   004B15D9  중단은 셋째 인자가 있을 때만 먹는다. 없으면 창이 그대로 남는다
    /// </code>
    /// 예전에는 한 줄짜리 차림 창을 한 개씩 되풀이해 띄우고 그때마다 「앞으로 %d개」를 먼저 물었다.
    /// </remarks>
    private static List<int>? Drop(Window owner, Engine.Game game, List<int> all, bool cancellable)
    {
        var names = all.Select(id => game.Items?.Find(id)?.Name ?? $"아이템 {id}").ToList();
        IReadOnlyList<int> picked = [];
        while (true)
        {
            // 주인 창이 닫혔으면(게임 창을 닫는 중 따위) 더 띄울 데가 없다 — 닫힌 창을 주인으로 새 창을 지으면
            // 터진다. 물릴 수 있으면 물리고, 아니면 앞에서부터 열여섯만 남긴다(새로 든 것이 뒤에 붙어 있다).
            if (Gone(owner))
                return cancellable ? null : [.. all.Take(Support.Local.Models.Player.MaxItems)];

            var got = HintListDialog.PickMany(owner, names, "삭제 아이템의 선택", [.. picked]);
            if (got.Count == 0)
            {
                if (cancellable) return null;
                continue;                                   // 원본은 창이 안 닫힌다 — 다시 띄운다
            }
            picked = got;
            int over = all.Count - got.Count - Support.Local.Models.Player.MaxItems;
            if (over > 0)
            {
                GameDialog.Show(owner, $"소지품을 앞으로 {over}개 삭제해 주십시오", "소지품 제한");
                continue;
            }
            return all.Where((_, i) => !got.Contains(i)).ToList();
        }
    }

    /// <summary>창이 이미 닫혔는지 — 닫힌 창은 제 HWND 원천이 없다.</summary>
    private static bool Gone(Window window) => PresentationSource.FromVisual(window) == null;
}
