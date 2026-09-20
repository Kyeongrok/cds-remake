using System.Runtime.InteropServices;
using CdsHelper.Game.Local.Helpers;
using CdsHelper.Support.Local.Helpers;
using Vortice.D3DCompiler;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Vortice.Mathematics;

namespace CdsHelper.Game.UI.Views;

/// <summary>
/// 세계지도를 Direct3D 11 로 그린다. 칸 하나가 화면 몇 픽셀이든 픽셀 셰이더가 그때
/// OCEAN.CDS 타일에서 점을 뽑으므로 확대해도 원본 그림이 그대로 나온다.
/// </summary>
/// <remarks>
/// CPU 로 그리던 <c>WorldMapSurface</c>(CdsHelper.Main) 와 그림은 같다. 다른 것은 배를 60fps 로
/// 움직일 때다 — 카메라가 배를 따라가면 프레임마다 화면을 다시 그려야 하는데, CPU 는
/// 그때마다 뷰포트 전체를 훑어야 하고 GPU 는 텍스처를 한 번 올려 두고 표본만 뽑으면 된다.
///
/// <para>텍스처 세 장으로 끝난다</para>
/// <list type="bullet">
///   <item>칸 지도 — 2500x1250 R16_UINT. WORLD.CDS 를 펼쳐 칸마다 타일 번호(하위 14비트)만 담는다.</item>
///   <item>타일 그림 — 2048x2048 R8_UINT. 16x16 타일 16,384장을 128x128 격자로 편 것.</item>
///   <item>팔레트 — 256x1 BGRA.</item>
/// </list>
/// 픽셀 셰이더는 화면 점 -> 칸 -> 타일 번호 -> 타일 안 점 -> 팔레트 순으로 짚는다.
/// 가로로 잇는 것(경도 -180/180 넘나들기)은 칸 좌표를 2500 으로 나눈 나머지로 처리한다.
/// </remarks>
public sealed unsafe class MapD3DRenderer : IDisposable
{
    /// <summary>타일 아틀라스 한 변에 들어가는 타일 수(128 x 128 = 16,384).</summary>
    private const int AtlasTiles = 128;
    private const int AtlasSize = AtlasTiles * OceanTiles.TileW;   // 2048

    // 셰이더 본문은 ASCII 로만 적는다. 한글 주석을 넣으면 컴파일이 깨진다 —
    // D3DCompile 이 원본을 cp949 로 받는데, 한글 음절의 끝바이트가 0x5C('\') 인 것이 많아
    // 줄 끝에 오면 줄이음으로 먹혀 다음 줄이 통째로 사라진다. 설명은 여기 바깥에 둔다.
    //
    //   VS  정점 버퍼 없이 삼각형 하나로 화면을 덮는다.
    //   PS  화면 점 -> 칸 좌표 -> 타일 번호 -> 타일 안 점 -> 팔레트 순으로 짚는다.
    //       cell.x 를 2500 으로 나눈 나머지로 접어 경도 -180/180 을 잇는다.
    //       배 그림(SpriteRect)이 있으면 그 자리는 지도 대신 배를 낸다. 색인 0 은 비침이다.
    //       덧그림(OverlayRect)은 배보다 먼저 보므로 배 위에 얹힌다 — 닻이 이것이다.
    //       남의 배(Folk)는 지도 위·내 배 아래다. 구름과 같은 결로 상수 배열에 자리를 싣는다.
    private const string ShaderSource = """
        Texture2D<uint>   CellMap  : register(t0);
        Texture2D<uint>   Atlas    : register(t1);
        Texture2D<float4> Palette  : register(t2);
        Texture2D<float4> Sprite   : register(t3);
        Texture2D<float4> Avg1     : register(t4);
        Texture2D<float4> Avg2     : register(t5);
        Texture2D<float4> Avg4     : register(t6);
        Texture2D<float4> Overlay  : register(t7);
        Texture2D<uint>   NextTile : register(t8);
        Texture2D<uint>   FlowGrid : register(t9);
        Texture2D<float4> ArrowTex : register(t10);
        Texture2D<float4> CloudTex : register(t11);
        Texture2D<float4> FolkTex  : register(t12);

        cbuffer Frame : register(b0)
        {
            float2 OriginCell;
            float2 CellPerPixel;
            float4 SpriteRect;
            float2 MapCells;
            float  Detail;
            float  Pad;
            float4 OverlayRect;
            float4 Cover;
            float4 Ripple;
            float4 Arrows;
            float4 Clouds[6];
            float4 Folk[16];
            float4 Route[32];
        };

        struct VSOut { float4 pos : SV_Position; };

        float4 Tint(float4 c)
        {
            return float4(lerp(c.rgb, Cover.rgb, Cover.a), 1);
        }

        VSOut VS(uint id : SV_VertexID)
        {
            VSOut o;
            float2 uv = float2((id << 1) & 2, id & 2);
            o.pos = float4(uv * float2(2, -2) + float2(-1, 1), 0, 1);
            return o;
        }

        float4 Glyph(float2 d, float h, uint word, float3 tint)
        {
            uint speed = (word >> 4) & 0xFu;
            if (speed == 0) return float4(0, 0, 0, 0);
            if (any(abs(d) >= h)) return float4(0, 0, 0, 0);
            uint dir = word & 0xFu;
            float2 t = d / h * 0.5 + 0.5;
            int2 tx = int2(int(dir) * 32 + int(t.x * 32.0), int(t.y * 32.0));
            float a = ArrowTex.Load(int3(tx, 0)).r;
            if (a <= 0.02) return float4(0, 0, 0, 0);
            float strong = step(0.6, a);
            float3 col = lerp(float3(0.04, 0.04, 0.07), tint, strong);
            float lo = 0.45 + 0.55 * float(speed) / 7.0;
            return float4(col, lerp(0.70, 0.90, strong) * lo);
        }

        float4 ArrowsAt(float2 cellRaw, float2 px)
        {
            float grid = Arrows.y;
            float2 g   = floor(cellRaw / grid);
            int gy = int(g.y);
            if (gy < 0 || gy >= int(Arrows.w)) return float4(0, 0, 0, 0);
            int gx = int(g.x - floor(g.x / Arrows.z) * Arrows.z);

            float cellPx = grid / CellPerPixel.x;
            float h = clamp(cellPx * 0.22, 4.0, 14.0);
            float2 center = ((g + 0.5) * grid - OriginCell) / CellPerPixel;

            uint word = FlowGrid.Load(int3(gx, gy, 0));
            float4 a = Glyph(px - center + float2(0, h), h, word & 0xFFFFu,
                             float3(1.00, 0.94, 0.72));
            if (a.a > 0) return a;
            return Glyph(px - center - float2(0, h), h, word >> 16,
                         float3(0.42, 0.90, 1.00));
        }

        float4 CloudsOver(float4 col, float2 px)
        {
            [unroll] for (int k = 0; k < 6; k++)
            {
                if (Clouds[k].w <= 0) continue;
                float2 d = (px - Clouds[k].xy) / Clouds[k].w;
                if (any(d < 0) || d.x >= 160.0 || d.y >= 120.0) continue;
                float4 c = CloudTex.Load(int3(int2(d) + int2(0, int(Clouds[k].z) * 120), 0));
                if (c.a > 0) col = c;
            }
            return col;
        }

        float4 FolkOver(float4 col, float2 px)
        {
            [unroll] for (int k = 0; k < 16; k++)
            {
                if (Folk[k].w <= 0) continue;
                float2 d = (px - Folk[k].xy) / Folk[k].w;
                if (any(d < 0) || any(d >= 48.0)) continue;
                float4 c = FolkTex.Load(int3(int2(d) + int2(0, int(Folk[k].z) * 48), 0));
                if (c.a > 0) col = c;
            }
            return col;
        }

        float2 SegClosest(float2 p, float2 a, float2 b)
        {
            float2 ab = b - a;
            float t = saturate(dot(p - a, ab) / max(dot(ab, ab), 1e-5));
            return a + ab * t;
        }

        float3 RouteOver(float3 col, float2 px)
        {
            float best = 1e6;
            [loop] for (int r = 0; r < 31; r++)
            {
                if (Route[r].w <= 0 || Route[r + 1].w <= 0) continue;
                float d = distance(px, SegClosest(px, Route[r].xy, Route[r + 1].xy));
                best = min(best, d);
            }
            const float core = 1.3, soft = 2.6;
            float a = 1.0 - saturate((best - core) / (soft - core));
            return lerp(col, float3(1.00, 0.55, 0.10), a);
        }

        float4 PS(VSOut i) : SV_Target
        {
            if (OverlayRect.z > 0)
            {
                float2 v = (i.pos.xy - OverlayRect.xy) / OverlayRect.zw;
                if (all(v >= 0) && all(v < 1))
                {
                    float4 c = Overlay.Load(int3(int2(v * 48.0), 0));
                    if (c.a > 0) return Tint(CloudsOver(c, i.pos.xy));
                }
            }

            if (SpriteRect.z > 0)
            {
                float2 s = (i.pos.xy - SpriteRect.xy) / SpriteRect.zw;
                if (all(s >= 0) && all(s < 1))
                {
                    float4 c = Sprite.Load(int3(int2(s * 48.0), 0));
                    if (c.a > 0) return Tint(CloudsOver(c, i.pos.xy));
                }
            }

            float2 cellRaw = OriginCell + i.pos.xy * CellPerPixel;
            float2 cell = cellRaw;
            cell.x = cell.x - floor(cell.x / MapCells.x) * MapCells.x;
            cell.y = clamp(cell.y, 0, MapCells.y - 0.001);

            int2 c    = int2(cell);
            uint tile = CellMap.Load(int3(c, 0));

            if (Ripple.z > 0)
            {
                float p = (Ripple.x * float(c.x) + Ripple.y * float(c.y)
                           - Ripple.z * Ripple.w * 16.0) / 64.0;
                if ((int(floor(p)) & 15) < 8)
                    tile = NextTile.Load(int3(int2(tile % 128u, tile / 128u), 0));
            }

            int2 org  = int2(tile % 128u, tile / 128u);
            float2 f  = frac(cell);

            float4 col;
            if (Detail < 0.5)      col = Avg1.Load(int3(org, 0));
            else if (Detail < 1.5) col = Avg2.Load(int3(org * 2 + int2(f * 2.0), 0));
            else if (Detail < 2.5) col = Avg4.Load(int3(org * 4 + int2(f * 4.0), 0));
            else
            {
                uint pal = Atlas.Load(int3(org * 16 + int2(f * 16.0), 0));
                col = Palette.Load(int3(int(pal), 0, 0));
            }

            if (Arrows.x > 0.5)
            {
                float4 a = ArrowsAt(cellRaw, i.pos.xy);
                col.rgb = lerp(col.rgb, a.rgb, a.a);
            }
            col.rgb = RouteOver(col.rgb, i.pos.xy);
            return Tint(CloudsOver(FolkOver(col, i.pos.xy), i.pos.xy));
        }
        """;

    [StructLayout(LayoutKind.Sequential)]
    private struct FrameCb
    {
        public float OriginCellX, OriginCellY;
        public float CellPerPixelX, CellPerPixelY;
        public float SpriteX, SpriteY, SpriteW, SpriteH;
        public float MapCellsX, MapCellsY;
        public float Detail;
        public float Pad0;
        public float OverlayX, OverlayY, OverlayW, OverlayH;
        public float CoverR, CoverG, CoverB, CoverA;
        public float RippleDirX, RippleDirY, RippleSpeed, RippleTick;
        public float ArrowOn, ArrowGrid, ArrowCols, ArrowRows;
        public fixed float Clouds[MaxClouds * 4];   // x, y, 그림번호, 보일지
        public fixed float Folk[MaxFolk * 4];       // x, y, 뱃머리(0~3), 배수
        public fixed float Route[MaxRoutePoints * 4]; // x, y, (안 씀), 켜졌는지
    }

    /// <summary>
    /// 지도에 함께 낼 남의 배 수. <b>게임과 같이 열여섯</b>이다(<c>0x004267A6</c> 의
    /// <c>cmp … 0x10</c>).
    /// </summary>
    public const int MaxFolk = 16;

    /// <summary>지도에 그릴 수 있는 자동항해 항로 마디 수. 셰이더의 배열 크기와 같다.</summary>
    public const int MaxRoutePoints = 32;

    /// <summary>
    /// 항로 마디 하나. <paramref name="X"/>·<paramref name="Y"/> 는 화면 자리(실픽셀).
    /// 마지막 마디 다음 칸은 <paramref name="Active"/> 를 거짓으로 두어 선을 끊는다.
    /// </summary>
    public readonly record struct RouteDraw(float X, float Y, bool Active);

    private readonly float[] _route = new float[MaxRoutePoints * 4];

    /// <summary>
    /// 이번 프레임에 그릴 항로. 이웃한 두 마디가 <b>둘 다 켜져 있을 때만</b> 그 사이를 잇는다 —
    /// 자동항해 중이 아니면 <see cref="ReadOnlySpan{T}.Empty"/> 를 준다.
    /// </summary>
    public void SetRoute(ReadOnlySpan<RouteDraw> points)
    {
        Array.Clear(_route);
        for (int i = 0; i < points.Length && i < MaxRoutePoints; i++)
        {
            _route[i * 4 + 0] = points[i].X;
            _route[i * 4 + 1] = points[i].Y;
            _route[i * 4 + 3] = points[i].Active ? 1 : 0;
        }
    }

    /// <summary>
    /// 남의 그림 장수 — 배 넉 장(북·서·남·동)에 말 넉 장을 이어 붙인 여덟 장이다.
    /// </summary>
    /// <remarks>
    /// 게임은 사람 자리의 부류가 2 이상(뭍)이면 배 대신 <b>말</b>을 그린다(<c>0x0048A799</c> 의
    /// <c>cmp eax, 2 / jge</c> → 뭍 그림 벌 <c>0x00569FE8</c>). 곧은 길로 가다 뭍을 만나면 말로,
    /// 다시 바다로 나오면 배로 바뀐다. 셰이더는 <c>Folk[k].z * 48</c> 로 줄을 내리므로 장수를
    /// 모른다 — 여기 값만 맞추면 된다.
    /// </remarks>
    public const int FolkFrames = 8;

    /// <summary>말 그림이 시작하는 장. 배 넉 장 다음이다.</summary>
    public const int FolkLandFrame = 4;

    /// <summary>남의 배 그림 한 변. 내 배와 같은 48이다.</summary>
    public const int FolkSize = 48;

    /// <summary>
    /// 남의 배 한 척이 놓일 자리. <paramref name="X"/>·<paramref name="Y"/> 는 화면 왼쪽
    /// 위(실픽셀), <paramref name="Frame"/> 은 0 북 · 1 서 · 2 남 · 3 동,
    /// <paramref name="Scale"/> 는 48점 한 변을 몇 배로 늘릴지다.
    /// </summary>
    public readonly record struct FolkDraw(float X, float Y, int Frame, float Scale);

    private readonly float[] _folk = new float[MaxFolk * 4];

    /// <summary>
    /// 이번 프레임에 그릴 남의 배들. 그림을 안 올렸으면 아무 일도 하지 않는다.
    /// </summary>
    public void SetFolk(ReadOnlySpan<FolkDraw> folk)
    {
        Array.Clear(_folk);
        if (!_folkReady) return;
        for (int i = 0; i < folk.Length && i < MaxFolk; i++)
        {
            _folk[i * 4 + 0] = folk[i].X;
            _folk[i * 4 + 1] = folk[i].Y;
            _folk[i * 4 + 2] = Math.Clamp(folk[i].Frame, 0, FolkFrames - 1);
            _folk[i * 4 + 3] = folk[i].Scale;       // 0 이면 셰이더가 안 그린다
        }
    }

    /// <summary>
    /// 남의 배 그림 넉 장을 건다 — 48x48 넉 장을 <b>세로로 이은</b> 48x192 다.
    /// </summary>
    public void SetFolkSprites(ReadOnlySpan<uint> bgra)
    {
        if (bgra.Length != FolkSize * FolkSize * FolkFrames) return;

        var old = _folkSrv;
        _folkSrv = CreateImmutable(bgra.ToArray(), FolkSize, FolkSize * FolkFrames,
                                   Format.B8G8R8A8_UNorm, sizeof(uint));
        old?.Dispose();
        _folkReady = true;
    }

    /// <summary>지도 위에 떠 있는 구름 수. 게임과 같다(<c>0x004890DB</c>).</summary>
    public const int MaxClouds = 6;

    /// <summary>
    /// 구름 한 장이 놓일 자리. <paramref name="X"/>·<paramref name="Y"/> 는 화면 왼쪽 위(실픽셀),
    /// <paramref name="Scale"/> 는 원본 160x120 을 몇 배로 늘려 그릴지다.
    /// </summary>
    public readonly record struct CloudDraw(float X, float Y, int Frame, float Scale);

    private readonly float[] _clouds = new float[MaxClouds * 4];

    /// <summary>
    /// 이번 프레임에 그릴 구름들. 준 것보다 적으면 나머지는 안 그린다.
    /// 구름 그림을 안 올렸으면 아무 일도 하지 않는다.
    /// </summary>
    public void SetClouds(ReadOnlySpan<CloudDraw> clouds)
    {
        Array.Clear(_clouds);
        if (!_cloudsReady) return;
        for (int i = 0; i < clouds.Length && i < MaxClouds; i++)
        {
            _clouds[i * 4 + 0] = clouds[i].X;
            _clouds[i * 4 + 1] = clouds[i].Y;
            _clouds[i * 4 + 2] = clouds[i].Frame;
            _clouds[i * 4 + 3] = clouds[i].Scale;   // 0 이면 셰이더가 안 그린다
        }
    }

    /// <summary>
    /// 물결. 해류 방위 벡터와 세기, 그리고 흐른 틱 수다. 세기가 0 이면 물결이 안 인다.
    /// </summary>
    /// <remarks>
    /// 게임이 그리는 칸마다 하는 판정(<c>0x0048A4D8</c>~)을 픽셀 셰이더로 옮긴 것이다.
    /// 원본은 화면 격자의 열·행으로 띠를 세는데 여기서는 <b>지도 칸</b>으로 센다 —
    /// 지도를 밀거나 키워도 물결이 바다에 붙어 있어야 한다. 자세한 것은
    /// <see cref="WindTable.InRippleBand"/>.
    /// </remarks>
    public (float DirX, float DirY, float Speed, float Tick) Ripple { get; set; }

    /// <summary>바람·해류 화살표를 얹을지. 게임에는 없는 것이라 커맨드 창에서 끄고 켠다.</summary>
    public bool ShowArrows { get; set; }

    /// <summary>
    /// 지도 위에 씌우는 막(색과 짙기). 알파가 0 이면 아무것도 안 씌운다 —
    /// 도시에 들어가 있는 동안 게임이 지도를 이렇게 덮는다(색을 칠하는 것이 아니라 비쳐 보인다).
    /// </summary>
    public (float R, float G, float B, float A) Cover { get; set; }

    private ID3D11Device _device = null!;
    private ID3D11DeviceContext _ctx = null!;
    private ID3D11VertexShader _vs = null!;
    private ID3D11PixelShader _ps = null!;
    private ID3D11Buffer _cb = null!;
    private ID3D11ShaderResourceView _cellSrv = null!;
    private ID3D11ShaderResourceView _atlasSrv = null!;
    private ID3D11ShaderResourceView _paletteSrv = null!;
    /// <summary>축소용 평균색 단계. 타일 한 변을 1/2/4 등분한 것.</summary>
    private static readonly int[] AvgLevels = [1, 2, 4];
    private readonly ID3D11ShaderResourceView[] _avgSrv = new ID3D11ShaderResourceView[AvgLevels.Length];
    private ID3D11Texture2D _spriteTex = null!;
    private ID3D11ShaderResourceView _spriteSrv = null!;
    private ID3D11Texture2D _overlayTex = null!;
    private ID3D11ShaderResourceView _overlaySrv = null!;
    private ID3D11ShaderResourceView _nextSrv = null!;
    private ID3D11Texture2D _flowTex = null!;
    private ID3D11ShaderResourceView _flowSrv = null!;
    private ID3D11ShaderResourceView _arrowSrv = null!;
    private ID3D11ShaderResourceView _cloudSrv = null!;
    private bool _cloudsReady;

    private ID3D11ShaderResourceView _folkSrv = null!;
    private bool _folkReady;

    /// <summary>덧그림 한 변. 셰이더에도 같은 값이 박혀 있다(닻이 배와 같은 48x48 이다).</summary>
    private const int OverlaySize = 48;

    private ID3D11Texture2D? _target;
    private ID3D11RenderTargetView? _rtv;
    private int _targetW, _targetH;

    public ID3D11Device Device => _device;

    /// <summary>지금 그리는 대상 텍스처. D3DImage 와 나눠 쓸 수 있도록 공유로 만든다.</summary>
    public ID3D11Texture2D? Target => _target;

    public int TargetWidth => _targetW;
    public int TargetHeight => _targetH;

    /// <summary>
    /// 장치와 텍스처를 올린다. <paramref name="worldData"/> 는 WORLD.CDS 원본,
    /// <paramref name="ocean"/> 은 풀어 둔 OCEAN.CDS 타일.
    /// </summary>
    public void Initialize(byte[] worldData, OceanTiles ocean)
    {
        var flags = DeviceCreationFlags.BgraSupport;
        var levels = new[] { FeatureLevel.Level_11_0, FeatureLevel.Level_10_1, FeatureLevel.Level_10_0 };
        // out 인자의 형을 적어 둬야 한다 — var 로 두면 FeatureLevel 을 내는 오버로드와 헷갈린다.
        D3D11.D3D11CreateDevice(null, DriverType.Hardware, flags, levels,
                                out ID3D11Device dev, out ID3D11DeviceContext ctx).CheckError();
        _device = dev;
        _ctx = ctx;

        var vsBlob = Compiler.Compile(ShaderSource, "VS", "map.hlsl", "vs_4_0");
        var psBlob = Compiler.Compile(ShaderSource, "PS", "map.hlsl", "ps_4_0");
        _vs = _device.CreateVertexShader(vsBlob.Span);
        _ps = _device.CreatePixelShader(psBlob.Span);

        _cb = _device.CreateBuffer((uint)Marshal.SizeOf<FrameCb>(), BindFlags.ConstantBuffer,
                                   ResourceUsage.Dynamic, CpuAccessFlags.Write);

        _world = worldData;
        CreateCellMap(worldData);
        CreateAtlas(ocean);
        CreatePalette(ocean);
        CreateSpriteTexture();
        CreateFlowTextures();
    }

    /// <summary>
    /// 물결·화살표에 쓰는 것들. 표를 못 열어도 셰이더에 걸 것은 있어야 하므로
    /// <b>제자리 표</b>(타일이 안 갈리는 표)와 빈 격자를 먼저 만들어 둔다.
    /// </summary>
    private void CreateFlowTextures()
    {
        var identity = new ushort[OceanTiles.TileCount];
        for (int t = 0; t < identity.Length; t++) identity[t] = (ushort)t;
        _nextSrv = CreateTileMap(identity);

        _flowTex = _device.CreateTexture2D(new Texture2DDescription
        {
            Width = WindTable.Cols,
            Height = WindTable.Rows,
            MipLevels = 1,
            ArraySize = 1,
            Format = Format.R32_UInt,
            SampleDescription = new SampleDescription(1, 0),
            Usage = ResourceUsage.Dynamic,
            BindFlags = BindFlags.ShaderResource,
            CPUAccessFlags = CpuAccessFlags.Write,
        });
        _flowSrv = _device.CreateShaderResourceView(_flowTex);

        _arrowSrv = CreateImmutable(FlowArrows.Alpha, FlowArrows.AtlasWidth, FlowArrows.CellSize,
                                    Format.R8_UNorm, 1);

        // 구름을 못 읽어도 셰이더에 걸 것은 있어야 한다. 안 그릴 것이므로 한 점이면 된다.
        _cloudSrv = CreateImmutable(new uint[1], 1, 1, Format.B8G8R8A8_UNorm, sizeof(uint));
        _folkSrv = CreateImmutable(new uint[1], 1, 1, Format.B8G8R8A8_UNorm, sizeof(uint));
    }

    /// <summary>
    /// 구름 그림 여섯 장을 건다(<see cref="CloudSprites.Bgra"/>). 걸기 전에는
    /// <see cref="SetClouds"/> 가 아무 일도 하지 않는다.
    /// </summary>
    public void SetCloudSprites(ReadOnlySpan<uint> bgra)
    {
        if (bgra.Length != CloudSprites.Width * CloudSprites.AtlasHeight) return;
        var old = _cloudSrv;
        _cloudSrv = CreateImmutable(bgra.ToArray(), CloudSprites.Width, CloudSprites.AtlasHeight,
                                    Format.B8G8R8A8_UNorm, sizeof(uint));
        old?.Dispose();
        _cloudsReady = true;
    }

    /// <summary>타일 번호 16,384개를 아틀라스와 같은 128x128 격자로 편다.</summary>
    private ID3D11ShaderResourceView CreateTileMap(ushort[] tiles) =>
        CreateImmutable(tiles, AtlasTiles, AtlasTiles, Format.R16_UInt, sizeof(ushort));

    /// <summary>
    /// 물결이 한 칸 돌아간 뒤의 타일 번호표를 건다
    /// (<see cref="WindTable.BuildRippleTiles"/> 가 만든 것).
    /// </summary>
    public void SetRippleTiles(ushort[] nextTiles)
    {
        if (nextTiles.Length != OceanTiles.TileCount) return;
        var old = _nextSrv;
        _nextSrv = CreateTileMap(nextTiles);
        old?.Dispose();
    }

    /// <summary>
    /// 화살표가 읽을 50x25 격자. 칸마다 <c>아래 16비트 = 바람, 위 16비트 = 해류</c>이고,
    /// 낱말 모양은 게임 표 그대로다(<c>방위 | 세기&lt;&lt;4 | 기후대&lt;&lt;8</c>).
    /// </summary>
    public void SetFlowGrid(ReadOnlySpan<uint> grid)
    {
        if (grid.Length < WindTable.Count) return;
        var map = _ctx.Map(_flowTex, 0, Vortice.Direct3D11.MapMode.WriteDiscard);
        try
        {
            for (int y = 0; y < WindTable.Rows; y++)
            {
                var dst = new Span<uint>((void*)(map.DataPointer + y * map.RowPitch), WindTable.Cols);
                grid.Slice(y * WindTable.Cols, WindTable.Cols).CopyTo(dst);
            }
        }
        finally { _ctx.Unmap(_flowTex, 0); }
    }

    /// <summary>올려 둔 WORLD.CDS 원본. 칸 지도를 다시 지을 때 쓴다.</summary>
    private byte[]? _world;

    /// <summary>
    /// 칸 지도 위에 덮어 둘 것 — <b>아직 안 선 도시를 지우는</b> 바탕 타일이다.
    /// </summary>
    /// <remarks>
    /// 게임은 그릴 때마다 칸마다 도시를 되짚어 갈아 끼우는데(<c>0x0048A1E0</c>), 우리는
    /// 칸 지도를 한 장으로 올려 두므로 <b>올릴 때 한 번</b> 덮는다. 도시가 새로 서면
    /// <see cref="Erase"/> 를 다시 불러 한 장을 새로 짓는다.
    /// </remarks>
    private IReadOnlyDictionary<(int X, int Y), ushort>? _erase;

    /// <summary>
    /// 지울 칸을 갈아 끼우고 칸 지도를 다시 짓는다. 같은 것이면 아무 일도 안 한다.
    /// </summary>
    public void Erase(IReadOnlyDictionary<(int X, int Y), ushort> patch)
    {
        if (_world is not { } world) return;
        if (_erase != null && Same(_erase, patch)) return;

        _erase = patch;
        var old = _cellSrv;
        CreateCellMap(world);
        old?.Dispose();
    }

    private static bool Same(IReadOnlyDictionary<(int X, int Y), ushort> a,
                             IReadOnlyDictionary<(int X, int Y), ushort> b)
    {
        if (a.Count != b.Count) return false;
        foreach (var (at, tile) in a)
            if (!b.TryGetValue(at, out ushort had) || had != tile) return false;
        return true;
    }

    /// <summary>WORLD.CDS 를 펼쳐 칸마다 타일 번호만 담은 텍스처를 만든다.</summary>
    private void CreateCellMap(byte[] world)
    {
        int w = WorldMapRenderer.UnfoldedW, h = WorldMapRenderer.CellH;
        var cells = new ushort[w * h];
        for (int ry = 0; ry < h; ry++)
        {
            int even = ry * 2 * WorldMapRenderer.RawStride;
            int odd = even + WorldMapRenderer.RawStride;
            int row = ry * w;
            for (int cx = 0; cx < WorldMapRenderer.CellW; cx++)
            {
                // 짝수 행이 지도의 왼쪽 절반, 홀수 행이 오른쪽 절반이다.
                cells[row + cx] = (ushort)((world[even + cx * 2] | (world[even + cx * 2 + 1] << 8)) & OceanTiles.TileMask);
                cells[row + cx + WorldMapRenderer.CellW] =
                    (ushort)((world[odd + cx * 2] | (world[odd + cx * 2 + 1] << 8)) & OceanTiles.TileMask);
            }
        }
        // 아직 안 선 도시를 지운다 — 도시 표 +0x74 의 바탕 타일로 덮는다.
        if (_erase is { } erase)
            foreach (var spot in erase)
            {
                var (cx, cy) = spot.Key;
                if (cx >= 0 && cx < w && cy >= 0 && cy < h) cells[cy * w + cx] = spot.Value;
            }

        _cellSrv = CreateImmutable(cells, w, h, Format.R16_UInt, sizeof(ushort));
    }

    /// <summary>16x16 타일 16,384장을 128x128 격자로 펴서 한 장으로 만든다.</summary>
    private void CreateAtlas(OceanTiles ocean)
    {
        var tiles = ocean.TileData;
        var atlas = new byte[AtlasSize * AtlasSize];
        for (int t = 0; t < OceanTiles.TileCount; t++)
        {
            int ax = (t % AtlasTiles) * OceanTiles.TileW;
            int ay = (t / AtlasTiles) * OceanTiles.TileW;
            int src = t * OceanTiles.TilePixels;
            for (int y = 0; y < OceanTiles.TileW; y++)
                Array.Copy(tiles, src + y * OceanTiles.TileW,
                           atlas, (ay + y) * AtlasSize + ax, OceanTiles.TileW);
        }
        _atlasSrv = CreateImmutable(atlas, AtlasSize, AtlasSize, Format.R8_UInt, 1);
    }

    private void CreatePalette(OceanTiles ocean)
    {
        var pal = new uint[256];
        for (int i = 0; i < 256; i++) pal[i] = ToBgra(ocean.PaletteRgb[i]);
        _paletteSrv = CreateImmutable(pal, 256, 1, Format.B8G8R8A8_UNorm, sizeof(uint));

        // 축소했을 때 쓸 평균색. 타일이 화면 한 점보다 작아지면 16x16 중 한 점만 뽑는 것이
        // 바다 디더링 무늬와 어긋나 물결(모아레)이 인다 — CPU 렌더러와 같은 표를 쓴다.
        for (int i = 0; i < AvgLevels.Length; i++)
        {
            int d = AvgLevels[i];
            var src = ocean.GetAverages(d);
            int side = AtlasTiles * d;
            var tex = new uint[side * side];
            for (int t = 0; t < OceanTiles.TileCount; t++)
            {
                int ax = (t % AtlasTiles) * d, ay = (t / AtlasTiles) * d;
                for (int qy = 0; qy < d; qy++)
                    for (int qx = 0; qx < d; qx++)
                        tex[(ay + qy) * side + ax + qx] = ToBgra(src[t * d * d + qy * d + qx]);
            }
            _avgSrv[i] = CreateImmutable(tex, side, side, Format.B8G8R8A8_UNorm, sizeof(uint));
        }
    }

    /// <summary>0xRRGGBB 를 B8G8R8A8 텍스처가 기대하는 배치로. 메모리 첫 바이트가 파랑이다.</summary>
    private static uint ToBgra(int rgb) => 0xFF000000u | (uint)(rgb & 0xFFFFFF);

    /// <summary>지금 배율에 맞는 잘게보기 단계를 고른다. 3 이면 원본 16x16 을 그대로 뽑는다.</summary>
    private static int PickDetail(double cellsPerPixel)
    {
        double pxPerCell = cellsPerPixel > 0 ? 1.0 / cellsPerPixel : OceanTiles.TileW;
        if (pxPerCell <= 1.0) return 0;   // 타일이 한 점보다 작다 -> 통째 평균
        if (pxPerCell <= 2.0) return 1;
        if (pxPerCell <= 4.0) return 2;
        return 3;
    }

    /// <summary>
    /// 배/말 그림 48x48 한 장과 덧그림(닻) 16x16 한 장. 매 프레임 갈아 끼울 수 있게
    /// 쓰기 가능으로 잡는다.
    /// </summary>
    private void CreateSpriteTexture()
    {
        _spriteTex = CreateDynamic(48);
        _spriteSrv = _device.CreateShaderResourceView(_spriteTex);
        _overlayTex = CreateDynamic(OverlaySize);
        _overlaySrv = _device.CreateShaderResourceView(_overlayTex);
    }

    private ID3D11Texture2D CreateDynamic(int side) => _device.CreateTexture2D(new Texture2DDescription
    {
        Width = (uint)side,
        Height = (uint)side,
        MipLevels = 1,
        ArraySize = 1,
        Format = Format.B8G8R8A8_UNorm,
        SampleDescription = new SampleDescription(1, 0),
        Usage = ResourceUsage.Dynamic,
        BindFlags = BindFlags.ShaderResource,
        CPUAccessFlags = CpuAccessFlags.Write,
    });

    private ID3D11ShaderResourceView CreateImmutable<T>(T[] data, int w, int h, Format fmt, int stride)
        where T : unmanaged
    {
        var handle = GCHandle.Alloc(data, GCHandleType.Pinned);
        try
        {
            var desc = new Texture2DDescription
            {
                Width = (uint)w,
                Height = (uint)h,
                MipLevels = 1,
                ArraySize = 1,
                Format = fmt,
                SampleDescription = new SampleDescription(1, 0),
                Usage = ResourceUsage.Immutable,
                BindFlags = BindFlags.ShaderResource,
            };
            var sub = new SubresourceData(handle.AddrOfPinnedObject(), (uint)(w * stride));
            using var tex = _device.CreateTexture2D(desc, [sub]);
            return _device.CreateShaderResourceView(tex);
        }
        finally { handle.Free(); }
    }

    /// <summary>배 그림을 갈아 끼운다. 48x48 BGRA 이고 알파 0 이 비침이다.</summary>
    public void SetSprite(ReadOnlySpan<uint> bgra48X48) => Upload(_spriteTex, bgra48X48, 48);

    /// <summary>배 위에 얹을 덧그림(닻)을 갈아 끼운다. 48x48 BGRA 다.</summary>
    public void SetOverlay(ReadOnlySpan<uint> bgra16X16) => Upload(_overlayTex, bgra16X16, OverlaySize);

    private void Upload(ID3D11Texture2D tex, ReadOnlySpan<uint> bgra, int side)
    {
        if (bgra.Length < side * side) return;
        var map = _ctx.Map(tex, 0, Vortice.Direct3D11.MapMode.WriteDiscard);
        try
        {
            for (int y = 0; y < side; y++)
            {
                var dst = new Span<uint>((void*)(map.DataPointer + y * map.RowPitch), side);
                bgra.Slice(y * side, side).CopyTo(dst);
            }
        }
        finally { _ctx.Unmap(tex, 0); }
    }

    /// <summary>그릴 대상 크기를 맞춘다. 바뀌었으면 새 텍스처를 만들고 true.</summary>
    public bool EnsureTarget(int width, int height)
    {
        if (_target != null && _targetW == width && _targetH == height) return false;

        _rtv?.Dispose();
        _target?.Dispose();
        _target = _device.CreateTexture2D(new Texture2DDescription
        {
            Width = (uint)width,
            Height = (uint)height,
            MipLevels = 1,
            ArraySize = 1,
            Format = Format.B8G8R8A8_UNorm,
            SampleDescription = new SampleDescription(1, 0),
            Usage = ResourceUsage.Default,
            BindFlags = BindFlags.RenderTarget | BindFlags.ShaderResource,
            // D3DImage 는 D3D9 표면을 받으므로 공유 텍스처로 만들어 건네준다.
            MiscFlags = ResourceOptionFlags.Shared,
        });
        _rtv = _device.CreateRenderTargetView(_target);
        _targetW = width;
        _targetH = height;
        return true;
    }

    /// <summary>
    /// 한 프레임을 그린다. <paramref name="originCell"/> 은 화면 왼쪽 위가 가리키는 칸 좌표,
    /// <paramref name="cellsPerPixel"/> 은 화면 한 점이 나아가는 칸 수다.
    /// <paramref name="spriteRect"/> 는 배 그림이 놓일 화면 사각형(폭이 0 이하면 안 그린다).
    /// </summary>
    public void Render((double X, double Y) originCell, (double X, double Y) cellsPerPixel,
                       (float X, float Y, float W, float H) spriteRect,
                       (float X, float Y, float W, float H) overlayRect = default)
    {
        if (_rtv == null) return;
        RenderTo(_rtv, _targetW, _targetH, originCell, cellsPerPixel, spriteRect, overlayRect);
        _ctx.Flush();
    }

    /// <summary>
    /// 밖에서 준 대상에 그린다. 스왑체인 백버퍼에 곧바로 그릴 때 쓴다 — D3DImage 를 거치지
    /// 않으므로 공유 표면 복사가 없다.
    /// </summary>
    public void RenderTo(ID3D11RenderTargetView rtv, int width, int height,
                         (double X, double Y) originCell, (double X, double Y) cellsPerPixel,
                         (float X, float Y, float W, float H) spriteRect,
                         (float X, float Y, float W, float H) overlayRect = default)
    {
        var cb = new FrameCb
        {
            OriginCellX = (float)originCell.X,
            OriginCellY = (float)originCell.Y,
            CellPerPixelX = (float)cellsPerPixel.X,
            CellPerPixelY = (float)cellsPerPixel.Y,
            SpriteX = spriteRect.X,
            SpriteY = spriteRect.Y,
            SpriteW = spriteRect.W,
            SpriteH = spriteRect.H,
            MapCellsX = WorldMapRenderer.UnfoldedW,
            MapCellsY = WorldMapRenderer.CellH,
            Detail = PickDetail(cellsPerPixel.X),
            OverlayX = overlayRect.X,
            OverlayY = overlayRect.Y,
            OverlayW = overlayRect.W,
            OverlayH = overlayRect.H,
            CoverR = Cover.R,
            CoverG = Cover.G,
            CoverB = Cover.B,
            CoverA = Cover.A,
            RippleDirX = Ripple.DirX,
            RippleDirY = Ripple.DirY,
            RippleSpeed = Ripple.Speed,
            RippleTick = Ripple.Tick,
            ArrowOn = ShowArrows ? 1 : 0,
            ArrowGrid = WindTable.CellRaw / OceanTiles.TileW,   // 800 원본단위 = 지도 50칸
            ArrowCols = WindTable.Cols,
            ArrowRows = WindTable.Rows,
        };
        for (int i = 0; i < _clouds.Length; i++) cb.Clouds[i] = _clouds[i];
        for (int i = 0; i < _folk.Length; i++) cb.Folk[i] = _folk[i];
        for (int i = 0; i < _route.Length; i++) cb.Route[i] = _route[i];

        var map = _ctx.Map(_cb, 0, Vortice.Direct3D11.MapMode.WriteDiscard);
        *(FrameCb*)map.DataPointer = cb;
        _ctx.Unmap(_cb, 0);

        _ctx.OMSetRenderTargets(rtv);
        _ctx.RSSetViewport(0, 0, width, height);
        _ctx.ClearRenderTargetView(rtv, new Color4(0, 0, 0, 1));
        _ctx.IASetPrimitiveTopology(PrimitiveTopology.TriangleList);
        _ctx.VSSetShader(_vs);
        _ctx.PSSetShader(_ps);
        _ctx.PSSetConstantBuffer(0, _cb);
        _ctx.PSSetShaderResources(0, [_cellSrv, _atlasSrv, _paletteSrv, _spriteSrv,
                                      _avgSrv[0], _avgSrv[1], _avgSrv[2], _overlaySrv,
                                      _nextSrv, _flowSrv, _arrowSrv, _cloudSrv, _folkSrv]);
        _ctx.Draw(3, 0);
    }

    /// <summary>대상 텍스처를 CPU 로 내려받는다(BGRA). 시험용.</summary>
    public byte[]? ReadBack()
    {
        if (_target == null) return null;
        using var staging = _device.CreateTexture2D(new Texture2DDescription
        {
            Width = (uint)_targetW,
            Height = (uint)_targetH,
            MipLevels = 1,
            ArraySize = 1,
            Format = Format.B8G8R8A8_UNorm,
            SampleDescription = new SampleDescription(1, 0),
            Usage = ResourceUsage.Staging,
            CPUAccessFlags = CpuAccessFlags.Read,
        });
        _ctx.CopyResource(staging, _target);
        var map = _ctx.Map(staging, 0, Vortice.Direct3D11.MapMode.Read);
        try
        {
            var outBuf = new byte[_targetW * _targetH * 4];
            for (int y = 0; y < _targetH; y++)
            {
                var src = new ReadOnlySpan<byte>((void*)(map.DataPointer + y * map.RowPitch), _targetW * 4);
                src.CopyTo(outBuf.AsSpan(y * _targetW * 4));
            }
            return outBuf;
        }
        finally { _ctx.Unmap(staging, 0); }
    }

    public void Dispose()
    {
        _rtv?.Dispose();
        _target?.Dispose();
        _folkSrv?.Dispose();
        _cloudSrv?.Dispose();
        _arrowSrv?.Dispose();
        _flowSrv?.Dispose();
        _flowTex?.Dispose();
        _nextSrv?.Dispose();
        _overlaySrv?.Dispose();
        _overlayTex?.Dispose();
        _spriteSrv?.Dispose();
        _spriteTex?.Dispose();
        foreach (var a in _avgSrv) a?.Dispose();
        _paletteSrv?.Dispose();
        _atlasSrv?.Dispose();
        _cellSrv?.Dispose();
        _cb?.Dispose();
        _ps?.Dispose();
        _vs?.Dispose();
        _ctx?.Dispose();
        _device?.Dispose();
    }
}
