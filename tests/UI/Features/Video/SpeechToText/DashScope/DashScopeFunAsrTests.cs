using System.Text;
using Nikse.SubtitleEdit.Features.Video.SpeechToText.DashScope;

namespace UITests.Features.Video.SpeechToText.DashScope;

/// <summary>
/// Fork addition. Two things this file protects, both of which fail silently if they regress.
///
/// ★The OSS PostObject encoding. .NET's default multipart output is rejected with
///   MalformedPOSTRequest, so the DashScope engine used to fail at its first upload and never
///   transcribe anything. Each rule below was isolated by probing the live bucket.
///
/// ★The two request dialects. Fun-ASR answers the same endpoint but rejects the Qwen shape
///   outright ("input must contain file_urls"), and the families disagree on the language field.
/// </summary>
public class DashScopeFunAsrTests
{
    private static DashScopeUploadPolicy MakePolicy() => new()
    {
        Policy = "cG9saWN5",
        Signature = "sig+with/slash=",
        UploadDir = "dashscope-instant/abc/2026-08-02/xyz",
        UploadHost = "https://example.oss-ap-southeast-1.aliyuncs.com",
        OssAccessKeyId = "LTAI-test",
        XOssObjectAcl = "private",
        XOssForbidOverwrite = "true",
    };

    private static string Serialize(MultipartFormDataContent form)
        => Encoding.UTF8.GetString(form.ReadAsByteArrayAsync().GetAwaiter().GetResult());

    // ①Every part name quoted. .NET writes `name=policy` for simple names, which OSS rejects.
    [Fact]
    public void UploadForm_QuotesEveryFieldName()
    {
        using var form = DashScopeSttService.BuildOssUploadForm(MakePolicy(), "dir/a.mp3", "a.mp3", [1, 2, 3]);
        var body = Serialize(form);

        foreach (var name in new[]
                 {
                     "OSSAccessKeyId", "policy", "Signature", "key",
                     "x-oss-object-acl", "x-oss-forbid-overwrite", "success_action_status", "file",
                 })
        {
            Assert.Contains($"name=\"{name}\"", body);
            Assert.DoesNotContain($"name={name}\r\n", body);
            Assert.DoesNotContain($"name={name};", body);
        }
    }

    // ②The boundary must NOT be quoted. .NET writes boundary="…", which OSS rejects.
    [Fact]
    public void UploadForm_LeavesTheBoundaryUnquoted()
    {
        using var form = DashScopeSttService.BuildOssUploadForm(MakePolicy(), "dir/a.mp3", "a.mp3", [1, 2, 3]);
        var contentType = string.Join(";", form.Headers.GetValues("Content-Type"));

        Assert.StartsWith("multipart/form-data; boundary=", contentType);
        Assert.DoesNotContain("boundary=\"", contentType);
    }

    // ③No Content-Type on the file part: OSS wants Content-Disposition first when both are
    //   present and HttpContentHeaders cannot order them, so the safe move is to omit it.
    [Fact]
    public void UploadForm_SendsNoContentTypeOnTheFilePart()
    {
        using var form = DashScopeSttService.BuildOssUploadForm(MakePolicy(), "dir/a.mp3", "a.mp3", [1, 2, 3]);
        var body = Serialize(form);

        Assert.Contains("name=\"file\"; filename=\"a.mp3\"", body);
        Assert.DoesNotContain("application/octet-stream", body);
        // The text fields must not carry StringContent's default text/plain either.
        Assert.DoesNotContain("text/plain", body);
    }

    [Fact]
    public void UploadForm_PutsTheFileFieldLast()
    {
        using var form = DashScopeSttService.BuildOssUploadForm(MakePolicy(), "dir/a.mp3", "a.mp3", [1, 2, 3]);
        var body = Serialize(form);

        Assert.True(body.IndexOf("name=\"file\"", StringComparison.Ordinal)
                    > body.IndexOf("name=\"success_action_status\"", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("fun-asr", true)]
    [InlineData("fun-asr-mtl", true)]
    [InlineData("paraformer-v2", true)]
    [InlineData("qwen3-asr-flash-filetrans", false)]
    [InlineData("qwen3-asr-flash", false)]
    [InlineData("", false)]
    public void IsFunAsrFamily_SplitsTheTwoDialects(string model, bool expected)
        => Assert.Equal(expected, DashScopeSttService.IsFunAsrFamily(model));

    // Sending the Qwen shape to Fun-ASR fails with "input must contain file_urls" - verified live.
    [Fact]
    public void SubmitBody_FunAsrTakesAnArrayAndLanguageHints()
    {
        var body = DashScopeSttService.BuildSubmitBody(
            new DashScopeSttSettings { Model = "fun-asr" }, "oss://bucket/a.mp3", "ja");

        Assert.Contains("\"file_urls\":[\"oss://bucket/a.mp3\"]", body);
        Assert.Contains("\"language_hints\":[\"ja\"]", body);
        Assert.DoesNotContain("\"file_url\"", body);
        Assert.DoesNotContain("\"language\":", body);
    }

    [Fact]
    public void SubmitBody_QwenKeepsTheSingularShape()
    {
        var body = DashScopeSttService.BuildSubmitBody(
            new DashScopeSttSettings { Model = "qwen3-asr-flash-filetrans" }, "oss://bucket/a.mp3", "ja");

        Assert.Contains("\"file_url\":\"oss://bucket/a.mp3\"", body);
        Assert.Contains("\"language\":\"ja\"", body);
        Assert.DoesNotContain("file_urls", body);
        Assert.DoesNotContain("language_hints", body);
    }

    [Fact]
    public void SubmitBody_CarriesHotwordsAndDiarizationForFunAsr()
    {
        var body = DashScopeSttService.BuildSubmitBody(
            new DashScopeSttSettings { Model = "fun-asr", VocabularyId = " vocab-jav-1 ", SpeakerCount = 2 },
            "oss://x", "ja");

        Assert.Contains("\"vocabulary_id\":\"vocab-jav-1\"", body);
        Assert.Contains("\"diarization_enabled\":true", body);
        Assert.Contains("\"speaker_count\":2", body);
    }

    // ★Gated rather than sent-and-ignored: hotwords and diarization are documented as Fun-ASR-only,
    //   and a stale setting travelling with a Qwen request would look like it had done something.
    [Fact]
    public void SubmitBody_DropsFunAsrOnlyOptionsForQwen()
    {
        var body = DashScopeSttService.BuildSubmitBody(
            new DashScopeSttSettings { Model = "qwen3-asr-flash-filetrans", VocabularyId = "vocab-jav-1", SpeakerCount = 2 },
            "oss://x", "ja");

        Assert.DoesNotContain("vocabulary_id", body);
        Assert.DoesNotContain("diarization_enabled", body);
        Assert.DoesNotContain("speaker_count", body);
    }

    [Fact]
    public void SubmitBody_OmitsDiarizationWhenNoSpeakerCountIsSet()
    {
        var body = DashScopeSttService.BuildSubmitBody(
            new DashScopeSttSettings { Model = "fun-asr", SpeakerCount = 0 }, "oss://x", "ja");

        Assert.DoesNotContain("diarization_enabled", body);
    }

    [Fact]
    public void SubmitBody_LeavesLanguageOutWhenAutoDetecting()
    {
        var funAsr = DashScopeSttService.BuildSubmitBody(new DashScopeSttSettings { Model = "fun-asr" }, "oss://x", "auto");
        var qwen = DashScopeSttService.BuildSubmitBody(new DashScopeSttSettings { Model = "qwen3-asr-flash-filetrans" }, "oss://x", "auto");

        Assert.DoesNotContain("language_hints", funAsr);
        Assert.DoesNotContain("\"language\":", qwen);
    }
}
