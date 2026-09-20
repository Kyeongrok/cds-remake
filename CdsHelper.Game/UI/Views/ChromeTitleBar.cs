using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shell;
// 파일에 담긴 그림(System.IO.Path)과 이름이 겹치기 쉬워 별명을 달아 쓴다.
using ShapePath = System.Windows.Shapes.Path;

namespace CdsHelper.Game.UI.Views;

/// <summary>
/// 크로뮴(크롬) 창처럼 제목 줄을 우리가 직접 그린다.
/// </summary>
/// <remarks>
/// 윈도가 주는 제목 줄은 이 창과 안 어울린다 — 밑에 바로 게임 액자 띠가 오는데 그 위에
/// 윈도 회색 줄이 얹히면 두 벌의 창테가 겹쳐 보인다. 크롬도 같은 이유로 제 제목 줄을
/// 그리므로, 그쪽 치수를 그대로 가져왔다.
/// <code>
///   줄 높이      32
///   단추         46 x 32 세 개 (최소화·최대화·닫기)
///   무늬         10 x 10, 선 굵기 1
///   위에 얹는 색 흰색 0x1A (닫기만 빨강 E81123)
/// </code>
/// 색만 게임 것으로 바꿨다. 크롬 회색을 그대로 쓰면 밤색 액자 띠 위에서 혼자 떠 보인다.
///
/// <see cref="WindowChrome"/> 로 붙이므로 창 자체는 보통 창 그대로다 — 끌기·두 번 눌러
/// 최대화·모서리 잡아 늘이기·화면 끝에 붙이기(스냅)를 전부 윈도가 하던 대로 한다.
/// <c>WindowStyle.None</c> 으로 창테를 아예 없애는 길도 있지만, 그러면 최대화했을 때
/// 작업 표시줄을 덮는 문제를 손으로 고쳐야 한다.
/// </remarks>
internal static class ChromeTitleBar
{
    /// <summary>제목 줄 높이. 크롬과 같다.</summary>
    public const double Height = 32;

    /// <summary>단추 하나가 차지하는 너비. 크롬과 같다.</summary>
    private const double ButtonWidth = 46;

    /// <summary>왼쪽 햄버거 단추 너비. 오른쪽 단추보다 좁게 둬 제목이 너무 밀리지 않게 한다.</summary>
    private const double MenuButtonWidth = 40;

    /// <summary>제목 줄 바탕. 게임 밤색(<c>3A241E</c>)보다 한 단 어둡게 둬 액자 띠가 도드라진다.</summary>
    private static readonly Brush Frame = Freeze(Color.FromRgb(0x2A, 0x1A, 0x15));

    /// <summary>물러난 창의 제목 줄. 크롬도 초점을 잃으면 이렇게 흐려진다.</summary>
    private static readonly Brush FrameIdle = Freeze(Color.FromRgb(0x22, 0x16, 0x12));

    private static readonly Brush Title = Freeze(Color.FromRgb(0xF2, 0xEA, 0xD6));
    private static readonly Brush TitleIdle = Freeze(Color.FromRgb(0x9A, 0x8A, 0x78));

    /// <summary>단추 위에 얹는 색. 크롬은 바탕색을 갈지 않고 흰색을 옅게 덮는다.</summary>
    private static readonly Brush Hover = Freeze(Color.FromArgb(0x1A, 0xFF, 0xFF, 0xFF));
    private static readonly Brush Press = Freeze(Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF));

    /// <summary>내림 차림표의 테. 제목 줄보다 한 단 밝게 둬야 바탕에서 떨어져 보인다.</summary>
    private static readonly Brush MenuEdge = Freeze(Color.FromRgb(0x54, 0x3A, 0x30));

    /// <summary>못 누르는 줄의 글자.</summary>
    private static readonly Brush MenuOff = Freeze(Color.FromRgb(0x8A, 0x7A, 0x6C));

    /// <summary>닫기 단추만 빨갛다. 크롬·윈도 둘 다 이 색이다.</summary>
    private static readonly Brush CloseHover = Freeze(Color.FromRgb(0xE8, 0x11, 0x23));
    private static readonly Brush ClosePress = Freeze(Color.FromRgb(0xF1, 0x70, 0x7A));

    private static SolidColorBrush Freeze(Color c)
    {
        var b = new SolidColorBrush(c);
        b.Freeze();
        return b;
    }

    /// <summary>
    /// 창에 제목 줄을 붙이고 그 줄을 돌려준다. 부르는 쪽은 이것을 창 맨 위에 놓으면 된다.
    /// </summary>
    /// <param name="menu">
    /// 왼쪽 위 햄버거 단추를 눌렀을 때 내려올 줄들. 하나도 안 주면 단추를 달지 않는다.
    /// <c>Run</c> 이 null 인 줄은 흐려 두고 안 먹는다.
    /// </param>
    /// <param name="shown">
    /// 줄 하나가 지금 보일지 묻는 손. null 이면 다 보인다. 차림표는 열 때마다 다시 짓기에
    /// 모드 창에서 켜고 끈 것이 바로 든다.
    /// </param>
    /// <param name="menuButton">
    /// 왼쪽 햄버거. 대화 상자가 떠 있는 동안 이것만 따로 덮으려고 내준다 — 상자 위에서
    /// 또 상자를 열 수 있으면 안 된다. 차림표가 없으면 null.
    /// </param>
    public static FrameworkElement Attach(Window win, out FrameworkElement? menuButton,
                                          Func<string, bool>? shown,
                                          params (string Text, Action? Run)[] menu)
    {
        menuButton = null;
        WindowChrome.SetWindowChrome(win, new WindowChrome
        {
            // 위 32 점이 제목 줄이 된다 — 끌기와 두 번 눌러 최대화를 윈도가 알아서 한다.
            CaptionHeight = Height,
            ResizeBorderThickness = new Thickness(6),
            // 유리 테를 끄지 않으면 우리가 그린 줄 위에 윈도 것이 한 겹 더 비친다.
            GlassFrameThickness = new Thickness(0),
            CornerRadius = default,
            UseAeroCaptionButtons = false,
        });

        var text = new TextBlock
        {
            Foreground = Title,
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(12, 0, 12, 0),
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        text.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding(nameof(Window.Title))
        {
            Source = win,
        });

        var maximize = Button(GlyphKind.Maximize, () => Toggle(win));

        // DockPanel 은 먼저 넣은 것이 더 바깥이다 — 닫기부터 넣어야 왼쪽에서
        // 최소화·최대화·닫기 차례로 놓인다.
        var bar = new DockPanel { LastChildFill = true };
        DockRight(bar, Button(GlyphKind.Close, () => SystemCommands.CloseWindow(win)));
        DockRight(bar, maximize);
        DockRight(bar, Button(GlyphKind.Minimize, () => SystemCommands.MinimizeWindow(win)));
        if (menu.Length > 0)
        {
            var hamburger = MenuButton(menu, shown);
            DockPanel.SetDock(hamburger, System.Windows.Controls.Dock.Left);
            bar.Children.Add(hamburger);
            menuButton = hamburger;
        }
        bar.Children.Add(text);

        var root = new Border { Background = Frame, Height = Height, Child = bar };

        // 최대화하면 무늬가 "되돌리기"(겹친 네모)로 바뀐다 — 크롬도 그렇다.
        void SyncState()
        {
            ((ShapePath)((Border)maximize).Child).Data =
                Geometry(win.WindowState == WindowState.Maximized ? GlyphKind.Restore : GlyphKind.Maximize);
        }
        win.StateChanged += (_, _) => SyncState();
        // 창을 띄우기 전에 최대화해 두면 StateChanged 가 안 온다 — 한 번 더 맞춰 둔다.
        win.Loaded += (_, _) => SyncState();
        SyncState();

        // 물러난 창은 흐려 둔다.
        void SyncActive()
        {
            bool on = win.IsActive;
            root.Background = on ? Frame : FrameIdle;
            text.Foreground = on ? Title : TitleIdle;
        }
        win.Activated += (_, _) => SyncActive();
        win.Deactivated += (_, _) => SyncActive();

        return root;
    }

    /// <summary>
    /// 왼쪽 위 햄버거. 누르면 차림표가 제목 줄 바로 밑에 내려온다.
    /// </summary>
    /// <remarks>
    /// 떠 있는 동안 다시 누르면 닫히게 해야 하는데, 차림표가 바깥을 누르면 스스로 닫히는
    /// 것(<see cref="Popup.StaysOpen"/> = false)과 겹친다 — 단추를 누르면 <b>내려가는 동안</b>
    /// 이미 닫히고, 손을 뗄 때 우리가 다시 여는 꼴이 된다. 그래서 닫힌 때를 적어 두고
    /// 방금 닫혔으면 열지 않는다.
    /// </remarks>
    private static FrameworkElement MenuButton((string Text, Action? Run)[] items,
                                               Func<string, bool>? shown)
    {
        DateTime closedAt = DateTime.MinValue;
        var button = (Border)Button(GlyphKind.Menu, MenuButtonWidth, "차림표", () => { });

        void Open()
        {
            if ((DateTime.UtcNow - closedAt).TotalMilliseconds < 250) return;

            var popup = new Popup
            {
                PlacementTarget = button,
                Placement = PlacementMode.Bottom,
                StaysOpen = false,      // 바깥을 누르면 닫힌다
                AllowsTransparency = true,
                Focusable = true,
            };
            var rows = new StackPanel();
            foreach (var (label, run) in items)
            {
                // 차림표를 열 때마다 다시 짓는다 — 모드로 켜고 끈 줄이 곧바로 든다.
                if (shown != null && !shown(label)) continue;
                rows.Children.Add(MenuRow(label, run, () => popup.IsOpen = false));
            }

            popup.Child = new Border
            {
                Background = Frame,
                BorderBrush = MenuEdge,
                BorderThickness = new Thickness(1),
                Padding = new Thickness(0, 4, 0, 4),
                MinWidth = 180,
                Child = rows,
            };
            popup.Closed += (_, _) => closedAt = DateTime.UtcNow;
            popup.IsOpen = true;
        }

        button.Tag = (Action)Open;
        return button;
    }

    /// <summary>차림표 한 줄. <paramref name="run"/> 이 null 이면 흐려 두고 안 먹는다.</summary>
    private static FrameworkElement MenuRow(string text, Action? run, Action close)
    {
        var row = new Border
        {
            Background = Brushes.Transparent,
            Padding = new Thickness(14, 7, 14, 7),
            Cursor = run != null ? Cursors.Hand : Cursors.Arrow,
            Child = new TextBlock
            {
                Text = text,
                FontSize = 13,
                Foreground = run != null ? Title : MenuOff,
            },
        };
        if (run == null) return row;

        row.MouseEnter += (_, _) => row.Background = Hover;
        row.MouseLeave += (_, _) => row.Background = Brushes.Transparent;
        row.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            close();
            run();
        };
        return row;
    }

    private static void DockRight(DockPanel bar, FrameworkElement button)
    {
        DockPanel.SetDock(button, System.Windows.Controls.Dock.Right);
        bar.Children.Add(button);
    }

    /// <summary>최대화와 되돌리기를 오간다. 두 번 눌렀을 때와 같은 일이다.</summary>
    private static void Toggle(Window win)
    {
        if (win.WindowState == WindowState.Maximized) SystemCommands.RestoreWindow(win);
        else SystemCommands.MaximizeWindow(win);
    }

    private enum GlyphKind { Menu, Minimize, Maximize, Restore, Close }

    /// <summary>
    /// 무늬는 글꼴(Segoe MDL2) 대신 선으로 그린다. 글꼴이 없는 자리에서 네모로 깨지는 일이
    /// 없고, 굵기도 화면 배율과 상관없이 한 점으로 떨어진다.
    /// </summary>
    private static Geometry Geometry(GlyphKind kind)
    {
        // 10 x 10 안에 그린다. 선이 점 가운데(0.5)에 놓여야 흐려지지 않는다.
        string data = kind switch
        {
            // 햄버거는 줄 셋. 크롬처럼 위아래로 4점씩 벌린다.
            GlyphKind.Menu => "M 0,1.5 H 12 M 0,5.5 H 12 M 0,9.5 H 12",
            GlyphKind.Minimize => "M 0,5.5 H 10",
            GlyphKind.Maximize => "M 0.5,0.5 H 9.5 V 9.5 H 0.5 Z",
            // 뒤 네모는 위/오른쪽만 보인다 — 앞 네모에 가려지는 두 변은 안 그린다.
            GlyphKind.Restore => "M 0.5,2.5 H 7.5 V 9.5 H 0.5 Z M 2.5,2.5 V 0.5 H 9.5 V 7.5 H 7.5",
            _ => "M 0.5,0.5 L 9.5,9.5 M 9.5,0.5 L 0.5,9.5",
        };
        var geometry = System.Windows.Media.Geometry.Parse(data);
        geometry.Freeze();
        return geometry;
    }

    private static FrameworkElement Button(GlyphKind kind, Action run) =>
        Button(kind, ButtonWidth, kind switch
        {
            GlyphKind.Minimize => "최소화",
            GlyphKind.Close => "닫기",
            _ => "최대화",
        }, run);

    private static FrameworkElement Button(GlyphKind kind, double width, string tip, Action run)
    {
        var glyph = new ShapePath
        {
            Data = Geometry(kind),
            Stroke = Title,
            StrokeThickness = 1,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            SnapsToDevicePixels = true,
        };

        var button = new Border
        {
            Width = width,
            Height = Height,
            Background = Brushes.Transparent,
            Child = glyph,
        };

        // 이 표시가 없으면 단추가 제목 줄에 묻혀 눌러도 창만 끌린다.
        WindowChrome.SetIsHitTestVisibleInChrome(button, true);

        bool over = false, down = false;
        var hover = kind == GlyphKind.Close ? CloseHover : Hover;
        var press = kind == GlyphKind.Close ? ClosePress : Press;
        void Sync() => button.Background = down && over ? press : over ? hover : Brushes.Transparent;

        button.MouseEnter += (_, _) => { over = true; Sync(); };
        button.MouseLeave += (_, _) => { over = false; Sync(); };
        button.MouseLeftButtonDown += (_, e) =>
        {
            down = true;
            button.CaptureMouse();
            Sync();
            e.Handled = true;
        };
        button.MouseLeftButtonUp += (_, e) =>
        {
            button.ReleaseMouseCapture();
            bool go = down && over;
            down = false;
            Sync();
            e.Handled = true;
            // 햄버거는 Tag 에 달아 둔 일을 부른다 — 차림표가 저를 여닫아야 해서
            // 단추를 지은 뒤에야 무엇을 할지 정해진다.
            if (go) ((button.Tag as Action) ?? run)();
        };

        button.ToolTip = tip;
        return button;
    }
}
