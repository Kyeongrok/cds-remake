using System.IO;
using System.IO.Compression;
using System.Text.Json;
using CdsHelper.Game.Engine.Disev;
using CdsHelper.Game.UI.Views;

namespace CdsHelper.Game.Local.Helpers;

/// <summary>
/// 발견물 하나를 <b>통째로</b> zip 한 장에 내보내고, 다시 불러온다.
/// </summary>
/// <remarks>
/// 발견물 하나는 네 군데에 흩어져 있다 — 표 줄(<see cref="DiscoveryTable.Record"/>, 손댄 것은
/// <see cref="DiscoveryEdits"/>), 발견 대본(<see cref="DisevBook"/> 의 그 번호 파트), 힌트
/// (<see cref="HintTable.Hint"/>, 손댄 것은 <see cref="HintEdits"/>), 미디어(그림·동영상·움직이는
/// 그림 — 번호만 있고 <see cref="DiscoveryStillFiles"/>·<see cref="MovieFiles"/>·
/// <see cref="DiscoveryClipFiles"/> 에 올려 둔 파일이 실물이다). 이 넷을 zip 하나로 묶어
/// 내보내고, 불러올 때는 <b>같은 번호</b>로 되돌려 놓아 다시 불러오면 꼭 그 발견물이 된다.
///
/// 미디어는 <b>번호가 아니라 파일 그대로</b> 담는다 — 원본 아카이브(DSTILL.CDS 따위) 안의
/// 그림이라도 꺼내서 넣는다. 그래야 원본 게임 폴더가 없는 자리에서 불러와도 그림이 산다.
/// </remarks>
public static class DiscoveryPackage
{
    /// <summary>zip 안에 담는 메타데이터 파일 이름.</summary>
    private const string ManifestName = "discovery.json";

    private const string StillEntry = "still.png";
    private const string MovieEntryStem = "movie";
    private const string ClipFolder = "clip/";

    /// <summary>zip 에 적어 두는 알맹이.</summary>
    private sealed record Manifest(
        DiscoveryTable.Record Record,
        string? EventBytes,           // DISEV 파트 원본 바이트(base64). 없으면 파트가 비어 있던 것이다.
        string? MovieExtension,       // "movie.<ext>" 로 담을 때의 확장자(점 포함).
        HintTable.Hint? Hint);        // Record.Hint 가 가리키는 힌트. 없으면 null.

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    /// <summary>
    /// 그 번호의 발견물을 zip 하나로 내보낸다.
    /// </summary>
    /// <param name="id">발견물 번호.</param>
    /// <param name="gameDirectory">원본 미디어를 찾을 게임 폴더. 없어도 올려 둔 것만으로 될 수 있다.</param>
    /// <param name="table">발견물 표.</param>
    /// <param name="hints">힌트 표. 없으면 힌트는 안 담는다.</param>
    /// <param name="zipPath">쓸 자리.</param>
    /// <returns>못 내보내면 까닭 한 줄. 잘 됐으면 빈 문자열.</returns>
    public static string Export(int id, string gameDirectory, DiscoveryTable table, HintTable? hints, string zipPath)
    {
        if (table.Find(id) is not { } record) return $"발견물 {id} 번이 없습니다";

        string? eventBytes = null;
        if (DisevBook.Open() is { } book && id >= 0 && id < book.Count)
        {
            var raw = book.Part(id);
            if (raw.Length > 0) eventBytes = Convert.ToBase64String(raw);
        }

        HintTable.Hint? hint = record.Hint >= 0 ? hints?.Find(record.Hint) : null;

        try
        {
            if (File.Exists(zipPath)) File.Delete(zipPath);
            using var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create);
            string? movieExt = null;

            if (record.Picture >= 0 && StillBgra(record.Picture, gameDirectory) is var (bgra, w, h) && bgra != null)
                WritePng(zip, StillEntry, bgra, w, h);

            if (record.Movie >= 0)
            {
                string? path = DiscoveryDialog.MovieOf(gameDirectory, record.Movie);
                if (path != null)
                {
                    movieExt = Path.GetExtension(path);
                    zip.CreateEntryFromFile(path, MovieEntryStem + movieExt);
                }
            }

            if (record.Clip >= 0)
            {
                var frames = ClipFrames(record.Clip, gameDirectory, out int cw, out int ch);
                for (int i = 0; frames != null && i < frames.Length; i++)
                    WritePng(zip, $"{ClipFolder}{i:000}.png", frames[i], cw, ch);
            }

            var manifest = new Manifest(record, eventBytes, movieExt, hint);
            var entry = zip.CreateEntry(ManifestName);
            using (var writer = new StreamWriter(entry.Open()))
                writer.Write(JsonSerializer.Serialize(manifest, Json));

            return "";
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            return $"{zipPath} 에 못 썼습니다 — {e.Message}";
        }
    }

    /// <summary>
    /// zip 을 불러와 <b>담겨 있던 그 번호</b>로 되돌려 놓는다 — 표 줄·대본·힌트를 씌우고,
    /// 미디어는 올려 둔 자리(<see cref="DiscoveryStillFiles"/> 따위)에 심는다.
    /// </summary>
    /// <returns>못 불러오면 까닭 한 줄. 잘 됐으면 빈 문자열.</returns>
    public static string Import(string zipPath, out int id)
    {
        id = -1;
        try
        {
            using var zip = ZipFile.OpenRead(zipPath);
            var manifestEntry = zip.GetEntry(ManifestName)
                ?? throw new InvalidDataException($"{ManifestName} 이 없습니다");
            Manifest? manifest;
            using (var reader = new StreamReader(manifestEntry.Open()))
                manifest = JsonSerializer.Deserialize<Manifest>(reader.ReadToEnd(), Json);
            if (manifest == null) return "발견물 자료를 못 읽었습니다";

            var record = manifest.Record;
            id = record.Id;

            if (record.Picture >= 0 && zip.GetEntry(StillEntry) is { } stillEntry)
            {
                var (bgra, w, h) = ReadPng(stillEntry);
                DiscoveryStillFiles.Upload(bgra, w, h, record.Picture);
            }

            if (record.Movie >= 0 && manifest.MovieExtension is { } ext
                && zip.GetEntry(MovieEntryStem + ext) is { } movieEntry)
            {
                string temp = Path.Combine(Path.GetTempPath(), $"cds-import-{Guid.NewGuid():N}{ext}");
                movieEntry.ExtractToFile(temp, overwrite: true);
                try { MovieFiles.Upload(temp, MovieFiles.DiscoveryStem(record.Movie)); }
                finally { File.Delete(temp); }
            }

            if (record.Clip >= 0)
            {
                var clipFrames = new List<(uint[] Bgra, int Width, int Height)>();
                foreach (var e in zip.Entries.Where(e => e.FullName.StartsWith(ClipFolder, StringComparison.Ordinal))
                                              .OrderBy(e => e.FullName, StringComparer.Ordinal))
                    clipFrames.Add(ReadPng(e));
                if (clipFrames.Count > 0) DiscoveryClipFiles.Upload(clipFrames, record.Clip);
            }

            DiscoveryEdits.Set(record);

            if (manifest.EventBytes is { } b64 && DisevBook.Open() is { } book
                && record.Id >= 0 && record.Id < book.Count)
            {
                book.Replace(record.Id, Convert.FromBase64String(b64));
                book.Save();
            }

            if (manifest.Hint is { } hint)
                HintEdits.Set(hint.Id, hint.Name, hint.Grade, hint.Category, hint.Funds,
                              hint.Deadline, hint.Discovery, hint.Text);

            return "";
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException
                                     or InvalidDataException or JsonException)
        {
            return $"{zipPath} 를 못 읽었습니다 — {e.Message}";
        }
    }

    /// <summary>그 그림 번호의 BGRA — 올려 둔 것을 먼저, 없으면 게임 폴더의 원본.</summary>
    private static (uint[]? Bgra, int W, int H) StillBgra(int picture, string gameDirectory)
    {
        if (DiscoveryStillFiles.TryGetBgra(picture, out int w, out int h) is { } up) return (up, w, h);
        if (DiscoveryStills.Open(gameDirectory)?.TryGetBgra(picture, out w, out h) is { } og) return (og, w, h);
        return (null, 0, 0);
    }

    /// <summary>그 편 번호의 장들 — 올려 둔 것을 먼저, 없으면 게임 폴더의 원본.</summary>
    private static uint[][]? ClipFrames(int clip, string gameDirectory, out int w, out int h)
    {
        if (DiscoveryClipFiles.Frames(clip, out w, out h) is { } up) return up;
        w = DiscoveryClips.Width; h = DiscoveryClips.Height;
        return DiscoveryClips.Open(gameDirectory)?.Frames(clip);
    }

    private static void WritePng(ZipArchive zip, string name, uint[] bgra, int w, int h)
    {
        var entry = zip.CreateEntry(name);
        using var stream = entry.Open();
        PngIo.Write(stream, bgra, w, h);
    }

    private static (uint[] Bgra, int Width, int Height) ReadPng(ZipArchiveEntry entry)
    {
        using var stream = entry.Open();
        using var mem = new MemoryStream();
        stream.CopyTo(mem);
        mem.Position = 0;
        return PngIo.Read(mem);
    }
}
