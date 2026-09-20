using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using CdsHelper.Game.Local.Helpers;

namespace CdsHelper.Game.UI.Views;

/// <summary>
/// 수를 하나 고르게 하는 작은 창 — 고를 줄 하나와 눈금 줄 몇을 얹는다.
/// </summary>
/// <remarks>
/// 게임의 <c>0x00454AA0</c> 을 옮겼다. 인자 차례가 그대로 화면 모양이다.
/// <code>
///   0x00454AA0(제목, 1, 라벨, 0, 단위, 0, 최대, 라벨2, 값2, 라벨3, 값3, 0)
///
///   ┌──── 선원고용 ────┐
///   │ 고용할 사람 수 [ 0][계산기]명 │
///   │ 현재의 선원 수          0명 │
///   │ 최저 선원 수           12명 │
///   │   [  결정  ]  [  중단  ]   │
///   └──────────────────────────┘
/// </code>
/// 선원 모집·해고가 이 창을 제목과 줄만 갈아 쓴다(<c>0x004773CF</c> · <c>0x004774A4</c>).
///
/// 화면에서 본 대로 맞춘 것 넷이다.
/// <list type="bullet">
///   <item><b>↑↓ 가 없다.</b> 값은 칸 옆 계산기로만 넣는다.</item>
///   <item><b>눈금 줄에도 단위가 붙는다</b> — "0명" · "12명" 이지 "0" · "12" 가 아니다.</item>
///   <item>제목 띠에 <b>닫기(X)가 없다</b>. 나가는 길은 "중단" 이다.</item>
///   <item>결정·중단이 <b>같은 폭</b>으로 나란히 선다.</item>
/// </list>
/// 키보드 ↑↓ 는 안 보이는 채로 남겨 둔다 — 화면 모양을 건드리지 않는 덤이다.
/// </remarks>
public sealed class CountDialog : GameWindow
{
    /// <summary>화면 바탕. 보급·계약 화면과 같은 밤색 판이다.</summary>
    private static readonly Brush Back = Frozen(Color.FromRgb(0x31, 0x18, 0x18));

    /// <summary>테를 두르는 짙은 선.</summary>
    private static readonly Brush Line = Frozen(Color.FromRgb(0x11, 0x09, 0x09));

    /// <summary>글꼴 조각을 못 읽었을 때 물러설 글씨색.</summary>
    private static readonly Brush Ink = Frozen(Color.FromRgb(0xCB, 0xC5, 0xC5));

    private static SolidColorBrush Frozen(Color c)
    {
        var b = new SolidColorBrush(c);
        b.Freeze();
        return b;
    }

    /// <summary>Shift 를 누르면 한 번에 이만큼 배로 뛴다.</summary>
    private const int Fast = 10;

    /// <summary>수를 적는 크림빛 칸의 폭과 키. 게임 화면에서 칸이 계산기 두 개 폭쯤이다.</summary>
    private const double FieldWidth = 44, FieldHeight = 18;

    /// <summary>줄 사이 틈과, 이름 끝에서 값 칸까지의 틈.</summary>
    private const double RowGap = 4, NameGap = 8;

    /// <summary>결정·중단 사이의 틈. 게임 화면에서 창 폭의 스무몇 분의 일이다.</summary>
    private const double ButtonGap = 10;

    /// <summary>눈금 줄 하나 — 이름과 값.</summary>
    /// <param name="Name">줄 이름("현재의 선원 수").</param>
    /// <param name="Value">그 값.</param>
    /// <param name="Unit">이 줄만의 단위. null 이면 고르는 줄 단위를 따른다(원본 마지막 인자, 「항해일수 보급」의 「명」).</param>
    public readonly record struct Gauge(string Name, int Value, string? Unit = null);

    private readonly int _max;
    private readonly int _step;
    private readonly GameUi.GameLabel _count;
    private readonly GameButton _decide;

    /// <summary>고른 수. 중단하면 0 이다.</summary>
    private int _picked;

    private int _at;

    /// <summary>0 도 고를 수 있는지 — 짐 창처럼 지금 값을 고쳐 적는 자리다(<see cref="Set"/>).</summary>
    private bool _zeroOk;

    private CountDialog(string caption, string label, string unit, int max, int step,
                        bool full, Gauge[] lines)
    {
        _max = max;
        _step = step;

        Title = caption;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.WidthAndHeight;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        Background = Back;

        // 고르는 수는 <b>크림빛 칸</b> 안에 짙은 글씨로 왼쪽에 붙는다 — 이름 일람·배 이름 칸과 같은 칸이다.
        _count = new GameUi.GameLabel(GameFont.ButtonColor)
        {
            Text = "",
            FallbackBrush = Brushes.Black,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(3, 0, 3, 0),
        };

        var field = new Border
        {
            Background = GameUi.PageFill,
            BorderBrush = GameUi.ItemEdge,
            BorderThickness = new Thickness(1),
            Width = FieldWidth,
            Height = FieldHeight,
            Child = _count,
        };

        var pick = new StackPanel { Orientation = Orientation.Horizontal };
        pick.Children.Add(field);
        // 값은 계산기로만 넣는다 — 게임도 칸 옆에 계산기 하나만 달아 두었다.
        pick.Children.Add(Pad());
        pick.Children.Add(Label(unit));

        // 이름 칸과 값 칸 두 줄기다. <b>이름은 오른쪽으로</b> 붙어 짧은 「최저 선원 수」가 한 자
        // 안쪽에서 시작하고, <b>값은 「명」 자리에 오른쪽 끝을 맞춘다</b> — 게임 화면이 그렇다.
        var rows = new Grid { Margin = new Thickness(12, 10, 16, 4) };
        rows.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        rows.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        AddRow(rows, label, pick);
        // 눈금 줄에도 단위가 붙는다 — 화면은 "65명" · "15명" 이다.
        foreach (var line in lines) AddRow(rows, line.Name, Label($"{line.Value}{line.Unit ?? unit}"));

        // 단추는 창 폭을 <b>나눠 채운다</b> — 결정·중단이 좁은 틈 하나를 두고 양옆으로 넓게 선다.
        // "최대" 는 계산기 판 안에도 MAX 로 있다. 돈처럼 자릿수가 큰 창에서만 밖에 낸다.
        var bands = new List<GameButton>();
        if (full) bands.Add(new GameButton("최대", () => { _at = _max; Paint(); }));
        _decide = new GameButton("결정", Decide) { On = false };
        bands.Add(_decide);
        bands.Add(new GameButton("중단", Close));

        var buttons = new Grid { Margin = new Thickness(12, 10, 12, 12) };
        for (int i = 0; i < bands.Count; i++)
        {
            buttons.ColumnDefinitions.Add(new ColumnDefinition());
            var band = bands[i];
            band.MinWidth = ButtonWidth;
            band.HorizontalAlignment = HorizontalAlignment.Stretch;
            band.Margin = new Thickness(i == 0 ? 0 : ButtonGap / 2, 0,
                                        i == bands.Count - 1 ? 0 : ButtonGap / 2, 0);
            Grid.SetColumn(band, i);
            buttons.Children.Add(band);
        }

        var page = new StackPanel();
        // 닫기(X)는 안 단다 — 화면의 이 창에는 없다. 나가는 길은 "중단" 이다.
        page.Children.Add(GameUi.TitleBar(caption, null));
        page.Children.Add(rows);
        page.Children.Add(buttons);

        var frame = GameUi.InfoFrame(page, Back, Line);
        GameUi.EnableDrag(this, frame);
        Content = frame;

        KeyDown += OnKey;
        MouseRightButtonUp += (_, _) => Close();
        Paint();
    }

    private void OnKey(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Escape: Close(); break;
            case Key.Up: Bump(+1); e.Handled = true; break;
            case Key.Down: Bump(-1); e.Handled = true; break;
            case Key.Enter or Key.Space when _at > 0 || _zeroOk: Decide(); e.Handled = true; break;
        }
    }

    private void Bump(int by)
    {
        int step = _step * (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? Fast : 1);
        _at = Math.Clamp(_at + by * step, 0, _max);
        Paint();
    }

    private void Paint()
    {
        _count.Text = $"{_at}";
        _decide.On = _at > 0 || _zeroOk;
    }

    private void Decide()
    {
        if (_at <= 0 && !_zeroOk) return;
        _picked = _at;
        Close();
    }

    /// <summary>밤색 판 위에 얹는 밝은 글씨.</summary>
    private static GameUi.GameLabel Label(string text) => new(GameFont.WhiteColor)
    {
        Text = text,
        FallbackBrush = Ink,
        HorizontalAlignment = HorizontalAlignment.Left,
        VerticalAlignment = VerticalAlignment.Center,
    };

    /// <summary>
    /// 줄 하나를 얹는다 — 이름은 첫 줄기 오른쪽 끝, 값은 둘째 줄기 오른쪽 끝에 붙는다.
    /// </summary>
    private static void AddRow(Grid grid, string name, FrameworkElement value)
    {
        int row = grid.RowDefinitions.Count;
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        double top = row == 0 ? 0 : RowGap;

        var label = Label(name);
        label.HorizontalAlignment = HorizontalAlignment.Right;
        label.Margin = new Thickness(0, top, 0, 0);
        Grid.SetRow(label, row);
        grid.Children.Add(label);

        value.HorizontalAlignment = HorizontalAlignment.Right;
        value.VerticalAlignment = VerticalAlignment.Center;
        value.Margin = new Thickness(NameGap, top, 0, 0);
        Grid.SetRow(value, row);
        Grid.SetColumn(value, 1);
        grid.Children.Add(value);
    }

    /// <summary>
    /// 칸 옆의 작은 계산기 단추. 누르면 숫자판이 떠서 값을 곧장 찍어 넣는다.
    /// </summary>
    /// <remarks>
    /// 판은 <see cref="NumberPadDialog"/> 다 — 신규 캐릭터 창의 연령·생일 칸이 여는 것과
    /// 같은 판이라 AC·DEL·MAX·MIN 이 그대로 있다. MAX 는 여기서 고를 수 있는 가장 큰 수,
    /// MIN 은 0 이다.
    /// </remarks>
    private UIElement Pad()
    {
        // 계산기는 칸과 키가 같다 — 화면에서 칸 바로 곁에 같은 높이로 붙어 있다.
        var box = GameUi.CalcButton(Type, FieldHeight);
        box.Margin = new Thickness(2, 0, 3, 0);
        box.VerticalAlignment = VerticalAlignment.Center;
        return box;

        void Type()
        {
            if (NumberPadDialog.Ask(this, _at, 0, _max) is not { } typed) return;
            _at = Math.Clamp(typed, 0, _max);
            Paint();
        }
    }

    /// <summary>결정·중단 한 단추의 가장 좁은 폭. 창이 넓으면 그만큼 늘어난다.</summary>
    private const double ButtonWidth = 92;

    /// <summary>
    /// 수를 고르게 한다. 고른 수를 내고, 중단하거나 0 이면 0 이다.
    /// </summary>
    /// <param name="caption">창 제목("선원고용").</param>
    /// <param name="label">고르는 줄 이름("고용할 사람 수").</param>
    /// <param name="unit">단위("명").</param>
    /// <param name="max">고를 수 있는 가장 큰 수.</param>
    /// <param name="step">↑↓ 한 번에 움직이는 수. Shift 를 누르면 그 열 배로 뛴다.</param>
    /// <param name="full">참이면 "최대" 단추를 단다 — 돈처럼 자릿수가 큰 것에 쓴다.</param>
    /// <param name="lines">밑에 붙는 눈금 줄들.</param>
    public static int Ask(Window owner, string caption, string label, string unit,
                          int max, int step = 1, bool full = false, params Gauge[] lines)
    {
        if (max <= 0) return 0;

        var dialog = new CountDialog(caption, label, unit, max, step, full, lines) { Owner = owner };
        dialog.ShowDialog();
        return dialog._picked;
    }

    /// <summary>
    /// 지금 값을 고쳐 적게 한다 — <paramref name="start"/> 에서 시작하고 0 도 된다. 중단하면 null.
    /// </summary>
    /// <remarks>짐 창에서 줄을 누를 때가 이것이다(<c>0x00454AA0</c>, 「탑재수」「통」「현재수」「한통의 무게」).</remarks>
    public static int? Set(Window owner, string caption, string label, string unit,
                           int start, int max, params Gauge[] lines)
    {
        var dialog = new CountDialog(caption, label, unit, Math.Max(0, max), 1, false, lines) { Owner = owner };
        dialog._zeroOk = true;
        dialog._at = Math.Clamp(start, 0, Math.Max(0, max));
        dialog._picked = -1;
        dialog.Paint();
        dialog.ShowDialog();
        return dialog._picked < 0 ? null : dialog._picked;
    }
}
