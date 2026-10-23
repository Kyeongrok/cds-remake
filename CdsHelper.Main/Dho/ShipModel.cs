// DHO 저장소(C:\Users\ocean\git\dho, src/Dho/Render)에서 옮겨 온 것이다 — 편집기 「DHO 배 찍기」의 3D 미리보기가 쓴다. 고칠 때는 그쪽과 맞춘다.
using System.IO;
using System.Buffers.Binary;
using System.Numerics;
using Dho.Data;
using Vortice.Direct3D11;

namespace Dho.Render;

/// <summary>
/// 배 모형 — <c>0001\sh0000.bin</c> 의 <c>XKMD</c> 항목 한 벌(선체 하나와 뒤따르는 돛 셋).
/// </summary>
/// <remarks>
/// XKMD 머리(0x80): +0x2C u16×10 개수 [정점버퍼, 행렬, 마디, 재질, 텍스처단, 0, 부분, 0, 부분B, 0],
/// +0x4C u32×13 구역 자리 [0 정점버퍼 설명, …, 8 부분, 9 부분B, 10 색인, 11 정점].
/// 정점버퍼 설명(0x34): u32 FVF, u32 한 정점 바이트, u32 정점 수, u32 정점 구역 안 자리, …, +0x20 u32 색인 수, u32 색인 구역 안 자리.
/// 부분: 구역 처음에 u32 자리 × 개수, 레코드 +6 u16 정점버퍼 번호, +0x2C u32 도형(5 = 삼각형 띠),
/// +0x38 u32 색인 시작, u32 도형 수. 색인은 정점버퍼 처음부터 센 번호다.
/// 정점은 D3D9 FVF 차례 — 0x112 = 자리·법선·UV, 0x316 = 자리·가중치·법선·UV×3(돛 천).
/// 좌표: y 가 위, 뱃머리가 +z, 물에 잠기는 선이 y ≈ 0. 길이는 12000 남짓이다.
/// </remarks>
internal sealed class ShipModel : IDisposable
{

    private const int FvfSail = 0x316;

    private readonly List<(Mesh Mesh, ID3D11ShaderResourceView? Texture, Vector4 Tint)> _parts = [];
    private ID3D11ShaderResourceView _hullTexture;
    private int _hullBase;

    /// <summary>
    /// 선체 그림을 바꾼다 — <c>sh0004.bin</c> 의 0 ~ 2 번(<c>SHIP_BASE_001 · 101 · 201</c>). 셋은 같은 자리의 그림인데 널빤지 쪽만 다르다:
    /// 001 은 잿빛 나무(재질 빛깔을 곱해 쓴다), 101 은 짙게 칠한 널, 201 은 밝게 칠한 널.
    /// </summary>
    public void SetHullBase(int which)
    {
        // 0 SHIP_BASE_001(잿빛 나무 — 옛 방식), 1 101(제 빛의 나무), 2 201(칠을 받는 잿빛 널), 3 401 · 4 002 · 5 202(비늘 따위 재질의 덧판 — 재질 줄의 텍스처 칸 308 · 419 · 420)
        which = Math.Clamp(which, 0, 5);
        if (which == _hullBase) return;
        int resource = which switch { 3 => 308, 4 => 419, 5 => 420, _ => which };
        if (_index == null || !_index.TryGetValue((0x301, resource), out var at)) return;
        ID3D11ShaderResourceView next;
        try { next = GameTexture.FromMftf(_device, new Pack(at.Pack).Entry(at.Entry)); } catch (Exception) { return; }
        for (int i = 0; i < _parts.Count; i++)
            if (_parts[i].Texture == _hullTexture) _parts[i] = (_parts[i].Mesh, next, _parts[i].Tint);
        _hullTexture.Dispose();
        (_hullTexture, _hullBase) = (next, which);
    }
    private ID3D11ShaderResourceView _sailTexture;
    private ID3D11ShaderResourceView? _decoTexture, _yardTexture;
    // 돛 위의 문장 — 돛 천의 둘째 UV 벌(0 ~ 1 의 네모가 문장 자리)로 만든 면들과, 단 문장의 그림
    private ID3D11ShaderResourceView? _emblemTexture;
    private int _emblem;
    private static Dictionary<int, (string Pack, int Entry)>? _symbols;

    // ── 선박 데코 ──
    // 배마다 「붙임 자리」 덩이가 하나 있다(색인 갈래 0x303, 자원 번호 = 모형 표의 줄 차례): u32 크기, u32 × 15 무리마다의 수, u32 × 15 무리의 첫 차례, 0x80 부터 4 × 4 행렬.
    // 무리(자리를 보고 읽은 것 — 짐작): 0 뱃머리 · 1 고물 · 2 · 3 좌우 현의 포문 · 4 · 7 앞뒤의 흩어진 자리 · 5 · 9 앞쪽 좌우 현 · 6 · 8 뒤쪽 좌우 현 · 10 가운데 · 11 갑판 · 12 · 13 앞뒤 물가.
    // 데코는 앞쪽 현(5 · 9)과 뒤쪽 현(6 · 8)의 첫째 · 둘째 자리에 좌우로 단다. 모형은 이름이 DECOnnn 인 것(nnn = 데코 표의 모형 번호 — 맞는지는 화면으로 본 것), 그림은 이름에 같은 번호가 든 것.
    private Matrix4x4[][] _spots = [];
    private readonly List<(Mesh Mesh, ID3D11ShaderResourceView? Texture, Matrix4x4 At, Vector4 Tint)> _decoParts = [];
    public static bool DecoTint = true;
    private readonly List<ID3D11ShaderResourceView> _decoTextures = [];
    private string _decoKey = "";

    // XKMD 항목의 이름 — 머리 +0x5C 가 이름 구역 자리, 그 +4 부터 32바이트
    private static string NameOf(Pack pack, int entry)
    {
        var head = pack.Slice(entry, 0, 0x80);
        if (head.Length < 0x80 || !head.AsSpan(0, 4).SequenceEqual("XKMD"u8)) return "";
        int at = BinaryPrimitives.ReadInt32LittleEndian(head.AsSpan(0x4C + 4 * 4));
        if (at == 0) return "";
        var name = pack.Slice(entry, at + 4, 32);
        int end = Array.IndexOf(name, (byte)0);
        return System.Text.Encoding.ASCII.GetString(name, 0, end < 0 ? name.Length : end);
    }
    private void ReadSpots(int rowIndex)
    {
        try
        {
            if (_index == null || !_index.TryGetValue((0x303, rowIndex), out var at)) return;
            var data = new Pack(at.Pack).Entry(at.Entry);
            _spots = new Matrix4x4[15][];
            for (int g = 0; g < 15; g++)
            {
                int count = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(4 + g * 4)), first = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(64 + g * 4));
                _spots[g] = new Matrix4x4[count];
                for (int k = 0; k < count; k++)
                {
                    int p = 0x80 + (first + k) * 64;
                    float F(int i) => BinaryPrimitives.ReadSingleLittleEndian(data.AsSpan(p + i * 4));
                    _spots[g][k] = new Matrix4x4(F(0), F(1), F(2), F(3), F(4), F(5), F(6), F(7), F(8), F(9), F(10), F(11), F(12), F(13), F(14), F(15));
                }
            }
        }
        catch (Exception) { _spots = []; }
    }

    /// <summary>
    /// 단 데코를 바꾼다 — 자리 넷(앞쪽 현 첫째 · 둘째, 뒤쪽 현 첫째 · 둘째)마다 데코 표의 모형 번호(0 이면 없음).
    /// mastKind 가 5 이상이면 마스트 톱의 장식(깃발이 아닌 것 — 스타 · 매의 목상 …)이다: 깃대 자리마다(돛대마다) 세우고 깃발은 안 건다(사용자, 2026-10-11).
    /// 그 모형은 데코 모형 표에 없다 — 이름이 FLAGnn 인 모형 자원이고 nn 이 데코 표의 모형 칸(5 ~ 14)이다,
    /// 그림은 데코 표의 덧칸 번호의 TEX_FLAGnn(깃발 그림과 같은 이름 줄 — 18 매의 목상 · 19 펌프킨 · 20 스타 …)이다.
    /// </summary>
    public void SetDecos(int[] models, int mastKind = 0, int mastPicture = 0)
    {
        if (models.Length < 5) models = [.. models, .. new int[5 - models.Length]];
        models[4] = mastKind > 4 ? mastKind : 0;
        string key = string.Join(",", models) + $"/{mastPicture}";
        if (key == _decoKey) return;
        _decoKey = key;
        foreach (var part in _decoParts) part.Mesh.Dispose();
        foreach (var texture in _decoTextures) texture.Dispose();
        _decoParts.Clear();
        _decoTextures.Clear();
        _mastDeco = false;      // 장식의 모형이 실제로 섰을 때만 깃발을 내린다(모형을 못 찾는 장식 — 11 ~ 14 — 은 깃발이 그대로 걸린다)
        if (_spots.Length < 15 && models[4] <= 0) return;
        try
        {
            // 데코의 모형 표 — 배 모형 표(0001\0002.bin)의 맨 끝: u32 줄 수(306), 10바이트 줄 = u16 모형 자원(갈래 0x300) · u16 그림 자원(0x301) · u16 둘째 그림 · u16 0 · u8 × 2 빛깔 번호.
            // 줄의 차례가 데코 표(138)의 모형 번호다(돛단배 8 · 9 · 10 이 같은 모형에 빛깔만 다르다). 그 앞에 u32 30 과 빛깔 30쌍(u32 ARGB × 2).
            var table = _modelTable!;
            int rows = 0, start = 0;
            for (int n = 1; table.Length - 4 - n * 10 > 0; n++)
                if (BinaryPrimitives.ReadInt32LittleEndian(table.AsSpan(table.Length - 4 - n * 10)) == n) (rows, start) = (n, table.Length - n * 10);
            for (int slot = 0; slot < Math.Min(5, models.Length); slot++)
            {
                if (slot < 4 && _spots.Length < 15) continue;
                if (models[slot] <= 0 || (slot < 4 && models[slot] >= rows)) continue;
                int row = slot < 4 ? start + models[slot] * 10 : start;
                int U16(int at) => BinaryPrimitives.ReadUInt16LittleEndian(table.AsSpan(at));
                (string Pack, int Entry) model;
                if (slot == 4) { if (FlagModel(models[slot]) is not { } top) continue; model = top; }
                else if (!_index!.TryGetValue((0x300, U16(row)), out model)) continue;
                ID3D11ShaderResourceView? picture = null;
                if (slot == 4) { if (FlagPicture(mastPicture) is { } named) _decoTextures.Add(picture = GameTexture.FromMftf(_device, new Pack(named.Pack).Entry(named.Entry))); }
                else if (_index.TryGetValue((0x301, U16(row + 2)), out var pictureAt)) _decoTextures.Add(picture = GameTexture.FromMftf(_device, new Pack(pictureAt.Pack).Entry(pictureAt.Entry)));
                // 빛깔: 줄 끝의 u8 둘이 빛깔 번호 둘(1 ~ 30)이고 번호마다 (밝은 빛, 어두운 빛) 쌍이다 — 돛단배 8 · 9 · 10 이 (7, 22) · (4, 12) · (11, 20).
                // 모형은 조각이 하나뿐이라(w-648 에서 세어 봄) 두 빛깔은 조각이 아니라 그림(텍스처의 알파나 밝기)으로 갈릴 것이다 — 그 법은 못 밝혀 첫째 번호의 밝은 빛 하나로 물들인다(짐작)
                var tint = Vector4.One;
                int colour = slot < 4 ? table[row + 8] : 0, colours = start - 4 - 240;
                if (DecoTint && colour is >= 1 and <= 30)
                {
                    uint argb = BinaryPrimitives.ReadUInt32LittleEndian(table.AsSpan(colours + (colour - 1) * 8));
                    tint = new Vector4((argb >> 16 & 255) / 255f, (argb >> 8 & 255) / 255f, (argb & 255) / 255f, 1);
                }
                var data = new Pack(model.Pack).Entry(model.Entry);
                // 붙일 자리들 — 현의 것은 양쪽 현의 그 칸, 마스트 톱의 것은 깃대 자리마다
                var places = new List<Matrix4x4>();
                if (slot == 4) places.AddRange(_flagSpots.Select(Matrix4x4.CreateTranslation));      // 돛대마다 하나씩(원본 화면 — 사용자, 2026-10-11; 처음에는 가장 높은 깃대 하나에만 세웠다)
                else
                    foreach (int group in slot < 2 ? (int[])[5, 9] : [6, 8])
                        if (_spots[group].Length > slot % 2) places.Add(_spots[group][slot % 2]);
                foreach (var place in places)
                {
                    int before = _parts.Count;
                    var (min, max) = (_min, _max);
                    _decoLoading = true;
                    try { Load(_device, data, isHull: false); } finally { _decoLoading = false; }
                    (_min, _max) = (min, max);                      // 데코는 배의 크기에 안 넣는다
                    for (int i = before; i < _parts.Count; i++) _decoParts.Add((_parts[i].Mesh, picture ?? _parts[i].Texture, place, tint));
                    if (slot == 4 && _parts.Count > before) _mastDeco = true;
                    _parts.RemoveRange(before, _parts.Count - before);
                }
            }
        }
        catch (Exception) { }                      // 못 읽는 데코는 안 단다
    }

    // 깃발 — 돛대 뼈대의 마디 가운데 이름이 FLAG… 인 자리(돛대 꼭대기 따위)에 나라 깃발을 단다.
    // 깃발 모형은 못 찾아서(FLAG05 부터만 이름 붙은 모형이 있다) 뒤로 나부끼는 띠를 지어 세운다. 그림은 sh0005 의 TEX_FLAG01 ~ 08:
    // 01 흰 깃발, 02 ~ 08 = 나라 1 ~ 7(에스파니아 · 포르투갈 · 베네치아 · 프랑스 · 네덜란드 · 잉글랜드 · 오스만 — 그림을 보고 맞춤).
    // 깃발 그림 — 이름이 TEX_FLAGnn 인 그림 자원(갈래 0x301)을 번호로. 01 ~ 08 은 나라 깃발, 그 뒤는 데코의 깃발과 마스트 톱 장식의 그림이다
    private static Dictionary<int, (string Pack, int Entry)>? _flagPictures;
    private static (string Pack, int Entry)? FlagPicture(int number)
    {
        if (_flagPictures == null)
        {
            _flagPictures = [];
            foreach (var (key, at) in _index ?? [])
            {
                if (key.Group != 0x301) continue;
                try
                {
                    var pack = new Pack(at.Pack);
                    string head = System.Text.Encoding.ASCII.GetString(pack.Slice(at.Entry, 0, Math.Min(400, pack.Size(at.Entry))));
                    int name = head.IndexOf("TEX_FLAG", StringComparison.Ordinal);
                    if (name >= 0 && int.TryParse(new string(head.Skip(name + 8).TakeWhile(char.IsDigit).ToArray()), out int found)) _flagPictures.TryAdd(found, at);
                }
                catch (Exception) { }
            }
        }
        return _flagPictures.TryGetValue(number, out var hit) ? hit : null;
    }
    // 마스트 톱 장식의 모형 — 이름이 FLAGnn 인 모형 자원(갈래 0x300). nn 은 데코 표의 모형 칸(5 ~ 14)
    private static Dictionary<int, (string Pack, int Entry)>? _flagModels;
    private static (string Pack, int Entry)? FlagModel(int number)
    {
        if (_flagModels == null)
        {
            _flagModels = [];
            foreach (var (key, at) in _index ?? [])
            {
                if (key.Group != 0x300) continue;
                try
                {
                    string name = NameOf(new Pack(at.Pack), at.Entry);
                    if (name.StartsWith("FLAG", StringComparison.OrdinalIgnoreCase) && int.TryParse(new string(name.Skip(4).TakeWhile(char.IsDigit).ToArray()), out int found)) _flagModels.TryAdd(found, at);
                }
                catch (Exception) { }
            }
        }
        return _flagModels.TryGetValue(number, out var hit) ? hit : null;
    }
    private readonly List<Vector3> _flagSpots = [];
    private bool _mastDeco;      // 마스트 톱에 장식을 달았다 — 깃발 대신 그것이 선다
    private Mesh? _flagMesh;
    private ID3D11ShaderResourceView? _flagTexture;
    private int _flagNation = -1;

    private void FlagSpots(byte[] data)
    {
        int nodes = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(0x2C + 4)), nodesAt = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(0x4C + 3 * 4)), namesAt = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(0x4C + 4 * 4));
        if (nodes == 0 || nodes > 200 || nodesAt == 0 || namesAt == 0 || nodesAt + nodes * 0x2C > data.Length) return;
        float F32(int at) => BinaryPrimitives.ReadSingleLittleEndian(data.AsSpan(at));
        var world = new Matrix4x4[nodes];
        for (int i = 0; i < nodes; i++)
        {
            int at = nodesAt + i * 0x2C, parent = BinaryPrimitives.ReadInt16LittleEndian(data.AsSpan(at + 2));
            var local = Matrix4x4.CreateFromQuaternion(new Quaternion(F32(at + 16), F32(at + 20), F32(at + 24), F32(at + 28))) * Matrix4x4.CreateTranslation(F32(at + 32), F32(at + 36), F32(at + 40));
            world[i] = parent >= 0 && parent < i ? local * world[parent] : local;
            int nameAt = namesAt + BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(namesAt + i * 4));
            if (nameAt + 4 > data.Length || !data.AsSpan(nameAt, 4).SequenceEqual("FLAG"u8)) continue;
            var spot = world[i].Translation;
            if (!_flagSpots.Exists(s => Vector3.DistanceSquared(s, spot) < 100 * 100)) _flagSpots.Add(spot);
        }
    }

    /// <summary>깃발의 나라를 정한다(0 이면 흰 깃발).</summary>
    public void SetFlag(int nation)
    {
        if (nation == _flagNation) return;
        _flagNation = nation;
        _flagTexture?.Dispose();
        _flagTexture = null;
        // 깃발 그림 묶음에서 깃발은 앞의 여덟 장뿐이다(0 무지, 1 ~ 7 나라 번호대로 — 에스파니아 … 오스만; 그 뒤 조각은 깃발이 아니다). 여덟째 나라부터와 해적 · 인물 깃발은 그림을 아직 못 찾아 무지로 건다
        try
        {
            // 여덟째부터(데코의 깃발 — TEX_FLAG09 …)는 이름으로 찾는다. 번호는 나라 차례 + 1 이다
            if (nation >= 8 && FlagPicture(nation + 1) is { } named) _flagTexture = GameTexture.FromMftf(_device, new Pack(named.Pack).Entry(named.Entry));
            else { var flags = new Pack(@"0001\sh0005.bin"); _flagTexture = GameTexture.FromMftf(_device, flags.Entry(nation is >= 1 and <= 7 ? nation : 0)); }
        }
        catch (Exception) { }
        if (_flagMesh != null || _flagSpots.Count == 0) return;
        // 띠 하나: 깃대에서 뒤(−z)로, 조금 물결치게 여섯 마디
        var builder = new MeshBuilder();
        float length = Math.Clamp(Radius * 0.11f, 260, 900), height = length * 0.3f;
        foreach (var spot in _flagSpots)
        {
            uint first = (uint)builder.Vertices.Count;
            const int segments = 6;
            for (int k = 0; k <= segments; k++)
            {
                float t = k / (float)segments, sway = MathF.Sin(t * 5.5f) * length * 0.07f * t;
                builder.Add(spot + new Vector3(sway, 0, -t * length), Vector3.UnitX, Vector4.One, new Vector2(t, 0));
                builder.Add(spot + new Vector3(sway, -height, -t * length), Vector3.UnitX, Vector4.One, new Vector2(t, 1));
            }
            for (uint k = 0; k < segments; k++)
            {
                uint a = first + k * 2;
                builder.Triangle(a, a + 2, a + 1); builder.Triangle(a + 1, a + 2, a + 3);
                builder.Triangle(a, a + 1, a + 2); builder.Triangle(a + 1, a + 3, a + 2);
            }
        }
        _flagMesh = builder.Build(_device);
    }

    /// <summary>
    /// 돛에 그릴 문장을 바꾼다 — 문장 그림 묶음 <c>0001\em0000 ~ 3.bin</c> 의 <c>tex_symbolNNN</c>, NNN = 문장 아이템 번호 − 1100000(그림을 나란히 놓고 맞춰 본 것). 0 이면 지운다.
    /// </summary>
    public void SetEmblem(int symbol)
    {
        if (symbol == _emblem) return;
        _emblem = symbol;
        _emblemTexture?.Dispose();
        _emblemTexture = null;
        if (symbol <= 0) return;
        try
        {
            if (_symbols == null)
            {
                _symbols = new Dictionary<int, (string, int)>();
                for (int k = 0; k < 4; k++)
                {
                    string file = $@"0001\em{k:D4}.bin";
                    var pack = new Pack(file);
                    for (int entry = 0; entry < pack.Count; entry++)
                    {
                        string head = System.Text.Encoding.ASCII.GetString(pack.Slice(entry, 0, Math.Min(700, pack.Size(entry))));
                        int at = head.IndexOf("tex_symbol", StringComparison.Ordinal);
                        if (at >= 0 && int.TryParse(new string(head.Skip(at + 10).TakeWhile(char.IsDigit).ToArray()), out int number)) _symbols.TryAdd(number, (file, entry));
                    }
                }
            }
            if (_symbols.TryGetValue(symbol, out var found)) _emblemTexture = GameTexture.FromMftf(_device, new Pack(found.Pack).Entry(found.Entry));
        }
        catch (Exception) { }                      // 그림을 못 읽으면 문장 없이
    }
    private readonly Gfx _device;
    private Vector3 _min = new(float.MaxValue), _max = new(float.MinValue);
    /// <summary>모형의 가운데와 반지름 — 창 안에 맞춰 보여 줄 때 쓴다.</summary>
    public Vector3 Center => (_min + _max) / 2;
    public float Radius => Vector3.Distance(_min, _max) / 2;

    /// <summary>돛 무늬 묶음(<c>0001\sa0000.bin</c>) — 무늬 열여덟 가지(TEX_BASE000 ~ 017), 무늬마다 색 벌 열 가지.</summary>
    public const int SailPatterns = 104, SailColors = 10;      // 무늬는 sa0000 ~ sa0005 에 18장씩(TEX_BASE000 ~ 103)

    /// <summary>돛의 무늬와 색을 바꾼다.</summary>
    public void SetSail(int pattern, int color)
    {
        var next = GameTexture.FromMftf(_device, new Pack($@"0001\sa{Math.Clamp(pattern, 0, SailPatterns - 1) / 18:D4}.bin").Entry(Math.Clamp(pattern, 0, SailPatterns - 1) % 18), 0, Math.Clamp(color, 0, SailColors - 1));
        for (int i = 0; i < _parts.Count; i++)
            if (_parts[i].Texture == _sailTexture) _parts[i] = (_parts[i].Mesh, next, _parts[i].Tint);
        _sailTexture.Dispose();
        _sailTexture = next;
    }

    /// <summary>자원 색인 — (갈래, 자원 번호) → (묶음 파일, 항목). 갈래 0x300 이 모형(XKMD), 0x301 이 텍스처다.</summary>
    private static Dictionary<(int Group, int Resource), (string Pack, int Entry)>? _index;
    private static byte[]? _modelTable;

    /// <summary>
    /// <c>0001\0001.tbl</c> 을 읽는다. 파일 전체가 57바이트 열쇠(「File Data Manager : (C) 2003 KOEI Co.,Ltd. Made in Japan.」)로 XOR 돼 있고,
    /// 풀면 「u8 길이 + 그만큼」 레코드의 줄이다. 묶음 머리: u32 ?, 항목 수, u8 글 길이, 글(「0001/sh0000.bin」).
    /// 항목(머리 뒤로 항목 수만큼, 묶음 안 차례 그대로): u32 ?, u16 갈래, u16 자원 번호, 묶음 안 번호. 수는 u8 인데 0xFF 면 뒤의 u16 이다.
    /// </summary>
    private static Dictionary<(int, int), (string, int)> ReadIndex()
    {
        var index = new Dictionary<(int, int), (string, int)>();
        var x = Dho.Data.GvoFiles.Read(@"0001\0001.tbl");
        var key = "File Data Manager : (C) 2003 KOEI Co.,Ltd. Made in Japan."u8;
        for (int i = 0; i < x.Length; i++) x[i] ^= key[i % key.Length];
        int left = 0, entry = 0;
        string path = "";
        for (int o = 0; o < x.Length;)
        {
            int p = o + 1, end = p + x[o];
            if (left == 0)
            {
                p += 4;
                left = x[p++];
                if (left == 0xFF) { left = BinaryPrimitives.ReadUInt16LittleEndian(x.AsSpan(p)); p += 2; }
                int length = x[p++];
                (path, entry) = (System.Text.Encoding.ASCII.GetString(x, p, length).Replace('/', '\\'), 0);
            }
            else
            {
                index[(BinaryPrimitives.ReadUInt16LittleEndian(x.AsSpan(p + 4)), BinaryPrimitives.ReadUInt16LittleEndian(x.AsSpan(p + 6)))] = (path, entry++);
                left--;
            }
            o = end;
        }
        return index;
    }

    /// <summary>
    /// 그 모형의 돛 자리 번호(0 ~ 4) — 돛대 · 돛 자원이 하나라도 적힌 자리. 모형이 없으면 빈 배열(편집기에서 더함).
    /// </summary>
    public static int[] SailSlots(int model)
    {
        var table = _modelTable ??= Dho.Data.GvoFiles.Read(@"0001\0002.bin");
        int row = RowOf(table, model);
        if (row < 0) return [];
        int U16(int at) => BinaryPrimitives.ReadUInt16LittleEndian(table.AsSpan(at));
        return [.. Enumerable.Range(0, 5).Where(k => U16(row + 40 + k * 2) != 0xFFFF || U16(row + 50 + k * 2) != 0xFFFF)];
    }

    /// <summary>
    /// 배 모형 표 <c>0001\0002.bin</c> 에서 모형 번호의 줄 자리. 머리: u32 범위 수, (u16 첫 모형 번호, u16 첫 줄) × 범위 수 — (1, 0) · (91, 71), u32 × 2, u32 줄 수.
    /// 모형 번호는 줄의 차례로 정해진다(72 ~ 90 은 없다). 줄 64바이트: u16 선체 자원, u16 일련번호, u16 NL 자원, 6바이트, f32 크기 비, u32,
    /// u16 × 5 텍스처(갈래 0x301), u16 × 5 돛대 갈래, u16 × 5 뼈대 자원, u16 × 5 돛 자원(빈 칸 0xFFFF). 없으면 −1.
    /// </summary>
    private static int RowOf(byte[] table, int model)
    {
        int ranges = BinaryPrimitives.ReadInt32LittleEndian(table), start = 4 + ranges * 4 + 12;
        int rows = BinaryPrimitives.ReadInt32LittleEndian(table.AsSpan(start - 4));
        for (int r = ranges - 1; r >= 0; r--)
        {
            int first = BinaryPrimitives.ReadUInt16LittleEndian(table.AsSpan(4 + r * 4)), row0 = BinaryPrimitives.ReadUInt16LittleEndian(table.AsSpan(6 + r * 4));
            if (model < first) continue;
            int k = row0 + model - first;
            int limit = r + 1 < ranges ? BinaryPrimitives.ReadUInt16LittleEndian(table.AsSpan(6 + (r + 1) * 4)) : rows;
            return k < limit ? start + 64 * k : -1;
        }
        return -1;
    }

    /// <summary>
    /// 배 표의 모형 번호로 모형을 세운다 — 모형 표의 줄에 적힌 자원 번호(선체, 뼈대 · 돛 다섯 벌)를 색인으로 묶음 항목에 잇는다.
    /// 남의 돛을 빌려 쓰는 배도 줄에 그 자원 번호가 그대로 적혀 있다. 줄이 없으면 <paramref name="fallbackModel"/> 의 것을 쓴다.
    /// </summary>
    /// <param name="skipMasts">뺄 돛 자리(0 ~ 4) — 편집기에서 더함. 한 돛대에 가로돛 · 삼각돛이 겹쳐 서는 모형(바사 따위)이 있다.</param>
    public ShipModel(Gfx gfx, int model, int fallbackModel = 16, IReadOnlySet<int>? skipMasts = null)
    {
        _device = gfx;
        _hullTexture = GameTexture.FromMftf(gfx, new Pack(@"0001\sh0004.bin").Entry(0));
        _sailTexture = GameTexture.FromMftf(gfx, new Pack(@"0001\sa0000.bin").Entry(0));

        try { _yardTexture = GameTexture.FromMftf(gfx, new Pack(@"0001\sh0005.bin").Entry(8), 0); } catch (Exception) { }
        _index ??= ReadIndex();
        var table = _modelTable ??= Dho.Data.GvoFiles.Read(@"0001\0002.bin");
        int row = RowOf(table, model);
        if (row < 0) row = RowOf(table, fallbackModel);
        int U16(int at) => BinaryPrimitives.ReadUInt16LittleEndian(table.AsSpan(at));
        ReadSpots((row - (4 + BinaryPrimitives.ReadInt32LittleEndian(table) * 4 + 12)) / 64);
        void Part(int resource, bool isHull)
        {
            if (resource == 0xFFFF || !_index.TryGetValue((0x300, resource), out var found)) return;
            var data = new Pack(found.Pack).Entry(found.Entry);
            if (data.Length < 0x80 || !data.AsSpan(0, 4).SequenceEqual("XKMD"u8)) return;
            Load(gfx, data, isHull);
            FlagSpots(data);
        }
        // 선체의 조각은 텍스처 번호가 둘이다: 0 = 바탕 판(SHIP_BASE_…), 1 = 그 배의 장식 판(줄의 넷째 텍스처 칸 — SHIP_DECO_001 ~ 004: 창 · 난간 · 금빛 띠)
        try
        {
            if (U16(row + 26) is var deco && deco != 0xFFFF && _index.TryGetValue((0x301, deco), out var decoAt))
                _decoTexture = GameTexture.FromMftf(gfx, new Pack(decoAt.Pack).Entry(decoAt.Entry));
        }
        catch (Exception) { }                      // 못 읽으면 장식도 바탕 판으로 그린다
        Part(U16(row), true);
        for (int mast = 0; mast < 5; mast++)
        {
            if (skipMasts?.Contains(mast) == true) continue;
            Part(U16(row + 40 + mast * 2), false);
            Part(U16(row + 50 + mast * 2), false);
        }
    }

    private void Load(Gfx gfx, byte[] data, bool isHull)
    {
        int U16(int at) => BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(at));
        int I32(int at) => BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(at));
        float F32(int at) => BinaryPrimitives.ReadSingleLittleEndian(data.AsSpan(at));

        int bufferCount = U16(0x2C);
        int buffersAt = I32(0x4C), indexAt = I32(0x4C + 10 * 4), vertexAt = I32(0x4C + 11 * 4);

        // 텍스처단 → 텍스처 번호(구역 6, 0x20 바이트씩, +4 가 i16 번호)
        int stageCount = U16(0x2C + 4 * 2), stagesAt = I32(0x4C + 6 * 4);
        var decoBuilders = new MeshBuilder?[bufferCount];
        // 정점버퍼마다 삼각형을 모은다
        var builders = new MeshBuilder?[bufferCount];
        var fills = new Func<MeshBuilder>?[bufferCount];
        var sailPositions = new Vector3[]?[bufferCount];
        var sailTriangles = new List<(uint A, uint B, uint C, int Wrinkle)>?[bufferCount];
        var sailCorners = new (Vector3 Normal, Vector2 Cloth, Vector2 Emblem, Vector2 Wrinkle)[]?[bufferCount];
        var dyed = new Dictionary<(int Buffer, int Dye), MeshBuilder>();
        var formats = new int[bufferCount];
        for (int b = 0; b < bufferCount; b++)
        {
            int description = buffersAt + b * 0x34;
            int fvf = formats[b] = I32(description), stride = I32(description + 4), count = I32(description + 8);
            int at = vertexAt + I32(description + 12);
            if ((fvf & 0x10) == 0 || (fvf >> 8 & 0xF) == 0) continue;      // 법선이나 UV 가 없는 것은 그리지 않는다

            int normalAt = 12 + 4 * ((fvf & 0xE) switch { 0x6 => 1, 0x8 => 2, 0xA => 3, 0xC => 4, 0xE => 5, _ => 0 });
            int uvAt = normalAt + 12 + ((fvf & 0x40) != 0 ? 4 : 0);
            var builder = builders[b] = new MeshBuilder();
            int bufferAt = at, bufferStride = stride, bufferCount2 = count;
            // 선체의 조각에는 빛깔 번호(조각 기록 +8, 0 ~ 4)가 있다 — 번호마다 따로 모아 재질의 빛깔을 달리 입힌다
            if (isHull) fills[b] = () =>
            {
                var filled = new MeshBuilder();
                for (int v = 0; v < bufferCount2; v++)
                {
                    int p = bufferAt + v * bufferStride;
                    filled.Add(new Vector3(F32(p), F32(p + 4), F32(p + 8)), new Vector3(F32(p + normalAt), F32(p + normalAt + 4), F32(p + normalAt + 8)), Vector4.One, new Vector2(F32(p + uvAt), F32(p + uvAt + 4)));
                }
                return filled;
            };
            var decoBuilder = isHull && _decoTexture != null ? decoBuilders[b] = new MeshBuilder() : null;
            // 돛 천: 둘째 UV 벌로 문장 면을 따로 만든다 — 천의 앞뒤로 조금 띄워 두 겹(정점 2v 가 앞, 2v + 1 이 뒤)
            if (!isHull && !_decoLoading && fvf == FvfSail)
            {
                (sailPositions[b], sailTriangles[b]) = (new Vector3[count], []);
                var corners = sailCorners[b] = new (Vector3 Normal, Vector2 Cloth, Vector2 Emblem, Vector2 Wrinkle)[count];
                for (int v = 0; v < count; v++)
                {
                    int p = at + v * stride;
                    sailPositions[b]![v] = new Vector3(F32(p), F32(p + 4), F32(p + 8));
                    corners[v] = (new Vector3(F32(p + normalAt), F32(p + normalAt + 4), F32(p + normalAt + 8)), new Vector2(F32(p + uvAt), F32(p + uvAt + 4)),
                                  new Vector2(F32(p + uvAt + 8), F32(p + uvAt + 12)), new Vector2(F32(p + uvAt + 16), F32(p + uvAt + 20)));
                }
            }
            for (int v = 0; v < count; v++)
            {
                int p = at + v * stride;
                _min = Vector3.Min(_min, new Vector3(F32(p), F32(p + 4), F32(p + 8)));
                _max = Vector3.Max(_max, new Vector3(F32(p), F32(p + 4), F32(p + 8)));
                builder.Add(new Vector3(F32(p), F32(p + 4), F32(p + 8)),
                            new Vector3(F32(p + normalAt), F32(p + normalAt + 4), F32(p + normalAt + 8)),
                            Vector4.One, new Vector2(F32(p + uvAt), F32(p + uvAt + 4)));
                decoBuilder?.Add(new Vector3(F32(p), F32(p + 4), F32(p + 8)),
                            new Vector3(F32(p + normalAt), F32(p + normalAt + 4), F32(p + normalAt + 8)),
                            Vector4.One, new Vector2(F32(p + uvAt), F32(p + uvAt + 4)));
            }
        }

        foreach (int section in (int[])[8, 9])
        {
            int subsetsAt = I32(0x4C + section * 4), subsetCount = U16(0x2C + (section == 8 ? 6 : 8) * 2);
            if (subsetsAt == 0) continue;
            for (int s = 0; s < subsetCount; s++)
            {
                int record = subsetsAt + I32(subsetsAt + s * 4);
                int buffer = U16(record + 6), primitive = I32(record + 0x2C);
                int first = I32(record + 0x38), primitives = I32(record + 0x3C);
                var builder = buffer < bufferCount ? builders[buffer] : null;
                if (builder == null) continue;
                // 이 조각의 첫 텍스처단이 가리키는 번호가 1 이면 장식 판 쪽에 모은다
                if (isHull && decoBuilders[buffer] is { } decorated && I32(record + 0x0C) > 0 && U16(record + 0x10) is var stage && stage < stageCount
                    && BinaryPrimitives.ReadInt16LittleEndian(data.AsSpan(stagesAt + stage * 0x20 + 4)) == 1) builder = decorated;
                else if (isHull && U16(record + 8) is > 0 and < 8 and var dye && fills[buffer] is { } fill)
                {
                    if (!dyed.TryGetValue((buffer, dye), out var own)) dyed[(buffer, dye)] = own = fill();
                    builder = own;
                }

                int indices = indexAt + I32(buffersAt + buffer * 0x34 + 0x24);
                uint Index(int i) => (uint)U16(indices + (first + i) * 2);
                // 돛 천은 돛 한 장씩 따로 만든다(아래) — 여기서는 삼각형과, 둘째 텍스처단의 주름 그림 번호(4 ~)만 모아 둔다
                var cloth = sailTriangles[buffer];
                int wrinkle = 0;
                if (cloth != null && I32(record + 0x0C) > 1 && U16(record + 0x12) is var second && second < stageCount)
                    wrinkle = BinaryPrimitives.ReadInt16LittleEndian(data.AsSpan(stagesAt + second * 0x20 + 4));
                if (primitive == 5)
                {
                    for (int i = 0; i < primitives; i++)
                    {
                        uint a = Index(i), b = Index(i + 1), c = Index(i + 2);
                        if (a == b || b == c || a == c) continue;
                        if ((i & 1) != 0) (a, b) = (b, a);
                        if (cloth != null) cloth.Add((a, b, c, wrinkle));
                        else builder.Triangle(a, b, c);
                    }
                }
                else
                {
                    for (int i = 0; i < primitives; i++)
                        if (cloth != null) cloth.Add((Index(i * 3), Index(i * 3 + 1), Index(i * 3 + 2), wrinkle));
                        else builder.Triangle(Index(i * 3), Index(i * 3 + 1), Index(i * 3 + 2));
                }
            }
        }

        // 돛 천을 돛 한 장씩 나눈다 — 이어진 삼각형 무리가 돛 한 장이다. 장마다 천, 문장 면(둘째 UV 벌), 주름 면(셋째 UV 벌 — 그림 번호마다)을 만든다.
        // 문장 · 주름 면은 천의 앞뒤로 조금 띄운 두 겹이다(같은 자리에 겹치면 깊이가 싸운다)
        for (int b = 0; b < bufferCount; b++)
        {
            if (sailTriangles[b] is not { Count: > 0 } triangles || sailPositions[b] is not { } spots || sailCorners[b] is not { } corners) continue;
            static int Find(int[] parent, int x) { while (parent[x] != x) x = parent[x] = parent[parent[x]]; return x; }
            var parent = new int[spots.Length];
            var seen = new Dictionary<(int, int, int), int>();
            for (int v = 0; v < spots.Length; v++)
            {
                parent[v] = v;
                var key = ((int)MathF.Round(spots[v].X), (int)MathF.Round(spots[v].Y), (int)MathF.Round(spots[v].Z));
                if (seen.TryGetValue(key, out int first)) parent[v] = first; else seen[key] = v;
            }
            foreach (var (a, b2, c, _) in triangles) { parent[Find(parent, (int)b2)] = Find(parent, (int)a); parent[Find(parent, (int)c)] = Find(parent, (int)a); }
            foreach (var sheet in triangles.GroupBy(t => Find(parent, (int)t.A)))
            {
                var cloth = new MeshBuilder();
                var emblem = new MeshBuilder();
                var folds = new Dictionary<int, MeshBuilder>();
                var clothAt = new Dictionary<uint, uint>();
                var emblemAt = new Dictionary<uint, uint>();
                var foldAt = new Dictionary<(int, uint), uint>();
                float area = 0;
                Vector3 sum = default, facing = default;
                int corner = 0;
                foreach (var (a, b2, c, wrinkle) in sheet)
                {
                    area += Vector3.Cross(spots[b2] - spots[a], spots[c] - spots[a]).Length();
                    uint Cloth(uint v)
                    {
                        if (clothAt.TryGetValue(v, out uint known)) return known;
                        cloth.Add(spots[v], corners[v].Normal, Vector4.One, corners[v].Cloth);
                        (sum, corner, facing) = (sum + spots[v], corner + 1, facing + Vector3.Abs(corners[v].Normal));
                        return clothAt[v] = (uint)clothAt.Count;
                    }
                    cloth.Triangle(Cloth(a), Cloth(b2), Cloth(c));
                    // 앞 겹은 2k, 뒤 겹은 2k + 1
                    uint Layer(MeshBuilder into, Dictionary<uint, uint> at, uint v, Vector2 uv, float lift)
                    {
                        if (at.TryGetValue(v, out uint known)) return known;
                        into.Add(spots[v] + corners[v].Normal * lift, corners[v].Normal, Vector4.One, uv);
                        into.Add(spots[v] - corners[v].Normal * lift, -corners[v].Normal, Vector4.One, uv);
                        return at[v] = (uint)at.Count * 2;
                    }
                    uint ea = Layer(emblem, emblemAt, a, corners[a].Emblem, 14), eb = Layer(emblem, emblemAt, b2, corners[b2].Emblem, 14), ec = Layer(emblem, emblemAt, c, corners[c].Emblem, 14);
                    emblem.Triangle(ea, eb, ec);
                    emblem.Triangle(eb + 1, ea + 1, ec + 1);
                    if (wrinkle < 4) continue;
                    if (!folds.TryGetValue(wrinkle, out var fold)) folds[wrinkle] = fold = new MeshBuilder();
                    uint Fold(uint v)
                    {
                        if (foldAt.TryGetValue((wrinkle, v), out uint known)) return known;
                        fold.Add(spots[v] + corners[v].Normal * 6, corners[v].Normal, Vector4.One, corners[v].Wrinkle);
                        fold.Add(spots[v] - corners[v].Normal * 6, -corners[v].Normal, Vector4.One, corners[v].Wrinkle);
                        return foldAt[(wrinkle, v)] = (uint)(fold.Vertices.Count - 2);
                    }
                    uint fa = Fold(a), fb = Fold(b2), fc = Fold(c);
                    fold.Triangle(fa, fb, fc);
                    fold.Triangle(fb + 1, fa + 1, fc + 1);
                }
                if (cloth.Indices.Count == 0) continue;
                _sails.Add(new Sail(cloth.Build(gfx), emblem.Build(gfx), folds.Select(f => (f.Value.Build(gfx), f.Key)).ToList(), sum / Math.Max(1, corner), area, facing / Math.Max(1, corner)));
                _sailsSorted = false;
            }
        }

        for (int b = 0; b < bufferCount; b++)
        {
            var builder = builders[b];
            if (isHull)
                foreach (var ((buffer, dye), own) in dyed)
                    if (buffer == b && own.Indices.Count > 0) _parts.Add((own.Build(gfx), _hullTexture, new Vector4(1, 1, 1, 10 + dye)));
            if (builder == null || (builder.Indices.Count == 0 && decoBuilders[b] is not { Indices.Count: > 0 })) continue;
            // 선체는 선체 그림, 돛 천은 돛 그림. 돛 파트의 나머지(활대·줄)는 그림을 못 찾아 나무 빛으로 칠한다.
            if (isHull)
            {
                if (builder.Indices.Count > 0) _parts.Add((builder.Build(gfx), _hullTexture, Vector4.One));
                if (decoBuilders[b] is { Indices.Count: > 0 } decorated) _parts.Add((decorated.Build(gfx), _decoTexture, Vector4.One));      // 장식은 재질 빛깔로 물들이지 않는다
                continue;
            }
            else if (formats[b] == FvfSail)
                _parts.Add((builder.Build(gfx), _sailTexture, Vector4.One));      // 데코 모형 따위에 든 돛 천 꼴의 조각(배의 돛은 위에서 장마다 만들었다)
            // 활대 · 밧줄: 돛 모형의 텍스처 번호 3 = 돛 부속 그림 묶음(sh0005 의 8번)의 첫 장 TEX_YARD(4 부터는 주름). 못 읽으면 나무 빛으로 칠한다
            else if (_yardTexture != null) _parts.Add((builder.Build(gfx), _yardTexture, Vector4.One));
            else _parts.Add((builder.Build(gfx), null, new Vector4(0.42f, 0.33f, 0.22f, 1)));
        }
    }

    /// <param name="sail">돛 천에 입히는 빛깔(돛 도료).</param>
    /// <param name="hull">선체에 입히는 빛깔(재질).</param>
    public static Vector4[]? DyeDebug;
    // 돛을 편 만큼(0 ~ 1) — 돛대의 차례대로 그만큼의 돛대에만 돛 천을 그린다. 0 이면 돛을 다 접은 것(활대 · 밧줄만 남는다)
    public float Furl { get; set; } = 1;
    // 선체의 띠(빛깔 번호 1 · 2)에 입히는 빛깔 — 없으면 선체와 같은 빛
    public Vector4? Trim { get; set; }
    private ID3D11ShaderResourceView? _woodTexture;
    private bool _woodTried;
    // 제 빛의 나무 판(SHIP_BASE_101) — 칠한 배의 돛대 · 갑판 · 밧줄에 쓴다
    private ID3D11ShaderResourceView? Wood()
    {
        if (_woodTried) return _woodTexture;
        _woodTried = true;
        try { if (_index != null && _index.TryGetValue((0x301, 1), out var at)) _woodTexture = GameTexture.FromMftf(_device, new Pack(at.Pack).Entry(at.Entry)); } catch (Exception) { }
        return _woodTexture;
    }
    // 돛 한 장 — 천, 문장 면, 주름 면들(그림 번호와 함께), 가운데 자리, 넓이. Emblem 은 문장을 그릴 돛인가(돛대마다 가장 넓은 한 장)
    private sealed record Sail(Mesh Cloth, Mesh EmblemMesh, List<(Mesh Mesh, int Number)> Folds, Vector3 Centre, float Area, Vector3 Facing) { public bool Emblem; }
    private readonly List<Sail> _sails = [];
    private bool _sailsSorted, _decoLoading;
    private readonly Dictionary<int, ID3D11ShaderResourceView?> _wrinkles = [];

    // 주름 그림 — 돛 부속 그림 묶음(sh0005 의 8번)의 TEX_WRINKLE…: 텍스처 번호 3 이 첫 장(TEX_YARD)이라 번호 n 은 n − 3 째 장이다
    private ID3D11ShaderResourceView? Wrinkle(int number)
    {
        if (_wrinkles.TryGetValue(number, out var known)) return known;
        ID3D11ShaderResourceView? made = null;
        try { made = GameTexture.FromMftf(_device, new Pack(@"0001\sh0005.bin").Entry(8), number - 3); } catch (Exception) { }
        return _wrinkles[number] = made;
    }

    // 돛을 아래에서 위로 차례 짓고(접을 때 위의 돛부터 접는다), 문장을 그릴 돛을 고른다:
    // 넓은 돛부터 보아, 배의 길이 쪽으로 이미 고른 돛과 충분히 떨어진 것 — 돛대마다 한 장쯤 된다(작은 돛 · 삼각돛은 빼려고 가장 넓은 돛의 45% 이상만)
    private void SortSails()
    {
        _sailsSorted = true;
        _sails.Sort((a, b) => a.Centre.Y.CompareTo(b.Centre.Y));
        if (_sails.Count == 0) return;
        var size = _max - _min;
        bool alongX = size.X > size.Z;
        float apart = MathF.Max(size.X, size.Z) / 6, widest = _sails.Max(s => s.Area);
        var picked = new List<float>();
        foreach (var sail in _sails) sail.Emblem = false;
        // 가로돛(배의 길이 쪽을 바라보는 돛)만 — 삼각돛 · 세로돛에는 안 그린다. 가로돛이 하나도 없는 배(라틴 돛)는 가리지 않는다
        bool Square(Sail s) => (alongX ? s.Facing.X : s.Facing.Z) > 0.6f;
        bool anySquare = _sails.Exists(Square);
        float At(Sail s) => alongX ? s.Centre.X : s.Centre.Z;
        foreach (var sail in _sails.OrderByDescending(s => s.Area))
        {
            float at = At(sail);
            if (anySquare && !Square(sail)) continue;
            if (sail.Area < widest * 0.45f || picked.Exists(p => MathF.Abs(p - at) < apart)) continue;
            picked.Add(at);
            // 그 돛대의 돛들(아래에서 위로) 가운데 것에 — 맨 아래 큰 돛이 아니라 가운데 돛에 문장이 달린다(사용자 기억)
            var mast = _sails.FindAll(s => MathF.Abs(At(s) - at) < apart && (!anySquare || Square(s)) && s.Area >= sail.Area * 0.3f);
            mast[mast.Count / 2].Emblem = true;
        }
    }

    public void Draw(SceneRenderer scene, in Matrix4x4 world, Vector4? sail = null, Vector4? hull = null)
    {
        if (!_sailsSorted) SortSails();
        // 편 돛의 수 — 아래 돛부터 그만큼
        int set = (int)MathF.Ceiling(_sails.Count * Math.Clamp(Furl, 0, 1) - 0.001f);
        for (int i = 0; i < set; i++) scene.Draw(_sails[i].Cloth, world, sail ?? Vector4.One, _sailTexture, cloth: true);
        foreach (var (mesh, texture, tint) in _parts)
        {
            Vector4 colour = texture == _sailTexture && sail is { } paint ? paint : texture == _hullTexture && hull is { } wood ? wood : tint with { W = 1 };
            int dye = tint.W >= 10 ? (int)tint.W - 10 : 0;
            if (texture == _hullTexture && DyeDebug != null) colour = DyeDebug[dye];
            else if (texture == _hullTexture && dye is 1 or 2 && hull != null && Trim is { } band) colour = band;
            // 선체 조각의 빛깔 번호: 3 = 선체 널, 1 · 2 = 띠, 0 = 돛대 · 갑판, 4 = 밧줄(색을 입혀 보고 읽은 것). 칠한 재질(바탕 판 2 ~)에서는 0 · 4 를 칠하지 않고 나무 판으로 그린다
            var plate = texture;
            if (texture == _hullTexture && DyeDebug == null && _hullBase >= 2 && dye is 0 or 4 && Wood() is { } timber) (plate, colour) = (timber, Vector4.One);
            scene.Draw(mesh, world, colour, plate, cloth: texture == _sailTexture);
        }
        if (_flagMesh != null && _flagTexture != null && !_mastDeco) scene.Draw(_flagMesh, world, null, _flagTexture, cloth: true);
        foreach (var (mesh, texture, at, tint) in _decoParts) scene.Draw(mesh, at * world, texture == null ? new Vector4(0.6f, 0.5f, 0.35f, 1) : tint, texture);
        // 주름은 돛 그림에 곱해진다(원본 텍스처단의 연산이 MODULATE), 문장은 그 위에 얹힌다
        scene.Multiply();
        for (int i = 0; i < set; i++)
            foreach (var (mesh, number) in _sails[i].Folds)
                if (Wrinkle(number) is { } wrinkle) scene.Draw(mesh, world, null, wrinkle, multiply: true);
        scene.Opaque();
        if (_emblemTexture != null)
            for (int i = 0; i < set; i++)
                if (_sails[i].Emblem) scene.Draw(_sails[i].EmblemMesh, world, null, _emblemTexture, cloth: true, emblem: true);
    }

    public void Dispose()
    {
        foreach (var (mesh, _, _) in _parts) mesh.Dispose();
        _hullTexture.Dispose();
        _sailTexture.Dispose();
        _decoTexture?.Dispose();
        _yardTexture?.Dispose();
        _flagTexture?.Dispose();
        _flagMesh?.Dispose();
        foreach (var part in _decoParts) part.Mesh.Dispose();
        foreach (var texture in _decoTextures) texture.Dispose();
        _emblemTexture?.Dispose();
        foreach (var sail in _sails)
        {
            sail.Cloth.Dispose();
            sail.EmblemMesh.Dispose();
            foreach (var fold in sail.Folds) fold.Mesh.Dispose();
        }
        foreach (var wrinkle in _wrinkles.Values) wrinkle?.Dispose();
        _woodTexture?.Dispose();
    }
}
