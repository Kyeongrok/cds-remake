using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using CdsHelper.Game.Local.Helpers;

namespace CdsHelper.Game.UI.Views;

/// <summary>
/// 햄버거 「릴리즈 노트」 — 역대 판을 왼쪽 목록에서 골라 보고, 위 칸에 낱말을 넣어 찾는다(버그 리포트 #7).
/// </summary>
/// <remarks>
/// 예전에는 최근 두 판만 알림창(<see cref="ConfirmDialog"/>)으로 띄웠다. 노트가 백 판을 넘어 알림창으로는
/// 다 못 담는다 — 그래서 이 창은 <see cref="BugReportDialog"/> 처럼 WPF 글자로 짓고 오른쪽 글은 칸 안에서 스크롤된다.
/// <para>
/// <b>찾기.</b> 띄어 쓴 낱말이 모두 든 줄만 판별로 모아 「검색 결과」에 보이고, 목록에는 그 줄이 있는 판만
/// 남긴다(판 옆 수가 맞은 줄 수). 맞은 낱말은 밝게 칠한다. 대소문자는 안 가린다.
/// </para>
/// 업데이트 뒤 처음 켤 때 뜨는 노트는 그대로 알림창이다(<c>App.ShowReleaseNotes</c>).
/// </remarks>
public sealed class ReleaseNotesDialog : GameWindow
{
    private const double WantWidth = 900, WantHeight = 640;
    private const double AreaGap = 32;
    private const double BodySize = 14;

    private readonly List<(Version Version, string Body)> _sections;
    private readonly string _thanks;
    private readonly TextBox _search;
    private readonly ListBox _list;
    private readonly FlowDocumentScrollViewer _view;
    private readonly TextBlock _status;
    private string[] _terms = [];

    /// <summary>목록 한 줄이 가리키는 것 — 판 하나, 검색 결과 전체, 도움 주신 분들.</summary>
    private sealed record Entry(string Label, Version? Version, bool All = false, bool Thanks = false)
    {
        public override string ToString() => Label;
    }

    private ReleaseNotesDialog(string text)
    {
        _sections = ReleaseNotes.Sections(text);
        _thanks = ReleaseNotes.Thanks(text);

        var room = SystemParameters.WorkArea;
        Title = Lang.T("릴리즈 노트");
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        Width = Math.Min(WantWidth, room.Width - AreaGap);
        Height = Math.Min(WantHeight, room.Height - AreaGap);
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        Background = GameUi.Back;

        _search = new TextBox
        {
            FontSize = BodySize,
            Padding = new Thickness(4, 2, 4, 2),
            BorderThickness = new Thickness(2),
            BorderBrush = GameUi.ItemEdge,
            VerticalContentAlignment = VerticalAlignment.Center,
        };
        InputMethod.SetPreferredImeState(_search, InputMethodState.On);
        InputMethod.SetPreferredImeConversionMode(_search, ImeConversionModeValues.Native);
        var searchLabel = Label(Lang.T("검색"));
        searchLabel.VerticalAlignment = VerticalAlignment.Center;
        searchLabel.Margin = new Thickness(0, 0, 8, 0);
        _status = Label("");
        _status.VerticalAlignment = VerticalAlignment.Center;
        _status.Margin = new Thickness(10, 0, 0, 0);
        DockPanel.SetDock(searchLabel, Dock.Left);
        DockPanel.SetDock(_status, Dock.Right);
        var top = new DockPanel { Margin = new Thickness(0, 0, 0, 8), Children = { searchLabel, _status, _search } };

        _list = new ListBox
        {
            FontSize = BodySize,
            Width = 150,
            Background = GameUi.PageFill,
            BorderBrush = GameUi.ItemEdge,
            BorderThickness = new Thickness(2),
            Margin = new Thickness(0, 0, 8, 0),
        };
        ScrollViewer.SetHorizontalScrollBarVisibility(_list, ScrollBarVisibility.Disabled);

        _view = new FlowDocumentScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            IsToolBarVisible = false,
            Background = GameUi.PageFill,
            BorderBrush = GameUi.ItemEdge,
            BorderThickness = new Thickness(2),
        };

        var close = new GameButton("닫기", Close, width: 110);
        var bottom = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 10, 0, 0),
            Children = { close },
        };

        var middle = new DockPanel();
        DockPanel.SetDock(_list, Dock.Left);
        middle.Children.Add(_list);
        middle.Children.Add(_view);

        var body = new DockPanel { Margin = new Thickness(14, 8, 14, 12) };
        DockPanel.SetDock(top, Dock.Top);
        DockPanel.SetDock(bottom, Dock.Bottom);
        body.Children.Add(top);
        body.Children.Add(bottom);
        body.Children.Add(middle);

        var frame = new DockPanel();
        var titleBar = GameUi.TitleBar("릴리즈 노트", Close);
        DockPanel.SetDock(titleBar, Dock.Top);
        frame.Children.Add(titleBar);
        frame.Children.Add(body);
        Content = new Border
        {
            Background = GameUi.Back,
            BorderBrush = GameUi.Edge,
            BorderThickness = new Thickness(2),
            Child = frame,
        };

        _search.TextChanged += (_, _) => Refill();
        _list.SelectionChanged += (_, _) => ShowSelected();
        PreviewKeyDown += OnKey;
        Loaded += (_, _) => _search.Focus();
        Refill();
    }

    private static TextBlock Label(string text) => new()
    {
        Text = text,
        Foreground = GameUi.Text,
        FontSize = BodySize,
        TextWrapping = TextWrapping.NoWrap,
    };

    /// <summary>Esc 는 찾는 말이 있으면 지우고, 없으면 닫는다. 찾는 칸에서도 위·아래로 판을 넘긴다.</summary>
    private void OnKey(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            e.Handled = true;
            if (_search.Text.Length > 0) _search.Clear();
            else Close();
        }
        else if (_search.IsKeyboardFocused && (e.Key == Key.Down || e.Key == Key.Up) && _list.Items.Count > 0)
        {
            e.Handled = true;
            int next = _list.SelectedIndex + (e.Key == Key.Down ? 1 : -1);
            _list.SelectedIndex = Math.Clamp(next, 0, _list.Items.Count - 1);
            _list.ScrollIntoView(_list.SelectedItem);
        }
    }

    /// <summary>찾는 말에 맞춰 목록을 다시 채운다.</summary>
    private void Refill()
    {
        _terms = _search.Text.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        _list.Items.Clear();

        if (_terms.Length == 0)
        {
            foreach (var (version, _) in _sections) _list.Items.Add(new Entry($"v{version}", version));
            if (_thanks.Length > 0) _list.Items.Add(new Entry(Lang.T(ReleaseNotes.ThanksTitle), null, Thanks: true));
            _status.Text = string.Format(Lang.T("{0}개 판"), _sections.Count);
        }
        else
        {
            int lines = 0, versions = 0;
            var found = new List<Entry>();
            foreach (var (version, body) in _sections)
            {
                int hits = Matching(body).Count();
                if (hits == 0) continue;
                lines += hits;
                versions++;
                found.Add(new Entry($"v{version}  ({hits})", version));
            }
            if (versions > 0) _list.Items.Add(new Entry(Lang.T("검색 결과"), null, All: true));
            foreach (var entry in found) _list.Items.Add(entry);
            if (_thanks.Length > 0 && Matching(_thanks).Any())
                _list.Items.Add(new Entry(Lang.T(ReleaseNotes.ThanksTitle), null, Thanks: true));
            _status.Text = string.Format(Lang.T("{0}개 판 · {1}줄"), versions, lines);
        }

        if (_list.Items.Count > 0) _list.SelectedIndex = 0;
        else ShowSelected();
    }

    /// <summary>찾는 낱말이 모두 든 줄.</summary>
    private IEnumerable<string> Matching(string body) =>
        Lines(body).Where(line => _terms.All(t => line.Contains(t, StringComparison.CurrentCultureIgnoreCase)));

    private static IEnumerable<string> Lines(string body) =>
        body.Replace("\r\n", "\n").Split('\n').Where(l => l.Trim().Length > 0);

    private void ShowSelected()
    {
        var doc = new FlowDocument
        {
            FontSize = BodySize,
            FontFamily = FontFamily,
            PagePadding = new Thickness(12, 8, 12, 8),
            Foreground = Brushes.Black,
            Background = GameUi.PageFill,
        };

        switch (_list.SelectedItem)
        {
            case Entry { All: true }:
                foreach (var (version, body) in _sections)
                {
                    var hits = Matching(body).ToList();
                    if (hits.Count == 0) continue;
                    doc.Blocks.Add(Heading($"v{version}"));
                    foreach (string line in hits) doc.Blocks.Add(Line(line));
                }
                break;
            case Entry { Thanks: true }:
                doc.Blocks.Add(Heading(Lang.T(ReleaseNotes.ThanksTitle)));
                foreach (string line in Lines(_thanks)) doc.Blocks.Add(Line(line));
                break;
            case Entry { Version: { } version }:
                doc.Blocks.Add(Heading($"v{version}"));
                foreach (string line in Lines(_sections.First(s => s.Version == version).Body))
                    doc.Blocks.Add(Line(line));
                break;
            default:
                doc.Blocks.Add(new Paragraph(new Run(Lang.T(_sections.Count == 0
                    ? "릴리즈 노트를 찾지 못했습니다."
                    : "찾는 말이 든 대목이 없습니다."))));
                break;
        }
        _view.Document = doc;
    }

    private static Paragraph Heading(string text) => new(new Run(text))
    {
        FontWeight = FontWeights.Bold,
        FontSize = BodySize + 2,
        Margin = new Thickness(0, 10, 0, 4),
    };

    /// <summary>노트 한 줄 — 「- 」 머리는 점으로, 주소만 있는 줄은 누르면 연다, 찾는 낱말은 칠한다.</summary>
    private Paragraph Line(string raw)
    {
        string text = raw.TrimEnd();
        int depth = text.Length - text.TrimStart().Length;
        text = text.TrimStart();
        bool bullet = text.StartsWith("- ") || text.StartsWith("* ");
        if (bullet) text = text[2..];

        var p = new Paragraph
        {
            Margin = new Thickness(bullet ? 16 + depth * 8 : depth * 8, 2, 0, 2),
            TextIndent = bullet ? -12 : 0,
        };
        if (bullet) p.Inlines.Add(new Run("• "));

        string url = text.Trim();
        if (url.StartsWith("https://", StringComparison.OrdinalIgnoreCase) && !url.Contains(' ')
            && Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            var link = new Hyperlink(new Run(url)) { NavigateUri = uri };
            link.RequestNavigate += (_, e) =>
            {
                try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true }); }
                catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException) { }
            };
            p.Inlines.Add(link);
            return p;
        }

        foreach (var inline in Marked(text)) p.Inlines.Add(inline);
        return p;
    }

    private static readonly Brush Mark = MakeMark();

    private static Brush MakeMark()
    {
        var b = new SolidColorBrush(Color.FromRgb(0xFF, 0xD8, 0x4A));
        b.Freeze();
        return b;
    }

    /// <summary>글을 찾는 낱말 자리에서 갈라 맞은 토막만 칠한다.</summary>
    private IEnumerable<Inline> Marked(string text)
    {
        if (_terms.Length == 0) { yield return new Run(text); yield break; }

        var hit = new bool[text.Length];
        foreach (string term in _terms)
            for (int at = text.IndexOf(term, StringComparison.CurrentCultureIgnoreCase); at >= 0;
                 at = text.IndexOf(term, at + 1, StringComparison.CurrentCultureIgnoreCase))
                for (int i = at; i < at + term.Length && i < text.Length; i++) hit[i] = true;

        int start = 0;
        for (int i = 1; i <= text.Length; i++)
        {
            if (i < text.Length && hit[i] == hit[start]) continue;
            var run = new Run(text[start..i]);
            if (hit[start]) { run.Background = Mark; run.FontWeight = FontWeights.Bold; }
            yield return run;
            start = i;
        }
    }

    /// <summary>창을 띄운다. 노트 파일이 없으면 알림만 낸다.</summary>
    public static void Show(Window owner)
    {
        string text = ReleaseNotes.Read();
        if (ReleaseNotes.Sections(text).Count == 0)
        {
            NoticeDialog.Show(owner, "릴리즈 노트를 찾지 못했습니다.", "릴리즈 노트");
            return;
        }
        new ReleaseNotesDialog(text) { Owner = owner }.ShowDialog();
    }
}
