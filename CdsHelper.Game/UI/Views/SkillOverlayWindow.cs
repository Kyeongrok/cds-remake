using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using CdsHelper.Game.Local.Helpers;
using CdsHelper.Game.Local.Settings;
using CdsHelper.Support.Local.Models;

namespace CdsHelper.Game.UI.Views;

/// <summary>
/// 기능·언어 쪽지 창 — 도시에 들어가면 <b>도시 그림 왼쪽</b>에 뜬다.
/// </summary>
/// <remarks>
/// 햄버거의 「기능·언어」 창(<see cref="SkillBoardDialog"/>)을 개발 창 체크상자
/// (<see cref="Local.Settings.GameSettings.ShowSkillOverlay"/>)로 켜고 끄는 겹쳐 보기로 옮긴 것이다.
/// 함대 쪽지(<see cref="FleetLabelWindow"/>)와 같은 꼴로 도시 창 밖에 제 창으로 붙고, 끌어 옮기면
/// 그 거리를 기억해 다음 도시에서도 그 자리에 뜬다. 초점은 뺏지 않는다.
/// </remarks>
public sealed class SkillOverlayWindow : Window
{
    /// <summary>도시 창과 쪽지 사이.</summary>
    private const double Gap = 10;

    /// <summary>주인 창 왼쪽 위에서 잰 자리. 한 번 옮겨 두면 앱이 도는 동안 그대로다.</summary>
    private static double _dx = double.NaN, _dy;

    private readonly Window _anchor;
    private readonly Border _frame;
    private readonly double _fontSize;

    /// <summary>막대 너비(글자 크기에 비례). 숫자가 막대 안에 들어가 짧게 둔다.</summary>
    private double BarWidth => _fontSize * 2.4;

    /// <summary>막대 색 — 누구 값이든 한 색이다(바랜 양피지).</summary>
    private static readonly SolidColorBrush BarFill = Brush("#F2E4C8");

    private SkillOverlayWindow(Window anchor, double fontSize)
    {
        _anchor = anchor;
        _fontSize = fontSize;

        Title = "기능·언어";
        Owner = anchor;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        ShowActivated = false;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        SizeToContent = SizeToContent.WidthAndHeight;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Cursor = Cursors.SizeAll;

        // 배경은 투명하게 — 도시 그림 옆 바다가 비친다. 글씨는 함대 쪽지처럼 흰 굵은 글씨에 까만 그림자다.
        // 완전히 투명한 자리는 마우스가 새므로 거의 투명한 바탕을 깔아 끌기를 받는다.
        _frame = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(0x01, 0x00, 0x00, 0x00)),
            Padding = new Thickness(4),
        };
        Content = _frame;

        // 아무 데나 눌러 끈다. 놓으면 새 자리를 적어 두고 초점을 도시 창에 돌려준다.
        MouseLeftButtonDown += (_, _) =>
        {
            DragMove();
            _dx = Left - _anchor.Left;
            _dy = Top - _anchor.Top;
            _anchor.Activate();
        };

        anchor.LocationChanged += OnAnchorMoved;
        anchor.SizeChanged += OnAnchorMoved;
        Closed += (_, _) =>
        {
            anchor.LocationChanged -= OnAnchorMoved;
            anchor.SizeChanged -= OnAnchorMoved;
        };
        SizeChanged += (_, _) => Place();
    }

    private void OnAnchorMoved(object? sender, EventArgs e) => Place();

    /// <summary>주인 창 왼쪽에 쪽지를 띄운다.</summary>
    public static SkillOverlayWindow Attach(Window anchor, Engine.Game game, double fontSize)
    {
        var note = new SkillOverlayWindow(anchor, fontSize);
        note.Refresh(game);
        note.Show();
        note.Place();
        return note;
    }

    /// <summary>지금 제독·부하 값으로 판을 다시 짓는다(술집에서 부하를 들이고 돌아왔을 때 따위).</summary>
    public void Refresh(Engine.Game game)
    {
        var people = PersonTable.Open();
        var content = new StackPanel();
        if (GameSettings.ShowContractHintOverlay && game.Player.Contract is { } contract)
        {
            content.Children.Add(Text("현재 계약 힌트"));
            content.Children.Add(Text(game.HintName(contract.Hint)));
        }

        var columns = new StackPanel { Orientation = Orientation.Horizontal };
        columns.Children.Add(Column("기능", SkillBoardDialog.Build(game.Player, people, language: false)));
        columns.Children.Add(Column("언어", SkillBoardDialog.Build(game.Player, people, language: true)));
        content.Children.Add(columns);

        _frame.Child = content;
    }

    /// <summary>한 갈래(기능·언어) — 이름 · 제일 높은 값을 안에 적은 막대.</summary>
    private UIElement Column(string title, List<SkillDisplayItem> items)
    {
        var grid = new Grid { Margin = new Thickness(0, 0, _fontSize * 1.2, 0) };
        for (int c = 0; c < 2; c++) grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var head = Text(title);
        head.Margin = new Thickness(0, 0, 0, _fontSize * 0.2);
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        Grid.SetColumnSpan(head, 2);
        grid.Children.Add(head);

        for (int i = 0; i < items.Count; i++)
        {
            var item = items[i];
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            int row = i + 1;

            var name = Text(item.Name);
            name.Margin = new Thickness(0, 0, _fontSize * 0.6, 0);
            // 막대 — 3 이 가득이다. 빈 자리는 옅은 반투명이라 바탕이 비친다. 값은 막대 한가운데에 적는다.
            double full = BarWidth, filled = full * Math.Clamp(item.BestLevel / 3.0, 0, 1);
            var bar = new Grid
            {
                Width = full,
                Height = _fontSize * 1.05,
                Margin = new Thickness(0, 1, 0, 1),
                VerticalAlignment = VerticalAlignment.Center,
            };
            bar.Children.Add(new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF)),
                BorderBrush = Brushes.Black,
                BorderThickness = new Thickness(1),
            });
            if (filled > 0)
                bar.Children.Add(new Border
                {
                    Width = filled,
                    HorizontalAlignment = HorizontalAlignment.Left,
                    Background = BarFill,
                    BorderBrush = Brushes.Black,
                    BorderThickness = new Thickness(1),
                });
            var level = Text(item.BestLevel.ToString());
            level.FontSize = _fontSize * 0.8;
            level.HorizontalAlignment = HorizontalAlignment.Center;
            bar.Children.Add(level);

            Grid.SetRow(name, row);
            Grid.SetRow(bar, row);
            Grid.SetColumn(bar, 1);
            grid.Children.Add(name);
            grid.Children.Add(bar);
        }
        return grid;
    }

    /// <summary>함대 쪽지(<see cref="FleetLabelWindow"/>)와 같은 글씨 — 흰 굵은 글씨에 까만 그림자.</summary>
    private TextBlock Text(string text) => new()
    {
        Text = text,
        FontSize = _fontSize,
        FontWeight = FontWeights.Bold,
        Foreground = Brushes.White,
        VerticalAlignment = VerticalAlignment.Center,
        Effect = new DropShadowEffect
        {
            Color = Colors.Black,
            ShadowDepth = 1.5,
            BlurRadius = 3,
            Opacity = 1,
        },
    };

    private static SolidColorBrush Brush(string hex) => new((Color)ColorConverter.ConvertFromString(hex));

    /// <summary>사건이 도는 동안 감춘다 — 함대 쪽지와 같다.</summary>
    public void Shade(bool on) => Visibility = on ? Visibility.Collapsed : Visibility.Visible;

    /// <summary>
    /// 적어 둔 거리대로 자리를 잡는다. 아직 옮긴 적이 없으면 <b>그림 왼쪽</b>에 붙이고,
    /// 거기가 화면 밖이면 오른쪽으로 돌려 붙인다.
    /// </summary>
    private void Place()
    {
        if (double.IsNaN(_anchor.Left) || double.IsNaN(_anchor.Top)) return;

        double width = ActualWidth > 0 ? ActualWidth : 0;
        if (double.IsNaN(_dx))
        {
            if (width <= 0) return;   // 크기를 알고 나서 자리를 정한다(SizeChanged 가 다시 부른다)
            _dx = -(width + Gap);
            _dy = 0;
            if (_anchor.Left + _dx < SystemParameters.VirtualScreenLeft) _dx = _anchor.ActualWidth + Gap;
        }

        Left = _anchor.Left + _dx;
        Top = _anchor.Top + _dy;
    }
}
