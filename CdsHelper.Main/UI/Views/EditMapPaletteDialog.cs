using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using CdsHelper.Support.Local.Models;
using WinFormsColorDialog = System.Windows.Forms.ColorDialog;

namespace CdsHelper.Main.UI.Views;

/// <summary>
/// 세계지도 색상 팔레트 편집 다이얼로그.
/// 각 슬롯마다 컬러 스와치(클릭=ColorDialog) + HEX 입력.
/// </summary>
public class EditMapPaletteDialog : Window
{
    private readonly List<ColorRow> _rows = new();
    public MapPalette ResultPalette { get; private set; }

    public EditMapPaletteDialog(MapPalette palette)
    {
        Title = "지도 색상 팔레트 편집";
        Width = 520;
        Height = 680;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResultPalette = CloneViaJson(palette);

        var root = new Grid { Margin = new Thickness(12) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        // 제목 / 안내
        var header = new TextBlock
        {
            Text = "색상 값을 변경한 뒤 저장을 누르면 지도가 다시 그려집니다.\n" +
                   "HEX 텍스트를 직접 입력하거나, 색 블록을 클릭해서 피커를 열 수 있습니다.",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 8),
        };
        Grid.SetRow(header, 0);
        root.Children.Add(header);

        // 행 스택
        var scroll = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
        };
        var stack = new StackPanel { Orientation = Orientation.Vertical };
        scroll.Content = stack;
        Grid.SetRow(scroll, 1);
        root.Children.Add(scroll);

        // ---- 섹션: 기본 ----
        AddSection(stack, "기본");
        AddRow(stack, "바다 기본",
            ResultPalette.SeaBase,
            v => ResultPalette.SeaBase = v);
        AddRow(stack, "해안선",
            ResultPalette.Coastline,
            v => ResultPalette.Coastline = v);
        AddRow(stack, "육지 기본 (매칭 없음)",
            ResultPalette.LandDefault,
            v => ResultPalette.LandDefault = v);
        AddRow(stack, "바람 기본 (매칭 없음)",
            ResultPalette.WindDefault,
            v => ResultPalette.WindDefault = v);

        // ---- 섹션: 육지 attr ----
        AddSection(stack, "육지 attr별");
        foreach (var key in ResultPalette.Land.Keys.OrderBy(k => int.TryParse(k, out var n) ? n : int.MaxValue).ToList())
        {
            var captured = key;
            AddRow(stack, $"attr {captured} ({LandLabel(captured)})",
                ResultPalette.Land[captured],
                v => ResultPalette.Land[captured] = v);
        }

        // ---- 섹션: 산/사막 (attr 86-116 이산 분리) ----
        AddSection(stack, "산/사막 (attr 86-116)");
        AddRow(stack, "사막",
            ResultPalette.Desert,
            v => ResultPalette.Desert = v);
        AddRow(stack, "산",
            ResultPalette.Mountain,
            v => ResultPalette.Mountain = v);
        AddIntRow(stack, "산 attr 임계값 (이상=산, 미만=사막)",
            ResultPalette.MountainAttrThreshold,
            v => ResultPalette.MountainAttrThreshold = v);

        // ---- 섹션: 바람 ----
        AddSection(stack, "바람/해류 attr별");
        foreach (var key in ResultPalette.Wind.Keys.OrderBy(k => int.TryParse(k, out var n) ? n : int.MaxValue).ToList())
        {
            var captured = key;
            AddRow(stack, $"wind {captured}",
                ResultPalette.Wind[captured],
                v => ResultPalette.Wind[captured] = v);
        }

        // 버튼 바
        var btnBar = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 10, 0, 0),
        };
        var btnReset = new Button { Content = "기본값으로", Padding = new Thickness(12, 4, 12, 4), Margin = new Thickness(0, 0, 8, 0) };
        btnReset.Click += (_, _) =>
        {
            if (MessageBox.Show(this, "모든 색상을 기본값으로 되돌립니다. 계속할까요?", "확인",
                    MessageBoxButton.OKCancel, MessageBoxImage.Question) == MessageBoxResult.OK)
            {
                ResultPalette = MapPalette.CreateDefault();
                foreach (var row in _rows) row.RefreshFrom(GetValue(row.Key));
            }
        };
        btnBar.Children.Add(btnReset);

        var btnOk = new Button { Content = "저장", Padding = new Thickness(16, 4, 16, 4), IsDefault = true, Margin = new Thickness(0, 0, 8, 0) };
        btnOk.Click += (_, _) => { DialogResult = true; Close(); };
        btnBar.Children.Add(btnOk);

        var btnCancel = new Button { Content = "취소", Padding = new Thickness(16, 4, 16, 4), IsCancel = true };
        btnBar.Children.Add(btnCancel);

        Grid.SetRow(btnBar, 2);
        root.Children.Add(btnBar);

        Content = root;
    }

    private static string LandLabel(string attrKey) => attrKey switch
    {
        "64" => "사막",
        "66" => "해안 근처",
        "67" => "중간 내륙",
        "68" => "깊은 내륙",
        _ => "",
    };

    private void AddSection(StackPanel parent, string title)
    {
        parent.Children.Add(new TextBlock
        {
            Text = title,
            FontWeight = FontWeights.Bold,
            Margin = new Thickness(0, 10, 0, 4),
        });
    }

    private void AddIntRow(StackPanel parent, string label, int initial, Action<int> setter)
    {
        var grid = new Grid { Margin = new Thickness(0, 2, 0, 2) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(240) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var tbLabel = new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(tbLabel, 0);
        grid.Children.Add(tbLabel);

        var tb = new TextBox
        {
            Width = 80,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center,
            FontFamily = new FontFamily("Consolas"),
            Text = initial.ToString(CultureInfo.InvariantCulture),
            Margin = new Thickness(8, 0, 0, 0),
        };
        tb.TextChanged += (_, _) =>
        {
            if (int.TryParse(tb.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v))
                setter(v);
        };
        Grid.SetColumn(tb, 1);
        grid.Children.Add(tb);
        parent.Children.Add(grid);
    }

    private void AddRow(StackPanel parent, string label, string initialHex, Action<string> setter)
    {
        var grid = new Grid { Margin = new Thickness(0, 2, 0, 2) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(200) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(36) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var tbLabel = new TextBlock
        {
            Text = label,
            VerticalAlignment = VerticalAlignment.Center,
        };
        Grid.SetColumn(tbLabel, 0);
        grid.Children.Add(tbLabel);

        var swatch = new Border
        {
            Width = 28,
            Height = 22,
            BorderBrush = Brushes.Gray,
            BorderThickness = new Thickness(1),
            Cursor = Cursors.Hand,
            VerticalAlignment = VerticalAlignment.Center,
        };
        Grid.SetColumn(swatch, 1);
        grid.Children.Add(swatch);

        var tbHex = new TextBox
        {
            Width = 120,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center,
            FontFamily = new FontFamily("Consolas"),
            Text = initialHex,
            Margin = new Thickness(8, 0, 0, 0),
        };
        Grid.SetColumn(tbHex, 2);
        grid.Children.Add(tbHex);

        var row = new ColorRow(label, swatch, tbHex, setter);
        _rows.Add(row);
        row.RefreshFrom(initialHex);

        tbHex.TextChanged += (_, _) =>
        {
            if (TryParseColor(tbHex.Text, out var c))
            {
                swatch.Background = new SolidColorBrush(c);
                setter(NormalizeHex(tbHex.Text));
            }
        };

        swatch.MouseLeftButtonDown += (_, _) =>
        {
            using var dlg = new WinFormsColorDialog
            {
                FullOpen = true,
                AnyColor = true,
                Color = ToWinColor(tbHex.Text),
            };
            if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                var hex = $"#{dlg.Color.R:X2}{dlg.Color.G:X2}{dlg.Color.B:X2}";
                tbHex.Text = hex; // TextChanged가 setter/swatch 갱신
            }
        };

        parent.Children.Add(grid);
    }

    private string GetValue(string key)
    {
        return key switch
        {
            "바다 기본" => ResultPalette.SeaBase,
            "해안선" => ResultPalette.Coastline,
            "육지 기본 (매칭 없음)" => ResultPalette.LandDefault,
            "바람 기본 (매칭 없음)" => ResultPalette.WindDefault,
            "사막" => ResultPalette.Desert,
            "산" => ResultPalette.Mountain,
            _ when key.StartsWith("attr ") => TryKey(ResultPalette.Land, key.Split(' ')[1]),
            _ when key.StartsWith("wind ") => TryKey(ResultPalette.Wind, key.Split(' ')[1]),
            _ => "#000000",
        };
    }

    private static string TryKey(Dictionary<string, string> dict, string k)
        => dict.TryGetValue(k, out var v) ? v : "#000000";

    private static bool TryParseColor(string hex, out Color color)
    {
        try
        {
            var obj = ColorConverter.ConvertFromString(hex.Trim());
            if (obj is Color c) { color = c; return true; }
        }
        catch { }
        color = Colors.Black;
        return false;
    }

    private static string NormalizeHex(string input)
    {
        var s = input.Trim();
        return s.StartsWith("#") ? s : "#" + s;
    }

    private static System.Drawing.Color ToWinColor(string hex)
    {
        if (TryParseColor(hex, out var c))
            return System.Drawing.Color.FromArgb(c.R, c.G, c.B);
        return System.Drawing.Color.White;
    }

    private static MapPalette CloneViaJson(MapPalette src)
    {
        var json = System.Text.Json.JsonSerializer.Serialize(src);
        return System.Text.Json.JsonSerializer.Deserialize<MapPalette>(json) ?? MapPalette.CreateDefault();
    }

    private class ColorRow
    {
        public string Key { get; }
        private readonly Border _swatch;
        private readonly TextBox _tb;
        private readonly Action<string> _setter;

        public ColorRow(string key, Border swatch, TextBox tb, Action<string> setter)
        {
            Key = key;
            _swatch = swatch;
            _tb = tb;
            _setter = setter;
        }

        public void RefreshFrom(string hex)
        {
            _tb.Text = hex;
            if (TryParseColor(hex, out var c))
                _swatch.Background = new SolidColorBrush(c);
        }
    }
}
