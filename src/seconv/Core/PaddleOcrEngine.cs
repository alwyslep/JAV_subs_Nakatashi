using System.Diagnostics;
using System.Text;
using SkiaSharp;

namespace SeConv.Core;

/// <summary>
/// OCR via the PaddleOCR command-line tool on the system PATH. Supports the launcher
/// installed by <c>pip install paddleocr</c> - <c>paddleocr</c> / <c>paddleocr.exe</c>, or a
/// <c>.bat</c> / <c>.cmd</c> shim as pyenv-win and pipx expose it.
/// Limited subset compared to SE's UI implementation; assumes a single image per invocation.
/// </summary>
internal sealed class PaddleOcrEngine : IOcrEngine
{
    public string Name => "paddleocr";

    public string ExecutablePath { get; }
    public string Language { get; }

    private readonly string _workDir;

    private PaddleOcrEngine(string executablePath, string language, string workDir)
    {
        ExecutablePath = executablePath;
        Language = language;
        _workDir = workDir;
    }

    public static string? Detect()
    {
        // Windows: probe the shim extensions too, not just .exe. pyenv-win (and pipx) put a
        // .bat/.cmd launcher on PATH and keep the real paddleocr.exe under a
        // versions\<ver>\Scripts folder that is NOT on PATH - probing only "paddleocr.exe"
        // reports "not on PATH" for an install that works fine from a shell. Measured: a
        // .bat shim starts correctly through ProcessStartInfo with UseShellExecute=false.
        var names = OperatingSystem.IsWindows()
            ? new[] { "paddleocr.exe", "paddleocr.bat", "paddleocr.cmd" }
            : new[] { "paddleocr" };
        var pathEnv = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        var separator = OperatingSystem.IsWindows() ? ';' : ':';
        foreach (var dir in pathEnv.Split(separator, StringSplitOptions.RemoveEmptyEntries))
        {
            foreach (var name in names)
            {
                var candidate = Path.Combine(dir.Trim(), name);
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
        }
        return null;
    }

    public static PaddleOcrEngine Create(string language = "en")
    {
        var path = Detect()
            ?? throw new InvalidOperationException(
                "PaddleOCR not found on PATH. Install it (e.g. `pip install paddleocr`) " +
                "and ensure the `paddleocr` binary is on PATH.");

        var workDir = Path.Combine(Path.GetTempPath(), "seconv_paddle_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workDir);
        return new PaddleOcrEngine(path, language, workDir);
    }

    public string Recognize(SKBitmap bitmap)
    {
        if (bitmap is null || bitmap.Width == 0 || bitmap.Height == 0)
        {
            return string.Empty;
        }

        var pngPath = Path.Combine(_workDir, "in_" + Guid.NewGuid().ToString("N") + ".png");
        try
        {
            using (var image = SKImage.FromBitmap(bitmap))
            using (var data = image.Encode(SKEncodedImageFormat.Png, 90))
            using (var fs = File.Create(pngPath))
            {
                data.SaveTo(fs);
            }

            // Flags mirror the UI's Paddle engine (Features/Ocr/Engines/PaddleOcr.cs):
            //  --use_textline_orientation  replaces --use_angle_cls, which PaddleOCR 3.x has
            //      deprecated (it prints a CLIDeprecationWarning and is slated for removal).
            //  --use_doc_orientation_classify / --use_doc_unwarping  are document-scan steps
            //      that a subtitle bitmap never needs; leaving them on downloads and runs two
            //      extra models (PP-LCNet_x1_0_doc_ori, UVDoc) per invocation.
            //  --enable_mkldnn False  is required, not an optimisation: a pip-installed
            //      paddlepaddle (measured on 3.3.1 + paddleocr 3.7.0, Windows CPU) aborts with
            //      "NotImplementedError: (Unimplemented) ConvertPirAttribute2RuntimeAttribute
            //      not support [pir::ArrayAttribute<pir::DoubleAttribute>]" from
            //      onednn_instruction.cc. The FLAGS_use_mkldnn env var does NOT help - paddlex
            //      overrides it from its own config, so it has to be the CLI option. The UI
            //      keeps MKL-DNN for its bundled standalone build, which is known-good; this
            //      engine only ever resolves a pip install, so it disables it unconditionally.
            var psi = new ProcessStartInfo(ExecutablePath)
            {
                ArgumentList =
                {
                    "ocr", "-i", pngPath, "--lang", Language,
                    "--use_textline_orientation", "False",
                    "--use_doc_orientation_classify", "False",
                    "--use_doc_unwarping", "False",
                    "--enable_mkldnn", "False",
                },
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = System.Text.Encoding.UTF8,
                StandardErrorEncoding = System.Text.Encoding.UTF8,
                UseShellExecute = false,
                CreateNoWindow = true,
            };

            // StandardOutputEncoding only fixes the decoding side. paddleocr is a Python CLI, and
            // on Windows Python encodes a *redirected* stdout with the ANSI codepage (until UTF-8
            // becomes the default in Python 3.15, PEP 686) - so the producer side must be forced
            // to UTF-8 too, or non-ASCII text still arrives as mojibake. Same env vars the UI's
            // Paddle engine sets.
            psi.EnvironmentVariables["PYTHONIOENCODING"] = "utf-8";
            psi.EnvironmentVariables["PYTHONUTF8"] = "1";

            using var proc = Process.Start(psi)
                ?? throw new InvalidOperationException("Failed to start paddleocr process.");
            // Drain stderr concurrently — paddleocr is chatty on stderr, and reading stdout
            // to completion while stderr fills the pipe buffer would deadlock.
            var stderrTask = proc.StandardError.ReadToEndAsync();
            var stdout = proc.StandardOutput.ReadToEnd();
            proc.WaitForExit();
            var stderr = stderrTask.GetAwaiter().GetResult();
            if (proc.ExitCode != 0)
            {
                throw new InvalidOperationException($"paddleocr exited with code {proc.ExitCode}: {stderr}");
            }

            // PaddleOCR 3.x emits the result dict through its logger, which writes to STDERR -
            // stdout comes back completely empty (measured on 3.7.0: exit 0, stdout length 0,
            // the whole "rec_texts" dict on stderr). 2.x printed its records to stdout. Reading
            // only stdout therefore recognises nothing on 3.x and the caller reports "No
            // subtitles recognised". Running the CLI in a terminal hides this, because both
            // streams land on the same console. Parse stdout first, then fall back to stderr.
            //
            // DELIBERATE DIVERGENCE from the UI engine (Features/Ocr/Engines/PaddleOcr.cs),
            // which meets the same problem by passing --save_path and reading one "<n>_res.json"
            // per image. Its comment calls the stderr dict "truncated", which is true of the
            // numpy arrays inside it (they print as "array([...], shape=(5,))") but NOT of
            // rec_texts: that is a plain Python list, and list repr does not elide. Measured on
            // a 25-line image - all 25 strings present, no "..." in the list. The UI needs the
            // JSON because it OCRs a whole folder asynchronously and reports per-line progress;
            // this engine hands over one image per invocation, so reading the stream it already
            // drains is both sufficient and one less temp directory to manage.
            var text = ParseStdout(stdout);
            return text.Length > 0 ? text : ParseStdout(stderr);
        }
        finally
        {
            try { File.Delete(pngPath); } catch { /* best-effort */ }
        }
    }

    /// <summary>
    /// Parses paddleocr's stdout, which changed shape between CLI generations:
    /// <list type="bullet">
    /// <item>2.x printed one <c>[bbox] ('text', conf)</c> record per line.</item>
    /// <item>3.x prints a Python dict per image whose <c>rec_texts</c> holds the strings
    /// (measured on paddleocr 3.7.0). The 2.x record shape is gone, so a parser that only
    /// knows it silently recognises nothing and the conversion fails with
    /// "No subtitles recognised".</item>
    /// </list>
    /// Both are handled: <c>rec_texts</c> wins when present, otherwise the 2.x records.
    /// </summary>
    internal static string ParseStdout(string stdout)
    {
        if (stdout.Contains("rec_texts", StringComparison.Ordinal))
        {
            return ParseRecTexts(stdout);
        }

        // Match: ('text', 0.95)  -- the recognised text is before the comma in single quotes.
        var sb = new StringBuilder();
        var lines = stdout.Replace("\r\n", "\n").Split('\n');
        foreach (var line in lines)
        {
            var startIdx = line.IndexOf("('", StringComparison.Ordinal);
            if (startIdx < 0)
            {
                continue;
            }
            var endIdx = line.IndexOf("',", startIdx + 2, StringComparison.Ordinal);
            if (endIdx < 0)
            {
                continue;
            }
            var text = line[(startIdx + 2)..endIdx];
            if (sb.Length > 0)
            {
                sb.AppendLine();
            }
            sb.Append(text);
        }
        return sb.ToString().Trim();
    }

    /// <summary>
    /// Pulls the strings out of every <c>rec_texts</c> list in a PaddleOCR 3.x dump, e.g.
    /// <c>{'res': {..., 'rec_texts': ['line one', 'line two'], 'rec_scores': array([...])}}</c>.
    /// Scanned over the whole dump rather than per line because the neighbouring numpy arrays
    /// are printed across several lines.
    /// </summary>
    private static string ParseRecTexts(string stdout)
    {
        var sb = new StringBuilder();
        var searchFrom = 0;
        while (true)
        {
            var keyIdx = stdout.IndexOf("rec_texts", searchFrom, StringComparison.Ordinal);
            if (keyIdx < 0)
            {
                break;
            }

            var open = stdout.IndexOf('[', keyIdx);
            if (open < 0)
            {
                break;
            }

            var close = stdout.IndexOf(']', open);
            if (close < 0)
            {
                break;
            }

            AppendQuotedItems(stdout[(open + 1)..close], sb);
            searchFrom = close + 1;
        }

        return sb.ToString().Trim();
    }

    /// <summary>
    /// Appends each quoted element of a Python list body to <paramref name="sb"/>, one per
    /// line. Handles either quote character and the backslash escapes Python's repr emits.
    /// </summary>
    private static void AppendQuotedItems(string body, StringBuilder sb)
    {
        var i = 0;
        while (i < body.Length)
        {
            var quote = body[i];
            if (quote != '\'' && quote != '"')
            {
                i++;
                continue;
            }

            i++;
            var item = new StringBuilder();
            while (i < body.Length && body[i] != quote)
            {
                if (body[i] == '\\' && i + 1 < body.Length)
                {
                    i++;
                }

                item.Append(body[i]);
                i++;
            }

            i++; // closing quote
            var text = item.ToString().Trim();
            if (text.Length == 0)
            {
                continue;
            }

            if (sb.Length > 0)
            {
                sb.AppendLine();
            }

            sb.Append(text);
        }
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_workDir))
            {
                Directory.Delete(_workDir, recursive: true);
            }
        }
        catch { /* ignore */ }
    }
}
