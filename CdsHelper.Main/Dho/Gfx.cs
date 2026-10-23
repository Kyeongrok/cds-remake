// DHO 저장소(C:\Users\ocean\git\dho, src/Dho/Render)에서 옮겨 온 것이다 — 편집기 「DHO 배 찍기」의 3D 미리보기가 쓴다. 고칠 때는 그쪽과 맞춘다.
using System.IO;
using System.Numerics;
using System.Runtime.InteropServices;
using Vortice.D3DCompiler;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DirectWrite;
using Vortice.DXGI;
using D2 = Vortice.Direct2D1;
using Vortice.Mathematics;

namespace Dho.Render;

/// <summary>프레임마다 한 번 올리는 값. HLSL 의 <c>cbuffer Frame</c> 와 짝.</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct FrameConstants
{
    public Matrix4x4 ViewProjection;
    public Matrix4x4 InverseViewProjection;
    public Vector3 CameraPosition; public float Time;
    public Vector3 SunDirection; public float Night;
    public Vector3 SunColor; public float FogDensity;
    public Vector3 Ambient; public float SunAngle;      // 해의 각(라디안) — 하늘 셰이더가 달의 자리를 여기서 셈한다
    public Vector3 HorizonColor; public float Pad1;
    public Vector3 ZenithColor; public float Pad2;
    public Vector3 WaterColor; public float Pad3;
    public Vector2 WorldOffset; public Vector2 ShipPosition;
    public Vector2 ShipDirection; public float ShipSpeed; public float WaveScale;
    public float WakeStyle; public float MateCount; public Vector2 PadWake;      // MateCount: 물살을 함께 그릴 부관 선장의 배 수(0 ~ 2)
    public Vector4 MateA; public Vector4 MateB;      // 그 배들 — xy 내 배에서 본 자리(세계 단위) · zw 뱃머리 방향      // 물살 모양(모드 창에서 고른 번호) — 바다 셰이더가 읽는다
}

/// <summary>그리는 물체마다 올리는 값. HLSL 의 <c>cbuffer Object</c> 와 짝.</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct ObjectConstants
{
    public Matrix4x4 World;
    public Vector4 Tint;
    /// <summary>x = 1 이면 정점 색에 빛이 구워져 있어 다시 비추지 않는다.</summary>
    public Vector4 Params;
}

/// <summary>D3D11 장치·스왑체인·깊이 버퍼와, 그 위에 글과 창을 그릴 Direct2D 를 쥔다.</summary>
internal sealed unsafe class Gfx : IDisposable
{
    public ID3D11Device Device { get; }
    public ID3D11DeviceContext Context { get; }
    public D2.ID2D1DeviceContext D2D { get; }
    public IDWriteFactory DWrite { get; }
    public int Width { get; private set; }

    /// <summary>프레임을 시작할 때 칠하는 바탕(편집기에서 더함 — DHO 는 검정 고정).</summary>
    public Color4 ClearColor { get; set; } = new(0, 0, 0, 1);
    public int Height { get; private set; }

    private readonly IDXGISwapChain1 _swapChain;
    private ID3D11RenderTargetView _backBuffer = null!;
    private ID3D11Texture2D _depthTexture = null!;
    private ID3D11DepthStencilView _depth = null!;
    private D2.ID2D1Bitmap1 _d2dTarget = null!;

    private readonly D2.ID2D1Factory1 _d2dFactory;
    private readonly D2.ID2D1Device _d2dDevice;

    private readonly ID3D11Buffer _frameBuffer, _objectBuffer;
    private readonly ID3D11SamplerState _wrapSampler;
    private readonly ID3D11BlendState _alphaBlend, _opaqueBlend, _multiplyBlend;
    private readonly ID3D11DepthStencilState _depthWrite, _depthRead, _depthNone;
    private readonly ID3D11RasterizerState _cullNone;

    public Gfx(IntPtr hwnd, int width, int height)
    {
        Width = width;
        Height = height;

        var levels = new[] { FeatureLevel.Level_11_0, FeatureLevel.Level_10_1, FeatureLevel.Level_10_0 };
        D3D11.D3D11CreateDevice(null, DriverType.Hardware, DeviceCreationFlags.BgraSupport, levels,
                                out ID3D11Device device, out ID3D11DeviceContext context).CheckError();
        Device = device;
        Context = context;

        using var dxgiDevice = Device.QueryInterface<IDXGIDevice>();
        using (var adapter = dxgiDevice.GetAdapter())
        using (var factory = adapter.GetParent<IDXGIFactory2>())
        {
            _swapChain = factory.CreateSwapChainForHwnd(Device, hwnd, new SwapChainDescription1
            {
                Width = (uint)width,
                Height = (uint)height,
                Format = Format.B8G8R8A8_UNorm,
                BufferCount = 2,
                BufferUsage = Usage.RenderTargetOutput,
                SampleDescription = new SampleDescription(1, 0),
                SwapEffect = SwapEffect.FlipDiscard,
                Scaling = Scaling.Stretch,
            });
        }

        _d2dFactory = D2.D2D1.D2D1CreateFactory<D2.ID2D1Factory1>();
        _d2dDevice = _d2dFactory.CreateDevice(dxgiDevice);
        D2D = _d2dDevice.CreateDeviceContext(D2.DeviceContextOptions.None);
        DWrite = Vortice.DirectWrite.DWrite.DWriteCreateFactory<IDWriteFactory>();

        _frameBuffer = Device.CreateBuffer(new BufferDescription((uint)sizeof(FrameConstants), BindFlags.ConstantBuffer));
        _objectBuffer = Device.CreateBuffer(new BufferDescription((uint)sizeof(ObjectConstants), BindFlags.ConstantBuffer));

        _wrapSampler = Device.CreateSamplerState(SamplerDescription.AnisotropicWrap);
        _alphaBlend = Device.CreateBlendState(BlendDescription.NonPremultiplied);
        _opaqueBlend = Device.CreateBlendState(BlendDescription.Opaque);
        // 결과 = 이미 그린 빛 × 그리는 빛. 알파 쪽에는 빛깔 인자를 못 쓰므로(만들 때 오류가 난다) 따로 적는다 — 알파는 그대로 둔다
        var multiply = new BlendDescription();
        multiply.RenderTarget[0] = new RenderTargetBlendDescription
        {
            BlendEnable = true, SourceBlend = Blend.Zero, DestinationBlend = Blend.SourceColor, BlendOperation = BlendOperation.Add,
            SourceBlendAlpha = Blend.Zero, DestinationBlendAlpha = Blend.One, BlendOperationAlpha = BlendOperation.Add,
            RenderTargetWriteMask = ColorWriteEnable.All,
        };
        _multiplyBlend = Device.CreateBlendState(multiply);
        _depthWrite = Device.CreateDepthStencilState(DepthStencilDescription.Default);
        _depthRead = Device.CreateDepthStencilState(DepthStencilDescription.DepthRead);
        _depthNone = Device.CreateDepthStencilState(DepthStencilDescription.None);
        _cullNone = Device.CreateRasterizerState(RasterizerDescription.CullNone);

        CreateTargets();
    }

    private void CreateTargets()
    {
        using (var back = _swapChain.GetBuffer<ID3D11Texture2D>(0))
            _backBuffer = Device.CreateRenderTargetView(back);

        _depthTexture = Device.CreateTexture2D(new Texture2DDescription
        {
            Width = (uint)Width,
            Height = (uint)Height,
            MipLevels = 1,
            ArraySize = 1,
            Format = Format.D32_Float,
            SampleDescription = new SampleDescription(1, 0),
            BindFlags = BindFlags.DepthStencil,
        });
        _depth = Device.CreateDepthStencilView(_depthTexture);

        using var surface = _swapChain.GetBuffer<IDXGISurface>(0);
        _d2dTarget = D2D.CreateBitmapFromDxgiSurface(surface, new D2.BitmapProperties1(
            new Vortice.DCommon.PixelFormat(Format.B8G8R8A8_UNorm, Vortice.DCommon.AlphaMode.Premultiplied),
            96, 96, D2.BitmapOptions.Target | D2.BitmapOptions.CannotDraw));
        D2D.Target = _d2dTarget;
    }

    public void Resize(int width, int height)
    {
        if (width <= 0 || height <= 0 || (width == Width && height == Height)) return;
        Width = width;
        Height = height;

        Context.OMSetRenderTargets((ID3D11RenderTargetView)null!);
        D2D.Target = null;
        _d2dTarget.Dispose();
        _backBuffer.Dispose();
        _depth.Dispose();
        _depthTexture.Dispose();
        _swapChain.ResizeBuffers(0, (uint)width, (uint)height, Format.Unknown, SwapChainFlags.None);
        CreateTargets();
    }

    // ── 한 프레임 ────────────────────────────────────────────────────────────

    public void Begin(in FrameConstants frame)
    {
        Context.OMSetRenderTargets(_backBuffer, _depth);
        Context.RSSetViewport(0, 0, Width, Height);
        Context.RSSetState(_cullNone);
        Context.ClearRenderTargetView(_backBuffer, ClearColor);
        Context.ClearDepthStencilView(_depth, DepthStencilClearFlags.Depth, 1f, 0);

        Context.UpdateSubresource(in frame, _frameBuffer);
        Context.VSSetConstantBuffer(0, _frameBuffer);
        Context.PSSetConstantBuffer(0, _frameBuffer);
        Context.VSSetConstantBuffer(1, _objectBuffer);
        Context.PSSetConstantBuffer(1, _objectBuffer);
        Context.PSSetSampler(0, _wrapSampler);
        Context.IASetPrimitiveTopology(PrimitiveTopology.TriangleList);
    }

    /// <summary>
    /// 화면 글을 다 그린 뒤, 창 안의 네모에 3D 를 한 번 더 그릴 채비 — 깊이만 비우고 그 네모를 뷰포트로 잡는다.
    /// 끝나면 <see cref="EndInset"/> 로 뷰포트를 되돌린다.
    /// </summary>
    public void BeginInset(in FrameConstants frame, float x, float y, float width, float height)
    {
        Context.OMSetRenderTargets(_backBuffer, _depth);
        Context.RSSetViewport(x, y, width, height);
        Context.RSSetState(_cullNone);
        Context.ClearDepthStencilView(_depth, DepthStencilClearFlags.Depth, 1f, 0);
        Context.UpdateSubresource(in frame, _frameBuffer);
        Context.VSSetConstantBuffer(0, _frameBuffer);
        Context.PSSetConstantBuffer(0, _frameBuffer);
        Context.VSSetConstantBuffer(1, _objectBuffer);
        Context.PSSetConstantBuffer(1, _objectBuffer);
        Context.PSSetSampler(0, _wrapSampler);
        Context.IASetPrimitiveTopology(PrimitiveTopology.TriangleList);
    }

    public void EndInset() => Context.RSSetViewport(0, 0, Width, Height);

    /// <param name="soft">알파로 섞어 그리는 면 — 거의 투명한 데만 잘라낸다.</param>
    /// <param name="cloth">돛 천 — 해를 등진 면도 어두워지지 않게 고르게 밝힌다.</param>
    public void SetObject(in Matrix4x4 world, Vector4 tint, bool baked = false, bool soft = false, bool figure = false, bool cloth = false, bool emblem = false, bool multiply = false)
    {
        // 넷째 칸: 1 돛 천, 2 돛 위의 문장(그림의 0 ~ 1 밖은 잘라낸다)
        var constants = new ObjectConstants { World = world, Tint = tint, Params = new Vector4(baked ? 1 : 0, soft ? 1 : 0, figure ? 1 : 0, multiply ? 3 : emblem ? 2 : cloth ? 1 : 0) };
        Context.UpdateSubresource(in constants, _objectBuffer);
    }

    /// <summary>불투명: 깊이를 쓰고 섞지 않는다.</summary>
    public void Opaque()
    {
        Context.OMSetBlendState(_opaqueBlend);
        Context.OMSetDepthStencilState(_depthWrite);
    }

    /// <summary>곱하기: 깊이는 읽기만 하고, 이미 그린 빛에 그리는 빛을 곱한다(돛의 주름).</summary>
    public void Multiply()
    {
        Context.OMSetBlendState(_multiplyBlend);
        Context.OMSetDepthStencilState(_depthRead);
    }

    /// <summary>반투명: 깊이는 읽기만 하고 알파로 섞는다.</summary>
    public void Translucent()
    {
        Context.OMSetBlendState(_alphaBlend);
        Context.OMSetDepthStencilState(_depthRead);
    }

    /// <summary>하늘처럼 깊이와 상관없이 까는 것.</summary>
    public void Background()
    {
        Context.OMSetBlendState(_opaqueBlend);
        Context.OMSetDepthStencilState(_depthNone);
    }

    public void Present() => _swapChain.Present(1, PresentFlags.None);

    /// <summary>지금 뒷버퍼를 PNG 로 적는다(확인용). Present 앞에서 부른다.</summary>
    public void Capture(string path)
    {
        Context.Flush();
        using var back = _swapChain.GetBuffer<ID3D11Texture2D>(0);
        using var staging = Device.CreateTexture2D(new Texture2DDescription
        {
            Width = (uint)Width,
            Height = (uint)Height,
            MipLevels = 1,
            ArraySize = 1,
            Format = Format.B8G8R8A8_UNorm,
            SampleDescription = new SampleDescription(1, 0),
            Usage = ResourceUsage.Staging,
            CPUAccessFlags = CpuAccessFlags.Read,
        });
        Context.CopyResource(staging, back);
        var map = Context.Map(staging, 0, MapMode.Read);
        try
        {
            var rows = new byte[Height * (Width * 3 + 1)];
            for (int y = 0; y < Height; y++)
            {
                var source = new ReadOnlySpan<byte>((void*)(map.DataPointer + y * (nint)map.RowPitch), Width * 4);
                int at = y * (Width * 3 + 1) + 1;
                for (int x = 0; x < Width; x++)
                {
                    rows[at + x * 3] = source[x * 4 + 2];
                    rows[at + x * 3 + 1] = source[x * 4 + 1];
                    rows[at + x * 3 + 2] = source[x * 4];
                }
            }
            Png.Write(path, Width, Height, rows);
        }
        finally { Context.Unmap(staging, 0); }
    }

    // ── 만들기 도우미 ────────────────────────────────────────────────────────

    public (ID3D11VertexShader, ID3D11PixelShader, ReadOnlyMemory<byte>) Compile(string source, string name)
    {
        var vs = Compiler.Compile(source, "VS", name, "vs_4_0");
        var ps = Compiler.Compile(source, "PS", name, "ps_4_0");
        return (Device.CreateVertexShader(vs.Span), Device.CreatePixelShader(ps.Span), vs);
    }

    public void Dispose()
    {
        D2D.Target = null;
        _d2dTarget.Dispose();
        D2D.Dispose();
        _d2dDevice.Dispose();
        _d2dFactory.Dispose();
        DWrite.Dispose();
        _cullNone.Dispose();
        _depthNone.Dispose();
        _depthRead.Dispose();
        _depthWrite.Dispose();
        _opaqueBlend.Dispose();
        _alphaBlend.Dispose();
        _wrapSampler.Dispose();
        _objectBuffer.Dispose();
        _frameBuffer.Dispose();
        _depth.Dispose();
        _depthTexture.Dispose();
        _backBuffer.Dispose();
        _swapChain.Dispose();
        Context.Dispose();
        Device.Dispose();
    }
}
