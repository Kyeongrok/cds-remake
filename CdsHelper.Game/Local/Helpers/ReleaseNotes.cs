using System.IO;
using System.Text;

namespace CdsHelper.Game.Local.Helpers;

/// <summary>
/// 릴리즈 노트(<c>릴리즈노트.md</c>) — 업데이트한 뒤 처음 켤 때 그 사이 판들의 대목을 보여 준다.
/// </summary>
/// <remarks>
/// 파일은 저장소 뿌리에 있고 exe 옆에 같이 놓인다. 판마다 <c>## v1.0.51</c> 줄로 대목을 열고 그 아래에
/// 바뀐 것을 적는다. 릴리즈 빌드(<c>release.yml</c>)도 같은 대목을 GitHub 릴리즈 본문으로 쓴다.
/// </remarks>
public static class ReleaseNotes
{
    /// <summary>exe 옆에 놓이는 파일 이름.</summary>
    public const string FileName = "릴리즈노트.md";

    /// <summary>한 번에 보여 주는 판 수 — 여러 판을 건너뛰어도 창이 화면을 넘지 않게 새 것부터 이만큼만 낸다.</summary>
    private const int MaxSections = 3;

    /// <summary>exe 옆의 노트 글. 없거나 못 읽으면 빈 글이다.</summary>
    public static string Read()
    {
        try
        {
            string path = Path.Combine(AppContext.BaseDirectory, FileName);
            return File.Exists(path) ? File.ReadAllText(path) : "";
        }
        catch (IOException) { return ""; }
        catch (UnauthorizedAccessException) { return ""; }
    }

    /// <summary>
    /// <paramref name="seen"/> 뒤부터 <paramref name="current"/> 까지의 대목을 새 판부터 이어 낸다. 보여 줄 것이 없으면 빈 글.
    /// </summary>
    /// <remarks>
    /// <b>지금 판의 대목이 노트에 있을 때만</b> 낸다 — 개발 중 빌드처럼 노트에 없는 판은 아무것도 안 띄운다.
    /// <paramref name="seen"/> 이 비었으면(처음 깔았거나 이 기능이 없던 판에서 올라왔으면) 지금 판 대목 하나만 낸다.
    /// </remarks>
    public static string Since(string text, string seen, Version current)
    {
        var sections = Parse(text);
        current = Trim(current);
        if (!sections.Exists(s => s.Version == current)) return "";

        Version? from = Version.TryParse(seen, out var parsed) ? Trim(parsed) : null;
        var shown = sections
            .Where(s => s.Version <= current && (from == null ? s.Version == current : s.Version > from))
            .OrderByDescending(s => s.Version)
            .Take(MaxSections);

        return string.Join(Environment.NewLine + Environment.NewLine,
                           shown.Select(s => $"v{s.Version}{Environment.NewLine}{s.Body}"));
    }

    /// <summary>판 번호를 세 자리로 맞춘다 — 어셈블리 판은 넷째 자리(0)가 붙어 온다.</summary>
    /// <summary>
    /// 가장 새 판부터 <paramref name="count"/> 판의 대목 — 햄버거 「릴리즈 노트」가 언제든 다시 보여 준다.
    /// 노트가 없으면 빈 글이다.
    /// </summary>
    public static string Recent(string text, int count = 2) =>
        string.Join(Environment.NewLine + Environment.NewLine,
                    Parse(text).OrderByDescending(s => s.Version).Take(count)
                               .Select(s => $"v{s.Version}{Environment.NewLine}{s.Body}"));

    public static Version Trim(Version v) => new(v.Major, Math.Max(0, v.Minor), Math.Max(0, v.Build));

    private static List<(Version Version, string Body)> Parse(string text)
    {
        var sections = new List<(Version, string)>();
        Version? version = null;
        var body = new StringBuilder();

        void Close()
        {
            if (version != null) sections.Add((version, body.ToString().Trim()));
            body.Clear();
        }

        bool comment = false;
        foreach (string raw in text.Replace("\r\n", "\n").Split('\n'))
        {
            string line = raw.TrimEnd();

            // 파일 머리의 적는 법 안내(<!-- ... -->)는 건너뛴다.
            if (line.StartsWith("<!--")) comment = true;
            if (comment) { if (line.EndsWith("-->")) comment = false; continue; }

            if (line.StartsWith("## "))
            {
                Close();
                version = Version.TryParse(line[3..].Trim().TrimStart('v', 'V'), out var v) ? Trim(v) : null;
                continue;
            }
            if (version != null) body.AppendLine(line);
        }
        Close();
        return sections;
    }
}
