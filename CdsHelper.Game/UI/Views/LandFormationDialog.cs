using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.IO;
using CdsHelper.Game.Engine.Land;
using CdsHelper.Game.Local.Helpers;
using CdsHelper.Support.Local.Models;
using CdsHelper.Support.Local.Settings;

namespace CdsHelper.Game.UI.Views;

/// <summary>
/// 개발 → <b>부대 편성</b> — 적이 내는 부대 세트 여덟 벌을 보고 고친다.
/// </summary>
/// <remarks>
/// 왼쪽이 나라 일흔여덟이다. 나라를 고르면 그 나라 수도의 문화권으로 진형이 정해지므로
/// (<c>0x004A1320</c>) 맘루크 왕조 이집트를 누르면 <b>이슬람 상비군</b>이, 무로마치 막부를
/// 누르면 <b>일본 무가군</b>이 오른쪽에 펴진다.
///
/// 자리는 병종 하나로 못 박을 수도 있고 <b>기능으로 갈리게</b> 둘 수도 있다 — 게임이
/// 적 대장의 검술·포술·사격술로 낙타병과 경보병을 가르는 그 자리다.
///
/// 고친 것은 <see cref="LandFormationEdits"/> 가 적어 두고 놀이도 그대로 쓴다.
/// </remarks>
public sealed class LandFormationDialog : GameWindow
{
    private readonly ListBox _nations = new()
    {
        Width = 230,
        Background = GameUi.PageFill,
        BorderBrush = GameUi.Edge,
        BorderThickness = new Thickness(1),
    };

    private readonly StackPanel _units = new();
    private readonly TextBlock _head = new()
    {
        Foreground = GameUi.Text,
        FontWeight = FontWeights.Bold,
        FontSize = 15,
        Margin = new Thickness(0, 0, 0, 2),
    };
    private readonly TextBlock _note = new()
    {
        Foreground = GameUi.Edge,
        TextWrapping = TextWrapping.Wrap,
        Margin = new Thickness(0, 0, 0, 8),
    };
    private readonly TextBox _name = new()
    {
        Width = 200,
        Background = GameUi.PageFill,
        Foreground = Brushes.Black,
        Padding = new Thickness(4, 2, 4, 2),
    };

    private readonly LandArt? _art;
    private readonly NationTable? _nationTable;
    private readonly CityExeTable? _cities;

    /// <summary>지금 펴 놓은 진형. 나라를 고르면 따라 바뀐다.</summary>
    private int _shape;

    /// <summary>고치는 중인 자리들. 「간직한다」를 눌러야 적힌다.</summary>
    private readonly List<LandFormations.Slot> _slots = [];

    private LandFormationDialog(string gameDirectory)
    {
        Title = "부대 편성";
        Width = 940;
        Height = 660;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = GameUi.Back;

        _art = gameDirectory.Length > 0 ? LandArt.Open(gameDirectory) : null;
        _nationTable = NationTable.Open(gameDirectory);
        _cities = CityExeTable.Open(gameDirectory);

        Fill();
        Content = Build();

        _nations.SelectionChanged += (_, _) => Pick();
        if (_nations.Items.Count > 0) _nations.SelectedIndex = 0;
    }

    /// <summary>창을 띄운다.</summary>
    public static void Show(Window? owner)
    {
        // 게임 폴더는 마지막으로 연 세이브가 있는 데다 — 다른 고치는 창들과 같다.
        string dir = Path.GetDirectoryName(AppSettings.LastSaveFilePath) ?? "";
        var window = new LandFormationDialog(dir);
        if (owner != null) window.Owner = owner;
        window.ShowDialog();
    }

    // ── 왼쪽 — 나라 ────────────────────────────────────────────────────────────

    /// <summary>목록 한 줄이 들고 있는 것.</summary>
    private sealed record Row(int Nation, string Name, int Culture, int Shape)
    {
        public override string ToString() =>
            $"{Name}  —  {LandFormations.Of(Shape).Name}";
    }

    private void Fill()
    {
        if (_nationTable is not { } table) return;

        foreach (var nation in table.Nations)
        {
            if (nation.Name.Length == 0) continue;

            int culture = _cities?.CultureOf(nation.Capital) ?? 0;
            _nations.Items.Add(new Row(nation.Id, nation.Name, culture,
                                       LandFormations.ShapeOf(culture)));
        }
    }

    private void Pick()
    {
        if (_nations.SelectedItem is not Row row) return;

        _shape = row.Shape;
        var set = LandFormations.Of(_shape);

        _name.Text = set.Name;
        _head.Text = $"진형 {_shape} — {set.Name}";

        string culture = row.Culture >= 0 && row.Culture < CityCultureEdits.Names.Length
            ? CityCultureEdits.Names[row.Culture] : $"{row.Culture}";
        _note.Text = $"{row.Name}의 수도는 {culture} 문화권이라 이 진형이 나온다. "
                     + $"같은 진형을 쓰는 데는 {set.Where} 다. "
                     + (set.Coin ? "기능이 하나 모자라면 반반으로 굴린다."
                                 : "기능이 넉넉한지 하나로만 가른다(반반 굴림이 없다).")
                     + (LandFormationEdits.Edited(_shape) ? "   ● 고쳐 둔 진형이다." : "");

        _slots.Clear();
        _slots.AddRange(set.Units);
        Paint();
    }

    // ── 오른쪽 — 자리들 ────────────────────────────────────────────────────────

    private void Paint()
    {
        _units.Children.Clear();
        for (int i = 0; i < _slots.Count; i++) _units.Children.Add(Line(i));

        if (_slots.Count < LandFormations.MaxUnits)
            _units.Children.Add(GameUi.PushButton("자리 늘리기", () =>
            {
                _slots.Add(new LandFormations.Slot(LandUnits.Light));
                Paint();
            }, 110));
    }

    /// <summary>자리 한 줄 — 그림 · 병종 · 갈림 · 걷기.</summary>
    private UIElement Line(int at)
    {
        var slot = _slots[at];

        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 0, 0, 6),
        };

        row.Children.Add(new TextBlock
        {
            Text = at == 0 ? "대장" : $"{at + 1}번",
            Width = 40,
            Foreground = at == 0 ? Brushes.Gold : GameUi.Text,
            VerticalAlignment = VerticalAlignment.Center,
        });

        if (Sprite(slot.Big) is { } art) row.Children.Add(art);

        row.Children.Add(Kinds(slot.Big, kind =>
        {
            _slots[at] = _slots[at] with { Big = kind };
            Paint();
        }));

        // 기능으로 갈리는 자리인지.
        var splits = new ComboBox
        {
            Width = 96,
            Margin = new Thickness(6, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
            ItemsSource = SplitNames,
            SelectedIndex = SplitAt(slot.Skill),
        };
        splits.SelectionChanged += (_, _) =>
        {
            int skill = SplitSkill(splits.SelectedIndex);
            _slots[at] = _slots[at] with
            {
                Skill = skill,
                Small = skill < 0 ? -1 : Math.Max(0, _slots[at].Small),
            };
            Paint();
        };
        row.Children.Add(splits);

        if (slot.Splits)
        {
            row.Children.Add(new TextBlock
            {
                Text = "아니면",
                Foreground = GameUi.Edge,
                Margin = new Thickness(6, 0, 4, 0),
                VerticalAlignment = VerticalAlignment.Center,
            });
            if (Sprite(slot.Small) is { } small) row.Children.Add(small);
            row.Children.Add(Kinds(slot.Small, kind =>
            {
                _slots[at] = _slots[at] with { Small = kind };
                Paint();
            }));
        }

        if (at > 0)
        {
            var drop = GameUi.PushButton("걷기", () => { _slots.RemoveAt(at); Paint(); }, 56);
            drop.Margin = new Thickness(10, 0, 0, 0);
            row.Children.Add(drop);
        }
        return row;
    }

    /// <summary>병종 스물넷을 고르는 칸.</summary>
    private static ComboBox Kinds(int kind, Action<int> pick)
    {
        var box = new ComboBox
        {
            Width = 116,
            Margin = new Thickness(4, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
            ItemsSource = LandUnits.Names,
            SelectedIndex = Math.Clamp(kind, 0, LandUnits.Names.Length - 1),
        };
        box.SelectionChanged += (_, _) => { if (box.SelectedIndex >= 0) pick(box.SelectedIndex); };
        return box;
    }

    /// <summary>갈림 칸의 줄들.</summary>
    private static readonly string[] SplitNames = ["안 갈림", "검술", "포술", "사격술", "셋 다"];

    private static int SplitAt(int skill) => skill switch
    {
        Skill.Sword => 1,
        Skill.Gunnery => 2,
        Skill.Shooting => 3,
        LandFormations.AllThree => 4,
        _ => 0,
    };

    private static int SplitSkill(int at) => at switch
    {
        1 => Skill.Sword,
        2 => Skill.Gunnery,
        3 => Skill.Shooting,
        4 => LandFormations.AllThree,
        _ => -1,
    };

    /// <summary>그 병종의 첫 몸짓. 그림을 못 구하면 null.</summary>
    private Image? Sprite(int kind)
    {
        if (_art == null || kind < 0) return null;

        var bgra = _art.TryGetUnit(kind, friend: false, culture: 0, frame: 0,
                                   out int w, out int h);
        if (bgra == null) return null;

        var bmp = BitmapSource.Create(w, h, 96, 96, PixelFormats.Bgra32, null, bgra, w * 4);
        bmp.Freeze();

        var image = new Image
        {
            Source = bmp,
            Width = w,
            Height = h,
            Stretch = Stretch.Fill,
            VerticalAlignment = VerticalAlignment.Center,
        };
        RenderOptions.SetBitmapScalingMode(image, GameUi.SpriteScaling);
        return image;
    }

    // ── 틀 ─────────────────────────────────────────────────────────────────────

    private UIElement Build()
    {
        var keep = GameUi.PushButton("간직한다", () =>
        {
            LandFormationEdits.Set(_shape, _name.Text.Trim(), _slots);
            Refill();
        }, 110);

        var back = GameUi.PushButton("되돌린다", () =>
        {
            LandFormationEdits.Reset(_shape);
            Refill();
        }, 110);

        var close = GameUi.PushButton("닫기", Close, 96);
        close.Margin = new Thickness(20, 0, 0, 0);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 10, 0, 0),
            Children = { keep, back, close },
        };

        var naming = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 0, 0, 10),
            Children =
            {
                new TextBlock
                {
                    Text = "이름",
                    Width = 40,
                    Foreground = GameUi.Text,
                    VerticalAlignment = VerticalAlignment.Center,
                },
                _name,
            },
        };

        var right = new DockPanel { Margin = new Thickness(12, 0, 0, 0) };
        DockPanel.SetDock(_head, Dock.Top);
        DockPanel.SetDock(_note, Dock.Top);
        DockPanel.SetDock(naming, Dock.Top);
        DockPanel.SetDock(buttons, Dock.Bottom);
        right.Children.Add(_head);
        right.Children.Add(_note);
        right.Children.Add(naming);
        right.Children.Add(buttons);
        right.Children.Add(new ScrollViewer
        {
            Content = _units,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        });

        var body = new DockPanel { Margin = new Thickness(12) };
        DockPanel.SetDock(_nations, Dock.Left);
        body.Children.Add(_nations);
        body.Children.Add(right);

        var page = new DockPanel();
        var title = GameUi.TitleBar("부대 편성", Close);
        DockPanel.SetDock(title, Dock.Top);
        page.Children.Add(title);
        page.Children.Add(body);
        return page;
    }

    /// <summary>목록을 다시 채운다 — 진형 이름이 바뀌면 줄 글도 따라 바뀐다.</summary>
    private void Refill()
    {
        int at = _nations.SelectedIndex;
        _nations.Items.Clear();
        Fill();
        _nations.SelectedIndex = Math.Clamp(at, 0, Math.Max(0, _nations.Items.Count - 1));
    }
}
