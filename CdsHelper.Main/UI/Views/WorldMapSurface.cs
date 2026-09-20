using System.Numerics;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CdsHelper.Support.Local.Helpers;
using CdsHelper.Support.Local.Models;

namespace CdsHelper.Main.UI.Views;

/// <summary>
/// 세계지도 바탕 그림. 보이는 영역만 그때그때 화면 해상도로 그린다.
/// </summary>
/// <remarks>
/// 예전에는 15000x2500 비트맵(150MB)을 한 번 구워 두고 <see cref="ScaleTransform"/> 으로
/// 늘였다. 그러면 확대할수록 구울 때 이미 버린 그림이 그대로 늘어나 뭉개진다 —
/// 칸 하나를 2픽셀로 구웠으니 OCEAN.CDS 타일 16x16 중 실제로 남은 것은 2x2 평균색뿐이었다.
///
/// 여기서는 cds95-mod 의 WorldMapKR(world.c <c>World_RenderView</c>)처럼 창에 보이는
/// 만큼만 그린다. 화면 점 하나가 타일의 어느 점인지를 그때 계산하므로, 확대하면
/// 칸당 최대 16x16 의 원본 타일 그림이 그대로 드러난다. 그리는 비용은 배율과 상관없이
/// "보이는 화면 픽셀 수"로 일정하다.
///
/// 바깥 좌표계(<see cref="LogicalW"/> x <see cref="LogicalH"/>)는 예전 비트맵과 똑같이 두었다.
/// 도시/발견물 마커, 경로, 위경도 변환이 전부 이 좌표를 쓰기 때문이다.
/// </remarks>
public sealed class WorldMapSurface : FrameworkElement
{
    /// <summary>논리 좌표에서 칸 하나가 차지하는 픽셀 수.</summary>
    public const int LogicalScale = 2;

    /// <summary>가로 무한 스크롤용 복제 수.</summary>
    public const int TileCopies = 3;

    /// <summary>복제 한 벌의 논리 폭(5000).</summary>
    public const int TileLogicalW = WorldMapRenderer.UnfoldedW * LogicalScale;

    /// <summary>논리 좌표 전체 크기(15000 x 2500).</summary>
    public const int LogicalW = TileLogicalW * TileCopies;
    public const int LogicalH = WorldMapRenderer.CellH * LogicalScale;

    /// <summary>논리 1픽셀이 덮는 OCEAN.CDS 타일 픽셀 수(16 / 2 = 8).</summary>
    private const int TilePxPerLogical = OceanTiles.TileW / LogicalScale;

    /// <summary>한 번에 그릴 비트맵의 변 길이 상한. 창을 아무리 키워도 여기서 멈춘다.</summary>
    private const int MaxSurfacePx = 8192;

    private byte[]? _world;                 // WORLD.CDS 원본 6.25MB
    private OceanTiles? _ocean;             // 없으면 _cellLut 으로 그린다
    private int[]? _cellLut;                // terrain * 256 + attr -> 0xRRGGBB

    private Rect _view;                     // 논리 좌표로 자른 가시 영역
    private double _pixelsPerLogical = 1;   // 논리 1px 당 화면 실픽셀 (줌 x DPI)

    // 다시 그릴 때마다 새로 잡지 않으려고 들고 있는 버퍼들
    private WriteableBitmap? _bmp;
    private int[] _pixels = [];
    private int[] _colCell = [];            // 열마다 raw 파일 안의 칸 오프셋
    private int[] _colSub = [];             // 열마다 타일 안 x (지금 고른 잘게보기 단위로)
    private bool[] _colRightHalf = [];

    private int _detail = OceanTiles.TileW;  // 타일 한 변을 몇 등분해서 볼지 (1/2/4/16)
    private int _detailShift;                // ftx >> _detailShift & (_detail-1) = 타일 안 자리

    public WorldMapSurface()
    {
        // 화면 픽셀과 1:1로 그리므로 WPF가 다시 표본화하지 않게 한다.
        RenderOptions.SetBitmapScalingMode(this, BitmapScalingMode.NearestNeighbor);
        RenderOptions.SetEdgeMode(this, EdgeMode.Aliased);
    }

    /// <summary>그릴 것이 있는지.</summary>
    public bool HasMap => _world != null;

    /// <summary>
    /// 그릴 자료를 갈아 끼운다. ocean 이 null 이면 palette 로 칸 색을 계산해 그린다.
    /// </summary>
    public void SetSource(byte[]? world, OceanTiles? ocean, MapPalette? palette,
                          bool showCoast = true, bool showWind = false)
    {
        _world = world;
        _ocean = ocean;
        // 타일 그림이 있으면 팔레트 표는 만들 필요가 없다.
        _cellLut = ocean != null ? null : WorldMapRenderer.BuildCellColorLut(palette, showCoast, showWind);
        InvalidateVisual();
    }

    /// <summary>
    /// 지금 창에 보이는 영역을 알려 준다. <paramref name="view"/> 는 논리 좌표,
    /// <paramref name="pixelsPerLogical"/> 는 논리 1픽셀이 화면 실픽셀 몇 개인지(줌 x DPI).
    /// </summary>
    public void SetView(Rect view, double pixelsPerLogical)
    {
        // 지도 밖은 그릴 것이 없다.
        double x0 = Math.Max(0, view.X);
        double y0 = Math.Max(0, view.Y);
        double x1 = Math.Min(LogicalW, view.Right);
        double y1 = Math.Min(LogicalH, view.Bottom);
        var clipped = x1 > x0 && y1 > y0 ? new Rect(x0, y0, x1 - x0, y1 - y0) : Rect.Empty;

        if (clipped == _view && Math.Abs(pixelsPerLogical - _pixelsPerLogical) < 1e-9) return;
        _view = clipped;
        _pixelsPerLogical = pixelsPerLogical > 0 ? pixelsPerLogical : 1;
        InvalidateVisual();
    }

    // 스크롤/줌이 이 크기를 기준으로 도므로 늘 전체 논리 크기를 알린다.
    protected override Size MeasureOverride(Size availableSize) => new(LogicalW, LogicalH);
    protected override Size ArrangeOverride(Size finalSize) => new(LogicalW, LogicalH);

    protected override void OnRender(DrawingContext dc)
    {
        // Image 가 Source 를 가졌을 때와 같게 — 이게 없으면 마우스 이벤트를 못 받는다.
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, LogicalW, LogicalH));

        if (_world == null || _view.IsEmpty) return;

        double s = _pixelsPerLogical;
        int dw = Math.Clamp((int)Math.Ceiling(_view.Width * s), 1, MaxSurfacePx);
        int dh = Math.Clamp((int)Math.Ceiling(_view.Height * s), 1, MaxSurfacePx);

        EnsureBuffers(dw, dh);
        ChooseDetail(dw);
        BuildColumns(dw);
        FillPixels(dw, dh);

        _bmp!.WritePixels(new Int32Rect(0, 0, dw, dh), _pixels, dw * 4, 0);
        // 비트맵 한 점이 화면 한 점이 되도록 그릴 사각형을 비트맵 크기에서 되짚어 잡는다.
        dc.DrawImage(_bmp, new Rect(_view.X, _view.Y, dw / s, dh / s));
    }

    private void EnsureBuffers(int dw, int dh)
    {
        if (_bmp == null || _bmp.PixelWidth != dw || _bmp.PixelHeight != dh)
        {
            _bmp = new WriteableBitmap(dw, dh, 96, 96, PixelFormats.Bgr32, null);
            _pixels = new int[dw * dh];
        }
        if (_colCell.Length < dw)
        {
            _colCell = new int[dw];
            _colSub = new int[dw];
            _colRightHalf = new bool[dw];
        }
    }

    /// <summary>
    /// 지금 배율에서 타일을 얼마나 잘게 볼지 고른다.
    /// </summary>
    /// <remarks>
    /// 축소해서 타일 하나가 화면 한 점보다 작아지면, 16x16 중 한 점만 뽑는 방식은
    /// 바다의 디더링 무늬와 화면 격자가 어긋나 물결(모아레)이 인다. 그럴 때는 미리 뽑아 둔
    /// 평균색을 쓴다 — 타일 하나가 화면에서 차지하는 점 수에 맞춰 1x1 / 2x2 / 4x4 중에 고른다.
    /// 그보다 크게 보일 때는 원본 16x16 을 그대로 뽑는다(평균표를 만들 이유가 없다).
    /// </remarks>
    private void ChooseDetail(int dw)
    {
        // 타일 하나가 화면에서 몇 점을 차지하는지.
        double stepX = _view.Width * TilePxPerLogical / dw;
        double samplesPerTile = stepX > 0 ? OceanTiles.TileW / stepX : OceanTiles.TileW;

        _detail = samplesPerTile switch
        {
            <= 1.0 => 1,
            <= 2.0 => 2,
            <= 4.0 => 4,
            _ => OceanTiles.TileW,      // 원본 그대로
        };
        _detailShift = BitOperations.Log2((uint)(OceanTiles.TileW / _detail));
    }

    /// <summary>
    /// 열마다 "raw 파일 안의 칸 위치"와 "타일 안 x"를 미리 뽑아 둔다.
    /// 줄마다 다시 계산하면 나눗셈이 폭 x 높이 번 돌기 때문이다.
    /// </summary>
    private void BuildColumns(int dw)
    {
        double tx0 = _view.X * TilePxPerLogical;
        double stepX = _view.Width * TilePxPerLogical / dw;
        const int worldTileW = WorldMapRenderer.UnfoldedW * OceanTiles.TileW;   // 40000

        for (int i = 0; i < dw; i++)
        {
            long ftx = (long)(tx0 + i * stepX);
            if (ftx < 0) ftx = 0;
            // 복제본 세 벌이 같은 지도이므로 한 벌 폭으로 접는다.
            ftx %= worldTileW;

            int ux = (int)(ftx >> 4);               // 펼친 칸 x (0~2499)
            _colSub[i] = (int)(ftx >> _detailShift) & (_detail - 1);   // 타일 안 x
            // 짝수 행이 지도의 왼쪽 절반, 홀수 행이 오른쪽 절반이다.
            bool right = ux >= WorldMapRenderer.CellW;
            _colRightHalf[i] = right;
            _colCell[i] = (right ? ux - WorldMapRenderer.CellW : ux) * 2;
        }
    }

    private void FillPixels(int dw, int dh)
    {
        double ty0 = _view.Y * TilePxPerLogical;
        double stepY = _view.Height * TilePxPerLogical / dh;

        var world = _world!;
        var lut = _cellLut;
        int detail = _detail;
        // 원본 그대로 볼 때는 타일 바이트 + 팔레트를, 줄여 볼 때는 평균색 표를 쓴다.
        bool raw = detail == OceanTiles.TileW;
        var tiles = raw ? _ocean?.TileData : null;
        var rgb = raw ? _ocean?.PaletteRgb : null;
        var avg = raw ? null : _ocean?.GetAverages(detail);

        for (int j = 0; j < dh; j++)
        {
            long fty = (long)(ty0 + j * stepY);
            if (fty < 0) fty = 0;
            int uy = (int)(fty >> 4);
            if (uy >= WorldMapRenderer.CellH) uy = WorldMapRenderer.CellH - 1;
            int sy = (int)(fty >> _detailShift) & (detail - 1);      // 타일 안 y

            int evenRow = uy * 2 * WorldMapRenderer.RawStride;
            int oddRow = evenRow + WorldMapRenderer.RawStride;
            int rowInTile = sy * detail;
            int dst = j * dw;

            if (tiles != null)
            {
                for (int i = 0; i < dw; i++)
                {
                    int off = (_colRightHalf[i] ? oddRow : evenRow) + _colCell[i];
                    int tile = (world[off] | (world[off + 1] << 8)) & OceanTiles.TileMask;
                    _pixels[dst + i] = rgb![tiles[tile * OceanTiles.TilePixels + rowInTile + _colSub[i]]];
                }
            }
            else if (avg != null)
            {
                int per = detail * detail;
                for (int i = 0; i < dw; i++)
                {
                    int off = (_colRightHalf[i] ? oddRow : evenRow) + _colCell[i];
                    int tile = (world[off] | (world[off + 1] << 8)) & OceanTiles.TileMask;
                    _pixels[dst + i] = avg[tile * per + rowInTile + _colSub[i]];
                }
            }
            else
            {
                // OCEAN.CDS 가 없을 때 — 칸 하나가 한 색이라 확대해도 더 나올 것이 없다.
                for (int i = 0; i < dw; i++)
                {
                    int off = (_colRightHalf[i] ? oddRow : evenRow) + _colCell[i];
                    _pixels[dst + i] = lut![(world[off] & 0x7F) * 256 + world[off + 1]];
                }
            }
        }
    }
}
