using System.Text.Json.Serialization;
using CdsHelper.Support.Local.Models;

namespace CdsHelper.Game.Local.Helpers;

/// <summary>
/// CDS_95.EXE 안의 책 표. 책마다 제목·저자·읽는 데 필요한 언어·나오는 해·놓인 도서관·주는
/// 힌트가 들어 있다.
/// </summary>
/// <remarks>
/// <code>
///   책 표    VA 0x004C4748, 257권 x 0x58 (.rdata)
///   +0x00 제목 ptr      +0x04 저자 ptr
///   +0x0C 책의 언어(0~13, 언어 이름표와 같은 차례)   ← 그 언어 3 이라야 읽는다
///   +0x10 나오는 해(1480 + 값)
///   +0x18~+0x34 놓인 도시 8칸(-1 = 없음)
///   +0x38~+0x54 주는 힌트 8칸(-1 = 없음)
///
///   힌트 표  VA 0x004D8EA0, 0x50 간격
///   +0x00 필요 기능(기능 이름표 색인)   +0x08 필요 수준
/// </code>
/// 자세한 것은 볼트 <c>20.분석-도서관 책과 책등 색</c> 에 있다. 표는 EXE 에서 그때그때
/// 읽는다 — <see cref="CityBuildingTable"/> 과 같은 수다.
/// </remarks>
public sealed class BookTable
{
    private const int BooksVa = 0x004C4748;
    private const int BookCount = 257;
    private const int BookSize = 0x58;
    private const int HintsVa = 0x004D8EA0;

    /// <summary>선행 발견물 칸이 <see cref="HintsVa"/> 에서 얼마나 떨어져 있는지(줄 안 +0x30).</summary>
    private const int ParentsOffset = 0x10;

    /// <summary>선행 발견물 칸 수(<c>0x0042CCC0</c> 이 여덟을 훑는다).</summary>
    private const int ParentSlots = 8;
    private const int HintCount = 186;
    private const int HintSize = 0x50;

    /// <summary>책 한 권.</summary>
    /// <param name="Language">책의 언어(언어 이름표 색인 0~13).</param>
    /// <param name="Year">이 해부터 서가에 나온다.</param>
    /// <param name="Cities">놓인 도서관의 도시 번호.</param>
    /// <param name="Hints">
    /// 읽으면 주는 힌트 번호. 여덟 칸을 <b>칸 차례 그대로</b> 담고 빈 칸(-1)만 뺐다 — 칸은 앞에서부터
    /// 채워져 있어서 <c>Hints[i]</c> 가 곧 펼친 책의 펼침면 <c>i</c> 다(<c>0x00464A30</c>).
    /// </param>
    /// <remarks>
    /// 레코드 <b>구조체</b>는 빈 생성자가 늘 있어서, 적어 둔 JSON 을 되읽을 때 어느 것을 쓸지
    /// 일러 주지 않으면 값이 전부 0 으로 들어온다.
    /// </remarks>
    [method: JsonConstructor]
    public readonly record struct Book(
        int Index, string Title, string Author, int Language, int Year,
        IReadOnlyList<int> Cities, IReadOnlyList<int> Hints);

    /// <summary>
    /// 힌트를 알아들으려면 있어야 하는 것 — 기능 자리와 <b>먼저 발견해 두어야 할 것들</b>.
    /// </summary>
    /// <remarks>
    /// 기능만으로는 안 되는 힌트가 있다. 게임은 <c>0x00463E50</c> 에서 기능을 보기 전에
    /// <c>0x0042CCC0</c> 으로 <b>선행 발견물 여덟 칸</b>을 먼저 훑는다 — 한 칸이라도
    /// 아직 못 찾았으면 그 힌트는 안 들어온다.
    /// <code>
    ///   힌트 52 카파도키아   신학 3  ·  선행 발견물 50(산티아고 대성당)
    ///   힌트 101 성스러운 유물상자  신학 2  ·  선행 발견물 62(성 마르틴 교회)
    /// </code>
    /// 성지순례를 다녀와야 읽힌다는 것이 이 칸이다.
    /// </remarks>
    /// <param name="Skill">필요 기능 번호. -1 이면 기능 조건이 없다.</param>
    /// <param name="Level">그 기능(과 언어)의 필요 자리.</param>
    /// <param name="Parents">먼저 발견해 두어야 할 발견물 번호들. 빈 칸(-1)은 뺐다.</param>
    /// <param name="Picture">
    /// 펼친 책 왼쪽 면에 얹는 삽화 번호(힌트 줄 <c>+0x10</c>, <c>0x004D8E90</c>). 0~19 면
    /// <see cref="OpenBookArt"/> 의 그림 <c>13+값</c> 이고, 그 밖(-1)이면 삽화가 없다.
    /// </param>
    /// <param name="Language">
    /// 필요 언어(언어 이름표 색인). -1 이면 없다. 힌트 줄 <c>+0x24</c>(<see cref="HintsVa"/> 기준 <c>+0x04</c>).
    /// 도서관은 책의 언어를 보므로 이 칸을 안 쓰고, <b>아이템에 걸린 힌트</b>를 읽을 때만 본다(<c>0x0046E9E7</c>).
    /// </param>
    [method: JsonConstructor]
    public readonly record struct HintNeed(int Skill, int Level, IReadOnlyList<int>? Parents = null,
                                           int Picture = -1, int Language = -1);

    /// <summary>적어 둘 파일 이름(<c>%APPDATA%\CdsHelper\exe-tables\책표.json</c>).</summary>
    private const string CacheName = "책표";

    /// <summary>알맹이 모양 판. 삽화 칸(<see cref="HintNeed.Picture"/>)으로 3, 언어 칸(<see cref="HintNeed.Language"/>)으로 4 로 올렸다.</summary>
    private const int SnapshotVersion = 4;

    /// <summary>삽화 칸이 <see cref="HintsVa"/> 에서 얼마나 떨어져 있는지(줄 안 +0x10).</summary>
    private const int PictureOffset = -0x10;

    /// <summary>JSON 으로 적어 두는 알맹이. EXE 를 읽어야만 알 수 있는 것 전부다.</summary>
    internal sealed record Snapshot(List<Book> Books, HintNeed[] HintNeeds);

    private readonly List<Book> _books;
    private readonly HintNeed[] _hintNeeds;

    private BookTable(Snapshot snapshot)
    {
        _books = snapshot.Books;
        _hintNeeds = snapshot.HintNeeds;
    }

    /// <summary>왜 못 읽었는지. 잘 열렸으면 빈 문자열.</summary>
    public static string LastError { get; private set; } = "";

    /// <summary>
    /// 책 전부 — 원본 257권에 <see cref="BookEdits"/> 로 더하거나 갈아 끼운 것을 얹은 것.
    /// </summary>
    public IReadOnlyList<Book> Books
    {
        get
        {
            var edits = BookEdits.All;
            var merged = new List<Book>(_books.Count + edits.Count);
            foreach (var b in _books)
                merged.Add(edits.TryGetValue(b.Index, out var edited) ? edited : b);
            foreach (var (id, b) in edits)
                if (_books.All(o => o.Index != id)) merged.Add(b);
            return merged;
        }
    }

    /// <summary><see cref="BookEdits"/> 로 덧씌우기 전의 원본 게임 값. 표 밖이면 null.</summary>
    public Book? Original(int id) => id >= 0 && id < _books.Count ? _books[id] : null;

    /// <summary>그 힌트를 알아들으려면 있어야 하는 것. 번호가 표 밖이면 기능 -1(조건 없음).</summary>
    public HintNeed NeedFor(int hint) =>
        hint >= 0 && hint < _hintNeeds.Length ? _hintNeeds[hint] : new HintNeed(-1, 0);

    /// <summary>
    /// 그 도시 도서관 서가에 꽂히는 책. 그 도시에 놓였고 <paramref name="year"/> 까지
    /// 나온 것만 고른다. 게임도 들어갈 때마다 이렇게 훑는다(색인을 안 만든다).
    /// </summary>
    public List<Book> InLibrary(int cityId, int year)
    {
        var got = new List<Book>();
        foreach (var b in Books)
            if (b.Year <= year && b.Cities.Contains(cityId))
                got.Add(b);
        return got;
    }

    /// <summary>게임 폴더의 CDS_95.EXE 에서 읽는다. 못 읽으면 null.</summary>
    /// <summary>
    /// 표를 연다. 적어 둔 JSON 이 있으면 그것을 읽고, 없거나 판이 갈렸으면 EXE 에서 읽어
    /// 적어 둔다. 둘 다 없을 때만 null 이다.
    /// </summary>
    public static BookTable? Open(string gameDirectory)
    {
        // 판 2 — 힌트의 선행 발견물 칸을 더하면서 올렸다. 예전 JSON 은 다시 굽힌다.
        var snapshot = ExeTable.Open<Snapshot>(CacheName, gameDirectory, ReadFromExe, out string error,
                                               SnapshotVersion);
        LastError = error;
        return snapshot == null ? null : new BookTable(snapshot);
    }

    /// <summary>EXE 에서 책 줄과 힌트 조건을 통째로 읽어 낸다.</summary>
    private static Snapshot? ReadFromExe(PeImage exe, out string error)
    {
        error = "";

        var books = new List<Book>(BookCount);
        for (int k = 0; k < BookCount; k++)
        {
            int row = BooksVa + k * BookSize;
            var title = exe.Text(exe.Word(row + 0x00));
            var author = exe.Text(exe.Word(row + 0x04));
            if (title == null || author == null) continue;

            var cities = new List<int>(8);
            var hints = new List<int>(8);
            for (int i = 0; i < 8; i++)
            {
                int city = exe.Int(row + 0x18 + i * 4);
                if (city >= 0) cities.Add(city);
                int hint = exe.Int(row + 0x38 + i * 4);
                if (hint >= 0) hints.Add(hint);
            }
            books.Add(new Book(k, title, author, exe.Int(row + 0x0C),
                               1480 + exe.Int(row + 0x10), cities, hints));
        }

        // 판이 다른 EXE 를 잘못 읽지 않게 첫 권을 확인한다.
        if (books.Count == 0 || books[0].Title != "형이상학")
        {
            error = "책 표가 기대한 모양이 아닙니다(다른 판의 EXE 일 수 있습니다)";
            return null;
        }

        var needs = new HintNeed[HintCount];
        for (int h = 0; h < HintCount; h++)
        {
            int row = HintsVa + h * HintSize;

            // 선행 발견물 여덟 칸(줄 안 +0x30 = HintsVa 기준 +0x10). -1 은 빈 칸이다.
            var parents = new List<int>(ParentSlots);
            for (int k = 0; k < ParentSlots; k++)
            {
                int id = exe.Int(row + ParentsOffset + k * 4);
                if (id >= 0) parents.Add(id);
            }

            needs[h] = new HintNeed(exe.Int(row), exe.Int(row + 0x08),
                                    parents.Count > 0 ? parents : null,
                                    exe.Int(row + PictureOffset), exe.Int(row + 0x04));
        }

        return new Snapshot(books, needs);
    }
}
