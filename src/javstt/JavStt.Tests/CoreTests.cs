using JavStt.Core;
using Nikse.SubtitleEdit.Features.Video.SpeechToText.OpenAiCompatible;

namespace JavStt.Tests;

public class AudioExtractorTests
{
    // ★Subtitle Edit's exact STT transcode string. If this drifts, transcription quality drifts
    //   with it against everything measured in the app - the volume boost especially.
    [Fact]
    public void Arguments_MatchSubtitleEditsOwnTranscodeSettings()
    {
        var args = AudioExtractor.BuildArguments(@"C:\v\a b.mp4", @"C:\t\out.mp3");

        Assert.Contains("-ar 16000", args);
        Assert.Contains("-ac 1", args);
        Assert.Contains("-af volume=1.75", args);
        Assert.Contains("-c:a libmp3lame", args);
        Assert.Contains("-b:a 32k", args);
        Assert.Contains("-f mp3", args);
        // Paths with spaces must survive as one argument each.
        Assert.Contains("\"C:\\v\\a b.mp4\"", args);
        Assert.Contains("\"C:\\t\\out.mp3\"", args);
    }

    [Fact]
    public void TryParseTime_ReadsFfmpegProgress()
    {
        Assert.True(AudioExtractor.TryParseTime("size=  1024kB time=00:01:30.50 bitrate=  32.0kbits/s", out var s));
        Assert.Equal(90.5, s, 2);
    }

    // ffmpeg emits time=-577014:32:22.77 before the first frame; a negative value would drive a
    // progress bar backwards.
    [Fact]
    public void TryParseTime_RejectsFfmpegsNegativePlaceholder()
    {
        Assert.False(AudioExtractor.TryParseTime("time=-577014:32:22.77 bitrate=N/A", out _));
        Assert.False(AudioExtractor.TryParseTime("no time here", out _));
    }
}

public class SubtitleBuilderTests
{
    private static OpenAiCompatibleSegment Seg(double start, double end, string text)
        => new() { Start = start, End = end, Text = text };

    [Fact]
    public void Build_OrdersRenumbersAndTrims()
    {
        var subtitle = SubtitleBuilder.Build([
            Seg(10, 12, " 二番目 "),
            Seg(1, 3, "最初"),
        ]);

        Assert.Equal(2, subtitle.Paragraphs.Count);
        Assert.Equal("最初", subtitle.Paragraphs[0].Text);
        Assert.Equal("二番目", subtitle.Paragraphs[1].Text);
        Assert.Equal(1, subtitle.Paragraphs[0].Number);
        Assert.Equal(2, subtitle.Paragraphs[1].Number);
    }

    [Fact]
    public void Build_DropsEmptySegments()
    {
        var subtitle = SubtitleBuilder.Build([Seg(1, 2, "  "), Seg(3, 4, "ある")]);

        Assert.Single(subtitle.Paragraphs);
        Assert.Equal("ある", subtitle.Paragraphs[0].Text);
    }

    // ★Fun-ASR returns a handful of sub-half-second cues per film (86 of 1264 measured). They hold
    //   real dialogue, so they are stretched rather than dropped.
    [Fact]
    public void Build_StretchesUnreadablyShortCues()
    {
        var subtitle = SubtitleBuilder.Build([Seg(1.00, 1.05, "うん")]);

        Assert.Equal(500, subtitle.Paragraphs[0].DurationTotalMilliseconds, 0);
    }

    // ...but never into the next cue: two lines showing at once is worse than one short line.
    [Fact]
    public void Build_NeverStretchesPastTheNextCue()
    {
        var subtitle = SubtitleBuilder.Build([Seg(1.0, 1.05, "うん"), Seg(1.2, 3.0, "そうです")]);

        Assert.True(subtitle.Paragraphs[0].EndTime.TotalSeconds <= subtitle.Paragraphs[1].StartTime.TotalSeconds);
    }

    [Fact]
    public void Build_HandlesNoSegments()
    {
        Assert.Empty(SubtitleBuilder.Build(null).Paragraphs);
        Assert.Empty(SubtitleBuilder.Build([]).Paragraphs);
    }

    [Fact]
    public void OutputPath_SitsBesideTheVideoWithTheLanguageSuffix()
    {
        Assert.Equal(
            Path.Combine(@"G:\18R_V", "ABF-062 (1080p_aac).ja.srt"),
            SubtitleBuilder.OutputPathFor(@"G:\18R_V\ABF-062 (1080p_aac).mp4", "ja"));

        // A suffix given with or without its dot means the same thing.
        Assert.EndsWith(".ja.srt", SubtitleBuilder.OutputPathFor(@"G:\v\a.mp4", ".ja"));
        Assert.EndsWith("a.srt", SubtitleBuilder.OutputPathFor(@"G:\v\a.mp4", ""));
    }
}

public class SettingsTests
{
    // The default is the finding this tool was built on - see JavSttSettings.Model.
    [Fact]
    public void Defaults_AreFunAsrJapaneseSingapore()
    {
        var settings = new JavSttSettings();

        Assert.Equal("fun-asr", settings.Model);
        Assert.Equal("ja", settings.Language);
        Assert.Equal("international", settings.Region);
        Assert.Equal("ja", settings.OutputSuffix);
        Assert.True(settings.SkipExisting);
        Assert.Equal(0, settings.SpeakerCount);
    }

    [Fact]
    public void ToDashScope_CarriesEverythingTheServiceNeeds()
    {
        var settings = new JavSttSettings { ApiKey = "k", Model = "fun-asr", Language = "ja", SpeakerCount = 2 };
        var dash = settings.ToDashScope();

        Assert.Equal("k", dash.ApiKey);
        Assert.Equal("fun-asr", dash.Model);
        Assert.Equal("ja", dash.Language);
        Assert.Equal(2, dash.SpeakerCount);
    }

    [Fact]
    public void Load_FallsBackToDefaultsOnAMissingOrCorruptFile()
    {
        var path = Path.Combine(Path.GetTempPath(), $"javstt-test-{Guid.NewGuid():N}.json");
        Assert.Equal("fun-asr", JavSttSettings.Load(path).Model);

        File.WriteAllText(path, "{ this is not json");
        try
        {
            Assert.Equal("fun-asr", JavSttSettings.Load(path).Model);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void SaveThenLoad_RoundTrips()
    {
        var path = Path.Combine(Path.GetTempPath(), $"javstt-test-{Guid.NewGuid():N}.json");
        try
        {
            new JavSttSettings { ApiKey = "sk-ws-x", Model = "fun-asr-mtl", SpeakerCount = 3 }.Save(path);
            var loaded = JavSttSettings.Load(path);

            Assert.Equal("sk-ws-x", loaded.ApiKey);
            Assert.Equal("fun-asr-mtl", loaded.Model);
            Assert.Equal(3, loaded.SpeakerCount);
        }
        finally
        {
            File.Delete(path);
        }
    }
}

public class BatchRunnerTests
{
    [Fact]
    public void IsVideo_AcceptsTheContainersThisLibraryUses()
    {
        Assert.True(BatchRunner.IsVideo(@"G:\v\ABF-062.mp4"));
        Assert.True(BatchRunner.IsVideo(@"G:\v\a.MKV"));
        Assert.False(BatchRunner.IsVideo(@"G:\v\a.ja.srt"));
        Assert.False(BatchRunner.IsVideo(@"G:\v\a.txt"));
    }

    [Fact]
    public async Task Run_SkipsFilmsThatAlreadyHaveTheirSubtitle()
    {
        var dir = Directory.CreateTempSubdirectory("javstt");
        try
        {
            var video = Path.Combine(dir.FullName, "ABF-062.mp4");
            File.WriteAllText(video, "not really a video");
            File.WriteAllText(Path.Combine(dir.FullName, "ABF-062.ja.srt"), "1\n");

            var job = new TranscriptionJob { VideoPath = video };
            // No API key: reaching the transcriber at all would throw, so Skipped proves the guard
            // ran first.
            await new BatchRunner(new JavSttSettings { SkipExisting = true }).RunAsync([job], TestContext.Current.CancellationToken);

            Assert.Equal(JobState.Skipped, job.State);
        }
        finally
        {
            dir.Delete(true);
        }
    }

    // ★One bad film must not end a batch of hundreds.
    [Fact]
    public async Task Run_KeepsGoingAfterAFailure()
    {
        var dir = Directory.CreateTempSubdirectory("javstt");
        try
        {
            var a = Path.Combine(dir.FullName, "a.mp4");
            var b = Path.Combine(dir.FullName, "b.mp4");
            File.WriteAllText(a, "x");
            File.WriteAllText(b, "x");
            File.WriteAllText(Path.Combine(dir.FullName, "b.ja.srt"), "1\n");

            var jobs = new List<TranscriptionJob>
            {
                new() { VideoPath = a }, // no API key -> fails
                new() { VideoPath = b }, // already has a subtitle -> skipped
            };

            await new BatchRunner(new JavSttSettings { SkipExisting = true }).RunAsync(jobs, TestContext.Current.CancellationToken);

            Assert.Equal(JobState.Failed, jobs[0].State);
            Assert.Equal(JobState.Skipped, jobs[1].State);
        }
        finally
        {
            dir.Delete(true);
        }
    }

    // A safe stop is only honoured between films - that is what makes it safe.
    [Fact]
    public async Task SafeStop_LeavesLaterFilmsQueuedRatherThanFailed()
    {
        var dir = Directory.CreateTempSubdirectory("javstt");
        try
        {
            var jobs = new List<TranscriptionJob>();
            for (var i = 0; i < 3; i++)
            {
                var path = Path.Combine(dir.FullName, $"{i}.mp4");
                File.WriteAllText(path, "x");
                jobs.Add(new TranscriptionJob { VideoPath = path });
            }

            var runner = new BatchRunner(new JavSttSettings());
            runner.JobChanged += _ => runner.RequestSafeStop();
            await runner.RunAsync(jobs, TestContext.Current.CancellationToken);

            Assert.Equal(JobState.Failed, jobs[0].State);   // ran (and failed for lack of a key)
            Assert.Equal(JobState.Queued, jobs[1].State);   // never started
            Assert.Equal(JobState.Queued, jobs[2].State);
            Assert.False(runner.IsRunning);
        }
        finally
        {
            dir.Delete(true);
        }
    }
}
