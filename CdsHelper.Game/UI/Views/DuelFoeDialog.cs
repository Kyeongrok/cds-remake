using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CdsHelper.Game.Local.Helpers;
using CdsHelper.Game.Local.Settings;
using CdsHelper.Support.Local.Models;

namespace CdsHelper.Game.UI.Views;

/// <summary>
/// 일기토 상대를 <b>인물표에서</b> 고르는 창 — 미니 게임에서만 연다.
/// </summary>
/// <remarks>
/// 예전에는 손으로 지은 넷(해적 두목·이슬람 제독·토벌대장·전설의 검객) 가운데 골랐다.
/// 값도 얼굴도 눈으로 고른 것이라 인물표와 아무 상관이 없었다. 여기서는 <b>표에 있는
/// 사람</b>을 그대로 세운다 — 얼굴도 능력도 기능도 그 사람 것이다.
///
/// 게임에 없는 화면이라 밤색 판이 아니라 <b>여느 개발 창</b>의 꼴로 짓는다 —
/// 모의전 창(<see cref="LandSparDialog"/>)과 같은 결이다.
/// </remarks>
internal sealed class DuelFoeDialog : GameWindow
{
    /// <summary>초상화를 거는 크기. 조각 그대로다.</summary>
    private const double FaceWidth = Portraits.Width, FaceHeight = Portraits.Height;

    /// <summary>
    /// 고를 수 있는 배경 — <c>asset/duel</c> 에 뽑아 둔 것 그대로다.
    /// </summary>
    /// <remarks>
    /// 배경마다 눈금판이 한 장씩 딸려 있다(<see cref="DuelArt.PanelFor"/>) — 눈금판은
    /// 제 팔레트가 없어 배경 것을 같이 쓰기 때문이다. 그래서 배경을 고르면 아래 판
    /// 빛깔도 같이 바뀐다.
    /// </remarks>
    private static readonly (string Key, string Name)[] Arenas =
    [
        ("duel-tavern", "술집"), ("duel-field", "초원"), ("duel-deck", "갑판"),
        ("duel-sand", "모래벌"), ("duel-wood", "숲"),
        ("duel-temple", "사원"), ("duel-mosque", "모스크"),
    ];

    private readonly ComboBox _arena = new() { Width = 140 };

    /// <summary>이름으로 걸러 내는 칸. 적는 대로 목록이 좁혀진다.</summary>
    private readonly TextBox _find = new() { Width = 150, VerticalContentAlignment = VerticalAlignment.Center };

    /// <summary>줄 세우는 차례.</summary>
    private readonly ComboBox _sort = new() { Width = 130 };

    private readonly ListBox _list = new()
    {
        Margin = new Thickness(14, 8, 14, 0),
        HorizontalContentAlignment = HorizontalAlignment.Stretch,
    };

    private readonly IReadOnlyList<PersonTable.Row> _people;
    private readonly Portraits? _faces;

    /// <summary>고르고 나면 그 사람. 물렀으면 null.</summary>
    private PersonTable.Row? _picked;

    /// <summary>줄 세우는 차례 — 목록 차례 그대로다.</summary>
    private static readonly string[] Sorts = ["명성 높은 차례", "무력 높은 차례", "무력 낮은 차례", "이름 차례"];

    /// <summary>능력 여섯 가운데 무력이 앉은 자리(<see cref="Ability.Names"/>).</summary>
    private const int MightAt = 2;

    private DuelFoeDialog(IReadOnlyList<PersonTable.Row> people, Portraits? faces)
    {
        _people = people;
        _faces = faces;

        Title = "일기토 — 상대 고르기";
        Width = 620;
        Height = 560;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var top = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(14, 10, 14, 0),
        };
        top.Children.Add(Label("배경", 40));
        foreach (var (_, name) in Arenas) _arena.Items.Add(name);
        _arena.SelectedIndex = 0;
        top.Children.Add(_arena);

        top.Children.Add(Label("차례", 44));
        foreach (string name in Sorts) _sort.Items.Add(name);
        _sort.SelectedIndex = 0;
        _sort.SelectionChanged += (_, _) => Fill();
        top.Children.Add(_sort);

        top.Children.Add(Label("이름", 44));
        _find.TextChanged += (_, _) => Fill();
        top.Children.Add(_find);

        // 두 번 누르면 그 자리에서 고른 것으로 친다 — 목록 창의 여느 결이다.
        var list = _list;
        list.MouseDoubleClick += (_, _) => Take(list);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(14),
        };
        var fight = new Button { Content = "붙는다", Padding = new Thickness(16, 3, 16, 3),
                                 Margin = new Thickness(0, 0, 8, 0), IsDefault = true };
        fight.Click += (_, _) => Take(list);
        buttons.Children.Add(fight);

        var stop = new Button { Content = "그만둔다", Padding = new Thickness(16, 3, 16, 3),
                                IsCancel = true };
        stop.Click += (_, _) => Close();
        buttons.Children.Add(stop);

        var page = new DockPanel();
        DockPanel.SetDock(top, Dock.Top);
        DockPanel.SetDock(buttons, Dock.Bottom);
        page.Children.Add(top);
        page.Children.Add(buttons);
        page.Children.Add(list);
        Content = page;

        Fill();
        _find.Focus();
    }

    /// <summary>앞에 붙이는 작은 이름표.</summary>
    private static TextBlock Label(string text, double width) => new()
    {
        Text = text,
        Width = width,
        VerticalAlignment = VerticalAlignment.Center,
        Margin = new Thickness(10, 0, 4, 0),
    };

    /// <summary>
    /// 목록을 다시 편다 — 이름으로 거르고, 차례대로 세우고, 최근에 싸운 이를 맨 위로 뺀다.
    /// </summary>
    /// <remarks>
    /// 최근에 싸운 이는 <b>아래 목록에서 뺀다</b> — 같은 사람이 두 줄로 서면 헷갈린다.
    /// 적어 두는 것은 <see cref="GameSettings.RecentDuelFoes"/> 이고 다섯까지 남는다.
    /// </remarks>
    private void Fill()
    {
        _list.Items.Clear();

        string want = _find.Text.Trim();
        var hits = _people.Where(who => want.Length == 0
                                     || who.Name.Contains(want, StringComparison.OrdinalIgnoreCase));

        var lined = (_sort.SelectedIndex switch
        {
            1 => hits.OrderByDescending(Might).ThenByDescending(who => who.Fame),
            2 => hits.OrderBy(Might).ThenByDescending(who => who.Fame),
            3 => hits.OrderBy(who => who.Name, StringComparer.CurrentCulture).ThenByDescending(who => who.Fame),
            _ => hits.OrderByDescending(who => who.Fame).ThenBy(who => who.Name),
        }).ToList();

        var recent = GameSettings.RecentDuelFoes
            .Select(name => lined.FirstOrDefault(who => who.Name == name))
            .Where(who => who != null)
            .Select(who => who!)
            .ToList();

        if (recent.Count > 0)
        {
            _list.Items.Add(Header("최근에 싸운 상대"));
            foreach (var who in recent) _list.Items.Add(Line(who));
            _list.Items.Add(Header(want.Length == 0 ? "그 밖의 사람" : $"「{want}」 이 든 사람"));
        }

        foreach (var who in lined.Except(recent)) _list.Items.Add(Line(who));

        // 머리글은 못 고르므로 사람 줄 가운데 첫 줄을 잡아 둔다.
        _list.SelectedItem = _list.Items.OfType<ListBoxItem>()
                                        .FirstOrDefault(one => one.Tag is PersonTable.Row);
    }

    /// <summary>그 사람의 무력.</summary>
    private static int Might(PersonTable.Row who) =>
        MightAt < who.Stats.Length ? who.Stats[MightAt] : 0;

    /// <summary>사람 한 줄.</summary>
    private ListBoxItem Line(PersonTable.Row who) => new() { Content = Card(who, _faces), Tag = who };

    /// <summary>목록을 가르는 머리글 — 고를 수 없는 줄이다.</summary>
    private static ListBoxItem Header(string text) => new()
    {
        Content = new TextBlock
        {
            Text = text,
            FontWeight = FontWeights.Bold,
            Margin = new Thickness(2, 4, 2, 4),
            Foreground = Brushes.DimGray,
        },
        Background = Brushes.WhiteSmoke,
        Focusable = false,
        IsHitTestVisible = false,
    };

    /// <summary>고른 줄을 집어 창을 닫는다. 고른 것이 없으면 그냥 둔다.</summary>
    private void Take(ListBox list)
    {
        if (list.SelectedItem is not ListBoxItem { Tag: PersonTable.Row who }) return;

        _picked = who;
        Close();
    }

    /// <summary>
    /// 한 사람의 줄 — <b>얼굴 · 이름 · 능력 여섯 · 지닌 기능</b>이다.
    /// </summary>
    /// <remarks>
    /// 기능은 <b>배운 것만</b> 적는다. 열셋을 다 늘어놓으면 줄이 길어져 고르기 나쁘고,
    /// 안 배운 것은 0 이라 볼 것이 없다.
    /// </remarks>
    private static UIElement Card(PersonTable.Row who, Portraits? faces)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(2) };
        row.Children.Add(Face(who, faces));

        var text = new StackPanel { Margin = new Thickness(10, 0, 0, 0) };
        text.Children.Add(new TextBlock
        {
            Text = who.Name,
            FontWeight = FontWeights.Bold,
            FontSize = 14,
        });
        text.Children.Add(Line($"나이 {who.Age}   명성 {who.Fame}"));
        text.Children.Add(Line(Abilities(who)));
        text.Children.Add(Line(Skills(who)));
        row.Children.Add(text);
        return row;
    }

    /// <summary>능력 여섯을 한 줄로 — 이름은 <see cref="Ability.Names"/> 차례 그대로다.</summary>
    private static string Abilities(PersonTable.Row who) =>
        string.Join("  ", Ability.Names.Select(
            (name, at) => $"{name} {(at < who.Stats.Length ? who.Stats[at] : 0)}"));

    /// <summary>배운 기능만. 하나도 없으면 그렇게 적는다.</summary>
    private static string Skills(PersonTable.Row who)
    {
        var learned = Skill.Names
            .Select((name, at) => (name, level: at < who.Skills.Length ? who.Skills[at] : 0))
            .Where(s => s.level > 0)
            .Select(s => $"{s.name} {s.level}");
        string joined = string.Join("  ", learned);
        return joined.Length > 0 ? joined : "익힌 기능 없음";
    }

    private static TextBlock Line(string text) => new()
    {
        Text = text,
        Foreground = Brushes.DimGray,
        Margin = new Thickness(0, 2, 0, 0),
    };

    /// <summary>얼굴 한 장. 못 읽으면 빈 칸으로 자리만 지킨다.</summary>
    private static UIElement Face(PersonTable.Row who, Portraits? faces)
    {
        var box = new Border
        {
            Width = FaceWidth,
            Height = FaceHeight,
            Background = Brushes.Black,
            BorderBrush = Brushes.Gray,
            BorderThickness = new Thickness(1),
        };
        if (faces?.TryGetBgra(who.Face, female: false) is not { } bgra) return box;

        var bmp = BitmapSource.Create(Portraits.Width, Portraits.Height, 96, 96,
                                      PixelFormats.Bgra32, null, bgra, Portraits.Width * 4);
        bmp.Freeze();

        var image = new Image { Source = bmp, Width = FaceWidth, Height = FaceHeight };
        RenderOptions.SetBitmapScalingMode(image, GameUi.SpriteScaling);
        RenderOptions.SetEdgeMode(image, EdgeMode.Aliased);
        box.Child = image;
        return box;
    }

    /// <summary>
    /// 상대를 고른다. 물렀거나 인물표를 못 읽었으면 null.
    /// </summary>
    /// <remarks>
    /// <b>명성이 높은 사람이 위</b>다 — 이름차례로 두면 누가 셀지가 안 보인다. 표에 있어도
    /// 이름이 없는 줄은 뺀다.
    /// </remarks>
    public static (PersonTable.Row Who, string Arena)? Ask(Window owner, Portraits? faces)
    {
        var table = PersonTable.Open();
        if (table == null) return null;

        var people = table.People
            .Where(who => who.Name.Length > 0)
            .OrderByDescending(who => who.Fame)
            .ThenBy(who => who.Name)
            .ToList();
        if (people.Count == 0) return null;

        var box = new DuelFoeDialog(people, faces) { Owner = owner };
        box.ShowDialog();
        if (box._picked is not { } who) return null;

        GameSettings.RememberDuelFoe(who.Name);

        int at = Math.Clamp(box._arena.SelectedIndex, 0, Arenas.Length - 1);
        return (who, Arenas[at].Key);
    }
}
