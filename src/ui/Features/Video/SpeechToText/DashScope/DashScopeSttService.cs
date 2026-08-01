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
using Nikse.SubtitleEdit.Logic.Config;

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

    private readonly HttpClient _httpClient;
    private readonly DashScopeSttSettings _settings;

    public DashScopeSttService(DashScopeSttSettings settings)
        : this(OpenAiSttService.SharedHttpClient, settings)
    {
    }

    public DashScopeSttService(HttpClient httpClient, DashScopeSttSettings settings)
    {
        _httpClient = httpClient;
        _settings = settings;
    }

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
            using var resultResponse = await _httpClient.GetAsync(transcriptionUrl, HttpCompletionOption.ResponseHeadersRead, ct);
            resultResponse.EnsureSuccessStatusCode();
            var resultJson = await resultResponse.Content.ReadAsStringAsync(ct);

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
    /// Upload the audio to DashScope temporary storage and return its
    /// <c>oss://</c> URL. Two steps: fetch an upload policy, then POST the file
    /// to the returned OSS host with the signed form fields (file field last).
    /// </summary>
    private async Task<string> UploadFileAsync(byte[] audioBytes, string fileName, CancellationToken ct)
    {
        var model = string.IsNullOrWhiteSpace(_settings.Model) ? "qwen3-asr-flash-filetrans" : _settings.Model;
        var policyUrl = $"{BaseUrl}{UploadsPath}?action=getPolicy&model={Uri.EscapeDataString(model)}";
        using var policyRequest = new HttpRequestMessage(HttpMethod.Get, policyUrl);
        policyRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _settings.ApiKey);

        using var policyResponse = await _httpClient.SendAsync(policyRequest, HttpCompletionOption.ResponseHeadersRead, ct);
        if (!policyResponse.IsSuccessStatusCode)
        {
            var err = await policyResponse.Content.ReadAsStringAsync(ct);
            _settings.Logger?.Invoke($"DashScope upload-policy request failed: GET {policyUrl} => {(int)policyResponse.StatusCode}: {err}");
            throw new HttpRequestException($"DashScope upload-policy request failed ({(int)policyResponse.StatusCode}). Response: {err}");
        }

        var policyJson = await policyResponse.Content.ReadAsStringAsync(ct);
        var policy = JsonSerializer.Deserialize<DashScopeUploadPolicyResponse>(policyJson,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })?.Data;
        if (policy == null)
        {
            _settings.Logger?.Invoke($"DashScope upload-policy response could not be parsed: {policyJson}");
            throw new InvalidOperationException($"DashScope upload-policy response could not be parsed. Response: {policyJson}");
        }

        var key = $"{policy.UploadDir}/{fileName}";

        using var form = BuildOssUploadForm(policy, key, fileName, audioBytes);
        using var uploadResponse = await _httpClient.PostAsync(policy.UploadHost, form, ct);
        if (!uploadResponse.IsSuccessStatusCode)
        {
            var err = await uploadResponse.Content.ReadAsStringAsync(ct);
            _settings.Logger?.Invoke($"DashScope OSS upload failed: POST {policy.UploadHost} => {(int)uploadResponse.StatusCode}: {err}");
            throw new HttpRequestException($"DashScope OSS upload failed ({(int)uploadResponse.StatusCode}). Response: {err}");
        }

        return "oss://" + key;
    }

    private async Task<string> SubmitTaskAsync(string ossUrl, string? language, CancellationToken ct)
    {
        var body = BuildSubmitBody(_settings, ossUrl, language);
        using var content = new StringContent(body, Encoding.UTF8, "application/json");
        using var request = new HttpRequestMessage(HttpMethod.Post, BaseUrl + TranscriptionPath)
        {
            Content = content,
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _settings.ApiKey);
        request.Headers.TryAddWithoutValidation("X-DashScope-Async", "enable");
        request.Headers.TryAddWithoutValidation("X-DashScope-OssResourceResolve", "enable");

        using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        var json = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
        {
            _settings.Logger?.Invoke($"DashScope async submit failed ({(int)response.StatusCode}): {json}");
            throw new HttpRequestException($"DashScope async submit failed ({(int)response.StatusCode}). Response: {json}");
        }

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

            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _settings.ApiKey);

            using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
            var json = await response.Content.ReadAsStringAsync(ct);
            if (!response.IsSuccessStatusCode)
            {
                throw new HttpRequestException($"DashScope task poll failed ({(int)response.StatusCode}). Response: {json}");
            }

            var output = JsonSerializer.Deserialize<DashScopeTaskResponse>(json, options)?.Output;
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
            writer.WriteBoolean("enable_itn", false);
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

    public static DashScopeSttSettings GetSettingsFromConfiguration()
    {
        var tools = Se.Settings.Tools;
        return new DashScopeSttSettings
        {
            ApiKey = tools.DashScopeSttApiKey,
            Model = tools.DashScopeSttModel,
            Language = tools.DashScopeSttLanguage,
            Region = tools.DashScopeSttRegion,
            EnableWords = tools.DashScopeSttEnableWords,
            TimeoutSeconds = tools.DashScopeSttTimeoutSeconds,
            VocabularyId = tools.DashScopeSttVocabularyId,
            SpeakerCount = tools.DashScopeSttSpeakerCount,
            Logger = Se.WriteToolsLog,
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
