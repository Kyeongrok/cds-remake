using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using CdsHelper.Game.Local.Helpers;

namespace CdsHelper.Game.UI.Views;

/// <summary>
/// 바다에서 다가간 함대의 신상 쪽지 — 초상화와 국적 · 직업 · 함대 규모(모드 「접근 함대 정보」).
/// </summary>
/// <remarks>
/// 원본은 이름 한 줄 뒤에 「우호적으로 접근한다 · 습격한다 · 떠난다」만 묻는다. 무엇을 고를지 가늠하게
/// 그 고르기 창 위에 이 쪽지를 띄워 두고, 고르면 걷는다. 누르지 않는 쪽지라 닫기 단추가 없다.
/// </remarks>
public sealed class FleetCardDialog : GameWindow
{
    private FleetCardDialog(string name, uint[]? face, IReadOnlyList<(string Label, string Value)> lines)
    {
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.WidthAndHeight;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        ShowActivated = false;
        Background = GameUi.Back;

        var rows = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 4, 0) };
        foreach (var (label, value) in lines)
            rows.Children.Add(new GameUi.GameLabel(GameFont.WhiteColor)
            {
                Text = $"{GameUi.Pad(label, 8)}{value}",
                Bold = false,
                FallbackBrush = GameUi.Text,
                HorizontalAlignment = HorizontalAlignment.Left,
                Margin = new Thickness(0, 2, 0, 2),
            });

        var body = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(10, 8, 12, 10) };
        if (face != null)
        {
            var image = new Image { Source = Portraits.Bitmap(face), Width = Portraits.Width, Height = Portraits.Height };
            RenderOptions.SetBitmapScalingMode(image, Portraits.Scaling(image.Source));
            body.Children.Add(image);
        }
        body.Children.Add(rows);

        var stack = new StackPanel();
        stack.Children.Add(GameUi.TitleBar(name, null));
        stack.Children.Add(body);
        Content = GameUi.DialogEdge(stack);
    }

    /// <summary>쪽지를 띄운다 — 부른 쪽이 고르기를 마치면 닫는다.</summary>
    public static FleetCardDialog Open(Window owner, string name, uint[]? face,
                                       IReadOnlyList<(string Label, string Value)> lines)
    {
        var card = new FleetCardDialog(name, face, lines) { Owner = owner };
        card.Show();
        card.UpdateLayout();
        return card;
    }
}
