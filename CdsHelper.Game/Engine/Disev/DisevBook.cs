using System.Buffers.Binary;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using CdsHelper.Game.Local.Helpers;

namespace CdsHelper.Game.Engine.Disev;

/// <summary>
/// 발견 이벤트 대본을 통째로 적어 둔 것 — <c>발견이벤트.json</c>.
/// </summary>
/// <remarks>
/// <b>차례가 이렇다.</b>
/// <list type="number">
///   <item>적어 둔 <c>%APPDATA%\CdsHelper\exe-tables\발견이벤트.json</c> 이 있으면 <b>그것을 읽는다</b>.</item>
///   <item>없으면 <b>앱 옆에 실어 둔 <c>발견이벤트.json</c></b>(<see cref="BundledPath"/>)을 먼저 적어 두고 그것을 읽는다.</item>
///   <item>편집기가 고치는 것도 적어 둔 JSON 이다.</item>
/// </list>
/// <b><c>DISEV.CDS</c> 는 안 읽는다.</b> 실어 둔 JSON 은 원본 <c>DISEV.CDS</c> 를 한 번 떠서 저장소
/// (<c>CdsHelper/발견이벤트.json</c>)에 넣은 것이라, 게임 폴더에 그 파일이 없어도 대본이 돈다.
///
/// 파트 하나를 <b>덩이마다 줄 나무 하나</b>로 적는다(<see cref="Entry"/> · <see cref="DisevTree"/>). 슬롯 표는
/// 오프셋 대신 덩이 번호로 적고, 머리말은 적을 때 다시 셈한다 — 그래야 덩이 하나를
/// 손으로 늘리고 줄여도 파일이 깨지지 않는다.
///
/// <b>앱이 새로 실려도 적어 둔 것은 저절로 안 바뀐다.</b> 사람이 고쳐 둔 대본을 말없이
/// 지울 수는 없다. 실린 원본으로 되돌리려면 편집기의 「원본에서 다시 뜨기」를 누른다.
/// </remarks>
public sealed class DisevBook
{
    /// <summary>적어 둘 파일 이름(<c>발견이벤트.json</c>).</summary>
    public const string CacheName = "발견이벤트";

    /// <summary>
    /// 이 집이 다룰 수 있는 <b>대본 책</b> 셋 — 발견 이벤트와 미리 만든 주인공 둘의 이야기다.
    /// </summary>
    /// <remarks>
    /// <c>STORY0.CDS</c> · <c>STORY1.CDS</c> 는 <b>그릇도 말도 DISEV.CDS 와 같다</b> — 같은
    /// Ls12 아카이브에 같은 명령을 쓴다(파트 열일곱). 다른 것은 파트 번호가 발견물 번호가
    /// 아니라 <b>마당 안의 장면 번호</b>라는 것뿐이라, 읽고 고치는 길은 그대로 쓴다.
    /// </remarks>
    public static readonly (string Cache, string Title, string Source)[] Books =
    [
        (CacheName, "발견 이벤트", "DISEV.CDS"),
        ("이야기0", "이야기 0(라몬)", "STORY0.CDS"),
        ("이야기1", "이야기 1(에밀리오)", "STORY1.CDS"),
        // 새 주인공(NORMAL)의 개인 이야기 — 차례가 곧 국적 x 4 + 직업이다(0x00552750).
        ("PEX", "개인 이야기(포르투갈 탐험가)", "PEX.CDS"),
        ("PDG", "개인 이야기(포르투갈 발굴자)", "PDG.CDS"),
        ("PHT", "개인 이야기(포르투갈 사냥꾼)", "PHT.CDS"),
        ("PCQ", "개인 이야기(포르투갈 정복자)", "PCQ.CDS"),
        ("EEX", "개인 이야기(에스파니아 탐험가)", "EEX.CDS"),
        ("EDG", "개인 이야기(에스파니아 발굴자)", "EDG.CDS"),
        ("EHT", "개인 이야기(에스파니아 사냥꾼)", "EHT.CDS"),
        ("ECQ", "개인 이야기(에스파니아 정복자)", "ECQ.CDS"),
    ];

    /// <summary>
    /// 새 주인공(NORMAL)의 개인 이야기 책 — <c>0x0045ECA8</c> 이 마무리 뒤에 <c>0x00552750[국적 x 4 + 직업]</c> 을
    /// 이야기 관리자(<c>0x004AB420</c>)에 건다. 표 밖이면 null.
    /// </summary>
    public static string? PersonalStory(int nation, int job) =>
        nation is 0 or 1 && job is >= 0 and < 4 ? Books[3 + nation * 4 + job].Cache : null;

    /// <summary>
    /// 알맹이 모양 판. 1 은 파트 통째 <c>Hex</c>, 2 는 덩이별 16진 글, 3 은 덩이마다 분기로 가른
    /// 줄 나무(<see cref="DisevTree"/>), 4 는 그 줄을 명령 하나씩 떼고 음원·EVSTILL·대사를 칸으로 푼 것,
    /// 5 는 발견 처리(<c>Discover</c>)·아이템 획득(<c>GetItem</c>)·AVI(<c>Avi</c>)·특수 조우(<c>Encounter</c>)·능력치 더하기/빼기(<c>Stat</c>)까지 칸으로 풀고 <c>33</c> 을 제 명령으로 뗀 것이다.
    /// 6 은 cds_disev_editor v1.0 대조로 명령 길이를 바로잡아 다시 가르고, 줄마다 분류(<c>Category</c>)를 붙인 것이다.
    /// 7 은 칸으로 못 푼 명령을 16진 글 대신 <c>{ Category, OpCode, Note }</c> 로 적은 것이다.
    /// 8 은 나무를 버리고 원본 차례 그대로 <b>평평한 줄 배열 + 라벨</b>(<c>Label</c>·<c>Goto</c>)로 적은 것이다.
    /// 9 는 분류(<c>Category</c>)를 빼고 <c>00</c> 무리 명령 이름(<c>Command</c>)을 붙인 것이다.
    /// 10 은 줄을 <b>함수 호출</b>로 적은 것이다 — <c>Call</c>·<c>Args</c>, 분기는 <c>GotoIf</c>·<c>GotoUnless</c>·<c>Target</c>(<see cref="DisevCalls"/>).
    /// 11 은 조건 41 08 · 41 10 · 42 10 · 65 · 59 와 06 한 바이트를, 12 는 40 0D(부관 앉힘)를, 13 은 04 한 바이트(이야기 끝)를, 14 는 38 12(후원자 소개)를 읽게 되어 다시 적는다.
    /// 옛 판도 읽어서 새 판으로 옮겨 적는다.
    /// </summary>
    private const int SnapshotVersion = 14;

    /// <summary>대본 한 파트.</summary>
    /// <param name="Index">발견물 번호이자 파트 번호(0~273).</param>
    /// <param name="Step">내부 단계 번호(<see cref="DisevPart.Step"/>).</param>
    /// <param name="Slots">슬롯 표 — 오프셋이 아니라 <paramref name="Chunks"/> 의 번호다.</param>
    /// <param name="Chunks">덩이마다 줄 나무 하나(<see cref="DisevTree"/>). 파트 안의 차례 그대로다.</param>
    /// <param name="Hex">
    /// 덩이로 못 나눈 파트의 통째 날바이트. 판 1 파일도 이 칸으로 읽힌다.
    /// 이것이 있으면 나머지 칸은 안 본다.
    /// </param>
    public sealed record Entry(
        int Index,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? Step = null,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] List<SlotEntry>? Slots = null,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull), JsonConverter(typeof(DisevChunksConverter))]
        List<List<DisevLine>>? Chunks = null,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Hex = null);

    /// <summary>슬롯 한 줄 — 조건 덩이와 본문 덩이의 번호.</summary>
    public readonly record struct SlotEntry(int Condition, int Body);

    /// <summary>JSON 으로 적어 두는 알맹이.</summary>
    internal sealed record Snapshot(List<Entry> Parts);

    private readonly List<byte[]> _parts;
    private readonly bool[] _edited;
    private readonly string _stamp;

    /// <summary>이 책이 적히는 이름(<see cref="Books"/> 의 첫 칸).</summary>
    private readonly string _cache;

    /// <summary>이 책이 적히는 이름. 편집기가 자리를 적을 때 쓴다.</summary>
    public string Cache => _cache;

    private DisevBook(List<byte[]> parts, string stamp, string cache)
    {
        _parts = parts;
        _edited = new bool[parts.Count];
        _stamp = stamp;
        _cache = cache;
    }

    /// <summary>왜 못 열었는지. 잘 열렸으면 빈 문자열.</summary>
    public static string LastError { get; private set; } = "";

    /// <summary>적어 둔 파일 자리.</summary>
    public static string Path_ => PathOf(CacheName);

    /// <summary>그 책이 적히는 자리.</summary>
    public static string PathOf(string cache) => TableCache.PathFor(cache);

    /// <summary>앱 옆에 실어 둔 원본 대본 — 게임 파일을 한 번 떠서 앱과 함께 싣는다.</summary>
    public static string BundledPath => BundledPathOf(CacheName);

    /// <summary>그 책의 실어 둔 원본 자리.</summary>
    public static string BundledPathOf(string cache) =>
        System.IO.Path.Combine(AppContext.BaseDirectory, cache + ".json");

    /// <summary>파트 수. 274 다.</summary>
    public int Count => _parts.Count;

    /// <summary>고친 파트가 하나라도 있는지.</summary>
    public bool HasChanges => Array.IndexOf(_edited, true) >= 0;

    /// <summary>그 파트를 고쳤는지.</summary>
    public bool IsEdited(int index) => index >= 0 && index < _edited.Length && _edited[index];

    /// <summary>그 파트의 알맹이. 밖에서 고치지 못하게 베껴 준다.</summary>
    public byte[] Part(int index) =>
        index >= 0 && index < _parts.Count ? (byte[])_parts[index].Clone() : [];

    /// <summary>그 파트를 갈아 끼운다. 적어 두는 것은 <see cref="Save"/> 가 한다.</summary>
    public void Replace(int index, byte[] data)
    {
        if (index < 0 || index >= _parts.Count || data.Length == 0) return;
        if (_parts[index].AsSpan().SequenceEqual(data)) return;

        _parts[index] = (byte[])data.Clone();
        _edited[index] = true;
    }

    /// <summary>
    /// 대본을 연다. 적어 둔 것이 있으면 그것을, 없으면 앱에 실린 원본을 적고 그것을.
    /// </summary>
    public static DisevBook? Open(string cache = CacheName)
    {
        LastError = "";

        // 판 1 은 버리지 않고 옮겨 적는다 — 사람이 고쳐 둔 대본이 들어 있을 수 있다.
        var cached = TableCache.Read<Snapshot>(cache);
        if (cached is { Version: >= 1 and <= SnapshotVersion } && cached.Data.Parts.Count > 0)
        {
            var book = FromEntries(cached.Data.Parts, cached.Stamp, cache);
            if (book != null && cached.Version != SnapshotVersion) book.Write();
            return book;
        }

        return Reset(cache);
    }

    /// <summary>
    /// 앱에 실린 원본 대본을 <c>발견이벤트.json</c> 에 적고 그것을 연다.
    /// </summary>
    /// <remarks>적어 둔 것이 있어도 <b>덮어쓴다</b> — 「원본에서 다시 뜨기」가 이 길이다.</remarks>
    public static DisevBook? Reset(string cache = CacheName)
    {
        LastError = "";
        if (ReadBundle(cache) is not { } bundle) return null;

        var book = FromEntries(bundle.Data.Parts, bundle.Stamp, cache);
        book?.Write();
        return book;
    }

    /// <summary>앱 옆에 실린 원본 대본을 읽는다. 없거나 깨졌으면 null 이고 까닭은 <see cref="LastError"/> 다.</summary>
    private static TableCache.Cached<Snapshot>? ReadBundle(string cache)
    {
        string path = BundledPathOf(cache);
        try
        {
            if (!File.Exists(path))
            {
                LastError = $"앱 폴더에 {cache}.json 이 없습니다 ({path})";
                return null;
            }

            var bundle = JsonSerializer.Deserialize<TableCache.Cached<Snapshot>>(File.ReadAllText(path));
            if (bundle?.Data is { Parts.Count: > 0 }) return bundle;
            LastError = $"앱에 실린 {cache}.json 이 비었습니다";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            LastError = $"앱에 실린 {cache}.json 을 못 읽었습니다 — {ex.Message}";
        }
        return null;
    }

    /// <summary>고친 것을 적어 둔다.</summary>
    public void Save()
    {
        Write();
        Array.Clear(_edited);
    }

    /// <summary>
    /// 그 파트를 <b>앱에 실린 원본</b>에서 도로 가져온다. 원본을 못 읽으면 아무 일도 없다.
    /// </summary>
    public bool Restore(int index)
    {
        LastError = "";
        if (index < 0 || index >= _parts.Count) return false;
        if (ReadBundle(_cache) is not { } bundle) return false;

        if (bundle.Data.Parts.FirstOrDefault(p => p.Index == index) is not { } entry)
        {
            LastError = $"실린 원본에 파트 {index} 가 없습니다";
            return false;
        }
        if (Join(entry, out string why) is not { Length: > 0 } data)
        {
            LastError = $"실린 원본의 파트 {index} 가 깨졌습니다 — {why}";
            return false;
        }

        _parts[index] = data;
        _edited[index] = true;      // 적어 둔 책과 달라졌으니 저장할 거리가 있다
        return true;
    }

    private static DisevBook? FromEntries(List<Entry> rows, string stamp, string cache = CacheName)
    {
        var parts = new List<byte[]>(rows.Count);
        foreach (var row in rows.OrderBy(r => r.Index))
        {
            if (Join(row, out string why) is not { Length: > 0 } data)
            {
                LastError = $"{cache}.json 의 파트 {row.Index} 가 깨졌습니다 — {why}";
                return null;
            }
            parts.Add(data);
        }
        return new DisevBook(parts, stamp, cache);
    }

    /// <summary>
    /// 파트를 덩이로 가른다. 되짜서 <b>한 바이트도 안 같으면</b> 통째 <c>Hex</c> 로 둔다.
    /// </summary>
    /// <remarks>
    /// 머리말 뒤와 첫 덩이 사이에 틈이 있거나 뼈대가 안 읽히는 파트는 덩이로 적으면
    /// 되짤 때 틈이 사라진다. 원본을 잃느니 통째로 적는다.
    /// </remarks>
    private static Entry Split(int index, byte[] data)
    {
        var whole = new Entry(index, Hex: DisevScript.Hex(data));
        if (DisevPart.Parse(data, out _) is not { } part || part.ChunkStarts[0] != part.HeaderEnd)
            return whole;

        var starts = part.ChunkStarts;
        var entry = new Entry(
            index,
            part.Step,
            part.Slots.Select(s => new SlotEntry(IndexOf(starts, s.Condition), IndexOf(starts, s.Body))).ToList(),
            DisevTree.BuildPart(part));

        return Join(entry, out _) is { } back && back.AsSpan().SequenceEqual(data) ? entry : whole;
    }

    /// <summary>
    /// 줄 나무로 고친 덩이들로 그 파트를 다시 짓는다 — 슬롯·단계는 <paramref name="part"/> 그대로, 라벨·점프는 새로 셈한다.
    /// </summary>
    /// <remarks>편집기가 명령을 넣고 뺄 때 쓴다. 바이트를 그 자리에 끼우면 덩이 밖으로 뛰는 이동이 어긋난다.</remarks>
    public static byte[]? JoinChunks(DisevPart part, List<List<DisevLine>> chunks, out string error)
    {
        var starts = part.ChunkStarts;
        var entry = new Entry(0, part.Step,
            part.Slots.Select(s => new SlotEntry(IndexOf(starts, s.Condition), IndexOf(starts, s.Body))).ToList(),
            chunks);
        return Join(entry, out error);
    }

    private static int IndexOf(IReadOnlyList<int> list, int value)
    {
        for (int i = 0; i < list.Count; i++)
            if (list[i] == value) return i;
        return -1;
    }

    /// <summary>
    /// 덩이들을 이어 파트를 짓는다 — 머리말의 슬롯 오프셋은 덩이 길이로 다시 셈한다.
    /// </summary>
    private static byte[]? Join(Entry entry, out string error)
    {
        error = "";
        if (entry.Hex != null)
        {
            if (DisevScript.ParseHex(entry.Hex) is { Length: > 0 } whole) return whole;
            error = "Hex 를 못 읽었습니다";
            return null;
        }

        if (entry.Step is not { } step || entry.Slots is not { Count: > 0 } slots || entry.Chunks is not { Count: > 0 } chunks)
        {
            error = "Step · Slots · Chunks 가 다 있어야 합니다";
            return null;
        }

        int headerEnd = 4 + slots.Count * 4;
        var output = new List<byte>(headerEnd + 1024);
        output.AddRange(new byte[headerEnd]);

        // 라벨은 파트 전체에서 찾는다 — 절대 이동(30 1D)이 덩이를 건너 뛴다.
        if (DisevTree.FlattenPart(chunks, headerEnd, out string why) is not { } pieces)
        {
            error = why;
            return null;
        }

        var at = new int[chunks.Count];
        for (int i = 0; i < chunks.Count; i++)
        {
            if (pieces[i].Length == 0)
            {
                error = $"덩이 {i} 가 비었습니다";
                return null;
            }
            at[i] = output.Count;
            output.AddRange(pieces[i]);
        }

        if (output.Count > 0xFFFF + 4)
        {
            error = $"파트가 너무 큽니다({output.Count}바이트)";
            return null;
        }

        var result = output.ToArray();
        BinaryPrimitives.WriteUInt16LittleEndian(result, (ushort)step);
        BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(2), (ushort)slots.Count);
        for (int i = 0; i < slots.Count; i++)
        {
            var (condition, body) = (slots[i].Condition, slots[i].Body);
            if (condition < 0 || condition >= chunks.Count || body < 0 || body >= chunks.Count)
            {
                error = $"슬롯 {i} 가 없는 덩이를 가리킵니다";
                return null;
            }
            BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(4 + i * 4), (ushort)(at[condition] - 4));
            BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(6 + i * 4), (ushort)(at[body] - 4));
        }
        return result;
    }

    private void Write()
    {
        var rows = new List<Entry>(_parts.Count);
        for (int i = 0; i < _parts.Count; i++) rows.Add(Split(i, _parts[i]));

        TableCache.Write(_cache, new TableCache.Cached<Snapshot>(
            _stamp, new Snapshot(rows), "DISEV.CDS", SnapshotVersion));
    }
}
