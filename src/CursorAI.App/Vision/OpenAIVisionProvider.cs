using OpenAI.Responses;

namespace CursorAI.App.Vision;

#pragma warning disable OPENAI001 // The official SDK currently marks its Responses API surface as experimental.
public sealed class OpenAIVisionProvider : IVisionProvider
{
    public string Description => $"OpenAI ({Model}) via the Responses API";

    private const string ApiKeyEnvironmentVariable = "OPENAI_API_KEY";
    private const string Model = "gpt-5.4-mini";

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
            Model = Model
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

        return new VisionResult(responseText, Model);
    }

    private ResponsesClient GetClient()
    {
        if (_client is not null)
        {
            return _client;
        }

        string? apiKey = Environment.GetEnvironmentVariable(ApiKeyEnvironmentVariable);
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new InvalidOperationException(
                $"{ApiKeyEnvironmentVariable} is not configured. Set it before starting CursorAI to use vision analysis.");
        }

        _client = new ResponsesClient(apiKey);
        return _client;
    }
}
#pragma warning restore OPENAI001
