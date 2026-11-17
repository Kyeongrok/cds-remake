using System.IO;
using System.Security.Cryptography;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CdsHelper.Support.Local.Helpers;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace CdsHelper.Game.Local.Helpers;

/// <summary>
/// 도시 그림(400x320 도트)을 업스케일 모델로 네 배 키워 쌓아 둔다 — 모드 「도시 그림 업스케일링」. 원본에 없는 것이다.
/// </summary>
/// <remarks>
/// <b>미리 구워 배포하지 않는다.</b> 100여 장을 키우면 PNG 로 수백 MB 라 업데이트가 무거워지고, 원본 그림을 키운 것을
/// 나눠 주는 꼴이 된다. 그래서 그 PC 의 CITYCG 를 그 자리에서 키운다. 모델은 선박 리마스터와 같은 Rebout Blend 4x 다
/// (<see cref="UpscaleModelAsset"/>, 없으면 처음에 받는다).
/// <para>
/// 한 장에 CPU 로 수십 초 걸려서 <b>뒤에서 한 장씩</b> 만든다 — 실 하나, 낮은 우선순위, 코어 절반. 입항한 도시는 줄 맨 앞으로
/// 당기고, 다 되면 그 창 그림을 갈아 끼운다(<see cref="Request"/>). 남은 도시는 게임을 켜 둔 동안 차례로 채운다(<see cref="Prefill"/>).
/// </para>
/// 만든 그림은 <c>%LOCALAPPDATA%\CdsHelper\cityhd</c> 에 그림 점의 해시 이름으로 둔다 — 커스텀 팩 · 도시 성장으로 그림이 바뀌면
/// 이름도 바뀐다. 비치는 점이 있는 그림(위에 얹는 건물)은 비침도 같은 모델로 키운다.
/// </remarks>
public static class CityUpscale
{
    /// <summary>모델 배율.</summary>
    public const int Factor = 4;

    public static string Folder => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CdsHelper", "cityhd");

    private static readonly object Gate = new();
    private static readonly LinkedList<Job> Queue = new();
    private static Thread? _worker, _filler;
    private static InferenceSession? _session;
    private static string _input = "";

    /// <summary>알릴 말 — 모델 받기 · 다 채움 따위. 지도 아래 띠로 낸다.</summary>
    public static Action<string>? Say { get; set; }

    /// <summary>모드가 켜져 있는지 — 꺼지면 줄에 남은 것을 버리고 멈춘다.</summary>
    public static Func<bool> Enabled { get; set; } = () => false;

    private static volatile int _checked, _total, _made;
    private static volatile bool _filling, _busy, _downloading, _failed;

    /// <summary>
    /// 지금 진행 — 모드 창에 띄운다. <c>Checked</c>/<c>Total</c> 은 채우기가 훑은 도시 그림 수(이미 있던 것 포함),
    /// <c>Made</c> 는 이번에 새로 키운 장 수, <c>Busy</c> 는 일꾼이 지금 한 장을 키우는 중인지.
    /// </summary>
    public static (int Checked, int Total, int Made, bool Filling, bool Busy, bool Downloading, bool Failed) Progress
        => (_checked, _total, _made, _filling, _busy, _downloading, _failed);

    private sealed record Job(string Key, uint[] Bgra, int Width, int Height, List<Action<BitmapSource>> Done);

    /// <summary>그림 점의 이름 — 같은 그림이면 같은 이름이다.</summary>
    public static string KeyOf(uint[] bgra, int width, int height)
    {
        var bytes = new byte[bgra.Length * 4];
        Buffer.BlockCopy(bgra, 0, bytes, 0, bytes.Length);
        return $"{width}x{height}-{Convert.ToHexString(SHA256.HashData(bytes))[..20]}";
    }

    private static string PathOf(string key) => Path.Combine(Folder, key + ".png");

    /// <summary>이미 키워 둔 그림. 없으면 null.</summary>
    public static BitmapSource? TryLoad(uint[] bgra, int width, int height) => TryLoad(KeyOf(bgra, width, height));

    private static BitmapSource? TryLoad(string key)
    {
        // 붙들어 두지 않는다 — 한 장이 8MB 라 100여 장을 다 쥐면 메모리가 1GB 가깝다. 입항할 때 파일에서 읽는다.
        string path = PathOf(key);
        if (!File.Exists(path)) return null;
        try
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.UriSource = new Uri(path);
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch (Exception ex) when (ex is IOException or NotSupportedException or UnauthorizedAccessException)
        {
            try { File.Delete(path); } catch { /* 다음에 다시 만든다 */ }
            return null;
        }
    }

    /// <summary>
    /// 이 그림을 <b>먼저</b> 키운다 — 다 되면 <paramref name="done"/> 을 일꾼 실에서 부른다(받는 쪽이 UI 실로 넘긴다).
    /// 이미 있으면 곧바로 부른다.
    /// </summary>
    public static void Request(uint[] bgra, int width, int height, Action<BitmapSource> done)
    {
        string key = KeyOf(bgra, width, height);
        if (TryLoad(key) is { } ready) { done(ready); return; }
        Enqueue(key, bgra, width, height, done, front: true);
    }

    /// <summary>남은 그림들을 뒤에서 차례로 채운다. 그림은 일꾼 실이 하나씩 달라고 한다(UI 실에서 풀어야 하면 받는 쪽이 넘긴다).</summary>
    public static void Prefill(int count, Func<int, uint[]?> picture, int width, int height)
    {
        lock (Gate) if (_filler is { IsAlive: true }) return;
        _total = count;
        _checked = 0;
        _made = 0;
        _filling = true;
        var filler = new Thread(() =>
        {
            try
            {
                for (int i = 0; i < count && Enabled(); i++, _checked = i)
                {
                    uint[]? px;
                    try { px = picture(i); }
                    catch { continue; }
                    if (px == null) continue;
                    string key = KeyOf(px, width, height);
                    if (File.Exists(PathOf(key))) continue;
                    using var finished = new ManualResetEventSlim();
                    Enqueue(key, px, width, height, _ => finished.Set(), front: false);
                    // 하나씩 넣는다 — 줄에 다 쌓아 두면 그림 점이 한꺼번에 수십 MB 잡힌다.
                    while (!finished.Wait(2000))
                        if (!Enabled()) return;
                    _made++;
                }
                if (Enabled()) _checked = count;
                if (_made > 0 && Enabled()) Say?.Invoke($"도시 그림 업스케일링을 마쳤습니다({_made}장).");
            }
            finally { _filling = false; }
        })
        { IsBackground = true, Priority = ThreadPriority.Lowest, Name = "도시 그림 채우기" };
        lock (Gate) _filler = filler;
        filler.Start();
    }

    private static void Enqueue(string key, uint[] bgra, int width, int height, Action<BitmapSource> done, bool front)
    {
        lock (Gate)
        {
            var same = Queue.FirstOrDefault(j => j.Key == key);
            if (same != null)
            {
                same.Done.Add(done);
                if (front) { Queue.Remove(same); Queue.AddFirst(same); }
            }
            else
            {
                var job = new Job(key, bgra, width, height, [done]);
                if (front) Queue.AddFirst(job); else Queue.AddLast(job);
            }

            if (_worker is { IsAlive: true }) { Monitor.PulseAll(Gate); return; }
            _worker = new Thread(Work) { IsBackground = true, Priority = ThreadPriority.BelowNormal, Name = "도시 그림 업스케일" };
            _worker.Start();
        }
    }

    private static void Work()
    {
        while (true)
        {
            Job job;
            lock (Gate)
            {
                while (Queue.Count == 0)
                    if (!Monitor.Wait(Gate, TimeSpan.FromSeconds(30))) { _worker = null; return; }
                if (!Enabled()) { Queue.Clear(); continue; }
                job = Queue.First!.Value;
                Queue.RemoveFirst();
            }

            _busy = true;
            try
            {
                if (!EnsureSession()) { lock (Gate) Queue.Clear(); continue; }
                var made = Upscale(job.Bgra, job.Width, job.Height);
                Directory.CreateDirectory(Folder);
                string path = PathOf(job.Key), temp = path + ".tmp";
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(made));
                using (var stream = File.Create(temp)) encoder.Save(stream);
                File.Move(temp, path, overwrite: true);
                foreach (var done in job.Done) done(made);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[CityUpscale] {ex.GetType().Name}: {ex.Message}");
            }
            finally { _busy = false; }
        }
    }

    /// <summary>모델을 연다 — 없으면 받는다(64MB). 못 열면 false.</summary>
    private static bool EnsureSession()
    {
        if (_session != null) return true;
        try
        {
            if (!UpscaleModelAsset.Exists)
            {
                Say?.Invoke("도시 그림 업스케일링 모델(64MB)을 받는 중입니다...");
                _downloading = true;
                try { UpscaleModelAsset.DownloadAsync().GetAwaiter().GetResult(); }
                finally { _downloading = false; }
            }
            // 코어 절반만 쓴다 — 게임이 끊기지 않게.
            var options = new SessionOptions
            {
                IntraOpNumThreads = Math.Max(1, Environment.ProcessorCount / 2),
                InterOpNumThreads = 1,
            };
            _session = new InferenceSession(UpscaleModelAsset.CachePath, options);
            _input = _session.InputMetadata.Keys.First();
            _failed = false;
            return true;
        }
        catch (Exception ex)
        {
            _failed = true;
            Say?.Invoke($"도시 그림 업스케일링 모델을 열지 못했습니다 — {ex.Message}");
            return false;
        }
    }

    /// <summary>BGRA 한 장을 네 배로 — 비치는 점이 있으면 비침도 같은 모델로 키운다. 돌려주는 것은 얼린 Pbgra32 다.</summary>
    private static BitmapSource Upscale(uint[] src, int w, int h)
    {
        bool alpha = src.Any(p => p >> 24 != 0xFF);
        var px = alpha ? Bleed(src, w, h) : src;

        var color = new DenseTensor<float>([1, 3, h, w]);
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                uint p = px[y * w + x];
                color[0, 0, y, x] = ((p >> 16) & 0xFF) / 255f;
                color[0, 1, y, x] = ((p >> 8) & 0xFF) / 255f;
                color[0, 2, y, x] = (p & 0xFF) / 255f;
            }
        var rgb = Run(color, out int bw, out int bh);

        float[]? mask = null;
        if (alpha)
        {
            var a = new DenseTensor<float>([1, 3, h, w]);
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                    a[0, 0, y, x] = a[0, 1, y, x] = a[0, 2, y, x] = (src[y * w + x] >> 24) / 255f;
            mask = Run(a, out _, out _);
        }

        int plane = bw * bh;
        var made = new uint[plane];
        for (int i = 0; i < plane; i++)
        {
            float a = 1f;
            if (mask != null)
            {
                // 흑백으로 넣었으니 세 채널이 거의 같다. 가운데를 가파르게 세워 흐린 테를 좁힌다(선박 리마스터와 같다).
                a = (mask[i] + mask[plane + i] + mask[2 * plane + i]) / 3f;
                a = Math.Clamp((a - 0.25f) / 0.5f, 0f, 1f);
                if (a <= 0f) continue;
            }
            // 알파를 곱해 둔다(Pbgra32) — 늘려 그릴 때 가장자리에 검은 테가 끼지 않는다.
            uint r = Byte(rgb[i] * a), g = Byte(rgb[plane + i] * a), b = Byte(rgb[2 * plane + i] * a);
            made[i] = (Byte(a) << 24) | (r << 16) | (g << 8) | b;
        }

        var bmp = BitmapSource.Create(bw, bh, 96, 96, PixelFormats.Pbgra32, null, made, bw * 4);
        bmp.Freeze();
        return bmp;
    }

    private static float[] Run(DenseTensor<float> tensor, out int width, out int height)
    {
        using var results = _session!.Run([NamedOnnxValue.CreateFromTensor(_input, tensor)]);
        var output = results.First().AsTensor<float>();
        width = output.Dimensions[3];
        height = output.Dimensions[2];
        return output.ToArray();
    }

    private static uint Byte(float v) => (uint)Math.Clamp((int)MathF.Round(v * 255f), 0, 255);

    /// <summary>비치는 자리에 둘레 색을 번지게 한다 — 안 그러면 키울 때 가장자리에 검은 테가 낀다.</summary>
    private static uint[] Bleed(uint[] src, int w, int h)
    {
        var px = (uint[])src.Clone();
        var filled = new bool[px.Length];
        for (int i = 0; i < px.Length; i++) filled[i] = px[i] >> 24 != 0;

        for (int pass = 0; pass < 6; pass++)
        {
            var next = (bool[])filled.Clone();
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    int i = y * w + x;
                    if (filled[i]) continue;
                    int r = 0, g = 0, b = 0, n = 0;
                    for (int dy = -1; dy <= 1; dy++)
                        for (int dx = -1; dx <= 1; dx++)
                        {
                            int nx = x + dx, ny = y + dy;
                            if (nx < 0 || ny < 0 || nx >= w || ny >= h || !filled[ny * w + nx]) continue;
                            uint p = px[ny * w + nx];
                            r += (int)((p >> 16) & 0xFF); g += (int)((p >> 8) & 0xFF); b += (int)(p & 0xFF);
                            n++;
                        }
                    if (n == 0) continue;
                    px[i] = (uint)((r / n) << 16 | (g / n) << 8 | (b / n));
                    next[i] = true;
                }
            filled = next;
        }
        return px;
    }
}
