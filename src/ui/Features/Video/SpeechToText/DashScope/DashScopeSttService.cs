using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Nikse.SubtitleEdit.Core.Common;
using Nikse.SubtitleEdit.Features.Video.SpeechToText.Engines;
using Nikse.SubtitleEdit.Features.Video.SpeechToText.OpenAiCompatible;
using Nikse.SubtitleEdit.UiLogic.Http;

namespace Nikse.SubtitleEdit.Features.Video.SpeechToText.DashScope;

/// <summary>
/// Speech-to-text via Alibaba Cloud Model Studio (DashScope) Qwen3-ASR, using the
/// asynchronous file-transcription path so real sentence (and optional word)
/// timestamps come back — the sync multimodal endpoint returns text only.
///
/// The async Filetrans job only accepts publicly resolvable URLs, so a local
/// file is first uploaded to DashScope's own temporary storage (an <c>oss://</c>
/// URL valid ~48h). The flow: getPolicy → OSS multipart upload → submit async
/// task → poll tasks/{id} → download the transcription_url JSON → map its
/// millisecond sentence timings into <see cref="OpenAiCompatibleSegment"/>s.
/// </summary>
public class DashScopeSttService : ISttTranscriber
{
    public const string InternationalBaseUrl = "https://dashscope-intl.aliyuncs.com";
    public const string ChinaBaseUrl = "https://dashscope.aliyuncs.com";
    private const string UploadsPath = "/api/v1/uploads";
    private const string TranscriptionPath = "/api/v1/services/audio/asr/transcription";
    private const string TasksPath = "/api/v1/tasks/";

    private static HttpClient? _sharedHttpClient;
    private static readonly Lock SharedHttpClientLock = new();

    /// <summary>
    /// Shared HttpClient for this service - created once, reused, infinite timeout so per-call
    /// deadlines come from a linked CancellationTokenSource; callers must NOT mutate Timeout on it.
    /// A fresh client per request leaks sockets into TIME_WAIT and skips DNS caching.
    ///
    /// ★Its own instead of borrowing the OpenAI one, so this file depends on nothing above it.
    ///   That is what lets the transcription engine be compiled into a standalone tool by linking
    ///   the source rather than copying it - and a copy would fork the OSS upload rules in
    ///   <see cref="BuildOssUploadForm"/>, which are the part that is expensive to rediscover.
    /// </summary>
    public static HttpClient SharedHttpClient
    {
        get
        {
            if (_sharedHttpClient != null)
            {
                return _sharedHttpClient;
            }

            lock (SharedHttpClientLock)
            {
                if (_sharedHttpClient == null)
                {
                    var client = HttpClientFactoryWithProxy.CreateHttpClientWithProxy();
                    client.Timeout = Timeout.InfiniteTimeSpan;
                    _sharedHttpClient = client;
                }
            }

            return _sharedHttpClient;
        }
    }

    private readonly HttpClient _httpClient;
    private readonly DashScopeSttSettings _settings;

    public DashScopeSttService(DashScopeSttSettings settings)
        : this(SharedHttpClient, settings)
    {
    }

    public DashScopeSttService(HttpClient httpClient, DashScopeSttSettings settings)
    {
        _httpClient = httpClient;
        _settings = settings;
    }

    /// <summary>
    /// Audio seconds the provider billed for the most recent transcription, or 0 when it reported
    /// none. ★Not the file's length - the provider counts detected speech, and a 600-second clip
    /// came back as 333, so only the reported figure says what was actually charged.
    /// </summary>
    public double LastBilledSeconds { get; private set; }

    /// <summary>Region base URL for the given region setting ("china" → Beijing, else international).</summary>
    public static string GetBaseUrl(string? region)
    {
        return string.Equals(region, "china", StringComparison.OrdinalIgnoreCase)
            ? ChinaBaseUrl
            : InternationalBaseUrl;
    }

    private string BaseUrl => GetBaseUrl(_settings.Region);

    public async Task<OpenAiCompatibleSttResponse> TranscribeAsync(
        string audioFilePath,
        string? language,
        IProgress<string>? progress,
        IProgress<OpenAiCompatibleSegment>? segmentProgress,
        CancellationToken cancellationToken)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        if (_settings.TimeoutSeconds > 0)
        {
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(_settings.TimeoutSeconds));
        }
        var ct = timeoutCts.Token;

        try
        {
            var bytes = await File.ReadAllBytesAsync(audioFilePath, ct);
            var fileName = Path.GetFileName(audioFilePath);

            progress?.Report("Uploading audio to DashScope temporary storage...");
            _settings.Logger?.Invoke($"DashScope: uploading audio to temporary storage ({BaseUrl})");
            var ossUrl = await UploadFileAsync(bytes, fileName, ct);

            progress?.Report("Submitting transcription task...");
            _settings.Logger?.Invoke($"DashScope: submitting async task for {ossUrl}");
            var taskId = await SubmitTaskAsync(ossUrl, language, ct);

            progress?.Report("Waiting for transcription to complete...");
            var transcriptionUrl = await PollTaskAsync(taskId, ct);

            _settings.Logger?.Invoke($"DashScope: fetching result from {transcriptionUrl}");

            // ★Worth retrying even though the work is done: the task has SUCCEEDED and been billed
            //   by this point, so losing the download throws away a transcription already paid for.
            var resultJson = await SendWithRetryAsync(
                () => new HttpRequestMessage(HttpMethod.Get, transcriptionUrl), "result download", ct);

            return ParseTranscriptionResult(resultJson);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // Our own timeout fired, not a user cancel — surface it as an error
            // so the caller doesn't mistake it for cancellation.
            throw new TimeoutException($"DashScope transcription timed out after {_settings.TimeoutSeconds} seconds.");
        }
    }

    /// <summary>
    /// Fork addition: send one request, retrying the transient failures, and return its body.
    ///
    /// ★Per REQUEST, not per transcription. Retrying <see cref="TranscribeAsync"/> as a whole would
    ///   re-upload the audio and submit a second task - and the provider bills per audio second, so
    ///   a retry that reached the polling stage would pay for the same film twice. Every request in
    ///   this flow is built from data already in memory, so each one can simply be made again.
    ///
    /// ★<paramref name="newRequest"/> is a factory rather than a message because an
    ///   <see cref="HttpRequestMessage"/> cannot be sent twice. Building it fresh also rebuilds the
    ///   body, which is what makes even the multipart upload replayable - the bytes are a
    ///   <c>byte[]</c> the caller still holds, so there is no stream to rewind.
    ///
    /// ★Retries spend the caller's deadline rather than getting a fresh one each. The token here is
    ///   already <see cref="TranscribeAsync"/>'s timeout-linked one, and it is the whole film's
    ///   budget - a 429 that keeps being answered slowly should end in the timeout the user set,
    ///   not extend past it four times over.
    /// </summary>
    private async Task<string> SendWithRetryAsync(
        Func<HttpRequestMessage> newRequest, string what, CancellationToken ct)
    {
        for (var attempt = 0; ; attempt++)
        {
            HttpRequestException failure;
            try
            {
                using var request = newRequest();
                using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
                var body = await response.Content.ReadAsStringAsync(ct);
                if (response.IsSuccessStatusCode)
                {
                    return body;
                }

                failure = new HttpRequestException(
                    $"DashScope {what} failed ({(int)response.StatusCode}). Response: {body}",
                    null,
                    response.StatusCode);

                // The provider knows when its quota window rolls over; carry that to the policy.
                if (SttRetryPolicy.ReadRetryAfterSeconds(response) is { } retryAfterSeconds)
                {
                    failure.Data[SttRetryPolicy.RetryAfterKey] = retryAfterSeconds;
                }
            }
            catch (HttpRequestException exception)
            {
                // Never got an answer - a reset connection or a DNS blip. The policy calls a
                // status-less failure replayable, so it lands in the same loop.
                //
                // ★Rewrapped rather than passed along, so every failure leaving this method names
                //   the stage it happened at. Raw transport messages do not ("The SSL connection
                //   could not be established" says nothing about which of the five requests it was),
                //   and BatchRunner prints exactly this text beside the film that failed. The status
                //   code is carried across so the policy still sees what it saw.
                failure = new HttpRequestException(
                    $"DashScope {what} failed: {exception.Message}", exception, exception.StatusCode);
            }

            if (attempt >= SttRetryPolicy.MaxTransientRetries || !SttRetryPolicy.IsRetryable(failure))
            {
                _settings.Logger?.Invoke(failure.Message);
                throw failure;
            }

            var delay = SttRetryPolicy.GetRetryDelay(failure, attempt);
            _settings.Logger?.Invoke(SttRetryPolicy.DescribeRetry(
                $"DashScope {what} {SttRetryPolicy.DescribeFailure(failure)}", delay, attempt));

            await Task.Delay(delay, ct);
        }
    }

    /// <summary>
    /// Upload the audio to DashScope temporary storage and return its
    /// <c>oss://</c> URL. Two steps: fetch an upload policy, then POST the file
    /// to the returned OSS host with the signed form fields (file field last).
    /// </summary>
    private async Task<string> UploadFileAsync(byte[] audioBytes, string fileName, CancellationToken ct)
    {
        var model = string.IsNullOrWhiteSpace(_settings.Model) ? "qwen3-asr-flash-filetrans" : _settings.Model;
        var policyUrl = $"{BaseUrl}{UploadsPath}?action=getPolicy&model={Uri.EscapeDataString(model)}";
        var policyJson = await SendWithRetryAsync(
            () =>
            {
                var request = new HttpRequestMessage(HttpMethod.Get, policyUrl);
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _settings.ApiKey);
                return request;
            },
            $"upload-policy request (GET {policyUrl})",
            ct);

        var policy = JsonSerializer.Deserialize<DashScopeUploadPolicyResponse>(policyJson,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })?.Data;
        if (policy == null)
        {
            _settings.Logger?.Invoke($"DashScope upload-policy response could not be parsed: {policyJson}");
            throw new InvalidOperationException($"DashScope upload-policy response could not be parsed. Response: {policyJson}");
        }

        var key = $"{policy.UploadDir}/{fileName}";

        // ★The form is rebuilt per attempt, which is the only reason a 45 MB upload can be retried
        //   at all: audioBytes is already in memory, so a replay costs bandwidth but needs no
        //   rewindable stream. An expired policy answers 403, which the policy calls non-retryable,
        //   so a signature that has gone stale fails once instead of three more times.
        await SendWithRetryAsync(
            () => new HttpRequestMessage(HttpMethod.Post, policy.UploadHost)
            {
                Content = BuildOssUploadForm(policy, key, fileName, audioBytes),
            },
            $"OSS upload (POST {policy.UploadHost})",
            ct);

        return "oss://" + key;
    }

    private async Task<string> SubmitTaskAsync(string ossUrl, string? language, CancellationToken ct)
    {
        var body = BuildSubmitBody(_settings, ossUrl, language);

        // ★This is where a quota rejection actually lands: the account's concurrent-task limit is
        //   spent at submit time, not at upload time. Losing it used to fail the whole film.
        var json = await SendWithRetryAsync(
            () =>
            {
                var request = new HttpRequestMessage(HttpMethod.Post, BaseUrl + TranscriptionPath)
                {
                    Content = new StringContent(body, Encoding.UTF8, "application/json"),
                };
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _settings.ApiKey);
                request.Headers.TryAddWithoutValidation("X-DashScope-Async", "enable");
                request.Headers.TryAddWithoutValidation("X-DashScope-OssResourceResolve", "enable");
                return request;
            },
            "async submit",
            ct);

        var taskId = JsonSerializer.Deserialize<DashScopeTaskResponse>(json,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })?.Output?.TaskId;
        if (string.IsNullOrEmpty(taskId))
        {
            throw new InvalidOperationException($"DashScope async submit returned no task_id. Response: {json}");
        }

        return taskId;
    }

    private async Task<string> PollTaskAsync(string taskId, CancellationToken ct)
    {
        var url = BaseUrl + TasksPath + Uri.EscapeDataString(taskId);
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

        while (true)
        {
            ct.ThrowIfCancellationRequested();

            // ★The retry budget is per poll, so it refreshes on every pass of this loop. That is
            //   deliberate: a film transcribing for ten minutes polls two hundred times, and one
            //   429 in the middle says nothing about the next one. A budget shared across the whole
            //   poll would spend itself on the first bad minute and abandon a task that is still
            //   running - and the provider has already billed for it by then.
            var json = await SendWithRetryAsync(
                () =>
                {
                    var request = new HttpRequestMessage(HttpMethod.Get, url);
                    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _settings.ApiKey);
                    return request;
                },
                "task poll",
                ct);

            var task = JsonSerializer.Deserialize<DashScopeTaskResponse>(json, options);
            var output = task?.Output;

            // ★Fork addition. The two families report the billed amount under DIFFERENT keys -
            //   fun-asr as usage.duration, qwen3-asr-flash as usage.seconds - and a probe that
            //   looked only for "seconds" concluded fun-asr reported nothing at all. It does.
            //   Worth carrying because the figure is not the file's length: a 600 s clip billed
            //   333 s, so it counts detected speech and only a real reading tells you the cost.
            var billed = task?.Usage?.Duration ?? task?.Usage?.Seconds;
            if (billed is > 0)
            {
                LastBilledSeconds = billed.Value;
            }

            var status = output?.TaskStatus ?? "UNKNOWN";
            switch (status.ToUpperInvariant())
            {
                case "SUCCEEDED":
                    var transcriptionUrl = output?.Result?.TranscriptionUrl
                        ?? (output?.Results != null && output.Results.Count > 0 ? output.Results[0].TranscriptionUrl : null);
                    if (string.IsNullOrEmpty(transcriptionUrl))
                    {
                        throw new InvalidOperationException($"DashScope task succeeded but returned no transcription_url. Response: {json}");
                    }
                    return transcriptionUrl;

                case "FAILED":
                case "UNKNOWN":
                    throw new InvalidOperationException($"DashScope transcription task {status}. Response: {json}");

                default: // QUEUED, PENDING, PROCESSING
                    await Task.Delay(TimeSpan.FromSeconds(3), ct);
                    break;
            }
        }
    }

    /// <summary>
    /// Serialize the async submit body. <c>enable_words</c> adds word-level
    /// timings; a fixed language is sent only when the user set one.
    /// </summary>
    /// <summary>
    /// Builds the OSS PostObject body.
    ///
    /// ★This is its own method because getting it wrong is invisible: the default
    ///   <see cref="MultipartFormDataContent"/> encoding is rejected with
    ///   <c>MalformedPOSTRequest</c>, which is how the DashScope engine came to fail at its very
    ///   first upload and therefore never transcribe anything. Three rules, each probed against the
    ///   live bucket rather than guessed:
    ///     ①every part name must be QUOTED - .NET writes <c>name=policy</c> for simple names
    ///     ②the boundary must NOT be quoted - .NET writes <c>boundary="…"</c> in Content-Type
    ///     ③the file part carries no Content-Type - OSS wants Content-Disposition first when both
    ///       are present, and <see cref="System.Net.Http.Headers.HttpContentHeaders"/> gives no way
    ///       to order them
    ///   Measured on ①②: bare+quoted 400, quoted+quoted 400, bare+bare 400, quoted+bare 200.
    ///   Measured on ③: Disposition-then-Type 200, Type-then-Disposition 400, no Content-Type 200.
    /// </summary>
    internal static MultipartFormDataContent BuildOssUploadForm(
        DashScopeUploadPolicy policy, string key, string fileName, byte[] audioBytes)
    {
        var boundary = Guid.NewGuid().ToString("N");
        var form = new MultipartFormDataContent(boundary);
        AddQuotedField(form, "OSSAccessKeyId", policy.OssAccessKeyId);
        AddQuotedField(form, "policy", policy.Policy);
        AddQuotedField(form, "Signature", policy.Signature);
        AddQuotedField(form, "key", key);
        AddQuotedField(form, "x-oss-object-acl", policy.XOssObjectAcl);
        AddQuotedField(form, "x-oss-forbid-overwrite", policy.XOssForbidOverwrite);
        AddQuotedField(form, "success_action_status", "200");

        var fileContent = new ByteArrayContent(audioBytes);
        fileContent.Headers.ContentDisposition = new ContentDispositionHeaderValue("form-data")
        {
            Name = "\"file\"",
            FileName = "\"" + fileName + "\"",
        };
        form.Add(fileContent); // file field must be last

        form.Headers.Remove("Content-Type");
        form.Headers.TryAddWithoutValidation("Content-Type", "multipart/form-data; boundary=" + boundary);
        return form;
    }

    /// <summary>
    /// Adds one OSS PostObject form field with its name explicitly quoted.
    /// ★<see cref="ContentDispositionHeaderValue.Name"/> leaves an already-quoted value alone, so
    ///   passing the quotes in is what forces <c>name="policy"</c> instead of <c>name=policy</c>.
    ///   Also drops the <c>text/plain</c> Content-Type StringContent would otherwise attach.
    /// </summary>
    private static void AddQuotedField(MultipartFormDataContent form, string name, string value)
    {
        var content = new StringContent(value);
        content.Headers.ContentType = null;
        content.Headers.ContentDisposition = new ContentDispositionHeaderValue("form-data")
        {
            Name = "\"" + name + "\"",
        };
        form.Add(content);
    }

    /// <summary>
    /// Whether <paramref name="model"/> belongs to the Fun-ASR / Paraformer family, which speaks a
    /// different dialect of this same endpoint.
    ///
    /// ★Not cosmetic. Sending the Qwen shape to Fun-ASR fails outright with
    ///   <c>InvalidParameter: input must contain file_urls</c>, and the two families also disagree
    ///   on the language field. Verified against the live API for both.
    /// </summary>
    internal static bool IsFunAsrFamily(string? model)
    {
        var m = (model ?? string.Empty).Trim();
        return m.StartsWith("fun-asr", StringComparison.OrdinalIgnoreCase)
            || m.StartsWith("paraformer", StringComparison.OrdinalIgnoreCase);
    }

    internal static string BuildSubmitBody(DashScopeSttSettings settings, string fileUrl, string? language)
    {
        var languageToUse = language ?? settings.Language;
        var model = string.IsNullOrWhiteSpace(settings.Model) ? "qwen3-asr-flash-filetrans" : settings.Model;
        var funAsr = IsFunAsrFamily(model);

        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteString("model", model);

            writer.WritePropertyName("input");
            writer.WriteStartObject();
            if (funAsr)
            {
                // Fun-ASR takes a batch even for one file, and answers with output.results[] to
                // match - which PollTaskAsync already reads alongside the singular shape.
                writer.WritePropertyName("file_urls");
                writer.WriteStartArray();
                writer.WriteStringValue(fileUrl);
                writer.WriteEndArray();
            }
            else
            {
                writer.WriteString("file_url", fileUrl);
            }

            writer.WriteEndObject();

            writer.WritePropertyName("parameters");
            writer.WriteStartObject();

            // ★enable_itn is NOT sent, and its absence is the measured position rather than an
            //   oversight. It used to go out hardcoded false. Probed live on the same 180 s clip
            //   with the flag true, false and omitted: fun-asr returned the same text and the same
            //   Arabic digits in all three, and qwen3-asr-flash-filetrans scored 1.0000 similarity
            //   true-vs-false. Neither family reads it. It is also absent from the documented
            //   parameter table for both. Note that this endpoint IGNORES unknown parameters
            //   silently - a deliberately invented one was accepted and ran - so "the request
            //   succeeded" was never evidence that it did anything.
            //
            // ★enable_words, by contrast, stays and matters - for ONE of the two families:
            //     fun-asr : no-op. Word timings, with per-word confidence, come back in EVERY
            //               response whether the flag is true, false or omitted (16 probe arms).
            //     qwen3   : load-bearing, and not only for timestamps. False returns 10 sentences
            //               with no words; true returns 55 sentences all carrying words - the same
            //               628 characters of text, cut into 5.5x as many cues. Deterministic over
            //               3 runs each. Sending false to that model yields ~18-second cues.
            writer.WriteBoolean("enable_words", settings.EnableWords);

            if (!string.IsNullOrWhiteSpace(languageToUse) && languageToUse != "auto")
            {
                if (funAsr)
                {
                    writer.WritePropertyName("language_hints");
                    writer.WriteStartArray();
                    writer.WriteStringValue(languageToUse);
                    writer.WriteEndArray();
                }
                else
                {
                    writer.WriteString("language", languageToUse);
                }
            }

            // ★Both of these are Fun-ASR-only per the model table, and both are silently ignored
            //   rather than rejected elsewhere - so gate them here instead of letting a stale
            //   setting travel with a Qwen request and look like it did something.
            if (funAsr && !string.IsNullOrWhiteSpace(settings.VocabularyId))
            {
                writer.WriteString("vocabulary_id", settings.VocabularyId.Trim());
            }

            if (funAsr && settings.SpeakerCount > 0)
            {
                writer.WriteBoolean("diarization_enabled", true);
                writer.WriteNumber("speaker_count", settings.SpeakerCount);
            }

            writer.WriteEndObject();

            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    /// <summary>
    /// Map a DashScope transcription-result JSON into the shared response shape.
    /// Each sentence (millisecond begin/end) becomes one timed segment.
    /// </summary>
    internal static OpenAiCompatibleSttResponse ParseTranscriptionResult(string json)
    {
        var segments = new List<OpenAiCompatibleSegment>();
        var fullText = new StringBuilder();

        try
        {
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var result = JsonSerializer.Deserialize<DashScopeTranscriptionResult>(json, options);
            var transcripts = result?.Transcripts;
            if (transcripts != null)
            {
                var id = 0;
                foreach (var transcript in transcripts)
                {
                    if (transcript.Sentences == null)
                    {
                        continue;
                    }

                    foreach (var sentence in transcript.Sentences)
                    {
                        var text = (sentence.Text ?? string.Empty).Trim();
                        if (text.Length == 0)
                        {
                            continue;
                        }

                        segments.Add(new OpenAiCompatibleSegment
                        {
                            Id = id++,
                            Start = sentence.BeginTime / 1000.0,
                            End = sentence.EndTime / 1000.0,
                            Text = text,
                        });

                        if (fullText.Length > 0)
                        {
                            fullText.Append(' ');
                        }
                        fullText.Append(text);
                    }
                }
            }
        }
        catch (JsonException)
        {
            // Leave segments empty; caller falls back to chunk-spanning text.
        }

        return new OpenAiCompatibleSttResponse
        {
            Text = fullText.ToString().Trim(),
            Segments = segments,
            Duration = segments.Count > 0 ? segments[^1].End : 0,
        };
    }

}

public class DashScopeSttSettings
{
    public string ApiKey { get; set; } = string.Empty;
    public string Model { get; set; } = "qwen3-asr-flash-filetrans";
    public string Language { get; set; } = string.Empty;
    public string Region { get; set; } = "international";
    public bool EnableWords { get; set; }
    public int TimeoutSeconds { get; set; } = 3600;

    /// <summary>Hotword list id from the speech-biasing API. Fun-ASR / Paraformer only.</summary>
    public string VocabularyId { get; set; } = string.Empty;

    /// <summary>Speakers to separate, or 0 for no diarization. Fun-ASR offline only.</summary>
    public int SpeakerCount { get; set; }

    public Action<string>? Logger { get; set; }
}

// --- Upload policy (getPolicy) ---
internal class DashScopeUploadPolicyResponse
{
    [JsonPropertyName("data")]
    public DashScopeUploadPolicy? Data { get; set; }
}

internal class DashScopeUploadPolicy
{
    [JsonPropertyName("policy")]
    public string Policy { get; set; } = string.Empty;

    [JsonPropertyName("signature")]
    public string Signature { get; set; } = string.Empty;

    [JsonPropertyName("upload_dir")]
    public string UploadDir { get; set; } = string.Empty;

    [JsonPropertyName("upload_host")]
    public string UploadHost { get; set; } = string.Empty;

    [JsonPropertyName("oss_access_key_id")]
    public string OssAccessKeyId { get; set; } = string.Empty;

    [JsonPropertyName("x_oss_object_acl")]
    public string XOssObjectAcl { get; set; } = string.Empty;

    [JsonPropertyName("x_oss_forbid_overwrite")]
    public string XOssForbidOverwrite { get; set; } = string.Empty;
}

// --- Async submit / poll ---
internal class DashScopeTaskResponse
{
    [JsonPropertyName("output")]
    public DashScopeTaskOutput? Output { get; set; }

    [JsonPropertyName("usage")]
    public DashScopeUsage? Usage { get; set; }
}

/// <summary>
/// What the provider says it billed. ★Two keys for one thing: fun-asr answers with
/// <c>duration</c>, qwen3-asr-flash with <c>seconds</c>. Reading only one of them makes the other
/// family look like it reports nothing.
/// </summary>
internal class DashScopeUsage
{
    [JsonPropertyName("duration")]
    public double? Duration { get; set; }

    [JsonPropertyName("seconds")]
    public double? Seconds { get; set; }
}

internal class DashScopeTaskOutput
{
    [JsonPropertyName("task_id")]
    public string? TaskId { get; set; }

    [JsonPropertyName("task_status")]
    public string? TaskStatus { get; set; }

    [JsonPropertyName("result")]
    public DashScopeTaskResult? Result { get; set; }

    [JsonPropertyName("results")]
    public List<DashScopeTaskResult>? Results { get; set; }
}

internal class DashScopeTaskResult
{
    [JsonPropertyName("transcription_url")]
    public string? TranscriptionUrl { get; set; }
}

// --- transcription_url result JSON ---
internal class DashScopeTranscriptionResult
{
    [JsonPropertyName("transcripts")]
    public List<DashScopeTranscript>? Transcripts { get; set; }
}

internal class DashScopeTranscript
{
    [JsonPropertyName("text")]
    public string? Text { get; set; }

    [JsonPropertyName("sentences")]
    public List<DashScopeSentence>? Sentences { get; set; }
}

internal class DashScopeSentence
{
    [JsonPropertyName("begin_time")]
    public double BeginTime { get; set; }

    [JsonPropertyName("end_time")]
    public double EndTime { get; set; }

    [JsonPropertyName("text")]
    public string? Text { get; set; }
}
