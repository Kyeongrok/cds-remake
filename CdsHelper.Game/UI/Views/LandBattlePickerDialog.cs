using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using CdsHelper.Game.Local.Helpers;

namespace CdsHelper.Game.UI.Views;

/// <summary>
/// 커스텀 발견물 장면의 「[육상전]」 줄을 짓는 창 — 상대 대장(인물)을 찾아 고르고, 상대 · 아군 병력과 일기토 그림 벌을 정한다.
/// </summary>
/// <remarks>
/// 지은 줄은 「[육상전] 아테네의 장군 상대 100~139 아군 70 그림 3」 꼴이다(<see cref="UserDiscoveries.LandTerms"/>).
/// 병력을 비우면 그 칸은 안 건드린다 — 상대는 그 인물의 무리 그대로, 아군은 지금 선원 그대로다.
/// 그림 벌은 이 싸움 뒤에 이어지는 대본 일기토의 상대 그림이다(<c>26 0F</c>).
/// </remarks>
public sealed class LandBattlePickerDialog : GameWindow
{
    private readonly Engine.Game _game;
    private readonly TextBox _find = new() { Width = 240, Margin = new Thickness(0, 0, 10, 0), VerticalContentAlignment = VerticalAlignment.Center };
    private readonly ListBox _list = new() { Margin = new Thickness(10, 6, 10, 6) };
    private readonly TextBox _foeFrom = Box(), _foeTo = Box(), _ours = Box();
    private readonly ComboBox _set = new() { Width = 160 };

    /// <summary>지은 줄의 글(「[육상전]」 뒤). 안 지었으면 null.</summary>
    public string? Picked { get; private set; }

    /// <summary>일기토 상대 그림 벌 이름(<see cref="FighterSprites.SetForCulture"/> 의 1~8).</summary>
    private static readonly string[] SetNames = ["유럽", "아프리카", "아랍", "아시아A", "아시아B", "중국", "일본", "아즈텍"];

    private static TextBox Box() => new() { Width = 60, Margin = new Thickness(0, 0, 6, 0), VerticalContentAlignment = VerticalAlignment.Center };

    private LandBattlePickerDialog(Engine.Game game, string current)
    {
        _game = game;
        Title = "육상전 넣기";
        Width = 640;
        Height = 600;
        Background = Brushes.White;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        _set.Items.Add("정하지 않음");
        for (int i = 0; i < SetNames.Length; i++) _set.Items.Add($"{i + 1} {SetNames[i]}");
        _set.SelectedIndex = 0;

        var top = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(10, 10, 10, 0) };
        top.Children.Add(Label("상대 대장 찾기"));
        top.Children.Add(_find);

        StackPanel Row(params UIElement[] parts)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(10, 4, 10, 0) };
            foreach (var p in parts) row.Children.Add(p);
            return row;
        }
        var terms = new StackPanel { Margin = new Thickness(0, 0, 0, 6) };
        terms.Children.Add(Row(Label("상대 병력"), _foeFrom, Label("~"), _foeTo, Label("명 (비우면 그 인물의 무리 그대로, 한 수만 적으면 그 수로)")));
        terms.Children.Add(Row(Label("아군 병력"), _ours, Label("명 (비우면 지금 선원 그대로)")));
        terms.Children.Add(Row(Label("일기토 그림"), _set, Label("이 싸움 뒤 대본 일기토의 상대 그림")));

        var ok = new Button { Content = "넣기", Padding = new Thickness(14, 2, 14, 2), Margin = new Thickness(0, 0, 6, 0) };
        ok.Click += (_, _) => Choose();
        var cancel = new Button { Content = "취소", Padding = new Thickness(14, 2, 14, 2) };
        cancel.Click += (_, _) => Close();
        var bar = new StackPanel
        {
            Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(10, 4, 10, 10), Children = { ok, cancel },
        };

        var page = new DockPanel();
        DockPanel.SetDock(top, Dock.Top);
        DockPanel.SetDock(bar, Dock.Bottom);
        DockPanel.SetDock(terms, Dock.Bottom);
        page.Children.Add(top);
        page.Children.Add(bar);
        page.Children.Add(terms);
        page.Children.Add(_list);
        Content = page;

        // 지금 줄이 있으면 그 값으로 연다.
        var now = UserDiscoveries.LandTerms(current);
        if (now.FoeFrom >= 0) { _foeFrom.Text = $"{now.FoeFrom}"; if (now.FoeTo > now.FoeFrom) _foeTo.Text = $"{now.FoeTo}"; }
        if (now.Ours >= 0) _ours.Text = $"{now.Ours}";
        if (now.DuelSet is >= 1 and <= 8) _set.SelectedIndex = now.DuelSet;

        _find.TextChanged += (_, _) => Fill(-1);
        _list.MouseDoubleClick += (_, _) => Choose();
        Loaded += (_, _) => { Fill(now.Person); _find.Focus(); };
    }

    private static TextBlock Label(string text) => new() { Text = text, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) };

    /// <summary>찾기로 거른 인물을 늘어놓는다 — 번호 · 이름 · 직업.</summary>
    private void Fill(int pick)
    {
        _list.Items.Clear();
        string want = _find.Text.Trim();
        foreach (var row in PersonTable.Open()?.People ?? [])
        {
            if (row.Name.Length == 0) continue;
            if (want.Length > 0 && !row.Name.Contains(want, StringComparison.OrdinalIgnoreCase) && row.Id.ToString() != want) continue;
            string job = _game.PersonTemplates?.Find(row.Id)?.JobName is { Length: > 0 } j ? $"  [{j}]" : "";
            var item = new ListBoxItem { Content = $"{row.Id,3}  {row.Name}{job}", Tag = row.Name };
            _list.Items.Add(item);
            if (row.Id == pick) _list.SelectedItem = item;
        }
        if (_list.SelectedItem != null) _list.ScrollIntoView(_list.SelectedItem);
    }

    private void Choose()
    {
        if (_list.SelectedItem is not ListBoxItem { Tag: string name })
        {
            MessageBox.Show(this, "상대 대장을 골라 주세요.", Title);
            return;
        }
        var text = new System.Text.StringBuilder(name);
        if (int.TryParse(_foeFrom.Text.Trim(), out int from) && from > 0)
        {
            text.Append($" 상대 {from}");
            if (int.TryParse(_foeTo.Text.Trim(), out int to) && to > from) text.Append($"~{to}");
        }
        if (int.TryParse(_ours.Text.Trim(), out int ours) && ours > 0) text.Append($" 아군 {ours}");
        if (_set.SelectedIndex >= 1) text.Append($" 그림 {_set.SelectedIndex}");
        Picked = text.ToString();
        Close();
    }

    /// <summary>창을 띄워 육상전 줄을 짓는다. 「[육상전]」 뒤의 글, 취소했으면 null.</summary>
    public static string? Pick(Window owner, Engine.Game game, string current = "")
    {
        var window = new LandBattlePickerDialog(game, current) { Owner = owner };
        window.ShowDialog();
        return window.Picked;
    }
}
