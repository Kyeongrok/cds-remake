using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using CdsHelper.Game.Local.Helpers;
using CdsHelper.Support.Local.Settings;

namespace CdsHelper.Game.UI.Views;

/// <summary>
/// 나라 표를 고치는 창 — 이름 · 쓰는 말 · 수도 · 출입여부.
/// </summary>
/// <remarks>
/// <b>적어 둔 <c>나라표.json</c> 을 직접 고치지 않는다.</b> 그 파일은 EXE 에서 구워 둔
/// 본이라 판이 바뀌면 다시 구워진다. 고친 것만 따로 적어 두고(<see cref="NationEdits"/>)
/// 표가 읽힐 때 얹는다 — 그래서 여기서 고치면 <b>놀이 안에서도 그대로</b> 따라온다.
/// 도시·문화권 창과 같은 결이다.
/// </remarks>
public sealed class NationEditDialog : GameWindow
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

    private readonly Button _reset = new()
    {
        Content = "이 줄 되돌리기",
        Padding = new Thickness(10, 2, 10, 2),
    };

    private readonly Button _resetAll = new()
    {
        Content = "전부 되돌리기",
        Padding = new Thickness(10, 2, 10, 2),
        Margin = new Thickness(6, 0, 0, 0),
    };

    private readonly TextBlock _status = new() { Margin = new Thickness(10, 4, 10, 8) };

    private NationTable? _table;
    private CityTable? _cities;
    private CityBuildingTable? _names;

    public NationEditDialog()
    {
        Title = "나라 표 고치기";
        Width = 760;
        Height = 620;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        Col("번호", nameof(Row.Id), 56, readOnly: true);
        Col("이름", nameof(Row.Name), 220);
        Col("언어ID", nameof(Row.Language), 60);
        Col("언어", nameof(Row.LanguageName), 130, readOnly: true);
        Col("수도ID", nameof(Row.Capital), 60);
        Col("수도", nameof(Row.CapitalName), 130, readOnly: true);
        Col("출입ID", nameof(Row.Access), 60);
        Col("출입여부", nameof(Row.AccessName), 150, readOnly: true);
        Col("고침", nameof(Row.Mark), 44, readOnly: true);

        _grid.CellEditEnding += (_, e) =>
        {
            // 칸을 다 쓰고 나서야 값이 들어온다 — 한 박자 뒤에 거둔다.
            if (e.EditAction == DataGridEditAction.Commit)
                Dispatcher.BeginInvoke(new Action(Collect));
        };

        _reset.Click += (_, _) =>
        {
            if (_grid.SelectedItem is Row row) NationEdits.Reset(row.Id);
            Rebuild();
        };
        _resetAll.Click += (_, _) => { NationEdits.ResetAll(); Rebuild(); };

        var bar = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(10, 0, 10, 0),
            Children = { _reset, _resetAll },
        };

        var page = new DockPanel();
        DockPanel.SetDock(_status, Dock.Bottom);
        DockPanel.SetDock(bar, Dock.Bottom);
        page.Children.Add(_status);
        page.Children.Add(bar);
        page.Children.Add(_grid);
        Content = page;

        Loaded += (_, _) => Load();
    }

    /// <summary>목록 한 줄.</summary>
    /// <param name="Mark">손으로 고친 줄에만 <c>●</c> 가 선다.</param>
    private sealed class Row
    {
        public int Id { get; init; }
        public string Name { get; set; } = "";
        public int Language { get; set; }
        public int Capital { get; set; }

        /// <summary>출입여부 0~2. 0 자유 · 1 마을만 막힘 · 2 항구까지 막힘.</summary>
        public int Access { get; set; }
        public string LanguageName { get; init; } = "";
        public string CapitalName { get; init; } = "";
        public string AccessName { get; init; } = "";
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

    private void Load()
    {
        string dir = Path.GetDirectoryName(AppSettings.LastSaveFilePath) ?? "";
        _table = NationTable.Open(dir);
        _cities = CityTable.Open();
        _names = CityBuildingTable.Open(dir);

        if (_table == null)
        {
            _status.Text = "나라 표를 못 읽었습니다 — 세이브를 한 번 열어 게임 폴더를 알려 주세요"
                         + $" ({NationTable.LastError})".TrimEnd();
            _grid.IsEnabled = false;
            return;
        }
        Rebuild();
    }

    private void Rebuild()
    {
        if (_table is not { } table) return;

        int keep = _grid.SelectedItem is Row picked ? picked.Id : -1;

        var rows = new List<Row>();
        foreach (var nation in table.Nations)
            rows.Add(new Row
            {
                Id = nation.Id,
                Name = nation.Name,
                Language = nation.Language,
                Capital = nation.Capital,
                Access = nation.Entry,
                AccessName = NationTable.EntryName(nation.Entry),
                LanguageName = LanguageName(nation.Language),
                CapitalName = _cities?.Cities.FirstOrDefault(c => c.Id == nation.Capital).Name ?? "",
                Mark = NationEdits.Of(nation.Id) == null ? "" : "●",
            });

        _grid.ItemsSource = rows;
        if (keep >= 0) _grid.SelectedItem = rows.FirstOrDefault(r => r.Id == keep);

        int edits = NationEdits.All.Count;
        _status.Text = $"나라 {rows.Count}곳 — 게임 표 0x004CA370 (24바이트 x {NationTable.Count})"
                     + (edits == 0 ? "" : $" · 손으로 고친 곳 {edits}곳")
                     + "   ·   고친 것은 놀이 안에서도 그대로 쓰인다";
    }

    private string LanguageName(int language) =>
        _names is { } names && language >= 0 && language < names.LanguageNames.Count
            ? names.LanguageNames[language] : "";

    /// <summary>고친 칸만 골라 적어 둔다 — 게임 값과 같으면 씌우지 않는다.</summary>
    private void Collect()
    {
        if (_table is not { } table || _grid.ItemsSource is not List<Row> rows) return;

        foreach (var row in rows)
        {
            if (table.Original(row.Id) is not { } game) continue;
            NationEdits.Set(row.Id,
                            row.Name == game.Name ? null : row.Name,
                            row.Language == game.Language ? null : row.Language,
                            row.Capital == game.Capital ? null : row.Capital,
                            row.Access == game.Entry ? null : row.Access);
        }
        Rebuild();
    }

    /// <summary>창을 띄운다.</summary>
    public static void Show(Window? owner)
    {
        var window = new NationEditDialog();
        if (owner != null) window.Owner = owner;
        window.ShowDialog();
    }
}
