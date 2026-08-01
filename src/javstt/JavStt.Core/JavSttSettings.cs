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
