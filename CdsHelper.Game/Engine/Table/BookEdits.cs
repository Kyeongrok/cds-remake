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

    private static Dictionary<int, BookTable.Book> Load()
    {
        var saved = TableCache.Read<Snapshot>(CacheName);
        var map = new Dictionary<int, BookTable.Book>();
        foreach (var row in saved?.Data.Books ?? []) map[row.Index] = row;
        return map;
    }

    private static void Save()
    {
        var rows = Map.OrderBy(p => p.Key).Select(p => p.Value).ToList();
        TableCache.Write(CacheName, new TableCache.Cached<Snapshot>(
            $"{rows.Count}권", new Snapshot(rows), "사람이 더한 것"));
        Changed?.Invoke();
    }
}
