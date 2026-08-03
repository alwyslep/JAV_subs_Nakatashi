using System;
using System.Net;
using System.Net.Http;

namespace Nikse.SubtitleEdit.Features.Video.SpeechToText;

/// <summary>
/// Fork addition. When an online STT request is worth repeating, and how long to wait first.
///
/// ★One policy for every online engine, in one place. It began inside <c>OpenAiSttService</c> and
///   moved here when DashScope needed the same thing: two copies of "which status codes are
///   transient" is exactly the kind of divergence that is invisible until the day they disagree.
///
/// ★Depends on nothing above <c>System.Net.Http</c> on purpose. That is what lets the DashScope
///   engine be compiled into the standalone javstt tool by linking the source - it links this file
///   too, and a policy that reached for application settings could not travel.
/// </summary>
internal static class SttRetryPolicy
{
    /// <summary>How many extra attempts a replayable request gets after the first one fails.</summary>
    internal const int MaxTransientRetries = 3;

    /// <summary>
    /// Ceiling on a single wait. ★A provider may ask for a very long pause when an hourly quota
    /// is spent; past a few minutes it is better to fail with a legible error than to look hung
    /// with no way to tell the difference.
    /// </summary>
    internal static readonly TimeSpan MaxRetryDelay = TimeSpan.FromMinutes(5);

    /// <summary>Key under which a caller stashes a Retry-After value on the exception.</summary>
    internal const string RetryAfterKey = "RetryAfterSeconds";

    /// <summary>
    /// Whether this failure is worth repeating verbatim.
    /// ★A null status means the request never got an answer (reset connection, DNS blip)
    ///   — replayable. Everything 4xx except 408/429 is the request's own fault and would
    ///   fail identically, so retrying it only delays the error the user needs to read.
    /// </summary>
    internal static bool IsRetryable(HttpRequestException exception)
    {
        if (exception.StatusCode is not { } status)
        {
            return true;
        }

        return status is HttpStatusCode.RequestTimeout
            or HttpStatusCode.TooManyRequests
            or HttpStatusCode.InternalServerError
            or HttpStatusCode.BadGateway
            or HttpStatusCode.ServiceUnavailable
            or HttpStatusCode.GatewayTimeout;
    }

    /// <summary>
    /// How long to wait before the next attempt.
    /// ★The server's own Retry-After wins when it sent one: on a quota rejection it knows when
    ///   the window rolls over and a guess would either come back too early (burning an attempt)
    ///   or wait far longer than needed. Otherwise back off exponentially from 2s.
    /// </summary>
    internal static TimeSpan GetRetryDelay(HttpRequestException exception, int attempt)
    {
        if (exception.Data[RetryAfterKey] is double seconds && seconds > 0)
        {
            return TimeSpan.FromSeconds(Math.Min(seconds, MaxRetryDelay.TotalSeconds));
        }

        var backoff = TimeSpan.FromSeconds(2 * Math.Pow(4, attempt));
        return backoff > MaxRetryDelay ? MaxRetryDelay : backoff;
    }

    /// <summary>
    /// Retry-After as seconds, or null when the response carries none.
    /// ★RFC 9110 allows either delta-seconds or an HTTP-date, and providers use both; a date in
    ///   the past yields null rather than a negative wait.
    /// </summary>
    internal static double? ReadRetryAfterSeconds(HttpResponseMessage response)
    {
        var retryAfter = response.Headers.RetryAfter;
        if (retryAfter == null)
        {
            return null;
        }

        if (retryAfter.Delta is { } delta)
        {
            return delta.TotalSeconds > 0 ? delta.TotalSeconds : null;
        }

        if (retryAfter.Date is { } date)
        {
            var seconds = (date - DateTimeOffset.UtcNow).TotalSeconds;
            return seconds > 0 ? seconds : null;
        }

        return null;
    }

    /// <summary>
    /// The tail every engine's retry line shares, so the attempt is counted the same way wherever
    /// it is logged. The caller supplies the subject because each engine names itself differently.
    /// ★<paramref name="attempt"/> is 0-based and the text is 1-based, counting the first try -
    ///   "attempt 2 of 4" is what a person reading a log expects after one failure.
    /// </summary>
    internal static string DescribeRetry(string subject, TimeSpan delay, int attempt)
        => $"{subject} — retrying in {delay.TotalSeconds:N0}s " +
           $"(attempt {attempt + 2} of {MaxTransientRetries + 1})";

    /// <summary>"HTTP 429", or "network error" when the request never got an answer.</summary>
    internal static string DescribeFailure(HttpRequestException exception)
        => exception.StatusCode.HasValue ? $"HTTP {(int)exception.StatusCode.Value}" : "network error";
}
