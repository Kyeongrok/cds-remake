using System.IO;
using System.Net.Http;
using System.Windows.Media.Imaging;

namespace CdsHelper.Game.Local.Helpers;

/// <summary>
/// 인물 초상화(80x96 도트)를 네 배(320x384) 키운 그림으로 건다 — 모드 「초상화 업스케일링」. 원본에 없는 것이다.
/// </summary>
/// <remarks>
/// 도시 그림 업스케일링(<see cref="CityUpscale"/>)과 같은 길이다. 원본 초상화 558장을 개발 PC 에서 Rebout Blend 4x 로 미리 키워
/// (<c>tools/upscale_portraits.py</c>) 고정 릴리즈 <c>portrait-hd</c> 에 그림 점 해시 이름의 PNG 와 그 목록(<c>index.txt</c>)을
/// 올려 두었다. 모드를 켜면 목록의 그림을 뒤에서 한 장씩 받는다(장마다 150KB 남짓, 다 합치면 90MB 쯤).
/// 이 PC 에서 키우지는 않는다 — 목록에 없는 얼굴(넣은 얼굴 · 바꾼 얼굴)은 도트 그대로다.
/// <para>
/// 받은 그림은 <c>%LOCALAPPDATA%\CdsHelper\portraithd</c> 에 둔다. 이름이 그림 점의 해시라 얼굴을 바꿔 넣으면 엉뚱한 그림이 붙지 않는다.
/// 거는 것은 <see cref="Portraits.TryGetBgra"/> 가 한다 — 줄이지 않고 넣은 얼굴(face-hd)이 있으면 그것이 먼저다.
/// </para>
/// </remarks>
public static class PortraitUpscale
{
    public static string Folder => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CdsHelper", "portraithd");

    /// <summary>미리 키운 그림을 올려 둔 고정 릴리즈.</summary>
    private const string HostedBase = "https://github.com/Kyeongrok/cds-remake/releases/download/portrait-hd/";

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(2) };
    private static readonly object Gate = new();
    private static Thread? _worker;
    private static volatile int _got, _total;
    private static volatile bool _running, _failed;

    /// <summary>모드가 켜져 있는지.</summary>
    public static bool Enabled => Settings.GameSettings.PortraitUpscale;

    /// <summary>지금 진행 — 모드 창에 띄운다. <c>Got</c> 은 받아 둔 장 수(이미 있던 것 포함).</summary>
    public static (int Got, int Total, bool Running, bool Failed) Progress => (_got, _total, _running, _failed);

    /// <summary>읽어 둔 그림 — 얼굴은 같은 몇 장이 자주 뜬다. 너무 쌓이면 비운다(한 장 0.5MB).</summary>
    private static readonly Dictionary<string, BitmapSource> Cache = [];

    /// <summary>그 얼굴 점에 맞는 키운 그림. 모드가 꺼졌거나 아직 없으면 null — 켜져 있으면 받기를 시작해 둔다.</summary>
    public static BitmapSource? TryLoad(uint[] bgra)
    {
        if (!Enabled) return null;
        Start();
        string key = CityUpscale.KeyOf(bgra, Portraits.Width, Portraits.Height);
        lock (Cache)
        {
            if (Cache.TryGetValue(key, out var kept)) return kept;
            string path = Path.Combine(Folder, key + ".png");
            if (!File.Exists(path)) return null;
            try
            {
                using var file = File.OpenRead(path);
                var image = new BitmapImage();
                image.BeginInit();
                image.CacheOption = BitmapCacheOption.OnLoad;
                image.StreamSource = file;
                image.EndInit();
                image.Freeze();
                if (Cache.Count >= 96) Cache.Clear();
                Cache[key] = image;
                return image;
            }
            catch (Exception ex) when (ex is IOException or NotSupportedException or UnauthorizedAccessException
                                           or ArgumentException or InvalidOperationException)
            {
                try { File.Delete(path); } catch { /* 다음에 다시 받는다 */ }
                return null;
            }
        }
    }

    /// <summary>
    /// 받지 않은 그림을 뒤에서 차례로 받는다. 이미 도는 중이면 그냥 둔다. 끄면 멈추고, 다시 켜면 남은 것부터 받는다.
    /// 한 번 실패하면(오프라인) <paramref name="retry"/> 로 부를 때(모드를 다시 켤 때)까지 다시 안 한다.
    /// </summary>
    public static void Start(bool retry = false)
    {
        lock (Gate)
        {
            if (_worker is { IsAlive: true } || !Enabled) return;
            if (_failed && !retry) return;
            if (!retry && _total > 0 && _got >= _total) return;
            _running = true;
            _failed = false;
            _worker = new Thread(Work) { IsBackground = true, Priority = ThreadPriority.BelowNormal, Name = "초상화 그림 받기" };
            _worker.Start();
        }
    }

    private static void Work()
    {
        try
        {
            string[] keys;
            string cached = Path.Combine(Folder, "index.txt");
            try
            {
                string text = Http.GetStringAsync(HostedBase + "index.txt").GetAwaiter().GetResult();
                Directory.CreateDirectory(Folder);
                File.WriteAllText(cached, text);
                keys = Split(text);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[PortraitUpscale] 목록을 못 받음: {ex.Message}");
                keys = File.Exists(cached) ? Split(File.ReadAllText(cached)) : [];
            }

            _total = keys.Length;
            _got = keys.Count(k => File.Exists(Path.Combine(Folder, k + ".png")));
            int misses = 0;
            foreach (string key in keys)
            {
                if (!Enabled) return;
                string path = Path.Combine(Folder, key + ".png"), temp = path + ".part";
                if (File.Exists(path)) continue;
                try
                {
                    var bytes = Http.GetByteArrayAsync(HostedBase + key + ".png").GetAwaiter().GetResult();
                    File.WriteAllBytes(temp, bytes);
                    File.Move(temp, path, overwrite: true);
                    _got++;
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[PortraitUpscale] {key} 를 못 받음: {ex.Message}");
                    try { if (File.Exists(temp)) File.Delete(temp); } catch { /* 다음에 덮어쓴다 */ }
                    // 줄줄이 실패하면(오프라인) 그만둔다 — 다음에 켜거나 앱을 다시 켜면 이어 받는다.
                    if (++misses >= 5) { _failed = true; return; }
                }
            }
            if (keys.Length == 0) _failed = true;
        }
        finally { _running = false; }
    }

    private static string[] Split(string text) =>
        text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
