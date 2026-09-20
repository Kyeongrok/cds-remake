using System.IO;
using System.Windows;
using System.Windows.Controls;
using CdsHelper.Game.Engine.Discovery;
using CdsHelper.Game.Local.Helpers;
using CdsHelper.Support.Local.Settings;

namespace CdsHelper.Game.UI.Views;

/// <summary>
/// 경쟁자를 고치는 창 — 역사 항해자 열넷이 <b>언제 무엇을 채가는지</b>.
/// </summary>
/// <remarks>
/// 이 놀이에서 경쟁자가 하는 일은 이것 하나뿐이다(<see cref="HistoryVoyages"/>).
/// <c>HISTCHR.CDS</c> 의 대본이 시키는 일이라 원래는 손댈 수 없는데, 여기서 고친 것을
/// <see cref="VoyagerEdits"/> 가 따로 들고 표 위에 얹는다 — <b>놀이 안에서도 그대로</b>
/// 따라온다. <c>HISTCHR.CDS</c> 자체는 손대지 않는다.
///
/// 한 번짜리 발견물(<see cref="DiscoveryTable.Record.Once"/>)은 <b>먼저 가는 쪽이 임자</b>라
/// 여기서 날짜를 늦추면 그만큼 내가 갈 시간이 는다. 희망봉이 1488년 1월, 마젤란해협이
/// 1520년 10월이다.
/// </remarks>
public sealed class VoyagerEditDialog : GameWindow
{
    private readonly DataGrid _grid = new()
    {
        AutoGenerateColumns = false,
        CanUserAddRows = false,
        CanUserDeleteRows = false,
        HeadersVisibility = DataGridHeadersVisibility.Column,
        SelectionMode = DataGridSelectionMode.Single,
        // 머리글을 눌러 정렬한다 — 언제 무엇이 채이는지 보려면 연·월 차례가 제일 쓸모 있다.
        CanUserSortColumns = true,
        Margin = new Thickness(10, 10, 10, 4),
    };

    private readonly ComboBox _who = new()
    {
        Width = 190,
        Margin = new Thickness(0, 0, 6, 0),
    };

    private readonly Button _add = new()
    {
        Content = "줄 더하기",
        Padding = new Thickness(10, 2, 10, 2),
    };

    private readonly Button _drop = new()
    {
        Content = "이 줄 지우기",
        Padding = new Thickness(10, 2, 10, 2),
        Margin = new Thickness(6, 0, 0, 0),
    };

    private readonly Button _reset = new()
    {
        Content = "이 사람 되돌리기",
        Padding = new Thickness(10, 2, 10, 2),
        Margin = new Thickness(6, 0, 0, 0),
    };

    private readonly Button _resetAll = new()
    {
        Content = "전부 되돌리기",
        Padding = new Thickness(10, 2, 10, 2),
        Margin = new Thickness(6, 0, 0, 0),
    };

    private readonly TextBlock _status = new()
    {
        Margin = new Thickness(10, 4, 10, 8),
        TextWrapping = TextWrapping.Wrap,
    };

    private HistoryVoyages? _table;
    private DiscoveryTable? _finds;

    public VoyagerEditDialog()
    {
        Title = "경쟁자 고치기";
        Width = 820;
        Height = 640;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        Col("사람", nameof(Row.Who), 170, readOnly: true);
        _year = Col("년", nameof(Row.Year), 60);
        _month = Col("월", nameof(Row.Month), 44);
        Col("발견물", nameof(Row.Discovery), 70);
        Col("이름", nameof(Row.Name), 200, readOnly: true);
        Col("한번", nameof(Row.OnceMark), 48, readOnly: true);
        Col("고침", nameof(Row.Mark), 48, readOnly: true);

        _grid.CellEditEnding += (_, e) =>
        {
            // 칸을 다 쓰고 나서야 값이 들어온다 — 한 박자 뒤에 거둔다.
            if (e.EditAction == DataGridEditAction.Commit)
                Dispatcher.BeginInvoke(new Action(Collect));
        };

        _who.SelectionChanged += (_, _) => Rebuild();
        _add.Click += (_, _) => AddRow();
        _drop.Click += (_, _) => DropRow();
        _reset.Click += (_, _) => { if (Editing) { VoyagerEdits.Reset(Picked); Reload(); } };
        _resetAll.Click += (_, _) => { VoyagerEdits.ResetAll(); Reload(); };

        var bar = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(10, 0, 10, 0),
            Children = { _who, _add, _drop, _reset, _resetAll },
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
    /// <param name="Mark">파일과 달라진 사람의 줄에 <c>●</c> 가 선다.</param>
    /// <param name="OnceMark">한 번짜리 — 빼앗기면 영영 못 얻는 것에 <c>★</c> 가 선다.</param>
    private sealed class Row
    {
        public int Voyager { get; init; }
        public string Who { get; init; } = "";
        public int Year { get; set; }
        public int Month { get; set; }
        public int Discovery { get; set; }
        public string Name { get; init; } = "";
        public string OnceMark { get; init; } = "";
        public string Mark { get; init; } = "";
    }

    /// <summary>고르는 칸의 첫 줄 — 열넷을 통째로 본다는 뜻이다.</summary>
    private const int All = -1;

    /// <summary>
    /// 지금 고르고 있는 사람. 첫 줄(「전체」)이면 <see cref="All"/> 이다.
    /// </summary>
    /// <remarks>
    /// <b>「전체」일 때는 고칠 수가 없다.</b> 줄을 더하거나 지우거나 되돌리는 손은 다
    /// 사람 하나를 집어야 하기 때문이다 — 그때는 목록을 <b>읽기만</b> 한다.
    /// </remarks>
    private int Picked => _who.SelectedIndex <= 0 ? All : _who.SelectedIndex - 1;

    /// <summary>지금 한 사람만 보고 있는지. 그때만 고칠 수 있다.</summary>
    private bool Editing => Picked != All;

    /// <summary>연·월 칸 — 처음 정렬 화살표를 세워 두려고 들고 있는다.</summary>
    private readonly DataGridTextColumn _year, _month;

    private DataGridTextColumn Col(string header, string path, double width, bool readOnly = false)
    {
        var column = new DataGridTextColumn
        {
            Header = header,
            Binding = new System.Windows.Data.Binding(path),
            Width = new DataGridLength(width),
            IsReadOnly = readOnly,
            SortMemberPath = path,
        };
        _grid.Columns.Add(column);
        return column;
    }

    private void Load()
    {
        _who.Items.Add("전체");
        for (int i = 0; i < HistoryVoyages.Count; i++)
            _who.Items.Add($"{i,2}. {HistoryVoyages.NameOf(i)}");
        _who.SelectedIndex = 0;

        Reload();
    }

    private void Reload()
    {
        string dir = Path.GetDirectoryName(AppSettings.LastSaveFilePath) ?? "";
        _table = HistoryVoyages.Open(dir);
        _finds = DiscoveryTable.Open(dir);

        if (_table == null)
        {
            _status.Text = $"{HistoryVoyages.FileName} 를 못 읽었습니다 — 세이브를 한 번 열어"
                         + $" 게임 폴더를 알려 주세요 ({HistoryVoyages.LastError})".TrimEnd();
            _grid.IsEnabled = false;
            return;
        }
        _grid.IsEnabled = true;
        Rebuild();
    }

    private void Rebuild()
    {
        if (_table is not { } table) return;

        int who = Picked;
        var rows = new List<Row>();
        foreach (var voyage in table.All)
        {
            if (who != All && voyage.Voyager != who) continue;
            var found = _finds?.Find(voyage.Discovery);
            rows.Add(new Row
            {
                Voyager = voyage.Voyager,
                Who = HistoryVoyages.NameOf(voyage.Voyager),
                Year = voyage.Year,
                Month = voyage.Month,
                Discovery = voyage.Discovery,
                Name = found?.Name ?? "",
                OnceMark = found is { Once: true } ? "★" : "",
                Mark = VoyagerEdits.Touched(who) ? "●" : "",
            });
        }

        // 「전체」 일 때는 읽기만 한다 — 고치는 손이 다 사람 하나를 집어야 하기 때문이다.
        _grid.IsReadOnly = !Editing;
        _add.IsEnabled = _drop.IsEnabled = _reset.IsEnabled = Editing;

        _grid.ItemsSource = rows;

        // <b>처음은 연·월 오름차순</b>이다 — 표 차례(사람별)로는 언제 채이는지 안 보인다.
        // 머리글을 누르면 그 뒤로는 고른 차례를 따른다.
        _grid.Items.SortDescriptions.Clear();
        _grid.Items.SortDescriptions.Add(
            new System.ComponentModel.SortDescription(nameof(Row.Year),
                                                      System.ComponentModel.ListSortDirection.Ascending));
        _grid.Items.SortDescriptions.Add(
            new System.ComponentModel.SortDescription(nameof(Row.Month),
                                                      System.ComponentModel.ListSortDirection.Ascending));
        _year.SortDirection = System.ComponentModel.ListSortDirection.Ascending;
        _month.SortDirection = System.ComponentModel.ListSortDirection.Ascending;
        _grid.Items.Refresh();

        int edited = VoyagerEdits.Count;
        int once = rows.Count(r => r.OnceMark.Length > 0);
        _status.Text = $"{HistoryVoyages.NameOf(who)} — 채가는 것 {rows.Count}건"
                     + (once == 0 ? "" : $" (★ 한 번짜리 {once}건)")
                     + $"   ·   표 전체 {table.All.Count}건 / {HistoryVoyages.Count}명"
                     + (edited == 0 ? "" : $" · 손댄 사람 {edited}명")
                     + "   ·   고친 것은 놀이 안에서도 그대로 쓰인다"
                     + $" ({HistoryVoyages.FileName} 는 손대지 않는다)";
    }

    /// <summary>그 사람에게 줄을 하나 더한다 — 놀이 첫날로 놓고 시작한다.</summary>
    private void AddRow()
    {
        if (_table == null || !Editing) return;

        var rows = Now();
        rows.Add(new HistoryVoyages.Voyage(Picked, Support.Local.Models.Player.StartDate.Year, 1, 0));
        VoyagerEdits.Set(Picked, rows);
        Reload();
    }

    private void DropRow()
    {
        if (_table == null || !Editing || _grid.SelectedItem is not Row picked) return;

        var rows = Now();
        int at = rows.FindIndex(v => v.Year == picked.Year && v.Month == picked.Month
                                  && v.Discovery == picked.Discovery);
        if (at < 0) return;

        rows.RemoveAt(at);
        VoyagerEdits.Set(Picked, rows);
        Reload();
    }

    /// <summary>지금 그 사람의 목록(고친 것이 얹힌 뒤).</summary>
    private List<HistoryVoyages.Voyage> Now() =>
        _table == null ? [] : [.. _table.All.Where(v => v.Voyager == Picked)];

    /// <summary>고친 칸을 거둔다 — 파일과 같아지면 씌운 것을 걷는다.</summary>
    private void Collect()
    {
        if (_table is not { } table || !Editing
            || _grid.ItemsSource is not List<Row> rows) return;

        int who = Picked;
        var edited = rows
            .Select(r => new HistoryVoyages.Voyage(who, Clamp(r.Year, 1000, 3000),
                                                   Clamp(r.Month, 1, 12),
                                                   Clamp(r.Discovery, 0, DiscoveryTable.Count - 1)))
            .ToList();
        edited.Sort(HistoryVoyages.ByVoyagerThenDate);

        var origin = table.Original.Where(v => v.Voyager == who).ToList();
        if (edited.SequenceEqual(origin)) VoyagerEdits.Reset(who);
        else VoyagerEdits.Set(who, edited);

        Reload();
    }

    private static int Clamp(int value, int low, int high) => Math.Clamp(value, low, high);

    /// <summary>창을 띄운다.</summary>
    public static void Show(Window? owner)
    {
        var window = new VoyagerEditDialog();
        if (owner != null) window.Owner = owner;
        window.ShowDialog();
    }
}
