using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using CdsHelper.Support.Local.Helpers;

namespace CdsHelper.Game.UI.Views;

/// <summary>
/// 게임을 끝낼 때 뜨는 마지막 그림 — <c>MISC.CDS</c> 파트 9(640x480)를 파트 10 팔레트로 찍는다.
/// </summary>
/// <remarks>
/// 게임을 끝내는 함수 <c>0x00410F90</c> 이 늘 <c>0x004068E0(0)</c> 을 부른다. 메인메뉴에서 나가거나
/// (판 고리가 끝나면 <c>0x0047A7BC</c>), CONTINUE? 에 아니오(<c>0x00410CF6</c> 이 비트 0x40 을 세워 고리를
/// 빠져나감), 창을 닫을 때(<c>0x00410F60</c> → <c>0x00410F87</c>) 모두 이 길로 간다. 놀이 중
/// 「게임 종료」는 첫 화면으로만 돌아가므로 여기를 안 지난다.
/// <code>
///   406922  0x004BA213(0, 0x100, 검정, 0x1E, 1)   ; 지금 화면을 30걸음에 검게
///   406994  0x00463680(9, 그림)   ; MISC.CDS(0x00552890) 파트 9 — 640x480 색인
///   4069d1  화면 가운데에 찍는다 ((화면 폭 − 640)/2, (화면 높이 − 480)/2)
///   4069e2  0x00463680(10, 팔레트) ; 파트 10 — 256색 RGB
///   4069f7  0x004BA213(0, 0x100, 팔레트, 0x1E, 1) ; 30걸음에 밝아진다. 한 걸음 = 1 x 50/3 ms(0x004BA39E)
///   406a28  0x00428140(0)          ; 누른 것을 다 뗄 때까지 기다리고
///   406a32  0x004280D0(0x64)       ; 누르거나 키를 치거나 100참(한 참 50ms, 0x004BA4BB) — 5초
///   406a3c  눌렀으면(1) 곧장 끝낸다 — 어두워지지 않는다
///   406a62  0x004BA213(0, 0x100, 검정, 0x3C, 1)   ; 시간이 다 됐으면 60걸음에 검게
/// </code>
/// 팔레트를 움직이는 대신 검은 판과 그림의 불투명도를 같은 걸음으로 올리고 내린다 — 검정과 제 색
/// 사이를 곧게 섞는 것이 팔레트 페이드와 같다.
/// </remarks>
internal static class ExitSplash
{
    /// <summary>그림이 든 파트.</summary>
    private const int PicturePart = 9;

    /// <summary>팔레트가 든 파트.</summary>
    private const int PalettePart = 10;

    private const int PictureWidth = 640, PictureHeight = 480;

    /// <summary>검게 가라앉는 걸음 수 · 밝아지는 걸음 수(<c>0x00406918</c> · <c>0x004069ED</c> 의 <c>0x1E</c>).</summary>
    private const int FadeInSteps = 30;

    /// <summary>시간이 다 돼 어두워지는 걸음 수(<c>0x00406A58</c> 의 <c>0x3C</c>).</summary>
    private const int FadeOutSteps = 60;

    /// <summary>한 걸음 — <c>0x004BA39E(1)</c> 이 <c>1 x 50 / 3</c> ms 를 쉰다.</summary>
    private static readonly TimeSpan Step = TimeSpan.FromMilliseconds(50.0 / 3);

    /// <summary>누르지 않으면 이만큼 둔다(<c>0x004280D0(100)</c>, 한 참 50ms).</summary>
    private static readonly TimeSpan Hold = TimeSpan.FromSeconds(5);

    /// <summary>
    /// 그림을 띄우고 다 걷힐 때까지 기다린다. 그림을 못 읽거나 창이 내려가 있으면 곧장 돌아간다.
    /// </summary>
    public static void Show(Window owner, string gameDirectory)
    {
        if (owner.WindowState == WindowState.Minimized || !owner.IsVisible) return;
        if (Load(gameDirectory) is not { } picture) return;

        var black = new Border { Background = Brushes.Black, Opacity = 0 };
        var image = new Image
        {
            Source = picture,
            Stretch = Stretch.Uniform,
            Opacity = 0,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.NearestNeighbor);

        var splash = new Window
        {
            Owner = owner,
            WindowStyle = WindowStyle.None,
            ResizeMode = ResizeMode.NoResize,
            AllowsTransparency = true,
            Background = Brushes.Transparent,
            ShowInTaskbar = false,
            WindowStartupLocation = WindowStartupLocation.Manual,
            Content = new Grid { Children = { black, image } },
        };
        // 최대화된 창은 Left·Top 이 복원 자리를 내므로 동영상 창과 같은 갈래로 덮는다.
        MoviePlayer.Cover(splash, owner);

        var frame = new DispatcherFrame();
        int step = 0;
        int phase = 0;              // 0 검게 · 1 밝아짐 · 2 기다림 · 3 어두워짐
        var held = DateTime.MinValue;

        // 기다리는 동안 누르면 어두워지지 않고 곧장 끝낸다(0x00406A3C).
        void Press()
        {
            if (phase == 2) frame.Continue = false;
        }
        splash.PreviewMouseDown += (_, e) => { e.Handled = true; Press(); };
        // 누른 채로 있던 키의 되풀이는 치지 않는다 — 원본도 다 뗀 뒤부터 센다(0x00428140).
        splash.PreviewKeyDown += (_, e) => { e.Handled = true; if (!e.IsRepeat) Press(); };

        var clock = new DispatcherTimer(Step, DispatcherPriority.Render, (_, _) =>
        {
            switch (phase)
            {
                case 0:
                    black.Opacity = Math.Min(1.0, ++step / (double)FadeInSteps);
                    if (step >= FadeInSteps) { phase = 1; step = 0; }
                    break;
                case 1:
                    image.Opacity = Math.Min(1.0, ++step / (double)FadeInSteps);
                    if (step >= FadeInSteps) { phase = 2; held = DateTime.UtcNow; }
                    break;
                case 2:
                    if (DateTime.UtcNow - held >= Hold) { phase = 3; step = 0; }
                    break;
                default:
                    image.Opacity = Math.Max(0.0, 1.0 - ++step / (double)FadeOutSteps);
                    if (step >= FadeOutSteps) frame.Continue = false;
                    break;
            }
        }, splash.Dispatcher);

        splash.Closed += (_, _) => frame.Continue = false;
        splash.Show();
        splash.Activate();
        clock.Start();
        try
        {
            Dispatcher.PushFrame(frame);
        }
        finally
        {
            clock.Stop();
            splash.Close();
        }
    }

    /// <summary>파트 9 를 파트 10 팔레트로 푼다. 못 하면 null.</summary>
    private static BitmapSource? Load(string gameDirectory)
    {
        if (gameDirectory.Length == 0) return null;
        try
        {
            string path = CdsAssetPath.Resolve(gameDirectory, "MISC.CDS");
            if (!File.Exists(path)) return null;
            var archive = Ls12Reader.Open(path);
            if (archive == null || archive.PartCount <= PalettePart) return null;

            var index = archive.Decode(PicturePart);
            var palette = archive.Decode(PalettePart);
            if (index == null || index.Length < PictureWidth * PictureHeight
                || palette == null || palette.Length < 256 * 3)
                return null;

            // 팔레트는 R·G·B 차례 8비트다(파트 10 첫 줄 000000 800000 008000 …).
            var bgra = new uint[PictureWidth * PictureHeight];
            for (int i = 0; i < bgra.Length; i++)
            {
                int k = index[i] * 3;
                bgra[i] = 0xFF000000u | (uint)(palette[k] << 16 | palette[k + 1] << 8 | palette[k + 2]);
            }
            var bmp = BitmapSource.Create(PictureWidth, PictureHeight, 96, 96, PixelFormats.Bgra32, null,
                                          bgra, PictureWidth * 4);
            bmp.Freeze();
            return bmp;
        }
        catch (IOException)
        {
            return null;
        }
    }
}
