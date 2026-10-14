using System.IO;
using System.Text.Json;
using CdsHelper.Game.Local.Settings;

namespace CdsHelper.Game.Local.Helpers;

/// <summary>
/// 화면 글의 언어 — 한국어(기본)와 일본어. 원본에 없는 것이다.
/// </summary>
/// <remarks>
/// 코드 속 글은 한국어로 적혀 있다. 일본어일 때는 <b>한국어 글 그대로를 열쇠로</b> 번역표
/// (<c>lang/ja.json</c>)에서 찾아 바꿔 낸다(<see cref="T"/>). 표에 없는 글은 한국어로 남는다 —
/// 글을 하나씩 감싸고 표를 채워 가며 차례로 옮긴다.
///
/// 게임 데이터(대본 · 이름 · 설명)는 이 표가 아니라 일본어판 게임 폴더의 원본 파일에서 읽는다.
/// 언어를 바꾸면 다시 켜야 다 바뀐다 — 이미 지어 둔 창과 읽어 둔 표는 그대로다.
/// </remarks>
public static class Lang
{
    /// <summary>한국어.</summary>
    public const string Korean = "ko";

    /// <summary>일본어.</summary>
    public const string Japanese = "ja";

    /// <summary>고를 수 있는 언어 — (코드, 그 언어로 적은 이름).</summary>
    public static readonly (string Code, string Name)[] All = [(Korean, "한국어"), (Japanese, "日本語")];

    /// <summary>
    /// 이번에 켠 언어 — 앱을 켤 때 설정에서 한 번 읽고 그대로 둔다. 설정을 바꿔도 다시 켜기 전에는 안 바뀐다
    /// (창마다 반쯤만 바뀌어 섞이지 않게).
    /// </summary>
    public static string Current { get; } = GameSettings.Language;

    /// <summary>일본어로 켰는지.</summary>
    public static bool IsJapanese => Current == Japanese;

    private static Dictionary<string, string>? _ja;

    /// <summary>
    /// 그 글의 지금 언어 판. 한국어면 그대로, 일본어면 번역표에서 찾고 없으면 그대로 낸다.
    /// </summary>
    public static string T(string korean)
    {
        if (!IsJapanese || string.IsNullOrEmpty(korean)) return korean;
        return Table().TryGetValue(korean, out var ja) && ja.Length > 0 ? ja : korean;
    }

    /// <summary>번역표 — 한 번 읽어 들고 있는다. 못 읽으면 빈 표.</summary>
    private static Dictionary<string, string> Table()
    {
        if (_ja != null) return _ja;
        try
        {
            string path = Path.Combine(AppContext.BaseDirectory, "lang", "ja.json");
            _ja = File.Exists(path)
                ? JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(path)) ?? []
                : [];
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Lang] 번역표를 못 읽었습니다 — {ex.Message}");
            _ja = [];
        }
        return _ja;
    }
}
