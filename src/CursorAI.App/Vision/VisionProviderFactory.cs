namespace CursorAI.App.Vision;

/// <summary>
/// Chooses the vision provider at startup from the process environment, so switching is a
/// configuration change rather than a rebuild. This is explicit selection, never fallback:
/// exactly one provider is constructed, an unrecognised name is an error rather than a
/// silent default, and no provider is ever substituted for another at runtime.
/// </summary>
internal static class VisionProviderFactory
{
    internal const string ProviderEnvironmentVariable = "CURSORAI_VISION_PROVIDER";
    internal const string DefaultProviderName = "gemini";

    internal static IVisionProvider Create()
    {
        return Create(Environment.GetEnvironmentVariable(ProviderEnvironmentVariable));
    }

    internal static IVisionProvider Create(string? providerName)
    {
        string selected = string.IsNullOrWhiteSpace(providerName)
            ? DefaultProviderName
            : providerName.Trim().ToLowerInvariant();

        return selected switch
        {
            "gemini" => new GeminiVisionProvider(),
            "openai" => new OpenAIVisionProvider(),
            _ => throw new InvalidOperationException(
                $"{ProviderEnvironmentVariable} is set to '{providerName}', which is not a known "
                + "vision provider. Valid values are 'gemini' and 'openai'. Leave it unset to use "
                + $"'{DefaultProviderName}'.")
        };
    }
}
