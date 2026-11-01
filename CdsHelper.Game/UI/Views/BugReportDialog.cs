using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using CdsHelper.Game.Local.Helpers;

namespace CdsHelper.Game.UI.Views;

/// <summary>버그 리포트 글 쓰는 창 — 여러 줄 글 칸 · 같이 보내는 것 요약 · 보내기 / 취소.</summary>
/// <remarks>
/// <b>글자 크기.</b> 창세기전3 리메이크의 같은 창은 고정 높이로 지어 화면 배율이 큰 모니터에서 글자 · 단추가 잘렸다.
/// 그래서 이 창에는 칸 높이를 못 박지 않는다 — 글이 정하는 줄(안내 · 글자 수 · 단추)은 Auto 로 글만큼 서고,
/// 두 글 칸이 남은 높이를 3:1 로 나눈다(넘치면 칸 안에서 스크롤된다). 글자 크기는 <see cref="BodySize"/> 하나로 맞춘다.
/// 창 크기는 작업 영역에서 자른다 — 1080p 200% 면 작업 영역이 540 높이뿐이라 원하는 높이를 그대로 쓰면 단추가 화면 밖으로 나간다.
/// WPF 는 모든 치수가 96 분의 1 인치이므로 배율은 알아서 먹는다.
/// </remarks>
public sealed class BugReportDialog : GameWindow
{
    private const double WantWidth = 640, WantHeight = 600;

    /// <summary>작업 영역에서 창을 얼마나 들이는지.</summary>
    private const double AreaGap = 32;

    /// <summary>이 창의 글자 크기.</summary>
    private const double BodySize = 14;

    private readonly TextBox _box;
    private readonly TextBlock _hint;
    private readonly GameButton _send, _cancel;
    private readonly Func<string, Task<BugReport.Result>> _submit;
    private bool _sending;
    private BugReport.Result? _result;

    private BugReportDialog(string summary, Func<string, Task<BugReport.Result>> submit, Rect? area = null)
    {
        _submit = submit;
        var room = area ?? SystemParameters.WorkArea;
        Title = "버그 리포트";
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        Width = Math.Min(WantWidth, room.Width - AreaGap);
        Height = Math.Min(WantHeight, room.Height - AreaGap);
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        Background = GameUi.Back;

        _box = new TextBox
        {
            AcceptsReturn = true,
            AcceptsTab = false,
            TextWrapping = TextWrapping.Wrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            MaxLength = BugReport.MaxText,
            FontSize = BodySize,
            Padding = new Thickness(4),
            BorderThickness = new Thickness(2),
            BorderBrush = GameUi.ItemEdge,
        };
        InputMethod.SetPreferredImeState(_box, InputMethodState.On);
        InputMethod.SetPreferredImeConversionMode(_box, ImeConversionModeValues.Native);

        var count = Label($"0 / {BugReport.MaxText}");
        count.HorizontalAlignment = HorizontalAlignment.Right;
        count.Margin = new Thickness(0, 4, 0, 6);

        var info = new TextBox
        {
            Text = Lang.T("같이 보내는 것:") + Environment.NewLine + summary,
            IsReadOnly = true,
            IsTabStop = false,
            TextWrapping = TextWrapping.Wrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            FontSize = BodySize - 1,
            Padding = new Thickness(4),
            Background = Brushes.Transparent,
            Foreground = GameUi.Text,
            BorderThickness = new Thickness(1),
            BorderBrush = GameUi.Edge,
        };

        _hint = Label(Lang.T("Ctrl+Enter 로도 보냅니다"));
        _hint.VerticalAlignment = VerticalAlignment.Center;
        _send = new GameButton("보내기", () => _ = Send(), width: 110);
        _cancel = new GameButton("취소", Close, width: 110);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Children = { _send, _cancel } };
        DockPanel.SetDock(buttons, Dock.Right);
        var bottom = new DockPanel { Margin = new Thickness(0, 10, 0, 0), Children = { buttons, _hint } };

        var body = new Grid { Margin = new Thickness(14, 8, 14, 12) };
        body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        body.RowDefinitions.Add(new RowDefinition { Height = new GridLength(3, GridUnitType.Star), MinHeight = 56 });
        body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        body.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star), MinHeight = 40 });
        body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var head = Label(Lang.T("어떤 일이 있었는지 적어 주세요. 무엇을 하다가, 무엇이 보였고, 원래는 어땠어야 하는지 적어 주시면 고치기 쉽습니다."));
        head.Margin = new Thickness(0, 0, 0, 6);
        UIElement[] rows = [head, _box, count, info, bottom];
        for (int i = 0; i < rows.Length; i++)
        {
            Grid.SetRow(rows[i], i);
            body.Children.Add(rows[i]);
        }

        var frame = new DockPanel();
        var titleBar = GameUi.TitleBar("버그 리포트", Close);
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

        _box.TextChanged += (_, _) =>
        {
            count.Text = $"{_box.Text.Length} / {BugReport.MaxText}";
            UpdateSend();
        };
        // 쓰던 글을 잃지 않게 Esc 는 글 칸이 비었을 때만 닫고, 오른쪽 단추로는 안 닫는다(붙여 넣기 차림표가 그 단추에 있다).
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.Control) { e.Handled = true; _ = Send(); }
            else if (e.Key == Key.Escape && _box.Text.Length == 0) { e.Handled = true; Close(); }
        };
        Closing += (_, e) => e.Cancel = _sending;   // 줄에는 이미 섰다 — 결과만 기다린다
        Loaded += (_, _) => _box.Focus();
        UpdateSend();
    }

    private static TextBlock Label(string text) => new()
    {
        Text = text,
        Foreground = GameUi.Text,
        FontSize = BodySize,
        TextWrapping = TextWrapping.Wrap,
    };

    private void UpdateSend() => _send.On = !_sending && _box.Text.Trim().Length > 0;

    private async Task Send()
    {
        if (_sending || _box.Text.Trim().Length == 0) return;
        _sending = true;
        UpdateSend();
        _cancel.On = false;
        _box.IsReadOnly = true;
        _hint.Text = Lang.T("보내는 중입니다…");
        var result = await _submit(_box.Text);
        _sending = false;
        _result = result;
        Close();
    }

    /// <summary>창을 띄워 글을 받아 보낸다 — 취소했으면 null.</summary>
    public static BugReport.Result? Ask(Window owner, string summary, Func<string, Task<BugReport.Result>> submit)
    {
        var dialog = new BugReportDialog(summary, submit) { Owner = owner };
        dialog.ShowDialog();
        return dialog._result;
    }
}
