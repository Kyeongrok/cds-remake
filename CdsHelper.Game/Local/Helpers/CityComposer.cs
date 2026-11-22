using System.IO;
using System.Text;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace CdsHelper.Game.Local.Helpers;

/// <summary>
/// 도시 그림을 <b>배경 · 테두리 · 건물</b> 세 겹으로 갈라 다시 짜 맞춘다 — 도구 「도시 이미지 메이커」. 원본에 없는 것이다.
/// </summary>
/// <remarks>
/// 도시 그림(CITYCG.CDS)은 통짜다 — 따로 얹는 것은 자택뿐이다. 그래서 겹을 그림에서 뽑아낸다.
/// <list type="bullet">
/// <item><b>테두리</b> — 장식 틀 · 명판은 문화권이 아니라 <b>그림 무리</b>마다 같다(226장이 스무남은 가지로 갈린다).
/// 가장자리 띠가 닮은 그림끼리 묶고(<see cref="EdgeBand"/>), 무리 안 <see cref="FrameAgree"/> 넘게 같은 점만 남긴 뒤
/// 바깥 가장자리에 닿은 덩어리만 두고 안쪽 구멍은 메운다.</item>
/// <item><b>건물</b> — <see cref="BuildingSprites.Extract"/> 의 문화권 · 갈래 · 변형 그림. 건물 점이 덜 맞아 빈 구멍은 메우고,
/// 점 색은 기준 건물의 그림에서 다시 가져온다.</item>
/// <item><b>배경</b> — 고른 도시 그림에서 건물 자리를 옆 땅으로 덮고(같은 줄 높이에서 가로로 옮겨 온 조각 — 원근이 맞는다),
/// 테두리 자리는 안쪽 풍경을 끌어와 메운다(새 테두리가 대개 덮는다).</item>
/// </list>
/// 4배 그림은 모드 「도시 그림 업스케일링」이 받아 둔 그림(<see cref="CityUpscale"/>)에서 같은 자리를 오려 낸다 — 가리개는 1배에서 정하고
/// 4배로 늘려 가장자리만 부드럽게 한다. 받아 둔 그림이 없는 도시는 1배 점을 그대로 네 배로 늘린다.
/// </remarks>
public sealed class CityComposer
{
    public const int W = CityPictures.Width, H = CityPictures.Height;

    /// <summary>업스케일 배율.</summary>
    public const int Hd = CityUpscale.Factor;

    /// <summary>무리를 가를 때 견주는 가장자리 띠 두께(1배 점).</summary>
    private const int EdgeBand = 12;

    /// <summary>가장자리 띠가 이만큼 넘게 같으면 한 무리다.</summary>
    private const double SameFrame = 0.6;

    /// <summary>한 점을 같은 색으로 볼 R·G·B 차이 합.</summary>
    private const int FrameTolerance = 30;

    /// <summary>무리 안에서 이만큼 넘는 그림이 같은 색이면 테두리 점이다.</summary>
    private const double FrameAgree = 0.8;

    /// <summary>테두리로 뽑을 무리의 최소 그림 수 — 둘 이하면 풍경까지 같아 테두리가 안 갈린다.</summary>
    private const int MinFrameCities = 3;

    /// <summary>테두리 한 가지 — 쓰는 도시들(첫 도시가 기준) · 1배 가리개 · 이름.</summary>
    public sealed record FrameStyle(int Id, IReadOnlyList<int> Cities, bool[] Mask, string Label)
    {
        public int RefCity => Cities[0];
        public string FileName => $"frame_{Id:00}.png";
        public override string ToString() => Label;
    }

    /// <summary>캔버스에 놓은 건물 — 상자 왼쪽 위(1배 점).</summary>
    public sealed record Placed(BuildingSprites.Sprite Sprite, int X, int Y);

    public CityBuildingTable Table { get; }
    public CityTable Cities { get; }
    public IReadOnlyList<BuildingSprites.Sprite> Sprites { get; }
    public IReadOnlyList<FrameStyle> Frames { get; }

    /// <summary>업스케일 그림이 있는 도시 수.</summary>
    public int HdCount { get; }

    public int Count => _pics.Length;

    private readonly uint[]?[] _pics;
    private readonly bool[] _hd;
    private readonly int[] _frameOf;
    private readonly Dictionary<CityBuildingTable.Building, BuildingSprites.Sprite> _spriteOf = [];
    private readonly Dictionary<BuildingSprites.Sprite, bool[]> _spriteMask = [];
    private readonly Dictionary<(int City, int Scale), uint[]> _clean = [];
    private readonly Dictionary<(object Key, int Scale), (uint[] Bgra, byte[] Alpha)> _layers = [];
    private readonly Dictionary<int, uint[]> _hdSource = [];
    private readonly Queue<int> _hdOrder = new();

    private CityComposer(uint[]?[] pics, bool[] hd, CityBuildingTable table, CityTable cities,
                         List<BuildingSprites.Sprite> sprites, List<FrameStyle> frames, int[] frameOf)
    {
        _pics = pics;
        _hd = hd;
        HdCount = hd.Count(h => h);
        Table = table;
        Cities = cities;
        Sprites = sprites;
        Frames = frames;
        _frameOf = frameOf;
        foreach (var s in sprites)
            foreach (var m in s.Members)
                _spriteOf[m] = s;
    }

    /// <summary>모든 그림을 읽어 겹을 가른다 — 몇 초 걸린다(UI 실 밖에서 부를 것).</summary>
    public static CityComposer Build(CityPictures pictures, CityBuildingTable table, CityTable cities)
    {
        int n = pictures.Count;
        var pics = new uint[]?[n];
        var hd = new bool[n];
        for (int i = 0; i < n; i++)
        {
            pics[i] = pictures.TryGetBgra(i);
            hd[i] = pics[i] is { } p && File.Exists(Path.Combine(CityUpscale.Folder, CityUpscale.KeyOf(p, W, H) + ".png"));
        }

        string Culture(int id) => cities.CultureOf(id) is { Length: > 0 } c ? c : "기타";
        var sprites = BuildingSprites.Extract(pictures, table, Culture);
        var (frames, frameOf) = FindFrames(pics, cities);
        return new CityComposer(pics, hd, table, cities, sprites, frames, frameOf);
    }

    public string CultureOf(int city) => Cities.CultureOf(city) is { Length: > 0 } c ? c : "기타";

    public bool HasPicture(int city) => city >= 0 && city < _pics.Length && _pics[city] != null;

    public bool HasHd(int city) => city >= 0 && city < _hd.Length && _hd[city];

    /// <summary>그 도시가 쓰는 테두리. 무리가 작아 못 뽑았으면 null.</summary>
    public FrameStyle? FrameOf(int city) => city >= 0 && city < _frameOf.Length && _frameOf[city] >= 0 ? Frames[_frameOf[city]] : null;

    /// <summary>그 도시에 선 건물을 그대로 놓은 목록 — 그림을 못 뽑은 건물(그 갈래에서 혼자인 것)은 배경에 남는다.</summary>
    public List<Placed> PlacedOf(int city) =>
        [.. Table.InCity(city).Where(_spriteOf.ContainsKey).Select(b => new Placed(_spriteOf[b], b.X, b.Y))];

    // ── 테두리 ───────────────────────────────────────────────────────────

    private static int Diff(uint a, uint b) =>
        Math.Abs((int)((a >> 16) & 255) - (int)((b >> 16) & 255))
        + Math.Abs((int)((a >> 8) & 255) - (int)((b >> 8) & 255))
        + Math.Abs((int)(a & 255) - (int)(b & 255));

    private static (List<FrameStyle>, int[]) FindFrames(uint[]?[] pics, CityTable cities)
    {
        int n = pics.Length;
        var edge = new List<int>();
        for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
                if (x < EdgeBand || y < EdgeBand || x >= W - EdgeBand || y >= H - EdgeBand)
                    edge.Add(y * W + x);

        var group = Enumerable.Repeat(-1, n).ToArray();
        var groups = new List<List<int>>();
        for (int i = 0; i < n; i++)
        {
            if (group[i] >= 0 || pics[i] is not { } a) continue;
            var members = new List<int>();
            for (int j = i; j < n; j++)
            {
                if (group[j] >= 0 || pics[j] is not { } b) continue;
                int same = 0;
                foreach (int k in edge) if (Diff(a[k], b[k]) < 40) same++;
                if (same > edge.Count * SameFrame) { group[j] = groups.Count; members.Add(j); }
            }
            groups.Add(members);
        }

        var frames = new List<FrameStyle>();
        var frameOf = Enumerable.Repeat(-1, n).ToArray();
        foreach (var members in groups.OrderByDescending(g => g.Count))
        {
            if (members.Count < MinFrameCities) continue;
            var refPic = pics[members[0]]!;
            var mask = new bool[W * H];
            int need = (int)Math.Ceiling(members.Count * FrameAgree);
            for (int k = 0; k < mask.Length; k++)
            {
                int agree = 0;
                foreach (int m in members) if (Diff(pics[m]![k], refPic[k]) < FrameTolerance) agree++;
                mask[k] = agree >= need;
            }
            mask = CleanFrame(mask);
            int id = frames.Count;
            foreach (int m in members) frameOf[m] = id;
            string culture = members.GroupBy(m => cities.CultureOf(m) is { Length: > 0 } c ? c : "기타")
                                    .MaxBy(g => g.Count())!.Key;
            string label = $"{id + 1:00}. {culture} — {cities.NameOf(members[0])}"
                           + (members.Count > 1 ? $" 외 {members.Count - 1}곳" : "");
            frames.Add(new FrameStyle(id, members, mask, label));
        }
        return (frames, frameOf);
    }

    /// <summary>바깥 가장자리에 닿은 덩어리만 남기고, 안쪽 풍경과 이어지지 않은 구멍은 메운다.</summary>
    private static bool[] CleanFrame(bool[] mask)
    {
        var keep = new bool[mask.Length];
        var stack = new Stack<int>();
        void Seed(int k) { if (mask[k] && !keep[k]) { keep[k] = true; stack.Push(k); } }
        for (int x = 0; x < W; x++) { Seed(x); Seed((H - 1) * W + x); }
        for (int y = 0; y < H; y++) { Seed(y * W); Seed(y * W + W - 1); }
        Flood(stack, k => mask[k] && !keep[k], k => keep[k] = true);

        // 안쪽 풍경 — 가운데 사각에서 테두리 아닌 점을 따라 번진다. 닿지 못한 점은 테두리 속 구멍이다.
        var open = new bool[mask.Length];
        for (int y = H / 4; y < H * 3 / 4; y++)
            for (int x = W / 4; x < W * 3 / 4; x++)
            {
                int k = y * W + x;
                if (!keep[k] && !open[k]) { open[k] = true; stack.Push(k); }
            }
        Flood(stack, k => !keep[k] && !open[k], k => open[k] = true);
        for (int k = 0; k < mask.Length; k++) keep[k] = !open[k];
        return keep;
    }

    private static void Flood(Stack<int> stack, Func<int, bool> can, Action<int> take, int width = W, int height = H)
    {
        while (stack.Count > 0)
        {
            int k = stack.Pop(), x = k % width, y = k / width;
            if (x > 0 && can(k - 1)) { take(k - 1); stack.Push(k - 1); }
            if (x < width - 1 && can(k + 1)) { take(k + 1); stack.Push(k + 1); }
            if (y > 0 && can(k - width)) { take(k - width); stack.Push(k - width); }
            if (y < height - 1 && can(k + width)) { take(k + width); stack.Push(k + width); }
        }
    }

    // ── 그림 원본 ─────────────────────────────────────────────────────────

    /// <summary>그 도시 그림 — 1배는 도트 그대로, 4배는 업스케일 그림(없으면 점을 네 배로 늘린 것).</summary>
    private uint[] Source(int city, int scale)
    {
        var pic = _pics[city] ?? new uint[W * H];
        if (scale == 1) return pic;
        if (_hdSource.TryGetValue(city, out var got)) return got;

        uint[]? hd = null;
        if (_hd[city] && CityUpscale.TryLoad(pic, W, H) is { } image)
        {
            BitmapSource src = image.Format == PixelFormats.Bgra32 ? image : new FormatConvertedBitmap(image, PixelFormats.Bgra32, null, 0);
            if (src.PixelWidth == W * scale && src.PixelHeight == H * scale)
            {
                hd = new uint[W * scale * H * scale];
                src.CopyPixels(hd, W * scale * 4, 0);
            }
        }
        if (hd == null)
        {
            hd = new uint[W * scale * H * scale];
            for (int y = 0; y < H * scale; y++)
                for (int x = 0; x < W * scale; x++)
                    hd[y * W * scale + x] = pic[y / scale * W + x / scale];
        }
        // 한 장이 8MB 다 — 몇 장만 쥔다.
        _hdSource[city] = hd;
        _hdOrder.Enqueue(city);
        while (_hdOrder.Count > 6) _hdSource.Remove(_hdOrder.Dequeue());
        return hd;
    }

    // ── 가리개 ───────────────────────────────────────────────────────────

    /// <summary>건물 그림의 가리개(96x80) — 점이 덜 맞아 빈 안쪽 구멍을 메운다.</summary>
    private bool[] SpriteMask(BuildingSprites.Sprite sprite)
    {
        if (_spriteMask.TryGetValue(sprite, out var got)) return got;
        const int bw = BuildingSprites.BoxW, bh = BuildingSprites.BoxH;
        var solid = new bool[bw * bh];
        for (int k = 0; k < solid.Length; k++) solid[k] = sprite.Bgra[k] != 0;
        // 상자 가장자리에서 빈 점을 따라 번진 곳이 바깥이고, 못 닿은 빈 점은 구멍이다.
        var outside = new bool[solid.Length];
        var stack = new Stack<int>();
        void Seed(int k) { if (!solid[k] && !outside[k]) { outside[k] = true; stack.Push(k); } }
        for (int x = 0; x < bw; x++) { Seed(x); Seed((bh - 1) * bw + x); }
        for (int y = 0; y < bh; y++) { Seed(y * bw); Seed(y * bw + bw - 1); }
        Flood(stack, k => !solid[k] && !outside[k], k => outside[k] = true, bw, bh);
        var mask = new bool[solid.Length];
        for (int k = 0; k < mask.Length; k++) mask[k] = !outside[k];
        return _spriteMask[sprite] = mask;
    }

    /// <summary>1배 가리개를 <paramref name="scale"/> 배로 — 쌍선형으로 늘린 뒤 가장자리만 부드럽게 남긴다.</summary>
    private static byte[] SoftAlpha(bool[] mask, int mw, int mh, int scale)
    {
        var alpha = new byte[mw * scale * mh * scale];
        if (scale == 1)
        {
            for (int k = 0; k < mask.Length; k++) alpha[k] = mask[k] ? (byte)255 : (byte)0;
            return alpha;
        }
        float M(int x, int y) => x >= 0 && y >= 0 && x < mw && y < mh && mask[y * mw + x] ? 1f : 0f;
        int ow = mw * scale;
        for (int y = 0; y < mh * scale; y++)
        {
            float fy = (y + 0.5f) / scale - 0.5f;
            int y0 = (int)Math.Floor(fy);
            float ty = fy - y0;
            for (int x = 0; x < ow; x++)
            {
                float fx = (x + 0.5f) / scale - 0.5f;
                int x0 = (int)Math.Floor(fx);
                float tx = fx - x0;
                float v = (M(x0, y0) * (1 - tx) + M(x0 + 1, y0) * tx) * (1 - ty)
                        + (M(x0, y0 + 1) * (1 - tx) + M(x0 + 1, y0 + 1) * tx) * ty;
                float a = Math.Clamp((v - 0.5f) * 2.5f + 0.5f, 0f, 1f);
                alpha[y * ow + x] = (byte)(a * 255 + 0.5f);
            }
        }
        return alpha;
    }

    // ── 겹 ───────────────────────────────────────────────────────────────

    /// <summary>건물 그림(96x80 의 <paramref name="scale"/> 배) — 기준 건물 도시 그림에서 오려 낸 점과 비침.</summary>
    public (uint[] Bgra, byte[] Alpha) Icon(BuildingSprites.Sprite sprite, int scale)
    {
        if (_layers.TryGetValue((sprite, scale), out var got)) return got;
        const int bw = BuildingSprites.BoxW, bh = BuildingSprites.BoxH;
        var src = Source(sprite.Reference.City, scale);
        int sw = W * scale, ow = bw * scale, oh = bh * scale;
        var bgra = new uint[ow * oh];
        for (int y = 0; y < oh; y++)
            for (int x = 0; x < ow; x++)
            {
                int gx = sprite.Reference.X * scale + x, gy = sprite.Reference.Y * scale + y;
                if (gx >= 0 && gy >= 0 && gx < sw && gy < H * scale) bgra[y * ow + x] = src[gy * sw + gx];
            }
        return _layers[(sprite, scale)] = (bgra, SoftAlpha(SpriteMask(sprite), bw, bh, scale));
    }

    /// <summary>테두리 겹(400x320 의 <paramref name="scale"/> 배).</summary>
    public (uint[] Bgra, byte[] Alpha) FrameLayer(FrameStyle frame, int scale)
    {
        if (_layers.TryGetValue((frame, scale), out var got)) return got;
        var src = Source(frame.RefCity, scale);
        return _layers[(frame, scale)] = ((uint[])src.Clone(), SoftAlpha(frame.Mask, W, H, scale));
    }

    /// <summary>
    /// 건물과 테두리를 걷어 낸 배경(400x320 의 <paramref name="scale"/> 배). 그 갈래에서 혼자라 그림을 못 뽑은 건물은 남는다.
    /// </summary>
    public uint[] Background(int city, int scale)
    {
        if (_clean.TryGetValue((city, scale), out var got)) return got;
        // 1배 사본 — 메운 자리를 다음 건물이 견줄 재료로 쓰려고 함께 고쳐 간다.
        var work = (uint[])(_pics[city] ?? new uint[W * H]).Clone();
        var canvas = (uint[])Source(city, scale).Clone();
        var frame = FrameOf(city)?.Mask ?? new bool[W * H];

        // 건물마다 가리개 — 한 점 번지게 해 가장자리 섞인 색까지 덮는다.
        var masks = new List<List<int>>();
        foreach (var b in Table.InCity(city))
        {
            if (!_spriteOf.TryGetValue(b, out var sprite)) continue;
            var m = SpriteMask(sprite);
            var one = new bool[W * H];
            for (int y = 0; y < BuildingSprites.BoxH; y++)
                for (int x = 0; x < BuildingSprites.BoxW; x++)
                {
                    int gx = b.X + x, gy = b.Y + y;
                    if (m[y * BuildingSprites.BoxW + x] && gx >= 0 && gy >= 0 && gx < W && gy < H) one[gy * W + gx] = true;
                }
            one = Dilate(one, 1);
            var list = new List<int>();
            for (int k = 0; k < one.Length; k++) if (one[k]) list.Add(k);
            if (list.Count > 0) masks.Add(list);
        }
        var bad = (bool[])frame.Clone();
        foreach (var m in masks) foreach (int k in m) bad[k] = true;

        // 작은 것부터 하나씩 메우고, 메운 자리는 풀어 다음 건물의 재료로 쓴다 — 빽빽한 도시(리스본)에서 옮길 곳이 없어지지 않게.
        foreach (var m in masks.OrderBy(m => m.Count))
        {
            Patch(canvas, scale, work, m, bad);
            foreach (int k in m) bad[k] = frame[k];
        }
        if (FrameOf(city) != null) FillFrame(canvas, scale, frame);
        if (_clean.Count > 8) _clean.Clear();
        return _clean[(city, scale)] = canvas;
    }

    private static bool[] Dilate(bool[] mask, int r)
    {
        var o = new bool[mask.Length];
        for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
            {
                if (!mask[y * W + x]) continue;
                for (int dy = -r; dy <= r; dy++)
                    for (int dx = -r; dx <= r; dx++)
                    {
                        int nx = x + dx, ny = y + dy;
                        if (nx >= 0 && ny >= 0 && nx < W && ny < H) o[ny * W + nx] = true;
                    }
            }
        return o;
    }

    /// <summary>
    /// 건물 하나를 옆 땅으로 덮는다 — 같은 높이(조금 위아래까지)에서 가로로 옮긴 자리 가운데 건물 · 테두리가 (거의) 없고
    /// 둘레 색이 가장 닮은 곳을 1배 점으로 고른 뒤, 그 배율 그림에서 통째로 베껴 가장자리를 섞는다. 옮길 곳이 없으면
    /// 둘레 땅 색을 네 방향에서 섞어 칠한다.
    /// </summary>
    private static void Patch(uint[] canvas, int scale, uint[] work, List<int> comp, bool[] bad)
    {
        int x0 = comp.Min(k => k % W), x1 = comp.Max(k => k % W), y0 = comp.Min(k => k / W), y1 = comp.Max(k => k / W);
        int bw = x1 - x0 + 1;
        var inComp = new HashSet<int>(comp);
        var ring = new List<int>();
        for (int y = Math.Max(0, y0 - 3); y <= Math.Min(H - 1, y1 + 3); y++)
            for (int x = Math.Max(0, x0 - 3); x <= Math.Min(W - 1, x1 + 3); x++)
            {
                int k = y * W + x;
                if (bad[k] || inComp.Contains(k)) continue;
                ring.Add(k);
            }

        int allowed = comp.Count / 50;
        (int Dx, int Dy, double Score)? best = null;
        int stepX = Math.Max(3, bw / 6);
        for (int d = bw / 2 + 2; d <= 220; d += stepX)
            foreach (int dx in new[] { -d, d })
                foreach (int dy in new[] { 0, -3, 3, -6, 6, -10, 10 })
                {
                    if (x0 + dx - 3 < 0 || x1 + dx + 3 >= W || y0 + dy - 3 < 0 || y1 + dy + 3 >= H) continue;
                    int hits = 0;
                    foreach (int k in comp) if (bad[k + dy * W + dx] && ++hits > allowed) break;
                    if (hits > allowed) continue;
                    double score = 0;
                    foreach (int k in ring) score += Diff(work[k], work[k + dy * W + dx]);
                    score = score / Math.Max(1, ring.Count) + Math.Abs(dx) * 0.03 + Math.Abs(dy) * 0.8;
                    if (best == null || score < best.Value.Score) best = (dx, dy, score);
                }

        // 가리개를 그 배율로 늘리고 한 점만큼 번지게 해 가장자리를 섞는다.
        int pad = 2, mw = bw + pad * 2, mh = y1 - y0 + 1 + pad * 2;
        var local = new bool[mw * mh];
        foreach (int k in comp) local[(k / W - y0 + pad) * mw + (k % W - x0 + pad)] = true;
        var grown = new bool[local.Length];
        for (int y = 0; y < mh; y++)
            for (int x = 0; x < mw; x++)
                if (local[y * mw + x])
                    for (int dy = -1; dy <= 1; dy++)
                        for (int dx = -1; dx <= 1; dx++)
                        {
                            int nx = x + dx, ny = y + dy;
                            if (nx >= 0 && ny >= 0 && nx < mw && ny < mh) grown[ny * mw + nx] = true;
                        }
        var alpha = SoftAlpha(grown, mw, mh, scale);

        int sw = W * scale, sh = H * scale, aw = mw * scale, ah = mh * scale;
        int ox = (x0 - pad) * scale, oy = (y0 - pad) * scale;

        if (best is { } b)
        {
            var copy = new uint[aw * ah];
            for (int y = 0; y < ah; y++)
                for (int x = 0; x < aw; x++)
                {
                    int sx = Math.Clamp(ox + x + b.Dx * scale, 0, sw - 1), sy = Math.Clamp(oy + y + b.Dy * scale, 0, sh - 1);
                    copy[y * aw + x] = canvas[sy * sw + sx];
                }
            Mix(canvas, sw, sh, copy, alpha, aw, ah, ox, oy);
            var w2 = (uint[])work.Clone();
            foreach (int k in comp) work[k] = w2[k + b.Dy * W + b.Dx];
            return;
        }

        // 옮길 곳이 없다 — 가리개 밖 가장 가까운 점을 네 방향에서 찾아 거리 반비례로 섞는다.
        var fill = new uint[aw * ah];
        bool Hole(int x, int y) => alpha[y * aw + x] > 0;
        for (int y = 0; y < ah; y++)
            for (int x = 0; x < aw; x++)
            {
                if (!Hole(x, y)) continue;
                double r = 0, g = 0, bl = 0, wsum = 0;
                foreach (var (sx, sy) in new[] { (-1, 0), (1, 0), (0, -1), (0, 1) })
                {
                    int cx = x, cy = y, dist = 0;
                    while (cx >= 0 && cy >= 0 && cx < aw && cy < ah && Hole(cx, cy)) { cx += sx; cy += sy; dist++; }
                    int gx = Math.Clamp(ox + cx, 0, sw - 1), gy = Math.Clamp(oy + cy, 0, sh - 1);
                    uint c = canvas[gy * sw + gx];
                    double wgt = 1.0 / (dist * dist + 1);
                    r += ((c >> 16) & 255) * wgt; g += ((c >> 8) & 255) * wgt; bl += (c & 255) * wgt; wsum += wgt;
                }
                fill[y * aw + x] = 0xFF000000u | ((uint)(r / wsum) << 16) | ((uint)(g / wsum) << 8) | (uint)(bl / wsum);
            }
        Mix(canvas, sw, sh, fill, alpha, aw, ah, ox, oy);
        foreach (int k in comp)
        {
            int x = k % W, y = k / W;
            work[k] = fill[Math.Min(ah - 1, (y - y0 + pad) * scale) * aw + Math.Min(aw - 1, (x - x0 + pad) * scale)];
        }
    }

    private static void Mix(uint[] canvas, int cw, int ch, uint[] layer, byte[] alpha, int w, int h, int ox, int oy)
    {
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                byte a = alpha[y * w + x];
                int gx = ox + x, gy = oy + y;
                if (a == 0 || gx < 0 || gy < 0 || gx >= cw || gy >= ch) continue;
                canvas[gy * cw + gx] = Lerp(canvas[gy * cw + gx], layer[y * w + x], a / 255.0);
            }
    }

    /// <summary>
    /// 테두리 자리를 안쪽 풍경으로 메운다 — 가운데로 걸어 들어가 풍경에 닿은 자리에서 그만큼 더 들어간 점(거울)을 쓴다.
    /// 그대로 늘이면 줄무늬가 지는데, 거울로 비추면 풍경 결이 남는다.
    /// </summary>
    private static void FillFrame(uint[] canvas, int scale, bool[] frame)
    {
        int sw = W * scale, sh = H * scale;
        double cx = sw / 2.0, cy = sh / 2.0;
        var before = (uint[])canvas.Clone();
        bool Frame(double px, double py) => frame[Math.Clamp((int)py / scale, 0, H - 1) * W + Math.Clamp((int)px / scale, 0, W - 1)];
        for (int y = 0; y < sh; y++)
            for (int x = 0; x < sw; x++)
            {
                if (!frame[y / scale * W + x / scale]) continue;
                double dx = cx - x, dy = cy - y, len = Math.Sqrt(dx * dx + dy * dy);
                if (len < 1) continue;
                dx /= len; dy /= len;
                int i = 0;
                while (i < len && Frame(x + dx * i, y + dy * i)) i++;
                // 풍경에 닿은 자리에서 그만큼 더 — 가운데를 넘지 않고, 거기가 또 테두리면 닿은 자리 바로 안쪽.
                double t = Math.Min(len, i * 2.0 + scale);
                if (Frame(x + dx * t, y + dy * t)) t = Math.Min(len, i + scale * 2.0);
                int qx = Math.Clamp((int)(x + dx * t), 0, sw - 1), qy = Math.Clamp((int)(y + dy * t), 0, sh - 1);
                canvas[y * sw + x] = before[qy * sw + qx];
            }
    }

    private static uint Lerp(uint a, uint b, double t)
    {
        int C(int shift) => (int)Math.Round(((a >> shift) & 255) * (1 - t) + ((b >> shift) & 255) * t);
        return 0xFF000000u | ((uint)C(16) << 16) | ((uint)C(8) << 8) | (uint)C(0);
    }

    // ── 짜 맞추기 ─────────────────────────────────────────────────────────

    /// <summary>배경 위에 건물(아래쪽 것이 앞)과 테두리를 얹는다. 결과는 400x320 의 <paramref name="scale"/> 배 BGRA.</summary>
    public uint[] Compose(int backgroundCity, FrameStyle? frame, IEnumerable<Placed> placed, int scale)
    {
        var canvas = (uint[])Background(backgroundCity, scale).Clone();
        int sw = W * scale, sh = H * scale;
        foreach (var p in placed.OrderBy(p => p.Y))
        {
            var (bgra, alpha) = Icon(p.Sprite, scale);
            Blend(canvas, sw, sh, bgra, alpha, BuildingSprites.BoxW * scale, BuildingSprites.BoxH * scale, p.X * scale, p.Y * scale);
        }
        if (frame != null)
        {
            var (bgra, alpha) = FrameLayer(frame, scale);
            Blend(canvas, sw, sh, bgra, alpha, sw, sh, 0, 0);
        }
        return canvas;
    }

    private static void Blend(uint[] canvas, int cw, int ch, uint[] bgra, byte[] alpha, int w, int h, int ox, int oy)
    {
        for (int y = 0; y < h; y++)
        {
            int gy = oy + y;
            if (gy < 0 || gy >= ch) continue;
            for (int x = 0; x < w; x++)
            {
                int gx = ox + x;
                if (gx < 0 || gx >= cw) continue;
                byte a = alpha[y * w + x];
                if (a == 0) continue;
                int k = gy * cw + gx;
                canvas[k] = a == 255 ? bgra[y * w + x] | 0xFF000000u : Lerp(canvas[k], bgra[y * w + x], a / 255.0);
            }
        }
    }

    // ── 내보내기 ─────────────────────────────────────────────────────────

    public static BitmapSource ToBitmap(uint[] bgra, int width, int height)
    {
        var bmp = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, bgra, width * 4);
        bmp.Freeze();
        return bmp;
    }

    /// <summary>비침을 알파 통로에 넣은 그림.</summary>
    public static BitmapSource ToBitmap(uint[] bgra, byte[] alpha, int width, int height)
    {
        var px = new uint[bgra.Length];
        for (int k = 0; k < px.Length; k++) px[k] = (bgra[k] & 0x00FFFFFFu) | ((uint)alpha[k] << 24);
        return ToBitmap(px, width, height);
    }

    public static void SavePng(BitmapSource bmp, string path)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bmp));
        using var file = File.Create(path);
        encoder.Save(file);
    }

    /// <summary>
    /// 건물 그림 · 테두리를 PNG 로 모두 적는다 — <paramref name="folder"/>\buildings\문화권_갈래_변형.png ·
    /// frames\frame_NN.png, 목록은 list.txt. 받는 사람이 다듬어 쓸 수 있게 비침은 알파로 둔다.
    /// </summary>
    public int ExportLayers(string folder, int scale, Action<int, int>? progress = null)
    {
        string buildings = Path.Combine(folder, "buildings"), frames = Path.Combine(folder, "frames");
        Directory.CreateDirectory(buildings);
        Directory.CreateDirectory(frames);
        var list = new StringBuilder();
        list.AppendLine($"# 도시 그림에서 뽑은 겹 — {scale}배, 비침은 알파. 건물 상자 96x80 · 그림 400x320 의 {scale}배.");
        int total = Sprites.Count + Frames.Count, done = 0;
        foreach (var s in Sprites.OrderBy(s => s.Reference.City))
        {
            var (bgra, alpha) = Icon(s, scale);
            SavePng(ToBitmap(bgra, alpha, BuildingSprites.BoxW * scale, BuildingSprites.BoxH * scale), Path.Combine(buildings, s.FileName));
            list.AppendLine($"buildings/{s.FileName}  기준 {Cities.NameOf(s.Reference.City)}  도시 {s.Members.Count}곳: "
                            + string.Join(", ", s.Members.Select(m => Cities.NameOf(m.City))));
            progress?.Invoke(++done, total);
        }
        foreach (var f in Frames.OrderBy(f => f.RefCity))
        {
            var (bgra, alpha) = FrameLayer(f, scale);
            SavePng(ToBitmap(bgra, alpha, W * scale, H * scale), Path.Combine(frames, f.FileName));
            list.AppendLine($"frames/{f.FileName}  {f.Label}: " + string.Join(", ", f.Cities.Select(Cities.NameOf)));
            progress?.Invoke(++done, total);
        }
        File.WriteAllText(Path.Combine(folder, "list.txt"), list.ToString(), new UTF8Encoding(true));
        // 4배 겹을 다 쥐면 수백 MB 다 — 내보낸 뒤에는 놓는다.
        _layers.Clear();
        return done;
    }
}
