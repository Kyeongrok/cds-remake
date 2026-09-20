using System.Buffers.Binary;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using CdsHelper.Game.Local.Helpers;

namespace CdsHelper.Game.Engine.Disev;

/// <summary>
/// 파트 하나를 <b>함수 호출 줄 배열</b>로 — <c>발견이벤트.json</c> 의 <c>Chunks</c> 다.
/// </summary>
/// <remarks>
/// 대본은 작은 가상 기계 코드다. 명령 하나가 게임 함수 한 번 부르기고(<see cref="DisevCall"/>), 분기는 조건식을 불러
/// 그 값으로 뛴다. 그래서 줄을 원본 차례 그대로 늘어놓고 <b>호출 이름 + 인자</b>로 적는다.
/// <code>
///   { "OpCode": "00 02", "Call": "PlayVideo", "Args": { "Id": 55 } }
///   { "OpCode": "43 2E", "GotoUnless": { "Call": "EqualTo", "Args": { "A": { "Stat": 5 }, "B": { "Const": 2 } } }, "Target": "L017A" }
///   { "OpCode": "00 0A", "Call": "Say", "Args": { "Speaker": "부관", "Text": "계시를 받으시겠습니까?" } }
///   { "OpCode": "30 1D", "Goto": "L01A9" }
///   { "Label": "L017A", "OpCode": "0B 0A", "Call": "AskYesNo", "Args": { "Speaker": "부관", "Text": "신의 계시를 받으시겠습니까?" } }
///   { "OpCode": "43 47", "GotoIf": { "Call": "Result" }, "Target": "L01A9" }
/// </code>
/// <list type="bullet">
///   <item><c>Call</c>·<c>Args</c> — 호출 이름과 인자. 바이트 꼴은 <see cref="DisevCalls"/> 표 하나가 짝짓는다.
///         뜻을 모르는 바이트는 <c>{ "Call": "Raw", "Args": { "Bytes": "…" } }</c> 다.</item>
///   <item><c>GotoIf</c>·<c>GotoUnless</c> + <c>Target</c> — 분기(<c>43</c>). 조건식 호출이 참이면/거짓이면 라벨로 뛴다.
///         원본은 늘 「거짓이면 뜀」이라, 거꾸로 이름이 있는 조건식은 <c>GotoIf</c> 로 적는다
///         (<c>43 12 0E</c> = 조건 「힌트 없음」 → <c>GotoIf HintActive</c>).</item>
///   <item><c>Goto</c> — 절대 이동(<c>30 1D</c>, 파트 +4 기준).</item>
///   <item><c>Label</c> — 어느 점프가 여기로 오면 붙는다. 이름은 원본 파트 안 자리(<c>L</c>+16진 넷)다.</item>
///   <item><c>OpCode</c>·<c>Note</c> — 사람이 읽으라고 붙인다. 읽을 때 안 본다(바이트는 Call·Args 가 정한다).</item>
/// </list>
/// 되짤 때 분기는 명령 끝에서 잰 상대 거리, 절대 이동은 파트 +4 기준 자리를 다시 세므로 어느 줄의 길이를 바꿔도
/// 점프가 안 어긋난다. 점프가 명령 머리가 아닌 자리로 가면 라벨을 못 달아 <c>Raw</c> 로 둔다.
/// 되짠 파트가 원본과 한 바이트라도 다르면 부른 쪽(<see cref="DisevBook"/>)이 파트 통째 16진으로 둔다.
///
/// 옛 판의 줄(나무 <c>If</c>·<c>Yes</c>·<c>No</c>, 칸 <c>Say</c>·<c>Sound</c> …, <c>OpCode</c>+<c>Goto</c>, 16진 글)도 그대로 읽힌다.
/// </remarks>
public static class DisevTree
{
    /// <summary>라벨 이름 — 원본 파트 안 자리.</summary>
    private static string LabelOf(int offset) => $"L{offset:X4}";

    /// <summary>
    /// 파트의 덩이들을 평평한 줄 배열로 푼다. 차례는 <see cref="DisevPart.ChunkStarts"/> 그대로다.
    /// </summary>
    public static List<List<DisevLine>> BuildPart(DisevPart part)
    {
        var data = part.Data;
        var chunks = new List<List<DisevScript.Op>>();
        var heads = new HashSet<int>();
        foreach (int start in part.ChunkStarts)
        {
            var (from, to) = part.ChunkRange(start);
            var ops = DisevScript.Parse(data, from, to);
            chunks.Add(ops);
            foreach (var op in ops) heads.Add(op.Offset);
        }

        // 먼저 점프 자리를 모은다 — 명령 머리에 떨어지는 것만 라벨이 된다.
        var targets = new Dictionary<int, int>();
        foreach (var ops in chunks)
            foreach (var op in ops)
                if (JumpTargetOf(data, op) is { } target && heads.Contains(target))
                    targets[op.Offset] = target;
        var labelled = targets.Values.ToHashSet();

        var result = new List<List<DisevLine>>(chunks.Count);
        foreach (var ops in chunks)
        {
            var lines = new List<DisevLine>(ops.Count);
            foreach (var op in ops)
            {
                var line = LineOf(data, op, targets.TryGetValue(op.Offset, out int t) ? t : null);
                if (labelled.Contains(op.Offset)) line.Label = LabelOf(op.Offset);
                lines.Add(line);
            }
            result.Add(lines);
        }
        return result;
    }

    /// <summary>
    /// 명령이 뛰는 파트 안 자리 — 분기(<see cref="DisevFlow.TargetOf"/>)와 절대 이동(<c>30 1D</c> → +4+v).
    /// </summary>
    private static int? JumpTargetOf(byte[] data, DisevScript.Op op)
    {
        if (IsAbsoluteGoto(op)) return 4 + BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(op.Offset + 2));
        return DisevFlow.TargetOf(data, op);
    }

    private static bool IsAbsoluteGoto(DisevScript.Op op) =>
        op.Kind == "절대 이동" && op.Length == 4;

    /// <summary>명령 하나를 호출 줄로. 표에 없거나 되짜서 같지 않으면 <c>Raw</c> 다.</summary>
    private static DisevLine LineOf(byte[] data, DisevScript.Op op, int? target)
    {
        var raw = data.AsSpan(op.Offset, Math.Min(op.Length, data.Length - op.Offset)).ToArray();
        string note = NoteOf(op);

        if (target is { } to)
        {
            if (IsAbsoluteGoto(op)) return new DisevLine { Hex = "30 1D", Goto = LabelOf(to), Note = note };

            // 점프값(끝 두 바이트)을 뗀 머리가 43 [조건식] 이면 조건식 호출로 푼다.
            if (DisevCalls.DecodeBranch(raw[..^2]) is { } branch)
            {
                var condition = ConditionNode(branch.Branch.Condition, branch.Branch.Args);
                return new DisevLine
                {
                    Hex = branch.OpCode,
                    GotoIf = branch.Branch.If ? condition : null,
                    GotoUnless = branch.Branch.If ? null : condition,
                    Target = LabelOf(to),
                    Note = note,
                };
            }
        }

        if (target == null && DisevCalls.Decode(raw) is { } call)
        {
            // 대사 무리는 Args.Text 가 곧 풀이라 Note 를 또 달지 않는다.
            bool speech = call.Call is DisevCall.Say or DisevCall.AskYesNo or DisevCall.SayBare or DisevCall.AskChoice
                or DisevCall.AskChoiceWide or DisevCall.SetDiscoveryName or DisevCall.InputDiscoveryName
                or DisevCall.AddCityRumor or DisevCall.AddCultureRumor;
            return new DisevLine
            {
                Hex = call.OpCode, Call = call.Call, Args = call.Args.Count > 0 ? call.Args : null, Note = speech ? null : note,
            };
        }

        return new DisevLine
        {
            Call = DisevCall.Raw,
            Args = new JsonObject { ["Bytes"] = DisevScript.Hex(raw) },
            Note = note,
        };
    }

    private static JsonObject ConditionNode(DisevCall condition, JsonObject args)
    {
        var node = new JsonObject { ["Call"] = condition.ToString() };
        if (args.Count > 0) node["Args"] = args;
        return node;
    }

    /// <summary>줄에 붙일 풀이 — 원본 자리에 매인 꼬리(상대 +0x… → 파트 +0x…)는 뗀다. 고치면 틀리기 때문이다.</summary>
    private static string NoteOf(DisevScript.Op op)
    {
        string text = op.Text;
        int cut = text.IndexOf(", 상대 ", StringComparison.Ordinal);
        if (cut < 0) cut = text.IndexOf(" → 파트 ", StringComparison.Ordinal);
        return cut > 0 ? text[..cut] : text;
    }

    /// <summary>
    /// 줄 배열을 덩이 날바이트들로 되짠다 — 라벨을 찾아 점프값을 다시 센다.
    /// </summary>
    /// <param name="chunks">덩이마다 줄 배열.</param>
    /// <param name="headerEnd">첫 덩이가 서는 파트 안 자리(머리말 뒤).</param>
    /// <param name="error">못 짰으면 까닭.</param>
    public static List<byte[]>? FlattenPart(IReadOnlyList<List<DisevLine>> chunks, int headerEnd, out string error)
    {
        error = "";

        // 1) 줄마다 바이트(점프는 자리만 비워 둔다)와 자리를 잡는다.
        var pieces = new List<List<(DisevLine Line, List<byte> Bytes, int At)>>(chunks.Count);
        var labels = new Dictionary<string, int>();
        int at = headerEnd;
        for (int c = 0; c < chunks.Count; c++)
        {
            var list = new List<(DisevLine, List<byte>, int)>();
            foreach (var line in chunks[c])
            {
                var bytes = new List<byte>();
                if (line.GotoIf != null || line.GotoUnless != null)
                {
                    var node = line.GotoIf ?? line.GotoUnless!;
                    if (!Enum.TryParse(node["Call"]?.GetValue<string>(), out DisevCall condition))
                    {
                        error = $"분기 조건식 이름을 모릅니다: {node["Call"]}";
                        return null;
                    }
                    if (DisevCalls.EncodeBranch(line.GotoIf != null, condition, node["Args"] as JsonObject, out error)
                        is not { } head) return null;
                    bytes.AddRange(head);
                    bytes.Add(0);
                    bytes.Add(0);
                }
                else if (line.Goto != null)
                {
                    // 새 판은 30 1D 만 Goto 로 적는다. 판 8·9 는 분기 머리를 OpCode 에 넣고 Goto 를 달았다.
                    byte[] head = line.Call == null && DisevScript.ParseHex(line.Hex ?? "") is { Length: > 0 } old ? old : [0x30, 0x1D];
                    bytes.AddRange(head);
                    bytes.Add(0);
                    bytes.Add(0);
                }
                else if (line.Call is { } call)
                {
                    if (DisevCalls.Encode(call, line.Args, out error) is not { } built) return null;
                    bytes.AddRange(built);
                }
                else if (!Append(bytes, [line], ref error))
                {
                    return null;
                }

                if (line.Label != null && !labels.TryAdd(line.Label, at))
                {
                    error = $"라벨이 겹칩니다: {line.Label}";
                    return null;
                }
                list.Add((line, bytes, at));
                at += bytes.Count;
            }
            pieces.Add(list);
        }

        // 2) 점프값을 채운다.
        var output = new List<byte[]>(pieces.Count);
        foreach (var list in pieces)
        {
            var chunk = new List<byte>();
            foreach (var (line, bytes, start) in list)
            {
                if ((line.Target ?? line.Goto) is { } label)
                {
                    if (!labels.TryGetValue(label, out int target))
                    {
                        error = $"없는 라벨로 뜁니다: {label}";
                        return null;
                    }
                    bool absolute = bytes.Count == 4 && bytes[0] == 0x30 && bytes[1] == 0x1D;
                    int value = absolute ? target - 4 : target - (start + bytes.Count);
                    if (value is < 0 or > ushort.MaxValue)
                    {
                        error = $"{label} 로 뛸 수 없습니다 — " + (absolute ? "파트 앞쪽 밖" : "뒤로 뛰는 분기");
                        return null;
                    }
                    bytes[^2] = (byte)value;
                    bytes[^1] = (byte)(value >> 8);
                }
                chunk.AddRange(bytes);
            }
            output.Add(chunk.ToArray());
        }
        return output;
    }

    // 발견이벤트.json 과 같은 꼴 — 들여 쓰고 한글을 \uXXXX 로 안 깬다.
    private static readonly JsonSerializerOptions Pretty = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>줄 나무를 <c>발견이벤트.json</c> 에 적히는 꼴 그대로 글로 — 편집기 JSON 탭이 쓴다.</summary>
    public static string ToJson(IReadOnlyList<DisevLine> lines) => JsonSerializer.Serialize(lines, Pretty);

    /// <summary>줄 나무를 덩이 날바이트로 되짠다. 분기의 상대값은 No 길이로 다시 셈한다.</summary>
    public static byte[]? Flatten(IReadOnlyList<DisevLine> lines, out string error)
    {
        error = "";
        var output = new List<byte>();
        return Append(output, lines, ref error) ? output.ToArray() : null;
    }

    /// <summary>화자 이름 → 태그 16진(띄어쓰기 없음). 이름이 겹치면 먼저 것.</summary>
    private static readonly Dictionary<string, string> SpeakerTags = BuildSpeakerTags();

    /// <summary>화자 이름의 태그 바이트. 모르는 이름이면 null.</summary>
    public static byte[]? SpeakerTagOf(string name) =>
        SpeakerTags.TryGetValue(name, out var tag) ? Convert.FromHexString(tag) : null;

    private static Dictionary<string, string> BuildSpeakerTags()
    {
        var tags = new Dictionary<string, string>();
        foreach (var (tag, name) in DisevScript.SpeakerNames) tags.TryAdd(name, tag);
        // 監察官 은 예전에 「검사관」으로 적었다 — 그때 적어 둔 발견이벤트.json 도 읽히게 옛 이름을 남긴다.
        if (tags.TryGetValue("감찰관", out var inspector)) tags.TryAdd("검사관", inspector);
        return tags;
    }

    private static bool Append(List<byte> output, IReadOnlyList<DisevLine> lines, ref string error)
    {
        foreach (var line in lines)
        {
            if (line.Sound is { } sound)
            {
                output.AddRange([0x0E, 0x03, (byte)sound, (byte)(sound >> 8)]);
                continue;
            }

            if (line.EvStill is { } still)
            {
                output.AddRange([0x00, 0x1F, (byte)still, (byte)(still >> 8)]);
                continue;
            }

            if (line.Discover is { } found)
            {
                output.AddRange([0x01, 0x0B, (byte)found, (byte)(found >> 8)]);
                continue;
            }

            if (line.GetItem is { } item)
            {
                output.AddRange([0x00, 0x05, (byte)item, (byte)(item >> 8)]);
                continue;
            }

            if (line.Avi is { } avi)
            {
                output.AddRange([0x00, 0x02, (byte)avi, (byte)(avi >> 8)]);
                continue;
            }

            if (line.Encounter is { } encounter)
            {
                output.AddRange([0x00, 0x1E, (byte)encounter, (byte)(encounter >> 8)]);
                continue;
            }

            if (line.Stat is { } stat)
            {
                if ((line.Add == null) == (line.Sub == null))
                {
                    error = $"능력치 {stat}: Add 와 Sub 가운데 하나만 적어야 합니다";
                    return false;
                }
                long amount = line.Add ?? line.Sub!.Value;
                if (stat is < 0 or > ushort.MaxValue || amount is < 0 or > uint.MaxValue)
                {
                    error = $"능력치 {stat}: 번호는 0~65535, 값은 0~4294967295 라야 합니다";
                    return false;
                }
                output.AddRange([line.Add != null ? (byte)0x19 : (byte)0x1A, 0x1C, (byte)stat, (byte)(stat >> 8), 0x1A,
                                 (byte)amount, (byte)(amount >> 8), (byte)(amount >> 16), (byte)(amount >> 24)]);
                continue;
            }

            if (line.Say is { } say)
            {
                byte[]? tag = line.Speaker != null
                    ? SpeakerTags.TryGetValue(line.Speaker, out var known) ? Convert.FromHexString(known) : null
                    : line.SpeakerTag != null ? DisevScript.ParseHex(line.SpeakerTag) : [];
                if (tag == null)
                {
                    error = $"화자를 모릅니다: {line.Speaker ?? line.SpeakerTag}";
                    return false;
                }
                if (line.Flag is < 0 or > 255)
                {
                    error = $"창 플래그는 0 ~ 255 라야 합니다: {line.Flag}";
                    return false;
                }
                output.AddRange(DisevForm.BuildDialogue(line.Flag, tag, say));
                continue;
            }

            if (line.If == null)
            {
                if (DisevScript.ParseHex(line.Hex ?? "") is not { } bytes)
                {
                    error = $"줄을 못 읽었습니다: {line.Hex}";
                    return false;
                }
                output.AddRange(bytes);
                continue;
            }

            if (DisevScript.ParseHex(line.If) is not { Length: > 0 } head)
            {
                error = $"분기 머리를 못 읽었습니다: {line.If}";
                return false;
            }

            var no = new List<byte>();
            if (!Append(no, line.No ?? [], ref error)) return false;
            if (no.Count > ushort.MaxValue)
            {
                error = $"분기 No 가 너무 깁니다({no.Count}바이트)";
                return false;
            }

            output.AddRange(head);
            output.Add((byte)no.Count);
            output.Add((byte)(no.Count >> 8));
            output.AddRange(no);
            if (!Append(output, line.Yes ?? [], ref error)) return false;
        }
        return true;
    }
}

/// <summary>
/// 대본 한 줄 — 명령 하나다(옛 판에서 읽은 나무 분기면 <see cref="If"/> 가 선다).
/// </summary>
/// <remarks>
/// JSON 에서 날바이트 줄은 <b>16진 글 하나</b>, 분기는
/// <c>{ "If", "Note", "Yes": [...], "No": [...] }</c>, 칸으로 푼 명령은 <c>{ "Sound" }</c> ·
/// <c>{ "EvStill" }</c> · <c>{ "Speaker", "Flag", "Say" }</c> 이다. <c>Note</c> 는 사람이 읽으라고
/// 적는 것이라 읽을 때 안 본다.
///
/// 칸으로 못 푼 명령은 <c>{ "OpCode", "Note" }</c> 객체로 적는다 — <c>OpCode</c> 가 그 명령의
/// 바이트 전부고 <c>Note</c> 가 풀이다(「델포이 신탁 출력」 따위). 이름 붙인 명령이면 <c>Command</c> 가 붙는다.
/// 풀이·이름은 읽을 때 안 본다.
/// 16진 글 하나로 적힌 옛 줄과 <c>Hex</c> 키로 적힌 판 6 줄도 그대로 읽힌다.
/// </remarks>
[JsonConverter(typeof(DisevLineConverter))]
public sealed class DisevLine
{
    /// <summary>날바이트 줄. 다른 갈래면 null.</summary>
    public string? Hex { get; set; }

    /// <summary>호출 이름(<see cref="DisevCall"/>).</summary>
    public DisevCall? Call { get; set; }

    /// <summary>호출 인자.</summary>
    public JsonObject? Args { get; set; }

    /// <summary>분기 — 이 조건식 호출이 참이면 <see cref="Target"/> 으로 뛴다.</summary>
    public JsonObject? GotoIf { get; set; }

    /// <summary>분기 — 이 조건식 호출이 거짓이면 <see cref="Target"/> 으로 뛴다.</summary>
    public JsonObject? GotoUnless { get; set; }

    /// <summary>분기가 뛰는 라벨.</summary>
    public string? Target { get; set; }

    /// <summary>이 줄의 라벨 — 어느 점프가 여기로 온다.</summary>
    public string? Label { get; set; }

    /// <summary>절대 이동(<c>30 1D</c>)이 가는 라벨. 판 8·9 에서는 분기도 이것에 적고 머리를 <see cref="Hex"/> 에 두었다.</summary>
    public string? Goto { get; set; }

    /// <summary>음원 재생(<c>0E 03</c>) 슬롯.</summary>
    public int? Sound { get; set; }

    /// <summary>EVSTILL 이미지 표시(<c>00 1F</c>) 슬롯.</summary>
    public int? EvStill { get; set; }

    /// <summary>발견물 등록/발견 처리(<c>01 0B</c>) 발견물 번호.</summary>
    public int? Discover { get; set; }

    /// <summary>아이템 획득(<c>00 05</c>) 아이템 번호.</summary>
    public int? GetItem { get; set; }

    /// <summary>AVI 재생(<c>00 02</c>) 슬롯.</summary>
    public int? Avi { get; set; }

    /// <summary>특수 조우 연출(<c>00 1E</c>) 번호(<see cref="DisevScript.Encounters"/>).</summary>
    public int? Encounter { get; set; }

    /// <summary>능력치 더하기·빼기(<c>19|1A 1C</c>)의 능력치 번호.</summary>
    public int? Stat { get; set; }

    /// <summary>능력치 이름(<see cref="DisevScript.StatNames"/>). 적을 때만 쓰고 읽을 때는 안 본다.</summary>
    public string? StatName { get; set; }

    /// <summary>더할 값(<c>19</c>).</summary>
    public long? Add { get; set; }

    /// <summary>뺄 값(<c>1A</c>).</summary>
    public long? Sub { get; set; }

    /// <summary>대사 본문 — 무손실로 푼 글(<see cref="DisevForm.BuildDialogue"/> 가 되돌린다).</summary>
    public string? Say { get; set; }

    /// <summary>대사 화자 이름(<see cref="DisevScript.SpeakerNames"/>).</summary>
    public string? Speaker { get; set; }

    /// <summary>이름을 모르는 화자 태그 16진.</summary>
    public string? SpeakerTag { get; set; }

    /// <summary>대사 창 플래그. null 이면 <c>0A</c> 로 바로 연다. JSON 에서 키가 없으면 0 이다.</summary>
    public int? Flag { get; set; }

    /// <summary>분기 머리 — 상대값 두 바이트를 뗀 명령 바이트. 날바이트 줄이면 null.</summary>
    public string? If { get; set; }

    /// <summary>분기 풀이. 적을 때만 쓴다.</summary>
    public string? Note { get; set; }

    /// <summary>조건 명령이 뛴 쪽.</summary>
    public List<DisevLine>? Yes { get; set; }

    /// <summary>뛰지 않고 다음 줄로 간 쪽.</summary>
    public List<DisevLine>? No { get; set; }
}

/// <summary>
/// <see cref="DisevLine"/> 을 글 하나 또는 분기 객체로 적고 읽는다.
/// </summary>
/// <remarks>
/// <c>Chunks</c> 한 칸도 이것으로 읽는다 — 판 2 파일은 덩이가 <b>글 하나</b>였으므로
/// 글이 오면 한 줄짜리 나무로 받는다(<see cref="DisevChunkConverter"/>).
/// </remarks>
public sealed class DisevLineConverter : JsonConverter<DisevLine>
{
    public override DisevLine? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.String) return new DisevLine { Hex = reader.GetString() };
        if (reader.TokenType != JsonTokenType.StartObject) throw new JsonException("줄은 글이나 분기 객체라야 합니다");

        // Flag 는 흔한 0 을 안 적으므로 키가 없으면 0 이다. 플래그 바이트가 없는 대사만 null 로 적힌다.
        var line = new DisevLine { Flag = 0 };
        while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
        {
            string? name = reader.GetString();
            reader.Read();
            switch (name)
            {
                case "Hex":
                case "OpCode": line.Hex = reader.GetString(); break;
                case "Label": line.Label = reader.GetString(); break;
                case "Call":
                    if (!Enum.TryParse(reader.GetString(), out DisevCall call))
                        throw new JsonException($"모르는 호출입니다: {reader.GetString()}");
                    line.Call = call;
                    break;
                case "Args": line.Args = JsonNode.Parse(ref reader) as JsonObject; break;
                case "GotoIf": line.GotoIf = JsonNode.Parse(ref reader) as JsonObject; break;
                case "GotoUnless": line.GotoUnless = JsonNode.Parse(ref reader) as JsonObject; break;
                case "Target": line.Target = reader.GetString(); break;
                // Category(판 6~8)·Command 는 읽기용이라 안 본다 — 바이트는 OpCode 와 칸이 정한다.
                case "Goto": line.Goto = reader.GetString(); break;
                case "Sound": line.Sound = reader.GetInt32(); break;
                case "EvStill": line.EvStill = reader.GetInt32(); break;
                case "Discover": line.Discover = reader.GetInt32(); break;
                case "GetItem": line.GetItem = reader.GetInt32(); break;
                case "Avi": line.Avi = reader.GetInt32(); break;
                case "Encounter": line.Encounter = reader.GetInt32(); break;
                case "Stat": line.Stat = reader.GetInt32(); break;
                case "Add": line.Add = reader.GetInt64(); break;
                case "Sub": line.Sub = reader.GetInt64(); break;
                case "Say": line.Say = reader.GetString(); break;
                case "Speaker": line.Speaker = reader.GetString(); break;
                case "SpeakerTag": line.SpeakerTag = reader.GetString(); break;
                case "Flag": line.Flag = reader.TokenType == JsonTokenType.Null ? null : reader.GetInt32(); break;
                case "If": line.If = reader.GetString(); break;
                case "Note": line.Note = reader.GetString(); break;
                case "Yes": line.Yes = JsonSerializer.Deserialize<List<DisevLine>>(ref reader, options) ?? []; break;
                case "No": line.No = JsonSerializer.Deserialize<List<DisevLine>>(ref reader, options) ?? []; break;
                default: reader.Skip(); break;
            }
        }
        if (line.Call == null && line.GotoIf == null && line.GotoUnless == null && line.Goto == null &&
            line.If == null && line.Hex == null && line.Sound == null && line.EvStill == null && line.Discover == null &&
            line.GetItem == null && line.Avi == null && line.Encounter == null && line.Stat == null && line.Say == null)
            throw new JsonException("줄 객체에 Call · GotoIf · GotoUnless · Goto · If · OpCode · Sound · EvStill · Discover · GetItem · Avi · Encounter · Stat · Say 가운데 하나가 있어야 합니다");
        if (line.If != null)
        {
            line.Yes ??= [];
            line.No ??= [];
        }
        // 대사는 OpCode 가 곧 명령이다 — 00 0A · 0B 0A · 0A. 옛 판은 Flag 로 적었다.
        if (line.Say != null && line.Hex != null)
        {
            var head = DisevScript.ParseHex(line.Hex);
            line.Flag = head switch
            {
                [0x0A] => null,
                [var flag, 0x0A] => flag,
                _ => throw new JsonException($"대사 OpCode 는 00 0A · 0B 0A · 0A 가운데 하나라야 합니다: {line.Hex}"),
            };
        }
        return line;
    }

    public override void Write(Utf8JsonWriter writer, DisevLine value, JsonSerializerOptions options)
    {
        // 옛 판에서 읽어 들인 나무 분기 — 그대로 적는다.
        if (value.If != null)
        {
            writer.WriteStartObject();
            writer.WriteString("If", value.If);
            if (value.Note != null) writer.WriteString("Note", value.Note);
            writer.WritePropertyName("Yes");
            JsonSerializer.Serialize(writer, value.Yes ?? [], options);
            writer.WritePropertyName("No");
            JsonSerializer.Serialize(writer, value.No ?? [], options);
            writer.WriteEndObject();
            return;
        }

        // 새 판 줄 — 호출·분기·절대 이동.
        if (value.Call != null || value.GotoIf != null || value.GotoUnless != null || (value.Goto != null && value.Say == null))
        {
            writer.WriteStartObject();
            if (value.Label != null) writer.WriteString("Label", value.Label);
            if (value.Hex != null) writer.WriteString("OpCode", value.Hex);
            if (value.Call is { } call) writer.WriteString("Call", call.ToString());
            if (value.Args != null) { writer.WritePropertyName("Args"); value.Args.WriteTo(writer); }
            if (value.GotoIf != null) { writer.WritePropertyName("GotoIf"); value.GotoIf.WriteTo(writer); }
            if (value.GotoUnless != null) { writer.WritePropertyName("GotoUnless"); value.GotoUnless.WriteTo(writer); }
            if (value.Target != null) writer.WriteString("Target", value.Target);
            if (value.Goto != null) writer.WriteString("Goto", value.Goto);
            if (value.Note != null) writer.WriteString("Note", value.Note);
            writer.WriteEndObject();
            return;
        }

        // 옛 판에서 읽어 들인 줄 — 날바이트는 글 하나, 칸으로 푼 것은 그 칸대로.
        bool structured = value.Sound != null || value.EvStill != null || value.Discover != null || value.GetItem != null
                          || value.Avi != null || value.Encounter != null || value.Stat != null || value.Say != null;
        if (!structured)
        {
            writer.WriteStringValue(value.Hex ?? "");
            return;
        }

        writer.WriteStartObject();
        if (value.Label != null) writer.WriteString("Label", value.Label);
        if (value.Hex != null) writer.WriteString("OpCode", value.Hex);
        else if (value.Say != null) writer.WriteString("OpCode", value.Flag is { } f ? $"{f:X2} 0A" : "0A");
        if (value.Sound is { } sound) writer.WriteNumber("Sound", sound);
        if (value.EvStill is { } still) writer.WriteNumber("EvStill", still);
        if (value.Discover is { } found) writer.WriteNumber("Discover", found);
        if (value.GetItem is { } item) writer.WriteNumber("GetItem", item);
        if (value.Avi is { } avi) writer.WriteNumber("Avi", avi);
        if (value.Encounter is { } encounter) writer.WriteNumber("Encounter", encounter);
        if (value.Stat is { } stat)
        {
            writer.WriteNumber("Stat", stat);
            if (value.Add is { } add) writer.WriteNumber("Add", add);
            if (value.Sub is { } sub) writer.WriteNumber("Sub", sub);
        }
        if (value.Say is { } say)
        {
            if (value.Speaker != null) writer.WriteString("Speaker", value.Speaker);
            if (value.SpeakerTag != null) writer.WriteString("SpeakerTag", value.SpeakerTag);
            writer.WriteString("Say", say);
        }
        if (value.Note != null) writer.WriteString("Note", value.Note);
        writer.WriteEndObject();
    }

}

/// <summary>덩이 한 칸 — 판 3 은 줄 배열, 판 2 는 16진 글 하나다.</summary>
public sealed class DisevChunkConverter : JsonConverter<List<DisevLine>>
{
    public override List<DisevLine>? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.String) return [new DisevLine { Hex = reader.GetString() }];

        var lines = new List<DisevLine>();
        if (reader.TokenType != JsonTokenType.StartArray) throw new JsonException("덩이는 글이나 배열이라야 합니다");
        while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
            lines.Add(JsonSerializer.Deserialize<DisevLine>(ref reader, options)!);
        return lines;
    }

    public override void Write(Utf8JsonWriter writer, List<DisevLine> value, JsonSerializerOptions options)
    {
        writer.WriteStartArray();
        foreach (var line in value) JsonSerializer.Serialize(writer, line, options);
        writer.WriteEndArray();
    }
}

/// <summary><c>Chunks</c> 목록 — 칸마다 <see cref="DisevChunkConverter"/> 로 읽는다.</summary>
public sealed class DisevChunksConverter : JsonConverter<List<List<DisevLine>>>
{
    private static readonly DisevChunkConverter Chunk = new();

    public override List<List<DisevLine>>? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null) return null;
        if (reader.TokenType != JsonTokenType.StartArray) throw new JsonException("Chunks 는 배열이라야 합니다");

        var chunks = new List<List<DisevLine>>();
        while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
            chunks.Add(Chunk.Read(ref reader, typeof(List<DisevLine>), options) ?? []);
        return chunks;
    }

    public override void Write(Utf8JsonWriter writer, List<List<DisevLine>> value, JsonSerializerOptions options)
    {
        writer.WriteStartArray();
        foreach (var chunk in value) Chunk.Write(writer, chunk, options);
        writer.WriteEndArray();
    }
}
