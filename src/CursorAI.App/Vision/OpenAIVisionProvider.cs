using CursorAI.App.Configuration;
using OpenAI.Responses;

namespace CursorAI.App.Vision;

#pragma warning disable OPENAI001 // The official SDK currently marks its Responses API surface as experimental.
public sealed class OpenAIVisionProvider : IVisionProvider
{
    /// <summary>
    /// Verified current on 2026-10-14 and documented as accepting text and image input. Note
    /// that many older OpenAI model identifiers stop working on 2026-10-23, so an inherited
    /// configuration may need revisiting.
    /// </summary>
    internal const string ModelEnvironmentVariable = "OPENAI_MODEL";

    private readonly object _clientGate = new();

    public string Description => $"OpenAI ({ResolveModel()}) via the Responses API";

    private const string ApiKeyEnvironmentVariable = "OPENAI_API_KEY";
    private const string DefaultModel = "gpt-5.4-mini";

    private ResponsesClient? _client;

    /// <summary>
    /// Nothing to release: the OpenAI SDK's ResponsesClient is not disposable. Present so the
    /// composition root can hold an IVisionProvider rather than a concrete type.
    /// </summary>
    public void Dispose()
    {
    }

    public async Task<VisionResult> AnalyzeAsync(
        VisionRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        ResponsesClient client = GetClient();
        string contextualPrompt = VisionPromptBuilder.Build(request);
        BinaryData imageData = BinaryData.FromBytes(request.EncodedImage, request.ImageMediaType);

        CreateResponseOptions options = new()
        {
            Model = ResolveModel()
        };
        options.InputItems.Add(ResponseItem.CreateUserMessageItem(
        [
            ResponseContentPart.CreateInputTextPart(contextualPrompt),
            ResponseContentPart.CreateInputImagePart(imageData)
        ]));

        ResponseResult response = await client.CreateResponseAsync(options, cancellationToken);
        string responseText = response.GetOutputText();
        if (string.IsNullOrWhiteSpace(responseText))
        {
            throw new InvalidOperationException("OpenAI returned no usable response text.");
        }

        // The guidance format is defined by VisionPromptBuilder rather than by a vendor, so the
        // same parser serves every provider.
        VisionTargetPayload? payload = VisionTargetParser.TryParse(
            responseText, request.ImagePixelWidth, request.ImagePixelHeight);

        return new VisionResult(
            payload?.Answer ?? responseText,
            ResolveModel(),
            Targets: payload?.Targets);
    }

    internal static string ResolveModel()
    {
        string? configured = AppConfiguration.Resolve(ModelEnvironmentVariable, static _ => null);
        return string.IsNullOrWhiteSpace(configured) ? DefaultModel : configured.Trim();
    }

    private ResponsesClient GetClient()
    {
        // Locked: a hotkey press and a tray click can both land here, and the previous lazy
        // initialisation would have built two clients.
        lock (_clientGate)
        {
            if (_client is not null)
            {
                return _client;
            }

        string? apiKey = AppConfiguration.Resolve(
            ApiKeyEnvironmentVariable,
            static settings => settings.OpenAiApiKey);

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new InvalidOperationException(
                "No OpenAI API key is configured. Open CursorAI settings to paste one, or set "
                + $"{ApiKeyEnvironmentVariable} before starting.");
        }

            _client = new ResponsesClient(apiKey);
            return _client;
        }
    }
}
#pragma warning restore OPENAI001
