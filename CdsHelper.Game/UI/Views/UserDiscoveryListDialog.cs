using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using CdsHelper.Game.Local.Helpers;

namespace CdsHelper.Game.UI.Views;

/// <summary>
/// 도시락 「추가 발견물」을 누르면 먼저 뜨는 목록 창 — 지어 둔 커스텀 발견물을 늘어놓고, 고르면 오른쪽에 그림 · 설명을 보인다.
/// 「발견물 추가」 · 「고치기」가 짓는 창(<see cref="UserDiscoveryDialog"/>)으로 간다(요청). 원본에 없는 창이다.
/// </summary>
/// <remarks>모양은 모드 창을 따른다 — 밤색 판 · 제목 띠 · 왼쪽 줄 목록 · 오른쪽 설명 칸 · 게임 단추.</remarks>
public sealed class UserDiscoveryListDialog : GameWindow
{
    private const double ListWidth = 290, TipWidth = 420, BodyHeight = 420;

    private static readonly Brush Hover = Frozen(Color.FromArgb(0x30, 0xFF, 0xFF, 0xFF));
    private static readonly Brush Picked = Frozen(Color.FromArgb(0x70, 0xB0, 0x80, 0x50));

    private readonly Func<DiscoveryAreaPicker.Map?>? _map;
    private readonly Engine.Game? _game;

    private readonly StackPanel _rows = new() { Margin = new Thickness(0, 2, 0, 2) };
    private readonly TextBlock _tipName = new()
    {
        Foreground = GameUi.Text, FontWeight = FontWeights.Bold, FontSize = 15, Margin = new Thickness(0, 0, 0, 6),
        TextWrapping = TextWrapping.Wrap,
    };
    private readonly TextBlock _tipText = new() { Foreground = GameUi.Text, FontSize = 13, LineHeight = 20, TextWrapping = TextWrapping.Wrap };
    private readonly Image _picture = new() { Width = 320, Height = 240, Stretch = Stretch.Uniform, HorizontalAlignment = HorizontalAlignment.Left };
    private readonly Border _pictureFrame;

    /// <summary>고른 줄의 UUID 와 그 줄.</summary>
    private string? _picked;
    private readonly Dictionary<string, Border> _rowOf = [];

    private UserDiscoveryListDialog(Func<DiscoveryAreaPicker.Map?>? map, Engine.Game? game)
    {
        _map = map;
        _game = game;
        Title = "추가 발견물";
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.WidthAndHeight;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        Background = GameUi.Back;

        var title = GameUi.TitleBar("추가 발견물", Close);
        GameUi.EnableDrag(this, title);

        var list = new Border
        {
            Width = ListWidth, Height = BodyHeight, Margin = new Thickness(12, 10, 8, 4),
            BorderBrush = GameUi.Edge, BorderThickness = new Thickness(1),
            Background = new SolidColorBrush(Color.FromArgb(0x20, 0, 0, 0)),
            Child = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = _rows },
        };

        _pictureFrame = new Border
        {
            BorderBrush = GameUi.Edge, BorderThickness = new Thickness(1), Margin = new Thickness(0, 0, 0, 8),
            HorizontalAlignment = HorizontalAlignment.Left, Child = _picture,
        };
        var tip = new StackPanel();
        tip.Children.Add(_tipName);
        tip.Children.Add(_pictureFrame);
        tip.Children.Add(_tipText);
        var side = new Border
        {
            Width = TipWidth, Height = BodyHeight, Margin = new Thickness(0, 10, 12, 4), Padding = new Thickness(10, 8, 10, 8),
            Background = new SolidColorBrush(Color.FromArgb(0x30, 0, 0, 0)),
            BorderBrush = GameUi.Edge, BorderThickness = new Thickness(1),
            Child = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = tip },
        };

        var body = new StackPanel { Orientation = Orientation.Horizontal };
        body.Children.Add(list);
        body.Children.Add(side);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 6, 0, 12) };
        buttons.Children.Add(new GameButton("발견물 추가", () => OpenEditor(add: true), width: 130));
        buttons.Children.Add(new GameButton("고치기", () => OpenEditor(add: false), width: 110));
        buttons.Children.Add(new GameButton("닫기", Close, width: 110));

        var stack = new StackPanel();
        stack.Children.Add(title);
        stack.Children.Add(body);
        stack.Children.Add(buttons);
        Content = new Border
        {
            Background = GameUi.Back, BorderBrush = GameUi.Edge, BorderThickness = new Thickness(2), Margin = new Thickness(4),
            Child = stack,
        };

        KeyDown += (_, e) => { if (e.Key is Key.Escape) Close(); };
        Fill(null);
    }

    private static Brush Frozen(Color c)
    {
        var b = new SolidColorBrush(c);
        b.Freeze();
        return b;
    }

    /// <summary>목록을 다시 채우고 그 UUID 를(없으면 첫 줄을) 고른다.</summary>
    private void Fill(string? pick)
    {
        _rows.Children.Clear();
        _rowOf.Clear();
        _picked = null;
        var all = UserDiscoveries.All.OrderBy(l => l.Entry.Name, StringComparer.Ordinal).ToList();
        foreach (var l in all)
        {
            string uuid = l.Entry.Uuid;
            var row = new Border
            {
                Padding = new Thickness(10, 5, 10, 5), Cursor = Cursors.Hand, Background = Brushes.Transparent,
                Child = new TextBlock
                {
                    Text = l.Entry.Name + (UserDiscoveries.IsLocked(l.Entry) ? "  🔒" : ""),
                    Foreground = GameUi.Text, FontSize = 14, TextTrimming = TextTrimming.CharacterEllipsis,
                },
            };
            row.MouseEnter += (_, _) => { if (_picked != uuid) row.Background = Hover; };
            row.MouseLeave += (_, _) => { if (_picked != uuid) row.Background = Brushes.Transparent; };
            row.MouseLeftButtonDown += (_, e) =>
            {
                e.Handled = true;
                if (e.ClickCount >= 2) OpenEditor(add: false);
                else Pick(uuid);
            };
            _rowOf[uuid] = row;
            _rows.Children.Add(row);
        }

        if (all.Count == 0)
        {
            _rows.Children.Add(new TextBlock
            {
                Text = "지어 둔 발견물이 없습니다.", Foreground = GameUi.Text, Opacity = 0.7, FontSize = 13,
                Margin = new Thickness(10, 8, 10, 0), TextWrapping = TextWrapping.Wrap,
            });
            ShowEmpty();
            return;
        }
        Pick(pick != null && _rowOf.ContainsKey(pick) ? pick : all[0].Entry.Uuid);
    }

    private const string EmptyText = "직접 지은 발견물을 게임에 넣는 곳입니다.\n\n「발견물 추가」를 누르면 새 발견물을 짓는 창이 열립니다. 자리 · 그림 · 장면 · 힌트를 정하고, 파일로 내보내거나 다른 분이 만든 것을 불러올 수도 있습니다.";
    private const string ModOffText = "모드 「추가 발견물」이 꺼져 있어 지은 발견물이 게임에 안 나옵니다.";

    private void ShowEmpty()
    {
        _tipName.Text = "추가 발견물";
        _picture.Source = null;
        _pictureFrame.Visibility = Visibility.Collapsed;
        // 글을 이어 붙이면 번역 표(lang/ja.json)와 안 맞는다 — 조각마다 Lang.T 를 거친다.
        _tipText.Text = Lang.T(EmptyText)
                        + (Local.Settings.GameSettings.ExtraDiscoveries ? "" : "\n\n" + Lang.T(ModOffText));
    }

    /// <summary>그 줄을 고르고 오른쪽에 그림 · 설명을 보인다.</summary>
    private void Pick(string uuid)
    {
        if (_picked != null && _rowOf.TryGetValue(_picked, out var old)) old.Background = Brushes.Transparent;
        _picked = uuid;
        if (_rowOf.TryGetValue(uuid, out var row)) row.Background = Picked;
        if (UserDiscoveries.All.FirstOrDefault(l => l.Entry.Uuid == uuid) is not { } l) { ShowEmpty(); return; }

        var e = l.Entry;
        _tipName.Text = e.Name;
        _picture.Source = l.PicturePath is { } path ? UserDiscoveryDialog.LoadPicture(path) : null;
        _pictureFrame.Visibility = _picture.Source != null ? Visibility.Visible : Visibility.Collapsed;
        string category = e.Category >= 0 && e.Category < DiscoveryTable.CategoryNames.Length ? DiscoveryTable.CategoryNames[e.Category] : "";
        var lines = new List<string>();
        lines.Add(string.Join(" · ", new[] { Lang.T(category), Lang.T($"보수 {e.Reward:N0}"), Lang.T(e.OnLand ? "육지" : "바다") }.Where(s => s.Length > 0)));
        if (e.Author.Length > 0) lines.Add(Lang.T($"만든 이 {e.Author}"));
        if (e.Description.Length > 0) lines.Add("\n" + e.Description);
        _tipText.Text = string.Join("\n", lines);
    }

    /// <summary>짓는 창을 연다 — 새로 짓거나 고른 것을 고친다. 닫으면 목록을 다시 채운다.</summary>
    private void OpenEditor(bool add)
    {
        if (!add && _picked == null) return;   // 고칠 것이 없다 — 「발견물 추가」로 짓는다
        var before = UserDiscoveries.All.Select(l => l.Entry.Uuid).ToHashSet();
        string? keep = _picked;
        UserDiscoveryDialog.Show(this, _map, _game, pick: add ? null : _picked, add: add);
        // 새로 지은 것이 있으면 그것을 고른다.
        Fill(UserDiscoveries.All.Select(l => l.Entry.Uuid).FirstOrDefault(u => !before.Contains(u)) ?? keep);
    }

    /// <summary>창을 띄운다.</summary>
    public static void Show(Window? owner, Func<DiscoveryAreaPicker.Map?>? map, Engine.Game? game = null)
    {
        var window = new UserDiscoveryListDialog(map, game);
        if (owner != null) window.Owner = owner;
        window.ShowDialog();
    }
}
