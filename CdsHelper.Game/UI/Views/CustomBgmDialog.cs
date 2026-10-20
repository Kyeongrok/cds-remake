using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using CdsHelper.Game.Local.Helpers;
using CdsHelper.Game.Local.Settings;
using Microsoft.Win32;

namespace CdsHelper.Game.UI.Views;

/// <summary>
/// 곡 번호마다 파일을 갈아 끼우는 창 — 원본에 없는 기능이다(피드백 <c>fb-mo-1</c>).
/// </summary>
/// <remarks>
/// 켜고 끄는 스위치는 여기 없다 — 모드 창의 「커스텀 BGM」이 맡는다. 여기는 등록만 한다.
/// 등록해 둔 것은 스위치가 꺼져 있어도 지워지지 않는다.
///
/// 곡은 번호로만 돈다(<see cref="BgmPlayer.Play"/>) — 화면·건물이 아니라 <b>번호</b>가
/// 재생 단위라서, <see cref="BgmPlayer.KnownTracks"/> 에 늘어놓은 스물네 곡만 갈아 끼우면
/// 원본에서 실제로 도는 곡을 빠짐없이 덮는다.
/// </remarks>
public sealed class CustomBgmDialog : GameWindow
{
    /// <summary>줄 목록(굴림대 빼고)의 폭. 이름 칸이 남는 폭을 다 먹는다.</summary>
    private const double ListWidth = 780;

    /// <summary>미리 듣기 — 게임 곡과 따로 돈다. 듣는 동안 게임 곡은 멈춘다.</summary>
    private readonly MediaPlayer _preview = new();

    /// <summary>지금 미리 듣는 단추와 그 단추의 원래 글. 안 듣고 있으면 null.</summary>
    private (Border Button, string Label)? _playing;

    private bool _gamePaused;

    /// <summary>이 높이를 넘으면 게임풍 굴림대로 굴린다.</summary>
    private const double ListHeight = 420;

    private CustomBgmDialog()
    {
        Title = "BGM";
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.WidthAndHeight;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        Background = GameUi.Back;

        var intro = new TextBlock
        {
            Text = "원본에 없는 기능입니다. 곡마다 파일을 골라 두면, 모드 창의 「커스텀 BGM」이"
                 + " 켜져 있는 동안 원래 곡 대신 그 파일이 돕니다.",
            Foreground = GameUi.Text,
            FontSize = 13,
            TextWrapping = TextWrapping.Wrap,
            Width = ListWidth + GameUi.ScrollWidth,
            Margin = new Thickness(12, 8, 12, 8),
        };

        // 줄마다 StackPanel 을 따로 두면 칸이 안 맞고, 폭을 손으로 박으면 오른쪽이 잘린다 —
        // 한 Grid 에 줄을 쌓고 이름 칸만 별(*)로 두어 남는 폭을 먹인다.
        var rows = new Grid { Width = ListWidth };
        rows.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });   // 번호
        rows.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); // 이름
        rows.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });   // 지금 파일
        rows.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });   // 선택…
        rows.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });   // 기본으로
        rows.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });   // ▶ 원곡
        rows.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });   // ▶ 넣은 곡
        // 곡 번호 오름차순으로 — 목록 자체는 화면 갈래(타이틀·해상·도시…)로 묶여 있어
        // 그대로 그리면 번호가 뒤섞여 보인다.
        foreach (var (track, label) in BgmPlayer.KnownTracks.OrderBy(t => t.Track))
            AddRow(rows, track, label);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 6, 0, 12),
        };
        buttons.Children.Add(GameUi.PushButton("닫기", Close, 96));

        var title = GameUi.TitleBar("BGM", Close);
        GameUi.EnableDrag(this, title);

        var stack = new StackPanel();
        stack.Children.Add(title);
        stack.Children.Add(intro);
        // 넘치면 게임 굴림대로 굴린다 — 윈도 굴림대는 모양이 너무 다르다.
        stack.Children.Add(new Border
        {
            Margin = new Thickness(12, 0, 12, 4),
            Child = GameUi.Scroller(rows, ListHeight),
        });
        stack.Children.Add(buttons);

        Content = new Border
        {
            Background = GameUi.Back,
            BorderBrush = GameUi.Edge,
            BorderThickness = new Thickness(2),
            Margin = new Thickness(4),
            Child = stack,
        };

        KeyDown += (_, e) => { if (e.Key is Key.Escape) Close(); };

        // 미리 듣던 곡은 끝나면 멈춘다(되감지 않는다). 창을 닫으면 멈추고 게임 곡을 잇는다.
        _preview.MediaEnded += (_, _) => StopPreview();
        Closed += (_, _) =>
        {
            StopPreview();
            _preview.Close();
        };
    }

    /// <summary>
    /// 그 파일을 미리 듣는다 — 같은 단추를 다시 누르면 멈춘다. 게임 곡은 멈춰 두었다가 다 듣거나 창을 닫으면 잇는다.
    /// </summary>
    private void Preview(Border button, string label, string? path)
    {
        bool same = _playing is { } now && now.Button == button;
        StopPreview();
        if (same) return;
        if (path == null || !File.Exists(path))
        {
            NoticeDialog.Show(this, path == null ? "들을 파일이 없습니다." : $"파일이 없습니다 — {path}");
            return;
        }
        try
        {
            _preview.Open(new Uri(path));
            _preview.Volume = BgmPlayer.Current?.Volume ?? 1;
            _preview.Play();
        }
        catch (Exception ex)
        {
            NoticeDialog.Show(this, $"틀지 못했습니다 — {ex.Message}");
            return;
        }
        if (!_gamePaused) { BgmPlayer.Current?.Pause(); _gamePaused = true; }
        _playing = (button, label);
        SetLabel(button, "■ 멈춤");
    }

    private void StopPreview()
    {
        if (_playing is { } now) SetLabel(now.Button, now.Label);
        _playing = null;
        _preview.Stop();
        if (_gamePaused) { BgmPlayer.Current?.Resume(); _gamePaused = false; }
    }

    private static void SetLabel(Border button, string text)
    {
        if (button.Child is TextBlock block) block.Text = text;
    }

    /// <summary>줄 하나 — 곡 번호·이름, 지금 갈아 끼운 파일, 「선택…」·「기본으로」.</summary>
    private void AddRow(Grid rows, int track, string label)
    {
        int row = rows.RowDefinitions.Count;
        rows.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var current = new TextBlock
        {
            Text = FileLabel(GameSettings.CustomBgmTrackPath(track)),
            Foreground = GameUi.Text,
            FontSize = 12,
            Opacity = 0.8,
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxWidth = 150,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(8, 0, 0, 0),
        };

        Border? mine = null;
        void Refresh()
        {
            current.Text = FileLabel(GameSettings.CustomBgmTrackPath(track));
            if (mine != null) mine.Opacity = GameSettings.CustomBgmTrackPath(track) == null ? 0.4 : 1;
        }

        var pick = GameUi.PushButton("선택…", () =>
        {
            var box = new OpenFileDialog
            {
                Title = $"{label}({track}번) 곡으로 쓸 파일",
                Filter = "오디오 파일 (*.mp3;*.wav;*.wma;*.m4a)|*.mp3;*.wav;*.wma;*.m4a|모든 파일 (*.*)|*.*",
            };
            if (box.ShowDialog(this) != true) return;
            GameSettings.SetCustomBgmTrack(track, box.FileName);
            Refresh();
        }, 72);
        pick.Margin = new Thickness(8, 0, 0, 0);

        var clear = GameUi.PushButton("기본으로", () =>
        {
            if (_playing is { } now && now.Button == mine) StopPreview();
            GameSettings.SetCustomBgmTrack(track, null);
            Refresh();
        }, 80);
        clear.Margin = new Thickness(6, 0, 0, 0);

        // 미리 듣기 — 원곡(게임 폴더 · 받아 둔 곡)과 넣은 곡을 견주어 들을 수 있게(요청).
        Border original = null!;
        original = GameUi.PushButton("▶ 원곡", () => Preview(original, "▶ 원곡", BgmPlayer.OriginalPath(track)), 80);
        original.Margin = new Thickness(10, 0, 0, 0);
        mine = GameUi.PushButton("▶ 넣은 곡", () => Preview(mine!, "▶ 넣은 곡", GameSettings.CustomBgmTrackPath(track)), 92);
        mine.Margin = new Thickness(6, 0, 0, 0);
        Refresh();

        var number = new TextBlock
        {
            Text = $"{track,2}",
            Foreground = GameUi.Text,
            Opacity = 0.6,
            FontFamily = new FontFamily("Consolas"),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0),
        };
        var name = new TextBlock
        {
            Text = label,
            Foreground = GameUi.Text,
            FontWeight = FontWeights.Bold,
            FontSize = 13,
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Center,
        };

        Put(rows, row, 0, number);
        Put(rows, row, 1, name);
        Put(rows, row, 2, current);
        Put(rows, row, 3, pick);
        Put(rows, row, 4, clear);
        Put(rows, row, 5, original);
        Put(rows, row, 6, mine);
    }

    private static void Put(Grid grid, int row, int column, FrameworkElement cell)
    {
        cell.Margin = new Thickness(cell.Margin.Left, 4, cell.Margin.Right, 4);
        Grid.SetRow(cell, row);
        Grid.SetColumn(cell, column);
        grid.Children.Add(cell);
    }

    private static string FileLabel(string? path) =>
        string.IsNullOrEmpty(path) ? "(기본)" : Path.GetFileName(path);

    public static void Show(Window owner) => new CustomBgmDialog { Owner = owner }.ShowDialog();
}
