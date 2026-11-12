using System.IO;
using System.Text.Json;
using CdsHelper.Support.Local.Models;

namespace CdsHelper.Game.Engine;

/// <summary>
/// <b>누적 캐릭터</b> — 은퇴한 제독을 다섯 자리에 올려 두고 다음 판에 남으로 내보내는 자리.
/// </summary>
/// <remarks>
/// 원본은 자리마다 파일 둘을 쓴다(<c>0x00425220</c> 이 EXE 옆을 가리킨다).
/// <code>
///   ACCDATA.CDS      지금 놀고 있는 제독의 <b>행적 기록</b>(0x00419F90 이 만들고 0x0041A070 이 덧붙인다)
///   ACCDATA%d.IDX    은퇴한 제독의 인물 레코드 — 직렬화한 CPerson 그대로(0x0041A270)
///   ACCDATA%d.ACC    그 행적을 DISEV 대본으로 옮긴 것 — 다음 판에서 되돌려 튼다(0x0040D1D0)
/// </code>
/// 등록은 <c>0x0041AB90</c> 이다 — <b>초심자용 캐릭터(<c>0x005A4D1A</c> 비트 8)는 못 올리고</b>,
/// 그 밖에는 명성·나이·돈 어떤 조건도 없다. 빈 자리를 <c>0x0041AC60</c> 이 찾아 주고,
/// 다섯이 다 차 있으면 <b>아무 말 없이</b> 세이브만 지운다(원본 그대로다 — 5명이 찼다고
/// 이르는 문구 <c>0x00571D88</c> 는 절대 안 걸리는 가지에 있다).
///
/// <see cref="Place"/> 가 인물 번호 <b>276~280</b> 에 앉히고, <see cref="AccReplay"/> 가 옛 판의
/// 발자취를 날마다 되짚어 그때 그 도시로 옮긴다.
/// </remarks>
public static class AccData
{
    /// <summary>자리 수 — 다섯이다(<c>0x0041AC60</c> 의 <c>cmp 5</c>).</summary>
    public const int Slots = 5;

    /// <summary>은퇴한 제독 하나.</summary>
    /// <param name="Face">얼굴 번호. MALE 그림의 자리다.</param>
    /// <param name="RetiredOn">은퇴한 게임 날짜.</param>
    /// <param name="City">은퇴한 도시. 다음 판에서 그 자리에 선다. 옛 파일은 −1 이다.</param>
    /// <param name="Track">행적 — 어느 날 어느 도시에 들었는지. 다음 판에서 되돌려 튼다.</param>
    /// <param name="Id">이 사람의 표지 — 판(세이브)이 앉힌 자리를 이것으로 기억한다(<see cref="Player.AccSeats"/>).
    /// 이 칸 앞의 파일은 읽을 때 이름과 은퇴 날짜로 지어 붙인다(<see cref="LegacyId"/>).</param>
    /// <param name="Hidden">「등장 안 함」 — 지우지 않고 다음 게임부터 안 내보낸다. 원본에 없는 것이다(모드 창 「누적 캐릭터…」).</param>
    public sealed record Character(string Name, string Family, string Given, int Face,
                                   int Nation, int JobIndex, int Blood, int Fame,
                                   int[] Abilities, Dictionary<string, int> Skills,
                                   Dictionary<string, int> Tongues, DateTime RetiredOn,
                                   int City = -1, Player.Trace[]? Track = null,
                                   string? Id = null, bool Hidden = false);

    /// <summary>적어 두는 자리 — 세이브와 같은 폴더다.</summary>
    public static string Path => System.IO.Path.Combine(
        System.IO.Path.GetDirectoryName(GameSave.Path) ?? "", "누적캐릭터.json");

    private static readonly JsonSerializerOptions Pretty = new() { WriteIndented = true };

    /// <summary>올라 있는 사람들. 없으면 빈 목록이다.</summary>
    /// <remarks>표지(<see cref="Character.Id"/>)가 없는 사람은 이름과 은퇴 날짜로 지어 붙이고 파일에도 적어 둔다.</remarks>
    public static List<Character> Load()
    {
        List<Character> all;
        try
        {
            if (!File.Exists(Path)) return [];
            all = JsonSerializer.Deserialize<List<Character>>(File.ReadAllText(Path)) ?? [];
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return [];
        }
        bool named = false;
        for (int i = 0; i < all.Count; i++)
        {
            if (!string.IsNullOrEmpty(all[i].Id)) continue;
            all[i] = all[i] with { Id = LegacyId(all[i]) };
            named = true;
        }
        if (named) Save(all);
        return all;
    }

    /// <summary>
    /// 표지가 없던 사람의 표지 — 이름과 은퇴 날짜로 짓는다. 파일에 못 적어도 읽을 때마다 같은 값이 나와
    /// 세이브가 기억한 자리(<see cref="Player.AccSeats"/>)와 맞는다.
    /// </summary>
    private static string LegacyId(Character who)
    {
        byte[] hash = System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes($"{who.Name}|{who.RetiredOn.Ticks}|{who.Face}"));
        return "old-" + Convert.ToHexString(hash, 0, 8).ToLowerInvariant();
    }

    private static string NewId() => Guid.NewGuid().ToString("N");

    /// <summary>
    /// 새 게임에 앉힐 사람들의 표지 — 「등장 안 함」을 뺀 앞의 다섯. 이 차례가 인물 276~280 이 된다.
    /// </summary>
    public static List<string?> SeatsForNewGame() =>
        [.. Load().Where(c => !c.Hidden).Take(Slots).Select(c => (string?)c.Id)];

    /// <summary>
    /// 이 칸 앞의 세이브가 앉힌 자리 — 예전처럼 파일 차례 그대로 다섯이다(「등장 안 함」도 그대로 든다).
    /// </summary>
    public static List<string?> SeatsAsListed() =>
        [.. Load().Take(Slots).Select(c => (string?)c.Id)];

    /// <summary>
    /// 그 사람을 지운다 — 원본에 없는 것이다(모드 창 「누적 캐릭터…」). 원본은 「등장시키지 않는다」를 고른 주인공이
    /// 은퇴할 때 다섯을 통째로 지울 뿐이다(<see cref="Clear"/>).
    /// </summary>
    /// <remarks>
    /// 남은 사람들의 차례가 당겨지지만 판마다 앉힌 자리를 표지로 기억하므로(<see cref="Player.AccSeats"/>)
    /// 이미 앉힌 판에서 다른 사람이 그 자리로 밀려오지 않는다.
    /// </remarks>
    public static bool Remove(string id)
    {
        var all = Load();
        int gone = all.RemoveAll(c => c.Id == id);
        return gone > 0 && Save(all);
    }

    /// <summary>「등장 안 함」을 켜고 끈다 — 다음 게임부터 든다.</summary>
    public static bool SetHidden(string id, bool hidden)
    {
        var all = Load();
        int at = all.FindIndex(c => c.Id == id);
        if (at < 0) return false;
        all[at] = all[at] with { Hidden = hidden };
        return Save(all);
    }

    /// <summary>앉힌 자리 표지로 사람을 찾는다. 지워졌으면 null.</summary>
    public static Character? Find(IReadOnlyList<Character> all, string? id) =>
        id == null ? null : all.FirstOrDefault(c => c.Id == id);

    /// <summary>그 이름을 쓰는 사람이 이미 있는지(<c>0x0045CF4A</c>).</summary>
    /// <remarks>새 제독을 지을 때 걸린다 — 「같은 성명을 쓰는 누적 캐릭터가 있습니다」(<c>0x00571758</c>).</remarks>
    public static bool NameTaken(string name) =>
        Load().Any(c => string.Equals(c.Name, name, StringComparison.Ordinal));

    /// <summary>
    /// 은퇴한 제독을 빈 자리에 올린다(<c>0x0041AB90</c> → <c>0x0041A270</c>).
    /// </summary>
    /// <returns>올렸으면 true. 자리가 다 찼으면 false 인데, 원본도 그때는 <b>아무 말이 없다</b>.</returns>
    public static bool Register(Player player)
    {
        var all = Load();
        if (all.Count >= Slots) return false;

        all.Add(new Character(player.Name, player.Family, player.Given, player.Face,
                              player.Nation, player.JobIndex, player.Blood, player.Fame,
                              [.. player.Abilities],
                              new Dictionary<string, int>(player.Skills),
                              new Dictionary<string, int>(player.Tongues),
                              player.Date, player.HomePort, [.. player.Traces], NewId()));
        return Save(all);
    }

    /// <summary>
    /// 적어 둔 판(세이브) 그대로 올린다 — 타이틀의 「모험 중단 → 은퇴시킨다」가 쓴다(<c>0x0045F77F</c>).
    /// </summary>
    public static bool Register(GameSave.Data saved)
    {
        var all = Load();
        if (all.Count >= Slots) return false;

        all.Add(new Character(saved.Name ?? "", saved.Family ?? "", saved.Given ?? "",
                              saved.Face ?? 0, saved.Nation ?? 0, saved.JobIndex ?? 0,
                              saved.Blood ?? 0, saved.Fame ?? 0,
                              saved.Abilities?.ToArray() ?? [],
                              new Dictionary<string, int>(saved.Skills),
                              new Dictionary<string, int>(saved.Tongues ?? []),
                              saved.Date, saved.HomePort ?? -1, saved.Traces?.ToArray() ?? [], NewId()));
        return Save(all);
    }

    /// <summary>누적 캐릭터가 앉는 첫 인물 번호(<c>0x0041AF00</c> 의 <c>0x114</c>).</summary>
    public const int FirstPerson = 276;

    /// <summary>
    /// 올라 있는 사람들을 인물 <b>276~280</b> 자리에 앉힌다(<c>0x0041AF00</c>).
    /// </summary>
    /// <remarks>
    /// 원본은 여기서 <c>ACCDATA%d.IDX</c> 의 인물 레코드를 그대로 그 칸에 부어 넣는다. 우리는
    /// 적어 둔 값으로 칸을 채운다 — 이름·얼굴·능력·기능·언어·명성이다.
    ///
    /// <b>첫 자리는 은퇴한 도시</b>고, 그 뒤로는 <see cref="AccReplay"/> 가 행적대로 옮긴다
    /// (<c>0x00432740</c> → <c>0x0040D1D0</c>). 번호가
    /// <see cref="Local.Helpers.PersonTable.MovingEnd"/> 뒤라 달마다 굴리지는 않는다.
    /// 그 사람을 만나 이야기할 수 있고, 바다에서 마주치면 해적질 대상이 된다.
    /// 나이는 적어 두지 않아 그 칸 그대로 둔다.
    /// </remarks>
    /// <param name="seats">이 판이 앉힌 사람들의 표지 — 자리 i 가 인물 276+i 다(<see cref="Player.AccSeats"/>).
    /// 표지가 있는데 그 사람이 지워졌으면 그 칸은 <b>세상에서 내린다</b>(등장 0) — 남은 사람을 당겨 앉히지 않는다.</param>
    /// <returns>앉힌 사람 수.</returns>
    public static int Place(IReadOnlyList<Local.Helpers.PersonTable.Row> people, IReadOnlyList<string?> seats)
    {
        var all = Load();
        int put = 0;
        for (int i = 0; i < seats.Count && i < Slots; i++)
        {
            int at = FirstPerson + i;
            if (at >= people.Count) break;

            var row = people[at];
            if (Find(all, seats[i]) is not { } who)
            {
                if (seats[i] != null) Unseat(row);
                continue;
            }
            row.First = who.Given.Length > 0 ? who.Given : who.Name;
            row.Last = who.Family;
            row.Face = who.Face;
            row.Fame = who.Fame;
            row.City = who.City;
            // 여관에 앉힌다 — 0x004321E0 이 +0xB8(건물) 을 5, +0xE8(갈래) 를 4 로 둔다(0x00432260 · 0x0043226A).
            row.Building = who.City >= 0 ? Local.Helpers.PersonTable.Inn : -1;
            row.Grade = 3;
            row.Hire = Local.Helpers.PersonTable.Retired;    // 갈래 4 — 부하로는 못 삼고 일기토 차림표가 뜬다(0x004A4860)
            row.Kind = 2;                            // 안 움직인다
            row.Dest = -1;
            row.From = -1;
            row.Wait = 0;
            row.Appear = who.City >= 0 ? 1 : 0;

            for (int k = 0; k < row.Stats.Length && k < who.Abilities.Length; k++)
                row.Stats[k] = who.Abilities[k];
            for (int k = 0; k < row.Skills.Length; k++)
                row.Skills[k] = who.Skills.TryGetValue(Support.Local.Models.Skill.Names[k], out int v) ? v : 0;
            for (int k = 0; k < row.Languages.Length; k++)
                row.Languages[k] = who.Tongues.TryGetValue(Support.Local.Models.Skill.Languages[k], out int v) ? v : 0;

            put++;
        }
        return put;
    }

    /// <summary>지운 사람이 앉아 있던 칸을 세상에서 내린다 — 만날 수도, 바다에서 마주칠 수도 없다.</summary>
    public static void Unseat(Local.Helpers.PersonTable.Row row)
    {
        row.Appear = 0;
        row.City = -1;
        row.Building = -1;
        row.Dest = -1;
        row.From = -1;
    }

    /// <summary>다섯 자리를 통째로 비운다 — 「누적 캐릭터를 등장시키지 않는다」를 고른 뒤다(<c>0x0041AD55</c>).</summary>
    public static bool Clear() => Save([]);

    private static bool Save(List<Character> all)
    {
        try
        {
            string dir = System.IO.Path.GetDirectoryName(Path) ?? "";
            if (dir.Length > 0) Directory.CreateDirectory(dir);
            File.WriteAllText(Path, JsonSerializer.Serialize(all, Pretty));
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
