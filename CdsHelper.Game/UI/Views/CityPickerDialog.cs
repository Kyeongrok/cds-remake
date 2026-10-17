using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using CdsHelper.Game.Local.Helpers;

namespace CdsHelper.Game.UI.Views;

/// <summary>
/// 커스텀 발견물의 소문 도시를 고르는 창 — 네비게이션(<see cref="AutoSailDialog"/>)처럼 문화권 탭마다 도시를 늘어놓는다.
/// 네비게이션은 아는 도시만 내지만 여기는 모든 도시다.
/// </summary>
/// <remarks>소문 줄은 도시 칸이 셋이라(<see cref="RumorTable"/>) 셋까지만 고른다.</remarks>
public sealed class CityPickerDialog : GameWindow
{
    private readonly List<(CheckBox Box, int Id, string Name)> _boxes = [];
    private readonly TextBlock _chosen = new() { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 10, 0), TextWrapping = TextWrapping.Wrap };
    private readonly int _limit;

    /// <summary>고른 도시 번호(고른 차례). 고르지 않고 닫았으면 null.</summary>
    public List<int>? Picked { get; private set; }

    /// <summary>고른 차례 — 체크한 차례대로 소문 칸에 들어간다.</summary>
    private readonly List<int> _order = [];

    private CityPickerDialog(Engine.Game game, IReadOnlyList<int> current, int limit)
    {
        _limit = limit;
        Title = $"소문 도시 고르기 (셋까지)";
        Width = 900;
        Height = 620;
        Background = Brushes.White;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var cities = Enumerable.Range(0, Support.Local.Helpers.GameMapCoords.CityCount)
            .Select(id => (Id: id, Name: game.CityName(id), Culture: game.CultureOf(id)))
            .Where(c => c.Name.Length > 0)
            .ToList();

        var tabs = new TabControl { Margin = new Thickness(10, 10, 10, 6) };
        foreach (var group in cities.GroupBy(c => c.Culture.Length > 0 ? c.Culture : "그 밖")
                                    .OrderBy(g => AutoSailDialog.CultureRank(g.Key)).ThenBy(g => g.Key, StringComparer.Ordinal))
        {
            var grid = new WrapPanel { Margin = new Thickness(6) };
            foreach (var (id, name, _) in group.OrderBy(c => c.Name, StringComparer.Ordinal))
            {
                var box = new CheckBox { Content = $"{name} ({id})", Width = 160, Margin = new Thickness(0, 3, 0, 3), IsChecked = current.Contains(id) };
                box.Checked += (_, _) => Toggle(box, id, true);
                box.Unchecked += (_, _) => Toggle(box, id, false);
                _boxes.Add((box, id, name));
                grid.Children.Add(box);
            }
            tabs.Items.Add(new TabItem
            {
                Header = $"{group.Key} {group.Count()}",
                Content = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = grid },
            });
        }
        _order.AddRange(current.Where(id => _boxes.Any(b => b.Id == id)));

        var ok = new Button { Content = "확인", Padding = new Thickness(14, 2, 14, 2), Margin = new Thickness(0, 0, 6, 0) };
        ok.Click += (_, _) => { Picked = [.. _order]; Close(); };
        var cancel = new Button { Content = "취소", Padding = new Thickness(14, 2, 14, 2) };
        cancel.Click += (_, _) => Close();
        var bar = new DockPanel { Margin = new Thickness(10, 0, 10, 10) };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Children = { ok, cancel } };
        DockPanel.SetDock(buttons, Dock.Right);
        bar.Children.Add(buttons);
        bar.Children.Add(_chosen);

        var page = new DockPanel();
        DockPanel.SetDock(bar, Dock.Bottom);
        page.Children.Add(bar);
        page.Children.Add(tabs);
        Content = page;
        ShowChosen();
    }

    private void Toggle(CheckBox box, int id, bool on)
    {
        if (on)
        {
            if (_order.Contains(id)) return;
            if (_order.Count >= _limit)
            {
                box.IsChecked = false;   // 셋을 넘으면 되돌린다
                _chosen.Text = $"셋까지만 고를 수 있습니다 — 먼저 하나를 풀어 주세요.   고른 도시: {Names()}";
                return;
            }
            _order.Add(id);
        }
        else _order.Remove(id);
        ShowChosen();
    }

    private string Names() =>
        _order.Count == 0 ? "(없음)" : string.Join(", ", _order.Select(id => _boxes.FirstOrDefault(b => b.Id == id).Name));

    private void ShowChosen() => _chosen.Text = $"고른 도시: {Names()}";

    /// <summary>창을 띄워 도시를 고르게 한다. 고른 번호들, 취소했으면 null.</summary>
    public static List<int>? Pick(Window owner, Engine.Game game, IReadOnlyList<int> current, int limit = 3)
    {
        var window = new CityPickerDialog(game, current, limit) { Owner = owner };
        window.ShowDialog();
        return window.Picked;
    }
}
