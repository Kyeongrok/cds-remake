using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Threading;

namespace CdsHelper.Game.UI.Views;

/// <summary>
/// 게임 창의 뿌리 — <b>모달을 걷은</b> 대화 상자다.
/// </summary>
/// <remarks>
/// <b>왜 모달을 걷었나.</b> 원본은 창이 하나라 대화 상자가 떠 있어도 오른쪽 위 닫기
/// 단추로 게임을 끝낼 수 있다. 우리는 상자마다 창을 띄우고 <c>ShowDialog()</c> 로
/// 모달을 걸었는데, 그러면 WPF 가 <b>주인 창의 입력을 통째로</b> 막아 그 창에 우리가
/// 그린 닫기 단추(<see cref="ChromeTitleBar"/>)까지 같이 죽는다.
///
/// 그래서 <c>ShowDialog()</c> 를 <c>new</c> 로 가리고 <b>속 고리를 하나 돌린다</b> —
/// 창은 <see cref="Window.Show"/> 로 그냥 띄우고, 닫힐 때까지 이 자리에서 기다린다.
/// 부르는 쪽에서 보면 예전과 똑같이 <b>닫힐 때까지 안 돌아온다</b>. 대신 주인 창은
/// 살아 있으므로, 손이 뒤로 새지 않게 <b>알맹이만 손을 안 받게</b> 덮는다
/// (<see cref="Shield"/>) — 제목 줄은 그대로 살아 닫기 단추가 눌린다.
///
/// <b>지도 위에 그리지 않는 까닭.</b> 지도는 WPF 가 아니라 D3D 자식 창이라
/// (<see cref="ShipMapHost"/>) airspace 규칙상 WPF 그림이 늘 그 밑에 깔린다. 상자를
/// 지도 창 <i>안에</i> 그리면 지도에 가려 안 보인다. 그래서 창은 창대로 두고 모달만
/// 걷었다.
///
/// <b>DialogResult 도 가린다.</b> WPF 는 <c>ShowDialog()</c> 로 띄운 창에만
/// <c>DialogResult</c> 를 쓰게 하고 아니면 터진다. 뜻은 같으니 여기서 받아 두었다가
/// <see cref="ShowDialog"/> 가 돌려준다 — 부르는 쪽도 상자 쪽도 적던 대로 적으면 된다.
/// </remarks>
public class GameWindow : Window
{
    /// <summary>
    /// 게임 창 <b>어디서나</b> V 를 들어 저장한다.
    /// </summary>
    /// <remarks>
    /// 상자마다 제 창이라 글쇠가 지도까지 안 올라온다. 그래서 창 갈래 하나에 걸어 두고
    /// 함대 창(<see cref="ShipMapWindow.Current"/>)을 찾아 부른다.
    ///
    /// <b>글자 칸에서는 안 먹는다</b> — 이름을 적다가 v 를 치면 저장을 물어 오면 곤란하다.
    /// 고침 글쇠(Ctrl·Alt)를 짚은 것도 넘긴다.
    /// </remarks>
    static GameWindow()
    {
        EventManager.RegisterClassHandler(typeof(GameWindow), Keyboard.KeyDownEvent,
                                          new KeyEventHandler(OnAnyKey));
    }

    private static void OnAnyKey(object sender, KeyEventArgs e)
    {
        if (e.Handled || Keyboard.Modifiers != ModifierKeys.None) return;
        if (e.OriginalSource is TextBoxBase or PasswordBox) return;
        if (sender is not Window window) return;

        if (e.Key == KeyOf(Local.Settings.GameSettings.SaveKey, Key.V))
        {
            ShipMapWindow.Current?.SaveByKey(window);
            e.Handled = true;
            return;
        }

        if (e.Key == KeyOf(Local.Settings.GameSettings.MapKey, Key.D))
        {
            ShipMapWindow.Current?.MapByKey();
            e.Handled = true;
        }
    }

    /// <summary>적어 둔 글쇠 이름을 글쇠로. 비었거나 모르는 이름이면 기본값이다.</summary>
    private static Key KeyOf(string name, Key fallback) =>
        !string.IsNullOrWhiteSpace(name) && Enum.TryParse(name, ignoreCase: true, out Key key)
            ? key
            : fallback;

    private bool? _result;

    /// <summary>
    /// 닫힐 때까지 기다린다. <b>모달은 안 건다.</b>
    /// </summary>
    public new bool? ShowDialog()
    {
        _result = null;

        var frame = new DispatcherFrame();
        void Stop(object? sender, EventArgs e) => frame.Continue = false;

        var owner = Owner;
        var shield = Shield.Raise(owner);
        Closed += Stop;
        if (owner != null) owner.Closed += Stop;   // 주인이 먼저 닫히면 같이 나온다

        try
        {
            Show();
            Activate();
            Dispatcher.PushFrame(frame);
        }
        finally
        {
            Closed -= Stop;
            if (owner != null) owner.Closed -= Stop;
            shield?.Dispose();

            // 초점을 <b>주인 창에 돌려준다</b>. 모달이 아니므로 상자가 닫혀도 윈도가
            // 알아서 주인을 잡아 주지 않는다 — 그냥 두면 Z 차례상 다음 창, 곧 딴 앱
            // 창으로 초점이 넘어간다.
            if (owner is { IsVisible: true }) owner.Activate();
        }
        return _result;
    }

    /// <summary>
    /// 이 상자가 내놓을 답. 넣으면 창이 닫힌다 — WPF 것과 뜻이 같다.
    /// </summary>
    public new bool? DialogResult
    {
        get => _result;
        set { _result = value; Close(); }
    }

    /// <summary>
    /// 상자가 떠 있는 동안 주인 창의 <b>알맹이만</b> 손을 안 받게 덮는 것.
    /// </summary>
    /// <remarks>
    /// <c>IsEnabled</c> 로 끄면 단추가 회색으로 죽어 화면이 달라 보인다. 손만 안 받게
    /// (<c>IsHitTestVisible</c>) 두면 보이는 것은 그대로다. 글쇠는 따로 막을 것이 없다 —
    /// 상자가 딴 창이라 초점이 그리로 가 있다.
    ///
    /// 덮을 데는 창마다 다르다. 지도 창은 <b>제목 줄을 빼고</b> 게임 화면만 덮어야
    /// 닫기 단추가 산다(<see cref="Cover"/> 로 적어 둔다). 안 적어 둔 창은 알맹이를
    /// 통째로 덮는다 — 예전 모달과 같은 꼴이다.
    ///
    /// 상자 위에 상자가 또 뜰 때는 이미 덮여 있으므로 <b>아무것도 안 한다</b> — 안쪽
    /// 상자가 닫히면서 남의 덮개를 걷어 버리면 안 되기 때문이다.
    /// </remarks>
    private sealed class Shield(UIElement[] body) : IDisposable
    {
        public static IDisposable? Raise(Window? owner)
        {
            if (owner == null) return null;

            UIElement[] parts = Covers.TryGetValue(owner, out var told) ? told
                              : owner.Content is UIElement body ? [body] : [];

            // 이미 딴 상자가 덮어 둔 것은 건드리지 않는다 — 안쪽 상자가 닫히면서 남의
            // 덮개를 걷어 버리면 안 된다.
            var mine = parts.Where(part => part.IsHitTestVisible).ToArray();
            if (mine.Length == 0) return null;

            foreach (var part in mine) part.IsHitTestVisible = false;
            return new Shield(mine);
        }

        public void Dispose()
        {
            foreach (var part in body) part.IsHitTestVisible = true;
        }
    }

    /// <summary>창마다 「여기만 덮어라」 하고 적어 둔 데.</summary>
    private static readonly ConditionalWeakTable<Window, UIElement[]> Covers = [];

    /// <summary>
    /// 상자가 떴을 때 <b>이 창에서 덮을 데</b>를 적어 둔다.
    /// </summary>
    /// <remarks>
    /// 지도 창이 제목 줄 밑의 게임 화면을 적어 둔다 — 그래야 상자가 떠 있어도 오른쪽
    /// 위 닫기 단추가 눌린다.
    /// </remarks>
    public static void Cover(Window window, params UIElement[] body) =>
        Covers.AddOrUpdate(window, body);
}
