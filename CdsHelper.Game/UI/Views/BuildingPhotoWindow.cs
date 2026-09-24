using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using System.Windows.Media;
using System.Windows;
using CdsHelper.Game.Local.Helpers;

namespace CdsHelper.Game.UI.Views;

/// <summary>
/// 건물에 들어가면 뜨는 타원 사진 창(<see cref="BuildingPhoto"/>). 도시 그림 창의
/// <b>오른쪽 아래</b>에 붙여 띄운다 — 게임도 그 자리에 낸다. 술집·여관이면 사진 앞에
/// 손님도 함께 세운다(<see cref="TavernGuests"/>).
/// </summary>
/// <remarks>
/// 그림 창 안에 그리지 않는다. 우리 창은 도시 그림 크기 그대로라(400x320) 320x240 짜리
/// 사진을 안에 얹으면 도시가 거의 가려진다. 명령 창과 같은 수로 제 창(HWND)을 쓴다
/// (<see cref="MenuWindow"/>).
///
/// <b>초점을 뺏지 않는다</b>(<see cref="Window.ShowActivated"/> = false). 이 창은 보여 주기만
/// 하는 것이라, 초점을 가져가면 방금 연 명령 창이 뒤로 밀려 방향키가 안 먹는다.
/// </remarks>
public sealed class BuildingPhotoWindow : GameWindow
{
    /// <summary>
    /// 손님 한 명. 그림과 크기, 커서를 올렸을 때 뜰 이름표, 눌렀을 때 할 일을 함께 든다.
    /// </summary>
    /// <param name="LiveName">있으면 커서를 올릴 때마다 이것으로 이름표를 다시 정한다 — 말을 걸어 낯을 트면
    /// 사진을 다시 세우지 않아도 이름이 뜬다.</param>
    public readonly record struct GuestArt(uint[] Bgra, int Width, int Height, string Name,
                                           Action? Click = null, Func<string>? LiveName = null);

    private BuildingPhotoWindow(uint[] photo, IReadOnlyList<GuestArt> guests, int scale)
    {
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.Manual;
        WindowStartupLocation = WindowStartupLocation.Manual;
        ShowInTaskbar = false;
        ShowActivated = false;
        AllowsTransparency = true;
        Background = Brushes.Transparent;   // 타원 밖은 비쳐야 한다

        double w = BuildingPhoto.Width * scale, h = BuildingPhoto.Height * scale;
        Width = w;
        Height = h;

        var canvas = new Canvas { Width = w, Height = h };
        var photoImage = Pixels(photo, BuildingPhoto.Width, BuildingPhoto.Height, scale);
        photoImage.IsHitTestVisible = false;      // 사진은 커서를 안 받는다
        canvas.Children.Add(photoImage);

        // 손님은 발끝을 사진 아래 끝에 맞추고 가로로 고르게 벌린다 — 게임도 그렇게 놓는다
        // (0x0042DBCE 가 y 를 "바닥 - 제 높이" 로, x 를 "왼쪽 + 간격 x i" 로 잡는다).
        // 키가 72~104 로 제각각인데도 한 줄에 서는 것이 그 셈이다.
        for (int i = 0; i < guests.Count; i++)
        {
            var g = guests[i];
            double left = (i + 0.5) * w / guests.Count - g.Width * scale / 2.0;

            var image = Pixels(g.Bgra, g.Width, g.Height, scale);
            Canvas.SetLeft(image, left);
            Canvas.SetTop(image, h - g.Height * scale);
            canvas.Children.Add(image);

            AddTag(canvas, image, g.Name, left + g.Width * scale / 2.0, h, g.LiveName);

            if (g.Click is { } run)
            {
                image.Cursor = Cursors.Hand;
                // 누름도 삼킨다 — 다른 창들과 같은 까닭이다(창 끌기에 먹히지 않게).
                image.MouseLeftButtonDown += (_, e) => e.Handled = true;
                image.MouseLeftButtonUp += (_, e) => { e.Handled = true; run(); };
            }
        }

        Content = canvas;
    }

    /// <summary>
    /// 손님에 커서를 올리면 발치에 뜨는 이름표. 세이브에서 그 술집에 앉힌 인물이면 그 이름을,
    /// 지나가는 손님이면 성별("남"·"여")을 낸다 — 게임이 그렇게 가른다.
    /// </summary>
    /// <remarks>
    /// 건물 이름표처럼 덩굴 띠를 두르지 않는다 — 게임 것은 짙은 판에 밝은 한 점 테를 두른
    /// 민 이름표다(<see cref="GameUi.HoverTag"/>).
    /// </remarks>
    private static void AddTag(Canvas canvas, Image guest, string name, double centerX, double bottom,
                               Func<string>? live = null)
    {
        var (tag, text) = GameUi.HoverTag(name);
        Panel.SetZIndex(tag, 10);
        canvas.Children.Add(tag);

        guest.MouseEnter += (_, _) =>
        {
            if (live != null) text.Text = live();
            tag.Visibility = Visibility.Visible;
            tag.UpdateLayout();
            double tw = tag.ActualWidth > 0 ? tag.ActualWidth : 48;
            double th = tag.ActualHeight > 0 ? tag.ActualHeight : 24;
            Canvas.SetLeft(tag, Math.Clamp(centerX - tw / 2, 0, Math.Max(0, canvas.Width - tw)));
            Canvas.SetTop(tag, bottom - th);       // 발치에 맞춰 놓는다
        };
        guest.MouseLeave += (_, _) => tag.Visibility = Visibility.Collapsed;
    }

    /// <summary>BGRA 한 장을 도트 그대로(안 섞고) 늘려 놓는다.</summary>
    private static Image Pixels(uint[] bgra, int width, int height, int scale)
    {
        var picture = BitmapSource.Create(width, height, 96, 96,
                                          PixelFormats.Bgra32, null, bgra, width * 4);
        picture.Freeze();

        var image = new Image
        {
            Source = picture,
            Width = width * scale,
            Height = height * scale,
            Stretch = Stretch.Fill,
        };
        RenderOptions.SetBitmapScalingMode(image, GameUi.SpriteScaling);
        RenderOptions.SetEdgeMode(image, EdgeMode.Aliased);
        return image;
    }

    /// <summary>
    /// 왼쪽 위 모서리를 정해 준 자리(화면 좌표, WPF 단위)에 맞춰 띄운다.
    /// 그림을 못 풀었으면 null.
    /// </summary>
    public static BuildingPhotoWindow? Show(Window owner, uint[]? photo,
                                            IReadOnlyList<GuestArt> guests, int scale, Point at)
    {
        if (photo == null) return null;

        var window = new BuildingPhotoWindow(photo, guests, scale) { Owner = owner };
        // 부수기 전에 주인 창을 띄워 둔다 — 안 그러면 초점이 다른 앱으로 샌다.
        window.Closing += (_, _) => window.Owner?.Activate();
        Local.Helpers.FocusWatch.KeepInApp(window);   // 그래도 새면 붙들어 온다
        window.Show();

        double w = window.Width, h = window.Height;
        window.Left = Math.Max(0, Math.Min(at.X, SystemParameters.VirtualScreenWidth - w));
        window.Top = Math.Max(0, Math.Min(at.Y, SystemParameters.VirtualScreenHeight - h));
        return window;
    }
}
