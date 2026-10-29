using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows.Media.Imaging;
using CdsHelper.Game.Engine.Disev;
using CdsHelper.Support.Local.Helpers;

namespace CdsHelper.Game.Local.Helpers;

/// <summary>
/// 사람이 지어 넣은 발견물 — 원본에 없는 것이다. 앱과 함께 실리는 표(<c>exe-tables/*-고친것.json</c>)에 섞지 않고
/// <c>%APPDATA%\CdsHelper\discoveries\{UUID}</c> 에 한 벌씩 두었다가 표를 읽을 때 얹는다 — 업데이트해도 남는다.
/// </summary>
/// <remarks>
/// 한 벌은 발견물 줄 · 힌트 · 술집 소문 · 책 · 설명 · 발견 장면 · 그림 · 출처를 다 든다. 번호는 들고 다니지 않는다.
/// <code>
///   신분     UUID — 지을 때 한 번 정하고 안 바뀐다. 같은 UUID 를 또 불러오면 새 판으로 덮는다
///   번호     발견물 · 일련번호 · 힌트 · 그림 번호를 UUID 로 셈해 2000~9999 에서 받는다(겹치면 다음 칸)
///            — 셈이 같아 PC 가 달라도 거의 늘 같은 번호라, 세이브를 옮겨도 찾은 기록이 맞는다
///   장면     대사 줄(Lines)로 짓고 발견 대본 파트로 굽는다. 대본 속 「이 발견물」 · 「이 그림」은 -1 로 적어 두고 구울 때 번호를 넣는다
/// </code>
/// 모드 「추가 발견물」을 켰을 때만 낸다(원본에 없던 줄과 같다).
/// </remarks>
public static class UserDiscoveries
{
    /// <summary>번호를 받는 구간 — 앱에 실린 추가 발견물(274~)과 원본 번호들보다 한참 위다.</summary>
    public const int NumberBase = 2000, NumberEnd = 9999;

    /// <summary>파일 모양 판. 2 부터 라이선스 · 잠금이 든다 — 잠금을 모르는 옛 판이 잠긴 발견물을 고치지 못하게 올렸다.</summary>
    public const int FormatVersion = 2;

    /// <summary>고를 수 있는 라이선스 — 크리에이티브 커먼즈.</summary>
    public static readonly string[] Licenses =
        ["CC BY-SA 4.0", "CC BY 4.0", "CC BY-NC 4.0", "CC BY-NC-SA 4.0", "CC0 1.0"];

    /// <summary>처음 고른 라이선스 — 위키백과 글(CC BY-SA)을 바탕으로 쓰는 일이 많아 그것과 맞춘다.</summary>
    public const string DefaultLicense = "CC BY-SA 4.0";

    /// <summary>장면 한 줄.</summary>
    /// <param name="Kind">
    /// 「say」 대사 · 「picture」 발견물 그림 · 「movie」 발견물 동영상 · 「found」 효과음과 「~을 발견했다！」, 발견 처리 ·
    /// 「battle」 바다 괴물과 해전(<see cref="SceneLine.Text"/> 가 괴물 이름, <see cref="Monsters"/>) ·
    /// 「land」 인물이 이끄는 무리와 육상전(<see cref="LandTerms"/>) ·
    /// 「lose」 싸움에서 지거나 놓쳤을 때의 갈래가 여기서부터다 · 「item」 아이템을 준다(글이 아이템 번호) · 「gameover」 게임 오버.
    /// </param>
    /// <param name="Speaker">대사 화자(부관 · 감찰관 · 항해사 …). 비우면 화자 없는 글.</param>
    public sealed record SceneLine(string Kind, string? Speaker = null, string Text = "");

    /// <summary>사람이 지은 발견물 한 벌 — 번호 없이 들고 다니는 모양.</summary>
    /// <param name="Category">발견물 갈래(<see cref="DiscoveryTable.CategoryNames"/>).</param>
    /// <param name="X1">세계 지도 칸 범위.</param>
    /// <param name="Chance">발견 확률 분모 — 1 이면 늘, 3 이면 세 번에 한 번.</param>
    /// <param name="Book">힌트를 실을 책 제목(박물지 따위). 비우면 책에 안 싣는다.</param>
    /// <param name="RumorCities">소문을 들려줄 도시 번호 셋까지.</param>
    /// <param name="Credit">그림 · 글의 출처와 라이선스.</param>
    public sealed record Entry(
        string Uuid, string Name, int Category, int Reward, bool OnLand, bool OpenAtStart,
        int X1, int Y1, int X2, int Y2,
        string HintName, int HintGrade, int HintCategory, int HintFunds, int HintDeadline, string HintText,
        int[] RumorCities, string RumorText, string Book, string Description,
        int Chance, List<SceneLine> Lines, string Credit = "", string Author = "", int Version = FormatVersion,
        string License = DefaultLicense, string Lock = "", bool HasHint = true);

    /// <summary>
    /// 장면의 「[해전] 이름」이 덤빌 수 있는 바다 괴물 — 인물 표 뒤쪽의 괴물 자리다(<see cref="Engine.Sea.EnemyFleet"/>).
    /// 원본 크라켄 발견(파트 197)이 <c>0D 0D 271</c> 로 덤비는 것과 같다.
    /// </summary>
    /// <summary>
    /// 「[육상전]」 줄의 글을 푼다 — 「아테네의 장군 상대 100~139 아군 70 그림 3」. 앞은 인물 이름(또는 번호)이고, 뒤는 고를 수 있는
    /// 「상대 병력」(수 하나나 a~b) · 「아군 병력」(그 수로 맞춘다) · 「일기토 그림 벌」(0~6)이다. 인물을 못 찾으면 Person 이 -1.
    /// </summary>
    /// <remarks>원본 파르테논(파트 23)이 <c>26 0F 3</c> · <c>26 1C 2=70</c> · <c>26 1C 16=100~139</c> 뒤에 <c>2F 0D 206</c> 로 싸운다.</remarks>
    public static (int Person, string Name, int FoeFrom, int FoeTo, int Ours, int DuelSet) LandTerms(string text)
    {
        var m = System.Text.RegularExpressions.Regex.Match(text.Trim(),
            @"^(?<who>.+?)(?:\s+상대\s+(?<f1>\d+)(?:\s*~\s*(?<f2>\d+))?)?(?:\s+아군\s+(?<our>\d+))?(?:\s+그림\s+(?<set>\d+))?\s*$");
        string who = m.Success ? m.Groups["who"].Value.Trim() : text.Trim();
        int person = int.TryParse(who, out int n) ? n
            : PersonTable.Open()?.People.FirstOrDefault(r => r.Name == who)?.Id ?? -1;
        int Int(string group, int fallback) => m.Success && m.Groups[group].Success ? int.Parse(m.Groups[group].Value) : fallback;
        int from = Int("f1", -1), to = Int("f2", from);
        return (person, who, from, Math.Max(from, to), Int("our", -1), Int("set", -1));
    }

    public static readonly (string Name, int Person)[] Monsters =
    [
        ("크라켄", Engine.Sea.EnemyFleet.Kraken), ("시서펜트", Engine.Sea.EnemyFleet.SeaSerpent),
        ("식인상어", Engine.Sea.EnemyFleet.ManEater), ("맨터", Engine.Sea.EnemyFleet.Manta),
    ];

    /// <summary>잠겼는지 — 비밀번호를 걸어 두면 그것을 모르는 사람은 불러와도 고치지 못한다.</summary>
    public static bool IsLocked(Entry entry) => entry.Lock.Length > 0;

    /// <summary>
    /// 비밀번호로 잠금 값을 짓는다 — <c>sha256:소금:해시</c>. 비밀번호는 적지 않는다.
    /// </summary>
    /// <remarks>
    /// 파일을 손으로 열어 이 칸을 지우면 풀린다 — 막는 것은 창에서 고치는 것뿐이다. 남의 것을 실수로 고쳐 같은 UUID 로
    /// 퍼뜨리는 일을 막으려는 것이지 암호가 아니다.
    /// </remarks>
    public static string LockOf(string password)
    {
        string salt = Convert.ToBase64String(RandomNumberGenerator.GetBytes(16));
        return $"sha256:{salt}:{Hash(salt, password)}";
    }

    /// <summary>그 비밀번호가 잠금과 맞는지. 잠기지 않았으면 늘 참이다.</summary>
    public static bool Unlocks(Entry entry, string password)
    {
        if (!IsLocked(entry)) return true;
        var parts = entry.Lock.Split(':');
        return parts.Length == 3 && parts[0] == "sha256" && parts[2] == Hash(parts[1], password);
    }

    private static string Hash(string salt, string password) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(salt + "\n" + password)));

    /// <summary>예제의 UUID — 목록 맨 위에 「(예제) 돌묵상어」로 서고, 적거나 내보낼 수 없다.</summary>
    public const string ExampleUuid = "example-basking-shark";

    /// <summary>둘째 예제 — 아테나의 시련으로 육상전을 치르는 파르테논 신전.</summary>
    public const string ParthenonUuid = "example-parthenon";

    /// <summary>
    /// 예제들과 그 그림 번호 · 원본 동영상 번호(없으면 -1). 목록 맨 위에 이 차례로 선다. 파르테논은 원본 동영상 2 번
    /// (<c>I02_0000</c>, <see cref="UI.Views.DiscoveryDialog.MovieOf"/>)을 튼다.
    /// </summary>
    public static IReadOnlyList<(Entry Entry, int Picture, int Movie)> Examples =>
        [(Example, ExamplePicture, -1), (ParthenonExample, -1, 2)];

    /// <summary>그 UUID 의 예제. 예제가 아니면 null.</summary>
    public static (Entry Entry, int Picture, int Movie)? ExampleOf(string uuid) =>
        Examples.FirstOrDefault(x => x.Entry.Uuid == uuid) is { Entry: not null } hit ? hit : null;

    /// <summary>
    /// 파르테논 신전 예제 — 원본 발견물 23 을 이 창의 칸 모양으로 옮긴 것. 힌트(그리스 이야기)를 받아 아테네의 아크로폴리스에
    /// 오르면 아테나가 시련을 걸어 육상전을 치르고, 지면 게임 오버다. 장면은 <c>발견이벤트.json</c> 파트 23 그대로다.
    /// </summary>
    /// <remarks>
    /// 원본은 도시 아테네(72)에 들어서는 발견이라 자리가 없다 — 예제는 아테네 곁 언덕 칸을 잡았다. 동영상은 원본 슬롯 2 다.
    /// 「힌트가 없으면 길이 막혀 있다」 갈래는 이 창에서 못 적어 뺐다(힌트가 있어야 열리는 것으로 갈음한다).
    /// </remarks>
    public static Entry ParthenonExample { get; } = new(
        ParthenonUuid, "파르테논 신전", Category: 1, Reward: 12000, OnLand: true, OpenAtStart: false,
        X1: 1409, Y1: 358, X2: 1412, Y2: 360,
        HintName: "파르테논 신전", HintGrade: 1, HintCategory: 1, HintFunds: 12000, HintDeadline: 3,
        HintText: "옛날 그리스 아테네의 아크로폴리스 언덕에 솟아 있는 파르테논신전은 수호신 아테나에게 바쳐진 것이다. 그 아름다움은 그리스건축의 대표격이다.",
        RumorCities: [], RumorText: "", Book: "그리스 이야기",
        Description: "아테네의 수호신 아테나를 기리는 신전. 당시의 그리스 최고 예술가를 총동원하여 기원전 432년에 완성. 장중한 도리아식 외견과 우아한 이오이아식 세부구조가 훌륭하게 조화를 이룬 그리스 건축의 최고 걸작이다.",
        Chance: 1,
        Lines:
        [
            new("movie"),
            new("found", null, "파르테논 신전을 발견했다！"),
            new("say", null, "〈여자의 목소리〉 누구세요？ 양해를 구하지 않고 신전에 들어온 자는？"),
            new("say", "감찰관", "아니， 우리는 수상한 자가···"),
            new("say", "부관", "저···그러는 당신은？"),
            new("say", null, "〈여자의 목소리〉 나는 올림포스 １２신 중의 하나인 아테나．"),
            new("say", null, "〈아테나〉 신전을 방문한 자는 시련을 받아야 합니다．"),
            new("say", null, "〈아테나〉 해내지 못했을 경우는 이곳을 나가지 못합니다．"),
            new("say", null, "＜아테나＞ 나는 싸움의 신． 당신의 지휘관으로서의 실력을 시험하겠습니다．"),
            new("land", null, "아테네의 장군 상대 100~139 아군 70 그림 3"),
            new("say", "부관", "우리가 이겼습니다！"),
            new("item", null, "4"),
            new("say", null, "이디스의 방패를 손에 넣었다！"),
            new("say", null, "〈아테나〉 오랜만에 이 시련을 극복한 자가 나타났군요． 그럼， 가십시오． 또 만날 날이 있겠지요．"),
            new("lose"),
            new("say", null, "〈아테나〉 유감입니다． 앞으로 당신도 병사의 한 사람으로서 여기를 방문하는 자들과 싸워야 됩니다 영원히···"),
            new("gameover"),
        ],
        Credit: "원본 게임 「대항해시대3」의 파르테논 신전(발견물 23)",
        Author: "대항해시대3 리메이크",
        License: "원본 게임 자료(예제용)");

    /// <summary>예제 그림 — 앱에 실린 돌묵상어 그림(발견물 그림 84번)이다.</summary>
    public const int ExamplePicture = 84;

    /// <summary>
    /// 예제 — 앱에 실린 돌묵상어(발견물 274)를 이 창의 칸 모양으로 옮긴 것. 빈 창에서 무엇을 적을지 보고 따라 하라고 둔다.
    /// </summary>
    /// <remarks>
    /// 값은 <c>발견물-고친것.json</c> · <c>힌트-고친것.json</c> · <c>소문-고친것.json</c> · <c>발견물설명.json</c> ·
    /// <c>발견이벤트.json</c>(파트 274) 그대로다. 장면의 효과음 두 줄(첨벙 · 발견)은 이 창에서 못 적어 뺐다.
    /// </remarks>
    public static Entry Example { get; } = new(
        ExampleUuid, "돌묵상어", Category: 6, Reward: 30000, OnLand: false, OpenAtStart: true,
        X1: 1168, Y1: 224, X2: 1194, Y2: 240,
        HintName: "햇볕을 쬐는 큰 물고기", HintGrade: 2, HintCategory: 5, HintFunds: 30000, HintDeadline: 4,
        HintText: "북쪽 바다에는 작은 배보다도 큰 물고기가 산다고 한다. 동굴 같은 입을 벌린 채 수면 가까이를 떠다니지만 사람을 해치지는 않는다. 볕이 좋은 날이면 등지느러미를 물 밖으로 내놓고 햇볕을 쬔다고 한다.",
        RumorCities: [43, 42, 40],
        RumorText: "스코틀랜드 서쪽 섬들 앞바다에 배만 한 물고기가 입을 벌린 채 떠다닌다더군. 볕 좋은 날이면 등지느러미가 물 위로 보인다고 하네.",
        Book: "박물지",
        Description: "북대서양을 비롯한 온대 바다에 사는 거대한 상어. 고래상어 다음으로 큰 물고기로 몸길이가 10미터를 넘기도 한다. 큰 입을 벌린 채 수면 가까이를 헤엄치며 작은 먹이를 걸러 먹는다. 성질이 온순해 사람을 해치지 않는다.",
        Chance: 3,
        Lines:
        [
            new("say", "감찰관", "사， 상어다！ 어서 배를 돌려라！"),
            new("say", "부관", "아니， 덤벼들지 않습니다． 입을 벌린 채 천천히 헤엄치고 있을 뿐입니다．"),
            new("say", null, "첨벙！"),
            new("picture"),
            new("say", "부관", "저 입을 보십시오！ 사람 하나쯤은 통째로 들어가겠습니다．"),
            new("say", "감찰관", "이빨이 보이지 않는군． 바닷물을 들이마시고 있는 것인가？"),
            new("say", "부관", "작은 먹이를 물과 함께 걸러 먹는 모양입니다． 온순한 상어로군요．"),
            new("found", null, "돌묵상어를 발견했다！"),
            new("say", "감찰관", "이렇게 큰 물고기가 햇볕을 쬐며 떠다니다니···． 세상은 넓구나．"),
        ],
        Credit: "글: 위키백과 「돌묵상어」 ko.wikipedia.org/wiki/돌묵상어 (CC BY-SA 4.0)\n그림: Greg Skomal / NOAA Fisheries Service, 위키미디어 공용, 퍼블릭 도메인",
        Author: "대항해시대3 리메이크",
        License: "CC BY-SA 4.0");

    /// <summary>이 PC 에서 받은 번호.</summary>
    public readonly record struct Numbers(int Id, int Serial, int Hint, int Picture, int Movie);

    /// <summary>읽어 둔 한 벌 — 알맹이와 받은 번호.</summary>
    public sealed record Loaded(Entry Entry, Numbers Numbers, string Folder)
    {
        /// <summary>그림 파일(320x240 png). 없으면 null.</summary>
        public string? PicturePath => File.Exists(Path.Combine(Folder, PictureFile)) ? Path.Combine(Folder, PictureFile) : null;

        /// <summary>동영상 파일(<c>movie.mp4</c> 따위, <see cref="MovieFiles.Extensions"/>). 없으면 null.</summary>
        public string? MoviePath => MovieIn(Folder);
    }

    /// <summary>그 폴더의 동영상 파일. 없으면 null.</summary>
    private static string? MovieIn(string folder) =>
        MovieFiles.Extensions.Select(ext => Path.Combine(folder, MovieStem + ext)).FirstOrDefault(File.Exists);

    private const string EntryFile = "discovery.json", PictureFile = "picture.png", MovieStem = "movie";

    /// <summary>모아 둔 자리.</summary>
    public static string Folder =>
        UserDataFolder.Combine("discoveries");

    private static readonly JsonSerializerOptions Pretty = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private static List<Loaded>? _all;
    private static Dictionary<int, Loaded>? _byId, _bySerial, _byHint, _byPicture, _byMovie;
    private static readonly Dictionary<int, byte[]> SceneCache = [];

    /// <summary>더하거나 고치거나 지웠을 때 — 표를 들고 있던 쪽이 다시 읽는다.</summary>
    public static event Action? Changed;

    /// <summary>모드 「추가 발견물」을 켰을 때만 낸다.</summary>
    private static bool On => Settings.GameSettings.ExtraDiscoveries;

    /// <summary>지어 둔 것 전부(모드와 상관없이).</summary>
    public static IReadOnlyList<Loaded> All
    {
        get
        {
            if (_all != null) return _all;
            var entries = new List<(Entry, string)>();
            try
            {
                if (Directory.Exists(Folder))
                    foreach (string dir in Directory.GetDirectories(Folder))
                    {
                        string file = Path.Combine(dir, EntryFile);
                        if (!File.Exists(file)) continue;
                        try
                        {
                            if (JsonSerializer.Deserialize<Entry>(File.ReadAllText(file)) is { Uuid.Length: > 0 } e) entries.Add((e, dir));
                        }
                        catch (JsonException) { }
                    }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }

            // 번호는 UUID 차례로 받는다 — 셈이 겹칠 때 누가 다음 칸으로 밀릴지가 PC 마다 같게.
            var taken = new[] { new HashSet<int>(), new HashSet<int>(), new HashSet<int>(), new HashSet<int>(), new HashSet<int>() };
            var loaded = new List<Loaded>();
            foreach (var (e, dir) in entries.OrderBy(x => x.Item1.Uuid, StringComparer.Ordinal))
            {
                int Take(int kind)
                {
                    int span = NumberEnd - NumberBase + 1;
                    int n = NumberBase + (int)(BitConverter.ToUInt32(SHA256.HashData(Encoding.UTF8.GetBytes($"{e.Uuid}:{kind}")), 0) % (uint)span);
                    while (!taken[kind].Add(n)) n = n >= NumberEnd ? NumberBase : n + 1;
                    return n;
                }
                loaded.Add(new Loaded(e, new Numbers(Take(0), Take(1), Take(2), Take(3), Take(4)), dir));
            }
            _all = loaded;
            _byId = loaded.ToDictionary(l => l.Numbers.Id);
            _bySerial = loaded.ToDictionary(l => l.Numbers.Serial);
            _byHint = loaded.ToDictionary(l => l.Numbers.Hint);
            _byPicture = loaded.ToDictionary(l => l.Numbers.Picture);
            _byMovie = loaded.ToDictionary(l => l.Numbers.Movie);
            return _all;
        }
    }

    private static void Forget()
    {
        _all = null;
        _byId = _bySerial = _byHint = _byPicture = _byMovie = null;
        lock (SceneCache) SceneCache.Clear();
        Changed?.Invoke();
    }

    private static Loaded? ById(int id) { _ = All; return On && _byId!.TryGetValue(id, out var l) ? l : null; }

    // ── 표에 얹는 것 ──────────────────────────────────────────────

    /// <summary>발견물 표 줄들(모드를 켰을 때).</summary>
    public static IEnumerable<DiscoveryTable.Record> Records => On ? All.Select(ToRecord) : [];

    /// <summary>그 번호의 발견물 줄. 없으면 null.</summary>
    public static DiscoveryTable.Record? Record(int id) => ById(id) is { } l ? ToRecord(l) : null;

    private static DiscoveryTable.Record ToRecord(Loaded l)
    {
        var e = l.Entry;
        return new DiscoveryTable.Record(l.Numbers.Id, e.Name, e.Category, l.Numbers.Serial, e.Reward, -1,
            Indirect: false, e.OpenAtStart, e.OnLand, Once: false,
            Math.Min(e.X1, e.X2), Math.Min(e.Y1, e.Y2), Math.Max(e.X1, e.X2), Math.Max(e.Y1, e.Y2),
            Picture: l.PicturePath != null ? l.Numbers.Picture : -1, Movie: l.MoviePath != null ? l.Numbers.Movie : -1,
            Erase: [65535, 65535, 65535, 65535], SoldIn: -1, Clip: -1);
    }

    /// <summary>힌트들(모드를 켰을 때).</summary>
    public static IEnumerable<HintTable.Hint> Hints => On ? All.Where(l => l.Entry.HasHint).Select(ToHint) : [];

    /// <summary>그 번호의 힌트. 없으면 null.</summary>
    public static HintTable.Hint? Hint(int id)
    {
        _ = All;
        return On && _byHint!.TryGetValue(id, out var l) && l.Entry.HasHint ? ToHint(l) : null;
    }

    private static HintTable.Hint ToHint(Loaded l)
    {
        var e = l.Entry;
        return new HintTable.Hint(l.Numbers.Hint, e.HintName, Math.Clamp(e.HintGrade, 1, 3), e.HintCategory,
                                  Math.Max(0, e.HintFunds), Math.Max(1, e.HintDeadline), l.Numbers.Serial, e.HintText);
    }

    /// <summary>술집 소문들(모드를 켰을 때). 소문 글이 빈 것은 안 낸다.</summary>
    public static IEnumerable<RumorTable.Rumor> Rumors =>
        On ? All.Where(l => l.Entry.HasHint && l.Entry.RumorText.Length > 0)
                .Select(l => RumorTable.UserRumor(l.Entry.Uuid, l.Numbers.Serial, Cities(l.Entry.RumorCities), l.Entry.RumorText)) : [];

    /// <summary>소문 도시 넷 칸 — 원본 줄처럼 남는 칸은 -1.</summary>
    private static int[] Cities(int[]? cities)
    {
        var four = new[] { -1, -1, -1, -1 };
        for (int i = 0; i < Math.Min(4, cities?.Length ?? 0); i++) four[i] = cities![i];
        return four;
    }

    /// <summary>그 책(제목)에 실을 힌트 번호들(모드를 켰을 때).</summary>
    public static IEnumerable<int> HintsInBook(string title) =>
        On ? All.Where(l => l.Entry.HasHint && l.Entry.Book.Length > 0 && l.Entry.Book == title).Select(l => l.Numbers.Hint) : [];

    /// <summary>그 번호의 설명 글. 없으면 null.</summary>
    public static string? Description(int id) => ById(id)?.Entry.Description;

    /// <summary>그 그림 번호의 그림 파일. 없으면 null.</summary>
    public static string? PicturePath(int picture)
    {
        _ = All;
        return On && _byPicture!.TryGetValue(picture, out var l) ? l.PicturePath : null;
    }

    /// <summary>그 동영상 번호의 동영상 파일. 없으면 null.</summary>
    public static string? MoviePath(int movie)
    {
        _ = All;
        return On && _byMovie!.TryGetValue(movie, out var l) ? l.MoviePath : null;
    }

    /// <summary>그 번호의 발견 대본 파트(구운 바이트). 없으면 null.</summary>
    public static byte[]? ScenePart(int id)
    {
        if (ById(id) is not { } l) return null;
        lock (SceneCache)
        {
            if (!SceneCache.TryGetValue(id, out var bytes))
                SceneCache[id] = bytes = Compile(l, out _) ?? [];
            return bytes.Length > 0 ? (byte[])bytes.Clone() : null;
        }
    }

    // ── 장면 ──────────────────────────────────────────────────

    /// <summary>
    /// 장면 줄을 발견 대본 파트로 굽는다 — 여우원숭이(275)와 같은 꼴이다: 확률 조건 덩이 하나, 몸통 덩이 하나.
    /// </summary>
    public static byte[]? Compile(Loaded l, out string error)
    {
        var e = l.Entry;
        JsonObject Say(string text, string? speaker)
        {
            var args = new JsonObject();
            if (!string.IsNullOrWhiteSpace(speaker)) args["Speaker"] = speaker.Trim();
            args["Text"] = text;
            return new JsonObject { ["OpCode"] = "00 0A", ["Call"] = "Say", ["Args"] = args };
        }
        JsonObject End() => new() { ["OpCode"] = "FF", ["Call"] = "End" };

        var body = new JsonArray();
        void Found()
        {
            body.Add(new JsonObject { ["OpCode"] = "0E 03", ["Call"] = "PlaySound", ["Args"] = new JsonObject { ["Id"] = 75 } });
            body.Add(Say($"{e.Name}{Josa(e.Name)} 발견했다！", null));
            body.Add(new JsonObject { ["OpCode"] = "01 0B", ["Call"] = "Discover", ["Args"] = new JsonObject { ["Discovery"] = l.Numbers.Id } });
        }
        void Emit(IEnumerable<SceneLine> lines)
        {
        foreach (var line in lines)
            switch (line.Kind)
            {
                case "movie" when l.MoviePath != null:
                    body.Add(new JsonObject { ["OpCode"] = "00 02", ["Call"] = "PlayVideo", ["Args"] = new JsonObject { ["Id"] = l.Numbers.Movie } });
                    break;
                case "picture" when l.PicturePath != null:
                    body.Add(new JsonObject { ["OpCode"] = "00 01", ["Call"] = "ShowDStill", ["Args"] = new JsonObject { ["Id"] = l.Numbers.Picture } });
                    break;
                case "found" when line.Text.Length == 0:
                    Found();
                    break;
                case "found":
                    body.Add(new JsonObject { ["OpCode"] = "0E 03", ["Call"] = "PlaySound", ["Args"] = new JsonObject { ["Id"] = 75 } });
                    body.Add(Say(line.Text, null));
                    body.Add(new JsonObject { ["OpCode"] = "01 0B", ["Call"] = "Discover", ["Args"] = new JsonObject { ["Discovery"] = l.Numbers.Id } });
                    break;
                case "say" when line.Text.Length > 0:
                    body.Add(Say(line.Text, line.Speaker));
                    break;
                case "item" when int.TryParse(line.Text.Trim(), out int item):
                    body.Add(new JsonObject { ["OpCode"] = "00 05", ["Call"] = "GiveItem", ["Args"] = new JsonObject { ["Item"] = item } });
                    break;
                case "gameover":
                    body.Add(new JsonObject { ["OpCode"] = "4A", ["Call"] = "GameOver" });
                    break;
            }
        }

        /// <summary>싸움 한 줄 — 해전이면 0D 0D 괴물, 육상전이면 그림 벌 · 병력을 맞춘 뒤 2F 0D 인물.</summary>
        void Fight(SceneLine line)
        {
            if (line.Kind == "battle")
            {
                int monster = Monsters.FirstOrDefault(m => m.Name == line.Text.Trim()) is { Name: not null } hit
                    ? hit.Person : Engine.Sea.EnemyFleet.Kraken;
                body.Add(new JsonObject { ["OpCode"] = "0D 0D", ["Call"] = "SeaBattle", ["Args"] = new JsonObject { ["Person"] = monster } });
                return;
            }
            var t = LandTerms(line.Text);
            if (t.DuelSet >= 0)
                body.Add(new JsonObject { ["OpCode"] = "26 0F", ["Call"] = "SetDuelSet", ["Args"] = new JsonObject { ["Set"] = t.DuelSet } });
            if (t.Ours >= 0)
                body.Add(new JsonObject
                {
                    ["OpCode"] = "26 1C", ["Call"] = "SetStat",
                    ["Args"] = new JsonObject { ["Stat"] = 2, ["Value"] = new JsonObject { ["Const"] = t.Ours } },
                });
            if (t.FoeFrom >= 0)
                body.Add(new JsonObject
                {
                    ["OpCode"] = "26 1C", ["Call"] = "SetStat",
                    ["Args"] = new JsonObject
                    {
                        ["Stat"] = 16,
                        ["Value"] = t.FoeTo > t.FoeFrom
                            ? new JsonObject { ["Random"] = new JsonObject { ["From"] = t.FoeFrom, ["Width"] = t.FoeTo - t.FoeFrom + 1 } }
                            : new JsonObject { ["Const"] = t.FoeFrom },
                    },
                });
            body.Add(new JsonObject { ["OpCode"] = "2F 0D", ["Call"] = "LandBattle", ["Args"] = new JsonObject { ["Person"] = Math.Max(0, t.Person) } });
        }

        var all = e.Lines ?? [];
        int battle = all.FindIndex(x => x.Kind is "battle" or "land");
        if (battle < 0)
        {
            Emit(all);
            // 「found」 줄이 없으면 끝에 붙인다 — 발견이 안 적히면 영영 안 찾은 것으로 남는다.
            if (!all.Any(x => x.Kind == "found")) Found();
            body.Add(new JsonObject { ["OpCode"] = "4C", ["Call"] = "EndDone" });
            body.Add(End());
        }
        else
        {
            // 원본 크라켄(파트 197)과 같은 꼴 — 싸움 뒤 지면(결과 거짓) 놓친 갈래로 뛰고, 이기면 발견하고 「성공」을 둔 뒤
            // 43 47 이 놓친 갈래를 건너뛴다. 놓치면 「실패」라 다음에 그 자리에 가면 또 덤빈다(놓친 갈래에 「게임 오버」를 두면 끝난다).
            int lose = all.FindIndex(battle + 1, x => x.Kind == "lose");
            var win = lose < 0 ? all.Skip(battle + 1).ToList() : all.Skip(battle + 1).Take(lose - battle - 1).ToList();
            var missed = lose < 0 ? [] : all.Skip(lose + 1).ToList();

            Emit(all.Take(battle));
            Fight(all[battle]);
            body.Add(new JsonObject { ["OpCode"] = "43 45", ["GotoIf"] = new JsonObject { ["Call"] = "ResultFalse" }, ["Target"] = "LOSE" });
            Emit(win);
            // 발견 줄이 싸움 앞에 있으면(원본 파르테논처럼 먼저 알리고 싸운다) 다시 붙이지 않는다.
            if (!all.Any(x => x.Kind == "found")) Found();
            body.Add(new JsonObject { ["OpCode"] = "4C", ["Call"] = "EndDone" });
            body.Add(new JsonObject { ["Label"] = "LOSE", ["OpCode"] = "43 47", ["GotoIf"] = new JsonObject { ["Call"] = "Result" }, ["Target"] = "END" });
            Emit(missed);
            body.Add(new JsonObject { ["OpCode"] = "4D", ["Call"] = "EndFailed" });
            body.Add(new JsonObject { ["Label"] = "END", ["OpCode"] = "FF", ["Call"] = "End" });
        }

        var cond = new JsonArray
        {
            new JsonObject
            {
                ["OpCode"] = "2E 1A", ["Call"] = "RandomChance",
                ["Args"] = new JsonObject { ["Denominator"] = Math.Max(1, e.Chance), ["Success"] = 1 },
            },
            End(),
        };
        var part = new JsonObject
        {
            ["Index"] = l.Numbers.Id,
            ["Step"] = l.Numbers.Id,
            ["Slots"] = new JsonArray { new JsonObject { ["Condition"] = 0, ["Body"] = 1 } },
            ["Chunks"] = new JsonArray { cond, body },
        };
        try
        {
            if (part.Deserialize<DisevBook.Entry>() is not { } entry) { error = "장면을 못 읽었습니다"; return null; }
            return DisevBook.Compile(entry, out error);
        }
        catch (JsonException ex) { error = ex.Message; return null; }
    }

    /// <summary>받침이 있으면 「을」, 없으면 「를」.</summary>
    private static string Josa(string name) =>
        name.Length > 0 && name[^1] is >= '가' and <= '힣' && (name[^1] - '가') % 28 != 0 ? "을" : "를";

    // ── 짓기 · 고치기 · 지우기 ─────────────────────────────────

    /// <summary>빈 발견물 하나 — 새 UUID 를 받는다.</summary>
    public static Entry Blank(string author = "") => new(
        Guid.NewGuid().ToString(), "새 발견물", Category: 6, Reward: 30000, OnLand: true, OpenAtStart: true,
        X1: -1, Y1: -1, X2: -1, Y2: -1,
        HintName: "새 힌트", HintGrade: 2, HintCategory: 5, HintFunds: 30000, HintDeadline: 4, HintText: "",
        RumorCities: [], RumorText: "", Book: "박물지", Description: "",
        Chance: 3,
        Lines:
        [
            new("say", "부관", "제독！ 저기를 보십시오．"),
            new("picture"),
            new("found"),
        ],
        Author: author);

    /// <summary>적는다 — 그림을 주면 그림도 갈아 넣는다(320x240 BGRA).</summary>
    /// <param name="movie">동영상 파일을 갈아 넣을 때 그 자리. 빈 글이면 동영상을 뺀다. null 이면 그대로 둔다.</param>
    public static string? Save(Entry entry, (uint[] Bgra, int Width, int Height)? picture = null, string? movie = null)
    {
        try
        {
            string dir = Path.Combine(Folder, entry.Uuid);
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, EntryFile), JsonSerializer.Serialize(entry with { Version = FormatVersion }, Pretty));
            if (picture is var (bgra, w, h)) SavePng(bgra, w, h, Path.Combine(dir, PictureFile));
            if (movie is { Length: > 0 })
            {
                string ext = Path.GetExtension(movie).ToLowerInvariant();
                if (!MovieFiles.Extensions.Contains(ext)) return $"{ext} 는 넣을 수 없는 동영상입니다 — {string.Join(" ", MovieFiles.Extensions)}";
                string target = Path.Combine(dir, MovieStem + ext);
                // 붙들어 둔 뒤 옛 것을 지운다 — 다른 확장자의 옛 동영상이 남지 않게.
                File.Copy(movie, target + ".part", overwrite: true);
                DeleteMovies(dir);
                File.Move(target + ".part", target);
            }
            else if (movie is { Length: 0 }) DeleteMovies(dir);
            Forget();
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return ex.Message; }
    }

    /// <summary>지운다.</summary>
    public static void Delete(string uuid)
    {
        try { Directory.Delete(Path.Combine(Folder, uuid), recursive: true); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        Forget();
    }

    private static void DeleteMovies(string dir)
    {
        foreach (string ext in MovieFiles.Extensions)
            if (File.Exists(Path.Combine(dir, MovieStem + ext))) File.Delete(Path.Combine(dir, MovieStem + ext));
    }

    private static void SavePng(uint[] bgra, int width, int height, string path)
    {
        var art = BitmapSource.Create(width, height, 96, 96, System.Windows.Media.PixelFormats.Bgra32, null, bgra, width * 4);
        var png = new PngBitmapEncoder();
        png.Frames.Add(BitmapFrame.Create(art));
        using var file = File.Create(path);
        png.Save(file);
    }

    // ── 내보내기 · 불러오기 ─────────────────────────────────────

    /// <summary>한 벌을 <c>.cdsdiscovery</c>(zip) 하나로 내보낸다. 잘 됐으면 null, 아니면 까닭.</summary>
    public static string? Export(string uuid, string path)
    {
        if (All.FirstOrDefault(l => l.Entry.Uuid == uuid) is not { } l) return "그 발견물이 없습니다";
        try
        {
            if (File.Exists(path)) File.Delete(path);
            using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
            zip.CreateEntryFromFile(Path.Combine(l.Folder, EntryFile), EntryFile);
            if (l.PicturePath is { } picture) zip.CreateEntryFromFile(picture, PictureFile);
            if (l.MoviePath is { } movie) zip.CreateEntryFromFile(movie, Path.GetFileName(movie), CompressionLevel.NoCompression);
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException) { return ex.Message; }
    }

    /// <summary>불러온다 — 같은 UUID 가 있으면 <paramref name="overwrite"/> 로 물어 덮는다. 불러온 것(못 했으면 null)과 알릴 말.</summary>
    public static (Entry? Entry, string Message) Import(string path, Func<Entry, Entry, bool> overwrite)
    {
        try
        {
            using var zip = ZipFile.OpenRead(path);
            if (zip.GetEntry(EntryFile) is not { } head) return (null, "발견물 파일이 아닙니다(discovery.json 이 없습니다).");
            Entry? entry;
            using (var stream = head.Open()) entry = JsonSerializer.Deserialize<Entry>(stream);
            if (entry is not { Uuid.Length: > 0 } || !Guid.TryParse(entry.Uuid, out _)) return (null, "discovery.json 을 읽지 못했습니다.");
            if (entry.Version > FormatVersion) return (null, "더 새 버전의 게임에서 만든 발견물입니다. 게임을 업데이트해 주세요.");

            if (All.FirstOrDefault(l => l.Entry.Uuid == entry.Uuid) is { } had && !overwrite(had.Entry, entry))
                return (null, "불러오지 않았습니다.");

            string dir = Path.Combine(Folder, entry.Uuid);
            Directory.CreateDirectory(dir);
            head.ExtractToFile(Path.Combine(dir, EntryFile), overwrite: true);
            string picture = Path.Combine(dir, PictureFile);
            if (zip.GetEntry(PictureFile) is { } art) art.ExtractToFile(picture, overwrite: true);
            else if (File.Exists(picture)) File.Delete(picture);
            DeleteMovies(dir);
            if (zip.Entries.FirstOrDefault(z => Path.GetFileNameWithoutExtension(z.Name) == MovieStem
                                               && MovieFiles.Extensions.Contains(Path.GetExtension(z.Name).ToLowerInvariant())) is { } clip)
                clip.ExtractToFile(Path.Combine(dir, MovieStem + Path.GetExtension(clip.Name).ToLowerInvariant()), overwrite: true);
            Forget();
            var numbers = All.First(l => l.Entry.Uuid == entry.Uuid).Numbers;
            return (entry, $"「{entry.Name}」 을 불러왔습니다 — 발견물 {numbers.Id}번.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException
                                      or JsonException or NotSupportedException)
        {
            return (null, $"불러오지 못했습니다 — {ex.Message}");
        }
    }
}
