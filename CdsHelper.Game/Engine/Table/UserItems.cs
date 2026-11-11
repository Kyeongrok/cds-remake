using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Windows.Media.Imaging;
using CdsHelper.Support.Local.Helpers;

namespace CdsHelper.Game.Local.Helpers;

/// <summary>
/// 사람이 지어 넣은 아이템 — 원본에 없는 것이다. 커스텀 발견물(<see cref="UserDiscoveries"/>)의 장면이 「[아이템] 이름」으로 보이고
/// 발표할 때 소지품에 넣는다. <c>%APPDATA%\CdsHelper\items\{UUID}</c> 에 한 벌씩 두고 아이템 표를 찾을 때 얹는다 — 업데이트해도 남는다.
/// </summary>
/// <remarks>
/// <code>
///   신분     UUID — 지을 때 한 번 정하고 안 바뀐다. 발견물 장면 줄 · 내보낸 파일은 번호가 아니라 이 UUID 로 가리킨다
///   번호     이 PC 에서 처음 볼 때 1000~9999 에서 한 번 받고 <c>items\ids.json</c> 에 적어 둔다 — 그 뒤로는 안 바뀐다
///            · 첫 칸은 UUID 셈(SHA-256)이라 PC 가 달라도 거의 늘 같은 번호다(세이브를 옮겨도 맞는다)
///            · 그 칸을 이미 누가 받았으면 다음 빈 칸 — 남의 PC 에서 같은 번호였던 두 아이템을 불러와도 안 겹친다
///            · 지운 아이템의 번호도 지우지 않는다 — 그 번호를 든 세이브가 엉뚱한 아이템을 가리키지 않게
///   그림     120x120 png(아이템 그림 크기). 그림 번호는 아이템 번호와 같다(<see cref="ItemArt"/> 가 1000 위를 여기서 찾는다)
/// </code>
/// 원본 286개 · 앱에 실린 더한 아이템(<c>items-added.json</c>, 286~)보다 한참 위라 겹치지 않는다. 세이브의 소지품은 수 목록이라
/// 이 번호를 그대로 든다.
/// </remarks>
public static class UserItems
{
    /// <summary>번호를 받는 구간 — DISEV 아이템 칸(u16)에 들고, 앱에 실린 아이템보다 한참 위다.</summary>
    public const int NumberBase = 1000, NumberEnd = 9999;

    /// <summary>파일 모양 판.</summary>
    public const int FormatVersion = 1;

    /// <summary>아이템 그림 한 장의 크기(<see cref="ItemArt.Width"/>).</summary>
    public const int PictureWidth = ItemArt.Width, PictureHeight = ItemArt.Height;

    /// <summary>사람이 지은 아이템 한 벌 — 번호 없이 들고 다니는 모양.</summary>
    /// <param name="Category">분류(<see cref="ItemTable.CategoryNames"/>, 0~8).</param>
    /// <param name="BuyList">살 때 정가.</param>
    /// <param name="SellList">팔 때 정가.</param>
    /// <param name="Effect">효과 — 무기 · 방어구면 장비 셈에 든다.</param>
    public sealed record Entry(
        string Uuid, string Name, int Category, int BuyList, int SellList, int Effect, string Description,
        string Author = "", int Version = FormatVersion);

    /// <summary>읽어 둔 한 벌 — 알맹이와 이 PC 에서 받은 번호.</summary>
    public sealed record Loaded(Entry Entry, int Id, string Folder)
    {
        /// <summary>그림 파일(120x120 png). 없으면 null.</summary>
        public string? PicturePath => File.Exists(Path.Combine(Folder, PictureFile)) ? Path.Combine(Folder, PictureFile) : null;
    }

    private const string EntryFile = "item.json", PictureFile = "picture.png", IdsFile = "ids.json";

    /// <summary>내보낸 발견물 파일(zip) 속에서 아이템이 드는 자리 — <c>items/{UUID}/item.json</c>.</summary>
    internal const string ZipFolder = "items/";

    /// <summary>모아 둔 자리.</summary>
    public static string Folder => UserDataFolder.Combine("items");

    private static readonly JsonSerializerOptions Pretty = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private static readonly object Gate = new();
    private static List<Loaded>? _all;
    private static Dictionary<int, Loaded>? _byId;
    private static Dictionary<string, Loaded>? _byUuid;

    /// <summary>더하거나 고치거나 지웠을 때.</summary>
    public static event Action? Changed;

    /// <summary>지어 둔 것 전부 — 번호 차례.</summary>
    public static IReadOnlyList<Loaded> All
    {
        get
        {
            lock (Gate)
            {
                if (_all != null) return _all;
                var entries = new List<(Entry, string)>();
                try
                {
                    if (Directory.Exists(Folder))
                        foreach (string dir in Directory.GetDirectories(Folder))
                        {
                            string file = Path.Combine(dir, EntryFile);
                            if (!File.Exists(file)) continue;
                            try
                            {
                                if (JsonSerializer.Deserialize<Entry>(File.ReadAllText(file)) is { Uuid.Length: > 0 } e) entries.Add((e, dir));
                            }
                            catch (JsonException) { }
                        }
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }

                // 번호표에 없는 것(손으로 옮겨 넣은 폴더 · 번호표가 지워진 PC)은 UUID 차례로 받는다 — 받으면 적어 둔다.
                var ids = ReadIds();
                bool grew = false;
                foreach (var (e, _) in entries.OrderBy(x => x.Item1.Uuid, StringComparer.Ordinal))
                    if (!ids.ContainsKey(e.Uuid)) { ids[e.Uuid] = Free(e.Uuid, ids); grew = true; }
                if (grew) WriteIds(ids);

                var loaded = entries.Select(x => new Loaded(x.Item1, ids[x.Item1.Uuid], x.Item2)).OrderBy(l => l.Id).ToList();
                _all = loaded;
                // 번호표를 손으로 고쳐 번호가 겹쳐도 멎지 않게 — 먼저 온 것(UUID 차례)이 쥔다.
                _byId = loaded.GroupBy(l => l.Id).ToDictionary(g => g.Key, g => g.First());
                _byUuid = loaded.GroupBy(l => l.Entry.Uuid, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
                return _all;
            }
        }
    }

    private static void Forget()
    {
        lock (Gate)
        {
            _all = null;
            _byId = null;
            _byUuid = null;
        }
        Changed?.Invoke();
        UserDiscoveries.ItemsChanged();   // 장면 · 발견물 줄이 아이템 번호를 들고 있다
    }

    /// <summary>그 번호의 아이템. 없으면 null.</summary>
    public static Loaded? ById(int id)
    {
        if (id < NumberBase || id > NumberEnd) return null;
        _ = All;
        lock (Gate) return _byId!.TryGetValue(id, out var l) ? l : null;
    }

    /// <summary>그 UUID 의 아이템. 없으면 null.</summary>
    public static Loaded? ByUuid(string uuid)
    {
        _ = All;
        lock (Gate) return _byUuid!.TryGetValue(uuid, out var l) ? l : null;
    }

    // ── 번호표 ─────────────────────────────────────────────────

    /// <summary>
    /// 이 PC 의 번호표(UUID → 번호). 한 번 적은 줄은 지우지 않는다 — 아이템을 지워도 남아, 그 번호를 다른 아이템이 받지 않는다.
    /// </summary>
    private static Dictionary<string, int> ReadIds()
    {
        try
        {
            string path = Path.Combine(Folder, IdsFile);
            if (File.Exists(path) && JsonSerializer.Deserialize<Dictionary<string, int>>(File.ReadAllText(path)) is { } ids)
                return new Dictionary<string, int>(ids.Where(p => p.Value is >= NumberBase and <= NumberEnd), StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { }
        return new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
    }

    private static void WriteIds(Dictionary<string, int> ids)
    {
        Directory.CreateDirectory(Folder);
        string path = Path.Combine(Folder, IdsFile);
        File.WriteAllText(path + ".part", JsonSerializer.Serialize(new SortedDictionary<string, int>(ids, StringComparer.Ordinal), Pretty));
        File.Move(path + ".part", path, overwrite: true);
    }

    /// <summary>그 UUID 가 받을 번호 — UUID 셈으로 첫 칸을 잡고, 번호표에 이미 있는 번호면 다음 칸으로 민다.</summary>
    private static int Free(string uuid, Dictionary<string, int> ids)
    {
        var taken = ids.Values.ToHashSet();
        if (taken.Count > NumberEnd - NumberBase) throw new IOException("아이템 번호가 다 찼습니다");
        int span = NumberEnd - NumberBase + 1;
        int n = NumberBase + (int)(BitConverter.ToUInt32(SHA256.HashData(Encoding.UTF8.GetBytes($"item:{uuid}")), 0) % (uint)span);
        while (taken.Contains(n)) n = n >= NumberEnd ? NumberBase : n + 1;
        return n;
    }

    /// <summary>그 UUID 의 번호 — 번호표에 없으면 받아 적는다. 이미 있으면 그 번호 그대로다.</summary>
    private static int Assign(string uuid)
    {
        lock (Gate)
        {
            var ids = ReadIds();
            if (ids.TryGetValue(uuid, out int id)) return id;
            ids[uuid] = id = Free(uuid, ids);
            WriteIds(ids);
            return id;
        }
    }

    // ── 표에 얹는 것 ──────────────────────────────────────────────

    /// <summary>아이템 표 줄 — 그림 번호는 그림이 있으면 아이템 번호, 없으면 -1.</summary>
    private static ItemTable.Record ToRecord(Loaded l) =>
        new(l.Id, StandardText.Apply(l.Entry.Name), l.PicturePath != null ? l.Id : -1,
            Math.Max(0, l.Entry.BuyList), Math.Max(0, l.Entry.SellList), l.Entry.Effect,
            Math.Clamp(l.Entry.Category, 0, ItemTable.CategoryNames.Length - 1));

    /// <summary>아이템 표 줄들 — 번호 차례.</summary>
    public static IEnumerable<ItemTable.Record> Records => All.Select(ToRecord);

    /// <summary>그 번호의 아이템 표 줄. 없으면 null.</summary>
    public static ItemTable.Record? Record(int id) => ById(id) is { } l ? ToRecord(l) : null;

    /// <summary>그 이름의 아이템 표 줄 — 같은 이름이 여럿이면 번호가 작은 것. 없으면 null.</summary>
    public static ItemTable.Record? Record(string name) =>
        All.FirstOrDefault(l => StandardText.Apply(l.Entry.Name) == name || l.Entry.Name == name) is { } l ? ToRecord(l) : null;

    /// <summary>그 번호의 설명 글. 없으면 null.</summary>
    public static string? Description(int id) => ById(id) is { } l ? StandardText.Apply(l.Entry.Description) : null;

    /// <summary>그 그림 번호가 사람이 넣은 아이템 그림인지.</summary>
    public static bool HasPicture(int pic) => ById(pic)?.PicturePath != null;

    /// <summary>그 그림 번호의 그림 파일. 없으면 null.</summary>
    public static string? PicturePath(int pic) => ById(pic)?.PicturePath;

    // ── 장면 줄의 아이템 ───────────────────────────────────────────

    /// <summary>
    /// 발견물 장면 「[아이템]」 줄의 글을 아이템 번호로 — 수이면 앱에 실린 아이템 번호 그대로, UUID 면 이 PC 에서 받은 번호다.
    /// 그 UUID 의 아이템이 없으면(안 불러왔거나 지웠으면) null.
    /// </summary>
    public static int? Resolve(string text)
    {
        string s = text.Trim();
        if (int.TryParse(s, out int n)) return n;
        return Guid.TryParse(s, out _) && ByUuid(s) is { } l ? l.Id : null;
    }

    /// <summary>그 글이 사람이 지은 아이템(UUID)을 가리키는지.</summary>
    public static bool IsReference(string text) => Guid.TryParse(text.Trim(), out _);

    // ── 짓기 · 고치기 · 지우기 ─────────────────────────────────

    /// <summary>빈 아이템 하나 — 새 UUID 를 받는다.</summary>
    public static Entry Blank(string author = "") =>
        new(Guid.NewGuid().ToString(), "새 아이템", Category: 1, BuyList: 10000, SellList: 5000, Effect: 0, Description: "", Author: author);

    /// <summary>적는다 — 그림을 주면(120x120 BGRA) 갈아 넣고, <paramref name="dropPicture"/> 면 뺀다. 잘 됐으면 null, 아니면 까닭.</summary>
    public static string? Save(Entry entry, uint[]? picture = null, bool dropPicture = false)
    {
        try
        {
            Assign(entry.Uuid);
            string dir = Path.Combine(Folder, entry.Uuid);
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, EntryFile), JsonSerializer.Serialize(entry with { Version = FormatVersion }, Pretty));
            string png = Path.Combine(dir, PictureFile);
            if (picture is { Length: PictureWidth * PictureHeight }) SavePng(picture, png);
            else if (dropPicture && File.Exists(png)) File.Delete(png);
            Forget();
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return ex.Message; }
    }

    /// <summary>지운다 — 번호표의 줄은 남긴다(그 번호를 다른 아이템이 받지 않게).</summary>
    public static void Delete(string uuid)
    {
        try { Directory.Delete(Path.Combine(Folder, uuid), recursive: true); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        Forget();
    }

    private static void SavePng(uint[] bgra, string path)
    {
        var art = BitmapSource.Create(PictureWidth, PictureHeight, 96, 96, System.Windows.Media.PixelFormats.Bgra32, null, bgra, PictureWidth * 4);
        var png = new PngBitmapEncoder();
        png.Frames.Add(BitmapFrame.Create(art));
        using var file = File.Create(path);
        png.Save(file);
    }

    /// <summary>그림 파일을 120x120 BGRA 로 푼다. 없거나 크기가 다르면 null.</summary>
    public static uint[]? ReadPicture(string path)
    {
        try
        {
            using var fs = File.OpenRead(path);
            var decoder = new PngBitmapDecoder(fs, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            var src = new FormatConvertedBitmap(decoder.Frames[0], System.Windows.Media.PixelFormats.Bgra32, null, 0);
            if (src.PixelWidth != PictureWidth || src.PixelHeight != PictureHeight) return null;
            var bgra = new uint[PictureWidth * PictureHeight];
            src.CopyPixels(bgra, PictureWidth * 4, 0);
            return bgra;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException
                                      or ArgumentException or FileFormatException)
        {
            return null;
        }
    }

    // ── 내보내기 · 불러오기(발견물 파일에 함께) ────────────────────────

    /// <summary>그 아이템을 발견물 파일(zip)에 담는다 — <c>items/{UUID}/item.json</c> · <c>picture.png</c>. 없는 UUID 면 false.</summary>
    internal static bool WriteTo(ZipArchive zip, string uuid)
    {
        if (ByUuid(uuid) is not { } l) return false;
        zip.CreateEntryFromFile(Path.Combine(l.Folder, EntryFile), $"{ZipFolder}{uuid}/{EntryFile}");
        if (l.PicturePath is { } picture) zip.CreateEntryFromFile(picture, $"{ZipFolder}{uuid}/{PictureFile}");
        return true;
    }

    /// <summary>불러온 결과 — 새로 들인 것 · 덮은 것 · 그대로 둔 것(같은 UUID 가 이미 있어 안 덮었다).</summary>
    internal sealed record ImportResult(List<Loaded> Added, List<Loaded> Replaced, List<Loaded> Kept, List<string> Skipped);

    /// <summary>
    /// 발견물 파일(zip)에 든 아이템을 들인다. 같은 UUID 가 이미 있으면 번호는 그대로 두고, 알맹이가 다를 때만
    /// <paramref name="overwrite"/>(지금 것, 불러온 것)로 물어 덮는다. 새 UUID 는 이 PC 의 빈 번호를 받는다.
    /// </summary>
    internal static ImportResult ImportFrom(ZipArchive zip, Func<Entry, Entry, bool>? overwrite)
    {
        var result = new ImportResult([], [], [], []);
        var heads = zip.Entries.Where(z => z.FullName.StartsWith(ZipFolder, StringComparison.Ordinal)
                                           && z.FullName.EndsWith("/" + EntryFile, StringComparison.Ordinal)).ToList();
        foreach (var head in heads)
        {
            Entry? entry;
            byte[] json;
            using (var stream = head.Open())
            using (var copy = new MemoryStream())
            {
                stream.CopyTo(copy);
                json = copy.ToArray();
            }
            try { entry = JsonSerializer.Deserialize<Entry>(json); }
            catch (JsonException) { entry = null; }
            if (entry is not { Uuid.Length: > 0 } || !Guid.TryParse(entry.Uuid, out _) || entry.Version > FormatVersion
                || head.FullName != $"{ZipFolder}{entry.Uuid}/{EntryFile}")
            {
                result.Skipped.Add(head.FullName);
                continue;
            }
            var art = zip.GetEntry($"{ZipFolder}{entry.Uuid}/{PictureFile}");
            byte[]? picture = null;
            if (art != null)
            {
                using var stream = art.Open();
                using var copy = new MemoryStream();
                stream.CopyTo(copy);
                picture = copy.ToArray();
            }

            var had = ByUuid(entry.Uuid);
            if (had != null)
            {
                bool same = File.ReadAllBytes(Path.Combine(had.Folder, EntryFile)).AsSpan().SequenceEqual(json)
                            && SameBytes(had.PicturePath, picture);
                if (same || overwrite == null || !overwrite(had.Entry, entry)) { result.Kept.Add(had); continue; }
            }

            Assign(entry.Uuid);
            string dir = Path.Combine(Folder, entry.Uuid);
            Directory.CreateDirectory(dir);
            File.WriteAllBytes(Path.Combine(dir, EntryFile), json);
            string png = Path.Combine(dir, PictureFile);
            if (picture != null) File.WriteAllBytes(png, picture);
            else if (File.Exists(png)) File.Delete(png);
            Forget();
            var now = ByUuid(entry.Uuid)!;
            (had != null ? result.Replaced : result.Added).Add(now);
        }
        return result;
    }

    private static bool SameBytes(string? path, byte[]? bytes) =>
        path == null ? bytes == null : bytes != null && File.ReadAllBytes(path).AsSpan().SequenceEqual(bytes);
}
