using System.IO;
using System.Windows.Media;

namespace CdsHelper.Game.Local.Helpers;

/// <summary>
/// 게임 폴더의 BGM 한 곡을 되풀이해 튼다.
/// </summary>
/// <remarks>
/// 파일은 게임 폴더 밑 <c>bgm/TrackNN.mp3</c> 다. 저장소로 복사하지 않고 그 자리에서 읽는다 —
/// 한 벌이 130MB 라 들고 다닐 것이 못 된다. 폴더가 없으면 아무 소리도 내지 않고 넘어간다.
///
/// <see cref="MediaPlayer"/> 는 만든 스레드(UI)에 매인다. 창에서 하나 만들어 쓰고 닫을 때 놓는다.
/// </remarks>
public sealed class BgmPlayer : IDisposable
{
    /// <summary>게임을 시작할 때 준비 여부를 확인할 대표 곡.</summary>
    public const int RequiredTrack = TitleTrack;

    /// <summary>타이틀 화면에서 도는 곡.</summary>
    public const int TitleTrack = 23;

    /// <summary>바다에서 도는 곡.</summary>
    public const int SeaTrack = 15;

    /// <summary>도시에 들어가 있는 동안 도는 곡.</summary>
    public const int CityTrack = 10;

    /// <summary>뭍에 올라 말로 다니는 동안 도는 곡.</summary>
    public const int LandTrack = 26;

    /// <summary>육상전이 도는 동안의 곡. 해전도 같은 곡이다.</summary>
    public const int BattleTrack = 28;

    /// <summary>놀이가 끝났을 때(CONTINUE? 화면) 도는 곡.</summary>
    public const int GameOverTrack = 8;

    /// <summary>술집에 들어가 있는 동안 도는 곡.</summary>
    public const int TavernTrack = 22;

    /// <summary>
    /// 일기토가 도는 동안의 곡 — 반란도 이 판으로 치르므로 반란 곡이기도 하다.
    /// </summary>
    /// <remarks>
    /// 일기토 들머리(<c>0x004AA700</c>)가 지금 곡을 멈추고(<c>0x00422A40(-1, 3)</c>) 소리 9 를
    /// 튼다(<c>0x004AA8A0</c> · <c>0x004225A0(9, 0)</c>). 트랙으로는 소리 + 2 = 11 이다.
    /// </remarks>
    public const int DuelTrack = 11;

    /// <summary>교회에 들어가 있는 동안 도는 곡. 나오면 도시 곡으로 돌아간다.</summary>
    public const int ChurchTrack = 16;

    /// <summary>
    /// 왕궁(건물 코드 2)에 들어가 있는 동안 도는 곡 — 소리 <c>0x12</c> 라 트랙으로는 20 이다
    /// (<c>0x00492B25</c>). 유럽 문화권(0·1·2) 도시에서만 바뀐다.
    /// </summary>
    public const int PalaceTrack = 0x12 + SoundToTrack;

    /// <summary>
    /// 해상에서 그 자리에 맞는 곡. 위경도로 구간을 갈라 고른다.
    /// </summary>
    /// <remarks>
    /// 게임의 <c>0x0048D5D8</c> 을 그대로 옮겼다. 배 자리를 담아 두는 두 값
    /// (<c>0x005B63B0</c> · <c>0x005B63B4</c>)은 <b>칸 좌표에 16 을 곱한 것</b>이다 —
    /// 켜 놓은 게임에서 칸 좌표와 맞대어 비율이 정확히 16 인 것을 확인했다.
    ///
    /// 게임이 견주는 값을 도로 풀면 경계가 다 딱 떨어진다.
    /// <code>
    ///   Y  3888 = 북위 55      X 12778 = 서경 65
    ///   Y  6666 = 북위 30      X 25000 = 동경 45
    ///   Y  7777 = 북위 20      X 23330 = 동경 30
    ///   Y 16112 = 남위 55      X 22330 = 동경 21 · 34430 = 동경 130
    /// </code>
    /// 그래서 규칙이 이렇게 된다.
    /// <code>
    ///   북위 55 위 · 남위 55 아래           19
    ///   북위 55~30 에서 서경 65~동경 45     15   그 밖은 13
    ///   북위 30 아래에서 서경 65~동경 30    25
    ///   북위 20 위                        13
    ///   동경 21~130                       13   그 밖은 24
    /// </code>
    /// <b>바뀌는 것은 지금 곡이 끝난 뒤다</b> — 게임도 이 갈래에서만
    /// <c>CDAudioNextPlayTrack</c>(기다렸다 바꾸기)을 쓴다. 그래서 경계를 넘고도 한동안
    /// 옛 곡이 이어져, 듣는 쪽에서는 경계가 더 북쪽인 것처럼 느껴진다.
    /// </remarks>
    public static int SeaTrackAt(double cellX, double cellY)
    {
        // 게임이 쓰는 값 그대로 견준다(칸 x 16).
        int x = (int)(cellX * PositionScale);
        int y = (int)(cellY * PositionScale);

        if (y < 3888 || y >= 16112) return 19;          // 북위 55 위 · 남위 55 아래

        if (y < 6666)                                    // 북위 55 ~ 30
            return x >= 12778 && x < 25000 ? SeaTrack : 13;

        if (x >= 12778 && x < 23330) return 25;          // 북위 30 아래, 서경 65~동경 30
        if (y < 7777) return 13;                         // 북위 20 위
        return x >= 22330 && x < 34430 ? 13 : 24;        // 동경 21~130
    }

    /// <summary>배 자리를 담아 두는 값이 칸 좌표의 몇 곱인지.</summary>
    private const int PositionScale = 16;

    /// <summary>
    /// 게임 소리 번호를 CD 트랙 번호로 옮기는 값.
    /// </summary>
    /// <remarks>
    /// 게임은 안에서 <b>소리 번호</b>로 부르고 <c>koeicda.dll</c> 에는 CD 트랙으로 넘긴다.
    /// 둘의 차이가 늘 2 다 — 알고 있던 곡 여섯으로 맞대어 확인했다.
    /// <code>
    ///   소리 8  -> 트랙 10  도시(이베리아)     소리 20 -> 트랙 22  술집
    ///   소리 7  -> 트랙  9  도시(북유럽)       소리 21 -> 트랙 23  타이틀
    ///   소리 5  -> 트랙  7  도시(중근동)       소리 24 -> 트랙 26  상륙
    /// </code>
    /// </remarks>
    private const int SoundToTrack = 2;

    /// <summary>
    /// 문화권별 도시 곡. 색인이 문화권 번호(<see cref="CityExeTable.CultureOf"/>)다.
    /// </summary>
    /// <remarks>
    /// 게임 EXE 의 표 <c>0x0056A078</c>(8바이트 x 11 — 소리 번호와 이름)를 그대로 옮겼다.
    /// 그 표를 쓰는 자리는 <c>0x004929E7</c> 이다 — <c>mov edx, [ecx*8 + 0x0056A078]</c> 로
    /// 도시 레코드 <c>+0x58</c>(문화권)을 색인 삼아 소리 번호를 꺼낸다.
    ///
    /// 여기에는 <b>트랙 번호</b>로 옮겨 적었다(소리 번호 + 2).
    /// </remarks>
    private static readonly int[] CityTrackByCulture =
    [
        10,  //  0 이베리아
         9,  //  1 북유럽
         5,  //  2 지중해
         2,  //  3 아프리카
         7,  //  4 중근동(이슬람)
        12,  //  5 인도
         6,  //  6 중국
        27,  //  7 중앙아시아
        27,  //  8 동남아시아
        18,  //  9 일본
         4,  // 10 아메리카
    ];

    /// <summary>문화권 번호로 도시 곡을 고른다. 모르는 번호면 기본 도시 곡.</summary>
    public static int CityTrackForCulture(int culture) =>
        culture >= 0 && culture < CityTrackByCulture.Length ? CityTrackByCulture[culture] : CityTrack;

    /// <summary>
    /// 그 도시에서 돌 곡. 문화권마다 다르다 — 세우타처럼 중근동에 드는 도시는 딴 곡이 돈다.
    /// </summary>
    /// <remarks>
    /// 이름으로 받는 길이다. 자료에 적힌 이름은 "이슬람" 인데 게임에서 부르는 말은
    /// "중근동" 이다 — 둘 다 받는다. 번호를 알면 <see cref="CityTrackForCulture"/> 가 낫다.
    /// </remarks>
    public static int CityTrackFor(string? culturalSphere) => CityTrackFor(culturalSphere, -1);

    /// <summary>
    /// 이름으로 고르되, <b>표에 없는 이름</b>(「발칸」 따위)이거나 이름이 비었으면 EXE 문화권 번호로 고른다.
    /// </summary>
    /// <remarks>
    /// 게임은 이름을 안 보고 도시 레코드 <c>+0x58</c> 의 번호만 본다(<c>0x004929E7</c>). 앱 도시 자료에는
    /// 간디아가 「발칸」으로 적혀 있어 이름으로만 고르면 기본 곡(이베리아, 10)이 났다 — 원본은 지중해(5)다.
    /// </remarks>
    public static int CityTrackFor(string? culturalSphere, int exeCulture) => culturalSphere switch
    {
        "이베리아" => CityTrackByCulture[0],
        "북유럽" => CityTrackByCulture[1],
        "지중해" => CityTrackByCulture[2],
        "아프리카" => CityTrackByCulture[3],
        "이슬람" or "중근동" => CityTrackByCulture[4],
        "인도" => CityTrackByCulture[5],
        "중국" => CityTrackByCulture[6],
        "중앙아시아" => CityTrackByCulture[7],
        "동남아시아" => CityTrackByCulture[8],
        "일본" => CityTrackByCulture[9],
        "아메리카" => CityTrackByCulture[10],
        _ => CityTrackForCulture(exeCulture),
    };

    /// <summary>
    /// 후원자를 알현하는 동안 도는 곡. 집사가 문간에서 돌려보내면 바뀌지 않는다 —
    /// 인사를 받고 안으로 들어갈 때부터다. 그 자리를 나오면 도시 곡으로 돌아간다.
    /// </summary>
    public const int SponsorTrack = 21;

    private readonly MediaPlayer _player = new();
    private string _dir = "";
    private int _track = -1;

    public BgmPlayer()
    {
        // 지난번에 맞춰 둔 소리 크기로 시작한다.
        Volume = Settings.GameSettings.BgmVolume / (double)Settings.GameSettings.MaxVolume;

        _player.MediaEnded += (_, _) =>
        {
            // 기다리라고 해 둔 곡이 있으면 이제 갈아탄다(해상에서 해역이 바뀔 때다).
            if (_queued >= 0)
            {
                int next = _queued;
                _queued = -1;
                Play(next);
                return;
            }

            // 없으면 처음으로 되감아 다시 튼다. 이어 재생은 이 한 줄이면 된다.
            _player.Position = TimeSpan.Zero;
            _player.Play();
        };
    }

    /// <summary>지금 곡이 끝나면 갈아탈 곡. 없으면 -1.</summary>
    private int _queued = -1;

    /// <summary>지금 곡이 끝나면 갈아탈 곡. 기다리는 것이 없으면 -1.</summary>
    public int Queued => _queued;

    /// <summary>못 튼 까닭 한 줄. 잘 돌고 있으면 빈 문자열.</summary>
    public string LastError { get; private set; } = "";

    /// <summary>지금 도는 곡 번호. 아무것도 안 틀고 있으면 -1.</summary>
    public int Track => _track;

    /// <summary>게임 폴더를 알려 준다. 그 밑의 <c>bgm</c> 을 본다.</summary>
    public void SetGameDirectory(string gameDir) => _dir = gameDir;

    /// <summary>게임 폴더에 BGM이 준비되어 있는지 확인한다.</summary>
    public static bool IsAvailable(string gameDir) =>
        File.Exists(Path.Combine(gameDir, "bgm", $"Track{RequiredTrack:D2}.mp3"))
        || File.Exists(BgmAssetDownloader.CachePath(RequiredTrack));

    /// <summary>곡을 틀지. 끄면 소리를 멈추고, 켜면 마지막으로 틀라던 곡부터 다시 돈다.</summary>
    public bool Enabled
    {
        get => _enabled;
        set
        {
            if (_enabled == value) return;
            _enabled = value;
            if (!value) { Stop(); return; }
            int again = _wanted;
            _wanted = -1;
            if (again >= 0) Play(again);
        }
    }

    private bool _enabled = true;

    /// <summary>마지막으로 틀라고 한 곡. 껐다 켤 때 이 곡으로 돌아온다.</summary>
    private int _wanted = -1;

    /// <summary>
    /// 지금 곡을 끝까지 들려 준 뒤에 갈아탄다. <b>해상에서 해역이 바뀔 때</b> 쓴다 —
    /// 게임은 그때만 곡을 자르지 않고 기다린다.
    /// </summary>
    /// <remarks>
    /// 도시에 들어가거나 건물에 들어설 때는 이것이 아니라 <see cref="Play"/> 다.
    /// 그쪽은 곡을 그 자리에서 자른다 — 게임도 그렇다.
    ///
    /// 아무것도 안 돌고 있으면 기다릴 것이 없으므로 바로 튼다.
    /// </remarks>
    public void PlayWhenDone(int track)
    {
        if (_track == track) { _queued = -1; return; }
        if (_track < 0 || !_enabled) { Play(track); return; }
        _queued = track;
    }

    /// <summary>그 번호의 곡으로 갈아 튼다. 이미 그 곡이 돌고 있으면 그대로 둔다.</summary>
    public void Play(int track)
    {
        _queued = -1;   // 바로 갈아타라고 했으니 기다리던 것은 버린다
        if (_wanted == track && _track == track) return;
        _wanted = track;
        if (!_enabled) return;
        if (_track == track) return;

        var path = Path.Combine(_dir, "bgm", $"Track{track:D2}.mp3");
        if (!File.Exists(path))
            path = BgmAssetDownloader.CachePath(track);
        if (!File.Exists(path))
        {
            LastError = $"{path} 없음";
            return;
        }

        try
        {
            _player.Open(new Uri(path));
            _player.Volume = Volume;
            _player.Play();
            _track = track;
            LastError = "";
        }
        catch (Exception ex)
        {
            LastError = $"{path} 를 틀지 못했습니다 — {ex.Message}";
        }
    }

    /// <summary>
    /// 소리 크기(0~1). 설정 창에서 갈면 <b>틀고 있는 곡에 곧바로</b> 먹는다.
    /// </summary>
    public double Volume
    {
        get => _volume;
        set
        {
            _volume = Math.Clamp(value, 0, 1);
            _player.Volume = _volume;
        }
    }

    private double _volume = 1;

    public void Stop()
    {
        _player.Stop();
        _player.Close();
        _track = -1;
    }

    public void Dispose() => Stop();
}
