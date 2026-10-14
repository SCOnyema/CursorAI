using System.Net;
using System.Net.Http;
using System.Text.Json;
using CursorAI.App.Capture;
using CursorAI.App.Vision;

namespace CursorAI.Tests;

public class OpenAICompatibleVisionProviderTests
{
    private const string BaseUrl = "http://localhost:11434/v1";
    private const string Model = "qwen2.5vl:7b";

    private static VisionRequest Request(bool requestTargets = true) => new(
        "What is under my cursor?",
        new byte[] { 0x89, 0x50, 0x4E, 0x47 },
        1920,
        1080,
        DateTimeOffset.UnixEpoch,
        new CapturedMonitorInfo(
            new PhysicalScreenBounds(0, 0, 1920, 1080),
            new PhysicalScreenBounds(0, 0, 1920, 1040),
            true),
        new PhysicalScreenPoint(400, 430),
        new PhysicalScreenPoint(400, 430),
        new ForegroundWindowInfo(1, "Editor", "editor"),
        requestTargets);

    private static OpenAICompatibleVisionProvider Create(StubHandler handler, string? key = "k") =>
        new(new HttpClient(handler), BaseUrl, Model, key);

    [Fact]
    public void Endpoint_IsChatCompletions_WhichIsWhatMakesItPortable()
    {
        // Third-party compatible endpoints implement chat-completions and almost none implement
        // OpenAI's Responses API, so this path is the reason one provider reaches many vendors.
        using OpenAICompatibleVisionProvider provider =
            new(new HttpClient(), BaseUrl, Model, apiKey: null);

        Assert.Equal("http://localhost:11434/v1/chat/completions", provider.Endpoint);
    }

    [Fact]
    public void Endpoint_ToleratesATrailingSlashInTheBaseUrl()
    {
        using OpenAICompatibleVisionProvider provider =
            new(new HttpClient(), BaseUrl + "/", Model, apiKey: null);

        Assert.Equal("http://localhost:11434/v1/chat/completions", provider.Endpoint);
    }

    [Fact]
    public void CreateRequest_SendsTheImageAsADataUriContentPart()
    {
        using OpenAICompatibleVisionProvider provider =
            new(new HttpClient(), BaseUrl, Model, apiKey: null);

        string json = JsonSerializer.Serialize(provider.CreateRequest(Request()));
        using JsonDocument document = JsonDocument.Parse(json);
        JsonElement content = document.RootElement
            .GetProperty("messages")[0]
            .GetProperty("content");

        Assert.Equal(Model, document.RootElement.GetProperty("model").GetString());
        Assert.Equal("image_url", content[0].GetProperty("type").GetString());
        Assert.StartsWith(
            "data:image/png;base64,",
            content[0].GetProperty("image_url").GetProperty("url").GetString());
        Assert.Equal("text", content[1].GetProperty("type").GetString());
        Assert.Contains("What is under my cursor?", content[1].GetProperty("text").GetString());
    }

    [Fact]
    public void ParseResponse_ReadsTextModelAndUsage()
    {
        const string json = """
            {
              "model": "qwen2.5vl:7b",
              "choices": [{ "message": { "content": "A toolbar." } }],
              "usage": { "total_tokens": 1200, "prompt_tokens": 1100, "completion_tokens": 100 }
            }
            """;

        VisionResult result = OpenAICompatibleVisionProvider.ParseResponse(json, 1920, 1080);

        Assert.Equal("A toolbar.", result.ResponseText);
        Assert.Equal("qwen2.5vl:7b", result.Model);
        Assert.Equal(1200, result.Usage!.TotalTokens);
        Assert.Equal(1100, result.Usage.InputTokens);
        Assert.Equal(100, result.Usage.OutputTokens);
    }

    [Fact]
    public void ParseResponse_ExtractsGuidanceTargetsUsingTheSharedParser()
    {
        // The guidance format comes from VisionPromptBuilder, not from a vendor, so every
        // provider parses it identically.
        const string json = """
            {
              "choices": [{ "message": { "content": "{\"answer\":\"The Export button.\",\"targets\":[{\"label\":\"Export PDF\",\"box_2d\":[369,155,429,262]}]}" } }]
            }
            """;

        VisionResult result = OpenAICompatibleVisionProvider.ParseResponse(json, 1920, 1080);

        Assert.Equal("The Export button.", result.ResponseText);
        VisionTarget target = Assert.Single(result.Targets!);
        Assert.Equal("Export PDF", target.Label);
        Assert.Equal(new PhysicalScreenBounds(298, 399, 205, 64), target.Bounds);
    }

    [Fact]
    public void ParseResponse_OnMalformedJson_KeepsThePayloadAsEvidence()
    {
        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
            () => OpenAICompatibleVisionProvider.ParseResponse("<html>502</html>", 1920, 1080));

        Assert.Contains("could not parse", exception.Message);
        Assert.Contains("502", exception.Message);
    }

    [Fact]
    public void ParseResponse_OnAnEmptyChoiceList_SaysSoWithThePayload()
    {
        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
            () => OpenAICompatibleVisionProvider.ParseResponse("""{"choices":[]}""", 1920, 1080));

        Assert.Contains("no usable response text", exception.Message);
    }

    [Fact]
    public async Task AnalyzeAsync_SendsABearerTokenWhenAKeyIsConfigured()
    {
        StubHandler handler = new((HttpStatusCode.OK, Ok));
        using HttpClient client = new(handler);
        using OpenAICompatibleVisionProvider provider = new(client, BaseUrl, Model, "secret-key");

        await provider.AnalyzeAsync(Request(), CancellationToken.None);

        Assert.Equal("Bearer", handler.LastAuthScheme);
        Assert.Equal("secret-key", handler.LastAuthParameter);
    }

    [Fact]
    public async Task AnalyzeAsync_SendsNoAuthorizationWhenThereIsNoKey()
    {
        // Local model servers normally need no credential, so an absent key is a supported
        // configuration rather than an error.
        StubHandler handler = new((HttpStatusCode.OK, Ok));
        using HttpClient client = new(handler);
        using OpenAICompatibleVisionProvider provider = new(client, BaseUrl, Model, apiKey: null);

        await provider.AnalyzeAsync(Request(), CancellationToken.None);

        Assert.Null(handler.LastAuthScheme);
    }

    [Fact]
    public async Task AnalyzeAsync_RetriesTransientFailuresThenSucceeds()
    {
        StubHandler handler = new(
            (HttpStatusCode.ServiceUnavailable, "busy"),
            (HttpStatusCode.OK, Ok));
        using HttpClient client = new(handler);
        using OpenAICompatibleVisionProvider provider = new(client, BaseUrl, Model, "k");

        VisionResult result = await provider.AnalyzeAsync(Request(), CancellationToken.None);

        Assert.Equal("A toolbar.", result.ResponseText);
        Assert.Equal(2, handler.CallCount);
    }

    [Fact]
    public async Task AnalyzeAsync_DoesNotRetryANonTransientFailure()
    {
        StubHandler handler = new((HttpStatusCode.BadRequest, "bad model"));
        using HttpClient client = new(handler);
        using OpenAICompatibleVisionProvider provider = new(client, BaseUrl, Model, "k");

        InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => provider.AnalyzeAsync(Request(), CancellationToken.None));

        Assert.Equal(1, handler.CallCount);
        Assert.Contains("bad model", exception.Message);
    }

    [Fact]
    public async Task AnalyzeAsync_RedactsTheKeyFromFailureMessages()
    {
        const string key = "sk-a-recognisable-secret";
        StubHandler handler = new((HttpStatusCode.Unauthorized, $"invalid key {key}"));
        using HttpClient client = new(handler);
        using OpenAICompatibleVisionProvider provider = new(client, BaseUrl, Model, key);

        InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => provider.AnalyzeAsync(Request(), CancellationToken.None));

        Assert.DoesNotContain(key, exception.Message);
    }

    [Fact]
    public void Constructor_RejectsAMissingBaseUrlOrModel()
    {
        // There is no sensible default for either: which models exist depends entirely on which
        // endpoint is configured.
        Assert.ThrowsAny<ArgumentException>(
            () => new OpenAICompatibleVisionProvider(new HttpClient(), "  ", Model, null));
        Assert.ThrowsAny<ArgumentException>(
            () => new OpenAICompatibleVisionProvider(new HttpClient(), BaseUrl, "  ", null));
    }

    private const string Ok = """
        {"model":"qwen2.5vl:7b","choices":[{"message":{"content":"A toolbar."}}]}
        """;

    private sealed class StubHandler(params (HttpStatusCode Status, string Body)[] responses)
        : HttpMessageHandler
    {
        internal int CallCount { get; private set; }

        internal string? LastAuthScheme { get; private set; }

        internal string? LastAuthParameter { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            LastAuthScheme = request.Headers.Authorization?.Scheme;
            LastAuthParameter = request.Headers.Authorization?.Parameter;

            (HttpStatusCode status, string body) = responses[Math.Min(CallCount, responses.Length - 1)];
            CallCount++;
            return Task.FromResult(new HttpResponseMessage(status)
            {
                Content = new StringContent(body)
            });
        }
    }
}
