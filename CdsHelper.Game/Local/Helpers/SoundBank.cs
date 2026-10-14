using System.IO;
using System.Media;
using CdsHelper.Support.Local.Helpers;

using CdsHelper.Support.Local.Settings;
using CdsHelper.Game.Local.Settings;

namespace CdsHelper.Game.Local.Helpers;

/// <summary>
/// 게임 효과음(WAVES.CDS)을 틀어 준다. 게임 중에 짧게 소리를 낼 때 쓴다.
/// </summary>
/// <remarks>
/// <see cref="WaveBank"/> 가 파트를 RIFF WAVE 통째로 풀어 주므로 그대로
/// <see cref="SoundPlayer"/> 에 넘기면 된다. 효과음 목록을 보는 창
/// (<c>WaveBankDialog</c>)과 같은 길이다.
///
/// 배경음악(<see cref="BgmPlayer"/>)과 따로 논다 — 효과음은 곡을 끊지 않고 겹쳐 난다.
/// 효과음은 <see cref="GameSettings.SfxEnabled"/> 하나로 갈린다 — 배경음악과 따로 켜고 끈다.
/// </remarks>
public sealed class SoundBank : IDisposable
{
    /// <summary>
    /// 닻을 내리고 올릴 때 나는 소리. 효과음 창의 <b>파트 1</b>(사운드 ID 29) 이다.
    /// </summary>
    /// <remarks>
    /// 파트 번호로 적는다 — 사운드 ID 는 여기에 28 을 더한 값이라 헷갈리기 쉽다
    /// (<see cref="WaveBank.FirstSoundId"/>).
    /// </remarks>
    public const int AnchorPart = 1;

    /// <summary>
    /// 집사가 문 앞에서 돌려보낼 때 나는 소리("…님은 바쁘셔서 만나실 수 없습니다").
    /// 닻과 같은 파트 1 이다.
    /// </summary>
    public const int TurnedAwayPart = 1;

    /// <summary>
    /// 성배 퍼즐에서 <b>물을 옮길 때</b> 나는 소리 — 파트 0(사운드 ID 28)이다.
    /// </summary>
    /// <remarks>
    /// 붓는 몸짓을 한 걸음 옮기는 <c>0x00467A90</c> 이 맨 앞에서
    /// <c>0x004225A0(0x1C, 0)</c> 을 부른다. <c>0x1C</c> 가 28 이고 WAVE 는 28부터라
    /// 파트 0 이다. 소리 내는 손은 <c>0x004225A0(사운드 ID, 0)</c> 하나고 부르는 데가
    /// 아흔 남짓이라, 다른 효과음도 이 자리에서 되짚으면 된다.
    /// </remarks>
    public const int PourPart = 0;

    /// <summary>성배를 다 채웠을 때 나는 소리 — 파트 11(사운드 ID 39, <c>0x0046860A</c>).</summary>
    public const int GrailWonPart = 11;

    /// <summary>
    /// 부대배치에서 부대를 <b>놓을 때</b> 나는 소리 — 파트 0(사운드 ID 28, <c>0x0049F23E</c>).
    /// </summary>
    /// <remarks>성배에 물을 부을 때와 같은 소리다(<see cref="PourPart"/>).</remarks>
    public const int DeployPlacePart = 0;

    /// <summary>
    /// 부대배치에서 부대를 <b>걷을 때</b> 나는 소리 — 파트 1(사운드 ID 29, <c>0x0049F205</c>).
    /// </summary>
    /// <remarks>닻을 올리고 내릴 때와 같은 소리다(<see cref="AnchorPart"/>).</remarks>
    public const int DeployLiftPart = 1;

    /// <summary>
    /// 일기토에서 명령을 고를 때 나는 칼 부딪히는 소리 — <b>사운드 ID 72</b>,
    /// 곧 파트 44 다.
    /// </summary>
    /// <remarks>
    /// 여기 상수는 죄다 파트 번호다. 사운드 ID 는 파트에 28 을 더한 값이라
    /// (<see cref="WaveBank.FirstSoundId"/>) 72 - 28 = 44 로 적는다.
    /// </remarks>
    public const int ClashPart = 72 - WaveBank.FirstSoundId;

    /// <summary>일기토에서 이겼을 때 나는 소리(사운드 ID <b>77</b>).</summary>
    public const int DuelWinPart = 77 - WaveBank.FirstSoundId;

    /// <summary>
    /// 일기토에서 졌을 때 나는 소리(사운드 ID <b>74</b>) — <c>0x004A6FCA</c> 가
    /// 진 판에 <c>0x4A</c> 를, 이긴 판에 <c>0x4D</c> 를 낸다.
    /// </summary>
    public const int DuelLosePart = 74 - WaveBank.FirstSoundId;

    /// <summary>
    /// 아래 띠에 말을 넣을 때 나는 소리 — <b>사운드 ID 0x1D</b>, 곧 파트 1 이다.
    /// </summary>
    /// <remarks>
    /// 띠 말 넣기 <c>0x0040E0A0</c> 이 <c>0x004225A0(0x1D, 0)</c> 을 부른다. 펼친 책의
    /// 「%s의 지식이 필요합니다」 가 이 소리와 같이 뜬다. 띠를 비우는 <c>0x0040E0C0</c> 은 소리가 없다.
    /// </remarks>
    public const int BandNoticePart = 0x1D - WaveBank.FirstSoundId;

    private readonly WaveBank? _bank;
    private readonly SoundPlayer _player = new();

    private SoundBank(WaveBank? bank) => _bank = bank;

    /// <summary>
    /// 뽑아 둔 효과음이 든 곳 — <b>게임 폴더가 없어도</b> 소리가 나게 미리 풀어 둔 것이다
    /// (<c>tools/extract_sfx.py</c>). 배 그림 <c>asset/ship</c>·아이템 그림
    /// <c>asset/item</c> 과 같은 길이다.
    /// </summary>
    /// <remarks>파일 이름은 <b>파트 번호</b>고, 사운드 ID 는 여기에 28 을 더한 값이다.</remarks>
    public const string AssetDirectory = "asset/sfx";

    /// <summary>뽑아 둔 효과음 수(WAVES.CDS 의 파트 수와 같다).</summary>
    public const int AssetParts = 50;

    /// <summary>뽑아 둔 것을 읽어 든 벌. 한 번 읽은 것은 들고 있는다.</summary>
    private readonly byte[]?[] _asset = new byte[]?[AssetParts];

    /// <summary>왜 못 열었는지. 잘 열렸으면 빈 문자열.</summary>
    public static string LastError { get; private set; } = "";

    /// <summary>게임 폴더의 WAVES.CDS 를 연다. 없으면 null — 소리가 안 날 뿐이다.</summary>
    public static SoundBank? Open(string gameDirectory)
    {
        LastError = "";

        var bank = gameDirectory.Length > 0 ? WaveBank.LoadFromDirectory(gameDirectory) : null;
        if (bank != null) return new SoundBank(bank);

        // 게임 폴더가 없거나 WAVES.CDS 를 못 읽으면 <b>뽑아 둔 것</b>으로 물러선다.
        LastError = gameDirectory.Length > 0 ? WaveBank.LastError : "게임 폴더를 모릅니다";
        return Directory.Exists(Path.Combine(AppContext.BaseDirectory, AssetDirectory))
            ? new SoundBank(null)
            : null;
    }

    /// <summary>
    /// 갈아 낀 효과음이 드는 곳 — <c>%LocalAppData%\CdsHelper\sfx</c>. 실행 파일 폴더 바깥이라 업데이트해도 남는다
    /// (설치판은 업데이트마다 실행 파일 폴더를 통째로 갈아 끼운다). 파일 이름은 <c>sfx-번호.wav</c>(번호는 파트, 두 자리).
    /// </summary>
    public static string CustomDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CdsHelper", "sfx");

    /// <summary>원본 효과음을 꺼내 두는 곳 — 어느 번호가 어느 소리인지 들어 보고 고르라고 둔다.</summary>
    public static string OriginalsDirectory => Path.Combine(CustomDirectory, "원본");

    /// <summary>갈아 낀 파일을 읽어 든 것 — 파일이 바뀌면(고친 시각) 다시 읽는다.</summary>
    private readonly Dictionary<int, (DateTime Stamp, byte[] Wav)> _custom = [];

    /// <summary>
    /// 그 파트의 소리 — 모드 「커스텀 효과음」이 켜져 있고 효과음 폴더에 그 번호 파일이 있으면 그것, 아니면 원본
    /// (게임 폴더의 WAVES.CDS), 그것도 없으면 뽑아 둔 것이다.
    /// </summary>
    private byte[]? WavOf(int part)
    {
        if (GameSettings.CustomSfxEnabled && CustomWav(part) is { } custom) return custom;
        return OriginalWav(part);
    }

    /// <summary>원본 소리 — 게임 폴더의 WAVES.CDS, 없으면 뽑아 둔 것.</summary>
    private byte[]? OriginalWav(int part) => _bank?.Wav(part) ?? AssetWav(part);

    private byte[]? CustomWav(int part)
    {
        string path = Path.Combine(CustomDirectory, $"sfx-{part:D2}.wav");
        try
        {
            if (!File.Exists(path)) return null;
            var stamp = File.GetLastWriteTimeUtc(path);
            if (_custom.TryGetValue(part, out var held) && held.Stamp == stamp) return held.Wav;
            var wav = File.ReadAllBytes(path);
            _custom[part] = (stamp, wav);
            return wav;
        }
        catch (Exception) { return null; }
    }

    /// <summary>
    /// 원본 효과음 전부를 <see cref="OriginalsDirectory"/> 에 <c>sfx-번호.wav</c> 로 꺼낸다. 꺼낸 수를 낸다.
    /// 이미 있는 파일은 덮어쓴다 — 게임 폴더가 바뀌었을 수 있다.
    /// </summary>
    public int ExportOriginals()
    {
        Directory.CreateDirectory(OriginalsDirectory);
        int count = 0;
        for (int part = 0; part < AssetParts; part++)
        {
            if (OriginalWav(part) is not { } wav) continue;
            File.WriteAllBytes(Path.Combine(OriginalsDirectory, $"sfx-{part:D2}.wav"), wav);
            count++;
        }
        return count;
    }

    /// <summary>뽑아 둔 효과음 한 자락. 없으면 null.</summary>
    private byte[]? AssetWav(int part)
    {
        if (part < 0 || part >= AssetParts) return null;
        if (_asset[part] is { } held) return held;

        var path = Path.Combine(AppContext.BaseDirectory, AssetDirectory, $"sfx-{part:D2}.wav");
        if (!File.Exists(path)) return null;

        try { return _asset[part] = File.ReadAllBytes(path); }
        catch (Exception) { return null; }
    }

    private static SoundBank? _shared;
    private static string _sharedDirectory = "";

    /// <summary>
    /// 앱이 함께 쓰는 한 벌. 여러 창이 소리를 내므로 파일을 창마다 다시 풀지 않는다.
    /// 게임 폴더가 바뀌면 그때 다시 연다.
    /// </summary>
    /// <summary>
    /// 지금 열려 있는 한 벌 — 게임 폴더를 모르는 창(일기토 판 따위)이 쓴다. 아직 아무도 안 열었으면 폴더 없이 연다.
    /// </summary>
    public static SoundBank? Current => _shared ?? Shared("");

    public static SoundBank? Shared(string gameDirectory)
    {
        // 폴더를 몰라도 연다 — 뽑아 둔 것으로 소리는 난다.
        gameDirectory ??= "";
        if (_shared != null && _sharedDirectory == gameDirectory) return _shared;

        _shared?.Dispose();
        _sharedDirectory = gameDirectory;
        _shared = Open(gameDirectory);
        if (_shared == null)
            System.Diagnostics.Debug.WriteLine($"[SoundBank] 효과음 없음: {LastError}");
        return _shared;
    }

    /// <summary>
    /// 효과음 하나를 낸다. 못 풀거나 못 틀면 조용히 넘어간다 — 소리 때문에 게임이 멎을 일은 없다.
    /// </summary>
    public void Play(int part)
    {
        if (!GameSettings.SfxEnabled) return;
        try
        {
            // 원본 것을 먼저 보고, 없으면 뽑아 둔 것으로 낸다.
            var wav = WavOf(part);
            if (wav == null) return;

            // SoundPlayer 는 스트림을 물고 있으므로 틀 때마다 새로 잡아 넘긴다.
            _player.Stream = new MemoryStream(Scaled(wav, GameSettings.SfxVolume));
            _player.Play();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[SoundBank] 파트 {part} 를 틀지 못했습니다 — {ex.Message}");
        }
    }

    /// <summary>
    /// 소리를 그 크기로 줄인 벌을 낸다 — <see cref="SoundPlayer"/> 에는 크기 손잡이가 없다.
    /// </summary>
    /// <remarks>
    /// 게임 효과음은 22kHz <b>8비트 부호 없는</b> 소리라 128 이 무음이다. 그 자리를 밑삼아
    /// 폭만 줄이면 된다. WAV 머리(44바이트)는 그대로 두고 소리 알맹이만 손댄다.
    /// </remarks>
    private static byte[] Scaled(byte[] wav, int volume)
    {
        if (volume >= GameSettings.MaxVolume) return wav;
        if (wav.Length <= WavHeader) return wav;
        // 갈아 낀 파일은 8비트가 아닐 수 있다(16비트 · 머리가 긴 것). 줄이는 셈은 8비트 무음 128 을 밑삼으므로
        // 그런 파일은 손대지 않고 그대로 낸다 — 크기 손잡이가 안 먹을 뿐 소리는 깨지지 않는다.
        if (!Plain8Bit(wav)) return wav;

        var made = (byte[])wav.Clone();
        for (int i = WavHeader; i < made.Length; i++)
            made[i] = (byte)(Silence + (made[i] - Silence) * volume / GameSettings.MaxVolume);
        return made;
    }

    /// <summary>
    /// 내고 있던 효과음을 끊는다 — 되풀이 소리(<see cref="PlayLoop"/>)는 안 끊는다(<see cref="StopLoop"/> 가 끊는다). 사건 애니메이션이 끝날 때 제 소리를 끄는 자리
    /// (<c>0x00422A40(소리, 3)</c>)에 쓴다.
    /// </summary>
    public void Stop()
    {
        try { _player.Stop(); }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[SoundBank] 소리를 끊지 못했습니다 — {ex.Message}");
        }
    }

    /// <summary>머리 44바이트짜리 8비트 PCM WAV 인지 — 게임 효과음이 다 이 꼴이다.</summary>
    private static bool Plain8Bit(byte[] wav) =>
        wav.Length > WavHeader
        && wav[0] == (byte)'R' && wav[1] == (byte)'I' && wav[2] == (byte)'F' && wav[3] == (byte)'F'
        && wav[34] == 8 && wav[35] == 0
        && wav[36] == (byte)'d' && wav[37] == (byte)'a' && wav[38] == (byte)'t' && wav[39] == (byte)'a';

    /// <summary>WAV 머리 길이와 8비트 소리의 무음 자리.</summary>
    private const int WavHeader = 44, Silence = 128;

    /// <summary>
    /// 되풀이 소리를 트는 손 — <see cref="SoundPlayer"/> 가 아니다. SoundPlayer 는 Win32 <c>PlaySound</c> 라
    /// 한 프로세스에 <b>한 소리만</b> 난다 — 효과음 하나(<see cref="Play"/>)가 빗소리·사건 장면 소리를 대신하며
    /// 끊고, <see cref="Stop"/> 도 되풀이를 함께 껐다. MediaPlayer 는 따로 섞여 나므로 둘이 겹쳐 난다.
    /// </summary>
    private System.Windows.Media.MediaPlayer? _loop;

    /// <summary>되풀이할 소리를 풀어 두는 곳 — MediaPlayer 는 메모리 스트림을 못 받아 파일로 넘긴다.</summary>
    private static readonly string LoopDirectory = Path.Combine(Path.GetTempPath(), "CostaDelSol-sfx");

    /// <summary>
    /// 되풀이해 내는 소리 — 지도 위 빗소리(<c>0x004225A0(0x3F, 0)</c>)가 이것이다. 효과음과 따로 돈다.
    /// </summary>
    public void PlayLoop(int part)
    {
        if (!GameSettings.SfxEnabled) return;
        try
        {
            var wav = WavOf(part);
            if (wav == null) return;

            // 크기를 줄인 벌마다 따로 적는다 — 돌고 있는 파일을 덮어쓰지 않는다.
            int volume = GameSettings.SfxVolume;
            Directory.CreateDirectory(LoopDirectory);
            string path = Path.Combine(LoopDirectory, $"loop-{part:D2}-{volume}-{wav.Length}.wav");
            if (!File.Exists(path)) File.WriteAllBytes(path, Scaled(wav, volume));

            if (_loop == null)
            {
                _loop = new System.Windows.Media.MediaPlayer { Volume = 1.0 };
                // 끝에 닿으면 처음으로 되감아 다시 튼다.
                _loop.MediaEnded += (_, _) =>
                {
                    if (_loop == null) return;
                    _loop.Position = TimeSpan.Zero;
                    _loop.Play();
                };
            }
            _loop.Stop();
            _loop.Open(new Uri(path));
            _loop.Volume = 1.0;          // 크기는 소리 알맹이에 이미 먹였다(Scaled)
            _loop.Play();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[SoundBank] 파트 {part} 를 되풀이하지 못했습니다 — {ex.Message}");
        }
    }

    /// <summary>
    /// <b>겹쳐 나는</b> 효과음 — 앞 소리를 끊지 않고 그 위에 섞어 낸다. 해전처럼 소리가 잇달아 나는 판에 쓴다.
    /// </summary>
    /// <remarks>
    /// <see cref="Play"/> 의 SoundPlayer(Win32 <c>PlaySound</c>)는 한 프로세스에 한 소리뿐이라, 해전에서 포탄이 닿자마자 나는
    /// 명중 소리가 0.74초짜리 발사 소리를 0.1초 남짓에서 끊고, 다음 발의 발사 소리가 1.3초짜리 명중 소리를 또 끊었다.
    /// 여기서는 MediaPlayer 여럿을 돌려 가며 쓴다 — MediaPlayer 는 저마다 따로 섞여 난다. 소리 알맹이는 되풀이 소리처럼
    /// 크기를 먹여 파일로 한 번 적어 두고 다시 쓴다.
    /// </remarks>
    public void PlayOver(int part)
    {
        if (!GameSettings.SfxEnabled) return;
        try
        {
            var wav = WavOf(part);
            if (wav == null) return;

            int volume = GameSettings.SfxVolume;
            Directory.CreateDirectory(LoopDirectory);
            string path = Path.Combine(LoopDirectory, $"over-{part:D2}-{volume}-{wav.Length}.wav");
            if (!File.Exists(path)) File.WriteAllBytes(path, Scaled(wav, volume));

            // 돌려 쓰는 자리 — 차례로 하나씩. 가장 오래 전에 튼 것을 다시 잡으므로, 한꺼번에 OverVoices 개까지 겹친다.
            var player = _over[_overNext] ??= new System.Windows.Media.MediaPlayer();
            _overNext = (_overNext + 1) % _over.Length;
            player.Stop();
            player.Open(new Uri(path));
            player.Volume = 1.0;          // 크기는 소리 알맹이에 이미 먹였다(Scaled)
            player.Play();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[SoundBank] 파트 {part} 를 겹쳐 내지 못했습니다 — {ex.Message}");
        }
    }

    /// <summary>한꺼번에 겹쳐 낼 수 있는 소리 수.</summary>
    private const int OverVoices = 6;

    private readonly System.Windows.Media.MediaPlayer?[] _over = new System.Windows.Media.MediaPlayer?[OverVoices];
    private int _overNext;

    /// <summary>겹쳐 내던 소리를 모두 끊는다.</summary>
    public void StopOver()
    {
        foreach (var player in _over)
        {
            try { player?.Stop(); }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[SoundBank] 겹친 소리를 끊지 못했습니다 — {ex.Message}");
            }
        }
    }

    /// <summary>되풀이하던 소리만 끊는다 — 나고 있는 효과음은 그대로 둔다.</summary>
    public void StopLoop()
    {
        try
        {
            _loop?.Stop();
            _loop?.Close();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[SoundBank] 되풀이 소리를 끊지 못했습니다 — {ex.Message}");
        }
    }

    public void Dispose()
    {
        _player.Dispose();
        StopLoop();
        _loop = null;
        StopOver();
        foreach (var player in _over) player?.Close();
    }
}
