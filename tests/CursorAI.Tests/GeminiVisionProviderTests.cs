using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using CursorAI.App.Capture;
using CursorAI.App.Vision;

namespace CursorAI.Tests;

public class GeminiVisionProviderTests
{
    [Fact]
    public void CreateInteractionRequest_PreservesContextAndInlinePngWithoutStorage()
    {
        byte[] pngBytes = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
        VisionRequest request = CreateVisionRequest(pngBytes);

        GeminiInteractionRequest interaction = GeminiVisionProvider.CreateInteractionRequest(request);

        Assert.Equal("gemini-3.8-flash", interaction.Model);
        Assert.False(interaction.Store);
        Assert.Collection(
            interaction.Input,
            image =>
            {
                Assert.Equal("image", image.Type);
                Assert.Equal("image/png", image.MimeType);
                Assert.Equal(pngBytes, Convert.FromBase64String(image.Data!));
                Assert.Null(image.Text);
            },
            text =>
            {
                Assert.Equal("text", text.Type);
                Assert.Equal(VisionPromptBuilder.Build(request), text.Text);
                Assert.Null(text.MimeType);
                Assert.Null(text.Data);
            });
    }

    [Fact]
    public void ModelAndEndpoint_UseCurrentInteractionsApi()
    {
        Assert.Equal("gemini-3.8-flash", GeminiVisionProvider.DefaultModel);
        Assert.Equal(
            "https://generativelanguage.googleapis.com/v1beta/interactions",
            GeminiVisionProvider.InteractionsEndpoint);
    }

    [Fact]
    public void SerializeInteractionRequest_WritesExplicitStoreFalseAndMultimodalSchema()
    {
        byte[] pngBytes = [1, 2, 3, 4];
        GeminiInteractionRequest request = GeminiVisionProvider.CreateInteractionRequest(
            CreateVisionRequest(pngBytes));

        byte[] json = GeminiVisionProvider.SerializeInteractionRequest(request);
        using JsonDocument document = JsonDocument.Parse(json);
        JsonElement root = document.RootElement;
        JsonElement input = root.GetProperty("input");

        Assert.Equal("gemini-3.8-flash", root.GetProperty("model").GetString());
        Assert.False(root.GetProperty("store").GetBoolean());
        Assert.Equal("image", input[0].GetProperty("type").GetString());
        Assert.Equal("image/png", input[0].GetProperty("mime_type").GetString());
        Assert.Equal(pngBytes, Convert.FromBase64String(input[0].GetProperty("data").GetString()!));
        Assert.Equal("text", input[1].GetProperty("type").GetString());
        Assert.Contains("What am I looking at", input[1].GetProperty("text").GetString());
    }

    [Fact]
    public void ParseResponse_ReturnsTextFromFinalModelOutputStep()
    {
        const string responseJson = """
            {
              "status": "completed",
              "steps": [
                {
                  "type": "thought",
                  "content": [{ "type": "text", "text": "internal" }]
                },
                {
                  "type": "model_output",
                  "content": [
                    { "type": "text", "text": "First paragraph." },
                    { "type": "text", "text": "Second paragraph." }
                  ]
                }
              ]
            }
            """;

        string result = GeminiVisionProvider.ParseResponse(responseJson).ResponseText;

        Assert.Equal($"First paragraph.{Environment.NewLine}Second paragraph.", result);
    }

    [Fact]
    public void ParseResponse_RejectsResponseWithoutModelText()
    {
        const string responseJson = """{"status":"completed","steps":[]}""";

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
            () => GeminiVisionProvider.ParseResponse(responseJson).ResponseText);

        Assert.Equal("Gemini returned no usable response text.", exception.Message);
    }

    [Fact]
    public void ParseApiError_ReadsArrayWrappedInteractionsEnvelope()
    {
        // Captured verbatim from the live /v1beta/interactions endpoint on 2026-10-08.
        // The Interactions API array-wraps error payloads; generateContent does not.
        // Deserializing this shape into a bare envelope type threw JsonException, which
        // discarded every real Gemini error and stalled the project for over a week.
        const string responseJson = """
            [{
              "error": {
                "code": 400,
                "message": "API key not valid. Please pass a valid API key.",
                "status": "INVALID_ARGUMENT",
                "details": [
                  {
                    "@type": "type.googleapis.com/google.rpc.ErrorInfo",
                    "reason": "API_KEY_INVALID",
                    "domain": "googleapis.com"
                  }
                ]
              }
            }]
            """;

        GeminiApiError error = GeminiVisionProvider.ParseApiError(responseJson);

        Assert.Equal("400", error.Code);
        Assert.Equal("INVALID_ARGUMENT", error.Status);
        Assert.Equal("API key not valid. Please pass a valid API key.", error.Message);
    }

    [Fact]
    public void ParseApiError_StillReadsBareObjectEnvelope()
    {
        const string responseJson = """
            {"error":{"code":429,"message":"Quota exceeded.","status":"RESOURCE_EXHAUSTED"}}
            """;

        GeminiApiError error = GeminiVisionProvider.ParseApiError(responseJson);

        Assert.Equal("429", error.Code);
        Assert.Equal("RESOURCE_EXHAUSTED", error.Status);
        Assert.Equal("Quota exceeded.", error.Message);
    }

    [Fact]
    public void ParseApiError_PreservesUnparseableBodyAsEvidence()
    {
        const string responseJson = "<html><body>502 Bad Gateway</body></html>";

        GeminiApiError error = GeminiVisionProvider.ParseApiError(responseJson);

        // The body must survive instead of being replaced by a generic sentence.
        Assert.Contains("502 Bad Gateway", error.Message);
        Assert.Null(error.Code);
        Assert.Null(error.Status);
    }

    [Fact]
    public void ParseResponse_ReadsArrayWrappedResponse()
    {
        const string responseJson = """
            [{
              "status": "completed",
              "steps": [
                {
                  "type": "model_output",
                  "content": [{ "type": "text", "text": "A settings window." }]
                }
              ]
            }]
            """;

        Assert.Equal("A settings window.", GeminiVisionProvider.ParseResponse(responseJson).ResponseText);
    }

    [Fact]
    public void ParseResponse_WhenUnparseable_ReportsRedactedPayload()
    {
        const string apiKey = "test-secret-key-value";
        string encodedData = new('C', 120);
        string responseJson =
            $"not json at all; api_key={apiKey}; at https://example.test/x; blob={encodedData}";

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
            () => GeminiVisionProvider.ParseResponse(responseJson).ResponseText);

        Assert.Contains("could not parse", exception.Message);
        Assert.Contains("not json at all", exception.Message);
        Assert.DoesNotContain(apiKey, exception.Message);
        Assert.DoesNotContain("https://", exception.Message);
        Assert.DoesNotContain(encodedData, exception.Message);
    }

    [Fact]
    public void ResolveModel_PrefersEnvironmentOverrideThenFallsBackToDefault()
    {
        try
        {
            Environment.SetEnvironmentVariable("GEMINI_MODEL", "  gemini-3.5-flash  ");
            Assert.Equal("gemini-3.5-flash", GeminiVisionProvider.ResolveModel());

            Environment.SetEnvironmentVariable("GEMINI_MODEL", "   ");
            Assert.Equal(GeminiVisionProvider.DefaultModel, GeminiVisionProvider.ResolveModel());
        }
        finally
        {
            Environment.SetEnvironmentVariable("GEMINI_MODEL", null);
        }
    }

    [Fact]
    public void DefaultModel_IsNotTheModelRetiringInNovember2026()
    {
        // gemini-3.6-flash retires 2026-11-19. Guard against regressing to it.
        Assert.NotEqual("gemini-3.6-flash", GeminiVisionProvider.DefaultModel);
    }

    [Fact]
    public void RequestTimeout_IsGenerousEnoughForElementDetection()
    {
        // Calibrated from measurement, not guessed. Observed against the live API:
        // plain description 4.4s synthetic and 25.5s on a real desktop; element detection with
        // coordinates 66.2s and 73.5s. A 60s ceiling killed both detection calls. This now
        // deliberately exceeds the 100s .NET default, because on a 20-request daily budget a
        // premature timeout spends a request and gets nothing for it.
        Assert.Equal(TimeSpan.FromSeconds(120), GeminiVisionProvider.RequestTimeout);
        Assert.True(GeminiVisionProvider.RequestTimeout > TimeSpan.FromSeconds(75));
    }

    [Fact]
    public void ErrorDiagnostics_ReportsClassificationAndRedactsSensitiveValues()
    {
        const string apiKey = "test-secret-key-value";
        string encodedData = new('A', 120);

        string diagnostic = GeminiApiErrorDiagnostics.FormatApiError(
            HttpStatusCode.NotFound,
            "404",
            "NOT_FOUND",
            $"Invalid request at https://example.test/path?key={apiKey}; api_key={apiKey}; image={encodedData}",
            apiKey);

        Assert.Contains("HTTP status: 404 (NotFound)", diagnostic);
        Assert.Contains("Gemini API code: 404", diagnostic);
        Assert.Contains("Gemini API status: NOT_FOUND", diagnostic);
        Assert.Contains("API message:", diagnostic);
        Assert.DoesNotContain(apiKey, diagnostic);
        Assert.DoesNotContain("https://", diagnostic);
        Assert.DoesNotContain(encodedData, diagnostic);
        Assert.Contains("<redacted>", diagnostic);
        Assert.Contains("<url omitted>", diagnostic);
        Assert.Contains("<encoded data omitted>", diagnostic);
    }

    [Fact]
    public void CancellationDiagnostics_DistinguishesApplicationCancellation()
    {
        string diagnostic = GeminiApiErrorDiagnostics.FormatCancellation(
            new OperationCanceledException("The operation was canceled."),
            applicationCancellationRequested: true,
            TimeSpan.FromSeconds(100),
            usesDefaultHttpClientTimeout: true,
            apiKey: null);

        Assert.Contains("Cancellation source: application cancellation token", diagnostic);
        Assert.Contains("Application cancellation requested: true", diagnostic);
        Assert.Contains("HttpClient timeout: 100 seconds (.NET default)", diagnostic);
    }

    [Fact]
    public void CancellationDiagnostics_IdentifiesRuntimeTimeoutEvidence()
    {
        string diagnostic = GeminiApiErrorDiagnostics.FormatCancellation(
            new TaskCanceledException("Request canceled.", new TimeoutException("Timed out.")),
            applicationCancellationRequested: false,
            TimeSpan.FromSeconds(100),
            usesDefaultHttpClientTimeout: true,
            apiKey: null);

        Assert.Contains("HTTP request timeout (runtime supplied an inner TimeoutException)", diagnostic);
        Assert.Contains("Application cancellation requested: false", diagnostic);
    }

    [Fact]
    public void CancellationDiagnostics_DoesNotGuessOrExposeSecrets()
    {
        const string apiKey = "test-secret-key-value";
        string encodedData = new('B', 120);
        string diagnostic = GeminiApiErrorDiagnostics.FormatCancellation(
            new TaskCanceledException(
                $"Canceled at https://example.test/path?key={apiKey} with {encodedData}"),
            applicationCancellationRequested: false,
            TimeSpan.FromSeconds(100),
            usesDefaultHttpClientTimeout: true,
            apiKey);

        Assert.Contains("exact source is not exposed by the runtime", diagnostic);
        Assert.DoesNotContain(apiKey, diagnostic);
        Assert.DoesNotContain("https://", diagnostic);
        Assert.DoesNotContain(encodedData, diagnostic);
    }

    [Fact]
    public void ParseApiError_ReadsStringCodeFromCapacityError()
    {
        // Captured verbatim from the live endpoint on 2026-10-08. Capacity errors use a
        // STRING code and omit "status" entirely, unlike validation errors which use an
        // int code. A typed int? field threw here and lost the whole error.
        const string responseJson = """
            {"error":{"message":"gemini-3.8-flash is currently experiencing high demand, spikes in demand are usually temporary. Please try again later.","code":"service_unavailable"}}
            """;

        GeminiApiError error = GeminiVisionProvider.ParseApiError(responseJson);

        Assert.Equal("service_unavailable", error.Code);
        Assert.Null(error.Status);
        Assert.Contains("experiencing high demand", error.Message);
    }

    [Fact]
    public async Task AnalyzeAsync_RetriesTransientCapacityFailuresThenSucceeds()
    {
        Environment.SetEnvironmentVariable("GEMINI_API_KEY", "stub-key");
        StubHandler handler = new(
            (HttpStatusCode.ServiceUnavailable, """{"error":{"code":"service_unavailable","message":"busy"}}"""),
            (HttpStatusCode.ServiceUnavailable, """{"error":{"code":"service_unavailable","message":"busy"}}"""),
            (HttpStatusCode.OK, """{"status":"completed","steps":[{"type":"model_output","content":[{"type":"text","text":"Recovered."}]}]}"""));

        using HttpClient client = new(handler);
        GeminiVisionProvider provider = new(client);

        VisionResult result = await provider.AnalyzeAsync(
            CreateVisionRequest([1, 2, 3]),
            CancellationToken.None);

        Assert.Equal("Recovered.", result.ResponseText);
        Assert.Equal(3, handler.CallCount);
    }

    [Fact]
    public async Task AnalyzeAsync_DoesNotRetryNonTransientFailures()
    {
        Environment.SetEnvironmentVariable("GEMINI_API_KEY", "stub-key");
        StubHandler handler = new(
            (HttpStatusCode.BadRequest, """{"error":{"code":400,"message":"Bad input.","status":"INVALID_ARGUMENT"}}"""));

        using HttpClient client = new(handler);
        GeminiVisionProvider provider = new(client);

        InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => provider.AnalyzeAsync(CreateVisionRequest([1, 2, 3]), CancellationToken.None));

        Assert.Equal(1, handler.CallCount);
        Assert.Contains("Bad input.", exception.Message);
        Assert.DoesNotContain("Attempts:", exception.Message);
    }

    [Fact]
    public async Task AnalyzeAsync_WhenTransientFailuresExhaustAttempts_ReportsAttemptCount()
    {
        Environment.SetEnvironmentVariable("GEMINI_API_KEY", "stub-key");
        const string busy = """{"error":{"code":"service_unavailable","message":"still busy"}}""";
        StubHandler handler = new(
            (HttpStatusCode.ServiceUnavailable, busy),
            (HttpStatusCode.ServiceUnavailable, busy),
            (HttpStatusCode.ServiceUnavailable, busy));

        using HttpClient client = new(handler);
        GeminiVisionProvider provider = new(client);

        InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => provider.AnalyzeAsync(CreateVisionRequest([1, 2, 3]), CancellationToken.None));

        Assert.Equal(GeminiVisionProvider.MaxAttempts, handler.CallCount);
        Assert.Contains($"Attempts: {GeminiVisionProvider.MaxAttempts}", exception.Message);
        Assert.Contains("still busy", exception.Message);
        Assert.Contains("Gemini API code: service_unavailable", exception.Message);
    }

    private sealed class StubHandler(params (HttpStatusCode Status, string Body)[] responses)
        : HttpMessageHandler
    {
        internal int CallCount { get; private set; }

        internal TimeSpan? RetryAfter { get; init; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            (HttpStatusCode status, string body) = responses[Math.Min(CallCount, responses.Length - 1)];
            CallCount++;
            HttpResponseMessage response = new(status) { Content = new StringContent(body) };
            if (RetryAfter is { } wait)
            {
                response.Headers.RetryAfter = new RetryConditionHeaderValue(wait);
            }

            return Task.FromResult(response);
        }
    }

    [Fact]
    public async Task AnalyzeAsync_WhenRetryAfterIsHoursAway_FailsWithoutBurningQuota()
    {
        // Verified against the live API 2026-10-08: an exhausted free-tier daily quota answers
        // 429 with Retry-After: 33769 (over nine hours). Retrying that spent three of twenty
        // daily requests to be refused three times. Exactly one attempt now.
        Environment.SetEnvironmentVariable("GEMINI_API_KEY", "stub-key");
        const string quota = """
            {"error":{"code":"too_many_requests","message":"Rate limit exceeded for model gemini-3.8-flash (limit: 20 requests per day on Free Tier)."}}
            """;
        StubHandler handler = new((HttpStatusCode.TooManyRequests, quota))
        {
            RetryAfter = TimeSpan.FromSeconds(33769)
        };

        using HttpClient client = new(handler);
        GeminiVisionProvider provider = new(client);

        InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => provider.AnalyzeAsync(CreateVisionRequest([1, 2, 3]), CancellationToken.None));

        Assert.Equal(1, handler.CallCount);
        Assert.Contains("Retry-After: 9h", exception.Message);
        Assert.Contains("20 requests per day", exception.Message);
        Assert.DoesNotContain("Attempts:", exception.Message);
    }

    [Fact]
    public async Task AnalyzeAsync_WhenRetryAfterIsShort_StillRetries()
    {
        Environment.SetEnvironmentVariable("GEMINI_API_KEY", "stub-key");
        StubHandler handler = new(
            (HttpStatusCode.TooManyRequests, """{"error":{"code":"too_many_requests","message":"slow down"}}"""),
            (HttpStatusCode.OK, """{"status":"completed","steps":[{"type":"model_output","content":[{"type":"text","text":"Fine."}]}]}"""))
        {
            RetryAfter = TimeSpan.FromSeconds(1)
        };

        using HttpClient client = new(handler);
        GeminiVisionProvider provider = new(client);

        VisionResult result = await provider.AnalyzeAsync(
            CreateVisionRequest([1, 2, 3]), CancellationToken.None);

        Assert.Equal("Fine.", result.ResponseText);
        Assert.Equal(2, handler.CallCount);
    }

    [Fact]
    public void RetryCeilings_DistinguishCapacityWaitsFromQuotaWaits()
    {
        // A busy model is worth waiting out; an exhausted daily allowance is not. Observed
        // 2026-10-12: a real 503 asked for 30s and a flat 10s ceiling threw the turn away,
        // while an exhausted quota asked for over nine hours.
        Assert.True(GeminiVisionProvider.MaxHonoredCapacityWait >= TimeSpan.FromSeconds(30));
        Assert.True(GeminiVisionProvider.MaxHonoredCapacityWait <= TimeSpan.FromSeconds(60));
        Assert.True(GeminiVisionProvider.MaxHonoredRateLimitWait <= TimeSpan.FromSeconds(15));
        Assert.True(
            GeminiVisionProvider.MaxHonoredRateLimitWait < GeminiVisionProvider.MaxHonoredCapacityWait);
    }

    [Fact]
    public async Task AnalyzeAsync_WaitsOutACapacity503ThatAsksForThirtySeconds()
    {
        // The exact case from the log: 503 with Retry-After: 30. Previously abandoned.
        Environment.SetEnvironmentVariable("GEMINI_API_KEY", "stub-key");
        StubHandler handler = new(
            (HttpStatusCode.ServiceUnavailable, BusyBody),
            (HttpStatusCode.OK, RecoveredBody))
        {
            RetryAfter = TimeSpan.FromSeconds(30)
        };

        using HttpClient client = new(handler);
        GeminiVisionProvider provider = new(client);

        // Confirms the decision to retry rather than the 30s wall-clock wait.
        Task<VisionResult> pending = provider.AnalyzeAsync(
            CreateVisionRequest([1, 2, 3]), CancellationToken.None);
        Task finished = await Task.WhenAny(pending, Task.Delay(TimeSpan.FromSeconds(2)));

        Assert.NotSame(pending, finished);
        Assert.Equal(1, handler.CallCount);
        Assert.False(pending.IsCompleted);
    }

    [Fact]
    public async Task AnalyzeAsync_StillRefusesAQuota429AskingForHours()
    {
        Environment.SetEnvironmentVariable("GEMINI_API_KEY", "stub-key");
        StubHandler handler = new((HttpStatusCode.TooManyRequests, QuotaBody))
        {
            RetryAfter = TimeSpan.FromSeconds(33769)
        };

        using HttpClient client = new(handler);
        GeminiVisionProvider provider = new(client);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => provider.AnalyzeAsync(CreateVisionRequest([1, 2, 3]), CancellationToken.None));

        Assert.Equal(1, handler.CallCount);
    }

    [Fact]
    public void ParseResponse_CapturesModelAndTokenUsage()
    {
        // Field names taken from a real 200 response observed on 2026-10-08.
        const string responseJson = """
            {
              "status": "completed",
              "model": "gemini-3.8-flash",
              "usage": {
                "total_tokens": 1844,
                "total_input_tokens": 1702,
                "total_output_tokens": 74,
                "total_thought_tokens": 68
              },
              "steps": [
                { "type": "model_output", "content": [{ "type": "text", "text": "A toolbar." }] }
              ]
            }
            """;

        VisionResult result = GeminiVisionProvider.ParseResponse(responseJson);

        Assert.Equal("A toolbar.", result.ResponseText);
        Assert.Equal("gemini-3.8-flash", result.Model);
        Assert.NotNull(result.Usage);
        Assert.Equal(1844, result.Usage!.TotalTokens);
        Assert.Equal(1702, result.Usage.InputTokens);
        Assert.Equal(74, result.Usage.OutputTokens);
        Assert.Equal(68, result.Usage.ThoughtTokens);
        // Latency belongs to VisionService, not the provider.
        Assert.Null(result.Latency);
    }

    [Fact]
    public void ParseResponse_WhenMetadataIsAbsent_LeavesItNullRatherThanZero()
    {
        const string responseJson = """{"status":"completed","steps":[{"type":"model_output","content":[{"type":"text","text":"Hi."}]}]}""";

        VisionResult result = GeminiVisionProvider.ParseResponse(responseJson);

        Assert.Equal("Hi.", result.ResponseText);
        Assert.Null(result.Model);
        Assert.Null(result.Usage);
    }

    [Fact]
    public async Task AnalyzeAsync_WhenCancelled_StopsPromptlyRatherThanWaitingOut()
    {
        // A request can take over a minute on a throttled free tier, so cancelling must not
        // wait for the provider. This exercises the same token the panel's Cancel feeds.
        Environment.SetEnvironmentVariable("GEMINI_API_KEY", "stub-key");
        SlowHandler handler = new(TimeSpan.FromMinutes(2));

        using HttpClient client = new(handler);
        GeminiVisionProvider provider = new(client);
        using CancellationTokenSource cts = new();

        Task<VisionResult> pending = provider.AnalyzeAsync(CreateVisionRequest([1, 2, 3]), cts.Token);
        await handler.Started.Task;
        System.Diagnostics.Stopwatch sw = System.Diagnostics.Stopwatch.StartNew();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
        sw.Stop();
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(5), $"took {sw.Elapsed.TotalSeconds:F1}s");
    }

    /// <summary>Hangs until cancelled, standing in for a provider that is being slow.</summary>
    private sealed class SlowHandler(TimeSpan delay) : HttpMessageHandler
    {
        internal TaskCompletionSource Started { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Started.TrySetResult();
            await Task.Delay(delay, cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK);
        }
    }

    private const string BusyBody =
        """{"error":{"code":"service_unavailable","message":"busy"}}""";

    private const string RecoveredBody =
        """{"status":"completed","steps":[{"type":"model_output","content":[{"type":"text","text":"Recovered."}]}]}""";

    private const string QuotaBody =
        """{"error":{"code":"too_many_requests","message":"Rate limit exceeded (limit: 20 requests per day on Free Tier)."}}""";

    private static VisionRequest CreateVisionRequest(byte[] pngBytes)
    {
        return new VisionRequest(
            "What am I looking at, and what is under my cursor?",
            pngBytes,
            1920,
            1080,
            new DateTimeOffset(2026, 9, 21, 12, 0, 0, TimeSpan.FromHours(2)),
            new CapturedMonitorInfo(
                new PhysicalScreenBounds(-1920, 0, 1920, 1080),
                new PhysicalScreenBounds(-1920, 0, 1920, 1040),
                false),
            new PhysicalScreenPoint(-1200, 400),
            new PhysicalScreenPoint(720, 400),
            new ForegroundWindowInfo(123, "Project - Editor", "editor"));
    }
}
