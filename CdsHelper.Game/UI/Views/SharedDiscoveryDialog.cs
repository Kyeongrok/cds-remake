using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CdsHelper.Game.Engine.Table;
using CdsHelper.Game.Local.Helpers;

namespace CdsHelper.Game.UI.Views;

/// <summary>
/// 「공유 발견물」 창 — 다른 분들이 클라우드에 올린 커스텀 발견물 목록. 고르면 그림 · 설명 · 만든 이를 보이고,
/// 「받아서 추가」로 내려받아 「불러오기」와 똑같이 들인다(<see cref="UserDiscoveries.Import"/>). 원본에 없는 것이다.
/// </summary>
/// <remarks>
/// 도시락 「추가 발견물」이 이 창을 먼저 띄우고, 「발견물 추가」로 짓는 창(<see cref="UserDiscoveryDialog"/>)에 간다(요청).
/// 모양은 모드 창을 따른다 — 밤색 판 · 제목 띠 · 왼쪽 줄 목록 · 오른쪽 설명 칸 · 게임 단추 · 어두운 스크롤바.
/// 줄마다 좋아요 수 · 받은 수 · 추가했는지를 바로 보이고, 위아래 글쇠로 고르고 Enter 로 받는다.
/// </remarks>
public sealed class SharedDiscoveryDialog : GameWindow
{
    private const double ListWidth = 360, TipWidth = 470, BodyHeight = 470;

    private static readonly Brush Hover = Frozen(Color.FromArgb(0x30, 0xFF, 0xFF, 0xFF));
    private static readonly Brush Picked = Frozen(Color.FromArgb(0x70, 0xB0, 0x80, 0x50));
    private static readonly Brush Dim = Frozen(Color.FromRgb(0xB8, 0xA8, 0x98));
    private static readonly Brush HaveInk = Frozen(Color.FromRgb(0x9C, 0xD0, 0x8C));
    private static readonly Brush NewerInk = Frozen(Color.FromRgb(0xF0, 0xC0, 0x60));
    private static readonly Brush LikeInk = Frozen(Color.FromRgb(0xF0, 0x80, 0x80));

    private readonly StackPanel _rows = new() { Margin = new Thickness(0, 2, 0, 2) };
    private readonly ScrollViewer _rowScroll;
    private readonly TextBlock _tipName = new()
    {
        Foreground = GameUi.Text, FontWeight = FontWeights.Bold, FontSize = 15, Margin = new Thickness(0, 0, 0, 6), TextWrapping = TextWrapping.Wrap,
    };
    private readonly TextBlock _detail = new() { Foreground = GameUi.Text, FontSize = 13, LineHeight = 20, TextWrapping = TextWrapping.Wrap };
    private readonly Image _picture = new() { Width = 320, Height = 240, Stretch = Stretch.Uniform, HorizontalAlignment = HorizontalAlignment.Left };
    private readonly Border _pictureFrame;
    private readonly TextBlock _status = new()
    {
        Foreground = Dim, FontSize = 12, Margin = new Thickness(14, 0, 14, 10), TextWrapping = TextWrapping.Wrap, MaxWidth = ListWidth + TipWidth + 20,
    };

    /// <summary>내려받는 중인지 — 그동안 + 를 또 눌러도 안 받는다.</summary>
    private bool _getting;

    /// <summary>
    /// 좋아요 하트 — 오른쪽 칸 이름 줄의 오른쪽 끝에 둔다. 누르면 누르거나 거둔다(아래 단추 줄에 두니 어색했다, 요청).
    /// </summary>
    private readonly Border _heart = new()
    {
        Cursor = Cursors.Hand, Padding = new Thickness(8, 2, 8, 2), CornerRadius = new CornerRadius(10),
        BorderThickness = new Thickness(1), VerticalAlignment = VerticalAlignment.Top, Visibility = Visibility.Collapsed,
    };
    private readonly TextBlock _heartText = new() { FontSize = 15, Foreground = LikeInk };
    private bool _liking;
    private readonly Engine.Game? _game;
    private readonly Func<DiscoveryAreaPicker.Map?>? _map;
    private List<DiscoveryShare.Shared> _shares = [];
    private readonly List<(DiscoveryShare.Shared Share, Border Row)> _rowOf = [];
    private int _picked = -1;
    private int _pictureAsk;

    /// <summary>하나라도 들였으면 그 UUID — 닫은 뒤 부른 창이 목록을 다시 읽는다.</summary>
    public string? Imported { get; private set; }

    private SharedDiscoveryDialog(Engine.Game? game, Func<DiscoveryAreaPicker.Map?>? map)
    {
        _game = game;
        _map = map;
        Title = "공유 발견물";
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.WidthAndHeight;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        Background = GameUi.Back;
        this.DarkScrollBars();

        var title = GameUi.TitleBar("공유 발견물", Close);
        GameUi.EnableDrag(this, title);

        _rowScroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = _rows };
        var list = new Border
        {
            Width = ListWidth, Height = BodyHeight, Margin = new Thickness(12, 10, 8, 4),
            BorderBrush = GameUi.Edge, BorderThickness = new Thickness(1),
            Background = new SolidColorBrush(Color.FromArgb(0x20, 0, 0, 0)),
            Child = _rowScroll,
        };

        _pictureFrame = new Border
        {
            BorderBrush = GameUi.Edge, BorderThickness = new Thickness(1), Margin = new Thickness(0, 0, 0, 8),
            HorizontalAlignment = HorizontalAlignment.Left, Child = _picture, Visibility = Visibility.Collapsed,
        };
        var tip = new StackPanel();
        // 이름 줄 — 왼쪽 이름, 오른쪽 끝 좋아요 하트.
        _heart.Child = _heartText;
        _heart.BorderBrush = LikeInk;
        _heart.MouseEnter += (_, _) => _heart.Background = Hover;
        _heart.MouseLeave += (_, _) => _heart.Background = Brushes.Transparent;
        _heart.MouseLeftButtonDown += (_, e) => { e.Handled = true; LikeSelected(); };
        _heart.Background = Brushes.Transparent;
        var head = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(_heart, Dock.Right);
        head.Children.Add(_heart);
        head.Children.Add(_tipName);
        tip.Children.Add(head);
        tip.Children.Add(_pictureFrame);
        tip.Children.Add(_detail);
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

        // 받아서 추가는 왼쪽 줄의 + 로, 좋아요는 오른쪽 칸의 하트로, 신고는 줄의 오른쪽 클릭 메뉴로 옮겼다 — 아래에는 창 단추만 둔다(요청).
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 6, 0, 6) };
        // 「발견물 추가」 — 짓는 창을 연다. 새 줄은 그 창의 「새 발견물」로 짓고, 「클라우드에 올리기」로 이 목록에 올린다.
        buttons.Children.Add(new GameButton("발견물 추가", OpenEditor, width: 130));
        buttons.Children.Add(new GameButton("새로 고침", () => _ = Load(), width: 120));
        buttons.Children.Add(new GameButton("닫기", Close, width: 100));

        var stack = new StackPanel();
        stack.Children.Add(title);
        stack.Children.Add(body);
        stack.Children.Add(buttons);
        stack.Children.Add(_status);
        Content = new Border
        {
            Background = GameUi.Back, BorderBrush = GameUi.Edge, BorderThickness = new Thickness(2), Margin = new Thickness(4),
            Child = stack,
        };

        // 위아래 · PageUp/Down · Home/End 로 고르고 Enter 로 받는다. Esc 는 닫는다.
        PreviewKeyDown += (_, e) =>
        {
            int count = _rowOf.Count;
            int to = e.Key switch
            {
                Key.Up => _picked - 1,
                Key.Down => _picked + 1,
                Key.PageUp => _picked - 8,
                Key.PageDown => _picked + 8,
                Key.Home => 0,
                Key.End => count - 1,
                _ => int.MinValue,
            };
            if (to != int.MinValue)
            {
                if (count > 0) Pick(Math.Clamp(to, 0, count - 1));
                e.Handled = true;
            }
            else if (e.Key == Key.Enter && _picked >= 0) { GetSelected(); e.Handled = true; }
            else if (e.Key == Key.Escape) { Close(); e.Handled = true; }
        };
        Loaded += (_, _) => _ = Load();
        Pick(-1);
    }

    private static Brush Frozen(Color c)
    {
        var b = new SolidColorBrush(c);
        b.Freeze();
        return b;
    }

    private DiscoveryShare.Shared? Selected => _picked >= 0 && _picked < _rowOf.Count ? _rowOf[_picked].Share : null;

    private static bool Have(DiscoveryShare.Shared s) => UserDiscoveries.All.Any(l => l.Entry.Uuid == s.Uuid);

    private async Task Load()
    {
        _status.Text = Lang.T("목록을 받는 중입니다…");
        var got = await DiscoveryShare.List();
        if (got == null)
        {
            _status.Text = Lang.T("목록을 받지 못했습니다.") + " " + DiscoveryShare.ListFailure;
            if (_rowOf.Count == 0) ShowEmpty(Lang.T("목록을 받지 못했습니다."));
            return;
        }
        string? keep = Selected?.Uuid;
        _shares = got;
        foreach (var s in _shares)
            if (Have(s)) Adopt(s);
        Fill(keep);
        _status.Text = _shares.Count == 0
            ? Lang.T("아직 올라온 발견물이 없습니다.")
            : $"{_shares.Count}" + Lang.T("개 — ♥ 좋아요 · ⬇ 받은 수. 위아래 글쇠로 고르고 Enter 로 받습니다.");
    }

    /// <summary>목록 줄을 다시 짓고 그 UUID 를(없으면 첫 줄을) 고른다.</summary>
    private void Fill(string? keep)
    {
        _rows.Children.Clear();
        _rowOf.Clear();
        _picked = -1;
        foreach (var s in _shares)
        {
            int index = _rowOf.Count;
            var row = Row(s, index);
            row.MouseEnter += (_, _) => { if (_picked != index) row.Background = Hover; };
            row.MouseLeave += (_, _) => { if (_picked != index) row.Background = Brushes.Transparent; };
            row.MouseLeftButtonDown += (_, e) =>
            {
                e.Handled = true;
                Pick(index);
                if (e.ClickCount >= 2) GetSelected();
            };
            _rowOf.Add((s, row));
            _rows.Children.Add(row);
        }
        if (_rowOf.Count == 0) { ShowEmpty(Lang.T("아직 올라온 발견물이 없습니다.")); return; }
        int at = _rowOf.FindIndex(r => r.Share.Uuid == keep);
        Pick(at >= 0 ? at : 0);
    }

    /// <summary>
    /// 한 줄 — 이름 · 만든 이 · 판, 오른쪽에 ♥ 좋아요 · ⬇ 받은 수, 맨 오른쪽에 + (받아서 추가) 또는 ✓ (추가됨).
    /// + 에 커서를 올리면 「추가」 툴팁이 뜬다(요청).
    /// </summary>
    private Border Row(DiscoveryShare.Shared s, int index)
    {
        bool have = Have(s);
        string author = s.Author.Length > 0 ? s.Author : Lang.T("이름 없음");
        bool newer = have && Newer(s);
        var (mark, ink) = s.Mine ? (Lang.T("내 것"), NewerInk)
            : newer ? (Lang.T("새 버전"), NewerInk)
            : ("", Dim);

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var left = new StackPanel();
        left.Children.Add(new TextBlock { Text = s.Name, Foreground = GameUi.Text, FontSize = 14, TextTrimming = TextTrimming.CharacterEllipsis });
        left.Children.Add(new TextBlock { Text = $"{author}  v{s.Revision}", Foreground = Dim, FontSize = 11, TextTrimming = TextTrimming.CharacterEllipsis });
        grid.Children.Add(left);

        var right = new StackPanel { HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(8, 0, 0, 0) };
        var counts = new TextBlock { FontSize = 12, HorizontalAlignment = HorizontalAlignment.Right };
        counts.Inlines.Add(new System.Windows.Documents.Run((s.Liked ? "♥ " : "♡ ") + s.Likes) { Foreground = LikeInk });
        counts.Inlines.Add(new System.Windows.Documents.Run($"   ⬇ {s.Downloads}") { Foreground = GameUi.Text });
        right.Children.Add(counts);
        if (mark.Length > 0)
            right.Children.Add(new TextBlock { Text = mark, Foreground = ink, FontSize = 11, FontWeight = FontWeights.Bold, HorizontalAlignment = HorizontalAlignment.Right });
        Grid.SetColumn(right, 1);
        grid.Children.Add(right);

        // 맨 오른쪽 — 안 가졌거나 새 판이 있으면 + (누르면 받는다), 가졌으면 ✓.
        bool canGet = !have || newer;
        var plus = new Border
        {
            Width = 26, Height = 26, CornerRadius = new CornerRadius(13), Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center,
            BorderThickness = new Thickness(1), BorderBrush = canGet ? (newer ? NewerInk : HaveInk) : Dim, Background = Brushes.Transparent,
            Cursor = canGet ? Cursors.Hand : Cursors.Arrow,
            ToolTip = Lang.T(canGet ? (newer ? "새 버전 받기" : "추가") : "추가됨"),
            Child = new TextBlock
            {
                Text = canGet ? "+" : "✓", FontSize = canGet ? 18 : 13, FontWeight = FontWeights.Bold,
                Foreground = canGet ? (newer ? NewerInk : HaveInk) : Dim,
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, canGet ? -3 : 0, 0, 0),
            },
        };
        if (canGet)
        {
            plus.MouseEnter += (_, _) => plus.Background = Hover;
            plus.MouseLeave += (_, _) => plus.Background = Brushes.Transparent;
            plus.MouseLeftButtonDown += (_, e) =>
            {
                e.Handled = true;
                Pick(index);
                GetSelected();
            };
        }
        Grid.SetColumn(plus, 2);
        grid.Children.Add(plus);

        var line = new Border { Padding = new Thickness(10, 4, 10, 4), Cursor = Cursors.Hand, Background = Brushes.Transparent, Child = grid };
        // 오른쪽 클릭 메뉴 — 내가 올린 줄은 「내 공유 내리기」, 남의 줄은 「신고」(단추 · 아이콘으로 두니 어색했다, 요청).
        var act = new MenuItem { Header = Lang.T(s.Mine ? "내 공유 내리기" : "신고"), Foreground = GameUi.Text };
        act.Click += (_, _) =>
        {
            Pick(index);
            if (s.Mine) RemoveSelected();
            else ReportSelected();
        };
        line.ContextMenu = new ContextMenu
        {
            Background = GameUi.Back, BorderBrush = GameUi.DarkFieldEdge, Foreground = GameUi.Text, Items = { act },
        };
        line.PreviewMouseRightButtonDown += (_, _) => Pick(index);
        return line;
    }

    private void ShowEmpty(string why)
    {
        _tipName.Text = Lang.T("공유 발견물");
        _picture.Source = null;
        _pictureFrame.Visibility = Visibility.Collapsed;
        _detail.Text = why + "\n\n" + Lang.T("「발견물 추가」로 직접 발견물을 지을 수 있습니다. 지은 발견물은 짓는 창의 「클라우드에 올리기」로 다른 분들과 나눌 수 있습니다.");
        Pick(-1);
    }

    /// <summary>
    /// 이미 가진 것의 공유 상태를 맞춘다 — 남의 것이면 「받은 것」(고칠 수 없음)으로 적는다. 이 기능 전에 받아 둔 것은
    /// 공유 상태가 없어 지금 판으로 적는다(몇 판을 받았는지 몰라서).
    /// </summary>
    private static void Adopt(DiscoveryShare.Shared s)
    {
        var state = DiscoveryShare.StateOf(s.Uuid);
        if (s.Mine)
        {
            if (state is not { Mine: true }) DiscoveryShare.SetState(s.Uuid, new DiscoveryShare.LocalState(true, s.Revision, ""));
        }
        else if (state is not { Mine: false }) DiscoveryShare.SetState(s.Uuid, new DiscoveryShare.LocalState(false, s.Revision, ""));
    }

    /// <summary>가진 남의 것보다 새 판이 올라왔는지.</summary>
    private static bool Newer(DiscoveryShare.Shared s) =>
        !s.Mine && DiscoveryShare.StateOf(s.Uuid) is { Mine: false } state && s.Revision > state.Rev;

    /// <summary>그 줄을 고르고(−1 이면 아무것도) 오른쪽 칸 · 단추를 맞춘다.</summary>
    private void Pick(int index)
    {
        if (_picked >= 0 && _picked < _rowOf.Count) _rowOf[_picked].Row.Background = Brushes.Transparent;
        _picked = index;
        if (index >= 0 && index < _rowOf.Count)
        {
            var row = _rowOf[index].Row;
            row.Background = Picked;
            row.BringIntoView();
        }
        ShowSelected();
    }

    private async void ShowSelected()
    {
        var s = Selected;
        ShowHeart(s);
        bool have = s != null && Have(s);
        if (s == null) return;

        _tipName.Text = s.Name;
        _picture.Source = null;
        _pictureFrame.Visibility = s.HasPicture ? Visibility.Visible : Visibility.Collapsed;
        string head = Head(s, have);
        _detail.Text = head;
        ShowHistory(s, head);

        if (!s.HasPicture) return;
        int ask = ++_pictureAsk;
        var bytes = await DiscoveryShare.Picture(s.Uuid, s.Revision);
        if (ask != _pictureAsk || bytes == null) return;
        try
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.StreamSource = new MemoryStream(bytes);
            image.EndInit();
            image.Freeze();
            _picture.Source = image;
        }
        catch (Exception ex) when (ex is NotSupportedException or IOException or InvalidOperationException) { }
    }

    /// <summary>오른쪽 칸의 글 — 갈래 · 만든 이 · 받은 수 · 좋아요 · 가졌는지 · 설명.</summary>
    private static string Head(DiscoveryShare.Shared s, bool have)
    {
        string category = s.Category >= 0 && s.Category < DiscoveryTable.CategoryNames.Length ? Lang.T(DiscoveryTable.CategoryNames[s.Category]) : "";
        var text = new System.Text.StringBuilder();
        if (category.Length > 0) text.AppendLine(category);
        text.AppendLine($"{Lang.T("만든 이")}: {(s.Author.Length > 0 ? s.Author : Lang.T("이름 없음"))}");
        text.AppendLine($"{Lang.T("라이선스")}: {s.License}");
        if (s.WantsOfficial) text.AppendLine(Lang.T("정식 버전에 넣어도 좋다고 한 발견물입니다."));
        text.AppendLine($"{Lang.T("크기")}: {s.Size / 1024.0:0.#} KB{(s.HasMovie ? " · " + Lang.T("동영상 있음") : "")}");
        text.AppendLine($"♥ {s.Likes} · ⬇ {s.Downloads} · {Lang.T("고친 날")}: {(s.UpdatedAt.Length >= 10 ? s.UpdatedAt[..10] : s.UpdatedAt)} (v{s.Revision})");
        text.AppendLine(s.Mine && have ? Lang.T("내가 올린 발견물입니다 — 받으면 내 PC 의 것을 덮어쓸지 묻습니다.")
            : !have ? Lang.T("아직 추가하지 않았습니다 — 왼쪽 줄의 + (또는 Enter)로 게임에 넣습니다.")
            : Newer(s) ? $"{Lang.T("가진 판")}: v{DiscoveryShare.StateOf(s.Uuid)?.Rev} → {Lang.T("새 판")}: v{s.Revision} — {Lang.T("왼쪽 줄의 + 로 바꿉니다.")}"
            : Lang.T("이미 추가한 발견물입니다(최신)."));
        if (!s.Mine) text.AppendLine(Lang.T("다른 분이 올린 발견물은 받아도 고칠 수 없습니다 — 「복제」해서 내 발견물로 고쳐 쓸 수 있습니다."));
        text.AppendLine();
        text.Append(s.Description);
        return text.ToString();
    }

    /// <summary>판 이력을 받아 설명 아래에 붙인다 — 예전 받는 곳이면 안 붙인다.</summary>
    private async void ShowHistory(DiscoveryShare.Shared s, string head)
    {
        int ask = _pictureAsk;
        var revisions = await DiscoveryShare.History(s.Uuid);
        if (ask != _pictureAsk || Selected?.Uuid != s.Uuid || revisions is not { Count: > 0 }) return;
        var text = new System.Text.StringBuilder(head);
        text.AppendLine();
        text.AppendLine();
        text.AppendLine(Lang.T("판 이력"));
        foreach (var r in revisions)
            text.AppendLine($"  v{r.Rev}  {(r.CreatedAt.Length >= 10 ? r.CreatedAt[..10] : r.CreatedAt)}  {(r.Note.Length > 0 ? r.Note : "—")}");
        _detail.Text = text.ToString().TrimEnd();
    }

    private async void GetSelected()
    {
        if (Selected is not { } s || _getting) return;
        _getting = true;
        _status.Text = Lang.T("내려받는 중입니다…");
        string? path = await DiscoveryShare.Download(s.Uuid);
        _getting = false;
        if (path == null) { _status.Text = Lang.T("내려받지 못했습니다."); return; }
        try
        {
            // 받은 남의 것은 고칠 수 없으니 새 판으로 묻지 않고 바꾼다. 내가 올린 것은 내 PC 에서 고친 것이 있을 수 있어 묻는다.
            bool update = !s.Mine && DiscoveryShare.IsOthers(s.Uuid);
            var (entry, message) = UserDiscoveries.Import(path,
                (had, got) => update || ConfirmDialog.Ask(this, $"같은 발견물 「{had.Name}」 이 이미 있습니다. 받은 「{got.Name}」 으로 덮어쓸까요?", "공유 발견물"),
                (had, got) => ConfirmDialog.Ask(this, $"같은 아이템 「{had.Name}」 이 이미 있습니다. 받은 「{got.Name}」 으로 덮어쓸까요?", "공유 발견물"),
                _game?.Books);
            if (entry != null)
            {
                Imported = entry.Uuid;
                DiscoveryShare.SetState(entry.Uuid, new DiscoveryShare.LocalState(s.Mine, s.Revision, s.Mine ? DiscoveryShare.ContentHash(entry.Uuid) : ""));
            }
            _status.Text = message.Replace("\n", " ");
        }
        finally
        {
            try { File.Delete(path); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
        await Load();
    }

    /// <summary>하트를 그 발견물의 좋아요 수 · 눌렀는지로 맞춘다. 고른 것이 없으면 숨긴다.</summary>
    private void ShowHeart(DiscoveryShare.Shared? s)
    {
        _heart.Visibility = s == null ? Visibility.Collapsed : Visibility.Visible;
        if (s == null) return;
        _heartText.Text = (s.Liked ? "♥ " : "♡ ") + s.Likes;
        _heart.ToolTip = Lang.T(s.Liked ? "좋아요 취소" : "좋아요");
    }

    /// <summary>좋아요를 누르거나 거둔다 — 받는 곳이 답한 수로 그 줄만 고친다.</summary>
    private async void LikeSelected()
    {
        if (Selected is not { } s) return;
        bool on = !s.Liked;
        if (_liking) return;
        _liking = true;
        int? likes = await DiscoveryShare.Like(s.Uuid, on);
        _liking = false;
        if (likes == null) { _status.Text = Lang.T("좋아요를 보내지 못했습니다."); return; }
        int at = _shares.FindIndex(x => x.Uuid == s.Uuid);
        if (at >= 0) _shares[at] = s with { Likes = likes.Value, Liked = on };
        Fill(s.Uuid);
        _status.Text = on ? Lang.T("좋아요를 눌렀습니다.") : Lang.T("좋아요를 거뒀습니다.");
    }

    private async void ReportSelected()
    {
        if (Selected is not { } s) return;
        if (!ConfirmDialog.Ask(this, $"「{s.Name}」 을 신고할까요? 여러 분이 신고하면 목록에서 내려갑니다.", "신고")) return;
        _status.Text = await DiscoveryShare.Report(s.Uuid, "") ? Lang.T("신고했습니다.") : Lang.T("신고하지 못했습니다.");
    }

    private async void RemoveSelected()
    {
        if (Selected is not { Mine: true } s) return;
        if (!ConfirmDialog.Ask(this, $"「{s.Name}」 을 공유 목록에서 내릴까요? 내 게임의 발견물은 그대로 남습니다.", "내 공유 내리기")) return;
        if (await DiscoveryShare.Delete(s.Uuid))
        {
            DiscoveryShare.ClearState(s.Uuid);
            _status.Text = Lang.T("공유 목록에서 내렸습니다.");
        }
        else _status.Text = Lang.T("내리지 못했습니다.");
        await Load();
    }

    /// <summary>
    /// 「발견물 추가」 — 짓는 창을 연다. 줄은 짓는 창의 「새 발견물」을 눌러야 생긴다(요청 — 열자마자 빈 줄이 생겼다).
    /// 닫으면 목록의 「추가됨」 · 「내 것」을 다시 맞춘다.
    /// </summary>
    private void OpenEditor()
    {
        string? keep = Selected?.Uuid;
        UserDiscoveryDialog.Show(this, _map, _game);
        Fill(keep);
    }

    /// <summary>창을 띄운다. 들인 발견물의 UUID(없으면 null).</summary>
    /// <param name="map">「발견물 추가」로 연 짓는 창이 자리를 고를 세계 지도.</param>
    public static string? Open(Window owner, Engine.Game? game, Func<DiscoveryAreaPicker.Map?>? map = null)
    {
        var window = new SharedDiscoveryDialog(game, map) { Owner = owner };
        window.ShowDialog();
        return window.Imported;
    }
}
