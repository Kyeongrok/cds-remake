using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using CdsHelper.Game.Local.Helpers;

namespace CdsHelper.Game.UI.Views;

/// <summary>
/// 발견 대본의 <b>움직이는 그림</b>(DISCOVER.CDS) 한 편을 가운데에 틀고 닫힌다.
/// </summary>
/// <remarks>
/// 대본 명령 <c>00 0C [u16 n]</c> 이 하는 일이다(<c>0x00408429</c> → <c>0x00466C90</c>).
/// 게임은 240x176 을 화면 가운데에 놓고(<c>0x005AA2D0</c> 틀의 한가운데) 장마다 기다리기
/// 손 <c>0x00459CC0</c> 을 한 번씩 돈다 — 기다리는 값은 <c>0x14 / 10 = 2</c> 이다.
/// 그 눈금이 몇 밀리초인지는 아직 못 짚어 <see cref="FrameTime"/> 은 눈대중이다.
/// </remarks>
public static class DiscoveryClipPlayer
{
    /// <summary>한 장이 머무는 참. 원본 눈금(2)을 어림한 값이다.</summary>
    private static readonly TimeSpan FrameTime = TimeSpan.FromMilliseconds(100);

    /// <summary>그 편을 끝까지 틀고 돌아온다. 못 풀면 아무 일도 없다.</summary>
    public static void Play(Window owner, DiscoveryClips? clips, int clip)
    {
        var frames = clips?.Frames(clip);
        if (frames == null || frames.Length == 0) return;

        var bitmaps = new BitmapSource[frames.Length];
        for (int i = 0; i < frames.Length; i++)
        {
            var bitmap = BitmapSource.Create(DiscoveryClips.Width, DiscoveryClips.Height, 96, 96,
                                             PixelFormats.Bgra32, null, frames[i],
                                             DiscoveryClips.Width * 4);
            bitmap.Freeze();
            bitmaps[i] = bitmap;
        }

        double zoom = GameUi.PixelZoom(owner, 2);
        var image = new Image
        {
            Width = DiscoveryClips.Width,
            Height = DiscoveryClips.Height,
            Stretch = Stretch.Fill,
            Source = bitmaps[0],
            LayoutTransform = new ScaleTransform(zoom, zoom),
        };
        RenderOptions.SetBitmapScalingMode(image, GameUi.SpriteScaling);

        var screen = new Window
        {
            Owner = owner,
            WindowStyle = WindowStyle.None,
            ResizeMode = ResizeMode.NoResize,
            ShowInTaskbar = false,
            SizeToContent = SizeToContent.WidthAndHeight,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = Brushes.Black,
            Content = image,
        };

        int at = 0;
        var clock = new DispatcherTimer { Interval = FrameTime };
        clock.Tick += (_, _) =>
        {
            if (++at >= bitmaps.Length)
            {
                clock.Stop();
                screen.Close();
                return;
            }
            image.Source = bitmaps[at];
        };

        // 기다리기 싫으면 눌러서 넘긴다 — 동영상(MoviePlayer)과 같다.
        screen.MouseLeftButtonUp += (_, _) => screen.Close();
        screen.MouseRightButtonUp += (_, _) => screen.Close();
        screen.KeyDown += (_, _) => screen.Close();
        screen.Loaded += (_, _) => clock.Start();
        screen.Closed += (_, _) => clock.Stop();

        screen.ShowDialog();
    }
}
