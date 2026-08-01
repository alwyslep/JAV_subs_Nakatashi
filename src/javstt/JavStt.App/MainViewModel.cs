using System.Collections.ObjectModel;
using System.Collections.Specialized;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using JavStt.Core;

namespace JavStt.App;

/// <summary>A queue row, wrapped so the list updates as the batch moves.</summary>
public partial class JobRow : ObservableObject
{
    public required TranscriptionJob Job { get; init; }

    public string Name => Job.Name;

    [ObservableProperty] private string _status = "대기중";
    [ObservableProperty] private double _fraction;
    [ObservableProperty] private string _duration = string.Empty;
    [ObservableProperty] private bool _isFailed;
    [ObservableProperty] private bool _isDone;

    public void Refresh()
    {
        Status = Job.State switch
        {
            JobState.Queued => "대기중",
            JobState.Skipped => "이미 있음",
            JobState.Done => Job.Status,
            JobState.Failed => "실패 — " + (Job.Error ?? string.Empty),
            JobState.Cancelled => "취소됨",
            _ => Job.Status,
        };
        Fraction = Job.Fraction;
        IsFailed = Job.State == JobState.Failed;
        IsDone = Job.State is JobState.Done or JobState.Skipped;
        if (Job.DurationSeconds > 0)
        {
            Duration = TimeSpan.FromSeconds(Job.DurationSeconds).ToString(@"h\:mm\:ss");
        }
    }
}

public partial class MainViewModel : ObservableObject
{
    private readonly JavSttSettings _settings;
    private BatchRunner? _runner;

    /// <summary>
    /// Every log line ever emitted this session, so the "문제만" filter can be applied after the
    /// fact rather than deciding at write time what will matter.
    /// </summary>
    private readonly List<string> _allLog = [];

    public MainViewModel(JavSttSettings settings)
    {
        _settings = settings;
        ApiKey = settings.ApiKey;
        Model = settings.Model;
        Language = settings.Language;
        Region = settings.Region;
        SkipExisting = settings.SkipExisting;
        Jobs.CollectionChanged += OnJobsChanged;
    }

    public ObservableCollection<JobRow> Jobs { get; } = [];
    public ObservableCollection<string> Log { get; } = [];

    public string[] Models { get; } = ["fun-asr", "fun-asr-mtl", "qwen3-asr-flash-filetrans"];
    public string[] Regions { get; } = ["international", "china"];

    [ObservableProperty] private string _apiKey = string.Empty;
    [ObservableProperty] private string _model = "fun-asr";
    [ObservableProperty] private string _language = "ja";
    [ObservableProperty] private string _region = "international";
    [ObservableProperty] private bool _skipExisting = true;
    [ObservableProperty] private bool _optionsVisible;
    [ObservableProperty] private bool _keyVisible;
    [ObservableProperty] private bool _problemsOnly;
    [ObservableProperty] private string _runState = "대기";
    [ObservableProperty] private bool _isRunning;
    [ObservableProperty] private bool _safeStopPending;

    public string KeyStatus => string.IsNullOrWhiteSpace(ApiKey) ? "키 없음" : "키 있음";
    public bool HasJobs => Jobs.Count > 0;
    public bool CanStart => !IsRunning && HasJobs && !string.IsNullOrWhiteSpace(ApiKey);

    partial void OnApiKeyChanged(string value)
    {
        OnPropertyChanged(nameof(KeyStatus));
        OnPropertyChanged(nameof(CanStart));
    }

    partial void OnIsRunningChanged(bool value) => OnPropertyChanged(nameof(CanStart));

    private void OnJobsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        OnPropertyChanged(nameof(HasJobs));
        OnPropertyChanged(nameof(CanStart));
    }

    public void AddPaths(IEnumerable<string> paths)
    {
        foreach (var path in paths)
        {
            foreach (var video in Expand(path))
            {
                if (Jobs.Any(j => string.Equals(j.Job.VideoPath, video, StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                var row = new JobRow { Job = new TranscriptionJob { VideoPath = video } };
                row.Refresh();
                Jobs.Add(row);
            }
        }

        WriteLog($"큐에 {Jobs.Count}개 파일");
    }

    /// <summary>A folder contributes every video under it; a file contributes itself.</summary>
    private static IEnumerable<string> Expand(string path)
    {
        if (Directory.Exists(path))
        {
            return Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories).Where(BatchRunner.IsVideo);
        }

        return BatchRunner.IsVideo(path) && File.Exists(path) ? [path] : [];
    }

    [RelayCommand]
    private void ClearJobs()
    {
        if (IsRunning)
        {
            return;
        }

        Jobs.Clear();
    }

    [RelayCommand]
    private async Task StartAsync()
    {
        if (IsRunning || !CanStart)
        {
            return;
        }

        PersistSettings();

        IsRunning = true;
        SafeStopPending = false;
        RunState = "실행 중";

        _runner = new BatchRunner(_settings, WriteLog);
        _runner.JobChanged += job =>
        {
            var row = Jobs.FirstOrDefault(r => ReferenceEquals(r.Job, job));
            if (row != null)
            {
                Dispatcher.UIThread.Post(row.Refresh);
            }
        };

        try
        {
            await _runner.RunAsync(Jobs.Select(r => r.Job).ToList());
        }
        finally
        {
            IsRunning = false;
            SafeStopPending = false;
            RunState = "대기";
            var done = Jobs.Count(r => r.Job.State == JobState.Done);
            var failed = Jobs.Count(r => r.Job.State == JobState.Failed);
            WriteLog($"완료 {done}개, 실패 {failed}개");
        }
    }

    /// <summary>Finish the film in flight, then stop - no half-written subtitle beside a video.</summary>
    [RelayCommand]
    private void SafeStop()
    {
        _runner?.RequestSafeStop();
        SafeStopPending = true;
        RunState = "현재 파일 후 정지";
        WriteLog("안전 정지 예약 — 현재 파일을 끝내고 멈춥니다");
    }

    [RelayCommand]
    private void Stop()
    {
        _runner?.RequestStop();
        RunState = "정지 중";
        WriteLog("정지 요청");
    }

    [RelayCommand]
    private void ToggleOptions() => OptionsVisible = !OptionsVisible;

    [RelayCommand]
    private void ToggleKey() => KeyVisible = !KeyVisible;

    [RelayCommand]
    private void ToggleProblems()
    {
        ProblemsOnly = !ProblemsOnly;
        RebuildLog();
    }

    [RelayCommand]
    private void ClearLog()
    {
        _allLog.Clear();
        Log.Clear();
    }

    public string LogText => string.Join(Environment.NewLine, Log);

    private void PersistSettings()
    {
        _settings.ApiKey = ApiKey;
        _settings.Model = Model;
        _settings.Language = Language;
        _settings.Region = Region;
        _settings.SkipExisting = SkipExisting;
        _settings.Save();
    }

    /// <summary>★Words that mark a line worth keeping when the noise is filtered out.</summary>
    private static readonly string[] ProblemMarkers =
        ["실패", "오류", "error", "failed", "429", "5xx", "취소", "timeout", "재시도", "retry"];

    private static bool IsProblem(string line)
        => ProblemMarkers.Any(m => line.Contains(m, StringComparison.OrdinalIgnoreCase));

    private void WriteLog(string message)
    {
        var line = $"{DateTime.Now:HH:mm:ss}  {message.TrimEnd()}";
        Dispatcher.UIThread.Post(() =>
        {
            _allLog.Add(line);
            if (!ProblemsOnly || IsProblem(line))
            {
                Log.Add(line);
            }

            // A three-hour batch would otherwise grow the pane without bound.
            while (Log.Count > 2000)
            {
                Log.RemoveAt(0);
            }
        });
    }

    private void RebuildLog()
    {
        Log.Clear();
        foreach (var line in _allLog.Where(l => !ProblemsOnly || IsProblem(l)))
        {
            Log.Add(line);
        }
    }
}
