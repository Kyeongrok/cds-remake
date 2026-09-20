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
/// 게임 EXE 는 건드리지 않는다 — 이 앱이 품고 있는 놀이(조선소 → 구입)에만 나온다.
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
    public static string FolderOf(string id) => Path.Combine(Root, id);

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
            // 읽다 넘어져도 놀이는 붙박이 다섯으로 굴러가야 한다.
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
            hulls.Add(over.TryGetValue(built.Name, out var edit) ? Overlay(built, edit) : built);

        var taken = hulls.Select(h => h.Name).ToHashSet();

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
        Id = $"builtin-{built.Name}",
        Builtin = built.Name,
        Name = built.Name,
        Hp = built.Hp,
        Speed = built.Speed,
        Capacity = built.Capacity,
        Tonnage = built.Tonnage,
        Crew = built.Crew,
        Guns = built.Guns,
        Price = built.Price,
        MaxMasts = built.MaxMasts,
        CanChangeSail = built.CanChangeSail,
    };

    public static Hull ToHull(Design design) => new(
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
        SpriteFolder: FolderOf(design.Id));

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
