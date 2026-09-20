using System.IO;
using System.Text.Json;

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

    public static string PathFor(string name) => Path.Combine(Folder, name + ".json");

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

    /// <summary>적어 둔 것을 읽는다. 없거나 깨졌으면 null.</summary>
    public static Cached<T>? Read<T>(string name) where T : class
    {
        try
        {
            string path = PathFor(name);
            if (!File.Exists(path)) return null;
            var cached = JsonSerializer.Deserialize<Cached<T>>(File.ReadAllText(path));
            return cached?.Data == null ? null : cached;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;   // 깨졌으면 없는 셈 치고 원본에서 다시 읽는다
        }
    }

    /// <summary>적어 둔다. 적지 못해도 이번 판은 원본에서 읽은 값으로 그대로 돈다.</summary>
    public static void Write<T>(string name, Cached<T> cached) where T : class
    {
        try
        {
            Directory.CreateDirectory(Folder);
            File.WriteAllText(PathFor(name), JsonSerializer.Serialize(cached, Pretty));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            System.Diagnostics.Debug.WriteLine($"[TableCache] {name} 을 적지 못했습니다: {ex.Message}");
        }
    }
}
