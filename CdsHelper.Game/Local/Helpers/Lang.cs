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
    /// 보통 윈도 글자(TextBlock)로 그리는 글도 바꾼다 — 게임 글꼴(<see cref="GameFont"/>)을 안 지나는 창(모드 · 설정 설명,
    /// 표 머리 따위)이다. 글 칸이 화면에 붙을 때 한 번 바꾼다. 그럴 언어가 아니면 아무것도 안 건다.
    /// </summary>
    public static void InstallWpfHook()
    {
        if (_hooked || !Current.TranslatesWpfText) return;
        _hooked = true;
        System.Windows.EventManager.RegisterClassHandler(typeof(System.Windows.Controls.TextBlock),
            System.Windows.FrameworkElement.LoadedEvent, new System.Windows.RoutedEventHandler((sender, _) =>
            {
                if (sender is not System.Windows.Controls.TextBlock block || string.IsNullOrEmpty(block.Text)) return;
                // 묶인 글은 손대지 않는다 — 묶음이 끊긴다.
                if (System.Windows.Data.BindingOperations.GetBindingExpression(block,
                        System.Windows.Controls.TextBlock.TextProperty) != null) return;
                if (block.Inlines.Count > 1) return;
                string done = T(block.Text);
                if (done != block.Text) block.Text = done;
            }));
        System.Windows.EventManager.RegisterClassHandler(typeof(System.Windows.Window),
            System.Windows.FrameworkElement.LoadedEvent, new System.Windows.RoutedEventHandler((sender, _) =>
            {
                if (sender is System.Windows.Window { Title.Length: > 0 } window) window.Title = T(window.Title);
            }));
    }
}
