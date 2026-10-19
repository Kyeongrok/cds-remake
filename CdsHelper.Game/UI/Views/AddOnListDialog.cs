using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using CdsHelper.Game.UI.AddOns;

namespace CdsHelper.Game.UI.Views;

/// <summary>
/// 도시락 「애드온」 — 애드온 폴더에 든 애드온을 늘어놓고 하나씩 켜고 끈다. 원본에 없는 창이다(요청).
/// </summary>
/// <remarks>
/// 켜고 끈 것은 <c>SavedVariables\disabled.txt</c> 에 적고 그 자리에서 모두 다시 읽는다(<see cref="AddOnHost.SetEnabled"/>).
/// 새로 넣은 애드온은 켜진 채로 시작한다. 오류로 멈춘 애드온은 마지막 오류를 붉게 적는다.
/// </remarks>
internal sealed class AddOnListDialog : InfoDialog
{
    private const double BoardWidth = 520;

    private static readonly Brush NoteInk = Frozen(Color.FromRgb(0xA8, 0x9C, 0x96));
    private static readonly Brush ErrorInk = Frozen(Color.FromRgb(240, 120, 100));

    private readonly StackPanel _list = new();

    private AddOnListDialog()
    {
        var scroll = new ScrollViewer
        {
            Content = _list,
            MaxHeight = 420,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        };
        Fill();
        // 마지막 줄 설명이 아래 단추에 붙지 않게 띄운다.
        var body = new StackPanel();
        body.Children.Add(scroll);
        body.Children.Add(Gap(10));

        Build("애드온", body, BoardWidth, double.NaN,
              new GameButton("폴더 열기", OpenFolder),
              new GameButton("다시 읽기", () => { AddOnHost.Reload(); Fill(); }),
              new GameButton("닫기", Close));
    }

    private void Fill()
    {
        _list.Children.Clear();
        if (AddOnHost.Loaded.Count == 0)
        {
            _list.Children.Add(Label("애드온이 없습니다. 「폴더 열기」로 연 폴더에 애드온 폴더를 넣고 「다시 읽기」를 누르세요."));
            return;
        }

        foreach (var addon in AddOnHost.Loaded)
        {
            var box = new CheckBox
            {
                Content = addon.Title == addon.Name ? addon.Name : $"{addon.Title}  ({addon.Name})",
                IsChecked = addon.Running,
                Foreground = GameUi.Text,
                FontSize = 14,
                Margin = new Thickness(0, 6, 0, 0),
            };
            string name = addon.Name;
            box.Checked += (_, _) => Toggle(name, true);
            box.Unchecked += (_, _) => Toggle(name, false);
            _list.Children.Add(box);

            if (addon.Notes.Length > 0) _list.Children.Add(Note(addon.Notes, NoteInk));
            if (addon.Failed is { } error) _list.Children.Add(Note($"오류로 멈췄습니다 — {error}", ErrorInk));
        }
    }

    /// <summary>켜고 끄면 모두 다시 읽으니, 줄도 새로 지어 상태(오류 따위)를 다시 적는다.</summary>
    private void Toggle(string name, bool on)
    {
        AddOnHost.SetEnabled(name, on);
        // 체크 칸의 이벤트 안에서 그 칸을 지우면 안 된다 — 이벤트가 끝난 뒤에 짓는다.
        Dispatcher.BeginInvoke(Fill);
    }

    private static TextBlock Note(string text, Brush ink) => new()
    {
        Text = text,
        Foreground = ink,
        FontSize = 12,
        TextWrapping = TextWrapping.Wrap,
        Margin = new Thickness(22, 1, 0, 0),
    };

    private void OpenFolder()
    {
        try
        {
            System.IO.Directory.CreateDirectory(AddOnHost.Root);
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(AddOnHost.Root) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            NoticeDialog.Show(this, $"애드온 폴더를 열지 못했습니다 — {ex.Message}");
        }
    }

    public static void Show(Window owner) => new AddOnListDialog { Owner = owner }.ShowDialog();
}
