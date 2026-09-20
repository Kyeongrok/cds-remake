using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CdsHelper.Game.Local.Helpers;

namespace CdsHelper.Game.UI.Views;

/// <summary>
/// 지도 한가운데에 사건 스틸(<c>EVSTILL.CDS</c>) 한 장을 세워 둔다 — 그 위로 부관 대사 창이 뜬다.
/// </summary>
/// <remarks>
/// 게임의 <c>0x00472FA0(번호)</c> 가 스틸을 세우고 <c>0x00473160</c> 이 걷는다. 해상재해는
/// 재해가 <b>실제로 터졌을 때만</b> 그림을 세운다(볼트 79).
/// <code>
///   0x004747C4  쥐        #2
///   0x004748EA  괴혈병    #1
///   0x00474A96  전염병    #1
///   0x00475317  반란      #0
/// </code>
/// 귀띔(「모두 약해져…」·「이상한 병…」)과 막아 낸 경우에는 그림이 없다.
///
/// 원본은 640x480 화면에 320x240 을 세운다 — 화면의 절반이다. 우리 지도는 크기가 들쭉날쭉해
/// 지도 폭·키의 절반쯤이 되도록 정수 배로 키운다.
/// </remarks>
internal sealed class EventStillPopup : Window
{
    /// <summary>해상재해·반란 스틸 번호.</summary>
    public const int Mutiny = 0, Sickness = 1, Rats = 2;

    /// <summary>액자의 까만 줄 두께와 밤색 판 폭. 발견물 그림 액자(<see cref="DiscoveryDialog"/>)와 같다.</summary>
    private const double FrameLine = 1, FrameWide = 8;

    /// <summary>액자의 까만 줄과 밤색 판.</summary>
    private static readonly Brush FrameEdge = FrozenBrush(Color.FromRgb(0x11, 0x09, 0x09));
    private static readonly Brush FrameFill = FrozenBrush(Color.FromRgb(0x4A, 0x2E, 0x24));

    private static Brush FrozenBrush(Color c)
    {
        var b = new SolidColorBrush(c);
        b.Freeze();
        return b;
    }

    private EventStillPopup(BitmapSource art, int scale, Rect area)
    {
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        ShowActivated = false;
        SizeToContent = SizeToContent.WidthAndHeight;
        Background = FrameFill;

        var image = new Image
        {
            Source = art,
            Width = art.PixelWidth * scale,
            Height = art.PixelHeight * scale,
            Stretch = Stretch.Fill,
        };
        RenderOptions.SetBitmapScalingMode(image, GameUi.SpriteScaling);
        RenderOptions.SetEdgeMode(image, EdgeMode.Aliased);

        // 게임은 스틸 뒤에 <b>밤색 판</b>을 깔고 까만 줄을 두 겹 두른다 — 발견물 그림 액자와 같은 꼴이다
        // (까만 줄 · 밤색 판 · 까만 줄). 예전에는 그림만 까만 창에 올렸다.
        Content = new Border
        {
            Background = FrameFill,
            BorderBrush = FrameEdge,
            BorderThickness = new Thickness(FrameLine),
            Padding = new Thickness(FrameWide),
            Child = new Border
            {
                BorderBrush = FrameEdge,
                BorderThickness = new Thickness(FrameLine),
                Child = image,
            },
        };

        double frameGrow = (FrameLine + FrameWide + FrameLine) * 2;
        if (area.Width > 0)
        {
            WindowStartupLocation = WindowStartupLocation.Manual;
            Left = area.X + (area.Width - image.Width - frameGrow) / 2;
            Top = area.Y + (area.Height - image.Height - frameGrow) / 2;
        }
        else
        {
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
        }
    }

    /// <summary>
    /// 스틸을 세운다. 못 읽으면 null — 그때는 대사 창만 뜬다. 다 쓰면 부른 쪽이 닫는다.
    /// </summary>
    public static Window? Open(Window owner, Engine.Game game, int picture, Rect area)
    {
        if (game.EventStills?.TryGetBgra(picture, out int w, out int h) is not { } bgra) return null;

        var bmp = BitmapSource.Create(w, h, 96, 96, PixelFormats.Bgra32, null, bgra, w * 4);
        bmp.Freeze();

        double areaW = area.Width > 0 ? area.Width : owner.ActualWidth;
        double areaH = area.Height > 0 ? area.Height : owner.ActualHeight;
        int scale = Math.Max(1, Math.Min(4, (int)Math.Min(areaW * 0.5 / w, areaH * 0.5 / h)));

        var popup = new EventStillPopup(bmp, scale, area) { Owner = owner };
        popup.Show();
        return popup;
    }
}
