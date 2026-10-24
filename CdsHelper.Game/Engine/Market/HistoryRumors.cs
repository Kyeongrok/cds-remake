using CdsHelper.Game.Local.Helpers;
using CdsHelper.Support.Local.Models;

namespace CdsHelper.Game.Engine.Market;

/// <summary>
/// 더해 넣은 역사 소문 — <c>exe-tables/history-rumors.json</c>(표 이름 「역사소문」). 원본에 없는 것이다(요청).
/// </summary>
/// <remarks>
/// 역사 대본(<see cref="CityHistory"/>)의 소문(<c>20 0A</c>)과 같은 소문 가게(<see cref="Player.AddRumor"/>)에 적는다 —
/// 술집 · 여관 무명 손님이 말한다. HIST_EV 는 게임 폴더 파일을 읽어 고칠 수 없으니 따로 둔다.
/// 소문은 <see cref="Player.RumorDays"/> 일이면 사라지므로, <see cref="Entry.Months"/> 동안은 달마다 보아
/// 그 도시에 그 말이 없으면 다시 적는다.
/// </remarks>
public static class HistoryRumors
{
    /// <summary>표 이름.</summary>
    private const string Name = "역사소문";

    /// <param name="Year">처음 도는 해.</param>
    /// <param name="Month">처음 도는 달.</param>
    /// <param name="Months">그 달부터 몇 달 동안 들리는지.</param>
    /// <param name="Cities">소문이 도는 도시 번호.</param>
    /// <param name="Text">손님이 하는 말. 일본어는 <c>lang/ja.json</c> 에 둔다.</param>
    public sealed record Entry(int Year, int Month, int Months, List<int> Cities, string Text);

    internal sealed record Snapshot(List<Entry> Rumors);

    private static List<Entry>? _all;

    private static List<Entry> All => _all ??= TableCache.Read<Snapshot>(Name)?.Data.Rumors ?? [];

    /// <summary>그 달에 들려야 할 소문을 적는다 — 달이 바뀔 때 역사 대본 다음에 돈다.</summary>
    public static void RunMonth(Player player, DateTime when)
    {
        int key = CityHistory.MonthKey(when.Year, when.Month);
        foreach (var rumor in All)
        {
            int from = CityHistory.MonthKey(rumor.Year, rumor.Month);
            if (key < from || key >= from + Math.Max(1, rumor.Months)) continue;
            foreach (int city in rumor.Cities)
                if (!player.Rumors.Any(r => r.City == city && r.Text == rumor.Text
                                            && (when - r.Added).TotalDays < Player.RumorDays))
                    player.AddRumor(city, rumor.Text, when);
        }
    }
}
