using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace CdsHelper.Game.UI.Views;

/// <summary>
/// 자동항해 목적지를 도시 <b>이름</b>으로 고르는 창.
/// </summary>
/// <remarks>
/// 게임에는 없는 창이다 — <see cref="PersonMoveDialog"/> 처럼 여느 WPF 꼴로 두고
/// 커맨드 창의 "자동항해…" 줄에서 연다. 지도를 <b>클릭</b>해 목적지를 찍는 길은 이 창을
/// 거치지 않고 발견물지도(<see cref="DiscoveryMapDialog"/>)와 주 지도에서 바로 된다 —
/// 여기는 도시 <b>이름</b>으로 고르고 싶을 때 쓴다.
/// </remarks>
public sealed class AutoSailDialog : GameWindow
{
    private readonly ComboBox _city = new()
    {
        Width = 260,
        IsEditable = true,
        IsTextSearchEnabled = true,
        StaysOpenOnEdit = true,
        VerticalAlignment = VerticalAlignment.Center,
    };

    private readonly TextBlock _note = new()
    {
        Margin = new Thickness(10, 0, 10, 8),
        Foreground = Brushes.OrangeRed,
        TextWrapping = TextWrapping.Wrap,
        MaxWidth = 320,
    };

    private readonly Func<int, (bool Ok, string Message)> _start;

    private AutoSailDialog(IEnumerable<(int Id, string Name)> cities, Func<int, (bool Ok, string Message)> start)
    {
        _start = start;

        Title = "자동항해";
        SizeToContent = SizeToContent.WidthAndHeight;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        Background = GameUi.Back;

        foreach (var (id, name) in cities.OrderBy(c => c.Name, StringComparer.Ordinal))
            _city.Items.Add(new ComboBoxItem { Content = name, Tag = id });
        if (_city.Items.Count > 0) _city.SelectedIndex = 0;

        var label = new TextBlock
        {
            Text = "목적지:",
            Foreground = GameUi.Text,
            Margin = new Thickness(10, 10, 6, 10),
            VerticalAlignment = VerticalAlignment.Center,
        };
        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Center,
            Children = { label, _city },
        };

        var go = GameUi.PushButton("항해 시작", Start, 100);
        var cancel = GameUi.PushButton("취소", Close, 80);
        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(4, 8, 4, 4),
            Children = { go, cancel },
        };

        Content = new StackPanel { Children = { row, buttons, _note } };
    }

    private void Start()
    {
        int id = _city.SelectedItem is ComboBoxItem { Tag: int fromList } ? fromList
            : _city.Items.OfType<ComboBoxItem>()
                .FirstOrDefault(i => string.Equals((string)i.Content, _city.Text, StringComparison.Ordinal))
                is { Tag: int fromText } ? fromText : -1;

        if (id < 0) { _note.Text = "목록에서 목적지 도시를 고르세요."; return; }

        var (ok, message) = _start(id);
        if (!ok) { _note.Text = message; return; }
        Close();
    }

    /// <summary>창을 연다. 고를 도시가 하나도 없으면 아무 일도 안 한다.</summary>
    /// <param name="cities">고를 수 있는 도시(번호·이름).</param>
    /// <param name="start">
    /// 도시 번호를 받아 그리로 자동항해를 건다. 실패하면 그 까닭을 돌려준다 — 창은 안 닫힌다.
    /// </param>
    public static void Show(Window owner, IEnumerable<(int Id, string Name)> cities,
                            Func<int, (bool Ok, string Message)> start)
    {
        var list = cities.ToList();
        if (list.Count == 0) { NoticeDialog.Show(owner, "아는 도시가 없습니다"); return; }
        new AutoSailDialog(list, start) { Owner = owner }.ShowDialog();
    }
}
