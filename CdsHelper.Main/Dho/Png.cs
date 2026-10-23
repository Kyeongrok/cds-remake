// DHO 저장소(C:\Users\ocean\git\dho, src/Dho/Render)에서 옮겨 온 것이다 — 편집기 「DHO 배 찍기」의 3D 미리보기가 쓴다. 고칠 때는 그쪽과 맞춘다.
using System.IO;
using System.Buffers.Binary;
using System.IO.Compression;

namespace Dho.Render;

/// <summary>화면 찍기에 쓰는 가장 작은 PNG 쓰개 (8비트 RGB).</summary>
internal static class Png
{
    /// <param name="rows">줄마다 거르개 바이트 0 하나와 RGB × 너비.</param>
    public static void Write(string path, int width, int height, byte[] rows)
    {
        using var file = File.Create(path);
        file.Write([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);

        var header = new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(header, width);
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4), height);
        header[8] = 8;      // 채널마다 8비트
        header[9] = 2;      // RGB
        Chunk(file, "IHDR", header);

        using var packed = new MemoryStream();
        using (var zlib = new ZLibStream(packed, CompressionLevel.Fastest, leaveOpen: true))
            zlib.Write(rows);
        Chunk(file, "IDAT", packed.ToArray());
        Chunk(file, "IEND", []);
    }

    private static void Chunk(Stream file, string type, byte[] data)
    {
        var head = new byte[8];
        BinaryPrimitives.WriteInt32BigEndian(head, data.Length);
        for (int i = 0; i < 4; i++) head[4 + i] = (byte)type[i];
        file.Write(head);
        file.Write(data);

        uint crc = 0xFFFFFFFF;
        foreach (byte b in head.AsSpan(4)) crc = Step(crc, b);
        foreach (byte b in data) crc = Step(crc, b);
        var tail = new byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(tail, ~crc);
        file.Write(tail);

        static uint Step(uint crc, byte b)
        {
            crc ^= b;
            for (int k = 0; k < 8; k++) crc = (crc & 1) != 0 ? crc >> 1 ^ 0xEDB88320 : crc >> 1;
            return crc;
        }
    }
}
