// DHO 저장소(C:\Users\ocean\git\dho, src/Dho.Data)에서 옮겨 온 것이다 — 편집기 「DHO 배 찍기」의 3D 미리보기가 쓴다. 고칠 때는 그쪽과 맞춘다.
using System.IO;
using System.Buffers.Binary;

namespace Dho.Data;

/// <summary>
/// <c>0001\*.bin</c> 묶음 — 이름 없이 차례가 곧 번호다.
/// </summary>
/// <remarks>
/// 큰 묶음(sh·md·hm·fi·ef): u8 0xFF, u16 개수, u32 자리 × (개수+1).
/// 작은 묶음(em·sa·fm): u8 개수, u32 자리 × (개수+1). 자리는 파일 처음부터, 마지막 자리가 파일 크기다.
/// </remarks>
public sealed class Pack
{
    private readonly string _path;
    private readonly int[] _offsets;

    public int Count => _offsets.Length - 1;

    public Pack(string relative)
    {
        _path = GvoFiles.PathOf(relative);
        using var stream = File.OpenRead(_path);
        int first = stream.ReadByte(), count = first, tableAt = 1;
        if (first == 0xFF)
        {
            var two = new byte[2];
            stream.ReadExactly(two);
            count = BinaryPrimitives.ReadUInt16LittleEndian(two);
            tableAt = 3;
        }
        stream.Position = tableAt;
        var table = new byte[(count + 1) * 4];
        stream.ReadExactly(table);
        _offsets = new int[count + 1];
        for (int i = 0; i <= count; i++) _offsets[i] = BinaryPrimitives.ReadInt32LittleEndian(table.AsSpan(i * 4));
    }

    public int Size(int index) => _offsets[index + 1] - _offsets[index];

    /// <summary>항목의 한 토막만 읽는다(머리만 볼 때).</summary>
    public byte[] Slice(int index, int offset, int count)
    {
        count = Math.Max(0, Math.Min(count, Size(index) - offset));
        using var stream = File.OpenRead(_path);
        stream.Position = _offsets[index] + offset;
        var data = new byte[count];
        stream.ReadExactly(data);
        return data;
    }

    public byte[] Entry(int index)
    {
        using var stream = File.OpenRead(_path);
        stream.Position = _offsets[index];
        var data = new byte[_offsets[index + 1] - _offsets[index]];
        stream.ReadExactly(data);
        return data;
    }
}
