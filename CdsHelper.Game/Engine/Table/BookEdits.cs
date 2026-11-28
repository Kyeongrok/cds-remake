using System.IO;
using CdsHelper.Support.Local.Helpers;

namespace CdsHelper.Game.Local.Helpers;

/// <summary>
/// 책 표 위에 사람이 손으로 얹어 둔 줄 — 원본에 없는 <b>새 책</b>을 더하거나, 있는 책을
/// 통째로 갈아 끼운다.
/// </summary>
/// <remarks>
/// <see cref="DiscoveryEdits"/> 와 같은 결이다 — 줄째로 갈아 끼우고, 번호(<see cref="BookTable.Book.Index"/>)가
/// 원본에 없어도 받는다. 커스텀 힌트(<see cref="HintEdits"/> 로 더한 것)는 <b>어느 책의
/// 힌트 목록에 들어 있어야만</b> 도서관에서 읽어 얻어지므로(<c>LibraryDialog.Shown</c>),
/// 책을 여기로 새로 짓거나 기존 책의 <see cref="BookTable.Book.Hints"/> 에 끼워 넣어야
/// 그 힌트를 실제로 손에 넣을 수 있다.
///
/// <b>적어 둔 책표.json 을 직접 고치지 않는다</b> — 그 파일은 EXE 를 읽어 구워 둔 본이라
/// <see cref="BookTable"/> 의 <c>SnapshotVersion</c> 이 오르면 통째로 다시 구워진다. 여기 적어
/// 둔 것은 따로 있어 그 재굽기를 타지 않는다.
/// </remarks>
public static class BookEdits
{
    /// <summary>적어 둘 파일 이름(<c>책-고친것.json</c>).</summary>
    private const string CacheName = "책-고친것";

    /// <summary>JSON 으로 적어 두는 알맹이.</summary>
    internal sealed record Snapshot(List<BookTable.Book> Books);

    private static Dictionary<int, BookTable.Book>? _map;

    /// <summary>켠 커스텀 팩이 바뀌면 담아 둔 것을 버린다 — 다음에 물을 때 새 팩으로 다시 읽는다.</summary>
    static BookEdits() => TableCache.Reloaded += () => _map = null;

    /// <summary>책이 더해지거나 고쳐졌을 때 알린다.</summary>
    public static event Action? Changed;

    /// <summary>더하거나 갈아 끼운 책 전부. 번호가 곧 <see cref="BookTable.Book.Index"/> 다.</summary>
    public static IReadOnlyDictionary<int, BookTable.Book> All => Map;

    /// <summary>그 번호에 얹어 둔 책. 없으면 null.</summary>
    public static BookTable.Book? Of(int id) => Map.TryGetValue(id, out var e) ? e : null;

    /// <summary>
    /// 그 번호로 책을 얹는다 — 원본에 있던 번호면 통째로 갈아 끼우고, 없던 번호면 새로 더한다.
    /// </summary>
    public static void Set(BookTable.Book book)
    {
        // 고유 번호는 한 번 받으면 그대로 — 고쳐 다시 얹어도(제목이 바뀌어도) 그 책을 가리킨 발견물 힌트가 이어진다.
        if (string.IsNullOrEmpty(book.Uuid))
            book = book with { Uuid = Map.TryGetValue(book.Index, out var had) && !string.IsNullOrEmpty(had.Uuid) ? had.Uuid : NewUuid() };
        Map[book.Index] = book;
        Save();
    }

    /// <summary>얹어 둔 책을 걷는다 — 원본에 있던 번호면 원본 값으로 되돌아간다.</summary>
    public static void Reset(int id)
    {
        if (!Map.Remove(id)) return;
        Save();
    }

    /// <summary>얹어 둔 것을 몽땅 걷는다.</summary>
    public static void ResetAll()
    {
        if (Map.Count == 0) return;
        Map.Clear();
        Save();
    }

    private static Dictionary<int, BookTable.Book> Map => _map ??= Load();

    /// <summary>
    /// 이 PC 에서 더하거나 고친 책을 따로 적어 두는 자리 — <c>%APPDATA%\CdsHelper\books-user.json</c>.
    /// </summary>
    /// <remarks>
    /// 기본 자리(<see cref="TableCache.Folder"/>)는 설치 폴더(<c>…\current\exe-tables</c>)라 업데이트가 통째로 갈아 끼운다 —
    /// 커스텀 발견물을 실으려고 더한 책이 업데이트마다 사라졌다(제보). 발견물 본문은 <see cref="UserDiscoveries"/> 가
    /// 이미 %APPDATA% 에 두므로 책만 빠져, 힌트를 실을 곳이 없어졌다. 이제 배포본 줄과 다른 줄은 여기에도 적고,
    /// 읽을 때 배포본 위에 얹는다.
    /// </remarks>
    private static string UserFile => UserDataFolder.Combine("books-user.json");

    private static readonly System.Text.Json.JsonSerializerOptions UserJson = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private static Dictionary<int, BookTable.Book> Load()
    {
        var saved = TableCache.Read<Snapshot>(CacheName);
        var map = new Dictionary<int, BookTable.Book>();
        bool stamped = false;
        foreach (var row in saved?.Data.Books ?? [])
        {
            // 고유 번호가 생기기 전에 적어 둔 책은 여기서 받는다.
            if (string.IsNullOrEmpty(row.Uuid)) { map[row.Index] = row with { Uuid = NewUuid() }; stamped = true; }
            else map[row.Index] = row;
        }

        // 이 PC 에서 더한 책을 얹는다. 같은 고유 번호의 책이 배포본에 이미 있으면 그 자리를, 번호가 배포본의 다른 책과
        // 겹치면(업데이트가 그 번호에 책을 더함) 빈 번호를 새로 받는다 — 발견물은 책을 고유 번호로 잇으니 번호가 바뀌어도 된다.
        foreach (var row in ReadUserRows())
        {
            int at = row.Index;
            int? same = string.IsNullOrEmpty(row.Uuid) ? null
                      : map.Where(p => p.Value.Uuid == row.Uuid).Select(p => (int?)p.Key).FirstOrDefault();
            if (same is int found)
                at = found;
            else if (map.TryGetValue(at, out var other) && !string.IsNullOrEmpty(other.Uuid) && other.Uuid != row.Uuid
                     && !string.IsNullOrEmpty(row.Uuid))
                at = Math.Max(map.Keys.DefaultIfEmpty(0).Max(), row.Index) + 1;
            map[at] = row with { Index = at };
        }

        if (stamped)
        {
            var rows = map.OrderBy(p => p.Key).Select(p => p.Value).ToList();
            TableCache.Write(CacheName, new TableCache.Cached<Snapshot>($"{rows.Count}권", new Snapshot(rows), "사람이 더한 것"));
        }
        return map;
    }

    private static List<BookTable.Book> ReadUserRows()
    {
        try
        {
            if (!File.Exists(UserFile)) return [];
            return System.Text.Json.JsonSerializer.Deserialize<Snapshot>(File.ReadAllText(UserFile))?.Books ?? [];
        }
        catch (Exception) { return []; }
    }

    /// <summary>배포본(기본 자리 · 팩) 줄과 다른 줄만 이 PC 자리에 적는다 — 배포본이 같은 줄을 고쳐 내면 그것을 받게.</summary>
    private static void SaveUserRows()
    {
        try
        {
            var shipped = (TableCache.ReadBase<Snapshot>(CacheName)?.Data.Books ?? []).ToDictionary(b => b.Index);
            string Key(BookTable.Book b) => System.Text.Json.JsonSerializer.Serialize(b);
            var mine = Map.OrderBy(p => p.Key).Select(p => p.Value)
                          .Where(b => !shipped.TryGetValue(b.Index, out var s) || Key(s) != Key(b)).ToList();
            if (mine.Count == 0) { if (File.Exists(UserFile)) File.Delete(UserFile); return; }
            Directory.CreateDirectory(Path.GetDirectoryName(UserFile)!);
            File.WriteAllText(UserFile, System.Text.Json.JsonSerializer.Serialize(new Snapshot(mine), UserJson));
        }
        catch (Exception) { }
    }

    private static string NewUuid() => Guid.NewGuid().ToString();

    /// <summary>그 고유 번호의 책. 없으면 null.</summary>
    public static BookTable.Book? ByUuid(string uuid) =>
        uuid.Length == 0 ? null : Map.Values.Where(b => b.Uuid == uuid).Select(b => (BookTable.Book?)b).FirstOrDefault();

    private static void Save()
    {
        var rows = Map.OrderBy(p => p.Key).Select(p => p.Value).ToList();
        TableCache.Write(CacheName, new TableCache.Cached<Snapshot>(
            $"{rows.Count}권", new Snapshot(rows), "사람이 더한 것"));
        SaveUserRows();
        Changed?.Invoke();
    }
}
