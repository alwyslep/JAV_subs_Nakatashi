using System.Text.Json;
using System.Text.Json.Serialization;
using Nikse.SubtitleEdit.Features.Video.SpeechToText.DashScope;

namespace JavStt.Core;

/// <summary>
/// Everything the tool needs, persisted beside the executable.
///
/// ★Portable by default, like Subtitle Edit's own settings file. The point of this tool is to run
///   against a library on an external drive; a per-user AppData file makes it behave differently
///   depending on who is logged in.
/// </summary>
public class JavSttSettings
{
    /// <summary>Alibaba Model Studio (DashScope) key. Kept in the settings file in plain text -
    /// the same treatment Subtitle Edit gives its own API keys.</summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>
    /// ★fun-asr rather than qwen3-asr-flash-filetrans, and that is the whole reason this tool
    ///   exists. Measured on the same 3h17m film: fun-asr returned 1264 cues with a 1.36s median
    ///   against qwen3-asr-flash's timestamps, 75% of which were under half a second with several
    ///   at exactly zero.
    /// </summary>
    public string Model { get; set; } = "fun-asr";

    public string Language { get; set; } = "ja";

    /// <summary>"international" is Singapore. The other value is "china".</summary>
    public string Region { get; set; } = "international";

    /// <summary>Suffix before .srt, so ABF-062.mp4 becomes ABF-062.ja.srt.</summary>
    public string OutputSuffix { get; set; } = "ja";

    /// <summary>Two hours. A 12-hour upload plus transcription has to fit inside it.</summary>
    public int TimeoutSeconds { get; set; } = 7200;

    /// <summary>Skip a video that already has the output file beside it.</summary>
    public bool SkipExisting { get; set; } = true;

    /// <summary>0 leaves diarization off - measured not to separate speakers on this material.</summary>
    public int SpeakerCount { get; set; }

    public string FfmpegPath { get; set; } = string.Empty;

    /// <summary>Folder the file picker opens in.</summary>
    public string LastFolder { get; set; } = string.Empty;

    // ── window state ────────────────────────────────────────────────────────────────────────
    // ★Restored on the next start, because a tool run against a library on an external drive gets
    //   opened hundreds of times and re-dragging it every time is the kind of friction that makes
    //   people stop using something.

    public double WindowWidth { get; set; } = 980;
    public double WindowHeight { get; set; } = 760;

    /// <summary>
    /// Null means "never positioned" - the window centres itself instead.
    /// ★Nullable rather than NaN. NaN is not representable in JSON and System.Text.Json throws on
    ///   it, which Save's catch then swallowed - so the entire settings file silently stopped being
    ///   written, taking the API key with it. Caught by the round-trip test, not by running the app.
    /// </summary>
    public double? WindowX { get; set; }

    public double? WindowY { get; set; }

    public bool WindowMaximized { get; set; }

    /// <summary>Height of the log pane; the queue takes the rest.</summary>
    public double LogHeight { get; set; } = 200;

    // ── usage ───────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Audio seconds the provider has billed, accumulated across every run.
    ///
    /// ★The provider's own figure, not the files' lengths - it counts detected speech, so a
    ///   600-second clip billed 333. Both families report it, under different keys
    ///   (fun-asr <c>usage.duration</c>, qwen3 <c>usage.seconds</c>); reading only one of them is
    ///   what made fun-asr look like it reported nothing.
    /// </summary>
    public double BilledSecondsTotal { get; set; }

    /// <summary>Films transcribed, ever. Paired with the seconds so the average is visible.</summary>
    public int FilmsTranscribedTotal { get; set; }

    [JsonIgnore]
    public static string DefaultPath =>
        Path.Combine(AppContext.BaseDirectory, "javstt.settings.json");

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };

    /// <summary>★Never throws: a corrupt or absent file yields defaults, because refusing to start
    /// over a settings file helps nobody.</summary>
    public static JavSttSettings Load(string? path = null)
    {
        var file = path ?? DefaultPath;
        try
        {
            if (File.Exists(file))
            {
                return JsonSerializer.Deserialize<JavSttSettings>(File.ReadAllText(file), Options) ?? new JavSttSettings();
            }
        }
        catch (Exception)
        {
            // fall through to defaults
        }

        return new JavSttSettings();
    }

    public void Save(string? path = null)
    {
        var file = path ?? DefaultPath;
        try
        {
            File.WriteAllText(file, JsonSerializer.Serialize(this, Options));
        }
        catch (Exception)
        {
            // A read-only install folder is a real deployment; losing settings is not worth a crash.
        }
    }

    /// <summary>The transcriber's own settings object, built from these.</summary>
    public DashScopeSttSettings ToDashScope(Action<string>? logger = null) => new()
    {
        ApiKey = ApiKey,
        Model = Model,
        Language = Language,
        Region = Region,
        TimeoutSeconds = TimeoutSeconds,
        SpeakerCount = SpeakerCount,
        Logger = logger,
    };

    /// <summary>
    /// ffmpeg as configured, or the name alone so PATH resolves it.
    /// ★Also probes Subtitle Edit's own configured path is NOT attempted - this tool is standalone
    ///   and guessing at another app's settings file would be a silent coupling.
    /// </summary>
    public string ResolveFfmpeg()
    {
        if (!string.IsNullOrWhiteSpace(FfmpegPath) && File.Exists(FfmpegPath))
        {
            return FfmpegPath;
        }

        return OperatingSystem.IsWindows() ? "ffmpeg.exe" : "ffmpeg";
    }
}
