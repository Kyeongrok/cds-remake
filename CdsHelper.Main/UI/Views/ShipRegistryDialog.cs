using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using CdsHelper.Support.Local.Helpers;
using CdsHelper.Support.Local.Models;
using CdsHelper.Support.UI.Units;

namespace CdsHelper.Main.UI.Views;

/// <summary>
/// 조선소에 낼 배를 등록·고침·지우는 창. 개발 → 선박 으로 연다.
/// </summary>
/// <remarks>
/// 게임 EXE 는 건드리지 않는다 — 여기서 등록한 배는 이 앱이 품고 있는 놀이의 조선소
/// (구입 표)에만 나온다. 적어 두는 자리와 그림 굽기는 <see cref="ShipRegistry"/> 가 맡는다.
///
/// 그림은 방향마다 한 장씩 여덟 장이 다 차야 조선소에 나온다. 한 장이라도 비면 목록에
/// "그림 모자람" 으로 뜨고 배는 안 나온다.
/// </remarks>
public sealed class ShipRegistryDialog : Window
{
    private const double SlotSize = 76;

    private readonly ListBox _list;
    private readonly TextBlock _status;
    private readonly Panel _form;
    private readonly TextBox _name;
    private readonly NumericSpinner _hp;
    private readonly NumericSpinner _speed;
    private readonly NumericSpinner _capacity;
    private readonly NumericSpinner _tonnage;
    private readonly NumericSpinner _crew;
    private readonly NumericSpinner _guns;
    private readonly NumericSpinner _price;
    private readonly NumericSpinner _maxMasts;
    private readonly CheckBox _canChangeSail;
    private readonly WrapPanel _slots;
    private readonly Button _saveButton;
    private readonly Button _deleteButton;

    private List<ShipRegistry.Design> _designs = [];
    private ShipRegistry.Design? _current;
    private bool _filling;

    public ShipRegistryDialog()
    {
        Title = "선박 등록";
        Width = 1060;
        Height = 720;
        MinWidth = 900;
        MinHeight = 560;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        _list = new ListBox { DisplayMemberPath = null, Width = 230 };
        _list.SelectionChanged += (_, _) => OnPicked();

        _name = new TextBox { Width = 200, VerticalContentAlignment = VerticalAlignment.Center };
        _hp = Spinner(1, 999);
        _speed = Spinner(1, 999);
        _capacity = Spinner(1, 9999);
        _tonnage = Spinner(1, 99999, step: 50);
        _crew = Spinner(1, 999);
        _guns = Spinner(0, 999);
        _price = Spinner(1, 999999, step: 10);
        _maxMasts = Spinner(1, Hull.MastLimit);
        _canChangeSail = new CheckBox { Content = "돛 종류를 바꿀 수 있다", VerticalAlignment = VerticalAlignment.Center };

        _slots = new WrapPanel();
        _status = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0) };

        _saveButton = MakeButton("저장", Save);
        _deleteButton = MakeButton("지우기", Delete);
        _form = BuildForm();

        Content = BuildContent();

        _name.TextChanged += (_, _) => Touch();
        foreach (var spinner in new[] { _hp, _speed, _capacity, _tonnage, _crew, _guns, _price, _maxMasts })
            spinner.ValueChanged += (_, _) => Touch();
        _canChangeSail.Checked += (_, _) => Touch();
        _canChangeSail.Unchecked += (_, _) => Touch();

        PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) Close(); };
        Loaded += (_, _) => Reload(null);
    }

    // ── 화면 짜기 ───────────────────────────────────────────────────────────

    private UIElement BuildContent()
    {
        var listHead = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 6) };
        listHead.Children.Add(MakeButton("새 배", NewDesign));
        listHead.Children.Add(_deleteButton);

        var left = new Grid { Width = 230, Margin = new Thickness(0, 0, 14, 0) };
        left.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        left.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        Place(left, listHead, 0);
        Place(left, _list, 1);

        var right = new ScrollViewer
        {
            Content = _form,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
        };

        var body = new Grid();
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        Grid.SetColumn(left, 0);
        Grid.SetColumn(right, 1);
        body.Children.Add(left);
        body.Children.Add(right);

        var foot = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 8, 0, 0),
        };
        foot.Children.Add(MakeButton("그림 폴더 열기", OpenFolder));
        foot.Children.Add(_saveButton);
        var close = MakeButton("닫기", Close);
        close.Margin = new Thickness(0);
        foot.Children.Add(close);

        var grid = new Grid { Margin = new Thickness(12) };
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        Place(grid, body, 0);
        Place(grid, _status, 1);
        Place(grid, foot, 2);
        return grid;
    }

    private Panel BuildForm()
    {
        var form = new StackPanel { Margin = new Thickness(0, 0, 8, 0) };

        form.Children.Add(Section("이름"));
        form.Children.Add(Row(_name));
        form.Children.Add(Note("조선소 표에 이 이름으로 뜬다. 붙박이 선체와 같은 이름은 쓸 수 없다."));

        form.Children.Add(Section("스펙"));
        var specs = new WrapPanel();
        specs.Children.Add(Field("내구력", _hp));
        specs.Children.Add(Field("추진력", _speed));
        specs.Children.Add(Field("적재용량", _capacity));
        specs.Children.Add(Field("적재중량", _tonnage));
        specs.Children.Add(Field("필요승원", _crew));
        specs.Children.Add(Field("대포수", _guns));
        specs.Children.Add(Field("값(닢)", _price));
        specs.Children.Add(Field("마스트 상한", _maxMasts));
        form.Children.Add(specs);
        form.Children.Add(Row(_canChangeSail));
        form.Children.Add(Note("값은 조선소 표에 그대로 뜬다. 되사 주는 값은 그 6할이다."));

        form.Children.Add(Section("8방향 그림"));
        var pick = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 6) };
        pick.Children.Add(MakeButton("여덟 장 한꺼번에…", ImportAll, "이름순으로 북 → 북서 → 서 … 차례로 넣는다"));
        pick.Children.Add(MakeButton("모두 지우기", ClearSprites));
        form.Children.Add(pick);
        form.Children.Add(_slots);
        form.Children.Add(Note(
            $"한 장 {ShipRegistry.SpriteWidth}x{ShipRegistry.SpriteWidth} 로 맞춰 굽는다 — 큰 그림은 비율 그대로 줄여 " +
            "한가운데에 놓고 둘레는 비워 둔다. 번호는 0 북에서 반시계로 돈다. " +
            "이미지 크기 줄이기의 2행 4열 나누기로 뽑은 여덟 장을 그대로 넣으면 된다."));

        return form;
    }

    private static void Place(Grid grid, UIElement child, int row)
    {
        Grid.SetRow(child, row);
        grid.Children.Add(child);
    }

    private static TextBlock Section(string text) => new()
    {
        Text = text,
        FontWeight = FontWeights.SemiBold,
        Margin = new Thickness(0, 14, 0, 4),
    };

    private static TextBlock Note(string text) => new()
    {
        Text = text,
        Foreground = Brushes.Gray,
        TextWrapping = TextWrapping.Wrap,
        Margin = new Thickness(0, 4, 0, 0),
    };

    private static StackPanel Row(params UIElement[] children)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 0) };
        foreach (var child in children) row.Children.Add(child);
        return row;
    }

    private static UIElement Field(string label, UIElement input)
    {
        var stack = new StackPanel { Margin = new Thickness(0, 0, 16, 8), Width = 108 };
        stack.Children.Add(new TextBlock { Text = label, Margin = new Thickness(0, 0, 0, 2) });
        stack.Children.Add(input);
        return stack;
    }

    private static NumericSpinner Spinner(double min, double max, double step = 1) => new()
    {
        Minimum = min,
        Maximum = max,
        Step = step,
        DecimalPlaces = 0,
        Value = min,
        Width = 104,
    };

    private Button MakeButton(string text, Action run, string? tip = null)
    {
        var button = new Button
        {
            Content = text,
            Padding = new Thickness(12, 4, 12, 4),
            Margin = new Thickness(0, 0, 8, 0),
            ToolTip = tip,
        };
        button.Click += (_, _) => run();
        return button;
    }

    // ── 목록 ────────────────────────────────────────────────────────────────

    /// <summary>적어 둔 배를 다시 읽어 목록을 채운다.</summary>
    private void Reload(string? selectId)
    {
        var saved = ShipRegistry.Load();

        // 붙박이 다섯을 먼저 세운다 — 고쳐 둔 줄이 있으면 그 값으로 뜬다.
        _designs = [];
        foreach (var built in Hull.Builtin)
            _designs.Add(saved.FirstOrDefault(d => d.Builtin == built.Name)
                         ?? ShipRegistry.FromBuiltin(built));

        _designs.AddRange(saved.Where(d => !d.IsBuiltin));
        _list.Items.Clear();

        foreach (var design in _designs)
        {
            if (design.IsBuiltin)
            {
                bool edited = saved.Any(d => d.Builtin == design.Builtin);
                _list.Items.Add(new ListBoxItem
                {
                    Content = edited ? $"{design.Name}  (붙박이 · 고침)" : $"{design.Name}  (붙박이)",
                    Tag = design.Id,
                    Foreground = Brushes.DimGray,
                });
                continue;
            }

            int have = Enumerable.Range(0, ShipRegistry.Directions)
                .Count(i => File.Exists(ShipRegistry.SpritePath(design.Id, i)));

            _list.Items.Add(new ListBoxItem
            {
                Content = have == ShipRegistry.Directions
                    ? design.Name
                    : $"{design.Name}  (그림 {have}/{ShipRegistry.Directions})",
                Tag = design.Id,
                Foreground = have == ShipRegistry.Directions ? Brushes.Black : Brushes.Firebrick,
            });
        }

        int at = selectId == null ? 0 : Math.Max(0, _designs.FindIndex(d => d.Id == selectId));
        _list.SelectedIndex = at;
    }

    private void OnPicked()
    {
        if (_list.SelectedItem is not ListBoxItem { Tag: string id }) return;

        Fill(_designs.FirstOrDefault(d => d.Id == id));
    }

    /// <summary>고른 배를 폼에 옮겨 담는다.</summary>
    private void Fill(ShipRegistry.Design? design)
    {
        _current = design;
        _filling = true;
        try
        {
            _form.IsEnabled = design != null;
            _deleteButton.IsEnabled = design != null;
            _saveButton.IsEnabled = design != null;

            // 붙박이는 그림을 안 갖는다 — 제 그림벌을 그대로 쓴다. 지우기는
            // 고친 것을 걷는 것이라 이름을 바꿔 단다.
            bool builtin = design?.IsBuiltin == true;
            _deleteButton.Content = builtin ? "되돌리기" : "지우기";

            if (design == null)
            {
                _name.Text = "";
                _slots.Children.Clear();
                return;
            }

            _name.Text = design.Name;
            _hp.Value = design.Hp;
            _speed.Value = design.Speed;
            _capacity.Value = design.Capacity;
            _tonnage.Value = design.Tonnage;
            _crew.Value = design.Crew;
            _guns.Value = design.Guns;
            _price.Value = design.Price;
            _maxMasts.Value = Math.Clamp(design.MaxMasts, 1, Hull.MastLimit);
            _canChangeSail.IsChecked = design.CanChangeSail;
        }
        finally
        {
            _filling = false;
        }

        FillSlots();
        _status.Text = ShipRegistry.HasAllSprites(design!.Id)
            ? "그림이 다 찼습니다 — 조선소 구입 표에 나옵니다."
            : "그림이 여덟 장 다 차야 조선소에 나옵니다.";
    }

    private void Touch()
    {
        if (_filling || _current == null) return;

        _status.Text = "고친 내용이 있습니다 — 저장을 눌러 주세요.";
    }

    // ── 8방향 그림 ──────────────────────────────────────────────────────────

    private void FillSlots()
    {
        _slots.Children.Clear();
        if (_current is not { } design) return;

        // 붙박이는 제 그림벌(asset/ship-g*)을 그대로 쓴다 — 갈아 끼울 것이 없어 보여만 준다.
        int skin = design.IsBuiltin
            ? Hull.Builtin.FirstOrDefault(h => h.Name == design.Builtin)?.Skin ?? 0
            : -1;

        for (int i = 0; i < ShipRegistry.Directions; i++)
            _slots.Children.Add(MakeSlot(design.Id, i, skin));
    }

    private UIElement MakeSlot(string id, int direction, int builtinSkin)
    {
        var image = builtinSkin >= 0
            ? ShipRegistry.ReadBuiltinSprite(builtinSkin, direction)
            : ShipRegistry.ReadSprite(id, direction);

        var frame = new Border
        {
            Width = SlotSize,
            Height = SlotSize,
            Background = Checker,
            BorderBrush = image == null ? Brushes.Firebrick : Brushes.LightGray,
            BorderThickness = new Thickness(1),
            Child = image == null
                ? new TextBlock
                {
                    Text = "비었음",
                    FontSize = 10,
                    Foreground = Brushes.Firebrick,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                }
                // 48x48 을 76 칸에 늘려 보여 준다 — 픽셀이 뭉개지지 않게 이웃 색으로 늘린다.
                : new Image
                {
                    Source = image,
                    Stretch = Stretch.Uniform,
                    Margin = new Thickness(2),
                    RenderTransformOrigin = new Point(0.5, 0.5),
                },
        };

        if (image != null) RenderOptions.SetBitmapScalingMode(frame.Child, BitmapScalingMode.NearestNeighbor);

        var stack = new StackPanel { Margin = new Thickness(0, 0, 8, 8) };
        stack.Children.Add(frame);
        stack.Children.Add(new TextBlock
        {
            Text = $"{direction}  {ShipRegistry.DirectionNames[direction]}",
            FontSize = 11,
            Foreground = Brushes.DimGray,
            TextAlignment = TextAlignment.Center,
            Width = SlotSize,
            Margin = new Thickness(0, 2, 0, 0),
        });

        var button = new Button
        {
            Content = stack,
            IsEnabled = builtinSkin < 0,
            ToolTip = builtinSkin >= 0
                ? "붙박이 선체는 게임 그림벌을 그대로 씁니다"
                : $"{ShipRegistry.DirectionNames[direction]} 쪽 그림 고르기",
            Padding = new Thickness(0),
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Cursor = Cursors.Hand,
        };
        button.Click += (_, _) => ImportOne(direction);
        return button;
    }

    private void ImportOne(int direction)
    {
        if (_current is not { } design) return;

        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title = $"{ShipRegistry.DirectionNames[direction]} 쪽 그림 고르기",
            Filter = ImageShrinker.FileFilter,
        };
        if (dlg.ShowDialog(this) != true) return;

        try
        {
            ShipRegistry.ImportSprite(design.Id, direction, dlg.FileName);
            FillSlots();
            _status.Text = $"{ShipRegistry.DirectionNames[direction]} 쪽 그림을 넣었습니다.";
        }
        catch (Exception ex)
        {
            _status.Text = $"그림을 넣지 못했습니다 — {ex.Message}";
        }
    }

    /// <summary>여덟 장을 한꺼번에 — 이름순으로 0번부터 채운다.</summary>
    private void ImportAll()
    {
        if (_current is not { } design) return;

        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title = "8방향 그림 여덟 장 고르기 (이름순으로 북 → 북서 → 서 …)",
            Filter = ImageShrinker.FileFilter,
            Multiselect = true,
        };
        if (dlg.ShowDialog(this) != true) return;

        var files = dlg.FileNames.OrderBy(f => f, StringComparer.OrdinalIgnoreCase).ToList();
        if (files.Count != ShipRegistry.Directions)
        {
            _status.Text = $"여덟 장이어야 합니다 — {files.Count}장을 고르셨습니다.";
            return;
        }

        int done = 0;
        try
        {
            for (; done < files.Count; done++)
                ShipRegistry.ImportSprite(design.Id, done, files[done]);

            _status.Text = "여덟 장을 다 넣었습니다.";
        }
        catch (Exception ex)
        {
            _status.Text = $"{done}장까지 넣고 멈췄습니다 — {ex.Message}";
        }

        FillSlots();
    }

    private void ClearSprites()
    {
        if (_current is not { } design) return;

        for (int i = 0; i < ShipRegistry.Directions; i++)
        {
            try { File.Delete(ShipRegistry.SpritePath(design.Id, i)); } catch { /* 없으면 그만 */ }
        }

        Hull.Reload();
        FillSlots();
        _status.Text = "그림을 다 지웠습니다 — 이 배는 조선소에 안 나옵니다.";
    }

    // ── 만들기·저장·지우기 ──────────────────────────────────────────────────

    private void NewDesign()
    {
        var design = new ShipRegistry.Design
        {
            Id = ShipRegistry.NewId(),
            Name = NextName(),
        };

        _designs.Add(design);
        ShipRegistry.Save(_designs);
        Reload(design.Id);
        _status.Text = "새 배를 만들었습니다 — 이름과 스펙을 적고 그림 여덟 장을 넣어 주세요.";
    }

    /// <summary>안 쓴 이름 하나. 붙박이 선체와도 안 겹치게 고른다.</summary>
    private string NextName()
    {
        var taken = Hull.Builtin.Select(h => h.Name)
            .Concat(_designs.Select(d => d.Name))
            .ToHashSet();

        for (int n = 1; ; n++)
        {
            string name = $"새 배 {n}";
            if (taken.Add(name)) return name;
        }
    }

    private void Save()
    {
        if (_current is not { } design) return;

        string name = _name.Text.Trim();
        if (name.Length == 0)
        {
            _status.Text = "이름을 적어 주세요.";
            return;
        }

        if (!design.IsBuiltin && Hull.Builtin.Any(h => h.Name == name))
        {
            _status.Text = $"'{name}' 은 붙박이 선체 이름입니다 — 다른 이름을 적어 주세요.";
            return;
        }

        if (_designs.Any(d => d.Id != design.Id && d.Name == name))
        {
            _status.Text = $"'{name}' 은 이미 등록한 배 이름입니다.";
            return;
        }

        design.Name = name;
        design.Hp = (int)_hp.Value;
        design.Speed = (int)_speed.Value;
        design.Capacity = (int)_capacity.Value;
        design.Tonnage = (int)_tonnage.Value;
        design.Crew = (int)_crew.Value;
        design.Guns = (int)_guns.Value;
        design.Price = (int)_price.Value;
        design.MaxMasts = (int)_maxMasts.Value;
        design.CanChangeSail = _canChangeSail.IsChecked == true;

        // 적어 두는 것은 <b>고친 줄만</b>이다 — 안 건드린 붙박이는 적지 않는다.
        var keep = ShipRegistry.Load().Where(d => d.Id != design.Id).ToList();
        keep.Add(design);
        ShipRegistry.Save(keep);
        Hull.Reload();
        Reload(design.Id);

        _status.Text = design.IsBuiltin
            ? $"저장했습니다 — 붙박이 '{design.Name}' 의 값이 놀이에 그대로 쓰입니다."
            : ShipRegistry.HasAllSprites(design.Id)
                ? $"저장했습니다 — '{design.Name}' 이 조선소 구입 표에 나옵니다."
                : "저장했습니다. 그림이 여덟 장 다 차야 조선소에 나옵니다.";
    }

    private void Delete()
    {
        if (_current is not { } design) return;

        // 붙박이는 지우는 것이 아니라 <b>고친 것을 걷는 것</b>이다.
        if (design.IsBuiltin)
        {
            var kept = ShipRegistry.Load().Where(d => d.Builtin != design.Builtin).ToList();
            ShipRegistry.Save(kept);
            Hull.Reload();
            Reload(design.Id);
            _status.Text = $"'{design.Builtin}' 을 게임 값으로 되돌렸습니다.";
            return;
        }

        if (MessageBox.Show(
                this,
                $"'{design.Name}' 을 그림째 지웁니다. 되돌릴 수 없습니다. 그대로 할까요?",
                "배 지우기",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning,
                MessageBoxResult.No) != MessageBoxResult.Yes)
        {
            return;
        }

        ShipRegistry.Delete(design.Id);
        Hull.Reload();
        Reload(null);
        _status.Text = $"'{design.Name}' 을 지웠습니다.";
    }

    private void OpenFolder()
    {
        string folder = _current == null ? ShipRegistry.Root : ShipRegistry.FolderOf(_current.Id);
        try
        {
            Directory.CreateDirectory(folder);
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = folder,
                UseShellExecute = true,
            });
        }
        catch (Exception ex)
        {
            _status.Text = $"폴더를 열지 못했습니다 — {ex.Message}";
        }
    }

    /// <summary>비침이 드러나도록 깔아 두는 바둑판.</summary>
    private static readonly Brush Checker = MakeChecker();

    private static Brush MakeChecker()
    {
        var cells = new GeometryGroup();
        cells.Children.Add(new RectangleGeometry(new Rect(0, 0, 8, 8)));
        cells.Children.Add(new RectangleGeometry(new Rect(8, 8, 8, 8)));

        var drawing = new DrawingGroup();
        drawing.Children.Add(new GeometryDrawing(Brushes.White, null, new RectangleGeometry(new Rect(0, 0, 16, 16))));
        drawing.Children.Add(new GeometryDrawing(new SolidColorBrush(Color.FromRgb(0xE4, 0xE4, 0xE4)), null, cells));

        var brush = new DrawingBrush(drawing)
        {
            TileMode = TileMode.Tile,
            Viewport = new Rect(0, 0, 16, 16),
            ViewportUnits = BrushMappingMode.Absolute,
            Stretch = Stretch.None,
        };
        brush.Freeze();
        return brush;
    }
}
