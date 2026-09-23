using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

using CdsHelper.Game.Local.Helpers;

namespace CdsHelper.Game.UI.Views;

/// <summary>
/// 「문자입력」 — 글자판을 <b>마우스로 하나씩 찍어</b> 글을 짓는 창.
/// </summary>
/// <remarks>
/// 게임에는 글쇠판 입력이 없다. 글꼴이 비트맵이라 글자를 찍어 넣는 판을 따로 두고 그것을
/// 눌러 짓는다 — 선명입력의 오른쪽 위 작은 단추(계산기처럼 생겼다)를 누르면 이 판이 뜬다.
///
/// 게임의 <c>0x004AFDF3</c> 창(400 x 280)이다. 자리는 모두 창 속 왼쪽 위에서 잰다.
/// <code>
///   입력 줄  (16,16)  304 x 16   "%-38s" 로 크림 띠를 채워 찍는다(0x004AF82F)
///   판       (16,48)  304 x 192  한 칸 16 x 16 — 19칸 x 12줄
///   단추     x 344 · 48 x 24     결정 16 · 뒤로 48 · 영문 80 · 한글 112 · 삭제 144
/// </code>
/// 판은 세 가지다(<c>[+0x26C]</c> 판 · <c>[+0x278]</c> 초성).
/// <list type="bullet">
/// <item><b>영문</b> — 표 <c>0x0057C6B8</c> 의 열두 줄. 줄마다 앞의 빈칸 둘이 첫 칸을 비운다.</item>
/// <item><b>초성 고르기</b> — "한글" 을 누르면 판에 두 줄만 뜬다(<c>0x004AF7B2</c>).
///   <c>가까나다따 싸아자짜차 / 라마바빠사 카타파하</c>.</item>
/// <item><b>음절</b> — 고른 초성으로 시작하는 <b>완성형 음절 전부</b>를 코드 차례로 한 줄에 열아홉씩
///   깐다(<c>0x004AF4E0</c>). 범위는 표 <c>0x0057C6E8</c> 의 {처음, 끝} 이다. 가장 많은 "아" 가
///   208자라 열두 줄(228칸)을 안 넘는다 — 게임의 굴림대는 실제로는 안 선다.</item>
/// </list>
/// 낱자를 모아 짓는 판이 아니다. 예전에는 첫소리·가운뎃소리·받침을 늘어놓고 모아 주었는데,
/// 받침이 다음 자로 못 넘어가고("가" 뒤에 ㄱ·ㅏ 가 "각아") "없음" 칸이 글자 그대로 찍혔다.
///
/// 찍는 규칙(<c>0x004AF8D0</c>).
/// <list type="bullet">
/// <item>판의 글자를 누르면 <b>커서 자리에 덮어쓰고</b> 커서가 한 자 나아간다(끼워 넣지 않는다).</item>
/// <item>입력 줄의 글자를 누르면 커서가 그 자로 간다. 글 끝 뒤를 눌러서는 안 옮겨진다.</item>
/// <item>"삭제" 는 커서 <b>앞</b> 한 자를 지우고 뒤를 당긴다.</item>
/// <item>"뒤로" 는 그만두기다. 오른쪽 단추는 「페이지선택」(영문·한글) 차림표를 띄운다(<c>0x004B0411</c>).</item>
/// <item>판은 <b>빈 채로</b> 열린다 — 게임이 버퍼를 0 으로 채워 잡는다.</item>
/// </list>
/// 게임은 글자를 찍을 때 딸깍 소리(<c>0x00428140(0)</c>)를 내는데 여기서는 아직 안 낸다.
/// </remarks>
public sealed class TextInputDialog : GameWindow
{
    // ── 창 속 자리(게임 그대로) ─────────────────────────────────────────────────

    private const double BodyWidth = 400, BodyHeight = 256;
    private const double LineX = 16, LineY = 16, PageX = 16, PageY = 48;
    private const int Cell = 16, Columns = 19, Rows = 12;
    private const double ButtonX = 344, ButtonWidth = 48, ButtonTop = 16, ButtonStep = 32;

    /// <summary>커서 — 글자 칸 아래에 붙는 밑줄 막대(<c>0x004AF865</c> 의 (0,12)-(15,15)).</summary>
    private const double CursorTop = 12, CursorHeight = 4;

    /// <summary>판 바탕(색 <c>0x2B</c>)과 글자·커서(색 <c>0x49</c>). 갈무리에서 뽑았다.</summary>
    private static readonly Brush Paper = Frozen(Color.FromRgb(0xD6, 0xCE, 0xB5));
    private static readonly Brush CursorInk = Frozen(Color.FromRgb(0x1C, 0x1D, 0x26));
    private const byte InkIndex = 0x49;

    // ── 판 ────────────────────────────────────────────────────────────────────

    /// <summary>영문 판 열두 줄(<c>0x0057C6B8</c>). 빈 줄은 게임도 빈 문자열이다.</summary>
    private static readonly string[] Roman =
    [
        "",
        "ＡＢＣＤＥＦＧＨＩＪＫＬＭ",
        "ＮＯＰＱＲＳＴＵＶＷＸＹＺ",
        "ａｂｃｄｅｆｇｈｉｊｋｌｍ",
        "ｎｏｐｑｒｓｔｕｖｗｘｙｚ",
        "",
        "０１２３４５６７８９",
        "　‘’，．·：；？！",
        "＋－±×÷＝≠＜＞≤≥∞∴",
        "〔〕「」《》【】（）…∼",
        "",
        "",
    ];

    /// <summary>
    /// 초성 고르기 두 줄(<c>0x0057C794</c> · <c>0x0057C77C</c>). 빈칸 둘이 한 칸을 비운다.
    /// </summary>
    private static readonly string[] Initials = ["가까나다따\u0000싸아자짜차", "라마바빠사\u0000카타파하"];

    /// <summary>
    /// 초성마다 완성형 코드 범위(<c>0x0057C6E8</c>, 한 칸이 {처음, 끝, 0xFFFF}).
    /// ㄱ ㄲ ㄴ ㄷ ㄸ ㄹ ㅁ ㅂ ㅃ ㅅ ㅆ ㅇ ㅈ ㅉ ㅊ ㅋ ㅌ ㅍ ㅎ 차례다.
    /// </summary>
    private static readonly (int First, int Last)[] Leads =
    [
        (0xB0A1, 0xB1ED), (0xB1EE, 0xB3A9), (0xB3AA, 0xB4D8), (0xB4D9, 0xB5FA),
        (0xB5FB, 0xB6F2), (0xB6F3, 0xB8B5), (0xB8B6, 0xB9D8), (0xB9D9, 0xBAFB),
        (0xBAFC, 0xBBE6), (0xBBE7, 0xBDCD), (0xBDCE, 0xBEC5), (0xBEC6, 0xC0D9),
        (0xC0DA, 0xC2A4), (0xC2A5, 0xC2F6), (0xC2F7, 0xC4AA), (0xC4AB, 0xC5B7),
        (0xC5B8, 0xC6C3), (0xC6C4, 0xC7CE), (0xC7CF, 0xC8FE),
    ];

    /// <summary>판 칸 표 — 칸마다 찍힐 글자. 빈 칸은 null(<c>[+0xA4]</c> 의 워드 표).</summary>
    private readonly string?[,] _cells = new string?[Rows, Columns];

    /// <summary>초성 고르기 칸 표 — 칸마다 초성 번호, 없으면 -1.</summary>
    private readonly int[,] _leadCells = new int[Rows, Columns];

    private enum Page { Roman, Initials, Syllables }

    private Page _page;

    // ── 글 ────────────────────────────────────────────────────────────────────

    private readonly StringBuilder _text = new();
    private int _cursor;
    private readonly int _maxLength;
    private string? _result;

    private readonly Canvas _pageLayer = new() { Width = Columns * Cell, Height = Rows * Cell };
    private readonly Canvas _line = new() { Width = Columns * Cell, Height = Cell, IsHitTestVisible = false };
    private readonly Rectangle _caret = new() { Width = Cell, Height = CursorHeight, Fill = CursorInk };

    private TextInputDialog(int maxLength, string caption)
    {
        _maxLength = maxLength;

        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.WidthAndHeight;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        Background = GameUi.Back;

        var body = new Canvas { Width = BodyWidth, Height = BodyHeight, Background = GameUi.Back };

        // 입력 줄 — 크림 띠 위에 글을 찍고, 누르면 그 자로 커서를 옮긴다.
        var lineBack = new Border { Width = Columns * Cell, Height = Cell, Background = Paper };
        lineBack.MouseLeftButtonUp += (_, e) => { e.Handled = true; PointLine(e.GetPosition(lineBack)); };
        Place(body, lineBack, LineX, LineY);

        Place(body, _line, LineX, LineY);
        _caret.IsHitTestVisible = false;
        body.Children.Add(_caret);

        // 판.
        var pageBack = new Border
        {
            Width = Columns * Cell,
            Height = Rows * Cell,
            Background = Paper,
            Child = _pageLayer,
        };
        pageBack.MouseLeftButtonUp += (_, e) => { e.Handled = true; PointPage(e.GetPosition(pageBack)); };
        Place(body, pageBack, PageX, PageY);

        // 오른쪽 단추 다섯. 게임 차례 그대로다.
        (string Text, Action Run)[] buttons =
        [
            ("결정", Decide), ("뒤로", Cancel), ("영문", ShowRoman), ("한글", ShowInitials), ("삭제", Delete),
        ];
        for (int i = 0; i < buttons.Length; i++)
        {
            var button = new GameButton(buttons[i].Text, buttons[i].Run, width: ButtonWidth)
            {
                Margin = default,
            };
            Place(body, button, ButtonX, ButtonTop + i * ButtonStep);
        }

        var title = GameUi.TitleBar(caption, null);
        GameUi.EnableDrag(this, title);

        var stack = new StackPanel();
        stack.Children.Add(title);
        stack.Children.Add(body);
        Content = stack;

        ShowRoman();
        Sync();

        KeyDown += (_, e) => { if (e.Key is Key.Escape) Cancel(); };
        MouseRightButtonUp += (_, e) =>
        {
            e.Handled = true;
            GameUi.ContextMenuAt(this, e.GetPosition(this),
                                 [("영문", ShowRoman), ("한글", ShowInitials)]);
        };
    }

    private static void Place(Canvas canvas, UIElement child, double x, double y)
    {
        Canvas.SetLeft(child, x);
        Canvas.SetTop(child, y);
        canvas.Children.Add(child);
    }

    // ── 판 바꾸기 ──────────────────────────────────────────────────────────────

    /// <summary>판을 비운다 — 칸 표도 그림도.</summary>
    private void ClearPage(Page page)
    {
        _page = page;
        _pageLayer.Children.Clear();
        Array.Clear(_cells);
        for (int r = 0; r < Rows; r++)
            for (int c = 0; c < Columns; c++)
                _leadCells[r, c] = -1;
    }

    /// <summary>
    /// 한 줄을 판에 찍는다. <paramref name="glyphs"/> 는 한 글자가 한 칸이고 <c>\0</c> 은 빈 칸이다.
    /// </summary>
    private void DrawRow(int row, int column, string glyphs) =>
        DrawGlyphs(_pageLayer, row * Cell, column, glyphs);

    /// <summary>
    /// 글자를 <b>한 칸에 하나씩</b> 놓는다 — 줄을 통째로 찍으면 글꼴에 없는 글자가 폭 0 으로
    /// 빠져 뒤 칸이 당겨진다. 게임은 칸 자리를 셈으로 가르므로 칸이 어긋나면 안 된다.
    /// </summary>
    private static void DrawGlyphs(Canvas layer, double top, int column, string glyphs)
    {
        for (int i = 0; i < glyphs.Length; i++)
        {
            if (glyphs[i] is '\0' or '　') continue;
            var label = Ink(glyphs[i].ToString());
            label.IsHitTestVisible = false;
            Canvas.SetLeft(label, (column + i) * Cell);
            Canvas.SetTop(label, top);
            layer.Children.Add(label);
        }
    }

    /// <summary>영문 판(<c>0x004AF630</c>). 글자는 둘째 칸부터 선다.</summary>
    private void ShowRoman()
    {
        ClearPage(Page.Roman);
        for (int r = 0; r < Rows; r++)
        {
            string row = Roman[r];
            if (row.Length == 0) continue;
            for (int c = 0; c < row.Length && c + 1 < Columns; c++)
                _cells[r, c + 1] = row[c].ToString();
            DrawRow(r, 1, row);
        }
    }

    /// <summary>
    /// 초성 고르기(<c>0x004AF7B2</c>). 둘째 줄부터 두 줄, 둘째 칸부터 다섯씩 두 묶음이다.
    /// </summary>
    /// <remarks>
    /// 누른 칸을 번호로 바꾸는 식이 <c>0x004AFB8B</c> 에 있다 — 윗줄 왼쪽 0~4 · 오른쪽 10~14,
    /// 아랫줄 왼쪽 5~9 · 오른쪽 15~18. 곧 ㄱ부터 ㅎ까지 제 차례다.
    /// </remarks>
    private void ShowInitials()
    {
        ClearPage(Page.Initials);
        int[][] numbers = [[0, 1, 2, 3, 4, -1, 10, 11, 12, 13, 14], [5, 6, 7, 8, 9, -1, 15, 16, 17, 18]];
        for (int r = 0; r < Initials.Length; r++)
        {
            for (int c = 0; c < numbers[r].Length; c++)
                _leadCells[r + 1, c + 1] = numbers[r][c];
            DrawRow(r + 1, 1, Initials[r]);
        }
    }

    /// <summary>그 초성의 완성형 음절을 다 깐다(<c>0x004AF4E0</c>).</summary>
    private void ShowSyllables(int lead)
    {
        ClearPage(Page.Syllables);
        var (first, last) = Leads[lead];
        var cp949 = Cp949;

        var all = new List<string>();
        for (int code = first; code <= last; code++)
        {
            int low = code & 0xFF;
            if (low < 0xA1 || low > 0xFE) continue;
            all.Add(cp949.GetString([(byte)(code >> 8), (byte)low]));
        }

        for (int r = 0; r < Rows && r * Columns < all.Count; r++)
        {
            var row = all.Skip(r * Columns).Take(Columns).ToList();
            for (int c = 0; c < row.Count; c++) _cells[r, c] = row[c];
            DrawRow(r, 0, string.Concat(row));
        }
    }

    private static Encoding? _cp949;

    private static Encoding Cp949
    {
        get
        {
            if (_cp949 != null) return _cp949;
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            return _cp949 = Encoding.GetEncoding(949);
        }
    }

    // ── 누르기 ─────────────────────────────────────────────────────────────────

    /// <summary>판을 눌렀다.</summary>
    private void PointPage(Point at)
    {
        int c = (int)(at.X / Cell), r = (int)(at.Y / Cell);
        if (c < 0 || c >= Columns || r < 0 || r >= Rows) return;

        if (_page == Page.Initials)
        {
            if (_leadCells[r, c] is >= 0 and var lead) ShowSyllables(lead);
            return;
        }

        if (_cells[r, c] is { } glyph) Type(glyph);
    }

    /// <summary>
    /// 한 자 찍는다 — 커서 자리에 <b>덮어쓰고</b> 한 자 나아간다. 길이가 찼으면 안 받는다.
    /// </summary>
    private void Type(string glyph)
    {
        if (_cursor >= _maxLength) return;

        if (_cursor < _text.Length) _text[_cursor] = glyph[0];
        else _text.Append(glyph);
        _cursor++;
        Sync();
    }

    /// <summary>입력 줄을 눌렀다 — 글이 있는 자리면 커서를 그리로 옮긴다.</summary>
    private void PointLine(Point at)
    {
        int pos = (int)(at.X / Cell);
        if (pos >= 0 && pos < _text.Length)
        {
            _cursor = pos;
            Sync();
        }
    }

    /// <summary>커서 앞의 한 자를 지운다.</summary>
    private void Delete()
    {
        if (_cursor <= 0) return;
        _cursor--;
        _text.Remove(_cursor, 1);
        Sync();
    }

    /// <summary>입력 줄과 커서를 다시 찍는다.</summary>
    private void Sync()
    {
        _line.Children.Clear();
        DrawGlyphs(_line, 0, 0, _text.ToString());
        Canvas.SetLeft(_caret, LineX + _cursor * Cell);
        Canvas.SetTop(_caret, LineY + CursorTop);
        _caret.Visibility = _cursor < Columns ? Visibility.Visible : Visibility.Hidden;
    }

    private void Decide()
    {
        _result = _text.ToString().Replace('　', ' ').Trim();
        Close();
    }

    private void Cancel()
    {
        _result = null;
        Close();
    }

    /// <summary>판과 입력 줄의 글자. <b>게임 글꼴</b>로 찍는다.</summary>
    private static GameUi.GameLabel Ink(string text) =>
        new(InkIndex)
        {
            Text = text,
            Bold = false,
            FallbackBrush = CursorInk,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
        };

    private static Brush Frozen(Color c)
    {
        var b = new SolidColorBrush(c);
        b.Freeze();
        return b;
    }

    /// <summary>
    /// 판을 띄우고 지은 글을 낸다. 그만뒀거나 빈 글이면 null.
    /// </summary>
    /// <param name="owner">주인 창.</param>
    /// <param name="start">
    /// 예전 인자라 받기만 한다 — 게임은 판을 <b>빈 채로</b> 연다(<c>0x004AF8D0</c> 가 버퍼를 0 으로 채운다).
    /// </param>
    /// <param name="maxLength">가장 긴 길이(글자 수).</param>
    /// <param name="caption">창 제목.</param>
    public static string? Ask(Window owner, string start, int maxLength,
                              string caption = "문자입력")
    {
        _ = start;
        var dialog = new TextInputDialog(maxLength, caption) { Owner = owner };
        dialog.ShowDialog();
        return string.IsNullOrWhiteSpace(dialog._result) ? null : dialog._result;
    }
}
