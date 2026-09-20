using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using CdsHelper.Game.Local.Helpers;
using CdsHelper.Support.Local.Settings;

namespace CdsHelper.Game.UI.Views;

/// <summary>
/// 도시마다의 <b>문화권</b>을 고치는 창 — 헬퍼 「요소 › 문화권」.
/// </summary>
/// <remarks>
/// 「개발 › 도시 · 문화권 · 왕국」 창에서 문화권 씌우기만 떼어 낸 것이다. 그 창은 문화권이 부르는
/// 시설 화자 얼굴 · 술집 손님을 맞대어 보는 개발용이고, 이쪽은 도시 목록을 찾아 문화권만 고친다.
///
/// 고친 것은 <see cref="CityCultureEdits"/> 에 적는다 — 두 창이 같은 곳에 적으므로 어느 쪽에서
/// 고쳐도 같다. 게임 EXE 는 손대지 않는다.
/// </remarks>
public sealed class CityCultureEditDialog : GameWindow
{
    /// <summary>도시 이름이나 문화권 이름으로 거른다.</summary>
    private readonly TextBox _find = new()
    {
        Width = 220,
        Padding = new Thickness(3, 2, 3, 2),
        VerticalAlignment = VerticalAlignment.Center,
    };

    private readonly DataGrid _cities = new()
    {
        AutoGenerateColumns = false,
        IsReadOnly = true,
        HeadersVisibility = DataGridHeadersVisibility.Column,
        SelectionMode = DataGridSelectionMode.Single,
        AlternatingRowBackground = Brushes.WhiteSmoke,
        Margin = new Thickness(10, 6, 10, 4),
    };

    private readonly ComboBox _culture = new() { Width = 220, VerticalAlignment = VerticalAlignment.Center };

    private readonly Button _apply = Bar("이 도시에 씌우기");
    private readonly Button _reset = Bar("되돌리기");
    private readonly Button _resetAll = Bar("모두 되돌리기");

    private readonly TextBlock _status = new() { Margin = new Thickness(10, 4, 10, 8) };

    /// <summary>이름표 폭 — 두 줄의 칸이 같은 자리에서 시작하게 한다.</summary>
    private const double LabelWidth = 56;

    private CityTable? _names;
    private CityExeTable? _rows;

    public CityCultureEditDialog()
    {
        Title = "문화권 — 도시마다의 문화권";
        Width = 640;
        Height = 660;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        Col("번호", nameof(Row.Id), 56);
        Col("도시", nameof(Row.Name), 150);
        Col("문화권", nameof(Row.CultureNo), 60);
        Col("이름", nameof(Row.Culture), 120);
        Col("씌움", nameof(Row.Mark), 44);

        _cities.SelectionChanged += (_, _) => PickCity();
        _find.TextChanged += (_, _) => Rebuild();
        _apply.Click += (_, _) => Apply();
        _reset.Click += (_, _) => Undo();
        _resetAll.Click += (_, _) => UndoAll();

        var findRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(10, 10, 10, 0),
            Children = { Label("찾기:"), _find },
        };

        var bar = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(10, 0, 10, 0),
            Children = { Label("문화권:"), _culture, _apply, _reset, _resetAll },
        };

        var page = new DockPanel();
        DockPanel.SetDock(findRow, Dock.Top);
        DockPanel.SetDock(_status, Dock.Bottom);
        DockPanel.SetDock(bar, Dock.Bottom);
        page.Children.Add(findRow);
        page.Children.Add(_status);
        page.Children.Add(bar);
        page.Children.Add(_cities);
        Content = page;

        Loaded += (_, _) => Load();
    }

    /// <summary>임자 창 가운데에 띄운다.</summary>
    public static void Show(Window owner) => new CityCultureEditDialog { Owner = owner }.ShowDialog();

    /// <summary>목록 한 줄 — 도시와 지금 문화권.</summary>
    /// <param name="Mark">손으로 씌운 줄에만 <c>●</c> 가 선다.</param>
    private sealed record Row(int Id, string Name, int CultureNo, string Culture, string Mark);

    private static Button Bar(string text) => new()
    {
        Content = text,
        Padding = new Thickness(10, 2, 10, 2),
        Margin = new Thickness(6, 0, 0, 0),
    };

    private static TextBlock Label(string text) => new()
    {
        Text = text,
        Width = LabelWidth,
        VerticalAlignment = VerticalAlignment.Center,
    };

    private void Col(string header, string path, double width) => _cities.Columns.Add(
        new DataGridTextColumn
        {
            Header = header,
            Binding = new System.Windows.Data.Binding(path),
            Width = new DataGridLength(width),
        });

    private void Load()
    {
        string dir = Path.GetDirectoryName(AppSettings.LastSaveFilePath) ?? "";
        _names = CityTable.Open();
        _rows = CityExeTable.Open(dir);

        if (_rows == null)
        {
            _status.Text = "도시 표를 못 읽었습니다 — 세이브를 한 번 열어 게임 폴더를 알려 주세요"
                         + $" ({CityExeTable.LastError})".TrimEnd();
            _apply.IsEnabled = _reset.IsEnabled = _resetAll.IsEnabled = false;
            return;
        }

        // 콤보 자리가 곧 문화권 번호다.
        for (int i = 0; i < SpeakerFaceTable.Cultures; i++)
            _culture.Items.Add($"{i}  {CityCultureEdits.NameOf(i)}");

        Rebuild();
        if (_cities.Items.Count > 0) _cities.SelectedIndex = 0;
    }

    /// <summary>도시 목록을 다시 짓는다. 보고 있던 도시는 그대로 붙들어 둔다.</summary>
    private void Rebuild()
    {
        if (_names is not { } names || _rows is not { } table) return;

        int keep = _cities.SelectedItem is Row picked ? picked.Id : -1;
        string find = _find.Text.Trim();

        var rows = new List<Row>();
        foreach (var city in names.Cities)
        {
            int changed = CityCultureEdits.Of(city.Id);
            string culture = changed == CityCultureEdits.None ? city.Culture : CityCultureEdits.NameOf(changed);
            if (find.Length > 0
                && !city.Name.Contains(find, StringComparison.OrdinalIgnoreCase)
                && !culture.Contains(find, StringComparison.OrdinalIgnoreCase))
                continue;

            rows.Add(new Row(city.Id, city.Name, table.CultureOf(city.Id), culture,
                             changed == CityCultureEdits.None ? "" : "●"));
        }

        _cities.ItemsSource = rows;
        if (keep >= 0) _cities.SelectedItem = rows.FirstOrDefault(r => r.Id == keep);

        int edits = CityCultureEdits.All.Count;
        _status.Text = $"도시 {rows.Count}곳 · 문화권 {SpeakerFaceTable.Cultures}가지"
                     + (edits == 0 ? "" : $" · 손으로 씌운 곳 {edits}곳");
    }

    /// <summary>고른 도시의 지금 문화권으로 콤보를 맞춰 준다.</summary>
    private void PickCity()
    {
        if (_cities.SelectedItem is not Row row) return;
        if (row.CultureNo >= 0 && row.CultureNo < _culture.Items.Count)
            _culture.SelectedIndex = row.CultureNo;
    }

    /// <summary>고른 문화권을 이 도시에 씌운다.</summary>
    private void Apply()
    {
        if (_cities.SelectedItem is not Row row || _culture.SelectedIndex < 0) return;
        CityCultureEdits.Set(row.Id, _culture.SelectedIndex);
        Rebuild();
    }

    /// <summary>이 도시에 씌운 문화권을 걷는다 — 게임 표의 값으로 돌아간다.</summary>
    private void Undo()
    {
        if (_cities.SelectedItem is not Row row) return;
        CityCultureEdits.Reset(row.Id);
        Rebuild();
        PickCity();
    }

    /// <summary>씌운 문화권을 몽땅 걷는다.</summary>
    private void UndoAll()
    {
        CityCultureEdits.ResetAll();
        Rebuild();
        PickCity();
    }
}
