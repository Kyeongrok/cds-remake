using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using CdsHelper.Game.Local.Helpers;
using CdsHelper.Support.Local.Helpers;

namespace CdsHelper.Game.UI.Views;

/// <summary>
/// 커스텀 발견물 장면의 「[효과음] 번호」 줄을 고르는 창 — WAVES.CDS 효과음 50개(파트 0~49)를 늘어놓고, 골라 들어 본 뒤 넣는다.
/// </summary>
/// <remarks>
/// 번호는 <b>파트</b>다. 대본은 사운드 ID(파트 + 28)로 낸다(<see cref="UserDiscoveries.SoundPart"/>).
/// 들어 보기는 효과음을 꺼 두었어도 난다(<see cref="SoundBank.Preview"/>).
/// </remarks>
public sealed class SoundPickerDialog : GameWindow
{
    private readonly ListBox _list = new() { Margin = new Thickness(10, 6, 10, 6) };
    private readonly TextBlock _status = new() { Margin = new Thickness(10, 0, 10, 4), Foreground = Brushes.DimGray, TextWrapping = TextWrapping.Wrap };
    private readonly SoundBank? _bank;

    /// <summary>고른 파트. 안 골랐으면 null.</summary>
    public int? Picked { get; private set; }

    /// <summary>
    /// 원본 대본이 그 소리를 어디에 쓰는지 — 고를 때 짐작하라고 붙인다(<c>exe-tables/disev.json</c> 의 <c>0E 03</c> 뒤 대사와
    /// <see cref="SoundBank"/> 의 상수에서 모으고, 사용자가 들어 보고 붙인 이름을 더했다). 없는 번호는 원본에서 쓰는 곳을 못 찾은 것이다.
    /// </summary>
    private static readonly Dictionary<int, string> Uses = new()
    {
        [0] = "물 붓기 · 부대 놓기",
        [1] = "닻 · 알림",
        [2] = "총격",
        [3] = "큰 소리",
        [4] = "괴물의 일격",
        [5] = "백병전",
        [6] = "화살",
        [7] = "코끼리 무덤",
        [8] = "힘이 솟음",
        [9] = "재규어",
        [11] = "회복 · 성배 완성",
        [12] = "헛디딤",
        [13] = "대포 발사",
        [14] = "대포 명중",
        [15] = "물에 빠짐",
        [18] = "배가 가라앉음",
        [19] = "괴물 포효",
        [21] = "갈매기",
        [22] = "배 화재 · 돌고래",
        [23] = "날치",
        [24] = "물장구",
        [25] = "세이렌",
        [26] = "파도",
        [27] = "첨벙",
        [28] = "괴물 울음",
        [30] = "행복",
        [31] = "늠름함",
        [32] = "비명",
        [33] = "홍학",
        [36] = "물소리",
        [37] = "무언가 들림",
        [38] = "회오리",
        [39] = "놀람",
        [41] = "오로라",
        [43] = "머리가 맑아짐",
        [44] = "칼 부딪힘",
        [45] = "생포 · 성공",
        [46] = "일기토 패배",
        [47] = "발견",
        [48] = "대원이 쓰러짐",
        [49] = "승리",
    };

    /// <summary>그 효과음의 라벨(「총격」 따위). 없으면 null.</summary>
    public static string? UseOf(int part) => Uses.TryGetValue(part, out var use) ? use : null;

    /// <summary>장면에 적을 「[효과음] 번호 라벨」 줄 — 읽을 때는 앞 번호만 쓴다(<see cref="UserDiscoveries.SoundPart"/>).</summary>
    public static string SceneLine(int part) => UseOf(part) is { } use ? $"[효과음] {part} {use}" : $"[효과음] {part}";

    private SoundPickerDialog(Engine.Game? game, int current)
    {
        Title = "효과음 넣기";
        Width = 420;
        Height = 560;
        Background = Brushes.White;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        _bank = game?.Sfx ?? SoundBank.Current;

        var hint = new TextBlock
        {
            Margin = new Thickness(10, 10, 10, 0), TextWrapping = TextWrapping.Wrap,
            Text = "줄을 고르면 소리가 납니다. 「넣기」를 누르면 장면에 「[효과음] 번호」 줄이 들어갑니다.",
        };

        Button Push(string text, Action act)
        {
            var b = new Button { Content = text, Padding = new Thickness(14, 2, 14, 2), Margin = new Thickness(0, 0, 6, 0) };
            b.Click += (_, _) => act();
            return b;
        }
        var bar = new StackPanel
        {
            Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(10, 4, 10, 10),
            Children = { Push("듣기", Listen), Push("넣기", Choose), Push("취소", Close) },
        };

        var page = new DockPanel();
        DockPanel.SetDock(hint, Dock.Top);
        DockPanel.SetDock(bar, Dock.Bottom);
        DockPanel.SetDock(_status, Dock.Bottom);
        page.Children.Add(hint);
        page.Children.Add(bar);
        page.Children.Add(_status);
        page.Children.Add(_list);
        Content = page;

        for (int part = 0; part < UserDiscoveries.SoundParts; part++)
        {
            string use = Uses.TryGetValue(part, out var u) ? $"  {u}" : "";
            var item = new ListBoxItem { Content = $"{part,2}{use}", Tag = part };
            _list.Items.Add(item);
            if (part == current) _list.SelectedItem = item;
        }
        if (_bank == null) _status.Text = "효과음을 열지 못해 들어 볼 수 없습니다 — 번호만 골라 넣을 수 있습니다.";

        _list.SelectionChanged += (_, _) => Listen();
        _list.MouseDoubleClick += (_, _) => Choose();
        _list.KeyDown += (_, e) => { if (e.Key == System.Windows.Input.Key.Enter) { Choose(); e.Handled = true; } };
        Loaded += (_, _) =>
        {
            if (_list.SelectedItem != null) _list.ScrollIntoView(_list.SelectedItem);
            _list.Focus();
        };
    }

    /// <summary>고른 줄의 소리를 낸다.</summary>
    private void Listen()
    {
        if (_list.SelectedItem is not ListBoxItem { Tag: int part } || _bank == null) return;
        _status.Text = _bank.Preview(part)
            ? $"효과음 {part}(사운드 ID {part + WaveBank.FirstSoundId})"
            : $"효과음 {part} 를 내지 못했습니다.";
    }

    private void Choose()
    {
        if (_list.SelectedItem is not ListBoxItem { Tag: int part })
        {
            MessageBox.Show(this, "효과음을 골라 주세요.", Title);
            return;
        }
        Picked = part;
        Close();
    }

    /// <summary>창을 띄워 효과음 하나를 고른다. 고른 파트, 취소했으면 null.</summary>
    public static int? Pick(Window owner, Engine.Game? game, int current = -1)
    {
        var window = new SoundPickerDialog(game, current) { Owner = owner };
        window.ShowDialog();
        return window.Picked;
    }
}
