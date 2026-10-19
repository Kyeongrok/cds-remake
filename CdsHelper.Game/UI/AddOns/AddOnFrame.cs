using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using MoonSharp.Interpreter;
using MoonSharp.Interpreter.Interop;

namespace CdsHelper.Game.UI.AddOns;

/// <summary>
/// 애드온 패널 — 와우의 <c>CreateFrame</c> 이다. 지도 위에 겹쳐 뜨는 상자 하나에 글 · 단추를 위에서 아래로 쌓는다.
/// </summary>
/// <remarks>
/// 지도가 D3D 자식 창이라 보통 WPF 요소로는 그 위에 못 얹는다(airspace) — 좌표 상자처럼 <see cref="Popup"/> 을 쓴다.
/// 자리는 지도 넓이 기준 아홉 곳(<c>TOPLEFT</c> · <c>CENTER</c> · <c>BOTTOMRIGHT</c> 따위)에서 x · y 만큼 민다.
/// <c>SetMovable(true)</c> 면 끌어 옮길 수 있고, 이름이 있는 패널은 옮긴 자리를 기억한다.
/// </remarks>
[MoonSharpUserData]
public sealed class AddOnFrame
{
    private readonly AddOn _owner;
    private readonly string? _name;
    private readonly Popup _popup;
    private readonly Border _box;
    private readonly StackPanel _stack = new();
    private readonly HashSet<string> _events = new(StringComparer.Ordinal);
    private readonly Dictionary<string, DynValue> _scripts = new(StringComparer.OrdinalIgnoreCase);
    private string _point = "TOPLEFT";
    private double _x = 10, _y = 10;
    private bool _shown = true, _movable, _closed;
    private Point? _grab;

    internal AddOnFrame(AddOn owner, string? name)
    {
        _owner = owner;
        _name = name;
        Self = UserData.Create(this);
        _box = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(0xB4, 0x10, 0x10, 0x10)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0xC8, 0x0B, 0x05, 0x05)),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(8, 4, 8, 4),
            Child = _stack,
        };
        _box.SizeChanged += (_, _) => Place();
        _box.MouseLeftButtonDown += (_, e) =>
        {
            if (!_movable) return;
            _grab = e.GetPosition(AddOnHost.Stage?.Anchor);
            _box.CaptureMouse();
            e.Handled = true;
        };
        _box.MouseMove += (_, e) =>
        {
            if (_grab is not { } from || AddOnHost.Stage?.Anchor is not { } anchor) return;
            var now = e.GetPosition(anchor);
            // 끄는 동안은 왼쪽 위 기준으로 바꿔 민다.
            var (left, top) = TopLeft();
            _point = "TOPLEFT";
            _x = left + now.X - from.X;
            _y = top + now.Y - from.Y;
            _grab = now;
            Place();
        };
        _box.MouseLeftButtonUp += (_, _) =>
        {
            if (_grab == null) return;
            _grab = null;
            _box.ReleaseMouseCapture();
            if (Key is { } key) Places[key] = [_x, _y];
        };

        _popup = new Popup
        {
            Placement = PlacementMode.Relative,
            PlacementTarget = AddOnHost.Stage?.Anchor,
            AllowsTransparency = true,
            StaysOpen = true,
            Child = _box,
        };
        if (Key is { } k && Places.TryGetValue(k, out var at) && at.Length == 2)
            (_point, _x, _y) = ("TOPLEFT", at[0], at[1]);
        Sync();
    }

    [MoonSharpHidden] public DynValue Self { get; }

    /// <summary>옮긴 자리를 기억하는 열쇠 — 이름 없는 패널은 기억하지 않는다.</summary>
    private string? Key => _name is { Length: > 0 } ? $"{_owner.Name}/{_name}" : null;

    // ── Lua 에 여는 것 ─────────────────────────────────────────────────────

    public string? GetName() => _name;

    public void SetPoint(string point, double x = 0, double y = 0)
    {
        _point = point.ToUpperInvariant();
        _x = x;
        _y = y;
        Place();
    }

    public void SetSize(double width, double height)
    {
        _box.Width = width > 0 ? width : double.NaN;
        _box.Height = height > 0 ? height : double.NaN;
    }

    /// <summary>바탕색 — 0~1. 알파를 0 으로 두면 테두리까지 비운다.</summary>
    public void SetBackdrop(double r, double g, double b, double a = 0.7)
    {
        _box.Background = new SolidColorBrush(AddOnText.ColorOf(r, g, b, a));
        _box.BorderThickness = a <= 0 ? default : new Thickness(1);
    }

    public AddOnText AddText(string text = "")
    {
        var t = new AddOnText(text);
        _stack.Children.Add(t.Block);
        return t;
    }

    public AddOnButton AddButton(string text, DynValue? onClick = null)
    {
        var b = new AddOnButton(_owner, text, onClick);
        _stack.Children.Add(b.Element);
        Sync();
        return b;
    }

    public void Show() { if (_shown) return; _shown = true; Sync(); Run("OnShow"); }

    public void Hide() { if (!_shown) return; _shown = false; Sync(); Run("OnHide"); }

    public bool IsShown() => _shown;

    public void SetMovable(bool movable)
    {
        _movable = movable;
        _box.Cursor = movable ? Cursors.SizeAll : null;
        Sync();
    }

    public void RegisterEvent(string name) => _events.Add(name);

    public void UnregisterEvent(string name) => _events.Remove(name);

    public void UnregisterAllEvents() => _events.Clear();

    /// <summary><c>OnEvent(self, 이벤트, ...)</c> · <c>OnUpdate(self, 지난 초)</c> · <c>OnShow(self)</c> · <c>OnHide(self)</c>.</summary>
    public void SetScript(string handler, DynValue fn)
    {
        if (fn.IsNil()) _scripts.Remove(handler);
        else _scripts[handler] = fn;
    }

    // ── 호스트가 부르는 것 ─────────────────────────────────────────────────

    [MoonSharpHidden]
    public void Dispatch(string name, object?[] args)
    {
        if (_closed || !_events.Contains(name) || !_scripts.TryGetValue("OnEvent", out var fn)) return;
        _owner.Call(fn, [Self, name, .. args]);
    }

    [MoonSharpHidden]
    public void Update(double elapsed)
    {
        if (_closed || !_shown || !_scripts.TryGetValue("OnUpdate", out var fn)) return;
        _owner.Call(fn, Self, elapsed);
    }

    private void Run(string handler)
    {
        if (!_closed && _scripts.TryGetValue(handler, out var fn)) _owner.Call(fn, Self);
    }

    /// <summary>띄울지 다시 따진다 — 지도가 보이고, 숨기지 않았고, 애드온이 멈추지 않았을 때만.</summary>
    [MoonSharpHidden]
    public void Sync()
    {
        bool open = !_closed && _shown && AddOnHost.Visible && _owner.Failed == null;
        // 단추도 없고 끌 수도 없는 패널은 쥐를 그냥 지나가게 한다 — 밑의 지도를 누를 수 있게.
        _popup.IsHitTestVisible = _movable || _stack.Children.OfType<Button>().Any();
        _box.IsHitTestVisible = _popup.IsHitTestVisible;
        if (_popup.IsOpen != open) _popup.IsOpen = open;
        if (open) Place();
    }

    [MoonSharpHidden]
    public void Close()
    {
        _closed = true;
        _popup.IsOpen = false;
    }

    private (double Left, double Top) TopLeft()
    {
        double w = AddOnHost.Stage?.Anchor.ActualWidth ?? 0, h = AddOnHost.Stage?.Anchor.ActualHeight ?? 0;
        double bw = _box.ActualWidth, bh = _box.ActualHeight;
        double left = _point switch
        {
            "TOP" or "CENTER" or "BOTTOM" => (w - bw) / 2 + _x,
            "TOPRIGHT" or "RIGHT" or "BOTTOMRIGHT" => w - bw - _x,
            _ => _x,
        };
        double top = _point switch
        {
            "LEFT" or "CENTER" or "RIGHT" => (h - bh) / 2 + _y,
            "BOTTOMLEFT" or "BOTTOM" or "BOTTOMRIGHT" => h - bh - _y,
            _ => _y,
        };
        return (left, top);
    }

    /// <summary>자리를 잡는다. 같은 값을 다시 넣으면 팝업이 안 움직이므로, 창이 옮겨졌을 때를 위해 한 번 흔든다.</summary>
    private void Place()
    {
        var (left, top) = TopLeft();
        _popup.HorizontalOffset = left + 0.01;
        _popup.HorizontalOffset = left;
        _popup.VerticalOffset = top;
    }

    // ── 옮긴 자리 ──────────────────────────────────────────────────────────

    private static Dictionary<string, double[]> Places = [];

    private static string PlacesPath => Path.Combine(AddOnHost.SavedDir, "frames.json");

    internal static void LoadPlaces()
    {
        try
        {
            Places = File.Exists(PlacesPath)
                ? JsonSerializer.Deserialize<Dictionary<string, double[]>>(File.ReadAllText(PlacesPath)) ?? []
                : [];
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException) { Places = []; }
    }

    internal static void SavePlaces()
    {
        if (Places.Count == 0) return;
        try
        {
            Directory.CreateDirectory(AddOnHost.SavedDir);
            File.WriteAllText(PlacesPath, JsonSerializer.Serialize(Places, AddOnHost.Json));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }
}

/// <summary>패널 속 글 한 줄 — <c>frame:AddText()</c>.</summary>
[MoonSharpUserData]
public sealed class AddOnText
{
    internal AddOnText(string text)
    {
        Block = new TextBlock { Text = text, Foreground = Brushes.White, FontSize = 12, IsHitTestVisible = false };
    }

    [MoonSharpHidden] public TextBlock Block { get; }

    public void SetText(string? text) => Block.Text = text ?? "";

    public string GetText() => Block.Text;

    public void SetTextColor(double r, double g, double b, double a = 1) =>
        Block.Foreground = new SolidColorBrush(ColorOf(r, g, b, a));

    public void SetFontSize(double size) => Block.FontSize = Math.Clamp(size, 6, 48);

    public void Show() => Block.Visibility = Visibility.Visible;

    public void Hide() => Block.Visibility = Visibility.Collapsed;

    [MoonSharpHidden]
    public static Color ColorOf(double r, double g, double b, double a) =>
        Color.FromArgb(Byte(a), Byte(r), Byte(g), Byte(b));

    private static byte Byte(double v) => (byte)Math.Round(Math.Clamp(v, 0, 1) * 255);
}

/// <summary>패널 속 단추 — <c>frame:AddButton(글, 함수)</c>. 누르면 <c>OnClick(단추)</c>.</summary>
[MoonSharpUserData]
public sealed class AddOnButton
{
    private readonly AddOn _owner;
    private DynValue? _click;

    internal AddOnButton(AddOn owner, string text, DynValue? onClick)
    {
        _owner = owner;
        _click = onClick;
        Element = new Button { Content = text, Margin = new Thickness(0, 2, 0, 2), Padding = new Thickness(6, 1, 6, 1), FontSize = 12 };
        var self = UserData.Create(this);
        Element.Click += (_, _) => { if (_click is { } fn) _owner.Call(fn, self); };
    }

    [MoonSharpHidden] public Button Element { get; }

    public void SetText(string? text) => Element.Content = text ?? "";

    public void SetScript(string handler, DynValue fn)
    {
        if (handler.Equals("OnClick", StringComparison.OrdinalIgnoreCase)) _click = fn.IsNil() ? null : fn;
    }

    public void SetEnabled(bool enabled) => Element.IsEnabled = enabled;

    public void Show() => Element.Visibility = Visibility.Visible;

    public void Hide() => Element.Visibility = Visibility.Collapsed;
}
