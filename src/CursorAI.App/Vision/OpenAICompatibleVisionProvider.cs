using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using CursorAI.App.Configuration;

namespace CursorAI.App.Vision;

/// <summary>
/// Vision through any endpoint that speaks the OpenAI chat-completions API.
/// </summary>
/// <remarks>
/// One provider rather than one per vendor. OpenRouter, Groq, Together, LM Studio, Ollama and
/// Azure OpenAI all expose `/chat/completions` with image content parts, so a configurable base
/// URL reaches all of them with the same code.
///
/// Chat-completions is used deliberately rather than OpenAI's newer Responses API, which
/// <see cref="OpenAIVisionProvider"/> uses: third-party compatible endpoints almost universally
/// implement chat-completions and almost none implement Responses, so that choice is what makes
/// this provider portable.
///
/// The notable case is a local model server. Pointed at Ollama or LM Studio this sends nothing
/// off the machine at all, which removes both the request quota and the external trust boundary
/// - at the cost of weaker reading of fine screen text, which the guidance coordinates depend
/// on. Nothing here has been verified against a live endpoint of any kind: no key and no local
/// server were available when it was written.
/// </remarks>
public sealed class OpenAICompatibleVisionProvider : IVisionProvider
{
    internal const string BaseUrlEnvironmentVariable = "CURSORAI_COMPATIBLE_BASE_URL";
    internal const string ModelEnvironmentVariable = "CURSORAI_COMPATIBLE_MODEL";
    internal const string ApiKeyEnvironmentVariable = "CURSORAI_COMPATIBLE_API_KEY";

    internal const int MaxAttempts = 3;

    internal static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(120);

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _httpClient;
    private readonly bool _ownsHttpClient;
    private readonly string _baseUrl;
    private readonly string _model;
    private readonly string? _apiKey;
    private bool _isDisposed;

    public OpenAICompatibleVisionProvider()
        : this(
            new HttpClient { Timeout = RequestTimeout },
            ResolveBaseUrl(),
            ResolveModel(),
            ResolveApiKey(),
            ownsHttpClient: true)
    {
    }

    internal OpenAICompatibleVisionProvider(
        HttpClient httpClient,
        string baseUrl,
        string model,
        string? apiKey,
        bool ownsHttpClient = false)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        ArgumentException.ThrowIfNullOrWhiteSpace(baseUrl);
        ArgumentException.ThrowIfNullOrWhiteSpace(model);

        _baseUrl = baseUrl.TrimEnd('/');
        _model = model;
        _apiKey = apiKey;
        _ownsHttpClient = ownsHttpClient;
    }

    public string Description => $"OpenAI-compatible ({_model} at {_baseUrl})";

    internal string Endpoint => $"{_baseUrl}/chat/completions";

    public async Task<VisionResult> AnalyzeAsync(
        VisionRequest request,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        byte[] body = JsonSerializer.SerializeToUtf8Bytes(CreateRequest(request), JsonOptions);

        for (int attempt = 1; ; attempt++)
        {
            using HttpRequestMessage httpRequest = new(HttpMethod.Post, Endpoint);

            // Local servers typically need no credential at all, so an absent key is normal
            // rather than an error here.
            if (!string.IsNullOrWhiteSpace(_apiKey))
            {
                httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);
            }

            httpRequest.Content = new ByteArrayContent(body);
            httpRequest.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");

            using HttpResponseMessage response = await SendAsync(httpRequest, cancellationToken);
            string json = await response.Content.ReadAsStringAsync(cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                return ParseResponse(json, request.ImagePixelWidth, request.ImagePixelHeight);
            }

            bool transient = response.StatusCode is HttpStatusCode.TooManyRequests
                or HttpStatusCode.InternalServerError
                or HttpStatusCode.BadGateway
                or HttpStatusCode.ServiceUnavailable
                or HttpStatusCode.GatewayTimeout;

            if (!transient || attempt >= MaxAttempts)
            {
                throw new InvalidOperationException(GeminiApiErrorDiagnostics.FormatApiError(
                    response.StatusCode,
                    apiCode: null,
                    apiStatus: null,
                    apiMessage: json,
                    _apiKey,
                    attempt));
            }

            await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, attempt - 1)), cancellationToken);
        }
    }

    internal object CreateRequest(VisionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        // Images travel as a data URI in an image_url part, which is the shape every
        // compatible endpoint implements.
        string dataUri =
            $"data:{request.ImageMediaType};base64,{Convert.ToBase64String(request.EncodedImage.Span)}";

        return new
        {
            model = _model,
            messages = new object[]
            {
                new
                {
                    role = "user",
                    content = new object[]
                    {
                        new { type = "image_url", image_url = new { url = dataUri } },
                        new { type = "text", text = VisionPromptBuilder.Build(request) }
                    }
                }
            }
        };
    }

    internal static VisionResult ParseResponse(string json, int imageWidth, int imageHeight)
    {
        CompletionResponse? response;

        try
        {
            response = JsonSerializer.Deserialize<CompletionResponse>(json, JsonOptions);
        }
        catch (JsonException)
        {
            // The payload is kept rather than replaced by a generic sentence, for the same
            // reason the Gemini provider does: a local parsing defect must not read as a
            // provider fault.
            throw new InvalidOperationException(
                "CursorAI could not parse the chat-completions response. "
                + $"Received: {GeminiApiErrorDiagnostics.DescribePayload(json)}");
        }

        string? text = response?.Choices?.FirstOrDefault()?.Message?.Content;
        if (string.IsNullOrWhiteSpace(text))
        {
            throw new InvalidOperationException(
                "The endpoint returned no usable response text. "
                + $"Received: {GeminiApiErrorDiagnostics.DescribePayload(json)}");
        }

        VisionTargetPayload? payload = VisionTargetParser.TryParse(text, imageWidth, imageHeight);

        return new VisionResult(
            payload?.Answer ?? text.Trim(),
            response?.Model,
            response?.Usage is { } usage
                ? new VisionUsage(usage.TotalTokens, usage.PromptTokens, usage.CompletionTokens)
                : null,
            Targets: payload?.Targets);
    }

    private async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        try
        {
            return await _httpClient.SendAsync(
                request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        }
        catch (HttpRequestException exception)
        {
            throw new InvalidOperationException(
                $"Could not reach {Endpoint}. "
                + GeminiApiErrorDiagnostics.FormatTransportError(exception, _apiKey));
        }
    }

    /// <summary>
    /// Base URL of the endpoint, including any version segment, for example
    /// https://openrouter.ai/api/v1 or http://localhost:11434/v1.
    /// </summary>
    internal static string ResolveBaseUrl()
    {
        string? configured = AppConfiguration.Resolve(
            BaseUrlEnvironmentVariable, static _ => null);

        return string.IsNullOrWhiteSpace(configured)
            ? throw new InvalidOperationException(
                $"{BaseUrlEnvironmentVariable} is not set. The OpenAI-compatible provider needs "
                + "the endpoint's base URL, for example https://openrouter.ai/api/v1 or "
                + "http://localhost:11434/v1 for a local model server.")
            : configured;
    }

    internal static string ResolveModel()
    {
        string? configured = AppConfiguration.Resolve(ModelEnvironmentVariable, static _ => null);

        return string.IsNullOrWhiteSpace(configured)
            ? throw new InvalidOperationException(
                $"{ModelEnvironmentVariable} is not set. The OpenAI-compatible provider has no "
                + "sensible default model, because the available models depend entirely on which "
                + "endpoint is configured.")
            : configured;
    }

    internal static string? ResolveApiKey()
    {
        return AppConfiguration.Resolve(ApiKeyEnvironmentVariable, static _ => null);
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

    private sealed record CompletionResponse(
        [property: JsonPropertyName("model")] string? Model,
        [property: JsonPropertyName("choices")] IReadOnlyList<CompletionChoice>? Choices,
        [property: JsonPropertyName("usage")] CompletionUsage? Usage);

    private sealed record CompletionChoice(
        [property: JsonPropertyName("message")] CompletionMessage? Message);

    private sealed record CompletionMessage(
        [property: JsonPropertyName("content")] string? Content);

    private sealed record CompletionUsage(
        [property: JsonPropertyName("total_tokens")] int? TotalTokens,
        [property: JsonPropertyName("prompt_tokens")] int? PromptTokens,
        [property: JsonPropertyName("completion_tokens")] int? CompletionTokens);
}
