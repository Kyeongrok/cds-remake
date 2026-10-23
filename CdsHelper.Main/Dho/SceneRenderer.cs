// DHO 저장소(C:\Users\ocean\git\dho, src/Dho/Render)에서 옮겨 온 것이다 — 편집기 「DHO 배 찍기」의 3D 미리보기가 쓴다. 고칠 때는 그쪽과 맞춘다.
using System.IO;
using System.Numerics;
using Vortice.Direct3D11;
using Vortice.DXGI;

namespace Dho.Render;

/// <summary>하늘·바다·물체를 그리는 셰이더 세 벌과, 바다 격자.</summary>
internal sealed class SceneRenderer : IDisposable
{
    // 셰이더 글 안에는 한글 주석을 넣지 않는다 — 컴파일러가 멀티바이트 글자 뒤의 줄바꿈을 먹는다.
    private const string Common = """
        cbuffer Frame : register(b0)
        {
            row_major float4x4 ViewProjection;
            row_major float4x4 InverseViewProjection;
            float3 CameraPosition; float Time;
            float3 SunDirection; float Night;
            float3 SunColor; float FogDensity;
            float3 Ambient; float SunAngle;
            float3 HorizonColor; float Pad1;
            float3 ZenithColor; float Pad2;
            float3 WaterColor; float Pad3;
            float2 WorldOffset; float2 ShipPosition;
            float2 ShipDirection; float ShipSpeed; float WaveScale;
            float WakeStyle; float MateCount; float2 PadWake;
            float4 MateA; float4 MateB;
        };
        cbuffer Object : register(b1)
        {
            row_major float4x4 World;
            float4 Tint;
            float4 Params;
        };

        float3 SkyColor(float3 dir)
        {
            float t = pow(saturate(dir.y), 0.45);
            return lerp(HorizonColor, ZenithColor, t);
        }
        float3 ApplyFog(float3 color, float3 position)
        {
            float d = length(position - CameraPosition);
            float f = 1.0 - exp(-d * FogDensity);
            return lerp(color, HorizonColor, saturate(f));
        }
        float Hash(float2 p)
        {
            p = frac(p * float2(123.34, 456.21));
            p += dot(p, p + 45.32);
            return frac(p.x * p.y);
        }
        float Noise(float2 p)
        {
            float2 i = floor(p), f = frac(p);
            f = f * f * (3.0 - 2.0 * f);
            return lerp(lerp(Hash(i), Hash(i + float2(1, 0)), f.x),
                        lerp(Hash(i + float2(0, 1)), Hash(i + float2(1, 1)), f.x), f.y);
        }
        """;

    private const string MeshShader = Common + """
        Texture2D Diffuse : register(t0);
        SamplerState Wrap : register(s0);
        struct VSIn { float3 pos : POSITION; float3 normal : NORMAL; float4 color : COLOR; float2 uv : TEXCOORD; };
        struct VSOut { float4 pos : SV_Position; float3 world : TEXCOORD0; float3 normal : TEXCOORD1; float4 color : COLOR; float2 uv : TEXCOORD2; };
        VSOut VS(VSIn i)
        {
            VSOut o;
            float4 world = mul(float4(i.pos, 1), World);
            o.world = world.xyz;
            o.pos = mul(world, ViewProjection);
            o.normal = mul(float4(i.normal, 0), World).xyz;
            o.color = i.color * Tint;
            o.uv = i.uv;
            return o;
        }
        float4 PS(VSOut i) : SV_Target
        {
            // emblem on a sail: only the 0..1 square of its own UV set carries the picture
            if (Params.w > 1.5 && Params.w < 2.5) clip(min(min(i.uv.x, 1.0 - i.uv.x), min(i.uv.y, 1.0 - i.uv.y)));
            float4 base = i.color * Diffuse.Sample(Wrap, i.uv);
            // wrinkle on a sail: the picture itself, multiplied onto what is already drawn (no lighting of its own)
            if (Params.w > 2.5) return float4(lerp(float3(1, 1, 1), base.rgb, 0.6), 1);
            if (Tint.a < 1.5) clip(base.a - (Params.y > 0.5 ? 0.02 : 0.35));
            float3 n = normalize(i.normal);
            float3 v = normalize(CameraPosition - i.world);
            if (dot(n, v) < 0) n = -n;
            float diffuse = saturate(dot(n, SunDirection)) * 0.85 + 0.15 * saturate(n.y * 0.5 + 0.5);
            float3 lit = base.rgb * (Ambient + SunColor * diffuse);
            if (Params.x > 0.5)
            {
                float3 x = base.rgb * 1.55;
                float3 over = max(x - 0.6, 0.0);
                lit = (min(x, 0.6) + over / (1.0 + over * 2.5)) * clamp(Ambient.g + SunColor.g * 0.55, 0.66, 0.84);
            }
            // figure: brightness only, no sky tint
            if (Params.z > 0.5)
                lit = base.rgb * (0.62 + 0.55 * diffuse) * clamp(Ambient.g + SunColor.g * 0.55, 0.66, 0.84);
            // sail cloth: evenly lit on both faces, only a little shading left; takes the colour of the light (warm at dusk, blue at night)
            if (Params.w > 0.5)
                lit = base.rgb * (0.84 + 0.22 * diffuse) * min(Ambient * 0.9 + SunColor * 0.62, 1.08);
            return float4(ApplyFog(lit, i.world), base.a);
        }
        """;

    private const string SkyShader = Common + """
        struct VSOut { float4 pos : SV_Position; float2 ndc : TEXCOORD0; };
        VSOut VS(uint id : SV_VertexID)
        {
            VSOut o;
            float2 uv = float2((id << 1) & 2, id & 2);
            o.ndc = uv * float2(2, -2) + float2(-1, 1);
            o.pos = float4(o.ndc, 1, 1);
            return o;
        }
        float4 PS(VSOut i) : SV_Target
        {
            float4 farPoint = mul(float4(i.ndc, 1, 1), InverseViewProjection);
            float3 dir = normalize(farPoint.xyz / farPoint.w - CameraPosition);
            float3 color = SkyColor(dir);

            // the sun disc follows the real sun (SunDirection is the light, which is clamped above the horizon and becomes moonlight at night)
            float3 trueSun = normalize(float3(cos(SunAngle) * 0.8, sin(SunAngle), 0.45));
            float sunUp = saturate(trueSun.y * 8.0 + 1.0);      // fades out once it is below the horizon
            float sun = saturate(dot(dir, trueSun));
            float3 sunTint = lerp(float3(1.0, 0.55, 0.30), float3(1.0, 0.96, 0.88), saturate(trueSun.y * 3.0));
            color += sunTint * (pow(sun, 1800.0) * 1.6 + pow(sun, 60.0) * 0.10) * sunUp;      // a smaller disc and a tighter glow
            // after screenshots of the original: no red ring - a wide soft white haze around the sun, and a large faint halo once it is up (numbers made up)
            float low = saturate((sunTint.r - sunTint.b) * 2.5) * sunUp;
            float3 hazeTint = lerp(float3(1.0, 0.97, 0.92), float3(1.0, 0.90, 0.78), low);
            color += hazeTint * (pow(sun, 10.0) * 0.20 + pow(sun, 4.0) * 0.07) * sunUp;
            float away = acos(min(sun, 0.9999));
            float halo = smoothstep(0.05, 0.0, abs(away - 0.24)) * saturate(trueSun.y * 4.0);
            color += float3(1.0, 1.0, 1.0) * halo * 0.07 * sunUp;

            float2 sphere = float2(atan2(dir.z, dir.x), asin(clamp(dir.y, -1, 1))) * 110.0;
            float2 cell = floor(sphere);
            float star = Hash(cell);
            float2 offset = frac(sphere) - 0.5 - (float2(Hash(cell + 7.1), Hash(cell + 3.7)) - 0.5) * 0.6;
            float twinkle = 0.75 + 0.25 * sin(Time * 2.0 + star * 60.0);
            float point_ = smoothstep(0.12, 0.0, length(offset)) * step(0.965, star) * twinkle;
            color += point_ * Night * saturate(dir.y * 4.0 + 0.2);

            // moon at night: a small pale disc opposite the sun, a little past half (gibbous), with faint mottling
            float3 moonDir = normalize(float3(-cos(SunAngle) * 0.8, -sin(SunAngle) * 0.5, -0.45));      // opposite the sun: rises at dusk, about 48 degrees up at midnight, sets at dawn
            float3 mx = normalize(cross(float3(0, 1, 0), moonDir));
            float3 my = cross(moonDir, mx);
            float2 mp = float2(dot(dir, mx), dot(dir, my)) / 0.017;
            if (dot(dir, moonDir) > 0.0 && length(mp) < 1.3)
            {
                float disc = smoothstep(1.0, 0.92, length(mp));
                float shade = smoothstep(0.95, 1.05, length(mp - float2(1.35, 0.55)));      // the dark side eats one edge
                float mottle = 0.82 + 0.18 * Noise(mp * 2.5 + 3.0);
                float lit = disc * shade;
                float up = saturate(moonDir.y * 12.0 + 0.3);      // dims as it sinks to the horizon
                lit *= up;
                color = lerp(color, float3(0.92, 0.90, 0.80) * mottle, lit * Night);
                color += float3(0.25, 0.27, 0.32) * smoothstep(1.3, 0.9, length(mp)) * (1.0 - lit) * Night * 0.25;
            }

            if (dir.y > 0.02)
            {
                float2 uv = dir.xz / (dir.y + 0.15) * 1.4 + Time * 0.004;
                float cloud = Noise(uv) * 0.6 + Noise(uv * 2.3) * 0.3 + Noise(uv * 5.1) * 0.1;
                cloud = smoothstep(0.55, 0.85, cloud) * saturate(dir.y * 3.0);
                color = lerp(color, color + (Ambient + SunColor * 0.6) * 0.5, cloud * 0.35);
            }
            return float4(color, 1);
        }
        """;

    private const string OceanShader = Common + """
        Texture3D Waves : register(t1);
        SamplerState Wrap : register(s0);
        struct VSOut { float4 pos : SV_Position; float3 world : TEXCOORD0; };
        static const float K = 95.0;

        float2 WaveSlope(float2 uv, float frame)
        {
            float3 s = Waves.Sample(Wrap, float3(uv, frame)).rgb;
            return (s.rb - 0.502) * 2.0;
        }

        float Height(float2 p, float t)
        {
            float h = 0;
            h += sin(dot(p, float2(0.031, 0.017)) + t * 0.9) * 0.9;
            h += sin(dot(p, float2(-0.019, 0.043)) + t * 1.3) * 0.5;
            h += sin(dot(p, float2(0.071, -0.052)) + t * 1.9) * 0.22;
            return h;
        }
        VSOut VS(float3 pos : POSITION)
        {
            VSOut o;
            float3 world = float3(pos.x + CameraPosition.x, 0, pos.z + CameraPosition.z);
            float fade = saturate(1.0 - length(pos.xz) / (2500.0 * K));
            world.y = Height((world.xz + WorldOffset) / K, Time) * fade * K * 0.4 * WaveScale;
            o.world = world;
            o.pos = mul(float4(world, 1), ViewProjection);
            return o;
        }
        float4 PS(VSOut i) : SV_Target
        {
            float2 p = (i.world.xz + WorldOffset) / K;
            float dist = length(i.world - CameraPosition) / K;

            float e = 0.6;
            float h0 = Height(p, Time);
            float hx = Height(p + float2(e, 0), Time);
            float hz = Height(p + float2(0, e), Time);
            float2 slope = float2(hx - h0, hz - h0) / e;
            float frame = Time / 5.0;
            // layers are turned off the grid so the 64 x 64 tile does not line up in rows
            // ShipPosition carries the extra drift of the water surface (grows along the heading with speed): the ripples and the foam
            // slide astern faster than the map moves, so a fast ship looks fast
            float2 pf = p + ShipPosition;
            float2 ripple = WaveSlope(float2(pf.x * 0.94 - pf.y * 0.34, pf.x * 0.34 + pf.y * 0.94) / 30.0, frame) * 1.0;
            ripple += WaveSlope(float2(pf.x * 0.6 + pf.y * 0.8, pf.y * 0.6 - pf.x * 0.8) / 97.0 + 0.37, frame * 0.61 + 0.5) * 1.1;
            float near = saturate(1.0 - dist / 420.0);
            ripple += WaveSlope(pf / 11.0 + 0.11, frame * 1.7) * 0.7 * near;
            float calm = saturate(1.0 - dist / 3500.0);
            // rougher seas: steeper swell and stronger ripples
            float rough = lerp(0.75, 1.0, saturate(WaveScale)) + max(WaveScale - 1.0, 0.0) * 0.55;
            float3 n = normalize(float3(-(slope.x * 0.6 * WaveScale + ripple.x * rough) * calm, 1.0, -(slope.y * 0.6 * WaveScale + ripple.y * rough) * calm));

            float3 v = normalize(CameraPosition - i.world);
            float fresnel = 0.03 + 0.97 * pow(1.0 - saturate(dot(n, v)), 5.0);
            float3 reflected = SkyColor(reflect(-v, n));
            float facing = saturate(dot(n, SunDirection));
            float crest = saturate((ripple.x + ripple.y) * 0.9 + 0.1) * calm;
            float3 body = WaterColor * (0.78 + 0.22 * facing) + (WaterColor * 0.9 + HorizonColor * 0.10) * crest * 0.22;
            float3 color = lerp(body, reflected, fresnel * 0.55);
            // far water melts into the haze of the horizon
            float haze = pow(1.0 - saturate(v.y), 7.0);
            color = lerp(color, HorizonColor, haze * 0.8);

            float3 halfway = normalize(v + SunDirection);
            color += SunColor * pow(saturate(dot(n, halfway)), 140.0) * 0.10;
            // a bright band on the water under the sun, strongest when the sun is low (after screenshots of the original; numbers made up)
            float3 trueSun = normalize(float3(cos(SunAngle) * 0.8, sin(SunAngle), 0.45));
            float2 sunFlat = normalize(trueSun.xz);
            float towards = saturate(dot(normalize(-v.xz + float2(0.0001, 0.0)), sunFlat));
            float sunLow = saturate(trueSun.y * 8.0 + 0.2) * saturate(1.25 - trueSun.y * 1.6);
            float grazing = pow(1.0 - saturate(v.y), 3.0);
            float band = pow(towards, 60.0) * (0.5 + 0.5 * grazing) * sunLow;
            color += lerp(float3(1.0, 0.86, 0.66), float3(1.0, 0.97, 0.90), saturate(trueSun.y * 3.0)) * band * (0.55 + 0.45 * crest) * 1.0;

            // the player's ship sits at the origin; the aide captains' ships (MateA, MateB) get the same wake.
            // mates are done first and the player's ship last, so along / across left behind serve the hull paint below
            float2 rel = i.world.xz / K;
            float along = 0.0, across = 0.0;
            for (int boat = 2; boat >= 0; boat--)
            {
            if (boat > 0 && float(boat) > MateCount + 0.5) continue;
            float2 org = boat == 0 ? float2(0.0, 0.0) : (boat == 1 ? MateA.xy : MateB.xy) / K;
            float2 dir = boat == 0 ? ShipDirection : (boat == 1 ? MateA.zw : MateB.zw);
            rel = i.world.xz / K - org;
            along = dot(rel, dir);
            across = dot(rel, float2(-dir.y, dir.x));
            float hull = saturate(1.0 - length(float2(across / 16.0, along / 44.0)));
            float behind = saturate(-along / 420.0);
            // bow wave: two bands of foam that leave the bow and open into a V; faster = longer, wider, whiter (shape after the original's look, numbers made up)
            // WakeStyle picks one of the candidate looks (mod window): 0 V bands, 1 hull foam only, 2 long straight trail, 3 wide V with ripples inside,
            // 4 soft glow without foam, 5 bow spray and a short trail, 6 wide milky band astern (after the original). All shapes and numbers are made up.
            float st = WakeStyle;
            float wArms = 1.25, wSides = 1.3, wAstern = 1.0, wStreak = 0.6, openK = 1.0, reachK = 1.0, trailK = 1.0, glow = 0.0;
            if (st > 0.5 && st < 1.5) { wArms = 0.0; wSides = 0.9; wAstern = 0.0; wStreak = 0.0; }
            else if (st > 1.5 && st < 2.5) { wArms = 0.25; wSides = 1.0; wAstern = 1.7; wStreak = 0.25; reachK = 0.45; trailK = 2.6; }
            else if (st > 2.5 && st < 3.5) { wArms = 1.1; wSides = 1.0; wAstern = 0.35; wStreak = 0.0; openK = 2.1; reachK = 1.5; }
            else if (st > 3.5 && st < 4.5) { wArms = 0.0; wSides = 0.45; wAstern = 0.0; wStreak = 0.0; openK = 1.3; reachK = 1.2; glow = 1.0; }
            else if (st > 4.5 && st < 5.5) { wArms = 0.55; wSides = 1.2; wAstern = 0.9; wStreak = 0.35; reachK = 0.3; trailK = 0.45; }
            else if (st > 5.5) { wArms = 0.0; wSides = 0.7; wAstern = 0.0; wStreak = 0.0; }
            float back = 40.0 - along;
            float reach = (90.0 + 520.0 * ShipSpeed) * reachK;
            float armAt = 9.0 + back * (0.16 + 0.10 * ShipSpeed) * openK;
            float armWide = 5.0 + back * 0.11;
            float arms = saturate(1.0 - abs(abs(across) - armAt) / armWide) * step(0.0, back) * saturate(1.0 - back / reach);
            float behindT = saturate(-along / (420.0 * trailK));
            float astern = saturate(1.0 - abs(across) / (8.0 + behindT * 40.0)) * step(along, -30.0) * (1.0 - behindT);
            // style 3: transverse ripples between the arms; style 5: a burst of spray at the bow
            float inside = saturate(1.0 - abs(across) / max(armAt, 1.0)) * step(0.0, back) * saturate(1.0 - back / reach);
            float ripples = (st > 2.5 && st < 3.5) ? inside * (0.5 + 0.5 * sin(back * 0.22 - Time * 2.5)) * 0.4 : 0.0;
            float spray = (st > 4.5 && st < 5.5) ? saturate(1.0 - length(float2(across / 15.0, (along - 44.0) / 20.0))) * 1.6 : 0.0;
            // noise needs small coordinates: far from the world origin the hash loses precision and the foam smears into streaks
            float2 q = frac(pf / 512.0) * 512.0;
            float drift = frac(Time / 640.0) * 640.0;
            float foamNoise = Noise(q * 0.22 + drift * 0.4) * 0.6 + Noise(q * 0.7 - drift * 0.8) * 0.4;
            // water cut by the hull: a narrow band of foam hugging the waterline along the ship's length
            float sides = saturate(1.0 - abs(abs(across) - 9.0) / 5.0) * saturate(1.0 - abs(along) / 48.0);
            float foam = saturate(hull * 0.5 * (0.3 + ShipSpeed) + (sides * wSides + arms * wArms + astern * wAstern + ripples + spray) * saturate(ShipSpeed * 2.5)) * smoothstep(0.26, 0.66, foamNoise + hull * 0.3 + arms * 0.08);
            color = lerp(color, (Ambient + SunColor) * 0.9, saturate(foam) * 0.85);
            // style 6: after a screenshot of the original - one wide, long, milky band of foam astern that widens slowly and fades (numbers made up)
            if (st > 5.5)
            {
                float mistT = saturate(-along / (700.0 + 900.0 * ShipSpeed));
                float mistWide = 12.0 + mistT * 46.0;
                float mist = smoothstep(1.0, 0.3, abs(across) / mistWide) * step(along, 20.0) * pow(1.0 - mistT, 1.3);
                float grain = Noise(q * 0.5 - drift * 0.6) * 0.5 + foamNoise * 0.5;
                color = lerp(color, (Ambient + SunColor) * 0.85, saturate(mist * (0.3 + 0.75 * grain) * saturate(ShipSpeed * 2.5)) * 0.8);
            }
            // speed lines: thin pale-blue streaks running astern beside the hull once the ship is fast (after the original's look; numbers made up)
            float streakNoise = Noise(float2(across * 1.5, (along + dot(ShipPosition, ShipDirection)) * 0.012));
            float streak = smoothstep(0.66, 0.8, streakNoise) * saturate(1.0 - abs(abs(across) - 20.0) / 15.0) * saturate(1.0 - abs(along + 50.0) / 150.0) * saturate((ShipSpeed - 0.5) * 2.5);
            color += float3(0.55, 0.75, 1.0) * (Ambient + SunColor) * streak * wStreak;
            // style 4: no foam, the V and the trail only lighten the water
            color += (Ambient + SunColor) * glow * 0.16 * saturate(arms + astern * 0.7) * saturate(ShipSpeed * 2.5);
            }

            // hull effect paint: light shimmering on the water around the ship (Pad1..3 = colour, 0 = none)
            float3 aura = float3(Pad1, Pad2, Pad3);
            if (dot(aura, aura) > 0.0001)
            {
                float ring = length(float2(across / 30.0, along / 62.0));
                float glow = smoothstep(1.35, 0.55, ring);
                float2 w = float2(across, along) * 0.16;
                float wisp = Noise(w + float2(0.0, frac(Time / 400.0) * 400.0 * 1.3)) * 0.6 + Noise(w * 2.3 - float2(frac(Time / 400.0) * 400.0 * 0.9, 0.0)) * 0.4;
                float pulse = 0.75 + 0.25 * sin(Time * 2.1 + ring * 6.0);
                color += aura * glow * (0.25 + 1.1 * wisp * wisp) * pulse;
            }

            return float4(ApplyFog(color, i.world), 1);
        }
        """;

    private readonly Gfx _gfx;
    private readonly ID3D11VertexShader _meshVs, _skyVs, _oceanVs;
    private readonly ID3D11PixelShader _meshPs, _skyPs, _oceanPs;
    private readonly ID3D11InputLayout _meshLayout, _oceanLayout;
    private readonly ID3D11Buffer _oceanVertices, _oceanIndices;
    private readonly int _oceanIndexCount;
    private readonly ID3D11ShaderResourceView _white;
    private readonly ID3D11ShaderResourceView _waves;

    public SceneRenderer(Gfx gfx)
    {
        _gfx = gfx;
        ReadOnlyMemory<byte> code;
        (_meshVs, _meshPs, code) = gfx.Compile(MeshShader, "mesh.hlsl");
        _meshLayout = gfx.Device.CreateInputLayout(Vertex.Layout, code.Span);
        (_skyVs, _skyPs, _) = gfx.Compile(SkyShader, "sky.hlsl");
        (_oceanVs, _oceanPs, code) = gfx.Compile(OceanShader, "ocean.hlsl");
        _oceanLayout = gfx.Device.CreateInputLayout(
            [new InputElementDescription("POSITION", 0, Format.R32G32B32_Float, 0, 0)], code.Span);

        // 바다 격자 — 가운데는 촘촘하고 수평선 쪽은 성기다
        const int n = 160;
        const float reach = 2600000f;
        var vertices = new Vector3[(n + 1) * (n + 1)];
        for (int z = 0; z <= n; z++)
        for (int x = 0; x <= n; x++)
        {
            float u = x / (float)n * 2 - 1, v = z / (float)n * 2 - 1;
            vertices[z * (n + 1) + x] = new Vector3(Spread(u) * reach, 0, Spread(v) * reach);
        }
        var indices = new uint[n * n * 6];
        int at = 0;
        for (int z = 0; z < n; z++)
        for (int x = 0; x < n; x++)
        {
            uint a = (uint)(z * (n + 1) + x), b = a + 1, c = a + (uint)n + 1, d = c + 1;
            indices[at++] = a; indices[at++] = c; indices[at++] = b;
            indices[at++] = b; indices[at++] = c; indices[at++] = d;
        }
        _oceanVertices = gfx.Device.CreateBuffer<Vector3>(vertices, BindFlags.VertexBuffer);
        _oceanIndices = gfx.Device.CreateBuffer<uint>(indices, BindFlags.IndexBuffer);
        _oceanIndexCount = indices.Length;

        _white = Texture.Solid(gfx, 0xFFFFFFFF);
        _waves = Texture.Waves(gfx);

        static float Spread(float t) => MathF.Sign(t) * MathF.Pow(MathF.Abs(t), 3f);
    }

    public void DrawSky()
    {
        var ctx = _gfx.Context;
        _gfx.Background();
        ctx.IASetInputLayout(null);
        ctx.VSSetShader(_skyVs);
        ctx.PSSetShader(_skyPs);
        ctx.Draw(3, 0);
    }

    public void DrawOcean()
    {
        var ctx = _gfx.Context;
        _gfx.Opaque();
        ctx.IASetInputLayout(_oceanLayout);
        ctx.VSSetShader(_oceanVs);
        ctx.PSSetShader(_oceanPs);
        ctx.PSSetShaderResource(1, _waves);
        ctx.IASetVertexBuffer(0, _oceanVertices, 12);
        ctx.IASetIndexBuffer(_oceanIndices, Format.R32_UInt, 0);
        ctx.DrawIndexed((uint)_oceanIndexCount, 0, 0);
    }

    /// <summary>이 뒤로 <see cref="Draw"/> 로 물체를 그린다.</summary>
    public void BeginMeshes()
    {
        var ctx = _gfx.Context;
        _gfx.Opaque();
        ctx.IASetInputLayout(_meshLayout);
        ctx.VSSetShader(_meshVs);
        ctx.PSSetShader(_meshPs);
    }

    // 곱해 그리기 / 다시 불투명으로 — 돛의 주름처럼 이미 그린 것 위에 그림을 곱할 때
    public void Multiply() => _gfx.Multiply();
    public void Opaque() => _gfx.Opaque();
    public void Translucent() => _gfx.Translucent();

    public void Draw(Mesh mesh, in Matrix4x4 world, Vector4? tint = null, ID3D11ShaderResourceView? texture = null, bool baked = false, bool soft = false, bool figure = false, bool cloth = false, bool emblem = false, bool multiply = false)
    {
        _gfx.SetObject(world, tint ?? Vector4.One, baked, soft, figure, cloth, emblem, multiply);
        _gfx.Context.PSSetShaderResource(0, texture ?? _white);
        mesh.Draw(_gfx);
    }

    public void Dispose()
    {
        _white.Dispose();
        _waves.Dispose();
        _oceanIndices.Dispose();
        _oceanVertices.Dispose();
        _oceanLayout.Dispose();
        _meshLayout.Dispose();
        _oceanPs.Dispose(); _oceanVs.Dispose();
        _skyPs.Dispose(); _skyVs.Dispose();
        _meshPs.Dispose(); _meshVs.Dispose();
    }
}

internal static unsafe class Texture
{
    /// <summary>한 가지 색(0xAARRGGBB)의 1x1 텍스처.</summary>
    public static ID3D11ShaderResourceView Solid(Gfx gfx, uint argb) => FromBgra(gfx, 1, 1, [argb]);

    /// <summary>
    /// 원본 물결 — <c>0001\oc0000.bin</c>(MFTF 안 XFTX)의 64 × 64 그림 150장. 빨강·파랑에 물결의 기울기(128 이 평평)가 들어 있다.
    /// 장을 깊이로 쌓은 3D 텍스처로 올려, 깊이를 따라가며 읽으면 장 사이가 저절로 이어진다. 파일이 없으면 평평한 한 장.
    /// </summary>
    public static ID3D11ShaderResourceView Waves(Gfx gfx)
    {
        int size = 1, frames = 1;
        byte[] pixels = [128, 255, 128, 255];
        try
        {
            var data = Dho.Data.GvoFiles.Read(@"0001\oc0000.bin");
            int I32(int at) => System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(at));
            int xftx = I32(24), count = I32(xftx + 16);
            int first = xftx + I32(xftx + 20);
            int width = System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(first));
            if (I32(first + 4) == 21 && width > 0 && count > 0)
            {
                var all = new byte[width * width * 4 * count];
                for (int i = 0; i < count; i++)
                {
                    int record = xftx + I32(xftx + 20 + i * 4);
                    Array.Copy(data, record + I32(record + 12), all, i * width * width * 4, width * width * 4);
                }
                (size, frames, pixels) = (width, count, all);
            }
        }
        catch (Exception) { }
        fixed (byte* bytes = pixels)
        {
            using var texture = gfx.Device.CreateTexture3D(new Texture3DDescription
            {
                Width = (uint)size,
                Height = (uint)size,
                Depth = (uint)frames,
                MipLevels = 1,
                Format = Format.B8G8R8A8_UNorm,
                BindFlags = BindFlags.ShaderResource,
            }, [new SubresourceData(bytes, (uint)(size * 4), (uint)(size * size * 4))]);
            return gfx.Device.CreateShaderResourceView(texture);
        }
    }

    /// <summary>BGRA 화소(0xAARRGGBB)로 텍스처를 만든다.</summary>
    public static ID3D11ShaderResourceView FromBgra(Gfx gfx, int width, int height, ReadOnlySpan<uint> pixels)
    {
        fixed (uint* data = pixels)
        {
            using var texture = gfx.Device.CreateTexture2D(new Texture2DDescription
            {
                Width = (uint)width,
                Height = (uint)height,
                MipLevels = 1,
                ArraySize = 1,
                Format = Format.B8G8R8A8_UNorm,
                SampleDescription = new SampleDescription(1, 0),
                BindFlags = BindFlags.ShaderResource,
            }, new SubresourceData(data, (uint)(width * 4)));
            return gfx.Device.CreateShaderResourceView(texture);
        }
    }
}
