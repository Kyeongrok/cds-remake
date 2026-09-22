using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CdsHelper.Game.Local.Helpers;
using CdsHelper.Support.Local.Helpers;
using CdsHelper.Support.Local.Settings;
using Microsoft.Win32;

using CdsHelper.Game.Engine.Disev;

namespace CdsHelper.Game.UI.Views;

/// <summary>
/// 발견 이벤트 편집기 — <c>DISEV.CDS</c> 의 파트를 발견물 이름으로 찾아 보고 고친다.
/// </summary>
/// <remarks>
/// 파트 하나가 발견물 하나고, 파트 안은 「조건 · 본문」 덩이 여럿이다
/// (<see cref="DisevPart"/>). 고치는 길은 <b>명령 하나를 칸으로</b>다 — 뜻이 확실한 명령만 칸을 준다
/// (<see cref="DisevForm"/>). 칸 밖의 바이트는 손대지 않는다.
/// 길이를 바꿔도 된다 — <see cref="DisevPart.Rebuild"/> 가 슬롯 표의 오프셋을 다시
/// 잡아 준다. 다만 <b>덩이 경계를 넘어 뛰는 상대 이동</b>은 못 고쳐 주므로,
/// 그런 명령이 있으면 창이 미리 일러 준다.
///
/// 저장은 <c>발견이벤트.json</c> 에만 한다 — 원본 <c>DISEV.CDS</c> 는 건드리지 않는다.
/// </remarks>
public sealed class DisevEditorDialog : GameWindow
{
    private readonly ListBox _discoveries = new() { Margin = new Thickness(0, 4, 0, 0) };

    /// <summary>발견물 이름으로 걸러 낸다. 글자를 칠 때마다 목록이 줄어든다.</summary>
    private readonly TextBox _find = new() { Padding = new Thickness(3, 2, 3, 2) };

    /// <summary>갈래로 걸러 낸다. 첫 줄이 「모두」다.</summary>
    private readonly ComboBox _category = new() { Margin = new Thickness(0, 4, 0, 0) };

    /// <summary>
    /// 어느 <b>대본 책</b>을 고칠지 — 발견 이벤트와 미리 만든 주인공 둘의 이야기다.
    /// </summary>
    /// <remarks>
    /// <c>STORY0/1.CDS</c> 는 그릇도 말도 <c>DISEV.CDS</c> 와 같아서(<see cref="DisevBook.Books"/>)
    /// 이 창이 그대로 읽는다. 다만 파트 번호가 발견물 번호가 아니라 <b>마당 안의 장면</b>이라
    /// 목록에 이름 대신 번호만 붙는다.
    /// </remarks>
    private readonly ComboBox _book_ = new() { Margin = new Thickness(0, 4, 0, 0) };

    /// <summary>걸러 내고 몇 개가 남았는지.</summary>
    private readonly TextBlock _found = new()
    {
        Margin = new Thickness(2, 4, 0, 0),
        Foreground = System.Windows.Media.Brushes.Gray,
        FontSize = 11,
    };
    private readonly ListBox _chunks = new() { Height = 92, Margin = new Thickness(4, 6, 10, 4) };

    private readonly DataGrid _ops = new()
    {
        AutoGenerateColumns = false,
        CanUserAddRows = false,
        CanUserDeleteRows = false,
        IsReadOnly = true,
        HeadersVisibility = DataGridHeadersVisibility.Column,
        SelectionMode = DataGridSelectionMode.Single,
        FontFamily = new FontFamily("Consolas, D2Coding, 맑은 고딕"),
        Margin = new Thickness(4, 0, 10, 4),
    };

    /// <summary>덩이 흐름도(<see cref="DisevFlowView"/>)가 들어앉는 자리.</summary>
    private readonly ScrollViewer _flow = new()
    {
        HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
    };

    /// <summary>
    /// 고른 덩이가 <c>발견이벤트.json</c> 에 적히는 모양 — 분기를 Yes/No 로 가른 줄 나무(<see cref="DisevTree"/>).
    /// 저장 전이라도 지금 고친 바이트로 짠다.
    /// </summary>
    private readonly TextBox _json = new()
    {
        IsReadOnly = true,
        AcceptsReturn = true,
        TextWrapping = TextWrapping.Wrap,
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        FontFamily = new FontFamily("Consolas, D2Coding, 맑은 고딕"),
        BorderThickness = new Thickness(0),
    };

    /// <summary>「JSON」·「표」·「흐름도」 세 보기. 표와 흐름도는 어느 쪽에서 골라도 아래 칸이 같은 명령을 잡는다.</summary>
    private readonly TabControl _views = new() { Margin = new Thickness(4, 0, 10, 4) };

    /// <summary>고른 명령의 칸들이 들어앉는 자리.</summary>
    private readonly WrapPanel _form = new() { Margin = new Thickness(4, 2, 10, 2) };

    private readonly Button _applyOp = Bar("명령 적용");
    private readonly Button _wide = Bar("전각으로");

    /// <summary>더할 명령 갈래 — 지금은 동영상 재생 · DSTILL 그림 둘이다(<see cref="AddKinds"/>).</summary>
    private readonly ComboBox _addKind = new() { Width = 150, Margin = new Thickness(6, 0, 4, 0), VerticalContentAlignment = VerticalAlignment.Center };

    /// <summary>더할 명령의 값 — 갈래에 따라 동영상 번호 · DSTILL 그림 번호를 고른다.</summary>
    private readonly ComboBox _addValue = new() { Width = 260, Margin = new Thickness(0, 0, 4, 0), VerticalContentAlignment = VerticalAlignment.Center };
    private readonly Button _addAbove = Bar("위로 추가");
    private readonly Button _addBelow = Bar("아래로 추가");
    private readonly Button _removeOp = Bar("명령 빼기");
    private readonly TextBlock _addTarget = new() { VerticalAlignment = VerticalAlignment.Center, Foreground = Brushes.Gray, Margin = new Thickness(8, 0, 0, 0) };

    /// <summary>순서도에서 더할 수 있는 명령 갈래.</summary>
    private static readonly (DisevCall Call, string Text)[] AddKinds =
    [
        (DisevCall.PlayVideo, "동영상 재생"),
        (DisevCall.ShowDStill, "DSTILL 그림 표시"),
    ];

    private readonly Button _open = Bar("게임 폴더 고르기");
    private readonly Button _revert = Bar("이 발견물만 원본으로");
    private readonly Button _revertAll = Bar("원본에서 다시 뜨기");
    private readonly Button _save = Bar("저장");
    private readonly TextBlock _status = new() { Margin = new Thickness(10, 4, 10, 8), TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _header = new() { Margin = new Thickness(4, 6, 10, 2) };

    private DisevBook? _book;

    /// <summary>게임 폴더 — 이름표(CDS_95.EXE) · EVSTILL 그림 · 소리를 읽을 때 쓴다. 대본은 여기서 안 읽는다.</summary>
    private string _gameDir = "";
    private DiscoveryTable? _names;
    private ItemTable? _items;
    private CityTable? _cities;
    private DisevPart? _part;

    /// <summary>지금 칸에 걸린 명령과 그 칸들.</summary>
    private DisevScript.Op? _op;
    private DisevForm.Field[] _fields = [];
    /// <summary>칸마다 지금 적힌 값을 읽는 것. 숫자가 아니면 null. 칸은 글상자이거나 고르는 상자다.</summary>
    private readonly List<Func<long?>> _readers = [];
    private TextBox? _flagBox, _speakerBox, _textBox;

    public DisevEditorDialog()
    {
        Title = "대본 편집기 — 발견 이벤트 · 이야기";
        Width = 1280;
        Height = 860;
        MinWidth = 900;
        MinHeight = 640;
        // 바탕을 못 박는다 — 안 주면 창 테마에 딸려 글씨가 안 보이는 자리가 생긴다.
        Background = System.Windows.Media.Brushes.White;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        Col("자리", nameof(OpRow.At), 60);
        Col("바이트", nameof(OpRow.Hex), 300);
        Col("풀이", nameof(OpRow.Text), 0);      // 남는 자리를 다 먹는다

        _open.Click += (_, _) => Pick();
        _applyOp.Click += (_, _) => ApplyOp();
        _addAbove.Click += (_, _) => EditLines(below: false, remove: false);
        _addBelow.Click += (_, _) => EditLines(below: true, remove: false);
        _removeOp.Click += (_, _) => EditLines(below: false, remove: true);
        foreach (var (_, text) in AddKinds) _addKind.Items.Add(text);
        _addKind.SelectedIndex = 0;
        _addKind.SelectionChanged += (_, _) => FillAddValues();
        // 펼칠 때마다 다시 채운다 — 창을 띄운 채로 에셋-동영상에서 새로 올려도 바로 보인다.
        _addValue.DropDownOpened += (_, _) => FillAddValues();
        _wide.Click += (_, _) => { if (_textBox != null) _textBox.Text = DisevForm.ToWide(_textBox.Text); };
        _revert.Click += (_, _) => RevertOne();
        _revertAll.Click += (_, _) => RevertAll();
        _save.Click += (_, _) => Save();

        _discoveries.SelectionChanged += (_, _) => ShowPart();

        // 글자를 칠 때마다·갈래를 고를 때마다 목록을 다시 짠다.
        _find.TextChanged += (_, _) => RefreshDiscoveries();
        _category.SelectionChanged += (_, _) => RefreshDiscoveries();
        _book_.SelectionChanged += (_, _) => { if (_gameDir != null) Load(_gameDir); };
        _chunks.SelectionChanged += (_, _) => ShowChunk();
        _ops.SelectionChanged += (_, _) => { BuildForm(); ShowAddTarget(); };

        var bar = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(10, 8, 10, 0),
            Children = { _open, _revert, _revertAll, _save },
        };

        var formBar = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(4, 0, 10, 4),
            Children = { _applyOp, _wide },
        };

        // 순서도·표에서 명령 하나를 고르면 그 위나 아래에 새 명령을 넣는다.
        var addBar = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(4, 0, 10, 6),
            Children =
            {
                new TextBlock { Text = "명령 추가", VerticalAlignment = VerticalAlignment.Center },
                _addKind, _addValue, _addAbove, _addBelow, _removeOp, _addTarget,
            },
        };

        var formHost = new Border
        {
            BorderBrush = new SolidColorBrush(Color.FromRgb(0xE0, 0xE0, 0xE0)),
            BorderThickness = new Thickness(0, 1, 0, 1),
            Margin = new Thickness(0, 2, 0, 2),
            Child = new StackPanel { Children = { _form, formBar, addBar } },
        };

        var right = new DockPanel();
        DockPanel.SetDock(_header, Dock.Top);
        DockPanel.SetDock(_chunks, Dock.Top);
        DockPanel.SetDock(formHost, Dock.Bottom);
        right.Children.Add(_header);
        right.Children.Add(_chunks);
        right.Children.Add(formHost);
        _ops.Margin = new Thickness(0);
        // JSON 을 맨 앞에 둔다 — 창이 뜨면 그것부터 보인다.
        _views.Items.Add(new TabItem { Header = "JSON", Content = _json });
        _views.Items.Add(new TabItem { Header = "표", Content = _ops });
        _views.Items.Add(new TabItem { Header = "흐름도", Content = _flow });
        right.Children.Add(_views);

        // 왼쪽 기둥 — 찾기 칸과 갈래 칸을 목록 위에 얹는다.
        _category.Items.Add(AllCategories);
        foreach (string name in DiscoveryTable.CategoryNames) _category.Items.Add(name);
        _category.SelectedIndex = 0;

        foreach (var (_, title, _) in DisevBook.Books) _book_.Items.Add(title);
        _book_.SelectedIndex = 0;

        var picker = new DockPanel { Width = 240, Margin = new Thickness(10, 6, 4, 6) };
        DockPanel.SetDock(_find, Dock.Top);
        DockPanel.SetDock(_book_, Dock.Top);
        DockPanel.SetDock(_category, Dock.Top);
        DockPanel.SetDock(_found, Dock.Top);
        picker.Children.Add(_find);
        picker.Children.Add(_book_);
        picker.Children.Add(_category);
        picker.Children.Add(_found);
        picker.Children.Add(_discoveries);

        var body = new DockPanel();
        DockPanel.SetDock(picker, Dock.Left);
        body.Children.Add(picker);
        body.Children.Add(right);

        var page = new DockPanel();
        DockPanel.SetDock(bar, Dock.Top);
        DockPanel.SetDock(_status, Dock.Bottom);
        page.Children.Add(bar);
        page.Children.Add(_status);
        page.Children.Add(body);
        Content = page;

        Loaded += (_, _) => OpenDefault();
        Closed += (_, _) => _bgm.Dispose();
    }

    /// <summary>임자 창 가운데에 띄운다.</summary>
    /// <param name="book">처음에 펼 책(<see cref="DisevBook.Books"/> 의 첫 칸, 「PEX」 따위). 없으면 발견 이벤트다.</param>
    public static void Show(Window owner, string? book = null)
    {
        var dialog = new DisevEditorDialog { Owner = owner };
        int at = Array.FindIndex(DisevBook.Books, b => b.Cache == book);
        if (at >= 0) dialog._book_.SelectedIndex = at;
        dialog.ShowDialog();
    }

    private static Button Bar(string text) => new()
    {
        Content = text,
        Padding = new Thickness(10, 3, 10, 3),
        Margin = new Thickness(0, 0, 6, 0),
    };

    /// <summary>
    /// 명령 목록의 칸 하나. <paramref name="width"/> 가 0 이면 <b>남는 자리를 다 먹는다</b>.
    /// </summary>
    /// <remarks>
    /// 마지막 「풀이」 칸을 못 박아 두었더니 세 칸을 더한 폭이 창보다 넓어 오른쪽이
    /// 잘려 나갔다. 남는 만큼 늘어나게 두어야 창 크기와 상관없이 다 보인다.
    /// </remarks>
    private void Col(string header, string path, double width) =>
        _ops.Columns.Add(new DataGridTextColumn
        {
            Header = header,
            Binding = new Binding(path),
            Width = width > 0 ? new DataGridLength(width) : new DataGridLength(1, DataGridLengthUnitType.Star),
        });

    /// <summary>명령 목록 한 줄.</summary>
    private sealed class OpRow
    {
        public string At { get; init; } = "";
        public string Hex { get; init; } = "";
        public string Text { get; init; } = "";
        public DisevScript.Op Op { get; init; }
    }

    /// <summary>덩이 목록 한 줄.</summary>
    private sealed class ChunkRow
    {
        public int Start { get; init; }
        public string Text { get; init; } = "";
        public override string ToString() => Text;
    }

    /// <summary>발견물 목록 한 줄.</summary>
    private sealed class PartRow
    {
        public int Index { get; init; }
        public string Text { get; init; } = "";
        public override string ToString() => Text;
    }

    /// <summary>
    /// 창이 뜨면 곧장 연다 — 적어 둔 <c>발견이벤트.json</c> 이 있으면 그것을, 없으면
    /// 앱에 실린 원본을 적고 그것을. 이름표 · 그림 · 소리는 세이브를 연 폴더에서 읽는다.
    /// </summary>
    private void OpenDefault() => Load(GameFolder());

    /// <summary>게임 폴더를 고른다 — 이름표 · 그림 · 소리를 딴 폴더에서 읽을 때 쓴다. 대본은 그대로다.</summary>
    private void Pick()
    {
        var dialog = new OpenFolderDialog
        {
            Title = "게임 폴더 고르기 (CDS_95.EXE 가 있는 곳)",
            InitialDirectory = GameFolder(),
        };
        if (dialog.ShowDialog(this) == true) Load(dialog.FolderName);
    }

    /// <summary>세이브를 연 폴더가 곧 게임 폴더다 — 앱의 다른 데도 그렇게 잡는다.</summary>
    private static string GameFolder() =>
        Path.GetDirectoryName(AppSettings.LastSaveFilePath) ?? "";

    /// <summary>
    /// 대본 책을 연다 — <b>읽는 것은 <c>발견이벤트.json</c></b> 이다.
    /// </summary>
    /// <remarks>
    /// 그 파일이 없으면 <see cref="DisevBook"/> 이 앱에 실린 원본 대본을 <b>먼저 적어 두고</b>
    /// 그것을 읽는다. <c>DISEV.CDS</c> 는 안 읽는다.
    /// </remarks>
    /// <param name="dir">게임 폴더 — 이름표 · 그림 · 소리를 읽는다.</param>
    /// <param name="fresh">참이면 적어 둔 것을 버리고 실린 원본으로 다시 편다.</param>
    private void Load(string dir, bool fresh = false)
    {
        _gameDir = dir;
        _eventStills = null;
        _stills = null;
        _itemDescriptions = null;
        _itemArt = null;
        string cache = DisevBook.Books[Math.Clamp(_book_.SelectedIndex, 0, DisevBook.Books.Length - 1)].Cache;
        _book = fresh ? DisevBook.Reset(cache) : DisevBook.Open(cache);

        if (_book == null)
        {
            _status.Text = $"열지 못했습니다 — {DisevBook.LastError}";
            _discoveries.ItemsSource = null;
            _chunks.ItemsSource = null;
            _ops.ItemsSource = null;
            return;
        }

        // 이름표는 게임 폴더의 EXE 에서 온다. 없어도 번호로는 다룰 수 있다.
        _names = DiscoveryTable.Open(dir);
        _items = ItemTable.Open(dir);
        _cities = CityTable.Open();

        RefreshDiscoveries();
        FillAddValues();
        ShowAddTarget();
        if (_discoveries.Items.Count > 0) _discoveries.SelectedIndex = 0;

        string missing = _names == null ? "  (CDS_95.EXE 를 못 읽어 이름 없이 번호로만 보입니다)" : "";
        _status.Text = $"{DisevBook.PathOf(_book.Cache)} — 파트 {_book.Count}개{missing}";
    }

    /// <summary>갈래 칸의 첫 줄 — 거르지 않는다는 뜻이다.</summary>
    private const string AllCategories = "갈래 모두";

    /// <summary>
    /// 목록을 다시 짠다. <see cref="_find"/> 의 글자와 <see cref="_category"/> 의 갈래로 거른다.
    /// </summary>
    /// <remarks>
    /// <b>고른 줄은 목록 자리가 아니라 발견물 번호로 붙든다.</b> 거르고 나면 자리가 통째로
    /// 밀리므로 자리로 붙들면 엉뚱한 발견물이 뜬다.
    ///
    /// 번호로도 찾게 해 두었다 — "137" 을 치면 137번이 걸린다. 이름을 모르는 채 자리만
    /// 아는 일이 잦다.
    /// </remarks>
    private void RefreshDiscoveries()
    {
        if (_book == null) return;
        int keep = SelectedPart;

        string find = _find.Text.Trim();
        string pick = _category.SelectedItem as string ?? AllCategories;

        var rows = new List<PartRow>(_book.Count);
        for (int i = 0; i < _book.Count; i++)
        {
            // 이야기 책은 파트 번호가 발견물 번호가 아니다 — 장면 조건과 첫 대사로 이름표를 짓는다.
            var record = Discoveries ? _names?.Find(i) : null;
            string name = record?.Name ?? (Discoveries ? $"발견물 {i}" : $"장면 {i}  {SceneLabel(i)}");
            string category = record?.CategoryName ?? "";

            if (pick != AllCategories && category != pick) continue;
            if (find.Length > 0
                && !name.Contains(find, StringComparison.OrdinalIgnoreCase)
                && !$"{i:000}".Contains(find)
                && i.ToString() != find) continue;

            string tail = category.Length > 0 ? $" · {category}" : "";
            string mark = _book.IsEdited(i) ? " ●" : "";
            rows.Add(new PartRow { Index = i, Text = $"{i:000}  {name}{tail}{mark}" });
        }

        _discoveries.ItemsSource = rows;

        // 붙들던 줄이 걸러져 나갔으면 첫 줄로 옮긴다 — 빈 채로 두면 오른쪽이 통째로 빈다.
        _discoveries.SelectedItem = rows.FirstOrDefault(r => r.Index == keep) ?? rows.FirstOrDefault();
        _found.Text = rows.Count == _book.Count
            ? $"{rows.Count}개"
            : $"{rows.Count}개 보임 / 모두 {_book.Count}개";
    }

    /// <summary>
    /// 이야기 책의 장면 이름표 — 슬롯 조건을 짧게 늘어놓고 「」 안에 본문 첫 대사를 붙인다.
    /// 「1500년~ · 명성 ≥ 3000 · 함대 있음 · 계약 없음 「제독, 편지가 왔습니다」」 꼴이다.
    /// </summary>
    /// <remarks>
    /// 개인 이야기(PEX 따위)는 파트 번호가 곧 장면 차례라 이름이 없다 — 어느 장면이 어디서 도는지 조건을
    /// 안 풀면 알 길이 없어 붙였다. 못 읽는 조건은 명령 이름 그대로 낸다.
    /// </remarks>
    private string SceneLabel(int index)
    {
        if (_book == null || DisevPart.Parse(_book.Part(index), out _) is not { } part) return "";

        var terms = new List<string>();
        string? firstLine = null;
        foreach (var slot in part.Slots)
        {
            foreach (var (call, args) in Calls(part, slot.Condition))
                if (Term(call, args) is { Length: > 0 } term && !terms.Contains(term)) terms.Add(term);
            if (firstLine == null)
                foreach (var (call, args) in Calls(part, slot.Body))
                    if (call is DisevCall.Say or DisevCall.SayBare && args["Text"]?.ToString() is { Length: > 0 } text)
                    {
                        firstLine = text.Trim();
                        break;
                    }
        }

        string head = string.Join(" · ", terms);
        if (firstLine is { } line)
        {
            if (line.Length > 22) line = line[..22] + "…";
            head = head.Length > 0 ? $"{head} 「{line}」" : $"「{line}」";
        }
        return head;
    }

    /// <summary>그 덩이의 명령을 호출로 풀어 낸다 — 못 푸는 줄은 건너뛴다.</summary>
    private static IEnumerable<(DisevCall Call, System.Text.Json.Nodes.JsonObject Args)> Calls(DisevPart part, int chunk)
    {
        if (chunk < 0 || chunk >= part.ChunkStarts.Count) yield break;
        var (from, to) = part.ChunkRange(part.ChunkStarts[chunk]);
        foreach (var op in DisevScript.Parse(part.Data, from, to))
        {
            var raw = new byte[Math.Min(op.Length, part.Data.Length - op.Offset)];
            Array.Copy(part.Data, op.Offset, raw, 0, raw.Length);
            if (DisevCalls.Decode(raw) is { } got) yield return (got.Call, got.Args);
        }
    }

    /// <summary>조건 한 줄을 짧은 말로. 이름표에 안 올릴 것은 빈 글.</summary>
    private string Term(DisevCall call, System.Text.Json.Nodes.JsonObject args)
    {
        long N(string key) => args[key] is { } node && long.TryParse(node.ToJsonString(), out long v) ? v : 0;
        string Expr(string key)
        {
            if (args[key] is not System.Text.Json.Nodes.JsonObject expr) return "?";
            if (expr["Const"] is { } c) return c.ToJsonString();
            if (expr["Stat"] is { } s)
                return DisevScript.StatNames.TryGetValue((int)long.Parse(s.ToJsonString()), out var name) ? name : $"값{s}";
            if (expr["Cargo"] is System.Text.Json.Nodes.JsonObject cargo)
                return $"{_cities?.NameOf((int)long.Parse(cargo["City"]?.ToJsonString() ?? "0")) ?? "?"} 교역품 {cargo["Goods"]}";
            return "?";
        }
        return call switch
        {
            DisevCall.End or DisevCall.Or => "",
            DisevCall.InCity => _cities?.NameOf((int)N("City")) ?? $"도시 {N("City")}",
            DisevCall.NotInCity => $"{_cities?.NameOf((int)N("City")) ?? $"도시 {N("City")}"} 아님",
            DisevCall.InBuilding => BuildingName((int)N("Building")),
            DisevCall.NotInBuilding => $"{BuildingName((int)N("Building"))} 아님",
            DisevCall.InNation => N("Nation") == 0 ? "포르투갈" : N("Nation") == 1 ? "에스파니아" : $"나라 {N("Nation")}",
            DisevCall.InCulture => $"문화권 {N("Culture")}",
            DisevCall.YearAtLeast => $"{N("Year")}년~",
            DisevCall.YearAtMost => $"~{N("Year")}년",
            DisevCall.YearIs => $"{N("Year")}년",
            DisevCall.YearBetween => $"{N("From")}~{N("To")}년",
            DisevCall.YearMonthIs => $"{N("Year")}년 {N("Month")}월",
            DisevCall.HasFleet => "함대 있음",
            DisevCall.NoContract => "계약 없음",
            DisevCall.NoAide => "부관 없음",
            DisevCall.HasItem => $"{_items?.Find((int)N("Item"))?.Name ?? $"아이템 {N("Item")}"} 지님",
            DisevCall.LacksItem => $"{_items?.Find((int)N("Item"))?.Name ?? $"아이템 {N("Item")}"} 없음",
            DisevCall.Discovered or DisevCall.DiscoveryDone => $"{_names?.Find((int)N("Discovery"))?.Name ?? $"발견물 {N("Discovery")}"} 발견",
            DisevCall.NotDiscovered or DisevCall.DiscoveryNotDone => $"{_names?.Find((int)N("Discovery"))?.Name ?? $"발견물 {N("Discovery")}"} 미발견",
            DisevCall.HintActive => $"힌트 {N("Hint")}",
            DisevCall.HintInactive => $"힌트 {N("Hint")} 없음",
            DisevCall.RandomChance => $"{N("Success")}/{N("Denominator")} 확률",
            DisevCall.GreaterThan => $"{Expr("A")} > {Expr("B")}",
            DisevCall.GreaterOrEqual => $"{Expr("A")} ≥ {Expr("B")}",
            DisevCall.LessThan => $"{Expr("A")} < {Expr("B")}",
            DisevCall.LessOrEqual => $"{Expr("A")} ≤ {Expr("B")}",
            DisevCall.EqualTo => $"{Expr("A")} = {Expr("B")}",
            DisevCall.NotEqualTo => $"{Expr("A")} ≠ {Expr("B")}",
            _ => call.ToString(),
        };
    }

    /// <summary>건물 코드 이름 — 0 항구 · 2 왕궁 · 6 조선소 · 10 성문 · 11 자택 · 12~15 후원자 저택(0x005606A0~).</summary>
    private static string BuildingName(int code) => code switch
    {
        0 => "항구", 1 => "교역소", 2 => "왕궁", 3 => "교회", 4 => "술집", 5 => "여관", 6 => "조선소", 7 => "시장",
        8 => "도서관", 9 => "조합", 10 => "성문", 11 => "자택", >= 12 and <= 15 => $"저택 {code - 11}", _ => $"건물 {code}",
    };

    /// <summary>지금 고치는 책이 <b>발견 이벤트</b>인지 — 이야기 책이면 이름표를 안 붙인다.</summary>
    private bool Discoveries => _book_.SelectedIndex <= 0;

    private int SelectedPart => (_discoveries.SelectedItem as PartRow)?.Index ?? -1;

    private void ShowPart()
    {
        _chunks.ItemsSource = null;
        _ops.ItemsSource = null;
        ClearForm();
        _part = null;

        if (_book == null || SelectedPart < 0) return;

        var data = _book.Part(SelectedPart);
        _part = DisevPart.Parse(data, out string error);
        if (_part == null)
        {
            _header.Text = $"파트 {SelectedPart}: 뼈대를 못 읽었습니다 — {error}";
            return;
        }

        _header.Text = $"파트 {SelectedPart} · {data.Length}바이트 · 단계 번호 {_part.Step} · "
                     + $"슬롯 {_part.Slots.Count}개 · 덩이 {_part.ChunkStarts.Count}개"
                     + (_book.IsEdited(SelectedPart) ? "   ● 고침" : "");

        var rows = _part.ChunkStarts
            .Select(start =>
            {
                var (from, to) = _part.ChunkRange(start);
                return new ChunkRow
                {
                    Start = start,
                    // 무엇에 쓰이는 덩이인지를 <b>앞에</b> 적는다 — 조건인지 본문인지가
                    // 자리·크기보다 먼저 눈에 들어와야 한다.
                    Text = $"{_part.UsersOf(start),-14}  +0x{start:X4}  {to - from,4}바이트",
                };
            })
            .ToList();

        _chunks.ItemsSource = rows;

        // <b>첫 덩이는 대개 조건이다.</b> 자리로 늘어놓으면 조건이 본문보다 앞에 서는데,
        // 조건 덩이는 「조건 없음」이면 FF 한 줄뿐이라 골라 봐야 아무것도 안 나온다.
        // 그래서 <b>0번 슬롯의 본문</b>을 먼저 편다 — 사람이 보고 싶은 것은 그쪽이다.
        int first = _part.Slots.Count > 0
            ? rows.FindIndex(r => r.Start == _part.Slots[0].Body)
            : -1;
        if (first < 0 && rows.Count > 0) first = 0;
        if (first >= 0) _chunks.SelectedIndex = first;
    }

    private void ShowChunk()
    {
        _ops.ItemsSource = null;
        _flow.Content = null;
        _json.Clear();
        ClearForm();
        if (_part == null || _chunks.SelectedItem is not ChunkRow chunk) return;

        var (from, to) = _part.ChunkRange(chunk.Start);
        int still = _names?.Find(SelectedPart)?.Picture ?? -1;
        var ops = DisevScript.Parse(_part.Data, from, to, still);

        _ops.ItemsSource = ops.Select(op => new OpRow
        {
            At = $"+0x{op.Offset:X4}",
            Hex = op.Hex.Length <= 54 ? op.Hex : op.Hex[..54] + " …",
            Text = op.Text,
            Op = op,
        }).ToList();

        // JSON 탭은 발견이벤트.json 에 적히는 꼴 그대로다 — 라벨이 파트 전체에서 매겨지므로 파트를 통째 푼 뒤 이 덩이만 보인다.
        int chunkIndex = _part.ChunkStarts.ToList().IndexOf(chunk.Start);
        _json.Text = DisevTree.ToJson(DisevTree.BuildPart(_part)[chunkIndex]);
        _flow.Content = DisevFlowView.Build(DisevFlow.Build(_part.Data, ops), PickOp, Describe, ActionFor);

        // 덩이 밖으로 뛰는 상대 이동이 있으면 길이를 바꿀 때 어긋난다 — 미리 일러 준다.
        int outside = ops.Count(op => op.Text.Contains("→ 파트 +0x") && !InsideChunk(op.Text, from, to));
        _status.Text = outside == 0
            ? $"덩이 +0x{chunk.Start:X4} — 명령 {ops.Count}개"
            : $"덩이 +0x{chunk.Start:X4} — 명령 {ops.Count}개, "
              + $"덩이 밖으로 뛰는 이동 {outside}개 있음 (길이를 바꾸면 어긋납니다)";
    }

    /// <summary>
    /// 흐름도 상자에 적을 풀이 — 칸이 아이템 · 발견물 · 도시 번호면 뒤에 이름을 붙인다.
    /// </summary>
    /// <remarks>「아이템 획득: 아이템 ID 173」 만으로는 무엇인지 모른다. 이름표는 칸 풀이와 같은 것을 쓴다.</remarks>
    private string Describe(DisevScript.Op op)
    {
        var raw = RawOf(op);
        var names = DisevForm.FieldsFor(op)
            .Where(f => f.Kind is DisevForm.Lookup.Item or DisevForm.Lookup.Discovery or DisevForm.Lookup.City)
            .Select(f => NameOf(f, DisevForm.Read(raw, f)))
            .Where(name => name.Length > 0)
            .ToList();
        return names.Count == 0 ? op.Text : $"{op.Text} ({string.Join(", ", names)})";
    }

    /// <summary>
    /// 흐름도 명령 상자에 붙일 단추 — EVSTILL 은 그림, 음원은 소리, 아이템 획득은 아이템 정보다.
    /// 붙일 것이 없으면 null.
    /// </summary>
    private (string Label, Action Run)? ActionFor(DisevScript.Op op)
    {
        var raw = RawOf(op);
        if (raw.Length != 4) return null;
        int slot = raw[2] | raw[3] << 8;

        if (op.Kind == "EVSTILL 이미지 표시") return ("그림", () => ShowEventStill(slot));
        if (op.Kind == "음원 재생") return ("▶", () => PlaySound(slot));
        if (op.Kind == "AVI 재생") return ("▶", () => PlayMovie(slot));
        if (op.Kind == "아이템 보이기") return ("보기", () => ShowItem(slot));
        return null;
    }

    /// <summary>아이템 설명 · 그림. 게임 폴더를 새로 열면 버린다.</summary>
    private ItemDescriptions? _itemDescriptions;
    private ItemArt? _itemArt;

    /// <summary>
    /// 아이템 한 가지의 그림과 설명을 띄운다 — 게임 소지품 창이 여는 정보 창(<see cref="ItemInfoDialog"/>) 그대로다.
    /// </summary>
    private void ShowItem(int id)
    {
        if (_items?.Find(id) is not { } item)
        {
            _status.Text = $"아이템 {id} 을 아이템 표에서 못 찾았습니다 — 게임 폴더({_gameDir})를 확인해 주세요.";
            return;
        }

        _itemDescriptions ??= ItemDescriptions.Open(_gameDir);
        _itemArt ??= ItemArt.Open(_gameDir);
        ItemInfoDialog.Show(this, item, _itemDescriptions?.Of(id) ?? "", _itemArt);
    }

    /// <summary>편집기가 따로 드는 배경음악 — 창을 닫으면 멈추고 놓는다.</summary>
    private readonly BgmPlayer _bgm = new();

    /// <summary>
    /// 음원 ID 로 소리를 낸다 — 게임 러너(<see cref="DisevRunner"/>)와 같은 가름이다.
    /// </summary>
    /// <remarks><c>0~27</c> 은 CD 트랙(배경음악), <c>28~77</c> 은 WAVES.CDS 효과음 파트(ID−28)다.</remarks>
    /// <summary>동영상 재생 명령의 번호를 튼다 — 올린 것(asset/movie)이 먼저, 없으면 게임 폴더 원본이다.</summary>
    private void PlayMovie(int movie)
    {
        if (DiscoveryDialog.MovieOf(_gameDir, movie) is not { } path)
        {
            _status.Text = $"동영상 {movie}({MovieFiles.DiscoveryStem(movie)}) 파일이 없습니다 — 에셋-동영상에서 올리세요.";
            return;
        }
        _status.Text = $"동영상 {movie} — {Path.GetFileName(path)} 를 틉니다.";
        ShowMoviePreview(movie, path);
    }

    /// <summary>띄워 둔 미리 보기 창. 새로 틀면 닫고 다시 연다.</summary>
    private Window? _moviePreview;

    /// <summary>
    /// 편집기 가운데에 <b>작은 미리 보기 창</b>으로 튼다 — 게임 재생(<see cref="MoviePlayer"/>)처럼 화면을 덮지 않는다.
    /// 편집기를 막지 않고, 다 돌면 마지막 장면에 멈춘 채 둔다.
    /// </summary>
    private void ShowMoviePreview(int movie, string path)
    {
        _moviePreview?.Close();

        var player = new MediaElement
        {
            Source = new Uri(path),
            LoadedBehavior = MediaState.Manual,
            UnloadedBehavior = MediaState.Close,
            Stretch = Stretch.Uniform,
        };
        var window = new Window
        {
            Title = $"동영상 {movie} — {Path.GetFileName(path)}",
            Owner = this,
            Width = 520,
            Height = 420,
            ShowInTaskbar = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = Brushes.Black,
            Content = player,
        };
        player.MediaFailed += (_, e) => _status.Text = $"동영상 {movie} 를 틀지 못했습니다 — {e.ErrorException?.Message}";
        window.Loaded += (_, _) => player.Play();
        window.Closed += (_, _) =>
        {
            player.Stop();
            player.Close();
            if (_moviePreview == window) _moviePreview = null;
        };
        _moviePreview = window;
        window.Show();
    }

    private void PlaySound(int soundId)
    {
        int track = WaveBank.CdTrackFromSoundId(soundId);
        if (track >= 0)
        {
            _bgm.SetGameDirectory(_gameDir);
            _bgm.Play(track);
            _status.Text = $"음원 {soundId} — CD 트랙 {track} 을 틉니다.";
            return;
        }

        int part = WaveBank.PartFromSoundId(soundId);
        if (part >= 0 && SoundBank.Shared(_gameDir) is { } bank)
        {
            bank.Play(part);
            _status.Text = $"음원 {soundId} — 효과음 파트 {part} 를 냅니다.";
            return;
        }

        _status.Text = $"음원 {soundId} 을 낼 수 없습니다 — 게임 폴더({_gameDir})에서 소리를 못 찾았습니다.";
    }

    /// <summary>EVSTILL.CDS 묶음. 게임 폴더를 새로 열면 버린다.</summary>
    private DiscoveryStills? _eventStills;

    /// <summary>사건 스틸 한 장을 두 배로 키워 창에 띄운다.</summary>
    private void ShowEventStill(int picture)
    {
        _eventStills ??= DiscoveryStills.Open(_gameDir, "EVSTILL.CDS");
        if (_eventStills?.TryGetBgra(picture, out int w, out int h) is not { } bgra)
        {
            _status.Text = $"EVSTILL {picture} 을 못 읽었습니다 — {DiscoveryStills.LastError}";
            return;
        }

        var bitmap = BitmapSource.Create(w, h, 96, 96, PixelFormats.Bgra32, null, bgra, w * 4);
        bitmap.Freeze();
        var image = new Image { Source = bitmap, Width = w * 2, Height = h * 2, Stretch = Stretch.Fill };
        RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.NearestNeighbor);

        new Window
        {
            Title = $"EVSTILL {picture}",
            Owner = this,
            SizeToContent = SizeToContent.WidthAndHeight,
            ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = Brushes.Black,
            Content = image,
        }.ShowDialog();
    }

    /// <summary>흐름도에서 누른 노드의 명령을 표에서 고른다 — 아래 칸이 그 명령으로 바뀐다.</summary>
    private void PickOp(int offset)
    {
        if (_ops.ItemsSource is not List<OpRow> rows) return;
        if (rows.FirstOrDefault(r => r.Op.Offset == offset) is not { } row) return;

        _ops.SelectedItem = row;
        _ops.ScrollIntoView(row);
    }

    private static bool InsideChunk(string text, int from, int to)
    {
        int at = text.LastIndexOf("→ 파트 +0x", StringComparison.Ordinal);
        if (at < 0) return true;
        string digits = new(text[(at + "→ 파트 +0x".Length)..].TakeWhile(Uri.IsHexDigit).ToArray());
        return int.TryParse(digits, System.Globalization.NumberStyles.HexNumber, null, out int target)
               && target >= from && target <= to;
    }

    // ── 명령 하나를 칸으로 고치기 ──────────────────────────────────────────

    private void ClearForm()
    {
        _form.Children.Clear();
        _readers.Clear();
        _flagBox = _speakerBox = _textBox = null;
        _fields = [];
        _op = null;
        _applyOp.IsEnabled = false;
        _wide.IsEnabled = false;
    }

    /// <summary>고른 명령에 맞는 칸을 깐다.</summary>
    private void BuildForm()
    {
        ClearForm();
        if (_part == null || _ops.SelectedItem is not OpRow row) return;

        var op = row.Op;
        _op = op;
        var raw = RawOf(op);

        if (op.Kind == "대사")
        {
            BuildDialogueForm(raw);
            _applyOp.IsEnabled = true;
            _wide.IsEnabled = true;
            return;
        }

        _fields = DisevForm.FieldsFor(op);
        if (_fields.Length == 0)
        {
            _form.Children.Add(new TextBlock
            {
                Text = $"{op.Kind} — 칸으로 고칠 수 있는 명령이 아닙니다.",
                Foreground = Brushes.DimGray,
                Margin = new Thickness(0, 4, 0, 4),
            });
            return;
        }

        foreach (var field in _fields)
        {
            long value = DisevForm.Read(raw, field);
            _form.Children.Add(new TextBlock
            {
                Text = field.Label,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 4, 0),
            });

            if (field.Kind == DisevForm.Lookup.Minigame)
            {
                _form.Children.Add(MinigamePicker(value));
                continue;
            }

            var box = new TextBox { Text = value.ToString(), Width = field.Width == 4 ? 92 : 64 };
            var hint = new TextBlock
            {
                Foreground = Brushes.SteelBlue,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(4, 0, 12, 0),
                MinWidth = 8,
                Text = NameOf(field, value),
            };
            var captured = field;
            box.TextChanged += (_, _) =>
                hint.Text = long.TryParse(box.Text, out long v) ? NameOf(captured, v) : "?";

            _readers.Add(() => long.TryParse(box.Text, out long v) ? v : null);
            _form.Children.Add(box);
            _form.Children.Add(hint);
        }
        _applyOp.IsEnabled = true;
    }

    /// <summary>
    /// 미니게임을 고르는 상자. 원본에 뜀표 밖 번호(4·5·7~)가 적혀 있으면 그 번호도 한 줄로 넣어
    /// 고치지 않고 적용해도 날바이트가 안 바뀌게 한다.
    /// </summary>
    private ComboBox MinigamePicker(long value)
    {
        // 명령마다 부를 수 있는 놀이가 다르다 — 0E 04 는 코인·탑을 건너뛰고, 0E 14/1A 은 그 둘만 띄운다.
        bool puzzle = _op?.Kind == "퍼즐 미니게임";
        var choices = Enum.GetValues<DisevMinigame>()
                          .Where(g => puzzle ? g.ByPuzzleCommand() : g.ByMinigameCommand())
                          .Select(g => new MinigameChoice(g, $"{(int)g}  {g.Title()}"))
                          .ToList();
        if (!choices.Exists(c => (long)c.Value == value))
            choices.Add(new MinigameChoice((DisevMinigame)value, $"{value}  {DisevScript.MinigameName((int)value)}"));

        var picker = new ComboBox
        {
            ItemsSource = choices,
            DisplayMemberPath = nameof(MinigameChoice.Text),
            SelectedIndex = choices.FindIndex(c => (long)c.Value == value),
            MinWidth = 180,
            Margin = new Thickness(0, 0, 12, 0),
        };
        _readers.Add(() => picker.SelectedItem is MinigameChoice c ? (long)c.Value : null);
        return picker;
    }

    /// <summary>미니게임 고르는 상자의 한 줄. 바인딩은 속성만 읽으므로 튜플이 아니라 레코드다.</summary>
    private sealed record MinigameChoice(DisevMinigame Value, string Text);

    /// <summary>칸 값 뒤에 붙는 이름 — 발견물·아이템·도시·능력치.</summary>
    private string NameOf(DisevForm.Field field, long value) => field.Kind switch
    {
        DisevForm.Lookup.Stat => DisevScript.StatNames.TryGetValue((int)value, out var s) ? s : "",
        DisevForm.Lookup.Discovery => _names?.Find((int)value)?.Name ?? "",
        DisevForm.Lookup.Item => _items?.Find((int)value)?.Name ?? "",
        DisevForm.Lookup.City => _cities?.NameOf((int)value) ?? "",
        DisevForm.Lookup.Minigame => DisevScript.MinigameName((int)value),
        DisevForm.Lookup.Encounter => DisevScript.EncounterName((int)value),
        DisevForm.Lookup.Relative => _op is { } op ? $"→ 파트 +0x{op.Offset + op.Length + value:X}" : "",
        _ => "",
    };

    private void BuildDialogueForm(byte[] raw)
    {
        var (flag, tag) = DisevForm.SplitDialogue(raw);
        int textStart = (flag == null ? 1 : 2) + (tag.Length > 0 ? tag.Length + 2 : 0);
        int textEnd = raw.Length > 0 && raw[^1] == 0x00 ? raw.Length - 1 : raw.Length;
        var (speakerName, body) = DisevScript.DecodeDialogue(
            raw.AsSpan(textStart, Math.Max(0, textEnd - textStart)), normalize: false);

        _flagBox = new TextBox { Text = flag?.ToString() ?? "", Width = 48 };
        _speakerBox = new TextBox
        {
            Text = DisevScript.Hex(tag),
            Width = 190,
            FontFamily = new FontFamily("Consolas, D2Coding"),
        };
        _textBox = new TextBox { Text = body, Width = 620, TextWrapping = TextWrapping.Wrap, AcceptsReturn = false };

        _form.Children.Add(Cell("창 플래그", _flagBox, "비우면 0A 로 바로 연다"));
        _form.Children.Add(Cell($"화자 ({(speakerName ?? "없음")})", _speakerBox, "CP932 태그 날바이트. 비우면 화자 없음"));
        _form.Children.Add(Cell("본문", _textBox, ""));
    }

    private static UIElement Cell(string label, TextBox box, string hint)
    {
        var stack = new StackPanel { Orientation = Orientation.Vertical, Margin = new Thickness(0, 2, 12, 2) };
        stack.Children.Add(new TextBlock { Text = label, Foreground = Brushes.DimGray });
        stack.Children.Add(box);
        if (hint.Length > 0)
            stack.Children.Add(new TextBlock { Text = hint, Foreground = Brushes.Gray, FontSize = 10 });
        return stack;
    }

    private byte[] RawOf(DisevScript.Op op)
    {
        int length = Math.Min(op.Length, (_part?.Data.Length ?? 0) - op.Offset);
        return _part == null || length <= 0 ? [] : _part.Data.AsSpan(op.Offset, length).ToArray();
    }

    /// <summary>칸에 적은 값으로 명령 하나를 갈아 끼운다.</summary>
    private void ApplyOp()
    {
        if (_book == null || _part == null || _op is not { } op ||
            _chunks.SelectedItem is not ChunkRow chunk) return;

        byte[] replacement;
        if (op.Kind == "대사")
        {
            int? flag = null;
            if (!string.IsNullOrWhiteSpace(_flagBox?.Text))
            {
                if (!int.TryParse(_flagBox!.Text, out int value) || value is < 0 or > 255)
                {
                    _status.Text = "창 플래그는 0 ~ 255 이거나 비어 있어야 합니다.";
                    return;
                }
                flag = value;
            }

            var tag = DisevScript.ParseHex(_speakerBox?.Text ?? "");
            if (tag == null)
            {
                _status.Text = "화자 태그를 못 읽었습니다 — 16진 두 자리씩 적어 주세요.";
                return;
            }
            replacement = DisevForm.BuildDialogue(flag, tag, _textBox?.Text ?? "");
        }
        else
        {
            replacement = RawOf(op);
            for (int i = 0; i < _fields.Length && i < _readers.Count; i++)
            {
                if (_readers[i]() is not { } value)
                {
                    _status.Text = $"{_fields[i].Label}: 숫자가 아닙니다.";
                    return;
                }
                var next = DisevForm.Write(replacement, _fields[i], value, out string error);
                if (next == null)
                {
                    _status.Text = error;
                    return;
                }
                replacement = next;
            }
        }

        // 덩이 안에서 그 명령 자리만 갈아 끼운다.
        var chunkBytes = _part.Chunk(chunk.Start);
        int at = op.Offset - chunk.Start;
        int len = Math.Min(op.Length, chunkBytes.Length - at);
        if (at < 0 || len < 0)
        {
            _status.Text = "명령 자리가 덩이 밖입니다.";
            return;
        }

        var merged = new List<byte>(chunkBytes.Length + replacement.Length);
        merged.AddRange(chunkBytes[..at]);
        merged.AddRange(replacement);
        merged.AddRange(chunkBytes[(at + len)..]);

        Commit(chunk.Start, merged.ToArray(),
               $"파트 {SelectedPart} +0x{op.Offset:X4} 「{op.Kind}」 을 {replacement.Length}바이트로 고쳤습니다");
    }

    /// <summary>고른 갈래의 값 칸을 채운다 — 동영상은 틀 파일이 있는 번호만, DSTILL 은 그림 전부.</summary>
    private void FillAddValues()
    {
        int keep = (_addValue.SelectedItem as ValueChoice)?.Id ?? -1;
        var call = AddKinds[Math.Clamp(_addKind.SelectedIndex, 0, AddKinds.Length - 1)].Call;
        var choices = new List<ValueChoice>();

        if (call == DisevCall.PlayVideo)
        {
            // 발견물 표가 그 번호를 쓰면 이름을 붙인다.
            var users = (_names?.Discoveries ?? []).Where(d => d.Movie >= 0)
                .GroupBy(d => d.Movie).ToDictionary(g => g.Key, g => string.Join(", ", g.Select(d => d.Name)));
            foreach (int n in Enumerable.Range(0, MovieFiles.OriginalDiscoveryMovies)
                         .Union(MovieFiles.UploadedDiscoveryNumbers()).OrderBy(n => n))
            {
                string stem = MovieFiles.DiscoveryStem(n);
                string who = users.TryGetValue(n, out var names) ? $" · {names}" : "";
                if (MovieFiles.Uploaded(stem) is { } up)
                    choices.Add(new ValueChoice(n, $"{n} · 올린 것 {Path.GetFileName(up)}{who}"));
                else if (MovieFiles.Original(_gameDir, stem) != null)
                    choices.Add(new ValueChoice(n, $"{n} · 원본{who}"));
            }
        }
        else
        {
            _stills ??= DiscoveryStills.Open(_gameDir);
            var users = (_names?.Discoveries ?? []).Where(d => d.Picture >= 0)
                .GroupBy(d => d.Picture).ToDictionary(g => g.Key, g => string.Join(", ", g.Select(d => d.Name)));
            int count = Math.Max(_stills?.Count ?? 0, users.Count == 0 ? 0 : users.Keys.Max() + 1);
            for (int n = 0; n < count; n++)
                choices.Add(new ValueChoice(n, users.TryGetValue(n, out var names) ? $"{n} · {names}" : $"{n}"));
        }

        _addValue.ItemsSource = choices;
        _addValue.SelectedItem = choices.FirstOrDefault(c => c.Id == keep)
                                 ?? (call == DisevCall.PlayVideo
                                     ? choices.LastOrDefault(c => c.Id >= MovieFiles.OriginalDiscoveryMovies)
                                     : null)
                                 ?? choices.FirstOrDefault();
    }

    /// <summary>DSTILL 그림 묶음 — 값 칸의 그림 수를 셀 때 쓴다. 게임 폴더를 새로 열면 버린다.</summary>
    private DiscoveryStills? _stills;

    /// <summary>값 칸 한 줄.</summary>
    private sealed record ValueChoice(int Id, string Text)
    {
        public override string ToString() => Text;
    }

    /// <summary>추가 줄 옆에 지금 기준이 되는 명령을 적고, 고른 것이 없으면 단추를 끈다.</summary>
    private void ShowAddTarget()
    {
        bool picked = _ops.SelectedItem is OpRow;
        _addAbove.IsEnabled = _addBelow.IsEnabled = _removeOp.IsEnabled = picked;
        _addTarget.Text = _ops.SelectedItem is OpRow row
            ? $"기준: {row.At} {row.Op.Kind}"
            : "순서도나 표에서 명령을 하나 고르세요";
    }

    /// <summary>
    /// 고른 명령의 <b>위나 아래</b>에 새 명령(동영상 재생 <c>00 02</c> · DSTILL 그림 <c>00 01</c>)을 넣거나, 고른 명령을 뺀다.
    /// </summary>
    /// <remarks>
    /// 바이트를 그 자리에 끼우지 않는다 — 덩이를 줄 나무(<see cref="DisevTree"/>)로 풀어 줄을 넣고 빼고,
    /// 파트를 다시 짠다(<see cref="DisevBook.JoinChunks"/>). 그래야 덩이 밖으로 뛰는 분기·절대 이동도 라벨을 따라
    /// 새 자리로 다시 셈한다. 위로 넣으면 그 명령으로 뛰던 분기는 <b>새 명령부터</b> 돈다(라벨이 새 줄로 옮는다).
    /// DSTILL 그림은 게임처럼 <b>다음 대사와 한 창에</b> 뜬다.
    /// </remarks>
    private void EditLines(bool below, bool remove)
    {
        if (_book == null || _part == null || _chunks.SelectedItem is not ChunkRow chunk) return;

        var chunks = DisevTree.BuildPart(_part);
        int chunkIndex = _part.ChunkStarts.ToList().IndexOf(chunk.Start);
        if (chunkIndex < 0) return;
        var lines = chunks[chunkIndex];

        int at = _ops.SelectedIndex;
        if (at < 0 || at >= lines.Count)
        {
            _status.Text = "순서도나 표에서 기준이 될 명령을 먼저 고르세요.";
            return;
        }

        string message;
        int select;
        if (remove)
        {
            var gone = lines[at];
            if (gone.Call == DisevCall.End && at == lines.Count - 1)
            {
                _status.Text = "덩이 끝(FF)은 뺄 수 없습니다.";
                return;
            }
            // 이리로 뛰던 점프는 다음 줄로 옮긴다.
            if (gone.Label != null)
            {
                if (at + 1 >= lines.Count || lines[at + 1].Label != null)
                {
                    _status.Text = $"이 명령으로 뛰는 점프({gone.Label})가 있어 뺄 수 없습니다.";
                    return;
                }
                lines[at + 1].Label = gone.Label;
            }
            lines.RemoveAt(at);
            message = $"파트 {SelectedPart} 에서 「{(_ops.SelectedItem as OpRow)?.Op.Kind}」 을 뺐습니다";
            select = Math.Min(at, lines.Count - 1);
        }
        else
        {
            var (call, kindText) = AddKinds[Math.Clamp(_addKind.SelectedIndex, 0, AddKinds.Length - 1)];
            if (_addValue.SelectedItem is not ValueChoice value)
            {
                _status.Text = call == DisevCall.PlayVideo
                    ? "넣을 동영상이 없습니다 — 에셋-동영상에서 「새 동영상 추가」로 먼저 올리세요."
                    : "DSTILL 그림을 못 읽었습니다 — 게임 폴더를 골라 주세요.";
                return;
            }
            if (below && lines[at].Call == DisevCall.End && at == lines.Count - 1)
            {
                _status.Text = "덩이 끝(FF) 아래에는 넣을 수 없습니다 — 위로 추가하세요.";
                return;
            }

            var line = new DisevLine
            {
                Call = call,
                Args = new System.Text.Json.Nodes.JsonObject { ["Id"] = value.Id },
            };
            int where = below ? at + 1 : at;
            if (!below)
            {
                // 위로 넣으면 그 명령으로 뛰던 분기가 새 명령부터 돌게 라벨을 옮긴다.
                line.Label = lines[at].Label;
                lines[at].Label = null;
            }
            lines.Insert(where, line);
            message = $"파트 {SelectedPart} 에 「{kindText} {value.Id}」 을 {(below ? "아래" : "위")}로 넣었습니다";
            select = where;
        }

        var rebuilt = DisevBook.JoinChunks(_part, chunks, out string error);
        if (rebuilt == null || DisevPart.Parse(rebuilt, out _) == null)
        {
            _status.Text = $"못 고쳤습니다 — {error}";
            return;
        }

        int part = SelectedPart;
        _book.Replace(part, rebuilt);
        RefreshDiscoveries();
        ShowPart();
        if (chunkIndex < _chunks.Items.Count) _chunks.SelectedIndex = chunkIndex;
        if (select >= 0 && select < _ops.Items.Count) _ops.SelectedIndex = select;
        _status.Text = message + " — 아직 적어 두지 않았습니다. 「저장」을 눌러야 들어갑니다.";
    }

    /// <summary>고친 덩이를 파트에 넣고 화면을 다시 그린다.</summary>
    private void Commit(int chunkStart, byte[] chunkBytes, string message)
    {
        if (_book == null || _part == null) return;

        var rebuilt = _part.Rebuild(chunkStart, chunkBytes, out string error);
        if (rebuilt == null)
        {
            _status.Text = $"못 고쳤습니다 — {error}";
            return;
        }
        if (DisevPart.Parse(rebuilt, out string check) == null)
        {
            _status.Text = $"고친 뒤 뼈대가 깨집니다 — {check}";
            return;
        }

        int part = SelectedPart, chunkIndex = _chunks.SelectedIndex, opIndex = _ops.SelectedIndex;
        _book.Replace(part, rebuilt);
        RefreshDiscoveries();
        ShowPart();
        if (chunkIndex >= 0 && chunkIndex < _chunks.Items.Count) _chunks.SelectedIndex = chunkIndex;
        if (opIndex >= 0 && opIndex < _ops.Items.Count) _ops.SelectedIndex = opIndex;
        _status.Text = message + " — 아직 적어 두지 않았습니다. 「저장」을 눌러야 들어갑니다.";
    }

    /// <summary>이 발견물만 원본 대본으로 되돌린다.</summary>
    private void RevertOne()
    {
        if (_book == null || SelectedPart < 0) return;

        if (!_book.Restore(SelectedPart))
        {
            _status.Text = $"되돌리지 못했습니다 — {DisevBook.LastError}";
            return;
        }

        _book.Save();
        RefreshDiscoveries();
        ShowPart();
        _status.Text = $"파트 {SelectedPart} 를 원본 대본으로 되돌렸습니다.";
    }

    /// <summary>적어 둔 것을 통째로 버리고 원본에서 다시 뜬다.</summary>
    private void RevertAll()
    {
        if (!ConfirmDialog.Ask(this,
                "적어 둔 대본을 버리고 앱에 실린 원본 대본으로 되돌립니다. 고친 것이 다 사라집니다. 좋습니까?",
                "원본에서 다시 뜨기"))
            return;

        Load(_gameDir, fresh: true);
        if (_book != null) _status.Text = $"원본에서 다시 떴습니다 — 파트 {_book.Count}개";
    }

    /// <summary>
    /// 고친 것을 <c>발견이벤트.json</c> 에 적어 둔다 — 원본 <c>DISEV.CDS</c> 는 안 건드린다.
    /// </summary>
    /// <remarks>
    /// 적어 두면 <b>우리 게임에는 곧장 든다</b> — 발견하러 가면 그 대본이 돈다
    /// (<see cref="DisevRunner.Open"/> 이 이 책을 읽는다).
    /// </remarks>
    private void Save()
    {
        if (_book == null) return;
        if (!_book.HasChanges)
        {
            _status.Text = "고친 것이 없습니다.";
            return;
        }

        _book.Save();
        RefreshDiscoveries();
        ShowPart();
        _status.Text = $"적어 두었습니다 — 게임에는 바로 듭니다. ({DisevBook.Path_})";
    }
}
