using Nikse.SubtitleEdit.Features.Video.SpeechToText.DashScope;

namespace JavStt.Core;

public enum TranscriptionStage
{
    Probing,
    ExtractingAudio,
    Uploading,
    Transcribing,
    Writing,
    Done,
}

public sealed record TranscriptionProgress(TranscriptionStage Stage, double Fraction, string Message);

public sealed record TranscriptionResult(
    string VideoPath,
    string SubtitlePath,
    int CueCount,
    int CharacterCount,
    double DurationSeconds,
    TimeSpan Elapsed,
    /// <summary>What the provider says it billed - detected speech, not the film's length.</summary>
    double BilledSeconds);

/// <summary>
/// One film in, one subtitle file out.
///
/// ★Deliberately linear, and that is the payoff of choosing Fun-ASR. Subtitle Edit's equivalent
///   path has to size chunks, run ffmpeg silencedetect, snap cut points to silence midpoints,
///   upload each piece, offset every timestamp back to absolute time and retry per chunk - all of
///   it forced by Whisper's 25 MB request cap. Fun-ASR takes 12 hours in one upload, so none of
///   that exists here: extract, upload, poll, write.
/// </summary>
public class TranscriptionRunner
{
    private readonly JavSttSettings _settings;
    private readonly Action<string>? _log;

    public TranscriptionRunner(JavSttSettings settings, Action<string>? log = null)
    {
        _settings = settings;
        _log = log;
    }

    public async Task<TranscriptionResult> RunAsync(
        string videoPath,
        IProgress<TranscriptionProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_settings.ApiKey))
        {
            throw new InvalidOperationException("No API key configured.");
        }

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var ffmpeg = _settings.ResolveFfmpeg();
        var name = Path.GetFileName(videoPath);

        progress?.Report(new TranscriptionProgress(TranscriptionStage.Probing, 0, "길이 확인 중…"));
        var duration = await MediaProbe.DurationSecondsAsync(
            MediaProbe.FfprobeBeside(ffmpeg), videoPath, cancellationToken);

        // ★A GUID temp name, not the film's. Two runs of the same film would otherwise collide, and
        //   ffmpeg's -y would let the second silently truncate the first mid-upload.
        var audioPath = Path.Combine(Path.GetTempPath(), "javstt", Guid.NewGuid().ToString("N") + ".mp3");

        try
        {
            progress?.Report(new TranscriptionProgress(TranscriptionStage.ExtractingAudio, 0, "오디오 추출 중…"));
            await AudioExtractor.ExtractAsync(
                ffmpeg, videoPath, audioPath, duration,
                new Progress<double>(f => progress?.Report(
                    new TranscriptionProgress(TranscriptionStage.ExtractingAudio, f, $"오디오 추출 {f:P0}"))),
                _log, cancellationToken);

            var megabytes = new FileInfo(audioPath).Length / 1024.0 / 1024.0;
            _log?.Invoke($"{name}: 오디오 {megabytes:N1} MB, {TimeSpan.FromSeconds(duration):hh\\:mm\\:ss} — 분할 없이 1회 업로드");

            var service = new DashScopeSttService(_settings.ToDashScope(_log));
            var response = await service.TranscribeAsync(
                audioPath,
                _settings.Language,
                new Progress<string>(m => progress?.Report(
                    new TranscriptionProgress(TranscriptionStage.Transcribing, 0, m))),
                null,
                cancellationToken);

            progress?.Report(new TranscriptionProgress(TranscriptionStage.Writing, 1, "자막 쓰는 중…"));
            var subtitle = SubtitleBuilder.Build(response.Segments);
            if (subtitle.Paragraphs.Count == 0)
            {
                throw new InvalidOperationException("전사 결과가 비어 있습니다 (자막을 만들 세그먼트 없음).");
            }

            var outputPath = SubtitleBuilder.Save(subtitle, videoPath, _settings.OutputSuffix);
            stopwatch.Stop();

            var billed = service.LastBilledSeconds;
            _log?.Invoke($"{name}: {subtitle.Paragraphs.Count} 큐, {response.Text.Length}자, " +
                         $"{stopwatch.Elapsed.TotalSeconds:N0}초, 청구 {TimeSpan.FromSeconds(billed):h\\:mm\\:ss}");
            progress?.Report(new TranscriptionProgress(TranscriptionStage.Done, 1, "완료"));

            return new TranscriptionResult(
                videoPath, outputPath, subtitle.Paragraphs.Count, response.Text.Length, duration, stopwatch.Elapsed, billed);
        }
        finally
        {
            // The extracted audio is 45 MB for a three-hour film; leaving it behind fills the disk
            // across a batch of thousands.
            try
            {
                if (File.Exists(audioPath))
                {
                    File.Delete(audioPath);
                }
            }
            catch (Exception)
            {
                // A locked temp file is not worth failing a completed transcription over.
            }
        }
    }
}
