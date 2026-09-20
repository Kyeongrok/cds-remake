using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CdsHelper.Game.Local.Helpers;

namespace CdsHelper.Game.UI.Views;

/// <summary>
/// 지도 창 — <b>항해지도</b>(밝힌 바다)와 <b>주변지도</b>(배 둘레)가 같은 창을 쓴다.
/// </summary>
/// <remarks>
/// 게임은 창 바탕(<c>0x00457ED0</c>, 파생 vtable <c>0x004D1158</c>)을 스택에 세우고
/// 여는 손 <c>0x00416970(this, 제목)</c> 으로 띄운다.
/// <code>
///   0x4B6963(640, 344)   겉 크기 640 x 344, 화면 가운데(0x458510)
///   0x458020(… 0x19, 제목)  테(0x01) · 0x08 · 닫기 단추(0x10, id 0x997) + 제목 띠(0x998)
///   0x459CC0             모달 고리 — 닫기 단추 클릭이나 Esc(0x413E60)로 끝난다
///   0x4168F0             그리기 — 625 x 313 그림을 안쪽 가운데에 <b>1:1</b> 로 옮긴다
/// </code>
/// 그림(<c>this+0xA0</c>, 625 x 313 바이트)은 <b>열 때 한 번</b> 짓고 닫을 때까지 안 바뀐다.
/// 글자·범례·눈금·호버·스크롤은 하나도 없다. 제목은 <c>0x0055F1A8</c> "항해지도" /
/// <c>0x0055F1F0</c> "주변지도" 다.
///
/// 그림은 지도 쪽(<see cref="ShipMapHost.Chart"/> · <see cref="ShipMapHost.LocalChart"/>)이
/// 짓는다. 밝힘은 주인공이 든다(<see cref="Support.Local.Models.ExploredMap"/>) — 세이브에 함께 적힌다.
/// </remarks>
public sealed class SeaChartDialog : GameWindow
{
    /// <summary>창 겉 크기 — <c>0x4B6963(0x280, 0x158)</c>.</summary>
    private const double OuterW = 640, OuterH = 344;

    private SeaChartDialog(BitmapSource chart, string title)
    {
        Title = title;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        Width = OuterW;
        Height = OuterH;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        Background = GameUi.Back;

        // 늘리지 않는다 — 그림 한 점이 창 한 점이다(0x4B5CDF 1:1 복사).
        var image = new Image
        {
            Source = chart,
            Width = chart.PixelWidth,
            Height = chart.PixelHeight,
            Stretch = Stretch.None,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        RenderOptions.SetBitmapScalingMode(image, GameUi.SpriteScaling);
        RenderOptions.SetEdgeMode(image, EdgeMode.Aliased);

        var bar = GameUi.TitleBar(title, Close);
        DockPanel.SetDock(bar, Dock.Top);

        var dock = new DockPanel { LastChildFill = true };
        dock.Children.Add(bar);
        // 안쪽 가운데 — 왼 = 안쪽.left + (안쪽 폭 - 625) / 2, 위도 같다(0x4168F0).
        dock.Children.Add(new Border { ClipToBounds = true, Child = image });

        Content = new Border
        {
            Background = GameUi.Back,
            BorderBrush = GameUi.EdgeDark,
            BorderThickness = new Thickness(1),
            Child = dock,
        };

        KeyDown += (_, e) => { if (e.Key is System.Windows.Input.Key.Escape) Close(); };
        GameUi.EnableDrag(this, bar);
    }

    /// <summary>
    /// 항해지도를 편다 — 밝힌 자리만 드러나고 표식은 없다(<c>0x00416A00</c>).
    /// </summary>
    public static void ShowWorld(Window owner, ShipMapHost host,
                                 Support.Local.Models.ExploredMap seen) =>
        Open(owner, "항해지도", host.Chart(seen, out int w, out int h), w, h);

    /// <summary>
    /// 주변지도를 편다 — 배 둘레를 크게 본다(<c>0x00416B60</c>). 알고 서 있는 도시와
    /// <b>이미 찾았거나 발표된</b> 발견물의 그림 칸이 밝아진다.
    /// </summary>
    /// <param name="cityShown">지도에 뜨는 도시인지(알고 서 있는지).</param>
    /// <param name="survey">측량술. 한 점이 <c>(측량 + 2) / 16</c> 칸이다.</param>
    public static void ShowAround(Window owner, ShipMapHost host,
                                  Engine.Discovery.DiscoveryLog? log,
                                  Support.Local.Models.Player player,
                                  Func<int, bool>? cityShown, int survey) =>
        Open(owner, "주변지도",
             host.LocalChart(log, player, cityShown, survey, out int w, out int h), w, h);

    /// <summary>그림을 창에 담아 띄운다. 못 지었으면 그 까닭을 알린다.</summary>
    private static void Open(Window owner, string title, uint[]? bgra, int w, int h)
    {
        if (bgra == null)
        {
            ConfirmDialog.Tell(owner, "지도를 읽지 못했습니다");
            return;
        }

        var bmp = BitmapSource.Create(w, h, 96, 96, PixelFormats.Bgra32, null, bgra, w * 4);
        bmp.Freeze();
        new SeaChartDialog(bmp, title) { Owner = owner }.ShowDialog();
    }
}
