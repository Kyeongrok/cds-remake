using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace CdsHelper.Game.Local.Helpers.Languages;

/// <summary>
/// 번역표를 쓰는 언어의 밑 — 코드 속 한국어 글을 <b>그대로 열쇠로</b> <c>lang/{코드}.json</c> 에서 찾고,
/// 대본 대사는 <c>lang/{코드}-books.json</c> 에서 자리로 찾는다.
/// </summary>
/// <remarks>
/// 찾는 차례(<see cref="Text"/>) — 통째로 → 틀(「[{0}]에 들어간다」 · 「&lt;금액&gt;」 자리) → 줄마다 → 띄어 쓴 이름.
/// 다 못 찾으면 한국어 그대로 낸다. 한 번 맞춰 본 글은 들고 있는다.
/// </remarks>
public abstract class TranslatedLanguage : ILanguage
{
    public abstract string Code { get; }
    public abstract string Name { get; }
    public abstract bool DrawsWithSystemFont(char ch);
    public abstract IReadOnlyList<(string Label, string? Glyphs)> InputPages { get; }
    public bool TranslatesWpfText => true;
    public bool HasScriptLines => true;

    /// <summary>한국어 조사는 안 붙인다 — 이름만 남아 번역되고, 조사는 번역된 문장 틀이 제 말로 붙인다.</summary>
    public bool UsesKoreanParticles => false;

    /// <summary>띄어 쓴 이름(이름 · 성)을 이을 때 사이에 넣는 글 — 일본어는 원본처럼 「＝」.</summary>
    protected virtual string NameJoiner => " ";

    private Dictionary<string, string>? _table;
    private Dictionary<string, Dictionary<string, List<string>>>? _books;
    private List<(Regex Pattern, string Translated, string Literal)>? _templates;
    private readonly Dictionary<string, string> _seen = [];

    public string Text(string korean)
    {
        if (string.IsNullOrEmpty(korean)) return korean;
        if (Table().TryGetValue(korean, out var done) && done.Length > 0) return done;
        // 한글이 없으면 이미 번역된 글(칸마다 번역해 이은 줄 따위)이다 — 다시 맞춰 보지 않는다.
        if (!korean.Any(c => c is >= '가' and <= '힣')) return korean;
        lock (_seen)
        {
            if (_seen.TryGetValue(korean, out var known)) return known;
            string made = ByTemplate(korean) ?? ByLines(korean) ?? ByWords(korean) ?? BySegments(korean) ?? korean;
            if (_seen.Count > 20000) _seen.Clear();
            _seen[korean] = made;
            return made;
        }
    }

    public string? ScriptLine(string book, int part, int index, int count)
    {
        if (index < 0) return null;
        _books ??= Load<Dictionary<string, Dictionary<string, List<string>>>>($"{Code}-books.json");
        return _books.TryGetValue(book, out var parts) && parts.TryGetValue(part.ToString(), out var lines)
               && lines.Count == count && index < lines.Count && lines[index].Length > 0
            ? lines[index]
            : null;
    }

    // ── 찾기 ───────────────────────────────────────────────────────────────

    /// <summary>
    /// 띄어 쓴 이름(「바르톨로메우 디아스」 — 이름과 성)을 낱말마다 찾아 <see cref="NameJoiner"/> 로 잇는다.
    /// 셋 넘는 낱말이거나 하나라도 못 찾으면 null — 문장을 낱말로 쪼개 옮기지는 않는다.
    /// </summary>
    private string? ByWords(string korean)
    {
        var words = korean.Split(' ');
        if (words.Length is < 2 or > 3) return null;
        var done = new string[words.Length];
        for (int i = 0; i < words.Length; i++)
        {
            if (!Table().TryGetValue(words[i], out var word) || word.Length == 0 || HasHangul(word)) return null;
            done[i] = word;
        }
        return string.Join(NameJoiner, done);
    }

    /// <summary>
    /// 조각을 구분자로 이은 글(「항구 · 베렌의 탑」, 「궁합 코드 3  ·  사자자리」, 「명성: 1200」)을 조각마다 찾는다 —
    /// 코드가 자리만으로 잇는 글(<c>$"{kind} · {name}"</c>)은 고정된 글자가 없어 틀로는 못 맞춘다.
    /// 한글이 든 조각이 하나라도 번역이 안 되면 null(섞어 내지 않는다). 구분자는 그대로 둔다.
    /// </summary>
    private string? BySegments(string korean)
    {
        var pieces = Separator.Split(korean);
        if (pieces.Length < 3) return null;   // 구분자가 하나도 없다
        bool any = false;
        for (int i = 0; i < pieces.Length; i += 2)   // 짝수 칸이 조각, 홀수 칸이 구분자
        {
            string piece = pieces[i].Trim();
            if (!HasHangul(piece)) continue;
            string done = Text(piece);
            if (HasHangul(done)) return null;
            pieces[i] = pieces[i].Replace(piece, done);
            any = true;
        }
        return any ? string.Concat(pieces) : null;
    }

    /// <summary>조각 구분자 — 가운뎃점 · 콜론 · 두 칸 넘는 띄움 · 줄표 · 빗금 · 괄호.</summary>
    private static readonly Regex Separator = new(@"(\s*[·・]\s*|:\s+|\s{2,}|\s+[—–-]\s+|\s*/\s*|\s*[()（）\[\]]\s*)", RegexOptions.Compiled);

    /// <summary>여러 줄 글은 줄마다 따로 찾는다. 하나도 못 찾으면 null.</summary>
    private string? ByLines(string korean)
    {
        if (!korean.Contains('\n')) return null;
        var lines = korean.Split('\n');
        bool any = false;
        for (int i = 0; i < lines.Length; i++)
        {
            string line = lines[i].TrimEnd('\r');
            if (line.Length == 0) continue;
            string done = Text(line);
            if (done != line) { lines[i] = done; any = true; }
        }
        return any ? string.Join('\n', lines) : null;
    }

    /// <summary>
    /// 이름이 끼는 글(「[{0}]에 들어간다」 꼴)을 틀로 맞춰 바꾼다. 자리에 든 글도 번역해 넣는다. 못 맞추면 null.
    /// </summary>
    /// <remarks>
    /// 자리 바로 뒤에 또 자리가 붙은 꼴(「{0}{1} 데리고」)은 뒷자리가 거의 늘 한국어 조사다(<c>GameUi.Josa</c>) —
    /// 그 자리는 조사로만 맞춘다. 자리 글이 번역이 안 돼 한글이 남으면 틀이 엉뚱한 글에 걸린 것이라 다음 틀을 본다.
    /// </remarks>
    private string? ByTemplate(string korean)
    {
        _templates ??= BuildTemplates();
        foreach (var (pattern, translated, literal) in _templates)
        {
            if (literal.Length > 0 && !korean.Contains(literal)) continue;
            var m = pattern.Match(korean);
            if (!m.Success) continue;
            string made = Hole.Replace(translated, x =>
            {
                var g = m.Groups[GroupOf(x.Value)];
                return g.Success ? Text(g.Value) : "";
            });
            if (HasHangul(made) && !HasHangul(translated)) continue;
            return made;
        }
        return null;
    }

    /// <summary>틀의 자리 — 코드가 채우는 <c>{0}</c> 과, 대사가 낼 때 채우는 <c>&lt;금액&gt;</c> · <c>&lt;제독&gt;</c> 같은 이름 자리.</summary>
    private static readonly Regex Hole = new(@"\{\d+\}|<[^<>\s]{1,8}>", RegexOptions.Compiled);

    /// <summary>자리 글 → 정규식 무리 이름(글자만 남긴다).</summary>
    private static string GroupOf(string hole) =>
        "h" + string.Concat(hole.Select(ch => char.IsLetterOrDigit(ch) ? ch.ToString() : ((int)ch).ToString("X")));

    protected static bool HasHangul(string text) => text.Any(IsHangul);

    /// <summary>한글(자모 · 완성형)인지.</summary>
    protected static bool IsHangul(char ch) =>
        ch is >= '가' and <= '힣' or >= 'ᄀ' and <= 'ᇿ' or >= '㄰' and <= '㆏';

    /// <summary>한국어 조사 — 자리 바로 뒤에 붙는 자리는 이것들로만 맞춘다.</summary>
    private const string Particles = "이라는|라는|이라고|라고|으로|로|에게|에서|에|의|이|가|을|를|은|는|와|과|이야|야|아|이여|여|";

    private List<(Regex, string, string)> BuildTemplates()
    {
        var list = new List<(Regex, string, string)>();
        foreach (var (key, translated) in Table())
        {
            if (translated.Length == 0 || !Hole.IsMatch(key)) continue;
            var regex = new System.Text.StringBuilder("^");
            string longest = "";
            var used = new HashSet<string>();
            int at = 0, previousEnd = -1;   // 바로 앞 자리가 끝난 곳 — 이 자리가 거기서 곧장 이어지면 조사 자리다
            foreach (Match hole in Hole.Matches(key))
            {
                string literal = key[at..hole.Index];
                regex.Append(Regex.Escape(literal));
                if (literal.Length > longest.Length) longest = literal;
                string name = GroupOf(hole.Value);
                // 같은 자리가 두 번 나오면 뒤엣것은 앞엣것과 같은 글이어야 한다.
                if (!used.Add(name)) regex.Append(@"\k<").Append(name).Append('>');
                else if (previousEnd == hole.Index && hole.Value.StartsWith('{')) regex.Append($"(?<{name}>{Particles})");
                else regex.Append($"(?<{name}>.+?)");
                at = previousEnd = hole.Index + hole.Length;
            }
            string tail = key[at..];
            regex.Append(Regex.Escape(tail)).Append('$');
            if (tail.Length > longest.Length) longest = tail;
            // 글자가 하나도 없는 틀(「{0}{1}」)은 아무 글에나 걸리고 자기 자신을 다시 부른다 — 안 쓴다.
            if (longest.Trim().Length == 0) continue;
            try { list.Add((new Regex(regex.ToString(), RegexOptions.Singleline), translated, longest)); }
            catch (ArgumentException) { /* 틀이 깨졌으면 건너뛴다 */ }
        }
        // 글자가 많이 박힌 틀부터 — 짧은 틀이 먼저 걸리지 않게.
        list.Sort((a, b) => b.Item3.Length.CompareTo(a.Item3.Length));
        return list;
    }

    // ── 표 읽기 ─────────────────────────────────────────────────────────────

    private Dictionary<string, string> Table() => _table ??= Load<Dictionary<string, string>>($"{Code}.json");

    /// <summary><c>lang/</c> 의 파일 하나를 읽는다. 없거나 못 읽으면 빈 것.</summary>
    private static T Load<T>(string file) where T : new()
    {
        try
        {
            string path = Path.Combine(AppContext.BaseDirectory, "lang", file);
            return File.Exists(path) ? JsonSerializer.Deserialize<T>(File.ReadAllText(path)) ?? new T() : new T();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Lang] {file} 을 못 읽었습니다 — {ex.Message}");
            return new T();
        }
    }
}

/// <summary>
/// 일본어 — 원본 일본어판 그대로. 이름 · 설명 · 대본 대사는 일본어판 CDS95.EXE · DISEV.CDS 에서 구운 것이고,
/// 리메이크가 새로 쓴 글은 옮긴 것이다(<c>lang/ja.json</c> · <c>lang/ja-books.json</c>).
/// </summary>
public sealed class JapaneseLanguage : TranslatedLanguage
{
    public override string Code => "ja";
    public override string Name => "日本語";

    /// <summary>원본 일본어판처럼 이름과 성을 「＝」로 잇는다(バルトロメウ＝ディアス).</summary>
    protected override string NameJoiner => "＝";

    /// <summary>한글이 아니면 다 MS 고딕으로 — 원본 일본어판은 글꼴 파일 없이 「ＭＳ ゴシック」 16px 로 찍는다(<c>0x004AC2FD</c>).</summary>
    public override bool DrawsWithSystemFont(char ch) => !IsHangul(ch);

    /// <summary>가나 판 — 오십음 · 탁음 · 작은 글자를 한 줄 열아홉씩.</summary>
    private const string Hiragana =
        "あいうえおかきくけこさしすせそたちつてとなにぬねのはひふへほまみむめもやゆよらりるれろわをん"
        + "がぎぐげござじずぜぞだぢづでどばびぶべぼぱぴぷぺぽぁぃぅぇぉっゃゅょゎー・";

    private static readonly string Katakana =
        new string(Hiragana.Select(ch => ch is >= 'ぁ' and <= 'ゖ' ? (char)(ch + 0x60) : ch).ToArray()) + "ヴ";

    public override IReadOnlyList<(string Label, string? Glyphs)> InputPages { get; } =
        [("かな", Hiragana), ("カナ", Katakana)];
}
