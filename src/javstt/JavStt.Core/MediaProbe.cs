using System.Diagnostics;
using System.Globalization;

namespace JavStt.Core;

/// <summary>
/// Reads a media file's duration with ffprobe.
///
/// ★Needed for two things that both look broken without it: the extraction progress bar has no
///   denominator, and the queue cannot show how much audio is about to be billed. Failure is not
///   fatal - the duration falls back to 0 and only the percentage is lost.
/// </summary>
public static class MediaProbe
{
    public static async Task<double> DurationSecondsAsync(
        string ffprobePath, string mediaPath, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(mediaPath) || !File.Exists(ffprobePath))
        {
            return 0;
        }

        var startInfo = new ProcessStartInfo(
            ffprobePath,
            $"-v error -show_entries format=duration -of default=noprint_wrappers=1:nokey=1 \"{mediaPath}\"")
        {
            CreateNoWindow = true,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        try
        {
            using var process = Process.Start(startInfo);
            if (process == null)
            {
                return 0;
            }

            var output = await process.StandardOutput.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken);

            return double.TryParse(output.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds)
                ? seconds
                : 0;
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            return 0;
        }
    }

    /// <summary>ffprobe sits beside ffmpeg in every distribution of it.</summary>
    public static string FfprobeBeside(string ffmpegPath)
    {
        var directory = Path.GetDirectoryName(ffmpegPath);
        if (string.IsNullOrEmpty(directory))
        {
            return OperatingSystem.IsWindows() ? "ffprobe.exe" : "ffprobe";
        }

        var name = OperatingSystem.IsWindows() ? "ffprobe.exe" : "ffprobe";
        return Path.Combine(directory, name);
    }
}
