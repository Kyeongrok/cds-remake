using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace CdsHelper.Game.Local.Helpers;

/// <summary>
/// 표 한 벌을 JSON 으로 적어 두고 다시 읽는 자리. 무엇을 적을지는 부르는 쪽이 정하고,
/// 여기서는 파일 다루는 일만 한다.
/// </summary>
/// <remarks>
/// <see cref="ExeTable"/>(정적으로 넣은 표)와 <see cref="CityTable"/>(앱 DB 에서 굽는 표)이
/// 같이 쓴다.
/// </remarks>
internal static class TableCache
{
    // 한글이 \uXXXX 로 깨져 보이지 않게 그대로 적는다(사람이 열어 볼 파일이다).
    private static readonly JsonSerializerOptions Pretty = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>정적으로 넣은 표 자리. 개발 때는 저장소 루트, 배포 때는 실행 파일 옆이다.</summary>
    public static string Folder
    {
        get
        {
            for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
            {
                string candidate = Path.Combine(dir.FullName, "exe-tables");
                if (Directory.Exists(candidate)) return candidate;
            }

            return Path.Combine(AppContext.BaseDirectory, "exe-tables");
        }
    }

    /// <summary>
    /// 표 이름(코드가 쓰는 한국어 열쇠) → 파일 이름(확장자 뺀 ASCII). 파일 이름은 ASCII 로만 짓는다.
    /// </summary>
    /// <remarks>
    /// 한글 파일 이름이 바뀌면 Velopack 차등 업데이트가 깨졌다 — 릴리즈를 굽는 GitHub 윈도 러너는 ANSI 코드 페이지가
    /// 1252 라 vpk 가 부르는 zstd.exe 가 한글 경로를 못 열어(「Stat failed on dictionary file ?????.json」) 옛 bsdiff 꼴로
    /// 물러서는데, 깔린 쪽 Update.exe 는 그 꼴을 못 읽어 「Unsupported patch format」으로 차등을 버리고 160MB 통짜를
    /// 받았다. 게임을 그 사이에 끄면 받다 만 채로 끝나 업데이트가 안 되는 것처럼 보였다(제보, v1.1.19 → v1.1.21).
    /// 여기 없는 이름이 한글이면 UTF-8 16진으로 바꾼다(<see cref="FileNameOf"/>) — 도구 앱이 짓는 시나리오 책 따위.
    /// </remarks>
    private static readonly Dictionary<string, string> FileNames = new()
    {
        ["건물사진표"] = "building-photos", ["건물표"] = "buildings", ["경쟁자-고친것"] = "voyagers-edits",
        ["교역소표"] = "trade-posts", ["교역품표"] = "goods", ["나라표"] = "nations", ["나라-고친것"] = "nations-edits",
        ["도시-문화권-고친것"] = "city-cultures-edits", ["도시-왕국-고친것"] = "city-nations-edits",
        ["도시표-게임"] = "cities-exe", ["도시표"] = "cities", ["돛효율표"] = "sails", ["마을대사"] = "town-lines",
        ["마을사람표"] = "town-folk", ["바람표"] = "winds", ["발견물-고친것"] = "discoveries-edits",
        ["발견물설명"] = "discovery-descriptions", ["발견물표"] = "discoveries", ["발견이벤트"] = "disev",
        ["소문-고친것"] = "rumors-added", ["소문표"] = "rumors", ["손님표"] = "tavern-guests", ["술표"] = "drinks",
        ["시나리오목록"] = "scenarios", ["시장물건"] = "market-goods", ["아이템설명"] = "item-descriptions", ["아이템-더한것"] = "items-added",
        ["아이템표"] = "items", ["여급표"] = "barmaids", ["여급-고친것"] = "barmaids-edits", ["역사대본"] = "history-events", ["역사소문"] = "history-rumors",
        ["역사항해자"] = "history-voyagers", ["이름표"] = "player-names", ["이야기0"] = "story0", ["이야기1"] = "story1",
        ["인물밑표"] = "person-templates", ["인물표"] = "persons", ["인물표-고친것"] = "persons-edits", ["작위"] = "nobility", ["제노바저항"] = "genoa-resistance",
        ["지형표"] = "terrain", ["책-고친것"] = "books-edits", ["책표"] = "books", ["화자이름표"] = "speaker-names",
        ["화자표"] = "speakers", ["후원자표"] = "sponsors", ["후원자-더한것"] = "sponsors-added", ["후원자-고친것"] = "sponsors-edits", ["힌트-고친것"] = "hints-edits", ["힌트표"] = "hints",
        ["선수상-고친것"] = "figureheads-edits", ["묘책확률-고친것"] = "ruses-edits", ["패시브"] = "passives",
        ["부대편성-고친것"] = "land-formations-edits", ["초상화-중년짝"] = "portrait-ages", ["선체표"] = "hulls",
    };

    /// <summary>시나리오 책(<c>시나리오책{번호}</c>)의 파일 이름 앞머리.</summary>
    private const string ScenarioBook = "시나리오책";

    /// <summary>그 표가 적히는 파일 이름(확장자 뺀 것) — 늘 ASCII 다.</summary>
    public static string FileNameOf(string name)
    {
        if (FileNames.TryGetValue(name, out var file)) return file;
        if (name.StartsWith(ScenarioBook, StringComparison.Ordinal) && int.TryParse(name.AsSpan(ScenarioBook.Length), out int n))
            return $"scenario-book{n}";
        return name.All(char.IsAscii) ? name : "u-" + Convert.ToHexString(System.Text.Encoding.UTF8.GetBytes(name)).ToLowerInvariant();
    }

    /// <summary>파일 이름(확장자 뺀 ASCII)을 표 이름(한국어 열쇠)으로 되짚는다 — <see cref="FileNameOf"/> 의 거꾸로.</summary>
    public static string NameOfFile(string file)
    {
        foreach (var (name, ascii) in FileNames)
            if (string.Equals(ascii, file, StringComparison.OrdinalIgnoreCase)) return name;
        if (file.StartsWith("scenario-book", StringComparison.Ordinal) && int.TryParse(file.AsSpan("scenario-book".Length), out int n))
            return ScenarioBook + n;
        if (file.StartsWith("u-", StringComparison.Ordinal))
        {
            try { return System.Text.Encoding.UTF8.GetString(Convert.FromHexString(file[2..])); }
            catch (FormatException) { }
        }
        return file;
    }

    /// <summary>
    /// 그 표의 파일 자리 — ASCII 이름이다. 켠 커스텀 팩에 그 표가 있으면 팩 파일을 낸다(기본 표에 얹는 것은
    /// <see cref="Read{T}"/> 가 한다). 그 파일이 없고 예전 한글 이름 파일만 있으면(이름을 바꾸기 전에 도구 앱이
    /// 적어 둔 것) 그쪽을 낸다 — 다음에 적을 때 ASCII 이름으로 옮겨 간다(<see cref="Write{T}"/>).
    /// </summary>
    public static string PathFor(string name) =>
        PackFileOf(name) is { } pack && File.Exists(pack) ? pack : BasePathFor(name);

    /// <summary>그 표의 <b>기본 자리</b>(<see cref="Folder"/>) 파일 — 켠 커스텀 팩은 안 본다.</summary>
    public static string BasePathFor(string name)
    {
        string path = Path.Combine(Folder, FileNameOf(name) + ".json");
        if (File.Exists(path)) return path;
        string legacy = Path.Combine(Folder, name + ".json");
        return legacy != path && File.Exists(legacy) ? legacy : path;
    }

    // ── 커스텀 팩 층 ────────────────────────────────────────────────────────────
    //
    // 개발도구로 고친 값(「-고친것」 표)을 팩 폴더(%APPDATA%\CdsHelper\packs\<이름>, CustomPacks)에 따로 담는다. 기본 자리는
    // 설치 폴더(…\current\exe-tables)라 업데이트가 통째로 갈아 끼우지만, 팩 폴더는 그대로 남는다.
    //
    //   읽기   켠 팩에 그 표가 있으면 기본 자리 표 위에 팩 줄을 열쇠(Id · Index · City …)로 얹는다 — 팩에 없는 줄은 기본 값이다.
    //   쓰기   팩이 켜져 있고 「-고친것」 표면 기본 자리 표와 <b>다른 줄만</b> 팩에 적는다(업데이트로 고쳐진 다른 줄은 받는다).
    //   판     팩 파일의 Version 이 기본 자리 표와 다르면 그 표만 건너뛰고(기본 값) 알린다(SkippedPacks).

    /// <summary>켠 커스텀 팩 폴더. null 이면 팩 없이 기본 자리만 본다.</summary>
    public static string? PackFolder { get; private set; }

    /// <summary>켠 팩이 바뀌었다 — 고친 것을 한 번 읽어 담아 둔 쪽(<c>NationEdits</c> 따위)이 담은 것을 버린다.</summary>
    public static event Action? Reloaded;

    /// <summary>판(Version)이 안 맞아 건너뛴 팩 표 — 표 이름 → 까닭.</summary>
    private static readonly Dictionary<string, string> Skipped = new(StringComparer.Ordinal);

    /// <summary>판이 안 맞아 건너뛴 팩 표들(「표 이름: 까닭」).</summary>
    public static IReadOnlyList<string> SkippedPacks
    {
        get { lock (Skipped) return [.. Skipped.Select(p => $"{p.Key}: {p.Value}")]; }
    }

    /// <summary>팩을 켠다(null 이면 끈다). 담아 둔 고친 것들을 버리게 알린다.</summary>
    public static void UsePack(string? folder)
    {
        PackFolder = string.IsNullOrEmpty(folder) ? null : folder;
        lock (Skipped) Skipped.Clear();
        Reloaded?.Invoke();
    }

    /// <summary>
    /// 팩에 담기는 표인지 — 개발도구가 고친 것을 적는 「-고친것」 표다(ASCII 파일 이름은 <c>*-edits.json</c>, 소문은
    /// <c>rumors-added.json</c>). 대본(발견이벤트 · 이야기) · 시나리오 · 패시브 · 초상화 짝은 아직 기본 자리에 적는다.
    /// </summary>
    public static bool Packable(string name) => name.EndsWith("-고친것", StringComparison.Ordinal);

    /// <summary>켠 팩 안에서 그 표가 적힐 자리. 팩이 꺼져 있으면 null.</summary>
    public static string? PackFileOf(string name) =>
        PackFolder == null ? null : Path.Combine(PackFolder, FileNameOf(name) + ".json");

    /// <summary>팩 줄을 기본 줄에 맞대는 열쇠 칸 — 줄 목록마다 처음으로 모든 줄에 있는 것을 쓴다.</summary>
    private static readonly string[] RowKeys = ["Id", "Index", "City", "Voyager", "Set", "Shape", "Face"];

    /// <summary>기본 자리의 표만 읽는다 — 켠 팩은 안 본다. 없거나 깨졌으면 null.</summary>
    public static Cached<T>? ReadBase<T>(string name) where T : class => ReadFile<T>(BasePathFor(name), name);

    /// <summary>켠 팩에 든 그 표만 읽는다(기본 표에 안 얹는다). 팩이 꺼져 있거나 없으면 null.</summary>
    public static Cached<T>? ReadPack<T>(string name) where T : class =>
        PackFileOf(name) is { } pack && File.Exists(pack) ? ReadFile<T>(pack, name) : null;

    /// <summary>켠 팩에 그 표를 그대로 적는다(기본 표와 견주지 않는다 — 부르는 쪽이 고친 줄만 골라 준다).</summary>
    public static void WritePack<T>(string name, Cached<T> cached) where T : class
    {
        if (PackFileOf(name) is not { } pack) return;
        WriteText(pack, name, JsonSerializer.Serialize(cached, Pretty));
    }

    /// <summary>켠 팩에 든 그 표를 지운다 — 다음에 읽으면 기본 값이다.</summary>
    public static void DeletePack(string name)
    {
        try
        {
            if (PackFileOf(name) is { } pack && File.Exists(pack)) File.Delete(pack);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            System.Diagnostics.Debug.WriteLine($"[TableCache] 팩의 {name} 을 지우지 못했습니다: {ex.Message}");
        }
    }

    /// <summary>
    /// 팩 파일 하나가 기본 자리 표와 판이 맞는지. 기본 표가 없으면(인물표처럼 본이 딴 자리에 있는 것) 따지지 않고 맞다고 본다.
    /// </summary>
    /// <returns>안 맞으면 까닭, 맞으면 null.</returns>
    public static string? CheckPackFile(string name, string packPath)
    {
        try
        {
            if (JsonNode.Parse(File.ReadAllText(packPath)) is not JsonObject pack) return "읽을 수 없는 파일입니다";
            string basePath = BasePathFor(name);
            if (!File.Exists(basePath)) return null;
            if (JsonNode.Parse(File.ReadAllText(basePath)) is not JsonObject root) return null;
            int want = VersionOf(root), got = VersionOf(pack);
            return want == got ? null : $"표 형식이 다릅니다(팩 {got} · 게임 {want})";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return ex.Message;
        }
    }

    /// <summary>팩 표 하나를 건너뛰었다고 적어 둔다 — 제 판을 따로 따지는 표(인물표)가 부른다.</summary>
    public static void NoteSkipped(string name, string why)
    {
        lock (Skipped) Skipped[name] = why;
    }

    private static int VersionOf(JsonObject root) =>
        root["Version"] is JsonValue v && v.TryGetValue(out int version) ? version : 1;

    /// <summary>기본 표 위에 팩 표를 얹은 글. 팩이 없거나 건너뛰면 null.</summary>
    private static string? Layered(string name)
    {
        if (PackFileOf(name) is not { } packPath || !File.Exists(packPath)) return null;
        lock (Skipped) if (Skipped.ContainsKey(name)) return null;

        try
        {
            if (JsonNode.Parse(File.ReadAllText(packPath)) is not JsonObject pack) return Skip("읽을 수 없는 파일입니다");
            string basePath = BasePathFor(name);
            if (!File.Exists(basePath) || JsonNode.Parse(File.ReadAllText(basePath)) is not JsonObject root)
                return pack.ToJsonString();
            if (VersionOf(root) != VersionOf(pack))
                return Skip($"표 형식이 다릅니다(팩 {VersionOf(pack)} · 게임 {VersionOf(root)})");

            if (pack["Data"] is JsonObject packData && root["Data"] is JsonObject baseData)
                root["Data"] = Overlay(baseData, packData);
            else if (pack["Data"] != null)
                root["Data"] = pack["Data"]!.DeepClone();
            return root.ToJsonString();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return Skip(ex.Message);
        }

        string? Skip(string why)
        {
            lock (Skipped) Skipped[name] = why;
            System.Diagnostics.Debug.WriteLine($"[TableCache] 팩의 {name} 을 건너뜁니다 — {why}");
            return null;
        }
    }

    /// <summary>알맹이 칸마다 — 줄 목록이면 열쇠로 줄 단위로 얹고, 그 밖은 팩 것으로 갈아 끼운다.</summary>
    private static JsonObject Overlay(JsonObject baseData, JsonObject packData)
    {
        var merged = (JsonObject)baseData.DeepClone();
        foreach (var (field, value) in packData)
        {
            if (value is JsonArray packRows && merged[field] is JsonArray baseRows && KeyOf(baseRows, packRows) is { } key)
            {
                var rows = baseRows.Select(r => r?.DeepClone()).ToList();
                foreach (var row in packRows)
                {
                    string id = row![key]!.ToJsonString();
                    int at = rows.FindIndex(r => r?[key]?.ToJsonString() == id);
                    if (at >= 0) rows[at] = row.DeepClone();
                    else rows.Add(row.DeepClone());
                }
                merged[field] = new JsonArray([.. rows]);
            }
            else merged[field] = value?.DeepClone();
        }
        return merged;
    }

    /// <summary>두 줄 목록이 다 가진 열쇠 칸. 없으면 null(팩 목록이 통째로 갈음한다).</summary>
    private static string? KeyOf(JsonArray a, JsonArray b) =>
        RowKeys.FirstOrDefault(key => a.Concat(b).All(r => r is JsonObject o && o[key] is JsonValue));

    /// <summary>기본 자리 표와 같은 줄을 걷어 낸 알맹이 — 팩에는 고친 줄만 남는다.</summary>
    private static JsonNode Diff(string name, JsonObject written)
    {
        try
        {
            string basePath = BasePathFor(name);
            if (!File.Exists(basePath) || JsonNode.Parse(File.ReadAllText(basePath)) is not JsonObject root
                || root["Data"] is not JsonObject baseData || written["Data"] is not JsonObject data)
                return written;
            foreach (var (field, value) in data.ToList())
            {
                if (value is not JsonArray rows || baseData[field] is not JsonArray baseRows
                    || KeyOf(baseRows, rows) is not { } key) continue;
                var byKey = new Dictionary<string, JsonNode>();
                foreach (var r in baseRows) if (r != null) byKey[r[key]!.ToJsonString()] = r;
                var changed = rows.Where(r => r != null && !(byKey.TryGetValue(r[key]!.ToJsonString(), out var b)
                                                            && JsonNode.DeepEquals(b, r)))
                                  .Select(r => r!.DeepClone()).ToArray();
                data[field] = new JsonArray(changed);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            System.Diagnostics.Debug.WriteLine($"[TableCache] {name} 을 기본 표와 견주지 못했습니다: {ex.Message}");
        }
        return written;
    }

    /// <summary>
    /// 적혀 있는 표 가운데 이름이 그 꼬리로 끝나는 것들(표 이름, 한국어 열쇠) — 도구 앱이 고쳐 둔 「-고친것」 따위.
    /// 파일 이름이 ASCII 라 파일 목록에서 바로 못 읽으므로 이름 표와 예전 한글 파일을 함께 본다.
    /// </summary>
    public static IEnumerable<string> NamesEndingWith(string suffix)
    {
        var names = new SortedSet<string>(StringComparer.Ordinal);
        foreach (string name in FileNames.Keys)
            if (name.EndsWith(suffix, StringComparison.Ordinal) && File.Exists(PathFor(name))) names.Add(name);
        try
        {
            if (Directory.Exists(Folder))
                foreach (string file in Directory.GetFiles(Folder, "*" + suffix + ".json"))
                    names.Add(Path.GetFileNameWithoutExtension(file));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        return names;
    }

    /// <summary>적을 자리 — 늘 ASCII 이름이다.</summary>
    private static string WritePathFor(string name) => Path.Combine(Folder, FileNameOf(name) + ".json");

    /// <summary>지금까지 적어 둔 파일 전부. 하나도 없으면 빈 목록.</summary>
    public static IReadOnlyList<string> Saved()
    {
        try
        {
            return Directory.Exists(Folder)
                ? Directory.GetFiles(Folder, "*.json").OrderBy(p => p).ToList()
                : [];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    /// <summary>
    /// 적어 둔 한 벌.
    /// </summary>
    /// <param name="Stamp">원본이 그때와 같은지 가리는 표. 무엇을 담을지는 부르는 쪽이 정한다.</param>
    /// <param name="Data">알맹이.</param>
    /// <param name="Source">
    /// 어디서 구웠는지("CDS_95.EXE", "cdshelper.db" …). 게임데이터 창이 이것을 보여 준다.
    /// 뒤에 붙인 항목이라 기본값을 둔다 — 이것이 없던 때 적어 둔 파일도 그대로 읽힌다.
    /// </param>
    /// <param name="Version">
    /// 알맹이의 <b>모양</b> 판. 표에 칸을 더하면 부르는 쪽이 이 값을 올린다 — 그러면 옛
    /// 모양으로 적어 둔 파일은 버리고 다시 굽는다.
    /// <para>
    /// 이것이 없으면 칸을 더해도 도장(EXE 크기·고친 때)이 그대로라 옛 파일을 계속 쓰게 되고,
    /// 새 칸이 비어 들어와 엉뚱한 자리에서 터진다.
    /// </para>
    /// </param>
    public sealed record Cached<T>(string Stamp, T Data, string Source = "", int Version = 1)
        where T : class;

    /// <summary>
    /// 적어 둔 것을 읽는다. 켠 커스텀 팩에 그 표가 있으면 기본 자리 표 위에 줄 단위로 얹어 낸다. 없거나 깨졌으면 null.
    /// </summary>
    public static Cached<T>? Read<T>(string name) where T : class
    {
        if (Layered(name) is { } layered && Parse<T>(layered, name) is { } merged) return merged;
        return ReadBase<T>(name);
    }

    private static Cached<T>? ReadFile<T>(string path, string name) where T : class
    {
        try
        {
            return File.Exists(path) ? Parse<T>(File.ReadAllText(path), name) : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static Cached<T>? Parse<T>(string text, string name) where T : class
    {
        try
        {
            var cached = JsonSerializer.Deserialize<Cached<T>>(text);
            return cached?.Data == null ? null : cached;
        }
        // 옛 모양으로 적힌 파일은 표의 생성자가 빈 칸(null)을 받아 NullReferenceException 을 낸다 — 오류 통계에
        // 「..ctor<TableCache.Read」 · 「PopulatePropertyInfo<TableCache.Read」로 올라왔다. 깨진 것과 같이 친다.
        catch (Exception ex) when (ex is JsonException or NullReferenceException or NotSupportedException or ArgumentException)
        {
            _ = name;
            return null;   // 깨졌으면 없는 셈 치고 원본에서 다시 읽는다
        }
    }

    /// <summary>
    /// 적어 둔다. 커스텀 팩이 켜져 있고 「-고친것」 표면 그 팩 폴더에 <b>기본 표와 다른 줄만</b> 적는다(<see cref="Packable"/>).
    /// 적지 못해도 이번 판은 원본에서 읽은 값으로 그대로 돈다.
    /// </summary>
    public static void Write<T>(string name, Cached<T> cached) where T : class
    {
        if (Packable(name) && PackFileOf(name) is { } pack)
        {
            var node = JsonSerializer.SerializeToNode(cached, Pretty) as JsonObject;
            if (node == null) { WriteText(pack, name, JsonSerializer.Serialize(cached, Pretty)); return; }
            var diff = Diff(name, node);
            // 기본 표와 다른 줄이 하나도 없으면 팩에서 그 표를 걷는다 — 빈 파일이 「고친 표」로 잡히지 않게.
            if (diff["Data"] is JsonObject data && data.Count > 0 && data.All(p => p.Value is JsonArray { Count: 0 }))
                DeletePack(name);
            else
                WriteText(pack, name, diff.ToJsonString(Pretty));
            return;
        }
        WriteBase(name, cached);
    }

    /// <summary>기본 자리(<see cref="Folder"/>)에 적는다 — 켠 커스텀 팩은 안 본다.</summary>
    public static void WriteBase<T>(string name, Cached<T> cached) where T : class
    {
        try
        {
            Directory.CreateDirectory(Folder);
            string path = WritePathFor(name), legacy = Path.Combine(Folder, name + ".json");
            File.WriteAllText(path, JsonSerializer.Serialize(cached, Pretty));
            // 예전 한글 이름 파일은 걷는다 — 남겨 두면 ASCII 파일이 지워졌을 때 옛 값으로 되읽힌다.
            if (legacy != path && File.Exists(legacy)) File.Delete(legacy);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            System.Diagnostics.Debug.WriteLine($"[TableCache] {name} 을 적지 못했습니다: {ex.Message}");
        }
    }

    private static void WriteText(string path, string name, string text)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, text);
            lock (Skipped) Skipped.Remove(name);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            System.Diagnostics.Debug.WriteLine($"[TableCache] {name} 을 팩에 적지 못했습니다: {ex.Message}");
        }
    }
}
