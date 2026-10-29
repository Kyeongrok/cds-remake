using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Text.Json;
using CdsHelper.Game.Local.Settings;
using CdsHelper.Support.Local.Helpers;

namespace CdsHelper.Game.Local.Helpers;

/// <summary>
/// 게임 통계 — 무엇을 발견하고 누구를 고용하고 얼마나 싸우는지 세고, 주인공 이름 · 능력치를 받는 곳으로 보낸다.
/// </summary>
/// <remarks>
/// 받는 곳은 <c>tools/stats-worker</c>(Cloudflare Worker + D1)다. 모드 옵션은 켤 때 한 번 찍는다.
/// <code>
///   discovery     발견물 번호                  battle  sea:Won · sea:Escaped · land:win · land:lose …
///   hire          고용한 부하의 인물 번호        error   「오류 갈래@클래스.메서드」
///   dismiss       해고한 부하의 인물 번호
///   nav           네비게이션 — pick:quick · end:arrived …
///   event         바다 · 육지에서 하루를 넘길 때의 일 — sea:Storm · land:Beast …
/// </code>
/// 받는 쪽은 열쇠마다 수를 <b>더하기만</b> 한다. 차림표 줄(menu)과 항해(voyage — 「떠난 도시>닿은 도시」 횟수 · 날수 · 선회)는
/// 예전에 셌는데 이제 안 센다. 주인공(<see cref="Hero"/>)은 셈이 아니라 판마다 마지막 값 한 벌이다 — 바뀌었을 때만 덩이에 싣는다.
/// 셈은 메모리에 모았다가 <b>도시를 나설 때</b>(출항 · 성문)와 끌 때 한 덩이로 줄(파일)에 세워 뒤에서 보낸다
/// (나서지 않고 오래 있으면 <see cref="FlushEvery"/> 마다) —
/// 인터넷이 없으면 다음에 켤 때 다시 보낸다. <b>게임 exe 가 <see cref="Start"/> 를 불러야만</b> 세기 시작하고,
/// 모은다고 한 번 알린 뒤(<see cref="GameSettings.SendStats"/>)에만, 태그로 낸 릴리즈 판에서만 보낸다.
/// 누가 보냈는지는 설치마다 만든 무작위 번호뿐이다 — 세이브 · IP 는 안 적는다. 이름은 주인공 이름만 싣는다.
/// </remarks>
public static class PlayStats
{
    /// <summary>받는 곳의 주소를 적어 두는 파일 — 저장소 뿌리에 있고 exe 옆에 실린다.</summary>
    public const string UrlFile = "stats-url.txt";

    /// <summary>
    /// 도시를 나서지 않고 이만큼 지나면 그때까지 모은 셈을 줄에 세운다 — 바다나 도시에 오래 머물다 강제로 꺼져도
    /// 다 잃지 않게 둔 안전판이다. 예전에는 5분마다 보냈는데 덩이가 너무 잦아 받는 곳의 하루 한도가 찼다.
    /// </summary>
    private static readonly TimeSpan FlushEvery = TimeSpan.FromMinutes(30);

    /// <summary>
    /// 도시를 나설 때 보내는 덩이 사이의 가장 짧은 틈. 받는 곳(D1)은 덩이 안의 셈 하나마다 한 줄을 쓰므로, 덩이를 드물게 보내
    /// 같은 열쇠가 한 덩이에 모일수록 쓰기가 준다 — 10분일 때 하루 쓰기 한도의 4분의 3까지 찼다.
    /// </summary>
    private static readonly TimeSpan LeaveGap = TimeSpan.FromMinutes(30);

    /// <summary>열쇠·이름의 가장 긴 길이. 받는 쪽도 같은 데서 자른다.</summary>
    private const int MaxKey = 96;

    /// <summary>오래 못 보낸 것이 한없이 쌓이지 않게 새 것부터 이만큼만 보낸다.</summary>
    private const int MaxQueued = 50;

    private static readonly string Folder = UserDataFolder.Roaming;

    private static string InstallPath => Path.Combine(Folder, "install-id.txt");
    private static string QueueFolder => Path.Combine(Folder, "stats-queue");
    private static string HardcoreFolder => Path.Combine(Folder, "hardcore-queue");

    private static readonly object Gate = new();
    /// <summary>모은 셈.</summary>
    private static readonly Dictionary<(string Kind, string Key), (string Name, int Count)> Counts = [];

    /// <summary>
    /// 주인공 한 벌 — 판 표지 · 이름 · 나라 · 게임 날짜 · 보이는 능력치 여섯 · 기함 선체 이름 · 교역소 투자(합과 도시 이름마다).
    /// </summary>
    private sealed record HeroInfo(string Game, string Name, string Nation, string Date, int[] Abilities,
                                   string Flagship, long Invested, Dictionary<string, long> Investments)
    {
        /// <summary>날짜만 바뀐 것은 새로 안 보낸다 — 이름 · 나라 · 능력치 · 기함 · 투자가 그대로면 같은 것으로 친다.</summary>
        public bool SameAs(HeroInfo? other) =>
            other != null && other.Game == Game && other.Name == Name && other.Nation == Nation
            && other.Abilities.SequenceEqual(Abilities) && other.Flagship == Flagship && other.Invested == Invested;
    }

    /// <summary>투자한 도시를 이만큼만 싣는다(많이 넣은 차례). 받는 쪽도 같은 데서 자른다.</summary>
    private const int MaxInvestCities = 30;

    /// <summary>마지막으로 본 주인공과 마지막으로 줄에 세운 주인공.</summary>
    private static HeroInfo? _hero, _heroSent;

    private static string _url = "";
    private static string _version = "";
    private static bool _release, _modsSent;
    private static DateTime _flushedAt = DateTime.UtcNow;
    private static int _uploading;

    /// <summary>세기 시작했는가 — 게임 exe 만 켠다. 개발도구에서 돌린 게임은 안 센다.</summary>
    public static bool Started { get; private set; }

    /// <summary>이 판이 보낼 수 있는 판인가(회원의 답과 무관) — 받는 곳 주소가 있고 태그로 낸 릴리즈 판일 때.</summary>
    public static bool CanUploadBuild => Started && _url.Length > 0 && _release;

    private static bool CanUpload => CanUploadBuild && GameSettings.SendStats == true;

    /// <summary>
    /// 게임을 켤 때 한 번 — 받는 곳 주소와 판 번호를 읽고, 지난번에 못 보낸 것을 뒤에서 보낸다.
    /// </summary>
    /// <param name="app">게임 exe 의 어셈블리. 손으로 빌드한 판은 판 번호가 csproj 값(1.12.0) 그대로라 릴리즈로 안 친다.</param>
    public static void Start(Assembly app)
    {
        _url = ReadUrl();
        var version = app.GetName().Version;
        _version = version is null ? "" : ReleaseNotes.Trim(version).ToString();
        // 릴리즈 노트에 대목이 있는 판만 릴리즈다 — 릴리즈 빌드만 태그 번호로 판을 박는다(release.yml).
        _release = version is not null && ReleaseNotes.Since(ReleaseNotes.Read(), "", version).Length > 0;
        Started = true;
        UploadInBackground();
    }

    private static string ReadUrl()
    {
        try
        {
            string path = Path.Combine(AppContext.BaseDirectory, UrlFile);
            if (!File.Exists(path)) return "";
            string url = File.ReadLines(path).Select(l => l.Trim())
                             .FirstOrDefault(l => l.Length > 0 && !l.StartsWith('#')) ?? "";
            return url.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ? url.TrimEnd('/') : "";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return ""; }
    }

    /// <summary>발견물 하나를 새로 발견했다.</summary>
    public static void Found(int id, string? name) => Count("discovery", id.ToString(), name);

    /// <summary>술집에서 부하를 고용했다.</summary>
    public static void Hired(int person, string? name) => Count("hire", person.ToString(), name);

    /// <summary>
    /// 부하를 <b>내 손으로</b> 내보냈다 — 부하편성의 「해고」(모드)와 재계약을 물렸을 때다. 선금이 모자라 떠나거나
    /// 대가 바뀌어 · 판이 끝나 흩어지는 것은 안 센다.
    /// </summary>
    public static void Dismissed(int person, string? name) => Count("dismiss", person.ToString(), name);

    /// <summary>
    /// 지금 주인공을 적어 둔다 — 도시를 나설 때와 끌 때 부른다. 다음 덩이에 실리고, 받는 곳은 판(<paramref name="gameId"/>)마다
    /// 한 줄에 처음 값과 마지막 값을 남긴다.
    /// </summary>
    /// <param name="abilities">보이는 능력치 여섯(체력 · 지력 · 무력 · 매력 · 운 · 신앙심) — 작위 패시브는 안 얹은 값.</param>
    /// <param name="flagship">기함의 선체 이름(캐러벨 따위) — 회원이 지은 배 이름이 아니다. 배가 없으면 빈 글.</param>
    /// <param name="investments">교역소에 투자한 돈 — 도시 이름 → 누적 금액(모드 「작위」 · 「도시 투자 성장」).</param>
    public static void Hero(string gameId, string name, string nation, DateTime date, IReadOnlyList<int> abilities,
                            string flagship, IEnumerable<KeyValuePair<string, long>> investments)
    {
        if (!Started || string.IsNullOrWhiteSpace(name)) return;
        string who = name.Trim();
        if (who.Length > MaxKey) who = who[..MaxKey];
        var invested = investments.Where(p => p.Value > 0).GroupBy(p => p.Key)
            .Select(g => (City: g.Key, Amount: g.Sum(p => p.Value))).ToList();
        var hero = new HeroInfo(gameId, who, nation,
                                date.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture), [.. abilities],
                                flagship, invested.Sum(p => p.Amount),
                                invested.OrderByDescending(p => p.Amount).Take(MaxInvestCities).ToDictionary(p => p.City, p => p.Amount));
        lock (Gate) _hero = hero;
    }

    /// <summary>
    /// 네비게이션(자동항해)에서 무엇을 골랐고 어떻게 끝났는지 — <c>pick:quick</c> · <c>end:arrived</c> 따위.
    /// </summary>
    public static void Nav(string what) => Count("nav", what, null);

    /// <summary>
    /// 바다 · 육지에서 하루를 넘기며 난 일 — <c>sea:Storm</c> · <c>sea:Plague</c> · <c>land:Beast</c> 따위.
    /// </summary>
    /// <remarks>
    /// 밑수(<c>sea:day</c> · <c>sea:eligible</c> · <c>land:day</c> — 넘긴 날)는 v1.1.27 까지만 셌다. 수십만 날이 쌓여 드문 정도는
    /// 나왔고, 메시지마다 끼어 D1 쓰기 몫만 먹었다.
    /// </remarks>
    public static void Event(string what) => Count("event", what, null);

    /// <summary>해전 한 판이 끝났다.</summary>
    public static void SeaBattle(string outcome) => Count("battle", $"sea:{outcome}", null);

    /// <summary>육상전 한 판이 끝났다.</summary>
    public static void LandBattle(bool won) => Count("battle", won ? "land:win" : "land:lose", null);

    /// <summary>
    /// 잡지 못한 오류가 났다 — <b>오류 갈래와 난 자리(클래스.메서드)만</b> 적는다. 오류 글은 파일 경로 따위가
    /// 섞일 수 있어 안 싣는다. 곧 꺼질 수도 있으니 바로 줄에 세운다.
    /// </summary>
    public static void Error(Exception ex)
    {
        var at = ex.TargetSite;
        string key = $"{ex.GetType().Name}@{at?.DeclaringType?.Name}.{at?.Name}";
        // 난 자리가 남의 라이브러리(WPF · DirectX) 안이면 어디서 불렀는지 알 수 없다 — 우리 코드의 첫 자리와 오류 번호를 덧붙인다.
        if (at?.DeclaringType?.Namespace?.StartsWith("CdsHelper") != true)
        {
            var mine = new System.Diagnostics.StackTrace(ex).GetFrames()
                .Select(f => f.GetMethod())
                .FirstOrDefault(m => m?.DeclaringType?.Namespace?.StartsWith("CdsHelper") == true);
            if (mine != null) key += $"<{mine.DeclaringType?.Name}.{mine.Name}";
            key += $":{ex.HResult:X8}";
        }
        else
        {
            // 우리 코드 안에서 났어도 작은 속성(ShipyardMenu.get__player 따위)이면 어디서 불렀는지 몰라 못 고친다 —
            // 부른 자리(다른 메서드인 첫 우리 쪽 자리)를 덧붙인다.
            var caller = new System.Diagnostics.StackTrace(ex).GetFrames()
                .Select(f => f.GetMethod())
                .FirstOrDefault(m => m != null && m != at && m.DeclaringType?.Namespace?.StartsWith("CdsHelper") == true);
            if (caller != null) key += $"<{caller.DeclaringType?.Name}.{caller.Name}";
        }
        Count("error", key, null);
        Flush();
    }

    /// <summary>
    /// 하드코어 판이 게임 오버가 되었다 — 랭킹(<c>/v1/ranking</c>)에 올릴 한 줄을 줄에 세우고 뒤에서 보낸다.
    /// </summary>
    /// <remarks>
    /// 셈과 달리 <b>주인공 이름</b>을 싣는다 — 랭킹이 누구인지 보여야 한다. 하드코어는 고르는 것이라 통계 보내기 답
    /// (<see cref="GameSettings.SendStats"/>)과 상관없이 릴리즈 판이면 보낸다. 판 표지로 한 번만 받으므로 다시 보내도 안 겹친다.
    /// </remarks>
    /// <param name="cause">게임 오버 그림 번호(<c>0x0B</c> · <c>0x0C</c> · <c>0x0D</c>).</param>
    /// <param name="marks">한 번이라도 켠 · 줄인 모드와 쓴 개발 기능(<see cref="Engine.HardcoreRules"/>).</param>
    public static void HardcoreOver(string gameId, string name, string nation, int discoveries, DateTime died, int cause,
                                    IEnumerable<string> marks)
    {
        // 랭킹을 아직 안 낸다 — 회원에게 알리기 전에는 이름을 보내지 않는다.
        if (!RankingOpen) return;
        // 서명 열쇠가 없는 판(손으로 빌드한 것)은 받는 곳이 못 대 보므로 안 보낸다.
        if (!CanUploadBuild || Engine.HardcoreRules.Key.Length == 0 || string.IsNullOrWhiteSpace(name)) return;
        try
        {
            string who = name.Trim(), day = died.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
            var sorted = Engine.HardcoreRules.Sorted(marks);
            var body = new
            {
                v = 1,
                gameId,
                install = InstallId(),
                version = _version,
                name = who,
                nation,
                discoveries,
                died = day,
                cause,
                marks = sorted,
                sig = Engine.HardcoreRules.Sign(gameId, who, nation, discoveries, day, cause, sorted),
            };
            Directory.CreateDirectory(HardcoreFolder);
            File.WriteAllText(Path.Combine(HardcoreFolder, gameId + ".json"), JsonSerializer.Serialize(body));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return; }
        UploadInBackground();
    }

    /// <summary>
    /// 하드코어 랭킹을 연 판인지 — 거짓이면 게임 오버 기록을 안 보낸다. 열 때 참으로 바꾸고 햄버거에 「하드코어 랭킹」 줄을 넣는다.
    /// </summary>
    public const bool RankingOpen = false;

    /// <summary>하드코어 랭킹 페이지 주소. 받는 곳이 없으면 빈 글.</summary>
    public static string RankingUrl => _url.Length > 0 ? _url + "/ranking" : "";

    private static void Count(string kind, string key, string? name, int by = 1)
    {
        if (!Started) return;
        if (key.Length > MaxKey) key = key[..MaxKey];
        if (name is { Length: > MaxKey }) name = name[..MaxKey];

        bool due;
        lock (Gate)
        {
            Counts.TryGetValue((kind, key), out var was);
            Counts[(kind, key)] = (name ?? was.Name ?? "", was.Count + by);
            due = DateTime.UtcNow - _flushedAt >= FlushEvery;
        }
        if (due) Flush();
    }

    /// <summary>
    /// 도시를 나설 때 부른다 — 앞서 보낸 지 <see cref="LeaveGap"/> 이 지났을 때만 줄에 세운다.
    /// 항구를 잇달아 들르면 몇 분마다 한 덩이씩 나가, 하는 사람이 늘자 받는 곳의 하루 쓰기 한도가 찼다.
    /// </summary>
    public static void FlushOnLeave()
    {
        bool due;
        lock (Gate) due = DateTime.UtcNow - _flushedAt >= LeaveGap;
        if (due) Flush();
    }

    /// <summary>
    /// 모은 셈을 줄에 세우고 뒤에서 보낸다. 끌 때와 오류가 났을 때, 그리고 때가 찼을 때 부른다 — 보내지 못하고 꺼져도 줄에 남아 다음에 간다.
    /// </summary>
    public static void Flush()
    {
        if (!Started) return;

        List<object> rows;
        HeroInfo? hero;
        lock (Gate)
        {
            _flushedAt = DateTime.UtcNow;
            // 주인공은 이름 · 나라 · 능력치가 바뀌었을 때만 싣는다 — 날짜는 실을 때 따라간다.
            hero = _hero != null && !_hero.SameAs(_heroSent) ? _hero : null;
            if (Counts.Count == 0 && hero == null) return;
            rows = [.. Counts.Select(p => new { kind = p.Key.Kind, key = p.Key.Key, name = p.Value.Name, n = p.Value.Count })];
            Counts.Clear();
            if (hero != null) _heroSent = hero;
        }
        if (!CanUpload) return;   // 보내지 않을 판 · 답은 셈을 버린다 — 쌓아 두지 않는다

        try
        {
            string batch = Guid.NewGuid().ToString();
            var body = new
            {
                v = 1,
                batch,
                install = InstallId(),
                version = _version,
                counts = rows,
                hero = hero is null ? null : new
                {
                    game = hero.Game, name = hero.Name, nation = hero.Nation, date = hero.Date, abilities = hero.Abilities,
                    flagship = hero.Flagship, invested = hero.Invested, investments = hero.Investments,
                },
                // 모드 옵션은 켠 뒤 첫 덩이에만 싣는다 — 받는 쪽이 설치마다 마지막 값으로 덮는다.
                mods = _modsSent ? null : Mods(),
            };
            Directory.CreateDirectory(QueueFolder);
            File.WriteAllText(Path.Combine(QueueFolder, batch + ".json"), JsonSerializer.Serialize(body));
            _modsSent = true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return; }

        UploadInBackground();
    }

    /// <summary>
    /// 지금 켜 둔 옵션들 — 설정의 참·거짓 값 전부와 단계가 있는 몇 가지다. 글로 된 값(글쇠 이름 따위)은 안 싣는다.
    /// </summary>
    private static Dictionary<string, int> Mods()
    {
        var mods = new Dictionary<string, int>();
        foreach (var p in typeof(GameSettings).GetProperties(BindingFlags.Public | BindingFlags.Static))
        {
            if (!p.CanRead || p.GetIndexParameters().Length > 0 || p.Name == nameof(GameSettings.SendStats)) continue;
            try
            {
                if (p.PropertyType == typeof(bool)) mods[p.Name] = (bool)p.GetValue(null)! ? 1 : 0;
                else if (p.PropertyType == typeof(int) && LevelMods.Contains(p.Name)) mods[p.Name] = (int)p.GetValue(null)!;
            }
            catch (TargetInvocationException) { /* 못 읽는 값은 건너뛴다 */ }
        }

        // 화면 해상도(주 모니터의 화면 점)와 배율(%) — 창이 화면 밖으로 삐져나오는 자리를 가늠하려고 싣는다.
        var (width, height, scale) = Screen();
        if (width > 0)
        {
            mods["ScreenWidth"] = width;
            mods["ScreenHeight"] = height;
            mods["ScreenScale"] = scale;
        }
        return mods;
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern uint GetDpiForSystem();

    /// <summary>주 모니터의 폭 · 높이(화면 점)와 배율(100 이 100%). 못 읽으면 0 이다.</summary>
    private static (int Width, int Height, int Scale) Screen()
    {
        try
        {
            double dpi = GetDpiForSystem();
            if (dpi <= 0) dpi = 96;
            // WPF 는 화면 크기를 DIP 로 내므로 배율을 곱해 화면 점으로 되돌린다.
            return ((int)Math.Round(System.Windows.SystemParameters.PrimaryScreenWidth * dpi / 96),
                    (int)Math.Round(System.Windows.SystemParameters.PrimaryScreenHeight * dpi / 96),
                    (int)Math.Round(dpi * 100 / 96));
        }
        catch (Exception ex) when (ex is EntryPointNotFoundException or DllNotFoundException or InvalidOperationException)
        {
            return (0, 0, 0);
        }
    }

    /// <summary>참·거짓이 아니라 단계로 고르는 옵션들.</summary>
    private static readonly HashSet<string> LevelMods =
    [
        nameof(GameSettings.InfoLevel), nameof(GameSettings.SeaRaidScale), nameof(GameSettings.MutinyRate),
        nameof(GameSettings.Resolution), nameof(GameSettings.PortDays),
        nameof(GameSettings.PersonRollDays), nameof(GameSettings.PersonMoveOdds), nameof(GameSettings.PersonRestDays),
        nameof(GameSettings.ModPreset),      // 권장 옵션 — 0 오리지널 · 1 초보 · 2 중수 · 3 고수 · 4 파워업키트
    ];

    /// <summary>설치마다 하나인 무작위 번호 — 누구인지는 모르고 같은 설치인지만 안다.</summary>
    private static string InstallId()
    {
        if (File.Exists(InstallPath) && Guid.TryParse(File.ReadAllText(InstallPath).Trim(), out var known))
            return known.ToString();
        string id = Guid.NewGuid().ToString();
        Directory.CreateDirectory(Folder);
        File.WriteAllText(InstallPath, id);
        return id;
    }

    /// <summary>줄에 선 덩이를 뒤에서 보낸다 — 받은 것은 지우고, 안 되면 조용히 남겨 둔다.</summary>
    private static void UploadInBackground()
    {
        // 하드코어 기록은 통계 보내기 답과 상관없이 간다(HardcoreOver). 셈 줄은 답이 「보낸다」일 때만이다.
        bool stats = CanUpload && Directory.Exists(QueueFolder);
        bool hardcore = CanUploadBuild && Directory.Exists(HardcoreFolder);
        if ((!stats && !hardcore) || Interlocked.Exchange(ref _uploading, 1) == 1) return;
        _ = Task.Run(async () =>
        {
            try
            {
                using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
                if (hardcore) await SendQueued(http, HardcoreFolder, _url + "/v1/hardcore");
                if (stats) await SendQueued(http, QueueFolder, _url + "/v1/play");
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException
                                             or IOException or UnauthorizedAccessException)
            {
                System.Diagnostics.Debug.WriteLine($"[PlayStats] {ex.GetType().Name}: {ex.Message}");
            }
            finally { Interlocked.Exchange(ref _uploading, 0); }
        });
    }

    /// <summary>한 줄(폴더)에 선 것을 새 것부터 보낸다 — 받은 것은 지우고, 안 되면 남겨 두고 멈춘다.</summary>
    private static async Task SendQueued(HttpClient http, string folder, string url)
    {
        var files = new DirectoryInfo(folder).GetFiles("*.json")
                        .OrderByDescending(f => f.LastWriteTimeUtc).ToList();
        foreach (var old in files.Skip(MaxQueued)) old.Delete();
        foreach (var file in files.Take(MaxQueued))
        {
            using var content = new StringContent(await File.ReadAllTextAsync(file.FullName),
                                                  Encoding.UTF8, "application/json");
            using var reply = await http.PostAsync(url, content);
            // 받는 쪽이 「못 쓸 글」이라 하면(4xx) 다시 보내도 같다 — 지운다. 그 밖의 실패는 다음에 다시.
            if (reply.IsSuccessStatusCode || (int)reply.StatusCode is >= 400 and < 500) file.Delete();
            else break;
        }
    }
}
