using System.ClientModel;
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

    /// <summary>
    /// Matches the other providers. Guidance requests have measured 66s to 73s on Gemini, and
    /// the SDK's own default of 100s would cut those close with no reason to differ.
    /// </summary>
    internal static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(120);

    private readonly object _clientGate = new();

    public string Description => $"OpenAI ({ResolveModel()}) via the Responses API";

    private const string ApiKeyEnvironmentVariable = "OPENAI_API_KEY";
    private const string DefaultModel = "gpt-5.4-mini";

    private ResponsesClient? _client;
    private string? _clientApiKey;

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

        CreateResponseOptions options = BuildOptions(ResolveModel(), contextualPrompt, imageData);

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

    /// <summary>
    /// Separated so the request shape, and the retention flag in particular, can be asserted
    /// without a live endpoint.
    /// </summary>
    internal static CreateResponseOptions BuildOptions(
        string model,
        string contextualPrompt,
        BinaryData imageData)
    {
        CreateResponseOptions options = new()
        {
            Model = model,
            // The Responses API stores responses by default. The privacy disclosure says requests
            // are marked not-for-retention, which was true of Gemini only until this was set.
            StoredOutputEnabled = false
        };
        options.InputItems.Add(ResponseItem.CreateUserMessageItem(
        [
            ResponseContentPart.CreateInputTextPart(contextualPrompt),
            ResponseContentPart.CreateInputImagePart(imageData)
        ]));
        return options;
    }

    internal static string ResolveModel()
    {
        string? configured = AppConfiguration.Resolve(
            ModelEnvironmentVariable,
            static settings => settings.OpenAiModel);
        return string.IsNullOrWhiteSpace(configured) ? DefaultModel : configured.Trim();
    }

    private ResponsesClient GetClient()
    {
        // Locked: a hotkey press and a tray click can both land here, and the previous lazy
        // initialisation would have built two clients.
        lock (_clientGate)
        {
            string? apiKey = AppConfiguration.Resolve(
                ApiKeyEnvironmentVariable,
                static settings => settings.OpenAiApiKey);

            if (string.IsNullOrWhiteSpace(apiKey))
            {
                throw new InvalidOperationException(
                    "No OpenAI API key is configured. Open CursorAI settings to paste one, or set "
                    + $"{ApiKeyEnvironmentVariable} before starting.");
            }

            // Rebuilt when the key changes, so a key corrected in settings takes effect on the
            // next request rather than after a restart, as it already does for Gemini.
            if (_client is not null && _clientApiKey == apiKey)
            {
                return _client;
            }

            _client = new ResponsesClient(
                new ApiKeyCredential(apiKey),
                new ResponsesClientOptions { NetworkTimeout = RequestTimeout });
            _clientApiKey = apiKey;
            return _client;
        }
    }
}
#pragma warning restore OPENAI001
