using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using CdsHelper.Game.Local.Helpers;

namespace CdsHelper.Game.UI.Views;

/// <summary>
/// 지도 위 한가운데에서 MPEFFECT 동그란 애니메이션(동전·하트) 한 벌을 돌리고 닫힌다.
/// </summary>
/// <remarks>
/// 바다 조우(<c>0x004555B0</c>)는 도시 그림 없이 지도 위에서 동전을 돌린다 —
/// 「도망간다」는 <c>0x00455B8D</c> 에서 굴린 뒤 <c>0x00455B98</c> 에서, 「교섭한다」는
/// <c>0x004559C2</c> 에서 굴린 뒤 <c>0x004559CD</c> 에서 <c>0x004A6380(결과)</c>(파트 4)를 부른다.
/// 말이 안 통하는 적이면 굴림 없이 진 동전(<c>0x00455860</c>)이다. 멎은 쪽이 곧 결과다.
///
/// 그림을 까는 창이 없는 자리에서 쓰려고 <see cref="GateScene"/> 의 돌리기를 떼어 냈다.
/// 한 장 참은 그쪽과 같다(동전 100ms · 하트 140ms).
/// </remarks>
internal sealed class EffectPopup : Window
{
    /// <summary>
    /// 게임 점 하나를 몇 배로 그리는지. 말 창·단추처럼 <b>1배</b>다 — 2배(160점)는 원본보다 훨씬 컸다.
    /// </summary>
    private const int Scale = 1;

    private static readonly TimeSpan CoinSpan = TimeSpan.FromMilliseconds(100);
    private static readonly TimeSpan HeartSpan = TimeSpan.FromMilliseconds(140);

    private readonly Image _image = new() { Stretch = Stretch.Fill };

    private EffectPopup(Rect area)
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        ShowActivated = false;
        IsHitTestVisible = false;

        double side = EffectAnim.Size * Scale;
        Width = side;
        Height = side;
        _image.Width = side;
        _image.Height = side;
        RenderOptions.SetBitmapScalingMode(_image, GameUi.SpriteScaling);
        RenderOptions.SetEdgeMode(_image, EdgeMode.Aliased);
        Content = _image;

        if (area.Width > 0)
        {
            WindowStartupLocation = WindowStartupLocation.Manual;
            Left = area.X + (area.Width - side) / 2;
            Top = area.Y + (area.Height - side) / 2;
        }
        else
        {
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
        }
    }

    /// <summary>동전을 돌린다. 이기면 첫째 장, 지면 넷째 장에서 멎는다.</summary>
    public static void PlayCoin(Window owner, Engine.Game game, bool won, Rect area) =>
        Play(owner, game, EffectAnim.Coin, won, area);

    /// <summary>
    /// 한 벌을 끝까지 돌리고 닫는다. 그림을 못 읽으면 아무 일도 없다.
    /// </summary>
    public static void Play(Window owner, Engine.Game game, int anim, bool won, Rect area)
    {
        if (game.Effects is not { } effects) return;

        var popup = new EffectPopup(area) { Owner = owner };
        var span = anim == EffectAnim.Coin ? CoinSpan : HeartSpan;
        var art = new BitmapSource?[EffectAnim.FrameCount];

        popup.Show();
        try
        {
            foreach (int f in EffectAnim.Frames(anim, won))
            {
                if (art[f] == null)
                {
                    if (effects.TryGetBgra(anim, f) is not { } bgra) continue;
                    var bmp = BitmapSource.Create(EffectAnim.Size, EffectAnim.Size, 96, 96,
                                                  PixelFormats.Bgra32, null, bgra,
                                                  EffectAnim.Size * 4);
                    bmp.Freeze();
                    art[f] = bmp;
                }
                popup._image.Source = art[f];
                Wait(span);
            }
        }
        finally
        {
            popup.Close();
        }
    }

    /// <summary>화면이 멎지 않게 하면서 한 참 기다린다.</summary>
    private static void Wait(TimeSpan span)
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer(span, DispatcherPriority.Normal,
                                        (_, _) => frame.Continue = false,
                                        Dispatcher.CurrentDispatcher);
        try { Dispatcher.PushFrame(frame); }
        finally { timer.Stop(); }
    }
}
