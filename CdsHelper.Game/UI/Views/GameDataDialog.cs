using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using CdsHelper.Game.Engine;
using CdsHelper.Game.Local.Helpers;

namespace CdsHelper.Game.UI.Views;

/// <summary>
/// 앱이 적어 둔 게임 데이터를 들여다보는 창. 제목 줄 햄버거에서 연다.
/// </summary>
/// <remarks>
/// 게임 EXE 에서 구워 둔 표(<see cref="ExeTable"/>)와 세이브가 여기 다 모인다. 눈으로 볼 수
/// 있어야 하는 까닭이 둘이다 — 표가 제대로 구워졌는지 확인할 데가 있어야 하고, 판이 갈려
/// 이상한 값이 적혔을 때 무엇이 적혔는지 봐야 지울지 말지 정할 수 있다.
///
/// 읽기만 한다. 고치려면 "폴더 열기" 로 파일을 직접 열면 된다 — 이 창에서 고치게 하면
/// 반쯤 고친 JSON 이 적혀 표가 아예 안 열리는 일이 생긴다.
/// </remarks>
public sealed class GameDataDialog : GameWindow
{
    /// <summary>
    /// 미리 보기에 한 번에 올리는 글자 수. 건물표는 1504줄이라 다 펼치면 40만 자가 넘는데,
    /// 그만큼을 <see cref="TextBox"/> 에 밀어 넣으면 창이 뜨는 데만 한참 걸린다.
    /// </summary>
    private const int PreviewLimit = 200_000;

    private readonly ListBox _files = new()
    {
        Width = 220,
        Background = GameUi.MenuBack,
        Foreground = GameUi.Text,
        BorderBrush = GameUi.Edge,
        BorderThickness = new Thickness(1),
        FontSize = 13,
    };

    private readonly TextBlock _where = new()
    {
        Foreground = GameUi.Text,
        FontSize = 12,
        Margin = new Thickness(0, 0, 0, 4),
        TextTrimming = TextTrimming.CharacterEllipsis,
    };

    private readonly TextBox _body = new()
    {
        IsReadOnly = true,
        AcceptsReturn = true,
        FontFamily = new FontFamily("Consolas, D2Coding, Courier New"),
        FontSize = 12,
        Background = GameUi.PageFill,
        Foreground = Brushes.Black,
        BorderBrush = GameUi.Edge,
        BorderThickness = new Thickness(1),
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
    };

    /// <summary>목록에 담는 한 줄. 화면에 보이는 것은 <see cref="ToString"/> 다.</summary>
    /// <param name="Kind">어디서 온 것인지 — 파일이 스스로 적어 둔 값이다.</param>
    private sealed record Entry(string Kind, string Path)
    {
        public override string ToString()
        {
            var info = new FileInfo(Path);
            return info.Exists
                ? $"{Kind}  {System.IO.Path.GetFileName(Path)}\n      {info.Length:N0}바이트 · {info.LastWriteTime:yy-MM-dd HH:mm}"
                : $"{Kind}  {System.IO.Path.GetFileName(Path)}\n      (아직 없다)";
        }
    }

    private GameDataDialog(Window owner)
    {
        Owner = owner;
        Title = "게임데이터";
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        Width = 900;
        Height = 600;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        Background = GameUi.Back;

        _files.SelectionChanged += (_, _) => ShowSelected();

        var right = new DockPanel { LastChildFill = true, Margin = new Thickness(8, 0, 0, 0) };
        DockPanel.SetDock(_where, Dock.Top);
        right.Children.Add(_where);
        right.Children.Add(_body);

        var split = new DockPanel { LastChildFill = true, Margin = new Thickness(12, 8, 12, 8) };
        DockPanel.SetDock(_files, Dock.Left);
        split.Children.Add(_files);
        split.Children.Add(right);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 12),
        };
        buttons.Children.Add(GameUi.PushButton("폴더 열기", OpenFolder, 130));
        buttons.Children.Add(GameUi.PushButton("다시 읽기", Reload, 130));
        buttons.Children.Add(GameUi.PushButton("닫기", Close, 96));

        var title = GameUi.TitleBar("게임데이터", Close);
        GameUi.EnableDrag(this, title);

        var stack = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(title, Dock.Top);
        stack.Children.Add(title);
        DockPanel.SetDock(buttons, Dock.Bottom);
        stack.Children.Add(buttons);
        stack.Children.Add(split);

        Content = new Border
        {
            Background = GameUi.Back,
            BorderBrush = GameUi.Edge,
            BorderThickness = new Thickness(2),
            Margin = new Thickness(4),
            Child = stack,
        };

        Reload();
        KeyDown += (_, e) => { if (e.Key is Key.Escape) Close(); };
    }

    /// <summary>목록을 새로 읽는다. 고르고 있던 파일이 그대로 있으면 그것을 다시 고른다.</summary>
    private void Reload()
    {
        string? was = (_files.SelectedItem as Entry)?.Path;

        var entries = new List<Entry>();
        foreach (var path in TableCache.Saved()) entries.Add(new Entry(KindOf(path), path));
        entries.Add(new Entry("[세이브]", GameSave.Path));

        _files.ItemsSource = entries;
        _files.SelectedItem = entries.FirstOrDefault(e => e.Path == was) ?? entries.FirstOrDefault();
        if (_files.SelectedItem == null) ShowSelected();
    }

    /// <summary>
    /// 표가 어디서 구워졌는지 — 파일 안의 <c>Source</c> 를 그대로 읽는다. 이름으로 짐작하면
    /// 표가 늘 때마다 여기를 같이 고쳐야 한다.
    /// </summary>
    private static string KindOf(string path)
    {
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(path));
            return doc.RootElement.TryGetProperty("Source", out var source)
                   && source.GetString() is { Length: > 0 } name
                ? $"[{name}]"
                : "[표]";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                                      or System.Text.Json.JsonException)
        {
            return "[표]";
        }
    }

    private void ShowSelected()
    {
        if (_files.SelectedItem is not Entry entry)
        {
            _where.Text = "";
            _body.Text = "적어 둔 것이 아직 없습니다.\n\n" +
                         "게임 표는 도시에 처음 들어갈 때 CDS_95.EXE 에서 구워집니다.";
            return;
        }

        _where.Text = entry.Path;
        try
        {
            if (!File.Exists(entry.Path))
            {
                _body.Text = "아직 없는 파일입니다.";
                return;
            }

            string text = File.ReadAllText(entry.Path);
            _body.Text = text.Length > PreviewLimit
                ? text[..PreviewLimit] +
                  $"\n\n… 여기서 잘랐습니다 — 전부 {text.Length:N0}자 중 {PreviewLimit:N0}자만 보입니다.\n" +
                  "다 보려면 [폴더 열기] 로 파일을 직접 여세요."
                : text;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _body.Text = $"읽지 못했습니다 — {ex.Message}";
        }
    }

    /// <summary>고른 파일이 든 폴더를 탐색기로 연다.</summary>
    private void OpenFolder()
    {
        string folder = _files.SelectedItem is Entry entry
            ? System.IO.Path.GetDirectoryName(entry.Path) ?? ExeTable.Folder
            : ExeTable.Folder;
        try
        {
            Directory.CreateDirectory(folder);
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(folder)
            {
                UseShellExecute = true,
            });
        }
        catch (Exception ex)
        {
            NoticeDialog.Show(this, $"폴더를 열지 못했습니다 — {ex.Message}");
        }
    }

    public static void Show(Window owner) => new GameDataDialog(owner).ShowDialog();
}
