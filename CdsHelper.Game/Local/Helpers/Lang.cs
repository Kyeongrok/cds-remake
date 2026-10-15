using CdsHelper.Game.Local.Helpers.Languages;
using CdsHelper.Game.Local.Settings;

namespace CdsHelper.Game.Local.Helpers;

/// <summary>
/// 화면 글의 언어 — 지금 켠 언어 전략(<see cref="ILanguage"/>)을 들고, 글을 내는 출구가 이것에 맡긴다. 원본에 없는 것이다.
/// </summary>
/// <remarks>
/// 출구는 몇 곳뿐이다(<see cref="ILanguage"/> 참고) — 대화 창(ConfirmDialog), 게임 글꼴(GameFont), 대본 대사
/// (DisevRunner.Speak), 이름 입력판, 윈도 글자. 코드 속 글은 한국어로 적혀 있고 언어는 출구에서만 갈린다.
/// 언어를 바꾸면 다시 켜야 다 바뀐다 — 이미 지어 둔 창과 읽어 둔 표는 그대로다.
/// </remarks>
public static class Lang
{
    /// <summary>고를 수 있는 언어 — 설정 창이 이 차례로 돈다.</summary>
    public static readonly ILanguage[] All = [new KoreanLanguage(), new JapaneseLanguage()];

    /// <summary>
    /// 이번에 켠 언어 — 앱을 켤 때 설정에서 한 번 읽고 그대로 둔다. 설정을 바꿔도 다시 켜기 전에는 안 바뀐다
    /// (창마다 반쯤만 바뀌어 섞이지 않게).
    /// </summary>
    public static ILanguage Current { get; } = All.FirstOrDefault(l => l.Code == GameSettings.Language) ?? All[0];

    /// <summary>그 글의 지금 언어 판(<see cref="ILanguage.Text"/>).</summary>
    public static string T(string korean) => string.IsNullOrEmpty(korean) ? korean : Current.Text(korean);

    private static bool _hooked;

    /// <summary>
    /// 보통 윈도 글자로 그리는 글도 바꾼다 — 게임 글꼴(<see cref="GameFont"/>)을 안 지나는 창(모드 · 설정 설명,
    /// 표 머리, 지도 위 쪽지, 불러오는 창 따위)이다. 그럴 언어가 아니면 아무것도 안 건다.
    /// </summary>
    /// <remarks>
    /// 칸이 <b>처음 자리를 잡을 때</b>(SizeChanged) 바꾸고, 그 뒤로는 글이 바뀔 때마다 또 바꾼다 — 쪽지 · 띠처럼
    /// 붙인 뒤에 글을 갈아 끼우는 칸이 많다.
    /// Loaded 를 쓰면 안 된다 — WPF 는 Loaded 를 <b>제 손(인스턴스 처리기)이 걸린 칸에만</b> 돌려서, 클래스 훅으로는
    /// 창 말고는 하나도 안 온다(예전 훅이 그래서 글 칸을 하나도 못 바꿨다). SizeChanged 는 칸마다 레이아웃이 직접 올린다.
    /// 바뀐 글은 렌더 전에 다시 재므로 한국어가 한 장면도 안 비친다.
    /// 앱을 켜자마자 불러야 한다. 스레드마다 따로 뜨는 창도 잡힌다.
    /// <code>
    ///   TextBlock.Text           묶인 글(Binding)은 건너뛴다 — 묶음이 끊긴다. 언어 이름처럼 일부러 안 바꿀 글은 묶어 넣는다.
    ///   ContentControl.Content   글이면(체크 상자 · 단추 이름 따위)
    ///   ToolTip                  글이면
    ///   Window.Title             창이 뜰 때
    /// </code>
    /// </remarks>
    public static void InstallWpfHook()
    {
        if (_hooked || !Current.TranslatesWpfText) return;
        _hooked = true;

        var text = System.ComponentModel.DependencyPropertyDescriptor.FromProperty(
            System.Windows.Controls.TextBlock.TextProperty, typeof(System.Windows.Controls.TextBlock));
        var content = System.ComponentModel.DependencyPropertyDescriptor.FromProperty(
            System.Windows.Controls.ContentControl.ContentProperty, typeof(System.Windows.Controls.ContentControl));

        System.Windows.EventManager.RegisterClassHandler(typeof(System.Windows.FrameworkElement),
            System.Windows.FrameworkElement.SizeChangedEvent, new System.Windows.SizeChangedEventHandler((sender, _) =>
            {
                switch (sender)
                {
                    case System.Windows.Controls.TextBlock block:
                        Swap(block);
                        Follow(block, text, Swap);
                        break;
                    case System.Windows.Window:
                        break;
                    case System.Windows.Controls.ContentControl control:
                        SwapContent(control);
                        Follow(control, content, SwapContent);
                        break;
                }
                if (sender is System.Windows.FrameworkElement { ToolTip: string tip } element)
                {
                    string done = T(tip);
                    if (done != tip) element.ToolTip = done;
                }
            }), handledEventsToo: true);
        System.Windows.EventManager.RegisterClassHandler(typeof(System.Windows.Window),
            System.Windows.FrameworkElement.LoadedEvent, new System.Windows.RoutedEventHandler((sender, _) =>
            {
                if (sender is System.Windows.Window { Title.Length: > 0 } window) window.Title = T(window.Title);
            }));
    }

    /// <summary>그 글 칸의 글을 바꾼다. 묶인 글 · 여러 조각 글은 건너뛴다.</summary>
    private static void Swap(System.Windows.Controls.TextBlock block)
    {
        if (string.IsNullOrEmpty(block.Text)) return;
        if (System.Windows.Data.BindingOperations.GetBindingExpression(block,
                System.Windows.Controls.TextBlock.TextProperty) != null) return;
        if (block.Inlines.Count > 1) return;
        string done = T(block.Text);
        if (done != block.Text) block.Text = done;
    }

    private static void SwapContent(System.Windows.Controls.ContentControl control)
    {
        if (control.Content is not string s || s.Length == 0) return;
        if (System.Windows.Data.BindingOperations.GetBindingExpression(control,
                System.Windows.Controls.ContentControl.ContentProperty) != null) return;
        string done = T(s);
        if (done != s) control.Content = done;
    }

    /// <summary>
    /// 붙어 있는 동안 그 값이 바뀌면 다시 바꾼다. 떨어지면 끊고 다시 붙으면 잇는다 — 지켜보는 손이 칸을 붙잡아
    /// 안 걷히는 일이 없게. 바꾼 글을 다시 넣으면 또 불리지만, 이미 번역된 글은 그대로 나오므로 거기서 멈춘다.
    /// </summary>
    private static void Follow<TElement>(TElement element, System.ComponentModel.DependencyPropertyDescriptor property, Action<TElement> swap)
        where TElement : System.Windows.FrameworkElement
    {
        if (Watched.TryGetValue(element, out _)) return;
        EventHandler changed = (_, _) => swap(element);
        Watched.Add(element, changed);
        property.AddValueChanged(element, changed);
        bool on = true;
        element.Unloaded += (_, _) =>
        {
            if (!on) return;
            on = false;
            property.RemoveValueChanged(element, changed);
        };
        element.Loaded += (_, _) =>
        {
            if (on) return;
            on = true;
            swap(element);
            property.AddValueChanged(element, changed);
        };
    }

    /// <summary>지켜보는 칸들 — 두 번 걸지 않게.</summary>
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<System.Windows.FrameworkElement, EventHandler>
        Watched = new();
}
