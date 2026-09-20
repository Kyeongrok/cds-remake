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
///                      「소지품을 앞으로 %d개 삭제해 주십시오」(0x00544A00) → 「삭제 아이템의 선택」(0x005449D8)
///                      을 16 이하가 될 때까지 되풀이한다 — 목록에 <b>새 것도 들어 있어</b> 그것을 버려도 된다
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
            while (all.Count > Support.Local.Models.Player.MaxItems)
            {
                GameDialog.Show(owner, $"소지품을 앞으로 {all.Count - Support.Local.Models.Player.MaxItems}개 삭제해 주십시오",
                                "소지품 제한");
                var names = all.Select(id => game.Items?.Find(id)?.Name ?? $"아이템 {id}").ToList();
                int at = ChoiceDialog.Pick(owner, "삭제 아이템의 선택", names);
                if (at >= 0 && at < all.Count) all.RemoveAt(at);
            }
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
            while (all.Count > Support.Local.Models.Player.MaxItems)
            {
                GameDialog.Show(owner, $"소지품을 앞으로 {all.Count - Support.Local.Models.Player.MaxItems}개 삭제해 주십시오",
                                "소지품 제한");
                var names = all.Select(id => game.Items?.Find(id)?.Name ?? $"아이템 {id}").ToList();
                int at = ChoiceDialog.Ask(owner, "삭제 아이템의 선택", names);
                if (at < 0 || at >= all.Count) return false;
                all.RemoveAt(at);
            }
        }
        player.ReplaceBelongings(all, player.Stored.ToList());
        return true;
    }
}
