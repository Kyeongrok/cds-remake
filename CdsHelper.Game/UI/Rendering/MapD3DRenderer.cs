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
    //       내 배는 지도 색을 다 낸 뒤에 얹는다(ShipOver) — 가장자리를 섞고 그림자·항적을 밑에 깔려면 바탕이 먼저 있어야 한다.
    //       ShipColor  큰 그림(ShipHi)이 있으면 그것을 매끈하게, 없으면 48x48 을 — 도트 필터가 켜져 있으면 모서리를 깎아 — 낸다.
    //       WakeOver   배가 지나온 자리(Wake)를 따라 벌어지는 물거품. 가장자리 두 줄이 짙고 가운데는 옅다. 물 점에만 든다.
    // CityCellSeaNote — 바다 입체 효과는 예전에 <b>수심 0 칸(뭍)</b>을 통째로 건너뛰었다. 도시 칸은 그림을 지형과 갈라 놓은 뒤에도
    // 지형 표에서 뭍으로 쳐 수심이 0 이라, 그 칸 안의 물 점만 음영이 빠져 바닷가 도시(시라쿠사 따위) 옆에 네모난 밝은 판이 섰다.
    // 이제 칸으로 거르지 않고 점의 색(파랑이 이기는지)으로만 물을 가린다.
    //
    // CoastFoamNote — 바다 입체 효과의 해안 물보라는 <b>칸 수심</b>(뭍까지 몇 칸)만 보면 1.7칸 안을 다 덮는다. 폭이 한두 칸인
    // 해협(지브롤터)은 통째로 그 안이라 해협 한가운데가 하얗게 일렁였고, 리스본 같은 들쭉날쭉한 해안도 하얗게 번졌다.
    // 그래서 원본 그림 점 단위로 반경 1.5~3점 안에 뭍이 얼마나 있는지(CoastNear)를 곱해 <b>해안에 바짝 붙은 얇은 띠</b>로만 낸다.
    //
    // HiResCoastNote — 고해상도 바다(HiResSeaCore)는 물/뭍 경계를 <b>두 점 묶음</b>(2x2 평균) 격자에서 B-스플라인으로 잰다.
    // 한 점 격자로 재면 해안 그림의 계단 · 섞어 찍은 그림자 점을 하나하나 따라가 해안선에 작은 혹이 줄줄이 붙고, 물 위에
    // 외딴 그림자 점이 떠 있었다 — 지브롤터처럼 두 해안이 붙은 곳에서 꿀렁거려 보였다. 한 칸(16점) 안에 여덟 묶음이라
    // 좁은 해협도 안 막힌다. 뭍 빛깔은 여전히 한 점 격자 가운데 넷에서 뽑는다.
    // <b>셰이더 글 안에는 한글을 쓰지 않는다</b> — HLSL 컴파일러가 UTF-8 주석에서 「파일 끝」 오류를 낸다.
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
        Texture2D<uint>   SeaDepth : register(t13);
        Texture2D<float4> CloudSoft: register(t14);
        Texture2D<uint>   PalWater : register(t15);
        Texture2D<uint>   TileKind : register(t16);
        Texture2D<uint>   CityMap  : register(t17);
        Texture2D<float4> CityTex  : register(t18);
        Texture2D<float4> CityHi   : register(t19);
        Texture2D<float4> SpriteHi : register(t20);
        Texture2D<float4> ShipHi   : register(t21);
        SamplerState      Lin      : register(s0);

        cbuffer Frame : register(b0)
        {
            float2 OriginCell;
            float2 CellPerPixel;
            float4 SpriteRect;
            float2 MapCells;
            float  Detail;
            float  SeaOn;
            float4 OverlayRect;
            float4 Cover;
            float4 Ripple;
            float4 Arrows;
            float4 Clouds[6];
            float4 Folk[16];
            float4 Route[32];
            float4 Sea;
            float4 Extra;
            float4 Flow;
            float4 FlowDir;
            float4 Ship;
            float4 Wake[12];
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
                if (Sea.y > 0.5)
                {
                    float2 dd = clamp(d, float2(0.5, 0.5), float2(159.5, 119.5));
                    float2 uv = float2(dd.x / 160.0, (dd.y + Clouds[k].z * 120.0) / 720.0);
                    float4 s = CloudSoft.SampleLevel(Lin, uv, 0);
                    col.rgb = col.rgb * (1.0 - s.a) + s.rgb;
                    continue;
                }
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

        float DepthAt(int2 q)
        {
            int w = int(MapCells.x);
            q.x = int(uint(q.x + w) % uint(w));
            q.y = clamp(q.y, 0, int(MapCells.y) - 1);
            return float(SeaDepth.Load(int3(q, 0)) & 15u);
        }

        float RiverAt(int2 q)
        {
            int w = int(MapCells.x);
            q.x = int(uint(q.x + w) % uint(w));
            q.y = clamp(q.y, 0, int(MapCells.y) - 1);
            return float(SeaDepth.Load(int3(q, 0)) >> 4);
        }

        float Hash(float2 p)
        {
            uint2 q = uint2(int2(p) + int2(65536, 65536));
            uint h = q.x * 1597334677u ^ q.y * 3812015801u;
            h = (h ^ (h >> 15)) * 2246822519u;
            h = h ^ (h >> 13);
            return float(h & 0xFFFFu) / 65535.0;
        }

        float Noise(float2 p)
        {
            float2 i = floor(p);
            float2 f = p - i;
            float2 u = f * f * (3.0 - 2.0 * f);
            return lerp(lerp(Hash(i), Hash(i + float2(1, 0)), u.x),
                        lerp(Hash(i + float2(0, 1)), Hash(i + float2(1, 1)), u.x), u.y);
        }

        float Waves(float2 p, float2 dir, float time)
        {
            float2 side = float2(-dir.y, dir.x);
            float h = 0.50 * Noise(p * 1.7 - dir * time * 0.55)
                    + 0.30 * Noise(p * 3.3 + side * time * 0.40 - dir * time * 0.30 + 17.0)
                    + 0.20 * Noise(p * 6.1 - dir * time * 0.85 - side * time * 0.25 + 41.0);
            h += 0.18 * sin(dot(p, dir) * 2.4 - time * 1.3 + Noise(p * 0.6) * 4.0);
            return h;
        }

        uint PalAt(int2 gi)
        {
            int w16 = int(MapCells.x) * 16;
            gi.x = int(uint(gi.x + w16) % uint(w16));
            gi.y = clamp(gi.y, 0, int(MapCells.y) * 16 - 1);
            int2 c = gi / 16;
            uint tile = CellMap.Load(int3(c, 0));
            int2 org = int2(tile % 128u, tile / 128u);
            return Atlas.Load(int3(org * 16 + (gi - c * 16), 0));
        }

        uint PixelArt(float2 cell, uint e)
        {
            float2 g  = cell * 16.0;
            int2   gi = int2(floor(g));
            float2 f  = g - floor(g);
            uint B = PalAt(gi + int2(0, -1));
            uint D = PalAt(gi + int2(-1, 0));
            uint F = PalAt(gi + int2(1, 0));
            uint H = PalAt(gi + int2(0, 1));
            if (B == H || D == F) return e;
            if (f.x + f.y < 0.5 && D == B) return D;
            if ((1.0 - f.x) + f.y < 0.5 && B == F) return F;
            if (f.x + (1.0 - f.y) < 0.5 && D == H) return D;
            if ((1.0 - f.x) + (1.0 - f.y) < 0.5 && H == F) return F;
            return e;
        }

        uint TexelPal(float2 g)
        {
            return PalAt(int2(floor(g)));
        }

        float WaterOf(uint pal)
        {
            return float(PalWater.Load(int3(int(pal), 0, 0)));
        }

        float4 BSpline(float t)
        {
            float t2 = t * t, t3 = t2 * t;
            return float4((1.0 - t) * (1.0 - t) * (1.0 - t),
                          3.0 * t3 - 6.0 * t2 + 4.0,
                          -3.0 * t3 + 3.0 * t2 + 3.0 * t + 1.0,
                          t3) / 6.0;
        }

        float3 HiResSeaCore(float3 col, float2 cell, uint pal)
        {
            float2 g = cell * 16.0 - 0.5;
            float2 b = floor(g);
            float2 f = g - b;

            float4 wx = BSpline(f.x);
            float4 wy = BSpline(f.y);
            float3 landSum = float3(0, 0, 0);
            float landW = 0.0;
            [unroll] for (int y = 0; y < 4; y++)
            {
                [unroll] for (int x = 0; x < 4; x++)
                {
                    uint p = TexelPal(b + float2(x - 1, y - 1));
                    float w = wx[x] * wy[y];
                    if (x >= 1 && x <= 2 && y >= 1 && y <= 2 && WaterOf(p) < 0.5)
                    {
                        landSum += Palette.Load(int3(int(p), 0, 0)).rgb * w;
                        landW += w;
                    }
                }
            }

            // coast mask on a 2x2-averaged grid (see HiResCoastNote in C#)
            float2 gc = cell * 8.0 - 0.5;
            float2 bc = floor(gc);
            float2 fc = gc - bc;
            float4 cx4 = BSpline(fc.x);
            float4 cy4 = BSpline(fc.y);
            float m = 0.0;
            [unroll] for (int yy = 0; yy < 4; yy++)
            {
                [unroll] for (int xx = 0; xx < 4; xx++)
                {
                    float2 o = (bc + float2(xx - 1, yy - 1)) * 2.0;
                    float wet = 0.25 * (WaterOf(TexelPal(o)) + WaterOf(TexelPal(o + float2(1, 0)))
                                      + WaterOf(TexelPal(o + float2(0, 1))) + WaterOf(TexelPal(o + float2(1, 1))));
                    m += wet * cx4[xx] * cy4[yy];
                }
            }
            if (m <= 0.02) return col;

            float edge = smoothstep(0.42, 0.58, m);

            float3 land = col;
            if (WaterOf(pal) > 0.5 && landW > 1e-4) land = landSum / landW;

            // bank rim for the Nile (river nibble 15, see MapShaderData.NileMark): dark brown cliff band on the land side
            if (RiverAt(int2(floor(cell))) > 14.5)
            {
                float jag = 0.55 + 0.45 * Noise(cell * 22.0) + 0.25 * Noise(cell * 61.0);
                float rim = smoothstep(0.03, 0.22, m * jag) * (1.0 - edge);
                float lip = smoothstep(0.20, 0.40, m);
                float3 cliff = lerp(float3(0.42, 0.27, 0.16), float3(0.18, 0.10, 0.05), lip);
                land = lerp(land, cliff, saturate(rim));
            }

            float2 cb = cell - 0.5;
            int2   cq = int2(floor(cb));
            float2 ct = cb - floor(cb);
            float d = lerp(lerp(DepthAt(cq), DepthAt(cq + int2(1, 0)), ct.x),
                           lerp(DepthAt(cq + int2(0, 1)), DepthAt(cq + int2(1, 1)), ct.x), ct.y);
            float3 tone = lerp(float3(0.31, 0.44, 0.52), float3(0.21, 0.34, 0.43), saturate((d - 1.0) / 5.0));

            float t = Sea.x;
            float2 drift = Flow.xy;
            float grain = 0.55 * Noise((cell - drift * 0.25) * 21.0 + float2(t * 0.35, t * 0.10))
                        + 0.45 * Noise((cell - drift * 0.5) * 8.5 - float2(t * 0.15, t * 0.22) + 7.0);
            float3 sea = tone * (0.92 + 0.16 * grain);

            float flow = Flow.z;
            if (flow > 0.01)
            {
                float2 fd = FlowDir.xy;
                float2 q = cell - drift;
                float streak = 0.10 * Noise((q - fd * 0.66) * 7.0)
                             + 0.15 * Noise((q - fd * 0.44) * 7.0)
                             + 0.17 * Noise((q - fd * 0.22) * 7.0)
                             + 0.16 * Noise(q * 7.0)
                             + 0.17 * Noise((q + fd * 0.22) * 7.0)
                             + 0.15 * Noise((q + fd * 0.44) * 7.0)
                             + 0.10 * Noise((q + fd * 0.66) * 7.0);
                streak = saturate((streak - 0.5) * 2.6 + 0.5);
                sea *= 1.0 + flow * 0.34 * (streak - 0.5);
                sea += flow * 0.09 * smoothstep(0.62, 0.90, streak);

                float band = (dot(Ripple.xy, cell) - Ripple.z * Flow.w * 16.0) / 64.0;
                sea *= 1.0 + flow * 0.06 * sin(band * 0.3926991);
            }

            return lerp(land, sea, edge);
        }

        float3 HiResSea(float3 col, float2 cell, uint pal)
        {
            float2 rb = cell - 0.5;
            int2   rq = int2(floor(rb));
            float2 rt = rb - floor(rb);
            float river = lerp(lerp(RiverAt(rq), RiverAt(rq + int2(1, 0)), rt.x),
                               lerp(RiverAt(rq + int2(0, 1)), RiverAt(rq + int2(1, 1)), rt.x), rt.y);
            float keep = saturate((river - 0.5) / 3.0);
            if (keep <= 0.0) return col;
            return lerp(col, HiResSeaCore(col, cell, pal), keep);
        }

        float CoastNear(float2 cell)
        {
            float2 g = cell * 16.0;
            float land = 0.0;
            [unroll] for (int k = 0; k < 8; k++)
            {
                float a = k * 0.785398;
                float2 o = float2(cos(a), sin(a)) * 3.0;
                land += 1.0 - WaterOf(TexelPal(g + o));
            }
            float2 o2[4] = { float2(1.5, 0), float2(-1.5, 0), float2(0, 1.5), float2(0, -1.5) };
            [unroll] for (int j = 0; j < 4; j++) land += 2.0 * (1.0 - WaterOf(TexelPal(g + o2[j])));
            return saturate(land / 6.0);
        }

        float3 SeaShade(float3 col, float2 cell)
        {
            // no per-cell land skip: city cells count as land but hold water texels (CityCellSeaNote in C#)
            float water = saturate((col.b - max(col.r, col.g) + 0.01) * 14.0);
            if (water <= 0.0) return col;
            float3 base = col;

            float2 b = cell - 0.5;
            int2   q = int2(floor(b));
            float2 t = b - floor(b);
            float d = lerp(lerp(DepthAt(q), DepthAt(q + int2(1, 0)), t.x),
                           lerp(DepthAt(q + int2(0, 1)), DepthAt(q + int2(1, 1)), t.x), t.y);

            float time = Sea.x;
            float fine = saturate((0.30 - CellPerPixel.x) / 0.20);

            float2 dir = Ripple.xy;
            dir = dot(dir, dir) > 1e-4 ? normalize(dir) : float2(0.94, 0.34);

            float2 p = cell;
            const float e = 0.05;
            float h0 = Waves(p, dir, time);
            float hx = Waves(p + float2(e, 0), dir, time);
            float hy = Waves(p + float2(0, e), dir, time);
            float2 g = float2(hx - h0, hy - h0) / e * 0.11;

            float3 n = normalize(float3(-g * fine, 1.0));
            float3 L = normalize(float3(-0.45, -0.55, 0.70));
            float diff = dot(n, L) / L.z;
            float3 H = normalize(L + float3(0, 0, 1));
            float glint = pow(saturate(dot(n, H)), 90.0) * smoothstep(0.55, 0.85, h0);
            float spec = glint * 0.45 * fine;

            float deep = saturate((d - 1.0) / 10.0);
            col *= lerp(1.16, 0.98, deep);
            col *= lerp(1.0, diff, 0.50);
            col += spec;

            // foam hugs the coast at texel scale (CoastFoamNote in C#)
            float shore = saturate(1.7 - d) * CoastNear(cell);
            float foam = shore * smoothstep(0.35, 0.75,
                0.5 + 0.5 * sin(time * 1.4 - d * 6.0 + Noise(p * 2.0 + time * 0.2) * 5.0));
            col = lerp(col, float3(0.93, 0.96, 0.98), foam * 0.35 * saturate(1.0 - CellPerPixel.x * 2.0));
            col *= Sea.z;
            return lerp(base, saturate(col), water);
        }

        float3 LandDetail(float3 col, float2 cell, uint tile)
        {
            if (col.b - max(col.r, col.g) > 0.01) return col;
            uint kind = TileKind.Load(int3(int2(tile % 128u, tile / 128u), 0));
            if (kind <= 1u || kind == 7u) return col;

            float px = 1.0 / max(CellPerPixel.x, 1e-4);
            float amount = saturate((px - 4.0) / 12.0);
            if (amount <= 0.0) return col;

            float2 p = cell;
            float m;
            if (kind == 4u)
            {
                float dune = 0.5 + 0.5 * sin(dot(p, float2(0.8, 0.6)) * 14.0 + Noise(p * 3.0) * 6.0);
                m = 0.96 + 0.05 * dune + 0.04 * (Noise(p * 90.0) - 0.5);
            }
            else if (kind == 3u)
            {
                float r = 1.0 - abs(Noise(p * 26.0) * 2.0 - 1.0);
                m = 0.86 + 0.22 * r * r + 0.08 * (Noise(p * 95.0) - 0.5);
            }
            else if (kind == 6u)
            {
                float leaf = smoothstep(0.30, 0.80, Noise(p * 48.0));
                m = 0.84 + 0.26 * leaf + 0.06 * (Noise(p * 120.0) - 0.5);
            }
            else
            {
                m = 0.96 + 0.06 * Noise(p * 14.0) + 0.04 * (Noise(p * 70.0) - 0.5);
            }
            return saturate(col * lerp(1.0, m, amount));
        }

        float4 SpriteTexel(int2 q)
        {
            if (any(q < 0) || any(q > 47)) return float4(0, 0, 0, 0);
            return Sprite.Load(int3(q, 0));
        }

        float ShipLod()
        {
            return max(0.0, log2(Ship.y / max(SpriteRect.z, 1.0)));
        }

        float4 ShipColor(float2 s)
        {
            if (Ship.y > 0.5) return ShipHi.SampleLevel(Lin, s, ShipLod());

            float2 g = s * 48.0;
            int2 gi = int2(floor(g));
            float4 e = SpriteTexel(gi);
            if (Extra.x > 0.5)
            {
                float2 f = g - floor(g);
                float4 B = SpriteTexel(gi + int2(0, -1));
                float4 D = SpriteTexel(gi + int2(-1, 0));
                float4 F = SpriteTexel(gi + int2(1, 0));
                float4 H = SpriteTexel(gi + int2(0, 1));
                if (!all(B == H) && !all(D == F))
                {
                    if (f.x + f.y < 0.5 && all(D == B)) e = D;
                    else if ((1.0 - f.x) + f.y < 0.5 && all(B == F)) e = F;
                    else if (f.x + (1.0 - f.y) < 0.5 && all(D == H)) e = D;
                    else if ((1.0 - f.x) + (1.0 - f.y) < 0.5 && all(H == F)) e = F;
                }
            }
            float a = step(0.001, e.a);
            return float4(e.rgb * a, a);
        }

        float3 ShipOver(float3 col, float2 px)
        {
            if (SpriteRect.z <= 0) return col;
            float2 s = (px - SpriteRect.xy) / SpriteRect.zw;
            if (Ship.x > 0.5)
            {
                s.y -= Ship.z;
                float2 sh = s - float2(0.035, 0.060);
                if (all(sh >= 0) && all(sh < 1))
                {
                    float a = Ship.y > 0.5 ? ShipHi.SampleLevel(Lin, sh, ShipLod() + 1.0).a
                                           : Sprite.SampleLevel(Lin, sh, 0).a;
                    col *= 1.0 - 0.30 * a;
                }
            }
            if (any(s < 0) || any(s >= 1)) return col;
            float4 c = ShipColor(s);
            return c.rgb + col * (1.0 - c.a);
        }

        float3 WakeOver(float3 col, float2 px, float2 cell, float wet)
        {
            if (Ship.x < 0.5 || wet <= 0.0) return col;
            float pxPerCell = 1.0 / max(CellPerPixel.x, 1e-5);
            float foam = 0.0;
            [loop] for (int k = 0; k < 11; k++)
            {
                if (Wake[k].w <= 0 || Wake[k + 1].w <= 0) continue;
                float2 a = Wake[k].xy;
                float2 ab = Wake[k + 1].xy - a;
                float t = saturate(dot(px - a, ab) / max(dot(ab, ab), 1e-5));
                float d = distance(px, a + ab * t) / pxPerCell;
                float age = lerp(Wake[k].z, Wake[k + 1].z, t);
                float r = d / (0.22 + 1.05 * age);
                if (r >= 1.0) continue;
                float rim = smoothstep(0.45, 0.85, r) * (1.0 - smoothstep(0.85, 1.0, r));
                float churn = (1.0 - r) * 0.35;
                float life = (1.0 - age) * (1.0 - age);
                foam = max(foam, (rim + churn) * life);
            }
            if (foam <= 0.0) return col;
            float n = 0.55 + 0.45 * Noise(cell * 13.0 + Sea.x * 0.6);
            return lerp(col, float3(0.90, 0.95, 0.97), saturate(foam * n * 0.62) * wet);
        }

        float4 PS(VSOut i) : SV_Target
        {
            float2 cellRaw = OriginCell + i.pos.xy * CellPerPixel;
            float2 cell = cellRaw;
            cell.x = cell.x - floor(cell.x / MapCells.x) * MapCells.x;
            cell.y = clamp(cell.y, 0, MapCells.y - 0.001);

            int2 c    = int2(cell);
            uint tile = CellMap.Load(int3(c, 0));

            bool swapped = false;
            if (Ripple.z > 0)
            {
                float p = (Ripple.x * float(c.x) + Ripple.y * float(c.y)
                           - Ripple.z * Ripple.w * 16.0) / 64.0;
                if ((int(floor(p)) & 15) < 8)
                {
                    uint next = NextTile.Load(int3(int2(tile % 128u, tile / 128u), 0));
                    swapped = next != tile;
                    tile = next;
                }
            }

            int2 org  = int2(tile % 128u, tile / 128u);
            float2 f  = frac(cell);

            float4 col;
            uint pal = 0u;
            if (Detail < 0.5)      col = Avg1.Load(int3(org, 0));
            else if (Detail < 1.5) col = Avg2.Load(int3(org * 2 + int2(f * 2.0), 0));
            else if (Detail < 2.5) col = Avg4.Load(int3(org * 4 + int2(f * 4.0), 0));
            else
            {
                pal = Atlas.Load(int3(org * 16 + int2(f * 16.0), 0));
                if (Extra.x > 0.5 && !swapped) pal = PixelArt(cell, pal);
                col = Palette.Load(int3(int(pal), 0, 0));
            }

            if (Extra.y > 0.5 && Detail > 2.5) col.rgb = LandDetail(col.rgb, cell, tile);
            if (Sea.w > 0.5 && Detail > 2.5) col.rgb = HiResSea(col.rgb, cell, pal);
            if (SeaOn > 0.5) col.rgb = SeaShade(col.rgb, cell);
            float wet = saturate((col.b - max(col.r, col.g)) * 14.0 + 0.14);

            if (Extra.z > 0.5)
            {
                uint v = CityMap.Load(int3(c, 0));
                if (v != 0u)
                {
                    uint id = (v & 511u) - 1u;
                    float2 tf = float2(float((v >> 9) & 3u) * 16.0 + f.x * 16.0,
                                       float((v >> 11) & 3u) * 16.0 + f.y * 16.0);
                    float4 h0 = SpriteHi.Load(int3(int(id), 0, 0));
                    if (h0.w > 0.5)
                    {
                        float4 h1 = SpriteHi.Load(int3(int(id), 1, 0));
                        float2 local = tf - h1.xy;
                        if (all(local >= 0.0) && all(local < h1.zw))
                        {
                            float4 s = CityHi.Load(int3(int2(h0.xy + local * h0.z), 0));
                            if (s.a > 0.5) col.rgb = s.rgb;
                        }
                    }
                    else
                    {
                        float4 s = CityTex.Load(int3(int2(int(id % 16u) * 48, int(id / 16u) * 48) + int2(tf), 0));
                        if (s.a > 0.0) col.rgb = s.rgb;
                    }
                }
            }

            if (Arrows.x > 0.5)
            {
                float4 a = ArrowsAt(cellRaw, i.pos.xy);
                col.rgb = lerp(col.rgb, a.rgb, a.a);
            }
            col.rgb = WakeOver(col.rgb, i.pos.xy, cell, wet);
            col.rgb = RouteOver(col.rgb, i.pos.xy);
            col = FolkOver(col, i.pos.xy);
            col.rgb = ShipOver(col.rgb, i.pos.xy);

            if (OverlayRect.z > 0)
            {
                float2 v = (i.pos.xy - OverlayRect.xy) / OverlayRect.zw;
                if (all(v >= 0) && all(v < 1))
                {
                    float4 o = Overlay.Load(int3(int2(v * 48.0), 0));
                    if (o.a > 0) col = o;
                }
            }
            return Tint(CloudsOver(col, i.pos.xy));
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
        public float SeaOn;
        public float OverlayX, OverlayY, OverlayW, OverlayH;
        public float CoverR, CoverG, CoverB, CoverA;
        public float RippleDirX, RippleDirY, RippleSpeed, RippleTick;
        public float ArrowOn, ArrowGrid, ArrowCols, ArrowRows;
        public fixed float Clouds[MaxClouds * 4];   // x, y, 그림번호, 보일지
        public fixed float Folk[MaxFolk * 4];       // x, y, 뱃머리(0~3), 배수
        public fixed float Route[MaxRoutePoints * 4]; // x, y, (안 씀), 켜졌는지
        public float SeaTime, CloudSmooth, SeaBright, HiSea;
        public float PixelOn, LandOn, CityOn, ExtraPad3;
        public float FlowX, FlowY, FlowStrength, FlowTick;
        public float FlowDirX, FlowDirY, FlowPad0, FlowPad1;
        public float ShipFxOn, ShipHiSide, ShipBobV, ShipPad;
        public fixed float Wake[MaxWake * 4];       // x, y(화면 점), 나이 0~1, 켜졌는지
    }

    /// <summary>항적 마디 수. 첫 마디가 지금 배 자리다.</summary>
    public const int MaxWake = 12;

    private readonly float[] _wake = new float[MaxWake * 4];

    /// <summary>
    /// 배가 바다에 닿는 효과 — 항적·그림자·출렁임. 바다에 떠 있을 때만 밖에서 켠다.
    /// </summary>
    public bool ShipFx { get; set; }

    /// <summary>배 그림을 위아래로 민 만큼(그림 한 변을 1 로). 출렁임이다.</summary>
    public float ShipBob { get; set; }

    /// <summary>항적 마디를 건다 — 화면 점 자리와 나이(0 갓 지난 자리 ~ 1 스러질 자리). 첫 마디가 배다.</summary>
    public void SetWake(ReadOnlySpan<(float X, float Y, float Age)> points)
    {
        Array.Clear(_wake);
        for (int i = 0; i < points.Length && i < MaxWake; i++)
        {
            _wake[i * 4 + 0] = points[i].X;
            _wake[i * 4 + 1] = points[i].Y;
            _wake[i * 4 + 2] = points[i].Age;
            _wake[i * 4 + 3] = 1;
        }
    }

    private ID3D11ShaderResourceView _shipHiSrv = null!;
    private int _shipHiSide;
    private uint[]? _shipHiSource;

    /// <summary>
    /// 고해상도 배 그림을 건다. null 이면 내린다 — 그때는 48x48 그림(<see cref="SetSprite"/>)을 쓴다.
    /// </summary>
    /// <param name="premultiplied">정사각 BGRA, <b>알파를 곱해 둔</b> 것. 선형으로 늘려도 가장자리에 검은 테가 안 생긴다.</param>
    /// <param name="side">한 변. 줄여 그릴 때 지글거리지 않게 밉 단계를 여기서 다 지어 올린다.</param>
    public void SetShipHi(uint[]? premultiplied, int side)
    {
        if (ReferenceEquals(premultiplied, _shipHiSource)) return;
        _shipHiSource = premultiplied;
        if (premultiplied == null || side <= 0 || premultiplied.Length < side * side)
        {
            _shipHiSide = 0;
            return;
        }

        var levels = new List<(uint[] Px, int Side)> { (premultiplied, side) };
        while (levels[^1].Side > 1)
        {
            var (src, n) = levels[^1];
            int m = Math.Max(1, n / 2);
            var dst = new uint[m * m];
            for (int y = 0; y < m; y++)
                for (int x = 0; x < m; x++)
                {
                    int x0 = Math.Min(x * 2, n - 1), x1 = Math.Min(x * 2 + 1, n - 1);
                    int y0 = Math.Min(y * 2, n - 1), y1 = Math.Min(y * 2 + 1, n - 1);
                    uint a = src[y0 * n + x0], b = src[y0 * n + x1], c = src[y1 * n + x0], d = src[y1 * n + x1];
                    uint o = 0;
                    for (int sh = 0; sh < 32; sh += 8)
                        o |= ((((a >> sh) & 255) + ((b >> sh) & 255) + ((c >> sh) & 255) + ((d >> sh) & 255) + 2) / 4) << sh;
                    dst[y * m + x] = o;
                }
            levels.Add((dst, m));
        }

        var pins = new GCHandle[levels.Count];
        try
        {
            var subs = new SubresourceData[levels.Count];
            for (int i = 0; i < levels.Count; i++)
            {
                pins[i] = GCHandle.Alloc(levels[i].Px, GCHandleType.Pinned);
                subs[i] = new SubresourceData(pins[i].AddrOfPinnedObject(), (uint)(levels[i].Side * sizeof(uint)));
            }
            using var tex = _device.CreateTexture2D(new Texture2DDescription
            {
                Width = (uint)side,
                Height = (uint)side,
                MipLevels = (uint)levels.Count,
                ArraySize = 1,
                Format = Format.B8G8R8A8_UNorm,
                SampleDescription = new SampleDescription(1, 0),
                Usage = ResourceUsage.Immutable,
                BindFlags = BindFlags.ShaderResource,
            }, subs);
            var old = _shipHiSrv;
            _shipHiSrv = _device.CreateShaderResourceView(tex);
            old?.Dispose();
            _shipHiSide = side;
        }
        finally
        {
            foreach (var pin in pins) if (pin.IsAllocated) pin.Free();
        }
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
    /// 남의 그림 장수 — 배 그림 벌 넷(코구·카라벨·카락·갤리온)마다 넉 장(북·서·남·동)에 말 넉 장을 이어 붙인 스무 장이다.
    /// 원본은 남의 배도 내 기함 벌 하나로 그리지만(<c>0x00569FE4</c>), 우리는 그 사람 기함 선체의 벌로 그린다.
    /// </summary>
    /// <remarks>
    /// 게임은 사람 자리의 부류가 2 이상(뭍)이면 배 대신 <b>말</b>을 그린다(<c>0x0048A799</c> 의
    /// <c>cmp eax, 2 / jge</c> → 뭍 그림 벌 <c>0x00569FE8</c>). 곧은 길로 가다 뭍을 만나면 말로,
    /// 다시 바다로 나오면 배로 바뀐다. 셰이더는 <c>Folk[k].z * 48</c> 로 줄을 내리므로 장수를
    /// 모른다 — 여기 값만 맞추면 된다.
    /// </remarks>
    public const int FolkFrames = FolkSkins * FolkWays + FolkWays * FolkLandPhases;

    /// <summary>배 그림 벌 수와 벌마다의 방향 장수.</summary>
    public const int FolkSkins = 4, FolkWays = 4;

    /// <summary>말은 방향마다 걸음 여덟 장이다 — 내 말과 같은 벌이라 다리가 같이 움직인다.</summary>
    public const int FolkLandPhases = Local.Helpers.ShipSprites.WalkPhases;

    /// <summary>말 그림이 시작하는 장. 배 열여섯 장 다음이다.</summary>
    public const int FolkLandFrame = FolkSkins * FolkWays;

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

    /// <summary>
    /// 고해상도 바다가 해류를 보이는 데 쓰는 값 — 물 무늬가 흘러간 만큼(칸), 흐름 방향(길이 1), 또렷함(0~1),
    /// 그리고 끊기지 않게 이은 틱이다.
    /// </summary>
    /// <remarks>
    /// 고해상도 바다는 물 점을 통째로 새로 칠해, 원본이 타일을 갈아 끼워 보이던 물결(<see cref="Ripple"/>)이
    /// 묻힌다. 그래서 그 바다의 잔물결을 이만큼 밀어 흘리고, 흐름 쪽으로 늘어진 결을 얹고, 원본 띠와 같은
    /// 간격·빠르기의 옅은 밝기 물결을 지나가게 한다. 흘러간 만큼은 밖에서 쌓아 준다 — 방향이 바뀌어도
    /// 무늬가 튀지 않는다.
    /// </remarks>
    public (float X, float Y, float DirX, float DirY, float Strength, float Tick) FlowDrift { get; set; }

    /// <summary>고해상도 바다에서 해류 결·띠의 짙기(0~1). 0 이면 잔물결만 흐르고 결과 띠는 안 선다.</summary>
    public float FlowAmount { get; set; } = 0.5f;

    /// <summary>바람·해류 화살표를 얹을지. 게임에는 없는 것이라 커맨드 창에서 끄고 켠다.</summary>
    public bool ShowArrows { get; set; }

    /// <summary>
    /// 바다 입체 효과 — 바다 칸에 움직이는 물결 굴곡·햇빛·반짝임을 얹고, 해안에서 멀수록 깊은 색으로,
    /// 해안선에는 흰 물보라를 친다. 원본에 없는 덧그림이라 모드 창에서 켠다. 깊이 표(<see cref="SetSeaDepth"/>)가 있어야 듣는다.
    /// </summary>
    public bool SeaEffect { get; set; }

    /// <summary>바다 물결이 흐르는 시각(초). 켜 둔 동안 프레임마다 밖에서 올린다.</summary>
    public float SeaTime { get; set; }

    /// <summary>바다 입체 효과의 밝기 배수. 1 이 기본이다.</summary>
    public float SeaBrightness { get; set; } = 1f;

    /// <summary>
    /// 부드러운 구름 — 원본은 바둑판으로 한 점 걸러 찍어 반투명을 흉내 내서, 키우면 격자가 그대로 커진다.
    /// 켜면 그 바둑판을 참 투명도(약 반)로 풀어 둔 그림을 선형 보간으로 늘려 그린다.
    /// </summary>
    public bool SmoothClouds { get; set; } = true;

    private ID3D11ShaderResourceView _cloudSoftSrv = null!;

    /// <summary>
    /// 고해상도 바다 — 물 점(파랑이 빨강·초록보다 큰 점)을 원본 16x16 타일 대신 화면 해상도로 새로 그린다.
    /// 바탕색은 해안 거리로 정하는 부드러운 물빛(얕으면 밝고 멀면 짙다)이고 그 위에 잔물결 무늬를 얹는다.
    /// 해안선은 타일 점 넷의 물·뭍을 보간해 문턱을 매끈하게 넘겨 계단 대신 곡선이 된다. 뭍 점은 원본 그대로다.
    /// 칸이 화면 네 점보다 클 때(세부 3)만 든다.
    /// </summary>
    public bool HiResSea { get; set; }

    /// <summary>
    /// 도트 확대 필터 — 키운 원본 도트의 대각선 계단을 사선으로 깎는다. Scale2x 의 규칙(위·왼쪽이 같고 오른쪽·아래와
    /// 다르면 그 모서리를 이웃 색으로)을 연속 배율로 옮겨, 한 점 안에서 모서리 삼각형만 이웃 색으로 채운다.
    /// 색 번호가 같은지로만 보므로 바둑판 잔무늬(디더)는 그대로 남는다. 칸이 화면 네 점보다 클 때만 든다.
    /// </summary>
    public bool PixelFilter { get; set; }

    /// <summary>
    /// 뭍 세부 질감 — 키웠을 때 원본 도트는 그대로 두고 지형마다 화면 해상도의 잔무늬를 얇게 얹는다
    /// (사막 모래 결 · 산 바위 결 · 숲 잎 덩이 · 평지 풀 결). 지형은 타일 부류 표(<see cref="SetTileKinds"/>)로 가르고,
    /// 도시·발견물 그림 타일과 물 점은 건드리지 않는다. 칸이 화면 네 점보다 클 때부터 배율에 따라 짙어진다.
    /// </summary>
    public bool LandDetail { get; set; }

    private ID3D11ShaderResourceView _tileKindSrv = null!;
    private bool _tileKindsReady;

    /// <summary>
    /// 도시 분리 — 도시 칸을 모두 <b>바탕 타일</b>(도시 표 <c>+0x74</c>)로 깔고, 선 도시만 그 위에 뽑아 둔 도시 그림을 얹는다.
    /// 도시 그림은 고해상도 바다·세부 질감·도트 필터를 다 거친 뒤에 얹으므로 그 효과들이 도시를 건드리지 않는다.
    /// 게임 규칙(뭍·물 판정)은 지도 원본을 그대로 보므로 바뀌지 않는다.
    /// </summary>
    public bool CitySprites { get; private set; }

    /// <summary>
    /// 그림 블록마다의 칸 자리 · 바탕 블록 · 블록 한 변 — 앞 226 이 도시(3x3), 그 뒤가 발견물(2x2)이고 번호가 곧 판 자리다.
    /// 그림을 안 걸었으면 빈 배열.
    /// </summary>
    private (int X, int Y, ushort[] Block, int Width)[] _cityBlocks = [];
    private readonly HashSet<int> _hiddenCities = [];
    private ID3D11ShaderResourceView _cityMapSrv = null!;
    private ID3D11ShaderResourceView _cityTexSrv = null!;

    /// <summary>도시 그림 판(48x48 x 226장, 16장씩 줄)과 도시 블록을 이미 걸었는지.</summary>
    public bool CityArtReady { get; private set; }

    /// <summary>도시 그림 한 장의 변(3칸 x 16점)과 판 한 줄에 놓는 장 수.</summary>
    public const int CitySpriteSide = 48, CitySpritesPerRow = 16;

    /// <summary>그림 판과 그림 블록(도시 · 발견물)을 건다. 블록 번호가 판 자리(16장씩 줄)다.</summary>
    public void SetCityArt(uint[] atlas, int atlasW, int atlasH, (int X, int Y, ushort[] Block, int Width)[] blocks)
    {
        var old = _cityTexSrv;
        _cityTexSrv = CreateImmutable(atlas, atlasW, atlasH, Format.B8G8R8A8_UNorm, sizeof(uint));
        old?.Dispose();
        _cityBlocks = blocks;
        CityArtReady = true;
        if (CitySprites) RebuildCells();
    }

    private ID3D11ShaderResourceView _cityHiSrv = null!;
    private ID3D11ShaderResourceView _spriteHiSrv = null!;

    /// <summary>
    /// 고해상도 도시 그림을 건다 — <paramref name="atlas"/> 는 디자인 그림들을 늘어놓은 판, <paramref name="info"/> 는 그림 블록마다
    /// 여덟 값(판 x · 판 y · 배율 · 쓸지 / 블록 안 x · y · 디자인 폭 · 높이)이다. 쓸지가 0 인 블록은 원본 그림을 쓴다.
    /// </summary>
    public void SetCityHiRes(uint[] atlas, int atlasW, int atlasH, float[] info, int count)
    {
        var oldA = _cityHiSrv;
        _cityHiSrv = CreateImmutable(atlas, Math.Max(1, atlasW), Math.Max(1, atlasH), Format.B8G8R8A8_UNorm, sizeof(uint));
        oldA?.Dispose();
        // 두 줄짜리 float4 표 — 0 줄이 판 자리·배율·쓸지, 1 줄이 블록 안 자리·디자인 크기.
        var table = new float[Math.Max(1, count) * 2 * 4];
        for (int i = 0; i < count; i++)
        {
            Array.Copy(info, i * 8, table, i * 4, 4);
            Array.Copy(info, i * 8 + 4, table, (count + i) * 4, 4);
        }
        var oldI = _spriteHiSrv;
        _spriteHiSrv = CreateImmutable(table, Math.Max(1, count), 2, Format.R32G32B32A32_Float, sizeof(float) * 4);
        oldI?.Dispose();
    }

    /// <summary>도시 분리를 켜고 끈다 — 칸 지도를 다시 짓는다.</summary>
    public void SetCitySprites(bool on)
    {
        if (CitySprites == on) return;
        CitySprites = on;
        RebuildCells();
    }

    /// <summary>그림을 얹지 않을 블록들 — 아직 안 선 도시, 아직 못 찾은 발견물. 도시 분리를 켰을 때만 칸 지도를 다시 짓는다.</summary>
    public void SetHiddenCities(IEnumerable<int> hidden)
    {
        var next = new HashSet<int>(hidden);
        if (next.SetEquals(_hiddenCities)) return;
        _hiddenCities.Clear();
        _hiddenCities.UnionWith(next);
        if (CitySprites) RebuildCells();
    }

    /// <summary>
    /// 지도를 통째로 갈아 끼운다 — WORLD.CDS 편집기가 칠한 것을 미리보기에 비출 때 쓴다.
    /// </summary>
    public void UpdateWorld(byte[] world)
    {
        _world = world;
        RebuildCells();
    }

    private void RebuildCells()
    {
        if (_world is not { } world) return;
        var old = _cellSrv;
        CreateCellMap(world);
        old?.Dispose();
    }

    /// <summary>타일 부류 표를 이미 올렸는지.</summary>
    public bool TileKindsReady => _tileKindsReady;

    /// <summary>
    /// 타일마다의 지형 부류(16,384바이트, 지형표 값 · 그림 타일은 7)를 아틀라스와 같은 128x128 격자로 올린다.
    /// </summary>
    public void SetTileKinds(byte[] kinds)
    {
        if (kinds.Length != OceanTiles.TileCount) return;
        var old = _tileKindSrv;
        _tileKindSrv = CreateImmutable(kinds, AtlasTiles, AtlasTiles, Format.R8_UInt, sizeof(byte));
        old?.Dispose();
        _tileKindsReady = true;
    }

    /// <summary>팔레트 색마다 물인지(1)·아닌지(0) — 256x1. <see cref="SetWaterPalette"/> 로 건다.</summary>
    private ID3D11ShaderResourceView _palWaterSrv = null!;

    /// <summary>
    /// 팔레트 색마다 물 색인지를 건다(256바이트, 1 이 물). 물 칸 타일에 주로 쓰이고 흙빛이 아닌 색이 물이다 —
    /// 강·해안·물결 점이 다 같은 물로 다시 그려진다.
    /// </summary>
    public void SetWaterPalette(byte[] water)
    {
        if (water.Length != 256) return;
        var old = _palWaterSrv;
        _palWaterSrv = CreateImmutable(water, 256, 1, Format.R8_UInt, sizeof(byte));
        old?.Dispose();
        WaterPaletteReady = true;
    }

    /// <summary>물 색 표를 이미 걸었는지.</summary>
    public bool WaterPaletteReady { get; private set; }
    private ID3D11SamplerState _linear = null!;

    private ID3D11ShaderResourceView _seaSrv = null!;
    private bool _seaReady;

    /// <summary>
    /// 칸마다 <b>뭍까지의 거리</b>(0 뭍 · 1 해안에 붙은 물 · … · 15 먼바다). 2500x1250 바이트다.
    /// </summary>
    public void SetSeaDepth(byte[] depth)
    {
        if (depth.Length != WorldMapRenderer.UnfoldedW * WorldMapRenderer.CellH) return;
        var old = _seaSrv;
        _seaSrv = CreateImmutable(depth, WorldMapRenderer.UnfoldedW, WorldMapRenderer.CellH, Format.R8_UInt, sizeof(byte));
        old?.Dispose();
        _seaReady = true;
    }

    /// <summary>깊이 표를 이미 올렸는지.</summary>
    public bool SeaDepthReady => _seaReady;

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
        // 깊이 표는 켤 때 짓는다 — 그때까지는 모두 뭍(0)인 한 칸짜리를 걸어 둔다.
        _seaSrv = CreateImmutable(new byte[] { 0 }, 1, 1, Format.R8_UInt, sizeof(byte));
        _tileKindSrv = CreateImmutable(new byte[] { 0 }, 1, 1, Format.R8_UInt, sizeof(byte));
        _palWaterSrv = CreateImmutable(new byte[256], 256, 1, Format.R8_UInt, sizeof(byte));
        _cityTexSrv = CreateImmutable(new uint[1], 1, 1, Format.B8G8R8A8_UNorm, sizeof(uint));
        _cityHiSrv = CreateImmutable(new uint[1], 1, 1, Format.B8G8R8A8_UNorm, sizeof(uint));
        _spriteHiSrv = CreateImmutable(new float[4], 1, 1, Format.R32G32B32A32_Float, sizeof(float) * 4);
        _shipHiSrv = CreateImmutable(new uint[1], 1, 1, Format.B8G8R8A8_UNorm, sizeof(uint));
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
        _cloudSoftSrv = CreateImmutable(new uint[1], 1, 1, Format.B8G8R8A8_UNorm, sizeof(uint));
        _folkSrv = CreateImmutable(new uint[1], 1, 1, Format.B8G8R8A8_UNorm, sizeof(uint));
        _linear = _device.CreateSamplerState(new SamplerDescription
        {
            Filter = Filter.MinMagMipLinear,
            AddressU = TextureAddressMode.Clamp,
            AddressV = TextureAddressMode.Clamp,
            AddressW = TextureAddressMode.Clamp,
            ComparisonFunc = ComparisonFunction.Never,
            MaxLOD = float.MaxValue,
        });
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
        var oldSoft = _cloudSoftSrv;
        _cloudSoftSrv = CreateImmutable(SoftenClouds(bgra), CloudSprites.Width, CloudSprites.AtlasHeight,
                                        Format.B8G8R8A8_UNorm, sizeof(uint));
        oldSoft?.Dispose();
        _cloudsReady = true;
    }

    /// <summary>
    /// 바둑판 반투명을 참 투명도로 푼다 — 장마다 1-2-1 천막 거르개(3x3)로 덮인 정도와 색을 섞는다.
    /// 한 점 걸러 찍힌 자리는 꼭 반(0.5)이 되고 가장자리는 부드럽게 잦아든다. 색은 <b>알파를 곱해 둔</b> 꼴로 담아
    /// 선형 보간해도 가장자리가 까매지지 않게 한다. 장 경계는 넘지 않는다.
    /// </summary>
    private static uint[] SoftenClouds(ReadOnlySpan<uint> bgra)
    {
        const int W = CloudSprites.Width, H = CloudSprites.Height;
        var src = bgra.ToArray();
        var outp = new uint[src.Length];
        for (int f = 0; f < CloudSprites.FrameCount; f++)
        {
            int baseRow = f * H;
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    double a = 0, r = 0, g = 0, b = 0;
                    for (int dy = -1; dy <= 1; dy++)
                        for (int dx = -1; dx <= 1; dx++)
                        {
                            int sx = x + dx, sy = y + dy;
                            if (sx < 0 || sx >= W || sy < 0 || sy >= H) continue;
                            uint c = src[(baseRow + sy) * W + sx];
                            if ((c >> 24) == 0) continue;
                            double w = (dx == 0 ? 2 : 1) * (dy == 0 ? 2 : 1) / 16.0;
                            a += w;
                            r += w * ((c >> 16) & 0xFF);
                            g += w * ((c >> 8) & 0xFF);
                            b += w * (c & 0xFF);
                        }
                    outp[(baseRow + y) * W + x] =
                        ((uint)Math.Round(Math.Min(1, a) * 255) << 24) | ((uint)Math.Round(r) << 16)
                        | ((uint)Math.Round(g) << 8) | (uint)Math.Round(b);
                }
        }
        return outp;
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
        // 도시 분리면 모든 도시 칸을 바탕으로 깔고, 선 도시 칸에는 「몇 번 도시의 어느 칸」을 적는다.
        var cityCells = new ushort[w * h];
        if (CitySprites && CityArtReady)
            for (int id = 0; id < _cityBlocks.Length; id++)
            {
                var (x0, y0, block, side) = _cityBlocks[id];
                if (side <= 0 || block.Length != side * side) continue;
                bool shown = !_hiddenCities.Contains(id);
                for (int k = 0; k < block.Length; k++)
                {
                    if (block[k] == CityExeTable.Keep) continue;
                    int dx = k % side, dy = k / side;
                    int cx = ((x0 + dx) % w + w) % w, cy = y0 + dy;
                    if (cy < 0 || cy >= h) continue;
                    cells[cy * w + cx] = (ushort)(block[k] & OceanTiles.TileMask);
                    if (shown) cityCells[cy * w + cx] = (ushort)((id + 1) | (dx << 9) | (dy << 11));
                }
            }
        var oldCityMap = _cityMapSrv;
        _cityMapSrv = CreateImmutable(cityCells, w, h, Format.R16_UInt, sizeof(ushort));
        oldCityMap?.Dispose();

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
            SeaOn = SeaEffect && _seaReady ? 1 : 0,
            SeaTime = SeaTime,
            CloudSmooth = SmoothClouds ? 1 : 0,
            HiSea = HiResSea && WaterPaletteReady ? 1 : 0,
            PixelOn = PixelFilter ? 1 : 0,
            LandOn = LandDetail && _tileKindsReady ? 1 : 0,
            CityOn = CitySprites && CityArtReady ? 1 : 0,
            SeaBright = SeaBrightness,
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
            FlowX = FlowDrift.X,
            FlowY = FlowDrift.Y,
            FlowStrength = FlowDrift.Strength * FlowAmount,
            FlowTick = FlowDrift.Tick,
            FlowDirX = FlowDrift.DirX,
            FlowDirY = FlowDrift.DirY,
            ShipFxOn = ShipFx ? 1 : 0,
            ShipHiSide = _shipHiSide,
            ShipBobV = ShipFx ? ShipBob : 0,
            ArrowOn = ShowArrows ? 1 : 0,
            ArrowGrid = WindTable.CellRaw / OceanTiles.TileW,   // 800 원본단위 = 지도 50칸
            ArrowCols = WindTable.Cols,
            ArrowRows = WindTable.Rows,
        };
        for (int i = 0; i < _clouds.Length; i++) cb.Clouds[i] = _clouds[i];
        for (int i = 0; i < _folk.Length; i++) cb.Folk[i] = _folk[i];
        for (int i = 0; i < _route.Length; i++) cb.Route[i] = _route[i];
        for (int i = 0; i < _wake.Length; i++) cb.Wake[i] = _wake[i];

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
                                      _nextSrv, _flowSrv, _arrowSrv, _cloudSrv, _folkSrv, _seaSrv, _cloudSoftSrv, _palWaterSrv, _tileKindSrv,
                                      _cityMapSrv, _cityTexSrv, _cityHiSrv, _spriteHiSrv, _shipHiSrv]);
        _ctx.PSSetSampler(0, _linear);
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
        _seaSrv?.Dispose();
        _cloudSoftSrv?.Dispose();
        _palWaterSrv?.Dispose();
        _cityMapSrv?.Dispose();
        _cityTexSrv?.Dispose();
        _cityHiSrv?.Dispose();
        _spriteHiSrv?.Dispose();
        _shipHiSrv?.Dispose();
        _tileKindSrv?.Dispose();
        _linear?.Dispose();
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
