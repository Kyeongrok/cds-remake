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
        if (Table().TryGetValue(korean, out var ja) && ja.Length > 0) return ja;
        lock (Seen)
        {
            if (Seen.TryGetValue(korean, out var known)) return known;
            string made = ByTemplate(korean) ?? ByLines(korean) ?? korean;
            if (Seen.Count > 20000) Seen.Clear();
            Seen[korean] = made;
            return made;
        }
    }

    private static bool _hooked;

    /// <summary>
    /// 보통 윈도 글자(TextBlock)로 그리는 글도 바꾼다 — 게임 글꼴(<see cref="GameFont"/>)을 안 지나는 창(모드 · 설정 설명,
    /// 표 머리 따위)이다. 글 칸이 화면에 붙을 때 한 번 바꾼다. 일본어가 아니면 아무것도 안 건다.
    /// </summary>
    public static void InstallWpfHook()
    {
        if (_hooked || !IsJapanese) return;
        _hooked = true;
        System.Windows.EventManager.RegisterClassHandler(typeof(System.Windows.Controls.TextBlock),
            System.Windows.FrameworkElement.LoadedEvent, new System.Windows.RoutedEventHandler((sender, _) =>
            {
                if (sender is not System.Windows.Controls.TextBlock block || string.IsNullOrEmpty(block.Text)) return;
                // 묶인 글은 손대지 않는다 — 묶음이 끊긴다.
                if (System.Windows.Data.BindingOperations.GetBindingExpression(block,
                        System.Windows.Controls.TextBlock.TextProperty) != null) return;
                if (block.Inlines.Count > 1) return;
                string done = T(block.Text);
                if (done != block.Text) block.Text = done;
            }));
        System.Windows.EventManager.RegisterClassHandler(typeof(System.Windows.Window),
            System.Windows.FrameworkElement.LoadedEvent, new System.Windows.RoutedEventHandler((sender, _) =>
            {
                if (sender is System.Windows.Window { Title.Length: > 0 } window) window.Title = T(window.Title);
            }));
    }

    /// <summary>한 번 맞춰 본 글 — 틀 맞추기는 비싸서 결과를 들고 있는다.</summary>
    private static readonly Dictionary<string, string> Seen = [];

    /// <summary>
    /// 여러 줄 글은 줄마다 따로 찾는다 — 설명 글이 줄 단위로 번역표에 들어 있을 수 있다. 하나도 못 찾으면 null.
    /// </summary>
    private static string? ByLines(string korean)
    {
        if (!korean.Contains('\n')) return null;
        var lines = korean.Split('\n');
        bool any = false;
        for (int i = 0; i < lines.Length; i++)
        {
            string line = lines[i].TrimEnd('\r');
            if (line.Length == 0) continue;
            string done = T(line);
            if (!ReferenceEquals(done, line) && done != line) { lines[i] = done; any = true; }
        }
        return any ? string.Join('\n', lines) : null;
    }

    /// <summary>번역표의 틀(「{0}」 자리가 있는 열쇠) — 정규식과 일본어 틀.</summary>
    private static List<(System.Text.RegularExpressions.Regex Pattern, string Japanese, string Literal)>? _templates;

    /// <summary>
    /// 이름이 끼는 글(「[{0}]에 들어간다」 꼴)을 틀로 맞춰 바꾼다. 자리에 든 글도 번역표로 바꿔 넣는다.
    /// 못 맞추면 null.
    /// </summary>
    /// <remarks>
    /// 자리 바로 뒤에 또 자리가 붙은 꼴(「{0}{1} 데리고」)은 뒷자리가 거의 늘 한국어 조사다(<c>GameUi.Josa</c>) —
    /// 그 자리는 조사로만 맞춘다. 앞자리를 짧게 잡아 이름이 쪼개지지 않게.
    /// </remarks>
    private static string? ByTemplate(string korean)
    {
        _templates ??= BuildTemplates();
        foreach (var (pattern, japanese, literal) in _templates)
        {
            if (literal.Length > 0 && !korean.Contains(literal)) continue;
            var m = pattern.Match(korean);
            if (!m.Success) continue;
            return System.Text.RegularExpressions.Regex.Replace(japanese, @"\{(\d+)\}", x =>
            {
                var g = m.Groups["a" + x.Groups[1].Value];
                return g.Success ? T(g.Value) : "";
            });
        }
        return null;
    }

    /// <summary>한국어 조사 — 자리 바로 뒤에 붙는 자리는 이것들로만 맞춘다.</summary>
    private const string Particles = "이라는|라는|이라고|라고|으로|로|에게|에서|에|의|이|가|을|를|은|는|와|과|이야|야|아|이여|여|";

    private static List<(System.Text.RegularExpressions.Regex, string, string)> BuildTemplates()
    {
        var list = new List<(System.Text.RegularExpressions.Regex, string, string)>();
        foreach (var (key, ja) in Table())
        {
            if (ja.Length == 0 || !key.Contains('{')) continue;
            var parts = System.Text.RegularExpressions.Regex.Split(key, @"(\{\d+\})");
            var regex = new System.Text.StringBuilder("^");
            string longest = "";
            for (int i = 0; i < parts.Length; i++)
            {
                var hole = System.Text.RegularExpressions.Regex.Match(parts[i], @"^\{(\d+)\}$");
                if (!hole.Success)
                {
                    regex.Append(System.Text.RegularExpressions.Regex.Escape(parts[i]));
                    if (parts[i].Length > longest.Length) longest = parts[i];
                    continue;
                }
                bool afterHole = i >= 2 && parts[i - 1].Length == 0
                                 && System.Text.RegularExpressions.Regex.IsMatch(parts[i - 2], @"^\{\d+\}$");
                string name = "a" + hole.Groups[1].Value;
                regex.Append(afterHole ? $"(?<{name}>{Particles})" : $"(?<{name}>.+?)");
            }
            regex.Append('$');
            // 글자가 하나도 없는 틀(「{0}{1}」)은 아무 글에나 걸리고 자기 자신을 다시 부른다 — 안 쓴다.
            if (longest.Trim().Length == 0) continue;
            try
            {
                list.Add((new System.Text.RegularExpressions.Regex(regex.ToString(),
                              System.Text.RegularExpressions.RegexOptions.Singleline), ja, longest));
            }
            catch (ArgumentException) { /* 틀이 깨졌으면 건너뛴다 */ }
        }
        // 글자가 많이 박힌 틀부터 — 짧은 틀(「{0}」 따위)이 먼저 걸리지 않게.
        list.Sort((a, b) => b.Item3.Length.CompareTo(a.Item3.Length));
        return list;
    }

    private static Dictionary<string, Dictionary<string, List<string>>>? _books;

    /// <summary>
    /// 대본 대사의 일본어 — 책 · 파트 · 파트 안 대사 차례(<c>대사</c> 명령을 토막 · 명령 차례로 센 번호)로 찾는다.
    /// 일본어판 원본 대본(DISEV.CDS · STORY · 개인 이야기)을 한국어 책과 같은 자리끼리 짝지어 구운
    /// <c>lang/ja-books.json</c> 이다(tools/ja-books). 일본어가 아니거나 그 자리가 없으면 null.
    /// </summary>
    /// <param name="count">이 파트의 대사 수 — 구울 때와 다르면(대본을 고쳤으면) 짝이 어긋나므로 안 쓴다.</param>
    public static string? ScriptLine(string book, int part, int index, int count)
    {
        if (!IsJapanese || index < 0) return null;
        if (_books == null)
        {
            try
            {
                string path = Path.Combine(AppContext.BaseDirectory, "lang", "ja-books.json");
                _books = File.Exists(path)
                    ? JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, List<string>>>>(File.ReadAllText(path)) ?? []
                    : [];
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Lang] 대본 번역을 못 읽었습니다 — {ex.Message}");
                _books = [];
            }
        }
        return _books.TryGetValue(book, out var parts) && parts.TryGetValue(part.ToString(), out var lines)
               && lines.Count == count && index < lines.Count && lines[index].Length > 0
            ? lines[index]
            : null;
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
