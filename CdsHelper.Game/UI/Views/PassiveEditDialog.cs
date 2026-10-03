using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using CdsHelper.Game.Engine.Town;
using CdsHelper.Support.Local.Models;

namespace CdsHelper.Game.UI.Views;

/// <summary>
/// 패시브 편집 — 모드 「작위」의 혜택을 만들고 고친다(<see cref="Passives"/>).
/// </summary>
/// <remarks>
/// 줄 하나가 패시브 하나다 — 어느 작위부터 · 무슨 갈래 · 얼마나. 같은 갈래를 여럿 두면 더해지고, 작위가 오르면 아래 것도 다 가진다.
/// 「운 +5」 같은 것은 갈래를 「능력치 증가」로 두고 능력치 칸에서 운을 고른다. 저장하면 <c>exe-tables/패시브.json</c> 에 적히고
/// 놀이 앱이 다음에 열 때 읽는다.
/// </remarks>
public sealed class PassiveEditDialog : GameWindow
{
    private readonly DataGrid _grid = new()
    {
        AutoGenerateColumns = false,
        CanUserAddRows = false,
        CanUserDeleteRows = false,
        HeadersVisibility = DataGridHeadersVisibility.Column,
        SelectionMode = DataGridSelectionMode.Single,
        Margin = new Thickness(10, 10, 10, 4),
    };

    private readonly TextBlock _status = new() { Margin = new Thickness(10, 4, 10, 8), TextWrapping = TextWrapping.Wrap };

    private List<Row> _rows = [];

    /// <summary>고른 칸의 한 줄 — 값과 보이는 글.</summary>
    private sealed record Choice(int Value, string Text);

    /// <summary>목록 한 줄.</summary>
    private sealed class Row
    {
        public int Rank { get; set; } = 1;
        public string Name { get; set; } = "";
        public int Effect { get; set; }
        public int Stat { get; set; }
        public int Amount { get; set; }

        public Passive ToPassive() => new(Name.Trim(), (PassiveEffect)Effect, Amount, Rank, Stat);

        /// <summary>풀이 — 이름을 비우면 이것이 이름이 된다.</summary>
        public string Text => Passives.Describe(ToPassive());

        public static Row Of(Passive p) => new()
        {
            Rank = p.Rank, Name = p.Name, Effect = (int)p.Effect, Stat = p.Stat, Amount = p.Amount,
        };
    }

    public PassiveEditDialog()
    {
        Title = "패시브 편집 — 작위 혜택";
        Width = 860;
        Height = 560;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = System.Windows.Media.Brushes.White;

        Combo("작위", nameof(Row.Rank), 80,
              Enumerable.Range(0, Nobility.MaxRank + 1).Select(r => new Choice(r, r == 0 ? "미배치" : $"{r} {Nobility.Names[r]}")));
        Text("이름", nameof(Row.Name), 180);
        Combo("갈래", nameof(Row.Effect), 140,
              Enum.GetValues<PassiveEffect>().Select(e => new Choice((int)e, Passives.NameOf(e))));
        Combo("능력치", nameof(Row.Stat), 90,
              Ability.Names.Select((n, i) => new Choice(i, n)));
        Text("양", nameof(Row.Amount), 60);
        Text("풀이", nameof(Row.Text), 0, readOnly: true);

        _grid.CellEditEnding += (_, e) =>
        {
            // 칸을 다 쓰고 나서야 값이 들어온다 — 한 박자 뒤에 풀이를 다시 칠한다.
            if (e.EditAction == DataGridEditAction.Commit)
                Dispatcher.BeginInvoke(new Action(() => { _grid.CommitEdit(); _grid.Items.Refresh(); ShowStatus(); }));
        };

        var add = Push("추가", () =>
        {
            int rank = _grid.SelectedItem is Row at ? at.Rank : 1;
            var row = new Row { Rank = rank, Effect = (int)PassiveEffect.Ability, Stat = Ability.Luck, Amount = 5 };
            _rows.Add(row);
            Rebind(row);
        });
        var remove = Push("빼기", () =>
        {
            if (_grid.SelectedItem is not Row row) return;
            _rows.Remove(row);
            Rebind(null);
        });
        var reset = Push("기본값으로", () =>
        {
            if (MessageBox.Show(this, "패시브 표를 처음 깔린 것으로 되돌릴까요? 고친 것은 사라집니다.", "기본값으로",
                                MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
            _rows = [.. Passives.Defaults.Select(Row.Of)];
            Rebind(null);
        });
        var save = Push("저장", Save);

        var bar = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(10, 0, 10, 0),
            Children = { add, remove, reset, save },
        };

        var page = new DockPanel();
        DockPanel.SetDock(_status, Dock.Bottom);
        DockPanel.SetDock(bar, Dock.Bottom);
        page.Children.Add(_status);
        page.Children.Add(bar);
        page.Children.Add(_grid);
        Content = page;

        Passives.Reload();
        _rows = [.. Passives.All.Select(Row.Of)];
        Rebind(null);
    }

    private void Text(string header, string path, double width, bool readOnly = false) =>
        _grid.Columns.Add(new DataGridTextColumn
        {
            Header = header,
            Binding = new Binding(path) { Mode = readOnly ? BindingMode.OneWay : BindingMode.TwoWay },
            Width = width > 0 ? new DataGridLength(width) : new DataGridLength(1, DataGridLengthUnitType.Star),
            IsReadOnly = readOnly,
        });

    private void Combo(string header, string path, double width, IEnumerable<Choice> choices) =>
        _grid.Columns.Add(new DataGridComboBoxColumn
        {
            Header = header,
            ItemsSource = choices.ToList(),
            SelectedValuePath = nameof(Choice.Value),
            DisplayMemberPath = nameof(Choice.Text),
            SelectedValueBinding = new Binding(path) { Mode = BindingMode.TwoWay },
            Width = new DataGridLength(width),
        });

    private static Button Push(string text, Action click)
    {
        var b = new Button { Content = text, Padding = new Thickness(10, 2, 10, 2), Margin = new Thickness(0, 0, 6, 0) };
        b.Click += (_, _) => click();
        return b;
    }

    /// <summary>작위 차례로 늘어놓고 그 줄을 고른다.</summary>
    private void Rebind(Row? pick)
    {
        _rows = [.. _rows.OrderBy(r => r.Rank == 0 ? int.MaxValue : r.Rank)];
        _grid.ItemsSource = _rows;
        if (pick != null) { _grid.SelectedItem = pick; _grid.ScrollIntoView(pick); }
        ShowStatus();
    }

    private void ShowStatus()
    {
        var byRank = Enumerable.Range(1, Nobility.MaxRank)
            .Select(r => $"{Nobility.Names[r]} {_rows.Count(x => x.Rank == r)}")
            .Append($"미배치 {_rows.Count(x => x.Rank == 0)}");
        _status.Text = $"패시브 {_rows.Count}개 ({string.Join(" · ", byRank)})"
                     + "   ·   작위가 오르면 아래 작위의 것도 다 가진다 · 같은 갈래는 더해진다 · 이름을 비우면 풀이가 이름이 된다"
                     + "   ·   능력치 칸은 갈래가 「능력치 증가」일 때만 쓴다 · 감소 갈래의 양은 % 다";
    }

    private void Save()
    {
        _grid.CommitEdit(DataGridEditingUnit.Row, true);
        foreach (var row in _rows)
            if (string.IsNullOrWhiteSpace(row.Name)) row.Name = row.Text;
        Passives.Save(_rows.Select(r => r.ToPassive()));
        _rows = [.. Passives.All.Select(Row.Of)];
        Rebind(null);
        _status.Text = "저장했습니다 — 놀이 앱은 다음에 열 때 새 표를 읽습니다.   ·   " + _status.Text;
    }

    /// <summary>창을 띄운다.</summary>
    public static void Show(Window? owner)
    {
        var window = new PassiveEditDialog();
        if (owner != null) window.Owner = owner;
        window.ShowDialog();
    }
}
