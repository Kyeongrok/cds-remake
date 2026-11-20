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
            string author = s.Author.Length > 0 ? s.Author : Lang.T("이름 없음");
            _list.Items.Add(new ListBoxItem
            {
                Tag = s,
                Content = $"{s.Name}  —  {author}{(s.Mine ? "  ★" : have ? "  ✓" : "")}",
            });
        }
        _list.SelectedItem = _list.Items.OfType<ListBoxItem>().FirstOrDefault(i => ((DiscoveryShare.Shared)i.Tag).Uuid == keep)
                             ?? _list.Items.OfType<ListBoxItem>().FirstOrDefault();
        _status.Text = _shares.Count == 0
            ? Lang.T("아직 올라온 발견물이 없습니다.")
            : $"{_shares.Count}" + Lang.T("개 — ★ 는 내가 올린 것, ✓ 는 이미 가진 것입니다.");
    }

    private async void ShowSelected()
    {
        var s = Selected;
        _get.IsEnabled = _report.IsEnabled = s != null;
        _remove.IsEnabled = s is { Mine: true };
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
        if (UserDiscoveries.All.FirstOrDefault(l => l.Entry.Uuid == s.Uuid) is { } have)
            text.AppendLine(Lang.T("이미 가진 발견물입니다 — 받으면 덮어쓸지 묻습니다."));
        text.AppendLine();
        text.AppendLine(s.Description);
        _detail.Text = text.ToString();

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
            var (entry, message) = UserDiscoveries.Import(path,
                (had, got) => ConfirmDialog.Ask(this, $"같은 발견물 「{had.Name}」 이 이미 있습니다. 받은 「{got.Name}」 으로 덮어쓸까요?", "공유 발견물"),
                (had, got) => ConfirmDialog.Ask(this, $"같은 아이템 「{had.Name}」 이 이미 있습니다. 받은 「{got.Name}」 으로 덮어쓸까요?", "공유 발견물"),
                _game?.Books);
            if (entry != null) Imported = entry.Uuid;
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
        _status.Text = await DiscoveryShare.Delete(s.Uuid) ? Lang.T("공유 목록에서 내렸습니다.") : Lang.T("내리지 못했습니다.");
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
