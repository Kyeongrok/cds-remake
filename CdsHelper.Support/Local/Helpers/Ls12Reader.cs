using System.Buffers.Binary;
using System.IO;

namespace CdsHelper.Support.Local.Helpers;

/// <summary>
/// KOEI LS11/Ls12 압축 아카이브 리더. 대항해시대3의 .CDS 파일 대부분이 이 형식이다
/// (WORLD.CDS / SAVEDATA.CDS / ACCDATA.CDS 만 예외로 날것).
/// </summary>
/// <remarks>
/// 파일 구조
/// <code>
///   0x000  매직 "Ls12"(또는 "LS11") + 공백 패딩      16바이트
///   0x010  사전 dictionary[256]                     256바이트
///   0x110  파트 표 — 12바이트씩 N개 (전부 빅엔디안)
///            +0 압축크기  +4 원본크기  +8 시작주소
///          4바이트 0 = 표 끝
///          데이터 블록들
/// </code>
/// 압축은 가변길이 비트코드다. code &lt; 256 이면 사전에서 바이트 하나를 내고,
/// code >= 256 이면 거리(code-256)를 받아 둔 뒤 다음 code로 길이(3+code)만큼 뒤에서 복사한다.
///
/// cds95-mod 의 plugins-src/CharacterUtilKR/src/ls12.c 를 옮긴 것이다. 디코드만 있다 —
/// 이 프로그램은 CDS 를 읽기만 한다.
/// </remarks>
public sealed class Ls12Reader
{
    private const int DictOffset = 0x10;
    private const int TableOffset = 0x110;
    private const int MaxParts = 512;

    private readonly byte[] _data;
    private readonly byte[] _dict;
    private readonly (uint Comp, uint Uncomp, uint Off)[] _parts;

    private Ls12Reader(byte[] data, byte[] dict, (uint, uint, uint)[] parts)
    {
        _data = data;
        _dict = dict;
        _parts = parts;
    }

    /// <summary>파트 개수.</summary>
    public int PartCount => _parts.Length;

    /// <summary>파트의 원본(압축 해제) 크기. 버퍼를 잡기 전에 확인용.</summary>
    public uint PartSize(int index) =>
        index >= 0 && index < _parts.Length ? _parts[index].Uncomp : 0;

    /// <summary>LS11/Ls12 파일을 열어 파트 표까지 읽는다. 형식이 아니거나 파트가 없으면 null.</summary>
    public static Ls12Reader? Open(string path)
    {
        if (!File.Exists(path)) return null;
        byte[] data;
        try
        {
            data = File.ReadAllBytes(path);
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
        return From(data);
    }

    /// <summary>이미 메모리에 올린 바이트열에서 읽는다.</summary>
    public static Ls12Reader? From(byte[] data)
    {
        if (data.Length < TableOffset + 4) return null;
        var magic = System.Text.Encoding.ASCII.GetString(data, 0, 4);
        if (magic != "LS11" && magic != "Ls12") return null;

        var dict = new byte[256];
        Array.Copy(data, DictOffset, dict, 0, 256);

        var parts = new List<(uint, uint, uint)>();
        int pos = TableOffset;
        while (pos + 12 <= data.Length && parts.Count < MaxParts)
        {
            uint comp = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(pos));
            if (comp == 0) break;                       // 4바이트 0 = 표 끝
            parts.Add((comp,
                       BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(pos + 4)),
                       BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(pos + 8))));
            pos += 12;
        }
        if (parts.Count == 0) return null;
        return new Ls12Reader(data, dict, parts.ToArray());
    }

    /// <summary>
    /// 파트 하나를 원본 크기만큼 풀어 돌려준다. 파트 표가 파일 밖을 가리키거나
    /// 원본 크기를 다 못 채우면 null.
    /// </summary>
    public byte[]? Decode(int index)
    {
        if (index < 0 || index >= _parts.Length) return null;
        var (comp, uncomp, off) = _parts[index];

        // 파트 표의 값은 파일에서 그대로 읽은 것이라 파일 밖을 가리킬 수 있다.
        if (off >= (uint)_data.Length) return null;
        if (comp > (uint)_data.Length - off) return null;
        if (uncomp == 0 || uncomp > int.MaxValue) return null;

        var outBuf = new byte[uncomp];
        if (comp == uncomp)                             // 무압축 저장
        {
            Array.Copy(_data, off, outBuf, 0, (int)uncomp);
            return outBuf;
        }

        var src = _data.AsSpan((int)off, (int)comp);
        long totalBits = (long)comp * 8;
        long bitPos = 0;
        int outPos = 0;
        uint delta = 0;

        while (outPos < outBuf.Length && bitPos < totalBits)
        {
            // unary: 1이 이어지는 동안 읽다가 0을 만나면 멈춘다. 읽은 비트 수가 maskLen.
            int maskLen = 0;
            int bit;
            do
            {
                bit = (src[(int)(bitPos >> 3)] >> (7 - (int)(bitPos & 7))) & 1;
                bitPos++;
                maskLen++;
            } while (bit != 0 && bitPos < totalBits && maskLen < 31);

            // 31비트를 넘기면 code 계산이 넘쳐 버린다 — 정상 스트림에는 없는 일이라 여기서 끊는다.
            if (maskLen >= 31) break;

            uint factor = 0;
            for (int k = 0; k < maskLen && bitPos < totalBits; k++)
            {
                factor = (factor << 1) | (uint)((src[(int)(bitPos >> 3)] >> (7 - (int)(bitPos & 7))) & 1);
                bitPos++;
            }
            uint code = ((1u << maskLen) - 2u) + factor;

            if (delta > 0)                              // 앞서 거리를 받아 뒀으면 이번엔 길이다
            {
                long runLen = 3L + code;
                for (long i = 0; i < runLen && outPos < outBuf.Length; i++)
                {
                    outBuf[outPos] = outPos >= delta ? outBuf[outPos - delta] : (byte)0;
                    outPos++;
                }
                delta = 0;
            }
            else if (code < 256)
            {
                outBuf[outPos++] = _dict[code];
            }
            else
            {
                delta = code - 256;
            }
        }

        return outPos == outBuf.Length ? outBuf : null;
    }
}
