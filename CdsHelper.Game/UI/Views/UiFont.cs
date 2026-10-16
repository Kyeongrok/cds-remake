using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using CdsHelper.Game.Local.Settings;

namespace CdsHelper.Game.UI.Views;

/// <summary>
/// 모드 「리디바탕 글꼴」 — 윈도 글꼴로 찍는 창 글씨를 리디바탕으로 바꾼다.
/// </summary>
/// <remarks>
/// 글꼴은 이 어셈블리에 리소스로 들어 있다(<c>Fonts/RIDIBatang.otf</c>, SIL OFL 1.1 — 전문은 <c>RIDIBatang-OFL.txt</c>).
/// 창의 <see cref="Control.FontFamily"/> 는 자식에게 물려 내려가므로 <b>창에만</b> 걸면 그 안의 TextBlock 이 다 따른다 —
/// 제 글꼴을 따로 적어 둔 칸과 게임 비트맵 글씨(그림)는 그대로다.
/// 뜨는 창마다 걸려고 창 갈래 전체의 Loaded 에 손을 하나 달아 두고, 켜고 끌 때는 떠 있는 창을 다시 칠한다.
/// 끌 때는 <b>우리가 건 창만</b> 되돌린다 — 제 글꼴을 가진 창을 지우지 않게.
/// </remarks>
internal static class UiFont
{
    /// <summary>리디바탕. 글꼴 안 이름은 「RIDIBatang」이다.</summary>
    private static readonly FontFamily Ridi =
        new(new Uri("pack://application:,,,/CdsHelper.Game;component/"), "./Fonts/#RIDIBatang");

    /// <summary>
    /// 리디바탕을 쓸 수 있는 언어인지 — 리디바탕은 한글 글꼴이라 가나 · 일본 한자가 없다. 일본어로 켜면 모드를 켜 두었어도
    /// 안 걸고, 모드 창의 「폰트」 탭도 안 낸다.
    /// </summary>
    public static bool Supported => Local.Helpers.Lang.Current.Code == "ko";

    /// <summary>창 글꼴을 리디바탕으로 거는지 — 모드를 켰고 언어가 받쳐 줄 때만.</summary>
    private static bool Active => GameSettings.RidiFont && Supported;

    /// <summary>일본어 목록 창이 리디바탕 대신 쓰는 명조.</summary>
    private static readonly FontFamily JapaneseSerif = new("Yu Mincho, MS PMincho, Meiryo");

    /// <summary>
    /// 리디바탕 표로 그리는 목록 창(향상된 인물정보 · 게임 로드)의 글꼴 — 한국어면 리디바탕, 아니면 그 언어의 명조.
    /// </summary>
    public static FontFamily ListFont => Supported ? Ridi : JapaneseSerif;

    /// <summary>우리가 글꼴을 건 창 — 끌 때 이것들만 되돌린다.</summary>
    private static readonly ConditionalWeakTable<Window, object> Applied = new();

    private static bool _installed;

    /// <summary>뜨는 창마다 글꼴을 걸도록 손을 단다. 두 번 불러도 한 번만 단다.</summary>
    public static void Install()
    {
        if (_installed) return;
        _installed = true;
        EventManager.RegisterClassHandler(typeof(Window), FrameworkElement.LoadedEvent,
                                          new RoutedEventHandler((s, _) => { if (s is Window w) Apply(w); }));
        Refresh();
    }

    /// <summary>떠 있는 창을 설정대로 다시 칠한다 — 모드 창에서 켜고 끌 때 부른다.</summary>
    public static void Refresh()
    {
        if (Application.Current is not { } app) return;
        foreach (Window w in app.Windows) Apply(w);
    }

    private static void Apply(Window window)
    {
        if (Active)
        {
            if (Applied.TryGetValue(window, out _)) return;
            // 제 글꼴을 따로 적어 둔 창은 건드리지 않는다.
            if (window.ReadLocalValue(Control.FontFamilyProperty) != DependencyProperty.UnsetValue) return;
            window.FontFamily = Ridi;
            Applied.AddOrUpdate(window, Ridi);
        }
        else if (Applied.TryGetValue(window, out _))
        {
            window.ClearValue(Control.FontFamilyProperty);
            Applied.Remove(window);
        }
    }
}
