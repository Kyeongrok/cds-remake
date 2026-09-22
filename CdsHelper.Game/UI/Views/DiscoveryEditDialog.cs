using System.IO;
using System.Windows;
using System.Windows.Controls;
using CdsHelper.Game.Local.Helpers;
using CdsHelper.Support.Local.Settings;
using Microsoft.Win32;

namespace CdsHelper.Game.UI.Views;

/// <summary>
/// 발견물 표를 보고, 고치고, <b>새 발견물을 더하는</b> 창.
/// </summary>
/// <remarks>
/// <b>적어 둔 발견물표.json 을 직접 고치지 않는다</b> — 고치거나 더한 줄만 따로 적어 두고
/// (<see cref="DiscoveryEdits"/>) 표가 읽힐 때 얹는다. 그래서 표에 원래 없던 번호(274 이상)도
/// 그대로 더할 수 있고, <see cref="DiscoveryTable"/> 의 <c>SnapshotVersion</c> 이 올라 원본표가
/// 다시 구워져도 여기서 더한 줄은 사라지지 않는다.
///
/// 「새 발견물 추가」는 다음 빈 번호로 빈 줄 하나를 얹어 두고 그 자리에서 바로 칸을 채우게
/// 한다 — 값을 다 채우기 전에는 자리(<c>X1~Y2</c>)가 <c>-1</c> 이라 <see cref="DiscoveryTable.Record.HasPlace"/>
/// 가 거짓이고, 그러면 아무 데서도 안 잡힌다.
/// </remarks>
public sealed class DiscoveryEditDialog : GameWindow
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

    private readonly TextBox _search = new()
    {
        Width = 180,
        Padding = new Thickness(4, 2, 4, 2),
        VerticalContentAlignment = VerticalAlignment.Center,
    };

    private readonly CheckBox _editedOnly = new()
    {
        Content = "고치거나 더한 줄만",
        VerticalAlignment = VerticalAlignment.Center,
        Margin = new Thickness(12, 0, 0, 0),
    };

    private readonly Button _add = new()
    {
        Content = "새 발견물 추가",
        Padding = new Thickness(10, 2, 10, 2),
        Margin = new Thickness(12, 0, 0, 0),
    };

    private readonly Button _reset = new()
    {
        Content = "이 줄 되돌리기",
        Padding = new Thickness(10, 2, 10, 2),
        Margin = new Thickness(6, 0, 0, 0),
        ToolTip = "원본에 있던 번호면 게임 값으로 돌아가고, 여기서 새로 더한 번호면 통째로 없어진다",
    };

    private readonly Button _resetAll = new()
    {
        Content = "전부 되돌리기",
        Padding = new Thickness(10, 2, 10, 2),
        Margin = new Thickness(6, 0, 0, 0),
    };

    private readonly Button _export = new()
    {
        Content = "내보내기…",
        Padding = new Thickness(10, 2, 10, 2),
        Margin = new Thickness(18, 0, 0, 0),
        ToolTip = "고른 발견물을 표 줄·대본·힌트·그림/동영상까지 통째로 zip 한 장에 담는다",
    };

    private readonly Button _import = new()
    {
        Content = "불러오기…",
        Padding = new Thickness(10, 2, 10, 2),
        Margin = new Thickness(6, 0, 0, 0),
        ToolTip = "내보내 둔 zip 을 읽어 그 번호의 발견물을 통째로 되돌려 놓는다",
    };

    private readonly TextBlock _status = new()
    {
        Margin = new Thickness(10, 4, 10, 8),
        TextWrapping = TextWrapping.Wrap,
    };

    private DiscoveryTable? _discoveries;
    private HintTable? _hints;
    private string _dir = "";

    public DiscoveryEditDialog()
    {
        Title = "발견물 고치기 · 더하기";
        Width = 1200;
        Height = 700;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        Col("번호", nameof(Row.Id), 52, readOnly: true);
        Col("이름", nameof(Row.Name), 150);
        Col("갈래", nameof(Row.Category), 44);
        Col("갈래 이름", nameof(Row.CategoryName), 72, readOnly: true);
        Col("일련번호", nameof(Row.Hint), 60);
        Col("가리키는 힌트", nameof(Row.HintName), 110, readOnly: true);
        Col("보수", nameof(Row.Reward), 70);
        Col("주는 물건", nameof(Row.ItemId), 60);
        CheckCol("간접", nameof(Row.Indirect), 44);
        CheckCol("처음부터", nameof(Row.OpenAtStart), 56);
        CheckCol("뭍", nameof(Row.OnLand), 36);
        CheckCol("한 번만", nameof(Row.Once), 52);
        Col("X1", nameof(Row.X1), 56);
        Col("Y1", nameof(Row.Y1), 56);
        Col("X2", nameof(Row.X2), 56);
        Col("Y2", nameof(Row.Y2), 56);
        Col("그림", nameof(Row.Picture), 44);
        Col("동영상", nameof(Row.Movie), 52);
        Col("고침", nameof(Row.Mark), 40, readOnly: true);

        _grid.CellEditEnding += (_, e) =>
        {
            if (e.EditAction == DataGridEditAction.Commit)
                Dispatcher.BeginInvoke(new Action(Collect));
        };

        _search.TextChanged += (_, _) => Rebuild();
        _editedOnly.Checked += (_, _) => Rebuild();
        _editedOnly.Unchecked += (_, _) => Rebuild();

        _add.Click += (_, _) => AddNew();
        _reset.Click += (_, _) =>
        {
            if (_grid.SelectedItem is Row row) DiscoveryEdits.Reset(row.Id);
            Rebuild();
        };
        _resetAll.Click += (_, _) => { DiscoveryEdits.ResetAll(); Rebuild(); };
        _export.Click += (_, _) => ExportSelected();
        _import.Click += (_, _) => ImportOne();

        var label = new TextBlock
        {
            Text = "찾기",
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 6, 0),
        };
        var bar = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(10, 10, 10, 0),
            Children = { label, _search, _editedOnly, _add, _reset, _resetAll, _export, _import },
        };

        var page = new DockPanel();
        DockPanel.SetDock(bar, Dock.Top);
        DockPanel.SetDock(_status, Dock.Bottom);
        page.Children.Add(bar);
        page.Children.Add(_status);
        page.Children.Add(_grid);
        Content = page;

        Loaded += (_, _) => Load();
    }

    /// <summary>목록 한 줄. 「갈래 이름」·「가리키는 힌트」·「고침」은 셈해서 낸 것이라 못 고친다.</summary>
    private sealed class Row
    {
        public int Id { get; init; }
        public string Name { get; set; } = "";
        public int Category { get; set; }
        public int Hint { get; set; }
        public int Reward { get; set; }
        public int ItemId { get; set; } = -1;
        public bool Indirect { get; set; }
        public bool OpenAtStart { get; set; }
        public bool OnLand { get; set; }
        public bool Once { get; set; }
        public int X1 { get; set; } = -1;
        public int Y1 { get; set; } = -1;
        public int X2 { get; set; } = -1;
        public int Y2 { get; set; } = -1;
        public int Picture { get; set; } = -1;
        public int Movie { get; set; } = -1;

        public string CategoryName { get; init; } = "";
        public string HintName { get; init; } = "";

        /// <summary>손으로 고치거나 더한 줄에만 <c>●</c> 가 선다.</summary>
        public string Mark { get; init; } = "";
    }

    private void Col(string header, string path, double width, bool readOnly = false) =>
        _grid.Columns.Add(new DataGridTextColumn
        {
            Header = header,
            Binding = new System.Windows.Data.Binding(path),
            Width = new DataGridLength(width),
            IsReadOnly = readOnly,
        });

    private void CheckCol(string header, string path, double width) =>
        _grid.Columns.Add(new DataGridCheckBoxColumn
        {
            Header = header,
            Binding = new System.Windows.Data.Binding(path),
            Width = new DataGridLength(width),
        });

    private void Load()
    {
        _dir = Path.GetDirectoryName(AppSettings.LastSaveFilePath) ?? "";
        _discoveries = DiscoveryTable.Open(_dir);
        _hints = HintTable.Open(_dir);

        if (_discoveries == null)
        {
            _status.Text = "발견물 표를 못 읽었습니다 — 세이브를 한 번 열어 게임 폴더를 알려 주세요"
                         + $" ({DiscoveryTable.LastError})".TrimEnd();
            _grid.IsEnabled = false;
            return;
        }
        Rebuild();
    }

    private void Rebuild()
    {
        if (_discoveries is not { } table) return;

        int keep = _grid.SelectedItem is Row picked ? picked.Id : -1;
        string find = _search.Text.Trim();

        var rows = new List<Row>();
        foreach (var d in table.Discoveries.OrderBy(d => d.Id))
        {
            var row = ToRow(d);
            if (_editedOnly.IsChecked == true && row.Mark.Length == 0) continue;
            if (find.Length > 0 && !Matches(row, find)) continue;
            rows.Add(row);
        }

        _grid.ItemsSource = rows;
        if (keep >= 0) _grid.SelectedItem = rows.FirstOrDefault(r => r.Id == keep);

        int edits = DiscoveryEdits.All.Count;
        _status.Text = $"발견물 {table.Discoveries.Count}줄 가운데 {rows.Count}줄 — 게임 표 0x0051C540"
                     + (edits == 0 ? "" : $" · 손으로 고치거나 더한 줄 {edits}")
                     + "   ·   자리(X1~Y2)가 -1 이면 그 발견물은 아무 데서도 안 잡힌다"
                     + "   ·   고치거나 더한 것은 놀이 안에서도 그대로 쓰인다";
    }

    private Row ToRow(in DiscoveryTable.Record d) => new()
    {
        Id = d.Id,
        Name = d.Name,
        Category = d.Category,
        Hint = d.Hint,
        Reward = d.Reward,
        ItemId = d.ItemId,
        Indirect = d.Indirect,
        OpenAtStart = d.OpenAtStart,
        OnLand = d.OnLand,
        Once = d.Once,
        X1 = d.X1,
        Y1 = d.Y1,
        X2 = d.X2,
        Y2 = d.Y2,
        Picture = d.Picture,
        Movie = d.Movie,
        CategoryName = d.CategoryName,
        HintName = HintNameOf(d.Hint),
        Mark = DiscoveryEdits.Of(d.Id) == null ? "" : "●",
    };

    /// <summary>그 일련번호를 가리키는 힌트 이름. 없으면 빈 글.</summary>
    private string HintNameOf(int serial)
    {
        if (serial < 0 || _hints is not { } hints) return "";
        var hit = hints.Hints.FirstOrDefault(h => h.Discovery == serial);
        return hit.Name ?? "";
    }

    private static bool Matches(Row row, string find) =>
        row.Name.Contains(find, StringComparison.OrdinalIgnoreCase)
        || row.HintName.Contains(find, StringComparison.OrdinalIgnoreCase)
        || row.CategoryName.Contains(find, StringComparison.OrdinalIgnoreCase);

    /// <summary>다음 빈 번호로 빈 줄 하나를 얹는다.</summary>
    private void AddNew()
    {
        if (_discoveries is not { } table) return;

        int nextId = table.Discoveries.Select(d => d.Id).DefaultIfEmpty(-1).Max() + 1;
        DiscoveryEdits.Set(new DiscoveryTable.Record(
            Id: nextId, Name: "새 발견물", Category: 0, Hint: -1, Reward: 0, ItemId: -1,
            Indirect: false, OpenAtStart: true, OnLand: true, Once: true,
            X1: -1, Y1: -1, X2: -1, Y2: -1));

        Rebuild();
        if (_grid.ItemsSource is List<Row> rows)
            _grid.SelectedItem = rows.FirstOrDefault(r => r.Id == nextId);
    }

    /// <summary>고친 칸만 골라 적어 둔다 — 게임 값과 같으면 씌우지 않는다.</summary>
    private void Collect()
    {
        if (_discoveries is not { } table || _grid.ItemsSource is not List<Row> rows) return;

        foreach (var row in rows)
        {
            var edited = new DiscoveryTable.Record(
                row.Id, row.Name, row.Category, row.Hint, row.Reward, row.ItemId,
                row.Indirect, row.OpenAtStart, row.OnLand, row.Once,
                row.X1, row.Y1, row.X2, row.Y2, row.Picture, row.Movie,
                Clip: table.Original(row.Id)?.Clip ?? -1);

            // 원본에 있던 번호이고 게임 값과 똑같아졌으면 씌운 것을 걷는다.
            if (table.Original(row.Id) is { } game && SameCore(edited, game))
                DiscoveryEdits.Reset(row.Id);
            else
                DiscoveryEdits.Set(edited);
        }
        Rebuild();
    }

    /// <summary>Erase 칸(자동 셈)은 빼고 사람이 고칠 수 있는 칸만 견준다.</summary>
    private static bool SameCore(in DiscoveryTable.Record a, in DiscoveryTable.Record b) =>
        a.Name == b.Name && a.Category == b.Category && a.Hint == b.Hint
        && a.Reward == b.Reward && a.ItemId == b.ItemId && a.Indirect == b.Indirect
        && a.OpenAtStart == b.OpenAtStart && a.OnLand == b.OnLand && a.Once == b.Once
        && a.X1 == b.X1 && a.Y1 == b.Y1 && a.X2 == b.X2 && a.Y2 == b.Y2
        && a.Picture == b.Picture && a.Movie == b.Movie;

    /// <summary>
    /// 고른 발견물을 zip 한 장으로 내보낸다 — 표 줄·대본·힌트·미디어까지 통째로다.
    /// </summary>
    private void ExportSelected()
    {
        if (_discoveries is not { } table) return;
        if (_grid.SelectedItem is not Row row) { _status.Text = "내보낼 줄을 먼저 고르세요."; return; }

        var box = new SaveFileDialog
        {
            Title = $"「{row.Name}」(발견물 {row.Id}) 내보내기",
            Filter = "발견물 꾸러미 (*.discovery.zip)|*.discovery.zip|모든 파일|*.*",
            FileName = $"{row.Id:000}_{row.Name}.discovery.zip",
        };
        if (box.ShowDialog(this) != true) return;

        string error = DiscoveryPackage.Export(row.Id, _dir, table, _hints, box.FileName);
        _status.Text = error.Length == 0
            ? $"{box.FileName} 로 내보냈습니다."
            : $"못 내보냈습니다 — {error}";
    }

    /// <summary>
    /// zip 을 불러와 담겨 있던 번호로 통째로 되돌려 놓는다 — 표 줄·대본·힌트·미디어까지.
    /// </summary>
    private void ImportOne()
    {
        var box = new OpenFileDialog
        {
            Title = "발견물 불러오기",
            Filter = "발견물 꾸러미 (*.discovery.zip)|*.discovery.zip|모든 파일|*.*",
        };
        if (box.ShowDialog(this) != true) return;

        string error = DiscoveryPackage.Import(box.FileName, out int id);
        if (error.Length > 0) { _status.Text = $"못 불러왔습니다 — {error}"; return; }

        Rebuild();
        if (_grid.ItemsSource is List<Row> rows)
            _grid.SelectedItem = rows.FirstOrDefault(r => r.Id == id);
        _status.Text = $"발견물 {id} 번을 불러왔습니다.";
    }

    /// <summary>창을 띄운다.</summary>
    public static void Show(Window? owner)
    {
        var window = new DiscoveryEditDialog();
        if (owner != null) window.Owner = owner;
        window.ShowDialog();
    }
}
