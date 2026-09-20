using System.Windows;
using CdsHelper.Game.UI.Views;

namespace CdsHelper.Game.Engine.Menu;

/// <summary>
/// 명령 창 하나를 띄우고 · 겹치고 · 되돌아가고 · 닫는 규칙.
/// </summary>
/// <remarks>
/// 창을 <b>짓는</b> 것은 <see cref="GameMenu"/> 가, <b>내놓는</b> 것은 이쪽이 맡는다.
/// 예전에는 창마다 같은 것을 조금씩 다르게 들고 있었다 — <c>_xxxMenu != null</c> 검사,
/// <c>Closed += … = null</c>, 되돌아갈 손을 인자로 넘기기(<c>FleetMenu(back)</c>) 같은 것들이다.
///
/// <b>겹을 쌓아 둔다.</b> 항구 창에서 "함대편성" 을 고르면 한 겹 들어가고(<see cref="Push"/>),
/// "편성 종료" 로 되돌아온다(<see cref="Pop"/>). 되돌아갈 창은 <b>다시 짓는다</b> —
/// 그 사이에 값이 바뀌었을 수 있어서다(돈을 썼으면 "허드렛일" 줄이 생기거나 사라진다).
/// 그래서 겹에 담는 것은 다 지은 창이 아니라 <b>짓는 손</b>이다.
///
/// 창은 하나만 뜬다. 이미 떠 있으면 새로 띄우지 않고 담긴 것만 갈아 끼우므로 자리가 안 튄다.
/// </remarks>
internal sealed class GameMenuHost(Window owner)
{
    private readonly Window _owner = owner;
    private readonly List<Func<GameMenu>> _stack = [];

    private MenuWindow? _window;

    /// <summary>닫는 중에 또 닫으라는 말이 들어오는 것을 막는 빗장.</summary>
    private bool _closing;

    /// <summary>창이 떠 있는지.</summary>
    public bool IsOpen => _window != null;

    /// <summary>지금 떠 있는 창. 그 위에 딴 창을 띄울 때 주인으로 쓴다. 안 떠 있으면 null.</summary>
    public Window? Window => _window;

    /// <summary>창이 닫혔을 때. 어떻게 닫혔든(줄·ESC·오른쪽 단추) 한 번 온다.</summary>
    public event Action? Closed;

    /// <summary>
    /// 뿌리 메뉴를 연다. 쌓여 있던 겹은 버린다.
    /// </summary>
    /// <param name="at">
    /// 띄울 화면 자리(WPF 단위). 안 주면 <b>주인 창 한가운데</b>다 — 게임이 시설 명령 창을
    /// 내는 자리다(볼트 <c>15.분석-시설 화면 엔진</c>).
    /// </param>
    public void Open(Func<GameMenu> build, Point? at = null)
    {
        _stack.Clear();
        _stack.Add(build);
        Show(at);
    }

    /// <summary>한 겹 들어간다. <see cref="Pop"/> 으로 되돌아온다.</summary>
    public void Push(Func<GameMenu> build)
    {
        if (_window == null) { Open(build); return; }
        _stack.Add(build);
        Show(null);
    }

    /// <summary>한 겹 되돌아간다. 뿌리에서 부르면 창을 닫는다.</summary>
    public void Pop()
    {
        if (_stack.Count <= 1) { Close(); return; }
        _stack.RemoveAt(_stack.Count - 1);
        Show(null);
    }

    /// <summary>지금 겹을 다시 지어 끼운다. 줄 안의 값이 바뀌었을 때 부른다.</summary>
    public void Refresh()
    {
        if (_window != null) Show(null);
    }

    /// <summary>창을 앞으로 가져온다. 안 떠 있으면 아무 일도 없다.</summary>
    public void Focus() => _window?.Activate();

    /// <summary>창을 닫는다. 닫는 중이면 아무 일도 없다.</summary>
    public void Close()
    {
        if (_closing || _window == null) return;
        _closing = true;
        // 점으로 오므라든 뒤에 닫힌다 — 게임도 그렇게 걷는다.
        _window.CloseZoomed();
    }

    private void Show(Point? at)
    {
        var content = _stack[^1]();

        if (_window == null)
        {
            _window = at is { } p ? MenuWindow.ShowAt(_owner, content, p)
                                  : MenuWindow.ShowCentered(_owner, content);
            _window.Closing += (_, _) => _closing = true;
            _window.Closed += (_, _) =>
            {
                _window = null;
                _closing = false;
                _stack.Clear();
                Closed?.Invoke();
            };
            return;
        }

        _window.SetContent(content);
        // 줄 수가 달라지면 크기도 달라진다 — 게임처럼 다시 한가운데로 민다.
        if (at is { } spot) _window.MoveTo(spot); else _window.Recenter();
        _window.Activate();
    }
}
