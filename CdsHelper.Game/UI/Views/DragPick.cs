using System.Windows;
using System.Windows.Input;

namespace CdsHelper.Game.UI.Views;

/// <summary>
/// 여럿 고르는 목록에서 <b>끌어서 한꺼번에</b> 켜고 끄기.
/// </summary>
/// <remarks>
/// 누른 줄이 꺼져 있었으면 끄는 동안 지나간 줄을 <b>다 켜고</b>, 켜져 있었으면 <b>다 끈다</b> — 누른 줄에서 지금 줄까지의
/// 사이만 그렇게 하고, 사이 밖으로 되돌아 나간 줄은 누르기 전 모양으로 돌린다. 끌지 않고 떼면 한 줄만 켜고 끄는
/// 여느 누르기와 같다. 시장 구입·매각(<see cref="GameList"/>)과 발표·버리기 고르기(<see cref="HintListDialog"/>)가 쓴다.
/// </remarks>
internal sealed class DragPick
{
    private readonly UIElement _host;
    private readonly IReadOnlyList<FrameworkElement> _rows;
    private readonly Func<int, bool> _isOn;
    private readonly Action<int, bool> _set;
    private readonly Func<int, bool> _open;
    private readonly Action<int> _changed;

    private int _anchor = -1, _last = -1;
    private bool _mode;
    private bool[] _before = [];

    /// <param name="host">끄는 동안 마우스를 잡아 둘 판 — 줄들을 담은 것.</param>
    /// <param name="rows">줄들. 차례가 줄 번호다 — 줄이 늘면 같은 목록에 더한다.</param>
    /// <param name="isOn">그 줄이 켜져 있는지.</param>
    /// <param name="set">그 줄을 켜거나 끈다.</param>
    /// <param name="changed">한 번 끌 때마다 — 받는 쪽이 다시 칠한다. 인자는 지금 손이 가 있는 줄.</param>
    /// <param name="open">고를 수 있는 줄인지. 없으면 다 된다.</param>
    public DragPick(UIElement host, IReadOnlyList<FrameworkElement> rows, Func<int, bool> isOn,
                    Action<int, bool> set, Action<int> changed, Func<int, bool>? open = null)
    {
        _host = host;
        _rows = rows;
        _isOn = isOn;
        _set = set;
        _changed = changed;
        _open = open ?? (_ => true);

        host.MouseMove += Move;
        host.MouseLeftButtonUp += End;
        host.LostMouseCapture += (_, _) => _anchor = -1;
    }

    /// <summary>그 줄에서 누르기를 받는다.</summary>
    public void Attach(FrameworkElement row, int index) =>
        row.MouseLeftButtonDown += (_, e) =>
        {
            // 누름은 여기서 삼킨다 — 창 끌기가 먼저 걸리면 마우스를 잡아 버린다.
            e.Handled = true;
            Press(index);
        };

    /// <summary>그 줄을 눌렀다 — 끌기를 시작한다. 줄의 누름을 제 손으로 받는 쪽이 부른다.</summary>
    public void Press(int index)
    {
        if (index < 0 || index >= _rows.Count || !_open(index)) return;

        _before = [.. Enumerable.Range(0, _rows.Count).Select(_isOn)];
        _mode = !_before[index];
        _anchor = _last = index;
        Apply(index);
        _host.CaptureMouse();
    }

    private void Move(object sender, MouseEventArgs e)
    {
        if (_anchor < 0) return;

        int at = RowAt(e);
        if (at == _last) return;
        _last = at;
        Apply(at);
        _rows[at].BringIntoView();   // 두루마리 끝에 닿으면 따라 굴러간다
    }

    private void End(object sender, MouseButtonEventArgs e)
    {
        if (_anchor < 0) return;
        _anchor = -1;
        _host.ReleaseMouseCapture();
        e.Handled = true;
    }

    /// <summary>누른 줄에서 <paramref name="at"/> 까지를 누른 쪽으로 맞추고, 사이 밖은 누르기 전으로 돌린다.</summary>
    private void Apply(int at)
    {
        int lo = Math.Min(_anchor, at), hi = Math.Max(_anchor, at);
        for (int i = 0; i < _rows.Count && i < _before.Length; i++)
        {
            bool want = i >= lo && i <= hi && _open(i) ? _mode : _before[i];
            if (_isOn(i) != want) _set(i, want);
        }
        _changed(at);
    }

    /// <summary>마우스 밑의 줄. 위로 벗어나면 첫 줄, 아래로 벗어나면 끝 줄이다.</summary>
    private int RowAt(MouseEventArgs e)
    {
        for (int i = 0; i < _rows.Count; i++)
        {
            var row = _rows[i];
            double y = e.GetPosition(row).Y;
            if (y < row.ActualHeight) return i;   // 줄 사이 틈은 아래 줄로 친다
        }
        return _rows.Count - 1;
    }
}
