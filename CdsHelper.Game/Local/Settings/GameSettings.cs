using System.IO;
using System.Text.Json;

namespace CdsHelper.Game.Local.Settings;

/// <summary>이대로 <c>game-settings.json</c> 이 된다.</summary>
public sealed class GameSettingsData
{
    /// <summary>앱을 켤 때 함대 보기(Direct3D) 창을 바로 띄울지. 기본은 켬.</summary>
    public bool AutoOpenShipMap { get; set; } = true;

    /// <summary>함대 창에서 배경음악을 틀지. 기본은 켬.</summary>
    public bool BgmEnabled { get; set; } = true;

    /// <summary>효과음(닻·거절 따위)을 낼지. 기본은 켬.</summary>
    public bool SfxEnabled { get; set; } = true;

    /// <summary>배경음악·효과음의 크기(0~100). 기본은 다 크게.</summary>
    public int BgmVolume { get; set; } = GameSettings.MaxVolume;

    /// <summary>해상 지도 배율(0.5~1.5, 0.25 칸). 기본 0.75 — 원본 크기에 맞춘 값이다.</summary>
    public double MapScale { get; set; } = GameSettings.DefaultMapScale;
    public int SfxVolume { get; set; } = GameSettings.MaxVolume;

    /// <summary>게임 창 단추의 좌우 여백(점).</summary>
    public int BandPad { get; set; } = GameSettings.DefaultBandPad;

    /// <summary>마을·항구에 들고 날 때 보내는 날수(1~10). 기본 10 — 원본 값이다.</summary>
    public int PortDays { get; set; } = GameSettings.DefaultPortDays;

    /// <summary>인물이 떠날지 굴리는 간격 — 0 이면 매월 1일(원본), 1~30 이면 그 날수마다.</summary>
    public int PersonRollDays { get; set; }

    /// <summary>인물이 떠날 확률의 분모(1~5) — N분의 1. 기본 5(원본).</summary>
    public int PersonMoveOdds { get; set; } = GameSettings.DefaultPersonMoveOdds;

    /// <summary>일기토에서 최근에 싸운 상대 이름 — 앞이 가장 최근이다.</summary>
    public List<string> RecentDuelFoes { get; set; } = [];

    /// <summary>도시 창이 열릴 때 줄 효과. <see cref="Settings.CityOpenEffect"/> 의 이름이다.</summary>
    public string CityOpenEffect { get; set; } = "Expand";

    /// <summary>게임 창 크기 — <see cref="GameSettings.Resolutions"/> 의 몇째인지.</summary>
    public int Resolution { get; set; } = GameSettings.DefaultResolution;

    /// <summary>지도 위에 좌표 상자를 겹쳐 보일지.</summary>
    public bool ShowCoordOverlay { get; set; } = true;

    /// <summary>지도 위의 까만 조작 줄을 보일지.</summary>
    public bool ShowToolBar { get; set; } = true;

    /// <summary>저장·발견물 지도 단축키(글쇠 이름). 비면 기본값을 쓴다.</summary>
    public string SaveKey { get; set; } = "V";
    public string MapKey { get; set; } = "D";

    /// <summary>지도 위에 만난 사람 상자를 겹쳐 보일지. 놀이에는 없는 것이라 꺼 두고 시작한다.</summary>
    public bool ShowPeopleOverlay { get; set; }

    /// <summary>지도 위에 제독 컨디션(HP) 상자를 겹쳐 보일지. 놀이에는 없는 것이라 꺼 두고 시작한다.</summary>
    public bool ShowConditionOverlay { get; set; }

    /// <summary>항해·뭍 이동 중에 지도 오른쪽 아래에 미니맵을 띄울지. 놀이에는 없는 것이라 꺼 두고 시작한다.</summary>
    public bool ShowMiniMap { get; set; }

    /// <summary>
    /// 풀린 미니게임 번호(0~6). 원본은 레지스트리 <c>MG00</c>~<c>MG06</c> 이라 세이브가 아니라 설치에 딸린다.
    /// </summary>
    public List<int> UnlockedMinigames { get; set; } = [];

    /// <summary>도시에 들어가면 도시 그림 왼쪽에 기능·언어 쪽지를 띄울지. 놀이에는 없는 것이라 꺼 두고 시작한다.</summary>
    public bool ShowSkillOverlay { get; set; }

    /// <summary>햄버거에 「발견물 지도」 줄을 낼지. 놀이에는 없는 것이라 꺼 두고 시작한다.</summary>
    public bool ShowDiscoveryMapMenu { get; set; }

    /// <summary>
    /// 계약을 맺을 때 배가 있으면 후원자가 「배를 빌리겠습니까?」를 묻는다 — 끄면 안 묻고
    /// 안 빌린다. 원본에는 늘 묻는 자리라 켜 두고 시작한다.
    /// </summary>
    public bool AskLendShips { get; set; } = true;

    /// <summary>
    /// <b>도시에 들어설 때마다</b> 자동저장 파일에 적을지. 놀이에는 없는 것이라 꺼 두고 시작한다.
    /// </summary>
    public bool AutoSaveOnPort { get; set; }

    /// <summary>햄버거에 「여급 수첩」 줄을 낼지. 놀이에는 없는 것이라 꺼 두고 시작한다.</summary>
    public bool ShowBarmaidBookMenu { get; set; }

    /// <summary>지도를 Ctrl+클릭해 배를 그 자리에 놓을지. 켠 채로 시작한다.</summary>
    public bool PlaceShipByCtrlClick { get; set; } = true;

    /// <summary>햄버거에 「인물 이동」 줄을 낼지. 놀이에는 없는 것이라 꺼 두고 시작한다.</summary>
    public bool ShowPersonMoveMenu { get; set; }

    /// <summary>
    /// 게임 상단 띠에 켜 둔 칸 이름들("날짜"·"소지금" …). 한 번도 안 건드렸으면 null 이라
    /// 부르는 쪽 기본값이 선다.
    /// </summary>
    public List<string>? BarCells { get; set; }

    /// <summary>지도 위에 바람·해류 화살표를 얹을지.</summary>
    public bool ShowFlowArrows { get; set; }

    /// <summary>발견물 지도에 풍향 화살표를 얹을지.</summary>
    public bool DiscoveryMapWind { get; set; }

    /// <summary>발견물 지도에 해류 화살표를 얹을지.</summary>
    public bool DiscoveryMapCurrent { get; set; }

    /// <summary>육상전 모의전 창이 지난번에 차렸던 짜임. 한 번도 안 차렸으면 null.</summary>
    public LandSparData? LandSpar { get; set; }
}

/// <summary>
/// 육상전 모의전 창(<c>LandSparDialog</c>)이 지난번에 차렸던 짜임.
/// </summary>
/// <remarks>
/// 같은 짜임으로 여러 판을 굴려 보는 자리라 <b>앱을 껐다 켜도</b> 남긴다. 손으로 고친
/// 파일이 들어와도 놀이는 굴러가야 하므로, 읽는 쪽이 칸 수와 범위를 다시 본다.
/// </remarks>
public sealed class LandSparData
{
    /// <summary>아군 여섯 자리의 병종. −1 이면 빈 자리다.</summary>
    public int[]? Mine { get; set; }

    /// <summary>적 여섯 자리.</summary>
    public int[]? Theirs { get; set; }

    public int MyMen { get; set; }
    public int FoeMen { get; set; }
    public int Culture { get; set; }
    public int Terrain { get; set; }
}

/// <summary>
/// 이 앱이 품고 있는 놀이에만 쓰는 설정. <c>%APPDATA%\CdsHelper\game-settings.json</c> 에 적는다.
/// </summary>
/// <remarks>
/// 앱 설정(<c>CdsHelper.Support</c> 의 <c>AppSettings</c>)과 갈라 두었다. 그쪽은 지도·발견물처럼
/// 이 앱이 도구로서 하는 일이고, 여기는 놀이 쪽이라 섞일 까닭이 없다. 갈라 두면 놀이를 통째로
/// 들어내도 앱 설정은 그대로다.
///
/// <b>실제 CDS_95 를 자동으로 조작하는 값(<c>AutoConfirmDialog</c> 따위)은 여기 없다</b> —
/// 그건 놀이가 아니라 도구 쪽 일이라 <c>AppSettings</c> 에 남아 있다.
///
/// 예전에는 이 값들이 <c>settings.json</c> 에 같이 들어 있었다. 새 파일이 없으면 그 파일에서
/// 한 번 옮겨 온다(<see cref="MigrateFromLegacy"/>) — 쓰던 사람이 맞춰 둔 값을 잃지 않는다.
/// </remarks>
public static class GameSettings
{
    /// <summary>
    /// 게임 창 단추의 좌우 여백 기본값(점).
    /// </summary>
    /// <remarks>
    /// 띠 마구리는 실제로 16점이라, 16 이면 마구리가 통째로 글자 밖에 선다. 그만큼 다 비우면
    /// 조금 헐거워 보여 눈으로 맞춰 12 로 잡았다 — 마구리 무늬가 글자에 살짝 걸치는 자리다.
    ///
    /// 이 값은 <b>적어 둔 것이 없을 때만</b> 선다. 한 번이라도 개발 창에서 만졌으면
    /// <c>game-settings.json</c> 에 적힌 값이 이긴다.
    /// </remarks>
    public const int DefaultBandPad = 12;

    /// <summary>단추 여백을 이 사이로만 잡는다.</summary>
    public const int MinBandPad = 0, MaxBandPad = 32;

    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "CdsHelper", "game-settings.json");

    /// <summary>옛 자리 — 갈라 놓기 전에는 여기 같이 들어 있었다.</summary>
    private static readonly string LegacyPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "CdsHelper", "settings.json");

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    private static GameSettingsData _data = new();
    private static bool _loaded;

    static GameSettings() => Load();

    /// <summary>
    /// 적어 둔 것을 읽는다. 두 번 불러도 한 번만 읽는다.
    /// </summary>
    /// <remarks>
    /// 앱을 켤 때 한 번 불러 둔다(<c>App.OnStartup</c>). 옛 <c>settings.json</c> 에서 옮겨 오는
    /// 일이 여기서 벌어지는데, 그 전에 앱 설정이 먼저 저장되면 옛 값이 지워진 뒤라 놓치게 된다.
    /// </remarks>
    public static void Load()
    {
        if (_loaded) return;
        _loaded = true;

        try
        {
            if (File.Exists(FilePath))
            {
                _data = JsonSerializer.Deserialize<GameSettingsData>(File.ReadAllText(FilePath)) ?? new();
                return;
            }

            if (MigrateFromLegacy()) Save();
        }
        catch
        {
            // 읽다 넘어져도 놀이는 기본값으로 굴러가야 한다.
            _data = new GameSettingsData();
        }
    }

    /// <summary>옛 <c>settings.json</c> 에 있던 값을 한 번 옮겨 온다. 옮길 게 있었으면 참.</summary>
    private static bool MigrateFromLegacy()
    {
        if (!File.Exists(LegacyPath)) return false;

        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(LegacyPath));
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return false;

            var moved = new GameSettingsData();
            bool any = false;

            any |= Bool("AutoOpenShipMap", v => moved.AutoOpenShipMap = v);
            any |= Bool("BgmEnabled", v => moved.BgmEnabled = v);
            any |= Bool("SfxEnabled", v => moved.SfxEnabled = v);
            any |= Bool("ShowCoordOverlay", v => moved.ShowCoordOverlay = v);
            any |= Bool("ShowPeopleOverlay", v => moved.ShowPeopleOverlay = v);
            any |= Bool("ShowToolBar", v => moved.ShowToolBar = v);
            any |= Bool("ShowFlowArrows", v => moved.ShowFlowArrows = v);

            if (root.TryGetProperty("BandPad", out var pad) && pad.TryGetInt32(out int padValue))
            {
                moved.BandPad = Math.Clamp(padValue, MinBandPad, MaxBandPad);
                any = true;
            }

            if (root.TryGetProperty("CityOpenEffect", out var effect) && effect.ValueKind == JsonValueKind.String)
            {
                moved.CityOpenEffect = effect.GetString() ?? "Expand";
                any = true;
            }

            if (root.TryGetProperty("BarCells", out var cells) && cells.ValueKind == JsonValueKind.Array)
            {
                moved.BarCells = [.. cells.EnumerateArray()
                    .Where(c => c.ValueKind == JsonValueKind.String)
                    .Select(c => c.GetString()!)];
                any = true;
            }

            if (any) _data = moved;
            return any;

            bool Bool(string name, Action<bool> set)
            {
                if (!root.TryGetProperty(name, out var value)) return false;
                if (value.ValueKind is not (JsonValueKind.True or JsonValueKind.False)) return false;

                set(value.GetBoolean());
                return true;
            }
        }
        catch
        {
            return false;
        }
    }

    private static void Save()
    {
        try
        {
            string? dir = Path.GetDirectoryName(FilePath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

            File.WriteAllText(FilePath, JsonSerializer.Serialize(_data, Json));
        }
        catch
        {
            // 못 적어도 이번 판은 굴러간다.
        }
    }

    private static T Get<T>(Func<GameSettingsData, T> read)
    {
        Load();
        return read(_data);
    }

    private static void Set(Action<GameSettingsData> write)
    {
        Load();
        write(_data);
        Save();
    }

    // ── 값들 ────────────────────────────────────────────────────────────────

    /// <summary>
    /// 그 미니게임이 MINI GAME 차림표에 풀렸는지 — 발견 이벤트에서 그 놀이를 이기면 풀린다
    /// (<c>0x00406B60</c> 이 레지스트리 <c>MG%02d</c> 를 켜고 <c>0x0045FA54</c> 벌이 읽는다).
    /// </summary>
    public static bool IsMinigameUnlocked(int game) => Get(d => d.UnlockedMinigames.Contains(game));

    /// <summary>미니게임 하나를 푼다.</summary>
    public static void UnlockMinigame(int game)
    {
        if (IsMinigameUnlocked(game)) return;
        Set(d => d.UnlockedMinigames.Add(game));
    }

    /// <summary>
    /// 앱을 켤 때 함대 보기(Direct3D) 창을 바로 띄울지. 기본은 <b>켬</b>이다 — 이 앱이
    /// 하는 일이 곧 그 창이라, 켤 때마다 메뉴에서 한 번 더 누르게 할 까닭이 없다.
    /// </summary>
    public static bool AutoOpenShipMap
    {
        get => Get(d => d.AutoOpenShipMap);
        set => Set(d => d.AutoOpenShipMap = value);
    }

    /// <summary>함대 창의 배경음악을 틀지. 설정 창에서 켜고 끈다.</summary>
    /// <summary>소리 크기의 위와 한 번에 오르내리는 폭.</summary>
    public const int MaxVolume = 100, VolumeStep = 10;

    /// <summary>배경음악 크기(0~100).</summary>
    public static int BgmVolume
    {
        get => Math.Clamp(Get(d => d.BgmVolume), 0, MaxVolume);
        set => Set(d => d.BgmVolume = Math.Clamp(value, 0, MaxVolume));
    }

    /// <summary>효과음 크기(0~100).</summary>
    public static int SfxVolume
    {
        get => Math.Clamp(Get(d => d.SfxVolume), 0, MaxVolume);
        set => Set(d => d.SfxVolume = Math.Clamp(value, 0, MaxVolume));
    }

    public static bool BgmEnabled
    {
        get => Get(d => d.BgmEnabled);
        set => Set(d => d.BgmEnabled = value);
    }

    /// <summary>효과음을 낼지. 배경음악과 따로 켜고 끈다.</summary>
    public static bool SfxEnabled
    {
        get => Get(d => d.SfxEnabled);
        set => Set(d => d.SfxEnabled = value);
    }

    /// <summary>해상 지도 배율의 아래·위·기본과 한 칸.</summary>
    public const double MinMapScale = 0.5, MaxMapScale = 1.5, DefaultMapScale = 0.75, MapScaleStep = 0.25;

    /// <summary>
    /// 해상 지도를 얼마나 크게 그릴지(0.5~1.5, 0.25 칸). 1 이 예전 크기(한 점에 1/32 칸)이고,
    /// 원본 화면에 맞춘 기본은 0.75(1/24 칸)다.
    /// </summary>
    public static double MapScale
    {
        get => Snap(Get(d => d.MapScale));
        set => Set(d => d.MapScale = Snap(value));
    }

    /// <summary>배율을 0.25 칸에 맞추고 범위 안으로 자른다.</summary>
    private static double Snap(double scale) =>
        Math.Clamp(Math.Round(scale / MapScaleStep) * MapScaleStep, MinMapScale, MaxMapScale);

    /// <summary>인물 떠남 확률 분모의 아래·위·기본.</summary>
    public const int MinPersonMoveOdds = 1, MaxPersonMoveOdds = 5, DefaultPersonMoveOdds = 5;

    /// <summary>인물 굴림 간격의 위 끝. 0 은 「매월 1일」(원본)이다.</summary>
    public const int MaxPersonRollDays = 30;

    /// <summary>
    /// 인물이 떠날지 굴리는 간격 — 0 이면 매월 1일(원본), 1~30 이면 1480년 1월 1일부터 그 날수마다.
    /// 역사 항해자의 대본은 이것과 상관없이 매월 1일에만 든다.
    /// </summary>
    public static int PersonRollDays
    {
        get => Math.Clamp(Get(d => d.PersonRollDays), 0, MaxPersonRollDays);
        set => Set(d => d.PersonRollDays = Math.Clamp(value, 0, MaxPersonRollDays));
    }

    /// <summary>인물이 떠날 확률의 분모(1~5) — 굴릴 때마다 N분의 1. 원본은 5다.</summary>
    public static int PersonMoveOdds
    {
        get => Math.Clamp(Get(d => d.PersonMoveOdds), MinPersonMoveOdds, MaxPersonMoveOdds);
        set => Set(d => d.PersonMoveOdds = Math.Clamp(value, MinPersonMoveOdds, MaxPersonMoveOdds));
    }

    /// <summary>들고 나는 날수의 아래·위·기본.</summary>
    public const int MinPortDays = 1, MaxPortDays = 10, DefaultPortDays = 10;

    /// <summary>
    /// 마을·항구에 들고 날 때 보내는 날수(1~10). 원본은 열흘이고, 개발 창에서 줄여 시험할 수 있다.
    /// </summary>
    public static int PortDays
    {
        get => Math.Clamp(Get(d => d.PortDays), MinPortDays, MaxPortDays);
        set => Set(d => d.PortDays = Math.Clamp(value, MinPortDays, MaxPortDays));
    }

    /// <summary>
    /// 게임 창 단추의 좌우 여백(점).
    /// </summary>
    /// <remarks>
    /// 띠는 왼끝·가운데·오른끝 셋으로 짓고 양 끝(마구리)이 16점씩이다. 이 값만큼을 글자
    /// 바깥에 비워 두므로, 16 이면 마구리가 통째로 글자 밖에 서고 그보다 작으면 글자가
    /// 마구리 위로 조금씩 올라앉는다. 바꾼 값은 <b>다음에 여는 창</b>부터 든다.
    /// </remarks>
    /// <summary>
    /// <b>저장</b> 단축키. 글쇠 이름(<see cref="System.Windows.Input.Key"/>)이고 기본은 <c>V</c> 다.
    /// </summary>
    public static string SaveKey
    {
        get => Get(d => d.SaveKey);
        set => Set(d => d.SaveKey = value);
    }

    /// <summary><b>발견물 지도</b> 단축키. 기본은 <c>D</c> 다.</summary>
    public static string MapKey
    {
        get => Get(d => d.MapKey);
        set => Set(d => d.MapKey = value);
    }

    /// <summary>몇 사람까지 적어 둘지.</summary>
    public const int MaxRecentFoes = 5;

    /// <summary>
    /// 일기토에서 최근에 싸운 상대 — 앞이 가장 최근이다.
    /// </summary>
    public static IReadOnlyList<string> RecentDuelFoes => Get(d => (IReadOnlyList<string>)[.. d.RecentDuelFoes]);

    /// <summary>
    /// 그 사람을 맨 앞에 적어 둔다. 이미 있으면 앞으로 끌어 올린다.
    /// </summary>
    public static void RememberDuelFoe(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return;

        Set(d =>
        {
            d.RecentDuelFoes.RemoveAll(one => one == name);
            d.RecentDuelFoes.Insert(0, name);
            if (d.RecentDuelFoes.Count > MaxRecentFoes)
                d.RecentDuelFoes.RemoveRange(MaxRecentFoes, d.RecentDuelFoes.Count - MaxRecentFoes);
        });
    }

    public static int BandPad
    {
        get => Get(d => Math.Clamp(d.BandPad, MinBandPad, MaxBandPad));
        set => Set(d => d.BandPad = Math.Clamp(value, MinBandPad, MaxBandPad));
    }

    /// <summary>도시 창이 열릴 때 줄 효과. 개발 창에서 고른다.</summary>
    public static CityOpenEffect CityOpenEffect
    {
        get => Get(d => Enum.TryParse<CityOpenEffect>(d.CityOpenEffect, out var effect)
            ? effect
            : Settings.CityOpenEffect.Expand);
        set => Set(d => d.CityOpenEffect = value.ToString());
    }

    /// <summary>지도 위에 좌표 상자를 겹쳐 보일지. 개발 창에서 켜고 끈다.</summary>
    public static bool ShowCoordOverlay
    {
        get => Get(d => d.ShowCoordOverlay);
        set => Set(d => d.ShowCoordOverlay = value);
    }

    /// <summary>
    /// 지도 위에 <b>만난 사람</b> 상자를 겹쳐 보일지. 개발 창의 "정보" 가 켜고 끈다.
    /// </summary>
    public static bool ShowPeopleOverlay
    {
        get => Get(d => d.ShowPeopleOverlay);
        set => Set(d => d.ShowPeopleOverlay = value);
    }

    /// <summary>
    /// 지도 위에 <b>제독 컨디션(HP)</b> 상자를 겹쳐 보일지. 개발 창의 「컨디션」이 켜고 끈다.
    /// </summary>
    public static bool ShowConditionOverlay
    {
        get => Get(d => d.ShowConditionOverlay);
        set => Set(d => d.ShowConditionOverlay = value);
    }

    /// <summary>
    /// 햄버거에 <b>발견물 지도</b> 줄을 낼지. 모드 창의 「발견물 지도」가 켜고 끈다.
    /// </summary>
    /// <remarks>
    /// 꺼 두면 줄도 안 뜨고 단축키(<see cref="MapKey"/>)도 안 먹는다 — 원본 항해지도는
    /// 표식을 안 찍으므로 이 지도는 앱이 얹은 것이다.
    /// </remarks>
    public static bool ShowDiscoveryMapMenu
    {
        get => Get(d => d.ShowDiscoveryMapMenu);
        set => Set(d => d.ShowDiscoveryMapMenu = value);
    }

    /// <summary>
    /// 지도를 <b>Ctrl+클릭</b>해 배를 그 자리에 놓을지. 모드 창의 「Ctrl+클릭 배 놓기」가 켜고 끈다.
    /// </summary>
    /// <remarks>
    /// 놀이에는 없는 길이다 — 끄면 Ctrl 을 짚고 찍어도 여느 클릭처럼 닻만 오르내린다.
    /// 잘못 눌러 배가 엉뚱한 데로 뛰는 것을 막고 싶을 때 끈다.
    /// </remarks>
    public static bool PlaceShipByCtrlClick
    {
        get => Get(d => d.PlaceShipByCtrlClick);
        set => Set(d => d.PlaceShipByCtrlClick = value);
    }

    /// <summary>
    /// 햄버거에 <b>인물 이동</b> 줄을 낼지. 모드 창의 「인물 이동」이 켜고 끈다.
    /// </summary>
    public static bool ShowPersonMoveMenu
    {
        get => Get(d => d.ShowPersonMoveMenu);
        set => Set(d => d.ShowPersonMoveMenu = value);
    }

    /// <summary>
    /// 햄버거에 <b>여급 수첩</b> 줄을 낼지. 모드 창의 「여급 수첩」이 켜고 끈다.
    /// </summary>
    public static bool ShowBarmaidBookMenu
    {
        get => Get(d => d.ShowBarmaidBookMenu);
        set => Set(d => d.ShowBarmaidBookMenu = value);
    }

    /// <summary>항해·뭍 이동 중 <b>미니맵</b>을 지도 오른쪽 아래에 띄울지. 개발 창의 「미니맵」이 켜고 끈다.</summary>
    public static bool ShowMiniMap
    {
        get => Get(d => d.ShowMiniMap);
        set => Set(d => d.ShowMiniMap = value);
    }

    /// <summary>
    /// 도시에 들어가면 <b>기능·언어</b> 쪽지를 도시 그림 왼쪽에 띄울지. 개발 창의 「기능·언어」가 켜고 끈다.
    /// 바꾸면 <see cref="ShowSkillOverlayChanged"/> 로 알려 떠 있는 도시 창이 곧바로 붙이거나 걷는다.
    /// </summary>
    public static bool ShowSkillOverlay
    {
        get => Get(d => d.ShowSkillOverlay);
        set
        {
            Set(d => d.ShowSkillOverlay = value);
            ShowSkillOverlayChanged?.Invoke();
        }
    }

    /// <summary><see cref="ShowSkillOverlay"/> 가 바뀌었다.</summary>
    public static event Action? ShowSkillOverlayChanged;

    /// <summary>
    /// 계약을 맺을 때 배가 있으면 후원자가 「배를 빌리겠습니까?」를 묻는지(<c>0x00410724</c>).
    /// 끄면 묻지 않고 안 빌린 것으로 한다 — 모드 창에서 켜고 끈다.
    /// </summary>
    public static bool AskLendShips
    {
        get => Get(d => d.AskLendShips);
        set => Set(d => d.AskLendShips = value);
    }

    /// <summary>
    /// 도시에 들어설 때마다 자동저장할지 — 모드 창에서 켜고 끈다.
    /// </summary>
    /// <remarks>
    /// 배로 입항하든 뭍으로 성문을 지나든 같은 자리를 거치므로(<c>EnterCity</c>) <b>항구가
    /// 없는 내륙 마을</b>에서도 적힌다. 적는 자리는 <see cref="Engine.GameSave.AutoPath"/> 라
    /// 손으로 적어 둔 것과 따로다. 첫 화면의 <b>CONTINUE</b> 가 그 파일을 연다.
    /// </remarks>
    public static bool AutoSaveOnPort
    {
        get => Get(d => d.AutoSaveOnPort);
        set => Set(d => d.AutoSaveOnPort = value);
    }

    /// <summary>지도 위의 까만 조작 줄을 보일지. 개발 창에서 켜고 끈다.</summary>
    public static bool ShowToolBar
    {
        get => Get(d => d.ShowToolBar);
        set => Set(d => d.ShowToolBar = value);
    }

    /// <summary>게임 상단 띠에 켜 둔 칸 이름들. 도시정보 창에서 켜고 끈다.</summary>
    public static IReadOnlyList<string>? BarCells
    {
        get => Get(d => d.BarCells);
        set => Set(d => d.BarCells = value == null ? null : [.. value]);
    }

    /// <summary>
    /// 고를 수 있는 게임 창 크기.
    /// </summary>
    /// <remarks>
    /// 원본은 <b>640x480 한 가지</b>다. 우리 지도는 훑어 보는 창이라 넓으면 넓은 만큼 더
    /// 보이므로 몇 가지를 열어 둔다. 원본 그림이 4:3 이라 4:3 을 위주로 두고, 지금까지
    /// 쓰던 1200x800 을 그대로 기본으로 남긴다. 폭이 0 이면 <b>전체 화면</b>이다.
    /// </remarks>
    public static readonly (string Name, int Width, int Height)[] Resolutions =
    [
        ("800 x 600", 800, 600),
        ("1024 x 768", 1024, 768),
        ("1200 x 800", 1200, 800),
        ("1280 x 960", 1280, 960),
        ("1600 x 1200", 1600, 1200),
        ("전체 화면", 0, 0),
    ];

    /// <summary>기본 크기 — 지금까지 쓰던 1200x800 이다.</summary>
    public const int DefaultResolution = 2;

    /// <summary>지금 고른 창 크기가 <see cref="Resolutions"/> 의 몇째인지.</summary>
    public static int Resolution
    {
        get => Get(d => Math.Clamp(d.Resolution, 0, Resolutions.Length - 1));
        set => Set(d => d.Resolution = Math.Clamp(value, 0, Resolutions.Length - 1));
    }

    /// <summary>지금 고른 창 크기. 폭이 0 이면 전체 화면이다.</summary>
    public static (string Name, int Width, int Height) WindowSize => Resolutions[Resolution];

    /// <summary>지도 위에 바람·해류 화살표를 얹을지. 함대 창 커맨드에서 켜고 끈다.</summary>
    public static bool ShowFlowArrows
    {
        get => Get(d => d.ShowFlowArrows);
        set => Set(d => d.ShowFlowArrows = value);
    }

    /// <summary>발견물 지도에 풍향 화살표를 얹을지. 그 창의 「풍향」 단추로 켜고 끈다.</summary>
    public static bool DiscoveryMapWind
    {
        get => Get(d => d.DiscoveryMapWind);
        set => Set(d => d.DiscoveryMapWind = value);
    }

    /// <summary>발견물 지도에 해류 화살표를 얹을지. 그 창의 「해류」 단추로 켜고 끈다.</summary>
    public static bool DiscoveryMapCurrent
    {
        get => Get(d => d.DiscoveryMapCurrent);
        set => Set(d => d.DiscoveryMapCurrent = value);
    }

    /// <summary>
    /// 육상전 모의전 창이 지난번에 차렸던 짜임. 한 번도 안 차렸으면 null.
    /// </summary>
    /// <remarks>
    /// 「싸운다」를 누를 때 적히고, 창을 열 때 되편다. 그 창은 놀이가 아니라 시험 삼아
    /// 굴려 보는 자리라 앱을 껐다 켜도 남긴다.
    /// </remarks>
    public static LandSparData? LandSpar
    {
        get => Get(d => d.LandSpar);
        set => Set(d => d.LandSpar = value);
    }
}
