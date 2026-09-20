using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CdsHelper.Game.Local.Helpers;

namespace CdsHelper.Game.UI.Views;

/// <summary>
/// 놀이가 끝났을 때 — 사건 스틸 한 장과 <c>CONTINUE?</c> 물음이다.
/// </summary>
/// <remarks>
/// 게임의 <c>0x00410CC2</c> 어름이다.
/// <code>
///   410CC2  끝난 까닭에 따라 그림 번호를 고른다 — 0x0B · 0x0C · 0x0D
///   410CD0  0x00472FA0(그림번호)          ; EVSTILL 한 장을 화면 가운데에 세운다
///   410CDA  0x0049E3E0(0x2002, "CONTINUE?", "게임을 다시 시작하겠습니까?")
/// </code>
/// 그림은 <c>EVSTILL.CDS</c> 에 있다 — 발견물 스틸과 짜임이 같아 같은 손으로 읽는다
/// (<see cref="Engine.Game.EventStills"/>).
///
/// 그림은 끝난 까닭(<c>0x005A4D18</c> 아래 세 비트, <c>0x0044AF40</c> 이 적는다)으로 고른다(<c>0x00410CA1</c> 뜀표).
/// <code>
///   0 반란·대본 4A·컨디션   4 일기토 죽음   5 감옥   6 사냥꾼에게 붙잡힘   → 0x0B
///   1 선원 0·배 0·극지방    2 해전 패배                                 → 0x0C
///   3 육상전 전멸(0x00449920)                                            → 0x0D
/// </code>
///
/// 바탕의 벽지 무늬는 타이틀 화면 것과 같은 그림이라 그것을 깔아 쓴다.
/// </remarks>
public sealed class GameOverDialog : GameWindow
{
    /// <summary>놀이가 끝나는 까닭마다의 그림 번호(<c>0x00410CC2</c>).</summary>
    public const int MutinyLost = 0x0B, FleetLost = 0x0C, LandLost = 0x0D;

    private bool _again;

    private GameOverDialog(DiscoveryStills? stills, int picture, Rect area)
    {
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Background = Brushes.Black;

        // <b>게임 화면만</b> 덮는다 — 제목 줄과 위·아래 띠는 그대로 보인다(원본도 띠가 남는다).
        // 잴 데가 없으면 부르는 쪽이 주인 창 자리를 준다.
        Left = area.Left;
        Top = area.Top;
        Width = area.Width;
        Height = area.Height;

        var page = new Grid { Background = Wallpaper() };

        // 그림을 <b>위쪽에</b> 세운다 — 물음창이 그 아래에 앉을 자리를 비워 두는 것이다.
        double drop = Height * DropRatio;
        var stack = new StackPanel
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, drop, 0, 0),
        };

        double tall = 0;
        if (Picture(stills, picture, out tall) is { } art) stack.Children.Add(art);

        page.Children.Add(stack);
        Content = page;

        // 그림이 다 깔린 뒤에 물음창을 그 위로 얹는다. 물음은 <b>여느 물음창</b>이다 —
        // 게임도 알림과 물음이 한 함수라(0x00469060) 여기만 딴 상자를 지을 까닭이 없다.
        Loaded += (_, _) =>
        {
            // 물음창을 <b>그림 아래로</b> 내려 앉힌다. 가운데에 두면 그림 한가운데를 덮는다.
            _again = ConfirmDialog.Ask(this, "게임을 다시 시작하겠습니까?", "CONTINUE?",
                place: box =>
                {
                    double want = Top + drop + tall + Gap;
                    double most = Top + Height - box.ActualHeight - Gap;
                    box.Top = Math.Min(want, Math.Max(Top + Gap, most));
                    // 손으로 앉히면 가로도 우리가 잡아야 한다 — 안 잡으면 창이 OS 기본 자리(화면 왼쪽 위)로 간다.
                    box.Left = Left + (Width - box.ActualWidth) / 2;
                });
            Close();
        };
    }

    /// <summary>그림과 물음창 사이, 그리고 화면 가장자리에 두는 틈.</summary>
    private const double Gap = 24;

    /// <summary>그림을 화면 높이의 이만큼 내려 앉힌다 — 원본이 위를 비워 둔다.</summary>
    private const double DropRatio = 0.2;

    /// <summary>벽지 무늬 — 타이틀 화면 것을 그대로 깐다.</summary>
    private static Brush Wallpaper() => ShipMapWindow.TitleBackground();

    /// <summary>사건 스틸 한 장을 밤색 액자에 넣는다. 못 읽으면 null.</summary>
    /// <param name="tall">액자까지 넣은 높이 — 물음창을 그 아래에 앉히는 데 쓴다.</param>
    private static UIElement? Picture(DiscoveryStills? stills, int picture, out double tall)
    {
        tall = 0;
        if (stills?.TryGetBgra(picture, out int w, out int h) is not { } bgra) return null;

        tall = h + 14;                      // 테두리 1 + 안쪽 여백 6, 위아래로

        var bmp = BitmapSource.Create(w, h, 96, 96, PixelFormats.Bgra32, null, bgra, w * 4);
        bmp.Freeze();

        var image = new Image { Source = bmp, Width = w, Height = h };
        RenderOptions.SetBitmapScalingMode(image, GameUi.SpriteScaling);
        RenderOptions.SetEdgeMode(image, EdgeMode.Aliased);

        return new Border
        {
            Background = GameUi.Back,
            BorderBrush = GameUi.Edge,
            BorderThickness = new Thickness(1),
            Padding = new Thickness(6),
            HorizontalAlignment = HorizontalAlignment.Center,
            Child = image,
        };
    }

    /// <summary>
    /// 놀이 끝을 알린다. 다시 시작하겠다고 하면 true.
    /// </summary>
    /// <param name="area">
    /// 덮을 자리(화면 좌표, WPF 단위). 게임 화면만 주면 제목 줄과 위·아래 띠가 남는다.
    /// 안 주거나 빈 자리면 주인 창을 통째로 덮는다.
    /// </param>
    /// <param name="bgm">놀이 끝 곡(8번)을 틀 이. 안 주면 곡은 그대로 돈다.</param>
    public static bool Show(Window owner, DiscoveryStills? stills, int picture = MutinyLost,
                            Rect? area = null, BgmPlayer? bgm = null)
    {
        var where = area is { Width: > 0, Height: > 0 } r
            ? r
            : new Rect(owner.Left, owner.Top,
                       owner.ActualWidth > 0 ? owner.ActualWidth : owner.Width,
                       owner.ActualHeight > 0 ? owner.ActualHeight : owner.Height);

        // 놀이가 끝나면 그 곡으로 갈린다. 되돌리지 않는다 — 여기서 나가면 타이틀이다.
        bgm?.Play(BgmPlayer.GameOverTrack);

        var dialog = new GameOverDialog(stills, picture, where) { Owner = owner };
        dialog.ShowDialog();
        return dialog._again;
    }
}
