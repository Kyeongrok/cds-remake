using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CdsHelper.Game.Local.Helpers;
using CdsHelper.Support.Local.Models;

namespace CdsHelper.Game.UI.Views;

/// <summary>
/// 조선소에서 마스트를 세우거나 돛을 바꾸는 동안 뜨는 <b>배 그림 창</b>(<see cref="ShipStill"/>).
/// </summary>
/// <remarks>
/// 원본은 두 개조를 시작할 때 건물 사진을 걷고(<c>0x004A21C0</c>) 이 창을 세운 뒤(<c>0x00494900</c>),
/// 끝나면 창을 부수고(<c>0x00494A20</c>) 사진을 되살린다(<c>0x004A21F0</c> · <c>0x004A2180</c>).
/// 그림 크기가 사진과 같은 320x240 이라 우리도 <b>사진 자리</b>에 앉힌다.
/// 보여 주기만 하므로 초점을 뺏지 않는다(<see cref="Window.ShowActivated"/> = false).
/// </remarks>
public sealed class ShipStillWindow : GameWindow
{
    private readonly Image _image;
    private readonly string _gameDirectory;

    private ShipStillWindow(string gameDirectory, int scale)
    {
        _gameDirectory = gameDirectory;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.Manual;
        WindowStartupLocation = WindowStartupLocation.Manual;
        ShowInTaskbar = false;
        ShowActivated = false;
        Background = Brushes.Black;
        Width = ShipStill.Width * scale;
        Height = ShipStill.Height * scale;

        _image = new Image { Width = Width, Height = Height, Stretch = Stretch.Fill, IsHitTestVisible = false };
        RenderOptions.SetBitmapScalingMode(_image, GameUi.SpriteScaling);
        RenderOptions.SetEdgeMode(_image, EdgeMode.Aliased);
        Content = _image;
    }

    /// <summary>배가 바뀌었으면 다시 그린다(<c>0x004949E0</c>). 그림을 못 지으면 그대로 둔다.</summary>
    public void Redraw(Ship ship)
    {
        if (ShipStill.Compose(_gameDirectory, ship) is not { } bgra) return;
        var picture = BitmapSource.Create(ShipStill.Width, ShipStill.Height, 96, 96,
                                          PixelFormats.Bgra32, null, bgra, ShipStill.Width * 4);
        picture.Freeze();
        _image.Source = picture;
    }

    /// <summary>
    /// 왼쪽 위 모서리를 <paramref name="at"/>(화면 좌표, WPF 단위)에 맞춰 띄운다.
    /// <c>SHIPSTIL.CDS</c> 를 못 읽었으면 null — 그림은 덤이라 개조는 그대로 간다.
    /// </summary>
    public static ShipStillWindow? Show(Window owner, string gameDirectory, Ship ship, int scale, Point at)
    {
        if (ShipStill.Compose(gameDirectory, ship) == null) return null;

        var window = new ShipStillWindow(gameDirectory, scale) { Owner = owner };
        window.Redraw(ship);
        window.Closing += (_, _) => window.Owner?.Activate();
        window.Show();
        window.Left = Math.Max(0, Math.Min(at.X, SystemParameters.VirtualScreenWidth - window.Width));
        window.Top = Math.Max(0, Math.Min(at.Y, SystemParameters.VirtualScreenHeight - window.Height));
        return window;
    }
}
