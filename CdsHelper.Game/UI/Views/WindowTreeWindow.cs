using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace CdsHelper.Game.UI.Views;

/// <summary>
/// 개발용 — 지금 떠 있는 우리 창을 <b>주인(Owner) 나무</b>로 늘어놓고, 창마다 상태를 보여 준다.
/// </summary>
/// <remarks>
/// 「알림이 메뉴 뒤로 간다」 · 「메뉴가 안 숨는다」 같은 일은 대개 주인을 잘못 잡은 데서 난다. 눈으로 보려고 둔 창이다.
/// 게임에는 없다.
/// <code>
///   주인    WPF Owner. 주인 밑에 딸린 창은 늘 주인보다 앞에 서고, 주인이 닫히면 같이 닫힌다.
///           형제(주인이 같은 창)끼리는 앞뒤가 정해져 있지 않다 — 나중에 활성화된 쪽이 앞이다.
///   앞뒤    Win32 창 차례(Z-order). 1 이 맨 앞이다. 우리 창끼리만 센다.
///   보임    Visible · Hidden(감춤) · Collapsed.
///   활성    지금 키보드 초점을 쥔 창(IsActive).
///   막힘    모달 창(ShowDialog)이 떠 있어 입력이 막힌 창(IsEnabled 거짓).
///   모달    ShowDialog 로 뜬 창.
///   맨위    Topmost.
/// </code>
/// 주인이 바뀌거나 창이 뜨고 닫히면 아래 기록에 한 줄씩 남긴다.
/// <para>
/// 이 창은 주인 없이 맨 위(Topmost)에 서고, 뜰 때 초점을 가져가지 않는다 — 보는 창이 보이는 것을 흔들지 않게.
/// 목록에서도 뺀다.
/// </para>
/// </remarks>
public sealed class WindowTreeWindow : Window
{
    private static WindowTreeWindow? _open;

    private readonly TextBox _tree = Mono();
    private readonly TextBox _log = Mono();
    private readonly DispatcherTimer _clock;

    /// <summary>창마다 지난번에 본 주인 — 바뀌면 기록한다.</summary>
    private readonly Dictionary<Window, Window?> _owners = [];

    /// <summary>창마다 붙인 번호 — 이름이 같은 창을 가린다.</summary>
    private readonly Dictionary<Window, int> _ids = [];
    private int _nextId = 1;

    private readonly List<string> _lines = [];
    private const int MaxLog = 200;

    /// <summary>띄운다. 이미 떠 있으면 앞으로 낸다.</summary>
    public static void Open()
    {
        if (_open != null) { _open.Topmost = true; return; }
        _open = new WindowTreeWindow();
        _open.Show();
    }

    private WindowTreeWindow()
    {
        Title = "창 주인 보기 (개발)";
        Width = 720;
        Height = 620;
        Topmost = true;
        ShowActivated = false;
        ShowInTaskbar = true;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Left = SystemParameters.WorkArea.Right - Width - 8;
        Top = SystemParameters.WorkArea.Top + 8;

        var grid = new Grid();
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(3, GridUnitType.Star) });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(2, GridUnitType.Star) });
        Grid.SetRow(_tree, 0);
        var caption = new TextBlock { Text = "기록 — 뜸 · 닫힘 · 주인 바뀜", Margin = new Thickness(4, 6, 4, 2) };
        Grid.SetRow(caption, 1);
        Grid.SetRow(_log, 2);
        grid.Children.Add(_tree);
        grid.Children.Add(caption);
        grid.Children.Add(_log);
        Content = grid;

        _clock = new DispatcherTimer(TimeSpan.FromMilliseconds(400), DispatcherPriority.Background,
                                     (_, _) => Refresh(), Dispatcher);
        Loaded += (_, _) => { Refresh(); _clock.Start(); };
        Closed += (_, _) => { _clock.Stop(); _open = null; };
    }

    private static TextBox Mono() => new()
    {
        IsReadOnly = true,
        FontFamily = new FontFamily("Consolas, D2Coding, Malgun Gothic"),
        FontSize = 12,
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
        TextWrapping = TextWrapping.NoWrap,
    };

    private void Refresh()
    {
        var windows = Application.Current?.Windows.Cast<Window>().Where(w => w != this).ToList() ?? [];
        var order = ZOrder(windows);

        // 뜸 · 닫힘 · 주인 바뀜을 적는다.
        foreach (var w in windows)
        {
            if (!_owners.TryGetValue(w, out var was))
            {
                Note($"뜸      {Name(w)}  주인 {Name(w.Owner)}");
                _owners[w] = w.Owner;
            }
            else if (was != w.Owner)
            {
                Note($"주인 바뀜 {Name(w)}  {Name(was)} → {Name(w.Owner)}");
                _owners[w] = w.Owner;
            }
        }
        foreach (var gone in _owners.Keys.Where(w => !windows.Contains(w)).ToList())
        {
            Note($"닫힘    {Name(gone)}");
            _owners.Remove(gone);
            _ids.Remove(gone);   // 닫힌 창을 붙들고 있지 않는다
        }

        // 주인 나무 — 주인이 없는(또는 목록에 없는) 창이 뿌리다.
        var text = new StringBuilder();
        text.AppendLine("앞뒤  상태                         창 (주인 밑으로 들여 씀)");
        foreach (var root in windows.Where(w => w.Owner == null || !windows.Contains(w.Owner))
                                    .OrderBy(w => order.GetValueOrDefault(w, int.MaxValue)))
            Draw(text, root, 0, windows, order);
        string made = text.ToString();
        if (_tree.Text != made) _tree.Text = made;
    }

    private void Draw(StringBuilder text, Window w, int depth, List<Window> all,
                      Dictionary<Window, int> order)
    {
        string z = order.TryGetValue(w, out int at) ? at.ToString() : "-";
        text.Append(z.PadLeft(3)).Append("  ").Append(State(w).PadRight(28)).Append(' ')
            .Append(new string(' ', depth * 3)).Append(depth > 0 ? "└ " : "").AppendLine(Name(w));
        foreach (var child in all.Where(c => c.Owner == w)
                                 .OrderBy(c => order.GetValueOrDefault(c, int.MaxValue)))
            Draw(text, child, depth + 1, all, order);
    }

    private static string State(Window w)
    {
        var parts = new List<string>
        {
            w.Visibility switch { Visibility.Visible => "보임", Visibility.Hidden => "감춤", _ => "접힘" },
        };
        if (w.IsActive) parts.Add("활성");
        if (!w.IsEnabled || !NativeEnabled(w)) parts.Add("막힘");
        if (IsModal(w)) parts.Add("모달");
        if (w.Topmost) parts.Add("맨위");
        if (w.WindowState == WindowState.Minimized) parts.Add("최소화");
        if (!w.IsLoaded) parts.Add("안 뜸");
        return string.Join(" ", parts);
    }

    private string Name(Window? w)
    {
        if (w == null) return "(없음)";
        if (!_ids.TryGetValue(w, out int id)) _ids[w] = id = _nextId++;
        // 이름은 키로 표에서 찾는다(번역되지 않는다). 제목은 덧붙여 보일 뿐이다 — 번역되면 바뀐다.
        string name = WindowNames.NameOf(w) is { } known ? $" [{known}]" : "";
        string title = string.IsNullOrWhiteSpace(w.Title) ? "" : $" 「{w.Title}」";
        return $"{WindowNames.KeyOf(w)}#{id}{name}{title}";
    }

    private void Note(string line)
    {
        _lines.Add($"{DateTime.Now:HH:mm:ss.f}  {line}");
        if (_lines.Count > MaxLog) _lines.RemoveAt(0);
        _log.Text = string.Join(Environment.NewLine, _lines);
        _log.ScrollToEnd();
    }

    /// <summary>ShowDialog 로 떴는지 — WPF 가 드러내 주지 않아 안쪽 칸을 읽는다. 못 읽으면 거짓.</summary>
    private static bool IsModal(Window w) =>
        ShowingAsDialog?.GetValue(w) is true;

    private static readonly FieldInfo? ShowingAsDialog =
        typeof(Window).GetField("_showingAsDialog", BindingFlags.Instance | BindingFlags.NonPublic);

    /// <summary>모달이 다른 창을 막을 때는 Win32 로 막아 IsEnabled 에 안 나타난다 — 그쪽도 본다.</summary>
    private static bool NativeEnabled(Window w)
    {
        var hwnd = new WindowInteropHelper(w).Handle;
        return hwnd == IntPtr.Zero || IsWindowEnabled(hwnd);
    }

    /// <summary>우리 창의 앞뒤 차례 — 맨 앞이 1 이다.</summary>
    private static Dictionary<Window, int> ZOrder(List<Window> windows)
    {
        var byHandle = new Dictionary<IntPtr, Window>();
        foreach (var w in windows)
        {
            var hwnd = new WindowInteropHelper(w).Handle;
            if (hwnd != IntPtr.Zero) byHandle[hwnd] = w;
        }

        var order = new Dictionary<Window, int>();
        int rank = 0;
        for (var h = GetTopWindow(IntPtr.Zero); h != IntPtr.Zero && order.Count < byHandle.Count;
             h = GetWindow(h, GwHwndNext))
        {
            if (byHandle.TryGetValue(h, out var w)) order[w] = ++rank;
        }
        return order;
    }

    private const uint GwHwndNext = 2;

    [DllImport("user32.dll")] private static extern IntPtr GetTopWindow(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern IntPtr GetWindow(IntPtr hWnd, uint cmd);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowEnabled(IntPtr hWnd);
}
