using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using CdsHelper.Game.Local.Helpers;
using CdsHelper.Support.Local.Models;

namespace CdsHelper.Game.UI.Views;

/// <summary>
/// 지도 오른쪽 아래에 붙는 <b>미니맵</b> — 발견물 지도(<see cref="DiscoveryMapDialog"/>)를 작게 잘라 배를 가운데 두고 따라간다.
/// </summary>
/// <remarks>
/// 놀이에는 없는 것이라 개발 창의 「미니맵」으로 켠다. 바탕은 발견물 지도와 같은 온 세계 양피지 지도이고(점 하나가 칸 4x4),
/// 빨강은 찾은 발견물 · 회색은 아직 · 파랑은 내 자리다. 도시 안에서는 안 뜬다(부르는 쪽이 가린다).
/// </remarks>
internal sealed class MiniMapView : Border
{
    /// <summary>보이는 창 크기(화면 점).</summary>
    public const double ViewW = 260, ViewH = 160;

    /// <summary>지도 점 하나를 몇 배로 키워 보일지.</summary>
    private const double Zoom = 2;

    private static readonly Brush Found = Frozen(Color.FromRgb(0xC0, 0x30, 0x20));
    private static readonly Brush Yet = Frozen(Color.FromRgb(0x50, 0x50, 0x50));
    private static readonly Brush Mine = Frozen(Color.FromRgb(0x20, 0x40, 0xC0));

    private static SolidColorBrush Frozen(Color c)
    {
        var b = new SolidColorBrush(c);
        b.Freeze();
        return b;
    }

    private readonly Canvas _world = new();
    private readonly Canvas _marks = new();
    private readonly Ellipse _ship = new() { Width = 5, Height = 5, Fill = Mine, Stroke = Brushes.White, StrokeThickness = 0.8 };
    private readonly TranslateTransform _shift = new();

    /// <summary>점을 마지막으로 찍었을 때의 찾은 발견물 수 — 달라지면 다시 찍는다.</summary>
    private int _markedFound = -1;

    public MiniMapView()
    {
        Width = ViewW;
        Height = ViewH;
        BorderBrush = GameUi.Edge;
        BorderThickness = new Thickness(2);
        Background = Brushes.Black;
        ClipToBounds = true;
        IsHitTestVisible = false;

        var moves = new TransformGroup();
        moves.Children.Add(new ScaleTransform(Zoom, Zoom));
        moves.Children.Add(_shift);
        _world.RenderTransform = moves;
        _world.Children.Add(_marks);
        _world.Children.Add(_ship);
        Child = new Canvas { Children = { _world } };
    }

    /// <summary>바탕 지도가 섰는지. 안 섰으면 <see cref="SetChart"/> 부터.</summary>
    public bool HasChart { get; private set; }

    /// <summary>바탕 지도를 깐다 — 한 번이면 된다.</summary>
    public void SetChart(uint[] chart, int width, int height)
    {
        var bmp = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, chart, width * 4);
        bmp.Freeze();
        var image = new Image { Source = bmp, Width = width, Height = height };
        RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.NearestNeighbor);
        _world.Width = width;
        _world.Height = height;
        _world.Children.Insert(0, image);
        HasChart = true;
    }

    /// <summary>배 자리(칸)로 가운데를 옮기고, 찾은 발견물이 늘었으면 점을 다시 찍는다.</summary>
    public void Update(DiscoveryTable? table, Player player, double cellX, double cellY)
    {
        if (table != null && player.Discoveries.Count != _markedFound)
        {
            _marks.Children.Clear();
            foreach (var row in table.Discoveries)
            {
                if (!row.HasPlace) continue;
                bool found = player.HasFound(row.Id);
                double x = (row.X1 + row.X2) / 2.0 / ExploredMap.CellsPerBlock;
                double y = (row.Y1 + row.Y2) / 2.0 / ExploredMap.CellsPerBlock;
                var dot = new Ellipse { Width = 2.5, Height = 2.5, Fill = found ? Found : Yet };
                Canvas.SetLeft(dot, x - 1.25);
                Canvas.SetTop(dot, y - 1.25);
                _marks.Children.Add(dot);
            }
            _markedFound = player.Discoveries.Count;
        }

        double px = cellX / ExploredMap.CellsPerBlock, py = cellY / ExploredMap.CellsPerBlock;
        Canvas.SetLeft(_ship, px - _ship.Width / 2);
        Canvas.SetTop(_ship, py - _ship.Height / 2);
        _shift.X = ViewW / 2 - px * Zoom;
        _shift.Y = ViewH / 2 - py * Zoom;
    }
}
