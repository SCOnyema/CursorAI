using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CursorAI.App.Vision;

public sealed class GeminiVisionProvider : IVisionProvider
{
    public string Description => $"Google Gemini ({ResolveModel()}) via the Interactions API";

    /// <summary>
    /// Default vision model. Measured availability 2026-10-08 over 5 calls each:
    /// gemini-3.8-flash 4/5, gemini-3.7-flash 2/5, gemini-3.5-flash 5/5.
    /// 3.8-flash stays the default because it is the newest and furthest from retirement,
    /// and three retry attempts turn an ~80% per-call rate into ~99%. Override via
    /// GEMINI_MODEL when 3.8 is having a bad day; gemini-3.5-flash was the most reliable
    /// alternative. Do not pick gemini-3.6-flash: it retires 2026-11-19.
    /// </summary>
    internal const string DefaultModel = "gemini-3.8-flash";
    internal const string InteractionsEndpoint = "https://generativelanguage.googleapis.com/v1beta/interactions";
    private const string ApiKeyEnvironmentVariable = "GEMINI_API_KEY";
    private const string ModelEnvironmentVariable = "GEMINI_MODEL";

    internal const int MaxAttempts = 3;

    /// <summary>
    /// Longest Retry-After to wait out when the model is merely busy. Observed 2026-10-12: a
    /// real 503 asked for 30 seconds and a flat 10-second ceiling abandoned the request,
    /// losing the user's whole turn for a wait that was worth taking.
    /// </summary>
    internal static readonly TimeSpan MaxHonoredCapacityWait = TimeSpan.FromSeconds(45);

    /// <summary>
    /// Longest Retry-After to wait out when rate-limited. Kept short on purpose: an exhausted
    /// free-tier daily allowance answers 429 with Retry-After over nine hours, and each retry
    /// spends another of only twenty daily requests to be refused again.
    /// </summary>
    internal static readonly TimeSpan MaxHonoredRateLimitWait = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Per-request timeout, calibrated from measurement rather than guessed.
    ///
    /// Observed against the live API with full-resolution 1920x1080 captures:
    ///   plain description, synthetic screen   4.4s
    ///   plain description, real desktop      25.5s
    ///   element detection with coordinates   66.2s and 73.5s
    ///
    /// Detection roughly triples the cost of a description, and an earlier 60s ceiling would
    /// have killed both detection calls. Set generously on purpose: the free tier allows only
    /// 20 requests per day per model, so killing a slow-but-working request spends one and
    /// gets nothing for it. Timeouts are not retried, so this is the true worst case rather
    /// than a per-attempt cost.
    /// </summary>
    internal static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(120);

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _httpClient;
    private readonly bool _ownsHttpClient;
    private readonly bool _usesDefaultHttpClientTimeout;
    private bool _isDisposed;

    public GeminiVisionProvider()
        : this(
            new HttpClient { Timeout = RequestTimeout },
            ownsHttpClient: true,
            usesDefaultHttpClientTimeout: false)
    {
    }

    internal GeminiVisionProvider(
        HttpClient httpClient,
        bool ownsHttpClient = false,
        bool usesDefaultHttpClientTimeout = false)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _ownsHttpClient = ownsHttpClient;
        _usesDefaultHttpClientTimeout = usesDefaultHttpClientTimeout;
    }

    public async Task<VisionResult> AnalyzeAsync(
        VisionRequest request,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        string apiKey = GetApiKey();
        GeminiInteractionRequest interactionRequest = CreateInteractionRequest(request);
        byte[] requestJson = SerializeInteractionRequest(interactionRequest);

        for (int attempt = 1; ; attempt++)
        {
            using HttpRequestMessage httpRequest = new(HttpMethod.Post, InteractionsEndpoint);
            httpRequest.Headers.Add("x-goog-api-key", apiKey);
            httpRequest.Content = new ByteArrayContent(requestJson);
            httpRequest.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");

            using HttpResponseMessage response = await SendAsync(httpRequest, apiKey, cancellationToken);
            string responseJson = await response.Content.ReadAsStringAsync(cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                return ParseResponse(
                    responseJson,
                    request.ImagePixelWidth,
                    request.ImagePixelHeight);
            }

            TimeSpan retryDelay = TimeSpan.Zero;
            bool canRetry = IsTransient(response.StatusCode)
                && attempt < MaxAttempts
                && TryGetRetryDelay(response, attempt, out retryDelay);

            if (!canRetry)
            {
                GeminiApiError error = ParseApiError(responseJson);
                throw new InvalidOperationException(GeminiApiErrorDiagnostics.FormatApiError(
                    response.StatusCode,
                    error.Code,
                    error.Status,
                    error.Message,
                    apiKey,
                    attempt,
                    ReadRetryAfter(response)));
            }

            await Task.Delay(retryDelay, cancellationToken);
        }
    }

    /// <summary>
    /// Capacity errors are common on this endpoint and explicitly described as temporary
    /// ("spikes in demand are usually temporary"). Verified 2026-10-08: roughly one call in
    /// three returned 503 while the model was under load. Retrying the same model is not
    /// provider fallback — no alternative model or provider is ever substituted.
    /// </summary>
    private static bool IsTransient(HttpStatusCode statusCode)
    {
        return statusCode is HttpStatusCode.TooManyRequests
            or HttpStatusCode.InternalServerError
            or HttpStatusCode.BadGateway
            or HttpStatusCode.ServiceUnavailable
            or HttpStatusCode.GatewayTimeout;
    }

    private static TimeSpan RetryDelay(int attempt) => TimeSpan.FromSeconds(Math.Pow(2, attempt - 1));

    /// <summary>
    /// Decides whether to retry and how long to wait, honouring Retry-After.
    /// Verified 2026-10-08: an exhausted free-tier daily quota answers 429 with
    /// Retry-After: 33769 (over nine hours). Blindly retrying that spent three requests from
    /// a twenty-per-day budget to receive the same refusal three times, so any wait longer
    /// than <see cref="MaxHonoredRetryAfter"/> fails fast instead.
    /// </summary>
    private static bool TryGetRetryDelay(HttpResponseMessage response, int attempt, out TimeSpan delay)
    {
        if (ReadRetryAfter(response) is { } requested)
        {
            // A busy model and an exhausted allowance both answer with Retry-After, but they
            // mean opposite things: one is worth waiting out, the other costs a request to be
            // refused again. The status code is what separates them.
            TimeSpan ceiling = response.StatusCode == HttpStatusCode.TooManyRequests
                ? MaxHonoredRateLimitWait
                : MaxHonoredCapacityWait;

            delay = requested > TimeSpan.Zero ? requested : TimeSpan.Zero;
            return requested <= ceiling;
        }

        delay = RetryDelay(attempt);
        return true;
    }

    private static TimeSpan? ReadRetryAfter(HttpResponseMessage response)
    {
        RetryConditionHeaderValue? header = response.Headers.RetryAfter;
        if (header is null)
        {
            return null;
        }

        return header.Delta ?? (header.Date is { } date ? date - DateTimeOffset.UtcNow : null);
    }

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        if (_ownsHttpClient)
        {
            _httpClient.Dispose();
        }
    }

    internal static GeminiInteractionRequest CreateInteractionRequest(VisionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        return new GeminiInteractionRequest(
            ResolveModel(),
            [
                new GeminiInteractionInput(
                    "image",
                    MimeType: request.ImageMediaType,
                    Data: Convert.ToBase64String(request.EncodedImage.Span)),
                new GeminiInteractionInput(
                    "text",
                    Text: VisionPromptBuilder.Build(request))
            ],
            Store: false);
    }

    internal static VisionResult ParseResponse(
        string responseJson,
        int imagePixelWidth = 0,
        int imagePixelHeight = 0)
    {
        GeminiInteractionResponse? response;

        try
        {
            using JsonDocument document = JsonDocument.Parse(responseJson);
            response = UnwrapEnvelope(document.RootElement)
                .Deserialize<GeminiInteractionResponse>(JsonOptions);
        }
        catch (JsonException)
        {
            // Never discard the payload here. Reporting only "malformed response" is
            // what previously made a local parsing defect look like a Gemini fault.
            throw new InvalidOperationException(
                "CursorAI could not parse the Gemini Interactions API response. "
                + $"Received: {GeminiApiErrorDiagnostics.DescribePayload(responseJson)}");
        }

        GeminiInteractionStep? modelOutput = response?.Steps?
            .LastOrDefault(step => string.Equals(step.Type, "model_output", StringComparison.Ordinal));
        string[] textParts = modelOutput?.Content?
            .Where(content => string.Equals(content.Type, "text", StringComparison.Ordinal)
                && !string.IsNullOrWhiteSpace(content.Text))
            .Select(content => content.Text!.Trim())
            .ToArray()
            ?? [];

        if (textParts.Length == 0)
        {
            throw new InvalidOperationException("Gemini returned no usable response text.");
        }

        string combined = string.Join(Environment.NewLine, textParts);
        GeminiTargetPayload? payload = GeminiTargetParser.TryParse(combined, imagePixelWidth, imagePixelHeight);

        return new VisionResult(
            payload?.Answer ?? combined,
            response?.Model,
            ToUsage(response?.Usage),
            Targets: payload?.Targets);
    }

    private static VisionUsage? ToUsage(GeminiInteractionUsage? usage)
    {
        return usage is null
            ? null
            : new VisionUsage(
                usage.TotalTokens,
                usage.TotalInputTokens,
                usage.TotalOutputTokens,
                usage.TotalThoughtTokens);
    }

    internal static byte[] SerializeInteractionRequest(GeminiInteractionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return JsonSerializer.SerializeToUtf8Bytes(request, JsonOptions);
    }

    private async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        string apiKey,
        CancellationToken cancellationToken)
    {
        try
        {
            return await _httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);
        }
        catch (HttpRequestException exception)
        {
            throw new InvalidOperationException(
                GeminiApiErrorDiagnostics.FormatTransportError(exception, apiKey));
        }
        catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            throw new InvalidOperationException(
                GeminiApiErrorDiagnostics.FormatCancellation(
                    exception,
                    applicationCancellationRequested: false,
                    _httpClient.Timeout,
                    _usesDefaultHttpClientTimeout,
                    apiKey),
                exception);
        }
    }

    internal static GeminiApiError ParseApiError(string responseJson)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(responseJson);
            JsonElement root = UnwrapEnvelope(document.RootElement);

            if (root.ValueKind == JsonValueKind.Object
                && root.TryGetProperty("error", out JsonElement error)
                && error.ValueKind == JsonValueKind.Object)
            {
                return new GeminiApiError(
                    ReadScalarAsText(error, "code"),
                    ReadScalarAsText(error, "message"),
                    ReadScalarAsText(error, "status"));
            }
        }
        catch (JsonException)
        {
            // Fall through: an unparseable body is still evidence worth surfacing.
        }

        // Preserve the raw body rather than replacing it with a generic sentence.
        // FormatApiError sanitizes and truncates it before it reaches the user.
        return new GeminiApiError(null, responseJson, null);
    }

    /// <summary>
    /// Gemini is not consistent about the type of error fields. Verified 2026-10-08:
    /// a validation error returns <c>"code": 400</c> (number) while a capacity error
    /// returns <c>"code": "service_unavailable"</c> (string) and omits "status" entirely.
    /// Read whatever scalar is present as text rather than assuming a type.
    /// </summary>
    private static string? ReadScalarAsText(JsonElement parent, string propertyName)
    {
        if (!parent.TryGetProperty(propertyName, out JsonElement value))
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number => value.GetRawText(),
            _ => null
        };
    }

    /// <summary>
    /// The Interactions endpoint wraps error payloads in a single-element array, unlike
    /// generateContent which returns a bare object. Verified against the live API on
    /// 2026-10-08: an auth failure returns <c>[{"error":{...}}]</c>, while successful
    /// responses are bare objects. Accept both shapes.
    /// </summary>
    private static JsonElement UnwrapEnvelope(JsonElement root)
    {
        return root.ValueKind == JsonValueKind.Array && root.GetArrayLength() > 0
            ? root[0]
            : root;
    }

    /// <summary>
    /// Resolves the model id, allowing GEMINI_MODEL to override the default without a rebuild.
    /// This is a development convenience, not provider fallback: nothing is ever substituted
    /// automatically, and a bad value surfaces as a normal Gemini API error.
    /// </summary>
    internal static string ResolveModel()
    {
        string? configured = System.Environment.GetEnvironmentVariable(ModelEnvironmentVariable);
        return string.IsNullOrWhiteSpace(configured) ? DefaultModel : configured.Trim();
    }

    private static string GetApiKey()
    {
        string? apiKey = System.Environment.GetEnvironmentVariable(ApiKeyEnvironmentVariable);
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new InvalidOperationException(
                $"{ApiKeyEnvironmentVariable} is not configured. Set it before starting CursorAI to use vision analysis.");
        }

        return apiKey;
    }
}

internal sealed record GeminiInteractionRequest(
    [property: JsonPropertyName("model")] string Model,
    [property: JsonPropertyName("input")] IReadOnlyList<GeminiInteractionInput> Input,
    [property: JsonPropertyName("store")] bool Store);

internal sealed record GeminiInteractionInput(
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("mime_type")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? MimeType = null,
    [property: JsonPropertyName("data")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Data = null,
    [property: JsonPropertyName("text")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Text = null);

internal sealed record GeminiInteractionResponse(
    [property: JsonPropertyName("status")] string? Status,
    [property: JsonPropertyName("steps")] IReadOnlyList<GeminiInteractionStep>? Steps,
    [property: JsonPropertyName("model")] string? Model = null,
    [property: JsonPropertyName("usage")] GeminiInteractionUsage? Usage = null);

// Field names verified against a live 200 response on 2026-10-08.
internal sealed record GeminiInteractionUsage(
    [property: JsonPropertyName("total_tokens")] int? TotalTokens,
    [property: JsonPropertyName("total_input_tokens")] int? TotalInputTokens,
    [property: JsonPropertyName("total_output_tokens")] int? TotalOutputTokens,
    [property: JsonPropertyName("total_thought_tokens")] int? TotalThoughtTokens);

internal sealed record GeminiInteractionStep(
    [property: JsonPropertyName("type")] string? Type,
    [property: JsonPropertyName("content")] IReadOnlyList<GeminiInteractionContent>? Content);

internal sealed record GeminiInteractionContent(
    [property: JsonPropertyName("type")] string? Type,
    [property: JsonPropertyName("text")] string? Text);

// Code is text, not int: Gemini returns a number for validation errors and a string
// for capacity errors. See ReadScalarAsText.
internal sealed record GeminiApiError(string? Code, string? Message, string? Status);
