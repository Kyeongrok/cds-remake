// DHO 저장소(C:\Users\ocean\git\dho, src/Dho/Render)에서 옮겨 온 것이다 — 편집기 「DHO 배 찍기」의 3D 미리보기가 쓴다. 고칠 때는 그쪽과 맞춘다.
using System.IO;
using System.Numerics;
using System.Runtime.InteropServices;
using Vortice.Direct3D11;
using Vortice.DXGI;

namespace Dho.Render;

[StructLayout(LayoutKind.Sequential)]
internal struct Vertex(Vector3 position, Vector3 normal, Vector4 color, Vector2 uv = default)
{
    public Vector3 Position = position;
    public Vector3 Normal = normal;
    public Vector4 Color = color;
    public Vector2 Uv = uv;

    public static readonly InputElementDescription[] Layout =
    [
        new("POSITION", 0, Format.R32G32B32_Float, 0, 0),
        new("NORMAL", 0, Format.R32G32B32_Float, 12, 0),
        new("COLOR", 0, Format.R32G32B32A32_Float, 24, 0),
        new("TEXCOORD", 0, Format.R32G32_Float, 40, 0),
    ];

    public const int Stride = 48;
}

/// <summary>GPU 에 올린 삼각형 묶음 하나.</summary>
internal sealed class Mesh : IDisposable
{
    private readonly ID3D11Buffer _vertices, _indices;
    private readonly int _indexCount;

    public Mesh(Gfx gfx, ReadOnlySpan<Vertex> vertices, ReadOnlySpan<uint> indices)
    {
        _vertices = gfx.Device.CreateBuffer(vertices, BindFlags.VertexBuffer);
        _indices = gfx.Device.CreateBuffer(indices, BindFlags.IndexBuffer);
        _indexCount = indices.Length;
    }

    public void Draw(Gfx gfx)
    {
        gfx.Context.IASetVertexBuffer(0, _vertices, Vertex.Stride);
        gfx.Context.IASetIndexBuffer(_indices, Format.R32_UInt, 0);
        gfx.Context.DrawIndexed((uint)_indexCount, 0, 0);
    }

    public void Dispose()
    {
        _vertices.Dispose();
        _indices.Dispose();
    }
}

/// <summary>정점과 색인을 모아 <see cref="Mesh"/> 를 만든다.</summary>
internal sealed class MeshBuilder
{
    public readonly List<Vertex> Vertices = [];
    public readonly List<uint> Indices = [];

    public Mesh Build(Gfx gfx) =>
        new(gfx, CollectionsMarshal.AsSpan(Vertices), CollectionsMarshal.AsSpan(Indices));

    public uint Add(Vector3 position, Vector3 normal, Vector4 color, Vector2 uv = default)
    {
        Vertices.Add(new Vertex(position, normal, color, uv));
        return (uint)Vertices.Count - 1;
    }

    public void Triangle(uint a, uint b, uint c)
    {
        Indices.Add(a); Indices.Add(b); Indices.Add(c);
    }

    /// <summary>네 꼭짓점(한 바퀴 차례)으로 면 하나. 법선은 면에서 셈한다.</summary>
    public void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector4 color)
    {
        var normal = Vector3.Normalize(Vector3.Cross(b - a, d - a));
        uint i = Add(a, normal, color, new Vector2(0, 0));
        Add(b, normal, color, new Vector2(1, 0));
        Add(c, normal, color, new Vector2(1, 1));
        Add(d, normal, color, new Vector2(0, 1));
        Triangle(i, i + 1, i + 2);
        Triangle(i, i + 2, i + 3);
    }

    /// <summary>축에 나란한 상자.</summary>
    public void Box(Vector3 min, Vector3 max, Vector4 color) =>
        Box(Matrix4x4.Identity, min, max, color);

    /// <summary>상자를 <paramref name="transform"/> 으로 옮겨 넣는다.</summary>
    public void Box(Matrix4x4 transform, Vector3 min, Vector3 max, Vector4 color)
    {
        Vector3 P(float x, float y, float z) => Vector3.Transform(new Vector3(x, y, z), transform);
        Vector3 p000 = P(min.X, min.Y, min.Z), p100 = P(max.X, min.Y, min.Z);
        Vector3 p010 = P(min.X, max.Y, min.Z), p110 = P(max.X, max.Y, min.Z);
        Vector3 p001 = P(min.X, min.Y, max.Z), p101 = P(max.X, min.Y, max.Z);
        Vector3 p011 = P(min.X, max.Y, max.Z), p111 = P(max.X, max.Y, max.Z);
        Quad(p010, p011, p111, p110, color);   // 위
        Quad(p000, p100, p101, p001, color);   // 아래
        Quad(p001, p101, p111, p011, color);   // +Z
        Quad(p100, p000, p010, p110, color);   // -Z
        Quad(p101, p100, p110, p111, color);   // +X
        Quad(p000, p001, p011, p010, color);   // -X
    }

    /// <summary>두 점을 잇는 여덟모 기둥(돛대·활대·밧줄).</summary>
    public void Cylinder(Vector3 from, Vector3 to, float radiusFrom, float radiusTo, Vector4 color, int sides = 8)
    {
        var axis = Vector3.Normalize(to - from);
        var side = Vector3.Normalize(Vector3.Cross(axis, MathF.Abs(axis.Y) < 0.9f ? Vector3.UnitY : Vector3.UnitX));
        var up = Vector3.Cross(axis, side);
        uint first = (uint)Vertices.Count;
        for (int i = 0; i <= sides; i++)
        {
            float angle = i * MathF.Tau / sides;
            var normal = side * MathF.Cos(angle) + up * MathF.Sin(angle);
            Add(from + normal * radiusFrom, normal, color);
            Add(to + normal * radiusTo, normal, color);
        }
        for (uint i = 0; i < sides; i++)
        {
            uint a = first + i * 2;
            Triangle(a, a + 1, a + 3);
            Triangle(a, a + 3, a + 2);
        }
    }
}
