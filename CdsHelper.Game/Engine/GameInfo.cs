using CdsHelper.Game.Local.Helpers;
using CdsHelper.Support.Local.Models;

namespace CdsHelper.Game.Engine;

/// <summary>
/// 정보 판에 채울 것들 — 발견물 이름 · 힌트 이름 · 계약 판.
/// </summary>
/// <remarks>
/// 같은 판을 <b>바다에서도 도시에서도</b> 낸다(지도 창의 "정보", 도시 커맨드 창의
/// "○○ 정보"). 게임도 한 창이다. 그래서 채울 것을 여기 한 벌만 두고 두 화면이 갖다 쓴다 —
/// 예전에는 발견물 이름 짓는 코드가 두 곳, 힌트 이름이 두 곳에 따로 있었다.
///
/// 창을 <b>어떻게 띄우는지</b>는 화면이 정한다. 계약이 없을 때 지도 창은 한 줄로 물리고
/// 도시 창은 빈 판을 내는데, 그 차이는 그대로 두었다.
/// </remarks>
public static class GameInfo
{
    /// <summary>
    /// 발견하고 <b>아직 보고·발표하지 않은 것</b>의 이름. 소지품 판의 발견물 칸에 쓴다.
    /// </summary>
    /// <remarks>
    /// 게임(<c>0x0044CC79</c>)은 발견물 274칸을 차례로 훑어 <b>발견자 칸(+0x18)이 차 있고 보고자 칸(+0x78)이
    /// 비어 있는</b> 것만 담는다. 보고·발표(<c>0x0047E680</c>)가 보고자 칸을 채우므로, 한 번 보고했거나
    /// 발표한 것은 이 칸에서 빠진다. 우리는 그 칸을 <see cref="Support.Local.Models.Player.Announced"/> 로 든다.
    /// 볼트 <c>88.분석-발견물 일람 거르기(보고·발표)</c>.
    /// </remarks>
    /// <summary>
    /// <b>아직 보고·발표하지 않은 발견물의 아이템</b> — 소지품 일람에 덧붙는다(<c>0x0044CBB3</c>).
    /// </summary>
    /// <remarks>
    /// 게임은 발견물 아이템(표 <c>+0x30</c>)을 발견할 때 소지품 16칸에 넣지 않는다. 찾았고(칸 0) 아직
    /// 알리지 않은(<c>+0x16 &amp; 0x80</c> 이 꺼진) 동안만 일람에 비쳐 보이고, 발표할 때 소지품으로 들어온다
    /// (<c>0x0047EA5F</c>). 「가졌는가」 조건·장비 효과·시장·선물은 이것을 안 센다 — 실제 16칸만 본다.
    /// </remarks>
    public static List<int> VirtualItems(Game game)
    {
        var table = game.Discoveries?.Table;
        var player = game.Player;
        if (table == null) return [];
        return [.. player.Discoveries.Where(id => !player.HasAnnounced(id)).Order()
                   .Select(id => table.Find(id)).Where(r => r is { GivesItem: true })
                   .Select(r => r!.Value.ItemId)];
    }

    public static List<string> DiscoveryNames(Game game)
    {
        var table = game.Discoveries?.Table;
        var player = game.Player;
        // 표에 없는 번호(대본 해석기가 잘못 적은 것)는 뺀다.
        return [.. player.Discoveries.Where(id => !player.HasAnnounced(id))
                   .Where(id => table == null || table.Find(id) != null).Order()
                   .Select(id => table?.Find(id)?.Name ?? $"발견물 {id}")];
    }

    /// <summary>
    /// 가지고 있는 힌트의 이름. 표 → DB → 번호 차례로 물러선다.
    /// </summary>
    /// <remarks>
    /// <b>찾아낸 것의 힌트는 뺀다.</b> 힌트는 「아직 못 찾은 것을 어디서 찾나」를 적어 둔
    /// 쪽지라, 그 발견물을 이미 찾았으면 목록에 남을 까닭이 없다. 힌트와 발견물은
    /// 번호로 짝을 맺는다(힌트의 <c>Discovery</c> 와 발견물의 <c>Hint</c> 가 같은 값).
    /// </remarks>
    public static List<string> HintNames(Game game)
    {
        var table = game.Discoveries?.Table;
        var hints = game.Hints;

        return [.. game.Player.Hints.Order()
                   .Where(id => !Found(game, table, hints, id))
                   .Select(id => HintLabel(game, id))];
    }

    /// <summary>
    /// 힌트 이름 뒤에 <b>갈래</b>를 괄호로 붙인다 — 「몽생미셸 (종교)」.
    /// </summary>
    /// <remarks>
    /// 갈래는 힌트 줄의 <c>+0x0C</c> 고 이름표는 <c>0x00560C60</c> 여덟이다(지리·역사·보물·
    /// 종교·교역품·미신·생물·민족). <b>후원자마다 좋아하는 갈래가 달라</b> 어느 갈래인지가
    /// 설득에 그대로 드는데, 목록에 이름만 있으면 그때마다 힌트 정보를 다시 펴야 했다.
    /// 표를 못 읽었으면 이름만 낸다.
    ///
    /// <b>원본은 이름만 적지만(<c>0x00476020</c>) 이것은 일부러 둔 것이다</b> — 원본에 없다고
    /// 뺐다가 되돌렸다. 다시 빼지 말 것.
    /// </remarks>
    public static string HintLabel(Game game, int id)
    {
        string name = game.HintName(id);
        if (game.Hints is not { } hints || hints.Find(id) is not { } row) return name;

        string category = hints.CategoryOf(row.Category);
        return category.Length > 0 ? $"{name} ({category})" : name;
    }

    /// <summary>
    /// 부하의 인물 판에 적을 직업·별자리·혈액형·국적 — 인물 표에서 이름으로 번호를 찾아 밑표에서 꺼낸다.
    /// 술집 인물 판(TavernMenu.SheetOf)과 같은 셈이다. 못 찾으면 빈 칸이다.
    /// </summary>
    internal static UI.Views.PersonInfoDialog.HireSheet SheetOf(Game game, Support.Local.Models.Player.MateInfo who)
    {
        int id = game.World?.People.FirstOrDefault(r => r.Name == who.Name)?.Id ?? -1;
        if (id < 0 || game.PersonTemplates?.Find(id) is not { } t)
            return new(who.Name, who.Body, who.Mind, who.Might, who.Charm, who.Age, "", "", "", "");

        string job = t.JobName.Length > 0 ? t.JobName : Support.Local.Models.Job.Of(t.Job).Name;
        string nation = game.Nations?.Find(t.Nation) is { } nat ? nat.Name : "";
        return new(who.Name, who.Body, who.Mind, who.Might, who.Charm, who.Age, job, t.Zodiac, t.BloodName, nation);
    }

    /// <summary>교역품 한 칸의 이름 — 「%s산」 뒤에 품목 이름을 <b>붙여</b> 쓴다(「세빌리아산총」, <c>0x0042E310</c>). 산지를 모르면 이름만이다.</summary>
    public static string CargoLabel(Game game, Support.Local.Models.Player.Cargo cargo)
    {
        string name = game.Goods?.Find(cargo.Kind)?.Name ?? $"교역품 {cargo.Kind}";
        return cargo.Origin >= 0 ? $"{game.CityName(cargo.Origin)}산{name}" : name;
    }

    /// <summary>그 힌트가 가리키는 발견물을 이미 찾았는지.</summary>
    private static bool Found(Game game, DiscoveryTable? table,
                              HintTable? hints, int hint)
    {
        if (table == null || hints?.Find(hint) is not { } row) return false;

        foreach (var found in table.Discoveries)
            if (found.Hint == row.Discovery && game.Player.HasFound(found.Id)) return true;

        return false;
    }

    /// <summary>
    /// 계약 정보 판에 채울 것.
    /// </summary>
    /// <param name="Contract">맺고 있는 계약. 없으면 null 이다.</param>
    /// <param name="HintName">맡은 힌트의 이름.</param>
    /// <param name="Found">계약 중에 찾아낸 것들.</param>
    /// <param name="Evidence">
    /// 그 가운데 <b>아직 지니고 있는</b> 물건 — 팔아 버렸으면 내밀 증거가 없다.
    /// </param>
    public readonly record struct ContractSheet(Contract? Contract, string HintName,
                                                List<string> Found, List<string> Evidence);

    /// <summary>계약 정보 판을 채운다.</summary>
    public static ContractSheet ContractSheetOf(Game game)
    {
        if (game.Player.Contract is not { } contract) return new(null, "", [], []);

        var table = game.Discoveries?.Table;
        var items = game.Items;

        // 게임은 <b>계약한 유적의 것만</b> 적는다(0x0047F6BA: 0x00493E60 으로 계약 힌트를 얻어
        // 0x0046AE90 · 0x0046B030 으로 모은다). 오다가 딴 것을 발견해도 여기 안 뜬다 —
        // 그것은 항구 「발표」 몫이다(Harbor.Announceable 이 계약 목표만 뺀다).
        int target = game.Hints?.Find(contract.Hint) is { } hint ? hint.Discovery : -1;

        var found = new List<string>();
        var evidence = new List<string>();
        foreach (int id in contract.Found)
        {
            var row = table?.Find(id);
            // 표에 없는 번호는 대본 해석기가 잘못 짚어 적은 것이다(DisevRunner) — 보이지 않는다.
            if (table != null && row == null) continue;
            if (target >= 0 && row != null && row.Value.Hint != target) continue;
            found.Add(row?.Name ?? $"발견물 {id}");

            // 증거품은 아직 보고 안 한 발견물의 아이템이다 — 소지품 16칸에 든 것이 아니다(VirtualItems).
            if (row is not { GivesItem: true } got || game.Player.HasAnnounced(id)) continue;
            evidence.Add(items?.Find(got.ItemId)?.Name ?? $"아이템 {got.ItemId}");
        }

        return new(contract, game.HintName(contract.Hint), found, evidence);
    }
}
