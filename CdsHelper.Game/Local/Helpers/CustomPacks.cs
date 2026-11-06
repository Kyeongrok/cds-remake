using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CdsHelper.Support.Local.Helpers;

namespace CdsHelper.Game.Local.Helpers;

/// <summary>
/// 커스텀 팩 — 개발도구로 고친 값(인물 능력치 · 후원자 · 나라 · 힌트 … 「-고친것」 표)을 묶어 두는 폴더. 업데이트와 상관없이 남는다.
/// </summary>
/// <remarks>
/// <code>
///   %APPDATA%\CdsHelper\packs\&lt;팩 이름&gt;\
///       pack.json            이름 · 만든 이 · 설명 · 만든 게임 버전 · 형식 판(1)
///       persons-edits.json   표 자리와 같은 ASCII 파일 이름 — 고친 줄만 든다
///       sponsors-edits.json …
/// </code>
/// 표 자리(<see cref="TableCache.Folder"/>)는 설치판에서 <c>%LOCALAPPDATA%\CdsHelper\current\exe-tables</c> 라 Velopack 업데이트가
/// 통째로 바꾼다. 팩은 <see cref="UserDataFolder.Roaming"/>(<c>%APPDATA%\CdsHelper</c>, 시험이면 <c>CDS_SAVEDIR</c>) 밑이라 남는다.
///
/// 한 번에 한 팩만 켠다(<see cref="Use"/>). 켜면 <see cref="TableCache"/> 가 그 팩 표를 기본 표 위에 줄 단위로 얹어 읽고,
/// 편집 창이 적는 「-고친것」 표는 그 팩에 <b>기본 표와 다른 줄만</b> 적힌다. 게임은 타이틀에서만 고른다(판을 시작한 뒤 바꾸면
/// 이미 읽은 값과 섞인다). 설계: <c>docs/커스텀팩-설계.md</c>.
/// </remarks>
public static class CustomPacks
{
    /// <summary>팩 형식 판. 팩 폴더 꼴을 바꾸면 올린다.</summary>
    public const int FormatVersion = 1;

    /// <summary>팩 머리 파일 이름.</summary>
    public const string ManifestFile = "pack.json";

    /// <summary>팩 이름의 가장 긴 길이(글자).</summary>
    public const int MaxNameLength = 40;

    /// <summary>팩 머리 — <c>pack.json</c>.</summary>
    /// <param name="Name">팩 이름(폴더 이름과 같다).</param>
    /// <param name="Author">만든 이.</param>
    /// <param name="Description">설명.</param>
    /// <param name="GameVersion">만든 게임 버전(「1.1.80」).</param>
    /// <param name="Format">형식 판(<see cref="FormatVersion"/>).</param>
    public sealed record Manifest(string Name, string Author = "", string Description = "", string GameVersion = "",
                                  int Format = FormatVersion);

    private static readonly JsonSerializerOptions Pretty = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>팩들이 사는 폴더(<c>%APPDATA%\CdsHelper\packs</c>).</summary>
    public static string Root => UserDataFolder.Combine("packs");

    /// <summary>그 팩의 폴더.</summary>
    public static string FolderOf(string name) => Path.Combine(Root, name);

    /// <summary>지금 켠 팩 이름. 없으면 null.</summary>
    public static string? Active { get; private set; }

    /// <summary>켠 팩이 바뀌었다.</summary>
    public static event Action? Changed;

    /// <summary>지금 이 앱(게임 · 개발도구)이 돌리는 판 — 「1.1.80」.</summary>
    public static string AppVersion =>
        System.Reflection.Assembly.GetEntryAssembly()?.GetName().Version is { } v ? $"{v.Major}.{v.Minor}.{v.Build}" : "";

    /// <summary>있는 팩 이름들(가나다 차례).</summary>
    public static IReadOnlyList<string> Names()
    {
        try
        {
            if (!Directory.Exists(Root)) return [];
            return [.. Directory.GetDirectories(Root).Select(Path.GetFileName).OfType<string>()
                                .Where(n => IsSafeName(n, out _)).Order(StringComparer.CurrentCulture)];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    /// <summary>그런 이름의 팩이 있는지.</summary>
    public static bool Exists(string name) => IsSafeName(name, out _) && Directory.Exists(FolderOf(name));

    /// <summary>
    /// 폴더 이름으로 쓸 수 있는 팩 이름인지 — 비지 않고, 파일 이름에 못 쓰는 글자(\ / : * ? " &lt; &gt; |)와 제어 글자가 없고,
    /// 앞뒤가 빈칸 · 점이 아니며, 윈도 예약 이름(CON · NUL · COM1 …)이 아니고, <see cref="MaxNameLength"/> 글자를 넘지 않는다.
    /// </summary>
    public static bool IsSafeName(string? name, out string why)
    {
        why = "";
        if (string.IsNullOrWhiteSpace(name)) { why = "이름이 비었습니다"; return false; }
        if (name.Length > MaxNameLength) { why = $"이름은 {MaxNameLength}자까지입니다"; return false; }
        if (name != name.Trim() || name.EndsWith('.') || name.StartsWith('.'))
        {
            why = "이름 앞뒤에 빈칸이나 점을 쓸 수 없습니다";
            return false;
        }
        if (name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || name.Any(char.IsControl))
        {
            why = "이름에 쓸 수 없는 글자가 있습니다(\\ / : * ? \" < > |)";
            return false;
        }
        string stem = name.Split('.')[0].ToUpperInvariant();
        if (stem is "CON" or "PRN" or "AUX" or "NUL"
            || (stem.Length == 4 && (stem.StartsWith("COM") || stem.StartsWith("LPT")) && char.IsDigit(stem[3])))
        {
            why = "윈도가 쓰는 이름이라 쓸 수 없습니다";
            return false;
        }
        return true;
    }

    /// <summary>그 팩의 머리. 없거나 깨졌으면 이름만 든 머리.</summary>
    public static Manifest Read(string name)
    {
        try
        {
            string path = Path.Combine(FolderOf(name), ManifestFile);
            if (File.Exists(path) && JsonSerializer.Deserialize<Manifest>(File.ReadAllText(path)) is { } m)
                return m with { Name = name };
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { }
        return new Manifest(name);
    }

    private static string WriteManifest(Manifest manifest)
    {
        try
        {
            Directory.CreateDirectory(FolderOf(manifest.Name));
            File.WriteAllText(Path.Combine(FolderOf(manifest.Name), ManifestFile), JsonSerializer.Serialize(manifest, Pretty));
            return "";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return ex.Message;
        }
    }

    /// <summary>빈 팩을 만든다. 잘 되면 빈 글, 아니면 까닭.</summary>
    public static string Create(string name, string author = "", string description = "")
    {
        if (!IsSafeName(name, out string why)) return why;
        if (Directory.Exists(FolderOf(name))) return $"「{name}」 팩이 이미 있습니다";
        return WriteManifest(new Manifest(name, author, description, AppVersion));
    }

    /// <summary>머리(만든 이 · 설명)를 고친다.</summary>
    public static string Describe(string name, string author, string description) =>
        Exists(name) ? WriteManifest(Read(name) with { Author = author, Description = description }) : $"「{name}」 팩이 없습니다";

    /// <summary>팩 이름을 바꾼다. 켜 둔 팩이면 새 이름으로 다시 켠다.</summary>
    public static string Rename(string from, string to)
    {
        if (!Exists(from)) return $"「{from}」 팩이 없습니다";
        if (!IsSafeName(to, out string why)) return why;
        if (from == to) return "";
        if (Directory.Exists(FolderOf(to)) && !string.Equals(from, to, StringComparison.OrdinalIgnoreCase))
            return $"「{to}」 팩이 이미 있습니다";
        bool active = Active == from;
        try
        {
            // 대소문자만 바꿀 때는 윈도가 같은 이름으로 보므로 딴 이름을 거친다.
            string temp = FolderOf(from) + ".renaming";
            Directory.Move(FolderOf(from), temp);
            Directory.Move(temp, FolderOf(to));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return ex.Message;
        }
        WriteManifest(Read(to));
        if (active) Use(to);
        return "";
    }

    /// <summary>팩을 지운다. 켜 둔 팩이면 끈다.</summary>
    public static string Delete(string name)
    {
        if (!Exists(name)) return $"「{name}」 팩이 없습니다";
        try
        {
            Directory.Delete(FolderOf(name), recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return ex.Message;
        }
        if (Active == name) Use(null);
        return "";
    }

    /// <summary>
    /// 그 팩을 켠다(null · 빈 글 · 없는 팩이면 끈다). 담아 둔 고친 것과 인물표를 다시 읽게 알린다.
    /// </summary>
    /// <returns>판(Version)이 안 맞아 건너뛴 표들(「표 이름: 까닭」). 없으면 빈 목록.</returns>
    public static IReadOnlyList<string> Use(string? name)
    {
        string? pick = !string.IsNullOrEmpty(name) && Exists(name) ? name : null;
        Active = pick;
        TableCache.UsePack(pick == null ? null : FolderOf(pick));

        var skipped = new List<string>();
        if (pick != null)
        {
            if (Read(pick).Format > FormatVersion)
                skipped.Add($"{ManifestFile}: 더 새 버전에서 만든 팩입니다(형식 {Read(pick).Format})");
            foreach (string file in TableFiles(pick))
            {
                string table = TableCache.NameOfFile(Path.GetFileNameWithoutExtension(file));
                if (TableCache.CheckPackFile(table, file) is { } why)
                {
                    TableCache.NoteSkipped(table, why);
                    skipped.Add($"{table}: {why}");
                }
            }
        }

        PersonTable.Reload();
        Changed?.Invoke();
        return skipped;
    }

    /// <summary>팩 안의 표 파일들(<c>pack.json</c> 을 뺀 <c>*.json</c>).</summary>
    public static IReadOnlyList<string> TableFiles(string name)
    {
        try
        {
            string folder = FolderOf(name);
            if (!Directory.Exists(folder)) return [];
            return [.. Directory.GetFiles(folder, "*.json")
                                .Where(f => !Path.GetFileName(f).Equals(ManifestFile, StringComparison.OrdinalIgnoreCase))
                                .Order(StringComparer.Ordinal)];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    /// <summary>
    /// 팩 지문 — 표 파일 이름과 알맹이를 이어 SHA-256 을 낸 앞 16자. 세이브에 적어 두고 불러올 때 같은 팩인지 견준다.
    /// 팩이 없으면 빈 글.
    /// </summary>
    public static string Fingerprint(string? name)
    {
        if (string.IsNullOrEmpty(name) || !Exists(name)) return "";
        using var sha = SHA256.Create();
        foreach (string file in TableFiles(name))
        {
            byte[] head = Encoding.UTF8.GetBytes(Path.GetFileName(file).ToLowerInvariant() + "\n");
            sha.TransformBlock(head, 0, head.Length, null, 0);
            try
            {
                byte[] body = File.ReadAllBytes(file);
                sha.TransformBlock(body, 0, body.Length, null, 0);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
        sha.TransformFinalBlock([], 0, 0);
        return Convert.ToHexString(sha.Hash!)[..16].ToLowerInvariant();
    }

    /// <summary>
    /// 세이브가 시작한 팩이 지금 켠 팩과 다르면 물을 글. 같으면 null — 이름이 같아도 지문이 다르면(팩을 고쳤으면) 묻는다.
    /// </summary>
    /// <param name="savedPack">세이브에 적힌 시작 팩 이름. 팩 없이 시작했거나 이 칸 앞의 세이브면 null · 빈 글.</param>
    /// <param name="savedPrint">세이브에 적힌 그 팩의 지문.</param>
    public static string? MismatchQuestion(string? savedPack, string? savedPrint)
    {
        string was = savedPack ?? "", now = Active ?? "";
        if (was != now)
            return was.Length == 0 ? $"이 세이브는 커스텀 팩 없이 시작했습니다.\n지금은 「{now}」 팩을 켜 두었습니다.\n그래도 불러오겠습니까?"
                 : now.Length == 0 ? $"이 세이브는 「{was}」 팩으로 시작했습니다.\n지금은 커스텀 팩을 꺼 두었습니다.\n그래도 불러오겠습니까?"
                 : $"이 세이브는 「{was}」 팩으로 시작했습니다.\n지금은 「{now}」 팩을 켜 두었습니다.\n그래도 불러오겠습니까?";
        if (was.Length > 0 && !string.IsNullOrEmpty(savedPrint) && savedPrint != Fingerprint(now))
            return $"이 세이브를 시작한 뒤 「{was}」 팩의 내용이 바뀌었습니다.\n그래도 불러오겠습니까?";
        return null;
    }

    // ── 내보내기 · 들이기(zip) ─────────────────────────────────────────────────
    // zip 안은 팩 폴더 그대로다 — pack.json 과 표 파일들이 맨 위에 든다(시나리오 · 발견물 꾸러미와 같은 결).

    /// <summary>팩을 zip 한 장으로 내보낸다. 잘 되면 빈 글, 아니면 까닭.</summary>
    public static string Export(string name, string zipPath)
    {
        if (!Exists(name)) return $"「{name}」 팩이 없습니다";
        try
        {
            WriteManifest(Read(name));   // 머리가 없던 팩(손으로 만든 폴더)도 머리를 갖춰 내보낸다
            if (File.Exists(zipPath)) File.Delete(zipPath);
            using var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create);
            zip.CreateEntryFromFile(Path.Combine(FolderOf(name), ManifestFile), ManifestFile);
            foreach (string file in TableFiles(name))
                zip.CreateEntryFromFile(file, Path.GetFileName(file));
            return "";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            return $"{zipPath} 에 못 썼습니다 — {ex.Message}";
        }
    }

    /// <summary>zip 에 든 팩 머리. 팩 zip 이 아니면 null 과 까닭.</summary>
    public static Manifest? Peek(string zipPath, out string why)
    {
        why = "";
        try
        {
            using var zip = ZipFile.OpenRead(zipPath);
            if (zip.GetEntry(ManifestFile) is not { } entry) { why = $"{ManifestFile} 이 없습니다 — 커스텀 팩 zip 이 아닙니다"; return null; }
            using var reader = new StreamReader(entry.Open());
            var manifest = JsonSerializer.Deserialize<Manifest>(reader.ReadToEnd());
            if (manifest == null || !IsSafeName(manifest.Name, out why)) { why = $"팩 이름이 맞지 않습니다 — {why}"; return null; }
            return manifest;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or JsonException)
        {
            why = ex.Message;
            return null;
        }
    }

    /// <summary>
    /// zip 을 <paramref name="asName"/> 팩으로 들인다 — 같은 이름의 팩이 있으면 갈아 끼운다(부르는 쪽이 먼저 묻는다).
    /// 맨 위의 <c>*.json</c> 만 받고 그 밖(폴더 · 딴 파일)은 버린다.
    /// </summary>
    public static string Import(string zipPath, string asName)
    {
        if (!IsSafeName(asName, out string why)) return why;
        if (Peek(zipPath, out why) is not { } manifest) return why;

        string folder = FolderOf(asName), temp = folder + ".importing";
        try
        {
            if (Directory.Exists(temp)) Directory.Delete(temp, recursive: true);
            Directory.CreateDirectory(temp);
            using (var zip = ZipFile.OpenRead(zipPath))
            {
                foreach (var entry in zip.Entries)
                {
                    // 경로가 든 이름(../ 따위)은 받지 않는다.
                    if (entry.FullName != entry.Name || !entry.Name.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) continue;
                    if (!IsSafeName(Path.GetFileNameWithoutExtension(entry.Name), out _)) continue;
                    entry.ExtractToFile(Path.Combine(temp, entry.Name), overwrite: true);
                }
            }
            File.WriteAllText(Path.Combine(temp, ManifestFile), JsonSerializer.Serialize(manifest with { Name = asName }, Pretty));

            bool active = Active == asName;
            if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
            Directory.Move(temp, folder);
            if (active) Use(asName);
            return "";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            try { if (Directory.Exists(temp)) Directory.Delete(temp, recursive: true); } catch { }
            return ex.Message;
        }
    }
}
