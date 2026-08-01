namespace JavStt.Core;

public enum JobState
{
    Queued,
    Skipped,
    Running,
    Done,
    Failed,
    Cancelled,
}

/// <summary>One film in the queue. Mutable because the UI binds to it and watches it change.</summary>
public class TranscriptionJob
{
    public required string VideoPath { get; init; }
    public string Name => Path.GetFileName(VideoPath);
    public JobState State { get; set; } = JobState.Queued;
    public double Fraction { get; set; }
    public string Status { get; set; } = string.Empty;
    public double DurationSeconds { get; set; }
    public int CueCount { get; set; }

    /// <summary>Audio seconds the provider billed for this film.</summary>
    public double BilledSeconds { get; set; }
    public string? SubtitlePath { get; set; }
    public string? Error { get; set; }
}

/// <summary>
/// Runs a queue of films, one at a time.
///
/// ★Sequential on purpose. Every job uploads tens of megabytes and the provider meters audio
///   seconds per hour; running four at once buys nothing and turns one rate-limit rejection into
///   four failed files.
///
/// ★Two stops, and the difference is the whole point. <see cref="RequestSafeStop"/> lets the film
///   in flight finish, so no half-written subtitle is left beside a video - the state a later run
///   cannot tell from a complete one. <see cref="RequestStop"/> abandons it immediately, for when
///   the answer is already wrong. Subtitle Edit's own STT window has only the second kind, and the
///   sibling translator learned to offer both.
/// </summary>
public class BatchRunner
{
    private readonly JavSttSettings _settings;
    private readonly Action<string>? _log;
    private CancellationTokenSource? _hardStop;
    private volatile bool _safeStopRequested;

    /// <summary>
    /// Non-null while paused; completing it resumes.
    ///
    /// ★An awaitable gate, not a ManualResetEventSlim. A blocking wait looked equivalent and was
    ///   not: RunAsync runs synchronously on the caller's thread until its first real await, and
    ///   with a job that fails fast - no API key, a missing file - there is no such await, so the
    ///   block landed on the caller. In a test that deadlocked before RunAsync even returned a
    ///   Task; from the GUI it would have frozen the UI thread. Found by the pause test hanging.
    /// </summary>
    private volatile TaskCompletionSource? _pauseGate;

    public BatchRunner(JavSttSettings settings, Action<string>? log = null)
    {
        _settings = settings;
        _log = log;
    }

    public bool IsRunning { get; private set; }
    public bool SafeStopRequested => _safeStopRequested;
    public bool IsPaused => _pauseGate != null;

    /// <summary>Audio seconds the provider billed for this batch so far.</summary>
    public double BilledSeconds { get; private set; }

    public event Action<TranscriptionJob>? JobChanged;
    public event Action? Finished;

    public void RequestSafeStop() => _safeStopRequested = true;

    public void RequestStop()
    {
        _safeStopRequested = true;
        Resume();           // a paused batch must still be stoppable
        _hardStop?.Cancel();
    }

    /// <summary>
    /// Hold after the current film. ★Between films, like a safe stop - the difference is only that
    /// this one is resumable. Pausing mid-upload would either abandon work already paid for or hold
    /// a provider connection open for as long as the user felt like it.
    /// </summary>
    public void Pause()
        => Interlocked.CompareExchange(
            ref _pauseGate, new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously), null);

    public void Resume() => Interlocked.Exchange(ref _pauseGate, null)?.TrySetResult();

    /// <summary>
    /// Video extensions worth offering. ★mkv is in the list even though the library measured
    /// 22,296 mp4 and 0 mkv - the cost of accepting one is nothing, and the cost of silently
    /// ignoring a dropped file is a confused user.
    /// </summary>
    public static readonly string[] VideoExtensions =
        [".mp4", ".mkv", ".avi", ".wmv", ".mov", ".ts", ".m4v", ".mpg", ".mpeg", ".webm", ".flv"];

    public static bool IsVideo(string path)
        => VideoExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);

    public async Task RunAsync(IReadOnlyList<TranscriptionJob> jobs, CancellationToken cancellationToken = default)
    {
        IsRunning = true;
        _safeStopRequested = false;
        _hardStop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var runner = new TranscriptionRunner(_settings, _log);

        try
        {
            foreach (var job in jobs)
            {
                if (job.State is not (JobState.Queued or JobState.Failed))
                {
                    continue;
                }

                // ★Checked before starting, not during: a safe stop must never leave a job half
                //   done, and the only place that is guaranteed is between films.
                if (_safeStopRequested)
                {
                    break;
                }

                // Pause waits here, for the same reason and at the same point.
                if (_pauseGate is { } gate)
                {
                    JobChanged?.Invoke(job);
                    try
                    {
                        await gate.Task.WaitAsync(_hardStop.Token);
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }

                    if (_safeStopRequested)
                    {
                        break;
                    }
                }

                if (_settings.SkipExisting &&
                    File.Exists(SubtitleBuilder.OutputPathFor(job.VideoPath, _settings.OutputSuffix)))
                {
                    job.State = JobState.Skipped;
                    job.Status = "이미 있음";
                    job.Fraction = 1;
                    JobChanged?.Invoke(job);
                    continue;
                }

                job.State = JobState.Running;
                job.Fraction = 0;
                job.Status = "시작…";
                job.Error = null;
                JobChanged?.Invoke(job);

                try
                {
                    var result = await runner.RunAsync(
                        job.VideoPath,
                        new Progress<TranscriptionProgress>(p =>
                        {
                            job.Fraction = p.Fraction;
                            job.Status = p.Message;
                            JobChanged?.Invoke(job);
                        }),
                        _hardStop.Token);

                    job.State = JobState.Done;
                    job.Fraction = 1;
                    job.CueCount = result.CueCount;
                    job.DurationSeconds = result.DurationSeconds;
                    job.SubtitlePath = result.SubtitlePath;
                    job.BilledSeconds = result.BilledSeconds;
                    job.Status = $"{result.CueCount} 큐";
                    BilledSeconds += result.BilledSeconds;
                }
                catch (OperationCanceledException)
                {
                    job.State = JobState.Cancelled;
                    job.Status = "취소됨";
                    JobChanged?.Invoke(job);
                    break;
                }
                catch (Exception exception)
                {
                    // ★One film's failure must not end the batch. A rate-limit rejection or a
                    //   corrupt file is a property of that file, and the remaining hundreds are
                    //   still worth doing.
                    job.State = JobState.Failed;
                    job.Status = "실패";
                    job.Error = exception.Message;
                    _log?.Invoke($"{job.Name}: 실패 — {exception.Message}");
                }

                JobChanged?.Invoke(job);
            }
        }
        finally
        {
            IsRunning = false;
            _safeStopRequested = false;
            // ★Clear the gate too, or a runner stopped while paused stays IsPaused forever and the
            //   next batch holds on its first film with nothing to resume it.
            Resume();
            _hardStop?.Dispose();
            _hardStop = null;
            Finished?.Invoke();
        }
    }
}
