using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using CdsHelper.Game.Engine.Town;

namespace CdsHelper.Game.UI.Views;

/// <summary>
/// 술집 · 여관 무명 손님 소문을 고치는 창 — 문화권 갈래 아홉 벌(<see cref="TavernRumors"/>).
/// </summary>
/// <remarks>
/// 술집과 여관은 같은 벌을 쓴다. 「술집만」에 표시한 줄은 술자리 말이라 여관 손님은 안 한다.
/// 저장하면 고친 갈래만 <see cref="TavernRumorEdits"/> 가 따로 적어 두고, 원본 줄은 그대로다.
/// </remarks>
public sealed class TavernRumorEditDialog : GameWindow
{
    private readonly ListBox _sets = new() { Width = 220, Margin = new Thickness(10, 10, 4, 4) };

    private readonly DataGrid _grid = new()
    {
        AutoGenerateColumns = false,
        CanUserAddRows = false,
        CanUserDeleteRows = false,
        HeadersVisibility = DataGridHeadersVisibility.Column,
        SelectionMode = DataGridSelectionMode.Single,
        Margin = new Thickness(4, 10, 10, 4),
    };

    private readonly TextBlock _status = new() { Margin = new Thickness(10, 4, 10, 8), TextWrapping = TextWrapping.Wrap };

    private List<Row> _rows = [];
    private int _set = -1;
    private bool _dirty;

    /// <summary>목록 한 줄.</summary>
    private sealed class Row
    {
        public bool TavernOnly { get; set; }
        public string Text { get; set; } = "";
    }

    public TavernRumorEditDialog()
    {
        Title = "소문 편집 — 술집 · 여관 무명 손님";
        Width = 1080;
        Height = 680;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = System.Windows.Media.Brushes.White;

        _grid.Columns.Add(new DataGridCheckBoxColumn
        {
            Header = "술집만",
            Binding = new Binding(nameof(Row.TavernOnly)) { Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged },
            Width = new DataGridLength(56),
        });
        _grid.Columns.Add(new DataGridTextColumn
        {
            Header = "소문",
            Binding = new Binding(nameof(Row.Text)) { Mode = BindingMode.TwoWay },
            Width = new DataGridLength(1, DataGridLengthUnitType.Star),
            ElementStyle = new Style(typeof(TextBlock)) { Setters = { new Setter(TextBlock.TextWrappingProperty, TextWrapping.Wrap) } },
        });
        _grid.CellEditEnding += (_, e) => { if (e.EditAction == DataGridEditAction.Commit) { _dirty = true; ShowStatus(); } };

        _sets.SelectionChanged += (_, _) =>
        {
            if (_sets.SelectedIndex == _set) return;
            if (_dirty && !AskDrop()) { _sets.SelectedIndex = _set; return; }
            Open(_sets.SelectedIndex);
        };

        var add = Push("줄 추가", () =>
        {
            int at = _grid.SelectedItem is Row picked ? _rows.IndexOf(picked) + 1 : _rows.Count;
            var row = new Row { Text = "새 소문" };
            _rows.Insert(at, row);
            Rebind(row);
            _dirty = true;
            ShowStatus();
        });
        var remove = Push("줄 빼기", () =>
        {
            if (_grid.SelectedItem is not Row row) return;
            _rows.Remove(row);
            Rebind(null);
            _dirty = true;
            ShowStatus();
        });
        var revert = Push("이 갈래 원래대로", () =>
        {
            if (_set < 0) return;
            if (MessageBox.Show(this, $"「{TavernRumors.SetNames[_set]}」 소문을 원본으로 되돌릴까요? 고친 것은 사라집니다.",
                                "원래대로", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
            TavernRumorEdits.Reset(_set);
            _dirty = false;
            Open(_set);
            RefreshSetNames();
        });
        var save = Push("저장", Save);

        var bar = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(10, 0, 10, 0),
            Children = { add, remove, revert, save },
        };

        var body = new DockPanel();
        DockPanel.SetDock(_sets, Dock.Left);
        body.Children.Add(_sets);
        body.Children.Add(_grid);

        var page = new DockPanel();
        DockPanel.SetDock(_status, Dock.Bottom);
        DockPanel.SetDock(bar, Dock.Bottom);
        page.Children.Add(_status);
        page.Children.Add(bar);
        page.Children.Add(body);
        Content = page;

        TavernRumorEdits.Reload();
        RefreshSetNames();
        _sets.SelectedIndex = 0;

        Closing += (_, e) => { if (_dirty && !AskDrop()) e.Cancel = true; };
    }

    private static Button Push(string text, Action click)
    {
        var b = new Button { Content = text, Padding = new Thickness(10, 2, 10, 2), Margin = new Thickness(0, 0, 6, 0) };
        b.Click += (_, _) => click();
        return b;
    }

    /// <summary>갈래 목록 — 고친 갈래는 ● 를 붙인다.</summary>
    private void RefreshSetNames()
    {
        int keep = _sets.SelectedIndex;
        _sets.Items.Clear();
        for (int i = 0; i < TavernRumors.SetNames.Length; i++)
            _sets.Items.Add((TavernRumorEdits.Of(i) != null ? "● " : "") + TavernRumors.SetNames[i]);
        _sets.SelectedIndex = keep;
    }

    private void Open(int set)
    {
        _set = set;
        _rows = set < 0 ? [] : [.. TavernRumors.LinesOf(set).Select(l => new Row { Text = l.Text, TavernOnly = l.TavernOnly })];
        _dirty = false;
        Rebind(null);
    }

    private void Rebind(Row? pick)
    {
        _grid.ItemsSource = null;
        _grid.ItemsSource = _rows;
        if (pick != null) { _grid.SelectedItem = pick; _grid.ScrollIntoView(pick); }
        ShowStatus();
    }

    private void ShowStatus()
    {
        if (_set < 0) { _status.Text = ""; return; }
        int innLines = _rows.Count(r => !r.TavernOnly);
        _status.Text = $"「{TavernRumors.SetNames[_set]}」 {_rows.Count}줄 (여관 손님은 {innLines}줄)"
                     + (TavernRumorEdits.Of(_set) != null ? " · 고친 갈래" : " · 원본")
                     + (_dirty ? " · 저장 안 함" : "")
                     + "   ·   술집과 여관이 같은 소문을 쓴다 · 「술집만」 줄은 여관 손님이 안 한다 · 놀이 앱은 다음에 열 때 읽는다";
    }

    private bool AskDrop() =>
        MessageBox.Show(this, "저장하지 않은 고침이 있습니다. 버릴까요?", "소문 편집", MessageBoxButton.YesNo) == MessageBoxResult.Yes;

    private void Save()
    {
        if (_set < 0) return;
        _grid.CommitEdit(DataGridEditingUnit.Row, true);
        var lines = _rows.Select(r => new TavernRumors.Line(r.Text ?? "", r.TavernOnly)).ToList();
        // 원본과 똑같으면 씌우지 않는다 — 고친 갈래 표시(●)가 거짓으로 서지 않게.
        if (lines.Where(l => !string.IsNullOrWhiteSpace(l.Text)).SequenceEqual(TavernRumors.DefaultsOf(_set)))
            TavernRumorEdits.Reset(_set);
        else
            TavernRumorEdits.Set(_set, lines);
        Open(_set);
        RefreshSetNames();
        _status.Text = "저장했습니다.   ·   " + _status.Text;
    }

    /// <summary>창을 띄운다.</summary>
    public static void Show(Window? owner)
    {
        var window = new TavernRumorEditDialog();
        if (owner != null) window.Owner = owner;
        window.ShowDialog();
    }
}
