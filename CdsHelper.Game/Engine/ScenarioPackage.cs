using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows.Media.Imaging;
using CdsHelper.Game.Engine.Disev;
using CdsHelper.Game.Local.Helpers;

namespace CdsHelper.Game.Engine;

/// <summary>
/// 시나리오 한 편을 파일 하나(zip)로 내보내고 불러온다 — 원본에 없는 것이다.
/// </summary>
/// <remarks>
/// <code>
///   scenario.json        시나리오 줄 · 주인공 나라가 시나리오 나라면 그 나라 · 주인공 얼굴의 지문
///   book.json            이야기 책(exe-tables/시나리오책N.json 그대로)
///   hero.png             주인공 얼굴 — 초상화 벌의 번호(9000 아래)일 때. 받는 PC 의 같은 번호 얼굴이 다르면 새 번호로 넣는다
///   faces/male-N.png     9000 넘는 얼굴 — 주인공 얼굴이거나 대본이 화자 「#N」 · Person N 으로 세우는 사람
/// </code>
/// 초상화 벌의 얼굴은 「초상화 넣기」로 PC 마다 번호가 달라질 수 있어 그림째 싣는다. 받는 쪽은 그림을
/// <see cref="Portraits.UserFaceFolder"/> 에 두고, 책 이름은 겹치지 않게 새로 받는다.
/// </remarks>
public static class ScenarioPackage
{
    /// <summary>파일 모양 판.</summary>
    private const int FormatVersion = 1;

    /// <summary>불러온 주인공 얼굴에 새 번호를 줄 때 여기부터 빈 번호를 찾는다 — 앱에 싣는 얼굴(9001~)과 안 겹치게 띄운다.</summary>
    private const int ImportedFaceBase = 9100;

    /// <summary>scenario.json 의 알맹이.</summary>
    private sealed record Package(int Version, Scenario Scenario, List<NationTable.Nation>? Nations, string? HeroFace);

    private static readonly JsonSerializerOptions Pretty = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>대본이 얼굴로 세우는 큰 번호 — Person 칸과 화자 꼬리표 「#N」.</summary>
    private static readonly Regex FaceRefs = new(@"""Person""\s*:\s*(\d{4,})|#(\d{4,})", RegexOptions.Compiled);

    /// <summary>내보낸다. 잘 됐으면 null, 아니면 까닭.</summary>
    public static string? Export(Scenario scenario, Game? game, string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
            using var zip = ZipFile.Open(path, ZipArchiveMode.Create);

            var nations = Scenarios.ExtraNations.Where(n => n.Id == scenario.Nation).ToList();
            var faces = new SortedSet<int>();
            string? heroHash = null;

            string bookPath = DisevBook.PathOf(scenario.Book);
            if (File.Exists(bookPath))
            {
                string text = File.ReadAllText(bookPath);
                zip.CreateEntryFromFile(bookPath, "book.json");
                foreach (Match m in FaceRefs.Matches(text))
                    if (int.TryParse(m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value, out int n)
                        && n >= Portraits.ShippedBase)
                        faces.Add(n);
            }

            if (scenario.Face >= Portraits.ShippedBase) faces.Add(scenario.Face);
            else if (game?.Faces?.TryGetBgra(scenario.Face, female: false) is { } bgra)
            {
                heroHash = Hash(bgra);
                var png = new PngBitmapEncoder();
                png.Frames.Add(BitmapFrame.Create(Portraits.Bitmap(bgra)));
                using var stream = zip.CreateEntry("hero.png").Open();
                png.Save(stream);
            }

            foreach (int face in faces)
            {
                string file = Portraits.ShippedPathOf(false, face);
                if (File.Exists(file)) zip.CreateEntryFromFile(file, "faces/" + Portraits.ShippedFileName(false, face));
            }

            using (var stream = zip.CreateEntry("scenario.json").Open())
                JsonSerializer.Serialize(stream, new Package(FormatVersion, scenario, nations, heroHash), Pretty);
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return ex.Message;
        }
    }

    /// <summary>불러온 결과 — 지은 시나리오(못 불러왔으면 null)와 알릴 말.</summary>
    public readonly record struct Result(Scenario? Scenario, string Message);

    /// <summary>
    /// 불러온다 — 책은 새 이름으로, 얼굴 그림은 <see cref="Portraits.UserFaceFolder"/> 에 둔다.
    /// </summary>
    /// <param name="overwrite">같은 번호에 다른 얼굴 그림이 이미 있을 때 덮어쓸지 묻는다.</param>
    public static Result Import(string path, Game? game, Func<int, bool> overwrite)
    {
        try
        {
            using var zip = ZipFile.OpenRead(path);
            if (zip.GetEntry("scenario.json") is not { } head) return new(null, "시나리오 파일이 아닙니다(scenario.json 이 없습니다).");
            Package? package;
            using (var stream = head.Open()) package = JsonSerializer.Deserialize<Package>(stream);
            if (package?.Scenario is not { } scenario) return new(null, "scenario.json 을 읽지 못했습니다.");

            var notes = new List<string>();
            var all = Scenarios.All.ToList();
            string book = Scenarios.NextBook(all);

            // 이야기 책 — 없으면 본보기 파트 하나로 짓는다.
            if (zip.GetEntry("book.json") is { } bookEntry)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(DisevBook.PathOf(book))!);
                bookEntry.ExtractToFile(DisevBook.PathOf(book), overwrite: true);
            }
            else if (DisevBook.CreateBlank(book) == null) return new(null, $"이야기 책을 못 지었습니다 — {DisevBook.LastError}");

            // 9000 넘는 얼굴 — 같은 번호에 같은 그림이면 그대로, 다르면 물어 덮는다.
            Directory.CreateDirectory(Portraits.UserFaceFolder);
            foreach (var entry in zip.Entries.Where(e => e.FullName.StartsWith("faces/") && e.Name.EndsWith(".png")))
            {
                var m = Regex.Match(entry.Name, @"^(male|female)-(\d+)\.png$");
                if (!m.Success) continue;
                bool female = m.Groups[1].Value == "female";
                int face = int.Parse(m.Groups[2].Value);
                byte[] bytes = Read(entry);
                string had = Portraits.ShippedPathOf(female, face);
                if (File.Exists(had))
                {
                    if (File.ReadAllBytes(had).AsSpan().SequenceEqual(bytes)) continue;
                    if (!overwrite(face)) { notes.Add($"얼굴 {face} 은 이미 있는 그림을 그대로 둡니다."); continue; }
                }
                File.WriteAllBytes(Path.Combine(Portraits.UserFaceFolder, Portraits.ShippedFileName(female, face)), bytes);
            }

            // 주인공 얼굴(초상화 벌 번호) — 이 PC 의 같은 번호 얼굴이 그 그림이 아니면 새 번호로 넣는다.
            if (zip.GetEntry("hero.png") is { } heroEntry && scenario.Face < Portraits.ShippedBase)
            {
                var mine = game?.Faces?.TryGetBgra(scenario.Face, female: false);
                if (mine == null || Hash(mine) != package.HeroFace)
                {
                    int face = ImportedFaceBase;
                    while (File.Exists(Portraits.ShippedPathOf(false, face)) || File.Exists(Portraits.ShippedPathOf(true, face))) face++;
                    File.WriteAllBytes(Path.Combine(Portraits.UserFaceFolder, Portraits.ShippedFileName(false, face)), Read(heroEntry));
                    scenario = scenario with { Face = face };
                    notes.Add($"주인공 얼굴을 {face} 번으로 넣었습니다.");
                }
            }
            Portraits.ForgetShipped();

            // 주인공 나라가 시나리오 나라면 함께 들인다 — 같은 번호에 다른 나라가 있으면 있는 것을 둔다.
            var nations = Scenarios.ExtraNations.ToList();
            foreach (var nation in package.Nations ?? [])
            {
                if (nations.FirstOrDefault(n => n.Id == nation.Id) is { } same)
                {
                    if (same.Name != nation.Name) notes.Add($"나라 {nation.Id} 에 이미 「{same.Name}」 이 있어 「{nation.Name}」 은 안 들였습니다.");
                    continue;
                }
                nations.Add(nation);
            }

            scenario = scenario with { Book = book };
            all.Add(scenario);
            Scenarios.Save(all, nations);
            return new(scenario, string.Join(" ", notes.Prepend($"「{scenario.Title}」 을 불러왔습니다 — 이야기 책 {book}.")));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException
                                      or JsonException or NotSupportedException)
        {
            return new(null, $"불러오지 못했습니다 — {ex.Message}");
        }
    }

    private static byte[] Read(ZipArchiveEntry entry)
    {
        using var stream = entry.Open();
        using var copy = new MemoryStream();
        stream.CopyTo(copy);
        return copy.ToArray();
    }

    /// <summary>얼굴 색 배열의 지문 — 받는 PC 의 같은 번호 얼굴이 같은 그림인지 견준다.</summary>
    private static string Hash(uint[] bgra)
    {
        var bytes = new byte[bgra.Length * 4];
        Buffer.BlockCopy(bgra, 0, bytes, 0, bytes.Length);
        return Convert.ToHexString(SHA256.HashData(bytes));
    }
}
