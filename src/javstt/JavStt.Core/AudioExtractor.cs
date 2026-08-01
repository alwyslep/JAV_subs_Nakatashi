using System.Diagnostics;
using System.Globalization;

namespace JavStt.Core;

/// <summary>
/// Turns a video into the one audio file the transcriber uploads.
///
/// ★The arguments are Subtitle Edit's own, character for character
///   (<c>SpeechToTextViewModel.GetFfmpegTranscodeFormatString</c>): 16 kHz mono mp3 at 32 kbps with
///   <c>volume=1.75</c>. They are not obvious - the volume boost in particular is what makes quiet
///   dialogue survive - and diverging from them would quietly change transcription quality relative
///   to everything measured in the app.
///
/// ★One file, not chunks. Fun-ASR accepts 12 hours / 2 GB in a single upload, so none of the app's
///   chunker, silence detection or seam handling is needed here. A 3h17m film comes out at 45 MB.
/// </summary>
public static class AudioExtractor
{
    /// <summary>Subtitle Edit's exact STT transcode settings.</summary>
    internal const string ArgumentsFormat =
        "-i \"{0}\" -vn -ar 16000 -ac 1 -af volume=1.75 -c:a libmp3lame -b:a 32k -f mp3 -y \"{1}\"";

    public static string BuildArguments(string videoPath, string outputPath)
        => string.Format(CultureInfo.InvariantCulture, ArgumentsFormat, videoPath, outputPath);

    /// <summary>
    /// Extracts <paramref name="videoPath"/> to <paramref name="outputPath"/>.
    /// ★Progress is reported as a percentage parsed from ffmpeg's own <c>time=</c> output, because a
    ///   three-hour film takes minutes here and a UI with no number looks hung.
    /// </summary>
    public static async Task ExtractAsync(
        string ffmpegPath,
        string videoPath,
        string outputPath,
        double totalSeconds,
        IProgress<double>? progress = null,
        Action<string>? log = null,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(videoPath))
        {
            throw new FileNotFoundException("Video not found", videoPath);
        }

        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);

        var startInfo = new ProcessStartInfo(ffmpegPath, BuildArguments(videoPath, outputPath))
        {
            CreateNoWindow = true,
            UseShellExecute = false,
            RedirectStandardError = true,
        };

        using var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
        var lastLine = string.Empty;

        process.ErrorDataReceived += (_, e) =>
        {
            if (string.IsNullOrWhiteSpace(e.Data))
            {
                return;
            }

            lastLine = e.Data;
            if (totalSeconds > 0 && progress != null && TryParseTime(e.Data, out var seconds))
            {
                progress.Report(Math.Clamp(seconds / totalSeconds, 0, 1));
            }
        };

        process.Start();
        process.BeginErrorReadLine();
        await process.WaitForExitAsync(cancellationToken);

        if (process.ExitCode != 0)
        {
            log?.Invoke($"ffmpeg exited {process.ExitCode}: {lastLine}");
            throw new InvalidOperationException($"ffmpeg failed ({process.ExitCode}) extracting audio. Last output: {lastLine}");
        }

        progress?.Report(1);
    }

    /// <summary>
    /// Reads <c>time=HH:MM:SS.ss</c> out of an ffmpeg progress line.
    /// ★ffmpeg also emits <c>time=-577014:32:22.77</c> before the first frame lands; a negative
    ///   value would drive the bar backwards, so it is rejected rather than clamped.
    /// </summary>
    internal static bool TryParseTime(string line, out double seconds)
    {
        seconds = 0;
        var at = line.IndexOf("time=", StringComparison.Ordinal);
        if (at < 0)
        {
            return false;
        }

        var value = line[(at + 5)..].TrimStart();
        var end = value.IndexOf(' ');
        if (end > 0)
        {
            value = value[..end];
        }

        if (!TimeSpan.TryParse(value, CultureInfo.InvariantCulture, out var span) || span < TimeSpan.Zero)
        {
            return false;
        }

        seconds = span.TotalSeconds;
        return true;
    }
}
