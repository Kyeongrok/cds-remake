using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;

namespace CdsHelper.Support.UI.Units;

/// <summary>
/// 숫자만 받는 입력 칸. 오른쪽에 오르내림 단추가 붙는다.
/// </summary>
/// <remarks>
/// mv-data-view 의 같은 이름 컨트롤을 옮겨 온 것이다. 생김새(색)만 이 앱 쪽으로 맞췄고
/// 쓰는 법은 그대로다 — <see cref="Value"/>, <see cref="Minimum"/>, <see cref="Maximum"/>,
/// <see cref="Step"/>, <see cref="DecimalPlaces"/>.
/// 단추·위아래 화살표·휠 어느 쪽으로 굴려도 <see cref="Minimum"/>~<see cref="Maximum"/> 밖으로는
/// 안 나간다. 글자로 아무거나 쳐 넣어도 칸을 떠나는 순간 마지막 성한 값으로 되돌린다.
/// </remarks>
[TemplatePart(Name = PartTextBox, Type = typeof(TextBox))]
[TemplatePart(Name = PartUpButton, Type = typeof(ButtonBase))]
[TemplatePart(Name = PartDownButton, Type = typeof(ButtonBase))]
public class NumericSpinner : Control
{
    private const string PartTextBox = "PART_TextBox";
    private const string PartUpButton = "PART_UpButton";
    private const string PartDownButton = "PART_DownButton";

    // ── 의존 속성 ───────────────────────────────────────────────────────────

    public static readonly DependencyProperty ValueProperty =
        DependencyProperty.Register(nameof(Value), typeof(double), typeof(NumericSpinner),
            new FrameworkPropertyMetadata(0.0,
                FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
                OnValueChanged, CoerceValue));

    public static readonly DependencyProperty MinimumProperty =
        DependencyProperty.Register(nameof(Minimum), typeof(double), typeof(NumericSpinner),
            new PropertyMetadata(double.NegativeInfinity, OnRangeChanged));

    public static readonly DependencyProperty MaximumProperty =
        DependencyProperty.Register(nameof(Maximum), typeof(double), typeof(NumericSpinner),
            new PropertyMetadata(double.PositiveInfinity, OnRangeChanged));

    public static readonly DependencyProperty StepProperty =
        DependencyProperty.Register(nameof(Step), typeof(double), typeof(NumericSpinner),
            new PropertyMetadata(1.0));

    public static readonly DependencyProperty DecimalPlacesProperty =
        DependencyProperty.Register(nameof(DecimalPlaces), typeof(int), typeof(NumericSpinner),
            new PropertyMetadata(1, OnDecimalPlacesChanged));

    // ── 라우티드 이벤트 ─────────────────────────────────────────────────────

    public static readonly RoutedEvent ValueChangedEvent =
        EventManager.RegisterRoutedEvent(nameof(ValueChanged), RoutingStrategy.Bubble,
            typeof(RoutedPropertyChangedEventHandler<double>), typeof(NumericSpinner));

    public event RoutedPropertyChangedEventHandler<double> ValueChanged
    {
        add => AddHandler(ValueChangedEvent, value);
        remove => RemoveHandler(ValueChangedEvent, value);
    }

    // ── 속성 ────────────────────────────────────────────────────────────────

    public double Value
    {
        get => (double)GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    public double Minimum
    {
        get => (double)GetValue(MinimumProperty);
        set => SetValue(MinimumProperty, value);
    }

    public double Maximum
    {
        get => (double)GetValue(MaximumProperty);
        set => SetValue(MaximumProperty, value);
    }

    /// <summary>단추 한 번·휠 한 칸에 움직일 폭.</summary>
    public double Step
    {
        get => (double)GetValue(StepProperty);
        set => SetValue(StepProperty, value);
    }

    /// <summary>소수점 아래 자릿수. 0 이면 정수만 다룬다.</summary>
    public int DecimalPlaces
    {
        get => (int)GetValue(DecimalPlacesProperty);
        set => SetValue(DecimalPlacesProperty, value);
    }

    // ── 속살 ────────────────────────────────────────────────────────────────

    private TextBox? _textBox;
    private bool _isSyncing;
    private bool _wheelHooked;

    static NumericSpinner()
    {
        DefaultStyleKeyProperty.OverrideMetadata(
            typeof(NumericSpinner),
            new FrameworkPropertyMetadata(typeof(NumericSpinner)));
    }

    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate();

        if (_textBox != null)
        {
            _textBox.LostFocus -= OnTextBoxLostFocus;
            _textBox.KeyDown -= OnTextBoxKeyDown;
            _textBox.PreviewMouseWheel -= OnMouseWheel;
        }

        _textBox = GetTemplateChild(PartTextBox) as TextBox;

        if (_textBox != null)
        {
            _textBox.Text = FormatValue(Value);
            _textBox.LostFocus += OnTextBoxLostFocus;
            _textBox.KeyDown += OnTextBoxKeyDown;
            _textBox.PreviewMouseWheel += OnMouseWheel;
        }

        // 템플릿이 새로 붙으면 단추도 새것이라 손잡이가 겹칠 일이 없다.
        if (GetTemplateChild(PartUpButton) is ButtonBase up)
            up.Click += (_, _) => Nudge(+Step);

        if (GetTemplateChild(PartDownButton) is ButtonBase down)
            down.Click += (_, _) => Nudge(-Step);

        // 이쪽은 컨트롤 제 몸에 거는 것이라 두 번 걸면 휠 한 칸에 두 칸씩 뛴다.
        if (_wheelHooked) return;

        PreviewMouseWheel += OnMouseWheel;
        _wheelHooked = true;
    }

    // ── 다루기 ──────────────────────────────────────────────────────────────

    private void OnTextBoxLostFocus(object sender, RoutedEventArgs e) => CommitText();

    private void OnTextBoxKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Enter:
                CommitText();
                e.Handled = true;
                break;
            case Key.Up:
                Nudge(+Step);
                e.Handled = true;
                break;
            case Key.Down:
                Nudge(-Step);
                e.Handled = true;
                break;
        }
    }

    private void OnMouseWheel(object sender, MouseWheelEventArgs e)
    {
        Nudge(e.Delta > 0 ? +Step : -Step);
        e.Handled = true;
    }

    /// <summary>쳐 넣은 글자를 값으로 삼는다. 숫자가 아니면 마지막 값으로 되돌린다.</summary>
    private void CommitText()
    {
        if (_textBox == null) return;

        if (double.TryParse(_textBox.Text, out double parsed))
            SetCurrentValue(ValueProperty, parsed);
        else
            SyncText();
    }

    private void Nudge(double delta) =>
        SetCurrentValue(ValueProperty, Math.Round(Value + delta, DecimalPlaces));

    private void SyncText()
    {
        if (_textBox == null || _isSyncing) return;

        _isSyncing = true;
        _textBox.Text = FormatValue(Value);
        _isSyncing = false;
    }

    private string FormatValue(double value) =>
        value.ToString(DecimalPlaces == 0 ? "F0" : $"F{DecimalPlaces}");

    // ── 의존 속성 뒤치다꺼리 ────────────────────────────────────────────────

    private static void OnValueChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var spinner = (NumericSpinner)d;
        spinner.SyncText();
        spinner.RaiseEvent(new RoutedPropertyChangedEventArgs<double>(
            (double)e.OldValue, (double)e.NewValue, ValueChangedEvent));
    }

    private static object CoerceValue(DependencyObject d, object baseValue)
    {
        var spinner = (NumericSpinner)d;
        double value = Math.Clamp((double)baseValue, spinner.Minimum, spinner.Maximum);
        return Math.Round(value, spinner.DecimalPlaces);
    }

    private static void OnRangeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        d.CoerceValue(ValueProperty);

    private static void OnDecimalPlacesChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var spinner = (NumericSpinner)d;
        spinner.CoerceValue(ValueProperty);
        spinner.SyncText();
    }
}
