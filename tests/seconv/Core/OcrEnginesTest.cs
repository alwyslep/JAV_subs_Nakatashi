using SeConv.Core;
using Xunit;

namespace SeConvTests.Core;

public class OcrEnginesTest : IDisposable
{
    private readonly string _tempRoot;

    public OcrEnginesTest()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "OcrEngines_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempRoot);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempRoot))
            Directory.Delete(_tempRoot, recursive: true);
    }

    private static ConversionOptions Opts(string engine, string? ocrDb = null) => new()
    {
        Patterns = ["dummy.sup"],
        Format = "SubRip",
        OcrEngine = engine,
        OcrLanguage = "eng",
        OcrDb = ocrDb,
    };

    [Fact]
    public void Factory_NOcrWithoutOcrDb_Throws()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => OcrEngineFactory.Create(Opts("nocr")));
        Assert.Contains("--ocr-db", ex.Message);
    }

    [Fact]
    public void Factory_NOcrMissingFile_Throws()
    {
        var ex = Assert.Throws<FileNotFoundException>(() =>
            OcrEngineFactory.Create(Opts("nocr", Path.Combine(_tempRoot, "missing.nocr"))));
        Assert.Contains("missing.nocr", ex.Message);
    }

    [Fact]
    public void Factory_NOcrAutoAppendsExtension()
    {
        // When --ocr-db is "Latin" (no extension), factory appends ".nocr" before checking
        var ex = Assert.Throws<FileNotFoundException>(() =>
            OcrEngineFactory.Create(Opts("nocr", Path.Combine(_tempRoot, "Latin"))));
        Assert.Contains("Latin.nocr", ex.Message);
    }

    [Fact]
    public void Factory_BinaryOcrWithoutOcrDb_Throws()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => OcrEngineFactory.Create(Opts("binaryocr")));
        Assert.Contains("--ocr-db", ex.Message);
        Assert.Contains(".db", ex.Message);
    }

    [Fact]
    public void Factory_BinaryOcrAutoAppendsDbExtension()
    {
        var ex = Assert.Throws<FileNotFoundException>(() =>
            OcrEngineFactory.Create(Opts("binaryocr", Path.Combine(_tempRoot, "Latin"))));
        Assert.Contains("Latin.db", ex.Message);
    }

    [Fact]
    public void Factory_UnknownEngine_Throws()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => OcrEngineFactory.Create(Opts("nope")));
        Assert.Contains("nope", ex.Message);
        Assert.Contains("tesseract", ex.Message);
        Assert.Contains("nocr", ex.Message);
        Assert.Contains("binaryocr", ex.Message);
        Assert.Contains("ollama", ex.Message);
        Assert.Contains("paddle", ex.Message);
    }

    [Fact]
    public void Factory_TesseractRouted()
    {
        // If Tesseract is installed, this succeeds. Otherwise InvalidOperationException
        // with the install hint.
        if (TesseractOcrEngine.Detect() is null)
        {
            Assert.Throws<InvalidOperationException>(() => OcrEngineFactory.Create(Opts("tesseract")));
        }
        else
        {
            using var engine = OcrEngineFactory.Create(Opts("tesseract"));
            Assert.Equal("tesseract", engine.Name);
        }
    }

    [Fact]
    public void Factory_PaddleRouted()
    {
        if (PaddleOcrEngine.Detect() is null)
        {
            Assert.Throws<InvalidOperationException>(() => OcrEngineFactory.Create(Opts("paddle")));
        }
        else
        {
            using var engine = OcrEngineFactory.Create(Opts("paddle"));
            Assert.Equal("paddleocr", engine.Name);
        }
    }

    [Fact]
    public void Factory_OllamaConstructionAlwaysSucceeds()
    {
        // Ollama doesn't probe at construction time — it's an HTTP client.
        // Bad URL only fails when Recognize() is called.
        using var engine = OcrEngineFactory.Create(Opts("ollama"));
        Assert.Equal("ollama", engine.Name);
    }

    [Fact]
    public void Paddle_ParseStdout_ExtractsTextLines()
    {
        // Real paddleocr output sample
        var stdout = """
            [[10, 20], [100, 20], [100, 40], [10, 40]] ('Hello world', 0.95)
            [[10, 50], [100, 50], [100, 70], [10, 70]] ('second line', 0.91)
            """;
        var text = PaddleOcrEngine.ParseStdout(stdout);
        Assert.Contains("Hello world", text);
        Assert.Contains("second line", text);
    }

    [Fact]
    public void Paddle_ParseStdout_NoMatches_ReturnsEmpty()
    {
        Assert.Equal(string.Empty, PaddleOcrEngine.ParseStdout("no recognized text here"));
        Assert.Equal(string.Empty, PaddleOcrEngine.ParseStdout(""));
    }

    // PaddleOCR 3.x dropped the 2.x "('text', conf)" record entirely and prints a Python dict
    // whose rec_texts holds the strings. Samples below are real paddleocr 3.7.0 stdout with the
    // numpy arrays shortened the way the CLI itself prints them.
    [Fact]
    public void Paddle_ParseStdout_PaddleOcr3_ExtractsRecTexts()
    {
        var stdout = """
            [2026/08/03 11:22:25] paddleocr INFO: Processed item 0 in 5121.5 ms
            {'res': {'input_path': 'C:\\tmp\\in.png', 'page_index': None, 'dt_polys': array([[[450,  22],
                    ...,
                    [450,  86]]], shape=(1, 4, 2), dtype=int16), 'text_type': 'general', 'rec_texts':
            ['こんにちは世界'], 'rec_scores': array([0.99997437])}}
            """;
        Assert.Equal("こんにちは世界", PaddleOcrEngine.ParseStdout(stdout));
    }

    [Fact]
    public void Paddle_ParseStdout_PaddleOcr3_JoinsMultipleLines()
    {
        var stdout = "{'res': {'rec_texts': ['first line', 'second line'], 'rec_scores': array([0.9, 0.8])}}";
        Assert.Equal("first line" + Environment.NewLine + "second line", PaddleOcrEngine.ParseStdout(stdout));
    }

    [Fact]
    public void Paddle_ParseStdout_PaddleOcr3_UnescapesQuotes()
    {
        // Python's repr escapes a quote inside a same-quoted string.
        var stdout = """"
            {'res': {'rec_texts': ['it\'s here'], 'rec_scores': array([0.9])}}
            """";
        Assert.Equal("it's here", PaddleOcrEngine.ParseStdout(stdout));
    }

    [Fact]
    public void Paddle_ParseStdout_PaddleOcr3_EmptyRecTexts_ReturnsEmpty()
    {
        Assert.Equal(string.Empty, PaddleOcrEngine.ParseStdout("{'res': {'rec_texts': [], 'rec_scores': array([])}}"));
    }
}
