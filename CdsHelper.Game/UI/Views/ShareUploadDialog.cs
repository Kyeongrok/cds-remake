using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using CdsHelper.Game.Local.Helpers;

namespace CdsHelper.Game.UI.Views;

/// <summary>
/// 「클라우드에 올리기」 전에 묻는 창 — 누구나 받아 갈 수 있게 공개된다는 것과 라이선스를 알리고,
/// <b>정식 버전에 넣어도 좋은지</b>를 체크박스로 받는다(<see cref="DiscoveryShare.Upload"/> 의 official).
/// 정식 버전에 넣은 발견물은 클라우드에서 지워 용량을 비운다. 원본에 없는 것이다.
/// </summary>
public sealed class ShareUploadDialog : GameWindow
{
    private readonly CheckBox _official = new()
    {
        Content = "정식 버전에 넣어도 좋습니다 — 개발자가 이 발견물을 게임 기본 발견물로 넣을 수 있습니다",
        Margin = new Thickness(0, 12, 0, 0),
    };

    /// <summary>고친 내용 — 판 이력에 남아 받는 분들이 「공유 발견물」 창에서 본다.</summary>
    private readonly TextBox _note = new() { MaxLength = 200, Margin = new Thickness(0, 4, 0, 0), TextWrapping = TextWrapping.Wrap, AcceptsReturn = false };

    private bool _ok;

    private ShareUploadDialog(string name, string license, int currentRev)
    {
        bool update = currentRev > 0;
        Title = "클라우드에 올리기";
        Width = 560;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        Background = Brushes.White;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var go = new Button { Content = "올리기", Padding = new Thickness(14, 2, 14, 2), Margin = new Thickness(0, 0, 6, 0), IsDefault = true };
        go.Click += (_, _) => { _ok = true; Close(); };
        var cancel = new Button { Content = "취소", Padding = new Thickness(14, 2, 14, 2), IsCancel = true };
        cancel.Click += (_, _) => Close();

        Content = new StackPanel
        {
            Margin = new Thickness(16),
            Children =
            {
                new TextBlock { Text = $"「{name}」", FontWeight = FontWeights.Bold, FontSize = 14 },
                new TextBlock
                {
                    TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0),
                    Text = Lang.T(update
                        ? "이미 올린 발견물입니다. 올리면 새 버전으로 바뀌고, 받아 간 분들에게 새 버전이 있다고 보입니다."
                        : "올리면 다른 분들이 「공유 발견물」 창에서 받아 갈 수 있습니다. 동영상은 5MB 까지 올릴 수 있습니다.")
                           + (update ? $"  (v{currentRev} → v{currentRev + 1})" : ""),
                },
                new TextBlock
                {
                    Margin = new Thickness(0, 10, 0, 0), FontWeight = FontWeights.Bold,
                    Text = Lang.T(update ? "고친 내용" : "소개 한 줄(판 이력에 남습니다, 비워도 됩니다)"),
                },
                _note,
                new TextBlock
                {
                    TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0), Foreground = Brushes.DimGray,
                    Text = $"{Lang.T("라이선스")}: {license}",
                },
                _official,
                new TextBlock
                {
                    TextWrapping = TextWrapping.Wrap, Margin = new Thickness(20, 4, 0, 0), Foreground = Brushes.DimGray,
                    Text = Lang.T("정식 버전에 들어가면 업데이트로 모든 분의 게임에 기본으로 나오고, 클라우드 목록에서는 내려갑니다."),
                },
                new StackPanel
                {
                    Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 0),
                    Children = { go, cancel },
                },
            },
        };
    }

    /// <summary>창을 띄운다. 올리기를 눌렀으면 (정식 버전 허락 여부, 고친 내용), 취소했으면 null.</summary>
    /// <param name="currentRev">이미 올린 판(처음 올리면 0).</param>
    public static (bool Official, string Note)? Ask(Window owner, string name, string license, int currentRev)
    {
        var window = new ShareUploadDialog(name, license, currentRev) { Owner = owner };
        window.Loaded += (_, _) => window._note.Focus();
        window.ShowDialog();
        return window._ok ? (window._official.IsChecked == true, window._note.Text.Trim()) : null;
    }
}
