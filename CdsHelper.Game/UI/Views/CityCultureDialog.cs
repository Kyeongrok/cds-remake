using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CdsHelper.Game.Engine.Town;
using CdsHelper.Game.Local.Helpers;
using CdsHelper.Support.Local.Settings;

namespace CdsHelper.Game.UI.Views;

/// <summary>
/// 도시마다의 문화권과, 그 문화권이 부르는 시설 화자 얼굴을 맞대어 보는 창.
/// </summary>
/// <remarks>
/// 같은 조선소라도 마을에 따라 딴 사람이 말을 건다 — 화자표(<c>0x0056823C</c>)를
/// <c>[건물코드][문화권]</c> 으로 읽기 때문이다. 도시를 고르면 그 마을 문화권의 얼굴이
/// 서고, 문화권을 손으로 갈아 보면 <b>같은 건물이 어떻게 바뀌는지</b>가 한눈에 보인다.
///
/// 고른 문화권을 <b>씌워 둘</b> 수도 있다. 씌우면 그 도시는 앱 어디서나 그 문화권으로
/// 굴러간다(<see cref="CityCultureEdits"/>) — 세빌리아를 이슬람으로 갈아 두고 그 마을에
/// 들어가면 조선소에 이슬람 쪽 사람이 앉는다. 게임 EXE 는 손대지 않는다.
/// </remarks>
public sealed class CityCultureDialog : GameWindow
{
    /// <summary>얼굴을 낼 시설들. 화자가 없는 자택·저택 따위는 뺐다.</summary>
    private static readonly (int Code, string Name)[] Kinds =
    [
        (0, "항구"), (1, "교역소"), (2, "왕궁"), (3, "교회"), (4, "술집"),
        (5, "여관"), (6, "조선소"), (7, "시장"), (8, "도서관"), (9, "조합"), (10, "성문"),
    ];

    private readonly DataGrid _cities = new()
    {
        AutoGenerateColumns = false,
        IsReadOnly = true,
        HeadersVisibility = DataGridHeadersVisibility.Column,
        AlternatingRowBackground = Brushes.WhiteSmoke,
        Width = 320,
    };

    private readonly ComboBox _culture = new() { Width = 220 };

    private readonly Button _apply = new()
    {
        Content = "이 도시에 씌우기",
        Padding = new Thickness(10, 2, 10, 2),
        Margin = new Thickness(8, 0, 0, 0),
    };

    private readonly Button _reset = new()
    {
        Content = "되돌리기",
        Padding = new Thickness(10, 2, 10, 2),
        Margin = new Thickness(6, 0, 0, 0),
    };

    private readonly Button _resetAll = new()
    {
        Content = "모두 되돌리기",
        Padding = new Thickness(10, 2, 10, 2),
        Margin = new Thickness(6, 0, 0, 0),
    };

    private readonly WrapPanel _faces = new() { Margin = new Thickness(8) };

    /// <summary>그 문화권 술집에 서는 손님들.</summary>
    private readonly WrapPanel _guestFaces = new() { Margin = new Thickness(8, 0, 8, 8) };

    /// <summary>손님 줄 머리글 — 몇 명인지 함께 적는다.</summary>
    private readonly TextBlock _guestHead = new()
    {
        Margin = new Thickness(10, 4, 8, 0),
        FontWeight = FontWeights.Bold,
    };

    private TavernGuests? _guests;
    private readonly TextBlock _status = new() { Margin = new Thickness(10, 6, 10, 8) };

    private readonly ComboBox _nation = new()
    {
        Width = 220,
        VerticalAlignment = VerticalAlignment.Center,
    };

    private readonly Button _applyNation = new()
    {
        Content = "이 도시에 씌우기",
        Padding = new Thickness(10, 2, 10, 2),
        Margin = new Thickness(8, 0, 0, 0),
    };

    /// <summary>두 줄의 이름표 폭. 콤보가 같은 자리에서 시작해야 줄이 가지런하다.</summary>
    private const double LabelWidth = 56;

    private NationTable? _kingdoms;
    private CityTable? _names;
    private CityExeTable? _rows;
    private SpeakerFaceTable? _speakers;
    private Portraits? _portraits;

    public CityCultureDialog()
    {
        Title = "도시 · 문화권 · 왕국";
        Width = 980;
        Height = 640;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        Col("번호", nameof(Row.Id), 56);
        Col("도시", nameof(Row.Name), 130);
        Col("문화권", nameof(Row.CultureNo), 60);
        Col("이름", nameof(Row.Culture), 90);
        Col("왕국", nameof(Row.Nation), 160);
        Col("씌움", nameof(Row.Mark), 44);
        _cities.SelectionChanged += (_, _) => PickCity();

        _culture.SelectionChanged += (_, _) => ShowFaces();

        _apply.Click += (_, _) => Apply();
        _reset.Click += (_, _) => Undo();
        _resetAll.Click += (_, _) => UndoAll();
        _applyNation.Click += (_, _) => ApplyNation();

        // 두 줄로 나눈다 — 한 줄에 다 늘어놓으면 창을 가로로 한참 키워야 왕국 칸이 보인다.
        var cultureRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Children =
            {
                new TextBlock
                {
                    Text = "문화권:",
                    Width = LabelWidth,
                    VerticalAlignment = VerticalAlignment.Center,
                },
                _culture,
                _apply,
                _reset,
                _resetAll,
            },
        };

        var nationRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 6, 0, 0),
            Children =
            {
                new TextBlock
                {
                    Text = "왕국:",
                    Width = LabelWidth,
                    VerticalAlignment = VerticalAlignment.Center,
                },
                _nation,
                _applyNation,
            },
        };

        var bar = new StackPanel
        {
            Margin = new Thickness(10, 8, 10, 4),
            Children = { cultureRow, nationRow },
        };

        var right = new DockPanel();
        DockPanel.SetDock(bar, Dock.Top);
        right.Children.Add(bar);
        // 시설 화자 얼굴 아래에 <b>그 문화권 술집 손님</b>을 이어 붙인다 — 같은 문화권이
        // 부르는 얼굴이라 한자리에서 맞대어 보는 것이 낫다.
        var stack = new StackPanel();
        stack.Children.Add(_faces);
        stack.Children.Add(_guestHead);
        stack.Children.Add(_guestFaces);

        right.Children.Add(new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = stack,
        });

        var split = new DockPanel();
        DockPanel.SetDock(_cities, Dock.Left);
        split.Children.Add(_cities);
        split.Children.Add(right);

        var page = new DockPanel();
        DockPanel.SetDock(_status, Dock.Bottom);
        page.Children.Add(_status);
        page.Children.Add(split);
        Content = page;

        Loaded += (_, _) => Load();
    }

    /// <summary>목록 한 줄 — 도시와 그 문화권.</summary>
    /// <param name="Mark">손으로 씌운 줄에만 <c>●</c> 가 선다.</param>
    private sealed record Row(int Id, string Name, int CultureNo, string Culture,
                              string Nation, int NationNo, string Mark);

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
        _speakers = SpeakerFaceTable.Open(dir);
        _guests = TavernGuests.Open(dir);
        _portraits = Portraits.Open(dir);
        _kingdoms = NationTable.Open(dir);

        if (_rows == null || _speakers == null)
        {
            _status.Text = "표를 못 읽었습니다 — 세이브를 한 번 열어 게임 폴더를 알려 주세요"
                         + $" ({CityExeTable.LastError} {SpeakerFaceTable.LastError})".TrimEnd();
            return;
        }

        for (int i = 0; i < SpeakerFaceTable.Cultures; i++)
            _culture.Items.Add($"{i}  {CityCultureEdits.NameOf(i)}");

        // 왕국은 나라 표에서 온다 — 못 읽으면 그 칸만 비워 둔다.
        foreach (var nation in _kingdoms?.Nations ?? [])
            _nation.Items.Add($"{nation.Id}  {nation.Name}");
        _applyNation.IsEnabled = _nation.Items.Count > 0;

        Rebuild();
        if (_cities.Items.Count > 0) _cities.SelectedIndex = 0;
    }

    /// <summary>도시 목록을 다시 짓는다. 보고 있던 도시는 그대로 붙들어 둔다.</summary>
    private void Rebuild()
    {
        if (_names is not { } names || _rows is not { } table) return;

        int keep = _cities.SelectedItem is Row picked ? picked.Id : -1;

        var rows = new List<Row>();
        foreach (var city in names.Cities)
        {
            int changed = CityCultureEdits.Of(city.Id);
            int no = table.CultureOf(city.Id);
            int nationNo = table.NationOf(city.Id);
            bool marked = changed != CityCultureEdits.None
                       || CityNationEdits.Of(city.Id) != CityNationEdits.None;
            rows.Add(new Row(city.Id, city.Name, no,
                             changed == CityCultureEdits.None
                                 ? city.Culture : CityCultureEdits.NameOf(changed),
                             _kingdoms?.Find(nationNo)?.Name ?? "", nationNo,
                             marked ? "●" : ""));
        }
        _cities.ItemsSource = rows;
        if (keep >= 0) _cities.SelectedItem = rows.FirstOrDefault(r => r.Id == keep);

        int edits = CityCultureEdits.All.Count + CityNationEdits.All.Count;
        _status.Text = $"도시 {rows.Count}곳 · 문화권 {SpeakerFaceTable.Cultures}가지 — "
                     + "화자표 0x0056823C 를 [건물코드][문화권] 으로 읽는다"
                     + (edits == 0 ? "" : $" · 손으로 씌운 곳 {edits}곳");
    }

    /// <summary>고른 문화권을 이 도시에 씌운다 — 앱이 그 도시를 그 문화권으로 굴린다.</summary>
    private void Apply()
    {
        if (_cities.SelectedItem is not Row row || _culture.SelectedIndex < 0) return;
        CityCultureEdits.Set(row.Id, _culture.SelectedIndex);
        Rebuild();
    }

    /// <summary>고른 왕국을 이 도시에 씌운다 — 그 도시가 그 나라 것이 된다.</summary>
    private void ApplyNation()
    {
        if (_cities.SelectedItem is not Row row || _nation.SelectedIndex < 0) return;
        CityNationEdits.Set(row.Id, _nation.SelectedIndex);
        Rebuild();
    }

    /// <summary>이 도시에 씌운 것을 걷는다 — 게임 표의 값으로 돌아간다.</summary>
    private void Undo()
    {
        if (_cities.SelectedItem is not Row row) return;
        CityCultureEdits.Reset(row.Id);
        CityNationEdits.Reset(row.Id);
        Rebuild();
        PickCity();
    }

    /// <summary>씌운 것을 몽땅 걷는다.</summary>
    private void UndoAll()
    {
        CityCultureEdits.ResetAll();
        CityNationEdits.ResetAll();
        Rebuild();
        PickCity();
    }

    /// <summary>고른 도시의 문화권으로 콤보를 맞춰 준다.</summary>
    private void PickCity()
    {
        if (_cities.SelectedItem is not Row row) return;
        _culture.SelectedIndex = row.CultureNo;   // 고르면 ShowFaces 가 따라 돈다
        if (row.NationNo >= 0 && row.NationNo < _nation.Items.Count)
            _nation.SelectedIndex = row.NationNo;
        ShowFaces();
    }

    /// <summary>그 문화권일 때 시설마다 누가 말을 거는지.</summary>
    private void ShowFaces()
    {
        _faces.Children.Clear();
        if (_speakers is not { } speakers) return;
        int culture = _culture.SelectedIndex;
        if (culture < 0) return;

        foreach (var (code, name) in Kinds)
        {
            int face = speakers.FaceOf(code, culture);
            _faces.Children.Add(Cell(name, code, face, speakers.IsFemale(code)));
        }

        // 술집·여관의 무명 손님 — 화자표가 아니라 고정 표 0x004A2F80 에서 온다.
        _faces.Children.Add(Cell("무명 손님", -1, TavernRumors.StrangerFace(culture), female: false));

        ShowGuests(CityCultureEdits.NameOf(culture));
    }

    /// <summary>그 문화권 술집에 서는 손님들을 죽 늘어놓는다.</summary>
    private void ShowGuests(string culture)
    {
        _guestFaces.Children.Clear();

        if (_guests is not { } book)
        {
            _guestHead.Text = $"술집 손님 — 그림을 못 읽었습니다 ({TavernGuests.LastError})";
            return;
        }

        var seats = book.Of(culture);
        _guestHead.Text = $"술집 손님 — {culture} 구간 {seats.Count}명"
                        + "   (여급으로 설 수 있는 여자는 번호 옆에 ♀)";

        foreach (var guest in seats) _guestFaces.Children.Add(GuestCell(book, guest));
    }

    /// <summary>
    /// 손님 한 칸 — 서 있는 그림과 번호.
    /// </summary>
    /// <remarks>
    /// 무명 손님의 <b>초상</b>은 손님마다 내지 않는다. 원본은 서 있는 그림과 상관없이
    /// 문화권 하나에 얼굴 하나를 물리므로(<c>0x004A2F80</c>, <see cref="TavernRumors.StrangerFace"/>)
    /// 위 시설 줄에 「무명 손님」 한 칸으로 낸다.
    /// </remarks>
    private static UIElement GuestCell(TavernGuests book, TavernGuests.Guest guest)
    {
        var box = new StackPanel { Margin = new Thickness(6), Width = 88 };

        var px = book.TryGetBgra(guest);
        if (px != null && guest.Width > 0 && guest.Height > 0)
        {
            var bmp = BitmapSource.Create(guest.Width, guest.Height, 96, 96,
                                          PixelFormats.Bgra32, null, px, guest.Width * 4);
            bmp.Freeze();

            var image = new Image
            {
                Source = bmp,
                Width = guest.Width,
                Height = guest.Height,
                Stretch = Stretch.Uniform,
                MaxHeight = 96,
            };
            RenderOptions.SetBitmapScalingMode(image, GameUi.SpriteScaling);
            box.Children.Add(image);
        }

        box.Children.Add(new TextBlock
        {
            Text = $"{guest.Index}{(guest.Female ? " ♀" : "")}",
            HorizontalAlignment = HorizontalAlignment.Center,
            FontSize = 11,
        });
        return box;
    }

    /// <summary>시설 한 칸 — 이름 · 얼굴 · 번호.</summary>
    private UIElement Cell(string name, int code, int face, bool female)
    {
        var box = new StackPanel { Margin = new Thickness(8), Width = 96 };
        box.Children.Add(new TextBlock
        {
            Text = code < 0 ? name : $"{name} ({code})",
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 4),
        });

        var px = face < 0 ? null : _portraits?.TryGetBgra(face, female);
        if (px != null)
        {
            var bmp = BitmapSource.Create(Portraits.Width, Portraits.Height, 96, 96,
                                          PixelFormats.Bgra32, null, px, Portraits.Width * 4);
            bmp.Freeze();

            var image = new Image { Source = bmp, Width = Portraits.Width, Height = Portraits.Height };
            RenderOptions.SetBitmapScalingMode(image, GameUi.SpriteScaling);
            box.Children.Add(image);
        }
        else
        {
            box.Children.Add(new Border
            {
                Width = Portraits.Width,
                Height = Portraits.Height,
                Background = Brushes.Gainsboro,
                Child = new TextBlock
                {
                    Text = face < 0 ? "없음" : "그림 없음",
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                },
            });
        }

        box.Children.Add(new TextBlock
        {
            Text = face < 0 ? "—" : female ? $"{face} (여)" : face.ToString(),
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 2, 0, 0),
        });
        return box;
    }
}
