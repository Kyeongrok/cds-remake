using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using CdsHelper.Game.Local.Helpers;

namespace CdsHelper.Game.UI.Views;

/// <summary>
/// 내 리포트 — 버그 리포트 창의 「내 리포트」 단추가 띄운다. 이 설치가 보낸 리포트마다 보낸 때 · 글 앞부분 · 상태(접수 · 확인함 · 고침 · 고치지 않음) ·
/// 받은 답을 보인다(<see cref="BugReport.LoadMine"/>). 원본에 없는 것이다.
/// </summary>
/// <remarks>
/// 설치 번호(<c>%APPDATA%\CdsHelper</c> 의 무작위 번호)로 고르므로 업데이트해도 그대로 보이고, PC 를 옮기면 예전 것은 안 보인다.
/// 칸 높이는 못 박지 않는다 — <see cref="BugReportDialog"/> 와 같은 까닭(화면 배율이 크면 잘린다).
/// </remarks>
public sealed class MyBugReportsDialog : GameWindow
{
    private const double WantWidth = 640, WantHeight = 600;
    private const double AreaGap = 32;
    private const double BodySize = 14;

    /// <summary>글 칸 안의 어두운 글자 — 바탕이 밝은 종이(<see cref="GameUi.PageFill"/>)라서.</summary>
    private static readonly Brush Ink = new SolidColorBrush(Color.FromRgb(0x2A, 0x1C, 0x14));
    private static readonly Brush Dim = new SolidColorBrush(Color.FromRgb(0x6A, 0x5A, 0x48));
    private static readonly Brush ReplyEdge = new SolidColorBrush(Color.FromRgb(0x2A, 0x5F, 0xD0));

    private readonly StackPanel _list;
    private readonly TextBlock _status;

    private MyBugReportsDialog()
    {
        var room = SystemParameters.WorkArea;
        Title = Lang.T("내 리포트");
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        Width = Math.Min(WantWidth, room.Width - AreaGap);
        Height = Math.Min(WantHeight, room.Height - AreaGap);
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        Background = GameUi.Back;

        _list = new StackPanel { Margin = new Thickness(8) };
        var scroll = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content = _list,
        };
        var paper = new Border { Background = GameUi.PageFill, BorderBrush = GameUi.ItemEdge, BorderThickness = new Thickness(2), Child = scroll };

        var head = Label(Lang.T("이 PC 에서 보내신 버그 리포트입니다. 고치거나 답을 달면 여기에 보입니다."));
        head.Margin = new Thickness(0, 0, 0, 6);
        DockPanel.SetDock(head, Dock.Top);

        _status = Label(Lang.T("불러오는 중입니다…"));
        _status.VerticalAlignment = VerticalAlignment.Center;
        var close = new GameButton("닫기", Close, width: 110);
        DockPanel.SetDock(close, Dock.Right);
        var bottom = new DockPanel { Margin = new Thickness(0, 10, 0, 0), Children = { close, _status } };
        DockPanel.SetDock(bottom, Dock.Bottom);

        var body = new DockPanel { Margin = new Thickness(14, 8, 14, 12), Children = { head, bottom, paper } };
        var frame = new DockPanel();
        var titleBar = GameUi.TitleBar("내 리포트", Close);
        DockPanel.SetDock(titleBar, Dock.Top);
        frame.Children.Add(titleBar);
        frame.Children.Add(body);
        Content = new Border { Background = GameUi.Back, BorderBrush = GameUi.Edge, BorderThickness = new Thickness(2), Child = frame };

        PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) { e.Handled = true; Close(); } };
        Loaded += async (_, _) => await Fill();
    }

    private static TextBlock Label(string text) => new() { Text = text, Foreground = GameUi.Text, FontSize = BodySize, TextWrapping = TextWrapping.Wrap };

    private static TextBlock Line(string text, Brush ink, double size = BodySize) =>
        new() { Text = text, Foreground = ink, FontSize = size, TextWrapping = TextWrapping.Wrap };

    /// <summary>받는 곳의 상태 이름을 보낸 분이 읽는 말로.</summary>
    private static string StatusName(string status) => status switch
    {
        "new" => Lang.T("접수"),
        "seen" => Lang.T("확인함"),
        "fixed" => Lang.T("고침"),
        "wontfix" => Lang.T("고치지 않음"),
        _ => status,
    };

    private async Task Fill()
    {
        var mine = await BugReport.LoadMine();
        int queued = BugReport.QueuedCount();
        string waiting = queued > 0 ? " · " + string.Format(Lang.T("아직 못 보낸 것 {0}건"), queued) : "";
        if (mine == null)
        {
            _status.Text = Lang.T("불러오지 못했습니다. 인터넷 연결을 확인해 주세요.") + waiting;
            return;
        }
        _status.Text = string.Format(Lang.T("{0}건"), mine.Count) + waiting;
        if (mine.Count == 0)
        {
            _list.Children.Add(Line(Lang.T("아직 보내신 리포트가 없습니다."), Dim));
            return;
        }
        foreach (var b in mine) _list.Children.Add(Card(b));
    }

    private static UIElement Card(BugReport.Mine b)
    {
        string when = DateTime.TryParse(b.CreatedAt, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var at)
            ? at.ToLocalTime().ToString("yyyy-MM-dd HH:mm") : b.CreatedAt;
        var card = new StackPanel { Margin = new Thickness(0, 0, 0, 12) };
        var top = new TextBlock { FontSize = BodySize, Foreground = Dim, TextWrapping = TextWrapping.Wrap };
        top.Inlines.Add(new System.Windows.Documents.Run($"#{b.Id}  ") { Foreground = Ink, FontWeight = FontWeights.Bold });
        top.Inlines.Add(new System.Windows.Documents.Run(StatusName(b.Status)) { Foreground = b.Status == "fixed" ? ReplyEdge : Ink, FontWeight = FontWeights.Bold });
        top.Inlines.Add(new System.Windows.Documents.Run($"  ·  {when}" + (b.Version.Length > 0 ? $"  ·  v{b.Version}" : "")));
        card.Children.Add(top);
        card.Children.Add(Line(b.Text.Length >= 300 ? b.Text + "…" : b.Text, Ink, BodySize - 1));
        if (b.Reply is { } reply)
            card.Children.Add(new Border
            {
                BorderBrush = ReplyEdge,
                BorderThickness = new Thickness(3, 0, 0, 0),
                Padding = new Thickness(8, 2, 0, 2),
                Margin = new Thickness(0, 4, 0, 0),
                Child = Line(Lang.T("답") + ": " + reply, Ink, BodySize - 1),
            });
        return new Border { BorderBrush = GameUi.ItemEdge, BorderThickness = new Thickness(0, 0, 0, 1), Child = card };
    }

    /// <summary>창을 띄운다.</summary>
    public static void Open(Window owner) => new MyBugReportsDialog { Owner = owner }.ShowDialog();
}
