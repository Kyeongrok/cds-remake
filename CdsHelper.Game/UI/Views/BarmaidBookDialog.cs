using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CdsHelper.Game.Engine.Town;
using CdsHelper.Game.Local.Helpers;
using CdsHelper.Support.Local.Models;

namespace CdsHelper.Game.UI.Views;

/// <summary>
/// 낯을 튼 여급들 — 어느 마을 누구고, 궁합은 맞는지, 얼마나 친해졌는지.
/// </summary>
/// <remarks>
/// 놀이에는 없는 창이다. 여급이 127명이고 마을마다 해가 가며 갈리는 데다
/// (<see cref="BarmaidTable"/>), <b>궁합은 초상화 번호 하나로 갈리는데</b> 화면에서는
/// 그것을 볼 길이 없어서 둔다.
///
/// <b>한잔 산 사람만 나온다</b> — 친밀도가 0 이면 아직 이름도 모르는 사이다.
/// </remarks>
public sealed class BarmaidBookDialog : GameWindow
{
    private BarmaidBookDialog(Player player, BarmaidTable table, Portraits? faces,
                              CityTable cities)
    {
        Title = "여급 수첩";
        Width = 720;
        Height = 560;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        int mine = Barmaids.FortuneOf(player);

        var rows = new StackPanel { Margin = new Thickness(10) };
        rows.Children.Add(new TextBlock
        {
            Text = $"내 궁합 코드 {mine}  (초상화 {player.Face} · {player.Age}세"
                 + $"{(player.Age >= BarmaidTable.AgedFrom ? " → +16" : "")})",
            FontWeight = FontWeights.Bold,
            Margin = new Thickness(0, 0, 0, 8),
        });

        var met = table.Barmaids
                       .Where(b => player.LikingOf(b.Id) > 0)
                       .OrderByDescending(b => player.LikingOf(b.Id))
                       .ToList();

        if (met.Count == 0)
        {
            rows.Children.Add(new TextBlock
            {
                Text = "아직 낯을 튼 여급이 없습니다. 술집에서 한잔 사 보세요.",
                Foreground = Brushes.Gray,
            });
        }

        foreach (var her in met) rows.Children.Add(Card(her, player, faces, cities, mine));

        Content = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = rows,
        };
    }

    /// <summary>한 사람 — 얼굴과 신상, 그리고 친밀도 막대.</summary>
    private static UIElement Card(in BarmaidTable.Barmaid her, Player player, Portraits? faces,
                                  CityTable cities, int mine)
    {
        int liking = player.LikingOf(her.Id);
        bool destined = BarmaidTable.Destined(mine, her.Fortune);
        bool wife = player.SpouseId == her.Id;

        var line = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 0, 0, 8),
        };

        var px = faces?.TryGetBgra(her.Face, female: true);
        if (px != null)
        {
            var bmp = BitmapSource.Create(Portraits.Width, Portraits.Height, 96, 96,
                                          PixelFormats.Bgra32, null, px, Portraits.Width * 4);
            bmp.Freeze();
            var image = new Image { Source = bmp, Width = Portraits.Width, Height = Portraits.Height };
            RenderOptions.SetBitmapScalingMode(image, GameUi.SpriteScaling);
            line.Children.Add(image);
        }

        var text = new StackPanel { Margin = new Thickness(10, 0, 0, 0), Width = 560 };
        text.Children.Add(new TextBlock
        {
            Text = $"{her.Name}   {cities.NameOf(her.City)}   {her.Year}년~"
                 + (wife ? "   ♥ 아내" : destined ? "   ★ 운명의 반려자" : ""),
            FontWeight = FontWeights.Bold,
            FontSize = 15,
        });
        text.Children.Add(new TextBlock
        {
            Text = $"궁합 코드 {her.Fortune}  ·  {her.PersonalityName}  ·  {her.BloodName}"
                 + $"  ·  별자리 {her.Zodiac}",
            Margin = new Thickness(0, 2, 0, 2),
        });
        text.Children.Add(new TextBlock { Text = $"친밀도 {liking} / {BarmaidTable.MaxLiking}" });
        text.Children.Add(new Border
        {
            Height = 8,
            Width = 300,
            HorizontalAlignment = HorizontalAlignment.Left,
            Background = Brushes.Gainsboro,
            Margin = new Thickness(0, 2, 0, 0),
            Child = new Border
            {
                HorizontalAlignment = HorizontalAlignment.Left,
                Width = 300.0 * liking / BarmaidTable.MaxLiking,
                Background = destined ? Brushes.Crimson : Brushes.SteelBlue,
            },
        });

        line.Children.Add(text);
        return line;
    }

    /// <summary>수첩을 편다. 표를 못 읽었으면 그 까닭을 알린다.</summary>
    public static void Show(Window owner, Engine.Game game)
    {
        if (game.Barmaids is not { } table)
        {
            ConfirmDialog.Tell(owner, "여급 표를 읽지 못했습니다");
            return;
        }

        new BarmaidBookDialog(game.Player, table, game.Faces, game.CityTable)
        {
            Owner = owner,
        }.ShowDialog();
    }
}
