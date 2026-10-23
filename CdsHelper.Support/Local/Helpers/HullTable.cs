using System.IO;
using System.Text.Json;
using CdsHelper.Support.Local.Models;

namespace CdsHelper.Support.Local.Helpers;

/// <summary>
/// 원본 선체 여덟 줄(<see cref="Hull.Table"/>) · 개조 한계 · 조선소에 내는 다섯(<see cref="Hull.Builtin"/>)을 한 파일
/// <c>exe-tables/hulls.json</c>(표 이름 「선체표」)에 적고 읽는다. 고쳐 커밋하면 업데이트로 모두에게 간다.
/// </summary>
/// <remarks>
/// 다른 표와 같은 꼴(<c>Stamp · Data · Source · Version</c>)로 적는다 — 게임의 TableCache 와 같다. 이 표는 게임 앱 말고
/// 편집기(Main)도 읽어야 해서 Support 에 따로 둔다. 파일이 없거나 깨졌으면 코드에 적어 둔 값(<see cref="Hull.Defaults"/>)으로 돈다.
///
/// <b>이름은 바꾸지 않는다</b> — 세이브가 선체를 이름으로 적는다.
/// </remarks>
public static class HullTable
{
    /// <summary>파일 이름(확장자 뺀 것). 게임 TableCache 의 이름 표에도 「선체표」로 올라 있다.</summary>
    public const string FileName = "hulls";

    /// <summary>알맹이 모양 판. 칸을 더하면 올린다.</summary>
    public const int Version = 1;

    /// <summary>
    /// 선체 한 줄. <see cref="Price"/> 는 닢이다(원본 표 <c>+0x38</c> 계수의 1000배). 한계(…Top)는 개조로 갈 수 있는 끝이다.
    /// </summary>
    /// <param name="Shipyard">조선소에서 파는지 — 원본은 갤리온 · 중카락 · 카락 · 대형카라벨 · 카라벨 다섯이다.</param>
    /// <param name="Skin">지도 그림 벌(0~3, <c>0x005695D8</c>).</param>
    /// <param name="SailBits">처음 돛 비트(2비트 x 3 — 메인 · 세브 · 선미, 0 없음 · 1 삼각 · 2 사각).</param>
    public sealed record Row(
        int Id, string Name, int Hp, int Speed, int Capacity, int Tonnage, int Crew,
        int GunsMin, int GunsMax, int Price, int SailBits,
        int HpTop, int SpeedTop, int CapacityTop, int TonnageTop, int GunsTop,
        int MaxMasts, bool CanChangeSail, int Skin, bool Shipyard);

    /// <summary>파일의 알맹이.</summary>
    public sealed record Snapshot(List<Row> Hulls);

    /// <summary>파일 한 벌 — 게임 TableCache.Cached 와 같은 꼴.</summary>
    public sealed record Cached(string Stamp, Snapshot Data, string Source = "", int Version = 1);

    private static readonly JsonSerializerOptions Pretty = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>파일 자리 — 개발 때는 저장소 <c>exe-tables</c>, 배포 때는 실행 파일 옆이다(TableCache.Folder 와 같은 길).</summary>
    public static string PathOf
    {
        get
        {
            for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
            {
                string candidate = Path.Combine(dir.FullName, "exe-tables");
                if (Directory.Exists(candidate)) return Path.Combine(candidate, FileName + ".json");
            }
            return Path.Combine(AppContext.BaseDirectory, "exe-tables", FileName + ".json");
        }
    }

    /// <summary>적어 둔 여덟 줄. 없거나 깨졌거나 번호 0~7 이 다 안 들었으면 null.</summary>
    public static Row[]? Load()
    {
        try
        {
            string path = PathOf;
            if (!File.Exists(path)) return null;
            var cached = JsonSerializer.Deserialize<Cached>(File.ReadAllText(path));
            if (cached?.Data?.Hulls is not { } rows || cached.Version != Version) return null;
            var sorted = rows.Where(r => r != null && !string.IsNullOrWhiteSpace(r.Name)).OrderBy(r => r.Id).ToArray();
            if (sorted.Length != Hull.Defaults.Length || sorted.Where((r, i) => r.Id != i).Any()) return null;
            return sorted;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException
                                      or NullReferenceException or NotSupportedException or ArgumentException)
        {
            return null;
        }
    }

    /// <summary>여덟 줄을 적는다. 적은 뒤 <see cref="Hull.Reload"/> 로 다시 읽게 한다.</summary>
    public static void Save(IEnumerable<Row> rows)
    {
        string path = PathOf;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var cached = new Cached("", new Snapshot([.. rows.OrderBy(r => r.Id)]), "CDS_95.EXE 선체표(0x004FC1E0) · 손으로 고침", Version);
        File.WriteAllText(path, JsonSerializer.Serialize(cached, Pretty));
        Hull.Reload();
    }
}
