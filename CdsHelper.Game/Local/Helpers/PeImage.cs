using System.Buffers.Binary;
using System.IO;
using System.Text;

namespace CdsHelper.Game.Local.Helpers;

/// <summary>
/// EXE 안의 표를 읽으려고 두는 아주 작은 PE 리더. VA 를 파일 오프셋으로 옮기고, 그 자리의
/// 정수와 CP949 문자열을 꺼내 준다. 그 이상은 하지 않는다.
/// </summary>
/// <remarks>
/// 게임 표를 저장소에 구워 두지 않고 그때그때 읽으려고 만들었다 —
/// <see cref="CityBuildingTable"/> 과 <see cref="BookTable"/> 이 같이 쓴다.
/// </remarks>
public sealed class PeImage
{
    private readonly byte[] _bytes;
    private readonly (uint Va, uint VSize, uint Raw, uint RawSize)[] _sections;
    private readonly uint _imageBase;
    private readonly Encoding _cp949;

    private PeImage(byte[] bytes, uint imageBase,
                    (uint, uint, uint, uint)[] sections, Encoding cp949)
    {
        _bytes = bytes;
        _imageBase = imageBase;
        _sections = sections;
        _cp949 = cp949;
    }

    /// <summary>파일을 열어 섹션표까지 읽는다. 못 읽으면 null 이고 까닭을 낸다.</summary>
    public static PeImage? Read(string path, out string error)
    {
        error = "";
        if (!File.Exists(path)) { error = $"{path} 가 없습니다"; return null; }

        byte[] bytes;
        try { bytes = File.ReadAllBytes(path); }
        catch (IOException ex) { error = ex.Message; return null; }
        catch (UnauthorizedAccessException ex) { error = ex.Message; return null; }

        // 표 안의 글자는 CP949 다. 앱이 이미 등록해 두지만(App.cs) 시험용으로 이 클래스만
        // 쓸 때도 되도록 여기서도 한 번 등록한다 — 두 번 불러도 탈이 없다.
        Encoding cp949;
        try
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            cp949 = Encoding.GetEncoding(949);
        }
        catch (ArgumentException) { error = "CP949 를 쓸 수 없습니다"; return null; }

        if (bytes.Length < 0x40 || bytes[0] != 'M' || bytes[1] != 'Z')
        {
            error = "PE 파일이 아닙니다";
            return null;
        }
        int pe = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(0x3C));
        if (pe < 0 || pe + 0x78 > bytes.Length ||
            BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(pe)) != 0x00004550)
        {
            error = "PE 머리를 못 찾았습니다";
            return null;
        }

        int sectionCount = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(pe + 6));
        int optSize = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(pe + 20));
        uint imageBase = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(pe + 24 + 28));
        int table = pe + 24 + optSize;

        var sections = new (uint, uint, uint, uint)[sectionCount];
        for (int i = 0; i < sectionCount; i++)
        {
            int s = table + i * 40;
            if (s + 40 > bytes.Length) { error = "섹션표가 잘렸습니다"; return null; }
            sections[i] = (
                BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(s + 12)),   // VirtualAddress
                BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(s + 8)),    // VirtualSize
                BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(s + 20)),   // PointerToRawData
                BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(s + 16)));  // SizeOfRawData
        }
        return new PeImage(bytes, imageBase, sections, cp949);
    }

    /// <summary>VA 의 파일 오프셋. 파일에 값이 없는 자리면 -1.</summary>
    public int Offset(uint va)
    {
        uint rva = va - _imageBase;
        foreach (var (secRva, vsize, raw, rawSize) in _sections)
        {
            if (rva < secRva || rva >= secRva + Math.Max(vsize, rawSize)) continue;
            uint o = rva - secRva;
            return o < rawSize ? (int)(raw + o) : -1;
        }
        return -1;
    }

    /// <summary>그 자리의 dword. 파일 밖이면 0.</summary>
    public uint Word(int va)
    {
        int o = Offset((uint)va);
        return o < 0 || o + 4 > _bytes.Length
            ? 0
            : BinaryPrimitives.ReadUInt32LittleEndian(_bytes.AsSpan(o));
    }

    /// <summary>그 자리의 부호 있는 dword. 표의 -1(없음)을 그대로 받으려고 둔다.</summary>
    public int Int(int va) => (int)Word(va);

    /// <summary>그 자리의 날바이트 문자열(NUL 앞까지). 못 읽으면 null.</summary>
    public byte[]? Raw(uint va, int limit = 64)
    {
        if (va == 0) return null;
        int o = Offset(va);
        if (o < 0) return null;
        int end = Array.IndexOf(_bytes, (byte)0, o, Math.Min(limit, _bytes.Length - o));
        return end < 0 ? null : _bytes.AsSpan(o, end - o).ToArray();
    }

    /// <summary>그 자리의 CP949 문자열. 못 읽으면 null.</summary>
    public string? Text(uint va, int limit = 64)
    {
        if (va == 0) return null;
        int o = Offset(va);
        if (o < 0) return null;
        int end = Array.IndexOf(_bytes, (byte)0, o, Math.Min(limit, _bytes.Length - o));
        return end < 0 ? null : _cp949.GetString(_bytes, o, end - o);
    }
}
