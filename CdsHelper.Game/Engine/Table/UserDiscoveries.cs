using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows.Media.Imaging;
using CdsHelper.Game.Engine.Disev;

namespace CdsHelper.Game.Local.Helpers;

/// <summary>
/// 사람이 지어 넣은 발견물 — 원본에 없는 것이다. 앱과 함께 실리는 표(<c>exe-tables/*-고친것.json</c>)에 섞지 않고
/// <c>%APPDATA%\CdsHelper\discoveries\{UUID}</c> 에 한 벌씩 두었다가 표를 읽을 때 얹는다 — 업데이트해도 남는다.
/// </summary>
/// <remarks>
/// 한 벌은 발견물 줄 · 힌트 · 술집 소문 · 책 · 설명 · 발견 장면 · 그림 · 출처를 다 든다. 번호는 들고 다니지 않는다.
/// <code>
///   신분     UUID — 지을 때 한 번 정하고 안 바뀐다. 같은 UUID 를 또 불러오면 새 판으로 덮는다
///   번호     발견물 · 일련번호 · 힌트 · 그림 번호를 UUID 로 셈해 2000~9999 에서 받는다(겹치면 다음 칸)
///            — 셈이 같아 PC 가 달라도 거의 늘 같은 번호라, 세이브를 옮겨도 찾은 기록이 맞는다
///   장면     대사 줄(Lines)로 짓고 발견 대본 파트로 굽는다. 대본 속 「이 발견물」 · 「이 그림」은 -1 로 적어 두고 구울 때 번호를 넣는다
/// </code>
/// 모드 「추가 발견물」을 켰을 때만 낸다(원본에 없던 줄과 같다).
/// </remarks>
public static class UserDiscoveries
{
    /// <summary>번호를 받는 구간 — 앱에 실린 추가 발견물(274~)과 원본 번호들보다 한참 위다.</summary>
    public const int NumberBase = 2000, NumberEnd = 9999;

    /// <summary>파일 모양 판.</summary>
    public const int FormatVersion = 1;

    /// <summary>장면 한 줄.</summary>
    /// <param name="Kind">「say」 대사 · 「picture」 발견물 그림 · 「found」 효과음과 「~을 발견했다！」, 발견 처리.</param>
    /// <param name="Speaker">대사 화자(부관 · 감찰관 · 항해사 …). 비우면 화자 없는 글.</param>
    public sealed record SceneLine(string Kind, string? Speaker = null, string Text = "");

    /// <summary>사람이 지은 발견물 한 벌 — 번호 없이 들고 다니는 모양.</summary>
    /// <param name="Category">발견물 갈래(<see cref="DiscoveryTable.CategoryNames"/>).</param>
    /// <param name="X1">세계 지도 칸 범위.</param>
    /// <param name="Chance">발견 확률 분모 — 1 이면 늘, 3 이면 세 번에 한 번.</param>
    /// <param name="Book">힌트를 실을 책 제목(박물지 따위). 비우면 책에 안 싣는다.</param>
    /// <param name="RumorCities">소문을 들려줄 도시 번호 셋까지.</param>
    /// <param name="Credit">그림 · 글의 출처와 라이선스.</param>
    public sealed record Entry(
        string Uuid, string Name, int Category, int Reward, bool OnLand, bool OpenAtStart,
        int X1, int Y1, int X2, int Y2,
        string HintName, int HintGrade, int HintCategory, int HintFunds, int HintDeadline, string HintText,
        int[] RumorCities, string RumorText, string Book, string Description,
        int Chance, List<SceneLine> Lines, string Credit = "", string Author = "", int Version = FormatVersion);

    /// <summary>이 PC 에서 받은 번호.</summary>
    public readonly record struct Numbers(int Id, int Serial, int Hint, int Picture);

    /// <summary>읽어 둔 한 벌 — 알맹이와 받은 번호.</summary>
    public sealed record Loaded(Entry Entry, Numbers Numbers, string Folder)
    {
        /// <summary>그림 파일(320x240 png). 없으면 null.</summary>
        public string? PicturePath => File.Exists(Path.Combine(Folder, PictureFile)) ? Path.Combine(Folder, PictureFile) : null;
    }

    private const string EntryFile = "discovery.json", PictureFile = "picture.png";

    /// <summary>모아 둔 자리.</summary>
    public static string Folder =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CdsHelper", "discoveries");

    private static readonly JsonSerializerOptions Pretty = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private static List<Loaded>? _all;
    private static Dictionary<int, Loaded>? _byId, _bySerial, _byHint, _byPicture;
    private static readonly Dictionary<int, byte[]> SceneCache = [];

    /// <summary>더하거나 고치거나 지웠을 때 — 표를 들고 있던 쪽이 다시 읽는다.</summary>
    public static event Action? Changed;

    /// <summary>모드 「추가 발견물」을 켰을 때만 낸다.</summary>
    private static bool On => Settings.GameSettings.ExtraDiscoveries;

    /// <summary>지어 둔 것 전부(모드와 상관없이).</summary>
    public static IReadOnlyList<Loaded> All
    {
        get
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

            // 번호는 UUID 차례로 받는다 — 셈이 겹칠 때 누가 다음 칸으로 밀릴지가 PC 마다 같게.
            var taken = new[] { new HashSet<int>(), new HashSet<int>(), new HashSet<int>(), new HashSet<int>() };
            var loaded = new List<Loaded>();
            foreach (var (e, dir) in entries.OrderBy(x => x.Item1.Uuid, StringComparer.Ordinal))
            {
                int Take(int kind)
                {
                    int span = NumberEnd - NumberBase + 1;
                    int n = NumberBase + (int)(BitConverter.ToUInt32(SHA256.HashData(Encoding.UTF8.GetBytes($"{e.Uuid}:{kind}")), 0) % (uint)span);
                    while (!taken[kind].Add(n)) n = n >= NumberEnd ? NumberBase : n + 1;
                    return n;
                }
                loaded.Add(new Loaded(e, new Numbers(Take(0), Take(1), Take(2), Take(3)), dir));
            }
            _all = loaded;
            _byId = loaded.ToDictionary(l => l.Numbers.Id);
            _bySerial = loaded.ToDictionary(l => l.Numbers.Serial);
            _byHint = loaded.ToDictionary(l => l.Numbers.Hint);
            _byPicture = loaded.ToDictionary(l => l.Numbers.Picture);
            return _all;
        }
    }

    private static void Forget()
    {
        _all = null;
        _byId = _bySerial = _byHint = _byPicture = null;
        lock (SceneCache) SceneCache.Clear();
        Changed?.Invoke();
    }

    private static Loaded? ById(int id) { _ = All; return On && _byId!.TryGetValue(id, out var l) ? l : null; }

    // ── 표에 얹는 것 ──────────────────────────────────────────────

    /// <summary>발견물 표 줄들(모드를 켰을 때).</summary>
    public static IEnumerable<DiscoveryTable.Record> Records => On ? All.Select(ToRecord) : [];

    /// <summary>그 번호의 발견물 줄. 없으면 null.</summary>
    public static DiscoveryTable.Record? Record(int id) => ById(id) is { } l ? ToRecord(l) : null;

    private static DiscoveryTable.Record ToRecord(Loaded l)
    {
        var e = l.Entry;
        return new DiscoveryTable.Record(l.Numbers.Id, e.Name, e.Category, l.Numbers.Serial, e.Reward, -1,
            Indirect: false, e.OpenAtStart, e.OnLand, Once: false,
            Math.Min(e.X1, e.X2), Math.Min(e.Y1, e.Y2), Math.Max(e.X1, e.X2), Math.Max(e.Y1, e.Y2),
            Picture: l.PicturePath != null ? l.Numbers.Picture : -1, Movie: -1,
            Erase: [65535, 65535, 65535, 65535], SoldIn: -1, Clip: -1);
    }

    /// <summary>힌트들(모드를 켰을 때).</summary>
    public static IEnumerable<HintTable.Hint> Hints => On ? All.Select(ToHint) : [];

    /// <summary>그 번호의 힌트. 없으면 null.</summary>
    public static HintTable.Hint? Hint(int id)
    {
        _ = All;
        return On && _byHint!.TryGetValue(id, out var l) ? ToHint(l) : null;
    }

    private static HintTable.Hint ToHint(Loaded l)
    {
        var e = l.Entry;
        return new HintTable.Hint(l.Numbers.Hint, e.HintName, Math.Clamp(e.HintGrade, 1, 3), e.HintCategory,
                                  Math.Max(0, e.HintFunds), Math.Max(1, e.HintDeadline), l.Numbers.Serial, e.HintText);
    }

    /// <summary>술집 소문들(모드를 켰을 때). 소문 글이 빈 것은 안 낸다.</summary>
    public static IEnumerable<RumorTable.Rumor> Rumors =>
        On ? All.Where(l => l.Entry.RumorText.Length > 0)
                .Select(l => new RumorTable.Rumor(l.Numbers.Serial, Cities(l.Entry.RumorCities), l.Entry.RumorText)) : [];

    /// <summary>소문 도시 넷 칸 — 원본 줄처럼 남는 칸은 -1.</summary>
    private static int[] Cities(int[]? cities)
    {
        var four = new[] { -1, -1, -1, -1 };
        for (int i = 0; i < Math.Min(4, cities?.Length ?? 0); i++) four[i] = cities![i];
        return four;
    }

    /// <summary>그 책(제목)에 실을 힌트 번호들(모드를 켰을 때).</summary>
    public static IEnumerable<int> HintsInBook(string title) =>
        On ? All.Where(l => l.Entry.Book.Length > 0 && l.Entry.Book == title).Select(l => l.Numbers.Hint) : [];

    /// <summary>그 번호의 설명 글. 없으면 null.</summary>
    public static string? Description(int id) => ById(id)?.Entry.Description;

    /// <summary>그 그림 번호의 그림 파일. 없으면 null.</summary>
    public static string? PicturePath(int picture)
    {
        _ = All;
        return On && _byPicture!.TryGetValue(picture, out var l) ? l.PicturePath : null;
    }

    /// <summary>그 번호의 발견 대본 파트(구운 바이트). 없으면 null.</summary>
    public static byte[]? ScenePart(int id)
    {
        if (ById(id) is not { } l) return null;
        lock (SceneCache)
        {
            if (!SceneCache.TryGetValue(id, out var bytes))
                SceneCache[id] = bytes = Compile(l, out _) ?? [];
            return bytes.Length > 0 ? (byte[])bytes.Clone() : null;
        }
    }

    // ── 장면 ──────────────────────────────────────────────────

    /// <summary>
    /// 장면 줄을 발견 대본 파트로 굽는다 — 여우원숭이(275)와 같은 꼴이다: 확률 조건 덩이 하나, 몸통 덩이 하나.
    /// </summary>
    public static byte[]? Compile(Loaded l, out string error)
    {
        var e = l.Entry;
        JsonObject Say(string text, string? speaker)
        {
            var args = new JsonObject();
            if (!string.IsNullOrWhiteSpace(speaker)) args["Speaker"] = speaker.Trim();
            args["Text"] = text;
            return new JsonObject { ["OpCode"] = "00 0A", ["Call"] = "Say", ["Args"] = args };
        }
        JsonObject End() => new() { ["OpCode"] = "FF", ["Call"] = "End" };

        var body = new JsonArray();
        foreach (var line in e.Lines ?? [])
            switch (line.Kind)
            {
                case "picture" when l.PicturePath != null:
                    body.Add(new JsonObject { ["OpCode"] = "00 01", ["Call"] = "ShowDStill", ["Args"] = new JsonObject { ["Id"] = l.Numbers.Picture } });
                    break;
                case "found":
                    body.Add(new JsonObject { ["OpCode"] = "0E 03", ["Call"] = "PlaySound", ["Args"] = new JsonObject { ["Id"] = 75 } });
                    body.Add(Say(line.Text.Length > 0 ? line.Text : $"{e.Name}{Josa(e.Name)} 발견했다！", null));
                    body.Add(new JsonObject { ["OpCode"] = "01 0B", ["Call"] = "Discover", ["Args"] = new JsonObject { ["Discovery"] = l.Numbers.Id } });
                    break;
                case "say" when line.Text.Length > 0:
                    body.Add(Say(line.Text, line.Speaker));
                    break;
            }
        // 「found」 줄이 없으면 끝에 붙인다 — 발견이 안 적히면 영영 안 찾은 것으로 남는다.
        if (!(e.Lines ?? []).Any(x => x.Kind == "found"))
        {
            body.Add(new JsonObject { ["OpCode"] = "0E 03", ["Call"] = "PlaySound", ["Args"] = new JsonObject { ["Id"] = 75 } });
            body.Add(Say($"{e.Name}{Josa(e.Name)} 발견했다！", null));
            body.Add(new JsonObject { ["OpCode"] = "01 0B", ["Call"] = "Discover", ["Args"] = new JsonObject { ["Discovery"] = l.Numbers.Id } });
        }
        body.Add(new JsonObject { ["OpCode"] = "4C", ["Call"] = "EndDone" });
        body.Add(End());

        var cond = new JsonArray
        {
            new JsonObject
            {
                ["OpCode"] = "2E 1A", ["Call"] = "RandomChance",
                ["Args"] = new JsonObject { ["Denominator"] = Math.Max(1, e.Chance), ["Success"] = 1 },
            },
            End(),
        };
        var part = new JsonObject
        {
            ["Index"] = l.Numbers.Id,
            ["Step"] = l.Numbers.Id,
            ["Slots"] = new JsonArray { new JsonObject { ["Condition"] = 0, ["Body"] = 1 } },
            ["Chunks"] = new JsonArray { cond, body },
        };
        try
        {
            if (part.Deserialize<DisevBook.Entry>() is not { } entry) { error = "장면을 못 읽었습니다"; return null; }
            return DisevBook.Compile(entry, out error);
        }
        catch (JsonException ex) { error = ex.Message; return null; }
    }

    /// <summary>받침이 있으면 「을」, 없으면 「를」.</summary>
    private static string Josa(string name) =>
        name.Length > 0 && name[^1] is >= '가' and <= '힣' && (name[^1] - '가') % 28 != 0 ? "을" : "를";

    // ── 짓기 · 고치기 · 지우기 ─────────────────────────────────

    /// <summary>빈 발견물 하나 — 새 UUID 를 받는다.</summary>
    public static Entry Blank(string author = "") => new(
        Guid.NewGuid().ToString(), "새 발견물", Category: 6, Reward: 30000, OnLand: true, OpenAtStart: true,
        X1: -1, Y1: -1, X2: -1, Y2: -1,
        HintName: "새 힌트", HintGrade: 2, HintCategory: 5, HintFunds: 30000, HintDeadline: 4, HintText: "",
        RumorCities: [], RumorText: "", Book: "박물지", Description: "",
        Chance: 3,
        Lines:
        [
            new("say", "부관", "제독！ 저기를 보십시오．"),
            new("picture"),
            new("found"),
        ],
        Author: author);

    /// <summary>적는다 — 그림을 주면 그림도 갈아 넣는다(320x240 BGRA).</summary>
    public static string? Save(Entry entry, (uint[] Bgra, int Width, int Height)? picture = null)
    {
        try
        {
            string dir = Path.Combine(Folder, entry.Uuid);
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, EntryFile), JsonSerializer.Serialize(entry with { Version = FormatVersion }, Pretty));
            if (picture is var (bgra, w, h)) SavePng(bgra, w, h, Path.Combine(dir, PictureFile));
            Forget();
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return ex.Message; }
    }

    /// <summary>지운다.</summary>
    public static void Delete(string uuid)
    {
        try { Directory.Delete(Path.Combine(Folder, uuid), recursive: true); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        Forget();
    }

    private static void SavePng(uint[] bgra, int width, int height, string path)
    {
        var art = BitmapSource.Create(width, height, 96, 96, System.Windows.Media.PixelFormats.Bgra32, null, bgra, width * 4);
        var png = new PngBitmapEncoder();
        png.Frames.Add(BitmapFrame.Create(art));
        using var file = File.Create(path);
        png.Save(file);
    }

    // ── 내보내기 · 불러오기 ─────────────────────────────────────

    /// <summary>한 벌을 <c>.cdsdiscovery</c>(zip) 하나로 내보낸다. 잘 됐으면 null, 아니면 까닭.</summary>
    public static string? Export(string uuid, string path)
    {
        if (All.FirstOrDefault(l => l.Entry.Uuid == uuid) is not { } l) return "그 발견물이 없습니다";
        try
        {
            if (File.Exists(path)) File.Delete(path);
            using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
            zip.CreateEntryFromFile(Path.Combine(l.Folder, EntryFile), EntryFile);
            if (l.PicturePath is { } picture) zip.CreateEntryFromFile(picture, PictureFile);
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException) { return ex.Message; }
    }

    /// <summary>불러온다 — 같은 UUID 가 있으면 <paramref name="overwrite"/> 로 물어 덮는다. 불러온 것(못 했으면 null)과 알릴 말.</summary>
    public static (Entry? Entry, string Message) Import(string path, Func<Entry, Entry, bool> overwrite)
    {
        try
        {
            using var zip = ZipFile.OpenRead(path);
            if (zip.GetEntry(EntryFile) is not { } head) return (null, "발견물 파일이 아닙니다(discovery.json 이 없습니다).");
            Entry? entry;
            using (var stream = head.Open()) entry = JsonSerializer.Deserialize<Entry>(stream);
            if (entry is not { Uuid.Length: > 0 } || !Guid.TryParse(entry.Uuid, out _)) return (null, "discovery.json 을 읽지 못했습니다.");
            if (entry.Version > FormatVersion) return (null, "더 새 버전의 게임에서 만든 발견물입니다. 게임을 업데이트해 주세요.");

            if (All.FirstOrDefault(l => l.Entry.Uuid == entry.Uuid) is { } had && !overwrite(had.Entry, entry))
                return (null, "불러오지 않았습니다.");

            string dir = Path.Combine(Folder, entry.Uuid);
            Directory.CreateDirectory(dir);
            head.ExtractToFile(Path.Combine(dir, EntryFile), overwrite: true);
            string picture = Path.Combine(dir, PictureFile);
            if (zip.GetEntry(PictureFile) is { } art) art.ExtractToFile(picture, overwrite: true);
            else if (File.Exists(picture)) File.Delete(picture);
            Forget();
            var numbers = All.First(l => l.Entry.Uuid == entry.Uuid).Numbers;
            return (entry, $"「{entry.Name}」 을 불러왔습니다 — 발견물 {numbers.Id}번.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException
                                      or JsonException or NotSupportedException)
        {
            return (null, $"불러오지 못했습니다 — {ex.Message}");
        }
    }
}
