using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using Nikse.SubtitleEdit.Features.Video.SpeechToText;

namespace UITests.Features.Video.SpeechToText;

/// <summary>
/// The shared online-STT retry policy. These began as OpenAiSttServiceTests cases and moved here
/// when DashScope started using the same policy - they now cover both engines at once, and a
/// change that suits one provider but not the other fails here rather than in one engine's tests.
/// </summary>
public class SttRetryPolicyTests
{
    [Fact]
    public void IsRetryable_CoversTransientStatusesOnly()
    {
        Assert.True(SttRetryPolicy.IsRetryable(new HttpRequestException("x", null, HttpStatusCode.TooManyRequests)));
        Assert.True(SttRetryPolicy.IsRetryable(new HttpRequestException("x", null, HttpStatusCode.InternalServerError)));
        Assert.True(SttRetryPolicy.IsRetryable(new HttpRequestException("x", null, HttpStatusCode.BadGateway)));
        Assert.True(SttRetryPolicy.IsRetryable(new HttpRequestException("x", null, HttpStatusCode.ServiceUnavailable)));
        Assert.True(SttRetryPolicy.IsRetryable(new HttpRequestException("x", null, HttpStatusCode.GatewayTimeout)));
        Assert.True(SttRetryPolicy.IsRetryable(new HttpRequestException("x", null, HttpStatusCode.RequestTimeout)));

        // No status at all means the request never got an answer - replayable.
        Assert.True(SttRetryPolicy.IsRetryable(new HttpRequestException("connection reset")));

        Assert.False(SttRetryPolicy.IsRetryable(new HttpRequestException("x", null, HttpStatusCode.BadRequest)));
        Assert.False(SttRetryPolicy.IsRetryable(new HttpRequestException("x", null, HttpStatusCode.Unauthorized)));
        Assert.False(SttRetryPolicy.IsRetryable(new HttpRequestException("x", null, HttpStatusCode.NotFound)));
        // The chunk-too-big case: retrying an oversized upload just spends the quota again.
        Assert.False(SttRetryPolicy.IsRetryable(new HttpRequestException("x", null, HttpStatusCode.RequestEntityTooLarge)));
    }

    /// <summary>
    /// ★403 must stay non-retryable, and DashScope is why it is worth stating twice. Its OSS
    ///   upload policy expires, and an expired signature answers 403 on every attempt - so a
    ///   retry there would spend three more 45 MB uploads to reach the same error.
    /// </summary>
    [Fact]
    public void IsRetryable_RejectsAnExpiredUploadSignature()
    {
        Assert.False(SttRetryPolicy.IsRetryable(new HttpRequestException("x", null, HttpStatusCode.Forbidden)));
    }

    // ★The server knows when its quota window rolls over; a guess would either come back too
    //   early and burn an attempt, or wait far longer than needed.
    [Fact]
    public void GetRetryDelay_PrefersRetryAfterOverBackoff()
    {
        var ex = new HttpRequestException("x", null, HttpStatusCode.TooManyRequests);
        ex.Data[SttRetryPolicy.RetryAfterKey] = 42.0;

        Assert.Equal(TimeSpan.FromSeconds(42), SttRetryPolicy.GetRetryDelay(ex, 0));
    }

    [Fact]
    public void GetRetryDelay_CapsBothPathsSoTheRunNeverLooksHung()
    {
        var withHugeRetryAfter = new HttpRequestException("x", null, HttpStatusCode.TooManyRequests);
        withHugeRetryAfter.Data[SttRetryPolicy.RetryAfterKey] = 86_400.0;
        Assert.Equal(SttRetryPolicy.MaxRetryDelay, SttRetryPolicy.GetRetryDelay(withHugeRetryAfter, 0));

        var plain = new HttpRequestException("x", null, HttpStatusCode.InternalServerError);
        Assert.Equal(TimeSpan.FromSeconds(2), SttRetryPolicy.GetRetryDelay(plain, 0));
        Assert.Equal(TimeSpan.FromSeconds(8), SttRetryPolicy.GetRetryDelay(plain, 1));
        Assert.True(SttRetryPolicy.GetRetryDelay(plain, 9) <= SttRetryPolicy.MaxRetryDelay);
    }

    [Fact]
    public void ReadRetryAfterSeconds_HandlesBothHeaderShapes()
    {
        using var delta = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
        delta.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(30));
        Assert.Equal(30, SttRetryPolicy.ReadRetryAfterSeconds(delta));

        using var date = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
        date.Headers.RetryAfter = new RetryConditionHeaderValue(DateTimeOffset.UtcNow.AddMinutes(2));
        Assert.True(SttRetryPolicy.ReadRetryAfterSeconds(date) > 60);

        // A date already in the past must not become a negative wait.
        using var past = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
        past.Headers.RetryAfter = new RetryConditionHeaderValue(DateTimeOffset.UtcNow.AddMinutes(-5));
        Assert.Null(SttRetryPolicy.ReadRetryAfterSeconds(past));

        using var none = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
        Assert.Null(SttRetryPolicy.ReadRetryAfterSeconds(none));
    }

    /// <summary>
    /// The log line a person actually reads. ★Counts from the first try, so the number in the
    /// message is one ahead of the 0-based attempt index - getting this backwards makes a run
    /// that retried three times report "attempt 3 of 4" and look like it gave up early.
    /// </summary>
    [Fact]
    public void DescribeRetry_CountsTheFirstAttemptToo()
    {
        Assert.Equal(
            "DashScope submit HTTP 429 — retrying in 2s (attempt 2 of 4)",
            SttRetryPolicy.DescribeRetry("DashScope submit HTTP 429", TimeSpan.FromSeconds(2), 0));

        Assert.Equal(
            "STT network error — retrying in 32s (attempt 4 of 4)",
            SttRetryPolicy.DescribeRetry("STT network error", TimeSpan.FromSeconds(32), 2));
    }

    [Fact]
    public void DescribeFailure_NamesTheStatusOrSaysThereWasNone()
    {
        Assert.Equal("HTTP 429",
            SttRetryPolicy.DescribeFailure(new HttpRequestException("x", null, HttpStatusCode.TooManyRequests)));
        Assert.Equal("network error", SttRetryPolicy.DescribeFailure(new HttpRequestException("reset")));
    }
}
