using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace CdsHelper.Game.UI.Views;

/// <summary>
/// 동영상 한 편을 <b>화면 가득</b> 튼다. 다 돌거나 아무 데나 누르면 닫힌다.
/// </summary>
/// <remarks>
/// 발견물 동영상(<c>AVI\I{번호:00}_0000.AVI</c>)이 이 길로 나온다. 게임도 액자에 넣지
/// 않고 화면을 통째로 덮는다 — 320x240 짜리라 위아래에 검은 띠가 남는다.
///
/// DISEV 의 「미디어 · 동영상 · AVI n」 명령이 하는 일이 이것이다(<c>00 02 [u16]</c>,
/// <see cref="Engine.Disev.DisevScript"/> 의 「AVI 재생」). 지금은 발견 알림이 곧장
/// 부르지만, DISEV 를 돌리는 쪽이 생기면 그쪽이 이 손을 쓴다.
///
/// 코덱이 없어 못 틀면 <b>조용히 넘어간다</b> — 발견은 이미 적혔고 동영상은 덤이다.
/// </remarks>
public static class MoviePlayer
{
    /// <summary>
    /// 동영상을 화면 가득 틀고, 끝날 때까지 기다린다.
    /// </summary>
    /// <param name="owner">덮을 창. 이 창의 화면을 가득 채운다.</param>
    /// <param name="path">틀 파일. 없으면 아무 일도 없다.</param>
    public static void Play(Window owner, string? path)
    {
        if (string.IsNullOrEmpty(path) || !File.Exists(path)) return;

        var player = new MediaElement
        {
            Source = new Uri(path),
            LoadedBehavior = MediaState.Manual,
            UnloadedBehavior = MediaState.Close,
            // 가로세로 비를 지키며 늘린다 — 320x240 이 위아래 검은 띠를 남기고 채운다.
            Stretch = Stretch.Uniform,
        };

        var screen = new GameWindow
        {
            Owner = owner,
            WindowStyle = WindowStyle.None,
            ResizeMode = ResizeMode.NoResize,
            ShowInTaskbar = false,
            AllowsTransparency = false,
            Background = Brushes.Black,
            WindowStartupLocation = WindowStartupLocation.Manual,
            Content = new Grid { Background = Brushes.Black, Children = { player } },
        };

        // 주인 창을 그대로 덮는다. 주인이 최대화·전체화면이면 그 크기 그대로다.
        Cover(screen, owner);

        bool done = false;
        void Finish()
        {
            if (done) return;
            done = true;
            player.Stop();
            screen.Close();
        }

        // 다 돌면 닫힌다. 기다리기 싫으면 아무 데나 눌러도 닫힌다 — 게임도 그렇다.
        player.MediaEnded += (_, _) => Finish();
        // 코덱이 없거나 파일이 깨졌으면 그냥 지나간다.
        player.MediaFailed += (_, _) => Finish();
        screen.MouseLeftButtonUp += (_, _) => Finish();
        screen.MouseRightButtonUp += (_, _) => Finish();
        screen.KeyDown += (_, _) => Finish();

        screen.Loaded += (_, _) => player.Play();
        screen.Closed += (_, _) => player.Close();

        screen.ShowDialog();
    }

    /// <summary>주인 창이 놓인 자리를 그대로 덮는다.</summary>
    private static void Cover(Window screen, Window owner)
    {
        if (owner.WindowState == WindowState.Maximized)
        {
            // 최대화된 창은 Left·Top 이 제 자리를 안 알려 준다 — 작업 영역을 쓴다.
            screen.Left = SystemParameters.WorkArea.Left;
            screen.Top = SystemParameters.WorkArea.Top;
            screen.Width = SystemParameters.WorkArea.Width;
            screen.Height = SystemParameters.WorkArea.Height;
            return;
        }

        screen.Left = owner.Left;
        screen.Top = owner.Top;
        screen.Width = owner.ActualWidth > 0 ? owner.ActualWidth : owner.Width;
        screen.Height = owner.ActualHeight > 0 ? owner.ActualHeight : owner.Height;
    }
}
