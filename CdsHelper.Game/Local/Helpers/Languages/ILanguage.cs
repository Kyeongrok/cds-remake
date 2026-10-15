namespace CdsHelper.Game.Local.Helpers.Languages;

/// <summary>
/// 화면 글의 언어 하나 — 원본 게임이 글을 내는 출구마다 이 전략에 맡긴다(<see cref="Lang.Current"/>).
/// </summary>
/// <remarks>
/// 원본은 글을 몇 안 되는 손으로만 낸다 — 얼굴 달린 대사(<c>0x004694C0</c>), 부관 대사(<c>0x00469680</c>),
/// 알림 창(<c>0x00469060</c>), 아래 띠 말(<c>0x0040E0A0</c>), 그리고 대본 해석기의 대사 명령(<c>0x00408B5B</c>).
/// 리메이크도 그대로 좁은 출구로 모인다.
/// <code>
///   ConfirmDialog       대화 · 알림 · 물음 창 하나(TalkDialog · NoticeDialog 가 다 이리 온다)   → Text
///   GameFont.Render     메뉴 줄 · 단추 · 띠 · 표 글(게임 글꼴로 찍는 것 전부)                → Text
///   DisevRunner.Speak   대본 대사 — 원본 대본의 그 자리 대사                               → ScriptLine
///   GameFont.TryGlyph   글자 하나를 어느 글꼴로 찍나                                       → DrawsWithSystemFont
///   TextInputDialog     이름 입력판                                                      → InputPages
///   TextBlock(윈도 글자) 게임 글꼴을 안 쓰는 창                                            → TranslatesWpfText
/// </code>
/// 코드 속 글은 한국어로 적혀 있고, 언어는 출구에서만 갈린다 — 호출하는 쪽 수천 곳은 손대지 않는다.
/// 새 언어는 이 꼴 하나(번역표를 쓰는 언어면 <see cref="TranslatedLanguage"/> 를 이어받아)를 더하면 된다.
/// </remarks>
public interface ILanguage
{
    /// <summary>설정에 적는 코드("ko" · "ja").</summary>
    string Code { get; }

    /// <summary>그 언어로 적은 이름 — 설정 창에 뜬다.</summary>
    string Name { get; }

    /// <summary>화면에 낼 글. 코드 속 한국어 글을 받아 이 언어 글로 낸다. 모르면 받은 그대로.</summary>
    string Text(string korean);

    /// <summary>원본 대본의 대사를 자리로 가져오는지 — 아니면 <see cref="ScriptLine"/> 을 안 부른다.</summary>
    bool HasScriptLines { get; }

    /// <summary>
    /// 대본 대사 — 책 · 파트 · 파트 안 대사 차례로 찾는다. 없으면 null(한국어 대사를 <see cref="Text"/> 로 낸다).
    /// </summary>
    /// <param name="count">그 파트의 대사 수 — 구울 때와 다르면(대본을 고쳤으면) 짝이 어긋나므로 null.</param>
    string? ScriptLine(string book, int part, int index, int count);

    /// <summary>그 글자를 게임 글꼴이 아니라 윈도 글꼴(MS 고딕)로 찍는지(<see cref="GdiGlyphs"/>).</summary>
    bool DrawsWithSystemFont(char ch);

    /// <summary>
    /// 이름 입력판의 글자 판들 — (단추 이름, 깔 글자). 글자가 null 이면 한글 초성 고르기 판이다.
    /// </summary>
    IReadOnlyList<(string Label, string? Glyphs)> InputPages { get; }

    /// <summary>보통 윈도 글자(TextBlock) · 창 제목도 바꾸는지.</summary>
    bool TranslatesWpfText { get; }

    /// <summary>
    /// 이름 뒤에 한국어 조사(을/를 · 이/가 …)를 붙이는지. 아니면 조사 손(<c>GameUi.Josa</c> · <c>NameToken.Of</c> 따위)이
    /// 빈 글을 낸다 — 「해적선이」가 「해적선」으로 남아 이름만 번역되고, 그 언어의 조사는 번역된 문장 틀이 붙인다.
    /// </summary>
    bool UsesKoreanParticles { get; }
}

/// <summary>한국어 — 코드 속 글이 곧 화면 글이다. 아무것도 안 바꾼다.</summary>
public sealed class KoreanLanguage : ILanguage
{
    public string Code => "ko";
    public string Name => "한국어";
    public string Text(string korean) => korean;
    public bool HasScriptLines => false;
    public string? ScriptLine(string book, int part, int index, int count) => null;

    /// <summary>한글 · 영문 · 기호는 게임 글꼴로 찍고, 거기 없는 글자(한자 따위)만 <see cref="GameFont"/> 가 메운다.</summary>
    public bool DrawsWithSystemFont(char ch) => false;

    public IReadOnlyList<(string Label, string? Glyphs)> InputPages { get; } = [("한글", null)];
    public bool TranslatesWpfText => false;
    public bool UsesKoreanParticles => true;
}
