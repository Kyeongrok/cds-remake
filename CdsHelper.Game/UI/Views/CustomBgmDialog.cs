using System.IO;
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
/// 게임에서 실제로 도는 곡을 빠짐없이 덮는다.
/// </remarks>
public sealed class CustomBgmDialog : GameWindow
{
    private const double RowWidth = 620;

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
            Width = RowWidth,
            Margin = new Thickness(12, 8, 12, 8),
        };

        var rows = new StackPanel { Width = RowWidth, Margin = new Thickness(12, 0, 12, 4) };
        foreach (var (track, label) in BgmPlayer.KnownTracks)
            rows.Children.Add(Row(track, label));

        var scroll = new ScrollViewer
        {
            MaxHeight = 420,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = rows,
        };

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
        stack.Children.Add(scroll);
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
    }

    /// <summary>줄 하나 — 곡 이름·번호, 지금 갈아 끼운 파일, 「선택…」·「기본으로」.</summary>
    private UIElement Row(int track, string label)
    {
        var current = new TextBlock
        {
            Text = FileLabel(GameSettings.CustomBgmTrackPath(track)),
            Foreground = GameUi.Text,
            FontSize = 12,
            Opacity = 0.8,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Width = 220,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(6, 0, 6, 0),
        };

        void Refresh() => current.Text = FileLabel(GameSettings.CustomBgmTrackPath(track));

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

        var clear = GameUi.PushButton("기본으로", () =>
        {
            GameSettings.SetCustomBgmTrack(track, null);
            Refresh();
        }, 80);

        var line = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 4, 0, 4),
        };
        line.Children.Add(new TextBlock
        {
            Text = $"{track,2}",
            Foreground = GameUi.Text,
            Opacity = 0.6,
            FontFamily = new FontFamily("Consolas"),
            Width = 22,
            VerticalAlignment = VerticalAlignment.Center,
        });
        line.Children.Add(new TextBlock
        {
            Text = label,
            Foreground = GameUi.Text,
            FontWeight = FontWeights.Bold,
            FontSize = 13,
            Width = 210,
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Center,
        });
        line.Children.Add(current);
        line.Children.Add(pick);
        line.Children.Add(clear);
        return line;
    }

    private static string FileLabel(string? path) =>
        string.IsNullOrEmpty(path) ? "(기본)" : Path.GetFileName(path);

    public static void Show(Window owner) => new CustomBgmDialog { Owner = owner }.ShowDialog();
}
