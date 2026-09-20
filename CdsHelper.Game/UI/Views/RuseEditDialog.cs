using System.Windows;
using System.Windows.Controls;
using CdsHelper.Game.Engine.Land;
using CdsHelper.Game.Local.Helpers;

namespace CdsHelper.Game.UI.Views;

/// <summary>
/// 묘책(기습명령)이 먹힐 확률 표를 보고 고치는 창.
/// </summary>
/// <remarks>
/// 원본 표는 <c>0x00549B80</c> 의 네 바이트 x 서른셋이고, 성사 굴림
/// <c>0x00449080</c> 이 이렇게 가른다.
/// <code>
///   00449085  t = 0x00447070(1)                 ; 상대 문화권 0~10
///   0044908a  index = 묘책(+0x54) + t * 3
///   00449094  odds  = 0x00549B80[index]
///   0044909b  rand(100) &lt;= odds 면 성사(소리 0x27), 아니면 실패(소리 0x28)
/// </code>
/// <b>칸 간격이 셋인데 묘책은 넷</b>이라 이웃 문화권끼리 칸을 나눠 쓴다 — 이를테면
/// 칸 3 은 「서유럽·심판」이자 「북유럽·기습」이다. 원본 데이터 그대로의 흠이라 고치지 않고,
/// 어느 짝이 그 칸을 보는지만 적어 준다. 심판(<c>0x00448F80</c>)은 <b>굴리지 않으므로</b>
/// 그 짝은 값이 있어도 안 쓰인다.
///
/// 고친 것은 <see cref="RuseEdits"/> 가 따로 적어 두므로 표를 다시 구워도 안 날아간다.
/// </remarks>
public sealed class RuseEditDialog : GameWindow
{
    /// <summary>문화권 이름 열하나.</summary>
    private static readonly string[] CultureNames =
    [
        "서유럽", "북유럽", "동유럽", "이슬람", "인도", "동남아시아",
        "동아시아", "일본", "아프리카", "중남미", "오세아니아",
    ];

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

    private readonly TextBlock _status = new() { Margin = new Thickness(10, 4, 10, 8), TextWrapping = TextWrapping.Wrap };

    public RuseEditDialog()
    {
        Title = "묘책 확률 고치기";
        Width = 760;
        Height = 620;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        Col("칸", nameof(Row.Index), 48, readOnly: true);
        Col("확률", nameof(Row.Odds), 64);
        Col("게임 값", nameof(Row.Builtin), 70, readOnly: true);
        Col("이 칸을 보는 짝", nameof(Row.Users), 420, readOnly: true);
        Col("고침", nameof(Row.Mark), 44, readOnly: true);

        _grid.CellEditEnding += (_, e) =>
        {
            // 칸을 다 쓰고 나서야 값이 들어온다 — 한 박자 뒤에 거둔다.
            if (e.EditAction == DataGridEditAction.Commit)
                Dispatcher.BeginInvoke(new Action(Collect));
        };

        _reset.Click += (_, _) =>
        {
            if (_grid.SelectedItem is Row row) RuseEdits.Reset(row.Index);
            Rebuild();
        };
        _resetAll.Click += (_, _) => { RuseEdits.ResetAll(); Rebuild(); };

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

        Loaded += (_, _) => Rebuild();
    }

    /// <summary>목록 한 줄. 고칠 수 있는 것은 <see cref="Odds"/> 뿐이다.</summary>
    private sealed class Row
    {
        public int Index { get; init; }
        public int Odds { get; set; }
        public int Builtin { get; init; }
        public string Users { get; init; } = "";
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

    /// <summary>그 칸을 읽는 (문화권 · 묘책) 짝을 다 적는다.</summary>
    private static string UsersOf(int index)
    {
        var said = new List<string>();
        for (int culture = 0; culture < CultureNames.Length; culture++)
            for (int ruse = 0; ruse < LandBattle.Ruses.Length; ruse++)
            {
                if (LandBattle.RuseOddsAt(culture, ruse) != index) continue;
                string name = LandBattle.Ruses[ruse];
                // 심판은 굴리지 않으므로 값이 있어도 안 쓰인다(0x00448F80).
                said.Add($"{CultureNames[culture]}·{name}"
                       + (ruse == LandBattle.Judgement ? "(안 굴림)" : ""));
            }
        return said.Count == 0 ? "— 아무도 안 봄" : string.Join(" · ", said);
    }

    private void Rebuild()
    {
        int keep = _grid.SelectedItem is Row picked ? picked.Index : -1;

        var rows = new List<Row>();
        for (int i = 0; i < LandBattle.RuseOddsCount; i++)
            rows.Add(new Row
            {
                Index = i,
                Odds = LandBattle.RuseOddsOf(i),
                Builtin = LandBattle.BuiltinRuseOdds(i),
                Users = UsersOf(i),
                Mark = RuseEdits.Of(i) == null ? "" : "●",
            });

        _grid.ItemsSource = rows;
        if (keep >= 0) _grid.SelectedItem = rows.FirstOrDefault(r => r.Index == keep);

        int edits = RuseEdits.All.Count;
        _status.Text = $"묘책 확률 {rows.Count}칸 — 게임 표 0x00549B80 (4바이트 x {rows.Count})"
                     + (edits == 0 ? "" : $" · 손으로 고친 것 {edits}개")
                     + "   ·   0~100, 고친 것은 놀이 안에서도 그대로 쓰인다"
                     + "\n칸 간격이 셋인데 묘책은 넷이라 이웃 문화권끼리 칸을 나눠 쓴다 — "
                     + "원본 그대로다. 심판은 굴리지 않아 늘 떨어진다.";
    }

    /// <summary>고친 칸만 골라 적어 둔다 — 게임 값과 같으면 씌우지 않는다.</summary>
    private void Collect()
    {
        if (_grid.ItemsSource is not List<Row> rows) return;

        foreach (var row in rows)
        {
            int odds = Math.Clamp(row.Odds, 0, 100);
            if (odds == LandBattle.BuiltinRuseOdds(row.Index)) RuseEdits.Reset(row.Index);
            else RuseEdits.Set(row.Index, odds);
        }
        Rebuild();
    }

    /// <summary>창을 띄운다.</summary>
    public static void Show(Window? owner)
    {
        var window = new RuseEditDialog();
        if (owner != null) window.Owner = owner;
        window.ShowDialog();
    }
}
