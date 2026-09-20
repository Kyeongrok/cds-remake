using System.Buffers.Binary;
using CdsHelper.Game.Local.Helpers;

namespace CdsHelper.Game.Engine.Disev;

/// <summary>
/// 덩이 하나의 흐름 — 명령 줄을 <b>갈림 없는 토막</b>으로 묶고 토막 사이를 화살로 잇는다.
/// </summary>
/// <remarks>
/// 토막은 다음 자리에서 끊는다.
/// <list type="bullet">
///   <item>덩이 첫 명령</item>
///   <item>어느 분기·이동이 뛰어 드는 명령</item>
///   <item>분기·이동·끝 명령 바로 뒤 명령</item>
/// </list>
/// 분기(<c>43 xx</c>)는 <b>마름모</b>가 되고, 조건 없는 절대 이동(<c>30 1D</c>)은 화살 하나다. 러너가 그렇듯 조건이 서면
/// 뛰고(<b>예</b>) 아니면 다음 줄로 간다(<b>아니오</b>) — <c>43 47</c> 이면 미니게임·육상전을
/// 이겼을 때가 예다(<see cref="DisevRunner"/>).
/// </remarks>
public static class DisevFlow
{
    public enum NodeKind
    {
        /// <summary>갈림 없이 차례로 도는 명령 묶음.</summary>
        Block,

        /// <summary>분기 명령 하나 — 예/아니오 두 갈래가 나간다.</summary>
        Decision,

        /// <summary>결과 코드·게임 오버·덩이 끝으로 멎는 묶음.</summary>
        End,

        /// <summary>이 덩이 밖으로 뛰는 자리.</summary>
        Outside,
    }

    /// <param name="Id">그래프 안 번호. 만든 차례라 명령 자리 차례와 같다(덩이 밖 자리만 끝에 붙는다).</param>
    /// <param name="Offset">첫 명령의 파트 안 자리. 덩이 밖이면 뛰어 가는 자리.</param>
    /// <param name="Title">마름모·덩이 밖에 적는 말. 묶음은 빈 글이다.</param>
    public sealed record Node(int Id, NodeKind Kind, int Offset, IReadOnlyList<DisevScript.Op> Ops, string Title);

    /// <param name="Label">「예」·「아니오」, 그 밖에는 빈 글.</param>
    /// <param name="Jump">분기가 <b>뛰는</b> 갈래인가 — 흐름도가 마름모 아래 꼭짓점에서 내린다.</param>
    public readonly record struct Edge(int From, int To, string Label, bool Jump = false);

    public sealed record Graph(IReadOnlyList<Node> Nodes, IReadOnlyList<Edge> Edges);

    public const string Yes = "예", No = "아니오";

    /// <summary>대본이 멎는 명령(러너가 <c>Stop</c> 을 내는 것과 덩이 끝).</summary>
    internal static readonly HashSet<string> Ends =
        ["덩이/갈래 끝", "게임 오버", "이벤트 결과 코드 0", "이벤트 결과 코드 1", "이벤트 결과 코드 2"];

    /// <summary>
    /// 조건 없이 뛰는 명령(<c>30 1D [u16 v]</c> → 파트 +4+v, <c>0x0040A48D</c>).
    /// </summary>
    /// <remarks>
    /// 예전에는 <c>43 45</c> 를 조건 없는 이동으로 그렸다. 그것은 「결과가 거짓이면 뜀」이라 마름모다.
    /// </remarks>
    private const string Goto = "절대 이동";

    /// <summary>
    /// 흐름도가 쓰는 뛰는 자리 — 분기(<see cref="TargetOf"/>)에 절대 이동을 더한다. <paramref name="data"/> 는 파트 알맹이라야 한다.
    /// </summary>
    /// <remarks>
    /// 절대 이동은 <see cref="TargetOf"/> 에 안 넣는다 — 나무(<see cref="DisevTree"/>)는 덩이만 떼어 읽어
    /// 자리가 파트 기준이 아니고, 분기처럼 No 길이로 되짜면 값이 틀어진다.
    /// </remarks>
    private static int? FlowTargetOf(byte[] data, DisevScript.Op op)
    {
        if (op.Kind != Goto) return TargetOf(data, op);
        if (op.Offset + 4 > data.Length) return null;
        return 4 + BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(op.Offset + 2));
    }

    /// <summary>
    /// 명령이 뛰어 가는 파트 안 자리. 뛰는 명령이 아니면 null.
    /// </summary>
    /// <remarks>풀이 글과 같은 셈이다 — <c>명령 자리 + 길이 + u16 상대값</c>.</remarks>
    public static int? TargetOf(byte[] data, DisevScript.Op op)
    {
        foreach (var form in DisevScript.Forms)
        {
            if (form.Kind != op.Kind || form.Length != op.Length || form.JumpOffset < 0) continue;

            int at = op.Offset + form.JumpOffset;
            if (at + 2 > data.Length) return null;
            return op.Offset + op.Length + BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(at));
        }
        return null;
    }

    /// <summary>덩이 하나의 명령 줄로 흐름을 짠다.</summary>
    /// <param name="data">파트 알맹이 — 상대 이동값을 여기서 읽는다.</param>
    /// <param name="ops"><see cref="DisevScript.Parse"/> 가 푼 한 덩이 명령 줄.</param>
    public static Graph Build(byte[] data, IReadOnlyList<DisevScript.Op> ops)
    {
        var nodes = new List<Node>();
        var edges = new List<Edge>();
        if (ops.Count == 0) return new Graph(nodes, edges);

        var index = new Dictionary<int, int>();
        for (int i = 0; i < ops.Count; i++) index[ops[i].Offset] = i;
        var targets = ops.Select(op => FlowTargetOf(data, op)).ToArray();

        var leaders = new SortedSet<int> { 0 };
        for (int i = 0; i < ops.Count; i++)
        {
            if (targets[i] is { } t && index.TryGetValue(t, out int j)) leaders.Add(j);
            if ((targets[i] != null || Ends.Contains(ops[i].Kind)) && i + 1 < ops.Count) leaders.Add(i + 1);
        }

        var starts = leaders.ToList();
        var head = new Dictionary<int, int>();                 // 토막 첫 명령 자리(줄 번호) → 노드
        var exits = new List<(int Tail, int Last, int Next)>(); // 토막 끝 노드, 끝 명령 줄, 다음 토막 줄

        for (int b = 0; b < starts.Count; b++)
        {
            int from = starts[b];
            int to = b + 1 < starts.Count ? starts[b + 1] : ops.Count;
            int last = to - 1;
            var lastOp = ops[last];
            bool branch = targets[last] != null && lastOp.Kind != Goto;

            int? first = null, tail = null;
            var body = ops.Skip(from).Take((branch ? last : to) - from).ToList();
            if (body.Count > 0)
            {
                var kind = !branch && Ends.Contains(lastOp.Kind) ? NodeKind.End : NodeKind.Block;
                var block = new Node(nodes.Count, kind, body[0].Offset, body, "");
                nodes.Add(block);
                first = tail = block.Id;
            }

            if (branch)
            {
                var decision = new Node(nodes.Count, NodeKind.Decision, lastOp.Offset, [lastOp], Question(data, lastOp).Title);
                nodes.Add(decision);
                if (tail is { } t) edges.Add(new Edge(t, decision.Id, ""));
                first ??= decision.Id;
                tail = decision.Id;
            }

            head[from] = first!.Value;
            exits.Add((tail!.Value, last, to));
        }

        var outside = new Dictionary<int, int>();
        int NodeAt(int target)
        {
            if (index.TryGetValue(target, out int line) && head.TryGetValue(line, out int id)) return id;
            if (outside.TryGetValue(target, out int known)) return known;

            var node = new Node(nodes.Count, NodeKind.Outside, target, [], $"파트 +0x{target:X} (덩이 밖)");
            nodes.Add(node);
            outside[target] = node.Id;
            return node.Id;
        }

        foreach (var (tail, last, next) in exits)
        {
            var op = ops[last];
            bool hasNext = next < ops.Count;

            if (targets[last] is { } target && op.Kind != Goto)
            {
                var (_, jumpLabel, fallLabel) = Question(data, op);
                edges.Add(new Edge(tail, NodeAt(target), jumpLabel, Jump: true));
                if (hasNext) edges.Add(new Edge(tail, head[next], fallLabel));
            }
            else if (targets[last] is { } jump)
                edges.Add(new Edge(tail, NodeAt(jump), ""));
            else if (!Ends.Contains(op.Kind) && hasNext)
                edges.Add(new Edge(tail, head[next], ""));
        }

        return new Graph(nodes, edges);
    }

    /// <summary>
    /// 마름모에 적을 물음과 두 갈래 이름.
    /// </summary>
    /// <remarks>
    /// <c>43</c> 은 뒤 조건 명령을 셈한 뒤 <b>그 값이 0(거짓)일 때만</b> 뛴다(<c>0x0040B19C</c> 가 뜀 표시만
    /// 세우고 <c>0x0040BCF9</c> 가 가른다). 그래서 아는 조건은 <b>뛰는 쪽이 「예」</b>가 되게 물음을 뒤집어 적는다.
    /// <code>
    ///   43 45        조건 45 = 결과 그대로       → 결과가 거짓(아니오·짐)이면 뛴다
    ///   43 47        조건 47 = 결과가 0         → 이기면(예) 뛴다
    ///   43 12 05     조건 12 05 = 아이템 없음   → 가졌으면 뛴다 (0x00409022)
    ///   43 12 0E     조건 12 0E = 힌트 못 얻음  → 얻었으면 뛴다 (0x00409043)
    ///   43 3A 0B     조건 3A 0B = 0x004AAD80 가 0 → 찾았으면 뛴다
    ///   43 2C 1C ..  조건 2C = 소지금 &lt; 금액 → 금액 이상이면 뛴다 (0x0040A359)
    /// </code>
    /// 모르는 조건은 「거짓이면 뜀」이라고만 적는다.
    /// </remarks>
    internal static (string Title, string Jump, string Fall) Question(byte[] data, DisevScript.Op op)
    {
        int U16(int at) => op.Offset + at + 2 <= data.Length
            ? BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(op.Offset + at)) : 0;
        int Byte(int at) => op.Offset + at < data.Length ? data[op.Offset + at] : 0;

        return op.Kind switch
        {
            "결과 참 시 이동" => ("결과가 참(예·이김)인가", Yes, No),
            "결과 거짓 시 이동" => ("결과가 거짓(아니오·짐)인가", Yes, No),
            "이전 조건 참 시 이동" => ("앞 조건이 참이었나", Yes, No),
            "부관 고용 시 이동" => ("부관이 있나", Yes, No),
            "선택지 분기" => ($"고른 값이 {Byte(3)} 이 아닌가", Yes, No),
            "아이템 조건 분기" => ($"아이템 {U16(3)} 가졌나", Yes, No),
            "아이템 미소지 분기" => ($"아이템 {U16(3)} 없나", Yes, No),
            "힌트 조건 분기" => ($"힌트 {U16(3)} 얻었나", Yes, No),
            "힌트 미활성 분기" => ($"힌트 {U16(3)} 없나", Yes, No),
            "발견물 조건 분기" => ($"발견물 {U16(3)} 찾았나", Yes, No),
            "미발견 분기" => ($"발견물 {U16(3)} 못 찾았나", Yes, No),
            "기준 연도 분기" => ($"{U16(3)}년 전인가", Yes, No),
            "연도 상한 분기" => ($"{U16(3)}년 뒤인가", Yes, No),
            "연도 범위 분기" => ($"{U16(3)}~{U16(6)}년 밖인가", Yes, No),
            "도시 분기" => ($"지금 도시가 {U16(3)} 이 아닌가", Yes, No),
            DisevScript.CompareKind => ($"{Condition(op).Replace(" 이면 이동", "")} 인가", Yes, No),
            _ => ($"{Condition(op)} — 거짓이면 뜀", "거짓", "참"),
        };
    }

    /// <summary>마름모에 적을 말 — 풀이에서 상대 이동 꼬리를 뗀 것이다.</summary>
    private static string Condition(DisevScript.Op op)
    {
        int cut = op.Text.IndexOf(", 상대 ", StringComparison.Ordinal);
        return cut > 0 ? op.Text[..cut] : op.Kind;
    }
}
