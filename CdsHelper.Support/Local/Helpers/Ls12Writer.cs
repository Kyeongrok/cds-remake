using System.Buffers.Binary;
using System.IO;

namespace CdsHelper.Support.Local.Helpers;

/// <summary>
/// LS11/Ls12 아카이브에 파트 하나를 <b>갈아 끼우거나·덧붙이거나·지우는</b> 손.
/// </summary>
/// <remarks>
/// <b>압축은 안 한다.</b> 파트 표는 <c>압축크기 == 원본크기</c> 면 날것으로 보므로
/// (<see cref="Ls12Reader.Decode"/> 의 「무압축 저장」 갈래) 새로 넣는 파트만 그렇게 적는다.
/// 건드리지 않는 파트는 <b>원본 덩어리를 그대로 옮겨 붙인다</b> — 다시 압축할 일이 없고
/// 파일도 거의 안 커진다.
///
/// <code>
///   0x000  매직 + 패딩        16바이트   그대로 옮긴다
///   0x010  사전 dictionary    256바이트  그대로 옮긴다
///   0x110  파트 표 12바이트 x N + 끝을 알리는 4바이트 0
///          데이터 덩어리들
/// </code>
/// 자리는 파일 첫머리부터 잰 <b>절대 자리</b>라, 파트를 하나 덧붙이면 표가 12바이트
/// 길어지면서 모든 덩어리가 밀린다. 그래서 통째로 다시 쓴다.
/// </remarks>
public static class Ls12Writer
{
    private const int HeadSize = 0x110;      // 매직 16 + 사전 256
    private const int RowSize = 12;

    /// <summary>왜 못 썼는지. 잘 됐으면 빈 글.</summary>
    public static string LastError { get; private set; } = "";

    /// <summary>
    /// 그 파트를 <paramref name="raw"/> 로 갈아 끼운다. <paramref name="part"/> 가 파트 수와
    /// 같으면 <b>맨 뒤에 덧붙인다</b>. 잘 됐으면 참.
    /// </summary>
    /// <param name="path">고칠 아카이브. 없으면 거짓.</param>
    /// <param name="part">갈아 끼울 자리. 0 부터다.</param>
    /// <param name="raw">넣을 날것. 크기는 마음대로다.</param>
    public static bool Put(string path, int part, byte[] raw)
    {
        LastError = "";
        if (raw.Length == 0) { LastError = "넣을 것이 비었습니다"; return false; }

        if (!Read(path, out var data, out var parts)) return false;
        if (part < 0 || part > parts.Count)
        {
            LastError = $"파트 자리가 범위 밖입니다(0~{parts.Count})";
            return false;
        }

        // 옮겨 붙일 덩어리들. 갈아 끼우는 자리만 날것으로 바꾼다.
        var blocks = new List<byte[]>(parts.Count + 1);
        var plain = new List<uint>(parts.Count + 1);
        for (int i = 0; i < parts.Count; i++)
        {
            if (i == part) { blocks.Add(raw); plain.Add((uint)raw.Length); continue; }
            if (Slice(data, parts[i], i) is not { } keep) return false;
            blocks.Add(keep);
            plain.Add(parts[i].Uncomp);
        }
        if (part == parts.Count) { blocks.Add(raw); plain.Add((uint)raw.Length); }

        return Write(path, data, blocks, plain);
    }

    /// <summary>
    /// 그 파트를 <b>아주 지운다</b>. 잘 됐으면 참.
    /// </summary>
    /// <remarks>
    /// <b>뒤 번호가 죄다 하나씩 당겨진다.</b> 파트 표에는 번호가 안 적혀 있고 줄 차례가
    /// 곧 번호라, 가운데를 지우면 그 뒤 것이 전부 밀려 올라간다. 번호로 가리키는 자료가
    /// 있으면(초상화가 그렇다) 맨 뒤가 아닌 자리는 함부로 지우면 안 된다.
    /// </remarks>
    public static bool Remove(string path, int part)
    {
        LastError = "";
        if (!Read(path, out var data, out var parts)) return false;

        if (part < 0 || part >= parts.Count)
        {
            LastError = $"파트 자리가 범위 밖입니다(0~{parts.Count - 1})";
            return false;
        }
        if (parts.Count == 1) { LastError = "마지막 하나는 못 지웁니다"; return false; }

        var blocks = new List<byte[]>(parts.Count - 1);
        var plain = new List<uint>(parts.Count - 1);
        for (int i = 0; i < parts.Count; i++)
        {
            if (i == part) continue;
            if (Slice(data, parts[i], i) is not { } keep) return false;
            blocks.Add(keep);
            plain.Add(parts[i].Uncomp);
        }
        return Write(path, data, blocks, plain);
    }

    // ── 잔손 ───────────────────────────────────────────────────────────────────

    private readonly record struct Row(uint Comp, uint Uncomp, uint Off);

    /// <summary>파일을 읽고 파트 표를 푼다.</summary>
    private static bool Read(string path, out byte[] data, out List<Row> parts)
    {
        data = [];
        parts = [];

        try { data = File.ReadAllBytes(path); }
        catch (IOException e) { LastError = e.Message; return false; }
        catch (UnauthorizedAccessException e) { LastError = e.Message; return false; }

        if (data.Length < HeadSize + 4) { LastError = "아카이브가 너무 짧습니다"; return false; }

        var magic = System.Text.Encoding.ASCII.GetString(data, 0, 4);
        if (magic != "LS11" && magic != "Ls12") { LastError = "LS11/Ls12 가 아닙니다"; return false; }

        for (int pos = HeadSize; pos + RowSize <= data.Length; pos += RowSize)
        {
            uint comp = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(pos));
            if (comp == 0) break;
            parts.Add(new Row(comp,
                              BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(pos + 4)),
                              BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(pos + 8))));
        }
        if (parts.Count == 0) { LastError = "파트가 없습니다"; return false; }
        return true;
    }

    /// <summary>손 안 댄 파트의 덩어리를 원본에서 그대로 떼어 온다.</summary>
    private static byte[]? Slice(byte[] data, Row row, int part)
    {
        if (row.Off > (uint)data.Length || row.Comp > (uint)data.Length - row.Off)
        {
            LastError = $"파트 {part} 가 파일 밖을 가리킵니다";
            return null;
        }
        return data[(int)row.Off..(int)(row.Off + row.Comp)];
    }

    /// <summary>
    /// 머리를 그대로 두고 표와 덩어리를 새로 깔아 되쓴다.
    /// </summary>
    /// <remarks>
    /// 자리는 파일 첫머리부터 잰 <b>절대 자리</b>다. 파트 수가 바뀌면 표 길이가 달라지면서
    /// 덩어리가 죄다 밀리므로 자리를 다시 매긴다.
    /// </remarks>
    private static bool Write(string path, byte[] data, List<byte[]> blocks, List<uint> plain)
    {
        int table = HeadSize + blocks.Count * RowSize + 4;
        var made = new byte[table + blocks.Sum(b => b.Length)];
        Array.Copy(data, made, HeadSize);

        int at = table;
        for (int i = 0; i < blocks.Count; i++)
        {
            int row = HeadSize + i * RowSize;

            BinaryPrimitives.WriteUInt32BigEndian(made.AsSpan(row), (uint)blocks[i].Length);
            BinaryPrimitives.WriteUInt32BigEndian(made.AsSpan(row + 4), plain[i]);
            BinaryPrimitives.WriteUInt32BigEndian(made.AsSpan(row + 8), (uint)at);

            Array.Copy(blocks[i], 0, made, at, blocks[i].Length);
            at += blocks[i].Length;
        }
        // 표 끝을 알리는 4바이트 0 은 배열이 이미 0 이라 그대로 둔다.

        try { File.WriteAllBytes(path, made); }
        catch (IOException e) { LastError = e.Message; return false; }
        catch (UnauthorizedAccessException e) { LastError = e.Message; return false; }
        return true;
    }

    /// <summary>
    /// 처음 고칠 때 옆에 백업을 하나 남긴다. 이미 있으면 그대로 둔다.
    /// </summary>
    public static void Backup(string path)
    {
        string keep = path + ".bak";
        try
        {
            if (File.Exists(path) && !File.Exists(keep)) File.Copy(path, keep);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
