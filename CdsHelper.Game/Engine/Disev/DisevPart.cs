using System.Buffers.Binary;

namespace CdsHelper.Game.Local.Helpers;

/// <summary>
/// 발견 이벤트 파트 하나의 뼈대 — 단계 번호 · 슬롯 표 · 덩이들.
/// </summary>
/// <remarks>
/// <code>
///   +0x00  u16  내부 단계 번호
///   +0x02  u16  슬롯 수 (1~16)
///   +0x04       슬롯마다 4바이트 — [조건 상대 오프셋 u16][본문 상대 오프셋 u16]
///   실제 자리 = 4 + 상대 오프셋
/// </code>
/// 슬롯 하나가 「이 조건이면 이 본문」한 벌이다. 위에서부터 조건을 재어 처음 맞는
/// 슬롯의 본문이 돈다. <b>조건과 본문 덩이는 여럿이 나눠 쓸 수 있다</b> — 슬롯 둘이
/// 같은 본문을 가리키는 일이 흔하다. 그래서 덩이를 슬롯별로 세지 않고
/// <see cref="ChunkStarts"/> 로 <b>서로 다른 시작 자리</b>만 모아 다룬다.
///
/// 덩이는 <c>0xFF</c> 로 끝난다.
/// </remarks>
public sealed class DisevPart
{
    /// <summary>슬롯 한 줄 — 조건 덩이와 본문 덩이의 절대 자리.</summary>
    public readonly record struct Slot(int Condition, int Body);

    private DisevPart(byte[] data, int step, Slot[] slots, int[] starts)
    {
        Data = data;
        Step = step;
        Slots = slots;
        ChunkStarts = starts;
    }

    /// <summary>파트 알맹이 그대로.</summary>
    public byte[] Data { get; }

    /// <summary>내부 단계 번호.</summary>
    public int Step { get; }

    /// <summary>슬롯 표.</summary>
    public IReadOnlyList<Slot> Slots { get; }

    /// <summary>서로 다른 덩이 시작 자리(오름차순).</summary>
    public IReadOnlyList<int> ChunkStarts { get; }

    /// <summary>슬롯 표가 끝나는 자리 — 첫 덩이는 이 뒤에 있어야 한다.</summary>
    public int HeaderEnd => 4 + Slots.Count * 4;

    /// <summary>뼈대를 읽는다. 어긋나면 null 이고 까닭이 <paramref name="error"/> 에 담긴다.</summary>
    public static DisevPart? Parse(byte[] data, out string error)
    {
        error = "";
        if (data.Length < 8)
        {
            error = "머리말이 너무 짧습니다";
            return null;
        }

        int step = BinaryPrimitives.ReadUInt16LittleEndian(data);
        int count = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(2));
        if (count is < 1 or > 16 || 4 + count * 4 > data.Length)
        {
            error = $"슬롯 수({count})가 말이 안 됩니다";
            return null;
        }

        int headerEnd = 4 + count * 4;
        var slots = new Slot[count];
        for (int i = 0; i < count; i++)
        {
            int condition = 4 + BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(4 + i * 4));
            int body = 4 + BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(6 + i * 4));
            if (condition < headerEnd || condition >= data.Length)
            {
                error = $"슬롯 {i} 의 조건 자리(+0x{condition:X4})가 파트 밖입니다";
                return null;
            }
            if (body < headerEnd || body >= data.Length)
            {
                error = $"슬롯 {i} 의 본문 자리(+0x{body:X4})가 파트 밖입니다";
                return null;
            }
            slots[i] = new Slot(condition, body);
        }

        var starts = slots.SelectMany(slot => new[] { slot.Condition, slot.Body })
                          .Distinct()
                          .Order()
                          .ToArray();
        return new DisevPart(data, step, slots, starts);
    }

    /// <summary>덩이 하나가 차지하는 범위 — 다음 덩이 시작(없으면 파트 끝)까지다.</summary>
    public (int Start, int End) ChunkRange(int start)
    {
        int end = Data.Length;
        foreach (int candidate in ChunkStarts)
        {
            if (candidate > start && candidate < end) end = candidate;
        }
        return (start, end);
    }

    /// <summary>덩이 하나를 떼어 낸다.</summary>
    public byte[] Chunk(int start)
    {
        var (from, to) = ChunkRange(start);
        return Data.AsSpan(from, to - from).ToArray();
    }

    /// <summary>이 덩이를 가리키는 슬롯들을 사람이 읽을 꼴로 적는다("0 조건 · 2 본문").</summary>
    public string UsersOf(int start)
    {
        var users = new List<string>();
        for (int i = 0; i < Slots.Count; i++)
        {
            if (Slots[i].Condition == start) users.Add($"{i} 조건");
            if (Slots[i].Body == start) users.Add($"{i} 본문");
        }
        return string.Join(" · ", users);
    }

    /// <summary>
    /// 덩이 하나를 갈아 끼운 새 파트를 짓는다 — <b>슬롯 표의 오프셋을 다시 잡아 준다.</b>
    /// </summary>
    /// <remarks>
    /// 길이가 달라져도 덩이 차례는 그대로 두고 뒤를 밀 뿐이다. 그러므로 <b>덩이 안에서만
    /// 뛰는 상대 이동</b>은 안전하지만, 덩이 경계를 넘어 뛰는 값이 있다면 어긋난다.
    /// 그런 자리는 창이 따로 일러 준다.
    /// </remarks>
    public byte[]? Rebuild(int chunkStart, byte[] replacement, out string error)
    {
        error = "";
        if (!ChunkStarts.Contains(chunkStart))
        {
            error = "그런 덩이가 없습니다";
            return null;
        }

        // 머리말 뒤 첫 덩이까지의 틈은 그대로 옮긴다(보통 없다).
        int firstStart = ChunkStarts[0];
        var output = new List<byte>(Data.Length + replacement.Length);
        output.AddRange(Data.AsSpan(0, firstStart).ToArray());

        var moved = new Dictionary<int, int>();
        foreach (int start in ChunkStarts)
        {
            moved[start] = output.Count;
            output.AddRange(start == chunkStart ? replacement : Chunk(start));
        }

        if (output.Count > 0xFFFF + 4)
        {
            error = $"파트가 너무 커집니다({output.Count}바이트) — 상대 오프셋이 u16 을 넘습니다";
            return null;
        }

        var result = output.ToArray();
        for (int i = 0; i < Slots.Count; i++)
        {
            BinaryPrimitives.WriteUInt16LittleEndian(
                result.AsSpan(4 + i * 4), (ushort)(moved[Slots[i].Condition] - 4));
            BinaryPrimitives.WriteUInt16LittleEndian(
                result.AsSpan(6 + i * 4), (ushort)(moved[Slots[i].Body] - 4));
        }
        return result;
    }
}
