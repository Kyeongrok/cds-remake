using System.IO;
using System.Text.Json;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CdsHelper.Support.Local.Models;

namespace CdsHelper.Support.Local.Helpers;

/// <summary>
/// 조선소에 낼 배를 손으로 등록해 두는 곳.
/// </summary>
/// <remarks>
/// 게임 EXE 는 건드리지 않는다 — 이 앱이 품고 있는 게임(조선소 → 구입)에만 나온다.
/// 스펙은 <c>%APPDATA%\CdsHelper\ships\ships.json</c> 에, 8방향 그림은 그 옆
/// <c>ships\{Id}\ship_0.png</c> ~ <c>ship_7.png</c> 에 둔다. 그림은 넣을 때
/// <see cref="SpriteWidth"/> 크기로 맞춰 굽는다 — 게임 그림이 48x48 이라서다.
/// </remarks>
public static class ShipRegistry
{
    /// <summary>그림 한 장의 한 변. <c>ShipSprites.Width</c> 와 같아야 한다.</summary>
    public const int SpriteWidth = 48;

    /// <summary>방향 수. 게임 16방향을 둘로 접은 것이다.</summary>
    public const int Directions = 8;

    /// <summary>0 북에서 반시계로 돈다 — 그림 번호와 짝이다.</summary>
    public static readonly string[] DirectionNames =
        ["북", "북서", "서", "남서", "남", "남동", "동", "북동"];

    /// <summary>등록해 넣은 배 한 척의 스펙. 이대로 json 이 된다.</summary>
    public sealed class Design
    {
        /// <summary>폴더 이름으로도 쓴다. 한 번 정하면 안 바꾼다.</summary>
        public string Id { get; set; } = "";

        public string Name { get; set; } = "";
        public int Hp { get; set; } = 30;
        public int Speed { get; set; } = 50;

        /// <summary>
        /// 추진력 한계 — 돛을 더 달아 올릴 수 있는 끝(<see cref="Hull.SpeedCeiling"/>). 0 이면 추진력의 <see cref="Ship.RefitCeiling"/> 배다.
        /// 붙박이 다섯은 선체표(<c>exe-tables/hulls.json</c>)의 한계를 쓴다 — 편집기에서 고치면 그 표에 적는다.
        /// </summary>
        public int SpeedTop { get; set; }
        public int Capacity { get; set; } = 200;
        public int Tonnage { get; set; } = 1750;
        public int Crew { get; set; } = 20;
        public int Guns { get; set; } = 6;
        public int Price { get; set; } = 300;
        public int MaxMasts { get; set; } = Hull.MastLimit;
        public bool CanChangeSail { get; set; } = true;

        /// <summary>
        /// 붙박이 선체를 덧씌우는 줄이면 그 <b>본디 이름</b>. 새로 등록한 배면 null 이다.
        /// </summary>
        /// <remarks>
        /// 붙박이 다섯(갤리온·중카락·카락·대형카라벨·카라벨)의 값도 고칠 수 있어야 해서
        /// 둔다. 그림은 그대로 <see cref="Hull.Skin"/> 을 쓰므로 이 줄에는 그림이 없다.
        /// EXE 도 <c>Hull.Builtin</c> 도 손대지 않고 <see cref="BuildHulls"/> 가 얹는다.
        /// </remarks>
        public string? Builtin { get; set; }

        /// <summary>앱에 실은 배인지(<see cref="ShipRegistry.IsBundled"/>). 적지 않는다.</summary>
        [System.Text.Json.Serialization.JsonIgnore]
        public bool IsBundled => ShipRegistry.IsBundled(Id);

        /// <summary>선체 동영상 줄기(<see cref="Hull.Movie"/>). 없으면 동영상 없이 나온다.</summary>
        public string? Movie { get; set; }

        /// <summary>파는 조선소의 도시 번호(<see cref="Hull.Cities"/>). null 이면 모든 조선소, 비어 있으면 어디서도 안 판다.</summary>
        public int[]? Cities { get; set; }

        /// <summary>붙박이를 덧씌우는 줄인지.</summary>
        [System.Text.Json.Serialization.JsonIgnore]
        public bool IsBuiltin => !string.IsNullOrEmpty(Builtin);

        public Design Copy() => (Design)MemberwiseClone();
    }

    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>등록해 넣은 배들이 사는 폴더.</summary>
    public static string Root { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "CdsHelper", "ships");

    public static string ListPath => Path.Combine(Root, "ships.json");

    /// <summary>배 하나의 그림이 든 폴더.</summary>
    /// <remarks>앱에 실은 배(<see cref="IsBundled"/>)면 그 폴더(<see cref="BundledRoot"/>)다 — 편집기에서 그림을 갈아도 그 자리에 든다.</remarks>
    public static string FolderOf(string id) => IsBundled(id) ? Path.Combine(BundledRoot, id) : Path.Combine(Root, id);

    /// <summary>앱에 실은 배인지 — <see cref="BundledRoot"/> 밑에 그 이름의 폴더와 <c>design.json</c> 이 있다.</summary>
    public static bool IsBundled(string id) =>
        !string.IsNullOrEmpty(id) && File.Exists(Path.Combine(BundledRoot, id, "design.json"));

    /// <summary>앱에 실은 배의 값을 적는다(<c>design.json</c>). 그림은 그 폴더에 그대로 둔다.</summary>
    public static void SaveBundled(Design design)
    {
        string folder = Path.Combine(BundledRoot, design.Id);
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "design.json"), JsonSerializer.Serialize(design, Json));
        Hull.Reload();
    }

    /// <summary>그림 한 장의 자리.</summary>
    public static string SpritePath(string id, int direction) =>
        Path.Combine(FolderOf(id), $"ship_{direction}.png");

    /// <summary>여덟 장이 다 있는지.</summary>
    public static bool HasAllSprites(string id) =>
        Enumerable.Range(0, Directions).All(i => File.Exists(SpritePath(id, i)));

    /// <summary>적어 둔 배를 다 읽는다. 파일이 없거나 깨졌으면 빈 목록.</summary>
    public static List<Design> Load()
    {
        try
        {
            if (!File.Exists(ListPath)) return [];

            return JsonSerializer.Deserialize<List<Design>>(File.ReadAllText(ListPath), Json)
                   ?? [];
        }
        catch
        {
            // 읽다 넘어져도 게임은 붙박이 다섯으로 굴러가야 한다.
            return [];
        }
    }

    /// <summary>목록을 적어 둔다. 적고 나면 <see cref="Hull.All"/> 도 다시 읽게 만든다.</summary>
    public static void Save(IEnumerable<Design> designs)
    {
        Directory.CreateDirectory(Root);
        File.WriteAllText(ListPath, JsonSerializer.Serialize(designs.ToList(), Json));
        Hull.Reload();
    }

    /// <summary>배 하나를 그림 폴더째 지운다.</summary>
    public static void Delete(string id)
    {
        Save(Load().Where(d => d.Id != id));

        try
        {
            if (Directory.Exists(FolderOf(id))) Directory.Delete(FolderOf(id), recursive: true);
        }
        catch
        {
            // 그림을 못 치워도 목록에서는 빠졌으니 조선소에는 안 나온다.
        }
    }

    /// <summary>겹치지 않는 새 Id 하나.</summary>
    public static string NewId() => "ship-" + Guid.NewGuid().ToString("N")[..8];

    /// <summary>
    /// 조선소에 낼 선체 전부 — 붙박이 다섯 뒤에 등록해 넣은 배를 얹고 값순으로 세운다.
    /// </summary>
    /// <remarks>
    /// 이름이 겹치면 등록해 넣은 쪽을 버린다. 세이브를 되돌릴 때 선체를 이름으로 찾기 때문에
    /// (<c>Player.RestoreFleet</c>) 같은 이름이 둘이면 어느 쪽인지 가릴 수 없다.
    /// 그림이 여덟 장 다 차지 않은 배도 뺀다 — 반쪽짜리로 바다에 내보낼 수는 없다.
    /// </remarks>
    /// <remarks>
    /// 붙박이 다섯을 밑에 깔고, <b>덧씌우는 줄</b>이 있으면 그 값으로 갈아 낸 뒤 새로
    /// 등록한 배를 얹는다. 덧씌운 줄은 그림을 안 갖는다 — 붙박이의 그림벌을 그대로 쓴다.
    /// </remarks>
    public static Hull[] BuildHulls()
    {
        var saved = Load();
        var over = saved.Where(d => d.IsBuiltin)
                        .GroupBy(d => d.Builtin!)
                        .ToDictionary(g => g.Key, g => g.Last());

        var hulls = new List<Hull>();
        foreach (var built in Hull.Builtin)
            // 덧씌움은 선체 id 로 찾는다 — id 를 적기 전 줄은 이름이 들어 있다.
            hulls.Add(over.TryGetValue(built.Ident, out var edit) || over.TryGetValue(built.Name, out edit) ? Overlay(built, edit) : built);

        var taken = hulls.Select(h => h.Name).ToHashSet();

        // 앱에 같이 싣는 배(asset/ship-added) — 손으로 등록한 배보다 먼저 이름을 잡는다.
        foreach (var (design, folder) in LoadBundled())
            if (!string.IsNullOrWhiteSpace(design.Name) && taken.Add(design.Name))
                hulls.Add(ToHull(design, folder) with { Bundled = true });

        foreach (var design in saved)
        {
            if (design.IsBuiltin) continue;
            if (string.IsNullOrWhiteSpace(design.Name) || !taken.Add(design.Name)) continue;
            if (!HasAllSprites(design.Id)) continue;

            hulls.Add(ToHull(design));
        }

        // 값이 비싼 쪽이 위 — 붙박이 표가 그 차례라 얹은 배도 같은 줄에 세운다.
        return [.. hulls.OrderByDescending(h => h.Price)];
    }

    /// <summary>
    /// 붙박이 선체를 고친 값을 선체표(<c>exe-tables/hulls.json</c>, <see cref="HullTable"/>)에 적는다 — 커밋하면 업데이트로 모두에게 간다.
    /// 이름도 바꾼다 — 세이브는 선체를 id 로 적는다. 이 PC 에 따로 적어 둔 덧씌움은 걷어 표 값이 그대로 보이게 한다.
    /// </summary>
    public static void SaveBuiltin(Design edit)
    {
        var rows = Hull.Rows.ToArray();
        int at = Array.FindIndex(rows, r => r.Key == edit.Builtin || r.Name == edit.Builtin);
        if (at < 0) return;
        rows[at] = rows[at] with
        {
            Name = string.IsNullOrWhiteSpace(edit.Name) ? rows[at].Name : edit.Name.Trim(),
            Hp = edit.Hp, Speed = edit.Speed, Capacity = edit.Capacity, Tonnage = edit.Tonnage, Crew = edit.Crew,
            GunsMin = edit.Guns, Price = edit.Price, MaxMasts = Math.Clamp(edit.MaxMasts, 1, Hull.MastLimit),
            CanChangeSail = edit.CanChangeSail,
            SpeedTop = edit.SpeedTop > 0 ? Math.Max(edit.SpeedTop, edit.Speed) : rows[at].SpeedTop,
        };
        HullTable.Save(rows);
        var saved = Load();
        string key = rows[at].Key;
        if (saved.RemoveAll(d => d.Builtin == key || d.Builtin == edit.Builtin) > 0) Save(saved);
        Hull.Reload();
    }

    /// <summary>덧씌우는 줄이 가리키는 붙박이 선체 — id 로, 없으면 이름으로(id 를 적기 전 줄).</summary>
    public static Hull? BuiltinOf(Design design) =>
        Hull.Builtin.FirstOrDefault(h => h.Ident == design.Builtin) ?? Hull.Builtin.FirstOrDefault(h => h.Name == design.Builtin);

    /// <summary>붙박이 선체에 고친 값을 얹는다. 그림벌(<see cref="Hull.Skin"/>)은 그대로다.</summary>
    public static Hull Overlay(Hull built, Design edit) => built with
    {
        Name = string.IsNullOrWhiteSpace(edit.Name) ? built.Name : edit.Name,
        Hp = edit.Hp,
        Speed = edit.Speed,
        Capacity = edit.Capacity,
        Tonnage = edit.Tonnage,
        Crew = edit.Crew,
        Guns = edit.Guns,
        Price = edit.Price,
        MaxMasts = Math.Clamp(edit.MaxMasts, 1, Hull.MastLimit),
        CanChangeSail = edit.CanChangeSail,
    };

    /// <summary>붙박이 선체를 고칠 줄을 짓는다 — 지금 값을 그대로 담는다.</summary>
    public static Design FromBuiltin(Hull built) => new()
    {
        Id = $"builtin-{built.Ident}",
        Builtin = built.Ident,
        Name = built.Name,
        Hp = built.Hp,
        Speed = built.Speed,
        SpeedTop = built.SpeedTop,
        Capacity = built.Capacity,
        Tonnage = built.Tonnage,
        Crew = built.Crew,
        Guns = built.Guns,
        Price = built.Price,
        MaxMasts = built.MaxMasts,
        CanChangeSail = built.CanChangeSail,
    };

    /// <summary>
    /// 앱에 같이 싣는 배가 사는 폴더 — <c>asset/ship-added/{Id}/</c> 에 <c>design.json</c>(이 <see cref="Design"/>)과 8방향 그림
    /// (<c>ship_0.png</c> ~, 고해상도 <c>ship_0_hd.png</c> ~)이 든다. 손으로 등록한 배(<see cref="Root"/>)와 달리 업데이트로 모두에게 간다.
    /// 폴더 이름은 ASCII 로 둔다(Velopack 차등 업데이트).
    /// </summary>
    /// <remarks>
    /// 저장소에서 빌드해 띄웠으면(위로 올라가 <c>.git</c> 이 있는 폴더) <b>저장소의 <c>asset/ship-added</c></b> 를 쓴다 — 편집기 「요소 → 선박」에서
    /// 고친 값이 빌드 출력이 아니라 커밋할 파일에 바로 남게. 깔린 앱이면 실행 폴더 옆의 것이다.
    /// </remarks>
    public static string BundledRoot => _bundledRoot ??= FindBundledRoot();

    private static string? _bundledRoot;

    private static string FindBundledRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
        {
            string source = Path.Combine(dir.FullName, "asset", "ship-added");
            if (Directory.Exists(Path.Combine(dir.FullName, ".git")) && Directory.Exists(source)) return source;
        }
        return Path.Combine(AppContext.BaseDirectory, "asset", "ship-added");
    }

    /// <summary>앱에 같이 싣는 배를 다 읽는다. 그림 여덟 장이 다 든 것만.</summary>
    public static List<(Design Design, string Folder)> LoadBundled()
    {
        var list = new List<(Design, string)>();
        if (!Directory.Exists(BundledRoot)) return list;
        foreach (var folder in Directory.GetDirectories(BundledRoot).Order(StringComparer.Ordinal))
        {
            try
            {
                string spec = Path.Combine(folder, "design.json");
                if (!File.Exists(spec)) continue;
                if (!Enumerable.Range(0, Directions).All(i => File.Exists(Path.Combine(folder, $"ship_{i}.png")))) continue;
                if (JsonSerializer.Deserialize<Design>(File.ReadAllText(spec), Json) is { } design) list.Add((design, folder));
            }
            catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
            {
                // 한 척이 깨져도 나머지는 나온다.
            }
        }
        return list;
    }

    public static Hull ToHull(Design design) => ToHull(design, FolderOf(design.Id));

    /// <summary>등록 줄을 선체로 — 그림은 <paramref name="folder"/> 에서 읽는다.</summary>
    public static Hull ToHull(Design design, string folder) => new(
        design.Name,
        design.Hp,
        design.Speed,
        design.Capacity,
        design.Tonnage,
        design.Crew,
        design.Guns,
        design.Price,
        Skin: -1,
        MaxMasts: Math.Clamp(design.MaxMasts, 1, Hull.MastLimit),
        CanChangeSail: design.CanChangeSail,
        SpeedTop: design.SpeedTop > 0 ? Math.Max(design.SpeedTop, design.Speed) : 0,
        SpriteFolder: folder,
        Movie: design.Movie,
        Cities: design.Cities,
        Key: design.Id);

    /// <summary>
    /// 그림 한 장을 배의 그림 폴더에 <see cref="SpriteWidth"/> 크기로 맞춰 넣는다.
    /// </summary>
    /// <remarks>
    /// 잘라 온 조각이 딱 48x48 일 리 없으므로 비율 그대로 줄여 한가운데에 놓고 둘레는 비워 둔다.
    /// 원본보다 키우지는 않는다 — 작은 그림을 늘려 봐야 흐려지기만 한다.
    /// </remarks>
    public static void ImportSprite(string id, int direction, string sourcePath)
    {
        var image = LoadFitted(sourcePath);

        Directory.CreateDirectory(FolderOf(id));

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(image));

        string target = SpritePath(id, direction);
        string temp = target + ".tmp";
        using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            encoder.Save(stream);
        }

        File.Move(temp, target, overwrite: true);
    }

    /// <summary>넣어 둔 그림 한 장을 읽는다. 없으면 null.</summary>
    /// <summary>
    /// 붙박이 선체가 쓰는 그림 자리 — <c>asset/ship-g{벌}/ship_{쪽}.png</c> 다.
    /// </summary>
    /// <remarks>등록해 넣은 배와 달리 제 폴더가 없다. 그림벌은 <see cref="Hull.Skin"/> 이다.</remarks>
    public static string BuiltinSpritePath(int skin, int direction) => Path.Combine(
        AppDomain.CurrentDomain.BaseDirectory, "asset", $"ship-g{Math.Max(0, skin)}",
        $"ship_{direction}.png");

    /// <summary>붙박이 선체의 그 쪽 그림. 없으면 null.</summary>
    public static BitmapSource? ReadBuiltinSprite(int skin, int direction) =>
        ReadFrom(BuiltinSpritePath(skin, direction));

    public static BitmapSource? ReadSprite(string id, int direction) =>
        ReadFrom(SpritePath(id, direction));

    private static BitmapSource? ReadFrom(string path)
    {
        if (!File.Exists(path)) return null;

        try
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.StreamSource = new MemoryStream(File.ReadAllBytes(path));
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>비율 그대로 48x48 안에 담고, 남는 자리는 비워 둔 그림 한 장.</summary>
    private static BitmapSource LoadFitted(string sourcePath)
    {
        var bytes = File.ReadAllBytes(sourcePath);

        int width, height;
        using (var probe = new MemoryStream(bytes, writable: false))
        {
            var frame = BitmapFrame.Create(probe, BitmapCreateOptions.DelayCreation, BitmapCacheOption.None);
            width = frame.PixelWidth;
            height = frame.PixelHeight;
        }

        if (width <= 0 || height <= 0) throw new InvalidOperationException("크기를 읽지 못했습니다");

        // 긴 변을 48 에 맞춘다. 이미 작으면 그대로 둔다.
        double scale = Math.Min(1.0, (double)SpriteWidth / Math.Max(width, height));
        int decodeWidth = Math.Max(1, (int)Math.Round(width * scale));

        BitmapSource decoded;
        using (var stream = new MemoryStream(bytes, writable: false))
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.StreamSource = stream;
            image.DecodePixelWidth = decodeWidth;
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
            image.EndInit();
            image.Freeze();
            decoded = image;
        }

        return CenterOn48(decoded);
    }

    /// <summary>48x48 빈 종이 한가운데에 얹는다.</summary>
    private static BitmapSource CenterOn48(BitmapSource image)
    {
        BitmapSource bgra = image.Format == PixelFormats.Bgra32
            ? image
            : new FormatConvertedBitmap(image, PixelFormats.Bgra32, null, 0);

        int w = Math.Min(bgra.PixelWidth, SpriteWidth), h = Math.Min(bgra.PixelHeight, SpriteWidth);
        int sourceStride = bgra.PixelWidth * 4;
        var pixels = new byte[sourceStride * bgra.PixelHeight];
        bgra.CopyPixels(pixels, sourceStride, 0);

        int stride = SpriteWidth * 4;
        var canvas = new byte[stride * SpriteWidth];
        int left = (SpriteWidth - w) / 2, top = (SpriteWidth - h) / 2;
        for (int y = 0; y < h; y++)
            Buffer.BlockCopy(pixels, y * sourceStride, canvas, (top + y) * stride + left * 4, w * 4);

        var fitted = BitmapSource.Create(SpriteWidth, SpriteWidth, 96, 96, PixelFormats.Bgra32, null, canvas, stride);
        fitted.Freeze();
        return fitted;
    }
}
