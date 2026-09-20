using System.IO;
using Windows.Foundation;
using Windows.Media.MediaProperties;
using Windows.Media.Transcoding;
using Windows.Storage;

namespace CdsHelper.Support.Local.Helpers;

/// <summary>
/// 동영상을 작게 줄이고, 시키면 소리를 빼고, 비트레이트를 낮춰 MP4 로 다시 써 주는 도구.
/// </summary>
/// <remarks>
/// 윈도가 이미 안고 있는 Media Foundation 변환기(<see cref="MediaTranscoder"/>)로만 한다 —
/// ffmpeg 같은 바깥 실행 파일을 물리지 않는다. 그래서 열 수 있는 동영상은 윈도가 풀 줄 아는
/// 것뿐이다(MP4·MOV·WMV·대개의 AVI). 결과는 늘 H.264 + AAC 를 담은 MP4 다.
/// 소리는 트랙째로 뺀다 — 배경음악만 골라 지우는 게 아니라 효과음까지 같이 빠진다.
/// 어느 방식으로 줄이든 가로세로 비는 지키고, 원본보다 키우지는 않는다.
/// </remarks>
public static class VideoShrinker
{
    /// <summary>얼마나 줄일지 정하는 방식.</summary>
    public enum SizeMode
    {
        /// <summary>크기는 그대로 두고 압축만 한다.</summary>
        Keep,

        /// <summary>가로를 정해진 픽셀에 맞춘다.</summary>
        Width,

        /// <summary>세로를 정해진 픽셀에 맞춘다.</summary>
        Height,

        /// <summary>긴 변을 정해진 픽셀에 맞춘다.</summary>
        LongestSide,

        /// <summary>원래 크기의 몇 퍼센트로 줄인다.</summary>
        Percent,
    }

    /// <summary>얼마나 세게 압축할지.</summary>
    public enum Quality
    {
        High,
        Medium,
        Small,

        /// <summary><see cref="Options.BitrateKbps"/> 를 그대로 쓴다.</summary>
        Bitrate,
    }

    /// <summary>어디에 쓸지. 결과가 늘 MP4 라 원본 덮어쓰기는 두지 않는다.</summary>
    public enum Destination
    {
        NextToSource,
        Folder,
    }

    public static readonly string[] Extensions =
        [".mp4", ".m4v", ".mov", ".wmv", ".avi", ".mkv", ".3gp"];

    public const string FileFilter =
        "동영상 파일|*.mp4;*.m4v;*.mov;*.wmv;*.avi;*.mkv;*.3gp|모든 파일|*.*";

    /// <summary>소리를 남길 때 쓰는 AAC 비트레이트.</summary>
    public const uint AudioBitrate = 128_000;

    /// <summary>이보다 낮게는 안 내린다 — 더 내리면 뭉개져서 알아볼 수가 없다.</summary>
    private const uint MinVideoBitrate = 150_000;

    /// <summary>진행이 이만큼(ms) 안 움직이면 변환기가 멈춘 것으로 본다.</summary>
    private const int StallMs = 30_000;

    public sealed class Options
    {
        public SizeMode Mode { get; init; } = SizeMode.Keep;

        /// <summary>맞출 픽셀. <see cref="SizeMode.Width"/>·<see cref="SizeMode.Height"/>·<see cref="SizeMode.LongestSide"/> 일 때 본다.</summary>
        public int Pixels { get; init; } = 1280;

        /// <summary>원래 크기 대비 퍼센트. <see cref="SizeMode.Percent"/> 일 때만 본다.</summary>
        public double Percent { get; init; } = 50;

        public bool RemoveAudio { get; init; }

        public Quality Quality { get; init; } = Quality.Medium;

        /// <summary><see cref="Quality.Bitrate"/> 일 때 쓰는 영상 비트레이트(kbps).</summary>
        public int BitrateKbps { get; init; } = 1500;

        public Destination Where { get; init; } = Destination.NextToSource;

        public string? Folder { get; init; }

        /// <summary><see cref="Destination.NextToSource"/> 일 때 이름 뒤에 붙일 꼬리말.</summary>
        public string Suffix { get; init; } = "_small";
    }

    /// <summary>원본 동영상에서 읽어 둔 값.</summary>
    public sealed record Probe(
        int Width,
        int Height,
        TimeSpan Duration,
        uint VideoBitrate,
        uint FrameRateNumerator,
        uint FrameRateDenominator,
        bool HasAudio,
        uint AudioSampleRate,
        uint AudioChannels,
        long Bytes)
    {
        public double FrameRate => FrameRateDenominator > 0 ? (double)FrameRateNumerator / FrameRateDenominator : 0;
    }

    public sealed class Result
    {
        public required string SourcePath { get; init; }
        public long SourceBytes { get; set; }
        public int OutputWidth { get; set; }
        public int OutputHeight { get; set; }
        public uint VideoBitrate { get; set; }
        public bool AudioRemoved { get; set; }
        public string? OutputPath { get; set; }
        public long OutputBytes { get; set; }
        public TimeSpan Elapsed { get; set; }
        public bool Canceled { get; set; }
        public string? Error { get; set; }

        /// <summary>변환기가 손잡이를 놓지 않아 못 지운 임시 파일. 앱을 닫으면 지울 수 있다.</summary>
        public string? LeftoverPath { get; set; }

        /// <summary>용량이 얼마나 줄었는지(0~1). 오히려 늘었으면 음수다.</summary>
        public double Saved => SourceBytes > 0 && OutputBytes > 0
            ? 1.0 - (double)OutputBytes / SourceBytes
            : 0;
    }

    public static bool IsSupported(string path) =>
        Extensions.Contains(Path.GetExtension(path).ToLowerInvariant());

    /// <summary>줄이고 나면 몇 픽셀이 될지 셈한다.</summary>
    /// <remarks>H.264 인코더는 홀수 크기를 못 받는다 — 둘 다 짝수로 내려 맞춘다.</remarks>
    public static (int Width, int Height) TargetSize(int sw, int sh, Options options)
    {
        if (sw <= 0 || sh <= 0) return (0, 0);

        double scale = options.Mode switch
        {
            SizeMode.Width => (double)options.Pixels / sw,
            SizeMode.Height => (double)options.Pixels / sh,
            SizeMode.LongestSide => (double)options.Pixels / Math.Max(sw, sh),
            SizeMode.Percent => Math.Clamp(options.Percent, 1, 100) / 100.0,
            _ => 1,
        };

        // 줄이는 도구다 — 원본보다 키우지는 않는다.
        scale = Math.Min(1, scale);

        int width = Even((int)Math.Round(sw * scale));
        int height = Even((int)Math.Round(sh * scale));
        return (width, height);

        static int Even(int value) => Math.Max(2, value & ~1);
    }

    /// <summary>
    /// 영상에 줄 비트레이트(bps).
    /// </summary>
    /// <remarks>
    /// 화질 단계는 한 프레임 한 픽셀에 몇 비트를 줄지로 잡는다 — 크기와 프레임 수가 달라도 같은
    /// 단계면 비슷한 화질이 나온다. 단계로 고른 값은 원본 비트레이트(줄인 넓이만큼 깎은 것)를
    /// 넘기지 않는다. 넘기면 압축한다면서 오히려 커진다. 직접 적은 값은 그대로 쓴다.
    /// </remarks>
    public static uint TargetVideoBitrate(Probe probe, int width, int height, Options options)
    {
        if (options.Quality == Quality.Bitrate)
            return Math.Max(MinVideoBitrate, (uint)Math.Max(1, options.BitrateKbps) * 1000);

        double bitsPerPixel = options.Quality switch
        {
            Quality.High => 0.10,
            Quality.Small => 0.035,
            _ => 0.06,
        };

        double fps = probe.FrameRate > 0 ? probe.FrameRate : 30;
        double bitrate = width * height * fps * bitsPerPixel;

        if (probe.VideoBitrate > 0 && probe.Width > 0 && probe.Height > 0)
        {
            double area = (double)width * height / ((double)probe.Width * probe.Height);
            bitrate = Math.Min(bitrate, probe.VideoBitrate * area);
        }

        return Math.Max(MinVideoBitrate, (uint)bitrate);
    }

    /// <summary>다 쓰고 나면 대강 몇 바이트가 될지 어림한다. MP4 머리말 몫으로 조금 얹는다.</summary>
    public static long EstimateBytes(Probe probe, Options options)
    {
        var (width, height) = TargetSize(probe.Width, probe.Height, options);
        double bits = TargetVideoBitrate(probe, width, height, options);
        if (KeepsAudio(probe, options)) bits += AudioBitrate;

        return (long)(bits * probe.Duration.TotalSeconds / 8 * 1.02);
    }

    public static bool KeepsAudio(Probe probe, Options options) => probe.HasAudio && !options.RemoveAudio;

    /// <summary>원본 동영상의 크기·길이·프레임 수·소리 유무를 읽는다.</summary>
    public static async Task<Probe> ProbeAsync(string path)
    {
        var full = Path.GetFullPath(path);
        var file = await StorageFile.GetFileFromPathAsync(full);
        var properties = await file.Properties.GetVideoPropertiesAsync();

        // 프레임 수와 소리 트랙은 탐색기 속성에 없다 — 프로필을 한 번 떠 본다.
        // 윈도가 못 푸는 동영상이면 여기서 던지는데, 그래도 크기만큼은 속성에서 건진다.
        MediaEncodingProfile? profile = null;
        try
        {
            profile = await MediaEncodingProfile.CreateFromFileAsync(file);
        }
        catch
        {
            // 아래에서 속성 값으로 메운다
        }

        var video = profile?.Video;
        var audio = profile?.Audio;

        int width = (int)(video?.Width > 0 ? video.Width : properties.Width);
        int height = (int)(video?.Height > 0 ? video.Height : properties.Height);
        if (width <= 0 || height <= 0)
            throw new InvalidDataException("동영상 크기를 읽지 못했습니다 — 윈도가 풀 줄 모르는 형식일 수 있습니다");

        return new Probe(
            width,
            height,
            properties.Duration,
            video?.Bitrate > 0 ? video.Bitrate : properties.Bitrate,
            video?.FrameRate?.Numerator ?? 0,
            video?.FrameRate?.Denominator ?? 0,
            audio is { ChannelCount: > 0 },
            audio?.SampleRate ?? 0,
            audio?.ChannelCount ?? 0,
            new FileInfo(full).Length);
    }

    /// <summary>
    /// 동영상 하나를 줄여 MP4 로 쓴다. 실패해도 던지지 않고 <see cref="Result.Error"/> 에 담아 준다.
    /// </summary>
    /// <param name="progress">0~1 로 얼마나 했는지 알린다.</param>
    public static async Task<Result> ShrinkAsync(
        string path, Options options, IProgress<double>? progress, CancellationToken cancel)
    {
        var result = new Result { SourcePath = path };
        var started = DateTime.UtcNow;
        string? temp = null;
        FileStream? input = null, output = null;
        bool abandoned = false;

        try
        {
            var full = Path.GetFullPath(path);
            if (!File.Exists(full))
            {
                result.Error = "파일이 없습니다";
                return result;
            }

            var probe = await ProbeAsync(full);
            result.SourceBytes = probe.Bytes;

            var (width, height) = TargetSize(probe.Width, probe.Height, options);
            result.OutputWidth = width;
            result.OutputHeight = height;
            result.VideoBitrate = TargetVideoBitrate(probe, width, height, options);
            result.AudioRemoved = probe.HasAudio && options.RemoveAudio;

            string outPath = BuildOutputPath(full, options);
            if (SamePath(outPath, full))
            {
                result.Error = "결과가 원본 자리와 같습니다 — 꼬리말을 적거나 다른 폴더를 고르세요";
                return result;
            }

            string dir = Path.GetDirectoryName(outPath) ?? ".";
            Directory.CreateDirectory(dir);

            // 다 쓴 뒤에 자리를 바꾼다 — 쓰다 말면 반쪽짜리 파일이 남는다.
            temp = Path.Combine(dir, Path.GetFileNameWithoutExtension(outPath) + ".shrink.tmp.mp4");

            // StorageFile 로 열면 윈도가 oplock 을 걸어 두는데, 미리 보기 플레이어 같은 딴 손잡이가
            // 원본을 건드리면 그게 깨지면서 변환이 「oplock 이 연결된 핸들이 닫혔습니다」로 죽는다.
            // 보통 FileStream 에는 oplock 이 없다 — 스트림으로 넘긴다.
            input = new FileStream(full, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            output = new FileStream(temp, FileMode.Create, FileAccess.ReadWrite, FileShare.Read);

            var transcoder = new MediaTranscoder { HardwareAccelerationEnabled = true };
            var prepared = await transcoder.PrepareStreamTranscodeAsync(
                input.AsRandomAccessStream(), output.AsRandomAccessStream(), BuildProfile(probe, result, options));
            if (!prepared.CanTranscode)
            {
                result.Error = prepared.FailureReason switch
                {
                    TranscodeFailureReason.CodecNotFound => "이 동영상을 풀거나 쓸 코덱이 윈도에 없습니다",
                    TranscodeFailureReason.InvalidProfile => "고른 설정이 이 동영상과 안 맞습니다",
                    _ => "알 수 없는 까닭으로 바꾸지 못합니다",
                };
                return result;
            }

            // 윈도가 못 푸는 동영상(H.264 4:4:4 같은 것)은 준비까지는 되는데 변환이 아무 말 없이
            // 멈춰 버린다. Cancel() 마저 안 돌아온다 — 그래서 끝나기를 붙들고 기다리지 않고,
            // 진행이 한동안 안 움직이거나 그만두라 하면 버리고 나온다.
            var operation = prepared.TranscodeAsync();
            long lastTick = Environment.TickCount64;
            operation.Progress = (_, value) =>
            {
                Interlocked.Exchange(ref lastTick, Environment.TickCount64);
                progress?.Report(value / 100.0);   // 변환기는 0~100 으로 알린다
            };

            var transcoding = operation.AsTask();
            _ = transcoding.ContinueWith(t => _ = t.Exception, TaskContinuationOptions.OnlyOnFaulted);

            while (await Task.WhenAny(transcoding, Task.Delay(500)) != transcoding)
            {
                if (cancel.IsCancellationRequested)
                {
                    abandoned = !await CancelQuietly(operation, transcoding);
                    result.Canceled = true;
                    return result;
                }

                if (Environment.TickCount64 - Interlocked.Read(ref lastTick) > StallMs)
                {
                    abandoned = !await CancelQuietly(operation, transcoding);
                    result.Error = $"{StallMs / 1000}초 동안 진행이 없어 그만두었습니다 — 윈도가 이 동영상을 못 푸는 것 같습니다";
                    return result;
                }
            }

            await transcoding;

            // 다 쓴 스트림을 닫아야 자리를 바꿀 수 있다.
            output.Dispose();
            output = null;
            File.Move(temp, outPath, overwrite: true);
            temp = null;

            result.OutputPath = outPath;
            result.OutputBytes = new FileInfo(outPath).Length;
            return result;
        }
        catch (OperationCanceledException)
        {
            result.Canceled = true;
            return result;
        }
        catch (Exception ex)
        {
            // WinRT 예외는 같은 문장을 빈 줄 사이에 두 번 싣고 온다 — 첫 줄만 쓴다.
            result.Error = ex.Message
                .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .FirstOrDefault() ?? ex.GetType().Name;
            return result;
        }
        finally
        {
            result.Elapsed = DateTime.UtcNow - started;

            // 멈춘 변환기가 아직 쥐고 있는 스트림은 닫지 않는다 — 닫힌 스트림을 두드리게 된다.
            // 그러면 임시 파일도 못 지우니 남은 자리를 알려 준다.
            if (!abandoned)
            {
                output?.Dispose();
                input?.Dispose();
            }

            if (temp != null && !DeleteQuietly(temp)) result.LeftoverPath = temp;
        }
    }

    // ── 속살 ────────────────────────────────────────────────────────────────

    private static MediaEncodingProfile BuildProfile(Probe probe, Result result, Options options)
    {
        var profile = MediaEncodingProfile.CreateMp4(VideoEncodingQuality.HD720p);
        profile.Video.Width = (uint)result.OutputWidth;
        profile.Video.Height = (uint)result.OutputHeight;
        profile.Video.Bitrate = result.VideoBitrate;

        // 프레임 수는 원본을 따른다. 못 읽었으면 틀의 30 을 그대로 둔다.
        if (probe.FrameRateNumerator > 0 && probe.FrameRateDenominator > 0)
        {
            profile.Video.FrameRate.Numerator = probe.FrameRateNumerator;
            profile.Video.FrameRate.Denominator = probe.FrameRateDenominator;
        }

        // 소리 트랙을 비워 두면 영상만 담긴다.
        if (KeepsAudio(probe, options))
        {
            // AAC 인코더는 44.1k·48k 와 두 채널까지만 받는다.
            uint sampleRate = probe.AudioSampleRate is 44100 or 48000 ? probe.AudioSampleRate : 44100;
            uint channels = Math.Clamp(probe.AudioChannels, 1u, 2u);
            profile.Audio = AudioEncodingProperties.CreateAac(sampleRate, channels, AudioBitrate);
        }
        else
        {
            profile.Audio = null;
        }

        return profile;
    }

    private static string BuildOutputPath(string path, Options options)
    {
        string name = Path.GetFileNameWithoutExtension(path);
        string dir = options.Where == Destination.Folder && !string.IsNullOrWhiteSpace(options.Folder)
            ? options.Folder
            : Path.GetDirectoryName(path) ?? ".";

        string suffix = options.Where == Destination.NextToSource ? options.Suffix : "";
        string candidate = Path.Combine(dir, name + suffix + ".mp4");

        // 폴더를 따로 정했는데 하필 원본과 같은 자리라면 덮지 않게 꼬리말을 붙인다.
        if (options.Where == Destination.Folder && SamePath(candidate, path))
            candidate = Path.Combine(dir, name + options.Suffix + ".mp4");

        return candidate;
    }

    private static bool SamePath(string a, string b)
    {
        try
        {
            return string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// 변환을 그만두게 한다.
    /// </summary>
    /// <remarks>
    /// 멈춘 변환기에서는 <c>Cancel()</c> 이 영영 안 돌아오고, 그 뒤로는 <c>Status</c> 를 읽어도 막힌다.
    /// 딴 실에 맡기고 잠깐만 기다린 뒤 다시는 건드리지 않는다.
    /// </remarks>
    /// <returns>변환이 정말 멈췄으면 true. 안 멈췄으면 false — 스트림을 닫으면 안 된다.</returns>
    private static async Task<bool> CancelQuietly(IAsyncActionWithProgress<double> operation, Task transcoding)
    {
        _ = Task.Run(() =>
        {
            try { operation.Cancel(); }
            catch { /* 이미 끝났으면 던질 수 있다 — 넘긴다 */ }
        });
        return await Task.WhenAny(transcoding, Task.Delay(3000)) == transcoding;
    }

    /// <returns>지웠거나 원래 없었으면 true. 변환기가 아직 쥐고 있어 못 지웠으면 false.</returns>
    private static bool DeleteQuietly(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
