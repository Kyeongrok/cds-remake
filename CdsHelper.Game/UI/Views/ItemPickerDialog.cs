using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace CdsHelper.Game.UI.Views;

/// <summary>
/// 아이템을 찾아 고르는 창 — 커스텀 발견물 장면의 「[아이템]」 줄에 넣을 아이템을 이름으로 고르게 한다. 번호를 외우지 않아도 된다.
/// </summary>
/// <remarks>위는 찾기 칸, 왼쪽은 분류마다 거른 목록, 오른쪽은 그 아이템의 설명이다. 두 번 누르거나 「고르기」로 정한다.</remarks>
public sealed class ItemPickerDialog : GameWindow
{
    private readonly Engine.Game _game;
    private readonly TextBox _find = new() { Width = 240, Margin = new Thickness(0, 0, 10, 0), VerticalContentAlignment = VerticalAlignment.Center };
    private readonly ComboBox _category = new() { Width = 150 };
    private readonly ListBox _list = new() { Width = 300, Margin = new Thickness(10, 6, 6, 6) };
    private readonly TextBox _detail = new()
    {
        IsReadOnly = true, TextWrapping = TextWrapping.Wrap, BorderThickness = new Thickness(0), FontSize = 13,
        Margin = new Thickness(6, 6, 10, 6), VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
    };

    /// <summary>고른 아이템(번호 · 이름). 안 골랐으면 null.</summary>
    public (int Id, string Name)? Picked { get; private set; }

    private ItemPickerDialog(Engine.Game game, int current)
    {
        _game = game;
        Title = "아이템 고르기";
        Width = 760;
        Height = 560;
        Background = Brushes.White;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        _category.Items.Add("모든 분류");
        foreach (string name in Local.Helpers.ItemTable.CategoryNames) _category.Items.Add(name);
        _category.SelectedIndex = 0;

        var top = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(10, 10, 10, 0) };
        top.Children.Add(new TextBlock { Text = "찾기", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) });
        top.Children.Add(_find);
        top.Children.Add(new TextBlock { Text = "분류", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) });
        top.Children.Add(_category);

        var ok = new Button { Content = "고르기", Padding = new Thickness(14, 2, 14, 2), Margin = new Thickness(0, 0, 6, 0) };
        ok.Click += (_, _) => Choose();
        var cancel = new Button { Content = "취소", Padding = new Thickness(14, 2, 14, 2) };
        cancel.Click += (_, _) => Close();
        var bar = new StackPanel
        {
            Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(10, 0, 10, 10), Children = { ok, cancel },
        };

        var body = new DockPanel();
        DockPanel.SetDock(_list, Dock.Left);
        body.Children.Add(_list);
        body.Children.Add(_detail);

        var page = new DockPanel();
        DockPanel.SetDock(top, Dock.Top);
        DockPanel.SetDock(bar, Dock.Bottom);
        page.Children.Add(top);
        page.Children.Add(bar);
        page.Children.Add(body);
        Content = page;

        _find.TextChanged += (_, _) => Fill(-1);
        _category.SelectionChanged += (_, _) => Fill(-1);
        _list.SelectionChanged += (_, _) => Describe();
        _list.MouseDoubleClick += (_, _) => Choose();
        Loaded += (_, _) => { Fill(current); _find.Focus(); };
    }

    /// <summary>찾기 · 분류로 거른 아이템을 늘어놓는다.</summary>
    private void Fill(int pick)
    {
        _list.Items.Clear();
        string want = _find.Text.Trim();
        int category = _category.SelectedIndex - 1;
        foreach (var item in _game.Items?.Items ?? [])
        {
            if (item.Name.Length == 0) continue;
            if (category >= 0 && item.Category != category) continue;
            if (want.Length > 0 && !item.Name.Contains(want, StringComparison.OrdinalIgnoreCase) && item.Id.ToString() != want) continue;
            var row = new ListBoxItem { Content = $"{item.Id,3}  {item.Name}  [{item.CategoryName}]", Tag = item.Id };
            _list.Items.Add(row);
            if (item.Id == pick) _list.SelectedItem = row;
        }
        if (_list.SelectedItem == null && _list.Items.Count > 0) _list.SelectedIndex = 0;
        if (_list.SelectedItem != null) _list.ScrollIntoView(_list.SelectedItem);
        if (_game.Items == null) _detail.Text = "아이템 표를 읽지 못했습니다 — 게임 안에서 개발 창을 열어 주세요.";
    }

    private void Describe()
    {
        if (_list.SelectedItem is not ListBoxItem { Tag: int id } || _game.Items?.Find(id) is not { } item) { _detail.Text = ""; return; }
        string text = _game.ItemText?.Of(id) ?? "";
        _detail.Text = $"{item.Name}  (아이템 {item.Id}번 · {item.CategoryName})\n\n{text}";
    }

    private void Choose()
    {
        if (_list.SelectedItem is not ListBoxItem { Tag: int id } || _game.Items?.Find(id) is not { } item) return;
        Picked = (id, item.Name);
        Close();
    }

    /// <summary>창을 띄워 아이템을 고르게 한다. 고른 것, 취소했으면 null.</summary>
    public static (int Id, string Name)? Pick(Window owner, Engine.Game game, int current = -1)
    {
        var window = new ItemPickerDialog(game, current) { Owner = owner };
        window.ShowDialog();
        return window.Picked;
    }
}
