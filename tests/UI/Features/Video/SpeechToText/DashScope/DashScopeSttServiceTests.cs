using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Nikse.SubtitleEdit.Features.Video.SpeechToText.DashScope;

namespace UITests.Features.Video.SpeechToText.DashScope;

public class DashScopeSttServiceTests
{
    private static DashScopeSttSettings MakeSettings()
        => new()
        {
            ApiKey = "test-key",
            Model = "qwen3-asr-flash-filetrans",
            Language = "en",
            Region = "international",
            EnableWords = false,
            TimeoutSeconds = 3600,
        };

    [Theory]
    [InlineData("international", "https://dashscope-intl.aliyuncs.com")]
    [InlineData("china", "https://dashscope.aliyuncs.com")]
    [InlineData("", "https://dashscope-intl.aliyuncs.com")]
    [InlineData(null, "https://dashscope-intl.aliyuncs.com")]
    public void GetBaseUrl_PicksRegion(string? region, string expected)
    {
        Assert.Equal(expected, DashScopeSttService.GetBaseUrl(region));
    }

    [Fact]
    public void BuildSubmitBody_SetsModelFileUrlAndParameters()
    {
        var settings = MakeSettings();
        settings.EnableWords = true;
        var body = DashScopeSttService.BuildSubmitBody(settings, "oss://bucket/dir/audio.mp3", "ja");

        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;

        Assert.Equal("qwen3-asr-flash-filetrans", root.GetProperty("model").GetString());
        Assert.Equal("oss://bucket/dir/audio.mp3", root.GetProperty("input").GetProperty("file_url").GetString());

        var parameters = root.GetProperty("parameters");
        Assert.True(parameters.GetProperty("enable_words").GetBoolean());
        Assert.False(parameters.GetProperty("enable_itn").GetBoolean());
        Assert.Equal("ja", parameters.GetProperty("language").GetString());
    }

    [Fact]
    public void BuildSubmitBody_OmitsLanguageWhenAutoOrEmpty()
    {
        var settings = MakeSettings();
        settings.Language = string.Empty;
        var body = DashScopeSttService.BuildSubmitBody(settings, "oss://x/y.mp3", null);

        using var doc = JsonDocument.Parse(body);
        Assert.False(doc.RootElement.GetProperty("parameters").TryGetProperty("language", out _));
    }

    [Fact]
    public void ParseTranscriptionResult_MapsSentencesToSecondsSegments()
    {
        const string json = """
            {
              "transcripts": [
                {
                  "text": "Hello there. How are you?",
                  "sentences": [
                    { "begin_time": 760, "end_time": 3240, "text": "Hello there." },
                    { "begin_time": 3500, "end_time": 5000, "text": "How are you?" }
                  ]
                }
              ]
            }
            """;

        var result = DashScopeSttService.ParseTranscriptionResult(json);

        Assert.NotNull(result.Segments);
        Assert.Equal(2, result.Segments!.Count);
        // Milliseconds converted to seconds.
        Assert.Equal(0.76, result.Segments[0].Start, 3);
        Assert.Equal(3.24, result.Segments[0].End, 3);
        Assert.Equal("Hello there.", result.Segments[0].Text);
        Assert.Equal(5.0, result.Segments[1].End, 3);
        Assert.Contains("How are you?", result.Text);
    }

    [Fact]
    public void ParseTranscriptionResult_EmptyOrMalformed_ReturnsEmpty()
    {
        var result = DashScopeSttService.ParseTranscriptionResult("not json");
        Assert.NotNull(result.Segments);
        Assert.Empty(result.Segments!);
        Assert.Equal(string.Empty, result.Text);
    }

    // ── retry ───────────────────────────────────────────────────────────────────────────────
    //
    // ★These drive the whole four-request flow rather than the policy in isolation, because the
    //   policy already has its own tests and what was actually missing here was DashScope USING
    //   it. Each test faults exactly one stage and leaves the rest healthy, so a failure names the
    //   stage that stopped retrying.

    private const string UploadHost = "https://oss-upload.example/";
    private const string TranscriptionUrl = "https://oss-result.example/result.json";

    // Concatenated rather than interpolated: these bodies end in `}}`, which a $$""" literal reads
    // as a closing interpolation brace.
    private const string PolicyJson =
        "{\"data\":{\"policy\":\"p\",\"signature\":\"s\",\"upload_dir\":\"dir\"," +
        "\"upload_host\":\"" + UploadHost + "\",\"oss_access_key_id\":\"ak\"," +
        "\"x_oss_object_acl\":\"public-read\",\"x_oss_forbid_overwrite\":\"false\"}}";

    private const string SubmitJson = """{"output":{"task_id":"task-1"}}""";

    private const string PolledSucceededJson =
        "{\"output\":{\"task_status\":\"SUCCEEDED\"," +
        "\"result\":{\"transcription_url\":\"" + TranscriptionUrl + "\"}}," +
        "\"usage\":{\"duration\":333}}";

    private const string ResultJson = """
        {"transcripts":[{"text":"ok","sentences":[{"begin_time":0,"end_time":1000,"text":"ok"}]}]}
        """;

    private enum Stage { Policy, Upload, Submit, Poll, Result }

    private static Stage StageOf(HttpRequestMessage request)
    {
        var url = request.RequestUri!.ToString();
        if (url.Contains("action=getPolicy", StringComparison.Ordinal)) return Stage.Policy;
        if (url.StartsWith(UploadHost, StringComparison.Ordinal)) return Stage.Upload;
        if (url.Contains("/asr/transcription", StringComparison.Ordinal)) return Stage.Submit;
        if (url.Contains("/api/v1/tasks/", StringComparison.Ordinal)) return Stage.Poll;
        if (url == TranscriptionUrl) return Stage.Result;
        throw new InvalidOperationException($"unexpected request: {url}");
    }

    private static HttpResponseMessage Ok(string body)
        => new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private static HttpResponseMessage Healthy(Stage stage) => stage switch
    {
        Stage.Policy => Ok(PolicyJson),
        Stage.Upload => Ok(string.Empty),
        Stage.Submit => Ok(SubmitJson),
        Stage.Poll => Ok(PolledSucceededJson),
        _ => Ok(ResultJson),
    };

    /// <summary>
    /// A transient rejection carrying the provider's own wait.
    /// ★Retry-After: 1s rather than letting the backoff pick, so each of these tests spends one
    ///   second instead of the two the exponential floor would cost - and the header path, which
    ///   is the one that actually fires on a real quota rejection, gets exercised end to end.
    /// </summary>
    private static HttpResponseMessage Throttled()
    {
        var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests)
        {
            Content = new StringContent("""{"code":"Throttling","message":"quota spent"}"""),
        };
        response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(1));
        return response;
    }

    private static string MakeTempAudio(out byte[] bytes)
    {
        bytes = Enumerable.Range(0, 4096).Select(i => (byte)(i % 251)).ToArray();
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".mp3");
        File.WriteAllBytes(path, bytes);
        return path;
    }

    /// <summary>
    /// The case the whole change exists for: the account's quota is spent at submit time, which
    /// used to fail the film outright even though the audio was already uploaded.
    /// </summary>
    [Fact]
    public async Task TranscribeAsync_RetriesA429OnSubmit()
    {
        var submits = 0;
        var submittedBodies = new List<string>();

        using var handler = new StubHandler(async (request, ct) =>
        {
            var stage = StageOf(request);
            if (stage != Stage.Submit)
            {
                return Healthy(stage);
            }

            submits++;
            // Read it every time: a retry that lost the body would submit an empty task.
            submittedBodies.Add(await request.Content!.ReadAsStringAsync(ct));
            return submits == 1 ? Throttled() : Ok(SubmitJson);
        });

        using var client = new HttpClient(handler);
        var service = new DashScopeSttService(client, MakeSettings());
        var audio = MakeTempAudio(out _);

        try
        {
            var result = await service.TranscribeAsync(
                audio, "ja", null, null, TestContext.Current.CancellationToken);

            Assert.Equal(2, submits);
            Assert.Equal(submittedBodies[0], submittedBodies[1]);
            Assert.Contains("ok", result.Text);
            // The billed figure still has to survive the retry - it is read off the poll.
            Assert.Equal(333, service.LastBilledSeconds);
        }
        finally
        {
            File.Delete(audio);
        }
    }

    /// <summary>
    /// ★The upload is the one request that could plausibly not be replayable, and this is what
    ///   proves it is: the multipart form is rebuilt per attempt from the same byte[], so the
    ///   second attempt has to carry the identical, non-empty body. A form built once and re-sent
    ///   would arrive empty here.
    /// </summary>
    [Fact]
    public async Task TranscribeAsync_RetriesTheOssUploadAndResendsEveryByte()
    {
        var uploads = 0;
        var uploadedLengths = new List<long>();

        using var handler = new StubHandler(async (request, ct) =>
        {
            var stage = StageOf(request);
            if (stage != Stage.Upload)
            {
                return Healthy(stage);
            }

            uploads++;
            uploadedLengths.Add((await request.Content!.ReadAsByteArrayAsync(ct)).Length);
            return uploads == 1 ? Throttled() : Ok(string.Empty);
        });

        using var client = new HttpClient(handler);
        var service = new DashScopeSttService(client, MakeSettings());
        var audio = MakeTempAudio(out var bytes);

        try
        {
            await service.TranscribeAsync(audio, "ja", null, null, TestContext.Current.CancellationToken);

            Assert.Equal(2, uploads);
            Assert.Equal(uploadedLengths[0], uploadedLengths[1]);
            Assert.True(uploadedLengths[1] > bytes.Length,
                "the replayed upload did not carry the audio - the multipart form was not rebuilt");
        }
        finally
        {
            File.Delete(audio);
        }
    }

    /// <summary>
    /// A poll that blips must not abandon a task the provider is already billing for.
    /// </summary>
    [Fact]
    public async Task TranscribeAsync_RetriesATransientPollFailure()
    {
        var polls = 0;

        using var handler = new StubHandler((request, ct) =>
        {
            var stage = StageOf(request);
            if (stage != Stage.Poll)
            {
                return Task.FromResult(Healthy(stage));
            }

            polls++;
            return Task.FromResult(polls == 1 ? Throttled() : Ok(PolledSucceededJson));
        });

        using var client = new HttpClient(handler);
        var service = new DashScopeSttService(client, MakeSettings());
        var audio = MakeTempAudio(out _);

        try
        {
            var result = await service.TranscribeAsync(
                audio, "ja", null, null, TestContext.Current.CancellationToken);

            Assert.Equal(2, polls);
            Assert.Contains("ok", result.Text);
        }
        finally
        {
            File.Delete(audio);
        }
    }

    /// <summary>
    /// A bad key stays bad. Retrying it three times only delays the message the user has to read -
    /// and on a batch of hundreds it would delay every single film.
    /// </summary>
    [Fact]
    public async Task TranscribeAsync_DoesNotRetryA401()
    {
        var attempts = 0;

        using var handler = new StubHandler((request, ct) =>
        {
            attempts++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized)
            {
                Content = new StringContent("""{"code":"InvalidApiKey"}"""),
            });
        });

        using var client = new HttpClient(handler);
        var service = new DashScopeSttService(client, MakeSettings());
        var audio = MakeTempAudio(out _);

        try
        {
            await Assert.ThrowsAsync<HttpRequestException>(
                () => service.TranscribeAsync(audio, "ja", null, null, TestContext.Current.CancellationToken));

            Assert.Equal(1, attempts);
        }
        finally
        {
            File.Delete(audio);
        }
    }

    /// <summary>
    /// The budget is per request, so it has to be spent and then given up on - four attempts at
    /// one stage, not an unbounded loop.
    /// </summary>
    [Fact]
    public async Task TranscribeAsync_GivesUpAfterTheRetryBudget()
    {
        var submits = 0;

        using var handler = new StubHandler((request, ct) =>
        {
            var stage = StageOf(request);
            if (stage != Stage.Submit)
            {
                return Task.FromResult(Healthy(stage));
            }

            submits++;
            return Task.FromResult(Throttled());
        });

        using var client = new HttpClient(handler);
        var service = new DashScopeSttService(client, MakeSettings());
        var audio = MakeTempAudio(out _);

        try
        {
            var failure = await Assert.ThrowsAsync<HttpRequestException>(
                () => service.TranscribeAsync(audio, "ja", null, null, TestContext.Current.CancellationToken));

            Assert.Equal(HttpStatusCode.TooManyRequests, failure.StatusCode);
            Assert.Equal(4, submits); // the first try plus SttRetryPolicy.MaxTransientRetries
        }
        finally
        {
            File.Delete(audio);
        }
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _send;

        public StubHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send)
        {
            _send = send;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => _send(request, cancellationToken);
    }
}
