using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using CdsHelper.Game.Local.Helpers;
using CdsHelper.Game.Local.Settings;
using Microsoft.Win32;

namespace CdsHelper.Game.UI.Views;

/// <summary>
/// 개발도구 메뉴 줄의 「커스텀 팩: 이름 ▾」 — 팩을 고르고, 만들고 · 이름 바꾸고 · 지우고 · zip 으로 내보내고 들인다.
/// </summary>
/// <remarks>
/// 팩을 골라 두면 편집 창(국가 · 인물 · 후원자 · 힌트 …)이 고친 「-고친것」 표를 그 팩 폴더에 <b>고친 줄만</b> 적는다
/// (<see cref="TableCache.Write{T}"/>). 「없음」이면 지금처럼 기본 표 자리(<c>exe-tables</c>)에 적는다 — 개발용이다.
/// 고른 팩은 <c>game-settings.json</c> 의 <see cref="GameSettings.EditorPack"/> 에 남는다. 편집 창은 모두 모달이라,
/// 팩을 바꾸는 동안 열린 편집 창은 없다(다음에 여는 창이 새 팩을 읽는다).
/// </remarks>
public static class CustomPackMenu
{
    /// <summary>메뉴 항목에 팩 고르개를 붙인다.</summary>
    public static void Attach(MenuItem menu, Window owner)
    {
        Fill(menu, owner);
        menu.SubmenuOpened += (_, e) => { if (ReferenceEquals(e.OriginalSource, menu)) Fill(menu, owner); };
        CustomPacks.Changed += () => menu.Dispatcher.BeginInvoke(() => Fill(menu, owner));
    }

    private static void Fill(MenuItem menu, Window owner)
    {
        string? active = CustomPacks.Active;
        menu.Header = $"커스텀 팩: {active ?? "없음"}";
        menu.Items.Clear();

        menu.Items.Add(Item("없음 — 기본 표 자리(exe-tables)에 적기", () => Pick(owner, null), check: active == null));
        foreach (string name in CustomPacks.Names())
        {
            var m = CustomPacks.Read(name);
            var item = Item(name, () => Pick(owner, name), check: active == name);
            var tip = new List<string>();
            if (m.Author.Length > 0) tip.Add($"만든 이: {m.Author}");
            if (m.GameVersion.Length > 0) tip.Add($"만든 게임 버전: {m.GameVersion}");
            tip.Add($"표 {CustomPacks.TableFiles(name).Count}개");
            if (m.Description.Length > 0) tip.Add(m.Description);
            item.ToolTip = string.Join("\n", tip);
            menu.Items.Add(item);
        }

        menu.Items.Add(new Separator());
        menu.Items.Add(Item("새 팩 만들기…", () => Create(owner)));
        menu.Items.Add(Item("이름 · 설명 바꾸기…", () => Rename(owner, active!), enabled: active != null));
        menu.Items.Add(Item("지우기…", () => Delete(owner, active!), enabled: active != null));
        menu.Items.Add(new Separator());
        menu.Items.Add(Item("내보내기 (zip)…", () => Export(owner, active!), enabled: active != null));
        menu.Items.Add(Item("들이기 (zip)…", () => Import(owner)));
        menu.Items.Add(Item("팩 폴더 열기", OpenFolder));
    }

    private static MenuItem Item(string header, Action run, bool check = false, bool enabled = true)
    {
        var item = new MenuItem { Header = header, IsCheckable = false, IsChecked = check, IsEnabled = enabled };
        item.Click += (_, _) => run();
        return item;
    }

    /// <summary>팩을 켠다 — 편집 창이 그 팩에 적는다. 판이 안 맞아 건너뛴 표가 있으면 알린다.</summary>
    private static void Pick(Window owner, string? name)
    {
        if (name == CustomPacks.Active) return;
        var skipped = CustomPacks.Use(name);
        GameSettings.EditorPack = name ?? "";
        if (skipped.Count > 0)
            MessageBox.Show(owner, "형식이 맞지 않아 기본 값으로 쓰는 표가 있습니다.\n\n" + string.Join("\n", skipped),
                            "커스텀 팩", MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    private static void Create(Window owner)
    {
        if (Ask(owner, "새 커스텀 팩", "", "", "") is not { } got) return;
        var (name, author, description) = got;
        string error = CustomPacks.Create(name, author, description);
        if (Fail(owner, error)) return;
        Pick(owner, name);
    }

    private static void Rename(Window owner, string name)
    {
        var m = CustomPacks.Read(name);
        if (Ask(owner, "커스텀 팩 이름 · 설명 바꾸기", name, m.Author, m.Description) is not { } got) return;
        var (to, author, description) = got;
        string error = CustomPacks.Rename(name, to);
        if (Fail(owner, error)) return;
        if (to != name) GameSettings.EditorPack = to;
        Fail(owner, CustomPacks.Describe(to, author, description));
    }

    private static void Delete(Window owner, string name)
    {
        if (MessageBox.Show(owner, $"「{name}」 팩을 지웁니다. 팩에 고쳐 둔 값이 모두 사라집니다.\n계속할까요?", "커스텀 팩 지우기",
                            MessageBoxButton.OKCancel, MessageBoxImage.Warning) != MessageBoxResult.OK) return;
        if (Fail(owner, CustomPacks.Delete(name))) return;
        GameSettings.EditorPack = "";
    }

    private static void Export(Window owner, string name)
    {
        var box = new SaveFileDialog
        {
            Title = $"「{name}」 팩 내보내기",
            Filter = "커스텀 팩 (*.pack.zip)|*.pack.zip|모든 파일|*.*",
            FileName = $"{name}.pack.zip",
        };
        if (box.ShowDialog(owner) != true) return;
        if (Fail(owner, CustomPacks.Export(name, box.FileName))) return;
        MessageBox.Show(owner, $"{box.FileName} 로 내보냈습니다.", "커스텀 팩", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private static void Import(Window owner)
    {
        var box = new OpenFileDialog
        {
            Title = "커스텀 팩 들이기",
            Filter = "커스텀 팩 (*.pack.zip;*.zip)|*.pack.zip;*.zip|모든 파일|*.*",
        };
        if (box.ShowDialog(owner) != true) return;
        if (CustomPacks.Peek(box.FileName, out string why) is not { } manifest) { Fail(owner, why); return; }

        string name = manifest.Name;
        if (CustomPacks.Exists(name)
            && MessageBox.Show(owner, $"「{name}」 팩이 이미 있습니다. 들이는 팩으로 갈아 끼울까요?", "커스텀 팩 들이기",
                               MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK) return;
        if (Fail(owner, CustomPacks.Import(box.FileName, name))) return;
        Pick(owner, name);
        MessageBox.Show(owner, $"「{name}」 팩을 들였습니다 — 표 {CustomPacks.TableFiles(name).Count}개.", "커스텀 팩",
                        MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private static void OpenFolder()
    {
        try
        {
            Directory.CreateDirectory(CustomPacks.Root);
            Process.Start(new ProcessStartInfo(CustomPacks.Root) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception) { }
    }

    /// <summary>까닭이 있으면 알리고 참.</summary>
    private static bool Fail(Window owner, string error)
    {
        if (error.Length == 0) return false;
        MessageBox.Show(owner, error, "커스텀 팩", MessageBoxButton.OK, MessageBoxImage.Warning);
        return true;
    }

    /// <summary>이름 · 만든 이 · 설명을 묻는 작은 창. 그만두면 null.</summary>
    private static (string Name, string Author, string Description)? Ask(Window owner, string title, string name,
                                                                          string author, string description)
    {
        var nameBox = new TextBox { Text = name, MaxLength = CustomPacks.MaxNameLength, Margin = new Thickness(0, 2, 0, 8) };
        var authorBox = new TextBox { Text = author, Margin = new Thickness(0, 2, 0, 8) };
        var descBox = new TextBox
        {
            Text = description, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, Height = 60,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Margin = new Thickness(0, 2, 0, 8),
        };
        var note = new TextBlock { Foreground = System.Windows.Media.Brushes.Firebrick, TextWrapping = TextWrapping.Wrap };
        var ok = new Button { Content = "확인", Width = 80, IsDefault = true, Margin = new Thickness(0, 0, 6, 0) };
        var cancel = new Button { Content = "취소", Width = 80, IsCancel = true };

        var window = new Window
        {
            Title = title, Owner = owner, Width = 380, SizeToContent = SizeToContent.Height,
            WindowStartupLocation = WindowStartupLocation.CenterOwner, ResizeMode = ResizeMode.NoResize,
            ShowInTaskbar = false,
            Content = new StackPanel
            {
                Margin = new Thickness(14),
                Children =
                {
                    new TextBlock { Text = "팩 이름 (폴더 이름으로 쓴다)" }, nameBox,
                    new TextBlock { Text = "만든 이" }, authorBox,
                    new TextBlock { Text = "설명" }, descBox,
                    note,
                    new StackPanel
                    {
                        Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right,
                        Margin = new Thickness(0, 8, 0, 0), Children = { ok, cancel },
                    },
                },
            },
        };
        ok.Click += (_, _) =>
        {
            if (!CustomPacks.IsSafeName(nameBox.Text, out string why)) { note.Text = why; return; }
            window.DialogResult = true;
        };
        window.Loaded += (_, _) => { nameBox.Focus(); nameBox.SelectAll(); };
        return window.ShowDialog() == true ? (nameBox.Text, authorBox.Text.Trim(), descBox.Text.Trim()) : null;
    }
}
