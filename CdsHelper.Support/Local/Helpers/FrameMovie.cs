using System.IO;
using Windows.Media.Editing;
using Windows.Media.MediaProperties;
using Windows.Media.Transcoding;
using Windows.Storage;

namespace CdsHelper.Support.Local.Helpers;

/// <summary>
/// 같은 크기의 그림 여러 장을 차례로 이어 MP4 하나로 쓴다 — 편집기 「DHO 배 찍기」가 배를 한 바퀴 돌린 장면을 묶을 때 쓴다.
/// </summary>
/// <remarks>
/// <see cref="VideoShrinker"/> 처럼 윈도가 안고 있는 것(WinRT <see cref="MediaComposition"/>)만 쓴다 — ffmpeg 를 안 물린다.
/// 그림 한 장이 클립 하나이고 길이는 1/초당 장수다. 결과는 H.264 MP4, 소리는 없다.
/// H.264 는 가로세로가 짝수라야 하므로 그림도 짝수 크기로 넘긴다(원본 선체 동영상은 240x176).
/// </remarks>
public static class FrameMovie
{
    /// <summary>그림들을 이어 MP4 로 쓴다. 실패하면 던진다.</summary>
    /// <param name="frames">그림 파일 차례.</param>
    /// <param name="fps">초당 장수(원본 선체 동영상은 15).</param>
    /// <param name="progress">0 ~ 1. 그림을 싣는 데 절반, 굽는 데 절반이다.</param>
    public static async Task WriteAsync(IReadOnlyList<string> frames, int fps, int width, int height, string output,
                                        IProgress<double>? progress = null, CancellationToken cancel = default)
    {
        if (frames.Count == 0) throw new ArgumentException("그림이 없습니다", nameof(frames));

        var composition = new MediaComposition();
        var each = TimeSpan.FromTicks(TimeSpan.TicksPerSecond / Math.Max(1, fps));
        for (int i = 0; i < frames.Count; i++)
        {
            cancel.ThrowIfCancellationRequested();
            var file = await StorageFile.GetFileFromPathAsync(Path.GetFullPath(frames[i]));
            composition.Clips.Add(await MediaClip.CreateFromImageFileAsync(file, each));
            progress?.Report(0.5 * (i + 1) / frames.Count);
        }

        string full = Path.GetFullPath(output);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        var folder = await StorageFolder.GetFolderFromPathAsync(Path.GetDirectoryName(full)!);
        var target = await folder.CreateFileAsync(Path.GetFileName(full), CreationCollisionOption.ReplaceExisting);

        var profile = MediaEncodingProfile.CreateMp4(VideoEncodingQuality.Vga);
        profile.Video.Width = (uint)width;
        profile.Video.Height = (uint)height;
        profile.Video.FrameRate.Numerator = (uint)Math.Max(1, fps);
        profile.Video.FrameRate.Denominator = 1;
        profile.Video.Bitrate = 2_000_000;
        profile.Audio = null;

        var render = composition.RenderToFileAsync(target, MediaTrimmingPreference.Precise, profile);
        render.Progress = (_, value) => progress?.Report(0.5 + value / 200.0);   // 굽기는 0 ~ 100 으로 알린다
        var result = await render.AsTask(cancel);
        if (result != TranscodeFailureReason.None)
            throw new InvalidOperationException($"동영상을 굽지 못했습니다 ({result})");
    }
}
