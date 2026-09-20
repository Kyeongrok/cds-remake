using System.Buffers.Binary;
using System.IO;
using CdsHelper.Support.Local.Helpers;

namespace CdsHelper.Game.Local.Helpers;

/// <summary>
/// <c>DISEV.CDS</c> — 발견물마다 하나씩 든 발견 이벤트 스크립트 아카이브.
/// </summary>
/// <remarks>
/// 껍데기는 여느 CDS 와 같은 <b>LS12</b> 다(<see cref="Ls12Reader"/>). 다른 점은
/// <b>파트 하나가 발견물 하나</b>라는 것뿐이다 — 파트 번호가 곧
/// <see cref="DiscoveryTable"/> 의 줄 번호다.
///
/// <code>
///   0x000  "Ls12" + 공백           16바이트
///   0x010  사전                   256바이트
///   0x110  파트 표 — 12바이트씩 (전부 빅엔디안)
///            +0 압축크기  +4 원본크기  +8 시작주소
///          4바이트 0 이 표의 끝이다
/// </code>
///
/// <b>고친 파트는 압축하지 않고 그대로 써 넣는다.</b> LS12 는 압축크기와 원본크기가
/// 같으면 그냥 베끼는 것으로 읽으므로(<see cref="Ls12Reader.Decode"/> 첫 줄), 인코더를
/// 만들 까닭이 없다. 파일이 조금 커지는 대신 되읽기가 확실하다.
/// </remarks>
public sealed class DisevArchive
{
    private const int DictOffset = 0x10;
    private const int TableOffset = 0x110;
    private const int MaxParts = 512;

    /// <summary>아카이브 안의 파트 한 줄.</summary>
    private readonly record struct Entry(uint Compressed, uint Uncompressed, uint Offset);

    private readonly byte[] _original;
    private readonly Entry[] _entries;
    private readonly byte[][] _decoded;
    private readonly byte[]?[] _edited;

    /// <summary>마지막으로 걸린 탈. 창에 그대로 내보인다.</summary>
    public static string LastError { get; private set; } = "";

    /// <summary>읽어 온 파일 자리.</summary>
    public string FilePath { get; }

    /// <summary>파트 수. 발견물 수(274)와 같아야 정상이다.</summary>
    public int PartCount => _entries.Length;

    private DisevArchive(string path, byte[] original, Entry[] entries, byte[][] decoded)
    {
        FilePath = path;
        _original = original;
        _entries = entries;
        _decoded = decoded;
        _edited = new byte[]?[entries.Length];
    }

    /// <summary>파일 하나를 열어 파트를 죄다 풀어 둔다. 못 읽으면 null.</summary>
    public static DisevArchive? Open(string path)
    {
        LastError = "";
        if (!File.Exists(path))
        {
            LastError = $"{path} 가 없습니다";
            return null;
        }

        byte[] data;
        try
        {
            data = File.ReadAllBytes(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            LastError = ex.Message;
            return null;
        }

        if (data.Length < TableOffset + 4 ||
            (System.Text.Encoding.ASCII.GetString(data, 0, 4) is not ("Ls12" or "LS11")))
        {
            LastError = "LS12/LS11 아카이브가 아닙니다";
            return null;
        }

        var entries = new List<Entry>();
        for (int pos = TableOffset; pos + 12 <= data.Length && entries.Count < MaxParts; pos += 12)
        {
            uint comp = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(pos));
            if (comp == 0) break;                       // 4바이트 0 = 표 끝
            uint uncomp = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(pos + 4));
            uint off = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(pos + 8));
            if (off < TableOffset || off + comp > (uint)data.Length)
            {
                LastError = $"파트 {entries.Count} 의 자리가 파일 밖입니다";
                return null;
            }
            entries.Add(new Entry(comp, uncomp, off));
        }

        if (entries.Count == 0)
        {
            LastError = "파트 표가 비어 있습니다";
            return null;
        }

        var reader = Ls12Reader.From(data);
        if (reader == null || reader.PartCount != entries.Count)
        {
            LastError = "파트를 푸는 데 실패했습니다";
            return null;
        }

        var decoded = new byte[entries.Count][];
        for (int i = 0; i < entries.Count; i++)
        {
            var part = reader.Decode(i);
            if (part == null)
            {
                LastError = $"파트 {i} 를 푸는 데 실패했습니다";
                return null;
            }
            decoded[i] = part;
        }

        return new DisevArchive(path, data, entries.ToArray(), decoded);
    }

    /// <summary>파트 알맹이 — 고친 것이 있으면 고친 쪽을 준다.</summary>
    public byte[] Part(int index) =>
        index >= 0 && index < _decoded.Length
            ? _edited[index] ?? _decoded[index]
            : [];

    /// <summary>손댄 파트인가.</summary>
    public bool IsModified(int index) =>
        index >= 0 && index < _edited.Length && _edited[index] != null;

    /// <summary>손댄 파트가 하나라도 있는가.</summary>
    public bool HasChanges => _edited.Any(part => part != null);

    /// <summary>파트 하나를 갈아 끼운다. 원본과 같으면 손댄 표시를 지운다.</summary>
    public void ReplacePart(int index, byte[] data)
    {
        if (index < 0 || index >= _edited.Length) return;
        _edited[index] = _decoded[index].AsSpan().SequenceEqual(data) ? null : data;
    }

    /// <summary>파트 하나를 원래대로 되돌린다.</summary>
    public void Revert(int index)
    {
        if (index >= 0 && index < _edited.Length) _edited[index] = null;
    }

    /// <summary>손댄 것을 죄다 되돌린다.</summary>
    public void RevertAll() => Array.Clear(_edited);

    /// <summary>
    /// 아카이브를 다시 짜서 덮어쓴다. 먼저 <b>시각을 붙인 백업</b>을 옆에 남긴다.
    /// 잘 되면 백업 파일 이름을, 안 되면 null 을 돌려주고 까닭은
    /// <see cref="LastError"/> 에 적는다.
    /// </summary>
    public string? Save()
    {
        LastError = "";
        byte[] output;
        try
        {
            output = Build();
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            return null;
        }

        // 되읽어 확인한다 — 파트 수와 알맹이가 그대로여야 쓴다.
        var check = Ls12Reader.From(output);
        if (check == null || check.PartCount != _entries.Length)
        {
            LastError = "다시 짠 아카이브를 되읽지 못했습니다 — 쓰지 않았습니다";
            return null;
        }
        for (int i = 0; i < _entries.Length; i++)
        {
            var back = check.Decode(i);
            if (back == null || !back.AsSpan().SequenceEqual(Part(i)))
            {
                LastError = $"되읽기 확인에서 파트 {i} 가 어긋납니다 — 쓰지 않았습니다";
                return null;
            }
        }

        string backup = $"{FilePath}.{DateTime.Now:yyyyMMdd_HHmmss}.bak";
        try
        {
            File.Copy(FilePath, backup, overwrite: false);
            File.WriteAllBytes(FilePath, output);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            LastError = ex.Message;
            return null;
        }

        // 쓴 것이 이제 원본이다.
        for (int i = 0; i < _edited.Length; i++)
        {
            if (_edited[i] != null) _decoded[i] = _edited[i]!;
        }
        Array.Clear(_edited);
        return backup;
    }

    /// <summary>고친 파트는 날것으로, 안 고친 파트는 원본 압축 덩이 그대로 이어 붙인다.</summary>
    private byte[] Build()
    {
        var blobs = new byte[_entries.Length][];
        var meta = new (uint Comp, uint Uncomp)[_entries.Length];

        for (int i = 0; i < _entries.Length; i++)
        {
            if (_edited[i] is { } edited)
            {
                blobs[i] = edited;
                meta[i] = ((uint)edited.Length, (uint)edited.Length);
            }
            else
            {
                var entry = _entries[i];
                blobs[i] = _original.AsSpan((int)entry.Offset, (int)entry.Compressed).ToArray();
                meta[i] = (entry.Compressed, entry.Uncompressed);
            }
        }

        int tableEnd = TableOffset + _entries.Length * 12 + 4;
        int total = tableEnd + blobs.Sum(blob => blob.Length);
        var output = new byte[total];
        _original.AsSpan(0, TableOffset).CopyTo(output);

        uint payload = (uint)tableEnd;
        for (int i = 0; i < _entries.Length; i++)
        {
            int at = TableOffset + i * 12;
            BinaryPrimitives.WriteUInt32BigEndian(output.AsSpan(at), meta[i].Comp);
            BinaryPrimitives.WriteUInt32BigEndian(output.AsSpan(at + 4), meta[i].Uncomp);
            BinaryPrimitives.WriteUInt32BigEndian(output.AsSpan(at + 8), payload);
            payload += meta[i].Comp;
        }
        // 표 끝 표시 네 바이트는 이미 0 이다.

        int cursor = tableEnd;
        foreach (var blob in blobs)
        {
            blob.CopyTo(output.AsSpan(cursor));
            cursor += blob.Length;
        }
        return output;
    }

    /// <summary>사전 256바이트 — 파트를 손으로 뜯어 볼 때 쓴다.</summary>
    public ReadOnlySpan<byte> Dictionary => _original.AsSpan(DictOffset, 256);
}
