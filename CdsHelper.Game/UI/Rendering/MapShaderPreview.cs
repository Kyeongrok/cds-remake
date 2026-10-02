using CdsHelper.Game.Local.Helpers;
using CdsHelper.Support.Local.Helpers;

namespace CdsHelper.Game.UI.Views;

/// <summary>
/// 놀이 지도의 셰이더를 <b>화면 밖에서</b> 돌려 그림 한 장으로 내준다 — WORLD.CDS 편집기가 고친 지도를
/// 놀이에서 보일 모습 그대로(고해상도 바다 · 도트 확대 필터 · 뭍 세부 질감 · 바다 입체 효과) 미리 보는 데 쓴다.
/// </summary>
/// <remarks>
/// 놀이와 <b>같은 렌더러</b>(<see cref="MapD3DRenderer"/>)에 같은 표(<see cref="MapShaderData"/>)를 건다 — 따로 흉내 낸
/// 그림이 아니라서, 여기서 다듬은 해안선은 놀이에서도 그 모양이다. 물결은 멈춰 있다(시각 0).
///
/// 배·구름·남의 배·도시 분리 그림은 걸지 않는다. 도시 칸은 넘겨받은 지도에 적힌 타일 그대로 나온다.
/// </remarks>
public sealed class MapShaderPreview : IDisposable
{
    private readonly MapD3DRenderer _renderer = new();
    private readonly TerrainTable _terrain;
    private readonly OceanTiles _ocean;

    private MapShaderPreview(TerrainTable terrain, OceanTiles ocean)
    {
        _terrain = terrain;
        _ocean = ocean;
    }

    /// <summary>왜 못 열었는지. 잘 열렸으면 빈 문자열.</summary>
    public static string LastError { get; private set; } = "";

    /// <summary>고해상도 바다.</summary>
    public bool HiResSea { get => _renderer.HiResSea; set => _renderer.HiResSea = value; }

    /// <summary>도트 확대 필터.</summary>
    public bool PixelFilter { get => _renderer.PixelFilter; set => _renderer.PixelFilter = value; }

    /// <summary>뭍 세부 질감.</summary>
    public bool LandDetail { get => _renderer.LandDetail; set => _renderer.LandDetail = value; }

    /// <summary>바다 입체 효과와 그 밝기.</summary>
    public bool SeaEffect { get => _renderer.SeaEffect; set => _renderer.SeaEffect = value; }

    public double SeaBrightness { get => _renderer.SeaBrightness; set => _renderer.SeaBrightness = (float)value; }

    /// <summary>
    /// 장치를 올리고 지도를 건다. 지형표를 못 읽거나 그래픽 장치를 못 만들면 null 이고 까닭은 <see cref="LastError"/> 다.
    /// </summary>
    public static MapShaderPreview? Open(string gameDirectory, byte[] world, OceanTiles ocean)
    {
        LastError = "";
        if (TerrainTable.Open(gameDirectory) is not { } terrain)
        {
            LastError = $"지형표를 읽지 못했습니다 ({TerrainTable.LastError})";
            return null;
        }
        var preview = new MapShaderPreview(terrain, ocean);
        try
        {
            preview._renderer.Initialize(world, ocean);
            preview._renderer.SetWaterPalette(MapShaderData.WaterPalette(world, terrain, ocean));
            preview.Update(world, depth: true);
            return preview;
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            preview.Dispose();
            return null;
        }
    }

    /// <summary>
    /// 고친 지도를 다시 건다. <paramref name="depth"/> 가 참이면 수심 표와 타일 부류 표도 다시 짓는다 —
    /// 지도 전체를 훑는 셈이라 붓을 끄는 동안에는 거짓으로 두고, 획이 끝났을 때 참으로 부른다.
    /// </summary>
    public void Update(byte[] world, bool depth)
    {
        _renderer.UpdateWorld(world);
        if (!depth) return;
        _renderer.SetSeaDepth(MapShaderData.SeaDepth(world, _terrain));
        _renderer.SetTileKinds(MapShaderData.TileKinds(world, _terrain));
    }

    /// <summary>
    /// 지도의 한 자리를 그려 BGRA 로 돌려준다(가로 x 세로 x 4바이트). 못 그리면 null.
    /// </summary>
    /// <param name="originX">그림 왼쪽 위가 가리키는 칸 좌표.</param>
    /// <param name="cellsPerPixel">그림 한 점이 나아가는 칸 수. 고해상도 바다는 칸이 네 점보다 클 때(0.25 미만)만 든다.</param>
    public byte[]? Render(double originX, double originY, double cellsPerPixel, int width, int height)
    {
        if (width <= 0 || height <= 0) return null;
        _renderer.SeaTime = 0;
        _renderer.EnsureTarget(width, height);
        _renderer.Render((originX, originY), (cellsPerPixel, cellsPerPixel), (0, 0, 0, 0));
        return _renderer.ReadBack();
    }

    public void Dispose() => _renderer.Dispose();
}
