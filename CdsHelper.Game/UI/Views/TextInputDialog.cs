using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

using CdsHelper.Game.Local.Helpers;

namespace CdsHelper.Game.UI.Views;

/// <summary>
/// 「문자입력」 — 글자판을 <b>마우스로 하나씩 찍어</b> 글을 짓는 창.
/// </summary>
/// <remarks>
/// 게임에는 글쇠판 입력이 없다. 글꼴이 비트맵이라 글자를 찍어 넣는 판을 따로 두고 그것을
/// 눌러 짓는다 — 선명입력의 오른쪽 위 작은 단추(계산기처럼 생겼다)를 누르면 이 판이 뜬다.
///
/// 판은 두 벌이다. <b>영문</b> 은 게임 화면 그대로 옮겼다.
/// <code>
///   A B C D E F G H I J K L M      0 1 2 3 4 5 6 7 8 9
///   N O P Q R S T U V W X Y Z      ' ' , . : ; ? !
///   a b c d e f g h i j k l m      + - ± × ÷ = ≈ &lt; &gt; ≤ ≥ ∞ ∴
///   n o p q r s t u v w x y z      ( ) 「 」 ≪ ≫ 【 】 ( ) … ~
/// </code>
/// <b>한글</b> 판은 우리가 지었다. 게임 화면을 못 봐서 짜임을 모르지만, 낱자를 찍어 모아
/// 쓰는 것 말고는 길이 없다 — 한글은 글자가 만 자가 넘어 통째로 늘어놓을 수가 없다.
/// 초성 열아홉 · 중성 스물하나 · 종성 스물일곱을 늘어놓고 찍는 대로 모아 준다.
/// </remarks>
public sealed class TextInputDialog : GameWindow
{
    /// <summary>지금까지 지은 글.</summary>
    private readonly StringBuilder _text = new();

    /// <summary>모으는 중인 한글 한 자(초성·중성·종성 자리).</summary>
    private int _lead = -1, _vowel = -1, _tail;

    private readonly GameUi.GameLabel _line;
    private readonly Border _page;
    private readonly int _maxLength;
    private string? _result;

    // ── 한글 낱자. 유니코드 조합 차례 그대로다(U+AC00 + (초x21 + 중)x28 + 종). ──
    private const string Leads = "ㄱㄲㄴㄷㄸㄹㅁㅂㅃㅅㅆㅇㅈㅉㅊㅋㅌㅍㅎ";
    private const string Vowels = "ㅏㅐㅑㅒㅓㅔㅕㅖㅗㅘㅙㅚㅛㅜㅝㅞㅟㅠㅡㅢㅣ";
    private const string Tails = " ㄱㄲㄳㄴㄵㄶㄷㄹㄺㄻㄼㄽㄾㄿㅀㅁㅂㅄㅅㅆㅇㅈㅊㅋㅌㅍㅎ";

    // ── 영문 판. 게임 화면에서 줄까지 그대로 옮겼다. ──
    private static readonly string[] Roman =
    [
        "ABCDEFGHIJKLM",
        "NOPQRSTUVWXYZ",
        "abcdefghijklm",
        "nopqrstuvwxyz",
        "",
        "0123456789",
        "‘’ ，·：；？！",
        "＋－±×÷＝≒＜＞≤≥∞∴",
        "()「」≪≫【】（）…～",
    ];

    private TextInputDialog(string start, int maxLength, string caption)
    {
        _maxLength = maxLength;
        _text.Append(start);

        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.WidthAndHeight;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        Background = GameUi.Back;

        _line = Ink("");
        _line.Margin = new Thickness(8, 3, 8, 3);
        _line.MinWidth = 300;

        _page = new Border { Padding = new Thickness(10, 8, 10, 8) };
        ShowRoman();

        // 오른쪽 단추 줄. 게임 화면 차례 그대로다.
        var side = new StackPanel { Margin = new Thickness(6, 0, 4, 0) };
        side.Children.Add(GameUi.PushButton("결정", Decide, 64));
        side.Children.Add(GameUi.PushButton("뒤로", Cancel, 64));
        side.Children.Add(GameUi.PushButton("영문", ShowRoman, 64));
        side.Children.Add(GameUi.PushButton("한글", ShowHangul, 64));
        side.Children.Add(GameUi.PushButton("삭제", Backspace, 64));

        var left = new StackPanel();
        left.Children.Add(Framed(_line, new Thickness(4, 4, 0, 0)));
        left.Children.Add(Framed(_page, new Thickness(4, 4, 0, 4)));

        var body = new DockPanel();
        DockPanel.SetDock(side, Dock.Right);
        body.Children.Add(side);
        body.Children.Add(left);

        var title = GameUi.TitleBar(caption, Cancel);
        GameUi.EnableDrag(this, title);

        var stack = new StackPanel();
        stack.Children.Add(title);
        stack.Children.Add(body);

        Content = new Border
        {
            Background = GameUi.Back,
            BorderBrush = GameUi.Edge,
            BorderThickness = new Thickness(2),
            Margin = new Thickness(4),
            Child = stack,
        };

        Sync();
        KeyDown += (_, e) => { if (e.Key is Key.Escape) Cancel(); };
        MouseRightButtonUp += (_, _) => Cancel();
    }

    private static Border Framed(UIElement child, Thickness margin) => new()
    {
        Background = GameUi.PageFill,
        BorderBrush = GameUi.ItemEdge,
        BorderThickness = new Thickness(2),
        Margin = margin,
        Child = child,
    };

    /// <summary>영문·숫자·기호 판.</summary>
    private void ShowRoman()
    {
        var rows = new StackPanel();
        foreach (string row in Roman)
        {
            if (row.Length == 0) { rows.Children.Add(new Border { Height = 10 }); continue; }
            rows.Children.Add(Keys(row.Select(c => c.ToString())));
        }
        _page.Child = rows;
    }

    /// <summary>한글 낱자 판 — 초성·중성·종성.</summary>
    private void ShowHangul()
    {
        var rows = new StackPanel();
        rows.Children.Add(Label("첫소리"));
        rows.Children.Add(Keys(Leads.Select(c => c.ToString())));
        rows.Children.Add(Label("가운뎃소리"));
        rows.Children.Add(Keys(Vowels.Select(c => c.ToString())));
        rows.Children.Add(Label("받침"));
        rows.Children.Add(Keys(Tails.Select(c => c == ' ' ? "없음" : c.ToString())));
        _page.Child = rows;
    }

    private static TextBlock Label(string text) => new()
    {
        Text = text,
        Foreground = new SolidColorBrush(Color.FromRgb(0x7A, 0x6A, 0x50)),
        FontSize = 12,
        Margin = new Thickness(2, 6, 0, 1),
    };

    /// <summary>글자 한 줄을 눌리는 칸으로 늘어놓는다.</summary>
    private WrapPanel Keys(IEnumerable<string> glyphs)
    {
        var row = new WrapPanel { MaxWidth = 420 };
        foreach (string glyph in glyphs)
        {
            string key = glyph;
            var cell = new Border
            {
                Background = Brushes.Transparent,
                Padding = new Thickness(key.Length > 1 ? 4 : 6, 2, key.Length > 1 ? 4 : 6, 2),
                Cursor = Cursors.Hand,
                Child = Ink(key, center: true),
            };
            cell.MouseEnter += (_, _) => cell.Background = GameUi.ItemFill;
            cell.MouseLeave += (_, _) => cell.Background = Brushes.Transparent;
            cell.MouseLeftButtonUp += (_, e) => { e.Handled = true; Tap(key); };
            row.Children.Add(cell);
        }
        return row;
    }

    /// <summary>글자 한 칸을 찍었다.</summary>
    private void Tap(string key)
    {
        int lead = Leads.IndexOf(key, StringComparison.Ordinal);
        int vowel = Vowels.IndexOf(key, StringComparison.Ordinal);
        int tail = key == "없음" ? 0 : Tails.IndexOf(key, StringComparison.Ordinal);

        // 한글이 아니면 모으던 자를 매듭짓고 그대로 붙인다.
        if (lead < 0 && vowel < 0 && tail <= 0)
        {
            Settle();
            Add(key);
            Sync();
            return;
        }

        if (lead >= 0 && _lead < 0) { _lead = lead; }
        else if (vowel >= 0 && _lead >= 0 && _vowel < 0) { _vowel = vowel; }
        else if (tail > 0 && _lead >= 0 && _vowel >= 0 && _tail == 0) { _tail = tail; Settle(); }
        else
        {
            // 차례가 어긋나면 모으던 자를 매듭짓고 새로 시작한다.
            Settle();
            if (lead >= 0) _lead = lead;
            else if (vowel >= 0) { _lead = Leads.IndexOf('ㅇ'); _vowel = vowel; }
        }
        Sync();
    }

    /// <summary>모으던 한글 한 자를 글에 붙인다.</summary>
    private void Settle()
    {
        if (_lead >= 0 && _vowel >= 0)
            Add(((char)(0xAC00 + ((_lead * 21) + _vowel) * 28 + _tail)).ToString());
        else if (_lead >= 0)
            Add(Leads[_lead].ToString());

        _lead = _vowel = -1;
        _tail = 0;
    }

    private void Add(string text)
    {
        if (_text.Length + text.Length <= _maxLength) _text.Append(text);
    }

    /// <summary>한 글자 지운다. 모으던 자가 있으면 그것부터 물린다.</summary>
    private void Backspace()
    {
        if (_tail > 0) _tail = 0;
        else if (_vowel >= 0) _vowel = -1;
        else if (_lead >= 0) _lead = -1;
        else if (_text.Length > 0) _text.Length--;
        Sync();
    }

    /// <summary>입력 줄을 다시 찍는다. 모으는 중인 자도 미리 보여 준다.</summary>
    private void Sync()
    {
        string pending = _lead >= 0 && _vowel >= 0
            ? ((char)(0xAC00 + ((_lead * 21) + _vowel) * 28 + _tail)).ToString()
            : _lead >= 0 ? Leads[_lead].ToString() : "";
        _line.Text = _text + pending;
    }

    private void Decide()
    {
        Settle();
        _result = _text.ToString().Trim();
        Close();
    }

    private void Cancel()
    {
        _result = null;
        Close();
    }

    /// <summary>
    /// 입력 칸과 자판 글자. <b>게임 글꼴</b>로 찍는다 — 바탕이 밝아 검은 글씨다.
    /// </summary>
    /// <remarks>
    /// 게임 글꼴을 못 읽었을 때만 윈도 글꼴로 물러선다(<see cref="GameUi.GameLabel"/>).
    /// </remarks>
    private static GameUi.GameLabel Ink(string text, bool center = false) =>
        new(GameFont.BlackColor)
        {
            Text = text,
            Bold = false,
            FallbackBrush = System.Windows.Media.Brushes.Black,
            HorizontalAlignment = center ? HorizontalAlignment.Center : HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center,
        };

    /// <summary>
    /// 판을 띄우고 지은 글을 낸다. 중단했으면 null.
    /// </summary>
    /// <param name="owner">주인 창.</param>
    /// <param name="start">처음 들어 있을 글.</param>
    /// <param name="maxLength">가장 긴 길이.</param>
    /// <param name="caption">창 제목.</param>
    public static string? Ask(Window owner, string start, int maxLength,
                              string caption = "문자입력")
    {
        var dialog = new TextInputDialog(start, maxLength, caption) { Owner = owner };
        dialog.ShowDialog();
        return string.IsNullOrWhiteSpace(dialog._result) ? null : dialog._result;
    }
}
