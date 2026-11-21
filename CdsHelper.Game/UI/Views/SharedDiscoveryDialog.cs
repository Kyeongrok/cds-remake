using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CdsHelper.Game.Engine.Table;
using CdsHelper.Game.Local.Helpers;

namespace CdsHelper.Game.UI.Views;

/// <summary>
/// 「공유 발견물」 창 — 다른 분들이 클라우드에 올린 커스텀 발견물 목록. 고르면 그림 · 설명 · 만든 이를 보이고,
/// 「받아서 추가」로 내려받아 「불러오기」와 똑같이 들인다(<see cref="UserDiscoveries.Import"/>). 원본에 없는 것이다.
/// </summary>
public sealed class SharedDiscoveryDialog : GameWindow
{
    private readonly ListBox _list = new() { Width = 300, Margin = new Thickness(10, 10, 6, 6) };
    private readonly Image _picture = new() { Width = 320, Height = 240, Stretch = Stretch.Uniform, HorizontalAlignment = HorizontalAlignment.Left };
    private readonly TextBox _detail = new()
    {
        IsReadOnly = true, TextWrapping = TextWrapping.Wrap, BorderThickness = new Thickness(0), FontSize = 13,
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Margin = new Thickness(0, 8, 0, 0),
    };
    private readonly TextBlock _status = new() { Margin = new Thickness(10, 0, 10, 8), Foreground = Brushes.DimGray, TextWrapping = TextWrapping.Wrap };
    private readonly Button _get, _report, _remove;
    private readonly Engine.Game? _game;
    private List<DiscoveryShare.Shared> _shares = [];
    private int _pictureAsk;

    /// <summary>하나라도 들였으면 그 UUID — 닫은 뒤 부른 창이 목록을 다시 읽는다.</summary>
    public string? Imported { get; private set; }

    private SharedDiscoveryDialog(Engine.Game? game)
    {
        _game = game;
        Title = "공유 발견물";
        Width = 900;
        Height = 640;
        Background = Brushes.White;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        _get = Push("받아서 추가", GetSelected);
        _report = Push("신고…", ReportSelected);
        _remove = Push("내 공유 내리기", RemoveSelected);
        var refresh = Push("새로 고침", () => _ = Load());
        var close = Push("닫기", Close);
        var bar = new WrapPanel { Margin = new Thickness(10, 0, 10, 4), Children = { _get, _report, _remove, refresh, close } };

        var right = new DockPanel { Margin = new Thickness(6, 10, 10, 6) };
        DockPanel.SetDock(_picture, Dock.Top);
        right.Children.Add(_picture);
        right.Children.Add(_detail);

        var body = new DockPanel();
        DockPanel.SetDock(_list, Dock.Left);
        body.Children.Add(_list);
        body.Children.Add(right);

        var page = new DockPanel();
        DockPanel.SetDock(_status, Dock.Bottom);
        DockPanel.SetDock(bar, Dock.Bottom);
        page.Children.Add(_status);
        page.Children.Add(bar);
        page.Children.Add(body);
        Content = page;

        _list.SelectionChanged += (_, _) => ShowSelected();
        _list.MouseDoubleClick += (_, _) => GetSelected();
        Loaded += (_, _) => _ = Load();
        ShowSelected();
    }

    private static Button Push(string text, Action act)
    {
        var button = new Button { Content = text, Padding = new Thickness(10, 2, 10, 2), Margin = new Thickness(0, 4, 6, 4) };
        button.Click += (_, _) => act();
        return button;
    }

    private DiscoveryShare.Shared? Selected => _list.SelectedItem is ListBoxItem { Tag: DiscoveryShare.Shared s } ? s : null;

    private async Task Load()
    {
        _status.Text = Lang.T("목록을 받는 중입니다…");
        var got = await DiscoveryShare.List();
        if (got == null) { _status.Text = Lang.T("목록을 받지 못했습니다 — 인터넷 연결을 확인해 주세요."); return; }
        string? keep = Selected?.Uuid;
        _shares = got;
        _list.Items.Clear();
        foreach (var s in _shares)
        {
            bool have = UserDiscoveries.All.Any(l => l.Entry.Uuid == s.Uuid);
            if (have) Adopt(s);
            string author = s.Author.Length > 0 ? s.Author : Lang.T("이름 없음");
            string mark = s.Mine ? "  ★" : !have ? "" : Newer(s) ? "  ↑" : "  ✓";
            _list.Items.Add(new ListBoxItem
            {
                Tag = s,
                Content = $"{s.Name}  —  {author}  v{s.Revision}{mark}",
            });
        }
        _list.SelectedItem = _list.Items.OfType<ListBoxItem>().FirstOrDefault(i => ((DiscoveryShare.Shared)i.Tag).Uuid == keep)
                             ?? _list.Items.OfType<ListBoxItem>().FirstOrDefault();
        _status.Text = _shares.Count == 0
            ? Lang.T("아직 올라온 발견물이 없습니다.")
            : $"{_shares.Count}" + Lang.T("개 — ★ 는 내가 올린 것, ✓ 는 이미 가진 것, ↑ 는 가진 것보다 새 버전이 올라온 것입니다.");
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

    private async void ShowSelected()
    {
        var s = Selected;
        _get.IsEnabled = _report.IsEnabled = s != null;
        _remove.IsEnabled = s is { Mine: true };
        bool have = s != null && UserDiscoveries.All.Any(l => l.Entry.Uuid == s.Uuid);
        _get.Content = s == null || !have ? Lang.T("받아서 추가") : Newer(s) ? Lang.T("새 버전 받기") : Lang.T("다시 받기");
        _picture.Source = null;
        if (s == null) { _detail.Text = ""; return; }

        string category = s.Category >= 0 && s.Category < DiscoveryTable.CategoryNames.Length ? Lang.T(DiscoveryTable.CategoryNames[s.Category]) : "";
        var text = new System.Text.StringBuilder();
        text.AppendLine($"『{s.Name}』  {category}");
        text.AppendLine($"{Lang.T("만든 이")}: {(s.Author.Length > 0 ? s.Author : Lang.T("이름 없음"))}");
        text.AppendLine($"{Lang.T("라이선스")}: {s.License}");
        if (s.WantsOfficial) text.AppendLine(Lang.T("정식 버전에 넣어도 좋다고 한 발견물입니다."));
        text.AppendLine($"{Lang.T("크기")}: {s.Size / 1024.0:0.#} KB{(s.HasMovie ? " · " + Lang.T("동영상 있음") : "")}");
        text.AppendLine($"{Lang.T("받은 수")}: {s.Downloads} · {Lang.T("고친 날")}: {(s.UpdatedAt.Length >= 10 ? s.UpdatedAt[..10] : s.UpdatedAt)} (v{s.Revision})");
        if (have)
            text.AppendLine(s.Mine ? Lang.T("내가 올린 발견물입니다 — 받으면 내 PC 의 것을 덮어쓸지 묻습니다.")
                : Newer(s) ? $"{Lang.T("가진 판")}: v{DiscoveryShare.StateOf(s.Uuid)?.Rev} → {Lang.T("새 판")}: v{s.Revision} — {Lang.T("「새 버전 받기」로 바꿉니다.")}"
                : Lang.T("이미 가진 발견물입니다(최신)."));
        if (!s.Mine) text.AppendLine(Lang.T("다른 분이 올린 발견물은 받아도 고칠 수 없습니다 — 「복제」해서 내 발견물로 고쳐 쓸 수 있습니다."));
        text.AppendLine();
        text.AppendLine(s.Description);
        _detail.Text = text.ToString();
        ShowHistory(s, text.ToString());

        if (!s.HasPicture) return;
        int ask = ++_pictureAsk;
        var bytes = await DiscoveryShare.Picture(s.Uuid);
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

    /// <summary>판 이력을 받아 설명 아래에 붙인다 — 예전 받는 곳이면 안 붙인다.</summary>
    private async void ShowHistory(DiscoveryShare.Shared s, string head)
    {
        int ask = _pictureAsk;
        var revisions = await DiscoveryShare.History(s.Uuid);
        if (ask != _pictureAsk || Selected?.Uuid != s.Uuid || revisions is not { Count: > 0 }) return;
        var text = new System.Text.StringBuilder(head);
        text.AppendLine();
        text.AppendLine(Lang.T("판 이력"));
        foreach (var r in revisions)
            text.AppendLine($"  v{r.Rev}  {(r.CreatedAt.Length >= 10 ? r.CreatedAt[..10] : r.CreatedAt)}  {(r.Note.Length > 0 ? r.Note : "—")}");
        _detail.Text = text.ToString();
    }

    private async void GetSelected()
    {
        if (Selected is not { } s) return;
        _get.IsEnabled = false;
        _status.Text = Lang.T("내려받는 중입니다…");
        string? path = await DiscoveryShare.Download(s.Uuid);
        _get.IsEnabled = true;
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

    /// <summary>창을 띄운다. 들인 발견물의 UUID(없으면 null).</summary>
    public static string? Open(Window owner, Engine.Game? game)
    {
        var window = new SharedDiscoveryDialog(game) { Owner = owner };
        window.ShowDialog();
        return window.Imported;
    }
}
