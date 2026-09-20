using System.IO;
using System.Windows;
using System.Windows.Controls;
using CdsHelper.Game.Engine.Sea;
using CdsHelper.Game.Local.Helpers;
using CdsHelper.Support.Local.Settings;

namespace CdsHelper.Game.UI.Views;

/// <summary>
/// 선수상 표를 고치는 창 — <b>등급</b>만 고친다.
/// </summary>
/// <remarks>
/// 원본 표는 <c>0x0054A0A0</c>(이름 ptr, 등급) 여덟 바이트 x 서른여섯이고, 이름은 소지품
/// 갈래 6(아이템 213~248)과 같은 차례다. 등급이 바뀌면 그대로 놀이에 든다.
/// <code>
///   막는 것    번호 % 4    0 쥐 · 1 병 · 2 반란 · 3 폭풍·눈보라
///   막을 확률  등급 x 30 − 20 (%)          ; 등급 0 은 −20 이라 아무것도 못 막는다
///   다는 삯    0x0056E280[등급]            ; 1 200 · 2 1000 · 3 5000 · 0 30000
/// </code>
/// 고친 것은 <see cref="FigureheadEdits"/> 가 따로 적어 두므로 표를 다시 구워도 안 날아간다.
/// </remarks>
public sealed class FigureheadEditDialog : GameWindow
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

    private ItemTable? _items;

    public FigureheadEditDialog()
    {
        Title = "선수상 등급 고치기";
        Width = 720;
        Height = 620;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        Col("번호", nameof(Row.Index), 56, readOnly: true);
        Col("아이템", nameof(Row.ItemId), 64, readOnly: true);
        Col("이름", nameof(Row.Name), 160, readOnly: true);
        Col("막는 것", nameof(Row.Guard), 110, readOnly: true);
        Col("등급", nameof(Row.Grade), 60);
        Col("막을 확률", nameof(Row.Odds), 90, readOnly: true);
        Col("다는 삯", nameof(Row.Price), 90, readOnly: true);
        Col("고침", nameof(Row.Mark), 44, readOnly: true);

        _grid.CellEditEnding += (_, e) =>
        {
            // 칸을 다 쓰고 나서야 값이 들어온다 — 한 박자 뒤에 거둔다.
            if (e.EditAction == DataGridEditAction.Commit)
                Dispatcher.BeginInvoke(new Action(Collect));
        };

        _reset.Click += (_, _) =>
        {
            if (_grid.SelectedItem is Row row) FigureheadEdits.Reset(row.Index);
            Rebuild();
        };
        _resetAll.Click += (_, _) => { FigureheadEdits.ResetAll(); Rebuild(); };

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

    /// <summary>목록 한 줄. 고칠 수 있는 것은 <see cref="Grade"/> 뿐이다.</summary>
    private sealed class Row
    {
        public int Index { get; init; }
        public int ItemId { get; init; }
        public string Name { get; init; } = "";
        public string Guard { get; init; } = "";
        public int Grade { get; set; }
        public string Odds { get; init; } = "";
        public string Price { get; init; } = "";
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
        _items = ItemTable.Open(dir);
        Rebuild();
    }

    /// <summary>막는 재앙 이름 — 번호 % 4 다.</summary>
    private static string GuardName(int guard) => guard switch
    {
        Figureheads.GuardsRats => "쥐",
        Figureheads.GuardsSickness => "병(괴혈·전염)",
        Figureheads.GuardsMutiny => "반란",
        _ => "폭풍·눈보라",
    };

    private void Rebuild()
    {
        int keep = _grid.SelectedItem is Row picked ? picked.Index : -1;

        var rows = new List<Row>();
        for (int i = 0; i < Figureheads.Count; i++)
        {
            int item = Figureheads.ToItem(i);
            int grade = Figureheads.GradeOf(i);
            rows.Add(new Row
            {
                Index = i,
                ItemId = item,
                Name = _items?.Find(item)?.Name ?? "",
                Guard = GuardName(Figureheads.GuardOf(i)),
                Grade = grade,
                Odds = $"{Figureheads.BlockPercent(i)}%",
                Price = $"{Figureheads.PriceOf(i)}닢",
                Mark = FigureheadEdits.Of(i) == null ? "" : "●",
            });
        }

        _grid.ItemsSource = rows;
        if (keep >= 0) _grid.SelectedItem = rows.FirstOrDefault(r => r.Index == keep);

        int edits = FigureheadEdits.All.Count;
        _status.Text = $"선수상 {rows.Count}개 — 게임 표 0x0054A0A0 (8바이트 x {Figureheads.Count})"
                     + (edits == 0 ? "" : $" · 손으로 고친 것 {edits}개")
                     + "   ·   등급은 0~3, 고친 것은 놀이 안에서도 그대로 쓰인다";
    }

    /// <summary>고친 칸만 골라 적어 둔다 — 게임 값과 같으면 씌우지 않는다.</summary>
    private void Collect()
    {
        if (_grid.ItemsSource is not List<Row> rows) return;

        foreach (var row in rows)
        {
            int grade = Math.Clamp(row.Grade, 0, FigureheadEdits.MaxGrade);
            if (grade == Figureheads.BuiltinGradeOf(row.Index)) FigureheadEdits.Reset(row.Index);
            else FigureheadEdits.Set(row.Index, grade);
        }
        Rebuild();
    }

    /// <summary>창을 띄운다.</summary>
    public static void Show(Window? owner)
    {
        var window = new FigureheadEditDialog();
        if (owner != null) window.Owner = owner;
        window.ShowDialog();
    }
}
