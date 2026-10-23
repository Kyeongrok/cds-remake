// DHO 저장소(C:\Users\ocean\git\dho, src/Dho/Render)에서 옮겨 온 것이다 — 편집기 「DHO 배 찍기」의 3D 미리보기가 쓴다. 고칠 때는 그쪽과 맞춘다.
using System.IO;
using System.Buffers.Binary;
using Vortice.Direct3D11;
using Vortice.DXGI;

namespace Dho.Render;

/// <summary>게임 텍스처(<c>MFTF0100</c> · <c>XFTX0200</c> · <c>GTEX</c>)를 GPU 텍스처로 올린다.</summary>
/// <remarks>
/// MFTF: +16 u32 개수 N, +20 부터 N × (u32 형식, u32 자리, u32 크기, u16 너비, u16 높이) — 자리에 XFTX 가 통째로 있다.
/// XFTX: +16 u32 그림 수, +20 u32 레코드 자리 × 그림 수. 레코드(자리 R):
/// u16 너비, u16 높이, u32 형식, u8, u8 밉 수, u8, u8 벌 수 K, 이어서 u32 자리(R 부터) × (밉 수 × max(K,1)).
/// 형식은 D3DFORMAT(21 = A8R8G8B8) 또는 FourCC(DXT1·DXT3·DXT5).
/// GTEX: "GTEX", u32 0, u32 개수, u32 자리 × 개수. 텍스처: u32 형식(21 또는 20 = R8G8B8), u16 너비, u16 높이,
/// u8, u8 밉 수, u8, u8, 이어서 눌리지 않은 밉들.
/// </remarks>
internal static unsafe class GameTexture
{
    private const uint Dxt1 = 0x31545844, Dxt3 = 0x33545844, Dxt5 = 0x35545844;
    private const uint A8R8G8B8 = 21, R8G8B8 = 20;

    /// <summary>MFTF(또는 XFTX)의 그림 하나. MFTF 면 DXT 판을 고른다.</summary>
    public static ID3D11ShaderResourceView FromMftf(Gfx gfx, byte[] data, int image = 0, int variant = 0)
    {
        int xftx = 0;
        if (data.AsSpan(0, 4).SequenceEqual("MFTF"u8))
        {
            int count = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(16));
            int best = -1;
            for (int i = 0; i < count; i++)
            {
                uint format = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(20 + i * 16));
                int at = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(24 + i * 16));
                if (format is Dxt1 or Dxt3 or Dxt5) { xftx = at; best = i; break; }
                if (format == A8R8G8B8 && best < 0) { xftx = at; best = i; }
            }
            if (best < 0) throw new NotSupportedException("MFTF 에 쓸 수 있는 형식판이 없다.");
        }

        int record = xftx + BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(xftx + 20 + image * 4));
        int width = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(record));
        int height = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(record + 2));
        uint fourCc = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(record + 4));
        int mips = data[record + 9];
        variant = Math.Min(variant, Math.Max(1, (int)data[record + 11]) - 1);

        var (dxgi, blockBytes) = fourCc switch
        {
            Dxt1 => (Format.BC1_UNorm, 8),
            Dxt3 => (Format.BC2_UNorm, 16),
            Dxt5 => (Format.BC3_UNorm, 16),
            A8R8G8B8 => (Format.B8G8R8A8_UNorm, 0),
            _ => throw new NotSupportedException($"텍스처 형식 {fourCc:X}"),
        };

        fixed (byte* bytes = data)
        {
            var levels = new SubresourceData[mips];
            for (int m = 0; m < mips; m++)
            {
                int at = record + BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(record + 12 + (variant * mips + m) * 4));
                int w = Math.Max(1, width >> m);
                uint pitch = blockBytes == 0 ? (uint)w * 4 : (uint)(Math.Max(1, (w + 3) / 4) * blockBytes);
                levels[m] = new SubresourceData(bytes + at, pitch);
            }
            return Create(gfx, width, height, dxgi, levels);
        }
    }

    /// <summary>MFTF 의 DXT3 그림 하나(색 벌 <paramref name="variant"/>)를 BGRA 로 편다 — 화면의 작은 그림용.</summary>
    public static (int Width, int Height, byte[] Bgra)? Pixels(byte[] data, int variant)
    {
        int xftx = -1;
        for (int i = 0; i < BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(16)); i++)
            if (BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(20 + i * 16)) is Dxt1 or Dxt3 or Dxt5) xftx = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(24 + i * 16));
        if (xftx < 0) return null;
        int record = xftx + BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(xftx + 20));
        int width = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(record)), height = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(record + 2));
        int mips = data[record + 9];
        // DXT1 은 블록이 8바이트(색만), DXT3 · 5 는 앞 8바이트가 알파다. 색 벌이 없는 그림(특수 돛)은 첫 벌만 있다
        int skip = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(record + 4)) == Dxt1 ? 0 : 8;
        variant = Math.Min(variant, Math.Max(1, (int)data[record + 11]) - 1);
        int at = record + BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(record + 12 + variant * mips * 4));
        var pixels = new byte[width * height * 4];
        Span<byte> colors = stackalloc byte[12];
        for (int by = 0; by < height / 4; by++)
        for (int bx = 0; bx < width / 4; bx++, at += skip + 8)
        {
            int c0 = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(at + skip)), c1 = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(at + skip + 2));
            uint bits = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(at + skip + 4));
            for (int k = 0; k < 3; k++)
            {
                int shift = k == 0 ? 0 : k == 1 ? 5 : 11, mask = k == 1 ? 63 : 31;
                int a = (c0 >> shift & mask) * 255 / mask, b = (c1 >> shift & mask) * 255 / mask;
                (colors[k], colors[3 + k], colors[6 + k], colors[9 + k]) = ((byte)a, (byte)b, (byte)((2 * a + b) / 3), (byte)((a + 2 * b) / 3));
            }
            for (int p = 0; p < 16; p++)
            {
                int pick = (int)(bits >> p * 2 & 3) * 3, to = ((by * 4 + p / 4) * width + bx * 4 + p % 4) * 4;
                (pixels[to], pixels[to + 1], pixels[to + 2], pixels[to + 3]) = (colors[pick], colors[pick + 1], colors[pick + 2], 255);
            }
        }
        return (width, height, pixels);
    }

    /// <summary>GTEX 묶음의 텍스처 하나. <paramref name="gtex"/> 는 "GTEX" 자리.</summary>
    public static ID3D11ShaderResourceView FromGtex(Gfx gfx, byte[] data, int gtex, int index)
    {
        int at = gtex + BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(gtex + 12 + index * 4));
        uint format = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(at));
        int width = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(at + 4));
        int height = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(at + 6));
        int mips = Math.Max(1, (int)data[at + 9]);
        int bytesPerPixel = format == R8G8B8 ? 3 : 4;
        at += 12;

        // 밉마다 BGRA 로 펴 놓는다
        var chain = new byte[mips][];
        for (int m = 0; m < mips; m++)
        {
            int w = Math.Max(1, width >> m), h = Math.Max(1, height >> m);
            var level = chain[m] = new byte[w * h * 4];
            if (bytesPerPixel == 4) data.AsSpan(at, w * h * 4).CopyTo(level);
            else
                for (int i = 0; i < w * h; i++)
                {
                    level[i * 4] = data[at + i * 3];
                    level[i * 4 + 1] = data[at + i * 3 + 1];
                    level[i * 4 + 2] = data[at + i * 3 + 2];
                    level[i * 4 + 3] = 255;
                }
            at += w * h * bytesPerPixel;
        }

        var handles = new System.Runtime.InteropServices.GCHandle[mips];
        try
        {
            var levels = new SubresourceData[mips];
            for (int m = 0; m < mips; m++)
            {
                handles[m] = System.Runtime.InteropServices.GCHandle.Alloc(chain[m], System.Runtime.InteropServices.GCHandleType.Pinned);
                levels[m] = new SubresourceData(handles[m].AddrOfPinnedObject(), (uint)Math.Max(1, width >> m) * 4);
            }
            return Create(gfx, width, height, Format.B8G8R8A8_UNorm, levels);
        }
        finally
        {
            foreach (var handle in handles)
                if (handle.IsAllocated) handle.Free();
        }
    }

    /// <summary>MFTF 묶음의 A8R8G8B8 판에 든 그림 수(없으면 0)와, 그 가운데 하나를 BGRA 로 — 원본 이펙트 그림을 화면에 보일 때 쓴다.</summary>
    public static int ArgbCount(byte[] data) => ArgbAt(data) is int xftx and >= 0 ? BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(xftx + 16)) : 0;
    public static (int Width, int Height, byte[] Bgra)? ArgbImage(byte[] data, int image)
    {
        int xftx = ArgbAt(data);
        if (xftx < 0 || image < 0 || image >= BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(xftx + 16))) return null;
        int record = xftx + BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(xftx + 20 + image * 4));
        int width = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(record)), height = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(record + 2));
        int at = record + BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(record + 12));
        if (width <= 0 || height <= 0 || at + width * height * 4 > data.Length) return null;
        return (width, height, data.AsSpan(at, width * height * 4).ToArray());
    }
    private static int ArgbAt(byte[] data)
    {
        if (data.Length < 24 || !data.AsSpan(0, 4).SequenceEqual("MFTF"u8)) return -1;
        for (int i = 0; i < BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(16)); i++)
            if (BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(20 + i * 16)) == A8R8G8B8) return BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(24 + i * 16));
        return -1;
    }

    /// <summary>GTEX 텍스처에서 뚫린(알파가 거의 0 인) 점의 몫 — 나뭇잎 · 울타리처럼 오려 낸 그림을 가려내는 데 쓴다. 알파가 없는 그림은 0.</summary>
    public static float GtexHoles(byte[] data, int gtex, int index)
    {
        int at = gtex + BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(gtex + 12 + index * 4));
        if (BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(at)) == R8G8B8) return 0;
        int width = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(at + 4)), height = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(at + 6));
        at += 12;
        int seen = 0, holes = 0;
        for (int i = 0; i < width * height && at + i * 4 + 3 < data.Length; i += 3, seen++)
            if (data[at + i * 4 + 3] < 48) holes++;
        return seen == 0 ? 0 : holes / (float)seen;
    }

    public static int GtexCount(byte[] data, int gtex) => BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(gtex + 8));

    private static ID3D11ShaderResourceView Create(Gfx gfx, int width, int height, Format format, SubresourceData[] levels)
    {
        using var texture = gfx.Device.CreateTexture2D(new Texture2DDescription
        {
            Width = (uint)width,
            Height = (uint)height,
            MipLevels = (uint)levels.Length,
            ArraySize = 1,
            Format = format,
            SampleDescription = new SampleDescription(1, 0),
            BindFlags = BindFlags.ShaderResource,
        }, levels);
        return gfx.Device.CreateShaderResourceView(texture);
    }
}
