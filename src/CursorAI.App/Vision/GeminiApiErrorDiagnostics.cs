using System.Net;
using System.Net.Http;
using System.Text.RegularExpressions;

namespace CursorAI.App.Vision;

internal static partial class GeminiApiErrorDiagnostics
{
    private const int MaximumMessageLength = 1000;

    internal static string FormatApiError(
        HttpStatusCode statusCode,
        string? apiCode,
        string? apiStatus,
        string? apiMessage,
        string? apiKey,
        int attempts = 1,
        TimeSpan? retryAfter = null)
    {
        List<string> details =
        [
            "Failure type: Gemini API error response",
            $"HTTP status: {(int)statusCode} ({statusCode})"
        ];

        if (attempts > 1)
        {
            details.Add($"Attempts: {attempts} (transient failures were retried)");
        }

        if (retryAfter is { } wait && wait > TimeSpan.Zero)
        {
            details.Add($"Retry-After: {FormatRetryAfter(wait)} (not waited out; request abandoned)");
        }

        if (!string.IsNullOrWhiteSpace(apiCode))
        {
            details.Add($"Gemini API code: {SanitizeMessage(apiCode, apiKey: null)}");
        }

        if (!string.IsNullOrWhiteSpace(apiStatus))
        {
            details.Add($"Gemini API status: {SanitizeMessage(apiStatus, apiKey: null)}");
        }

        details.Add($"API message: {SanitizeMessage(apiMessage, apiKey)}");
        return CreateDiagnostic(details);
    }

    internal static string FormatTransportError(HttpRequestException exception, string? apiKey)
    {
        ArgumentNullException.ThrowIfNull(exception);

        List<string> details = [$".NET exception: {exception.GetType().Name}"];
        if (exception.StatusCode is HttpStatusCode statusCode)
        {
            details.Add($"HTTP status: {(int)statusCode} ({statusCode})");
        }

        details.Add($"HTTP classification: {exception.HttpRequestError}");
        details.Add($"API message: {SanitizeMessage(exception.Message, apiKey)}");
        return CreateDiagnostic(details);
    }

    internal static string FormatCancellation(
        OperationCanceledException exception,
        bool applicationCancellationRequested,
        TimeSpan httpClientTimeout,
        bool usesDefaultHttpClientTimeout,
        string? apiKey)
    {
        ArgumentNullException.ThrowIfNull(exception);

        string source = applicationCancellationRequested
            ? "application cancellation token"
            : exception.InnerException is TimeoutException
                ? "HTTP request timeout (runtime supplied an inner TimeoutException)"
                : "HTTP transport cancellation; exact source is not exposed by the runtime";

        List<string> details =
        [
            $".NET exception: {exception.GetType().Name}",
            $"Cancellation source: {source}",
            $"Application cancellation requested: {applicationCancellationRequested.ToString().ToLowerInvariant()}",
            $"HttpClient timeout: {FormatTimeout(httpClientTimeout, usesDefaultHttpClientTimeout)}",
            $"Transport message: {SanitizeMessage(exception.Message, apiKey)}"
        ];

        return CreateDiagnostic(details);
    }

    /// <summary>
    /// Renders an unexpected response body for display: redacted, single-line, truncated.
    /// Used when parsing fails, so the payload itself survives into the error message.
    /// </summary>
    internal static string DescribePayload(string? payload)
    {
        return string.IsNullOrWhiteSpace(payload)
            ? "<empty response body>"
            : SanitizeMessage(payload, apiKey: null);
    }

    private static string CreateDiagnostic(List<string> details)
    {
        return $"Gemini API request failed.{Environment.NewLine}{string.Join(Environment.NewLine, details)}";
    }

    private static string FormatRetryAfter(TimeSpan wait)
    {
        return wait.TotalHours >= 1
            ? $"{(int)wait.TotalHours}h {wait.Minutes}m"
            : wait.TotalMinutes >= 1
                ? $"{(int)wait.TotalMinutes}m {wait.Seconds}s"
                : $"{wait.TotalSeconds:0.#}s";
    }

    private static string FormatTimeout(TimeSpan timeout, bool usesDefault)
    {
        string value = timeout == Timeout.InfiniteTimeSpan
            ? "infinite"
            : $"{timeout.TotalSeconds:0.###} seconds";
        return usesDefault ? $"{value} (.NET default)" : value;
    }

    private static string SanitizeMessage(string? message, string? apiKey)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return "<unavailable>";
        }

        string sanitized = message.Replace('\r', ' ').Replace('\n', ' ').Trim();
        if (!string.IsNullOrWhiteSpace(apiKey))
        {
            sanitized = sanitized.Replace(apiKey, "<redacted>", StringComparison.Ordinal);
        }

        sanitized = CredentialPattern().Replace(sanitized, "$1<redacted>");
        sanitized = UrlPattern().Replace(sanitized, "<url omitted>");
        sanitized = LongEncodedDataPattern().Replace(sanitized, "<encoded data omitted>");

        return sanitized.Length <= MaximumMessageLength
            ? sanitized
            : $"{sanitized[..MaximumMessageLength]}... <truncated>";
    }

    [GeneratedRegex("""(?i)\b(api[_-]?key|key|x-goog-api-key)\s*[:=]\s*("[^"]*"|\S+)""")]
    private static partial Regex CredentialPattern();

    [GeneratedRegex(@"https?://\S+", RegexOptions.IgnoreCase)]
    private static partial Regex UrlPattern();

    [GeneratedRegex(@"[A-Za-z0-9+/]{80,}={0,2}")]
    private static partial Regex LongEncodedDataPattern();
}
